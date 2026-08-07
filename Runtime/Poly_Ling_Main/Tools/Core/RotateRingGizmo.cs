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

        /// <summary>
        /// リングの向き。既定 identity のときは従来どおりワールド軸まわりのリング。
        /// 作業用ローカル軸など任意フレームで回したい場合にここへ回転を設定する。
        /// 描画・ヒットテスト・角度ドラッグのすべてがこの向きに従う。
        /// </summary>
        public Quaternion Orientation = Quaternion.identity;

        /// <summary>Orientation を適用した軸方向（ワールド）。</summary>
        public Vector3 GetOrientedAxisVector(AxisGizmo.AxisType axis)
            => Orientation * AxisVector(axis);

        /// <summary>指定軸リングのスクリーン点列を返す（閉ループ、末尾=先頭）。</summary>
        public Vector2[] GetRingScreen(ToolContext ctx, AxisGizmo.AxisType axis)
        {
            var pts = new Vector2[Segments + 1];
            if (ctx == null || ctx.WorldToScreenPos == null) return pts;

            float r = Mathf.Max(0.001f, ctx.CameraDistance * RadiusFactor);
            GetPlaneBasis(axis, out Vector3 u, out Vector3 v);
            u = Orientation * u;
            v = Orientation * v;

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
            float dX = -1f, dY = -1f, dZ = -1f;
            foreach (var axis in new[] { AxisGizmo.AxisType.X, AxisGizmo.AxisType.Y, AxisGizmo.AxisType.Z })
            {
                var pts = GetRingScreen(ctx, axis);
                float d = MinDistToPolyline(screenPos, pts);
                if (axis == AxisGizmo.AxisType.X) dX = d;
                else if (axis == AxisGizmo.AxisType.Y) dY = d;
                else dZ = d;
                if (d < bestDist) { bestDist = d; best = axis; }
            }

            if (AxisGizmo.GizmoDebugLog)
            {
                Vector2 centerScreen = (ctx?.WorldToScreenPos != null)
                    ? ctx.WorldToScreenPos(Center, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget)
                    : Vector2.zero;
                Debug.Log(
                    $"[GizmoDbg/Ring] mouse={screenPos} centerScreen={centerScreen} " +
                    $"hit={best} d=({dX:F1},{dY:F1},{dZ:F1}) thr={HitThreshold} " +
                    $"center={Center} rect={ctx?.PreviewRect}");
            }

            return best;
        }

        // ================================================================
        // 角度ドラッグセッション（共有）
        //
        // 「ピボットのスクリーン座標を基準に開始角を記録し、以後カーソル角との
        // 差を軸符号付きの累計角（度）で返す」手順を、リングを使う全ツールで
        // 共有する。フレーム差分ではなく毎回「開始角からの絶対角」を返す。
        // 座標系は ctx.WorldToScreenPos が返す系（AxisGizmo と同一）。
        // Center は呼び出し前に設定しておくこと。
        // ================================================================

        private AxisGizmo.AxisType _angleDragAxis = AxisGizmo.AxisType.None;
        private Vector2 _angleDragPivotScreen;
        private float   _angleDragStartDeg;
        private float   _angleDragSign = 1f;

        /// <summary>角度ドラッグ中か。</summary>
        public bool IsAngleDragging => _angleDragAxis != AxisGizmo.AxisType.None;

        /// <summary>ドラッグ中の回転軸（ワールド。Orientation 適用済み）。非ドラッグ時は Orientation * up。</summary>
        public Vector3 AngleDragAxisVector => GetOrientedAxisVector(_angleDragAxis);

        /// <summary>
        /// 角度ドラッグを開始する。cursorScreen は ctx 系（WorldToScreenPos と同じ系）。
        /// </summary>
        public bool BeginAngleDrag(ToolContext ctx, Vector2 cursorScreen, AxisGizmo.AxisType axis)
        {
            _angleDragAxis = AxisGizmo.AxisType.None;
            if (ctx == null || ctx.WorldToScreenPos == null) return false;
            if (axis == AxisGizmo.AxisType.None) return false;

            _angleDragPivotScreen = ctx.WorldToScreenPos(
                Center, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            _angleDragStartDeg = ScreenAngleDeg(cursorScreen, _angleDragPivotScreen);

            // 軸がカメラ側を向くとき +1。裏から見たときに回転方向が反転しないようにする。
            // Orientation を適用した実際のリング法線で判定しないと、ローカル軸表示のとき
            // 符号が合わずドラッグ方向が反転する。
            Vector3 worldAxis = GetOrientedAxisVector(axis);
            Vector3 camDir    = (ctx.CameraPosition - Center).normalized;
            _angleDragSign    = Vector3.Dot(worldAxis, camDir) >= 0f ? 1f : -1f;

            _angleDragAxis = axis;
            return true;
        }

        /// <summary>開始角からの累計角（度）。BeginAngleDrag していなければ 0。</summary>
        public float ComputeAngleDeltaDeg(Vector2 cursorScreen)
        {
            if (_angleDragAxis == AxisGizmo.AxisType.None) return 0f;
            float cur = ScreenAngleDeg(cursorScreen, _angleDragPivotScreen);
            return Mathf.DeltaAngle(_angleDragStartDeg, cur) * _angleDragSign;
        }

        /// <summary>角度ドラッグを終了する。</summary>
        public void EndAngleDrag()
        {
            _angleDragAxis = AxisGizmo.AxisType.None;
        }

        /// <summary>ピボットスクリーン座標を基準としたカーソル角（度）。</summary>
        public static float ScreenAngleDeg(Vector2 cursor, Vector2 pivot)
            => Mathf.Atan2(cursor.y - pivot.y, cursor.x - pivot.x) * Mathf.Rad2Deg;

        /// <summary>
        /// ワールド軸方向。Orientation は反映しない。
        /// 向きを反映したいときはインスタンスメソッド GetOrientedAxisVector を使うこと。
        /// </summary>
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
                float d = AxisGizmo.DistanceToSegment(p, pts[i], pts[i + 1]);
                if (d < min) min = d;
            }
            return min;
        }

    }
}
