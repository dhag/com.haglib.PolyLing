// BoneInputHandler.cs
// Player 向けボーン選択・ドラッグ移動ハンドラ。
// PolyLingCore に依存せず AxisGizmo と ModelContext のパブリック API のみで実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class BoneInputHandler : IPlayerToolHandler
    {
        // ================================================================
        // 定数
        // ================================================================

        private const float BonePickRadius    = 18f;
        private const float BoneDragThreshold = 4f;

        // ================================================================
        // 状態
        // ================================================================

        private enum BoneDragState { Idle, PendingDrag, AxisDragging, CenterDragging }
        private BoneDragState _dragState = BoneDragState.Idle;

        private readonly AxisGizmo         _gizmo       = new AxisGizmo();
        private AxisGizmo.AxisType         _dragAxis    = AxisGizmo.AxisType.None;
        private Vector2                    _mouseDownPos;
        private Vector2                    _lastDragPos;
        private readonly Dictionary<int, BoneTransformSnapshot> _beforeSnapshots
            = new Dictionary<int, BoneTransformSnapshot>();

        // ================================================================
        // 外部コールバック
        // ================================================================

        public Func<ToolContext>  GetToolContext;
        public Action             OnRepaint;
        public Action             OnSelectionChanged;
        public Action             OnDrawableMeshSelectionChanged;

        // ================================================================
        // 依存
        // ================================================================

        private ProjectContext  _project;
        private MeshUndoController _undoController;

        public void SetProject(ProjectContext project)         => _project         = project;
        public void SetUndoController(MeshUndoController ctrl) => _undoController  = ctrl;

        // ================================================================
        // AxisGizmo スクリーン座標取得（GizmoOverlay 用）
        // ================================================================

        public bool TryGetGizmoScreenPositions(
            ToolContext ctx,
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis, out AxisGizmo.AxisType draggingAxis)
        {
            origin = xEnd = yEnd = zEnd = Vector2.zero;
            hoveredAxis  = AxisGizmo.AxisType.None;
            draggingAxis = AxisGizmo.AxisType.None;

            var model = _project?.CurrentModel;
            if (model == null || !model.HasBoneSelection) return false;
            if (ctx == null) return false;

            UpdateGizmoCenter(model);
            _gizmo.GetScreenPositions(ctx, out origin, out xEnd, out yEnd, out zEnd);
            hoveredAxis  = _gizmo.HoveredAxis;
            draggingAxis = _gizmo.DraggingAxis;
            return true;
        }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            HandleMouseDown(ToImgui(screenPos, ctx), mods, ctx);
            HandleMouseUp(ctx);
            OnRepaint?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            HandleMouseDown(ToImgui(screenPos, ctx), mods, ctx);
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            HandleMouseDrag(ToImgui(screenPos, ctx), ctx);
            OnRepaint?.Invoke();
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            HandleMouseUp(ctx);
            OnRepaint?.Invoke();
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null || _dragState != BoneDragState.Idle) return;
            var model = _project?.CurrentModel;
            if (model == null || !model.HasBoneSelection) return;
            _gizmo.HoveredAxis = _gizmo.FindAxisAtScreenPos(ToImgui(screenPos, ctx), ctx);
        }

        // ================================================================
        // 内部ハンドラ
        // ================================================================

        private void HandleMouseDown(Vector2 imguiPos, ModifierKeys mods, ToolContext ctx)
        {
            _mouseDownPos = imguiPos;
            var model = _project?.CurrentModel;
            if (model == null) return;

            if (model.HasBoneSelection)
            {
                UpdateGizmoCenter(model);
                var hitAxis = _gizmo.FindAxisAtScreenPos(imguiPos, ctx);
                if (hitAxis != AxisGizmo.AxisType.None)
                {
                    SaveSnapshots(model);
                    _dragAxis    = hitAxis;
                    _lastDragPos = imguiPos;
                    _dragState   = hitAxis == AxisGizmo.AxisType.Center
                        ? BoneDragState.CenterDragging
                        : BoneDragState.AxisDragging;
                    _gizmo.DraggingAxis = hitAxis;
                    return;
                }
            }

            if (TryPickBone(imguiPos, ctx, mods.Shift, mods.Ctrl, model))
                _dragState = BoneDragState.PendingDrag;
        }

        private void HandleMouseDrag(Vector2 imguiPos, ToolContext ctx)
        {
            var model = _project?.CurrentModel;
            if (model == null) return;

            switch (_dragState)
            {
                case BoneDragState.PendingDrag:
                    if (Vector2.Distance(imguiPos, _mouseDownPos) > BoneDragThreshold)
                    {
                        SaveSnapshots(model);
                        UpdateGizmoCenter(model);
                        _dragAxis    = AxisGizmo.AxisType.Center;
                        _lastDragPos = _mouseDownPos;
                        _dragState   = BoneDragState.CenterDragging;
                        ApplyFreeDelta(imguiPos - _mouseDownPos, ctx, model);
                        _lastDragPos = imguiPos;
                    }
                    break;

                case BoneDragState.AxisDragging:
                    ApplyAxisDelta(imguiPos - _lastDragPos, _dragAxis, ctx, model);
                    _lastDragPos = imguiPos;
                    break;

                case BoneDragState.CenterDragging:
                    ApplyFreeDelta(imguiPos - _lastDragPos, ctx, model);
                    _lastDragPos = imguiPos;
                    break;
            }
        }

        private void HandleMouseUp(ToolContext ctx)
        {
            var model = _project?.CurrentModel;
            if (_dragState == BoneDragState.AxisDragging ||
                _dragState == BoneDragState.CenterDragging)
                CommitUndo(model);

            ResetDragState();
        }

        // ================================================================
        // ピッキング
        // ================================================================

        private bool TryPickBone(Vector2 imguiPos, ToolContext ctx,
                                  bool shift, bool ctrl, ModelContext model)
        {
            int bestIndex = -1;
            float bestDist = BonePickRadius;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                var wm = mc.WorldMatrix;
                Vector2 sp = ctx.WorldToScreen(new Vector3(wm.m03, wm.m13, wm.m23));
                float dist = Vector2.Distance(imguiPos, sp);
                if (dist < bestDist) { bestDist = dist; bestIndex = i; }
            }

            if (bestIndex < 0) return false;

            if (ctrl)       model.ToggleMeshContextSelection(bestIndex);
            else if (shift) model.AddToBoneSelection(bestIndex);
            else            model.SelectBone(bestIndex);

            model.IsDirty = true;
            model.OnListChanged?.Invoke();
            OnSelectionChanged?.Invoke();
            if (model.ActiveCategory == ModelContext.SelectionCategory.Mesh)
                OnDrawableMeshSelectionChanged?.Invoke();
            return true;
        }

        // ================================================================
        // 移動適用
        // ================================================================

        private void ApplyFreeDelta(Vector2 screenDelta, ToolContext ctx, ModelContext model)
        {
            Vector3 worldDelta = _gizmo.ComputeFreeDelta(screenDelta, ctx);
            ApplyWorldDelta(worldDelta, model);
        }

        private void ApplyAxisDelta(Vector2 screenDelta, AxisGizmo.AxisType axis,
                                     ToolContext ctx, ModelContext model)
        {
            Vector3 worldDelta = _gizmo.ComputeAxisDelta(screenDelta, axis, ctx);
            ApplyWorldDelta(worldDelta, model);
        }

        private void ApplyWorldDelta(Vector3 worldDelta, ModelContext model)
        {
            if (worldDelta.sqrMagnitude < 1e-10f) return;
            foreach (int idx in model.SelectedBoneIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;
                mc.BoneTransform.UseLocalTransform = true;
                mc.BoneTransform.Position += worldDelta;
            }
            model.ComputeWorldMatrices();
            UpdateGizmoCenter(model);
        }

        // ================================================================
        // Undo
        // ================================================================

        private void SaveSnapshots(ModelContext model)
        {
            _beforeSnapshots.Clear();
            foreach (int idx in model.SelectedBoneIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform != null)
                    _beforeSnapshots[idx] = mc.BoneTransform.CreateSnapshot();
            }
        }

        private void CommitUndo(ModelContext model)
        {
            if (_beforeSnapshots.Count == 0 || _undoController == null || model == null) return;

            var record = new MultiBoneTransformChangeRecord();
            foreach (var kv in _beforeSnapshots)
            {
                var mc = model.GetMeshContext(kv.Key);
                if (mc?.BoneTransform == null) continue;
                record.Entries.Add(new MultiBoneTransformChangeRecord.Entry
                {
                    MasterIndex = kv.Key,
                    OldSnapshot = kv.Value,
                    NewSnapshot = mc.BoneTransform.CreateSnapshot(),
                });
            }
            if (record.Entries.Count > 0)
            {
                _undoController.MeshListStack.Record(record, "ボーン移動");
                _undoController.FocusMeshList();
            }
            model.OnListChanged?.Invoke();
            _beforeSnapshots.Clear();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void UpdateGizmoCenter(ModelContext model)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (int idx in model.SelectedBoneIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc == null) continue;
                var wm = mc.WorldMatrix;
                sum += new Vector3(wm.m03, wm.m13, wm.m23);
                count++;
            }
            _gizmo.Center = count > 0 ? sum / count : Vector3.zero;
        }

        private void ResetDragState()
        {
            _dragState          = BoneDragState.Idle;
            _dragAxis           = AxisGizmo.AxisType.None;
            _gizmo.DraggingAxis = AxisGizmo.AxisType.None;
            _gizmo.HoveredAxis  = AxisGizmo.AxisType.None;
            _beforeSnapshots.Clear();
        }

        private ToolContext BuildToolContext(ModifierKeys mods, Vector2 screenPosYDown)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;
            var ctx = GetToolContext?.Invoke() ?? new ToolContext();
            ctx.Model      = model;
            ctx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(screenPosYDown, ctx),
            };
            ctx.Repaint = OnRepaint;
            return ctx;
        }

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
