// PlayerPartsSelectionSetSubPanel.cs
// PartsSelectionSetPanelV2 の Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.EditorBridge;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerPartsSelectionSetSubPanel
    {
        public Func<ProjectContext>   GetView;
        public Action<PanelCommand> SendCommand;

        private Label       _warningLabel;
        private Label       _meshNameLabel;
        private Label       _currentSelLabel;
        private TextField   _setNameField;
        private ListView    _setListView;
        private Button      _btnLoad, _btnAdd, _btnSubtract, _btnDelete;
        private Label       _statusLabel;

        private int _selectedSetIndex = -1;
        private readonly List<string> _setNames = new List<string>();

        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;
        private ProjectContext GetProject() => GetView?.Invoke();
        private MeshContext FirstSelectedMeshContext
            => GetView?.Invoke()?.CurrentModel?.FirstSelectedDrawableMesh;

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("パーツ選択辞書"));

            _warningLabel = new Label();
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            _meshNameLabel = new Label();
            _meshNameLabel.style.fontSize    = 10;
            _meshNameLabel.style.marginBottom = 2;
            root.Add(_meshNameLabel);

            _currentSelLabel = new Label();
            _currentSelLabel.style.fontSize    = 10;
            _currentSelLabel.style.color       = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _currentSelLabel.style.marginBottom = 4;
            root.Add(_currentSelLabel);

            // 名前フィールド + 辞書化ボタン
            var saveRow = new VisualElement(); saveRow.style.flexDirection = FlexDirection.Row; saveRow.style.marginBottom = 4;
            _setNameField = new TextField(); _setNameField.style.flexGrow = 1;
            _setNameField.tooltip = "辞書エントリ名（空欄時は自動生成）";
            var btnSave = new Button(OnSave) { text = "辞書化" }; btnSave.style.width = 52;
            saveRow.Add(_setNameField); saveRow.Add(btnSave);
            root.Add(saveRow);

            // 辞書リスト
            _setListView = new ListView(_setNames, 22, MakeItem, BindItem);
            _setListView.selectionType   = SelectionType.Single;
            _setListView.style.minHeight = 60; _setListView.style.maxHeight = 150;
            _setListView.style.marginBottom = 4;
            _setListView.selectionChanged += OnSetSelectionChanged;
            root.Add(_setListView);

            // 操作ボタン行
            var opRow = new VisualElement(); opRow.style.flexDirection = FlexDirection.Row; opRow.style.marginBottom = 4;
            _btnLoad     = MkBtn("呼出し",  OnLoad);
            _btnAdd      = MkBtn("追加",    OnAdd);
            _btnSubtract = MkBtn("除外",    OnSubtract);
            _btnDelete   = MkBtn("削除",    OnDelete);
            foreach (var b in new[] { _btnLoad, _btnAdd, _btnSubtract, _btnDelete }) { b.style.flexGrow = 1; opRow.Add(b); }
            root.Add(opRow);

            // CSV行
            var csvRow = new VisualElement(); csvRow.style.flexDirection = FlexDirection.Row; csvRow.style.marginBottom = 4;
            var btnExport = MkBtn("CSV保存",  OnExport);
            var btnImport = MkBtn("CSV読込み", OnImport);
            btnExport.style.flexGrow = 1; btnImport.style.flexGrow = 1;
            csvRow.Add(btnExport); csvRow.Add(btnImport);
            root.Add(csvRow);

            _statusLabel = new Label(); _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new StyleColor(Color.white);
            root.Add(_statusLabel);

            UpdateButtonStates();
        }

        public void Refresh()
        {
            if (_warningLabel == null) return;
            var mc = FirstSelectedMeshContext;

            if (mc == null)
            {
                _warningLabel.text          = "メッシュが選択されていません";
                _warningLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                _warningLabel.style.display = DisplayStyle.None;
                _meshNameLabel.text = mc.Name ?? "(no name)";

                var parts = new List<string>();
                if (mc.SelectedVertices?.Count > 0) parts.Add($"V:{mc.SelectedVertices.Count}");
                if (mc.SelectedEdges?.Count   > 0) parts.Add($"E:{mc.SelectedEdges.Count}");
                if (mc.SelectedFaces?.Count   > 0) parts.Add($"F:{mc.SelectedFaces.Count}");
                _currentSelLabel.text = parts.Count > 0 ? string.Join("  ", parts) : "(選択なし)";
            }

            // 辞書リスト再構築
            _setNames.Clear();
            var sets = mc?.PartsSelectionSetList;
            if (sets != null) foreach (var s in sets) _setNames.Add(s.Name ?? "");
            _setListView.itemsSource = _setNames;
            _setListView.Rebuild();
            _selectedSetIndex = Mathf.Clamp(_selectedSetIndex, -1, _setNames.Count - 1);
            if (_selectedSetIndex >= 0) _setListView.SetSelection(_selectedSetIndex);
            UpdateButtonStates();
        }

        // ── ListView helpers ─────────────────────────────────────────────
        private VisualElement MakeItem()
        {
            var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row;
            var lbl = new Label(); lbl.style.flexGrow = 1; lbl.style.unityTextAlign = TextAnchor.MiddleLeft; lbl.style.paddingLeft = 4;
            var renameBtn = new Button { text = "✎" }; renameBtn.style.width = 22; renameBtn.style.height = 18;
            row.Add(lbl); row.Add(renameBtn);
            return row;
        }

        private void BindItem(VisualElement elem, int i)
        {
            if (i >= _setNames.Count) return;
            if (elem.Q<Label>() is Label l) l.text = $"[{i}] {_setNames[i]}";
            if (elem.Q<Button>() is Button b) { int ci = i; b.clicked += () => OnRenameAt(ci); }
        }

        // ── Operations ───────────────────────────────────────────────────
        private void OnSetSelectionChanged(IEnumerable<object> _)
        {
            _selectedSetIndex = _setListView.selectedIndex;
            UpdateButtonStates();
        }

        private void OnSave()
        {
            var mv = FirstSelectedMeshContext; if (mv == null) return;
            bool hasSel = (mv.SelectedVertices?.Count > 0) || (mv.SelectedEdges?.Count > 0)
                       || (mv.SelectedFaces?.Count > 0);
            if (!hasSel) { SetStatus("選択なし"); return; }
            SendCmd(new SavePartsSetCommand(ModelIndex, _setNameField?.value?.Trim() ?? ""));
            _setNameField?.SetValueWithoutNotify(""); SetStatus("辞書化しました");
        }

        private void OnLoad()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new LoadPartsSetCommand(ModelIndex, _selectedSetIndex)); SetStatus("選択を適用しました");
        }

        private void OnAdd()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new AddPartsSetCommand(ModelIndex, _selectedSetIndex)); SetStatus("選択を追加しました");
        }

        private void OnSubtract()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new SubtractPartsSetCommand(ModelIndex, _selectedSetIndex)); SetStatus("選択を除外しました");
        }

        private void OnDelete()
        {
            if (_selectedSetIndex < 0) return;
            var sets = FirstSelectedMeshContext?.PartsSelectionSetList;
            string name = (sets != null && _selectedSetIndex < sets.Count) ? sets[_selectedSetIndex].Name : "?";
            bool ok = PLEditorBridge.I.DisplayDialogYesNo("削除確認", $"「{name}」を削除しますか？", "削除", "キャンセル");
            if (!ok) return;
            SendCmd(new DeletePartsSetCommand(ModelIndex, _selectedSetIndex));
            _selectedSetIndex = -1; SetStatus($"削除: {name}");
        }

        private void OnRenameAt(int index)
        {
            string newName = _setNameField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(newName)) { SetStatus("名前フィールドに新しい名前を入力してください"); return; }
            SendCmd(new RenamePartsSetCommand(ModelIndex, index, newName));
            _setNameField?.SetValueWithoutNotify(""); SetStatus($"名前変更 → {newName}");
        }

        private void OnExport() => SendCmd(new ExportPartsSetsCsvCommand(ModelIndex));
        private void OnImport() => SendCmd(new ImportPartsSetCsvCommand(ModelIndex));

        private void UpdateButtonStates()
        {
            bool hasSel = _selectedSetIndex >= 0;
            if (_btnLoad     != null) _btnLoad.SetEnabled(hasSel);
            if (_btnAdd      != null) _btnAdd.SetEnabled(hasSel);
            if (_btnSubtract != null) _btnSubtract.SetEnabled(hasSel);
            if (_btnDelete   != null) _btnDelete.SetEnabled(hasSel);
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static Button MkBtn(string t, Action a) { var b = new Button(a) { text = t }; b.style.height = 22; return b; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
