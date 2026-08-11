// AttributeSelectOps.cs
// 属性ベースの頂点選択（クリック非依存）。
// AdvancedSelectMode.UvNormalCount / NearAxis の判定本体。
// Runtime/Editor 共通。#if UNITY_EDITOR 不使用。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 属性ベースの頂点選択。
    /// 画面座標を使わないため CPU ヒットテスト（SelectionHelper.FindNearest*）とは無関係で、
    /// GPU ホバー経路の制約も受けない。
    /// </summary>
    public static class AttributeSelectOps
    {
        /// <summary>
        /// UV/法線スロット数がしきい値より大きい頂点を列挙する。
        ///
        /// 判定値は max(Vertex.UVs.Count, Vertex.Normals.Count)。
        /// MeshObject の不変条件では UVs.Count == Normals.Count（MeshObject.GetOrAddUVNormal の
        /// 説明を参照）だが、崩れたデータでも取りこぼさないよう大きい方を採る。
        /// </summary>
        /// <param name="mesh">対象メッシュ</param>
        /// <param name="threshold">しきい値。判定値がこれ「より大きい」頂点を選ぶ</param>
        /// <param name="limitToCurrentSelection">true なら currentVertices に含まれる頂点のみ対象</param>
        /// <param name="currentVertices">現在の選択頂点。limitToCurrentSelection が false なら未使用</param>
        public static List<int> SelectByUvNormalCount(
            MeshObject mesh,
            int threshold,
            bool limitToCurrentSelection,
            HashSet<int> currentVertices)
        {
            var result = new List<int>();
            if (mesh == null) return result;

            if (limitToCurrentSelection && (currentVertices == null || currentVertices.Count == 0))
                return result;

            int count = mesh.VertexCount;
            for (int i = 0; i < count; i++)
            {
                if (limitToCurrentSelection && !currentVertices.Contains(i)) continue;

                var v = mesh.Vertices[i];
                if (v == null) continue;

                int uvCount = v.UVs != null ? v.UVs.Count : 0;
                int nrmCount = v.Normals != null ? v.Normals.Count : 0;
                int slots = Mathf.Max(uvCount, nrmCount);

                if (slots > threshold) result.Add(i);
            }

            return result;
        }

        /// <summary>
        /// 軸に対応する平面までの距離がしきい値未満の頂点を列挙する。
        ///
        /// SymmetryAxis.X なら YZ 平面までの距離＝|Position.x|。
        /// 軸の直線までの距離（sqrt(y^2+z^2) 等）ではない。
        /// </summary>
        /// <param name="mesh">対象メッシュ</param>
        /// <param name="axis">軸。X → |x|、Y → |y|、Z → |z|</param>
        /// <param name="threshold">しきい値。距離がこれ「未満」の頂点を選ぶ</param>
        /// <param name="limitToCurrentSelection">true なら currentVertices に含まれる頂点のみ対象</param>
        /// <param name="currentVertices">現在の選択頂点。limitToCurrentSelection が false なら未使用</param>
        public static List<int> SelectNearAxisPlane(
            MeshObject mesh,
            SymmetryAxis axis,
            float threshold,
            bool limitToCurrentSelection,
            HashSet<int> currentVertices)
        {
            var result = new List<int>();
            if (mesh == null) return result;

            if (limitToCurrentSelection && (currentVertices == null || currentVertices.Count == 0))
                return result;

            int count = mesh.VertexCount;
            for (int i = 0; i < count; i++)
            {
                if (limitToCurrentSelection && !currentVertices.Contains(i)) continue;

                var v = mesh.Vertices[i];
                if (v == null) continue;

                float d = PlaneDistance(v.Position, axis);
                if (d < threshold) result.Add(i);
            }

            return result;
        }

        /// <summary>軸に対応する平面までの距離（成分の絶対値）。</summary>
        private static float PlaneDistance(Vector3 pos, SymmetryAxis axis)
        {
            switch (axis)
            {
                case SymmetryAxis.Y: return Mathf.Abs(pos.y);
                case SymmetryAxis.Z: return Mathf.Abs(pos.z);
                default: return Mathf.Abs(pos.x);
            }
        }
    }
}
