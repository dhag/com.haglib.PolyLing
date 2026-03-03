// HumanoidMappingPanel.cs
// Humanoidボーンマッピングパネル (UIToolkit)
// CSVからマッピング取り込み、プレビュー、適用

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// <summary>
    /// Humanoidボーンマッピングパネル（UIToolkit版）
    /// </summary>
    public class HumanoidMappingPanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/HumanoidMappingPanel/HumanoidMappingPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/HumanoidMappingPanel/HumanoidMappingPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/HumanoidMappingPanel/HumanoidMappingPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/HumanoidMappingPanel/HumanoidMappingPanel.uss";

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Humanoid Bone Mapping", ["ja"] = "Humanoidボーンマッピング" },
            ["NoContext"] = new() { ["en"] = "toolContext not set. Open from Poly_Ling window.", ["ja"] = "toolContext未設定。Poly_Lingウィンドウから開いてください。" },
            ["NoModel"] = new() { ["en"] = "No model loaded", ["ja"] = "モデルが読み込まれていません" },
            ["NoBones"] = new() { ["en"] = "No bones in model", ["ja"] = "モデルにボーンがありません" },
            ["MappingSource"] = new() { ["en"] = "Mapping Source", ["ja"] = "マッピングソース" },
            ["CSVFile"] = new() { ["en"] = "CSV File", ["ja"] = "CSVファイル" },
            ["DragDropCSV"] = new() { ["en"] = "Drag & Drop CSV here, or click [...] to browse", ["ja"] = "CSVをここにドロップ、または[...]で選択" },
            ["AutoMapPMX"] = new() { ["en"] = "Auto Map (PMX Standard)", ["ja"] = "自動マッピング（PMX標準）" },
            ["LoadCSV"] = new() { ["en"] = "Load from CSV", ["ja"] = "CSVから読み込み" },
            ["Preview"] = new() { ["en"] = "Mapping Preview", ["ja"] = "マッピングプレビュー" },
            ["MappedCount"] = new() { ["en"] = "Mapped: {0} / {1}", ["ja"] = "マッピング済: {0} / {1}" },
            ["RequiredMissing"] = new() { ["en"] = "Required bones missing: {0}", ["ja"] = "必須ボーン不足: {0}" },
            ["CanCreateAvatar"] = new() { ["en"] = "✓ Can create Humanoid Avatar", ["ja"] = "✓ Humanoid Avatar作成可能" },
            ["CannotCreateAvatar"] = new() { ["en"] = "✗ Cannot create Avatar (missing required bones)", ["ja"] = "✗ Avatar作成不可（必須ボーン不足）" },
            ["Apply"] = new() { ["en"] = "Apply to Model", ["ja"] = "モデルに適用" },
            ["Clear"] = new() { ["en"] = "Clear Mapping", ["ja"] = "マッピングをクリア" },
            ["ApplySuccess"] = new() { ["en"] = "Mapping applied: {0} bones", ["ja"] = "マッピング適用: {0}ボーン" },
            ["Bones"] = new() { ["en"] = "Bones", ["ja"] = "ボーン" },
            ["Required"] = new() { ["en"] = "*", ["ja"] = "*" },
            ["NoMappingLoaded"] = new() { ["en"] = "(No mapping loaded)", ["ja"] = "(マッピング未読み込み)" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        private ModelContext Model => _toolContext?.Model;

        // ================================================================
        // 状態
        // ================================================================

        private string _csvFilePath = "";
        private List<string> _csvLines;
        private HumanoidBoneMapping _previewMapping;

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel;
        private ScrollView _mainContent;
        private Label _sourceHeader, _csvFileLabel, _csvHintLabel;
        private VisualElement _csvDropZone;
        private TextField _csvPathField;
        private Button _btnBrowse, _btnAutoMap, _btnLoadCsv;
        private Foldout _foldoutPreview;
        private Label _previewEmptyLabel;
        private VisualElement _previewContent;
        private Label _mappedCountLabel;
        private VisualElement _missingSection;
        private Label _missingHeaderLabel;
        private VisualElement _missingListContainer;
        private Label _avatarStatusLabel;
        private VisualElement _mappingDetailContainer;
        private Button _btnApply, _btnClear;
        private Foldout _foldoutBoneList;
        private VisualElement _boneListContainer;

        // ================================================================
        // Open
        // ================================================================

        public static HumanoidMappingPanel Open(ToolContext ctx)
        {
            var window = GetWindow<HumanoidMappingPanel>();
            window.titleContent = new GUIContent(T("WindowTitle"));
            window.minSize = new Vector2(350, 400);
            window.SetContext(ctx);
            window.Show();
            return window;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();
        private void Cleanup() { }

        // ================================================================
        // IToolContextReceiver
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            _toolContext = ctx;
            _previewMapping = null;
            _csvLines = null;
            Refresh();
        }

        // ================================================================
        // CreateGUI
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)
                          ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPathAssets);
            if (visualTree != null)
                visualTree.CloneTree(root);
            else
            {
                root.Add(new Label($"UXML not found: {UxmlPath}"));
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath)
                          ?? AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPathAssets);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            BindUI(root);
            Refresh();
        }

        // ================================================================
        // UI バインド
        // ================================================================

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _mainContent = root.Q<ScrollView>("main-content");
            _sourceHeader = root.Q<Label>("source-header");
            _csvFileLabel = root.Q<Label>("csv-file-label");
            _csvHintLabel = root.Q<Label>("csv-hint-label");
            _csvDropZone = root.Q<VisualElement>("csv-drop-zone");
            _csvPathField = root.Q<TextField>("csv-path-field");
            _btnBrowse = root.Q<Button>("btn-browse");
            _btnAutoMap = root.Q<Button>("btn-auto-map");
            _btnLoadCsv = root.Q<Button>("btn-load-csv");
            _foldoutPreview = root.Q<Foldout>("foldout-preview");
            _previewEmptyLabel = root.Q<Label>("preview-empty-label");
            _previewContent = root.Q<VisualElement>("preview-content");
            _mappedCountLabel = root.Q<Label>("mapped-count-label");
            _missingSection = root.Q<VisualElement>("missing-section");
            _missingHeaderLabel = root.Q<Label>("missing-header-label");
            _missingListContainer = root.Q<VisualElement>("missing-list-container");
            _avatarStatusLabel = root.Q<Label>("avatar-status-label");
            _mappingDetailContainer = root.Q<VisualElement>("mapping-detail-container");
            _btnApply = root.Q<Button>("btn-apply");
            _btnClear = root.Q<Button>("btn-clear");
            _foldoutBoneList = root.Q<Foldout>("foldout-bone-list");
            _boneListContainer = root.Q<VisualElement>("bone-list-container");

            // ボタンイベント
            _btnBrowse.clicked += OnBrowseCSV;
            _btnAutoMap.clicked += OnAutoMap;
            _btnLoadCsv.clicked += OnLoadCSV;
            _btnApply.clicked += OnApply;
            _btnClear.clicked += OnClear;

            // ドラッグ＆ドロップ
            _csvDropZone.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            _csvDropZone.RegisterCallback<DragPerformEvent>(OnDragPerform);
            _csvDropZone.RegisterCallback<DragLeaveEvent>(e =>
                _csvDropZone.RemoveFromClassList("hm-csv-row--drag-hover"));
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
                _csvDropZone.AddToClassList("hm-csv-row--drag-hover");
                e.StopPropagation();
            }
        }

        private void OnDragPerform(DragPerformEvent e)
        {
            _csvDropZone.RemoveFromClassList("hm-csv-row--drag-hover");

            if (DragAndDrop.paths.Length > 0)
            {
                string path = DragAndDrop.paths[0];
                if (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    DragAndDrop.AcceptDrag();
                    _csvFilePath = path;
                    _csvPathField.SetValueWithoutNotify(_csvFilePath);
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

            if (_toolContext == null)
            {
                ShowWarning(T("NoContext"));
                return;
            }

            if (Model == null)
            {
                ShowWarning(T("NoModel"));
                return;
            }

            var boneNames = GetBoneNames();
            if (boneNames.Count == 0)
            {
                ShowWarning(T("NoBones"));
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display = DisplayStyle.Flex;

            // ローカライズ
            _sourceHeader.text = T("MappingSource");
            _csvFileLabel.text = T("CSVFile");
            _btnAutoMap.text = T("AutoMapPMX");
            _btnLoadCsv.text = T("LoadCSV");
            _foldoutPreview.text = T("Preview");
            _btnApply.text = T("Apply");
            _btnClear.text = T("Clear");

            // CSVヒント
            _csvHintLabel.text = string.IsNullOrEmpty(_csvFilePath) ? T("DragDropCSV") : "";
            _csvHintLabel.style.display = string.IsNullOrEmpty(_csvFilePath)
                ? DisplayStyle.Flex : DisplayStyle.None;

            // LoadCSVボタン有効/無効
            _btnLoadCsv.SetEnabled(!string.IsNullOrEmpty(_csvFilePath));

            // プレビュー
            RefreshPreview(boneNames);

            // 適用ボタン状態
            _btnApply.SetEnabled(_previewMapping != null && !_previewMapping.IsEmpty);
            _btnClear.SetEnabled(Model?.HumanoidMapping != null && !Model.HumanoidMapping.IsEmpty);

            // ボーンリスト
            RefreshBoneList(boneNames);
        }

        private void ShowWarning(string message)
        {
            _warningLabel.text = message;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display = DisplayStyle.None;
        }

        // ================================================================
        // プレビュー
        // ================================================================

        private void RefreshPreview(List<string> boneNames)
        {
            if (_previewMapping == null || _previewMapping.IsEmpty)
            {
                _previewEmptyLabel.text = T("NoMappingLoaded");
                _previewEmptyLabel.style.display = DisplayStyle.Flex;
                _previewContent.style.display = DisplayStyle.None;
                return;
            }

            _previewEmptyLabel.style.display = DisplayStyle.None;
            _previewContent.style.display = DisplayStyle.Flex;

            // マッピング数
            int totalHumanoid = HumanoidBoneMapping.AllHumanoidBones.Length;
            int mappedCount = _previewMapping.Count;
            _mappedCountLabel.text = T("MappedCount", mappedCount, totalHumanoid);

            // 必須ボーン不足
            var missing = _previewMapping.GetMissingRequiredBones();
            if (missing.Count > 0)
            {
                _missingSection.style.display = DisplayStyle.Flex;
                _missingHeaderLabel.text = T("RequiredMissing", missing.Count);
                _missingListContainer.Clear();
                foreach (var bone in missing)
                {
                    var entry = new Label($"• {bone}");
                    entry.AddToClassList("hm-missing-entry");
                    _missingListContainer.Add(entry);
                }
            }
            else
            {
                _missingSection.style.display = DisplayStyle.None;
            }

            // Avatar作成可否
            if (_previewMapping.CanCreateAvatar)
            {
                _avatarStatusLabel.text = T("CanCreateAvatar");
                _avatarStatusLabel.RemoveFromClassList("hm-status-label--ng");
                _avatarStatusLabel.AddToClassList("hm-status-label--ok");
            }
            else
            {
                _avatarStatusLabel.text = T("CannotCreateAvatar");
                _avatarStatusLabel.RemoveFromClassList("hm-status-label--ok");
                _avatarStatusLabel.AddToClassList("hm-status-label--ng");
            }

            // マッピング詳細
            _mappingDetailContainer.Clear();
            foreach (var humanoidBone in HumanoidBoneMapping.AllHumanoidBones)
            {
                int boneIndex = _previewMapping.Get(humanoidBone);
                if (boneIndex < 0) continue;

                string boneName = "(invalid index)";
                if (boneIndex < Model.MeshContextList.Count)
                    boneName = Model.MeshContextList[boneIndex]?.Name ?? boneName;

                bool isRequired = HumanoidBoneMapping.RequiredBones.Contains(humanoidBone);
                string label = isRequired ? $"{humanoidBone} {T("Required")}" : humanoidBone;

                var row = new VisualElement();
                row.AddToClassList("hm-mapping-row");

                var keyLabel = new Label(label);
                keyLabel.AddToClassList("hm-mapping-key");
                row.Add(keyLabel);

                var valueLabel = new Label(boneName);
                valueLabel.AddToClassList("hm-mapping-value");
                row.Add(valueLabel);

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
                var entry = new Label($"[{i}] {boneNames[i]}");
                entry.AddToClassList("hm-bone-entry");
                _boneListContainer.Add(entry);
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

        private Dictionary<string, int> GetBoneNameToIndexMap()
        {
            var map = new Dictionary<string, int>();
            if (Model?.MeshContextList == null) return map;

            for (int i = 0; i < Model.MeshContextList.Count; i++)
            {
                var ctx = Model.MeshContextList[i];
                if (ctx?.Type == MeshType.Bone && !string.IsNullOrEmpty(ctx.Name))
                {
                    if (!map.ContainsKey(ctx.Name))
                        map[ctx.Name] = i;
                }
            }
            return map;
        }

        // ================================================================
        // ボタンハンドラ
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
                _csvPathField.SetValueWithoutNotify(_csvFilePath);
                LoadCSVMapping(GetBoneNames());
            }
        }

        private void OnAutoMap()
        {
            var boneNames = GetBoneNames();
            _previewMapping = new HumanoidBoneMapping();
            int count = _previewMapping.AutoMapFromEmbeddedCSV(boneNames);
            Debug.Log($"[HumanoidMappingPanel] Auto-mapped {count} bones from embedded mapping");
            Refresh();
        }

        private void OnLoadCSV()
        {
            LoadCSVMapping(GetBoneNames());
        }

        private void OnApply()
        {
            if (_previewMapping == null || Model == null) return;

            var before = Model.HumanoidMapping.Clone();
            Model.HumanoidMapping.CopyFrom(_previewMapping);
            var after = Model.HumanoidMapping.Clone();

            var undo = _toolContext?.UndoController;
            if (undo != null)
            {
                var record = new HumanoidMappingChangedRecord(before, after, "Apply Humanoid Mapping");
                undo.MeshListStack.Record(record, "Apply Humanoid Mapping");
            }

            Model.IsDirty = true;
            Debug.Log($"[HumanoidMappingPanel] {T("ApplySuccess", _previewMapping.Count)}");
            Refresh();
        }

        private void OnClear()
        {
            if (Model == null) return;

            var before = Model.HumanoidMapping.Clone();
            Model.HumanoidMapping.ClearAll();
            var after = Model.HumanoidMapping.Clone();

            var undo = _toolContext?.UndoController;
            if (undo != null)
            {
                var record = new HumanoidMappingChangedRecord(before, after, "Clear Humanoid Mapping");
                undo.MeshListStack.Record(record, "Clear Humanoid Mapping");
            }

            Model.IsDirty = true;
            _previewMapping = null;
            Debug.Log("[HumanoidMappingPanel] Mapping cleared");
            Refresh();
        }

        // ================================================================
        // CSV読み込み
        // ================================================================

        private void LoadCSVMapping(List<string> boneNames)
        {
            if (string.IsNullOrEmpty(_csvFilePath) || !File.Exists(_csvFilePath))
            {
                Debug.LogWarning("[HumanoidMappingPanel] CSV file not found");
                return;
            }

            try
            {
                _csvLines = new List<string>(File.ReadAllLines(_csvFilePath, Encoding.UTF8));
                Debug.Log($"[HumanoidMappingPanel] Loaded CSV: {_csvLines.Count} lines");

                _previewMapping = new HumanoidBoneMapping();
                int count = _previewMapping.LoadFromCSV(_csvLines, boneNames);
                Debug.Log($"[HumanoidMappingPanel] Preview mapping created: {count} bones");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HumanoidMappingPanel] Failed to load CSV: {ex.Message}");
                _csvLines = null;
                _previewMapping = null;
            }

            Refresh();
        }
    }
}
