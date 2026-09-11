// FrillProfileSplit.cs
// 断面プロファイルを x 方向に等分して、梯子の連続するステップへ配る断片を作る。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【なぜ要るか】
//   フリルは rung 1区間ごとに断面プロファイルを1周期置く。
//   円筒を細かく割ると曲がりはなめらかになるが、プリーツの数も分割数ぶん増えてしまう。
//   逆に円筒を粗くするとプリーツは減るが、レールが折れ線になって直線的になる。
//   細かいまま数だけ減らすために、プロファイル1周期を span 本ぶんの梯子へ広げる。
//
// 【割り方】
//   切断値は x = d/span（d = 1..span-1）で固定する。プロファイル自身の x の範囲では割らない。
//   span = 1 のとき断片が元のプロファイルそのものになり、従来と同じ結果になるため。
//   x が切断値ちょうどの点があればその点を使い、無ければ前後の点を線形補間して作る。
//
// 【断片の x】
//   断片 d の点は x' = x*span - d へ写す。断片は必ず x'=0 で始まり x'=1 で終わるので、
//   隣り合うステップの rung 境界で切れ目なくつながる。
//
// 【A / B の対応】
//   2プロファイルでは A[k] と B[k] が対で補間される。切断位置を A の x から求め、
//   同じ (区間番号, 比率) を B にも使う。こうすると A と B の断片の点数が必ず一致する。
//   B の x が A と違っていてもよい（断片ごとに B は B 自身の x で 0→1 へ正規化されない点に注意。
//   x' の式は A と同じものを使う。B の x が A と大きく違う場合は 2プロファイルの
//   従来動作と同じく、x も Lerp された値になる）。
//
// 【端の扱い】
//   切断値が見つからない（x が単調でない／切断値まで届かない）ときは末尾の点を使う。
//   切断位置は前の切断位置より手前へは戻さない。断片が逆順になるのを防ぐため。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Frill
{
    /// <summary>断面プロファイルを x で等分し、ステップへ配る断片を作る。</summary>
    public static class FrillProfileSplit
    {
        /// <summary>
        /// プロファイルを span 個の断片へ割る。
        /// b は点数が a と同じときだけ使い、違うときは piecesB を null にする。
        /// 割れないとき（span &lt; 2 / 点が足りない）は false を返す。
        /// </summary>
        public static bool Split(
            IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b, int span,
            out Vector2[][] piecesA, out Vector2[][] piecesB)
        {
            piecesA = null;
            piecesB = null;

            if (span < 2 || a == null || a.Count < 2) return false;

            int  n    = a.Count;
            bool useB = (b != null && b.Count == n);

            // 切断位置は「区間番号 + 区間内の比率」で持つ。両端は元の点そのもの。
            var cutIdx = new int[span + 1];
            var cutU   = new float[span + 1];

            cutIdx[0]    = 0;     cutU[0]    = 0f;
            cutIdx[span] = n - 2; cutU[span] = 1f;

            for (int d = 1; d < span; d++)
            {
                float c = (float)d / span;

                int j = -1;
                for (int k = 0; k < n - 1; k++)
                {
                    if (a[k].x <= c && c <= a[k + 1].x) { j = k; break; }
                }

                float u;
                if (j < 0)
                {
                    j = n - 2;
                    u = 1f;
                }
                else
                {
                    float w = a[j + 1].x - a[j].x;
                    u = (Mathf.Abs(w) < 1e-9f) ? 0f : Mathf.Clamp01((c - a[j].x) / w);
                }

                // 前の切断位置より手前へは戻さない。
                if (j < cutIdx[d - 1] || (j == cutIdx[d - 1] && u < cutU[d - 1]))
                {
                    j = cutIdx[d - 1];
                    u = cutU[d - 1];
                }

                cutIdx[d] = j;
                cutU[d]   = u;
            }

            piecesA = new Vector2[span][];
            if (useB) piecesB = new Vector2[span][];

            for (int d = 0; d < span; d++)
            {
                float f0 = cutIdx[d]     + cutU[d];
                float f1 = cutIdx[d + 1] + cutU[d + 1];

                // 両端の切断点にはさまれた元の点だけを内側に入れる。
                var inner = new List<int>();
                for (int i = 0; i < n; i++)
                    if (i > f0 + 1e-6f && i < f1 - 1e-6f) inner.Add(i);

                piecesA[d] = BuildPiece(a, inner, cutIdx[d], cutU[d], cutIdx[d + 1], cutU[d + 1], span, d);
                if (useB)
                    piecesB[d] = BuildPiece(b, inner, cutIdx[d], cutU[d], cutIdx[d + 1], cutU[d + 1], span, d);
            }

            return true;
        }

        // ================================================================
        // 内部
        // ================================================================

        private static Vector2[] BuildPiece(
            IReadOnlyList<Vector2> src, List<int> inner,
            int j0, float u0, int j1, float u1, int span, int d)
        {
            int m   = inner.Count + 2;
            var dst = new Vector2[m];

            dst[0] = At(src, j0, u0);
            for (int k = 0; k < inner.Count; k++) dst[k + 1] = src[inner[k]];
            dst[m - 1] = At(src, j1, u1);

            for (int k = 0; k < m; k++) dst[k] = new Vector2(dst[k].x * span - d, dst[k].y);
            return dst;
        }

        private static Vector2 At(IReadOnlyList<Vector2> src, int j, float u)
            => Vector2.Lerp(src[j], src[j + 1], u);
    }
}
