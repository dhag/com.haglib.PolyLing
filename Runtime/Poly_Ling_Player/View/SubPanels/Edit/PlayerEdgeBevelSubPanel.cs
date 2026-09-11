// PlayerEdgeBevelSubPanel.cs
// 辺ベベルツール用サブパネル。エディタ版 EdgeBevelTool.DrawSettingsUI() と同等。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerEdgeBevelSubPanel
    {
        public Func<EdgeBevelToolHandler> GetH;

        // UI 自動操作の ID は "edgeBevel.<下の Id>"（UiControlAttribute.cs）。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("amount", Description = "ベベルの幅")]
        private FloatField    _amountField;
        [UiControl("dragSensitivity", Description = "ドラッグ量に対する幅の変化の比例係数")]
        private FloatField    _dragSensField;
        [UiControl("segments", Description = "ベベルの分割数")]
        private SliderInt     _segmentsSlider;
        [UiControl("fillet", Reveal = nameof(RevealFillet), Description = "丸める（分割数 2 以上のときだけ表示）")]
        private Toggle        _filletToggle;
        [UiControl(Ignore = true)]
        private VisualElement _filletRow;
        [UiControl("amountPreset.small", Safety = UiSafety.SafeWrite, Description = "幅を 0.05 にする")]
        private Button        _amountPreset005Btn;
        [UiControl("amountPreset.medium", Safety = UiSafety.SafeWrite, Description = "幅を 0.1 にする")]
        private Button        _amountPreset01Btn;
        [UiControl("amountPreset.large", Safety = UiSafety.SafeWrite, Description = "幅を 0.2 にする")]
        private Button        _amountPreset02Btn;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            _root.Add(Header("Edge Bevel"));
            _root.Add(new HelpBox("エッジにカーソルを合わせてドラッグ", HelpBoxMessageType.Info));

            // Amount — FloatField（エディタ版と同じ直接入力）
            var amountRow = new VisualElement();
            amountRow.style.flexDirection = FlexDirection.Row;
            amountRow.style.marginBottom  = 3;
            var amountLbl = new Label("Amount");
            amountLbl.style.color = new StyleColor(Color.white);
            amountLbl.style.width = 60; amountLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _amountField = new FloatField { value = 0.1f };
            _amountField.style.flexGrow = 1;
            _amountField.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Max(0.001f, e.newValue);
                _amountField.SetValueWithoutNotify(v);
                var h = GetH(); if (h != null) h.Amount = v;
            });
            amountRow.Add(amountLbl); amountRow.Add(_amountField);
            _root.Add(amountRow);

            // プリセットボタン
            var presetRow = new VisualElement();
            presetRow.style.flexDirection = FlexDirection.Row;
            presetRow.style.marginBottom  = 4;
            var presetBtns = new Button[3];
            int presetIdx = 0;
            foreach (var (label, val) in new[] { ("0.05", 0.05f), ("0.1", 0.1f), ("0.2", 0.2f) })
            {
                float v = val;
                var b = new Button(() => { _amountField?.SetValueWithoutNotify(v); var h = GetH(); if (h != null) h.Amount = v; }) { text = label };
                b.style.flexGrow = 1;
                presetRow.Add(b);
                presetBtns[presetIdx++] = b;
            }
            _amountPreset005Btn = presetBtns[0];
            _amountPreset01Btn  = presetBtns[1];
            _amountPreset02Btn  = presetBtns[2];
            _root.Add(presetRow);

            // Drag Sensitivity — テキストボックス（カメラ平面での実ドラッグ距離への比例係数）
            var sensRow = new VisualElement();
            sensRow.style.flexDirection = FlexDirection.Row;
            sensRow.style.marginBottom  = 4;
            var sensLbl = new Label("Drag Sens.");
            sensLbl.style.color = new StyleColor(Color.white);
            sensLbl.style.width = 70; sensLbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            _dragSensField = new FloatField { value = 1f };
            _dragSensField.style.flexGrow = 1;
            _dragSensField.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Max(0.001f, e.newValue);
                _dragSensField.SetValueWithoutNotify(v);
                var h = GetH(); if (h != null) h.DragSensitivity = v;
            });
            sensRow.Add(sensLbl); sensRow.Add(_dragSensField);
            _root.Add(sensRow);


            // Segments
            _segmentsSlider = new SliderInt("Segments", 1, 10) { value = 1 };
            _segmentsSlider.style.color = new StyleColor(Color.white);
            _segmentsSlider.style.marginBottom = 3;
            _segmentsSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetH(); if (h != null) h.Segments = e.newValue;
                UpdateFilletVisibility(e.newValue);
            });
            _root.Add(_segmentsSlider);

            // Fillet（Segments >= 2 時のみ表示）
            _filletRow = new VisualElement();
            _filletToggle = new Toggle("Fillet (Round)") { value = true };
            _filletToggle.style.color = new StyleColor(Color.white);
            _filletToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.Fillet = e.newValue; });
            _filletRow.Add(_filletToggle);
            _root.Add(_filletRow);

            UpdateFilletVisibility(1);
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            _amountField?.SetValueWithoutNotify(h.Amount);
            _dragSensField?.SetValueWithoutNotify(h.DragSensitivity);
            _segmentsSlider?.SetValueWithoutNotify(h.Segments);
            _filletToggle?.SetValueWithoutNotify(h.Fillet);
            UpdateFilletVisibility(h.Segments);
        }

        private void UpdateFilletVisibility(int segs)
        {
            if (_filletRow != null)
                _filletRow.style.display = segs >= 2 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// UI 自動操作の表示の下準備（UiControl の Reveal）。Fillet は分割数 2 以上のときだけ
        /// 表示されるので、利用者と同じく分割数を 2 にする。既に 2 以上なら何もせず false。
        /// </summary>
        private bool RevealFillet()
        {
            if (_segmentsSlider == null || _segmentsSlider.value >= 2) return false;
            _segmentsSlider.value = 2;
            return true;
        }

        private static Label Header(string t) { var l = new Label(t); l.style.marginTop = 4; l.style.marginBottom = 3; return l; }

    }
}