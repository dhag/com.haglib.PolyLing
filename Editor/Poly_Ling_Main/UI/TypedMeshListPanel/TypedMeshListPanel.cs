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
    public class TypedMeshListPanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // 定数
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/TypedMeshListPanel/TypedMeshListPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/TypedMeshListPanel/TypedMeshListPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/TypedMeshListPanel/TypedMeshListPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/TypedMeshListPanel/TypedMeshListPanel.uss";

        private enum TabType { Drawable, Bone, Morph }

        // ================================================================
        // UI要素
        // ================================================================

        private Button _tabDrawable, _tabBone, _tabMorph;
        private VisualElement _mainContent, _morphEditor;
        private TreeView _treeView;
        private Label _countLabel, _statusLabel;
        private Toggle _showInfoToggle;

        // モーフエディタUI（Phase MorphEditor v2: UIToolkit ListView）
        private Label _morphCountLabel, _morphStatusLabel;
        private ListView _morphListView;
        private Slider _morphTestWeight;

        // 変換セクション
        private VisualElement _morphSourceMeshPopupContainer;
        private VisualElement _morphParentPopupContainer;
        private VisualElement _morphPanelPopupContainer;
        private TextField _morphNameField;
        private Button _btnMeshToMorph, _btnMorphToMesh;
        private PopupField<int> _morphSourceMeshPopup;
        private PopupField<int> _morphParentPopup;
        private PopupField<int> _morphPanelPopup;

        // モーフセット
        private TextField _morphSetNameField;
        private VisualElement _morphSetTypePopupContainer;
        private PopupField<int> _morphSetTypePopup;
        private Button _btnCreateMorphSet;

        // 詳細パネル
        private Foldout _detailFoldout;
        private TextField _meshNameField;
        private Label _vertexCountLabel, _faceCountLabel;
        private Label _triCountLabel, _quadCountLabel, _ngonCountLabel;
        private VisualElement _indexInfo;
        private Label _boneIndexLabel, _masterIndexLabel;

        // BonePoseセクション（Phase BonePose追加）
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
        private bool _isSyncingPoseUI = false;

        // BonePose Undo用（スライダードラッグ中のスナップショット保持）
        private Dictionary<int, BonePoseDataSnapshot> _sliderDragBeforeSnapshots = new Dictionary<int, BonePoseDataSnapshot>();

        // ================================================================
        // データ
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        [NonSerialized] private TypedTreeRoot _treeRoot;
        [NonSerialized] private TreeViewDragDropHelper<TypedTreeAdapter> _dragDropHelper;
        [NonSerialized] private ModelContext _subscribedModel;

        private TabType _currentTab = TabType.Drawable;
        private List<TypedTreeAdapter> _selectedAdapters = new List<TypedTreeAdapter>();
        private bool _isSyncingFromExternal = false;
        private bool _refreshScheduled = false;

        // モーフテストのプレビュー状態
        private bool _isMorphPreviewActive = false;
        private Dictionary<int, Vector3[]> _morphPreviewBackups = new Dictionary<int, Vector3[]>();
        private List<(int morphIndex, int baseIndex)> _morphTestChecked = new List<(int, int)>();
        private bool _isSyncingMorphSelection = false;

        // モーフリストのデータソース
        private List<(int masterIndex, string name, string info)> _morphListData = new List<(int, string, string)>();

        // ================================================================
        // プロパティ
        // ================================================================

        private ModelContext Model => _toolContext?.Model;

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

        private void OnDisable() => Cleanup();
        private void OnDestroy() => Cleanup();

        private void Cleanup()
        {
            EndMorphPreview();
            if (_morphListView != null)
                _morphListView.selectionChanged -= OnMorphListSelectionChanged;
            UnsubscribeFromModel();
            if (_toolContext != null)
            {
                if (_toolContext.UndoController != null)
                    _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
                _toolContext.OnModelChanged -= OnModelReferenceChanged;
            }
            CleanupDragDrop();
        }

        private void CreateGUI()
        {
            BuildUI();
            SetupTreeView();
            RegisterButtonEvents();
            RefreshAllImmediate();
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            UnsubscribeFromModel();
            if (_toolContext != null)
            {
                if (_toolContext.UndoController != null)
                    _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
                _toolContext.OnModelChanged -= OnModelReferenceChanged;
            }

            _toolContext = ctx;

            if (_toolContext != null)
            {
                _toolContext.OnModelChanged += OnModelReferenceChanged;

                if (_toolContext.Model != null)
                {
                    CreateTreeRoot();
                    SetupDragDrop();
                    SubscribeToModel();

                    if (_toolContext.UndoController != null)
                        _toolContext.UndoController.OnUndoRedoPerformed += OnUndoRedoPerformed;

                    RefreshAllImmediate();
                }
            }
        }

        /// <summary>
        /// ToolContext.Modelの参照が差し替わったときに呼ばれる
        /// 旧Modelの購読を解除し、新Modelに再購読してリフレッシュする
        /// </summary>
        private void OnModelReferenceChanged()
        {
            UnsubscribeFromModel();

            if (_toolContext?.Model != null)
            {
                CreateTreeRoot();
                SetupDragDrop();
                SubscribeToModel();

                if (_toolContext.UndoController != null)
                {
                    _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
                    _toolContext.UndoController.OnUndoRedoPerformed += OnUndoRedoPerformed;
                }

                RefreshAllImmediate();
            }
        }

        private void CreateTreeRoot()
        {
            if (Model == null) return;

            _treeRoot = new TypedTreeRoot(Model, _toolContext, CurrentCategory);
            _treeRoot.OnChanged = () =>
            {
                _isSyncingFromExternal = true;
                try
                {
                    RefreshTreeImmediate();
                    SyncTreeViewSelection();
                    UpdateDetailPanel();
                    NotifyModelChanged();
                    _toolContext?.UndoController?.MeshListContext?.OnReorderCompleted?.Invoke();
                }
                finally
                {
                    _isSyncingFromExternal = false;
                }
            };
        }

        private void SubscribeToModel()
        {
            if (Model != null)
            {
                Model.OnListChanged += OnModelListChanged;
                _subscribedModel = Model;
            }
        }

        private void UnsubscribeFromModel()
        {
            if (_subscribedModel != null)
            {
                _subscribedModel.OnListChanged -= OnModelListChanged;
                _subscribedModel = null;
            }
        }

        private void OnModelListChanged()
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
                // TreeView.Rebuild()後に遅延選択イベントが発火するため、
                // 即座にフラグを解除するとOnSelectionChangedが偽イベントを処理してしまう。
                // delayCallで次フレームまで抑制を維持する。
                EditorApplication.delayCall += () => _isSyncingFromExternal = false;
            }
        }

        private void OnUndoRedoPerformed()
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
            _toolContext?.SyncMesh?.Invoke();
            _toolContext?.Repaint?.Invoke();
            _isSyncingFromExternal = false;
        }

        // ================================================================
        // UI構築
        // ================================================================

        private void BuildUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)
                          ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPathAssets);

            if (visualTree != null)
            {
                visualTree.CloneTree(root);
            }
            else
            {
                root.Add(new Label($"UXML not found: {UxmlPath}"));
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath)
                          ?? AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPathAssets);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            // UI要素取得
            _tabDrawable = root.Q<Button>("tab-drawable");
            _tabBone = root.Q<Button>("tab-bone");
            _tabMorph = root.Q<Button>("tab-morph");

            _mainContent = root.Q<VisualElement>("main-content");
            _morphEditor = root.Q<VisualElement>("morph-editor");

            _treeView = root.Q<TreeView>("mesh-tree");
            _countLabel = root.Q<Label>("count-label");
            _showInfoToggle = root.Q<Toggle>("show-info-toggle");
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

            if (cache.NameLabel != null)
                cache.NameLabel.text = adapter.DisplayName;

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
                bool hasMirror = adapter.MirrorType > 0 || adapter.IsBakedMirror;
                UpdateAttributeButton(cache.SymBtn, hasMirror, adapter.GetMirrorTypeDisplay(), "");
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
            _toolContext?.UpdateMeshAttributes?.Invoke(new[]
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

            _toolContext?.UpdateMeshAttributes?.Invoke(changes.ToArray());
            Log($"一括{(visible ? "表示" : "非表示")}: {changes.Count}件");
        }

        private void OnLockToggle(TypedTreeAdapter adapter)
        {
            int index = adapter.MasterIndex;
            if (index < 0) return;

            bool newValue = !adapter.IsLocked;
            _toolContext?.UpdateMeshAttributes?.Invoke(new[]
            {
                new MeshAttributeChange { Index = index, IsLocked = newValue }
            });
            Log($"ロック: {adapter.DisplayName} → {(newValue ? "ロック" : "解除")}");
        }

        private void OnSymmetryToggle(TypedTreeAdapter adapter)
        {
            if (adapter.IsBakedMirror)
            {
                Log("ベイクドミラーは対称設定を変更できません");
                return;
            }

            int index = adapter.MasterIndex;
            if (index < 0) return;

            int newMirrorType = (adapter.MirrorType + 1) % 4;
            _toolContext?.UpdateMeshAttributes?.Invoke(new[]
            {
                new MeshAttributeChange { Index = index, MirrorType = newMirrorType }
            });

            string[] mirrorNames = { "なし", "X軸", "Y軸", "Z軸" };
            Log($"対称: {adapter.DisplayName} → {mirrorNames[newMirrorType]}");
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
                    _selectedAdapters.Add(adapter);
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
                    _toolContext?.OnMeshSelectionChanged?.Invoke();

                    // 本体エディタに反映
                    if (Model != null)
                    {
                        Model.IsDirty = true;
                        Model.OnListChanged?.Invoke();
                    }
                    _toolContext?.SyncMesh?.Invoke();
                    _toolContext?.Repaint?.Invoke();
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
            var undoController = _toolContext?.UndoController;
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
                _toolContext?.UpdateMeshAttributes?.Invoke(new[]
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
            if (_toolContext?.AddMeshContext == null) return;

            var newCtx = new MeshContext
            {
                MeshObject = new MeshObject("New Mesh"),
                UnityMesh = new Mesh(),
                OriginalPositions = new Vector3[0]
            };
            _toolContext.AddMeshContext(newCtx);
            _treeRoot?.Rebuild();
            RefreshAll();
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
                    _toolContext?.DuplicateMeshContent?.Invoke(index);
            }

            _treeRoot?.Rebuild();
            RefreshAll();
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
                    _toolContext?.RemoveMeshContext?.Invoke(index);
            }

            _selectedAdapters.Clear();
            _treeRoot?.Rebuild();
            RefreshAll();
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

        private void RefreshAll()
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

            var treeData = TreeViewHelper.BuildTreeData(_treeRoot.RootItems);
            _treeView.SetRootItems(treeData);
            _treeView.Rebuild();

            RestoreExpandedStates(_treeRoot.RootItems);
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
        // BonePose セクション（Phase BonePose追加）
        // ================================================================

        /// <summary>
        /// BonePoseセクションのUI要素をバインド
        /// </summary>
        private void BindBonePoseUI(VisualElement root)
        {
            _bonePoseSection = root.Q<VisualElement>("bone-pose-section");
            _poseFoldout = root.Q<Foldout>("pose-foldout");
            _bindposeFoldout = root.Q<Foldout>("bindpose-foldout");

            _poseActiveToggle = root.Q<Toggle>("pose-active-toggle");

            _restPosX = root.Q<FloatField>("rest-pos-x");
            _restPosY = root.Q<FloatField>("rest-pos-y");
            _restPosZ = root.Q<FloatField>("rest-pos-z");
            _restRotX = root.Q<FloatField>("rest-rot-x");
            _restRotY = root.Q<FloatField>("rest-rot-y");
            _restRotZ = root.Q<FloatField>("rest-rot-z");
            _restRotSliderX = root.Q<Slider>("rest-rot-slider-x");
            _restRotSliderY = root.Q<Slider>("rest-rot-slider-y");
            _restRotSliderZ = root.Q<Slider>("rest-rot-slider-z");
            _restSclX = root.Q<FloatField>("rest-scl-x");
            _restSclY = root.Q<FloatField>("rest-scl-y");
            _restSclZ = root.Q<FloatField>("rest-scl-z");

            _poseLayersContainer = root.Q<VisualElement>("pose-layers-container");
            _poseNoLayersLabel = root.Q<Label>("pose-no-layers-label");

            _poseResultPos = root.Q<Label>("pose-result-pos");
            _poseResultRot = root.Q<Label>("pose-result-rot");

            _btnInitPose = root.Q<Button>("btn-init-pose");
            _btnResetLayers = root.Q<Button>("btn-reset-layers");

            _bindposePos = root.Q<Label>("bindpose-pos");
            _bindposeRot = root.Q<Label>("bindpose-rot");
            _bindposeScl = root.Q<Label>("bindpose-scl");
            _btnBakePose = root.Q<Button>("btn-bake-pose");

            // イベント登録
            _poseActiveToggle?.RegisterValueChangedCallback(OnPoseActiveChanged);

            RegisterRestPoseField(_restPosX, (pose, v) => pose.RestPosition = SetX(pose.RestPosition, v));
            RegisterRestPoseField(_restPosY, (pose, v) => pose.RestPosition = SetY(pose.RestPosition, v));
            RegisterRestPoseField(_restPosZ, (pose, v) => pose.RestPosition = SetZ(pose.RestPosition, v));

            RegisterRestRotField(_restRotX, 0);
            RegisterRestRotField(_restRotY, 1);
            RegisterRestRotField(_restRotZ, 2);

            RegisterRestRotSlider(_restRotSliderX, 0);
            RegisterRestRotSlider(_restRotSliderY, 1);
            RegisterRestRotSlider(_restRotSliderZ, 2);

            RegisterRestPoseField(_restSclX, (pose, v) => pose.RestScale = SetX(pose.RestScale, v));
            RegisterRestPoseField(_restSclY, (pose, v) => pose.RestScale = SetY(pose.RestScale, v));
            RegisterRestPoseField(_restSclZ, (pose, v) => pose.RestScale = SetZ(pose.RestScale, v));

            _btnInitPose?.RegisterCallback<ClickEvent>(_ => OnInitPoseClicked());
            _btnResetLayers?.RegisterCallback<ClickEvent>(_ => OnResetLayersClicked());
            _btnBakePose?.RegisterCallback<ClickEvent>(_ => OnBakePoseClicked());
        }

        private void RegisterRestPoseField(FloatField field, Action<BonePoseData, float> setter)
        {
            field?.RegisterValueChangedCallback(evt =>
            {
                if (_isSyncingPoseUI) return;
                var targets = GetSelectedBonePoseDatas();
                if (targets.Count == 0) return;

                var beforeSnapshots = CaptureSnapshots(targets);
                foreach (var (_, _, pose) in targets)
                {
                    setter(pose, evt.newValue);
                    pose.SetDirty();
                }
                var afterSnapshots = CaptureSnapshots(targets);
                RecordMultiBonePoseUndo(targets, beforeSnapshots, afterSnapshots, "ボーンポーズ変更");
                UpdateBonePosePanel();
                NotifyModelChanged();
            });
        }

        private void RegisterRestRotField(FloatField field, int axis)
        {
            field?.RegisterValueChangedCallback(evt =>
            {
                if (_isSyncingPoseUI) return;
                var targets = GetSelectedBonePoseDatas();
                if (targets.Count == 0) return;

                var beforeSnapshots = CaptureSnapshots(targets);
                foreach (var (_, _, pose) in targets)
                {
                    Vector3 euler = IsQuatValid(pose.RestRotation)
                        ? pose.RestRotation.eulerAngles
                        : Vector3.zero;

                    if (axis == 0) euler.x = evt.newValue;
                    else if (axis == 1) euler.y = evt.newValue;
                    else euler.z = evt.newValue;

                    pose.RestRotation = Quaternion.Euler(euler);
                    pose.SetDirty();
                }
                var afterSnapshots = CaptureSnapshots(targets);
                RecordMultiBonePoseUndo(targets, beforeSnapshots, afterSnapshots, "ボーン回転変更");

                // スライダ同期
                _isSyncingPoseUI = true;
                try
                {
                    var slider = axis == 0 ? _restRotSliderX : (axis == 1 ? _restRotSliderY : _restRotSliderZ);
                    float normalized = NormalizeAngle(evt.newValue);
                    slider?.SetValueWithoutNotify(normalized);
                }
                finally
                {
                    _isSyncingPoseUI = false;
                }

                UpdateBonePosePanel();
                NotifyModelChanged();
            });
        }

        private void RegisterRestRotSlider(Slider slider, int axis)
        {
            slider?.RegisterValueChangedCallback(evt =>
            {
                if (_isSyncingPoseUI) return;
                var targets = GetSelectedBonePoseDatas();
                if (targets.Count == 0) return;

                // ドラッグ開始時にスナップショットを取得（1ドラッグ1記録）
                if (_sliderDragBeforeSnapshots.Count == 0)
                {
                    foreach (var (idx, _, pose) in targets)
                        _sliderDragBeforeSnapshots[idx] = pose.CreateSnapshot();
                }

                foreach (var (_, _, pose) in targets)
                {
                    Vector3 euler = IsQuatValid(pose.RestRotation)
                        ? pose.RestRotation.eulerAngles
                        : Vector3.zero;

                    if (axis == 0) euler.x = evt.newValue;
                    else if (axis == 1) euler.y = evt.newValue;
                    else euler.z = evt.newValue;

                    pose.RestRotation = Quaternion.Euler(euler);
                    pose.SetDirty();
                }

                // FloatField同期
                _isSyncingPoseUI = true;
                try
                {
                    var floatField = axis == 0 ? _restRotX : (axis == 1 ? _restRotY : _restRotZ);
                    floatField?.SetValueWithoutNotify((float)System.Math.Round(evt.newValue, 4));
                }
                finally
                {
                    _isSyncingPoseUI = false;
                }

                UpdateBonePosePanel();
                NotifyModelChanged();
            });

            // ドラッグ完了時にUndo記録をコミット
            slider?.RegisterCallback<PointerCaptureOutEvent>(_ => CommitSliderDragUndo("ボーン回転変更"));
        }

        /// <summary>
        /// 角度を -180～180 の範囲に正規化
        /// </summary>
        private static float NormalizeAngle(float angle)
        {
            angle = angle % 360f;
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            return angle;
        }

        private void OnPoseActiveChanged(ChangeEvent<bool> evt)
        {
            if (_isSyncingPoseUI) return;
            var boneContexts = GetSelectedBoneContexts();
            if (boneContexts.Count == 0) return;

            var beforeSnapshots = new Dictionary<int, BonePoseDataSnapshot?>();
            foreach (var (idx, ctx) in boneContexts)
                beforeSnapshots[idx] = ctx.BonePoseData?.CreateSnapshot();

            foreach (var (_, ctx) in boneContexts)
            {
                if (evt.newValue)
                {
                    if (ctx.BonePoseData == null)
                        ctx.BonePoseData = new BonePoseData();
                    ctx.BonePoseData.IsActive = true;
                    ctx.BonePoseData.SetDirty();
                }
                else
                {
                    if (ctx.BonePoseData != null)
                    {
                        ctx.BonePoseData.IsActive = false;
                        ctx.BonePoseData.SetDirty();
                    }
                }
            }

            var afterSnapshots = new Dictionary<int, BonePoseDataSnapshot?>();
            foreach (var (idx, ctx) in boneContexts)
                afterSnapshots[idx] = ctx.BonePoseData?.CreateSnapshot();

            RecordMultiBonePoseUndoRaw(beforeSnapshots, afterSnapshots,
                evt.newValue ? "ボーンポーズ有効化" : "ボーンポーズ無効化");
            UpdateBonePosePanel();
            NotifyModelChanged();
        }

        private void OnInitPoseClicked()
        {
            var boneContexts = GetSelectedBoneContexts();
            if (boneContexts.Count == 0) return;

            var beforeSnapshots = new Dictionary<int, BonePoseDataSnapshot?>();
            foreach (var (idx, ctx) in boneContexts)
                beforeSnapshots[idx] = ctx.BonePoseData?.CreateSnapshot();

            foreach (var (_, ctx) in boneContexts)
            {
                if (ctx.BonePoseData == null)
                {
                    ctx.BonePoseData = new BonePoseData();
                    ctx.BonePoseData.IsActive = true;
                }
            }

            var afterSnapshots = new Dictionary<int, BonePoseDataSnapshot?>();
            foreach (var (idx, ctx) in boneContexts)
                afterSnapshots[idx] = ctx.BonePoseData?.CreateSnapshot();

            RecordMultiBonePoseUndoRaw(beforeSnapshots, afterSnapshots, "ボーンポーズ初期化");
            UpdateBonePosePanel();
            NotifyModelChanged();
            Log("BonePoseData初期化完了");
        }

        private void OnResetLayersClicked()
        {
            var targets = GetSelectedBonePoseDatas();
            if (targets.Count == 0) return;

            var beforeSnapshots = CaptureSnapshots(targets);
            foreach (var (_, _, pose) in targets)
                pose.ClearAllLayers();
            var afterSnapshots = CaptureSnapshots(targets);
            RecordMultiBonePoseUndo(targets, beforeSnapshots, afterSnapshots, "全レイヤークリア");
            UpdateBonePosePanel();
            NotifyModelChanged();
            Log("全レイヤーをクリア");
        }

        private void OnBakePoseClicked()
        {
            var boneContexts = GetSelectedBoneContexts();
            if (boneContexts.Count == 0) return;

            // BonePoseDataを持つもののみ対象
            var targets = new List<(int idx, MeshContext ctx)>();
            foreach (var (idx, ctx) in boneContexts)
            {
                if (ctx.BonePoseData != null)
                    targets.Add((idx, ctx));
            }
            if (targets.Count == 0) return;

            var record = new MultiBonePoseChangeRecord();
            foreach (var (idx, ctx) in targets)
            {
                var beforePose = ctx.BonePoseData.CreateSnapshot();
                Matrix4x4 oldBindPose = ctx.BindPose;

                ctx.BonePoseData.BakeToBindPose(ctx.WorldMatrix);
                ctx.BindPose = ctx.WorldMatrix.inverse;

                var afterPose = ctx.BonePoseData.CreateSnapshot();
                Matrix4x4 newBindPose = ctx.BindPose;

                record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                {
                    MasterIndex = idx,
                    OldSnapshot = beforePose,
                    NewSnapshot = afterPose,
                    OldBindPose = oldBindPose,
                    NewBindPose = newBindPose
                });
            }

            var undoController = _toolContext?.UndoController;
            if (undoController != null)
            {
                undoController.MeshListStack.Record(record, "BindPoseにベイク");
                undoController.FocusMeshList();
            }

            UpdateBonePosePanel();
            NotifyModelChanged();
            Log($"BindPoseにベイク完了 ({targets.Count}件)");
        }

        /// <summary>
        /// BonePoseパネルの表示を更新（複数選択対応・Unity-style混合値）
        /// </summary>
        private void UpdateBonePosePanel()
        {
            if (_bonePoseSection == null) return;
            if (_currentTab != TabType.Bone) return;

            _isSyncingPoseUI = true;
            try
            {
                var boneContexts = GetSelectedBoneContexts();

                if (boneContexts.Count == 0)
                {
                    // 選択なし
                    SetPoseFieldsEmpty();
                    return;
                }

                // 全選択ボーンのBonePoseDataを収集
                var poses = new List<BonePoseData>();
                foreach (var (_, ctx) in boneContexts)
                {
                    if (ctx.BonePoseData != null)
                        poses.Add(ctx.BonePoseData);
                }

                bool allHavePose = poses.Count == boneContexts.Count;
                bool noneHavePose = poses.Count == 0;

                // Active トグル
                if (allHavePose)
                {
                    bool firstActive = poses[0].IsActive;
                    bool allSame = poses.TrueForAll(p => p.IsActive == firstActive);
                    _poseActiveToggle?.SetValueWithoutNotify(allSame ? firstActive : false);
                    SetMixedValue(_poseActiveToggle, !allSame);
                }
                else if (noneHavePose)
                {
                    _poseActiveToggle?.SetValueWithoutNotify(false);
                    SetMixedValue(_poseActiveToggle, false);
                }
                else
                {
                    _poseActiveToggle?.SetValueWithoutNotify(false);
                    SetMixedValue(_poseActiveToggle, true);
                }
                _poseActiveToggle?.SetEnabled(boneContexts.Count > 0);

                // RestPose フィールド
                if (allHavePose && poses.Count > 0)
                {
                    SetMixedFloatField(_restPosX, poses, p => p.RestPosition.x, true);
                    SetMixedFloatField(_restPosY, poses, p => p.RestPosition.y, true);
                    SetMixedFloatField(_restPosZ, poses, p => p.RestPosition.z, true);

                    SetMixedRotField(_restRotX, _restRotSliderX, poses, 0, true);
                    SetMixedRotField(_restRotY, _restRotSliderY, poses, 1, true);
                    SetMixedRotField(_restRotZ, _restRotSliderZ, poses, 2, true);

                    SetMixedFloatField(_restSclX, poses, p => p.RestScale.x, true);
                    SetMixedFloatField(_restSclY, poses, p => p.RestScale.y, true);
                    SetMixedFloatField(_restSclZ, poses, p => p.RestScale.z, true);
                }
                else
                {
                    SetFloatField(_restPosX, 0, false); SetFloatField(_restPosY, 0, false); SetFloatField(_restPosZ, 0, false);
                    SetFloatField(_restRotX, 0, false); SetFloatField(_restRotY, 0, false); SetFloatField(_restRotZ, 0, false);
                    SetSlider(_restRotSliderX, 0, false); SetSlider(_restRotSliderY, 0, false); SetSlider(_restRotSliderZ, 0, false);
                    SetFloatField(_restSclX, 1, false); SetFloatField(_restSclY, 1, false); SetFloatField(_restSclZ, 1, false);
                }

                // レイヤー一覧（単一選択のみ）
                BonePoseData singlePose = (boneContexts.Count == 1 && allHavePose) ? poses[0] : null;
                UpdateLayersList(singlePose);

                // 合成結果（単一選択のみ）
                if (singlePose != null)
                {
                    Vector3 pos = singlePose.Position;
                    Vector3 rot = IsQuatValid(singlePose.Rotation)
                        ? singlePose.Rotation.eulerAngles
                        : Vector3.zero;
                    if (_poseResultPos != null)
                        _poseResultPos.text = $"Pos: ({pos.x:F3}, {pos.y:F3}, {pos.z:F3})";
                    if (_poseResultRot != null)
                        _poseResultRot.text = $"Rot: ({rot.x:F1}, {rot.y:F1}, {rot.z:F1})";
                }
                else
                {
                    if (_poseResultPos != null) _poseResultPos.text = boneContexts.Count > 1 ? "Pos: (複数選択)" : "Pos: -";
                    if (_poseResultRot != null) _poseResultRot.text = boneContexts.Count > 1 ? "Rot: (複数選択)" : "Rot: -";
                }

                // Initボタン
                _btnInitPose?.SetEnabled(false);
                if (_btnInitPose != null)
                    _btnInitPose.style.display = DisplayStyle.None;
                _btnResetLayers?.SetEnabled(allHavePose && poses.Any(p => p.LayerCount > 0));

                // BindPose（単一選択のみ値表示）
                if (boneContexts.Count == 1)
                {
                    var ctx = boneContexts[0].ctx;
                    Matrix4x4 bp = ctx.BindPose;
                    Vector3 bpPos = (Vector3)bp.GetColumn(3);
                    Vector3 bpRot = IsQuatValid(bp.rotation)
                        ? bp.rotation.eulerAngles
                        : Vector3.zero;
                    Vector3 bpScl = bp.lossyScale;

                    if (_bindposePos != null)
                        _bindposePos.text = $"Pos: ({bpPos.x:F3}, {bpPos.y:F3}, {bpPos.z:F3})";
                    if (_bindposeRot != null)
                        _bindposeRot.text = $"Rot: ({bpRot.x:F1}, {bpRot.y:F1}, {bpRot.z:F1})";
                    if (_bindposeScl != null)
                        _bindposeScl.text = $"Scl: ({bpScl.x:F3}, {bpScl.y:F3}, {bpScl.z:F3})";
                }
                else
                {
                    if (_bindposePos != null) _bindposePos.text = boneContexts.Count > 1 ? "Pos: (複数選択)" : "Pos: -";
                    if (_bindposeRot != null) _bindposeRot.text = boneContexts.Count > 1 ? "Rot: (複数選択)" : "Rot: -";
                    if (_bindposeScl != null) _bindposeScl.text = boneContexts.Count > 1 ? "Scl: (複数選択)" : "Scl: -";
                }

                _btnBakePose?.SetEnabled(allHavePose);
            }
            finally
            {
                _isSyncingPoseUI = false;
            }
        }

        private void SetPoseFieldsEmpty()
        {
            _poseActiveToggle?.SetValueWithoutNotify(false);
            _poseActiveToggle?.SetEnabled(false);
            SetMixedValue(_poseActiveToggle, false);

            SetFloatField(_restPosX, 0, false); SetFloatField(_restPosY, 0, false); SetFloatField(_restPosZ, 0, false);
            SetFloatField(_restRotX, 0, false); SetFloatField(_restRotY, 0, false); SetFloatField(_restRotZ, 0, false);
            SetSlider(_restRotSliderX, 0, false); SetSlider(_restRotSliderY, 0, false); SetSlider(_restRotSliderZ, 0, false);
            SetFloatField(_restSclX, 1, false); SetFloatField(_restSclY, 1, false); SetFloatField(_restSclZ, 1, false);

            UpdateLayersList(null);

            if (_poseResultPos != null) _poseResultPos.text = "Pos: -";
            if (_poseResultRot != null) _poseResultRot.text = "Rot: -";
            _btnInitPose?.SetEnabled(false);
            if (_btnInitPose != null) _btnInitPose.style.display = DisplayStyle.None;
            _btnResetLayers?.SetEnabled(false);

            if (_bindposePos != null) _bindposePos.text = "Pos: -";
            if (_bindposeRot != null) _bindposeRot.text = "Rot: -";
            if (_bindposeScl != null) _bindposeScl.text = "Scl: -";
            _btnBakePose?.SetEnabled(false);
        }

        /// <summary>Unity-style: 値が全一致→表示、不一致→showMixedValue</summary>
        private void SetMixedFloatField(FloatField field, List<BonePoseData> poses,
            Func<BonePoseData, float> getter, bool enabled)
        {
            if (field == null) return;
            float first = getter(poses[0]);
            bool allSame = poses.TrueForAll(p => Mathf.Abs(getter(p) - first) < 0.0001f);
            field.SetValueWithoutNotify(allSame ? (float)System.Math.Round(first, 4) : 0f);
            field.showMixedValue = !allSame;
            field.SetEnabled(enabled);
        }

        /// <summary>Unity-style: 回転フィールドとスライダーの混合値処理</summary>
        private void SetMixedRotField(FloatField field, Slider slider, List<BonePoseData> poses,
            int axis, bool enabled)
        {
            if (field == null) return;
            float first = GetEulerAxis(poses[0], axis);
            bool allSame = poses.TrueForAll(p => Mathf.Abs(GetEulerAxis(p, axis) - first) < 0.01f);
            float val = allSame ? first : 0f;
            field.SetValueWithoutNotify((float)System.Math.Round(val, 4));
            field.showMixedValue = !allSame;
            field.SetEnabled(enabled);
            if (slider != null)
            {
                slider.SetValueWithoutNotify(allSame ? NormalizeAngle(val) : 0f);
                slider.SetEnabled(enabled && allSame);
            }
        }

        private static float GetEulerAxis(BonePoseData pose, int axis)
        {
            Vector3 euler = IsQuatValid(pose.RestRotation)
                ? pose.RestRotation.eulerAngles
                : Vector3.zero;
            return axis == 0 ? euler.x : (axis == 1 ? euler.y : euler.z);
        }

        private static void SetMixedValue(Toggle toggle, bool mixed)
        {
            if (toggle == null) return;
            toggle.showMixedValue = mixed;
        }

        private void UpdateLayersList(BonePoseData pose)
        {
            if (_poseLayersContainer == null) return;

            // 動的に追加したレイヤー行を削除（Labelは残す）
            var toRemove = new List<VisualElement>();
            foreach (var child in _poseLayersContainer.Children())
            {
                if (child.ClassListContains("pose-layer-row"))
                    toRemove.Add(child);
            }
            foreach (var el in toRemove)
                _poseLayersContainer.Remove(el);

            bool hasLayers = pose != null && pose.LayerCount > 0;
            if (_poseNoLayersLabel != null)
                _poseNoLayersLabel.style.display = hasLayers ? DisplayStyle.None : DisplayStyle.Flex;

            if (!hasLayers) return;

            foreach (var layer in pose.Layers)
            {
                var row = new VisualElement();
                row.AddToClassList("pose-layer-row");
                if (!layer.Enabled) row.AddToClassList("pose-layer-disabled");

                var nameLabel = new Label(layer.Name);
                nameLabel.AddToClassList("pose-layer-name");

                var euler = IsQuatValid(layer.DeltaRotation)
                    ? layer.DeltaRotation.eulerAngles
                    : Vector3.zero;
                string deltaInfo = $"dP({layer.DeltaPosition.x:F2},{layer.DeltaPosition.y:F2},{layer.DeltaPosition.z:F2}) " +
                                   $"dR({euler.x:F1},{euler.y:F1},{euler.z:F1})";
                var infoLabel = new Label(deltaInfo);
                infoLabel.AddToClassList("pose-layer-info");

                var weightLabel = new Label($"w={layer.Weight:F2}");
                weightLabel.AddToClassList("pose-layer-weight");

                row.Add(nameLabel);
                row.Add(infoLabel);
                row.Add(weightLabel);
                _poseLayersContainer.Add(row);
            }
        }

        // ================================================================
        // BonePose ヘルパー
        // ================================================================

        private MeshContext GetSelectedMeshContext()
        {
            if (_selectedAdapters.Count != 1) return null;
            return _selectedAdapters[0].Entry.Context;
        }

        private BonePoseData GetSelectedBonePoseData()
        {
            return GetSelectedMeshContext()?.BonePoseData;
        }

        private int GetSelectedMasterIndex()
        {
            if (_selectedAdapters.Count != 1) return -1;
            return _selectedAdapters[0].MasterIndex;
        }

        /// <summary>選択中の全ボーンの(masterIndex, MeshContext)リスト</summary>
        private List<(int idx, MeshContext ctx)> GetSelectedBoneContexts()
        {
            var result = new List<(int, MeshContext)>();
            foreach (var adapter in _selectedAdapters)
            {
                var ctx = adapter.Entry.Context;
                if (ctx != null)
                    result.Add((adapter.MasterIndex, ctx));
            }
            return result;
        }

        /// <summary>選択中の全ボーンの(masterIndex, MeshContext, BonePoseData)リスト（BonePoseData有りのみ）</summary>
        private List<(int idx, MeshContext ctx, BonePoseData pose)> GetSelectedBonePoseDatas()
        {
            var result = new List<(int, MeshContext, BonePoseData)>();
            foreach (var adapter in _selectedAdapters)
            {
                var ctx = adapter.Entry.Context;
                if (ctx?.BonePoseData != null)
                    result.Add((adapter.MasterIndex, ctx, ctx.BonePoseData));
            }
            return result;
        }

        /// <summary>スナップショット一括取得</summary>
        private Dictionary<int, BonePoseDataSnapshot> CaptureSnapshots(
            List<(int idx, MeshContext ctx, BonePoseData pose)> targets)
        {
            var dict = new Dictionary<int, BonePoseDataSnapshot>();
            foreach (var (idx, _, pose) in targets)
                dict[idx] = pose.CreateSnapshot();
            return dict;
        }

        /// <summary>
        /// 複数ボーンのBonePose変更をUndoスタックに記録
        /// </summary>
        private void RecordMultiBonePoseUndo(
            List<(int idx, MeshContext ctx, BonePoseData pose)> targets,
            Dictionary<int, BonePoseDataSnapshot> before,
            Dictionary<int, BonePoseDataSnapshot> after,
            string description)
        {
            var undoController = _toolContext?.UndoController;
            if (undoController == null) return;

            var record = new MultiBonePoseChangeRecord();
            foreach (var (idx, _, _) in targets)
            {
                record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                {
                    MasterIndex = idx,
                    OldSnapshot = before.TryGetValue(idx, out var b) ? b : (BonePoseDataSnapshot?)null,
                    NewSnapshot = after.TryGetValue(idx, out var a) ? a : (BonePoseDataSnapshot?)null,
                });
            }
            undoController.MeshListStack.Record(record, description);
            undoController.FocusMeshList();
        }

        /// <summary>
        /// 複数ボーンのBonePose変更をUndoスタックに記録（nullable snapshot辞書版）
        /// </summary>
        private void RecordMultiBonePoseUndoRaw(
            Dictionary<int, BonePoseDataSnapshot?> before,
            Dictionary<int, BonePoseDataSnapshot?> after,
            string description)
        {
            var undoController = _toolContext?.UndoController;
            if (undoController == null) return;

            var record = new MultiBonePoseChangeRecord();
            foreach (var kvp in before)
            {
                after.TryGetValue(kvp.Key, out var afterVal);
                record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                {
                    MasterIndex = kvp.Key,
                    OldSnapshot = kvp.Value,
                    NewSnapshot = afterVal,
                });
            }
            undoController.MeshListStack.Record(record, description);
            undoController.FocusMeshList();
        }

        /// <summary>
        /// スライダードラッグ完了時にUndo記録をコミット（複数ボーン対応）
        /// </summary>
        private void CommitSliderDragUndo(string description)
        {
            if (_sliderDragBeforeSnapshots.Count == 0) return;

            var targets = GetSelectedBonePoseDatas();
            var afterSnapshots = CaptureSnapshots(targets);

            var undoController = _toolContext?.UndoController;
            if (undoController != null)
            {
                var record = new MultiBonePoseChangeRecord();
                foreach (var kvp in _sliderDragBeforeSnapshots)
                {
                    afterSnapshots.TryGetValue(kvp.Key, out var afterVal);
                    record.Entries.Add(new MultiBonePoseChangeRecord.Entry
                    {
                        MasterIndex = kvp.Key,
                        OldSnapshot = kvp.Value,
                        NewSnapshot = afterVal,
                    });
                }
                undoController.MeshListStack.Record(record, description);
                undoController.FocusMeshList();
            }

            _sliderDragBeforeSnapshots.Clear();
        }

        private static void SetFloatField(FloatField field, float value, bool enabled)
        {
            if (field == null) return;
            field.SetValueWithoutNotify((float)System.Math.Round(value, 4));
            field.showMixedValue = false;
            field.SetEnabled(enabled);
        }

        private static void SetSlider(Slider slider, float value, bool enabled)
        {
            if (slider == null) return;
            slider.SetValueWithoutNotify(value);
            slider.SetEnabled(enabled);
        }

        private static Vector3 SetX(Vector3 v, float x) => new Vector3(x, v.y, v.z);
        private static Vector3 SetY(Vector3 v, float y) => new Vector3(v.x, y, v.z);
        private static Vector3 SetZ(Vector3 v, float z) => new Vector3(v.x, v.y, z);

        private static bool IsQuatValid(Quaternion q)
        {
            return !float.IsNaN(q.x) && !float.IsNaN(q.y) && !float.IsNaN(q.z) && !float.IsNaN(q.w)
                && (q.x != 0 || q.y != 0 || q.z != 0 || q.w != 0);
        }

        private static Vector3 SafeEuler(Quaternion q)
        {
            return IsQuatValid(q) ? q.eulerAngles : Vector3.zero;
        }

        // ================================================================
        // モーフエディタ（Phase MorphEditor v2: UIToolkit ListView）
        // ================================================================

        /// <summary>
        /// モーフエディタのUI要素をバインド
        /// </summary>
        private void BindMorphEditorUI(VisualElement root)
        {
            _morphCountLabel = root.Q<Label>("morph-count-label");
            _morphStatusLabel = root.Q<Label>("morph-status-label");
            _morphNameField = root.Q<TextField>("morph-name-field");
            _morphTestWeight = root.Q<Slider>("morph-test-weight");

            // モーフ ListView
            _morphListView = root.Q<ListView>("morph-listview");
            if (_morphListView != null)
            {
                _morphListView.makeItem = MorphListMakeItem;
                _morphListView.bindItem = MorphListBindItem;
                _morphListView.fixedItemHeight = 20;
                _morphListView.itemsSource = _morphListData;
                _morphListView.selectionType = SelectionType.Multiple;
                _morphListView.selectionChanged += OnMorphListSelectionChanged;
            }

            // PopupFieldコンテナ
            _morphSourceMeshPopupContainer = root.Q<VisualElement>("morph-source-mesh-container");
            _morphParentPopupContainer = root.Q<VisualElement>("morph-parent-container");
            _morphPanelPopupContainer = root.Q<VisualElement>("morph-panel-container");

            // モーフセット
            _morphSetNameField = root.Q<TextField>("morph-set-name-field");
            _morphSetTypePopupContainer = root.Q<VisualElement>("morph-set-type-container");
            _btnCreateMorphSet = root.Q<Button>("btn-create-morph-set");

            // ボタンイベント
            _btnMeshToMorph = root.Q<Button>("btn-mesh-to-morph");
            _btnMorphToMesh = root.Q<Button>("btn-morph-to-mesh");

            _btnMeshToMorph?.RegisterCallback<ClickEvent>(_ => OnMeshToMorph());
            _btnMorphToMesh?.RegisterCallback<ClickEvent>(_ => OnMorphToMesh());
            _btnCreateMorphSet?.RegisterCallback<ClickEvent>(_ => OnCreateMorphSet());

            root.Q<Button>("btn-morph-test-reset")?.RegisterCallback<ClickEvent>(_ => OnMorphTestReset());
            root.Q<Button>("btn-morph-test-select-all")?.RegisterCallback<ClickEvent>(_ => OnMorphTestSelectAll(true));
            root.Q<Button>("btn-morph-test-deselect-all")?.RegisterCallback<ClickEvent>(_ => OnMorphTestSelectAll(false));

            // ウェイトスライダー
            _morphTestWeight?.RegisterValueChangedCallback(OnMorphTestWeightChanged);

            // 初期データ投入（display:none状態でもitemsSourceにデータを入れておく）
            RefreshMorphListData();
        }

        // ----------------------------------------------------------------
        // ListView makeItem / bindItem
        // ----------------------------------------------------------------

        private VisualElement MorphListMakeItem()
        {
            var row = new VisualElement();
            row.AddToClassList("morph-list-row");

            var nameLabel = new Label();
            nameLabel.AddToClassList("morph-list-name");
            row.Add(nameLabel);

            var infoLabel = new Label();
            infoLabel.AddToClassList("morph-list-info");
            row.Add(infoLabel);

            return row;
        }

        private void MorphListBindItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _morphListData.Count) return;
            var data = _morphListData[index];

            var nameLabel = element.Q<Label>(className: "morph-list-name");
            var infoLabel = element.Q<Label>(className: "morph-list-info");

            if (nameLabel != null) nameLabel.text = data.name;
            if (infoLabel != null) infoLabel.text = data.info;
        }

        // ----------------------------------------------------------------
        // モーフエディタ更新
        // ----------------------------------------------------------------

        private void RefreshMorphEditor()
        {
            if (Model == null) return;

            RefreshMorphListData();
            RefreshMorphConvertSection();
            RefreshMorphSetSection();
        }

        /// <summary>
        /// モーフリストのデータソースを更新してListViewをリフレッシュ
        /// </summary>
        private void RefreshMorphListData()
        {
            _morphListData.Clear();

            if (Model != null)
            {
                var morphEntries = Model.TypedIndices.GetEntries(MeshCategory.Morph);
                foreach (var entry in morphEntries)
                {
                    var ctx = entry.Context;
                    string info = "";
                    if (ctx != null && ctx.MorphParentIndex >= 0)
                    {
                        var parentCtx = Model.GetMeshContext(ctx.MorphParentIndex);
                        info = parentCtx != null ? $"→{parentCtx.Name}" : $"→[{ctx.MorphParentIndex}]";
                    }
                    else if (ctx != null && !string.IsNullOrEmpty(ctx.MorphName))
                    {
                        info = ctx.MorphName;
                    }
                    _morphListData.Add((entry.MasterIndex, entry.Name, info));
                }
            }

            if (_morphCountLabel != null)
                _morphCountLabel.text = $"モーフ: {_morphListData.Count}";

            _morphListView?.RefreshItems();
            SyncMorphListViewSelection();
        }

        // ----------------------------------------------------------------
        // モーフリスト選択 (ListView selectionChanged)
        // ----------------------------------------------------------------

        private void OnMorphListSelectionChanged(IEnumerable<object> selection)
        {
            if (_isSyncingMorphSelection || Model == null) return;

            var oldIndices = Model.SelectedMorphIndices.ToArray();

            // ListView選択 → Model.SelectedMorphIndices
            Model.ClearMorphSelection();
            foreach (var item in selection)
            {
                if (item is (int masterIndex, string, string))
                    Model.AddToMorphSelection(masterIndex);
            }

            var newIndices = Model.SelectedMorphIndices.ToArray();

            // Undo記録（変化があった場合のみ）
            if (!oldIndices.SequenceEqual(newIndices))
            {
                var undoController = _toolContext?.UndoController;
                if (undoController != null)
                {
                    var record = new MorphSelectionChangeRecord(oldIndices, newIndices);
                    undoController.MeshListStack.Record(record, "モーフ選択変更");
                    undoController.FocusMeshList();
                }
            }

            _toolContext?.OnMeshSelectionChanged?.Invoke();
            _toolContext?.Repaint?.Invoke();
        }

        /// <summary>
        /// Model.SelectedMorphIndices → ListView選択に同期（Undo/Redo・外部変更時用）
        /// </summary>
        private void SyncMorphListViewSelection()
        {
            if (_morphListView == null || Model == null) return;

            _isSyncingMorphSelection = true;
            try
            {
                var selectedListIndices = new List<int>();
                var selectedMorphSet = new HashSet<int>(Model.SelectedMorphIndices);

                for (int i = 0; i < _morphListData.Count; i++)
                {
                    if (selectedMorphSet.Contains(_morphListData[i].masterIndex))
                        selectedListIndices.Add(i);
                }

                _morphListView.SetSelectionWithoutNotify(selectedListIndices);
            }
            finally
            {
                _isSyncingMorphSelection = false;
            }
        }

        // ----------------------------------------------------------------
        // 変換セクション
        // ----------------------------------------------------------------

        private void RefreshMorphConvertSection()
        {
            if (Model == null) return;

            RebuildPopup(ref _morphSourceMeshPopup, _morphSourceMeshPopupContainer, BuildDrawableMeshChoices(), "morph-popup");
            RebuildPopup(ref _morphParentPopup, _morphParentPopupContainer, BuildDrawableMeshChoices(), "morph-popup");
            RebuildPopup(ref _morphPanelPopup, _morphPanelPopupContainer,
                new List<(int, string)> { (0, "眉"), (1, "目"), (2, "口"), (3, "その他") }, "morph-popup", 3);
        }

        private List<(int index, string name)> BuildDrawableMeshChoices()
        {
            var choices = new List<(int, string)>();
            if (Model == null) return choices;
            foreach (var entry in Model.TypedIndices.GetEntries(MeshCategory.Drawable))
                choices.Add((entry.MasterIndex, $"[{entry.MasterIndex}] {entry.Name}"));
            return choices;
        }

        private void RebuildPopup(ref PopupField<int> popup, VisualElement container,
            List<(int index, string name)> options, string cssClass, int defaultValue = -1)
        {
            if (container == null) return;
            container.Clear();

            var indices = new List<int> { -1 };
            var displayMap = new Dictionary<int, string> { [-1] = "(なし)" };
            foreach (var (idx, name) in options)
            {
                indices.Add(idx);
                displayMap[idx] = name;
            }

            int initial = indices.Contains(defaultValue) ? defaultValue : -1;
            popup = new PopupField<int>(indices, initial,
                v => displayMap.TryGetValue(v, out var s) ? s : v.ToString(),
                v => displayMap.TryGetValue(v, out var s) ? s : v.ToString());
            popup.AddToClassList(cssClass);
            popup.style.flexGrow = 1;
            container.Add(popup);
        }

        // ----------------------------------------------------------------
        // メッシュ → モーフ 変換
        // ----------------------------------------------------------------

        private void OnMeshToMorph()
        {
            if (Model == null) return;

            int sourceIdx = _morphSourceMeshPopup?.value ?? -1;
            int parentIdx = _morphParentPopup?.value ?? -1;
            string morphName = _morphNameField?.value?.Trim() ?? "";
            int panel = _morphPanelPopup?.value ?? 3;

            if (sourceIdx < 0 || sourceIdx >= Model.MeshContextCount)
            { MorphLog("対象メッシュを選択してください"); return; }

            var ctx = Model.GetMeshContext(sourceIdx);
            if (ctx == null || ctx.MeshObject == null)
            { MorphLog("メッシュが無効です"); return; }

            if (ctx.IsMorph)
            { MorphLog("既にモーフです"); return; }

            if (string.IsNullOrEmpty(morphName)) morphName = ctx.Name;

            var record = new MorphConversionRecord
            {
                MasterIndex = sourceIdx,
                OldType = ctx.Type, NewType = MeshType.Morph,
                OldMorphBaseData = ctx.MorphBaseData?.Clone(),
                OldMorphParentIndex = ctx.MorphParentIndex,
                OldName = ctx.Name, OldExcludeFromExport = ctx.ExcludeFromExport,
            };

            // 親メッシュのMeshObjectをBasePositionsの基準として渡す
            MeshObject baseMeshObject = null;
            if (parentIdx >= 0 && parentIdx < Model.MeshContextCount)
            {
                var parentCtx = Model.GetMeshContext(parentIdx);
                baseMeshObject = parentCtx?.MeshObject;
            }
            ctx.SetAsMorph(morphName, baseMeshObject);
            ctx.MorphPanel = panel;
            ctx.MorphParentIndex = parentIdx;
            ctx.Type = MeshType.Morph;

            ctx.IsVisible = false; // 非表示にしておく

            if (ctx.MeshObject != null) ctx.MeshObject.Type = MeshType.Morph;
            ctx.ExcludeFromExport = true;

            record.NewMorphBaseData = ctx.MorphBaseData?.Clone();
            record.NewMorphParentIndex = ctx.MorphParentIndex;
            record.NewName = ctx.Name;
            record.NewExcludeFromExport = ctx.ExcludeFromExport;

            RecordMorphUndo(record, "メッシュ→モーフ変換");

            // 変換元を選択リストから除去し、有効なDrawableを選択
            Model.RemoveFromSelectionByType(sourceIdx);
            Model.TypedIndices?.Invalidate();
            var drawables = Model.TypedIndices.GetEntries(MeshCategory.Drawable);
            if (drawables.Count > 0 && !Model.HasMeshSelection)
                Model.SelectDrawable(drawables[0].MasterIndex);

            NotifyModelChanged();
            RefreshMorphEditor();
            MorphLog($"'{ctx.Name}' をモーフに変換");
        }

        // ----------------------------------------------------------------
        // モーフ → メッシュ 変換
        // ----------------------------------------------------------------

        private void OnMorphToMesh()
        {
            if (Model == null) return;

            // 選択中のモーフを収集
            var targets = new List<int>();
            foreach (var morphIdx in Model.SelectedMorphIndices.ToList())
            {
                if (morphIdx < 0 || morphIdx >= Model.MeshContextCount) continue;
                var ctx = Model.GetMeshContext(morphIdx);
                if (ctx != null && (ctx.IsMorph || ctx.Type == MeshType.Morph))
                    targets.Add(morphIdx);
            }

            if (targets.Count == 0)
            { MorphLog("モーフが選択されていません"); return; }

            // モーフプレビュー終了
            EndMorphPreview();
            _morphTestWeight?.SetValueWithoutNotify(0f);

            var convertedNames = new List<string>();
            foreach (int targetIdx in targets)
            {
                var ctx = Model.GetMeshContext(targetIdx);
                if (ctx == null) continue;

                var record = new MorphConversionRecord
                {
                    MasterIndex = targetIdx,
                    OldType = ctx.Type, NewType = MeshType.Mesh,
                    OldMorphBaseData = ctx.MorphBaseData?.Clone(),
                    OldMorphParentIndex = ctx.MorphParentIndex,
                    OldName = ctx.Name, OldExcludeFromExport = ctx.ExcludeFromExport,
                    NewMorphBaseData = null, NewMorphParentIndex = -1,
                    NewName = ctx.Name, NewExcludeFromExport = false,
                };

                ctx.ClearMorphData();
                ctx.Type = MeshType.Mesh;
                if (ctx.MeshObject != null) ctx.MeshObject.Type = MeshType.Mesh;
                ctx.ExcludeFromExport = false;

                RecordMorphUndo(record, $"モーフ→メッシュ: {ctx.Name}");
                convertedNames.Add(ctx.Name);
            }

            // 変換したメッシュを選択リストから除去
            foreach (int idx in targets)
                Model.RemoveFromSelectionByType(idx);

            Model.TypedIndices?.Invalidate();

            // Drawableが残っていれば先頭を選択
            var drawables = Model.TypedIndices.GetEntries(MeshCategory.Drawable);
            if (drawables.Count > 0 && !Model.HasMeshSelection)
                Model.SelectDrawable(drawables[0].MasterIndex);

            NotifyModelChanged();
            RefreshMorphEditor();
            MorphLog($"{convertedNames.Count}件をメッシュに戻した: {string.Join(", ", convertedNames)}");
        }

        // ----------------------------------------------------------------
        // 簡易モーフテスト
        // ----------------------------------------------------------------

        private void OnMorphTestWeightChanged(ChangeEvent<float> evt)
        {
            ApplyMorphTest(evt.newValue);
        }

        private void OnMorphTestReset()
        {
            EndMorphPreview();
            _morphTestWeight?.SetValueWithoutNotify(0f);
            _toolContext?.SyncMesh?.Invoke();
            _toolContext?.Repaint?.Invoke();
            MorphLog("モーフテストリセット");
        }

        private void OnMorphTestSelectAll(bool select)
        {
            if (Model == null) return;

            var oldIndices = Model.SelectedMorphIndices.ToArray();

            Model.ClearMorphSelection();
            if (select)
                foreach (var d in _morphListData)
                    Model.AddToMorphSelection(d.masterIndex);

            var newIndices = Model.SelectedMorphIndices.ToArray();

            // Undo記録
            if (!oldIndices.SequenceEqual(newIndices))
            {
                var undoController = _toolContext?.UndoController;
                if (undoController != null)
                {
                    var record = new MorphSelectionChangeRecord(oldIndices, newIndices);
                    undoController.MeshListStack.Record(record, select ? "モーフ全選択" : "モーフ全解除");
                    undoController.FocusMeshList();
                }
            }

            EndMorphPreview();
            _morphTestWeight?.SetValueWithoutNotify(0f);
            SyncMorphListViewSelection();
        }

        private void ApplyMorphTest(float weight)
        {
            if (Model == null || Model.SelectedMorphIndices.Count == 0) return;

            if (!_isMorphPreviewActive) StartMorphPreview();

            // バックアップから復元
            foreach (var (baseIndex, backup) in _morphPreviewBackups)
            {
                var baseCtx = Model.GetMeshContext(baseIndex);
                if (baseCtx?.MeshObject == null) continue;
                var baseMesh = baseCtx.MeshObject;
                int count = Mathf.Min(backup.Length, baseMesh.VertexCount);
                for (int i = 0; i < count; i++)
                    baseMesh.Vertices[i].Position = backup[i];
            }

            // オフセット適用
            foreach (var (morphIndex, baseIndex) in _morphTestChecked)
            {
                var morphCtx = Model.GetMeshContext(morphIndex);
                var baseCtx = Model.GetMeshContext(baseIndex);
                if (morphCtx?.MeshObject == null || baseCtx?.MeshObject == null) continue;

                var baseMesh = baseCtx.MeshObject;
                foreach (var (vertexIndex, offset) in morphCtx.GetMorphOffsets())
                    if (vertexIndex < baseMesh.VertexCount)
                        baseMesh.Vertices[vertexIndex].Position += offset * weight;
            }

            foreach (var baseIndex in _morphPreviewBackups.Keys)
            {
                var baseCtx = Model.GetMeshContext(baseIndex);
                if (baseCtx != null) _toolContext?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
            }
            _toolContext?.Repaint?.Invoke();
        }

        private void StartMorphPreview()
        {
            if (Model == null) return;
            EndMorphPreview();
            _morphTestChecked.Clear();
            _morphPreviewBackups.Clear();

            foreach (var morphIdx in Model.SelectedMorphIndices)
            {
                var morphCtx = Model.GetMeshContext(morphIdx);
                if (morphCtx == null || !morphCtx.IsMorph) continue;

                int baseIdx = morphCtx.MorphParentIndex;
                if (baseIdx < 0) baseIdx = FindBaseMeshByName(morphCtx);
                if (baseIdx < 0) continue;

                var baseCtx = Model.GetMeshContext(baseIdx);
                if (baseCtx?.MeshObject == null) continue;

                if (!_morphPreviewBackups.ContainsKey(baseIdx))
                {
                    var baseMesh = baseCtx.MeshObject;
                    var backup = new Vector3[baseMesh.VertexCount];
                    for (int i = 0; i < baseMesh.VertexCount; i++)
                        backup[i] = baseMesh.Vertices[i].Position;
                    _morphPreviewBackups[baseIdx] = backup;
                }
                _morphTestChecked.Add((morphIdx, baseIdx));
            }
            _isMorphPreviewActive = true;
        }

        private void EndMorphPreview()
        {
            if (!_isMorphPreviewActive || _morphPreviewBackups.Count == 0)
            {
                _isMorphPreviewActive = false;
                _morphPreviewBackups.Clear();
                _morphTestChecked.Clear();
                return;
            }

            if (Model != null)
            {
                foreach (var (baseIndex, backup) in _morphPreviewBackups)
                {
                    var baseCtx = Model.GetMeshContext(baseIndex);
                    if (baseCtx?.MeshObject == null) continue;
                    var baseMesh = baseCtx.MeshObject;
                    int count = Mathf.Min(backup.Length, baseMesh.VertexCount);
                    for (int i = 0; i < count; i++)
                        baseMesh.Vertices[i].Position = backup[i];
                    _toolContext?.SyncMeshContextPositionsOnly?.Invoke(baseCtx);
                }
            }
            _isMorphPreviewActive = false;
            _morphPreviewBackups.Clear();
            _morphTestChecked.Clear();
            _toolContext?.Repaint?.Invoke();
        }

        private int FindBaseMeshByName(MeshContext morphCtx)
        {
            if (morphCtx == null || Model == null) return -1;
            string morphName = morphCtx.MorphName;
            string meshName = morphCtx.Name;
            if (!string.IsNullOrEmpty(morphName) && meshName.EndsWith($"_{morphName}"))
            {
                string baseName = meshName.Substring(0, meshName.Length - morphName.Length - 1);
                for (int i = 0; i < Model.MeshContextCount; i++)
                {
                    var ctx = Model.GetMeshContext(i);
                    if (ctx != null && (ctx.Type == MeshType.Mesh || ctx.Type == MeshType.BakedMirror) && ctx.Name == baseName)
                        return i;
                }
            }
            return -1;
        }

        // ----------------------------------------------------------------
        // モーフセット（新規作成のみ、管理はMorphPanelで）
        // ----------------------------------------------------------------

        private void RefreshMorphSetSection()
        {
            if (Model == null) return;

            RebuildPopup(ref _morphSetTypePopup, _morphSetTypePopupContainer,
                new List<(int, string)> { ((int)MorphType.Vertex, "Vertex"), ((int)MorphType.UV, "UV") },
                "morph-popup", (int)MorphType.Vertex);
        }

        private void OnCreateMorphSet()
        {
            if (Model == null) return;

            string setName = _morphSetNameField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(setName))
                setName = Model.GenerateUniqueMorphExpressionName("MorphExpression");

            if (Model.FindMorphExpressionByName(setName) != null)
            { MorphLog($"セット名 '{setName}' は既に存在します"); return; }

            int typeInt = _morphSetTypePopup?.value ?? (int)MorphType.Vertex;
            var set = new MorphExpression(setName, (MorphType)typeInt);

            foreach (var morphIdx in Model.SelectedMorphIndices)
            {
                var morphCtx = Model.GetMeshContext(morphIdx);
                if (morphCtx != null && morphCtx.IsMorph)
                    set.AddMesh(morphIdx);
            }

            if (set.MeshCount == 0)
            { MorphLog("モーフが選択されていません"); return; }

            int addIndex = Model.MorphExpressions.Count;
            var record = new MorphExpressionChangeRecord
            {
                AddExpression = set.Clone(),
                AddedIndex = addIndex,
            };
            RecordMorphUndo(record, $"モーフセット生成: {setName}");

            Model.MorphExpressions.Add(set);
            NotifyModelChanged();
            MorphLog($"モーフセット '{setName}' を生成 ({set.MeshCount}件)");
        }

        // ----------------------------------------------------------------
        // Undo / ステータス
        // ----------------------------------------------------------------

        private void RecordMorphUndo(MeshListUndoRecord record, string description)
        {
            var undoController = _toolContext?.UndoController;
            if (undoController == null) return;
            undoController.MeshListStack.Record(record, description);
            undoController.FocusMeshList();
        }

        private void MorphLog(string msg)
        {
            if (_morphStatusLabel != null) _morphStatusLabel.text = msg;
            Log(msg);
        }
    }

    /// <summary>
    /// D&Dバリデータ
    /// </summary>
    public class TypedDragValidator : IDragDropValidator<TypedTreeAdapter>
    {
        public bool CanDrag(TypedTreeAdapter item) => true;
        public bool CanDrop(TypedTreeAdapter dragged, TypedTreeAdapter target, DropPosition position) => true;
    }
}