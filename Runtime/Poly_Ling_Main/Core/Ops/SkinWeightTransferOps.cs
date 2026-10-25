// SkinWeightTransferOps.cs
// 写し元メッシュの最も近い面の点から、写し先メッシュの頂点へボーンウェイトを写す。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【前提】写し元・写し先ともスキンド（頂点がワールド座標）。呼び出し側で弾く。
// 【やり方】
//   写し先の頂点ごとに、写し元の全面（多角形は扇で三角形に割る）から最も近い点を探す。
//   その三角形の 3 頂点のウェイトを重心座標で混ぜ、ボーンごとに足し合わせ、
//   多い順に 4 本へ絞って正規化する。ボーン番号は model の masterIndex。
//   ウェイトの無い頂点（BoneWeight が null）は 0 本として扱う。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class SkinWeightTransferOps
    {
        /// <summary>写し先 target の全頂点へ写す。戻り値は書き換えた頂点数。</summary>
        public static int CopyFromNearest(MeshObject source, MeshObject target)
        {
            if (source == null || target == null || source == target) return 0;

            // ── 写し元の三角形 ──
            var tris = new List<int>();
            for (int f = 0; f < source.FaceCount; f++)
            {
                var vs = source.Faces[f]?.VertexIndices;
                if (vs == null || vs.Count < 3) continue;
                for (int k = 1; k + 1 < vs.Count; k++)
                {
                    tris.Add(vs[0]); tris.Add(vs[k]); tris.Add(vs[k + 1]);
                }
            }
            if (tris.Count == 0) return 0;

            int count = 0;
            var acc = new Dictionary<int, float>();

            for (int vi = 0; vi < target.VertexCount; vi++)
            {
                Vector3 p = target.Vertices[vi].Position;

                float best = float.MaxValue;
                int bt = -1;
                Vector3 bBary = Vector3.zero;

                for (int t = 0; t < tris.Count; t += 3)
                {
                    Vector3 a = source.Vertices[tris[t]].Position;
                    Vector3 b = source.Vertices[tris[t + 1]].Position;
                    Vector3 c = source.Vertices[tris[t + 2]].Position;

                    Vector3 q = ClosestPointOnTriangle(p, a, b, c, out Vector3 bary);
                    float d = (q - p).sqrMagnitude;
                    if (d < best) { best = d; bt = t; bBary = bary; }
                }
                if (bt < 0) continue;

                acc.Clear();
                Accumulate(acc, source.Vertices[tris[bt]].BoneWeight,     bBary.x);
                Accumulate(acc, source.Vertices[tris[bt + 1]].BoneWeight, bBary.y);
                Accumulate(acc, source.Vertices[tris[bt + 2]].BoneWeight, bBary.z);
                if (acc.Count == 0) continue;

                var list = new List<KeyValuePair<int, float>>(acc);
                list.Sort((x, y) => y.Value.CompareTo(x.Value));

                var bw = new BoneWeight();
                float total = 0f;
                for (int i = 0; i < 4 && i < list.Count; i++) total += list[i].Value;
                if (total <= 1e-6f) continue;

                for (int i = 0; i < 4; i++)
                {
                    int   idx = i < list.Count ? list[i].Key : 0;
                    float w   = i < list.Count ? list[i].Value / total : 0f;
                    switch (i)
                    {
                        case 0: bw.boneIndex0 = idx; bw.weight0 = w; break;
                        case 1: bw.boneIndex1 = idx; bw.weight1 = w; break;
                        case 2: bw.boneIndex2 = idx; bw.weight2 = w; break;
                        case 3: bw.boneIndex3 = idx; bw.weight3 = w; break;
                    }
                }

                target.Vertices[vi].BoneWeight = bw;
                count++;
            }

            if (count > 0) target.RecomputeSkinKind();
            return count;
        }

        private static void Accumulate(Dictionary<int, float> acc, BoneWeight? bwN, float k)
        {
            if (!bwN.HasValue || k <= 0f) return;
            var bw = bwN.Value;
            Add(acc, bw.boneIndex0, bw.weight0 * k);
            Add(acc, bw.boneIndex1, bw.weight1 * k);
            Add(acc, bw.boneIndex2, bw.weight2 * k);
            Add(acc, bw.boneIndex3, bw.weight3 * k);
        }

        private static void Add(Dictionary<int, float> acc, int bone, float w)
        {
            if (w <= 0f || bone < 0) return;
            acc.TryGetValue(bone, out float cur);
            acc[bone] = cur + w;
        }

        /// <summary>三角形 abc 上で p に最も近い点と、その重心座標 (a,b,c の割合)。</summary>
        private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out Vector3 bary)
        {
            // Ericson, Real-Time Collision Detection 5.1.5
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) { bary = new Vector3(1, 0, 0); return a; }

            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) { bary = new Vector3(0, 1, 0); return b; }

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / (d1 - d3);
                bary = new Vector3(1 - v, v, 0);
                return a + v * ab;
            }

            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) { bary = new Vector3(0, 0, 1); return c; }

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / (d2 - d6);
                bary = new Vector3(1 - w, 0, w);
                return a + w * ac;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                bary = new Vector3(0, 1 - w, w);
                return b + w * (c - b);
            }

            float denom = 1f / (va + vb + vc);
            float vv = vb * denom, ww = vc * denom;
            bary = new Vector3(1 - vv - ww, vv, ww);
            return a + ab * vv + ac * ww;
        }
    }
}
