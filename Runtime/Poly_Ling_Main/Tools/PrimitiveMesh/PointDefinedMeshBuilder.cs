// PointDefinedMeshBuilder.cs
// 点指定図形のメッシュ組み立て（線分：円筒・角柱 / 三角：板 / 四角：板）。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【役割】UI にもモデルにも依存しない純粋な組み立て。
//   出力は「枠（slot）の並び」と「枠番号で表した面」の計画 PointDefinedMeshPlan。
//   枠は新規頂点（ワールド座標）か既存頂点（書き込み先の頂点番号）のどちらか。
//   プレビュー用 MeshObject も、書き込み先への実際の追加も、この計画 1 つから作る。
//   プレビュー用と生成用で別の組み立てを持たない（仕様 3.3）。
//
// 【頂点の共有】（仕様 8.1）
//   同じ位置の頂点を別々に作って後から結合することはしない。
//   隣接セル・表面と側面・裏面と側面・三角形の頂点集中部は最初から同じ枠を使う。
//   既存経路と共有する辺は、経路上の既存頂点の枠をそのまま使う（仕様 9.8）。
//
// 【表裏と奥行き】（仕様 4）
//   viewDir はカメラの視線方向（表面から裏面へ向かう向き）。
//   表面の法線が viewDir の逆（カメラ側）を向くように、最後に全面の巻き順をそろえる。
//   裏面の枠は表面の枠を viewDir * 奥行き だけずらした位置に置く。
//   法線は NormalHelper.CalculateFaceNormal と同じ規約（Cross(p1-p0, p2-p0)）。
//
// 【面の張り方】
//   四角：境界 4 辺の点列から Coons 補間（4 辺を使った双線形補間。辺が直線なら双線形と一致）。
//   三角：上辺を頂点 A に潰した同じ補間。各段の分割数は底辺分割数で一定。
//         最上段は A に集まる三角形、その下は四角形。
//   線分：P0-P1 を軸にした角柱。キャップは中心頂点を置き、側面の端の輪を共有する。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>角・経路上の 1 点。既存頂点を指すときは書き込み先の頂点番号を持つ。</summary>
    public struct PointDefinedCorner
    {
        public Vector3 World;
        /// <summary>書き込み先の既存頂点番号。新規点は -1。</summary>
        public int ExistingVertex;

        public static PointDefinedCorner New(Vector3 world)
            => new PointDefinedCorner { World = world, ExistingVertex = -1 };

        public static PointDefinedCorner Existing(int vertex, Vector3 world)
            => new PointDefinedCorner { World = world, ExistingVertex = vertex };
    }

    /// <summary>組み立て結果。枠の並びと、枠番号で表した面。</summary>
    public sealed class PointDefinedMeshPlan
    {
        /// <summary>枠ごとのワールド座標。</summary>
        public readonly List<Vector3> SlotWorld = new List<Vector3>();

        /// <summary>枠ごとの既存頂点番号。新規の枠は -1。</summary>
        public readonly List<int> SlotExisting = new List<int>();

        /// <summary>枠番号で表した面。巻き順は表がカメラ側。</summary>
        public readonly List<int[]> Faces = new List<int[]>();

        private readonly Dictionary<int, int> _slotOfExisting = new Dictionary<int, int>();

        public int SlotCount => SlotWorld.Count;

        public int AddNew(Vector3 world)
        {
            SlotWorld.Add(world);
            SlotExisting.Add(-1);
            return SlotWorld.Count - 1;
        }

        /// <summary>既存頂点の枠。同じ頂点は同じ枠になる。</summary>
        public int AddExisting(int vertex, Vector3 world)
        {
            if (_slotOfExisting.TryGetValue(vertex, out int slot)) return slot;
            SlotWorld.Add(world);
            SlotExisting.Add(vertex);
            slot = SlotWorld.Count - 1;
            _slotOfExisting[vertex] = slot;
            return slot;
        }

        public int AddCorner(PointDefinedCorner c)
            => c.ExistingVertex >= 0 ? AddExisting(c.ExistingVertex, c.World) : AddNew(c.World);

        public void AddFace(params int[] slots) => Faces.Add(slots);

        public void ReverseAll()
        {
            foreach (var f in Faces) Array.Reverse(f);
        }
    }

    public static class PointDefinedMeshBuilder
    {
        private const float PositionEps = 1e-6f;

        /// <summary>図形ごとに要る点の数。</summary>
        public static int RequiredPoints(PointPrimitiveMode mode)
        {
            switch (mode)
            {
                case PointPrimitiveMode.Line:     return 2;
                case PointPrimitiveMode.Triangle: return 3;
                default:                          return 4;
            }
        }

        // ================================================================
        // 線分：円筒・角柱
        // ================================================================

        public static bool BuildLine(
            Vector3 p0, Vector3 p1, Vector3 viewDir, PointDefinedParams p,
            out PointDefinedMeshPlan plan, out string reason)
        {
            plan = null;
            reason = null;

            Vector3 axis = p1 - p0;
            float length = axis.magnitude;
            if (length < PositionEps) { reason = "2 点が同じ位置です"; return false; }

            int   sides  = Mathf.Clamp(p.Sides, PointDefinedParams.SidesMin, PointDefinedParams.SidesMax);
            int   segs   = Mathf.Clamp(p.LengthSegments, PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax);
            float radius = Mathf.Clamp(p.Radius, PointDefinedParams.RadiusMin, PointDefinedParams.RadiusMax);

            // 断面の基準方向はビルボード（視線）から取る。
            // u は視線と軸の両方に直交（画面内で軸に直交する向き）、w は u と軸に直交。
            Vector3 dir = axis / length;
            Vector3 u = Vector3.Cross(viewDir, dir);
            if (u.sqrMagnitude < 1e-10f)
                u = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right);
            u.Normalize();
            Vector3 w = Vector3.Cross(dir, u);

            plan = new PointDefinedMeshPlan();

            var ring = new int[segs + 1, sides];
            for (int k = 0; k <= segs; k++)
            {
                Vector3 center = p0 + axis * ((float)k / segs);
                for (int s = 0; s < sides; s++)
                {
                    float a = 2f * Mathf.PI * s / sides;
                    ring[k, s] = plan.AddNew(center + radius * (Mathf.Cos(a) * u + Mathf.Sin(a) * w));
                }
            }

            for (int k = 0; k < segs; k++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    plan.AddFace(ring[k, s], ring[k, s1], ring[k + 1, s1], ring[k + 1, s]);
                }
            }

            if (p.Cap)
            {
                int c0 = plan.AddNew(p0);
                for (int s = 0; s < sides; s++)
                    plan.AddFace(c0, ring[0, (s + 1) % sides], ring[0, s]);

                int c1 = plan.AddNew(p1);
                for (int s = 0; s < sides; s++)
                    plan.AddFace(c1, ring[segs, s], ring[segs, (s + 1) % sides]);
            }

            // 側面 1 枚の法線が外（軸から離れる向き）を向くように全面をそろえる。
            var f0 = plan.Faces[0];
            var q = new List<Vector3>(4);
            Vector3 fc = Vector3.zero;
            foreach (int slot in f0) { q.Add(plan.SlotWorld[slot]); fc += plan.SlotWorld[slot]; }
            fc /= f0.Length;
            Vector3 onAxis = p0 + dir * Vector3.Dot(fc - p0, dir);
            if (Vector3.Dot(Newell(q), fc - onAxis) < 0f) plan.ReverseAll();

            return true;
        }

        // ================================================================
        // 四角：板
        // ================================================================

        /// <summary>
        /// 四角形の板を組む。点は P0 → P1 → P2 → P3 の周回順。
        /// 共有する辺は既存頂点列（両端を含み、分割数 + 1 個）を渡す。共有しない辺は null。
        /// </summary>
        /// <param name="bottom">P0 → P1（横）</param>
        /// <param name="top">P3 → P2（横）</param>
        /// <param name="left">P0 → P3（縦）</param>
        /// <param name="right">P1 → P2（縦）</param>
        public static bool BuildQuad(
            PointDefinedCorner[] c,
            IReadOnlyList<PointDefinedCorner> bottom, IReadOnlyList<PointDefinedCorner> top,
            IReadOnlyList<PointDefinedCorner> left,   IReadOnlyList<PointDefinedCorner> right,
            Vector3 viewDir, PointDefinedParams p,
            out PointDefinedMeshPlan plan, out string reason)
        {
            plan = null;
            reason = null;

            if (c == null || c.Length != 4) { reason = "四角形には点が 4 個要ります"; return false; }
            if (!CheckDistinct(c, out reason)) return false;

            var quad = new List<Vector3> { c[0].World, c[1].World, c[2].World, c[3].World };
            Vector3 n = Newell(quad);
            if (n.magnitude < AreaEps(quad)) { reason = "四角形の面積がほぼ 0 です"; return false; }
            if (IsSelfIntersectingQuad(quad, viewDir))
            { reason = "四角形が自己交差しています。点を周回順に指定してください"; return false; }

            int N = Mathf.Clamp(p.USegments, PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax);
            int M = Mathf.Clamp(p.VSegments, PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax);

            if (!CheckPath(bottom, N, c[0], c[1], out reason)) return false;
            if (!CheckPath(top,    N, c[3], c[2], out reason)) return false;
            if (!CheckPath(left,   M, c[0], c[3], out reason)) return false;
            if (!CheckPath(right,  M, c[1], c[2], out reason)) return false;

            Vector3 c0 = c[0].World, c1 = c[1].World, c2 = c[2].World, c3 = c[3].World;

            Vector3 B(int i) => bottom != null ? bottom[i].World : Vector3.Lerp(c0, c1, (float)i / N);
            Vector3 T(int i) => top    != null ? top[i].World    : Vector3.Lerp(c3, c2, (float)i / N);
            Vector3 L(int j) => left   != null ? left[j].World   : Vector3.Lerp(c0, c3, (float)j / M);
            Vector3 R(int j) => right  != null ? right[j].World  : Vector3.Lerp(c1, c2, (float)j / M);

            Vector3 S(int i, int j)
            {
                float u = (float)i / N, v = (float)j / M;
                return (1f - v) * B(i) + v * T(i) + (1f - u) * L(j) + u * R(j)
                     - ((1f - u) * (1f - v) * c0 + u * (1f - v) * c1 + (1f - u) * v * c3 + u * v * c2);
            }

            plan = new PointDefinedMeshPlan();
            var pl = plan;

            int FrontSlot(int i, int j)
            {
                if (i == 0 && j == 0) return pl.AddCorner(c[0]);
                if (i == N && j == 0) return pl.AddCorner(c[1]);
                if (i == N && j == M) return pl.AddCorner(c[2]);
                if (i == 0 && j == M) return pl.AddCorner(c[3]);
                if (j == 0 && bottom != null) return pl.AddCorner(bottom[i]);
                if (j == M && top    != null) return pl.AddCorner(top[i]);
                if (i == 0 && left   != null) return pl.AddCorner(left[j]);
                if (i == N && right  != null) return pl.AddCorner(right[j]);
                return pl.AddNew(S(i, j));
            }

            var g = new int[N + 1, M + 1];
            for (int j = 0; j <= M; j++)
                for (int i = 0; i <= N; i++)
                    g[i, j] = FrontSlot(i, j);

            for (int j = 0; j < M; j++)
                for (int i = 0; i < N; i++)
                    plan.AddFace(g[i, j], g[i + 1, j], g[i + 1, j + 1], g[i, j + 1]);

            float depth = Mathf.Clamp(p.Depth, PointDefinedParams.DepthMin, PointDefinedParams.DepthMax);
            if (depth > PositionEps)
            {
                int K = Mathf.Clamp(p.DepthSegments, PointDefinedParams.DepthSegmentsMin, PointDefinedParams.DepthSegmentsMax);

                // 境界の輪。表面の面と同じ向きにたどる。
                var ring = new List<int>();
                for (int i = 0; i < N; i++) ring.Add(g[i, 0]);
                for (int j = 0; j < M; j++) ring.Add(g[N, j]);
                for (int i = N; i > 0; i--) ring.Add(g[i, M]);
                for (int j = M; j > 0; j--) ring.Add(g[0, j]);

                var gb = new int[N + 1, M + 1];
                for (int j = 0; j <= M; j++)
                    for (int i = 0; i <= N; i++)
                        gb[i, j] = plan.AddNew(plan.SlotWorld[g[i, j]] + viewDir * depth);

                var backRing = new List<int>();
                for (int i = 0; i < N; i++) backRing.Add(gb[i, 0]);
                for (int j = 0; j < M; j++) backRing.Add(gb[N, j]);
                for (int i = N; i > 0; i--) backRing.Add(gb[i, M]);
                for (int j = M; j > 0; j--) backRing.Add(gb[0, j]);

                AddSides(plan, ring, backRing, viewDir, depth, K);

                for (int j = 0; j < M; j++)
                    for (int i = 0; i < N; i++)
                        plan.AddFace(gb[i, j + 1], gb[i + 1, j + 1], gb[i + 1, j], gb[i, j]);
            }

            if (Vector3.Dot(n, -viewDir) < 0f) plan.ReverseAll();
            return true;
        }

        // ================================================================
        // 三角：板
        // ================================================================

        /// <summary>
        /// 三角形の板を組む。apex が頂点、b → c が底辺。
        /// 共有する辺は既存頂点列（両端を含み、分割数 + 1 個）を渡す。共有しない辺は null。
        /// </summary>
        /// <param name="left">apex → b（斜辺。高さ方向分割数）</param>
        /// <param name="right">apex → c（斜辺。高さ方向分割数）</param>
        /// <param name="baseEdge">b → c（底辺。底辺分割数）</param>
        public static bool BuildTriangle(
            PointDefinedCorner apex, PointDefinedCorner b, PointDefinedCorner c,
            IReadOnlyList<PointDefinedCorner> left, IReadOnlyList<PointDefinedCorner> right,
            IReadOnlyList<PointDefinedCorner> baseEdge,
            Vector3 viewDir, PointDefinedParams p,
            out PointDefinedMeshPlan plan, out string reason)
        {
            plan = null;
            reason = null;

            if (!CheckDistinct(new[] { apex, b, c }, out reason)) return false;

            var tri = new List<Vector3> { apex.World, b.World, c.World };
            Vector3 n = Newell(tri);
            if (n.magnitude < AreaEps(tri)) { reason = "三角形の面積がほぼ 0 です"; return false; }

            int N = Mathf.Clamp(p.BaseSegments,   PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax);
            int M = Mathf.Clamp(p.HeightSegments, PointDefinedParams.SegmentsMin, PointDefinedParams.SegmentsMax);

            if (!CheckPath(left,     M, apex, b, out reason)) return false;
            if (!CheckPath(right,    M, apex, c, out reason)) return false;
            if (!CheckPath(baseEdge, N, b,    c, out reason)) return false;

            Vector3 A = apex.World, Bw = b.World, Cw = c.World;

            Vector3 L(int j)  => left     != null ? left[j].World     : Vector3.Lerp(A, Bw, (float)j / M);
            Vector3 R(int j)  => right    != null ? right[j].World    : Vector3.Lerp(A, Cw, (float)j / M);
            Vector3 Bs(int i) => baseEdge != null ? baseEdge[i].World : Vector3.Lerp(Bw, Cw, (float)i / N);

            Vector3 S(int i, int j)
            {
                float u = (float)i / N, v = (float)j / M;
                return L(j) + (R(j) - L(j)) * u + v * (Bs(i) - Vector3.Lerp(Bw, Cw, u));
            }

            plan = new PointDefinedMeshPlan();
            var pl = plan;

            int apexSlot = plan.AddCorner(apex);

            int RowSlot(int i, int j)
            {
                if (j == M && i == 0) return pl.AddCorner(b);
                if (j == M && i == N) return pl.AddCorner(c);
                if (i == 0 && left     != null) return pl.AddCorner(left[j]);
                if (i == N && right    != null) return pl.AddCorner(right[j]);
                if (j == M && baseEdge != null) return pl.AddCorner(baseEdge[i]);
                return pl.AddNew(S(i, j));
            }

            // g[i, j] は j = 1..M の段。j = 0 は頂点 A（apexSlot）。
            var g = new int[N + 1, M + 1];
            for (int j = 1; j <= M; j++)
                for (int i = 0; i <= N; i++)
                    g[i, j] = RowSlot(i, j);

            for (int i = 0; i < N; i++)
                plan.AddFace(apexSlot, g[i, 1], g[i + 1, 1]);
            for (int j = 1; j < M; j++)
                for (int i = 0; i < N; i++)
                    plan.AddFace(g[i, j], g[i, j + 1], g[i + 1, j + 1], g[i + 1, j]);

            float depth = Mathf.Clamp(p.Depth, PointDefinedParams.DepthMin, PointDefinedParams.DepthMax);
            if (depth > PositionEps)
            {
                int K = Mathf.Clamp(p.DepthSegments, PointDefinedParams.DepthSegmentsMin, PointDefinedParams.DepthSegmentsMax);

                // 境界の輪：A → 左斜辺 → B → 底辺 → C → 右斜辺 → A。表面の面と同じ向き。
                var ring = new List<int> { apexSlot };
                for (int j = 1; j <= M; j++) ring.Add(g[0, j]);
                for (int i = 1; i <= N; i++) ring.Add(g[i, M]);
                for (int j = M - 1; j >= 1; j--) ring.Add(g[N, j]);

                Vector3 off = viewDir * depth;
                int apexBack = plan.AddNew(plan.SlotWorld[apexSlot] + off);
                var gb = new int[N + 1, M + 1];
                for (int j = 1; j <= M; j++)
                    for (int i = 0; i <= N; i++)
                        gb[i, j] = plan.AddNew(plan.SlotWorld[g[i, j]] + off);

                var backRing = new List<int> { apexBack };
                for (int j = 1; j <= M; j++) backRing.Add(gb[0, j]);
                for (int i = 1; i <= N; i++) backRing.Add(gb[i, M]);
                for (int j = M - 1; j >= 1; j--) backRing.Add(gb[N, j]);

                AddSides(plan, ring, backRing, viewDir, depth, K);

                for (int i = 0; i < N; i++)
                    plan.AddFace(apexBack, gb[i + 1, 1], gb[i, 1]);
                for (int j = 1; j < M; j++)
                    for (int i = 0; i < N; i++)
                        plan.AddFace(gb[i + 1, j], gb[i + 1, j + 1], gb[i, j + 1], gb[i, j]);
            }

            if (Vector3.Dot(n, -viewDir) < 0f) plan.ReverseAll();
            return true;
        }

        // ================================================================
        // 側面（三角・四角で共用）
        // ================================================================

        /// <summary>
        /// 表面の輪と裏面の輪の間に、奥行き方向 K 分割の側面を張る。
        /// 中間の段の枠は表面の枠を viewDir * depth * k / K だけずらして作る。
        /// 輪は表面の面と同じ向きにたどったもの。側面は輪の辺を逆向きにたどるので、
        /// 表面・裏面と向きがそろった閉じた面になる。
        /// </summary>
        private static void AddSides(
            PointDefinedMeshPlan plan, List<int> frontRing, List<int> backRing,
            Vector3 viewDir, float depth, int K)
        {
            int len = frontRing.Count;
            var layers = new List<int>[K + 1];
            layers[0] = frontRing;
            layers[K] = backRing;
            for (int k = 1; k < K; k++)
            {
                var layer = new List<int>(len);
                Vector3 off = viewDir * (depth * k / K);
                for (int r = 0; r < len; r++)
                    layer.Add(plan.AddNew(plan.SlotWorld[frontRing[r]] + off));
                layers[k] = layer;
            }

            for (int k = 0; k < K; k++)
            {
                var near = layers[k];
                var far  = layers[k + 1];
                for (int r = 0; r < len; r++)
                {
                    int a = r, bb = (r + 1) % len;
                    plan.AddFace(near[bb], near[a], far[a], far[bb]);
                }
            }
        }

        // ================================================================
        // 出力
        // ================================================================

        /// <summary>
        /// 書き込み先に既にある面の頂点集合。計画に「全頂点が既存頂点の面」が 1 枚も無ければ null。
        /// 同じ頂点集合の面は 2 重に張らないための照合に使う。
        /// </summary>
        public static HashSet<string> CollectFaceKeysIfNeeded(PointDefinedMeshPlan plan, MeshObject target)
        {
            if (plan == null || target == null) return null;

            bool need = false;
            foreach (var f in plan.Faces)
                if (AllExisting(plan, f)) { need = true; break; }
            if (!need) return null;

            var keys = new HashSet<string>();
            foreach (var face in target.Faces)
            {
                if (face?.VertexIndices == null || face.VertexIndices.Count < 3) continue;
                keys.Add(FaceKey(face.VertexIndices));
            }
            return keys;
        }

        /// <summary>プレビュー用。枠を全部新しい頂点（ワールド座標）にしたメッシュを作る。</summary>
        public static MeshObject ToPreviewMesh(
            PointDefinedMeshPlan plan, string name, HashSet<string> existingFaceKeys)
        {
            if (plan == null) return null;
            var mo = new MeshObject(name);
            var map = new int[plan.SlotCount];
            for (int i = 0; i < plan.SlotCount; i++)
                map[i] = mo.AddVertex(new Vertex(plan.SlotWorld[i]));
            AppendFaces(mo, plan, map, 0, existingFaceKeys);
            return mo;
        }

        /// <summary>
        /// 計画の面を dst へ足す。枠 → dst の頂点番号は slotToVertex。
        /// 法線は dst 上の座標から面ごとに求め、UV・法線の枠は GetOrAddUVNormal で取る
        /// （既存頂点の既存の枠は書き換えない）。
        /// 既存の面と同じ頂点集合になる面は足さない。
        /// </summary>
        /// <returns>足した面の数。</returns>
        public static int AppendFaces(
            MeshObject dst, PointDefinedMeshPlan plan, int[] slotToVertex,
            int materialIndex, HashSet<string> existingFaceKeys)
        {
            int added = 0;
            var pos = new List<Vector3>(4);

            foreach (var f in plan.Faces)
            {
                if (IsDuplicateOfExisting(plan, f, existingFaceKeys)) continue;

                var face = new Face();
                pos.Clear();
                for (int k = 0; k < f.Length; k++)
                {
                    int vi = slotToVertex[f[k]];
                    face.VertexIndices.Add(vi);
                    pos.Add(dst.Vertices[vi].Position);
                }
                if (!HasThreeDistinct(face.VertexIndices)) continue;

                Vector3 n = Newell(pos);
                n = n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;

                for (int k = 0; k < face.VertexIndices.Count; k++)
                {
                    int slot = dst.Vertices[face.VertexIndices[k]].GetOrAddUVNormal(Vector2.zero, n);
                    face.UVIndices.Add(slot);
                    face.NormalIndices.Add(slot);
                }

                face.MaterialIndex = materialIndex;
                dst.AddFace(face);
                added++;
            }
            return added;
        }

        /// <summary>頂点番号の並びを順不同で比べるための鍵。</summary>
        public static string FaceKey(IList<int> indices)
        {
            var arr = new int[indices.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = indices[i];
            Array.Sort(arr);
            return string.Join(",", arr);
        }

        /// <summary>Newell 法の面法線（正規化しない）。規約は Cross(p1-p0, p2-p0) と同じ向き。</summary>
        public static Vector3 Newell(IList<Vector3> pts)
        {
            Vector3 n = Vector3.zero;
            int c = pts.Count;
            for (int i = 0; i < c; i++)
            {
                Vector3 p0 = pts[i];
                Vector3 p1 = pts[(i + 1) % c];
                n.x += (p0.y - p1.y) * (p0.z + p1.z);
                n.y += (p0.z - p1.z) * (p0.x + p1.x);
                n.z += (p0.x - p1.x) * (p0.y + p1.y);
            }
            return n;
        }

        // ================================================================
        // 内部
        // ================================================================

        private static bool AllExisting(PointDefinedMeshPlan plan, int[] f)
        {
            foreach (int s in f)
                if (plan.SlotExisting[s] < 0) return false;
            return true;
        }

        private static bool IsDuplicateOfExisting(PointDefinedMeshPlan plan, int[] f, HashSet<string> keys)
        {
            if (keys == null || !AllExisting(plan, f)) return false;
            var idx = new int[f.Length];
            for (int i = 0; i < f.Length; i++) idx[i] = plan.SlotExisting[f[i]];
            return keys.Contains(FaceKey(idx));
        }

        private static bool HasThreeDistinct(List<int> indices)
        {
            var set = new HashSet<int>(indices);
            return set.Count >= 3 && set.Count == indices.Count;
        }

        /// <summary>面積がほぼ 0 とみなす閾値。図形の大きさに比例させる。</summary>
        private static float AreaEps(List<Vector3> pts)
        {
            float maxLen2 = 0f;
            for (int i = 0; i < pts.Count; i++)
                for (int j = i + 1; j < pts.Count; j++)
                    maxLen2 = Mathf.Max(maxLen2, (pts[i] - pts[j]).sqrMagnitude);
            return Mathf.Max(1e-10f, maxLen2 * 1e-6f);
        }

        private static bool CheckDistinct(PointDefinedCorner[] c, out string reason)
        {
            reason = null;
            for (int i = 0; i < c.Length; i++)
            {
                for (int j = i + 1; j < c.Length; j++)
                {
                    bool sameVertex = c[i].ExistingVertex >= 0 && c[i].ExistingVertex == c[j].ExistingVertex;
                    if (sameVertex || (c[i].World - c[j].World).sqrMagnitude < PositionEps * PositionEps)
                    {
                        reason = $"P{i} と P{j} が同じ位置です";
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>共有経路の形を確かめる。null は共有なしで常に通す。</summary>
        private static bool CheckPath(
            IReadOnlyList<PointDefinedCorner> path, int segments,
            PointDefinedCorner from, PointDefinedCorner to, out string reason)
        {
            reason = null;
            if (path == null) return true;
            if (path.Count != segments + 1)
            { reason = $"共有経路の頂点数 {path.Count} が分割数 {segments} + 1 と一致しません"; return false; }
            if (path[0].ExistingVertex != from.ExistingVertex || path[path.Count - 1].ExistingVertex != to.ExistingVertex)
            { reason = "共有経路の両端が指定点と一致しません"; return false; }
            return true;
        }

        /// <summary>視線に直交する平面へ投影して、向かい合う辺どうしが交差するかを見る。</summary>
        private static bool IsSelfIntersectingQuad(List<Vector3> q, Vector3 viewDir)
        {
            Vector3 d = viewDir.sqrMagnitude > 1e-12f ? viewDir.normalized : Vector3.forward;
            Vector3 e1 = Vector3.Cross(d, Mathf.Abs(d.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 e2 = Vector3.Cross(d, e1);

            Vector2 P(Vector3 v) => new Vector2(Vector3.Dot(v, e1), Vector3.Dot(v, e2));

            Vector2 a = P(q[0]), b = P(q[1]), c = P(q[2]), e = P(q[3]);
            return SegmentsCross(a, b, c, e) || SegmentsCross(b, c, e, a);
        }

        private static bool SegmentsCross(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            float d1 = Cross2(p4 - p3, p1 - p3);
            float d2 = Cross2(p4 - p3, p2 - p3);
            float d3 = Cross2(p2 - p1, p3 - p1);
            float d4 = Cross2(p2 - p1, p4 - p1);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f))
                && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross2(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
