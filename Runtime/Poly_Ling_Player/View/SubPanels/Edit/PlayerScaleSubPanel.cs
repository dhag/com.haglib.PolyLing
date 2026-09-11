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
        // UI 自動操作の ID は "scale.<下の Id>"（UiControlAttribute.cs）。
        // 倍率の変更はプレビューで、確定は「Apply」。Uniform オンは XYZ、オフは X/Y/Z を表示する。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("factor.x", Reveal = nameof(RevealPerAxis), Description = "X 方向の倍率。プレビューで、Apply で確定")]
        private Slider _sliderX;
        [UiControl("factor.y", Reveal = nameof(RevealPerAxis), Description = "Y 方向の倍率。プレビューで、Apply で確定")]
        private Slider _sliderY;
        [UiControl("factor.z", Reveal = nameof(RevealPerAxis), Description = "Z 方向の倍率。プレビューで、Apply で確定")]
        private Slider _sliderZ;
        [UiControl("factor.xyz", Reveal = nameof(RevealUniform), Description = "XYZ 共通の倍率。プレビューで、Apply で確定")]
        private Slider _sliderXYZ;
        [UiControl("uniform", Description = "XYZ を同じ倍率にする")]
        private Toggle _uniformToggle;
        [UiControl("aroundOrigin", Description = "オブジェクトの原点を中心に拡大縮小する")]
        private Toggle _originToggle;
        [UiControl("magnet.enabled", Description = "マグネット（周囲の頂点も減衰付きで拡大縮小する）を使う")]
        private Toggle _magnetToggle;
        [UiControl("magnet.radius", Description = "マグネットの半径")]
        private Slider _magnetRadius;
        [UiControl("magnet.falloff", Description = "マグネットの減衰の形")]
        private EnumField _magnetFalloff;
        [UiControl("magnet.distanceMode", Description = "マグネットの距離の測り方")]
        private EnumField _magnetDistance;
        [UiControl("axis.x", Description = "スケール軸（フレーム）の X 回転（度）")]
        private Slider _axisX;
        [UiControl("axis.y", Description = "スケール軸（フレーム）の Y 回転（度）")]
        private Slider _axisY;
        [UiControl("axis.z", Description = "スケール軸（フレーム）の Z 回転（度）")]
        private Slider _axisZ;
        [UiControl("targetCount", Safety = UiSafety.ReadOnly, Description = "拡大縮小の影響を受ける頂点数")]
        private Label _targetLabel;
        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "プレビュー中の拡大縮小を確定する")]
        private Button _applyBtn;
        [UiControl("reset", Safety = UiSafety.SafeWrite, Description = "確定していないプレビューの拡大縮小を戻す")]
        private Button _revertBtn;

        // スライダー併設の数値入力欄。スライダーと双方向同期する。
        [UiControl("factor.xyzValue", Reveal = nameof(RevealUniform), Description = "XYZ 共通の倍率の数値入力")]
        private FloatField _fieldXYZ;
        [UiControl("factor.xValue", Reveal = nameof(RevealPerAxis), Description = "X 方向の倍率の数値入力")]
        private FloatField _fieldX;
        [UiControl("factor.yValue", Reveal = nameof(RevealPerAxis), Description = "Y 方向の倍率の数値入力")]
        private FloatField _fieldY;
        [UiControl("factor.zValue", Reveal = nameof(RevealPerAxis), Description = "Z 方向の倍率の数値入力")]
        private FloatField _fieldZ;
        [UiControl("axis.xValue", Description = "スケール軸の X 回転の数値入力（度）")]
        private FloatField _fieldAxisX;
        [UiControl("axis.yValue", Description = "スケール軸の Y 回転の数値入力（度）")]
        private FloatField _fieldAxisY;
        [UiControl("axis.zValue", Description = "スケール軸の Z 回転の数値入力（度）")]
        private FloatField _fieldAxisZ;

        // スライダー行のコンテナ（Uniform 切替で表示を出し分けるため保持する）。
        [UiControl(Ignore = true)]
        private VisualElement _rowXYZ;
        [UiControl(Ignore = true)]
        private VisualElement _rowX;
        [UiControl(Ignore = true)]
        private VisualElement _rowY;
        [UiControl(Ignore = true)]
        private VisualElement _rowZ;

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
            // 確定はコマンド経由。CommitViaCommand が開始状態へ戻して
            // ScaleSelectionCommand を送り、その受け口がベイクと Undo 記録を行う。
            foreach (var s in new[] { _sliderXYZ, _sliderX, _sliderY, _sliderZ })
                s.RegisterCallback<PointerUpEvent>(_ => { GetH()?.CommitViaCommand(); Refresh(); });

            _fieldXYZ = new FloatField(); _fieldX = new FloatField();
            _fieldY   = new FloatField(); _fieldZ = new FloatField();
            // 数値欄はプレビューのみ。確定（＝ベイクと Undo 記録）は Apply ボタンだけが行う。
            _rowXYZ = SliderWithField(_sliderXYZ, _fieldXYZ, 0.01f, 5f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleX = v; h.ScaleY = v; h.ScaleZ = v; });
            _rowX = SliderWithField(_sliderX, _fieldX, 0.01f, 5f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleX = v; });
            _rowY = SliderWithField(_sliderY, _fieldY, 0.01f, 5f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleY = v; });
            _rowZ = SliderWithField(_sliderZ, _fieldZ, 0.01f, 5f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleZ = v; });
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
                s.RegisterCallback<PointerUpEvent>(_ => { GetH()?.CommitViaCommand(); Refresh(); });

            _fieldAxisX = new FloatField(); _fieldAxisY = new FloatField(); _fieldAxisZ = new FloatField();
            _root.Add(SliderWithField(_axisX, _fieldAxisX, -180f, 180f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleAxisX = v; }));
            _root.Add(SliderWithField(_axisY, _fieldAxisY, -180f, 180f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleAxisY = v; }));
            _root.Add(SliderWithField(_axisZ, _fieldAxisZ, -180f, 180f,
                v => { var h = GetH(); if (h == null) return; h.BeginSliderDrag(); h.ScaleAxisZ = v; }));

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
            var applyBtn = new Button(() => { GetH()?.CommitViaCommand(); Refresh(); }) { text = "Apply" }; applyBtn.style.flexGrow = 1; applyBtn.style.marginRight = 2;
            // 確定後はスケールが 1 に戻るので、Refresh で表示も 1 へ揃う
            // （CommitViaCommand が取り出し時に 1 へ戻し、受け口も終了時に 1 へ戻す）。
            var revertBtn = new Button(() => { GetH()?.Revert(); Refresh(); }) { text = "Reset" }; revertBtn.style.flexGrow = 1;
            btnRow.Add(applyBtn); btnRow.Add(revertBtn); _root.Add(btnRow);
            _applyBtn  = applyBtn;
            _revertBtn = revertBtn;
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
        ///
        /// 【確定は Apply ボタンだけ】
        /// 数値欄は値を入れてもプレビューを更新するだけで、ベイクも Undo 記録もしない。
        /// 旧実装は欄の変更ごとに EndSliderDrag（= ApplyScale）まで走らせていたため、
        /// 入力途中の桁がそのまま確定・ベイクされ、続く入力がその上に積まれていた。
        /// スライダーは従来どおりポインタアップで確定する（終端が明確なため）。
        /// </summary>
        /// <summary>
        /// UI 自動操作の表示の下準備（UiControl の Reveal）。XYZ の行は Uniform がオンのときだけ
        /// 表示されるので、利用者と同じくチェックを入れる。既にオンなら false。
        /// </summary>
        private bool RevealUniform()
        {
            if (_uniformToggle == null || _uniformToggle.value) return false;
            _uniformToggle.value = true;
            return true;
        }

        /// <summary>X/Y/Z の行は Uniform がオフのときだけ表示される。既にオフなら false。</summary>
        private bool RevealPerAxis()
        {
            if (_uniformToggle == null || !_uniformToggle.value) return false;
            _uniformToggle.value = false;
            return true;
        }

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
