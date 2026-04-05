// MeshListSubPanel.cs
// MeshListPanelV2 のランタイムポート。
// エディタ依存APIを以下のように置換:
//   EditorApplication.delayCall     → _root.schedule.Execute
//   EditorUtility.DisplayDialog     → 確認なし即実行
//   PopupField<int>                 → DropdownField + インデックス変換
//   AssetDatabase / UXML / USS      → コードによる UI 構築
//   EditorWindow / CreateGUI        → Build(VisualElement) + SetContext
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.View;
using UIList.UIToolkitExtensions;

namespace Poly_Ling.MeshListV2
{
    public class MeshListSubPanel
    {
        private enum TabType { Drawable, Bone, Morph }

        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _ctx;
        private bool _isReceiving;

        // ================================================================
        // UI要素（エディタ版と同名）
        // ================================================================

        private VisualElement _root;
        private Button _tabDrawable, _tabBone, _tabMorph;
        private VisualElement _mainContent, _morphEditor;
        private TreeView _treeView;
        private Label _countLabel, _statusLabel;
        private Toggle _showInfoToggle, _showMirrorSideToggle;
        private TextField _filterField;

        private Foldout _detailFoldout;
        private TextField _meshNameField;
        private Label _vertexCountLabel, _faceCountLabel, _triCountLabel, _quadCountLabel, _ngonCountLabel;
        private VisualElement _indexInfo;
        private Label _boneIndexLabel, _masterIndexLabel;

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

        // 詳細モード切り替え（エディタ版「detail-mode-toggle」＝「スキンドメッシュ」に名称変更）
        private Toggle _detailModeToggle;
        private VisualElement _tabHeader;

        private Foldout _transformFoldout;
        private FloatField _localPosX, _localPosY, _localPosZ;
        private FloatField _localRotX, _localRotY, _localRotZ;
        private Slider _localRotSliderX, _localRotSliderY, _localRotSliderZ;
        private FloatField _localSclX, _localSclY, _localSclZ;
        private bool _isSyncingTransformUI;

        // モーフエディタ
        private Label _morphCountLabel, _morphStatusLabel;
        private ListView _morphListView;
        private Slider _morphTestWeight;
        private TextField _morphFilterField;
        private VisualElement _morphSourceMeshPopupContainer, _morphParentPopupContainer, _morphPanelPopupContainer;
        private TextField _morphNameField;
        private Button _btnMeshToMorph, _btnMorphToMesh;
        // PopupField<int> の代替：DropdownField + マスターインデックスリスト
        private DropdownField _morphSourceMeshDropdown, _morphParentDropdown, _morphPanelDropdown;
        private List<int> _morphSourceMeshIds = new List<int>();
        private List<int> _morphParentIds     = new List<int>();
        private VisualElement _morphSetTypePopupContainer;
        private TextField _morphSetNameField;
        private DropdownField _morphSetTypeDropdown;
        private Button _btnCreateMorphSet;

        // ================================================================
        // データ
        // ================================================================

        private TabType _currentTab = TabType.Drawable;
        private SummaryTreeRoot _treeRoot;
        private TreeViewDragDropHelper<SummaryTreeAdapter> _dragDropHelper;
        private List<SummaryTreeAdapter> _selectedAdapters = new List<SummaryTreeAdapter>();
        private bool _refreshScheduled;

        private List<IMeshView> _morphListData     = new List<IMeshView>();
        private List<IMeshView> _morphFilteredData = new List<IMeshView>();
        private bool _isSyncingMorphSelection;
        private bool _isMorphPreviewStarted;

        private class TreeItemCache
        {
            public Label NameLabel, InfoLabel;
            public Button VisBtn, LockBtn, SymBtn;
        }

        // ================================================================
        // プロパティ（エディタ版と同一）
        // ================================================================

        private MeshCategory CurrentCategory => _currentTab switch
        {
            TabType.Drawable => MeshCategory.Drawable,
            TabType.Bone     => MeshCategory.Bone,
            TabType.Morph    => MeshCategory.Morph,
            _                => MeshCategory.All
        };

        private bool IsSimpleMode => !(_detailModeToggle?.value ?? false);
        private int ModelIndex    => _ctx?.CurrentView?.CurrentModelIndex ?? 0;
        private IModelView CurrentModel => _ctx?.CurrentView?.CurrentModel;

        // ================================================================
        // Build / SetContext
        // ================================================================

        public void Build(VisualElement parent)
        {
            parent.Clear();
            _root = parent;
            BuildUI(parent);
            SetupTreeView();
            RegisterButtonEvents();
            BindBonePoseUI(parent);
            BindTransformUI(parent);
            BindMorphEditorUI(parent);
            SwitchTab(TabType.Drawable);
        }

        public void SetContext(PanelContext ctx)
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
            _ctx = ctx;
            if (_ctx != null)
            {
                _ctx.OnViewChanged += OnViewChanged;
                if (_ctx.CurrentView != null) OnViewChanged(_ctx.CurrentView, ChangeKind.ModelSwitch);
            }
        }

        public void Detach()
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
            SendEndMorphPreview();
            CleanupDragDrop();
        }

        // ================================================================
        // UI構築（エディタ版はUXMLだが、ここではコードで同等構造を構築）
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft = 4; root.style.paddingRight  = 4;
            root.style.paddingTop  = 4; root.style.paddingBottom = 4;

            // ── パネル名
            var panelNameLabel = new Label("オブジェクトリスト");
            panelNameLabel.style.color = new StyleColor(Color.white);
            panelNameLabel.style.fontSize = 12;
            panelNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            panelNameLabel.style.marginBottom = 3;
            root.Add(panelNameLabel);

            // ── スキンドメッシュ（= 詳細モード）トグル
            _detailModeToggle = new Toggle("スキンドメッシュ") { value = false, name = "detail-mode-toggle" };
            _detailModeToggle.style.color = new StyleColor(Color.white);
            _detailModeToggle.style.marginBottom = 3;
            root.Add(_detailModeToggle);

            // ── タブヘッダー（詳細モード時のみ表示）
            _tabHeader = new VisualElement { name = "tab-header" };
            _tabHeader.style.flexDirection = FlexDirection.Row;
            _tabHeader.style.marginBottom  = 3;
            _tabDrawable = MakeTabBtn("Mesh",  "tab-drawable");
            _tabBone     = MakeTabBtn("Bone",  "tab-bone");
            _tabMorph    = MakeTabBtn("Morph", "tab-morph");
            _tabHeader.Add(_tabDrawable); _tabHeader.Add(_tabBone); _tabHeader.Add(_tabMorph);
            root.Add(_tabHeader);

            // ── カウント・フィルター行
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems    = Align.Center;
            topRow.style.marginBottom  = 3;

            _countLabel = new Label { name = "count-label" };
            _countLabel.style.color = new StyleColor(Color.white);
            topRow.Add(_countLabel);

            _showInfoToggle = new Toggle { name = "show-info-toggle", value = true };
            _showInfoToggle.tooltip = "情報表示"; _showInfoToggle.style.marginLeft = 4;
            topRow.Add(_showInfoToggle);

            _showMirrorSideToggle = new Toggle { name = "show-mirror-toggle", value = false };
            _showMirrorSideToggle.tooltip = "ミラー側表示"; _showMirrorSideToggle.style.marginLeft = 2;
            topRow.Add(_showMirrorSideToggle);
            root.Add(topRow);

            _filterField = new TextField { name = "filter-field" };
            _filterField.style.color = new StyleColor(Color.black);
            _filterField.style.marginBottom = 3;
            root.Add(_filterField);

            // ── メインコンテンツ（ツリー + 詳細 + BonePose + Transform）
            _mainContent = new VisualElement { name = "main-content" };
            _mainContent.style.flexGrow = 1;

            _treeView = new TreeView { name = "mesh-tree" };
            _treeView.style.flexGrow  = 1;
            _treeView.style.minHeight = 80;
            _treeView.style.maxHeight = 200;
            _mainContent.Add(_treeView);

            // 操作ボタン行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.flexWrap     = Wrap.Wrap;
            btnRow.style.marginTop    = 3;
            btnRow.Add(MakeSmallBtn("+",   "btn-add"));
            btnRow.Add(MakeSmallBtn("▲",  "btn-up"));
            btnRow.Add(MakeSmallBtn("▼",  "btn-down"));
            btnRow.Add(MakeSmallBtn("←",  "btn-outdent"));
            btnRow.Add(MakeSmallBtn("→",  "btn-indent"));
            btnRow.Add(MakeSmallBtn("Dup", "btn-duplicate"));
            btnRow.Add(MakeSmallBtn("Del", "btn-delete"));
            btnRow.Add(MakeSmallBtn("👁",  "btn-show"));
            btnRow.Add(MakeSmallBtn("−",   "btn-hide"));
            _mainContent.Add(btnRow);

            // 詳細Foldout
            _detailFoldout = new Foldout { text = "詳細", value = true, name = "detail-foldout" };
            _detailFoldout.style.marginTop = 4;
            BuildDetailFoldout(_detailFoldout.contentContainer);
            _mainContent.Add(_detailFoldout);

            // indexInfo（ボーンタブ用）
            _indexInfo = new VisualElement { name = "index-info" };
            _boneIndexLabel   = MakeInfoLabel("bone-index-label");
            _masterIndexLabel = MakeInfoLabel("master-index-label");
            _indexInfo.Add(_boneIndexLabel); _indexInfo.Add(_masterIndexLabel);
            _mainContent.Add(_indexInfo);

            root.Add(_mainContent);

            // ── モーフエディタ（詳細モード+Morphタブ時のみ表示）
            _morphEditor = new VisualElement { name = "morph-editor" };
            _morphEditor.style.display = DisplayStyle.None;
            BuildMorphEditor(_morphEditor);
            root.Add(_morphEditor);

            // ── ステータス
            _statusLabel = new Label("") { name = "status-label" };
            _statusLabel.style.color = new StyleColor(Color.white);
            root.Add(_statusLabel);
        }

        private void BuildDetailFoldout(VisualElement c)
        {
            _meshNameField = new TextField { name = "mesh-name-field" };
            _meshNameField.style.color = new StyleColor(Color.black);
            _meshNameField.style.marginBottom = 3;
            c.Add(_meshNameField);
            _vertexCountLabel = MakeInfoLabel("vertex-count-label"); c.Add(_vertexCountLabel);
            _faceCountLabel   = MakeInfoLabel("face-count-label");   c.Add(_faceCountLabel);
            _triCountLabel    = MakeInfoLabel("tri-count-label");     c.Add(_triCountLabel);
            _quadCountLabel   = MakeInfoLabel("quad-count-label");    c.Add(_quadCountLabel);
            _ngonCountLabel   = MakeInfoLabel("ngon-count-label");    c.Add(_ngonCountLabel);
        }

        private void BuildMorphEditor(VisualElement parent)
        {
            // カウント・フィルター
            var topRow = new VisualElement(); topRow.style.flexDirection = FlexDirection.Row;
            topRow.Add(_morphCountLabel);
            parent.Add(topRow);

            _morphFilterField = new TextField(); _morphFilterField.style.marginBottom = 3;
            _morphFilterField.style.color = new StyleColor(Color.black);
            parent.Add(_morphFilterField);

            // リスト
            _morphListView = new ListView(_morphFilteredData, 20, MorphMake, MorphBind);
            _morphListView.style.flexGrow  = 1; _morphListView.style.minHeight = 60; _morphListView.style.maxHeight = 140;
            _morphListView.selectionType   = SelectionType.Multiple;
            _morphListView.selectionChanged += OnMorphSel;
            parent.Add(_morphListView);

            // テストウェイト
            var wRow = new VisualElement(); wRow.style.flexDirection = FlexDirection.Row; wRow.style.marginTop = 4; wRow.style.alignItems = Align.Center;
            _morphTestWeight = new Slider(0f, 1f); _morphTestWeight.style.flexGrow = 1; wRow.Add(_morphTestWeight);
            _morphTestWeight.style.color = new StyleColor(Color.white);
            parent.Add(wRow);

            // 選択操作ボタン
            var selRow = new VisualElement(); selRow.style.flexDirection = FlexDirection.Row; selRow.style.marginTop = 3;
            selRow.Add(MakeSmallBtn("全選択",   "btn-morph-test-select-all"));
            selRow.Add(MakeSmallBtn("全解除",   "btn-morph-test-deselect-all"));
            selRow.Add(MakeSmallBtn("リセット", "btn-morph-test-reset"));
            parent.Add(selRow);

            parent.Add(Separator());

            // モーフ変換
            parent.Add(SectionHeader("メッシュ→モーフ"));

            _morphSourceMeshPopupContainer = new VisualElement { name = "morph-source-mesh-container" };
            parent.Add(LabeledRow("元メッシュ", _morphSourceMeshPopupContainer));

            _morphParentPopupContainer = new VisualElement { name = "morph-parent-container" };
            parent.Add(LabeledRow("親", _morphParentPopupContainer));

            _morphPanelPopupContainer = new VisualElement { name = "morph-panel-container" };
            parent.Add(LabeledRow("パネル", _morphPanelPopupContainer));

            _morphNameField = new TextField(); _morphNameField.name = "morph-name-field";
            _morphNameField.style.color = new StyleColor(Color.black);
            parent.Add(LabeledRow("名前", _morphNameField));

            var convRow = new VisualElement(); convRow.style.flexDirection = FlexDirection.Row;
            _btnMeshToMorph = MakeSmallBtn("Mesh→Morph", "btn-mesh-to-morph");
            _btnMorphToMesh = MakeSmallBtn("Morph→Mesh", "btn-morph-to-mesh");
            convRow.Add(_btnMeshToMorph); convRow.Add(_btnMorphToMesh);
            parent.Add(convRow);

            parent.Add(Separator());

            // モーフセット作成
            parent.Add(SectionHeader("モーフセット作成"));
            _morphSetNameField = new TextField(); _morphSetNameField.name = "morph-set-name-field";
            _morphSetNameField.style.color = new StyleColor(Color.black);
            parent.Add(LabeledRow("セット名", _morphSetNameField));

            _morphSetTypePopupContainer = new VisualElement { name = "morph-set-type-container" };
            parent.Add(LabeledRow("種別", _morphSetTypePopupContainer));

            _btnCreateMorphSet = MakeSmallBtn("セット作成", "btn-create-morph-set");
            parent.Add(_btnCreateMorphSet);

            _morphStatusLabel = new Label(""); _morphStatusLabel.style.fontSize = 10; _morphStatusLabel.style.color = new StyleColor(new Color(1f, 0.7f, 0.4f)); _morphStatusLabel.style.marginTop = 3;
            _morphStatusLabel.style.color = new StyleColor(Color.white);
            parent.Add(_morphStatusLabel);
        }

        // ================================================================
        // SetupTreeView（エディタ版と同一）
        // ================================================================

        private void SetupTreeView()
        {
            if (_treeView == null) return;
            _treeView.fixedItemHeight    = 20;
            _treeView.makeItem           = MakeTreeItem;
            _treeView.bindItem           = BindTreeItem;
            _treeView.selectionType      = SelectionType.Multiple;
            _treeView.selectionChanged   += OnSelectionChanged;
            _treeView.itemExpandedChanged += OnItemExpandedChanged;
        }

        // ================================================================
        // タブ切り替え（エディタ版と同一）
        // ================================================================

        private void SwitchTab(TabType tab)
        {
            if (_currentTab == TabType.Morph && tab != TabType.Morph) SendEndMorphPreview();
            _currentTab = tab;
            SetTabActive(_tabDrawable, tab == TabType.Drawable);
            SetTabActive(_tabBone,     tab == TabType.Bone);
            SetTabActive(_tabMorph,    tab == TabType.Morph);

            bool simpleMode = IsSimpleMode;
            if (_tabHeader != null) _tabHeader.style.display = simpleMode ? DisplayStyle.None : DisplayStyle.Flex;

            if (simpleMode)
            {
                if (_indexInfo     != null) _indexInfo.style.display     = DisplayStyle.None;
                if (_bonePoseSection != null) _bonePoseSection.style.display = DisplayStyle.Flex;
                if (_mainContent   != null) _mainContent.style.display   = DisplayStyle.Flex;
                if (_morphEditor   != null) _morphEditor.style.display   = DisplayStyle.None;
            }
            else
            {
                if (_indexInfo != null)
                    _indexInfo.style.display = tab == TabType.Bone ? DisplayStyle.Flex : DisplayStyle.None;
                if (_bonePoseSection != null)
                    _bonePoseSection.style.display = tab == TabType.Bone ? DisplayStyle.Flex : DisplayStyle.None;
                bool isMorph = tab == TabType.Morph;
                if (_mainContent != null) _mainContent.style.display = isMorph ? DisplayStyle.None : DisplayStyle.Flex;
                if (_morphEditor != null) _morphEditor.style.display = isMorph ? DisplayStyle.Flex : DisplayStyle.None;
            }

            _selectedAdapters.Clear();
            bool showMorph = !simpleMode && tab == TabType.Morph;
            if (!showMorph) CreateTreeRoot();
            if (showMorph) RefreshMorphEditor();
            RefreshAllImmediate();
            Log($"{tab} タブ");
        }

        private void SetTabActive(Button btn, bool active) => btn?.EnableInClassList("tab-active", active);

        private void OnDetailModeChanged()
        {
            _selectedAdapters.Clear();
            if (IsSimpleMode) SendEndMorphPreview();
            SwitchTab(IsSimpleMode ? TabType.Drawable : _currentTab);
            UpdateBonePosePanel();
            UpdateTransformPanel();
        }

        // ================================================================
        // CreateTreeRoot（エディタ版と同一）
        // ================================================================

        private void CreateTreeRoot()
        {
            var model = CurrentModel;
            if (model == null) return;

            IReadOnlyList<IMeshView> sourceList;
            MeshCategory category;

            if (IsSimpleMode)
            {
                var filtered = model.DrawableList?.Where(v => !v.HasBoneWeight).ToList() ?? new List<IMeshView>();
                sourceList = filtered;
                category   = MeshCategory.Drawable;
            }
            else
            {
                sourceList = _currentTab switch
                {
                    TabType.Drawable => model.DrawableList,
                    TabType.Bone     => model.BoneList,
                    _                => null
                };
                category = CurrentCategory;
            }

            if (sourceList == null) return;

            bool   excludeMirror = !(_showMirrorSideToggle?.value ?? false);
            string filter        = _filterField?.value;

            _treeRoot = new SummaryTreeRoot();
            _treeRoot.ModelIndex   = ModelIndex;
            _treeRoot.SendCommand  = cmd => _ctx?.SendCommand(cmd);
            _treeRoot.OnChanged    = () =>
            {
                _isReceiving = true;
                try { RefreshTreeImmediate(); SyncTreeViewSelection(); UpdateDetailPanel(); }
                finally { _isReceiving = false; }
            };
            _treeRoot.Build(sourceList, category, excludeMirror, filter);
            SetupDragDrop();
        }

        // ================================================================
        // MakeItem / BindItem（エディタ版と同一）
        // ================================================================

        private VisualElement MakeTreeItem()
        {
            var c = new VisualElement();
            c.style.flexDirection = FlexDirection.Row;
            c.style.flexGrow = 1; c.style.alignItems = Align.Center;
            c.style.paddingLeft = 2; c.style.paddingRight = 4;

            var nameLabel = new Label { name = "name" };
            nameLabel.style.color = new StyleColor(Color.white);
            nameLabel.style.flexGrow = 1; nameLabel.style.flexShrink = 1;
            nameLabel.style.overflow = Overflow.Hidden; nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft; nameLabel.style.marginRight = 4;
            c.Add(nameLabel);

            var infoLabel = new Label { name = "info" };
            infoLabel.style.width = 80; infoLabel.style.flexShrink = 0;
            infoLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            infoLabel.style.color = new StyleColor(Color.white);
            infoLabel.style.fontSize = 11; infoLabel.style.marginRight = 4;
            c.Add(infoLabel);

            var attr = new VisualElement(); attr.style.flexDirection = FlexDirection.Row; attr.style.flexShrink = 0;
            var visBtn = MkAttrBtn("vis-btn", "👁", "可視性切り替え");
            var lockBtn = MkAttrBtn("lock-btn", "🔒", "ロック切り替え");
            var symBtn  = MkAttrBtn("sym-btn", "⇆", "対称切り替え");
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
            b.style.paddingLeft = 0; b.style.paddingRight = 0; b.style.paddingTop = 0; b.style.paddingBottom = 0;
            b.style.fontSize = 12;
            b.style.borderTopWidth = 0; b.style.borderBottomWidth = 0; b.style.borderLeftWidth = 0; b.style.borderRightWidth = 0;
            b.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
            return b;
        }

        private void BindTreeItem(VisualElement element, int index)
        {
            var adapter = _treeView.GetItemDataForIndex<SummaryTreeAdapter>(index);
            if (adapter == null) return;
            var cache = element.userData as TreeItemCache;
            if (cache == null) return;

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

            if (cache.InfoLabel != null)
            {
                bool showInfo = _showInfoToggle?.value ?? true;
                cache.InfoLabel.text = showInfo
                    ? (_currentTab == TabType.Bone ? $"Bone:{adapter.MeshView.BoneIndex}" : adapter.GetInfoString())
                    : "";
                cache.InfoLabel.style.display = showInfo ? DisplayStyle.Flex : DisplayStyle.None;
            }

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
                    if      (adapter.IsMirrorSide)        icon = "\U0001FA9E";
                    else if (adapter.IsRealSide)          icon = "\u21C6";
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
        // 選択（エディタ版と同一）
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
            UpdateTransformPanel();
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
                TabType.Bone     => CurrentModel.SelectedBoneIndices,
                _                => null,
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
            RebuildSelectedAdaptersFromCurrentModel();
        }

        // ================================================================
        // D&D（エディタ版と同一）
        // ================================================================

        private void SetupDragDrop()
        {
            CleanupDragDrop();
            if (_treeView == null || _treeRoot == null) return;
            _dragDropHelper = new TreeViewDragDropHelper<SummaryTreeAdapter>(_treeView, _treeRoot, new SummaryDragValidator());
            _dragDropHelper.Setup();
        }

        private void CleanupDragDrop() { _dragDropHelper?.Cleanup(); _dragDropHelper = null; }

        // ================================================================
        // ボタンイベント（エディタ版と同一ロジック、DisplayDialog除去）
        // ================================================================

        private void RegisterButtonEvents()
        {
            _tabDrawable?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Drawable));
            _tabBone    ?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Bone));
            _tabMorph   ?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Morph));

            Q<Button>("btn-add")      ?.RegisterCallback<ClickEvent>(_ => OnAdd());
            Q<Button>("btn-up")       ?.RegisterCallback<ClickEvent>(_ => MoveSelected(-1));
            Q<Button>("btn-down")     ?.RegisterCallback<ClickEvent>(_ => MoveSelected(1));
            Q<Button>("btn-outdent")  ?.RegisterCallback<ClickEvent>(_ => OutdentSelected());
            Q<Button>("btn-indent")   ?.RegisterCallback<ClickEvent>(_ => IndentSelected());
            Q<Button>("btn-duplicate")?.RegisterCallback<ClickEvent>(_ => DuplicateSelected());
            Q<Button>("btn-delete")   ?.RegisterCallback<ClickEvent>(_ => DeleteSelected());
            Q<Button>("btn-show")     ?.RegisterCallback<ClickEvent>(_ => SetSelectedVisibility(true));
            Q<Button>("btn-hide")     ?.RegisterCallback<ClickEvent>(_ => SetSelectedVisibility(false));

            _detailModeToggle?.RegisterValueChangedCallback(_ => OnDetailModeChanged());
            _showInfoToggle?.RegisterValueChangedCallback(_ => RefreshTree());
            _showMirrorSideToggle?.RegisterValueChangedCallback(_ => RefreshTreeImmediate());
            _filterField?.RegisterValueChangedCallback(_ => RefreshTreeImmediate());
            _meshNameField?.RegisterValueChangedCallback(OnNameFieldChanged);
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
            { TreeViewHelper.RebuildParentReferences(_treeRoot.RootItems); _treeRoot.OnTreeChanged(); }
        }

        private void IndentSelected()
        {
            if (_selectedAdapters.Count != 1 || _treeRoot == null) return;
            if (TreeViewHelper.Indent(_selectedAdapters[0], _treeRoot.RootItems))
            { TreeViewHelper.RebuildParentReferences(_treeRoot.RootItems); _treeRoot.OnTreeChanged(); }
        }

        private void DuplicateSelected()
        {
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new DuplicateMeshesCommand(ModelIndex, SelIndices()));
        }

        private void DeleteSelected()
        {
            // Player: EditorUtility.DisplayDialog なし、即実行
            if (_selectedAdapters.Count == 0) return;
            SendCmd(new DeleteMeshesCommand(ModelIndex,
                _selectedAdapters.OrderByDescending(a => a.MasterIndex).Select(a => a.MasterIndex).ToArray()));
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
        // OnViewChanged（エディタ版と同一、EditorApplication.delayCall を schedule.Execute に）
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
                        if (_currentTab != TabType.Morph) SyncTreeViewSelection(); else SyncMorphSel();
                        UpdateDetailPanel(); UpdateBonePosePanel(); UpdateTransformPanel();
                        break;
                    case ChangeKind.Attributes:
                        if (_currentTab != TabType.Morph) { _treeView?.RefreshItems(); SyncTreeViewSelection(); }
                        else RefreshMorphEditor();
                        UpdateDetailPanel(); UpdateBonePosePanel(); UpdateTransformPanel();
                        break;
                    case ChangeKind.ListStructure:
                    case ChangeKind.ModelSwitch:
                    default:
                        // スキンドメッシュの自動設定
                        if (_detailModeToggle != null)
                        {
                            var model = view?.CurrentModel;
                            bool hasSkinned = model?.DrawableList?.Any(v => v.HasBoneWeight) ?? false;
                            _detailModeToggle.SetValueWithoutNotify(hasSkinned);
                            OnDetailModeChanged();
                        }
                        if (_currentTab != TabType.Morph) { CreateTreeRoot(); RefreshAllImmediate(); SyncTreeViewSelection(); }
                        if (_currentTab == TabType.Morph) RefreshMorphEditor();
                        UpdateDetailPanel(); UpdateBonePosePanel(); UpdateTransformPanel();
                        break;
                }
            }
            finally { _root?.schedule.Execute(() => _isReceiving = false); }
        }

        // ================================================================
        // 更新（RefreshTree の delayCall → schedule.Execute）
        // ================================================================

        private void RefreshAllImmediate() { RefreshTreeImmediate(); UpdateHeader(); UpdateDetailPanel(); }

        private void RefreshTree()
        {
            if (_treeView == null || _treeRoot == null || _refreshScheduled) return;
            _refreshScheduled = true;
            _root?.schedule.Execute(() => { _refreshScheduled = false; ApplyTreeToView(); });
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
            if (IsSimpleMode) { _countLabel.text = $"メッシュ+ボーン: {_treeRoot?.TotalCount ?? 0}"; return; }
            string label = _currentTab switch { TabType.Drawable => "メッシュ", TabType.Bone => "ボーン", _ => "モーフ" };
            _countLabel.text = $"{label}: {_treeRoot?.TotalCount ?? 0}";
        }

        // ================================================================
        // 詳細パネル（エディタ版と同一）
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
                SL(_faceCountLabel,   $"面: {_selectedAdapters.Sum(a => a.FaceCount)} (合計)");
            }
        }

        // ================================================================
        // BonePose（エディタ版と同一）
        // ================================================================

        private void BindBonePoseUI(VisualElement root)
        {
            _bonePoseSection = new VisualElement { name = "bone-pose-section" };
            _bonePoseSection.style.marginTop = 4;

            _poseFoldout = new Foldout { text = "ボーンポーズ", value = true, name = "pose-foldout" };

            _poseActiveToggle = new Toggle("アクティブ") { name = "pose-active-toggle" };
            _poseActiveToggle.style.color = new StyleColor(Color.white);
            _poseActiveToggle.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                SendCmd(new SetBonePoseActiveCommand(ModelIndex, SelIndices(), e.newValue));
            });
            _poseFoldout.Add(_poseActiveToggle);

            _poseFoldout.Add(SectionHeader("位置"));
            AddXYZFields(_poseFoldout, out _restPosX, out _restPosY, out _restPosZ, "rest-pos");
            RegRestTF(_restPosX, SetBoneTransformValueCommand.Field.PositionX);
            RegRestTF(_restPosY, SetBoneTransformValueCommand.Field.PositionY);
            RegRestTF(_restPosZ, SetBoneTransformValueCommand.Field.PositionZ);

            _poseFoldout.Add(SectionHeader("回転"));
            AddRotFields(_poseFoldout,
                out _restRotX, out _restRotSliderX, SetBoneTransformValueCommand.Field.RotationX,
                out _restRotY, out _restRotSliderY, SetBoneTransformValueCommand.Field.RotationY,
                out _restRotZ, out _restRotSliderZ, SetBoneTransformValueCommand.Field.RotationZ, isPose: true);

            _poseFoldout.Add(SectionHeader("スケール"));
            AddXYZFields(_poseFoldout, out _restSclX, out _restSclY, out _restSclZ, "rest-scl");
            RegRestTF(_restSclX, SetBoneTransformValueCommand.Field.ScaleX);
            RegRestTF(_restSclY, SetBoneTransformValueCommand.Field.ScaleY);
            RegRestTF(_restSclZ, SetBoneTransformValueCommand.Field.ScaleZ);

            _poseFoldout.Add(_poseResultPos); _poseFoldout.Add(_poseResultRot);

            _poseLayersContainer = new VisualElement { name = "pose-layers-container" };
            _poseNoLayersLabel = new Label("(レイヤーなし)") { name = "pose-no-layers-label" };
            _poseNoLayersLabel.style.color = new StyleColor(Color.white);
            _poseLayersContainer.Add(_poseNoLayersLabel);
            _poseFoldout.Add(_poseLayersContainer);

            var poseRow = new VisualElement(); poseRow.style.flexDirection = FlexDirection.Row; poseRow.style.marginTop = 4;
            _btnInitPose     = MakeSmallBtn("初期化", "btn-init-pose");
            _btnResetLayers  = MakeSmallBtn("レイヤーリセット", "btn-reset-layers");
            poseRow.Add(_btnInitPose); poseRow.Add(_btnResetLayers);
            _poseFoldout.Add(poseRow);
            _bonePoseSection.Add(_poseFoldout);

            _bindposeFoldout = new Foldout { text = "バインドポーズ", value = false, name = "bindpose-foldout" };
            _bindposeFoldout.Add(_bindposePos); _bindposeFoldout.Add(_bindposeRot); _bindposeFoldout.Add(_bindposeScl);
            _btnBakePose = MakeSmallBtn("ポーズベイク", "btn-bake-pose");
            _bindposeFoldout.Add(_btnBakePose);
            _bonePoseSection.Add(_bindposeFoldout);

            _btnInitPose?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new InitBonePoseCommand(ModelIndex, i)); });
            _btnResetLayers?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new ResetBonePoseLayersCommand(ModelIndex, i)); });
            _btnBakePose?.RegisterCallback<ClickEvent>(_ => { var i = SelIndices(); if (i.Length > 0) SendCmd(new BakePoseToBindPoseCommand(ModelIndex, i)); });

            _mainContent?.Add(_bonePoseSection);
        }

        private void RegRestTF(FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
            });
        }

        private void RegRestRotField(FloatField f, Slider s, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingPoseUI = true;
                try { s?.SetValueWithoutNotify(NormAngle(e.newValue)); } finally { _isSyncingPoseUI = false; }
            });
        }

        private void RegRestRotSlider(Slider s, FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            s?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingPoseUI || _ctx == null) return;
                var i = SelIndices(); if (i.Length == 0) return;
                SendCmd(new BeginBoneTransformSliderDragCommand(ModelIndex, i));
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingPoseUI = true;
                try { f?.SetValueWithoutNotify((float)System.Math.Round(e.newValue, 4)); } finally { _isSyncingPoseUI = false; }
            });
            s?.RegisterCallback<PointerCaptureOutEvent>(_ => SendCmd(new EndBoneTransformSliderDragCommand(ModelIndex, "ボーン回転変更")));
        }

        private void UpdateBonePosePanel()
        {
            if (_bonePoseSection == null) return;

            if (IsSimpleMode)
            {
                bool show = _selectedAdapters.Any(a => a.MeshView.BonePose.HasPose);
                _bonePoseSection.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show) return;
            }

            if (_currentTab != TabType.Bone) return;
            _isSyncingPoseUI = true;
            try
            {
                if (_selectedAdapters.Count == 0) { SetPoseEmpty(); return; }
                var poses = _selectedAdapters.Select(a => a.MeshView.BonePose).Where(bp => bp.HasPose).ToList();
                bool all  = poses.Count == _selectedAdapters.Count;
                bool none = poses.Count == 0;

                if (all) { bool f = poses[0].IsActive; bool same = poses.TrueForAll(p => p.IsActive == f); _poseActiveToggle?.SetValueWithoutNotify(same ? f : false); SMV(_poseActiveToggle, !same); }
                else { _poseActiveToggle?.SetValueWithoutNotify(false); SMV(_poseActiveToggle, !none); }
                _poseActiveToggle?.SetEnabled(true);

                if (all && poses.Count > 0)
                {
                    var views = _selectedAdapters.Select(a => a.MeshView).ToList();
                    MixFT(_restPosX, views, v => v.LocalPosition.x); MixFT(_restPosY, views, v => v.LocalPosition.y); MixFT(_restPosZ, views, v => v.LocalPosition.z);
                    MixRTF(_restRotX, _restRotSliderX, views, v => v.LocalRotationEuler.x); MixRTF(_restRotY, _restRotSliderY, views, v => v.LocalRotationEuler.y); MixRTF(_restRotZ, _restRotSliderZ, views, v => v.LocalRotationEuler.z);
                    MixFT(_restSclX, views, v => v.LocalScale.x); MixFT(_restSclY, views, v => v.LocalScale.y); MixFT(_restSclZ, views, v => v.LocalScale.z);
                }
                else
                {
                    SF(_restPosX,0,false); SF(_restPosY,0,false); SF(_restPosZ,0,false);
                    SF(_restRotX,0,false); SF(_restRotY,0,false); SF(_restRotZ,0,false);
                    SS(_restRotSliderX,0,false); SS(_restRotSliderY,0,false); SS(_restRotSliderZ,0,false);
                    SF(_restSclX,1,false); SF(_restSclY,1,false); SF(_restSclZ,1,false);
                }

                var single = (_selectedAdapters.Count == 1 && all) ? poses[0] : null;
                UpdateLayers(single);
                if (single != null)
                {
                    SL(_poseResultPos, $"Pos: ({single.ResultPosition.x:F3}, {single.ResultPosition.y:F3}, {single.ResultPosition.z:F3})");
                    SL(_poseResultRot, $"Rot: ({single.ResultRotationEuler.x:F1}, {single.ResultRotationEuler.y:F1}, {single.ResultRotationEuler.z:F1})");
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
            if (_btnInitPose != null) _btnInitPose.style.display = DisplayStyle.None;
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
        // モーフエディタ（エディタ版と同一、PopupField<int>→DropdownField）
        // ================================================================

        private void BindMorphEditorUI(VisualElement root)
        {
            if (_morphListView != null)
            {
                _morphListView.makeItem  = MorphMake;
                _morphListView.bindItem  = MorphBind;
                _morphListView.fixedItemHeight = 20;
                _morphListView.itemsSource     = _morphFilteredData;
                _morphListView.selectionType   = SelectionType.Multiple;
                _morphListView.selectionChanged += OnMorphSel;
            }
            Q<Button>("btn-morph-test-reset")       ?.RegisterCallback<ClickEvent>(_ => OnMorphTestReset());
            Q<Button>("btn-morph-test-select-all")  ?.RegisterCallback<ClickEvent>(_ => OnMorphSelAll(true));
            Q<Button>("btn-morph-test-deselect-all")?.RegisterCallback<ClickEvent>(_ => OnMorphSelAll(false));
            _morphTestWeight?.RegisterValueChangedCallback(OnMorphWeight);
            _morphFilterField?.RegisterValueChangedCallback(_ => RefreshMorphListData());
            _btnMeshToMorph?.RegisterCallback<ClickEvent>(_ => OnMeshToMorph());
            _btnMorphToMesh?.RegisterCallback<ClickEvent>(_ => OnMorphToMesh());
            _btnCreateMorphSet?.RegisterCallback<ClickEvent>(_ => OnCreateMorphSet());
        }

        private VisualElement MorphMake()
        {
            var r = new VisualElement(); r.AddToClassList("morph-list-row");
            r.Add(new Label { name = "n" }); r.Q<Label>("n").AddToClassList("morph-list-name");
            var il = new Label { name = "i" }; il.AddToClassList("morph-list-info"); r.Add(il);
            il.style.color = new StyleColor(Color.white);
            return r;
        }

        private void MorphBind(VisualElement el, int idx)
        {
            if (idx < 0 || idx >= _morphFilteredData.Count) return;
            var s  = _morphFilteredData[idx];
            var nl = el.Q<Label>("n"); if (nl != null) nl.text = s.Name;
            var il = el.Q<Label>("i");
            if (il != null)
            {
                if (s.MorphParentIndex >= 0) { var pn = FindDrawableName(s.MorphParentIndex); il.text = pn != null ? $"→{pn}" : $"→[{s.MorphParentIndex}]"; }
                else if (!string.IsNullOrEmpty(s.MorphName)) il.text = s.MorphName;
                else il.text = "";
            }
        }

        private void RefreshMorphEditor() { if (CurrentModel == null) return; RefreshMorphListData(); RefreshMorphConvert(); RefreshMorphSet(); }

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
                var li  = new List<int>();
                for (int i = 0; i < _morphFilteredData.Count; i++)
                    if (set.Contains(_morphFilteredData[i].MasterIndex)) li.Add(i);
                _morphListView.SetSelectionWithoutNotify(li);
            }
            finally { _isSyncingMorphSelection = false; }
        }

        // RefreshMorphConvert: PopupField<int> → DropdownField
        private void RefreshMorphConvert()
        {
            if (CurrentModel == null) return;
            var labels = new List<string> { "(なし)" };
            _morphSourceMeshIds = new List<int> { -1 };
            _morphParentIds     = new List<int> { -1 };
            foreach (var s in CurrentModel.DrawableList ?? (IReadOnlyList<IMeshView>)Array.Empty<IMeshView>())
            {
                labels.Add($"[{s.MasterIndex}] {s.Name}");
                _morphSourceMeshIds.Add(s.MasterIndex);
                _morphParentIds.Add(s.MasterIndex);
            }
            RebuildDropdown(ref _morphSourceMeshDropdown, _morphSourceMeshPopupContainer, labels);
            RebuildDropdown(ref _morphParentDropdown,     _morphParentPopupContainer,     labels);

            var panelLabels = new List<string> { "眉", "目", "口", "その他" };
            RebuildDropdown(ref _morphPanelDropdown, _morphPanelPopupContainer, panelLabels, 3);
        }

        private void RefreshMorphSet()
        {
            if (CurrentModel == null) return;
            var stLabels = new List<string> { "Vertex", "UV" };
            RebuildDropdown(ref _morphSetTypeDropdown, _morphSetTypePopupContainer, stLabels, 0);
        }

        private static void RebuildDropdown(ref DropdownField df, VisualElement container, List<string> choices, int initial = 0)
        {
            if (container == null) return;
            if (df == null)
            {
                df = new DropdownField(choices, initial);
                df.style.color = new StyleColor(Color.white);
                df.AddToClassList("morph-popup"); df.style.flexGrow = 1;
                container.Add(df);
            }
            else
            {
                df.choices = choices;
                df.SetValueWithoutNotify(choices.Count > 0 ? choices[Mathf.Clamp(initial, 0, choices.Count - 1)] : "");
            }
        }

        private void OnMeshToMorph()
        {
            int srcIdx = (_morphSourceMeshDropdown?.index ?? 0) - 1; // 0=(なし)
            int src = (srcIdx >= 0 && srcIdx < _morphSourceMeshIds.Count - 1) ? _morphSourceMeshIds[srcIdx + 1] : -1;
            int parIdx = (_morphParentDropdown?.index ?? 0) - 1;
            int par = (parIdx >= 0 && parIdx < _morphParentIds.Count - 1) ? _morphParentIds[parIdx + 1] : -1;
            int pan = _morphPanelDropdown?.index ?? 3;
            string nm = _morphNameField?.value?.Trim() ?? "";
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
            int ty = (_morphSetTypeDropdown?.index == 1) ? 3 : 1;
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
            else     SendCmd(new DeselectAllMorphsCommand(ModelIndex));
        }

        private void SendEndMorphPreview()
        {
            if (_ctx != null && _isMorphPreviewStarted) SendCmd(new EndMorphPreviewCommand(ModelIndex));
            _isMorphPreviewStarted = false;
        }

        // ================================================================
        // BoneTransform（エディタ版と同一）
        // ================================================================

        private void BindTransformUI(VisualElement root)
        {
            _transformFoldout = new Foldout { text = "トランスフォーム", value = false, name = "transform-foldout" };
            _transformFoldout.style.marginTop  = 4;
            _transformFoldout.style.display    = DisplayStyle.None;

            _transformFoldout.Add(SectionHeader("位置"));
            AddXYZFields(_transformFoldout, out _localPosX, out _localPosY, out _localPosZ, "local-pos");
            RegTF(_localPosX, SetBoneTransformValueCommand.Field.PositionX);
            RegTF(_localPosY, SetBoneTransformValueCommand.Field.PositionY);
            RegTF(_localPosZ, SetBoneTransformValueCommand.Field.PositionZ);

            _transformFoldout.Add(SectionHeader("回転"));
            AddRotFields(_transformFoldout,
                out _localRotX, out _localRotSliderX, SetBoneTransformValueCommand.Field.RotationX,
                out _localRotY, out _localRotSliderY, SetBoneTransformValueCommand.Field.RotationY,
                out _localRotZ, out _localRotSliderZ, SetBoneTransformValueCommand.Field.RotationZ, isPose: false);

            _transformFoldout.Add(SectionHeader("スケール"));
            AddXYZFields(_transformFoldout, out _localSclX, out _localSclY, out _localSclZ, "local-scl");
            RegTF(_localSclX, SetBoneTransformValueCommand.Field.ScaleX);
            RegTF(_localSclY, SetBoneTransformValueCommand.Field.ScaleY);
            RegTF(_localSclZ, SetBoneTransformValueCommand.Field.ScaleZ);

            _mainContent?.Add(_transformFoldout);
        }

        private void RegTF(FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingTransformUI || _ctx == null) return;
                var i = SelTransformIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
            });
        }

        private void RegTRotField(FloatField f, Slider s, SetBoneTransformValueCommand.Field tf)
        {
            f?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingTransformUI || _ctx == null) return;
                var i = SelTransformIndices(); if (i.Length == 0) return;
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingTransformUI = true;
                try { s?.SetValueWithoutNotify(NormAngle(e.newValue)); } finally { _isSyncingTransformUI = false; }
            });
        }

        private void RegTRotSlider(Slider s, FloatField f, SetBoneTransformValueCommand.Field tf)
        {
            s?.RegisterValueChangedCallback(e =>
            {
                if (_isSyncingTransformUI || _ctx == null) return;
                var i = SelTransformIndices(); if (i.Length == 0) return;
                SendCmd(new BeginBoneTransformSliderDragCommand(ModelIndex, i));
                SendCmd(new SetBoneTransformValueCommand(ModelIndex, i, tf, e.newValue));
                _isSyncingTransformUI = true;
                try { f?.SetValueWithoutNotify((float)System.Math.Round(e.newValue, 4)); } finally { _isSyncingTransformUI = false; }
            });
            s?.RegisterCallback<PointerCaptureOutEvent>(_ => SendCmd(new EndBoneTransformSliderDragCommand(ModelIndex, "トランスフォーム回転変更")));
        }

        private void UpdateTransformPanel()
        {
            if (_transformFoldout == null) return;
            if (!IsSimpleMode) { _transformFoldout.style.display = DisplayStyle.None; return; }
            bool show = _selectedAdapters.Any(a => !a.MeshView.BonePose.HasPose);
            _transformFoldout.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            _isSyncingTransformUI = true;
            try
            {
                var views = _selectedAdapters.Where(a => !a.MeshView.BonePose.HasPose).Select(a => a.MeshView).ToList();
                MixFT(_localPosX, views, v => v.LocalPosition.x); MixFT(_localPosY, views, v => v.LocalPosition.y); MixFT(_localPosZ, views, v => v.LocalPosition.z);
                MixRTF(_localRotX, _localRotSliderX, views, v => v.LocalRotationEuler.x); MixRTF(_localRotY, _localRotSliderY, views, v => v.LocalRotationEuler.y); MixRTF(_localRotZ, _localRotSliderZ, views, v => v.LocalRotationEuler.z);
                MixFT(_localSclX, views, v => v.LocalScale.x); MixFT(_localSclY, views, v => v.LocalScale.y); MixFT(_localSclZ, views, v => v.LocalScale.z);
            }
            finally { _isSyncingTransformUI = false; }
        }

        // ================================================================
        // ヘルパー（エディタ版と同一）
        // ================================================================

        private void SendCmd(PanelCommand c) => _ctx?.SendCommand(c);
        private int[] SelIndices() => _selectedAdapters.Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();
        private int[] SelTransformIndices() => _selectedAdapters.Where(a => !a.MeshView.BonePose.HasPose).Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();

        private void RebuildSelectedAdaptersFromTreeView()
        {
            _selectedAdapters.Clear();
            if (_treeView == null) return;
            foreach (var item in _treeView.selectedItems)
                if (item is SummaryTreeAdapter a && !a.IsBakedMirror && !a.IsMirrorSide)
                    _selectedAdapters.Add(a);
        }

        private void RebuildSelectedAdaptersFromCurrentModel()
        {
            _selectedAdapters.Clear();
            if (_treeRoot == null || CurrentModel == null) return;
            int[] sel = _currentTab switch
            {
                TabType.Drawable => CurrentModel.SelectedDrawableIndices,
                TabType.Bone     => CurrentModel.SelectedBoneIndices,
                _                => null,
            };
            if (sel == null) return;
            foreach (int idx in sel)
            {
                var a = _treeRoot.GetAdapterByMasterIndex(idx);
                if (a != null && !a.IsBakedMirror && !a.IsMirrorSide)
                    _selectedAdapters.Add(a);
            }
        }

        private string FindDrawableName(int mi)
        {
            if (CurrentModel?.DrawableList != null) foreach (var s in CurrentModel.DrawableList) if (s.MasterIndex == mi) return s.Name;
            if (CurrentModel?.BoneList     != null) foreach (var s in CurrentModel.BoneList)     if (s.MasterIndex == mi) return s.Name;
            return null;
        }

        private void SL(Label l, string t)    { if (l != null) l.text = t; }
        private void Log(string m)            { if (_statusLabel != null) _statusLabel.text = m; }
        private void ML(string m)             { if (_morphStatusLabel != null) _morphStatusLabel.text = m; Log(m); }

        private static float NormAngle(float a) { a %= 360f; if (a > 180f) a -= 360f; if (a < -180f) a += 360f; return a; }
        private static void SMV(Toggle t, bool m) { if (t != null) t.showMixedValue = m; }

        private void MixFT(FloatField f, List<IMeshView> vs, Func<IMeshView, float> g)
        {
            if (f == null || vs.Count == 0) return;
            float v0 = g(vs[0]); bool same = vs.TrueForAll(v => Mathf.Abs(g(v) - v0) < 0.0001f);
            f.SetValueWithoutNotify(same ? (float)System.Math.Round(v0, 4) : 0f);
            f.showMixedValue = !same; f.SetEnabled(true);
        }

        private void MixRTF(FloatField f, Slider s, List<IMeshView> vs, Func<IMeshView, float> g)
        {
            if (f == null || vs.Count == 0) return;
            float v0 = g(vs[0]); bool same = vs.TrueForAll(v => Mathf.Abs(g(v) - v0) < 0.01f);
            float val = same ? v0 : 0f;
            f.SetValueWithoutNotify((float)System.Math.Round(val, 4)); f.showMixedValue = !same; f.SetEnabled(true);
            if (s != null) { s.SetValueWithoutNotify(same ? NormAngle(val) : 0f); s.SetEnabled(same); }
        }

        private static void SF(FloatField f, float v, bool e) { if (f == null) return; f.SetValueWithoutNotify((float)System.Math.Round(v, 4)); f.showMixedValue = false; f.SetEnabled(e); }
        private static void SS(Slider s, float v, bool e)     { if (s != null) { s.SetValueWithoutNotify(v); s.SetEnabled(e); } }

        // ================================================================
        // UIパーツ生成ヘルパー
        // ================================================================

        private T Q<T>(string name) where T : VisualElement => _root?.Q<T>(name);

        private static Button MakeTabBtn(string label, string name)
        {
            var b = new Button { text = label, name = name };
            b.style.flexGrow = 1; b.style.height = 20; b.style.marginRight = 2; b.style.fontSize = 10;
            return b;
        }

        private static Button MakeSmallBtn(string label, string name)
        {
            var b = new Button { text = label, name = name };
            b.style.height = 18; b.style.marginRight = 2; b.style.marginBottom = 2; b.style.fontSize = 10;
            b.style.paddingLeft = 4; b.style.paddingRight = 4; b.style.paddingTop = 0; b.style.paddingBottom = 0;
            return b;
        }

        private static Label MakeInfoLabel(string name = "")
        {
            var l = new Label { name = name };
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginBottom = 1;
            return l;
        }

        private static Label SectionHeader(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginTop = 4; l.style.marginBottom = 1;
            return l;
        }

        private static VisualElement Separator()
        {
            var v = new VisualElement();
            v.style.height = 1; v.style.marginTop = 4; v.style.marginBottom = 4;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        private static VisualElement LabeledRow(string label, VisualElement content)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2; row.style.alignItems = Align.Center;
            var lbl = new Label(label); lbl.style.width = 70; lbl.style.fontSize = 10;
            lbl.style.color = new StyleColor(Color.white);
            row.Add(lbl); content.style.flexGrow = 1; row.Add(content);
            return row;
        }

        private static StyleColor Col(float v) => new StyleColor(new Color(v, v, v));

        private static void AddXYZFields(VisualElement parent, out FloatField fx, out FloatField fy, out FloatField fz, string prefix)
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.marginBottom = 2;
            fx = new FloatField("X") { name = $"{prefix}-x" }; fx.style.flexGrow = 1;
            fx.style.color = new StyleColor(Color.black);
            fy = new FloatField("Y") { name = $"{prefix}-y" }; fy.style.flexGrow = 1;
            fy.style.color = new StyleColor(Color.black);
            fz = new FloatField("Z") { name = $"{prefix}-z" }; fz.style.flexGrow = 1;
            fz.style.color = new StyleColor(Color.black);
            row.Add(fx); row.Add(fy); row.Add(fz); parent.Add(row);
        }

        private void AddRotFields(
            VisualElement parent,
            out FloatField fx, out Slider sx, SetBoneTransformValueCommand.Field tfx,
            out FloatField fy, out Slider sy, SetBoneTransformValueCommand.Field tfy,
            out FloatField fz, out Slider sz, SetBoneTransformValueCommand.Field tfz,
            bool isPose)
        {
            var frow = new VisualElement(); frow.style.flexDirection = FlexDirection.Row; frow.style.marginBottom = 1;
            fx = new FloatField("X"); fx.style.flexGrow = 1;
            fx.style.color = new StyleColor(Color.black);
            fy = new FloatField("Y"); fy.style.flexGrow = 1;
            fy.style.color = new StyleColor(Color.black);
            fz = new FloatField("Z"); fz.style.flexGrow = 1;
            fz.style.color = new StyleColor(Color.black);
            frow.Add(fx); frow.Add(fy); frow.Add(fz); parent.Add(frow);

            var srow = new VisualElement(); srow.style.flexDirection = FlexDirection.Row; srow.style.marginBottom = 2;
            sx = new Slider(-180f, 180f); sx.style.flexGrow = 1;
            sx.style.color = new StyleColor(Color.white);
            sy = new Slider(-180f, 180f); sy.style.flexGrow = 1;
            sy.style.color = new StyleColor(Color.white);
            sz = new Slider(-180f, 180f); sz.style.flexGrow = 1;
            sz.style.color = new StyleColor(Color.white);
            srow.Add(sx); srow.Add(sy); srow.Add(sz); parent.Add(srow);

            if (isPose)
            {
                RegRestRotField(fx, sx, tfx); RegRestRotField(fy, sy, tfy); RegRestRotField(fz, sz, tfz);
                RegRestRotSlider(sx, fx, tfx); RegRestRotSlider(sy, fy, tfy); RegRestRotSlider(sz, fz, tfz);
            }
            else
            {
                RegTRotField(fx, sx, tfx); RegTRotField(fy, sy, tfy); RegTRotField(fz, sz, tfz);
                RegTRotSlider(sx, fx, tfx); RegTRotSlider(sy, fy, tfy); RegTRotSlider(sz, fz, tfz);
            }
        }
    }

    public class SummaryDragValidator : IDragDropValidator<SummaryTreeAdapter>
    {
        public bool CanDrag(SummaryTreeAdapter item) => true;
        public bool CanDrop(SummaryTreeAdapter dragged, SummaryTreeAdapter target, DropPosition position) => true;
    }
}
