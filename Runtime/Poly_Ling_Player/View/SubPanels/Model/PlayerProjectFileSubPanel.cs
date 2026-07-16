// PlayerProjectFileSubPanel.cs
// プレイビュー右ペイン用 プロジェクトファイル保存/読み込みパネル（UIToolkit）。
// .mfproj（JSON）とCSVフォルダ形式の両方に対応する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.EditorBridge;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 右ペインに表示するプロジェクトファイル UI。
    /// .mfproj(JSON) / CSVフォルダ それぞれで 開く / 保存 / 名前を付けて保存 を提供する。
    /// 各操作の実行は Viewer 側コールバックに委譲する。
    /// </summary>
    public class PlayerProjectFileSubPanel
    {
        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>.mfproj 開く（指定パスから読込）。</summary>
        public Action<string> OnLoad;

        /// <summary>.mfproj 保存（指定パスへ上書き保存）。</summary>
        public Action<string> OnSave;

        /// <summary>.mfproj 名前を付けて保存（ダイアログ）。</summary>
        public Action OnSaveAs;

        /// <summary>CSVフォルダ 開く（指定パスから読込。merge=true で既存モデルにマージ）。</summary>
        public Action<string, bool> OnLoadCsv;

        /// <summary>CSVフォルダ 保存（指定パスへ保存）。</summary>
        public Action<string> OnSaveCsv;

        /// <summary>CSVフォルダ 名前を付けて保存（ダイアログ）。</summary>
        public Action OnSaveAsCsv;

        // ================================================================
        // 内部 UI 参照
        // ================================================================

        private Label     _statusLabel;
        private TextField _jsonPathField;
        private TextField _csvPathField;
        private Toggle    _csvMergeToggle;

        private const string JsonPathKey = "Project.JsonPath";
        private const string CsvPathKey  = "Project.CsvPath";

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            parent.Clear();

            var title = new Label("プロジェクトファイル");
            title.style.color = new StyleColor(Color.white);
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6;
            parent.Add(title);

            // ── .mfproj(JSON) セクション ──────────────────────────────
            parent.Add(SectionLabel(".mfproj (JSON)"));

            _jsonPathField = new TextField();
            _jsonPathField.RegisterValueChangedCallback(e => RecentPaths.Set(JsonPathKey, e.newValue));
            parent.Add(MakePathRow(_jsonPathField, OnBrowseJson));
            _jsonPathField.SetValueWithoutNotify(RecentPaths.Get(JsonPathKey));

            parent.Add(MakeWideBtn("開く", () => OnLoad?.Invoke(_jsonPathField.value)));
            parent.Add(MakeSpacer());
            parent.Add(MakeWideBtn("保存", () => OnSave?.Invoke(_jsonPathField.value)));
            parent.Add(MakeWideBtn("名前を付けて保存", () => OnSaveAs?.Invoke()));

            // ── 区切り線 ──────────────────────────────────────────────
            parent.Add(Divider());

            // ── CSV セクション ────────────────────────────────────────
            parent.Add(SectionLabel("CSV"));

            _csvPathField = new TextField();
            _csvPathField.RegisterValueChangedCallback(e => RecentPaths.Set(CsvPathKey, e.newValue));
            parent.Add(MakePathRow(_csvPathField, OnBrowseCsv));
            _csvPathField.SetValueWithoutNotify(RecentPaths.Get(CsvPathKey));

            _csvMergeToggle = new Toggle("追加マージ");
            _csvMergeToggle.tooltip = "CSVフォルダからメッシュを追加（名前重複時は置き換え）";
            _csvMergeToggle.style.marginBottom = 2;
            parent.Add(_csvMergeToggle);

            parent.Add(MakeWideBtn("開く", () => OnLoadCsv?.Invoke(_csvPathField.value, _csvMergeToggle.value)));
            parent.Add(MakeSpacer());
            parent.Add(MakeWideBtn("保存", () => OnSaveCsv?.Invoke(_csvPathField.value)));
            parent.Add(MakeWideBtn("名前を付けて保存", () => OnSaveAsCsv?.Invoke()));

            // ── ステータス ───────────────────────────────────────────
            _statusLabel = new Label("");
            _statusLabel.style.color      = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.marginTop  = 4;
            parent.Add(_statusLabel);
        }

        // ================================================================
        // browse（[...] = 指定して開く）
        // ================================================================

        private void OnBrowseJson()
        {
            string dir = string.IsNullOrEmpty(_jsonPathField.value)
                ? Application.dataPath
                : Path.GetDirectoryName(_jsonPathField.value);
            string path = PLEditorBridge.I.OpenFilePanel("プロジェクトを開く", dir, "mfproj");
            if (!string.IsNullOrEmpty(path))
                _jsonPathField.value = path;
        }

        private void OnBrowseCsv()
        {
            string dir = string.IsNullOrEmpty(_csvPathField.value)
                ? Application.dataPath
                : _csvPathField.value;
            string path = PLEditorBridge.I.OpenFolderPanel("CSVフォルダを開く", dir, "");
            if (!string.IsNullOrEmpty(path))
                _csvPathField.value = path;
        }

        // ================================================================
        // ステータス表示
        // ================================================================

        public void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        // ================================================================
        // UI ヘルパー
        // ================================================================

        /// <summary>[...]（左）＋パス用 TextField（右）の行。</summary>
        private static VisualElement MakePathRow(TextField field, Action onBrowse)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var browse = new Button(onBrowse) { text = "..." };
            browse.style.width       = 28;
            browse.style.marginRight = 2;

            field.style.flexGrow = 1;
            // カーソル(キャレット)色を白にする USS を適用（Import と同様）
            field.AddToClassList("visible-caret");
            var caretSheet = Resources.Load<StyleSheet>("PolyLingCaret");
            if (caretSheet != null) field.styleSheets.Add(caretSheet);

            row.Add(browse);
            row.Add(field);
            return row;
        }

        /// <summary>幅いっぱいのボタン（縦積み用）。</summary>
        private static Button MakeWideBtn(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.height       = 26;
            b.style.marginBottom = 2;
            return b;
        }

        private static VisualElement MakeSpacer()
        {
            var s = new VisualElement();
            s.style.height = 8;
            return s;
        }

        private static VisualElement Divider()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 6;
            v.style.marginBottom    = 6;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        private static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.color        = new StyleColor(new Color(0.6f, 0.8f, 1f));
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }
    }
}
