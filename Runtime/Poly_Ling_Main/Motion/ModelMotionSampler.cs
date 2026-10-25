// ModelMotionSampler.cs
// ============================================================
// モーションをモデルへ当て、関節ごとのワールド姿勢と表情の重みをフレームごとに取る
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。
// BVH 書き出し（BvhCodec.ExportFromModel）と FBX のモーション書き出しが同じ標本を使う。
// 規則の正本は PolyLing_BVH対応計画.md 5（当て方・関節・根の動き）と PolyLing_FBX対応計画.md（段 3）。
//
// ■ 流れ（BvhCodec.ExportFromModel から切り出した。中身は変えていない）
//   1. MotionClipApplier でモーションをモデルへ当てる（パネル再生と同じ経路）。
//   2. 関節は UnityClipVirtualSkeleton のノード（半身モデルの仮想ミラーを含む）から IK ボーンを除いたもの。
//      親は IK を飛ばした一番近い祖先。並びは深さ優先（親が子より前）。根は複数ありうる。
//   3. 再生経路は Hips から見た姿勢しか当てない（body を当てない。UnityClipApplier.cs の恒久メモ）。
//      マッスルを持つクリップは、CanonBoneMotion で body から Hips の回転 Qc と正規化位置を求め、
//      全関節に剛体変換 T = 移動(Hips のレスト位置 + 正規化位置 × 腰の高さ)·回転 Qc·移動(−Hips のレスト位置) を掛ける。
//      T は FrameRoot に控える（BVH の合成根 PolyLingRoot はこれを姿勢にする）。
//   4. 各関節：F = T · ノードの今のワールド行列。Delta = 回転(F)·回転(レストのワールド)⁻¹、Pos = F の位置。
//      座標は Unity のまま（左手系）。書き出し側がそれぞれの座標へ直す。
//
// ■ 表情（SampleExpressions のときだけ）
//   クリップの表情トラックを MotionClipApplier.FindExpression で引き、ExpandToVertexMorphs で
//   頂点モーフへ展開して（グループは子を辿る）、モーフ MeshContext ごとに重みを足し合わせる。
//   再生（MotionClipApplier.ApplyExpressions）と同じ展開・同じ重み。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.UnityClip;

namespace Poly_Ling.Motion
{
    public sealed class ModelMotionSampleOptions
    {
        /// <summary>1 秒あたりのフレーム数。0 以下ならクリップの frameRate（それも無ければ 30）。</summary>
        public float Fps;
        /// <summary>区間の開始（秒）。</summary>
        public float StartSec;
        /// <summary>区間の終了（秒）。StartSec 以下ならクリップの終端。</summary>
        public float EndSec;
        /// <summary>モーションをモデルへ当てるときの位置の倍率（MotionClipApplier.PositionScale。VMD は PmxUnityRatio）。</summary>
        public float ClipPositionScale = 1f;
        /// <summary>T ポーズ整列を脚（太もも・すね）にも掛けるか（MotionClipApplier.AlignLegs）。</summary>
        public bool AlignLegs;
        /// <summary>表情の重みも取るか。</summary>
        public bool SampleExpressions;
    }

    public sealed class ModelMotionSamples
    {
        public float Fps;
        public float StartSec;
        public float EndSec;
        public int   FrameCount;

        public UnityClipVirtualSkeleton Skeleton;

        /// <summary>関節 → 仮想骨格のノード。深さ優先（親が子より前）。</summary>
        public readonly List<int> JointNode   = new List<int>();
        /// <summary>関節 → 親の関節。根は -1。</summary>
        public readonly List<int> JointParent = new List<int>();

        /// <summary>レストのワールド位置・回転（Unity 座標）。</summary>
        public Vector3[]    RestPos;
        public Quaternion[] RestRot;

        /// <summary>[フレーム, 関節] のワールド位置（Unity 座標）。</summary>
        public Vector3[,]    Pos;
        /// <summary>[フレーム, 関節] のワールドの回転差分 回転(F)·回転(レスト)⁻¹（Unity 座標）。</summary>
        public Quaternion[,] Delta;
        /// <summary>フレームごとの根の動き T（マッスルを持たないクリップでは単位）。</summary>
        public Matrix4x4[]   FrameRoot;

        public bool RootMotion;
        public int  MatchedTrackCount;
        public readonly List<string> SkippedIk = new List<string>();

        /// <summary>モーフ MeshContext 索引 → フレームごとの重み（0〜1、表情を足し合わせたもの）。</summary>
        public readonly Dictionary<int, float[]> MorphWeights = new Dictionary<int, float[]>();
        public int ExpressionTrackCount;
        public int ExpressionResolvedCount;
        public readonly List<string> UnresolvedExpressions = new List<string>();

        public float DurationSec => EndSec - StartSec;
    }

    public static class ModelMotionSampler
    {
        /// <summary>
        /// クリップをモデルへ当てながら標本を取る。モデルの姿勢は終わったら戻す。
        /// 失敗時は false と理由。
        /// </summary>
        public static bool Sample(ModelContext model, MotionClipDTO clip, ModelMotionSampleOptions options,
                                  out ModelMotionSamples samples, out string reason)
        {
            // out 引数はローカル関数から触れないので、ローカルで組んで最初に渡しておく。
            var s = new ModelMotionSamples();
            samples = s;
            reason = null;
            options = options ?? new ModelMotionSampleOptions();
            if (model == null) { reason = "モデルがありません"; return false; }
            if (clip == null)  { reason = "モーションがありません"; return false; }

            var applier = new MotionClipApplier { PositionScale = options.ClipPositionScale, AlignLegs = options.AlignLegs };
            CanonBoneMotion cbm = null;
            try
            {
                applier.SetClip(clip);
                applier.BuildMapping(model);
                var sk = applier.Skeleton;
                if (sk == null || sk.Nodes.Count == 0) { reason = "モデルの骨格を組めません"; return false; }
                s.Skeleton = sk;

                // ── 関節（IK を除く）と親 ──────────────────────────
                int nN = sk.Nodes.Count;
                var use = new bool[nN];
                for (int n = 0; n < nN; n++)
                {
                    var src = sk.SourceContext(model, n);
                    use[n] = src != null && !src.IsIK;
                    if (src != null && src.IsIK) s.SkippedIk.Add(sk.Nodes[n].Name);
                }
                var parentNode = new int[nN];
                for (int n = 0; n < nN; n++)
                {
                    int p = sk.ParentNodeOf(model, n), guard = nN + 1;
                    while (p >= 0 && !use[p] && guard-- > 0) p = sk.ParentNodeOf(model, p);
                    parentNode[n] = p;
                }

                var children = new List<int>[nN];
                for (int n = 0; n < nN; n++) children[n] = new List<int>();
                var roots = new List<int>();
                for (int n = 0; n < nN; n++)
                {
                    if (!use[n]) continue;
                    if (parentNode[n] >= 0) children[parentNode[n]].Add(n); else roots.Add(n);
                }
                if (roots.Count == 0) { reason = "書き出せる関節がありません"; return false; }

                void Visit(int n, int parentJ)
                {
                    int self = s.JointNode.Count;
                    s.JointNode.Add(n); s.JointParent.Add(parentJ);
                    foreach (int c in children[n]) Visit(c, self);
                }
                foreach (int rn in roots) Visit(rn, -1);
                int nJ = s.JointNode.Count;

                // ── レスト ─────────────────────────────────────
                s.RestPos = new Vector3[nJ];
                s.RestRot = new Quaternion[nJ];
                for (int j = 0; j < nJ; j++)
                {
                    var w = sk.RestWorldMatrix(model, s.JointNode[j]);
                    s.RestPos[j] = new Vector3(w.m03, w.m13, w.m23);
                    s.RestRot[j] = Rotation(w);
                }

                // ── 時刻 ───────────────────────────────────────
                float fps   = options.Fps > 0f ? options.Fps : (clip.frameRate > 0f ? clip.frameRate : 30f);
                float len   = MotionClipSerializer.Length(clip);
                float start = Mathf.Max(0f, options.StartSec);
                float end   = options.EndSec > start ? options.EndSec : len;
                if (end < start) end = start;
                int nF = Mathf.Max(1, Mathf.RoundToInt((end - start) * fps) + 1);
                s.Fps = fps; s.StartSec = start; s.EndSec = end; s.FrameCount = nF;

                // ── 根の動き（body）────────────────────────────────
                // rotationSource = "bakedBones" のクリップは bakedBones の Hips（回転と正規化位置）をそのまま使う。
                var bakedSampler = MotionClipDTO.BakedBonesArePrimary(clip) ? new MotionBakedBoneSampler(clip) : null;
                bool rootMotion = bakedSampler != null || (clip.muscles != null && clip.muscles.Count > 0);
                Vector3 hipsRest = Vector3.zero;
                float hipsH = 0f;
                MotionMuscleSampler sampler = null;
                float[] buf = null;
                if (rootMotion)
                {
                    int hipsNode = sk.NodeOfHumanoid(nameof(HumanBodyBones.Hips));
                    int hipsJ = hipsNode >= 0 ? s.JointNode.IndexOf(hipsNode) : -1;
                    if (hipsJ < 0) { reason = "モデルに Hips の割り当てがありません（マッスルのモーションには要ります）"; return false; }
                    hipsRest = s.RestPos[hipsJ];
                    hipsH = hipsRest.y;
                    if (hipsH <= 1e-6f) { reason = $"モデルの Hips のレストの高さが {hipsH} です"; return false; }
                    if (bakedSampler == null)
                    {
                        cbm = new CanonBoneMotion();
                        if (!cbm.Build(out reason)) return false;
                        sampler = new MotionMuscleSampler(clip, useBody: true);
                        buf = new float[HumanTrait.MuscleCount];
                    }
                }
                s.RootMotion = rootMotion;

                // ── 表情の展開（モーフ索引 → [(トラック, 重み)]）──────────
                var morphTerms = new Dictionary<int, List<(MotionExpressionTrackDTO track, float w)>>();
                if (options.SampleExpressions && clip.expressions != null)
                {
                    // 同じ名前のトラック（パス＝レンダラー違い）は最初の 1 本だけを当てる（MotionClipApplier と同じ）。
                    var usedExprNames = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var tr in clip.expressions)
                    {
                        if (tr == null) continue;
                        s.ExpressionTrackCount++;
                        if (tr.name != null && !usedExprNames.Add(tr.name)) continue;
                        var expr = MotionClipApplier.FindExpression(model, tr.name);
                        int before = morphTerms.Count;
                        bool any = false;
                        if (expr != null)
                            MotionClipApplier.ExpandToVertexMorphs(model, expr, 1f, (morphIndex, baseIndex, morphCtx, w) =>
                            {
                                if (!morphTerms.TryGetValue(morphIndex, out var list))
                                    morphTerms[morphIndex] = list = new List<(MotionExpressionTrackDTO, float)>();
                                list.Add((tr, w));
                                any = true;
                            });
                        if (any) s.ExpressionResolvedCount++;
                        else     s.UnresolvedExpressions.Add(tr.name);
                    }
                    foreach (var kv in morphTerms) s.MorphWeights[kv.Key] = new float[nF];
                }

                // ── フレーム ─────────────────────────────────────
                s.Pos       = new Vector3[nF, nJ];
                s.Delta     = new Quaternion[nF, nJ];
                s.FrameRoot = new Matrix4x4[nF];
                for (int f = 0; f < nF; f++)
                {
                    float t = Mathf.Min(start + f / fps, end);
                    applier.ApplyFrame(model, t);
                    if (f == 0)
                    {
                        s.MatchedTrackCount = applier.MatchedTrackCount;
                        if (s.MatchedTrackCount == 0) { reason = "モーションのトラックがモデルに 1 本も当たりません"; return false; }
                    }

                    Matrix4x4 T = Matrix4x4.identity;
                    if (rootMotion)
                    {
                        Quaternion qc;
                        Vector3 off;
                        if (bakedSampler != null)
                            bakedSampler.TrySampleHips(t, out qc, out off);
                        else
                        {
                            sampler.Sample(t, buf, cbm.RestMuscles, out var bp, out var br);
                            cbm.SetMuscles(buf, bp, br);
                            qc  = cbm.GetLocal(HumanBodyBones.Hips);
                            off = cbm.GetHipsOffset();
                        }
                        T = Matrix4x4.Translate(hipsRest + off * hipsH) * Matrix4x4.Rotate(qc) * Matrix4x4.Translate(-hipsRest);
                    }
                    s.FrameRoot[f] = T;

                    for (int j = 0; j < nJ; j++)
                    {
                        var F = T * NodeWorld(model, sk, applier, s.JointNode[j]);
                        s.Delta[f, j] = Rotation(F) * Quaternion.Inverse(s.RestRot[j]);
                        s.Pos[f, j]   = new Vector3(F.m03, F.m13, F.m23);
                    }

                    foreach (var kv in morphTerms)
                    {
                        float sum = 0f;
                        foreach (var (track, w) in kv.Value) sum += MotionCurveMath.EvaluateScalar(track, t) * w;
                        s.MorphWeights[kv.Key][f] = sum;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModelMotionSampler] {ex}");
                reason = ex.Message;
                return false;
            }
            finally
            {
                cbm?.Dispose();
                applier.ResetAllBones(model);
            }
        }

        // ノードの今のワールド行列。マッスル・path を当てたときは再生経路の控え（仮想ミラーを含む）、
        // boneName だけのクリップはモデルの WorldMatrix、実体の無い仮想関節はレスト。
        private static Matrix4x4 NodeWorld(ModelContext model, UnityClipVirtualSkeleton sk, MotionClipApplier applier, int node)
        {
            if (node < 0) return Matrix4x4.identity;
            if (applier.TryGetNodeWorldMatrix(node, out var w)) return w;
            var ctx = sk.TargetContext(model, node);
            return ctx != null ? ctx.WorldMatrix : sk.RestWorldMatrix(model, node);
        }

        /// <summary>行列の回転（BvhCodec と同じ取り方）。</summary>
        public static Quaternion Rotation(Matrix4x4 m) => Norm(VMD.VmdVrmAnimationExport.ProbeRotation(m));

        private static Quaternion Norm(Quaternion q)
        {
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return n > 1e-8f ? new Quaternion(q.x / n, q.y / n, q.z / n, q.w / n) : Quaternion.identity;
        }
    }
}
