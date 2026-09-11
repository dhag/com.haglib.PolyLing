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
        //   経路は 1 本だけである。Zero / MinQ / MaxQ を使う経路に統一した。
        //     dof 毎に「Zero → ±1 のローカル回転」からデルタ
        //       full_d = Zero^-1 · Extreme_d
        //     を作り、Slerp(identity, full_d, |v|) で v 倍したものを 3 dof 合成して d(v) を得る。
        //
        //   Zero / MinQ / MaxQ の出どころは 2 通りあるが、以後の計算は同一:
        //     (1) 外部 UnityLimit CSV の Measured 行（ソースアバターの実測）
        //     (2) 無いときは CanonMuscleTable（T ポーズ基準の Unity 定義値）を
        //         ターゲットの rest 方向へ整列して合成した既定 entry
        //     ※ かつて存在した「dof を X/Y/Z へ直接入れて Quaternion.Euler で組む」
        //       近似経路は削除した。ひざが Z 軸回りに曲がる原因だった。復活させないこと。
        //
        //   ■ Zero はモデルのレスト姿勢ではない（重要）
        //     Unity のマッスル 0 は、モデルのレストが T ポーズであっても T ポーズにならない。
        //     ひざ・ひじ・股関節が曲がった Unity 定義の固定ポーズになる。
        //     例: ひざは一方向にしか曲がらないため Stretch の範囲 -80〜+80 の中央（0）が
        //     「80 度曲がった位置」、+1 が伸展位になる。
        //
        //     Unity 側のローカル回転は        L(v) = Zero · d(v)
        //     PolyLing が入れるのはレスト差分  D    = RestL^-1 · Zero · d(v)
        //
        //     RestL は PolyLing 自身が持つレスト・ローカル回転（ctx.BoneTransform）。
        //     d(v) をそのまま入れると Zero→rest 分（ひざで約 80 度）が丸ごと過剰になり、
        //     ひざが逆に曲がる。この補正は実測・既定のどちらでも常時必要である。
        //
        //   rest からのデルタとして BonePoseData に載せる。
        private int ApplySelfMuscle(ModelContext model, UnityClipDTO clip, float timeSec)
        {
            if (clip.muscles == null || clip.muscles.Count == 0) return 0;

            var muscleByName = new Dictionary<string, UnityMuscleTrackDTO>();
            foreach (var m in clip.muscles)
                if (m != null && !string.IsNullOrEmpty(m.name)) muscleByName[m.name] = m;

            var muscleNames = HumanTrait.MuscleName;
            int boneCount = HumanTrait.BoneCount;
            int matched = 0;

            MuscleMatchedCount = 0;
            MuscleTargetCount  = 0;
            UnresolvedMuscleBones.Clear();

            for (int bi = 0; bi < boneCount; bi++)
            {
                string boneName = HumanTrait.BoneName[bi];          // 例 "Left Upper Arm"（空白入り）
                string key = boneName.Replace(" ", string.Empty);    // 対応表キー "LeftUpperArm"

                // このボーンをクリップが実際に駆動しているか（dof のいずれかにトラックがあるか）。
                // 駆動していないボーンは母数に入れない（未解決として報告しない）。
                bool driven = false;
                for (int dof = 0; dof < 3 && !driven; dof++)
                {
                    int mi0 = HumanTrait.MuscleFromBone(bi, dof);
                    if (mi0 < 0 || muscleNames == null || mi0 >= muscleNames.Length) continue;
                    if (muscleByName.ContainsKey(muscleNames[mi0])) driven = true;
                }
                if (!driven) continue;
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

                // 外部 UnityLimit CSV の行（Humanoid 列挙名で引く）。
                // 無い / 実測列を持たない場合は、T ポーズ基準の Unity 定義値から
                // 既定 entry を合成する（自動補正）。
                MuscleLimitEntry lim = null;
                if (_muscleLimits != null) _muscleLimits.TryGetValue(key, out lim);
                bool fromCsv = lim != null && lim.Measured;
                if (!fromCsv) lim = GetCanonEntry(key);
                if (lim == null)
                {
                    UnresolvedMuscleBones.Add(key + "(既定なし)");
                    continue;
                }

                Quaternion delta = Quaternion.identity;
                bool any = false;

                // 内訳ログ（対象ボーンのみ）
                bool dbg = MuscleDebugLog && MuscleDebugBones != null && MuscleDebugBones.Contains(key);
                System.Text.StringBuilder dsb = null;
                if (dbg)
                {
                    dsb = new System.Text.StringBuilder();
                    dsb.Append("[UnityClipApplier/muscle] ").Append(key)
                       .Append("  t=").Append(timeSec.ToString("F3"))
                       .Append("  src=").Append(
                            fromCsv ? "CSV実測"
                                    : (lim.FromModelLimit ? "モデル可動域" : "既定(Tポーズ基準)")).Append('\n');
                    dsb.Append("   Zero  ").Append(AxAng(lim.Zero))
                       .Append("  min(deg)=").Append(lim.Min)
                       .Append(" max(deg)=").Append(lim.Max).Append('\n');
                }

                for (int dof = 0; dof < 3; dof++)
                {
                    int mi = HumanTrait.MuscleFromBone(bi, dof);
                    if (mi < 0 || muscleNames == null || mi >= muscleNames.Length)
                    {
                        if (dbg) dsb.Append("   dof").Append(dof).Append(": マッスル無し\n");
                        continue;
                    }
                    if (!muscleByName.TryGetValue(muscleNames[mi], out var mt))
                    {
                        if (dbg) dsb.Append("   dof").Append(dof).Append(": クリップにトラック無し (")
                                    .Append(muscleNames[mi]).Append(")\n");
                        continue;
                    }

                    float v = SampleWeight(mt, timeSec);                 // 正規化値 [-1,1]

                    // Zero 基準のデルタを |v| だけ効かせる（v=0 で identity）
                    Quaternion ext  = v >= 0f ? lim.MaxQ[dof] : lim.MinQ[dof];
                    Quaternion full = Quaternion.Inverse(lim.Zero) * ext;
                    Quaternion d    = Quaternion.Slerp(Quaternion.identity, full, Mathf.Min(1f, Mathf.Abs(v)));
                    delta = delta * d;

                    if (dbg)
                    {
                        dsb.Append("   dof").Append(dof).Append(' ').Append(muscleNames[mi])
                           .Append("  v=").Append(v.ToString("F4"))
                           .Append("  side=").Append(v >= 0f ? "Max" : "Min").Append('\n');
                        dsb.Append("      MinQ ").Append(AxAng(lim.MinQ[dof]))
                           .Append("   MaxQ ").Append(AxAng(lim.MaxQ[dof])).Append('\n');
                        dsb.Append("      full ").Append(AxAng(full))
                           .Append("   ->d ").Append(AxAng(d)).Append('\n');
                    }
                    any = true;
                }
                if (!any) continue;

                // Zero（マッスル0）とモデルのレスト姿勢の差を打ち消す。常時適用する。
                //   D = RestL^-1 · Zero · d(v)
                // Unity のマッスル 0 は T ポーズではなく、ひざ・ひじ・股関節が曲がった
                // 固定ポーズである。この補正を外すとひざが逆に曲がる。
                // 実測・既定のどちらの entry でも必要。条件を付けないこと。
                // ミラーノードでは自身の枠（右半身の枠）で見た rest を使う。
                Quaternion restL = _skeleton.RestLocalRotation(model, k);
                Quaternion zeroFix = Quaternion.Inverse(restL) * lim.Zero;
                delta = zeroFix * delta;

                Quaternion applied = delta;

                if (dbg)
                {
                    dsb.Append("   RestL ").Append(AxAng(restL))
                       .Append("   RestL^-1*Zero ").Append(AxAng(zeroFix)).Append('\n');
                    dsb.Append("   合成 delta ").Append(AxAng(applied)).Append('\n');
                    // モデル空間へ写した向き（R = BoneModelRotation）。ボーンがどちらへ回るかの確認用。
                    Quaternion R = ctx.BoneModelRotation;
                    Quaternion dWorld = R * applied * Quaternion.Inverse(R);
                    dsb.Append("   R(BoneModelRotation) ").Append(AxAng(R))
                       .Append("   モデル空間 ").Append(AxAng(dWorld)).Append('\n');
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
