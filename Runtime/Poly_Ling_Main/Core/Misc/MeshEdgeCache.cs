// Assets/Editor/Poly_Ling/Core/MeshEdgeCache.cs
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Rendering
{
    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct LineData
    {
        public int V1;
        public int V2;
        public int FaceIndex;
        public int LineType;
        public static int SizeInBytes => sizeof(int) * 4;
    }

    public class MeshEdgeCache
    {
        public List<LineData> Lines { get; private set; } = new List<LineData>();
        public List<(int v1, int v2)> UniqueEdges { get; private set; } = new List<(int, int)>();
        public List<(int v1, int v2)> AuxLines { get; private set; } = new List<(int, int)>();
        public int LineCount => Lines.Count;
        public int EdgeCount => UniqueEdges.Count;
        public int AuxLineCount => AuxLines.Count;

        private MeshObject _cachedMeshObject;

        public void Update(MeshObject meshObject, bool force = false)
        {
            if (meshObject == null) { Clear(); return; }
            if (!force && _cachedMeshObject == meshObject && Lines.Count > 0) return;

            _cachedMeshObject = meshObject;
            Lines.Clear();
            UniqueEdges.Clear();
            AuxLines.Clear();

            var edgeSet = new HashSet<(int, int)>();

            for (int faceIdx = 0; faceIdx < meshObject.FaceCount; faceIdx++)
            {
                var face = meshObject.Faces[faceIdx];
                if (face.VertexCount == 2)
                {
                    int v1 = face.VertexIndices[0];
                    int v2 = face.VertexIndices[1];
                    Lines.Add(new LineData { V1 = v1, V2 = v2, FaceIndex = faceIdx, LineType = 1 });
                    AuxLines.Add((v1, v2));
                }
                else if (face.VertexCount >= 3)
                {
                    for (int i = 0; i < face.VertexCount; i++)
                    {
                        int v1 = face.VertexIndices[i];
                        int v2 = face.VertexIndices[(i + 1) % face.VertexCount];
                        Lines.Add(new LineData { V1 = v1, V2 = v2, FaceIndex = faceIdx, LineType = 0 });
                        int a = v1, b = v2;
                        if (a > b) (a, b) = (b, a);
                        if (edgeSet.Add((a, b))) UniqueEdges.Add((a, b));
                    }
                }
            }
        }

        public void Invalidate() => _cachedMeshObject = null;
        public void Clear() { Lines.Clear(); UniqueEdges.Clear(); AuxLines.Clear(); _cachedMeshObject = null; }
    }
}
