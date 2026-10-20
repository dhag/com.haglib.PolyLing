// ZodiacSignMeshGenerator.cs
// 12 星座のマーク（♈～♓）のメッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【字形】フォントには頼らず、筆画の中心線（直線・円弧・ベジエ）をここに持つ。
//   フォントの有無で作れない星座が出ないようにするため。座標は x 右・y 上。
// 【輪郭化】中心線を Clipper2 で線の太さぶん太らせ、重なった筆画を合体して
//   穴つきの輪郭にする。穴かどうかは合体結果の木構造から取る。
// 【立体化】輪郭は文字と同じ 2D 押し出し生成器（Profile2DExtrudeMeshGenerator）へ渡す。
//   厚み・表裏の面取り／ラウンドはそちらの機能をそのまま使う。
//   巻き方向は文字と同じく全ループ CCW に揃える（ベベルのインセットが CCW 前提のため）。
// 【周囲の円】マークとは別に輪郭化・生成して連結し、パーツ ID を分ける（マーク 0、円 1）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Clipper2Lib;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Profile2DExtrude;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>12 星座。並びは黄道の順（牡羊座から魚座）。</summary>
    public enum ZodiacSign
    {
        Aries, Taurus, Gemini, Cancer, Leo, Virgo,
        Libra, Scorpio, Sagittarius, Capricorn, Aquarius, Pisces,
    }

    public static class ZodiacSignMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct ZodiacSignParams : IEquatable<ZodiacSignParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            public const float SizeMin = 0.01f;
            public const float SizeMax = 10f;

            /// <summary>線の太さ（大きさに対する比）。</summary>
            public const float StrokeWidthMin = 0.01f;
            public const float StrokeWidthMax = 0.3f;

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

            [PLParam(TextKey = "ZodiacSignKind", Description = "星座（Aries=牡羊座 … Pisces=魚座）")]
            public ZodiacSign Sign;

            [PLParam(TextKey = "Size", Description = "マークの大きさ（中心線の外接矩形の長辺）", Min = SizeMin, Max = SizeMax)]
            public float Size;

            [PLParam(TextKey = "ZodiacStrokeWidth", Description = "線の太さ。大きさに対する比", Min = StrokeWidthMin,
                     Max = StrokeWidthMax)]
            public float StrokeWidth;

            [PLParam(TextKey = "ZodiacRoundCaps", Description = "線端を丸くする。false で平らに切る")]
            public bool RoundCaps;

            [PLParam(TextKey = "Segments", Description = "曲線の分割数（円弧 90° あたり）", Min = SegmentsMin,
                     Max = SegmentsMax, Step = 1)]
            public int Segments;

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

            [PLParam(TextKey = "ZodiacRing", Description = "マークの周囲に円を描く")]
            public bool Ring;
            [PLParam(TextKey = "ZodiacRingRadius", Description = "円の半径（線の中心）。大きさに対する比",
                     Min = RingRadiusMin, Max = RingRadiusMax)]
            public float RingRadius;
            [PLParam(TextKey = "ZodiacRingWidth", Description = "円の線の太さ。大きさに対する比",
                     Min = RingWidthMin, Max = RingWidthMax)]
            public float RingWidth;

            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static ZodiacSignParams Default => new ZodiacSignParams
            {
                MeshName      = "ZodiacSign",
                Sign          = ZodiacSign.Aries,
                Size          = 1f,
                StrokeWidth   = 0.08f,
                RoundCaps     = true,
                Segments      = 6,
                Thickness     = 0.1f,
                SegmentsFront = 0,
                SegmentsBack  = 0,
                EdgeSizeFront = 0.015f,
                EdgeSizeBack  = 0.015f,
                EdgeInward    = false,
                Ring          = false,
                RingRadius    = 0.75f,
                RingWidth     = 0.06f,
                FlipFaces     = false,
                Pivot         = Vector3.zero,
            };

            public bool Equals(ZodiacSignParams o) =>
                MeshName == o.MeshName &&
                Sign == o.Sign &&
                Mathf.Approximately(Size, o.Size) &&
                Mathf.Approximately(StrokeWidth, o.StrokeWidth) &&
                RoundCaps == o.RoundCaps &&
                Segments == o.Segments &&
                Mathf.Approximately(Thickness, o.Thickness) &&
                SegmentsFront == o.SegmentsFront &&
                SegmentsBack == o.SegmentsBack &&
                Mathf.Approximately(EdgeSizeFront, o.EdgeSizeFront) &&
                Mathf.Approximately(EdgeSizeBack, o.EdgeSizeBack) &&
                EdgeInward == o.EdgeInward &&
                Ring == o.Ring &&
                Mathf.Approximately(RingRadius, o.RingRadius) &&
                Mathf.Approximately(RingWidth, o.RingWidth) &&
                FlipFaces == o.FlipFaces &&
                Pivot == o.Pivot;

            public override bool Equals(object obj) => obj is ZodiacSignParams p && Equals(p);
            public override int GetHashCode() => (MeshName?.GetHashCode() ?? 0) * 31 + (int)Sign;
        }

        /// <summary>Clipper2 の小数桁。1e-5 単位で丸める。</summary>
        private const int ClipPrecision = 5;

        // ================================================================
        // 生成
        // ================================================================

        public static MeshObject Generate(ZodiacSignParams p)
        {
            float size = Mathf.Clamp(p.Size, ZodiacSignParams.SizeMin, ZodiacSignParams.SizeMax);
            int seg = Mathf.Clamp(p.Segments, ZodiacSignParams.SegmentsMin, ZodiacSignParams.SegmentsMax);

            var strokes = BuildGlyph(p.Sign, seg);
            if (strokes.Count == 0) return null;

            // 中心線の外接矩形を原点中心・長辺 size へ合わせる。
            NormalizeStrokes(strokes, size);

            float strokeW = size * Mathf.Clamp(p.StrokeWidth,
                ZodiacSignParams.StrokeWidthMin, ZodiacSignParams.StrokeWidthMax);
            var markLoops = Outline(strokes, strokeW, p.RoundCaps, seg);

            List<Loop> ringLoops = null;
            float minHalf = strokeW * 0.5f;
            if (p.Ring)
            {
                float rr = size * Mathf.Clamp(p.RingRadius,
                    ZodiacSignParams.RingRadiusMin, ZodiacSignParams.RingRadiusMax);
                float rw = size * Mathf.Clamp(p.RingWidth,
                    ZodiacSignParams.RingWidthMin, ZodiacSignParams.RingWidthMax);
                var ring = new List<Stroke>();
                AddCircle(ring, seg * 2, 0f, 0f, rr);
                ringLoops = Outline(ring, rw, true, seg);
                minHalf = Mathf.Min(minHalf, rw * 0.5f);
            }

            // ベベルのインセットが線の半幅を超えると輪郭が潰れるので、半幅の 9 割で止める。
            float edgeCap = Mathf.Max(ZodiacSignParams.EdgeSizeMin, minHalf * 0.9f);

            var gp = new Profile2DGenerateParams
            {
                Scale         = 1f,
                Offset        = Vector2.zero,
                FlipY         = false,
                Thickness     = Mathf.Clamp(p.Thickness, ZodiacSignParams.ThicknessMin, ZodiacSignParams.ThicknessMax),
                SegmentsFront = Mathf.Clamp(p.SegmentsFront, ZodiacSignParams.EdgeSegmentsMin, ZodiacSignParams.EdgeSegmentsMax),
                SegmentsBack  = Mathf.Clamp(p.SegmentsBack,  ZodiacSignParams.EdgeSegmentsMin, ZodiacSignParams.EdgeSegmentsMax),
                EdgeSizeFront = Mathf.Clamp(p.EdgeSizeFront, ZodiacSignParams.EdgeSizeMin, edgeCap),
                EdgeSizeBack  = Mathf.Clamp(p.EdgeSizeBack,  ZodiacSignParams.EdgeSizeMin, edgeCap),
                EdgeInward    = p.EdgeInward,
                SymmetryMode  = false,
            };

            MeshObject result = null;
            AppendPart(ref result, markLoops, p.MeshName, gp, 0);
            AppendPart(ref result, ringLoops, p.MeshName, gp, 1);
            if (result == null) return null;

            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(result);
            PrimitiveMeshPostProcess.ApplyPivotOffset(result, p.Pivot);
            return result;
        }

        private static void AppendPart(ref MeshObject result, List<Loop> loops, string meshName,
            Profile2DGenerateParams gp, int partsId)
        {
            if (loops == null || loops.Count == 0) return;
            var one = Profile2DExtrudeMeshGenerator.Generate(loops, meshName, gp);
            if (one == null || one.VertexCount == 0) return;

            PartsIdOps.SetPartsId(one, partsId);
            if (result == null) result = one;
            else MeshObjectAppendOps.Append(result, one, copyNormals: true);
        }

        // ================================================================
        // 輪郭化
        // ================================================================

        private sealed class Stroke
        {
            public readonly List<Vector2> Points = new List<Vector2>();
            public bool Closed;
        }

        /// <summary>中心線を太らせて合体し、全ループ CCW・穴フラグ付きの輪郭にする。</summary>
        private static List<Loop> Outline(List<Stroke> strokes, float width, bool roundCaps, int seg)
        {
            double half = width * 0.5;
            var open = new PathsD();
            var closed = new PathsD();
            foreach (var s in strokes)
            {
                if (s.Points.Count < 2) continue;
                var path = new PathD(s.Points.Count);
                foreach (var q in s.Points) path.Add(new PointD(q.x, q.y));
                (s.Closed ? closed : open).Add(path);
            }

            // 線端の半円を 180° あたり 2*seg 段で刻むときの弦の許容誤差。
            double arcTol = half * (1.0 - Math.Cos(Math.PI / (4.0 * seg)));

            var inflated = new PathsD();
            if (open.Count > 0)
                inflated.AddRange(Clipper.InflatePaths(open, half, JoinType.Round,
                    roundCaps ? EndType.Round : EndType.Butt, 2.0, ClipPrecision, arcTol));
            if (closed.Count > 0)
                inflated.AddRange(Clipper.InflatePaths(closed, half, JoinType.Round,
                    EndType.Joined, 2.0, ClipPrecision, arcTol));

            var loops = new List<Loop>();
            if (inflated.Count == 0) return loops;

            var c = new ClipperD(ClipPrecision);
            c.AddSubject(inflated);
            var tree = new PolyTreeD();
            c.Execute(ClipType.Union, FillRule.NonZero, tree);

            CollectLoops(tree, loops);
            return loops;
        }

        /// <summary>合体結果の木から、全ループ CCW・穴フラグ付きの輪郭を集める。紋章図形も使う。</summary>
        internal static void CollectLoops(PolyPathD node, List<Loop> loops)
        {
            for (int i = 0; i < node.Count; i++)
            {
                var child = node[i];
                var poly = child.Polygon;
                if (poly != null && poly.Count >= 3)
                {
                    var pts = new List<Vector2>(poly.Count);
                    foreach (var q in poly) pts.Add(new Vector2((float)q.x, (float)q.y));

                    float area = SignedArea(pts);
                    if (Mathf.Abs(area) > 1e-10f)
                    {
                        if (area < 0f) pts.Reverse();
                        var loop = new Loop { IsHole = child.IsHole };
                        foreach (var q in pts) loop.Points.Add(new Vector3(q.x, q.y, 0f));
                        loops.Add(loop);
                    }
                }
                CollectLoops(child, loops);
            }
        }

        private static float SignedArea(List<Vector2> pts)
        {
            float s = 0f;
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                int j = i + 1 < n ? i + 1 : 0;
                s += pts[i].x * pts[j].y - pts[j].x * pts[i].y;
            }
            return s * 0.5f;
        }

        private static void NormalizeStrokes(List<Stroke> strokes, float size)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var s in strokes)
                foreach (var q in s.Points) { min = Vector2.Min(min, q); max = Vector2.Max(max, q); }

            Vector2 center = (min + max) * 0.5f;
            float extent = Mathf.Max(max.x - min.x, max.y - min.y);
            float k = extent > 1e-6f ? size / extent : size;

            foreach (var s in strokes)
                for (int i = 0; i < s.Points.Count; i++)
                    s.Points[i] = (s.Points[i] - center) * k;
        }

        // ================================================================
        // 中心線の組み立て
        // ================================================================

        /// <summary>1 本の筆画を先頭から順に描くペン。曲線は seg に応じて折れ線化する。</summary>
        private sealed class Pen
        {
            private readonly Stroke _s;
            private readonly int _seg;

            public Pen(List<Stroke> list, int seg, float x, float y)
            {
                _seg = seg;
                _s = new Stroke();
                list.Add(_s);
                Add(new Vector2(x, y));
            }

            private Vector2 Last => _s.Points[_s.Points.Count - 1];

            private void Add(Vector2 q)
            {
                var pts = _s.Points;
                if (pts.Count > 0 && (pts[pts.Count - 1] - q).sqrMagnitude < 1e-10f) return;
                pts.Add(q);
            }

            public Pen LineTo(float x, float y) { Add(new Vector2(x, y)); return this; }

            public Pen QuadTo(float x1, float y1, float x2, float y2)
            {
                Vector2 p0 = Last, p1 = new Vector2(x1, y1), p2 = new Vector2(x2, y2);
                int n = Mathf.Max(2, _seg * 2);
                for (int i = 1; i <= n; i++)
                {
                    float t = (float)i / n, u = 1f - t;
                    Add(u * u * p0 + 2f * u * t * p1 + t * t * p2);
                }
                return this;
            }

            public Pen CubicTo(float x1, float y1, float x2, float y2, float x3, float y3)
            {
                Vector2 p0 = Last, p1 = new Vector2(x1, y1), p2 = new Vector2(x2, y2), p3 = new Vector2(x3, y3);
                int n = Mathf.Max(2, _seg * 2);
                for (int i = 1; i <= n; i++)
                {
                    float t = (float)i / n, u = 1f - t;
                    Add(u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3);
                }
                return this;
            }

            /// <summary>中心 (cx,cy)、半径 r の円弧を a0 → a1（度。反時計回りが正）で描く。始点は円弧上の点。</summary>
            public Pen ArcTo(float cx, float cy, float r, float a0, float a1)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(a1 - a0) / 90f * _seg));
                for (int i = 0; i <= n; i++)
                {
                    float a = Mathf.Lerp(a0, a1, (float)i / n) * Mathf.Deg2Rad;
                    Add(new Vector2(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a)));
                }
                return this;
            }
        }

        private static Pen Start(List<Stroke> l, int seg, float x, float y) => new Pen(l, seg, x, y);

        private static Pen Arc(List<Stroke> l, int seg, float cx, float cy, float r, float a0, float a1)
        {
            float a = a0 * Mathf.Deg2Rad;
            return new Pen(l, seg, cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a)).ArcTo(cx, cy, r, a0, a1);
        }

        private static void AddCircle(List<Stroke> l, int seg, float cx, float cy, float r)
        {
            var s = new Stroke { Closed = true };
            int n = Mathf.Max(8, seg * 4);
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                s.Points.Add(new Vector2(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a)));
            }
            l.Add(s);
        }

        /// <summary>矢じり。tip から dir の逆向きへ ±deg 開いた 2 本の線。</summary>
        private static void AddArrowHead(List<Stroke> l, int seg, Vector2 tip, Vector2 dir, float len, float deg)
        {
            dir.Normalize();
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                float a = sgn * deg * Mathf.Deg2Rad;
                Vector2 back = -dir;
                var b = new Vector2(back.x * Mathf.Cos(a) - back.y * Mathf.Sin(a),
                                    back.x * Mathf.Sin(a) + back.y * Mathf.Cos(a)) * len;
                Start(l, seg, tip.x + b.x, tip.y + b.y).LineTo(tip.x, tip.y);
            }
        }

        /// <summary>乙女座・蠍座に共通の「m」の 2 つ目の脚まで（1 本目の脚・山 2 つ・2 本目の脚）。</summary>
        private static void AddMBody(List<Stroke> l, int seg)
        {
            const float top = 0.15f, bot = -0.45f, r = 0.125f;
            Start(l, seg, -0.47f, 0.24f).QuadTo(-0.4f, 0.3f, -0.4f, top).LineTo(-0.4f, bot);
            Arc(l, seg, -0.275f, top, r, 180f, 0f).LineTo(-0.15f, bot);
        }

        private static List<Stroke> BuildGlyph(ZodiacSign sign, int seg)
        {
            var l = new List<Stroke>();
            switch (sign)
            {
                case ZodiacSign.Aries:
                    // 縦棒の上で左右に分かれ、外側へ巻き下がる角。
                    Start(l, seg, 0f, -0.5f).LineTo(0f, 0.22f);
                    Arc(l, seg, -0.21f, 0.22f, 0.21f, 0f, 230f);
                    Arc(l, seg, 0.21f, 0.22f, 0.21f, 180f, -50f);
                    break;

                case ZodiacSign.Taurus:
                    // 円の上に、上向きに開いた角の弧。
                    AddCircle(l, seg, 0f, -0.2f, 0.28f);
                    Arc(l, seg, 0f, 0.37f, 0.3f, 170f, 370f);
                    break;

                case ZodiacSign.Gemini:
                    // 上下の反った横棒と 2 本の柱。
                    Start(l, seg, -0.42f, 0.45f).QuadTo(0f, 0.3f, 0.42f, 0.45f);
                    Start(l, seg, -0.42f, -0.45f).QuadTo(0f, -0.3f, 0.42f, -0.45f);
                    Start(l, seg, -0.17f, 0.4f).LineTo(-0.17f, -0.4f);
                    Start(l, seg, 0.17f, 0.4f).LineTo(0.17f, -0.4f);
                    break;

                case ZodiacSign.Cancer:
                    // 小円から尾が伸びる形を、点対称に 2 つ。
                    AddCircle(l, seg, -0.22f, 0.12f, 0.12f);
                    Start(l, seg, -0.22f, 0.24f).CubicTo(0f, 0.42f, 0.32f, 0.4f, 0.45f, 0.18f);
                    AddCircle(l, seg, 0.22f, -0.12f, 0.12f);
                    Start(l, seg, 0.22f, -0.24f).CubicTo(0f, -0.42f, -0.32f, -0.4f, -0.45f, -0.18f);
                    break;

                case ZodiacSign.Leo:
                    // 左下の小円から上へ回り込み、右下で外へ跳ねる。
                    AddCircle(l, seg, -0.25f, -0.18f, 0.14f);
                    Start(l, seg, -0.11f, -0.18f)
                        .CubicTo(-0.12f, 0.25f, 0f, 0.45f, 0.15f, 0.42f)
                        .CubicTo(0.35f, 0.38f, 0.3f, 0.15f, 0.18f, -0.1f)
                        .CubicTo(0.1f, -0.28f, 0.15f, -0.45f, 0.3f, -0.45f)
                        .CubicTo(0.38f, -0.45f, 0.42f, -0.4f, 0.44f, -0.33f);
                    break;

                case ZodiacSign.Virgo:
                    // 「m」の 3 本目の脚に、交差する輪を付ける。
                    AddMBody(l, seg);
                    Arc(l, seg, -0.025f, 0.15f, 0.125f, 180f, 0f).LineTo(0.1f, -0.45f);
                    Start(l, seg, 0.1f, -0.05f)
                        .CubicTo(0.35f, 0.15f, 0.48f, -0.12f, 0.3f, -0.26f)
                        .CubicTo(0.2f, -0.34f, 0.08f, -0.4f, -0.02f, -0.5f);
                    break;

                case ZodiacSign.Libra:
                    // 下の横棒と、中央が盛り上がった上の横棒。
                    Start(l, seg, -0.45f, -0.3f).LineTo(0.45f, -0.3f);
                    Start(l, seg, -0.45f, -0.05f).LineTo(-0.17f, -0.05f)
                        .ArcTo(0f, 0.08f, 0.214f, 217.4f, -37.4f)
                        .LineTo(0.45f, -0.05f);
                    break;

                case ZodiacSign.Scorpio:
                    // 「m」の 3 本目の脚が右上へ跳ね、矢じりで終わる。
                    AddMBody(l, seg);
                    Arc(l, seg, -0.025f, 0.15f, 0.125f, 180f, 0f).LineTo(0.1f, -0.3f)
                        .CubicTo(0.1f, -0.45f, 0.22f, -0.47f, 0.3f, -0.38f)
                        .LineTo(0.47f, -0.12f);
                    AddArrowHead(l, seg, new Vector2(0.47f, -0.12f), new Vector2(0.17f, 0.26f), 0.17f, 38f);
                    break;

                case ZodiacSign.Sagittarius:
                    // 右上を指す矢と、それを横切る短い棒。
                    Start(l, seg, -0.42f, -0.42f).LineTo(0.42f, 0.42f);
                    Start(l, seg, 0.1f, 0.42f).LineTo(0.42f, 0.42f).LineTo(0.42f, 0.1f);
                    Start(l, seg, -0.32f, 0.02f).LineTo(0.02f, -0.32f);
                    break;

                case ZodiacSign.Capricorn:
                    // 左の V から右の柱へ上がり、右下の輪を回って左下へ尾を引く。
                    Start(l, seg, -0.46f, 0.3f).QuadTo(-0.4f, 0.38f, -0.35f, 0.3f)
                        .LineTo(-0.2f, -0.35f).LineTo(-0.05f, 0.25f)
                        .CubicTo(0f, 0.45f, 0.18f, 0.45f, 0.18f, 0.22f)
                        .LineTo(0.18f, -0.08f)
                        .CubicTo(0.2f, 0.08f, 0.48f, 0.02f, 0.44f, -0.2f)
                        .CubicTo(0.4f, -0.4f, 0.18f, -0.36f, 0.16f, -0.22f)
                        .CubicTo(0.14f, -0.1f, 0.05f, -0.45f, -0.12f, -0.5f);
                    break;

                case ZodiacSign.Aquarius:
                    // 平行な 2 本の波線（ジグザグ）。
                    foreach (float b in new[] { 0.14f, -0.14f })
                    {
                        Start(l, seg, -0.45f, b).LineTo(-0.3f, b + 0.12f).LineTo(-0.15f, b)
                            .LineTo(0f, b + 0.12f).LineTo(0.15f, b).LineTo(0.3f, b + 0.12f).LineTo(0.45f, b);
                    }
                    break;

                case ZodiacSign.Pisces:
                    // 内側へふくらむ左右の弧と、それをつなぐ横棒。
                    Arc(l, seg, -0.7f, 0f, 0.55f, -50f, 50f);
                    Arc(l, seg, 0.7f, 0f, 0.55f, 130f, 230f);
                    Start(l, seg, -0.25f, 0f).LineTo(0.25f, 0f);
                    break;
            }
            return l;
        }
    }
}
