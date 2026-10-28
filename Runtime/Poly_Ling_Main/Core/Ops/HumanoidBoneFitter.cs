// HumanoidBoneFitter.cs
// ボーンの無いモデルを描いた画像の MediaPipe 姿勢（33 点）から、Humanoid ボーンのワールド位置を決める。
// T ポーズのモデルを正面から撮った画像が前提。指は扱わない。
//
// ■ 画像上の位置（MediaPipe 姿勢の番号。画素、x 右・y 下）
//   胴体の四角：上辺＝肩 11・12、下辺＝腰 23・24。中心線＝下辺の中点 → 上辺の中点。
//     Hips       下辺の中点
//     Spine      中心線の 1/4
//     Chest      中心線の 2/4
//     UpperChest 中心線の 3/4
//     Neck       上辺の中点
//   上辺の上で Neck → Shoulder → UpperArm を均等に置く：
//     Shoulder   Neck と肩の中点
//     UpperArm   肩 11 / 12
//   Head        中心線の延長上で、耳 7・8 の中点の高さ（中心線へ射影した点）
//   LowerArm 肘 13/14、Hand 手首 15/16、UpperLeg 腰 23/24、LowerLeg 膝 25/26、Foot 足首 27/28、Toes つま先 31/32
//
// ■ 左右
//   正面から撮った前提で、本人の左は画像の右（x が大きい側）。MediaPipe の番号の左右は使わない
//   （描いたモデルで本人の左右か画像の左右かは未確認のため）。腕は肩 11・12、脚は腰 23・24 の x で決める。
//
// ■ 奥行き
//   対象メッシュの頂点（ワールド。呼び出し側が GPU の表示位置を渡す）を撮影カメラで画像へ投影し、
//   関節の画素から半径内に入った頂点のカメラ奥行き（視線方向の距離）の最小・最大の中点を関節の奥行きにする。
//   半径は肩幅（画素）× radiusRatio。半径内に頂点が無い関節は失敗にする。
//   画素と奥行きからワールドへの戻しは、近・遠クリップ面への逆投影の 2 点を結ぶ線上で奥行きを合わせる
//   （透視・平行投影のどちらでも同じ式）。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Tools.MediaPipe
{
    /// <summary>当てはめた関節 1 つ。</summary>
    public sealed class HumanoidFitJoint
    {
        /// <summary>Humanoid ボーン名（HumanBodyBones の名前）。</summary>
        public string  Bone;
        /// <summary>親の Humanoid ボーン名（Hips は空）。</summary>
        public string  Parent;
        public Vector2 Pixel;
        public Vector3 World;
        /// <summary>奥行きを決めた頂点の数。</summary>
        public int     Hits;
    }

    public sealed class HumanoidFitResult
    {
        /// <summary>親が先の並び。</summary>
        public readonly List<HumanoidFitJoint> Joints = new List<HumanoidFitJoint>();
        /// <summary>腕で MediaPipe の左（奇数）を本人の右にしたか。</summary>
        public bool  ArmsSwapped;
        /// <summary>脚で MediaPipe の左（奇数）を本人の右にしたか。</summary>
        public bool  LegsSwapped;
        /// <summary>使った半径（画素）。</summary>
        public float RadiusPixels;
    }

    public static class HumanoidBoneFitter
    {
        /// <summary>
        /// 当てはめる。posePixels は姿勢 33 点の画素位置（撮影画像と同じ大きさ）、
        /// meshWorld は対象メッシュごとの頂点のワールド位置。失敗すれば null（error に理由）。
        /// </summary>
        public static HumanoidFitResult Fit(
            FaceCaptureCamera camera, Vector2[] posePixels, IReadOnlyList<Vector3[]> meshWorld,
            float radiusRatio, out string error)
        {
            error = "";
            if (camera == null) { error = "撮影時のカメラがありません"; return null; }
            if (posePixels == null || posePixels.Length < MediaPipeFrame.PosePoints) { error = "姿勢の点が 33 点ありません"; return null; }
            if (meshWorld == null || meshWorld.Count == 0) { error = "対象のメッシュがありません"; return null; }
            if (!(radiusRatio > 0f)) { error = "radiusRatio は正の数にしてください"; return null; }

            Vector2 P(int i) => posePixels[i];

            // ---- 左右（本人の左＝画像の右）
            bool armsSwapped = P(11).x < P(12).x;
            bool legsSwapped = P(23).x < P(24).x;
            int LA(int mpLeft, int mpRight) => armsSwapped ? mpRight : mpLeft;   // 本人の左の腕の点
            int RA(int mpLeft, int mpRight) => armsSwapped ? mpLeft : mpRight;
            int LL(int mpLeft, int mpRight) => legsSwapped ? mpRight : mpLeft;
            int RL(int mpLeft, int mpRight) => legsSwapped ? mpLeft : mpRight;

            // ---- 画像上の位置
            Vector2 hipC = (P(23) + P(24)) * 0.5f;
            Vector2 shC  = (P(11) + P(12)) * 0.5f;
            Vector2 axis = shC - hipC;
            if (axis.sqrMagnitude < 1e-6f) { error = "肩と腰の中点が重なっています"; return null; }
            Vector2 axisDir = axis.normalized;
            Vector2 earC = (P(7) + P(8)) * 0.5f;
            Vector2 head = shC + axisDir * Vector2.Dot(earC - shC, axisDir);

            float shoulderWidth = (P(11) - P(12)).magnitude;
            if (shoulderWidth < 1f) { error = "肩幅が 1 画素未満です"; return null; }
            float radius = shoulderWidth * radiusRatio;

            var plan = new List<(string bone, string parent, Vector2 px)>
            {
                ("Hips",       "",           hipC),
                ("Spine",      "Hips",       Vector2.Lerp(hipC, shC, 0.25f)),
                ("Chest",      "Spine",      Vector2.Lerp(hipC, shC, 0.50f)),
                ("UpperChest", "Chest",      Vector2.Lerp(hipC, shC, 0.75f)),
                ("Neck",       "UpperChest", shC),
                ("Head",       "Neck",       head),
            };
            void Arm(string side, int sh, int el, int wr)
            {
                plan.Add((side + "Shoulder", "UpperChest",        (shC + P(sh)) * 0.5f));
                plan.Add((side + "UpperArm", side + "Shoulder",   P(sh)));
                plan.Add((side + "LowerArm", side + "UpperArm",   P(el)));
                plan.Add((side + "Hand",     side + "LowerArm",   P(wr)));
            }
            void Leg(string side, int hip, int knee, int ankle, int toe)
            {
                plan.Add((side + "UpperLeg", "Hips",              P(hip)));
                plan.Add((side + "LowerLeg", side + "UpperLeg",   P(knee)));
                plan.Add((side + "Foot",     side + "LowerLeg",   P(ankle)));
                plan.Add((side + "Toes",     side + "Foot",       P(toe)));
            }
            Arm("Left",  LA(11, 12), LA(13, 14), LA(15, 16));
            Arm("Right", RA(11, 12), RA(13, 14), RA(15, 16));
            Leg("Left",  LL(23, 24), LL(25, 26), LL(27, 28), LL(31, 32));
            Leg("Right", RL(23, 24), RL(25, 26), RL(27, 28), RL(31, 32));

            // ---- 頂点を画像へ投影（画素とカメラ奥行き）
            var px    = new List<Vector2>();
            var depth = new List<float>();
            foreach (var w in meshWorld)
            {
                if (w == null) continue;
                for (int i = 0; i < w.Length; i++)
                {
                    if (!camera.ToPixel(w[i], out var p, out _)) continue;
                    px.Add(p);
                    depth.Add(-(camera.View.MultiplyPoint3x4(w[i]).z));
                }
            }
            if (px.Count == 0) { error = "対象のメッシュの頂点がカメラに写っていません"; return null; }

            // ---- 関節ごとに奥行きを決めてワールドへ
            var result = new HumanoidFitResult { ArmsSwapped = armsSwapped, LegsSwapped = legsSwapped, RadiusPixels = radius };
            var missing = new List<string>();
            float r2 = radius * radius;
            foreach (var (bone, parent, jp) in plan)
            {
                float dMin = float.MaxValue, dMax = float.MinValue;
                int hits = 0;
                for (int i = 0; i < px.Count; i++)
                {
                    if ((px[i] - jp).sqrMagnitude > r2) continue;
                    float d = depth[i];
                    if (d < dMin) dMin = d;
                    if (d > dMax) dMax = d;
                    hits++;
                }
                if (hits == 0) { missing.Add(bone); continue; }
                result.Joints.Add(new HumanoidFitJoint
                {
                    Bone = bone, Parent = parent, Pixel = jp, Hits = hits,
                    World = Unproject(camera, jp, (dMin + dMax) * 0.5f),
                });
            }
            if (missing.Count > 0)
            {
                error = $"半径 {radius:F1} 画素の内に頂点が無い関節があります: {string.Join(", ", missing)}";
                return null;
            }
            return result;
        }

        /// <summary>画素とカメラ奥行き（視線方向の距離）からワールド位置を求める。</summary>
        public static Vector3 Unproject(FaceCaptureCamera camera, Vector2 pixel, float viewDepth)
        {
            float nx = pixel.x / camera.Width * 2f - 1f;
            float ny = (1f - pixel.y / camera.Height) * 2f - 1f;
            var inv = (camera.Projection * camera.View).inverse;
            Vector3 a = ToPoint(inv * new Vector4(nx, ny, -1f, 1f));
            Vector3 b = ToPoint(inv * new Vector4(nx, ny,  1f, 1f));
            float za = -camera.View.MultiplyPoint3x4(a).z;
            float zb = -camera.View.MultiplyPoint3x4(b).z;
            float t = Mathf.Abs(zb - za) < 1e-12f ? 0f : (viewDepth - za) / (zb - za);
            return a + (b - a) * t;
        }

        private static Vector3 ToPoint(Vector4 h) => new Vector3(h.x / h.w, h.y / h.w, h.z / h.w);
    }
}
