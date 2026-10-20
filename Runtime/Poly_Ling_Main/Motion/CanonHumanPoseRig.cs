// CanonHumanPoseRig.cs
// ============================================================
// 正準骨格（VRM T ポーズ）＋ Humanoid Avatar ＋ HumanPoseHandler の一式
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。
//
// ■ 何のためか
//   ボーンのローカル回転 ⇄ マッスル値（と RootT / RootQ）を Unity に解かせる口。
//   使う側：CanonMuscleSolver（再生）、CanonBoneMotion（bakedBones・VRMA）、
//           MediaPipePoseSolver（MediaPipe の点 → マッスル）、VmdMotionBake（VMD＋モデル → マッスル）。
//
// ■ 骨格
//   CanonSkeleton（正準骨格の唯一の置き場）。レストで全ノードのローカル回転が単位。
//   HumanDescription は mocopi 公式と同じ値（twist 0.5、stretch 0.05、feetSpacing 0、
//   hasTranslationDoF false）。この値は MediaPipePoseSolver から移したもので、変えていない。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Motion
{
    public sealed class CanonHumanPoseRig : IDisposable
    {
        /// <summary>正準骨格。Build 後に有効。</summary>
        public CanonSkeleton Skeleton { get; private set; }

        /// <summary>Humanoid ボーンを親から子の順に並べたもの。ワールド回転の代入順に使う。</summary>
        public readonly List<KeyValuePair<HumanBodyBones, Transform>> Ordered
            = new List<KeyValuePair<HumanBodyBones, Transform>>();

        private Avatar           _avatar;
        private HumanPoseHandler _handler;

        /// <summary>正準骨格と Avatar を組む。失敗時は false と理由。</summary>
        /// <param name="limits">ボーンごとの可動域の上書き。無いボーンは Unity 既定（useDefaultValues）。</param>
        public bool Build(out string reason,
                          IReadOnlyDictionary<HumanBodyBones, HumanLimit> limits = null)
        {
            Dispose();
            Skeleton = CanonSkeleton.Build();

            var human = new List<HumanBone>();
            var boneNames = HumanTrait.BoneName;
            for (int bi = 0; bi < boneNames.Length; bi++)
            {
                if (!Enum.TryParse<HumanBodyBones>(boneNames[bi].Replace(" ", string.Empty), out var hbb)) continue;
                if (!Skeleton.HumanBones.TryGetValue(hbb, out var t)) continue;
                var hb = new HumanBone { humanName = boneNames[bi], boneName = t.name };
                if (limits != null && limits.TryGetValue(hbb, out var lim))
                    hb.limit = lim;
                else
                    hb.limit.useDefaultValues = true;
                human.Add(hb);
            }

            var skeleton = new List<SkeletonBone>();
            AddSkeleton(Skeleton.Root.transform, skeleton);

            var desc = new HumanDescription
            {
                human = human.ToArray(),
                skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0.0f,
                hasTranslationDoF = false,
            };
            _avatar = AvatarBuilder.BuildHumanAvatar(Skeleton.Root, desc);
            if (_avatar == null || !_avatar.isValid || !_avatar.isHuman)
            {
                reason = "正準骨格から Humanoid の Avatar を組めませんでした";
                Dispose();
                return false;
            }
            _avatar.hideFlags = HideFlags.HideAndDontSave;
            _handler = new HumanPoseHandler(_avatar, Skeleton.Root.transform);

            // 親から子の順（CanonSkeleton.Rows がその順）
            foreach (var r in CanonSkeleton.Rows)
                Ordered.Add(new KeyValuePair<HumanBodyBones, Transform>(r.Bone, Skeleton.HumanBones[r.Bone]));

            reason = null;
            return true;
        }

        /// <summary>骨格の現在の姿勢からマッスル値と身体の位置・向きを読む。Build 前は false。</summary>
        public bool GetHumanPose(ref HumanPose pose)
        {
            if (_handler == null) return false;
            _handler.GetHumanPose(ref pose);
            return true;
        }

        /// <summary>マッスル値から骨格の姿勢を作る（Unity の逆変換）。Build 前は false。</summary>
        public bool SetHumanPose(ref HumanPose pose)
        {
            if (_handler == null) return false;
            _handler.SetHumanPose(ref pose);
            return true;
        }

        public void Dispose()
        {
            _handler?.Dispose();
            _handler = null;
            if (_avatar != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(_avatar);
                else                       UnityEngine.Object.DestroyImmediate(_avatar);
            }
            _avatar = null;
            Skeleton?.Dispose();
            Skeleton = null;
            Ordered.Clear();
        }

        private static void AddSkeleton(Transform t, List<SkeletonBone> list)
        {
            list.Add(new SkeletonBone
            {
                name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
            });
            for (int i = 0; i < t.childCount; i++) AddSkeleton(t.GetChild(i), list);
        }
    }
}
