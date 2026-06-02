// Tools/TopologyTools/Modify/KnifeTool_/KnifeToolSub/KnifeTool_Erase.cs
// ナイフツール - Erase モード（共有辺で2面を統合）。
// インデックス / VertexPair ベース（SelectionHelper を流用）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    public partial class KnifeTool
    {
        private const float ERASE_EDGE_THRESHOLD = 10f;

        private VertexPair _hoveredEraseEdge;
        private bool       _hasEraseHover;

        // ================================================================
        // クリック / ホバー
        // ================================================================

        private bool HandleEraseClick(ToolContext ctx, Vector2 mousePos)
        {
            var mo = ctx.FirstSelectedMeshObject;
            if (mo == null) return false;

            var edge = FindNearestSharedEdge(ctx, mo, mousePos, out var faces);
            if (!edge.HasValue || faces == null || faces.Count != 2) return false;

            MeshObjectSnapshot before = ctx.UndoController != null
                ? MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext)
                : null;

            MergeFaces(mo, faces[0], faces[1], edge.Value);
            ctx.SyncMesh?.Invoke();
            ctx.NotifyTopologyChanged?.Invoke();

            if (ctx.UndoController != null && before != null)
            {
                var after = MeshObjectSnapshot.Capture(ctx.UndoController.MeshUndoContext);
                ctx.UndoController.RecordMeshTopologyChange(before, after, "Knife Erase Edge");
            }

            _hasEraseHover = false;
            _preview.Clear();
            ctx.Repaint?.Invoke();
            return true;
        }

        private void UpdateEraseHover(ToolContext ctx, Vector2 mousePos)
        {
            _preview.Clear();
            var mo = ctx.FirstSelectedMeshObject;
            if (mo == null) return;

            var edge = FindNearestSharedEdge(ctx, mo, mousePos, out _);
            _hasEraseHover = edge.HasValue;
            _hoveredEraseEdge = edge ?? default;

            if (_hasEraseHover)
                _preview.Lines.Add((mo.Vertices[_hoveredEraseEdge.V1].Position,
                                    mo.Vertices[_hoveredEraseEdge.V2].Position));
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>2面で共有される最近傍の辺を返す。</summary>
        private VertexPair? FindNearestSharedEdge(ToolContext ctx, MeshObject mo, Vector2 mousePos, out List<int> hitFaces)
        {
            hitFaces = null;
            var edgeToFaces = SelectionHelper.BuildEdgeToFacesMap(mo);

            float best = ERASE_EDGE_THRESHOLD;
            VertexPair? bestEdge = null;
            List<int> bestFaces = null;

            foreach (var kvp in edgeToFaces)
            {
                if (kvp.Value.Count != 2) continue;
                var e = kvp.Key;
                Vector2 s1 = ctx.WorldToScreen(mo.Vertices[e.V1].Position);
                Vector2 s2 = ctx.WorldToScreen(mo.Vertices[e.V2].Position);
                float d = SelectionHelper.DistanceToLineSegment(mousePos, s1, s2);
                if (d < best)
                {
                    best = d;
                    bestEdge = e;
                    bestFaces = kvp.Value;
                }
            }

            hitFaces = bestFaces;
            return bestEdge;
        }

        /// <summary>共有辺を消して2面を統合する。</summary>
        private void MergeFaces(MeshObject mo, int faceIdx1, int faceIdx2, VertexPair sharedEdge)
        {
            var face1 = mo.Faces[faceIdx1];
            var face2 = mo.Faces[faceIdx2];

            int s1 = LocalEdgeStart(face1, sharedEdge);
            int s2 = LocalEdgeStart(face2, sharedEdge);
            if (s1 < 0 || s2 < 0) return;

            var newVerts = new List<int>();

            int n1 = face1.VertexIndices.Count;
            for (int i = 0; i < n1 - 1; i++)
                newVerts.Add(face1.VertexIndices[(s1 + 1 + i) % n1]);

            int n2 = face2.VertexIndices.Count;
            for (int i = 0; i < n2 - 1; i++)
            {
                int v = face2.VertexIndices[(s2 + 1 + i) % n2];
                if (!newVerts.Contains(v)) newVerts.Add(v);
            }

            var uvs = new List<int>();
            var normals = new List<int>();
            for (int i = 0; i < newVerts.Count; i++) { uvs.Add(0); normals.Add(0); }

            var newFace = new Face
            {
                VertexIndices = newVerts,
                UVIndices = uvs,
                NormalIndices = normals,
                MaterialIndex = face1.MaterialIndex
            };

            int maxIdx = Mathf.Max(faceIdx1, faceIdx2);
            int minIdx = Mathf.Min(faceIdx1, faceIdx2);
            mo.Faces.RemoveAt(maxIdx);
            mo.Faces.RemoveAt(minIdx);
            mo.Faces.Add(newFace);
        }

        private static int LocalEdgeStart(Face face, VertexPair edge)
        {
            int n = face.VertexIndices.Count;
            for (int i = 0; i < n; i++)
            {
                int a = face.VertexIndices[i];
                int b = face.VertexIndices[(i + 1) % n];
                if ((a == edge.V1 && b == edge.V2) || (a == edge.V2 && b == edge.V1)) return i;
            }
            return -1;
        }
    }
}
