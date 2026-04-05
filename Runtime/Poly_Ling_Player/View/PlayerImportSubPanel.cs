// PlayerImportSubPanel.cs
// プレイビュー右ペイン用 PMX / MQO インポート設定パネル（UIToolkit）。
// エディタ版 PMXImportPanel / MQOImportPanel と同じ設定項目を UIToolkit で実装し、
// PMXImportTexts / MQOImportTexts を共有する。
// ファイル選択は PLEditorBridge.I.OpenFilePanel 経由。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.PMX;
using Poly_Ling.MQO;
using Poly_Ling.Localization;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 右ペインに表示する PMX / MQO インポート設定 UI。
    /// Build(parent) で UIToolkit 要素を生成し、
    /// OnImport コールバックでインポート実行を Viewer に委譲する。
    /// </summary>
    public class PlayerImportSubPanel
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

        /// <summary>
        /// PMX Import ボタン押下時に呼ばれる。
        /// 引数は (filePath, settings のコピー)。
        /// Viewer がコマンド生成・エンキューを行う。
        /// </summary>
        public Action<string, PMXImportSettings> OnImportPmx;

        /// <summary>
        /// MQO Import ボタン押下時に呼ばれる。
        /// 引数は (filePath, settings のコピー)。
        /// </summary>
        public Action<string, MQOImportSettings> OnImportMqo;

        /// <summary>インポート後に3D表示をオートスケールするか</summary>
        public bool AutoScale => _autoScale;

        // ================================================================
        // 内部 UI 参照
        // ================================================================

        private TextField    _pathField;
        private Label        _statusLabel;
        private Label        _panelNameLabel;
        private VisualElement _settingsContainer;
        private bool         _autoScale = false;

        // ================================================================
        // Build
        // ================================================================

        /// <summary>
        /// parent に UI を構築する。
        /// 呼び出し後に SetMode() でモードを設定すること。
        /// </summary>
        public void Build(VisualElement parent)
        {
            parent.Clear();

            // ── パネル名ラベル ──
            _panelNameLabel = new Label("") ;
            _panelNameLabel.style.fontSize = 12;
            _panelNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _panelNameLabel.style.marginBottom = 4;
            parent.Add(_panelNameLabel);

            // ── ファイルパス行 ──
            var fileSection = new VisualElement();
            fileSection.style.marginBottom = 6;

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
            fileSection.Add(fileRow);

            // ── Import ボタン（パスフィールド直下）──
            var importBtn = new Button(OnImportClicked) { text = "リロード" };
            importBtn.style.marginTop    = 2;
            importBtn.style.marginBottom = 4;
            importBtn.style.height       = 28;
            importBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            fileSection.Add(importBtn);

            _statusLabel = new Label("");
            _statusLabel.style.color      = new StyleColor(new Color(1f, 0.7f, 0.4f));
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.fontSize   = 10;
            fileSection.Add(_statusLabel);
            parent.Add(fileSection);

            // ── 設定コンテナ（SetMode で再構築） ──
            _settingsContainer = new VisualElement();
            parent.Add(_settingsContainer);
        }

        /// <summary>
        /// パネルのモードを切り替え、設定 UI を再構築する。
        /// filePath が非空の場合はパスフィールドに設定する。
        /// </summary>
        public void SetMode(Mode mode, string filePath = null)
        {
            _mode = mode;

            if (_panelNameLabel != null)
                _panelNameLabel.text = mode == Mode.PMX ? "PMXインポータ" : "MQOインポータ";

            if (_pathField != null && !string.IsNullOrEmpty(filePath))
                _pathField.value = filePath;

            RebuildSettings();
        }

        // ================================================================
        // ファイルブラウズ
        // ================================================================

        private void OnBrowse()
        {
            string ext   = _mode == Mode.PMX ? "pmx" : "mqo";
            string title = _mode == Mode.PMX ? "Select PMX File" : "Select MQO File";
            string dir   = string.IsNullOrEmpty(_pathField.value)
                ? Application.dataPath
                : Path.GetDirectoryName(_pathField.value);

            string path = PLEditorBridge.I.OpenFilePanel(title, dir, ext);
            if (!string.IsNullOrEmpty(path))
            {
                _pathField.value = path;
                OnImportClicked();
            }
        }

        // ================================================================
        // Import 実行
        // ================================================================

        private void OnImportClicked()
        {
            var path = _pathField?.value ?? "";
            if (string.IsNullOrEmpty(path))
            {
                SetStatus("ファイルパスを指定してください");
                return;
            }
            if (!File.Exists(path))
            {
                SetStatus($"ファイルが見つかりません: {Path.GetFileName(path)}");
                return;
            }

            SetStatus("");

            if (_mode == Mode.PMX)
                OnImportPmx?.Invoke(path, ClonePmxSettings());
            else
                OnImportMqo?.Invoke(path, CloneMqoSettings());
        }

        private void SetStatus(string msg)
        {
            if (_statusLabel != null)
                _statusLabel.text = msg;
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
            PlayerLayoutRoot.ApplyDarkTheme(_settingsContainer);
        }

        // ────────────────────────────────────────────────────────
        // PMX 設定
        // ────────────────────────────────────────────────────────

        private void BuildPmxSettings(VisualElement parent)
        {
            // インポートモード
            parent.Add(SectionLabel(TP("ImportMode")));
            var modeField = new DropdownField(
                new System.Collections.Generic.List<string>
                {
                    TP("ModeNewModel"), TP("ModeAppend"), TP("ModeReplace")
                },
                (int)_pmxSettings.ImportMode);
            modeField.RegisterValueChangedCallback(e =>
                _pmxSettings.ImportMode = (PMXImportMode)modeField.index);
            parent.Add(modeField);

            // プリセット
            parent.Add(SectionLabel(TP("Preset")));
            var presetRow = new VisualElement();
            presetRow.style.flexDirection = FlexDirection.Row;
            presetRow.style.marginBottom  = 4;
            AddSmallBtn(presetRow, TP("Default"),      () => _pmxSettings = PMXImportSettings.CreateDefault());
            AddSmallBtn(presetRow, "MMD",               () => _pmxSettings = PMXImportSettings.CreateMMDCompatible());
            AddSmallBtn(presetRow, TP("BonesOnly"),     () => _pmxSettings = PMXImportSettings.CreateBonesOnly());
            parent.Add(presetRow);

            parent.Add(Separator());

            // インポート対象
            parent.Add(SectionLabel(TP("ImportTarget")));
            parent.Add(FlagToggle(TP("TargetMesh"),   () => _pmxSettings.ShouldImportMesh,
                v => SetPmxTarget(PMXImportTarget.Mesh, v)));
            parent.Add(FlagToggle(TP("TargetBones"),  () => _pmxSettings.ShouldImportBones,
                v => SetPmxTarget(PMXImportTarget.Bones, v)));
            parent.Add(FlagToggle(TP("TargetMorphs"), () => _pmxSettings.ShouldImportMorphs,
                v => SetPmxTarget(PMXImportTarget.Morphs, v)));

            parent.Add(Separator());

            // 座標変換
            parent.Add(SectionLabel(TP("Coordinate")));
            parent.Add(FloatRow(TP("Scale"),    () => _pmxSettings.Scale,    v => _pmxSettings.Scale    = v));
            parent.Add(ToggleRow(TP("FlipZAxis"), () => _pmxSettings.FlipZ,  v => _pmxSettings.FlipZ    = v));
            parent.Add(ToggleRow(TP("FlipUV_V"),  () => _pmxSettings.FlipUV_V, v => _pmxSettings.FlipUV_V = v));
            parent.Add(ToggleRow("3D表示オートスケール", () => _autoScale, v => _autoScale = v));

            parent.Add(Separator());

            // オプション（メッシュ時のみ）
            parent.Add(SectionLabel(TP("Options")));
            parent.Add(ToggleRow(TP("ImportMaterials"),   () => _pmxSettings.ImportMaterials,   v => _pmxSettings.ImportMaterials   = v));
            parent.Add(ToggleRow(TP("DetectNamedMirror"), () => _pmxSettings.DetectNamedMirror, v => _pmxSettings.DetectNamedMirror = v));
            parent.Add(ToggleRow(TP("BakeMirror"),        () => _pmxSettings.BakeMirror,        v => _pmxSettings.BakeMirror        = v));
            parent.Add(ToggleRow(TP("ConvertToTPose"),    () => _pmxSettings.ConvertToTPose,    v => _pmxSettings.ConvertToTPose    = v));

            parent.Add(Separator());

            // アルファ
            parent.Add(SectionLabel(TP("AlphaSettings")));
            parent.Add(SliderRow(TP("AlphaCutoff"), 0f, 1f, () => _pmxSettings.AlphaCutoff, v => _pmxSettings.AlphaCutoff = v));
            parent.Add(EnumRow(
                TP("AlphaConflict"),
                new[] { TP("AlphaConflictTransparent"), TP("AlphaConflictAlphaClip") },
                () => (int)_pmxSettings.AlphaConflict,
                v  => _pmxSettings.AlphaConflict = (AlphaConflictMode)v));

            parent.Add(Separator());

            // 法線
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

        // ────────────────────────────────────────────────────────
        // MQO 設定
        // ────────────────────────────────────────────────────────

        private void BuildMqoSettings(VisualElement parent)
        {
            // インポートモード
            parent.Add(SectionLabel(TM("ImportMode")));
            var modeField = new DropdownField(
                new System.Collections.Generic.List<string>
                {
                    TM("ModeNewModel"), TM("ModeAppend"), TM("ModeReplace")
                },
                (int)_mqoSettings.ImportMode);
            modeField.RegisterValueChangedCallback(e =>
                _mqoSettings.ImportMode = (MQOImportMode)modeField.index);
            parent.Add(modeField);

            // プリセット
            parent.Add(SectionLabel(TM("Preset")));
            var presetRow = new VisualElement();
            presetRow.style.flexDirection = FlexDirection.Row;
            presetRow.style.marginBottom  = 4;
            AddSmallBtn(presetRow, TM("Default"), () => _mqoSettings = MQOImportSettings.CreateDefault());
            AddSmallBtn(presetRow, "MMD",          () => _mqoSettings = MQOImportSettings.CreateMMDCompatible());
            AddSmallBtn(presetRow, "1:1",          () => _mqoSettings = MQOImportSettings.CreateNoScale());
            parent.Add(presetRow);

            parent.Add(Separator());

            // 座標変換
            parent.Add(SectionLabel(TM("Coordinate")));
            parent.Add(FloatRow(TM("Scale"),    () => _mqoSettings.Scale,    v => _mqoSettings.Scale    = v));
            parent.Add(ToggleRow(TM("FlipZAxis"), () => _mqoSettings.FlipZ,  v => _mqoSettings.FlipZ    = v));
            parent.Add(ToggleRow(TM("FlipUV_V"),  () => _mqoSettings.FlipUV_V, v => _mqoSettings.FlipUV_V = v));
            parent.Add(ToggleRow("3D表示オートスケール", () => _autoScale, v => _autoScale = v));

            parent.Add(Separator());

            // オプション
            parent.Add(SectionLabel(TM("Options")));
            parent.Add(ToggleRow(TM("ImportMaterials"),    () => _mqoSettings.ImportMaterials,    v => _mqoSettings.ImportMaterials    = v));
            parent.Add(ToggleRow(TM("SkipHiddenObjects"),  () => _mqoSettings.SkipHiddenObjects,  v => _mqoSettings.SkipHiddenObjects  = v));
            parent.Add(ToggleRow(TM("SkipEmptyObjects"),   () => _mqoSettings.SkipEmptyObjects,   v => _mqoSettings.SkipEmptyObjects   = v));
            parent.Add(ToggleRow(TM("MergeAllObjects"),    () => _mqoSettings.MergeObjects,       v => _mqoSettings.MergeObjects       = v));
            parent.Add(ToggleRow(TM("BakeMirror"),         () => _mqoSettings.BakeMirror,         v => _mqoSettings.BakeMirror         = v));

            parent.Add(Separator());

            // アルファ
            parent.Add(SectionLabel(TM("AlphaSettings")));
            parent.Add(SliderRow(TM("AlphaCutoff"), 0f, 1f, () => _mqoSettings.AlphaCutoff, v => _mqoSettings.AlphaCutoff = v));
            parent.Add(EnumRow(
                TM("AlphaConflict"),
                new[] { TM("AlphaConflictTransparent"), TM("AlphaConflictAlphaClip") },
                () => (int)_mqoSettings.AlphaConflict,
                v  => _mqoSettings.AlphaConflict = (AlphaConflictMode)v));

            parent.Add(Separator());

            // 法線
            parent.Add(SectionLabel(TM("Normals")));
            parent.Add(EnumRow(
                TM("NormalMode"),
                new[] { "FaceNormal", "Smooth", "Unity" },
                () => (int)_mqoSettings.NormalMode,
                v  => _mqoSettings.NormalMode = (MQO.NormalMode)v));
            parent.Add(SliderRow(TM("SmoothingAngle"), 0f, 180f, () => _mqoSettings.SmoothingAngle, v => _mqoSettings.SmoothingAngle = v));

            parent.Add(Separator());

            // ボーン/ウェイト
            parent.Add(SectionLabel(TM("BoneWeightSettings")));
            parent.Add(SectionLabel(TM("MqoSpecialFaces"), small: true));
            parent.Add(ToggleRow(TM("SkipMqoBoneIndices"), () => _mqoSettings.SkipMqoBoneIndices, v => _mqoSettings.SkipMqoBoneIndices = v));
            parent.Add(ToggleRow(TM("SkipMqoBoneWeights"), () => _mqoSettings.SkipMqoBoneWeights, v => _mqoSettings.SkipMqoBoneWeights = v));

            parent.Add(SectionLabel(TM("ArmatureBones"), small: true));
            parent.Add(ToggleRow(TM("ImportBonesFromArmature"), () => _mqoSettings.ImportBonesFromArmature, v => _mqoSettings.ImportBonesFromArmature = v));
            parent.Add(ToggleRow(TM("ConvertToTPose"),           () => _mqoSettings.ConvertToTPose,          v => _mqoSettings.ConvertToTPose          = v));

            parent.Add(SectionLabel(TM("ExternalCSV"), small: true));
            parent.Add(CsvPathRow(TM("BoneWeightCSV"), () => _mqoSettings.BoneWeightCSVPath, v => _mqoSettings.BoneWeightCSVPath = v, "csv"));
            parent.Add(CsvPathRow(TM("BoneCSV"),       () => _mqoSettings.BoneCSVPath,       v => _mqoSettings.BoneCSVPath       = v, "csv"));
        }

        private MQOImportSettings CloneMqoSettings()
        {
            var s = new MQOImportSettings();
            s.CopyFrom(_mqoSettings);
            return s;
        }

        // ================================================================
        // ローカライズヘルパー
        // ================================================================

        private static string TP(string key) => L.GetFrom(PMXImportTexts.Texts, key);
        private static string TM(string key) => L.GetFrom(MQOImportTexts.Texts, key);

        // ================================================================
        // UIパーツ ヘルパー
        // ================================================================

        private static Label SectionLabel(string text, bool small = false)
        {
            var l = new Label(text);
            l.style.marginTop    = small ? 3 : 6;
            l.style.marginBottom = 2;
            l.style.color        = small
                ? new StyleColor(Color.white)
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

        /// <summary>Toggle 行（ラベル + Toggle）</summary>
        private static VisualElement ToggleRow(string label, Func<bool> get, Action<bool> set)
        {
            var t = new Toggle(label) { value = get() };
            t.style.marginBottom = 2;
            t.RegisterValueChangedCallback(e => set(e.newValue));
            return t;
        }

        /// <summary>Flag Toggle（PMXImportTarget 用）</summary>
        private static VisualElement FlagToggle(string label, Func<bool> get, Action<bool> set)
            => ToggleRow(label, get, set);

        /// <summary>Float 入力行（ラベル + FloatField）</summary>
        private static VisualElement FloatRow(string label, Func<float> get, Action<float> set)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.width          = 80;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.fontSize       = 10;

            var field = new FloatField { value = get() };
            field.style.flexGrow = 1;
            field.RegisterValueChangedCallback(e => set(e.newValue));

            row.Add(lbl);
            row.Add(field);
            return row;
        }

        /// <summary>Slider 行</summary>
        private static VisualElement SliderRow(string label, float min, float max, Func<float> get, Action<float> set)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.width          = 80;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.fontSize       = 10;

            var slider = new Slider(min, max) { value = get() };
            slider.style.flexGrow = 1;
            slider.RegisterValueChangedCallback(e => set(e.newValue));

            row.Add(lbl);
            row.Add(slider);
            return row;
        }

        /// <summary>DropdownField 行</summary>
        private static VisualElement EnumRow(string label, string[] choices, Func<int> get, Action<int> set)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.width          = 80;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.fontSize       = 10;

            var dropdown = new DropdownField(
                new System.Collections.Generic.List<string>(choices),
                get());
            dropdown.style.flexGrow = 1;
            dropdown.RegisterValueChangedCallback(e => set(dropdown.index));

            row.Add(lbl);
            row.Add(dropdown);
            return row;
        }

        /// <summary>CSVパス行（ラベル + パス表示 + Browse + Clear）</summary>
        private VisualElement CsvPathRow(string label, Func<string> get, Action<string> set, string ext)
        {
            var container = new VisualElement();
            container.style.marginBottom = 3;

            var lbl = new Label(label);
            lbl.style.fontSize = 9;
            container.Add(lbl);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            var pathLbl = new Label(string.IsNullOrEmpty(get()) ? TM("CSVNotSet") : Path.GetFileName(get()));
            pathLbl.style.flexGrow   = 1;
            pathLbl.style.fontSize   = 9;
            pathLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            pathLbl.style.overflow   = Overflow.Hidden;

            var browseBtn = new Button(() =>
            {
                string dir = string.IsNullOrEmpty(get()) ? Application.dataPath : Path.GetDirectoryName(get());
                string path = PLEditorBridge.I.OpenFilePanel(label, dir, ext);
                if (!string.IsNullOrEmpty(path))
                {
                    set(path);
                    pathLbl.text = Path.GetFileName(path);
                }
            }) { text = TM("Browse") };
            browseBtn.style.width       = 52;
            browseBtn.style.marginLeft  = 2;
            browseBtn.style.fontSize    = 9;

            var clearBtn = new Button(() =>
            {
                set("");
                pathLbl.text = TM("CSVNotSet");
            }) { text = TM("Clear") };
            clearBtn.style.width      = 36;
            clearBtn.style.marginLeft = 2;
            clearBtn.style.fontSize   = 9;

            row.Add(pathLbl);
            row.Add(browseBtn);
            row.Add(clearBtn);
            container.Add(row);
            return container;
        }

        private static void AddSmallBtn(VisualElement parent, string text, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.flexGrow     = 1;
            b.style.marginRight  = 2;
            b.style.height       = 18;
            b.style.fontSize     = 9;
            parent.Add(b);
        }
    }
}
