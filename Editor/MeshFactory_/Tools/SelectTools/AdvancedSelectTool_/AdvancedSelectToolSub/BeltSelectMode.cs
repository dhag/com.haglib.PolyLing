// Assets/Editor/MeshFactory/Tools/Selection/Modes/BeltSelectMode.cs
// ベルト選択モード
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
    /// ベルト選択モード
    /// </summary>
    public class BeltSelectMode : IAdvancedSelectMode
    {
        private struct BeltData
        {
            public HashSet<int> Vertices;
            public List<VertexPair> LadderEdges;
            public List<int> Faces;
        }

        public bool HandleClick(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            var edge = SelectionHelper.FindNearestEdgePair(toolCtx, mousePos);
            if (!edge.HasValue)
            {
                var legacyEdge = SelectionHelper.FindNearestEdgeLegacy(toolCtx, mousePos);
                if (legacyEdge.Item1 < 0) return false;
                edge = new VertexPair(legacyEdge.Item1, legacyEdge.Item2);
            }

            var beltData = GetBeltData(toolCtx.MeshObject, edge.Value);

            if (selectMode.Has(MeshSelectMode.Vertex))
                SelectionHelper.ApplyVertexSelection(toolCtx, beltData.Vertices.ToList(), ctx.AddToSelection);

            if (selectMode.Has(MeshSelectMode.Edge))
                SelectionHelper.ApplyEdgeSelection(toolCtx, beltData.LadderEdges, ctx.AddToSelection);

            if (selectMode.Has(MeshSelectMode.Face))
                SelectionHelper.ApplyFaceSelection(toolCtx, beltData.Faces, ctx.AddToSelection);

            return true;
        }

        public void UpdatePreview(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            ctx.HoveredEdgePair = SelectionHelper.FindNearestEdgePair(toolCtx, mousePos);
            if (!ctx.HoveredEdgePair.HasValue)
            {
                var legacyEdge = SelectionHelper.FindNearestEdgeLegacy(toolCtx, mousePos);
                if (legacyEdge.Item1 >= 0)
                    ctx.HoveredEdgePair = new VertexPair(legacyEdge.Item1, legacyEdge.Item2);
            }

            if (!ctx.HoveredEdgePair.HasValue) return;

            var beltData = GetBeltData(toolCtx.MeshObject, ctx.HoveredEdgePair.Value);

            if (selectMode.Has(MeshSelectMode.Vertex))
                ctx.PreviewVertices.AddRange(beltData.Vertices);
            if (selectMode.Has(MeshSelectMode.Edge))
                ctx.PreviewEdges.AddRange(beltData.LadderEdges);
            if (selectMode.Has(MeshSelectMode.Face))
                ctx.PreviewFaces.AddRange(beltData.Faces);
        }

        public void Reset() { }

        public void DrawModeSettingsUI()
        {
            EditorGUILayout.HelpBox(T("BeltHelp"), MessageType.Info);
        }

        // ================================================================
        // アルゴリズム
        // ================================================================

        private BeltData GetBeltData(MeshObject meshObject, VertexPair startEdge)
        {
            var result = new BeltData
            {
                Vertices = new HashSet<int>(),
                LadderEdges = new List<VertexPair>(),
                Faces = new List<int>()
            };

            var edgeToFaces = SelectionHelper.BuildEdgeToFacesMap(meshObject);
            var visitedEdges = new HashSet<VertexPair>();

            TraverseBeltData(meshObject, startEdge, edgeToFaces, visitedEdges, result, true);
            TraverseBeltData(meshObject, startEdge, edgeToFaces, visitedEdges, result, false);

            return result;
        }

        private void TraverseBeltData(MeshObject meshObject, VertexPair startEdge,
            Dictionary<VertexPair, List<int>> edgeToFaces, HashSet<VertexPair> visitedEdges,
            BeltData result, bool forward)
        {
            var currentEdge = startEdge;

            while (true)
            {
                if (visitedEdges.Contains(currentEdge)) break;
                visitedEdges.Add(currentEdge);

                result.Vertices.Add(currentEdge.V1);
                result.Vertices.Add(currentEdge.V2);
                result.LadderEdges.Add(currentEdge);

                if (!edgeToFaces.TryGetValue(currentEdge, out var faces)) break;

                VertexPair? nextEdge = null;

                foreach (int faceIdx in faces)
                {
                    var face = meshObject.Faces[faceIdx];
                    if (face.VertexIndices.Count != 4) continue;

                    if (!result.Faces.Contains(faceIdx))
                        result.Faces.Add(faceIdx);

                    var opposite = FindOppositeEdge(face, currentEdge.V1, currentEdge.V2);
                    if (opposite.HasValue)
                    {
                        var oppPair = new VertexPair(opposite.Value.Item1, opposite.Value.Item2);
                        if (!visitedEdges.Contains(oppPair))
                        {
                            nextEdge = oppPair;
                            break;
                        }
                    }
                }

                if (!nextEdge.HasValue) break;
                currentEdge = nextEdge.Value;
            }
        }

        private (int, int)? FindOppositeEdge(Face face, int v1, int v2)
        {
            var verts = face.VertexIndices;
            int n = verts.Count;
            if (n != 4) return null;

            for (int i = 0; i < n; i++)
            {
                if ((verts[i] == v1 && verts[(i + 1) % n] == v2) ||
                    (verts[i] == v2 && verts[(i + 1) % n] == v1))
                {
                    int oppStart = (i + 2) % n;
                    int oppEnd = (i + 3) % n;
                    return (verts[oppStart], verts[oppEnd]);
                }
            }

            return null;
        }
    }
}
