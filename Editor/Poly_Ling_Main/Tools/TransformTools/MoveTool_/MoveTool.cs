// Tools/MoveTool.cs
// 頂点移動ツール（マグネット、軸ドラッグ含む）
// 改善版: 正確な軸方向表示、中央ドラッグ対応
// Edge/Face/Line選択時も移動可能
// IToolSettings対応版
// ローカライズ対応版
// Phase 6: ホバー/クリック整合性対応（ToolContext.LastHoverHitResult使用）
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Transforms;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Localization;
using Poly_Ling.Rendering;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 頂点移動ツール
    /// 
    /// 【ホバー/クリック整合性（Phase 6）】
    /// このツールはToolContext.LastHoverHitResultを優先的に使用して
    /// ホバーとクリックの整合性を保つ。従来のCPU版ヒットテストは
    /// フォールバックとしてのみ使用する。
    /// </summary>
    public partial class MoveTool : IEditTool
    {
        public string Name => "Move";
        public string DisplayName => "Move";

        /// <summary>
        /// ローカライズされた表示名を取得
        /// </summary>
        public string GetLocalizedDisplayName() => L.Get("Tool_Move");


        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private MoveSettings _settings = new MoveSettings();

        /// <summary>
        /// ツール設定（Undo対応）
        /// </summary>
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ（後方互換 + 内部使用）
        public bool UseMagnet
        {
            get => _settings.UseMagnet;
            set => _settings.UseMagnet = value;
        }

        public float MagnetRadius
        {
            get => _settings.MagnetRadius;
            set => _settings.MagnetRadius = value;
        }

        public FalloffType MagnetFalloff
        {
            get => _settings.MagnetFalloff;
            set => _settings.MagnetFalloff = value;
        }

        // ================================================================
        // 状態
        // ================================================================

        private enum MoveState
        {
            Idle,
            PendingAction,
            MovingVertices,
            AxisDragging,
            CenterDragging  // 中央ドラッグ（自由移動）
        }
        private MoveState _state = MoveState.Idle;

        // 軸
        private enum AxisType { None, X, Y, Z, Center }
        private AxisType _hitAxisOnMouseDown = AxisType.None;
        private AxisType _draggingAxis = AxisType.None;
        private AxisType _hoveredAxis = AxisType.None;  // ホバー中の軸
        private Vector3 _gizmoCenter = Vector3.zero;
        private Vector3 _selectionCenter = Vector3.zero;  // 選択頂点の重心
        private Vector2 _lastAxisDragScreenPos;

        // ドラッグ
        private Vector2 _mouseDownScreenPos;
        private int _hitVertexOnMouseDown = -1;
        private Vector3[] _dragStartPositions;
        private IVertexTransform _currentTransform;
        private const float DragThreshold = 4f;

        // 移動対象の頂点（Edge/Face/Line選択時も含む）
        private HashSet<int> _affectedVertices = new HashSet<int>();

        // v2.1: 複数メッシュ対応
        private Dictionary<int, HashSet<int>> _multiMeshAffectedVertices = new Dictionary<int, HashSet<int>>();
        private Dictionary<int, Vector3[]> _multiMeshDragStartPositions = new Dictionary<int, Vector3[]>();
        private Vector3 _multiMeshAccumulatedDelta = Vector3.zero;

        // ドラッグ開始時のヒット情報
        /// <summary>
        /// 保留中のヒットタイプ
        /// 
        /// 【Phase 6簡素化】
        /// 選択処理はSimpleMeshFactoryが担当するため、
        /// MoveToolは「選択済み」かどうかだけを判断すればよい。
        /// </summary>
        private enum PendingHitType
        {
            None,
            Selection,      // ★ Phase 6: 汎用的な「選択済み要素がある」
            // 以下は削除候補（互換性のため残す）
            Vertex,
            Edge,
            Face,
            Line,
            SelectedEdge,
            SelectedFace,
            SelectedLine
        }
        private PendingHitType _pendingHitType = PendingHitType.None;
        private VertexPair _pendingEdgePair;
        private int _pendingFaceIndex = -1;
        private int _pendingLineIndex = -1;

        // ギズモ設定
        private Vector2 _gizmoScreenOffset = new Vector2(60, -60);  // 重心からのスクリーンオフセット（右上）
        private float _handleHitRadius = 10f;  // 軸先端のヒット半径（ピクセル）

        // ================================================================
        // Phase 7: 描画時のスクリーン座標キャッシュ
        // DrawAxisGizmo（Repaintイベント）で計算した座標を保存し、
        // FindAxisHandleAtScreenPos（MouseDownイベント）で使用する。
        // これによりカメラ位置の不整合を解消。
        // ================================================================
        private Vector2 _cachedOriginScreen;
        private Vector2 _cachedXEnd;
        private Vector2 _cachedYEnd;
        private Vector2 _cachedZEnd;
        private bool _gizmoCacheValid = false;
        private float _handleSize = 8f;  // 軸先端のハンドルサイズ（ピクセル）
        private float _centerSize = 14f;  // 中央四角のサイズ（ピクセル）
        private float _screenAxisLength = 50f;  // 軸の長さ（ピクセル）

        // 最後のマウス位置（ホバー検出用）
        private Vector2 _lastMousePos;
        private ToolContext _lastContext;

        // 修飾キー状態（ドラッグ開始時に保存）
        private bool _shiftHeld = false;
        private bool _ctrlHeld = false;

        // === IEditTool実装 ===

        /// <summary>
        /// MouseDown処理
        /// 
        /// 【Phase 6: 責任分離】
        /// 選択処理はSimpleMeshFactoryが担当（MouseDown時に即座に反映）。
        /// MoveToolは以下のみを担当:
        /// 1. 軸ギズモのヒットテスト → 軸ドラッグ開始
        /// 2. 既に選択されているものがあれば → 移動準備
        /// 
        /// return true: イベントを消費（軸ドラッグ開始時）
        /// return false: SimpleMeshFactoryに処理を委譲
        /// </summary>
        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (_state != MoveState.Idle)
            {
                // 前回の操作が完了していない場合（MouseUpが届かなかった場合など）
                // 強制的にリセットして新しい操作を受け付ける
                //Reset();
            }

            _mouseDownScreenPos = mousePos;
            _lastMousePos = mousePos;
            _lastContext = ctx;

            // 修飾キー状態を保存
            _shiftHeld = Event.current != null && Event.current.shift;
            _ctrlHeld = Event.current != null && Event.current.control;

            // ================================================================
            // 選択はSimpleMeshFactoryが既に処理済み
            // ここでは選択済み頂点を取得するだけ
            // ================================================================
            UpdateAffectedVertices(ctx);

            // v2.1: 全メッシュの選択頂点数を計算
            int totalAffectedCount = GetTotalAffectedCount(ctx);

            // ================================================================
            // 1. 軸ギズモのヒットテスト（最優先）
            // ================================================================
            if (totalAffectedCount > 0)
            {
                UpdateGizmoCenter(ctx);
                _hitAxisOnMouseDown = FindAxisHandleAtScreenPos(mousePos, ctx);
                if (_hitAxisOnMouseDown != AxisType.None)
                {
                    if (_hitAxisOnMouseDown == AxisType.Center)
                    {
                        StartCenterDrag(ctx);
                    }
                    else
                    {
                        StartAxisDrag(ctx, _hitAxisOnMouseDown);
                    }
                    return true;  // 軸ドラッグ開始、イベント消費
                }
            }

            // ================================================================
            // 2. 選択済み要素があれば移動準備
            // ================================================================
            if (totalAffectedCount > 0)
            {
                _state = MoveState.PendingAction;
                _pendingHitType = PendingHitType.Selection;  // 汎用的な「選択済み」
                return false;  // イベントは消費しない（SimpleMeshFactoryの処理を妨げない）
            }

            return false;  // 何もない
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            _lastMousePos = mousePos;
            _lastContext = ctx;

            switch (_state)
            {
                case MoveState.PendingAction:
                    float dragDistance = Vector2.Distance(mousePos, _mouseDownScreenPos);
                    if (dragDistance > DragThreshold)
                    {
                        // ★ Phase 6: 選択済み要素を移動開始
                        StartMoveFromSelection(ctx);
                    }
                    ctx.Repaint?.Invoke();
                    return true;

                case MoveState.MovingVertices:
                    MoveSelectedVertices(delta, ctx);
                    ctx.Repaint?.Invoke();
                    return true;

                case MoveState.AxisDragging:
                    MoveVerticesAlongAxis(mousePos, ctx);
                    _lastAxisDragScreenPos = mousePos;
                    ctx.Repaint?.Invoke();
                    return true;

                case MoveState.CenterDragging:
                    MoveSelectedVertices(delta, ctx);
                    ctx.Repaint?.Invoke();
                    return true;
            }

            // ホバー更新
            UpdateAffectedVertices(ctx);
            if (_state == MoveState.Idle && GetTotalAffectedCount(ctx) > 0)
            {
                UpdateGizmoCenter(ctx);
                var newHovered = FindAxisHandleAtScreenPos(mousePos, ctx);
                if (newHovered != _hoveredAxis)
                {
                    _hoveredAxis = newHovered;
                    ctx.Repaint?.Invoke();
                }
            }

            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            bool handled = false;

            switch (_state)
            {
                case MoveState.MovingVertices:
                    EndVertexMove(ctx);
                    handled = true;
                    break;

                case MoveState.AxisDragging:
                    EndAxisDrag(ctx);
                    handled = true;
                    break;

                case MoveState.CenterDragging:
                    EndCenterDrag(ctx);
                    handled = true;
                    break;

                case MoveState.PendingAction:
                    // クリック（ドラッグなし）はSimpleMeshFactory側で選択処理
                    handled = false;
                    break;
            }

            Reset();
            ctx.Repaint?.Invoke();
            return handled;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            _lastContext = ctx;
            UpdateAffectedVertices(ctx);

            // v2.1: プライマリ＋セカンダリの全選択頂点数
            int totalAffected = GetTotalAffectedCount(ctx);
            if (totalAffected == 0)
                return;

            UpdateGizmoCenter(ctx);

            // ホバー更新（MouseMoveイベントがない場合用）
            if (_state == MoveState.Idle)
            {
                _hoveredAxis = FindAxisHandleAtScreenPos(_lastMousePos, ctx);
            }

            DrawAxisGizmo(ctx);
        }

        /// <summary>
        /// v2.1: 全メッシュの選択頂点数を計算
        /// </summary>
        private int GetTotalAffectedCount(ToolContext ctx)
        {
            int total = 0;
            foreach (var kv in _multiMeshAffectedVertices)
            {
                total += kv.Value.Count;
            }
            return total;
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Magnet"), EditorStyles.miniBoldLabel);

            // MoveSettingsを直接編集（Undo検出はGUI_Tools側で行う）
            _settings.UseMagnet = EditorGUILayout.Toggle(T("Enable"), _settings.UseMagnet);

            using (new EditorGUI.DisabledScope(!_settings.UseMagnet))
            {
                _settings.MagnetRadius = EditorGUILayout.Slider(T("Radius"), _settings.MagnetRadius, _settings.MIN_MAGNET_RADIUS, _settings.MAX_MAGNET_RADIUS);//スライダーの上限下限
                _settings.MagnetFalloff = (FalloffType)EditorGUILayout.EnumPopup(T("Falloff"), _settings.MagnetFalloff);
            }

            // ギズモ設定（Undo対象外）
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("Gizmo"), EditorStyles.miniBoldLabel);
            _gizmoScreenOffset.x = EditorGUILayout.Slider(T("OffsetX"), _gizmoScreenOffset.x, _settings.MIN_SCREEN_OFFSET_X, _settings.MAX_SCREEN_OFFSET_X);//スライダーの上限下限
            _gizmoScreenOffset.y = EditorGUILayout.Slider(T("OffsetY"), _gizmoScreenOffset.y, _settings.MIN_SCREEN_OFFSET_Y, _settings.MAX_SCREEN_OFFSET_Y);//スライダーの上限下限

            // 選択情報表示
            EditorGUILayout.Space(5);
            int totalAffected = GetTotalAffectedCount(_lastContext);
            if (totalAffected > 0)
            {
                EditorGUILayout.HelpBox(T("TargetVertices", totalAffected), MessageType.None);
            }
        }

        public void OnActivate(ToolContext ctx)
        {
            _lastContext = ctx;
            UpdateAffectedVertices(ctx);
        }

        public void OnDeactivate(ToolContext ctx) { Reset(); }

        public void Reset()
        {
            _state = MoveState.Idle;
            _hitVertexOnMouseDown = -1;
            _hitAxisOnMouseDown = AxisType.None;
            _draggingAxis = AxisType.None;
            _hoveredAxis = AxisType.None;
            _currentTransform = null;
            _dragStartPositions = null;
            _affectedVertices.Clear();

            // Pending情報クリア
            _pendingHitType = PendingHitType.None;
            _pendingEdgePair = default;
            _pendingFaceIndex = -1;
            _pendingLineIndex = -1;

            // 修飾キー状態クリア
            _shiftHeld = false;
            _ctrlHeld = false;

            // Phase 7: ギズモキャッシュ無効化
            _gizmoCacheValid = false;
        }

        // === 影響を受ける頂点の更新 ===

        private void UpdateAffectedVertices(ToolContext ctx)
        {
            _affectedVertices.Clear();
            _multiMeshAffectedVertices.Clear();

            var model = ctx.Model;
            if (model == null) return;

            // 全選択メッシュを統一的にイテレート
            foreach (int meshIdx in model.SelectedMeshIndices)
            {
                var meshContext = model.GetMeshContext(meshIdx);
                if (meshContext == null || !meshContext.HasSelection)
                    continue;

                var meshObject = meshContext.MeshObject;
                if (meshObject == null)
                    continue;

                var affected = new HashSet<int>();
                foreach (var v in meshContext.SelectedVertices)
                    affected.Add(v);
                foreach (var edge in meshContext.SelectedEdges)
                {
                    affected.Add(edge.V1);
                    affected.Add(edge.V2);
                }
                foreach (var faceIdx in meshContext.SelectedFaces)
                {
                    if (faceIdx >= 0 && faceIdx < meshObject.FaceCount)
                    {
                        foreach (var vIdx in meshObject.Faces[faceIdx].VertexIndices)
                            affected.Add(vIdx);
                    }
                }
                foreach (var lineIdx in meshContext.SelectedLines)
                {
                    if (lineIdx >= 0 && lineIdx < meshObject.FaceCount)
                    {
                        var face = meshObject.Faces[lineIdx];
                        if (face.VertexCount == 2)
                        {
                            affected.Add(face.VertexIndices[0]);
                            affected.Add(face.VertexIndices[1]);
                        }
                    }
                }

                if (affected.Count > 0)
                {
                    _multiMeshAffectedVertices[meshIdx] = affected;
                }
            }

            // プライマリメッシュの影響頂点を _affectedVertices にも反映（後方互換）
            int primaryMesh = model.PrimarySelectedMeshIndex;
            if (primaryMesh >= 0 && _multiMeshAffectedVertices.ContainsKey(primaryMesh))
            {
                _affectedVertices = new HashSet<int>(_multiMeshAffectedVertices[primaryMesh]);
            }
        }

        // === 頂点移動処理 ===

        private void StartVertexMove(ToolContext ctx)
        {
            var oldSelection = new HashSet<int>(ctx.SelectedVertices);
            bool selectionChanged = false;

            // Ctrl: 除外選択して移動キャンセル
            if (_ctrlHeld)
            {
                if (_hitVertexOnMouseDown >= 0 && ctx.SelectedVertices.Contains(_hitVertexOnMouseDown))
                {
                    ctx.SelectedVertices.Remove(_hitVertexOnMouseDown);
                    if (ctx.SelectionState != null)
                    {
                        ctx.SelectionState.DeselectVertex(_hitVertexOnMouseDown);
                    }
                    selectionChanged = true;
                }

                if (selectionChanged)
                {
                    ctx.RecordSelectionChange?.Invoke(oldSelection, ctx.SelectedVertices);
                }

                _state = MoveState.Idle;
                return;  // 移動しない
            }

            // 未選択頂点からドラッグ開始した場合
            if (_hitVertexOnMouseDown >= 0 && !_affectedVertices.Contains(_hitVertexOnMouseDown))
            {
                if (_shiftHeld)
                {
                    // Shift: 追加選択（既存維持）
                    ctx.SelectedVertices.Add(_hitVertexOnMouseDown);
                    if (ctx.SelectionState != null)
                    {
                        ctx.SelectionState.SelectVertex(_hitVertexOnMouseDown, true);  // addToSelection = true
                    }
                }
                else
                {
                    // 修飾なし: 新規選択（既存クリア）
                    ctx.SelectedVertices.Clear();
                    ctx.SelectedVertices.Add(_hitVertexOnMouseDown);

                    if (ctx.SelectionState != null)
                    {
                        ctx.SelectionState.ClearAll();
                        ctx.SelectionState.SelectVertex(_hitVertexOnMouseDown, false);
                    }
                }

                selectionChanged = true;
                UpdateAffectedVertices(ctx);
            }

            if (selectionChanged)
            {
                ctx.RecordSelectionChange?.Invoke(oldSelection, ctx.SelectedVertices);
            }

            BeginMove(ctx);
        }

        private void SelectAndStartMove_Edge(ToolContext ctx)
        {
            if (ctx.SelectionState == null) return;

            // Ctrl: 除外選択して移動キャンセル
            if (_ctrlHeld)
            {
                if (ctx.SelectionState.Edges.Contains(_pendingEdgePair))
                {
                    ctx.SelectionState.DeselectEdge(_pendingEdgePair);
                }
                _state = MoveState.Idle;
                return;  // 移動しない
            }

            if (_shiftHeld)
            {
                // Shift: 追加選択（既存維持）
                ctx.SelectionState.SelectEdge(_pendingEdgePair, true);
            }
            else
            {
                // 修飾なし: 新規選択（既存クリア）
                ctx.SelectionState.ClearAll();
                ctx.SelectionState.SelectEdge(_pendingEdgePair, false);
                ctx.SelectedVertices.Clear();
            }

            UpdateAffectedVertices(ctx);
            BeginMove(ctx);
        }

        private void SelectAndStartMove_Face(ToolContext ctx)
        {
            if (ctx.SelectionState == null || _pendingFaceIndex < 0) return;

            // Ctrl: 除外選択して移動キャンセル
            if (_ctrlHeld)
            {
                if (ctx.SelectionState.Faces.Contains(_pendingFaceIndex))
                {
                    ctx.SelectionState.DeselectFace(_pendingFaceIndex);
                }
                _state = MoveState.Idle;
                return;  // 移動しない
            }

            if (_shiftHeld)
            {
                // Shift: 追加選択（既存維持）
                ctx.SelectionState.SelectFace(_pendingFaceIndex, true);
            }
            else
            {
                // 修飾なし: 新規選択（既存クリア）
                ctx.SelectionState.ClearAll();
                ctx.SelectionState.SelectFace(_pendingFaceIndex, false);
                ctx.SelectedVertices.Clear();
            }

            UpdateAffectedVertices(ctx);
            BeginMove(ctx);
        }

        private void SelectAndStartMove_Line(ToolContext ctx)
        {
            if (ctx.SelectionState == null || _pendingLineIndex < 0) return;

            // Ctrl: 除外選択して移動キャンセル
            if (_ctrlHeld)
            {
                if (ctx.SelectionState.Lines.Contains(_pendingLineIndex))
                {
                    ctx.SelectionState.DeselectLine(_pendingLineIndex);
                }
                _state = MoveState.Idle;
                return;  // 移動しない
            }

            if (_shiftHeld)
            {
                // Shift: 追加選択（既存維持）
                ctx.SelectionState.SelectLine(_pendingLineIndex, true);
            }
            else
            {
                // 修飾なし: 新規選択（既存クリア）
                ctx.SelectionState.ClearAll();
                ctx.SelectionState.SelectLine(_pendingLineIndex, false);
                ctx.SelectedVertices.Clear();
            }

            UpdateAffectedVertices(ctx);
            BeginMove(ctx);
        }

        private void StartMoveFromSelection(ToolContext ctx)
        {
            UpdateAffectedVertices(ctx);
            BeginMove(ctx);
        }

        private void BeginMove(ToolContext ctx)
        {
            // v2.1: プライマリ＋セカンダリの全選択頂点数
            if (GetTotalAffectedCount(ctx) == 0)
            {
                _state = MoveState.Idle;
                return;
            }

            // v2.1: 複数メッシュ対応 - 開始位置保存
            _multiMeshAccumulatedDelta = Vector3.zero;
            _multiMeshDragStartPositions.Clear();
            if (_multiMeshAffectedVertices.Count > 0 && ctx.Model != null)
            {
                foreach (var kv in _multiMeshAffectedVertices)
                {
                    var meshContext = ctx.Model.GetMeshContext(kv.Key);
                    if (meshContext?.MeshObject != null)
                    {
                        _multiMeshDragStartPositions[kv.Key] = 
                            (Vector3[])meshContext.MeshObject.Positions.Clone();
                    }
                }
            }

            _dragStartPositions = (Vector3[])ctx.MeshObject.Positions.Clone();

            if (UseMagnet)
            {
                _currentTransform = new MagnetMoveTransform(MagnetRadius, MagnetFalloff);
            }
            else
            {
                _currentTransform = new SimpleMoveTransform();
            }

            _currentTransform.Begin(ctx.MeshObject, _affectedVertices, _dragStartPositions);
            _state = MoveState.MovingVertices;
            ctx.EnterTransformDragging?.Invoke();
        }

        private void MoveSelectedVertices(Vector2 screenDelta, ToolContext ctx)
        {
            // v2.1: プライマリ＋セカンダリの全選択頂点数
            if (GetTotalAffectedCount(ctx) == 0 || _currentTransform == null)
                return;

            Vector3 worldDelta = ctx.ScreenDeltaToWorldDelta(
                screenDelta, ctx.CameraPosition, ctx.CameraTarget,
                ctx.CameraDistance, ctx.PreviewRect);

            // DisplayMatrixが非identityの場合、移動ベクトルを変換
            // （表示座標系からメッシュ座標系へ）
            if (ctx.DisplayMatrix != Matrix4x4.identity)
            {
                Matrix4x4 inverseMatrix = ctx.DisplayMatrix.inverse;
                worldDelta = inverseMatrix.MultiplyVector(worldDelta);
            }

            // v2.1: 複数メッシュ対応
            _multiMeshAccumulatedDelta += worldDelta;
            if (_multiMeshAffectedVertices.Count > 0 && ctx.Model != null)
            {
                foreach (var kv in _multiMeshAffectedVertices)
                {
                    int meshIdx = kv.Key;
                    var affected = kv.Value;
                    if (!_multiMeshDragStartPositions.ContainsKey(meshIdx))
                        continue;
                    
                    var meshContext = ctx.Model.GetMeshContext(meshIdx);
                    if (meshContext?.MeshObject == null)
                        continue;
                    
                    var meshObject = meshContext.MeshObject;
                    var startPositions = _multiMeshDragStartPositions[meshIdx];
                    
                    foreach (int vIdx in affected)
                    {
                        if (vIdx >= 0 && vIdx < meshObject.VertexCount && vIdx < startPositions.Length)
                        {
                            meshObject.Vertices[vIdx].Position = startPositions[vIdx] + _multiMeshAccumulatedDelta;
                        }
                    }
                }
                ctx.SyncMeshPositionsOnly?.Invoke();
                return;
            }

            // 従来の単一メッシュ処理
            _currentTransform.Apply(worldDelta);

            var affectedIndices = _currentTransform.GetAffectedIndices();
            
            // OriginalPositions が null でない場合のみオフセット計算
            if (ctx.VertexOffsets != null && ctx.OriginalPositions != null)
            {
                foreach (int idx in affectedIndices)
                {
                    if (idx >= 0 && idx < ctx.VertexOffsets.Length && idx < ctx.OriginalPositions.Length)
                    {
                        ctx.VertexOffsets[idx] = ctx.MeshObject.Vertices[idx].Position - ctx.OriginalPositions[idx];
                        ctx.GroupOffsets[idx] = ctx.VertexOffsets[idx];
                    }
                }
            }

            // ドラッグ中は軽量版を使用（位置のみ更新）
            ctx.SyncMeshPositionsOnly?.Invoke();

            if (ctx.UndoController != null)
            {
                ctx.UndoController.MeshUndoContext.MeshObject = ctx.MeshObject;
            }
        }

        private void EndVertexMove(ToolContext ctx)
        {
            if (_currentTransform == null || ctx.MeshObject == null)
            {
                _currentTransform = null;
                _dragStartPositions = null;
                ctx.ExitTransformDragging?.Invoke();
                return;
            }

            _currentTransform.End();

            var affectedIndices = _currentTransform.GetAffectedIndices();
            var originalPositions = _currentTransform.GetOriginalPositions();
            var currentPositions = _currentTransform.GetCurrentPositions();

            var movedIndices = new List<int>();
            var oldPositions = new List<Vector3>();
            var newPositions = new List<Vector3>();

            for (int i = 0; i < affectedIndices.Length; i++)
            {
                if (Vector3.Distance(currentPositions[i], originalPositions[i]) > 0.0001f)
                {
                    movedIndices.Add(affectedIndices[i]);
                    oldPositions.Add(originalPositions[i]);
                    newPositions.Add(currentPositions[i]);
                }
            }

            if (movedIndices.Count > 0 && ctx.UndoController != null)
            {
                string actionName = UseMagnet
                    ? $"Magnet Move {movedIndices.Count} Vertices"
                    : $"Move {movedIndices.Count} Vertices";

                var record = new VertexMoveRecord(
                    movedIndices.ToArray(),
                    oldPositions.ToArray(),
                    newPositions.ToArray());
                ctx.UndoController.FocusVertexEdit();
                ctx.UndoController.VertexEditStack.Record(record, actionName);
            }

            _currentTransform = null;
            _dragStartPositions = null;
            ctx.ExitTransformDragging?.Invoke();
        }

        // === 軸ドラッグ処理 ===

        private void StartAxisDrag(ToolContext ctx, AxisType axis)
        {
            _draggingAxis = axis;
            _lastAxisDragScreenPos = _mouseDownScreenPos;

            // v2.1: 複数メッシュ対応 - 開始位置保存
            _multiMeshAccumulatedDelta = Vector3.zero;
            _multiMeshDragStartPositions.Clear();
            if (_multiMeshAffectedVertices.Count > 0 && ctx.Model != null)
            {
                foreach (var kv in _multiMeshAffectedVertices)
                {
                    var meshContext = ctx.Model.GetMeshContext(kv.Key);
                    if (meshContext?.MeshObject != null)
                    {
                        _multiMeshDragStartPositions[kv.Key] = 
                            (Vector3[])meshContext.MeshObject.Positions.Clone();
                    }
                }
            }

            _dragStartPositions = (Vector3[])ctx.MeshObject.Positions.Clone();

            if (UseMagnet)
            {
                _currentTransform = new MagnetMoveTransform(MagnetRadius, MagnetFalloff);
            }
            else
            {
                _currentTransform = new SimpleMoveTransform();
            }

            _currentTransform.Begin(ctx.MeshObject, _affectedVertices, _dragStartPositions);
            _state = MoveState.AxisDragging;
            ctx.EnterTransformDragging?.Invoke();
        }

        private void MoveVerticesAlongAxis(Vector2 currentScreenPos, ToolContext ctx)
        {
            if (ctx.MeshObject == null)
                return;

            // スクリーン上での移動量を計算（フレーム差分）
            Vector2 screenDelta = currentScreenPos - _lastAxisDragScreenPos;
            _lastAxisDragScreenPos = currentScreenPos;  // 次フレーム用に更新
            
            if (screenDelta.sqrMagnitude < 0.001f)
                return;

            // ワールド座標での軸方向
            Vector3 axisDir = GetAxisDirection(_draggingAxis);

            // 軸方向のスクリーン投影
            Vector3 axisScreenDir = GetAxisScreenDirection(ctx, axisDir);
            Vector2 axisScreenDir2D = new Vector2(axisScreenDir.x, axisScreenDir.y);

            if (axisScreenDir2D.sqrMagnitude < 0.001f)
                return;

            axisScreenDir2D.Normalize();

            // スクリーン移動量を軸方向成分に分解
            float axisScreenMovement = Vector2.Dot(screenDelta, axisScreenDir2D);

            // スクリーン移動量をワールド移動量に変換（距離依存のスケール）
            float worldScale = ctx.CameraDistance * 0.001f;
            Vector3 worldDeltaFrame = axisDir * axisScreenMovement * worldScale;

            // DisplayMatrixが非identityの場合、移動ベクトルを変換
            // （表示座標系からメッシュ座標系へ）
            if (ctx.DisplayMatrix != Matrix4x4.identity)
            {
                Matrix4x4 inverseMatrix = ctx.DisplayMatrix.inverse;
                worldDeltaFrame = inverseMatrix.MultiplyVector(worldDeltaFrame);
            }

            // v2.1: 複数メッシュ対応
            _multiMeshAccumulatedDelta += worldDeltaFrame;
            if (_multiMeshAffectedVertices.Count > 0 && ctx.Model != null)
            {
                foreach (var kv in _multiMeshAffectedVertices)
                {
                    int meshIdx = kv.Key;
                    var affected = kv.Value;
                    if (!_multiMeshDragStartPositions.ContainsKey(meshIdx))
                        continue;
                    
                    var meshContext = ctx.Model.GetMeshContext(meshIdx);
                    if (meshContext?.MeshObject == null)
                        continue;
                    
                    var meshObject = meshContext.MeshObject;
                    var startPositions = _multiMeshDragStartPositions[meshIdx];
                    
                    foreach (int vIdx in affected)
                    {
                        if (vIdx >= 0 && vIdx < meshObject.VertexCount && vIdx < startPositions.Length)
                        {
                            meshObject.Vertices[vIdx].Position = startPositions[vIdx] + _multiMeshAccumulatedDelta;
                        }
                    }
                }
                ctx.SyncMeshPositionsOnly?.Invoke();
                return;
            }

            // 従来の単一メッシュ処理
            if (_currentTransform == null)
                return;

            _currentTransform.Apply(worldDeltaFrame);

            var affectedIndices = _currentTransform.GetAffectedIndices();
            foreach (int idx in affectedIndices)
            {
                if (ctx.VertexOffsets != null && idx >= 0 && idx < ctx.VertexOffsets.Length)
                {
                    ctx.VertexOffsets[idx] = ctx.MeshObject.Vertices[idx].Position - ctx.OriginalPositions[idx];
                    ctx.GroupOffsets[idx] = ctx.VertexOffsets[idx];
                }
            }

            ctx.SyncMeshPositionsOnly?.Invoke();
        }

        private void EndAxisDrag(ToolContext ctx)
        {
            EndVertexMove(ctx);  // 共通処理
            _draggingAxis = AxisType.None;
        }

        // === 中央ドラッグ処理 ===

        private void StartCenterDrag(ToolContext ctx)
        {
            // v2.1: 複数メッシュ対応 - 開始位置保存
            _multiMeshAccumulatedDelta = Vector3.zero;
            _multiMeshDragStartPositions.Clear();
            if (_multiMeshAffectedVertices.Count > 0 && ctx.Model != null)
            {
                foreach (var kv in _multiMeshAffectedVertices)
                {
                    var meshContext = ctx.Model.GetMeshContext(kv.Key);
                    if (meshContext?.MeshObject != null)
                    {
                        _multiMeshDragStartPositions[kv.Key] = 
                            (Vector3[])meshContext.MeshObject.Positions.Clone();
                    }
                }
            }

            _dragStartPositions = (Vector3[])ctx.MeshObject.Positions.Clone();

            if (UseMagnet)
            {
                _currentTransform = new MagnetMoveTransform(MagnetRadius, MagnetFalloff);
            }
            else
            {
                _currentTransform = new SimpleMoveTransform();
            }

            _currentTransform.Begin(ctx.MeshObject, _affectedVertices, _dragStartPositions);
            _state = MoveState.CenterDragging;
            ctx.EnterTransformDragging?.Invoke();
        }

        private void EndCenterDrag(ToolContext ctx)
        {
            EndVertexMove(ctx);  // 共通処理
        }

        // === ギズモ計算・描画 ===

        private void UpdateGizmoCenter(ToolContext ctx)
        {
            // v2.1: 複数メッシュ対応
            int totalVertices = 0;
            Vector3 sum = Vector3.zero;
            
            if (_multiMeshAffectedVertices.Count > 0 && ctx.Model != null)
            {
                foreach (var kv in _multiMeshAffectedVertices)
                {
                    var meshContext = ctx.Model.GetMeshContext(kv.Key);
                    if (meshContext?.MeshObject == null)
                        continue;
                    
                    var meshObject = meshContext.MeshObject;
                    foreach (int vi in kv.Value)
                    {
                        if (vi >= 0 && vi < meshObject.VertexCount)
                        {
                            sum += meshObject.Vertices[vi].Position;
                            totalVertices++;
                        }
                    }
                }
            }
            else if (_affectedVertices.Count > 0 && ctx.MeshObject != null)
            {
                // 単一メッシュ
                foreach (int vi in _affectedVertices)
                {
                    if (vi >= 0 && vi < ctx.MeshObject.VertexCount)
                    {
                        sum += ctx.MeshObject.Vertices[vi].Position;
                        totalVertices++;
                    }
                }
            }
            
            if (totalVertices == 0)
            {
                _selectionCenter = Vector3.zero;
                _gizmoCenter = Vector3.zero;
                return;
            }

            _selectionCenter = sum / totalVertices;
            _gizmoCenter = _selectionCenter;
        }

        private Vector2 GetGizmoOriginScreen(ToolContext ctx)
        {
            // ギズモ中心のスクリーン座標 + オフセット
            // _gizmoCenterはメッシュ座標系、WorldToScreenPos内でDisplayMatrixが適用される
            Vector2 centerScreen = ctx.WorldToScreenPos(
                _gizmoCenter, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            return centerScreen + _gizmoScreenOffset;
        }

        private Vector3 GetAxisScreenDirection(ToolContext ctx, Vector3 worldAxis)
        {
            // DisplayMatrixで軸方向を変換（回転のみ）
            Vector3 transformedAxis = ctx.DisplayMatrix.MultiplyVector(worldAxis).normalized;

            // カメラ距離に応じてスケール調整（近くても遠くても安定した方向計算）
            float scale = Mathf.Max(0.1f, ctx.CameraDistance * 0.1f);

            // ギズモ中心からワールド軸方向に進んだ点のスクリーン座標を計算
            // _gizmoCenterはメッシュ座標系なので、軸方向もメッシュ座標系で加算
            Vector3 axisEnd = _gizmoCenter + worldAxis * scale;
            Vector2 centerScreen = ctx.WorldToScreenPos(
                _gizmoCenter, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 axisEndScreen = ctx.WorldToScreenPos(
                axisEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            Vector2 diff = axisEndScreen - centerScreen;
            if (diff.magnitude < 0.001f)
            {
                // 軸がカメラ方向と平行（スクリーン上でほぼ点になる）
                return Vector3.zero;
            }

            Vector2 screenDir = diff.normalized;
            return new Vector3(screenDir.x, screenDir.y, 0);
        }

        private Vector2 GetAxisScreenEnd(ToolContext ctx, Vector3 worldAxis, Vector2 originScreen)
        {
            // スクリーン空間での軸方向を計算
            Vector3 screenDir = GetAxisScreenDirection(ctx, worldAxis);
            return originScreen + new Vector2(screenDir.x, screenDir.y) * _screenAxisLength;
        }

        private void DrawAxisGizmo(ToolContext ctx)
        {
            Vector2 originScreen = GetGizmoOriginScreen(ctx);

            // 軸の色
            Color xColor = (_draggingAxis == AxisType.X || _hoveredAxis == AxisType.X)
                ? new Color(1f, 0.3f, 0.3f, 1f)
                : new Color(0.8f, 0.2f, 0.2f, 0.7f);

            Color yColor = (_draggingAxis == AxisType.Y || _hoveredAxis == AxisType.Y)
                ? new Color(0.3f, 1f, 0.3f, 1f)
                : new Color(0.2f, 0.8f, 0.2f, 0.7f);

            Color zColor = (_draggingAxis == AxisType.Z || _hoveredAxis == AxisType.Z)
                ? new Color(0.3f, 0.3f, 1f, 1f)
                : new Color(0.2f, 0.2f, 0.8f, 0.7f);

            // 軸先端の位置
            Vector2 xEnd = GetAxisScreenEnd(ctx, Vector3.right, originScreen);
            Vector2 yEnd = GetAxisScreenEnd(ctx, Vector3.up, originScreen);
            Vector2 zEnd = GetAxisScreenEnd(ctx, Vector3.forward, originScreen);

            // ================================================================
            // Phase 7: スクリーン座標をキャッシュ
            // ================================================================
            _cachedOriginScreen = originScreen;
            _cachedXEnd = xEnd;
            _cachedYEnd = yEnd;
            _cachedZEnd = zEnd;
            _gizmoCacheValid = true;

            // 軸線を描画
            float lineWidth = 2f;
            DrawAxisLine(originScreen, xEnd, xColor, lineWidth);
            DrawAxisLine(originScreen, yEnd, yColor, lineWidth);
            DrawAxisLine(originScreen, zEnd, zColor, lineWidth);

            // 軸先端のハンドルを描画
            DrawAxisHandle(xEnd, xColor, _hoveredAxis == AxisType.X, "X");
            DrawAxisHandle(yEnd, yColor, _hoveredAxis == AxisType.Y, "Y");
            DrawAxisHandle(zEnd, zColor, _hoveredAxis == AxisType.Z, "Z");

            // 中央の四角を描画
            bool centerHovered = (_hoveredAxis == AxisType.Center);
            Color centerColor = centerHovered
                ? new Color(1f, 1f, 1f, 0.9f)
                : new Color(0.8f, 0.8f, 0.8f, 0.6f);

            float halfCenter = _centerSize / 2;
            Rect centerRect = new Rect(
                originScreen.x - halfCenter,
                originScreen.y - halfCenter,
                _centerSize,
                _centerSize);

            // 中央の枠線

            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.DrawRect(centerRect, centerColor);
            UnityEditor_Handles.color = centerHovered ? Color.white : new Color(0.5f, 0.5f, 0.5f);
            UnityEditor_Handles.DrawSolidRectangleWithOutline(centerRect, Color.clear, UnityEditor_Handles.color);
            UnityEditor_Handles.EndGUI();
        }

        private void DrawAxisLine(Vector2 from, Vector2 to, Color color, float lineWidth)
        {
            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = color;
            UnityEditor_Handles.DrawAAPolyLine(lineWidth,
                new Vector3(from.x, from.y, 0),
                new Vector3(to.x, to.y, 0));
            UnityEditor_Handles.EndGUI();
        }

        private void DrawAxisHandle(Vector2 pos, Color color, bool hovered, string label)
        {
            float size = hovered ? _handleSize * 1.3f : _handleSize;

            Rect handleRect = new Rect(pos.x - size / 2, pos.y - size / 2, size, size);

            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.DrawRect(handleRect, color);
            UnityEditor_Handles.color = Color.white;
            UnityEditor_Handles.DrawSolidRectangleWithOutline(handleRect, Color.clear, Color.white);
            UnityEditor_Handles.EndGUI();

            GUIStyle style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = color;
            style.fontStyle = hovered ? FontStyle.Bold : FontStyle.Normal;
            GUI.Label(new Rect(pos.x + size / 2 + 2, pos.y - 8, 20, 16), label, style);
        }

        private AxisType FindAxisHandleAtScreenPos(Vector2 screenPos, ToolContext ctx)
        {
            // v2.1: プライマリ＋セカンダリの全選択頂点数
            if (GetTotalAffectedCount(ctx) == 0)
                return AxisType.None;

            // 毎回スクリーン座標を計算（キャッシュ廃止）
            UpdateGizmoCenter(ctx);
            Vector2 originScreen = GetGizmoOriginScreen(ctx);
            Vector2 xEnd = GetAxisScreenEnd(ctx, Vector3.right, originScreen);
            Vector2 yEnd = GetAxisScreenEnd(ctx, Vector3.up, originScreen);
            Vector2 zEnd = GetAxisScreenEnd(ctx, Vector3.forward, originScreen);

            // 中央四角のヒットテスト（優先）
            float halfCenter = _centerSize / 2 + 2;  // 少し大きめ
            if (Mathf.Abs(screenPos.x - originScreen.x) < halfCenter &&
                Mathf.Abs(screenPos.y - originScreen.y) < halfCenter)
            {
                return AxisType.Center;
            }

            // X軸先端
            if (Vector2.Distance(screenPos, xEnd) < _handleHitRadius)
            {
                return AxisType.X;
            }

            // Y軸先端
            if (Vector2.Distance(screenPos, yEnd) < _handleHitRadius)
            {
                return AxisType.Y;
            }

            // Z軸先端
            if (Vector2.Distance(screenPos, zEnd) < _handleHitRadius)
            {
                return AxisType.Z;
            }

            return AxisType.None;
        }

        private Vector3 GetAxisDirection(AxisType axis)
        {
            switch (axis)
            {
                case AxisType.X: return Vector3.right;
                case AxisType.Y: return Vector3.up;
                case AxisType.Z: return Vector3.forward;
                default: return Vector3.zero;
            }
        }

        // === 状態アクセス ===

        public bool IsIdle => _state == MoveState.Idle;
        public bool IsMoving => _state == MoveState.MovingVertices || _state == MoveState.AxisDragging || _state == MoveState.CenterDragging;

        // === 辺・面のヒットテスト ===

        private bool IsClickOnSelectedEdge(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshObject == null || ctx.SelectionState == null)
                return false;

            const float hitDistance = 8f;

            foreach (var edge in ctx.SelectionState.Edges)
            {
                if (edge.V1 < 0 || edge.V1 >= ctx.MeshObject.VertexCount ||
                    edge.V2 < 0 || edge.V2 >= ctx.MeshObject.VertexCount)
                    continue;

                Vector3 p1 = ctx.MeshObject.Vertices[edge.V1].Position;
                Vector3 p2 = ctx.MeshObject.Vertices[edge.V2].Position;

                // DisplayMatrixを適用（表示座標系に変換）
                p1 = ctx.DisplayMatrix.MultiplyPoint3x4(p1);
                p2 = ctx.DisplayMatrix.MultiplyPoint3x4(p2);

                Vector2 sp1 = ctx.WorldToScreenPos(p1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 sp2 = ctx.WorldToScreenPos(p2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                float dist = DistanceToLineSegment(mousePos, sp1, sp2);
                if (dist < hitDistance)
                    return true;
            }

            return false;
        }

        private bool IsClickOnSelectedFace(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshObject == null || ctx.SelectionState == null)
                return false;

            foreach (int faceIdx in ctx.SelectionState.Faces)
            {
                if (faceIdx < 0 || faceIdx >= ctx.MeshObject.FaceCount)
                    continue;

                var face = ctx.MeshObject.Faces[faceIdx];
                if (face.VertexCount < 3)
                    continue;

                var screenPoints = new List<Vector2>();
                foreach (int vIdx in face.VertexIndices)
                {
                    if (vIdx >= 0 && vIdx < ctx.MeshObject.VertexCount)
                    {
                        Vector3 p = ctx.MeshObject.Vertices[vIdx].Position;
                        // DisplayMatrixを適用（表示座標系に変換）
                        p = ctx.DisplayMatrix.MultiplyPoint3x4(p);
                        Vector2 sp = ctx.WorldToScreenPos(p, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                        screenPoints.Add(sp);
                    }
                }

                if (screenPoints.Count >= 3 && IsPointInPolygon(mousePos, screenPoints))
                    return true;
            }

            return false;
        }

        private bool IsClickOnSelectedLine(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshObject == null || ctx.SelectionState == null)
                return false;

            const float hitDistance = 8f;

            foreach (int lineIdx in ctx.SelectionState.Lines)
            {
                if (lineIdx < 0 || lineIdx >= ctx.MeshObject.FaceCount)
                    continue;

                var face = ctx.MeshObject.Faces[lineIdx];
                if (face.VertexCount != 2)
                    continue;

                int v1 = face.VertexIndices[0];
                int v2 = face.VertexIndices[1];

                if (v1 < 0 || v1 >= ctx.MeshObject.VertexCount ||
                    v2 < 0 || v2 >= ctx.MeshObject.VertexCount)
                    continue;

                Vector3 p1 = ctx.MeshObject.Vertices[v1].Position;
                Vector3 p2 = ctx.MeshObject.Vertices[v2].Position;

                // DisplayMatrixを適用（表示座標系に変換）
                p1 = ctx.DisplayMatrix.MultiplyPoint3x4(p1);
                p2 = ctx.DisplayMatrix.MultiplyPoint3x4(p2);

                Vector2 sp1 = ctx.WorldToScreenPos(p1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 sp2 = ctx.WorldToScreenPos(p2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                float dist = DistanceToLineSegment(mousePos, sp1, sp2);
                if (dist < hitDistance)
                    return true;
            }

            return false;
        }

        private bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; j = i++)
            {
                if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                    (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float len = line.magnitude;
            if (len < 0.001f) return Vector2.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (len * len));
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }
    }
}