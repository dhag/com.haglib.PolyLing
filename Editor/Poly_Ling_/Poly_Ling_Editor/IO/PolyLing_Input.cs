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

    // ホバー判定の閾値（MouseSettingsから取得）
    private float HOVER_VERTEX_RADIUS => _mouseSettings.HoverVertexRadius;
    private float HOVER_LINE_DISTANCE => _mouseSettings.HoverLineDistance;

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

        // プレビュー領域内でのホイール操作
        if (e.type == EventType.ScrollWheel && rect.Contains(mousePos))
        {
            // ホイールの値はdelta.xまたはdelta.yに入る（環境依存）
            float scrollValue = Mathf.Abs(e.delta.y) > Mathf.Abs(e.delta.x) ? e.delta.y : e.delta.x;

            if (e.shift)
            {
                // Shift+ホイール: 注目点をカメラ視線方向に前後移動
                // Note: Shiftは高速移動だが、ここでは既にShiftがトリガーなので修飾キー倍率は適用しない
                Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, _rotationZ);
                Vector3 forward = rot * Vector3.forward;
                float moveAmount = _mouseSettings.GetFocusPointZDelta(scrollValue, _cameraDistance);
                _cameraTarget += forward * moveAmount;
            }
            else
            {
                // 通常ホイール: ズーム（Ctrl押下時はゆっくり）
                _cameraDistance *= _mouseSettings.GetZoomMultiplier(scrollValue, e);
                _cameraDistance = Mathf.Clamp(_cameraDistance, 0.1f, 80f);
            }
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
                // v2.1: 複数メッシュ対応
                bool isSelectedMesh = meshIdx == _selectedIndex ||
                    (_model?.SelectedMeshIndices?.Contains(meshIdx) ?? false);
                if (isSelectedMesh)
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
                // v2.1: 複数メッシュ対応
                bool isSelectedMesh = meshIdx == _selectedIndex ||
                    (_model?.SelectedMeshIndices?.Contains(meshIdx) ?? false);
                if (isSelectedMesh)
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
                // v2.1: 複数メッシュ対応
                bool isSelectedMesh = meshIdx == _selectedIndex ||
                    (_model?.SelectedMeshIndices?.Contains(meshIdx) ?? false);
                if (isSelectedMesh)
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
        _hitMeshIndexOnMouseDown = -1;  // v2.1: ヒットしたメッシュをリセット
        var currentMode = _selectionState?.Mode ?? MeshSelectMode.Vertex;
        var bufferManager = _unifiedAdapter?.BufferManager;

        // v2.1: デバッグログ - ホバー状態を確認
        int globalVertex = _unifiedAdapter?.HoverVertexIndex ?? -1;
        int globalLine = _unifiedAdapter?.HoverLineIndex ?? -1;
        int globalFace = _unifiedAdapter?.HoverFaceIndex ?? -1;
        Debug.Log($"[OnMouseDown] Hover: V={globalVertex}, L={globalLine}, F={globalFace}, mode={currentMode}, selV={_selectionState?.Vertices.Count ?? 0}");

        if (bufferManager != null)
        {
            // 頂点ヒット判定（頂点モードの場合のみ）
            if (currentMode.Has(MeshSelectMode.Vertex) && globalVertex >= 0)
            {
                if (bufferManager.GlobalToLocalVertexIndex(globalVertex, out int meshIdx, out int localVertex))
                {
                    Debug.Log($"[OnMouseDown] Vertex hit: meshIdx={meshIdx}, localVertex={localVertex}");
                    // v2.1: 複数メッシュ対応 - 選択中のメッシュならヒット
                    bool isSelectedMesh = meshIdx == _selectedIndex ||
                        (_model?.SelectedMeshIndices?.Contains(meshIdx) ?? false);
                    if (isSelectedMesh)
                    {
                        _hitMeshIndexOnMouseDown = meshIdx;  // v2.1: メッシュインデックスを保存
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
                    // v2.1: 複数メッシュ対応
                    bool isSelectedMesh = meshIdx == _selectedIndex ||
                        (_model?.SelectedMeshIndices?.Contains(meshIdx) ?? false);
                    if (isSelectedMesh)
                    {
                        _hitMeshIndexOnMouseDown = meshIdx;  // v2.1: メッシュインデックスを保存
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
                    // v2.1: 複数メッシュ対応
                    bool isSelectedMesh = meshIdx == _selectedIndex ||
                        (_model?.SelectedMeshIndices?.Contains(meshIdx) ?? false);
                    if (isSelectedMesh)
                    {
                        _hitMeshIndexOnMouseDown = meshIdx;  // v2.1: メッシュインデックスを保存
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

        // v2.1: ヒットなしでも選択頂点がある場合、その上でクリックしたかチェック
        // ワールドモードでホバーが検出されない場合の対策
        if (!hitIsAlreadySelected && _hitResultOnMouseDown.HitType == MeshSelectMode.None)
        {
            // 選択済み頂点があり、その近くでクリックした場合は選択を保持
            if (_selectionState != null && _selectionState.Vertices.Count > 0)
            {
                // 選択頂点のスクリーン位置をチェック
                var editorState = _undoController?.EditorState;
                bool useWorldTransform = editorState?.ShowWorldTransform ?? false;
                Vector3[] displayPositions = null;
                int vertexOffset = 0;

                if (useWorldTransform && _unifiedAdapter?.BufferManager != null)
                {
                    displayPositions = _unifiedAdapter.BufferManager.GetDisplayPositions();
                    vertexOffset = _unifiedAdapter.GetVertexOffset(_selectedIndex);
                }

                foreach (int vIdx in _selectionState.Vertices)
                {
                    Vector3 worldPos;
                    if (displayPositions != null)
                    {
                        int globalIdx = vertexOffset + vIdx;
                        worldPos = (globalIdx >= 0 && globalIdx < displayPositions.Length)
                            ? displayPositions[globalIdx]
                            : meshObject.Vertices[vIdx].Position;
                    }
                    else
                    {
                        worldPos = meshObject.Vertices[vIdx].Position;
                    }

                    Vector2 screenPos = WorldToPreviewPos(worldPos, rect, camPos, lookAt);
                    float dist = Vector2.Distance(screenPos, mousePos);

                    if (dist < handleRadius * 2f)  // 少し大きめの判定範囲
                    {
                        Debug.Log($"[OnMouseDown] Hit selected vertex {vIdx} at distance {dist}");
                        hitIsAlreadySelected = true;
                        break;
                    }
                }
            }
        }

        Debug.Log($"[OnMouseDown] hitIsAlreadySelected={hitIsAlreadySelected}, hitType={_hitResultOnMouseDown.HitType}");

        if (!hitIsAlreadySelected)
        {
            // 未選択要素または空白クリック → 選択を更新
            ApplySelectionOnMouseDown(shiftHeld, ctrlHeld);
        }
        // else: 選択済み要素 → 選択変更なし（ドラッグで移動可能、MouseUpでトグル判定）

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
    /// <summary>
    /// ヒット結果が既に選択されているかチェック
    /// 
    /// 【判定基準】
    /// - クリックした要素そのものが選択されているか
    /// - プライマリメッシュ: _selectionStateを見る
    /// - セカンダリメッシュ: MeshContextを見る
    /// </summary>
    private bool IsHitResultAlreadySelected(HitResult hit)
    {
        if (hit.HitType == MeshSelectMode.None)
            return false;

        int hitMeshIdx = _hitMeshIndexOnMouseDown;
        bool isSecondaryMesh = hitMeshIdx >= 0 && hitMeshIdx != _selectedIndex;

        if (isSecondaryMesh && _model != null)
        {
            // セカンダリメッシュ: MeshContextを見る
            var meshContext = _model.GetMeshContext(hitMeshIdx);
            if (meshContext == null)
                return false;

            switch (hit.HitType)
            {
                case MeshSelectMode.Vertex:
                    return hit.VertexIndex >= 0 && meshContext.SelectedVertices.Contains(hit.VertexIndex);
                case MeshSelectMode.Edge:
                    return hit.EdgePair.HasValue && meshContext.SelectedEdges.Contains(hit.EdgePair.Value);
                case MeshSelectMode.Face:
                    return hit.FaceIndex >= 0 && meshContext.SelectedFaces.Contains(hit.FaceIndex);
                case MeshSelectMode.Line:
                    return hit.LineIndex >= 0 && meshContext.SelectedLines.Contains(hit.LineIndex);
                default:
                    return false;
            }
        }
        else
        {
            // プライマリメッシュ: _selectionStateを見る
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
    }

    /// <summary>
    /// MouseDown時に選択を即座に反映
    /// 
    /// 【選択操作の仕様】
    /// ■ 修飾キーなし + 未選択要素: 全選択クリア→その要素を選択（連動ルール適用）
    /// ■ Shift/Ctrl + 未選択要素: 選択に追加（連動ルール適用）
    /// ■ 選択済み要素: MouseDownでは何もしない（ドラッグ移動の可能性）
    ///   → Ctrl+選択済みのMouseUp時の除外はHandleClickで処理
    /// 
    /// 【「選択済み要素」の判定】
    /// - クリックした要素そのものが選択されているか（IsHitResultAlreadySelected）
    /// - 線分Lをクリック → Lines.Contains(L) で判定
    /// - 連動で選択された頂点は考慮しない
    ///   例: 線分L(A-B)選択 → 頂点A,Bも連動選択される
    ///       その後、線分Lをクリック → Lines.Contains(L)で判定
    ///       頂点A,Bが選択されていても、線分L自体がLinesになければ「未選択」
    ///       → 線分Lが追加される（重複AddはHashSetで無視）
    /// 
    /// 【選択の連動ルール - 追加時】
    /// - 頂点Aを選択 → 頂点A
    /// - 線分L(A-B)を選択 → 線分L + 頂点A + 頂点B
    /// - 辺(A-B)を選択 → 辺(A-B) + 頂点A + 頂点B
    /// - 面Fを選択 → 面F + 面の全辺 + 面の全頂点
    /// 
    /// 【複数メッシュ対応】
    /// - プライマリ: _selectionStateを操作（_selectionOps.ApplyHitResult経由）
    /// - セカンダリ: MeshContextを操作
    /// - 「全選択クリア」は全メッシュの全モードをクリア
    /// </summary>
    private void ApplySelectionOnMouseDown(bool shiftHeld, bool ctrlHeld)
    {
        if (_selectionState == null || _selectionOps == null)
            return;

        bool additive = shiftHeld || ctrlHeld;
        int hitMeshIdx = _hitMeshIndexOnMouseDown;
        bool isSecondaryMesh = hitMeshIdx >= 0 && hitMeshIdx != _selectedIndex;

        // 非加算モードで全メッシュの全モード選択をクリア
        if (!additive && _model != null)
        {
            foreach (int meshIdx in _model.SelectedMeshIndices)
            {
                var meshContext = _model.GetMeshContext(meshIdx);
                meshContext?.ClearSelection();
            }
            _selectionState?.ClearAll();
        }

        // 選択を追加
        if (isSecondaryMesh && _model != null)
        {
            // セカンダリメッシュ: MeshContextに直接書き込む
            var hitMeshContext = _model.GetMeshContext(hitMeshIdx);
            if (hitMeshContext != null)
            {
                AddSelectionWithLinkage(hitMeshContext, _hitResultOnMouseDown);
            }
        }
        else
        {
            // プライマリメッシュ: 元の_selectionOps.ApplyHitResultを使用
            _selectionOps.ApplyHitResult(_hitResultOnMouseDown, shiftHeld, ctrlHeld);

            // _selectedVertices を即座に同期（レガシー互換 + ToolContext用）
            SyncSelectedVerticesFromState();

            // プライマリメッシュのMeshContextにも同期
            var primaryMeshContext = _model?.CurrentMeshContext;
            if (primaryMeshContext != null)
            {
                primaryMeshContext.LoadSelectionFrom(_selectionState);
            }
        }

        // ToolContextも更新（MoveToolが参照する）
        if (_toolManager?.toolContext != null)
        {
            _toolManager.toolContext.SelectedVertices = _selectedVertices;
            _toolManager.toolContext.SelectionState = _selectionState;
        }

        // UnifiedSystemに選択変更を通知
        _unifiedAdapter?.UnifiedSystem?.ProcessSelectionUpdate();

        // 表示を即座に更新
        Repaint();
    }

    /// <summary>
    /// 選択を追加（連動ルール適用）- MeshContext用（セカンダリメッシュ）
    /// 
    /// 【選択の連動ルール - 追加時】
    /// - 頂点Aを選択 → 頂点A
    /// - 線分L(A-B)を選択 → 線分L + 頂点A + 頂点B
    /// - 辺(A-B)を選択 → 辺(A-B) + 頂点A + 頂点B
    /// - 面Fを選択 → 面F + 面の全辺 + 面の全頂点
    /// </summary>
    private void AddSelectionWithLinkage(MeshContext meshContext, HitResult hit)
    {
        if (meshContext == null || hit.HitType == MeshSelectMode.None)
            return;

        var meshObject = meshContext.MeshObject;

        switch (hit.HitType)
        {
            case MeshSelectMode.Vertex:
                if (hit.VertexIndex >= 0)
                {
                    meshContext.SelectedVertices.Add(hit.VertexIndex);
                }
                break;

            case MeshSelectMode.Edge:
                if (hit.EdgePair.HasValue)
                {
                    meshContext.SelectedEdges.Add(hit.EdgePair.Value);
                    // 連動: 両端頂点も選択
                    meshContext.SelectedVertices.Add(hit.EdgePair.Value.V1);
                    meshContext.SelectedVertices.Add(hit.EdgePair.Value.V2);
                }
                break;

            case MeshSelectMode.Face:
                if (hit.FaceIndex >= 0 && meshObject != null && hit.FaceIndex < meshObject.FaceCount)
                {
                    meshContext.SelectedFaces.Add(hit.FaceIndex);
                    var face = meshObject.Faces[hit.FaceIndex];
                    // 連動: 面の全頂点を選択
                    foreach (var vIdx in face.VertexIndices)
                    {
                        meshContext.SelectedVertices.Add(vIdx);
                    }
                    // 連動: 面の全辺を選択
                    var verts = face.VertexIndices;
                    for (int i = 0; i < verts.Count; i++)
                    {
                        int v1 = verts[i];
                        int v2 = verts[(i + 1) % verts.Count];
                        meshContext.SelectedEdges.Add(new VertexPair(v1, v2));
                    }
                }
                break;

            case MeshSelectMode.Line:
                if (hit.LineIndex >= 0 && meshObject != null && hit.LineIndex < meshObject.FaceCount)
                {
                    meshContext.SelectedLines.Add(hit.LineIndex);
                    var lineFace = meshObject.Faces[hit.LineIndex];
                    // 連動: 線分の両端頂点を選択
                    if (lineFace.VertexCount == 2)
                    {
                        meshContext.SelectedVertices.Add(lineFace.VertexIndices[0]);
                        meshContext.SelectedVertices.Add(lineFace.VertexIndices[1]);
                    }
                }
                break;
        }
    }

    /// <summary>
    /// 選択を除外（連動ルール適用）- _selectionState用
    /// 
    /// 【選択の連動ルール - 除外時】
    /// - 頂点Aを解除 → 頂点A + 頂点Aを含む線分/辺/面 を解除
    /// - 辺(A-B)を解除 → 辺(A-B) + その辺を含む面 を解除（頂点A,Bは残る）
    /// - 線分Lを解除 → 線分Lのみ解除（頂点は残る）
    /// - 面Fを解除 → 面Fのみ解除（辺/頂点は残る）
    /// </summary>
    private void RemoveSelectionWithLinkageFromState(HitResult hit)
    {
        if (_selectionState == null || hit.HitType == MeshSelectMode.None)
            return;

        var meshObject = _model?.CurrentMeshContext?.MeshObject;

        switch (hit.HitType)
        {
            case MeshSelectMode.Vertex:
                if (hit.VertexIndex >= 0)
                {
                    int vIdx = hit.VertexIndex;
                    _selectionState.Vertices.Remove(vIdx);

                    // 連動: この頂点を含む辺を解除
                    var edgesToRemove = _selectionState.Edges
                        .Where(e => e.V1 == vIdx || e.V2 == vIdx).ToList();
                    foreach (var e in edgesToRemove)
                        _selectionState.Edges.Remove(e);

                    // 連動: この頂点を含む面を解除
                    if (meshObject != null)
                    {
                        var facesToRemove = _selectionState.Faces
                            .Where(f => f >= 0 && f < meshObject.FaceCount &&
                                        meshObject.Faces[f].VertexIndices.Contains(vIdx)).ToList();
                        foreach (var f in facesToRemove)
                            _selectionState.Faces.Remove(f);
                    }

                    // 連動: この頂点を含む線分を解除
                    if (meshObject != null)
                    {
                        var linesToRemove = _selectionState.Lines
                            .Where(l => l >= 0 && l < meshObject.FaceCount &&
                                        meshObject.Faces[l].VertexCount == 2 &&
                                        meshObject.Faces[l].VertexIndices.Contains(vIdx)).ToList();
                        foreach (var l in linesToRemove)
                            _selectionState.Lines.Remove(l);
                    }
                }
                break;

            case MeshSelectMode.Edge:
                if (hit.EdgePair.HasValue)
                {
                    var edge = hit.EdgePair.Value;
                    _selectionState.Edges.Remove(edge);

                    // 連動: この辺を含む面を解除
                    if (meshObject != null)
                    {
                        var facesToRemove = _selectionState.Faces
                            .Where(f => {
                                if (f < 0 || f >= meshObject.FaceCount) return false;
                                var verts = meshObject.Faces[f].VertexIndices;
                                for (int i = 0; i < verts.Count; i++)
                                {
                                    int v1 = verts[i];
                                    int v2 = verts[(i + 1) % verts.Count];
                                    if ((v1 == edge.V1 && v2 == edge.V2) || (v1 == edge.V2 && v2 == edge.V1))
                                        return true;
                                }
                                return false;
                            }).ToList();
                        foreach (var f in facesToRemove)
                            _selectionState.Faces.Remove(f);
                    }
                    // 頂点は残す
                }
                break;

            case MeshSelectMode.Line:
                if (hit.LineIndex >= 0)
                {
                    _selectionState.Lines.Remove(hit.LineIndex);
                    // 頂点は残す
                }
                break;

            case MeshSelectMode.Face:
                if (hit.FaceIndex >= 0)
                {
                    _selectionState.Faces.Remove(hit.FaceIndex);
                    // 辺/頂点は残す
                }
                break;
        }

        // 選択変更通知はSyncSelectedVerticesFromStateで行われる
    }

    /// <summary>
    /// 選択を除外（連動ルール適用）- MeshContext用（セカンダリメッシュ）
    /// 
    /// 【選択の連動ルール - 除外時】
    /// - 頂点Aを解除 → 頂点A + 頂点Aを含む線分/辺/面 を解除
    /// - 辺(A-B)を解除 → 辺(A-B) + その辺を含む面 を解除（頂点A,Bは残る）
    /// - 線分Lを解除 → 線分Lのみ解除（頂点は残る）
    /// - 面Fを解除 → 面Fのみ解除（辺/頂点は残る）
    /// </summary>
    private void RemoveSelectionWithLinkage(MeshContext meshContext, HitResult hit)
    {
        if (meshContext == null || hit.HitType == MeshSelectMode.None)
            return;

        var meshObject = meshContext.MeshObject;

        switch (hit.HitType)
        {
            case MeshSelectMode.Vertex:
                if (hit.VertexIndex >= 0)
                {
                    int vIdx = hit.VertexIndex;
                    meshContext.SelectedVertices.Remove(vIdx);

                    // 連動: この頂点を含む辺を解除
                    var edgesToRemove = meshContext.SelectedEdges
                        .Where(e => e.V1 == vIdx || e.V2 == vIdx).ToList();
                    foreach (var e in edgesToRemove)
                        meshContext.SelectedEdges.Remove(e);

                    // 連動: この頂点を含む面を解除
                    if (meshObject != null)
                    {
                        var facesToRemove = meshContext.SelectedFaces
                            .Where(f => f >= 0 && f < meshObject.FaceCount &&
                                        meshObject.Faces[f].VertexIndices.Contains(vIdx)).ToList();
                        foreach (var f in facesToRemove)
                            meshContext.SelectedFaces.Remove(f);
                    }

                    // 連動: この頂点を含む線分を解除
                    if (meshObject != null)
                    {
                        var linesToRemove = meshContext.SelectedLines
                            .Where(l => l >= 0 && l < meshObject.FaceCount &&
                                        meshObject.Faces[l].VertexCount == 2 &&
                                        meshObject.Faces[l].VertexIndices.Contains(vIdx)).ToList();
                        foreach (var l in linesToRemove)
                            meshContext.SelectedLines.Remove(l);
                    }
                }
                break;

            case MeshSelectMode.Edge:
                if (hit.EdgePair.HasValue)
                {
                    var edge = hit.EdgePair.Value;
                    meshContext.SelectedEdges.Remove(edge);

                    // 連動: この辺を含む面を解除
                    if (meshObject != null)
                    {
                        var facesToRemove = meshContext.SelectedFaces
                            .Where(f => {
                                if (f < 0 || f >= meshObject.FaceCount) return false;
                                var verts = meshObject.Faces[f].VertexIndices;
                                for (int i = 0; i < verts.Count; i++)
                                {
                                    int v1 = verts[i];
                                    int v2 = verts[(i + 1) % verts.Count];
                                    if ((v1 == edge.V1 && v2 == edge.V2) || (v1 == edge.V2 && v2 == edge.V1))
                                        return true;
                                }
                                return false;
                            }).ToList();
                        foreach (var f in facesToRemove)
                            meshContext.SelectedFaces.Remove(f);
                    }
                    // 頂点は残す
                }
                break;

            case MeshSelectMode.Line:
                if (hit.LineIndex >= 0)
                {
                    meshContext.SelectedLines.Remove(hit.LineIndex);
                    // 頂点は残す
                }
                break;

            case MeshSelectMode.Face:
                if (hit.FaceIndex >= 0)
                {
                    meshContext.SelectedFaces.Remove(hit.FaceIndex);
                    // 辺/頂点は残す
                }
                break;
        }
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
    /// 【Ctrl+選択済み要素クリックの仕様】
    /// - ドラッグなし（クリックのみ）: 選択から除外（連動ルール適用）
    /// - ドラッグあり: 移動する（選択は維持）
    /// 
    /// そのため、MouseDown時点では除外せず、MouseUp時（ここ）で判定する。
    /// ドラッグが発生した場合は_editStateがPendingActionではなくなるため、
    /// この関数は呼ばれない。
    /// 
    /// 【選択の連動ルール - 除外時】
    /// - 頂点Aを解除 → 頂点A + 頂点Aを含む線分/辺/面 を解除
    /// - 辺(A-B)を解除 → 辺(A-B) + その辺を含む面 を解除（頂点A,Bは残る）
    /// - 線分Lを解除 → 線分Lのみ解除（頂点は残る）
    /// - 面Fを解除 → 面Fのみ解除（辺/頂点は残る）
    /// </summary>
    private void HandleClick(bool shiftHeld, bool ctrlHeld, MeshObject meshObject, Rect rect, Vector3 camPos, Vector3 lookAt, float handleRadius)
    {
        // Ctrl+選択済み要素の場合、ここで除外を実行
        // （MouseDown時はドラッグ移動の可能性があるため保留していた）
        if (ctrlHeld && _hitResultOnMouseDown.HitType != MeshSelectMode.None)
        {
            bool hitIsAlreadySelected = IsHitResultAlreadySelected(_hitResultOnMouseDown);
            if (hitIsAlreadySelected)
            {
                // 選択済み要素をCtrl+クリック → 除外（連動ルール適用）
                int hitMeshIdx = _hitMeshIndexOnMouseDown;
                bool isSecondaryMesh = hitMeshIdx >= 0 && hitMeshIdx != _selectedIndex;

                if (isSecondaryMesh && _model != null)
                {
                    // セカンダリメッシュ: MeshContextから除外
                    var hitMeshContext = _model.GetMeshContext(hitMeshIdx);
                    if (hitMeshContext != null)
                    {
                        RemoveSelectionWithLinkage(hitMeshContext, _hitResultOnMouseDown);
                    }
                }
                else
                {
                    // プライマリメッシュ: _selectionStateから除外
                    RemoveSelectionWithLinkageFromState(_hitResultOnMouseDown);

                    // _selectedVertices を即座に同期
                    SyncSelectedVerticesFromState();

                    // プライマリメッシュのMeshContextにも同期
                    var primaryMeshContext = _model?.CurrentMeshContext;
                    if (primaryMeshContext != null)
                    {
                        primaryMeshContext.LoadSelectionFrom(_selectionState);
                    }
                }

                // ToolContextも更新
                if (_toolManager?.toolContext != null)
                {
                    _toolManager.toolContext.SelectedVertices = _selectedVertices;
                    _toolManager.toolContext.SelectionState = _selectionState;
                }

                // UnifiedSystemに選択変更を通知
                _unifiedAdapter?.UnifiedSystem?.ProcessSelectionUpdate();

                // 表示を即座に更新
                Repaint();
            }
        }
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
        // カリング情報をGPUからCPUに読み戻す（背面カリング対応）
        _unifiedAdapter?.ReadBackVertexFlags();

        HashSet<int> oldSelection = new HashSet<int>(_selectedVertices);
        SelectionSnapshot oldSnapshot = _selectionState?.CreateSnapshot();

        // 矩形を正規化
        Rect selectRect = new Rect(
            Mathf.Min(_boxSelectStart.x, _boxSelectEnd.x),
            Mathf.Min(_boxSelectStart.y, _boxSelectEnd.y),
            Mathf.Abs(_boxSelectEnd.x - _boxSelectStart.x),
            Mathf.Abs(_boxSelectEnd.y - _boxSelectStart.y)
        );

        // v2.1: ワールドモード判定
        var editorState = _undoController?.EditorState;
        bool useWorldTransform = editorState?.ShowWorldTransform ?? false;

        // GPU変換後の座標を取得（ワールドモード時のみ）
        Vector3[] displayPositions = null;
        if (useWorldTransform && _unifiedAdapter?.BufferManager != null)
        {
            displayPositions = _unifiedAdapter.BufferManager.GetDisplayPositions();
        }

        bool additive = shiftHeld || ctrlHeld;

        // 選択モード取得
        var currentMode = _selectionState?.Mode ?? MeshSelectMode.Vertex;

        // v2.1: 複数メッシュ対応 - 選択中の全メッシュに対して矩形選択を実行
        var selectedMeshIndices = _model?.SelectedMeshIndices;
        Debug.Log($"[FinishBoxSelect] _model={_model?.GetHashCode()}, toolCtx.Model={_toolContext?.Model?.GetHashCode()}, same={_model == _toolContext?.Model}");

        if (selectedMeshIndices != null && selectedMeshIndices.Count > 0)
        {
            // 加算モードでない場合、全メッシュの選択をクリア
            if (!additive)
            {
                foreach (int meshIdx in selectedMeshIndices)
                {
                    var ctx = _model.GetMeshContext(meshIdx);
                    ctx?.ClearSelection();
                }
                _selectionState?.ClearAll();
            }

            foreach (int meshIdx in selectedMeshIndices)
            {
                var meshContext = _model.GetMeshContext(meshIdx);
                if (meshContext == null || meshContext.MeshObject == null)
                    continue;

                var targetMeshObject = meshContext.MeshObject;

                // 表示用トランスフォーム行列を取得
                Matrix4x4 displayMatrix = GetDisplayMatrix(meshIdx);

                // 頂点オフセットを取得
                int vertexOffset = _unifiedAdapter?.GetVertexOffset(meshIdx) ?? 0;

                // ワールド→スクリーン変換デリゲート
                Func<Vector3, Vector2> worldToScreen = (worldPos) =>
                {
                    Vector3 transformedPos = displayMatrix.MultiplyPoint3x4(worldPos);
                    return WorldToPreviewPos(transformedPos, previewRect, camPos, lookAt);
                };

                // 頂点インデックスからスクリーン座標を取得
                Func<int, Vector2> vertexIndexToScreen;
                if (useWorldTransform && displayPositions != null)
                {
                    int localOffset = vertexOffset;
                    vertexIndexToScreen = (vertexIndex) =>
                    {
                        int globalIndex = localOffset + vertexIndex;
                        if (globalIndex >= 0 && globalIndex < displayPositions.Length)
                        {
                            return WorldToPreviewPos(displayPositions[globalIndex], previewRect, camPos, lookAt);
                        }
                        return worldToScreen(targetMeshObject.Vertices[vertexIndex].Position);
                    };
                }
                else
                {
                    vertexIndexToScreen = (vertexIndex) =>
                        worldToScreen(targetMeshObject.Vertices[vertexIndex].Position);
                }

                // 頂点選択
                if (currentMode.Has(MeshSelectMode.Vertex))
                {
                    // カリングチェック用にVisibilityProviderのMeshIndexを設定
                    if (_visibilityProvider != null)
                    {
                        _visibilityProvider.MeshIndex = meshIdx;
                    }

                    for (int i = 0; i < targetMeshObject.VertexCount; i++)
                    {
                        // 背面カリングチェック
                        if (_visibilityProvider != null && !_visibilityProvider.IsVertexVisible(i))
                            continue;

                        if (selectRect.Contains(vertexIndexToScreen(i)))
                        {
                            meshContext.SelectedVertices.Add(i);
                        }
                    }
                }

                // エッジ選択
                if (currentMode.Has(MeshSelectMode.Edge))
                {
                    var topology = new TopologyCache();
                    topology.SetMeshObject(targetMeshObject);

                    foreach (var pair in topology.AllEdgePairs)
                    {
                        // 背面カリング：両端頂点の少なくとも一方が見えていなければスキップ
                        if (_visibilityProvider != null)
                        {
                            bool v1Visible = _visibilityProvider.IsVertexVisible(pair.V1);
                            bool v2Visible = _visibilityProvider.IsVertexVisible(pair.V2);
                            if (!v1Visible && !v2Visible)
                                continue;
                        }

                        if (selectRect.Contains(vertexIndexToScreen(pair.V1)) &&
                            selectRect.Contains(vertexIndexToScreen(pair.V2)))
                        {
                            meshContext.SelectedEdges.Add(pair);
                        }
                    }
                }

                // 面選択
                if (currentMode.Has(MeshSelectMode.Face))
                {
                    for (int faceIdx = 0; faceIdx < targetMeshObject.FaceCount; faceIdx++)
                    {
                        var face = targetMeshObject.Faces[faceIdx];
                        if (face.VertexCount < 3) continue;

                        // 背面カリング：少なくとも1つの頂点が見えていなければスキップ
                        if (_visibilityProvider != null)
                        {
                            bool anyVisible = false;
                            foreach (int vIdx in face.VertexIndices)
                            {
                                if (_visibilityProvider.IsVertexVisible(vIdx))
                                {
                                    anyVisible = true;
                                    break;
                                }
                            }
                            if (!anyVisible) continue;
                        }

                        bool allInRect = true;
                        foreach (int vIdx in face.VertexIndices)
                        {
                            if (!selectRect.Contains(vertexIndexToScreen(vIdx)))
                            {
                                allInRect = false;
                                break;
                            }
                        }
                        if (allInRect) meshContext.SelectedFaces.Add(faceIdx);
                    }
                }

                // 線分選択
                if (currentMode.Has(MeshSelectMode.Line))
                {
                    for (int faceIdx = 0; faceIdx < targetMeshObject.FaceCount; faceIdx++)
                    {
                        var face = targetMeshObject.Faces[faceIdx];
                        if (face.VertexCount != 2) continue;

                        // 背面カリング：両端頂点の少なくとも一方が見えていなければスキップ
                        if (_visibilityProvider != null)
                        {
                            bool v1Visible = _visibilityProvider.IsVertexVisible(face.VertexIndices[0]);
                            bool v2Visible = _visibilityProvider.IsVertexVisible(face.VertexIndices[1]);
                            if (!v1Visible && !v2Visible)
                                continue;
                        }

                        if (selectRect.Contains(vertexIndexToScreen(face.VertexIndices[0])) &&
                            selectRect.Contains(vertexIndexToScreen(face.VertexIndices[1])))
                        {
                            meshContext.SelectedLines.Add(faceIdx);
                        }
                    }
                }

                Debug.Log($"[FinishBoxSelect] Mesh {meshIdx}: V={meshContext.SelectedVertices.Count}, E={meshContext.SelectedEdges.Count}, F={meshContext.SelectedFaces.Count}, L={meshContext.SelectedLines.Count}");
            }

            // プライマリメッシュの選択を _selectionState にも同期（レガシー互換）
            int primaryMesh = _model.PrimarySelectedMeshIndex;
            if (primaryMesh >= 0)
            {
                var primaryContext = _model.GetMeshContext(primaryMesh);
                primaryContext?.LoadSelectionTo(_selectionState);
            }

            // _selectedVertices と同期（レガシー互換）
            _selectedVertices.Clear();
            if (_selectionState != null)
            {
                foreach (var v in _selectionState.Vertices)
                    _selectedVertices.Add(v);
            }

            // UnifiedSystemに選択変更を通知
            SyncMultiMeshSelectionToGPU();
        }
        else
        {
            // フォールバック：従来の単一メッシュ処理
            Matrix4x4 displayMatrix = GetDisplayMatrix(_selectedIndex);
            int vertexOffset = _unifiedAdapter?.GetVertexOffset(_selectedIndex) ?? 0;

            Func<Vector3, Vector2> worldToScreen = (worldPos) =>
            {
                Vector3 transformedPos = displayMatrix.MultiplyPoint3x4(worldPos);
                return WorldToPreviewPos(transformedPos, previewRect, camPos, lookAt);
            };

            Func<int, Vector2> vertexIndexToScreen = null;
            if (useWorldTransform && displayPositions != null)
            {
                vertexIndexToScreen = (vertexIndex) =>
                {
                    int globalIndex = vertexOffset + vertexIndex;
                    if (globalIndex >= 0 && globalIndex < displayPositions.Length)
                    {
                        return WorldToPreviewPos(displayPositions[globalIndex], previewRect, camPos, lookAt);
                    }
                    return worldToScreen(meshObject.Vertices[vertexIndex].Position);
                };
            }

            if (_selectionState != null && _selectionOps != null)
            {
                if (vertexIndexToScreen != null)
                {
                    _selectionOps.SelectInRectByIndex(selectRect, meshObject, vertexIndexToScreen, additive);
                }
                else
                {
                    _selectionOps.SelectInRect(selectRect, meshObject, worldToScreen, additive);
                }

                _selectedVertices.Clear();
                foreach (var v in _selectionState.Vertices)
                    _selectedVertices.Add(v);

                _unifiedAdapter?.UnifiedSystem?.ProcessSelectionUpdate();
            }
        }

        // 選択が変更されていたら記録
        if (!oldSelection.SetEquals(_selectedVertices) ||
            (_selectionState != null && oldSnapshot != null && oldSnapshot.IsDifferentFrom(_selectionState.CreateSnapshot())))
        {
            RecordExtendedSelectionChange(oldSnapshot, oldSelection);
        }
    }

    /// <summary>
    /// v2.1: 複数メッシュの選択状態をGPUに同期
    /// </summary>
    private void SyncMultiMeshSelectionToGPU()
    {
        if (_model == null || _unifiedAdapter?.BufferManager == null)
            return;

        var bufferManager = _unifiedAdapter.BufferManager;
        var selectedMeshIndices = _model.SelectedMeshIndices;

        // まず全頂点の選択フラグをクリア
        bufferManager.ClearAllVertexSelectedFlags();

        // 選択中メッシュの選択状態をフラグに反映
        foreach (int meshIdx in selectedMeshIndices)
        {
            var meshContext = _model.GetMeshContext(meshIdx);
            if (meshContext == null || !meshContext.HasSelection)
                continue;

            int vertexOffset = _unifiedAdapter.GetVertexOffset(meshIdx);

            // 頂点選択フラグを設定
            foreach (int localVertex in meshContext.SelectedVertices)
            {
                int globalVertex = vertexOffset + localVertex;
                bufferManager.SetVertexSelectedFlag(globalVertex, true);
            }
        }

        // GPUにアップロード
        bufferManager.UploadVertexFlags();
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