// UvUnwrapOps.cs
// UV展開ユーティリティ
// PolyLing_CommandHandlers_UV.cs から分離（CommandHandlers削除に伴い独立ファイル化）

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Ops
{
    internal static class UvUnwrapOps
    {
        // ----------------------------------------------------------------
        // UV展開
        // ----------------------------------------------------------------

        public static void UnwrapMesh(MeshObject meshObj, ProjectionType proj,
            float scale, float offsetU, float offsetV)
        {
            if (meshObj.VertexCount == 0 || meshObj.FaceCount == 0) return;

            Bounds bounds = meshObj.CalculateBounds();
            Vector3 size  = bounds.size;
            if (size.x < 0.0001f) size.x = 1f;
            if (size.y < 0.0001f) size.y = 1f;
            if (size.z < 0.0001f) size.z = 1f;

            switch (proj)
            {
                case ProjectionType.PlanarXY:
                    UnwrapPlanar(meshObj, bounds, 0, 1, scale, offsetU, offsetV); break;
                case ProjectionType.PlanarXZ:
                    UnwrapPlanar(meshObj, bounds, 0, 2, scale, offsetU, offsetV); break;
                case ProjectionType.PlanarYZ:
                    UnwrapPlanar(meshObj, bounds, 1, 2, scale, offsetU, offsetV); break;
                case ProjectionType.Box:
                    UnwrapBox(meshObj, bounds, scale, offsetU, offsetV); break;
                case ProjectionType.Cylindrical:
                    UnwrapCylindrical(meshObj, bounds, scale, offsetU, offsetV); break;
                case ProjectionType.Spherical:
                    UnwrapSpherical(meshObj, bounds, scale, offsetU, offsetV); break;
            }
        }

        private static void UnwrapPlanar(MeshObject meshObj, Bounds bounds,
            int axisU, int axisV, float scale, float offsetU, float offsetV)
        {
            Vector3 bMin = bounds.min;
            float sizeU  = bounds.size[axisU]; if (sizeU < 0.0001f) sizeU = 1f;
            float sizeV  = bounds.size[axisV]; if (sizeV < 0.0001f) sizeV = 1f;

            foreach (var vertex in meshObj.Vertices)
            {
                float u = (vertex.Position[axisU] - bMin[axisU]) / sizeU * scale + offsetU;
                float v = (vertex.Position[axisV] - bMin[axisV]) / sizeV * scale + offsetV;
                SetVertexUV(vertex, new Vector2(u, v));
            }
            ResetFaceUVIndices(meshObj);
        }

        private static void UnwrapBox(MeshObject meshObj, Bounds bounds,
            float scale, float offsetU, float offsetV)
        {
            Vector3 bMin  = bounds.min;
            Vector3 bSize = bounds.size;

            foreach (var face in meshObj.Faces)
            {
                if (face == null || face.VertexCount < 3) continue;

                Vector3 normal = ComputeFaceNormal(meshObj, face);
                int dominant   = GetDominantAxis(normal);

                int axisU, axisV;
                switch (dominant)
                {
                    case 0: axisU = 1; axisV = 2; break;
                    case 1: axisU = 0; axisV = 2; break;
                    default: axisU = 0; axisV = 1; break;
                }

                float sizeU = bSize[axisU]; if (sizeU < 0.0001f) sizeU = 1f;
                float sizeV = bSize[axisV]; if (sizeV < 0.0001f) sizeV = 1f;

                for (int ci = 0; ci < face.VertexCount; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (vi < 0 || vi >= meshObj.VertexCount) continue;

                    var vertex = meshObj.Vertices[vi];
                    float u = (vertex.Position[axisU] - bMin[axisU]) / sizeU * scale + offsetU;
                    float v = (vertex.Position[axisV] - bMin[axisV]) / sizeV * scale + offsetV;

                    int uvIdx = vertex.GetOrAddUV(new Vector2(u, v));
                    while (face.UVIndices.Count <= ci) face.UVIndices.Add(0);
                    face.UVIndices[ci] = uvIdx;
                }
            }
        }

        private static void UnwrapCylindrical(MeshObject meshObj, Bounds bounds,
            float scale, float offsetU, float offsetV)
        {
            Vector3 center = bounds.center;
            float height   = bounds.size.y; if (height < 0.0001f) height = 1f;
            float bMinY    = bounds.min.y;

            foreach (var vertex in meshObj.Vertices)
            {
                float dx = vertex.Position.x - center.x;
                float dz = vertex.Position.z - center.z;
                float u  = ((Mathf.Atan2(dz, dx) + Mathf.PI) / (2f * Mathf.PI)) * scale + offsetU;
                float v  = ((vertex.Position.y - bMinY) / height) * scale + offsetV;
                SetVertexUV(vertex, new Vector2(u, v));
            }
            ResetFaceUVIndices(meshObj);
        }

        private static void UnwrapSpherical(MeshObject meshObj, Bounds bounds,
            float scale, float offsetU, float offsetV)
        {
            Vector3 center = bounds.center;

            foreach (var vertex in meshObj.Vertices)
            {
                Vector3 dir = (vertex.Position - center).normalized;
                if (dir.sqrMagnitude < 0.0001f) dir = Vector3.up;

                float u = ((Mathf.Atan2(dir.z, dir.x) + Mathf.PI) / (2f * Mathf.PI)) * scale + offsetU;
                float v = (Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) / Mathf.PI + 0.5f) * scale + offsetV;
                SetVertexUV(vertex, new Vector2(u, v));
            }
            ResetFaceUVIndices(meshObj);
        }

        // ----------------------------------------------------------------
        // UVZ展開メッシュ生成
        // ----------------------------------------------------------------

        public static MeshObject BuildUvzMesh(MeshObject src,
            float uvScale, float depthScale, Vector3 camPos, Vector3 camForward)
        {
            var vertexMapping = new Dictionary<(int vi, int uvIdx), int>();
            var newVertices   = new List<Vertex>();

            for (int vIdx = 0; vIdx < src.Vertices.Count; vIdx++)
            {
                var srcVert = src.Vertices[vIdx];
                int uvCount = Mathf.Max(srcVert.UVs.Count, 1);
                float depth = Vector3.Dot(srcVert.Position - camPos, camForward);

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    vertexMapping[(vIdx, uvIdx)] = newVertices.Count;

                    Vector2 uv = uvIdx < srcVert.UVs.Count ? srcVert.UVs[uvIdx] : Vector2.zero;

                    var newVert = new Vertex(new Vector3(
                        uv.x * uvScale, uv.y * uvScale, depth * depthScale));
                    newVert.UVs.Add(uv);
                    newVert.Normals.Add(-camForward);
                    newVertices.Add(newVert);
                }
            }

            var newFaces = new List<Face>();
            foreach (var srcFace in src.Faces)
            {
                if (srcFace == null || srcFace.VertexCount < 2) continue;

                var newFace = new Face();
                newFace.MaterialIndex = srcFace.MaterialIndex;
                newFace.Flags = srcFace.Flags;

                for (int ci = 0; ci < srcFace.VertexCount; ci++)
                {
                    int origVi   = srcFace.VertexIndices[ci];
                    int uvSubIdx = ci < srcFace.UVIndices.Count ? srcFace.UVIndices[ci] : 0;

                    if (!vertexMapping.TryGetValue((origVi, uvSubIdx), out int newVi))
                        if (!vertexMapping.TryGetValue((origVi, 0), out newVi))
                            newVi = 0;

                    newFace.VertexIndices.Add(newVi);
                    newFace.UVIndices.Add(0);
                    newFace.NormalIndices.Add(0);
                }
                newFaces.Add(newFace);
            }

            var newMeshObj = new MeshObject($"{src.Name}_UVZ");
            newMeshObj.Vertices = newVertices;
            newMeshObj.Faces    = newFaces;
            newMeshObj.Type     = MeshType.Mesh;
            newMeshObj.AssignMissingIds();
            return newMeshObj;
        }

        // ----------------------------------------------------------------
        // XYZ→UV書き戻し
        // ----------------------------------------------------------------

        public static void WritebackXyzToUv(MeshObject src, MeshObject target, float uvScale)
        {
            if (uvScale < 0.001f) uvScale = 1f;

            int srcIdx = 0;
            foreach (var targetVert in target.Vertices)
            {
                int uvCount = Mathf.Max(targetVert.UVs.Count, 1);
                targetVert.UVs.Clear();

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    if (srcIdx < src.Vertices.Count)
                    {
                        var sv = src.Vertices[srcIdx];
                        targetVert.UVs.Add(new Vector2(sv.Position.x / uvScale, sv.Position.y / uvScale));
                        srcIdx++;
                    }
                    else
                    {
                        targetVert.UVs.Add(Vector2.zero);
                    }
                }
            }
        }

        // ----------------------------------------------------------------
        // 共通ヘルパー
        // ----------------------------------------------------------------

        private static void SetVertexUV(Vertex vertex, Vector2 uv)
        {
            if (vertex.UVs.Count == 0) vertex.UVs.Add(uv);
            else vertex.UVs[0] = uv;
        }

        private static void ResetFaceUVIndices(MeshObject meshObj)
        {
            foreach (var face in meshObj.Faces)
            {
                if (face == null) continue;
                for (int i = 0; i < face.UVIndices.Count; i++)
                    face.UVIndices[i] = 0;
            }
        }

        private static Vector3 ComputeFaceNormal(MeshObject meshObj, Face face)
        {
            if (face.VertexCount < 3) return Vector3.up;
            int i0 = face.VertexIndices[0];
            int i1 = face.VertexIndices[1];
            int i2 = face.VertexIndices[2];
            if (i0 < 0 || i0 >= meshObj.VertexCount ||
                i1 < 0 || i1 >= meshObj.VertexCount ||
                i2 < 0 || i2 >= meshObj.VertexCount) return Vector3.up;
            return NormalHelper.CalculateFaceNormal(
                meshObj.Vertices[i0].Position,
                meshObj.Vertices[i1].Position,
                meshObj.Vertices[i2].Position);
        }

        private static int GetDominantAxis(Vector3 n)
        {
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ax >= ay && ax >= az) return 0;
            if (ay >= ax && ay >= az) return 1;
            return 2;
        }
    }
}
