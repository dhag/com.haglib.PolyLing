// PlayerCommandDispatcher.EdgePipe.cs
// コマンドディスパッチャ：辺のパイプ化（CreateEdgePipeCommand）と、辺を選択辞書へ控える補助。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【手順】
//   1. 辺の辞書を決める（辞書名が空なら選択辺を新しい辞書へ保存）
//   2. EdgeRibbonFaceCommand（開始タグ付き・辞書名・新規オブジェクト・モディファイアスタックとして残す）
//   3. 帯を梯子として自動検索（BeltAcquire / AutoLadder）
//   4. CreatePipeCommand（梯子の取り込み元＝帯・AutoLadder・モディファイアスタックとして残す）
//   5. 2 と 4 のスタックを MergeModifierStackCommand で 1 つにまとめる
//   6. 帯を隠す（SetBatchVisibilityCommand）
//
// 【スタックに残るのは 2 と 4 の単体コマンド】
//   このコマンド自体はスタックに残さない。作り直しでは帯を書き戻してから
//   RefreshBelts が帯から梯子を取り直すので、元の辺を動かせばパイプが追随する。
//
// 【Undo はまとめて 1 件】
//   内側の記録は止め、終わってから MeshList のスナップショットと
//   スタックの追加を 1 つの操作として積む（RebuildModifierStackCommand と同じ形）。
//   辞書の追加もスナップショットに含まれる（MeshContextCloneOps が StateSnapshot で辞書を写す）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Ops;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>選択辺を保存する辞書の名前の元。</summary>
        private const string EdgeSetBaseName = "EdgeRibbonEdges";

        /// <summary>選択頂点を保存する辞書の名前の元。</summary>
        private const string VertexSetBaseName = "VertexBillboardVertices";

        /// <summary>
        /// DispatchCore の分担：辺のパイプ化。
        /// 該当するコマンドなら処理して true を返す。
        /// </summary>
        private bool DispatchEdgePipe(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case CreateSweepMeshCommand c:
                {
                    // モデルが無くても作れる（Maya の Sweep Mesh と同じ。図形生成と同じくプロジェクトを用意する）。
                    int swModel = c.ModelIndex;
                    string swWhy = EnsureModelForAssembly(ref project, ref model, ref swModel);
                    if (swWhy != null) { Fail(swWhy); return true; }
                    if (OnEdgeRibbonFace == null) { Fail("edge ribbon face handler not wired"); return true; }
                    if (OnCreatePrimitiveMesh == null) { Fail("primitive mesh handler not wired"); return true; }
                    string reason = ExecuteCreateSweep(project, model, c);
                    if (reason != null) Fail(reason);
                    return true;
                }

                case AddSpringChainFromHistoryCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    string chainWhy = ExecuteAddSpringChainFromHistory(project, model, c);
                    if (chainWhy != null) Fail(chainWhy);
                    return true;
                }

                case CreateEdgePipeCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    if (OnEdgeRibbonFace == null) { Fail("edge ribbon face handler not wired"); return true; }
                    if (OnCreatePrimitiveMesh == null) { Fail("primitive mesh handler not wired"); return true; }

                    if (c.Placement.AddMode != PrimitiveAddMode.NewObject &&
                        c.Placement.AddMode != PrimitiveAddMode.AddToExisting)
                    { Fail($"追加先モード {c.Placement.AddMode} は使えません。新規オブジェクトか既存へ追加にしてください"); return true; }

                    if (c.Profile == null || c.Profile.Length < 2)
                    { Fail("断面プロファイルの点が足りません"); return true; }

                    if (string.IsNullOrEmpty(c.EdgeSetName) &&
                        !PlayerCommandTargets.MatchesSelectedDrawables(model, c.MasterIndices, out string epSelReason))
                    { Fail(epSelReason); return true; }

                    _undoController?.SetModelContext(model);
                    var epListBefore  = MeshFilterToSkinnedRecord.CaptureList(model);
                    int epGroupsBefore = model.ModifierStackCount;

                    string epFail;
                    string epGroupName = null;
                    string epSetName   = null;
                    int    epLadders   = 0;
                    int    epPipeIndex = -1;

                    _undoController?.SuspendRecording();
                    try
                    {
                        epFail = RunEdgePipe(
                            project, model, c,
                            out epGroupName, out epSetName, out epLadders, out epPipeIndex);
                    }
                    finally
                    {
                        _undoController?.ResumeRecording();
                    }

                    // 途中で落ちても記録は積む。そこまでの変更を Undo で戻せるようにする。
                    RecordEdgeOperationUndo(model, epListBefore, epGroupsBefore, "辺をパイプにする");

                    model.IsDirty = true;
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);

                    if (epFail != null) { Fail(epFail); return true; }

                    var epPipeCtx = epPipeIndex >= 0 ? model.GetMeshContext(epPipeIndex) : null;
                    ReportData(
                        CommandDataJson.New()
                            .Text("groupName",   epGroupName ?? "")
                            .Int ("ladders",     epLadders)
                            .Text("edgeSetName", epSetName ?? "")
                            .Build(),
                        epPipeCtx != null ? new[] { epPipeIndex } : null,
                        epPipeCtx != null && epPipeCtx.ObjectId != 0UL ? new[] { epPipeCtx.ObjectId } : null);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 辺のパイプ化の本体。Undo の記録は呼び出し側が持つ。
        /// </summary>
        /// <returns>失敗理由。成功時は null。</returns>
        private string RunEdgePipe(
            ProjectContext project, ModelContext model, CreateEdgePipeCommand c,
            out string groupName, out string setName, out int ladders, out int pipeIndex)
        {
            groupName = null;
            setName   = null;
            ladders   = 0;
            pipeIndex = -1;

            // ── 1. 辺の辞書 ──
            if (string.IsNullOrEmpty(c.EdgeSetName))
            {
                if (!TryCreateEdgeSelectionSets(model, c.MasterIndices, out setName, out string setReason))
                    return setReason;
            }
            else
            {
                setName = c.EdgeSetName;
            }

            // ── 2. 帯（開始タグ付き）──
            // 帯は隠す中間物なので必ず新規オブジェクトにする。頂点はワールド座標なので姿勢は入れない。
            var ribbonPl = c.Placement;
            ribbonPl.AddMode        = PrimitiveAddMode.NewObject;
            ribbonPl.AddTargetIndex = -1;
            ribbonPl.WorldPosition  = Vector3.zero;
            ribbonPl.PlaceRotation  = Vector3.zero;
            ribbonPl.PlaceScale     = Vector3.one;
            ribbonPl.KeepAsModifierStack    = true;

            int groupsBeforeRibbon = model.ModifierStackCount;

            // 開いた連なりは開始タグを付けて梯子で読み、輪だけの辺はタグを付けられないので
            // 閉じた帯のまま円環で読む（EdgeRibbonFaceOps の【梯子タグ】）。
            bool ringOnly = AllEdgeChainsClosed(model, c.MasterIndices, setName);
            var acquireMethod = ringOnly ? BeltAcquireMethod.AutoRing : BeltAcquireMethod.AutoLadder;

            var ribbonResult = Dispatch(new EdgeRibbonFaceCommand(
                c.ModelIndex, c.MasterIndices, c.WidthWorld, ribbonPl, c.ObjectIds,
                !ringOnly, !ringOnly && c.AddEndTag, setName));
            if (ribbonResult != null && !ribbonResult.Success)
                return $"帯面を作れませんでした: {ribbonResult.Reason}";

            int ribbonIndex = (ribbonResult?.MasterIndices != null && ribbonResult.MasterIndices.Length > 0)
                ? ribbonResult.MasterIndices[0] : -1;
            var ribbonCtx = model.GetMeshContext(ribbonIndex);
            if (ribbonCtx?.MeshObject == null) return "作った帯面の描画オブジェクトを引けません";

            var ribbonGroup = FindGroupWithOutput(model, groupsBeforeRibbon, ribbonCtx.ObjectId);
            if (ribbonGroup == null) return "帯面のモディファイアスタックが作られていません";

            // ── 3. 帯を梯子として取り込む ──
            var acquired = BeltAcquire.Acquire(ribbonCtx, acquireMethod, false, "");
            if (!acquired.Ok) return $"帯から梯子を取り込めませんでした: {acquired.Message}";
            ladders = acquired.Belts.Count;

            CreateBeltPrimitiveCommand.SplitBelts(
                acquired.Belts.ToArray(),
                out var left, out var right, out var starts,
                out var closed, out var flip, out var height);

            // ── 4. パイプ ──
            // 梯子の点は帯のローカル座標（＝ワールド）なので姿勢は入れない。ピボットも使わない。
            var pipePl = c.Placement;
            pipePl.WorldPosition = Vector3.zero;
            pipePl.PlaceRotation = Vector3.zero;
            pipePl.PlaceScale    = Vector3.one;
            pipePl.KeepAsModifierStack   = true;

            var pipeParams = c.Params;
            pipeParams.Pivot = Vector3.zero;

            int groupsBeforePipe = model.ModifierStackCount;

            var pipeResult = Dispatch(new CreatePipeCommand(
                c.ModelIndex, pipeParams, c.Profile, c.ProfileClosed,
                left, right, starts, closed, flip, height,
                c.Orient, c.Spline, pipePl,
                model.MeshContextList.IndexOf(ribbonCtx),
                acquireMethod, false, ""));
            if (pipeResult != null && !pipeResult.Success)
                return $"パイプを作れませんでした: {pipeResult.Reason}";

            pipeIndex = (pipeResult?.MasterIndices != null && pipeResult.MasterIndices.Length > 0)
                ? pipeResult.MasterIndices[0] : -1;

            if (model.ModifierStackCount <= groupsBeforePipe)
                return "パイプのモディファイアスタックが作られていません";
            var pipeGroup = model.ModifierStacks[model.ModifierStackCount - 1];

            // ── 5. 1 つのスタックにまとめる ──
            var mergeResult = Dispatch(new MergeModifierStackCommand(
                c.ModelIndex, ribbonGroup.Name, pipeGroup.Name));
            if (mergeResult != null && !mergeResult.Success)
                return $"スタックをまとめられませんでした: {mergeResult.Reason}";
            groupName = ribbonGroup.Name;

            // ── 6. 帯を隠す ──
            int ribbonNow = model.MeshContextList.IndexOf(ribbonCtx);
            if (ribbonNow < 0) return "帯面の描画オブジェクトを引けません";

            var hideResult = Dispatch(new SetBatchVisibilityCommand(
                c.ModelIndex, new[] { ribbonNow }, false));
            if (hideResult != null && !hideResult.Success)
                return $"帯面を隠せませんでした: {hideResult.Reason}";

            // 帯とカーブは形を決める材料で、書き出すのはパイプだけ（Maya のカーブと同じ）。
            // PMX は隠したものも書き出すので、印を立てないと帯が板のままパイプと重なって出る。
            ribbonCtx.ExcludeFromExport = true;
            foreach (int src in c.MasterIndices ?? Array.Empty<int>())
            {
                var srcCtx = model.GetMeshContext(src);
                if (srcCtx != null && IsCurveObject(srcCtx.MeshObject)) srcCtx.ExcludeFromExport = true;
            }

            return null;
        }

        /// <summary>辞書の辺が全部、閉じた連なりか。</summary>
        private static bool AllEdgeChainsClosed(ModelContext model, int[] masterIndices, string setName)
        {
            bool any = false;
            foreach (int idx in masterIndices ?? Array.Empty<int>())
            {
                var edges = EdgeRibbonFaceTool.ResolveEdges(model.GetMeshContext(idx), setName);
                if (edges == null || edges.Count == 0) continue;
                foreach (var chain in LineGroupEditOps.SplitChains(edges))
                {
                    if (!chain.Closed) return false;
                    any = true;
                }
            }
            return any;
        }

        /// <summary>
        /// 記録を止めて行った辺の操作（辞書の追加・帯・パイプ・スタック）を Undo 1 件として積む。
        /// MeshList のスナップショット（辞書を含む）と、groupsBefore 以降に足されたスタックの追加を
        /// 1 つの操作にまとめる。
        /// </summary>
        private void RecordEdgeOperationUndo(
            ModelContext model, List<MeshContext> listBefore, int groupsBefore, string description)
        {
            if (_undoController == null || model == null) return;

            _undoController.MeshListStack.BeginGroup(description);

            RecordMeshListSnapshot(listBefore, model, description);

            for (int gi = groupsBefore; gi < model.ModifierStackCount; gi++)
            {
                var added = model.ModifierStacks[gi];
                if (added == null) continue;
                RecordModifierStackUndo(
                    new ModifierStackChangeRecord
                    {
                        AddedGroup = added.Clone(),
                        AddedIndex = gi,
                    },
                    $"モディファイアスタック追加: {added.Name}");
            }

            _undoController.MeshListStack.EndGroup();
        }

        /// <summary>fromIndex 以降に足されたスタックのうち、objectId を出力に持つもの。</summary>
        private static Poly_Ling.Data.ModifierStack FindGroupWithOutput(
            ModelContext model, int fromIndex, ulong objectId)
        {
            if (model?.ModifierStacks == null || objectId == 0UL) return null;
            for (int i = Mathf.Max(0, fromIndex); i < model.ModifierStacks.Count; i++)
            {
                var g = model.ModifierStacks[i];
                if (g != null && g.ContainsOutput(objectId)) return g;
            }
            return null;
        }

        /// <summary>
        /// 対象それぞれの選択辺を、全対象で同じ名前のパーツ選択辞書へ保存する。
        /// 選択辺を持たない対象には作らない。
        /// </summary>
        private static bool TryCreateEdgeSelectionSets(
            ModelContext model, int[] masterIndices, out string setName, out string reason)
            => TryCreateSelectionSets(
                model, masterIndices, EdgeSetBaseName, "選択辺がありません",
                mc => mc.Selection?.Edges != null && mc.Selection.Edges.Count > 0,
                (mc, name) => PartsSelectionSet.FromCurrentSelection(
                    name, null, new HashSet<VertexPair>(mc.Selection.Edges),
                    null, null, MeshSelectMode.Edge),
                out setName, out reason);

        /// <summary>
        /// 対象それぞれの選択頂点を、全対象で同じ名前のパーツ選択辞書へ保存する。
        /// 選択頂点を持たない対象には作らない。
        /// </summary>
        private static bool TryCreateVertexSelectionSets(
            ModelContext model, int[] masterIndices, out string setName, out string reason)
            => TryCreateSelectionSets(
                model, masterIndices, VertexSetBaseName, "選択頂点がありません",
                mc => mc.Selection?.Vertices != null && mc.Selection.Vertices.Count > 0,
                (mc, name) => PartsSelectionSet.FromCurrentSelection(
                    name, new HashSet<int>(mc.Selection.Vertices), null,
                    null, null, MeshSelectMode.Vertex),
                out setName, out reason);

        /// <summary>
        /// 対象それぞれへ、全対象で同じ名前のパーツ選択辞書を足す。
        /// 名前は baseName（重複時は _2, _3 …）のうち、どの対象にも無いものを選ぶ。
        /// hasElements が false の対象には作らない。
        /// 索引がずれたときに引き直せるよう、識別子も控える（PartsSelectionSet.CaptureIds）。
        /// 失敗したときは何も足していない。
        /// </summary>
        private static bool TryCreateSelectionSets(
            ModelContext model, int[] masterIndices, string baseName, string emptyReason,
            Func<MeshContext, bool> hasElements,
            Func<MeshContext, string, PartsSelectionSet> makeSet,
            out string setName, out string reason)
        {
            setName = null;
            reason  = null;

            if (model == null) { reason = "モデルがありません"; return false; }
            if (masterIndices == null || masterIndices.Length == 0)
            { reason = "MasterIndices が空です"; return false; }

            var targets = new List<MeshContext>();
            foreach (int idx in masterIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;
                if (!hasElements(mc)) continue;
                targets.Add(mc);
            }

            if (targets.Count == 0) { reason = emptyReason; return false; }

            for (int n = 1; n < 100000; n++)
            {
                string candidate = n == 1 ? baseName : $"{baseName}_{n}";

                bool used = false;
                foreach (var mc in targets)
                    if (mc.FindSelectionSetByName(candidate) != null) { used = true; break; }

                if (!used) { setName = candidate; break; }
            }

            if (setName == null) { reason = "辞書の名前を決められません"; return false; }

            foreach (var mc in targets)
            {
                var set = makeSet(mc, setName);
                set.CaptureIds(mc.MeshObject);
                mc.PartsSelectionSetList.Add(set);
            }

            return true;
        }
    }
}
