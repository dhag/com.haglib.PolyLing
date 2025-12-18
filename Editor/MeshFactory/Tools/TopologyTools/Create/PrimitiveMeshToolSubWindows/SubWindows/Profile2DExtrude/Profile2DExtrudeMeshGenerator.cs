// Assets/Editor/MeshCreators/Profile2DExtrude/Profile2DExtrudeMeshGenerator.cs
// 2D閉曲線押し出しメッシュ生成（Poly2Tri使用）

using System;
using System.Collections.Generic;
using UnityEngine;
using MeshFactory.Data;
using Poly2Tri;

namespace MeshFactory.Profile2DExtrude
{
    /// <summary>
    /// 2D押し出しメッシュ生成パラメータ（生成用）
    /// </summary>
    public struct Profile2DGenerateParams
    {
        public float Scale;
        public Vector2 Offset;
        public bool FlipY;
        public float Thickness;
        public int SegmentsFront, SegmentsBack;
        public float EdgeSizeFront, EdgeSizeBack;
        public bool EdgeInward;
    }

    /// <summary>
    /// 2D押し出しメッシュ生成
    /// </summary>
    public static class Profile2DExtrudeMeshGenerator
    {
        /// <summary>
        /// メッシュデータを生成
        /// </summary>
        public static MeshData Generate(List<Loop> loops, string meshName, Profile2DGenerateParams p)
        {
            if (loops == null || loops.Count == 0)
                return null;

            // 外側ループが存在するか確認
            bool hasOuter = false;
            foreach (var loop in loops)
            {
                if (!loop.IsHole && loop.Points.Count >= 3)
                {
                    hasOuter = true;
                    break;
                }
            }
            if (!hasOuter) return null;

            try
            {
                // 変換済み座標を保持
                var transformedLoops = new List<List<Vector2>>();
                var isHoleFlags = new List<bool>();

                foreach (var loop in loops)
                {
                    if (loop.Points.Count < 3) continue;

                    var transformed = new List<Vector2>();
                    foreach (var pt in loop.Points)
                    {
                        float x = pt.x * p.Scale + p.Offset.x;
                        float y = (p.FlipY ? -pt.y : pt.y) * p.Scale + p.Offset.y;
                        transformed.Add(new Vector2(x, y));
                    }
                    transformedLoops.Add(transformed);
                    isHoleFlags.Add(loop.IsHole);
                }

                var md = new MeshData(meshName);

                if (p.Thickness <= 0.001f)
                {
                    // 厚みなし：平面のみ
                    GenerateFlatFace(md, transformedLoops, isHoleFlags, 0f, Vector3.back, false);
                }
                else
                {
                    float halfThick = p.Thickness * 0.5f;

                    // 角処理適用した座標を計算
                    float frontOffset = p.SegmentsFront > 0 ? p.EdgeSizeFront : 0f;
                    float backOffset = p.SegmentsBack > 0 ? p.EdgeSizeBack : 0f;

                    var offsetFrontLoops = ApplyEdgeOffset(transformedLoops, isHoleFlags, frontOffset);
                    var offsetBackLoops = ApplyEdgeOffset(transformedLoops, isHoleFlags, backOffset);

                    if (p.EdgeInward)
                    {
                        // Outwardモード
                        GenerateFlatFace(md, offsetFrontLoops, isHoleFlags, -halfThick, Vector3.back, false);
                        GenerateFlatFace(md, offsetBackLoops, isHoleFlags, halfThick, Vector3.forward, true);
                        GenerateSideFacesOutward(md, transformedLoops, offsetFrontLoops, offsetBackLoops, isHoleFlags, halfThick, p);
                    }
                    else
                    {
                        // 通常モード
                        GenerateFlatFace(md, offsetFrontLoops, isHoleFlags, -halfThick, Vector3.back, false);
                        GenerateFlatFace(md, offsetBackLoops, isHoleFlags, halfThick, Vector3.forward, true);
                        GenerateSideFacesNormal(md, transformedLoops, offsetFrontLoops, offsetBackLoops, isHoleFlags, halfThick, p);
                    }
                }

                return md;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Poly2Tri triangulation failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 角処理のオフセットを適用
        /// </summary>
        private static List<List<Vector2>> ApplyEdgeOffset(List<List<Vector2>> loops, List<bool> isHoleFlags, float offset)
        {
            if (offset <= 0.001f)
                return loops;

            var result = new List<List<Vector2>>();

            for (int li = 0; li < loops.Count; li++)
            {
                var loop = loops[li];
                bool isHole = isHoleFlags[li];
                var newLoop = new List<Vector2>();

                for (int i = 0; i < loop.Count; i++)
                {
                    int prev = (i - 1 + loop.Count) % loop.Count;
                    int next = (i + 1) % loop.Count;

                    Vector2 p = loop[i];

                    Vector2 edge1 = (p - loop[prev]).normalized;
                    Vector2 edge2 = (loop[next] - p).normalized;

                    Vector2 normal1 = new Vector2(edge1.y, -edge1.x);
                    Vector2 normal2 = new Vector2(edge2.y, -edge2.x);

                    Vector2 avgNormal = (normal1 + normal2).normalized;

                    if (avgNormal.sqrMagnitude < 0.001f)
                    {
                        avgNormal = normal1;
                    }

                    float direction = isHole ? 1f : -1f;
                    Vector2 offsetVec = avgNormal * offset * direction;

                    newLoop.Add(p + offsetVec);
                }

                result.Add(newLoop);
            }

            return result;
        }

        /// <summary>
        /// 平面を生成（Poly2Tri使用）
        /// </summary>
        private static void GenerateFlatFace(MeshData md, List<List<Vector2>> loops, List<bool> isHoleFlags,
                                              float z, Vector3 normal, bool flipWinding)
        {
            // 外側ループを探す
            int outerIdx = -1;
            for (int i = 0; i < isHoleFlags.Count; i++)
            {
                if (!isHoleFlags[i])
                {
                    outerIdx = i;
                    break;
                }
            }
            if (outerIdx < 0) return;

            // Poly2Triは頂点が辺上にあるとエラーになるため、微小なオフセットを追加
            const float epsilon = 1e-5f;
            int seed = 12345;

            var outerPoints = new List<PolygonPoint>();
            foreach (var pt in loops[outerIdx])
            {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                float offsetX = ((seed % 1000) / 1000f - 0.5f) * epsilon;
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                float offsetY = ((seed % 1000) / 1000f - 0.5f) * epsilon;
                outerPoints.Add(new PolygonPoint(pt.x + offsetX, pt.y + offsetY));
            }

            var polygon = new Polygon(outerPoints);

            // 穴を追加
            for (int i = 0; i < loops.Count; i++)
            {
                if (!isHoleFlags[i]) continue;

                var holePoints = new List<PolygonPoint>();
                foreach (var pt in loops[i])
                {
                    seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                    float offsetX = ((seed % 1000) / 1000f - 0.5f) * epsilon;
                    seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                    float offsetY = ((seed % 1000) / 1000f - 0.5f) * epsilon;
                    holePoints.Add(new PolygonPoint(pt.x + offsetX, pt.y + offsetY));
                }
                polygon.AddHole(new Polygon(holePoints));
            }

            P2T.Triangulate(polygon);

            var vertexMap = new Dictionary<TriangulationPoint, int>();

            foreach (var tri in polygon.Triangles)
            {
                int[] indices = new int[3];
                for (int i = 0; i < 3; i++)
                {
                    TriangulationPoint p = tri.Points[i];
                    if (!vertexMap.TryGetValue(p, out int idx))
                    {
                        idx = md.VertexCount;
                        Vector3 pos = new Vector3((float)p.X, (float)p.Y, z);
                        Vector2 uv = new Vector2((float)p.X, (float)p.Y);
                        md.Vertices.Add(new Vertex(pos, uv, normal));
                        vertexMap[p] = idx;
                    }
                    indices[i] = idx;
                }

                if (flipWinding)
                    md.AddTriangle(indices[0], indices[1], indices[2]);
                else
                    md.AddTriangle(indices[0], indices[2], indices[1]);
            }
        }

        /// <summary>
        /// 側面を生成（Outwardモード）
        /// </summary>
        private static void GenerateSideFacesOutward(MeshData md, List<List<Vector2>> baseLoops,
                                                      List<List<Vector2>> offsetFrontLoops, List<List<Vector2>> offsetBackLoops,
                                                      List<bool> isHoleFlags, float halfThick, Profile2DGenerateParams p)
        {
            for (int li = 0; li < baseLoops.Count; li++)
            {
                var baseLoop = baseLoops[li];
                var offsetFront = offsetFrontLoops[li];
                var offsetBack = offsetBackLoops[li];
                bool isHole = isHoleFlags[li];

                int n = baseLoop.Count;

                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;

                    Vector2 edge = baseLoop[next] - baseLoop[i];
                    Vector3 sideNormal = new Vector3(edge.y, -edge.x, 0).normalized;

                    if (isHole)
                        sideNormal = -sideNormal;

                    // 表面の角処理部分
                    if (p.SegmentsFront > 0)
                    {
                        GenerateEdgeFaces(md,
                            offsetFront[i], offsetFront[next],
                            baseLoop[i], baseLoop[next],
                            -halfThick, -halfThick + p.EdgeSizeFront,
                            sideNormal, Vector3.back,
                            p.SegmentsFront, isHole, concave: false, isBackFace: false);
                    }

                    // メイン側面
                    {
                        float frontZ = p.SegmentsFront > 0 ? -halfThick + p.EdgeSizeFront : -halfThick;
                        float backZ = p.SegmentsBack > 0 ? halfThick - p.EdgeSizeBack : halfThick;

                        Vector2 frontPt0 = p.SegmentsFront > 0 ? baseLoop[i] : offsetFront[i];
                        Vector2 frontPt1 = p.SegmentsFront > 0 ? baseLoop[next] : offsetFront[next];
                        Vector2 backPt0 = p.SegmentsBack > 0 ? baseLoop[i] : offsetBack[i];
                        Vector2 backPt1 = p.SegmentsBack > 0 ? baseLoop[next] : offsetBack[next];

                        Vector3 v0 = new Vector3(frontPt0.x, frontPt0.y, frontZ);
                        Vector3 v1 = new Vector3(frontPt1.x, frontPt1.y, frontZ);
                        Vector3 v2 = new Vector3(backPt1.x, backPt1.y, backZ);
                        Vector3 v3 = new Vector3(backPt0.x, backPt0.y, backZ);

                        int idx = md.VertexCount;
                        md.Vertices.Add(new Vertex(v0, new Vector2(0, 0), sideNormal));
                        md.Vertices.Add(new Vertex(v1, new Vector2(1, 0), sideNormal));
                        md.Vertices.Add(new Vertex(v2, new Vector2(1, 1), sideNormal));
                        md.Vertices.Add(new Vertex(v3, new Vector2(0, 1), sideNormal));

                        if (isHole)
                            md.AddQuad(idx, idx + 3, idx + 2, idx + 1);
                        else
                            md.AddQuad(idx, idx + 1, idx + 2, idx + 3);
                    }

                    // 裏面の角処理部分
                    if (p.SegmentsBack > 0)
                    {
                        GenerateEdgeFaces(md,
                            baseLoop[i], baseLoop[next],
                            offsetBack[i], offsetBack[next],
                            halfThick - p.EdgeSizeBack, halfThick,
                            sideNormal, Vector3.forward,
                            p.SegmentsBack, isHole, concave: true, isBackFace: true);
                    }
                }
            }
        }

        /// <summary>
        /// 側面を生成（通常モード）
        /// </summary>
        private static void GenerateSideFacesNormal(MeshData md, List<List<Vector2>> baseLoops,
                                                     List<List<Vector2>> offsetFrontLoops, List<List<Vector2>> offsetBackLoops,
                                                     List<bool> isHoleFlags, float halfThick, Profile2DGenerateParams p)
        {
            for (int li = 0; li < baseLoops.Count; li++)
            {
                var baseLoop = baseLoops[li];
                var offsetFront = offsetFrontLoops[li];
                var offsetBack = offsetBackLoops[li];
                bool isHole = isHoleFlags[li];

                int n = baseLoop.Count;

                for (int i = 0; i < n; i++)
                {
                    int next = (i + 1) % n;

                    Vector2 edge = baseLoop[next] - baseLoop[i];
                    Vector3 sideNormal = new Vector3(edge.y, -edge.x, 0).normalized;

                    if (isHole)
                        sideNormal = -sideNormal;

                    // 表面の角処理部分
                    if (p.SegmentsFront > 0)
                    {
                        GenerateEdgeFaces(md,
                            offsetFront[i], offsetFront[next],
                            baseLoop[i], baseLoop[next],
                            -halfThick, -halfThick + p.EdgeSizeFront,
                            sideNormal, Vector3.back,
                            p.SegmentsFront, isHole, concave: true, isBackFace: false);
                    }

                    // メイン側面
                    {
                        float frontZ = p.SegmentsFront > 0 ? -halfThick + p.EdgeSizeFront : -halfThick;
                        float backZ = p.SegmentsBack > 0 ? halfThick - p.EdgeSizeBack : halfThick;

                        Vector2 frontPt0 = p.SegmentsFront > 0 ? baseLoop[i] : offsetFront[i];
                        Vector2 frontPt1 = p.SegmentsFront > 0 ? baseLoop[next] : offsetFront[next];
                        Vector2 backPt0 = p.SegmentsBack > 0 ? baseLoop[i] : offsetBack[i];
                        Vector2 backPt1 = p.SegmentsBack > 0 ? baseLoop[next] : offsetBack[next];

                        Vector3 v0 = new Vector3(frontPt0.x, frontPt0.y, frontZ);
                        Vector3 v1 = new Vector3(frontPt1.x, frontPt1.y, frontZ);
                        Vector3 v2 = new Vector3(backPt1.x, backPt1.y, backZ);
                        Vector3 v3 = new Vector3(backPt0.x, backPt0.y, backZ);

                        int idx = md.VertexCount;
                        md.Vertices.Add(new Vertex(v0, new Vector2(0, 0), sideNormal));
                        md.Vertices.Add(new Vertex(v1, new Vector2(1, 0), sideNormal));
                        md.Vertices.Add(new Vertex(v2, new Vector2(1, 1), sideNormal));
                        md.Vertices.Add(new Vertex(v3, new Vector2(0, 1), sideNormal));

                        if (isHole)
                            md.AddQuad(idx, idx + 3, idx + 2, idx + 1);
                        else
                            md.AddQuad(idx, idx + 1, idx + 2, idx + 3);
                    }

                    // 裏面の角処理部分
                    if (p.SegmentsBack > 0)
                    {
                        GenerateEdgeFaces(md,
                            baseLoop[i], baseLoop[next],
                            offsetBack[i], offsetBack[next],
                            halfThick - p.EdgeSizeBack, halfThick,
                            sideNormal, Vector3.forward,
                            p.SegmentsBack, isHole, concave: false, isBackFace: true);
                    }
                }
            }
        }

        /// <summary>
        /// 角処理の面を生成
        /// </summary>
        private static void GenerateEdgeFaces(MeshData md,
            Vector2 outer0, Vector2 outer1,
            Vector2 inner0, Vector2 inner1,
            float outerZ, float innerZ,
            Vector3 sideNormal,
            Vector3 faceNormal,
            int segments,
            bool isHole,
            bool concave = false,
            bool isBackFace = false)
        {
            if (segments == 1)
            {
                // ベベル
                Vector3 v0 = new Vector3(outer0.x, outer0.y, outerZ);
                Vector3 v1 = new Vector3(outer1.x, outer1.y, outerZ);
                Vector3 v2 = new Vector3(inner1.x, inner1.y, innerZ);
                Vector3 v3 = new Vector3(inner0.x, inner0.y, innerZ);

                Vector3 bevelNormal = (sideNormal + faceNormal).normalized;

                int idx = md.VertexCount;
                md.Vertices.Add(new Vertex(v0, new Vector2(0, 0), bevelNormal));
                md.Vertices.Add(new Vertex(v1, new Vector2(1, 0), bevelNormal));
                md.Vertices.Add(new Vertex(v2, new Vector2(1, 1), bevelNormal));
                md.Vertices.Add(new Vertex(v3, new Vector2(0, 1), bevelNormal));

                bool flipWinding = isHole;
                if (flipWinding)
                    md.AddQuad(idx, idx + 3, idx + 2, idx + 1);
                else
                    md.AddQuad(idx, idx + 1, idx + 2, idx + 3);
            }
            else
            {
                // ラウンド
                for (int s = 0; s < segments; s++)
                {
                    float t0 = (float)s / segments;
                    float t1 = (float)(s + 1) / segments;

                    float angle0 = t0 * Mathf.PI * 0.5f;
                    float angle1 = t1 * Mathf.PI * 0.5f;

                    float xyLerp0, xyLerp1, zLerp0, zLerp1;

                    if (concave)
                    {
                        xyLerp0 = Mathf.Sin(angle0);
                        xyLerp1 = Mathf.Sin(angle1);
                        zLerp0 = 1f - Mathf.Cos(angle0);
                        zLerp1 = 1f - Mathf.Cos(angle1);
                    }
                    else
                    {
                        xyLerp0 = 1f - Mathf.Cos(angle0);
                        xyLerp1 = 1f - Mathf.Cos(angle1);
                        zLerp0 = Mathf.Sin(angle0);
                        zLerp1 = Mathf.Sin(angle1);
                    }

                    Vector2 p0_0 = Vector2.Lerp(outer0, inner0, xyLerp0);
                    Vector2 p0_1 = Vector2.Lerp(outer1, inner1, xyLerp0);
                    Vector2 p1_0 = Vector2.Lerp(outer0, inner0, xyLerp1);
                    Vector2 p1_1 = Vector2.Lerp(outer1, inner1, xyLerp1);

                    float z0 = Mathf.Lerp(outerZ, innerZ, zLerp0);
                    float z1 = Mathf.Lerp(outerZ, innerZ, zLerp1);

                    Vector3 n0, n1;
                    if (isBackFace)
                    {
                        n0 = Vector3.Slerp(sideNormal, faceNormal, t0).normalized;
                        n1 = Vector3.Slerp(sideNormal, faceNormal, t1).normalized;
                    }
                    else
                    {
                        n0 = Vector3.Slerp(faceNormal, sideNormal, t0).normalized;
                        n1 = Vector3.Slerp(faceNormal, sideNormal, t1).normalized;
                    }

                    Vector3 v0 = new Vector3(p0_0.x, p0_0.y, z0);
                    Vector3 v1 = new Vector3(p0_1.x, p0_1.y, z0);
                    Vector3 v2 = new Vector3(p1_1.x, p1_1.y, z1);
                    Vector3 v3 = new Vector3(p1_0.x, p1_0.y, z1);

                    int idx = md.VertexCount;
                    md.Vertices.Add(new Vertex(v0, new Vector2(0, t0), n0));
                    md.Vertices.Add(new Vertex(v1, new Vector2(1, t0), n0));
                    md.Vertices.Add(new Vertex(v2, new Vector2(1, t1), n1));
                    md.Vertices.Add(new Vertex(v3, new Vector2(0, t1), n1));

                    bool flipWinding = isHole;
                    if (flipWinding)
                        md.AddQuad(idx, idx + 3, idx + 2, idx + 1);
                    else
                        md.AddQuad(idx, idx + 1, idx + 2, idx + 3);
                }
            }
        }
    }
}
