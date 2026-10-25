// MediaPipeHeadBuilder.cs
// MediaPipe の顔メッシュから頭部全体を作る計算（MediaPipeHeadBuildCommand の本体）。
//
// ■ 入力（すべてワールド座標）
//   before … 貼り付け用の顔（例「Cタイプ_貼り付け用_FaceMesh2」）。MediaPipe の顔と頂点番号が一致していること
//             （0〜467 が顔の点、468〜477 が虹彩、478〜 が外縁）。左右の対応はこのメッシュから求める。
//   after  … MediaPipe の顔（createNohMask の出力。穴埋め・外縁ありで before と同じ頂点数）。
//   head   … before に頭部をつないだメッシュ（例「Cタイプ_頭くっつけ用_FaceMesh2」）。
//
// ■ 流れ
//   1. 大きさ合わせ：after を before へ重ねる相似変換（回転・一様な拡大・移動、Horn の方法）を、
//      顔の点（先頭 FaceMeshPoints 点）の対応で求めて after の全点にかける。
//   2. 左右の対応：顔の点（0〜467）は正準顔モデル由来の表（MediaPipeFaceMirrorData）を使う。
//      before の対称面（x = 一定）の x はその組の中点の平均。虹彩・外縁は before の上で、同じ種類の点どうしの中で
//      鏡像の最近傍が互いに一致するものを組にする（before 自体は左右対称ではないので、全点の最近傍では組めない）。
//      組で面を鏡写しにして面になるかを検算する。
//   3. 左右対称化：揃えた after を、before の対称面について組の平均で対称にする。中央の頂点は面に載せる。
//   3b. before も同じ組と対称面で左右対称にする（symBefore）。
//   4. 三角形ごとのアフィン：before の面（多角形は扇に割る）のうち頂点がすべて検出点（0〜467）のものごとに、
//      対称化した before の XY → 対称化した after の XY の 2 次元アフィンを求める。外縁は生成時に顔の面に沿って押し出した
//      だけの点で、before の外縁（頭部へつなぐよう手で直したもの）と形が大きく違うので使わない。
//   5. 頭部の頂点：顔の点（0〜477）で before の同じ番号の頂点と同じ位置にあるものは、対称化した after の位置（XYZ）にする。
//      外縁（478〜）は頭部側として扱う。
//      残りは XY が入っている before の三角形のアフィンで XY を決め、Z は元のまま。
//      入る三角形が複数あれば、その点での before の Z が頂点の Z に近い三角形を使う。
//      どれにも入らなければ（領域の外）、領域の外周を使って写す（Border。輪郭際の細い三角形 1 個の
//      アフィンを延長すると大きく飛ぶため）。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Tools.MediaPipe
{
    public sealed class MediaPipeHeadBuildReport
    {
        public float FitScale;        // 大きさ合わせの拡大率（after → before）
        public float MirrorPlaneX;    // before の対称面の x
        public int   Pairs;           // 左右の組（中央の頂点を除く）
        public int   Midline;         // 中央の頂点（自分自身と組）
        public int   Unpaired;        // 組にならなかった頂点
        public float MaxPairError;    // 組の鏡像のずれの最大
        public int   MirrorFacesOk;   // 鏡写しにしても面になった面
        public int   MirrorFaces;     // 鏡写しを検算できた面（頂点がすべて組を持つ面）
        public int   Triangles;       // アフィンに使った三角形
        public int   CopiedVertices;  // after の位置をそのまま入れた頂点
        public int   AffineVertices;  // アフィンで XY を決めた頂点
        public int   OutsideVertices; // そのうち、どの三角形にも入らなかった頂点
        public float MaxBeforeSymShift; // BEFORE の対称化で動いた量の最大
        public bool[] Copied;         // 頭部の頂点ごとに、MediaPipe の位置をそのまま入れたか
    }

    public static class MediaPipeHeadBuilder
    {
        private struct Tri
        {
            public int     I0, I1, I2;   // 頂点番号（診断用）
            public Vector2 P0, E1, E2;   // before の XY
            public float   Z0, Z1, Z2;   // before の Z
            public float   DZ0, DZ1, DZ2; // 奥行きの変化（after の z − before の z）
            public float   Det;
            public Vector2 Q0;           // after の XY（p0 の行き先）
            public float   A11, A12, A21, A22;
        }

        /// <summary>写し方（手順 1〜4 の結果）。頭部にも、A・B のような別の顔メッシュにも同じものを使う。</summary>
        private sealed class Field
        {
            public List<Tri> Tris;
            public Border    Border;
            public Vector3[] Sym;        // 対称化した after
            public Vector3[] SymBefore;  // 対称化した before
        }

        /// <summary>手順 1〜4（大きさ合わせ・左右の対応・対称化・三角形ごとのアフィンと外周）。</summary>
        private static Field Prepare(Vector3[] before, IList<int[]> beforeFaces, Vector3[] after,
                                     out MediaPipeHeadBuildReport report)
        {
            if (before == null || after == null || beforeFaces == null)
                throw new ArgumentNullException();
            if (before.Length != after.Length)
                throw new InvalidOperationException($"貼り付け用の顔（{before.Length}）と MediaPipe の顔（{after.Length}）の頂点数が違います");
            int fitN = FaceExpressionTransfer.FaceMeshPoints;
            if (before.Length < fitN)
                throw new InvalidOperationException($"顔の頂点が {fitN} 個未満です");

            report = new MediaPipeHeadBuildReport();
            int n = before.Length;

            // 1. 大きさ合わせ
            var fitted = FaceExpressionTransfer.AlignSimilarity(after, before, fitN);
            report.FitScale = RmsRadius(fitted, fitN) / Mathf.Max(1e-12f, RmsRadius(after, fitN));

            // 2. 左右の対応
            int[] mate = FindMirrorPairs(before, out float planeX, out float maxErr);
            report.MirrorPlaneX = planeX;
            report.MaxPairError = maxErr;
            for (int i = 0; i < n; i++)
            {
                if (mate[i] < 0) report.Unpaired++;
                else if (mate[i] == i) report.Midline++;
                else if (i < mate[i]) report.Pairs++;
            }
            CheckMirrorFaces(beforeFaces, mate, out report.MirrorFacesOk, out report.MirrorFaces);

            // 3. 左右対称化
            var sym = (Vector3[])fitted.Clone();
            for (int i = 0; i < n; i++)
            {
                int j = mate[i];
                if (j < 0) continue;
                if (j == i) { sym[i].x = planeX; continue; }
                if (j < i) continue;
                Vector3 p = (fitted[i] + Mirror(fitted[j], planeX)) * 0.5f;
                sym[i] = p;
                sym[j] = Mirror(p, planeX);
            }

            // 3b. BEFORE も同じ組と対称面で左右対称にする（アフィンの元）。
            //     元も行き先も対称なので、対称面の上で X によって Y が変わる成分（傾き）がアフィンに入らない。
            //     頭部の頂点は元の BEFORE を基準に置かれたものをそのまま使う（ずれは report.MaxBeforeSymShift）。
            var symBefore = (Vector3[])before.Clone();
            for (int i = 0; i < n; i++)
            {
                int j = mate[i];
                if (j < 0) continue;
                if (j == i) { symBefore[i].x = planeX; continue; }
                if (j < i) continue;
                Vector3 p = (before[i] + Mirror(before[j], planeX)) * 0.5f;
                symBefore[i] = p;
                symBefore[j] = Mirror(p, planeX);
            }
            for (int i = 0; i < n; i++)
            {
                float d = (symBefore[i] - before[i]).magnitude;
                if (d > report.MaxBeforeSymShift) report.MaxBeforeSymShift = d;
            }

            // 4. 三角形ごとのアフィン
            var tris = new List<Tri>();
            var regionFaces = new List<int[]>();
            foreach (var f in beforeFaces)
            {
                if (f == null || f.Length < 3) continue;
                // 頂点がすべて MediaPipe の検出点（顔の点 0〜467 と虹彩 468〜477）の面だけを使う。
                // 外縁（478〜）は生成時に押し出しただけの点なので、アフィンの元にしない。
                // 虹彩を含む面（目の穴を塞いだ面）を入れることで、目の中も領域の内側になる。
                int landmarkCount = Mathf.Min(n, fitN + 10);
                bool ok = true;
                foreach (int v in f) if (v < 0 || v >= landmarkCount) { ok = false; break; }
                if (!ok) continue;
                regionFaces.Add(f);
                for (int k = 1; k + 1 < f.Length; k++)
                    if (MakeTri(symBefore, sym, f[0], f[k], f[k + 1], out var t)) tris.Add(t);
            }
            report.Triangles = tris.Count;
            if (tris.Count == 0) throw new InvalidOperationException("貼り付け用の顔に三角形がありません");

            // 領域の外周（1 面だけが使う辺）と、そのつながりごとの重みの幅
            var border = new Border(symBefore, sym, regionFaces);
            return new Field { Tris = tris, Border = border, Sym = sym, SymBefore = symBefore };
        }

        /// <summary>
        /// 1 点を写す。顔の領域の内側は入った三角形のアフィン（XY）、外側は外周（XY）。
        /// Z は z + depthBlend·dz。dz は検出点ごとの奥行きの変化（対称化した after の z − 対称化した before の z）で、
        /// 内側は三角形の中の重心座標で補間、外側は外周上の点 q での dz に σ₀/(σ₀ + |p − q|) を掛けて減らす。
        /// inside のとき tri と重み（w0, w1, w2 = 三角形の 3 頂点 I0, I1, I2 の重心座標）を返す。
        /// </summary>
        private static Vector3 MapPoint(Field fld, Vector3 h, float depthBlend, float insideTol, bool dbg, int k,
                                        out bool inside, out int tri, out Vector3 bary)
        {
            var tris = fld.Tris;
            var xy = new Vector2(h.x, h.y);
            int bestIn = -1; float bestDz = float.MaxValue, bestU = 0f, bestV = 0f;
            for (int t = 0; t < tris.Count; t++)
            {
                var tr = tris[t];
                Vector2 d = xy - tr.P0;
                float u = (d.x * tr.E2.y - d.y * tr.E2.x) / tr.Det;
                float v = (tr.E1.x * d.y - tr.E1.y * d.x) / tr.Det;
                const float eps = 1e-6f;
                if (u >= -eps && v >= -eps && u + v <= 1f + eps)
                {
                    float z  = tr.Z0 + u * (tr.Z1 - tr.Z0) + v * (tr.Z2 - tr.Z0);
                    float dz = Mathf.Abs(z - h.z);
                    if (dz < bestDz) { bestDz = dz; bestIn = t; bestU = u; bestV = v; }
                }
            }
            // 正面から見て顔に重なっても、顔の面より insideTol 以上奥（または手前）の頂点は内側とみなさない
            if (bestIn >= 0 && bestDz > insideTol) bestIn = -1;
            tri = bestIn;
            if (bestIn < 0)
            {
                inside = false; bary = Vector3.zero;
                Vector2 mapped = fld.Border.Map(xy, dbg, out string info, out float dzq, out float dist, out float sigma0);
                float fall = sigma0 / Mathf.Max(1e-12f, sigma0 + dist);
                var res = new Vector3(mapped.x, mapped.y, h.z + depthBlend * dzq * fall);
                if (dbg) Debug.Log($"[MediaPipeHead] v{k} 外 p=({xy.x:F5},{xy.y:F5}) → ({res.x:F5},{res.y:F5},{res.z:F5}) dz(q)={dzq:F5} 減衰={fall:F3} {info}");
                return res;
            }
            inside = true;
            var trU = tris[bestIn];
            bary = new Vector3(1f - bestU - bestV, bestU, bestV);
            Vector2 r = xy - trU.P0;
            float dzIn = bary.x * trU.DZ0 + bary.y * trU.DZ1 + bary.z * trU.DZ2;
            var resIn = new Vector3(
                trU.Q0.x + trU.A11 * r.x + trU.A12 * r.y,
                trU.Q0.y + trU.A21 * r.x + trU.A22 * r.y,
                h.z + depthBlend * dzIn);
            if (dbg)
                Debug.Log($"[MediaPipeHead] v{k} 内 p=({xy.x:F5},{xy.y:F5}) → ({resIn.x:F5},{resIn.y:F5},{resIn.z:F5}) " +
                          $"三角形 ({trU.I0},{trU.I1},{trU.I2}) A=[{trU.A11:F4} {trU.A12:F4}; {trU.A21:F4} {trU.A22:F4}] dz={dzIn:F5}");
            return resIn;
        }

        /// <summary>
        /// 別の顔メッシュ（A・B タイプなど。before と同じ場所に置かれたもの）の全頂点を写す。
        /// faceUVs が与えられれば、顔の領域の内側の頂点について、入った三角形の 3 頂点の UV を重心座標で補間した UV を
        /// uvOut に入れる（uvValid=false の頂点は外側）。
        /// </summary>
        public static Vector3[] BuildTarget(Vector3[] before, IList<int[]> beforeFaces, Vector3[] after, Vector3[] target,
                                            float depthBlend, Vector2[] faceUVs,
                                            out MediaPipeHeadBuildReport report, out Vector2[] uvOut, out bool[] uvValid,
                                            ICollection<int> debugVertices = null, float insideTol = 0.03f)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var fld = Prepare(before, beforeFaces, after, out report);
            var result = new Vector3[target.Length];
            uvOut = new Vector2[target.Length];
            uvValid = new bool[target.Length];
            report.Copied = new bool[target.Length];
            for (int k = 0; k < target.Length; k++)
            {
                bool dbg = debugVertices != null && debugVertices.Contains(k);
                result[k] = MapPoint(fld, target[k], depthBlend, insideTol, dbg, k, out bool inside, out int tri, out Vector3 b);
                report.AffineVertices++;
                if (!inside) { report.OutsideVertices++; continue; }
                if (faceUVs == null) continue;
                var tr = fld.Tris[tri];
                if (tr.I0 >= faceUVs.Length || tr.I1 >= faceUVs.Length || tr.I2 >= faceUVs.Length) continue;
                uvOut[k] = faceUVs[tr.I0] * b.x + faceUVs[tr.I1] * b.y + faceUVs[tr.I2] * b.z;
                uvValid[k] = true;
            }
            return result;
        }

        /// <summary>頭部の新しい頂点位置（ワールド）を返す。</summary>
        public static Vector3[] Build(Vector3[] before, IList<int[]> beforeFaces, Vector3[] after, Vector3[] head,
                                      out MediaPipeHeadBuildReport report, ICollection<int> debugVertices = null,
                                      float depthBlend = 0f, float insideTol = 0.03f)
        {
            if (head == null) throw new ArgumentNullException(nameof(head));
            var fld = Prepare(before, beforeFaces, after, out report);
            var sym = fld.Sym;
            int n = before.Length;
            int fitN = FaceExpressionTransfer.FaceMeshPoints;

            // 5. 頭部の頂点
            float size = BoundsSize(before);
            float tol2 = (1e-5f * size) * (1e-5f * size);
            int landmarkEnd = Mathf.Min(n, fitN + 10);
            var result = new Vector3[head.Length];
            report.Copied = new bool[head.Length];
            for (int k = 0; k < head.Length; k++)
            {
                Vector3 h = head[k];
                // 顔の点（検出点と虹彩）だけを MediaPipe の位置にする。外縁（478〜）は頭部側として扱う。
                if (k < landmarkEnd && (h - before[k]).sqrMagnitude <= tol2)
                {
                    result[k] = sym[k];
                    report.CopiedVertices++;
                    report.Copied[k] = true;
                    continue;
                }

                report.AffineVertices++;
                bool dbg = debugVertices != null && debugVertices.Contains(k);
                result[k] = MapPoint(fld, h, depthBlend, insideTol, dbg, k, out bool inside, out _, out _);
                if (!inside) report.OutsideVertices++;
            }
            return result;
        }

        /// <summary>
        /// 検出点の三角形が覆う領域の外周（XY）。外の点 p を、外周上のいちばん近い点 q の行き先 q'（辺の両端の
        /// 行き先の線形補間）と、q の近くの外周点から重み付き最小二乗で求めた線形部分 L で p' = q' + L(p − q) に写す。
        /// 重みは exp(−(距離/σ)²)、σ = σ₀ + |p − q|。σ₀ はその外周のつながり（輪）の外周点の重心からの RMS 半径の半分。
        /// p = q で p' = q' となり、内側のアフィンとつながる。
        /// </summary>
        private sealed class Border
        {
            private readonly Vector2[] _b, _a;            // 頂点ごとの before / after の XY
            private readonly float[]   _dz;               // 頂点ごとの奥行きの変化（after の z − before の z）
            private readonly List<(int u, int v, int loop)> _edges = new List<(int, int, int)>();
            private readonly List<List<int>> _loopVerts = new List<List<int>>();
            private readonly List<float> _sigma = new List<float>();

            public Border(Vector3[] before, Vector3[] after, List<int[]> faces)
            {
                _b = new Vector2[before.Length];
                _a = new Vector2[after.Length];
                _dz = new float[before.Length];
                for (int i = 0; i < before.Length; i++) { _b[i] = before[i]; _a[i] = after[i]; _dz[i] = after[i].z - before[i].z; }

                var count = new Dictionary<long, int>();
                foreach (var f in faces)
                    for (int k = 0; k < f.Length; k++)
                    {
                        long key = Key(f[k], f[(k + 1) % f.Length]);
                        count.TryGetValue(key, out int c);
                        count[key] = c + 1;
                    }

                // 外周の辺をつながりごとに分ける（素集合）
                var parent = new Dictionary<int, int>();
                int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
                var raw = new List<(int u, int v)>();
                foreach (var kv in count)
                {
                    if (kv.Value != 1) continue;
                    int u = (int)(kv.Key >> 32), v = (int)(kv.Key & 0xffffffff);
                    raw.Add((u, v));
                    if (!parent.ContainsKey(u)) parent[u] = u;
                    if (!parent.ContainsKey(v)) parent[v] = v;
                    int ru = Find(u), rv = Find(v);
                    if (ru != rv) parent[ru] = rv;
                }
                if (raw.Count == 0) throw new InvalidOperationException("検出点の三角形の領域に外周がありません");

                var loopOf = new Dictionary<int, int>();
                // Find は経路を縮めて parent を書き換えるので、鍵の写しで回す
                foreach (var v in new List<int>(parent.Keys))
                {
                    int r = Find(v);
                    if (!loopOf.TryGetValue(r, out int li)) { li = _loopVerts.Count; loopOf[r] = li; _loopVerts.Add(new List<int>()); }
                    _loopVerts[li].Add(v);
                }
                foreach (var (u, v) in raw) _edges.Add((u, v, loopOf[Find(u)]));

                foreach (var vs in _loopVerts)
                {
                    Vector2 c = Vector2.zero;
                    foreach (int v in vs) c += _b[v];
                    c /= vs.Count;
                    double s = 0;
                    foreach (int v in vs) s += (_b[v] - c).sqrMagnitude;
                    _sigma.Add(0.5f * (float)Math.Sqrt(s / vs.Count));
                }
            }

            public Vector2 Map(Vector2 p) => Map(p, false, out _, out _, out _, out _);

            /// <summary>dzq は q での奥行きの変化、dist は |p − q|、sigma0 はその輪の σ₀。</summary>
            public Vector2 Map(Vector2 p, bool wantInfo, out string info, out float dzq, out float dist, out float sigma0)
            {
                info = null;
                // q：外周上のいちばん近い点
                int best = -1; float bestD = float.MaxValue, bestT = 0f;
                for (int e = 0; e < _edges.Count; e++)
                {
                    Vector2 a = _b[_edges[e].u], ab = _b[_edges[e].v] - a;
                    float l2 = ab.sqrMagnitude;
                    float t = l2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
                    float d = (p - (a + ab * t)).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = e; bestT = t; }
                }
                var (eu, ev, loop) = _edges[best];
                Vector2 q  = Vector2.Lerp(_b[eu], _b[ev], bestT);
                Vector2 qa = Vector2.Lerp(_a[eu], _a[ev], bestT);
                dzq    = Mathf.Lerp(_dz[eu], _dz[ev], bestT);
                dist   = Mathf.Sqrt(bestD);
                sigma0 = _sigma[loop];

                // L：q の近くの外周点で重み付き最小二乗（中心は重み付き重心）
                // 重みの幅は外周からの距離に応じて広げる（σ = σ₀ + |p − q|）。外周の近くは局所の L、
                // 遠い頂点ほど広い範囲で求めた L になり、外周に沿って L が急に変わらない。
                float sig  = _sigma[loop] + Mathf.Sqrt(bestD);
                float sig2 = Mathf.Max(1e-12f, sig * sig);
                double sw = 0, cbx = 0, cby = 0, cax = 0, cay = 0;
                var vs = _loopVerts[loop];
                var w = new double[vs.Count];
                for (int i = 0; i < vs.Count; i++)
                {
                    w[i] = Math.Exp(-(_b[vs[i]] - q).sqrMagnitude / sig2);
                    sw += w[i];
                    cbx += w[i] * _b[vs[i]].x; cby += w[i] * _b[vs[i]].y;
                    cax += w[i] * _a[vs[i]].x; cay += w[i] * _a[vs[i]].y;
                }
                cbx /= sw; cby /= sw; cax /= sw; cay /= sw;
                double sxx = 0, sxy = 0, syy = 0, mxx = 0, mxy = 0, myx = 0, myy = 0;
                for (int i = 0; i < vs.Count; i++)
                {
                    double dx = _b[vs[i]].x - cbx, dy = _b[vs[i]].y - cby;
                    double ex = _a[vs[i]].x - cax, ey = _a[vs[i]].y - cay;
                    sxx += w[i] * dx * dx; sxy += w[i] * dx * dy; syy += w[i] * dy * dy;
                    mxx += w[i] * ex * dx; mxy += w[i] * ex * dy;
                    myx += w[i] * ey * dx; myy += w[i] * ey * dy;
                }
                double det = sxx * syy - sxy * sxy;
                if (Math.Abs(det) <= 1e-18)
                {
                    if (wantInfo) info = $"辺({eu},{ev}) t={bestT:F3} q=({q.x:F5},{q.y:F5}) q'=({qa.x:F5},{qa.y:F5}) L=平行移動のみ";
                    return qa + (p - q);   // 外周点が 1 直線上に並ぶときは平行移動だけ
                }
                // L = M·S⁻¹（M = Σw e dᵀ, S = Σw d dᵀ）
                double i11 = syy / det, i12 = -sxy / det, i22 = sxx / det;
                double l11 = mxx * i11 + mxy * i12, l12 = mxx * i12 + mxy * i22;
                double l21 = myx * i11 + myy * i12, l22 = myx * i12 + myy * i22;
                Vector2 r = p - q;
                if (wantInfo)
                    info = $"辺({eu},{ev}) t={bestT:F3} q=({q.x:F5},{q.y:F5}) q'=({qa.x:F5},{qa.y:F5}) " +
                           $"p-q=({r.x:F5},{r.y:F5}) L=[{l11:F4} {l12:F4}; {l21:F4} {l22:F4}] σ={sig:F4}";
                return new Vector2(qa.x + (float)(l11 * r.x + l12 * r.y), qa.y + (float)(l21 * r.x + l22 * r.y));
            }

            private static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        /// <summary>
        /// 左右対称の頭部を作るための半身を求める（鏡写しは呼ぶ側が bakeMirror で行う）。
        /// headOrig … 元の頭部（ワールド）、built … Build の結果、faceVertexCount … 顔の頂点数（貼り付け用の顔の頂点数）、
        /// planeX … 顔の対称面の x（Build の MirrorPlaneX）、seamX … 元の頭部で頭蓋の継ぎ目が並ぶ x。
        /// 1. −X 側を残す：顔の範囲の頂点（&lt; faceVertexCount）だけでできた面のうち、頂点がすべて x ≥ planeX の面を消す。
        ///    頭蓋の面（顔の範囲外の頂点を含む面）はすべて残す。
        /// 2. 継ぎ目：元の頭部の開いた縁の頂点で |x − seamX| &lt; seamTolerance のもの。顔の範囲外の各頂点を、
        ///    元の頭部で YZ がいちばん近い継ぎ目頂点のずれ（結果の x − planeX）だけ X に戻す。
        /// 3. 残した面の頂点で、まだ x &gt; planeX のものは x = planeX に載せる。
        /// 返り値は新しい頂点位置（全頂点ぶん）。keepFace[f] が false の面は消す。
        /// </summary>
        public static Vector3[] BuildHalf(Vector3[] headOrig, Vector3[] built, IList<int[]> headFaces,
                                          int faceVertexCount, float planeX, float seamX, float seamTolerance,
                                          out bool[] keepFace, out int deletedFaces, out int seamVertices,
                                          out int shiftedVertices, out float maxShift, out int clampedVertices)
        {
            int nv = built.Length;
            var pos = (Vector3[])built.Clone();

            // 継ぎ目（元の頭部の開いた縁で、x が seamX に近い頂点）
            var count = new Dictionary<long, int>();
            foreach (var f in headFaces)
                for (int k = 0; k < f.Length; k++)
                {
                    long key = EdgeKey(f[k], f[(k + 1) % f.Length]);
                    count.TryGetValue(key, out int c);
                    count[key] = c + 1;
                }
            var seam = new HashSet<int>();
            foreach (var kv in count)
            {
                if (kv.Value != 1) continue;
                int u = (int)(kv.Key >> 32), v = (int)(kv.Key & 0xffffffff);
                if (Mathf.Abs(headOrig[u].x - seamX) < seamTolerance) seam.Add(u);
                if (Mathf.Abs(headOrig[v].x - seamX) < seamTolerance) seam.Add(v);
            }
            seamVertices = seam.Count;
            if (seam.Count == 0) throw new InvalidOperationException("頭蓋の継ぎ目（開いた縁）が見つかりません");
            var seamList = new List<int>(seam);

            // 2. 顔の範囲外の頂点を、YZ がいちばん近い継ぎ目頂点のずれだけ戻す
            shiftedVertices = 0; maxShift = 0f;
            var shift = new float[nv];
            for (int k = faceVertexCount; k < nv; k++)
            {
                int best = -1; float bd = float.MaxValue;
                foreach (int s in seamList)
                {
                    float dy = headOrig[k].y - headOrig[s].y, dz = headOrig[k].z - headOrig[s].z;
                    float d = dy * dy + dz * dz;
                    if (d < bd) { bd = d; best = s; }
                }
                shift[k] = built[best].x - planeX;
            }
            for (int k = faceVertexCount; k < nv; k++)
            {
                pos[k].x -= shift[k];
                shiftedVertices++;
                if (Mathf.Abs(shift[k]) > maxShift) maxShift = Mathf.Abs(shift[k]);
            }
            foreach (int s in seamList) pos[s].x = planeX;

            // 1. −X 側を残す
            const float eps = 1e-6f;
            keepFace = new bool[headFaces.Count];
            deletedFaces = 0;
            for (int fi = 0; fi < headFaces.Count; fi++)
            {
                var f = headFaces[fi];
                bool faceOnly = true, allPlus = true;
                foreach (int v in f)
                {
                    if (v >= faceVertexCount) faceOnly = false;
                    if (pos[v].x < planeX - eps) allPlus = false;
                }
                keepFace[fi] = !(faceOnly && allPlus);
                if (!keepFace[fi]) deletedFaces++;
            }

            // 3. 残した面の頂点で x > planeX のものを対称面へ載せる
            clampedVertices = 0;
            var used = new bool[nv];
            for (int fi = 0; fi < headFaces.Count; fi++)
                if (keepFace[fi]) foreach (int v in headFaces[fi]) used[v] = true;
            for (int k = 0; k < nv; k++)
                if (used[k] && pos[k].x > planeX + eps) { pos[k].x = planeX; clampedVertices++; }
            return pos;
        }

        /// <summary>
        /// 顔面の材質と UV。頂点がすべて copied（MediaPipe の位置を入れた顔の点）の面の材質を materialIndex にし、
        /// その頂点に MediaPipe の顔メッシュの UV（faceMesh の同じ番号の頂点の 1 番目の UV）を 1 つ足して、
        /// 対象の面だけがそれを使うようにする。ほかの面の UV・材質は変えない。対象にした面の数を返す。
        /// </summary>
        public static int ApplyFaceTexture(Poly_Ling.Data.MeshObject mo, bool[] copied,
                                           Poly_Ling.Data.MeshObject faceMesh, int materialIndex)
        {
            if (mo == null || copied == null || faceMesh == null || materialIndex < 0) return 0;
            var added = new Dictionary<int, int>();
            int faces = 0;
            foreach (var f in mo.Faces)
            {
                if (f?.VertexIndices == null || f.VertexIndices.Count < 3) continue;
                bool all = true;
                foreach (int v in f.VertexIndices)
                    if (v < 0 || v >= copied.Length || !copied[v] || v >= faceMesh.VertexCount
                        || faceMesh.Vertices[v].UVs == null || faceMesh.Vertices[v].UVs.Count == 0) { all = false; break; }
                if (!all) continue;

                while (f.UVIndices.Count < f.VertexIndices.Count) f.UVIndices.Add(0);
                for (int k = 0; k < f.VertexIndices.Count; k++)
                {
                    int v = f.VertexIndices[k];
                    if (!added.TryGetValue(v, out int slot))
                    {
                        var uvs = mo.Vertices[v].UVs;
                        uvs.Add(faceMesh.Vertices[v].UVs[0]);
                        slot = uvs.Count - 1;
                        added[v] = slot;
                    }
                    f.UVIndices[k] = slot;
                }
                f.MaterialIndex = materialIndex;
                faces++;
            }
            return faces;
        }

        /// <summary>
        /// 別の顔メッシュ用の顔面の材質と UV。頂点がすべて uvValid の面の材質を materialIndex にし、
        /// その頂点に uv を 1 つ足して対象の面だけがそれを使うようにする。対象にした面の数を返す。
        /// </summary>
        public static int ApplyTargetTexture(Poly_Ling.Data.MeshObject mo, Vector2[] uv, bool[] uvValid, int materialIndex)
        {
            if (mo == null || uv == null || uvValid == null || materialIndex < 0) return 0;
            var added = new Dictionary<int, int>();
            int faces = 0;
            foreach (var f in mo.Faces)
            {
                if (f?.VertexIndices == null || f.VertexIndices.Count < 3) continue;
                bool all = true;
                foreach (int v in f.VertexIndices) if (v < 0 || v >= uvValid.Length || !uvValid[v]) { all = false; break; }
                if (!all) continue;
                while (f.UVIndices.Count < f.VertexIndices.Count) f.UVIndices.Add(0);
                for (int k = 0; k < f.VertexIndices.Count; k++)
                {
                    int v = f.VertexIndices[k];
                    if (!added.TryGetValue(v, out int slot))
                    {
                        mo.Vertices[v].UVs.Add(uv[v]);
                        slot = mo.Vertices[v].UVs.Count - 1;
                        added[v] = slot;
                    }
                    f.UVIndices[k] = slot;
                }
                f.MaterialIndex = materialIndex;
                faces++;
            }
            return faces;
        }

        private static long EdgeKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        private static bool MakeTri(Vector3[] before, Vector3[] after, int i0, int i1, int i2, out Tri t)
        {
            t = default;
            Vector2 p0 = before[i0], p1 = before[i1], p2 = before[i2];
            Vector2 q0 = after[i0],  q1 = after[i1],  q2 = after[i2];
            Vector2 e1 = p1 - p0, e2 = p2 - p0, f1 = q1 - q0, f2 = q2 - q0;
            float det = e1.x * e2.y - e1.y * e2.x;
            float scale = Mathf.Max(e1.sqrMagnitude, e2.sqrMagnitude);
            if (scale <= 0f || Mathf.Abs(det) <= 1e-8f * scale) return false;   // XY で潰れた三角形

            t.I0 = i0; t.I1 = i1; t.I2 = i2;
            t.P0 = p0; t.E1 = e1; t.E2 = e2; t.Det = det; t.Q0 = q0;
            t.Z0 = before[i0].z; t.Z1 = before[i1].z; t.Z2 = before[i2].z;
            t.DZ0 = after[i0].z - before[i0].z; t.DZ1 = after[i1].z - before[i1].z; t.DZ2 = after[i2].z - before[i2].z;
            // A = [f1 f2]·[e1 e2]⁻¹
            t.A11 = ( f1.x * e2.y - f2.x * e1.y) / det;
            t.A12 = (-f1.x * e2.x + f2.x * e1.x) / det;
            t.A21 = ( f1.y * e2.y - f2.y * e1.y) / det;
            t.A22 = (-f1.y * e2.x + f2.y * e1.x) / det;
            return true;
        }

        /// <summary>
        /// 左右の組を作る。組が無い頂点は -1、中央の頂点は自分自身。
        /// 顔の点（0〜467）は正準顔モデル由来の表（MediaPipeFaceMirrorData）。対称面の x はその組の中点の平均。
        /// 虹彩（468〜477）と外縁（478〜）は、同じ種類の点どうしの中で、鏡像の最近傍が互いに一致するものを組にする。
        /// </summary>
        private static int[] FindMirrorPairs(Vector3[] p, out float planeX, out float maxErr)
        {
            int n = p.Length;
            int nFace = MediaPipeFaceMirrorData.Count;
            var mate = new int[n];
            for (int i = 0; i < n; i++) mate[i] = -1;

            double sum = 0; int cnt = 0;
            for (int i = 0; i < nFace && i < n; i++)
            {
                int j = MediaPipeFaceMirrorData.Mate[i];
                if (j < 0 || j >= n) continue;
                mate[i] = j;
                sum += (p[i].x + p[j].x) * 0.5; cnt++;
            }
            float c = cnt > 0 ? (float)(sum / cnt) : 0f;

            int irisEnd = Mathf.Min(n, MediaPipeFrame.FaceMeshPoints + 10);
            PairWithinGroup(p, c, nFace, irisEnd, mate);   // 虹彩
            PairWithinGroup(p, c, irisEnd, n, mate);       // 外縁

            // 組の鏡像のずれの最大を測る
            maxErr = 0f;
            for (int i = 0; i < n; i++)
            {
                int j = mate[i];
                if (j < 0) continue;
                float e = (Mirror(p[i], c) - p[j]).magnitude;
                if (e > maxErr) maxErr = e;
            }
            planeX = c;
            return mate;
        }

        /// <summary>[begin, end) の点どうしで、鏡像の最近傍が互いに一致するものを組にする。</summary>
        private static void PairWithinGroup(Vector3[] p, float c, int begin, int end, int[] mate)
        {
            if (end - begin <= 0) return;
            var near = new int[end - begin];
            for (int i = begin; i < end; i++)
            {
                Vector3 m = Mirror(p[i], c);
                int best = -1; float bd = float.MaxValue;
                for (int j = begin; j < end; j++)
                {
                    float d = (m - p[j]).sqrMagnitude;
                    if (d < bd) { bd = d; best = j; }
                }
                near[i - begin] = best;
            }
            for (int i = begin; i < end; i++)
            {
                int j = near[i - begin];
                if (near[j - begin] == i) mate[i] = j;
            }
        }

        private static void CheckMirrorFaces(IList<int[]> faces, int[] mate, out int ok, out int total)
        {
            ok = 0; total = 0;
            var keys = new HashSet<string>();
            foreach (var f in faces) if (f != null && f.Length >= 3) keys.Add(Key(f));
            foreach (var f in faces)
            {
                if (f == null || f.Length < 3) continue;
                var m = new int[f.Length];
                bool all = true;
                for (int k = 0; k < f.Length; k++)
                {
                    int v = f[k];
                    if (v < 0 || v >= mate.Length || mate[v] < 0) { all = false; break; }
                    m[k] = mate[v];
                }
                if (!all) continue;
                total++;
                if (keys.Contains(Key(m))) ok++;
            }
        }

        private static string Key(int[] f)
        {
            var s = (int[])f.Clone();
            Array.Sort(s);
            return string.Join(",", s);
        }

        private static Vector3 Mirror(Vector3 v, float planeX) => new Vector3(2f * planeX - v.x, v.y, v.z);

        private static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            float t = l2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        private static float RmsRadius(Vector3[] p, int count)
        {
            Vector3 c = Vector3.zero;
            for (int i = 0; i < count; i++) c += p[i];
            c /= count;
            double s = 0;
            for (int i = 0; i < count; i++) s += (p[i] - c).sqrMagnitude;
            return (float)Math.Sqrt(s / count);
        }

        private static float BoundsSize(Vector3[] p)
        {
            Vector3 mn = p[0], mx = p[0];
            foreach (var v in p) { mn = Vector3.Min(mn, v); mx = Vector3.Max(mx, v); }
            return (mx - mn).magnitude;
        }
    }
}
