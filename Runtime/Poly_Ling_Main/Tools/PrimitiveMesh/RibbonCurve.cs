// RibbonCurve.cs
// リボンの中心曲線（3次ベジエの連結）とサンプリング。Runtime / Editor 共有。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【役割】形状パラメータには関与しない。制御点を受け取り、
//   弧長でほぼ等間隔になる位置と接線の列を返すだけ。
//
// 【等間隔化】各セグメントを DenseSteps 個に細分した折れ線で弧長を積み、
//   目標弧長に当たる区間で媒介変数を線形補間してから曲線上を評価する。
//   折れ線長は真の弧長より短めに出るが、分割位置の偏りを均す用途には足りる。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Ribbon
{
    /// <summary>3次ベジエ曲線1本。</summary>
    public readonly struct RibbonBezier
    {
        public readonly Vector3 P0, P1, P2, P3;

        public RibbonBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
        {
            P0 = p0; P1 = p1; P2 = p2; P3 = p3;
        }

        /// <summary>曲線上の点。</summary>
        public Vector3 Point(float t)
        {
            float u = 1f - t;
            return u * u * u * P0
                 + 3f * u * u * t * P1
                 + 3f * u * t * t * P2
                 + t * t * t * P3;
        }

        /// <summary>始点と終点を入れ替えた同一形状の曲線。</summary>
        public RibbonBezier Reversed() => new RibbonBezier(P3, P2, P1, P0);

        /// <summary>接線（微分）。長さが 0 に近い場合は差分で代用する。</summary>
        public Vector3 Tangent(float t)
        {
            float u = 1f - t;
            Vector3 d = 3f * u * u * (P1 - P0)
                      + 6f * u * t * (P2 - P1)
                      + 3f * t * t * (P3 - P2);

            if (d.sqrMagnitude > 1e-12f) return d;

            // 制御点が重なって微分が消える位置では前後差分を使う。
            float t0 = Mathf.Max(0f, t - 1e-3f);
            float t1 = Mathf.Min(1f, t + 1e-3f);
            return Point(t1) - Point(t0);
        }
    }

    public static class RibbonCurveSampler
    {
        /// <summary>弧長計測用に1セグメントを細分する数。</summary>
        private const int DenseSteps = 32;

        /// <summary>
        /// 連結ベジエを弧長ほぼ等間隔で (segments + 1) 点サンプリングする。
        /// 出力の positions / tangents は同じ長さになる。tangents は正規化済み。
        /// </summary>
        public static void Sample(
            IReadOnlyList<RibbonBezier> segs, int segments,
            List<Vector3> positions, List<Vector3> tangents)
        {
            positions.Clear();
            tangents.Clear();

            if (segs == null || segs.Count == 0) return;
            if (segments < 1) segments = 1;

            // ── 細分点の弧長テーブル ──
            int    denseCount = segs.Count * DenseSteps + 1;
            var    lengths    = new float[denseCount];
            var    segIndex   = new int[denseCount];
            var    localT     = new float[denseCount];

            Vector3 prev = segs[0].Point(0f);
            lengths[0]  = 0f;
            segIndex[0] = 0;
            localT[0]   = 0f;

            int k = 1;
            for (int s = 0; s < segs.Count; s++)
            {
                for (int i = 1; i <= DenseSteps; i++)
                {
                    float t = i / (float)DenseSteps;
                    Vector3 cur = segs[s].Point(t);

                    lengths[k]  = lengths[k - 1] + Vector3.Distance(prev, cur);
                    segIndex[k] = s;
                    localT[k]   = t;

                    prev = cur;
                    k++;
                }
            }

            float total = lengths[denseCount - 1];

            // ── 目標弧長ごとに評価 ──
            int cursor = 0;
            for (int i = 0; i <= segments; i++)
            {
                float target = (total <= 1e-9f)
                    ? 0f
                    : total * (i / (float)segments);

                while (cursor < denseCount - 2 && lengths[cursor + 1] < target) cursor++;

                int a = cursor;
                int b = Mathf.Min(cursor + 1, denseCount - 1);

                float span = lengths[b] - lengths[a];
                float f    = (span <= 1e-9f) ? 0f : (target - lengths[a]) / span;

                // 区間 a→b はセグメントをまたぐことがある。またぐ場合は b 側の媒介変数へ寄せる。
                int   si = segIndex[b];
                float ta = (segIndex[a] == si) ? localT[a] : 0f;
                float tt = Mathf.Clamp01(Mathf.Lerp(ta, localT[b], f));

                var seg = segs[si];
                positions.Add(seg.Point(tt));

                Vector3 tan = seg.Tangent(tt);
                tangents.Add(tan.sqrMagnitude > 1e-12f ? tan.normalized : Vector3.right);
            }
        }
    }
}
