// PlayerNormalExcludeSetSubPanel.cs
// 法線再計算 除外辞書のサブパネル。
// 実体は MeshObject.NormalRecalcExcludeList（パーツ選択辞書と同じ PartsSelectionSet 構造）。
// Runtime/Poly_Ling_Player/View/SubPanels/Model/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.EditorBridge;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    public class PlayerNormalExcludeSetSubPanel
    {
        public Func<ProjectContext> GetView;
        public Action<PanelCommand> SendCommand;

        private Label     _warningLabel;
        private Label     _meshNameLabel;
        private Label     _currentSelLabel;
        private TextField _setNameField;
        private ListView  _setListView;
        private Button    _btnLoad, _btnDelete;
        private Label     _statusLabel;

        private int _selectedSetIndex = -1;
        private readonly List<string> _setNames = new List<string>();

        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        private MeshContext ActiveMeshContext
            => GetView?.Invoke()?.CurrentModel?.ActiveMeshContext;

        private List<PartsSelectionSet> ExcludeList
            => ActiveMeshContext?.MeshObject?.NormalRecalcExcludeList;

        private void SendCmd(PanelCommand cmd) => SendCommand?.Invoke(cmd);

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("法線再計算 除外辞書"));

            var help = new HelpBox(
                "辞書に登録した頂点／面は、法線の自動再計算の直前に法線を退避し、"
                + "計算後に元の法線へ戻す。辺は両端頂点として扱う。",
                HelpBoxMessageType.Info);
            help.style.marginBottom = 4;
            root.Add(help);

            _warningLabel = new Label();
            _warningLabel.style.color         = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.display       = DisplayStyle.None;
            _warningLabel.style.marginBottom  = 4;
            root.Add(_warningLabel);

            _meshNameLabel = new Label();
            _meshNameLabel.style.fontSize     = 10;
            _meshNameLabel.style.marginBottom = 2;
            root.Add(_meshNameLabel);

            _currentSelLabel = new Label();
            _currentSelLabel.style.fontSize     = 10;
            _currentSelLabel.style.marginBottom = 4;
            root.Add(_currentSelLabel);

            // 名前フィールド + 登録ボタン
            var saveRow = new VisualElement();
            saveRow.style.flexDirection = FlexDirection.Row;
            saveRow.style.marginBottom  = 4;
            _setNameField = new TextField();
            _setNameField.style.flexGrow = 1;
            _setNameField.tooltip = "辞書エントリ名（空欄時は自動生成）";
            var btnSave = new Button(OnSave) { text = "除外登録" };
            btnSave.style.width = 64;
            saveRow.Add(_setNameField);
            saveRow.Add(btnSave);
            root.Add(saveRow);

            // 辞書リスト
            _setListView = new ListView(_setNames, 22, MakeItem, BindItem);
            _setListView.selectionType    = SelectionType.Single;
            _setListView.style.minHeight  = 60;
            _setListView.style.maxHeight  = 150;
            _setListView.style.marginBottom = 4;
            _setListView.selectionChanged += OnSetSelectionChanged;
            root.Add(_setListView);

            // 操作ボタン行
            var opRow = new VisualElement();
            opRow.style.flexDirection = FlexDirection.Row;
            opRow.style.marginBottom  = 4;
            _btnLoad   = MkBtn("呼出し", OnLoad);
            _btnDelete = MkBtn("削除",   OnDelete);
            foreach (var b in new[] { _btnLoad, _btnDelete }) { b.style.flexGrow = 1; opRow.Add(b); }
            root.Add(opRow);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color      = new StyleColor(new Color(0.75f, 0.75f, 0.75f));
            root.Add(_statusLabel);

            UpdateButtonStates();
        }

        public void Refresh()
        {
            if (_warningLabel == null) return;
            var mc = ActiveMeshContext;

            if (mc == null)
            {
                _warningLabel.text          = "メッシュが選択されていません";
                _warningLabel.style.display = DisplayStyle.Flex;
                _meshNameLabel.text         = "";
                _currentSelLabel.text       = "";
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

            _setNames.Clear();
            var sets = ExcludeList;
            if (sets != null)
            {
                foreach (var s in sets)
                    _setNames.Add(s != null ? $"{s.Name}  {s.Summary}" : "");
            }
            _setListView.itemsSource = _setNames;
            _setListView.Rebuild();
            _selectedSetIndex = Mathf.Clamp(_selectedSetIndex, -1, _setNames.Count - 1);
            if (_selectedSetIndex >= 0) _setListView.SetSelection(_selectedSetIndex);
            UpdateButtonStates();
        }

        // ── ListView helpers ─────────────────────────────────────────────
        private VisualElement MakeItem()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var lbl = new Label();
            lbl.style.flexGrow = 1;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.paddingLeft = 4;
            var renameBtn = new Button { text = "※" };
            renameBtn.style.width = 22;
            renameBtn.style.height = 18;
            row.Add(lbl);
            row.Add(renameBtn);
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
            var mc = ActiveMeshContext;
            if (mc == null) { SetStatus("メッシュが選択されていません"); return; }

            bool hasSel = (mc.SelectedVertices?.Count > 0) || (mc.SelectedEdges?.Count > 0)
                       || (mc.SelectedFaces?.Count > 0);
            if (!hasSel) { SetStatus("選択なし"); return; }

            SendCmd(new SaveNormalExcludeSetCommand(ModelIndex, _setNameField?.value?.Trim() ?? ""));
            _setNameField?.SetValueWithoutNotify("");
            Refresh();
            SetStatus("除外に登録しました");
        }

        private void OnLoad()
        {
            if (_selectedSetIndex < 0) return;
            SendCmd(new LoadNormalExcludeSetCommand(ModelIndex, _selectedSetIndex));
            SetStatus("選択に適用しました");
        }

        private void OnDelete()
        {
            if (_selectedSetIndex < 0) return;
            var sets = ExcludeList;
            string name = (sets != null && _selectedSetIndex < sets.Count)
                ? sets[_selectedSetIndex].Name : "?";
            bool ok = PLEditorBridge.I.DisplayDialogYesNo(
                "削除確認", $"「{name}」を削除しますか？", "削除", "キャンセル");
            if (!ok) return;

            SendCmd(new DeleteNormalExcludeSetCommand(ModelIndex, _selectedSetIndex));
            _selectedSetIndex = -1;
            Refresh();
            SetStatus($"削除: {name}");
        }

        private void OnRenameAt(int index)
        {
            string newName = _setNameField?.value?.Trim() ?? "";
            if (string.IsNullOrEmpty(newName))
            {
                SetStatus("名前フィールドに新しい名前を入力してください");
                return;
            }
            SendCmd(new RenameNormalExcludeSetCommand(ModelIndex, index, newName));
            _setNameField?.SetValueWithoutNotify("");
            Refresh();
            SetStatus($"名前変更 → {newName}");
        }

        private void UpdateButtonStates()
        {
            bool hasSel = _selectedSetIndex >= 0;
            if (_btnLoad   != null) _btnLoad.SetEnabled(hasSel);
            if (_btnDelete != null) _btnDelete.SetEnabled(hasSel);
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static Button MkBtn(string t, Action a)
        {
            var b = new Button(a) { text = t };
            b.style.height = 22;
            return b;
        }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10;
            l.style.marginBottom = 3;
            return l;
        }
    }
}
