// Assets/Editor/SimpleMeshFactory.Input.cs
// 入力処理（マウスイベント、クリック処理、矩形選択）
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools;
using MeshFactory.Selection;

public partial class SimpleMeshFactory
{

    // フィールド追加（クラスの先頭付近）
    private const float HOVER_VERTEX_RADIUS = 10f;
    private const float HOVER_LINE_DISTANCE = 5f;
    private Vector2 _lastHoverMousePos;

    // ================================================================
    // 入力処理（MeshObjectベース）
    // ================================================================
    private void HandleInput(Rect rect, MeshContext meshContext, Vector3 camPos, Vector3 lookAt, float camDist)
    {
        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;

        // ツールコンテキストを更新
        UpdateToolContext(meshContext, rect, camPos, camDist);

        // プレビュー外でのMouseUp処理
        if (!rect.Contains(mousePos))
        {
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                //トポロジー変更スナップショット保存
                RecordSelectionChangeIfNeeded();
                HandleMouseUpOutside(meshContext, rect, camPos, lookAt);
            }
            return;
        }

        // 右ドラッグ: カメラ回転（常に有効）
        HandleCameraRotation(e);

        // 頂点編集モードでなければ終了
        if (!_vertexEditMode)
            return;

        var meshObject = meshContext.MeshObject;
        if (meshObject == null)
            return;

        float handleRadius = 10f;

        // キーボードショートカット
        if (e.type == EventType.KeyDown)
        {
            HandleKeyboardShortcuts(e, meshContext);
        }

        switch (e.type)
        {
            case EventType.MouseDown:
                if (e.button == 0)
                {
                    // スナップショット取得
                    CaptureSelectionSnapshotForUndo();

                    // まずツールに処理を委譲
                    if (_currentTool != null && _currentTool.OnMouseDown(_toolContext, mousePos))
                    {
                        e.Use();
                    }
                    else
                    {
                        // ツールが処理しなければ共通の選択処理
                        OnMouseDown(e, mousePos, meshObject, rect, camPos, lookAt, handleRadius, meshContext);
                    }
                }
                else if (e.button == 1)
                {
                    // 右クリック：まずツールに委譲（AddFaceTool等の点取り消し用）
                    if (_currentTool != null && _currentTool.OnMouseDown(_toolContext, mousePos))
                    {
                        e.Use();
                    }
                    // ツールが処理しなければカメラ操作（下のHandleCameraInputで処理）
                }
                break;

            case EventType.MouseDrag:
                if (e.button == 0)
                {
                    // ツールに処理を委譲
                    if (_currentTool != null && _currentTool.OnMouseDrag(_toolContext, mousePos, e.delta))
                    {
                        e.Use();
                    }
                    else
                    {
                        OnMouseDrag(e, mousePos, meshObject, rect, camPos, lookAt, camDist, meshContext);
                    }
                }
                break;

            case EventType.MouseUp:
                if (e.button == 0)
                {
                    // ツールに処理を委譲
                    if (_currentTool != null && _currentTool.OnMouseUp(_toolContext, mousePos))
                    {
                        ResetEditState();  // ツールが処理した場合も状態リセット
                        e.Use();
                    }
                    else
                    {
                        OnMouseUp(e, mousePos, meshObject, rect, camPos, lookAt, handleRadius, meshContext);
                    }
                    // 選択変更検出
                    RecordSelectionChangeIfNeeded();

                }
                break;

            case EventType.MouseMove:
                // マウス移動時にツールのプレビュー更新を呼ぶ
                if (_currentTool != null)
                {
                    _currentTool.OnMouseDrag(_toolContext, mousePos, Vector2.zero);
                    Repaint();
                }
                if (e.type == EventType.MouseMove)
                {
                    UpdateHoverOnMouseMove(e.mousePosition, rect);
                }

                break;
        }
    }
    // メソッド追加
    private void UpdateHoverOnMouseMove(Vector2 mousePos, Rect rect)
    {
        if (!rect.Contains(mousePos))
        {
            _gpuRenderer?.ClearHoverState();
            return;
        }

        if (Vector2.Distance(mousePos, _lastHoverMousePos) < 1f)
            return;

        _lastHoverMousePos = mousePos;

        if (_gpuRenderer == null || !_gpuRenderer.HitTestAvailable)
            return;

        float tabHeight = GUIUtility.GUIToScreenPoint(Vector2.zero).y - position.y;
        var hitResult = _gpuRenderer.DispatchHitTest(mousePos, rect, tabHeight);

        // デバッグ検証付きでホバー更新
        _gpuRenderer.UpdateHoverState(hitResult, HOVER_VERTEX_RADIUS, HOVER_LINE_DISTANCE, mousePos, _edgeCache);
        Repaint();
    }
    /// <summary>
    /// MouseDown処理（共通の選択処理）
    /// </summary>
    private void OnMouseDown(Event e, Vector2 mousePos, MeshObject meshObject, Rect rect,
        Vector3 camPos, Vector3 lookAt, float handleRadius, MeshContext meshContext)
    {
        if (_editState != VertexEditState.Idle)
            return;

        _mouseDownScreenPos = mousePos;

        // ワールド→スクリーン変換デリゲート
        Func<Vector3, Vector2> worldToScreen = (worldPos) => WorldToPreviewPos(worldPos, rect, camPos, lookAt);

        // MeshObject を TopologyCache に設定
        _meshTopology?.SetMeshObject(meshObject);

        // 新選択システム: 有効なモードに応じたヒットテスト（優先順位付き）
        if (_selectionOps != null && _selectionState != null)
        {
            _hitResultOnMouseDown = _selectionOps.FindAtEnabledModes(mousePos, meshObject, worldToScreen, camPos);
        }
        // === ここに追加 ===
        ValidateHitTestOnClick(mousePos, rect, meshObject, camPos, lookAt, _hitResultOnMouseDown);
        // レガシー互換: ヒット結果がVertexの場合は従来の変数も更新
        if (_hitResultOnMouseDown.HitType == MeshSelectMode.Vertex && _hitResultOnMouseDown.VertexIndex >= 0)
        {
            _hitVertexOnMouseDown = _hitResultOnMouseDown.VertexIndex;
        }
        else
        {
            _hitVertexOnMouseDown = -1;
        }

        _editState = VertexEditState.PendingAction;
        e.Use();
    }

    /// <summary>
    /// MouseDrag処理
    /// </summary>
    private void OnMouseDrag(Event e, Vector2 mousePos, MeshObject meshObject, Rect rect,
        Vector3 camPos, Vector3 lookAt, float camDist, MeshContext meshContext)
    {
        switch (_editState)
        {
            case VertexEditState.PendingAction:
                // ドラッグ閾値を超えたか判定
                float dragDistance = Vector2.Distance(mousePos, _mouseDownScreenPos);
                if (dragDistance > DragThreshold)
                {
                    // 空白から開始 → 矩形選択モード
                    if (_hitVertexOnMouseDown < 0)
                    {
                        StartBoxSelect(mousePos);
                    }
                    else
                    {
                        // 頂点上からのドラッグはツールに委譲済み
                        // Selectツール時は何もしない
                        _editState = VertexEditState.Idle;
                    }
                }
                e.Use();
                Repaint();
                break;

            case VertexEditState.BoxSelecting:
                // 矩形選択範囲を更新
                _boxSelectEnd = mousePos;
                e.Use();
                Repaint();
                break;
        }
    }

    /// <summary>
    /// MouseUp処理（共通の選択処理）
    /// </summary>
    private void OnMouseUp(Event e, Vector2 mousePos, MeshObject meshObject, Rect rect,
        Vector3 camPos, Vector3 lookAt, float handleRadius, MeshContext meshContext)
    {
        bool shiftHeld = e.shift;
        bool ctrlHeld = e.control;

        switch (_editState)
        {
            case VertexEditState.PendingAction:
                // ドラッグなし = クリック
                HandleClick(shiftHeld, meshObject, rect, camPos, lookAt, handleRadius);
                break;

            case VertexEditState.BoxSelecting:
                // 矩形選択完了
                FinishBoxSelect(shiftHeld, ctrlHeld, meshObject, rect, camPos, lookAt);
                break;
        }

        ResetEditState();
        e.Use();
        Repaint();
    }

    /// <summary>
    /// プレビュー外でのMouseUp処理
    /// </summary>
    private void HandleMouseUpOutside(MeshContext meshContext, Rect rect, Vector3 camPos, Vector3 lookAt)
    {
        // ツールの状態をリセット
        _currentTool?.Reset();

        ResetEditState();
        Repaint();
    }

    /// <summary>
    /// 編集状態をリセット
    /// </summary>
    private void ResetEditState()
    {
        _editState = VertexEditState.Idle;
        _hitVertexOnMouseDown = -1;
        _hitResultOnMouseDown = HitResult.None;  // 追加
        _boxSelectStart = Vector2.zero;
        _boxSelectEnd = Vector2.zero;
    }
    // ================================================================
    // クリック処理
    // ================================================================
    private void HandleClick(bool shiftHeld, MeshObject meshObject, Rect rect, Vector3 camPos, Vector3 lookAt, float handleRadius)
    {
        HashSet<int> oldLegacySelection = new HashSet<int>(_selectedVertices);
        SelectionSnapshot oldSnapshot = _selectionState?.CreateSnapshot();
        bool selectionChanged = false;

        // SelectionOperationsを使用（複数モード対応）
        if (_selectionState != null && _selectionOps != null)
        {
            selectionChanged = _selectionOps.ApplyHitResult(_hitResultOnMouseDown, shiftHeld);

            // _selectedVertices と同期（レガシー互換）
            _selectedVertices.Clear();
            foreach (var v in _selectionState.Vertices)
            {
                _selectedVertices.Add(v);
            }
        }

        // 選択変更を記録
        if (selectionChanged)
        {
            RecordExtendedSelectionChange(oldSnapshot, oldLegacySelection);
        }
    }

    /// <summary>
    /// 選択変更をUndoスタックに記録（WorkPlane原点連動）
    /// </summary>
    private void RecordSelectionChange(HashSet<int> oldSelection, HashSet<int> newSelection)
    {
        if (_undoController == null)
            return;

        // MeshContextの選択状態も更新
        _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>(newSelection);

        var workPlane = _undoController.WorkPlane;
        WorkPlaneSnapshot? oldWorkPlane = null;
        WorkPlaneSnapshot? newWorkPlane = null;

        // AutoUpdate有効かつロックされていない場合、WorkPlane原点も連動
        if (workPlane != null && workPlane.AutoUpdateOriginOnSelection && !workPlane.IsLocked)
        {
            // 変更前のスナップショット
            oldWorkPlane = workPlane.CreateSnapshot();

            // WorkPlane原点を更新
            var meshContext = _model.CurrentMeshContext;
            if (meshContext?.MeshObject != null && newSelection.Count > 0)
            {
                workPlane.UpdateOriginFromSelection(meshContext.MeshObject, newSelection);
            }

            // 変更後のスナップショット
            newWorkPlane = workPlane.CreateSnapshot();

            // 変更がない場合はnullに戻す
            if (oldWorkPlane.HasValue && newWorkPlane.HasValue &&
                !oldWorkPlane.Value.IsDifferentFrom(newWorkPlane.Value))
            {
                oldWorkPlane = null;
                newWorkPlane = null;
            }
        }

        // Undo記録（WorkPlane連動版）
        _undoController.RecordSelectionChangeWithWorkPlane(
            oldSelection, newSelection,
            oldWorkPlane, newWorkPlane);
    }
    /// <summary>
    /// 拡張選択変更をUndoスタックに記録
    /// </summary>
    private void RecordExtendedSelectionChange(SelectionSnapshot oldSnapshot, HashSet<int> oldLegacyVertices)
    {
        if (_undoController == null || _selectionState == null)
            return;

        var newSnapshot = _selectionState.CreateSnapshot();
        var newLegacyVertices = new HashSet<int>(_selectedVertices);

        // MeshContextの選択状態も更新
        _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>(newLegacyVertices);

        var workPlane = _undoController.WorkPlane;
        WorkPlaneSnapshot? oldWorkPlane = null;
        WorkPlaneSnapshot? newWorkPlane = null;

        // AutoUpdate有効かつロックされていない場合、WorkPlane原点も連動
        if (workPlane != null && workPlane.AutoUpdateOriginOnSelection && !workPlane.IsLocked)
        {
            oldWorkPlane = workPlane.CreateSnapshot();

            var meshContext = _model.CurrentMeshContext;
            var affectedVertices = _selectionState.GetAllAffectedVertices(meshContext?.MeshObject);
            if (meshContext?.MeshObject != null && affectedVertices.Count > 0)
            {
                workPlane.UpdateOriginFromSelection(meshContext.MeshObject, affectedVertices);
            }

            newWorkPlane = workPlane.CreateSnapshot();

            if (oldWorkPlane.HasValue && newWorkPlane.HasValue &&
                !oldWorkPlane.Value.IsDifferentFrom(newWorkPlane.Value))
            {
                oldWorkPlane = null;
                newWorkPlane = null;
            }
        }

        // Undo記録
        _undoController.RecordExtendedSelectionChange(
            oldSnapshot,
            newSnapshot,
            oldLegacyVertices,
            newLegacyVertices,
            oldWorkPlane,
            newWorkPlane);

        // 最新スナップショットを保存
        _lastSelectionSnapshot = newSnapshot;
    }
    // ================================================================
    // 矩形選択
    // ================================================================
    private void StartBoxSelect(Vector2 startPos)
    {
        _boxSelectStart = startPos;
        _boxSelectEnd = startPos;
        _editState = VertexEditState.BoxSelecting;
    }

    private void FinishBoxSelect(bool shiftHeld, bool ctrlHeld, MeshObject meshObject, Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        HashSet<int> oldSelection = new HashSet<int>(_selectedVertices);
        SelectionSnapshot oldSnapshot = _selectionState?.CreateSnapshot();

        // 矩形を正規化
        Rect selectRect = new Rect(
            Mathf.Min(_boxSelectStart.x, _boxSelectEnd.x),
            Mathf.Min(_boxSelectStart.y, _boxSelectEnd.y),
            Mathf.Abs(_boxSelectEnd.x - _boxSelectStart.x),
            Mathf.Abs(_boxSelectEnd.y - _boxSelectStart.y)
        );

        // ワールド→スクリーン変換デリゲート
        Func<Vector3, Vector2> worldToScreen = (worldPos) => WorldToPreviewPos(worldPos, previewRect, camPos, lookAt);

        // SelectionOperationsを使用（複数モード対応）
        if (_selectionState != null && _selectionOps != null)
        {
            bool additive = shiftHeld || ctrlHeld;
            _selectionOps.SelectInRect(selectRect, meshObject, worldToScreen, additive);

            // _selectedVertices と同期（レガシー互換）
            _selectedVertices.Clear();
            foreach (var v in _selectionState.Vertices)
            {
                _selectedVertices.Add(v);
            }
        }

        // 選択が変更されていたら記録
        if (!oldSelection.SetEquals(_selectedVertices) ||
            (_selectionState != null && oldSnapshot != null && oldSnapshot.IsDifferentFrom(_selectionState.CreateSnapshot())))
        {
            RecordExtendedSelectionChange(oldSnapshot, oldSelection);
        }
    }

    // ================================================================
    // 【フェーズ2追加】選択Undo用メソッド
    // ================================================================
    // 
    // これらのメソッドを SimpleMeshFactory_Input.cs の末尾
    // （クラスの閉じ括弧 } の直前）に追加してください
    //

    /// <summary>
    /// 選択スナップショットを取得（MouseDown時に呼び出し）
    /// 
    /// 【フェーズ2追加】
    /// </summary>
    private void CaptureSelectionSnapshotForUndo()
    {
        // 選択スナップショット
        _selectionSnapshotOnMouseDown = _selectionState?.CreateSnapshot();

        // レガシー選択
        _legacySelectionOnMouseDown = _selectedVertices != null
            ? new HashSet<int>(_selectedVertices)
            : new HashSet<int>();

        // トポロジー変更フラグをリセット
        _topologyChangedDuringMouseOperation = false;

        // WorkPlane連動用（AutoUpdateOriginOnSelection有効時）
        var workPlane = _undoController?.WorkPlane;
        if (workPlane != null && workPlane.AutoUpdateOriginOnSelection && !workPlane.IsLocked)
        {
            _workPlaneSnapshotOnMouseDown = workPlane.CreateSnapshot();
        }
        else
        {
            _workPlaneSnapshotOnMouseDown = null;
        }
    }

    /// <summary>
    /// 選択変更があればUndo記録する（MouseUp時に呼び出し）
    /// 
    /// 【フェーズ2追加】
    /// </summary>
    private void RecordSelectionChangeIfNeeded()
    {
        // トポロジー変更があった場合はスキップ
        if (_topologyChangedDuringMouseOperation)
        {
            _topologyChangedDuringMouseOperation = false;
            _selectionSnapshotOnMouseDown = null;
            _legacySelectionOnMouseDown = null;
            _workPlaneSnapshotOnMouseDown = null;
            return;
        }

        // スナップショットがない場合はスキップ
        if (_selectionSnapshotOnMouseDown == null || _selectionState == null)
        {
            return;
        }

        // 変更チェック
        SelectionSnapshot afterSnapshot = _selectionState.CreateSnapshot();

        if (!_selectionSnapshotOnMouseDown.IsDifferentFrom(afterSnapshot))
        {
            // 変更なし
            _selectionSnapshotOnMouseDown = null;
            _legacySelectionOnMouseDown = null;
            _workPlaneSnapshotOnMouseDown = null;
            return;
        }

        // 選択が変更された → Undo記録
        HashSet<int> newLegacyVertices = _selectedVertices != null
            ? new HashSet<int>(_selectedVertices)
            : new HashSet<int>();

        if (_undoController?.MeshUndoContext != null)
        {
            _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>(newLegacyVertices);
        }

        // WorkPlane連動
        WorkPlaneSnapshot? newWorkPlane = null;
        var workPlane = _undoController?.WorkPlane;

        if (_workPlaneSnapshotOnMouseDown.HasValue &&
            workPlane != null &&
            workPlane.AutoUpdateOriginOnSelection &&
            !workPlane.IsLocked)
        {
            var meshContext = _model.CurrentMeshContext;
            var affectedVertices = _selectionState.GetAllAffectedVertices(meshContext?.MeshObject);
            if (meshContext?.MeshObject != null && affectedVertices.Count > 0)
            {
                workPlane.UpdateOriginFromSelection(meshContext.MeshObject, affectedVertices);
            }

            newWorkPlane = workPlane.CreateSnapshot();

            if (!_workPlaneSnapshotOnMouseDown.Value.IsDifferentFrom(newWorkPlane.Value))
            {
                newWorkPlane = null;
                _workPlaneSnapshotOnMouseDown = null;
            }
        }

        // Undo記録
        _undoController?.RecordExtendedSelectionChange(
            _selectionSnapshotOnMouseDown,
            afterSnapshot,
            _legacySelectionOnMouseDown,
            newLegacyVertices,
            _workPlaneSnapshotOnMouseDown,
            newWorkPlane
        );

        _lastSelectionSnapshot = afterSnapshot;

        // クリーンアップ
        _selectionSnapshotOnMouseDown = null;
        _legacySelectionOnMouseDown = null;
        _workPlaneSnapshotOnMouseDown = null;
    }

    /// <summary>
    /// トポロジー変更フラグを設定
    /// 【フェーズ2追加】
    /// </summary>
    public void SetTopologyChangedFlag()
    {
        _topologyChangedDuringMouseOperation = true;
    }
}
