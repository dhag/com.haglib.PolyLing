// PolyLingCore_BoneInput.cs
// ボーンピッキング＋ドラッグ移動ロジック
// PolyLing_BoneInput.cs から移植（partial class PolyLing → PolyLingCore）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Core
{
    public partial class PolyLingCore
    {
        // ================================================================
        // 定数
        // ================================================================

        private const float BonePickRadius    = 18f;
        private const float BoneDragThreshold = 4f;

        // ================================================================
        // 状態
        // ================================================================

        public enum BoneDragState
        {
            Idle,
            PendingDrag,
            AxisDragging,
            CenterDragging,
        }

        public BoneDragState CurrentBoneDragState { get; private set; } = BoneDragState.Idle;

        private AxisGizmo _boneAxisGizmo = new AxisGizmo();
        public  AxisGizmo BoneAxisGizmo => _boneAxisGizmo;

        private AxisGizmo.AxisType _boneDragAxis     = AxisGizmo.AxisType.None;
        private Vector2            _boneMouseDownPos;
        private Vector2            _boneLastDragScreenPos;

        private Dictionary<int, BoneTransformSnapshot> _boneDragBeforeSnapshots
            = new Dictionary<int, BoneTransformSnapshot>();

        // ================================================================
        // 入力ハンドラ（Editor側から呼び出し）
        // ================================================================

        public bool HandleBoneInput(EventType eventType, int button, Vector2 mousePos,
                                    bool shift, bool ctrl, bool alt,
                                    Rect previewRect, Vector3 camPos, Vector3 lookAt)
        {
            var ctx = _toolManager?.toolContext;
            if (ctx == null || _model == null) return false;

            if (eventType == EventType.MouseDown && button == 0 && !alt)
                return HandleBoneMouseDown(mousePos, previewRect, camPos, lookAt, shift, ctrl, ctx);

            if (eventType == EventType.MouseDrag && button == 0)
                return HandleBoneMouseDrag(mousePos, ctx);

            if (eventType == EventType.MouseUp && button == 0)
                return HandleBoneMouseUp(ctx);

            return false;
        }

        // ================================================================
        // MouseDown
        // ================================================================

        private bool HandleBoneMouseDown(Vector2 mousePos, Rect previewRect,
                                          Vector3 camPos, Vector3 lookAt,
                                          bool shift, bool ctrl, ToolContext ctx)
        {
            _boneMouseDownPos = mousePos;

            if (_model.HasBoneSelection)
            {
                UpdateBoneGizmoCenter();
                var hitAxis = _boneAxisGizmo.FindAxisAtScreenPos(mousePos, ctx);
                if (hitAxis != AxisGizmo.AxisType.None)
                {
                    SaveBoneDragSnapshots();
                    _boneDragAxis = hitAxis;
                    _boneLastDragScreenPos = mousePos;

                    if (hitAxis == AxisGizmo.AxisType.Center)
                        CurrentBoneDragState = BoneDragState.CenterDragging;
                    else
                    {
                        CurrentBoneDragState = BoneDragState.AxisDragging;
                        _boneAxisGizmo.DraggingAxis = hitAxis;
                    }
                    return true;
                }
            }

            if (TryPickBone(mousePos, previewRect, camPos, lookAt, shift, ctrl))
            {
                CurrentBoneDragState = BoneDragState.PendingDrag;
                return true;
            }

            return false;
        }

        // ================================================================
        // MouseDrag
        // ================================================================

        private bool HandleBoneMouseDrag(Vector2 mousePos, ToolContext ctx)
        {
            switch (CurrentBoneDragState)
            {
                case BoneDragState.PendingDrag:
                {
                    float dist = Vector2.Distance(mousePos, _boneMouseDownPos);
                    if (dist > BoneDragThreshold)
                    {
                        SaveBoneDragSnapshots();
                        UpdateBoneGizmoCenter();
                        _boneDragAxis = AxisGizmo.AxisType.Center;
                        _boneLastDragScreenPos = _boneMouseDownPos;
                        CurrentBoneDragState = BoneDragState.CenterDragging;

                        ApplyBoneDelta(mousePos - _boneMouseDownPos, ctx);
                        _boneLastDragScreenPos = mousePos;
                    }
                    OnRepaintRequired?.Invoke();
                    return true;
                }

                case BoneDragState.AxisDragging:
                {
                    ApplyBoneAxisDelta(mousePos - _boneLastDragScreenPos, ctx);
                    _boneLastDragScreenPos = mousePos;
                    OnRepaintRequired?.Invoke();
                    return true;
                }

                case BoneDragState.CenterDragging:
                {
                    ApplyBoneDelta(mousePos - _boneLastDragScreenPos, ctx);
                    _boneLastDragScreenPos = mousePos;
                    OnRepaintRequired?.Invoke();
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // MouseUp
        // ================================================================

        private bool HandleBoneMouseUp(ToolContext ctx)
        {
            bool handled = false;
            switch (CurrentBoneDragState)
            {
                case BoneDragState.AxisDragging:
                case BoneDragState.CenterDragging:
                    CommitBoneDragUndo();
                    handled = true;
                    break;
                case BoneDragState.PendingDrag:
                    handled = true;
                    break;
            }
            ResetBoneDragState();
            OnRepaintRequired?.Invoke();
            return handled;
        }

        // ================================================================
        // ギズモ中心更新
        // ================================================================

        public void UpdateBoneGizmoCenter()
        {
            if (_model == null || !_model.HasBoneSelection)
            {
                _boneAxisGizmo.Center = Vector3.zero;
                return;
            }

            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (int idx in _model.SelectedBoneIndices)
            {
                var mc = _model.GetMeshContext(idx);
                if (mc == null) continue;
                var wm = mc.WorldMatrix;
                sum += new Vector3(wm.m03, wm.m13, wm.m23);
                count++;
            }
            _boneAxisGizmo.Center = count > 0 ? sum / count : Vector3.zero;
        }

        // ================================================================
        // ピッキング
        // ================================================================

        private bool TryPickBone(Vector2 mousePos, Rect previewRect,
                                  Vector3 camPos, Vector3 lookAt,
                                  bool shift, bool ctrl)
        {
            if (_model == null || _meshContextList == null) return false;

            int bestIndex = -1;
            float bestDist = BonePickRadius;

            var ctx = _toolManager?.toolContext;
            if (ctx == null) return false;

            for (int i = 0; i < _meshContextList.Count; i++)
            {
                var mc = _meshContextList[i];
                if (mc == null || mc.Type != MeshType.Bone) continue;

                var wm = mc.WorldMatrix;
                Vector3 boneWorldPos = new Vector3(wm.m03, wm.m13, wm.m23);
                Vector2 screenPos = ctx.WorldToScreen(boneWorldPos);

                float dist = Vector2.Distance(mousePos, screenPos);
                if (dist < bestDist)
                {
                    bestDist  = dist;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0) return false;

            if (ctrl)         _model.ToggleSelection(bestIndex);
            else if (shift)   _model.AddToBoneSelection(bestIndex);
            else              _model.SelectBone(bestIndex);

            _model.IsDirty = true;
            _model.OnListChanged?.Invoke();
            _toolManager?.toolContext?.OnMeshSelectionChanged?.Invoke();
            OnRepaintRequired?.Invoke();
            return true;
        }

        // ================================================================
        // 移動適用
        // ================================================================

        private void ApplyBoneDelta(Vector2 screenDelta, ToolContext ctx)
        {
            Vector3 worldDelta = _boneAxisGizmo.ComputeFreeDelta(screenDelta, ctx);
            ApplyBoneWorldDelta(worldDelta);
        }

        private void ApplyBoneAxisDelta(Vector2 screenDelta, ToolContext ctx)
        {
            Vector3 worldDelta = _boneAxisGizmo.ComputeAxisDelta(screenDelta, _boneDragAxis, ctx);
            ApplyBoneWorldDelta(worldDelta);
        }

        private void ApplyBoneWorldDelta(Vector3 worldDelta)
        {
            if (worldDelta.sqrMagnitude < 1e-10f || _model == null) return;

            foreach (int idx in _model.SelectedBoneIndices)
            {
                var mc = _model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;
                mc.BoneTransform.UseLocalTransform = true;
                mc.BoneTransform.Position += worldDelta;
            }

            _model.ComputeWorldMatrices();
            OnRepaintRequired?.Invoke();
        }

        // ================================================================
        // Undo
        // ================================================================

        private void SaveBoneDragSnapshots()
        {
            _boneDragBeforeSnapshots.Clear();
            if (_model == null) return;

            foreach (int idx in _model.SelectedBoneIndices)
            {
                var mc = _model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;
                _boneDragBeforeSnapshots[idx] = mc.BoneTransform.CreateSnapshot();
            }
        }

        private void CommitBoneDragUndo()
        {
            if (_boneDragBeforeSnapshots.Count == 0 || _undoController == null) return;

            var record = new MultiBoneTransformChangeRecord();
            foreach (var kvp in _boneDragBeforeSnapshots)
            {
                int idx = kvp.Key;
                var mc = _model?.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;

                record.Entries.Add(new MultiBoneTransformChangeRecord.Entry
                {
                    MasterIndex = idx,
                    OldSnapshot = kvp.Value,
                    NewSnapshot = mc.BoneTransform.CreateSnapshot(),
                });
            }

            if (record.Entries.Count > 0)
            {
                _undoController.MeshListStack.Record(record, "ボーン移動");
                _undoController.FocusMeshList();
            }

            _model?.OnListChanged?.Invoke();
            _boneDragBeforeSnapshots.Clear();
        }

        // ================================================================
        // 状態リセット
        // ================================================================

        public void ResetBoneDragState()
        {
            CurrentBoneDragState    = BoneDragState.Idle;
            _boneDragAxis           = AxisGizmo.AxisType.None;
            _boneAxisGizmo.DraggingAxis = AxisGizmo.AxisType.None;
            _boneAxisGizmo.HoveredAxis  = AxisGizmo.AxisType.None;
        }

        public void UpdateBoneGizmoHover(Vector2 mousePos)
        {
            if (CurrentBoneDragState != BoneDragState.Idle) return;
            var ctx = _toolManager?.toolContext;
            if (ctx == null) return;
            _boneAxisGizmo.HoveredAxis = _boneAxisGizmo.FindAxisAtScreenPos(mousePos, ctx);
        }
    }
}
