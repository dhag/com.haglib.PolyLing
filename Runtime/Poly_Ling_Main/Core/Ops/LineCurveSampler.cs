// LineCurveSampler.cs
// 線分群のハンドル（ベジェ）から曲線を点列に分割する。
// 表示（ビューポートの重ね描き）と生成（LineProfileExtractor の取り込み）で使う。
// メッシュには焼き込まない（群の点と弦の 2 頂点の面だけを持つ）。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【区間の形】点 i → 点 i+1 を 3 次ベジェ
//   P0 = pos[i], P1 = pos[i] + out[i], P2 = pos[i+1] + in[i+1], P3 = pos[i+1]
//   ハンドルが 0 なら直線と同じ。閉じた群は最後の点 → 最初の点も区間にする。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class LineCurveSampler
    {
        /// <summary>取り込み・表示で使う 1 区間あたりの既定の分割数。</summary>
        public const int DefaultSegmentsPerSpan = 8;

        /// <summary>
        /// 点の位置・入りのずれ・出のずれ（同じ座標系）から曲線の点列を作る。
        /// 開いた群は先頭の点から最後の点まで。閉じた群は最初の点を末尾に繰り返さない。
        /// </summary>
        public static List<Vector3> Sample(
            IReadOnlyList<Vector3> pos, IReadOnlyList<Vector3> inOff, IReadOnlyList<Vector3> outOff,
            bool closed, int segmentsPerSpan)
        {
            var result = new List<Vector3>();
            int n = pos?.Count ?? 0;
            if (n == 0) return result;
            int seg = Mathf.Max(1, segmentsPerSpan);
            int spans = closed ? n : n - 1;

            result.Add(pos[0]);
            for (int s = 0; s < spans; s++)
            {
                int a = s, b = (s + 1) % n;
                Vector3 p0 = pos[a];
                Vector3 p1 = pos[a] + outOff[a];
                Vector3 p2 = pos[b] + inOff[b];
                Vector3 p3 = pos[b];
                bool straight = outOff[a].sqrMagnitude < 1e-12f && inOff[b].sqrMagnitude < 1e-12f;
                int steps = straight ? 1 : seg;
                for (int k = 1; k <= steps; k++)
                {
                    if (closed && s == spans - 1 && k == steps) break;   // 最初の点は繰り返さない
                    result.Add(Bezier(p0, p1, p2, p3, (float)k / steps));
                }
            }
            return result;
        }

        /// <summary>メッシュのローカル座標で群の曲線を作る。ハンドルが無い群は点列そのもの。</summary>
        public static List<Vector3> SampleLocal(MeshObject mo, LineGroup g, int segmentsPerSpan)
        {
            var pos = new List<Vector3>();
            if (mo == null || g?.Order == null) return pos;
            foreach (int vi in g.Order)
                pos.Add((vi >= 0 && vi < mo.VertexCount) ? mo.Vertices[vi].Position : Vector3.zero);
            if (!g.HasHandles) return pos;

            var ins  = new List<Vector3>(pos.Count);
            var outs = new List<Vector3>(pos.Count);
            foreach (var h in g.PointHandles) { ins.Add(h?.InOffset ?? Vector3.zero); outs.Add(h?.OutOffset ?? Vector3.zero); }
            return Sample(pos, ins, outs, g.Closed, segmentsPerSpan);
        }

        public static Vector3 Bezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }
    }
}
