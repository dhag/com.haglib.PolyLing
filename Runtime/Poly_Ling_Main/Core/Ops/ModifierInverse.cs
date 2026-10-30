// ModifierInverse.cs
// 逆方向（出力の編集 → 入力）の共通の核と、方法ごとの「枠 → 入力の引数」の戻し方。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【流れ】（PolyLing_作り直し整備計画.md 2.5・3.4・段 6）
//   1. 正方向を控え付き（ModifierFrameTrace）で作り直して、編集前の出力 G と枠を得る（Player 層）
//   2. 枠ごとに、G と編集後の出力 E を「生成器が点を置いた空間」へ戻して突き合わせ、
//      相似変換 S（回転＋等方の倍率＋平行移動）を最小二乗で求める（Fit。PLSimilarityEstimator）
//        ・3 頂点以上：相似変換。一直線に並ぶ枠は姿勢が決まらないので失敗にする
//        ・1〜2 頂点（パイプの先端の要）：平行移動だけ
//   3. 方法ごとに S を入力へ戻す（このファイルの Belt* / Bolt* / Radial* / Deck* / Vertex*）
//      入力の頂点から作った頂点（梯子化・帯・貼り付け。段 14）は S を使わず、枠（Kind=Source）の
//      平均の移動を入力の頂点へ戻す（InvertSources）
//   4. 新しい入力から正方向を作り直して出力を置き換える（編集は丸められる。整備計画 6 章の決定）
//
// 【S が持たない成分＝失うもの】相似でない変形（ひしゃげ・歪み）は残差として落ちる。
//   さらに方法ごとに、入力の引数で表せない成分を捨てる（PLModifierAttribute.InverseLoses）。
//
// 【空間】G・E は出力の格納空間。ModifierFrameTrace.ToOutput（生成器の空間 → 出力の格納空間。
//   ピボット・回転と拡大の焼き込み・置き場所・段 5 の M）の逆で戻してから推定する。

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Numerics;
using Poly_Ling.PrimitiveMesh;
using Poly_Ling.PlaceObject;

namespace Poly_Ling.Ops
{
    public static class ModifierInverse
    {
        // ================================================================
        // 宣言
        // ================================================================

        /// <summary>型が宣言した逆方向。</summary>
        public sealed class Info
        {
            public string Method = "";
            public string Keeps  = "";
            public string Loses  = "";
        }

        /// <summary>型の逆方向の宣言。持たなければ null。</summary>
        public static Info Of(Type t)
        {
            if (t == null) return null;
            var a = t.GetCustomAttribute<PLModifierAttribute>(inherit: true);
            if (a == null || string.IsNullOrEmpty(a.Inverse)) return null;
            return new Info { Method = a.Inverse, Keeps = a.InverseKeeps ?? "", Loses = a.InverseLoses ?? "" };
        }

        public static bool HasInverse(Type t) => Of(t) != null;

        // ================================================================
        // 共通の核：枠ごとの姿勢の推定 → 相似変換
        // ================================================================

        /// <summary>枠 1 つの推定結果。S は生成器の空間での相似変換。</summary>
        public sealed class FrameFit
        {
            public ModifierFrameTrace.Frame Frame;
            public bool      Ok;
            public string    Why = "";
            public Matrix4x4 S = Matrix4x4.identity;
            /// <summary>S の等方の倍率。</summary>
            public float     Scale = 1f;
            /// <summary>S の回転。</summary>
            public Quaternion Rotation = Quaternion.identity;
        }

        /// <summary>
        /// 枠ごとに G → E の相似変換を求める。G と E は同じ並びの同じ頂点数であること。
        /// toOutput は生成器の空間 → G・E の空間の行列。
        /// </summary>
        public static List<FrameFit> Fit(
            MeshObject template, MeshObject edited, List<ModifierFrameTrace.Frame> frames,
            Matrix4x4 toOutput, out string error)
        {
            error = null;
            var result = new List<FrameFit>();
            if (template == null || edited == null) { error = "出力がありません"; return result; }
            if (template.VertexCount != edited.VertexCount || template.FaceCount != edited.FaceCount)
            {
                error = $"出力の頂点・面の数が作り直しと合いません（今 {edited.VertexCount} 頂点・{edited.FaceCount} 面 / "
                      + $"作り直し {template.VertexCount} 頂点・{template.FaceCount} 面）。逆方向は位置の編集だけを戻せます";
                return result;
            }
            if (frames == null || frames.Count == 0)
            {
                error = "生成の控えに枠がありません（厚み付けなどで部品を作り直していて、頂点の対応が取れない）";
                return result;
            }

            Matrix4x4 back = toOutput.inverse;

            foreach (var f in frames)
            {
                var fit = new FrameFit { Frame = f };
                result.Add(fit);

                var before = new List<Vector3>(f.Vertices.Count);
                var after  = new List<Vector3>(f.Vertices.Count);
                foreach (int v in f.Vertices)
                {
                    if (v < 0 || v >= template.VertexCount) continue;
                    before.Add(back.MultiplyPoint3x4(template.Vertices[v].Position));
                    after .Add(back.MultiplyPoint3x4(edited.Vertices[v].Position));
                }
                if (before.Count == 0) { fit.Why = "枠に頂点がありません"; continue; }

                if (before.Count < PLSimilarityEstimator.MinimumPointCount)
                {
                    Vector3 d = Vector3.zero;
                    for (int i = 0; i < before.Count; i++) d += after[i] - before[i];
                    d /= before.Count;
                    fit.S  = Matrix4x4.Translate(d);
                    fit.Ok = true;
                    continue;
                }

                if (!PLSimilarityEstimator.TryEstimate(before, after, out var s, out var rot, out var scale))
                {
                    fit.Why = "枠の頂点が一直線に並ぶか重なっていて、姿勢が決まらない";
                    continue;
                }
                fit.S = s; fit.Rotation = rot; fit.Scale = scale; fit.Ok = true;
            }
            return result;
        }

        /// <summary>2 つの出力の頂点の位置のずれ（最大・平均）。数が違えば false。</summary>
        public static bool Deviation(MeshObject a, MeshObject b, out float max, out float mean)
        {
            max = 0f; mean = 0f;
            if (a == null || b == null || a.VertexCount != b.VertexCount) return false;
            double sum = 0;
            for (int i = 0; i < a.VertexCount; i++)
            {
                float d = (a.Vertices[i].Position - b.Vertices[i].Position).magnitude;
                if (d > max) max = d;
                sum += d;
            }
            mean = a.VertexCount > 0 ? (float)(sum / a.VertexCount) : 0f;
            return true;
        }

        // ================================================================
        // beltFrames：梯子の段（パイプの輪・藤壺の 1 個）→ 梯子の段の左右・先端
        // ================================================================

        /// <summary>梯子への戻しの結果。</summary>
        public sealed class BeltResult
        {
            /// <summary>書き直した梯子（元の並び。c.Belts と同じ番号）。</summary>
            public BeltCsvEntry[] Belts;
            /// <summary>段 r が書き直されたか（梯子ごと）。</summary>
            public List<bool[]> Moved = new List<bool[]>();
            public bool[] StartMoved;
            public bool[] EndMoved;
            public int MovedRungs;
            public int UntouchedRungs;
            public int FailedFrames;
            public string FailedText = "";
        }

        /// <summary>
        /// 段ごとの S を元の梯子の段の左右へ掛ける。補間した段（元の段の番号が整数でないもの）は使わない。
        /// 左右の入れ替え・並びの反転は控え（BeltRungTrace）で元の番号へ戻す。S は点に掛けるので左右の別は要らない。
        /// </summary>
        public static BeltResult InvertBelts(BeltCsvEntry[] belts, ModifierFrameTrace trace, List<FrameFit> fits)
        {
            var r = new BeltResult();
            if (belts == null) { r.Belts = Array.Empty<BeltCsvEntry>(); return r; }

            int nb = belts.Length;
            r.Belts      = new BeltCsvEntry[nb];
            r.StartMoved = new bool[nb];
            r.EndMoved   = new bool[nb];

            var sumL = new List<Vector3[]>(); var sumR = new List<Vector3[]>(); var cnt = new List<int[]>();
            for (int b = 0; b < nb; b++)
            {
                r.Belts[b] = BeltShapeOps.Clone(belts[b]);
                int n = belts[b] == null ? 0 : Mathf.Min(belts[b].Left.Count, belts[b].Right.Count);
                sumL.Add(new Vector3[n]); sumR.Add(new Vector3[n]); cnt.Add(new int[n]);
                r.Moved.Add(new bool[n]);
            }

            var failed = new List<string>();
            foreach (var fit in fits)
            {
                var f = fit.Frame;
                if (f.Belt < 0 || f.Belt >= nb || belts[f.Belt] == null) continue;
                if (!fit.Ok) { r.FailedFrames++; if (failed.Count < 10) failed.Add($"梯子 {f.Belt} 段 {f.Key}: {fit.Why}"); continue; }

                var src = belts[f.Belt];
                trace.Belts.TryGetValue(f.Belt, out var info);
                bool reversed = info?.Rungs?.Reversed ?? false;

                if (f.Kind == ModifierFrameKind.StartApex || f.Kind == ModifierFrameKind.EndApex)
                {
                    bool isStart = (f.Kind == ModifierFrameKind.StartApex) ^ reversed;
                    if (isStart && src.StartPoint.HasValue)
                    { r.Belts[f.Belt].StartPoint = fit.S.MultiplyPoint3x4(src.StartPoint.Value); r.StartMoved[f.Belt] = true; }
                    else if (!isStart && src.EndPoint.HasValue)
                    { r.Belts[f.Belt].EndPoint = fit.S.MultiplyPoint3x4(src.EndPoint.Value); r.EndMoved[f.Belt] = true; }
                    continue;
                }

                var map = info?.Rungs?.Source;
                if (map == null || f.Key < 0 || f.Key >= map.Length) continue;
                float sp = map[f.Key];
                int rung = Mathf.RoundToInt(sp);
                if (Mathf.Abs(sp - rung) > 1e-3f) continue;            // 補間した段
                if (rung < 0 || rung >= cnt[f.Belt].Length) continue;

                sumL[f.Belt][rung] += fit.S.MultiplyPoint3x4(src.Left[rung]);
                sumR[f.Belt][rung] += fit.S.MultiplyPoint3x4(src.Right[rung]);
                cnt [f.Belt][rung]++;
            }

            for (int b = 0; b < nb; b++)
            {
                var dst = r.Belts[b];
                if (dst == null) continue;
                for (int i = 0; i < cnt[b].Length; i++)
                {
                    if (cnt[b][i] == 0) { r.UntouchedRungs++; continue; }
                    dst.Left[i]  = sumL[b][i] / cnt[b][i];
                    dst.Right[i] = sumR[b][i] / cnt[b][i];
                    r.Moved[b][i] = true;
                    r.MovedRungs++;
                }
            }
            r.FailedText = string.Join(" / ", failed);
            return r;
        }

        // ================================================================
        // lushFrames：まつげの 1 本（補間した段）→ 元の梯子の段（最小二乗）と回転の始点・終点（段 7）
        //
        //   位置：前処理（向き補正・スプライン分割・切り詰め・残す段）は点に線形なので、
        //     補間した段 k の左右 ＝ Σ_j（元の段 j の左右の重み）＋ 先端の寄与 と書ける。
        //     重みは単位の入力（段 j の左だけ (1,0,0)）を前処理に通して取る（規則は BeltShapeOps の 1 か所のまま）。
        //     目標は 補間した段の左右を、置いた点（アンカー）の S による移動だけ平行移動した点。
        //     1 本の向きの変化は段の向きでも回転の引数でも表せて一意に決まらないので、位置は段へ、
        //     向きは回転の引数へ割り振る。元の段の左右と先端（開始・終了。スプラインの制御点）を最小二乗で求め、
        //     置いていない段（切り詰め・残さなかった段）が決まらないぶんは元の値へ弱く寄せる（λ）。
        //   回転：新しい梯子で枠を作り直し、残る回転 Bn⁻¹·R·Bo·q を X→Y→Z の角度にほどき
        //     （元の角度の近くへ 360° 単位で寄せる）、角度 ≈ 始点·(1−t)＋終点·t を軸ごとに最小二乗で解く。
        // ================================================================

        /// <summary>まつげの戻しの結果。</summary>
        public sealed class LushResult
        {
            public BeltResult Belt;
            public Vector3 RotationStart;
            public Vector3 RotationEnd;
            public int RotationFrames;
        }

        /// <summary>寄せる重みの係数（正規方程式の対角の平均に掛ける）。</summary>
        public const double LushRegularization = 1e-6;

        public static LushResult InvertLush(
            CreateLushCommand c, ModifierFrameTrace trace, List<FrameFit> fits)
        {
            var p = c.Params;
            var belts = c.Belts ?? Array.Empty<BeltCsvEntry>();
            int nb = belts.Length;

            var r = new LushResult
            {
                Belt = new BeltResult
                {
                    Belts = new BeltCsvEntry[nb],
                    StartMoved = new bool[nb],
                    EndMoved = new bool[nb],
                },
                RotationStart = p.RotationStart,
                RotationEnd   = p.RotationEnd,
            };

            var failed = new List<string>();
            // 回転の当てはめに使う (t, 角度)。
            var rotT = new List<float>();
            var rotA = new List<Vector3>();

            for (int b = 0; b < nb; b++)
            {
                var raw = belts[b];
                r.Belt.Belts[b] = BeltShapeOps.Clone(raw);
                int n = raw == null ? 0 : Mathf.Min(raw.Left.Count, raw.Right.Count);
                r.Belt.Moved.Add(new bool[n]);
                if (raw == null || !raw.HasData) continue;

                var pre = BeltShapeOps.PreprocessTraced(raw, c.Orient, c.Spline, out _);
                if (pre == null || !pre.HasData) continue;
                int np = Mathf.Min(pre.Left.Count, pre.Right.Count);

                var myFits = new List<FrameFit>();
                foreach (var fit in fits)
                {
                    var f = fit.Frame;
                    if (f.Belt != b || f.Kind != ModifierFrameKind.Instance) continue;
                    if (!fit.Ok) { r.Belt.FailedFrames++; if (failed.Count < 10) failed.Add($"梯子 {b} 段 {f.Key}: {fit.Why}"); continue; }
                    if (f.Key < 0 || f.Key >= np) continue;
                    myFits.Add(fit);
                }
                if (myFits.Count == 0) continue;

                // ── 位置 ──
                // 未知：元の段の左 n 個・右 n 個と、ある先端（開始・終了）。先端もスプラインの制御点なので一緒に解く。
                int iStart = raw.StartPoint.HasValue ? 2 * n : -1;
                int iEnd   = raw.EndPoint.HasValue ? 2 * n + (iStart >= 0 ? 1 : 0) : -1;
                int m = 2 * n + (iStart >= 0 ? 1 : 0) + (iEnd >= 0 ? 1 : 0);

                Vector3 OldOf(int u) => u < n ? raw.Left[u] : u < 2 * n ? raw.Right[u - n]
                                      : u == iStart ? raw.StartPoint.Value : raw.EndPoint.Value;

                // 重み：未知 u を (1,0,0)、ほかを 0 にした梯子を前処理に通し、補間後の段 k の左右の x を読む。
                var aL = new double[m][];
                var aR = new double[m][];
                for (int u = 0; u < m; u++)
                {
                    var unit = ZeroBelt(raw);
                    if (u < n) unit.Left[u] = Vector3.right;
                    else if (u < 2 * n) unit.Right[u - n] = Vector3.right;
                    else if (u == iStart) unit.StartPoint = Vector3.right;
                    else unit.EndPoint = Vector3.right;
                    var o = BeltShapeOps.PreprocessTraced(unit, c.Orient, c.Spline, out _);
                    aL[u] = new double[np]; aR[u] = new double[np];
                    if (o == null || !o.HasData || o.Left.Count != np) continue;
                    for (int k = 0; k < np; k++) { aL[u][k] = o.Left[k].x; aR[u][k] = o.Right[k].x; }
                }

                var N = new double[m, m];
                var rhs = new double[m, 3];
                // 目標：置いた点（アンカー）の移動だけ段の左右を平行移動した点。
                //   1 本の向きの変化は「段の向き」と「回転の引数」のどちらでも表せて一意に決まらないので、
                //   位置は段へ、向きは回転の引数へ割り振る（段の向き・長さは今のまま）。
                foreach (var fit in myFits)
                {
                    int k = fit.Frame.Key;
                    Vector3 d = fit.Frame.HasAnchor ? fit.S.MultiplyPoint3x4(fit.Frame.Anchor) - fit.Frame.Anchor : Vector3.zero;
                    AddRow(N, rhs, aL, k, pre.Left[k] + d);
                    AddRow(N, rhs, aR, k, pre.Right[k] + d);
                }
                double diag = 0;
                for (int u = 0; u < m; u++) diag += N[u, u];
                // 段の位置へ効かない先端（スプライン分割を切っているときなど）は、正規方程式の列が 0 で
                // 元の値へ寄せられて動かない。ところが終点は最後の段の枠の向きを決める（TryComputeFrame）ので、
                // 平行移動が回転の引数へ漏れていた（段 8 で確かめた）。そうした先端は隣の段のアンカーの移動だけ
                // 平行移動する（位置は段へ、の割り振りどおり）。
                bool startFree = iStart >= 0 && N[iStart, iStart] < 1e-12;
                bool endFree   = iEnd   >= 0 && N[iEnd, iEnd]     < 1e-12;
                FrameFit firstFit = null, lastFit = null;
                foreach (var fit in myFits)
                {
                    if (firstFit == null || fit.Frame.Key < firstFit.Frame.Key) firstFit = fit;
                    if (lastFit  == null || fit.Frame.Key > lastFit.Frame.Key)  lastFit  = fit;
                }
                Vector3 AnchorShift(FrameFit fit) => fit != null && fit.Frame.HasAnchor
                    ? fit.S.MultiplyPoint3x4(fit.Frame.Anchor) - fit.Frame.Anchor : Vector3.zero;
                double lambda = LushRegularization * (diag / m) + 1e-12;
                for (int u = 0; u < m; u++)
                {
                    Vector3 old = OldOf(u);
                    N[u, u] += lambda;
                    rhs[u, 0] += lambda * old.x; rhs[u, 1] += lambda * old.y; rhs[u, 2] += lambda * old.z;
                }
                if (!SolveSymmetric(N, rhs, m))
                {
                    failed.Add($"梯子 {b}: 段の当てはめの連立方程式が解けない");
                    continue;
                }

                Vector3 Sol(int u)
                {
                    if (u == iStart && startFree) return raw.StartPoint.Value + AnchorShift(firstFit);
                    if (u == iEnd   && endFree)   return raw.EndPoint.Value   + AnchorShift(lastFit);
                    return new Vector3((float)rhs[u, 0], (float)rhs[u, 1], (float)rhs[u, 2]);
                }
                const float MovedEps = 1e-12f;

                var dst = r.Belt.Belts[b];
                for (int j = 0; j < n; j++)
                {
                    var nl = Sol(j);
                    var nr = Sol(n + j);
                    bool moved = (nl - raw.Left[j]).sqrMagnitude > MovedEps || (nr - raw.Right[j]).sqrMagnitude > MovedEps;
                    dst.Left[j] = nl; dst.Right[j] = nr;
                    if (moved) { r.Belt.Moved[b][j] = true; r.Belt.MovedRungs++; }
                    else r.Belt.UntouchedRungs++;
                }
                if (iStart >= 0 && (Sol(iStart) - raw.StartPoint.Value).sqrMagnitude > MovedEps)
                { dst.StartPoint = Sol(iStart); r.Belt.StartMoved[b] = true; }
                if (iEnd >= 0 && (Sol(iEnd) - raw.EndPoint.Value).sqrMagnitude > MovedEps)
                { dst.EndPoint = Sol(iEnd); r.Belt.EndMoved[b] = true; }

                // ── 回転 ──
                var preNew = BeltShapeOps.PreprocessTraced(dst, c.Orient, c.Spline, out _);
                if (preNew == null || !preNew.HasData || Mathf.Min(preNew.Left.Count, preNew.Right.Count) != np) continue;
                foreach (var fit in myFits)
                {
                    int k = fit.Frame.Key;
                    if (!LushMeshGenerator.TryComputeFrame(pre.Left, pre.Right, pre.Closed, pre.FlipWinding, pre.EndPoint, k, p, out var fo)) continue;
                    if (!LushMeshGenerator.TryComputeFrame(preNew.Left, preNew.Right, preNew.Closed, preNew.FlipWinding, preNew.EndPoint, k, p, out var fn)) continue;

                    Vector3 oldDeg = LushMeshGenerator.AnglesAt(k, np, p);
                    Matrix4x4 q  = Matrix4x4.Rotate(LushMeshGenerator.EulerLocalXYZ(oldDeg));
                    Matrix4x4 bo = Basis(fo), bn = Basis(fn);
                    Matrix4x4 rot = Matrix4x4.Rotate(fit.Rotation);
                    Matrix4x4 res = bn.transpose * rot * bo * q;   // 枠の軸は正規直交なので逆＝転置

                    Vector3 deg = EulerLocalXYZDeg(res);
                    deg = new Vector3(Near(deg.x, oldDeg.x), Near(deg.y, oldDeg.y), Near(deg.z, oldDeg.z));
                    rotT.Add(LushMeshGenerator.InterpT(k, np, p.RotationInterp));
                    rotA.Add(deg);
                }
            }

            // 回転の始点・終点を軸ごとに当てはめる。
            r.RotationFrames = rotA.Count;
            if (rotA.Count > 0)
            {
                Vector3 s0 = p.RotationStart, s1 = p.RotationEnd;
                for (int axis = 0; axis < 3; axis++)
                {
                    double a00 = 0, a01 = 0, a11 = 0, b0 = 0, b1 = 0, sumRes = 0;
                    for (int i = 0; i < rotA.Count; i++)
                    {
                        double t = rotT[i], u = 1 - t, th = rotA[i][axis];
                        a00 += u * u; a01 += u * t; a11 += t * t; b0 += u * th; b1 += t * th;
                        sumRes += th - (s0[axis] * u + s1[axis] * t);
                    }
                    double det = a00 * a11 - a01 * a01;
                    if (p.RotationInterp == LushRotationInterp.Constant || Math.Abs(det) < 1e-9)
                    {
                        // 位置 t が 1 種類しか無い：両端を同じだけずらす（傾きは保つ）。
                        float d = (float)(sumRes / rotA.Count);
                        s0[axis] += d; s1[axis] += d;
                    }
                    else
                    {
                        s0[axis] = (float)((b0 * a11 - b1 * a01) / det);
                        s1[axis] = (float)((a00 * b1 - a01 * b0) / det);
                    }
                }
                r.RotationStart = s0;
                r.RotationEnd   = s1;
            }

            r.Belt.FailedText = string.Join(" / ", failed);
            return r;
        }

        /// <summary>raw と同じ形（段数・閉じ・巻き順・先端の有無）で、点がすべて 0 の梯子。</summary>
        private static BeltCsvEntry ZeroBelt(BeltCsvEntry raw)
        {
            var z = BeltShapeOps.Clone(raw);
            z.LeftWeights = null; z.RightWeights = null;
            for (int i = 0; i < z.Left.Count; i++)  z.Left[i]  = Vector3.zero;
            for (int i = 0; i < z.Right.Count; i++) z.Right[i] = Vector3.zero;
            if (z.StartPoint.HasValue) z.StartPoint = Vector3.zero;
            if (z.EndPoint.HasValue)   z.EndPoint   = Vector3.zero;
            return z;
        }

        /// <summary>行（補間後の段 k の 1 点）を正規方程式へ足す。</summary>
        private static void AddRow(double[,] N, double[,] rhs, double[][] a, int k, Vector3 target)
        {
            int m = a.Length;
            for (int u = 0; u < m; u++)
            {
                double au = a[u][k];
                if (au == 0) continue;
                for (int v = 0; v < m; v++)
                {
                    double av = a[v][k];
                    if (av != 0) N[u, v] += au * av;
                }
                rhs[u, 0] += au * target.x; rhs[u, 1] += au * target.y; rhs[u, 2] += au * target.z;
            }
        }

        /// <summary>対称正定値の N x = rhs（3 列）をガウスの消去法（部分ピボット）で解き、rhs に x を返す。</summary>
        private static bool SolveSymmetric(double[,] N, double[,] rhs, int m)
        {
            for (int col = 0; col < m; col++)
            {
                int piv = col; double best = Math.Abs(N[col, col]);
                for (int i = col + 1; i < m; i++) if (Math.Abs(N[i, col]) > best) { best = Math.Abs(N[i, col]); piv = i; }
                if (best < 1e-300) return false;
                if (piv != col)
                {
                    for (int j = 0; j < m; j++) { var t = N[col, j]; N[col, j] = N[piv, j]; N[piv, j] = t; }
                    for (int j = 0; j < 3; j++) { var t = rhs[col, j]; rhs[col, j] = rhs[piv, j]; rhs[piv, j] = t; }
                }
                for (int i = col + 1; i < m; i++)
                {
                    double f = N[i, col] / N[col, col];
                    if (f == 0) continue;
                    for (int j = col; j < m; j++) N[i, j] -= f * N[col, j];
                    for (int j = 0; j < 3; j++) rhs[i, j] -= f * rhs[col, j];
                }
            }
            for (int i = m - 1; i >= 0; i--)
                for (int j = 0; j < 3; j++)
                {
                    double s = rhs[i, j];
                    for (int k = i + 1; k < m; k++) s -= N[i, k] * rhs[k, j];
                    rhs[i, j] = s / N[i, i];
                }
            return true;
        }

        /// <summary>枠の軸を列にした行列。</summary>
        private static Matrix4x4 Basis(LushMeshGenerator.Frame f)
        {
            var m = Matrix4x4.identity;
            m.SetColumn(0, new Vector4(f.X.x, f.X.y, f.X.z, 0f));
            m.SetColumn(1, new Vector4(f.Y.x, f.Y.y, f.Y.z, 0f));
            m.SetColumn(2, new Vector4(f.Z.x, f.Z.y, f.Z.z, 0f));
            return m;
        }

        /// <summary>
        /// 回転行列 M ＝ Rx(a)·Ry(b)·Rz(c) の角度（度）。LushMeshGenerator.EulerLocalXYZ の逆。
        ///   M02 = sin b、M12 = −sin a cos b、M22 = cos a cos b、M01 = −cos b sin c、M00 = cos b cos c
        /// </summary>
        private static Vector3 EulerLocalXYZDeg(Matrix4x4 m)
        {
            float b = Mathf.Asin(Mathf.Clamp(m.m02, -1f, 1f));
            float a = Mathf.Atan2(-m.m12, m.m22);
            float c = Mathf.Atan2(-m.m01, m.m00);
            return new Vector3(a, b, c) * Mathf.Rad2Deg;
        }

        /// <summary>d を 360° 単位でずらし、reference に最も近い値にする。</summary>
        private static float Near(float d, float reference)
            => d + 360f * Mathf.Round((reference - d) / 360f);

        // ================================================================
        // 置いた 1 個（藤壺の位置・倍率）を戻す共通部分
        // ================================================================

        /// <summary>置いた 1 個の新しい位置・倍率・回転（生成器の空間）。番号は枠の Key。</summary>
        public sealed class Placed
        {
            public int        Key;
            public Vector3    OldCenter;
            public Vector3    Center;
            public float      ScaleFactor = 1f;
            public Quaternion Rotation = Quaternion.identity;
        }

        /// <summary>置いた位置を控えた枠の新しい位置。失敗した枠は fails に数える。</summary>
        public static List<Placed> PlacedInstances(List<FrameFit> fits, out int fails, out string failText)
        {
            fails = 0;
            var text = new List<string>();
            var list = new List<Placed>();
            foreach (var fit in fits)
            {
                if (!fit.Frame.HasAnchor) continue;
                if (!fit.Ok) { fails++; if (text.Count < 10) text.Add($"{fit.Frame.Key}: {fit.Why}"); continue; }
                list.Add(new Placed
                {
                    Key = fit.Frame.Key,
                    OldCenter = fit.Frame.Anchor,
                    Center = fit.S.MultiplyPoint3x4(fit.Frame.Anchor),
                    ScaleFactor = fit.Scale,
                    Rotation = fit.Rotation,
                });
            }
            list.Sort((a, b) => a.Key.CompareTo(b.Key));
            failText = string.Join(" / ", text);
            return list;
        }

        private static float MeanScale(List<Placed> list)
        {
            if (list.Count == 0) return 1f;
            float s = 0f;
            foreach (var p in list) s += p.ScaleFactor;
            return s / list.Count;
        }

        /// <summary>角度（度）の列の平均（円周上の平均）。</summary>
        private static float CircularMeanDeg(List<float> deg)
        {
            double sx = 0, sy = 0;
            foreach (float d in deg) { sx += Math.Cos(d * Mathf.Deg2Rad); sy += Math.Sin(d * Mathf.Deg2Rad); }
            return (float)(Math.Atan2(sy, sx) * Mathf.Rad2Deg);
        }

        private static float WrapDeg(float d)
        {
            d %= 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }

        // ================================================================
        // boltPattern：置いた 1 個 → PCD の半径・開始角（長方形は幅・高さ）・倍率
        // ================================================================

        public static BoltPatternMeshGenerator.BoltPatternParams InvertBolt(
            BoltPatternMeshGenerator.BoltPatternParams p, List<Placed> placed)
        {
            if (placed.Count == 0) return p;
            Quaternion q = p.Orientation == PlaneOrientation.XZ ? Quaternion.Euler(90f, 0f, 0f)
                         : p.Orientation == PlaneOrientation.YZ ? Quaternion.Euler(0f, 90f, 0f)
                         : Quaternion.identity;
            Quaternion qi = Quaternion.Inverse(q);

            var flat = new List<Vector2>();
            foreach (var pl in placed) { var v = qi * pl.Center; flat.Add(new Vector2(v.x, v.y)); }

            if (p.Layout == BoltPatternLayout.Rectangle)
            {
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                foreach (var f in flat)
                { minX = Mathf.Min(minX, f.x); maxX = Mathf.Max(maxX, f.x); minY = Mathf.Min(minY, f.y); maxY = Mathf.Max(maxY, f.y); }
                p.Width  = Mathf.Clamp(maxX - minX, BoltPatternMeshGenerator.BoltPatternParams.SizeMin, BoltPatternMeshGenerator.BoltPatternParams.SizeMax);
                p.Height = Mathf.Clamp(maxY - minY, BoltPatternMeshGenerator.BoltPatternParams.SizeMin, BoltPatternMeshGenerator.BoltPatternParams.SizeMax);
            }
            else
            {
                int n = Mathf.Clamp(p.Count, BoltPatternMeshGenerator.BoltPatternParams.CircleCountMin, BoltPatternMeshGenerator.BoltPatternParams.CircleCountMax);
                float r = 0f;
                var starts = new List<float>();
                for (int i = 0; i < placed.Count; i++)
                {
                    r += flat[i].magnitude;
                    float a = Mathf.Atan2(flat[i].y, flat[i].x) * Mathf.Rad2Deg;
                    starts.Add(a - 360f * placed[i].Key / n);
                }
                p.PcdRadius  = Mathf.Clamp(r / placed.Count, BoltPatternMeshGenerator.BoltPatternParams.RadiusMin, BoltPatternMeshGenerator.BoltPatternParams.RadiusMax);
                p.StartAngle = Mathf.Clamp(WrapDeg(CircularMeanDeg(starts)), BoltPatternMeshGenerator.BoltPatternParams.AngleMin, BoltPatternMeshGenerator.BoltPatternParams.AngleMax);
            }

            float s0 = p.Scale <= 0f ? 1f : p.Scale;
            p.Scale = Mathf.Clamp(s0 * MeanScale(placed), BoltPatternMeshGenerator.BoltPatternParams.ScaleMin, BoltPatternMeshGenerator.BoltPatternParams.ScaleMax);
            return p;
        }

        // ================================================================
        // radialPlace：置いた 1 個 → 半径・開始角・倍率
        // ================================================================

        public static RadialPlaceMeshGenerator.RadialPlaceParams InvertRadial(
            RadialPlaceMeshGenerator.RadialPlaceParams p, List<Placed> placed)
        {
            if (placed.Count == 0) return p;
            int n = Mathf.Clamp(p.Count, RadialPlaceMeshGenerator.RadialPlaceParams.CountMin, RadialPlaceMeshGenerator.RadialPlaceParams.CountMax);
            float dir = p.CounterClockwise ? -1f : 1f;

            float r = 0f;
            var starts = new List<float>();
            foreach (var pl in placed)
            {
                var c = pl.Center;
                r += new Vector2(c.x, c.y).magnitude;
                // RadialLayout.Position：(-sin a · r, cos a · r)
                float a = Mathf.Atan2(-c.x, c.y) * Mathf.Rad2Deg;
                starts.Add(a - dir * 360f * pl.Key / n);
            }
            p.Radius     = Mathf.Clamp(r / placed.Count, RadialPlaceMeshGenerator.RadialPlaceParams.RadiusMin, RadialPlaceMeshGenerator.RadialPlaceParams.RadiusMax);
            p.StartAngle = Mathf.Clamp(WrapDeg(CircularMeanDeg(starts)), RadialLayout.AngleMin, RadialLayout.AngleMax);

            float s0 = p.Scale <= 0f ? 1f : p.Scale;
            p.Scale = Mathf.Clamp(s0 * MeanScale(placed), RadialPlaceMeshGenerator.RadialPlaceParams.ScaleMin, RadialPlaceMeshGenerator.RadialPlaceParams.ScaleMax);
            return p;
        }

        // ================================================================
        // towerDeck：デッキ → 高さ・Y 軸まわりの回転・倍率
        // ================================================================

        public static TowerDeckMeshGenerator.TowerDeckParams InvertDeck(
            TowerDeckMeshGenerator.TowerDeckParams p, List<Placed> placed)
        {
            if (placed.Count == 0) return p;
            var pl = placed[0];
            p.Height = Mathf.Clamp(pl.Center.y, -1000f, 1000f);

            // 置いた X 軸（Y 軸まわりに RotationY 回した +X）を推定した回転で回し、XZ 面の向きを読む。
            Vector3 x = pl.Rotation * (Quaternion.AngleAxis(p.RotationY, Vector3.up) * Vector3.right);
            if (new Vector2(x.x, x.z).sqrMagnitude > 1e-10f)
                p.RotationY = Mathf.Clamp(WrapDeg(Mathf.Atan2(-x.z, x.x) * Mathf.Rad2Deg), -360f, 360f);

            float s0 = p.Scale <= 0f ? 1f : p.Scale;
            p.Scale = Mathf.Clamp(s0 * pl.ScaleFactor, TowerDeckMeshGenerator.TowerDeckParams.ScaleMin, TowerDeckMeshGenerator.TowerDeckParams.ScaleMax);
            return p;
        }

        // ================================================================
        // copiedVertices / ribbonCenters / surfacePaste：入力の頂点から作った頂点 → 入力の頂点（段 14）
        //   枠（Kind=Source）の頂点の平均を生成器の空間で見本 G と編集後 E について取り、
        //   入力の頂点ごとの「作った点の古い位置・新しい位置」を返す。方法ごとの入力への戻し方は Player 層
        //   （写し・中点はその移動、貼り付けは SurfacePasteOps.InvertPoint）。
        // ================================================================

        /// <summary>入力の頂点 1 個ぶんの移動（生成器の空間）。</summary>
        public sealed class SourceMove
        {
            public MeshObject SourceMesh;
            public int        SourceVertex;
            public Vector3    Old;
            public Vector3    New;
            public bool       Moved;
        }

        /// <summary>移動とみなす下限（生成器の空間の長さ）。</summary>
        public const float SourceMoveEpsilon = 1e-6f;

        /// <summary>
        /// Source の枠ごとに G・E の平均を生成器の空間へ戻して比べる。同じ入力の頂点の枠が複数あれば平均する。
        /// G と E は同じ並びの同じ頂点数であること（Fit と同じ検査を先に通すこと）。
        /// </summary>
        public static List<SourceMove> InvertSources(
            MeshObject template, MeshObject edited, List<ModifierFrameTrace.Frame> frames, Matrix4x4 toOutput)
        {
            var list = new List<SourceMove>();
            if (template == null || edited == null || frames == null) return list;
            Matrix4x4 back = toOutput.inverse;

            var index = new Dictionary<(MeshObject, int), (SourceMove m, int n)>();
            foreach (var f in frames)
            {
                if (f.Kind != ModifierFrameKind.Source || f.SourceMesh == null || f.SourceVertex < 0) continue;
                Vector3 g = Vector3.zero, e = Vector3.zero;
                int c = 0;
                foreach (int v in f.Vertices)
                {
                    if (v < 0 || v >= template.VertexCount || v >= edited.VertexCount) continue;
                    g += back.MultiplyPoint3x4(template.Vertices[v].Position);
                    e += back.MultiplyPoint3x4(edited.Vertices[v].Position);
                    c++;
                }
                if (c == 0) continue;
                g /= c; e /= c;

                var key = (f.SourceMesh, f.SourceVertex);
                if (index.TryGetValue(key, out var acc))
                {
                    acc.m.Old = (acc.m.Old * acc.n + g) / (acc.n + 1);
                    acc.m.New = (acc.m.New * acc.n + e) / (acc.n + 1);
                    index[key] = (acc.m, acc.n + 1);
                }
                else
                {
                    var m = new SourceMove { SourceMesh = f.SourceMesh, SourceVertex = f.SourceVertex, Old = g, New = e };
                    index[key] = (m, 1);
                    list.Add(m);
                }
            }
            foreach (var m in list) m.Moved = (m.New - m.Old).magnitude > SourceMoveEpsilon;
            return list;
        }
    }
}
