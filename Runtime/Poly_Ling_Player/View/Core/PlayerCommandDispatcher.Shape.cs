// PlayerCommandDispatcher.Shape.cs
// コマンドディスパッチャ：形（名前付きの頂点位置）の追加・削除・属性・切り替え・入れ替え・照会・点検。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 中身の決まりは Core/Data/ShapeData.cs、操作は Core/Ops/ShapeOps.cs。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// 形の名前を番号へ直す。空は基本の形（0）。見つからなければ false。
        /// </summary>
        private static bool TryResolveShapeName(ModelContext model, string name, out int id)
        {
            id = ShapeOps.BaseShapeId;
            if (string.IsNullOrEmpty(name)) return true;
            var def = model.FindShapeByName(name);
            if (def == null) return false;
            id = def.Id;
            return true;
        }

        /// <summary>
        /// 形の名前と重みの並びを、表情の子（GroupChildren）の並びにする。
        /// 名前が形に無い・数が合わないときは Fail して false。
        /// </summary>
        private bool TryBuildShapeChildren(ModelContext model, string[] shapes, float[] weights, out List<MorphGroupChild> children)
        {
            children = new List<MorphGroupChild>();
            shapes = shapes ?? System.Array.Empty<string>();
            weights = weights ?? System.Array.Empty<float>();
            if (weights.Length != shapes.Length) { Fail($"形の数（{shapes.Length}）と重みの数（{weights.Length}）が違います"); return false; }
            for (int i = 0; i < shapes.Length; i++)
            {
                var def = model.FindShapeByName(shapes[i]);
                if (def == null) { Fail($"形「{shapes[i]}」がありません"); return false; }
                children.Add(new MorphGroupChild(def.Name, weights[i]));
            }
            return true;
        }

        /// <summary>
        /// 今の形を切り替えたあとに呼ぶ（Undo・Redo の切り替えは除く）。
        /// 切り替えは試し表示・再生の足し込み（WorkingPositions）を捨てる（ShapeOps.SwitchModel。計画書 3.4）ので、
        /// 持ち主（表情の試し表示・形の試し表示）が自分の状態を終える。
        /// </summary>
        public System.Action OnShapeSwitched;

        /// <summary>今の形を切り替え、Undo に積み、表示を更新する。変わらなければ何もしない。</summary>
        private void SwitchShapeWithUndo(ProjectContext project, ModelContext model, int to, string description)
        {
            int from = model.CurrentShapeId;
            if (from == to) return;
            int prevBefore = model.PreviousShapeId;

            ShapeOps.SwitchModel(model, to);
            model.PreviousShapeId = from;
            // 形の試し表示（previewShapes）も足し込みが捨てられたので終える（計画書 3.4）。
            _meshListOps?.EndShapePreview();
            OnShapeSwitched?.Invoke();

            RecordMeshListUndo(new ShapeSwitchRecord
            {
                From = from, To = to, PreviousBefore = prevBefore, PreviousAfter = from,
            }, description);

            _viewportManager.EnterTopologyChanged(project);
        }

        /// <summary>形のコマンドなら処理して true を返す。</summary>
        private bool DispatchShape(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // 形の試し表示。基本の形への差分を足すので、始めるときに基本の形へ切り替える
                // （切り替えの記録が 1 件残る。PolyLing_モーフ整理計画.md 3.6・6 の 3）。
                // 重みを変えるたびに送られるので、始めたあとは通知しない（GPU への反映は MeshListOps.SyncPositionsOnly）。
                case PreviewShapesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.Shapes.Length == 0) { Fail("試し表示する形がありません"); return true; }
                    foreach (var n in c.Shapes)
                        if (ShapeBlendOps.FindShape(model, n) == null) { Fail($"形「{n}」がありません"); return true; }
                    if (model.CurrentShapeId != ShapeOps.BaseShapeId)
                    {
                        SwitchShapeWithUndo(project, model, ShapeOps.BaseShapeId, "形の切り替え: 基本の形（試し表示）");
                        _notifyPanels(ChangeKind.Attributes);   // 形のパネルの「今の形」と一覧の選択を合わせる
                    }
                    int n2 = GetMeshListOps(model).StartShapePreview(c.Shapes, c.Weights);
                    ReportData(CommandDataJson.New().Int("shapeCount", n2).Build());
                    return true;
                }

                // 終了時は法線抑止の解除と全 viewport の再計算が要るので DragEnd を通す。
                case EndShapePreviewCommand _:
                    if (model == null) { Fail("no current model"); return true; }
                    GetMeshListOps(model).EndShapePreview();
                    _viewportManager.EnterVerticesMoved(project, VerticesMovedPhase.DragEnd);
                    return true;

                // 形の位置で描画オブジェクトを複製し、新しいモデルにする（形・表情は持たない）。
                // 旧 convertMorphToMesh・expandMorphExpressionToModel の置き換え（3.12）。
                case ExtractShapeModelCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (!TryResolveShapeName(model, c.Name, out int shapeId)) { Fail($"形「{c.Name}」がありません"); return true; }
                    var def = model.FindShapeById(shapeId);
                    bool isUV = def?.IsUV == true;
                    int current = model.CurrentShapeId;
                    string label = def != null ? def.Name : "基本の形";
                    var newModel = new ModelContext { Name = $"{model.Name}_{label}" };
                    int count = 0;
                    foreach (var e in model.DrawableMeshes)
                    {
                        var src = e.Context;
                        var mo = src?.MeshObject;
                        if (mo == null || mo.VertexCount == 0) continue;
                        if (Poly_Ling.Modifiers.ModifierResultOps.IsFoldedResult(src)) continue;
                        var clone = mo.Clone();
                        for (int i = 0; i < clone.VertexCount; i++)
                        {
                            var sv = mo.Vertices[i];
                            var dv = clone.Vertices[i];
                            if (isUV)
                            {
                                if (ShapeOps.HasUV0(dv)) dv.UVs[0] = ShapeOps.GetShapeUV(sv, current, shapeId);
                                dv.Position = ShapeOps.GetBasePosition(sv, current);
                            }
                            else
                            {
                                dv.Position = ShapeOps.GetShapePosition(sv, current, shapeId);
                                if (ShapeOps.HasUV0(dv)) dv.UVs[0] = ShapeOps.GetBaseUV(sv, current);
                            }
                        }
                        ShapeOps.ClearShapes(clone);
                        clone.InvalidatePositionCache();
                        newModel.Add(new MeshContext { Name = src.Name, MeshObject = clone, IsVisible = true });
                        count++;
                    }
                    if (count == 0) { Fail("取り出せる描画オブジェクトがありません"); return true; }
                    project.AddModel(newModel);
                    _rebuildModelList?.Invoke();
                    ReportData(CommandDataJson.New()
                        .Int("newModelIndex", project.ModelCount - 1)
                        .Int("meshes", count)
                        .Text("modelName", newModel.Name)
                        .Build());
                    return true;
                }

                case AddShapeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (string.IsNullOrEmpty(c.Name)) { Fail("形の名前が空です"); return true; }
                    if (model.FindShapeByName(c.Name) != null) { Fail($"形「{c.Name}」は既にあります"); return true; }
                    if (!TryResolveShapeName(model, c.SourceShape, out int srcId))
                    { Fail($"写す元の形「{c.SourceShape}」がありません"); return true; }

                    var before = ShapeListRecord.CloneList(model.Shapes);
                    var def = new ShapeDef
                    {
                        Id = model.NextShapeId(),
                        Name = c.Name,
                        NameEnglish = c.NameEnglish,
                        Panel = System.Math.Clamp(c.Panel, 0, MorphPanels.Count - 1),
                        // 写す元の種類を引き継ぐ（UV の形の複製は UV の形。基本の形からは位置の形）。
                        Kind = model.FindShapeById(srcId)?.Kind ?? ShapeKind.Position,
                    };
                    model.Shapes.Add(def);
                    ShapeOps.CopyShape(model, srcId, def.Id);

                    RecordMeshListUndo(new ShapeListRecord
                    {
                        Before = before,
                        After = ShapeListRecord.CloneList(model.Shapes),
                        ShapeId = def.Id,
                        EntriesBefore = new List<ShapeEntryRef>(),
                        EntriesAfter = ShapeOps.CollectEntries(model, def.Id),
                        UVEntriesAfter = ShapeOps.CollectUVEntries(model, def.Id),
                        PreviousBefore = model.PreviousShapeId,
                        PreviousAfter = model.PreviousShapeId,
                    }, $"形の追加: {def.Name}");
                    model.IsDirty = true;

                    if (c.MakeCurrent)
                        SwitchShapeWithUndo(project, model, def.Id, $"形の切り替え: {def.Name}");

                    _notifyPanels(ChangeKind.Attributes);
                    ReportData(CommandDataJson.New()
                        .Int("shapeId", def.Id)
                        .Int("movedVertices", ShapeOps.CountMovedVertices(model, def.Id))
                        .Build());
                    return true;
                }

                case DeleteShapeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var def = model.FindShapeByName(c.Name);
                    if (def == null) { Fail($"形「{c.Name}」がありません"); return true; }
                    int id = def.Id;

                    // 今の形は Position にあるので、先に基本の形へ戻して控えへ移す。
                    if (model.CurrentShapeId == id)
                        SwitchShapeWithUndo(project, model, ShapeOps.BaseShapeId, "形の切り替え: 基本の形");

                    var before = ShapeListRecord.CloneList(model.Shapes);
                    var entriesBefore = ShapeOps.CollectEntries(model, id);
                    var uvEntriesBefore = ShapeOps.CollectUVEntries(model, id);
                    int prevBefore = model.PreviousShapeId;

                    model.Shapes.Remove(def);
                    ShapeOps.RemoveEntries(model, id);
                    if (model.PreviousShapeId == id) model.PreviousShapeId = ShapeOps.BaseShapeId;

                    RecordMeshListUndo(new ShapeListRecord
                    {
                        Before = before,
                        After = ShapeListRecord.CloneList(model.Shapes),
                        ShapeId = id,
                        EntriesBefore = entriesBefore,
                        EntriesAfter = new List<ShapeEntryRef>(),
                        UVEntriesBefore = uvEntriesBefore,
                        PreviousBefore = prevBefore,
                        PreviousAfter = model.PreviousShapeId,
                    }, $"形の削除: {c.Name}");
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case SetShapeAttributesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var def = model.FindShapeByName(c.Name);
                    if (def == null) { Fail($"形「{c.Name}」がありません"); return true; }
                    if (string.IsNullOrEmpty(c.NewName)) { Fail("新しい名前が空です"); return true; }
                    var other = model.FindShapeByName(c.NewName);
                    if (other != null && !ReferenceEquals(other, def)) { Fail($"形「{c.NewName}」は既にあります"); return true; }

                    int panel = System.Math.Clamp(c.Panel, 0, MorphPanels.Count - 1);
                    if (def.Name == c.NewName && def.NameEnglish == c.NameEnglish && def.Panel == panel) return true;

                    var before = ShapeListRecord.CloneList(model.Shapes);
                    def.Name = c.NewName; def.NameEnglish = c.NameEnglish; def.Panel = panel;

                    RecordMeshListUndo(new ShapeListRecord
                    {
                        Before = before,
                        After = ShapeListRecord.CloneList(model.Shapes),
                        ShapeId = ShapeOps.BaseShapeId,
                        PreviousBefore = model.PreviousShapeId,
                        PreviousAfter = model.PreviousShapeId,
                    }, $"形の属性変更: {def.Name}");
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case SetCurrentShapeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (!TryResolveShapeName(model, c.Name, out int to))
                    { Fail($"形「{c.Name}」がありません"); return true; }
                    if (model.CurrentShapeId == to) return true;

                    SwitchShapeWithUndo(project, model, to,
                        $"形の切り替え: {(string.IsNullOrEmpty(c.Name) ? "基本の形" : c.Name)}");
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case SwapShapeCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    int to = model.PreviousShapeId;
                    if (to != ShapeOps.BaseShapeId && model.FindShapeById(to) == null) to = ShapeOps.BaseShapeId;
                    if (model.CurrentShapeId == to) return true;

                    var toDef = model.FindShapeById(to);
                    SwitchShapeWithUndo(project, model, to, $"形の入れ替え: {(toDef != null ? toDef.Name : "基本の形")}");
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case MirrorShapeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (string.IsNullOrEmpty(c.Name)) { Fail("基本の形は左右反転できません（反転する形の名前が空です）"); return true; }
                    var srcDef = model.FindShapeByName(c.Name);
                    if (srcDef == null) { Fail($"形「{c.Name}」がありません"); return true; }
                    if (srcDef.IsUV) { Fail($"形「{c.Name}」は UV の形なので左右反転できません（左右反転は頂点位置の形だけ）"); return true; }
                    if (string.IsNullOrEmpty(c.NewName)) { Fail("作る形の名前が空です"); return true; }
                    if (model.FindShapeByName(c.NewName) != null) { Fail($"形「{c.NewName}」は既にあります"); return true; }

                    var before = ShapeListRecord.CloneList(model.Shapes);
                    int panel = c.Panel < 0
                        ? srcDef.Panel
                        : System.Math.Clamp(c.Panel, 0, MorphPanels.Count - 1);
                    var def = new ShapeDef
                    {
                        Id = model.NextShapeId(),
                        Name = c.NewName,
                        NameEnglish = c.NameEnglish,
                        Panel = panel,
                    };
                    model.Shapes.Add(def);
                    ShapeOps.MirrorShape(model, srcDef.Id, def.Id, out int unmatched);

                    RecordMeshListUndo(new ShapeListRecord
                    {
                        Before = before,
                        After = ShapeListRecord.CloneList(model.Shapes),
                        ShapeId = def.Id,
                        EntriesBefore = new List<ShapeEntryRef>(),
                        EntriesAfter = ShapeOps.CollectEntries(model, def.Id),
                        PreviousBefore = model.PreviousShapeId,
                        PreviousAfter = model.PreviousShapeId,
                    }, $"形の左右反転: {srcDef.Name} → {def.Name}");
                    model.IsDirty = true;

                    _notifyPanels(ChangeKind.Attributes);
                    ReportData(CommandDataJson.New()
                        .Int("shapeId", def.Id)
                        .Int("movedVertices", ShapeOps.CountMovedVertices(model, def.Id))
                        .Int("unmatchedVertices", unmatched)
                        .Build());
                    return true;
                }

                case AddExpressionCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (string.IsNullOrEmpty(c.Name)) { Fail("表情の名前が空です"); return true; }
                    if (!TryBuildShapeChildren(model, c.Shapes, c.Weights, out var children)) return true;

                    var expr = new MorphExpression(c.Name, MorphType.Group)
                    {
                        NameEnglish = c.NameEnglish,
                        Panel = System.Math.Clamp(c.Panel, 0, MorphPanels.Count - 1),
                    };
                    expr.GroupChildren.AddRange(children);
                    int index = model.MorphExpressions.Count;
                    model.MorphExpressions.Add(expr);
                    RecordMeshListUndo(new MorphExpressionChangeRecord { AddExpression = expr.Clone(), AddedIndex = index },
                        $"表情の作成: {expr.Name}");
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    ReportData(CommandDataJson.New().Int("setIndex", index).Build());
                    return true;
                }

                case SetExpressionShapesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.SetIndex < 0 || c.SetIndex >= model.MorphExpressionCount) { Fail($"表情 {c.SetIndex} がありません"); return true; }
                    var set = model.MorphExpressions[c.SetIndex];
                    if (set.Type != MorphType.Group)
                    { Fail($"表情「{set.Name}」はグループではありません（頂点の表情は形そのもの。形を組み合わせるには addExpression でグループを作る）"); return true; }
                    if (!TryBuildShapeChildren(model, c.Shapes, c.Weights, out var children)) return true;

                    // 形でない子（旧モーフの子の名前）は元の並びで残し、形の子だけを置き換える。
                    var next = new List<MorphGroupChild>();
                    foreach (var ch in set.GroupChildren)
                        if (model.FindShapeByName(ch.Name) == null) next.Add(ch);
                    next.AddRange(children);

                    bool same = next.Count == set.GroupChildren.Count;
                    for (int i = 0; same && i < next.Count; i++)
                        same = next[i].Name == set.GroupChildren[i].Name && next[i].Weight == set.GroupChildren[i].Weight;
                    if (same) return true;

                    var before = set.Clone();
                    set.GroupChildren = next;
                    RecordMeshListUndo(new MorphExpressionEditRecord { SetIndex = c.SetIndex, OldSnapshot = before, NewSnapshot = set.Clone() },
                        $"表情の形の変更: {set.Name}");
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case QueryShapesCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var names = new List<string>();
                    var namesEn = new List<string>();
                    var ids = new List<int>();
                    var panels = new List<int>();
                    var moved = new List<int>();
                    var kinds = new List<string>();
                    if (model.Shapes != null)
                    {
                        foreach (var s in model.Shapes)
                        {
                            if (s == null) continue;
                            kinds.Add(s.IsUV ? "uv" : "position");
                            names.Add(s.Name ?? "");
                            namesEn.Add(s.NameEnglish ?? "");
                            ids.Add(s.Id);
                            panels.Add(s.Panel);
                            moved.Add(ShapeOps.CountMovedVertices(model, s.Id));
                        }
                    }
                    ReportData(CommandDataJson.New()
                        .Text("current",  model.FindShapeById(model.CurrentShapeId)?.Name ?? "")
                        .Text("previous", model.FindShapeById(model.PreviousShapeId)?.Name ?? "")
                        .Int("count", names.Count)
                        .Texts("names", names)
                        .Texts("namesEnglish", namesEn)
                        .Ints("ids", ids)
                        .Ints("panels", panels)
                        .Ints("movedVertices", moved)
                        .Texts("kinds", kinds)
                        .Build());
                    return true;
                }

                case QueryDisplayedPositionsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var with = new List<int>();
                    for (int i = 0; i < model.MeshContextCount; i++)
                        if (model.GetMeshContext(i)?.WorkingPositions != null) with.Add(i);
                    var mc = model.GetMeshContext(c.MasterIndex);
                    var mo = mc?.MeshObject;
                    if (mo == null) { Fail($"オブジェクト {c.MasterIndex} がありません"); return true; }
                    var wp = mc.WorkingPositions;
                    var pos = new List<float>();
                    var off = new List<float>();
                    foreach (int vi in c.VertexIndices)
                    {
                        if (vi < 0 || vi >= mo.VertexCount) { Fail($"頂点 {vi} がありません"); return true; }
                        Vector3 o = wp != null && vi < wp.Length ? wp[vi] : Vector3.zero;
                        Vector3 p = mo.Vertices[vi].Position + o;
                        pos.Add(p.x); pos.Add(p.y); pos.Add(p.z);
                        off.Add(o.x); off.Add(o.y); off.Add(o.z);
                    }
                    ReportData(CommandDataJson.New()
                        .Ints("meshesWithWorking", with)
                        .Int("hasWorking", wp != null ? 1 : 0)
                        .Nums("positions", pos)
                        .Nums("offsets", off)
                        .Build());
                    return true;
                }

                case QueryShapeAuditCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var a = ShapeOps.Audit(model);
                    ReportData(CommandDataJson.New()
                        .Int("issues",           a.Issues)
                        .Int("currentMissing",   a.CurrentMissing)
                        .Int("badIds",           a.BadIds)
                        .Int("badNames",         a.BadNames)
                        .Int("unknownEntries",   a.UnknownEntries)
                        .Int("forbiddenEntries", a.ForbiddenEntries)
                        .Int("duplicateEntries", a.DuplicateEntries)
                        .Int("redundantEntries", a.RedundantEntries)
                        .Int("mirrorMismatch",   a.MirrorMismatch)
                        .Int("modifierOutputEntries", a.ModifierOutputEntries)
                        .Build());
                    return true;
                }

                default: return false;
            }
        }
    }
}
