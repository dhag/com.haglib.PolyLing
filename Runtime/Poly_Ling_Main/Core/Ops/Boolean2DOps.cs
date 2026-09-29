// Boolean2DOps.cs
// 同じ平面上の図形どうしのブーリアン演算（和 / 差 / 積 / 排他的論理和）。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 実体の多角形演算は Runtime/ThirdParty/Clipper2Lib（Clipper2, Boost Software License）。
// ここは「3D の点 ⇄ 平面の 2D 整数座標」の変換と、入力・出力の形の組み立てだけを担う。
//
// 【入力の形は 3 つ】
//   ・面   … 3 頂点以上の面を 1 枚ずつ多角形にし、向きをそろえて NonZero で和を取った領域。
//            面の表裏が混ざっていても打ち消し合わない。
//   ・線分 … 閉じたループ（LineProfileExtractor.ExtractLoops）を EvenOdd で重ねた領域。
//            内側のループが穴になる。
//   ・輪郭 … 2D押し出しのループ。XY 平面で演算し、z は元の辺から補間する。
//
// 【平面】
//   法線は A の多角形の Newell 和（面の向きのまま足す）。0 なら B から取る。
//   全点がこの平面から tolerance 以内に無ければ失敗にする（射影して無理に演算しない）。
//
// 【座標の精度】
//   Clipper2 の整数座標へ Scale 倍して丸める。1/Scale より細かい差は失われる。

using System;
using System.Collections.Generic;
using UnityEngine;
using Clipper2Lib;
using Poly_Ling.Data;
using Poly_Ling.Profile2DExtrude;
using Poly_Ling.PrimitiveMesh;
// Clipper2Lib にも internal class Vertex があるため、PolyLing 側を明示する。
using Vertex = Poly_Ling.Data.Vertex;
using Face   = Poly_Ling.Data.Face;
using Loop   = Poly_Ling.Profile2DExtrude.Loop;

namespace Poly_Ling.Ops
{
    /// <summary>2D ブーリアン演算の種類。</summary>
    public enum Boolean2DOpKind
    {
        /// <summary>和（A ∪ B）</summary>
        Union = 0,
        /// <summary>差（A − B）</summary>
        Subtract = 1,
        /// <summary>積（A ∩ B）</summary>
        Intersect = 2,
        /// <summary>排他的論理和（どちらか一方だけ）</summary>
        Xor = 3,
    }

    /// <summary>2D ブーリアンの入力にするもの。</summary>
    public enum Boolean2DSource
    {
        /// <summary>3 頂点以上の面。結果は三角形の面。</summary>
        Faces = 0,
        /// <summary>線分（2 頂点の面）の閉じたループ。結果は線分と線分群。</summary>
        Lines = 1,
    }

    /// <summary>平面上の 2D 座標系。</summary>
    public struct Boolean2DPlane
    {
        public Vector3 Origin;
        public Vector3 U;
        public Vector3 V;
        /// <summary>単位法線。Cross(U, V) と同じ向き。</summary>
        public Vector3 Normal;

        public Vector2 To2D(Vector3 p)
        {
            var d = p - Origin;
            return new Vector2(Vector3.Dot(d, U), Vector3.Dot(d, V));
        }

        public Vector3 To3D(double u, double v) => Origin + U * (float)u + V * (float)v;

        public float Distance(Vector3 p) => Vector3.Dot(p - Origin, Normal);
    }

    /// <summary>2D ブーリアンの結果の輪郭 1 本。</summary>
    public sealed class Boolean2DLoop
    {
        public List<Vector3> Points = new List<Vector3>();
        public bool IsHole;
    }

    public static class Boolean2DOps
    {
        /// <summary>2D 座標を整数へ直す倍率。1/Scale が座標の最小単位。</summary>
        public const double Scale = 1e6;

        /// <summary>同一平面とみなす距離の既定値（演算空間の単位）。</summary>
        public const float DefaultPlaneTolerance = 1e-4f;

        public static string DisplayName(Boolean2DOpKind op)
        {
            switch (op)
            {
                case Boolean2DOpKind.Union:     return "和";
                case Boolean2DOpKind.Subtract:  return "差";
                case Boolean2DOpKind.Intersect: return "積";
                case Boolean2DOpKind.Xor:       return "排他的論理和";
                default:                        return op.ToString();
            }
        }

        private static ClipType ToClipType(Boolean2DOpKind op)
        {
            switch (op)
            {
                case Boolean2DOpKind.Union:     return ClipType.Union;
                case Boolean2DOpKind.Subtract:  return ClipType.Difference;
                case Boolean2DOpKind.Intersect: return ClipType.Intersection;
                case Boolean2DOpKind.Xor:       return ClipType.Xor;
                default:                        return ClipType.NoClip;
            }
        }

        // ================================================================
        // 平面
        // ================================================================

        /// <summary>多角形の Newell 和（面積 × 2 × 法線）。向きは点の並びのまま。</summary>
        public static Vector3 NewellSum(IReadOnlyList<Vector3> poly)
        {
            var n = Vector3.zero;
            if (poly == null) return n;
            int c = poly.Count;
            for (int i = 0; i < c; i++)
                n += Vector3.Cross(poly[i], poly[(i + 1) % c]);
            return n;
        }

        /// <summary>
        /// 法線と点群から平面を作り、全点が tolerance 以内に乗っているかを確かめる。
        /// 原点は点群の重心。
        /// </summary>
        public static bool TryMakePlane(Vector3 normalSum, IReadOnlyList<Vector3> points, float tolerance,
                                        out Boolean2DPlane plane, out string reason)
        {
            plane = default;
            reason = null;
            if (points == null || points.Count < 3) { reason = "点が足りません"; return false; }
            if (normalSum.sqrMagnitude < 1e-20f) { reason = "面積が 0 で平面を決められません"; return false; }

            var n = normalSum.normalized;
            var o = Vector3.zero;
            foreach (var p in points) o += p;
            o /= points.Count;

            // U は法線と最も直交に近い座標軸から作る。
            Vector3 axis = Mathf.Abs(n.x) < 0.9f ? Vector3.right : Vector3.up;
            var u = Vector3.Cross(axis, n).normalized;
            var v = Vector3.Cross(n, u).normalized;   // Cross(u, v) = n
            plane = new Boolean2DPlane { Origin = o, U = u, V = v, Normal = n };

            float maxDist = 0f;
            foreach (var p in points) maxDist = Mathf.Max(maxDist, Mathf.Abs(plane.Distance(p)));
            if (maxDist > tolerance)
            {
                reason = $"同じ平面に乗っていません（平面からの最大距離 {maxDist:G4} > 許容量 {tolerance:G4}）";
                return false;
            }
            return true;
        }

        // ================================================================
        // 2D 整数座標
        // ================================================================

        private static Point64 ToPoint64(Vector2 p) => new Point64(p.x * Scale, p.y * Scale);

        private static Path64 ToPath64(Boolean2DPlane plane, IReadOnlyList<Vector3> poly)
        {
            var path = new Path64(poly.Count);
            foreach (var p in poly) path.Add(ToPoint64(plane.To2D(p)));
            return path;
        }

        /// <summary>
        /// 面の多角形群を領域にする。各多角形を正の向きにそろえ、NonZero で和を取る。
        /// </summary>
        public static Paths64 RegionFromFaces(Boolean2DPlane plane, IEnumerable<IReadOnlyList<Vector3>> polys)
        {
            var paths = new Paths64();
            foreach (var poly in polys)
            {
                if (poly == null || poly.Count < 3) continue;
                var path = ToPath64(plane, poly);
                double a = Clipper.Area(path);
                if (a == 0) continue;
                if (a < 0) path.Reverse();
                paths.Add(path);
            }
            return Clipper.Union(paths, FillRule.NonZero);
        }

        /// <summary>閉じたループ群を領域にする。EvenOdd で重ねるので内側のループは穴になる。</summary>
        public static Paths64 RegionFromLoops(Boolean2DPlane plane, IEnumerable<IReadOnlyList<Vector3>> loops)
        {
            var paths = new Paths64();
            foreach (var lp in loops)
            {
                if (lp == null || lp.Count < 3) continue;
                paths.Add(ToPath64(plane, lp));
            }
            return Clipper.Union(paths, FillRule.EvenOdd);
        }

        /// <summary>2 つの領域を演算し、外周と穴の木を返す。</summary>
        public static PolyTree64 Execute(Boolean2DOpKind op, Paths64 a, Paths64 b)
        {
            var tree = new PolyTree64();
            var c = new Clipper64();
            c.AddPaths(a, PathType.Subject);
            c.AddPaths(b, PathType.Clip);
            c.Execute(ToClipType(op), FillRule.NonZero, tree);
            return tree;
        }

        // ================================================================
        // 結果 → 輪郭
        // ================================================================

        /// <summary>木の全ての輪郭（外周・穴）を平面上の 3D 点列にして返す。</summary>
        public static List<Boolean2DLoop> TreeToLoops(PolyTree64 tree, Func<Point64, Vector3> lift)
        {
            var result = new List<Boolean2DLoop>();
            CollectLoops(tree, lift, result);
            return result;
        }

        private static void CollectLoops(PolyPath64 node, Func<Point64, Vector3> lift, List<Boolean2DLoop> dst)
        {
            for (int i = 0; i < node.Count; i++)
            {
                var ch = node[i];
                if (ch.Polygon != null && ch.Polygon.Count >= 3)
                {
                    var lp = new Boolean2DLoop { IsHole = ch.IsHole };
                    foreach (var q in ch.Polygon) lp.Points.Add(lift(q));
                    dst.Add(lp);
                }
                CollectLoops(ch, lift, dst);
            }
        }

        /// <summary>平面の点へ戻す。</summary>
        public static Func<Point64, Vector3> PlaneLift(Boolean2DPlane plane)
            => q => plane.To3D(q.X / Scale, q.Y / Scale);

        // ================================================================
        // 結果 → 三角形の面
        // ================================================================

        /// <summary>
        /// 木を三角形に分けて MeshObject にする。外周ごとに直下の穴と一緒に分割する。
        /// 三角形の向きは frontNormal と同じ側を表にする。
        /// UV は平面の 2D 座標、法線は平面の法線。
        /// </summary>
        public static bool TreeToFaceMesh(PolyTree64 tree, Boolean2DPlane plane, Vector3 frontNormal,
                                          int materialIndex, string name,
                                          out MeshObject mesh, out string reason)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(name) ? "Boolean2D" : name);
            mesh = mo;
            reason = null;

            var index = new Dictionary<(long, long), int>();
            Vector3 nrm = frontNormal.sqrMagnitude > 0f ? frontNormal.normalized : plane.Normal;

            int VertexOf(Point64 q)
            {
                var key = (q.X, q.Y);
                if (index.TryGetValue(key, out int vi)) return vi;
                double u = q.X / Scale, v = q.Y / Scale;
                vi = mo.Vertices.Count;
                mo.Vertices.Add(new Vertex(plane.To3D(u, v), new Vector2((float)u, (float)v), nrm));
                index[key] = vi;
                return vi;
            }

            var outers = new List<PolyPath64>();
            CollectOuters(tree, outers);
            if (outers.Count == 0) { reason = "結果が空になりました（重なっていない可能性があります）"; return false; }

            foreach (var outer in outers)
            {
                var pp = new Paths64 { outer.Polygon };
                for (int i = 0; i < outer.Count; i++)
                    if (outer[i].Polygon != null && outer[i].Polygon.Count >= 3) pp.Add(outer[i].Polygon);

                var res = Clipper.Triangulate(pp, out Paths64 tris);
                if (res != TriangulateResult.success)
                {
                    reason = $"三角形に分けられませんでした（{res}）";
                    return false;
                }

                foreach (var t in tris)
                {
                    if (t.Count != 3) continue;
                    int a = VertexOf(t[0]), b = VertexOf(t[1]), c = VertexOf(t[2]);
                    if (a == b || b == c || c == a) continue;

                    var pa = mesh.Vertices[a].Position;
                    var pb = mesh.Vertices[b].Position;
                    var pc = mesh.Vertices[c].Position;
                    if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), nrm) < 0f) { int tmp = b; b = c; c = tmp; }

                    var f = new Face { MaterialIndex = materialIndex };
                    foreach (int vi in new[] { a, b, c })
                    {
                        f.VertexIndices.Add(vi); f.UVIndices.Add(0); f.NormalIndices.Add(0);
                    }
                    mesh.Faces.Add(f);
                }
            }

            if (mesh.FaceCount == 0) { reason = "結果が空になりました（重なっていない可能性があります）"; return false; }
            return true;
        }

        /// <summary>外周（穴でない輪郭）を木の全段から集める。穴の中の島も外周として拾う。</summary>
        private static void CollectOuters(PolyPath64 node, List<PolyPath64> dst)
        {
            for (int i = 0; i < node.Count; i++)
            {
                var ch = node[i];
                if (!ch.IsHole && ch.Polygon != null && ch.Polygon.Count >= 3) dst.Add(ch);
                CollectOuters(ch, dst);
            }
        }

        // ================================================================
        // メッシュ 2 つ
        // ================================================================

        /// <summary>メッシュ 2 つの演算結果。</summary>
        public struct MeshResult
        {
            public bool Success;
            public string Message;
            public MeshObject Mesh;
            /// <summary>結果の輪郭の数（外周と穴）。</summary>
            public int LoopCount;
            /// <summary>そのうち穴の数。</summary>
            public int HoleCount;
        }

        /// <summary>
        /// 同じ平面上にあるメッシュ 2 つを演算し、新しい MeshObject を返す。入力は変えない。
        /// b の点は bToA で a のローカルへ写してから演算する。結果は a のローカル。
        /// </summary>
        public static MeshResult PerformMeshes(Boolean2DOpKind op, Boolean2DSource source,
                                               MeshObject a, MeshObject b, Matrix4x4 bToA,
                                               float planeTolerance, string resultName)
        {
            if (a == null || b == null) return MeshFail("メッシュが指定されていません");
            if (a.IsSkinnedKind || b.IsSkinnedKind) return MeshFail("スキンドメッシュは対象にできません（ボーンウェイトが失われるため）");
            if (float.IsNaN(planeTolerance) || planeTolerance < 0f) return MeshFail("平面の許容量は 0 以上にしてください");

            List<List<Vector3>> polysA, polysB;
            if (source == Boolean2DSource.Faces)
            {
                polysA = CollectFacePolygons(a, Matrix4x4.identity);
                polysB = CollectFacePolygons(b, bToA);
                if (polysA.Count == 0) return MeshFail("A に 3 頂点以上の面がありません");
                if (polysB.Count == 0) return MeshFail("B に 3 頂点以上の面がありません");
            }
            else
            {
                polysA = CollectLineLoops(a, Matrix4x4.identity);
                polysB = CollectLineLoops(b, bToA);
                if (polysA.Count == 0) return MeshFail("A に閉じた線分のループがありません");
                if (polysB.Count == 0) return MeshFail("B に閉じた線分のループがありません");
            }

            // 法線：A の Newell 和。0 なら B。
            var nA = Vector3.zero; foreach (var p in polysA) nA += NewellSum(p);
            var nB = Vector3.zero; foreach (var p in polysB) nB += NewellSum(p);
            if (source == Boolean2DSource.Lines)
            {
                // 線分のループは向きがそろっていないので、絶対値の大きい向きへそろえて足す。
                nA = OrientedSum(polysA);
                nB = OrientedSum(polysB);
            }
            var nSum = nA.sqrMagnitude > 1e-20f ? nA : nB;

            var all = new List<Vector3>();
            foreach (var p in polysA) all.AddRange(p);
            foreach (var p in polysB) all.AddRange(p);
            if (!TryMakePlane(nSum, all, planeTolerance, out var plane, out string why)) return MeshFail(why);

            Paths64 ra, rb;
            if (source == Boolean2DSource.Faces)
            {
                ra = RegionFromFaces(plane, polysA);
                rb = RegionFromFaces(plane, polysB);
            }
            else
            {
                ra = RegionFromLoops(plane, polysA);
                rb = RegionFromLoops(plane, polysB);
            }

            var tree = Execute(op, ra, rb);
            string name = string.IsNullOrEmpty(resultName)
                ? $"{a.Name}_{DisplayName(op)}_{b.Name}"
                : resultName;

            var loops = TreeToLoops(tree, PlaneLift(plane));
            int holes = 0; foreach (var lp in loops) if (lp.IsHole) holes++;
            if (loops.Count == 0) return MeshFail("結果が空になりました（重なっていない可能性があります）");

            MeshObject mesh;
            if (source == Boolean2DSource.Faces)
            {
                int mat = a.FaceCount > 0 ? FirstPolygonMaterial(a) : 0;
                if (!TreeToFaceMesh(tree, plane, nSum, mat, name, out mesh, out string r2)) return MeshFail(r2);
            }
            else
            {
                var profLoops = new List<Loop>();
                foreach (var lp in loops) profLoops.Add(new Loop { Points = lp.Points, IsHole = lp.IsHole });
                mesh = LineProfileExtractor.LoopsToLineMesh(profLoops, name);
                if (mesh == null || mesh.FaceCount == 0) return MeshFail("結果が空になりました");
            }

            return new MeshResult { Success = true, Mesh = mesh, LoopCount = loops.Count, HoleCount = holes };
        }

        private static MeshResult MeshFail(string msg) => new MeshResult { Success = false, Message = msg };

        private static int FirstPolygonMaterial(MeshObject mo)
        {
            foreach (var f in mo.Faces) if (f.VertexCount >= 3) return f.MaterialIndex;
            return 0;
        }

        private static Vector3 OrientedSum(List<List<Vector3>> polys)
        {
            Vector3 best = Vector3.zero;
            foreach (var p in polys) { var n = NewellSum(p); if (n.sqrMagnitude > best.sqrMagnitude) best = n; }
            var sum = Vector3.zero;
            foreach (var p in polys) { var n = NewellSum(p); sum += Vector3.Dot(n, best) < 0f ? -n : n; }
            return sum;
        }

        /// <summary>3 頂点以上の面を多角形（m で写した点列）にする。</summary>
        public static List<List<Vector3>> CollectFacePolygons(MeshObject mo, Matrix4x4 m)
        {
            var list = new List<List<Vector3>>();
            foreach (var f in mo.Faces)
            {
                if (f.VertexCount < 3) continue;
                var poly = new List<Vector3>(f.VertexCount);
                foreach (int vi in f.VertexIndices)
                    if (vi >= 0 && vi < mo.Vertices.Count) poly.Add(m.MultiplyPoint3x4(mo.Vertices[vi].Position));
                if (poly.Count >= 3) list.Add(poly);
            }
            return list;
        }

        /// <summary>線分の閉じたループを点列（m で写した点列）にする。</summary>
        public static List<List<Vector3>> CollectLineLoops(MeshObject mo, Matrix4x4 m)
        {
            var list = new List<List<Vector3>>();
            var loops = LineProfileExtractor.ExtractLoops(mo, LineProfileExtractor.CollectLineFaceIndices(mo));
            foreach (var lp in loops)
            {
                if (lp?.Points == null || lp.Points.Count < 3) continue;
                var poly = new List<Vector3>(lp.Points.Count);
                foreach (var p in lp.Points) poly.Add(m.MultiplyPoint3x4(p));
                list.Add(poly);
            }
            return list;
        }

        // ================================================================
        // 2D押し出しの輪郭
        // ================================================================

        /// <summary>
        /// 2D押し出しの輪郭どうしを XY 平面で演算する。
        /// a・b はそれぞれループの集まりで、EvenOdd で重ねた領域として扱う（IsHole は見ない）。
        /// 結果は外周が反時計回り（IsHole = false）、穴が時計回り（IsHole = true）。
        /// z は結果の点に最も近い入力の辺から補間する。
        /// </summary>
        public static List<Loop> PerformProfileLoops(Boolean2DOpKind op, IList<Loop> a, IList<Loop> b)
        {
            var plane = new Boolean2DPlane
            {
                Origin = Vector3.zero, U = Vector3.right, V = Vector3.up, Normal = Vector3.forward,
            };

            var la = new List<IReadOnlyList<Vector3>>();
            var lb = new List<IReadOnlyList<Vector3>>();
            var segs = new List<(Vector3, Vector3)>();
            void Gather(IList<Loop> src, List<IReadOnlyList<Vector3>> dst)
            {
                if (src == null) return;
                foreach (var lp in src)
                {
                    if (lp?.Points == null || lp.Points.Count < 3) continue;
                    dst.Add(lp.Points);
                    for (int i = 0; i < lp.Points.Count; i++)
                        segs.Add((lp.Points[i], lp.Points[(i + 1) % lp.Points.Count]));
                }
            }
            Gather(a, la);
            Gather(b, lb);

            var tree = Execute(op, RegionFromLoops(plane, la), RegionFromLoops(plane, lb));

            Vector3 Lift(Point64 q)
            {
                var p = new Vector2((float)(q.X / Scale), (float)(q.Y / Scale));
                return new Vector3(p.x, p.y, InterpolateZ(p, segs));
            }

            var result = new List<Loop>();
            foreach (var lp in TreeToLoops(tree, Lift))
            {
                // 外周は反時計回り、穴は時計回りにそろえる（LineProfileExtractor.ExtractLoops と同じ規約）。
                float area2 = 0f;
                for (int i = 0; i < lp.Points.Count; i++)
                {
                    var p0 = lp.Points[i]; var p1 = lp.Points[(i + 1) % lp.Points.Count];
                    area2 += p0.x * p1.y - p1.x * p0.y;
                }
                bool ccw = area2 > 0f;
                if (ccw == lp.IsHole) lp.Points.Reverse();
                result.Add(new Loop { Points = lp.Points, IsHole = lp.IsHole });
            }
            return result;
        }

        /// <summary>XY で最も近い辺の上の z を返す。辺が無ければ 0。</summary>
        private static float InterpolateZ(Vector2 p, List<(Vector3 a, Vector3 b)> segs)
        {
            float best = float.MaxValue, z = 0f;
            foreach (var (a, b) in segs)
            {
                var a2 = new Vector2(a.x, a.y); var b2 = new Vector2(b.x, b.y);
                var ab = b2 - a2;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a2, ab) / len2) : 0f;
                float d = (a2 + ab * t - p).sqrMagnitude;
                if (d < best) { best = d; z = Mathf.Lerp(a.z, b.z, t); }
            }
            return z;
        }
    }
}
