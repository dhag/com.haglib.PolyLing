// PlayerCommandDispatcher.NormalEdit.cs
// コマンドディスパッチャ：法線編集。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectPose;
using Poly_Ling.Ops;
using Poly_Ling.UI;
using Poly_Ling.Diagnostics;
using Poly_Ling.Serialization;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// DispatchCore の分担：法線編集。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchNormalEdit(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case RepairVertexIdsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var idTargets = CollectSelectedMeshContexts(model);
                    if (idTargets.Count == 0) { Fail("対象がありません"); return true; }

                    int totalChanged = 0;
                    foreach (var mc in idTargets)
                    {
                        if (mc?.MeshObject == null) continue;

                        // Undo はメッシュごとに記録する。MeshObjectSnapshot は
                        // MeshObject.Clone() を保持し、Vertex.Clone() が Id を
                        // 引き継ぐため、ID の変更もそのまま復元できる。
                        if (_undoController != null)
                        {
                            _undoController.SetMeshObject(mc.MeshObject, mc.UnityMesh);
                            _undoController.MeshUndoContext.ParentModelContext = model;
                        }
                        var idBefore = _undoController?.CaptureMeshObjectSnapshot();

                        int changed = c.Mode switch
                        {
                            RepairVertexIdsCommand.RepairMode.AssignMissing      => VertexIdOps.AssignMissing(mc),
                            RepairVertexIdsCommand.RepairMode.ResolveDuplicates  => VertexIdOps.ResolveDuplicates(mc),
                            RepairVertexIdsCommand.RepairMode.ReassignSequential => VertexIdOps.ReassignSequential(mc),
                            RepairVertexIdsCommand.RepairMode.ClearAll           => VertexIdOps.ClearAll(mc),
                            _ => 0,
                        };
                        totalChanged += changed;

                        if (changed > 0 && _undoController != null && idBefore != null)
                        {
                            var idAfter = _undoController.CaptureMeshObjectSnapshot();
                            _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                                _undoController, idBefore, idAfter, $"Repair Vertex Ids ({c.Mode})"));
                        }
                    }

                    // 頂点IDは描画に影響しないので GPU 再構築は不要。
                    // パネル表示（診断結果）だけ更新させる。
                    if (totalChanged > 0) _notifyPanels(ChangeKind.Attributes);
                    Debug.Log($"[VertexId] {c.Mode}: {idTargets.Count} オブジェクト / {totalChanged} 頂点");
                    return true;
                }

                case AssignPartsIdsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var partsMc = model.GetMeshContext(c.TargetMasterIndex);
                    if (partsMc?.MeshObject == null)
                    {
                        Debug.LogWarning(
                            $"[PartsId] 対象メッシュが見つかりません masterIndex={c.TargetMasterIndex}");
                        LastPartsIdResult = PartsIdAssignResult.Fail("対象メッシュが見つかりません");
                        return true;
                    }
                    var partsMo = partsMc.MeshObject;

                    // リファレンスは「1 パーツの頂点数」を取るためだけに読む。書き換えない。
                    int perPart = 0;
                    if (c.Mode == AssignPartsIdsCommand.PartsIdMode.ReferenceVertexCount)
                    {
                        var refMc = model.GetMeshContext(c.ReferenceMasterIndex);
                        if (refMc?.MeshObject == null || refMc.MeshObject.VertexCount == 0)
                        {
                            Debug.LogWarning(
                                $"[PartsId] リファレンスが見つかりません masterIndex={c.ReferenceMasterIndex}");
                            LastPartsIdResult = PartsIdAssignResult.Fail("リファレンスが見つかりません");
                            return true;
                        }
                        if (ReferenceEquals(refMc, partsMc))
                        {
                            Debug.LogWarning("[PartsId] リファレンスに対象と同じオブジェクトは指定できません");
                            LastPartsIdResult =
                                PartsIdAssignResult.Fail("リファレンスに対象と同じオブジェクトは指定できません");
                            return true;
                        }
                        perPart = refMc.MeshObject.VertexCount;
                    }

                    // Undo は頂点ID修復と同じ MeshObjectSnapshot 方式。
                    // Vertex.Clone() が PartsId / SubId を引き継ぐ（MeshObject.cs:313-314）ので
                    // スナップショットで元に戻せる。
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(partsMo, partsMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var partsBefore = _undoController?.CaptureMeshObjectSnapshot();

                    PartsIdAssignResult partsResult;
                    switch (c.Mode)
                    {
                        case AssignPartsIdsCommand.PartsIdMode.Connectivity:
                            partsResult = PartsIdAssignOps.AssignByConnectivity(partsMo, c.IsolatedPolicy);
                            break;
                        case AssignPartsIdsCommand.PartsIdMode.ReferenceVertexCount:
                            partsResult = PartsIdAssignOps.AssignByVertexCount(partsMo, perPart);
                            break;
                        case AssignPartsIdsCommand.PartsIdMode.SubIdOnly:
                            partsResult = PartsIdAssignOps.AssignSubIdOnly(partsMo);
                            break;
                        case AssignPartsIdsCommand.PartsIdMode.Clear:
                            partsResult = PartsIdAssignOps.Clear(partsMo);
                            break;
                        default:
                            LastPartsIdResult = PartsIdAssignResult.Fail("未知の採番モードです");
                            return true;
                    }

                    if (!partsResult.Success)
                    {
                        Debug.LogWarning($"[PartsId] {c.Mode}: {partsResult.Reason}");
                        LastPartsIdResult = partsResult;
                        _notifyPanels(ChangeKind.Attributes);
                        return true;
                    }

                    if (_undoController != null && partsBefore != null)
                    {
                        var partsAfter = _undoController.CaptureMeshObjectSnapshot();
                        _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                            _undoController, partsBefore, partsAfter, $"Assign Parts Ids ({c.Mode})"));
                    }

                    LastPartsIdResult = partsResult;

                    // パーツID / サブIDは描画に影響しないので GPU 再構築は不要。
                    _notifyPanels(ChangeKind.Attributes);
                    Debug.Log(
                        $"[PartsId] {c.Mode}: \"{partsMc.Name}\" 頂点 {partsResult.VertexCount} / "
                      + $"パーツ {partsResult.PartCount} / 孤立頂点 {partsResult.IsolatedVertexCount}");
                    return true;
                }

                case AssignPartsIdsByBoneWeightCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var bwMc = model.GetMeshContext(c.TargetMasterIndex);
                    if (bwMc?.MeshObject == null)
                    {
                        Debug.LogWarning(
                            $"[PartsId] 対象メッシュが見つかりません masterIndex={c.TargetMasterIndex}");
                        LastPartsIdByBoneWeightResult =
                            PartsIdByBoneWeightResult.Fail("対象メッシュが見つかりません");
                        return true;
                    }
                    var bwMo = bwMc.MeshObject;

                    // ボーン索引の定義域はモデルの MeshContextList の長さ
                    // （MeshObject.cs:123「boneIndex = _meshContextList のインデックス」）。
                    // 群の番号をボーン索引と衝突しない位置から始めるために渡す。
                    int boneCount = model.MeshContextList?.Count ?? 0;

                    // Undo は AssignPartsIdsCommand と同じ MeshObjectSnapshot 方式。
                    // Vertex.Clone() が PartsId / SubId を引き継ぐ（MeshObject.cs:313-314）。
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(bwMo, bwMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var bwBefore = _undoController?.CaptureMeshObjectSnapshot();

                    var bwResult = PartsIdByBoneWeightOps.AssignByBoneWeight(bwMo, boneCount);

                    if (!bwResult.Success)
                    {
                        Debug.LogWarning($"[PartsId] ボーンウェイト採番: {bwResult.Reason}");
                        LastPartsIdByBoneWeightResult = bwResult;
                        _notifyPanels(ChangeKind.Attributes);
                        return true;
                    }

                    if (_undoController != null && bwBefore != null)
                    {
                        var bwAfter = _undoController.CaptureMeshObjectSnapshot();
                        _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                            _undoController, bwBefore, bwAfter, "Assign Parts Ids (BoneWeight)"));
                    }

                    LastPartsIdByBoneWeightResult = bwResult;

                    // パーツID / サブIDは描画に影響しないので GPU 再構築は不要。
                    _notifyPanels(ChangeKind.Attributes);
                    Debug.Log($"[PartsId] ボーンウェイト採番: \"{bwMc.Name}\" {bwResult.Summary}");
                    return true;
                }

                case SplitObjectByPartsIdCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnSplitObjectByPartsId == null)
                    { Fail("parts id split handler not wired"); return true; }

                    string splitReason = OnSplitObjectByPartsId.Invoke(c);
                    if (splitReason != null) { Fail(splitReason); return true; }
                    return true;
                }

                case TransferVertexDataCommand c:
                {
                    var srcModel = project?.GetModel(c.SourceModelIndex);
                    var dstModel = project?.GetModel(c.TargetModelIndex);
                    if (srcModel == null || dstModel == null) { Fail("転送元か転送先のモデルがありません"); return true; }
                    if (c.SourceMeshIndices == null || c.TargetMeshIndices == null) { Fail("転送元と転送先を指定してください"); return true; }

                    int pairCount = Math.Min(c.SourceMeshIndices.Length, c.TargetMeshIndices.Length);
                    if (pairCount == 0) { Fail("転送できる組がありません"); return true; }

                    int totalWritten = 0;
                    var syncedTargets = new List<MeshContext>();
                    for (int p = 0; p < pairCount; p++)
                    {
                        var srcMc = srcModel.GetMeshContext(c.SourceMeshIndices[p]);
                        var dstMc = dstModel.GetMeshContext(c.TargetMeshIndices[p]);
                        if (srcMc?.MeshObject == null || dstMc?.MeshObject == null) continue;

                        // Undo は転送先メッシュごとに記録する。頂点数・面数は変えないが、
                        // 位置 / UV / ウェイト / ID などを書き換えるため MeshObject
                        // 丸ごとのスナップショットで戻せるようにする。
                        if (_undoController != null)
                        {
                            _undoController.SetMeshObject(dstMc.MeshObject, dstMc.UnityMesh);
                            _undoController.MeshUndoContext.ParentModelContext = dstModel;
                        }
                        var tvBefore = _undoController?.CaptureMeshObjectSnapshot();

                        var r = VertexDataTransferOps.Transfer(
                            srcModel, srcMc, dstModel, dstMc, c.MatchMode, c.Kinds);
                        totalWritten += r.Written;

                        foreach (var w in r.Warnings)
                            Debug.LogWarning($"[VertexTransfer] {r.SourceName} → {r.TargetName}: {w}");
                        Debug.Log($"[VertexTransfer] {r.Summary}");

                        if (r.Written > 0)
                        {
                            syncedTargets.Add(dstMc);
                            if (_undoController != null && tvBefore != null)
                            {
                                var tvAfter = _undoController.CaptureMeshObjectSnapshot();
                                _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                                    _undoController, tvBefore, tvAfter, "Transfer Vertex Data"));
                            }
                        }
                    }

                    if (totalWritten > 0)
                    {
                        // ------------------------------------------------------------
                        // 描画更新は転送した項目に応じて段階を選ぶ。
                        // 以前は常に EnterTopologyChanged を呼んでいたが、これは
                        // RebuildAdapter（UnifiedSystemAdapter を Dispose して GPU
                        // ComputeBuffer を全再確保）を伴い、頂点数が変わらない転送には
                        // 過剰で実機で重かった。
                        //
                        //   UV / 法線 / ウェイト / フラグ … バッファ構築時に焼き込まれる
                        //     (UnifiedBufferManager_Build 参照)。差分更新の口が無いため
                        //     再構築が要る。
                        //   位置 … SyncMeshPositionsAndTransform で差分同期できる。
                        //   頂点ID / モーフ基準 / 選択辞書 … 描画に出ないので更新不要。
                        //
                        // また、転送先が CurrentModel でない場合は今の adapter が
                        // 別モデルのものなので更新しても無駄（かつ誤り）。モデル切替時に
                        // EnterSceneReset で作り直されるため、ここでは何もしない。
                        // ------------------------------------------------------------
                        bool targetIsCurrent = project != null
                            && project.CurrentModelIndex == c.TargetModelIndex;

                        const VertexDataKind rebuildKinds =
                              VertexDataKind.UVs
                            | VertexDataKind.Normals
                            | VertexDataKind.Flags
                            | VertexDataKind.BoneWeight
                            | VertexDataKind.MirrorBoneWeight;

                        bool needsRebuild  = (c.Kinds & rebuildKinds) != 0;
                        bool positionOnly  = !needsRebuild && c.Kinds.HasFlag(VertexDataKind.Position);

                        if (targetIsCurrent && needsRebuild)
                        {
                            _viewportManager.EnterTopologyChanged(project);
                        }
                        else if (targetIsCurrent && positionOnly)
                        {
                            // 書き換えたメッシュだけ位置を同期し、最後に一度だけ
                            // カリング再計算と再描画を行う。
                            foreach (var mc in syncedTargets)
                                _viewportManager.EnterVerticesMoved(
                                    project, VerticesMovedPhase.Dragging, mc);
                            _viewportManager.EnterVerticesMoved(project, VerticesMovedPhase.DragEnd);
                        }

                        _notifyPanels(ChangeKind.Attributes);
                    }
                    return true;
                }

                case SaveMeshSelSetsCsvCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (string.IsNullOrEmpty(c.FilePath)) { Fail("FilePath が空です"); return true; }
                    if (!Poly_Ling.Core.PLSandbox.TryResolveWrite(
                            c.FilePath, out string smsPath, out string smsSbReason))
                    { Fail(smsSbReason); return true; }
                    MeshSelSetCsvHelper.SaveToFile(model, smsPath);
                    return true;
                }

                case LoadMeshSelSetsCsvCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (string.IsNullOrEmpty(c.FilePath)) { Fail("FilePath が空です"); return true; }
                    if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                            c.FilePath, out string lmsPath, out string lmsSbReason))
                    { Fail(lmsSbReason); return true; }
                    if (MeshSelSetCsvHelper.LoadFromFile(model, lmsPath) > 0)
                        _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // Fail はコンソールへ出さず（Fail の定義を参照）、Dispatch の戻り値も
                // DispatchPanelCommand が捨てる。書き出し系はパネル側も
                // File.Exists でしか成否を見ないため、ここで出さないと理由が
                // どこにも残らない。ConvertUnityClipToVrmaCommand と同じ形にそろえる。
                case ExportVrmAnimationCommand c:
                {
                    if (model == null)
                    {
                        Fail("no current model");
                        Debug.LogError("[PolyLing] VRMA 書き出し: モデルがありません");
                        return true;
                    }
                    if (OnExportVrmAnimation == null)
                    {
                        Fail("vrm animation export handler not wired");
                        Debug.LogError("[PolyLing] VRMA 書き出し: 受け口が配線されていません");
                        return true;
                    }
                    string vaReason = OnExportVrmAnimation.Invoke(c);
                    if (vaReason != null)
                    {
                        Fail(vaReason);
                        Debug.LogError($"[PolyLing] VRMA 書き出しに失敗: {vaReason}");
                        return true;
                    }
                    return true;
                }

                case ExportVmdToVrmaCommand c:
                {
                    if (model == null)
                    {
                        Fail("no current model");
                        Debug.LogError("[PolyLing] VMD→VRMA: モデルがありません");
                        return true;
                    }
                    if (OnExportVmdToVrma == null)
                    {
                        Fail("vmd to vrma export handler not wired");
                        Debug.LogError("[PolyLing] VMD→VRMA: 受け口が配線されていません");
                        return true;
                    }
                    string vvReason = OnExportVmdToVrma.Invoke(c);
                    if (vvReason != null)
                    {
                        Fail(vvReason);
                        Debug.LogError($"[PolyLing] VMD→VRMA 書き出しに失敗: {vvReason}");
                        return true;
                    }
                    return true;
                }

                // ── メッシュ選択辞書 ───────────────────────────────────────────
                case SaveSelectionDictionaryCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var sdCategory = c.Category switch
                    {
                        MeshCategory.Bone  => ModelContext.SelectionCategory.Bone,
                        MeshCategory.Morph => ModelContext.SelectionCategory.Morph,
                        _                  => ModelContext.SelectionCategory.Mesh,
                    };
                    string sdName = string.IsNullOrEmpty(c.SetName)
                        ? model.GenerateUniqueMeshSelectionSetName("MeshSet")
                        : c.SetName;
                    if (model.FindMeshSelectionSetByName(sdName) != null)
                        sdName = model.GenerateUniqueMeshSelectionSetName(sdName);
                    var sdSet = new MeshSelectionSet(sdName) { Category = sdCategory };
                    foreach (var n in c.MeshNames)
                        if (!string.IsNullOrEmpty(n) && !sdSet.MeshNames.Contains(n))
                            sdSet.MeshNames.Add(n);
                    model.MeshSelectionSets.Add(sdSet);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // 法線編集の実行
        // ================================================================
        // 対象範囲は NormalEditOps.CollectTargetCorners のルールに従う
        //   面選択がある → その面のコーナー / 頂点選択のみ → その頂点の全スロット
        //   選択が無い   → メッシュ全体
        // RecalcByAngle だけはスロットを作り直すのでメッシュ全体が対象。
        private static int ApplyNormalEdit(MeshContext mc, NormalEditCommand c)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return 0;

            if (c.Operation == NormalEditCommand.Op.RecalcByAngle)
            {
                NormalEditOps.RecalcByAngle(mo, c.AngleDeg, c.WeightMode);
                return mo.FaceCount;
            }

            var sel = mc.Selection;
            var corners = NormalEditOps.CollectTargetCorners(
                mo, sel?.Faces, sel?.Vertices);
            if (corners.Count == 0) return 0;

            switch (c.Operation)
            {
                case NormalEditCommand.Op.SetFromFaces:
                    return NormalEditOps.SetFromFaces(mo, corners);

                // 面法線だけを平均して1本にする。スロット数は変わらないため
                // slotCountMayChange には含めない。
                case NormalEditCommand.Op.AverageFromFaces:
                    return NormalEditOps.AverageFromFaces(mo, corners, c.WeightMode);

                case NormalEditCommand.Op.Unify:
                    return NormalEditOps.Unify(mo, corners, c.WeightMode);

                case NormalEditCommand.Op.Break:
                    return NormalEditOps.Break(mo, corners);

                case NormalEditCommand.Op.AverageAll:
                    return NormalEditOps.AverageAll(mo, corners);

                case NormalEditCommand.Op.Smooth:
                    return NormalEditOps.Smooth(mo, corners, c.Strength);

                case NormalEditCommand.Op.Sphereize:
                {
                    Vector3 center = c.UseSelectionCenter
                        ? NormalEditOps.CenterOf(mo, corners)
                        : c.Target;
                    return NormalEditOps.Sphereize(mo, corners, center);
                }

                case NormalEditCommand.Op.PointToTarget:
                    return NormalEditOps.PointToTarget(mo, corners, c.Target, c.AlignVectors);

                case NormalEditCommand.Op.AlignToAxis:
                {
                    Vector3 dir = c.Axis switch
                    {
                        0 => Vector3.right,
                        1 => Vector3.up,
                        _ => Vector3.forward,
                    };
                    if (c.Negative) dir = -dir;
                    return NormalEditOps.SetDirection(mo, corners, dir);
                }

                case NormalEditCommand.Op.FlattenOnAxis:
                    return NormalEditOps.FlattenOnAxis(mo, corners, c.Axis);

                // ミラー対応（X軸対称）。中央近傍の頂点だけ法線の X をゼロにする。
                // スロット数は変わらないため slotCountMayChange には含めない。
                case NormalEditCommand.Op.MirrorFlattenSeamX:
                    return NormalEditOps.FlattenMirrorSeamX(mo, corners, c.MirrorThreshold);

                case NormalEditCommand.Op.Flip:
                    return NormalEditOps.Flip(mo, corners);

                default:
                    return 0;
            }
        }
    }
}
