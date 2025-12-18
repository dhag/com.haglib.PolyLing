// Tools/KnifeTool.Core.cs
// ナイフツール - コア機能（面検出、交差点計算、面分割）

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using static MeshFactory.Gizmo.GLGizmoDrawer;

namespace MeshFactory.Tools
{
    public partial class KnifeTool
    {
        // ================================================================
        // 面検出
        // ================================================================

        /// <summary>
        /// 指定位置で最前面の面を検出
        /// </summary>
        private int FindFrontmostFaceAtPosition(ToolContext ctx, Vector2 screenPos)
        {
            int bestFaceIndex = -1;
            float bestDepth = float.MaxValue;

            for (int faceIdx = 0; faceIdx < ctx.MeshData.FaceCount; faceIdx++)
            {
                var face = ctx.MeshData.Faces[faceIdx];
                if (face.VertexCount < 3) continue;

                var screenPoints = new List<Vector2>();
                float avgDepth = 0;

                foreach (var vIdx in face.VertexIndices)
                {
                    var worldPos = ctx.MeshData.Vertices[vIdx].Position;
                    var sp = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    screenPoints.Add(sp);
                    avgDepth += Vector3.Distance(worldPos, ctx.CameraPosition);
                }
                avgDepth /= face.VertexCount;

                if (IsPointInPolygon(screenPos, screenPoints))
                {
                    if (avgDepth < bestDepth)
                    {
                        bestDepth = avgDepth;
                        bestFaceIndex = faceIdx;
                    }
                }
            }

            return bestFaceIndex;
        }

        /// <summary>
        /// ドラッグライン上で最前面の面を検出
        /// </summary>
        private int FindFrontmostFaceOnLine(ToolContext ctx)
        {
            int faceIdx = FindFrontmostFaceAtPosition(ctx, _startScreenPos);
            if (faceIdx >= 0) return faceIdx;

            int samples = 10;
            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                var samplePos = Vector2.Lerp(_startScreenPos, _currentScreenPos, t);
                faceIdx = FindFrontmostFaceAtPosition(ctx, samplePos);
                if (faceIdx >= 0) return faceIdx;
            }

            return -1;
        }

        // ================================================================
        // 交差点計算
        // ================================================================

        /// <summary>
        /// 交差点を更新
        /// </summary>
        private void UpdateIntersections(ToolContext ctx)
        {
            _intersections.Clear();
            _chainTargets.Clear();

            if (ctx.MeshData == null || ctx.MeshData.FaceCount == 0) return;

            if (ChainMode && Mode == KnifeMode.Cut)
            {
                for (int faceIdx = 0; faceIdx < ctx.MeshData.FaceCount; faceIdx++)
                {
                    var face = ctx.MeshData.Faces[faceIdx];
                    if (face.VertexCount < 3) continue;

                    var intersections = FindIntersectionsForFace(ctx, face);

                    if (intersections.Count >= 2)
                    {
                        var sorted = intersections
                            .OrderBy(x => Vector2.Distance(_startScreenPos, x.ScreenPos))
                            .Take(2)
                            .ToList();

                        _chainTargets.Add((faceIdx, sorted));
                    }
                }

                if (_chainTargets.Count > 0)
                {
                    var best = _chainTargets
                        .OrderBy(t => t.Intersections.Min(x => Vector2.Distance(_startScreenPos, x.ScreenPos)))
                        .First();
                    _targetFaceIndex = best.FaceIndex;
                    _intersections = best.Intersections;
                }
                else
                {
                    _targetFaceIndex = -1;
                }
            }
            else
            {
                int bestFaceIndex = -1;
                List<EdgeIntersection> bestIntersections = null;
                float bestMinDistance = float.MaxValue;

                for (int faceIdx = 0; faceIdx < ctx.MeshData.FaceCount; faceIdx++)
                {
                    var face = ctx.MeshData.Faces[faceIdx];
                    if (face.VertexCount < 3) continue;

                    var intersections = FindIntersectionsForFace(ctx, face);

                    if (intersections.Count >= 2)
                    {
                        float minDist = intersections.Min(x => Vector2.Distance(_startScreenPos, x.ScreenPos));

                        if (minDist < bestMinDistance)
                        {
                            bestMinDistance = minDist;
                            bestFaceIndex = faceIdx;
                            bestIntersections = intersections;
                        }
                    }
                }

                if (bestFaceIndex >= 0 && bestIntersections != null)
                {
                    _targetFaceIndex = bestFaceIndex;
                    _intersections = bestIntersections
                        .OrderBy(x => Vector2.Distance(_startScreenPos, x.ScreenPos))
                        .Take(2)
                        .ToList();
                }
                else
                {
                    _targetFaceIndex = -1;
                }
            }
        }

        /// <summary>
        /// 指定した面とワイヤーの交差点を計算
        /// </summary>
        private List<EdgeIntersection> FindIntersectionsForFace(ToolContext ctx, Face face)
        {
            var intersections = new List<EdgeIntersection>();

            for (int i = 0; i < face.VertexCount; i++)
            {
                int nextI = (i + 1) % face.VertexCount;

                int vIdx0 = face.VertexIndices[i];
                int vIdx1 = face.VertexIndices[nextI];

                var p0 = ctx.MeshData.Vertices[vIdx0].Position;
                var p1 = ctx.MeshData.Vertices[vIdx1].Position;

                var sp0 = ctx.WorldToScreenPos(p0, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                var sp1 = ctx.WorldToScreenPos(p1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                if (LineSegmentIntersection(
                    _startScreenPos, _currentScreenPos,
                    sp0, sp1,
                    out float t1, out float t2))
                {
                    if (t2 > 0.0001f && t2 < 0.9999f)
                    {
                        var worldPos = Vector3.Lerp(p0, p1, t2);
                        var screenPos = Vector2.Lerp(sp0, sp1, t2);

                        intersections.Add(new EdgeIntersection
                        {
                            EdgeStartIndex = i,
                            EdgeEndIndex = nextI,
                            T = t2,
                            WorldPos = worldPos,
                            ScreenPos = screenPos
                        });
                    }
                }
            }

            return intersections;
        }

        /// <summary>
        /// 辺上の点のT値を計算
        /// </summary>
        private float CalculateTOnEdge(ToolContext ctx, (Vector3, Vector3) edgePos, Vector3 point)
        {
            var edgeDir = edgePos.Item2 - edgePos.Item1;
            float edgeLength = edgeDir.magnitude;
            if (edgeLength < POSITION_EPSILON) return 0.5f;

            var toPoint = point - edgePos.Item1;
            float t = Vector3.Dot(toPoint, edgeDir) / (edgeLength * edgeLength);
            return Mathf.Clamp01(t);
        }

        // ================================================================
        // 面分割
        // ================================================================

        /// <summary>
        /// 切断を実行（単一面）
        /// </summary>
        private void ExecuteCut(ToolContext ctx)
        {
            if (_intersections.Count < 2) return;
            if (_targetFaceIndex < 0 || _targetFaceIndex >= ctx.MeshData.FaceCount) return;

            var face = ctx.MeshData.Faces[_targetFaceIndex];
            var inter0 = _intersections[0];
            var inter1 = _intersections[1];

            if (inter0.EdgeStartIndex == inter1.EdgeStartIndex) return;

            var originalFace = face.Clone();
            var addedVertices = new List<(int Index, Vertex Vertex)>();

            int newVertexIdx0 = ctx.MeshData.VertexCount;
            var newVertex0 = new Vertex(inter0.WorldPos);
            InterpolateVertexAttributes(ctx.MeshData, face, inter0.EdgeStartIndex, inter0.EdgeEndIndex, inter0.T, newVertex0);
            ctx.MeshData.Vertices.Add(newVertex0);
            addedVertices.Add((newVertexIdx0, newVertex0.Clone()));

            int newVertexIdx1 = ctx.MeshData.VertexCount;
            var newVertex1 = new Vertex(inter1.WorldPos);
            InterpolateVertexAttributes(ctx.MeshData, face, inter1.EdgeStartIndex, inter1.EdgeEndIndex, inter1.T, newVertex1);
            ctx.MeshData.Vertices.Add(newVertex1);
            addedVertices.Add((newVertexIdx1, newVertex1.Clone()));

            var (face1, face2) = SplitFace(face, inter0, inter1, newVertexIdx0, newVertexIdx1);

            ctx.MeshData.Faces[_targetFaceIndex] = face1;
            int newFaceIdx = ctx.MeshData.FaceCount;
            ctx.MeshData.Faces.Add(face2);

            ctx.UndoController?.RecordKnifeCut(
                _targetFaceIndex,
                originalFace,
                face1.Clone(),
                newFaceIdx,
                face2.Clone(),
                addedVertices
            );

            ctx.SyncMesh?.Invoke();
        }

        /// <summary>
        /// 面を2つに分割
        /// </summary>
        private (Face face1, Face face2) SplitFace(
            Face originalFace,
            EdgeIntersection inter0,
            EdgeIntersection inter1,
            int newVertexIdx0,
            int newVertexIdx1)
        {
            var verts = originalFace.VertexIndices;
            var uvs = originalFace.UVIndices;
            var normals = originalFace.NormalIndices;
            int n = verts.Count;

            int edge0Start = inter0.EdgeStartIndex;
            int edge1Start = inter1.EdgeStartIndex;

            if (edge0Start > edge1Start)
            {
                (edge0Start, edge1Start) = (edge1Start, edge0Start);
                (newVertexIdx0, newVertexIdx1) = (newVertexIdx1, newVertexIdx0);
            }

            var face1Verts = new List<int>();
            var face1UVs = new List<int>();
            var face1Normals = new List<int>();

            face1Verts.Add(newVertexIdx0);
            face1UVs.Add(0);
            face1Normals.Add(0);

            for (int i = edge0Start + 1; i <= edge1Start; i++)
            {
                face1Verts.Add(verts[i]);
                face1UVs.Add(uvs.Count > i ? uvs[i] : 0);
                face1Normals.Add(normals.Count > i ? normals[i] : 0);
            }

            face1Verts.Add(newVertexIdx1);
            face1UVs.Add(0);
            face1Normals.Add(0);

            var face2Verts = new List<int>();
            var face2UVs = new List<int>();
            var face2Normals = new List<int>();

            face2Verts.Add(newVertexIdx1);
            face2UVs.Add(0);
            face2Normals.Add(0);

            for (int i = edge1Start + 1; i < n; i++)
            {
                face2Verts.Add(verts[i]);
                face2UVs.Add(uvs.Count > i ? uvs[i] : 0);
                face2Normals.Add(normals.Count > i ? normals[i] : 0);
            }
            for (int i = 0; i <= edge0Start; i++)
            {
                face2Verts.Add(verts[i]);
                face2UVs.Add(uvs.Count > i ? uvs[i] : 0);
                face2Normals.Add(normals.Count > i ? normals[i] : 0);
            }

            face2Verts.Add(newVertexIdx0);
            face2UVs.Add(0);
            face2Normals.Add(0);

            var face1 = new Face
            {
                VertexIndices = face1Verts,
                UVIndices = face1UVs,
                NormalIndices = face1Normals
            };

            var face2 = new Face
            {
                VertexIndices = face2Verts,
                UVIndices = face2UVs,
                NormalIndices = face2Normals
            };

            return (face1, face2);
        }

        /// <summary>
        /// 頂点属性を補間
        /// </summary>
        private void InterpolateVertexAttributes(
            MeshData meshData,
            Face face,
            int localIdx0,
            int localIdx1,
            float t,
            Vertex targetVertex)
        {
            int vIdx0 = face.VertexIndices[localIdx0];
            int vIdx1 = face.VertexIndices[localIdx1];

            var v0 = meshData.Vertices[vIdx0];
            var v1 = meshData.Vertices[vIdx1];

            if (v0.UVs.Count > 0 && v1.UVs.Count > 0)
            {
                int uvIdx0 = face.UVIndices.Count > localIdx0 ? face.UVIndices[localIdx0] : 0;
                int uvIdx1 = face.UVIndices.Count > localIdx1 ? face.UVIndices[localIdx1] : 0;

                if (uvIdx0 < v0.UVs.Count && uvIdx1 < v1.UVs.Count)
                {
                    var uv = Vector2.Lerp(v0.UVs[uvIdx0], v1.UVs[uvIdx1], t);
                    targetVertex.UVs.Add(uv);
                }
            }

            if (v0.Normals.Count > 0 && v1.Normals.Count > 0)
            {
                int nIdx0 = face.NormalIndices.Count > localIdx0 ? face.NormalIndices[localIdx0] : 0;
                int nIdx1 = face.NormalIndices.Count > localIdx1 ? face.NormalIndices[localIdx1] : 0;

                if (nIdx0 < v0.Normals.Count && nIdx1 < v1.Normals.Count)
                {
                    var normal = Vector3.Lerp(v0.Normals[nIdx0], v1.Normals[nIdx1], t).normalized;
                    targetVertex.Normals.Add(normal);
                }
            }
        }

        // ================================================================
        // 共通描画
        // ================================================================

        /// <summary>
        /// ワイヤーを描画
        /// </summary>
        private void DrawWire(ToolContext ctx)
        {
            UnityEditor_Handles.color = new Color(1f, 0.5f, 0f, 0.8f);
            var start = new Vector3(_startScreenPos.x, _startScreenPos.y, 0);
            var end = new Vector3(_currentScreenPos.x, _currentScreenPos.y, 0);
            UnityEditor_Handles.DrawLine(start, end, 2f);
        }

        /// <summary>
        /// 交差点を描画（線のみ）
        /// </summary>
        private void DrawIntersections(ToolContext ctx)
        {
            if (_intersections.Count < 2) return;

            UnityEditor_Handles.color = Color.green;
            var p0 = new Vector3(_intersections[0].ScreenPos.x, _intersections[0].ScreenPos.y, 0);
            var p1 = new Vector3(_intersections[1].ScreenPos.x, _intersections[1].ScreenPos.y, 0);
            UnityEditor_Handles.DrawLine(p0, p1, 3f);
        }

        /// <summary>
        /// ターゲット面をハイライト
        /// </summary>
        private void DrawTargetFaceHighlight(ToolContext ctx)
        {
            if (_targetFaceIndex < 0 || _targetFaceIndex >= ctx.MeshData.FaceCount) return;

            var face = ctx.MeshData.Faces[_targetFaceIndex];
            if (face.VertexCount < 3) return;

            UnityEditor_Handles.color = new Color(0f, 1f, 1f, 0.3f);

            var points = new Vector3[face.VertexCount + 1];
            for (int i = 0; i < face.VertexCount; i++)
            {
                var worldPos = ctx.MeshData.Vertices[face.VertexIndices[i]].Position;
                var screenPos = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                points[i] = new Vector3(screenPos.x, screenPos.y, 0);
            }
            points[face.VertexCount] = points[0];

            UnityEditor_Handles.DrawAAPolyLine(3f, points);
        }
    }
}
