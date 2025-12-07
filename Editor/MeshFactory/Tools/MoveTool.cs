// Tools/MoveTool.cs
// 頂点移動ツール（マグネット、軸ドラッグ含む）

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Transforms;
using MeshFactory.UndoSystem;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 頂点移動ツール
    /// </summary>
    public class MoveTool : IEditTool
    {
        public string Name => "Move";

        // === 状態 ===
        private enum MoveState
        {
            Idle,
            PendingAction,
            MovingVertices,
            AxisDragging
        }
        private MoveState _state = MoveState.Idle;

        // 軸
        private enum AxisType { None, X, Y, Z }
        private AxisType _hitAxisOnMouseDown = AxisType.None;
        private AxisType _draggingAxis = AxisType.None;
        private Vector3 _gizmoCenter = Vector3.zero;
        private Vector2 _lastAxisDragScreenPos;

        // ドラッグ
        private Vector2 _mouseDownScreenPos;
        private int _hitVertexOnMouseDown = -1;
        private Vector3[] _dragStartPositions;
        private IVertexTransform _currentTransform;
        private const float DragThreshold = 4f;

        // マグネット設定（外部から設定可能）
        public bool UseMagnet { get; set; } = false;
        public float MagnetRadius { get; set; } = 0.5f;
        public FalloffType MagnetFalloff { get; set; } = FalloffType.Smooth;

        // === IEditTool実装 ===

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (_state != MoveState.Idle)
                return false;

            _mouseDownScreenPos = mousePos;

            // 軸ギズモのヒットテスト（軸がヒットした場合のみ処理）
            if (ctx.SelectedVertices.Count > 0)
            {
                UpdateGizmoCenter(ctx);
                _hitAxisOnMouseDown = FindAxisAtScreenPos(mousePos, ctx);
                if (_hitAxisOnMouseDown != AxisType.None)
                {
                    StartAxisDrag(ctx, _hitAxisOnMouseDown);
                    return true;  // 軸ドラッグ開始、イベント消費
                }
            }

            // 頂点のヒットテスト（移動準備のため記録するが、処理は委譲）
            _hitVertexOnMouseDown = ctx.FindVertexAtScreenPos(
                mousePos, ctx.MeshData, ctx.PreviewRect, 
                ctx.CameraPosition, ctx.CameraTarget, ctx.HandleRadius);
            
            // 頂点がヒットした場合のみ移動準備
            if (_hitVertexOnMouseDown >= 0)
            {
                _state = MoveState.PendingAction;
                return false;  // SimpleMeshFactory側でクリック選択処理を行わせる
            }

            return false;  // 空白クリック、SimpleMeshFactory側で処理
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            switch (_state)
            {
                case MoveState.PendingAction:
                    float dragDistance = Vector2.Distance(mousePos, _mouseDownScreenPos);
                    if (dragDistance > DragThreshold)
                    {
                        if (_hitVertexOnMouseDown >= 0)
                        {
                            StartVertexMove(ctx);
                        }
                        // 空白からのドラッグは矩形選択（メイン側で処理）
                        else
                        {
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
            if (ctx.SelectedVertices == null || ctx.SelectedVertices.Count == 0)
                return;

            UpdateGizmoCenter(ctx);
            DrawAxisGizmo(ctx);
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Magnet", EditorStyles.miniBoldLabel);
            UseMagnet = EditorGUILayout.Toggle("Enable", UseMagnet);

            using (new EditorGUI.DisabledScope(!UseMagnet))
            {
                MagnetRadius = EditorGUILayout.Slider("Radius", MagnetRadius, 0.01f, 2f);
                MagnetFalloff = (FalloffType)EditorGUILayout.EnumPopup("Falloff", MagnetFalloff);
            }
        }

        public void OnActivate(ToolContext ctx) { }
        public void OnDeactivate(ToolContext ctx) { Reset(); }

        public void Reset()
        {
            _state = MoveState.Idle;
            _hitVertexOnMouseDown = -1;
            _hitAxisOnMouseDown = AxisType.None;
            _draggingAxis = AxisType.None;
            _currentTransform = null;
            _dragStartPositions = null;
        }

        // === 頂点移動処理 ===

        private void StartVertexMove(ToolContext ctx)
        {
            var oldSelection = new HashSet<int>(ctx.SelectedVertices);
            bool selectionChanged = false;

            // 未選択頂点をドラッグした場合、その頂点のみ選択
            if (!ctx.SelectedVertices.Contains(_hitVertexOnMouseDown))
            {
                ctx.SelectedVertices.Clear();
                ctx.SelectedVertices.Add(_hitVertexOnMouseDown);
                selectionChanged = true;
            }

            if (selectionChanged)
            {
                ctx.RecordSelectionChange?.Invoke(oldSelection, ctx.SelectedVertices);
            }

            // ドラッグ開始位置を記録
            _dragStartPositions = ctx.MeshData.Vertices.Select(v => v.Position).ToArray();

            // 変形オブジェクトを作成
            if (UseMagnet)
            {
                _currentTransform = new MagnetMoveTransform(MagnetRadius, MagnetFalloff);
            }
            else
            {
                _currentTransform = new SimpleMoveTransform();
            }

            _currentTransform.Begin(ctx.MeshData, ctx.SelectedVertices, _dragStartPositions);
            _state = MoveState.MovingVertices;
        }

        private void MoveSelectedVertices(Vector2 screenDelta, ToolContext ctx)
        {
            if (ctx.SelectedVertices.Count == 0 || _currentTransform == null)
                return;

            Vector3 worldDelta = ctx.ScreenDeltaToWorldDelta(
                screenDelta, ctx.CameraPosition, ctx.CameraTarget, 
                ctx.CameraDistance, ctx.PreviewRect);

            _currentTransform.Apply(worldDelta);

            // オフセット更新
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

            // Undo記録
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

            _currentTransform.Begin(ctx.MeshData, ctx.SelectedVertices, _dragStartPositions);
            _state = MoveState.AxisDragging;
        }

        private void MoveVerticesAlongAxis(Vector2 mousePos, ToolContext ctx)
        {
            if (_currentTransform == null || _draggingAxis == AxisType.None)
                return;

            Vector3 axisDirection = GetAxisDirection(_draggingAxis);

            Vector2 originScreen = ctx.WorldToScreenPos(_gizmoCenter, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector3 axisEnd = _gizmoCenter + axisDirection * 0.3f;
            Vector2 axisEndScreen = ctx.WorldToScreenPos(axisEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            Vector2 axisScreenDir = (axisEndScreen - originScreen).normalized;
            Vector2 mouseDelta = mousePos - _lastAxisDragScreenPos;
            float screenMovement = Vector2.Dot(mouseDelta, axisScreenDir);
            float worldMovement = screenMovement * ctx.CameraDistance * 0.002f;

            Vector3 worldDelta = axisDirection * worldMovement;
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

        private void EndAxisDrag(ToolContext ctx)
        {
            if (_currentTransform == null || ctx.MeshData == null)
            {
                _currentTransform = null;
                _dragStartPositions = null;
                _draggingAxis = AxisType.None;
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
                string axisName = _draggingAxis.ToString();
                string actionName = UseMagnet
                    ? $"Magnet {axisName}-Axis Move {movedIndices.Count} Vertices"
                    : $"{axisName}-Axis Move {movedIndices.Count} Vertices";

                var record = new VertexMoveRecord(
                    movedIndices.ToArray(),
                    oldPositions.ToArray(),
                    newPositions.ToArray());
                ctx.UndoController.VertexEditStack.Record(record, actionName);
            }

            _currentTransform = null;
            _dragStartPositions = null;
            _draggingAxis = AxisType.None;
        }

        // === ギズモ描画 ===

        private void UpdateGizmoCenter(ToolContext ctx)
        {
            if (ctx.SelectedVertices.Count > 0 && ctx.MeshData != null)
            {
                Vector3 sum = Vector3.zero;
                foreach (int idx in ctx.SelectedVertices)
                {
                    if (idx >= 0 && idx < ctx.MeshData.VertexCount)
                    {
                        sum += ctx.MeshData.Vertices[idx].Position;
                    }
                }
                _gizmoCenter = sum / ctx.SelectedVertices.Count;
            }
            else
            {
                _gizmoCenter = Vector3.zero;
            }
        }

        private void DrawAxisGizmo(ToolContext ctx)
        {
            Vector2 originScreen = ctx.WorldToScreenPos(_gizmoCenter, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            if (!ctx.PreviewRect.Contains(originScreen))
                return;

            float axisLength = 0.3f;

            // X軸
            Vector3 xEnd = _gizmoCenter + Vector3.right * axisLength;
            Vector2 xScreen = ctx.WorldToScreenPos(xEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Color xColor = _draggingAxis == AxisType.X ? Color.yellow : Color.red;
            float xWidth = _draggingAxis == AxisType.X ? 5f : 3f;
            DrawAxisArrow(originScreen, xScreen, xColor, "X", xWidth);

            // Y軸
            Vector3 yEnd = _gizmoCenter + Vector3.up * axisLength;
            Vector2 yScreen = ctx.WorldToScreenPos(yEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Color yColor = _draggingAxis == AxisType.Y ? Color.yellow : Color.green;
            float yWidth = _draggingAxis == AxisType.Y ? 5f : 3f;
            DrawAxisArrow(originScreen, yScreen, yColor, "Y", yWidth);

            // Z軸
            Vector3 zEnd = _gizmoCenter + Vector3.forward * axisLength;
            Vector2 zScreen = ctx.WorldToScreenPos(zEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Color zColor = _draggingAxis == AxisType.Z ? Color.yellow : Color.blue;
            float zWidth = _draggingAxis == AxisType.Z ? 5f : 3f;
            DrawAxisArrow(originScreen, zScreen, zColor, "Z", zWidth);

            // 中心点
            float centerSize = 6f;
            EditorGUI.DrawRect(new Rect(
                originScreen.x - centerSize / 2,
                originScreen.y - centerSize / 2,
                centerSize,
                centerSize), Color.white);
        }

        private void DrawAxisArrow(Vector2 from, Vector2 to, Color color, string label, float lineWidth = 3f)
        {
            Handles.BeginGUI();
            Handles.color = color;

            Vector3 from3 = new Vector3(from.x, from.y, 0);
            Vector3 to3 = new Vector3(to.x, to.y, 0);
            Handles.DrawAAPolyLine(lineWidth, from3, to3);

            Vector2 dir = (to - from).normalized;
            float arrowSize = 8f;
            Vector2 perpendicular = new Vector2(-dir.y, dir.x);

            Vector2 arrowLeft = to - dir * arrowSize + perpendicular * arrowSize * 0.5f;
            Vector2 arrowRight = to - dir * arrowSize - perpendicular * arrowSize * 0.5f;

            Handles.DrawAAPolyLine(lineWidth, to3, new Vector3(arrowLeft.x, arrowLeft.y, 0));
            Handles.DrawAAPolyLine(lineWidth, to3, new Vector3(arrowRight.x, arrowRight.y, 0));

            Handles.EndGUI();

            GUIStyle style = new GUIStyle(EditorStyles.boldLabel);
            style.normal.textColor = color;
            GUI.Label(new Rect(to.x + 4, to.y - 8, 20, 16), label, style);
        }

        private AxisType FindAxisAtScreenPos(Vector2 screenPos, ToolContext ctx)
        {
            float axisLength = 0.3f;
            float hitRadius = 12f;

            Vector2 originScreen = ctx.WorldToScreenPos(_gizmoCenter, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            Vector3 xEnd = _gizmoCenter + Vector3.right * axisLength;
            Vector2 xScreen = ctx.WorldToScreenPos(xEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            if (DistanceToLineSegment(screenPos, originScreen, xScreen) < hitRadius)
                return AxisType.X;

            Vector3 yEnd = _gizmoCenter + Vector3.up * axisLength;
            Vector2 yScreen = ctx.WorldToScreenPos(yEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            if (DistanceToLineSegment(screenPos, originScreen, yScreen) < hitRadius)
                return AxisType.Y;

            Vector3 zEnd = _gizmoCenter + Vector3.forward * axisLength;
            Vector2 zScreen = ctx.WorldToScreenPos(zEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            if (DistanceToLineSegment(screenPos, originScreen, zScreen) < hitRadius)
                return AxisType.Z;

            return AxisType.None;
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

        // === 状態アクセス（メイン側で必要な場合用） ===

        public bool IsIdle => _state == MoveState.Idle;
        public bool IsMoving => _state == MoveState.MovingVertices || _state == MoveState.AxisDragging;
    }
}
