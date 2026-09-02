// Runtime/Poly_Ling_Main/Tools/Core/LinkDistanceLocalSearch.cs
// CSR 隣接グラフ上の単一始点リンク距離探索。件数打ち切りと距離打ち切りの両方に対応する。
//
// 【LinkDistanceField との違い】
// LinkDistanceField.Compute は 1 回の呼び出しごとに Dictionary と MinHeap を
// 新しく確保する。頂点ごとに独立に近傍を求める用途では呼び出し回数が
// ターゲット頂点数と同じになり、確保がそのまま回数分積み上がる。
// こちらは作業配列を保持して使い回し、世代印で初期化を省く。
// また、件数での打ち切り（近い順に N 個で止める）は Compute には無い。
//
// 【世代印】
// 距離配列を毎回ゼロクリアすると頂点数に比例した時間がかかる。
// 探索ごとに世代番号を 1 つ進め、印が現世代と一致する頂点だけを
// 「この探索で触った」とみなすことで、クリアを不要にする。
//
// 【スレッド】
// 作業配列を持つためスレッド安全ではない。1 インスタンスを 1 スレッドから
// 使うこと。複数スレッドで使う場合はスレッドごとにインスタンスを作る。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// CSR 隣接グラフ上で、1 つの始点からのリンク距離（辺長の累積）を求める。
    /// 作業配列を使い回すため、同じグラフに対して繰り返し呼ぶ用途に向く。
    /// </summary>
    public sealed class LinkDistanceLocalSearch
    {
        private readonly int[]     _adjStart;   // 長さ n+1
        private readonly int[]     _adjList;    // 長さ = 有向辺数
        private readonly Vector3[] _positions;  // 長さ n

        private readonly float[] _dist;
        private readonly int[]   _visitStamp;
        private readonly int[]   _settleStamp;
        private int              _generation;

        private int[]   _heapIndex;
        private float[] _heapDist;
        private int     _heapSize;

        /// <summary>頂点数。</summary>
        public int VertexCount => _positions.Length;

        /// <summary>
        /// CSR 形式の隣接グラフから探索器を作る。渡した配列は保持されるため、
        /// 構築後に内容を変更しないこと。
        /// </summary>
        /// <param name="adjacencyStart">頂点 i の隣接は adjacencyList[adjacencyStart[i] .. adjacencyStart[i+1]) 。長さ n+1。</param>
        /// <param name="adjacencyList">隣接頂点索引の並び。</param>
        /// <param name="positions">頂点位置。辺長の算出に使う。長さ n。</param>
        public LinkDistanceLocalSearch(int[] adjacencyStart, int[] adjacencyList, Vector3[] positions)
        {
            if (adjacencyStart == null) throw new ArgumentNullException(nameof(adjacencyStart));
            if (adjacencyList  == null) throw new ArgumentNullException(nameof(adjacencyList));
            if (positions      == null) throw new ArgumentNullException(nameof(positions));
            if (adjacencyStart.Length != positions.Length + 1)
                throw new ArgumentException("adjacencyStart の長さは positions の要素数 + 1 であること");

            _adjStart  = adjacencyStart;
            _adjList   = adjacencyList;
            _positions = positions;

            int n = positions.Length;
            _dist        = new float[n];
            _visitStamp  = new int[n];
            _settleStamp = new int[n];
            _generation  = 0;

            int cap = Mathf.Max(16, Mathf.Min(n, 1024));
            _heapIndex = new int[cap];
            _heapDist  = new float[cap];
        }

        /// <summary>
        /// 始点から近い順に最大 maxCount 個の頂点を返す。結果はリンク距離の昇順で、
        /// 先頭は必ず始点自身（距離 0）。
        /// </summary>
        /// <returns>見つかった個数。</returns>
        public int SearchNearestCount(int start, int maxCount, List<int> result)
        {
            return Search(start, maxCount, float.PositiveInfinity, result);
        }

        /// <summary>
        /// 始点からのリンク距離が maxDistance 以下の頂点を、近い順に最大 maxCount 個返す。
        /// 結果はリンク距離の昇順で、先頭は必ず始点自身（距離 0）。
        /// </summary>
        /// <returns>見つかった個数。</returns>
        public int SearchWithinDistance(int start, float maxDistance, int maxCount, List<int> result)
        {
            if (!(maxDistance >= 0f)) return 0;
            return Search(start, maxCount, maxDistance, result);
        }

        /// <summary>
        /// 直前の探索で確定した頂点のリンク距離を返す。
        /// 確定していない頂点に対しては false。
        /// </summary>
        public bool TryGetLastDistance(int vertex, out float distance)
        {
            distance = 0f;
            if (vertex < 0 || vertex >= _positions.Length) return false;
            if (_settleStamp[vertex] != _generation) return false;
            distance = _dist[vertex];
            return true;
        }

        // ================================================================
        // 本体
        // ================================================================

        private int Search(int start, int maxCount, float maxDistance, List<int> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Clear();

            int n = _positions.Length;
            if (start < 0 || start >= n) return 0;
            if (maxCount <= 0) return 0;

            // 世代印の桁あふれ対策。あふれる直前に一度だけ全消去してやり直す
            if (_generation == int.MaxValue)
            {
                Array.Clear(_visitStamp,  0, _visitStamp.Length);
                Array.Clear(_settleStamp, 0, _settleStamp.Length);
                _generation = 0;
            }

            _generation++;
            _heapSize = 0;

            _dist[start]       = 0f;
            _visitStamp[start] = _generation;
            HeapPush(start, 0f);

            int settled = 0;

            while (_heapSize > 0)
            {
                HeapPop(out int current, out float d);

                // 遅延削除。取り出した値が最新でなければ捨てる
                if (_visitStamp[current] != _generation) continue;
                if (d > _dist[current]) continue;
                if (_settleStamp[current] == _generation) continue;

                _settleStamp[current] = _generation;
                result.Add(current);
                settled++;
                if (settled >= maxCount) break;

                Vector3 cp    = _positions[current];
                int     begin = _adjStart[current];
                int     end   = _adjStart[current + 1];

                for (int e = begin; e < end; e++)
                {
                    int next = _adjList[e];
                    if (next < 0 || next >= n) continue;
                    if (_settleStamp[next] == _generation) continue;

                    Vector3 np = _positions[next];
                    float dx = np.x - cp.x;
                    float dy = np.y - cp.y;
                    float dz = np.z - cp.z;
                    float newDist = d + Mathf.Sqrt(dx * dx + dy * dy + dz * dz);

                    if (newDist > maxDistance) continue;

                    if (_visitStamp[next] != _generation)
                    {
                        _visitStamp[next] = _generation;
                        _dist[next]       = newDist;
                        HeapPush(next, newDist);
                    }
                    else if (newDist < _dist[next])
                    {
                        _dist[next] = newDist;
                        HeapPush(next, newDist);
                    }
                }
            }

            return settled;
        }

        // ================================================================
        // 最小ヒープ（配列を使い回す）
        // ================================================================

        private void HeapPush(int index, float dist)
        {
            if (_heapSize >= _heapIndex.Length)
            {
                int newCap = _heapIndex.Length * 2;
                Array.Resize(ref _heapIndex, newCap);
                Array.Resize(ref _heapDist,  newCap);
            }

            int i = _heapSize++;
            _heapIndex[i] = index;
            _heapDist[i]  = dist;

            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (_heapDist[parent] <= _heapDist[i]) break;
                HeapSwap(i, parent);
                i = parent;
            }
        }

        private void HeapPop(out int index, out float dist)
        {
            index = _heapIndex[0];
            dist  = _heapDist[0];

            _heapSize--;
            if (_heapSize > 0)
            {
                _heapIndex[0] = _heapIndex[_heapSize];
                _heapDist[0]  = _heapDist[_heapSize];

                int i = 0;
                while (true)
                {
                    int left     = 2 * i + 1;
                    int right    = left + 1;
                    int smallest = i;

                    if (left  < _heapSize && _heapDist[left]  < _heapDist[smallest]) smallest = left;
                    if (right < _heapSize && _heapDist[right] < _heapDist[smallest]) smallest = right;
                    if (smallest == i) break;

                    HeapSwap(i, smallest);
                    i = smallest;
                }
            }
        }

        private void HeapSwap(int a, int b)
        {
            (_heapIndex[a], _heapIndex[b]) = (_heapIndex[b], _heapIndex[a]);
            (_heapDist[a],  _heapDist[b])  = (_heapDist[b],  _heapDist[a]);
        }
    }
}
