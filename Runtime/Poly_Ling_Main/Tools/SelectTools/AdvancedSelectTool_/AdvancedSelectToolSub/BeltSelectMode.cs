// Assets/Editor/Poly_Ling/Tools/Selection/Modes/BeltSelectMode.cs
// ベルト選択モード
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
    /// ベルト選択モード
    /// </summary>
    public partial class BeltSelectMode : IAdvancedSelectMode
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

            // 【CPUヒットテスト禁止。これもバグあり使用禁止】CPU フォールバック（FindNearestEdgePair / FindNearestEdgeLegacy）を全撤去。
            var edge = ctx.GpuStartEdge;
            if (!edge.HasValue) return false;

            var beltData = GetBeltData(toolCtx.FirstSelectedMeshObject, edge.Value);

            // 頂点選択は廃止（辺主体の機能で頂点選択は無駄かつ分かりにくいため）。
            if (selectMode.Has(MeshSelectMode.Edge))
                SelectionHelper.ApplyEdgeSelection(toolCtx, beltData.LadderEdges, ctx.AddToSelection);

            if (selectMode.Has(MeshSelectMode.Face))
                SelectionHelper.ApplyFaceSelection(toolCtx, beltData.Faces, ctx.AddToSelection);

            return true;
        }

        public void UpdatePreview(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            // GPU ホバー由来の開始辺からプレビューを作る。CPU 探索は使わない。
            ctx.HoveredEdgePair = ctx.GpuStartEdge;
            if (!ctx.HoveredEdgePair.HasValue) return;

            var beltData = GetBeltData(toolCtx.FirstSelectedMeshObject, ctx.HoveredEdgePair.Value);

            // 頂点選択は廃止。
            if (selectMode.Has(MeshSelectMode.Edge))
                ctx.PreviewEdges.AddRange(beltData.LadderEdges);
            if (selectMode.Has(MeshSelectMode.Face))
                ctx.PreviewFaces.AddRange(beltData.Faces);
        }

        public void Reset() { }

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

            // 開始辺を先に登録する（両側の展開が開始辺で衝突しないように）。
            visitedEdges.Add(startEdge);
            result.Vertices.Add(startEdge.V1);
            result.Vertices.Add(startEdge.V2);
            result.LadderEdges.Add(startEdge);

            // 開始辺に隣接する各四角形（最大2枚）の対辺から、それぞれ独立に展開する。
            // これにより開始辺を挟んだ双方向のベルトが選択される。
            if (edgeToFaces.TryGetValue(startEdge, out var startFaces))
            {
                foreach (int faceIdx in startFaces)
                {
                    var face = meshObject.Faces[faceIdx];
                    if (face.VertexIndices.Count != 4) continue;

                    if (!result.Faces.Contains(faceIdx))
                        result.Faces.Add(faceIdx);

                    var opposite = FindOppositeEdge(face, startEdge.V1, startEdge.V2);
                    if (!opposite.HasValue) continue;

                    var oppPair = new VertexPair(opposite.Value.Item1, opposite.Value.Item2);
                    if (!visitedEdges.Contains(oppPair))
                        TraverseBeltData(meshObject, oppPair, edgeToFaces, visitedEdges, result);
                }
            }

            return result;
        }

        private void TraverseBeltData(MeshObject meshObject, VertexPair startEdge,
            Dictionary<VertexPair, List<int>> edgeToFaces, HashSet<VertexPair> visitedEdges,
            BeltData result)
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
