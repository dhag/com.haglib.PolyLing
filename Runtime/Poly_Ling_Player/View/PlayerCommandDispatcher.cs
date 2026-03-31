// PlayerCommandDispatcher.cs
// PanelCommand を受け取り ProjectContext に適用するクラス。
// PolyLingPlayerViewer の DispatchPanelCommand を分離したもの。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class PlayerCommandDispatcher
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly Func<ProjectContext>  _getProject;
        private readonly MeshSceneRenderer     _renderer;
        private readonly PlayerViewportManager _viewportManager;
        private readonly PlayerSelectionOps    _selectionOps;
        private readonly Action<ChangeKind>    _notifyPanels;
        private readonly Action                _rebuildModelList;

        // ================================================================
        // 初期化
        // ================================================================

        public PlayerCommandDispatcher(
            Func<ProjectContext>  getProject,
            MeshSceneRenderer     renderer,
            PlayerViewportManager viewportManager,
            PlayerSelectionOps    selectionOps,
            Action<ChangeKind>    notifyPanels,
            Action                rebuildModelList)
        {
            _getProject       = getProject       ?? throw new ArgumentNullException(nameof(getProject));
            _renderer         = renderer         ?? throw new ArgumentNullException(nameof(renderer));
            _viewportManager  = viewportManager  ?? throw new ArgumentNullException(nameof(viewportManager));
            _selectionOps     = selectionOps;
            _notifyPanels     = notifyPanels     ?? throw new ArgumentNullException(nameof(notifyPanels));
            _rebuildModelList = rebuildModelList ?? throw new ArgumentNullException(nameof(rebuildModelList));
        }

        // ================================================================
        // ディスパッチ
        // ================================================================

        public void Dispatch(PanelCommand cmd)
        {
            var project = _getProject();
            if (project == null) return;
            var model   = project.CurrentModel;

            switch (cmd)
            {
                // ── モデル選択
                case SwitchModelCommand c:
                    project.SelectModel(c.TargetModelIndex);
                    {
                        var switchedModel = project.CurrentModel;
                        if (switchedModel != null)
                        {
                            _renderer?.ClearScene();
                            _viewportManager.RebuildAdapter(0, switchedModel);
                            var firstMc = switchedModel.FirstSelectedDrawableMesh;
                            if (firstMc != null)
                            {
                                _selectionOps?.SetSelectionState(firstMc.Selection);
                                _renderer?.SetSelectionState(firstMc.Selection);
                            }
                            _renderer?.UpdateSelectedDrawableMesh(0, switchedModel);
                            _viewportManager.NotifyCameraChanged(_viewportManager.PerspectiveViewport);
                        }
                    }
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return;

                // ── モデル名前変更
                case RenameModelCommand c:
                    var renameTarget = project.GetModel(c.ModelIndex);
                    if (renameTarget != null && !string.IsNullOrEmpty(c.NewName))
                        renameTarget.Name = c.NewName;
                    _notifyPanels(ChangeKind.ListStructure);
                    return;

                // ── モデル削除
                case DeleteModelCommand c:
                    project.RemoveModelAt(c.ModelIndex);
                    _rebuildModelList();
                    return;

                // ── メッシュ選択
                case SelectMeshCommand sel:
                    if (model == null) return;
                    switch (sel.Category)
                    {
                        case MeshCategory.Drawable:
                            model.ClearMeshSelection();
                            foreach (int idx in sel.Indices) model.AddToMeshSelection(idx);
                            if (sel.Indices.Length > 0)
                                model.SelectDrawableMesh(sel.Indices[0]);
                            var selMc = model.FirstSelectedDrawableMesh;
                            if (selMc != null)
                            {
                                _selectionOps?.SetSelectionState(selMc.Selection);
                                _renderer?.SetSelectionState(selMc.Selection);
                            }
                            _renderer?.UpdateSelectedDrawableMesh(0, model);
                            break;
                        case MeshCategory.Bone:
                            model.ClearBoneSelection();
                            foreach (int idx in sel.Indices) model.AddToBoneSelection(idx);
                            break;
                        case MeshCategory.Morph:
                            model.ClearMorphSelection();
                            foreach (int idx in sel.Indices) model.AddToMorphSelection(idx);
                            break;
                    }
                    _notifyPanels(ChangeKind.Selection);
                    return;

                // ── 可視性トグル
                case ToggleVisibilityCommand c:
                    if (model == null) return;
                    var visCtx = model.GetMeshContext(c.MasterIndex);
                    if (visCtx != null) visCtx.IsVisible = !visCtx.IsVisible;
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── 一括可視性
                case SetBatchVisibilityCommand c:
                    if (model == null) return;
                    foreach (int mi in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(mi);
                        if (ctx != null) ctx.IsVisible = c.Visible;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── ロックトグル
                case ToggleLockCommand c:
                    if (model == null) return;
                    var lckCtx = model.GetMeshContext(c.MasterIndex);
                    if (lckCtx != null) lckCtx.IsLocked = !lckCtx.IsLocked;
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── ミラータイプ
                case CycleMirrorTypeCommand c:
                    if (model == null) return;
                    var mirCtx = model.GetMeshContext(c.MasterIndex);
                    if (mirCtx != null)
                        mirCtx.MirrorType = (mirCtx.MirrorType + 1) % 4;
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── メッシュ名前変更
                case RenameMeshCommand c:
                    if (model == null) return;
                    var renCtx = model.GetMeshContext(c.MasterIndex);
                    if (renCtx != null && !string.IsNullOrEmpty(c.NewName))
                        renCtx.Name = c.NewName;
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── メッシュ削除
                case DeleteMeshesCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices.OrderByDescending(i => i))
                        model.RemoveAt(idx);
                    _notifyPanels(ChangeKind.ListStructure);
                    return;

                // ── メッシュ複製
                case DuplicateMeshesCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var srcCtx = model.GetMeshContext(idx);
                        if (srcCtx == null) continue;
                        var dup = new MeshContext
                        {
                            Name       = srcCtx.Name + "_copy",
                            MeshObject = srcCtx.MeshObject?.Clone(),
                            IsVisible  = srcCtx.IsVisible,
                            IsLocked   = srcCtx.IsLocked,
                            Depth      = srcCtx.Depth,
                        };
                        model.Add(dup);
                    }
                    _notifyPanels(ChangeKind.ListStructure);
                    return;

                // ── BonePose Active
                case SetBonePoseActiveCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx?.BonePoseData != null) ctx.BonePoseData.IsActive = c.Active;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── BonePose レイヤーリセット
                case ResetBonePoseLayersCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                        model.GetMeshContext(idx)?.BonePoseData?.ClearAllLayers();
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── BonePose ベイク（Player では BindPose 更新のみ）
                case BakePoseToBindPoseCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx?.BonePoseData == null) continue;
                        // Player では WorldMatrix/BindPose 再計算は省略し Notify のみ
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── モーフ全選択 / 全解除
                case SelectAllMorphsCommand c:
                    if (model == null) return;
                    model.ClearMorphSelection();
                    foreach (int idx in c.AllMorphIndices) model.AddToMorphSelection(idx);
                    _notifyPanels(ChangeKind.Selection);
                    return;

                case DeselectAllMorphsCommand _:
                    model?.ClearMorphSelection();
                    _notifyPanels(ChangeKind.Selection);
                    return;

                // ── モーフ変換・プレビュー・セット作成（PolyLingCore が必要、Player では未実装）
                case ConvertMeshToMorphCommand _:
                case ConvertMorphToMeshCommand _:
                case CreateMorphSetCommand _:
                case StartMorphPreviewCommand _:
                case ApplyMorphPreviewCommand _:
                case EndMorphPreviewCommand _:
                    Debug.LogWarning($"[PlayerCommandDispatcher] {cmd.GetType().Name} requires PolyLingCore (not implemented in Player).");
                    return;

                // ── BoneTransform 値設定
                case SetBoneTransformValueCommand c:
                    if (model == null) return;
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx?.BoneTransform == null) continue;
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
                    _viewportManager.UpdateTransform();
                    _notifyPanels(ChangeKind.Attributes);
                    return;

                // ── BoneTransform スライダー開始／終了（通知不要）
                case BeginBoneTransformSliderDragCommand _:
                case EndBoneTransformSliderDragCommand _:
                    return;

                // ── その他（モーフ変換・プレビュー等）は Player では未実装
                default:
                    Debug.LogWarning($"[PlayerCommandDispatcher] Unhandled PanelCommand: {cmd.GetType().Name}");
                    return;
            }
        }
    }
}
