// TJunctionOps.cs
// T 字接合（辺の途中に他の面の頂点が乗っている状態）を解消する。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【何のためにあるか】
//   ブーリアン（BooleanOps → pb_CSG）は BSP で多角形を切るとき、
//   切った側にだけ頂点を足し、辺を共有する隣の面には知らせない。
//   そのため辺の途中に頂点が乗った状態（T 字）が残り、
//   その辺は隣と共有されないので境界として扱われる。
//   立方体から立方体を引くだけでも境界ループが 1 つ残る（実測）。
//
//   ここは「辺の上に乗っている頂点を、その辺を持つ面へ挿入する」だけを行う。
//   頂点も面も増やさず、面の頂点列に点を足すので、平面性は崩れない。
//
// 【ブーリアン専用にしない理由】
//   同じ状態は穴つなぎや面削除の後にも起きる。位相をつなぎ直す道具として
//   独立させ、どの工程からでも呼べるようにする。
//
// 【限界】
//   辺に乗っているかを距離で見る。しきい値を大きくすると、
//   乗っていない頂点まで拾って面をねじる。既定は小さめにしてある。
//   面の向き（巻き順）は変えない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>T 字接合の解消。</summary>
    public static class TJunctionOps
    {
        /// <summary>辺に乗っているとみなす距離の既定値[m]。</summary>
        public const float DefaultTolerance = 1e-4f;

        /// <summary>解消の結果。</summary>
        public struct Result
        {
            /// <summary>点を挿入した回数。</summary>
            public int Inserted;

            /// <summary>点を挿入した面の数。</summary>
            public int TouchedFaces;
        }

        /// <summary>
        /// 辺の上に乗っている頂点を、その辺を持つ面へ挿入する。
        /// 頂点の位置は変えない。面の数も変えない。
        /// </summary>
        public static Result Resolve(MeshObject mesh, float tolerance = DefaultTolerance)
        {
            var r = new Result();
            if (mesh == null || mesh.FaceCount == 0 || mesh.VertexCount == 0) return r;

            float tol   = Mathf.Max(tolerance, 1e-7f);
            float tolSq = tol * tol;

            // 位置で引けるよう、粗い格子に頂点を入れておく。
            // 全頂点を総当たりすると面数×辺数×頂点数になって重い。
            float cell = Mathf.Max(tol * 8f, 1e-3f);
            var grid = new Dictionary<(int, int, int), List<int>>();

            for (int v = 0; v < mesh.VertexCount; v++)
            {
                var key = CellOf(mesh.Vertices[v].Position, cell);
                if (!grid.TryGetValue(key, out var list)) { list = new List<int>(); grid[key] = list; }
                list.Add(v);
            }

            var near = new List<int>();
            var hits = new List<(int Vertex, float T)>();

            for (int f = 0; f < mesh.FaceCount; f++)
            {
                var face = mesh.Faces[f];
                if (face?.VertexIndices == null || face.VertexIndices.Count < 3) continue;

                bool touched = false;

                // 挿入すると添字がずれるので、後ろの辺から見る。
                for (int e = face.VertexIndices.Count - 1; e >= 0; e--)
                {
                    int i1 = face.VertexIndices[e];
                    int i2 = face.VertexIndices[(e + 1) % face.VertexIndices.Count];

                    Vector3 p1 = mesh.Vertices[i1].Position;
                    Vector3 p2 = mesh.Vertices[i2].Position;
                    Vector3 d  = p2 - p1;

                    float lenSq = d.sqrMagnitude;
                    if (lenSq < tolSq) continue;

                    CollectNear(grid, cell, p1, p2, tol, near);

                    hits.Clear();
                    foreach (int v in near)
                    {
                        if (v == i1 || v == i2) continue;
                        if (face.VertexIndices.Contains(v)) continue;

                        Vector3 p = mesh.Vertices[v].Position;
                        float t = Vector3.Dot(p - p1, d) / lenSq;

                        // 端は除く。端に乗っているなら別の頂点が重なっているだけで、
                        // それは頂点の統合で扱うべきもの。
                        if (t <= 0f || t >= 1f) continue;

                        Vector3 foot = p1 + d * t;
                        if ((p - foot).sqrMagnitude > tolSq) continue;

                        hits.Add((v, t));
                    }

                    if (hits.Count == 0) continue;

                    // 辺に沿った順に並べてから入れる。
                    hits.Sort((x, y) => x.T.CompareTo(y.T));

                    for (int k = hits.Count - 1; k >= 0; k--)
                        face.VertexIndices.Insert(e + 1, hits[k].Vertex);

                    r.Inserted += hits.Count;
                    touched = true;
                }

                if (touched) r.TouchedFaces++;
            }

            // 面の頂点列を書き換えただけなので、描画の作り直しは呼ぶ側に任せる。
            return r;
        }

        private static (int, int, int) CellOf(Vector3 p, float cell)
            => (Mathf.FloorToInt(p.x / cell), Mathf.FloorToInt(p.y / cell), Mathf.FloorToInt(p.z / cell));

        /// <summary>辺の通る格子と、その周り 1 つぶんの頂点を集める。</summary>
        private static void CollectNear(
            Dictionary<(int, int, int), List<int>> grid, float cell,
            Vector3 p1, Vector3 p2, float tol, List<int> into)
        {
            into.Clear();

            Vector3 lo = Vector3.Min(p1, p2) - Vector3.one * tol;
            Vector3 hi = Vector3.Max(p1, p2) + Vector3.one * tol;

            var a = CellOf(lo, cell);
            var b = CellOf(hi, cell);

            for (int x = a.Item1; x <= b.Item1; x++)
                for (int y = a.Item2; y <= b.Item2; y++)
                    for (int z = a.Item3; z <= b.Item3; z++)
                        if (grid.TryGetValue((x, y, z), out var list))
                            into.AddRange(list);
        }
    }
}
