// PartsSelectionSetPanelV2.cs
// パーツ選択辞書パネル（新形式）
// PanelContext / IProjectView / PanelCommand 構成
// 頂点・辺・面・線分の選択保存/復元

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.View;
using Poly_Ling.Selection;

namespace Poly_Ling.UI
{
    public class PartsSelectionSetPanelV2 : EditorWindow
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _ctx;
        private bool _isReceiving;

        // ================================================================
        // 状態
        // ================================================================

        private int _selectedSetIndex = -1;

        // ================================================================
        // UI 要素
        // ================================================================

        private Label _warningLabel;
        private Label _meshNameLabel;
        private Label _currentSelLabel;
        private TextField _setNameField;
        private Button _btnSave;
        private ListView _setListView;
        private Button _btnLoad, _btnAdd, _btnSubtract, _btnDelete;
        private Button _btnExport, _btnImport;
        private Label _statusLabel;

        // ================================================================
        // プロパティ
        // ================================================================

        private IModelView CurrentModel => _ctx?.CurrentView?.CurrentModel;
        private int ModelIndex => _ctx?.CurrentView?.CurrentModelIndex ?? 0;

        /// <summary>選択中の最初の Drawable メッシュビュー</summary>
        private IMeshView FirstSelectedMeshView
        {
            get
            {
                var model = CurrentModel;
                if (model == null) return null;
                var indices = model.SelectedDrawableIndices;
                if (indices == null || indices.Length == 0) return null;
                int first = indices[0];
                return model.DrawableList?.FirstOrDefault(mv => mv.MasterIndex == first);
            }
        }

        // ================================================================
        // ウィンドウ
        // ================================================================

        //[MenuItem("Tools/Poly_Ling/Parts Selection Dictionary V2")]
        public static void ShowWindow()
        {
            var w = GetWindow<PartsSelectionSetPanelV2>();
            w.titleContent = new GUIContent("パーツ選択辞書 V2");
            w.minSize = new Vector2(300, 320);
        }

        public static PartsSelectionSetPanelV2 Open(PanelContext ctx)
        {
            var w = GetWindow<PartsSelectionSetPanelV2>();
            w.titleContent = new GUIContent("パーツ選択辞書 V2");
            w.minSize = new Vector2(300, 320);
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
            if (_ctx != null) { _ctx.OnViewChanged -= OnViewChanged; _ctx.OnViewChanged += OnViewChanged; }
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
            root.style.paddingLeft = 4; root.style.paddingRight = 4;
            root.style.paddingTop = 4;  root.style.paddingBottom = 4;

            // 警告
            _warningLabel = new Label();
            _warningLabel.style.color = new Color(0.8f, 0.8f, 0.3f);
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.display = DisplayStyle.None;
            root.Add(_warningLabel);

            // 対象メッシュ名
            _meshNameLabel = new Label();
            _meshNameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _meshNameLabel.style.marginBottom = 2;
            root.Add(_meshNameLabel);

            // 現在の選択状態
            var selBox = new VisualElement();
            selBox.style.flexDirection = FlexDirection.Row;
            selBox.style.marginBottom = 4;
            selBox.style.paddingLeft = 4; selBox.style.paddingRight = 4;
            selBox.style.paddingTop = 2;  selBox.style.paddingBottom = 2;
            selBox.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 0.5f);

            var selTitle = new Label("選択中: ");
            selTitle.style.color = new Color(0.7f, 0.7f, 0.7f);
            selBox.Add(selTitle);

            _currentSelLabel = new Label("なし");
            _currentSelLabel.style.flexGrow = 1;
            selBox.Add(_currentSelLabel);
            root.Add(selBox);

            // 保存行
            var saveRow = new VisualElement();
            saveRow.style.flexDirection = FlexDirection.Row;
            saveRow.style.marginBottom = 4; saveRow.style.height = 22;
            _setNameField = new TextField();
            _setNameField.style.flexGrow = 1; _setNameField.style.minHeight = 20;
            _setNameField.tooltip = "辞書エントリ名（空欄時は自動生成）";
            saveRow.Add(_setNameField);
            _btnSave = new Button(OnSave) { text = "辞書化" };
            _btnSave.style.width = 52; _btnSave.style.minHeight = 20;
            saveRow.Add(_btnSave);
            root.Add(saveRow);

            // セットリスト
            _setListView = new ListView();
            _setListView.fixedItemHeight = 22f;
            _setListView.selectionType = SelectionType.Single;
            _setListView.makeItem = MakeSetItem;
            _setListView.bindItem = BindSetItem;
            _setListView.selectionChanged += OnSetSelectionChanged;
            _setListView.style.flexGrow = 1;
            _setListView.style.minHeight = 60;
            _setListView.style.maxHeight = 180;
            root.Add(_setListView);

            // 操作ボタン行
            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginTop = 2; opRow.style.height = 24;
            _btnLoad     = MkBtn("呼出し", OnLoad);
            _btnAdd      = MkBtn("追加",   OnAdd);
            _btnSubtract = MkBtn("除外",   OnSubtract);
            _btnDelete   = MkBtn("削除",   OnDelete);
            opRow.Add(_btnLoad); opRow.Add(_btnAdd); opRow.Add(_btnSubtract); opRow.Add(_btnDelete);
            root.Add(opRow);

            // CSV行
            var csvRow = new VisualElement();
            csvRow.style.flexDirection = FlexDirection.Row;
            csvRow.style.marginTop = 4; csvRow.style.height = 24;
            _btnExport = MkBtn("CSV保存", OnExport);
            _btnImport = MkBtn("CSV読込み", OnImport);
            csvRow.Add(_btnExport); csvRow.Add(_btnImport);
            root.Add(csvRow);

            // ステータス
            _statusLabel = new Label("");
            _statusLabel.style.marginTop = 4; _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            root.Add(_statusLabel);
        }

        // ================================================================
        // ViewChanged / Refresh
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (_isReceiving) return;
            _isReceiving = true;
            try { Refresh(view); }
            finally { EditorApplication.delayCall += () => _isReceiving = false; }
        }

        private void Refresh(IProjectView view)
        {
            if (_warningLabel == null) return;

            var mv = FirstSelectedMeshView;
            if (mv == null)
            {
                _warningLabel.text = "メッシュが選択されていません";
                _warningLabel.style.display = DisplayStyle.Flex;
                _meshNameLabel.text = "";
                _currentSelLabel.text = "なし";
                _setListView.itemsSource = null;
                _setListView.Rebuild();
                UpdateButtons(mv);
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _meshNameLabel.text = $"📐 {mv.Name}";

            // 現在の選択状態
            var parts = new List<string>();
            if (mv.SelectedVertexCount > 0) parts.Add($"V:{mv.SelectedVertexCount}");
            if (mv.SelectedEdgeCount   > 0) parts.Add($"E:{mv.SelectedEdgeCount}");
            if (mv.SelectedFaceCount   > 0) parts.Add($"F:{mv.SelectedFaceCount}");
            if (mv.SelectedLineCount   > 0) parts.Add($"L:{mv.SelectedLineCount}");
            _currentSelLabel.text = parts.Count > 0 ? string.Join("  ", parts) : "なし";

            // セットリスト更新
            var sets = mv.PartsSelectionSets;
            _setListView.itemsSource = sets as System.Collections.IList ?? sets?.ToList<IPartsSetView>();
            _setListView.Rebuild();
            if (_selectedSetIndex >= (sets?.Count ?? 0)) _selectedSetIndex = -1;

            // ボタン有効状態
            UpdateButtons(mv);
        }

        // ================================================================
        // ListView
        // ================================================================

        private VisualElement MakeSetItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = 22; row.style.alignItems = Align.Center;
            row.style.paddingLeft = 4;

            var iconLbl = new Label { name = "icon" };
            iconLbl.style.width = 18;
            iconLbl.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(iconLbl);

            var nameLbl = new Label { name = "name" };
            nameLbl.style.flexGrow = 1;
            nameLbl.style.overflow = Overflow.Hidden;
            nameLbl.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(nameLbl);

            var sumLbl = new Label { name = "summary" };
            sumLbl.style.width = 80;
            sumLbl.style.fontSize = 10;
            sumLbl.style.unityTextAlign = TextAnchor.MiddleRight;
            sumLbl.style.color = new Color(0.6f, 0.6f, 0.6f);
            row.Add(sumLbl);

            // 名前変更ボタン
            var renameBtn = new Button { name = "rename", text = "✎" };
            renameBtn.style.width = 22; renameBtn.style.height = 18;
            renameBtn.style.paddingLeft = 0; renameBtn.style.paddingRight = 0;
            renameBtn.style.paddingTop = 0; renameBtn.style.paddingBottom = 0;
            renameBtn.style.fontSize = 11;
            renameBtn.style.backgroundColor = new Color(0, 0, 0, 0);
            renameBtn.style.borderTopWidth = 0; renameBtn.style.borderBottomWidth = 0;
            renameBtn.style.borderLeftWidth = 0; renameBtn.style.borderRightWidth = 0;
            row.Add(renameBtn);

            return row;
        }

        private void BindSetItem(VisualElement el, int index)
        {
            var sets = (_setListView.itemsSource as IList<IPartsSetView>)
                    ?? (_setListView.itemsSource as System.Collections.IList)?.Cast<IPartsSetView>().ToList();
            if (sets == null || index < 0 || index >= sets.Count) return;
            var s = sets[index];

            var il = el.Q<Label>("icon");     if (il != null) il.text = ModeIcon(s.Mode);
            var nl = el.Q<Label>("name");     if (nl != null) nl.text = s.Name;
            var sl = el.Q<Label>("summary");  if (sl != null) sl.text = s.Summary;

            int captured = index;
            var rb = el.Q<Button>("rename");
            if (rb != null)
            {
                rb.clicked -= rb.userData as Action;
                Action act = () => OnRenameAt(captured);
                rb.userData = act;
                rb.clicked += act;
            }
        }

        private void OnSetSelectionChanged(IEnumerable<object> sel)
        {
            var list = sel?.ToList();
            _selectedSetIndex = (list != null && list.Count > 0)
                ? (_setListView.selectedIndex)
                : -1;
            UpdateButtons(FirstSelectedMeshView);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnSave()
        {
            var mv = FirstSelectedMeshView;
            if (mv == null) return;
            bool hasSel = mv.SelectedVertexCount > 0 || mv.SelectedEdgeCount > 0
                       || mv.SelectedFaceCount > 0   || mv.SelectedLineCount > 0;
            if (!hasSel) { SetStatus("選択なし"); return; }
            SendCmd(new SavePartsSetCommand(ModelIndex, _setNameField?.value?.Trim() ?? ""));
            _setNameField?.SetValueWithoutNotify("");
            SetStatus("辞書化しました");
        }

        private void OnLoad()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new LoadPartsSetCommand(ModelIndex, _selectedSetIndex));
            SetStatus("選択を適用しました");
        }

        private void OnAdd()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new AddPartsSetCommand(ModelIndex, _selectedSetIndex));
            SetStatus("選択を追加しました");
        }

        private void OnSubtract()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new SubtractPartsSetCommand(ModelIndex, _selectedSetIndex));
            SetStatus("選択を除外しました");
        }

        private void OnDelete()
        {
            if (_selectedSetIndex < 0) return;
            var sets = FirstSelectedMeshView?.PartsSelectionSets;
            string name = (sets != null && _selectedSetIndex < sets.Count)
                ? sets[_selectedSetIndex].Name : "?";
            if (!EditorUtility.DisplayDialog("削除確認", $"「{name}」を削除しますか？", "削除", "キャンセル")) return;
            SendCmd(new DeletePartsSetCommand(ModelIndex, _selectedSetIndex));
            _selectedSetIndex = -1;
            SetStatus($"削除: {name}");
        }

        private void OnRenameAt(int index)
        {
            var sets = FirstSelectedMeshView?.PartsSelectionSets;
            if (sets == null || index < 0 || index >= sets.Count) return;
            string current = sets[index].Name;
            string newName = _setNameField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(newName)) { SetStatus("名前フィールドに新しい名前を入力してください"); return; }
            SendCmd(new RenamePartsSetCommand(ModelIndex, index, newName));
            _setNameField?.SetValueWithoutNotify("");
            SetStatus($"名前変更: {current} → {newName}");
        }

        private void OnExport()
        {
            SendCmd(new ExportPartsSetsCsvCommand(ModelIndex));
        }

        private void OnImport()
        {
            SendCmd(new ImportPartsSetCsvCommand(ModelIndex));
        }

        // ================================================================
        // ボタン有効状態
        // ================================================================

        private void UpdateButtons(IMeshView mv)
        {
            bool hasSets = (mv?.PartsSelectionSetCount ?? 0) > 0;
            bool hasSel = _selectedSetIndex >= 0;
            _btnLoad?.SetEnabled(hasSel);
            _btnAdd?.SetEnabled(hasSel);
            _btnSubtract?.SetEnabled(hasSel);
            _btnDelete?.SetEnabled(hasSel);
            _btnExport?.SetEnabled(hasSets);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static string ModeIcon(MeshSelectMode mode) => mode switch
        {
            MeshSelectMode.Vertex => "●",
            MeshSelectMode.Edge   => "━",
            MeshSelectMode.Face   => "■",
            MeshSelectMode.Line   => "╱",
            _                     => "○"
        };

        private void SendCmd(PanelCommand c) => _ctx?.SendCommand(c);
        private void SetStatus(string msg) { if (_statusLabel != null) _statusLabel.text = msg; }

        private static Button MkBtn(string label, Action click)
        {
            var b = new Button(click) { text = label };
            b.style.flexGrow = 1; b.style.minHeight = 22;
            return b;
        }
    }
}
