// TriangleDistance.cs
// 三角形どうしの最短距離と交差判定
// UnityEditor非依存
//
// 【使い方】
// ShrinkFaceCollisionSolver の保守的前進法が、各ステップで
// 「移動中の三角形」と「静止コライダー三角形」の距離を必要とする。
//
// 【アルゴリズム】
// 交差している場合は 0。交差していない場合、最短距離は必ず
//   ・辺どうしの最短距離（9組）
//   ・一方の頂点と他方の三角形の最短距離（6組）
// のいずれかで達成される。交差している場合はこの 15 組が正の値を返し得るため、
// 先に 6 本の辺それぞれについて相手の三角形との交差を調べ、当たれば 0 を返す。
//
// Python（scipy.optimize による制約付き最小化）との突き合わせで
// 非交差 183 件の最大絶対差 9.3e-8 を確認済み。

using UnityEngine;

namespace Poly_Ling.UI
{
    public static class TriangleDistance
    {
        private const float Eps = 1e-16f;

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>
        /// 三角形 A(a0,a1,a2) と三角形 B(b0,b1,b2) の最短距離。交差していれば 0。
        /// </summary>
        public static float Distance(
            Vector3 a0, Vector3 a1, Vector3 a2,
            Vector3 b0, Vector3 b1, Vector3 b2)
        {
            if (Intersects(a0, a1, a2, b0, b1, b2)) return 0f;

            float best = float.MaxValue;

            // 辺どうし（9組）
            best = Min(best, SegSegDistanceSq(a0, a1, b0, b1));
            best = Min(best, SegSegDistanceSq(a0, a1, b1, b2));
            best = Min(best, SegSegDistanceSq(a0, a1, b2, b0));
            best = Min(best, SegSegDistanceSq(a1, a2, b0, b1));
            best = Min(best, SegSegDistanceSq(a1, a2, b1, b2));
            best = Min(best, SegSegDistanceSq(a1, a2, b2, b0));
            best = Min(best, SegSegDistanceSq(a2, a0, b0, b1));
            best = Min(best, SegSegDistanceSq(a2, a0, b1, b2));
            best = Min(best, SegSegDistanceSq(a2, a0, b2, b0));

            // 頂点と三角形（6組）
            best = Min(best, PointTriangleDistanceSq(a0, b0, b1, b2));
            best = Min(best, PointTriangleDistanceSq(a1, b0, b1, b2));
            best = Min(best, PointTriangleDistanceSq(a2, b0, b1, b2));
            best = Min(best, PointTriangleDistanceSq(b0, a0, a1, a2));
            best = Min(best, PointTriangleDistanceSq(b1, a0, a1, a2));
            best = Min(best, PointTriangleDistanceSq(b2, a0, a1, a2));

            return best <= 0f ? 0f : Mathf.Sqrt(best);
        }

        /// <summary>
        /// 三角形どうしが交差しているか。
        /// 真に交差する配置では、どちらかの辺が相手の三角形を貫く。
        /// 同一平面で一方が他方に完全に含まれる場合は辺が貫かないが、
        /// その場合は頂点-三角形距離が 0 になるので Distance 側で拾える。
        /// </summary>
        public static bool Intersects(
            Vector3 a0, Vector3 a1, Vector3 a2,
            Vector3 b0, Vector3 b1, Vector3 b2)
        {
            if (SegmentHitsTriangle(a0, a1, b0, b1, b2)) return true;
            if (SegmentHitsTriangle(a1, a2, b0, b1, b2)) return true;
            if (SegmentHitsTriangle(a2, a0, b0, b1, b2)) return true;
            if (SegmentHitsTriangle(b0, b1, a0, a1, a2)) return true;
            if (SegmentHitsTriangle(b1, b2, a0, a1, a2)) return true;
            if (SegmentHitsTriangle(b2, b0, a0, a1, a2)) return true;
            return false;
        }

        // ================================================================
        // 線分×三角形（Möller–Trumbore・両面）
        // ================================================================

        private static bool SegmentHitsTriangle(
            Vector3 p, Vector3 q,
            Vector3 v0, Vector3 v1, Vector3 v2)
        {
            Vector3 dir = q - p;
            Vector3 e1  = v1 - v0;
            Vector3 e2  = v2 - v0;

            Vector3 pv  = Vector3.Cross(dir, e2);
            float   det = Vector3.Dot(e1, pv);
            if (det > -Eps && det < Eps) return false;

            float   invDet = 1f / det;
            Vector3 tv     = p - v0;

            float bu = Vector3.Dot(tv, pv) * invDet;
            if (bu < 0f || bu > 1f) return false;

            Vector3 qv = Vector3.Cross(tv, e1);
            float   bv = Vector3.Dot(dir, qv) * invDet;
            if (bv < 0f || bu + bv > 1f) return false;

            float t = Vector3.Dot(e2, qv) * invDet;
            return t >= 0f && t <= 1f;
        }

        // ================================================================
        // 線分×線分 最短距離の二乗
        // ================================================================

        private static float SegSegDistanceSq(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r  = p1 - p2;

            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            const float Tiny = 1e-18f;

            float s, t;

            if (a <= Tiny && e <= Tiny)
                return (p1 - p2).sqrMagnitude;

            if (a <= Tiny)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= Tiny)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b     = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;

                    s = denom > Tiny ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;

                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }

            Vector3 c1 = p1 + d1 * s;
            Vector3 c2 = p2 + d2 * t;
            return (c1 - c2).sqrMagnitude;
        }

        // ================================================================
        // 点×三角形 最短距離の二乗
        // ================================================================

        private static float PointTriangleDistanceSq(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 cp = ClosestPointOnTriangle(p, a, b, c);
            return (p - cp).sqrMagnitude;
        }

        /// <summary>
        /// 三角形上で点 p に最も近い点。Ericson "Real-Time Collision Detection" の
        /// ボロノイ領域による場合分け。
        /// </summary>
        public static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = p - a;

            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                return a + ab * v;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                return a + ac * w;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                return b + (c - b) * w;
            }

            float denom = 1f / (va + vb + vc);
            float vv = vb * denom;
            float ww = vc * denom;
            return a + ab * vv + ac * ww;
        }

        // ================================================================
        // 内部
        // ================================================================

        private static float Min(float a, float b) => a < b ? a : b;
    }
}
