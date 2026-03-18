// MeshSelectionSetPanelV2.cs
// メッシュ選択辞書パネル（新形式）
// MeshListPanelV2 と同じ PanelContext / IProjectView / PanelCommand 構成
// Drawable / Bone / Morph タブで完全分離
// ModelContext への直接依存なし（辞書・クリップボード操作は LiveModelView キャスト経由）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.Serialization.FolderSerializer;

namespace Poly_Ling.UI
{
    public class MeshSelectionSetPanelV2 : EditorWindow
    {
        // ================================================================
        // 内部定義
        // ================================================================

        private enum TabType { Drawable, Bone, Morph }

        private const float ITEM_HEIGHT = 20f;

        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _ctx;
        private bool _isReceiving;
        private bool _isSyncingSelection;

        // ================================================================
        // 状態
        // ================================================================

        private TabType _currentTab = TabType.Drawable;
        // タブごとのフィルタ文字列
        private readonly string[] _filters = { "", "", "" };
        // フィルタ済みメッシュリスト（現タブ用）
        private readonly List<IMeshView> _filteredData = new List<IMeshView>();
        // 選択辞書リスト選択位置
        private int _selectedSetIndex = -1;

        // ================================================================
        // UI 要素
        // ================================================================

        // タブ
        private Button _tabDrawable, _tabBone, _tabMorph;
        // フィルタ
        private TextField _filterField;
        // メッシュリスト
        private ListView _meshListView;
        private Label _countLabel;
        // クリップボード
        private Button _btnCopy, _btnCut, _btnPaste;
        // CSV
        private Button _btnSaveMeshCsv, _btnLoadMeshCsv;
        // 選択辞書
        private TextField _setNameField;
        private Button _btnSaveSet;
        private ListView _setListView;
        private Button _btnApplySet, _btnAddSet, _btnRenameSet, _btnDeleteSet;
        private Button _btnSaveDic, _btnLoadDic;
        // ステータス
        private Label _statusLabel;

        // ================================================================
        // プロパティ
        // ================================================================

        private IModelView CurrentModel => _ctx?.CurrentView?.CurrentModel;
        private int ModelIndex => _ctx?.CurrentView?.CurrentModelIndex ?? 0;

        private MeshCategory CurrentCategory => _currentTab switch
        {
            TabType.Drawable => MeshCategory.Drawable,
            TabType.Bone => MeshCategory.Bone,
            TabType.Morph => MeshCategory.Morph,
            _ => MeshCategory.Drawable
        };

        private int CurrentFilterIndex => (int)_currentTab;

        /// <summary>
        /// ローカル実行時限定：ModelContext を直接取得する。
        /// 辞書・クリップボード操作に使用。LiveModelView 以外の場合は null。
        /// </summary>
        private ModelContext GetModelContext()
            => (CurrentModel as LiveModelView)?.ModelContext;

        // ================================================================
        // ウィンドウ
        // ================================================================

        //[MenuItem("Tools/Poly_Ling/Utility/Mesh Selection Dictionary V2")]
        public static void ShowWindow()
        {
            var w = GetWindow<MeshSelectionSetPanelV2>();
            w.titleContent = new GUIContent("メッシュ選択辞書 V2");
            w.minSize = new Vector2(300, 480);
        }

        public static MeshSelectionSetPanelV2 Open(PanelContext ctx)
        {
            var w = GetWindow<MeshSelectionSetPanelV2>();
            w.titleContent = new GUIContent("メッシュ選択辞書 V2");
            w.minSize = new Vector2(300, 480);
            w.SetContext(ctx);
            w.Show();
            return w;
        }

        private void SetContext(PanelContext ctx)
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
            _ctx = ctx;
            if (_ctx != null)
            {
                _ctx.OnViewChanged += OnViewChanged;
                if (_ctx.CurrentView != null) OnViewChanged(_ctx.CurrentView, ChangeKind.ModelSwitch);
            }
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            if (_ctx != null)
            {
                _ctx.OnViewChanged -= OnViewChanged;
                _ctx.OnViewChanged += OnViewChanged;
            }
        }

        private void OnDisable()
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
        }

        private void CreateGUI()
        {
            BuildUI(rootVisualElement);
            SwitchTab(TabType.Drawable);
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft = 4;
            root.style.paddingRight = 4;
            root.style.paddingTop = 4;
            root.style.paddingBottom = 4;

            // ---- タブ行 ----
            var tabRow = new VisualElement();
            tabRow.style.flexDirection = FlexDirection.Row;
            tabRow.style.marginBottom = 4;
            _tabDrawable = MkTabBtn("Drawable");
            _tabBone     = MkTabBtn("Bone");
            _tabMorph    = MkTabBtn("Morph");
            tabRow.Add(_tabDrawable); tabRow.Add(_tabBone); tabRow.Add(_tabMorph);
            root.Add(tabRow);

            _tabDrawable.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Drawable));
            _tabBone.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Bone));
            _tabMorph.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Morph));

            // ---- フィルタ行 ----
            var filterRow = new VisualElement();
            filterRow.style.flexDirection = FlexDirection.Row;
            filterRow.style.marginBottom = 4;
            filterRow.style.height = 22;

            _filterField = new TextField();
            _filterField.style.flexGrow = 1;
            _filterField.style.minHeight = 20;
            _filterField.RegisterValueChangedCallback(evt =>
            {
                _filters[CurrentFilterIndex] = evt.newValue ?? "";
                RebuildFilteredData();
            });
            filterRow.Add(_filterField);

            var btnClear = new Button(() => { _filterField.value = ""; }) { text = "✕" };
            btnClear.style.width = 24; btnClear.style.minHeight = 20;
            filterRow.Add(btnClear);
            root.Add(filterRow);

            // ---- メッシュリスト ----
            _countLabel = new Label("0 items");
            _countLabel.style.marginBottom = 2;
            root.Add(_countLabel);

            _meshListView = new ListView();
            _meshListView.fixedItemHeight = ITEM_HEIGHT;
            _meshListView.selectionType = SelectionType.Multiple;
            _meshListView.makeItem = MakeMeshItem;
            _meshListView.bindItem = BindMeshItem;
            _meshListView.itemsSource = _filteredData;
            _meshListView.selectionChanged += OnMeshListSelectionChanged;
            _meshListView.style.flexGrow = 1;
            _meshListView.style.minHeight = 80;
            _meshListView.style.maxHeight = 260;
            root.Add(_meshListView);

            // ---- クリップボード行 ----
            var clipRow = new VisualElement();
            clipRow.style.flexDirection = FlexDirection.Row;
            clipRow.style.marginTop = 2; clipRow.style.height = 24;
            _btnCopy  = MkBtn("複製",   OnCopy);
            _btnCut   = MkBtn("切取り", OnCut);
            _btnPaste = MkBtn("貼付け", OnPaste);
            clipRow.Add(_btnCopy); clipRow.Add(_btnCut); clipRow.Add(_btnPaste);
            root.Add(clipRow);

            // ---- CSV行 ----
            var csvRow = new VisualElement();
            csvRow.style.flexDirection = FlexDirection.Row;
            csvRow.style.marginTop = 2; csvRow.style.height = 24;
            _btnSaveMeshCsv = MkBtn("メッシュCSV↓", OnSaveMeshCsv);
            _btnLoadMeshCsv = MkBtn("メッシュCSV↑", OnLoadMeshCsv);
            csvRow.Add(_btnSaveMeshCsv); csvRow.Add(_btnLoadMeshCsv);
            root.Add(csvRow);

            // ---- 選択辞書セクション ----
            var dicSection = new Foldout { text = "選択辞書", value = true };
            dicSection.style.marginTop = 6; dicSection.style.flexShrink = 0;

            // 保存行
            var saveRow = new VisualElement();
            saveRow.style.flexDirection = FlexDirection.Row;
            saveRow.style.marginBottom = 4; saveRow.style.height = 22;
            _setNameField = new TextField();
            _setNameField.style.flexGrow = 1; _setNameField.style.minHeight = 20;
            _setNameField.tooltip = "辞書エントリ名（空欄時は自動生成）";
            saveRow.Add(_setNameField);
            _btnSaveSet = new Button(OnSaveSet) { text = "辞書化" };
            _btnSaveSet.style.width = 52; _btnSaveSet.style.minHeight = 20;
            saveRow.Add(_btnSaveSet);
            dicSection.Add(saveRow);

            // セットリスト
            _setListView = new ListView();
            _setListView.fixedItemHeight = ITEM_HEIGHT;
            _setListView.selectionType = SelectionType.Single;
            _setListView.makeItem = MakeSetItem;
            _setListView.bindItem = BindSetItem;
            _setListView.selectionChanged += OnSetListSelectionChanged;
            _setListView.style.minHeight = 50; _setListView.style.maxHeight = 130;
            dicSection.Add(_setListView);

            // 操作ボタン行
            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginTop = 4; opRow.style.height = 24;
            _btnApplySet  = MkBtn("呼出し", OnApplySet);
            _btnAddSet    = MkBtn("追加",   OnAddSet);
            _btnRenameSet = MkBtn("✎",      OnRenameSet);
            _btnDeleteSet = MkBtn("削除",   OnDeleteSet);
            opRow.Add(_btnApplySet); opRow.Add(_btnAddSet); opRow.Add(_btnRenameSet); opRow.Add(_btnDeleteSet);
            dicSection.Add(opRow);

            // ファイル行
            var fileRow = new VisualElement();
            fileRow.style.flexDirection = FlexDirection.Row;
            fileRow.style.marginTop = 4; fileRow.style.height = 24;
            _btnSaveDic = MkBtn("辞書の保存",       OnSaveDicFile);
            _btnLoadDic = MkBtn("辞書ファイルを開く", OnLoadDicFile);
            fileRow.Add(_btnSaveDic); fileRow.Add(_btnLoadDic);
            dicSection.Add(fileRow);

            root.Add(dicSection);

            // ---- ステータス ----
            _statusLabel = new Label("");
            _statusLabel.style.marginTop = 2; _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            root.Add(_statusLabel);

            UpdateDicButtons();
        }

        // ================================================================
        // タブ切り替え
        // ================================================================

        private void SwitchTab(TabType tab)
        {
            _currentTab = tab;
            SetTabActive(_tabDrawable, tab == TabType.Drawable);
            SetTabActive(_tabBone,     tab == TabType.Bone);
            SetTabActive(_tabMorph,    tab == TabType.Morph);

            // フィルタフィールドを現タブの値に復元
            _filterField?.SetValueWithoutNotify(_filters[CurrentFilterIndex]);
            RebuildFilteredData();
        }

        private void SetTabActive(Button btn, bool active)
        {
            if (btn == null) return;
            btn.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
            btn.style.borderBottomWidth = active ? 2 : 0;
        }

        // ================================================================
        // フィルタ & リストビュー更新
        // ================================================================

        private void RebuildFilteredData()
        {
            _filteredData.Clear();
            var model = CurrentModel;
            if (model != null)
            {
                IReadOnlyList<IMeshView> source = _currentTab switch
                {
                    TabType.Drawable => model.DrawableList,
                    TabType.Bone     => model.BoneList,
                    TabType.Morph    => model.MorphList,
                    _ => null
                };
                string f = _filters[CurrentFilterIndex];
                bool hasFilter = !string.IsNullOrEmpty(f);
                if (source != null)
                {
                    foreach (var mv in source)
                    {
                        if (hasFilter && mv.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        _filteredData.Add(mv);
                    }
                }
            }
            RefreshMeshListView();
        }

        private void RefreshMeshListView()
        {
            if (_meshListView != null)
            {
                _meshListView.itemsSource = _filteredData;
                _meshListView.Rebuild();
            }
            int total = _currentTab switch
            {
                TabType.Drawable => CurrentModel?.DrawableCount ?? 0,
                TabType.Bone     => CurrentModel?.BoneCount ?? 0,
                TabType.Morph    => CurrentModel?.MorphCount ?? 0,
                _ => 0
            };
            if (_countLabel != null)
                _countLabel.text = _filteredData.Count == total
                    ? $"{total} items"
                    : $"{_filteredData.Count}/{total} items";
            SyncMeshListSelection();
            UpdateClipboardButtons();
        }

        private void RefreshSetListView()
        {
            if (_setListView == null) return;
            var sets = GetModelContext()?.MeshSelectionSets;
            _setListView.itemsSource = sets ?? (System.Collections.IList)new List<MeshSelectionSet>();
            _setListView.Rebuild();
            UpdateDicButtons();
        }

        // ================================================================
        // 選択同期
        // ================================================================

        private int[] GetCurrentSelectedIndices()
        {
            return _currentTab switch
            {
                TabType.Drawable => CurrentModel?.SelectedDrawableIndices ?? Array.Empty<int>(),
                TabType.Bone     => CurrentModel?.SelectedBoneIndices ?? Array.Empty<int>(),
                TabType.Morph    => CurrentModel?.SelectedMorphIndices ?? Array.Empty<int>(),
                _ => Array.Empty<int>()
            };
        }

        private void SyncMeshListSelection()
        {
            if (_meshListView == null || _isSyncingSelection) return;
            _isSyncingSelection = true;
            try
            {
                var selected = new HashSet<int>(GetCurrentSelectedIndices());
                var indices = new List<int>();
                for (int i = 0; i < _filteredData.Count; i++)
                    if (selected.Contains(_filteredData[i].MasterIndex))
                        indices.Add(i);
                _meshListView.SetSelectionWithoutNotify(indices);
            }
            finally { _isSyncingSelection = false; }
        }

        private void OnMeshListSelectionChanged(IEnumerable<object> _)
        {
            if (_isReceiving || _isSyncingSelection || _ctx == null) return;
            _isSyncingSelection = true;
            try
            {
                var indices = new List<int>();
                foreach (int i in _meshListView.selectedIndices)
                    if (i >= 0 && i < _filteredData.Count)
                        indices.Add(_filteredData[i].MasterIndex);
                SendCmd(new SelectMeshCommand(ModelIndex, CurrentCategory, indices.ToArray()));
            }
            finally { _isSyncingSelection = false; }
            UpdateClipboardButtons();
        }

        // ================================================================
        // ViewChanged
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (_isReceiving) return;
            _isReceiving = true;
            try
            {
                switch (kind)
                {
                    case ChangeKind.Selection:
                        SyncMeshListSelection();
                        UpdateClipboardButtons();
                        break;

                    case ChangeKind.Attributes:
                        RefreshSetListView();
                        SyncMeshListSelection();
                        break;

                    case ChangeKind.ListStructure:
                    case ChangeKind.ModelSwitch:
                    default:
                        RebuildFilteredData();
                        RefreshSetListView();
                        break;
                }
            }
            finally { EditorApplication.delayCall += () => _isReceiving = false; }
        }

        // ================================================================
        // ListView: MakeItem / BindItem
        // ================================================================

        private VisualElement MakeMeshItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = ITEM_HEIGHT;
            row.style.alignItems = Align.Center;

            var nameLbl = new Label { name = "name" };
            nameLbl.style.flexGrow = 1;
            nameLbl.style.overflow = Overflow.Hidden;
            nameLbl.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(nameLbl);

            var infoLbl = new Label { name = "info" };
            infoLbl.style.width = 70;
            infoLbl.style.fontSize = 10;
            infoLbl.style.unityTextAlign = TextAnchor.MiddleRight;
            infoLbl.style.color = new Color(0.6f, 0.6f, 0.6f);
            row.Add(infoLbl);

            return row;
        }

        private void BindMeshItem(VisualElement el, int index)
        {
            if (index < 0 || index >= _filteredData.Count) return;
            var mv = _filteredData[index];
            var nl = el.Q<Label>("name");
            if (nl != null) nl.text = mv.Name;
            var il = el.Q<Label>("info");
            if (il != null)
                il.text = _currentTab == TabType.Bone
                    ? $"Bone:{mv.BoneIndex}"
                    : (mv.VertexCount > 0 ? $"V:{mv.VertexCount} F:{mv.FaceCount}" : "");
        }

        private VisualElement MakeSetItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = ITEM_HEIGHT;
            row.style.alignItems = Align.Center;

            var nameLbl = new Label { name = "set-name" };
            nameLbl.style.flexGrow = 1;
            nameLbl.style.overflow = Overflow.Hidden;
            row.Add(nameLbl);

            var sumLbl = new Label { name = "set-sum" };
            sumLbl.style.width = 80;
            sumLbl.style.fontSize = 10;
            sumLbl.style.unityTextAlign = TextAnchor.MiddleRight;
            sumLbl.style.color = new Color(0.6f, 0.6f, 0.6f);
            row.Add(sumLbl);

            return row;
        }

        private void BindSetItem(VisualElement el, int index)
        {
            var sets = GetModelContext()?.MeshSelectionSets;
            if (sets == null || index < 0 || index >= sets.Count) return;
            var set = sets[index];
            var nl = el.Q<Label>("set-name"); if (nl != null) nl.text = set.Name;
            var sl = el.Q<Label>("set-sum");  if (sl != null) sl.text = set.Summary;
        }

        private void OnSetListSelectionChanged(IEnumerable<object> sel)
        {
            var list = sel?.ToList();
            var sets = GetModelContext()?.MeshSelectionSets;
            if (list != null && list.Count > 0 && sets != null)
                _selectedSetIndex = sets.IndexOf(list[0] as MeshSelectionSet);
            else
                _selectedSetIndex = -1;
            UpdateDicButtons();
        }

        // ================================================================
        // クリップボード操作
        // ================================================================

        private void OnCopy()
        {
            var mc = GetModelContext();
            if (mc == null) { SetStatus("ローカルモデルが必要です"); return; }
            var entries = BuildSelectedCsvEntries(mc);
            if (entries.Count == 0) { SetStatus("選択なし"); return; }
            GUIUtility.systemCopyBuffer = MeshClipboardHelper.SerializeEntriesToCsv(entries, mc);
            SetStatus($"コピー: {entries.Count} メッシュ");
        }

        private void OnCut()
        {
            var mc = GetModelContext();
            if (mc == null) { SetStatus("ローカルモデルが必要です"); return; }
            var entries = BuildSelectedCsvEntries(mc);
            if (entries.Count == 0) { SetStatus("選択なし"); return; }
            GUIUtility.systemCopyBuffer = MeshClipboardHelper.SerializeEntriesToCsv(entries, mc);
            // 削除は既存コマンドに委譲
            var masterIndices = entries
                .Select(e => e.GlobalIndex)
                .OrderByDescending(i => i)
                .ToArray();
            SendCmd(new DeleteMeshesCommand(ModelIndex, masterIndices));
            SetStatus($"切取り: {entries.Count} メッシュ");
        }

        private void OnPaste()
        {
            var mc = GetModelContext();
            if (mc == null) { SetStatus("ローカルモデルが必要です"); return; }
            string csv = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(csv)) { SetStatus("クリップボードが空です"); return; }
            List<CsvMeshEntry> entries;
            try { entries = MeshClipboardHelper.DeserializeFromCsv(csv); }
            catch (Exception ex) { SetStatus($"貼付け失敗: {ex.Message}"); return; }
            if (entries == null || entries.Count == 0) { SetStatus("有効なメッシュデータなし"); return; }
            MeshClipboardHelper.ResolveDuplicateNames(entries, mc);
            int added = MeshClipboardHelper.AddEntriesToModel(entries, mc);
            // 構造変更通知をコマンド経由で送る
            SendCmd(new NotifyListStructureChangedCommand(ModelIndex));
            SetStatus($"貼付け: {added} メッシュ");
        }

        private void OnSaveMeshCsv()
        {
            var mc = GetModelContext();
            if (mc == null) { SetStatus("ローカルモデルが必要です"); return; }
            var entries = BuildSelectedCsvEntries(mc);
            if (entries.Count == 0) { SetStatus("選択なし"); return; }
            if (MeshCsvIOHelper.SaveSelectedMeshesToCsv(entries, mc))
                SetStatus($"保存: {entries.Count} メッシュ");
            else
                SetStatus("保存キャンセルまたは失敗");
        }

        private void OnLoadMeshCsv()
        {
            var mc = GetModelContext();
            if (mc == null) { SetStatus("ローカルモデルが必要です"); return; }
            var entries = MeshCsvIOHelper.LoadMeshesFromCsv(mc);
            if (entries == null || entries.Count == 0) { SetStatus("有効なメッシュデータなし"); return; }
            SendCmd(new NotifyListStructureChangedCommand(ModelIndex));
            SetStatus($"読込み: {entries.Count} メッシュ");
        }

        // ================================================================
        // 選択辞書操作
        // ================================================================

        private void OnSaveSet()
        {
            if (CurrentModel == null) return;
            var selectedIndices = GetCurrentSelectedIndices();
            if (selectedIndices.Length == 0) { SetStatus("選択なし"); return; }

            // 選択中の IMeshView から名前を収集
            IReadOnlyList<IMeshView> source = _currentTab switch
            {
                TabType.Drawable => CurrentModel.DrawableList,
                TabType.Bone     => CurrentModel.BoneList,
                TabType.Morph    => CurrentModel.MorphList,
                _ => null
            };
            if (source == null) return;
            var selectedSet = new HashSet<int>(selectedIndices);
            var names = source
                .Where(mv => selectedSet.Contains(mv.MasterIndex) && !string.IsNullOrEmpty(mv.Name))
                .Select(mv => mv.Name)
                .Distinct()
                .ToArray();
            if (names.Length == 0) { SetStatus("選択メッシュに名前なし"); return; }

            string setName = _setNameField?.value?.Trim() ?? "";
            SendCmd(new SaveSelectionDictionaryCommand(ModelIndex, CurrentCategory, setName, names));
            _setNameField?.SetValueWithoutNotify("");
            SetStatus($"辞書化: {names.Length} メッシュ");
        }

        private void OnApplySet()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new ApplySelectionDictionaryCommand(ModelIndex, _selectedSetIndex, addToExisting: false));
            SetStatus("辞書を選択に適用");
        }

        private void OnAddSet()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new ApplySelectionDictionaryCommand(ModelIndex, _selectedSetIndex, addToExisting: true));
            SetStatus("辞書を選択に追加");
        }

        private void OnDeleteSet()
        {
            var sets = GetModelContext()?.MeshSelectionSets;
            if (sets == null || _selectedSetIndex < 0 || _selectedSetIndex >= sets.Count) return;
            string name = sets[_selectedSetIndex].Name;
            if (!EditorUtility.DisplayDialog("削除確認", $"'{name}' を削除しますか？", "削除", "キャンセル")) return;
            SendCmd(new DeleteSelectionDictionaryCommand(ModelIndex, _selectedSetIndex));
            _selectedSetIndex = -1;
            SetStatus($"削除: {name}");
        }

        private void OnRenameSet()
        {
            if (_selectedSetIndex < 0) return;
            string newName = _setNameField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(newName)) { SetStatus("新しい名前を入力してください"); return; }
            SendCmd(new RenameSelectionDictionaryCommand(ModelIndex, _selectedSetIndex, newName));
            _setNameField?.SetValueWithoutNotify("");
            SetStatus($"名前変更 → {newName}");
        }

        private void OnSaveDicFile()
        {
            var mc = GetModelContext();
            if (mc == null) { SetStatus("ローカルモデルが必要です"); return; }
            var sets = mc.MeshSelectionSets;
            if (sets == null || sets.Count == 0) { SetStatus("辞書なし"); return; }
            if (MeshDictionaryIOHelper.SaveDictionaryFile(sets, mc))
                SetStatus($"保存: {sets.Count} 辞書");
            else
                SetStatus("保存キャンセルまたは失敗");
        }

        private void OnLoadDicFile()
        {
            var mc = GetModelContext();
            if (mc == null) { SetStatus("ローカルモデルが必要です"); return; }
            var loaded = MeshDictionaryIOHelper.LoadDictionaryFile(mc);
            if (loaded == null || loaded.Count == 0) { SetStatus("辞書データなし"); return; }

            bool replace = false;
            if (mc.MeshSelectionSets.Count > 0)
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "辞書ファイルを開く",
                    $"読込み: {loaded.Count} 辞書。現在 {mc.MeshSelectionSets.Count} 辞書あり。\n処理方法:",
                    "すべて置換", "キャンセル", "追加（マージ）");
                if (choice == 1) return;
                replace = choice == 0;
            }
            if (replace) mc.MeshSelectionSets.Clear();
            foreach (var set in loaded)
            {
                if (mc.FindMeshSelectionSetByName(set.Name) != null)
                    set.Name = mc.GenerateUniqueMeshSelectionSetName(set.Name);
                mc.MeshSelectionSets.Add(set);
            }
            // Attributes 変更通知（リスト構造ではなく辞書メタ変更なので Attributes）
            SendCmd(new NotifyDictionaryChangedCommand(ModelIndex));
            SetStatus($"読込み: {loaded.Count} 辞書");
        }

        // ================================================================
        // ボタン有効状態更新
        // ================================================================

        private void UpdateClipboardButtons()
        {
            bool hasSel = _meshListView?.selectedIndices?.Any() ?? false;
            _btnCopy?.SetEnabled(hasSel);
            _btnCut?.SetEnabled(hasSel);
            _btnSaveMeshCsv?.SetEnabled(hasSel);
        }

        private void UpdateDicButtons()
        {
            bool hasSel = _selectedSetIndex >= 0;
            _btnApplySet?.SetEnabled(hasSel);
            _btnAddSet?.SetEnabled(hasSel);
            _btnRenameSet?.SetEnabled(hasSel);
            _btnDeleteSet?.SetEnabled(hasSel);
            var sets = GetModelContext()?.MeshSelectionSets;
            _btnSaveDic?.SetEnabled(sets != null && sets.Count > 0);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private List<CsvMeshEntry> BuildSelectedCsvEntries(ModelContext mc)
        {
            var result = new List<CsvMeshEntry>();
            if (mc == null || _meshListView == null) return result;
            foreach (var item in _meshListView.selectedItems)
            {
                if (item is LiveMeshView lv)
                {
                    var ctx = lv.Context;
                    if (ctx != null)
                        result.Add(new CsvMeshEntry { GlobalIndex = lv.MasterIndex, MeshContext = ctx });
                }
            }
            return result;
        }

        private void SendCmd(PanelCommand c) => _ctx?.SendCommand(c);
        private void SetStatus(string msg) { if (_statusLabel != null) _statusLabel.text = msg; }

        private static Button MkBtn(string label, Action click)
        {
            var b = new Button(click) { text = label };
            b.style.flexGrow = 1; b.style.minHeight = 22;
            return b;
        }

        private static Button MkTabBtn(string label)
        {
            var b = new Button { text = label };
            b.style.flexGrow = 1; b.style.minHeight = 22;
            return b;
        }
    }
}
