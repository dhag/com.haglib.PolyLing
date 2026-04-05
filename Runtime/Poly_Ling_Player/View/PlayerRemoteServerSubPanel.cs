// PlayerRemoteServerSubPanel.cs
// RemoteServer UI の Player 版サブパネル（完全版）。
// IMGUI → UIToolkit。PolyLingPlayerServer MonoBehaviour への参照で操作する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Player
{
    public class PlayerRemoteServerSubPanel
    {
        /// <summary>PolyLingPlayerServer への参照。PolyLingPlayerViewer から SerializeField 経由で設定する。</summary>
        public Func<PolyLingPlayerServer> GetServer;

        private const string AutoStartPrefKey = "PolyLing.RemoteServer.AutoStart";
        private const string AutoStartPortKey = "PolyLing.RemoteServer.Port";

        // UI
        private Label         _missingLabel;
        private VisualElement _mainContent;
        private Label         _statusInfo;
        private IntegerField  _portField;
        private Toggle        _autoStartToggle;
        private Button        _btnStart, _btnStop;
        private Label         _capturedInfo;
        private Button        _btnSendImages, _btnClearImages, _btnSendHeader;
        private ScrollView    _logScroll;
        private VisualElement _logContainer;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("Remote Panel Server"));

            // サーバ未設定警告
            _missingLabel = new Label(
                "PolyLingPlayerServer が見つかりません。\n" +
                "Hierarchy の PolyLingPlayerViewer の\n_playerServer フィールドに設定してください。");
            _missingLabel.style.display      = DisplayStyle.None;
            _missingLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _missingLabel.style.fontSize     = 10;
            _missingLabel.style.marginBottom = 4;
            _missingLabel.style.whiteSpace   = WhiteSpace.Normal;
            root.Add(_missingLabel);

            _mainContent = new VisualElement();
            _mainContent.style.display = DisplayStyle.None;
            root.Add(_mainContent);
            BuildMainContent(_mainContent);
        }

        private void BuildMainContent(VisualElement root)
        {
            // サーバー状態
            _statusInfo = new Label();
            _statusInfo.style.fontSize     = 10;
            _statusInfo.style.marginBottom = 4;
            root.Add(_statusInfo);

            // ポート設定
            var portRow = new VisualElement(); portRow.style.flexDirection = FlexDirection.Row; portRow.style.marginBottom = 3;
            var portLbl = new Label("Port"); portLbl.style.width = 50; portLbl.style.fontSize = 10; portLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _portField = new IntegerField { value = PLEditorBridge.I.GetPrefInt(AutoStartPortKey, 8765) };
            _portField.style.flexGrow = 1;
            _portField.RegisterValueChangedCallback(e => PLEditorBridge.I.SetPrefInt(AutoStartPortKey, e.newValue));
            portRow.Add(portLbl); portRow.Add(_portField);
            root.Add(portRow);

            // 自動起動 Toggle
            _autoStartToggle = new Toggle("アプリ起動時に自動開始")
            { value = PLEditorBridge.I.GetPrefBool(AutoStartPrefKey, false) };
            _autoStartToggle.style.marginBottom = 4;
            _autoStartToggle.RegisterValueChangedCallback(e =>
                PLEditorBridge.I.SetPrefBool(AutoStartPrefKey, e.newValue));
            root.Add(_autoStartToggle);

            // Start / Stop ボタン
            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginBottom = 6;
            _btnStart = new Button(OnStart) { text = "Start Server" }; _btnStart.style.flexGrow = 1; _btnStart.style.marginRight = 4;
            _btnStop  = new Button(OnStop)  { text = "Stop Server" };  _btnStop.style.flexGrow  = 1;
            btnRow.Add(_btnStart); btnRow.Add(_btnStop);
            root.Add(btnRow);

            root.Add(MakeSep());

            // ── Captured Images ───────────────────────────────────────────
            root.Add(SecLabel("Captured Images"));
            _capturedInfo = new Label();
            _capturedInfo.style.fontSize     = 10;
            _capturedInfo.style.color        = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _capturedInfo.style.marginBottom = 3;
            root.Add(_capturedInfo);

            var imgRow = new VisualElement(); imgRow.style.flexDirection = FlexDirection.Row; imgRow.style.marginBottom = 4;
            _btnSendImages  = new Button(OnSendImages)  { text = "Send Images" };  _btnSendImages.style.flexGrow  = 1; _btnSendImages.style.marginRight  = 3;
            _btnClearImages = new Button(OnClearImages) { text = "Clear" };         _btnClearImages.style.flexGrow = 1; _btnClearImages.style.marginRight = 3;
            _btnSendHeader  = new Button(OnSendHeader)  { text = "Send Header" };   _btnSendHeader.style.flexGrow  = 1;
            imgRow.Add(_btnSendImages); imgRow.Add(_btnClearImages); imgRow.Add(_btnSendHeader);
            root.Add(imgRow);

            root.Add(MakeSep());

            // ── ログ ──────────────────────────────────────────────────────
            root.Add(SecLabel("Log"));
            _logScroll = new ScrollView(ScrollViewMode.Vertical);
            _logScroll.style.maxHeight  = 150;
            _logScroll.style.minHeight  = 60;
            _logScroll.style.marginBottom = 3;
            _logScroll.style.borderTopWidth = _logScroll.style.borderBottomWidth =
            _logScroll.style.borderLeftWidth = _logScroll.style.borderRightWidth = 1;
            _logScroll.style.borderTopColor = _logScroll.style.borderBottomColor =
            _logScroll.style.borderLeftColor = _logScroll.style.borderRightColor =
                new StyleColor(Color.white);
            _logContainer = new VisualElement();
            _logScroll.Add(_logContainer);
            root.Add(_logScroll);

            new Button(OnClearLog) { text = "Clear Log" }.Apply(b => { b.style.marginBottom = 4; root.Add(b); });
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            if (_missingLabel == null) return;
            var server = GetServer?.Invoke();
            if (server == null)
            {
                _missingLabel.style.display  = DisplayStyle.Flex;
                _mainContent.style.display   = DisplayStyle.None;
                return;
            }
            _missingLabel.style.display = DisplayStyle.None;
            _mainContent.style.display  = DisplayStyle.Flex;

            bool running = server.IsRunning;
            int  port    = server.Port;

            _statusInfo.text  = running
                ? $"稼働中  http://localhost:{port}/  接続数: {server.ClientCount}"
                : "停止中";
            _statusInfo.style.color = new StyleColor(running
                ? new Color(0.4f, 0.9f, 0.4f)
                : new Color(0.6f, 0.6f, 0.6f));

            _btnStart?.SetEnabled(!running);
            _btnStop?.SetEnabled(running);
            _portField?.SetEnabled(!running);

            // Captured Images 情報
            var images = server.CapturedImages;
            if (_capturedInfo != null)
            {
                if (images == null || images.Count == 0)
                    _capturedInfo.text = "(empty)";
                else
                {
                    long totalBytes = 0;
                    foreach (var img in images) totalBytes += img.Data?.Length ?? 0;
                    _capturedInfo.text = $"{images.Count} 枚  ({totalBytes / 1024} KB)";
                }
            }

            bool hasImages = images != null && images.Count > 0;
            _btnSendImages?.SetEnabled(running && hasImages);
            _btnClearImages?.SetEnabled(hasImages);
            _btnSendHeader?.SetEnabled(running);

            // ログ
            RefreshLog(server);
        }

        private void RefreshLog(PolyLingPlayerServer server)
        {
            if (_logContainer == null) return;
            _logContainer.Clear();
            var msgs = server.LogMessages;
            if (msgs == null) return;
            foreach (var msg in msgs)
            {
                var lbl = new Label(msg);
                lbl.style.fontSize = 9;
                lbl.style.color    = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
                lbl.style.whiteSpace = WhiteSpace.Normal;
                _logContainer.Add(lbl);
            }
            // 最下部にスクロール
            _logScroll?.ScrollTo(_logContainer.Children().LastOrDefault() as VisualElement ?? _logContainer);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnStart()
        {
            GetServer?.Invoke()?.StartServer();
            Refresh();
        }

        private void OnStop()
        {
            GetServer?.Invoke()?.StopServer();
            Refresh();
        }

        private void OnSendImages()
        {
            GetServer?.Invoke()?.SendCapturedImages();
            Refresh();
        }

        private void OnClearImages()
        {
            GetServer?.Invoke()?.ClearCapturedImages();
            Refresh();
        }

        private void OnSendHeader()
        {
            GetServer?.Invoke()?.SendProjectHeader();
            Refresh();
        }

        private void OnClearLog()
        {
            var server = GetServer?.Invoke();
            if (server == null) return;
            // LogMessages は ReadOnly — server 側の List をクリアするために Flush メソッドを呼ぶ
            // PolyLingPlayerServer.ClearLog() は未実装のため、ここでは別手段
            // → PolyLingPlayerServer に ClearLog() を追加済みならそれを使う
            //   今回は LogMessages が IReadOnlyList のため、内部 List には直接アクセスできない
            //   → RefreshLog で表示だけクリアする方式（ログ本体は維持）
            if (_logContainer != null) _logContainer.Clear();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static VisualElement MakeSep()
        {
            var s = new VisualElement();
            s.style.height = 1; s.style.backgroundColor = new StyleColor(Color.white);
            s.style.marginTop = 4; s.style.marginBottom = 4;
            return s;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10; l.style.marginBottom = 3; return l;
        }
    }

    // ── 拡張メソッド（Button.Apply 用）────────────────────────────────
    internal static class VeExtensions
    {
        internal static T Apply<T>(this T element, Action<T> configure) where T : VisualElement
        { configure(element); return element; }

        internal static VisualElement LastOrDefault(this System.Collections.Generic.IEnumerable<VisualElement> source)
        {
            VisualElement last = null;
            foreach (var e in source) last = e;
            return last;
        }
    }
}
