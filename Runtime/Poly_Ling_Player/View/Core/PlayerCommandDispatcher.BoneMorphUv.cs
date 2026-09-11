// PlayerCommandDispatcher.BoneMorphUv.cs
// コマンドディスパッチャ：BonePose・モーフ・BoneTransform・UV 展開・マテリアル。
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
        /// DispatchCore の分担：BonePose・モーフ・BoneTransform・UV 展開・マテリアル。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchBoneMorphUv(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── BonePose Active
                case SetBonePoseActiveCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;
                        // BonePoseData未初期化の場合、Active=trueで初期化する
                        if (ctx.BonePoseData == null && c.Active)
                            ctx.BonePoseData = new BonePoseData();
                        if (ctx.BonePoseData != null) ctx.BonePoseData.IsActive = c.Active;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return true;

                // ── BonePose レイヤーリセット
                case ResetBonePoseLayersCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    foreach (int idx in c.MasterIndices)
                        model.GetMeshContext(idx)?.BonePoseData?.ClearAllLayers();
                    _notifyPanels(ChangeKind.Attributes);
                    return true;

                // ── BonePose → BindPose ベイク
                case BakePoseToBindPoseCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx?.BonePoseData == null) continue;
                        ctx.BindPose = ctx.WorldMatrix.inverse;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return true;

                // ── モーフ全選択 / 全解除
                case SelectAllMorphsCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    model.ClearMorphSelection();
                    foreach (int idx in c.AllMorphIndices) model.AddToMorphSelection(idx);
                    _notifyPanels(ChangeKind.Selection);
                    return true;

                case DeselectAllMorphsCommand _:
                    model?.ClearMorphSelection();
                    _notifyPanels(ChangeKind.Selection);
                    return true;

                // ── モーフ変換（構造変更）
                case ConvertMeshToMorphCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    GetMeshListOps(model).ConvertMeshToMorph(
                        c.SourceIndex, c.ParentIndex, c.MorphName, c.Panel);
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;

                case ConvertMorphToMeshCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    GetMeshListOps(model).ConvertMorphToMesh(c.MasterIndices);
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;

                case CreateMorphSetCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    GetMeshListOps(model).CreateMorphSet(
                        c.SetName, c.MorphType, c.MorphIndices);
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;

                // ── モーフプレビュー
                // 開始と重み変更では通知しない。頂点位置の GPU 反映は
                // MeshListOps.SyncPositionsOnly（GetMeshListOps で配線）が担う。
                case StartMorphPreviewCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    GetMeshListOps(model).StartMorphPreview(c.MorphIndices);
                    return true;

                case ApplyMorphPreviewCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    GetMeshListOps(model).ApplyMorphPreview(c.Weight);
                    return true;

                // 終了時は元の頂点位置へ戻したうえで確定させる。
                // 法線抑止の解除と全 viewport の再計算が要るので DragEnd を通す。
                case EndMorphPreviewCommand _:
                    if (model == null) { Fail("no current model"); return true; }
                    GetMeshListOps(model).EndMorphPreview();
                    _viewportManager.EnterVerticesMoved(project, VerticesMovedPhase.DragEnd);
                    return true;

                // ── パネル側で直接変更した後の通知
                case NotifyListStructureChangedCommand _:
                    if (model == null) { Fail("no current model"); return true; }
                    model.OnListChanged?.Invoke();
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;

                case NotifyDictionaryChangedCommand _:
                    _notifyPanels(ChangeKind.Attributes);
                    return true;

                // ── BoneTransform 値設定
                case SetBoneTransformValueCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;

                        // C(ポーズ一時): BonePoseData の "Manual" 層へ差分として書く
                        if (_activeBoneEditMode == BoneMoveMode.PoseLayer)
                        {
                            ApplyPoseLayerField(ctx, c.TargetField, c.Value);
                            continue;
                        }

                        if (ctx.BoneTransform == null) continue;
                        ctx.BoneTransform.UseLocalTransform = true;
                        switch (c.TargetField)
                        {
                            case SetBoneTransformValueCommand.Field.PositionX: ctx.BoneTransform.Position = new Vector3(c.Value, ctx.BoneTransform.Position.y, ctx.BoneTransform.Position.z); break;
                            case SetBoneTransformValueCommand.Field.PositionY: ctx.BoneTransform.Position = new Vector3(ctx.BoneTransform.Position.x, c.Value, ctx.BoneTransform.Position.z); break;
                            case SetBoneTransformValueCommand.Field.PositionZ: ctx.BoneTransform.Position = new Vector3(ctx.BoneTransform.Position.x, ctx.BoneTransform.Position.y, c.Value); break;
                            case SetBoneTransformValueCommand.Field.RotationX: ctx.BoneTransform.Rotation = new Vector3(c.Value, ctx.BoneTransform.Rotation.y, ctx.BoneTransform.Rotation.z); break;
                            case SetBoneTransformValueCommand.Field.RotationY: ctx.BoneTransform.Rotation = new Vector3(ctx.BoneTransform.Rotation.x, c.Value, ctx.BoneTransform.Rotation.z); break;
                            case SetBoneTransformValueCommand.Field.RotationZ: ctx.BoneTransform.Rotation = new Vector3(ctx.BoneTransform.Rotation.x, ctx.BoneTransform.Rotation.y, c.Value); break;
                            case SetBoneTransformValueCommand.Field.ScaleX:    ctx.BoneTransform.Scale    = new Vector3(c.Value, ctx.BoneTransform.Scale.y, ctx.BoneTransform.Scale.z); break;
                            case SetBoneTransformValueCommand.Field.ScaleY:    ctx.BoneTransform.Scale    = new Vector3(ctx.BoneTransform.Scale.x, c.Value, ctx.BoneTransform.Scale.z); break;
                            case SetBoneTransformValueCommand.Field.ScaleZ:    ctx.BoneTransform.Scale    = new Vector3(ctx.BoneTransform.Scale.x, ctx.BoneTransform.Scale.y, c.Value); break;
                        }
                    }
                    model.ComputeWorldMatrices();

                    // 原点だけ移動: 対象 MeshFilter の自頂点を「開始ワールド位置を保つ」よう
                    // 再ローカル化する。ObjectMoveTool.ApplyWorldDelta / ApplyWorldRotation と同じ式。
                    if (_boneOriginOnly && _boneOriginStartWorld.Count > 0)
                    {
                        foreach (var okv in _boneOriginStartWorld)
                        {
                            if (!_boneOriginStartPositions.TryGetValue(okv.Key, out var startPos)) continue;
                            var omc = model.GetMeshContext(okv.Key);
                            var omo = omc?.MeshObject;
                            if (omo == null) continue;

                            Matrix4x4 curInv = omc.WorldMatrixInverse;
                            int n = Mathf.Min(omo.VertexCount, startPos.Length);
                            for (int i = 0; i < n; i++)
                            {
                                Vector3 wp = okv.Value.MultiplyPoint3x4(startPos[i]);
                                var v = omo.Vertices[i];
                                v.Position = curInv.MultiplyPoint3x4(wp);
                                omo.Vertices[i] = v;
                            }
                            omo.InvalidatePositionCache();

                            // 書き換えた頂点を GPU へ送る（PresentAll 経路では位置バッファが更新されない）。
                            _viewportManager.EnterVerticesMoved(
                                project, VerticesMovedPhase.Dragging, omc);
                        }
                    }

                    // A(スキン固定): World が変わったボーンの BindPose を追従更新し、SkinningMatrix を開始時と同一に保つ
                    if (_activeBoneEditMode == BoneMoveMode.BoneOnlyRebind && _boneRebindStartSkinning.Count > 0)
                    {
                        foreach (var kv in _boneRebindStartSkinning)
                        {
                            var bmc = model.GetMeshContext(kv.Key);
                            if (bmc == null || bmc.Type != MeshType.Bone) continue;
                            bmc.BindPose = bmc.WorldMatrix.inverse * kv.Value;
                        }
                    }
                    // Phase 2a-2g-1: ComputeWorldMatrices + UpdateTransform を EnterVerticesMoved(Dragging) に集約。
                    _viewportManager.EnterVerticesMoved(project, VerticesMovedPhase.Dragging);
                    // A(スキン固定): PresentAll 経路は GPU の transform 行列を push しないため、
                    // 補正後の SkinningMatrix(World×BindPose) を明示反映する（移動ツールと同じ理由）。
                    if (_activeBoneEditMode == BoneMoveMode.BoneOnlyRebind && _boneRebindStartSkinning.Count > 0)
                        _viewportManager.UpdateTransform();
                    else if (_activeBoneEditMode == BoneMoveMode.PoseLayer)
                        _viewportManager.UpdateTransform();
                    _notifyPanels(ChangeKind.Attributes);
                    return true;

                // ── UV展開
                case ApplyUvUnwrapCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    // 先頭ターゲットを UndoController に設定（CaptureMeshObjectSnapshot に必要）
                    if (c.MasterIndices.Length > 0)
                    {
                        var uvMc = model.GetMeshContext(c.MasterIndices[0]);
                        if (uvMc?.MeshObject != null && _undoController != null)
                        {
                            _undoController.SetMeshObject(uvMc.MeshObject, uvMc.UnityMesh);
                            _undoController.MeshUndoContext.ParentModelContext = model;
                        }
                    }
                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleApplyUvUnwrap(
                        model, _undoController, BuildMinimalToolCtx(model), () => { }, c);
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── マテリアルスロット追加
                case AddMaterialSlotCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var addMc = model.ActiveMeshContext;
                    if (addMc?.MeshObject != null && _undoController != null)
                    {
                        _undoController.SetMeshObject(addMc.MeshObject, addMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var addBefore = _undoController?.CaptureMeshObjectSnapshot();
                    model.AddMaterial(null);
                    model.CurrentMaterialIndex = model.MaterialCount - 1;
                    if (_undoController != null && addBefore != null)
                    {
                        var addAfter = _undoController.CaptureMeshObjectSnapshot();
                        _undoController.RecordTopologyChange(addBefore, addAfter, "Add Material Slot");
                    }
                    if (model.AutoSetDefaultMaterials)
                    {
                        model.DefaultMaterials            = new System.Collections.Generic.List<Material>(model.Materials);
                        model.DefaultCurrentMaterialIndex = model.CurrentMaterialIndex;
                    }
                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── マテリアルスロット削除
                case RemoveMaterialSlotCommand c:
                {
                    if (model == null || model.MaterialCount <= 1) { Fail("材質が 1 つしかありません"); return true; }
                    var remMc = model.ActiveMeshContext;
                    if (remMc?.MeshObject != null && _undoController != null)
                    {
                        _undoController.SetMeshObject(remMc.MeshObject, remMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var remBefore = _undoController?.CaptureMeshObjectSnapshot();
                    if (remMc?.MeshObject != null)
                        foreach (var face in remMc.MeshObject.Faces)
                        {
                            if (face.MaterialIndex == c.SlotIndex)       face.MaterialIndex = 0;
                            else if (face.MaterialIndex > c.SlotIndex)   face.MaterialIndex--;
                        }
                    model.RemoveMaterialAt(c.SlotIndex);
                    if (model.CurrentMaterialIndex >= model.MaterialCount)
                        model.CurrentMaterialIndex = model.MaterialCount - 1;
                    if (_undoController != null && remBefore != null)
                    {
                        var remAfter = _undoController.CaptureMeshObjectSnapshot();
                        _undoController.RecordTopologyChange(remBefore, remAfter, $"Remove Material Slot [{c.SlotIndex}]");
                    }
                    if (remMc?.UnityMesh != null && remMc.MeshObject != null)
                        remMc.ReplaceUnityMesh(remMc.MeshObject.ToUnityMesh());
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── 選択面にマテリアル適用
                case ApplyMaterialToFacesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var matMc = model.GetMeshContext(c.MasterIndex);
                    if (matMc?.MeshObject == null) { Fail("対象メッシュがありません"); return true; }
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(matMc.MeshObject, matMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var matBefore = _undoController?.CaptureMeshObjectSnapshot();
                    foreach (int fi in c.FaceIndices)
                        if (fi >= 0 && fi < matMc.MeshObject.FaceCount)
                            matMc.MeshObject.Faces[fi].MaterialIndex = c.MaterialSlot;
                    if (_undoController != null && matBefore != null)
                    {
                        var matAfter = _undoController.CaptureMeshObjectSnapshot();
                        _undoController.RecordTopologyChange(matBefore, matAfter, $"Apply Material [{c.MaterialSlot}]");
                    }
                    // テクスチャ表面(ctx.UnityMesh)は MaterialIndex 別サブメッシュで描画されるため、
                    // MaterialIndex 変更後は UnityMesh を再構築しないと表面に反映されない
                    // （EnterTopologyChanged は編集用GPUアダプタのみ再構築し UnityMesh は触らない）。
                    matMc.ReplaceUnityMesh(matMc.MeshObject.ToUnityMesh(model.MaterialCount));
                    // Phase 2a-2g-1: Material 変更後の GPU 反映を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── マテリアル色設定
                case SetMaterialColorCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var colRef = model.GetMaterialReference(c.SlotIndex);
                    if (colRef == null) { Fail($"材質スロット {c.SlotIndex} がありません"); return true; }

                    // 永続データ側。保存に乗るのはこちら。
                    if (colRef.Data == null) colRef.Data = new Poly_Ling.Materials.MaterialData();
                    colRef.Data.SetBaseColor(c.BaseColor);

                    // 起きている Material 側。Data を書いてもキャッシュは作り直されないので、
                    // ここへ入れないと画面の色が変わらない。
                    var colMat = colRef.Material;
                    if (colMat != null)
                    {
                        if (colMat.HasProperty("_BaseColor")) colMat.SetColor("_BaseColor", c.BaseColor);
                        if (colMat.HasProperty("_Color"))     colMat.SetColor("_Color",     c.BaseColor);
                    }

                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();

                    // 色だけの変更なので載せる集合は変わらない。
                    // EnterMeshAttributesChanged は集合が同じなら再構築せず、
                    // 描き直しだけを回す（PlayerViewportManager.cs:735-760）。
                    _viewportManager.EnterMeshAttributesChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── LSCM UV 展開
                case ApplyLscmUnwrapCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var lscmMc = model.GetMeshContext(c.MasterIndex);
                    if (lscmMc?.MeshObject == null) { Fail("対象メッシュがありません"); return true; }

                    // UndoController に対象メッシュを設定
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(lscmMc.MeshObject, lscmMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }

                    var before = _undoController?.CaptureMeshObjectSnapshot();

                    // Seam エッジは実行時点の SelectedEdges から取得
                    var seamEdges = lscmMc.SelectedEdges
                        ?? new HashSet<VertexPair>();
                    var result = Poly_Ling.UI.Lscm.LscmUnwrapOperation.Execute(
                        lscmMc.MeshObject, seamEdges,
                        c.IncludeBoundaryAsSeam,
                        Mathf.Clamp(c.MaxIterations, 100, 50000));

                    if (result.Success)
                    {
                        if (_undoController != null && before != null)
                        {
                            var after = _undoController.CaptureMeshObjectSnapshot();
                            _undoController.RecordTopologyChange(before, after, "LSCM UV展開");
                        }
                        lscmMc.ReplaceUnityMesh(lscmMc.MeshObject.ToUnityMesh());
                        // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                        _viewportManager.EnterTopologyChanged(project);
                        _notifyPanels(ChangeKind.Attributes);
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[LSCM] {result.StatusMessage}");
                    }
                    return true;
                }

                // ── UV→XYZ展開メッシュ生成
                case UvToXyzCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    // 追加前のリストをスナップショット（MeshListStack Undo 用）
                    var uvzBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleUvToXyz(
                        model, _undoController, BuildMinimalToolCtx(model),
                        mc =>
                        {
                            // UnityMesh は HandleUvToXyz が MeshContext の初期化子で
                            // 生成済み（PolyLingCore_UvHandlers.cs）。ここで作り直すと
                            // 1 個作っては捨てる二重生成になり、旧 Mesh が漏れる。
                            model.Add(mc);
                        },
                        () => { }, c);

                    // 追加後のリストをスナップショット → MeshListStack に記録
                    if (_undoController != null)
                    {
                        var uvzAfter = MeshFilterToSkinnedRecord.CaptureList(model);
                        var uvzRecord = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = uvzBefore,
                            AfterList  = uvzAfter,
                        };
                        {
                            string __dbgDesc = "UV→XYZ メッシュ生成";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, uvzRecord);
                            _undoController.MeshListStack.Record(uvzRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── XYZ→UV書き戻し
                case XyzToUvCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    // ターゲットメッシュに SetMeshObject（RecordTopologyChange に必要）
                    var xyzTargetMc = model.GetMeshContext(c.TargetMasterIndex);
                    if (xyzTargetMc?.MeshObject != null && _undoController != null)
                    {
                        _undoController.SetMeshObject(xyzTargetMc.MeshObject, xyzTargetMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    Poly_Ling.Core.PolyLingCoreUvHandlers.HandleXyzToUv(
                        model, _undoController, BuildMinimalToolCtx(model), () => { }, c);
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── BoneTransform スライダー開始：スナップショット保存
                case BeginBoneTransformSliderDragCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    _boneTransformBeforeSnapshots.Clear();
                    foreach (int idx in c.MasterIndices)
                    {
                        var mc = model.GetMeshContext(idx);
                        if (mc?.BoneTransform != null)
                            _boneTransformBeforeSnapshots[idx] = mc.BoneTransform.CreateSnapshot();
                    }

                    // A/B: 確定モードと開始状態を保持
                    _activeBoneEditMode = c.Mode;
                    _boneRebindStartSkinning.Clear();
                    _boneRebindStartBindPose.Clear();
                    _boneFreezeBefore = null;
                    _bonePoseBeforeSnapshots.Clear();

                    // 原点だけ移動: 対象 MeshFilter(非スキンド)の頂点と WorldMatrix を保存。
                    // ObjectMoveTool.SaveSnapshots の OriginOnly 分岐と同じ条件。
                    _boneOriginOnly = c.OriginOnly;
                    _boneOriginStartPositions.Clear();
                    _boneOriginStartWorld.Clear();
                    if (c.OriginOnly)
                    {
                        foreach (int idx in c.MasterIndices)
                        {
                            var omc = model.GetMeshContext(idx);
                            if (omc?.MeshObject == null) continue;
                            if (omc.Type != MeshType.Mesh || omc.IsSkinned) continue;
                            _boneOriginStartPositions[idx] = (Vector3[])omc.MeshObject.Positions.Clone();
                            _boneOriginStartWorld[idx]     = omc.WorldMatrix;
                        }
                    }
                    if (c.Mode == BoneMoveMode.BoneOnlyRebind)
                    {
                        for (int i = 0; i < model.Count; i++)
                        {
                            var bmc = model.GetMeshContext(i);
                            if (bmc == null || bmc.Type != MeshType.Bone) continue;
                            _boneRebindStartSkinning[i] = bmc.SkinningMatrix;   // World × BindPose
                            _boneRebindStartBindPose[i] = bmc.BindPose;
                        }
                    }
                    else if (c.Mode == BoneMoveMode.SkinBakeRebind)
                    {
                        _boneFreezeBefore = new TPoseBackup();
                        TPoseConverter.CaptureBackup(model.MeshContextList, _boneFreezeBefore);
                    }
                    else if (c.Mode == BoneMoveMode.PoseLayer)
                    {
                        foreach (int idx in c.MasterIndices)
                        {
                            var bmc = model.GetMeshContext(idx);
                            if (bmc == null || bmc.Type != MeshType.Bone) continue;
                            if (bmc.BonePoseData == null) bmc.BonePoseData = new BonePoseData();
                            bmc.BonePoseData.IsActive = true;
                            _bonePoseBeforeSnapshots[idx] = bmc.BonePoseData.CreateSnapshot();
                        }
                    }
                    return true;
                }

                // ── BoneTransform スライダー終了：Undo記録
                case EndBoneTransformSliderDragCommand c:
                {
                    if (model == null || _undoController == null) { _boneTransformBeforeSnapshots.Clear(); Fail("no current model"); return true; }
                    if (_boneTransformBeforeSnapshots.Count == 0) { Fail("ドラッグ開始が記録されていません"); return true; }

                    // 原点だけ移動: 頂点 + BoneTransform を 1 グループで記録する。
                    // ObjectMoveTool.CommitUndo の OriginOnly 分岐と同じ構成。
                    if (_boneOriginOnly && _boneOriginStartPositions.Count > 0)
                    {
                        _undoController.SetModelContext(model);
                        _undoController.MeshListStack.BeginGroup("原点だけ移動");

                        foreach (var okv in _boneOriginStartPositions)
                        {
                            int idx = okv.Key;
                            var omc = model.GetMeshContext(idx);
                            if (omc?.MeshObject == null || omc.BoneTransform == null) continue;

                            int vc = omc.MeshObject.VertexCount;
                            var indices = new int[vc];
                            var newPos  = new Vector3[vc];
                            for (int i = 0; i < vc; i++)
                            {
                                indices[i] = i;
                                newPos[i]  = omc.MeshObject.Vertices[i].Position;
                            }

                            _undoController.MeshListStack.Record(new PivotMoveRecord
                            {
                                MasterIndex        = idx,
                                VertexIndices      = indices,
                                OldVertexPositions = okv.Value,
                                NewVertexPositions = newPos,
                                OldBoneTransform   = _boneTransformBeforeSnapshots.TryGetValue(idx, out var ob0)
                                    ? ob0 : omc.BoneTransform.CreateSnapshot(),
                                NewBoneTransform   = omc.BoneTransform.CreateSnapshot(),
                            }, "原点だけ移動");
                        }

                        _undoController.MeshListStack.EndGroup();
                        _undoController.FocusMeshList();

                        _boneOriginOnly = false;
                        _boneOriginStartPositions.Clear();
                        _boneOriginStartWorld.Clear();
                        _boneRebindStartSkinning.Clear();
                        _boneRebindStartBindPose.Clear();
                        _boneTransformBeforeSnapshots.Clear();
                        return true;
                    }

                    // C(ポーズ一時): BonePoseData の変更を記録
                    if (_activeBoneEditMode == BoneMoveMode.PoseLayer)
                    {
                        var prec = new MultiBonePoseChangeRecord();
                        foreach (var kv in _bonePoseBeforeSnapshots)
                        {
                            var mc = model.GetMeshContext(kv.Key);
                            if (mc?.BonePoseData == null) continue;
                            prec.Entries.Add(new MultiBonePoseChangeRecord.Entry
                            {
                                MasterIndex = kv.Key,
                                OldSnapshot = kv.Value,
                                NewSnapshot = mc.BonePoseData.CreateSnapshot(),
                            });
                        }
                        if (prec.Entries.Count > 0)
                        {
                            {
                                string __dbgDesc = c.Description ?? "ボーンポーズ変更";
                                PLDiag.UndoRecord("MeshList", __dbgDesc, prec);
                                _undoController.MeshListStack.Record(prec, __dbgDesc);
                            }
                            _undoController.FocusMeshList();
                        }
                        _bonePoseBeforeSnapshots.Clear();
                        _boneTransformBeforeSnapshots.Clear();
                        return true;
                    }

                    // B(スキンごと確定): 頂点焼き込み＋リバインド。Tポーズ変換と同じ処理。
                    if (_activeBoneEditMode == BoneMoveMode.SkinBakeRebind && _boneFreezeBefore != null)
                    {
                        model.ComputeWorldMatrices();
                        TPoseConverter.BakeSkinnedVertices(model.MeshContextList);
                        for (int i = 0; i < model.Count; i++)
                        {
                            var bmc = model.GetMeshContext(i);
                            if (bmc == null || bmc.Type != MeshType.Bone) continue;
                            bmc.BindPose = bmc.WorldMatrix.inverse;
                        }
                        var afterBackup = new TPoseBackup();
                        TPoseConverter.CaptureBackup(model.MeshContextList, afterBackup);

                        _undoController.SetModelContext(model);
                        var frec = new TPoseUndoRecord(_boneFreezeBefore, afterBackup,
                            model.TPoseBackup, model.TPoseBackup, c.Description ?? "スキンごと確定");
                        {
                            string __dbgDesc = c.Description ?? "スキンごと確定";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, frec);
                            _undoController.MeshListStack.Record(frec, __dbgDesc);
                        }
                        _undoController.FocusMeshList();

                        _boneFreezeBefore = null;
                        _boneRebindStartSkinning.Clear();
                        _boneRebindStartBindPose.Clear();
                        _boneTransformBeforeSnapshots.Clear();
                        model.IsDirty = true;
                        model.OnListChanged?.Invoke();
                        _viewportManager.EnterTopologyChanged(project);
                        _notifyPanels(ChangeKind.Attributes);
                        return true;
                    }

                    // A(スキン固定): BoneTransform＋BindPose を複合レコードで記録
                    var record = new MultiBoneMoveRebindRecord();
                    var handled = new HashSet<int>();
                    foreach (var kv in _boneTransformBeforeSnapshots)
                    {
                        var mc = model.GetMeshContext(kv.Key);
                        if (mc?.BoneTransform == null) continue;
                        var after = mc.BoneTransform.CreateSnapshot();
                        bool btChanged = after.IsDifferentFrom(kv.Value);

                        Matrix4x4? oldBind = null, newBind = null;
                        if (_boneRebindStartBindPose.TryGetValue(kv.Key, out var ob) && ob != mc.BindPose)
                        {
                            oldBind = ob; newBind = mc.BindPose;
                        }
                        if (!btChanged && oldBind == null) continue;

                        record.Entries.Add(new MultiBoneMoveRebindRecord.Entry
                        {
                            MasterIndex      = kv.Key,
                            OldBoneTransform = btChanged ? kv.Value : (BoneTransformSnapshot?)null,
                            NewBoneTransform = btChanged ? after    : (BoneTransformSnapshot?)null,
                            OldBindPose      = oldBind,
                            NewBindPose      = newBind,
                        });
                        handled.Add(kv.Key);
                    }
                    // リバインドで BindPose が変わった子孫ボーン（編集対象以外）も記録
                    foreach (var kv in _boneRebindStartBindPose)
                    {
                        if (handled.Contains(kv.Key)) continue;
                        var mc = model.GetMeshContext(kv.Key);
                        if (mc == null || kv.Value == mc.BindPose) continue;
                        record.Entries.Add(new MultiBoneMoveRebindRecord.Entry
                        {
                            MasterIndex = kv.Key,
                            OldBindPose = kv.Value,
                            NewBindPose = mc.BindPose,
                        });
                    }

                    if (record.Entries.Count > 0)
                    {
                        _undoController.SetModelContext(model);
                        {
                            string __dbgDesc = c.Description ?? "BoneTransform変更";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    _boneRebindStartSkinning.Clear();
                    _boneRebindStartBindPose.Clear();
                    _boneTransformBeforeSnapshots.Clear();
                    return true;
                }

                // ── モデルブレンド: クローン作成
                case CreateBlendCloneCommand c:
                {
                    var src = project.GetModel(c.ModelIndex);
                    if (src == null) { Fail("複製元のオブジェクトがありません"); return true; }
                    string uniqueName = project.GenerateUniqueModelName(
                        string.IsNullOrEmpty(c.CloneNameBase) ? src.Name + "_blend" : c.CloneNameBase);
                    var clone = DeepCloneModelContext(src, uniqueName);
                    if (clone == null) { Fail("複製を作れませんでした"); return true; }
                    int cloneIndex = project.AddModel(clone);
                    // スキニング再計算（BoneTransform → WorldMatrix → BindPose）
                    clone.ComputeWorldAndBindPoses();
                    clone.ComputeMeshFilterBindPoses();
                    // Phase 2a-2g-1: 設計 A - クローンを CurrentModel に切り替え、
                    // 以降の Preview/Apply は通常編集フローと同じ扱いにする。
                    // Undo でモデル切替戻し → クローン削除まで戻れる。
                    project.SelectModel(cloneIndex);
                    _viewportManager.EnterSceneReset(project, clearScene: true);
                    _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return true;
                }
            }
            return false;
        }
    }
}
