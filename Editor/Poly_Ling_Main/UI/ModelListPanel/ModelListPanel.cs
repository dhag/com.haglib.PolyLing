// ModelListPanel.cs
// モデルリスト管理パネル (UIToolkit)
// モデルの一覧表示と選択、名前変更、削除

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.Localization;

namespace Poly_Ling.UI
{
    /// <summary>
    /// モデルリスト管理パネル（UIToolkit版）
    /// </summary>
    public class ModelListPanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // アセットパス
        // ================================================================

        private const string UxmlPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/ModelListPanel/ModelListPanel.uxml";
        private const string UssPath = "Packages/com.haglib.polyling/Editor/Poly_Ling_Main/UI/ModelListPanel/ModelListPanel.uss";
        private const string UxmlPathAssets = "Assets/Editor/Poly_Ling_Main/UI/ModelListPanel/ModelListPanel.uxml";
        private const string UssPathAssets = "Assets/Editor/Poly_Ling_Main/UI/ModelListPanel/ModelListPanel.uss";

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "Model List", ["ja"] = "モデルリスト" },
            ["Models"] = new() { ["en"] = "Models", ["ja"] = "モデル" },
            ["NoProject"] = new() { ["en"] = "No project available", ["ja"] = "プロジェクトがありません" },
            ["NoModels"] = new() { ["en"] = "No models", ["ja"] = "モデルがありません" },
            ["Meshes"] = new() { ["en"] = "meshes", ["ja"] = "メッシュ" },
            ["Delete"] = new() { ["en"] = "Delete", ["ja"] = "削除" },
            ["ConfirmDelete"] = new() { ["en"] = "Delete model \"{0}\"?", ["ja"] = "モデル「{0}」を削除しますか？" },
            ["ModelName"] = new() { ["en"] = "Model Name", ["ja"] = "モデル名" },
        };

        private static string T(string key)
        {
            if (_localize.TryGetValue(key, out var dict))
            {
                string lang = L.GetLanguageKey(L.CurrentLanguage);
                if (dict.TryGetValue(lang, out var text))
                    return text;
                if (dict.TryGetValue("en", out var fallback))
                    return fallback;
            }
            return key;
        }

        // ================================================================
        // コンテキスト
        // ================================================================

        [NonSerialized] private ToolContext _toolContext;
        private ProjectContext Project => _toolContext?.Project;

        // ================================================================
        // UI要素
        // ================================================================

        private Label _warningLabel;
        private VisualElement _renameSection;
        private VisualElement _renameDisplay;
        private VisualElement _renameEdit;
        private Label _renameCaptionLabel;
        private Label _renameEditCaptionLabel;
        private Label _currentNameLabel;
        private TextField _renameField;
        private Button _btnStartRename;
        private Button _btnConfirmRename;
        private Button _btnCancelRename;
        private VisualElement _modelListContainer;

        // ================================================================
        // Open
        // ================================================================

        public static ModelListPanel Open(ToolContext ctx)
        {
            var window = GetWindow<ModelListPanel>();
            window.titleContent = new GUIContent(T("WindowTitle"));
            window.minSize = new Vector2(280, 220);
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
            UnsubscribeFromProject();
        }

        // ================================================================
        // IToolContextReceiver
        // ================================================================

        public void SetContext(ToolContext ctx)
        {
            UnsubscribeFromProject();
            _toolContext = ctx;
            SubscribeToProject();
            Refresh();
        }

        // ================================================================
        // ProjectContext イベント購読
        // ================================================================

        private void SubscribeToProject()
        {
            if (Project != null)
                Project.OnModelsChanged += OnModelsChanged;
        }

        private void UnsubscribeFromProject()
        {
            if (Project != null)
                Project.OnModelsChanged -= OnModelsChanged;
        }

        private void OnModelsChanged()
        {
            Refresh();
        }

        // ================================================================
        // CreateGUI
        // ================================================================

        private void CreateGUI()
        {
            var root = rootVisualElement;

            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath)
                          ?? AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPathAssets);
            if (visualTree != null)
                visualTree.CloneTree(root);
            else
            {
                root.Add(new Label($"UXML not found: {UxmlPath}"));
                return;
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath)
                          ?? AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPathAssets);
            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            BindUI(root);
            Refresh();
        }

        // ================================================================
        // UI バインド
        // ================================================================

        private void BindUI(VisualElement root)
        {
            _warningLabel = root.Q<Label>("warning-label");
            _renameSection = root.Q<VisualElement>("rename-section");
            _renameDisplay = root.Q<VisualElement>("rename-display");
            _renameEdit = root.Q<VisualElement>("rename-edit");
            _renameCaptionLabel = root.Q<Label>("rename-caption");
            _renameEditCaptionLabel = root.Q<Label>("rename-edit-caption");
            _currentNameLabel = root.Q<Label>("current-name-label");
            _renameField = root.Q<TextField>("rename-field");
            _btnStartRename = root.Q<Button>("btn-start-rename");
            _btnConfirmRename = root.Q<Button>("btn-confirm-rename");
            _btnCancelRename = root.Q<Button>("btn-cancel-rename");
            _modelListContainer = root.Q<VisualElement>("model-list-container");

            // ボタンイベント
            _btnStartRename.clicked += OnStartRename;
            _btnConfirmRename.clicked += OnConfirmRename;
            _btnCancelRename.clicked += OnCancelRename;

            // EnterキーでRename確定
            _renameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    OnConfirmRename();
                else if (e.keyCode == KeyCode.Escape)
                    OnCancelRename();
            });
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return; // CreateGUI未実行

            var project = Project;

            // ローカライズ更新
            if (_renameCaptionLabel != null) _renameCaptionLabel.text = T("ModelName");
            if (_renameEditCaptionLabel != null) _renameEditCaptionLabel.text = T("ModelName");

            // 警告表示判定
            if (project == null)
            {
                _warningLabel.text = T("NoProject");
                _warningLabel.style.display = DisplayStyle.Flex;
                _renameSection.style.display = DisplayStyle.None;
                _modelListContainer.style.display = DisplayStyle.None;
                return;
            }

            if (project.ModelCount == 0)
            {
                _warningLabel.text = T("NoModels");
                _warningLabel.style.display = DisplayStyle.Flex;
                _renameSection.style.display = DisplayStyle.None;
                _modelListContainer.style.display = DisplayStyle.None;
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _renameSection.style.display = DisplayStyle.Flex;
            _modelListContainer.style.display = DisplayStyle.Flex;

            // モデル名エディタ更新
            RefreshRenameSection(project);

            // モデルリスト再構築
            RebuildModelList(project);
        }

        // ================================================================
        // モデル名編集セクション
        // ================================================================

        private void RefreshRenameSection(ProjectContext project)
        {
            int currentIndex = project.CurrentModelIndex;
            if (currentIndex < 0 || currentIndex >= project.ModelCount)
            {
                _renameSection.style.display = DisplayStyle.None;
                return;
            }

            var model = project.GetModel(currentIndex);
            if (model == null)
            {
                _renameSection.style.display = DisplayStyle.None;
                return;
            }

            _currentNameLabel.text = model.Name;

            // 非編集モード表示
            _renameDisplay.style.display = DisplayStyle.Flex;
            _renameEdit.style.display = DisplayStyle.None;
        }

        private void OnStartRename()
        {
            var project = Project;
            if (project == null) return;

            int currentIndex = project.CurrentModelIndex;
            if (currentIndex < 0 || currentIndex >= project.ModelCount) return;

            var model = project.GetModel(currentIndex);
            if (model == null) return;

            _renameField.value = model.Name;
            _renameDisplay.style.display = DisplayStyle.None;
            _renameEdit.style.display = DisplayStyle.Flex;

            // フォーカスを遅延設定
            _renameField.schedule.Execute(() => _renameField.Focus());
        }

        private void OnConfirmRename()
        {
            var project = Project;
            if (project == null) return;

            int currentIndex = project.CurrentModelIndex;
            if (currentIndex < 0 || currentIndex >= project.ModelCount) return;

            var model = project.GetModel(currentIndex);
            if (model == null) return;

            string newName = _renameField.value;
            if (!string.IsNullOrEmpty(newName) && newName != model.Name)
            {
                model.Name = newName;
                project.OnModelsChanged?.Invoke();
            }

            _renameDisplay.style.display = DisplayStyle.Flex;
            _renameEdit.style.display = DisplayStyle.None;
        }

        private void OnCancelRename()
        {
            _renameDisplay.style.display = DisplayStyle.Flex;
            _renameEdit.style.display = DisplayStyle.None;
        }

        // ================================================================
        // モデルリスト構築
        // ================================================================

        private void RebuildModelList(ProjectContext project)
        {
            _modelListContainer.Clear();

            for (int i = 0; i < project.ModelCount; i++)
            {
                var model = project.GetModel(i);
                if (model == null) continue;

                bool isCurrent = (i == project.CurrentModelIndex);
                var row = CreateModelRow(model, i, isCurrent);
                _modelListContainer.Add(row);
            }
        }

        private VisualElement CreateModelRow(ModelContext model, int index, bool isCurrent)
        {
            var row = new VisualElement();
            row.AddToClassList("ml-model-row");
            if (isCurrent)
                row.AddToClassList("ml-model-row--selected");

            // モデル名ラベル（クリックで選択）
            var nameLabel = new Label(model.Name);
            nameLabel.AddToClassList("ml-model-name");
            if (isCurrent)
                nameLabel.AddToClassList("ml-model-name--selected");

            int capturedIndex = index;
            nameLabel.RegisterCallback<ClickEvent>(e => SelectModel(capturedIndex));
            row.Add(nameLabel);

            // メッシュ数
            int meshCount = model.MeshContextCount;
            var countLabel = new Label($"{meshCount} {T("Meshes")}");
            countLabel.AddToClassList("ml-mesh-count");
            row.Add(countLabel);

            // 削除ボタン
            var deleteBtn = new Button(() => OnDeleteModel(capturedIndex, model.Name)) { text = "×" };
            deleteBtn.AddToClassList("ml-delete-btn");
            row.Add(deleteBtn);

            return row;
        }

        // ================================================================
        // 操作
        // ================================================================

        private void SelectModel(int index)
        {
            if (_toolContext?.SelectModel != null)
            {
                _toolContext.SelectModel(index);
            }
            else
            {
                Project?.SelectModel(index);
            }
            Refresh();
        }

        private void OnDeleteModel(int index, string modelName)
        {
            if (EditorUtility.DisplayDialog(
                T("Delete"),
                string.Format(T("ConfirmDelete"), modelName),
                "OK", "Cancel"))
            {
                Project?.RemoveModelAt(index);
                Refresh();
            }
        }
    }
}
