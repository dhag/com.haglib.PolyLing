// PlayerBooleanSubPanel.cs
// ブーリアン演算（和 / 差 / 積）の Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置
//
// 選択メッシュ 2 個を対象にする。リストで選んだ側が A（基準）、
// もう一方が B になる。演算は A のローカル空間で行い、結果も A の姿勢を引き継ぐ。
// 実処理は BooleanMeshCommand -> BooleanOps。ここは入力の組み立てだけを行う。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.View;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerBooleanSubPanel
    {
        public Func<ProjectContext> GetView;
        public Action<PanelCommand> SendCommand;

        private Label      _selectionLabel;
        private ListView   _baseObjectList;
        private EnumField  _opField;
        private Toggle     _createNewMeshToggle;
        private Toggle     _deleteBToggle;
        private Toggle     _mergeVerticesToggle;
        private FloatField _mergeThresholdField;
        private FloatField _epsilonField;
        private Button     _executeButton;
        private Label      _statusLabel;

        private readonly List<IMeshView> _selectedMeshViews = new List<IMeshView>();
        private int _baseListIndex = 0;

        public void Build(VisualElement parent)
        {
            var root = new VisualElement();
            root.style.paddingLeft = root.style.paddingRight =
            root.style.paddingTop  = root.style.paddingBottom = 4;
            parent.Add(root);

            root.Add(SecLabel("ブーリアン"));

            _selectionLabel = new Label("選択メッシュ: 0");
            _selectionLabel.style.marginBottom = 4;
            root.Add(_selectionLabel);

            var baseLabel = new Label("A（基準／差では削られる側）:");
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
            _baseObjectList.style.minHeight    = 60;
            _baseObjectList.style.marginBottom = 6;
            _baseObjectList.style.borderTopWidth    = _baseObjectList.style.borderBottomWidth =
            _baseObjectList.style.borderLeftWidth   = _baseObjectList.style.borderRightWidth  = 1;
            _baseObjectList.style.borderTopColor    = _baseObjectList.style.borderBottomColor =
            _baseObjectList.style.borderLeftColor   = _baseObjectList.style.borderRightColor  =
                new StyleColor(Color.white);
            _baseObjectList.selectionChanged += _ =>
            {
                _baseListIndex = _baseObjectList.selectedIndex >= 0 ? _baseObjectList.selectedIndex : 0;
                UpdateExecutable();
            };
            root.Add(_baseObjectList);

            _opField = new EnumField("演算", BooleanOpKind.Subtract);
            _opField.style.marginBottom = 6;
            root.Add(_opField);

            _createNewMeshToggle = new Toggle("新規メッシュオブジェクトに格納する") { value = true };
            _createNewMeshToggle.style.marginBottom = 2;
            root.Add(_createNewMeshToggle);

            _deleteBToggle = new Toggle("B を削除する") { value = false };
            _deleteBToggle.style.marginBottom = 6;
            root.Add(_deleteBToggle);

            _mergeVerticesToggle = new Toggle("同一位置の頂点をマージする") { value = true };
            _mergeVerticesToggle.style.marginBottom = 2;
            root.Add(_mergeVerticesToggle);

            _mergeThresholdField = new FloatField("マージしきい値") { value = BooleanOps.DefaultMergeThreshold };
            _mergeThresholdField.style.marginBottom = 2;
            root.Add(_mergeThresholdField);

            _epsilonField = new FloatField("epsilon") { value = BooleanOps.DefaultEpsilon };
            _epsilonField.style.marginBottom = 8;
            root.Add(_epsilonField);

            _executeButton = new Button(OnExecute) { text = "ブーリアン実行" };
            _executeButton.style.height       = 28;
            _executeButton.style.marginBottom = 4;
            root.Add(_executeButton);

            _statusLabel = new Label();
            _statusLabel.style.fontSize    = 10;
            _statusLabel.style.color       = new StyleColor(Color.white);
            _statusLabel.style.whiteSpace  = WhiteSpace.Normal;
            root.Add(_statusLabel);
        }

        public void Refresh()
        {
            var project = GetView?.Invoke();
            if (project == null) { SetStatus("プロジェクトなし"); return; }
            var model = project.CurrentModel;
            if (model == null) { SetStatus("モデルなし"); return; }

            _selectedMeshViews.Clear();
            var liveModel  = new LiveModelView(model);
            var selIndices = liveModel.SelectedDrawableIndices;
            var drawList   = liveModel.DrawableList;
            if (selIndices != null && drawList != null)
                foreach (int idx in selIndices)
                    if (idx >= 0 && idx < drawList.Count) _selectedMeshViews.Add(drawList[idx]);

            _selectionLabel.text        = $"選択メッシュ: {_selectedMeshViews.Count}";
            _baseObjectList.itemsSource = _selectedMeshViews;
            _baseObjectList.Rebuild();
            if (_selectedMeshViews.Count > 0)
            {
                _baseListIndex = Mathf.Clamp(_baseListIndex, 0, _selectedMeshViews.Count - 1);
                _baseObjectList.SetSelection(_baseListIndex);
            }

            UpdateExecutable();
        }

        /// <summary>実行可否を判定してボタンと説明を更新する。</summary>
        private void UpdateExecutable()
        {
            if (_executeButton == null) return;

            if (_selectedMeshViews.Count != 2)
            {
                _executeButton.SetEnabled(false);
                SetStatus("メッシュを 2 つ選択してください");
                return;
            }

            // スキンドメッシュは CSG がボーンウェイトを運べないため対象外。
            foreach (var mv in _selectedMeshViews)
            {
                if (mv.HasBoneWeight)
                {
                    _executeButton.SetEnabled(false);
                    SetStatus($"スキンドメッシュは対象にできません: {mv.Name}");
                    return;
                }
            }

            int bIndex = _baseListIndex == 0 ? 1 : 0;
            if (bIndex >= _selectedMeshViews.Count) { _executeButton.SetEnabled(false); return; }

            _executeButton.SetEnabled(true);
            SetStatus($"A = {_selectedMeshViews[_baseListIndex].Name} / B = {_selectedMeshViews[bIndex].Name}");
        }

        private void OnExecute()
        {
            var view = GetView?.Invoke(); if (view == null) return;
            var model = view.CurrentModel; if (model == null) return;
            int modelIdx = view.CurrentModelIndex;

            if (_selectedMeshViews.Count != 2) { SetStatus("メッシュを 2 つ選択してください"); return; }
            if (_baseListIndex < 0 || _baseListIndex >= _selectedMeshViews.Count) { SetStatus("A を選択してください"); return; }

            int bIndex = _baseListIndex == 0 ? 1 : 0;

            int aMaster = _selectedMeshViews[_baseListIndex].MasterIndex;
            int bMaster = _selectedMeshViews[bIndex].MasterIndex;
            if (aMaster == bMaster) { SetStatus("同一メッシュは指定できません"); return; }

            var op = (BooleanOpKind)_opField.value;

            float mergeThreshold = Mathf.Max(0f, _mergeThresholdField.value);
            float epsilon        = _epsilonField.value;
            if (epsilon <= 0f) epsilon = BooleanOps.DefaultEpsilon;

            SendCommand?.Invoke(new BooleanMeshCommand(
                modelIdx,
                aMaster,
                bMaster,
                op,
                _createNewMeshToggle.value,
                _deleteBToggle.value,
                _mergeVerticesToggle.value,
                mergeThreshold,
                epsilon));

            SetStatus($"{BooleanOps.DisplayName(op)} を実行しました");
        }

        private void SetStatus(string s) { if (_statusLabel != null) _statusLabel.text = s; }

        private static Label SecLabel(string t)
        {
            var l = new Label(t);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginBottom = 3;
            return l;
        }
    }
}
