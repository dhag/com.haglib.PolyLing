// CanonSkeleton.cs
// ============================================================
// 正準骨格（VRM T ポーズ）。PolyLing のモーションの基準骨格の唯一の置き場。
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/Motion/ に配置。
//
// ■ 何に使うか
//   ・Unity の Humanoid（CanonHumanPoseRig）の骨格。マッスル ⇄ 回転を Unity に解かせる土台。
//   ・.plmotion の bakedBones の基準（レストの全ローカル回転が単位）。
//   ・.vrma のレスト骨格（VrmaCodec）。目は .vrma には載せない（仕様で humanBones に置けない）。
//   JS（モーションスタジオ）も同じ値を使う。値は PolyLing_VRMA整備計画.md「5. 正準 VRMA 骨格」に写してある。
//   ここを変えたら計画書の表も同時に直すこと。
//
// ■ 骨格の決まり
//   VRM T ポーズ、+Z 正面、Unity 左手系（キャラクターの左は -X）、単位はメートル。
//   Hips はレストで根から (0, 1, 0)（腰の高さ 1.0）。拡大縮小なし、レストのローカル回転はすべて単位。
//   各ボーンの「自分 → 子」の向きは UnityClipApplier の CanonDirTable と同じにしてある
//   （Unity が関節の軸を骨の向きから決めるため。中指の付け根は手の向きの真上に置く）。
//   目は Humanoid のマッスル（視線）を解くためだけに持つ。あごは持たない。
//   指は VRM 1.0 の T ポーズ（tpose.md 1.7・1.8）に合わせる（2026-10-14）：4 本の指の Intermediate・Distal は
//   X 軸に平行、親指の Intermediate・Distal は水平で X と Z から 45°。親指 Proximal の付け根の位置は仕様に無いので旧値のまま。
//   この骨格では、レスト（全ローカル回転が単位）を GetHumanPose→SetHumanPose で往復すると親指 Proximal だけ 10.10° ずれる。
//   Unity の親指 Proximal のマッスル 0 の向き（Zero、骨の向きに依らない）が骨の軸まわりのねじりを含み、
//   親指 Proximal にねじりの自由度が無いため（ねじり成分＝2·atan(|v·d|/w)、d は骨の向き）。旧骨格では 4.01°。
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Poly_Ling.Motion
{
    public sealed class CanonSkeleton : IDisposable
    {
        /// <summary>
        /// 1 行＝「Humanoid 名|親の Humanoid 名|親から見た位置 x y z」。親から子の順。
        /// Hips の親は空（根）。
        /// </summary>
        public static readonly string[] Table =
        {
            "Hips||0 1 0",
            "Spine|Hips|0 0.1 0",
            "Chest|Spine|0 0.12 0",
            "UpperChest|Chest|0 0.12 0",
            "Neck|UpperChest|0 0.16 0",
            "Head|Neck|0 0.1 0",
            "LeftEye|Head|-0.03 0.07 0.08",
            "RightEye|Head|0.03 0.07 0.08",
            "LeftUpperLeg|Hips|-0.09 -0.05 0",
            "LeftLowerLeg|LeftUpperLeg|-0.0105 -0.419869 0",
            "LeftFoot|LeftLowerLeg|0 -0.42 0",
            "LeftToes|LeftFoot|0 0 0.12",
            "RightUpperLeg|Hips|0.09 -0.05 0",
            "RightLowerLeg|RightUpperLeg|0.0105 -0.419869 0",
            "RightFoot|RightLowerLeg|0 -0.42 0",
            "RightToes|RightFoot|0 0 0.12",
            "LeftShoulder|UpperChest|-0.02 0.12 0",
            "LeftUpperArm|LeftShoulder|-0.09993 0.00373 0",
            "LeftLowerArm|LeftUpperArm|-0.26 0 0",
            "LeftHand|LeftLowerArm|-0.24 0 0",
            "LeftThumbProximal|LeftHand|-0.02 -0.01 0.025",
            "LeftThumbIntermediate|LeftThumbProximal|-0.028284 0 0.028284",
            "LeftThumbDistal|LeftThumbIntermediate|-0.021213 0 0.021213",
            "LeftIndexProximal|LeftHand|-0.08 0 0.02",
            "LeftIndexIntermediate|LeftIndexProximal|-0.04 0 0",
            "LeftIndexDistal|LeftIndexIntermediate|-0.025 0 0",
            "LeftMiddleProximal|LeftHand|-0.08 0 0",
            "LeftMiddleIntermediate|LeftMiddleProximal|-0.045 0 0",
            "LeftMiddleDistal|LeftMiddleIntermediate|-0.03 0 0",
            "LeftRingProximal|LeftHand|-0.075 0 -0.02",
            "LeftRingIntermediate|LeftRingProximal|-0.04 0 0",
            "LeftRingDistal|LeftRingIntermediate|-0.025 0 0",
            "LeftLittleProximal|LeftHand|-0.07 0 -0.04",
            "LeftLittleIntermediate|LeftLittleProximal|-0.03 0 0",
            "LeftLittleDistal|LeftLittleIntermediate|-0.02 0 0",
            "RightShoulder|UpperChest|0.02 0.12 0",
            "RightUpperArm|RightShoulder|0.09993 0.00373 0",
            "RightLowerArm|RightUpperArm|0.26 0 0",
            "RightHand|RightLowerArm|0.24 0 0",
            "RightThumbProximal|RightHand|0.02 -0.01 0.025",
            "RightThumbIntermediate|RightThumbProximal|0.028284 0 0.028284",
            "RightThumbDistal|RightThumbIntermediate|0.021213 0 0.021213",
            "RightIndexProximal|RightHand|0.08 0 0.02",
            "RightIndexIntermediate|RightIndexProximal|0.04 0 0",
            "RightIndexDistal|RightIndexIntermediate|0.025 0 0",
            "RightMiddleProximal|RightHand|0.08 0 0",
            "RightMiddleIntermediate|RightMiddleProximal|0.045 0 0",
            "RightMiddleDistal|RightMiddleIntermediate|0.03 0 0",
            "RightRingProximal|RightHand|0.075 0 -0.02",
            "RightRingIntermediate|RightRingProximal|0.04 0 0",
            "RightRingDistal|RightRingIntermediate|0.025 0 0",
            "RightLittleProximal|RightHand|0.07 0 -0.04",
            "RightLittleIntermediate|RightLittleProximal|0.03 0 0",
            "RightLittleDistal|RightLittleIntermediate|0.02 0 0",
        };

        /// <summary>表の 1 行を解いたもの。</summary>
        public struct Row
        {
            public HumanBodyBones Bone;
            public HumanBodyBones Parent;   // Hips は LastBone
            public bool HasParent;
            public Vector3 LocalPosition;
        }

        private static List<Row> _rows;

        /// <summary>表を解いた行（親から子の順）。</summary>
        public static IReadOnlyList<Row> Rows
        {
            get
            {
                if (_rows != null) return _rows;
                var list = new List<Row>(Table.Length);
                foreach (var line in Table)
                {
                    var col = line.Split('|');
                    var p = col[2].Split(' ');
                    var row = new Row
                    {
                        Bone = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), col[0]),
                        HasParent = col[1].Length > 0,
                        LocalPosition = new Vector3(F(p[0]), F(p[1]), F(p[2])),
                    };
                    row.Parent = row.HasParent
                        ? (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), col[1])
                        : HumanBodyBones.LastBone;
                    list.Add(row);
                }
                _rows = list;
                return _rows;
            }
        }

        /// <summary>.vrma に載せるボーンか（目・あごは載せない）。</summary>
        public static bool IsVrmaBone(HumanBodyBones b)
            => b != HumanBodyBones.LeftEye && b != HumanBodyBones.RightEye && b != HumanBodyBones.Jaw;

        /// <summary>Hips のレスト位置（根から見たローカル）。</summary>
        public static Vector3 HipsRest => Rows[0].LocalPosition;

        /// <summary>レストの腰の高さ。Hips の位置の正規化に使う。</summary>
        public static float HipsHeight => HipsRest.y;

        private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        // ================================================================
        // GameObject としての骨格
        // ================================================================

        /// <summary>骨格の根（原点・単位回転）。</summary>
        public GameObject Root { get; private set; }

        /// <summary>Humanoid ボーン → Transform。</summary>
        public readonly Dictionary<HumanBodyBones, Transform> HumanBones
            = new Dictionary<HumanBodyBones, Transform>();

        /// <summary>Hips のレスト位置（根から見たローカル）。</summary>
        public Vector3 HipsRestLocalPosition => HipsRest;

        private CanonSkeleton() { }

        /// <summary>レスト姿勢の骨格を組む。使い終わったら Dispose。</summary>
        public static CanonSkeleton Build()
        {
            var s = new CanonSkeleton();
            s.Root = new GameObject("PolyLingCanonSkeleton");
            s.Root.hideFlags = HideFlags.HideAndDontSave;
            foreach (var r in Rows)
            {
                var t = new GameObject(r.Bone.ToString()).transform;
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
                t.SetParent(r.HasParent ? s.HumanBones[r.Parent] : s.Root.transform, false);
                t.localPosition = r.LocalPosition;
                t.localRotation = Quaternion.identity;
                t.localScale    = Vector3.one;
                s.HumanBones[r.Bone] = t;
            }
            return s;
        }

        /// <summary>全ボーンをレストへ戻す。</summary>
        public void ResetToRest()
        {
            foreach (var r in Rows)
            {
                if (!HumanBones.TryGetValue(r.Bone, out var t) || t == null) continue;
                t.localPosition = r.LocalPosition;
                t.localRotation = Quaternion.identity;
            }
        }

        public void Dispose()
        {
            if (Root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(Root);
                else                       UnityEngine.Object.DestroyImmediate(Root);
            }
            Root = null;
            HumanBones.Clear();
        }
    }
}
