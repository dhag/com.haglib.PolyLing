// PlayerMergeMeshesSubPanel.cs
// MergeMeshesPanel の Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.View;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerMergeMeshesSubPanel
    {
        public Func<ProjectContext>   GetView;
        public Action<PanelCommand> SendCommand;

        private Label         _selectionLabel;
        private ListView      _baseObjectList;
        private Toggle        _createNewMeshToggle;
        private Button        _mergeButton;
        private Label         _statusLabel;

        private readonly List<IMeshView> _selectedMeshViews = new List<IMeshView>();
        private int _baseListIndex = 0;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("メッシュマージ"));

            _selectionLabel = new Label("選択メッシュ: 0");
            _selectionLabel.style.marginBottom = 4;
            root.Add(_selectionLabel);

            var baseLabel = new Label("基準オブジェクト:");
            baseLabel.style.marginBottom = 2;
            root.Add(baseLabel);

            _baseObjectList = new ListView
            {
                selectionType   = SelectionType.Single,
                fixedItemHeight = 22,
                makeItem        = () =>
                {
                    var lbl = new Label();
                    lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
                    lbl.style.paddingLeft    = 4;
                    return lbl;
                },
                bindItem    = (elem, i) => { if (elem is Label l && i < _selectedMeshViews.Count) l.text = _selectedMeshViews[i].Name; },
                itemsSource = _selectedMeshViews,
            };
            _baseObjectList.style.minHeight    = 80;
            _baseObjectList.style.marginBottom = 6;
            _baseObjectList.style.borderTopWidth    = _baseObjectList.style.borderBottomWidth =
            _baseObjectList.style.borderLeftWidth   = _baseObjectList.style.borderRightWidth  = 1;
            _baseObjectList.style.borderTopColor    = _baseObjectList.style.borderBottomColor =
            _baseObjectList.style.borderLeftColor   = _baseObjectList.style.borderRightColor  =
                new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            _baseObjectList.selectionChanged += _ => _baseListIndex = _baseObjectList.selectedIndex >= 0 ? _baseObjectList.selectedIndex : 0;
            root.Add(_baseObjectList);

            _createNewMeshToggle = new Toggle("新規メッシュオブジェクトに格納する") { value = true };
            _createNewMeshToggle.style.marginBottom = 8;
            root.Add(_createNewMeshToggle);

            _mergeButton = new Button(OnMerge) { text = "マージ実行" };
            _mergeButton.style.height       = 28;
            _mergeButton.style.marginBottom = 4;
            root.Add(_mergeButton);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 10;
            _statusLabel.style.color    = new StyleColor(new Color(0.6f, 0.6f, 0.6f));
            root.Add(_statusLabel);
        }

        public void Refresh()
        {
            var project = GetView?.Invoke();
            if (project == null) { SetStatus("プロジェクトなし"); return; }
            var model = project.CurrentModel;
            if (model == null) { SetStatus("モデルなし"); return; }

            _selectedMeshViews.Clear();
            var liveModel = new LiveModelView(model);
            var selIndices = liveModel.SelectedDrawableIndices;
            var drawList   = liveModel.DrawableList;
            if (selIndices != null && drawList != null)
                foreach (int idx in selIndices)
                    if (idx >= 0 && idx < drawList.Count) _selectedMeshViews.Add(drawList[idx]);

            _selectionLabel.text = $"選択メッシュ: {_selectedMeshViews.Count}";
            _baseObjectList.itemsSource = _selectedMeshViews;
            _baseObjectList.Rebuild();
            if (_selectedMeshViews.Count > 0)
            {
                _baseListIndex = Mathf.Clamp(_baseListIndex, 0, _selectedMeshViews.Count - 1);
                _baseObjectList.SetSelection(_baseListIndex);
            }
            bool canMerge = _selectedMeshViews.Count >= 2;
            _mergeButton.SetEnabled(canMerge);
            _statusLabel.text = canMerge ? "" : "2つ以上のメッシュを選択してください";
        }

        private void OnMerge()
        {
            var view = GetView?.Invoke(); if (view == null) return;
            var model = view.CurrentModel; if (model == null) return;
            int modelIdx = view.CurrentModelIndex;
            if (_selectedMeshViews.Count < 2) { SetStatus("2つ以上のメッシュを選択してください"); return; }
            if (_baseListIndex < 0 || _baseListIndex >= _selectedMeshViews.Count) { SetStatus("基準オブジェクトを選択してください"); return; }

            var masterIndices = new int[_selectedMeshViews.Count];
            for (int i = 0; i < _selectedMeshViews.Count; i++) masterIndices[i] = _selectedMeshViews[i].MasterIndex;
            int baseMasterIndex = _selectedMeshViews[_baseListIndex].MasterIndex;

            SendCommand?.Invoke(new MergeMeshesCommand(modelIdx, masterIndices, baseMasterIndex, _createNewMeshToggle.value));
            SetStatus("マージを実行しました");
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }
        private static Label SecLabel(string t) { var l = new Label(t); l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f)); l.style.fontSize = 10; l.style.marginBottom = 3; return l; }
    }
}
