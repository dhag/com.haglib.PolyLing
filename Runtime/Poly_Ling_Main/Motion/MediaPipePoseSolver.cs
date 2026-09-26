// MediaPipePoseSolver.cs
// MediaPipe の点（world 座標）から Humanoid の各関節のローカル回転を求め、アバター経由でマッスル値にする。
//
// ■ 構成
//   SolveBody        … ボディ（腰・背骨・首・頭・肩・腕・脚。下の「ボディ」参照）
//   SolveRightHand   … 右手の指 15 骨
//   SolveLeftHand    … 左手の指 15 骨
//   SolveJoint       … 任意の関節 1 つ（親のワールド回転・T ポーズでの骨の向き・観測した骨の向き → ローカル回転）
//   AdjustBody / AdjustRightHand / AdjustLeftHand … 部位ごとの総合調整（初期版では何もしない）
//   AdjustWholeBody  … 全身の総合調整（初期版では何もしない）
//   ToMuscles        … 格納したローカル回転を正準骨格に当て、Avatar（HumanPoseHandler）でマッスル値にする
//   結果は Locals（Humanoid 骨 → ローカル回転）に入る。解けなかった骨は入らない。
//
// ■ 座標
//   MediaPipe の world 座標は x 右・y 下・z 奥（人はカメラの方を向く）。
//   正準骨格（UnityClipApplier の正準テーブル）は Unity の左手系で、人は +Z を向き、左手は -X 側。
//   変換は p_u = (-x, -y, -z)：y を上向きにし、Y 軸まわりに 180° 回して人を +Z 向きにする（行列式 -1 で右手系→左手系）。
//
// ■ 正準骨格
//   T ポーズで全骨のレストのローカル回転が単位。骨の向きは UnityClipApplier.TryGetCanonDir（自骨→子骨）。
//   末端（Distal）は表に無いので Intermediate の向きを使う。
//   骨格・Avatar・HumanPoseHandler は CanonHumanPoseRig が組む（VmdMotionBake と共用）。
//
// ■ 手
//   手の向きは、手首(0)→中指の付け根(9) を前、小指の付け根(17)→人差し指の付け根(5) を横として、
//   T ポーズでの前（中指の Proximal の向き）・横（+Z。T ポーズで手のひらは下、人差し指側が前）に重ねる回転。
//   指の骨と点の対応（MediaPipe の手の 21 点）:
//     親指   Proximal 1→2、Intermediate 2→3、Distal 3→4
//     人差し Proximal 5→6、Intermediate 6→7、Distal 7→8（中指 9〜12、薬指 13〜16、小指 17〜20 も同じ）
//   初期版は向きを合わせるだけで、骨のひねりは扱わない（FromToRotation の最小回転）。
//
// ■ ボディ（姿勢の 33 点。番号は MediaPipe Pose）
//   骨ごとに「主の向き（そのまま合わせる）＋補助の向き（主に直交する成分だけ使う）」から
//   ワールド回転を作り（AlignTwo）、親のワールド回転の逆を掛けてローカル回転にする。
//   方式は keypoint から方向を取り出して骨の回転を作る先例（Dollars MoCap のブログ
//   "Animating 3D Characters with MediaPipe"、ThreeDPoseUnityBarracuda）に倣う。
//   正準骨格はレストの回転がすべて単位なので、モデルごとの初期回転の補正は要らない。
//     Hips       主：腰の中心(23,24)→肩の中心(11,12)、補助：左の腰(23)→右の腰(24)
//     Chest      主：同上、補助：左の肩(11)→右の肩(12)。Spine・UpperChest はローカル単位（初期版）
//     Head       顔の点があれば 主：顎(152)→額(10)、補助：左目尻(263)→右目尻(33)。Neck はローカル単位
//     Shoulder   ローカル単位（初期版）
//     UpperArm   肩→肘 へ SolveJoint（ひねりなし）
//     LowerArm   肘→手首 へ SolveJoint。手の点があれば、前腕の長軸まわりのひねりを手のひらの法線で決める
//                （MetU の方式：GanniPiece/MetU の LeftElbowCalculator ほか。肘→手首へ回したあと、
//                 前方向を手首→人差し指と手首→小指の外積に合わせる）。
//                ここでは T ポーズでの手のひらの法線（下向き）を手の点から求めた手の回転で運び、
//                長軸に直交する成分どうしを合わせる。
//     Hand       手の点があれば 手の回転（SolveHand と同じ求め方）、無ければ ローカル単位
//     UpperLeg   主：腰→膝、補助：左の腰→右の腰
//     LowerLeg   主：膝→足首、補助：同上
//     Foot       主：足首→つま先(31,32)、補助：同上
//   左右は MediaPipe の番号の左右（奇数が左）をそのまま Humanoid の左右にする（本人の左右かは未確認）。
//   RootT（腰の位置）は MediaPipe の world 座標の原点が腰の中心なので取れない。回転だけを求める。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.UnityClip;

namespace Poly_Ling.Motion
{
    public sealed class MediaPipePoseSolver : IDisposable
    {
        /// <summary>解いた骨のローカル回転（正準骨格＝T ポーズ基準）。</summary>
        public readonly Dictionary<HumanBodyBones, Quaternion> Locals = new Dictionary<HumanBodyBones, Quaternion>();

        private readonly CanonHumanPoseRig _rig = new CanonHumanPoseRig();
        private HumanPose                  _pose;

        // 指：Humanoid 名の接頭辞と、MediaPipe の手の点の番号（付け根から先へ 4 点）
        private static readonly (string finger, int[] idx)[] Fingers =
        {
            ("Thumb",  new[] { 1, 2, 3, 4 }),
            ("Index",  new[] { 5, 6, 7, 8 }),
            ("Middle", new[] { 9, 10, 11, 12 }),
            ("Ring",   new[] { 13, 14, 15, 16 }),
            ("Little", new[] { 17, 18, 19, 20 }),
        };
        private static readonly string[] Segments = { "Proximal", "Intermediate", "Distal" };

        /// <summary>MediaPipe の world 座標の点を正準骨格の座標へ直す。</summary>
        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(-x, -y, -z);

        // ================================================================
        // 1 フレーム
        // ================================================================

        /// <summary>前のフレームの結果を消す。</summary>
        public void Clear() => Locals.Clear();

        /// <summary>
        /// ボディ。pose は姿勢の 33 点、face は顔の点（468 点以上。無ければ null）、
        /// rightHand / leftHand は手の 21 点（無ければ null）。すべて正準骨格の座標。
        /// </summary>
        public void SolveBody(Vector3[] pose, Vector3[] face, Vector3[] rightHand, Vector3[] leftHand)
        {
            if (pose == null || pose.Length < 33) return;

            Vector3 hipL = pose[23], hipR = pose[24], shL = pose[11], shR = pose[12];
            Vector3 hipC = (hipL + hipR) * 0.5f, shC = (shL + shR) * 0.5f;
            Vector3 up = shC - hipC, hipLine = hipR - hipL, shLine = shR - shL;
            var restUp = Vector3.up; var restX = Vector3.right;

            // 腰（根。親は正準骨格の根＝単位）
            Quaternion hips = AlignTwo(restUp, restX, up, hipLine);
            Locals[HumanBodyBones.Hips] = hips;

            // 背骨：Spine は単位、Chest で肩の向きへ、UpperChest は単位
            Quaternion spine = hips;
            Locals[HumanBodyBones.Spine] = Quaternion.identity;
            Quaternion chest = AlignTwo(restUp, restX, up, shLine);
            Locals[HumanBodyBones.Chest] = Quaternion.Inverse(spine) * chest;
            Quaternion upperChest = chest;
            Locals[HumanBodyBones.UpperChest] = Quaternion.identity;

            // 首・頭
            Quaternion neck = upperChest;
            Locals[HumanBodyBones.Neck] = Quaternion.identity;
            if (face != null && face.Length > 263)
            {
                Quaternion head = AlignTwo(restUp, restX, face[10] - face[152], face[33] - face[263]);
                Locals[HumanBodyBones.Head] = Quaternion.Inverse(neck) * head;
            }

            // 腕
            SolveArm("Left",  upperChest, pose[11], pose[13], pose[15], leftHand);
            SolveArm("Right", upperChest, pose[12], pose[14], pose[16], rightHand);

            // 脚
            SolveLeg("Left",  hips, hipLine, pose[23], pose[25], pose[27], pose[31]);
            SolveLeg("Right", hips, hipLine, pose[24], pose[26], pose[28], pose[32]);
        }

        private void SolveArm(string side, Quaternion chestWorld, Vector3 shoulder, Vector3 elbow, Vector3 wrist, Vector3[] hand)
        {
            if (!Enum.TryParse<HumanBodyBones>(side + "Shoulder",  out var bShoulder)) return;
            Enum.TryParse<HumanBodyBones>(side + "UpperArm", out var bUpper);
            Enum.TryParse<HumanBodyBones>(side + "LowerArm", out var bLower);
            Enum.TryParse<HumanBodyBones>(side + "Hand",     out var bHand);

            Quaternion shoulderWorld = chestWorld;
            Locals[bShoulder] = Quaternion.identity;

            if (!UnityClipApplier.TryGetCanonDir(side + "UpperArm", out var restUpper)) return;
            Locals[bUpper] = SolveJoint(shoulderWorld, restUpper, elbow - shoulder, out var upperWorld);

            if (!UnityClipApplier.TryGetCanonDir(side + "LowerArm", out var restLower)) return;
            Locals[bLower] = SolveJoint(upperWorld, restLower, wrist - elbow, out var lowerWorld);

            // 手の点があれば、前腕のひねりを手のひらの法線で決め（MetU の方式）、手の回転も当てる
            if (TryHandWorld(side, hand, out var handWorld))
            {
                Vector3 axis = (wrist - elbow).normalized;
                Vector3 palmRest = Vector3.down;                       // T ポーズで手のひらは下向き
                lowerWorld = TwistToward(lowerWorld, axis, palmRest, handWorld * palmRest);
                Locals[bLower] = Quaternion.Inverse(upperWorld) * lowerWorld;
                Locals[bHand]  = Quaternion.Inverse(lowerWorld) * handWorld;
            }
            else Locals[bHand] = Quaternion.identity;
        }

        private void SolveLeg(string side, Quaternion hipsWorld, Vector3 hipLine,
                              Vector3 hip, Vector3 knee, Vector3 ankle, Vector3 toe)
        {
            if (!Enum.TryParse<HumanBodyBones>(side + "UpperLeg", out var bUpper)) return;
            Enum.TryParse<HumanBodyBones>(side + "LowerLeg", out var bLower);
            Enum.TryParse<HumanBodyBones>(side + "Foot",     out var bFoot);
            var restX = Vector3.right;

            if (!UnityClipApplier.TryGetCanonDir(side + "UpperLeg", out var restUpper)) return;
            Quaternion upper = AlignTwo(restUpper, restX, knee - hip, hipLine);
            Locals[bUpper] = Quaternion.Inverse(hipsWorld) * upper;

            if (!UnityClipApplier.TryGetCanonDir(side + "LowerLeg", out var restLower)) return;
            Quaternion lower = AlignTwo(restLower, restX, ankle - knee, hipLine);
            Locals[bLower] = Quaternion.Inverse(upper) * lower;

            var restFoot = new Vector3(0f, 0f, 1f);                  // T ポーズでつま先は前（UnityClipCanonVrmAnimation と同じ）
            Quaternion foot = AlignTwo(restFoot, restX, toe - ankle, hipLine);
            Locals[bFoot] = Quaternion.Inverse(lower) * foot;
        }

        /// <summary>
        /// レストの主の向き restP・補助の向き restS を、観測の obsP・obsS へ移すワールド回転。
        /// 主の向きはそのまま合い、補助は主に直交する成分だけが合う（Quaternion.LookRotation の性質）。
        /// どちらかが潰れていれば主の向きだけ合わせる。
        /// </summary>
        public static Quaternion AlignTwo(Vector3 restP, Vector3 restS, Vector3 obsP, Vector3 obsS)
        {
            if (obsP.sqrMagnitude < 1e-12f) return Quaternion.identity;
            if (obsS.sqrMagnitude < 1e-12f || Vector3.Cross(obsP.normalized, obsS.normalized).sqrMagnitude < 1e-6f
                || Vector3.Cross(restP.normalized, restS.normalized).sqrMagnitude < 1e-6f)
                return Quaternion.FromToRotation(restP, obsP);
            return Quaternion.LookRotation(obsP, obsS) * Quaternion.Inverse(Quaternion.LookRotation(restP, restS));
        }

        /// <summary>
        /// world を長軸 axis まわりに回し、world で運んだ refRest の向きを target に（axis に直交する成分どうしで）合わせる。
        /// </summary>
        public static Quaternion TwistToward(Quaternion world, Vector3 axis, Vector3 refRest, Vector3 target)
        {
            Vector3 cur = Vector3.ProjectOnPlane(world * refRest, axis);
            Vector3 tgt = Vector3.ProjectOnPlane(target, axis);
            if (cur.sqrMagnitude < 1e-10f || tgt.sqrMagnitude < 1e-10f) return world;
            float angle = Vector3.SignedAngle(cur, tgt, axis);
            return Quaternion.AngleAxis(angle, axis) * world;
        }

        /// <summary>右手の指。points は手の 21 点（正準骨格の座標）。</summary>
        public void SolveRightHand(Vector3[] points) => SolveHand("Right", points);

        /// <summary>左手の指。points は手の 21 点（正準骨格の座標）。</summary>
        public void SolveLeftHand(Vector3[] points) => SolveHand("Left", points);

        /// <summary>ボディの総合調整（初期版では何もしない）。</summary>
        public void AdjustBody() { }
        /// <summary>右手の総合調整（初期版では何もしない）。</summary>
        public void AdjustRightHand() { }
        /// <summary>左手の総合調整（初期版では何もしない）。</summary>
        public void AdjustLeftHand() { }
        /// <summary>全身の総合調整（初期版では何もしない）。</summary>
        public void AdjustWholeBody() { }

        /// <summary>
        /// 任意の関節 1 つ。親のワールド回転 parentWorld のもとで、T ポーズでの骨の向き restDir（ワールド。
        /// 正準骨格はレストが単位なので親の枠でも同じ）を、観測した骨の向き observedDir へ最小の回転で向ける。
        /// 返り値はローカル回転。world にその骨のワールド回転を返す（子の親として使う）。
        /// </summary>
        public static Quaternion SolveJoint(Quaternion parentWorld, Vector3 restDir, Vector3 observedDir, out Quaternion world)
        {
            if (observedDir.sqrMagnitude < 1e-12f || restDir.sqrMagnitude < 1e-12f)
            {
                world = parentWorld;
                return Quaternion.identity;
            }
            Vector3 carried = parentWorld * restDir;        // 親に付いて動いたレストの向き
            world = Quaternion.FromToRotation(carried, observedDir) * parentWorld;
            return Quaternion.Inverse(parentWorld) * world;
        }

        private void SolveHand(string side, Vector3[] p)
        {
            if (p == null || p.Length < 21) return;

            if (!TryHandWorld(side, p, out var handWorld)) return;

            foreach (var (finger, idx) in Fingers)
            {
                Quaternion parent = handWorld;
                Vector3 prevRest = Vector3.zero;
                for (int s = 0; s < 3; s++)
                {
                    string name = side + finger + Segments[s];
                    if (!Enum.TryParse<HumanBodyBones>(name, out var hbb)) break;
                    if (!UnityClipApplier.TryGetCanonDir(name, out var rest)) rest = prevRest;   // Distal は表に無い
                    if (rest.sqrMagnitude < 1e-12f) break;
                    prevRest = rest;

                    Vector3 obs = p[idx[s + 1]] - p[idx[s]];
                    Locals[hbb] = SolveJoint(parent, rest, obs, out parent);
                }
            }
        }

        /// <summary>
        /// 手のワールド回転：T ポーズの（前＝中指の Proximal の向き、横＝+Z。手のひらは下で人差し指側が前）を、
        /// 観測の（前＝手首(0)→中指の付け根(9)、横＝小指の付け根(17)→人差し指の付け根(5)）へ。
        /// </summary>
        private static bool TryHandWorld(string side, Vector3[] p, out Quaternion handWorld)
        {
            handWorld = Quaternion.identity;
            if (p == null || p.Length < 21) return false;
            if (!UnityClipApplier.TryGetCanonDir(side + "MiddleProximal", out var fwd0)) return false;
            var side0 = new Vector3(0f, 0f, 1f);
            Vector3 fwd = p[9] - p[0];
            Vector3 lat = p[5] - p[17];
            if (fwd.sqrMagnitude < 1e-12f || lat.sqrMagnitude < 1e-12f) return false;
            handWorld = AlignTwo(fwd0, side0, fwd, lat);
            return true;
        }

        // ================================================================
        // マッスル
        // ================================================================

        /// <summary>正準骨格と Avatar を組む。ToMuscles の前に 1 回呼ぶ。</summary>
        public bool Build(out string reason)
        {
            if (!_rig.Build(0.1f, out reason)) return false;
            _pose = new HumanPose();
            return true;
        }

        /// <summary>
        /// Locals を正準骨格に当て、マッスル値を into に入れる。有効マスクは解いた骨のマッスルだけ。
        /// Build していなければ false。
        /// </summary>
        public bool ToMuscles(MotionLiveFrame into)
        {
            if (_rig.Skeleton == null || into == null) return false;

            foreach (var kv in _rig.Skeleton.HumanBones)
                kv.Value.localRotation = Locals.TryGetValue(kv.Key, out var q) ? q : Quaternion.identity;
            if (!_rig.GetHumanPose(ref _pose)) return false;

            int n = HumanTrait.MuscleCount;
            into.EnsureCount(n);
            Array.Clear(into.Muscles, 0, n);
            Array.Clear(into.Valid, 0, n);
            foreach (var hbb in Locals.Keys)
            {
                int bi = (int)hbb;
                for (int dof = 0; dof < 3; dof++)
                {
                    int mi = HumanTrait.MuscleFromBone(bi, dof);
                    if (mi < 0 || mi >= n || _pose.muscles == null || mi >= _pose.muscles.Length) continue;
                    into.Muscles[mi] = _pose.muscles[mi];
                    into.Valid[mi]   = true;
                }
            }
            // 腰を解いたときだけ身体の向き（と正準骨格での位置）を載せる
            into.HasRoot = Locals.ContainsKey(HumanBodyBones.Hips);
            if (into.HasRoot)
            {
                into.RootT = _pose.bodyPosition;
                into.RootQ = _pose.bodyRotation;
            }
            return true;
        }

        public void Dispose() => _rig.Dispose();
    }
}
