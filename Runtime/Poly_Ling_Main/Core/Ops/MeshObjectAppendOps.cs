// MeshObjectAppendOps.cs
// MeshObject の連結ユーティリティ。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【移設元】Runtime/Poly_Ling_Player/View/PrimitiveMesh/PlayerPrimitiveMeshSubPanel.BeltProfile.cs
//   AppendMesh    (private static) → Append
//   CombineMeshes (private static) → Combine
//   処理内容は移設元のまま。パネル固有の依存は元から無い。
//
// 【座標】頂点のローカル座標をそのまま連結する。BoneTransform は考慮しない
//   （既存の配置が source.Vertices[v].Position を直接使うのと同じ扱い）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>MeshObject の連結。</summary>
    public static class MeshObjectAppendOps
    {
        /// <summary>src の頂点・面を dst へ連結する（UVスロットとマテリアルは元のまま）。</summary>
        public static void Append(MeshObject dst, MeshObject src)
        {
            if (dst == null || src == null || src.VertexCount == 0) return;

            int baseIdx = dst.VertexCount;

            for (int v = 0; v < src.VertexCount; v++)
            {
                var sv = src.Vertices[v];
                var nv = new Vertex(sv.Position);
                if (sv.UVs != null)
                    for (int k = 0; k < sv.UVs.Count; k++) nv.UVs.Add(sv.UVs[k]);
                if (nv.UVs.Count == 0) nv.UVs.Add(Vector2.zero);

                // 部品ID / サブIDは連結で失わない。
                nv.PartsId = sv.PartsId;
                nv.SubId   = sv.SubId;

                dst.Vertices.Add(nv);
            }

            for (int f = 0; f < src.FaceCount; f++)
            {
                var sf = src.Faces[f];
                if (sf?.VertexIndices == null || sf.VertexIndices.Count < 3) continue;

                var nf = new Face { MaterialIndex = sf.MaterialIndex };
                for (int k = 0; k < sf.VertexIndices.Count; k++)
                {
                    nf.VertexIndices.Add(baseIdx + sf.VertexIndices[k]);
                    nf.UVIndices.Add(sf.UVIndices != null && k < sf.UVIndices.Count ? sf.UVIndices[k] : 0);
                    nf.NormalIndices.Add(0);
                }
                dst.AddFace(nf);
            }
        }

        /// <summary>
        /// 複数メッシュを1つへ連結する。頂点ローカル座標をそのまま連結する
        /// （既存の配置が source.Vertices[v].Position を直接使うのと同じ扱いで、BoneTransform は考慮しない）。
        /// </summary>
        public static MeshObject Combine(IReadOnlyList<MeshObject> sources, string meshName)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(meshName) ? "Combined" : meshName);
            if (sources == null) return mo;
            foreach (var s in sources) Append(mo, s);
            return mo;
        }
    }
}
