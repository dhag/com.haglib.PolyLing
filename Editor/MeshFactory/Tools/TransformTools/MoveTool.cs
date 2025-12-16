// Tools/MoveTool.cs
// 頂点移動ツール（マグネット、軸ドラッグ含む）
// 改善版: 正確な軸方向表示、中央ドラッグ対応
// Edge/Face/Line選択時も移動可能
// IToolSettings対応版
// ローカライズ対応版
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Transforms;
using MeshFactory.UndoSystem;
using MeshFactory.Selection;
using MeshFactory.Localization;
using static MeshFactory.Gizmo.GLGizmoDrawer;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 頂点移動ツール
    /// </summary>
    public class MoveTool : IEditTool
    {
        public string Name => "Move";
        public string DisplayName => "Move";
        //public ToolCategory Category => ToolCategory.Transform;  

        /// <summary>
        /// ローカライズされた表示名を取得
        /// </summary>
        public string GetLocalizedDisplayName() => L.Get("Tool_Move");

        // ================================================================
        // ツール固有ローカライズ辞書
        // ================================================================
        
        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["Magnet"] = new() { ["en"] = "Magnet", ["ja"] = "マグネット", ["hi"] = "じしゃく" },
            ["Enable"] = new() { ["en"] = "Enable", ["ja"] = "有効", ["hi"] = "つかう" },
            ["Radius"] = new() { ["en"] = "Radius", ["ja"] = "半径", ["hi"] = "はんけい" },
            ["Falloff"] = new() { ["en"] = "Falloff", ["ja"] = "減衰", ["hi"] = "よわまりかた" },
            ["Gizmo"] = new() { ["en"] = "Gizmo", ["ja"] = "ギズモ", ["hi"] = "ギズモ" },
            ["OffsetX"] = new() { ["en"] = "Offset X", ["ja"] = "オフセット X", ["hi"] = "ずれ X" },
            ["OffsetY"] = new() { ["en"] = "Offset Y", ["ja"] = "オフセット Y", ["hi"] = "ずれ Y" },
            ["TargetVertices"] = new() { ["en"] = "Target: {0} vertices", ["ja"] = "移動対象: {0} 頂点", ["hi"] = "うごかすてん: {0}こ" },
        };
        
        /// <summary>ツール内ローカライズ取得</summary>
        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);  

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

        // ドラッグ開始時のヒット情報
        private enum PendingHitType
        {
            None,
            Vertex,
            Edge,           // 新規Edge選択+移動
            Face,           // 新規Face選択+移動
            Line,           // 新規Line選択+移動
            SelectedEdge,   // 既存選択Edgeの移動
            SelectedFace,   // 既存選択Faceの移動
            SelectedLine    // 既存選択Lineの移動
        }
        private PendingHitType _pendingHitType = PendingHitType.None;
        private VertexPair _pendingEdgePair;
        private int _pendingFaceIndex = -1;
        private int _pendingLineIndex = -1;

        // ギズモ設定
        private Vector2 _gizmoScreenOffset = new Vector2(60, -60);  // 重心からのスクリーンオフセット（右上）
        private float _handleHitRadius = 10f;  // 軸先端のヒット半径（ピクセル）
        private float _handleSize = 8f;  // 軸先端のハンドルサイズ（ピクセル）
        private float _centerSize = 14f;  // 中央四角のサイズ（ピクセル）
        private float _screenAxisLength = 50f;  // 軸の長さ（ピクセル）

        // 最後のマウス位置（ホバー検出用）
        private Vector2 _lastMousePos;
        private ToolContext _lastContext;

        // === IEditTool実装 ===

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (_state != MoveState.Idle)
                return false;

            _mouseDownScreenPos = mousePos;
            _lastMousePos = mousePos;
            _lastContext = ctx;

            // 影響を受ける頂点を更新
            UpdateAffectedVertices(ctx);

            // 軸ギズモのヒットテスト（軸先端または中央がヒットした場合のみ処理）
            if (_affectedVertices.Count > 0)
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
                    return true;  // ドラッグ開始、イベント消費
                }
            }

            // =========================================================
            // 既存選択のヒットテストを先に行う（新規選択より優先）
            // =========================================================

            // 選択された辺の線上をクリックした場合（既存選択の移動）
            if (ctx.SelectionState != null && ctx.SelectionState.Edges.Count > 0)
            {
                if (IsClickOnSelectedEdge(ctx, mousePos))
                {
                    _state = MoveState.PendingAction;
                    _pendingHitType = PendingHitType.SelectedEdge;
                    return true;
                }
            }

            // 選択されたLineの線上をクリックした場合
            if (ctx.SelectionState != null && ctx.SelectionState.Lines.Count > 0)
            {
                if (IsClickOnSelectedLine(ctx, mousePos))
                {
                    _state = MoveState.PendingAction;
                    _pendingHitType = PendingHitType.SelectedLine;
                    return true;
                }
            }

            // 選択された面の内部をクリックした場合
            if (ctx.SelectionState != null && ctx.SelectionState.Faces.Count > 0)
            {
                if (IsClickOnSelectedFace(ctx, mousePos))
                {
                    _state = MoveState.PendingAction;
                    _pendingHitType = PendingHitType.SelectedFace;
                    return true;
                }
            }

            // =========================================================
            // 頂点のヒットテスト
            // =========================================================
            _hitVertexOnMouseDown = ctx.FindVertexAtScreenPos(
                mousePos, ctx.MeshData, ctx.PreviewRect,
                ctx.CameraPosition, ctx.CameraTarget, ctx.HandleRadius);

            // 頂点がヒットした場合は移動準備
            if (_hitVertexOnMouseDown >= 0)
            {
                _state = MoveState.PendingAction;
                _pendingHitType = PendingHitType.Vertex;
                return false;  // SimpleMeshFactory側でクリック選択処理を行わせる
            }

            // =========================================================
            // 新規Edge/Face/Lineのヒットテスト（既存選択がない場合）
            // =========================================================
            if (ctx.SelectionState != null && ctx.SelectionOps != null)
            {
                var mode = ctx.SelectionState.Mode;
                Func<Vector3, Vector2> worldToScreen = (pos) =>
                    ctx.WorldToScreenPos(pos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                // Edgeモードでのヒットテスト
                if (mode.Has(MeshSelectMode.Edge))
                {
                    var edgePair = ctx.SelectionOps.FindEdgeAt(mousePos, ctx.MeshData, worldToScreen);
                    if (edgePair.HasValue)
                    {
                        _pendingEdgePair = edgePair.Value;
                        _state = MoveState.PendingAction;
                        _pendingHitType = PendingHitType.Edge;
                        return true;
                    }
                }

                // Lineモードでのヒットテスト
                if (mode.Has(MeshSelectMode.Line))
                {
                    int lineIdx = ctx.SelectionOps.FindLineAt(mousePos, ctx.MeshData, worldToScreen);
                    if (lineIdx >= 0)
                    {
                        _pendingLineIndex = lineIdx;
                        _state = MoveState.PendingAction;
                        _pendingHitType = PendingHitType.Line;
                        return true;
                    }
                }

                // Faceモードでのヒットテスト
                if (mode.Has(MeshSelectMode.Face))
                {
                    int faceIdx = ctx.SelectionOps.FindFaceAt(mousePos, ctx.MeshData, worldToScreen, ctx.CameraPosition);
                    if (faceIdx >= 0)
                    {
                        _pendingFaceIndex = faceIdx;
                        _state = MoveState.PendingAction;
                        _pendingHitType = PendingHitType.Face;
                        return true;
                    }
                }
            }

            return false;  // 空白クリック、SimpleMeshFactory側で処理
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
                        // ヒットタイプに応じて選択+移動開始
                        switch (_pendingHitType)
                        {
                            case PendingHitType.Vertex:
                                if (_hitVertexOnMouseDown >= 0)
                                    StartVertexMove(ctx);
                                break;
                            case PendingHitType.Edge:
                                SelectAndStartMove_Edge(ctx);
                                break;
                            case PendingHitType.Face:
                                SelectAndStartMove_Face(ctx);
                                break;
                            case PendingHitType.Line:
                                SelectAndStartMove_Line(ctx);
                                break;
                            case PendingHitType.SelectedEdge:
                            case PendingHitType.SelectedFace:
                            case PendingHitType.SelectedLine:
                                // 既存選択の移動
                                StartMoveFromSelection(ctx);
                                break;
                            default:
                                _state = MoveState.Idle;
                                return false;
                        }
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
            if (_state == MoveState.Idle && _affectedVertices.Count > 0)
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

            if (_affectedVertices.Count == 0)
                return;

            UpdateGizmoCenter(ctx);

            // ホバー更新（MouseMoveイベントがない場合用）
            if (_state == MoveState.Idle)
            {
                _hoveredAxis = FindAxisHandleAtScreenPos(_lastMousePos, ctx);
            }

            DrawAxisGizmo(ctx);
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Magnet"), EditorStyles.miniBoldLabel);

            // MoveSettingsを直接編集（Undo検出はGUI_Tools側で行う）
            _settings.UseMagnet = EditorGUILayout.Toggle(T("Enable"), _settings.UseMagnet);

            using (new EditorGUI.DisabledScope(!_settings.UseMagnet))
            {
                _settings.MagnetRadius = EditorGUILayout.Slider(T("Radius"), _settings.MagnetRadius, 0.01f, 2f);
                _settings.MagnetFalloff = (FalloffType)EditorGUILayout.EnumPopup(T("Falloff"), _settings.MagnetFalloff);
            }

            // ギズモ設定（Undo対象外）
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("Gizmo"), EditorStyles.miniBoldLabel);
            _gizmoScreenOffset.x = EditorGUILayout.Slider(T("OffsetX"), _gizmoScreenOffset.x, -100f, 100f);
            _gizmoScreenOffset.y = EditorGUILayout.Slider(T("OffsetY"), _gizmoScreenOffset.y, -100f, 100f);

            // 選択情報表示
            EditorGUILayout.Space(5);
            if (_affectedVertices.Count > 0)
            {
                EditorGUILayout.HelpBox(T("TargetVertices", _affectedVertices.Count), MessageType.None);
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
        }

        // === 影響を受ける頂点の更新 ===

        private void UpdateAffectedVertices(ToolContext ctx)
        {
            _affectedVertices.Clear();

            if (ctx.SelectionState != null)
            {
                var affected = ctx.SelectionState.GetAllAffectedVertices(ctx.MeshData);
                foreach (var v in affected)
                {
                    _affectedVertices.Add(v);
                }
            }
            else if (ctx.SelectedVertices != null)
            {
                foreach (var v in ctx.SelectedVertices)
                {
                    _affectedVertices.Add(v);
                }
            }
        }

        // === 頂点移動処理 ===

        private void StartVertexMove(ToolContext ctx)
        {
            var oldSelection = new HashSet<int>(ctx.SelectedVertices);
            bool selectionChanged = false;

            if (_hitVertexOnMouseDown >= 0 && !_affectedVertices.Contains(_hitVertexOnMouseDown))
            {
                ctx.SelectedVertices.Clear();
                ctx.SelectedVertices.Add(_hitVertexOnMouseDown);

                if (ctx.SelectionState != null)
                {
                    ctx.SelectionState.ClearAll();
                    ctx.SelectionState.SelectVertex(_hitVertexOnMouseDown, false);
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

            ctx.SelectionState.ClearAll();
            ctx.SelectionState.SelectEdge(_pendingEdgePair, false);
            ctx.SelectedVertices.Clear();
            UpdateAffectedVertices(ctx);
            BeginMove(ctx);
        }

        private void SelectAndStartMove_Face(ToolContext ctx)
        {
            if (ctx.SelectionState == null || _pendingFaceIndex < 0) return;

            ctx.SelectionState.ClearAll();
            ctx.SelectionState.SelectFace(_pendingFaceIndex, false);
            ctx.SelectedVertices.Clear();
            UpdateAffectedVertices(ctx);
            BeginMove(ctx);
        }

        private void SelectAndStartMove_Line(ToolContext ctx)
        {
            if (ctx.SelectionState == null || _pendingLineIndex < 0) return;

            ctx.SelectionState.ClearAll();
            ctx.SelectionState.SelectLine(_pendingLineIndex, false);
            ctx.SelectedVertices.Clear();
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
            if (_affectedVertices.Count == 0)
            {
                _state = MoveState.Idle;
                return;
            }

            _dragStartPositions = ctx.MeshData.Vertices.Select(v => v.Position).ToArray();

            if (UseMagnet)
            {
                _currentTransform = new MagnetMoveTransform(MagnetRadius, MagnetFalloff);
            }
            else
            {
                _currentTransform = new SimpleMoveTransform();
            }

            _currentTransform.Begin(ctx.MeshData, _affectedVertices, _dragStartPositions);
            _state = MoveState.MovingVertices;
        }

        private void MoveSelectedVertices(Vector2 screenDelta, ToolContext ctx)
        {
            if (_affectedVertices.Count == 0 || _currentTransform == null)
                return;

            Vector3 worldDelta = ctx.ScreenDeltaToWorldDelta(
                screenDelta, ctx.CameraPosition, ctx.CameraTarget,
                ctx.CameraDistance, ctx.PreviewRect);

            _currentTransform.Apply(worldDelta);

            var affectedIndices = _currentTransform.GetAffectedIndices();
            foreach (int idx in affectedIndices)
            {
                if (ctx.VertexOffsets != null && idx >= 0 && idx < ctx.VertexOffsets.Length)
                {
                    ctx.VertexOffsets[idx] = ctx.MeshData.Vertices[idx].Position - ctx.OriginalPositions[idx];
                    ctx.GroupOffsets[idx] = ctx.VertexOffsets[idx];
                }
            }

            ctx.SyncMesh?.Invoke();

            if (ctx.UndoController != null)
            {
                ctx.UndoController.MeshContext.MeshData = ctx.MeshData;
            }
        }

        private void EndVertexMove(ToolContext ctx)
        {
            if (_currentTransform == null || ctx.MeshData == null)
            {
                _currentTransform = null;
                _dragStartPositions = null;
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
                ctx.UndoController.VertexEditStack.Record(record, actionName);
            }

            _currentTransform = null;
            _dragStartPositions = null;
        }

        // === 軸ドラッグ処理 ===

        private void StartAxisDrag(ToolContext ctx, AxisType axis)
        {
            _draggingAxis = axis;
            _lastAxisDragScreenPos = _mouseDownScreenPos;

            _dragStartPositions = ctx.MeshData.Vertices.Select(v => v.Position).ToArray();

            if (UseMagnet)
            {
                _currentTransform = new MagnetMoveTransform(MagnetRadius, MagnetFalloff);
            }
            else
            {
                _currentTransform = new SimpleMoveTransform();
            }

            _currentTransform.Begin(ctx.MeshData, _affectedVertices, _dragStartPositions);
            _state = MoveState.AxisDragging;
        }

        private void MoveVerticesAlongAxis(Vector2 currentScreenPos, ToolContext ctx)
        {
            if (_currentTransform == null || ctx.MeshData == null)
                return;

            // スクリーン上での移動量を計算
            Vector2 screenDelta = currentScreenPos - _lastAxisDragScreenPos;
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
            Vector3 worldDelta = axisDir * axisScreenMovement * worldScale;

            _currentTransform.Apply(worldDelta);

            var affectedIndices = _currentTransform.GetAffectedIndices();
            foreach (int idx in affectedIndices)
            {
                if (ctx.VertexOffsets != null && idx >= 0 && idx < ctx.VertexOffsets.Length)
                {
                    ctx.VertexOffsets[idx] = ctx.MeshData.Vertices[idx].Position - ctx.OriginalPositions[idx];
                    ctx.GroupOffsets[idx] = ctx.VertexOffsets[idx];
                }
            }

            ctx.SyncMesh?.Invoke();
        }

        private void EndAxisDrag(ToolContext ctx)
        {
            EndVertexMove(ctx);  // 共通処理
            _draggingAxis = AxisType.None;
        }

        // === 中央ドラッグ処理 ===

        private void StartCenterDrag(ToolContext ctx)
        {
            _dragStartPositions = ctx.MeshData.Vertices.Select(v => v.Position).ToArray();

            if (UseMagnet)
            {
                _currentTransform = new MagnetMoveTransform(MagnetRadius, MagnetFalloff);
            }
            else
            {
                _currentTransform = new SimpleMoveTransform();
            }

            _currentTransform.Begin(ctx.MeshData, _affectedVertices, _dragStartPositions);
            _state = MoveState.CenterDragging;
        }

        private void EndCenterDrag(ToolContext ctx)
        {
            EndVertexMove(ctx);  // 共通処理
        }

        // === ギズモ計算・描画 ===

        private void UpdateGizmoCenter(ToolContext ctx)
        {
            if (_affectedVertices.Count == 0 || ctx.MeshData == null)
            {
                _selectionCenter = Vector3.zero;
                _gizmoCenter = Vector3.zero;
                return;
            }

            // 選択頂点の重心を計算
            _selectionCenter = Vector3.zero;
            foreach (int vi in _affectedVertices)
            {
                if (vi >= 0 && vi < ctx.MeshData.VertexCount)
                {
                    _selectionCenter += ctx.MeshData.Vertices[vi].Position;
                }
            }
            _selectionCenter /= _affectedVertices.Count;
            _gizmoCenter = _selectionCenter;
        }

        private Vector2 GetGizmoOriginScreen(ToolContext ctx)
        {
            // 重心のスクリーン座標 + オフセット
            Vector2 centerScreen = ctx.WorldToScreenPos(
                _selectionCenter, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            return centerScreen + _gizmoScreenOffset;
        }

        private Vector3 GetAxisScreenDirection(ToolContext ctx, Vector3 worldAxis)
        {
            // 重心からワールド軸方向に少し進んだ点のスクリーン座標を計算
            Vector3 axisEnd = _selectionCenter + worldAxis;
            Vector2 centerScreen = ctx.WorldToScreenPos(
                _selectionCenter, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 axisEndScreen = ctx.WorldToScreenPos(
                axisEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            Vector2 screenDir = (axisEndScreen - centerScreen).normalized;
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
            if (_affectedVertices.Count == 0)
                return AxisType.None;

            Vector2 originScreen = GetGizmoOriginScreen(ctx);

            // 中央四角のヒットテスト（優先）
            float halfCenter = _centerSize / 2 + 2;  // 少し大きめ
            if (Mathf.Abs(screenPos.x - originScreen.x) < halfCenter &&
                Mathf.Abs(screenPos.y - originScreen.y) < halfCenter)
            {
                return AxisType.Center;
            }

            // X軸先端
            Vector2 xEnd = GetAxisScreenEnd(ctx, Vector3.right, originScreen);
            if (Vector2.Distance(screenPos, xEnd) < _handleHitRadius)
                return AxisType.X;

            // Y軸先端
            Vector2 yEnd = GetAxisScreenEnd(ctx, Vector3.up, originScreen);
            if (Vector2.Distance(screenPos, yEnd) < _handleHitRadius)
                return AxisType.Y;

            // Z軸先端
            Vector2 zEnd = GetAxisScreenEnd(ctx, Vector3.forward, originScreen);
            if (Vector2.Distance(screenPos, zEnd) < _handleHitRadius)
                return AxisType.Z;

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
            if (ctx.MeshData == null || ctx.SelectionState == null)
                return false;

            const float hitDistance = 8f;

            foreach (var edge in ctx.SelectionState.Edges)
            {
                if (edge.V1 < 0 || edge.V1 >= ctx.MeshData.VertexCount ||
                    edge.V2 < 0 || edge.V2 >= ctx.MeshData.VertexCount)
                    continue;

                Vector3 p1 = ctx.MeshData.Vertices[edge.V1].Position;
                Vector3 p2 = ctx.MeshData.Vertices[edge.V2].Position;

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
            if (ctx.MeshData == null || ctx.SelectionState == null)
                return false;

            foreach (int faceIdx in ctx.SelectionState.Faces)
            {
                if (faceIdx < 0 || faceIdx >= ctx.MeshData.FaceCount)
                    continue;

                var face = ctx.MeshData.Faces[faceIdx];
                if (face.VertexCount < 3)
                    continue;

                var screenPoints = new List<Vector2>();
                foreach (int vIdx in face.VertexIndices)
                {
                    if (vIdx >= 0 && vIdx < ctx.MeshData.VertexCount)
                    {
                        Vector3 p = ctx.MeshData.Vertices[vIdx].Position;
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
            if (ctx.MeshData == null || ctx.SelectionState == null)
                return false;

            const float hitDistance = 8f;

            foreach (int lineIdx in ctx.SelectionState.Lines)
            {
                if (lineIdx < 0 || lineIdx >= ctx.MeshData.FaceCount)
                    continue;

                var face = ctx.MeshData.Faces[lineIdx];
                if (face.VertexCount != 2)
                    continue;

                int v1 = face.VertexIndices[0];
                int v2 = face.VertexIndices[1];

                if (v1 < 0 || v1 >= ctx.MeshData.VertexCount ||
                    v2 < 0 || v2 >= ctx.MeshData.VertexCount)
                    continue;

                Vector3 p1 = ctx.MeshData.Vertices[v1].Position;
                Vector3 p2 = ctx.MeshData.Vertices[v2].Position;

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
