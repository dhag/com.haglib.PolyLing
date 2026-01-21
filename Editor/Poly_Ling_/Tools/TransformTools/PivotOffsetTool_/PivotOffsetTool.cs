// Tools/PivotOffsetTool.cs
// ピボットオフセット移動ツール
// ハンドルを移動すると全頂点が逆方向に移動
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// ピボットオフセット移動ツール
    /// ハンドルを動かすと全頂点が逆方向に移動する
    /// </summary>
    public partial class PivotOffsetTool : IEditTool
    {
        public string Name => "Pivot Offset";
        public string DisplayName => "Pivot Offset";

        //public ToolCategory Category => ToolCategory.Utility;
        /// <summary>
        /// 設定なし（nullを返す）
        /// </summary>
        public IToolSettings Settings => null;

        // === 状態 ===
        private enum ToolState
        {
            Idle,
            AxisDragging,
            CenterDragging
        }
        private ToolState _state = ToolState.Idle;

        // 軸
        private enum AxisType { None, X, Y, Z, Center }
        private AxisType _draggingAxis = AxisType.None;
        private AxisType _hoveredAxis = AxisType.None;
        private Vector2 _lastDragScreenPos;
        private Vector2 _mouseDownScreenPos;

        // ドラッグ開始時の位置
        private Vector3[] _dragStartPositions;
        private Vector3 _totalOffset = Vector3.zero;  // 累積オフセット

        // ギズモ設定
        private float _handleHitRadius = 12f;
        private float _handleSize = 10f;
        private float _centerSize = 16f;
        private float _screenAxisLength = 60f;

        // 最後のマウス位置
        private Vector2 _lastMousePos;
        private ToolContext _lastContext;

        // === IEditTool実装 ===

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (_state != ToolState.Idle)
                return false;

            _mouseDownScreenPos = mousePos;
            _lastMousePos = mousePos;
            _lastContext = ctx;

            // 軸ギズモのヒットテスト
            var hitAxis = FindAxisHandleAtScreenPos(mousePos, ctx);
            if (hitAxis != AxisType.None)
            {
                StartDrag(ctx, hitAxis);
                return true;
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            _lastMousePos = mousePos;
            _lastContext = ctx;

            switch (_state)
            {
                case ToolState.AxisDragging:
                    MoveAlongAxis(mousePos, ctx);
                    _lastDragScreenPos = mousePos;
                    ctx.Repaint?.Invoke();
                    return true;

                case ToolState.CenterDragging:
                    MoveFreely(delta, ctx);
                    ctx.Repaint?.Invoke();
                    return true;
            }

            // ホバー更新
            if (_state == ToolState.Idle)
            {
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

            if (_state == ToolState.AxisDragging || _state == ToolState.CenterDragging)
            {
                EndDrag(ctx);
                handled = true;
            }

            Reset();
            ctx.Repaint?.Invoke();
            return handled;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            _lastContext = ctx;

            // ホバー更新
            if (_state == ToolState.Idle)
            {
                _hoveredAxis = FindAxisHandleAtScreenPos(_lastMousePos, ctx);
            }

            DrawAxisGizmo(ctx);
        }


        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.miniBoldLabel);  // ← 変更
            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);  // ← 変更

            if (_state != ToolState.Idle)
            {
                EditorGUILayout.LabelField(T("Moving", _totalOffset.ToString("F3")));  // ← 変更
            }
        }
        public void OnActivate(ToolContext ctx)
        {
            _lastContext = ctx;
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
        }

        public void Reset()
        {
            _state = ToolState.Idle;
            _draggingAxis = AxisType.None;
            _hoveredAxis = AxisType.None;
            _dragStartPositions = null;
            _totalOffset = Vector3.zero;
        }

        // === ドラッグ処理 ===

        private void StartDrag(ToolContext ctx, AxisType axis)
        {
            _draggingAxis = axis;
            _lastDragScreenPos = _mouseDownScreenPos;
            _totalOffset = Vector3.zero;

            // 全頂点の開始位置を記録
            _dragStartPositions = ctx.MeshObject.Vertices.Select(v => v.Position).ToArray();

            _state = (axis == AxisType.Center) ? ToolState.CenterDragging : ToolState.AxisDragging;
        }

        private void MoveAlongAxis(Vector2 mousePos, ToolContext ctx)
        {
            if (_draggingAxis == AxisType.None || _draggingAxis == AxisType.Center)
                return;

            Vector3 axisDirection = GetAxisDirection(_draggingAxis);

            // 原点を基準に軸方向を計算
            Vector2 originScreen = ctx.WorldToScreenPos(Vector3.zero, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector3 axisEnd = axisDirection * 0.1f;
            Vector2 axisEndScreen = ctx.WorldToScreenPos(axisEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            Vector2 axisScreenDir = (axisEndScreen - originScreen).normalized;
            if (axisScreenDir.sqrMagnitude < 0.001f)
            {
                // 軸がカメラを向いている場合のフォールバック
                if (axisDirection == Vector3.right) axisScreenDir = new Vector2(1, 0);
                else if (axisDirection == Vector3.up) axisScreenDir = new Vector2(0, -1);
                else axisScreenDir = new Vector2(-0.7f, 0.7f);
            }

            Vector2 mouseDelta = mousePos - _lastDragScreenPos;
            float screenMovement = Vector2.Dot(mouseDelta, axisScreenDir);
            float worldMovement = screenMovement * ctx.CameraDistance * 0.002f;

            // ハンドルの移動方向 = 頂点の逆方向
            // なのでworldMovementを反転
            Vector3 vertexDelta = axisDirection * (-worldMovement);
            _totalOffset += vertexDelta;

            ApplyOffset(vertexDelta, ctx);
        }

        private void MoveFreely(Vector2 screenDelta, ToolContext ctx)
        {
            Vector3 worldDelta = ctx.ScreenDeltaToWorldDelta(
                screenDelta, ctx.CameraPosition, ctx.CameraTarget,
                ctx.CameraDistance, ctx.PreviewRect);

            // ハンドルの移動方向 = 頂点の逆方向
            Vector3 vertexDelta = -worldDelta;
            _totalOffset += vertexDelta;

            ApplyOffset(vertexDelta, ctx);
        }

        private void ApplyOffset(Vector3 delta, ToolContext ctx)
        {
            // 全頂点を移動
            for (int i = 0; i < ctx.MeshObject.VertexCount; i++)
            {
                var v = ctx.MeshObject.Vertices[i];
                v.Position += delta;
                ctx.MeshObject.Vertices[i] = v;

                // オフセット更新
                if (ctx.VertexOffsets != null && i < ctx.VertexOffsets.Length)
                {
                    ctx.VertexOffsets[i] = ctx.MeshObject.Vertices[i].Position - ctx.OriginalPositions[i];
                    ctx.GroupOffsets[i] = ctx.VertexOffsets[i];
                }
            }

            ctx.SyncMesh?.Invoke();

            if (ctx.UndoController != null)
            {
                ctx.UndoController.MeshUndoContext.MeshObject = ctx.MeshObject;
            }
        }

        private void EndDrag(ToolContext ctx)
        {
            if (_dragStartPositions == null || ctx.MeshObject == null)
            {
                _dragStartPositions = null;
                _draggingAxis = AxisType.None;
                return;
            }

            // Undo記録（全頂点）
            var movedIndices = new List<int>();
            var oldPositions = new List<Vector3>();
            var newPositions = new List<Vector3>();

            for (int i = 0; i < ctx.MeshObject.VertexCount; i++)
            {
                Vector3 oldPos = _dragStartPositions[i];
                Vector3 newPos = ctx.MeshObject.Vertices[i].Position;

                if (Vector3.Distance(oldPos, newPos) > 0.0001f)
                {
                    movedIndices.Add(i);
                    oldPositions.Add(oldPos);
                    newPositions.Add(newPos);
                }
            }

            if (movedIndices.Count > 0 && ctx.UndoController != null)
            {
                string axisName = _draggingAxis == AxisType.Center ? "Free" : _draggingAxis.ToString();
                string actionName = $"Pivot Offset ({axisName})";

                var record = new VertexMoveRecord(
                    movedIndices.ToArray(),
                    oldPositions.ToArray(),
                    newPositions.ToArray());
                ctx.UndoController.FocusVertexEdit();
                ctx.UndoController.VertexEditStack.Record(record, actionName);
            }

            _dragStartPositions = null;
            _draggingAxis = AxisType.None;
        }

        // === ギズモ描画 ===

        private void DrawAxisGizmo(ToolContext ctx)
        {
            // 原点をスクリーン座標に変換
            Vector2 originScreen = ctx.WorldToScreenPos(Vector3.zero, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            if (!ctx.PreviewRect.Contains(originScreen))
                return;

            // X軸
            Vector2 xEnd = GetAxisScreenEnd(ctx, Vector3.right, originScreen);
            bool xHovered = _hoveredAxis == AxisType.X || _draggingAxis == AxisType.X;
            Color xColor = xHovered ? Color.yellow : Color.red;
            float xWidth = xHovered ? 3f : 2f;
            DrawAxisLine(originScreen, xEnd, xColor, xWidth);
            DrawAxisHandle(xEnd, xColor, xHovered, "X");

            // Y軸
            Vector2 yEnd = GetAxisScreenEnd(ctx, Vector3.up, originScreen);
            bool yHovered = _hoveredAxis == AxisType.Y || _draggingAxis == AxisType.Y;
            Color yColor = yHovered ? Color.yellow : Color.green;
            float yWidth = yHovered ? 3f : 2f;
            DrawAxisLine(originScreen, yEnd, yColor, yWidth);
            DrawAxisHandle(yEnd, yColor, yHovered, "Y");

            // Z軸
            Vector2 zEnd = GetAxisScreenEnd(ctx, Vector3.forward, originScreen);
            bool zHovered = _hoveredAxis == AxisType.Z || _draggingAxis == AxisType.Z;
            Color zColor = zHovered ? Color.yellow : Color.blue;
            float zWidth = zHovered ? 3f : 2f;
            DrawAxisLine(originScreen, zEnd, zColor, zWidth);
            DrawAxisHandle(zEnd, zColor, zHovered, "Z");

            // 中心点（大きく）
            bool centerHovered = _hoveredAxis == AxisType.Center || _state == ToolState.CenterDragging;
            Color centerColor = centerHovered ? Color.yellow : new Color(1f, 0.8f, 0.2f);  // オレンジ系
            float currentCenterSize = centerHovered ? _centerSize * 1.2f : _centerSize;

            Rect centerRect = new Rect(
                originScreen.x - currentCenterSize / 2,
                originScreen.y - currentCenterSize / 2,
                currentCenterSize,
                currentCenterSize);

            // 中央の枠線
            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.DrawRect(centerRect, centerColor);// 中心点
            UnityEditor_Handles.color = centerHovered ? Color.white : new Color(0.8f, 0.5f, 0f);
            UnityEditor_Handles.DrawSolidRectangleWithOutline(centerRect, Color.clear, UnityEditor_Handles.color);
            UnityEditor_Handles.EndGUI();

            // ラベル
            GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel);
            labelStyle.normal.textColor = centerColor;
            labelStyle.fontStyle = centerHovered ? FontStyle.Bold : FontStyle.Normal;
            GUI.Label(new Rect(originScreen.x + currentCenterSize / 2 + 4, originScreen.y - 8, 50, 16),
                T("Pivot"), labelStyle);  // ← 変更
        }
    

        private Vector2 GetAxisScreenEnd(ToolContext ctx, Vector3 axisDirection, Vector2 originScreen)
        {
            Vector3 axisEnd = axisDirection * 0.1f;
            Vector2 axisEndScreen = ctx.WorldToScreenPos(axisEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            Vector2 dir = (axisEndScreen - originScreen).normalized;

            if (dir.sqrMagnitude < 0.001f)
            {
                if (axisDirection == Vector3.right) dir = new Vector2(1, 0);
                else if (axisDirection == Vector3.up) dir = new Vector2(0, -1);
                else dir = new Vector2(-0.7f, 0.7f);
            }

            return originScreen + dir * _screenAxisLength;
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
            Vector2 originScreen = ctx.WorldToScreenPos(Vector3.zero, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            // 中央四角のヒットテスト（優先）
            float halfCenter = _centerSize / 2 + 2;
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

        public bool IsIdle => _state == ToolState.Idle;
        public bool IsMoving => _state != ToolState.Idle;
    }
}
