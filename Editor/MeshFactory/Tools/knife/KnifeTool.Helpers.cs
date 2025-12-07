// Tools/KnifeTool.Helpers.cs
// ナイフツール - ヘルパー関数

using System;
using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;

namespace MeshFactory.Tools
{
    public partial class KnifeTool
    {
        // ================================================================
        // 座標変換・数学
        // ================================================================

        /// <summary>
        /// 水平/垂直方向にスナップ
        /// </summary>
        private Vector2 SnapToAxis(Vector2 start, Vector2 current)
        {
            var delta = current - start;
            float angle = Mathf.Atan2(Mathf.Abs(delta.y), Mathf.Abs(delta.x)) * Mathf.Rad2Deg;
            float length = delta.magnitude;

            if (angle < SNAP_ANGLE_THRESHOLD)
            {
                return start + new Vector2(Mathf.Sign(delta.x) * length, 0);
            }
            else if (angle > 90 - SNAP_ANGLE_THRESHOLD)
            {
                return start + new Vector2(0, Mathf.Sign(delta.y) * length);
            }
            else
            {
                float diag = length / Mathf.Sqrt(2);
                return start + new Vector2(Mathf.Sign(delta.x) * diag, Mathf.Sign(delta.y) * diag);
            }
        }

        /// <summary>
        /// 点が多角形の内部にあるか判定（2D）
        /// </summary>
        private bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            if (polygon.Count < 3) return false;

            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; i++)
            {
                if ((polygon[i].y < point.y && polygon[j].y >= point.y ||
                     polygon[j].y < point.y && polygon[i].y >= point.y) &&
                    (polygon[i].x + (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) * (polygon[j].x - polygon[i].x) < point.x))
                {
                    inside = !inside;
                }
                j = i;
            }

            return inside;
        }

        /// <summary>
        /// 2つの線分の交差判定
        /// </summary>
        private bool LineSegmentIntersection(
            Vector2 p1, Vector2 p2,
            Vector2 p3, Vector2 p4,
            out float t1, out float t2)
        {
            t1 = 0;
            t2 = 0;

            float d = (p4.y - p3.y) * (p2.x - p1.x) - (p4.x - p3.x) * (p2.y - p1.y);

            if (Mathf.Abs(d) < 0.0001f)
                return false;

            float ua = ((p4.x - p3.x) * (p1.y - p3.y) - (p4.y - p3.y) * (p1.x - p3.x)) / d;
            float ub = ((p2.x - p1.x) * (p1.y - p3.y) - (p2.y - p1.y) * (p1.x - p3.x)) / d;

            t1 = ua;
            t2 = ub;

            return ua >= 0 && ua <= 1 && ub >= 0 && ub <= 1;
        }

        /// <summary>
        /// 直線（無限）と線分の交差判定
        /// </summary>
        private bool LineIntersectsSegment(
            Vector2 lineP1, Vector2 lineP2,
            Vector2 segP1, Vector2 segP2,
            out float t)
        {
            t = 0;

            float d = (segP2.y - segP1.y) * (lineP2.x - lineP1.x) - (segP2.x - segP1.x) * (lineP2.y - lineP1.y);

            if (Mathf.Abs(d) < 0.0001f)
                return false;

            float ub = ((lineP2.x - lineP1.x) * (lineP1.y - segP1.y) - (lineP2.y - lineP1.y) * (lineP1.x - segP1.x)) / d;

            t = ub;

            return ub >= 0 && ub <= 1;
        }

        /// <summary>
        /// 点から線分への距離
        /// </summary>
        private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float len = line.magnitude;
            if (len < 0.0001f) return Vector2.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (len * len));
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }

        // ================================================================
        // 辺の正規化・比較
        // ================================================================

        /// <summary>
        /// 辺を正規化（インデックスベース、小さい方が先）
        /// </summary>
        private (int, int) NormalizeEdge(int v1, int v2)
        {
            return v1 < v2 ? (v1, v2) : (v2, v1);
        }

        /// <summary>
        /// 辺のワールド座標を正規化（x→y→zで小さい方が先）
        /// </summary>
        private (Vector3, Vector3) NormalizeEdgeWorldPos(Vector3 p1, Vector3 p2)
        {
            if (p1.x < p2.x - POSITION_EPSILON) return (p1, p2);
            if (p1.x > p2.x + POSITION_EPSILON) return (p2, p1);
            if (p1.y < p2.y - POSITION_EPSILON) return (p1, p2);
            if (p1.y > p2.y + POSITION_EPSILON) return (p2, p1);
            if (p1.z < p2.z - POSITION_EPSILON) return (p1, p2);
            return (p2, p1);
        }

        /// <summary>
        /// 2つの辺位置が同じかどうか判定
        /// </summary>
        private bool IsSameEdgePosition((Vector3, Vector3) a, (Vector3, Vector3) b)
        {
            bool match1 = Vector3.Distance(a.Item1, b.Item1) < POSITION_EPSILON &&
                          Vector3.Distance(a.Item2, b.Item2) < POSITION_EPSILON;
            bool match2 = Vector3.Distance(a.Item1, b.Item2) < POSITION_EPSILON &&
                          Vector3.Distance(a.Item2, b.Item1) < POSITION_EPSILON;
            return match1 || match2;
        }

        /// <summary>
        /// 辺位置比較用クラス
        /// </summary>
        private class EdgePositionComparer : IEqualityComparer<(Vector3, Vector3)>
        {
            private readonly float _epsilon;

            public EdgePositionComparer(float epsilon = 0.0001f)
            {
                _epsilon = epsilon;
            }

            public bool Equals((Vector3, Vector3) a, (Vector3, Vector3) b)
            {
                bool match1 = Vector3.Distance(a.Item1, b.Item1) < _epsilon &&
                              Vector3.Distance(a.Item2, b.Item2) < _epsilon;
                bool match2 = Vector3.Distance(a.Item1, b.Item2) < _epsilon &&
                              Vector3.Distance(a.Item2, b.Item1) < _epsilon;
                return match1 || match2;
            }

            public int GetHashCode((Vector3, Vector3) obj)
            {
                int Round(float v) => Mathf.RoundToInt(v / _epsilon);
                var p1 = (Round(obj.Item1.x), Round(obj.Item1.y), Round(obj.Item1.z));
                var p2 = (Round(obj.Item2.x), Round(obj.Item2.y), Round(obj.Item2.z));
                return p1.GetHashCode() ^ p2.GetHashCode();
            }
        }

        // ================================================================
        // 辺・面検索
        // ================================================================

        /// <summary>
        /// 指定位置付近の全ての辺を取得
        /// </summary>
        private List<(int faceIndex, int edgeLocalIdx, (Vector3, Vector3) worldPos)> FindEdgesNearPosition(
            ToolContext ctx, Vector2 mousePos, float threshold)
        {
            var result = new List<(int, int, (Vector3, Vector3))>();

            for (int faceIdx = 0; faceIdx < ctx.MeshData.FaceCount; faceIdx++)
            {
                var face = ctx.MeshData.Faces[faceIdx];
                int n = face.VertexIndices.Count;

                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    var p1 = ctx.MeshData.Vertices[v1].Position;
                    var p2 = ctx.MeshData.Vertices[v2].Position;
                    var sp1 = ctx.WorldToScreenPos(p1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    var sp2 = ctx.WorldToScreenPos(p2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                    float dist = DistanceToLineSegment(mousePos, sp1, sp2);
                    if (dist < threshold)
                    {
                        var worldPos = NormalizeEdgeWorldPos(p1, p2);
                        result.Add((faceIdx, i, worldPos));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// マウス位置に最も近い辺のワールド座標を取得
        /// </summary>
        private (Vector3, Vector3)? FindNearestEdgeWorldPos(ToolContext ctx, Vector2 mousePos, float threshold)
        {
            var edges = FindEdgesNearPosition(ctx, mousePos, threshold);
            if (edges.Count == 0) return null;

            float bestDist = float.MaxValue;
            (Vector3, Vector3)? bestPos = null;

            foreach (var (faceIdx, localIdx, worldPos) in edges)
            {
                var face = ctx.MeshData.Faces[faceIdx];
                int v1 = face.VertexIndices[localIdx];
                int v2 = face.VertexIndices[(localIdx + 1) % face.VertexCount];

                var p1 = ctx.MeshData.Vertices[v1].Position;
                var p2 = ctx.MeshData.Vertices[v2].Position;
                var sp1 = ctx.WorldToScreenPos(p1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                var sp2 = ctx.WorldToScreenPos(p2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                float dist = DistanceToLineSegment(mousePos, sp1, sp2);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPos = worldPos;
                }
            }

            return bestPos;
        }

        /// <summary>
        /// 指定したワールド座標の辺を含む全ての面を取得
        /// </summary>
        private List<(int faceIndex, int edgeLocalIdx)> FindFacesWithEdgePosition(
            ToolContext ctx, (Vector3, Vector3) edgeWorldPos)
        {
            var result = new List<(int, int)>();

            for (int faceIdx = 0; faceIdx < ctx.MeshData.FaceCount; faceIdx++)
            {
                var face = ctx.MeshData.Faces[faceIdx];
                int n = face.VertexIndices.Count;

                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    var p1 = ctx.MeshData.Vertices[v1].Position;
                    var p2 = ctx.MeshData.Vertices[v2].Position;
                    var worldPos = NormalizeEdgeWorldPos(p1, p2);

                    if (IsSameEdgePosition(worldPos, edgeWorldPos))
                    {
                        result.Add((faceIdx, i));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 2つの辺を両方含む面のインデックスを取得
        /// </summary>
        private int FindFaceWithBothEdges(ToolContext ctx, (Vector3, Vector3) edge1Pos, (Vector3, Vector3) edge2Pos)
        {
            var meshData = ctx.MeshData;

            for (int faceIdx = 0; faceIdx < meshData.FaceCount; faceIdx++)
            {
                var face = meshData.Faces[faceIdx];
                int n = face.VertexIndices.Count;
                bool hasEdge1 = false, hasEdge2 = false;

                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];
                    var p1 = meshData.Vertices[v1].Position;
                    var p2 = meshData.Vertices[v2].Position;
                    var edgePos = NormalizeEdgeWorldPos(p1, p2);

                    if (IsSameEdgePosition(edgePos, edge1Pos)) hasEdge1 = true;
                    if (IsSameEdgePosition(edgePos, edge2Pos)) hasEdge2 = true;
                }

                if (hasEdge1 && hasEdge2) return faceIdx;
            }

            return -1;
        }

        /// <summary>
        /// 辺から面へのマップを構築（インデックスベース）
        /// </summary>
        private Dictionary<(int, int), List<int>> BuildEdgeToFacesMap(MeshData meshData)
        {
            var map = new Dictionary<(int, int), List<int>>();

            for (int faceIdx = 0; faceIdx < meshData.FaceCount; faceIdx++)
            {
                var face = meshData.Faces[faceIdx];
                int n = face.VertexIndices.Count;

                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];
                    var key = NormalizeEdge(v1, v2);

                    if (!map.ContainsKey(key))
                        map[key] = new List<int>();

                    map[key].Add(faceIdx);
                }
            }

            return map;
        }

        /// <summary>
        /// 四角形面で指定辺の対向辺を見つける
        /// </summary>
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

        // ================================================================
        // 切断比率計算
        // ================================================================

        /// <summary>
        /// 辺上でマウス位置に最も近い点のパラメータt（0-1）を計算
        /// </summary>
        private float CalculateCutRatioOnEdge(ToolContext ctx, Vector2 mousePos, (Vector3, Vector3) edgeWorldPos)
        {
            var sp1 = ctx.WorldToScreenPos(edgeWorldPos.Item1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            var sp2 = ctx.WorldToScreenPos(edgeWorldPos.Item2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            Vector2 edge = sp2 - sp1;
            float edgeLenSq = edge.sqrMagnitude;

            if (edgeLenSq < 0.0001f) return 0.5f;

            float t = Vector2.Dot(mousePos - sp1, edge) / edgeLenSq;
            return Mathf.Clamp(t, 0.05f, 0.95f);
        }

        /// <summary>
        /// Cutモードでの切断比率を取得
        /// </summary>
        private float GetEdgeCutRatio(ToolContext ctx, Vector2 mousePos, (Vector3, Vector3) edgeWorldPos)
        {
            return _edgeBisectMode ? _cutRatio : CalculateCutRatioOnEdge(ctx, mousePos, edgeWorldPos);
        }

        /// <summary>
        /// Vertexモードでの切断比率を取得
        /// </summary>
        private float GetVertexCutRatio(ToolContext ctx, Vector2 mousePos, (Vector3, Vector3) edgeWorldPos)
        {
            return _vertexBisectMode ? 0.5f : CalculateCutRatioOnEdge(ctx, mousePos, edgeWorldPos);
        }

        // ================================================================
        // 描画ヘルパー
        // ================================================================

        /// <summary>
        /// ワールド座標の辺を描画
        /// </summary>
        private void DrawEdgeByWorldPos(ToolContext ctx, (Vector3, Vector3) edgePos)
        {
            var sp1 = ctx.WorldToScreenPos(edgePos.Item1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            var sp2 = ctx.WorldToScreenPos(edgePos.Item2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            UnityEditor.Handles.DrawAAPolyLine(4f, new Vector3(sp1.x, sp1.y, 0), new Vector3(sp2.x, sp2.y, 0));
        }
    }
}
