// UnityClipApplier.Canon.cs
// Unity クリップ適用：正準マッスル基準テーブルと正準定数の公開。
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
        // 正準マッスル基準テーブル（T ポーズ基準・Unity の定義値）
        // ----------------------------------------------------------------
        //   ファイル冒頭の恒久メモを必ず読むこと。ここは導出値ではなく定義値である。
        //
        //   CanonMuscleTable 1 行の書式（'|' 区切り）:
        //     Humanoid名 | Zero(x y z w) | dof0 | dof1 | dof2
        //   dof の書式:
        //     軸x 軸y 軸z 最小度 最大度      （そのボーンに該当マッスルが無ければ "-"）
        //   軸はボーン局所（＝ T ポーズではモデル空間）での回転軸。
        //   最小度・最大度は Zero からの相対角。HumanTrait の可動域とは一致しない
        //   （ツイストは捩りボーンへ分配されるため実効はおよそ半分になる）。
        //
        //   CanonDirTable は T ポーズでの「自ボーン → 正準子ボーン」方向。
        //   ターゲットモデルの rest 方向との最短弧を取って軸を整列するのに使う。
        // ================================================================

        private class CanonMuscleBone
        {
            public Quaternion Zero = Quaternion.identity;
            public readonly bool[]    Has    = new bool[3];
            public readonly Vector3[] Axis   = new Vector3[3];
            public readonly float[]   MinDeg = new float[3];
            public readonly float[]   MaxDeg = new float[3];
        }

        private static Dictionary<string, CanonMuscleBone> _canonMuscle;
        private static Dictionary<string, Vector3>         _canonDir;

        private static void EnsureCanonMuscle()
        {
            if (_canonMuscle != null) return;

            var m = new Dictionary<string, CanonMuscleBone>();
            foreach (var row in CanonMuscleTable)
            {
                var col = row.Split('|');
                if (col.Length < 5) continue;
                var cb = new CanonMuscleBone();

                var z = col[1].Split(' ');
                if (z.Length >= 4)
                    cb.Zero = QuatNorm(new Quaternion(ParseF(z[0]), ParseF(z[1]), ParseF(z[2]), ParseF(z[3])));

                for (int dof = 0; dof < 3; dof++)
                {
                    string f = col[2 + dof];
                    if (f == "-") continue;
                    var p = f.Split(' ');
                    if (p.Length < 5) continue;
                    var ax = new Vector3(ParseF(p[0]), ParseF(p[1]), ParseF(p[2]));
                    if (ax.sqrMagnitude <= 1e-12f) continue;
                    cb.Has[dof]    = true;
                    cb.Axis[dof]   = ax.normalized;
                    cb.MinDeg[dof] = ParseF(p[3]);
                    cb.MaxDeg[dof] = ParseF(p[4]);
                }
                m[col[0]] = cb;
            }
            _canonMuscle = m;

            var d = new Dictionary<string, Vector3>();
            foreach (var row in CanonDirTable)
            {
                var col = row.Split('|');
                if (col.Length < 2) continue;
                var p = col[1].Split(' ');
                if (p.Length < 3) continue;
                var v = new Vector3(ParseF(p[0]), ParseF(p[1]), ParseF(p[2]));
                if (v.sqrMagnitude <= 1e-12f) continue;
                d[col[0]] = v.normalized;
            }
            _canonDir = d;
        }

        // ================================================================
        // 正準定数の公開（モデル非依存の変換用）
        // ----------------------------------------------------------------
        //   CanonMuscleTable / CanonDirTable / _canonParent は定義値であり、
        //   このクラスが唯一の置き場である。モデル非依存の VRMA 変換
        //   （UnityClipCanonVrmAnimation）は定数を複製せずここから引く。
        // ================================================================

        /// <summary>正準テーブルが持つ Humanoid 名（CanonMuscleTable の並び）。</summary>
        public static IReadOnlyList<string> CanonHumanoidNames
        {
            get
            {
                if (_canonNames == null)
                {
                    var list = new List<string>(CanonMuscleTable.Length);
                    foreach (var row in CanonMuscleTable)
                    {
                        int bar = row.IndexOf('|');
                        if (bar > 0) list.Add(row.Substring(0, bar));
                    }
                    _canonNames = list;
                }
                return _canonNames;
            }
        }
        private static List<string> _canonNames;

        /// <summary>正準階層の親。Hips は null。名前が無ければ false。</summary>
        public static bool TryGetCanonParent(string humanoidName, out string parent)
        {
            EnsureCanon();
            return _canonParent.TryGetValue(humanoidName, out parent);
        }

        /// <summary>T ポーズでの「自ボーン → 正準子ボーン」方向（単位ベクトル）。</summary>
        public static bool TryGetCanonDir(string humanoidName, out Vector3 dir)
        {
            EnsureCanonMuscle();
            return _canonDir.TryGetValue(humanoidName, out dir);
        }

        // Humanoid 名（空白なし）→ HumanTrait のボーン索引。
        private static Dictionary<string, int> _canonBoneIndex;

        private static int CanonBoneIndex(string humanoidName)
        {
            if (_canonBoneIndex == null)
            {
                var d = new Dictionary<string, int>();
                var names = HumanTrait.BoneName;
                for (int i = 0; i < names.Length; i++)
                    d[names[i].Replace(" ", string.Empty)] = i;
                _canonBoneIndex = d;
            }
            return _canonBoneIndex.TryGetValue(humanoidName, out int bi) ? bi : -1;
        }

        /// <summary>
        /// T ポーズ基準（モデル非依存）でマッスル値からローカル回転を作る。
        ///
        /// GetCanonEntry を A = RestW = RestL = 単位で通したのと同じ式であり、
        /// 掛ける順序も ApplySelfMuscle と同じにしてある。
        ///   Zero = cb.Zero
        ///   ext  = Zero · AngleAxis(Min/MaxDeg[dof], Axis[dof])
        ///   full = Zero⁻¹ · ext
        ///   d    = Slerp(identity, full, |v|) を 3 dof 合成
        ///   L    = Zero · d
        /// レストが T ポーズなら L がそのまま正規化 Humanoid のローカル回転になる。
        /// </summary>
        /// <returns>クリップがそのボーンを 1 dof も駆動していなければ false。</returns>
        public static bool TryGetCanonLocalRotation(
            string humanoidName,
            IReadOnlyDictionary<string, UnityMuscleTrackDTO> muscleByName,
            float timeSec,
            out Quaternion local)
        {
            local = Quaternion.identity;
            if (string.IsNullOrEmpty(humanoidName) || muscleByName == null) return false;

            EnsureCanonMuscle();
            if (!_canonMuscle.TryGetValue(humanoidName, out var cb)) return false;

            int bi = CanonBoneIndex(humanoidName);
            if (bi < 0) return false;

            var muscleNames = HumanTrait.MuscleName;
            Quaternion zero  = cb.Zero;
            Quaternion delta = Quaternion.identity;
            bool any = false;

            for (int dof = 0; dof < 3; dof++)
            {
                if (!cb.Has[dof]) continue;

                int mi = HumanTrait.MuscleFromBone(bi, dof);
                if (mi < 0 || muscleNames == null || mi >= muscleNames.Length) continue;
                if (!muscleByName.TryGetValue(muscleNames[mi], out var mt)) continue;

                float v = SampleWeight(mt, timeSec);

                Quaternion ext  = zero * Quaternion.AngleAxis(
                                      v >= 0f ? cb.MaxDeg[dof] : cb.MinDeg[dof], cb.Axis[dof]);
                Quaternion full = Quaternion.Inverse(zero) * ext;
                Quaternion d    = Quaternion.Slerp(Quaternion.identity, full, Mathf.Min(1f, Mathf.Abs(v)));
                delta = delta * d;
                any = true;
            }

            if (!any) return false;

            local = QuatNorm(zero * delta);
            return true;
        }

        // present で親をたどる（欠損はスキップ）
        private static string ParentOf(string cn, HashSet<string> present)
        {
            _canonParent.TryGetValue(cn, out var p);
            while (p != null && !present.Contains(p)) _canonParent.TryGetValue(p, out p);
            return p;
        }

        private static int DepthOf(string cn)
        {
            int d = 0;
            _canonParent.TryGetValue(cn, out var p);
            while (p != null) { d++; _canonParent.TryGetValue(p, out p); }
            return d;
        }

        // rest 方向（骨→CANON子の単位ベクトル）
        private static bool DirRest(Dictionary<string, Vector3> pos, string cn, out Vector3 dir)
        {
            dir = Vector3.zero;
            if (!_canonChild.TryGetValue(cn, out var ch) || ch == null) return false;
            if (!pos.TryGetValue(cn, out var pc) || !pos.TryGetValue(ch, out var pch)) return false;
            Vector3 v = pch - pc;
            float n = v.magnitude;
            if (n <= 1e-9f) return false;
            dir = v / n;
            return true;
        }

        // 本体レスト補正の本体：applyRetarget(isModel=false) を移植し timeSec で適用。
        private int ApplyRetargetedBody(ModelContext model, UnityClipDTO clip, float timeSec)
        {
            if (clip.bakedBones == null || clip.bakedBones.Count == 0) return 0;
            if (_sourceRest == null || _sourceRest.Count == 0) return 0;
            EnsureCanon();

            // byCanon: Humanoid名（= bakedBones.path）→ トラック
            var byCanon = new Dictionary<string, UnityBoneTrackDTO>();
            foreach (var t in clip.bakedBones)
                if (t != null && t.keys != null && t.keys.Count > 0 && !string.IsNullOrEmpty(t.path))
                    byCanon[t.path] = t;
            if (byCanon.Count == 0) return 0;

            var present = new HashSet<string>(byCanon.Keys);
            var order = new List<string>(byCanon.Keys);
            order.Sort((a, b) => DepthOf(a) - DepthOf(b));

            // ターゲット rest 位置（モデル空間 = BindPose.inverse の並進）
            var tp = new Dictionary<string, Vector3>();
            foreach (var cn in order)
            {
                int master = ResolveMasterIndex(cn);
                if (master < 0 || master >= model.MeshContextList.Count) continue;
                var ctx = model.MeshContextList[master];
                if (ctx == null) continue;
                Matrix4x4 world = ctx.BindPose.inverse;
                // Mx(Y軸180°回転)が向きを担うため、tp は Z 反転しない（二重反転回避）
                tp[cn] = new Vector3(world.m03, world.m13, world.m23);
            }

            // ソース rest 位置
            var sp = new Dictionary<string, Vector3>();
            foreach (var kv in _sourceRest) sp[kv.Key] = kv.Value.RestPos;

            // 整列 A[cn] = ft(ターゲットrest方向 → ミラーしたソースrest方向)。子が無ければ単位。
            var Aq = new Dictionary<string, Q>();
            foreach (var cn in order)
            {
                bool hasDs = DirRest(sp, cn, out var ds);
                bool hasDt = DirRest(tp, cn, out var dt);
                Aq[cn] = (hasDs && hasDt) ? QFromTo(dt, Mx(ds)) : Q.Identity;
            }

            // timeSec で local をサンプル
            var Lsrc = new Dictionary<string, Q>();
            foreach (var cn in order)
            {
                Quaternion? s = SampleRotation(byCanon[cn], timeSec);
                Lsrc[cn] = s.HasValue ? Q.From(s.Value) : Q.Identity;
            }

            // FK でワールド化
            var W = new Dictionary<string, Q>();
            foreach (var cn in order)
            {
                string p = ParentOf(cn, present);
                W[cn] = (p != null) ? QMul(W[p], Lsrc[cn]) : Lsrc[cn];
            }

            // ワールド差分 → ミラー＋整列 → CANON親相対ローカル → 適用
            var Wt = new Dictionary<string, Q>();
            int matched = 0;
            foreach (var cn in order)
            {
                Q srcRestW = _sourceRest.TryGetValue(cn, out var sr) ? Q.From(sr.RestW) : Q.Identity;
                Q E = QMul(W[cn], QConj(srcRestW));         // ワールド差分（レスト相対）
                Wt[cn] = QMul(QMir(E), Aq[cn]);             // 左右ミラー＋A/T整列
                string p = ParentOf(cn, present);
                Q outLocal = (p != null) ? QNorm(QMul(QConj(Wt[p]), Wt[cn])) : QNorm(Wt[cn]);

                if (ApplyLocalRotationToBone(model, cn, outLocal)) matched++;
            }
            return matched;
        }

        // CANON名（= Humanoid名）のモデルボーンへ CANON親相対ローカル回転を適用（回転のみ・位置は rest 維持）。
        private bool ApplyLocalRotationToBone(ModelContext model, string canonName, Q outLocal)
        {
            int node = ResolveNode(canonName);
            if (node < 0) return false;
            var ctx = _skeleton.SourceContext(model, node);
            if (ctx == null) return false;

            // outLocal は「ターゲット rest 回転 = identity」前提（motion_timeline と同じ MMD 規約）の
            // CANON 親相対ローカル回転。PMX ボーンはボーン整列の rest 回転 R(=BoneModelRotation, 非identity)
            // を持つため、上書きすると R を捨てて全ボーンが誤配向になる。
            // VMDApplier と同じく delta = R^-1 * outLocal * R を rest（baseMat）へのデルタとして適用する（回転のみ）。
            Q R = Q.From(ctx.BoneModelRotation);
            Q delta = QMul(QConj(R), QMul(outLocal, R));
            SetNodeDelta(model, node, Vector3.zero, delta.ToUnity());
            return true;
        }
    }
}
