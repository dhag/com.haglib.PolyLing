// Runtime/Poly_Ling_Main/Core/Ops/UvTextureRemapper.cs
// UV 調整前・調整後のオブジェクトから、テクスチャを調整後の UV に合うよう描き直す計算
// （RemapTextureByUvCommand の本体）。
//
// 【対応の取り方】
//   調整前と調整後は面番号・角番号が一致していること。角数が違う面、3 角未満の面、
//   UV が非有限の面は飛ばす。
//
// 【写像の向き】
//   出力画素（調整後 UV）→ 入力画素（調整前 UV）。出力の各画素について担当の面を決め、
//   その面の写像で入力画像の位置を求めて双一次補間で引く。入力の外側は繰り返し（repeat）。
//   座標はすべて画素単位（u*幅, v*高さ。画素 x の中心は x+0.5）で扱う。
//
// 【アフィン】
//   面ごとに係数を 1 つ決める。三角形はそのまま。四角形は対角線 2 通りの分割で三角形 4 つ、
//   五角形以上は 0 番の頂点からの扇形分割（Face.Triangulate と同じ）で三角形を作り、
//   三角形ごとの係数の平均を使う。面積がほぼ 0 の三角形は平均から外す。
//
// 【TPS（2 次元・近傍点数指定）】
//   調整後 UV の点（位置の重なりは除く）から、面の重心に近い点を指定数だけ選び、
//   調整後→調整前の TPS を面ごとに 1 回解く。近傍点数が 0 または全点数以上なら全点で 1 回だけ解く。
//   解けない面はその面のアフィンへ降格する。
//
// 【面に覆われない画素】
//   延ばし幅の画素数だけ、隣の覆われた画素の面の写像で延ばす（継ぎ目のにじみ対策）。
//   それより外は入力の同じ位置の画素をそのまま写す。
//
// 【スレッド】
//   UnityEngine.Object に触らない。入力・出力とも配列。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Numerics;

namespace Poly_Ling.Ops
{
    /// <summary>UvTextureRemapper の結果の内訳。</summary>
    public sealed class UvTextureRemapReport
    {
        /// <summary>面の総数。</summary>
        public int Faces;

        /// <summary>角数の不一致・3 角未満・UV 不正で飛ばした面。</summary>
        public int SkippedFaces;

        /// <summary>写像が決まらなかった面（三角形がすべて潰れているなど）。</summary>
        public int UnmappedFaces;

        /// <summary>TPS で写した面。</summary>
        public int TpsFaces;

        /// <summary>TPS が解けずアフィンへ降格した面。</summary>
        public int TpsFallbackFaces;

        /// <summary>TPS の制御点（調整後 UV の重なりを除いた点）の数。</summary>
        public int ControlPoints;

        /// <summary>面に覆われた画素。</summary>
        public int CoveredPixels;

        /// <summary>延ばした画素。</summary>
        public int PaddedPixels;

        /// <summary>入力をそのまま写した画素。</summary>
        public int CopiedPixels;

        /// <summary>写像の結果が非有限で、入力をそのまま写した画素。</summary>
        public int InvalidPixels;
    }

    /// <summary>
    /// UV 調整前後のオブジェクトからテクスチャを描き直す。
    /// </summary>
    public static class UvTextureRemapper
    {
        /// <summary>扱う画像の一辺の上限。</summary>
        public const int MaxTextureSide = 8192;

        /// <summary>近傍点数の上限。</summary>
        public const int MaxNeighborCount = 256;

        /// <summary>延ばし幅の上限（画素）。</summary>
        public const int MaxPadPixels = 64;

        /// <summary>
        /// TPS の評価量（制御点数 × 画素数）の上限。これを超えると時間がかかりすぎるので失敗にする。
        /// </summary>
        public const long MaxTpsWork = 600_000_000L;

        // ================================================================
        // 面の UV の取り出し
        // ================================================================

        /// <summary>
        /// 面ごとの角の UV を取り出す。取り出せない面は null。
        /// </summary>
        public static Vector2[][] CollectFaceUvs(MeshObject mo)
        {
            if (mo?.Faces == null) return Array.Empty<Vector2[]>();
            var result = new Vector2[mo.Faces.Count][];
            int vertexCount = mo.Vertices != null ? mo.Vertices.Count : 0;
            for (int f = 0; f < mo.Faces.Count; f++)
            {
                var face = mo.Faces[f];
                if (face?.VertexIndices == null || face.VertexIndices.Count < 3) continue;
                int n = face.VertexIndices.Count;
                var uvs = new Vector2[n];
                bool ok = true;
                for (int k = 0; k < n && ok; k++)
                {
                    int vi = face.VertexIndices[k];
                    if (vi < 0 || vi >= vertexCount) { ok = false; break; }
                    var vtx = mo.Vertices[vi];
                    if (vtx?.UVs == null || vtx.UVs.Count == 0) { ok = false; break; }
                    int ui = (face.UVIndices != null && k < face.UVIndices.Count) ? face.UVIndices[k] : 0;
                    if (ui < 0 || ui >= vtx.UVs.Count) ui = 0;
                    uvs[k] = vtx.UVs[ui];
                }
                if (ok) result[f] = uvs;
            }
            return result;
        }

        // ================================================================
        // 本体
        // ================================================================

        /// <summary>
        /// テクスチャを調整後の UV に合うよう描き直す。出力の大きさは入力と同じ。
        /// </summary>
        /// <param name="src">入力画素（下の行から。Texture2D.GetPixels32 と同じ並び）。</param>
        /// <param name="width">幅。</param>
        /// <param name="height">高さ。</param>
        /// <param name="beforeUvs">調整前の面ごとの角の UV（取り出せない面は null）。</param>
        /// <param name="afterUvs">調整後の面ごとの角の UV。beforeUvs と同数。</param>
        /// <param name="useTps">true で TPS、false でアフィン。</param>
        /// <param name="neighborCount">TPS の近傍点数。0 で全点。</param>
        /// <param name="lambda">TPS の平滑化係数（画素単位の座標に対する値）。</param>
        /// <param name="padPixels">延ばし幅（画素）。</param>
        /// <param name="report">内訳。</param>
        public static Color32[] Remap(
            Color32[] src, int width, int height,
            IReadOnlyList<Vector2[]> beforeUvs, IReadOnlyList<Vector2[]> afterUvs,
            bool useTps, int neighborCount, float lambda, int padPixels,
            out UvTextureRemapReport report)
        {
            report = new UvTextureRemapReport();

            if (src == null) throw new ArgumentNullException(nameof(src));
            if (width <= 0 || height <= 0 || width > MaxTextureSide || height > MaxTextureSide)
                throw new ArgumentException($"画像の大きさが範囲外です（{width}×{height}、一辺 {MaxTextureSide} まで）");
            if (src.Length != width * height)
                throw new ArgumentException("画素数が幅×高さと一致しません");
            if (beforeUvs == null || afterUvs == null)
                throw new ArgumentNullException(beforeUvs == null ? nameof(beforeUvs) : nameof(afterUvs));
            if (beforeUvs.Count != afterUvs.Count)
                throw new ArgumentException($"面数が一致しません（調整前 {beforeUvs.Count}・調整後 {afterUvs.Count}）");
            if (neighborCount < 0 || neighborCount > MaxNeighborCount)
                throw new ArgumentException($"近傍点数は 0〜{MaxNeighborCount} にしてください");
            if (float.IsNaN(lambda) || float.IsInfinity(lambda) || lambda < 0f)
                throw new ArgumentException("平滑化係数は 0 以上の有限値にしてください");
            padPixels = Mathf.Clamp(padPixels, 0, MaxPadPixels);

            int faceCount = afterUvs.Count;
            report.Faces = faceCount;

            // ── 画素単位の角座標
            var bx = new double[faceCount][];
            var by = new double[faceCount][];
            var ax = new double[faceCount][];
            var ay = new double[faceCount][];
            for (int f = 0; f < faceCount; f++)
            {
                var b = beforeUvs[f];
                var a = afterUvs[f];
                if (b == null || a == null || a.Length < 3 || a.Length != b.Length || !AllFinite(a) || !AllFinite(b))
                {
                    report.SkippedFaces++;
                    continue;
                }
                int n = a.Length;
                bx[f] = new double[n]; by[f] = new double[n];
                ax[f] = new double[n]; ay[f] = new double[n];
                for (int k = 0; k < n; k++)
                {
                    bx[f][k] = (double)b[k].x * width;
                    by[f][k] = (double)b[k].y * height;
                    ax[f][k] = (double)a[k].x * width;
                    ay[f][k] = (double)a[k].y * height;
                }
            }

            // ── 面ごとのアフィン（調整後→調整前）
            var affine = new double[faceCount][];
            for (int f = 0; f < faceCount; f++)
            {
                if (ax[f] == null) continue;
                affine[f] = FaceAffine(ax[f], ay[f], bx[f], by[f]);
            }

            // ── 面ごとの TPS
            PLThinPlateSpline2D[] faceTps = null;
            if (useTps)
                faceTps = SolveFaceTps(ax, ay, bx, by, affine, neighborCount, lambda,
                                       (long)width * height, report);

            // ── 担当の面を塗る
            var owner = new int[width * height];
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;
            var xs = new List<double>(16);
            for (int f = 0; f < faceCount; f++)
            {
                if (ax[f] == null) continue;
                if (affine[f] == null && (faceTps == null || faceTps[f] == null))
                {
                    report.UnmappedFaces++;
                    continue;
                }
                report.CoveredPixels += FillPolygon(ax[f], ay[f], f, owner, width, height, xs);
            }

            // ── 延ばす
            if (padPixels > 0) report.PaddedPixels = Pad(owner, width, height, padPixels);

            // ── 描き直す
            var dst = new Color32[src.Length];
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                double py = y + 0.5;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    int f = owner[i];
                    if (f < 0)
                    {
                        dst[i] = src[i];
                        report.CopiedPixels++;
                        continue;
                    }

                    double px = x + 0.5;
                    double sx, sy;
                    var tps = faceTps != null ? faceTps[f] : null;
                    if (tps != null)
                    {
                        Vector2 w = tps.WarpPoint(new Vector2((float)px, (float)py));
                        sx = w.x; sy = w.y;
                    }
                    else
                    {
                        double[] m = affine[f];
                        sx = m[0] * px + m[1] * py + m[2];
                        sy = m[3] * px + m[4] * py + m[5];
                    }

                    if (!TrySample(src, width, height, sx, sy, out Color32 c))
                    {
                        dst[i] = src[i];
                        report.InvalidPixels++;
                        continue;
                    }
                    dst[i] = c;
                }
            }
            return dst;
        }

        // ================================================================
        // アフィン
        // ================================================================

        /// <summary>
        /// 面のアフィン係数（調整後→調整前）。[a, b, c, d, e, f] で
        /// x' = a x + b y + c、y' = d x + e y + f。決まらなければ null。
        /// </summary>
        private static double[] FaceAffine(double[] ax, double[] ay, double[] bx, double[] by)
        {
            int n = ax.Length;
            var sum = new double[6];
            int used = 0;
            var m = new double[6];

            void Add(int i0, int i1, int i2)
            {
                if (TriangleAffine(ax, ay, bx, by, i0, i1, i2, m))
                {
                    for (int k = 0; k < 6; k++) sum[k] += m[k];
                    used++;
                }
            }

            if (n == 3)
            {
                Add(0, 1, 2);
            }
            else if (n == 4)
            {
                // 対角線 0-2 の分割と 1-3 の分割
                Add(0, 1, 2); Add(0, 2, 3);
                Add(0, 1, 3); Add(1, 2, 3);
            }
            else
            {
                for (int i = 1; i < n - 1; i++) Add(0, i, i + 1);
            }

            if (used == 0) return null;
            for (int k = 0; k < 6; k++)
            {
                sum[k] /= used;
                if (double.IsNaN(sum[k]) || double.IsInfinity(sum[k])) return null;
            }
            return sum;
        }

        /// <summary>三角形 1 つのアフィン係数（調整後→調整前）。潰れていれば false。</summary>
        private static bool TriangleAffine(
            double[] ax, double[] ay, double[] bx, double[] by,
            int i0, int i1, int i2, double[] m)
        {
            double e1x = ax[i1] - ax[i0], e1y = ay[i1] - ay[i0];
            double e2x = ax[i2] - ax[i0], e2y = ay[i2] - ay[i0];
            double det = e1x * e2y - e1y * e2x;
            double scale = e1x * e1x + e1y * e1y + e2x * e2x + e2y * e2y;
            if (!(scale > 0.0) || Math.Abs(det) <= 1e-9 * scale) return false;

            double f1x = bx[i1] - bx[i0], f1y = by[i1] - by[i0];
            double f2x = bx[i2] - bx[i0], f2y = by[i2] - by[i0];

            double l00 = ( f1x * e2y - f2x * e1y) / det;
            double l01 = (-f1x * e2x + f2x * e1x) / det;
            double l10 = ( f1y * e2y - f2y * e1y) / det;
            double l11 = (-f1y * e2x + f2y * e1x) / det;

            m[0] = l00; m[1] = l01; m[2] = bx[i0] - (l00 * ax[i0] + l01 * ay[i0]);
            m[3] = l10; m[4] = l11; m[5] = by[i0] - (l10 * ax[i0] + l11 * ay[i0]);
            for (int k = 0; k < 6; k++)
                if (double.IsNaN(m[k]) || double.IsInfinity(m[k])) return false;
            return true;
        }

        // ================================================================
        // TPS
        // ================================================================

        private static PLThinPlateSpline2D[] SolveFaceTps(
            double[][] ax, double[][] ay, double[][] bx, double[][] by, double[][] affine,
            int neighborCount, float lambda, long pixelCount, UvTextureRemapReport report)
        {
            int faceCount = ax.Length;

            // 制御点（調整後の位置の重なりを除く。重なったら先に出た方を使う）
            var afterPts = new List<Vector2>();
            var beforePts = new List<Vector2>();
            var seen = new Dictionary<Vector2, int>();
            for (int f = 0; f < faceCount; f++)
            {
                if (ax[f] == null) continue;
                for (int c = 0; c < ax[f].Length; c++)
                {
                    var p = new Vector2((float)ax[f][c], (float)ay[f][c]);
                    if (seen.ContainsKey(p)) continue;
                    seen.Add(p, afterPts.Count);
                    afterPts.Add(p);
                    beforePts.Add(new Vector2((float)bx[f][c], (float)by[f][c]));
                }
            }
            int pointCount = afterPts.Count;
            report.ControlPoints = pointCount;

            var result = new PLThinPlateSpline2D[faceCount];
            if (pointCount < PLThinPlateSpline2D.MinimumPointCount)
            {
                for (int f = 0; f < faceCount; f++)
                    if (ax[f] != null && affine[f] != null) report.TpsFallbackFaces++;
                return result;
            }

            bool global = neighborCount == 0 || neighborCount >= pointCount;
            int k = global ? pointCount : neighborCount;
            long work = (long)k * pixelCount;
            if (work > MaxTpsWork)
                throw new InvalidOperationException(
                    $"TPS の計算量が多すぎます（制御点 {k} × 画素 {pixelCount}）。近傍点数を減らすか画像を小さくしてください");

            if (global)
            {
                var tps = new PLThinPlateSpline2D();
                bool ok;
                try { ok = tps.Solve(afterPts, beforePts, lambda); }
                catch (Exception) { ok = false; }
                for (int f = 0; f < faceCount; f++)
                {
                    if (ax[f] == null) continue;
                    if (ok) { result[f] = tps; report.TpsFaces++; }
                    else if (affine[f] != null) report.TpsFallbackFaces++;
                }
                return result;
            }

            var grid = new PointGrid(afterPts);
            var near = new List<int>(k);
            var nb = new List<Vector2>(k);
            var na = new List<Vector2>(k);
            for (int f = 0; f < faceCount; f++)
            {
                if (ax[f] == null) continue;

                double cx = 0, cy = 0;
                int n = ax[f].Length;
                for (int c = 0; c < n; c++) { cx += ax[f][c]; cy += ay[f][c]; }
                var center = new Vector2((float)(cx / n), (float)(cy / n));

                grid.Query(center, k, near);
                na.Clear(); nb.Clear();
                for (int i = 0; i < near.Count; i++)
                {
                    na.Add(afterPts[near[i]]);
                    nb.Add(beforePts[near[i]]);
                }

                var tps = new PLThinPlateSpline2D();
                bool ok;
                try { ok = na.Count >= PLThinPlateSpline2D.MinimumPointCount && tps.Solve(na, nb, lambda); }
                catch (Exception) { ok = false; }

                if (ok) { result[f] = tps; report.TpsFaces++; }
                else if (affine[f] != null) report.TpsFallbackFaces++;
            }
            return result;
        }

        /// <summary>近傍点を引くための一様格子。</summary>
        private sealed class PointGrid
        {
            private readonly List<Vector2> _pts;
            private readonly float _minX, _minY, _cell;
            private readonly int _nx, _ny;
            private readonly List<int>[] _cells;
            private readonly List<int> _cand = new List<int>();
            private readonly List<float> _dist = new List<float>();

            public PointGrid(List<Vector2> pts)
            {
                _pts = pts;
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var p in pts)
                {
                    if (p.x < minX) minX = p.x;
                    if (p.y < minY) minY = p.y;
                    if (p.x > maxX) maxX = p.x;
                    if (p.y > maxY) maxY = p.y;
                }
                float w = Math.Max(maxX - minX, 1e-3f), h = Math.Max(maxY - minY, 1e-3f);
                float cell = (float)Math.Sqrt(w * (double)h / Math.Max(1, pts.Count)) * 2f;
                if (!(cell > 0f) || float.IsInfinity(cell)) cell = Math.Max(w, h);
                _nx = Mathf.Clamp((int)Math.Ceiling(w / cell), 1, 1024);
                _ny = Mathf.Clamp((int)Math.Ceiling(h / cell), 1, 1024);
                _cell = Math.Max(w / _nx, h / _ny);
                _minX = minX; _minY = minY;
                _cells = new List<int>[_nx * _ny];
                for (int i = 0; i < pts.Count; i++)
                {
                    int ci = CellIndex(pts[i], out _, out _);
                    (_cells[ci] ??= new List<int>()).Add(i);
                }
            }

            private int CellIndex(Vector2 p, out int cx, out int cy)
            {
                cx = Mathf.Clamp((int)Math.Floor((p.x - _minX) / _cell), 0, _nx - 1);
                cy = Mathf.Clamp((int)Math.Floor((p.y - _minY) / _cell), 0, _ny - 1);
                return cy * _nx + cx;
            }

            /// <summary>q に近い順に k 個（足りなければ全部）を result に入れる。</summary>
            public void Query(Vector2 q, int k, List<int> result)
            {
                result.Clear();
                _cand.Clear();
                CellIndex(q, out int cx, out int cy);
                int maxR = Math.Max(_nx, _ny);
                for (int r = 0; r <= maxR; r++)
                {
                    for (int j = cy - r; j <= cy + r; j++)
                    {
                        if (j < 0 || j >= _ny) continue;
                        bool edgeRow = j == cy - r || j == cy + r;
                        for (int i = cx - r; i <= cx + r; i++)
                        {
                            if (i < 0 || i >= _nx) continue;
                            if (!edgeRow && i != cx - r && i != cx + r) continue;
                            var cell = _cells[j * _nx + i];
                            if (cell != null) _cand.AddRange(cell);
                        }
                    }

                    if (_cand.Count >= k)
                    {
                        // 次の輪の点はすべて r*_cell より遠い
                        float kth = KthDistance(q, k);
                        float bound = r * _cell;
                        if (kth <= bound * bound) break;
                    }
                }

                _cand.Sort((a, b) => (_pts[a] - q).sqrMagnitude.CompareTo((_pts[b] - q).sqrMagnitude));
                int take = Math.Min(k, _cand.Count);
                for (int i = 0; i < take; i++) result.Add(_cand[i]);
            }

            private float KthDistance(Vector2 q, int k)
            {
                _dist.Clear();
                for (int i = 0; i < _cand.Count; i++) _dist.Add((_pts[_cand[i]] - q).sqrMagnitude);
                _dist.Sort();
                return _dist[Math.Min(k, _dist.Count) - 1];
            }
        }

        // ================================================================
        // 塗りと延ばし
        // ================================================================

        /// <summary>
        /// 多角形（画素単位）に中心が入る画素のうち、まだ担当の無い画素へ face を入れる（偶奇規則）。
        /// </summary>
        /// <returns>入れた画素数。</returns>
        private static int FillPolygon(double[] px, double[] py, int face, int[] owner, int width, int height, List<double> xs)
        {
            int n = px.Length;
            double minY = double.MaxValue, maxY = double.MinValue;
            for (int k = 0; k < n; k++)
            {
                if (py[k] < minY) minY = py[k];
                if (py[k] > maxY) maxY = py[k];
            }
            if (double.IsNaN(minY) || double.IsNaN(maxY)) return 0;
            if (maxY < 0.0 || minY > height) return 0;

            int y0 = Math.Max(0, (int)Math.Floor(minY - 0.5));
            int y1 = Math.Min(height - 1, (int)Math.Ceiling(maxY));
            int filled = 0;
            for (int y = y0; y <= y1; y++)
            {
                double yc = y + 0.5;
                xs.Clear();
                for (int k = 0; k < n; k++)
                {
                    int k2 = (k + 1) % n;
                    double ya = py[k], yb = py[k2];
                    if ((ya <= yc && yb > yc) || (yb <= yc && ya > yc))
                        xs.Add(px[k] + (yc - ya) * (px[k2] - px[k]) / (yb - ya));
                }
                if (xs.Count < 2) continue;
                xs.Sort();
                int row = y * width;
                for (int s = 0; s + 1 < xs.Count; s += 2)
                {
                    double xa = xs[s], xb = xs[s + 1];
                    if (xb < 0.0 || xa > width) continue;
                    int xStart = Math.Max(0, (int)Math.Ceiling(xa - 0.5));
                    int xEnd = Math.Min(width - 1, (int)Math.Ceiling(xb - 0.5) - 1);
                    for (int x = xStart; x <= xEnd; x++)
                    {
                        int i = row + x;
                        if (owner[i] >= 0) continue;
                        owner[i] = face;
                        filled++;
                    }
                }
            }
            return filled;
        }

        /// <summary>覆われた画素から 8 近傍で padPixels 回だけ担当を延ばす。</summary>
        /// <returns>延ばした画素数。</returns>
        private static int Pad(int[] owner, int width, int height, int padPixels)
        {
            var frontier = new List<int>();
            for (int i = 0; i < owner.Length; i++)
            {
                if (owner[i] < 0) continue;
                int x = i % width, y = i / width;
                bool border = false;
                for (int dy = -1; dy <= 1 && !border; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        if (owner[ny * width + nx] < 0) { border = true; break; }
                    }
                if (border) frontier.Add(i);
            }

            int padded = 0;
            var next = new List<int>();
            for (int step = 0; step < padPixels && frontier.Count > 0; step++)
            {
                next.Clear();
                foreach (int i in frontier)
                {
                    int x = i % width, y = i / width;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                            int j = ny * width + nx;
                            if (owner[j] >= 0) continue;
                            owner[j] = owner[i];
                            next.Add(j);
                            padded++;
                        }
                }
                var t = frontier; frontier = next; next = t;
            }
            return padded;
        }

        // ================================================================
        // 標本化
        // ================================================================

        /// <summary>画素単位の位置 (sx, sy) を双一次補間で引く（外側は繰り返し）。</summary>
        private static bool TrySample(Color32[] src, int width, int height, double sx, double sy, out Color32 c)
        {
            c = default;
            if (double.IsNaN(sx) || double.IsNaN(sy) || double.IsInfinity(sx) || double.IsInfinity(sy)) return false;
            const double limit = 1e7;
            if (Math.Abs(sx) > limit || Math.Abs(sy) > limit) return false;

            double fx = sx - 0.5, fy = sy - 0.5;
            double flx = Math.Floor(fx), fly = Math.Floor(fy);
            float tx = (float)(fx - flx), ty = (float)(fy - fly);
            int x0 = Wrap((long)flx, width), x1 = Wrap((long)flx + 1, width);
            int y0 = Wrap((long)fly, height), y1 = Wrap((long)fly + 1, height);

            Color32 c00 = src[y0 * width + x0], c10 = src[y0 * width + x1];
            Color32 c01 = src[y1 * width + x0], c11 = src[y1 * width + x1];

            c = new Color32(
                Lerp2(c00.r, c10.r, c01.r, c11.r, tx, ty),
                Lerp2(c00.g, c10.g, c01.g, c11.g, tx, ty),
                Lerp2(c00.b, c10.b, c01.b, c11.b, tx, ty),
                Lerp2(c00.a, c10.a, c01.a, c11.a, tx, ty));
            return true;
        }

        private static int Wrap(long v, int size)
        {
            long m = v % size;
            if (m < 0) m += size;
            return (int)m;
        }

        private static byte Lerp2(byte c00, byte c10, byte c01, byte c11, float tx, float ty)
        {
            float a = c00 + (c10 - c00) * tx;
            float b = c01 + (c11 - c01) * tx;
            float v = a + (b - a) * ty;
            int r = (int)Math.Round(v);
            return (byte)(r < 0 ? 0 : (r > 255 ? 255 : r));
        }

        private static bool AllFinite(Vector2[] a)
        {
            for (int i = 0; i < a.Length; i++)
            {
                float x = a[i].x, y = a[i].y;
                if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) return false;
            }
            return true;
        }
    }
}
