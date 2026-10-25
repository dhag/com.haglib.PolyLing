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
//   2. 左右の対応：before の対称面（x = 一定）に対し、鏡像の最近傍が互いに一致する頂点を組にする。
//      対称面の x は組の中点の平均で 3 回求め直す。組で面を鏡写しにして面になるかを検算する。
//   3. 左右対称化：揃えた after を、before の対称面について組の平均で対称にする。中央の頂点は面に載せる。
//   4. 三角形ごとのアフィン：before の面（多角形は扇に割る）ごとに、before の XY → 対称化した after の XY の
//      2 次元アフィンを求める。
//   5. 頭部の頂点：before の同じ番号の頂点と同じ位置にあるものは、対称化した after の位置（XYZ）にする。
//      残りは XY が入っている before の三角形のアフィンで XY を決め、Z は元のまま。
//      入る三角形が複数あれば、その点での before の Z が頂点の Z に近い三角形を使う。
//      どれにも入らなければ XY 上でいちばん近い三角形のアフィンを使う。

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
    }

    public static class MediaPipeHeadBuilder
    {
        private struct Tri
        {
            public Vector2 P0, E1, E2;   // before の XY
            public float   Z0, Z1, Z2;   // before の Z
            public float   Det;
            public Vector2 Q0;           // after の XY（p0 の行き先）
            public float   A11, A12, A21, A22;
        }

        /// <summary>頭部の新しい頂点位置（ワールド）を返す。</summary>
        public static Vector3[] Build(Vector3[] before, IList<int[]> beforeFaces, Vector3[] after, Vector3[] head,
                                      out MediaPipeHeadBuildReport report)
        {
            if (before == null || after == null || head == null || beforeFaces == null)
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

            // 4. 三角形ごとのアフィン
            var tris = new List<Tri>();
            foreach (var f in beforeFaces)
            {
                if (f == null || f.Length < 3) continue;
                bool ok = true;
                foreach (int v in f) if (v < 0 || v >= n) { ok = false; break; }
                if (!ok) continue;
                for (int k = 1; k + 1 < f.Length; k++)
                    if (MakeTri(before, sym, f[0], f[k], f[k + 1], out var t)) tris.Add(t);
            }
            report.Triangles = tris.Count;
            if (tris.Count == 0) throw new InvalidOperationException("貼り付け用の顔に三角形がありません");

            // 5. 頭部の頂点
            float size = BoundsSize(before);
            float tol2 = (1e-5f * size) * (1e-5f * size);
            var result = new Vector3[head.Length];
            for (int k = 0; k < head.Length; k++)
            {
                Vector3 h = head[k];
                if (k < n && (h - before[k]).sqrMagnitude <= tol2)
                {
                    result[k] = sym[k];
                    report.CopiedVertices++;
                    continue;
                }

                var xy = new Vector2(h.x, h.y);
                int bestIn = -1;  float bestDz = float.MaxValue;
                int bestOut = -1; float bestDist = float.MaxValue;
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
                        if (dz < bestDz) { bestDz = dz; bestIn = t; }
                    }
                    else if (bestIn < 0)
                    {
                        Vector2 a = tr.P0, b = tr.P0 + tr.E1, c = tr.P0 + tr.E2;
                        float dist = Mathf.Min(SegDist(xy, a, b), Mathf.Min(SegDist(xy, b, c), SegDist(xy, c, a)));
                        if (dist < bestDist) { bestDist = dist; bestOut = t; }
                    }
                }
                int use = bestIn >= 0 ? bestIn : bestOut;
                if (bestIn < 0) report.OutsideVertices++;
                var trU = tris[use];
                Vector2 r = xy - trU.P0;
                result[k] = new Vector3(
                    trU.Q0.x + trU.A11 * r.x + trU.A12 * r.y,
                    trU.Q0.y + trU.A21 * r.x + trU.A22 * r.y,
                    h.z);
                report.AffineVertices++;
            }
            return result;
        }

        private static bool MakeTri(Vector3[] before, Vector3[] after, int i0, int i1, int i2, out Tri t)
        {
            t = default;
            Vector2 p0 = before[i0], p1 = before[i1], p2 = before[i2];
            Vector2 q0 = after[i0],  q1 = after[i1],  q2 = after[i2];
            Vector2 e1 = p1 - p0, e2 = p2 - p0, f1 = q1 - q0, f2 = q2 - q0;
            float det = e1.x * e2.y - e1.y * e2.x;
            float scale = Mathf.Max(e1.sqrMagnitude, e2.sqrMagnitude);
            if (scale <= 0f || Mathf.Abs(det) <= 1e-8f * scale) return false;   // XY で潰れた三角形

            t.P0 = p0; t.E1 = e1; t.E2 = e2; t.Det = det; t.Q0 = q0;
            t.Z0 = before[i0].z; t.Z1 = before[i1].z; t.Z2 = before[i2].z;
            // A = [f1 f2]·[e1 e2]⁻¹
            t.A11 = ( f1.x * e2.y - f2.x * e1.y) / det;
            t.A12 = (-f1.x * e2.x + f2.x * e1.x) / det;
            t.A21 = ( f1.y * e2.y - f2.y * e1.y) / det;
            t.A22 = (-f1.y * e2.x + f2.y * e1.x) / det;
            return true;
        }

        /// <summary>鏡像の最近傍が互いに一致する頂点を組にする。組が無い頂点は -1、中央の頂点は自分自身。</summary>
        private static int[] FindMirrorPairs(Vector3[] p, out float planeX, out float maxErr)
        {
            int n = p.Length;
            float c = 0f;
            for (int i = 0; i < n; i++) c += p[i].x;
            c /= n;

            var near = new int[n];
            var mate = new int[n];
            for (int iter = 0; iter < 3; iter++)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3 m = Mirror(p[i], c);
                    int best = -1; float bd = float.MaxValue;
                    for (int j = 0; j < n; j++)
                    {
                        float d = (m - p[j]).sqrMagnitude;
                        if (d < bd) { bd = d; best = j; }
                    }
                    near[i] = best;
                }
                double sum = 0; int cnt = 0;
                for (int i = 0; i < n; i++)
                {
                    mate[i] = near[near[i]] == i ? near[i] : -1;
                    if (mate[i] >= 0) { sum += (p[i].x + p[mate[i]].x) * 0.5; cnt++; }
                }
                if (cnt > 0) c = (float)(sum / cnt);
            }

            // 最後の c で、組の鏡像のずれの最大を測る
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
