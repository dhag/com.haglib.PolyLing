// EmblemMeshGenerator.cs
// 紋章図形（五芒星・六芒星・クロス・八芒星）のメッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【図形】中心線を閉じた多角形の組で持つ（半径 1・頂点が真上の向きで組み、大きさと回転を掛ける）。
//   五芒星   : 正五角形の頂点を 1 つ飛ばしに結んだ自己交差の 1 本
//   六芒星   : 正三角形 2 枚
//   クロス   : 腕の太さを持つ十字の外形（12 角形）
//   八芒星   : 正方形と、それを 45 度回した正方形の 2 枚
// 【描き方】
//   塗り : 多角形の組を非ゼロ規則で合体した領域（五芒星は中央の五角形も埋まる）
//   線   : 多角形の各辺を線の太さの帯にして合体した領域（魔法陣風）。
//          頂点ごとに「前の辺の中点 → 頂点 → 次の辺の中点」の折れ線を、角はとがり・端は平らで太らせる。
//          隣り合う折れ線は辺の中点で同じ切り口を共有するので、合体すると継ぎ目なくつながる。
// 【角の丸め】合体後の輪郭を「縮めて戻す」（凸の角）・「広げて戻す」（凹の角）で丸める。
// 【立体化】星座マークと同じく 2D 押し出し生成器（Profile2DExtrudeMeshGenerator）へ渡す。
//   厚み・表裏の面取り／ラウンドはそちらの機能をそのまま使う。
// 【周囲の円】図形とは別に輪郭化・生成して連結し、パーツ ID を分ける（図形 0、円 1）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Clipper2Lib;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Profile2DExtrude;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>紋章図形の種類。</summary>
    public enum EmblemKind { Pentagram, Hexagram, Cross, Octagram }

    /// <summary>紋章図形の描き方。</summary>
    public enum EmblemStyle { Fill, Line }

    public static class EmblemMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct EmblemParams : IEquatable<EmblemParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>大きさ（外接円の半径）。</summary>
            public const float SizeMin = 0.01f;
            public const float SizeMax = 10f;

            public const float RotationMin = 0f;
            public const float RotationMax = 360f;

            /// <summary>線の太さ（大きさに対する比）。</summary>
            public const float LineWidthMin = 0.01f;
            public const float LineWidthMax = 0.5f;

            /// <summary>クロスの腕の太さ（大きさに対する比）。</summary>
            public const float CrossArmWidthMin = 0.05f;
            public const float CrossArmWidthMax = 1.5f;

            /// <summary>角の丸め半径（大きさに対する比）。0 で丸めない。</summary>
            public const float CornerRadiusMin = 0f;
            public const float CornerRadiusMax = 0.3f;

            public const int SegmentsMin = 1;
            public const int SegmentsMax = 16;

            public const float ThicknessMin = 0f;
            public const float ThicknessMax = 3f;

            public const int EdgeSegmentsMin = 0;
            public const int EdgeSegmentsMax = 16;

            public const float EdgeSizeMin = 0.001f;
            public const float EdgeSizeMax = 0.5f;

            /// <summary>円の半径（大きさに対する比）。</summary>
            public const float RingRadiusMin = 0.3f;
            public const float RingRadiusMax = 2f;

            /// <summary>円の線の太さ（大きさに対する比）。</summary>
            public const float RingWidthMin = 0.01f;
            public const float RingWidthMax = 0.5f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            [PLParam(TextKey = "EmblemKind", Description = "図形（Pentagram=五芒星 / Hexagram=六芒星 / Cross=クロス / Octagram=八芒星）")]
            public EmblemKind Kind;

            [PLParam(TextKey = "EmblemStyle", Description = "描き方（Fill=塗り / Line=線）")]
            public EmblemStyle Style;

            [PLParam(TextKey = "EmblemSize", Description = "大きさ（外接円の半径）", Min = SizeMin, Max = SizeMax)]
            public float Size;

            [PLParam(TextKey = "EmblemRotation", Description = "回転（度）。0 で頂点が真上", Min = RotationMin, Max = RotationMax)]
            public float Rotation;

            [PLParam(TextKey = "EmblemLineWidth", Description = "線の太さ（描き方が線のとき）。大きさに対する比",
                     Min = LineWidthMin, Max = LineWidthMax)]
            public float LineWidth;

            [PLParam(TextKey = "EmblemCrossArmWidth", Description = "クロスの腕の太さ。大きさに対する比",
                     Min = CrossArmWidthMin, Max = CrossArmWidthMax)]
            public float CrossArmWidth;

            [PLParam(TextKey = "EmblemCornerRadius", Description = "角の丸め半径。大きさに対する比。0 で丸めない",
                     Min = CornerRadiusMin, Max = CornerRadiusMax)]
            public float CornerRadius;

            [PLParam(TextKey = "Segments", Description = "曲線の分割数（円弧 90° あたり）", Min = SegmentsMin,
                     Max = SegmentsMax, Step = 1)]
            public int Segments;

            [PLParam(TextKey = "ZodiacRing", Description = "図形の周囲に円を描く")]
            public bool Ring;
            [PLParam(TextKey = "ZodiacRingRadius", Description = "円の半径（線の中心）。大きさに対する比",
                     Min = RingRadiusMin, Max = RingRadiusMax)]
            public float RingRadius;
            [PLParam(TextKey = "ZodiacRingWidth", Description = "円の線の太さ。大きさに対する比",
                     Min = RingWidthMin, Max = RingWidthMax)]
            public float RingWidth;

            [PLParam(TextKey = "Thickness", Description = "押し出しの厚み。0 で板", Min = ThicknessMin, Max = ThicknessMax)]
            public float Thickness;
            [PLParam(TextKey = "FrontSegments", Description = "表側エッジの分割数（0=無効 / 1=面取り / 2以上=ラウンド）",
                     Min = EdgeSegmentsMin, Max = EdgeSegmentsMax, Step = 1)]
            public int SegmentsFront;
            [PLParam(TextKey = "BackSegments", Description = "裏側エッジの分割数（0=無効 / 1=面取り / 2以上=ラウンド）",
                     Min = EdgeSegmentsMin, Max = EdgeSegmentsMax, Step = 1)]
            public int SegmentsBack;
            [PLParam(TextKey = "EdgeSize", Description = "表側エッジのサイズ。線の太さの半分未満に抑える", Min = EdgeSizeMin,
                     Max = EdgeSizeMax)]
            public float EdgeSizeFront;
            [PLParam(TextKey = "EdgeSize", Description = "裏側エッジのサイズ。線の太さの半分未満に抑える", Min = EdgeSizeMin,
                     Max = EdgeSizeMax)]
            public float EdgeSizeBack;
            [PLParam(TextKey = "EdgeInward", Description = "ラウンドの曲率方向を入れ替える")]
            public bool EdgeInward;

            [PLParam(TextKey = "Orientation", Description = "板の向き（XY / XZ / YZ）")]
            public PlaneOrientation Orientation;
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static EmblemParams Default => new EmblemParams
            {
                MeshName      = "Emblem",
                Kind          = EmblemKind.Pentagram,
                Style         = EmblemStyle.Line,
                Size          = 0.5f,
                Rotation      = 0f,
                LineWidth     = 0.08f,
                CrossArmWidth = 0.4f,
                CornerRadius  = 0f,
                Segments      = 6,
                Ring          = false,
                RingRadius    = 1.1f,
                RingWidth     = 0.06f,
                Thickness     = 0.05f,
                SegmentsFront = 0,
                SegmentsBack  = 0,
                EdgeSizeFront = 0.01f,
                EdgeSizeBack  = 0.01f,
                EdgeInward    = false,
                Orientation   = PlaneOrientation.XY,
                FlipFaces     = false,
                Pivot         = Vector3.zero,
            };

            public bool Equals(EmblemParams o) =>
                MeshName == o.MeshName &&
                Kind == o.Kind &&
                Style == o.Style &&
                Mathf.Approximately(Size, o.Size) &&
                Mathf.Approximately(Rotation, o.Rotation) &&
                Mathf.Approximately(LineWidth, o.LineWidth) &&
                Mathf.Approximately(CrossArmWidth, o.CrossArmWidth) &&
                Mathf.Approximately(CornerRadius, o.CornerRadius) &&
                Segments == o.Segments &&
                Ring == o.Ring &&
                Mathf.Approximately(RingRadius, o.RingRadius) &&
                Mathf.Approximately(RingWidth, o.RingWidth) &&
                Mathf.Approximately(Thickness, o.Thickness) &&
                SegmentsFront == o.SegmentsFront &&
                SegmentsBack == o.SegmentsBack &&
                Mathf.Approximately(EdgeSizeFront, o.EdgeSizeFront) &&
                Mathf.Approximately(EdgeSizeBack, o.EdgeSizeBack) &&
                EdgeInward == o.EdgeInward &&
                Orientation == o.Orientation &&
                FlipFaces == o.FlipFaces &&
                Pivot == o.Pivot;

            public override bool Equals(object obj) => obj is EmblemParams p && Equals(p);
            public override int GetHashCode() => (MeshName?.GetHashCode() ?? 0) * 31 + (int)Kind * 2 + (int)Style;
        }

        /// <summary>Clipper2 の小数桁。1e-5 単位で丸める。</summary>
        private const int ClipPrecision = 5;

        /// <summary>線の角をとがらせるときのミター上限（線の半幅に対する倍率）。五芒星の先端（36°）を切らない値。</summary>
        private const double MiterLimit = 8.0;

        // ================================================================
        // 生成
        // ================================================================

        public static MeshObject Generate(EmblemParams p)
        {
            float size = Mathf.Clamp(p.Size, EmblemParams.SizeMin, EmblemParams.SizeMax);
            int seg = Mathf.Clamp(p.Segments, EmblemParams.SegmentsMin, EmblemParams.SegmentsMax);

            var polys = BuildCenterPolygons(p.Kind, size,
                Mathf.Clamp(p.CrossArmWidth, EmblemParams.CrossArmWidthMin, EmblemParams.CrossArmWidthMax),
                p.Rotation);
            if (polys.Count == 0) return null;

            float corner = size * Mathf.Clamp(p.CornerRadius, EmblemParams.CornerRadiusMin, EmblemParams.CornerRadiusMax);

            PathsD shape;
            float minHalf;
            if (p.Style == EmblemStyle.Line)
            {
                float lineW = size * Mathf.Clamp(p.LineWidth, EmblemParams.LineWidthMin, EmblemParams.LineWidthMax);
                double half = lineW * 0.5;
                shape = LineRegion(polys, half);
                minHalf = (float)half;
                // 線の半幅以上で縮めると線が消えるので、半幅の 9 割で止める。
                corner = Mathf.Min(corner, minHalf * 0.9f);
            }
            else
            {
                shape = FillRegion(polys);
                minHalf = float.MaxValue;
                if (p.Kind == EmblemKind.Cross)
                {
                    float armHalf = size * Mathf.Clamp(p.CrossArmWidth,
                        EmblemParams.CrossArmWidthMin, EmblemParams.CrossArmWidthMax) * 0.5f;
                    minHalf = armHalf;
                    corner = Mathf.Min(corner, armHalf * 0.9f);
                }
            }

            if (corner > 1e-6f)
                shape = RoundCorners(shape, corner, seg);

            var shapeLoops = ToLoops(shape);

            List<Loop> ringLoops = null;
            if (p.Ring)
            {
                float rr = size * Mathf.Clamp(p.RingRadius, EmblemParams.RingRadiusMin, EmblemParams.RingRadiusMax);
                float rw = size * Mathf.Clamp(p.RingWidth, EmblemParams.RingWidthMin, EmblemParams.RingWidthMax);
                ringLoops = ToLoops(RingRegion(rr, rw * 0.5, seg));
                minHalf = Mathf.Min(minHalf, rw * 0.5f);
            }

            // ベベルのインセットが線の半幅を超えると輪郭が潰れるので、半幅の 9 割で止める。
            float edgeCap = minHalf < float.MaxValue
                ? Mathf.Max(EmblemParams.EdgeSizeMin, minHalf * 0.9f)
                : EmblemParams.EdgeSizeMax;

            var gp = new Profile2DGenerateParams
            {
                Scale         = 1f,
                Offset        = Vector2.zero,
                FlipY         = false,
                Thickness     = Mathf.Clamp(p.Thickness, EmblemParams.ThicknessMin, EmblemParams.ThicknessMax),
                SegmentsFront = Mathf.Clamp(p.SegmentsFront, EmblemParams.EdgeSegmentsMin, EmblemParams.EdgeSegmentsMax),
                SegmentsBack  = Mathf.Clamp(p.SegmentsBack,  EmblemParams.EdgeSegmentsMin, EmblemParams.EdgeSegmentsMax),
                EdgeSizeFront = Mathf.Clamp(p.EdgeSizeFront, EmblemParams.EdgeSizeMin, edgeCap),
                EdgeSizeBack  = Mathf.Clamp(p.EdgeSizeBack,  EmblemParams.EdgeSizeMin, edgeCap),
                EdgeInward    = p.EdgeInward,
                SymmetryMode  = false,
            };

            MeshObject result = null;
            AppendPart(ref result, shapeLoops, p.MeshName, gp, 0);
            AppendPart(ref result, ringLoops, p.MeshName, gp, 1);
            if (result == null) return null;

            GearDiskBuilder.ApplyOrientation(result, p.Orientation);
            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(result);
            PrimitiveMeshPostProcess.ApplyPivotOffset(result, p.Pivot);
            result.InvalidatePositionCache();
            return result;
        }

        private static void AppendPart(ref MeshObject result, List<Loop> loops, string meshName,
            Profile2DGenerateParams gp, int partsId)
        {
            if (loops == null || loops.Count == 0) return;
            var one = Profile2DExtrudeMeshGenerator.Generate(loops, string.IsNullOrEmpty(meshName) ? "Emblem" : meshName, gp);
            if (one == null || one.VertexCount == 0) return;

            PartsIdOps.SetPartsId(one, partsId);
            if (result == null) result = one;
            else MeshObjectAppendOps.Append(result, one, copyNormals: true);
        }

        // ================================================================
        // 中心線の多角形
        // ================================================================

        /// <summary>半径 size・頂点が真上の向きで中心線の閉じた多角形を組み、rotationDeg だけ回す。</summary>
        private static List<List<Vector2>> BuildCenterPolygons(EmblemKind kind, float size, float armRatio, float rotationDeg)
        {
            var polys = new List<List<Vector2>>();
            switch (kind)
            {
                case EmblemKind.Pentagram:
                {
                    // 正五角形の頂点を 1 つ飛ばしに結ぶ（{5/2}）。
                    var ring = RegularPoints(5, size);
                    var path = new List<Vector2>(5);
                    for (int i = 0; i < 5; i++) path.Add(ring[(i * 2) % 5]);
                    polys.Add(path);
                    break;
                }
                case EmblemKind.Hexagram:
                {
                    var ring = RegularPoints(6, size);
                    polys.Add(new List<Vector2> { ring[0], ring[2], ring[4] });
                    polys.Add(new List<Vector2> { ring[1], ring[3], ring[5] });
                    break;
                }
                case EmblemKind.Octagram:
                {
                    var ring = RegularPoints(8, size);
                    polys.Add(new List<Vector2> { ring[0], ring[2], ring[4], ring[6] });
                    polys.Add(new List<Vector2> { ring[1], ring[3], ring[5], ring[7] });
                    break;
                }
                case EmblemKind.Cross:
                {
                    float a = Mathf.Min(size * armRatio * 0.5f, size * 0.99f);
                    float r = size;
                    // 上の腕の右上から反時計回り。
                    polys.Add(new List<Vector2>
                    {
                        new Vector2( a,  r), new Vector2(-a,  r), new Vector2(-a,  a),
                        new Vector2(-r,  a), new Vector2(-r, -a), new Vector2(-a, -a),
                        new Vector2(-a, -r), new Vector2( a, -r), new Vector2( a, -a),
                        new Vector2( r, -a), new Vector2( r,  a), new Vector2( a,  a),
                    });
                    break;
                }
            }

            if (Mathf.Abs(rotationDeg) > 1e-6f)
            {
                float rad = rotationDeg * Mathf.Deg2Rad;
                float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                foreach (var poly in polys)
                    for (int i = 0; i < poly.Count; i++)
                    {
                        var q = poly[i];
                        poly[i] = new Vector2(q.x * c - q.y * s, q.x * s + q.y * c);
                    }
            }
            return polys;
        }

        /// <summary>正 n 角形の頂点。先頭が真上、反時計回り。</summary>
        private static Vector2[] RegularPoints(int n, float r)
        {
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = (90f + 360f * i / n) * Mathf.Deg2Rad;
                pts[i] = new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a));
            }
            return pts;
        }

        // ================================================================
        // 領域
        // ================================================================

        private static PathD ToPath(IList<Vector2> pts)
        {
            var path = new PathD(pts.Count);
            foreach (var q in pts) path.Add(new PointD(q.x, q.y));
            return path;
        }

        /// <summary>塗り：多角形の組を非ゼロ規則で合体する。</summary>
        private static PathsD FillRegion(List<List<Vector2>> polys)
        {
            var subj = new PathsD();
            foreach (var poly in polys) if (poly.Count >= 3) subj.Add(ToPath(poly));
            return Union(subj);
        }

        /// <summary>
        /// 線：各頂点について「前の辺の中点 → 頂点 → 次の辺の中点」の折れ線を、
        /// 角はとがり（ミター）・端は平らで太らせ、すべて合体する。
        /// </summary>
        private static PathsD LineRegion(List<List<Vector2>> polys, double half)
        {
            var pieces = new PathsD();
            foreach (var poly in polys)
            {
                int n = poly.Count;
                if (n < 2) continue;
                for (int i = 0; i < n; i++)
                {
                    Vector2 prev = poly[(i - 1 + n) % n];
                    Vector2 cur = poly[i];
                    Vector2 next = poly[(i + 1) % n];
                    pieces.Add(ToPath(new[] { (prev + cur) * 0.5f, cur, (cur + next) * 0.5f }));
                }
            }
            if (pieces.Count == 0) return new PathsD();

            var inflated = Clipper.InflatePaths(pieces, half, JoinType.Miter, EndType.Butt, MiterLimit, ClipPrecision);
            return Union(inflated);
        }

        /// <summary>周囲の円：半径 r の円を半幅 half の帯にする。</summary>
        private static PathsD RingRegion(float r, double half, int seg)
        {
            int n = Mathf.Max(16, seg * 8);
            var circle = new PathD(n);
            for (int i = 0; i < n; i++)
            {
                double a = 2.0 * Math.PI * i / n;
                circle.Add(new PointD(r * Math.Cos(a), r * Math.Sin(a)));
            }
            var paths = new PathsD { circle };
            var inflated = Clipper.InflatePaths(paths, half, JoinType.Round, EndType.Joined, 2.0, ClipPrecision,
                ArcTolerance(half, seg));
            return Union(inflated);
        }

        /// <summary>
        /// 角を半径 r で丸める。縮めて戻すと凸の角が、広げて戻すと凹の角が丸くなる。
        /// 丸めた結果が消えたときは丸めずに返す。
        /// </summary>
        private static PathsD RoundCorners(PathsD paths, double r, int seg)
        {
            if (paths == null || paths.Count == 0) return paths;
            double tol = ArcTolerance(r, seg);

            var opened = Clipper.InflatePaths(paths, -r, JoinType.Round, EndType.Polygon, 2.0, ClipPrecision, tol);
            opened = Clipper.InflatePaths(opened, r, JoinType.Round, EndType.Polygon, 2.0, ClipPrecision, tol);
            if (opened.Count == 0) return paths;

            var closed = Clipper.InflatePaths(opened, r, JoinType.Round, EndType.Polygon, 2.0, ClipPrecision, tol);
            closed = Clipper.InflatePaths(closed, -r, JoinType.Round, EndType.Polygon, 2.0, ClipPrecision, tol);
            if (closed.Count == 0) return opened;

            return Union(closed);
        }

        /// <summary>半径 r の円弧を 90° あたり seg 段で刻むときの弦の許容誤差。</summary>
        private static double ArcTolerance(double r, int seg)
            => Math.Max(1e-5, r * (1.0 - Math.Cos(Math.PI / (4.0 * seg))));

        /// <summary>非ゼロ規則での合体（自己交差・重なりを 1 つの領域にする）。</summary>
        private static PathsD Union(PathsD subj)
        {
            var result = new PathsD();
            if (subj == null || subj.Count == 0) return result;
            var c = new ClipperD(ClipPrecision);
            c.AddSubject(subj);
            c.Execute(ClipType.Union, FillRule.NonZero, result);
            return result;
        }

        /// <summary>合体結果を木構造へ取り直し、全ループ CCW・穴フラグ付きの輪郭にする。</summary>
        private static List<Loop> ToLoops(PathsD paths)
        {
            var loops = new List<Loop>();
            if (paths == null || paths.Count == 0) return loops;

            var c = new ClipperD(ClipPrecision);
            c.AddSubject(paths);
            var tree = new PolyTreeD();
            c.Execute(ClipType.Union, FillRule.NonZero, tree);

            ZodiacSignMeshGenerator.CollectLoops(tree, loops);
            return loops;
        }
    }
}
