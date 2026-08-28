// TextOutlineBuilder.cs
// .plgly のグリフ輪郭を Profile2DExtrudeMeshGenerator 用の Loop 群へ変換する。
// 曲線の折れ線化は TTFont.cpp:308-325 と同じ 2 次ベジエ式を使う（終点も出力する点だけ異なる）。
// 巻き方向は全ループ CCW へ正規化する。MiterCollapse
// (Profile2DExtrudeMeshGenerator.cs:240-267) が CCW でのみ正しくインセットするため。
// 正規化で元の巻き方向は失われるので、穴かどうかは包含関係だけで決める。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Profile2DExtrude;

namespace Poly_Ling.GlyphText
{
    /// <summary>配置パラメータ。</summary>
    public struct TextLayoutParams
    {
        /// <summary>曲線 1 本あたりの分割数。1 以上。</summary>
        public int Segment;

        /// <summary>字間の追加量（em 単位）。</summary>
        public float LetterSpacing;

        /// <summary>行送り倍率。1 で ascent+descent+lineGap。</summary>
        public float LineSpacing;
    }

    public static class TextOutlineBuilder
    {
        /// <summary>
        /// 文字列を 2D ループ群へ変換する。
        /// 座標は 1em = 1 単位。ベースラインは y = descent/unitsPerEm。
        /// </summary>
        /// <param name="missingCount">フォントに存在せず飛ばした文字数。</param>
        public static List<Loop> Build(PlyGlyphFile font, string text, TextLayoutParams p, out int missingCount)
        {
            missingCount = 0;
            var loops = new List<Loop>();

            if (font == null || string.IsNullOrEmpty(text))
                return loops;

            int segment = Mathf.Max(1, p.Segment);
            float inv = 1f / font.UnitsPerEm;
            float lineHeight = (font.Ascent + font.Descent + font.LineGap) * inv * p.LineSpacing;
            float mergeEps = font.UnitsPerEm * 1e-5f;

            // 事前に必要なコードポイントを集めて一括読みする（1 文字ごとに開き直さない）。
            var codePoints = new List<int>();
            CollectCodePoints(text, codePoints);
            font.Preload(codePoints);

            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                float penX = 0f;
                float lineY = -li * lineHeight;

                int i = 0;
                while (i < line.Length)
                {
                    int cp;
                    if (char.IsHighSurrogate(line[i]) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]))
                    {
                        cp = char.ConvertToUtf32(line[i], line[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        cp = line[i];
                        i++;
                    }

                    if (!font.TryGetGlyph(cp, out PlyGlyph glyph))
                    {
                        missingCount++;
                        continue;
                    }

                    AppendGlyph(loops, glyph, font, segment, mergeEps, inv, penX, lineY);
                    penX += glyph.Advance * inv + p.LetterSpacing;
                }
            }

            return loops;
        }

        public static List<Loop> Build(PlyGlyphFile font, string text, TextLayoutParams p)
            => Build(font, text, p, out _);

        private static void CollectCodePoints(string text, List<int> dst)
        {
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '\n' || c == '\r') { i++; continue; }

                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    dst.Add(char.ConvertToUtf32(c, text[i + 1]));
                    i += 2;
                }
                else
                {
                    dst.Add(c);
                    i++;
                }
            }
        }

        /// <summary>
        /// 1 グリフ分の輪郭を折れ線化して loops へ足す。
        /// 穴判定はこのグリフ内で閉じる（文字同士が重なっても影響しない）。
        /// </summary>
        private static void AppendGlyph(List<Loop> loops, PlyGlyph glyph, PlyGlyphFile font,
            int segment, float mergeEps, float inv, float penX, float lineY)
        {
            if (glyph.Contours == null || glyph.Contours.Length == 0)
                return;

            var built = new List<List<Vector2>>();

            for (int ci = 0; ci < glyph.Contours.Length; ci++)
            {
                var raw = Flatten(glyph.Contours[ci], segment, mergeEps);
                if (raw == null || raw.Count < 3) continue;

                // デザイン単位 → em 単位。ベースラインを descent だけ持ち上げる。
                var pts = new List<Vector2>(raw.Count);
                for (int k = 0; k < raw.Count; k++)
                {
                    pts.Add(new Vector2(
                        raw[k].x * inv + penX,
                        (raw[k].y + font.Descent) * inv + lineY));
                }

                // 面積 0 の輪郭は捨てる（Poly2Tri へ渡すと落ちる）。
                float area = SignedArea(pts);
                if (Mathf.Abs(area) < 1e-12f) continue;

                // 全ループ CCW へ正規化する。
                if (area < 0f) pts.Reverse();

                built.Add(pts);
            }

            if (built.Count == 0) return;

            // 外接矩形は内包判定の足切りに使う。
            var mins = new Vector2[built.Count];
            var maxs = new Vector2[built.Count];
            for (int i = 0; i < built.Count; i++)
                ComputeBounds(built[i], out mins[i], out maxs[i]);

            // 「その輪郭を完全に内包する輪郭」の数の偶奇で穴を決める。
            //
            // 単に 1 頂点の包含数を数えると、筆画が重なるグリフ（漢字に多い）で
            // 誤判定する。重なり合う 2 本の輪郭は互いに交差しており、どちらも
            // 相手を内包していない。交差する輪郭を数えないことで、両方とも
            // 外側として扱われ、どちらのフタも張られる。
            for (int a = 0; a < built.Count; a++)
            {
                int depth = 0;
                for (int b = 0; b < built.Count; b++)
                {
                    if (a == b) continue;
                    if (ContainsLoop(built[b], mins[b], maxs[b], built[a], mins[a], maxs[a]))
                        depth++;
                }

                var loop = new Loop();
                loop.Points = built[a];
                loop.IsHole = (depth & 1) != 0;
                loops.Add(loop);
            }
        }

        /// <summary>
        /// 輪郭を折れ線へ展開する。座標はデザイン単位のまま。
        /// 終端は暗黙クローズなので始点へ戻る線分は出力しない。
        /// </summary>
        private static List<Vector2> Flatten(PlyContour c, int segment, float mergeEps)
        {
            if (c == null || c.Commands == null) return null;

            var pts = new List<Vector2>(c.Commands.Length * segment + 1);
            float epsSq = mergeEps * mergeEps;

            void Add(float x, float y)
            {
                if (pts.Count > 0)
                {
                    Vector2 last = pts[pts.Count - 1];
                    float dx = x - last.x, dy = y - last.y;
                    if (dx * dx + dy * dy <= epsSq) return;
                }
                pts.Add(new Vector2(x, y));
            }

            float px = c.StartX, py = c.StartY;
            Add(px, py);

            for (int i = 0; i < c.Commands.Length; i++)
            {
                PlyCommand cmd = c.Commands[i];
                switch (cmd.Type)
                {
                    case PlyCommandType.Line:
                        Add(cmd.X2, cmd.Y2);
                        break;

                    case PlyCommandType.Quad:
                        for (int s = 1; s <= segment; s++)
                        {
                            float t = (float)s / segment;
                            float u = 1f - t;
                            float x = u * u * px + 2f * t * u * cmd.X1 + t * t * cmd.X2;
                            float y = u * u * py + 2f * t * u * cmd.Y1 + t * t * cmd.Y2;
                            Add(x, y);
                        }
                        break;

                    case PlyCommandType.Cubic:
                        for (int s = 1; s <= segment; s++)
                        {
                            float t = (float)s / segment;
                            float u = 1f - t;
                            float x = u * u * u * px + 3f * t * u * u * cmd.X0
                                    + 3f * t * t * u * cmd.X1 + t * t * t * cmd.X2;
                            float y = u * u * u * py + 3f * t * u * u * cmd.Y0
                                    + 3f * t * t * u * cmd.Y1 + t * t * t * cmd.Y2;
                            Add(x, y);
                        }
                        break;
                }

                px = cmd.X2;
                py = cmd.Y2;
            }

            // 終点が始点と重なっていれば落とす（暗黙クローズと重複するため）。
            while (pts.Count >= 2)
            {
                Vector2 a = pts[0], b = pts[pts.Count - 1];
                float dx = b.x - a.x, dy = b.y - a.y;
                if (dx * dx + dy * dy > epsSq) break;
                pts.RemoveAt(pts.Count - 1);
            }

            return pts;
        }

        private static void ComputeBounds(List<Vector2> pts, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 p = pts[i];
                if (p.x < min.x) min.x = p.x;
                if (p.y < min.y) min.y = p.y;
                if (p.x > max.x) max.x = p.x;
                if (p.y > max.y) max.y = p.y;
            }
        }

        /// <summary>
        /// inner が outer に完全に内包されるか。
        ///
        /// 辺が 1 本でも交差していれば内包ではない（重なり合う筆画がここで弾かれる）。
        /// 交差が無ければ、閉曲線同士は「完全に内側」か「完全に外側」のどちらかしか
        /// 取り得ないので、1 点の内外判定で決まる。
        /// </summary>
        private static bool ContainsLoop(
            List<Vector2> outer, Vector2 outerMin, Vector2 outerMax,
            List<Vector2> inner, Vector2 innerMin, Vector2 innerMax)
        {
            if (outer == null || inner == null || outer.Count < 3 || inner.Count < 3)
                return false;

            // 外接矩形に収まらなければ内包し得ない。ここで大半の組を落とす。
            if (innerMin.x < outerMin.x || innerMin.y < outerMin.y ||
                innerMax.x > outerMax.x || innerMax.y > outerMax.y)
                return false;

            if (AnyEdgeCrosses(inner, outer)) return false;

            return PointInPolygon(inner[0], outer);
        }

        /// <summary>2 つの閉曲線の辺が 1 本でも交差するか。</summary>
        private static bool AnyEdgeCrosses(List<Vector2> a, List<Vector2> b)
        {
            int na = a.Count;
            int nb = b.Count;

            for (int i = 0; i < na; i++)
            {
                Vector2 p1 = a[i];
                Vector2 p2 = a[i + 1 < na ? i + 1 : 0];

                float pminx = p1.x < p2.x ? p1.x : p2.x;
                float pmaxx = p1.x > p2.x ? p1.x : p2.x;
                float pminy = p1.y < p2.y ? p1.y : p2.y;
                float pmaxy = p1.y > p2.y ? p1.y : p2.y;

                for (int j = 0; j < nb; j++)
                {
                    Vector2 q1 = b[j];
                    Vector2 q2 = b[j + 1 < nb ? j + 1 : 0];

                    // 辺同士の外接矩形が重ならなければ交差しない。
                    if (pmaxx < (q1.x < q2.x ? q1.x : q2.x)) continue;
                    if (pminx > (q1.x > q2.x ? q1.x : q2.x)) continue;
                    if (pmaxy < (q1.y < q2.y ? q1.y : q2.y)) continue;
                    if (pminy > (q1.y > q2.y ? q1.y : q2.y)) continue;

                    if (SegmentsProperlyIntersect(p1, p2, q1, q2)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 線分が真に交差するか。端点が相手の線分上に乗るだけの接触は交差としない。
        /// 入れ子の輪郭が 1 点で接するだけの場合に、内包を取り消さないため。
        /// </summary>
        private static bool SegmentsProperlyIntersect(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float d1 = Cross(q2 - q1, p1 - q1);
            float d2 = Cross(q2 - q1, p2 - q1);
            float d3 = Cross(p2 - p1, q1 - p1);
            float d4 = Cross(p2 - p1, q2 - p1);

            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f))
                && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

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

        /// <summary>点がポリゴン内部か（even-odd）。巻き方向に依存しない。</summary>
        private static bool PointInPolygon(Vector2 pt, List<Vector2> poly)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (((poly[i].y > pt.y) != (poly[j].y > pt.y)) &&
                    (pt.x < (poly[j].x - poly[i].x) * (pt.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}
