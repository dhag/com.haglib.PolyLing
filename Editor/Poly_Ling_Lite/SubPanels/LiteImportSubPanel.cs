// LiteImportSubPanel.cs
// PolyLing Lite 用 PMX / MQO インポート設定パネル（UIToolkit）。
// PlayerImportSubPanel と同構成。ファイルダイアログは EditorUtility を使用。
//
// Editor/Poly_Ling_Lite/SubPanels/ に配置

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.Localization;

namespace Poly_Ling.Lite
{
    public class LiteImportSubPanel
    {
        // ================================================================
        // モード
        // ================================================================

        public enum Mode { PMX, MQO }

        private Mode _mode;

        // ================================================================
        // 設定
        // ================================================================

        private PMXImportSettings _pmxSettings = PMXImportSettings.CreateDefault();
        private MQOImportSettings _mqoSettings = MQOImportSettings.CreateDefault();

        // ================================================================
        // コールバック
        // ================================================================

        public Action<string, PMXImportSettings> OnImportPmx;
        public Action<string, MQOImportSettings> OnImportMqo;

        // ================================================================
        // 内部 UI
        // ================================================================

        private TextField     _pathField;
        private Label         _statusLabel;
        private Label         _panelNameLabel;
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

            // パスフィールド
            var fileRow = new VisualElement();
            fileRow.style.flexDirection = FlexDirection.Row;
            fileRow.style.marginBottom  = 2;

            _pathField = new TextField();
            _pathField.style.flexGrow   = 1;
            _pathField.style.marginRight = 2;

            var browseBtn = new Button(OnBrowse) { text = "..." };
            browseBtn.style.width = 28;

            fileRow.Add(_pathField);
            fileRow.Add(browseBtn);
            parent.Add(fileRow);

            var importBtn = new Button(OnImportClicked) { text = "リロード" };
            importBtn.style.marginTop    = 2;
            importBtn.style.marginBottom = 4;
            importBtn.style.height       = 28;
            importBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(importBtn);

            _statusLabel = new Label("");
            _statusLabel.style.color      = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            parent.Add(_statusLabel);

            _settingsContainer = new VisualElement();
            parent.Add(_settingsContainer);
        }

        public void SetMode(Mode mode, string filePath = null)
        {
            _mode = mode;
            if (_panelNameLabel != null)
                _panelNameLabel.text = mode == Mode.PMX ? "PMXインポータ" : "MQOインポータ";
            if (_pathField != null && !string.IsNullOrEmpty(filePath))
                _pathField.value = filePath;
            RebuildSettings();
        }

        public void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        // ================================================================
        // ファイルブラウズ（EditorUtility 使用）
        // ================================================================

        private void OnBrowse()
        {
            string ext   = _mode == Mode.PMX ? "pmx" : "mqo";
            string title = _mode == Mode.PMX ? "Select PMX File" : "Select MQO File";
            string dir   = string.IsNullOrEmpty(_pathField?.value)
                ? Application.dataPath
                : Path.GetDirectoryName(_pathField.value);

            string path = EditorUtility.OpenFilePanel(title, dir, ext);
            if (!string.IsNullOrEmpty(path))
            {
                _pathField.value = path;
                OnImportClicked();
            }
        }

        private void OnImportClicked()
        {
            var path = _pathField?.value ?? "";
            if (string.IsNullOrEmpty(path)) { SetStatus("ファイルパスを指定してください"); return; }
            if (!File.Exists(path))         { SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}"); return; }

            SetStatus("");
            if (_mode == Mode.PMX) OnImportPmx?.Invoke(path, ClonePmxSettings());
            else                   OnImportMqo?.Invoke(path, CloneMqoSettings());
        }

        // ================================================================
        // 設定 UI 構築
        // ================================================================

        private void RebuildSettings()
        {
            _settingsContainer?.Clear();
            if (_settingsContainer == null) return;
            if (_mode == Mode.PMX) BuildPmxSettings(_settingsContainer);
            else                   BuildMqoSettings(_settingsContainer);
        }

        private void BuildPmxSettings(VisualElement parent)
        {
            parent.Add(SectionLabel(TP("ImportMode")));
            var modeField = new DropdownField(
                new System.Collections.Generic.List<string>
                { TP("ModeNewModel"), TP("ModeAppend"), TP("ModeReplace") },
                (int)_pmxSettings.ImportMode);
            modeField.RegisterValueChangedCallback(e => _pmxSettings.ImportMode = (PMXImportMode)modeField.index);
            parent.Add(modeField);

            parent.Add(SectionLabel(TP("Preset")));
            var presetRow = new VisualElement();
            presetRow.style.flexDirection = FlexDirection.Row;
            presetRow.style.marginBottom  = 4;
            AddSmallBtn(presetRow, TP("Default"),  () => _pmxSettings = PMXImportSettings.CreateDefault());
            AddSmallBtn(presetRow, "MMD",           () => _pmxSettings = PMXImportSettings.CreateMMDCompatible());
            AddSmallBtn(presetRow, TP("BonesOnly"), () => _pmxSettings = PMXImportSettings.CreateBonesOnly());
            parent.Add(presetRow);
            parent.Add(Separator());

            parent.Add(SectionLabel(TP("ImportTarget")));
            parent.Add(FlagToggle(TP("TargetMesh"),   () => _pmxSettings.ShouldImportMesh,
                v => SetPmxTarget(PMXImportTarget.Mesh, v)));
            parent.Add(FlagToggle(TP("TargetBones"),  () => _pmxSettings.ShouldImportBones,
                v => SetPmxTarget(PMXImportTarget.Bones, v)));
            parent.Add(FlagToggle(TP("TargetMorphs"), () => _pmxSettings.ShouldImportMorphs,
                v => SetPmxTarget(PMXImportTarget.Morphs, v)));
            parent.Add(Separator());

            parent.Add(SectionLabel(TP("Coordinate")));
            parent.Add(FloatRow(TP("Scale"), () => _pmxSettings.Scale, v => _pmxSettings.Scale = v));
            parent.Add(ToggleRow(TP("FlipZAxis"), () => _pmxSettings.FlipZ,    v => _pmxSettings.FlipZ    = v));
            parent.Add(ToggleRow(TP("FlipUV_V"),  () => _pmxSettings.FlipUV_V, v => _pmxSettings.FlipUV_V = v));
            parent.Add(Separator());

            parent.Add(SectionLabel(TP("Options")));
            parent.Add(ToggleRow(TP("ImportMaterials"),   () => _pmxSettings.ImportMaterials,   v => _pmxSettings.ImportMaterials   = v));
            parent.Add(ToggleRow(TP("DetectNamedMirror"), () => _pmxSettings.DetectNamedMirror, v => _pmxSettings.DetectNamedMirror = v));
            parent.Add(ToggleRow(TP("BakeMirror"),        () => _pmxSettings.BakeMirror,        v => _pmxSettings.BakeMirror        = v));
            parent.Add(ToggleRow(TP("ConvertToTPose"),    () => _pmxSettings.ConvertToTPose,    v => _pmxSettings.ConvertToTPose    = v));
            parent.Add(Separator());

            parent.Add(SectionLabel(TP("Normals")));
            parent.Add(ToggleRow(TP("RecalculateNormals"), () => _pmxSettings.RecalculateNormals, v => _pmxSettings.RecalculateNormals = v));
            parent.Add(SliderRow(TP("SmoothingAngle"), 0f, 180f, () => _pmxSettings.SmoothingAngle, v => _pmxSettings.SmoothingAngle = v));
        }

        private void SetPmxTarget(PMXImportTarget flag, bool value)
        {
            if (value) _pmxSettings.ImportTarget |=  flag;
            else       _pmxSettings.ImportTarget &= ~flag;
        }

        private PMXImportSettings ClonePmxSettings()
        {
            var s = new PMXImportSettings();
            s.CopyFrom(_pmxSettings);
            return s;
        }

        private void BuildMqoSettings(VisualElement parent)
        {
            parent.Add(SectionLabel(TM("ImportMode")));
            var modeField = new DropdownField(
                new System.Collections.Generic.List<string>
                { TM("ModeNewModel"), TM("ModeAppend"), TM("ModeReplace") },
                (int)_mqoSettings.ImportMode);
            modeField.RegisterValueChangedCallback(e => _mqoSettings.ImportMode = (MQOImportMode)modeField.index);
            parent.Add(modeField);

            parent.Add(SectionLabel(TM("Preset")));
            var presetRow = new VisualElement();
            presetRow.style.flexDirection = FlexDirection.Row;
            presetRow.style.marginBottom  = 4;
            AddSmallBtn(presetRow, TM("Default"), () => _mqoSettings = MQOImportSettings.CreateDefault());
            AddSmallBtn(presetRow, "MMD",          () => _mqoSettings = MQOImportSettings.CreateMMDCompatible());
            AddSmallBtn(presetRow, "1:1",          () => _mqoSettings = MQOImportSettings.CreateNoScale());
            parent.Add(presetRow);
            parent.Add(Separator());

            parent.Add(SectionLabel(TM("Coordinate")));
            parent.Add(FloatRow(TM("Scale"), () => _mqoSettings.Scale, v => _mqoSettings.Scale = v));
            parent.Add(ToggleRow(TM("FlipZAxis"), () => _mqoSettings.FlipZ,    v => _mqoSettings.FlipZ    = v));
            parent.Add(ToggleRow(TM("FlipUV_V"),  () => _mqoSettings.FlipUV_V, v => _mqoSettings.FlipUV_V = v));
            parent.Add(Separator());

            parent.Add(SectionLabel(TM("Options")));
            parent.Add(ToggleRow(TM("ImportMaterials"),    () => _mqoSettings.ImportMaterials,    v => _mqoSettings.ImportMaterials    = v));
            parent.Add(ToggleRow(TM("SkipHiddenObjects"),  () => _mqoSettings.SkipHiddenObjects,  v => _mqoSettings.SkipHiddenObjects  = v));
            parent.Add(ToggleRow(TM("SkipEmptyObjects"),   () => _mqoSettings.SkipEmptyObjects,   v => _mqoSettings.SkipEmptyObjects   = v));
            parent.Add(ToggleRow(TM("MergeAllObjects"),    () => _mqoSettings.MergeObjects,       v => _mqoSettings.MergeObjects       = v));
            parent.Add(ToggleRow(TM("BakeMirror"),         () => _mqoSettings.BakeMirror,         v => _mqoSettings.BakeMirror         = v));
            parent.Add(Separator());

            parent.Add(SectionLabel(TM("BoneWeightSettings")));
            parent.Add(ToggleRow(TM("ImportBonesFromArmature"), () => _mqoSettings.ImportBonesFromArmature, v => _mqoSettings.ImportBonesFromArmature = v));
            parent.Add(ToggleRow(TM("ConvertToTPose"),           () => _mqoSettings.ConvertToTPose,          v => _mqoSettings.ConvertToTPose          = v));
        }

        private MQOImportSettings CloneMqoSettings()
        {
            var s = new MQOImportSettings();
            s.CopyFrom(_mqoSettings);
            return s;
        }

        // ================================================================
        // ローカライズ
        // ================================================================

        private static string TP(string key) => L.GetFrom(PMXImportTexts.Texts, key);
        private static string TM(string key) => L.GetFrom(MQOImportTexts.Texts, key);

        // ================================================================
        // UI パーツ ヘルパー（PlayerImportSubPanel と同実装）
        // ================================================================

        private static Label SectionLabel(string text, bool small = false)
        {
            var l = new Label(text);
            l.style.marginTop    = small ? 3 : 6;
            l.style.marginBottom = 2;
            l.style.color        = small
                ? new StyleColor(new Color(0.6f, 0.6f, 0.6f))
                : new StyleColor(new Color(0.7f, 0.85f, 1f));
            l.style.fontSize     = small ? 9 : 10;
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

        private static VisualElement FlagToggle(string label, Func<bool> get, Action<bool> set)
            => ToggleRow(label, get, set);

        private static VisualElement FloatRow(string label, Func<float> get, Action<float> set)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var lbl = new Label(label);
            lbl.style.width = 80; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f)); lbl.style.fontSize = 10;
            var field = new FloatField { value = get() };
            field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(e => set(e.newValue));
            row.Add(lbl); row.Add(field);
            return row;
        }

        private static VisualElement SliderRow(string label, float min, float max, Func<float> get, Action<float> set)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;
            var lbl = new Label(label);
            lbl.style.width = 80; lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f)); lbl.style.fontSize = 10;
            var slider = new Slider(min, max) { value = get() };
            slider.style.flexGrow = 1;
            slider.RegisterValueChangedCallback(e => set(e.newValue));
            row.Add(lbl); row.Add(slider);
            return row;
        }

        private static void AddSmallBtn(VisualElement parent, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow = 1; b.style.marginRight = 2; b.style.height = 18; b.style.fontSize = 9;
            parent.Add(b);
        }
    }
}
