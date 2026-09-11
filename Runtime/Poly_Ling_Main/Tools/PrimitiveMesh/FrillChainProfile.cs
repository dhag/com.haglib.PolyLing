// FrillChainProfile.cs
// 共有レールでつながった梯子の鎖に沿って、断面プロファイルの補間パラメータ t を配る。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【なぜ要るか】
//   既定の t は取り込み時の段グループ（RowIndex / RowCount）から決まる。
//   段グループを持たない梯子（別々に取り込んだ／CSV に $group が無い）を N 本並べても、
//   梯子1本ごとに t=0→1 が閉じてしまい、鎖の全体で A → B にならない。
//   ここでは形状（レール線分の共有）から並び順を作り、鎖の端から端へ t を配る。
//
// 【鎖の作り方】
//   梯子の左右レールをそれぞれ「線分の集合」として見て、線分を1本でも共有する
//   レールどうしを隣とみなす。1本のレールに相手が2つ以上あるときはどちらが隣か
//   決められないので辺にしない（＝そこで鎖が切れる）。
//   線分の向きは見ない。溶接（FrillMeshGenerator の RailKey）は向きまで一致した
//   ときだけ起きるが、補間の並び順としては位置が同じなら隣として扱ってよい。
//
// 【端の決め方】
//   自由レール（どの梯子とも共有していないレール）を持つ梯子が鎖の端になる。
//   端が2つあるときは、梯子リストで先に現れる側の自由レールを t=0（プロファイルA）にする。
//   梯子の上下は幾何では決まらないため、A/B が逆に出たら ProfileFlip で入れ替える。
//
// 【閉じた鎖】
//   端が無い（全部の梯子が両側を共有している）ときは、リストで最も早い梯子の
//   左レールを t=0 の位置にする。一周した先は t=1 になるので、そこが上下を分ける
//   裂け目になる（RailKey に t を含むため、その1本だけ溶接されない）。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Frill
{
    /// <summary>共有レールでつながった梯子の鎖に沿って t を配る。</summary>
    public static class FrillChainProfile
    {
        /// <summary>位置キーの量子化幅。FrillMeshGenerator と同じ値。</summary>
        private const float PosEps = 1e-5f;

        /// <summary>
        /// 鎖ごとに TLeft / TRight を入れ直す。鎖に属さない梯子は左 0・右 1 になる。
        /// flip が true のときは t を 1-t にする（A/B の入替）。
        /// </summary>
        public static void Assign(IReadOnlyList<FrillBeltInput> belts, bool flip)
        {
            if (belts == null || belts.Count == 0) return;

            int n = belts.Count;

            // スロット = 梯子index * 2 + (0: 左レール / 1: 右レール)
            var segOwners = new Dictionary<SegKey, List<int>>();
            for (int i = 0; i < n; i++)
            {
                var b = belts[i];
                if (b == null) continue;
                AddSegments(segOwners, b.Left,  b.Closed, i * 2 + 0);
                AddSegments(segOwners, b.Right, b.Closed, i * 2 + 1);
            }

            // スロットごとの相手候補
            var partners = new HashSet<int>[n * 2];
            for (int k = 0; k < n * 2; k++) partners[k] = new HashSet<int>();

            foreach (var kv in segOwners)
            {
                var list = kv.Value;
                for (int a = 0; a < list.Count; a++)
                {
                    for (int c = a + 1; c < list.Count; c++)
                    {
                        int sa = list[a], sc = list[c];
                        if ((sa >> 1) == (sc >> 1)) continue;   // 同じ梯子の左右が重なっている
                        partners[sa].Add(sc);
                        partners[sc].Add(sa);
                    }
                }
            }

            // 相手がちょうど1つで、相手から見ても自分がちょうど1つのときだけ辺にする。
            var link = new int[n * 2];
            for (int k = 0; k < n * 2; k++) link[k] = -1;

            for (int k = 0; k < n * 2; k++)
            {
                if (partners[k].Count != 1) continue;
                int other = First(partners[k]);
                if (other < 0) continue;
                if (partners[other].Count != 1) continue;
                if (First(partners[other]) != k) continue;
                link[k] = other;
            }

            var visited = new bool[n];

            for (int i = 0; i < n; i++)
            {
                if (visited[i] || belts[i] == null) continue;

                var members = new List<int>();
                Collect(i, link, visited, members);

                // 端（自由レールを持つ梯子）のうち、リストで先に現れるものを起点にする。
                int startBelt = -1;
                int startSide = 0;

                foreach (int b in members)
                {
                    bool freeL = link[b * 2 + 0] < 0;
                    bool freeR = link[b * 2 + 1] < 0;
                    if (!freeL && !freeR) continue;
                    if (startBelt >= 0 && b >= startBelt) continue;
                    startBelt = b;
                    startSide = freeL ? 0 : 1;
                }

                if (startBelt < 0)
                {
                    // 閉じた鎖。リストで最も早い梯子の左レールを t=0 にする。
                    startBelt = members[0];
                    foreach (int b in members) if (b < startBelt) startBelt = b;
                    startSide = 0;
                }

                int count = members.Count;
                int belt  = startBelt;
                int side  = startSide;

                for (int k = 0; k < count; k++)
                {
                    SetT(belts[belt], side,     (float)k / count,       flip);
                    SetT(belts[belt], 1 - side, (float)(k + 1) / count, flip);

                    int next = link[belt * 2 + (1 - side)];
                    if (next < 0) break;

                    int nextBelt = next >> 1;
                    if (nextBelt == startBelt) break;   // 閉じた鎖は一周で終わり

                    belt = nextBelt;
                    side = next & 1;
                }
            }
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>start とつながっている梯子を集める。辺は梯子1本につき最大2本。</summary>
        private static void Collect(int start, int[] link, bool[] visited, List<int> members)
        {
            var stack = new Stack<int>();
            stack.Push(start);
            visited[start] = true;

            while (stack.Count > 0)
            {
                int b = stack.Pop();
                members.Add(b);

                for (int side = 0; side < 2; side++)
                {
                    int next = link[b * 2 + side];
                    if (next < 0) continue;

                    int nb = next >> 1;
                    if (visited[nb]) continue;

                    visited[nb] = true;
                    stack.Push(nb);
                }
            }
        }

        private static void SetT(FrillBeltInput belt, int side, float t, bool flip)
        {
            if (belt == null) return;

            float v = flip ? 1f - t : t;
            if (side == 0) belt.TLeft = v;
            else           belt.TRight = v;
        }

        private static void AddSegments(
            Dictionary<SegKey, List<int>> owners, IReadOnlyList<Vector3> rail, bool closed, int slot)
        {
            if (rail == null || rail.Count < 2) return;

            int cnt = closed ? rail.Count : rail.Count - 1;
            for (int s = 0; s < cnt; s++)
            {
                var key = new SegKey(rail[s], rail[(s + 1) % rail.Count]);

                if (!owners.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    owners[key] = list;
                }
                if (!list.Contains(slot)) list.Add(slot);
            }
        }

        private static int First(HashSet<int> set)
        {
            foreach (int v in set) return v;
            return -1;
        }

        private static long Q(float f) => (long)Mathf.Round(f / PosEps);

        /// <summary>量子化した線分。向きは見ない（小さい側の端点を先にそろえる）。</summary>
        private readonly struct SegKey : System.IEquatable<SegKey>
        {
            private readonly long _x0, _y0, _z0, _x1, _y1, _z1;

            public SegKey(Vector3 a, Vector3 b)
            {
                long ax = Q(a.x), ay = Q(a.y), az = Q(a.z);
                long bx = Q(b.x), by = Q(b.y), bz = Q(b.z);

                if (Less(bx, by, bz, ax, ay, az))
                {
                    _x0 = bx; _y0 = by; _z0 = bz;
                    _x1 = ax; _y1 = ay; _z1 = az;
                }
                else
                {
                    _x0 = ax; _y0 = ay; _z0 = az;
                    _x1 = bx; _y1 = by; _z1 = bz;
                }
            }

            private static bool Less(long ax, long ay, long az, long bx, long by, long bz)
                => ax != bx ? ax < bx : (ay != by ? ay < by : az < bz);

            public bool Equals(SegKey o)
                => _x0 == o._x0 && _y0 == o._y0 && _z0 == o._z0
                && _x1 == o._x1 && _y1 == o._y1 && _z1 == o._z1;

            public override bool Equals(object obj) => obj is SegKey k && Equals(k);

            public override int GetHashCode()
            {
                unchecked
                {
                    long h = _x0;
                    h = h * 31 + _y0;
                    h = h * 31 + _z0;
                    h = h * 31 + _x1;
                    h = h * 31 + _y1;
                    h = h * 31 + _z1;
                    return (int)(h ^ (h >> 32));
                }
            }
        }
    }
}
