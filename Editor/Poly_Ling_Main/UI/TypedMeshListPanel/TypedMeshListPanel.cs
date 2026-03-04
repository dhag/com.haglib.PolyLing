// TypedMeshListPanel.cs
// タイプ別メッシュリストパネル
// MeshListPanelUXMLの機能 + タブでDrawable/Bone/Morphを切り替え
// Model.DrawableMeshes, Model.Bones, Model.Morphsを使用

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using UIList.UIToolkitExtensions;

namespace Poly_Ling.UI
{
    /// <summary>
    /// タイプ別メッシュリストパネル
    /// タブでDrawable/Bone/Morphを切り替え、各タブでMeshListPanelと同等の機能を提供
    /// </summary>
    public partial class TypedMeshListPanel : IToolPanelBaseUXML
    {
        // ================================================================
        // 定数
        // ================================================================

        protected override string UxmlPackagePath => "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/TypedMeshListPanel/TypedMeshListPanel.uxml";
        protected override string UxmlAssetsPath  => "Assets/Editor/Poly_Ling_Main/UI/TypedMeshListPanel/TypedMeshListPanel.uxml";
        protected override string UssPackagePath  => "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/TypedMeshListPanel/TypedMeshListPanel.uss";
        protected override string UssAssetsPath   => "Assets/Editor/Poly_Ling_Main/UI/TypedMeshListPanel/TypedMeshListPanel.uss";

        // 基底クラス設定: OnModelChanged（モデル参照変更）を購読
        protected override bool SubscribeModelReferenceChanged => true;

        private enum TabType { Drawable, Bone, Morph }

        // ================================================================
        // UI要素
        // ================================================================

        private Button _tabDrawable, _tabBone, _tabMorph;
        private VisualElement _mainContent, _morphEditor;
        private TreeView _treeView;
        private Label _countLabel, _statusLabel;
        private Toggle _showInfoToggle;
        private Toggle _showMirrorSideToggle;

        // 詳細パネル
        private Foldout _detailFoldout;
        private TextField _meshNameField;
        private Label _vertexCountLabel, _faceCountLabel;
        private Label _triCountLabel, _quadCountLabel, _ngonCountLabel;
        private VisualElement _indexInfo;
        private Label _boneIndexLabel, _masterIndexLabel;

        // ================================================================
        // データ
        // ================================================================

        [NonSerialized] private TypedTreeRoot _treeRoot;
        [NonSerialized] private TreeViewDragDropHelper<TypedTreeAdapter> _dragDropHelper;

        private TabType _currentTab = TabType.Drawable;
        private List<TypedTreeAdapter> _selectedAdapters = new List<TypedTreeAdapter>();
        private bool _isSyncingFromExternal = false;
        private bool _refreshScheduled = false;

        // モーフテストのプレビュー状態
        private List<(int morphIndex, int baseIndex)> _morphTestChecked = new List<(int, int)>();
        private bool _isSyncingMorphSelection = false;

        // モーフリストのデータソース
        private List<(int masterIndex, string name, string info)> _morphListData = new List<(int, string, string)>();

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

        // ================================================================
        // ウィンドウ
        // ================================================================

        [MenuItem("Tools/Poly_Ling/debug/Typed Mesh List")]
        public static void ShowWindow()
        {
            var window = GetWindow<TypedMeshListPanel>();
            window.titleContent = new GUIContent("Typed Mesh List");
            window.minSize = new Vector2(300, 400);
        }

        public static TypedMeshListPanel Open(ToolContext ctx)
        {
            var window = GetWindow<TypedMeshListPanel>();
            window.titleContent = new GUIContent("Typed Mesh List");
            window.minSize = new Vector2(300, 400);
            window.SetContext(ctx);
            window.Show();
            return window;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        protected override void OnCleanup()
        {
            EndMorphPreview();
            if (_morphListView != null)
                _morphListView.selectionChanged -= OnMorphListSelectionChanged;
            CleanupDragDrop();
        }

        protected override void OnCreateGUI(VisualElement root)
        {
            BindUIElements(root);
            SetupTreeView();
            RegisterButtonEvents();
        }

        // ================================================================
        // コンテキスト設定（基底クラスのSetContextを利用）
        // ================================================================

        protected override void OnContextSet()
        {
            if (Model != null)
            {
                CreateTreeRoot();
                SetupDragDrop();
            }
        }

        protected override void OnModelReferenceChanged()
        {
            if (Model != null)
            {
                CreateTreeRoot();
                SetupDragDrop();
            }
            RefreshAllImmediate();
        }

        private void CreateTreeRoot()
        {
            if (Model == null) return;

            _treeRoot = new TypedTreeRoot(Model, ToolCtx, CurrentCategory);
            _treeRoot.OnChanged = () =>
            {
                _isSyncingFromExternal = true;
                try
                {
                    RefreshTreeImmediate();
                    SyncTreeViewSelection();
                    UpdateDetailPanel();
                    NotifyModelChanged();
                    ToolCtx?.UndoController?.MeshListContext?.OnReorderCompleted?.Invoke();
                }
                finally
                {
                    _isSyncingFromExternal = false;
                }
            };
        }

        protected override void OnModelListChanged()
        {
            if (_isSyncingFromExternal) return;

            _isSyncingFromExternal = true;
            try
            {
                _treeRoot?.Rebuild();
                RefreshAllImmediate();
                SyncTreeViewSelection();

                // モーフタブの場合はモーフエディタも更新
                if (_currentTab == TabType.Morph)
                {
                    RefreshMorphEditor();
                }
            }
            finally
            {
                // TreeView.Rebuild()後の遅延選択イベント抑制
                EditorApplication.delayCall += () => _isSyncingFromExternal = false;
            }
        }

        protected override void OnUndoRedoPerformed()
        {
            _isSyncingFromExternal = true;
            try
            {
                _treeRoot?.Rebuild();
                RefreshAllImmediate();
                SyncTreeViewSelection();

                // モーフタブの場合はモーフエディタも更新
                if (_currentTab == TabType.Morph)
                {
                    EndMorphPreview();
                    _morphTestWeight?.SetValueWithoutNotify(0f);
                    RefreshMorphEditor();
                }
            }
            finally
            {
                // TreeView.Rebuild()後の遅延選択イベント抑制
                EditorApplication.delayCall += () => _isSyncingFromExternal = false;
            }
        }

        private void NotifyModelChanged()
        {
            _isSyncingFromExternal = true;
            if (Model != null)
            {
                Model.IsDirty = true;
                Model.OnListChanged?.Invoke();
            }
            ToolCtx?.SyncMesh?.Invoke();
            ToolCtx?.Repaint?.Invoke();
            _isSyncingFromExternal = false;
        }

        // ================================================================
        // UI構築
        // ================================================================

        private void BindUIElements(VisualElement root)
        {
            // UI要素取得
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

            // タブイベント
            _tabDrawable?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Drawable));
            _tabBone?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Bone));
            _tabMorph?.RegisterCallback<ClickEvent>(_ => SwitchTab(TabType.Morph));

            // Info表示トグル
            _showInfoToggle?.RegisterValueChangedCallback(_ => RefreshTree());
            _showMirrorSideToggle?.RegisterValueChangedCallback(_ => RefreshTree());

            // 名前フィールド
            _meshNameField?.RegisterValueChangedCallback(OnNameFieldChanged);

            // BonePoseセクション（Phase BonePose追加）
            BindBonePoseUI(root);

            // モーフエディタセクション（Phase MorphEditor追加）
            BindMorphEditorUI(root);
        }

        // ================================================================
        // タブ切り替え
        // ================================================================

        private void SwitchTab(TabType tab)
        {
            _currentTab = tab;

            SetTabActive(_tabDrawable, tab == TabType.Drawable);
            SetTabActive(_tabBone, tab == TabType.Bone);
            SetTabActive(_tabMorph, tab == TabType.Morph);

            // インデックス情報表示（ボーンタブのみ）
            if (_indexInfo != null)
                _indexInfo.style.display = tab == TabType.Bone ? DisplayStyle.Flex : DisplayStyle.None;

            // BonePoseセクション表示（ボーンタブのみ）（Phase BonePose追加）
            if (_bonePoseSection != null)
                _bonePoseSection.style.display = tab == TabType.Bone ? DisplayStyle.Flex : DisplayStyle.None;

            // モーフタブはモーフエディタ表示
            bool isMorph = tab == TabType.Morph;
            if (_mainContent != null)
                _mainContent.style.display = isMorph ? DisplayStyle.None : DisplayStyle.Flex;
            if (_morphEditor != null)
                _morphEditor.style.display = isMorph ? DisplayStyle.Flex : DisplayStyle.None;

            // ツリーを再構築
            if (Model != null && !isMorph)
            {
                CreateTreeRoot();
                SetupDragDrop();
            }

            // モーフタブ切り替え時にプレビュー終了＆UI更新
            if (isMorph)
            {
                EndMorphPreview();
                RefreshMorphEditor();
            }

            _selectedAdapters.Clear();
            RefreshAllImmediate();
            Log($"{tab} タブ");
        }

        private void SetTabActive(Button btn, bool active)
        {
            btn?.EnableInClassList("tab-active", active);
        }

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

        /// <summary>
        /// BindTreeItem の Q&lt;&gt;() 呼び出しを排除するためのキャッシュ
        /// </summary>
        private class TreeItemCache
        {
            public Label NameLabel;
            public Label InfoLabel;
            public Button VisBtn;
            public Button LockBtn;
            public Button SymBtn;
        }

        private VisualElement MakeTreeItem()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexGrow = 1;
            container.style.alignItems = Align.Center;
            container.style.paddingLeft = 2;
            container.style.paddingRight = 4;

            var nameLabel = new Label { name = "name" };
            nameLabel.style.flexGrow = 1;
            nameLabel.style.flexShrink = 1;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.marginRight = 4;
            container.Add(nameLabel);

            var infoLabel = new Label { name = "info" };
            infoLabel.style.width = 80;
            infoLabel.style.flexShrink = 0;
            infoLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            infoLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            infoLabel.style.fontSize = 11;
            infoLabel.style.marginRight = 4;
            container.Add(infoLabel);

            var attrContainer = new VisualElement();
            attrContainer.style.flexDirection = FlexDirection.Row;
            attrContainer.style.flexShrink = 0;

            var visBtn = CreateAttributeButton("vis-btn", "👁", "可視性切り替え");
            var lockBtn = CreateAttributeButton("lock-btn", "🔒", "ロック切り替え");
            var symBtn = CreateAttributeButton("sym-btn", "⇆", "対称切り替え");
            attrContainer.Add(visBtn);
            attrContainer.Add(lockBtn);
            attrContainer.Add(symBtn);

            container.Add(attrContainer);

            // キャッシュを保存
            container.userData = new TreeItemCache
            {
                NameLabel = nameLabel,
                InfoLabel = infoLabel,
                VisBtn = visBtn,
                LockBtn = lockBtn,
                SymBtn = symBtn
            };

            return container;
        }

        private Button CreateAttributeButton(string name, string icon, string tooltip)
        {
            var btn = new Button { name = name, text = icon, tooltip = tooltip };
            btn.style.width = 24;
            btn.style.height = 18;
            btn.style.marginLeft = 1;
            btn.style.marginRight = 1;
            btn.style.paddingLeft = 0;
            btn.style.paddingRight = 0;
            btn.style.paddingTop = 0;
            btn.style.paddingBottom = 0;
            btn.style.fontSize = 12;
            btn.style.borderTopWidth = 0;
            btn.style.borderBottomWidth = 0;
            btn.style.borderLeftWidth = 0;
            btn.style.borderRightWidth = 0;
            btn.style.backgroundColor = new Color(0, 0, 0, 0);
            return btn;
        }

        private void BindTreeItem(VisualElement element, int index)
        {
            var adapter = _treeView.GetItemDataForIndex<TypedTreeAdapter>(index);
            if (adapter == null) return;

            var cache = element.userData as TreeItemCache;
            if (cache == null)
            {
                // フォールバック（通常到達しない）
                cache = new TreeItemCache
                {
                    NameLabel = element.Q<Label>("name"),
                    InfoLabel = element.Q<Label>("info"),
                    VisBtn = element.Q<Button>("vis-btn"),
                    LockBtn = element.Q<Button>("lock-btn"),
                    SymBtn = element.Q<Button>("sym-btn")
                };
                element.userData = cache;
            }

            // ミラー状態判定
            bool isMirrorSide = (adapter.MeshContext?.IsBakedMirror == true)
                             || (Model != null && adapter.MeshContext != null && Model.IsMirrorSide(adapter.MeshContext));
            bool isRealSide = Model != null && adapter.MeshContext != null && Model.IsRealSide(adapter.MeshContext);
            bool hasBakedMirrorChild = adapter.MeshContext?.HasBakedMirrorChild ?? false;

            if (cache.NameLabel != null)
            {
                if (isMirrorSide)
                {
                    cache.NameLabel.text = $"🪞 {adapter.DisplayName}";
                    cache.NameLabel.style.opacity = 0.4f;
                }
                else if (isRealSide)
                {
                    cache.NameLabel.text = $"⇆ {adapter.DisplayName}";
                    cache.NameLabel.style.opacity = 1f;
                }
                else if (hasBakedMirrorChild)
                {
                    cache.NameLabel.text = $"⇆B {adapter.DisplayName}";
                    cache.NameLabel.style.opacity = 1f;
                }
                else
                {
                    cache.NameLabel.text = adapter.DisplayName;
                    cache.NameLabel.style.opacity = 1f;
                }
            }

            if (cache.InfoLabel != null)
            {
                bool showInfo = _showInfoToggle?.value ?? true;
                if (_currentTab == TabType.Bone)
                {
                    int boneIdx = Model?.TypedIndices.MasterToBoneIndex(adapter.MasterIndex) ?? -1;
                    cache.InfoLabel.text = showInfo ? $"Bone:{boneIdx}" : "";
                }
                else
                {
                    cache.InfoLabel.text = showInfo ? adapter.GetInfoString() : "";
                }
                cache.InfoLabel.style.display = showInfo ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // 属性ボタン
            if (cache.VisBtn != null)
            {
                UpdateAttributeButton(cache.VisBtn, adapter.IsVisible, "👁", "−");
                cache.VisBtn.clickable = new Clickable(() => OnVisibilityToggle(adapter));
            }

            if (cache.LockBtn != null)
            {
                UpdateAttributeButton(cache.LockBtn, adapter.IsLocked, "🔒", "🔓");
                cache.LockBtn.clickable = new Clickable(() => OnLockToggle(adapter));
            }

            if (cache.SymBtn != null)
            {
                bool hasMirror = adapter.MirrorType > 0 || adapter.IsBakedMirror || isMirrorSide || isRealSide || hasBakedMirrorChild;
                string symIcon;
                if (isMirrorSide)
                    symIcon = "🪞";
                else if (isRealSide)
                    symIcon = "⇆";
                else if (hasBakedMirrorChild)
                    symIcon = "⇆B";
                else
                    symIcon = adapter.GetMirrorTypeDisplay();
                UpdateAttributeButton(cache.SymBtn, hasMirror, symIcon, "");
                cache.SymBtn.clickable = new Clickable(() => OnSymmetryToggle(adapter));
                cache.SymBtn.style.display = _currentTab == TabType.Drawable ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void UpdateAttributeButton(Button btn, bool isActive, string activeIcon, string inactiveIcon)
        {
            btn.text = isActive ? activeIcon : inactiveIcon;
            btn.style.opacity = isActive ? 1f : 0.3f;
        }

        // ================================================================
        // 属性トグル
        // ================================================================

        private void OnVisibilityToggle(TypedTreeAdapter adapter)
        {
            int index = adapter.MasterIndex;
            if (index < 0) return;

            bool newValue = !adapter.IsVisible;
            ToolCtx?.UpdateMeshAttributes?.Invoke(new[]
            {
                new MeshAttributeChange { Index = index, IsVisible = newValue }
            });
            Log($"可視性: {adapter.DisplayName} → {(newValue ? "表示" : "非表示")}");
        }

        /// <summary>
        /// 選択中のアイテムの可視性をまとめて設定
        /// </summary>
        private void SetSelectedVisibility(bool visible)
        {
            if (_selectedAdapters.Count == 0) return;

            var changes = new List<MeshAttributeChange>();
            foreach (var adapter in _selectedAdapters)
            {
                int idx = adapter.MasterIndex;
                if (idx < 0) continue;
                if (adapter.IsVisible == visible) continue; // 既に同じ状態ならスキップ
                changes.Add(new MeshAttributeChange { Index = idx, IsVisible = visible });
            }

            if (changes.Count == 0) return;

            ToolCtx?.UpdateMeshAttributes?.Invoke(changes.ToArray());
            Log($"一括{(visible ? "表示" : "非表示")}: {changes.Count}件");
        }

        private void OnLockToggle(TypedTreeAdapter adapter)
        {
            int index = adapter.MasterIndex;
            if (index < 0) return;

            bool newValue = !adapter.IsLocked;
            ToolCtx?.UpdateMeshAttributes?.Invoke(new[]
            {
                new MeshAttributeChange { Index = index, IsLocked = newValue }
            });
            Log($"ロック: {adapter.DisplayName} → {(newValue ? "ロック" : "解除")}");
        }


        // ================================================================
        // 選択
        // ================================================================

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            var oldIndices = _selectedAdapters.Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();

            _selectedAdapters.Clear();
            foreach (var item in selection)
            {
                if (item is TypedTreeAdapter adapter)
                {
                    // MirrorPairのミラー側・BakedMirrorは選択不可
                    if (adapter.MeshContext?.IsBakedMirror == true
                        || (Model != null && adapter.MeshContext != null && Model.IsMirrorSide(adapter.MeshContext)))
                        continue;
                    _selectedAdapters.Add(adapter);
                }
            }

            var newIndices = _selectedAdapters.Select(a => a.MasterIndex).Where(i => i >= 0).ToArray();

            // 外部同期中はUndo記録と本体通知をスキップ
            if (!_isSyncingFromExternal)
            {
                // フラグを立てて、以降のコールバックでRebuildを防ぐ
                _isSyncingFromExternal = true;
                try
                {
                    // Undo記録（変化があった場合のみ）
                    if (!oldIndices.SequenceEqual(newIndices))
                        RecordMultiSelectionChange(oldIndices, newIndices);

                    // ModelContextの選択も更新（カテゴリ別）
                    if (_treeRoot != null)
                    {
                        _treeRoot.SelectMultiple(_selectedAdapters);
                    }

                    // v2.1: 複数選択対応 - SelectMeshContextは単一選択になるため呼ばない
                    // 代わりにOnMeshSelectionChangedでGPUバッファを同期
                    ToolCtx?.OnMeshSelectionChanged?.Invoke();

                    // 本体エディタに反映
                    if (Model != null)
                    {
                        Model.IsDirty = true;
                        Model.OnListChanged?.Invoke();
                    }
                    ToolCtx?.SyncMesh?.Invoke();
                    ToolCtx?.Repaint?.Invoke();
                }
                finally
                {
                    _isSyncingFromExternal = false;
                }
            }

            UpdateDetailPanel();
        }

        private void RecordMultiSelectionChange(int[] oldIndices, int[] newIndices)
        {
            var undoController = ToolCtx?.UndoController;
            if (undoController == null) return;

            var record = new MeshMultiSelectionChangeRecord(oldIndices, newIndices);
            undoController.MeshListStack.Record(record, "メッシュ選択変更");
            undoController.FocusMeshList();
        }

        private void OnItemExpandedChanged(TreeViewExpansionChangedArgs args)
        {
            var adapter = _treeRoot?.FindById(args.id);
            if (adapter != null)
            {
                adapter.IsExpanded = _treeView.IsExpanded(args.id);
                // 折り畳み状態変更を保存対象に
                if (Model != null)
                    Model.IsDirty = true;
            }
        }

        private void SyncTreeViewSelection()
        {
            if (_treeView == null || _treeRoot == null || Model == null) return;

            // v2.0: カテゴリに応じた選択セットのみを参照
            IEnumerable<int> selectedIndices = CurrentCategory switch
            {
                MeshCategory.Drawable => Model.SelectedMeshIndices,
                MeshCategory.Bone => Model.SelectedBoneIndices,
                MeshCategory.Morph => Model.SelectedMorphIndices,
                _ => Model.SelectedMeshIndices
            };

            var selectedIds = new List<int>();
            foreach (var idx in selectedIndices)
            {
                var adapter = _treeRoot.GetAdapterByMasterIndex(idx);
                if (adapter != null)
                    selectedIds.Add(adapter.Id);
            }

            _isSyncingFromExternal = true;
            try
            {
                _treeView.SetSelectionWithoutNotify(selectedIds);
            }
            finally
            {
                _isSyncingFromExternal = false;
            }
        }

        // ================================================================
        // D&D
        // ================================================================

        private void SetupDragDrop()
        {
            CleanupDragDrop();
            if (_treeView == null || _treeRoot == null) return;

            _treeView.RegisterCallback<PointerDownEvent>(OnTreeViewPointerDown, TrickleDown.TrickleDown);

            _dragDropHelper = new TreeViewDragDropHelper<TypedTreeAdapter>(
                _treeView,
                _treeRoot,
                new TypedDragValidator()
            );
            _dragDropHelper.Setup();
        }

        private void CleanupDragDrop()
        {
            if (_treeView != null)
                _treeView.UnregisterCallback<PointerDownEvent>(OnTreeViewPointerDown, TrickleDown.TrickleDown);
            _dragDropHelper?.Cleanup();
            _dragDropHelper = null;
        }

        private void OnTreeViewPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            _treeRoot?.SavePreChangeSnapshot();
        }

        // ================================================================
        // ボタンイベント
        // ================================================================

        private void RegisterButtonEvents()
        {
            var root = rootVisualElement;

            root.Q<Button>("btn-add")?.RegisterCallback<ClickEvent>(_ => OnAdd());
            root.Q<Button>("btn-up")?.RegisterCallback<ClickEvent>(_ => MoveSelected(-1));
            root.Q<Button>("btn-down")?.RegisterCallback<ClickEvent>(_ => MoveSelected(1));
            root.Q<Button>("btn-outdent")?.RegisterCallback<ClickEvent>(_ => OutdentSelected());
            root.Q<Button>("btn-indent")?.RegisterCallback<ClickEvent>(_ => IndentSelected());
            root.Q<Button>("btn-duplicate")?.RegisterCallback<ClickEvent>(_ => DuplicateSelected());
            root.Q<Button>("btn-delete")?.RegisterCallback<ClickEvent>(_ => DeleteSelected());
            root.Q<Button>("btn-show")?.RegisterCallback<ClickEvent>(_ => SetSelectedVisibility(true));
            root.Q<Button>("btn-hide")?.RegisterCallback<ClickEvent>(_ => SetSelectedVisibility(false));
            root.Q<Button>("btn-to-top")?.RegisterCallback<ClickEvent>(_ => MoveToTop());
            root.Q<Button>("btn-to-bottom")?.RegisterCallback<ClickEvent>(_ => MoveToBottom());
        }

        private void OnNameFieldChanged(ChangeEvent<string> evt)
        {
            if (_selectedAdapters.Count == 1 && !string.IsNullOrEmpty(evt.newValue))
            {
                var adapter = _selectedAdapters[0];
                ToolCtx?.UpdateMeshAttributes?.Invoke(new[]
                {
                    new MeshAttributeChange { Index = adapter.MasterIndex, Name = evt.newValue }
                });
                Log($"名前変更: {evt.newValue}");
            }
        }

        // ================================================================
        // 操作メソッド
        // ================================================================

        private void OnAdd()
        {
            if (ToolCtx?.AddMeshContext == null) return;

            var newCtx = new MeshContext
            {
                MeshObject = new MeshObject("New Mesh"),
                UnityMesh = new Mesh(),
                OriginalPositions = new Vector3[0]
            };
            ToolCtx.AddMeshContext(newCtx);
            _treeRoot?.Rebuild();
            RefreshDeferred();
            Log("新規追加");
        }

        private void MoveSelected(int direction)
        {
            if (_selectedAdapters.Count == 0)
            {
                Log("選択なし");
                return;
            }

            if (_treeRoot == null) return;

            bool success = TreeViewHelper.MoveItems(
                _selectedAdapters,
                _treeRoot.RootItems,
                direction
            );

            if (success)
            {
                _treeRoot.OnTreeChanged();
                Log(direction < 0 ? "上へ移動" : "下へ移動");
            }
            else
            {
                Log(direction < 0 ? "これ以上上に移動できない" : "これ以上下に移動できない");
            }
        }

        private void OutdentSelected()
        {
            if (_selectedAdapters.Count != 1)
            {
                Log("1つだけ選択してください");
                return;
            }

            if (_treeRoot == null) return;

            bool success = TreeViewHelper.Outdent(_selectedAdapters[0], _treeRoot.RootItems);

            if (success)
            {
                TreeViewHelper.RebuildParentReferences(_treeRoot.RootItems);
                _treeRoot.OnTreeChanged();
                Log("階層を上げた");
            }
            else
            {
                Log("これ以上外に出せない");
            }
        }

        private void IndentSelected()
        {
            if (_selectedAdapters.Count != 1)
            {
                Log("1つだけ選択してください");
                return;
            }

            if (_treeRoot == null) return;

            bool success = TreeViewHelper.Indent(_selectedAdapters[0], _treeRoot.RootItems);

            if (success)
            {
                TreeViewHelper.RebuildParentReferences(_treeRoot.RootItems);
                _treeRoot.OnTreeChanged();
                Log("階層を下げた");
            }
            else
            {
                Log("上に兄弟がいない");
            }
        }

        private void DuplicateSelected()
        {
            if (_selectedAdapters.Count == 0)
            {
                Log("選択なし");
                return;
            }

            foreach (var adapter in _selectedAdapters.ToList())
            {
                int index = adapter.MasterIndex;
                if (index >= 0)
                    ToolCtx?.DuplicateMeshContent?.Invoke(index);
            }

            _treeRoot?.Rebuild();
            RefreshDeferred();
            Log($"複製: {_selectedAdapters.Count}個");
        }

        private void DeleteSelected()
        {
            if (_selectedAdapters.Count == 0)
            {
                Log("選択なし");
                return;
            }

            string msg = _selectedAdapters.Count == 1
                ? $"'{_selectedAdapters[0].DisplayName}' を削除しますか？"
                : $"{_selectedAdapters.Count}個を削除しますか？";

            if (!EditorUtility.DisplayDialog("削除確認", msg, "削除", "キャンセル"))
                return;

            foreach (var adapter in _selectedAdapters.OrderByDescending(a => a.MasterIndex))
            {
                int index = adapter.MasterIndex;
                if (index >= 0)
                    ToolCtx?.RemoveMeshContext?.Invoke(index);
            }

            _selectedAdapters.Clear();
            _treeRoot?.Rebuild();
            RefreshDeferred();
            Log("削除完了");
        }

        private void MoveToTop()
        {
            if (_selectedAdapters.Count == 0)
            {
                Log("選択なし");
                return;
            }

            if (_treeRoot == null) return;

            var item = _selectedAdapters[0];
            var siblings = item.Parent?.Children ?? _treeRoot.RootItems;

            if (siblings.IndexOf(item) > 0)
            {
                siblings.Remove(item);
                siblings.Insert(0, item);
                _treeRoot.OnTreeChanged();
                Log("先頭へ移動");
            }
        }

        private void MoveToBottom()
        {
            if (_selectedAdapters.Count == 0)
            {
                Log("選択なし");
                return;
            }

            if (_treeRoot == null) return;

            var item = _selectedAdapters[0];
            var siblings = item.Parent?.Children ?? _treeRoot.RootItems;

            if (siblings.IndexOf(item) < siblings.Count - 1)
            {
                siblings.Remove(item);
                siblings.Add(item);
                _treeRoot.OnTreeChanged();
                Log("末尾へ移動");
            }
        }

        // ================================================================
        // 更新
        // ================================================================

        protected override void RefreshAll()
        {
            RefreshAllImmediate();
        }

        private void RefreshDeferred()
        {
            RefreshTree();
            UpdateHeader();
            UpdateDetailPanel();
        }

        /// <summary>
        /// 即時リフレッシュ（初期化・タブ切替・D&D完了後など遅延不可の場面用）
        /// </summary>
        private void RefreshAllImmediate()
        {
            RefreshTreeImmediate();
            UpdateHeader();
            UpdateDetailPanel();
        }

        private void RefreshTree()
        {
            if (_treeView == null || _treeRoot == null) return;

            if (_refreshScheduled) return;
            _refreshScheduled = true;

            EditorApplication.delayCall += () =>
            {
                _refreshScheduled = false;
                if (_treeView == null || _treeRoot == null) return;

                _treeRoot.Rebuild(GetMirrorSideFilter());
                var treeData = TreeViewHelper.BuildTreeData(_treeRoot.RootItems);
                _treeView.SetRootItems(treeData);
                _treeView.Rebuild();

                RestoreExpandedStates(_treeRoot.RootItems);
            };
        }

        /// <summary>
        /// 即時リフレッシュ（タブ切り替え等、遅延不可の場面用）
        /// </summary>
        private void RefreshTreeImmediate()
        {
            if (_treeView == null || _treeRoot == null) return;
            _refreshScheduled = false;

            _treeRoot.Rebuild(GetMirrorSideFilter());
            var treeData = TreeViewHelper.BuildTreeData(_treeRoot.RootItems);
            _treeView.SetRootItems(treeData);
            _treeView.Rebuild();

            RestoreExpandedStates(_treeRoot.RootItems);
        }

        /// <summary>
        /// ミラートグルに応じたフィルタを返す（トグルOFF時にミラー側を除外）
        /// </summary>
        private Predicate<MeshContext> GetMirrorSideFilter()
        {
            bool showMirror = _showMirrorSideToggle?.value ?? false;
            if (showMirror) return null; // フィルタなし
            return mc => Model != null && Model.IsMirrorSide(mc);
        }

        private void RestoreExpandedStates(List<TypedTreeAdapter> items)
        {
            // 展開済みアイテムのみ処理（CollapseItemはデフォルト状態なので不要）
            foreach (var item in items)
            {
                if (item.IsExpanded)
                    _treeView.ExpandItem(item.Id, false);

                if (item.HasChildren)
                    RestoreExpandedStates(item.Children);
            }
        }

        private void UpdateHeader()
        {
            if (_countLabel == null) return;

            string label = _currentTab switch
            {
                TabType.Drawable => "メッシュ",
                TabType.Bone => "ボーン",
                TabType.Morph => "モーフ",
                _ => "Items"
            };
            _countLabel.text = $"{label}: {_treeRoot?.TotalCount ?? 0}";
        }

        private void UpdateDetailPanel()
        {
            if (_selectedAdapters.Count == 0)
            {
                _meshNameField?.SetValueWithoutNotify("");
                if (_vertexCountLabel != null) _vertexCountLabel.text = "頂点: -";
                if (_faceCountLabel != null) _faceCountLabel.text = "面: -";
                if (_triCountLabel != null) _triCountLabel.text = "三角形: -";
                if (_quadCountLabel != null) _quadCountLabel.text = "四角形: -";
                if (_ngonCountLabel != null) _ngonCountLabel.text = "多角形: -";
                if (_boneIndexLabel != null) _boneIndexLabel.text = "ボーンIdx: -";
                if (_masterIndexLabel != null) _masterIndexLabel.text = "マスターIdx: -";
                _detailFoldout?.SetEnabled(false);
                return;
            }

            _detailFoldout?.SetEnabled(true);

            if (_selectedAdapters.Count == 1)
            {
                var adapter = _selectedAdapters[0];
                var meshObj = adapter.Entry.MeshObject;

                _meshNameField?.SetValueWithoutNotify(adapter.DisplayName);
                _meshNameField?.SetEnabled(true);

                if (meshObj != null)
                {
                    if (_vertexCountLabel != null) _vertexCountLabel.text = $"頂点: {meshObj.VertexCount}";
                    if (_faceCountLabel != null) _faceCountLabel.text = $"面: {meshObj.FaceCount}";

                    int tri = 0, quad = 0, ngon = 0;
                    foreach (var face in meshObj.Faces)
                    {
                        if (face.IsTriangle) tri++;
                        else if (face.IsQuad) quad++;
                        else ngon++;
                    }
                    if (_triCountLabel != null) _triCountLabel.text = $"三角形: {tri}";
                    if (_quadCountLabel != null) _quadCountLabel.text = $"四角形: {quad}";
                    if (_ngonCountLabel != null) _ngonCountLabel.text = $"多角形: {ngon}";
                }

                int boneIdx = Model?.TypedIndices.MasterToBoneIndex(adapter.MasterIndex) ?? -1;
                if (_boneIndexLabel != null) _boneIndexLabel.text = $"ボーンIdx: {boneIdx}";
                if (_masterIndexLabel != null) _masterIndexLabel.text = $"マスターIdx: {adapter.MasterIndex}";
            }
            else
            {
                _meshNameField?.SetValueWithoutNotify($"({_selectedAdapters.Count}個選択)");
                _meshNameField?.SetEnabled(false);

                int totalV = _selectedAdapters.Sum(a => a.VertexCount);
                int totalF = _selectedAdapters.Sum(a => a.FaceCount);
                if (_vertexCountLabel != null) _vertexCountLabel.text = $"頂点: {totalV} (合計)";
                if (_faceCountLabel != null) _faceCountLabel.text = $"面: {totalF} (合計)";
            }

            // BonePose更新（Phase BonePose追加）
            UpdateBonePosePanel();
        }

        private void Log(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        // ================================================================
    }

    /// <summary>D&Dバリデータ</summary>
    public class TypedDragValidator : IDragDropValidator<TypedTreeAdapter>
    {
        public bool CanDrag(TypedTreeAdapter item) => true;
        public bool CanDrop(TypedTreeAdapter dragged, TypedTreeAdapter target, DropPosition position) => true;
    }
}
