// Assets/Editor/MeshFactory/Tools/Selection/Modes/ConnectedSelectMode.cs
// 接続領域選択モード
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
    /// 接続領域選択モード
    /// </summary>
    public class ConnectedSelectMode : IAdvancedSelectMode
    {
        public bool HandleClick(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            // Flags優先順位でヒットテスト（Vertex > Edge > Face > Line）
            if (selectMode.Has(MeshSelectMode.Vertex))
            {
                int hitVertex = SelectionHelper.FindNearestVertex(toolCtx, mousePos);
                if (hitVertex >= 0)
                {
                    ApplyConnectedFromVertex(ctx, hitVertex, selectMode);
                    return true;
                }
            }

            if (selectMode.Has(MeshSelectMode.Edge))
            {
                var hitEdge = SelectionHelper.FindNearestEdgePair(toolCtx, mousePos);
                if (hitEdge.HasValue)
                {
                    ApplyConnectedFromEdge(ctx, hitEdge.Value, selectMode);
                    return true;
                }
            }

            if (selectMode.Has(MeshSelectMode.Face))
            {
                int hitFace = SelectionHelper.FindNearestFace(toolCtx, mousePos);
                if (hitFace >= 0)
                {
                    ApplyConnectedFromFace(ctx, hitFace, selectMode);
                    return true;
                }
            }

            if (selectMode.Has(MeshSelectMode.Line))
            {
                int hitLine = SelectionHelper.FindNearestLine(toolCtx, mousePos);
                if (hitLine >= 0)
                {
                    ApplyConnectedFromLine(ctx, hitLine, selectMode);
                    return true;
                }
            }

            return false;
        }

        public void UpdatePreview(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            if (selectMode.Has(MeshSelectMode.Vertex))
            {
                ctx.HoveredVertex = SelectionHelper.FindNearestVertex(toolCtx, mousePos);
                if (ctx.HoveredVertex >= 0)
                {
                    var connectedVerts = GetConnectedVertices(toolCtx.MeshObject, ctx.HoveredVertex);
                    if (selectMode.Has(MeshSelectMode.Vertex))
                        ctx.PreviewVertices.AddRange(connectedVerts);
                    if (selectMode.Has(MeshSelectMode.Edge))
                        ctx.PreviewEdges.AddRange(SelectionHelper.GetEdgesFromVertices(toolCtx, connectedVerts));
                    if (selectMode.Has(MeshSelectMode.Face))
                        ctx.PreviewFaces.AddRange(SelectionHelper.GetFacesFromVertices(toolCtx, connectedVerts));
                    if (selectMode.Has(MeshSelectMode.Line))
                        ctx.PreviewLines.AddRange(SelectionHelper.GetLinesFromVertices(toolCtx, connectedVerts));
                    return;
                }
            }

            if (selectMode.Has(MeshSelectMode.Edge))
            {
                ctx.HoveredEdgePair = SelectionHelper.FindNearestEdgePair(toolCtx, mousePos);
                if (ctx.HoveredEdgePair.HasValue)
                {
                    var connectedEdges = GetConnectedEdges(toolCtx, ctx.HoveredEdgePair.Value);
                    var connectedVerts = new HashSet<int>();
                    foreach (var e in connectedEdges) { connectedVerts.Add(e.V1); connectedVerts.Add(e.V2); }

                    if (selectMode.Has(MeshSelectMode.Vertex))
                        ctx.PreviewVertices.AddRange(connectedVerts);
                    if (selectMode.Has(MeshSelectMode.Edge))
                        ctx.PreviewEdges.AddRange(connectedEdges);
                    if (selectMode.Has(MeshSelectMode.Face))
                        ctx.PreviewFaces.AddRange(SelectionHelper.GetFacesFromVertices(toolCtx, connectedVerts.ToList()));
                    return;
                }
            }

            if (selectMode.Has(MeshSelectMode.Face))
            {
                ctx.HoveredFace = SelectionHelper.FindNearestFace(toolCtx, mousePos);
                if (ctx.HoveredFace >= 0)
                {
                    var connectedFaces = GetConnectedFaces(toolCtx, ctx.HoveredFace);
                    var connectedVerts = new HashSet<int>();
                    foreach (int fIdx in connectedFaces)
                        foreach (int vIdx in toolCtx.MeshObject.Faces[fIdx].VertexIndices)
                            connectedVerts.Add(vIdx);

                    if (selectMode.Has(MeshSelectMode.Vertex))
                        ctx.PreviewVertices.AddRange(connectedVerts);
                    if (selectMode.Has(MeshSelectMode.Edge))
                        ctx.PreviewEdges.AddRange(SelectionHelper.GetEdgesFromFaces(toolCtx, connectedFaces));
                    if (selectMode.Has(MeshSelectMode.Face))
                        ctx.PreviewFaces.AddRange(connectedFaces);
                    return;
                }
            }

            if (selectMode.Has(MeshSelectMode.Line))
            {
                ctx.HoveredLine = SelectionHelper.FindNearestLine(toolCtx, mousePos);
                if (ctx.HoveredLine >= 0)
                {
                    var connectedLines = GetConnectedLines(toolCtx, ctx.HoveredLine);
                    var connectedVerts = new HashSet<int>();
                    foreach (int lIdx in connectedLines)
                    {
                        var face = toolCtx.MeshObject.Faces[lIdx];
                        if (face.VertexCount == 2)
                        {
                            connectedVerts.Add(face.VertexIndices[0]);
                            connectedVerts.Add(face.VertexIndices[1]);
                        }
                    }

                    if (selectMode.Has(MeshSelectMode.Vertex))
                        ctx.PreviewVertices.AddRange(connectedVerts);
                    if (selectMode.Has(MeshSelectMode.Line))
                        ctx.PreviewLines.AddRange(connectedLines);
                }
            }
        }

        public void Reset() { }

        public void DrawModeSettingsUI()
        {
            EditorGUILayout.HelpBox(T("ConnectedHelp"), MessageType.Info);
        }

        // ================================================================
        // 適用
        // ================================================================

        private void ApplyConnectedFromVertex(AdvancedSelectContext ctx, int startVertex, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;
            var connectedVerts = GetConnectedVertices(toolCtx.MeshObject, startVertex);

            if (selectMode.Has(MeshSelectMode.Vertex))
                SelectionHelper.ApplyVertexSelection(toolCtx, connectedVerts, ctx.AddToSelection);

            if (selectMode.Has(MeshSelectMode.Edge))
            {
                var edges = SelectionHelper.GetEdgesFromVertices(toolCtx, connectedVerts);
                SelectionHelper.ApplyEdgeSelection(toolCtx, edges, ctx.AddToSelection);
            }

            if (selectMode.Has(MeshSelectMode.Face))
            {
                var faces = SelectionHelper.GetFacesFromVertices(toolCtx, connectedVerts);
                SelectionHelper.ApplyFaceSelection(toolCtx, faces, ctx.AddToSelection);
            }

            if (selectMode.Has(MeshSelectMode.Line))
            {
                var lines = SelectionHelper.GetLinesFromVertices(toolCtx, connectedVerts);
                SelectionHelper.ApplyLineSelection(toolCtx, lines, ctx.AddToSelection);
            }
        }

        private void ApplyConnectedFromEdge(AdvancedSelectContext ctx, VertexPair startEdge, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;
            var connectedEdges = GetConnectedEdges(toolCtx, startEdge);
            var connectedVerts = new HashSet<int>();
            foreach (var e in connectedEdges)
            {
                connectedVerts.Add(e.V1);
                connectedVerts.Add(e.V2);
            }

            if (selectMode.Has(MeshSelectMode.Vertex))
                SelectionHelper.ApplyVertexSelection(toolCtx, connectedVerts.ToList(), ctx.AddToSelection);

            if (selectMode.Has(MeshSelectMode.Edge))
                SelectionHelper.ApplyEdgeSelection(toolCtx, connectedEdges, ctx.AddToSelection);

            if (selectMode.Has(MeshSelectMode.Face))
            {
                var faces = SelectionHelper.GetFacesFromVertices(toolCtx, connectedVerts.ToList());
                SelectionHelper.ApplyFaceSelection(toolCtx, faces, ctx.AddToSelection);
            }
        }

        private void ApplyConnectedFromFace(AdvancedSelectContext ctx, int startFace, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;
            var connectedFaces = GetConnectedFaces(toolCtx, startFace);
            var connectedVerts = new HashSet<int>();
            foreach (int fIdx in connectedFaces)
            {
                foreach (int vIdx in toolCtx.MeshObject.Faces[fIdx].VertexIndices)
                    connectedVerts.Add(vIdx);
            }

            if (selectMode.Has(MeshSelectMode.Vertex))
                SelectionHelper.ApplyVertexSelection(toolCtx, connectedVerts.ToList(), ctx.AddToSelection);

            if (selectMode.Has(MeshSelectMode.Edge))
            {
                var edges = SelectionHelper.GetEdgesFromFaces(toolCtx, connectedFaces);
                SelectionHelper.ApplyEdgeSelection(toolCtx, edges, ctx.AddToSelection);
            }

            if (selectMode.Has(MeshSelectMode.Face))
                SelectionHelper.ApplyFaceSelection(toolCtx, connectedFaces, ctx.AddToSelection);
        }

        private void ApplyConnectedFromLine(AdvancedSelectContext ctx, int startLine, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;
            var connectedLines = GetConnectedLines(toolCtx, startLine);
            var connectedVerts = new HashSet<int>();
            foreach (int lIdx in connectedLines)
            {
                var face = toolCtx.MeshObject.Faces[lIdx];
                if (face.VertexCount == 2)
                {
                    connectedVerts.Add(face.VertexIndices[0]);
                    connectedVerts.Add(face.VertexIndices[1]);
                }
            }

            if (selectMode.Has(MeshSelectMode.Vertex))
                SelectionHelper.ApplyVertexSelection(toolCtx, connectedVerts.ToList(), ctx.AddToSelection);

            if (selectMode.Has(MeshSelectMode.Line))
                SelectionHelper.ApplyLineSelection(toolCtx, connectedLines, ctx.AddToSelection);
        }

        // ================================================================
        // アルゴリズム
        // ================================================================

        private List<int> GetConnectedVertices(MeshObject meshObject, int startVertex)
        {
            var result = new HashSet<int>();
            var queue = new Queue<int>();
            var adjacency = SelectionHelper.BuildVertexAdjacency(meshObject);

            queue.Enqueue(startVertex);
            result.Add(startVertex);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (int neighbor in neighbors)
                {
                    if (result.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return result.ToList();
        }

        private List<VertexPair> GetConnectedEdges(ToolContext ctx, VertexPair startEdge)
        {
            var result = new HashSet<VertexPair>();
            var queue = new Queue<VertexPair>();
            var edgeAdjacency = SelectionHelper.BuildEdgeAdjacency(ctx);

            queue.Enqueue(startEdge);
            result.Add(startEdge);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!edgeAdjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (var neighbor in neighbors)
                {
                    if (result.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return result.ToList();
        }

        private List<int> GetConnectedFaces(ToolContext ctx, int startFace)
        {
            var result = new HashSet<int>();
            var queue = new Queue<int>();
            var faceAdjacency = SelectionHelper.BuildFaceAdjacency(ctx.MeshObject);

            queue.Enqueue(startFace);
            result.Add(startFace);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!faceAdjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (int neighbor in neighbors)
                {
                    if (result.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return result.ToList();
        }

        private List<int> GetConnectedLines(ToolContext ctx, int startLine)
        {
            if (ctx.MeshObject == null) return new List<int> { startLine };

            var result = new HashSet<int>();
            var queue = new Queue<int>();
            var lineAdjacency = SelectionHelper.BuildLineAdjacency(ctx.MeshObject);

            queue.Enqueue(startLine);
            result.Add(startLine);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!lineAdjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (int neighbor in neighbors)
                {
                    if (result.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return result.ToList();
        }
    }
}
