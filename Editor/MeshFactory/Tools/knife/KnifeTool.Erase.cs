// Tools/KnifeTool.Erase.cs
// ナイフツール - Eraseモード（辺消去）

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;

namespace MeshFactory.Tools
{
    public partial class KnifeTool
    {
        // ================================================================
        // Erase: 辺消去
        // ================================================================

        private bool HandleEraseMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (_hoveredEdge.Item1 < 0) return false;

            // 共有辺かどうか確認
            var edgeToFaces = BuildEdgeToFacesMap(ctx.MeshData);
            var key = _hoveredEdge;

            if (!edgeToFaces.TryGetValue(key, out var faces) || faces.Count != 2)
            {
                // 共有辺でない場合は何もしない
                return false;
            }

            // Undo用スナップショット（統合前）
            MeshDataSnapshot beforeSnapshot = ctx.UndoController != null 
                ? MeshDataSnapshot.Capture(ctx.UndoController.MeshContext) 
                : null;

            // 2つの面を統合
            MergeFaces(ctx, faces[0], faces[1], key);

            // Undo記録
            if (ctx.UndoController != null && beforeSnapshot != null)
            {
                var afterSnapshot = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
                ctx.UndoController.RecordMeshTopologyChange(beforeSnapshot, afterSnapshot, "Erase Edge");
            }

            _hoveredEdge = (-1, -1);
            ctx.Repaint?.Invoke();
            return true;
        }

        private bool HandleEraseMouseDrag(ToolContext ctx, Vector2 mousePos)
        {
            _hoveredEdge = FindNearestSharedEdge(ctx, mousePos);
            ctx.Repaint?.Invoke();
            return false;
        }

        private void DrawEraseGizmo(ToolContext ctx)
        {
            Handles.BeginGUI();

            if (_hoveredEdge.Item1 >= 0)
            {
                Handles.color = Color.red;
                DrawEdge(ctx, _hoveredEdge);
            }

            Handles.EndGUI();
        }

        // ================================================================
        // Eraseヘルパー
        // ================================================================

        /// <summary>
        /// 最も近い共有辺を検出（2つの面で共有されている辺のみ）
        /// </summary>
        private (int, int) FindNearestSharedEdge(ToolContext ctx, Vector2 mousePos)
        {
            var edgeToFaces = BuildEdgeToFacesMap(ctx.MeshData);

            float bestDist = EDGE_CLICK_THRESHOLD;
            (int, int) bestEdge = (-1, -1);

            foreach (var kvp in edgeToFaces)
            {
                // 2つの面で共有されている辺のみ対象
                if (kvp.Value.Count != 2) continue;

                var edge = kvp.Key;
                var p1 = ctx.MeshData.Vertices[edge.Item1].Position;
                var p2 = ctx.MeshData.Vertices[edge.Item2].Position;
                var sp1 = ctx.WorldToScreenPos(p1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                var sp2 = ctx.WorldToScreenPos(p2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                float dist = DistanceToLineSegment(mousePos, sp1, sp2);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestEdge = edge;
                }
            }

            return bestEdge;
        }

        /// <summary>
        /// 2つの面を統合（共有辺を消去）
        /// </summary>
        private void MergeFaces(ToolContext ctx, int faceIdx1, int faceIdx2, (int, int) sharedEdge)
        {
            var meshData = ctx.MeshData;
            var face1 = meshData.Faces[faceIdx1];
            var face2 = meshData.Faces[faceIdx2];

            // 新しい頂点リストを作成
            var newVerts = new List<int>();

            // face1から共有辺以外の頂点を追加
            int n1 = face1.VertexIndices.Count;
            int sharedStart1 = -1;

            for (int i = 0; i < n1; i++)
            {
                int v1 = face1.VertexIndices[i];
                int v2 = face1.VertexIndices[(i + 1) % n1];
                var edge = NormalizeEdge(v1, v2);

                if (edge == sharedEdge)
                {
                    sharedStart1 = i;
                    break;
                }
            }

            if (sharedStart1 < 0) return;

            // face1の頂点（共有辺の終点からスタート）
            for (int i = 0; i < n1 - 1; i++)
            {
                int idx = (sharedStart1 + 1 + i) % n1;
                newVerts.Add(face1.VertexIndices[idx]);
            }

            // face2の頂点（共有辺以外）
            int n2 = face2.VertexIndices.Count;
            int sharedStart2 = -1;

            for (int i = 0; i < n2; i++)
            {
                int v1 = face2.VertexIndices[i];
                int v2 = face2.VertexIndices[(i + 1) % n2];
                var edge = NormalizeEdge(v1, v2);

                if (edge == sharedEdge)
                {
                    sharedStart2 = i;
                    break;
                }
            }

            if (sharedStart2 < 0) return;

            for (int i = 0; i < n2 - 1; i++)
            {
                int idx = (sharedStart2 + 1 + i) % n2;
                int v = face2.VertexIndices[idx];

                // 既に追加済みでなければ追加
                if (!newVerts.Contains(v))
                {
                    newVerts.Add(v);
                }
            }

            // 新しい面を作成
            var newFace = new Face { VertexIndices = newVerts };

            // 元の面を削除（インデックスが大きい方から）
            int maxIdx = Mathf.Max(faceIdx1, faceIdx2);
            int minIdx = Mathf.Min(faceIdx1, faceIdx2);

            meshData.Faces.RemoveAt(maxIdx);
            meshData.Faces.RemoveAt(minIdx);

            // 新しい面を追加
            meshData.Faces.Add(newFace);

            ctx.SyncMesh?.Invoke();
        }

        /// <summary>
        /// 最も近い辺を検出（インデックスベース）
        /// </summary>
        private (int, int) FindNearestEdge(ToolContext ctx, Vector2 mousePos)
        {
            float bestDist = EDGE_CLICK_THRESHOLD;
            (int, int) bestEdge = (-1, -1);

            var edgeSet = new HashSet<(int, int)>();

            foreach (var face in ctx.MeshData.Faces)
            {
                int n = face.VertexIndices.Count;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];
                    var edge = NormalizeEdge(v1, v2);

                    if (edgeSet.Contains(edge)) continue;
                    edgeSet.Add(edge);

                    var p1 = ctx.MeshData.Vertices[edge.Item1].Position;
                    var p2 = ctx.MeshData.Vertices[edge.Item2].Position;
                    var sp1 = ctx.WorldToScreenPos(p1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    var sp2 = ctx.WorldToScreenPos(p2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                    float dist = DistanceToLineSegment(mousePos, sp1, sp2);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestEdge = edge;
                    }
                }
            }

            return bestEdge;
        }

        /// <summary>
        /// 辺を描画（インデックスベース）
        /// </summary>
        private void DrawEdge(ToolContext ctx, (int, int) edge)
        {
            var p1 = ctx.MeshData.Vertices[edge.Item1].Position;
            var p2 = ctx.MeshData.Vertices[edge.Item2].Position;
            var sp1 = ctx.WorldToScreenPos(p1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            var sp2 = ctx.WorldToScreenPos(p2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Handles.DrawAAPolyLine(4f, new Vector3(sp1.x, sp1.y, 0), new Vector3(sp2.x, sp2.y, 0));
        }
    }
}
