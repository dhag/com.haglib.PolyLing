// PlayerReferenceSymmetrySubPanel.cs
// 「その他 > リファレンスに基づく対称化（臨時）」パネル。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.UI;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public sealed class PlayerReferenceSymmetrySubPanel
    {
        private const string NoneChoice = "(なし)";

        /// <summary>モデルの窓口（操作経路統一計画.md E）。</summary>
        public Func<IModelView> GetModel;
        /// <summary>コマンドの送り先（対称化は ApplyReferenceSymmetryCommand で行う）。失敗理由を返す（成功なら null）。</summary>
        public Func<PanelCommand, string> SendCommand;
        /// <summary>送り先のモデル番号。</summary>
        public Func<int> GetModelIndex;

        private readonly List<int> _referenceMap = new List<int>();
        private readonly List<int> _targetMap    = new List<int>();
        private bool _refreshing;

        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("reference", Description = "左右対称な参照オブジェクト（あたまREF）")]
        private DropdownField _referenceDropdown;
        [UiControl("target", Description = "正 X 側を負 X 側へ移植する対象（あたま1）")]
        private DropdownField _targetDropdown;
        [UiControl("tolerance", Description = "REF の対称点を照合する位置誤差")]
        private FloatField _toleranceField;
        [UiControl("recalcNormals", Description = "生成後に法線を再計算する")]
        private Toggle _recalcNormalsToggle;
        [UiControl("sourcePositive", Description = "移植元を正 X 側にする（オフなら負 X 側から正 X 側へ移植）")]
        private Toggle _sourcePositiveToggle;
        [UiControl("newName", Description = "作成するオブジェクトの名前（空欄なら「対象名_対称」。重複時は末尾に番号）")]
        private TextField _nameField;
        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "対象を複製し、REF の対応に基づいて負 X 側を対称化する")]
        private Button _applyButton;
        [UiControl("status", Safety = UiSafety.ReadOnly, Description = "検査結果または実行結果")]
        private Label _statusLabel;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingLeft = 4;
            _root.style.paddingRight = 4;
            _root.style.paddingTop = 4;
            parent.Add(_root);

            var title = new Label("リファレンスに基づく対称化（臨時）");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            _root.Add(title);

            var note = new Label(
                "REF の移植元側の頂点に対応する反対側の頂点を求め、対象の移植元側を反転してクローンの反対側へ移植します。|X| が許容誤差以下の頂点（中心線）は変更しません。");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginBottom = 6;
            _root.Add(note);

            _referenceDropdown = new DropdownField("REF", new List<string> { NoneChoice }, 0);
            _targetDropdown = new DropdownField("対象", new List<string> { NoneChoice }, 0);
            _toleranceField = new FloatField("対称点の許容誤差") { value = 0.0001f };
            _recalcNormalsToggle = new Toggle("法線を再計算") { value = true };
            _sourcePositiveToggle = new Toggle("移植元を正 X 側にする（オフ: 負 X → 正 X）") { value = true };
            _nameField = new TextField("作成する名前") { value = "" };
            _nameField.tooltip = "空欄なら「対象名_対称」。既存と重複すれば末尾に番号を付けます。";
            _applyButton = new Button(Apply) { text = "クローンを作成して対称化" };
            _applyButton.style.marginTop = 6;
            _statusLabel = new Label();
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginTop = 4;

            _referenceDropdown.RegisterValueChangedCallback(_ => RefreshActionState());
            _targetDropdown.RegisterValueChangedCallback(_ => RefreshActionState());
            _toleranceField.RegisterValueChangedCallback(e =>
            {
                if (e.newValue > 0f) return;
                _toleranceField.SetValueWithoutNotify(0.0001f);
            });

            _root.Add(_referenceDropdown);
            _root.Add(_targetDropdown);
            _root.Add(_toleranceField);
            _root.Add(_recalcNormalsToggle);
            _root.Add(_sourcePositiveToggle);
            _root.Add(_nameField);
            _root.Add(_applyButton);
            _root.Add(_statusLabel);
        }

        public void Refresh()
        {
            if (_refreshing) return;
            _refreshing = true;
            try
            {
                var model = GetModel?.Invoke();
                string oldRef = _referenceDropdown?.value;
                string oldTarget = _targetDropdown?.value;
                var choices = new List<string> { NoneChoice };
                _referenceMap.Clear();
                _targetMap.Clear();
                _referenceMap.Add(-1);
                _targetMap.Add(-1);

                if (model != null)
                {
                    foreach (var mc in model.DrawableList)
                    {
                        if (mc == null) continue;
                        choices.Add($"{mc.MasterIndex}: {mc.Name} ({mc.VertexCount}頂点)");
                        _referenceMap.Add(mc.MasterIndex);
                        _targetMap.Add(mc.MasterIndex);
                    }
                }

                _referenceDropdown.choices = new List<string>(choices);
                _targetDropdown.choices = new List<string>(choices);
                SelectChoice(_referenceDropdown, oldRef, choices, "あたまREF");
                SelectChoice(_targetDropdown, oldTarget, choices, "あたま１", "あたま1");
                _statusLabel.text = model == null ? "モデルがありません" : "";
            }
            finally
            {
                _refreshing = false;
            }
            RefreshActionState();
        }

        private void Apply()
        {
            int refIndex = SelectedIndex(_referenceDropdown, _referenceMap);
            int targetIndex = SelectedIndex(_targetDropdown, _targetMap);
            if (SendCommand == null) return;

            // 対称化はコマンドで行う（操作経路統一計画.md E）。
            string err = SendCommand(new ApplyReferenceSymmetryCommand(
                GetModelIndex?.Invoke() ?? 0, refIndex, targetIndex, _toleranceField.value,
                _recalcNormalsToggle.value, _nameField?.value ?? "",
                _sourcePositiveToggle?.value ?? true));

            bool ok = err == null;
            if (ok) Refresh();
            _statusLabel.text = ok ? "完了しました" : err;
            _statusLabel.style.color = new StyleColor(
                ok ? new Color(0.35f, 0.8f, 0.4f) : new Color(1f, 0.45f, 0.25f));
        }

        private void RefreshActionState()
        {
            if (_applyButton == null) return;
            int r = SelectedIndex(_referenceDropdown, _referenceMap);
            int t = SelectedIndex(_targetDropdown, _targetMap);
            _applyButton.SetEnabled(r >= 0 && t >= 0 && r != t);
        }

        private static int SelectedIndex(DropdownField field, List<int> map)
        {
            int i = field?.index ?? -1;
            return i >= 0 && i < map.Count ? map[i] : -1;
        }

        private static void SelectChoice(
            DropdownField field, string oldValue, List<string> choices, params string[] preferredNames)
        {
            int index = !string.IsNullOrEmpty(oldValue) ? choices.IndexOf(oldValue) : -1;
            if (index < 0 && preferredNames != null)
            {
                foreach (string name in preferredNames)
                {
                    index = choices.FindIndex(c => c.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (index >= 0) break;
                }
            }
            field.index = index >= 0 ? index : 0;
        }
    }
}
