// HumanoidMappingPanelV2.cs
// Humanoidボーンマッピングパネル V2（コード構築 UIToolkit）
// PanelContext（通知）+ ToolContext（実処理）ハイブリッド

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Localization;
using Poly_Ling.Model;
using Poly_Ling.Records;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public class HumanoidMappingPanelV2 : EditorWindow
    {
        // ================================================================
        // ローカライズ辞書（V1 と同一）
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"]         = new() { ["en"] = "Humanoid Bone Mapping",                          ["ja"] = "Humanoidボーンマッピング" },
            ["NoContext"]           = new() { ["en"] = "toolContext not set. Open from Poly_Ling window.",["ja"] = "toolContext未設定。Poly_Lingウィンドウから開いてください。" },
            ["NoModel"]             = new() { ["en"] = "No model loaded",                                 ["ja"] = "モデルが読み込まれていません" },
            ["NoBones"]             = new() { ["en"] = "No bones in model",                               ["ja"] = "モデルにボーンがありません" },
            ["MappingSource"]       = new() { ["en"] = "Mapping Source",                                  ["ja"] = "マッピングソース" },
            ["CSVFile"]             = new() { ["en"] = "CSV File",                                        ["ja"] = "CSVファイル" },
            ["DragDropCSV"]         = new() { ["en"] = "Drag & Drop CSV here, or click [...] to browse",  ["ja"] = "CSVをここにドロップ、または[...]で選択" },
            ["AutoMapPMX"]          = new() { ["en"] = "Auto Map (PMX Standard)",                        ["ja"] = "自動マッピング（PMX標準）" },
            ["LoadCSV"]             = new() { ["en"] = "Load from CSV",                                   ["ja"] = "CSVから読み込み" },
            ["Preview"]             = new() { ["en"] = "Mapping Preview",                                 ["ja"] = "マッピングプレビュー" },
            ["MappedCount"]         = new() { ["en"] = "Mapped: {0} / {1}",                               ["ja"] = "マッピング済: {0} / {1}" },
            ["RequiredMissing"]     = new() { ["en"] = "Required bones missing: {0}",                     ["ja"] = "必須ボーン不足: {0}" },
            ["CanCreateAvatar"]     = new() { ["en"] = "✓ Can create Humanoid Avatar",                   ["ja"] = "✓ Humanoid Avatar作成可能" },
            ["CannotCreateAvatar"]  = new() { ["en"] = "✗ Cannot create Avatar (missing required bones)", ["ja"] = "✗ Avatar作成不可（必須ボーン不足）" },
            ["Apply"]               = new() { ["en"] = "Apply to Model",                                  ["ja"] = "モデルに適用" },
            ["Clear"]               = new() { ["en"] = "Clear Mapping",                                   ["ja"] = "マッピングをクリア" },
            ["ApplySuccess"]        = new() { ["en"] = "Mapping applied: {0} bones",                      ["ja"] = "マッピング適用: {0}ボーン" },
            ["Bones"]               = new() { ["en"] = "Bones",                                           ["ja"] = "ボーン" },
            ["Required"]            = new() { ["en"] = "*",                                               ["ja"] = "*" },
            ["NoMappingLoaded"]     = new() { ["en"] = "(No mapping loaded)",                             ["ja"] = "(マッピング未読み込み)" },
        };

        private static string T(string key)                       => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _panelCtx;
        private ToolContext  _toolCtx;

        private ModelContext Model => _toolCtx?.Model;

        // ================================================================
        // 状態
        // ================================================================

        private string               _csvFilePath   = "";
        private HumanoidBoneMapping  _previewMapping;

        // ================================================================
        // UI 要素
        // ================================================================

        private Label         _warningLabel;
        private ScrollView    _mainScroll;

        // CSVソース
        private TextField     _csvPathField;
        private Label         _csvHintLabel;
        private VisualElement _csvDropZone;
        private Button        _btnBrowse;
        private Button        _btnAutoMap;
        private Button        _btnLoadCsv;

        // プレビュー
        private Foldout       _foldoutPreview;
        private Label         _previewEmptyLabel;
        private VisualElement _previewContent;
        private Label         _mappedCountLabel;
        private VisualElement _missingSection;
        private Label         _missingHeaderLabel;
        private VisualElement _missingListContainer;
        private Label         _avatarStatusLabel;
        private VisualElement _mappingDetailContainer;
        private Button        _btnApply;
        private Button        _btnClear;

        // ボーンリスト
        private Foldout       _foldoutBoneList;
        private VisualElement _boneListContainer;

        // ================================================================
        // Open
        // ================================================================

        public static HumanoidMappingPanelV2 Open(PanelContext panelCtx, ToolContext toolCtx)
        {
            var w = GetWindow<HumanoidMappingPanelV2>();
            w.titleContent = new GUIContent(T("WindowTitle"));
            w.minSize = new Vector2(350, 400);
            w.SetContexts(panelCtx, toolCtx);
            w.Show();
            return w;
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        private void SetContexts(PanelContext panelCtx, ToolContext toolCtx)
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;

            _panelCtx       = panelCtx;
            _toolCtx        = toolCtx;
            _previewMapping = null;

            if (_panelCtx != null) _panelCtx.OnViewChanged += OnViewChanged;

            Refresh();
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            if (_panelCtx != null)
            {
                _panelCtx.OnViewChanged -= OnViewChanged;
                _panelCtx.OnViewChanged += OnViewChanged;
            }
        }

        private void OnDisable()
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void OnDestroy()
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void CreateGUI()
        {
            BuildUI(rootVisualElement);
            Refresh();
        }

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (kind == ChangeKind.ModelSwitch || kind == ChangeKind.ListStructure)
            {
                _previewMapping = null;
                Refresh();
            }
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft   = 6;
            root.style.paddingRight  = 6;
            root.style.paddingTop    = 6;
            root.style.paddingBottom = 6;

            // 警告
            _warningLabel = new Label();
            _warningLabel.style.display     = DisplayStyle.None;
            _warningLabel.style.color       = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            // スクロールビュー
            _mainScroll = new ScrollView();
            _mainScroll.style.flexGrow  = 1;
            _mainScroll.style.display   = DisplayStyle.None;
            root.Add(_mainScroll);
            var sv = _mainScroll.contentContainer;

            // ── マッピングソース ──
            sv.Add(MakeSectionLabel(T("MappingSource")));

            // CSV ドロップゾーン + パスフィールド
            _csvDropZone = new VisualElement();
            _csvDropZone.style.borderTopWidth    = 1;
            _csvDropZone.style.borderBottomWidth = 1;
            _csvDropZone.style.borderLeftWidth   = 1;
            _csvDropZone.style.borderRightWidth  = 1;
            _csvDropZone.style.borderTopColor    =
            _csvDropZone.style.borderBottomColor =
            _csvDropZone.style.borderLeftColor   =
            _csvDropZone.style.borderRightColor  = new StyleColor(new Color(0.4f, 0.4f, 0.4f));
            _csvDropZone.style.paddingTop         = 4;
            _csvDropZone.style.paddingBottom      = 4;
            _csvDropZone.style.paddingLeft        = 4;
            _csvDropZone.style.paddingRight       = 4;
            _csvDropZone.style.marginBottom       = 4;

            var csvRow = new VisualElement();
            csvRow.style.flexDirection = FlexDirection.Row;
            _csvPathField = new TextField();
            _csvPathField.style.flexGrow = 1;
            _csvPathField.isReadOnly     = true;
            _btnBrowse = new Button(OnBrowseCSV) { text = "..." };
            _btnBrowse.style.width = 28;
            csvRow.Add(new Label(T("CSVFile") + ": ") { style = { alignSelf = Align.Center, color = new StyleColor(Color.gray) } });
            csvRow.Add(_csvPathField);
            csvRow.Add(_btnBrowse);
            _csvDropZone.Add(csvRow);

            _csvHintLabel = new Label();
            _csvHintLabel.style.fontSize = 10;
            _csvHintLabel.style.color    = new StyleColor(Color.gray);
            _csvDropZone.Add(_csvHintLabel);

            sv.Add(_csvDropZone);

            // ドラッグ＆ドロップ
            _csvDropZone.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            _csvDropZone.RegisterCallback<DragPerformEvent>(OnDragPerform);
            _csvDropZone.RegisterCallback<DragLeaveEvent>(_ =>
                _csvDropZone.style.backgroundColor = StyleKeyword.Null);

            // ボタン行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginBottom  = 8;
            _btnAutoMap  = new Button(OnAutoMap)  { text = T("AutoMapPMX") };
            _btnAutoMap.style.flexGrow  = 1;
            _btnAutoMap.style.marginRight = 4;
            _btnLoadCsv  = new Button(OnLoadCSV)  { text = T("LoadCSV") };
            _btnLoadCsv.style.flexGrow  = 1;
            btnRow.Add(_btnAutoMap);
            btnRow.Add(_btnLoadCsv);
            sv.Add(btnRow);

            sv.Add(MakeSep());

            // ── プレビュー ──
            _foldoutPreview = new Foldout { text = T("Preview"), value = true };
            sv.Add(_foldoutPreview);

            _previewEmptyLabel = new Label(T("NoMappingLoaded"));
            _previewEmptyLabel.style.color = new StyleColor(Color.gray);
            _foldoutPreview.Add(_previewEmptyLabel);

            _previewContent = new VisualElement();
            _previewContent.style.display = DisplayStyle.None;
            _foldoutPreview.Add(_previewContent);

            _mappedCountLabel = new Label();
            _mappedCountLabel.style.marginBottom = 4;
            _previewContent.Add(_mappedCountLabel);

            _missingSection = new VisualElement();
            _missingSection.style.display = DisplayStyle.None;
            _previewContent.Add(_missingSection);
            _missingHeaderLabel = new Label();
            _missingHeaderLabel.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
            _missingSection.Add(_missingHeaderLabel);
            _missingListContainer = new VisualElement();
            _missingListContainer.style.paddingLeft = 12;
            _missingSection.Add(_missingListContainer);

            _avatarStatusLabel = new Label();
            _avatarStatusLabel.style.marginTop    = 4;
            _avatarStatusLabel.style.marginBottom = 4;
            _previewContent.Add(_avatarStatusLabel);

            _mappingDetailContainer = new VisualElement();
            _previewContent.Add(_mappingDetailContainer);

            // Apply / Clear ボタン
            var applyRow = new VisualElement();
            applyRow.style.flexDirection = FlexDirection.Row;
            applyRow.style.marginTop     = 6;
            applyRow.style.marginBottom  = 6;
            _btnApply = new Button(OnApply) { text = T("Apply") };
            _btnApply.style.flexGrow   = 1;
            _btnApply.style.marginRight = 4;
            _btnClear = new Button(OnClear) { text = T("Clear") };
            _btnClear.style.flexGrow   = 1;
            applyRow.Add(_btnApply);
            applyRow.Add(_btnClear);
            sv.Add(applyRow);

            sv.Add(MakeSep());

            // ── ボーンリスト ──
            _foldoutBoneList = new Foldout { text = T("Bones"), value = false };
            sv.Add(_foldoutBoneList);
            _boneListContainer = new VisualElement();
            _foldoutBoneList.Add(_boneListContainer);
        }

        private static Label MakeSectionLabel(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement MakeSep()
        {
            var sep = new VisualElement();
            sep.style.height          = 1;
            sep.style.backgroundColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            sep.style.marginTop       = 4;
            sep.style.marginBottom    = 4;
            return sep;
        }

        // ================================================================
        // ドラッグ＆ドロップ
        // ================================================================

        private void OnDragUpdated(DragUpdatedEvent e)
        {
            if (DragAndDrop.paths.Length > 0 &&
                DragAndDrop.paths[0].EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                _csvDropZone.style.backgroundColor = new StyleColor(new Color(0.3f, 0.5f, 0.3f, 0.3f));
                e.StopPropagation();
            }
        }

        private void OnDragPerform(DragPerformEvent e)
        {
            _csvDropZone.style.backgroundColor = StyleKeyword.Null;
            if (DragAndDrop.paths.Length > 0)
            {
                string path = DragAndDrop.paths[0];
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    DragAndDrop.AcceptDrag();
                    _csvFilePath = path;
                    _csvPathField?.SetValueWithoutNotify(_csvFilePath);
                    LoadCSVMapping(GetBoneNames());
                    e.StopPropagation();
                }
            }
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return;

            if (_toolCtx == null)
            {
                ShowWarning(T("NoContext")); return;
            }
            if (Model == null)
            {
                ShowWarning(T("NoModel")); return;
            }
            var boneNames = GetBoneNames();
            if (boneNames.Count == 0)
            {
                ShowWarning(T("NoBones")); return;
            }

            _warningLabel.style.display  = DisplayStyle.None;
            _mainScroll.style.display    = DisplayStyle.Flex;

            // ヒント
            _csvHintLabel.text          = string.IsNullOrEmpty(_csvFilePath) ? T("DragDropCSV") : "";
            _csvHintLabel.style.display = string.IsNullOrEmpty(_csvFilePath) ? DisplayStyle.Flex : DisplayStyle.None;
            _btnLoadCsv.SetEnabled(!string.IsNullOrEmpty(_csvFilePath));

            RefreshPreview(boneNames);

            _btnApply.SetEnabled(_previewMapping != null && !_previewMapping.IsEmpty);
            _btnClear.SetEnabled(Model?.HumanoidMapping != null && !Model.HumanoidMapping.IsEmpty);

            RefreshBoneList(boneNames);
        }

        private void ShowWarning(string message)
        {
            if (_warningLabel == null) return;
            _warningLabel.text           = message;
            _warningLabel.style.display  = DisplayStyle.Flex;
            if (_mainScroll != null) _mainScroll.style.display = DisplayStyle.None;
        }

        // ================================================================
        // プレビュー（V1 と同一ロジック）
        // ================================================================

        private void RefreshPreview(List<string> boneNames)
        {
            if (_previewMapping == null || _previewMapping.IsEmpty)
            {
                _previewEmptyLabel.text          = T("NoMappingLoaded");
                _previewEmptyLabel.style.display = DisplayStyle.Flex;
                _previewContent.style.display    = DisplayStyle.None;
                return;
            }

            _previewEmptyLabel.style.display = DisplayStyle.None;
            _previewContent.style.display    = DisplayStyle.Flex;

            int totalHumanoid = HumanoidBoneMapping.AllHumanoidBones.Length;
            int mappedCount   = _previewMapping.Count;
            _mappedCountLabel.text = T("MappedCount", mappedCount, totalHumanoid);

            var missing = _previewMapping.GetMissingRequiredBones();
            if (missing.Count > 0)
            {
                _missingSection.style.display = DisplayStyle.Flex;
                _missingHeaderLabel.text      = T("RequiredMissing", missing.Count);
                _missingListContainer.Clear();
                foreach (var bone in missing)
                {
                    var lbl = new Label($"• {bone}");
                    lbl.style.fontSize = 11;
                    _missingListContainer.Add(lbl);
                }
            }
            else
            {
                _missingSection.style.display = DisplayStyle.None;
            }

            if (_previewMapping.CanCreateAvatar)
            {
                _avatarStatusLabel.text  = T("CanCreateAvatar");
                _avatarStatusLabel.style.color = new StyleColor(new Color(0.3f, 0.9f, 0.3f));
            }
            else
            {
                _avatarStatusLabel.text  = T("CannotCreateAvatar");
                _avatarStatusLabel.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
            }

            _mappingDetailContainer.Clear();
            foreach (var humanoidBone in HumanoidBoneMapping.AllHumanoidBones)
            {
                int boneIndex = _previewMapping.Get(humanoidBone);
                if (boneIndex < 0) continue;

                string boneName = "(invalid index)";
                if (boneIndex < Model.MeshContextList.Count)
                    boneName = Model.MeshContextList[boneIndex]?.Name ?? boneName;

                bool   isRequired = HumanoidBoneMapping.RequiredBones.Contains(humanoidBone);
                string label      = isRequired ? $"{humanoidBone} {T("Required")}" : humanoidBone;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.paddingTop    = 1;
                row.style.paddingBottom = 1;

                var keyLbl = new Label(label);
                keyLbl.style.width    = 160;
                keyLbl.style.color    = isRequired
                    ? new StyleColor(new Color(1f, 0.9f, 0.5f))
                    : StyleKeyword.Null;
                var valLbl = new Label(boneName);
                valLbl.style.flexGrow = 1;

                row.Add(keyLbl);
                row.Add(valLbl);
                _mappingDetailContainer.Add(row);
            }
        }

        // ================================================================
        // ボーンリスト
        // ================================================================

        private void RefreshBoneList(List<string> boneNames)
        {
            _foldoutBoneList.text = $"{T("Bones")} ({boneNames.Count})";
            _boneListContainer.Clear();
            for (int i = 0; i < boneNames.Count; i++)
            {
                var lbl = new Label($"[{i}] {boneNames[i]}");
                lbl.style.fontSize = 11;
                _boneListContainer.Add(lbl);
            }
        }

        // ================================================================
        // ボーン名取得
        // ================================================================

        private List<string> GetBoneNames()
        {
            var names = new List<string>();
            if (Model?.MeshContextList == null) return names;
            for (int i = 0; i < Model.MeshContextList.Count; i++)
            {
                var ctx = Model.MeshContextList[i];
                if (ctx?.Type == MeshType.Bone)
                    names.Add(ctx.Name ?? $"Bone_{i}");
            }
            return names;
        }

        // ================================================================
        // ボタンハンドラ（V1 と同一ロジック）
        // ================================================================

        private void OnBrowseCSV()
        {
            string dir = string.IsNullOrEmpty(_csvFilePath)
                ? Application.dataPath
                : Path.GetDirectoryName(_csvFilePath);
            string path = EditorUtility.OpenFilePanel("Select Bone Mapping CSV", dir, "csv");
            if (!string.IsNullOrEmpty(path))
            {
                _csvFilePath = path;
                _csvPathField?.SetValueWithoutNotify(_csvFilePath);
                LoadCSVMapping(GetBoneNames());
            }
        }

        private void OnAutoMap()
        {
            var boneNames = GetBoneNames();
            _previewMapping = new HumanoidBoneMapping();
            int count = _previewMapping.AutoMapFromEmbeddedCSV(boneNames);
            Debug.Log($"[HumanoidMappingPanelV2] Auto-mapped {count} bones");
            Refresh();
        }

        private void OnLoadCSV() => LoadCSVMapping(GetBoneNames());

        private void OnApply()
        {
            if (_previewMapping == null || Model == null) return;
            var before = Model.HumanoidMapping.Clone();
            Model.HumanoidMapping.CopyFrom(_previewMapping);
            var after = Model.HumanoidMapping.Clone();

            var undo = _toolCtx?.UndoController;
            if (undo != null)
            {
                var record = new HumanoidMappingChangedRecord(before, after, "Apply Humanoid Mapping");
                undo.MeshListStack.Record(record, "Apply Humanoid Mapping");
            }

            Model.IsDirty = true;
            Debug.Log($"[HumanoidMappingPanelV2] {T("ApplySuccess", _previewMapping.Count)}");
            Refresh();
        }

        private void OnClear()
        {
            if (Model == null) return;
            var before = Model.HumanoidMapping.Clone();
            Model.HumanoidMapping.ClearAll();
            var after = Model.HumanoidMapping.Clone();

            var undo = _toolCtx?.UndoController;
            if (undo != null)
            {
                var record = new HumanoidMappingChangedRecord(before, after, "Clear Humanoid Mapping");
                undo.MeshListStack.Record(record, "Clear Humanoid Mapping");
            }

            Model.IsDirty    = true;
            _previewMapping  = null;
            Debug.Log("[HumanoidMappingPanelV2] Mapping cleared");
            Refresh();
        }

        // ================================================================
        // CSV 読み込み（V1 と同一）
        // ================================================================

        private void LoadCSVMapping(List<string> boneNames)
        {
            if (string.IsNullOrEmpty(_csvFilePath) || !File.Exists(_csvFilePath))
            {
                Debug.LogWarning("[HumanoidMappingPanelV2] CSV file not found");
                return;
            }
            try
            {
                var csvLines = new List<string>(File.ReadAllLines(_csvFilePath, Encoding.UTF8));
                Debug.Log($"[HumanoidMappingPanelV2] Loaded CSV: {csvLines.Count} lines");
                _previewMapping = new HumanoidBoneMapping();
                int count = _previewMapping.LoadFromCSV(csvLines, boneNames);
                Debug.Log($"[HumanoidMappingPanelV2] Preview mapping: {count} bones");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HumanoidMappingPanelV2] Failed to load CSV: {ex.Message}");
                _previewMapping = null;
            }
            Refresh();
        }
    }
}
