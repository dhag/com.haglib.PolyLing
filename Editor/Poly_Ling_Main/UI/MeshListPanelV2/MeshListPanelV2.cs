// MeshListPanelV2.cs
// TypedMeshListPanelの全機能をPanelContext/PanelCommand経由で実現
// MeshContext/ModelContext/ToolContextへの依存なし

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Poly_Ling.Data;
using UIList.UIToolkitExtensions;

namespace Poly_Ling.MeshListV2
{
    public class MeshListPanelV2 : EditorWindow
    {
        // ================================================================
        // UXML/USSパス（MeshListV2フォルダ、UIフォルダの外）
        // ================================================================

        private const string UxmlPackagePath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MeshListPanelV2/MeshListPanelV2.uxml";
        private const string UxmlAssetsPath  = "Assets/Editor/Poly_Ling_Main/UI/MeshListPanelV2/MeshListPanelV2.uxml";
        private const string UssPackagePath  = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MeshListPanelV2/MeshListPanelV2.uss";
        private const string UssAssetsPath   = "Assets/Editor/Poly_Ling_Main/UI/MeshListPanelV2/MeshListPanelV2.uss";

        private enum TabType { Drawable, Bone, Morph }

        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _ctx;
        private bool _isReceiving;

        // ================================================================
        // UI要素
        // ================================================================

        private Button _tabDrawable, _tabBone, _tabMorph;
        private VisualElement _mainContent, _morphEditor;
        private TreeView _treeView;
        private Label _countLabel, _statusLabel;
        private Toggle _showInfoToggle, _showMirrorSideToggle;
        private TextField _filterField;

        // モーフエディタ
        private Label _morphCountLabel, _morphStatusLabel;
        private ListView _morphListView;
        private Slider _morphTestWeight;
        private TextField _morphFilterField;

        // 変換セクション
        private VisualElement _morphSourceMeshPopupContainer, _morphParentPopupContainer, _morphPanelPopupContainer;
        private TextField _morphNameField;
        private Button _btnMeshToMorph, _btnMorphToMesh;
        private PopupField<int> _morphSourceMeshPopup, _morphParentPopup, _morphPanelPopup;

        // モーフセット
        private TextField _morphSetNameField;
        private VisualElement _morphSetTypePopupContainer;
        private PopupField<int> _morphSetTypePopup;
        private Button _btnCreateMorphSet;

        // 詳細パネル
        private Foldout _detailFoldout;
        private TextField _meshNameField;
        private Label _vertexCountLabel, _faceCountLabel, _triCountLabel, _quadCountLabel, _ngonCountLabel;
        private VisualElement _indexInfo;
        private Label _boneIndexLabel, _masterIndexLabel;

        // BonePose
        private VisualElement _bonePoseSection;
        private Foldout _poseFoldout, _bindposeFoldout;
        private Toggle _poseActiveToggle;
        private FloatField _restPosX, _restPosY, _restPosZ;
        private FloatField _restRotX, _restRotY, _restRotZ;
        private Slider _restRotSliderX, _restRotSliderY, _restRotSliderZ;
        private FloatField _restSclX, _restSclY, _restSclZ;
        private VisualElement _poseLayersContainer;
        private Label _poseNoLayersLabel;
        private Label _poseResultPos, _poseResultRot;
        private Button _btnInitPose, _btnResetLayers;
        private Label _bindposePos, _bindposeRot, _bindposeScl;
        private Button _btnBakePose;
        private bool _isSyncingPoseUI;

        // ================================================================
        // データ
        // ================================================================

        private TabType _currentTab = TabType.Drawable;
        private SummaryTreeRoot _treeRoot;
        private TreeViewDragDropHelper<SummaryTreeAdapter> _dragDropHelper;
        private List<SummaryTreeAdapter> _selectedAdapters = new List<SummaryTreeAdapter>();
        private bool _refreshScheduled;

        // モーフリスト
        private List<IMeshView> _morphListData = new List<IMeshView>();
        private List<IMeshView> _morphFilteredData = new List<IMeshView>();
        private bool _isSyncingMorphSelection;
        private bool _isMorphPreviewStarted;

        // TreeItemキャッシュ
        private class TreeItemCache
        {
            public Label NameLabel, InfoLabel;
            public Button VisBtn, LockBtn, SymBtn;
        }

        // ================================================================
        // プロパティ
        // ================================================================

        private MeshCategory CurrentCategory => _currentTab switch
        {
            TabType.Drawable => MeshCategory.Drawable,
            TabType.Bone => MeshCategory.Bone,
            TabType.Morph => MeshCategory.Morph,
            _ => MeshCategory.All
        };

        private int ModelIndex => _ctx?.CurrentView?.CurrentModelIndex ?? 0;
        private IModelView CurrentModel => _ctx?.CurrentView?.CurrentModel;

        // ================================================================
        // ウィンドウ
        // ================================================================

        //[MenuItem("Tools/Poly_Ling/debug/Mesh List V2")]
        public static void ShowWindow()
        {
            var window = GetWindow<MeshListPanelV2>();
            window.titleContent = new GUIContent("Mesh List V2");
            window.minSize = new Vector2(300, 400);
        }

        public static MeshListPanelV2 Open(PanelContext ctx)
        {
            var window = GetWindow<MeshListPanelV2>();
            window.titleContent = new GUIContent("Mesh List V2");
            window.minSize = new Vector2(300, 400);
            window.SetContext(ctx);
            window.Show();
            return window;
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
            SendEndMorphPreview();
            CleanupDragDrop();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            var vt = TryLoadAsset<VisualTreeAsset>(UxmlPackagePath, UxmlAssetsPath);
            if (vt != null) vt.CloneTree(root);
            else { root.Add(new Label($"UXML not found: {UxmlPackagePath}")); return; }

            var ss = TryLoadAsset<StyleSheet>(UssPackagePath, UssAssetsPath);
            if (ss != null) root.styleSheets.Add(ss);

            BindUIElements(root);
            SetupTreeView();
            RegisterButtonEvents();
            BindBonePoseUI(root);
            BindMorphEditorUI(root);
            SwitchTab(TabType.Drawable);

            if (_ctx?.CurrentView != null) OnViewChanged(_ctx.CurrentView, ChangeKind.ModelSwitch);
        }

        private static T TryLoadAsset<T>(string pkg, string assets) where T : UnityEngine.Object
        {
            T a = null;
            if (!string.IsNullOrEmpty(pkg)) a = AssetDatabase.LoadAssetAtPath<T>(pkg);
            if (a == null && !string.IsNullOrEmpty(assets)) a = AssetDatabase.LoadAssetAtPath<T>(assets);
            return a;
        }

        // ================================================================
        // UI構築
        // ================================================================

        private void BindUIElements(VisualElement root)
        {
            _tabDrawable = root.Q<Button>("tab-drawable");
            _tabBone = root.Q<Button>("tab-bone");
            _tabMorph = root.Q<Button>("tab-morph");
            _mainContent = root.Q<VisualElement>("main-content");
            _morphEditor = root.Q<VisualElement>("morph-editor");
            _treeView = root.Q<TreeView>("mesh-tree");
            _countLabel = root.Q<Label>("count-label");
            _showInfoToggle = root.Q<Toggle>("show-info-toggle");
            _showMirrorSideToggle = root.Q<Toggle>("show-mirror-toggle");
            _statusLabel = root.Q<Label>("status-label");
            _filterField = root.Q<TextField>("filter-field");

            _detailFoldout = root.Q<Foldout>("detail-foldout");
            _meshNameField = root.Q<TextField>("mesh-name-field");
            _vertexCountLabel = root.Q<Label>("vertex-count-label");
            _faceCountLabel = root.Q<Label>("face-count-label");
            _triCountLabel = root.Q<Label>("tri-count-label");
            _quadCountLabel = root.Q<Label>("quad-count-label");
            _ngonCountLabel = root.Q<Label>("ngon-count-label");
            _indexInfo = root.Q<VisualElement>("index-info");
            _boneIndexLabel = root.Q<Label>("bone-index-label");
            _masterIndexLabel = root.Q<Label>("master-index-label");

            _tabDrawable?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Drawable));
            _tabBone?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Bone));
            _tabMorph?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Morph));

            _showInfoToggle?.RegisterValueChangedCallback(_ => RefreshTree());
            _showMirrorSideToggle?.RegisterValueChangedCallback(_ => RefreshTreeImmediate());
            _filterField?.RegisterValueChangedCallback(_ => RefreshTreeImmediate());
            _meshNameField?.RegisterValueChangedCallback(OnNameFieldChanged);
        }

        // ================================================================
        // タブ切り替え
        // ================================================================

        private void SwitchTab(TabType tab)
        {
            if (_currentTab == TabType.Morph && tab != TabType.Morph) SendEndMorphPreview();
            _currentTab = tab;
            SetTabActive(_tabDrawable, tab == TabType.Drawable);
            SetTabActive(_tabBone, tab == TabType.Bone);
            SetTabActive(_tabMorph, tab == TabType.Morph);

            if (_indexInfo != null) _indexInfo.style.display = tab == TabType.Bone ? DisplayStyle.Flex : DisplayStyle.None;
            if (_bonePoseSection != null) _bonePoseSection.style.display = tab == TabType.Bone ? DisplayStyle.Flex : DisplayStyle.None;

            bool isMorph = tab == TabType.Morph;
            if (_mainContent != null) _mainContent.style.display = isMorph ? DisplayStyle.None : DisplayStyle.Flex;
            if (_morphEditor != null) _morphEditor.style.display = isMorph ? DisplayStyle.Flex : DisplayStyle.None;

            _selectedAdapters.Clear();
            if (!isMorph) CreateTreeRoot();
            if (isMorph) RefreshMorphEditor();
            RefreshAllImmediate();
            Log($"{tab} タブ");
        }

        private void SetTabActive(Button btn, bool active) => btn?.EnableInClassList("tab-active", active);

        // ================================================================
        // TreeView設定
        // ================================================================

        private void SetupTreeView()
        {
            if (_treeView == null) return;
            _treeView.fixedItemHeight = 20;
            _treeView.makeItem = MakeTreeItem;
            _treeView.bindItem = BindTreeItem;
            _treeView.selectionType = SelectionType.Multiple;
            _treeView.selectionChanged += OnSelectionChanged;
            _treeView.itemExpandedChanged += OnItemExpandedChanged;
        }

        private void CreateTreeRoot()
        {
            var model = CurrentModel;
            if (model == null) return;

            var sourceList = _currentTab switch
            {
                TabType.Drawable => model.DrawableList,
                TabType.Bone => model.BoneList,
                _ => null
            };
            if (sourceList == null) return;

            bool excludeMirror = !(_showMirrorSideToggle?.value ?? false);
            string filter = _filterField?.value;

            _treeRoot = new SummaryTreeRoot();
            _treeRoot.ModelIndex = ModelIndex;
            _treeRoot.SendCommand = cmd => _ctx?.SendCommand(cmd);
            _treeRoot.OnChanged = () =>
            {
                _isReceiving = true;
                try { RefreshTreeImmediate(); SyncTreeViewSelection(); UpdateDetailPanel(); }
                finally { _isReceiving = false; }
            };
            _treeRoot.Build(sourceList, CurrentCategory, excludeMirror, filter);
            SetupDragDrop();
        }

        // ================================================================
        // TreeView MakeItem / BindItem
        // ================================================================

        private VisualElement MakeTreeItem()
        {
            var c = new VisualElement();
            c.style.flexDirection = FlexDirection.Row;
            c.style.flexGrow = 1;
            c.style.alignItems = Align.Center;
            c.style.paddingLeft = 2;
            c.style.paddingRight = 4;

            var nameLabel = new Label { name = "name" };
            nameLabel.style.flexGrow = 1; nameLabel.style.flexShrink = 1;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.marginRight = 4;
            c.Add(nameLabel);

            var infoLabel = new Label { name = "info" };
            infoLabel.style.width = 80; infoLabel.style.flexShrink = 0;
            infoLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            infoLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            infoLabel.style.fontSize = 11; infoLabel.style.marginRight = 4;
            c.Add(infoLabel);

            var attr = new VisualElement();
            attr.style.flexDirection = FlexDirection.Row;
            attr.style.flexShrink = 0;
            var visBtn = MkAttrBtn("vis-btn", "\u0001F441", "可視性切り替え");
            var lockBtn = MkAttrBtn("lock-btn", "\u0001F512", "ロック切り替え");
            var symBtn = MkAttrBtn("sym-btn", "\u21C6", "対称切り替え");
            attr.Add(visBtn); attr.Add(lockBtn); attr.Add(symBtn);
            c.Add(attr);

            c.userData = new TreeItemCache { NameLabel = nameLabel, InfoLabel = infoLabel, VisBtn = visBtn, LockBtn = lockBtn, SymBtn = symBtn };
            return c;
        }

        private Button MkAttrBtn(string name, string icon, string tip)
        {
            var b = new Button { name = name, text = icon, tooltip = tip };
            b.style.width = 24; b.style.height = 18;
            b.style.marginLeft = 1; b.style.marginRight = 1;
            b.style.paddingLeft = 0; b.style.paddingRight = 0;
            b.style.paddingTop = 0; b.style.paddingBottom = 0;
            b.style.fontSize = 12;
            b.style.borderTopWidth = 0; b.style.borderBottomWidth = 0;
            b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
            b.style.backgroundColor = new Color(0, 0, 0, 0);
            return b;
        }

        private void BindTreeItem(VisualElement element, int index)
        {
            var adapter = _treeView.GetItemDataForIndex<SummaryTreeAdapter>(index);
            if (adapter == null) return;
            var cache = element.userData as TreeItemCache;
            if (cache == null) return;

            // 名前（ミラー情報付き）
            if (cache.NameLabel != null)
            {
                if (adapter.IsMirrorSide)
                { cache.NameLabel.text = $"\U0001FA9E {adapter.DisplayName}"; cache.NameLabel.style.opacity = 0.4f; }
                else if (adapter.IsRealSide)
                { cache.NameLabel.text = $"\u21C6 {adapter.DisplayName}"; cache.NameLabel.style.opacity = 1f; }
                else if (adapter.HasBakedMirrorChild)
                { cache.NameLabel.text = $"\u21C6B {adapter.DisplayName}"; cache.NameLabel.style.opacity = 1f; }
                else
                { cache.NameLabel.text = adapter.DisplayName; cache.NameLabel.style.opacity = 1f; }
            }

            // Info
            if (cache.InfoLabel != null)
            {
                bool showInfo = _showInfoToggle?.value ?? true;
                cache.InfoLabel.text = showInfo
                    ? (_currentTab == TabType.Bone ? $"Bone:{adapter.MeshView.BoneIndex}" : adapter.GetInfoString())
                    : "";
                cache.InfoLabel.style.display = showInfo ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // 属性ボタン
            BindAttrBtn(cache.VisBtn, adapter.IsVisible, "👁", "−",
                () => SendCmd(new ToggleVisibilityCommand(ModelIndex, adapter.MasterIndex)));
            BindAttrBtn(cache.LockBtn, adapter.IsLocked, "🔒", "🔓",
                () => SendCmd(new ToggleLockCommand(ModelIndex, adapter.MasterIndex)));

            if (cache.SymBtn != null)
            {
                bool show = _currentTab == TabType.Drawable;
                cache.SymBtn.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (show)
                {
                    string icon = adapter.GetMirrorTypeDisplay();
                    if (adapter.IsMirrorSide) icon = "\U0001FA9E";
                    else if (adapter.IsRealSide) icon = "\u21C6";
                    else if (adapter.HasBakedMirrorChild) icon = "\u21C6B";
                    BindAttrBtn(cache.SymBtn, adapter.HasMirrorIcon, icon, "",
                        () => SendCmd(new CycleMirrorTypeCommand(ModelIndex, adapter.MasterIndex)));
                }
            }
        }

        private void BindAttrBtn(Button btn, bool active, string onIcon, string offIcon, Action click)
        {
            if (btn == null) return;
            btn.text = active ? onIcon : offIcon;
            btn.style.opacity = active ? 1f : 0.3f;
            btn.clickable = new Clickable(click);
        }

        // ================================================================
        // 選択
        // ================================================================

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            if (_isReceiving || _ctx == null) return;
            _selectedAdapters.Clear();
            foreach (var item in selection)
                if (item is SummaryTreeAdapter a && !a.IsBakedMirror && !a.IsMirrorSide)
                    _selectedAdapters.Add(a);

            _isReceiving = true;
            try
            {
                var indices = _selectedAdapters.Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();
                SendCmd(new SelectMeshCommand(ModelIndex, CurrentCategory, indices));
            }
            finally { _isReceiving = false; }
            UpdateDetailPanel();
            UpdateBonePosePanel();
        }

        private void OnItemExpandedChanged(TreeViewExpansionChangedArgs args)
        {
            var a = _treeRoot?.FindById(args.id);
            if (a != null) a.IsExpanded = _treeView.IsExpanded(args.id);
        }

        private void SyncTreeViewSelection()
        {
            if (_treeView == null || _treeRoot == null || CurrentModel == null) return;
            int[] sel = _currentTab switch
            {
                TabType.Drawable => CurrentModel.SelectedDrawableIndices,
                TabType.Bone => CurrentModel.SelectedBoneIndices,
                _ => null
            };
            if (sel == null) { _treeView.ClearSelection(); return; }
            var ids = new List<int>();
            foreach (var idx in sel)
            {
                var a = _treeRoot.GetAdapterByMasterIndex(idx);
                if (a != null) ids.Add(a.Id);
            }
            _isReceiving = true;
            try { _treeView.SetSelectionWithoutNotify(ids); }
            finally { _isReceiving = false; }
        }

        // ================================================================
        // D&D
        // ================================================================

        private void SetupDragDrop()
        {
            CleanupDragDrop();
            if (_treeView == null || _treeRoot == null) return;
            _dragDropHelper = new TreeViewDragDropHelper<SummaryTreeAdapter>(_treeView, _treeRoot, new SummaryDragValidator());
            _dragDropHelper.Setup();
        }

        private void CleanupDragDrop()
        {
            _dragDropHelper?.Cleanup();
            _dragDropHelper = null;
        }

        // ================================================================
        // ボタンイベント
        // ================================================================

        private void RegisterButtonEvents()
        {
            var r = rootVisualElement;
            r.Q<Button>("btn-add")?.RegisterCallback<ClickEvent>(_ => OnAdd());
            r.Q<Button>("btn-up")?.RegisterCallback<ClickEvent>(_ => MoveSelected(-1));
            r.Q<Button>("btn-down")?.RegisterCallback<ClickEvent>(_ => MoveSelected(1));
            r.Q<Button>("btn-outdent")?.RegisterCallback<ClickEvent>(_ => OutdentSelected());
            r.Q<Button>("btn-indent")?.RegisterCallback<ClickEvent>(_ => IndentSelected());
            r.Q<Button>("btn-duplicate")?.RegisterCallback<ClickEvent>(_ => DuplicateSelected());
            r.Q<Button>("btn-delete")?.RegisterCallback<ClickEvent>(_ => DeleteSelected());
            r.Q<Button>("btn-show")?.RegisterCallback<ClickEvent>(_ => SetSelectedVisibility(true));
            r.Q<Button>("btn-hide")?.RegisterCallback<ClickEvent>(_ => SetSelectedVisibility(false));
            r.Q<Button>("btn-to-top")?.RegisterCallback<ClickEvent>(_ => MoveToEdge(true));
            r.Q<Button>("btn-to-bottom")?.RegisterCallback<ClickEvent>(_ => MoveToEdge(false));
        }

        private void OnNameFieldChanged(ChangeEvent<string> evt)
        {
            if (_isReceiving || _ctx == null) return;
            if (_selectedAdapters.Count == 1 && !string.IsNullOrEmpty(evt.newValue))
                SendCmd(new RenameMeshCommand(ModelIndex, _selectedAdapters[0].MasterIndex, evt.newValue));
        }

        private void OnAdd() => SendCmd(new AddMeshCommand(ModelIndex));

        private void MoveSelected(int dir)
        {
            if (_selectedAdapters.Count == 0 || _treeRoot == null) return;
            if (TreeViewHelper.MoveItems(_selectedAdapters, _treeRoot.RootItems, dir))
                _treeRoot.OnTreeChanged();
        }

        private void OutdentSelected()
        {
            if (_selectedAdapters.Count != 1 || _treeRoot == null) return;
            if (TreeViewHelper.Outdent(_selectedAdapters[0], _treeRoot.RootItems))
            {
                TreeViewHelper.RebuildParentReferences(_treeRoot.RootItems);
                _treeRoot.OnTreeChanged();
            }
        }

        private void IndentSelected()
        {
            if (_selectedAdapters.Count != 1 || _treeRoot == null) return;
            if (TreeViewHelper.Indent(_selectedAdapters[0], _treeRoot.RootItems))
            {
                TreeViewHelper.RebuildParentReferences(_treeRoot.RootItems);
                _treeRoot.OnTreeChanged();
            }
        }

        private void DuplicateSelected()
        {
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new DuplicateMeshesCommand(ModelIndex, SelIndices()));
        }

        private void DeleteSelected()
        {
            if (_selectedAdapters.Count == 0) return;
            string msg = _selectedAdapters.Count == 1
                ? $"'{_selectedAdapters[0].DisplayName}' を削除しますか？"
                : $"{_selectedAdapters.Count}個を削除しますか？";
            if (!EditorUtility.DisplayDialog("削除確認", msg, "削除", "キャンセル")) return;
            SendCmd(new DeleteMeshesCommand(ModelIndex, _selectedAdapters.OrderByDescending(a => a.MasterIndex).Select(a => a.MasterIndex).ToArray()));
            _selectedAdapters.Clear();
        }

        private void SetSelectedVisibility(bool visible)
        {
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new SetBatchVisibilityCommand(ModelIndex, SelIndices(), visible));
        }

        private void MoveToEdge(bool toTop)
        {
            if (_selectedAdapters.Count == 0 || _treeRoot == null) return;
            var item = _selectedAdapters[0];
            var siblings = item.Parent?.Children ?? _treeRoot.RootItems;
            int pos = siblings.IndexOf(item);
            if (toTop && pos > 0) { siblings.Remove(item); siblings.Insert(0, item); _treeRoot.OnTreeChanged(); }
            else if (!toTop && pos < siblings.Count - 1) { siblings.Remove(item); siblings.Add(item); _treeRoot.OnTreeChanged(); }
        }

        // ================================================================
        // Summary受信 → 全体更新
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
                        // 選択変更のみ：ツリー再構築不要
                        if (_currentTab != TabType.Morph)
                            SyncTreeViewSelection();
                        else
                            SyncMorphSel();
                        UpdateDetailPanel();
                        UpdateBonePosePanel();
                        break;

                    case ChangeKind.Attributes:
                        // 属性変更：既存ツリーを再バインドのみ
                        if (_currentTab != TabType.Morph)
                        {
                            _treeView?.RefreshItems();
                            SyncTreeViewSelection();
                        }
                        else
                        {
                            RefreshMorphEditor();
                        }
                        UpdateDetailPanel();
                        UpdateBonePosePanel();
                        break;

                    case ChangeKind.ListStructure:
                    case ChangeKind.ModelSwitch:
                    default:
                        // フルリビルド
                        if (_currentTab != TabType.Morph)
                        {
                            CreateTreeRoot();
                            RefreshAllImmediate();
                            SyncTreeViewSelection();
                        }
                        if (_currentTab == TabType.Morph) RefreshMorphEditor();
                        UpdateDetailPanel();
                        UpdateBonePosePanel();
                        break;
                }
            }
            finally { EditorApplication.delayCall += () => _isReceiving = false; }
        }

        // ================================================================
        // 更新
        // ================================================================

        private void RefreshAllImmediate() { RefreshTreeImmediate(); UpdateHeader(); UpdateDetailPanel(); }

        private void RefreshTree()
        {
            if (_treeView == null || _treeRoot == null || _refreshScheduled) return;
            _refreshScheduled = true;
            EditorApplication.delayCall += () => { _refreshScheduled = false; ApplyTreeToView(); };
        }

        private void RefreshTreeImmediate()
        {
            if (_treeView == null || _treeRoot == null) return;
            _refreshScheduled = false;
            ApplyTreeToView();
        }

        private void ApplyTreeToView()
        {
            if (_treeView == null || _treeRoot == null) return;
            _treeView.SetRootItems(TreeViewHelper.BuildTreeData(_treeRoot.RootItems));
            _treeView.Rebuild();
            RestoreExpanded(_treeRoot.RootItems);
        }

        private void RestoreExpanded(List<SummaryTreeAdapter> items)
        {
            foreach (var i in items)
            {
                if (i.IsExpanded) _treeView.ExpandItem(i.Id, false);
                if (i.HasChildren) RestoreExpanded(i.Children);
            }
        }

        private void UpdateHeader()
        {
            if (_countLabel == null) return;
            string label = _currentTab switch { TabType.Drawable => "メッシュ", TabType.Bone => "ボーン", _ => "モーフ" };
            _countLabel.text = $"{label}: {_treeRoot?.TotalCount ?? 0}";
        }

        // ================================================================
        // 詳細パネル
        // ================================================================

        private void UpdateDetailPanel()
        {
            if (_currentTab == TabType.Morph) return;
            if (_selectedAdapters.Count == 0)
            {
                _meshNameField?.SetValueWithoutNotify("");
                SL(_vertexCountLabel, "頂点: -"); SL(_faceCountLabel, "面: -");
                SL(_triCountLabel, "三角形: -"); SL(_quadCountLabel, "四角形: -"); SL(_ngonCountLabel, "多角形: -");
                SL(_boneIndexLabel, "ボーンIdx: -"); SL(_masterIndexLabel, "マスターIdx: -");
                _detailFoldout?.SetEnabled(false);
                return;
            }
            _detailFoldout?.SetEnabled(true);
            if (_selectedAdapters.Count == 1)
            {
                var s = _selectedAdapters[0].MeshView;
                _meshNameField?.SetValueWithoutNotify(s.Name); _meshNameField?.SetEnabled(true);
                SL(_vertexCountLabel, $"頂点: {s.VertexCount}"); SL(_faceCountLabel, $"面: {s.FaceCount}");
                SL(_triCountLabel, $"三角形: {s.TriCount}"); SL(_quadCountLabel, $"四角形: {s.QuadCount}"); SL(_ngonCountLabel, $"多角形: {s.NgonCount}");
                SL(_boneIndexLabel, $"ボーンIdx: {s.BoneIndex}"); SL(_masterIndexLabel, $"マスターIdx: {s.MasterIndex}");
            }
            else
            {
                _meshNameField?.SetValueWithoutNotify($"({_selectedAdapters.Count}個選択)"); _meshNameField?.SetEnabled(false);
                SL(_vertexCountLabel, $"頂点: {_selectedAdapters.Sum(a => a.VertexCount)} (合計)");
                SL(_faceCountLabel, $"面: {_selectedAdapters.Sum(a => a.FaceCount)} (合計)");
            }
        }

        // ================================================================
        // BonePose セクション
        // ================================================================

        private void BindBonePoseUI(VisualElement root)
        {
            _bonePoseSection = root.Q<VisualElement>("bone-pose-section");
            _poseFoldout = root.Q<Foldout>("pose-foldout");
            _bindposeFoldout = root.Q<Foldout>("bindpose-foldout");
            _poseActiveToggle = root.Q<Toggle>("pose-active-toggle");
            _restPosX = root.Q<FloatField>("rest-pos-x"); _restPosY = root.Q<FloatField>("rest-pos-y"); _restPosZ = root.Q<FloatField>("rest-pos-z");
            _restRotX = root.Q<FloatField>("rest-rot-x"); _restRotY = root.Q<FloatField>("rest-rot-y"); _restRotZ = root.Q<FloatField>("rest-rot-z");
            _restRotSliderX = root.Q<Slider>("rest-rot-slider-x"); _restRotSliderY = root.Q<Slider>("rest-rot-slider-y"); _restRotSliderZ = root.Q<Slider>("rest-rot-slider-z");
            _restSclX = root.Q<FloatField>("rest-scl-x"); _restSclY = root.Q<FloatField>("rest-scl-y"); _restSclZ = root.Q<FloatField>("rest-scl-z");
            _poseLayersContainer = root.Q<VisualElement>("pose-layers-container");
            _poseNoLayersLabel = root.Q<Label>("pose-no-layers-label");
            _poseResultPos = root.Q<Label>("pose-result-pos"); _poseResultRot = root.Q<Label>("pose-result-rot");
            _btnInitPose = root.Q<Button>("btn-init-pose"); _btnResetLayers = root.Q<Button>("btn-reset-layers");
            _bindposePos = root.Q<Label>("bindpose-pos"); _bindposeRot = root.Q<Label>("bindpose-rot"); _bindposeScl = root.Q<Label>("bindpose-scl");
            _btnBakePose = root.Q<Button>("btn-bake-pose");

            _poseActiveToggle?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                SendCmd(new SetBonePoseActiveCommand(ModelIndex, SelIndices(), e.newValue));
            });

            RegRest(_restPosX, SetBonePoseRestValueCommand.Field.PositionX);
            RegRest(_restPosY, SetBonePoseRestValueCommand.Field.PositionY);
            RegRest(_restPosZ, SetBonePoseRestValueCommand.Field.PositionZ);
            RegRest(_restSclX, SetBonePoseRestValueCommand.Field.ScaleX);
            RegRest(_restSclY, SetBonePoseRestValueCommand.Field.ScaleY);
            RegRest(_restSclZ, SetBonePoseRestValueCommand.Field.ScaleZ);

            RegRotField(_restRotX, _restRotSliderX, SetBonePoseRestValueCommand.Field.RotationX);
            RegRotField(_restRotY, _restRotSliderY, SetBonePoseRestValueCommand.Field.RotationY);
            RegRotField(_restRotZ, _restRotSliderZ, SetBonePoseRestValueCommand.Field.RotationZ);

            RegRotSlider(_restRotSliderX, _restRotX, SetBonePoseRestValueCommand.Field.RotationX);
            RegRotSlider(_restRotSliderY, _restRotY, SetBonePoseRestValueCommand.Field.RotationY);
            RegRotSlider(_restRotSliderZ, _restRotZ, SetBonePoseRestValueCommand.Field.RotationZ);

            _btnInitPose?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new InitBonePoseCommand(ModelIndex, i)); });
            _btnResetLayers?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new ResetBonePoseLayersCommand(ModelIndex, i)); });
            _btnBakePose?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new BakePoseToBindPoseCommand(ModelIndex, i)); });
        }

        private void RegRest(FloatField f, SetBonePoseRestValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new SetBonePoseRestValueCommand(ModelIndex, i, tf, e.newValue));
            });
        }

        private void RegRotField(FloatField f, Slider s, SetBonePoseRestValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new SetBonePoseRestValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingPoseUI = true;
                try { s?.SetValueWithoutNotify(NormAngle(e.newValue)); } finally { _isSyncingPoseUI = false; }
            });
        }

        private void RegRotSlider(Slider s, FloatField f, SetBonePoseRestValueCommand.Field tf)
        {
            s?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new BeginBonePoseSliderDragCommand(ModelIndex, i));
                SendCmd(new SetBonePoseRestValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingPoseUI = true;
                try { f?.SetValueWithoutNotify((float)System.Math.Round(e.newValue, 4)); } finally { _isSyncingPoseUI = false; }
            });
            s?.RegisterCallback<PointerCaptureOutEvent>(_ =>
                SendCmd(new EndBonePoseSliderDragCommand(ModelIndex, "ボーン回転変更")));
        }

        private void UpdateBonePosePanel()
        {
            if (_bonePoseSection == null || _currentTab != TabType.Bone) return;
            _isSyncingPoseUI = true;
            try
            {
                if (_selectedAdapters.Count == 0) { SetPoseEmpty(); return; }
                var poses = _selectedAdapters.Select(a => a.MeshView.BonePose).Where(bp => bp.HasPose).ToList();
                bool all = poses.Count == _selectedAdapters.Count, none = poses.Count == 0;

                // Active
                if (all) { bool f = poses[0].IsActive; bool same = poses.TrueForAll(p => p.IsActive == f); _poseActiveToggle?.SetValueWithoutNotify(same ? f : false); SMV(_poseActiveToggle, !same); }
                else { _poseActiveToggle?.SetValueWithoutNotify(false); SMV(_poseActiveToggle, !none); }
                _poseActiveToggle?.SetEnabled(true);

                if (all && poses.Count > 0)
                {
                    MixF(_restPosX, poses, p => p.RestPosition.x); MixF(_restPosY, poses, p => p.RestPosition.y); MixF(_restPosZ, poses, p => p.RestPosition.z);
                    MixR(_restRotX, _restRotSliderX, poses, p => p.RestRotationEuler.x); MixR(_restRotY, _restRotSliderY, poses, p => p.RestRotationEuler.y); MixR(_restRotZ, _restRotSliderZ, poses, p => p.RestRotationEuler.z);
                    MixF(_restSclX, poses, p => p.RestScale.x); MixF(_restSclY, poses, p => p.RestScale.y); MixF(_restSclZ, poses, p => p.RestScale.z);
                }
                else
                {
                    SF(_restPosX, 0, false); SF(_restPosY, 0, false); SF(_restPosZ, 0, false);
                    SF(_restRotX, 0, false); SF(_restRotY, 0, false); SF(_restRotZ, 0, false);
                    SS(_restRotSliderX, 0, false); SS(_restRotSliderY, 0, false); SS(_restRotSliderZ, 0, false);
                    SF(_restSclX, 1, false); SF(_restSclY, 1, false); SF(_restSclZ, 1, false);
                }

                // レイヤー・結果（単一選択のみ）
                var single = (_selectedAdapters.Count == 1 && all) ? poses[0] : null;
                UpdateLayers(single);
                if (single != null)
                {
                    var bp = single;
                    SL(_poseResultPos, $"Pos: ({bp.ResultPosition.x:F3}, {bp.ResultPosition.y:F3}, {bp.ResultPosition.z:F3})");
                    SL(_poseResultRot, $"Rot: ({bp.ResultRotationEuler.x:F1}, {bp.ResultRotationEuler.y:F1}, {bp.ResultRotationEuler.z:F1})");
                }
                else { string m = _selectedAdapters.Count > 1 ? "(複数選択)" : "-"; SL(_poseResultPos, $"Pos: {m}"); SL(_poseResultRot, $"Rot: {m}"); }

                _btnInitPose?.SetEnabled(false); if (_btnInitPose != null) _btnInitPose.style.display = DisplayStyle.None;
                _btnResetLayers?.SetEnabled(all && poses.Any(p => p.LayerCount > 0));

                if (_selectedAdapters.Count == 1 && all)
                {
                    var bp = poses[0];
                    SL(_bindposePos, $"Pos: ({bp.BindPosePosition.x:F3}, {bp.BindPosePosition.y:F3}, {bp.BindPosePosition.z:F3})");
                    SL(_bindposeRot, $"Rot: ({bp.BindPoseRotationEuler.x:F1}, {bp.BindPoseRotationEuler.y:F1}, {bp.BindPoseRotationEuler.z:F1})");
                    SL(_bindposeScl, $"Scl: ({bp.BindPoseScale.x:F3}, {bp.BindPoseScale.y:F3}, {bp.BindPoseScale.z:F3})");
                }
                else { string m = _selectedAdapters.Count > 1 ? "(複数選択)" : "-"; SL(_bindposePos, $"Pos: {m}"); SL(_bindposeRot, $"Rot: {m}"); SL(_bindposeScl, $"Scl: {m}"); }
                _btnBakePose?.SetEnabled(all);
            }
            finally { _isSyncingPoseUI = false; }
        }

        private void SetPoseEmpty()
        {
            _poseActiveToggle?.SetValueWithoutNotify(false); _poseActiveToggle?.SetEnabled(false); SMV(_poseActiveToggle, false);
            SF(_restPosX,0,false); SF(_restPosY,0,false); SF(_restPosZ,0,false);
            SF(_restRotX,0,false); SF(_restRotY,0,false); SF(_restRotZ,0,false);
            SS(_restRotSliderX,0,false); SS(_restRotSliderY,0,false); SS(_restRotSliderZ,0,false);
            SF(_restSclX,1,false); SF(_restSclY,1,false); SF(_restSclZ,1,false);
            UpdateLayers(null);
            SL(_poseResultPos,"Pos: -"); SL(_poseResultRot,"Rot: -");
            _btnInitPose?.SetEnabled(false); if(_btnInitPose!=null)_btnInitPose.style.display=DisplayStyle.None;
            _btnResetLayers?.SetEnabled(false);
            SL(_bindposePos,"Pos: -"); SL(_bindposeRot,"Rot: -"); SL(_bindposeScl,"Scl: -");
            _btnBakePose?.SetEnabled(false);
        }

        private void UpdateLayers(IBonePoseView pose)
        {
            if (_poseLayersContainer == null) return;
            var rm = _poseLayersContainer.Children().Where(c => c.ClassListContains("pose-layer-row")).ToList();
            foreach (var e in rm) _poseLayersContainer.Remove(e);
            bool has = pose != null && pose.LayerCount > 0;
            if (_poseNoLayersLabel != null) _poseNoLayersLabel.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            if (has)
            {
                var row = new VisualElement(); row.AddToClassList("pose-layer-row");
                row.Add(new Label($"({pose.LayerCount} layers)") { style = { fontSize = 11 } });
                _poseLayersContainer.Add(row);
            }
        }

        // ================================================================
        // モーフエディタ
        // ================================================================

        private void BindMorphEditorUI(VisualElement root)
        {
            _morphCountLabel = root.Q<Label>("morph-count-label");
            _morphStatusLabel = root.Q<Label>("morph-status-label");
            _morphNameField = root.Q<TextField>("morph-name-field");
            _morphTestWeight = root.Q<Slider>("morph-test-weight");
            _morphFilterField = root.Q<TextField>("morph-filter-field");
            _morphListView = root.Q<ListView>("morph-listview");
            if (_morphListView != null)
            {
                _morphListView.makeItem = MorphMake;
                _morphListView.bindItem = MorphBind;
                _morphListView.fixedItemHeight = 20;
                _morphListView.itemsSource = _morphFilteredData;
                _morphListView.selectionType = SelectionType.Multiple;
                _morphListView.selectionChanged += OnMorphSel;
            }
            _morphSourceMeshPopupContainer = root.Q<VisualElement>("morph-source-mesh-container");
            _morphParentPopupContainer = root.Q<VisualElement>("morph-parent-container");
            _morphPanelPopupContainer = root.Q<VisualElement>("morph-panel-container");
            _morphSetNameField = root.Q<TextField>("morph-set-name-field");
            _morphSetTypePopupContainer = root.Q<VisualElement>("morph-set-type-container");
            _btnMeshToMorph = root.Q<Button>("btn-mesh-to-morph");
            _btnMorphToMesh = root.Q<Button>("btn-morph-to-mesh");
            _btnCreateMorphSet = root.Q<Button>("btn-create-morph-set");
            _btnMeshToMorph?.RegisterCallback<ClickEvent>(_ => OnMeshToMorph());
            _btnMorphToMesh?.RegisterCallback<ClickEvent>(_ => OnMorphToMesh());
            _btnCreateMorphSet?.RegisterCallback<ClickEvent>(_ => OnCreateMorphSet());
            root.Q<Button>("btn-morph-test-reset")?.RegisterCallback<ClickEvent>(_ => OnMorphTestReset());
            root.Q<Button>("btn-morph-test-select-all")?.RegisterCallback<ClickEvent>(_ => OnMorphSelAll(true));
            root.Q<Button>("btn-morph-test-deselect-all")?.RegisterCallback<ClickEvent>(_ => OnMorphSelAll(false));
            _morphTestWeight?.RegisterValueChangedCallback(OnMorphWeight);
            _morphFilterField?.RegisterValueChangedCallback(_ => RefreshMorphListData());
        }

        private VisualElement MorphMake()
        {
            var r = new VisualElement(); r.AddToClassList("morph-list-row");
            r.Add(new Label { name = "n" }); r.Q<Label>("n").AddToClassList("morph-list-name");
            var il = new Label { name = "i" }; il.AddToClassList("morph-list-info"); r.Add(il);
            return r;
        }

        private void MorphBind(VisualElement el, int idx)
        {
            if (idx < 0 || idx >= _morphFilteredData.Count) return;
            var s = _morphFilteredData[idx];
            var nl = el.Q<Label>("n"); if (nl != null) nl.text = s.Name;
            var il = el.Q<Label>("i");
            if (il != null)
            {
                if (s.MorphParentIndex >= 0) { var pn = FindDrawableName(s.MorphParentIndex); il.text = pn != null ? $"→{pn}" : $"→[{s.MorphParentIndex}]"; }
                else if (!string.IsNullOrEmpty(s.MorphName)) il.text = s.MorphName;
                else il.text = "";
            }
        }

        private void RefreshMorphEditor()
        {
            if (CurrentModel == null) return;
            RefreshMorphListData();
            RefreshMorphConvert();
            RefreshMorphSet();
        }

        private void RefreshMorphListData()
        {
            _morphListData.Clear(); _morphFilteredData.Clear();
            if (CurrentModel?.MorphList != null) foreach (var s in CurrentModel.MorphList) _morphListData.Add(s);
            string f = _morphFilterField?.value;
            foreach (var s in _morphListData)
                if (string.IsNullOrEmpty(f) || s.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    _morphFilteredData.Add(s);
            if (_morphCountLabel != null) _morphCountLabel.text = $"モーフ: {_morphFilteredData.Count}";
            _morphListView?.RefreshItems();
            SyncMorphSel();
        }

        private void OnMorphSel(IEnumerable<object> _)
        {
            if (_isSyncingMorphSelection || _isReceiving || _ctx == null) return;
            if (_isMorphPreviewStarted) { SendEndMorphPreview(); _morphTestWeight?.SetValueWithoutNotify(0f); }
            var ids = new List<int>();
            foreach (int i in _morphListView.selectedIndices)
                if (i >= 0 && i < _morphFilteredData.Count) ids.Add(_morphFilteredData[i].MasterIndex);
            SendCmd(new SelectMeshCommand(ModelIndex, MeshCategory.Morph, ids.ToArray()));
        }

        private void SyncMorphSel()
        {
            if (_morphListView == null || CurrentModel == null) return;
            _isSyncingMorphSelection = true;
            try
            {
                var set = new HashSet<int>(CurrentModel.SelectedMorphIndices ?? Array.Empty<int>());
                var li = new List<int>();
                for (int i = 0; i < _morphFilteredData.Count; i++)
                    if (set.Contains(_morphFilteredData[i].MasterIndex)) li.Add(i);
                _morphListView.SetSelectionWithoutNotify(li);
            }
            finally { _isSyncingMorphSelection = false; }
        }

        private void RefreshMorphConvert()
        {
            if (CurrentModel == null) return;
            var dc = BuildDrawableChoices();
            RebuildPopup(ref _morphSourceMeshPopup, _morphSourceMeshPopupContainer, dc, "morph-popup");
            RebuildPopup(ref _morphParentPopup, _morphParentPopupContainer, dc, "morph-popup");
            RebuildPopup(ref _morphPanelPopup, _morphPanelPopupContainer,
                new List<(int,string)>{(0,"眉"),(1,"目"),(2,"口"),(3,"その他")}, "morph-popup", 3);
        }

        private List<(int,string)> BuildDrawableChoices()
        {
            var c = new List<(int,string)>();
            if (CurrentModel?.DrawableList == null) return c;
            foreach (var s in CurrentModel.DrawableList) c.Add((s.MasterIndex, $"[{s.MasterIndex}] {s.Name}"));
            return c;
        }

        private void RebuildPopup(ref PopupField<int> popup, VisualElement container, List<(int,string)> opts, string css, int def = -1)
        {
            if (container == null) return; container.Clear();
            var ids = new List<int>{-1}; var dm = new Dictionary<int,string>{{-1,"(なし)"}};
            foreach (var (i,n) in opts) { ids.Add(i); dm[i] = n; }
            int init = ids.Contains(def) ? def : -1;
            popup = new PopupField<int>(ids, init, v => dm.TryGetValue(v, out var s) ? s : v.ToString(), v => dm.TryGetValue(v, out var s) ? s : v.ToString());
            popup.AddToClassList(css); popup.style.flexGrow = 1; container.Add(popup);
        }

        private void RefreshMorphSet()
        {
            if (CurrentModel == null) return;
            RebuildPopup(ref _morphSetTypePopup, _morphSetTypePopupContainer,
                new List<(int,string)>{(1,"Vertex"),(3,"UV")}, "morph-popup", 1);
        }

        private void OnMeshToMorph()
        {
            int src = _morphSourceMeshPopup?.value ?? -1;
            int par = _morphParentPopup?.value ?? -1;
            string nm = _morphNameField?.value?.Trim() ?? "";
            int pan = _morphPanelPopup?.value ?? 3;
            if (src < 0) { ML("対象メッシュを選択してください"); return; }
            SendEndMorphPreview();
            SendCmd(new ConvertMeshToMorphCommand(ModelIndex, src, par, nm, pan));
        }

        private void OnMorphToMesh()
        {
            if (CurrentModel == null) return;
            var ids = CurrentModel.SelectedMorphIndices;
            if (ids == null || ids.Length == 0) { ML("モーフが選択されていません"); return; }
            SendEndMorphPreview(); _morphTestWeight?.SetValueWithoutNotify(0f);
            SendCmd(new ConvertMorphToMeshCommand(ModelIndex, ids));
        }

        private void OnCreateMorphSet()
        {
            if (CurrentModel == null) return;
            string nm = _morphSetNameField?.value?.Trim() ?? "";
            int ty = _morphSetTypePopup?.value ?? 1;
            var mi = CurrentModel.SelectedMorphIndices;
            if (mi == null || mi.Length == 0) { ML("モーフが選択されていません"); return; }
            SendCmd(new CreateMorphSetCommand(ModelIndex, nm, ty, mi));
        }

        private void OnMorphWeight(ChangeEvent<float> e)
        {
            if (_isReceiving || _ctx == null || CurrentModel == null) return;
            var mi = CurrentModel.SelectedMorphIndices;
            if (mi == null || mi.Length == 0) return;
            if (!_isMorphPreviewStarted) { SendCmd(new StartMorphPreviewCommand(ModelIndex, mi)); _isMorphPreviewStarted = true; }
            SendCmd(new ApplyMorphPreviewCommand(ModelIndex, e.newValue));
        }

        private void OnMorphTestReset() { SendEndMorphPreview(); _morphTestWeight?.SetValueWithoutNotify(0f); }

        private void OnMorphSelAll(bool sel)
        {
            if (CurrentModel == null) return;
            SendEndMorphPreview(); _morphTestWeight?.SetValueWithoutNotify(0f);
            if (sel) SendCmd(new SelectAllMorphsCommand(ModelIndex, _morphFilteredData.Select(s => s.MasterIndex).ToArray()));
            else SendCmd(new DeselectAllMorphsCommand(ModelIndex));
        }

        private void SendEndMorphPreview()
        {
            if (_ctx != null && _isMorphPreviewStarted) SendCmd(new EndMorphPreviewCommand(ModelIndex));
            _isMorphPreviewStarted = false;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void SendCmd(PanelCommand c) => _ctx?.SendCommand(c);
        private int[] SelIndices() => _selectedAdapters.Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();

        private string FindDrawableName(int mi)
        {
            if (CurrentModel?.DrawableList != null) foreach (var s in CurrentModel.DrawableList) if (s.MasterIndex == mi) return s.Name;
            if (CurrentModel?.BoneList != null) foreach (var s in CurrentModel.BoneList) if (s.MasterIndex == mi) return s.Name;
            return null;
        }

        private void SL(Label l, string t) { if (l != null) l.text = t; }
        private void Log(string m) { if (_statusLabel != null) _statusLabel.text = m; }
        private void ML(string m) { if (_morphStatusLabel != null) _morphStatusLabel.text = m; Log(m); }

        private static float NormAngle(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a < -180f) a += 360f; return a; }
        private static void SMV(Toggle t, bool m) { if (t != null) t.showMixedValue = m; }

        private void MixF(FloatField f, List<IBonePoseView> ps, Func<IBonePoseView,float> g)
        {
            if (f == null) return;
            float v0 = g(ps[0]); bool same = ps.TrueForAll(p => Mathf.Abs(g(p) - v0) < 0.0001f);
            f.SetValueWithoutNotify(same ? (float)System.Math.Round(v0, 4) : 0f);
            f.showMixedValue = !same; f.SetEnabled(true);
        }

        private void MixR(FloatField f, Slider s, List<IBonePoseView> ps, Func<IBonePoseView,float> g)
        {
            if (f == null) return;
            float v0 = g(ps[0]); bool same = ps.TrueForAll(p => Mathf.Abs(g(p) - v0) < 0.01f);
            float v = same ? v0 : 0f;
            f.SetValueWithoutNotify((float)System.Math.Round(v, 4)); f.showMixedValue = !same; f.SetEnabled(true);
            if (s != null) { s.SetValueWithoutNotify(same ? NormAngle(v) : 0f); s.SetEnabled(same); }
        }

        private static void SF(FloatField f, float v, bool e)
        {
            if (f == null) return;
            f.SetValueWithoutNotify((float)System.Math.Round(v, 4)); f.showMixedValue = false; f.SetEnabled(e);
        }

        private static void SS(Slider s, float v, bool e) { if (s != null) { s.SetValueWithoutNotify(v); s.SetEnabled(e); } }
    }

    public class SummaryDragValidator : IDragDropValidator<SummaryTreeAdapter>
    {
        public bool CanDrag(SummaryTreeAdapter item) => true;
        public bool CanDrop(SummaryTreeAdapter dragged, SummaryTreeAdapter target, DropPosition position) => true;
    }
}
