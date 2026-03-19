// Tools/PivotOffsetTool.cs
// ピボットオフセット移動ツール
// ハンドルを移動すると全頂点が逆方向に移動
using System;
using System.Collections.Generic;
using System.Linq;
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
        private BoneTransformSnapshot _dragStartBoneSnapshot;   // ← 追加
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
            _dragStartBoneSnapshot = default;
            _totalOffset = Vector3.zero;
        }

        // === ドラッグ処理 ===

        private void StartDrag(ToolContext ctx, AxisType axis)
        {
            _draggingAxis = axis;
            _lastDragScreenPos = _mouseDownScreenPos;
            _totalOffset = Vector3.zero;

            // 全頂点の開始位置を記録
            _dragStartPositions = (Vector3[])ctx.FirstSelectedMeshObject.Positions.Clone();

            // BoneTransform の開始状態を記録
            var mc = ctx.FirstSelectedMeshContext;
            if (mc?.BoneTransform != null)
                _dragStartBoneSnapshot = mc.BoneTransform.CreateSnapshot();

            _state = (axis == AxisType.Center) ? ToolState.CenterDragging : ToolState.AxisDragging;
        }

        private void MoveAlongAxis(Vector2 mousePos, ToolContext ctx)
        {
            if (_draggingAxis == AxisType.None || _draggingAxis == AxisType.Center)
                return;

            // 選択メッシュのワールド位置をピボットとして使用
            var worldMatrix = ctx.FirstSelectedMeshContext?.WorldMatrix ?? Matrix4x4.identity;
            Vector3 axisWorldDir = GetLocalAxisDirection(_draggingAxis, worldMatrix); // スクリーン投影用（ワールド空間）
            Vector3 pivotWorld = worldMatrix.GetColumn(3);
            Vector2 originScreen = ctx.WorldToScreenPos(pivotWorld, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector3 axisEnd = pivotWorld + axisWorldDir * 0.1f;
            Vector2 axisEndScreen = ctx.WorldToScreenPos(axisEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            Vector2 axisScreenDir = (axisEndScreen - originScreen).normalized;
            if (axisScreenDir.sqrMagnitude < 0.001f)
            {
                // 軸がカメラを向いている場合のフォールバック
                if (_draggingAxis == AxisType.X) axisScreenDir = new Vector2(1, 0);
                else if (_draggingAxis == AxisType.Y) axisScreenDir = new Vector2(0, -1);
                else axisScreenDir = new Vector2(-0.7f, 0.7f);
            }

            Vector2 mouseDelta = mousePos - _lastDragScreenPos;
            float screenMovement = Vector2.Dot(mouseDelta, axisScreenDir);
            float worldMovement = screenMovement * ctx.CameraDistance * 0.002f;

            // 頂点はローカル空間に格納されているのでローカル軸で移動する
            // ハンドルの移動方向 = 頂点の逆方向
            Vector3 localAxisDir = GetAxisDirection(_draggingAxis); // ローカル空間軸（Vector3.up等）
            Vector3 vertexDelta = localAxisDir * (-worldMovement);
            _totalOffset += vertexDelta;

            // BoneTransform のデルタはワールド空間で +worldMovement（ハンドル方向）
            Vector3 boneWorldDelta = GetLocalAxisDirection(_draggingAxis, worldMatrix) * worldMovement;
            ApplyOffset(vertexDelta, boneWorldDelta, ctx);
        }

        private void MoveFreely(Vector2 screenDelta, ToolContext ctx)
        {
            Vector3 worldDelta = ctx.ScreenDeltaToWorldDelta(
                screenDelta, ctx.CameraPosition, ctx.CameraTarget,
                ctx.CameraDistance, ctx.PreviewRect);

            // ハンドルの移動方向 = 頂点の逆方向
            Vector3 vertexDelta = -worldDelta;
            _totalOffset += vertexDelta;

            ApplyOffset(vertexDelta, worldDelta, ctx);
        }

        private void ApplyOffset(Vector3 vertexDelta, Vector3 boneWorldDelta, ToolContext ctx)
        {
            // 全頂点を逆方向に移動
            for (int i = 0; i < ctx.FirstSelectedMeshObject.VertexCount; i++)
            {
                var v = ctx.FirstSelectedMeshObject.Vertices[i];
                v.Position += vertexDelta;
                ctx.FirstSelectedMeshObject.Vertices[i] = v;

                // オフセット更新
                if (ctx.VertexOffsets != null && i < ctx.VertexOffsets.Length)
                {
                    ctx.VertexOffsets[i] = ctx.FirstSelectedMeshObject.Vertices[i].Position - ctx.OriginalPositions[i];
                    ctx.GroupOffsets[i] = ctx.VertexOffsets[i];
                }
            }

            // BoneTransform をハンドルと同方向に移動してピボットを追従させる
            var mc = ctx.FirstSelectedMeshContext;
            if (mc?.BoneTransform != null)
            {
                mc.BoneTransform.UseLocalTransform = true;
                mc.BoneTransform.Position += boneWorldDelta;
            }

            ctx.SyncMesh?.Invoke();
            ctx.SyncBoneTransforms?.Invoke();
        }

        private void EndDrag(ToolContext ctx)
        {
            if (_dragStartPositions == null || ctx.FirstSelectedMeshObject == null)
            {
                _dragStartPositions = null;
                _draggingAxis = AxisType.None;
                return;
            }

            var mc = ctx.FirstSelectedMeshContext;
            if (mc == null)
            {
                _dragStartPositions = null;
                _draggingAxis = AxisType.None;
                return;
            }

            // 移動した頂点インデックスと新旧位置を収集
            var movedIndices  = new List<int>();
            var oldPositions  = new List<Vector3>();
            var newPositions  = new List<Vector3>();

            for (int i = 0; i < ctx.FirstSelectedMeshObject.VertexCount; i++)
            {
                Vector3 oldPos = _dragStartPositions[i];
                Vector3 newPos = ctx.FirstSelectedMeshObject.Vertices[i].Position;
                if (Vector3.Distance(oldPos, newPos) > 0.0001f)
                {
                    movedIndices.Add(i);
                    oldPositions.Add(oldPos);
                    newPositions.Add(newPos);
                }
            }

            // OriginalPositions を現在の頂点位置に更新（VertexOffsets の基準をリセット）
            mc.OriginalPositions = (Vector3[])ctx.FirstSelectedMeshObject.Positions.Clone();

            if (movedIndices.Count > 0 && ctx.UndoController != null)
            {
                string axisName = _draggingAxis == AxisType.Center ? "Free" : _draggingAxis.ToString();

                var record = new Poly_Ling.UndoSystem.PivotMoveRecord
                {
                    MasterIndex        = ctx.Model.IndexOf(mc),
                    VertexIndices      = movedIndices.ToArray(),
                    OldVertexPositions = oldPositions.ToArray(),
                    NewVertexPositions = newPositions.ToArray(),
                    OldBoneTransform   = _dragStartBoneSnapshot,
                    NewBoneTransform   = mc.BoneTransform.CreateSnapshot(),
                };

                ctx.UndoController.MeshListStack.Record(record, $"Pivot Move ({axisName})");
                ctx.UndoController.FocusMeshList();
            }

            _dragStartPositions = null;
            _draggingAxis = AxisType.None;
        }

        // === ギズモ描画 ===

        private void DrawAxisGizmo(ToolContext ctx)
        {
            // 選択メッシュのワールド行列からピボット位置とローカル軸方向を取得
            var worldMatrix = ctx.FirstSelectedMeshContext?.WorldMatrix ?? Matrix4x4.identity;
            Vector3 pivotWorld = worldMatrix.GetColumn(3);
            Vector2 originScreen = ctx.WorldToScreenPos(pivotWorld, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            if (!ctx.PreviewRect.Contains(originScreen))
                return;

            // X軸
            Vector2 xEnd = GetAxisScreenEnd(ctx, GetLocalAxisDirection(AxisType.X, worldMatrix), originScreen, pivotWorld, AxisType.X);
            bool xHovered = _hoveredAxis == AxisType.X || _draggingAxis == AxisType.X;
            Color xColor = xHovered ? Color.yellow : Color.red;
            float xWidth = xHovered ? 3f : 2f;
            DrawAxisLine(originScreen, xEnd, xColor, xWidth);
            DrawAxisHandle(xEnd, xColor, xHovered, "X");

            // Y軸
            Vector2 yEnd = GetAxisScreenEnd(ctx, GetLocalAxisDirection(AxisType.Y, worldMatrix), originScreen, pivotWorld, AxisType.Y);
            bool yHovered = _hoveredAxis == AxisType.Y || _draggingAxis == AxisType.Y;
            Color yColor = yHovered ? Color.yellow : Color.green;
            float yWidth = yHovered ? 3f : 2f;
            DrawAxisLine(originScreen, yEnd, yColor, yWidth);
            DrawAxisHandle(yEnd, yColor, yHovered, "Y");

            // Z軸
            Vector2 zEnd = GetAxisScreenEnd(ctx, GetLocalAxisDirection(AxisType.Z, worldMatrix), originScreen, pivotWorld, AxisType.Z);
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
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 10 };
            labelStyle.normal.textColor = centerColor;
            labelStyle.fontStyle = centerHovered ? FontStyle.Bold : FontStyle.Normal;
            GUI.Label(new Rect(originScreen.x + currentCenterSize / 2 + 4, originScreen.y - 8, 50, 16),
                T("Pivot"), labelStyle);  // ← 変更
        }
    

        private Vector2 GetAxisScreenEnd(ToolContext ctx, Vector3 axisDirection, Vector2 originScreen, Vector3 pivotWorld, AxisType axisType = AxisType.None)
        {
            Vector3 axisEnd = pivotWorld + axisDirection * 0.1f;
            Vector2 axisEndScreen = ctx.WorldToScreenPos(axisEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            Vector2 dir = (axisEndScreen - originScreen).normalized;

            if (dir.sqrMagnitude < 0.001f)
            {
                // 軸がカメラを向いている場合のフォールバック
                if (axisType == AxisType.X) dir = new Vector2(1, 0);
                else if (axisType == AxisType.Y) dir = new Vector2(0, -1);
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

            GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 10 };
            style.normal.textColor = color;
            style.fontStyle = hovered ? FontStyle.Bold : FontStyle.Normal;
            GUI.Label(new Rect(pos.x + size / 2 + 2, pos.y - 8, 20, 16), label, style);
        }

        private AxisType FindAxisHandleAtScreenPos(Vector2 screenPos, ToolContext ctx)
        {
            var worldMatrix = ctx.FirstSelectedMeshContext?.WorldMatrix ?? Matrix4x4.identity;
            Vector3 pivotWorld = worldMatrix.GetColumn(3);
            Vector2 originScreen = ctx.WorldToScreenPos(pivotWorld, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            // 中央四角のヒットテスト（優先）
            float halfCenter = _centerSize / 2 + 2;
            if (Mathf.Abs(screenPos.x - originScreen.x) < halfCenter &&
                Mathf.Abs(screenPos.y - originScreen.y) < halfCenter)
            {
                return AxisType.Center;
            }

            // X軸先端
            Vector2 xEnd = GetAxisScreenEnd(ctx, GetLocalAxisDirection(AxisType.X, worldMatrix), originScreen, pivotWorld, AxisType.X);
            if (Vector2.Distance(screenPos, xEnd) < _handleHitRadius)
                return AxisType.X;

            // Y軸先端
            Vector2 yEnd = GetAxisScreenEnd(ctx, GetLocalAxisDirection(AxisType.Y, worldMatrix), originScreen, pivotWorld, AxisType.Y);
            if (Vector2.Distance(screenPos, yEnd) < _handleHitRadius)
                return AxisType.Y;

            // Z軸先端
            Vector2 zEnd = GetAxisScreenEnd(ctx, GetLocalAxisDirection(AxisType.Z, worldMatrix), originScreen, pivotWorld, AxisType.Z);
            if (Vector2.Distance(screenPos, zEnd) < _handleHitRadius)
                return AxisType.Z;

            return AxisType.None;
        }

        private Vector3 GetLocalAxisDirection(AxisType axis, Matrix4x4 worldMatrix)
        {
            switch (axis)
            {
                case AxisType.X: return ((Vector3)worldMatrix.GetColumn(0)).normalized;
                case AxisType.Y: return ((Vector3)worldMatrix.GetColumn(1)).normalized;
                case AxisType.Z: return ((Vector3)worldMatrix.GetColumn(2)).normalized;
                default: return Vector3.zero;
            }
        }

        // ローカル空間軸（頂点移動用）
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
