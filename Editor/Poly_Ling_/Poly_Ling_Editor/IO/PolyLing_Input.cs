// Assets/Editor/PolyLing.Input.cs
// 入力処理（マウスイベント、クリック処理、矩形選択）
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.Commands;

public partial class PolyLing
{
    // ================================================================
    // ホバー/クリック処理
    // ================================================================
    // 
    // UnifiedSystemがホバー計算を行い、結果を _lastHoverHitResult に反映。
    // クリック時は _lastHoverHitResult を使用して選択処理を行う。
    // ================================================================

    // ホバー判定の閾値
    private const float HOVER_VERTEX_RADIUS = 12f;   // 頂点ヒット判定半径（ピクセル）
    private const float HOVER_LINE_DISTANCE = 18f;   // 線分ヒット判定距離（ピクセル）

    // ホバー状態の保存（クリック時に使用）
    private Vector2 _lastHoverMousePos;
    private Poly_Ling.Rendering.GPUHitTestResult _lastHoverHitResult;

    // ================================================================
    // 入力処理（MeshObjectベース）
    // ================================================================
    private void HandleInput(Rect rect, MeshContext meshContext, Vector3 camPos, Vector3 lookAt, float camDist)
    {
        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;

        // ツールコンテキストを更新
        UpdateToolContext(meshContext, rect, camPos, camDist);

        // プレビュー領域内でのホイールズーム
        if (e.type == EventType.ScrollWheel && rect.Contains(mousePos))
        {
            _cameraDistance *= (1f + e.delta.y * 0.05f);
            _cameraDistance = Mathf.Clamp(_cameraDistance, 0.1f, 10f);
            e.Use();
            Repaint();
            return;
        }

        // プレビュー外でのMouseUp処理
        if (!rect.Contains(mousePos))
        {
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                //トポロジー変更スナップショット保存
                RecordSelectionChangeIfNeeded();

                // ツールにもMouseUpを通知（状態リセットのため）
                _currentTool?.OnMouseUp(_toolContext, mousePos);

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

                    // ================================================================
                    // ★ Phase 7: ツールを先に処理、軸ギズモヒット時は選択処理をスキップ
                    // 理由: 軸ギズモ上のクリックが「空白クリック」と判定され
                    //       選択がクリアされてしまう問題を回避
                    // ================================================================

                    // 1. まずツールに処理を委譲（軸ギズモヒット判定）
                    bool toolHandled = false;
                    if (_currentTool != null)
                    {
                        toolHandled = _currentTool.OnMouseDown(_toolContext, mousePos);
                    }

                    // 2. ツールが処理しなかった場合のみ選択処理
                    if (!toolHandled)
                    {
                        OnMouseDown(e, mousePos, meshObject, rect, camPos, lookAt, handleRadius, meshContext);

                        // 選択更新後、再度ツールに通知（選択済み頂点の移動準備）
                        _currentTool?.OnMouseDown(_toolContext, mousePos);
                    }
                    else
                    {
                        e.Use();
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
    /// <summary>
    /// マウス移動時のホバー更新
    /// 実際のホバー計算はUpdateUnifiedFrameで実行済み
    /// ここではマウス位置の保存とRepaintのみ行う
    /// </summary>
    private void UpdateHoverOnMouseMove(Vector2 mousePos, Rect rect)
    {
        if (!rect.Contains(mousePos))
            return;

        if (Vector2.Distance(mousePos, _lastHoverMousePos) < 1f)
            return;

        _lastHoverMousePos = mousePos;

        // ホバー結果はUnifiedSystemから取得（UpdateUnifiedFrameで更新済み）
        UpdateLastHoverHitResultFromUnified();

        Repaint();
    }

    /// <summary>
    /// UnifiedSystemからホバー結果を取得して_lastHoverHitResultを更新
    /// Note: インデックスはグローバルインデックスとして保持
    /// </summary>
    private void UpdateLastHoverHitResultFromUnified()
    {
        if (_unifiedAdapter == null)
            return;

        var bufferManager = _unifiedAdapter.BufferManager;
        if (bufferManager == null)
            return;

        // グローバルインデックスを取得
        int globalVertex = _unifiedAdapter.HoverVertexIndex;
        int globalLine = _unifiedAdapter.HoverLineIndex;
        int globalFace = _unifiedAdapter.HoverFaceIndex;

        // 選択中メッシュのインデックスかチェックし、ローカルインデックスに変換
        int localVertex = -1;
        int localFace = -1;
        float vertexDist = float.MaxValue;
        float lineDist = float.MaxValue;

        if (globalVertex >= 0)
        {
            if (bufferManager.GlobalToLocalVertexIndex(globalVertex, out int meshIdx, out int localIdx))
            {
                if (meshIdx == _selectedIndex)
                {
                    localVertex = localIdx;
                    vertexDist = 0f;
                }
            }
        }

        // 線分はグローバルインデックスのまま保持（_edgeCacheはグローバルリスト）
        // ただし選択中メッシュの線分かチェック
        int validGlobalLine = -1;
        if (globalLine >= 0)
        {
            if (bufferManager.GlobalToLocalLineIndex(globalLine, out int meshIdx, out int localIdx))
            {
                if (meshIdx == _selectedIndex)
                {
                    validGlobalLine = globalLine;  // グローバルインデックスを保持
                    lineDist = 0f;
                }
            }
        }

        // 面はローカルインデックスに変換
        if (globalFace >= 0)
        {
            if (bufferManager.GlobalToLocalFaceIndex(globalFace, out int meshIdx, out int localIdx))
            {
                if (meshIdx == _selectedIndex)
                {
                    localFace = localIdx;
                }
            }
        }

        _lastHoverHitResult = new Poly_Ling.Rendering.GPUHitTestResult
        {
            NearestVertexIndex = localVertex,
            NearestVertexDistance = vertexDist,
            NearestVertexDepth = 0f,
            NearestLineIndex = validGlobalLine,  // グローバルインデックス
            NearestLineDistance = lineDist,
            NearestLineDepth = 0f,
            HitFaceIndices = localFace >= 0 ? new int[] { localFace } : null
        };
    }
    /// <summary>
    /// MouseDown処理（共通の選択処理）
    /// 
    /// 【Phase 6: MouseDown時即時選択 + 選択済みドラッグ対応】
    /// - 選択済み要素をクリック → 選択変更なし（ドラッグで移動可能）
    /// - 未選択要素をクリック → その要素を選択（Shift/Ctrlで追加）
    /// - 空白クリック → 全選択クリア
    /// </summary>
    private void OnMouseDown(Event e, Vector2 mousePos, MeshObject meshObject, Rect rect,
        Vector3 camPos, Vector3 lookAt, float handleRadius, MeshContext meshContext)
    {
        if (_editState != VertexEditState.Idle)
            return;

        _mouseDownScreenPos = mousePos;

        // 修飾キー状態を取得
        bool shiftHeld = e.shift;
        bool ctrlHeld = e.control;

        // MeshObject を TopologyCache に設定
        _meshTopology?.SetMeshObject(meshObject);

        // ================================================================
        // ★ ホバー結果から _hitResultOnMouseDown を構築
        // UnifiedSystemのホバー結果を直接使用
        // ================================================================
        _hitResultOnMouseDown = HitResult.None;
        var currentMode = _selectionState?.Mode ?? MeshSelectMode.Vertex;
        var bufferManager = _unifiedAdapter?.BufferManager;

        if (bufferManager != null)
        {
            // グローバルインデックスを取得
            int globalVertex = _unifiedAdapter.HoverVertexIndex;
            int globalLine = _unifiedAdapter.HoverLineIndex;
            int globalFace = _unifiedAdapter.HoverFaceIndex;

            // 頂点ヒット判定（頂点モードの場合のみ）
            if (currentMode.Has(MeshSelectMode.Vertex) && globalVertex >= 0)
            {
                if (bufferManager.GlobalToLocalVertexIndex(globalVertex, out int meshIdx, out int localVertex))
                {
                    if (meshIdx == _selectedIndex)
                    {
                        _hitResultOnMouseDown = new HitResult
                        {
                            HitType = MeshSelectMode.Vertex,
                            VertexIndex = localVertex,
                            EdgePair = null,
                            FaceIndex = -1,
                            LineIndex = -1
                        };
                    }
                }
            }
            
            // 線分ヒット判定（Edge/Lineモードの場合）
            if (_hitResultOnMouseDown.HitType == MeshSelectMode.None &&
                (currentMode.Has(MeshSelectMode.Edge) || currentMode.Has(MeshSelectMode.Line)) && 
                globalLine >= 0)
            {
                if (bufferManager.GetLineVerticesLocal(globalLine, out int meshIdx, out int localV1, out int localV2))
                {
                    if (meshIdx == _selectedIndex)
                    {
                        // LineTypeを取得（UnifiedLineから）
                        if (bufferManager.GetLineType(globalLine, out bool isAuxLine))
                        {
                            if (isAuxLine && currentMode.Has(MeshSelectMode.Line))
                            {
                                // 補助線 → Line選択
                                if (bufferManager.GetLineFaceIndex(globalLine, out int faceIndex))
                                {
                                    _hitResultOnMouseDown = new HitResult
                                    {
                                        HitType = MeshSelectMode.Line,
                                        VertexIndex = -1,
                                        EdgePair = null,
                                        FaceIndex = -1,
                                        LineIndex = faceIndex
                                    };
                                }
                            }
                            else if (!isAuxLine && currentMode.Has(MeshSelectMode.Edge))
                            {
                                // 通常エッジ → Edge選択
                                _hitResultOnMouseDown = new HitResult
                                {
                                    HitType = MeshSelectMode.Edge,
                                    VertexIndex = -1,
                                    EdgePair = new VertexPair(localV1, localV2),
                                    FaceIndex = -1,
                                    LineIndex = -1
                                };
                            }
                        }
                    }
                }
            }
            
            // 面ヒット判定（Faceモードの場合）
            if (_hitResultOnMouseDown.HitType == MeshSelectMode.None &&
                currentMode.Has(MeshSelectMode.Face) && globalFace >= 0)
            {
                if (bufferManager.GlobalToLocalFaceIndex(globalFace, out int meshIdx, out int localFace))
                {
                    if (meshIdx == _selectedIndex)
                    {
                        _hitResultOnMouseDown = new HitResult
                        {
                            HitType = MeshSelectMode.Face,
                            VertexIndex = -1,
                            EdgePair = null,
                            FaceIndex = localFace,
                            LineIndex = -1
                        };
                    }
                }
            }
        }

        // ================================================================
        // ★ Phase 6: 選択済み要素クリック時は選択変更なし（Blender風）
        // ================================================================
        bool hitIsAlreadySelected = IsHitResultAlreadySelected(_hitResultOnMouseDown);

        if (!hitIsAlreadySelected)
        {
            // 未選択要素または空白クリック → 選択を更新
            ApplySelectionOnMouseDown(shiftHeld, ctrlHeld);
        }
        // else: 選択済み要素 → 選択変更なし（ドラッグで移動可能）

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
    /// ヒット結果が既に選択済みかチェック
    /// </summary>
    private bool IsHitResultAlreadySelected(HitResult hit)
    {
        if (_selectionState == null)
            return false;

        switch (hit.HitType)
        {
            case MeshSelectMode.Vertex:
                return hit.VertexIndex >= 0 && _selectionState.Vertices.Contains(hit.VertexIndex);

            case MeshSelectMode.Edge:
                return hit.EdgePair.HasValue && _selectionState.Edges.Contains(hit.EdgePair.Value);

            case MeshSelectMode.Face:
                return hit.FaceIndex >= 0 && _selectionState.Faces.Contains(hit.FaceIndex);

            case MeshSelectMode.Line:
                return hit.LineIndex >= 0 && _selectionState.Lines.Contains(hit.LineIndex);

            default:
                return false;
        }
    }

    /// <summary>
    /// MouseDown時に選択を即座に反映
    /// 
    /// 【Phase 6追加】
    /// - 他モードの選択をクリア（非加算モード時）
    /// - _selectedVertices を即座に同期
    /// - ToolContext.SelectedVertices/SelectionState も更新（MoveToolが参照）
    /// </summary>
    private void ApplySelectionOnMouseDown(bool shiftHeld, bool ctrlHeld)
    {
        if (_selectionState == null || _selectionOps == null)
            return;

        // 選択を適用（他モードクリア含む）
        _selectionOps.ApplyHitResult(_hitResultOnMouseDown, shiftHeld, ctrlHeld);

        // _selectedVertices を即座に同期（レガシー互換 + ToolContext用）
        SyncSelectedVerticesFromState();

        // ToolContextも更新（MoveToolが参照する）
        if (_toolManager?.toolContext != null)
        {
            _toolManager.toolContext.SelectedVertices = _selectedVertices;
            _toolManager.toolContext.SelectionState = _selectionState;  // ★ SelectionStateも更新
        }

        // UnifiedSystemに選択変更を通知
        _unifiedAdapter?.UnifiedSystem?.ProcessSelectionUpdate();

        // 表示を即座に更新
        Repaint();
    }

    /// <summary>
    /// SelectionStateから_selectedVerticesへ同期
    /// </summary>
    private void SyncSelectedVerticesFromState()
    {
        _selectedVertices.Clear();

        // Vertex選択
        if (_selectionState.Vertices != null)
        {
            foreach (var v in _selectionState.Vertices)
            {
                _selectedVertices.Add(v);
            }
        }

        // Edge/Face/Line選択時は関連する頂点も追加
        var meshObject = _model?.CurrentMeshContext?.MeshObject;
        if (meshObject != null)
        {
            var affected = _selectionState.GetAllAffectedVertices(meshObject);
            foreach (var v in affected)
            {
                _selectedVertices.Add(v);
            }
        }
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
                // ドラッグ閾値を超えたか（矩形選択とかの開始）判定
                float dragDistance = Vector2.Distance(mousePos, _mouseDownScreenPos);
                if (dragDistance > DragThreshold)
                {
                    // 空白から開始 → 矩形選択モード
                    if (_hitVertexOnMouseDown < 0)
                    {
                        StartBoxSelect(_mouseDownScreenPos);
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
                HandleClick(shiftHeld, ctrlHeld, meshObject, rect, camPos, lookAt, handleRadius);
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

    /// <summary>
    /// クリック確定時の処理（MouseUp時）
    /// 
    /// 【Phase 6変更】
    /// 選択自体はMouseDown時に既に反映済み。
    /// ここではUndo記録のみを行う。
    /// </summary>
    private void HandleClick(bool shiftHeld, bool ctrlHeld, MeshObject meshObject, Rect rect, Vector3 camPos, Vector3 lookAt, float handleRadius)
    {
        // MouseDown時に選択は既に反映済みなので、ここでは何もしない
        // Undo記録はRecordSelectionChangeIfNeeded()で行われる
    }

    /// <summary>
    /// 選択変更をUndoスタックに記録（WorkPlane原点連動・キュー経由）
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

        // Undo記録（キュー経由）
        _commandQueue?.Enqueue(new RecordSelectionChangeCommand(
            _undoController,
            oldSelection, newSelection,
            oldWorkPlane, newWorkPlane));
    }
    /// <summary>
    /// 拡張選択変更をUndoスタックに記録（キュー経由）
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

        // Undo記録（キュー経由）
        _commandQueue?.Enqueue(new RecordExtendedSelectionChangeCommand(
            _undoController,
            oldSnapshot,
            newSnapshot,
            oldLegacyVertices,
            newLegacyVertices,
            oldWorkPlane,
            newWorkPlane));

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

        // 表示用トランスフォーム行列を取得（トランスフォーム表示モード対応）
        Matrix4x4 displayMatrix = GetDisplayMatrix(_selectedIndex);

        // ワールド→スクリーン変換デリゲート（トランスフォーム行列適用）
        Func<Vector3, Vector2> worldToScreen = (worldPos) =>
        {
            Vector3 transformedPos = displayMatrix.MultiplyPoint3x4(worldPos);
            return WorldToPreviewPos(transformedPos, previewRect, camPos, lookAt);
        };

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

            // UnifiedSystemに選択変更を通知
            _unifiedAdapter?.UnifiedSystem?.ProcessSelectionUpdate();
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

        // Undo記録（コマンドキュー経由）
        _commandQueue?.Enqueue(new RecordExtendedSelectionChangeCommand(
            _undoController,
            _selectionSnapshotOnMouseDown,
            afterSnapshot,
            _legacySelectionOnMouseDown,
            newLegacyVertices,
            _workPlaneSnapshotOnMouseDown,
            newWorkPlane
        ));

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