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
        private Toggle _magnetToggle;
        private Slider _magnetRadius;
        private EnumField _magnetFalloff, _magnetDistance;
        private Slider _axisX, _axisY, _axisZ;
        private Label _targetLabel;

        // スライダー併設の数値入力欄。スライダーと双方向同期する。
        private FloatField _fieldXYZ, _fieldX, _fieldY, _fieldZ;
        private FloatField _fieldAxisX, _fieldAxisY, _fieldAxisZ;

        // スライダー行のコンテナ（Uniform 切替で表示を出し分けるため保持する）。
        private VisualElement _rowXYZ, _rowX, _rowY, _rowZ;

        // スライダー ⇔ 数値欄の相互更新による再入を防ぐ。
        private bool _suppressSync;

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

            _fieldXYZ = new FloatField(); _fieldX = new FloatField();
            _fieldY   = new FloatField(); _fieldZ = new FloatField();
            // EndSliderDrag はスケール値を 1 に戻すため、確定後に Refresh で表示も 1 に戻す。
            _rowXYZ = SliderWithField(_sliderXYZ, _fieldXYZ, 0.01f, 5f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleX = v; h.ScaleY = v; h.ScaleZ = v; h.EndSliderDrag(); Refresh(); });
            _rowX = SliderWithField(_sliderX, _fieldX, 0.01f, 5f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleX = v; h.EndSliderDrag(); Refresh(); });
            _rowY = SliderWithField(_sliderY, _fieldY, 0.01f, 5f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleY = v; h.EndSliderDrag(); Refresh(); });
            _rowZ = SliderWithField(_sliderZ, _fieldZ, 0.01f, 5f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleZ = v; h.EndSliderDrag(); Refresh(); });
            _root.Add(_rowXYZ); _root.Add(_rowX); _root.Add(_rowY); _root.Add(_rowZ);
            _originToggle = new Toggle("オブジェクトの原点を中心に") { value = false }; _originToggle.RegisterValueChangedCallback(e => { if (GetH() != null) GetH().UseOriginPivot = e.newValue; });
            _originToggle.style.color = new StyleColor(Color.white);
            _root.Add(_originToggle);

            // スケール軸（フレーム回転）
            _root.Add(Header("Scale Axis (°)"));
            _axisX = MakeSlider("X", -180f, 180f, 0f, v => { GetH()?.BeginSliderDrag(); if (GetH() != null) GetH().ScaleAxisX = v; });
            _axisY = MakeSlider("Y", -180f, 180f, 0f, v => { GetH()?.BeginSliderDrag(); if (GetH() != null) GetH().ScaleAxisY = v; });
            _axisZ = MakeSlider("Z", -180f, 180f, 0f, v => { GetH()?.BeginSliderDrag(); if (GetH() != null) GetH().ScaleAxisZ = v; });
            foreach (var s in new[] { _axisX, _axisY, _axisZ })
                s.RegisterCallback<PointerUpEvent>(_ => GetH()?.EndSliderDrag());

            _fieldAxisX = new FloatField(); _fieldAxisY = new FloatField(); _fieldAxisZ = new FloatField();
            _root.Add(SliderWithField(_axisX, _fieldAxisX, -180f, 180f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleAxisX = v; h.EndSliderDrag(); Refresh(); }));
            _root.Add(SliderWithField(_axisY, _fieldAxisY, -180f, 180f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleAxisY = v; h.EndSliderDrag(); Refresh(); }));
            _root.Add(SliderWithField(_axisZ, _fieldAxisZ, -180f, 180f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleAxisZ = v; h.EndSliderDrag(); Refresh(); }));

            // マグネット（比例編集）
            _magnetToggle = new Toggle("Magnet") { value = false };
            _magnetToggle.style.color = new StyleColor(Color.white);
            _magnetToggle.RegisterValueChangedCallback(e => { if (GetH() != null) GetH().UseMagnet = e.newValue; });
            _root.Add(_magnetToggle);
            _magnetRadius = MakeSlider("Radius", 0.01f, 1f, 0.5f, v => { if (GetH() != null) GetH().MagnetRadius = v; });
            _root.Add(_magnetRadius);
            _magnetDistance = new EnumField("Distance", DistanceMode.Euclidean);
            _magnetDistance.style.color = new StyleColor(Color.white);
            _magnetDistance.RegisterValueChangedCallback(e => { if (GetH() != null) GetH().MagnetDistanceMode = (DistanceMode)e.newValue; });
            _root.Add(_magnetDistance);
            _magnetFalloff = new EnumField("Falloff", FalloffType.Smooth);
            _magnetFalloff.style.color = new StyleColor(Color.white);
            _magnetFalloff.RegisterValueChangedCallback(e => { if (GetH() != null) GetH().MagnetFalloff = (FalloffType)e.newValue; });
            _root.Add(_magnetFalloff);

            var btnRow = new VisualElement(); btnRow.style.flexDirection = FlexDirection.Row; btnRow.style.marginTop = 4;
            var applyBtn = new Button(() => GetH()?.EndSliderDrag()) { text = "Apply" }; applyBtn.style.flexGrow = 1; applyBtn.style.marginRight = 2;
            var revertBtn = new Button(() =>
            {
                GetH()?.Revert();
                _suppressSync = true;
                _sliderX?.SetValueWithoutNotify(1); _sliderY?.SetValueWithoutNotify(1);
                _sliderZ?.SetValueWithoutNotify(1); _sliderXYZ?.SetValueWithoutNotify(1);
                _fieldX?.SetValueWithoutNotify(1); _fieldY?.SetValueWithoutNotify(1);
                _fieldZ?.SetValueWithoutNotify(1); _fieldXYZ?.SetValueWithoutNotify(1);
                _suppressSync = false;
            }) { text = "Reset" }; revertBtn.style.flexGrow = 1;
            btnRow.Add(applyBtn); btnRow.Add(revertBtn); _root.Add(btnRow);
        }

        public void Refresh()
        {
            var h = GetH(); if (h == null) return;
            _targetLabel.text = $"Target: {h.GetTotalAffectedCount()} vertices";
            bool uni = h.UniformScale;
            _uniformToggle?.SetValueWithoutNotify(uni);
            // 数値欄を含む行ごと出し分ける（スライダー単体を隠すと数値欄が残るため）。
            if (_rowXYZ != null) _rowXYZ.style.display = uni ? DisplayStyle.Flex : DisplayStyle.None;
            if (_rowX   != null) _rowX.style.display   = uni ? DisplayStyle.None : DisplayStyle.Flex;
            if (_rowY   != null) _rowY.style.display   = uni ? DisplayStyle.None : DisplayStyle.Flex;
            if (_rowZ   != null) _rowZ.style.display   = uni ? DisplayStyle.None : DisplayStyle.Flex;
            _suppressSync = true;
            if (uni) { _sliderXYZ?.SetValueWithoutNotify(h.ScaleX); _fieldXYZ?.SetValueWithoutNotify(h.ScaleX); }
            else
            {
                _sliderX?.SetValueWithoutNotify(h.ScaleX); _sliderY?.SetValueWithoutNotify(h.ScaleY); _sliderZ?.SetValueWithoutNotify(h.ScaleZ);
                _fieldX?.SetValueWithoutNotify(h.ScaleX);  _fieldY?.SetValueWithoutNotify(h.ScaleY);  _fieldZ?.SetValueWithoutNotify(h.ScaleZ);
            }
            _suppressSync = false;
            _originToggle?.SetValueWithoutNotify(h.UseOriginPivot);
            _magnetToggle?.SetValueWithoutNotify(h.UseMagnet);
            _magnetRadius?.SetValueWithoutNotify(h.MagnetRadius);
            _magnetFalloff?.SetValueWithoutNotify(h.MagnetFalloff);
            _magnetDistance?.SetValueWithoutNotify(h.MagnetDistanceMode);
            _suppressSync = true;
            _axisX?.SetValueWithoutNotify(h.ScaleAxisX);
            _axisY?.SetValueWithoutNotify(h.ScaleAxisY);
            _axisZ?.SetValueWithoutNotify(h.ScaleAxisZ);
            _fieldAxisX?.SetValueWithoutNotify(h.ScaleAxisX);
            _fieldAxisY?.SetValueWithoutNotify(h.ScaleAxisY);
            _fieldAxisZ?.SetValueWithoutNotify(h.ScaleAxisZ);
            _suppressSync = false;
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

        /// <summary>
        /// スライダーと数値入力欄を 1 行に並べる。
        /// スライダー操作は数値欄へ表示同期するだけ（適用は既存の PointerUp → EndSliderDrag）。
        /// 数値欄の確定は min..max へクランプしたうえで onCommit を呼ぶ。
        /// onCommit 側で BeginSliderDrag → 値設定 → EndSliderDrag を行い、Undo 1 件にまとめる。
        /// </summary>
        private VisualElement SliderWithField(Slider slider, FloatField field, float min, float max, Action<float> onCommit)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginBottom  = 3;

            slider.style.flexGrow     = 1;
            slider.style.marginBottom = 0;

            field.style.width      = 56;
            field.style.marginLeft = 4;
            field.style.color      = new StyleColor(Color.black);

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
                onCommit(v);
            });

            row.Add(slider); row.Add(field);
            return row;
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
