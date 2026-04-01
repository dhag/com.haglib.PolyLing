// PlayerRotateSubPanel.cs
// 回転ツール用サブパネル。エディタ版 RotateTool.DrawSettingsUI() と同等。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerRotateSubPanel
    {
        public Func<RotateToolHandler> GetH;

        private VisualElement _root;
        private Slider        _sliderX, _sliderY, _sliderZ;
        private Toggle        _snapToggle, _originToggle;
        private FloatField    _snapField;
        private Label         _targetLabel;
        private Label         _pivotLabel;

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            _root.Add(Header("Rotate"));
            _targetLabel = InfoLabel(); _root.Add(_targetLabel);
            _pivotLabel  = InfoLabel(); _root.Add(_pivotLabel);

            _sliderX = MakeSlider("X", -180f, 180f, 0f, v => { GetH()?.BeginSliderDrag(); var h = GetH(); if (h != null) h.RotX = Snap(v); });
            _sliderY = MakeSlider("Y", -180f, 180f, 0f, v => { GetH()?.BeginSliderDrag(); var h = GetH(); if (h != null) h.RotY = Snap(v); });
            _sliderZ = MakeSlider("Z", -180f, 180f, 0f, v => { GetH()?.BeginSliderDrag(); var h = GetH(); if (h != null) h.RotZ = Snap(v); });

            // PointerUpEvent で自動 EndSliderDrag（エディタ版 Event.current.type == MouseUp に対応）
            foreach (var s in new[] { _sliderX, _sliderY, _sliderZ })
            {
                s.RegisterCallback<PointerUpEvent>(_ => GetH()?.EndSliderDrag());
                _root.Add(s);
            }

            var snapRow = new VisualElement();
            snapRow.style.flexDirection = FlexDirection.Row;
            snapRow.style.marginBottom  = 3;
            _snapToggle = new Toggle("Snap") { value = false };
            _snapToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.UseSnap = e.newValue; });
            _snapField = new FloatField { value = 15f };
            _snapField.style.width = 50; _snapField.style.marginLeft = 4;
            _snapField.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.SnapAngle = Mathf.Max(0.1f, e.newValue); });
            snapRow.Add(_snapToggle); snapRow.Add(_snapField);
            _root.Add(snapRow);

            _originToggle = new Toggle("Origin Pivot") { value = false };
            _originToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.UseOriginPivot = e.newValue; });
            _root.Add(_originToggle);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop     = 4;
            var applyBtn  = new Button(() => GetH()?.EndSliderDrag()) { text = "Apply" };
            applyBtn.style.flexGrow = 1; applyBtn.style.marginRight = 2;
            var revertBtn = new Button(() =>
            {
                GetH()?.Revert();
                _sliderX?.SetValueWithoutNotify(0);
                _sliderY?.SetValueWithoutNotify(0);
                _sliderZ?.SetValueWithoutNotify(0);
            }) { text = "Reset" };
            revertBtn.style.flexGrow = 1;
            btnRow.Add(applyBtn); btnRow.Add(revertBtn);
            _root.Add(btnRow);
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            _targetLabel.text = $"Target: {h.GetTotalAffectedCount()} vertices";
            var p = h.PivotPublic;
            _pivotLabel.text  = $"Pivot: ({p.x:F2}, {p.y:F2}, {p.z:F2})";
            _sliderX?.SetValueWithoutNotify(h.RotX);
            _sliderY?.SetValueWithoutNotify(h.RotY);
            _sliderZ?.SetValueWithoutNotify(h.RotZ);
            _snapToggle?.SetValueWithoutNotify(h.UseSnap);
            _snapField?.SetValueWithoutNotify(h.SnapAngle);
            _originToggle?.SetValueWithoutNotify(h.UseOriginPivot);
        }

        private float Snap(float v) { var h = GetH(); if (h == null || !h.UseSnap) return v; return Mathf.Round(v / h.SnapAngle) * h.SnapAngle; }

        private static Label Header(string t) { var l = new Label(t); l.style.marginTop = 4; l.style.marginBottom = 3; l.style.color = new StyleColor(new Color(0.85f, 0.85f, 0.85f)); return l; }
        private static Label InfoLabel() { var l = new Label(); l.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f)); l.style.fontSize = 10; l.style.marginBottom = 2; return l; }
        private static Slider MakeSlider(string label, float min, float max, float init, Action<float> onChange) { var s = new Slider(label, min, max) { value = init }; s.style.marginBottom = 3; s.RegisterValueChangedCallback(e => onChange(e.newValue)); return s; }
    }
}
