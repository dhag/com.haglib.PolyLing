// BoundaryEdgeSelectMode.cs
// エッジ群選択モード
//
// 「エッジ」＝1つの面だけが使っている辺。穴の縁・開いた面の外周がこれにあたる。
// クリックした要素（頂点／辺／面）からエッジグループを引き当て、
// そのグループのエッジ全部と構成頂点を選択する。
//
// 開始要素の確定は GPU ホバー（ctx.GpuStartVertex/Edge/Face）を優先度
// 頂点>辺>面 で解決する。CPU ヒットテスト（SelectionHelper.FindNearest*）は
// 深度・遮蔽・WorldMatrix 非考慮で誤選択するため使用しない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Ops;
using Poly_Ling.Selection;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// エッジ群選択モード
    /// </summary>
    public class BoundaryEdgeSelectMode : IAdvancedSelectMode
    {
        public bool HandleClick(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;
            if (toolCtx?.ActiveMeshObject == null) return false;

            var edges = ResolveGroup(ctx);
            if (edges.Count == 0) return false;

            // このモードの出力は「グループのエッジ」と「その構成頂点」で固定する。
            // 選択モードの ON/OFF で片方が欠けると穴の選択として使えないため。
            SelectionHelper.ApplyEdgeSelection(toolCtx, edges, ctx.AddToSelection);
            SelectionHelper.ApplyVertexSelection(toolCtx, BoundaryEdgeOps.VerticesOf(edges), ctx.AddToSelection);
            return true;
        }

        public void UpdatePreview(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;
            if (toolCtx?.ActiveMeshObject == null) return;

            var edges = ResolveGroup(ctx);
            if (edges.Count == 0) return;

            ctx.PreviewEdges.AddRange(edges);
            ctx.PreviewVertices.AddRange(BoundaryEdgeOps.VerticesOf(edges));
        }

        public void Reset() { }

        // ================================================================
        // 開始要素の解決
        // ================================================================

        /// <summary>
        /// GPU ホバー由来の開始要素からエッジグループを引く。
        /// 優先度は 頂点 > 辺 > 面。どれもエッジに触れていなければ空を返す。
        /// </summary>
        private static List<VertexPair> ResolveGroup(AdvancedSelectContext ctx)
        {
            var mesh = ctx.ToolCtx.ActiveMeshObject;

            if (ctx.GpuStartVertex >= 0)
            {
                ctx.HoveredVertex = ctx.GpuStartVertex;
                var g = BoundaryEdgeOps.GroupFromVertex(mesh, ctx.GpuStartVertex);
                if (g.Count > 0) return g;
            }

            if (ctx.GpuStartEdge.HasValue)
            {
                ctx.HoveredEdgePair = ctx.GpuStartEdge;
                var g = BoundaryEdgeOps.GroupFromEdge(mesh, ctx.GpuStartEdge.Value);
                if (g.Count > 0) return g;
            }

            if (ctx.GpuStartFace >= 0)
            {
                ctx.HoveredFace = ctx.GpuStartFace;
                var g = BoundaryEdgeOps.GroupFromFace(mesh, ctx.GpuStartFace);
                if (g.Count > 0) return g;
            }

            return new List<VertexPair>();
        }
    }
}
