// BridgeLoopOps.cs
// 2つのエッジループ（穴の縁）を面でつなぐ「ブリッジ」の位相計算。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【手順】
//   1. 各穴の縁を、指定した開始頂点（と初期方向）から順序付きループにする。
//   2. 2つのループの対応を「正規化パラメータの格子経路」で決める。
//      (i+1)/n と (j+1)/m を比べ、小さい側だけ進めれば三角形、等しければ
//      両方進めて四角形になる。頂点数が同じなら全部四角形、違えば余りが三角形。
//   3. 分割数 s>0 のときは、格子経路上の各対応点 (i,j) ごとに A_i→B_j を
//      s 分割した中間頂点の「列」を1本作り、隣り合う列の間に面を張る。
//      中間頂点は隣接セルで共有するので裂けない。
//
// 面の頂点は「符号化ID」で返す。呼び出し側が実頂点へ解決する。
//   0            .. ACount-1          → ループA の k 番目
//   ACount       .. ACount+BCount-1   → ループB の k 番目
//   ACount+BCount ..                  → 中間頂点 Inter[k]

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    public static class BridgeLoopOps
    {
        /// <summary>中間頂点。ループAの AIdx 番目とループBの BIdx 番目を T で内分した位置。</summary>
        public struct InterPoint
        {
            public int   AIdx;
            public int   BIdx;
            public float T;
        }

        /// <summary>ブリッジの計算結果。</summary>
        public class BridgeResult
        {
            public bool   Ok;
            public string Message;

            /// <summary>ループA の頂点数（＝AOrder.Count）。符号化IDの境界に使う。</summary>
            public int ACount;
            /// <summary>ループB の頂点数。</summary>
            public int BCount;

            /// <summary>中間頂点（分割数 0 のときは空）。</summary>
            public List<InterPoint> Inter = new List<InterPoint>();

            /// <summary>面。各要素は符号化IDの列（3 または 4 個）。</summary>
            public List<int[]> Faces = new List<int[]>();

            public int InterBase => ACount + BCount;
        }

        // ================================================================
        // ループの順序化
        // ================================================================

        /// <summary>
        /// startVertex を含むエッジグループ（1面だけが使う辺の連結成分）を、
        /// startVertex から一方向にたどって順序付きの頂点列にする。
        /// directionHint が startVertex の隣接頂点なら、そちらへ進む向きを採る。
        /// </summary>
        public static List<int> OrderBoundaryLoop(
            MeshObject mesh, int startVertex, int directionHint, out string message)
        {
            message = null;
            var order = new List<int>();

            if (mesh == null || startVertex < 0 || startVertex >= mesh.Vertices.Count)
            {
                message = "頂点が不正です";
                return order;
            }

            var group = BoundaryEdgeOps.GroupFromVertex(mesh, startVertex);
            if (group.Count == 0)
            {
                message = "指定頂点はエッジ（1面だけが使う辺）上にありません";
                return order;
            }

            // 頂点 → 隣接（グループ内のみ）
            var adj = new Dictionary<int, List<int>>();
            foreach (var e in group)
            {
                AddAdj(adj, e.V1, e.V2);
                AddAdj(adj, e.V2, e.V1);
            }

            if (!adj.TryGetValue(startVertex, out var startNeighbors) || startNeighbors.Count == 0)
            {
                message = "エッジをたどれませんでした";
                return order;
            }

            int next = -1;
            if (directionHint >= 0 && startNeighbors.Contains(directionHint))
                next = directionHint;
            else
                next = MinOf(startNeighbors);

            var visited = new HashSet<int> { startVertex };
            order.Add(startVertex);

            int prev = startVertex;
            int cur  = next;

            while (cur >= 0 && !visited.Contains(cur))
            {
                order.Add(cur);
                visited.Add(cur);

                if (!adj.TryGetValue(cur, out var ns)) break;

                int forward = -1;
                foreach (int n in ns)
                {
                    if (n == prev) continue;
                    if (visited.Contains(n)) continue;
                    if (forward < 0 || n < forward) forward = n;
                }

                prev = cur;
                cur  = forward;
            }

            if (order.Count < 3)
            {
                message = "エッジの頂点が足りません";
                order.Clear();
                return order;
            }

            message = $"{order.Count} 頂点";
            return order;
        }

        private static void AddAdj(Dictionary<int, List<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<int>();
            if (!list.Contains(value)) list.Add(value);
        }

        private static int MinOf(List<int> values)
        {
            int best = values[0];
            for (int i = 1; i < values.Count; i++)
                if (values[i] < best) best = values[i];
            return best;
        }

        // ================================================================
        // ブリッジ構築
        // ================================================================

        /// <summary>
        /// ループA（n頂点）とループB（m頂点）をつなぐ面を作る。
        /// flipCorrespondence が true のときループBの周回方向を反転する。
        /// flipFaces が true のとき生成面の巻き方向を反転する。
        /// subdivisions は A→B 方向の分割数（0 で分割なし）。
        /// </summary>
        public static BridgeResult Build(
            int aCount, int bCount, bool flipCorrespondence, bool flipFaces, int subdivisions)
        {
            var r = new BridgeResult { ACount = aCount, BCount = bCount };

            if (aCount < 3 || bCount < 3)
            {
                r.Message = "エッジの頂点が足りません";
                return r;
            }

            int s = Mathf.Max(0, subdivisions);

            // ループB の周回順。反転時は先頭を保ったまま逆回りにする
            //（開始点どうしの対応は保ちたいため、先頭は動かさない）。
            var bOrder = new int[bCount];
            for (int k = 0; k < bCount; k++)
                bOrder[k] = flipCorrespondence ? (bCount - k) % bCount : k;

            // 格子経路（(0,0) → (n,m)）
            var path = BuildPairPath(aCount, bCount);

            // 各対応点の中間頂点列。path の末尾は先頭と同じ位置なので列も共有する。
            int pairCount = path.Count - 1;                 // 独立した対応点の数
            var columns = new int[pairCount][];             // columns[k][l] = 符号化ID（l=0..s+1）

            for (int k = 0; k < pairCount; k++)
            {
                int ai = path[k].I % aCount;
                int bj = bOrder[path[k].J % bCount];

                var col = new int[s + 2];
                col[0]     = ai;                 // ループA
                col[s + 1] = aCount + bj;        // ループB

                for (int l = 1; l <= s; l++)
                {
                    float t = (float)l / (s + 1);
                    r.Inter.Add(new InterPoint { AIdx = ai, BIdx = bj, T = t });
                    col[l] = r.InterBase + r.Inter.Count - 1;
                }
                columns[k] = col;
            }

            // 隣り合う列の間に面を張る
            for (int k = 0; k < pairCount; k++)
            {
                int k2 = (k + 1) % pairCount;

                bool aAdvanced = path[k + 1].I != path[k].I;
                bool bAdvanced = path[k + 1].J != path[k].J;

                var c0 = columns[k];
                var c1 = columns[k2];

                for (int l = 0; l <= s; l++)
                {
                    bool degenerateLow  = !aAdvanced && l == 0;       // 下端が同じ頂点
                    bool degenerateHigh = !bAdvanced && l == s;       // 上端が同じ頂点

                    int[] face;
                    if (degenerateLow && degenerateHigh)
                        continue;                                     // 面にならない
                    else if (degenerateLow)
                        face = new[] { c0[0], c1[l + 1], c0[l + 1] };
                    else if (degenerateHigh)
                        face = new[] { c0[l], c1[l], c0[s + 1] };
                    else
                        face = new[] { c0[l], c1[l], c1[l + 1], c0[l + 1] };

                    if (flipFaces) System.Array.Reverse(face);
                    r.Faces.Add(face);
                }
            }

            r.Ok = r.Faces.Count > 0;
            r.Message = r.Ok
                ? $"面 {r.Faces.Count} / 追加頂点 {r.Inter.Count}"
                : "面を作れませんでした";
            return r;
        }

        /// <summary>格子経路の1点。</summary>
        private struct Pair
        {
            public int I, J;
        }

        /// <summary>
        /// (0,0) から (n,m) までの単調な格子経路を作る。
        /// 次に進むと (i+1)/n と (j+1)/m のどちらが小さいかで進む側を決め、
        /// 同じなら両方進める（＝四角形になる）。
        /// </summary>
        private static List<Pair> BuildPairPath(int n, int m)
        {
            var path = new List<Pair> { new Pair { I = 0, J = 0 } };

            int i = 0, j = 0;
            const float Eps = 1e-6f;

            while (i < n || j < m)
            {
                if (j >= m)      { i++; }
                else if (i >= n) { j++; }
                else
                {
                    float ta = (float)(i + 1) / n;
                    float tb = (float)(j + 1) / m;
                    if (Mathf.Abs(ta - tb) <= Eps) { i++; j++; }
                    else if (ta < tb)              { i++; }
                    else                           { j++; }
                }
                path.Add(new Pair { I = i, J = j });
            }

            return path;
        }

        // ================================================================
        // 位置の解決
        // ================================================================

        /// <summary>
        /// 符号化IDを位置へ解決する。プレビュー生成と実生成の両方で使う。
        /// </summary>
        public static Vector3 ResolvePosition(
            BridgeResult r, int encodedId, IReadOnlyList<Vector3> aPos, IReadOnlyList<Vector3> bPos)
        {
            if (encodedId < r.ACount) return aPos[encodedId];
            if (encodedId < r.InterBase) return bPos[encodedId - r.ACount];

            var ip = r.Inter[encodedId - r.InterBase];
            return Vector3.Lerp(aPos[ip.AIdx], bPos[ip.BIdx], ip.T);
        }
    }
}
