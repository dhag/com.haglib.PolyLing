// PlayerExportSubPanel.cs
// プレイビュー右ペイン用 PMX / MQO エクスポート設定パネル（UIToolkit）。
// PlayerImportSubPanel と対称な設計。
// ファイル保存ダイアログは PLEditorBridge.I.SaveFilePanel 経由。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 右ペインに表示する PMX / MQO エクスポート設定 UI。
    /// Build(parent) で UIToolkit 要素を生成し、
    /// OnExportPmx / OnExportMqo コールバックで実行を Viewer に委譲する。
    /// </summary>
    public class PlayerExportSubPanel
    {
        // ================================================================
        // モード
        // ================================================================

        public enum Mode { PMX, MQO }

        private Mode _mode;

        // ================================================================
        // 設定
        // ================================================================

        private PMXExportSettings _pmxSettings = PMXExportSettings.CreateFullExport();
        private MQOExportSettings _mqoSettings = MQOExportSettings.CreateFromCoordinate(0.01f, true);

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>PMX Export 実行時。引数は (outputPath, settingsのコピー)。</summary>
        public Action<string, PMXExportSettings> OnExportPmx;

        /// <summary>MQO Export 実行時。引数は (outputPath, settingsのコピー)。</summary>
        public Action<string, MQOExportSettings> OnExportMqo;

        // ================================================================
        // 内部 UI 参照
        // ================================================================

        private Label         _panelNameLabel;
        private Label         _statusLabel;
        private VisualElement _settingsContainer;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            parent.Clear();

            _panelNameLabel = new Label("");
            _panelNameLabel.style.fontSize = 12;
            _panelNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _panelNameLabel.style.marginBottom = 4;
            parent.Add(_panelNameLabel);

            // Export ボタン
            var exportBtn = new Button(OnExportClicked) { text = "Export" };
            exportBtn.style.marginTop    = 2;
            exportBtn.style.marginBottom = 4;
            exportBtn.style.height       = 28;
            exportBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(exportBtn);

            _statusLabel = new Label("");
            _statusLabel.style.color      = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            parent.Add(_statusLabel);

            _settingsContainer = new VisualElement();
            parent.Add(_settingsContainer);
        }

        public void SetMode(Mode mode)
        {
            _mode = mode;
            if (_panelNameLabel != null)
                _panelNameLabel.text = mode == Mode.PMX ? "PMXエクスポータ" : "MQOエクスポータ";
            RebuildSettings();
        }

        // ================================================================
        // Export 実行
        // ================================================================

        private void OnExportClicked()
        {
            SetStatus("");

            string ext      = _mode == Mode.PMX ? "pmx" : "mqo";
            string title    = _mode == Mode.PMX ? "Export PMX" : "Export MQO";
            string savePath = PLEditorBridge.I.SaveFilePanel(title, Application.dataPath, "model", ext);

            if (string.IsNullOrEmpty(savePath))
                return;

            if (_mode == Mode.PMX)
                OnExportPmx?.Invoke(savePath, ClonePmxSettings());
            else
                OnExportMqo?.Invoke(savePath, CloneMqoSettings());
        }

        public void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        // ================================================================
        // 設定 UI 構築
        // ================================================================

        private void RebuildSettings()
        {
            if (_settingsContainer == null) return;
            _settingsContainer.Clear();

            if (_mode == Mode.PMX)
                BuildPmxSettings(_settingsContainer);
            else
                BuildMqoSettings(_settingsContainer);
        }

        // ────────────────────────────────────────────────────────
        // PMX 設定
        // ────────────────────────────────────────────────────────

        private void BuildPmxSettings(VisualElement parent)
        {
            parent.Add(SectionLabel("座標変換"));
            parent.Add(FloatRow("Scale",    () => _pmxSettings.Scale,    v => _pmxSettings.Scale    = v));
            parent.Add(ToggleRow("Flip Z",  () => _pmxSettings.FlipZ,    v => _pmxSettings.FlipZ    = v));
            parent.Add(ToggleRow("Flip UV V", () => _pmxSettings.FlipUV_V, v => _pmxSettings.FlipUV_V = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("出力対象"));
            parent.Add(ToggleRow("材質",   () => _pmxSettings.ExportMaterials, v => _pmxSettings.ExportMaterials = v));
            parent.Add(ToggleRow("ボーン", () => _pmxSettings.ExportBones,     v => _pmxSettings.ExportBones     = v));
            parent.Add(ToggleRow("モーフ", () => _pmxSettings.ExportMorphs,    v => _pmxSettings.ExportMorphs    = v));
            parent.Add(ToggleRow("剛体",   () => _pmxSettings.ExportBodies,    v => _pmxSettings.ExportBodies    = v));
            parent.Add(ToggleRow("ジョイント", () => _pmxSettings.ExportJoints, v => _pmxSettings.ExportJoints  = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("出力形式"));
            parent.Add(ToggleRow("バイナリ PMX", () => _pmxSettings.OutputBinaryPMX, v => _pmxSettings.OutputBinaryPMX = v));
            parent.Add(ToggleRow("CSV も出力",   () => _pmxSettings.OutputCSV,        v => _pmxSettings.OutputCSV        = v));
        }

        private PMXExportSettings ClonePmxSettings()
        {
            // PMXExportSettings にコピーコンストラクタがないため手動コピー
            return new PMXExportSettings
            {
                ExportMode       = _pmxSettings.ExportMode,
                Scale            = _pmxSettings.Scale,
                FlipZ            = _pmxSettings.FlipZ,
                FlipUV_V         = _pmxSettings.FlipUV_V,
                ExportMaterials  = _pmxSettings.ExportMaterials,
                ExportBones      = _pmxSettings.ExportBones,
                ExportMorphs     = _pmxSettings.ExportMorphs,
                ExportBodies     = _pmxSettings.ExportBodies,
                ExportJoints     = _pmxSettings.ExportJoints,
                OutputBinaryPMX  = _pmxSettings.OutputBinaryPMX,
                OutputCSV        = _pmxSettings.OutputCSV,
                DecimalPrecision = _pmxSettings.DecimalPrecision,
            };
        }

        // ────────────────────────────────────────────────────────
        // MQO 設定
        // ────────────────────────────────────────────────────────

        private void BuildMqoSettings(VisualElement parent)
        {
            parent.Add(SectionLabel("座標変換"));
            parent.Add(FloatRow("Scale",    () => _mqoSettings.Scale,    v => _mqoSettings.Scale    = v));
            parent.Add(ToggleRow("Flip Z",  () => _mqoSettings.FlipZ,    v => _mqoSettings.FlipZ    = v));
            parent.Add(ToggleRow("Flip UV V", () => _mqoSettings.FlipUV_V, v => _mqoSettings.FlipUV_V = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("出力対象"));
            parent.Add(ToggleRow("材質",            () => _mqoSettings.ExportMaterials,       v => _mqoSettings.ExportMaterials       = v));
            parent.Add(ToggleRow("ボーン",          () => _mqoSettings.ExportBones,           v => _mqoSettings.ExportBones           = v));
            parent.Add(ToggleRow("BWを埋め込む",    () => _mqoSettings.EmbedBoneWeightsInMQO, v => _mqoSettings.EmbedBoneWeightsInMQO = v));
            parent.Add(ToggleRow("BakedMirrorをスキップ", () => _mqoSettings.SkipBakedMirror, v => _mqoSettings.SkipBakedMirror       = v));
            parent.Add(ToggleRow("名前ミラー(+)をスキップ", () => _mqoSettings.SkipNamedMirror, v => _mqoSettings.SkipNamedMirror    = v));
        }

        private MQOExportSettings CloneMqoSettings() => _mqoSettings.Clone();

        // ================================================================
        // UIパーツ ヘルパー（PlayerImportSubPanel と共通パターン）
        // ================================================================

        private static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.marginTop    = 6;
            l.style.marginBottom = 2;
            l.style.color        = new StyleColor(new Color(0.7f, 0.85f, 1f));
            l.style.fontSize     = 10;
            return l;
        }

        private static VisualElement Separator()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 4;
            v.style.marginBottom    = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        private static VisualElement ToggleRow(string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() };
            t.style.marginBottom = 2;
            t.style.color        = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            t.RegisterValueChangedCallback(e => set(e.newValue));
            return t;
        }

        private static VisualElement FloatRow(string label, Func<float> get, Action<float> set)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.width          = 80;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.color          = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            lbl.style.fontSize       = 10;

            var field = new FloatField { value = get() };
            field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(e => set(e.newValue));

            row.Add(lbl);
            row.Add(field);
            return row;
        }
    }
}
