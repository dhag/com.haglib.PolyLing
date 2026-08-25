// GearDiskBuilder.cs
// 閉じた 2D 輪郭を厚み方向へ押し出し、中心に丸穴を開けられる共有ビルダー。
// 簡易歯車 / スタア / インボリュートトロコイド歯車の 3 図形が共用する。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【入力の規約】
//   outline は XY 平面の閉じた単純多角形。終点に始点を重ねないこと。
//   原点まわりに CCW（符号付き面積が正）であること。
//   角度の単調増加は要求しない。インボリュート歯車の歯元トロコイドは、
//   切り下げが起きると接続点の手前でわずかに角度が逆行する。
//
// 【フタ】
//   Poly2Tri で三角化する。穴があるときは穴ループを hole として渡す。
//   三角化に失敗したときだけ、輪郭の各点を穴円へ半径方向に投影する四角形帯へ退避する
//   （この退避経路は角度が単調な形状でしか正しくならないため、あくまで保険）。
//
// 【面の向き】
//   XY 平面で組み、前面（+Z 側）の法線を +Z にする。PolyLing の正面ビューは +Z 側なので、
//   生成直後の見た目が正面ビューの表になる。
//   外周壁は外向き、穴壁は軸へ向く（穴の内側から見える）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly2Tri;

namespace Poly_Ling.PrimitiveMesh
{
    public static class GearDiskBuilder
    {
        /// <summary>
        /// 外周壁を滑らかにつなぐ角度のしきい値（度）。
        /// 隣り合う辺の法線がこれより開いていれば、その角で頂点を分けて折り目にする。
        /// 歯先の角は折り目、インボリュート曲線部は滑らかになる。
        /// </summary>
        private const float WallSmoothAngleDeg = 40f;

        /// <summary>穴リングの分割数の下限・上限。</summary>
        public const int BoreSegmentsMin = 6;
        public const int BoreSegmentsMax = 256;

        /// <summary>
        /// 閉じた 2D 輪郭から押し出しメッシュを作る。
        /// </summary>
        /// <param name="meshName">メッシュ名</param>
        /// <param name="outline">XY 平面の閉じた輪郭（CCW）。終点に始点を重複させないこと。</param>
        /// <param name="thickness">厚み。0 のときは前面 1 枚だけの板になる。</param>
        /// <param name="boreRadius">中心の丸穴半径。0 以下で穴なし。</param>
        /// <param name="boreSegments">穴リングの分割数。</param>
        /// <param name="orientation">板を置く平面。XY / XZ / YZ。</param>
        /// <param name="flipFaces">生成後に全面を裏返す。</param>
        /// <param name="pivot">AABB サイズ基準のピボット。</param>
        public static MeshObject Build(
            string meshName,
            IReadOnlyList<Vector2> outline,
            float thickness,
            float boreRadius,
            int boreSegments,
            PlaneOrientation orientation,
            bool flipFaces,
            Vector3 pivot)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(meshName) ? "GearDisk" : meshName);

            int n = outline?.Count ?? 0;
            if (n < 3) return mo;

            float half = Mathf.Max(0f, thickness) * 0.5f;
            bool solid = half > 1e-6f;

            // 厚み 0 は前面 1 枚だけの板にする。前後を重ねると同じ位置に面が二重に載る。
            float zF = solid ? +half : 0f;
            float zB = solid ? -half : 0f;

            // 穴半径は「原点から輪郭の辺までの最短距離」未満に抑える。
            // 頂点までの距離で測ると、谷が直線で結ばれている形（簡易歯車の歯底など）で
            // 辺の中ほどが穴の内側へ入り込み、穴が外形を突き抜ける。
            float maxR = 0f;
            for (int i = 0; i < n; i++)
                maxR = Mathf.Max(maxR, outline[i].magnitude);

            float minEdgeDist = MinDistanceToOutline(outline, n);

            float bore = Mathf.Max(0f, boreRadius);
            if (bore > 0f && bore >= minEdgeDist) bore = minEdgeDist * 0.99f;
            bool hasBore = bore > 1e-6f;

            float uvScale = maxR > 1e-6f ? 0.5f / maxR : 1f;

            // ------------------------------------------------------------
            // 穴リングとフタの三角形分割
            // ------------------------------------------------------------

            Vector2[] boreRing = null;
            if (hasBore)
            {
                int m = Mathf.Clamp(boreSegments, BoreSegmentsMin, BoreSegmentsMax);
                boreRing = new Vector2[m];
                for (int i = 0; i < m; i++)
                {
                    float a = i * 2f * Mathf.PI / m;
                    boreRing[i] = Polar(bore, a);
                }
            }

            List<int> capTris = TryTriangulateCap(outline, boreRing);

            if (capTris == null && hasBore)
            {
                // 退避経路：穴リングを輪郭の角度へ合わせ直し、四角形帯で塞ぐ。
                boreRing = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    float a = Mathf.Atan2(outline[i].y, outline[i].x);
                    boreRing[i] = Polar(bore, a);
                }
            }

            int boreCount = boreRing?.Length ?? 0;

            // ------------------------------------------------------------
            // フタの頂点
            // ------------------------------------------------------------

            int fCap = mo.VertexCount;
            AddCapRingVertices(mo, outline, boreRing, zF, Vector3.forward, uvScale);

            int bCap = solid ? mo.VertexCount : -1;
            if (solid)
                AddCapRingVertices(mo, outline, boreRing, zB, Vector3.back, uvScale);

            int fCenter = -1, bCenter = -1;
            if (!hasBore && capTris == null)
            {
                fCenter = mo.VertexCount;
                mo.Vertices.Add(new Vertex(new Vector3(0f, 0f, zF), new Vector2(0.5f, 0.5f), Vector3.forward));

                if (solid)
                {
                    bCenter = mo.VertexCount;
                    mo.Vertices.Add(new Vertex(new Vector3(0f, 0f, zB), new Vector2(0.5f, 0.5f), Vector3.back));
                }
            }

            // ------------------------------------------------------------
            // フタの面
            // ------------------------------------------------------------

            if (capTris != null)
            {
                for (int t = 0; t + 2 < capTris.Count; t += 3)
                {
                    int a = capTris[t], b = capTris[t + 1], c = capTris[t + 2];

                    // Poly2Tri の返す巻き順は保証しないので、符号付き面積で CCW へ揃える。
                    if (SignedArea(CapPoint(outline, boreRing, a),
                                   CapPoint(outline, boreRing, b),
                                   CapPoint(outline, boreRing, c)) < 0f)
                    {
                        int tmp = b; b = c; c = tmp;
                    }

                    // CCW = 前面（+Z）。背面はその逆。
                    mo.AddTriangle(fCap + a, fCap + b, fCap + c);
                    if (solid)
                        mo.AddTriangle(bCap + a, bCap + c, bCap + b);
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;

                    if (hasBore)
                    {
                        // 輪郭 2 点の角度が同じだと穴リングの 2 点が重なる。そのときは三角形にする。
                        bool degenerate = (boreRing[i] - boreRing[j]).sqrMagnitude <= 1e-12f;

                        if (degenerate)
                        {
                            mo.AddTriangle(fCap + i, fCap + j, fCap + n + i);
                            if (solid) mo.AddTriangle(bCap + i, bCap + n + i, bCap + j);
                        }
                        else
                        {
                            mo.AddQuad(fCap + i, fCap + j, fCap + n + j, fCap + n + i);
                            if (solid) mo.AddQuad(bCap + i, bCap + n + i, bCap + n + j, bCap + j);
                        }
                    }
                    else
                    {
                        mo.AddTriangle(fCenter, fCap + i, fCap + j);
                        if (solid) mo.AddTriangle(bCenter, bCap + j, bCap + i);
                    }
                }
            }

            // ------------------------------------------------------------
            // 壁（厚みがあるときだけ）
            // ------------------------------------------------------------

            if (solid)
            {
                BuildOuterWall(mo, outline, n, zF, zB);

                if (hasBore)
                    BuildBoreWall(mo, boreRing, boreCount, zF, zB, bore);
            }

            // ------------------------------------------------------------
            // 後処理
            // ------------------------------------------------------------

            ApplyOrientation(mo, orientation);

            if (flipFaces)
                PrimitiveMeshPostProcess.FlipFaces(mo);

            PrimitiveMeshPostProcess.ApplyPivotOffset(mo, pivot);
            PrimitiveMeshPostProcess.SortVerticesCanonical(mo);

            mo.InvalidatePositionCache();
            return mo;
        }

        // ================================================================
        // フタ
        // ================================================================

        /// <summary>フタ用の統合インデックス（0..n-1 = 輪郭、n 以降 = 穴リング）から点を引く。</summary>
        private static Vector2 CapPoint(IReadOnlyList<Vector2> outline, Vector2[] boreRing, int index)
            => index < outline.Count ? outline[index] : boreRing[index - outline.Count];

        /// <summary>原点から輪郭の各辺までの最短距離。</summary>
        private static float MinDistanceToOutline(IReadOnlyList<Vector2> outline, int n)
        {
            float min = float.MaxValue;

            for (int i = 0; i < n; i++)
            {
                Vector2 a = outline[i];
                Vector2 b = outline[(i + 1) % n];
                Vector2 ab = b - a;

                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-12f
                    ? Mathf.Clamp(-(a.x * ab.x + a.y * ab.y) / len2, 0f, 1f)
                    : 0f;

                float d = (a + ab * t).magnitude;
                if (d < min) min = d;
            }

            return min == float.MaxValue ? 0f : min;
        }

        /// <summary>三角形の符号付き面積（CCW で正）。</summary>
        private static float SignedArea(Vector2 a, Vector2 b, Vector2 c)
            => 0.5f * ((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));

        /// <summary>輪郭リング＋穴リングの順で、片面ぶんのフタ頂点を並べる。</summary>
        private static void AddCapRingVertices(
            MeshObject mo, IReadOnlyList<Vector2> outline, Vector2[] boreRing,
            float z, Vector3 normal, float uvScale)
        {
            for (int i = 0; i < outline.Count; i++)
                mo.Vertices.Add(new Vertex(
                    new Vector3(outline[i].x, outline[i].y, z),
                    PlanarUV(outline[i], uvScale),
                    normal));

            if (boreRing == null) return;

            for (int i = 0; i < boreRing.Length; i++)
                mo.Vertices.Add(new Vertex(
                    new Vector3(boreRing[i].x, boreRing[i].y, z),
                    PlanarUV(boreRing[i], uvScale),
                    normal));
        }

        /// <summary>
        /// フタを Poly2Tri で三角化し、統合インデックスの三角形列を返す。失敗したら null。
        /// </summary>
        private static List<int> TryTriangulateCap(IReadOnlyList<Vector2> outline, Vector2[] boreRing)
        {
            // Poly2Tri は頂点が辺上に載るとエラーになるため、三角化の入力にだけ微小オフセットを乗せる。
            // 面を張る位置は元の座標を使う（Profile2DExtrudeMeshGenerator と同じ手口）。
            int seed = 12345;

            try
            {
                var pmap = new Dictionary<TriangulationPoint, int>();

                var outerPoints = new List<PolygonPoint>(outline.Count);
                for (int i = 0; i < outline.Count; i++)
                {
                    var pp = new PolygonPoint(
                        outline[i].x + Jitter(ref seed),
                        outline[i].y + Jitter(ref seed));
                    pmap[pp] = i;
                    outerPoints.Add(pp);
                }

                var polygon = new Polygon(outerPoints);

                if (boreRing != null && boreRing.Length >= 3)
                {
                    var holePoints = new List<PolygonPoint>(boreRing.Length);
                    for (int i = 0; i < boreRing.Length; i++)
                    {
                        var pp = new PolygonPoint(
                            boreRing[i].x + Jitter(ref seed),
                            boreRing[i].y + Jitter(ref seed));
                        pmap[pp] = outline.Count + i;
                        holePoints.Add(pp);
                    }
                    polygon.AddHole(new Polygon(holePoints));
                }

                P2T.Triangulate(polygon);

                if (polygon.Triangles == null) return null;

                var tris = new List<int>(polygon.Triangles.Count * 3);
                foreach (var tri in polygon.Triangles)
                {
                    // Head / Tail など、入力に無い点を含む三角形は捨てる。
                    if (!pmap.TryGetValue(tri.Points[0], out int a)) continue;
                    if (!pmap.TryGetValue(tri.Points[1], out int b)) continue;
                    if (!pmap.TryGetValue(tri.Points[2], out int c)) continue;

                    tris.Add(a); tris.Add(b); tris.Add(c);
                }

                return tris.Count >= 3 ? tris : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"GearDiskBuilder: フタの三角化に失敗しました（半径方向の四角形帯へ退避します）: {ex.Message}");
                return null;
            }
        }

        /// <summary>三角化入力へ乗せる微小オフセット。線形合同法で決定的に散らす。</summary>
        private static float Jitter(ref int seed)
        {
            const float epsilon = 1e-6f;
            seed = (seed * 1103515245 + 12345) & 0x7fffffff;
            return ((seed % 1000) / 1000f - 0.5f) * epsilon;
        }

        // ================================================================
        // 外周壁
        // ================================================================

        /// <summary>
        /// 外周壁を張る。角が鋭いところは頂点を分けて折り目にし、緩いところは法線を平均して滑らかにつなぐ。
        /// </summary>
        private static void BuildOuterWall(
            MeshObject mo, IReadOnlyList<Vector2> outline, int n, float zF, float zB)
        {
            // 辺ごとの外向き法線（CCW 輪郭では (dy, -dx)）と長さ。
            var edgeN = new Vector2[n];
            var edgeLen = new float[n];
            float total = 0f;

            for (int e = 0; e < n; e++)
            {
                Vector2 d = outline[(e + 1) % n] - outline[e];
                float len = d.magnitude;
                edgeLen[e] = len;
                total += len;
                edgeN[e] = len > 1e-9f
                    ? new Vector2(d.y / len, -d.x / len)
                    : outline[e].normalized;
            }

            // 各辺の始点までの累積長（U 座標用）。
            var uAt = new float[n];
            float acc = 0f;
            for (int e = 0; e < n; e++)
            {
                uAt[e] = total > 1e-9f ? acc / total : 0f;
                acc += edgeLen[e];
            }

            float cosLimit = Mathf.Cos(WallSmoothAngleDeg * Mathf.Deg2Rad);

            // 辺 e の始点側 / 終点側が使う頂点。
            var fStart = new int[n];
            var fEnd = new int[n];
            var bStart = new int[n];
            var bEnd = new int[n];

            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                Vector2 p = outline[i];

                bool smooth = Vector2.Dot(edgeN[prev], edgeN[i]) >= cosLimit;

                if (smooth)
                {
                    Vector2 nn = (edgeN[prev] + edgeN[i]).normalized;
                    if (nn.sqrMagnitude < 1e-12f) nn = edgeN[i];

                    var nrm = new Vector3(nn.x, nn.y, 0f);
                    float u = uAt[i];

                    int vf = mo.VertexCount;
                    mo.Vertices.Add(new Vertex(new Vector3(p.x, p.y, zF), new Vector2(u, 1f), nrm));
                    int vb = mo.VertexCount;
                    mo.Vertices.Add(new Vertex(new Vector3(p.x, p.y, zB), new Vector2(u, 0f), nrm));

                    fEnd[prev] = vf; fStart[i] = vf;
                    bEnd[prev] = vb; bStart[i] = vb;
                }
                else
                {
                    // 折り目：直前の辺の終点と、この辺の始点を別々の頂点にする。
                    var nPrev = new Vector3(edgeN[prev].x, edgeN[prev].y, 0f);
                    var nCur = new Vector3(edgeN[i].x, edgeN[i].y, 0f);

                    // 折り目では頂点が分かれるので、輪の終端側は U=1 に伸ばして継ぎ目を作らない。
                    float uPrevEnd = (i == 0) ? 1f : uAt[i];
                    float uCurStart = uAt[i];

                    int vfPrev = mo.VertexCount;
                    mo.Vertices.Add(new Vertex(new Vector3(p.x, p.y, zF), new Vector2(uPrevEnd, 1f), nPrev));
                    int vbPrev = mo.VertexCount;
                    mo.Vertices.Add(new Vertex(new Vector3(p.x, p.y, zB), new Vector2(uPrevEnd, 0f), nPrev));

                    int vfCur = mo.VertexCount;
                    mo.Vertices.Add(new Vertex(new Vector3(p.x, p.y, zF), new Vector2(uCurStart, 1f), nCur));
                    int vbCur = mo.VertexCount;
                    mo.Vertices.Add(new Vertex(new Vector3(p.x, p.y, zB), new Vector2(uCurStart, 0f), nCur));

                    fEnd[prev] = vfPrev; bEnd[prev] = vbPrev;
                    fStart[i] = vfCur; bStart[i] = vbCur;
                }
            }

            for (int e = 0; e < n; e++)
            {
                if (edgeLen[e] <= 1e-9f) continue;
                // 前面始点 → 背面始点 → 背面終点 → 前面終点 で外向き。
                mo.AddQuad(fStart[e], bStart[e], bEnd[e], fEnd[e]);
            }
        }

        // ================================================================
        // 穴壁
        // ================================================================

        /// <summary>穴の内壁を張る。法線は軸へ向ける。</summary>
        private static void BuildBoreWall(
            MeshObject mo, Vector2[] boreRing, int count, float zF, float zB, float bore)
        {
            int start = mo.VertexCount;

            for (int i = 0; i < count; i++)
            {
                Vector2 p = boreRing[i];
                Vector2 dir = bore > 1e-9f ? p / bore : Vector2.right;
                var nrm = new Vector3(-dir.x, -dir.y, 0f);
                float u = (float)i / count;

                mo.Vertices.Add(new Vertex(new Vector3(p.x, p.y, zF), new Vector2(u, 1f), nrm));
                mo.Vertices.Add(new Vertex(new Vector3(p.x, p.y, zB), new Vector2(u, 0f), nrm));
            }

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                if ((boreRing[i] - boreRing[j]).sqrMagnitude <= 1e-12f) continue;

                int fi = start + i * 2;
                int bi = start + i * 2 + 1;
                int fj = start + j * 2;
                int bj = start + j * 2 + 1;

                // 前面 i → 前面 j → 背面 j → 背面 i で軸向き。
                mo.AddQuad(fi, fj, bj, bi);
            }
        }

        // ================================================================
        // 配置面
        // ================================================================

        /// <summary>
        /// XY 平面で組んだ形状を指定の平面へ回す。純回転なので巻き順は変わらない。
        /// </summary>
        private static void ApplyOrientation(MeshObject mo, PlaneOrientation orientation)
        {
            if (orientation == PlaneOrientation.XY) return;

            // XZ: +90° about X（板を XZ へ倒し、厚みを Y へ）
            // YZ: +90° about Y（板を YZ へ立て、厚みを X へ）
            Quaternion q = orientation == PlaneOrientation.XZ
                ? Quaternion.Euler(90f, 0f, 0f)
                : Quaternion.Euler(0f, 90f, 0f);

            foreach (var v in mo.Vertices)
            {
                if (v == null) continue;
                v.Position = q * v.Position;
                if (v.Normals == null) continue;
                for (int i = 0; i < v.Normals.Count; i++)
                    v.Normals[i] = q * v.Normals[i];
            }

            mo.InvalidatePositionCache();
        }

        // ================================================================
        // 共有ユーティリティ
        // ================================================================

        /// <summary>極座標から XY 平面の点を作る。角度はラジアン。</summary>
        public static Vector2 Polar(float radius, float angleRad)
            => new Vector2(radius * Mathf.Cos(angleRad), radius * Mathf.Sin(angleRad));

        /// <summary>前面 / 背面の平面投影 UV。</summary>
        private static Vector2 PlanarUV(Vector2 p, float scale)
            => new Vector2(0.5f + p.x * scale, 0.5f + p.y * scale);

        /// <summary>隣り合う重複点を取り除く（閉じた輪郭として先頭と末尾も見る）。</summary>
        public static void RemoveNearlyDuplicateNeighbors(List<Vector2> points, float sqrEpsilon)
        {
            if (points == null) return;

            for (int i = points.Count - 1; i > 0; i--)
            {
                if ((points[i] - points[i - 1]).sqrMagnitude <= sqrEpsilon)
                    points.RemoveAt(i);
            }

            if (points.Count > 2 &&
                (points[0] - points[points.Count - 1]).sqrMagnitude <= sqrEpsilon)
            {
                points.RemoveAt(points.Count - 1);
            }
        }
    }
}
