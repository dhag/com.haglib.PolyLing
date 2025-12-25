// Assets/Editor/MeshFactory/Tools/Selection/SelectionHelper.cs
// 選択処理の共通ヘルパー

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 選択処理の共通ヘルパー
    /// </summary>
    public static class SelectionHelper
    {
        // 定数
        public const float VERTEX_CLICK_THRESHOLD = 15f;
        public const float EDGE_CLICK_THRESHOLD = 10f;

        // ================================================================
        // ヒットテスト
        // ================================================================

        public static int FindNearestVertex(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshObject == null) return -1;

            float minDist = VERTEX_CLICK_THRESHOLD;
            int nearest = -1;

            for (int i = 0; i < ctx.MeshObject.VertexCount; i++)
            {
                Vector2 vScreen = ctx.WorldToScreen(ctx.MeshObject.Vertices[i].Position);
                float dist = Vector2.Distance(screenPos, vScreen);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }

        public static VertexPair? FindNearestEdgePair(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshObject == null) return null;

            if (ctx.TopologyCache != null)
            {
                float minDist = EDGE_CLICK_THRESHOLD;
                VertexPair? nearest = null;

                foreach (var pair in ctx.TopologyCache.AllEdgePairs)
                {
                    Vector2 p1 = ctx.WorldToScreen(ctx.MeshObject.Vertices[pair.V1].Position);
                    Vector2 p2 = ctx.WorldToScreen(ctx.MeshObject.Vertices[pair.V2].Position);
                    float dist = DistanceToLineSegment(screenPos, p1, p2);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = pair;
                    }
                }

                return nearest;
            }

            var legacy = FindNearestEdgeLegacy(ctx, screenPos);
            if (legacy.Item1 >= 0)
                return new VertexPair(legacy.Item1, legacy.Item2);

            return null;
        }

        public static (int, int) FindNearestEdgeLegacy(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshObject == null) return (-1, -1);

            float minDist = EDGE_CLICK_THRESHOLD;
            (int, int) nearest = (-1, -1);

            foreach (var face in ctx.MeshObject.Faces)
            {
                int n = face.VertexCount;
                if (n < 2) continue;

                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    Vector2 p1 = ctx.WorldToScreen(ctx.MeshObject.Vertices[v1].Position);
                    Vector2 p2 = ctx.WorldToScreen(ctx.MeshObject.Vertices[v2].Position);
                    float dist = DistanceToLineSegment(screenPos, p1, p2);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = v1 < v2 ? (v1, v2) : (v2, v1);
                    }
                }
            }

            return nearest;
        }

        public static int FindNearestFace(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshObject == null) return -1;

            int nearest = -1;
            float nearestDepth = float.MaxValue;

            for (int faceIdx = 0; faceIdx < ctx.MeshObject.FaceCount; faceIdx++)
            {
                var face = ctx.MeshObject.Faces[faceIdx];
                if (face.VertexCount < 3) continue;

                var screenPoints = new Vector2[face.VertexCount];
                Vector3 centroid = Vector3.zero;

                for (int i = 0; i < face.VertexCount; i++)
                {
                    var worldPos = ctx.MeshObject.Vertices[face.VertexIndices[i]].Position;
                    screenPoints[i] = ctx.WorldToScreen(worldPos);
                    centroid += worldPos;
                }
                centroid /= face.VertexCount;

                if (IsPointInPolygon(screenPos, screenPoints))
                {
                    float depth = Vector3.Distance(ctx.CameraPosition, centroid);
                    if (depth < nearestDepth)
                    {
                        nearestDepth = depth;
                        nearest = faceIdx;
                    }
                }
            }

            return nearest;
        }

        public static int FindNearestLine(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshObject == null) return -1;

            float minDist = EDGE_CLICK_THRESHOLD;
            int nearest = -1;

            for (int faceIdx = 0; faceIdx < ctx.MeshObject.FaceCount; faceIdx++)
            {
                var face = ctx.MeshObject.Faces[faceIdx];
                if (face.VertexCount != 2) continue;

                Vector2 p1 = ctx.WorldToScreen(ctx.MeshObject.Vertices[face.VertexIndices[0]].Position);
                Vector2 p2 = ctx.WorldToScreen(ctx.MeshObject.Vertices[face.VertexIndices[1]].Position);
                float dist = DistanceToLineSegment(screenPos, p1, p2);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = faceIdx;
                }
            }

            return nearest;
        }

        // ================================================================
        // 隣接関係構築
        // ================================================================

        public static Dictionary<int, HashSet<int>> BuildVertexAdjacency(MeshObject meshObject)
        {
            var adjacency = new Dictionary<int, HashSet<int>>();

            foreach (var face in meshObject.Faces)
            {
                int n = face.VertexIndices.Count;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    if (!adjacency.ContainsKey(v1))
                        adjacency[v1] = new HashSet<int>();
                    if (!adjacency.ContainsKey(v2))
                        adjacency[v2] = new HashSet<int>();

                    adjacency[v1].Add(v2);
                    adjacency[v2].Add(v1);
                }
            }

            return adjacency;
        }

        public static Dictionary<VertexPair, HashSet<VertexPair>> BuildEdgeAdjacency(ToolContext ctx)
        {
            var adjacency = new Dictionary<VertexPair, HashSet<VertexPair>>();
            var vertexToEdges = new Dictionary<int, List<VertexPair>>();

            var allEdges = new HashSet<VertexPair>();
            foreach (var face in ctx.MeshObject.Faces)
            {
                int n = face.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    var edge = new VertexPair(face.VertexIndices[i], face.VertexIndices[(i + 1) % n]);
                    allEdges.Add(edge);
                }
            }

            foreach (var edge in allEdges)
            {
                if (!vertexToEdges.ContainsKey(edge.V1))
                    vertexToEdges[edge.V1] = new List<VertexPair>();
                if (!vertexToEdges.ContainsKey(edge.V2))
                    vertexToEdges[edge.V2] = new List<VertexPair>();

                vertexToEdges[edge.V1].Add(edge);
                vertexToEdges[edge.V2].Add(edge);
            }

            foreach (var edge in allEdges)
            {
                adjacency[edge] = new HashSet<VertexPair>();

                if (vertexToEdges.TryGetValue(edge.V1, out var list1))
                {
                    foreach (var neighbor in list1)
                        if (neighbor != edge) adjacency[edge].Add(neighbor);
                }
                if (vertexToEdges.TryGetValue(edge.V2, out var list2))
                {
                    foreach (var neighbor in list2)
                        if (neighbor != edge) adjacency[edge].Add(neighbor);
                }
            }

            return adjacency;
        }

        public static Dictionary<int, HashSet<int>> BuildFaceAdjacency(MeshObject meshObject)
        {
            var adjacency = new Dictionary<int, HashSet<int>>();
            var edgeToFaces = new Dictionary<VertexPair, List<int>>();

            for (int fIdx = 0; fIdx < meshObject.FaceCount; fIdx++)
            {
                var face = meshObject.Faces[fIdx];
                if (face.VertexCount < 3) continue;

                adjacency[fIdx] = new HashSet<int>();

                int n = face.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    var edge = new VertexPair(face.VertexIndices[i], face.VertexIndices[(i + 1) % n]);
                    if (!edgeToFaces.ContainsKey(edge))
                        edgeToFaces[edge] = new List<int>();
                    edgeToFaces[edge].Add(fIdx);
                }
            }

            foreach (var kvp in edgeToFaces)
            {
                var faces = kvp.Value;
                for (int i = 0; i < faces.Count; i++)
                {
                    for (int j = i + 1; j < faces.Count; j++)
                    {
                        adjacency[faces[i]].Add(faces[j]);
                        adjacency[faces[j]].Add(faces[i]);
                    }
                }
            }

            return adjacency;
        }

        public static Dictionary<int, HashSet<int>> BuildLineAdjacency(MeshObject meshObject)
        {
            var adjacency = new Dictionary<int, HashSet<int>>();
            var vertexToLines = new Dictionary<int, List<int>>();

            for (int fIdx = 0; fIdx < meshObject.FaceCount; fIdx++)
            {
                var face = meshObject.Faces[fIdx];
                if (face.VertexCount != 2) continue;

                adjacency[fIdx] = new HashSet<int>();

                int v1 = face.VertexIndices[0];
                int v2 = face.VertexIndices[1];

                if (!vertexToLines.ContainsKey(v1))
                    vertexToLines[v1] = new List<int>();
                if (!vertexToLines.ContainsKey(v2))
                    vertexToLines[v2] = new List<int>();

                vertexToLines[v1].Add(fIdx);
                vertexToLines[v2].Add(fIdx);
            }

            foreach (int fIdx in adjacency.Keys.ToList())
            {
                var face = meshObject.Faces[fIdx];
                if (face.VertexCount != 2) continue;

                if (vertexToLines.TryGetValue(face.VertexIndices[0], out var list1))
                {
                    foreach (var neighbor in list1)
                        if (neighbor != fIdx) adjacency[fIdx].Add(neighbor);
                }
                if (vertexToLines.TryGetValue(face.VertexIndices[1], out var list2))
                {
                    foreach (var neighbor in list2)
                        if (neighbor != fIdx) adjacency[fIdx].Add(neighbor);
                }
            }

            return adjacency;
        }

        public static Dictionary<VertexPair, List<int>> BuildEdgeToFacesMap(MeshObject meshObject)
        {
            var map = new Dictionary<VertexPair, List<int>>();

            for (int faceIdx = 0; faceIdx < meshObject.FaceCount; faceIdx++)
            {
                var face = meshObject.Faces[faceIdx];
                int n = face.VertexIndices.Count;
                if (n < 2) continue;

                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];
                    var key = new VertexPair(v1, v2);

                    if (!map.ContainsKey(key))
                        map[key] = new List<int>();

                    map[key].Add(faceIdx);
                }
            }

            return map;
        }

        // ================================================================
        // 変換ヘルパー
        // ================================================================

        public static List<VertexPair> GetEdgesFromVertices(ToolContext ctx, List<int> vertices)
        {
            var vertSet = new HashSet<int>(vertices);
            var result = new List<VertexPair>();

            foreach (var face in ctx.MeshObject.Faces)
            {
                int n = face.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    if (vertSet.Contains(v1) && vertSet.Contains(v2))
                    {
                        var edge = new VertexPair(v1, v2);
                        if (!result.Contains(edge))
                            result.Add(edge);
                    }
                }
            }

            return result;
        }

        public static List<int> GetFacesFromVertices(ToolContext ctx, List<int> vertices)
        {
            var vertSet = new HashSet<int>(vertices);
            var result = new List<int>();

            for (int fIdx = 0; fIdx < ctx.MeshObject.FaceCount; fIdx++)
            {
                var face = ctx.MeshObject.Faces[fIdx];
                if (face.VertexCount < 3) continue;

                bool allIn = face.VertexIndices.All(v => vertSet.Contains(v));
                if (allIn)
                    result.Add(fIdx);
            }

            return result;
        }

        public static List<int> GetLinesFromVertices(ToolContext ctx, List<int> vertices)
        {
            var vertSet = new HashSet<int>(vertices);
            var result = new List<int>();

            for (int fIdx = 0; fIdx < ctx.MeshObject.FaceCount; fIdx++)
            {
                var face = ctx.MeshObject.Faces[fIdx];
                if (face.VertexCount != 2) continue;

                if (vertSet.Contains(face.VertexIndices[0]) && vertSet.Contains(face.VertexIndices[1]))
                    result.Add(fIdx);
            }

            return result;
        }

        public static List<VertexPair> GetEdgesFromFaces(ToolContext ctx, List<int> faces)
        {
            var result = new HashSet<VertexPair>();

            foreach (int fIdx in faces)
            {
                var face = ctx.MeshObject.Faces[fIdx];
                int n = face.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    var edge = new VertexPair(face.VertexIndices[i], face.VertexIndices[(i + 1) % n]);
                    result.Add(edge);
                }
            }

            return result.ToList();
        }

        public static List<int> GetAdjacentFaces(ToolContext ctx, List<VertexPair> edges)
        {
            var edgeToFaces = BuildEdgeToFacesMap(ctx.MeshObject);
            var result = new HashSet<int>();

            foreach (var edge in edges)
            {
                if (edgeToFaces.TryGetValue(edge, out var faces))
                {
                    foreach (int f in faces)
                        result.Add(f);
                }
            }

            return result.ToList();
        }

        public static List<VertexPair> GetEdgesFromPath(List<int> path)
        {
            var result = new List<VertexPair>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                result.Add(new VertexPair(path[i], path[i + 1]));
            }
            return result;
        }

        // ================================================================
        // 選択適用
        // ================================================================

        public static void ApplyVertexSelection(ToolContext ctx, List<int> vertices, bool addToSelection)
        {
            var state = ctx.SelectionState;
            if (state != null)
            {
                foreach (int v in vertices)
                {
                    if (addToSelection)
                        state.Vertices.Add(v);
                    else
                        state.Vertices.Remove(v);
                }
            }

            if (ctx.SelectedVertices != null)
            {
                foreach (int v in vertices)
                {
                    if (addToSelection)
                        ctx.SelectedVertices.Add(v);
                    else
                        ctx.SelectedVertices.Remove(v);
                }
            }

            ctx.Repaint?.Invoke();
        }

        public static void ApplyEdgeSelection(ToolContext ctx, List<VertexPair> edges, bool addToSelection)
        {
            var state = ctx.SelectionState;
            if (state == null) return;

            foreach (var edge in edges)
            {
                if (addToSelection)
                    state.Edges.Add(edge);
                else
                    state.Edges.Remove(edge);
            }

            ctx.Repaint?.Invoke();
        }

        public static void ApplyFaceSelection(ToolContext ctx, List<int> faces, bool addToSelection)
        {
            var state = ctx.SelectionState;
            if (state == null) return;

            foreach (int f in faces)
            {
                if (addToSelection)
                    state.Faces.Add(f);
                else
                    state.Faces.Remove(f);
            }

            ctx.Repaint?.Invoke();
        }

        public static void ApplyLineSelection(ToolContext ctx, List<int> lines, bool addToSelection)
        {
            var state = ctx.SelectionState;
            if (state == null) return;

            foreach (int l in lines)
            {
                if (addToSelection)
                    state.Lines.Add(l);
                else
                    state.Lines.Remove(l);
            }

            ctx.Repaint?.Invoke();
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        public static float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float len = line.magnitude;
            if (len < 0.001f) return Vector2.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (len * len));
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }

        public static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return false;

            bool inside = false;
            int j = polygon.Length - 1;

            for (int i = 0; i < polygon.Length; i++)
            {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) /
                              (polygon[j].y - polygon[i].y) + polygon[i].x)
                {
                    inside = !inside;
                }
                j = i;
            }

            return inside;
        }
    }
}
