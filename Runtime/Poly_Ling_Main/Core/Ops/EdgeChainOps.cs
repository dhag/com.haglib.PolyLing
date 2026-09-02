// EdgeChainOps.cs
// 拾った辺の集合を「ちょうど2つの辺群」に分け、それぞれを順序付き頂点列にする。
// 辺群ブリッジ（EdgeBridgeToolHandler）の入力を作るための位相計算。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【なぜ A/B を別々に拾わせないか】
//   連結関係で 2 領域を判別できるため、利用者は「辺を拾う」だけでよい。
//   3 群以上・分岐ありは面の張り方が一意に決まらないので、ここで拒否する。
//
// 【BoundaryEdgeOps との関係】
//   連結成分分割は BoundaryEdgeOps.BuildGroups をそのまま使う。あちらは
//   渡された辺集合を頂点共有で分けるだけで、境界辺であることを前提にしない。
//   「境界辺だけを対象にするか」は拾う側（ツール）の責任で、ここでは問わない。
//
// 【並びの固定】
//   BuildGroups は HashSet を走査するので列挙順が保証されない。
//   A/B の割り当ては「構成頂点の最小番号が小さい方を A」で固定し、
//   鎖の始点も「番号の小さい端点」で固定する。同じ入力なら毎回同じ結果になる。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    public static class EdgeChainOps
    {
        /// <summary>順序付きの辺群 1 本分。</summary>
        public sealed class Chain
        {
            /// <summary>順に並んだ頂点インデックス（重複なし）。</summary>
            public List<int> Order = new List<int>();

            /// <summary>閉環なら true。開いた鎖なら false。</summary>
            public bool Closed;

            /// <summary>この辺群を構成する辺。</summary>
            public List<VertexPair> Edges = new List<VertexPair>();

            public int Count => Order.Count;
        }

        // ================================================================
        // 2 群への分割
        // ================================================================

        /// <summary>
        /// 辺集合を連結成分に分け、ちょうど 2 つの順序付き辺群にする。
        /// 成分数が 2 でない、分岐がある、といった場合は false と理由を返す。
        /// 戻り値の A は「構成頂点の最小番号が小さい方」で固定する。
        /// </summary>
        public static bool SplitIntoTwoChains(
            IReadOnlyCollection<VertexPair> edges,
            out Chain chainA, out Chain chainB, out string message)
        {
            chainA = null;
            chainB = null;
            message = null;

            if (edges == null || edges.Count == 0)
            {
                message = "辺が拾われていません";
                return false;
            }

            var groups = BoundaryEdgeOps.BuildGroups(new HashSet<VertexPair>(edges));
            if (groups.Count != 2)
            {
                message = groups.Count < 2
                    ? "辺群が 1 つしかありません。離れた 2 か所の辺を拾ってください"
                    : $"辺群が {groups.Count} 個あります。2 か所だけにしてください";
                return false;
            }

            var chains = new List<Chain>(2);
            for (int g = 0; g < groups.Count; g++)
            {
                if (!OrderChain(groups[g], out var chain, out string msg))
                {
                    message = $"辺群{(g == 0 ? "①" : "②")}: {msg}";
                    return false;
                }
                chains.Add(chain);
            }

            // A/B の割り当てを決定的にする（構成頂点の最小番号が小さい方を A）。
            int minA = MinVertex(chains[0]);
            int minB = MinVertex(chains[1]);
            if (minA <= minB) { chainA = chains[0]; chainB = chains[1]; }
            else              { chainA = chains[1]; chainB = chains[0]; }

            return true;
        }

        private static int MinVertex(Chain c)
        {
            int min = int.MaxValue;
            foreach (int v in c.Order)
                if (v < min) min = v;
            return min;
        }

        // ================================================================
        // 1 群の順序化
        // ================================================================

        /// <summary>
        /// 連結した辺の集まりを順序付き頂点列にする。
        /// 次数 1 の頂点が 2 個なら開いた鎖、全て次数 2 なら閉環。
        /// 次数 3 以上（分岐）は面の張り方が決まらないので拒否する。
        /// </summary>
        public static bool OrderChain(
            IReadOnlyList<VertexPair> group, out Chain chain, out string message)
        {
            chain = null;
            message = null;

            if (group == null || group.Count == 0)
            {
                message = "辺がありません";
                return false;
            }

            // 頂点 → 隣接頂点（この群の中だけ）
            var adj = new Dictionary<int, List<int>>();
            foreach (var e in group)
            {
                AddAdj(adj, e.V1, e.V2);
                AddAdj(adj, e.V2, e.V1);
            }

            int branch = -1;
            var ends = new List<int>();
            foreach (var kv in adj)
            {
                int d = kv.Value.Count;
                if (d > 2) { if (branch < 0 || kv.Key < branch) branch = kv.Key; }
                else if (d == 1) ends.Add(kv.Key);
            }

            if (branch >= 0)
            {
                message = $"辺が分岐しています（頂点 {branch}）。枝分かれのない一続きの辺にしてください";
                return false;
            }

            bool closed;
            int start;
            if (ends.Count == 0)
            {
                closed = true;
                start  = MinKey(adj);
            }
            else if (ends.Count == 2)
            {
                closed = false;
                start  = Mathf.Min(ends[0], ends[1]);
            }
            else
            {
                message = $"端点が {ends.Count} 個あります。一続きの辺にしてください";
                return false;
            }

            // 始点から一方向へたどる。次に進む先は番号の小さい未訪問頂点で固定する。
            var order   = new List<int> { start };
            var visited = new HashSet<int> { start };

            int prev = -1;
            int cur  = start;
            while (true)
            {
                if (!adj.TryGetValue(cur, out var ns)) break;

                int next = -1;
                foreach (int n in ns)
                {
                    if (n == prev) continue;
                    if (visited.Contains(n)) continue;
                    if (next < 0 || n < next) next = n;
                }
                if (next < 0) break;

                order.Add(next);
                visited.Add(next);
                prev = cur;
                cur  = next;
            }

            if (order.Count != adj.Count)
            {
                message = "たどりきれない辺があります";
                return false;
            }

            if (closed && order.Count < 3)
            {
                message = "閉じた辺群は 3 頂点以上必要です";
                return false;
            }
            if (!closed && order.Count < 2)
            {
                message = "頂点が足りません";
                return false;
            }

            chain = new Chain { Closed = closed };
            chain.Order.AddRange(order);
            chain.Edges.AddRange(group);
            return true;
        }

        private static void AddAdj(Dictionary<int, List<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<int>();
            if (!list.Contains(value)) list.Add(value);
        }

        private static int MinKey(Dictionary<int, List<int>> map)
        {
            int min = int.MaxValue;
            foreach (var kv in map)
                if (kv.Key < min) min = kv.Key;
            return min;
        }

        // ================================================================
        // 対応の決定（始点合わせと回り向き）
        // ================================================================

        /// <summary>
        /// 2 つの辺群の対応を決める。
        /// <para>
        /// 開環同士: A・B それぞれの両端 4 点のうち、端点どうしの距離和が
        /// 小さくなる組合せを採る。B を反転すべきなら flipCorrespondence=true。
        /// </para>
        /// <para>
        /// 閉環同士: 総当たりで最短の頂点ペアを探し、そこが対応の先頭に来るよう
        /// 両方の並びを回転させる。回り向きは判定できないので flipCorrespondence は
        /// false のまま返す（利用者がチェックボックスで反転する）。
        /// </para>
        /// 座標はワールド空間で渡すこと（メッシュごとに WorldMatrix が違うため）。
        /// </summary>
        public static bool ResolveCorrespondence(
            Chain chainA, IReadOnlyList<Vector3> worldA,
            Chain chainB, IReadOnlyList<Vector3> worldB,
            out bool flipCorrespondence, out string message)
        {
            flipCorrespondence = false;
            message = null;

            if (chainA == null || chainB == null)
            {
                message = "辺群がありません";
                return false;
            }
            if (chainA.Closed != chainB.Closed)
            {
                message = "片方だけが閉じた辺群です。両方とも閉じるか、両方とも開いた辺にしてください";
                return false;
            }
            if (worldA == null || worldB == null
                || worldA.Count != chainA.Count || worldB.Count != chainB.Count)
            {
                message = "座標の数が辺群と合っていません";
                return false;
            }

            if (!chainA.Closed)
            {
                // 開環：A の先頭を B の先頭に合わせるか、B の末尾に合わせるか。
                int la = chainA.Count - 1;
                int lb = chainB.Count - 1;

                float same = (worldA[0]  - worldB[0]).magnitude
                           + (worldA[la] - worldB[lb]).magnitude;
                float rev  = (worldA[0]  - worldB[lb]).magnitude
                           + (worldA[la] - worldB[0]).magnitude;

                flipCorrespondence = rev < same;
                return true;
            }

            // 閉環：最短の頂点ペアが先頭に来るよう、両方の並びを回転させる。
            int bestA = 0, bestB = 0;
            float best = float.MaxValue;
            for (int i = 0; i < worldA.Count; i++)
            {
                for (int j = 0; j < worldB.Count; j++)
                {
                    float d = (worldA[i] - worldB[j]).sqrMagnitude;
                    if (d < best) { best = d; bestA = i; bestB = j; }
                }
            }

            RotateInPlace(chainA.Order, bestA);
            RotateInPlace(chainB.Order, bestB);
            return true;
        }

        /// <summary>list を offset 個だけ左回転する（先頭を offset 番目にする）。</summary>
        public static void RotateInPlace(List<int> list, int offset)
        {
            if (list == null || list.Count == 0) return;
            offset %= list.Count;
            if (offset <= 0) return;

            var head = list.GetRange(0, offset);
            list.RemoveRange(0, offset);
            list.AddRange(head);
        }
    }
}
