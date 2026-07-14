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
        private Vector2 _mouseDownScreenPos;

        // ドラッグ開始時の位置
        private Vector3[] _dragStartPositions;
        private BoneTransformSnapshot _dragStartBoneSnapshot;   // ← 追加
        private Vector3 _totalOffset = Vector3.zero;  // 表示用の総オフセット

        // ドラッグ開始時に固定する基準（絶対計算用）。毎フレーム再計算するとドリフトの原因になる。
        private Vector3 _dragStartBonePosition;  // 開始ピボット位置
        private Vector2 _dragAxisScreenDir;      // 軸のスクリーン方向（開始時固定）
        private Vector3 _dragAxisWorldDir;       // 軸のワールド方向（開始時固定）
        private Matrix4x4 _dragStartWorldMatrix = Matrix4x4.identity; // 開始WorldMatrix（見かけ保持のローカル変換用）

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
                    ctx.Repaint?.Invoke();
                    return true;

                case ToolState.CenterDragging:
                    MoveFreely(mousePos, ctx);
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

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        public void DrawGizmo(ToolContext ctx) { }
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
            _totalOffset = Vector3.zero;

            // 全頂点の開始位置を記録
            _dragStartPositions = (Vector3[])ctx.FirstSelectedMeshObject.Positions.Clone();

            // BoneTransform の開始状態を記録
            var mc = ctx.FirstSelectedMeshContext;
            if (mc?.BoneTransform != null)
            {
                _dragStartBoneSnapshot = mc.BoneTransform.CreateSnapshot();
                _dragStartBonePosition = mc.BoneTransform.Position;
            }
            // 見かけ保持のローカル変換に使う WorldMatrix を開始時点で固定する
            _dragStartWorldMatrix = mc?.WorldMatrix ?? Matrix4x4.identity;

            // 軸移動時は、軸のスクリーン方向とワールド方向を開始時点で固定する。
            // （毎フレーム再計算するとピボット移動で方向がぶれ、往復が相殺せずドリフトする）
            if (axis != AxisType.Center)
            {
                var worldMatrix = mc?.WorldMatrix ?? Matrix4x4.identity;
                _dragAxisWorldDir = GetLocalAxisDirection(axis, worldMatrix);
                Vector3 pivotWorld = worldMatrix.GetColumn(3);
                Vector2 originScreen = ctx.WorldToScreenPos(pivotWorld, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector3 axisEnd = pivotWorld + _dragAxisWorldDir * 0.1f;
                Vector2 axisEndScreen = ctx.WorldToScreenPos(axisEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 dir = (axisEndScreen - originScreen).normalized;
                if (dir.sqrMagnitude < 0.001f)
                {
                    // 軸がカメラを向いている場合のフォールバック
                    if (axis == AxisType.X) dir = new Vector2(1, 0);
                    else if (axis == AxisType.Y) dir = new Vector2(0, -1);
                    else dir = new Vector2(-0.7f, 0.7f);
                }
                _dragAxisScreenDir = dir;
            }

            _state = (axis == AxisType.Center) ? ToolState.CenterDragging : ToolState.AxisDragging;
        }

        private void MoveAlongAxis(Vector2 mousePos, ToolContext ctx)
        {
            if (_draggingAxis == AxisType.None || _draggingAxis == AxisType.Center)
                return;

            // 開始マウス位置からの総移動を、開始時に固定した軸スクリーン方向へ投影する（絶対計算）。
            Vector2 totalMouseDelta = mousePos - _mouseDownScreenPos;
            float screenMovement = Vector2.Dot(totalMouseDelta, _dragAxisScreenDir);
            float worldMovement = screenMovement * ctx.CameraDistance * 0.002f;

            // ピボット(ローカル原点)のワールド移動量。頂点補正は ApplyAbsolute でローカル変換する。
            Vector3 worldDelta = _dragAxisWorldDir * worldMovement;
            ApplyAbsolute(worldDelta, ctx);
        }

        private void MoveFreely(Vector2 mousePos, ToolContext ctx)
        {
            // 開始マウス位置からの総移動で絶対計算する。
            // mousePos は IMGUI 系(Y=0 上)。元実装は Y=0 下系の delta を使っていたため、
            // 同じ向きに揃えるよう Y を反転する。
            Vector2 totalMouseDelta = mousePos - _mouseDownScreenPos;
            totalMouseDelta.y = -totalMouseDelta.y;

            Vector3 worldDelta = ctx.ScreenDeltaToWorldDelta(
                totalMouseDelta, ctx.CameraPosition, ctx.CameraTarget,
                ctx.CameraDistance, ctx.PreviewRect);

            ApplyAbsolute(worldDelta, ctx);
        }

        private void ApplyAbsolute(Vector3 worldDelta, ToolContext ctx)
        {
            var mo = ctx.FirstSelectedMeshObject;
            if (mo == null || _dragStartPositions == null) return;

            // ピボット(ローカル原点)のワールド移動 worldDelta を、開始WorldMatrixの線形逆変換で
            // メッシュのローカル空間へ変換する。R·S·localDelta = worldDelta が成り立つため、
            // 頂点を -localDelta、ピボットを +worldDelta とすると見かけが厳密に相殺される
            // （回転・スケールがあっても保持される）。
            Vector3 localDelta = _dragStartWorldMatrix.inverse.MultiplyVector(worldDelta);

            int n = Mathf.Min(mo.VertexCount, _dragStartPositions.Length);
            for (int i = 0; i < n; i++)
            {
                var v = mo.Vertices[i];
                v.Position = _dragStartPositions[i] - localDelta; // 開始基準で絶対（見かけ保持のローカル逆補正）
                mo.Vertices[i] = v;

                // オフセット更新
                if (ctx.VertexOffsets != null && i < ctx.VertexOffsets.Length &&
                    ctx.OriginalPositions != null && i < ctx.OriginalPositions.Length)
                {
                    ctx.VertexOffsets[i] = mo.Vertices[i].Position - ctx.OriginalPositions[i];
                    if (ctx.GroupOffsets != null && i < ctx.GroupOffsets.Length)
                        ctx.GroupOffsets[i] = ctx.VertexOffsets[i];
                }
            }

            // ピボット(BoneTransform)はワールドで worldDelta 移動（親なし/identity 前提）。
            var mc = ctx.FirstSelectedMeshContext;
            if (mc?.BoneTransform != null)
            {
                mc.BoneTransform.UseLocalTransform = true;
                mc.BoneTransform.Position = _dragStartBonePosition + worldDelta;
            }

            _totalOffset = -localDelta; // 表示用（頂点側オフセット）

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

                ctx.UndoController.SetModelContext(ctx.Model);
                {
                    string __dbgDesc = $"Pivot Move ({axisName})";
                    UnityEngine.Debug.Log("[UndoDbg] MeshList.Record desc=" + __dbgDesc + " type=" + ((record)?.GetType().Name ?? "<null>"));
                    ctx.UndoController.MeshListStack.Record(record, __dbgDesc);
                }
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
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み// 中心点
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み

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
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
        }

        private void DrawAxisHandle(Vector2 pos, Color color, bool hovered, string label)
        {
            float size = hovered ? _handleSize * 1.3f : _handleSize;

            Rect handleRect = new Rect(pos.x - size / 2, pos.y - size / 2, size, size);

            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み
            // UnityEditor_Handles 削除済み

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

        /// <summary>
        /// ポインター移動時のホバー更新専用（ドラッグ中は何もしない）。
        /// PivotOffsetToolHandler.UpdateHover から呼ぶ。
        /// OnMouseDrag を呼ぶとドラッグ中に MoveFreely 等が実行されるため、この専用メソッドを使う。
        /// </summary>
        public void UpdateHoverOnly(ToolContext ctx, Vector2 mousePos)
        {
            _lastMousePos = mousePos;
            if (_state != ToolState.Idle) return;
            var newHovered = FindAxisHandleAtScreenPos(mousePos, ctx);
            if (newHovered != _hoveredAxis)
            {
                _hoveredAxis = newHovered;
                ctx.Repaint?.Invoke();
            }
        }
    }
}
