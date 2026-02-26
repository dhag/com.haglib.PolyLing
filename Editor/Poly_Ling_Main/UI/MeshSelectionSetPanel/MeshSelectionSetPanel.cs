// Assets/Editor/Poly_Ling_Main/UI/MeshSelectionSetPanel/MeshSelectionSetPanel.cs
// メッシュ選択セット管理パネル
// 名前・タイプフィルタ + 選択セット保存/復元
// UIToolkit ListView（仮想化）でパフォーマンス確保

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.Serialization.FolderSerializer;

namespace Poly_Ling.UI
{
    public class MeshSelectionSetPanel : EditorWindow
    {
        // ================================================================
        // 定数
        // ================================================================

        private const float ITEM_HEIGHT = 20f;
        private const int MAX_VISIBLE_ITEMS = 500;

        // ================================================================
        // データ
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;

        // フィルタ状態
        private string _nameFilter = "";
        private HashSet<MeshType> _activeTypeFilters = new HashSet<MeshType>();

        // フィルタ結果（ListView のデータソース）
        private List<FilteredMeshEntry> _filteredEntries = new List<FilteredMeshEntry>();

        // セットリストUI専用ローカル（セットリスト内の選択位置。他の選択系とは無関係）
        private int _selectedSetListIndex = -1;

        private bool _isRenaming = false;
        private int _renamingIndex = -1;
        private string _renamingName = "";

        // ================================================================
        // UI要素
        // ================================================================

        private TextField _nameFilterField;
        private VisualElement _typeFilterContainer;
        private ListView _meshListView;
        private Label _countLabel;
        private Label _statusLabel;

        // メッシュリスト操作
        private Button _btnCopy, _btnCut, _btnPaste;

        // セット管理
        private TextField _setNameField;
        private Button _btnSave;
        private ListView _setListView;
        private Button _btnLoad, _btnAdd, _btnDelete, _btnRename;

        // ================================================================
        // プロパティ
        // ================================================================

        private ModelContext Model => _toolContext?.Model;

        // ================================================================
        // ウィンドウ
        // ================================================================

        [MenuItem("Tools/Poly_Ling/debug/Mesh Selection Sets")]
        public static void ShowWindow()
        {
            var window = GetWindow<MeshSelectionSetPanel>();
            window.titleContent = new GUIContent("Mesh Selection Sets");
            window.minSize = new Vector2(300, 400);
        }

        public static MeshSelectionSetPanel Open(ToolContext ctx)
        {
            var window = GetWindow<MeshSelectionSetPanel>();
            window.titleContent = new GUIContent("Mesh Selection Sets");
            window.minSize = new Vector2(300, 400);
            window.SetContext(ctx);
            window.Show();
            return window;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            if (_toolContext != null)
            {
                if (_toolContext.Model != null)
                    _toolContext.Model.OnListChanged -= OnModelChanged;
                _toolContext.OnMeshSelectionChanged -= OnSelectionChangedExternal;
            }
        }

        private void CreateGUI()
        {
            BuildUI();
            ApplyFilter();
        }

        // ================================================================
        // コンテキスト
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            if (_toolContext != null)
            {
                if (_toolContext.Model != null)
                    _toolContext.Model.OnListChanged -= OnModelChanged;
                _toolContext.OnMeshSelectionChanged -= OnSelectionChangedExternal;
            }

            _toolContext = ctx;

            if (_toolContext != null)
            {
                if (_toolContext.Model != null)
                    _toolContext.Model.OnListChanged += OnModelChanged;
                _toolContext.OnMeshSelectionChanged += OnSelectionChangedExternal;
                ApplyFilter();
                RefreshSetList();
            }
        }

        private void OnModelChanged()
        {
            ApplyFilter();
            RefreshSetList();
        }

        /// <summary>
        /// 他パネルからの選択変更通知 → メッシュリストの選択表示を同期
        /// </summary>
        private void OnSelectionChangedExternal()
        {
            SyncMeshListSelection();
            UpdateClipboardButtons();
        }

        // ================================================================
        // UI構築
        // ================================================================

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 4;
            root.style.paddingRight = 4;
            root.style.paddingTop = 4;
            root.style.paddingBottom = 4;

            // ---- フィルタセクション ----
            var filterSection = new Foldout { text = "Filter", value = true };
            filterSection.style.marginBottom = 4;

            // 名前フィルタ
            var nameRow = new VisualElement();
            nameRow.style.flexDirection = FlexDirection.Row;
            nameRow.style.marginBottom = 4;
            nameRow.style.height = 22;

            var nameLabel = new Label("Name");
            nameLabel.style.width = 40;
            nameLabel.style.minHeight = 20;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameRow.Add(nameLabel);

            _nameFilterField = new TextField();
            _nameFilterField.style.flexGrow = 1;
            _nameFilterField.style.minHeight = 20;
            _nameFilterField.RegisterValueChangedCallback(evt =>
            {
                _nameFilter = evt.newValue ?? "";
                ApplyFilter();
            });
            nameRow.Add(_nameFilterField);

            var btnClearName = new Button(() =>
            {
                _nameFilterField.value = "";
            })
            { text = "✕" };
            btnClearName.style.width = 24;
            btnClearName.style.minHeight = 20;
            nameRow.Add(btnClearName);

            filterSection.Add(nameRow);

            // タイプフィルタ
            var typeLabel = new Label("Type");
            typeLabel.style.marginTop = 4;
            typeLabel.style.marginBottom = 2;
            filterSection.Add(typeLabel);

            _typeFilterContainer = new VisualElement();
            _typeFilterContainer.style.flexDirection = FlexDirection.Row;
            _typeFilterContainer.style.flexWrap = Wrap.Wrap;
            _typeFilterContainer.style.marginBottom = 4;

            foreach (MeshType meshType in Enum.GetValues(typeof(MeshType)))
            {
                var toggle = new Toggle(meshType.ToString());
                toggle.style.marginRight = 8;
                toggle.style.marginBottom = 2;
                toggle.style.minHeight = 18;
                toggle.style.minWidth = 60;
                toggle.value = false;
                var capturedType = meshType;
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                        _activeTypeFilters.Add(capturedType);
                    else
                        _activeTypeFilters.Remove(capturedType);
                    ApplyFilter();
                });
                _typeFilterContainer.Add(toggle);
            }

            filterSection.Add(_typeFilterContainer);

            root.Add(filterSection);

            // ---- メッシュリスト ----
            _countLabel = new Label("0 items");
            _countLabel.style.marginBottom = 2;
            root.Add(_countLabel);

            _meshListView = new ListView();
            _meshListView.fixedItemHeight = ITEM_HEIGHT;
            _meshListView.selectionType = SelectionType.Multiple;
            _meshListView.makeItem = MakeMeshItem;
            _meshListView.bindItem = BindMeshItem;
            _meshListView.selectionChanged += OnMeshListSelectionChanged;
            _meshListView.style.flexGrow = 1;
            _meshListView.style.minHeight = 100;
            _meshListView.style.maxHeight = 300;
            root.Add(_meshListView);

            // ---- Copy/Cut/Paste ボタン行（メッシュリスト操作用） ----
            var clipRow = new VisualElement();
            clipRow.style.flexDirection = FlexDirection.Row;
            clipRow.style.marginTop = 2;
            clipRow.style.height = 24;

            _btnCopy = new Button(OnCopyMeshes) { text = "Copy" };
            _btnCopy.style.flexGrow = 1;
            _btnCopy.style.minHeight = 22;
            _btnCopy.SetEnabled(false);
            clipRow.Add(_btnCopy);

            _btnCut = new Button(OnCutMeshes) { text = "Cut" };
            _btnCut.style.flexGrow = 1;
            _btnCut.style.minHeight = 22;
            _btnCut.SetEnabled(false);
            clipRow.Add(_btnCut);

            _btnPaste = new Button(OnPasteMeshes) { text = "Paste" };
            _btnPaste.style.flexGrow = 1;
            _btnPaste.style.minHeight = 22;
            clipRow.Add(_btnPaste);

            root.Add(clipRow);

            // ---- 選択セットセクション ----
            var setSection = new Foldout { text = "Selection Sets", value = true };
            setSection.style.marginTop = 4;
            setSection.style.flexShrink = 0;

            // 保存行
            var saveRow = new VisualElement();
            saveRow.style.flexDirection = FlexDirection.Row;
            saveRow.style.marginBottom = 4;
            saveRow.style.height = 22;

            _setNameField = new TextField();
            _setNameField.style.flexGrow = 1;
            _setNameField.style.minHeight = 20;
            saveRow.Add(_setNameField);

            _btnSave = new Button(OnSaveSet) { text = "Save" };
            _btnSave.style.width = 50;
            _btnSave.style.minHeight = 20;
            saveRow.Add(_btnSave);

            setSection.Add(saveRow);

            // セットリスト
            _setListView = new ListView();
            _setListView.fixedItemHeight = ITEM_HEIGHT;
            _setListView.selectionType = SelectionType.Single;
            _setListView.makeItem = MakeSetItem;
            _setListView.bindItem = BindSetItem;
            _setListView.selectionChanged += OnSetListSelectionChanged;
            _setListView.style.minHeight = 60;
            _setListView.style.maxHeight = 150;
            setSection.Add(_setListView);

            // 操作ボタン行
            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginTop = 4;
            opRow.style.height = 24;

            _btnLoad = new Button(OnLoadSet) { text = "Load" };
            _btnLoad.style.flexGrow = 1;
            _btnLoad.style.minHeight = 22;
            _btnLoad.SetEnabled(false);
            opRow.Add(_btnLoad);

            _btnAdd = new Button(OnAddSet) { text = "Add" };
            _btnAdd.style.flexGrow = 1;
            _btnAdd.style.minHeight = 22;
            _btnAdd.SetEnabled(false);
            opRow.Add(_btnAdd);

            _btnRename = new Button(OnRenameSet) { text = "Rename" };
            _btnRename.style.flexGrow = 1;
            _btnRename.style.minHeight = 22;
            _btnRename.SetEnabled(false);
            opRow.Add(_btnRename);

            _btnDelete = new Button(OnDeleteSet) { text = "Delete" };
            _btnDelete.style.flexGrow = 1;
            _btnDelete.style.minHeight = 22;
            _btnDelete.SetEnabled(false);
            opRow.Add(_btnDelete);

            setSection.Add(opRow);

            root.Add(setSection);

            // ステータス
            _statusLabel = new Label("");
            _statusLabel.style.marginTop = 2;
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            root.Add(_statusLabel);
        }

        // ================================================================
        // メッシュリストアイテム
        // ================================================================

        private VisualElement MakeMeshItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = ITEM_HEIGHT;
            row.style.alignItems = Align.Center;

            var typeLbl = new Label();
            typeLbl.name = "type-label";
            typeLbl.style.width = 50;
            typeLbl.style.fontSize = 10;
            typeLbl.style.color = new Color(0.5f, 0.7f, 1f);
            row.Add(typeLbl);

            var nameLbl = new Label();
            nameLbl.name = "name-label";
            nameLbl.style.flexGrow = 1;
            nameLbl.style.overflow = Overflow.Hidden;
            row.Add(nameLbl);

            var infoLbl = new Label();
            infoLbl.name = "info-label";
            infoLbl.style.width = 70;
            infoLbl.style.fontSize = 10;
            infoLbl.style.unityTextAlign = TextAnchor.MiddleRight;
            infoLbl.style.color = new Color(0.6f, 0.6f, 0.6f);
            row.Add(infoLbl);

            return row;
        }

        private void BindMeshItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _filteredEntries.Count) return;

            var entry = _filteredEntries[index];

            var typeLbl = element.Q<Label>("type-label");
            if (typeLbl != null)
                typeLbl.text = entry.TypeShort;

            var nameLbl = element.Q<Label>("name-label");
            if (nameLbl != null)
                nameLbl.text = entry.Name;

            var infoLbl = element.Q<Label>("info-label");
            if (infoLbl != null)
                infoLbl.text = entry.Info;
        }

        // ================================================================
        // セットリストアイテム
        // ================================================================

        private VisualElement MakeSetItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = ITEM_HEIGHT;
            row.style.alignItems = Align.Center;

            var nameLbl = new Label();
            nameLbl.name = "set-name";
            nameLbl.style.flexGrow = 1;
            row.Add(nameLbl);

            var summaryLbl = new Label();
            summaryLbl.name = "set-summary";
            summaryLbl.style.width = 80;
            summaryLbl.style.fontSize = 10;
            summaryLbl.style.unityTextAlign = TextAnchor.MiddleRight;
            summaryLbl.style.color = new Color(0.6f, 0.6f, 0.6f);
            row.Add(summaryLbl);

            return row;
        }

        private void BindSetItem(VisualElement element, int index)
        {
            var sets = Model?.MeshSelectionSets;
            if (sets == null || index < 0 || index >= sets.Count) return;

            var set = sets[index];

            var nameLbl = element.Q<Label>("set-name");
            if (nameLbl != null)
                nameLbl.text = set.Name;

            var summaryLbl = element.Q<Label>("set-summary");
            if (summaryLbl != null)
                summaryLbl.text = set.Summary;
        }

        // ================================================================
        // フィルタ
        // ================================================================

        private void ApplyFilter()
        {
            _filteredEntries.Clear();

            if (Model == null)
            {
                RefreshMeshListView();
                return;
            }

            bool hasNameFilter = !string.IsNullOrEmpty(_nameFilter);
            bool hasTypeFilter = _activeTypeFilters.Count > 0;
            string lowerFilter = hasNameFilter ? _nameFilter.ToLowerInvariant() : "";

            int total = Model.MeshContextCount;
            int limit = (!hasNameFilter && !hasTypeFilter) ? MAX_VISIBLE_ITEMS : int.MaxValue;

            for (int i = 0; i < total && _filteredEntries.Count < limit; i++)
            {
                var ctx = Model.GetMeshContext(i);
                if (ctx == null) continue;

                var meshType = ctx.MeshObject?.Type ?? MeshType.Mesh;

                if (hasTypeFilter && !_activeTypeFilters.Contains(meshType))
                    continue;

                if (hasNameFilter)
                {
                    string name = ctx.Name ?? "";
                    if (!name.ToLowerInvariant().Contains(lowerFilter))
                        continue;
                }

                _filteredEntries.Add(new FilteredMeshEntry(i, ctx));
            }

            RefreshMeshListView();
        }

        private void RefreshMeshListView()
        {
            if (_meshListView == null) return;

            _meshListView.itemsSource = _filteredEntries;
            _meshListView.Rebuild();

            int total = Model?.MeshContextCount ?? 0;
            bool truncated = _filteredEntries.Count >= MAX_VISIBLE_ITEMS &&
                             _activeTypeFilters.Count == 0 &&
                             string.IsNullOrEmpty(_nameFilter);

            string countText = truncated
                ? $"{_filteredEntries.Count}/{total} items (filtered to limit)"
                : $"{_filteredEntries.Count}/{total} items";

            if (_countLabel != null)
                _countLabel.text = countText;

            SyncMeshListSelection();
        }

        // ================================================================
        // メッシュリスト選択同期
        // ================================================================

        private bool _isSyncingSelection = false;

        /// <summary>
        /// ModelContext → ListView 選択同期
        /// </summary>
        private void SyncMeshListSelection()
        {
            if (_meshListView == null || Model == null || _isSyncingSelection) return;

            _isSyncingSelection = true;
            try
            {
                var allSelected = Model.SelectedMeshContextIndices;
                var indices = new List<int>();

                for (int i = 0; i < _filteredEntries.Count; i++)
                {
                    if (allSelected.Contains(_filteredEntries[i].MasterIndex))
                        indices.Add(i);
                }

                _meshListView.SetSelectionWithoutNotify(indices);
                UpdateClipboardButtons();
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        /// <summary>
        /// ListView → ModelContext 選択同期
        /// </summary>
        private void OnMeshListSelectionChanged(IEnumerable<object> selectedItems)
        {
            if (Model == null || _isSyncingSelection) return;

            _isSyncingSelection = true;
            try
            {
                var meshIndices = new List<int>();
                var boneIndices = new List<int>();
                var morphIndices = new List<int>();

                foreach (var item in selectedItems)
                {
                    if (item is FilteredMeshEntry entry)
                    {
                        switch (entry.MeshType)
                        {
                            case MeshType.Mesh:
                            case MeshType.BakedMirror:
                                meshIndices.Add(entry.MasterIndex);
                                break;
                            case MeshType.Bone:
                                boneIndices.Add(entry.MasterIndex);
                                break;
                            case MeshType.Morph:
                                morphIndices.Add(entry.MasterIndex);
                                break;
                            default:
                                meshIndices.Add(entry.MasterIndex);
                                break;
                        }
                    }
                }

                if (meshIndices.Count > 0 || boneIndices.Count == 0 && morphIndices.Count == 0)
                {
                    Model.ClearMeshSelection();
                    foreach (var idx in meshIndices)
                        Model.AddToMeshSelection(idx);
                }

                if (boneIndices.Count > 0)
                {
                    Model.ClearBoneSelection();
                    foreach (var idx in boneIndices)
                        Model.AddToBoneSelection(idx);
                }

                if (morphIndices.Count > 0)
                {
                    Model.ClearMorphSelection();
                    foreach (var idx in morphIndices)
                        Model.AddToMorphSelection(idx);
                }

                _toolContext?.OnMeshSelectionChanged?.Invoke();
                _toolContext?.Repaint?.Invoke();
                UpdateClipboardButtons();
            }
            finally
            {
                _isSyncingSelection = false;
            }
        }

        // ================================================================
        // クリップボード操作（Copy / Cut / Paste）
        // 対象: メッシュリストで選択中のMeshContext群
        // フォーマット: CsvMeshSerializer（プロジェクトCSV保存と同一）
        // ================================================================

        /// <summary>
        /// メッシュリスト選択状態に応じてCopy/Cutボタンの有効/無効を更新
        /// </summary>
        private void UpdateClipboardButtons()
        {
            bool hasSelection = _meshListView?.selectedIndices?.Any() ?? false;
            _btnCopy?.SetEnabled(hasSelection);
            _btnCut?.SetEnabled(hasSelection);
        }

        private void OnCopyMeshes()
        {
            if (Model == null) return;

            var selected = GetSelectedMeshEntries();
            if (selected.Count == 0)
            {
                SetStatus("No meshes selected");
                return;
            }

            string csv = SerializeEntriesToCsv(selected);
            GUIUtility.systemCopyBuffer = csv;
            SetStatus($"Copied: {selected.Count} mesh(es)");
        }

        private void OnCutMeshes()
        {
            if (Model == null) return;

            var selected = GetSelectedMeshEntries();
            if (selected.Count == 0)
            {
                SetStatus("No meshes selected");
                return;
            }

            string csv = SerializeEntriesToCsv(selected);
            GUIUtility.systemCopyBuffer = csv;

            // インデックス降順で削除（後ろから消さないとインデックスがずれる）
            var masterIndices = selected.Select(e => e.GlobalIndex).OrderByDescending(i => i).ToList();
            foreach (int idx in masterIndices)
            {
                if (idx >= 0 && idx < Model.MeshContextCount)
                {
                    Model.MeshContextList.RemoveAt(idx);
                }
            }

            Model.OnListChanged?.Invoke();
            _toolContext?.Repaint?.Invoke();
            SetStatus($"Cut: {selected.Count} mesh(es)");
        }

        private void OnPasteMeshes()
        {
            if (Model == null) return;

            string clipboard = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(clipboard))
            {
                SetStatus("Clipboard is empty");
                return;
            }

            List<CsvMeshEntry> entries;
            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), $"polyling_paste_{Guid.NewGuid():N}.csv");
                File.WriteAllText(tempPath, clipboard, System.Text.Encoding.UTF8);
                entries = CsvMeshSerializer.ReadFile(tempPath);
                File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                SetStatus($"Paste failed: {ex.Message}");
                return;
            }

            if (entries == null || entries.Count == 0)
            {
                SetStatus("No valid mesh data in clipboard");
                return;
            }

            // 名前重複チェック用
            var existingNames = new HashSet<string>();
            for (int i = 0; i < Model.MeshContextCount; i++)
            {
                var mc = Model.GetMeshContext(i);
                if (mc != null && !string.IsNullOrEmpty(mc.Name))
                    existingNames.Add(mc.Name);
            }

            int addedCount = 0;
            foreach (var entry in entries)
            {
                var mc = entry.MeshContext;
                if (mc == null) continue;

                // 名前重複時はユニーク名生成
                if (existingNames.Contains(mc.Name))
                {
                    string baseName = mc.Name;
                    int suffix = 2;
                    string candidate = $"{baseName}_{suffix}";
                    while (existingNames.Contains(candidate))
                    {
                        suffix++;
                        candidate = $"{baseName}_{suffix}";
                    }
                    mc.Name = candidate;
                    if (mc.MeshObject != null) mc.MeshObject.Name = candidate;
                }

                existingNames.Add(mc.Name);
                Model.Add(mc);
                addedCount++;
            }

            Model.OnListChanged?.Invoke();
            _toolContext?.Repaint?.Invoke();
            SetStatus($"Pasted: {addedCount} mesh(es)");
        }

        // ================================================================
        // クリップボード: ヘルパー
        // ================================================================

        /// <summary>
        /// メッシュリストで選択中のFilteredMeshEntryからCsvMeshEntryリストを構築
        /// </summary>
        private List<CsvMeshEntry> GetSelectedMeshEntries()
        {
            var result = new List<CsvMeshEntry>();
            if (_meshListView == null || Model == null) return result;

            foreach (var item in _meshListView.selectedItems)
            {
                if (item is FilteredMeshEntry filtered)
                {
                    var mc = Model.GetMeshContext(filtered.MasterIndex);
                    if (mc != null)
                    {
                        result.Add(new CsvMeshEntry
                        {
                            GlobalIndex = filtered.MasterIndex,
                            MeshContext = mc
                        });
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// CsvMeshEntryリストをCSV文字列にシリアライズ（CsvMeshSerializerと同一フォーマット）
        /// </summary>
        private string SerializeEntriesToCsv(List<CsvMeshEntry> entries)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"polyling_copy_{Guid.NewGuid():N}.csv");
            try
            {
                // ReadFileはヘッダに関係なく各エントリのtype行でタイプ判定するため、
                // fileTypeは多数派を使用（混在しても読み込み可能）
                int boneCount = entries.Count(e => e.MeshContext?.Type == MeshType.Bone);
                int morphCount = entries.Count(e => e.MeshContext?.Type == MeshType.Morph);
                int meshCount = entries.Count - boneCount - morphCount;

                string fileType = "mesh";
                if (boneCount > meshCount && boneCount > morphCount) fileType = "bone";
                else if (morphCount > meshCount && morphCount > boneCount) fileType = "morph";

                CsvMeshSerializer.WriteFile(tempPath, entries, fileType);
                string csv = File.ReadAllText(tempPath, System.Text.Encoding.UTF8);
                return csv;
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        // ================================================================
        // 選択セット操作
        // ================================================================

        private void OnSaveSet()
        {
            if (Model == null) return;

            var selectedItems = _meshListView.selectedItems?.Cast<FilteredMeshEntry>().ToList();
            if (selectedItems == null || selectedItems.Count == 0)
            {
                SetStatus("No meshes selected");
                return;
            }

            string name = _setNameField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(name))
                name = Model.GenerateUniqueMeshSelectionSetName("MeshSet");

            if (Model.FindMeshSelectionSetByName(name) != null)
                name = Model.GenerateUniqueMeshSelectionSetName(name);

            var category = DetermineCategory(selectedItems);

            var set = new MeshSelectionSet(name) { Category = category };
            foreach (var entry in selectedItems)
            {
                if (!string.IsNullOrEmpty(entry.Name) && !set.MeshNames.Contains(entry.Name))
                    set.MeshNames.Add(entry.Name);
            }

            Model.MeshSelectionSets.Add(set);
            _setNameField.value = "";
            RefreshSetList();
            SetStatus($"Saved: {name} ({set.Count} meshes)");
        }

        private ModelContext.SelectionCategory DetermineCategory(List<FilteredMeshEntry> entries)
        {
            int mesh = 0, bone = 0, morph = 0;
            foreach (var e in entries)
            {
                switch (e.MeshType)
                {
                    case MeshType.Bone: bone++; break;
                    case MeshType.Morph: morph++; break;
                    default: mesh++; break;
                }
            }

            if (bone >= mesh && bone >= morph) return ModelContext.SelectionCategory.Bone;
            if (morph >= mesh && morph >= bone) return ModelContext.SelectionCategory.Morph;
            return ModelContext.SelectionCategory.Mesh;
        }

        private void OnLoadSet()
        {
            if (Model == null || _selectedSetListIndex < 0) return;

            var sets = Model.MeshSelectionSets;
            if (_selectedSetListIndex >= sets.Count) return;

            var set = sets[_selectedSetListIndex];
            set.ApplyTo(Model);

            _toolContext?.OnMeshSelectionChanged?.Invoke();
            _toolContext?.Repaint?.Invoke();
            SyncMeshListSelection();
            SetStatus($"Loaded: {set.Name}");
        }

        private void OnAddSet()
        {
            if (Model == null || _selectedSetListIndex < 0) return;

            var sets = Model.MeshSelectionSets;
            if (_selectedSetListIndex >= sets.Count) return;

            var set = sets[_selectedSetListIndex];
            set.AddTo(Model);

            _toolContext?.OnMeshSelectionChanged?.Invoke();
            _toolContext?.Repaint?.Invoke();
            SyncMeshListSelection();
            SetStatus($"Added: {set.Name}");
        }

        private void OnDeleteSet()
        {
            if (Model == null || _selectedSetListIndex < 0) return;

            var sets = Model.MeshSelectionSets;
            if (_selectedSetListIndex >= sets.Count) return;

            string name = sets[_selectedSetListIndex].Name;

            if (!EditorUtility.DisplayDialog("Delete", $"Delete set '{name}'?", "OK", "Cancel"))
                return;

            sets.RemoveAt(_selectedSetListIndex);
            _selectedSetListIndex = -1;
            RefreshSetList();
            UpdateSetButtons();
            SetStatus($"Deleted: {name}");
        }

        private void OnRenameSet()
        {
            if (Model == null || _selectedSetListIndex < 0) return;

            var sets = Model.MeshSelectionSets;
            if (_selectedSetListIndex >= sets.Count) return;

            var set = sets[_selectedSetListIndex];

            string newName = _setNameField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(newName))
            {
                SetStatus("Enter new name in the text field first");
                return;
            }

            if (Model.FindMeshSelectionSetByName(newName) != null && newName != set.Name)
            {
                SetStatus($"Name '{newName}' already exists");
                return;
            }

            string oldName = set.Name;
            set.Name = newName;
            _setNameField.value = "";
            RefreshSetList();
            SetStatus($"Renamed: {oldName} → {newName}");
        }

        // ================================================================
        // セットリスト
        // ================================================================

        private void RefreshSetList()
        {
            if (_setListView == null) return;

            var sets = Model?.MeshSelectionSets;
            if (sets == null)
            {
                _setListView.itemsSource = new List<MeshSelectionSet>();
                _setListView.Rebuild();
                return;
            }

            _setListView.itemsSource = sets;
            _setListView.Rebuild();
        }

        private void OnSetListSelectionChanged(IEnumerable<object> selectedItems)
        {
            var list = selectedItems?.ToList();
            _selectedSetListIndex = (list != null && list.Count > 0)
                ? Model?.MeshSelectionSets?.IndexOf(list[0] as MeshSelectionSet) ?? -1
                : -1;
            UpdateSetButtons();
        }

        private void UpdateSetButtons()
        {
            bool hasSelection = _selectedSetListIndex >= 0;
            _btnLoad?.SetEnabled(hasSelection);
            _btnAdd?.SetEnabled(hasSelection);
            _btnDelete?.SetEnabled(hasSelection);
            _btnRename?.SetEnabled(hasSelection);
        }

        // ================================================================
        // ステータス
        // ================================================================

        private void SetStatus(string msg)
        {
            if (_statusLabel != null)
                _statusLabel.text = msg;
        }

        // ================================================================
        // フィルタ結果エントリ
        // ================================================================

        private class FilteredMeshEntry
        {
            public int MasterIndex { get; }
            public string Name { get; }
            public MeshType MeshType { get; }
            public string TypeShort { get; }
            public string Info { get; }

            public FilteredMeshEntry(int masterIndex, MeshContext ctx)
            {
                MasterIndex = masterIndex;
                Name = ctx.Name ?? "Untitled";
                MeshType = ctx.MeshObject?.Type ?? MeshType.Mesh;
                TypeShort = MeshType switch
                {
                    MeshType.Mesh => "Mesh",
                    MeshType.Bone => "Bone",
                    MeshType.Morph => "Morph",
                    MeshType.BakedMirror => "Mirror",
                    MeshType.RigidBody => "Rigid",
                    MeshType.RigidBodyJoint => "Joint",
                    MeshType.Helper => "Help",
                    MeshType.Group => "Group",
                    _ => "?"
                };

                int vc = ctx.MeshObject?.VertexCount ?? 0;
                int fc = ctx.MeshObject?.FaceCount ?? 0;
                Info = vc > 0 ? $"V:{vc} F:{fc}" : "";
            }
        }
    }
}
