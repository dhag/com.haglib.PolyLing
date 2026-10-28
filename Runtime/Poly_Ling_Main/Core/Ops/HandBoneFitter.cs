// HandBoneFitter.cs
// 手を描いた画像の MediaPipe Hand（21 点）から、Humanoid の指ボーンのワールド位置を決める。
// 体の当てはめ（HumanoidBoneFitter）と同じく、奥行きは対象メッシュの頂点から決める。
//
// ■ 割当（MediaPipe Hand の番号）
//   0 手首 … <Side>Hand（体が無いときだけ作る。あれば既存の Hand ボーンにつなぐ）
//   親指   1 CMC → ThumbProximal、2 MCP → ThumbIntermediate、3 IP → ThumbDistal、4 先端 → ThumbTip
//   他の指 MCP → Proximal、PIP → Intermediate、DIP → Distal、先端 → Tip
//     人差し指 5〜8 Index、中指 9〜12 Middle、薬指 13〜16 Ring、小指 17〜20 Little
//   先端（<Side><Finger>Tip）は Humanoid の割当を持たない末端ボーン。includeTips が偽なら作らない。
//
// ■ 左右（side が Auto のとき）
//   T ポーズで手のひらが下向きの前提。指の向き f（手首→中指の付け根）と親指側 s（小指の付け根→人差し指の付け根）の
//   外積 f×s の Y が正なら左手、負なら右手。体の向き（+Z / −Z）によらない
//   （左手：f は本人の左、s は本人の前。−Z 向きなら f=+X, s=−Z で f×s=+Y、+Z 向きなら f=−X, s=+Z で f×s=+Y）。
//
// ■ 奥行き
//   関節の画素から半径内に写る頂点のカメラ奥行きの最小・最大の中点。半径は 手首→中指の付け根の画素長 × radiusRatio。
//   半径内に頂点が無い関節は失敗にする。曲げた指が視線方向に重なると、重なった部分の奥行きは混ざる。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Tools.MediaPipe
{
    public sealed class HandFitResult
    {
        /// <summary>親が先の並び。手首（Hand）は先頭。</summary>
        public readonly List<HumanoidFitJoint> Joints = new List<HumanoidFitJoint>();
        /// <summary>Left / Right。</summary>
        public string Side;
        /// <summary>Auto で左右を決めたとき、外積の Y（決めなかったときは 0）。</summary>
        public float  ChiralityY;
        public float  RadiusPixels;
    }

    public static class HandBoneFitter
    {
        public const int Points = 21;

        private static readonly string[] Fingers = { "Thumb", "Index", "Middle", "Ring", "Little" };
        private static readonly string[] Segs    = { "Proximal", "Intermediate", "Distal", "Tip" };

        /// <summary>Humanoid の割当を持たない末端ボーンの名前か。</summary>
        public static bool IsTip(string bone) => bone != null && bone.EndsWith("Tip", StringComparison.Ordinal);

        /// <summary>
        /// side は "Left" / "Right" / "Auto"。handPixels は 21 点の画素位置（撮影画像と同じ大きさ。x 右・y 下）。
        /// 失敗すれば null（error に理由）。
        /// </summary>
        public static HandFitResult Fit(
            FaceCaptureCamera camera, Vector2[] handPixels, IReadOnlyList<Vector3[]> meshWorld,
            float radiusRatio, string side, bool includeTips, out string error)
        {
            error = "";
            if (camera == null) { error = "撮影時のカメラがありません"; return null; }
            if (handPixels == null || handPixels.Length < Points) { error = "手の点が 21 点ありません"; return null; }
            if (meshWorld == null || meshWorld.Count == 0) { error = "対象のメッシュがありません"; return null; }
            if (!(radiusRatio > 0f)) { error = "radiusRatio は正の数にしてください"; return null; }
            if (side != "Left" && side != "Right" && side != "Auto") { error = $"side は Left / Right / Auto にしてください（{side}）"; return null; }

            float palm = (handPixels[0] - handPixels[9]).magnitude;
            if (palm < 1f) { error = "手首と中指の付け根が 1 画素未満です"; return null; }
            float radius = palm * radiusRatio;

            // ---- 頂点を画像へ投影
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

            // ---- 21 点すべての奥行きとワールド位置
            var world = new Vector3[Points];
            var hits  = new int[Points];
            var missing = new List<int>();
            float r2 = radius * radius;
            for (int k = 0; k < Points; k++)
            {
                Vector2 jp = handPixels[k];
                float dMin = float.MaxValue, dMax = float.MinValue;
                int h = 0;
                for (int i = 0; i < px.Count; i++)
                {
                    if ((px[i] - jp).sqrMagnitude > r2) continue;
                    float d = depth[i];
                    if (d < dMin) dMin = d;
                    if (d > dMax) dMax = d;
                    h++;
                }
                hits[k] = h;
                if (h == 0)
                {
                    if (includeTips || !IsTipIndex(k)) missing.Add(k);
                    continue;
                }
                world[k] = HumanoidBoneFitter.Unproject(camera, jp, (dMin + dMax) * 0.5f);
            }
            if (missing.Count > 0)
            {
                error = $"半径 {radius:F1} 画素の内に頂点が無い点があります（MediaPipe Hand の番号）: {string.Join(", ", missing)}";
                return null;
            }

            // ---- 左右
            var result = new HandFitResult { RadiusPixels = radius };
            if (side == "Auto")
            {
                Vector3 f = world[9] - world[0];
                Vector3 s = world[5] - world[17];
                float y = Vector3.Cross(f, s).y;
                if (Mathf.Abs(y) < 1e-12f) { error = "手の向きから左右を決められません（指の向きと親指側が平行です）"; return null; }
                side = y > 0f ? "Left" : "Right";
                result.ChiralityY = y;
            }
            result.Side = side;

            // ---- 並べる（親が先）
            void Add(string bone, string parent, int k)
                => result.Joints.Add(new HumanoidFitJoint { Bone = bone, Parent = parent, Pixel = handPixels[k], World = world[k], Hits = hits[k] });

            string hand = side + "Hand";
            Add(hand, "", 0);
            for (int fi = 0; fi < Fingers.Length; fi++)
            {
                int baseK = 1 + fi * 4;
                string parent = hand;
                for (int si = 0; si < Segs.Length; si++)
                {
                    if (si == 3 && !includeTips) break;
                    string bone = side + Fingers[fi] + Segs[si];
                    Add(bone, parent, baseK + si);
                    parent = bone;
                }
            }
            return result;
        }

        private static bool IsTipIndex(int k) => k > 0 && k % 4 == 0;
    }
}
