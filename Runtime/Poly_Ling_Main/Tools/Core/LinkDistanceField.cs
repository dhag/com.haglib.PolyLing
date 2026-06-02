// Tools/Core/LinkDistanceField.cs
// リンク距離（辺グラフ上の測地距離）計算
// マグネット・スカルプト・特殊選択で共有する独立クラス

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Tools
{
    // ================================================================
    // 距離モード（マグネット・スカルプト共有）
    // ================================================================
    /// <summary>
    /// 影響範囲の距離計算方式。
    /// Euclidean: 直線距離（従来）。Link: 辺グラフ上の測地距離（リンク距離）。
    /// </summary>
    public enum DistanceMode
    {
        /// <summary>ユークリッド直線距離</summary>
        Euclidean,
        /// <summary>リンク距離（辺をたどった累積距離）</summary>
        Link,
    }

    // ================================================================
    // リンク距離計算
    // ================================================================
    /// <summary>
    /// 頂点隣接グラフ上の辺長累積距離（リンク距離）を多始点ダイクストラで計算する。
    /// MonoBehaviour・ToolContext には依存しない純粋ロジック。
    /// </summary>
    public static class LinkDistanceField
    {
        /// <summary>
        /// 隣接グラフと頂点位置を与えてリンク距離場を計算する。
        /// </summary>
        /// <param name="adjacency">頂点インデックス→隣接頂点集合</param>
        /// <param name="positions">頂点位置（インデックス対応）</param>
        /// <param name="seeds">始点頂点インデックス（複数可）</param>
        /// <param name="maxDistance">この距離を超える頂点は結果に含めない</param>
        /// <returns>頂点インデックス→始点集合からのリンク距離（始点は 0）</returns>
        public static Dictionary<int, float> Compute(
            Dictionary<int, HashSet<int>> adjacency,
            IReadOnlyList<Vector3> positions,
            IEnumerable<int> seeds,
            float maxDistance)
        {
            var result = new Dictionary<int, float>();
            if (adjacency == null || positions == null || seeds == null)
                return result;

            int count = positions.Count;
            var heap = new MinHeap(count);

            // 始点を距離 0 で投入
            foreach (int s in seeds)
            {
                if (s < 0 || s >= count) continue;
                if (!result.ContainsKey(s))
                {
                    result[s] = 0f;
                    heap.Push(s, 0f);
                }
            }

            if (maxDistance <= 0f)
                return result; // 始点のみ

            while (heap.Count > 0)
            {
                heap.Pop(out int current, out float dist);

                // 取り出した値が最新でなければスキップ
                if (!result.TryGetValue(current, out float best) || dist > best)
                    continue;

                if (!adjacency.TryGetValue(current, out var neighbors))
                    continue;

                Vector3 currentPos = positions[current];

                foreach (int neighbor in neighbors)
                {
                    if (neighbor < 0 || neighbor >= count) continue;

                    float edgeLen = Vector3.Distance(currentPos, positions[neighbor]);
                    float newDist = dist + edgeLen;

                    if (newDist > maxDistance) continue;

                    if (!result.TryGetValue(neighbor, out float old) || newDist < old)
                    {
                        result[neighbor] = newDist;
                        heap.Push(neighbor, newDist);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// MeshObject から隣接グラフを構築してリンク距離場を計算する簡易版。
        /// </summary>
        public static Dictionary<int, float> Compute(
            MeshObject meshObject,
            IEnumerable<int> seeds,
            float maxDistance)
        {
            if (meshObject == null)
                return new Dictionary<int, float>();

            var adjacency = SelectionHelper.BuildVertexAdjacency(meshObject);
            return Compute(adjacency, meshObject.Positions, seeds, maxDistance);
        }

        // ============================================================
        // 内部: 単純なバイナリ最小ヒープ（System.PriorityQueue 非依存）
        // ============================================================
        private struct MinHeap
        {
            private int[] _idx;
            private float[] _dist;
            private int _size;

            public MinHeap(int capacityHint)
            {
                int cap = Mathf.Max(16, capacityHint);
                _idx = new int[cap];
                _dist = new float[cap];
                _size = 0;
            }

            public int Count => _size;

            public void Push(int index, float dist)
            {
                if (_size >= _idx.Length)
                {
                    int newCap = _idx.Length * 2;
                    System.Array.Resize(ref _idx, newCap);
                    System.Array.Resize(ref _dist, newCap);
                }

                int i = _size++;
                _idx[i] = index;
                _dist[i] = dist;

                // 上方向へ
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (_dist[parent] <= _dist[i]) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public void Pop(out int index, out float dist)
            {
                index = _idx[0];
                dist = _dist[0];

                _size--;
                if (_size > 0)
                {
                    _idx[0] = _idx[_size];
                    _dist[0] = _dist[_size];

                    // 下方向へ
                    int i = 0;
                    while (true)
                    {
                        int left = 2 * i + 1;
                        int right = 2 * i + 2;
                        int smallest = i;

                        if (left < _size && _dist[left] < _dist[smallest]) smallest = left;
                        if (right < _size && _dist[right] < _dist[smallest]) smallest = right;
                        if (smallest == i) break;

                        Swap(i, smallest);
                        i = smallest;
                    }
                }
            }

            private void Swap(int a, int b)
            {
                (_idx[a], _idx[b]) = (_idx[b], _idx[a]);
                (_dist[a], _dist[b]) = (_dist[b], _dist[a]);
            }
        }
    }
}
