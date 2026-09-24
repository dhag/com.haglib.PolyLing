// PlayerMediaPipePoseSubPanel.cs
// 「メディアパイプ（姿勢・指）」「メディアパイプ（姿勢・ボディ）」の右ペイン。
// どちらも窓口 "mediaPipePose" を使う。指の欄は手の指、ボディの欄はボディを解く。
// Runtime/Poly_Ling_Player/View/SubPanels/MediaPipe/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerMediaPipePoseSubPanel
    {
        /// <summary>true ならボディの欄、false なら指の欄。</summary>
        public bool IsBody;
        public IToolSurface Surface;

        private const string Tool = "mediaPipePose";

        [UiControl("start", Safety = UiSafety.SafeWrite, Description = "開始（MediaPipe クライアントのデータからマッスル値を求めて送る）")]
        private Button _btnStart;
        [UiControl("stop", Safety = UiSafety.SafeWrite, Description = "停止")]
        private Button _btnStop;
        [UiControl("swapHands", Description = "左右の手を入れ替える（MediaPipe の左右が本人の左右か画像の左右か未確認のため）")]
        private Toggle _swapToggle;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "状態")]
        private Label  _statusLabel;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            var head = new Label(IsBody ? "メディアパイプ（姿勢・ボディ）" : "メディアパイプ（姿勢・指）");
            head.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            head.style.fontSize = 10;
            head.style.marginBottom = 3;
            root.Add(head);

            root.Add(new HelpBox(IsBody
                ? "モーションパネルのライブ受信を受け入れ許可にし、MediaPipe クライアントからボディを送ってください（リアルタイム推奨）。\n" +
                  "顔も送ると頭の向き、手も送ると前腕のひねりと手首に使います。腰の位置は取れません（回転だけ）。"
                : "モーションパネルのライブ受信を受け入れ許可にし、MediaPipe クライアントから手を送ってください（リアルタイム推奨）。\n" +
                  "開始中は、届くたびに指のローカル回転を求めてマッスル値にし、つながっている接続（モーションスタジオなど）へ送ります。",
                HelpBoxMessageType.None));

            if (!IsBody)
            {
                _swapToggle = new Toggle("左右の手を入れ替える") { value = false };
                _swapToggle.style.fontSize = 10;
                root.Add(_swapToggle);
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 4;
            _btnStart = new Button(() =>
            {
                Surface?.Invoke(Tool, "start", ("hands", !IsBody), ("body", IsBody),
                                ("swapHands", _swapToggle != null && _swapToggle.value));
                Refresh();
            }) { text = "開始" };
            _btnStart.style.flexGrow = 1; _btnStart.style.marginRight = 2;
            _btnStop = new Button(() => { Surface?.Invoke(Tool, "stop"); Refresh(); }) { text = "停止" };
            _btnStop.style.flexGrow = 1;
            row.Add(_btnStart); row.Add(_btnStop);
            root.Add(row);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop = 4;
            root.Add(_statusLabel);

            Refresh();
        }

        /// <summary>状態表示とボタンの有効/無効を更新する。</summary>
        public void Refresh()
        {
            if (_statusLabel == null || Surface == null) return;
            bool running = Surface.GetBool(Tool, "running");
            bool mine    = running && Surface.GetBool(Tool, IsBody ? "body" : "hands");
            _statusLabel.text =
                (running ? (mine ? "開始中" : "開始中（もう一方の欄から）") : "停止中") +
                $"　処理 {Surface.GetInt(Tool, "processed")} / 送信 {Surface.GetInt(Tool, "sent")}" +
                (running ? $"　部位 {Surface.GetString(Tool, "lastParts")}" : "") + "\n" +
                Surface.GetString(Tool, "status");
            _btnStart?.SetEnabled(!running);
            _btnStop?.SetEnabled(running);
            _swapToggle?.SetEnabled(!running);
        }
    }
}
