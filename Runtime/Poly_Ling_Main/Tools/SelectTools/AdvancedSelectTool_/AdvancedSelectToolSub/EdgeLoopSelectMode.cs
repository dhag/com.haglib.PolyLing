// Assets/Editor/Poly_Ling/Tools/Selection/Modes/EdgeLoopSelectMode.cs
// 連続エッジ選択モード
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
    /// 連続エッジ選択モード
    /// </summary>
    public partial class EdgeLoopSelectMode : IAdvancedSelectMode
    {
        public bool HandleClick(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            var edge = ctx.GpuStartEdge ?? SelectionHelper.FindNearestEdgePair(toolCtx, mousePos);
            if (!edge.HasValue)
            {
                var legacyEdge = SelectionHelper.FindNearestEdgeLegacy(toolCtx, mousePos);
                if (legacyEdge.Item1 < 0) return false;
                edge = new VertexPair(legacyEdge.Item1, legacyEdge.Item2);
            }

            var loopEdges = GetEdgeLoopEdges(toolCtx.FirstSelectedMeshObject, edge.Value, ctx.EdgeLoopThreshold);

            // 頂点選択は廃止（辺主体の機能で頂点選択は無駄かつ分かりにくいため）。
            if (selectMode.Has(MeshSelectMode.Edge))
                SelectionHelper.ApplyEdgeSelection(toolCtx, loopEdges, ctx.AddToSelection);

            if (selectMode.Has(MeshSelectMode.Face))
            {
                var faces = SelectionHelper.GetAdjacentFaces(toolCtx, loopEdges);
                SelectionHelper.ApplyFaceSelection(toolCtx, faces, ctx.AddToSelection);
            }

            return true;
        }

        public void UpdatePreview(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            // 【利用禁止。おそらくバグがある】ホバープレビューは GPU ホバー未参照の CPU 探索のまま。
            // クリック確定は GpuStartEdge を優先するがプレビューは未対応でズレる可能性がある。
            ctx.HoveredEdgePair = SelectionHelper.FindNearestEdgePair(toolCtx, mousePos);
            if (!ctx.HoveredEdgePair.HasValue)
            {
                var legacyEdge = SelectionHelper.FindNearestEdgeLegacy(toolCtx, mousePos);
                if (legacyEdge.Item1 >= 0)
                    ctx.HoveredEdgePair = new VertexPair(legacyEdge.Item1, legacyEdge.Item2);
            }

            if (!ctx.HoveredEdgePair.HasValue) return;

            var loopEdges = GetEdgeLoopEdges(toolCtx.FirstSelectedMeshObject, ctx.HoveredEdgePair.Value, ctx.EdgeLoopThreshold);

            // 頂点選択は廃止。
            if (selectMode.Has(MeshSelectMode.Edge))
                ctx.PreviewEdges.AddRange(loopEdges);
            if (selectMode.Has(MeshSelectMode.Face))
                ctx.PreviewFaces.AddRange(SelectionHelper.GetAdjacentFaces(toolCtx, loopEdges));
        }

        public void Reset() { }

        // ================================================================
        // アルゴリズム
        // ================================================================

        private List<VertexPair> GetEdgeLoopEdges(MeshObject meshObject, VertexPair startEdge, float threshold)
        {
            var result = new HashSet<VertexPair>();
            var visitedEdges = new HashSet<VertexPair>();

            Vector3 edgeDir = (meshObject.Vertices[startEdge.V2].Position -
                              meshObject.Vertices[startEdge.V1].Position).normalized;

            var adjacency = SelectionHelper.BuildVertexAdjacency(meshObject);

            // 開始辺を先に登録する（両方向探索が先頭で即 break しないように）。
            visitedEdges.Add(startEdge);
            result.Add(startEdge);

            // 方向1: V2 を起点に、V1→V2 の向きへ延ばす。
            TraverseEdgeLoopEdges(meshObject, startEdge.V2, startEdge.V1, edgeDir, adjacency, visitedEdges, result, threshold);
            // 方向2: V1 を起点に、V2→V1 の向きへ延ばす。
            TraverseEdgeLoopEdges(meshObject, startEdge.V1, startEdge.V2, -edgeDir, adjacency, visitedEdges, result, threshold);

            return result.ToList();
        }

        /// <param name="pivot">今いる頂点。ここから次の辺を探す。</param>
        /// <param name="prev">直前の頂点（戻り防止）。</param>
        /// <param name="direction">進行方向（pivot へ入ってきた向き）。</param>
        private void TraverseEdgeLoopEdges(MeshObject meshObject, int pivot, int prev, Vector3 direction,
            Dictionary<int, HashSet<int>> adjacency, HashSet<VertexPair> visitedEdges, HashSet<VertexPair> result, float threshold)
        {
            int current = pivot;
            int prevV   = prev;
            Vector3 currentDir = direction;

            while (true)
            {
                if (!adjacency.TryGetValue(current, out var neighbors)) break;

                int bestNext = -1;
                float bestDot = threshold;

                foreach (int next in neighbors)
                {
                    if (next == prevV) continue;

                    Vector3 nextDir = (meshObject.Vertices[next].Position - meshObject.Vertices[current].Position).normalized;
                    float dot = Vector3.Dot(currentDir, nextDir);

                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestNext = next;
                    }
                }

                if (bestNext < 0) break;

                var edge = new VertexPair(current, bestNext);
                if (visitedEdges.Contains(edge)) break;
                visitedEdges.Add(edge);
                result.Add(edge);

                currentDir = (meshObject.Vertices[bestNext].Position - meshObject.Vertices[current].Position).normalized;
                prevV   = current;
                current = bestNext;
            }
        }
    }
}
