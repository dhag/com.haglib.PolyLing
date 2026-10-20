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
        /// マッスルトラックの timeSec での値（キー間は線形）。
        /// マッスル → 回転は CanonMuscleSolver（Unity）で解くこと。
        /// 以前ここにあった「dof ごとの回転を順に掛ける合成」（TryGetCanonLocalRotation）は
        /// Unity と最大 57.81 度食い違ったため削除した（UnityClipApplier.cs 冒頭の実測メモ）。
        /// </summary>
        public static float SampleMuscleWeight(UnityMuscleTrackDTO track, float timeSec)
            => SampleWeight(track, timeSec);

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

    }
}
