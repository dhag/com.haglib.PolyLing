// MeshListPanelUXML.cs
// UXML版メッシュリストパネル
// Unity6 UIToolkit + TreeViewDragDropHelper

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.Localization;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using UIList.UIToolkitExtensions;

namespace Poly_Ling.UI
{
    /// <summary>
    /// UXML版メッシュリストパネル。
    /// 
    /// 機能:
    /// - ツリー形式でメッシュ階層を表示
    /// - ドラッグ&amp;ドロップで並べ替え・親子関係変更
    /// - 複製・削除・名前変更
    /// - 詳細情報表示
    /// 
    /// 将来的にタブコンテナ内に配置可能。
    /// </summary>
    public class MeshListPanelUXML : EditorWindow
    {
        // ================================================================
        // 定数
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MeshListPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MeshListPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/MeshListPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/MeshListPanel.uss";

        // ================================================================
        // UI要素
        // ================================================================

        private TreeView _treeView;
        private Label _meshCountLabel;
        private Toggle _showInfoToggle;
        private Label _statusLabel;

        // 詳細パネル
        private Foldout _detailFoldout;
        private TextField _meshNameField;
        private Label _vertexCountLabel;
        private Label _faceCountLabel;
        private Label _triCountLabel;
        private Label _quadCountLabel;
        private Label _ngonCountLabel;
        private Label _materialCountLabel;

        // ================================================================
        // データ
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        [NonSerialized] private MeshTreeRoot _treeRoot;
        [NonSerialized] private TreeViewDragDropHelper<MeshTreeAdapter> _dragDropHelper;

        // 現在選択中のアダプター
        private List<MeshTreeAdapter> _selectedAdapters = new List<MeshTreeAdapter>();

        // 外部からの同期中フラグ（Undo記録を抑制）
        private bool _isSyncingFromExternal = false;

        // ================================================================
        // ウィンドウ表示
        // ================================================================

        [MenuItem("Poly_Ling/Mesh List (UXML)")]
        public static void ShowWindow()
        {
            var window = GetWindow<MeshListPanelUXML>();
            window.titleContent = new GUIContent("Mesh List (UXML)");
            window.minSize = new Vector2(300, 300);
        }

        /// <summary>
        /// ToolContextを指定してウィンドウを開く
        /// </summary>
        public static MeshListPanelUXML Open(ToolContext ctx)
        {
            var window = GetWindow<MeshListPanelUXML>();
            window.titleContent = new GUIContent("Mesh List (UXML)");
            window.minSize = new Vector2(300, 300);
            window.SetContext(ctx);
            window.Show();
            return window;
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            // ウィンドウが再アクティブ化された時の処理
        }

        private void OnDisable()
        {
            // コールバックを解除
            UnsubscribeFromModel();
            if (_toolContext?.UndoController != null)
            {
                _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
            }
            CleanupDragDrop();
        }

        private void OnDestroy()
        {
            // コールバックを解除
            UnsubscribeFromModel();
            if (_toolContext?.UndoController != null)
            {
                _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
            }
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
        // 外部変更検出（イベント駆動）
        // ================================================================

        /// <summary>
        /// ModelContext.OnListChanged のハンドラ
        /// 本体エディタや他のパネルからの変更を検出
        /// </summary>
        private void OnModelListChanged()
        {
            // 自分が起こした変更は無視
            if (_isSyncingFromExternal) return;

            // 外部からの変更として処理
            _isSyncingFromExternal = true;
            try
            {
                _treeRoot?.RebuildFromModelContext();
                RefreshAll();
                SyncTreeViewSelection();
            }
            finally
            {
                _isSyncingFromExternal = false;
            }
        }

        /// <summary>
        /// 変更を行い、OnListChangedを発火する
        /// </summary>
        private void NotifyModelChanged()
        {
            // フラグを立てて自分の変更であることを示す
            _isSyncingFromExternal = true;
            
            // モデルの変更フラグを立てる
            if (Model != null)
            {
                Model.IsDirty = true;
                // イベント発火（他のパネルに通知）
                Model.OnListChanged?.Invoke();
            }
            
            // SyncMeshコールバック
            _toolContext?.SyncMesh?.Invoke();
            _toolContext?.Repaint?.Invoke();
            
            _isSyncingFromExternal = false;
        }

        /// <summary>
        /// TreeViewの選択状態をModelContextと同期
        /// </summary>
        private void SyncTreeViewSelection()
        {
            if (_treeView == null || _treeRoot == null || Model == null) return;

            // ModelContextの選択インデックスからIDリストを作成
            var selectedIds = new List<int>();
            foreach (var idx in Model.SelectedMeshContextIndices)
            {
                var adapter = _treeRoot.GetAdapterByIndex(idx);
                if (adapter != null)
                {
                    selectedIds.Add(adapter.Id);
                }
            }

            // 外部同期フラグを立てる（OnSelectionChangedでUndo記録を抑制）
            _isSyncingFromExternal = true;
            try
            {
                // TreeViewの選択を更新
                // SetSelectionWithoutNotifyがある場合はそれを使用
                _treeView.SetSelectionWithoutNotify(selectedIds);
            }
            finally
            {
                _isSyncingFromExternal = false;
            }
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        /// <summary>
        /// ToolContextを設定
        /// </summary>
        public void SetContext(ToolContext ctx)
        {
            // 以前のコールバックを解除
            UnsubscribeFromModel();
            if (_toolContext?.UndoController != null)
            {
                _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
            }

            _toolContext = ctx;

            if (_toolContext?.Model != null)
            {
                // MeshTreeRootを作成
                _treeRoot = new MeshTreeRoot(_toolContext.Model, _toolContext);
                _treeRoot.OnChanged = () =>
                {
                    _isSyncingFromExternal = true;
                    try
                    {
                        RefreshTree();
                        SyncTreeViewSelection();  // ModelContextの選択をTreeViewに同期
                        UpdateDetailPanel();
                        // D&D完了時に他のパネルに通知
                        NotifyModelChanged();
                        // 順序変更はトポロジー変更なのでCG再構築（OnReorderCompletedでMeshUndoContextも更新される）
                        // 注意: ModelはToolContext.Modelだが、OnReorderCompletedは
                        // UndoController.MeshListContextに設定されている
                        _toolContext?.UndoController?.MeshListContext?.OnReorderCompleted?.Invoke();
                    }
                    finally
                    {
                        _isSyncingFromExternal = false;
                    }
                };

                // D&Dを設定
                SetupDragDrop();

                // コールバックを登録
                SubscribeToModel();
                if (_toolContext.UndoController != null)
                {
                    _toolContext.UndoController.OnUndoRedoPerformed += OnUndoRedoPerformed;
                }

                // 表示を更新
                RefreshAll();
            }
        }

        /// <summary>
        /// ModelContextのイベントを購読
        /// </summary>
        private void SubscribeToModel()
        {
            if (Model != null)
            {
                Model.OnListChanged += OnModelListChanged;
            }
        }

        /// <summary>
        /// ModelContextのイベント購読を解除
        /// </summary>
        private void UnsubscribeFromModel()
        {
            if (_toolContext?.Model != null)
            {
                _toolContext.Model.OnListChanged -= OnModelListChanged;
            }
        }

        /// <summary>
        /// Undo/Redo実行後の更新
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            // 外部同期フラグを立てる
            _isSyncingFromExternal = true;
            try
            {
                // ツリーを再構築
                _treeRoot?.RebuildFromModelContext();
                RefreshAll();
                SyncTreeViewSelection();
            }
            finally
            {
                _isSyncingFromExternal = false;
            }
        }

        /// <summary>
        /// 現在のModelContext
        /// </summary>
        private ModelContext Model => _toolContext?.Model;

        // ================================================================
        // UI構築
        // ================================================================

        private void BuildUI()
        {
            var root = rootVisualElement;

            // UXMLをロード
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)
                          ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPathAssets);
            if (visualTree != null)
            {
                visualTree.CloneTree(root);
            }
            else
            {
                // UXMLがない場合はコードで構築
                BuildUIFallback(root);
            }

            // USSをロード
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath)
                          ?? AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPathAssets);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            // UI要素を取得
            _treeView = root.Q<TreeView>("mesh-tree");
            _meshCountLabel = root.Q<Label>("mesh-count-label");
            _showInfoToggle = root.Q<Toggle>("show-info-toggle");
            _statusLabel = root.Q<Label>("status-label");

            // 詳細パネル
            _detailFoldout = root.Q<Foldout>("detail-foldout");
            _meshNameField = root.Q<TextField>("mesh-name-field");
            _vertexCountLabel = root.Q<Label>("vertex-count-label");
            _faceCountLabel = root.Q<Label>("face-count-label");
            _triCountLabel = root.Q<Label>("tri-count-label");
            _quadCountLabel = root.Q<Label>("quad-count-label");
            _ngonCountLabel = root.Q<Label>("ngon-count-label");
            _materialCountLabel = root.Q<Label>("material-count-label");

            // Info表示トグルの変更を監視
            if (_showInfoToggle != null)
            {
                _showInfoToggle.RegisterValueChangedCallback(_ => RefreshTree());
            }

            // 名前フィールドの変更を監視
            if (_meshNameField != null)
            {
                _meshNameField.RegisterValueChangedCallback(evt =>
                {
                    if (_selectedAdapters.Count == 1)
                    {
                        var adapter = _selectedAdapters[0];
                        int index = adapter.GetCurrentIndex();
                        if (index >= 0 && !string.IsNullOrEmpty(evt.newValue))
                        {
                            string newName = evt.newValue;
                            
                            // コマンド発行（Undoは本体で記録）
                            _toolContext?.UpdateMeshAttributes?.Invoke(new[]
                            {
                                new MeshAttributeChange { Index = index, Name = newName }
                            });
                            
                            Log($"名前変更: {newName}");
                        }
                    }
                });
            }
        }

        /// <summary>
        /// UXMLがない場合のフォールバックUI構築
        /// </summary>
        private void BuildUIFallback(VisualElement root)
        {
            root.style.paddingLeft = root.style.paddingRight = 4;
            root.style.paddingTop = root.style.paddingBottom = 4;

            // ヘッダー
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.Add(new Label { name = "mesh-count-label", text = "Meshes: 0" });
            header.Add(new VisualElement { style = { flexGrow = 1 } });
            header.Add(new Toggle { name = "show-info-toggle", label = "Info", value = true });
            header.Add(new Button { name = "btn-add", text = "+" });
            root.Add(header);

            // TreeView
            var treeView = new TreeView { name = "mesh-tree" };
            treeView.style.flexGrow = 1;
            treeView.style.marginTop = treeView.style.marginBottom = 4;
            treeView.selectionType = SelectionType.Multiple;
            root.Add(treeView);

            // ツールバー
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.justifyContent = Justify.SpaceBetween;

            var leftButtons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            leftButtons.Add(new Button { name = "btn-up", text = "↑" });
            leftButtons.Add(new Button { name = "btn-down", text = "↓" });
            leftButtons.Add(new Button { name = "btn-outdent", text = "←" });
            leftButtons.Add(new Button { name = "btn-indent", text = "→" });
            toolbar.Add(leftButtons);

            var rightButtons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            rightButtons.Add(new Button { name = "btn-duplicate", text = "D" });
            rightButtons.Add(new Button { name = "btn-delete", text = "X" });
            toolbar.Add(rightButtons);

            root.Add(toolbar);

            // 詳細パネル
            var detailFoldout = new Foldout { name = "detail-foldout", text = "詳細", value = true };
            detailFoldout.Add(new TextField { name = "mesh-name-field", label = "名前" });
            detailFoldout.Add(new Label { name = "vertex-count-label", text = "頂点: -" });
            detailFoldout.Add(new Label { name = "face-count-label", text = "面: -" });
            detailFoldout.Add(new Label { name = "tri-count-label", text = "三角形: -" });
            detailFoldout.Add(new Label { name = "quad-count-label", text = "四角形: -" });
            detailFoldout.Add(new Label { name = "ngon-count-label", text = "多角形: -" });
            detailFoldout.Add(new Label { name = "material-count-label", text = "マテリアル: -" });

            var detailButtons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            detailButtons.Add(new Button { name = "btn-to-top", text = "先頭へ" });
            detailButtons.Add(new Button { name = "btn-to-bottom", text = "末尾へ" });
            detailFoldout.Add(detailButtons);

            root.Add(detailFoldout);

            // ステータス
            root.Add(new Label { name = "status-label", text = "" });
        }

        // ================================================================
        // TreeView設定
        // ================================================================

        private void SetupTreeView()
        {
            if (_treeView == null) return;

            // 行の作成
            _treeView.makeItem = MakeTreeItem;

            // 行のバインド
            _treeView.bindItem = BindTreeItem;

            // 選択タイプ
            _treeView.selectionType = SelectionType.Multiple;

            // 選択変更イベント
            _treeView.selectionChanged += OnSelectionChanged;

            // 展開状態変更イベント
            _treeView.itemExpandedChanged += OnItemExpandedChanged;
        }

        /// <summary>
        /// ツリーアイテムを作成
        /// </summary>
        private VisualElement MakeTreeItem()
        {
            var container = new VisualElement();
            container.style.flexDirection = FlexDirection.Row;
            container.style.flexGrow = 1;
            container.style.alignItems = Align.Center;
            container.style.paddingLeft = 2;
            container.style.paddingRight = 4;

            // 名前ラベル（伸縮可能、はみ出しは省略）
            var nameLabel = new Label { name = "name" };
            nameLabel.style.flexGrow = 1;
            nameLabel.style.flexShrink = 1;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.marginRight = 4;
            container.Add(nameLabel);

            // 情報ラベル（固定幅、縮まない）
            var infoLabel = new Label { name = "info" };
            infoLabel.style.width = 80;
            infoLabel.style.flexShrink = 0;
            infoLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            infoLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            infoLabel.style.fontSize = 11;
            infoLabel.style.marginRight = 4;
            container.Add(infoLabel);

            // === 属性ボタン群 ===
            var attrContainer = new VisualElement();
            attrContainer.style.flexDirection = FlexDirection.Row;
            attrContainer.style.flexShrink = 0;

            // 可視性ボタン（👁）
            var visBtn = CreateAttributeButton("vis-btn", "👁", "可視性切り替え");
            attrContainer.Add(visBtn);

            // ロックボタン（🔒）
            var lockBtn = CreateAttributeButton("lock-btn", "🔒", "ロック切り替え");
            attrContainer.Add(lockBtn);

            // 対称ボタン（⇆）
            var symBtn = CreateAttributeButton("sym-btn", "⇆", "対称切り替え");
            attrContainer.Add(symBtn);

            container.Add(attrContainer);

            return container;
        }

        /// <summary>
        /// 属性トグルボタンを作成
        /// </summary>
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

        /// <summary>
        /// ツリーアイテムにデータをバインド
        /// </summary>
        private void BindTreeItem(VisualElement element, int index)
        {
            var adapter = _treeView.GetItemDataForIndex<MeshTreeAdapter>(index);
            if (adapter == null) return;

            // 名前
            var nameLabel = element.Q<Label>("name");
            if (nameLabel != null)
            {
                nameLabel.text = adapter.DisplayName;
            }

            // 情報（トグルで表示切り替え）
            var infoLabel = element.Q<Label>("info");
            if (infoLabel != null)
            {
                bool showInfo = _showInfoToggle?.value ?? true;
                infoLabel.text = showInfo ? adapter.GetInfoString() : "";
                infoLabel.style.display = showInfo ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // === 属性ボタン ===

            // 可視性ボタン
            var visBtn = element.Q<Button>("vis-btn");
            if (visBtn != null)
            {
                UpdateAttributeButton(visBtn, adapter.IsVisible, "👁", "−");
                SetupAttributeButtonCallback(visBtn, adapter, OnVisibilityToggle);
            }

            // ロックボタン
            var lockBtn = element.Q<Button>("lock-btn");
            if (lockBtn != null)
            {
                UpdateAttributeButton(lockBtn, adapter.IsLocked, "🔒", "−");
                SetupAttributeButtonCallback(lockBtn, adapter, OnLockToggle);
            }

            // 対称ボタン
            var symBtn = element.Q<Button>("sym-btn");
            if (symBtn != null)
            {
                bool isSymmetric = adapter.MirrorType > 0 || adapter.IsBakedMirror;
                string symText = adapter.GetMirrorTypeDisplay();
                if (string.IsNullOrEmpty(symText)) symText = "−";
                
                UpdateAttributeButton(symBtn, isSymmetric, symText, "−");
                
                // ベイクドミラーは特別な色
                if (adapter.IsBakedMirror)
                {
                    symBtn.style.color = new Color(0.8f, 0.58f, 0.84f);
                }
                
                SetupAttributeButtonCallback(symBtn, adapter, OnSymmetryToggle);
            }
        }

        /// <summary>
        /// 属性ボタンの表示を更新
        /// </summary>
        private void UpdateAttributeButton(Button btn, bool isActive, string activeText, string inactiveText)
        {
            btn.text = isActive ? activeText : inactiveText;
            btn.style.opacity = isActive ? 1f : 0.3f;
            btn.style.color = isActive ? new Color(0.31f, 0.76f, 0.97f) : new Color(0.5f, 0.5f, 0.5f);
        }

        /// <summary>
        /// 属性ボタンのコールバックを設定（重複登録を防止）
        /// </summary>
        private void SetupAttributeButtonCallback(Button btn, MeshTreeAdapter adapter, Action<MeshTreeAdapter> callback)
        {
            // 既存のコールバックを削除するため、userDataに格納
            btn.userData = adapter;
            
            // イベントを一度解除してから再登録
            btn.UnregisterCallback<ClickEvent>(OnAttributeButtonClick);
            btn.RegisterCallback<ClickEvent>(OnAttributeButtonClick);
            
            // コールバックをボタン名で識別して格納
            if (!_attributeCallbacks.ContainsKey(btn.name))
            {
                _attributeCallbacks[btn.name] = callback;
            }
        }

        // 属性ボタンのコールバックマップ
        private Dictionary<string, Action<MeshTreeAdapter>> _attributeCallbacks = new Dictionary<string, Action<MeshTreeAdapter>>()
        {
            { "vis-btn", null },
            { "lock-btn", null },
            { "sym-btn", null }
        };

        /// <summary>
        /// 属性ボタンのクリックイベントハンドラ
        /// </summary>
        private void OnAttributeButtonClick(ClickEvent evt)
        {
            if (evt.target is Button btn && btn.userData is MeshTreeAdapter adapter)
            {
                // ボタン名に応じたコールバックを実行
                switch (btn.name)
                {
                    case "vis-btn":
                        OnVisibilityToggle(adapter);
                        break;
                    case "lock-btn":
                        OnLockToggle(adapter);
                        break;
                    case "sym-btn":
                        OnSymmetryToggle(adapter);
                        break;
                }
                
                // イベントの伝播を止める（TreeViewの選択を変えない）
                evt.StopPropagation();
            }
        }

        // === 属性トグル処理 ===

        private void OnVisibilityToggle(MeshTreeAdapter adapter)
        {
            int index = adapter.GetCurrentIndex();
            if (index < 0) return;
            
            bool newValue = !adapter.IsVisible;
            
            // コマンド発行（Undoは本体で記録）
            _toolContext?.UpdateMeshAttributes?.Invoke(new[]
            {
                new MeshAttributeChange { Index = index, IsVisible = newValue }
            });
            
            Log($"可視性: {adapter.DisplayName} → {(newValue ? "表示" : "非表示")}");
        }

        private void OnLockToggle(MeshTreeAdapter adapter)
        {
            int index = adapter.GetCurrentIndex();
            if (index < 0) return;
            
            bool newValue = !adapter.IsLocked;
            
            // コマンド発行（Undoは本体で記録）
            _toolContext?.UpdateMeshAttributes?.Invoke(new[]
            {
                new MeshAttributeChange { Index = index, IsLocked = newValue }
            });
            
            Log($"ロック: {adapter.DisplayName} → {(newValue ? "ロック" : "解除")}");
        }

        private void OnSymmetryToggle(MeshTreeAdapter adapter)
        {
            // ベイクドミラーは変更不可
            if (adapter.IsBakedMirror)
            {
                Log("ベイクドミラーは対称設定を変更できません");
                return;
            }
            
            int index = adapter.GetCurrentIndex();
            if (index < 0) return;
            
            int newMirrorType = (adapter.MirrorType + 1) % 4;
            
            // コマンド発行（Undoは本体で記録）
            _toolContext?.UpdateMeshAttributes?.Invoke(new[]
            {
                new MeshAttributeChange { Index = index, MirrorType = newMirrorType }
            });
            
            string[] mirrorNames = { "なし", "X軸", "Y軸", "Z軸" };
            Log($"対称: {adapter.DisplayName} → {mirrorNames[newMirrorType]}");
        }

        // ================================================================
        // Undo/Redo サポート
        // ================================================================

        /// <summary>
        /// 選択変更をUndoスタックに記録
        /// </summary>
        private void RecordSelectionChange(HashSet<int> oldSelection, HashSet<int> newSelection)
        {
            var undoController = _toolContext?.UndoController;
            var commandQueue = _toolContext?.CommandQueue;
            if (undoController == null || commandQueue == null) return;
            
            // 同じなら記録しない
            if (oldSelection.SetEquals(newSelection)) return;
            
            commandQueue.Enqueue(new RecordMeshSelectionChangeCommand(
                undoController,
                oldSelection.Count > 0 ? oldSelection.First() : -1,
                newSelection.Count > 0 ? newSelection.First() : -1
            ));
        }

        /// <summary>
        /// MeshContextの変更を本体エディタに通知
        /// </summary>
        private void NotifyMeshContextChanged(MeshTreeAdapter adapter)
        {
            NotifyModelChanged();
        }

        /// <summary>
        /// リスト構造の変更を本体エディタに通知
        /// </summary>
        private void NotifyListStructureChanged()
        {
            NotifyModelChanged();
        }

        // ================================================================
        // D&D設定
        // ================================================================

        private void SetupDragDrop()
        {
            CleanupDragDrop();

            if (_treeView == null || _treeRoot == null) return;

            // ドラッグ開始前にスナップショットを保存するイベントを登録
            _treeView.RegisterCallback<PointerDownEvent>(OnTreeViewPointerDown, TrickleDown.TrickleDown);

            // D&Dヘルパーを作成
            _dragDropHelper = new TreeViewDragDropHelper<MeshTreeAdapter>(
                _treeView,
                _treeRoot,
                new MeshDragValidator()
            );
            _dragDropHelper.Setup();
        }

        private void CleanupDragDrop()
        {
            // イベント解除
            if (_treeView != null)
            {
                _treeView.UnregisterCallback<PointerDownEvent>(OnTreeViewPointerDown, TrickleDown.TrickleDown);
            }

            _dragDropHelper?.Cleanup();
            _dragDropHelper = null;
        }

        /// <summary>
        /// TreeViewのPointerDown：D&D開始前にスナップショットを保存
        /// </summary>
        private void OnTreeViewPointerDown(PointerDownEvent evt)
        {
            // 左クリックのみ
            if (evt.button != 0) return;

            // D&D用にスナップショットを保存
            _treeRoot?.SavePreChangeSnapshot();
        }

        // ================================================================
        // ボタンイベント登録
        // ================================================================

        private void RegisterButtonEvents()
        {
            var root = rootVisualElement;

            // 追加
            root.Q<Button>("btn-add")?.RegisterCallback<ClickEvent>(_ => OnAddClicked());

            // 上下移動
            root.Q<Button>("btn-up")?.RegisterCallback<ClickEvent>(_ => MoveSelected(-1));
            root.Q<Button>("btn-down")?.RegisterCallback<ClickEvent>(_ => MoveSelected(1));

            // 階層変更
            root.Q<Button>("btn-outdent")?.RegisterCallback<ClickEvent>(_ => OutdentSelected());
            root.Q<Button>("btn-indent")?.RegisterCallback<ClickEvent>(_ => IndentSelected());

            // 複製・削除
            root.Q<Button>("btn-duplicate")?.RegisterCallback<ClickEvent>(_ => DuplicateSelected());
            root.Q<Button>("btn-delete")?.RegisterCallback<ClickEvent>(_ => DeleteSelected());

            // 先頭・末尾へ
            root.Q<Button>("btn-to-top")?.RegisterCallback<ClickEvent>(_ => MoveToTop());
            root.Q<Button>("btn-to-bottom")?.RegisterCallback<ClickEvent>(_ => MoveToBottom());
        }

        // ================================================================
        // イベントハンドラ
        // ================================================================

        private void OnSelectionChanged(IEnumerable<object> selection)
        {
            // 変更前の選択インデックスを保存
            var oldSelectedIndices = _selectedAdapters
                .Select(a => a.GetCurrentIndex())
                .Where(i => i >= 0)
                .ToArray();

            _selectedAdapters.Clear();

            foreach (var item in selection)
            {
                if (item is MeshTreeAdapter adapter)
                {
                    _selectedAdapters.Add(adapter);
                }
            }

            // 変更後の選択インデックス
            var newSelectedIndices = _selectedAdapters
                .Select(a => a.GetCurrentIndex())
                .Where(i => i >= 0)
                .ToArray();

            // 外部同期中はUndo記録と本体通知をスキップ
            if (!_isSyncingFromExternal)
            {
                // Undo記録（変化があった場合のみ）
                if (!oldSelectedIndices.SequenceEqual(newSelectedIndices))
                {
                    RecordMultiSelectionChange(oldSelectedIndices, newSelectedIndices);
                }

                // ModelContextの選択も更新
                if (_treeRoot != null)
                {
                    _treeRoot.SelectMultiple(_selectedAdapters);
                }

                // v2.1: 複数選択対応 - SelectMeshContextは単一選択になるため呼ばない
                // 代わりにOnMeshSelectionChangedでGPUバッファを同期
                _toolContext?.OnMeshSelectionChanged?.Invoke();

                // 本体エディタに反映
                NotifySelectionChanged();
            }

            UpdateDetailPanel();
        }

        /// <summary>
        /// 複数選択変更をUndoスタックに記録
        /// </summary>
        private void RecordMultiSelectionChange(int[] oldIndices, int[] newIndices)
        {
            var undoController = _toolContext?.UndoController;
            if (undoController == null) return;

            var record = new MeshMultiSelectionChangeRecord(oldIndices, newIndices);
            undoController.MeshListStack.Record(record, "メッシュ選択変更");
            undoController.FocusMeshList();
        }

        /// <summary>
        /// 選択変更を本体エディタに通知
        /// </summary>
        private void NotifySelectionChanged()
        {
            NotifyModelChanged();
        }

        private void OnItemExpandedChanged(TreeViewExpansionChangedArgs args)
        {
            if (_treeRoot == null) return;

            var adapter = _treeRoot.FindById(args.id);
            if (adapter != null)
            {
                adapter.IsExpanded = _treeView.IsExpanded(args.id);
            }
        }

        // ================================================================
        // 操作メソッド
        // ================================================================

        private void OnAddClicked()
        {
            // 新規メッシュ追加
            if (_toolContext?.AddMeshContext != null)
            {
                var newMeshContext = new MeshContext
                {
                    MeshObject = new MeshObject("New Mesh"),
                    UnityMesh = new Mesh(),
                    OriginalPositions = new Vector3[0]
                };
                _toolContext.AddMeshContext(newMeshContext);
                _treeRoot?.RebuildFromModelContext();
                RefreshAll();
                Log("新規メッシュを追加");
            }
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
                _treeRoot.RebuildParentReferences();
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
                _treeRoot.RebuildParentReferences();
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
                int index = adapter.GetCurrentIndex();
                if (index >= 0)
                {
                    _toolContext?.DuplicateMeshContent?.Invoke(index);
                }
            }

            _treeRoot?.RebuildFromModelContext();
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

            // 確認ダイアログ
            string message = _selectedAdapters.Count == 1
                ? $"'{_selectedAdapters[0].DisplayName}' を削除しますか？"
                : $"{_selectedAdapters.Count}個のメッシュを削除しますか？";

            if (!EditorUtility.DisplayDialog("削除確認", message, "削除", "キャンセル"))
            {
                return;
            }

            // インデックス降順でソート（後ろから削除）
            var sorted = _selectedAdapters
                .OrderByDescending(a => a.GetCurrentIndex())
                .ToList();

            foreach (var adapter in sorted)
            {
                int index = adapter.GetCurrentIndex();
                if (index >= 0)
                {
                    _toolContext?.RemoveMeshContext?.Invoke(index);
                }
            }

            _selectedAdapters.Clear();
            _treeRoot?.RebuildFromModelContext();
            RefreshAll();
            Log($"削除: {sorted.Count}個");
        }

        private void MoveToTop()
        {
            if (_selectedAdapters.Count != 1)
            {
                Log("1つだけ選択してください");
                return;
            }

            var adapter = _selectedAdapters[0];
            int currentIndex = adapter.GetCurrentIndex();

            if (currentIndex > 0)
            {
                _toolContext?.ReorderMeshContext?.Invoke(currentIndex, 0);
                _treeRoot?.RebuildFromModelContext();
                RefreshAll();
                Log("先頭へ移動");
            }
        }

        private void MoveToBottom()
        {
            if (_selectedAdapters.Count != 1)
            {
                Log("1つだけ選択してください");
                return;
            }

            var adapter = _selectedAdapters[0];
            int currentIndex = adapter.GetCurrentIndex();
            int lastIndex = (Model?.MeshContextCount ?? 1) - 1;

            if (currentIndex < lastIndex)
            {
                _toolContext?.ReorderMeshContext?.Invoke(currentIndex, lastIndex);
                _treeRoot?.RebuildFromModelContext();
                RefreshAll();
                Log("末尾へ移動");
            }
        }

        // ================================================================
        // 表示更新
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

            // TreeView用データを構築
            var treeData = TreeViewHelper.BuildTreeData(_treeRoot.RootItems);
            _treeView.SetRootItems(treeData);
            _treeView.Rebuild();

            // 展開状態を復元
            RestoreExpandedStates(_treeRoot.RootItems);
        }

        private void RestoreExpandedStates(List<MeshTreeAdapter> items)
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
            if (_meshCountLabel != null)
            {
                int count = Model?.MeshContextCount ?? 0;
                _meshCountLabel.text = $"Meshes: {count}";
            }
        }

        private void UpdateDetailPanel()
        {
            if (_selectedAdapters.Count == 0)
            {
                // 選択なし
                SetDetailPanelEnabled(false);
                if (_meshNameField != null) _meshNameField.value = "";
                if (_vertexCountLabel != null) _vertexCountLabel.text = "頂点: -";
                if (_faceCountLabel != null) _faceCountLabel.text = "面: -";
                if (_triCountLabel != null) _triCountLabel.text = "三角形: -";
                if (_quadCountLabel != null) _quadCountLabel.text = "四角形: -";
                if (_ngonCountLabel != null) _ngonCountLabel.text = "多角形: -";
                if (_materialCountLabel != null) _materialCountLabel.text = "マテリアル: -";
                return;
            }

            SetDetailPanelEnabled(true);

            if (_selectedAdapters.Count == 1)
            {
                // 単一選択
                var adapter = _selectedAdapters[0];
                var meshContext = adapter.MeshContext;
                var meshObject = meshContext?.MeshObject;

                if (_meshNameField != null)
                {
                    _meshNameField.SetValueWithoutNotify(adapter.DisplayName);
                    _meshNameField.SetEnabled(true);
                }

                if (meshObject != null)
                {
                    if (_vertexCountLabel != null)
                        _vertexCountLabel.text = $"頂点: {meshObject.VertexCount}";
                    if (_faceCountLabel != null)
                        _faceCountLabel.text = $"面: {meshObject.FaceCount}";

                    // 面タイプ内訳
                    int triCount = 0, quadCount = 0, ngonCount = 0;
                    foreach (var face in meshObject.Faces)
                    {
                        if (face.IsTriangle) triCount++;
                        else if (face.IsQuad) quadCount++;
                        else ngonCount++;
                    }

                    if (_triCountLabel != null)
                        _triCountLabel.text = $"三角形: {triCount}";
                    if (_quadCountLabel != null)
                        _quadCountLabel.text = $"四角形: {quadCount}";
                    if (_ngonCountLabel != null)
                    {
                        _ngonCountLabel.text = $"多角形: {ngonCount}";
                        _ngonCountLabel.style.display = ngonCount > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                    }

                    if (_materialCountLabel != null)
                        _materialCountLabel.text = $"マテリアル: {meshContext.SubMeshCount}";
                }
            }
            else
            {
                // 複数選択
                if (_meshNameField != null)
                {
                    _meshNameField.SetValueWithoutNotify($"({_selectedAdapters.Count}個選択)");
                    _meshNameField.SetEnabled(false);
                }

                // 合計を計算
                int totalVerts = _selectedAdapters.Sum(a => a.VertexCount);
                int totalFaces = _selectedAdapters.Sum(a => a.FaceCount);

                if (_vertexCountLabel != null)
                    _vertexCountLabel.text = $"頂点: {totalVerts} (合計)";
                if (_faceCountLabel != null)
                    _faceCountLabel.text = $"面: {totalFaces} (合計)";
            }
        }

        private void SetDetailPanelEnabled(bool enabled)
        {
            if (_detailFoldout != null)
            {
                _detailFoldout.SetEnabled(enabled);
            }
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        private void Log(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = message;
            }
            Debug.Log($"[MeshListPanel] {message}");
        }
    }

    // ================================================================
    // D&Dバリデータ
    // ================================================================

    /// <summary>
    /// メッシュアイテムのドラッグ&amp;ドロップ可否を判定。
    /// ロックは頂点・面編集の禁止であり、D&Dは許可する。
    /// </summary>
    public class MeshDragValidator : IDragDropValidator<MeshTreeAdapter>
    {
        /// <summary>ドラッグ可能か（常にtrue）</summary>
        public bool CanDrag(MeshTreeAdapter item)
        {
            // ロックはメッシュ内の頂点・面の編集禁止であり、
            // リスト内での移動（D&D）は許可する
            return true;
        }

        /// <summary>ドロップ可能か</summary>
        public bool CanDrop(MeshTreeAdapter dragged, MeshTreeAdapter target, DropPosition position)
        {
            // 自分自身や自分の子孫の中にはドロップ不可
            // （TreeViewDragDropHelper側で対処されるはずだが念のため）
            return true;
        }
    }
}
