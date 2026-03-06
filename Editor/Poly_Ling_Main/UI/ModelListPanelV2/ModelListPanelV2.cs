// ModelListPanelV2.cs
// モデルリスト管理パネル（新形式）
// MeshListPanelV2 と同じ PanelContext / IProjectView / PanelCommand 構成
// UXML/USS 不要のコード完結型

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.UI
{
    public class ModelListPanelV2 : EditorWindow
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _ctx;
        private bool _isReceiving;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _modelListContainer;
        private Label _warningLabel;
        // 名前変更セクション
        private VisualElement _renameSection;
        private VisualElement _renameDisplay;
        private VisualElement _renameEdit;
        private Label _currentNameLabel;
        private TextField _renameField;
        private Button _btnStartRename;
        private Button _btnConfirmRename;
        private Button _btnCancelRename;
        private Label _statusLabel;

        // ================================================================
        // ウィンドウ
        // ================================================================

        [MenuItem("Tools/Poly_Ling/Model List V2")]
        public static void ShowWindow()
        {
            var w = GetWindow<ModelListPanelV2>();
            w.titleContent = new GUIContent("モデルリスト V2");
            w.minSize = new Vector2(280, 240);
        }

        public static ModelListPanelV2 Open(PanelContext ctx)
        {
            var w = GetWindow<ModelListPanelV2>();
            w.titleContent = new GUIContent("モデルリスト V2");
            w.minSize = new Vector2(280, 240);
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
                if (_ctx.CurrentView != null) Refresh(_ctx.CurrentView);
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
            if (_ctx?.CurrentView != null) Refresh(_ctx.CurrentView);
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

            // 警告ラベル
            _warningLabel = new Label();
            _warningLabel.style.color = new Color(0.8f, 0.8f, 0.3f);
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.display = DisplayStyle.None;
            root.Add(_warningLabel);

            // ---- 名前変更セクション ----
            _renameSection = new VisualElement();
            _renameSection.style.marginBottom = 6;

            // 表示行（名前 + 編集ボタン）
            _renameDisplay = new VisualElement();
            _renameDisplay.style.flexDirection = FlexDirection.Row;
            _renameDisplay.style.height = 22;
            _renameDisplay.style.alignItems = Align.Center;

            _currentNameLabel = new Label();
            _currentNameLabel.style.flexGrow = 1;
            _currentNameLabel.style.overflow = Overflow.Hidden;
            _currentNameLabel.style.textOverflow = TextOverflow.Ellipsis;
            _renameDisplay.Add(_currentNameLabel);

            _btnStartRename = new Button(OnStartRename) { text = "✎ 名前変更" };
            _btnStartRename.style.width = 80;
            _btnStartRename.style.minHeight = 20;
            _renameDisplay.Add(_btnStartRename);
            _renameSection.Add(_renameDisplay);

            // 編集行（フィールド + 確定/キャンセル）
            _renameEdit = new VisualElement();
            _renameEdit.style.flexDirection = FlexDirection.Row;
            _renameEdit.style.height = 22;
            _renameEdit.style.display = DisplayStyle.None;

            _renameField = new TextField();
            _renameField.style.flexGrow = 1;
            _renameField.style.minHeight = 20;
            _renameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    OnConfirmRename();
                else if (e.keyCode == KeyCode.Escape)
                    OnCancelRename();
            });
            _renameEdit.Add(_renameField);

            _btnConfirmRename = new Button(OnConfirmRename) { text = "✓" };
            _btnConfirmRename.style.width = 28; _btnConfirmRename.style.minHeight = 20;
            _renameEdit.Add(_btnConfirmRename);

            _btnCancelRename = new Button(OnCancelRename) { text = "✕" };
            _btnCancelRename.style.width = 28; _btnCancelRename.style.minHeight = 20;
            _renameEdit.Add(_btnCancelRename);
            _renameSection.Add(_renameEdit);

            root.Add(_renameSection);

            // ---- モデルリスト ----
            _modelListContainer = new VisualElement();
            _modelListContainer.style.flexGrow = 1;
            root.Add(_modelListContainer);

            // ステータス
            _statusLabel = new Label("");
            _statusLabel.style.marginTop = 4;
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            root.Add(_statusLabel);
        }

        // ================================================================
        // ViewChanged
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (_isReceiving) return;
            _isReceiving = true;
            try { Refresh(view); }
            finally { EditorApplication.delayCall += () => _isReceiving = false; }
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void Refresh(IProjectView view)
        {
            if (_warningLabel == null) return; // CreateGUI 未実行

            if (view == null || view.ModelCount == 0)
            {
                _warningLabel.text = view == null ? "プロジェクトがありません" : "モデルがありません";
                _warningLabel.style.display = DisplayStyle.Flex;
                _renameSection?.SetEnabled(false);
                _modelListContainer?.SetEnabled(false);
                _modelListContainer?.Clear();
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _renameSection?.SetEnabled(true);
            _modelListContainer?.SetEnabled(true);

            // 名前変更セクション更新
            var cur = view.GetModelView(view.CurrentModelIndex);
            _currentNameLabel.text = cur?.Name ?? "(不明)";
            // 編集中でなければ表示モードに戻す
            if (_renameEdit.style.display == DisplayStyle.None || _renameEdit == null)
            {
                _renameDisplay.style.display = DisplayStyle.Flex;
                _renameEdit.style.display = DisplayStyle.None;
            }

            // モデルリスト再構築
            RebuildModelList(view);
        }

        private void RebuildModelList(IProjectView view)
        {
            _modelListContainer.Clear();
            int count = view.ModelCount;
            for (int i = 0; i < count; i++)
            {
                var mv = view.GetModelView(i);
                if (mv == null) continue;
                bool isCurrent = (i == view.CurrentModelIndex);
                _modelListContainer.Add(CreateModelRow(mv, i, isCurrent));
            }
        }

        private VisualElement CreateModelRow(IModelView mv, int index, bool isCurrent)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = 22;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;
            row.style.paddingLeft = 4; row.style.paddingRight = 4;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new Color(0.25f, 0.25f, 0.25f);
            if (isCurrent)
            {
                row.style.backgroundColor = new Color(0.24f, 0.37f, 0.58f, 0.6f);
            }

            // 名前ラベル（クリックで選択）
            var nameLabel = new Label(mv.Name);
            nameLabel.style.flexGrow = 1;
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            if (isCurrent)
                nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            int capturedIndex = index;
            nameLabel.RegisterCallback<ClickEvent>(_ => OnSelectModel(capturedIndex));
            row.Add(nameLabel);

            // メッシュ数
            var countLabel = new Label($"{mv.TotalMeshCount} mesh");
            countLabel.style.width = 70;
            countLabel.style.fontSize = 10;
            countLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            countLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            countLabel.style.marginRight = 4;
            row.Add(countLabel);

            // 削除ボタン
            var delBtn = new Button(() => OnDeleteModel(capturedIndex, mv.Name)) { text = "×" };
            delBtn.style.width = 22; delBtn.style.height = 18;
            delBtn.style.paddingLeft = 0; delBtn.style.paddingRight = 0;
            delBtn.style.paddingTop = 0; delBtn.style.paddingBottom = 0;
            delBtn.style.fontSize = 11;
            delBtn.style.color = new Color(0.9f, 0.4f, 0.4f);
            delBtn.style.borderTopWidth = 0; delBtn.style.borderBottomWidth = 0;
            delBtn.style.borderLeftWidth = 0; delBtn.style.borderRightWidth = 0;
            delBtn.style.backgroundColor = new Color(0, 0, 0, 0);
            row.Add(delBtn);

            return row;
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnSelectModel(int index)
        {
            SendCmd(new SwitchModelCommand(index));
        }

        private void OnStartRename()
        {
            var cur = _ctx?.CurrentView?.CurrentModel;
            if (cur == null) return;
            _renameField.SetValueWithoutNotify(cur.Name);
            _renameDisplay.style.display = DisplayStyle.None;
            _renameEdit.style.display = DisplayStyle.Flex;
            _renameField.schedule.Execute(() => _renameField.Focus());
        }

        private void OnConfirmRename()
        {
            var view = _ctx?.CurrentView;
            if (view == null) return;
            string newName = _renameField.value?.Trim();
            if (!string.IsNullOrEmpty(newName) && newName != view.CurrentModel?.Name)
            {
                SendCmd(new RenameModelCommand(view.CurrentModelIndex, newName));
                SetStatus($"名前変更: {newName}");
            }
            _renameDisplay.style.display = DisplayStyle.Flex;
            _renameEdit.style.display = DisplayStyle.None;
        }

        private void OnCancelRename()
        {
            _renameDisplay.style.display = DisplayStyle.Flex;
            _renameEdit.style.display = DisplayStyle.None;
        }

        private void OnDeleteModel(int index, string name)
        {
            if (!EditorUtility.DisplayDialog("削除確認", $"モデル「{name}」を削除しますか？", "削除", "キャンセル"))
                return;
            SendCmd(new DeleteModelCommand(index));
            SetStatus($"削除: {name}");
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void SendCmd(PanelCommand c) => _ctx?.SendCommand(c);
        private void SetStatus(string msg) { if (_statusLabel != null) _statusLabel.text = msg; }
    }
}
