// ModelListSubPanel.cs
// プレイヤービュー右ペイン用モデルリスト UI。
// エディタ版 ModelListPanelV2 と同じ PanelContext / IProjectView / PanelCommand 構成。
// Build(parent) → SetContext(ctx) パターン。
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class ModelListSubPanel
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext  _ctx;
        private bool          _isReceiving;

        // ================================================================
        // UI要素
        // ================================================================

        private VisualElement _root;
        private Label         _warningLabel;
        private VisualElement _renameSection;
        private VisualElement _renameDisplay;
        private VisualElement _renameEdit;
        private Label         _currentNameLabel;
        private TextField     _renameField;
        private Button        _btnStartRename, _btnConfirmRename, _btnCancelRename;
        private VisualElement _modelListContainer;
        private Label         _statusLabel;

        // ================================================================
        // Build / SetContext
        // ================================================================

        public void Build(VisualElement parent)
        {
            parent.Clear();
            _root = parent;
            BuildUI(parent);
        }

        public void SetContext(PanelContext ctx)
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
        // UI構築
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft   = 4; root.style.paddingRight  = 4;
            root.style.paddingTop    = 4; root.style.paddingBottom = 4;

            // セクションヘッダー
            var header = new Label(ModelListTexts.T("Title"));
            header.style.color        = new StyleColor(new Color(0.7f, 0.85f, 1f));
            header.style.fontSize     = 10;
            header.style.marginBottom = 4;
            root.Add(header);

            // 警告ラベル
            _warningLabel = new Label();
            _warningLabel.style.color        = new StyleColor(new Color(0.8f, 0.8f, 0.3f));
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            _warningLabel.style.display      = DisplayStyle.None;
            root.Add(_warningLabel);

            // ---- 名前変更セクション ----
            _renameSection = new VisualElement();
            _renameSection.style.marginBottom = 6;

            // 表示行（名前 + 編集ボタン）
            _renameDisplay = new VisualElement();
            _renameDisplay.style.flexDirection = FlexDirection.Row;
            _renameDisplay.style.height        = 22;
            _renameDisplay.style.alignItems    = Align.Center;

            _currentNameLabel = new Label();
            _currentNameLabel.style.flexGrow     = 1;
            _currentNameLabel.style.overflow     = Overflow.Hidden;
            _currentNameLabel.style.textOverflow = TextOverflow.Ellipsis;
            _currentNameLabel.style.color        = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            _renameDisplay.Add(_currentNameLabel);

            _btnStartRename = new Button(OnStartRename) { text = ModelListTexts.T("Rename") };
            _btnStartRename.style.width   = 78;
            _btnStartRename.style.height  = 20;
            _renameDisplay.Add(_btnStartRename);
            _renameSection.Add(_renameDisplay);

            // 編集行（フィールド + 確定/キャンセル）
            _renameEdit = new VisualElement();
            _renameEdit.style.flexDirection = FlexDirection.Row;
            _renameEdit.style.height        = 22;
            _renameEdit.style.display       = DisplayStyle.None;

            _renameField = new TextField();
            _renameField.style.flexGrow = 1;
            _renameField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                    OnConfirmRename();
                else if (e.keyCode == KeyCode.Escape)
                    OnCancelRename();
            });
            _renameEdit.Add(_renameField);

            _btnConfirmRename = new Button(OnConfirmRename) { text = ModelListTexts.T("Confirm") };
            _btnConfirmRename.style.width = 28; _btnConfirmRename.style.height = 20;
            _renameEdit.Add(_btnConfirmRename);

            _btnCancelRename = new Button(OnCancelRename) { text = ModelListTexts.T("Cancel") };
            _btnCancelRename.style.width = 28; _btnCancelRename.style.height = 20;
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
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
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
            finally
            {
                _root?.schedule.Execute(() => _isReceiving = false);
            }
        }

        // ================================================================
        // Refresh
        // ================================================================

        private void Refresh(IProjectView view)
        {
            if (_warningLabel == null) return;

            if (view == null || view.ModelCount == 0)
            {
                _warningLabel.text = view == null
                    ? ModelListTexts.T("NoProject")
                    : ModelListTexts.T("NoModel");
                _warningLabel.style.display = DisplayStyle.Flex;
                _renameSection?.SetEnabled(false);
                _modelListContainer?.SetEnabled(false);
                _modelListContainer?.Clear();
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _renameSection?.SetEnabled(true);
            _modelListContainer?.SetEnabled(true);

            var cur = view.GetModelView(view.CurrentModelIndex);
            _currentNameLabel.text = cur?.Name ?? "(不明)";

            // 編集中でなければ表示モードのまま
            if (_renameEdit.style.display == DisplayStyle.None)
            {
                _renameDisplay.style.display = DisplayStyle.Flex;
                _renameEdit.style.display    = DisplayStyle.None;
            }

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
                _modelListContainer.Add(CreateModelRow(mv, i, i == view.CurrentModelIndex));
            }
        }

        private VisualElement CreateModelRow(IModelView mv, int index, bool isCurrent)
        {
            var row = new VisualElement();
            row.style.flexDirection  = FlexDirection.Row;
            row.style.height         = 22;
            row.style.alignItems     = Align.Center;
            row.style.marginBottom   = 2;
            row.style.paddingLeft    = 4; row.style.paddingRight = 4;
            row.style.borderBottomWidth = 1;
            row.style.borderBottomColor = new StyleColor(new Color(0.25f, 0.25f, 0.25f));
            if (isCurrent)
                row.style.backgroundColor = new StyleColor(new Color(0.24f, 0.37f, 0.58f, 0.6f));

            // 名前ラベル
            var nameLabel = new Label(mv.Name);
            nameLabel.style.flexGrow     = 1;
            nameLabel.style.overflow     = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            nameLabel.style.color        = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            if (isCurrent) nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            int capturedIndex = index;
            nameLabel.RegisterCallback<ClickEvent>(_ => SendCmd(new SwitchModelCommand(capturedIndex)));
            row.Add(nameLabel);

            // メッシュ数
            var countLabel = new Label(ModelListTexts.T("MeshCount", mv.TotalMeshCount));
            countLabel.style.width         = 55;
            countLabel.style.fontSize      = 10;
            countLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            countLabel.style.color         = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            countLabel.style.marginRight   = 4;
            row.Add(countLabel);

            // 削除ボタン
            var delBtn = new Button(() => OnDeleteModel(capturedIndex))
                { text = ModelListTexts.T("Delete") };
            delBtn.style.width  = 22; delBtn.style.height = 18;
            delBtn.style.paddingLeft = 0; delBtn.style.paddingRight  = 0;
            delBtn.style.paddingTop  = 0; delBtn.style.paddingBottom = 0;
            delBtn.style.fontSize     = 11;
            delBtn.style.color        = new StyleColor(new Color(0.9f, 0.4f, 0.4f));
            delBtn.style.borderTopWidth = 0; delBtn.style.borderBottomWidth = 0;
            delBtn.style.borderLeftWidth = 0; delBtn.style.borderRightWidth = 0;
            delBtn.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
            row.Add(delBtn);

            return row;
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnStartRename()
        {
            var cur = _ctx?.CurrentView?.CurrentModel;
            if (cur == null) return;
            _renameField.SetValueWithoutNotify(cur.Name);
            _renameDisplay.style.display = DisplayStyle.None;
            _renameEdit.style.display    = DisplayStyle.Flex;
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
            _renameEdit.style.display    = DisplayStyle.None;
        }

        private void OnCancelRename()
        {
            _renameDisplay.style.display = DisplayStyle.Flex;
            _renameEdit.style.display    = DisplayStyle.None;
        }

        private void OnDeleteModel(int index)
        {
            SendCmd(new DeleteModelCommand(index));
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void SendCmd(PanelCommand c) => _ctx?.SendCommand(c);
        private void SetStatus(string msg)   { if (_statusLabel != null) _statusLabel.text = msg; }
    }
}
