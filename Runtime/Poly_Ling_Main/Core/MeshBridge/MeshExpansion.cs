// Runtime/Poly_Ling_Main/Core/MeshBridge/MeshExpansion.cs
// UV展開順序の唯一の実装。
//
// ================================================================
// なぜこのファイルが要るか
// ================================================================
// 「MeshObject の論理頂点を (頂点, UVスロット) 単位へ展開する」順序は、
// 以前は 9 か所に手書きで重複していた。そのうち 1 か所
// (UnifiedBufferManager_Build.BuildExpandedVertexMapping) だけが
// 孤立頂点を含めており、他の 8 か所は除外していた。
//
// その食い違いにより、孤立頂点を持つメッシュでは
//   UnifiedSystemAdapter.WritebackTransformedVertices の
//   「UnityMesh.vertexCount == 展開頂点数」判定が必ず偽になり、
//   (a) GPU のワールド座標が UnityMesh に書き戻されず、
//       ローカル座標のまま Matrix4x4.identity で描画される
//   (b) 判定が偽になるたびに ToUnityMesh() が新しい Mesh を作り、
//       直接代入で旧 Mesh が漏れる（頂点ドラッグ中は毎フレーム）
// という 2 つの障害が同時に起きていた。
//
// 展開順序を書くのはこのファイルだけにする。新たに手書きしないこと。
//
// ================================================================
// 展開規則（変更禁止・変更する場合は全消費者を同時に直すこと）
// ================================================================
// 1. 孤立頂点を除外する。
//    「孤立」= VertexCount が 3 以上の面から一度も参照されない頂点。
//    2 頂点の補助線しか参照していない頂点は「孤立」である。
//
// 2. face.IsHidden は見ない。
//    面の非表示は編集補助であり、見るかどうかで展開頂点数が変わると
//    GPU 展開バッファと UnityMesh の index 空間が食い違う。
//    非表示は三角形の生成側で行うこと。
//
// 3. 外側ループ = 論理頂点 index 昇順、内側ループ = その頂点の UV スロット順。
//    UV スロットが 0 個の頂点はスロット 1 個ぶん（uvIdx = 0）として扱う。
//
// ================================================================
// 消費者（ここを直したら全部を確認すること）
// ================================================================
//   MeshBridgeDefault.ToUnityMesh
//   MeshBridgeDefault.ToUnityMeshShared
//   MeshBridgeDefault.ApplyTrianglesInPlace
//   MeshObject.BuildExpansionMap / BuildInverseExpansionMap
//   UnifiedBufferManager_Build.BuildExpandedVertexMapping（GPU 展開バッファ）
//   PlayerViewportManager.UpdateExpandedUnityMesh
//   PMXExporter.AppendExpandedVertices（別実装。規則だけ一致させてある）
//
// 【混同禁止】基本頂点バッファ (_positions / _worldPositions) とは別物。
//   あちらは孤立頂点を必ず含める。孤立点だけのオブジェクトや、頂点を置いた
//   直後でまだ面の無いオブジェクトを点として描き、選択できるようにするため
//   （UnifiedBufferManager_Build.ShouldIncludeInBuffers を参照）。
//   本ファイルが扱うのは UV 展開バッファと UnityMesh の index 空間だけ。

using System;
using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.MeshBridge
{
    /// <summary>
    /// UV展開順序の唯一の実装。ファイル冒頭の展開規則を参照。
    /// </summary>
    public static class MeshExpansion
    {
        /// <summary>
        /// 展開対象となる頂点 index の集合を返す（＝孤立していない頂点）。
        ///
        /// 「孤立」の定義は VertexCount が 3 以上の面から一度も参照されないこと。
        /// face.IsHidden は見ない（ファイル冒頭の規則 2）。
        /// </summary>
        public static HashSet<int> BuildNonIsolatedSet(MeshObject source)
        {
            var set = new HashSet<int>();
            if (source?.Faces == null) return set;

            foreach (var face in source.Faces)
            {
                if (face == null || face.VertexCount < 3) continue;
                var indices = face.VertexIndices;
                if (indices == null) continue;
                foreach (int vi in indices) set.Add(vi);
            }
            return set;
        }

        /// <summary>
        /// 指定頂点の UV スロット数。スロットが無ければ 1 とみなす。
        /// 展開数の計算はすべてここを通すこと。
        /// </summary>
        public static int SlotCount(Vertex vertex)
        {
            if (vertex == null) return 1;
            return vertex.UVs != null && vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;
        }

        /// <summary>
        /// 展開後の頂点数を返す。
        ///
        /// nonIsolated に null 以外を渡すと、その集合を再利用する
        /// （同じメッシュに対して複数回問い合わせる呼び出し元向け）。
        /// </summary>
        public static int CountExpanded(MeshObject source, HashSet<int> nonIsolated = null)
        {
            if (source?.Vertices == null) return 0;
            var set = nonIsolated ?? BuildNonIsolatedSet(source);

            int count = 0;
            for (int vIdx = 0; vIdx < source.Vertices.Count; vIdx++)
            {
                if (!set.Contains(vIdx)) continue;
                count += SlotCount(source.Vertices[vIdx]);
            }
            return count;
        }

        /// <summary>
        /// 展開順に (論理頂点 index, UVスロット index, 展開後 index) を列挙する。
        /// 展開後 index は 0 から連番。
        ///
        /// nonIsolated に null 以外を渡すと、その集合を再利用する。
        /// </summary>
        public static void Enumerate(
            MeshObject source,
            Action<int, int, int> onSlot,
            HashSet<int> nonIsolated = null)
        {
            if (source?.Vertices == null || onSlot == null) return;
            var set = nonIsolated ?? BuildNonIsolatedSet(source);

            int expandedIdx = 0;
            for (int vIdx = 0; vIdx < source.Vertices.Count; vIdx++)
            {
                if (!set.Contains(vIdx)) continue;
                int slots = SlotCount(source.Vertices[vIdx]);
                for (int uvIdx = 0; uvIdx < slots; uvIdx++)
                {
                    onSlot(vIdx, uvIdx, expandedIdx);
                    expandedIdx++;
                }
            }
        }
    }
}
