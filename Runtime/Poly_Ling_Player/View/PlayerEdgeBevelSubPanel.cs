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

        private VisualElement _root;
        private FloatField    _amountField;
        private SliderInt     _segmentsSlider;
        private Toggle        _filletToggle;
        private VisualElement _filletRow;

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
            foreach (var (label, val) in new[] { ("0.05", 0.05f), ("0.1", 0.1f), ("0.2", 0.2f) })
            {
                float v = val;
                var b = new Button(() => { _amountField?.SetValueWithoutNotify(v); var h = GetH(); if (h != null) h.Amount = v; }) { text = label };
                b.style.flexGrow = 1;
                presetRow.Add(b);
            }
            _root.Add(presetRow);

            // Segments
            _segmentsSlider = new SliderInt("Segments", 1, 10) { value = 1 };
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
            _filletToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.Fillet = e.newValue; });
            _filletRow.Add(_filletToggle);
            _root.Add(_filletRow);

            UpdateFilletVisibility(1);
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            _amountField?.SetValueWithoutNotify(h.Amount);
            _segmentsSlider?.SetValueWithoutNotify(h.Segments);
            _filletToggle?.SetValueWithoutNotify(h.Fillet);
            UpdateFilletVisibility(h.Segments);
        }

        private void UpdateFilletVisibility(int segs)
        {
            if (_filletRow != null)
                _filletRow.style.display = segs >= 2 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static Label Header(string t) { var l = new Label(t); l.style.marginTop = 4; l.style.marginBottom = 3; return l; }

    }
}