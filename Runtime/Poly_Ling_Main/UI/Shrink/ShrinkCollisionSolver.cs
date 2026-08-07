// ShrinkCollisionSolver.cs
// シュリンカー用 線分／三角形 交差ソルバ（一様グリッド）
// UnityEditor非依存
//
// 【座標系】
// 入力はすべてワールド座標。呼び出し側は GPU が計算したワールド座標
// （UnifiedBufferManager.GetDisplayPositions）を渡すこと。
// CPU 側でスキニング規則を再実装してはならない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.UI
{
    /// <summary>
    /// コライダー三角形群を一様グリッドに登録し、線分との最初の交差を求める。
    /// ビフォー→アフターの経路は固定なので、構築も交差計算も1回だけ行えばよい。
    /// </summary>
    public sealed class ShrinkCollisionSolver
    {
        // ================================================================
        // 三角形データ
        // ================================================================

        private readonly List<Vector3> _a = new List<Vector3>();
        private readonly List<Vector3> _b = new List<Vector3>();
        private readonly List<Vector3> _c = new List<Vector3>();

        public int TriangleCount => _a.Count;

        // ================================================================
        // グリッド
        // ================================================================

        private Vector3 _gridMin;
        private Vector3 _cellSize;
        private int _nx, _ny, _nz;
        private List<int>[] _cells;
        private bool _built;

        // 同一三角形の重複判定を避けるスタンプ
        private int[] _stamp;
        private int _queryId;

        private const float Eps = 1e-9f;

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
        // グリッド構築
        // ================================================================

        /// <summary>
        /// 一様グリッドを構築する。総当りは頂点数×三角形数になり実用にならないため必須。
        /// </summary>
        public void Build(int maxCells = 2097152)
        {
            _built = true;
            _cells = null;
            _stamp = null;
            _queryId = 0;

            int triCount = _a.Count;
            if (triCount == 0) return;

            Vector3 min = _a[0], max = _a[0];
            for (int i = 0; i < triCount; i++)
            {
                Expand(ref min, ref max, _a[i]);
                Expand(ref min, ref max, _b[i]);
                Expand(ref min, ref max, _c[i]);
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

            // セル総数の上限を超える場合は等比で縮める
            long total = (long)_nx * _ny * _nz;
            while (total > maxCells)
            {
                _nx = Mathf.Max(1, _nx / 2);
                _ny = Mathf.Max(1, _ny / 2);
                _nz = Mathf.Max(1, _nz / 2);
                total = (long)_nx * _ny * _nz;
                if (_nx == 1 && _ny == 1 && _nz == 1) break;
            }

            _gridMin = min;
            _cellSize = new Vector3(size.x / _nx, size.y / _ny, size.z / _nz);
            if (_cellSize.x <= 0f) _cellSize.x = 1f;
            if (_cellSize.y <= 0f) _cellSize.y = 1f;
            if (_cellSize.z <= 0f) _cellSize.z = 1f;

            _cells = new List<int>[_nx * _ny * _nz];
            _stamp = new int[triCount];

            for (int i = 0; i < triCount; i++)
            {
                Vector3 tmin = _a[i], tmax = _a[i];
                Expand(ref tmin, ref tmax, _b[i]);
                Expand(ref tmin, ref tmax, _c[i]);

                CellRange(tmin, tmax,
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
        // 交差判定
        // ================================================================

        /// <summary>
        /// 線分 p0→p1 とコライダー三角形の最初の交差を求める。
        /// frontFaceOnly=true のとき、進行方向に対して表を向いた面（法線と進行方向の
        /// 内積が負）だけを採用する。これにより「既に内側にある頂点が出口面で止まる」
        /// 誤動作を避ける。
        /// </summary>
        /// <param name="t">交差位置の線分パラメータ [0,1]</param>
        public bool RaycastSegment(Vector3 p0, Vector3 p1, bool frontFaceOnly, out float t)
        {
            t = 1f;
            if (!_built) Build();
            if (_cells == null || _a.Count == 0) return false;

            Vector3 dir = p1 - p0;
            float len = dir.magnitude;
            if (len < 1e-8f) return false;

            Vector3 tmin = Vector3.Min(p0, p1);
            Vector3 tmax = Vector3.Max(p0, p1);
            CellRange(tmin, tmax,
                out int x0, out int y0, out int z0,
                out int x1, out int y1, out int z1);

            _queryId++;
            bool hit = false;
            float best = float.MaxValue;

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

                    if (!IntersectTriangle(p0, dir, _a[ti], _b[ti], _c[ti], frontFaceOnly, out float u))
                        continue;
                    if (u < best) { best = u; hit = true; }
                }
            }

            if (hit) t = best;
            return hit;
        }

        /// <summary>
        /// Möller–Trumbore。dir は正規化しない（u はそのまま線分パラメータになる）。
        /// 面法線は NormalHelper.CalculateFaceNormal と同じ cross(p1-p0, p2-p0) 規則。
        /// </summary>
        private static bool IntersectTriangle(
            Vector3 origin, Vector3 dir,
            Vector3 v0, Vector3 v1, Vector3 v2,
            bool frontFaceOnly, out float u)
        {
            u = 0f;

            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;

            if (frontFaceOnly)
            {
                Vector3 n = Vector3.Cross(e1, e2);
                if (Vector3.Dot(n, dir) >= 0f) return false;
            }

            Vector3 pv = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, pv);
            if (det > -Eps && det < Eps) return false;

            float invDet = 1f / det;
            Vector3 tv = origin - v0;

            float bu = Vector3.Dot(tv, pv) * invDet;
            if (bu < 0f || bu > 1f) return false;

            Vector3 qv = Vector3.Cross(tv, e1);
            float bv = Vector3.Dot(dir, qv) * invDet;
            if (bv < 0f || bu + bv > 1f) return false;

            float tt = Vector3.Dot(e2, qv) * invDet;
            if (tt < 0f || tt > 1f) return false;

            u = tt;
            return true;
        }

        // ================================================================
        // 停止パラメータ算出
        // ================================================================

        /// <summary>
        /// 各頂点についてビフォー→アフターの線分上での停止パラメータ [0,1] を返す。
        /// 衝突しない頂点は 1（アフターまで到達）。
        /// surfaceOffset はコライダー面から手前に残す距離（ワールド単位）。
        /// </summary>
        public float[] ComputeStopParams(
            Vector3[] beforeWorld, Vector3[] afterWorld,
            float surfaceOffset, bool frontFaceOnly)
        {
            if (beforeWorld == null || afterWorld == null) return null;

            int count = Mathf.Min(beforeWorld.Length, afterWorld.Length);
            var result = new float[count];

            for (int i = 0; i < count; i++)
            {
                result[i] = 1f;

                Vector3 p0 = beforeWorld[i];
                Vector3 p1 = afterWorld[i];
                float len = (p1 - p0).magnitude;
                if (len < 1e-8f) continue;

                if (!RaycastSegment(p0, p1, frontFaceOnly, out float u)) continue;

                float back = surfaceOffset > 0f ? surfaceOffset / len : 0f;
                result[i] = Mathf.Clamp01(u - back);
            }

            return result;
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
