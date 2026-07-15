// Assets/Editor/Poly_Ling/Tools/Selection/Modes/ConnectedSelectMode.cs
// 接続領域選択モード
// ローカライズ対応版

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using static Poly_Ling.Tools.SelectModeTexts;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 接続領域選択モード
    ///
    /// このモードは開始要素の確定に GPU ホバー（ctx.GpuStartVertex/Edge/Face/Line）を優先度
    /// （頂点>辺>面>線）で解決する。CPU ヒットテスト（SelectionHelper.FindNearest*）は深度/
    /// 遮蔽/WorldMatrix 非考慮で誤選択するため使用禁止・全撤去済み。
    /// </summary>
    public partial class ConnectedSelectMode : IAdvancedSelectMode
    {
        public bool HandleClick(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            // GPU ホバー由来の開始要素を優先度（頂点>辺>面>線）で解決する。CPU ヒットテストは使わない。
            if (selectMode.Has(MeshSelectMode.Vertex) && ctx.GpuStartVertex >= 0)
            {
                ApplyConnectedFromVertex(ctx, ctx.GpuStartVertex, selectMode);
                return true;
            }
            if (selectMode.Has(MeshSelectMode.Edge) && ctx.GpuStartEdge.HasValue)
            {
                ApplyConnectedFromEdge(ctx, ctx.GpuStartEdge.Value, selectMode);
                return true;
            }
            if (selectMode.Has(MeshSelectMode.Face) && ctx.GpuStartFace >= 0)
            {
                ApplyConnectedFromFace(ctx, ctx.GpuStartFace, selectMode);
                return true;
            }
            if (selectMode.Has(MeshSelectMode.Line) && ctx.GpuStartLine >= 0)
            {
                ApplyConnectedFromLine(ctx, ctx.GpuStartLine, selectMode);
                return true;
            }
            return false;
        }

        public void UpdatePreview(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            // GPU ホバー由来の開始要素を優先度（頂点>辺>面>線）で解決してプレビューを作る。CPU 不使用。
            if (selectMode.Has(MeshSelectMode.Vertex) && ctx.GpuStartVertex >= 0)
            {
                ctx.HoveredVertex = ctx.GpuStartVertex;
                var connectedVerts = GetConnectedVertices(toolCtx.FirstSelectedMeshObject, ctx.HoveredVertex);
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

            if (selectMode.Has(MeshSelectMode.Edge) && ctx.GpuStartEdge.HasValue)
            {
                ctx.HoveredEdgePair = ctx.GpuStartEdge;
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

            if (selectMode.Has(MeshSelectMode.Face) && ctx.GpuStartFace >= 0)
            {
                ctx.HoveredFace = ctx.GpuStartFace;
                var connectedFaces = GetConnectedFaces(toolCtx, ctx.HoveredFace);
                var connectedVerts = new HashSet<int>();
                foreach (int fIdx in connectedFaces)
                    foreach (int vIdx in toolCtx.FirstSelectedMeshObject.Faces[fIdx].VertexIndices)
                        connectedVerts.Add(vIdx);

                if (selectMode.Has(MeshSelectMode.Vertex))
                    ctx.PreviewVertices.AddRange(connectedVerts);
                if (selectMode.Has(MeshSelectMode.Edge))
                    ctx.PreviewEdges.AddRange(SelectionHelper.GetEdgesFromFaces(toolCtx, connectedFaces));
                if (selectMode.Has(MeshSelectMode.Face))
                    ctx.PreviewFaces.AddRange(connectedFaces);
                return;
            }

            if (selectMode.Has(MeshSelectMode.Line) && ctx.GpuStartLine >= 0)
            {
                ctx.HoveredLine = ctx.GpuStartLine;
                var connectedLines = GetConnectedLines(toolCtx, ctx.HoveredLine);
                var connectedVerts = new HashSet<int>();
                foreach (int lIdx in connectedLines)
                {
                    var face = toolCtx.FirstSelectedMeshObject.Faces[lIdx];
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

        public void Reset() { }

        // ================================================================
        // 適用
        // ================================================================

        private void ApplyConnectedFromVertex(AdvancedSelectContext ctx, int startVertex, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;
            var connectedVerts = GetConnectedVertices(toolCtx.FirstSelectedMeshObject, startVertex);

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
                foreach (int vIdx in toolCtx.FirstSelectedMeshObject.Faces[fIdx].VertexIndices)
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
                var face = toolCtx.FirstSelectedMeshObject.Faces[lIdx];
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
            var faceAdjacency = SelectionHelper.BuildFaceAdjacency(ctx.FirstSelectedMeshObject);

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
            if (ctx.FirstSelectedMeshObject == null) return new List<int> { startLine };

            var result = new HashSet<int>();
            var queue = new Queue<int>();
            var lineAdjacency = SelectionHelper.BuildLineAdjacency(ctx.FirstSelectedMeshObject);

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
