// NormalHelper.cs
// 面法線計算の共通ヘルパー
// 縮退三角形（面積ゼロ・同一直線上の頂点）でゼロ法線を返さないよう保護

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class NormalHelper
    {
        /// <summary>
        /// 3頂点から面法線を計算。縮退三角形の場合はVector3.upを返す。
        /// </summary>
        public static Vector3 CalculateFaceNormal(Vector3 p0, Vector3 p1, Vector3 p2)
        {
            Vector3 cross = Vector3.Cross((p1 - p0).normalized, (p2 - p0).normalized);
            if (cross.sqrMagnitude < 1e-6f)
                return Vector3.up;
            return cross.normalized;
        }

        /// <summary>
        /// Unity Mesh 側で法線を再計算した後、除外セットの分だけ MeshObject 由来の
        /// 法線へ書き戻す。Unity Mesh は法線を頂点単位でしか持てないため、
        /// 面指定の除外はその構成頂点へ展開される。
        /// </summary>
        /// <param name="recalculated">再計算後の展開法線配列（書き換え対象）</param>
        /// <param name="source">MeshObject 由来の展開法線配列（ToUnityMeshShared の出力）</param>
        /// <param name="meshObject">除外セットの保持元</param>
        /// <returns>1件でも書き戻したら true</returns>
        public static bool RestoreExcludedNormals(
            Vector3[] recalculated, Vector3[] source, MeshObject meshObject)
        {
            if (recalculated == null || source == null || meshObject == null) return false;
            if (recalculated.Length != source.Length) return false;
            if (!meshObject.HasNormalRecalcExclude) return false;

            var excluded = meshObject.GetNormalRecalcExcludedVertexIndices();
            if (excluded.Count == 0) return false;

            // 展開順序は ToUnityMeshShared / ToUnityMesh と同一。
            var map = meshObject.BuildExpansionMap();
            bool changed = false;

            foreach (int vIdx in excluded)
            {
                if (vIdx < 0 || vIdx >= meshObject.Vertices.Count) continue;
                int uvCount = meshObject.Vertices[vIdx].UVs.Count;
                if (uvCount <= 0) uvCount = 1;

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    if (!map.TryGetValue((vIdx, uvIdx), out int expandedIdx)) continue;
                    if (expandedIdx < 0 || expandedIdx >= recalculated.Length) continue;

                    recalculated[expandedIdx] = source[expandedIdx];
                    changed = true;
                }
            }
            return changed;
        }
    }
}
