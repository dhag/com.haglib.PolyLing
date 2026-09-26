// CanonMuscleSolver.cs
// ============================================================
// マッスル値 → 正準骨格（T ポーズ）のローカル回転（Unity の定義そのもの）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/UnityClip/ に配置。
//
// ■ なぜ Unity に解かせるか（実測・恒久メモ・削除禁止）
//   以前は CanonMuscleTable（muscle=0 の姿勢・dof ごとの軸と可動端）から
//   dof ごとの回転を順に掛けて合成していた（UnityClipApplier.ApplyMuscleValues ほか）。
//   2026-09-26 に正準骨格の Avatar へ HumanPoseHandler.SetHumanPose で当てて比べた結果:
//     全 0                      53 本   最大 0.00°
//     1 本だけ ±0.5 / ±1       372 件  最大 0.00°
//     同一ボーンの dof を同時   93 件   最大 57.81°（上腕。前腕・すね 29.29°、太もも 22.34°、
//                                              背骨・首・頭 19.63°）
//   表の値は正しいが、dof の合成規則が Unity と違う。合成は自前で再現せず Unity に任せる。
//
// ■ 使い方
//   Shared(limits) で可動域ごとに 1 つの解法を得る（同じ可動域なら共有）。
//   Solve(muscles) のあと TryGetLocal で読む。次の Solve までの値。
//   得られるのは「正準骨格（レストの全ローカル回転が単位）での」ローカル回転。
//   モデルへ当てるときは UnityClipApplier の枠の移し替え（A・P・ΔL）を掛ける。
//
// ■ 共有の理由
//   骨格と Avatar は GameObject を伴う。呼び出し元ごとに作ると後始末の責任が散るので、
//   可動域の組ごとに 1 つだけ持つ。Play 終了などで破棄されていたら作り直す。
// ============================================================

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Poly_Ling.Motion;

namespace Poly_Ling.UnityClip
{
    public sealed class CanonMuscleSolver
    {
        /// <summary>正準骨格の骨長。マッスルは正規化量なので値に依らない。</summary>
        public const float BoneLength = 0.1f;

        private static readonly Dictionary<string, CanonMuscleSolver> _shared
            = new Dictionary<string, CanonMuscleSolver>();

        private readonly CanonHumanPoseRig _rig = new CanonHumanPoseRig();
        private HumanPose _pose;

        private CanonMuscleSolver() { }

        /// <summary>
        /// 可動域の組に対応する解法。limits が null / 空なら Unity 既定。
        /// 失敗時は null と理由。
        /// </summary>
        public static CanonMuscleSolver Shared(IReadOnlyDictionary<HumanBodyBones, HumanLimit> limits, out string reason)
        {
            reason = null;
            string key = KeyOf(limits);
            if (_shared.TryGetValue(key, out var s) && s.IsAlive) return s;

            s = new CanonMuscleSolver();
            if (!s._rig.Build(BoneLength, out reason, limits)) return null;
            s._pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
            _shared[key] = s;
            return s;
        }

        private bool IsAlive => _rig.Skeleton != null && _rig.Skeleton.Root != null;

        /// <summary>マッスル値（HumanTrait.MuscleName の並び）を当てる。</summary>
        public void Solve(float[] muscles)
        {
            int n = _pose.muscles.Length;
            for (int i = 0; i < n; i++)
                _pose.muscles[i] = (muscles != null && i < muscles.Length) ? muscles[i] : 0f;
            _pose.bodyPosition = new Vector3(0f, 1f, 0f);
            _pose.bodyRotation = Quaternion.identity;
            _rig.SetHumanPose(ref _pose);
        }

        /// <summary>直近の Solve での正準ローカル回転。正準骨格に無いボーンは false。</summary>
        public bool TryGetLocal(HumanBodyBones bone, out Quaternion local)
        {
            local = Quaternion.identity;
            if (_rig.Skeleton == null || !_rig.Skeleton.HumanBones.TryGetValue(bone, out var t) || t == null)
                return false;
            local = t.localRotation;
            return true;
        }

        /// <summary>直近の Solve での、Hips から見た正準ワールド回転。正準骨格に無いボーンは false。</summary>
        public bool TryGetHipsRelativeWorld(HumanBodyBones bone, out Quaternion rel)
        {
            rel = Quaternion.identity;
            if (_rig.Skeleton == null) return false;
            if (!_rig.Skeleton.HumanBones.TryGetValue(bone, out var t) || t == null) return false;
            if (!_rig.Skeleton.HumanBones.TryGetValue(HumanBodyBones.Hips, out var h) || h == null) return false;
            rel = Quaternion.Inverse(h.rotation) * t.rotation;
            return true;
        }

        // 可動域の組を文字列にする（同じ組なら同じ解法を返すため）。
        private static string KeyOf(IReadOnlyDictionary<HumanBodyBones, HumanLimit> limits)
        {
            if (limits == null || limits.Count == 0) return "";
            var keys = new List<HumanBodyBones>(limits.Keys);
            keys.Sort();
            var sb = new StringBuilder();
            foreach (var k in keys)
            {
                var l = limits[k];
                sb.Append((int)k).Append(':').Append(l.useDefaultValues ? 1 : 0);
                if (!l.useDefaultValues)
                {
                    Append(sb, l.min); Append(sb, l.max); Append(sb, l.center);
                    sb.Append(',').Append(l.axisLength.ToString("R", CultureInfo.InvariantCulture));
                }
                sb.Append(';');
            }
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, Vector3 v)
        {
            sb.Append(',').Append(v.x.ToString("R", CultureInfo.InvariantCulture))
              .Append(',').Append(v.y.ToString("R", CultureInfo.InvariantCulture))
              .Append(',').Append(v.z.ToString("R", CultureInfo.InvariantCulture));
        }
    }
}
