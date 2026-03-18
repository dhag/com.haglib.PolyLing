// Runtime/Poly_Ling_Main/MQO/Common/MQOVertexExpandHelper.cs
// Editor/Utility/DependTool/_EditorWindow_Tools_/PMXMQOTransferPanel.cs から分離

using System.Collections.Generic;
using Poly_Ling.Data;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQO頂点展開数計算ヘルパー
    /// </summary>
    public static class MQOVertexExpandHelper
    {
        /// <summary>
        /// 面に使われていない孤立頂点インデックスを返す
        /// </summary>
        public static HashSet<int> GetIsolatedVertices(MeshObject mo)
        {
            var usedVertices = new HashSet<int>();

            foreach (var face in mo.Faces)
            {
                if (face.VertexIndices != null)
                {
                    foreach (var vi in face.VertexIndices)
                        usedVertices.Add(vi);
                }
            }

            var isolated = new HashSet<int>();
            for (int i = 0; i < mo.VertexCount; i++)
            {
                if (!usedVertices.Contains(i))
                    isolated.Add(i);
            }

            return isolated;
        }

        /// <summary>
        /// UV展開後の頂点数を計算（孤立点除外）
        /// </summary>
        public static int CalculateExpandedVertexCount(MeshObject mo, HashSet<int> excludeVertices)
        {
            int count = 0;
            for (int i = 0; i < mo.Vertices.Count; i++)
            {
                if (excludeVertices != null && excludeVertices.Contains(i))
                    continue;

                var vertex = mo.Vertices[i];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;
                count += uvCount;
            }
            return count;
        }
    }
}
