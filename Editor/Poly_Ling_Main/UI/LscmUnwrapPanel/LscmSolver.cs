// Assets/Editor/Poly_Ling_Main/UI/LscmUnwrapPanel/LscmSolver.cs
// LSCM（Least Squares Conformal Maps）UV展開ソルバ
// SeamSplitter: Corner単位でSeamを処理しUV用頂点を最小限分裂
// LscmSolver:   疎行列組み立て + CG法でUV座標を算出

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.UI.Lscm
{
    // ================================================================
    // Union-Find（Disjoint Set Union）
    // ================================================================

    /// <summary>
    /// 経路圧縮+ランクによるUnion-Find
    /// </summary>
    public sealed class UnionFind
    {
        private readonly int[] _parent;
        private readonly int[] _rank;

        public UnionFind(int n)
        {
            _parent = new int[n];
            _rank = new int[n];
            for (int i = 0; i < n; i++) _parent[i] = i;
        }

        public int Find(int x)
        {
            while (_parent[x] != x)
            {
                _parent[x] = _parent[_parent[x]];
                x = _parent[x];
            }
            return x;
        }

        public void Union(int x, int y)
        {
            x = Find(x); y = Find(y);
            if (x == y) return;
            if (_rank[x] < _rank[y]) _parent[x] = y;
            else if (_rank[x] > _rank[y]) _parent[y] = x;
            else { _parent[y] = x; _rank[x]++; }
        }
    }

    // ================================================================
    // SeamSplitter
    // ================================================================

    /// <summary>
    /// Corner（face-vertex）単位でSeamを処理し、UV展開用の三角形メッシュを生成する。
    /// N-gon面は扇形分割で三角形化する。
    /// Seamでないエッジのcornerはunion、Seamエッジはunion禁止。
    /// </summary>
    public static class SeamSplitter
    {
        /// <summary>
        /// Seam分割結果
        /// </summary>
        public class SplitResult
        {
            /// <summary>UV用頂点の3D位置（分裂後）</summary>
            public Vector3[] Positions;

            /// <summary>三角形インデックス（UV用頂点インデックス）</summary>
            public int[] Triangles;

            /// <summary>UV用頂点数</summary>
            public int VertexCount;

            /// <summary>三角形数</summary>
            public int TriangleCount;

            /// <summary>UV用頂点→元MeshObject頂点インデックス</summary>
            public int[] UvToOrigVertex;

            /// <summary>corner→UV用頂点インデックス</summary>
            public int[] CornerToUvVertex;

            /// <summary>アイランド数</summary>
            public int IslandCount;

            /// <summary>各UV頂点のアイランドID</summary>
            public int[] VertexIslandId;

            /// <summary>
            /// 三角形cornerの元face情報 (triIdx → origFaceIndex)
            /// </summary>
            public int[] TriToOrigFace;

            /// <summary>
            /// 三角形cornerの元face内ローカルインデックス
            /// triIdx*3+local → origFace内の頂点ローカルインデックス
            /// </summary>
            public int[] TriCornerToFaceLocal;
        }

        /// <summary>
        /// MeshObjectとSeamエッジ集合から、UV展開用の三角形メッシュを生成する。
        /// </summary>
        /// <param name="meshObj">対象メッシュ</param>
        /// <param name="seamEdges">Seamとして指定されたエッジ（VertexPair）</param>
        /// <param name="includeBoundaryAsSeam">境界エッジもSeam扱いにするか</param>
        public static SplitResult Build(MeshObject meshObj, HashSet<VertexPair> seamEdges, bool includeBoundaryAsSeam)
        {
            // --- Step1: N-gon→三角形に分解し、corner配列を構築 ---
            var triVerts = new List<int>();    // 三角形の頂点インデックス（元MeshObject内）
            var triFaces = new List<int>();    // 三角形→元Face index
            var triLocals = new List<int>();   // corner→元Face内ローカルindex

            for (int fi = 0; fi < meshObj.Faces.Count; fi++)
            {
                var face = meshObj.Faces[fi];
                if (face == null || face.VertexCount < 3) continue;

                // 扇形分割: v0を中心に (v0,v1,v2), (v0,v2,v3), ...
                for (int k = 1; k < face.VertexCount - 1; k++)
                {
                    triVerts.Add(face.VertexIndices[0]);
                    triVerts.Add(face.VertexIndices[k]);
                    triVerts.Add(face.VertexIndices[k + 1]);

                    triFaces.Add(fi);
                    triFaces.Add(fi);
                    triFaces.Add(fi);

                    triLocals.Add(0);
                    triLocals.Add(k);
                    triLocals.Add(k + 1);
                }
            }

            int triCount = triVerts.Count / 3;
            int cornerCount = triVerts.Count; // = triCount * 3

            // --- Step2: エッジ→半辺マップ構築 & 境界エッジ検出 ---
            // key: VertexPair, value: List<corner index>（その辺を"from"とするcorner）
            var edgeCorners = new Dictionary<VertexPair, List<int>>();

            for (int c = 0; c < cornerCount; c++)
            {
                int tri = c / 3;
                int local = c % 3;
                int nextLocal = (local + 1) % 3;
                int fromV = triVerts[c];
                int toV = triVerts[tri * 3 + nextLocal];

                var edge = new VertexPair(fromV, toV);
                if (!edgeCorners.TryGetValue(edge, out var list))
                {
                    list = new List<int>(2);
                    edgeCorners[edge] = list;
                }
                list.Add(c);
            }

            // 境界エッジ = 片側だけの半辺を持つエッジ
            HashSet<VertexPair> boundaryEdges = null;
            if (includeBoundaryAsSeam)
            {
                boundaryEdges = new HashSet<VertexPair>();
                foreach (var kv in edgeCorners)
                {
                    if (kv.Value.Count == 1)
                        boundaryEdges.Add(kv.Key);
                }
            }

            // --- Step3: Union-Find でcornerを結合 ---
            var uf = new UnionFind(cornerCount);

            // 同一エッジを共有するcornerペアについて、Seamでなければunion
            foreach (var kv in edgeCorners)
            {
                var edge = kv.Key;
                var corners = kv.Value;
                if (corners.Count < 2) continue; // 境界エッジ → union相手がいない

                bool isSeam = seamEdges.Contains(edge);
                if (!isSeam && includeBoundaryAsSeam && boundaryEdges != null && boundaryEdges.Contains(edge))
                    isSeam = true;

                if (isSeam) continue; // Seam → union禁止

                // 共有エッジの両側cornerをunion
                // 同じエッジを持つcorner同士で、同じ頂点を参照するものをunion
                for (int i = 0; i < corners.Count; i++)
                {
                    for (int j = i + 1; j < corners.Count; j++)
                    {
                        int ci = corners[i];
                        int cj = corners[j];

                        // このエッジのfrom側頂点が同じcorner同士をunion
                        int triI = ci / 3, localI = ci % 3;
                        int triJ = cj / 3, localJ = cj % 3;

                        // エッジ (fromV, toV) の両頂点について、対応するcornerをunion
                        UnionVertexCorners(uf, triVerts,
                            triI, localI, triJ, localJ, edge);
                    }
                }
            }

            // 同じ三角形内の同一エッジを共有しない頂点はunion不要
            // → エッジベースのunionで十分（同じ頂点を参照するcornerは、
            //   そのエッジを共有する三角形ペアを通じて結合される）

            // --- Step4: 代表→UV頂点番号を振り直し ---
            var repToUvIdx = new Dictionary<int, int>();
            var uvPositions = new List<Vector3>();
            var uvToOrig = new List<int>();
            var cornerToUv = new int[cornerCount];

            for (int c = 0; c < cornerCount; c++)
            {
                int rep = uf.Find(c);
                if (!repToUvIdx.TryGetValue(rep, out int uvIdx))
                {
                    uvIdx = uvPositions.Count;
                    repToUvIdx[rep] = uvIdx;

                    int origV = triVerts[c];
                    uvPositions.Add(meshObj.Vertices[origV].Position);
                    uvToOrig.Add(origV);
                }
                cornerToUv[c] = uvIdx;
            }

            // --- Step5: 三角形インデックスを再構築 ---
            var newTris = new int[cornerCount];
            for (int c = 0; c < cornerCount; c++)
                newTris[c] = cornerToUv[c];

            // --- Step6: アイランド検出（連結成分） ---
            int uvVertCount = uvPositions.Count;
            var islandUf = new UnionFind(uvVertCount);

            for (int t = 0; t < triCount; t++)
            {
                int v0 = newTris[t * 3 + 0];
                int v1 = newTris[t * 3 + 1];
                int v2 = newTris[t * 3 + 2];
                islandUf.Union(v0, v1);
                islandUf.Union(v1, v2);
            }

            var islandMap = new Dictionary<int, int>();
            var vertIsland = new int[uvVertCount];
            int islandCount = 0;
            for (int i = 0; i < uvVertCount; i++)
            {
                int rep = islandUf.Find(i);
                if (!islandMap.TryGetValue(rep, out int id))
                {
                    id = islandCount++;
                    islandMap[rep] = id;
                }
                vertIsland[i] = id;
            }

            // --- 元Faceへのマッピング ---
            var triToOrigFace = new int[triCount];
            for (int t = 0; t < triCount; t++)
                triToOrigFace[t] = triFaces[t * 3]; // 同じ三角形の3cornerは同じFace由来

            return new SplitResult
            {
                Positions = uvPositions.ToArray(),
                Triangles = newTris,
                VertexCount = uvVertCount,
                TriangleCount = triCount,
                UvToOrigVertex = uvToOrig.ToArray(),
                CornerToUvVertex = cornerToUv,
                IslandCount = islandCount,
                VertexIslandId = vertIsland,
                TriToOrigFace = triToOrigFace,
                TriCornerToFaceLocal = triLocals.ToArray(),
            };
        }

        /// <summary>
        /// 共有エッジの両頂点について、対応するcornerをunionする。
        /// </summary>
        private static void UnionVertexCorners(
            UnionFind uf, List<int> triVerts,
            int triI, int localI, int triJ, int localJ,
            VertexPair edge)
        {
            // 三角形Iのcornerで、edge.V1を参照するものを探す
            for (int li = 0; li < 3; li++)
            {
                int vi = triVerts[triI * 3 + li];
                if (vi != edge.V1 && vi != edge.V2) continue;

                for (int lj = 0; lj < 3; lj++)
                {
                    int vj = triVerts[triJ * 3 + lj];
                    if (vi == vj)
                    {
                        uf.Union(triI * 3 + li, triJ * 3 + lj);
                    }
                }
            }
        }
    }

    // ================================================================
    // 対称疎行列（下三角保持）
    // ================================================================

    internal sealed class SymSparse
    {
        private readonly int _n;
        private readonly Dictionary<long, double> _data = new();

        public SymSparse(int n) { _n = n; }
        public int Size => _n;

        public void Add(int i, int j, double v)
        {
            if (Math.Abs(v) < 1e-30) return;
            if (i < j) (i, j) = (j, i);
            long key = ((long)i << 32) | (uint)j;
            if (_data.TryGetValue(key, out var cur))
                _data[key] = cur + v;
            else
                _data[key] = v;
        }

        public void Mul(double[] x, double[] y)
        {
            Array.Clear(y, 0, y.Length);
            foreach (var kv in _data)
            {
                long key = kv.Key;
                double val = kv.Value;
                int i = (int)(key >> 32);
                int j = (int)(key & 0xffffffff);

                y[i] += val * x[j];
                if (i != j) y[j] += val * x[i];
            }
        }
    }

    // ================================================================
    // LscmSolver
    // ================================================================

    /// <summary>
    /// LSCM（Least Squares Conformal Maps）ソルバ。
    /// SeamSplitterの出力を受け取り、各アイランドごとにUV座標を計算する。
    /// </summary>
    public static class LscmSolver
    {
        /// <summary>
        /// LSCM展開結果
        /// </summary>
        public class LscmResult
        {
            /// <summary>各UV頂点のUV座標</summary>
            public Vector2[] UVs;

            /// <summary>成功したか</summary>
            public bool Success;

            /// <summary>エラーメッセージ（失敗時）</summary>
            public string Error;

            /// <summary>アイランド数</summary>
            public int IslandCount;
        }

        /// <summary>
        /// LSCM展開を実行する。
        /// </summary>
        /// <param name="split">SeamSplitterの出力</param>
        /// <param name="maxIters">CG法の最大反復数</param>
        /// <param name="tolerance">CG法の収束判定閾値</param>
        public static LscmResult Solve(SeamSplitter.SplitResult split, int maxIters = 3000, double tolerance = 1e-10)
        {
            if (split.TriangleCount == 0)
                return new LscmResult { Success = false, Error = "三角形がありません" };

            var uvs = new Vector2[split.VertexCount];

            // アイランドごとに個別にLSCM
            for (int island = 0; island < split.IslandCount; island++)
            {
                var result = SolveIsland(split, island, uvs, maxIters, tolerance);
                if (!result.Success)
                    return result;
            }

            // アイランドごとに正規化してパッキング
            PackIslands(uvs, split);

            return new LscmResult
            {
                UVs = uvs,
                Success = true,
                IslandCount = split.IslandCount
            };
        }

        /// <summary>
        /// 単一アイランドのLSCMを解く。
        /// </summary>
        private static LscmResult SolveIsland(
            SeamSplitter.SplitResult split, int islandId,
            Vector2[] uvs, int maxIters, double tolerance)
        {
            // このアイランドに属する頂点を収集
            var islandVerts = new List<int>();
            for (int i = 0; i < split.VertexCount; i++)
            {
                if (split.VertexIslandId[i] == islandId)
                    islandVerts.Add(i);
            }

            if (islandVerts.Count < 3)
            {
                // 退化：適当にUVを割り当て
                foreach (int v in islandVerts)
                    uvs[v] = Vector2.zero;
                return new LscmResult { Success = true };
            }

            // このアイランドに属する三角形を収集
            var islandTris = new List<int>();
            for (int t = 0; t < split.TriangleCount; t++)
            {
                int v0 = split.Triangles[t * 3];
                if (split.VertexIslandId[v0] == islandId)
                    islandTris.Add(t);
            }

            if (islandTris.Count == 0)
            {
                foreach (int v in islandVerts)
                    uvs[v] = Vector2.zero;
                return new LscmResult { Success = true };
            }

            // UV頂点→ローカルインデックスマップ
            var globalToLocal = new Dictionary<int, int>(islandVerts.Count);
            for (int i = 0; i < islandVerts.Count; i++)
                globalToLocal[islandVerts[i]] = i;

            int n = islandVerts.Count;

            // --- Pin選択（境界上の最遠ペア） ---
            var boundary = FindBoundaryVertices(split, islandTris, islandId);

            int pinAGlobal, pinBGlobal;
            if (boundary.Count >= 2)
            {
                FindFarthestPair(split.Positions, boundary, out pinAGlobal, out pinBGlobal);
            }
            else
            {
                // 境界なし（閉曲面）→ 最遠ペアを使用（結果は不安定になりうる）
                pinAGlobal = islandVerts[0];
                pinBGlobal = FindFarthestFrom(split.Positions, islandVerts, pinAGlobal);
            }

            int pinALocal = globalToLocal[pinAGlobal];
            int pinBLocal = globalToLocal[pinBGlobal];

            // Pin座標
            float dist = Vector3.Distance(split.Positions[pinAGlobal], split.Positions[pinBGlobal]);
            if (dist < 1e-8f) dist = 1f;

            var pinnedLocal = new Dictionary<int, Vector2>
            {
                [pinALocal] = new Vector2(0f, 0f),
                [pinBLocal] = new Vector2(1f, 0f),
            };

            // 自由変数のインデックスマップ
            var freeMap = new int[n]; // local → freeIndex (-1 if pinned)
            int nFree = 0;
            for (int i = 0; i < n; i++)
            {
                if (pinnedLocal.ContainsKey(i))
                    freeMap[i] = -1;
                else
                    freeMap[i] = nFree++;
            }

            if (nFree == 0)
            {
                // 全部pinnedなら即返す
                uvs[pinAGlobal] = pinnedLocal[pinALocal];
                uvs[pinBGlobal] = pinnedLocal[pinBLocal];
                return new LscmResult { Success = true };
            }

            int dim = 2 * nFree;

            // --- 正規方程式を組み立て ---
            var M = new SymSparse(dim);
            var rhs = new double[dim];

            foreach (int t in islandTris)
            {
                int gi0 = split.Triangles[t * 3 + 0];
                int gi1 = split.Triangles[t * 3 + 1];
                int gi2 = split.Triangles[t * 3 + 2];

                if (!TryLocal2D(split.Positions[gi0], split.Positions[gi1], split.Positions[gi2],
                    out var p0, out var p1, out var p2, out double area2))
                    continue;

                double w = Math.Abs(area2) * 0.5;
                if (w < 1e-20) continue;

                ComputeBarycentricGradients(p0, p1, p2, out var g0, out var g1, out var g2);

                int li0 = globalToLocal[gi0];
                int li1 = globalToLocal[gi1];
                int li2 = globalToLocal[gi2];

                // CR eq1: du/dx - dv/dy = 0 → Σ gx*u - Σ gy*v = 0
                AddRow(w,
                    (li0, g0.x, -g0.y),
                    (li1, g1.x, -g1.y),
                    (li2, g2.x, -g2.y),
                    pinnedLocal, freeMap, M, rhs);

                // CR eq2: du/dy + dv/dx = 0 → Σ gy*u + Σ gx*v = 0
                AddRow(w,
                    (li0, g0.y, g0.x),
                    (li1, g1.y, g1.x),
                    (li2, g2.y, g2.x),
                    pinnedLocal, freeMap, M, rhs);
            }

            // --- CG法で解く ---
            double[] x = ConjugateGradient(M, rhs, maxIters, tolerance);

            // --- UV復元 ---
            for (int i = 0; i < n; i++)
            {
                int globalIdx = islandVerts[i];
                if (pinnedLocal.TryGetValue(i, out var pinUv))
                {
                    uvs[globalIdx] = pinUv;
                }
                else
                {
                    int fi = freeMap[i];
                    uvs[globalIdx] = new Vector2((float)x[2 * fi], (float)x[2 * fi + 1]);
                }
            }

            return new LscmResult { Success = true };
        }

        // ================================================================
        // 正規方程式への1行追加
        // ================================================================

        private static void AddRow(
            double w,
            (int local, double cu, double cv) a,
            (int local, double cu, double cv) b,
            (int local, double cu, double cv) c,
            Dictionary<int, Vector2> pinnedLocal,
            int[] freeMap,
            SymSparse M,
            double[] rhs)
        {
            // 非ゼロ係数リスト（最大6要素: 3頂点×(u,v)）
            var terms = new (int idx, double coeff)[6];
            int m = 0;
            double constant = 0.0;

            AccumVertex(a.local, a.cu, a.cv, pinnedLocal, freeMap, terms, ref m, ref constant);
            AccumVertex(b.local, b.cu, b.cv, pinnedLocal, freeMap, terms, ref m, ref constant);
            AccumVertex(c.local, c.cu, c.cv, pinnedLocal, freeMap, terms, ref m, ref constant);

            // M += w * t*t^T, rhs += -w * t * constant
            for (int i = 0; i < m; i++)
            {
                int ii = terms[i].idx;
                double ci = terms[i].coeff;

                rhs[ii] += -w * ci * constant;

                for (int j = 0; j <= i; j++)
                {
                    int jj = terms[j].idx;
                    double cj = terms[j].coeff;
                    M.Add(ii, jj, w * ci * cj);
                }
            }
        }

        private static void AccumVertex(
            int local, double cu, double cv,
            Dictionary<int, Vector2> pinnedLocal,
            int[] freeMap,
            (int idx, double coeff)[] terms,
            ref int m,
            ref double constant)
        {
            if (pinnedLocal.TryGetValue(local, out var p))
            {
                constant += cu * p.x + cv * p.y;
                return;
            }

            int fi = freeMap[local];
            if (fi < 0) return;

            if (Math.Abs(cu) > 1e-30)
                terms[m++] = (2 * fi, cu);
            if (Math.Abs(cv) > 1e-30)
                terms[m++] = (2 * fi + 1, cv);
        }

        // ================================================================
        // 三角形の局所2D化
        // ================================================================

        private static bool TryLocal2D(Vector3 v0, Vector3 v1, Vector3 v2,
            out Vector2 p0, out Vector2 p1, out Vector2 p2, out double area2)
        {
            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            double len1 = e1.magnitude;
            if (len1 < 1e-12)
            {
                p0 = p1 = p2 = default;
                area2 = 0;
                return false;
            }

            Vector3 xAxis = e1 / (float)len1;
            double x2 = Vector3.Dot(e2, xAxis);
            Vector3 e2Perp = e2 - (float)x2 * xAxis;
            double y2 = e2Perp.magnitude;

            p0 = new Vector2(0f, 0f);
            p1 = new Vector2((float)len1, 0f);
            p2 = new Vector2((float)x2, (float)y2);

            area2 = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
            return Math.Abs(area2) > 1e-20;
        }

        private static void ComputeBarycentricGradients(
            Vector2 p0, Vector2 p1, Vector2 p2,
            out Vector2 g0, out Vector2 g1, out Vector2 g2)
        {
            double A2 = (p1.x - p0.x) * (p2.y - p0.y) - (p2.x - p0.x) * (p1.y - p0.y);
            double invA2 = 1.0 / A2;

            g0 = new Vector2(
                (float)((p1.y - p2.y) * invA2),
                (float)((p2.x - p1.x) * invA2));
            g1 = new Vector2(
                (float)((p2.y - p0.y) * invA2),
                (float)((p0.x - p2.x) * invA2));
            g2 = new Vector2(
                (float)((p0.y - p1.y) * invA2),
                (float)((p1.x - p0.x) * invA2));
        }

        // ================================================================
        // 境界頂点検出
        // ================================================================

        /// <summary>
        /// 指定アイランドの境界頂点を返す。
        /// 境界エッジ = 1つの三角形にのみ属するエッジ。
        /// </summary>
        private static List<int> FindBoundaryVertices(
            SeamSplitter.SplitResult split, List<int> islandTris, int islandId)
        {
            var edgeCount = new Dictionary<long, int>();

            foreach (int t in islandTris)
            {
                for (int e = 0; e < 3; e++)
                {
                    int va = split.Triangles[t * 3 + e];
                    int vb = split.Triangles[t * 3 + (e + 1) % 3];
                    long key = va < vb ? ((long)va << 32) | (uint)vb : ((long)vb << 32) | (uint)va;

                    if (edgeCount.TryGetValue(key, out int cnt))
                        edgeCount[key] = cnt + 1;
                    else
                        edgeCount[key] = 1;
                }
            }

            var boundaryVerts = new HashSet<int>();
            foreach (var kv in edgeCount)
            {
                if (kv.Value == 1)
                {
                    int va = (int)(kv.Key >> 32);
                    int vb = (int)(kv.Key & 0xffffffff);
                    boundaryVerts.Add(va);
                    boundaryVerts.Add(vb);
                }
            }

            return new List<int>(boundaryVerts);
        }

        /// <summary>
        /// 最遠ペアを探す。
        /// </summary>
        private static void FindFarthestPair(Vector3[] positions, List<int> candidates,
            out int bestA, out int bestB)
        {
            bestA = candidates[0];
            bestB = candidates.Count > 1 ? candidates[1] : candidates[0];
            float bestDist = -1f;

            // O(n^2) だが境界頂点数は通常少ない
            for (int i = 0; i < candidates.Count; i++)
            {
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    float d = (positions[candidates[i]] - positions[candidates[j]]).sqrMagnitude;
                    if (d > bestDist)
                    {
                        bestDist = d;
                        bestA = candidates[i];
                        bestB = candidates[j];
                    }
                }
            }
        }

        private static int FindFarthestFrom(Vector3[] positions, List<int> candidates, int from)
        {
            int best = from;
            float bestDist = -1f;
            Vector3 p = positions[from];
            foreach (int c in candidates)
            {
                float d = (positions[c] - p).sqrMagnitude;
                if (d > bestDist) { bestDist = d; best = c; }
            }
            return best;
        }

        // ================================================================
        // アイランドパッキング（簡易：横並び配置）
        // ================================================================

        private static void PackIslands(Vector2[] uvs, SeamSplitter.SplitResult split)
        {
            if (split.IslandCount <= 0) return;

            // 各アイランドを[0,1]に正規化
            var mins = new Vector2[split.IslandCount];
            var maxs = new Vector2[split.IslandCount];
            var hasVert = new bool[split.IslandCount];

            for (int i = 0; i < split.IslandCount; i++)
            {
                mins[i] = new Vector2(float.MaxValue, float.MaxValue);
                maxs[i] = new Vector2(float.MinValue, float.MinValue);
            }

            for (int v = 0; v < uvs.Length; v++)
            {
                int island = split.VertexIslandId[v];
                mins[island] = Vector2.Min(mins[island], uvs[v]);
                maxs[island] = Vector2.Max(maxs[island], uvs[v]);
                hasVert[island] = true;
            }

            // 各アイランドの正規化＋横並び配置
            float offsetU = 0f;
            const float padding = 0.02f;

            for (int island = 0; island < split.IslandCount; island++)
            {
                if (!hasVert[island]) continue;

                Vector2 size = maxs[island] - mins[island];
                float scale = Mathf.Max(size.x, size.y);
                if (scale < 1e-8f) scale = 1f;

                for (int v = 0; v < uvs.Length; v++)
                {
                    if (split.VertexIslandId[v] != island) continue;
                    Vector2 uv = (uvs[v] - mins[island]) / scale;
                    uvs[v] = new Vector2(uv.x + offsetU, uv.y);
                }

                float normalizedWidth = size.x / scale;
                offsetU += normalizedWidth + padding;
            }

            // 全体を[0,1]に再正規化（1アイランドなら自動的にそうなる）
            if (split.IslandCount > 1)
            {
                Vector2 globalMin = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 globalMax = new Vector2(float.MinValue, float.MinValue);
                for (int v = 0; v < uvs.Length; v++)
                {
                    globalMin = Vector2.Min(globalMin, uvs[v]);
                    globalMax = Vector2.Max(globalMax, uvs[v]);
                }
                Vector2 gSize = globalMax - globalMin;
                float gScale = Mathf.Max(gSize.x, gSize.y);
                if (gScale < 1e-8f) gScale = 1f;
                for (int v = 0; v < uvs.Length; v++)
                    uvs[v] = (uvs[v] - globalMin) / gScale;
            }
        }

        // ================================================================
        // Conjugate Gradient（SPD行列用）
        // ================================================================

        private static double[] ConjugateGradient(SymSparse A, double[] b, int maxIters, double tol)
        {
            int n = A.Size;
            var x = new double[n];
            var r = new double[n];
            var p = new double[n];
            var Ap = new double[n];

            Array.Copy(b, r, n);
            Array.Copy(r, p, n);

            double rsOld = Dot(r, r);
            double bNorm = Math.Sqrt(Math.Max(Dot(b, b), 1e-30));
            double tolAbs = tol * bNorm;

            for (int k = 0; k < maxIters; k++)
            {
                A.Mul(p, Ap);
                double pAp = Dot(p, Ap);
                if (Math.Abs(pAp) < 1e-30) break;

                double alpha = rsOld / pAp;

                Axpy(x, p, alpha);
                Axpy(r, Ap, -alpha);

                double rsNew = Dot(r, r);
                if (Math.Sqrt(rsNew) < tolAbs) break;

                double beta = rsNew / Math.Max(rsOld, 1e-30);
                for (int i = 0; i < n; i++)
                    p[i] = r[i] + beta * p[i];

                rsOld = rsNew;
            }

            return x;
        }

        private static double Dot(double[] a, double[] b)
        {
            double s = 0;
            for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
            return s;
        }

        private static void Axpy(double[] y, double[] x, double a)
        {
            for (int i = 0; i < y.Length; i++) y[i] += a * x[i];
        }
    }

    // ================================================================
    // UV書き戻しユーティリティ
    // ================================================================

    /// <summary>
    /// LSCM結果をMeshObjectのVertex.UVs / Face.UVIndicesに書き戻す。
    /// </summary>
    public static class LscmUvWriter
    {
        /// <summary>
        /// LSCM結果をMeshObjectに適用する。
        /// </summary>
        public static void Apply(
            MeshObject meshObj,
            SeamSplitter.SplitResult split,
            LscmSolver.LscmResult lscmResult)
        {
            if (!lscmResult.Success || lscmResult.UVs == null) return;

            // UV用頂点→元頂点 の逆引きマップ
            // 同一元頂点に複数UV用頂点が対応する場合がある（Seam分裂）
            // → 元頂点のUVsリストに追加し、FaceのUVIndicesを更新

            // まず全頂点のUVsをクリア
            foreach (var v in meshObj.Vertices)
                v.UVs.Clear();

            // cornerから元Face・ローカルインデックスへのマッピングを使い、
            // Face.UVIndicesを正しく設定する

            // UV用頂点 → 元頂点のUVs内インデックス
            var uvVertToUvSubIdx = new int[split.VertexCount];

            // 元頂点ごとに、既に割り当てたUV値→UVsサブインデックスのマップ
            var vertUvMap = new Dictionary<int, Dictionary<Vector2Approx, int>>();

            for (int uv = 0; uv < split.VertexCount; uv++)
            {
                int origV = split.UvToOrigVertex[uv];
                var uvCoord = lscmResult.UVs[uv];

                if (!vertUvMap.TryGetValue(origV, out var map))
                {
                    map = new Dictionary<Vector2Approx, int>();
                    vertUvMap[origV] = map;
                }

                var key = new Vector2Approx(uvCoord);
                if (!map.TryGetValue(key, out int subIdx))
                {
                    subIdx = meshObj.Vertices[origV].UVs.Count;
                    meshObj.Vertices[origV].UVs.Add(uvCoord);
                    map[key] = subIdx;
                }
                uvVertToUvSubIdx[uv] = subIdx;
            }

            // 各三角形のcornerからFace.UVIndicesを更新
            // 三角形→元Face のマッピングと、corner→元Faceローカルインデックスを使う
            // 注意：扇形分割で複数三角形が1つのFaceから生成されるため、
            //        同じFaceの異なるcornerが複数の三角形に分散している

            // まずFace.UVIndicesを正しいサイズに初期化
            foreach (var face in meshObj.Faces)
            {
                face.UVIndices.Clear();
                for (int i = 0; i < face.VertexCount; i++)
                    face.UVIndices.Add(0);
            }

            // 三角形cornerからFaceのUVIndicesへ書き戻し
            int cornerCount = split.TriangleCount * 3;
            for (int c = 0; c < cornerCount; c++)
            {
                int triIdx = c / 3;
                int origFace = split.TriToOrigFace[triIdx];
                int faceLocal = split.TriCornerToFaceLocal[c];

                if (origFace < 0 || origFace >= meshObj.Faces.Count) continue;
                var face = meshObj.Faces[origFace];
                if (faceLocal < 0 || faceLocal >= face.VertexCount) continue;

                int uvVertIdx = split.CornerToUvVertex[c];
                int uvSubIdx = uvVertToUvSubIdx[uvVertIdx];

                face.UVIndices[faceLocal] = uvSubIdx;
            }
        }

        /// <summary>
        /// 近似比較用のVector2ラッパー
        /// </summary>
        private readonly struct Vector2Approx : IEquatable<Vector2Approx>
        {
            private readonly int _hx, _hy;

            public Vector2Approx(Vector2 v)
            {
                _hx = Mathf.RoundToInt(v.x * 100000);
                _hy = Mathf.RoundToInt(v.y * 100000);
            }

            public bool Equals(Vector2Approx other) => _hx == other._hx && _hy == other._hy;
            public override bool Equals(object obj) => obj is Vector2Approx o && Equals(o);
            public override int GetHashCode() => (_hx * 73856093) ^ (_hy * 19349663);
        }
    }
}
