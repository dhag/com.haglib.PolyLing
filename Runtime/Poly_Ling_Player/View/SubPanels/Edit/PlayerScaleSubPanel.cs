// PlayerScaleSubPanel.cs
// ScaleToolHandler を使用するサブパネル（UIToolkit）。
// エディタ版 DrawSettingsUI() と同等の内容を提供する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerScaleSubPanel
    {
        public Func<ScaleToolHandler> GetH;
        private VisualElement _root;
        private Slider _sliderX, _sliderY, _sliderZ, _sliderXYZ;
        private Toggle _uniformToggle, _originToggle;
        private Label _targetLabel;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement(); _root.style.paddingTop = 4; _root.style.paddingLeft = 4; _root.style.paddingRight = 4;
            parent.Add(_root);
            _root.Add(Header("Scale"));
            _targetLabel = InfoLabel(); _root.Add(_targetLabel);
            _uniformToggle = new Toggle("Uniform") { value = true };
            _uniformToggle.style.color = new StyleColor(Color.white);
            _uniformToggle.RegisterValueChangedCallback(e => { if (GetH() != null) GetH().UniformScale = e.newValue; Refresh(); });
            _root.Add(_uniformToggle);
            _sliderXYZ = MakeSlider("XYZ", 0.01f, 5f, 1f, v => { GetH()?.BeginSliderDrag(); if (GetH() != null) { GetH().ScaleX = v; GetH().ScaleY = v; GetH().ScaleZ = v; } });
            _sliderX = MakeSlider("X", 0.01f, 5f, 1f, v => { GetH()?.BeginSliderDrag(); if (GetH() != null) GetH().ScaleX = v; });
            _sliderY = MakeSlider("Y", 0.01f, 5f, 1f, v => { GetH()?.BeginSliderDrag(); if (GetH() != null) GetH().ScaleY = v; });
            _sliderZ = MakeSlider("Z", 0.01f, 5f, 1f, v => { GetH()?.BeginSliderDrag(); if (GetH() != null) GetH().ScaleZ = v; });
            foreach (var s in new[] { _sliderXYZ, _sliderX, _sliderY, _sliderZ })
                s.RegisterCallback<PointerUpEvent>(_ => GetH()?.EndSliderDrag());
            _root.Add(_sliderXYZ); _root.Add(_sliderX); _root.Add(_sliderY); _root.Add(_sliderZ);
            _originToggle = new Toggle("Origin Pivot") { value = false }; _originToggle.RegisterValueChangedCallback(e => { if (GetH() != null) GetH().UseOriginPivot = e.newValue; });
            _originToggle.style.color = new StyleColor(Color.white);
            _root.Add(_originToggle);
            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginTop = 4;
            var applyBtn = new Button(() => GetH()?.EndSliderDrag()) { text = "Apply" }; applyBtn.style.flexGrow = 1; applyBtn.style.marginRight = 2;
            var revertBtn = new Button(() => { GetH()?.Revert(); _sliderX?.SetValueWithoutNotify(1); _sliderY?.SetValueWithoutNotify(1); _sliderZ?.SetValueWithoutNotify(1); _sliderXYZ?.SetValueWithoutNotify(1); }) { text = "Reset" }; revertBtn.style.flexGrow = 1;
            btnRow.Add(applyBtn); btnRow.Add(revertBtn); _root.Add(btnRow);
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            _targetLabel.text = $"Target: {h.GetTotalAffectedCount()} vertices";
            bool uni = h.UniformScale;
            _uniformToggle?.SetValueWithoutNotify(uni);
            _sliderXYZ.style.display = uni ? DisplayStyle.Flex : DisplayStyle.None;
            _sliderX.style.display = uni ? DisplayStyle.None : DisplayStyle.Flex;
            _sliderY.style.display = uni ? DisplayStyle.None : DisplayStyle.Flex;
            _sliderZ.style.display = uni ? DisplayStyle.None : DisplayStyle.Flex;
            if (uni) _sliderXYZ?.SetValueWithoutNotify(h.ScaleX);
            else { _sliderX?.SetValueWithoutNotify(h.ScaleX); _sliderY?.SetValueWithoutNotify(h.ScaleY); _sliderZ?.SetValueWithoutNotify(h.ScaleZ); }
            _originToggle?.SetValueWithoutNotify(h.UseOriginPivot);
        }

        // ── ヘルパー ──────────────────────────────────────────────────────

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.marginTop = 4; l.style.marginBottom = 3;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10; l.style.marginBottom = 2;
            return l;
        }

        private static Slider MakeSlider(string label, float min, float max, float init, Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = init };
            s.style.color = new StyleColor(Color.white);
            s.style.marginBottom = 3;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }

        private static SliderInt MakeIntSlider(string label, int min, int max, int init, Action<int> onChange)
        {
            var s = new SliderInt(label, min, max) { value = init };
            s.style.color = new StyleColor(Color.white);
            s.style.marginBottom = 3;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }
    }
}
