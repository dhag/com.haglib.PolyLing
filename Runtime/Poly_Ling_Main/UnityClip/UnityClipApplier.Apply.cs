// UnityClipApplier.Apply.cs
// Unity クリップ適用：フレームの適用とレスト補正（Unity→MMD 完全リターゲット）。
// Runtime/Poly_Ling_Main/UnityClip/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UnityClip;

namespace Poly_Ling.UnityClip
{
    public partial class UnityClipApplier
    {
        // ================================================================
        // 適用
        // ================================================================

        public void ApplyFrame(ModelContext model, UnityClipDTO clip, float timeSec)
        {
            if (model == null || clip == null) return;
            if (_mappedModel != model || _mapping == null) BuildMapping(model);

            ClearNodeDeltas();

            int matched = 0;

            // 二次骨（袖/髪/スカート等）: どちらの方式でも常時適用
            PathMatchedCount = 0;
            PathTrackCount   = clip.bones != null ? clip.bones.Count : 0;
            UnresolvedPathTracks.Clear();
            if (clip.bones != null)
            {
                foreach (var track in clip.bones)
                {
                    int n = ApplyTrackAt(model, track, timeSec);
                    matched += n;
                    if (n > 0) PathMatchedCount++;
                    else if (track != null) UnresolvedPathTracks.Add(LastSegment(track.path));
                }
            }

            // 本体ボーン: 方式を解決してから適用する
            bool hasBaked = clip.bakedBones != null && clip.bakedBones.Count > 0;
            BodySource mode = BodyMode;
            if (mode == BodySource.Auto)
                mode = hasBaked ? BodySource.BakedBones : BodySource.Muscle;
            ResolvedBodyMode = mode;

            if (mode == BodySource.BakedBones)
            {
                if (HasSourceRest)
                {
                    // レスト補正あり: Unity→MMD 完全リターゲット（applyRetarget 移植）
                    matched += ApplyRetargetedBody(model, clip, timeSec);
                }
                else if (clip.bakedBones != null)
                {
                    // 未読込: 同一リグ用の従来経路（モデル rest デルタ）
                    foreach (var track in clip.bakedBones)
                        matched += ApplyTrackAt(model, track, timeSec);
                }
            }
            else
            {
                matched += ApplySelfMuscle(model, clip, timeSec);
            }

            MatchedTrackCount = matched;
            FinishFrame(model);
        }

        /// <summary>
        /// 受信したマッスル値（HumanTrait.MuscleName の並び）を当てる。ライブ受信用。
        /// valid[i] が false のマッスルは「トラック無し」と同じ扱いにする（その骨を当てない）。
        /// 計算はクリップのマッスル経路（ApplyMuscleValues）と同じ。path トラックは無い。
        /// </summary>
        public void ApplyMuscleFrame(ModelContext model, float[] values, bool[] valid, float timeSec)
        {
            if (model == null || values == null) return;
            if (_mappedModel != model || _mapping == null) BuildMapping(model);

            ClearNodeDeltas();

            PathMatchedCount = 0;
            PathTrackCount   = 0;
            UnresolvedPathTracks.Clear();
            ResolvedBodyMode = BodySource.Muscle;

            MatchedTrackCount = ApplyMuscleValues(model,
                (int mi, out float v) =>
                {
                    if (mi >= 0 && mi < values.Length && (valid == null || (mi < valid.Length && valid[mi])))
                    {
                        v = values[mi];
                        return true;
                    }
                    v = 0f;
                    return false;
                },
                timeSec);

            FinishFrame(model);
        }

        // フレーム適用の後始末（クリップ・ライブ受信で共通）。
        private void FinishFrame(ModelContext model)
        {
            LogDiagnosticsIfChanged();

            // 1 パス目: 実体ノードのデルタを反映してワールドを確定させる。
            model.ComputeWorldMatrices();

            // 2 パス目: 確定した実体側ワールドを足場に仮想ミラー鎖を解き、
            //           ミラー側コンテキストへ書き戻してからもう一度組み直す。
            if (ApplyVirtualMirror(model))
                model.ComputeWorldMatrices();

            CacheNodeWorlds(model);
        }

        // 1 トラックを timeSec でサンプルして適用。適用できたら 1。
        private int ApplyTrackAt(ModelContext model, UnityBoneTrackDTO track, float timeSec)
        {
            if (track == null || track.keys == null || track.keys.Count == 0) return 0;
            int node = ResolveNode(track.path);
            if (node < 0) return 0;
            if (_skeleton.SourceContext(model, node) == null) return 0;

            Vector3? sPos = SamplePosition(track, timeSec);
            Quaternion? sRot = SampleRotation(track, timeSec);

            // ベース（rest ローカル・ノード自身の枠）。
            // ミラーノードでは鏡像化済みの値になる（クリップは右半身の枠で来る）。
            Matrix4x4 baseMat = _skeleton.RestLocalMatrixOwn(model, node);
            _skeleton.RestLocalTRSOwn(model, node, out Vector3 restPos, out Quaternion restRot);

            Vector3 localPos = sPos.HasValue ? sPos.Value * PositionScale : restPos;
            Quaternion localRot = sRot.HasValue ? sRot.Value : restRot;

            // clip 絶対ローカル → デルタ（rest^-1 × clipLocal）
            Matrix4x4 clipLocal = Matrix4x4.TRS(localPos, localRot, Vector3.one);
            Matrix4x4 deltaMat = baseMat.inverse * clipLocal;
            Vector3 deltaPos = new Vector3(deltaMat.m03, deltaMat.m13, deltaMat.m23);
            SetNodeDelta(model, node, deltaPos, deltaMat.rotation);
            return 1;
        }

        // (b) muscles から本体ボーンのローカル回転を再構成して適用。
        //
        //   1. フレームのマッスル 95 本を正準骨格の Avatar へ当て、Unity 自身に
        //      正準（T ポーズ）の姿勢を解かせる（CanonMuscleSolver）。
        //      dof ごとの回転を自前で合成しないこと（UnityClipApplier.cs 冒頭の実測メモ）。
        //   2. 正準の姿勢（Hips から見たワールド）を、焼き込みの逆でモデルのワールド差分 Δ にする（DeltaOf）。
        //   3. レスト差分 D = RestW⁻¹ · Δ_祖先⁻¹ · Δ · RestW を BonePoseData に載せる。
        //
        //   ■ muscle=0 はモデルのレスト姿勢ではない（重要）
        //     Unity のマッスル 0 は、モデルのレストが T ポーズであっても T ポーズにならない。
        //     ひざ・ひじ・股関節が曲がった Unity 定義の固定ポーズになる。
        //     正準の姿勢はそれを含んだ絶対の姿勢として扱う（差分にしない）。
        //
        //   クリップがトラックを持たない dof は 0 として解かせる（以前の「その dof を掛けない」と同じ）。
        //   rest からのデルタとして BonePoseData に載せる。
        private int ApplySelfMuscle(ModelContext model, UnityClipDTO clip, float timeSec)
        {
            if (clip.muscles == null || clip.muscles.Count == 0) return 0;

            var muscleByName = new Dictionary<string, UnityMuscleTrackDTO>();
            foreach (var m in clip.muscles)
                if (m != null && !string.IsNullOrEmpty(m.name)) muscleByName[m.name] = m;

            var names = HumanTrait.MuscleName;
            return ApplyMuscleValues(model,
                (int mi, out float v) =>
                {
                    if (names != null && mi >= 0 && mi < names.Length
                        && muscleByName.TryGetValue(names[mi], out var mt))
                    {
                        v = SampleWeight(mt, timeSec);               // 正規化値 [-1,1]
                        return true;
                    }
                    v = 0f;
                    return false;
                },
                timeSec);
        }

        /// <summary>マッスル番号（HumanTrait.MuscleName の添字）の値を返す。無ければ false。</summary>
        private delegate bool MuscleValueSource(int muscleIndex, out float value);

        // マッスル値から本体ボーンのデルタを作って当てる（値の出どころだけが呼び出し元で違う）。
        // timeSec は内訳ログの表示にだけ使う。
        private int ApplyMuscleValues(ModelContext model, MuscleValueSource valueOf, float timeSec)
        {
            var muscleNames = HumanTrait.MuscleName;
            int boneCount = HumanTrait.BoneCount;
            int matched = 0;

            MuscleMatchedCount = 0;
            MuscleTargetCount  = 0;
            UnresolvedMuscleBones.Clear();

            if (!EnsureSolver(out string solverReason))
            {
                UnresolvedMuscleBones.Add("(正準 Avatar を組めません: " + solverReason + ")");
                return 0;
            }

            // フレームのマッスル値をそろえて Unity に解かせる（1 フレーム 1 回）
            int mc = HumanTrait.MuscleCount;
            if (_muscleBuf == null || _muscleBuf.Length != mc) _muscleBuf = new float[mc];
            for (int mi = 0; mi < mc; mi++)
                _muscleBuf[mi] = valueOf(mi, out float mv) ? mv : 0f;
            _solver.Solve(_muscleBuf);

            // 駆動されている Humanoid（dof のどれかにトラックがある）を先に集める。
            // 駆動されていない Humanoid は祖先に付いていく（DeltaOf）。
            _deltaMemo.Clear();
            _drivenKeys.Clear();
            for (int bi = 0; bi < boneCount; bi++)
            {
                for (int dof = 0; dof < 3; dof++)
                {
                    int mi0 = HumanTrait.MuscleFromBone(bi, dof);
                    if (mi0 < 0 || muscleNames == null || mi0 >= muscleNames.Length) continue;
                    if (valueOf(mi0, out _)) { _drivenKeys.Add(HumanTrait.BoneName[bi].Replace(" ", string.Empty)); break; }
                }
            }

            for (int bi = 0; bi < boneCount; bi++)
            {
                string boneName = HumanTrait.BoneName[bi];          // 例 "Left Upper Arm"（空白入り）
                string key = boneName.Replace(" ", string.Empty);    // 対応表キー "LeftUpperArm"

                // 駆動していないボーンは母数に入れない（未解決として報告しない）。
                if (!_drivenKeys.Contains(key)) continue;
                MuscleTargetCount++;

                int k = ResolveNode(key);
                if (k < 0) k = ResolveNode(boneName);
                if (k < 0)
                {
                    UnresolvedMuscleBones.Add(key);
                    continue;
                }

                // 姿勢の正本は実体側コンテキスト（ミラーノードは相方）。
                var ctx = _skeleton.SourceContext(model, k);
                if (ctx == null)
                {
                    UnresolvedMuscleBones.Add(key + "(ctx=null)");
                    continue;
                }

                if (!_solver.TryGetHipsRelativeWorld((HumanBodyBones)bi, out _))
                {
                    UnresolvedMuscleBones.Add(key + "(正準骨格に無い)");
                    continue;
                }

                // 正準の姿勢 → モデルのワールド差分 → レスト差分（UnityClipApplier.cs の恒久メモ）。
                var        fr       = FrameOf(key);
                string     anc      = AncestorOf(key);
                Quaternion delta    = DeltaOf(key);
                Quaternion ancDelta = DeltaOf(anc);
                Quaternion applied  = QuatNorm(Quaternion.Inverse(fr.RestW) * Quaternion.Inverse(ancDelta)
                                               * delta * fr.RestW);

                if (MuscleDebugLog && MuscleDebugBones != null && MuscleDebugBones.Contains(key))
                {
                    var dsb = new System.Text.StringBuilder();
                    dsb.Append("[UnityClipApplier/muscle] ").Append(key)
                       .Append("  t=").Append(timeSec.ToString("F3")).Append('\n');
                    for (int dof = 0; dof < 3; dof++)
                    {
                        int mi = HumanTrait.MuscleFromBone(bi, dof);
                        if (mi < 0 || muscleNames == null || mi >= muscleNames.Length) continue;
                        bool has = valueOf(mi, out float v);
                        dsb.Append("   dof").Append(dof).Append(' ').Append(muscleNames[mi])
                           .Append(has ? "  v=" + v.ToString("F4") : "  (トラック無し→0)").Append('\n');
                    }
                    dsb.Append("   A ").Append(AxAng(fr.A))
                       .Append("   Δ ").Append(AxAng(delta))
                       .Append("   祖先 ").Append(anc ?? "(なし)").Append(" Δ ").Append(AxAng(ancDelta)).Append('\n');
                    dsb.Append("   RestW ").Append(AxAng(fr.RestW))
                       .Append("   差分 ").Append(AxAng(applied)).Append('\n');
                    Debug.Log(dsb.ToString());
                }

                // rest からのデルタ（位置は変えない）
                SetNodeDelta(model, k, Vector3.zero, applied);
                MuscleMatchedCount++;
                matched++;
            }
            return matched;
        }

        // ================================================================
        // レスト補正（Unity→MMD 完全リターゲット）
        //   motion_timeline.html applyRetarget（isModel=false 経路）を逐語移植。
        //   「FK でワールド化 → ソース rest(RestW) 相対のワールド差分 → 左右ミラー
        //     → A/T 整列 → CANON 親相対のローカルへ戻す」。
        //   ソース rest（RestW/位置）は外部 UnityBone CSV v2（拡張C）から供給する。
        //   独自の座標変換は足さない（mir / ft / Mx は逐語）。
        // ================================================================

        // JS QR 準拠のクォータニオン [x,y,z,w]（world = QMul(parentWorld, local)）
        private struct Q
        {
            public float x, y, z, w;
            public Q(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
            public static Q Identity => new Q(0f, 0f, 0f, 1f);
            public Quaternion ToUnity() => new Quaternion(x, y, z, w);
            public static Q From(Quaternion q) => new Q(q.x, q.y, q.z, q.w);
        }

        private static Q QMul(Q a, Q b) => new Q(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        private static Q QConj(Q q) => new Q(-q.x, -q.y, -q.z, q.w);

        private static Q QNorm(Q q)
        {
            float n = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (n <= 1e-20f) n = 1f;
            return new Q(q.x / n, q.y / n, q.z / n, q.w / n);
        }

        // Y軸180°回転の共役（Unity→PMX: 両左手系で向き-Z差のみ、正則回転）
        private static Q QMir(Q q) => new Q(-q.x, q.y, -q.z, q.w);

        // 単位ベクトル a→b の最短弧（JS QR.ft 逐語）
        private static Q QFromTo(Vector3 a, Vector3 b)
        {
            float d = Mathf.Clamp(a.x * b.x + a.y * b.y + a.z * b.z, -1f, 1f);
            if (d > 0.999999f) return Q.Identity;
            if (d < -0.999999f)
            {
                Vector3 ax = Mathf.Abs(a.x) < 0.9f ? new Vector3(1f, 0f, 0f) : new Vector3(0f, 1f, 0f);
                Vector3 c0 = new Vector3(
                    a.y * ax.z - a.z * ax.y,
                    a.z * ax.x - a.x * ax.z,
                    a.x * ax.y - a.y * ax.x);
                float n0 = Mathf.Sqrt(c0.x * c0.x + c0.y * c0.y + c0.z * c0.z);
                if (n0 <= 1e-20f) n0 = 1f;
                return new Q(c0.x / n0, c0.y / n0, c0.z / n0, 0f);
            }
            Vector3 c = new Vector3(
                a.y * b.z - a.z * b.y,
                a.z * b.x - a.x * b.z,
                a.x * b.y - a.y * b.x);
            float w = 1f + d;
            float n = Mathf.Sqrt(c.x * c.x + c.y * c.y + c.z * c.z + w * w);
            if (n <= 1e-20f) n = 1f;
            return new Q(c.x / n, c.y / n, c.z / n, w / n);
        }

        private static Vector3 Mx(Vector3 v) => new Vector3(-v.x, v.y, -v.z);   // Y軸180°回転（位置/方向）

        // 正準(Humanoid)階層テーブル（motion_timeline.html CANON_PARENT / CANON_CHILD と同一）
        private static Dictionary<string, string> _canonParent;
        private static Dictionary<string, string> _canonChild;

        private static void EnsureCanon()
        {
            if (_canonParent != null) return;
            var P = new Dictionary<string, string>
            {
                { "Hips", null }, { "Spine", "Hips" }, { "Chest", "Spine" }, { "UpperChest", "Chest" },
                { "Neck", "UpperChest" }, { "Head", "Neck" }, { "Jaw", "Head" },
                { "LeftEye", "Head" }, { "RightEye", "Head" },
            };
            var C = new Dictionary<string, string>
            {
                { "Hips", "Spine" }, { "Spine", "Chest" }, { "Chest", "Neck" },
                { "UpperChest", "Neck" }, { "Neck", "Head" },
            };
            string[] fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
            foreach (var s in new[] { "Left", "Right" })
            {
                P[s + "Shoulder"] = "UpperChest"; P[s + "UpperArm"] = s + "Shoulder";
                P[s + "LowerArm"] = s + "UpperArm"; P[s + "Hand"] = s + "LowerArm";
                P[s + "UpperLeg"] = "Hips"; P[s + "LowerLeg"] = s + "UpperLeg";
                P[s + "Foot"] = s + "LowerLeg"; P[s + "Toes"] = s + "Foot";

                C[s + "Shoulder"] = s + "UpperArm"; C[s + "UpperArm"] = s + "LowerArm"; C[s + "LowerArm"] = s + "Hand";
                C[s + "UpperLeg"] = s + "LowerLeg"; C[s + "LowerLeg"] = s + "Foot"; C[s + "Foot"] = s + "Toes";

                foreach (var fg in fingers)
                {
                    P[s + fg + "Proximal"] = s + "Hand";
                    P[s + fg + "Intermediate"] = s + fg + "Proximal";
                    P[s + fg + "Distal"] = s + fg + "Intermediate";

                    C[s + fg + "Proximal"] = s + fg + "Intermediate";
                    C[s + fg + "Intermediate"] = s + fg + "Distal";
                }
            }
            _canonParent = P;
            _canonChild = C;
        }
    }
}
