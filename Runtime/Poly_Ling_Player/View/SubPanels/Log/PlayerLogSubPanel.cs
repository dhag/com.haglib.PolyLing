// PlayerLogSubPanel.cs
// 統合ログ表示サブパネル（UIToolkit）。
// PlayerLog に蓄積されたログ（サーバログ＋Unity ログ）を表示し、
// テキスト選択によるコピー・全文コピー・ファイル保存・クリアを提供する。
// デザインは PlayerIoUiKit（読み書きパネル）に準拠する。
// Runtime/Poly_Ling_Player/View/SubPanels/Log/ に配置

using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;
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

        // ================================================================
        // 内部 UI 参照
        // ================================================================

        private VisualElement _root;
        private Label         _countLabel;
        private ScrollView    _scroll;
        private TextField     _logField;
        private TextField     _pathField;
        private Label         _statusLabel;

        private bool _subscribed;

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

            UpdateText();
        }

        /// <summary>購読解除。PolyLingPlayerViewerCore.Dispose から呼ぶ。</summary>
        public void Dispose()
        {
            if (!_subscribed) return;
            PlayerLog.OnChanged -= OnLogChanged;
            _subscribed = false;
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

            string text = PlayerLog.BuildText();
            _logField.SetValueWithoutNotify(text);

            if (_countLabel != null)
                _countLabel.text = $"{PlayerLog.Count} 行";

            // レイアウト確定後に最下部へスクロールする（一回限りの遅延実行）。
            _scroll?.schedule.Execute(ScrollToBottom);
        }

        private void ScrollToBottom()
        {
            if (_scroll == null) return;
            float h = _scroll.contentContainer?.layout.height ?? 0f;
            _scroll.scrollOffset = new Vector2(0f, h);
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
            UpdateText();
            SetStatus("クリアしました");
        }

        private void OnBrowseSave()
        {
            string cur = _pathField?.value ?? string.Empty;
            string dir = string.IsNullOrEmpty(cur)
                ? Application.persistentDataPath
                : (Path.GetDirectoryName(cur) ?? Application.persistentDataPath);
            string name = string.IsNullOrEmpty(cur)
                ? DefaultFileName()
                : Path.GetFileName(cur);

            string path = PLEditorBridge.I.SaveFilePanel("ログを保存", dir, name, "txt");
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
