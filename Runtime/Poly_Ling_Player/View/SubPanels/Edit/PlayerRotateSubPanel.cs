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

        // スライダー併設の数値入力欄。スライダーと双方向同期する。
        private FloatField    _fieldX, _fieldY, _fieldZ, _fieldAngle;

        // スライダー ⇔ 数値欄の相互更新による再入を防ぐ。
        private bool          _suppressSync;

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
                s.RegisterCallback<PointerUpEvent>(_ => { GetH()?.EndSliderDrag(); Refresh(); });

            _fieldX = new FloatField(); _fieldY = new FloatField(); _fieldZ = new FloatField();
            _eulerGroup.Add(SliderWithField(_sliderX, _fieldX, -180f, 180f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.RotX = Snap(v); }));
            _eulerGroup.Add(SliderWithField(_sliderY, _fieldY, -180f, 180f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.RotY = Snap(v); }));
            _eulerGroup.Add(SliderWithField(_sliderZ, _fieldZ, -180f, 180f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.RotZ = Snap(v); }));
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
            _axisAngle.RegisterCallback<PointerUpEvent>(_ => { GetH()?.EndSliderDrag(); Refresh(); });
            _fieldAngle = new FloatField();
            _axisGroup.Add(SliderWithField(_axisAngle, _fieldAngle, -180f, 180f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.AxisAngle = Snap(v); }));
            _root.Add(_axisGroup);
            UpdateModeVisibility(false);

            var snapRow = new VisualElement();
            snapRow.style.flexDirection = FlexDirection.Row;
            snapRow.style.marginBottom  = 3;
            _snapToggle = new Toggle("Snap") { value = false };
            _snapToggle.style.color = new StyleColor(Color.white);
            _snapToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.UseSnap = e.newValue; });
            _snapField = new FloatField { value = 15f };
            _snapField.style.width = 50; _snapField.style.marginLeft = 4;
            _snapField.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.SnapAngle = Mathf.Max(0.1f, e.newValue); });
            snapRow.Add(_snapToggle); snapRow.Add(_snapField);
            _root.Add(snapRow);

            _originToggle = new Toggle("オブジェクトの原点を中心に") { value = false };
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
            var applyBtn  = new Button(() => { GetH()?.EndSliderDrag(); Refresh(); }) { text = "Apply" };
            applyBtn.style.flexGrow = 1; applyBtn.style.marginRight = 2;
            // 確定後は EndSliderDrag が角度を 0 に戻すので、Refresh で表示も 0 へ揃う。
            var revertBtn = new Button(() => { GetH()?.Revert(); Refresh(); }) { text = "Reset" };
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
            _suppressSync = true;
            _sliderX?.SetValueWithoutNotify(h.RotX);
            _sliderY?.SetValueWithoutNotify(h.RotY);
            _sliderZ?.SetValueWithoutNotify(h.RotZ);
            _fieldX?.SetValueWithoutNotify(h.RotX);
            _fieldY?.SetValueWithoutNotify(h.RotY);
            _fieldZ?.SetValueWithoutNotify(h.RotZ);
            _suppressSync = false;
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
            _suppressSync = true;
            _axisAngle?.SetValueWithoutNotify(h.AxisAngle);
            _fieldAngle?.SetValueWithoutNotify(h.AxisAngle);
            _suppressSync = false;
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
            f.RegisterValueChangedCallback(e => onChange(e.newValue));
            return f;
        }

        /// <summary>
        /// スライダーと数値入力欄を 1 行に並べる。
        ///
        /// 【確定は Apply ボタンだけ】
        /// 数値欄は値を入れてもプレビューを更新するだけで、ベイクも Undo 記録もしない。
        /// 旧実装は欄の変更ごとに EndSliderDrag（= ApplyRotation）まで走らせていたため、
        /// 「90」と打つ途中の「9」が確定・ベイクされ、続く「90」がその上に積まれて
        /// 合計 99 度になっていた。
        /// スライダーは従来どおりポインタアップで確定する（終端が明確なため）。
        /// </summary>
        private VisualElement SliderWithField(Slider slider, FloatField field, float min, float max, Action<float> onPreview)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 3;

            slider.style.flexGrow     = 1;
            slider.style.marginBottom = 0;

            field.style.width      = 56;
            field.style.marginLeft = 4;

            slider.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                _suppressSync = true;
                field.SetValueWithoutNotify(e.newValue);
                _suppressSync = false;
            });

            field.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                float v = Mathf.Clamp(e.newValue, min, max);
                _suppressSync = true;
                field.SetValueWithoutNotify(v);
                slider.SetValueWithoutNotify(v);
                _suppressSync = false;
                onPreview(v);
            });

            row.Add(slider); row.Add(field);
            return row;
        }

        private static Slider MakeSlider(string label, float min, float max, float init, Action<float> onChange) { var s = new Slider(label, min, max) { value = init }; s.style.marginBottom = 3; s.RegisterValueChangedCallback(e => onChange(e.newValue)); return s; }
        private static Label Header(string t) { var l = new Label(t); l.style.marginTop = 4; l.style.marginBottom = 3; return l; }
        private static Label InfoLabel() { var l = new Label(); l.style.fontSize = 10; l.style.marginBottom = 2; return l; }

    }
}