// Assets/Editor/MeshCreators/Profile2DExtrude/Profile2DExtrudeMeshGenerator.cs
// 2D閉曲線押し出しメッシュ生成（Poly2Tri使用）

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly2Tri;

namespace Poly_Ling.Profile2DExtrude
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
        public bool SymmetryMode;
    }

    /// <summary>
    /// 2D押し出しメッシュ生成
    /// </summary>
    public static class Profile2DExtrudeMeshGenerator
    {
        /// <summary>
        /// メッシュデータを生成
        /// </summary>
        public static MeshObject Generate(List<Loop> loops, string meshName, Profile2DGenerateParams p)
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

                // 左右対称モード：x<=0 を x=0 へスナップし、軸上runをy整列＋同y結合
                List<bool[]> onAxisFlags = null;
                if (p.SymmetryMode)
                {
                    ApplySymmetry(transformedLoops, isHoleFlags, out onAxisFlags);
                    bool hasOuterAfter = false;
                    for (int i = 0; i < transformedLoops.Count; i++)
                        if (!isHoleFlags[i] && transformedLoops[i].Count >= 3) { hasOuterAfter = true; break; }
                    if (!hasOuterAfter) return null;
                }

                var md = new MeshObject(meshName);

                if (p.Thickness <= 0.001f)
                {
                    // 厚みなし：平面のみ
                    GenerateFlatFaceReindexed(md, transformedLoops, transformedLoops, isHoleFlags, 0f, Vector3.back, false);
                }
                else
                {
                    float halfThick = p.Thickness * 0.5f;

                    // 角処理適用した座標を計算
                    float frontOffset = p.SegmentsFront > 0 ? p.EdgeSizeFront : 0f;
                    float backOffset = p.SegmentsBack > 0 ? p.EdgeSizeBack : 0f;

                    // ベベルで潰れる小突起を base からも除去（扇状の折れ対策）
                    float maxOff = Mathf.Max(frontOffset, backOffset);
                    if (!p.SymmetryMode && maxOff > 0.001f)
                        transformedLoops = SimplifyBaseForBevel(transformedLoops, isHoleFlags, maxOff);

                    var offsetFrontLoops = ApplyEdgeOffset(transformedLoops, isHoleFlags, frontOffset);
                    var offsetBackLoops = ApplyEdgeOffset(transformedLoops, isHoleFlags, backOffset);

                    // 対称モード：軸上頂点のオフセット像XYをbase(x=0,同y)へ固定（ベベルでXY不変、Zスライスは現行のまま）
                    if (p.SymmetryMode && onAxisFlags != null)
                        PinAxisVertices(transformedLoops, offsetFrontLoops, offsetBackLoops, onAxisFlags);

                    if (p.EdgeInward)
                    {
                        // Outwardモード
                        GenerateFlatFaceReindexed(md, transformedLoops, offsetFrontLoops, isHoleFlags, -halfThick, Vector3.back, false);
                        GenerateFlatFaceReindexed(md, transformedLoops, offsetBackLoops, isHoleFlags, halfThick, Vector3.forward, true);
                        GenerateSideFacesOutward(md, transformedLoops, offsetFrontLoops, offsetBackLoops, isHoleFlags, halfThick, p);
                    }
                    else
                    {
                        // 通常モード
                        GenerateFlatFaceReindexed(md, transformedLoops, offsetFrontLoops, isHoleFlags, -halfThick, Vector3.back, false);
                        GenerateFlatFaceReindexed(md, transformedLoops, offsetBackLoops, isHoleFlags, halfThick, Vector3.forward, true);
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
        // 2D直線交点。p1+t*d1 と p2+s*d2 の交点。平行なら false。
        private static bool LineIntersect(Vector2 p1, Vector2 d1, Vector2 p2, Vector2 d2, out Vector2 hit)
        {
            float den = d1.x * (-d2.y) - d1.y * (-d2.x);
            if (Mathf.Abs(den) < 1e-9f) { hit = default; return false; }
            Vector2 r = p2 - p1;
            float t = (r.x * (-d2.y) - r.y * (-d2.x)) / den;
            hit = p1 + d1 * t;
            return true;
        }

        private static int Uf_Find(int[] parent, int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        private static void Uf_Union(int[] parent, int a, int b)
        {
            parent[Uf_Find(parent, a)] = Uf_Find(parent, b);
        }

        // 反転辺（元辺方向に対しオフセット辺が逆向き）を検出し、連結した反転頂点群を重心1点へ潰す。
        // 反転が無くなるまで反復（上限あり）。頂点数は不変（潰れた点は重複頂点として残る）。
        private static void CollapseReversedRuns(Vector2[] v, Vector2[] edir, int n)
        {
            var parent = new int[n];
            for (int iter = 0; iter < 64; iter++)
            {
                for (int i = 0; i < n; i++) parent[i] = i;

                bool any = false;
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    if (Vector2.Dot(edir[i], v[j] - v[i]) < -1e-9f)
                    {
                        any = true;
                        Uf_Union(parent, i, j);
                    }
                }
                if (!any) break;

                var sum = new Vector2[n];
                var cnt = new int[n];
                for (int i = 0; i < n; i++)
                {
                    int r = Uf_Find(parent, i);
                    sum[r] += v[i];
                    cnt[r]++;
                }
                for (int i = 0; i < n; i++)
                {
                    int r = Uf_Find(parent, i);
                    if (cnt[r] >= 2) v[i] = sum[r] / cnt[r];
                }
            }
        }

        // インセット頂点が元輪郭の外／境界近傍(margin未満)に出た場合、最近傍境界点から内側へ margin だけ入れる。
        // reflex 頂点のミターが外へはみ出して生じるスパイクを抑える（外周のみに適用する想定）。
        private static void ClampInsetInside(Vector2[] v, List<Vector2> baseLoop, int n, float margin)
        {
            int m = baseLoop.Count;
            if (m < 3) return;

            Vector2 cen = Vector2.zero;
            for (int i = 0; i < m; i++) cen += baseLoop[i];
            cen /= m;

            for (int k = 0; k < n; k++)
            {
                float best = float.MaxValue;
                Vector2 bestPt = v[k];
                Vector2 bestIn = Vector2.zero;
                for (int i = 0; i < m; i++)
                {
                    Vector2 a = baseLoop[i];
                    Vector2 b = baseLoop[(i + 1) % m];
                    Vector2 ab = b - a;
                    float L2 = Mathf.Max(Vector2.Dot(ab, ab), 1e-12f);
                    float t = Mathf.Clamp01(Vector2.Dot(v[k] - a, ab) / L2);
                    Vector2 pr = a + ab * t;
                    float d = (v[k] - pr).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        bestPt = pr;
                        Vector2 e = ab.normalized;
                        Vector2 inn = new Vector2(e.y, -e.x);
                        Vector2 mid = (a + b) * 0.5f;
                        if (Vector2.Dot(inn, cen - mid) < 0f) inn = -inn;
                        bestIn = inn;
                    }
                }
                bool outside = !PointInPolygon(v[k], baseLoop);
                if (outside || Mathf.Sqrt(best) < margin)
                    v[k] = bestPt + bestIn * margin;
            }
        }

        // ミター内側オフセット＋反転区間の潰し（clampなし）。頂点数は入力と一致。
        private static Vector2[] MiterCollapse(List<Vector2> loop, bool isHole, float offset)
        {
            int n = loop.Count;
            var v = new Vector2[n];
            if (n < 3) { for (int i = 0; i < n; i++) v[i] = loop[i]; return v; }

            float dir = isHole ? 1f : -1f;
            var edir = new Vector2[n];
            var nrm  = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 e = (loop[(i + 1) % n] - loop[i]).normalized;
                edir[i] = e;
                nrm[i]  = new Vector2(e.y, -e.x);
            }
            for (int i = 0; i < n; i++)
            {
                int pe = (i - 1 + n) % n;
                Vector2 ap = loop[i] + nrm[pe] * offset * dir;
                Vector2 bp = loop[i] + nrm[i]  * offset * dir;
                if (edir[pe].sqrMagnitude < 1e-12f || edir[i].sqrMagnitude < 1e-12f
                    || !LineIntersect(ap, edir[pe], bp, edir[i], out Vector2 hit))
                    hit = bp;   // 平行・退化辺はフォールバック
                v[i] = hit;
            }
            CollapseReversedRuns(v, edir, n);
            return v;
        }

        // ベベルで潰れる（ミター後に同座標へ collapse する）小突起を、base 輪郭からも1点へマージする。
        // collapse が消えるまで反復（上限8）。外周のみ対象、穴はそのまま。ベベル幅より小さい凸は除去される。
        private static List<List<Vector2>> SimplifyBaseForBevel(List<List<Vector2>> loops, List<bool> isHoleFlags, float offset)
        {
            if (offset <= 0.001f) return loops;

            var result = new List<List<Vector2>>(loops.Count);
            for (int li = 0; li < loops.Count; li++)
            {
                var loop = loops[li];
                if (isHoleFlags[li] || loop == null || loop.Count < 3) { result.Add(loop); continue; }

                var cur = new List<Vector2>(loop);
                for (int iter = 0; iter < 8; iter++)
                {
                    int n = cur.Count;
                    if (n < 3) break;
                    var v = MiterCollapse(cur, false, offset);

                    // 同座標に潰れた群（collapse で全く同じ座標を共有する）を検出
                    var groups = new Dictionary<Vector2, List<int>>();
                    for (int i = 0; i < n; i++)
                    {
                        if (!groups.TryGetValue(v[i], out var lst)) { lst = new List<int>(); groups[v[i]] = lst; }
                        lst.Add(i);
                    }

                    var drop = new bool[n];
                    var rep  = new Dictionary<int, Vector2>();
                    bool any = false;
                    foreach (var kv in groups)
                    {
                        if (kv.Value.Count < 2) continue;
                        any = true;
                        Vector2 c = Vector2.zero;
                        foreach (int idx in kv.Value) c += cur[idx];
                        c /= kv.Value.Count;
                        int keep = kv.Value[0];
                        rep[keep] = c;
                        for (int t = 1; t < kv.Value.Count; t++) drop[kv.Value[t]] = true;
                    }
                    if (!any) break;

                    var next = new List<Vector2>(n);
                    for (int i = 0; i < n; i++)
                    {
                        if (drop[i]) continue;
                        next.Add(rep.TryGetValue(i, out var rp) ? rp : cur[i]);
                    }
                    if (next.Count < 3) break;   // 潰しすぎ防止
                    cur = next;
                }
                result.Add(cur);
            }
            return result;
        }

        // 多角形の内側オフセット（インセット）。
        // 各頂点を「隣接2辺を内側へ offset 平行移動した直線の交点（ミター）」に置く＝各辺を一様な垂直距離で内側へ。
        // offset が短辺長を超えて辺が反転する箇所は、連結した反転区間を1点へ潰す（頂点数は base と一致）。
        private static List<List<Vector2>> ApplyEdgeOffset(List<List<Vector2>> loops, List<bool> isHoleFlags, float offset)
        {
            if (offset <= 0.001f)
                return loops;

            var result = new List<List<Vector2>>();

            for (int li = 0; li < loops.Count; li++)
            {
                var loop = loops[li];
                int n = loop.Count;
                if (n < 3) { result.Add(loop); continue; }

                var v = MiterCollapse(loop, isHoleFlags[li], offset);

                // 外周のみ: 外へはみ出した/境界近傍のインセット頂点を内側へ引き戻す（スパイク対策）
                if (!isHoleFlags[li])
                    ClampInsetInside(v, loop, n, 0.25f * offset);

                var newLoop = new List<Vector2>(n);
                for (int i = 0; i < n; i++) newLoop.Add(v[i]);
                result.Add(newLoop);
            }

            return result;
        }

        /// <summary>
        /// 平面を生成（Poly2Tri使用）
        /// </summary>
        // 元輪郭 topoLoops で三角化し、その位相を用いて posLoops の位置で平面を生成する。
        // 自己交差し得るインセット(posLoops)を Poly2Tri に渡さないため、Head/Tail 混入が起きない。
        // topoLoops と posLoops は同一の頂点数・対応 [li][pi] でなければならない。
        private static void GenerateFlatFaceReindexed(
            MeshObject md, List<List<Vector2>> topoLoops, List<List<Vector2>> posLoops,
            List<bool> isHoleFlags, float z, Vector3 normal, bool flipWinding)
        {
            var outers = new List<int>();
            var holes  = new List<int>();
            for (int i = 0; i < topoLoops.Count; i++)
            {
                if (topoLoops[i] == null || topoLoops[i].Count < 3) continue;
                if (isHoleFlags[i]) holes.Add(i); else outers.Add(i);
            }
            if (outers.Count == 0) return;

            var holesByOuter = new Dictionary<int, List<int>>();
            foreach (int hi in holes)
            {
                Vector2 hp0 = topoLoops[hi][0];
                int owner = -1;
                foreach (int oi in outers)
                    if (PointInPolygon(hp0, topoLoops[oi])) { owner = oi; break; }
                if (owner < 0) owner = outers[0];
                if (!holesByOuter.TryGetValue(owner, out var lst)) { lst = new List<int>(); holesByOuter[owner] = lst; }
                lst.Add(hi);
            }

            // Poly2Triは頂点が辺上にあるとエラーになるため、三角化入力にのみ微小オフセットを加える
            const float epsilon = 1e-5f;
            int seed = 12345;

            foreach (int oi in outers)
            {
                // PolygonPoint -> (loopIdx, ptIdx)。位相を元頂点に戻すため。
                var pmap = new Dictionary<TriangulationPoint, long>();

                var outerPoints = new List<PolygonPoint>();
                var src = topoLoops[oi];
                for (int pi = 0; pi < src.Count; pi++)
                {
                    seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                    float ox = ((seed % 1000) / 1000f - 0.5f) * epsilon;
                    seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                    float oy = ((seed % 1000) / 1000f - 0.5f) * epsilon;
                    var pp = new PolygonPoint(src[pi].x + ox, src[pi].y + oy);
                    pmap[pp] = ((long)oi << 32) | (uint)pi;
                    outerPoints.Add(pp);
                }
                var polygon = new Polygon(outerPoints);

                if (holesByOuter.TryGetValue(oi, out var hlist))
                {
                    foreach (int hi in hlist)
                    {
                        var hsrc = topoLoops[hi];
                        if (hsrc.Count < 3) continue;
                        var holePoints = new List<PolygonPoint>();
                        for (int pi = 0; pi < hsrc.Count; pi++)
                        {
                            seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                            float ox = ((seed % 1000) / 1000f - 0.5f) * epsilon;
                            seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                            float oy = ((seed % 1000) / 1000f - 0.5f) * epsilon;
                            var pp = new PolygonPoint(hsrc[pi].x + ox, hsrc[pi].y + oy);
                            pmap[pp] = ((long)hi << 32) | (uint)pi;
                            holePoints.Add(pp);
                        }
                        polygon.AddHole(new Polygon(holePoints));
                    }
                }

                try { P2T.Triangulate(polygon); }
                catch (Exception ex) { Debug.LogWarning($"Poly2Tri failed for a contour: {ex.Message}"); continue; }

                // (li,pi) -> md頂点index（このフタ内で共有）。posLoops の位置で生成する。
                var vertexMap = new Dictionary<long, int>();
                foreach (var tri in polygon.Triangles)
                {
                    int[] indices = new int[3];
                    bool ok = true;
                    for (int i = 0; i < 3; i++)
                    {
                        if (!pmap.TryGetValue(tri.Points[i], out long key)) { ok = false; break; } // Head/Tail等は除外
                        if (!vertexMap.TryGetValue(key, out int idx))
                        {
                            int li = (int)(key >> 32);
                            int pi = (int)(key & 0xffffffff);
                            Vector2 p2 = posLoops[li][pi];
                            idx = md.VertexCount;
                            md.Vertices.Add(new Vertex(new Vector3(p2.x, p2.y, z), new Vector2(p2.x, p2.y), normal));
                            vertexMap[key] = idx;
                        }
                        indices[i] = idx;
                    }
                    if (!ok) continue;

                    if (flipWinding)
                        md.AddTriangle(indices[0], indices[1], indices[2]);
                    else
                        md.AddTriangle(indices[0], indices[2], indices[1]);
                }
            }
        }

        /// <summary>点がポリゴン内部か（even-odd）。</summary>
        private static bool PointInPolygon(Vector2 pt, List<Vector2> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((poly[i].y > pt.y) != (poly[j].y > pt.y)) &&
                    (pt.x < (poly[j].x - poly[i].x) * (pt.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// 側面を生成（Outwardモード）
        /// </summary>
        private static void GenerateSideFacesOutward(MeshObject md, List<List<Vector2>> baseLoops,
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
        private static void GenerateSideFacesNormal(MeshObject md, List<List<Vector2>> baseLoops,
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
        private static void GenerateEdgeFaces(MeshObject md,
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

        // 左右対称モード前処理。Y軸交差を認識して軸上seamを作る。
        //  Type A（軸上頂点）: |x|<=tol かつ最近傍非ゼロ符号が前後で正負 → (0, 自身のy)。
        //  Type B（対称対）  : 符号反転辺（両端|x|>tol）→ 負側端点を (0, 正側y) へ。
        //  非交差の x<=0 頂点 : (0, clamp(y, Ymin, Ymax))。Ymin/Ymax は交差軸Yの最小/最大。
        // その後 非軸点を先頭へ回転→軸run を y整列→同y結合。全点 x<=0（右側消失）のループは破棄。
        // 交差が無く右側のみのループは無変換。分割ケース（軸を複数回跨ぐ形状）は想定外。
        // loops/isHoleFlags は in-place で更新。onAxisFlags は残存ループ各頂点の軸上フラグ。
        private static void ApplySymmetry(
            List<List<Vector2>> loops, List<bool> isHoleFlags, out List<bool[]> onAxisFlags)
        {
            const float mergeTol = 1e-4f;
            onAxisFlags = new List<bool[]>();

            for (int li = loops.Count - 1; li >= 0; li--)
            {
                var srcLoop = loops[li];
                int n = srcLoop.Count;

                // --- 符号判定（tol はX最大絶対値の2%、最低1e-4） ---
                float maxAbsX = 0f;
                for (int i = 0; i < n; i++) maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(srcLoop[i].x));
                float tol = Mathf.Max(1e-4f, 0.02f * maxAbsX);

                var sign = new int[n];
                for (int i = 0; i < n; i++)
                {
                    float x = srcLoop[i].x;
                    sign[i] = x > tol ? 1 : (x < -tol ? -1 : 0);
                }

                // 各頂点の前後最近傍の非ゼロ符号インデックス（循環）
                var prevNZ = new int[n];
                var nextNZ = new int[n];
                int lastNZ = -1;
                for (int c = 0; c < 2 * n; c++) { int i = c % n; if (sign[i] != 0) lastNZ = i; if (c >= n) prevNZ[i] = lastNZ; }
                int firstNZ = -1;
                for (int c = 2 * n - 1; c >= 0; c--) { int i = c % n; if (sign[i] != 0) firstNZ = i; if (c < n) nextNZ[i] = firstNZ; }

                // role: 0=右側保持 / 1=TypeA / 2=TypeB負側端点 / 3=非交差の左側
                var role = new int[n];
                var axisY = new float[n];
                bool hasCross = false;
                float yMin = float.MaxValue, yMax = float.MinValue;

                // Type A：軸上頂点で最近傍非ゼロ符号が前後で正負に分かれる
                for (int i = 0; i < n; i++)
                {
                    if (sign[i] != 0 || prevNZ[i] < 0 || nextNZ[i] < 0) continue;
                    if (sign[prevNZ[i]] * sign[nextNZ[i]] < 0)
                    {
                        role[i] = 1; axisY[i] = srcLoop[i].y;
                        hasCross = true;
                        yMin = Mathf.Min(yMin, axisY[i]); yMax = Mathf.Max(yMax, axisY[i]);
                    }
                }

                // Type B：符号反転辺（両端が正負）→ 負側端点を (0, 正側y) へ
                for (int i = 0; i < n; i++)
                {
                    int q = (i + 1) % n;
                    if (sign[i] * sign[q] < 0)
                    {
                        int neg = sign[i] < 0 ? i : q;
                        int pos = sign[i] < 0 ? q : i;
                        role[neg] = 2; axisY[neg] = srcLoop[pos].y;
                        hasCross = true;
                        yMin = Mathf.Min(yMin, axisY[neg]); yMax = Mathf.Max(yMax, axisY[neg]);
                    }
                }

                // 交差なし：右側があれば無変換（seamなし）／全て左なら破棄
                if (!hasCross)
                {
                    bool hasPos = false;
                    for (int i = 0; i < n; i++) if (srcLoop[i].x > 0f) { hasPos = true; break; }
                    if (!hasPos)
                    {
                        loops.RemoveAt(li); isHoleFlags.RemoveAt(li); continue;
                    }
                    onAxisFlags.Insert(0, new bool[n]);   // 全 false
                    continue;
                }

                // 頂点構築：交差点＝軸Yへ、非交差x<=0＝Ymin..Ymaxへクリップ、右側＝保持
                var pts = new List<Vector2>(n);
                var flag = new List<bool>(n);
                int axisCount = 0;
                for (int i = 0; i < n; i++)
                {
                    if (role[i] == 1 || role[i] == 2)
                    {
                        pts.Add(new Vector2(0f, axisY[i])); flag.Add(true); axisCount++;
                    }
                    else if (srcLoop[i].x <= 0f)
                    {
                        pts.Add(new Vector2(0f, Mathf.Clamp(srcLoop[i].y, yMin, yMax))); flag.Add(true); axisCount++;
                    }
                    else
                    {
                        pts.Add(srcLoop[i]); flag.Add(false);
                    }
                }

                // 全点が軸上（右側消失）＝線分物体 → 破棄
                if (axisCount == n)
                {
                    loops.RemoveAt(li);
                    isHoleFlags.RemoveAt(li);
                    continue;
                }

                // 軸上区間が配列端を跨がないよう、非軸点を先頭へ回転
                int rot = flag.FindIndex(f => !f);   // axisCount<n なので必ず存在
                if (rot > 0)
                {
                    var rp = new List<Vector2>(n);
                    var rf = new List<bool>(n);
                    for (int i = 0; i < n; i++) { rp.Add(pts[(i + rot) % n]); rf.Add(flag[(i + rot) % n]); }
                    pts = rp; flag = rf;
                }

                // 連続する軸上区間ごとに y整列＋同y結合
                var outPts = new List<Vector2>(n);
                var outFlag = new List<bool>(n);
                int k = 0;
                while (k < n)
                {
                    if (!flag[k]) { outPts.Add(pts[k]); outFlag.Add(false); k++; continue; }

                    int j = k;
                    while (j < n && flag[j]) j++;    // 区間 [k, j-1]、直前 k-1 は非軸

                    float prevY = pts[k - 1].y;                 // k>=1（先頭非軸を保証）
                    float nextY = (j < n) ? pts[j].y : pts[0].y;

                    var seg = new List<Vector2>(j - k);
                    for (int t = k; t < j; t++) seg.Add(pts[t]);
                    seg.Sort((a, b) => a.y.CompareTo(b.y));
                    if (prevY > nextY) seg.Reverse();

                    for (int t = 0; t < seg.Count; t++)
                    {
                        // 直前に確定した軸点と同y（xは共に0）なら結合してスキップ
                        if (outFlag.Count > 0 && outFlag[outFlag.Count - 1] &&
                            Mathf.Abs(outPts[outPts.Count - 1].y - seg[t].y) < mergeTol)
                            continue;
                        outPts.Add(seg[t]);
                        outFlag.Add(true);
                    }
                    k = j;
                }

                loops[li] = outPts;
                onAxisFlags.Insert(0, outFlag.ToArray());
            }
        }

        // 軸上頂点のオフセット像の X を 0 に固定し、Y（ミター結果）は保持する。
        //  seam内部辺（両隣が軸）は法線+xのみ・y成分ゼロなので y=base.y のまま＝ベベルされず平坦。
        //  交差端点は正側辺のミターy成分が残るため、正側リンクが正しくベベルされる。
        private static void PinAxisVertices(
            List<List<Vector2>> baseLoops,
            List<List<Vector2>> offFront, List<List<Vector2>> offBack,
            List<bool[]> onAxisFlags)
        {
            for (int li = 0; li < baseLoops.Count; li++)
            {
                if (li >= onAxisFlags.Count) break;
                var flags = onAxisFlags[li];
                int n = baseLoops[li].Count;
                for (int i = 0; i < n; i++)
                {
                    if (i >= flags.Length || !flags[i]) continue;
                    if (offFront[li] != null && i < offFront[li].Count) offFront[li][i] = new Vector2(0f, offFront[li][i].y);
                    if (offBack[li] != null && i < offBack[li].Count) offBack[li][i] = new Vector2(0f, offBack[li][i].y);
                }
            }
        }
    }
}
