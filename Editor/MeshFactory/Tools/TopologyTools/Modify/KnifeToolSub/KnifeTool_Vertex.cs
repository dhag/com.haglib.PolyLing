// Tools/KnifeTool.Vertex.cs
// ナイフツール - Vertexモード

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using static MeshFactory.Gizmo.GLGizmoDrawer;

namespace MeshFactory.Tools
{
    public partial class KnifeTool
    {
        // ================================================================
        // Vertex + ドラッグ
        // ================================================================

        private bool HandleVertexDragMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ChainMode && !AutoChain && _firstVertexWorldPos.HasValue)
            {
                _isDragging = true;
                _targetEdgeWorldPos = null;
                _startScreenPos = mousePos;
                _currentScreenPos = mousePos;
                ctx.Repaint?.Invoke();
                return true;
            }

            var clickedVertexPos = FindNearestVertexWorldPos(ctx, mousePos, VERTEX_CLICK_THRESHOLD);
            if (!clickedVertexPos.HasValue) return false;

            _isDragging = true;
            _firstVertexWorldPos = clickedVertexPos;
            _targetEdgeWorldPos = null;
            _startScreenPos = mousePos;
            _currentScreenPos = mousePos;
            ctx.Repaint?.Invoke();
            return true;
        }

        private bool HandleVertexDragMouseDrag(ToolContext ctx, Vector2 mousePos)
        {
            if (!_isDragging || !_firstVertexWorldPos.HasValue) return false;

            _currentScreenPos = mousePos;
            _isShiftHeld = Event.current.shift;
            if (_isShiftHeld) _currentScreenPos = SnapToAxis(_startScreenPos, mousePos);

            _targetEdgeWorldPos = FindNearestNonAdjacentEdge(ctx, _currentScreenPos, _firstVertexWorldPos.Value, EDGE_CLICK_THRESHOLD * 2)?.edgeWorldPos;
            ctx.Repaint?.Invoke();
            return true;
        }

        private bool HandleVertexDragMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            if (!_isDragging) return false;
            _isDragging = false;
            _currentScreenPos = _isShiftHeld ? SnapToAxis(_startScreenPos, mousePos) : mousePos;

            if (_firstVertexWorldPos.HasValue && _targetEdgeWorldPos.HasValue)
            {
                var targetEdge = FindNearestNonAdjacentEdge(ctx, _currentScreenPos, _firstVertexWorldPos.Value, EDGE_CLICK_THRESHOLD * 2);
                if (targetEdge.HasValue)
                {
                    float cutRatio = GetVertexCutRatio(ctx, _currentScreenPos, targetEdge.Value.edgeWorldPos);

                    if (ChainMode && AutoChain)
                    {
                        ExecuteVertexChainCut(ctx, _firstVertexWorldPos.Value, targetEdge.Value.faceIndex, targetEdge.Value.edgeWorldPos, cutRatio);
                        _firstVertexWorldPos = null;
                    }
                    else if (ChainMode && !AutoChain)
                    {
                        var cutPos = Vector3.Lerp(targetEdge.Value.edgeWorldPos.Item1, targetEdge.Value.edgeWorldPos.Item2, cutRatio);
                        ExecuteVertexCut(ctx, _firstVertexWorldPos.Value, targetEdge.Value.faceIndex, targetEdge.Value.edgeWorldPos, cutRatio);
                        _firstVertexWorldPos = cutPos;
                    }
                    else
                    {
                        ExecuteVertexCut(ctx, _firstVertexWorldPos.Value, targetEdge.Value.faceIndex, targetEdge.Value.edgeWorldPos, cutRatio);
                        _firstVertexWorldPos = null;
                    }
                }
                else
                {
                    _firstVertexWorldPos = null;
                }
            }
            else
            {
                _firstVertexWorldPos = null;
            }

            _targetEdgeWorldPos = null;
            ctx.Repaint?.Invoke();
            return true;
        }

        private void DrawVertexDragGizmo(ToolContext ctx)
        {
            UnityEditor_Handles.BeginGUI();

            if (_isDragging && _firstVertexWorldPos.HasValue)
            {
                var sp = ctx.WorldToScreenPos(_firstVertexWorldPos.Value, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                UnityEditor_Handles.color = Color.yellow;
                UnityEditor_Handles.DrawSolidDisc(new Vector3(sp.x, sp.y, 0), Vector3.forward, 7f);

                UnityEditor_Handles.color = new Color(1f, 0.5f, 0f, 0.8f);
                UnityEditor_Handles.DrawAAPolyLine(2f, new Vector3(sp.x, sp.y, 0), new Vector3(_currentScreenPos.x, _currentScreenPos.y, 0));

                var candidates = FindNonAdjacentEdgesForVertex(ctx, _firstVertexWorldPos.Value);
                foreach (var (faceIdx, edgeLocalIdx, edgePos) in candidates)
                {
                    bool isTarget = _targetEdgeWorldPos.HasValue && IsSameEdgePosition(edgePos, _targetEdgeWorldPos.Value);
                    if (isTarget)
                    {
                        UnityEditor_Handles.color = Color.cyan;
                        DrawEdgeByWorldPos(ctx, edgePos);
                        float cutRatio = GetVertexCutRatio(ctx, _currentScreenPos, edgePos);
                        var midPoint = Vector3.Lerp(edgePos.Item1, edgePos.Item2, cutRatio);
                        var midSp = ctx.WorldToScreenPos(midPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                        UnityEditor_Handles.color = Color.green;
                        UnityEditor_Handles.DrawAAPolyLine(3f, new Vector3(sp.x, sp.y, 0), new Vector3(midSp.x, midSp.y, 0));
                    }
                    else
                    {
                        UnityEditor_Handles.color = new Color(0f, 1f, 0f, 0.3f);
                        DrawEdgeByWorldPos(ctx, edgePos);
                    }
                }
            }
            else if (ChainMode && !AutoChain && _firstVertexWorldPos.HasValue)
            {
                var sp = ctx.WorldToScreenPos(_firstVertexWorldPos.Value, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                UnityEditor_Handles.color = Color.yellow;
                UnityEditor_Handles.DrawSolidDisc(new Vector3(sp.x, sp.y, 0), Vector3.forward, 7f);
            }
            else
            {
                var hoveredVertex = FindNearestVertexWorldPos(ctx, Event.current.mousePosition, VERTEX_CLICK_THRESHOLD);
                if (hoveredVertex.HasValue)
                {
                    var sp = ctx.WorldToScreenPos(hoveredVertex.Value, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    UnityEditor_Handles.color = Color.white;
                    UnityEditor_Handles.DrawSolidDisc(new Vector3(sp.x, sp.y, 0), Vector3.forward, 6f);
                }
            }

            UnityEditor_Handles.EndGUI();
        }

        // ================================================================
        // Vertex + EdgeSelect
        // ================================================================

        private bool HandleVertexEdgeSelectMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (Event.current.keyCode == KeyCode.Escape)
            {
                Reset();
                ctx.Repaint?.Invoke();
                return true;
            }

            if (!_firstVertexWorldPos.HasValue)
            {
                var clickedVertexPos = FindNearestVertexWorldPos(ctx, mousePos, VERTEX_CLICK_THRESHOLD);
                if (!clickedVertexPos.HasValue) return false;
                _firstVertexWorldPos = clickedVertexPos;
                _targetEdgeWorldPos = null;
                ctx.Repaint?.Invoke();
                return true;
            }
            else
            {
                (int faceIndex, (Vector3, Vector3) edgeWorldPos)? targetEdge = null;
                if (_targetEdgeWorldPos.HasValue)
                {
                    var facesWithEdge = FindFacesWithEdgePosition(ctx, _targetEdgeWorldPos.Value);
                    if (facesWithEdge.Count > 0)
                        targetEdge = (facesWithEdge[0].faceIndex, _targetEdgeWorldPos.Value);
                }
                if (!targetEdge.HasValue)
                    targetEdge = FindNearestNonAdjacentEdge(ctx, mousePos, _firstVertexWorldPos.Value, EDGE_CLICK_THRESHOLD);

                if (targetEdge.HasValue)
                {
                    float cutRatio = GetVertexCutRatio(ctx, mousePos, targetEdge.Value.edgeWorldPos);

                    if (ChainMode && AutoChain)
                    {
                        ExecuteVertexChainCut(ctx, _firstVertexWorldPos.Value, targetEdge.Value.faceIndex, targetEdge.Value.edgeWorldPos, cutRatio);
                        _firstVertexWorldPos = null;
                    }
                    else if (ChainMode && !AutoChain)
                    {
                        var cutPos = Vector3.Lerp(targetEdge.Value.edgeWorldPos.Item1, targetEdge.Value.edgeWorldPos.Item2, cutRatio);
                        ExecuteVertexCut(ctx, _firstVertexWorldPos.Value, targetEdge.Value.faceIndex, targetEdge.Value.edgeWorldPos, cutRatio);
                        _firstVertexWorldPos = cutPos;
                    }
                    else
                    {
                        ExecuteVertexCut(ctx, _firstVertexWorldPos.Value, targetEdge.Value.faceIndex, targetEdge.Value.edgeWorldPos, cutRatio);
                        _firstVertexWorldPos = null;
                    }
                }
                else
                {
                    _firstVertexWorldPos = null;
                }

                _targetEdgeWorldPos = null;
                ctx.Repaint?.Invoke();
                return true;
            }
        }

        private bool HandleVertexEdgeSelectMouseDrag(ToolContext ctx, Vector2 mousePos)
        {
            if (_firstVertexWorldPos.HasValue)
                _targetEdgeWorldPos = FindNearestNonAdjacentEdge(ctx, mousePos, _firstVertexWorldPos.Value, EDGE_CLICK_THRESHOLD)?.edgeWorldPos;
            else
                _targetEdgeWorldPos = null;
            ctx.Repaint?.Invoke();
            return false;
        }

        private void DrawVertexEdgeSelectGizmo(ToolContext ctx)
        {
            UnityEditor_Handles.BeginGUI();

            if (_firstVertexWorldPos.HasValue)
            {
                var sp = ctx.WorldToScreenPos(_firstVertexWorldPos.Value, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                UnityEditor_Handles.color = Color.yellow;
                UnityEditor_Handles.DrawSolidDisc(new Vector3(sp.x, sp.y, 0), Vector3.forward, 7f);

                var candidates = FindNonAdjacentEdgesForVertex(ctx, _firstVertexWorldPos.Value);
                foreach (var (faceIdx, edgeLocalIdx, edgePos) in candidates)
                {
                    bool isHovered = _targetEdgeWorldPos.HasValue && IsSameEdgePosition(edgePos, _targetEdgeWorldPos.Value);
                    if (isHovered)
                    {
                        UnityEditor_Handles.color = Color.cyan;
                        DrawEdgeByWorldPos(ctx, edgePos);
                        float cutRatio = GetVertexCutRatio(ctx, Event.current.mousePosition, edgePos);
                        var midPoint = Vector3.Lerp(edgePos.Item1, edgePos.Item2, cutRatio);
                        var midSp = ctx.WorldToScreenPos(midPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                        UnityEditor_Handles.color = new Color(1f, 0.5f, 0f, 0.8f);
                        UnityEditor_Handles.DrawAAPolyLine(3f, new Vector3(sp.x, sp.y, 0), new Vector3(midSp.x, midSp.y, 0));
                    }
                    else
                    {
                        UnityEditor_Handles.color = new Color(0f, 1f, 0f, 0.4f);
                        DrawEdgeByWorldPos(ctx, edgePos);
                    }
                }
            }
            else
            {
                var hoveredVertex = FindNearestVertexWorldPos(ctx, Event.current.mousePosition, VERTEX_CLICK_THRESHOLD);
                if (hoveredVertex.HasValue)
                {
                    var sp = ctx.WorldToScreenPos(hoveredVertex.Value, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    UnityEditor_Handles.color = Color.white;
                    UnityEditor_Handles.DrawSolidDisc(new Vector3(sp.x, sp.y, 0), Vector3.forward, 6f);
                }
            }

            UnityEditor_Handles.EndGUI();
        }

        // ================================================================
        // Vertex切断実行
        // ================================================================

        private void ExecuteVertexCut(ToolContext ctx, Vector3 vertexWorldPos, int faceIdx, (Vector3, Vector3) edgeWorldPos, float cutRatio)
        {
            var meshData = ctx.MeshData;
            var face = meshData.Faces[faceIdx];
            int n = face.VertexIndices.Count;

            int vertexLocalIdx = -1;
            for (int i = 0; i < n; i++)
            {
                var pos = meshData.Vertices[face.VertexIndices[i]].Position;
                if (Vector3.Distance(pos, vertexWorldPos) < POSITION_EPSILON)
                {
                    vertexLocalIdx = i;
                    break;
                }
            }
            if (vertexLocalIdx < 0) return;

            int edgeLocalIdx = -1;
            for (int i = 0; i < n; i++)
            {
                int v1 = face.VertexIndices[i];
                int v2 = face.VertexIndices[(i + 1) % n];
                var p1 = meshData.Vertices[v1].Position;
                var p2 = meshData.Vertices[v2].Position;
                var pos = NormalizeEdgeWorldPos(p1, p2);
                if (IsSameEdgePosition(pos, edgeWorldPos))
                {
                    edgeLocalIdx = i;
                    break;
                }
            }
            if (edgeLocalIdx < 0) return;

            // Undo用スナップショット（切断前）
            MeshDataSnapshot beforeSnapshot = ctx.UndoController != null 
                ? MeshDataSnapshot.Capture(ctx.UndoController.MeshContext) 
                : null;

            var cutPos = Vector3.Lerp(edgeWorldPos.Item1, edgeWorldPos.Item2, cutRatio);
            int newVertexIdx = meshData.VertexCount;
            var newVertex = new Vertex(cutPos);

            int ev1 = face.VertexIndices[edgeLocalIdx];
            int ev2 = face.VertexIndices[(edgeLocalIdx + 1) % n];
            var vert1 = meshData.Vertices[ev1];
            var vert2 = meshData.Vertices[ev2];
            if (vert1.UVs.Count > 0 && vert2.UVs.Count > 0)
                newVertex.UVs.Add(Vector2.Lerp(vert1.UVs[0], vert2.UVs[0], cutRatio));
            if (vert1.Normals.Count > 0 && vert2.Normals.Count > 0)
                newVertex.Normals.Add(Vector3.Lerp(vert1.Normals[0], vert2.Normals[0], cutRatio).normalized);

            meshData.Vertices.Add(newVertex);
            SplitFaceVertexToEdge(meshData, faceIdx, vertexLocalIdx, edgeLocalIdx, newVertexIdx);
            ctx.SyncMesh?.Invoke();

            // Undo記録
            if (ctx.UndoController != null && beforeSnapshot != null)
            {
                var afterSnapshot = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
                ctx.UndoController.RecordMeshTopologyChange(beforeSnapshot, afterSnapshot, "Vertex Cut");
            }
        }

        private void SplitFaceVertexToEdge(MeshData meshData, int faceIdx, int vertexLocalIdx, int edgeLocalIdx, int newVertexIdx)
        {
            var face = meshData.Faces[faceIdx];
            var verts = face.VertexIndices;
            var uvs = face.UVIndices;
            var normals = face.NormalIndices;
            int n = verts.Count;

            int edgeEndIdx = (edgeLocalIdx + 1) % n;

            var face1Verts = new List<int>();
            var face1UVs = new List<int>();
            var face1Normals = new List<int>();

            for (int i = vertexLocalIdx; ; i = (i + 1) % n)
            {
                face1Verts.Add(verts[i]);
                face1UVs.Add(uvs.Count > i ? uvs[i] : 0);
                face1Normals.Add(normals.Count > i ? normals[i] : 0);
                if (i == edgeLocalIdx) break;
            }
            face1Verts.Add(newVertexIdx);
            face1UVs.Add(0);
            face1Normals.Add(0);

            var face2Verts = new List<int>();
            var face2UVs = new List<int>();
            var face2Normals = new List<int>();

            face2Verts.Add(newVertexIdx);
            face2UVs.Add(0);
            face2Normals.Add(0);

            for (int i = edgeEndIdx; ; i = (i + 1) % n)
            {
                face2Verts.Add(verts[i]);
                face2UVs.Add(uvs.Count > i ? uvs[i] : 0);
                face2Normals.Add(normals.Count > i ? normals[i] : 0);
                if (i == vertexLocalIdx) break;
            }

            meshData.Faces[faceIdx] = new Face { VertexIndices = face1Verts, UVIndices = face1UVs, NormalIndices = face1Normals };
            meshData.Faces.Add(new Face { VertexIndices = face2Verts, UVIndices = face2UVs, NormalIndices = face2Normals });
        }

        private void ExecuteVertexChainCut(ToolContext ctx, Vector3 startVertexPos, int startFaceIdx, (Vector3, Vector3) startEdgePos, float cutRatio)
        {
            // Undo用スナップショット（切断前）
            MeshDataSnapshot beforeSnapshot = ctx.UndoController != null 
                ? MeshDataSnapshot.Capture(ctx.UndoController.MeshContext) 
                : null;

            var meshData = ctx.MeshData;
            var processedFaces = new HashSet<int>();

            var firstFace = meshData.Faces[startFaceIdx];
            int n = firstFace.VertexIndices.Count;

            int vertexLocalIdx = -1;
            for (int i = 0; i < n; i++)
            {
                var pos = meshData.Vertices[firstFace.VertexIndices[i]].Position;
                if (Vector3.Distance(pos, startVertexPos) < POSITION_EPSILON)
                {
                    vertexLocalIdx = i;
                    break;
                }
            }
            if (vertexLocalIdx < 0) { ctx.SyncMesh?.Invoke(); return; }

            int edgeLocalIdx = -1;
            for (int i = 0; i < n; i++)
            {
                int v1 = firstFace.VertexIndices[i];
                int v2 = firstFace.VertexIndices[(i + 1) % n];
                var edgePos = NormalizeEdgeWorldPos(meshData.Vertices[v1].Position, meshData.Vertices[v2].Position);
                if (IsSameEdgePosition(edgePos, startEdgePos))
                {
                    edgeLocalIdx = i;
                    break;
                }
            }
            if (edgeLocalIdx < 0) { ctx.SyncMesh?.Invoke(); return; }

            var cutPos = Vector3.Lerp(startEdgePos.Item1, startEdgePos.Item2, cutRatio);
            int newVertexIdx = meshData.VertexCount;
            var newVertex = new Vertex(cutPos);

            int ev1 = firstFace.VertexIndices[edgeLocalIdx];
            int ev2 = firstFace.VertexIndices[(edgeLocalIdx + 1) % n];
            var vert1 = meshData.Vertices[ev1];
            var vert2 = meshData.Vertices[ev2];
            if (vert1.UVs.Count > 0 && vert2.UVs.Count > 0)
                newVertex.UVs.Add(Vector2.Lerp(vert1.UVs[0], vert2.UVs[0], cutRatio));
            if (vert1.Normals.Count > 0 && vert2.Normals.Count > 0)
                newVertex.Normals.Add(Vector3.Lerp(vert1.Normals[0], vert2.Normals[0], cutRatio).normalized);
            meshData.Vertices.Add(newVertex);

            processedFaces.Add(startFaceIdx);
            SplitFaceVertexToEdge(meshData, startFaceIdx, vertexLocalIdx, edgeLocalIdx, newVertexIdx);

            var previousEdgePos = startEdgePos;

            for (int iter = 0; iter < 100; iter++)
            {
                var facesWithEdge = FindFacesWithEdgePosition(ctx, previousEdgePos);

                int nextFaceIdx = -1;
                int prevEdgeLocalIdx = -1;
                foreach (var (faceIdx, localIdx) in facesWithEdge)
                {
                    if (!processedFaces.Contains(faceIdx))
                    {
                        nextFaceIdx = faceIdx;
                        prevEdgeLocalIdx = localIdx;
                        break;
                    }
                }

                if (nextFaceIdx < 0) break;

                var face = meshData.Faces[nextFaceIdx];
                n = face.VertexIndices.Count;
                if (n != 4) break;

                int oppEdgeLocalIdx = (prevEdgeLocalIdx + 2) % n;
                int ov1 = face.VertexIndices[oppEdgeLocalIdx];
                int ov2 = face.VertexIndices[(oppEdgeLocalIdx + 1) % n];
                var oppEdgePos = NormalizeEdgeWorldPos(meshData.Vertices[ov1].Position, meshData.Vertices[ov2].Position);

                var cutPos1 = Vector3.Lerp(previousEdgePos.Item1, previousEdgePos.Item2, cutRatio);
                var cutPos2 = Vector3.Lerp(oppEdgePos.Item1, oppEdgePos.Item2, cutRatio);

                int newVIdx1 = meshData.VertexCount;
                var newVert1 = new Vertex(cutPos1);
                int pv1 = face.VertexIndices[prevEdgeLocalIdx];
                int pv2 = face.VertexIndices[(prevEdgeLocalIdx + 1) % n];
                var pVert1 = meshData.Vertices[pv1];
                var pVert2 = meshData.Vertices[pv2];
                if (pVert1.UVs.Count > 0 && pVert2.UVs.Count > 0)
                    newVert1.UVs.Add(Vector2.Lerp(pVert1.UVs[0], pVert2.UVs[0], cutRatio));
                if (pVert1.Normals.Count > 0 && pVert2.Normals.Count > 0)
                    newVert1.Normals.Add(Vector3.Lerp(pVert1.Normals[0], pVert2.Normals[0], cutRatio).normalized);
                meshData.Vertices.Add(newVert1);

                int newVIdx2 = meshData.VertexCount;
                var newVert2 = new Vertex(cutPos2);
                var oVert1 = meshData.Vertices[ov1];
                var oVert2 = meshData.Vertices[ov2];
                if (oVert1.UVs.Count > 0 && oVert2.UVs.Count > 0)
                    newVert2.UVs.Add(Vector2.Lerp(oVert1.UVs[0], oVert2.UVs[0], cutRatio));
                if (oVert1.Normals.Count > 0 && oVert2.Normals.Count > 0)
                    newVert2.Normals.Add(Vector3.Lerp(oVert1.Normals[0], oVert2.Normals[0], cutRatio).normalized);
                meshData.Vertices.Add(newVert2);

                var inter1 = new EdgeIntersection { EdgeStartIndex = prevEdgeLocalIdx, EdgeEndIndex = (prevEdgeLocalIdx + 1) % n, T = cutRatio, WorldPos = cutPos1 };
                var inter2 = new EdgeIntersection { EdgeStartIndex = oppEdgeLocalIdx, EdgeEndIndex = (oppEdgeLocalIdx + 1) % n, T = cutRatio, WorldPos = cutPos2 };

                var (face1, face2) = SplitFace(face, inter1, inter2, newVIdx1, newVIdx2);
                meshData.Faces[nextFaceIdx] = face1;
                meshData.Faces.Add(face2);

                processedFaces.Add(nextFaceIdx);
                previousEdgePos = oppEdgePos;
            }

            ctx.SyncMesh?.Invoke();

            // Undo記録
            if (ctx.UndoController != null && beforeSnapshot != null)
            {
                var afterSnapshot = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
                ctx.UndoController.RecordMeshTopologyChange(beforeSnapshot, afterSnapshot, "Vertex Chain Cut");
            }
        }

        // ================================================================
        // Vertexヘルパー
        // ================================================================

        private Vector3? FindNearestVertexWorldPos(ToolContext ctx, Vector2 mousePos, float threshold)
        {
            float bestDist = threshold;
            Vector3? bestPos = null;

            for (int i = 0; i < ctx.MeshData.VertexCount; i++)
            {
                var worldPos = ctx.MeshData.Vertices[i].Position;
                var screenPos = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                float dist = Vector2.Distance(mousePos, screenPos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPos = worldPos;
                }
            }

            return bestPos;
        }

        private List<(int faceIndex, int vertexLocalIdx)> FindFacesWithVertexPosition(ToolContext ctx, Vector3 vertexWorldPos)
        {
            var result = new List<(int, int)>();

            for (int faceIdx = 0; faceIdx < ctx.MeshData.FaceCount; faceIdx++)
            {
                var face = ctx.MeshData.Faces[faceIdx];
                int n = face.VertexIndices.Count;

                for (int i = 0; i < n; i++)
                {
                    int vIdx = face.VertexIndices[i];
                    var pos = ctx.MeshData.Vertices[vIdx].Position;
                    if (Vector3.Distance(pos, vertexWorldPos) < POSITION_EPSILON)
                    {
                        result.Add((faceIdx, i));
                        break;
                    }
                }
            }

            return result;
        }

        private List<(int faceIndex, int edgeLocalIdx, (Vector3, Vector3) edgeWorldPos)> FindNonAdjacentEdgesForVertex(ToolContext ctx, Vector3 vertexWorldPos)
        {
            var result = new List<(int, int, (Vector3, Vector3))>();
            var facesWithVertex = FindFacesWithVertexPosition(ctx, vertexWorldPos);

            foreach (var (faceIdx, vertexLocalIdx) in facesWithVertex)
            {
                var face = ctx.MeshData.Faces[faceIdx];
                int n = face.VertexIndices.Count;

                int prevEdgeIdx = (vertexLocalIdx - 1 + n) % n;
                int nextEdgeIdx = vertexLocalIdx;

                for (int i = 0; i < n; i++)
                {
                    if (i == prevEdgeIdx || i == nextEdgeIdx) continue;

                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];
                    var p1 = ctx.MeshData.Vertices[v1].Position;
                    var p2 = ctx.MeshData.Vertices[v2].Position;
                    var edgePos = NormalizeEdgeWorldPos(p1, p2);
                    result.Add((faceIdx, i, edgePos));
                }
            }

            return result;
        }

        private (int faceIndex, (Vector3, Vector3) edgeWorldPos)? FindNearestNonAdjacentEdge(ToolContext ctx, Vector2 mousePos, Vector3 vertexWorldPos, float threshold)
        {
            var candidates = FindNonAdjacentEdgesForVertex(ctx, vertexWorldPos);
            if (candidates.Count == 0) return null;

            float bestDist = threshold;
            (int faceIndex, (Vector3, Vector3) edgeWorldPos)? best = null;

            foreach (var (faceIdx, edgeLocalIdx, edgePos) in candidates)
            {
                var sp1 = ctx.WorldToScreenPos(edgePos.Item1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                var sp2 = ctx.WorldToScreenPos(edgePos.Item2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                float dist = DistanceToLineSegment(mousePos, sp1, sp2);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = (faceIdx, edgePos);
                }
            }

            return best;
        }
    }
}
