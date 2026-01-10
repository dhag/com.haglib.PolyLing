// Assets/Editor/MeshFactory/Tools/Selection/Modes/ShortestPathSelectMode.cs
// 最短ルート選択モード
// ローカライズ対応版

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;
using static MeshFactory.Tools.SelectModeTexts;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 最短ルート選択モード
    /// </summary>
    public class ShortestPathSelectMode : IAdvancedSelectMode
    {
        private int _firstVertex = -1;

        public int FirstVertex => _firstVertex;

        public bool HandleClick(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            int vIdx = SelectionHelper.FindNearestVertex(toolCtx, mousePos);
            if (vIdx < 0) return false;

            if (_firstVertex < 0)
            {
                _firstVertex = vIdx;
                return true;
            }
            else
            {
                var path = GetShortestPath(toolCtx.MeshObject, _firstVertex, vIdx);

                if (selectMode.Has(MeshSelectMode.Vertex))
                    SelectionHelper.ApplyVertexSelection(toolCtx, path, ctx.AddToSelection);

                if (selectMode.Has(MeshSelectMode.Edge))
                {
                    var pathEdges = SelectionHelper.GetEdgesFromPath(path);
                    SelectionHelper.ApplyEdgeSelection(toolCtx, pathEdges, ctx.AddToSelection);
                }

                if (selectMode.Has(MeshSelectMode.Face))
                {
                    var pathEdges = SelectionHelper.GetEdgesFromPath(path);
                    var faces = SelectionHelper.GetAdjacentFaces(toolCtx, pathEdges);
                    SelectionHelper.ApplyFaceSelection(toolCtx, faces, ctx.AddToSelection);
                }

                _firstVertex = -1;
                return true;
            }
        }

        public void UpdatePreview(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            ctx.HoveredVertex = SelectionHelper.FindNearestVertex(toolCtx, mousePos);

            if (_firstVertex >= 0 && ctx.HoveredVertex >= 0 && _firstVertex != ctx.HoveredVertex)
            {
                ctx.PreviewPath.AddRange(GetShortestPath(toolCtx.MeshObject, _firstVertex, ctx.HoveredVertex));

                if (selectMode.Has(MeshSelectMode.Edge))
                    ctx.PreviewEdges.AddRange(SelectionHelper.GetEdgesFromPath(ctx.PreviewPath));

                if (selectMode.Has(MeshSelectMode.Face))
                {
                    var pathEdges = SelectionHelper.GetEdgesFromPath(ctx.PreviewPath);
                    ctx.PreviewFaces.AddRange(SelectionHelper.GetAdjacentFaces(toolCtx, pathEdges));
                }
            }
        }

        public void Reset()
        {
            _firstVertex = -1;
        }

        public void ClearFirstPoint()
        {
            _firstVertex = -1;
        }

        public void DrawModeSettingsUI()
        {
            EditorGUILayout.HelpBox(T("ShortestPathHelp"), MessageType.Info);

            if (_firstVertex >= 0)
            {
                EditorGUILayout.LabelField(T("FirstVertex", _firstVertex));
                if (GUILayout.Button(T("ClearFirstPoint")))
                {
                    _firstVertex = -1;
                }
            }
        }

        // ================================================================
        // アルゴリズム (Dijkstra)
        // ================================================================

        private List<int> GetShortestPath(MeshObject meshObject, int start, int end)
        {
            var adjacency = SelectionHelper.BuildVertexAdjacency(meshObject);
            var distances = new Dictionary<int, float>();
            var previous = new Dictionary<int, int>();
            var unvisited = new HashSet<int>();

            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                distances[i] = float.MaxValue;
                unvisited.Add(i);
            }
            distances[start] = 0;

            while (unvisited.Count > 0)
            {
                int current = -1;
                float minDist = float.MaxValue;
                foreach (int v in unvisited)
                {
                    if (distances[v] < minDist)
                    {
                        minDist = distances[v];
                        current = v;
                    }
                }

                if (current < 0 || current == end) break;
                unvisited.Remove(current);

                if (!adjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (int neighbor in neighbors)
                {
                    if (!unvisited.Contains(neighbor)) continue;

                    float edgeLength = Vector3.Distance(
                        meshObject.Vertices[current].Position,
                        meshObject.Vertices[neighbor].Position);

                    float alt = distances[current] + edgeLength;
                    if (alt < distances[neighbor])
                    {
                        distances[neighbor] = alt;
                        previous[neighbor] = current;
                    }
                }
            }

            var path = new List<int>();
            int node = end;
            while (previous.ContainsKey(node))
            {
                path.Add(node);
                node = previous[node];
            }
            path.Add(start);
            path.Reverse();

            return path;
        }
    }
}
