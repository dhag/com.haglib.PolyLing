// SurfaceSnapOps.cs
// 「面に張り付け」の幾何計算。カメラ目線でリファレンス三角形の最前面ヒットを求める。
// UnityEditor非依存。
// Runtime/Poly_Ling_Main/Tools/VertexTools/SurfaceSnapTool_/ に配置
//
// 【ワールド座標について】
// 本ファイルは自前でスキニング／ワールド変換を計算しない。
// 呼び出し側が GPU の計算結果（PlayerViewportManager.TryGetMeshWorldPositions →
// UnifiedBufferManager.GetDisplayPositions）を渡すこと。
//
// 【投影の考え方】
// 視線レイは「カメラの投影図上の 1 点 (u,v)」と 1 対 1 に対応する。
//   正射影 … u = dot(p, right), v = dot(p, up)
//   透視   … d = p - camPos, e = dot(d, fwd) として u = dot(d,right)/e, v = dot(d,up)/e
// これで三角形の (u,v) 外接矩形を作り、2次元一様グリッドへ積む。
// 頂点は自分の (u,v) が入るセルだけを見ればよい。
//
// 【前面／背面】
// レイはカメラ側から飛ばし、パラメータ t が最小のヒットを採る。
// 頂点がもともとリファレンスの前にあっても後ろにあっても同じ面へ移るので、
// 前にあったものは後退し、後ろにあったものは前進する。
//
// 【既存実装を流用しない理由】
// ShrinkTriangleGrid は 3次元 AABB 照会専用で視線レイの口が無い。
// ShrinkCollisionSolver.IntersectTriangle は private かつ t を [0,1] に制限している
// （線分用）。どちらもこの用途には使えないため、レイ版をここに置く。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>リファレンスの裏面を対象にするか。</summary>
    public enum SurfaceSnapBackface
    {
        /// <summary>裏面も対象（既定）。最前面がどちら向きでも張り付く。</summary>
        Both = 0,

        /// <summary>表面のみ。視線と向かい合う面だけを対象にする。</summary>
        FrontOnly = 1,
    }

    /// <summary>
    /// 張り付けに使うカメラ。Player の PlayerViewport.Cam から値を写して渡す。
    /// ここに Unity の Camera を持ち込まないのは、Poly_Ling_Main を
    /// Player のビューポート実装から独立させるため。
    /// </summary>
    public struct SurfaceSnapCamera
    {
        /// <summary>正射影か。</summary>
        public bool IsOrthographic;

        /// <summary>カメラ位置（ワールド）。透視のときだけ使う。</summary>
        public Vector3 Position;

        /// <summary>視線方向（ワールド）。正規化していなくてよい。</summary>
        public Vector3 Forward;
    }

    /// <summary>
    /// リファレンス三角形群を「カメラの投影図」でバケット化し、
    /// 任意のワールド座標について最前面のヒット位置を返す。
    /// </summary>
    public sealed class SurfaceSnapProjector
    {
        // 三角形群
        private readonly List<Vector3> _a = new List<Vector3>();
        private readonly List<Vector3> _b = new List<Vector3>();
        private readonly List<Vector3> _c = new List<Vector3>();

        // カメラ基底
        private SurfaceSnapCamera _cam;
        private Vector3 _right, _up, _fwd;

        /// <summary>正射影で投影平面を置く視線方向の位置。全三角形より手前に置く。</summary>
        private float _orthoStart;

        // 三角形ごとの投影外接矩形
        private float[] _minU, _minV, _maxU, _maxV;
        private bool[]  _valid;

        // 2次元一様グリッド
        private float _gridMinU, _gridMinV, _cellU, _cellV;
        private int   _nu, _nv;
        private List<int>[] _cells;

        private bool _built;

        // Möller–Trumbore の行列式しきい値。ShrinkCollisionSolver.cs:46 と同値。
        private const float Eps = 1e-9f;

        /// <summary>透視でカメラ面より後ろとみなすしきい値。</summary>
        private const float FrontEps = 1e-6f;

        /// <summary>登録した三角形の総数。</summary>
        public int TriangleCount => _a.Count;

        /// <summary>投影できた三角形の数。</summary>
        public int ValidTriangleCount { get; private set; }

        /// <summary>カメラ面より後ろにかかるため除外した三角形の数。</summary>
        public int SkippedTriangleCount { get; private set; }

        // ================================================================
        // 登録
        // ================================================================

        /// <summary>
        /// メッシュの全面を三角形に分解して登録する。
        /// worldPositions は頂点ローカルインデックス順のワールド座標。
        /// 分割規則は ShrinkTriangleGrid.AddMesh（:65-94）と同じ扇状分割で、
        /// 非表示面は対象外にする。
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
        /// カメラを決めて投影グリッドを作る。カメラが変わったら作り直すこと。
        /// </summary>
        public void Build(SurfaceSnapCamera cam)
        {
            _built = true;
            _cam   = cam;
            _cells = null;
            ValidTriangleCount   = 0;
            SkippedTriangleCount = 0;

            Vector3 f = cam.Forward;
            if (f.sqrMagnitude <= Eps) f = Vector3.forward;
            _fwd = f.normalized;

            // 視線と平行にならない種を選んで正規直交基底を作る。
            Vector3 seed = Mathf.Abs(_fwd.y) > 0.999f ? Vector3.forward : Vector3.up;
            _right = Vector3.Cross(seed, _fwd).normalized;
            _up    = Vector3.Cross(_fwd, _right).normalized;

            int n = _a.Count;
            _minU  = new float[n];
            _minV  = new float[n];
            _maxU  = new float[n];
            _maxV  = new float[n];
            _valid = new bool[n];
            if (n == 0) return;

            // 正射影は投影平面を三角形群より手前へ下げる。
            // 平面が三角形の間にあると、奥側のヒットが負の t になって拾えない。
            if (_cam.IsOrthographic)
            {
                float minDepth = float.MaxValue;
                float maxDepth = float.MinValue;
                for (int i = 0; i < n; i++)
                {
                    float d0 = Vector3.Dot(_a[i], _fwd);
                    float d1 = Vector3.Dot(_b[i], _fwd);
                    float d2 = Vector3.Dot(_c[i], _fwd);
                    if (d0 < minDepth) minDepth = d0;
                    if (d1 < minDepth) minDepth = d1;
                    if (d2 < minDepth) minDepth = d2;
                    if (d0 > maxDepth) maxDepth = d0;
                    if (d1 > maxDepth) maxDepth = d1;
                    if (d2 > maxDepth) maxDepth = d2;
                }
                float span = Mathf.Max(1e-3f, maxDepth - minDepth);
                _orthoStart = minDepth - span;
            }

            bool  has   = false;
            float gMinU = 0f, gMinV = 0f, gMaxU = 0f, gMaxV = 0f;

            for (int i = 0; i < n; i++)
            {
                if (!Project(_a[i], out float u0, out float v0) ||
                    !Project(_b[i], out float u1, out float v1) ||
                    !Project(_c[i], out float u2, out float v2))
                {
                    _valid[i] = false;
                    SkippedTriangleCount++;
                    continue;
                }

                _valid[i] = true;
                ValidTriangleCount++;

                float lo0 = u0 < u1 ? u0 : u1; if (u2 < lo0) lo0 = u2;
                float hi0 = u0 > u1 ? u0 : u1; if (u2 > hi0) hi0 = u2;
                float lo1 = v0 < v1 ? v0 : v1; if (v2 < lo1) lo1 = v2;
                float hi1 = v0 > v1 ? v0 : v1; if (v2 > hi1) hi1 = v2;

                _minU[i] = lo0; _maxU[i] = hi0;
                _minV[i] = lo1; _maxV[i] = hi1;

                if (!has)
                {
                    gMinU = lo0; gMaxU = hi0;
                    gMinV = lo1; gMaxV = hi1;
                    has = true;
                }
                else
                {
                    if (lo0 < gMinU) gMinU = lo0;
                    if (hi0 > gMaxU) gMaxU = hi0;
                    if (lo1 < gMinV) gMinV = lo1;
                    if (hi1 > gMaxV) gMaxV = hi1;
                }
            }

            if (!has) return;

            // 平面状でも 0 除算しないよう最小幅を確保する。
            float padU = Mathf.Max(1e-6f, (gMaxU - gMinU) * 1e-3f);
            float padV = Mathf.Max(1e-6f, (gMaxV - gMinV) * 1e-3f);
            gMinU -= padU; gMaxU += padU;
            gMinV -= padV; gMaxV += padV;

            // 1セルあたり三角形数がおおよそ一定になる分割数
            int div = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(ValidTriangleCount)));
            _nu = Mathf.Clamp(div, 1, 512);
            _nv = Mathf.Clamp(div, 1, 512);

            _gridMinU = gMinU;
            _gridMinV = gMinV;
            _cellU    = (gMaxU - gMinU) / _nu;
            _cellV    = (gMaxV - gMinV) / _nv;
            if (_cellU <= 0f) _cellU = 1f;
            if (_cellV <= 0f) _cellV = 1f;

            _cells = new List<int>[_nu * _nv];

            for (int i = 0; i < n; i++)
            {
                if (!_valid[i]) continue;

                CellRange(_minU[i], _minV[i], _maxU[i], _maxV[i],
                    out int x0, out int y0, out int x1, out int y1);

                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int ci   = y * _nu + x;
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
        /// world を通る視線レイの最前面ヒット位置を返す。
        /// surfaceOffset は面からカメラ側へ残す距離（ワールド単位）。
        /// ヒットしなければ false を返し、result は world のまま。
        /// </summary>
        public bool TryProject(
            Vector3 world, float surfaceOffset, SurfaceSnapBackface backface,
            out Vector3 result)
        {
            result = world;

            if (!_built || _cells == null) return false;
            if (!Project(world, out float u, out float v)) return false;

            int cu = Mathf.FloorToInt((u - _gridMinU) / _cellU);
            int cv = Mathf.FloorToInt((v - _gridMinV) / _cellV);
            if (cu < 0 || cu >= _nu || cv < 0 || cv >= _nv) return false;

            var list = _cells[cv * _nu + cu];
            if (list == null || list.Count == 0) return false;

            MakeRay(u, v, out Vector3 origin, out Vector3 dir);

            bool  frontOnly = backface == SurfaceSnapBackface.FrontOnly;
            bool  hit       = false;
            float best      = float.MaxValue;

            for (int li = 0; li < list.Count; li++)
            {
                int ti = list[li];

                // セルは外接矩形で積んでいるので、点で当たっているかを先に見る。
                if (u < _minU[ti] || u > _maxU[ti]) continue;
                if (v < _minV[ti] || v > _maxV[ti]) continue;

                if (!IntersectTriangle(origin, dir, _a[ti], _b[ti], _c[ti], frontOnly, out float t))
                    continue;

                if (t < best) { best = t; hit = true; }
            }

            if (!hit) return false;

            result = origin + dir * (best - surfaceOffset);
            return true;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        /// <summary>ワールド座標を投影図の (u,v) にする。透視でカメラ面より後ろなら false。</summary>
        private bool Project(Vector3 p, out float u, out float v)
        {
            if (_cam.IsOrthographic)
            {
                u = Vector3.Dot(p, _right);
                v = Vector3.Dot(p, _up);
                return true;
            }

            Vector3 d = p - _cam.Position;
            float   e = Vector3.Dot(d, _fwd);
            if (e <= FrontEps) { u = 0f; v = 0f; return false; }

            u = Vector3.Dot(d, _right) / e;
            v = Vector3.Dot(d, _up)    / e;
            return true;
        }

        /// <summary>投影図の (u,v) を通る視線レイ。dir は正規化済みなので t は距離。</summary>
        private void MakeRay(float u, float v, out Vector3 origin, out Vector3 dir)
        {
            if (_cam.IsOrthographic)
            {
                origin = _right * u + _up * v + _fwd * _orthoStart;
                dir    = _fwd;
                return;
            }

            origin = _cam.Position;
            dir    = (_fwd + _right * u + _up * v).normalized;
        }

        private void CellRange(
            float minU, float minV, float maxU, float maxV,
            out int x0, out int y0, out int x1, out int y1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt((minU - _gridMinU) / _cellU), 0, _nu - 1);
            y0 = Mathf.Clamp(Mathf.FloorToInt((minV - _gridMinV) / _cellV), 0, _nv - 1);
            x1 = Mathf.Clamp(Mathf.FloorToInt((maxU - _gridMinU) / _cellU), 0, _nu - 1);
            y1 = Mathf.Clamp(Mathf.FloorToInt((maxV - _gridMinV) / _cellV), 0, _nv - 1);
        }

        /// <summary>
        /// Möller–Trumbore のレイ版。dir は正規化済み、t の上限は無い。
        /// 面法線は NormalHelper.CalculateFaceNormal と同じ cross(p1-p0, p2-p0) 規則。
        /// </summary>
        private static bool IntersectTriangle(
            Vector3 origin, Vector3 dir,
            Vector3 v0, Vector3 v1, Vector3 v2,
            bool frontFaceOnly, out float t)
        {
            t = 0f;

            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;

            if (frontFaceOnly)
            {
                Vector3 nrm = Vector3.Cross(e1, e2);
                if (Vector3.Dot(nrm, dir) >= 0f) return false;
            }

            Vector3 pv  = Vector3.Cross(dir, e2);
            float   det = Vector3.Dot(e1, pv);
            if (det > -Eps && det < Eps) return false;

            float   invDet = 1f / det;
            Vector3 tv     = origin - v0;

            float bu = Vector3.Dot(tv, pv) * invDet;
            if (bu < 0f || bu > 1f) return false;

            Vector3 qv = Vector3.Cross(tv, e1);
            float   bv = Vector3.Dot(dir, qv) * invDet;
            if (bv < 0f || bu + bv > 1f) return false;

            float tt = Vector3.Dot(e2, qv) * invDet;
            if (tt < 0f) return false;

            t = tt;
            return true;
        }
    }
}
