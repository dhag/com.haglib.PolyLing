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
    public class TypedMeshListPanel : EditorWindow
    {
        // ================================================================
        // 定数
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/TypedMeshListPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/TypedMeshListPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/TypedMeshListPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/TypedMeshListPanel.uss";

        private enum TabType { Drawable, Bone, Morph }

        // ================================================================
        // UI要素
        // ================================================================

        private Button _tabDrawable, _tabBone, _tabMorph;
        private VisualElement _mainContent, _morphPlaceholder;
        private TreeView _treeView;
        private Label _countLabel, _statusLabel;
        private Toggle _showInfoToggle;

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
        private FloatField _restSclX, _restSclY, _restSclZ;
        private VisualElement _poseLayersContainer;
        private Label _poseNoLayersLabel;
        private Label _poseResultPos, _poseResultRot;
        private Button _btnInitPose, _btnResetLayers;
        private Label _bindposePos, _bindposeRot, _bindposeScl;
        private Button _btnBakePose;
        private bool _isSyncingPoseUI = false;

        // ================================================================
        // データ
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        [NonSerialized] private TypedTreeRoot _treeRoot;
        [NonSerialized] private TreeViewDragDropHelper<TypedTreeAdapter> _dragDropHelper;

        private TabType _currentTab = TabType.Drawable;
        private List<TypedTreeAdapter> _selectedAdapters = new List<TypedTreeAdapter>();
        private bool _isSyncingFromExternal = false;

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

        [MenuItem("Poly_Ling/Typed Mesh List")]
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
            UnsubscribeFromModel();
            if (_toolContext?.UndoController != null)
                _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
            CleanupDragDrop();
        }

        private void CreateGUI()
        {
            BuildUI();
            SetupTreeView();
            RegisterButtonEvents();
            RefreshAll();
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            UnsubscribeFromModel();
            if (_toolContext?.UndoController != null)
                _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;

            _toolContext = ctx;

            if (_toolContext?.Model != null)
            {
                CreateTreeRoot();
                SetupDragDrop();
                SubscribeToModel();

                if (_toolContext.UndoController != null)
                    _toolContext.UndoController.OnUndoRedoPerformed += OnUndoRedoPerformed;

                RefreshAll();
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
                    RefreshTree();
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
                Model.OnListChanged += OnModelListChanged;
        }

        private void UnsubscribeFromModel()
        {
            if (_toolContext?.Model != null)
                _toolContext.Model.OnListChanged -= OnModelListChanged;
        }

        private void OnModelListChanged()
        {
            if (_isSyncingFromExternal) return;

            _isSyncingFromExternal = true;
            try
            {
                _treeRoot?.Rebuild();
                RefreshAll();
                SyncTreeViewSelection();
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
                RefreshAll();
                SyncTreeViewSelection();
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
            _morphPlaceholder = root.Q<VisualElement>("morph-placeholder");

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

            // モーフタブはプレースホルダー
            bool isMorph = tab == TabType.Morph;
            if (_mainContent != null)
                _mainContent.style.display = isMorph ? DisplayStyle.None : DisplayStyle.Flex;
            if (_morphPlaceholder != null)
                _morphPlaceholder.style.display = isMorph ? DisplayStyle.Flex : DisplayStyle.None;

            // ツリーを再構築
            if (Model != null && !isMorph)
            {
                CreateTreeRoot();
                SetupDragDrop();
            }

            _selectedAdapters.Clear();
            RefreshAll();
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

            _treeView.makeItem = MakeTreeItem;
            _treeView.bindItem = BindTreeItem;
            _treeView.selectionType = SelectionType.Multiple;
            _treeView.selectionChanged += OnSelectionChanged;
            _treeView.itemExpandedChanged += OnItemExpandedChanged;
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

            attrContainer.Add(CreateAttributeButton("vis-btn", "👁", "可視性切り替え"));
            attrContainer.Add(CreateAttributeButton("lock-btn", "🔒", "ロック切り替え"));
            attrContainer.Add(CreateAttributeButton("sym-btn", "⇆", "対称切り替え"));

            container.Add(attrContainer);

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

            var nameLabel = element.Q<Label>("name");
            if (nameLabel != null)
                nameLabel.text = adapter.DisplayName;

            var infoLabel = element.Q<Label>("info");
            if (infoLabel != null)
            {
                bool showInfo = _showInfoToggle?.value ?? true;
                if (_currentTab == TabType.Bone)
                {
                    int boneIdx = Model?.TypedIndices.MasterToBoneIndex(adapter.MasterIndex) ?? -1;
                    infoLabel.text = showInfo ? $"Bone:{boneIdx}" : "";
                }
                else
                {
                    infoLabel.text = showInfo ? adapter.GetInfoString() : "";
                }
                infoLabel.style.display = showInfo ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // 属性ボタン
            var visBtn = element.Q<Button>("vis-btn");
            if (visBtn != null)
            {
                UpdateAttributeButton(visBtn, adapter.IsVisible, "👁", "−");
                SetupAttributeButtonCallback(visBtn, adapter, OnVisibilityToggle);
            }

            var lockBtn = element.Q<Button>("lock-btn");
            if (lockBtn != null)
            {
                UpdateAttributeButton(lockBtn, adapter.IsLocked, "🔒", "🔓");
                SetupAttributeButtonCallback(lockBtn, adapter, OnLockToggle);
            }

            var symBtn = element.Q<Button>("sym-btn");
            if (symBtn != null)
            {
                bool hasMirror = adapter.MirrorType > 0 || adapter.IsBakedMirror;
                UpdateAttributeButton(symBtn, hasMirror, adapter.GetMirrorTypeDisplay(), "");
                SetupAttributeButtonCallback(symBtn, adapter, OnSymmetryToggle);
                symBtn.style.display = _currentTab == TabType.Drawable ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void UpdateAttributeButton(Button btn, bool isActive, string activeIcon, string inactiveIcon)
        {
            btn.text = isActive ? activeIcon : inactiveIcon;
            btn.style.opacity = isActive ? 1f : 0.3f;
        }

        private void SetupAttributeButtonCallback(Button btn, TypedTreeAdapter adapter, Action<TypedTreeAdapter> callback)
        {
            btn.clickable = new Clickable(() => callback(adapter));
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

        private void RefreshTree()
        {
            if (_treeView == null || _treeRoot == null) return;

            var treeData = TreeViewHelper.BuildTreeData(_treeRoot.RootItems);
            _treeView.SetRootItems(treeData);
            _treeView.Rebuild();

            RestoreExpandedStates(_treeRoot.RootItems);
        }

        private void RestoreExpandedStates(List<TypedTreeAdapter> items)
        {
            foreach (var item in items)
            {
                if (item.IsExpanded)
                    _treeView.ExpandItem(item.Id, false);
                else
                    _treeView.CollapseItem(item.Id, false);

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
                var pose = GetSelectedBonePoseData();
                if (pose == null) return;
                setter(pose, evt.newValue);
                pose.SetDirty();
                UpdateBonePosePanel();
                NotifyModelChanged();
            });
        }

        private void RegisterRestRotField(FloatField field, int axis)
        {
            field?.RegisterValueChangedCallback(evt =>
            {
                if (_isSyncingPoseUI) return;
                var pose = GetSelectedBonePoseData();
                if (pose == null) return;

                Vector3 euler = IsQuatValid(pose.RestRotation)
                    ? pose.RestRotation.eulerAngles
                    : Vector3.zero;

                if (axis == 0) euler.x = evt.newValue;
                else if (axis == 1) euler.y = evt.newValue;
                else euler.z = evt.newValue;

                pose.RestRotation = Quaternion.Euler(euler);
                pose.SetDirty();
                UpdateBonePosePanel();
                NotifyModelChanged();
            });
        }

        private void OnPoseActiveChanged(ChangeEvent<bool> evt)
        {
            if (_isSyncingPoseUI) return;
            var ctx = GetSelectedMeshContext();
            if (ctx == null) return;

            if (evt.newValue)
            {
                // ON: BonePoseDataがなければIdentityで自動生成
                if (ctx.BonePoseData == null)
                {
                    // RestPose = Identity（デルタなし = 見た目変わらない）
                    ctx.BonePoseData = new BonePoseData();
                }
                ctx.BonePoseData.IsActive = true;
                ctx.BonePoseData.SetDirty();
            }
            else
            {
                // OFF: データは残してIsActiveだけfalse
                if (ctx.BonePoseData != null)
                {
                    ctx.BonePoseData.IsActive = false;
                    ctx.BonePoseData.SetDirty();
                }
            }
            UpdateBonePosePanel();
            NotifyModelChanged();
        }

        private void OnInitPoseClicked()
        {
            var ctx = GetSelectedMeshContext();
            if (ctx == null) return;

            if (ctx.BonePoseData == null)
            {
                // RestPose = Identity
                ctx.BonePoseData = new BonePoseData();
                ctx.BonePoseData.IsActive = true;
                Log("BonePoseData初期化完了（Identity）");
            }
            else
            {
                Log("BonePoseDataは既に存在");
            }
            UpdateBonePosePanel();
            NotifyModelChanged();
        }

        private void OnResetLayersClicked()
        {
            var pose = GetSelectedBonePoseData();
            if (pose == null) return;
            pose.ClearAllLayers();
            UpdateBonePosePanel();
            NotifyModelChanged();
            Log("全レイヤーをクリア");
        }

        private void OnBakePoseClicked()
        {
            var ctx = GetSelectedMeshContext();
            if (ctx == null || ctx.BonePoseData == null) return;
            ctx.BonePoseData.BakeToBindPose(ctx.WorldMatrix);
            ctx.BindPose = ctx.WorldMatrix.inverse;
            UpdateBonePosePanel();
            NotifyModelChanged();
            Log("BindPoseにベイク完了");
        }

        /// <summary>
        /// BonePoseパネルの表示を更新
        /// </summary>
        private void UpdateBonePosePanel()
        {
            if (_bonePoseSection == null) return;
            if (_currentTab != TabType.Bone) return;

            _isSyncingPoseUI = true;
            try
            {
                var ctx = GetSelectedMeshContext();

                // BonePoseData
                var pose = ctx?.BonePoseData;
                bool hasPose = pose != null;

                _poseActiveToggle?.SetValueWithoutNotify(hasPose && pose.IsActive);
                _poseActiveToggle?.SetEnabled(ctx != null);

                // RestPose
                Vector3 restPos = hasPose ? pose.RestPosition : Vector3.zero;
                Vector3 restRot = hasPose && IsQuatValid(pose.RestRotation)
                    ? pose.RestRotation.eulerAngles
                    : Vector3.zero;
                Vector3 restScl = hasPose ? pose.RestScale : Vector3.one;

                SetFloatField(_restPosX, restPos.x, hasPose);
                SetFloatField(_restPosY, restPos.y, hasPose);
                SetFloatField(_restPosZ, restPos.z, hasPose);
                SetFloatField(_restRotX, restRot.x, hasPose);
                SetFloatField(_restRotY, restRot.y, hasPose);
                SetFloatField(_restRotZ, restRot.z, hasPose);
                SetFloatField(_restSclX, restScl.x, hasPose);
                SetFloatField(_restSclY, restScl.y, hasPose);
                SetFloatField(_restSclZ, restScl.z, hasPose);

                // レイヤー一覧
                UpdateLayersList(pose);

                // 合成結果
                if (hasPose)
                {
                    Vector3 pos = pose.Position;
                    Vector3 rot = IsQuatValid(pose.Rotation)
                        ? pose.Rotation.eulerAngles
                        : Vector3.zero;
                    if (_poseResultPos != null)
                        _poseResultPos.text = $"Pos: ({pos.x:F3}, {pos.y:F3}, {pos.z:F3})";
                    if (_poseResultRot != null)
                        _poseResultRot.text = $"Rot: ({rot.x:F1}, {rot.y:F1}, {rot.z:F1})";
                }
                else
                {
                    if (_poseResultPos != null) _poseResultPos.text = "Pos: -";
                    if (_poseResultRot != null) _poseResultRot.text = "Rot: -";
                }

                // Initボタンは廃止（Activeトグルで自動生成）
                _btnInitPose?.SetEnabled(false);
                if (_btnInitPose != null)
                    _btnInitPose.style.display = DisplayStyle.None;
                _btnResetLayers?.SetEnabled(hasPose && pose.LayerCount > 0);

                // BindPose
                if (ctx != null)
                {
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
                    if (_bindposePos != null) _bindposePos.text = "Pos: -";
                    if (_bindposeRot != null) _bindposeRot.text = "Rot: -";
                    if (_bindposeScl != null) _bindposeScl.text = "Scl: -";
                }

                _btnBakePose?.SetEnabled(hasPose);
            }
            finally
            {
                _isSyncingPoseUI = false;
            }
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

        private static void SetFloatField(FloatField field, float value, bool enabled)
        {
            if (field == null) return;
            field.SetValueWithoutNotify((float)System.Math.Round(value, 4));
            field.SetEnabled(enabled);
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