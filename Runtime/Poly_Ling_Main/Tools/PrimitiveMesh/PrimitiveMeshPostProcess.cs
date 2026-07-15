// PrimitiveMeshPostProcess.cs
// 基本図形生成後の共有後処理（頂点並べ替え等）。Runtime / Editor 共有。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class PrimitiveMeshPostProcess
    {
        /// <summary>
        /// 頂点を Y 降順 → X 降順 → Z 降順（同値は元順で安定）に並べ替え、
        /// Face.VertexIndices を再マップする。
        /// Face.UVIndices / NormalIndices は各頂点の UVs / Normals へのサブ参照であり、
        /// 頂点は自身の UV/法線を伴って移動するため不変（書き換え不要）。
        /// </summary>
        public static void SortVerticesCanonical(MeshObject mo)
        {
            if (mo == null || mo.Vertices == null || mo.Vertices.Count < 2) return;

            int n = mo.Vertices.Count;
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;

            System.Array.Sort(order, (a, b) =>
            {
                Vector3 pa = mo.Vertices[a].Position, pb = mo.Vertices[b].Position;
                int c = pb.y.CompareTo(pa.y); if (c != 0) return c; // Y 降順
                c = pb.x.CompareTo(pa.x);     if (c != 0) return c; // X 降順
                c = pb.z.CompareTo(pa.z);     if (c != 0) return c; // Z 降順
                return a.CompareTo(b);                              // 同値は元順（決定的）
            });

            var newVerts = new List<Vertex>(n);
            var oldToNew = new int[n];
            for (int k = 0; k < n; k++)
            {
                newVerts.Add(mo.Vertices[order[k]]);
                oldToNew[order[k]] = k;
            }
            mo.Vertices = newVerts;

            if (mo.Faces == null) return;
            foreach (var f in mo.Faces)
            {
                if (f == null || f.VertexIndices == null) continue;
                for (int j = 0; j < f.VertexIndices.Count; j++)
                {
                    int oi = f.VertexIndices[j];
                    if (oi >= 0 && oi < n) f.VertexIndices[j] = oldToNew[oi];
                }
            }
        }
    }
}
