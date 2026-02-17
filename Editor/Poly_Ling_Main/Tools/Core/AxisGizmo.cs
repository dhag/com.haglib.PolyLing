// Assets/Editor/Poly_Ling_Main/Tools/Core/AxisGizmo.cs
// 軸ギズモ共有クラス
// MoveTool（頂点移動）とBoneInput（ボーン移動）で共通使用
// 描画、ヒットテスト、移動量計算を提供

using UnityEngine;
using UnityEditor;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    public class AxisGizmo
    {
        // ================================================================
        // 軸タイプ
        // ================================================================

        public enum AxisType { None, X, Y, Z, Center }

        // ================================================================
        // 設定
        // ================================================================

        public Vector2 ScreenOffset { get; set; } = new Vector2(60, -60);
        public float HandleHitRadius { get; set; } = 10f;
        public float HandleSize { get; set; } = 8f;
        public float CenterSize { get; set; } = 14f;
        public float ScreenAxisLength { get; set; } = 50f;

        // ================================================================
        // 状態（描画色制御用。呼び出し元が設定する）
        // ================================================================

        public AxisType HoveredAxis { get; set; } = AxisType.None;
        public AxisType DraggingAxis { get; set; } = AxisType.None;

        /// <summary>ギズモ中心のワールド座標</summary>
        public Vector3 Center { get; set; }

        // ================================================================
        // 描画（Repaintイベント中に呼び出す）
        // ================================================================

        public void Draw(ToolContext ctx)
        {
            Vector2 originScreen = GetOriginScreen(ctx);

            // 軸色
            Color xColor = (DraggingAxis == AxisType.X || HoveredAxis == AxisType.X)
                ? new Color(1f, 0.3f, 0.3f, 1f)
                : new Color(0.8f, 0.2f, 0.2f, 0.7f);

            Color yColor = (DraggingAxis == AxisType.Y || HoveredAxis == AxisType.Y)
                ? new Color(0.3f, 1f, 0.3f, 1f)
                : new Color(0.2f, 0.8f, 0.2f, 0.7f);

            Color zColor = (DraggingAxis == AxisType.Z || HoveredAxis == AxisType.Z)
                ? new Color(0.3f, 0.3f, 1f, 1f)
                : new Color(0.2f, 0.2f, 0.8f, 0.7f);

            Vector2 xEnd = GetAxisScreenEnd(ctx, Vector3.right, originScreen);
            Vector2 yEnd = GetAxisScreenEnd(ctx, Vector3.up, originScreen);
            Vector2 zEnd = GetAxisScreenEnd(ctx, Vector3.forward, originScreen);

            // 軸線
            float lineWidth = 2f;
            DrawAxisLine(originScreen, xEnd, xColor, lineWidth);
            DrawAxisLine(originScreen, yEnd, yColor, lineWidth);
            DrawAxisLine(originScreen, zEnd, zColor, lineWidth);

            // 軸先端ハンドル
            DrawAxisHandle(xEnd, xColor, HoveredAxis == AxisType.X, "X");
            DrawAxisHandle(yEnd, yColor, HoveredAxis == AxisType.Y, "Y");
            DrawAxisHandle(zEnd, zColor, HoveredAxis == AxisType.Z, "Z");

            // 中央四角
            bool centerHovered = (HoveredAxis == AxisType.Center);
            Color centerColor = centerHovered
                ? new Color(1f, 1f, 1f, 0.9f)
                : new Color(0.8f, 0.8f, 0.8f, 0.6f);

            float halfCenter = CenterSize / 2;
            Rect centerRect = new Rect(
                originScreen.x - halfCenter,
                originScreen.y - halfCenter,
                CenterSize,
                CenterSize);

            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.DrawRect(centerRect, centerColor);
            UnityEditor_Handles.color = centerHovered ? Color.white : new Color(0.5f, 0.5f, 0.5f);
            UnityEditor_Handles.DrawSolidRectangleWithOutline(centerRect, Color.clear, UnityEditor_Handles.color);
            UnityEditor_Handles.EndGUI();
        }

        // ================================================================
        // ヒットテスト（MouseDownイベント中に呼び出す）
        // ================================================================

        public AxisType FindAxisAtScreenPos(Vector2 screenPos, ToolContext ctx)
        {
            Vector2 originScreen = GetOriginScreen(ctx);
            Vector2 xEnd = GetAxisScreenEnd(ctx, Vector3.right, originScreen);
            Vector2 yEnd = GetAxisScreenEnd(ctx, Vector3.up, originScreen);
            Vector2 zEnd = GetAxisScreenEnd(ctx, Vector3.forward, originScreen);

            // 中央四角（優先）
            float halfCenter = CenterSize / 2 + 2;
            if (Mathf.Abs(screenPos.x - originScreen.x) < halfCenter &&
                Mathf.Abs(screenPos.y - originScreen.y) < halfCenter)
            {
                return AxisType.Center;
            }

            if (Vector2.Distance(screenPos, xEnd) < HandleHitRadius)
                return AxisType.X;
            if (Vector2.Distance(screenPos, yEnd) < HandleHitRadius)
                return AxisType.Y;
            if (Vector2.Distance(screenPos, zEnd) < HandleHitRadius)
                return AxisType.Z;

            return AxisType.None;
        }

        // ================================================================
        // 移動量計算
        // ================================================================

        /// <summary>
        /// 軸拘束ドラッグ時のフレーム移動量を計算（ワールド座標系）。
        /// DisplayMatrix逆変換適用済み。
        /// </summary>
        public Vector3 ComputeAxisDelta(Vector2 screenDelta, AxisType axis, ToolContext ctx)
        {
            if (screenDelta.sqrMagnitude < 0.001f || axis == AxisType.None || axis == AxisType.Center)
                return Vector3.zero;

            Vector3 axisDir = GetAxisDirection(axis);
            Vector3 screenDir3 = GetAxisScreenDirection(ctx, axisDir);
            Vector2 axisScreenDir2D = new Vector2(screenDir3.x, screenDir3.y);

            if (axisScreenDir2D.sqrMagnitude < 0.001f)
                return Vector3.zero;

            axisScreenDir2D.Normalize();
            float axisScreenMovement = Vector2.Dot(screenDelta, axisScreenDir2D);
            float worldScale = ctx.CameraDistance * 0.001f;
            Vector3 worldDelta = axisDir * axisScreenMovement * worldScale;

            if (ctx.DisplayMatrix != Matrix4x4.identity)
                worldDelta = ctx.DisplayMatrix.inverse.MultiplyVector(worldDelta);

            return worldDelta;
        }

        /// <summary>
        /// 自由移動（中央ドラッグ）のフレーム移動量を計算（ワールド座標系）。
        /// DisplayMatrix逆変換適用済み。
        /// </summary>
        public Vector3 ComputeFreeDelta(Vector2 screenDelta, ToolContext ctx)
        {
            Vector3 worldDelta = ctx.ScreenDeltaToWorldDelta(
                screenDelta, ctx.CameraPosition, ctx.CameraTarget,
                ctx.CameraDistance, ctx.PreviewRect);

            if (ctx.DisplayMatrix != Matrix4x4.identity)
                worldDelta = ctx.DisplayMatrix.inverse.MultiplyVector(worldDelta);

            return worldDelta;
        }

        // ================================================================
        // 静的ユーティリティ
        // ================================================================

        public static Vector3 GetAxisDirection(AxisType axis)
        {
            switch (axis)
            {
                case AxisType.X: return Vector3.right;
                case AxisType.Y: return Vector3.up;
                case AxisType.Z: return Vector3.forward;
                default: return Vector3.zero;
            }
        }

        // ================================================================
        // 内部メソッド
        // ================================================================

        private Vector2 GetOriginScreen(ToolContext ctx)
        {
            Vector2 centerScreen = ctx.WorldToScreenPos(
                Center, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            return centerScreen + ScreenOffset;
        }

        private Vector3 GetAxisScreenDirection(ToolContext ctx, Vector3 worldAxis)
        {
            float scale = Mathf.Max(0.1f, ctx.CameraDistance * 0.1f);
            Vector3 axisEnd = Center + worldAxis * scale;

            Vector2 centerScreen = ctx.WorldToScreenPos(
                Center, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 axisEndScreen = ctx.WorldToScreenPos(
                axisEnd, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            Vector2 diff = axisEndScreen - centerScreen;
            if (diff.magnitude < 0.001f)
                return Vector3.zero;

            Vector2 screenDir = diff.normalized;
            return new Vector3(screenDir.x, screenDir.y, 0);
        }

        private Vector2 GetAxisScreenEnd(ToolContext ctx, Vector3 worldAxis, Vector2 originScreen)
        {
            Vector3 screenDir = GetAxisScreenDirection(ctx, worldAxis);
            return originScreen + new Vector2(screenDir.x, screenDir.y) * ScreenAxisLength;
        }

        // ================================================================
        // 描画ヘルパー
        // ================================================================

        private static void DrawAxisLine(Vector2 from, Vector2 to, Color color, float lineWidth)
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
            float size = hovered ? HandleSize * 1.3f : HandleSize;
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
    }
}
