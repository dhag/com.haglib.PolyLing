// Original CSG.JS library by Evan Wallace (http://madebyevan.com), under the MIT license.
// GitHub: https://github.com/evanw/csg.js/
//
// C++ port by Tomasz Dabrowski (http://28byteslater.com), under the MIT license.
// GitHub: https://github.com/dabroz/csgjs-cpp/
//
// C# port by Karl Henkel (parabox.co), under MIT license.
// GitHub: https://github.com/karl-/pb_CSG
//
// PolyLing 改変:
//   - namespace を Parabox.CSG -> Poly_Ling.CSG。
//   - GameObject / Transform / Material 依存を削除し、Model(Mesh, Matrix4x4) を追加。
//   - サブメッシュのキーを Material から int（サブメッシュ番号）へ変更。
//   - 元コードは Model(Mesh, ...) で非三角形サブメッシュを読み飛ばす際に
//     m_Indices の添字と実サブメッシュ番号がずれていた
//     （ToPolygons 側で m_Materials[s] と添字一致を前提にしていたため）。
//     実番号を m_MaterialIndices に記録して解消した。
//   - Mesh への変換で、サブメッシュ番号を材質番号そのものに揃える
//     （未使用番号は空サブメッシュにする）。PolyLing の FromUnityMesh は
//     サブメッシュ番号をそのまま Face.MaterialIndex にするため。
// 詳細は同フォルダの LICENSE.txt を参照。

using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace Poly_Ling.CSG
{
    /// <summary>
    /// Representation of a mesh in CSG terms. Contains methods for translating to and from UnityEngine.Mesh.
    /// </summary>
    public sealed class Model
    {
        List<Vertex> m_Vertices;

        /// <summary>
        /// m_Indices[i] が属するサブメッシュ番号。
        /// 元コードの m_Materials（List&lt;Material&gt;）を置き換えたもの。
        /// </summary>
        List<int> m_MaterialIndices;

        List<List<int>> m_Indices;

        public List<int> materialIndices
        {
            get { return m_MaterialIndices; }
            set { m_MaterialIndices = value; }
        }

        public List<Vertex> vertices
        {
            get { return m_Vertices; }
            set { m_Vertices = value; }
        }

        public List<List<int>> indices
        {
            get { return m_Indices; }
            set { m_Indices = value; }
        }

        public Mesh mesh
        {
            get { return (Mesh)this; }
        }

        /// <summary>
        /// Initialize a Model from a UnityEngine.Mesh and a transform matrix.
        /// </summary>
        /// <param name="mesh">元メッシュ。</param>
        /// <param name="transform">頂点に適用する変換行列（例: 対象ローカル → 演算空間）。</param>
        public Model(Mesh mesh, Matrix4x4 transform)
        {
            if (mesh == null)
                throw new ArgumentNullException("mesh");

            // 法線用の逆転置行列は頂点ごとではなく一度だけ作る。
            Matrix4x4 normalMatrix = transform.inverse.transpose;

            var src = VertexUtility.GetVertices(mesh);
            m_Vertices = new List<Vertex>(src.Length);
            for (int i = 0; i < src.Length; i++)
                m_Vertices.Add(VertexUtility.TransformVertex(transform, normalMatrix, src[i]));

            m_MaterialIndices = new List<int>();
            m_Indices = new List<List<int>>();

            for (int i = 0, c = mesh.subMeshCount; i < c; i++)
            {
                if (mesh.GetTopology(i) != MeshTopology.Triangles)
                    continue;

                var indices = new List<int>();
                mesh.GetIndices(indices, i);
                if (indices.Count < 3)
                    continue;

                m_Indices.Add(indices);
                // 読み飛ばしがあっても実サブメッシュ番号を保つ。
                m_MaterialIndices.Add(i);
            }
        }

        internal Model(List<Polygon> polygons)
        {
            m_Vertices = new List<Vertex>();

            var submeshes = new Dictionary<int, List<int>>();

            int p = 0;

            for (int i = 0; i < polygons.Count; i++)
            {
                Polygon poly = polygons[i];
                List<int> indices;

                if (!submeshes.TryGetValue(poly.materialIndex, out indices))
                    submeshes.Add(poly.materialIndex, indices = new List<int>());

                for (int j = 2; j < poly.vertices.Count; j++)
                {
                    m_Vertices.Add(poly.vertices[0]);
                    indices.Add(p++);

                    m_Vertices.Add(poly.vertices[j - 1]);
                    indices.Add(p++);

                    m_Vertices.Add(poly.vertices[j]);
                    indices.Add(p++);
                }
            }

            // Dictionary の列挙順に依存しないよう材質番号の昇順で確定させる。
            var keys = submeshes.Keys.ToList();
            keys.Sort();

            m_MaterialIndices = keys;
            m_Indices = new List<List<int>>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
                m_Indices.Add(submeshes[keys[i]]);
        }

        internal List<Polygon> ToPolygons()
        {
            List<Polygon> list = new List<Polygon>();

            for (int s = 0, c = m_Indices.Count; s < c; s++)
            {
                var indices = m_Indices[s];
                int materialIndex = m_MaterialIndices[s];

                for (int i = 0, ic = indices.Count; i + 2 < ic; i += 3)
                {
                    List<Vertex> triangle = new List<Vertex>()
                    {
                        m_Vertices[indices[i + 0]],
                        m_Vertices[indices[i + 1]],
                        m_Vertices[indices[i + 2]]
                    };

                    list.Add(new Polygon(triangle, materialIndex));
                }
            }

            return list;
        }

        public static explicit operator Mesh(Model model)
        {
            var mesh = new Mesh();

            // 結果が空（交差が無い等）のことがある。
            // VertexUtility.SetMesh は vertices[0] を読むため、先に弾く。
            if (model.m_Vertices == null || model.m_Vertices.Count == 0)
            {
                mesh.subMeshCount = 1;
                return mesh;
            }

            VertexUtility.SetMesh(mesh, model.m_Vertices);

            // サブメッシュ番号 = 材質番号 に揃える。
            // 使われていない番号は空のサブメッシュとして残す。
            int subMeshCount = 0;
            for (int i = 0; i < model.m_MaterialIndices.Count; i++)
                subMeshCount = Mathf.Max(subMeshCount, model.m_MaterialIndices[i] + 1);
            if (subMeshCount < 1)
                subMeshCount = 1;

            var buckets = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                buckets[i] = new List<int>();

            for (int i = 0; i < model.m_Indices.Count; i++)
            {
                int mat = model.m_MaterialIndices[i];
                if (mat < 0 || mat >= subMeshCount) continue;
                buckets[mat].AddRange(model.m_Indices[i]);
            }

            mesh.subMeshCount = subMeshCount;
            for (int i = 0; i < subMeshCount; i++)
                mesh.SetIndices(buckets[i], MeshTopology.Triangles, i);

            return mesh;
        }
    }
}
