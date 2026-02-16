// Assets/Editor/Poly_Ling_/ToolPanels/MorphPanel.cs
// モーフエクスプレッション管理・プレビューパネル (UIToolkit)

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.CSV;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools.Panels
{
    /// <summary>
    /// モーフパネル設定
    /// </summary>
    [Serializable]
    public class MorphPanelSettings : IToolSettings
    {
        public int SelectedMorphExpressionIndex = -1;

        [Range(0f, 1f)]
        public float PreviewWeight = 0f;

        public IToolSettings Clone()
        {
            return new MorphPanelSettings
            {
                SelectedMorphExpressionIndex = this.SelectedMorphExpressionIndex,
                PreviewWeight = this.PreviewWeight
            };
        }

        public bool IsDifferentFrom(IToolSettings other)
        {
            if (other is not MorphPanelSettings o) return true;
            return SelectedMorphExpressionIndex != o.SelectedMorphExpressionIndex ||
                   !Mathf.Approximately(PreviewWeight, o.PreviewWeight);
        }

        public void CopyFrom(IToolSettings other)
        {
            if (other is not MorphPanelSettings o) return;
            SelectedMorphExpressionIndex = o.SelectedMorphExpressionIndex;
            PreviewWeight = o.PreviewWeight;
        }
    }


    /// <summary>
    /// モーフエクスプレッション管理・プレビューパネル (UIToolkit)
    /// </summary>
    public class MorphPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel
        // ================================================================

        public override string Name => "MorphPanel";
        public override string Title => "Morph";

        private MorphPanelSettings _settings = new MorphPanelSettings();
        public override IToolSettings Settings => _settings;

        public override string GetLocalizedTitle() => "モーフエディタ";

        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MorphPanel/MorphPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MorphPanel/MorphPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/MorphPanel/MorphPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/MorphPanel/MorphPanel.uss";


 
        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel, _statusLabel, _previewInfo, _setTypeLabel;
        private ListView _setListView, _entryListView;
        private VisualElement _setDetail, _previewSection;
        private TextField _setName, _setNameEn;
        private VisualElement _setPanelContainer;
        private PopupField<int> _setPanelPopup;
        private Slider _previewWeight;

        // ================================================================
        // データソース
        // ================================================================

        private List<(int index, string name, string info)> _setListData = new();
        private List<(int entryIdx, int meshIndex, string meshName, float weight)> _entryData = new();
        private int _selectedSetIndex = -1;
        private MorphExpression _entryEditSnapshot = null;

        // ================================================================
        // プレビュー状態
        // ================================================================

        private bool _isPreviewActive = false;
        private Dictionary<int, Vector3[]> _previewBackups = new();
        private List<(int morphIndex, int baseIndex, float entryWeight)> _previewPairs = new();
        private int _previewMorphExpressionIndex = -1;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<MorphPanel>();
            panel.titleContent = new GUIContent("モーフエディタ");
            panel.minSize = new Vector2(300, 400);
            panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // CreateGUI (UIToolkit)
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
        }

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _statusLabel = root.Q<Label>("status-label");
            _previewInfo = root.Q<Label>("preview-info");
            _setTypeLabel = root.Q<Label>("set-type-label");
            _setDetail = root.Q<VisualElement>("set-detail");
            _previewSection = root.Q<VisualElement>("preview-section");
            _setName = root.Q<TextField>("set-name");
            _setNameEn = root.Q<TextField>("set-name-en");
            _setPanelContainer = root.Q<VisualElement>("set-panel-container");
            _previewWeight = root.Q<Slider>("preview-weight");

            // セット ListView
            _setListView = root.Q<ListView>("set-listview");
            if (_setListView != null)
            {
                _setListView.makeItem = SetMakeItem;
                _setListView.bindItem = SetBindItem;
                _setListView.fixedItemHeight = 20;
                _setListView.itemsSource = _setListData;
                _setListView.selectionChanged += OnSetSelectionChanged;
            }

            // エントリ ListView
            _entryListView = root.Q<ListView>("entry-listview");
            if (_entryListView != null)
            {
                _entryListView.makeItem = EntryMakeItem;
                _entryListView.bindItem = EntryBindItem;
                _entryListView.fixedItemHeight = 22;
                _entryListView.itemsSource = _entryData;
            }

            // ボタン
            root.Q<Button>("btn-csv-import")?.RegisterCallback<ClickEvent>(_ => OnCsvImport());
            root.Q<Button>("btn-csv-export")?.RegisterCallback<ClickEvent>(_ => OnCsvExport());
            root.Q<Button>("btn-delete-set")?.RegisterCallback<ClickEvent>(_ => OnDeleteSet());
            root.Q<Button>("btn-reset")?.RegisterCallback<ClickEvent>(_ => OnResetPreview());
            root.Q<Button>("btn-end-preview")?.RegisterCallback<ClickEvent>(_ => OnEndPreview());

            // 詳細変更
            _setName?.RegisterCallback<FocusOutEvent>(_ => OnSetDetailChanged());
            _setNameEn?.RegisterCallback<FocusOutEvent>(_ => OnSetDetailChanged());

            // プレビューウェイト
            _previewWeight?.RegisterValueChangedCallback(OnPreviewWeightChanged);

            // 初期非表示
            if (_setDetail != null) _setDetail.style.display = DisplayStyle.None;
            if (_previewSection != null) _previewSection.style.display = DisplayStyle.None;

            RefreshAll();
        }

        // ================================================================
        // ListView makeItem / bindItem
        // ================================================================

        private VisualElement SetMakeItem()
        {
            var row = new VisualElement();
            row.AddToClassList("mp-set-row");
            var name = new Label();
            name.AddToClassList("mp-set-row-name");
            row.Add(name);
            var info = new Label();
            info.AddToClassList("mp-set-row-info");
            row.Add(info);
            return row;
        }

        private void SetBindItem(VisualElement el, int index)
        {
            if (index < 0 || index >= _setListData.Count) return;
            var d = _setListData[index];
            var name = el.Q<Label>(className: "mp-set-row-name");
            var info = el.Q<Label>(className: "mp-set-row-info");
            if (name != null) name.text = d.name;
            if (info != null) info.text = d.info;
        }

        private VisualElement EntryMakeItem()
        {
            var row = new VisualElement();
            row.AddToClassList("mp-entry-row");
            var name = new Label();
            name.AddToClassList("mp-entry-name");
            row.Add(name);
            var slider = new Slider(0f, 1f);
            slider.AddToClassList("mp-entry-weight-slider");
            row.Add(slider);
            var wl = new Label();
            wl.AddToClassList("mp-entry-weight-label");
            row.Add(wl);
            return row;
        }

        private void EntryBindItem(VisualElement el, int index)
        {
            if (index < 0 || index >= _entryData.Count) return;
            var d = _entryData[index];

            var name = el.Q<Label>(className: "mp-entry-name");
            var slider = el.Q<Slider>();
            var wl = el.Q<Label>(className: "mp-entry-weight-label");

            if (name != null) name.text = d.meshName;
            if (wl != null) wl.text = d.weight.ToString("F2");

            if (slider != null)
            {
                int eIdx = d.entryIdx;
                slider.SetValueWithoutNotify(d.weight);
                slider.RegisterValueChangedCallback(evt =>
                {
                    OnEntryWeightChanged(eIdx, evt.newValue);
                    if (wl != null) wl.text = evt.newValue.ToString("F2");
                });
                slider.RegisterCallback<PointerDownEvent>(_ => OnEntryWeightStart());
                slider.RegisterCallback<PointerUpEvent>(_ => OnEntryWeightEnd());
            }
        }

        // ================================================================
        // 更新
        // ================================================================

        private void RefreshAll()
        {
            if (_warningLabel == null) return;

            if (_context == null)
            {
                _warningLabel.text = "toolContextが設定されていません";
                _warningLabel.style.display = DisplayStyle.Flex;
                return;
            }

            var model = Model;
            if (model == null)
            {
                _warningLabel.text = "モデルがありません";
                _warningLabel.style.display = DisplayStyle.Flex;
                return;
            }

            if (!model.HasMorphExpressions)
            {
                _warningLabel.text = "モーフエクスプレッションがありません";
                _warningLabel.style.display = DisplayStyle.Flex;
                if (_setDetail != null) _setDetail.style.display = DisplayStyle.None;
                if (_previewSection != null) _previewSection.style.display = DisplayStyle.None;
            }
            else
            {
                _warningLabel.style.display = DisplayStyle.None;
            }

            RefreshSetList();
        }

        private void RefreshSetList()
        {
            _setListData.Clear();
            var model = Model;
            if (model != null)
            {
                for (int i = 0; i < model.MorphExpressionCount; i++)
                {
                    var set = model.MorphExpressions[i];
                    _setListData.Add((i, set.Name, $"({set.Type}, {set.MeshCount}件)"));
                }
            }
            _setListView?.RefreshItems();

            // 選択復元
            if (_selectedSetIndex >= 0 && _selectedSetIndex < _setListData.Count)
            {
                _setListView?.SetSelection(_selectedSetIndex);
                RefreshSetDetail(_selectedSetIndex);
            }
            else
            {
                _selectedSetIndex = -1;
                if (_setDetail != null) _setDetail.style.display = DisplayStyle.None;
                if (_previewSection != null) _previewSection.style.display = DisplayStyle.None;
            }
        }

        // ================================================================
        // セット選択
        // ================================================================

        private void OnSetSelectionChanged(IEnumerable<object> items)
        {
            int idx = _setListView?.selectedIndex ?? -1;
            if (idx == _selectedSetIndex) return;

            EndPreview();
            _settings.PreviewWeight = 0f;
            _previewWeight?.SetValueWithoutNotify(0f);

            _selectedSetIndex = idx;
            _settings.SelectedMorphExpressionIndex = idx;

            if (idx >= 0 && idx < (Model?.MorphExpressionCount ?? 0))
                RefreshSetDetail(idx);
            else
            {
                if (_setDetail != null) _setDetail.style.display = DisplayStyle.None;
                if (_previewSection != null) _previewSection.style.display = DisplayStyle.None;
            }
        }

        // ================================================================
        // セット詳細
        // ================================================================

        private void RefreshSetDetail(int setIndex)
        {
            var model = Model;
            if (_setDetail == null || model == null) return;
            if (setIndex < 0 || setIndex >= model.MorphExpressionCount)
            {
                _setDetail.style.display = DisplayStyle.None;
                if (_previewSection != null) _previewSection.style.display = DisplayStyle.None;
                return;
            }

            _setDetail.style.display = DisplayStyle.Flex;
            var set = model.MorphExpressions[setIndex];

            _setName?.SetValueWithoutNotify(set.Name);
            _setNameEn?.SetValueWithoutNotify(set.NameEnglish);
            if (_setTypeLabel != null) _setTypeLabel.text = set.Type.ToString();

            // パネルPopup
            RebuildPanelPopup(set.Panel);

            // エントリ
            _entryData.Clear();
            for (int i = 0; i < set.MeshEntries.Count; i++)
            {
                var entry = set.MeshEntries[i];
                string meshName = $"[{entry.MeshIndex}]";
                if (entry.MeshIndex >= 0 && entry.MeshIndex < model.MeshContextCount)
                {
                    var mc = model.GetMeshContext(entry.MeshIndex);
                    if (mc != null) meshName = $"[{entry.MeshIndex}] {mc.Name}";
                }
                _entryData.Add((i, entry.MeshIndex, meshName, entry.Weight));
            }
            _entryListView?.RefreshItems();

            // プレビューセクション表示
            RefreshPreviewSection(model, set, setIndex);
        }

        private void RebuildPanelPopup(int currentPanel)
        {
            if (_setPanelContainer == null) return;
            _setPanelContainer.Clear();

            var indices = new List<int> { 0, 1, 2, 3 };
            var names = new Dictionary<int, string>
            {
                [0] = "眉", [1] = "目", [2] = "口", [3] = "その他"
            };

            _setPanelPopup = new PopupField<int>(indices, currentPanel,
                v => names.TryGetValue(v, out var s) ? s : v.ToString(),
                v => names.TryGetValue(v, out var s) ? s : v.ToString());
            _setPanelPopup.AddToClassList("mp-popup");
            _setPanelPopup.style.flexGrow = 1;
            _setPanelPopup.RegisterValueChangedCallback(_ => OnSetDetailChanged());
            _setPanelContainer.Add(_setPanelPopup);
        }

        private void OnSetDetailChanged()
        {
            var model = Model;
            if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount) return;
            var set = model.MorphExpressions[_selectedSetIndex];

            string newName = _setName?.value?.Trim() ?? set.Name;
            string newNameEn = _setNameEn?.value?.Trim() ?? set.NameEnglish;
            int newPanel = _setPanelPopup?.value ?? set.Panel;

            if (newName == set.Name && newNameEn == set.NameEnglish && newPanel == set.Panel)
                return;

            var record = new MorphExpressionEditRecord
            {
                SetIndex = _selectedSetIndex,
                OldSnapshot = set.Clone(),
            };

            set.Name = newName;
            set.NameEnglish = newNameEn;
            set.Panel = newPanel;

            record.NewSnapshot = set.Clone();
            RecordUndo(record, $"モーフエクスプレッション属性変更: {newName}");

            // リスト表示更新
            if (_selectedSetIndex < _setListData.Count)
                _setListData[_selectedSetIndex] = (_selectedSetIndex, set.Name, $"({set.Type}, {set.MeshCount}件)");
            _setListView?.RefreshItems();
        }

        // ================================================================
        // エントリウェイト
        // ================================================================

        private void OnEntryWeightStart()
        {
            var model = Model;
            if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount) return;
            _entryEditSnapshot = model.MorphExpressions[_selectedSetIndex].Clone();
        }

        private void OnEntryWeightChanged(int entryIdx, float newWeight)
        {
            var model = Model;
            if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount) return;
            var set = model.MorphExpressions[_selectedSetIndex];
            if (entryIdx < 0 || entryIdx >= set.MeshEntries.Count) return;

            var entry = set.MeshEntries[entryIdx];
            entry.Weight = newWeight;
            set.MeshEntries[entryIdx] = entry;

            if (entryIdx < _entryData.Count)
            {
                var d = _entryData[entryIdx];
                _entryData[entryIdx] = (d.entryIdx, d.meshIndex, d.meshName, newWeight);
            }

            // ライブプレビュー更新
            if (_isPreviewActive)
                ApplyBatchPreview(model, _settings.PreviewWeight);
        }

        private void OnEntryWeightEnd()
        {
            if (_entryEditSnapshot == null) return;
            var model = Model;
            if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount)
            {
                _entryEditSnapshot = null;
                return;
            }

            var set = model.MorphExpressions[_selectedSetIndex];
            bool changed = false;
            if (_entryEditSnapshot.MeshEntries.Count == set.MeshEntries.Count)
            {
                for (int i = 0; i < set.MeshEntries.Count; i++)
                    if (Mathf.Abs(_entryEditSnapshot.MeshEntries[i].Weight - set.MeshEntries[i].Weight) > 0.0001f)
                    { changed = true; break; }
            }
            else changed = true;

            if (changed)
            {
                var record = new MorphExpressionEditRecord
                {
                    SetIndex = _selectedSetIndex,
                    OldSnapshot = _entryEditSnapshot,
                    NewSnapshot = set.Clone(),
                };
                RecordUndo(record, $"モーフエクスプレッションウェイト変更: {set.Name}");
            }
            _entryEditSnapshot = null;
        }

        // ================================================================
        // セット削除
        // ================================================================

        private void OnDeleteSet()
        {
            var model = Model;
            if (model == null || _selectedSetIndex < 0 || _selectedSetIndex >= model.MorphExpressionCount) return;

            var set = model.MorphExpressions[_selectedSetIndex];
            if (!EditorUtility.DisplayDialog("削除確認", $"モーフエクスプレッション '{set.Name}' を削除しますか？", "削除", "キャンセル"))
                return;

            EndPreview();

            var record = new MorphExpressionChangeRecord
            {
                RemovedExpression = set.Clone(),
                RemovedIndex = _selectedSetIndex,
            };
            RecordUndo(record, $"モーフエクスプレッション削除: {set.Name}");

            model.MorphExpressions.RemoveAt(_selectedSetIndex);
            _selectedSetIndex = -1;
            _settings.SelectedMorphExpressionIndex = -1;
            StatusLog($"モーフエクスプレッション '{set.Name}' を削除");
            RefreshAll();
        }

        // ================================================================
        // プレビュー
        // ================================================================

        private void RefreshPreviewSection(ModelContext model, MorphExpression set, int setIndex)
        {
            if (_previewSection == null) return;

            var pairs = BuildMorphBasePairs(model, set);
            if (pairs.Count == 0)
            {
                _previewSection.style.display = DisplayStyle.None;
                return;
            }

            _previewSection.style.display = DisplayStyle.Flex;
            if (_previewInfo != null)
                _previewInfo.text = $"対象: {pairs.Count}ペア";

            // プレビュー開始
            if (!_isPreviewActive || _previewMorphExpressionIndex != setIndex)
                StartBatchPreview(model, pairs, setIndex);
        }

        private void OnPreviewWeightChanged(ChangeEvent<float> evt)
        {
            var model = Model;
            if (model == null) return;

            _settings.PreviewWeight = evt.newValue;
            ApplyBatchPreview(model, evt.newValue);
        }

        private void OnResetPreview()
        {
            _settings.PreviewWeight = 0f;
            _previewWeight?.SetValueWithoutNotify(0f);
            var model = Model;
            if (model != null)
                ApplyBatchPreview(model, 0f);
        }

        private void OnEndPreview()
        {
            EndPreview();
            _settings.PreviewWeight = 0f;
            _previewWeight?.SetValueWithoutNotify(0f);
            _selectedSetIndex = -1;
            _settings.SelectedMorphExpressionIndex = -1;
            RefreshAll();
        }

        // ================================================================
        // バッチプレビュー
        // ================================================================

        private void StartBatchPreview(ModelContext model,
            List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx, float weight)> pairs,
            int setIndex)
        {
            EndPreview();

            _previewBackups.Clear();
            _previewPairs.Clear();

            foreach (var (morphIndex, baseIndex, morphCtx, baseCtx, weight) in pairs)
            {
                if (baseCtx?.MeshObject == null) continue;

                if (!_previewBackups.ContainsKey(baseIndex))
                {
                    var baseMesh = baseCtx.MeshObject;
                    var backup = new Vector3[baseMesh.VertexCount];
                    for (int i = 0; i < baseMesh.VertexCount; i++)
                        backup[i] = baseMesh.Vertices[i].Position;
                    _previewBackups[baseIndex] = backup;
                }
                _previewPairs.Add((morphIndex, baseIndex, weight));
            }

            _previewMorphExpressionIndex = setIndex;
            _isPreviewActive = true;
        }

        private void ApplyBatchPreview(ModelContext model, float weight)
        {
            if (!_isPreviewActive || _previewBackups.Count == 0) return;

            // 復元
            foreach (var (baseIndex, backup) in _previewBackups)
            {
                var baseCtx = model.GetMeshContext(baseIndex);
                if (baseCtx?.MeshObject == null) continue;
                var baseMesh = baseCtx.MeshObject;
                int count = Mathf.Min(backup.Length, baseMesh.VertexCount);
                for (int i = 0; i < count; i++)
                    baseMesh.Vertices[i].Position = backup[i];
            }

            // 適用
            foreach (var (morphIndex, baseIndex, entryWeight) in _previewPairs)
            {
                var morphCtx = model.GetMeshContext(morphIndex);
                var baseCtx = model.GetMeshContext(baseIndex);
                if (morphCtx?.MeshObject == null || baseCtx?.MeshObject == null) continue;

                var baseMesh = baseCtx.MeshObject;
                foreach (var (vertexIndex, offset) in morphCtx.GetMorphOffsets())
                    if (vertexIndex < baseMesh.VertexCount)
                        baseMesh.Vertices[vertexIndex].Position += offset * (entryWeight * weight);
            }

            // GPU更新
            foreach (var baseIndex in _previewBackups.Keys)
            {
                var baseCtx = model.GetMeshContext(baseIndex);
                if (baseCtx != null)
                    _context?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
            }
            _context?.Repaint?.Invoke();
        }

        private void EndPreview()
        {
            if (!_isPreviewActive || _previewBackups.Count == 0)
            {
                _isPreviewActive = false;
                _previewBackups.Clear();
                _previewPairs.Clear();
                _previewMorphExpressionIndex = -1;
                return;
            }

            var model = Model;
            if (model != null)
            {
                foreach (var (baseIndex, backup) in _previewBackups)
                {
                    var baseCtx = model.GetMeshContext(baseIndex);
                    if (baseCtx?.MeshObject == null) continue;
                    var baseMesh = baseCtx.MeshObject;
                    int count = Mathf.Min(backup.Length, baseMesh.VertexCount);
                    for (int i = 0; i < count; i++)
                        baseMesh.Vertices[i].Position = backup[i];
                    _context?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
                }
            }

            _previewBackups.Clear();
            _previewPairs.Clear();
            _previewMorphExpressionIndex = -1;
            _isPreviewActive = false;
            _context?.Repaint?.Invoke();
        }

        // ================================================================
        // モーフ-ベースペア構築
        // ================================================================

        private List<(int morphIndex, int baseIndex, MeshContext morphCtx, MeshContext baseCtx, float weight)>
            BuildMorphBasePairs(ModelContext model, MorphExpression set)
        {
            var pairs = new List<(int, int, MeshContext, MeshContext, float)>();

            for (int i = 0; i < set.MeshEntries.Count; i++)
            {
                var entry = set.MeshEntries[i];
                var morphCtx = model.GetMeshContext(entry.MeshIndex);
                if (morphCtx == null || !morphCtx.IsMorph) continue;

                int baseIndex = FindBaseMeshIndex(model, morphCtx);
                var baseCtx = baseIndex >= 0 ? model.GetMeshContext(baseIndex) : null;

                if (baseCtx?.MeshObject != null)
                    pairs.Add((entry.MeshIndex, baseIndex, morphCtx, baseCtx, entry.Weight));
            }
            return pairs;
        }

        private int FindBaseMeshIndex(ModelContext model, MeshContext morphCtx)
        {
            if (morphCtx == null) return -1;

            // MorphParentIndex優先
            if (morphCtx.MorphParentIndex >= 0)
                return morphCtx.MorphParentIndex;

            // フォールバック：名前規則 "baseName_morphName"
            string morphName = morphCtx.MorphName;
            string meshName = morphCtx.Name;

            if (!string.IsNullOrEmpty(morphName) && meshName.EndsWith($"_{morphName}"))
            {
                string baseName = meshName.Substring(0, meshName.Length - morphName.Length - 1);
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var ctx = model.GetMeshContext(i);
                    if (ctx != null && (ctx.Type == MeshType.Mesh || ctx.Type == MeshType.BakedMirror) && ctx.Name == baseName)
                        return i;
                }
            }
            return -1;
        }

        // ================================================================
        // CSV読込・保存
        // ================================================================

        private void OnCsvImport()
        {
            var model = Model;
            if (model == null) return;

            string path = EditorUtility.OpenFilePanel("BlendShapeSync CSV読込", "", "csv");
            if (string.IsNullOrEmpty(path)) return;

            // CSVHelper でパース
            var rows = CSVHelper.ParseFile(path);
            if (rows.Count == 0)
            { StatusLog("CSVが空です"); return; }

            // メッシュ名 → インデックス辞書構築
            var meshNameToIndex = new Dictionary<string, int>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc != null && !string.IsNullOrEmpty(mc.Name))
                    meshNameToIndex[mc.Name] = i;
            }

            // CSV形式: ExpressionName,MeshName,BlendShapeName,Weight,MeshName,BlendShapeName,Weight,...
            var importedSets = new Dictionary<string, MorphExpression>();
            int unmatchedCount = 0;

            foreach (var row in rows)
            {
                if (CSVHelper.IsCommentLine(row.OriginalLine)) continue;
                if (row.FieldCount < 4) continue;

                string expressionName = row[0];
                if (string.IsNullOrEmpty(expressionName)) continue;

                var set = new MorphExpression
                {
                    Name = expressionName,
                    NameEnglish = "",
                    Panel = 3,
                    Type = MorphType.Vertex,
                };

                // 残りを3つずつ解析: MeshName, BlendShapeName, Weight
                for (int i = 1; i + 2 < row.FieldCount; i += 3)
                {
                    string meshName = row[i];
                    string shapeName = row[i + 1];
                    string weightStr = row[i + 2];

                    if (string.IsNullOrEmpty(meshName) || string.IsNullOrEmpty(shapeName)) continue;
                    if (!float.TryParse(weightStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float weight))
                        continue;

                    // モーフメッシュ名: "BaseMeshName_BlendShapeName"
                    string morphMeshName = $"{meshName}_{shapeName}";
                    if (meshNameToIndex.TryGetValue(morphMeshName, out int meshIndex))
                        set.AddMesh(meshIndex, weight);
                    else
                        unmatchedCount++;
                }

                if (set.MeshCount > 0)
                    importedSets[expressionName] = set;
            }

            if (importedSets.Count == 0)
            { StatusLog($"マッチするモーフメッシュが見つかりません (未マッチ: {unmatchedCount})"); return; }

            // 同名チェック
            var overwriteNames = new List<string>();
            foreach (var name in importedSets.Keys)
                if (model.FindMorphExpressionByName(name) != null)
                    overwriteNames.Add(name);

            if (overwriteNames.Count > 0)
            {
                string msg = $"以下のセットは既に存在します。上書きしますか？\n{string.Join(", ", overwriteNames)}";
                if (!EditorUtility.DisplayDialog("上書き確認", msg, "上書き", "キャンセル"))
                    return;
            }

            var oldSets = model.MorphExpressions.Select(s => s.Clone()).ToList();

            foreach (var imported in importedSets.Values)
            {
                int existIdx = model.MorphExpressions.FindIndex(s => s.Name == imported.Name);
                if (existIdx >= 0)
                    model.MorphExpressions[existIdx] = imported;
                else
                    model.MorphExpressions.Add(imported);
            }

            var newSets = model.MorphExpressions.Select(s => s.Clone()).ToList();
            var record = new MorphExpressionListReplaceRecord { OldSets = oldSets, NewSets = newSets };
            RecordUndo(record, $"CSVインポート: {importedSets.Count}セット");

            string unmatchMsg = unmatchedCount > 0 ? $" (未マッチ: {unmatchedCount})" : "";
            StatusLog($"CSV読込完了: {importedSets.Count}セット ({overwriteNames.Count}件上書き){unmatchMsg}");
            RefreshAll();
        }

        private void OnCsvExport()
        {
            var model = Model;
            if (model == null || model.MorphExpressionCount == 0)
            { StatusLog("保存するモーフエクスプレッションがありません"); return; }

            string path = EditorUtility.SaveFilePanel("BlendShapeSync CSV保存", "", "blendshape_sync.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                // CSV形式: ExpressionName,MeshName,BlendShapeName,Weight,MeshName,BlendShapeName,Weight,...
                var writer = new CSVWriter(2);
                writer.AddComment(" ExpressionName,MeshName,BlendShapeName,Weight,...");

                foreach (var set in model.MorphExpressions)
                {
                    if (set.Type != MorphType.Vertex) continue;
                    if (!set.IsValid) continue;

                    var parts = new List<object>();
                    parts.Add(set.Name); // ExpressionName

                    foreach (var entry in set.MeshEntries)
                    {
                        if (entry.MeshIndex < 0 || entry.MeshIndex >= model.MeshContextCount)
                            continue;

                        var morphCtx = model.GetMeshContext(entry.MeshIndex);
                        if (morphCtx == null || !morphCtx.IsMorph) continue;

                        // モーフメッシュ名 "BaseMeshName_MorphName" → BaseMeshName
                        string morphMeshName = morphCtx.Name;
                        int lastUnderscore = morphMeshName.LastIndexOf('_');
                        if (lastUnderscore <= 0) continue;

                        string baseMeshName = morphMeshName.Substring(0, lastUnderscore);

                        parts.Add(baseMeshName);       // MeshName
                        parts.Add(set.Name);           // BlendShapeName
                        parts.Add(entry.Weight);       // Weight
                    }

                    if (parts.Count > 1)
                        writer.AddRow(parts.ToArray());
                }

                writer.WriteToFile(path);
                StatusLog($"CSV保存完了: {model.MorphExpressionCount}セット → {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex) { StatusLog($"CSV保存失敗: {ex.Message}"); }
        }

        // ================================================================
        // Undo
        // ================================================================

        private void RecordUndo(MeshListUndoRecord record, string description)
        {
            var undoController = _context?.UndoController;
            if (undoController == null) return;
            undoController.MeshListStack.Record(record, description);
            undoController.FocusMeshList();
        }

        // ================================================================
        // ステータス
        // ================================================================

        private void StatusLog(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
            Debug.Log($"[MorphPanel] {msg}");
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        protected override void OnContextSet()
        {
            EndPreview();
            _settings.SelectedMorphExpressionIndex = -1;
            _settings.PreviewWeight = 0f;
            _selectedSetIndex = -1;
            RefreshAll();
        }

        protected override void OnUndoRedoPerformed()
        {
            base.OnUndoRedoPerformed();
            EndPreview();
            _settings.PreviewWeight = 0f;
            _previewWeight?.SetValueWithoutNotify(0f);
            RefreshAll();
        }

        private void OnDisable()
        {
            EndPreview();
        }

        protected override void OnDestroy()
        {
            EndPreview();
            base.OnDestroy();
        }
    }
}
