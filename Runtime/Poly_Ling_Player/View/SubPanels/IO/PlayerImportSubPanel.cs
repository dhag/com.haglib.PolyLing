// PlayerImportSubPanel.cs
// プレイビュー右ペイン用 PMX / MQO / OBJ インポート設定パネル（UIToolkit）。
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
using Poly_Ling.OBJ;
using Poly_Ling.Localization;
using Poly_Ling.EditorBridge;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 右ペインに表示する PMX / MQO / OBJ インポート設定 UI。
    /// Build(parent) で UIToolkit 要素を生成し、
    /// OnImport コールバックでインポート実行を Viewer に委譲する。
    /// </summary>
    public class PlayerImportSubPanel
    {
        // ================================================================
        // モード
        // ================================================================

        public enum Mode { PMX, MQO, OBJ }

        private Mode _mode;

        // ================================================================
        // 読込後オプション
        // ================================================================

        /// <summary>
        /// インポータ本体の設定ではなく、読み込みが終わったあとに
        /// 追加で流す処理の指定。呼び出し側（Viewer）がこれを見て
        /// ApplyHumanoidMappingCommand / ApplyObjectOriginsCommand を送る。
        ///
        /// 検証パネルから OnImportXxx を直接呼ぶ経路は null を渡すため、
        /// パネルのチェック状態が自動検証に混ざることはない。
        /// </summary>
        public class PostOptions
        {
            /// <summary>アバター用ヒューマンマッピングを名前から自動割当するか。</summary>
            public bool HumanoidAutoMap;

            /// <summary>読込後に原点CSVを適用するか。</summary>
            public bool ApplyOriginCsv;

            /// <summary>適用する原点CSVのパス。</summary>
            public string OriginCsvPath;

            /// <summary>原点CSVの回転列（rotX,rotY,rotZ）も適用対象にするか。</summary>
            public bool OriginCsvIncludeRotation;
        }

        // ================================================================
        // 設定
        // ================================================================

        private PMXImportSettings _pmxSettings = PMXImportSettings.CreateDefault();
        private MQOImportSettings _mqoSettings = MQOImportSettings.CreateDefault();
        private ObjImportSettings _objSettings = ObjImportSettings.CreateDefault();

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>
        /// PMX Import ボタン押下時に呼ばれる。
        /// 引数は (filePath, settings のコピー, 読込後オプション)。
        /// Viewer がコマンド生成・エンキューを行う。
        /// </summary>
        public Action<string, PMXImportSettings, PostOptions> OnImportPmx;

        /// <summary>
        /// MQO Import ボタン押下時に呼ばれる。
        /// 引数は (filePath, settings のコピー, 読込後オプション)。
        /// </summary>
        public Action<string, MQOImportSettings, PostOptions> OnImportMqo;

        /// <summary>
        /// OBJ Import ボタン押下時に呼ばれる。
        /// 引数は (filePath, settings のコピー, 読込後オプション)。
        /// </summary>
        public Action<string, ObjImportSettings, PostOptions> OnImportObj;

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

        // ── 読込後オプションの保持値（パネル再構築をまたいで残す）──
        //
        // チェックボックスはどちらも既定オフ。
        // 原点CSVのパスだけは「設定に保存されているもの」を既定にするため、
        // 姿勢タブと同じ履歴キー(OriginCsvRecentKey)から初期値を取る。
        private bool   _humanoidAutoMap     = false;
        private bool   _applyOriginCsv      = false;
        private string _originCsvPath       = null;   // null = 履歴から未取得
        private bool   _originCsvIncludeRot = false;

        /// <summary>
        /// 原点CSVの最近使ったパス。「描画オブジェクトの姿勢」タブの
        /// 書出・読込（PlayerBoneEditorSubPanel）と同じキーを共有する。
        /// </summary>
        private const string OriginCsvRecentKey = "BoneEditor.OriginCsv.Path";

        /// <summary>原点CSVのパス。未取得なら履歴から読み出して確定する。</summary>
        private string OriginCsvPath
        {
            get
            {
                if (_originCsvPath == null)
                    _originCsvPath = RecentPaths.Get(OriginCsvRecentKey) ?? "";
                return _originCsvPath;
            }
            set => _originCsvPath = value ?? "";
        }

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
            _pathField.RegisterValueChangedCallback(e => RecentPaths.Set(ImportPathKey(), e.newValue));

            var browseBtn = new Button(OnBrowse) { text = "..." };
            browseBtn.style.width = 28;
            browseBtn.style.marginRight = 2;

            fileRow.Add(browseBtn);
            fileRow.Add(_pathField);
            fileSection.Add(fileRow);

            // ── Import ボタン（パスフィールド直下）──
            var importBtn = new Button(OnBrowse) { text = "開く" };
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
                _panelNameLabel.text = ModeName(mode) + "インポータ";

            if (_pathField != null)
            {
                if (!string.IsNullOrEmpty(filePath))
                    _pathField.value = filePath;
                else
                    _pathField.SetValueWithoutNotify(RecentPaths.Get(ImportPathKey()));
            }

            RebuildSettings();
        }

        /// <summary>モード名（表示・保存キー・拡張子の共通元）</summary>
        private static string ModeName(Mode mode)
        {
            switch (mode)
            {
                case Mode.PMX: return "PMX";
                case Mode.OBJ: return "OBJ";
                default:       return "MQO";
            }
        }

        /// <summary>インポートパスの保存キー（モード別）</summary>
        private string ImportPathKey()
            => "Import." + ModeName(_mode) + ".Path";

        // ================================================================
        // ファイルブラウズ
        // ================================================================

        // 「開く」と [...] の共通処理。パス欄の値をダイアログの初期値にする。
        private void OnBrowse()
        {
            string name  = ModeName(_mode);
            string ext   = name.ToLowerInvariant();
            string title = $"Select {name} File";

            string path = PlayerIoUiKit.AskLoadPath(title, ImportPathKey(), _pathField.value, ext);
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
                OnImportPmx?.Invoke(path, ClonePmxSettings(), BuildPostOptions());
            else if (_mode == Mode.OBJ)
                OnImportObj?.Invoke(path, _objSettings.Clone(), BuildPostOptions());
            else
                OnImportMqo?.Invoke(path, CloneMqoSettings(), BuildPostOptions());
        }

        /// <summary>
        /// 現在のチェック状態から読込後オプションを作る。
        /// 原点CSVは MQO / OBJ だけの機能なので PMX では常に無効にする。
        /// </summary>
        private PostOptions BuildPostOptions()
        {
            bool originCsv = _applyOriginCsv && _mode != Mode.PMX;
            return new PostOptions
            {
                HumanoidAutoMap          = _humanoidAutoMap,
                ApplyOriginCsv           = originCsv,
                OriginCsvPath            = originCsv ? OriginCsvPath : "",
                OriginCsvIncludeRotation = _originCsvIncludeRot,
            };
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
            else if (_mode == Mode.OBJ)
                BuildObjSettings(_settingsContainer);
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
            parent.Add(FlagToggle("剛体", () => _pmxSettings.ShouldImportBodies,
                v => SetPmxTarget(PMXImportTarget.Bodies, v)));
            parent.Add(FlagToggle("Joint", () => _pmxSettings.ShouldImportJoints,
                v => SetPmxTarget(PMXImportTarget.Joints, v)));

            parent.Add(Separator());

            // 座標変換
            parent.Add(SectionLabel(TP("Coordinate")));
            parent.Add(FloatRow(TP("Scale"),    () => _pmxSettings.Scale,    v => _pmxSettings.Scale    = v));
            parent.Add(ToggleRow(TP("FlipXAxis"), () => _pmxSettings.FlipX,  v => _pmxSettings.FlipX    = v));
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

            parent.Add(Separator());

            // 読込後オプション（インポータ本体の設定ではない）
            parent.Add(SectionLabel("読込後オプション"));
            parent.Add(HumanoidAutoMapToggle());
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
            parent.Add(ToggleRow(TM("FlipXAxis"), () => _mqoSettings.FlipX,  v => _mqoSettings.FlipX    = v));
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
            parent.Add(ToggleRow(TM("SetMeshHierarchyParent"), () => _mqoSettings.SetMeshHierarchyParent, v => _mqoSettings.SetMeshHierarchyParent = v));
            parent.Add(ToggleRow(TM("AutoDetectMirrorBranchRoot"), () => _mqoSettings.AutoDetectMirrorBranchRoot, v => _mqoSettings.AutoDetectMirrorBranchRoot = v));
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
                new[] { "FaceNormal", "Smooth", "Unity", "SmoothFacet" },
                () => (int)_mqoSettings.NormalMode,
                v  => _mqoSettings.NormalMode = (MQO.NormalMode)v));
            parent.Add(SliderRow(TM("SmoothingAngle"), 0f, 180f, () => _mqoSettings.SmoothingAngle, v => _mqoSettings.SmoothingAngle = v));
            parent.Add(ToggleRow(TM("UseMqoFacet"), () => _mqoSettings.UseMqoFacet, v => _mqoSettings.UseMqoFacet = v));

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
            parent.Add(CsvPathRow(TM("BoneWeightCSV"), () => _mqoSettings.BoneWeightCSVPath, v => _mqoSettings.BoneWeightCSVPath = v, "csv", "Import.MQO.BoneWeightCSV"));
            parent.Add(CsvPathRow(TM("BoneCSV"),       () => _mqoSettings.BoneCSVPath,       v => _mqoSettings.BoneCSVPath       = v, "csv", "Import.MQO.BoneCSV"));

            parent.Add(Separator());

            // 読込後オプション（インポータ本体の設定ではない）
            parent.Add(SectionLabel("読込後オプション"));
            parent.Add(HumanoidAutoMapToggle());
            parent.Add(OriginCsvOptionBlock());
        }

        private MQOImportSettings CloneMqoSettings()
        {
            var s = new MQOImportSettings();
            s.CopyFrom(_mqoSettings);
            return s;
        }

        // ================================================================
        // 読込後オプション UI
        // ================================================================

        /// <summary>
        /// 「ヒューマンマッピングAuto」チェック（既定オフ）。
        /// オンのとき、読込後にボーン名から Humanoid 割当を作って適用する。
        /// </summary>
        private VisualElement HumanoidAutoMapToggle()
        {
            var t = new Toggle("ヒューマンマッピングAuto") { value = _humanoidAutoMap };
            t.tooltip = "読み込みが終わったあと、ボーン名からアバター用の\n"
                      + "ヒューマンマッピングを自動で作って割り当てる。\n"
                      + "一致する名前が無ければ何もしない。";
            t.style.marginBottom = 2;
            t.RegisterValueChangedCallback(e => _humanoidAutoMap = e.newValue);
            return t;
        }

        /// <summary>
        /// 「オブジェクトのローカル姿勢（原点）」チェック（既定オフ）と、
        /// オンのときだけ出す原点CSVの読み込み設定。
        ///
        /// 設定の中身は「描画オブジェクトの姿勢」タブの原点CSV読込と同じで、
        /// CSVパスの履歴キー（OriginCsvRecentKey）も回転列の扱いもそろえてある。
        /// ここで選んだCSVを、読み込みが終わったあとに適用する。
        /// </summary>
        private VisualElement OriginCsvOptionBlock()
        {
            var container = new VisualElement();

            var detail = new VisualElement();
            detail.style.marginLeft    = 12;
            detail.style.marginBottom  = 2;
            detail.style.display       = _applyOriginCsv ? DisplayStyle.Flex : DisplayStyle.None;

            var t = new Toggle("オブジェクトのローカル姿勢（原点）") { value = _applyOriginCsv };
            t.tooltip = "読み込みが終わったあと、指定した原点CSVを名前一致で適用する\n"
                      + "（原点だけ移動・子は動かさない）。\n"
                      + "CSV に載っていないオブジェクトと、モデルに無い名前はどちらも無視する";
            t.style.marginBottom = 2;
            t.RegisterValueChangedCallback(e =>
            {
                _applyOriginCsv     = e.newValue;
                detail.style.display = e.newValue ? DisplayStyle.Flex : DisplayStyle.None;
            });
            container.Add(t);

            detail.Add(OriginCsvPathRow());

            var rotToggle = new Toggle("回転(°)も対象") { value = _originCsvIncludeRot };
            rotToggle.tooltip =
                "読込: 回転列がある行だけ回転も適用する（列が無い行・オフのときは位置だけ）";
            rotToggle.style.marginTop = 2;
            rotToggle.RegisterValueChangedCallback(e => _originCsvIncludeRot = e.newValue);
            detail.Add(rotToggle);

            container.Add(detail);
            return container;
        }

        /// <summary>
        /// 原点CSVのパス行（ラベル + ファイル名 + 参照 + クリア）。
        ///
        /// 汎用の CsvPathRow を使わないのは「クリア」の扱いが違うため。
        /// 履歴キーを「描画オブジェクトの姿勢」タブと共有しているので、
        /// CsvPathRow のように RecentPaths からキーごと消すと、姿勢タブ側で
        /// 覚えているパスまで巻き添えで消える。ここでは選択の解除だけを行い、
        /// 保存されている設定には触らない。
        /// </summary>
        private VisualElement OriginCsvPathRow()
        {
            var container = new VisualElement();
            container.style.marginBottom = 3;

            var lbl = new Label("原点CSV");
            lbl.style.fontSize = 9;
            container.Add(lbl);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            var pathLbl = new Label(PathLabelText(OriginCsvPath));
            pathLbl.style.flexGrow       = 1;
            pathLbl.style.fontSize       = 9;
            pathLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            pathLbl.style.overflow       = Overflow.Hidden;

            var browseBtn = new Button(() =>
            {
                // 確定したパスは AskLoadPath が履歴へ書き戻す。
                string path = PlayerIoUiKit.AskLoadPath(
                    "原点CSVの読み込み", OriginCsvRecentKey, OriginCsvPath, "csv");
                if (!string.IsNullOrEmpty(path))
                {
                    OriginCsvPath = path;
                    pathLbl.text  = PathLabelText(path);
                }
            }) { text = TM("Browse") };
            browseBtn.style.width      = 52;
            browseBtn.style.marginLeft = 2;
            browseBtn.style.fontSize   = 9;

            var clearBtn = new Button(() =>
            {
                OriginCsvPath = "";
                pathLbl.text  = PathLabelText("");
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

        private static string PathLabelText(string path)
            => string.IsNullOrEmpty(path) ? TM("CSVNotSet") : Path.GetFileName(path);

        // ================================================================
        // ローカライズヘルパー
        // ================================================================

        private static string TP(string key) => L.GetFrom(PMXImportTexts.Texts, key);
        private static string TM(string key) => L.GetFrom(MQOImportTexts.Texts, key);

        // ================================================================
        // UIパーツ ヘルパー
        // ================================================================

        // ────────────────────────────────────────────────────────
        // OBJ 設定
        //
        // OBJ は右手系・+Y 上でメタセコイアと同じ置き方をするため、
        // Unity へは X のみ反転で揃う（既定 Flip X = ON）。
        // UV 原点は OBJ / Unity とも左下なので V 反転は既定 OFF。
        // ────────────────────────────────────────────────────────

        private void BuildObjSettings(VisualElement parent)
        {
            parent.Add(SectionLabel("座標変換"));
            parent.Add(FloatRow("Scale",      () => _objSettings.Scale,    v => _objSettings.Scale    = v));
            parent.Add(ToggleRow("Flip X",    () => _objSettings.FlipX,    v => _objSettings.FlipX    = v));
            parent.Add(ToggleRow("Flip Z",    () => _objSettings.FlipZ,    v => _objSettings.FlipZ    = v));
            parent.Add(ToggleRow("Flip UV V", () => _objSettings.FlipUV_V, v => _objSettings.FlipUV_V = v));
            parent.Add(ToggleRow("3D表示オートスケール", () => _autoScale, v => _autoScale = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("分割"));

            var groupingNames = new System.Collections.Generic.List<string>
            {
                "オブジェクト (o)", "グループ (g)", "マテリアル", "分割しない"
            };
            var groupingField = new DropdownField(groupingNames, (int)_objSettings.Grouping);
            groupingField.tooltip = "OBJ をどの単位で1オブジェクトにするか。"
                                  + "o / g が無いファイルでは自動でひとまとめになる。";
            groupingField.style.marginBottom = 2;
            groupingField.RegisterValueChangedCallback(
                e => _objSettings.Grouping = (ObjGroupingMode)groupingField.index);
            parent.Add(groupingField);

            parent.Add(ToggleRow("空のオブジェクトをスキップ",
                () => _objSettings.SkipEmptyObjects, v => _objSettings.SkipEmptyObjects = v));
            parent.Add(ToggleRow("折れ線(l)を補助線として読む",
                () => _objSettings.ImportLines, v => _objSettings.ImportLines = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("法線"));
            parent.Add(ToggleRow("ファイルの法線(vn)を使う",
                () => _objSettings.UseFileNormals, v => _objSettings.UseFileNormals = v));
            parent.Add(FloatRow("スムージング角",
                () => _objSettings.SmoothingAngle, v => _objSettings.SmoothingAngle = v));

            parent.Add(Separator());
            parent.Add(SectionLabel("マテリアル"));
            parent.Add(ToggleRow("MTL を読み込む",
                () => _objSettings.ImportMaterials, v => _objSettings.ImportMaterials = v));
            parent.Add(ToggleRow("テクスチャを読み込む",
                () => _objSettings.ImportTextures, v => _objSettings.ImportTextures = v));

            parent.Add(Separator());

            // 読込後オプション（インポータ本体の設定ではない）
            parent.Add(SectionLabel("読込後オプション"));
            parent.Add(HumanoidAutoMapToggle());
            parent.Add(OriginCsvOptionBlock());
        }

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
        private VisualElement CsvPathRow(string label, Func<string> get, Action<string> set, string ext, string recentKey = null)
        {
            var container = new VisualElement();
            container.style.marginBottom = 3;

            // 保存済みパスの復元（設定オブジェクトが未設定の場合のみ RecentPaths から seed）
            if (!string.IsNullOrEmpty(recentKey) && string.IsNullOrEmpty(get()))
            {
                string savedCsv = RecentPaths.Get(recentKey);
                if (!string.IsNullOrEmpty(savedCsv)) set(savedCsv);
            }

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
                string path = PlayerIoUiKit.AskLoadPath(
                    label, string.IsNullOrEmpty(recentKey) ? "Import.Csv." + label : recentKey,
                    get(), ext);
                if (!string.IsNullOrEmpty(path))
                {
                    set(path);
                    if (!string.IsNullOrEmpty(recentKey)) RecentPaths.Set(recentKey, path);
                    pathLbl.text = Path.GetFileName(path);
                }
            }) { text = TM("Browse") };
            browseBtn.style.width       = 52;
            browseBtn.style.marginLeft  = 2;
            browseBtn.style.fontSize    = 9;

            var clearBtn = new Button(() =>
            {
                set("");
                if (!string.IsNullOrEmpty(recentKey)) RecentPaths.Set(recentKey, "");
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
