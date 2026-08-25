// NormalPrismSolver.cs
// 法線移植用 プリズム（ビフォー三角形＋アフター三角形）ソルバ
// UnityEditor非依存
//
// 【座標系】
// 位置はすべてワールド座標。呼び出し側は GPU が計算したワールド座標
// （UnifiedBufferManager.GetDisplayPositions → PlayerViewportManager.TryGetMeshWorldPositions）
// を渡すこと。CPU 側でスキニング規則を再実装してはならない。
//
// 法線は MeshObject のローカル値しか存在しない（GPU 側に法線の読み戻し経路は無い。
// UnifiedBufferManager_Update.cs:1780 の _WorldNormalBuffer は _normalBuffer を
// そのまま束ねている）。よって呼び出し側が法線行列（WorldMatrix の逆転置）を渡し、
// 本クラスがワールド法線へ変換して保持する。スキニング無しが前提。
//
// 【プリズムの分割】
// ビフォーとアフターは同一トポロジ（面数・各面のコーナー数が一致）であることが前提。
// 各面を i0 / i_k / i_k+1 の扇で三角形化し（Face.ToTriangleIndices と同じ規則）、
// 対応する三角形ペアからプリズム（b0,b1,b2 / a0,a1,a2）を作る。
//
// プリズム1個は次の3つの四面体へ分割する。
//   {b0,b1,b2,a2} {b0,b1,a2,a1} {b0,a1,a2,a0}
// この分割はプリズムの側面（四角形）に対角線を1本ずつ引く。隣接プリズムと
// 対角線の取り方が食い違うと境界に微小な隙間・重なりが出るため、
// 底面3頂点のうち「頂点インデックスが最小のもの」を局所0番へ回転させ、
// さらに局所1番より局所2番のほうが小さければ 1↔2 / 4↔5 を入れ替える。
// この2段の並べ替えにより、各側面の対角線は必ず「頂点インデックスが小さいほうの
// 底辺端点」から引かれ、隣接プリズム間で一致する。
// それでも数値誤差までは吸収できないため、重心座標には許容値を設ける。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.UI
{
    /// <summary>
    /// ビフォー／アフターの三角形ペアが作るプリズム群を一様グリッドへ登録し、
    /// 任意のワールド座標に対して補間法線を返す。
    /// </summary>
    public sealed class NormalPrismSolver
    {
        // ================================================================
        // 補間モード
        // ================================================================

        public enum TriangleBlendMode
        {
            /// <summary>三角形内は重心座標による線形補間（加重和 → 正規化）</summary>
            Linear,
            /// <summary>三角形内は球面補間（Slerp を2段）</summary>
            Spherical,
        }

        // ================================================================
        // プリズム
        // ================================================================

        private struct Prism
        {
            public Vector3 B0, B1, B2;      // ビフォー三角形（ワールド座標）
            public Vector3 A0, A1, A2;      // アフター三角形（ワールド座標）
            public Vector3 NB0, NB1, NB2;   // ビフォー側コーナー法線（ワールド）
            public Vector3 NA0, NA1, NA2;   // アフター側コーナー法線（ワールド）
        }

        private Prism[] _prisms;
        private int _count;

        public int PrismCount => _count;

        // ================================================================
        // グリッド
        // ================================================================

        private Vector3 _gridMin;
        private Vector3 _cellSize;
        private int _nx, _ny, _nz;
        private List<int>[] _cells;

        private int[] _stamp;
        private int _queryId;

        /// <summary>重心座標の許容値。分割の対角線由来の微小な隙間を吸収する。</summary>
        private const float BaryTolerance = 1e-4f;

        private const float Eps = 1e-12f;

        // ================================================================
        // 構築
        // ================================================================

        /// <summary>
        /// ビフォー／アフターからプリズム群を作る。失敗時は null を返し error に理由を入れる。
        /// </summary>
        /// <param name="beforeWorld">ビフォーの頂点ワールド座標（頂点インデックス順）</param>
        /// <param name="beforeNormalMatrix">ビフォーの法線行列（WorldMatrix の逆転置）</param>
        /// <param name="afterWorld">アフターの頂点ワールド座標（頂点インデックス順）</param>
        /// <param name="afterNormalMatrix">アフターの法線行列（WorldMatrix の逆転置）</param>
        public static NormalPrismSolver Build(
            MeshObject beforeMesh, Vector3[] beforeWorld, Matrix4x4 beforeNormalMatrix,
            MeshObject afterMesh, Vector3[] afterWorld, Matrix4x4 afterNormalMatrix,
            out string error)
        {
            error = null;

            if (beforeMesh == null) { error = "ビフォーのメッシュがありません"; return null; }
            if (afterMesh == null) { error = "アフターのメッシュがありません"; return null; }
            if (beforeWorld == null) { error = "ビフォーのワールド座標を取得できません"; return null; }
            if (afterWorld == null) { error = "アフターのワールド座標を取得できません"; return null; }

            if (beforeMesh.Faces.Count != afterMesh.Faces.Count)
            {
                error = $"ビフォーとアフターの面数が一致しません（{beforeMesh.Faces.Count} / {afterMesh.Faces.Count}）";
                return null;
            }

            int bvc = Mathf.Min(beforeMesh.VertexCount, beforeWorld.Length);
            int avc = Mathf.Min(afterMesh.VertexCount, afterWorld.Length);

            var solver = new NormalPrismSolver();
            var list = new List<Prism>(beforeMesh.Faces.Count * 2);

            for (int fi = 0; fi < beforeMesh.Faces.Count; fi++)
            {
                var bf = beforeMesh.Faces[fi];
                var af = afterMesh.Faces[fi];
                if (bf == null || af == null) continue;

                if (bf.VertexCount != af.VertexCount)
                {
                    error = $"面 {fi} のコーナー数が一致しません（{bf.VertexCount} / {af.VertexCount}）";
                    return null;
                }

                if (bf.VertexCount < 3) continue;

                // 非表示面はシェルに含めない（ShrinkCollisionSolver.AddMesh と同じ扱い）
                if (bf.IsHidden || af.IsHidden) continue;

                Vector3 bFallback = NormalSmoothingOps.CalculateFaceNormalNewell(beforeMesh, bf);
                Vector3 aFallback = NormalSmoothingOps.CalculateFaceNormalNewell(afterMesh, af);

                // 扇分割（Face.ToTriangleIndices と同じ規則）
                for (int k = 1; k + 1 < bf.VertexCount; k++)
                {
                    int c0 = 0, c1 = k, c2 = k + 1;

                    int v0 = bf.VertexIndices[c0];
                    int v1 = bf.VertexIndices[c1];
                    int v2 = bf.VertexIndices[c2];
                    if (v0 < 0 || v0 >= bvc) continue;
                    if (v1 < 0 || v1 >= bvc) continue;
                    if (v2 < 0 || v2 >= bvc) continue;

                    int w0 = af.VertexIndices[c0];
                    int w1 = af.VertexIndices[c1];
                    int w2 = af.VertexIndices[c2];
                    if (w0 < 0 || w0 >= avc) continue;
                    if (w1 < 0 || w1 >= avc) continue;
                    if (w2 < 0 || w2 >= avc) continue;

                    // 対角線の取り方を隣接プリズムと揃えるための並べ替え
                    OrderCorners(v0, v1, v2, ref c0, ref c1, ref c2);

                    int bv0 = bf.VertexIndices[c0];
                    int bv1 = bf.VertexIndices[c1];
                    int bv2 = bf.VertexIndices[c2];
                    int av0 = af.VertexIndices[c0];
                    int av1 = af.VertexIndices[c1];
                    int av2 = af.VertexIndices[c2];

                    var p = new Prism
                    {
                        B0 = beforeWorld[bv0],
                        B1 = beforeWorld[bv1],
                        B2 = beforeWorld[bv2],
                        A0 = afterWorld[av0],
                        A1 = afterWorld[av1],
                        A2 = afterWorld[av2],

                        NB0 = TransformNormal(CornerNormal(beforeMesh, bf, c0, bFallback), beforeNormalMatrix),
                        NB1 = TransformNormal(CornerNormal(beforeMesh, bf, c1, bFallback), beforeNormalMatrix),
                        NB2 = TransformNormal(CornerNormal(beforeMesh, bf, c2, bFallback), beforeNormalMatrix),
                        NA0 = TransformNormal(CornerNormal(afterMesh, af, c0, aFallback), afterNormalMatrix),
                        NA1 = TransformNormal(CornerNormal(afterMesh, af, c1, aFallback), afterNormalMatrix),
                        NA2 = TransformNormal(CornerNormal(afterMesh, af, c2, aFallback), afterNormalMatrix),
                    };

                    list.Add(p);
                }
            }

            if (list.Count == 0)
            {
                error = "プリズムが0件です（面が無いか、すべて非表示です）";
                return null;
            }

            solver._prisms = list.ToArray();
            solver._count = list.Count;
            solver.BuildGrid();
            return solver;
        }

        /// <summary>
        /// 底面3頂点のうち頂点インデックス最小のものを局所0番へ回転させ、
        /// 局所2番のほうが局所1番より小さければ 1 と 2 を入れ替える。
        /// </summary>
        private static void OrderCorners(int v0, int v1, int v2, ref int c0, ref int c1, ref int c2)
        {
            // 回転（最小を先頭へ）
            if (v1 < v0 && v1 <= v2)
            {
                int t0 = c1, t1 = c2, t2 = c0;
                int u0 = v1, u1 = v2, u2 = v0;
                c0 = t0; c1 = t1; c2 = t2;
                v0 = u0; v1 = u1; v2 = u2;
            }
            else if (v2 < v0 && v2 <= v1)
            {
                int t0 = c2, t1 = c0, t2 = c1;
                int u0 = v2, u1 = v0, u2 = v1;
                c0 = t0; c1 = t1; c2 = t2;
                v0 = u0; v1 = u1; v2 = u2;
            }

            // 反転（局所1番と局所2番のうち小さいほうを1番へ）
            if (v2 < v1)
            {
                int t = c1; c1 = c2; c2 = t;
            }
        }

        /// <summary>面コーナーが参照するスロット法線。読めない場合は面法線で代用する。</summary>
        private static Vector3 CornerNormal(MeshObject mesh, Face face, int corner, Vector3 fallback)
        {
            if (corner < 0 || corner >= face.VertexCount) return fallback;

            int vi = face.VertexIndices[corner];
            if (vi < 0 || vi >= mesh.Vertices.Count) return fallback;

            var vertex = mesh.Vertices[vi];
            if (corner >= face.NormalIndices.Count) return fallback;

            int slot = face.NormalIndices[corner];
            if (slot < 0 || slot >= vertex.Normals.Count) return fallback;

            Vector3 n = vertex.Normals[slot];
            return n.sqrMagnitude < Eps ? fallback : n;
        }

        private static Vector3 TransformNormal(Vector3 localNormal, Matrix4x4 normalMatrix)
        {
            Vector3 n = normalMatrix.MultiplyVector(localNormal);
            return n.sqrMagnitude < Eps ? Vector3.up : n.normalized;
        }

        // ================================================================
        // グリッド構築
        // ================================================================

        private void BuildGrid()
        {
            _cells = null;
            _stamp = null;
            _queryId = 0;

            if (_count == 0) return;

            Vector3 min = _prisms[0].B0, max = _prisms[0].B0;
            for (int i = 0; i < _count; i++)
            {
                ref Prism p = ref _prisms[i];
                Expand(ref min, ref max, p.B0);
                Expand(ref min, ref max, p.B1);
                Expand(ref min, ref max, p.B2);
                Expand(ref min, ref max, p.A0);
                Expand(ref min, ref max, p.A1);
                Expand(ref min, ref max, p.A2);
            }

            Vector3 size = max - min;
            float pad = Mathf.Max(1e-4f, Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 1e-3f);
            min -= new Vector3(pad, pad, pad);
            max += new Vector3(pad, pad, pad);
            size = max - min;

            // 1セルあたりのプリズム数がおおよそ一定になる分割数
            int n = Mathf.Max(1, Mathf.CeilToInt(Mathf.Pow(_count, 1f / 3f)));
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (longest <= 0f) longest = 1f;
            float cell = longest / n;

            _nx = Mathf.Clamp(Mathf.CeilToInt(size.x / cell), 1, 256);
            _ny = Mathf.Clamp(Mathf.CeilToInt(size.y / cell), 1, 256);
            _nz = Mathf.Clamp(Mathf.CeilToInt(size.z / cell), 1, 256);

            long total = (long)_nx * _ny * _nz;
            while (total > 2097152)
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
            _stamp = new int[_count];

            for (int i = 0; i < _count; i++)
            {
                ref Prism p = ref _prisms[i];

                Vector3 tmin = p.B0, tmax = p.B0;
                Expand(ref tmin, ref tmax, p.B1);
                Expand(ref tmin, ref tmax, p.B2);
                Expand(ref tmin, ref tmax, p.A0);
                Expand(ref tmin, ref tmax, p.A1);
                Expand(ref tmin, ref tmax, p.A2);

                CellRange(tmin, tmax,
                    out int x0, out int y0, out int z0,
                    out int x1, out int y1, out int z1);

                for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int ci = (z * _ny + y) * _nx + x;
                    var cellList = _cells[ci];
                    if (cellList == null) { cellList = new List<int>(4); _cells[ci] = cellList; }
                    cellList.Add(i);
                }
            }
        }

        // ================================================================
        // 評価
        // ================================================================

        /// <summary>
        /// ワールド座標 p に対する補間法線を返す。
        /// inside は「どれかのプリズムに内包されていたか」。
        /// allowNearest=false かつ内包されていない場合は false を返す。
        /// </summary>
        public bool TryEvaluate(
            Vector3 p, TriangleBlendMode mode, bool allowNearest,
            out Vector3 worldNormal, out bool inside)
        {
            worldNormal = Vector3.up;
            inside = false;

            if (_cells == null || _count == 0) return false;

            // 内包しているセルだけを見る
            CellRange(p, p,
                out int x0, out int y0, out int z0,
                out int x1, out int y1, out int z1);

            _queryId++;
            float bestScore = float.NegativeInfinity;
            int bestPrism = -1;
            Vector3 bestParam = Vector3.zero;

            for (int z = z0; z <= z1; z++)
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                ScanCell(x, y, z, p, ref bestScore, ref bestPrism, ref bestParam);

            if (bestPrism >= 0 && bestScore >= -BaryTolerance)
            {
                inside = true;
                worldNormal = Evaluate(bestPrism, bestParam, mode);
                return true;
            }

            if (!allowNearest) return false;

            // 内包していない。中心セルから外へ殻を広げ、最初に見つかった段と
            // その1段外まで調べて最良（min λ が最大）のプリズムを採る。
            int cx = Mathf.Clamp(Mathf.FloorToInt((p.x - _gridMin.x) / _cellSize.x), 0, _nx - 1);
            int cy = Mathf.Clamp(Mathf.FloorToInt((p.y - _gridMin.y) / _cellSize.y), 0, _ny - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((p.z - _gridMin.z) / _cellSize.z), 0, _nz - 1);

            int maxRadius = Mathf.Max(_nx, Mathf.Max(_ny, _nz));
            int extra = -1;

            for (int r = 1; r <= maxRadius; r++)
            {
                bool scanned = ScanShell(cx, cy, cz, r, p, ref bestScore, ref bestPrism, ref bestParam);

                if (bestPrism >= 0)
                {
                    if (extra < 0) extra = r + 1;
                    if (r >= extra) break;
                }
                else if (!scanned && r >= maxRadius)
                {
                    break;
                }
            }

            if (bestPrism < 0) return false;

            worldNormal = Evaluate(bestPrism, ClampParam(bestParam), mode);
            return true;
        }

        /// <summary>指定セルのプリズムを評価する。</summary>
        private void ScanCell(
            int x, int y, int z, Vector3 p,
            ref float bestScore, ref int bestPrism, ref Vector3 bestParam)
        {
            if (x < 0 || x >= _nx || y < 0 || y >= _ny || z < 0 || z >= _nz) return;

            var list = _cells[(z * _ny + y) * _nx + x];
            if (list == null) return;

            for (int li = 0; li < list.Count; li++)
            {
                int pi = list[li];
                if (_stamp[pi] == _queryId) continue;
                _stamp[pi] = _queryId;

                if (!Locate(pi, p, out float score, out Vector3 param)) continue;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPrism = pi;
                    bestParam = param;
                }
            }
        }

        /// <summary>
        /// 中心セルから半径 r の殻（外周のみ）を走査する。1セルでも実体があれば true。
        /// 内側は前の段で走査済みなので、立方体を全走査せず外周だけを列挙する。
        /// </summary>
        private bool ScanShell(
            int cx, int cy, int cz, int r, Vector3 p,
            ref float bestScore, ref int bestPrism, ref Vector3 bestParam)
        {
            bool any = false;

            int x0 = cx - r, x1 = cx + r;
            int y0 = cy - r, y1 = cy + r;
            int z0 = cz - r, z1 = cz + r;

            for (int z = z0; z <= z1; z++)
            {
                bool zCap = (z == z0 || z == z1);

                for (int y = y0; y <= y1; y++)
                {
                    bool yCap = (y == y0 || y == y1);

                    if (zCap || yCap)
                    {
                        // 面まるごと（z の蓋、または y の縁）
                        for (int x = x0; x <= x1; x++)
                        {
                            if (x < 0 || x >= _nx || y < 0 || y >= _ny || z < 0 || z >= _nz) continue;
                            any = true;
                            ScanCell(x, y, z, p, ref bestScore, ref bestPrism, ref bestParam);
                        }
                        continue;
                    }

                    // 内側の行は左右の 2 セルだけ
                    for (int s = 0; s < 2; s++)
                    {
                        int x = (s == 0) ? x0 : x1;
                        if (x0 == x1 && s == 1) break;
                        if (x < 0 || x >= _nx || y < 0 || y >= _ny || z < 0 || z >= _nz) continue;
                        any = true;
                        ScanCell(x, y, z, p, ref bestScore, ref bestPrism, ref bestParam);
                    }
                }
            }

            return any;
        }

        /// <summary>
        /// プリズム pi の3四面体を調べ、最も内側の四面体のパラメータ (u,v,t) と
        /// その最小重心座標（内包の度合い。0以上なら内包）を返す。
        /// </summary>
        private bool Locate(int pi, Vector3 p, out float score, out Vector3 param)
        {
            score = float.NegativeInfinity;
            param = Vector3.zero;

            ref Prism pr = ref _prisms[pi];

            bool found = false;

            // {b0,b1,b2,a2}
            if (Barycentric(pr.B0, pr.B1, pr.B2, pr.A2, p, out Vector4 l0))
            {
                float s = Min4(l0);
                if (s > score)
                {
                    score = s;
                    param = l0.x * Pb0 + l0.y * Pb1 + l0.z * Pb2 + l0.w * Pa2;
                    found = true;
                }
            }

            // {b0,b1,a2,a1}
            if (Barycentric(pr.B0, pr.B1, pr.A2, pr.A1, p, out Vector4 l1))
            {
                float s = Min4(l1);
                if (s > score)
                {
                    score = s;
                    param = l1.x * Pb0 + l1.y * Pb1 + l1.z * Pa2 + l1.w * Pa1;
                    found = true;
                }
            }

            // {b0,a1,a2,a0}
            if (Barycentric(pr.B0, pr.A1, pr.A2, pr.A0, p, out Vector4 l2))
            {
                float s = Min4(l2);
                if (s > score)
                {
                    score = s;
                    param = l2.x * Pb0 + l2.y * Pa1 + l2.z * Pa2 + l2.w * Pa0;
                    found = true;
                }
            }

            return found;
        }

        // プリズムパラメータ空間における各コーナーの (u, v, t)
        private static readonly Vector3 Pb0 = new Vector3(0f, 0f, 0f);
        private static readonly Vector3 Pb1 = new Vector3(1f, 0f, 0f);
        private static readonly Vector3 Pb2 = new Vector3(0f, 1f, 0f);
        private static readonly Vector3 Pa0 = new Vector3(0f, 0f, 1f);
        private static readonly Vector3 Pa1 = new Vector3(1f, 0f, 1f);
        private static readonly Vector3 Pa2 = new Vector3(0f, 1f, 1f);

        /// <summary>
        /// 四面体 (q0,q1,q2,q3) における点 p の重心座標。退化していれば false。
        /// </summary>
        private static bool Barycentric(
            Vector3 q0, Vector3 q1, Vector3 q2, Vector3 q3, Vector3 p, out Vector4 lambda)
        {
            lambda = Vector4.zero;

            Vector3 e1 = q1 - q0;
            Vector3 e2 = q2 - q0;
            Vector3 e3 = q3 - q0;
            Vector3 d = p - q0;

            float det = Vector3.Dot(e1, Vector3.Cross(e2, e3));
            if (det > -Eps && det < Eps) return false;

            float inv = 1f / det;
            float u = Vector3.Dot(d, Vector3.Cross(e2, e3)) * inv;
            float v = Vector3.Dot(e1, Vector3.Cross(d, e3)) * inv;
            float w = Vector3.Dot(e1, Vector3.Cross(e2, d)) * inv;

            lambda = new Vector4(1f - u - v - w, u, v, w);
            return true;
        }

        private static float Min4(Vector4 v)
        {
            float m = v.x;
            if (v.y < m) m = v.y;
            if (v.z < m) m = v.z;
            if (v.w < m) m = v.w;
            return m;
        }

        private static Vector3 ClampParam(Vector3 param)
        {
            float u = Mathf.Clamp01(param.x);
            float v = Mathf.Clamp01(param.y);
            float sum = u + v;
            if (sum > 1f) { u /= sum; v /= sum; }
            return new Vector3(u, v, Mathf.Clamp01(param.z));
        }

        /// <summary>
        /// パラメータ (u,v,t) から法線を作る。
        /// 三角形内は mode に従い、ビフォー→アフター間は線形補間。
        /// </summary>
        private Vector3 Evaluate(int pi, Vector3 param, TriangleBlendMode mode)
        {
            ref Prism pr = ref _prisms[pi];

            float u = param.x;
            float v = param.y;
            float t = param.z;
            float w0 = 1f - u - v;

            Vector3 nb = BlendTriangle(pr.NB0, pr.NB1, pr.NB2, w0, u, v, mode);
            Vector3 na = BlendTriangle(pr.NA0, pr.NA1, pr.NA2, w0, u, v, mode);

            Vector3 n = Vector3.Lerp(nb, na, Mathf.Clamp01(t));
            return n.sqrMagnitude < Eps ? nb : n.normalized;
        }

        private static Vector3 BlendTriangle(
            Vector3 n0, Vector3 n1, Vector3 n2,
            float w0, float w1, float w2, TriangleBlendMode mode)
        {
            if (mode == TriangleBlendMode.Linear)
            {
                Vector3 sum = n0 * w0 + n1 * w1 + n2 * w2;
                return sum.sqrMagnitude < Eps ? n0 : sum.normalized;
            }

            // 球面補間。n0-n1 を先に混ぜ、その結果と n2 を混ぜる。
            float s01 = w0 + w1;
            Vector3 a = s01 > 1e-6f
                ? Vector3.Slerp(n0, n1, Mathf.Clamp01(w1 / s01))
                : n2;

            Vector3 r = Vector3.Slerp(a, n2, Mathf.Clamp01(w2));
            return r.sqrMagnitude < Eps ? n0 : r.normalized;
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
