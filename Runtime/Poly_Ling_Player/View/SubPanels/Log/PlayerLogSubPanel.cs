// PlayerLogSubPanel.cs
// 統合ログ表示サブパネル（UIToolkit）。
// PlayerLog に蓄積されたログ（サーバログ＋Unity ログ）を表示し、
// テキスト選択によるコピー・全文コピー・ファイル保存・クリアを提供する。
// 併せて診断ログ（PLDiag）のスイッチをここで切り替える。
// デザインは PlayerIoUiKit（読み書きパネル）に準拠する。
//
// 【表示は追記方式】
//   更新のたびに全文を作り直すと、行数 N に対して 1 行あたり O(N)、
//   N 行投入で O(N^2) になる。PlayerLog.BuildTextSince で増分だけを取り、
//   TextField へ足す。先頭が Trim / Clear で失われたときだけ作り直す。
//
// 【表示は末尾だけ】
//   TextField が抱える文字数を DisplayMaxChars で頭打ちにする。
//   全文はコピー・保存が PlayerLog.BuildText() から直接取るため失われない。
//
// 【自動スクロール】
//   スクロール予約は 1 件だけ保持し、次を積む前に前の予約を止める。
//   予約を積みっぱなしにすると、その全部が scrollOffset を書き換え、
//   利用者のスクロール操作を奪う。
//   利用者が上へスクロールしたら追従を止め、最下部へ戻したら再開する。
//
// Runtime/Poly_Ling_Player/View/SubPanels/Log/ に配置

using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
using Poly_Ling.Diagnostics;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 右ペインに表示する統合ログ UI。
    /// PlayerLog.OnChanged を購読し、セクションが表示中のときだけ本文を更新する。
    /// </summary>
    public class PlayerLogSubPanel
    {
        // ================================================================
        // 定数
        // ================================================================

        /// <summary>保存先パスの永続化キー。</summary>
        private const string SavePathKey = "Log.SavePath";

        /// <summary>TextField が抱える最大文字数。超過分は先頭から捨てる。</summary>
        private const int DisplayMaxChars = 60000;

        /// <summary>追従再開とみなす最下部からの距離（px）。</summary>
        private const float AutoScrollResumePx = 4f;

        // ================================================================
        // 内部 UI 参照
        // ================================================================

        private VisualElement _root;
        private Label         _countLabel;
        private ScrollView    _scroll;
        private TextField     _logField;
        private TextField     _pathField;
        private Label         _statusLabel;
        private Toggle        _autoScrollToggle;

        private bool _subscribed;

        /// <summary>表示済みの総投入行数。PlayerLog.TotalAdded と突き合わせる。</summary>
        private long _cursor;

        /// <summary>末尾追従中か。</summary>
        private bool _autoScroll = true;

        /// <summary>自動追従による scrollOffset 書き換え中か（利用者操作と区別する）。</summary>
        private bool _applyingScroll;

        /// <summary>末尾へのスクロール予約。積み増さず 1 件だけ持つ。</summary>
        private IVisualElementScheduledItem _scrollItem;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            if (parent == null) return;
            parent.Clear();
            _root = parent;

            parent.Add(PlayerIoUiKit.Title("ログ"));

            // ── 行数表示 ──────────────────────────────────────────────
            _countLabel = new Label("0 行");
            _countLabel.style.fontSize     = 10;
            _countLabel.style.marginBottom = 2;
            parent.Add(_countLabel);

            // ── 本文（読み取り専用テキストボックス）───────────────────
            // ScrollView 内に高さ無指定の複数行 TextField を置き、内容ぶん伸ばす。
            // TextField なのでドラッグ選択＋Ctrl+C による部分コピーができる。
            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.minHeight    = 140;
            _scroll.style.maxHeight    = 320;
            _scroll.style.marginBottom = 4;
            _scroll.style.borderTopWidth    = _scroll.style.borderBottomWidth =
            _scroll.style.borderLeftWidth   = _scroll.style.borderRightWidth = 1;
            _scroll.style.borderTopColor    = _scroll.style.borderBottomColor =
            _scroll.style.borderLeftColor   = _scroll.style.borderRightColor =
                new StyleColor(new Color(1f, 1f, 1f, 0.20f));

            _logField = new TextField
            {
                multiline  = true,
                isReadOnly = true,
                value      = string.Empty,
            };
            _logField.style.fontSize   = 10;
            _logField.style.whiteSpace = WhiteSpace.Normal;   // white-space は継承プロパティ
            _logField.style.flexShrink = 0;                    // ScrollView 内で縮ませない
            _scroll.Add(_logField);
            parent.Add(_scroll);

            // 利用者のスクロール操作を検出して追従を切り替える。
            if (_scroll.verticalScroller != null)
                _scroll.verticalScroller.valueChanged += OnVerticalScrollChanged;

            // ── 追従トグル ───────────────────────────────────────────
            _autoScrollToggle = new Toggle("末尾に追従") { value = _autoScroll };
            _autoScrollToggle.style.fontSize    = 10;
            _autoScrollToggle.style.marginBottom = 2;
            _autoScrollToggle.RegisterValueChangedCallback(e =>
            {
                _autoScroll = e.newValue;
                if (_autoScroll) RequestScrollToBottom();
            });
            parent.Add(_autoScrollToggle);

            // ── コピー / クリア ───────────────────────────────────────
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 2;

            var btnCopy = new Button(OnCopyAll) { text = "全部コピー" };
            btnCopy.style.flexGrow    = 1;
            btnCopy.style.height      = 26;
            btnCopy.style.marginRight = 2;

            var btnClear = new Button(OnClear) { text = "クリア" };
            btnClear.style.flexGrow = 1;
            btnClear.style.height   = 26;

            btnRow.Add(btnCopy);
            btnRow.Add(btnClear);
            parent.Add(btnRow);

            // ── 診断ログのスイッチ ───────────────────────────────────
            BuildDiagSection(parent);

            // ── 保存 ──────────────────────────────────────────────────
            parent.Add(PlayerIoUiKit.Divider());
            parent.Add(PlayerIoUiKit.SectionLabel("ファイル保存"));

            _pathField = new TextField();
            _pathField.RegisterValueChangedCallback(e => RecentPaths.Set(SavePathKey, e.newValue));
            parent.Add(PlayerIoUiKit.PathRow(_pathField, OnBrowseSave));
            _pathField.SetValueWithoutNotify(RecentPaths.Get(SavePathKey));

            parent.Add(PlayerIoUiKit.WideBtn("保存", OnSave));

            // ── ステータス ───────────────────────────────────────────
            _statusLabel = PlayerIoUiKit.StatusLabel();
            parent.Add(_statusLabel);

            if (!_subscribed)
            {
                PlayerLog.OnChanged += OnLogChanged;
                _subscribed = true;
            }

            // 再構築時は表示済み位置を捨てて作り直す。
            _cursor = -1;
            UpdateText();
        }

        /// <summary>購読解除。PolyLingPlayerViewerCore.Dispose から呼ぶ。</summary>
        public void Dispose()
        {
            _scrollItem?.Pause();
            _scrollItem = null;

            if (_scroll?.verticalScroller != null)
                _scroll.verticalScroller.valueChanged -= OnVerticalScrollChanged;

            if (!_subscribed) return;
            PlayerLog.OnChanged -= OnLogChanged;
            _subscribed = false;
        }

        // ================================================================
        // 診断ログのスイッチ
        // ================================================================

        /// <summary>
        /// PLDiag の各スイッチを切り替える行を作る。
        ///
        /// PLDiag の出力は Debug.Log を経由し、1 件ごとに PlayerLog へも積まれる。
        /// 既定は全て OFF で、採取したいときだけここで ON にする。
        /// </summary>
        private void BuildDiagSection(VisualElement parent)
        {
            parent.Add(PlayerIoUiKit.Divider());
            parent.Add(PlayerIoUiKit.SectionLabel("診断ログ（既定は全て OFF）"));

            parent.Add(DiagToggle("全体有効 (Enabled)", () => PLDiag.Enabled,  v => PLDiag.Enabled  = v));
            parent.Add(DiagToggle("コマンド (Cmd)",     () => PLDiag.Command,  v => PLDiag.Command  = v));
            parent.Add(DiagToggle("通知 (Notify)",      () => PLDiag.Notify,   v => PLDiag.Notify   = v));
            parent.Add(DiagToggle("描画入口 (Viewport)", () => PLDiag.Viewport, v => PLDiag.Viewport = v));
            parent.Add(DiagToggle("属性 (Attr)",        () => PLDiag.Attr,     v => PLDiag.Attr     = v));
            parent.Add(DiagToggle("Undo",               () => PLDiag.Undo,     v => PLDiag.Undo     = v));

            parent.Add(PlayerIoUiKit.SectionLabel("ピック／移動"));

            parent.Add(DiagToggle("逐次出力 (Pick)", () => PLDiag.Pick, v => PLDiag.Pick = v));
            parent.Add(DiagToggle("自動ダンプ (PickAutoDump)",
                                  () => PLDiag.PickAutoDump,
                                  v =>
                                  {
                                      PLDiag.PickAutoDump = v;
                                      if (v) PLDiag.ResetPickDumpBudget();
                                  }));

            parent.Add(DiagToggle("頂点同期 (EditSync)",   () => PLDiag.EditSync,    v => PLDiag.EditSync    = v));
            parent.Add(DiagToggle("Undo 詳細 (UndoVerbose)", () => PLDiag.UndoVerbose, v => PLDiag.UndoVerbose = v));

            var diagRow = new VisualElement();
            diagRow.style.flexDirection = FlexDirection.Row;
            diagRow.style.marginTop     = 2;

            var btnDump = new Button(OnPickDumpNow) { text = "いまダンプ" };
            btnDump.style.flexGrow    = 1;
            btnDump.style.height      = 24;
            btnDump.style.marginRight = 2;

            var btnReset = new Button(OnResetDumpBudget) { text = "ダンプ回数リセット" };
            btnReset.style.flexGrow = 1;
            btnReset.style.height   = 24;

            diagRow.Add(btnDump);
            diagRow.Add(btnReset);
            parent.Add(diagRow);
        }

        /// <summary>PLDiag のフィールド 1 個ぶんのトグル行。</summary>
        private static Toggle DiagToggle(string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() };
            t.style.fontSize = 10;
            t.RegisterValueChangedCallback(e => set(e.newValue));
            return t;
        }

        private void OnPickDumpNow()
        {
            PLDiag.PickDumpNow();
            SetStatus("ピックリングをダンプしました");
        }

        private void OnResetDumpBudget()
        {
            PLDiag.ResetPickDumpBudget();
            SetStatus("自動ダンプの発行数をリセットしました");
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            UpdateText();
        }

        // ================================================================
        // 更新
        // ================================================================

        /// <summary>PlayerLog 変化時のハンドラ。非表示中は本文更新を行わない。</summary>
        private void OnLogChanged()
        {
            if (_root == null || _root.panel == null) return;
            if (_root.style.display == DisplayStyle.None) return;
            UpdateText();
        }

        private void UpdateText()
        {
            if (_logField == null) return;

            string chunk = PlayerLog.BuildTextSince(_cursor, out long newCursor, out bool fullRebuild);

            // 変化なし。TextField を触らない（触ると再レイアウトが走る）。
            if (!fullRebuild && newCursor == _cursor && chunk.Length == 0) return;

            string text;
            if (fullRebuild)
            {
                text = chunk;
            }
            else
            {
                string cur = _logField.value ?? string.Empty;
                text = cur.Length == 0 ? chunk
                     : (chunk.Length == 0 ? cur : cur + "\n" + chunk);
            }

            bool trimmedForDisplay = false;
            if (text.Length > DisplayMaxChars)
            {
                int cut = text.Length - DisplayMaxChars;
                int nl  = text.IndexOf('\n', cut);
                text = nl >= 0 ? text.Substring(nl + 1) : text.Substring(cut);
                trimmedForDisplay = true;
            }

            _logField.SetValueWithoutNotify(text);
            _cursor = newCursor;

            if (_countLabel != null)
            {
                _countLabel.text = trimmedForDisplay
                    ? $"{PlayerLog.Count} 行（表示は末尾のみ。全文はコピー／保存で取得）"
                    : $"{PlayerLog.Count} 行";
            }

            RequestScrollToBottom();
        }

        /// <summary>
        /// 最下部へのスクロールを予約する。
        /// 前の予約は捨てる（積み増すと予約の数だけ scrollOffset が書き換わる）。
        /// </summary>
        private void RequestScrollToBottom()
        {
            if (_scroll == null || !_autoScroll) return;
            _scrollItem?.Pause();
            _scrollItem = _scroll.schedule.Execute(ScrollToBottom);
        }

        private void ScrollToBottom()
        {
            if (_scroll == null || !_autoScroll) return;
            float h = _scroll.contentContainer?.layout.height ?? 0f;

            _applyingScroll = true;
            try   { _scroll.scrollOffset = new Vector2(0f, h); }
            finally { _applyingScroll = false; }
        }

        /// <summary>
        /// 利用者がスクロールしたときの追従切り替え。
        /// 自動追従による書き換え（_applyingScroll）は対象外。
        /// </summary>
        private void OnVerticalScrollChanged(float value)
        {
            if (_applyingScroll) return;
            if (_scroll?.verticalScroller == null) return;

            float max = _scroll.verticalScroller.highValue;
            bool  atBottom = max <= 0f || (max - value) <= AutoScrollResumePx;

            if (_autoScroll == atBottom) return;
            _autoScroll = atBottom;
            _autoScrollToggle?.SetValueWithoutNotify(atBottom);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnCopyAll()
        {
            string text = PlayerLog.BuildText();
            if (string.IsNullOrEmpty(text)) { SetStatus("ログが空です"); return; }
            GUIUtility.systemCopyBuffer = text;
            SetStatus($"{PlayerLog.Count} 行をコピーしました");
        }

        private void OnClear()
        {
            PlayerLog.Clear();
            _cursor = -1;
            _logField?.SetValueWithoutNotify(string.Empty);
            UpdateText();
            SetStatus("クリアしました");
        }

        private void OnBrowseSave()
        {
            string path = PlayerIoUiKit.AskSavePath(
                "ログを保存", SavePathKey, _pathField?.value, DefaultFileName(), "txt");
            if (!string.IsNullOrEmpty(path))
                _pathField.value = path;
        }

        private void OnSave()
        {
            string path = _pathField?.value;
            if (string.IsNullOrEmpty(path))
            {
                path = Path.Combine(Application.persistentDataPath, DefaultFileName());
                _pathField.value = path;
            }

            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // ファイル内は OS 既定の改行に揃える。
                string text = PlayerLog.BuildText().Replace("\n", Environment.NewLine);
                File.WriteAllText(path, text, new UTF8Encoding(false));

                SetStatus($"保存しました: {path}");
            }
            catch (Exception ex)
            {
                SetStatus($"保存失敗: {ex.Message}");
            }
        }

        private static string DefaultFileName()
            => $"polyling_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

        private void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }
    }
}
