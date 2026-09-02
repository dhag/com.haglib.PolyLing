// Runtime/Poly_Ling_Main/Tools/Core/PointGrid3D.cs
// 静的な点群に対する一様格子。最近傍・k近傍・半径内の問い合わせを総当りより速く行う。
//
// 【構造】
// 立方体セルに点を振り分け、CSR（セル先頭索引＋要素配列）で保持する。
// 構築は計数ソートなので、点数に対して線形。構築後の点の追加・削除はできない。
//
// 【セル辺長の決め方】
// 1 セルあたりの平均点数が targetPointsPerCell 前後になるよう、
// 点群の広がりから決める。平面状・直線状の点群では 1 辺の広がりが 0 に
// なりうるため、最大辺の 1e-4 を下限としてから体積を求める。
// そのうえで総セル数が MaxCellCount を超えないようセル辺長を引き上げる。
//
// 【k近傍の打ち切り】
// 中心セルからチェビシェフ距離 r のシェルを順に見る。点 p は中心セルの
// 内側にあるので、シェル r に属するセルまでの距離は (r-1)*cellSize 以上。
// よって「まだ見ていないセル」までの距離は r*cellSize 以上になる。
// k 個そろっていて、かつ k 番目の距離が r*cellSize 以下なら、
// それ以上外側を見ても順位は変わらないので打ち切れる。
//
// 【スレッド】
// 構築後は読み取り専用なので、複数スレッドから同時に問い合わせてよい。
// ただし呼び出し側が渡すバッファは共有しないこと。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Tools
{
    /// <summary>静的な点群に対する一様格子。近傍探索に使う。</summary>
    public sealed class PointGrid3D
    {
        /// <summary>総セル数の上限。これを超えないようセル辺長を引き上げる。</summary>
        public const int MaxCellCount = 4000000;

        /// <summary>1 軸あたりのセル数の上限。</summary>
        public const int MaxCellPerAxis = 1024;

        private readonly Vector3 _min;
        private readonly float   _cellSize;
        private readonly float   _invCellSize;
        private readonly int     _nx, _ny, _nz;

        private readonly int[]     _cellStart;   // 長さ nx*ny*nz + 1
        private readonly int[]     _items;       // 長さ pointCount。セル順に並んだ点索引
        private readonly Vector3[] _points;      // 参照のみ。呼び出し側が変更しないこと

        /// <summary>格子に入っている点の数。</summary>
        public int PointCount => _points.Length;

        /// <summary>セルの 1 辺の長さ。</summary>
        public float CellSize => _cellSize;

        /// <summary>
        /// 点群から格子を構築する。points は構築後に変更しないこと。
        /// </summary>
        /// <param name="points">対象の点群。</param>
        /// <param name="targetPointsPerCell">1 セルあたりの目標点数。</param>
        public PointGrid3D(IReadOnlyList<Vector3> points, float targetPointsPerCell = 2f)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));

            int count = points.Count;
            _points = new Vector3[count];
            for (int i = 0; i < count; i++) _points[i] = points[i];

            if (count == 0)
            {
                _min         = Vector3.zero;
                _cellSize    = 1f;
                _invCellSize = 1f;
                _nx = _ny = _nz = 1;
                _cellStart = new int[2];
                _items     = Array.Empty<int>();
                return;
            }

            // ── 境界
            Vector3 min = _points[0];
            Vector3 max = _points[0];
            for (int i = 1; i < count; i++)
            {
                Vector3 p = _points[i];
                if (p.x < min.x) min.x = p.x; else if (p.x > max.x) max.x = p.x;
                if (p.y < min.y) min.y = p.y; else if (p.y > max.y) max.y = p.y;
                if (p.z < min.z) min.z = p.z; else if (p.z > max.z) max.z = p.z;
            }
            _min = min;

            float sx = max.x - min.x;
            float sy = max.y - min.y;
            float sz = max.z - min.z;
            float maxExtent = Mathf.Max(sx, Mathf.Max(sy, sz));
            if (!(maxExtent > 0f)) maxExtent = 1f;

            // 平面状・直線状でも体積が 0 にならないよう下限を入れる
            float floor = maxExtent * 1.0e-4f;
            double ex = Mathf.Max(sx, floor);
            double ey = Mathf.Max(sy, floor);
            double ez = Mathf.Max(sz, floor);

            if (targetPointsPerCell < 0.25f) targetPointsPerCell = 0.25f;
            double targetCells = count / (double)targetPointsPerCell;
            if (targetCells < 1.0) targetCells = 1.0;

            double cell = Math.Pow(ex * ey * ez / targetCells, 1.0 / 3.0);
            if (!(cell > 0.0) || double.IsNaN(cell) || double.IsInfinity(cell)) cell = maxExtent;

            // 1 軸あたりのセル数を抑える
            cell = Math.Max(cell, ex / MaxCellPerAxis);
            cell = Math.Max(cell, ey / MaxCellPerAxis);
            cell = Math.Max(cell, ez / MaxCellPerAxis);

            // 総セル数を抑える。cell を k 倍するとセル数は 1/k^3 になる
            for (int guard = 0; guard < 64; guard++)
            {
                long nx = DimOf(ex, cell);
                long ny = DimOf(ey, cell);
                long nz = DimOf(ez, cell);
                if (nx * ny * nz <= MaxCellCount) break;

                double ratio = (nx * (double)ny * nz) / MaxCellCount;
                cell *= Math.Max(1.05, Math.Pow(ratio, 1.0 / 3.0));
            }

            _cellSize    = (float)cell;
            _invCellSize = 1f / _cellSize;
            _nx = (int)DimOf(ex, cell);
            _ny = (int)DimOf(ey, cell);
            _nz = (int)DimOf(ez, cell);

            // ── 計数ソートで CSR を作る
            int cellCount = _nx * _ny * _nz;
            _cellStart = new int[cellCount + 1];
            _items     = new int[count];

            int[] cellOf = new int[count];
            for (int i = 0; i < count; i++)
            {
                int c = CellIndexOf(_points[i]);
                cellOf[i] = c;
                _cellStart[c + 1]++;
            }
            for (int c = 0; c < cellCount; c++) _cellStart[c + 1] += _cellStart[c];

            int[] cursor = new int[cellCount];
            for (int i = 0; i < count; i++)
            {
                int c = cellOf[i];
                _items[_cellStart[c] + cursor[c]] = i;
                cursor[c]++;
            }
        }

        private static long DimOf(double extent, double cell)
        {
            long n = (long)Math.Ceiling(extent / cell);
            if (n < 1) n = 1;
            if (n > MaxCellPerAxis) n = MaxCellPerAxis;
            return n;
        }

        // ================================================================
        // 問い合わせ
        // ================================================================

        /// <summary>
        /// 最も近い点の索引を返す。点が 1 つも無ければ -1。
        /// </summary>
        public int FindNearest(Vector3 p)
        {
            if (_points.Length == 0) return -1;

            int[]   idx = new int[1];
            float[] d2  = new float[1];
            int n = FindKNearest(p, 1, ref idx, ref d2);
            return n > 0 ? idx[0] : -1;
        }

        /// <summary>
        /// 近い順に最大 k 個の点を返す。結果は距離の昇順。
        /// </summary>
        /// <param name="p">問い合わせ点。</param>
        /// <param name="k">求める個数。</param>
        /// <param name="indices">結果の索引。長さが足りなければ確保し直す。</param>
        /// <param name="sqrDistances">結果の二乗距離。indices と同じ扱い。</param>
        /// <returns>実際に見つかった個数。</returns>
        public int FindKNearest(Vector3 p, int k, ref int[] indices, ref float[] sqrDistances)
        {
            if (k <= 0 || _points.Length == 0) return 0;
            if (k > _points.Length) k = _points.Length;

            EnsureCapacity(ref indices, ref sqrDistances, k);

            // 二乗距離の最大ヒープ。根が「今の k 位」
            int heapCount = 0;

            int cx = CellCoord(p.x - _min.x, _nx);
            int cy = CellCoord(p.y - _min.y, _ny);
            int cz = CellCoord(p.z - _min.z, _nz);

            int maxRing = Math.Max(
                Math.Max(Math.Max(cx, _nx - 1 - cx), Math.Max(cy, _ny - 1 - cy)),
                Math.Max(cz, _nz - 1 - cz));

            for (int r = 0; r <= maxRing; r++)
            {
                VisitShell(cx, cy, cz, r, p, k, indices, sqrDistances, ref heapCount);

                if (heapCount >= k)
                {
                    // 未走査のセルまでの距離は r*cellSize 以上
                    float bound = r * _cellSize;
                    if (sqrDistances[0] <= bound * bound) break;
                }
            }

            HeapSortAscending(indices, sqrDistances, heapCount);
            return heapCount;
        }

        /// <summary>
        /// 半径 radius 以内の点を近い順に最大 maxCount 個返す。結果は距離の昇順。
        /// </summary>
        /// <param name="p">問い合わせ点。</param>
        /// <param name="radius">半径。0 以下なら 0 個。</param>
        /// <param name="maxCount">返す個数の上限。超えた分は遠い方から捨てる。</param>
        /// <param name="indices">結果の索引。長さが足りなければ確保し直す。</param>
        /// <param name="sqrDistances">結果の二乗距離。</param>
        /// <returns>実際に見つかった個数。</returns>
        public int FindWithinRadius(
            Vector3 p, float radius, int maxCount, ref int[] indices, ref float[] sqrDistances)
        {
            if (radius <= 0f || maxCount <= 0 || _points.Length == 0) return 0;
            if (maxCount > _points.Length) maxCount = _points.Length;

            EnsureCapacity(ref indices, ref sqrDistances, maxCount);

            float r2 = radius * radius;
            int heapCount = 0;

            int x0 = CellCoord(p.x - radius - _min.x, _nx);
            int x1 = CellCoord(p.x + radius - _min.x, _nx);
            int y0 = CellCoord(p.y - radius - _min.y, _ny);
            int y1 = CellCoord(p.y + radius - _min.y, _ny);
            int z0 = CellCoord(p.z - radius - _min.z, _nz);
            int z1 = CellCoord(p.z + radius - _min.z, _nz);

            for (int iz = z0; iz <= z1; iz++)
            {
                int baseZ = iz * _ny;
                for (int iy = y0; iy <= y1; iy++)
                {
                    int baseY = (baseZ + iy) * _nx;
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        VisitCell(baseY + ix, p, maxCount, r2, indices, sqrDistances, ref heapCount);
                    }
                }
            }

            HeapSortAscending(indices, sqrDistances, heapCount);
            return heapCount;
        }

        // ================================================================
        // 内部
        // ================================================================

        private static void EnsureCapacity(ref int[] indices, ref float[] sqrDistances, int need)
        {
            if (indices == null || indices.Length < need) indices = new int[need];
            if (sqrDistances == null || sqrDistances.Length < need) sqrDistances = new float[need];
        }

        private int CellCoord(float offset, int dim)
        {
            int c = (int)Mathf.Floor(offset * _invCellSize);
            if (c < 0) return 0;
            if (c >= dim) return dim - 1;
            return c;
        }

        private int CellIndexOf(Vector3 p)
        {
            int ix = CellCoord(p.x - _min.x, _nx);
            int iy = CellCoord(p.y - _min.y, _ny);
            int iz = CellCoord(p.z - _min.z, _nz);
            return (iz * _ny + iy) * _nx + ix;
        }

        /// <summary>中心セルからチェビシェフ距離がちょうど r のセルをすべて見る。</summary>
        private void VisitShell(
            int cx, int cy, int cz, int r, Vector3 p, int k,
            int[] indices, float[] sqrDistances, ref int heapCount)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                int ix = cx + dx;
                if (ix < 0 || ix >= _nx) continue;
                bool edgeX = (dx == -r || dx == r);

                for (int dy = -r; dy <= r; dy++)
                {
                    int iy = cy + dy;
                    if (iy < 0 || iy >= _ny) continue;
                    bool edgeXY = edgeX || (dy == -r || dy == r);

                    if (edgeXY)
                    {
                        for (int dz = -r; dz <= r; dz++)
                        {
                            int iz = cz + dz;
                            if (iz < 0 || iz >= _nz) continue;
                            VisitCell((iz * _ny + iy) * _nx + ix, p, k,
                                      float.PositiveInfinity, indices, sqrDistances, ref heapCount);
                        }
                    }
                    else
                    {
                        int izLow = cz - r;
                        if (izLow >= 0 && izLow < _nz)
                        {
                            VisitCell((izLow * _ny + iy) * _nx + ix, p, k,
                                      float.PositiveInfinity, indices, sqrDistances, ref heapCount);
                        }
                        if (r > 0)
                        {
                            int izHigh = cz + r;
                            if (izHigh >= 0 && izHigh < _nz)
                            {
                                VisitCell((izHigh * _ny + iy) * _nx + ix, p, k,
                                          float.PositiveInfinity, indices, sqrDistances, ref heapCount);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>1 セル分の点を最大ヒープへ入れる。</summary>
        private void VisitCell(
            int cell, Vector3 p, int k, float maxSqrDistance,
            int[] indices, float[] sqrDistances, ref int heapCount)
        {
            int begin = _cellStart[cell];
            int end   = _cellStart[cell + 1];

            for (int t = begin; t < end; t++)
            {
                int pi = _items[t];
                Vector3 q = _points[pi];
                float dx = q.x - p.x;
                float dy = q.y - p.y;
                float dz = q.z - p.z;
                float d2 = dx * dx + dy * dy + dz * dz;

                if (d2 > maxSqrDistance) continue;

                if (heapCount < k)
                {
                    indices[heapCount]      = pi;
                    sqrDistances[heapCount] = d2;
                    heapCount++;
                    SiftUp(indices, sqrDistances, heapCount - 1);
                }
                else if (d2 < sqrDistances[0])
                {
                    indices[0]      = pi;
                    sqrDistances[0] = d2;
                    SiftDown(indices, sqrDistances, 0, heapCount);
                }
            }
        }

        // ── 二乗距離の最大ヒープ

        private static void SiftUp(int[] idx, float[] d2, int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (d2[parent] >= d2[i]) break;
                Swap(idx, d2, i, parent);
                i = parent;
            }
        }

        private static void SiftDown(int[] idx, float[] d2, int i, int count)
        {
            while (true)
            {
                int left    = 2 * i + 1;
                int right   = left + 1;
                int largest = i;

                if (left  < count && d2[left]  > d2[largest]) largest = left;
                if (right < count && d2[right] > d2[largest]) largest = right;
                if (largest == i) break;

                Swap(idx, d2, i, largest);
                i = largest;
            }
        }

        /// <summary>最大ヒープをその場で昇順に並べ替える（ヒープソート）。</summary>
        private static void HeapSortAscending(int[] idx, float[] d2, int count)
        {
            for (int end = count - 1; end > 0; end--)
            {
                Swap(idx, d2, 0, end);
                SiftDown(idx, d2, 0, end);
            }
        }

        private static void Swap(int[] idx, float[] d2, int a, int b)
        {
            (idx[a], idx[b]) = (idx[b], idx[a]);
            (d2[a],  d2[b])  = (d2[b],  d2[a]);
        }
    }
}
