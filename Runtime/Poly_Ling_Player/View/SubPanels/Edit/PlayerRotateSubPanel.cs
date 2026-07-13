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
        private Toggle        _magnetToggle;
        private Slider        _magnetRadius;
        private EnumField     _magnetFalloff, _magnetDistance;
        private Toggle        _axisToggle;
        private FloatField    _axisX, _axisY, _axisZ;
        private Slider        _axisAngle;
        private VisualElement _eulerGroup, _axisGroup;
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

            // 軸-角度 / Euler 切替
            _axisToggle = new Toggle("Axis-Angle") { value = false };
            _axisToggle.style.color = new StyleColor(Color.white);
            _axisToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.AxisMode = e.newValue; UpdateModeVisibility(e.newValue); });
            _root.Add(_axisToggle);

            // Euler グループ
            _eulerGroup = new VisualElement();
            _sliderX = MakeSlider("X", -180f, 180f, 0f, v => { GetH()?.BeginSliderDrag(); var h = GetH(); if (h != null) h.RotX = Snap(v); });
            _sliderY = MakeSlider("Y", -180f, 180f, 0f, v => { GetH()?.BeginSliderDrag(); var h = GetH(); if (h != null) h.RotY = Snap(v); });
            _sliderZ = MakeSlider("Z", -180f, 180f, 0f, v => { GetH()?.BeginSliderDrag(); var h = GetH(); if (h != null) h.RotZ = Snap(v); });
            foreach (var s in new[] { _sliderX, _sliderY, _sliderZ })
            {
                s.RegisterCallback<PointerUpEvent>(_ => GetH()?.EndSliderDrag());
                _eulerGroup.Add(s);
            }
            _root.Add(_eulerGroup);

            // 軸-角度 グループ
            _axisGroup = new VisualElement();
            var axisRow = new VisualElement(); axisRow.style.flexDirection = FlexDirection.Row; axisRow.style.marginBottom = 3;
            _axisX = MakeAxisField("X", v => { var h = GetH(); if (h != null) h.AxisVecX = v; if (GetH() != null && GetH().AxisMode) GetH().BeginSliderDrag(); });
            _axisY = MakeAxisField("Y", v => { var h = GetH(); if (h != null) h.AxisVecY = v; });
            _axisZ = MakeAxisField("Z", v => { var h = GetH(); if (h != null) h.AxisVecZ = v; });
            _axisY.value = 1f;
            axisRow.Add(_axisX); axisRow.Add(_axisY); axisRow.Add(_axisZ);
            _axisGroup.Add(axisRow);
            _axisAngle = MakeSlider("Angle", -180f, 180f, 0f, v => { GetH()?.BeginSliderDrag(); var h = GetH(); if (h != null) h.AxisAngle = Snap(v); });
            _axisAngle.RegisterCallback<PointerUpEvent>(_ => GetH()?.EndSliderDrag());
            _axisGroup.Add(_axisAngle);
            _root.Add(_axisGroup);
            UpdateModeVisibility(false);

            var snapRow = new VisualElement();
            snapRow.style.flexDirection = FlexDirection.Row;
            snapRow.style.marginBottom  = 3;
            _snapToggle = new Toggle("Snap") { value = false };
            _snapToggle.style.color = new StyleColor(Color.white);
            _snapToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.UseSnap = e.newValue; });
            _snapField = new FloatField { value = 15f };
            _snapField.style.color = new StyleColor(Color.black);
            _snapField.style.width = 50; _snapField.style.marginLeft = 4;
            _snapField.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.SnapAngle = Mathf.Max(0.1f, e.newValue); });
            snapRow.Add(_snapToggle); snapRow.Add(_snapField);
            _root.Add(snapRow);

            _originToggle = new Toggle("Origin Pivot") { value = false };
            _originToggle.style.color = new StyleColor(Color.white);
            _originToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.UseOriginPivot = e.newValue; });
            _root.Add(_originToggle);

            // マグネット（比例編集）
            _magnetToggle = new Toggle("Magnet") { value = false };
            _magnetToggle.style.color = new StyleColor(Color.white);
            _magnetToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.UseMagnet = e.newValue; });
            _root.Add(_magnetToggle);
            _magnetRadius = MakeSlider("Radius", 0.01f, 1f, 0.5f, v => { var h = GetH(); if (h != null) h.MagnetRadius = v; });
            _root.Add(_magnetRadius);
            _magnetDistance = new EnumField("Distance", DistanceMode.Euclidean);
            _magnetDistance.style.color = new StyleColor(Color.white);
            _magnetDistance.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.MagnetDistanceMode = (DistanceMode)e.newValue; });
            _root.Add(_magnetDistance);
            _magnetFalloff = new EnumField("Falloff", FalloffType.Smooth);
            _magnetFalloff.style.color = new StyleColor(Color.white);
            _magnetFalloff.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.MagnetFalloff = (FalloffType)e.newValue; });
            _root.Add(_magnetFalloff);

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
                _axisAngle?.SetValueWithoutNotify(0);
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
            _magnetToggle?.SetValueWithoutNotify(h.UseMagnet);
            _magnetRadius?.SetValueWithoutNotify(h.MagnetRadius);
            _magnetFalloff?.SetValueWithoutNotify(h.MagnetFalloff);
            _magnetDistance?.SetValueWithoutNotify(h.MagnetDistanceMode);
            bool axisMode = h.AxisMode;
            _axisToggle?.SetValueWithoutNotify(axisMode);
            UpdateModeVisibility(axisMode);
            _axisX?.SetValueWithoutNotify(h.AxisVecX);
            _axisY?.SetValueWithoutNotify(h.AxisVecY);
            _axisZ?.SetValueWithoutNotify(h.AxisVecZ);
            _axisAngle?.SetValueWithoutNotify(h.AxisAngle);
        }

        private float Snap(float v) { var h = GetH(); if (h == null || !h.UseSnap) return v; return Mathf.Round(v / h.SnapAngle) * h.SnapAngle; }

        private void UpdateModeVisibility(bool axis)
        {
            if (_eulerGroup != null) _eulerGroup.style.display = axis ? DisplayStyle.None : DisplayStyle.Flex;
            if (_axisGroup  != null) _axisGroup.style.display  = axis ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static FloatField MakeAxisField(string label, Action<float> onChange)
        {
            var f = new FloatField(label) { value = 0f };
            f.style.flexGrow = 1; f.style.marginRight = 2;
            f.style.color = new StyleColor(Color.black);
            f.RegisterValueChangedCallback(e => onChange(e.newValue));
            return f;
        }

        private static Slider MakeSlider(string label, float min, float max, float init, Action<float> onChange) { var s = new Slider(label, min, max) { value = init }; s.style.marginBottom = 3; s.RegisterValueChangedCallback(e => onChange(e.newValue)); return s; }
        private static Label Header(string t) { var l = new Label(t); l.style.marginTop = 4; l.style.marginBottom = 3; return l; }
        private static Label InfoLabel() { var l = new Label(); l.style.fontSize = 10; l.style.marginBottom = 2; return l; }

    }
}