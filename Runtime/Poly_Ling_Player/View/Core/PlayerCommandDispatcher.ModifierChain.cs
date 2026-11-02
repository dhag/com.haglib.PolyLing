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
                IsVisible  = true,
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

        private string ExecuteApplyMeshModifierChain(ProjectContext project, ModelContext model, ApplyMeshModifierChainCommand c)
        {
            if (!TryFindChain(model, c.StackName, out var g, out string cErr)) return cErr;
            ChainEnds(model, g, out _, out var result);

            _undoController?.SetModelContext(model);
            var listBefore = MeshFilterToSkinnedRecord.CaptureList(model);
            int gi = model.ModifierStacks.IndexOf(g);
            var removed = new ModifierStackChangeRecord { RemovedGroup = g.Clone(), RemovedIndex = gi };

            _undoController?.SuspendRecording();
            try
            {
                // DeleteModifierStack（解除）と同じ：処理器が結果のロックと元メッシュの塗りを戻す。
                ModifierEvaluator.ReleaseGroup(model, g);
                model.RemoveModifierStack(g);
            }
            finally
            {
                _undoController?.ResumeRecording();
            }

            FinishGroupEdit(project, model);
            RecordSubdivisionUndo(model, listBefore, model.ModifierStackCount,
                new List<ModifierStackChangeRecord> { removed }, $"処理列の適用: {g.Name}");

            int ri = result != null ? model.MeshContextList.IndexOf(result) : -1;
            ReportData(CommandDataJson.New().Int("resultIndex", ri).Build(),
                ri >= 0 ? new[] { ri } : null,
                result != null && result.ObjectId != 0UL ? new[] { result.ObjectId } : null);
            return null;
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
                })
                .Texts("problems",      new List<string>(MeshModifierRegistry.Problems))
                .Build();
        }
    }
}
