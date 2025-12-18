// Tools/KnifeTool.Cut.cs
// ナイフツール - Cutモード（ドラッグ + EdgeSelect）

using System.Collections.Generic;
using System.Linq;
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
        // Cut + ドラッグ
        // ================================================================

        private bool HandleDragCutMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            _isDragging = true;
            _startScreenPos = mousePos;
            _currentScreenPos = mousePos;
            _isShiftHeld = Event.current.shift;
            _targetFaceIndex = -1;
            _intersections.Clear();
            _targetFaceIndex = FindFrontmostFaceAtPosition(ctx, mousePos);
            ctx.Repaint?.Invoke();
            return true;
        }

        private bool HandleDragCutMouseDrag(ToolContext ctx, Vector2 mousePos)
        {
            if (!_isDragging) return false;
            _currentScreenPos = mousePos;
            _isShiftHeld = Event.current.shift;
            if (_isShiftHeld) _currentScreenPos = SnapToAxis(_startScreenPos, mousePos);
            UpdateIntersections(ctx);
            ctx.Repaint?.Invoke();
            return true;
        }

        private bool HandleDragCutMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            if (!_isDragging) return false;
            _isDragging = false;
            _currentScreenPos = _isShiftHeld ? SnapToAxis(_startScreenPos, mousePos) : mousePos;
            UpdateIntersections(ctx);

            if (ChainMode)
            {
                if (AutoChain && _intersections.Count >= 2)
                    ExecuteAutoDragChainCut(ctx);
                else if (!AutoChain && _chainTargets.Count > 0)
                    ExecuteChainDragCut(ctx);
            }
            else if (_intersections.Count >= 2)
            {
                ExecuteCut(ctx);
            }

            _targetFaceIndex = -1;
            _intersections.Clear();
            _chainTargets.Clear();
            ctx.Repaint?.Invoke();
            return true;
        }

        private void DrawDragCutGizmo(ToolContext ctx)
        {
            if (_isDragging) DrawWire(ctx);
            if (ChainMode && _chainTargets.Count > 0)
            {
                DrawChainIntersections(ctx);
                DrawChainTargetFaces(ctx);
                
                // マウスダウンに最も近い切断点に赤丸
                DrawNearestCutPoint(ctx);
            }
            else
            {
                DrawIntersections(ctx);
                if (_targetFaceIndex >= 0) DrawTargetFaceHighlight(ctx);
                
                // マウスダウンに最も近い切断点に赤丸
                DrawNearestCutPoint(ctx);
            }
        }
        
        /// <summary>
        /// マウスダウン位置に最も近い切断点に赤丸を描画
        /// </summary>
        private void DrawNearestCutPoint(ToolContext ctx)
        {
            if (_intersections.Count < 1) return;
            
            // マウスダウン位置に最も近い交差点を探す
            float minDist = float.MaxValue;
            Vector2 nearestScreenPos = Vector2.zero;
            
            foreach (var inter in _intersections)
            {
                float dist = Vector2.Distance(_startScreenPos, inter.ScreenPos);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearestScreenPos = inter.ScreenPos;
                }
            }
            
            if (minDist < float.MaxValue)
            {
                UnityEditor_Handles.BeginGUI();
                UnityEditor_Handles.color = Color.red;
                UnityEditor_Handles.DrawSolidDisc(new Vector3(nearestScreenPos.x, nearestScreenPos.y, 0), Vector3.forward, 5f);
                UnityEditor_Handles.EndGUI();
            }
        }

        // ================================================================
        // Cut + EdgeSelect
        // ================================================================

        private bool HandleEdgeSelectMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (Event.current.keyCode == KeyCode.Escape)
            {
                Reset();
                ctx.Repaint?.Invoke();
                return true;
            }

            if (!_firstEdgeWorldPos.HasValue)
            {
                var clickedEdgePos = _hoveredEdgeWorldPos ?? FindNearestEdgeWorldPos(ctx, mousePos, EDGE_CLICK_THRESHOLD);
                if (!clickedEdgePos.HasValue) return false;

                _firstEdgeWorldPos = clickedEdgePos;
                _firstCutRatio = GetEdgeCutRatio(ctx, mousePos, clickedEdgePos.Value);
                _beltEdgePositions.Clear();
                ctx.Repaint?.Invoke();
                return true;
            }
            else
            {
                // クリックした辺を取得
                var clickedEdgePos = _hoveredEdgeWorldPos ?? FindNearestEdgeWorldPos(ctx, mousePos, EDGE_CLICK_THRESHOLD);
                
                // 開始辺と同じ辺をクリックした場合はキャンセル
                if (clickedEdgePos.HasValue && IsSameEdgePosition(clickedEdgePos.Value, _firstEdgeWorldPos.Value))
                {
                    Reset();
                    ctx.Repaint?.Invoke();
                    return true;
                }
                
                if (!_hoveredEdgeWorldPos.HasValue) return false;
                var secondEdgePos = _hoveredEdgeWorldPos.Value;
                _cutRatio = GetEdgeCutRatio(ctx, mousePos, secondEdgePos);

                _targetFaceIndex = FindFaceWithBothEdges(ctx, _firstEdgeWorldPos.Value, secondEdgePos);
                if (_targetFaceIndex < 0)
                {
                    _firstEdgeWorldPos = null;
                    _hoveredEdgeWorldPos = null;
                    ctx.Repaint?.Invoke();
                    return false;
                }

                if (ChainMode && AutoChain)
                    ExecuteEdgeSelectAutoChainCut(ctx, _firstEdgeWorldPos.Value, secondEdgePos, _firstCutRatio, _cutRatio);
                else
                    ExecuteEdgeSelectSingleCut(ctx, _firstEdgeWorldPos.Value, secondEdgePos, _firstCutRatio, _cutRatio);

                _targetFaceIndex = -1;
                _intersections.Clear();
                _chainTargets.Clear();
                _firstEdgeWorldPos = null;
                _hoveredEdgeWorldPos = null;
                _beltEdgePositions.Clear();
                ctx.Repaint?.Invoke();
                return true;
            }
        }

        private bool HandleEdgeSelectMouseDrag(ToolContext ctx, Vector2 mousePos)
        {
            var nearestEdge = FindNearestEdgeWorldPos(ctx, mousePos, EDGE_CLICK_THRESHOLD);
            if (_firstEdgeWorldPos.HasValue && nearestEdge.HasValue && IsSameEdgePosition(nearestEdge.Value, _firstEdgeWorldPos.Value))
                _hoveredEdgeWorldPos = null;
            else
                _hoveredEdgeWorldPos = nearestEdge;
            ctx.Repaint?.Invoke();
            return false;
        }

        private void DrawEdgeSelectGizmo(ToolContext ctx)
        {
            UnityEditor_Handles.BeginGUI();
            if (_hoveredEdgeWorldPos.HasValue)
            {
                UnityEditor_Handles.color = Color.cyan;
                DrawEdgeByWorldPos(ctx, _hoveredEdgeWorldPos.Value);
                
                // ホバー中の辺上の切断位置に赤丸
                float hoverRatio = GetEdgeCutRatio(ctx, Event.current.mousePosition, _hoveredEdgeWorldPos.Value);
                var hoverCutPoint = Vector3.Lerp(_hoveredEdgeWorldPos.Value.Item1, _hoveredEdgeWorldPos.Value.Item2, hoverRatio);
                var hoverSp = ctx.WorldToScreenPos(hoverCutPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                UnityEditor_Handles.color = Color.red;
                UnityEditor_Handles.DrawSolidDisc(new Vector3(hoverSp.x, hoverSp.y, 0), Vector3.forward, 5f);
            }
            if (_firstEdgeWorldPos.HasValue)
            {
                UnityEditor_Handles.color = Color.yellow;
                DrawEdgeByWorldPos(ctx, _firstEdgeWorldPos.Value);
                
                // 開始辺上のクリック位置に赤丸
                var startCutPoint = Vector3.Lerp(_firstEdgeWorldPos.Value.Item1, _firstEdgeWorldPos.Value.Item2, _firstCutRatio);
                var startSp = ctx.WorldToScreenPos(startCutPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                UnityEditor_Handles.color = Color.red;
                UnityEditor_Handles.DrawSolidDisc(new Vector3(startSp.x, startSp.y, 0), Vector3.forward, 5f);
            }
            UnityEditor_Handles.EndGUI();
        }

        // ================================================================
        // Cut 切断実行
        // ================================================================

        private void ExecuteEdgeSelectSingleCut(
            ToolContext ctx,
            (Vector3, Vector3) edge1Pos,
            (Vector3, Vector3) edge2Pos,
            float cutRatio1,
            float cutRatio2)
        {
            if (_targetFaceIndex < 0) return;

            var meshData = ctx.MeshData;
            var face = meshData.Faces[_targetFaceIndex];
            int n = face.VertexIndices.Count;

            int edge1LocalIdx = -1, edge2LocalIdx = -1;
            float edge1T = cutRatio1, edge2T = cutRatio2;

            for (int i = 0; i < n; i++)
            {
                int v1 = face.VertexIndices[i];
                int v2 = face.VertexIndices[(i + 1) % n];
                var p1 = meshData.Vertices[v1].Position;
                var p2 = meshData.Vertices[v2].Position;
                var edgePos = NormalizeEdgeWorldPos(p1, p2);

                if (IsSameEdgePosition(edgePos, edge1Pos))
                {
                    edge1LocalIdx = i;
                    if (Vector3.Distance(p1, edge1Pos.Item1) > POSITION_EPSILON)
                        edge1T = 1f - cutRatio1;
                }
                else if (IsSameEdgePosition(edgePos, edge2Pos))
                {
                    edge2LocalIdx = i;
                    if (Vector3.Distance(p1, edge2Pos.Item1) > POSITION_EPSILON)
                        edge2T = 1f - cutRatio2;
                }
            }

            if (edge1LocalIdx < 0 || edge2LocalIdx < 0) return;
            if (edge1LocalIdx == edge2LocalIdx) return;

            // Undo用スナップショット（切断前）
            MeshDataSnapshot beforeSnapshot = ctx.UndoController != null 
                ? MeshDataSnapshot.Capture(ctx.UndoController.MeshContext) 
                : null;

            var comparer = new EdgePositionComparer(POSITION_EPSILON);
            var cache = new Dictionary<(Vector3, Vector3), int>(comparer);
            var addedVertices = new List<(int Index, Vertex Vertex)>();

            int newVIdx1 = GetOrCreateEdgeVertexByPosition(ctx, edge1Pos, cache, addedVertices, cutRatio1);
            int newVIdx2 = GetOrCreateEdgeVertexByPosition(ctx, edge2Pos, cache, addedVertices, cutRatio2);

            var inter1 = new EdgeIntersection { EdgeStartIndex = edge1LocalIdx, EdgeEndIndex = (edge1LocalIdx + 1) % n, T = edge1T, WorldPos = meshData.Vertices[newVIdx1].Position };
            var inter2 = new EdgeIntersection { EdgeStartIndex = edge2LocalIdx, EdgeEndIndex = (edge2LocalIdx + 1) % n, T = edge2T, WorldPos = meshData.Vertices[newVIdx2].Position };

            var (face1, face2) = SplitFace(face, inter1, inter2, newVIdx1, newVIdx2);
            meshData.Faces[_targetFaceIndex] = face1;
            meshData.Faces.Add(face2);
            ctx.SyncMesh?.Invoke();

            // Undo記録
            if (ctx.UndoController != null && beforeSnapshot != null)
            {
                var afterSnapshot = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
                ctx.UndoController.RecordMeshTopologyChange(beforeSnapshot, afterSnapshot, "Knife Cut");
            }
        }

        private void ExecuteChainDragCut(ToolContext ctx)
        {
            if (_chainTargets.Count == 0) return;

            // Undo用スナップショット（切断前）
            MeshDataSnapshot beforeSnapshot = ctx.UndoController != null 
                ? MeshDataSnapshot.Capture(ctx.UndoController.MeshContext) 
                : null;

            var meshData = ctx.MeshData;
            var sortedTargets = _chainTargets.OrderByDescending(t => t.FaceIndex).ToList();

            foreach (var (faceIdx, intersections) in sortedTargets)
            {
                if (intersections.Count < 2) continue;
                if (faceIdx < 0 || faceIdx >= meshData.FaceCount) continue;

                var face = meshData.Faces[faceIdx];
                var inter0 = intersections[0];
                var inter1 = intersections[1];
                if (inter0.EdgeStartIndex == inter1.EdgeStartIndex) continue;

                int newVertexIdx0 = meshData.VertexCount;
                var newVertex0 = new Vertex(inter0.WorldPos);
                InterpolateVertexAttributes(meshData, face, inter0.EdgeStartIndex, inter0.EdgeEndIndex, inter0.T, newVertex0);
                meshData.Vertices.Add(newVertex0);

                int newVertexIdx1 = meshData.VertexCount;
                var newVertex1 = new Vertex(inter1.WorldPos);
                InterpolateVertexAttributes(meshData, face, inter1.EdgeStartIndex, inter1.EdgeEndIndex, inter1.T, newVertex1);
                meshData.Vertices.Add(newVertex1);

                var (face1, face2) = SplitFace(face, inter0, inter1, newVertexIdx0, newVertexIdx1);
                meshData.Faces[faceIdx] = face1;
                meshData.Faces.Add(face2);
            }

            ctx.SyncMesh?.Invoke();

            // Undo記録
            if (ctx.UndoController != null && beforeSnapshot != null)
            {
                var afterSnapshot = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
                ctx.UndoController.RecordMeshTopologyChange(beforeSnapshot, afterSnapshot, "Knife Chain Cut");
            }
        }

        // ================================================================
        // Chain描画
        // ================================================================

        private void DrawChainIntersections(ToolContext ctx)
        {
            UnityEditor_Handles.BeginGUI();
            foreach (var (faceIdx, intersections) in _chainTargets)
            {
                if (intersections.Count >= 2)
                {
                    UnityEditor_Handles.color = new Color(1f, 0.5f, 0f, 0.8f);
                    var sp1 = intersections[0].ScreenPos;
                    var sp2 = intersections[1].ScreenPos;
                    UnityEditor_Handles.DrawAAPolyLine(3f, new Vector3(sp1.x, sp1.y, 0), new Vector3(sp2.x, sp2.y, 0));
                }
            }
            UnityEditor_Handles.EndGUI();
        }

        private void DrawChainTargetFaces(ToolContext ctx)
        {
            UnityEditor_Handles.BeginGUI();
            UnityEditor_Handles.color = new Color(1f, 1f, 0f, 0.15f);
            foreach (var (faceIdx, _) in _chainTargets)
            {
                if (faceIdx < 0 || faceIdx >= ctx.MeshData.FaceCount) continue;
                var face = ctx.MeshData.Faces[faceIdx];
                var screenPoints = new Vector3[face.VertexCount];
                for (int i = 0; i < face.VertexCount; i++)
                {
                    var worldPos = ctx.MeshData.Vertices[face.VertexIndices[i]].Position;
                    var sp = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    screenPoints[i] = new Vector3(sp.x, sp.y, 0);
                }
                UnityEditor_Handles.DrawAAConvexPolygon(screenPoints);
            }
            UnityEditor_Handles.EndGUI();
        }
    }
}
