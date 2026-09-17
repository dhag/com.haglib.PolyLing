// PlayerCommandDispatcher.EdgePipe.cs
// コマンドディスパッチャ：辺のパイプ化（CreateEdgePipeCommand）と、辺を選択辞書へ控える補助。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【手順】
//   1. 辺の辞書を決める（辞書名が空なら選択辺を新しい辞書へ保存）
//   2. EdgeRibbonFaceCommand（開始タグ付き・辞書名・新規オブジェクト・グループとして残す）
//   3. 帯を梯子として自動検索（BeltAcquire / AutoLadder）
//   4. CreatePipeCommand（梯子の取り込み元＝帯・AutoLadder・グループとして残す）
//   5. 2 と 4 のグループを MergeObjectGroupCommand で 1 つにまとめる
//   6. 帯を隠す（SetBatchVisibilityCommand）
//
// 【グループに残るのは 2 と 4 の単体コマンド】
//   このコマンド自体はグループに残さない。作り直しでは帯を書き戻してから
//   RefreshBelts が帯から梯子を取り直すので、元の辺を動かせばパイプが追随する。
//
// 【Undo はまとめて 1 件】
//   内側の記録は止め、終わってから MeshList のスナップショットと
//   グループの追加を 1 つの操作として積む（RebuildObjectGroupCommand と同じ形）。
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

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>選択辺を保存する辞書の名前の元。</summary>
        private const string EdgeSetBaseName = "EdgeRibbonEdges";

        /// <summary>
        /// DispatchCore の分担：辺のパイプ化。
        /// 該当するコマンドなら処理して true を返す。
        /// </summary>
        private bool DispatchEdgePipe(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case CreateEdgePipeCommand c:
                {
                    if (model == null || project == null) { Fail("no current model"); return true; }
                    if (OnEdgeRibbonFace == null) { Fail("edge ribbon face handler not wired"); return true; }
                    if (OnCreatePrimitiveMesh == null) { Fail("primitive mesh handler not wired"); return true; }

                    if (!c.AddStartTag)
                    { Fail("パイプにするには開始タグが要ります（梯子の自動検索の起点）"); return true; }

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
                    int epGroupsBefore = model.ObjectGroupCount;

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
            ribbonPl.KeepAsGroup    = true;

            int groupsBeforeRibbon = model.ObjectGroupCount;

            var ribbonResult = Dispatch(new EdgeRibbonFaceCommand(
                c.ModelIndex, c.MasterIndices, c.WidthWorld, ribbonPl, c.ObjectIds,
                true, c.AddEndTag, setName));
            if (ribbonResult != null && !ribbonResult.Success)
                return $"帯面を作れませんでした: {ribbonResult.Reason}";

            int ribbonIndex = (ribbonResult?.MasterIndices != null && ribbonResult.MasterIndices.Length > 0)
                ? ribbonResult.MasterIndices[0] : -1;
            var ribbonCtx = model.GetMeshContext(ribbonIndex);
            if (ribbonCtx?.MeshObject == null) return "作った帯面の描画オブジェクトを引けません";

            var ribbonGroup = FindGroupWithOutput(model, groupsBeforeRibbon, ribbonCtx.ObjectId);
            if (ribbonGroup == null) return "帯面のオブジェクトグループが作られていません";

            // ── 3. 帯を梯子として取り込む ──
            var acquired = BeltAcquire.Acquire(ribbonCtx, BeltAcquireMethod.AutoLadder, false, "");
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
            pipePl.KeepAsGroup   = true;

            var pipeParams = c.Params;
            pipeParams.Pivot = Vector3.zero;

            int groupsBeforePipe = model.ObjectGroupCount;

            var pipeResult = Dispatch(new CreatePipeCommand(
                c.ModelIndex, pipeParams, c.Profile, c.ProfileClosed,
                left, right, starts, closed, flip, height,
                c.Orient, c.Spline, pipePl,
                model.MeshContextList.IndexOf(ribbonCtx),
                BeltAcquireMethod.AutoLadder, false, ""));
            if (pipeResult != null && !pipeResult.Success)
                return $"パイプを作れませんでした: {pipeResult.Reason}";

            pipeIndex = (pipeResult?.MasterIndices != null && pipeResult.MasterIndices.Length > 0)
                ? pipeResult.MasterIndices[0] : -1;

            if (model.ObjectGroupCount <= groupsBeforePipe)
                return "パイプのオブジェクトグループが作られていません";
            var pipeGroup = model.ObjectGroups[model.ObjectGroupCount - 1];

            // ── 5. 1 つのグループにまとめる ──
            var mergeResult = Dispatch(new MergeObjectGroupCommand(
                c.ModelIndex, ribbonGroup.Name, pipeGroup.Name));
            if (mergeResult != null && !mergeResult.Success)
                return $"グループをまとめられませんでした: {mergeResult.Reason}";
            groupName = ribbonGroup.Name;

            // ── 6. 帯を隠す ──
            int ribbonNow = model.MeshContextList.IndexOf(ribbonCtx);
            if (ribbonNow < 0) return "帯面の描画オブジェクトを引けません";

            var hideResult = Dispatch(new SetBatchVisibilityCommand(
                c.ModelIndex, new[] { ribbonNow }, false));
            if (hideResult != null && !hideResult.Success)
                return $"帯面を隠せませんでした: {hideResult.Reason}";

            return null;
        }

        /// <summary>
        /// 記録を止めて行った辺の操作（辞書の追加・帯・パイプ・グループ）を Undo 1 件として積む。
        /// MeshList のスナップショット（辞書を含む）と、groupsBefore 以降に足されたグループの追加を
        /// 1 つの操作にまとめる。
        /// </summary>
        private void RecordEdgeOperationUndo(
            ModelContext model, List<MeshContext> listBefore, int groupsBefore, string description)
        {
            if (_undoController == null || model == null) return;

            _undoController.MeshListStack.BeginGroup(description);

            RecordMeshListSnapshot(listBefore, model, description);

            for (int gi = groupsBefore; gi < model.ObjectGroupCount; gi++)
            {
                var added = model.ObjectGroups[gi];
                if (added == null) continue;
                RecordObjectGroupUndo(
                    new ObjectGroupChangeRecord
                    {
                        AddedGroup = added.Clone(),
                        AddedIndex = gi,
                    },
                    $"オブジェクトグループ追加: {added.Name}");
            }

            _undoController.MeshListStack.EndGroup();
        }

        /// <summary>fromIndex 以降に足されたグループのうち、objectId を出力に持つもの。</summary>
        private static Poly_Ling.Data.ObjectGroup FindGroupWithOutput(
            ModelContext model, int fromIndex, ulong objectId)
        {
            if (model?.ObjectGroups == null || objectId == 0UL) return null;
            for (int i = Mathf.Max(0, fromIndex); i < model.ObjectGroups.Count; i++)
            {
                var g = model.ObjectGroups[i];
                if (g != null && g.ContainsOutput(objectId)) return g;
            }
            return null;
        }

        /// <summary>
        /// 対象それぞれの選択辺を、全対象で同じ名前のパーツ選択辞書へ保存する。
        /// 名前はどの対象にも無いものを選ぶ。選択辺を持たない対象には作らない。
        /// 索引がずれたときに引き直せるよう、識別子も控える（PartsSelectionSet.CaptureIds）。
        /// </summary>
        private static bool TryCreateEdgeSelectionSets(
            ModelContext model, int[] masterIndices, out string setName, out string reason)
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
                if (mc.Selection?.Edges == null || mc.Selection.Edges.Count == 0) continue;
                targets.Add(mc);
            }

            if (targets.Count == 0) { reason = "選択辺がありません"; return false; }

            for (int n = 1; n < 100000; n++)
            {
                string candidate = n == 1 ? EdgeSetBaseName : $"{EdgeSetBaseName}_{n}";

                bool used = false;
                foreach (var mc in targets)
                    if (mc.FindSelectionSetByName(candidate) != null) { used = true; break; }

                if (!used) { setName = candidate; break; }
            }

            if (setName == null) { reason = "辞書の名前を決められません"; return false; }

            foreach (var mc in targets)
            {
                var set = PartsSelectionSet.FromCurrentSelection(
                    setName, null, new HashSet<VertexPair>(mc.Selection.Edges),
                    null, null, MeshSelectMode.Edge);
                set.CaptureIds(mc.MeshObject);
                mc.PartsSelectionSetList.Add(set);
            }

            return true;
        }
    }
}
