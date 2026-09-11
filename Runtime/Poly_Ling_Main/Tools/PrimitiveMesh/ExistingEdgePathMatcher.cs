// ExistingEdgePathMatcher.cs
// 点指定図形：新しく作る辺の両端が既存頂点のとき、既存メッシュ上の辺列と
// 完全共有できるかを判定する。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【判定】（仕様 9.3〜9.5）
//   1. 両端が同じメッシュの既存頂点であること。
//   2. そのメッシュの辺をたどる最短経路（通過エッジ数が最小）を求める。
//   3. 同じエッジ数の経路が複数あるときは、線分 P0-P1 に最も近いものを採る。
//        E = Σ distance(Vi, segment(P0, P1))^2   （Vi は中間頂点）
//      経路を全部並べると格子状メッシュで組合せ爆発するので、
//      E が頂点ごとの和であることを使い、最短経路の DAG 上の動的計画法で最小を求める。
//   4. 経路のエッジ数 == 新しい辺の分割数 のときだけ共有可能。
//
// 【キャッシュ】（仕様 9.12）
//   経路は点を指定し直したときに Invalidate し、分割数が変わっただけでは探し直さない。
//   メッシュの差し替え・頂点数や面数の変化でも捨てる。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>既存経路との共有判定の結果（仕様 13）。</summary>
    public sealed class EdgeShareMatch
    {
        public bool      CanShare;
        public int       MeshIndex = -1;
        /// <summary>始点から終点までの既存頂点列（両端を含む）。経路が無ければ null。</summary>
        public List<int> VertexPath;
        /// <summary>経路のエッジ数。経路が無ければ -1。</summary>
        public int       EdgeCount = -1;
        public float     PathError;
    }

    public sealed class ExistingEdgePathMatcher
    {
        private MeshObject _mo;
        private int        _meshIndex = -1;
        private int        _vertexCount = -1;
        private int        _faceCount = -1;
        private int        _maxEdges;
        private Func<int, Vector3> _worldOf;
        private Dictionary<int, HashSet<int>> _adjacency;

        /// <summary>(小さい番号, 大きい番号) → 小さい番号から大きい番号への経路。経路なしは null。</summary>
        private readonly Dictionary<long, List<int>> _cache = new Dictionary<long, List<int>>();
        private readonly Dictionary<long, float>     _cacheError = new Dictionary<long, float>();

        /// <summary>
        /// 判定対象のメッシュを設定する。前回と別のメッシュ、または頂点数・面数が
        /// 変わっていればキャッシュを捨てる。
        /// </summary>
        /// <param name="worldOf">頂点番号 → ワールド座標。E の評価に使う。</param>
        /// <param name="maxEdges">探索するエッジ数の上限。分割数の上限と同じにする。</param>
        public void Prepare(MeshObject mo, int meshIndex, Func<int, Vector3> worldOf, int maxEdges)
        {
            _worldOf = worldOf;

            bool changed = !ReferenceEquals(mo, _mo)
                        || meshIndex != _meshIndex
                        || (mo != null && (mo.VertexCount != _vertexCount || mo.FaceCount != _faceCount))
                        || maxEdges != _maxEdges;
            if (!changed) return;

            _mo          = mo;
            _meshIndex   = meshIndex;
            _vertexCount = mo?.VertexCount ?? -1;
            _faceCount   = mo?.FaceCount ?? -1;
            _maxEdges    = maxEdges;
            Invalidate();
        }

        /// <summary>経路のキャッシュを捨てる。点を指定し直したときに呼ぶ。</summary>
        public void Invalidate()
        {
            _cache.Clear();
            _cacheError.Clear();
            _adjacency = null;
        }

        /// <summary>仕様 13 の形。両端が判定対象メッシュの既存頂点でなければ共有しない。</summary>
        public EdgeShareMatch TryMatch(PointPick start, PointPick end, int subdivisionCount)
        {
            if (start.MeshIndex != _meshIndex || end.MeshIndex != _meshIndex)
                return new EdgeShareMatch { MeshIndex = _meshIndex };
            return TryMatch(start.VertexIndex, end.VertexIndex, subdivisionCount);
        }

        /// <summary>
        /// 判定対象メッシュの頂点 start から end への最短経路を求め、分割数と比べる。
        /// 返す経路は start → end の向き。
        /// </summary>
        public EdgeShareMatch TryMatch(int start, int end, int subdivisionCount)
        {
            var result = new EdgeShareMatch { MeshIndex = _meshIndex };
            if (_mo == null) return result;
            if (start < 0 || end < 0 || start >= _mo.VertexCount || end >= _mo.VertexCount) return result;
            if (start == end) return result;

            int lo = Math.Min(start, end), hi = Math.Max(start, end);
            long key = ((long)lo << 32) | (uint)hi;

            if (!_cache.TryGetValue(key, out var path))
            {
                if (_adjacency == null) _adjacency = SelectionHelper.BuildVertexAdjacency(_mo);
                path = FindShortestPath(_adjacency, lo, hi, _maxEdges, _worldOf, out float err);
                _cache[key] = path;
                _cacheError[key] = err;
            }
            if (path == null) return result;

            var oriented = new List<int>(path);
            if (start != lo) oriented.Reverse();

            result.VertexPath = oriented;
            result.EdgeCount  = oriented.Count - 1;
            result.PathError  = _cacheError.TryGetValue(key, out float e) ? e : 0f;
            result.CanShare   = result.EdgeCount == subdivisionCount;
            return result;
        }

        /// <summary>
        /// エッジ数が最小の経路のうち、線分 (start, end) との二乗距離和が最小のものを返す。
        /// maxEdges 以内で届かなければ null。
        /// </summary>
        public static List<int> FindShortestPath(
            Dictionary<int, HashSet<int>> adjacency, int start, int end, int maxEdges,
            Func<int, Vector3> worldOf, out float pathError)
        {
            pathError = 0f;
            if (adjacency == null || worldOf == null) return null;
            if (!adjacency.ContainsKey(start) || !adjacency.ContainsKey(end)) return null;

            // ── 幅優先探索（end の段まで） ──
            var dist  = new Dictionary<int, int> { { start, 0 } };
            var order = new List<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            int endDist = -1;

            while (queue.Count > 0)
            {
                int v  = queue.Dequeue();
                int dv = dist[v];
                if (endDist >= 0 && dv >= endDist) continue;
                if (dv >= maxEdges) continue;
                if (!adjacency.TryGetValue(v, out var nb)) continue;

                foreach (int w in nb)
                {
                    if (dist.ContainsKey(w)) continue;
                    dist[w] = dv + 1;
                    order.Add(w);
                    if (w == end) endDist = dv + 1;
                    queue.Enqueue(w);
                }
            }
            if (endDist < 0) return null;

            // ── 最短経路の DAG 上で E を最小化 ──
            Vector3 a = worldOf(start);
            Vector3 b = worldOf(end);

            var best = new Dictionary<int, float> { { start, 0f } };
            var prev = new Dictionary<int, int>();

            foreach (int v in order)
            {
                if (v == start) continue;
                int dv = dist[v];
                if (dv > endDist) continue;

                float bestCost = float.MaxValue;
                int   bestPrev = -1;
                foreach (int p in adjacency[v])
                {
                    if (!dist.TryGetValue(p, out int dp) || dp != dv - 1) continue;
                    if (!best.TryGetValue(p, out float c)) continue;
                    if (c < bestCost || (c == bestCost && p < bestPrev))
                    {
                        bestCost = c;
                        bestPrev = p;
                    }
                }
                if (bestPrev < 0) continue;

                float own = (v == end) ? 0f : SqrDistanceToSegment(worldOf(v), a, b);
                best[v] = bestCost + own;
                prev[v] = bestPrev;
            }
            if (!prev.ContainsKey(end)) return null;

            var path = new List<int> { end };
            int cur = end;
            while (cur != start)
            {
                cur = prev[cur];
                path.Add(cur);
            }
            path.Reverse();
            pathError = best[end];
            return path;
        }

        private static float SqrDistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-20f) return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
            return (p - (a + ab * t)).sqrMagnitude;
        }
    }
}
