// PlayerProjectFileSubPanel.cs
// プレイビュー右ペイン用 プロジェクトファイル保存/読み込みパネル（UIToolkit）。
// .mfproj（JSON）とCSVフォルダ形式の両方に対応する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 右ペインに表示するプロジェクトファイル UI。
    /// Save / Load / CSVフォルダ保存 / CSVフォルダ読込 / CSVフォルダ追加マージ の5操作を提供する。
    /// 各操作の実行は Viewer 側コールバックに委譲する。
    /// </summary>
    public class PlayerProjectFileSubPanel
    {
        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>.mfproj 保存。Viewer が ProjectContext → ProjectDTO → ファイル保存を行う。</summary>
        public Action OnSave;

        /// <summary>.mfproj 読込。Viewer がファイル選択 → ProjectContext 復元を行う。</summary>
        public Action OnLoad;

        /// <summary>CSVフォルダ保存。Viewer が CsvProjectSerializer.ExportWithDialog を呼ぶ。</summary>
        public Action OnSaveCsv;

        /// <summary>CSVフォルダ読込。Viewer が CsvProjectSerializer.ImportWithDialog を呼ぶ。</summary>
        public Action OnLoadCsv;

        /// <summary>
        /// CSVフォルダ追加マージ。Viewer が CsvModelSerializer.LoadAllMeshEntriesFromFolder
        /// でエントリを読み込んでモデルにマージする。
        /// </summary>
        public Action OnMergeCsv;

        // ================================================================
        // 内部 UI 参照
        // ================================================================

        private Label _statusLabel;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            parent.Clear();

            var title = new Label("プロジェクトファイル");
            title.style.fontSize = 12;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 6;
            parent.Add(title);

            // ── .mfproj セクション ──────────────────────────────────
            parent.Add(SectionLabel(".mfproj (JSON)"));

            var jsonRow = new VisualElement();
            jsonRow.style.flexDirection = FlexDirection.Row;
            jsonRow.style.marginBottom  = 4;

            jsonRow.Add(MakeBtn("保存", () => OnSave?.Invoke()));
            jsonRow.Add(MakeBtn("読込", () => OnLoad?.Invoke()));
            parent.Add(jsonRow);

            // ── CSVフォルダセクション ────────────────────────────────
            parent.Add(SectionLabel("CSVフォルダ"));

            var csvRow = new VisualElement();
            csvRow.style.flexDirection = FlexDirection.Row;
            csvRow.style.marginBottom  = 4;

            csvRow.Add(MakeBtn("保存",   () => OnSaveCsv?.Invoke()));
            csvRow.Add(MakeBtn("読込",   () => OnLoadCsv?.Invoke()));

            var mergeBtn = MakeBtn("追加マージ", () => OnMergeCsv?.Invoke());
            mergeBtn.tooltip = "CSVフォルダからメッシュを追加（名前重複時は置き換え）";
            csvRow.Add(mergeBtn);
            parent.Add(csvRow);

            // ── ステータス ───────────────────────────────────────────
            _statusLabel = new Label("");
            _statusLabel.style.color      = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            _statusLabel.style.marginTop  = 4;
            parent.Add(_statusLabel);
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

        private static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.color        = new StyleColor(new Color(0.6f, 0.8f, 1f));
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static Button MakeBtn(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow    = 1;
            b.style.marginRight = 2;
            b.style.height      = 26;
            return b;
        }
    }
}
