// NohMaskMeshExporter.cs
// 既存メッシュを能面(NohMask)JSON形式(landmarks + triangles)へ書き出す。
// 本質的に能面とは無関係で、JSONスキーマを汎用コンテナとして流用する。
// 座標は頂点 Position をそのまま x/y/z へ格納する（生座標。中心引き算/Scale/反転は行わない）。
// NohMaskMeshGenerator と対で Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置。

using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.NohMask
{
    /// <summary>MeshObject を能面JSON形式(landmarks/triangles)へ書き出すユーティリティ。</summary>
    public static class NohMaskMeshExporter
    {
        /// <summary>
        /// landmarks JSON（FaceLandmarksJson 形式）を生成する。
        /// 各頂点の Position を生座標のまま x/y/z へ格納（index=頂点順、pixel_x/y=0）。
        /// ネスト配列を含まないため JsonUtility.ToJson で出力する。
        /// </summary>
        public static string BuildLandmarksJson(MeshObject mo)
        {
            var data = new FaceLandmarksJson
            {
                schema             = "mediapipe.face_landmarker",
                num_faces_detected = 1,
            };

            int n = mo?.VertexCount ?? 0;
            var lms = new Landmark[n];
            for (int i = 0; i < n; i++)
            {
                var pos = mo.Vertices[i].Position;
                lms[i] = new Landmark
                {
                    index   = i,
                    x       = pos.x,
                    y       = pos.y,
                    z       = pos.z,
                    pixel_x = 0f,
                    pixel_y = 0f,
                };
            }

            data.faces = new[]
            {
                new FaceData
                {
                    face_index = 0,
                    image      = new ImageData { path = "", width = 0, height = 0 },
                    landmarks  = lms,
                }
            };

            return JsonUtility.ToJson(data, true);
        }

        /// <summary>
        /// triangles JSON（FaceMeshTrianglesJson 形式）を生成する。
        /// 面は扇形三角形化し [i0, i1, i2] 列として出力する。
        /// ネスト int[][] は JsonUtility で出力できないため手動整形（生成側の Regex 形式に一致）。
        /// </summary>
        public static string BuildTrianglesJson(MeshObject mo)
        {
            int vertexCount = mo?.VertexCount ?? 0;

            var tris = new List<int[]>();
            if (mo?.Faces != null)
            {
                foreach (var face in mo.Faces)
                {
                    if (face == null) continue;
                    foreach (var t in face.Triangulate())
                    {
                        if (t.VertexCount != 3) continue;
                        int a = t.VertexIndices[0], b = t.VertexIndices[1], c = t.VertexIndices[2];
                        if (a < 0 || b < 0 || c < 0) continue;
                        tris.Add(new[] { a, b, c });
                    }
                }
            }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"source\": \"PolyLing.mesh_export\",\n");
            sb.Append("  \"triangle_count\": ").Append(tris.Count).Append(",\n");
            sb.Append("  \"vertex_count\": ").Append(vertexCount).Append(",\n");
            sb.Append("  \"triangles\": [\n");
            for (int i = 0; i < tris.Count; i++)
            {
                var t = tris[i];
                sb.Append("    [").Append(t[0]).Append(", ").Append(t[1]).Append(", ").Append(t[2]).Append("]");
                sb.Append(i < tris.Count - 1 ? ",\n" : "\n");
            }
            sb.Append("  ]\n");
            sb.Append("}\n");
            return sb.ToString();
        }
    }
}
