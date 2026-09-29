// PlayerBooleanSubPanel.cs
// ブーリアン演算（和 / 差 / 積）の Player 版サブパネル。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置
//
// 選択メッシュ 2 個を対象にする。リストで選んだ側が A（基準）、
// もう一方が B になる。演算は A のローカル空間で行い、結果も A の姿勢を引き継ぐ。
// 実処理は BooleanMeshCommand -> BooleanOps。ここは入力の組み立てだけを行う。
// 「2D」を入れると BooleanMesh2DCommand -> Boolean2DOps（同じ平面の面／線分のループどうし）。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.View;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public class PlayerBooleanSubPanel
    {
        public Func<Poly_Ling.View.IProjectView> GetView;
        public Action<PanelCommand> SendCommand;

        // UI 自動操作の ID は "boolean.<下の Id>"（UiControlAttribute.cs）。
        [UiControl("selection", Safety = UiSafety.ReadOnly, Description = "選択中のメッシュの数")]
        private Label      _selectionLabel;
        [UiControl("baseObject", Description = "A（基準／差では削られる側）にするオブジェクト（選択メッシュの一覧の行番号）")]
        private ListView   _baseObjectList;
        [UiControl("operation", Description = "ブーリアン演算の種類")]
        private EnumField  _opField;
        [UiControl("createNewMesh", Description = "結果を新しいメッシュオブジェクトに格納する")]
        private Toggle     _createNewMeshToggle;
        [UiControl("deleteB", Description = "実行後に B を削除する")]
        private Toggle     _deleteBToggle;
        [UiControl("mergeVertices", Description = "同一位置の頂点をマージする")]
        private Toggle     _mergeVerticesToggle;
        [UiControl("mergeThreshold", Description = "頂点マージのしきい値")]
        private FloatField _mergeThresholdField;
        [UiControl("epsilon", Description = "演算に渡す epsilon")]
        private FloatField _epsilonField;
        // ── 2D（同じ平面の図形どうし。BooleanMesh2DCommand） ──
        [UiControl("mode2D", Description = "2D ブーリアンにする（同じ平面上の面どうし、または線分のループどうしを演算する）")]
        private Toggle     _mode2DToggle;
        [UiControl(Ignore = true)]
        private VisualElement _box3D;
        [UiControl(Ignore = true)]
        private VisualElement _box2D;
        [UiControl("operation2D", Description = "2D のときの演算の種類（和 / 差 / 積 / 排他的論理和）")]
        private EnumField  _op2DField;
        [UiControl("source2D", Description = "2D のときの入力。Faces は 3 頂点以上の面、Lines は線分の閉じたループ")]
        private EnumField  _source2DField;
        [UiControl("planeTolerance", Description = "2D のとき、同じ平面とみなす距離の上限")]
        private FloatField _planeTolField;

        [UiControl("run", Safety = UiSafety.Destructive, Description = "ブーリアン演算を実行する。「B を削除する」がオンなら B を削除する")]
        private Button     _executeButton;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "実行できない理由、または実行結果")]
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

            _mode2DToggle = new Toggle("2D（同じ平面の図形どうし）") { value = false };
            _mode2DToggle.style.marginBottom = 4;
            _mode2DToggle.RegisterValueChangedCallback(_ => UpdateModeVisibility());
            root.Add(_mode2DToggle);

            _box3D = new VisualElement();
            _box2D = new VisualElement();

            _opField = new EnumField("演算", BooleanOpKind.Subtract);
            _opField.style.marginBottom = 6;
            _box3D.Add(_opField);

            _op2DField = new EnumField("演算", Boolean2DOpKind.Subtract);
            _op2DField.style.marginBottom = 2;
            _box2D.Add(_op2DField);

            _source2DField = new EnumField("入力", Boolean2DSource.Faces);
            _source2DField.style.marginBottom = 2;
            _box2D.Add(_source2DField);

            _planeTolField = new FloatField("平面の許容量") { value = Boolean2DOps.DefaultPlaneTolerance };
            _planeTolField.style.marginBottom = 6;
            _box2D.Add(_planeTolField);

            root.Add(_box3D);
            root.Add(_box2D);

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

            UpdateModeVisibility();
        }

        /// <summary>3D / 2D で使う欄だけを見せる。頂点マージと epsilon は 3D の CSG 専用。</summary>
        private void UpdateModeVisibility()
        {
            bool is2D = _mode2DToggle != null && _mode2DToggle.value;
            var show3D = is2D ? DisplayStyle.None : DisplayStyle.Flex;
            if (_box3D != null) _box3D.style.display = show3D;
            if (_box2D != null) _box2D.style.display = is2D ? DisplayStyle.Flex : DisplayStyle.None;
            if (_mergeVerticesToggle != null) _mergeVerticesToggle.style.display = show3D;
            if (_mergeThresholdField != null) _mergeThresholdField.style.display = show3D;
            if (_epsilonField        != null) _epsilonField.style.display        = show3D;
        }

        public void Refresh()
        {
            var project = GetView?.Invoke();
            if (project == null) { SetStatus("プロジェクトなし"); return; }
            var model = project.CurrentModel;
            if (model == null) { SetStatus("モデルなし"); return; }

            _selectedMeshViews.Clear();
            foreach (int idx in model.SelectedDrawableIndices ?? System.Array.Empty<int>())
            {
                var mv = model.GetMesh(idx);
                if (mv != null) _selectedMeshViews.Add(mv);
            }

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

            if (_mode2DToggle.value)
            {
                var op2 = (Boolean2DOpKind)_op2DField.value;
                SendCommand?.Invoke(new BooleanMesh2DCommand(
                    modelIdx, aMaster, bMaster, op2,
                    (Boolean2DSource)_source2DField.value,
                    _createNewMeshToggle.value,
                    _deleteBToggle.value,
                    Mathf.Max(0f, _planeTolField.value)));
                SetStatus($"2D {Boolean2DOps.DisplayName(op2)} を実行しました");
                return;
            }

            var op = (BooleanOpKind)_opField.value;

            float mergeThreshold = Mathf.Max(MergeThresholdMin, _mergeThresholdField.value);
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

        // ================================================================
        // レンジ（上下限）
        //
        // 実体は ParameterLimits（persistentDataPath の CSV）にあり、ここでは
        // キーを引くだけにする。同じキーを PanelCommand の PLParam(LimitKey) が
        // 指すので、UI とスキーマで範囲の定義が1箇所になる。
        // ================================================================

        private static float MergeThresholdMin => ParameterLimits.GetF("Boolean.MergeThreshold.Min");

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
