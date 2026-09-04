// GearLoftBuilder.cs
// 断面列（閉じた 2D 輪郭 ＋ 任意形状の穴）を Z 方向へロフトする共有ビルダー。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【何のためにあるか】
//   機構部品はどれも「閉じた断面を Z へ積む」形で書ける。
//     はすば歯車     … 断面を少しずつ回す
//     かさ歯車       … 断面を少しずつ縮める
//     ウォームホイール… 断面を回しつつ半径方向へ歪める
//     ウォーム       … 断面ごとに半径が変わる円
//     ラック         … 断面が同じ、または位相だけずれる
//     内歯車         … 外周が円、穴が歯形（断面は 1 種類）
//   断面の作り方は図形ごとに違うが、面の張り方・フタ・法線・向き・ピボットは共通なので
//   そこだけをここへ集める。
//
// 【入力の規約】
//   ・sections は Z の昇順。先頭が背面（-Z 側）、末尾が前面（+Z 側）。
//   ・Outer は XY 平面の閉じた単純多角形で、原点まわりに CCW（符号付き面積が正）。
//     終点に始点を重ねないこと。全断面で点数を揃えること。
//   ・Hole も CCW。全断面が null か、全断面が同じ点数か、のどちらか。
//   ・角度の単調増加は要求しない。歯元トロコイドは切り下げが起きると角度が少し逆行する。
//
// 【フタ】
//   Triangulate … Poly2Tri で三角化する（GearDiskBuilder と同じ経路・同じ頂点並び）。
//                 失敗したら、外周と穴の点数が同じときに限り四角形帯へ退避する。
//   IndexBand   … 最初から四角形帯で塞ぐ。外周と穴の点数が同じであることが前提。
//                 内歯車のように、外周点を穴の各点の角度へ合わせて作った形で使う。
//
// 【面の向き】
//   GearDiskBuilder に合わせる。前面（+Z 側）の法線が +Z、外周壁は外向き、
//   穴壁は軸へ向く（穴の内側から見える）。
//
// 【法線】
//   壁は面法線を頂点へ積んで平均する。ロフトでは断面が縮んだり歪んだりして壁が
//   Z に対して傾くため、断面内の 2D 法線をそのまま置くと円錐面や、のど形状の陰影が崩れる。
//   周方向に鋭い角があるところは頂点を分けて折り目にする（しきい値は GearDiskBuilder と同じ）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>ロフト 1 断面。</summary>
    public sealed class GearLoftSection
    {
        /// <summary>断面を置く Z。</summary>
        public float Z;

        /// <summary>外周輪郭（CCW）。全断面で同じ点数。</summary>
        public Vector2[] Outer;

        /// <summary>穴の輪郭（CCW）。穴なしは null。全断面で同じ点数。</summary>
        public Vector2[] Hole;

        public GearLoftSection() { }

        public GearLoftSection(float z, Vector2[] outer, Vector2[] hole = null)
        {
            Z = z;
            Outer = outer;
            Hole = hole;
        }
    }

    /// <summary>フタの張り方。</summary>
    public enum GearLoftCapMode
    {
        /// <summary>Poly2Tri で三角化する。</summary>
        Triangulate,
        /// <summary>外周と穴を添字で対応づけた四角形帯で塞ぐ。</summary>
        IndexBand,
    }

    public static class GearLoftBuilder
    {
        /// <summary>
        /// 壁を滑らかにつなぐ角度のしきい値（度）。
        /// 隣り合う辺の法線がこれより開いていれば、その角で頂点を分けて折り目にする。
        /// </summary>
        public const float WallSmoothAngleDeg = 40f;

        /// <summary>穴リングの分割数の下限・上限。GearDiskBuilder と同じ値。</summary>
        public const int BoreSegmentsMin = GearDiskBuilder.BoreSegmentsMin;
        public const int BoreSegmentsMax = GearDiskBuilder.BoreSegmentsMax;

        // ================================================================
        // 入口
        // ================================================================

        /// <summary>
        /// 断面列からロフトメッシュを作る。断面が 1 枚のときは前面のフタだけの板になる。
        /// </summary>
        /// <param name="meshName">メッシュ名</param>
        /// <param name="sections">Z 昇順の断面列。</param>
        /// <param name="capMode">フタの張り方。</param>
        /// <param name="orientation">板を置く平面。XY / XZ / YZ。</param>
        /// <param name="flipFaces">生成後に全面を裏返す。</param>
        /// <param name="pivot">AABB サイズ基準のピボット。</param>
        public static MeshObject Build(
            string meshName,
            IReadOnlyList<GearLoftSection> sections,
            GearLoftCapMode capMode,
            PlaneOrientation orientation,
            bool flipFaces,
            Vector3 pivot)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(meshName) ? "GearLoft" : meshName);

            if (!Validate(sections, out int n, out int m))
                return mo;

            int ns = sections.Count;

            float maxR = 0f;
            for (int s = 0; s < ns; s++)
            {
                var sec = sections[s];
                for (int i = 0; i < n; i++)
                    maxR = Mathf.Max(maxR, sec.Outer[i].magnitude);
            }
            float uvScale = maxR > 1e-6f ? 0.5f / maxR : 1f;

            // ------------------------------------------------------------
            // フタ
            // ------------------------------------------------------------

            // 断面 1 枚は前面 1 枚だけの板。前後を重ねると同じ位置に面が二重に載る。
            if (ns >= 2)
                BuildCap(mo, sections[0], false, capMode, uvScale, n, m);

            BuildCap(mo, sections[ns - 1], true, capMode, uvScale, n, m);

            // ------------------------------------------------------------
            // 壁
            // ------------------------------------------------------------

            if (ns >= 2)
            {
                BuildWall(mo, sections, false, n);

                if (m >= 3)
                    BuildWall(mo, sections, true, m);
            }

            // ------------------------------------------------------------
            // 後処理
            // ------------------------------------------------------------

            GearDiskBuilder.ApplyOrientation(mo, orientation);

            if (flipFaces)
                PrimitiveMeshPostProcess.FlipFaces(mo);

            PrimitiveMeshPostProcess.ApplyPivotOffset(mo, pivot);
            PrimitiveMeshPostProcess.SortVerticesCanonical(mo);

            mo.InvalidatePositionCache();
            return mo;
        }

        /// <summary>
        /// 中心の丸穴リングを作る。半径 0 以下なら null。
        /// 角度は昇順なので CCW になる。
        /// </summary>
        public static Vector2[] MakeBoreRing(float radius, int segments)
        {
            if (radius <= 1e-6f) return null;

            int m = Mathf.Clamp(segments, BoreSegmentsMin, BoreSegmentsMax);
            var ring = new Vector2[m];

            for (int i = 0; i < m; i++)
            {
                float a = i * 2f * Mathf.PI / m;
                ring[i] = GearDiskBuilder.Polar(radius, a);
            }

            return ring;
        }

        /// <summary>
        /// 輪郭の各辺までの最短距離を下回るように、穴半径を抑える。
        /// 頂点までの距離で測ると、谷が直線で結ばれている形で辺の中ほどが穴の内側へ入り込む。
        /// </summary>
        public static float ClampBoreRadius(IReadOnlyList<Vector2> outline, float boreRadius)
        {
            float bore = Mathf.Max(0f, boreRadius);
            if (bore <= 0f || outline == null || outline.Count < 3) return bore;

            float minEdgeDist = GearDiskBuilder.MinDistanceToOutline(outline, outline.Count);
            if (bore >= minEdgeDist) bore = minEdgeDist * 0.99f;

            return bore;
        }

        // ================================================================
        // 検証
        // ================================================================

        private static bool Validate(IReadOnlyList<GearLoftSection> sections, out int n, out int m)
        {
            n = 0;
            m = 0;

            if (sections == null || sections.Count < 1) return false;

            var first = sections[0];
            if (first == null || first.Outer == null || first.Outer.Length < 3) return false;

            n = first.Outer.Length;
            m = first.Hole?.Length ?? 0;

            // 穴の点数が 3 未満なら穴なし扱いにする。
            if (m < 3) m = 0;

            for (int s = 0; s < sections.Count; s++)
            {
                var sec = sections[s];

                if (sec == null || sec.Outer == null || sec.Outer.Length != n)
                {
                    Debug.LogWarning("[GearLoftBuilder] 断面ごとに外周の点数が違います。");
                    return false;
                }

                int hm = sec.Hole?.Length ?? 0;
                if (hm < 3) hm = 0;

                if (hm != m)
                {
                    Debug.LogWarning("[GearLoftBuilder] 断面ごとに穴の点数が違います。");
                    return false;
                }
            }

            return true;
        }

        // ================================================================
        // フタ
        // ================================================================

        /// <summary>
        /// 片面ぶんのフタを張る。頂点並びは「外周リング → 穴リング」で、
        /// GearDiskBuilder.AddCapRingVertices / TryTriangulateCap の統合インデックスに合わせてある。
        /// </summary>
        private static void BuildCap(
            MeshObject mo, GearLoftSection sec, bool front,
            GearLoftCapMode capMode, float uvScale, int n, int m)
        {
            Vector2[] hole = m >= 3 ? sec.Hole : null;

            int b = mo.VertexCount;
            GearDiskBuilder.AddCapRingVertices(
                mo, sec.Outer, hole, sec.Z,
                front ? Vector3.forward : Vector3.back, uvScale);

            List<int> tris = capMode == GearLoftCapMode.Triangulate
                ? GearDiskBuilder.TryTriangulateCap(sec.Outer, hole)
                : null;

            if (tris != null)
            {
                for (int t = 0; t + 2 < tris.Count; t += 3)
                {
                    int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];

                    // Poly2Tri の返す巻き順は保証しないので、符号付き面積で CCW へ揃える。
                    if (GearDiskBuilder.SignedArea(
                            GearDiskBuilder.CapPoint(sec.Outer, hole, i0),
                            GearDiskBuilder.CapPoint(sec.Outer, hole, i1),
                            GearDiskBuilder.CapPoint(sec.Outer, hole, i2)) < 0f)
                    {
                        int tmp = i1; i1 = i2; i2 = tmp;
                    }

                    // CCW = 前面（+Z）。背面はその逆。
                    if (front) mo.AddTriangle(b + i0, b + i1, b + i2);
                    else       mo.AddTriangle(b + i0, b + i2, b + i1);
                }

                return;
            }

            if (hole != null && m == n)
            {
                BuildBandCap(mo, sec.Outer, hole, b, n, front);
                return;
            }

            Debug.LogWarning(
                "[GearLoftBuilder] フタを張れませんでした" +
                "（三角化に失敗し、外周と穴の点数も一致していません）。");
        }

        /// <summary>外周と穴を添字で対応づけた四角形帯でフタを塞ぐ。</summary>
        private static void BuildBandCap(
            MeshObject mo, Vector2[] outer, Vector2[] hole, int b, int n, bool front)
        {
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;

                bool outerDegenerate = (outer[i] - outer[j]).sqrMagnitude <= 1e-12f;
                bool holeDegenerate  = (hole[i]  - hole[j]).sqrMagnitude  <= 1e-12f;

                if (outerDegenerate && holeDegenerate) continue;

                if (holeDegenerate)
                {
                    if (front) mo.AddTriangle(b + i, b + j, b + n + i);
                    else       mo.AddTriangle(b + i, b + n + i, b + j);
                }
                else if (outerDegenerate)
                {
                    if (front) mo.AddTriangle(b + i, b + n + j, b + n + i);
                    else       mo.AddTriangle(b + i, b + n + i, b + n + j);
                }
                else
                {
                    if (front) mo.AddQuad(b + i, b + j, b + n + j, b + n + i);
                    else       mo.AddQuad(b + i, b + n + i, b + n + j, b + j);
                }
            }
        }

        // ================================================================
        // 壁
        // ================================================================

        /// <summary>
        /// 外周壁または穴壁を張る。
        /// 角が鋭いところは頂点を分けて折り目にし、緩いところは面法線を平均して滑らかにつなぐ。
        /// </summary>
        /// <param name="useHole">true なら穴の輪郭を使い、法線を軸へ向ける。</param>
        /// <param name="n">輪郭の点数。</param>
        private static void BuildWall(
            MeshObject mo, IReadOnlyList<GearLoftSection> sections, bool useHole, int n)
        {
            int ns = sections.Count;
            if (ns < 2 || n < 3) return;

            float cosLimit = Mathf.Cos(WallSmoothAngleDeg * Mathf.Deg2Rad);

            // ── 断面ごとの辺法線・辺長 ──
            var edgeN   = new Vector2[ns][];
            var edgeLen = new float[ns][];

            for (int s = 0; s < ns; s++)
            {
                var loop = Loop(sections[s], useHole);

                edgeN[s]   = new Vector2[n];
                edgeLen[s] = new float[n];

                for (int e = 0; e < n; e++)
                {
                    Vector2 d = loop[(e + 1) % n] - loop[e];
                    float len = d.magnitude;

                    edgeLen[s][e] = len;

                    Vector2 outward = len > 1e-9f
                        ? new Vector2(d.y / len, -d.x / len)
                        : loop[e].normalized;

                    edgeN[s][e] = useHole ? -outward : outward;
                }
            }

            // ── U 座標は先頭断面の周長で決める（断面ごとに動かすと帯がねじれて見える） ──
            var uAt = new float[n];
            {
                float total = 0f;
                for (int e = 0; e < n; e++) total += edgeLen[0][e];

                float acc = 0f;
                for (int e = 0; e < n; e++)
                {
                    uAt[e] = total > 1e-9f ? acc / total : 0f;
                    acc += edgeLen[0][e];
                }
            }

            // ── 頂点 ──
            var vStart = new int[ns][];
            var vEnd   = new int[ns][];

            int wallBase = mo.VertexCount;
            var fallback = new List<Vector3>();

            for (int s = 0; s < ns; s++)
            {
                var loop = Loop(sections[s], useHole);
                float z = sections[s].Z;
                float v = ns > 1 ? s / (float)(ns - 1) : 0f;

                vStart[s] = new int[n];
                vEnd[s]   = new int[n];

                for (int i = 0; i < n; i++)
                {
                    int prev = (i - 1 + n) % n;
                    Vector2 p = loop[i];

                    bool smooth = Vector2.Dot(edgeN[s][prev], edgeN[s][i]) >= cosLimit;

                    if (smooth)
                    {
                        Vector2 nn = (edgeN[s][prev] + edgeN[s][i]).normalized;
                        if (nn.sqrMagnitude < 1e-12f) nn = edgeN[s][i];

                        int idx = mo.VertexCount;
                        mo.Vertices.Add(new Vertex(
                            new Vector3(p.x, p.y, z), new Vector2(uAt[i], v), Vector3.zero));
                        fallback.Add(new Vector3(nn.x, nn.y, 0f));

                        vEnd[s][prev] = idx;
                        vStart[s][i]  = idx;
                    }
                    else
                    {
                        // 折り目：直前の辺の終点と、この辺の始点を別々の頂点にする。
                        // 輪の終端側は U=1 に伸ばして継ぎ目を作らない。
                        float uPrevEnd = (i == 0) ? 1f : uAt[i];

                        int idxPrev = mo.VertexCount;
                        mo.Vertices.Add(new Vertex(
                            new Vector3(p.x, p.y, z), new Vector2(uPrevEnd, v), Vector3.zero));
                        fallback.Add(new Vector3(edgeN[s][prev].x, edgeN[s][prev].y, 0f));

                        int idxCur = mo.VertexCount;
                        mo.Vertices.Add(new Vertex(
                            new Vector3(p.x, p.y, z), new Vector2(uAt[i], v), Vector3.zero));
                        fallback.Add(new Vector3(edgeN[s][i].x, edgeN[s][i].y, 0f));

                        vEnd[s][prev] = idxPrev;
                        vStart[s][i]  = idxCur;
                    }
                }
            }

            int wallCount = mo.VertexCount - wallBase;
            var accum = new Vector3[wallCount];

            // ── 面 ──
            for (int s = 0; s + 1 < ns; s++)
            {
                for (int e = 0; e < n; e++)
                {
                    if (edgeLen[s][e] <= 1e-9f && edgeLen[s + 1][e] <= 1e-9f) continue;

                    int a = vStart[s][e];
                    int b = vEnd[s][e];
                    int c = vEnd[s + 1][e];
                    int d = vStart[s + 1][e];

                    int q0, q1, q2, q3;

                    if (!useHole)
                    {
                        // 断面 s の始点 → 断面 s の終点 → 断面 s+1 の終点 → 断面 s+1 の始点 で外向き。
                        q0 = a; q1 = b; q2 = c; q3 = d;
                    }
                    else
                    {
                        // 穴壁は逆回り。法線が軸へ向く。
                        q0 = a; q1 = d; q2 = c; q3 = b;
                    }

                    mo.AddQuad(q0, q1, q2, q3);

                    Vector3 p0 = mo.Vertices[q0].Position;
                    Vector3 nrm = Vector3.Cross(
                        mo.Vertices[q1].Position - p0,
                        mo.Vertices[q3].Position - p0);

                    if (nrm.sqrMagnitude <= 1e-20f) continue;

                    nrm.Normalize();

                    accum[q0 - wallBase] += nrm;
                    accum[q1 - wallBase] += nrm;
                    accum[q2 - wallBase] += nrm;
                    accum[q3 - wallBase] += nrm;
                }
            }

            // ── 法線 ──
            for (int k = 0; k < wallCount; k++)
            {
                Vector3 nrm = accum[k];

                if (nrm.sqrMagnitude <= 1e-20f) nrm = fallback[k];
                if (nrm.sqrMagnitude <= 1e-20f) nrm = Vector3.forward;

                var vert = mo.Vertices[wallBase + k];
                if (vert.Normals.Count > 0) vert.Normals[0] = nrm.normalized;
                else                        vert.Normals.Add(nrm.normalized);
            }
        }

        private static Vector2[] Loop(GearLoftSection sec, bool useHole)
            => useHole ? sec.Hole : sec.Outer;
    }
}
