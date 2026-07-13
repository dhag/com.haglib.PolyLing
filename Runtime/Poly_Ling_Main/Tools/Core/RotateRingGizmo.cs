// RotateRingGizmo.cs
// 回転ツール用の簡易リングギズモ。ピボット中心に X/Y/Z 3軸のリング（円）を
// スクリーン投影し、ドラッグでその軸まわりに回転させる。
// 座標系は ctx.WorldToScreenPos が返す系（AxisGizmo と同一）。
// Runtime/Poly_Ling_Main/Tools/Core/ に配置

using UnityEngine;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    /// <summary>回転リングギズモ（X/Y/Z の3リング）。</summary>
    public class RotateRingGizmo
    {
        public Vector3 Center;
        public float RadiusFactor = 0.1f;   // ワールド半径 = CameraDistance * RadiusFactor
        public int    Segments    = 48;
        public float  HitThreshold = 8f;    // スクリーン距離（px）

        public AxisGizmo.AxisType HoveredAxis  = AxisGizmo.AxisType.None;
        public AxisGizmo.AxisType DraggingAxis = AxisGizmo.AxisType.None;

        /// <summary>指定軸リングのスクリーン点列を返す（閉ループ、末尾=先頭）。</summary>
        public Vector2[] GetRingScreen(ToolContext ctx, AxisGizmo.AxisType axis)
        {
            var pts = new Vector2[Segments + 1];
            if (ctx == null || ctx.WorldToScreenPos == null) return pts;

            float r = Mathf.Max(0.001f, ctx.CameraDistance * RadiusFactor);
            GetPlaneBasis(axis, out Vector3 u, out Vector3 v);

            for (int i = 0; i < Segments; i++)
            {
                float a = (2f * Mathf.PI * i) / Segments;
                Vector3 world = Center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r;
                pts[i] = ctx.WorldToScreenPos(world, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            }
            pts[Segments] = pts[0];
            return pts;
        }

        /// <summary>スクリーン座標（ctx系）に最も近いリングの軸を返す。</summary>
        public AxisGizmo.AxisType FindRingAtScreenPos(Vector2 screenPos, ToolContext ctx)
        {
            var best = AxisGizmo.AxisType.None;
            float bestDist = HitThreshold;
            foreach (var axis in new[] { AxisGizmo.AxisType.X, AxisGizmo.AxisType.Y, AxisGizmo.AxisType.Z })
            {
                var pts = GetRingScreen(ctx, axis);
                float d = MinDistToPolyline(screenPos, pts);
                if (d < bestDist) { bestDist = d; best = axis; }
            }
            return best;
        }

        public static Vector3 AxisVector(AxisGizmo.AxisType axis)
        {
            switch (axis)
            {
                case AxisGizmo.AxisType.X: return Vector3.right;
                case AxisGizmo.AxisType.Y: return Vector3.up;
                case AxisGizmo.AxisType.Z: return Vector3.forward;
                default: return Vector3.up;
            }
        }

        private static void GetPlaneBasis(AxisGizmo.AxisType axis, out Vector3 u, out Vector3 v)
        {
            // 軸に垂直な平面の基底
            switch (axis)
            {
                case AxisGizmo.AxisType.X: u = Vector3.up;      v = Vector3.forward; break; // YZ 平面
                case AxisGizmo.AxisType.Y: u = Vector3.forward; v = Vector3.right;   break; // ZX 平面
                default:                   u = Vector3.right;   v = Vector3.up;       break; // XY 平面（Z）
            }
        }

        private static float MinDistToPolyline(Vector2 p, Vector2[] pts)
        {
            float min = float.MaxValue;
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                float d = DistToSegment(p, pts[i], pts[i + 1]);
                if (d < min) min = d;
            }
            return min;
        }

        private static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
