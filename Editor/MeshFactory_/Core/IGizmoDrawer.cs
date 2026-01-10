// Assets/Editor/MeshFactory/Core/Gizmo/IGizmoDrawer.cs
// ギズモ描画の抽象化インターフェース
// エディタ（Handles）とランタイム（GL）の両方に対応
// Phase 1: インターフェース定義

using UnityEngine;

namespace MeshFactory.Gizmo
{
    /// <summary>
    /// 2Dギズモ描画インターフェース
    /// スクリーン座標系での描画（UnityEditor_Handles.BeginGUI/EndGUI内相当）
    /// </summary>
    public interface IGizmoDrawer2D
    {
        // ================================================================
        // セッション管理
        // ================================================================

        /// <summary>
        /// 2D描画セッション開始
        /// Editor: UnityEditor_Handles.BeginGUI()
        /// Runtime: GL.PushMatrix() + 座標系設定
        /// </summary>
        void Begin();

        /// <summary>
        /// 2D描画セッション終了
        /// Editor: UnityEditor_Handles.EndGUI()
        /// Runtime: GL.PopMatrix()
        /// </summary>
        void End();

        // ================================================================
        // 色設定
        // ================================================================

        /// <summary>
        /// 描画色を設定
        /// </summary>
        Color Color { get; set; }

        // ================================================================
        // 線描画
        // ================================================================

        /// <summary>
        /// 線を描画（アンチエイリアス付き）
        /// Editor: UnityEditor_Handles.DrawAAPolyLine
        /// </summary>
        /// <param name="from">開始点（スクリーン座標）</param>
        /// <param name="to">終了点（スクリーン座標）</param>
        /// <param name="width">線幅（ピクセル）</param>
        void DrawLine(Vector2 from, Vector2 to, float width = 1f);

        /// <summary>
        /// 連続線を描画（アンチエイリアス付き）
        /// Editor: UnityEditor_Handles.DrawAAPolyLine
        /// </summary>
        /// <param name="width">線幅（ピクセル）</param>
        /// <param name="points">点列（スクリーン座標）</param>
        void DrawPolyLine(float width, params Vector3[] points);

        /// <summary>
        /// 線を描画（シンプル版）
        /// Editor: UnityEditor_Handles.DrawLine
        /// </summary>
        void DrawLineSimple(Vector3 from, Vector3 to);

        // ================================================================
        // 円描画
        // ================================================================

        /// <summary>
        /// 塗りつぶし円を描画
        /// Editor: UnityEditor_Handles.DrawSolidDisc
        /// </summary>
        /// <param name="center">中心（スクリーン座標）</param>
        /// <param name="radius">半径（ピクセル）</param>
        void DrawSolidDisc(Vector2 center, float radius);

        /// <summary>
        /// ワイヤー円を描画
        /// </summary>
        /// <param name="center">中心（スクリーン座標）</param>
        /// <param name="radius">半径（ピクセル）</param>
        /// <param name="segments">分割数</param>
        void DrawWireCircle(Vector2 center, float radius, int segments = 32);

        // ================================================================
        // 矩形描画
        // ================================================================

        /// <summary>
        /// 塗りつぶし矩形を描画（枠線付き）
        /// Editor: UnityEditor_Handles.DrawSolidRectangleWithOutline
        /// </summary>
        /// <param name="rect">矩形（スクリーン座標）</param>
        /// <param name="fillColor">塗りつぶし色</param>
        /// <param name="outlineColor">枠線色</param>
        void DrawSolidRectWithOutline(Rect rect, Color fillColor, Color outlineColor);

        // ================================================================
        // 多角形描画
        // ================================================================

        /// <summary>
        /// 凸多角形を描画（アンチエイリアス付き）
        /// Editor: UnityEditor_Handles.DrawAAConvexPolygon
        /// </summary>
        /// <param name="points">頂点列（スクリーン座標）</param>
        void DrawConvexPolygon(params Vector3[] points);
    }

    /// <summary>
    /// 3Dギズモ描画インターフェース
    /// ワールド座標系での描画
    /// </summary>
    public interface IGizmoDrawer3D
    {
        // ================================================================
        // 色設定
        // ================================================================

        /// <summary>
        /// 描画色を設定
        /// </summary>
        Color Color { get; set; }

        // ================================================================
        // 線描画
        // ================================================================

        /// <summary>
        /// 3D線を描画
        /// Editor: UnityEditor_Handles.DrawLine
        /// </summary>
        /// <param name="from">開始点（ワールド座標）</param>
        /// <param name="to">終了点（ワールド座標）</param>
        void DrawLine(Vector3 from, Vector3 to);

        /// <summary>
        /// 3D線を描画（太さ指定）
        /// Editor: UnityEditor_Handles.DrawLine with thickness
        /// </summary>
        void DrawLine(Vector3 from, Vector3 to, float thickness);

        // ================================================================
        // 円/ディスク描画
        // ================================================================

        /// <summary>
        /// ワイヤーディスクを描画
        /// Editor: UnityEditor_Handles.DrawWireDisc
        /// </summary>
        /// <param name="center">中心（ワールド座標）</param>
        /// <param name="normal">法線方向</param>
        /// <param name="radius">半径</param>
        void DrawWireDisc(Vector3 center, Vector3 normal, float radius);

        /// <summary>
        /// 塗りつぶしディスクを描画
        /// Editor: UnityEditor_Handles.DrawSolidDisc
        /// </summary>
        void DrawSolidDisc(Vector3 center, Vector3 normal, float radius);
    }

    /// <summary>
    /// 統合ギズモ描画インターフェース
    /// 2Dと3D両方を提供
    /// </summary>
    public interface IGizmoDrawer : IGizmoDrawer2D, IGizmoDrawer3D
    {
        /// <summary>
        /// 2D描画モードかどうか
        /// </summary>
        bool Is2DMode { get; }
    }

    /// <summary>
    /// ギズモ描画のヘルパー拡張メソッド
    /// </summary>
    public static class GizmoDrawerExtensions
    {
        /// <summary>
        /// 矢印を描画
        /// </summary>
        public static void DrawArrow(this IGizmoDrawer2D drawer, Vector2 from, Vector2 to, float headSize = 8f)
        {
            drawer.DrawLine(from, to, 2f);

            // 矢印の頭
            Vector2 dir = (to - from).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            Vector2 head1 = to - dir * headSize + perp * headSize * 0.5f;
            Vector2 head2 = to - dir * headSize - perp * headSize * 0.5f;

            drawer.DrawLine(to, head1, 2f);
            drawer.DrawLine(to, head2, 2f);
        }

        /// <summary>
        /// 十字マーカーを描画
        /// </summary>
        public static void DrawCross(this IGizmoDrawer2D drawer, Vector2 center, float size = 5f)
        {
            drawer.DrawLine(
                new Vector2(center.x - size, center.y),
                new Vector2(center.x + size, center.y), 1f);
            drawer.DrawLine(
                new Vector2(center.x, center.y - size),
                new Vector2(center.x, center.y + size), 1f);
        }

        /// <summary>
        /// XYZ軸を描画（3D）
        /// </summary>
        public static void DrawAxisGizmo(this IGizmoDrawer3D drawer, Vector3 origin, float size = 1f)
        {
            drawer.Color = Color.red;
            drawer.DrawLine(origin, origin + Vector3.right * size);

            drawer.Color = Color.green;
            drawer.DrawLine(origin, origin + Vector3.up * size);

            drawer.Color = Color.blue;
            drawer.DrawLine(origin, origin + Vector3.forward * size);
        }

        /// <summary>
        /// グリッドを描画（3D）
        /// </summary>
        public static void DrawGrid(
            this IGizmoDrawer3D drawer,
            Vector3 origin,
            Vector3 axisU,
            Vector3 axisV,
            float size,
            int divisions,
            Color color)
        {
            drawer.Color = color;
            float step = size / divisions;
            float halfSize = size / 2f;

            for (int i = 0; i <= divisions; i++)
            {
                float offset = -halfSize + i * step;

                // U方向の線
                Vector3 startU = origin + axisV * offset - axisU * halfSize;
                Vector3 endU = origin + axisV * offset + axisU * halfSize;
                drawer.DrawLine(startU, endU);

                // V方向の線
                Vector3 startV = origin + axisU * offset - axisV * halfSize;
                Vector3 endV = origin + axisU * offset + axisV * halfSize;
                drawer.DrawLine(startV, endV);
            }
        }
    }
}
