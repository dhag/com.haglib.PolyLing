// PlayerCommandDispatcher.ModifierChain.cs
// コマンドディスパッチャ：処理列（ModifierStack.IsChain）の共通コマンド 6 本と照会 2 つ。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.5・段 4
//
// 【処理列の持ち方】（段 3）
//   元メッシュは各項目の MeshRefIds[ModifierChainHandler.SourceKey]、結果は各項目の OutputObjectIds。
//   全項目でそろえる。参照オブジェクト（種類の PLParam(IsMeshRef) の引数）は、引数の索引を
//   ObjectId に引き直して MeshRefIds[キー] に持つ（処理器が読む）。Args の索引は書いた時点の値で、
//   照会と引数の変更では MeshRefIds から今の索引へ引き直して使う。
//
// 【作り方】最初の追加（stackName を省いた addMeshModifier）で作る。結果は元メッシュの子として
//   サブディビジョンの子と同じに置く（PlayerCommandDispatcher.Subdivision.cs）。
//   元メッシュの面の塗りを止めるのは処理器（ModifierChainHandler）。
//
// 【最後の項目の削除】処理列ごと外し、結果の描画オブジェクトを消す（元メッシュだけが残る）。
//
// 【Undo】書き換えは SuspendRecording の中で行い、評価（FinishGroupEdit）の後に
//   オブジェクト一覧のスナップショットとスタックの変更を 1 件にまとめて積む（RecordSubdivisionUndo）。
//   検査はすべて書く前に済ませ、失敗したらモデルを変えない。
//   評価で失敗した項目はコマンドの失敗にせず、状態として照会に出す（計画書 3.4）。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Modifiers;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// DispatchCore の分担：処理列。該当するコマンドなら処理して true を返す。
        /// </summary>
        private bool DispatchModifierChain(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case QueryMeshModifierKindsCommand _:
                {
                    MarkNotRecorded();
                    ReportData(BuildMeshModifierKindsData());
                    return true;
                }

                case QueryMeshModifierChainCommand c:
                {
                    MarkNotRecorded();
                    var qm = project?.GetModel(c.ModelIndex);
                    if (qm == null) { Fail($"no model at index {c.ModelIndex}"); return true; }
                    string data = BuildMeshModifierChainData(qm, c.StackName, out string err);
                    if (data == null) { Fail(err); return true; }
                    ReportData(data);
                    return true;
                }

                case AddMeshModifierCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteAddMeshModifier(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case SetMeshModifierArgsCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteSetMeshModifierArgs(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case SetMeshModifierVertexGroupCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteSetMeshModifierVertexGroup(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case SetMeshModifierMirrorAttributeCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteSetMeshModifierMirrorAttribute(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case ShowModifierGizmoCommand c:
                {
                    if (OnShowModifierGizmo == null) { Fail("modifier gizmo handler not wired"); return true; }
                    string reason = OnShowModifierGizmo.Invoke(c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case ApplyMeshModifierCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteApplyMeshModifier(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case DuplicateMeshModifierCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteDuplicateMeshModifier(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case CopyMeshModifierToObjectsCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteCopyMeshModifierToObjects(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                // 「ここまで表示」。保存しない表示設定なので Undo に積まない（元の線の表示と同じ）。
                case SetModifierDisplayUpToCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    if (!TryFindChain(model, c.StackName, out var dg, out string dErr)) { Fail(dErr); return true; }
                    if (!string.IsNullOrEmpty(c.ItemId) && dg.FindStep(c.ItemId) == null)
                    { Fail($"処理列 {dg.Name} に項目 {c.ItemId} がありません"); return true; }
                    MarkNotRecorded();
                    dg.DisplayUpToItemId = c.ItemId ?? "";
                    FinishGroupEdit(project, model);
                    ReportData(CommandDataJson.New().Text("state", ChainStateText(model, dg)).Build());
                    return true;
                }

                case MoveMeshModifierCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteMoveMeshModifier(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case SetMeshModifierEnabledCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteSetMeshModifierEnabled(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case RemoveMeshModifierCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteRemoveMeshModifier(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case ApplyMeshModifierChainCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string reason = ExecuteApplyMeshModifierChain(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                // 元の線の表示（Blender の Show in Edit Mode に当たる表示の切り替え）。
                // 保存しない表示設定なので Undo に積まない（ビルボード表示と同じ）。
                case SetModifierCageVisibleCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    var p = Poly_Ling.Modifiers.ModifierResultOps.PairOf(model, model.FindModifierStackByName(c.StackName));
                    if (p == null) { Fail($"モディファイアのスタックではありません: {c.StackName}"); return true; }
                    p.Source.HideCageLines = !c.Visible;
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // 追加
        // ================================================================

        private string ExecuteAddMeshModifier(ProjectContext project, ModelContext model, AddMeshModifierCommand c)
        {
            if (!TryPairArgs(c.ArgKeys, c.ArgValues, out var args, out string pErr)) return pErr;

            ModifierStack g = null;
            MeshContext source, result = null;
            if (!string.IsNullOrEmpty(c.StackName))
            {
                if (!TryFindChain(model, c.StackName, out g, out string cErr)) return cErr;
                ChainEnds(model, g, out source, out result);
                if (source?.MeshObject == null) return $"処理列 {g.Name} の元メッシュが見つかりません";
                if (result?.MeshObject == null) return $"処理列 {g.Name} の結果が見つかりません";
            }
            else
            {
                source = model.GetMeshContext(c.SourceMasterIndex);
                if (source?.MeshObject == null) return $"元メッシュが見つかりません (sourceMasterIndex={c.SourceMasterIndex})";
                if (source.Type != MeshType.Mesh) return "元メッシュは通常のメッシュでなければなりません";
                if (source.IsSkinned) return "スキンド化したメッシュは元メッシュにできません";
                string end = ChainEndOf(model, source);
                if (end != null) return $"{source.Name} は処理列 {end} の元メッシュか結果です。stackName={end} で足してください";
            }

            // 追加メニューの既定値。渡した引数が優先。足す位置の入力は今の結果（無ければ元メッシュ）。
            if (c.UseAddDefaults)
                foreach (var kv in MeshModifierRegistry.AddDefaultArgs(c.Kind, result?.MeshObject ?? source.MeshObject))
                    if (!args.ContainsKey(kv.Key)) args[kv.Key] = kv.Value;

            if (!TryNormalizeModifierArgs(model, c.Kind, args, source, result,
                    out var normalized, out var refs, out string nErr))
                return nErr;

            // 末尾固定の種類（段 8）は処理列に 1 つだけ。
            var kindInfo = MeshModifierRegistry.Find(c.Kind);
            int pinnedAt = g != null ? PinnedIndex(g) : -1;
            if (kindInfo != null && kindInfo.PinnedLast && pinnedAt >= 0)
                return $"処理列 {g.Name} には既に {DisplayNameOf(g.Steps[pinnedAt].Action)} があります（末尾固定の種類は 1 つだけ置けます）";

            _undoController?.SetModelContext(model);
            var listBefore   = MeshFilterToSkinnedRecord.CaptureList(model);
            int groupsBefore = model.ModifierStackCount;
            var changes      = new List<ModifierStackChangeRecord>();
            bool created     = false;
            ModifierStackStep step;

            _undoController?.SuspendRecording();
            try
            {
                if (g == null)
                {
                    result = CreateChainResult(model, source);
                    g = new ModifierStack(model.GenerateUniqueModifierStackName(source.Name + "_chain")) { IsChain = true };
                    g.Steps.Clear();
                    step = g.AddStep(BuildChainStep(c.Kind, normalized, refs, source, result));
                    model.AddModifierStack(g);
                    created = true;
                }
                else
                {
                    int gi = model.ModifierStacks.IndexOf(g);
                    var before = g.Clone();
                    step = g.AddStep(BuildChainStep(c.Kind, normalized, refs, source, result));
                    // 普通の種類は末尾固定の項目の前へ入れる（段 8）。
                    if (pinnedAt >= 0)
                    {
                        g.Steps.Remove(step);
                        g.Steps.Insert(pinnedAt, step);
                    }
                    changes.Add(new ModifierStackChangeRecord { ReplacedIndex = gi, OldGroup = before, NewGroup = g.Clone() });
                }
                SyncChainDisplayMirror(model, g);
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            if (created) ModifierEvaluator.PrimeGroup(model, g);
            FinishGroupEdit(project, model);
            RecordSubdivisionUndo(model, listBefore, groupsBefore, changes, $"処理列に追加: {g.Name} {c.Kind}");

            int ri = model.MeshContextList.IndexOf(result);
            ReportData(CommandDataJson.New()
                    .Text("stackName",   g.Name)
                    .Text("itemId",      step.ScenarioItemId)
                    .Int ("resultIndex", ri)
                    .Text("resultId",    result.ObjectId.ToString(CultureInfo.InvariantCulture))
                    .Text("state",       ChainStateText(model, g))
                    .Build(),
                ri >= 0 ? new[] { ri } : null,
                result.ObjectId != 0UL ? new[] { result.ObjectId } : null);
            return null;
        }

        /// <summary>
        /// 結果の描画オブジェクトを元メッシュの子として作る（サブディビジョンの子と同じ置き方）。
        /// 新しい MeshObject に頂点・面・線分だけを写す（ModifierChainHandler.WriteResult と同じ写し方）。
        /// MeshObject.Clone は BoneTransform・IK・剛体・揺れものなどの付帯データまで写し、
        /// 元メッシュに位置があると子のローカルにも同じ位置が入って二重に動く（枠組み計画 段 8 の範囲外・段 9 で修正）。
        /// </summary>
        private static MeshContext CreateChainResult(ModelContext model, MeshContext source)
        {
            string name = model.GenerateUniqueMeshName(source.Name + "_mod");
            var src = source.MeshObject;
            var mo = new MeshObject(name);
            foreach (var v in src.Vertices) mo.Vertices.Add(v.Clone());
            foreach (var f in src.Faces) mo.Faces.Add(f.Clone());
            var lgs = new List<LineGroup>();
            if (src.LineGroups != null) foreach (var lg in src.LineGroups) if (lg != null) lgs.Add(lg.Clone());
            mo.LineGroups = lgs;
            mo.RebuildIdSets();
            mo.InvalidatePositionCache();

            var unityMesh = mo.ToUnityMesh(model.MaterialCount);
            unityMesh.name      = name;
            unityMesh.hideFlags = HideFlags.HideAndDontSave;

            var result = new MeshContext
            {
                MeshObject = mo,
                Name       = name,
                UnityMesh  = unityMesh,
                // 1 本道は元と 1 つのオブジェクトなので表示をそろえる。元を他の項目が読むなら結果は別のオブジェクトで、
                // 元が隠れていても（隠した帯など）見えるようにする（書き出しからも消えないように）。
                IsVisible  = source.IsVisible || Poly_Ling.Modifiers.ModifierResultOps.IsReadBySteps(model, source.ObjectId),
                IsLocked   = true,
            };
            result.ParentModelContext   = model;
            result.HierarchyParentIndex = model.MeshContextList.IndexOf(source);
            result.Depth                = source.Depth + 1;
            model.Add(result);
            return result;
        }

        private static ModifierStackStep BuildChainStep(
            string kind, Dictionary<string, string> args, Dictionary<string, List<ulong>> refs,
            MeshContext source, MeshContext result)
        {
            var s = new ModifierStackStep { Kind = ModifierStackStepKind.Command, Action = kind };
            foreach (var kv in args) s.Args[kv.Key] = kv.Value;
            SetChainRefs(s, refs, source.ObjectId);
            s.OutputObjectIds = new List<ulong> { result.ObjectId };
            return s;
        }

        /// <summary>項目の MeshRefIds を元メッシュと refs だけにする。</summary>
        private static void SetChainRefs(ModifierStackStep s, Dictionary<string, List<ulong>> refs, ulong sourceId)
        {
            s.MeshRefIds = new Dictionary<string, List<ulong>>(StringComparer.Ordinal);
            s.SetMeshRefIds(ModifierChainHandler.SourceKey, new List<ulong> { sourceId });
            if (refs != null)
                foreach (var kv in refs) s.SetMeshRefIds(kv.Key, new List<ulong>(kv.Value));
        }

        // ================================================================
        // 引数の変更・並べ替え・有効
        // ================================================================

        private string ExecuteSetMeshModifierArgs(ProjectContext project, ModelContext model, SetMeshModifierArgsCommand c)
        {
            if (!TryFindChain(model, c.StackName, out var g, out string cErr)) return cErr;
            var step = g.FindStep(c.ItemId);
            if (step == null) return $"処理列 {g.Name} に項目 {c.ItemId} がありません";
            if (!TryPairArgs(c.ArgKeys, c.ArgValues, out var given, out string pErr)) return pErr;
            if (given.Count == 0) return "書く引数がありません";

            ChainEnds(model, g, out var source, out var result);
            if (source?.MeshObject == null || result?.MeshObject == null) return $"処理列 {g.Name} の元メッシュか結果が見つかりません";

            var merged = CurrentChainArgs(model, step);
            foreach (var kv in given) merged[kv.Key] = kv.Value;

            if (!TryNormalizeModifierArgs(model, step.Action, merged, source, result,
                    out var normalized, out var refs, out string nErr))
                return nErr;

            Action edit = () =>
            {
                step.Args = new Dictionary<string, string>(normalized, StringComparer.Ordinal);
                SetChainRefs(step, refs, source.ObjectId);
            };
            string desc = $"処理列の引数: {g.Name} {c.ItemId}";

            if (c.Preview) return PreviewChainArgs(project, model, g, c.ItemId, desc, edit);
            if (_chainPreview != null && _chainPreview.Matches(model, g.Name, c.ItemId))
                return CommitChainArgsPreview(project, model, g, edit);
            return EditChain(project, model, g, desc, edit);
        }

        // ================================================================
        // 頂点グループ（操作統合計画 段 3）
        //   範囲は頂点 ID で持つので、ID の無い頂点には振ってから登録する。
        //   頂点 ID の自動付与は他モデルとのモーフのために禁止されているが、利用者が頂点グループを作った元メッシュに限って
        //   未設定の頂点へ振ってよい（計画書 6 章）。既にある ID は変えない。
        //   元メッシュが別の項目の出力なら、作り直しで消えるので振らない。
        // ================================================================

        private string ExecuteSetMeshModifierVertexGroup(ProjectContext project, ModelContext model, SetMeshModifierVertexGroupCommand c)
        {
            if (!TryFindChain(model, c.StackName, out var g, out string cErr)) return cErr;
            var step = g.FindStep(c.ItemId);
            if (step == null) return $"処理列 {g.Name} に項目 {c.ItemId} がありません";
            ChainEnds(model, g, out var source, out var result);
            if (source?.MeshObject == null || result?.MeshObject == null) return $"処理列 {g.Name} の元メッシュか結果が見つかりません";

            bool canAssign = !IsStepOutput(model, source);
            var sel = source.Selection;
            MeshModifierScope scope;
            Poly_Ling.Selection.PartsSelectionSet partsSet = null;
            bool capture = false;
            int noId = 0;
            if (!string.IsNullOrEmpty(c.PartsSetName))
            {
                if (source.PartsSelectionSetList != null)
                    foreach (var ps in source.PartsSelectionSetList)
                        if (ps != null && ps.Name == c.PartsSetName) { partsSet = ps; break; }
                if (partsSet == null) return $"{source.Name} にパーツ選択辞書「{c.PartsSetName}」がありません";
                scope = MeshModifierScope.PartsSet(c.PartsSetName);
                // 控えの有無は複製で見る（引き当ては辞書の番号を書き換えるので、利用者の辞書には掛けない）。
                capture = canAssign && !partsSet.Clone().ResolveByIds(source.MeshObject, out _, out _);
            }
            else
            {
                scope = MeshModifierScope.FromIndices(source.MeshObject, sel?.Vertices, sel?.Edges, sel?.Faces, out noId);
                bool empty = scope.VertexIds.Length + scope.EdgeVertexIds.Length + scope.FaceSizes.Length == 0;
                if (empty && noId == 0) return "元メッシュで頂点・辺・面を選んでから実行してください";
                if (empty && !canAssign)
                    return $"選んだ要素 {noId} 件に頂点 ID がありません。{source.Name} は別の項目の出力で、作り直すと ID が消えるので振れません";
            }

            int assigned = 0, left = noId;
            string editErr = null;
            string r = EditChain(project, model, g, $"頂点グループ: {g.Name} {c.ItemId}", () =>
            {
                if (capture)
                {
                    assigned = VertexIdOps.AssignMissing(source);
                    partsSet.CaptureIds(source.MeshObject);
                }
                else if (noId > 0 && canAssign)
                {
                    assigned = VertexIdOps.AssignMissing(source);
                    scope = MeshModifierScope.FromIndices(source.MeshObject, sel?.Vertices, sel?.Edges, sel?.Faces, out left);
                }

                var merged = CurrentChainArgs(model, step);
                bool invert = merged.TryGetValue(MeshModifierScope.KeyInvert, out string iv) && iv == "true";
                foreach (var k in new List<string>(merged.Keys))
                    if (k.StartsWith(MeshModifierScope.KeyPrefix, StringComparison.Ordinal)) merged.Remove(k);
                scope.Invert = invert;
                foreach (var kv in MeshModifierRegistry.ScopeToArgs(scope)) merged[kv.Key] = kv.Value;

                if (!TryNormalizeModifierArgs(model, step.Action, merged, source, result,
                        out var normalized, out var refs, out editErr))
                    return;
                step.Args = new Dictionary<string, string>(normalized, StringComparer.Ordinal);
                SetChainRefs(step, refs, source.ObjectId);
            });
            if (r != null) return r;
            if (editErr != null) return editErr;

            ReportData(CommandDataJson.New()
                .Text("state", ChainStateText(model, g))
                .Int("assigned", assigned)
                .Int("noId", left)
                .Build());
            return null;
        }

        /// <summary>
        /// モディファイアの結果か（流し直すと頂点が置き換わり、振った ID が残らない）。
        /// 履歴の生成物（パイプなど）は作り直しが ID を番号どおりに戻す（RestoreVertexIds）ので当たらない。
        /// </summary>
        private static bool IsStepOutput(ModelContext model, MeshContext mc)
        {
            if (model?.ModifierStacks == null || mc == null || mc.ObjectId == 0UL) return false;
            foreach (var g in model.ModifierStacks)
            {
                if (g?.Steps == null || !g.IsChain) continue;
                foreach (var s in g.Steps)
                    if (s != null && s.IsExecutable && s.ContainsOutput(mc.ObjectId)) return true;
            }
            return false;
        }

        // ================================================================
        // 1 個ずつの操作（操作統合計画 段 4）
        //   適用は Blender の Apply：その項目を元メッシュの今の形に掛けて焼き込み、項目を消し、残りは残す。
        //   途中までの適用は 3ds Max の Collapse To。
        //   元を他の項目も読む形（並列・分岐）は元を書き換えない（ModifierResultOps の決まり）。
        // ================================================================

        private string ExecuteApplyMeshModifier(ProjectContext project, ModelContext model, ApplyMeshModifierCommand c)
        {
            if (!TryFindChain(model, c.StackName, out var g, out string cErr)) return cErr;
            var step = g.FindStep(c.ItemId);
            if (step == null) return $"処理列 {g.Name} に項目 {c.ItemId} がありません";
            if (!step.Enabled) return $"{DisplayNameOf(step.Action)} は無効なので適用しません";
            var pair = Poly_Ling.Modifiers.ModifierResultOps.PairOf(model, g);
            if (pair == null) return $"処理列 {g.Name} の元メッシュか結果が見つかりません";
            var source = pair.Source;

            var executable = g.Steps.FindAll(s => s != null && s.IsExecutable);
            int pos = executable.IndexOf(step);
            var targets = new List<ModifierStackStep>();
            if (c.UpTo)
            {
                for (int i = 0; i <= pos; i++) if (executable[i].Enabled) targets.Add(executable[i]);
            }
            else targets.Add(step);

            ModifierChainHandler.ClearDisplayUpTo(model);

            // 全部の項目に当たるなら「すべて適用」と同じ（ミラー側の引き継ぎもそちらが受け持つ）。
            if (targets.Count == executable.Count)
                return ExecuteApplyMeshModifierChain(project, model, new ApplyMeshModifierChainCommand(c.ModelIndex, c.StackName));

            if (targets.Exists(s => MeshModifierRegistry.Find(s.Action)?.PinnedLast ?? false))
                return $"{DisplayNameOf(step.Action)}（MQO の属性の形）は他のモディファイアが残っている間は適用できません。「すべて適用」を使ってください";
            if (!Poly_Ling.Modifiers.ModifierResultOps.IsFolded(model, g, pair.Step)
                || Poly_Ling.Modifiers.ModifierResultOps.SourceOf(model, source) != null)
                return $"{source.Name} は他からも読まれているので、1 個ずつは適用できません（読んでいる側の形が変わるため）。「すべて適用」で結果を独立させてください";

            var output = ModifierChainHandler.EvaluateOnSource(model, targets, source, out string evalErr);
            if (output == null) return $"適用できません: {evalErr}";

            int sourceIndex = model.MeshContextList.IndexOf(source);
            if (output.VertexCount != source.MeshObject.VertexCount)
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc != null && mc.Type == MeshType.Morph && mc.MorphParentIndex == sourceIndex)
                        return $"{source.Name} はモーフを持つので、頂点数を変えるモディファイアは適用できません";
                }

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);
            var changes    = new List<ModifierStackChangeRecord>();

            _undoController?.SuspendRecording();
            try
            {
                RemoveHistoryOf(model, source, changes);
                int gi = model.ModifierStacks.IndexOf(g);
                var before = g.Clone();
                foreach (var s in targets) g.Steps.Remove(s);
                changes.Add(new ModifierStackChangeRecord { ReplacedIndex = gi, OldGroup = before, NewGroup = g.Clone() });

                Poly_Ling.Modifiers.ModifierResultOps.CopyGeometry(output, source.MeshObject);
                source.Selection?.ClearAll();
                var mesh = source.MeshObject.ToUnityMesh(model.MaterialCount);
                mesh.name      = source.Name;
                mesh.hideFlags = HideFlags.HideAndDontSave;
                source.ReplaceUnityMeshDeferred(mesh);
                source.OriginalPositions = (Vector3[])source.MeshObject.Positions.Clone();
                source.InvalidateSymmetryCache();
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            FinishGroupEdit(project, model);
            RecordSubdivisionUndo(model, listBefore, model.ModifierStackCount, changes, $"モディファイアの適用: {g.Name} {c.ItemId}");

            ReportData(CommandDataJson.New()
                .Text("warning", !c.UpTo && pos > 0 ? "先頭のモディファイアではないので、前のモディファイアを飛ばして元の形に適用しました。形が変わることがあります" : "")
                .Text("state", ChainStateText(model, g))
                .Build());
            return null;
        }

        /// <summary>項目の参照の引数（元メッシュを除く）を写す。</summary>
        private static Dictionary<string, List<ulong>> CopyChainRefs(ModifierStackStep step)
        {
            var refs = new Dictionary<string, List<ulong>>(StringComparer.Ordinal);
            var info = MeshModifierRegistry.Find(step.Action);
            if (info == null) return refs;
            foreach (var p in info.Params)
            {
                if (!p.Attr.IsMeshRef) continue;
                var ids = step.GetMeshRefIds(p.Key);
                if (ids.Count > 0) refs[p.Key] = new List<ulong>(ids);
            }
            return refs;
        }

        private string ExecuteDuplicateMeshModifier(ProjectContext project, ModelContext model, DuplicateMeshModifierCommand c)
        {
            if (!TryFindChain(model, c.StackName, out var g, out string cErr)) return cErr;
            var step = g.FindStep(c.ItemId);
            if (step == null) return $"処理列 {g.Name} に項目 {c.ItemId} がありません";
            if (MeshModifierRegistry.Find(step.Action)?.PinnedLast ?? false)
                return "MQO の属性で持つミラーは 1 つだけなので複製できません";
            ChainEnds(model, g, out var source, out var result);
            if (source?.MeshObject == null || result?.MeshObject == null) return $"処理列 {g.Name} の元メッシュか結果が見つかりません";

            ModifierStackStep copy = null;
            string r = EditChain(project, model, g, $"モディファイアの複製: {g.Name} {c.ItemId}", () =>
            {
                copy = g.AddStep(BuildChainStep(step.Action, new Dictionary<string, string>(step.Args, StringComparer.Ordinal),
                                                CopyChainRefs(step), source, result));
                copy.Enabled = step.Enabled;
                g.Steps.Remove(copy);
                g.Steps.Insert(g.Steps.IndexOf(step) + 1, copy);
            });
            if (r != null) return r;
            ReportData(CommandDataJson.New().Text("itemId", copy.ScenarioItemId).Text("state", ChainStateText(model, g)).Build());
            return null;
        }

        private string ExecuteCopyMeshModifierToObjects(ProjectContext project, ModelContext model, CopyMeshModifierToObjectsCommand c)
        {
            if (!TryFindChain(model, c.StackName, out var g, out string cErr)) return cErr;
            var step = g.FindStep(c.ItemId);
            if (step == null) return $"処理列 {g.Name} に項目 {c.ItemId} がありません";
            ChainEnds(model, g, out var from, out _);

            var targets = new List<MeshContext>();
            if (c.TargetMasterIndices != null && c.TargetMasterIndices.Length > 0)
            {
                foreach (int i in c.TargetMasterIndices)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null) return $"描画オブジェクト {i} がありません";
                    targets.Add(mc);
                }
            }
            else targets = CollectSelectedMeshContexts(model);
            targets.RemoveAll(t => ReferenceEquals(t, from));
            if (targets.Count == 0) return "コピー先のオブジェクトも選んでください";

            // 選択から作った頂点グループはオブジェクトごとに ID が違うので全体に戻す。辞書の名前はそのまま使う。
            var args = new Dictionary<string, string>(step.Args, StringComparer.Ordinal);
            if (args.TryGetValue(MeshModifierScope.KeyKind, out string kindText)
                && PanelCommandFactory.TryParseValue(kindText, typeof(MeshModifierScopeKind), out object kindValue, out _)
                && (MeshModifierScopeKind)kindValue == MeshModifierScopeKind.Ids)
            {
                foreach (var k in new List<string>(args.Keys))
                    if (k.StartsWith(MeshModifierScope.KeyPrefix, StringComparison.Ordinal)) args.Remove(k);
            }
            var refs = CopyChainRefs(step);
            bool pinned = MeshModifierRegistry.Find(step.Action)?.PinnedLast ?? false;

            _undoController?.SetModelContext(model);
            var listBefore   = MeshFilterToSkinnedRecord.CaptureList(model);
            int groupsBefore = model.ModifierStackCount;
            var changes      = new List<ModifierStackChangeRecord>();
            var created      = new List<ModifierStack>();
            var skipped      = new List<string>();
            int copied = 0;

            _undoController?.SuspendRecording();
            try
            {
                foreach (var t in targets)
                {
                    if (t.MeshObject == null || t.Type != MeshType.Mesh) { skipped.Add($"{t.Name}: 通常のメッシュではない"); continue; }
                    bool refersSelf = false;
                    foreach (var kv in refs) if (kv.Value.Contains(t.ObjectId)) refersSelf = true;
                    if (refersSelf) { skipped.Add($"{t.Name}: このモディファイアの相手"); continue; }

                    var tg = ModifierChainOps.ChainOf(model, t);
                    ChainEnds(model, tg, out var tSource, out var tResult);
                    if (tg != null && !ReferenceEquals(tSource, t)) { skipped.Add($"{t.Name}: モディファイアの結果"); continue; }
                    if (tg == null && t.IsSkinned) { skipped.Add($"{t.Name}: スキンド化したメッシュ"); continue; }
                    int tPinned = tg != null ? PinnedIndex(tg) : -1;
                    if (pinned && tPinned >= 0) { skipped.Add($"{t.Name}: 既に MQO の属性で持つミラーがある"); continue; }

                    if (tg == null)
                    {
                        var result = CreateChainResult(model, t);
                        tg = new ModifierStack(model.GenerateUniqueModifierStackName(t.Name + "_chain")) { IsChain = true };
                        tg.Steps.Clear();
                        tg.AddStep(BuildChainStep(step.Action, args, refs, t, result));
                        model.AddModifierStack(tg);
                        created.Add(tg);
                    }
                    else
                    {
                        int gi = model.ModifierStacks.IndexOf(tg);
                        var before = tg.Clone();
                        var added = tg.AddStep(BuildChainStep(step.Action, args, refs, t, tResult));
                        if (tPinned >= 0)
                        {
                            tg.Steps.Remove(added);
                            tg.Steps.Insert(tPinned, added);
                        }
                        changes.Add(new ModifierStackChangeRecord { ReplacedIndex = gi, OldGroup = before, NewGroup = tg.Clone() });
                    }
                    SyncChainDisplayMirror(model, tg);
                    copied++;
                }
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            if (copied == 0) return "コピーできません: " + string.Join(" / ", skipped);
            foreach (var cg in created) ModifierEvaluator.PrimeGroup(model, cg);
            FinishGroupEdit(project, model);
            RecordSubdivisionUndo(model, listBefore, groupsBefore, changes, $"モディファイアのコピー: {g.Name} {c.ItemId}");
            ReportData(CommandDataJson.New().Int("copied", copied).Text("skipped", string.Join(" / ", skipped)).Build());
            return null;
        }

        // ================================================================
        // ミラーの持ち方（操作統合計画 段 3）
        // ================================================================

        private const string MirrorBakeKind = "mirrorBake";

        /// <summary>ミラーの持ち方を切り替えたときの、切り替える前の種類の引数（項目ごと・種類ごと）。</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ModifierStackStep, Dictionary<string, Dictionary<string, string>>> _mirrorKindArgs
            = new System.Runtime.CompilerServices.ConditionalWeakTable<ModifierStackStep, Dictionary<string, Dictionary<string, string>>>();

        private string ExecuteSetMeshModifierMirrorAttribute(ProjectContext project, ModelContext model, SetMeshModifierMirrorAttributeCommand c)
        {
            if (!TryFindChain(model, c.StackName, out var g, out string cErr)) return cErr;
            var step = g.FindStep(c.ItemId);
            if (step == null) return $"処理列 {g.Name} に項目 {c.ItemId} がありません";
            bool isBake = string.Equals(step.Action, MirrorBakeKind, StringComparison.Ordinal);
            bool isAttr = string.Equals(step.Action, MirrorDisplayKind, StringComparison.Ordinal);
            if (!isBake && !isAttr) return $"{DisplayNameOf(step.Action)} はミラーの項目ではありません";
            if (isAttr == c.AsAttribute)
            {
                ReportData(CommandDataJson.New().Text("state", ChainStateText(model, g)).Build());
                return null;
            }

            ChainEnds(model, g, out var source, out var result);
            if (source?.MeshObject == null || result?.MeshObject == null) return $"処理列 {g.Name} の元メッシュか結果が見つかりません";
            if (c.AsAttribute && PinnedIndex(g) >= 0)
                return $"処理列 {g.Name} には既に {DisplayNameOf(g.Steps[PinnedIndex(g)].Action)} があります（MQO の属性で持つミラーは 1 つだけ置けます）";

            // 軸と平面の位置は引き継ぐ。片方にしか無い設定は控えておき、戻したときに戻す。
            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            var kept = _mirrorKindArgs.GetOrCreateValue(step);
            if (kept.TryGetValue(c.AsAttribute ? MirrorDisplayKind : MirrorBakeKind, out var keptArgs))
                foreach (var kv in keptArgs) args[kv.Key] = kv.Value;
            kept[step.Action] = new Dictionary<string, string>(step.Args, StringComparer.Ordinal);
            if (!MeshModifierRegistry.FromArgs(step.Action, step.Args, out var mod, out _, out string fErr)) return fErr;
            string targetKind;
            if (mod is MirrorBakeMeshModifier bake)
            {
                targetKind = MirrorDisplayKind;
                args["axis"] = ((int)bake.Axis).ToString(CultureInfo.InvariantCulture);
                args["distance"] = bake.PlaneOffset.ToString("R", CultureInfo.InvariantCulture);
            }
            else
            {
                var disp = (MirrorDisplayMeshModifier)mod;
                targetKind = MirrorBakeKind;
                args["axis"] = ((int)disp.Axis).ToString(CultureInfo.InvariantCulture);
                args["planeOffset"] = disp.Distance.ToString("R", CultureInfo.InvariantCulture);
            }
            if (!TryNormalizeModifierArgs(model, targetKind, args, source, result, out var normalized, out var refs, out string nErr))
                return nErr;

            return EditChain(project, model, g, $"ミラーの持ち方: {g.Name} {c.ItemId}", () =>
            {
                step.Action = targetKind;
                step.Args = new Dictionary<string, string>(normalized, StringComparer.Ordinal);
                SetChainRefs(step, refs, source.ObjectId);
                if (c.AsAttribute)
                {
                    // 列の最後に 1 つだけ置ける形なので最後へ動かす。
                    g.Steps.Remove(step);
                    g.Steps.Add(step);
                }
            });
        }

        // ================================================================
        // 引数の下見（スライダーのドラッグ中。段 5）
        //   前例：ボーンのスライダー（Begin で控え → End で Undo。PlayerCommandDispatcher.BoneMorphUv.cs）と
        //   マテリアルのスライダー（操作中は下見 → 離したら確定。PlayerMaterialListSubPanel.cs）。
        //   preview=true の最初の 1 回で変更前のスタックとオブジェクト一覧を控え、以後は Undo を積まずに書いて流す。
        //   同じ項目への preview=false で、控えを変更前として Undo を 1 件積む。
        //   控えが残ったまま別の書き込みコマンドが来たら、その時点の状態で Undo 1 件にして閉じる（DispatchCore）。
        // ================================================================

        private sealed class ChainArgsPreviewHold
        {
            public ModelContext      Model;
            public string            StackName;
            public string            ItemId;
            public string            Description;
            public List<MeshContext> ListBefore;
            public int               GroupIndex;
            public ModifierStack     Before;

            public bool Matches(ModelContext model, string stackName, string itemId)
                => ReferenceEquals(Model, model)
                && string.Equals(StackName, stackName, StringComparison.Ordinal)
                && string.Equals(ItemId, itemId, StringComparison.Ordinal);
        }

        private ChainArgsPreviewHold _chainPreview;

        private string PreviewChainArgs(ProjectContext project, ModelContext model, ModifierStack g, string itemId,
                                        string desc, Action edit)
        {
            if (_chainPreview != null && !_chainPreview.Matches(model, g.Name, itemId)) FlushChainArgsPreview();
            if (_chainPreview == null)
            {
                _chainPreview = new ChainArgsPreviewHold
                {
                    Model       = model,
                    StackName   = g.Name,
                    ItemId      = itemId,
                    Description = desc,
                    ListBefore  = MeshFilterToSkinnedRecord.CaptureList(model),
                    GroupIndex  = model.ModifierStacks.IndexOf(g),
                    Before      = g.Clone(),
                };
            }

            MarkNotRecorded();
            _undoController?.SuspendRecording();
            try     { edit(); SyncChainDisplayMirror(model, g); }
            finally { _undoController?.ResumeRecording(); }

            FinishGroupEdit(project, model);
            ReportData(CommandDataJson.New().Text("state", ChainStateText(model, g)).Build());
            return null;
        }

        private string CommitChainArgsPreview(ProjectContext project, ModelContext model, ModifierStack g, Action edit)
        {
            var hold = _chainPreview;
            _chainPreview = null;

            _undoController?.SetModelContext(model);
            _undoController?.SuspendRecording();
            try     { edit(); SyncChainDisplayMirror(model, g); }
            finally { _undoController?.ResumeRecording(); }

            FinishGroupEdit(project, model);
            RecordChainArgsPreviewUndo(model, g, hold);
            ReportData(CommandDataJson.New().Text("state", ChainStateText(model, g)).Build());
            return null;
        }

        /// <summary>下見の控えが残っていれば、今の状態で Undo 1 件にして閉じる。</summary>
        private void FlushChainArgsPreview()
        {
            var hold = _chainPreview;
            _chainPreview = null;
            if (hold?.Model == null) return;
            var g = hold.Model.FindModifierStackByName(hold.StackName);
            if (g == null) return;
            _undoController?.SetModelContext(hold.Model);
            RecordChainArgsPreviewUndo(hold.Model, g, hold);
        }

        private void RecordChainArgsPreviewUndo(ModelContext model, ModifierStack g, ChainArgsPreviewHold hold)
        {
            RecordSubdivisionUndo(model, hold.ListBefore, model.ModifierStackCount,
                new List<ModifierStackChangeRecord>
                {
                    new ModifierStackChangeRecord { ReplacedIndex = hold.GroupIndex, OldGroup = hold.Before, NewGroup = g.Clone() },
                },
                hold.Description);
        }

        /// <summary>
        /// DispatchCore の門の直後に呼ぶ。下見の続き（同じ項目への setMeshModifierArgs）と、
        /// 書き込まないコマンド（PLCommand.Writes=None）では閉じない。
        /// UI 自動操作（Category "ui"。uiSetValue・uiClick）も閉じない。中で画面が送るコマンドが
        /// 改めてここを通るので、閉じるかはそのコマンドで決まる（uiSetValue でスライダーを動かすと
        /// 外側の uiSetValue が先に来るので、ここで閉じると下見が 1 件ずつ Undo になる）。
        /// </summary>
        private void CloseChainArgsPreviewBefore(PanelCommand cmd)
        {
            if (_chainPreview == null || cmd == null) return;
            if (cmd is SetMeshModifierArgsCommand s
                && string.Equals(s.StackName, _chainPreview.StackName, StringComparison.Ordinal)
                && string.Equals(s.ItemId, _chainPreview.ItemId, StringComparison.Ordinal))
                return;
            var a = (PLCommandAttribute)Attribute.GetCustomAttribute(cmd.GetType(), typeof(PLCommandAttribute), false);
            if (a != null && (a.Writes == PLWriteScope.None || a.Category == "ui")) return;
            FlushChainArgsPreview();
        }

        private string ExecuteMoveMeshModifier(ProjectContext project, ModelContext model, MoveMeshModifierCommand c)
        {
            if (!TryFindChain(model, c.StackName, out var g, out string cErr)) return cErr;
            int from = g.IndexOfStep(c.ItemId);
            if (from < 0) return $"処理列 {g.Name} に項目 {c.ItemId} がありません";
            bool toEnd = string.IsNullOrEmpty(c.BeforeItemId);
            if (!toEnd)
            {
                if (string.Equals(c.BeforeItemId, c.ItemId, StringComparison.Ordinal)) return "動かす項目と同じ項目の前へは動かせません";
                if (g.IndexOfStep(c.BeforeItemId) < 0) return $"処理列 {g.Name} に項目 {c.BeforeItemId} がありません";
            }

            // 末尾固定の項目（段 8）は動かせない。他の項目は末尾固定の項目の後ろへ置かない
            // （末尾へ＝末尾固定の項目の前へ）。
            int pinned = PinnedIndex(g);
            if (pinned == from)
                return $"{DisplayNameOf(g.Steps[from].Action)} は末尾固定の項目なので並べ替えできません";

            // 動かした後の並びを先に作り、変わらなければ何もしない。
            var order = new List<ModifierStackStep>(g.Steps);
            var moving = order[from];
            order.RemoveAt(from);
            int to = toEnd ? order.Count : order.FindIndex(s => s != null && s.ScenarioItemId == c.BeforeItemId);
            if (pinned >= 0)
            {
                int pinnedNow = order.IndexOf(g.Steps[pinned]);
                if (to > pinnedNow) to = pinnedNow;
            }
            order.Insert(to, moving);
            bool same = true;
            for (int i = 0; i < order.Count; i++) if (!ReferenceEquals(order[i], g.Steps[i])) { same = false; break; }
            if (same) { ReportData(CommandDataJson.New().Text("state", ChainStateText(model, g)).Build()); return null; }

            return EditChain(project, model, g, $"処理列の並べ替え: {g.Name} {c.ItemId}", () =>
            {
                g.Steps.Clear();
                g.Steps.AddRange(order);
            });
        }

        private string ExecuteSetMeshModifierEnabled(ProjectContext project, ModelContext model, SetMeshModifierEnabledCommand c)
        {
            if (!TryFindChain(model, c.StackName, out var g, out string cErr)) return cErr;
            var step = g.FindStep(c.ItemId);
            if (step == null) return $"処理列 {g.Name} に項目 {c.ItemId} がありません";
            if (step.Enabled == c.Enabled) { ReportData(CommandDataJson.New().Text("state", ChainStateText(model, g)).Build()); return null; }

            return EditChain(project, model, g, $"処理列の{(c.Enabled ? "有効" : "無効")}: {g.Name} {c.ItemId}",
                () => step.Enabled = c.Enabled);
        }

        /// <summary>スタックを書き換え、流し直し、Undo を 1 件積む（差し替えの記録）。</summary>
        private string EditChain(ProjectContext project, ModelContext model, ModifierStack g, string desc, Action edit)
        {
            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);
            int gi = model.ModifierStacks.IndexOf(g);
            var before = g.Clone();

            _undoController?.SuspendRecording();
            try     { edit(); SyncChainDisplayMirror(model, g); }
            finally { _undoController?.ResumeRecording(); }

            FinishGroupEdit(project, model);
            RecordSubdivisionUndo(model, listBefore, model.ModifierStackCount,
                new List<ModifierStackChangeRecord>
                {
                    new ModifierStackChangeRecord { ReplacedIndex = gi, OldGroup = before, NewGroup = g.Clone() },
                },
                desc);

            ReportData(CommandDataJson.New().Text("state", ChainStateText(model, g)).Build());
            return null;
        }

        // ================================================================
        // 削除・適用
        // ================================================================

        private string ExecuteRemoveMeshModifier(ProjectContext project, ModelContext model, RemoveMeshModifierCommand c)
        {
            if (!TryFindChain(model, c.StackName, out var g, out string cErr)) return cErr;
            var step = g.FindStep(c.ItemId);
            if (step == null) return $"処理列 {g.Name} に項目 {c.ItemId} がありません";

            int executable = 0;
            foreach (var s in g.Steps) if (s != null && s.IsExecutable) executable++;
            bool last = executable <= 1 && step.IsExecutable;
            if (!last)
            {
                string r = EditChain(project, model, g, $"処理列から削除: {g.Name} {c.ItemId}", () => g.Steps.Remove(step));
                if (r == null)
                    ReportData(CommandDataJson.New().Flag("stackRemoved", false).Text("state", ChainStateText(model, g)).Build());
                return r;
            }

            // 最後の項目：処理列ごと外し、結果の描画オブジェクトを消す。
            ChainEnds(model, g, out _, out var result);

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);
            int gi = model.ModifierStacks.IndexOf(g);
            var removed = new ModifierStackChangeRecord { RemovedGroup = g.Clone(), RemovedIndex = gi };

            _undoController?.SuspendRecording();
            try
            {
                ModifierEvaluator.ReleaseGroup(model, g);
                model.RemoveModifierStack(g);
                int ri = result != null ? model.MeshContextList.IndexOf(result) : -1;
                if (ri >= 0)
                {
                    // 結果に表示ミラー（ミラー（表示）の項目が作ったミラー側）があれば一緒に消す（段 8）。
                    var gone = new List<int>();
                    MirrorBranchOps.CollectMirrorPeers(model, ri, gone);
                    model.MirrorPairs?.RemoveAll(pr => pr != null && ReferenceEquals(pr.Real, result));
                    gone.Add(ri);
                    gone.Sort();
                    for (int k = gone.Count - 1; k >= 0; k--) model.RemoveAt(gone[k]);
                }
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            FinishGroupEdit(project, model);
            RecordSubdivisionUndo(model, listBefore, model.ModifierStackCount,
                new List<ModifierStackChangeRecord> { removed }, $"処理列から削除（処理列ごと外す）: {g.Name}");

            ReportData(CommandDataJson.New().Flag("stackRemoved", true).Text("state", "").Build());
            return null;
        }

        /// <summary>
        /// すべて適用（3ds Max の Collapse All。操作統合計画 段 2）。結果の形が元の名前のオブジェクトになり、
        /// オブジェクトは 1 つになる。
        ///
        /// 【実体】元の行を残して結果の頂点・面・線分を写し、結果の行を消す。元の行に付いている名前・親子・姿勢・
        ///   パーツ選択辞書・ObjectId（他のスタックの入力からの参照）はそのまま残る。結果を指していた参照は元へ付け替える。
        /// 【履歴】元を出力に持つ生成の項目も外す。残すと次の作り直しで適用した形を上書きするため（Collapse All も履歴ごと畳む）。
        /// 【モーフ】頂点数が変わるなら、モーフを持つオブジェクトには適用できない（Blender と同じ）。何も変えずに失敗する。
        /// </summary>
        private string ExecuteApplyMeshModifierChain(ProjectContext project, ModelContext model, ApplyMeshModifierChainCommand c)
        {
            var g = string.IsNullOrEmpty(c.StackName) ? null : model.FindModifierStackByName(c.StackName);
            if (g == null) return $"スタックが見つかりません: {c.StackName}";
            var pair = Poly_Ling.Modifiers.ModifierResultOps.PairOf(model, g);
            if (pair == null) return $"モディファイアのスタックではありません: {c.StackName}";
            ModifierChainHandler.ClearDisplayUpTo(model);
            var source = pair.Source;
            var result = pair.Result;

            // 元を他の項目が読む・元が別の項目の結果・結果を生成が読む形では、元を上書きしない。
            // 結果を独立したオブジェクトにする（並列の入力を壊さない。ModifierResultOps の決まり）。
            if (!Poly_Ling.Modifiers.ModifierResultOps.IsFolded(model, g, pair.Step)
                || Poly_Ling.Modifiers.ModifierResultOps.SourceOf(model, source) != null)
                return DetachModifierResult(project, model, g, pair);

            int sourceIndex = model.MeshContextList.IndexOf(source);
            if (result.MeshObject.VertexCount != source.MeshObject.VertexCount)
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc != null && mc.Type == MeshType.Morph && mc.MorphParentIndex == sourceIndex)
                        return $"{source.Name} はモーフを持つので、頂点数を変えるモディファイアは適用できません";
                }

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);
            var changes    = new List<ModifierStackChangeRecord>();

            _undoController?.SuspendRecording();
            try
            {
                RemoveModifierLink(model, pair, changes);
                RemoveHistoryOf(model, source, changes);
                RedirectModifierRefs(model, result.ObjectId, source.ObjectId, changes);

                // 結果のミラー側（ミラーのモディファイア）は、元の相方にする。元に別のミラー側があれば消す。
                var resultPeers = new List<int>();
                MirrorBranchOps.CollectMirrorPeers(model, model.MeshContextList.IndexOf(result), resultPeers);
                var sourcePeers = new List<int>();
                MirrorBranchOps.CollectMirrorPeers(model, sourceIndex, sourcePeers);

                Poly_Ling.Modifiers.ModifierResultOps.CopyGeometry(result.MeshObject, source.MeshObject);
                source.Selection?.ClearAll();

                var gone = new List<MeshContext> { result };
                if (resultPeers.Count > 0)
                {
                    foreach (int m in sourcePeers) gone.Add(model.GetMeshContext(m));
                    source.MirrorType           = result.MirrorType;
                    source.MirrorAxis           = result.MirrorAxis;
                    source.MirrorDistance       = result.MirrorDistance;
                    source.MirrorMaterialOffset = result.MirrorMaterialOffset;
                    var mirrorPair = model.GetMirrorPair(result);
                    if (mirrorPair != null && ReferenceEquals(mirrorPair.Real, result))
                    {
                        mirrorPair.Real = source;
                        var mirrorCtx = mirrorPair.Mirror;
                        if (mirrorCtx != null)
                        {
                            mirrorCtx.Name = MirrorNameOps.MakeMirrorName(source.Name, MirrorBranchOps.MirrorBranchSuffix, n => ExistsMeshName(model, n));
                            mirrorCtx.HierarchyParentIndex = source.HierarchyParentIndex;
                            mirrorCtx.Depth = source.Depth;
                            if (mirrorCtx.BakedMirrorSourceIndex >= 0)
                                mirrorCtx.BakedMirrorSourceIndex = model.MeshContextList.IndexOf(source);
                        }
                    }
                }
                // 消す前に対を外す。元の側の対は、作り直した相方で残る。
                model.MirrorPairs?.RemoveAll(pr => pr != null && (gone.Contains(pr.Real) || gone.Contains(pr.Mirror)));

                var goneIndices = new List<int>();
                foreach (var mc in gone)
                {
                    int i = model.MeshContextList.IndexOf(mc);
                    if (i >= 0) goneIndices.Add(i);
                }
                goneIndices.Sort();
                for (int k = goneIndices.Count - 1; k >= 0; k--) model.RemoveAt(goneIndices[k]);

                // 元の行の中身が変わったので、表示用のメッシュとミラー側を作り直す。
                var mesh = source.MeshObject.ToUnityMesh(model.MaterialCount);
                mesh.name      = source.Name;
                mesh.hideFlags = HideFlags.HideAndDontSave;
                source.ReplaceUnityMeshDeferred(mesh);
                source.OriginalPositions = (Vector3[])source.MeshObject.Positions.Clone();
                ModifierChainHandler.RebuildMirrorSide(model, source);
                source.InvalidateSymmetryCache();
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            FinishGroupEdit(project, model);
            RecordSubdivisionUndo(model, listBefore, model.ModifierStackCount, changes, $"すべて適用: {g.Name}");

            int ri = model.MeshContextList.IndexOf(source);
            ReportData(CommandDataJson.New().Int("resultIndex", ri).Build(),
                ri >= 0 ? new[] { ri } : null,
                source.ObjectId != 0UL ? new[] { source.ObjectId } : null);
            return null;
        }

        /// <summary>
        /// 結果を今の形のまま独立したオブジェクトにする。元・元の履歴・他の項目の参照には触らない。
        /// </summary>
        private string DetachModifierResult(ProjectContext project, ModelContext model, ModifierStack g,
                                            Poly_Ling.Modifiers.ModifierResultOps.Pair pair)
        {
            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);
            var changes    = new List<ModifierStackChangeRecord>();

            _undoController?.SuspendRecording();
            try
            {
                RemoveModifierLink(model, pair, changes);
                if (pair.IsSubdivision) SubdivisionSync.Forget(pair.Result);
                pair.Result.IsLocked = false;
                var source = pair.Source;
                if (!Poly_Ling.Modifiers.ModifierResultOps.CollectPairs(model).Exists(p => ReferenceEquals(p.Source, source)))
                {
                    source.HideFaceShading = false;
                    source.HideCageLines   = false;
                }
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            FinishGroupEdit(project, model);
            RecordSubdivisionUndo(model, listBefore, model.ModifierStackCount, changes, $"すべて適用: {g.Name}");

            int ri = model.MeshContextList.IndexOf(pair.Result);
            ReportData(CommandDataJson.New().Int("resultIndex", ri).Build(),
                ri >= 0 ? new[] { ri } : null,
                pair.Result.ObjectId != 0UL ? new[] { pair.Result.ObjectId } : null);
            return null;
        }

        /// <summary>
        /// 元と結果のつながりをスタックから外す（書き換えは呼び出し側が SuspendRecording の中で行う）。
        /// 処理器が立てたロック・塗りの止めを戻す。サブディビジョンで他の項目があるスタックは項目だけを外す。
        /// </summary>
        private static void RemoveModifierLink(ModelContext model, Poly_Ling.Modifiers.ModifierResultOps.Pair p,
                                               List<ModifierStackChangeRecord> changes)
        {
            int gi = model.ModifierStacks.IndexOf(p.Stack);
            if (gi < 0) return;

            if (p.IsSubdivision && p.Stack.StepCount > 1)
            {
                var before = p.Stack.Clone();
                SubdivisionSync.Forget(p.Result);
                p.Result.IsLocked = false;
                p.Source.HideFaceShading = false;
                p.Source.HideCageLines   = false;
                p.Stack.Steps.Remove(p.Step);
                changes.Add(new ModifierStackChangeRecord { ReplacedIndex = gi, OldGroup = before, NewGroup = p.Stack.Clone() });
                return;
            }

            changes.Add(new ModifierStackChangeRecord { RemovedGroup = p.Stack.Clone(), RemovedIndex = gi });
            ModifierEvaluator.ReleaseGroup(model, p.Stack);
            model.RemoveModifierStack(p.Stack);
        }

        /// <summary>
        /// 描画オブジェクトを出力に持つ生成の項目を外す。項目の出力が他にもあれば、項目単位では外せないのでスタックごと外す。
        /// 実行する項目が無くなったスタックも外す。
        /// </summary>
        private static void RemoveHistoryOf(ModelContext model, MeshContext target, List<ModifierStackChangeRecord> changes)
        {
            if (model.ModifierStacks == null || target.ObjectId == 0UL) return;
            foreach (var g in new List<ModifierStack>(model.ModifierStacks))
            {
                if (g?.Steps == null || g.IsChain) continue;
                var hits = g.Steps.FindAll(s => s != null && s.IsExecutable && s.ContainsOutput(target.ObjectId));
                if (hits.Count == 0) continue;

                int gi = model.ModifierStacks.IndexOf(g);
                bool whole = hits.Exists(s => s.OutputObjectIds.Count > 1)
                          || g.Steps.TrueForAll(s => s == null || !s.IsExecutable || hits.Contains(s));
                if (whole)
                {
                    changes.Add(new ModifierStackChangeRecord { RemovedGroup = g.Clone(), RemovedIndex = gi });
                    ModifierEvaluator.ReleaseGroup(model, g);
                    model.RemoveModifierStack(g);
                }
                else
                {
                    var before = g.Clone();
                    foreach (var s in hits) g.Steps.Remove(s);
                    changes.Add(new ModifierStackChangeRecord { ReplacedIndex = gi, OldGroup = before, NewGroup = g.Clone() });
                }
            }
        }

        /// <summary>全スタックの入力の参照を from から to へ付け替える。</summary>
        private static void RedirectModifierRefs(ModelContext model, ulong from, ulong to, List<ModifierStackChangeRecord> changes)
        {
            if (model.ModifierStacks == null || from == 0UL) return;
            for (int gi = 0; gi < model.ModifierStacks.Count; gi++)
            {
                var g = model.ModifierStacks[gi];
                if (g?.Steps == null) continue;
                ModifierStack before = null;
                foreach (var s in g.Steps)
                {
                    if (s == null) continue;
                    foreach (var kv in s.SortedMeshRefIds())
                    {
                        if (kv.Value == null || !kv.Value.Contains(from)) continue;
                        before ??= g.Clone();
                        var ids = new List<ulong>(kv.Value);
                        for (int i = 0; i < ids.Count; i++) if (ids[i] == from) ids[i] = to;
                        s.SetMeshRefIds(kv.Key, ids);
                    }
                }
                if (before != null)
                    changes.Add(new ModifierStackChangeRecord { ReplacedIndex = gi, OldGroup = before, NewGroup = g.Clone() });
            }
        }

        /// <summary>
        /// 消す対象にモディファイアの元が含まれていれば、結果（とミラー側）とスタックも一緒に消して Undo を 1 件積み、true を返す。
        /// 含まれていなければ何もせず false（呼び出し側が普通に消す）。
        /// </summary>
        private bool DeleteMeshesWithModifiers(ProjectContext project, ModelContext model, int[] masterIndices)
        {
            // 畳まれた 1 本道だけを一緒に消す。別オブジェクトの結果は消さない。
            var pairs = new List<Poly_Ling.Modifiers.ModifierResultOps.Pair>();
            var targets = new HashSet<int>(masterIndices);
            var folded = Poly_Ling.Modifiers.ModifierResultOps.FoldedPairs(model);
            bool grew = true;
            while (grew)   // 多段は根から順に広げる
            {
                grew = false;
                foreach (var p in folded)
                {
                    if (pairs.Contains(p) || !targets.Contains(model.MeshContextList.IndexOf(p.Source))) continue;
                    pairs.Add(p);
                    targets.Add(model.MeshContextList.IndexOf(p.Result));
                    grew = true;
                }
            }
            if (pairs.Count == 0) return false;

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);
            var changes    = new List<ModifierStackChangeRecord>();

            _undoController?.SuspendRecording();
            try
            {
                var peers = new List<int>();
                foreach (var p in pairs)
                {
                    int ri = model.MeshContextList.IndexOf(p.Result);
                    if (ri < 0) continue;
                    targets.Add(ri);
                    peers.Clear();
                    MirrorBranchOps.CollectMirrorPeers(model, ri, peers);
                    foreach (int m in peers) targets.Add(m);
                }
                var gone = new List<MeshContext>();
                foreach (int i in targets)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc != null) gone.Add(mc);
                }
                foreach (var p in pairs) RemoveModifierLink(model, p, changes);
                model.MirrorPairs?.RemoveAll(pr => pr != null && (gone.Contains(pr.Real) || gone.Contains(pr.Mirror)));

                var order = new List<int>(targets);
                order.Sort();
                for (int k = order.Count - 1; k >= 0; k--)
                    if (order[k] >= 0 && order[k] < model.MeshContextCount) model.RemoveAt(order[k]);
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            FinishGroupEdit(project, model);
            RecordSubdivisionUndo(model, listBefore, model.ModifierStackCount, changes, "オブジェクトの削除（モディファイアごと）");
            return true;
        }

        /// <summary>
        /// 元の対のモディファイアを、複製したオブジェクトへ写す（結果の行とスタックを作る）。
        /// 書き換えは呼び出し側が SuspendRecording の中で行う。複製したスタックは次の評価で結果を作り直す。
        /// </summary>
        /// <returns>複製の結果の行（多段は次の段の元にする）。</returns>
        private MeshContext CopyModifiersTo(ModelContext model, Poly_Ling.Modifiers.ModifierResultOps.Pair p, MeshContext dup)
        {
            var result = CreateChainResult(model, dup);

            var g = p.Stack.Clone();
            g.Name = model.GenerateUniqueModifierStackName(dup.Name + (p.IsSubdivision ? "_subdiv" : "_chain"));
            string sourceKey = p.IsSubdivision ? SubdivisionSync.CageKey : ModifierChainHandler.SourceKey;
            if (p.IsSubdivision)
            {
                // サブディビジョンのスタックは、つながりの項目だけを写す。
                int at = p.Stack.Steps.IndexOf(p.Step);
                var keep = at >= 0 && at < g.Steps.Count ? g.Steps[at] : null;
                g.Steps.Clear();
                if (keep != null) g.Steps.Add(keep);
            }
            foreach (var s in g.Steps)
            {
                if (s == null || !s.IsExecutable) continue;
                s.SetMeshRefIds(sourceKey, new List<ulong> { dup.ObjectId });
                s.OutputObjectIds = new List<ulong> { result.ObjectId };
            }
            model.AddModifierStack(g);
            if (g.IsChain) SyncChainDisplayMirror(model, g);
            return result;
        }

        // ================================================================
        // 共通
        // ================================================================

        private static bool TryFindChain(ModelContext model, string name, out ModifierStack g, out string error)
        {
            error = null;
            g = string.IsNullOrEmpty(name) ? null : model.FindModifierStackByName(name);
            if (g == null) { error = $"スタックが見つかりません: {name}"; return false; }
            if (!g.IsChain) { error = $"処理列ではありません: {name}（処理列のコマンドは処理列のスタックにだけ使える）"; return false; }
            return true;
        }

        /// <summary>末尾固定の種類（PLMeshModifier.PinnedLast。段 8）の項目の位置。無ければ -1。</summary>
        private static int PinnedIndex(ModifierStack g)
        {
            if (g?.Steps == null) return -1;
            for (int i = 0; i < g.Steps.Count; i++)
            {
                var s = g.Steps[i];
                if (s == null || !s.IsExecutable) continue;
                var info = MeshModifierRegistry.Find(s.Action);
                if (info != null && info.PinnedLast) return i;
            }
            return -1;
        }

        private static string DisplayNameOf(string kind)
            => MeshModifierRegistry.Find(kind)?.DisplayName ?? kind;

        /// <summary>
        /// 処理列の結果の表示ミラーを「ミラー（表示）」の項目に合わせる（段 8。計画書 3.6）。
        /// 書き換えの直後、SuspendRecording の中で呼ぶ（一覧のスナップショットとミラー対の控えで Undo に乗る）。
        ///   有効な mirrorDisplay がある … 結果の MirrorAxis・MirrorDistance を書き、ミラー側が無ければ作る
        ///                                    （EnableMirror。setMirrorEnabled と同じ）。軸か距離が変わったら対の軸を
        ///                                    合わせてミラー側を作り直す。MirrorType を書く
        ///   無い（無効・削除）             … ミラー側を片付け MirrorType を 0 にする（DisableMirror）
        /// 結果の形が変わったときのミラー側の追随は処理器（ModifierChainHandler.WriteResult・SyncDrag）。
        /// </summary>
        private void SyncChainDisplayMirror(ModelContext model, ModifierStack g)
        {
            if (model == null || g == null || !model.ModifierStacks.Contains(g)) return;
            ChainEnds(model, g, out _, out var result);
            if (result?.MeshObject == null) return;
            int ri = model.MeshContextList.IndexOf(result);
            if (ri < 0) return;

            MirrorDisplayMeshModifier want = null;
            foreach (var s in g.Steps)
            {
                if (s == null || !s.IsExecutable || !s.Enabled) continue;
                if (!string.Equals(s.Action, MirrorDisplayKind, StringComparison.Ordinal)) continue;
                if (MeshModifierRegistry.FromArgs(s.Action, s.Args, out var m, out _, out _)) want = m as MirrorDisplayMeshModifier;
                break;
            }

            var peers = new List<int>();
            MirrorBranchOps.CollectMirrorPeers(model, ri, peers);

            if (want == null)
            {
                if (peers.Count == 0 && result.MirrorType == 0) return;
                DisableMirror(model, ri, result, new List<(int, MeshContext)>());
                return;
            }

            int   axis = want.GetMirrorAxisValue();
            float dist = want.Distance;
            bool geomChanged = result.MirrorAxis != axis || result.MirrorDistance != dist;
            result.MirrorAxis     = axis;
            result.MirrorDistance = dist;

            if (peers.Count == 0)
            {
                EnableMirror(model, ri, result, new List<(int Index, MeshContext MeshContext)>());
            }
            else if (geomChanged)
            {
                var pair = model.GetMirrorPair(result);
                if (pair != null && ReferenceEquals(pair.Real, result)) pair.Axis = result.GetMirrorSymmetryAxis();
                ModifierChainHandler.RebuildMirrorSide(model, result);
            }
            result.MirrorType = (int)want.Type;
            result.InvalidateSymmetryCache();
        }

        private const string MirrorDisplayKind = "mirrorDisplay";

        /// <summary>処理列の元メッシュと結果（最初の実行項目から引く）。無ければ null。</summary>
        private static void ChainEnds(ModelContext model, ModifierStack g, out MeshContext source, out MeshContext result)
            => ModifierChainOps.ChainEnds(model, g, out source, out result);

        /// <summary>この描画オブジェクトがどれかの処理列の元メッシュか結果なら、そのスタック名。</summary>
        private static string ChainEndOf(ModelContext model, MeshContext mc)
            => ModifierChainOps.ChainOf(model, mc)?.Name;

        /// <summary>キーと値の並びを辞書にする。長さ違い・空のキー・重複は失敗。</summary>
        private static bool TryPairArgs(string[] keys, string[] values, out Dictionary<string, string> args, out string error)
        {
            args = new Dictionary<string, string>(StringComparer.Ordinal);
            error = null;
            int nk = keys?.Length ?? 0, nv = values?.Length ?? 0;
            if (nk != nv) { error = $"argKeys（{nk} 件）と argValues（{nv} 件）の長さが違います"; return false; }
            for (int i = 0; i < nk; i++)
            {
                if (string.IsNullOrEmpty(keys[i])) { error = $"argKeys[{i}] が空です"; return false; }
                if (args.ContainsKey(keys[i])) { error = $"引数 {keys[i]} が 2 回あります"; return false; }
                args[keys[i]] = values[i] ?? "";
            }
            return true;
        }

        /// <summary>
        /// 種類の宣言で引数を検査し、正規化した文字列（ToArgs）と、参照の引数の ObjectId を返す。
        /// 参照の引数は索引で受け、元メッシュ・結果は指せない。
        /// </summary>
        private static bool TryNormalizeModifierArgs(
            ModelContext model, string kind, Dictionary<string, string> args,
            MeshContext source, MeshContext result,
            out Dictionary<string, string> normalized, out Dictionary<string, List<ulong>> refs, out string error)
        {
            normalized = null;
            refs = new Dictionary<string, List<ulong>>(StringComparer.Ordinal);
            if (!MeshModifierRegistry.FromArgs(kind, args, out var mod, out var scope, out error)) return false;
            normalized = MeshModifierRegistry.ToArgs(mod, scope);

            var info = MeshModifierRegistry.Find(kind);
            foreach (var p in info.Params)
            {
                if (!p.Attr.IsMeshRef) continue;
                if (!normalized.TryGetValue(p.Key, out string raw) || string.IsNullOrEmpty(raw)) continue;
                if (!PanelCommandFactory.TryParseValue(raw, typeof(int[]), out object v, out string why))
                { error = $"参照 \"{p.Key}\" を索引にできない（{why}）"; return false; }
                var ids = new List<ulong>();
                foreach (int idx in (int[])v)
                {
                    if (idx < 0) continue;
                    var mc = model.GetMeshContext(idx);
                    if (mc == null || mc.ObjectId == 0UL) { error = $"参照 \"{p.Key}\" の描画オブジェクト {idx} がありません"; return false; }
                    if (ReferenceEquals(mc, source)) { error = $"参照 \"{p.Key}\" に元メッシュは指せません"; return false; }
                    if (result != null && ReferenceEquals(mc, result)) { error = $"参照 \"{p.Key}\" に結果は指せません"; return false; }
                    // 相手が畳まれた 1 本道なら、その末端の形を読む（Blender のブーリアンと同じ。操作統合計画 段 2）。
                    // 別オブジェクトとして見えている元は、選ばれたものをそのまま読む。
                    mc = Poly_Ling.Modifiers.ModifierResultOps.FoldedLeafOf(model, mc);
                    ids.Add(mc.ObjectId);
                }
                if (ids.Count > 0) refs[p.Key] = ids;
            }
            return true;
        }

        /// <summary>項目の今の引数。参照の引数は MeshRefIds から今の索引へ引き直す。</summary>
        private static Dictionary<string, string> CurrentChainArgs(ModelContext model, ModifierStackStep step)
            => ModifierChainOps.CurrentArgs(model, step);

        /// <summary>処理列の節点の最後の評価の結果と理由。</summary>
        private static string ChainStateText(ModelContext model, ModifierStack g)
            => ModifierChainOps.TryGetChainState(model, g, out var r, out string msg)
                ? $"{r}: {msg}"
                : "None: 節点がありません";

        // ================================================================
        // 照会の組み立て
        // ================================================================

        private static string BuildMeshModifierChainData(ModelContext model, string stackName, out string error)
        {
            if (!TryFindChain(model, stackName, out var g, out error)) return null;
            ChainEnds(model, g, out var source, out var result);

            var stateById = new Dictionary<string, ModifierChainHandler.ItemState>(StringComparer.Ordinal);
            foreach (var st in ModifierChainHandler.ItemStates(result))
                if (st != null && !string.IsNullOrEmpty(st.ScenarioItemId)) stateById[st.ScenarioItemId] = st;

            var itemIds = new List<string>();
            var kinds   = new List<string>();
            var enabled = new List<int>();
            var argsTxt = new List<string>();
            var states  = new List<string>();
            var msgs    = new List<string>();
            foreach (var s in g.Steps)
            {
                if (s == null || !s.IsExecutable) continue;
                itemIds.Add(s.ScenarioItemId);
                kinds.Add(s.Action);
                enabled.Add(s.Enabled ? 1 : 0);

                var cur = CurrentChainArgs(model, s);
                var keys = new List<string>(cur.Keys);
                keys.Sort(string.CompareOrdinal);
                var sb = new StringBuilder();
                foreach (var k in keys)
                {
                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(k).Append('=').Append(cur[k]);
                    var ids = k == ModifierChainHandler.SourceKey ? null : s.GetMeshRefIds(k);
                    if (ids != null && ids.Count > 0)
                    {
                        sb.Append("=>");
                        for (int i = 0; i < ids.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append(ids[i].ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }
                argsTxt.Add(sb.ToString());

                if (stateById.TryGetValue(s.ScenarioItemId, out var st)) { states.Add(st.Result.ToString()); msgs.Add(st.Message ?? ""); }
                else { states.Add(ModifierChainHandler.ItemResult.None.ToString()); msgs.Add(""); }
            }

            string chainResult = "None", chainMessage = "";
            foreach (var e in ModifierEvaluator.GetGraph(model))
            {
                if (!e.IsChain || !ReferenceEquals(e.Group, g)) continue;
                ModifierEvaluator.GetEntryState(model, e, out _, out var r, out chainMessage);
                chainResult = r.ToString();
                break;
            }

            return CommandDataJson.New()
                .Int  ("sourceIndex",  source != null ? model.MeshContextList.IndexOf(source) : -1)
                .Text ("sourceId",     source != null ? source.ObjectId.ToString(CultureInfo.InvariantCulture) : "")
                .Int  ("resultIndex",  result != null ? model.MeshContextList.IndexOf(result) : -1)
                .Text ("resultId",     result != null ? result.ObjectId.ToString(CultureInfo.InvariantCulture) : "")
                .Texts("itemIds",      itemIds)
                .Texts("kinds",        kinds)
                .Ints ("enabled",      enabled)
                .Texts("args",         argsTxt)
                .Texts("states",       states)
                .Texts("messages",     msgs)
                .Text ("chainResult",  chainResult)
                .Text ("chainMessage", chainMessage ?? "")
                .Build();
        }

        private static string BuildMeshModifierKindsData()
        {
            var inv = CultureInfo.InvariantCulture;
            var kinds = new List<string>();
            var names = new List<string>();
            var cats  = new List<string>();
            var chg   = new List<string>();
            var pos   = new List<int>();
            var follow = new List<int>();
            var pinned = new List<int>();
            var descs = new List<string>();
            var prms  = new List<string>();
            foreach (var info in MeshModifierRegistry.All)
            {
                kinds.Add(info.Kind);
                names.Add(info.DisplayName ?? "");
                cats.Add(info.Category ?? "");
                chg.Add(info.Change.ToString());
                pos.Add(info.HasPositionsPath ? 1 : 0);
                follow.Add(info.HasDragFollow ? 1 : 0);
                pinned.Add(info.PinnedLast ? 1 : 0);
                descs.Add(info.Description ?? "");

                var sb = new StringBuilder();
                foreach (var p in info.Params)
                {
                    if (sb.Length > 0) sb.Append("; ");
                    sb.Append(p.Key).Append(':').Append(p.Type.Name);
                    var a = p.Attr;
                    if (a.Required) sb.Append(" required");
                    if (a.HasMin)  sb.Append(" min=").Append(a.Min.ToString(inv));
                    if (a.HasMax)  sb.Append(" max=").Append(a.Max.ToString(inv));
                    if (a.HasStep) sb.Append(" step=").Append(a.Step.ToString(inv));
                    if (a.IsMeshRef) sb.Append(" meshRef");
                    if (!string.IsNullOrEmpty(a.Description)) sb.Append(' ').Append(a.Description);
                }
                prms.Add(sb.ToString());
            }

            return CommandDataJson.New()
                .Texts("kinds",         kinds)
                .Texts("displayNames",  names)
                .Texts("categories",    cats)
                .Texts("changes",       chg)
                .Ints ("positionsPath", pos)
                .Ints ("dragFollow",    follow)
                .Ints ("pinnedLast",    pinned)
                .Texts("descriptions",  descs)
                .Texts("params",        prms)
                .Texts("scopeKeys",     new List<string>
                {
                    MeshModifierScope.KeyKind, MeshModifierScope.KeyVertexIds, MeshModifierScope.KeyEdgeVertexIds,
                    MeshModifierScope.KeyFaceVertexIds, MeshModifierScope.KeyFaceSizes, MeshModifierScope.KeyPartsSet,
                    MeshModifierScope.KeyInvert,
                })
                .Texts("problems",      new List<string>(MeshModifierRegistry.Problems))
                .Build();
        }
    }
}
