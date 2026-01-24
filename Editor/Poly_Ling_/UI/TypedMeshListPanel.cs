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

        private const string UxmlPath = "Packages/com.haglib.meshfactory/Editor/Poly_Ling_/UI/TypedMeshListPanel.uxml";
        private const string UssPath = "Packages/com.haglib.meshfactory/Editor/Poly_Ling_/UI/TypedMeshListPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_/UI/TypedMeshListPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_/UI/TypedMeshListPanel.uss";

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
                _isSyncingFromExternal = false;
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
                _isSyncingFromExternal = false;
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

                    // ModelContextの選択も更新
                    if (_treeRoot != null)
                    {
                        _treeRoot.SelectMultiple(_selectedAdapters);
                    }

                    // ToolContextのコールバック（あれば）
                    if (_selectedAdapters.Count > 0 && _toolContext?.SelectMeshContext != null)
                    {
                        var firstIndex = _selectedAdapters[0].MasterIndex;
                        if (firstIndex >= 0)
                        {
                            _toolContext.SelectMeshContext(firstIndex);
                        }
                    }

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
                adapter.IsExpanded = _treeView.IsExpanded(args.id);
        }

        private void SyncTreeViewSelection()
        {
            if (_treeView == null || _treeRoot == null || Model == null) return;

            var selectedIds = new List<int>();
            foreach (var idx in Model.SelectedMeshContextIndices)
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
        }

        private void Log(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
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