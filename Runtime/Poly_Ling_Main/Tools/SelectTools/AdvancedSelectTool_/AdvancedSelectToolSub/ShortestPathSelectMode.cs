// Assets/Editor/Poly_Ling/Tools/Selection/Modes/ShortestPathSelectMode.cs
// 最短ルート選択モード
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
    /// 最短ルート選択モード
    /// </summary>
    public partial class ShortestPathSelectMode : IAdvancedSelectMode
    {
        private int _firstVertex = -1;

        public int FirstVertex => _firstVertex;

        public bool HandleClick(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            // 【CPUヒットテスト禁止。これもバグあり使用禁止】CPU フォールバック（FindNearestVertex）を全撤去。
            int vIdx = ctx.GpuStartVertex;
            if (vIdx < 0) return false;

            if (_firstVertex < 0)
            {
                _firstVertex = vIdx;
                return true;
            }
            else
            {
                var path = GetShortestPath(toolCtx.ActiveMeshObject, _firstVertex, vIdx);

                if (selectMode.Has(MeshSelectMode.Vertex))
                    SelectionHelper.ApplyVertexSelection(toolCtx, path, ctx.AddToSelection);

                // 経路の区間を、面の辺は辺選択へ、補助線分（2 頂点の面）は線分選択へ入れる。
                // 始点・終点は常にクリックした頂点（ホバーは頂点に絞られる）なので、
                // 以前の「辺を起点にした始点解決」の問題は起きない。
                if (selectMode.Has(MeshSelectMode.Edge) || selectMode.Has(MeshSelectMode.Line))
                {
                    SplitPathSegments(toolCtx.ActiveMeshObject, path, out var polyEdges, out var lineFaces);
                    if (selectMode.Has(MeshSelectMode.Edge))
                        SelectionHelper.ApplyEdgeSelection(toolCtx, polyEdges, ctx.AddToSelection);
                    if (selectMode.Has(MeshSelectMode.Line))
                        SelectionHelper.ApplyLineSelection(toolCtx, lineFaces, ctx.AddToSelection);
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

            // GPU ホバー由来の頂点からパスプレビューを作る。CPU 探索は使わない。
            ctx.HoveredVertex = ctx.GpuStartVertex;

            if (_firstVertex >= 0 && ctx.HoveredVertex >= 0 && _firstVertex != ctx.HoveredVertex)
            {
                ctx.PreviewPath.AddRange(GetShortestPath(toolCtx.ActiveMeshObject, _firstVertex, ctx.HoveredVertex));

                if (selectMode.Has(MeshSelectMode.Edge) || selectMode.Has(MeshSelectMode.Line))
                {
                    SplitPathSegments(toolCtx.ActiveMeshObject, ctx.PreviewPath, out var polyEdges, out var lineFaces);
                    if (selectMode.Has(MeshSelectMode.Edge)) ctx.PreviewEdges.AddRange(polyEdges);
                    if (selectMode.Has(MeshSelectMode.Line)) ctx.PreviewLines.AddRange(lineFaces);
                }

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

        /// <summary>
        /// 経路の区間を、3 頂点以上の面の辺（polyEdges）と補助線分の面番号（lineFaces）に分ける。
        /// 区間が両方に当たるとき（面の辺と補助線分が重なっている）は両方に入る。
        /// </summary>
        private static void SplitPathSegments(MeshObject mo, List<int> path,
            out List<VertexPair> polyEdges, out List<int> lineFaces)
        {
            polyEdges = new List<VertexPair>();
            lineFaces = new List<int>();
            var segs = SelectionHelper.GetEdgesFromPath(path);
            if (mo == null || segs.Count == 0) return;

            var want   = new HashSet<VertexPair>(segs);
            var inPoly = new HashSet<VertexPair>();
            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                var face = mo.Faces[fi];
                if (face == null) continue;
                var vs = face.VertexIndices;
                int n  = vs.Count;
                if (n == 2)
                {
                    if (want.Contains(new VertexPair(vs[0], vs[1]))) lineFaces.Add(fi);
                }
                else if (n >= 3)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var e = new VertexPair(vs[i], vs[(i + 1) % n]);
                        if (want.Contains(e)) inPoly.Add(e);
                    }
                }
            }
            foreach (var s in segs) if (inPoly.Contains(s)) polyEdges.Add(s);
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
