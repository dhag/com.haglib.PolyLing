// PartsIndexRemap.cs
// 頂点・面の索引が詰められたときに、選択とパーツ選択辞書の索引を付け替える。
// Runtime/Poly_Ling_Main/Selection/ に配置
//
// 【何のためにあるか】
//   頂点や面を消すと索引が詰まる。選択状態（SelectionState）とパーツ選択辞書
//   （PartsSelectionSet）と法線再計算の除外セットは、どれも索引で要素を指しているので、
//   詰め直しに追随しないと別の要素を指したままになる。
//   付け替えの中身は 3 者で同じなので、ここ 1 か所に置く。
//
// 【表の約束】
//   map[旧索引] = 新索引。-1 は「消えた」。表の長さは操作前の要素数。
//   表の外（範囲外の索引）は消えた扱いにする。

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.Selection
{
    /// <summary>索引の詰め直しを選択・辞書へ反映する。</summary>
    public static class PartsIndexRemap
    {
        private static int Map(int[] map, int old)
            => (map == null || old < 0 || old >= map.Length) ? -1 : map[old];

        // ================================================================
        // 頂点の表
        // ================================================================

        /// <summary>頂点集合を付け替える。消えた頂点は落とす。</summary>
        public static void ApplyVertexMap(HashSet<int> vertices, int[] map)
        {
            if (vertices == null || vertices.Count == 0 || map == null) return;

            var next = new HashSet<int>();
            foreach (int v in vertices)
            {
                int nv = Map(map, v);
                if (nv >= 0) next.Add(nv);
            }
            vertices.Clear();
            vertices.UnionWith(next);
        }

        /// <summary>辺集合を付け替える。片端でも消えたら落とす。</summary>
        public static void ApplyVertexMap(HashSet<VertexPair> edges, int[] map)
        {
            if (edges == null || edges.Count == 0 || map == null) return;

            var next = new HashSet<VertexPair>();
            foreach (var e in edges)
            {
                int a = Map(map, e.V1);
                int b = Map(map, e.V2);
                if (a >= 0 && b >= 0 && a != b) next.Add(new VertexPair(a, b));
            }
            edges.Clear();
            edges.UnionWith(next);
        }

        /// <summary>頂点ID の控え（索引→ID）を付け替える。消えた索引の控えは落とす。</summary>
        public static void ApplyVertexMap(Dictionary<int, VertexIdTriple> vertexIds, int[] map)
        {
            if (vertexIds == null || vertexIds.Count == 0 || map == null) return;

            var next = new Dictionary<int, VertexIdTriple>(vertexIds.Count);
            foreach (var kv in vertexIds)
            {
                int nv = Map(map, kv.Key);
                if (nv >= 0) next[nv] = kv.Value;
            }
            vertexIds.Clear();
            foreach (var kv in next) vertexIds[kv.Key] = kv.Value;
        }

        /// <summary>選択状態の頂点側（頂点・辺）を付け替える。</summary>
        public static void ApplyVertexMap(SelectionState selection, int[] map)
        {
            if (selection == null) return;
            ApplyVertexMap(selection.Vertices, map);
            ApplyVertexMap(selection.Edges, map);
        }

        /// <summary>パーツ選択辞書 1 セットの頂点側（頂点・辺・頂点ID の控え）を付け替える。</summary>
        public static void ApplyVertexMap(PartsSelectionSet set, int[] map)
        {
            if (set == null) return;
            ApplyVertexMap(set.Vertices, map);
            ApplyVertexMap(set.Edges, map);
            ApplyVertexMap(set.VertexIds, map);
        }

        // ================================================================
        // 面の表
        //   線分（Lines）も 2 頂点の面なので、面の索引で指している。
        //   ゆえに面と同じ表で付け替える。
        // ================================================================

        /// <summary>選択状態の面側（面・線分）を付け替える。</summary>
        public static void ApplyFaceMap(SelectionState selection, int[] map)
        {
            if (selection == null) return;
            ApplyVertexMap(selection.Faces, map);   // 集合の付け替え自体は頂点と同じ
            ApplyVertexMap(selection.Lines, map);
        }

        /// <summary>パーツ選択辞書 1 セットの面側（面・線分）を付け替える。</summary>
        public static void ApplyFaceMap(PartsSelectionSet set, int[] map)
        {
            if (set == null) return;
            ApplyVertexMap(set.Faces, map);
            ApplyVertexMap(set.Lines, map);
        }
    }
}
