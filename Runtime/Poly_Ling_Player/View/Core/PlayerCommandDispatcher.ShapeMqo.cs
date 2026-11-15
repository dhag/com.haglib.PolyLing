// PlayerCommandDispatcher.ShapeMqo.cs
// コマンドディスパッチャ：形と MQO の連携（形へ読み込む・再読み込み・MQO へ書き出す）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 決まりは MQO/ShapeMqoOps.cs の冒頭注記。Undo は ShapeStateRecord 1 件
// （形を作る・切り替える・読むを丸ごと前後で控える）。

using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.MQO;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// 形と MQO の連携のコマンドの直近の結果の一文（形のパネルが状態の欄へ出す。
        /// 決まらない・頂点数が合わないオブジェクトの名前もここに入る）。
        /// </summary>
        public string LastShapeMqoSummary { get; private set; } = "";

        private void FailMqo(string msg)
        {
            LastShapeMqoSummary = "失敗: " + msg;
            Fail(msg);
        }

        /// <summary>形と MQO の連携のコマンドなら処理して true を返す。</summary>
        private bool DispatchShapeMqo(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            if (cmd is ImportShapeMqoCommand || cmd is ReloadShapeMqoCommand || cmd is ExportShapeMqoCommand)
                LastShapeMqoSummary = "";
            switch (cmd)
            {
                case ImportShapeMqoCommand c:
                {
                    if (model == null) { FailMqo("no current model"); return true; }
                    if (string.IsNullOrEmpty(c.MqoPath)) { FailMqo("mqoPath が空です"); return true; }
                    if (!string.IsNullOrEmpty(c.NewShape) && model.FindShapeByName(c.NewShape) != null)
                    { FailMqo($"形「{c.NewShape}」は既にあります"); return true; }
                    if (c.PairModels.Length != c.PairMqos.Length)
                    { FailMqo($"pairModels が {c.PairModels.Length} 件、pairMqos が {c.PairMqos.Length} 件で長さが合いません"); return true; }
                    if (c.Scale == 0f) { FailMqo("scale が 0 です"); return true; }
                    if (string.IsNullOrEmpty(c.NewShape) && model.FindShapeById(model.CurrentShapeId)?.IsUV == true)
                    { FailMqo($"今の形「{model.FindShapeById(model.CurrentShapeId).Name}」は UV の形なので、MQO の頂点位置は読めません"); return true; }

                    if (!Poly_Ling.Core.PLSandbox.TryResolveRead(c.MqoPath, out string full, out string reason))
                    { FailMqo(reason); return true; }
                    if (!ShapeMqoOps.TryLoad(model, full, true, out var helper, out reason))
                    { FailMqo(reason); return true; }

                    // ① の組：渡されたもの。無ければ、今の形（作るなら新しい形＝無し）が同じファイルを読んでいたときの組。
                    List<ShapeSourcePair> remembered = null;
                    if (c.PairModels.Length > 0)
                    {
                        remembered = new List<ShapeSourcePair>();
                        for (int i = 0; i < c.PairModels.Length; i++)
                            remembered.Add(new ShapeSourcePair(c.PairModels[i], c.PairMqos[i]));
                    }
                    else if (string.IsNullOrEmpty(c.NewShape))
                    {
                        var cur = model.GetShapeSource(model.CurrentShapeId);
                        if (cur != null && cur.Path == c.MqoPath) remembered = cur.Pairs;
                    }

                    var match = ShapeMqoOps.Match(helper, remembered);
                    if (match.Count == 0)
                    { FailMatch("組めるオブジェクトがありません", match); return true; }

                    var before = ShapeModelState.Capture(model);

                    if (!string.IsNullOrEmpty(c.NewShape))
                    {
                        var def = new ShapeDef { Id = model.NextShapeId(), Name = c.NewShape, NameEnglish = "", Panel = 3 };
                        model.Shapes.Add(def);
                        int from = model.CurrentShapeId;
                        ShapeOps.SwitchModel(model, def.Id);
                        model.PreviousShapeId = from;
                        OnShapeSwitched?.Invoke();
                    }

                    var src = new ShapeSource
                    {
                        Path = c.MqoPath, Scale = c.Scale, FlipX = c.FlipX, FlipZ = c.FlipZ, Pairs = match.ToPairs(),
                    };
                    FinishShapeImport(project, model, match, src, c.KeepShapeOffsets, before, "MQO から読み込み", null);
                    return true;
                }

                case ReloadShapeMqoCommand c:
                {
                    if (model == null) { FailMqo("no current model"); return true; }
                    if (!TryResolveShapeName(model, c.Name, out int id))
                    { FailMqo($"形「{c.Name}」がありません"); return true; }
                    if (model.FindShapeById(id)?.IsUV == true)
                    { FailMqo($"形「{c.Name}」は UV の形なので、MQO の頂点位置は読めません"); return true; }
                    var src = model.GetShapeSource(id);
                    if (src == null || string.IsNullOrEmpty(src.Path))
                    { FailMqo($"{ShapeLabel(model, id)}には読み込み元がありません（先に importShapeMqo で読む）"); return true; }

                    if (!Poly_Ling.Core.PLSandbox.TryResolveRead(src.Path, out string full, out string reason))
                    { FailMqo(reason); return true; }
                    if (!ShapeMqoOps.TryLoad(model, full, true, out var helper, out reason))
                    { FailMqo(reason); return true; }

                    var match = ShapeMqoOps.Match(helper, src.Pairs);
                    if (match.Count == 0)
                    { FailMatch("組めるオブジェクトがありません", match); return true; }

                    var before = ShapeModelState.Capture(model);
                    if (model.CurrentShapeId != id)
                    {
                        int from = model.CurrentShapeId;
                        ShapeOps.SwitchModel(model, id);
                        model.PreviousShapeId = from;
                        OnShapeSwitched?.Invoke();
                    }

                    var newSrc = src.Clone();
                    newSrc.Pairs = match.ToPairs();
                    FinishShapeImport(project, model, match, newSrc, c.KeepShapeOffsets, before, "MQO の再読み込み", src.Path);
                    return true;
                }

                case ExportShapeMqoCommand c:
                {
                    if (model == null) { FailMqo("no current model"); return true; }
                    if (string.IsNullOrEmpty(c.FilePath)) { FailMqo("filePath が空です"); return true; }

                    int id = model.CurrentShapeId;
                    var src = c.UseSource ? model.GetShapeSource(id) : null;
                    string refPath = !string.IsNullOrEmpty(c.ReferencePath) ? c.ReferencePath : (src?.Path ?? "");
                    if (string.IsNullOrEmpty(refPath))
                    { FailMqo($"{ShapeLabel(model, id)}には読み込み元が無いので、referencePath（元にする MQO）を渡すこと"); return true; }
                    float scale = src != null ? src.Scale : c.Scale;
                    bool  flipX = src != null ? src.FlipX : c.FlipX;
                    bool  flipZ = src != null ? src.FlipZ : c.FlipZ;
                    if (scale == 0f) { FailMqo("scale が 0 です"); return true; }

                    if (!Poly_Ling.Core.PLSandbox.TryResolveRead(refPath, out string refFull, out string reason))
                    { FailMqo(reason); return true; }
                    if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(c.FilePath, out string outFull, out reason))
                    { FailMqo(reason); return true; }
                    if (!ShapeMqoOps.TryLoad(model, refFull, false, out var helper, out reason))
                    { FailMqo(reason); return true; }

                    var match = ShapeMqoOps.Match(helper, src?.Pairs);
                    if (match.Count == 0)
                    { FailMatch("組めるオブジェクトがありません", match); return true; }

                    int transferred = ShapeMqoOps.Export(helper, match, scale, flipX, flipZ);
                    var wr = MQODocumentIO.WriteDocumentToFile(helper.MQODocument, outFull);
                    if (wr == null || !wr.Success) { FailMqo($"書き出せません: {wr?.ErrorMessage}"); return true; }

                    var mNames = new List<string>(); var qNames = new List<string>();
                    for (int i = 0; i < match.Count; i++) { mNames.Add(match.Models[i].Name ?? ""); qNames.Add(match.Mqos[i].Name ?? ""); }
                    LastShapeMqoSummary = Summary($"書き出し: {System.IO.Path.GetFileName(outFull)}", match, transferred);
                    ReportData(CommandDataJson.New()
                        .Text("shape", model.FindShapeById(id)?.Name ?? "")
                        .Text("path", outFull)
                        .Text("reference", refFull)
                        .Int("pairs", match.Count)
                        .Int("transferred", transferred)
                        .Texts("modelNames", mNames)
                        .Texts("mqoNames", qNames)
                        .Texts("unresolved", match.Unresolved)
                        .Texts("mismatched", match.Mismatched)
                        .Build());
                    return true;
                }

                default: return false;
            }
        }

        /// <summary>形の表示名（エラー文用）。</summary>
        private static string ShapeLabel(ModelContext model, int id)
            => id == ShapeOps.BaseShapeId ? "基本の形" : $"形「{model.FindShapeById(id)?.Name}」";

        /// <summary>組めなかったときの失敗。決まらない・頂点数が合わないものの名前を添える。</summary>
        private void FailMatch(string head, ShapeMqoMatch match)
        {
            string msg = head;
            if (match.Unresolved.Count > 0) msg += $"。決まらない: {string.Join(", ", match.Unresolved)}";
            if (match.Mismatched.Count > 0) msg += $"。頂点数が合わない: {string.Join(", ", match.Mismatched)}";
            FailMqo(msg);
        }

        /// <summary>パネルへ出す結果の一文。</summary>
        private static string Summary(string head, ShapeMqoMatch match, int transferred)
        {
            string msg = $"{head}（{match.Count} 組・{transferred} 頂点: {string.Join(", ", match.ToPairs().ConvertAll(p => p.Model == p.Mqo ? p.Model : p.Model + "←" + p.Mqo))}）";
            if (match.Unresolved.Count > 0) msg += $"。決まらず読まない: {string.Join(", ", match.Unresolved)}";
            if (match.Mismatched.Count > 0) msg += $"。頂点数が合わず読まない: {string.Join(", ", match.Mismatched)}";
            return msg;
        }

        /// <summary>
        /// 今の形へ読み、読み込み元を覚え、Undo を 1 件積み、結果を返す。
        /// before は形を作る・切り替える前に控えた状態。
        /// </summary>
        private void FinishShapeImport(ProjectContext project, ModelContext model, ShapeMqoMatch match, ShapeSource src,
                                       bool keepShapeOffsets, ShapeModelState before, string what, string reportPath)
        {
            int id = model.CurrentShapeId;
            int transferred = ShapeMqoOps.Import(model, match, src, keepShapeOffsets, out int shifted, out int dropped);

            if (id == ShapeOps.BaseShapeId) model.BaseShapeSource = src;
            else { var def = model.FindShapeById(id); if (def != null) def.Source = src; }
            model.IsDirty = true;

            // 表示の更新でミラー側の同期が走るので、その後の状態を控える。
            _viewportManager.EnterTopologyChanged(project);

            var after = ShapeModelState.Capture(model);
            RecordMeshListUndo(new ShapeStateRecord { Before = before, After = after },
                $"形の{what}: {(id == ShapeOps.BaseShapeId ? "基本の形" : model.FindShapeById(id)?.Name)}");

            LastShapeMqoSummary = Summary($"{what}: {(id == ShapeOps.BaseShapeId ? "基本の形" : model.FindShapeById(id)?.Name)}", match, transferred);
            _notifyPanels(ChangeKind.Attributes);

            var mNames = new List<string>(); var qNames = new List<string>();
            for (int i = 0; i < match.Count; i++) { mNames.Add(match.Models[i].Name ?? ""); qNames.Add(match.Mqos[i].Name ?? ""); }
            var data = CommandDataJson.New()
                .Text("shape", model.FindShapeById(id)?.Name ?? "");
            if (reportPath != null) data = data.Text("path", reportPath);
            ReportData(data
                .Int("pairs", match.Count)
                .Int("transferred", transferred)
                .Texts("modelNames", mNames)
                .Texts("mqoNames", qNames)
                .Texts("matchedBy", match.How)
                .Texts("unresolved", match.Unresolved)
                .Texts("mismatched", match.Mismatched)
                .Int("movedVertices", ShapeOps.CountMovedVertices(model, id))
                .Int("shiftedEntries", shifted)
                .Int("droppedEntries", dropped)
                .Build());
        }
    }
}
