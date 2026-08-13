// BoundaryEdgeOps.cs
// 「エッジ」＝1つの面だけが使っている辺（面のうち共有を持たない辺）の抽出とグループ分け。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【用語】
//   エッジ       : 面 1 枚だけが使う辺。穴の縁・開いた面の外周がこれにあたる。
//                  2頂点の面（線分）は面ではないので対象外。
//   エッジグループ: 頂点を共有してつながるエッジの連結成分。穴 1 つ＝グループ 1 つになる。
//
// 位相計算のみを行う。ヒットテスト・描画・選択適用は呼び出し側の担当。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    public static class BoundaryEdgeOps
    {
        // ================================================================
        // 抽出
        // ================================================================

        /// <summary>
        /// メッシュの全エッジ（1面だけが使う辺）を返す。
        /// 3頂点未満の面（線分）は辺を持たないものとして無視する。
        /// </summary>
        public static HashSet<VertexPair> CollectBoundaryEdges(MeshObject mesh)
        {
            var result = new HashSet<VertexPair>();
            if (mesh == null) return result;

            var useCount = new Dictionary<VertexPair, int>();

            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                int n = face.VertexIndices.Count;
                if (n < 3) continue;   // 線分は対象外

                for (int i = 0; i < n; i++)
                {
                    int a = face.VertexIndices[i];
                    int b = face.VertexIndices[(i + 1) % n];
                    if (a == b) continue;

                    var key = new VertexPair(a, b);
                    useCount.TryGetValue(key, out int c);
                    useCount[key] = c + 1;
                }
            }

            foreach (var kv in useCount)
                if (kv.Value == 1) result.Add(kv.Key);

            return result;
        }

        // ================================================================
        // グループ分け
        // ================================================================

        /// <summary>
        /// エッジを頂点共有でつなぎ、連結成分（グループ）に分ける。
        /// </summary>
        public static List<List<VertexPair>> BuildGroups(HashSet<VertexPair> edges)
        {
            var groups = new List<List<VertexPair>>();
            if (edges == null || edges.Count == 0) return groups;

            // 頂点 → その頂点に接するエッジ
            var byVertex = new Dictionary<int, List<VertexPair>>();
            foreach (var e in edges)
            {
                Add(byVertex, e.V1, e);
                Add(byVertex, e.V2, e);
            }

            var visited = new HashSet<VertexPair>();
            foreach (var seed in edges)
            {
                if (visited.Contains(seed)) continue;

                var group = new List<VertexPair>();
                var stack = new Stack<VertexPair>();
                stack.Push(seed);
                visited.Add(seed);

                while (stack.Count > 0)
                {
                    var e = stack.Pop();
                    group.Add(e);

                    PushNeighbors(byVertex, e.V1, visited, stack);
                    PushNeighbors(byVertex, e.V2, visited, stack);
                }

                groups.Add(group);
            }

            return groups;
        }

        private static void Add(Dictionary<int, List<VertexPair>> map, int key, VertexPair e)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = new List<VertexPair>();
            list.Add(e);
        }

        private static void PushNeighbors(
            Dictionary<int, List<VertexPair>> byVertex, int vertex,
            HashSet<VertexPair> visited, Stack<VertexPair> stack)
        {
            if (!byVertex.TryGetValue(vertex, out var list)) return;
            foreach (var n in list)
            {
                if (visited.Contains(n)) continue;
                visited.Add(n);
                stack.Push(n);
            }
        }

        // ================================================================
        // 開始要素からグループを引く
        // ================================================================

        /// <summary>
        /// 指定頂点に接するエッジが属するグループを返す。無ければ空。
        /// 複数グループに接する頂点（グループ同士が1点で接する形）では、
        /// 接するすべてのグループを合わせて返す。
        /// </summary>
        public static List<VertexPair> GroupFromVertex(MeshObject mesh, int vertex)
        {
            var edges = CollectBoundaryEdges(mesh);
            if (edges.Count == 0 || vertex < 0) return new List<VertexPair>();

            var seeds = new List<VertexPair>();
            foreach (var e in edges)
                if (e.V1 == vertex || e.V2 == vertex) seeds.Add(e);

            return CollectGroupsContaining(edges, seeds);
        }

        /// <summary>
        /// 指定辺が属するグループを返す。その辺がエッジでなければ空。
        /// </summary>
        public static List<VertexPair> GroupFromEdge(MeshObject mesh, VertexPair edge)
        {
            var edges = CollectBoundaryEdges(mesh);
            if (!edges.Contains(edge)) return new List<VertexPair>();

            return CollectGroupsContaining(edges, new List<VertexPair> { edge });
        }

        /// <summary>
        /// 指定面が持つエッジのグループを返す。その面がエッジを持たなければ空。
        /// </summary>
        public static List<VertexPair> GroupFromFace(MeshObject mesh, int face)
        {
            var edges = CollectBoundaryEdges(mesh);
            if (edges.Count == 0) return new List<VertexPair>();
            if (mesh == null || face < 0 || face >= mesh.Faces.Count) return new List<VertexPair>();

            var f = mesh.Faces[face];
            int n = f.VertexIndices.Count;
            if (n < 3) return new List<VertexPair>();

            var seeds = new List<VertexPair>();
            for (int i = 0; i < n; i++)
            {
                var key = new VertexPair(f.VertexIndices[i], f.VertexIndices[(i + 1) % n]);
                if (edges.Contains(key)) seeds.Add(key);
            }

            return CollectGroupsContaining(edges, seeds);
        }

        /// <summary>seeds が属するグループをすべて集めて返す（重複なし）。</summary>
        private static List<VertexPair> CollectGroupsContaining(
            HashSet<VertexPair> edges, List<VertexPair> seeds)
        {
            var result = new List<VertexPair>();
            if (seeds.Count == 0) return result;

            var groups = BuildGroups(edges);
            var taken = new HashSet<VertexPair>();

            foreach (var g in groups)
            {
                var set = new HashSet<VertexPair>(g);
                bool hit = false;
                foreach (var s in seeds)
                {
                    if (set.Contains(s)) { hit = true; break; }
                }
                if (!hit) continue;

                foreach (var e in g)
                    if (taken.Add(e)) result.Add(e);
            }

            return result;
        }

        // ================================================================
        // 選択範囲内のエッジ
        // ================================================================

        /// <summary>
        /// 両端点が selectedVertices に含まれるエッジだけを返す。
        /// </summary>
        public static List<VertexPair> EdgesWithinSelection(
            MeshObject mesh, HashSet<int> selectedVertices)
        {
            var result = new List<VertexPair>();
            if (mesh == null || selectedVertices == null || selectedVertices.Count == 0)
                return result;

            foreach (var e in CollectBoundaryEdges(mesh))
            {
                if (selectedVertices.Contains(e.V1) && selectedVertices.Contains(e.V2))
                    result.Add(e);
            }

            return result;
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        /// <summary>エッジ列の構成頂点を返す。</summary>
        public static List<int> VerticesOf(List<VertexPair> edges)
        {
            var set = new HashSet<int>();
            var list = new List<int>();
            if (edges == null) return list;

            foreach (var e in edges)
            {
                if (set.Add(e.V1)) list.Add(e.V1);
                if (set.Add(e.V2)) list.Add(e.V2);
            }
            return list;
        }
    }
}
