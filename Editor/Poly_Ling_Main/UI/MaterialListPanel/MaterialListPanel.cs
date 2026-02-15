// MaterialListPanel.cs
// マテリアルリストパネル（UIToolkit版）
// PolyLing_VertexEdit.DrawMaterialUIの機能をパネル化

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
using Poly_Ling.Selection;

namespace Poly_Ling.UI
{
    /// <summary>
    /// マテリアルリストパネル
    /// ModelContextのマテリアルスロットを管理するUIToolkitベースのEditorWindow
    /// </summary>
    public class MaterialListPanel : EditorWindow
    {
        // ================================================================
        // 定数
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MaterialListPanel/MaterialListPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/MaterialListPanel/MaterialListPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/MaterialListPanel/MaterialListPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/MaterialListPanel/MaterialListPanel.uss";


        // ================================================================
        // UI要素
        // ================================================================

        private Label _titleLabel, _countLabel, _currentLabel;
        private ScrollView _materialList;
        private Button _btnAdd;
        private VisualElement _dropArea;
        private Button _btnSetDefault;
        private Toggle _toggleAutoDefault;
        private Label _defaultInfoLabel;
        private VisualElement _applySection;
        private Label _selectionInfoLabel;
        private Button _btnApply;
        private Label _statusLabel;

        // ================================================================
        // データ
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        [NonSerialized] private bool _isSyncing = false;

        // ================================================================
        // プロパティ
        // ================================================================

        private ModelContext Model => _toolContext?.Model;
        private MeshUndoController UndoController => _toolContext?.UndoController;
        private SelectionState SelectionState => _toolContext?.SelectionState;

        // ================================================================
        // ウィンドウ
        // ================================================================

        [MenuItem("Tools/Poly_Ling/debug/Material List")]
        public static void ShowWindow()
        {
            var window = GetWindow<MaterialListPanel>();
            window.titleContent = new GUIContent("Material List");
            window.minSize = new Vector2(280, 300);
        }

        public static MaterialListPanel Open(ToolContext ctx)
        {
            var window = GetWindow<MaterialListPanel>();
            window.titleContent = new GUIContent("Material List");
            window.minSize = new Vector2(280, 300);
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
            UnsubscribeEvents();
        }

        private void CreateGUI()
        {
            BuildUI();
            RegisterEvents();
            RefreshAll();
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            UnsubscribeEvents();
            _toolContext = ctx;
            SubscribeEvents();
            RefreshAll();
        }

        private void SubscribeEvents()
        {
            if (Model != null)
                Model.OnListChanged += OnModelChanged;
            if (UndoController != null)
                UndoController.OnUndoRedoPerformed += OnUndoRedoPerformed;
        }

        private void UnsubscribeEvents()
        {
            if (_toolContext?.Model != null)
                _toolContext.Model.OnListChanged -= OnModelChanged;
            if (_toolContext?.UndoController != null)
                _toolContext.UndoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
        }

        private void OnModelChanged()
        {
            if (_isSyncing) return;
            RefreshAll();
        }

        private void OnUndoRedoPerformed()
        {
            RefreshAll();
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
            _titleLabel = root.Q<Label>("title-label");
            _countLabel = root.Q<Label>("count-label");
            _currentLabel = root.Q<Label>("current-label");
            _materialList = root.Q<ScrollView>("material-list");
            _btnAdd = root.Q<Button>("btn-add");
            _dropArea = root.Q<VisualElement>("drop-area");
            _btnSetDefault = root.Q<Button>("btn-set-default");
            _toggleAutoDefault = root.Q<Toggle>("toggle-auto-default");
            _defaultInfoLabel = root.Q<Label>("default-info-label");
            _applySection = root.Q<VisualElement>("apply-section");
            _selectionInfoLabel = root.Q<Label>("selection-info-label");
            _btnApply = root.Q<Button>("btn-apply");
            _statusLabel = root.Q<Label>("status-label");
        }

        private void RegisterEvents()
        {
            _btnAdd?.RegisterCallback<ClickEvent>(_ => OnAddSlotClicked());
            _btnSetDefault?.RegisterCallback<ClickEvent>(_ => OnSetDefaultClicked());
            _toggleAutoDefault?.RegisterValueChangedCallback(OnAutoDefaultChanged);
            _btnApply?.RegisterCallback<ClickEvent>(_ => OnApplyToSelectionClicked());

            // ドロップエリアのD&D
            if (_dropArea != null)
            {
                _dropArea.RegisterCallback<DragEnterEvent>(_ =>
                    _dropArea.AddToClassList("drop-area-highlight"));
                _dropArea.RegisterCallback<DragLeaveEvent>(_ =>
                    _dropArea.RemoveFromClassList("drop-area-highlight"));
                _dropArea.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
                _dropArea.RegisterCallback<DragPerformEvent>(OnDragPerform);
            }
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void RefreshAll()
        {
            RefreshMaterialList();
            RefreshCurrentLabel();
            RefreshDefaultSection();
            RefreshApplySection();
        }

        private void RefreshMaterialList()
        {
            if (_materialList == null) return;
            _materialList.Clear();

            if (Model == null)
            {
                if (_countLabel != null) _countLabel.text = "0 slots";
                return;
            }

            int count = Model.MaterialCount;
            if (_countLabel != null) _countLabel.text = $"{count} slots";

            for (int i = 0; i < count; i++)
            {
                _materialList.Add(CreateMaterialRow(i));
            }
        }

        private VisualElement CreateMaterialRow(int index)
        {
            var row = new VisualElement();
            row.AddToClassList("material-row");

            bool isCurrent = (Model != null && index == Model.CurrentMaterialIndex);
            if (isCurrent) row.AddToClassList("material-row-selected");

            // 選択マーカー
            var selectBtn = new Button { text = isCurrent ? "●" : "○" };
            selectBtn.AddToClassList("material-select-btn");
            int capturedIndex = index;
            selectBtn.clicked += () => OnSelectSlot(capturedIndex);
            row.Add(selectBtn);

            // スロット番号
            var indexLabel = new Label($"[{index}]");
            indexLabel.AddToClassList("material-index-label");
            row.Add(indexLabel);

            // マテリアルフィールド
            var matField = new ObjectField();
            matField.objectType = typeof(Material);
            matField.value = Model?.GetMaterial(index);
            matField.AddToClassList("material-field");
            matField.RegisterValueChangedCallback(evt => OnMaterialChanged(capturedIndex, evt.newValue as Material));
            row.Add(matField);

            // 削除ボタン
            var deleteBtn = new Button { text = "×" };
            deleteBtn.AddToClassList("material-delete-btn");
            deleteBtn.SetEnabled(Model != null && Model.MaterialCount > 1);
            deleteBtn.clicked += () => OnRemoveSlot(capturedIndex);
            row.Add(deleteBtn);

            return row;
        }

        private void RefreshCurrentLabel()
        {
            if (_currentLabel == null) return;

            if (Model == null)
            {
                _currentLabel.text = "Current: -";
                return;
            }

            _currentLabel.text = $"Current: [{Model.CurrentMaterialIndex}]";

            Material mat = Model.GetMaterial(Model.CurrentMaterialIndex);
            if (mat == null)
                _currentLabel.text += "  (None: デフォルト使用)";
        }

        private void RefreshDefaultSection()
        {
            if (Model == null) return;

            _toggleAutoDefault?.SetValueWithoutNotify(Model.AutoSetDefaultMaterials);

            if (_defaultInfoLabel != null)
            {
                int count = Model.DefaultMaterials?.Count ?? 0;
                int idx = Model.DefaultCurrentMaterialIndex;
                _defaultInfoLabel.text = $"(Default: {count} slots, idx={idx})";
            }
        }

        private void RefreshApplySection()
        {
            if (_applySection == null) return;

            var selState = SelectionState;
            bool hasFaceSelection = selState != null && selState.Faces.Count > 0;

            _applySection.style.display = hasFaceSelection ? DisplayStyle.Flex : DisplayStyle.None;

            if (!hasFaceSelection) return;

            var meshContext = Model?.FirstSelectedMeshContext;
            if (meshContext?.MeshObject == null) return;

            // 選択面のマテリアル分布
            var materialCounts = new Dictionary<int, int>();
            foreach (int faceIdx in selState.Faces)
            {
                if (faceIdx >= 0 && faceIdx < meshContext.MeshObject.FaceCount)
                {
                    int matIdx = meshContext.MeshObject.Faces[faceIdx].MaterialIndex;
                    if (!materialCounts.ContainsKey(matIdx))
                        materialCounts[matIdx] = 0;
                    materialCounts[matIdx]++;
                }
            }

            string distribution = string.Join(", ",
                materialCounts.OrderBy(kv => kv.Key)
                              .Select(kv => $"[{kv.Key}]:{kv.Value}"));

            if (_selectionInfoLabel != null)
                _selectionInfoLabel.text = $"Selected: {selState.Faces.Count} faces ({distribution})";

            if (_btnApply != null)
                _btnApply.text = $"Apply Material [{Model?.CurrentMaterialIndex ?? 0}] to Selection";
        }

        // ================================================================
        // マテリアル操作
        // ================================================================

        private void OnSelectSlot(int index)
        {
            if (Model == null) return;

            Model.CurrentMaterialIndex = index;
            AutoUpdateDefaultMaterials();
            RefreshAll();
            NotifyChanged();
            Log($"Material slot [{index}] selected");
        }

        private void OnMaterialChanged(int index, Material newMat)
        {
            if (_isSyncing || Model == null) return;

            // Undo用スナップショット
            var before = UndoController?.CaptureMeshObjectSnapshot();

            Model.SetMaterial(index, newMat);

            RecordMaterialChange(before, $"Change Material [{index}]");
            AutoUpdateDefaultMaterials();
            RefreshAll();
            NotifyChanged();
        }

        private void OnAddSlotClicked()
        {
            if (Model == null) return;

            var before = UndoController?.CaptureMeshObjectSnapshot();

            Model.AddMaterial(null);
            Model.CurrentMaterialIndex = Model.MaterialCount - 1;

            RecordMaterialChange(before, "Add Material Slot");
            AutoUpdateDefaultMaterials();
            RefreshAll();
            NotifyChanged();
            Log("Material slot added");
        }

        private void OnRemoveSlot(int index)
        {
            if (Model == null || Model.MaterialCount <= 1) return;
            if (index < 0 || index >= Model.MaterialCount) return;

            var before = UndoController?.CaptureMeshObjectSnapshot();

            // 該当マテリアルを使用している面をスロット0に移動
            var meshContext = Model.FirstSelectedMeshContext;
            if (meshContext?.MeshObject != null)
            {
                foreach (var face in meshContext.MeshObject.Faces)
                {
                    if (face.MaterialIndex == index)
                        face.MaterialIndex = 0;
                    else if (face.MaterialIndex > index)
                        face.MaterialIndex--;
                }
            }

            Model.RemoveMaterialAt(index);

            // CurrentMaterialIndexの調整
            if (Model.CurrentMaterialIndex >= Model.MaterialCount)
                Model.CurrentMaterialIndex = Model.MaterialCount - 1;
            else if (Model.CurrentMaterialIndex > index)
                Model.CurrentMaterialIndex--;
            else if (Model.CurrentMaterialIndex == index)
                Model.CurrentMaterialIndex = 0;

            RecordMaterialChange(before, $"Remove Material Slot [{index}]");

            // メッシュを更新
            _toolContext?.SyncMesh?.Invoke();
            RefreshAll();
            NotifyChanged();
            Log($"Material slot [{index}] removed");
        }

        private void OnSetDefaultClicked()
        {
            if (Model == null || Model.MaterialCount == 0) return;

            var before = UndoController?.CaptureMeshObjectSnapshot();

            Model.DefaultMaterials = new List<Material>(Model.Materials);
            Model.DefaultCurrentMaterialIndex = Model.CurrentMaterialIndex;

            RecordMaterialChange(before, "Set Default Materials");
            RefreshDefaultSection();
            Log("Default materials set");
        }

        private void OnAutoDefaultChanged(ChangeEvent<bool> evt)
        {
            if (_isSyncing || Model == null) return;

            var before = UndoController?.CaptureMeshObjectSnapshot();

            Model.AutoSetDefaultMaterials = evt.newValue;

            RecordMaterialChange(before, $"Auto Default Materials: {(evt.newValue ? "ON" : "OFF")}");
            RefreshDefaultSection();
        }

        private void OnApplyToSelectionClicked()
        {
            if (Model == null) return;

            var meshContext = Model.FirstSelectedMeshContext;
            var selState = SelectionState;
            if (meshContext?.MeshObject == null || selState == null || selState.Faces.Count == 0)
                return;

            int materialIndex = Model.CurrentMaterialIndex;
            if (materialIndex < 0 || materialIndex >= Model.MaterialCount)
                return;

            var before = UndoController?.CaptureMeshObjectSnapshot();

            bool changed = false;
            foreach (int faceIdx in selState.Faces)
            {
                if (faceIdx >= 0 && faceIdx < meshContext.MeshObject.FaceCount)
                {
                    var face = meshContext.MeshObject.Faces[faceIdx];
                    if (face.MaterialIndex != materialIndex)
                    {
                        face.MaterialIndex = materialIndex;
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                _toolContext?.SyncMesh?.Invoke();
                RecordMaterialChange(before, $"Apply Material [{materialIndex}] to {selState.Faces.Count} faces");
                RefreshAll();
                NotifyChanged();
                Log($"Material [{materialIndex}] applied to {selState.Faces.Count} faces");
            }
        }

        // ================================================================
        // ドラッグ＆ドロップ
        // ================================================================

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            bool hasMaterials = DragAndDrop.objectReferences.Any(o => o is Material);
            DragAndDrop.visualMode = hasMaterials ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            evt.StopPropagation();
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            DragAndDrop.AcceptDrag();
            _dropArea?.RemoveFromClassList("drop-area-highlight");

            if (Model == null) return;

            var materials = DragAndDrop.objectReferences.OfType<Material>().ToList();
            if (materials.Count == 0) return;

            var before = UndoController?.CaptureMeshObjectSnapshot();

            foreach (var mat in materials)
            {
                Model.AddMaterial(mat);
            }
            Model.CurrentMaterialIndex = Model.MaterialCount - 1;

            RecordMaterialChange(before, $"Add {materials.Count} Material(s)");
            AutoUpdateDefaultMaterials();
            RefreshAll();
            NotifyChanged();
            Log($"Dropped {materials.Count} material(s)");

            evt.StopPropagation();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void RecordMaterialChange(MeshObjectSnapshot before, string description)
        {
            if (before == null || UndoController == null) return;

            var after = UndoController.CaptureMeshObjectSnapshot();
            var queue = _toolContext?.UndoController?.MainGroup;

            // CommandQueue経由がベストだが、直接記録も可
            UndoController.RecordTopologyChange(before, after, description);
        }

        private void AutoUpdateDefaultMaterials()
        {
            if (Model == null || !Model.AutoSetDefaultMaterials || Model.MaterialCount == 0)
                return;

            Model.DefaultMaterials = new List<Material>(Model.Materials);
            Model.DefaultCurrentMaterialIndex = Model.CurrentMaterialIndex;
        }

        private void NotifyChanged()
        {
            _isSyncing = true;
            try
            {
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
                _isSyncing = false;
            }
        }

        private void Log(string message)
        {
            if (_statusLabel != null)
                _statusLabel.text = message;
        }
    }
}
