// Assets/Editor/Poly_Ling/PolyLing/PolyLing_BoneInput.cs
// ボーンピッキング＋ドラッグ移動処理（partial class）
// AxisGizmoを使用した軸ギズモ表示・軸拘束移動・自由移動
// 選択結果はModel.SelectedBoneIndices経由でTypedMeshListPanelと同期

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

public partial class PolyLing
{
    // ================================================================
    // ボーン操作 定数
    // ================================================================

    /// <summary>ボーンピッキングの画面上距離閾値（ピクセル）</summary>
    private const float BonePickRadius = 18f;
    private const float BoneDragThreshold = 4f;

    // ================================================================
    // ボーン操作 状態
    // ================================================================

    private enum BoneDragState
    {
        Idle,
        PendingDrag,    // マウスダウン後、閾値未到達
        AxisDragging,   // 軸拘束ドラッグ中
        CenterDragging, // 自由ドラッグ中
    }

    private BoneDragState _boneDragState = BoneDragState.Idle;
    private AxisGizmo _boneAxisGizmo = new AxisGizmo();
    private AxisGizmo.AxisType _boneDragAxis = AxisGizmo.AxisType.None;
    private Vector2 _boneMouseDownPos;
    private Vector2 _boneLastDragScreenPos;

    // Undo用スナップショット（ドラッグ開始時のRestPosition保存）
    private Dictionary<int, BonePoseDataSnapshot> _boneDragBeforeSnapshots
        = new Dictionary<int, BonePoseDataSnapshot>();

    // ================================================================
    // ボーン入力ハンドラ（HandleInputから呼び出し）
    // ================================================================

    /// <summary>
    /// ボーン関連のマウスイベントを処理する。
    /// trueを返した場合、呼び出し元はイベントを消費する。
    /// </summary>
    private bool HandleBoneInput(Event e, Vector2 mousePos, Rect previewRect,
                                  Vector3 camPos, Vector3 lookAt)
    {
        var ctx = _toolManager?.toolContext;
        if (ctx == null || _model == null) return false;

        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0 && !e.alt:
                return HandleBoneMouseDown(mousePos, previewRect, camPos, lookAt, e.shift, e.control || e.command, ctx);

            case EventType.MouseDrag when e.button == 0:
                return HandleBoneMouseDrag(mousePos, ctx);

            case EventType.MouseUp when e.button == 0:
                return HandleBoneMouseUp(ctx);
        }

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

        // 1. ボーン選択中ならギズモのヒットテスト（最優先）
        if (_model.HasBoneSelection)
        {
            UpdateBoneGizmoCenter();
            var hitAxis = _boneAxisGizmo.FindAxisAtScreenPos(mousePos, ctx);
            if (hitAxis != AxisGizmo.AxisType.None)
            {
                // ドラッグ開始準備（スナップショット保存）
                SaveBoneDragSnapshots();
                _boneDragAxis = hitAxis;
                _boneLastDragScreenPos = mousePos;

                if (hitAxis == AxisGizmo.AxisType.Center)
                {
                    _boneDragState = BoneDragState.CenterDragging;
                }
                else
                {
                    _boneDragState = BoneDragState.AxisDragging;
                    _boneAxisGizmo.DraggingAxis = hitAxis;
                }
                return true;
            }
        }

        // 2. ボーンピッキング
        if (TryPickBone(mousePos, previewRect, camPos, lookAt, shift, ctrl))
        {
            _boneDragState = BoneDragState.PendingDrag;
            return true;
        }

        return false;
    }

    // ================================================================
    // MouseDrag
    // ================================================================

    private bool HandleBoneMouseDrag(Vector2 mousePos, ToolContext ctx)
    {
        switch (_boneDragState)
        {
            case BoneDragState.PendingDrag:
            {
                float dist = Vector2.Distance(mousePos, _boneMouseDownPos);
                if (dist > BoneDragThreshold)
                {
                    // ギズモ中央ドラッグとして開始
                    SaveBoneDragSnapshots();
                    UpdateBoneGizmoCenter();
                    _boneDragAxis = AxisGizmo.AxisType.Center;
                    _boneLastDragScreenPos = _boneMouseDownPos;
                    _boneDragState = BoneDragState.CenterDragging;

                    // 開始点からの累積delta適用
                    Vector2 totalDelta = mousePos - _boneMouseDownPos;
                    ApplyBoneDelta(totalDelta, ctx);
                    _boneLastDragScreenPos = mousePos;
                }
                Repaint();
                return true;
            }

            case BoneDragState.AxisDragging:
            {
                Vector2 frameDelta = mousePos - _boneLastDragScreenPos;
                ApplyBoneAxisDelta(frameDelta, ctx);
                _boneLastDragScreenPos = mousePos;
                Repaint();
                return true;
            }

            case BoneDragState.CenterDragging:
            {
                Vector2 frameDelta = mousePos - _boneLastDragScreenPos;
                ApplyBoneDelta(frameDelta, ctx);
                _boneLastDragScreenPos = mousePos;
                Repaint();
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

        switch (_boneDragState)
        {
            case BoneDragState.AxisDragging:
            case BoneDragState.CenterDragging:
                CommitBoneDragUndo();
                handled = true;
                break;

            case BoneDragState.PendingDrag:
                // クリックのみ（ドラッグなし）→選択は既に完了
                handled = true;
                break;
        }

        ResetBoneDragState();
        Repaint();
        return handled;
    }

    // ================================================================
    // ボーンギズモ描画（DrawPreviewのRepaint中に呼び出す）
    // ================================================================

    private void DrawBoneGizmo(Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        if (_model == null || !_model.HasBoneSelection) return;

        var ctx = _toolManager?.toolContext;
        if (ctx == null) return;

        UpdateBoneGizmoCenter();

        // ホバー更新（Idle時のみ）
        if (_boneDragState == BoneDragState.Idle)
        {
            // Repaint時のマウス位置はBeginClip後のローカル座標
            _boneAxisGizmo.HoveredAxis = _boneAxisGizmo.FindAxisAtScreenPos(
                Event.current.mousePosition, ctx);
        }

        _boneAxisGizmo.Draw(ctx);
    }

    // ================================================================
    // ボーンピッキング
    // ================================================================

    /// <summary>
    /// マウス位置から最近傍ボーンを選択する。
    /// </summary>
    private bool TryPickBone(Vector2 mousePos, Rect previewRect, Vector3 camPos, Vector3 lookAt,
                              bool shift, bool ctrl)
    {
        if (_model == null || _meshContextList == null) return false;

        int bestIndex = -1;
        float bestDist = BonePickRadius;

        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var meshCtx = _meshContextList[i];
            if (meshCtx == null || meshCtx.Type != MeshType.Bone)
                continue;

            var wm = meshCtx.WorldMatrix;
            Vector3 boneWorldPos = new Vector3(wm.m03, wm.m13, wm.m23);
            Vector2 screenPos = WorldToPreviewPos(boneWorldPos, previewRect, camPos, lookAt);

            float dist = Vector2.Distance(mousePos, screenPos);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        if (bestIndex < 0) return false;

        if (ctrl)
            _model.ToggleSelection(bestIndex);
        else if (shift)
            _model.AddToBoneSelection(bestIndex);
        else
            _model.SelectBone(bestIndex);

        _model.IsDirty = true;
        _model.OnListChanged?.Invoke();
        _toolManager?.toolContext?.OnMeshSelectionChanged?.Invoke();
        Repaint();

        return true;
    }

    // ================================================================
    // ギズモ中心更新
    // ================================================================

    private void UpdateBoneGizmoCenter()
    {
        if (_model == null || !_model.HasBoneSelection)
        {
            _boneAxisGizmo.Center = Vector3.zero;
            return;
        }

        // 選択ボーンのWorldPosition重心
        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (int idx in _model.SelectedBoneIndices)
        {
            var meshCtx = _model.GetMeshContext(idx);
            if (meshCtx == null) continue;

            var wm = meshCtx.WorldMatrix;
            sum += new Vector3(wm.m03, wm.m13, wm.m23);
            count++;
        }

        _boneAxisGizmo.Center = count > 0 ? sum / count : Vector3.zero;
    }

    // ================================================================
    // 移動適用
    // ================================================================

    /// <summary>自由移動（中央ドラッグ）のデルタをRestPositionに適用</summary>
    private void ApplyBoneDelta(Vector2 screenDelta, ToolContext ctx)
    {
        Vector3 worldDelta = _boneAxisGizmo.ComputeFreeDelta(screenDelta, ctx);
        ApplyBoneWorldDelta(worldDelta);
    }

    /// <summary>軸拘束ドラッグのデルタをRestPositionに適用</summary>
    private void ApplyBoneAxisDelta(Vector2 screenDelta, ToolContext ctx)
    {
        Vector3 worldDelta = _boneAxisGizmo.ComputeAxisDelta(screenDelta, _boneDragAxis, ctx);
        ApplyBoneWorldDelta(worldDelta);
    }

    /// <summary>ワールドデルタを選択ボーンのRestPositionに加算</summary>
    private void ApplyBoneWorldDelta(Vector3 worldDelta)
    {
        if (worldDelta.sqrMagnitude < 1e-10f) return;
        if (_model == null) return;

        foreach (int idx in _model.SelectedBoneIndices)
        {
            var meshCtx = _model.GetMeshContext(idx);
            if (meshCtx == null) continue;

            EnsureBonePoseData(meshCtx);

            meshCtx.BonePoseData.RestPosition += worldDelta;
            meshCtx.BonePoseData.SetDirty();
        }

        _model.ComputeWorldMatrices();
        _unifiedAdapter?.RequestNormal();
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
            var meshCtx = _model.GetMeshContext(idx);
            if (meshCtx == null) continue;

            EnsureBonePoseData(meshCtx);
            _boneDragBeforeSnapshots[idx] = meshCtx.BonePoseData.CreateSnapshot();
        }
    }

    /// <summary>
    /// BonePoseDataがnullの場合に初期化する。
    /// BoneTransformの位置はbaseMatrixとして乗算されるため、
    /// BonePoseData.RestPositionは(0,0,0)で初期化して差分管理する。
    /// </summary>
    private static void EnsureBonePoseData(Poly_Ling.Data.MeshContext meshCtx)
    {
        if (meshCtx.BonePoseData != null) return;
        meshCtx.BonePoseData = new Poly_Ling.Data.BonePoseData();
        meshCtx.BonePoseData.IsActive = true;
    }

    private void CommitBoneDragUndo()
    {
        if (_boneDragBeforeSnapshots.Count == 0) return;

        var undoCtrl = _undoController;
        if (undoCtrl == null) return;

        var record = new MultiBonePoseChangeRecord();
        foreach (var kvp in _boneDragBeforeSnapshots)
        {
            int idx = kvp.Key;
            var meshCtx = _model?.GetMeshContext(idx);

            record.Entries.Add(new MultiBonePoseChangeRecord.Entry
            {
                MasterIndex = idx,
                OldSnapshot = kvp.Value,
                NewSnapshot = meshCtx?.BonePoseData?.CreateSnapshot(),
            });
        }

        undoCtrl.MeshListStack.Record(record, "ボーン移動");
        undoCtrl.FocusMeshList();

        _model?.OnListChanged?.Invoke();
        _boneDragBeforeSnapshots.Clear();
    }

    // ================================================================
    // 状態リセット
    // ================================================================

    private void ResetBoneDragState()
    {
        _boneDragState = BoneDragState.Idle;
        _boneDragAxis = AxisGizmo.AxisType.None;
        _boneAxisGizmo.DraggingAxis = AxisGizmo.AxisType.None;
        _boneAxisGizmo.HoveredAxis = AxisGizmo.AxisType.None;
    }
}
