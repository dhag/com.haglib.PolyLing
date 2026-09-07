// Runtime/Poly_Ling_Main/MQO/Common/MQOVertexExpandHelper.cs
// Editor/Utility/DependTool/_EditorWindow_Tools_/PMXMQOTransferPanel.cs から分離
//
// 展開規則そのものは持たない。孤立判定も展開数の計算も
// Poly_Ling.MeshBridge.MeshExpansion へ委譲する。
// 以前はここに独自の孤立判定（面の頂点数を見ずに参照されていれば使用とみなす）
// を持っていたため、2 頂点の補助線しか参照しない頂点の扱いが MeshExpansion と
// 食い違い、AutoMatch の頂点数一致判定が PMX の実頂点数とずれていた。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQO頂点展開数計算ヘルパー。実装は MeshExpansion に一本化してある。
    /// </summary>
    public static class MQOVertexExpandHelper
    {
        /// <summary>
        /// 面に使われていない孤立頂点インデックスを返す。
        /// 「孤立」の定義は MeshExpansion と同じ（頂点数 3 以上の面から一度も参照されない）。
        /// </summary>
        public static HashSet<int> GetIsolatedVertices(MeshObject mo)
        {
            var isolated = new HashSet<int>();
            if (mo?.Vertices == null) return isolated;

            var nonIsolated = MeshExpansion.BuildNonIsolatedSet(mo);
            for (int i = 0; i < mo.VertexCount; i++)
                if (!nonIsolated.Contains(i)) isolated.Add(i);

            return isolated;
        }

        /// <summary>
        /// UV展開後の頂点数を計算（孤立点除外）。
        /// excludeVertices は GetIsolatedVertices の結果を渡す想定。
        /// </summary>
        public static int CalculateExpandedVertexCount(MeshObject mo, HashSet<int> excludeVertices)
        {
            if (mo?.Vertices == null) return 0;

            if (excludeVertices == null)
                return MeshExpansion.CountExpanded(mo);

            var nonIsolated = new HashSet<int>();
            for (int i = 0; i < mo.VertexCount; i++)
                if (!excludeVertices.Contains(i)) nonIsolated.Add(i);

            return MeshExpansion.CountExpanded(mo, nonIsolated);
        }
    }
}
