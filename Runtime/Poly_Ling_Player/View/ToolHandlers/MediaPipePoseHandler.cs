// MediaPipePoseHandler.cs
// メディアパイプ（姿勢）のツールの窓口 "mediaPipePose"。
// 開始中は、motionLive が保持する MediaPipe の最新データが新しくなるたびに（OnMediaPipe）
// MediaPipePoseSolver でローカル回転を求めてマッスル値にし、つながっている接続があれば {type:'muscles'} で送る。
// 送信は MotionLiveHandler.BroadcastFrame（前の送信が終わっていない接続には送らない）。
//
// ■ 対象
//   hands … 右手・左手の指
//   body  … ボディ（姿勢の world 点。顔の点があれば頭、手の点があれば前腕のひねりと手首にも使う）
// ■ 左右
//   MediaPipe の leftHand / rightHand が本人の左右か画像の左右かは未確認。swapHands=true で入れ替える。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Motion;
using Poly_Ling.Tools.MediaPipe;

namespace Poly_Ling.Player
{
    [PLTool("mediaPipePose", Description = "メディアパイプ（姿勢）：MediaPipe の点からマッスル値を求めて、つながっている接続へ送る")]
    public sealed class MediaPipePoseHandler
    {
        /// <summary>データの入手先・送信先（motionLive）。</summary>
        public MotionLiveHandler Live;
        /// <summary>状態が変わったときに呼ぶ（パネルの表示更新）。</summary>
        public Action            OnChanged;

        private MediaPipePoseSolver _solver;
        private readonly MotionLiveFrame _frame = new MotionLiveFrame();
        private uint _lastSeq = uint.MaxValue;

        [PLToolState(Description = "開始中か")]
        public bool   Running { get; private set; }
        [PLToolState(Description = "手の指を解くか")]
        public bool   Hands { get; private set; }
        [PLToolState(Description = "ボディを解くか")]
        public bool   Body { get; private set; }
        [PLToolState(Description = "左右の手を入れ替えるか")]
        public bool   SwapHands { get; private set; }
        [PLToolState(Description = "処理したフレーム数")]
        public int    Processed { get; private set; }
        [PLToolState(Description = "送ったフレーム数（つながっている接続があったもの）")]
        public int    Sent { get; private set; }
        [PLToolState(Description = "直近のフレームで解いた部位")]
        public string LastParts { get; private set; } = "";
        [PLToolState(Description = "直近の操作・処理の結果")]
        public string Status { get; private set; } = "";

        /// <summary>開始する。hands・body で解く部位、swapHands で左右の入れ替えを決める。</summary>
        [PLToolAction(Description = "開始（hands：手の指を解く、body：ボディを解く、swapHands：左右の手を入れ替える）")]
        public void Start(bool hands, bool body, bool swapHands)
        {
            Stop();
            if (Live == null || !Live.Listening) { SetStatus("motionLive を受け入れ許可にしてください"); return; }
            _solver = new MediaPipePoseSolver();
            if (!_solver.Build(out string reason))
            {
                _solver.Dispose();
                _solver = null;
                SetStatus("開始できません: " + reason);
                return;
            }
            Hands = hands; Body = body; SwapHands = swapHands;
            Processed = 0; Sent = 0; LastParts = "";
            _lastSeq = uint.MaxValue;
            Running = true;
            SetStatus("開始しました。MediaPipe クライアントからのデータを待っています");
        }

        [PLToolAction(Description = "停止")]
        public void Stop()
        {
            if (!Running && _solver == null) return;
            Running = false;
            _solver?.Dispose();
            _solver = null;
            SetStatus("停止しました");
        }

        /// <summary>motionLive が MediaPipe のデータを受けたとき（メインスレッド）に呼ぶ。</summary>
        public void OnMediaPipe()
        {
            if (!Running || _solver == null || Live == null) return;
            if (!Live.TryGetLatestMediaPipe(out var mp) || mp.Seq == _lastSeq) return;
            _lastSeq = mp.Seq;

            _solver.Clear();
            var parts = new List<string>();
            var rh = ReadWorld(SwapHands ? mp.LeftHand : mp.RightHand, MediaPipeFrame.HandPoints);
            var lh = ReadWorld(SwapHands ? mp.RightHand : mp.LeftHand, MediaPipeFrame.HandPoints);
            var pose = Body ? ReadWorld(mp.Pose, MediaPipeFrame.PosePoints) : null;
            if (pose != null)
            {
                var face = ReadFace(mp);
                _solver.SolveBody(pose, face, rh, lh);
                _solver.AdjustBody();
                parts.Add("ボディ");
            }
            if (Hands)
            {
                if (rh != null) { _solver.SolveRightHand(rh); _solver.AdjustRightHand(); parts.Add("右手"); }
                if (lh != null) { _solver.SolveLeftHand(lh);  _solver.AdjustLeftHand();  parts.Add("左手"); }
            }
            _solver.AdjustWholeBody();
            Processed++;
            LastParts = string.Join("・", parts);

            if (_solver.Locals.Count == 0) { SetStatus("解けた部位がありません（手が写っていないなど）"); return; }
            if (!_solver.ToMuscles(_frame)) return;
            _frame.Source = MotionLiveJson.SourceMediaPipe;
            _frame.Time   = mp.Time;
            if (Live.ClientCount > 0 && Live.BroadcastFrame(_frame)) Sent++;
            if (Processed % 10 == 1) SetStatus($"処理中（{LastParts}）");
        }

        // 部位の world の先頭 count 点を正準骨格の座標で返す。world が無い・足りなければ null。
        private static Vector3[] ReadWorld(MediaPipePart part, int count)
        {
            if (part?.World == null || part.World.Length < count) return null;
            var points = new Vector3[count];
            for (int i = 0; i < count; i++) points[i] = MediaPipeCoords.ToUnity(part.World[i]);
            return points;
        }

        // 顔の点（画像基準の正規化座標）を画素単位にして正準骨格の座標で返す。無ければ null。
        private static Vector3[] ReadFace(MediaPipeFrame mp)
        {
            var lm = mp.Face?.Landmarks;
            if (lm == null || lm.Length < MediaPipeFrame.FaceMeshPoints || !mp.HasImageSize) return null;
            var points = new Vector3[lm.Length];
            for (int i = 0; i < lm.Length; i++)
                points[i] = MediaPipeCoords.ToUnity(MediaPipeCoords.ToPixels(lm[i], mp.ImageWidth, mp.ImageHeight));
            return points;
        }

        private void SetStatus(string s)
        {
            Status = s;
            OnChanged?.Invoke();
        }
    }
}
