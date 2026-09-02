// ShrinkTriangleGrid.cs
// 静止三角形群の一様グリッド。AABB 範囲照会に答える。
// UnityEditor非依存
//
// 【座標系】
// 入力はすべてワールド座標。呼び出し側は GPU が計算したワールド座標
// （UnifiedBufferManager.GetDisplayPositions）を渡すこと。
// CPU 側でスキニング規則を再実装してはならない。
//
// 既存の ShrinkCollisionSolver も内部に同種のグリッドを持つが、あちらは
// 線分レイキャスト専用で外部照会の口が無い。既存経路の動作を変えないため、
// こちらは別実装として置く。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.UI
{
    public sealed class ShrinkTriangleGrid
    {
        // ================================================================
        // 三角形データ
        // ================================================================

        private readonly List<Vector3> _a = new List<Vector3>();
        private readonly List<Vector3> _b = new List<Vector3>();
        private readonly List<Vector3> _c = new List<Vector3>();

        // 三角形ごとの AABB（照会時の絞り込みに使う）
        private Vector3[] _triMin;
        private Vector3[] _triMax;

        public int TriangleCount => _a.Count;

        public Vector3 A(int i) => _a[i];
        public Vector3 B(int i) => _b[i];
        public Vector3 C(int i) => _c[i];

        public Vector3 TriMin(int i) => _triMin[i];
        public Vector3 TriMax(int i) => _triMax[i];

        // ================================================================
        // グリッド
        // ================================================================

        private Vector3 _gridMin;
        private Vector3 _cellSize;
        private int _nx, _ny, _nz;
        private List<int>[] _cells;
        private bool _built;

        // 同一三角形の重複返却を避けるスタンプ
        private int[] _stamp;
        private int _queryId;

        // ================================================================
        // 登録
        // ================================================================

        /// <summary>
        /// メッシュの全面を三角形に分解して登録する。
        /// worldPositions は頂点ローカルインデックス順のワールド座標。
        /// </summary>
        public void AddMesh(MeshObject mo, Vector3[] worldPositions)
        {
            if (mo == null || worldPositions == null) return;
            _built = false;

            int vc = Mathf.Min(mo.VertexCount, worldPositions.Length);

            foreach (var face in mo.Faces)
            {
                if (face == null || face.VertexCount < 3) continue;
                if (face.IsHidden) continue;

                var vi = face.VertexIndices;
                int i0 = vi[0];
                if (i0 < 0 || i0 >= vc) continue;

                // Face.ToTriangleIndices と同じ扇状分割
                for (int k = 1; k + 1 < vi.Count; k++)
                {
                    int i1 = vi[k];
                    int i2 = vi[k + 1];
                    if (i1 < 0 || i1 >= vc) continue;
                    if (i2 < 0 || i2 >= vc) continue;

                    _a.Add(worldPositions[i0]);
                    _b.Add(worldPositions[i1]);
                    _c.Add(worldPositions[i2]);
                }
            }
        }

        // ================================================================
        // 構築
        // ================================================================

        /// <summary>
        /// 一様グリッドを構築する。総当りは面数×面数になり実用にならないため必須。
        /// </summary>
        public void Build(int maxCells = 2097152)
        {
            _built   = true;
            _cells   = null;
            _stamp   = null;
            _triMin  = null;
            _triMax  = null;
            _queryId = 0;

            int triCount = _a.Count;
            if (triCount == 0) return;

            _triMin = new Vector3[triCount];
            _triMax = new Vector3[triCount];

            Vector3 min = _a[0], max = _a[0];
            for (int i = 0; i < triCount; i++)
            {
                Vector3 tmin = _a[i], tmax = _a[i];
                Expand(ref tmin, ref tmax, _b[i]);
                Expand(ref tmin, ref tmax, _c[i]);
                _triMin[i] = tmin;
                _triMax[i] = tmax;

                Expand(ref min, ref max, tmin);
                Expand(ref min, ref max, tmax);
            }

            Vector3 size = max - min;
            // 平面状のコライダーでも 0 除算しないよう最小幅を確保
            float pad = Mathf.Max(1e-4f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 1e-3f);
            min -= new Vector3(pad, pad, pad);
            max += new Vector3(pad, pad, pad);
            size = max - min;

            // 1セルあたり三角形数がおおよそ一定になる分割数
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Pow(triCount, 1f / 3f)));
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (longest <= 0f) longest = 1f;
            float cell = longest / n;

            _nx = Mathf.Clamp(Mathf.CeilToInt(size.x / cell), 1, 512);
            _ny = Mathf.Clamp(Mathf.CeilToInt(size.y / cell), 1, 512);
            _nz = Mathf.Clamp(Mathf.CeilToInt(size.z / cell), 1, 512);

            long total = (long)_nx * _ny * _nz;
            while (total > maxCells)
            {
                _nx = Mathf.Max(1, _nx / 2);
                _ny = Mathf.Max(1, _ny / 2);
                _nz = Mathf.Max(1, _nz / 2);
                total = (long)_nx * _ny * _nz;
                if (_nx == 1 && _ny == 1 && _nz == 1) break;
            }

            _gridMin  = min;
            _cellSize = new Vector3(size.x / _nx, size.y / _ny, size.z / _nz);
            if (_cellSize.x <= 0f) _cellSize.x = 1f;
            if (_cellSize.y <= 0f) _cellSize.y = 1f;
            if (_cellSize.z <= 0f) _cellSize.z = 1f;

            _cells = new List<int>[_nx * _ny * _nz];
            _stamp = new int[triCount];

            for (int i = 0; i < triCount; i++)
            {
                CellRange(_triMin[i], _triMax[i],
                    out int x0, out int y0, out int z0,
                    out int x1, out int y1, out int z1);

                for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int ci = (z * _ny + y) * _nx + x;
                    var list = _cells[ci];
                    if (list == null) { list = new List<int>(4); _cells[ci] = list; }
                    list.Add(i);
                }
            }
        }

        // ================================================================
        // 照会
        // ================================================================

        /// <summary>
        /// AABB [min, max] と重なるセルに登録されている三角形番号を result へ入れる。
        /// 三角形ごとの AABB でさらに絞り込むので、返るのは実際に AABB が重なるものだけ。
        /// result は呼び出し側で使い回すこと（毎回 Clear される）。
        /// </summary>
        public void Query(Vector3 min, Vector3 max, List<int> result)
        {
            if (result == null) return;
            result.Clear();

            if (!_built) Build();
            if (_cells == null || _a.Count == 0) return;

            CellRange(min, max,
                out int x0, out int y0, out int z0,
                out int x1, out int y1, out int z1);

            _queryId++;

            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var list = _cells[(z * _ny + y) * _nx + x];
                if (list == null) continue;

                for (int li = 0; li < list.Count; li++)
                {
                    int ti = list[li];
                    if (_stamp[ti] == _queryId) continue;
                    _stamp[ti] = _queryId;

                    Vector3 tmin = _triMin[ti];
                    Vector3 tmax = _triMax[ti];
                    if (tmax.x < min.x || tmin.x > max.x) continue;
                    if (tmax.y < min.y || tmin.y > max.y) continue;
                    if (tmax.z < min.z || tmin.z > max.z) continue;

                    result.Add(ti);
                }
            }
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private static void Expand(ref Vector3 min, ref Vector3 max, Vector3 p)
        {
            if (p.x < min.x) min.x = p.x; else if (p.x > max.x) max.x = p.x;
            if (p.y < min.y) min.y = p.y; else if (p.y > max.y) max.y = p.y;
            if (p.z < min.z) min.z = p.z; else if (p.z > max.z) max.z = p.z;
        }

        private void CellRange(
            Vector3 min, Vector3 max,
            out int x0, out int y0, out int z0,
            out int x1, out int y1, out int z1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt((min.x - _gridMin.x) / _cellSize.x), 0, _nx - 1);
            y0 = Mathf.Clamp(Mathf.FloorToInt((min.y - _gridMin.y) / _cellSize.y), 0, _ny - 1);
            z0 = Mathf.Clamp(Mathf.FloorToInt((min.z - _gridMin.z) / _cellSize.z), 0, _nz - 1);
            x1 = Mathf.Clamp(Mathf.FloorToInt((max.x - _gridMin.x) / _cellSize.x), 0, _nx - 1);
            y1 = Mathf.Clamp(Mathf.FloorToInt((max.y - _gridMin.y) / _cellSize.y), 0, _ny - 1);
            z1 = Mathf.Clamp(Mathf.FloorToInt((max.z - _gridMin.z) / _cellSize.z), 0, _nz - 1);
        }
    }
}
