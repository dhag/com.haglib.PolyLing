// PlayerRotateSubPanel.cs
// 回転ツール用サブパネル。エディタ版 RotateTool.DrawSettingsUI() と同等。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerRotateSubPanel
    {
        /// <summary>ツールへの窓口（操作経路統一計画.md E）。ハンドラを直接は触らない。</summary>
        public Poly_Ling.Data.IToolSurface Surface;
        private const string Tool = "rotate";

        // UI 自動操作の ID は "rotate.<下の Id>"（UiControlAttribute.cs）。
        // 角度の変更はプレビューで、確定は「Apply」（またはスライダーを離したとき）。
        // Euler と Axis-Angle は「Axis-Angle」のオン・オフで切り替わる。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("euler.x", Reveal = nameof(RevealEuler), Description = "X 軸まわりの角度（度）。プレビューで、Apply で確定")]
        private Slider        _sliderX;
        [UiControl("euler.y", Reveal = nameof(RevealEuler), Description = "Y 軸まわりの角度（度）。プレビューで、Apply で確定")]
        private Slider        _sliderY;
        [UiControl("euler.z", Reveal = nameof(RevealEuler), Description = "Z 軸まわりの角度（度）。プレビューで、Apply で確定")]
        private Slider        _sliderZ;
        [UiControl("snap.enabled", Description = "角度をスナップ刻みに丸める")]
        private Toggle        _snapToggle;
        [UiControl("aroundOrigin", Description = "オブジェクトの原点を中心に回す")]
        private Toggle        _originToggle;
        [UiControl("snap.step", Description = "スナップ刻み（度）")]
        private FloatField    _snapField;
        [UiControl("magnet.enabled", Description = "マグネット（周囲の頂点も減衰付きで回す）を使う")]
        private Toggle        _magnetToggle;
        [UiControl("magnet.radius", Description = "マグネットの半径")]
        private Slider        _magnetRadius;
        [UiControl("magnet.falloff", Description = "マグネットの減衰の形")]
        private EnumField     _magnetFalloff;
        [UiControl("magnet.distanceMode", Description = "マグネットの距離の測り方")]
        private EnumField     _magnetDistance;
        [UiControl("axisAngle.enabled", Description = "Axis-Angle（軸と角度）で回す。オフは Euler")]
        private Toggle        _axisToggle;
        [UiControl("axisAngle.axis.x", Reveal = nameof(RevealAxisAngle), Description = "回転軸の X")]
        private FloatField    _axisX;
        [UiControl("axisAngle.axis.y", Reveal = nameof(RevealAxisAngle), Description = "回転軸の Y")]
        private FloatField    _axisY;
        [UiControl("axisAngle.axis.z", Reveal = nameof(RevealAxisAngle), Description = "回転軸の Z")]
        private FloatField    _axisZ;
        [UiControl("axisAngle.angle", Reveal = nameof(RevealAxisAngle), Description = "軸まわりの角度（度）。プレビューで、Apply で確定")]
        private Slider        _axisAngle;
        [UiControl(Ignore = true)]
        private VisualElement _eulerGroup;
        [UiControl(Ignore = true)]
        private VisualElement _axisGroup;
        [UiControl("targetCount", Safety = UiSafety.ReadOnly, Description = "回転の影響を受ける頂点数")]
        private Label         _targetLabel;
        [UiControl("pivot", Safety = UiSafety.ReadOnly, Description = "回転の中心座標")]
        private Label         _pivotLabel;
        [UiControl("apply", Safety = UiSafety.SafeWrite, Description = "プレビュー中の回転を確定する")]
        private Button        _applyBtn;
        [UiControl("reset", Safety = UiSafety.SafeWrite, Description = "確定していないプレビューの回転を戻す")]
        private Button        _revertBtn;

        // スライダー併設の数値入力欄。スライダーと双方向同期する。
        [UiControl("euler.xValue", Reveal = nameof(RevealEuler), Description = "X 軸まわりの角度の数値入力（-180〜180 に丸める）")]
        private FloatField    _fieldX;
        [UiControl("euler.yValue", Reveal = nameof(RevealEuler), Description = "Y 軸まわりの角度の数値入力（-180〜180 に丸める）")]
        private FloatField    _fieldY;
        [UiControl("euler.zValue", Reveal = nameof(RevealEuler), Description = "Z 軸まわりの角度の数値入力（-180〜180 に丸める）")]
        private FloatField    _fieldZ;
        [UiControl("axisAngle.angleValue", Reveal = nameof(RevealAxisAngle), Description = "軸まわりの角度の数値入力（-180〜180 に丸める）")]
        private FloatField    _fieldAngle;

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
            _axisToggle.RegisterValueChangedCallback(e => { Surface?.Set(Tool, "axisMode", e.newValue); UpdateModeVisibility(e.newValue); });
            _root.Add(_axisToggle);

            // Euler グループ
            _eulerGroup = new VisualElement();
            _sliderX = MakeSlider("X", -180f, 180f, 0f, v => Preview("rotX", Snap(v)));
            _sliderY = MakeSlider("Y", -180f, 180f, 0f, v => Preview("rotY", Snap(v)));
            _sliderZ = MakeSlider("Z", -180f, 180f, 0f, v => Preview("rotZ", Snap(v)));
            // 確定はコマンド経由。commitViaCommand が開始状態へ戻して
            // RotateSelectionCommand を送り、その受け口がベイクと Undo 記録を行う。
            foreach (var s in new[] { _sliderX, _sliderY, _sliderZ })
                s.RegisterCallback<PointerUpEvent>(_ => Commit());

            _fieldX = new FloatField(); _fieldY = new FloatField(); _fieldZ = new FloatField();
            _eulerGroup.Add(SliderWithField(_sliderX, _fieldX, -180f, 180f, v => Preview("rotX", Snap(v))));
            _eulerGroup.Add(SliderWithField(_sliderY, _fieldY, -180f, 180f, v => Preview("rotY", Snap(v))));
            _eulerGroup.Add(SliderWithField(_sliderZ, _fieldZ, -180f, 180f, v => Preview("rotZ", Snap(v))));
            _root.Add(_eulerGroup);

            // 軸-角度 グループ
            _axisGroup = new VisualElement();
            var axisRow = new VisualElement(); axisRow.style.flexDirection = FlexDirection.Row; axisRow.style.marginBottom = 3;
            _axisX = MakeAxisField("X", v =>
            {
                Surface?.Set(Tool, "axisVecX", v);
                if (Surface != null && Surface.GetBool(Tool, "axisMode")) Surface.Invoke(Tool, "beginSliderDragFromPanel");
            });
            _axisY = MakeAxisField("Y", v => Surface?.Set(Tool, "axisVecY", v));
            _axisZ = MakeAxisField("Z", v => Surface?.Set(Tool, "axisVecZ", v));
            _axisY.value = 1f;
            axisRow.Add(_axisX); axisRow.Add(_axisY); axisRow.Add(_axisZ);
            _axisGroup.Add(axisRow);
            _axisAngle = MakeSlider("Angle", -180f, 180f, 0f, v => Preview("axisAngle", Snap(v)));
            _axisAngle.RegisterCallback<PointerUpEvent>(_ => Commit());
            _fieldAngle = new FloatField();
            _axisGroup.Add(SliderWithField(_axisAngle, _fieldAngle, -180f, 180f, v => Preview("axisAngle", Snap(v))));
            _root.Add(_axisGroup);
            UpdateModeVisibility(false);

            var snapRow = new VisualElement();
            snapRow.style.flexDirection = FlexDirection.Row;
            snapRow.style.marginBottom  = 3;
            _snapToggle = new Toggle("Snap") { value = false };
            _snapToggle.style.color = new StyleColor(Color.white);
            _snapToggle.RegisterValueChangedCallback(e => Surface?.Set(Tool, "useSnap", e.newValue));
            _snapField = new FloatField { value = 15f };
            _snapField.style.width = 50; _snapField.style.marginLeft = 4;
            _snapField.RegisterValueChangedCallback(e => Surface?.Set(Tool, "snapAngle", Mathf.Max(0.1f, e.newValue)));
            snapRow.Add(_snapToggle); snapRow.Add(_snapField);
            _root.Add(snapRow);

            _originToggle = new Toggle("オブジェクトの原点を中心に") { value = false };
            _originToggle.style.color = new StyleColor(Color.white);
            _originToggle.RegisterValueChangedCallback(e => Surface?.Set(Tool, "useOriginPivot", e.newValue));
            _root.Add(_originToggle);

            // マグネット（比例編集）
            _magnetToggle = new Toggle("Magnet") { value = false };
            _magnetToggle.style.color = new StyleColor(Color.white);
            _magnetToggle.RegisterValueChangedCallback(e => Surface?.Set(Tool, "useMagnet", e.newValue));
            _root.Add(_magnetToggle);
            _magnetRadius = MakeSlider("Radius", 0.01f, 1f, 0.5f, v => Surface?.Set(Tool, "magnetRadius", v));
            _root.Add(_magnetRadius);
            _magnetDistance = new EnumField("Distance", DistanceMode.Euclidean);
            _magnetDistance.style.color = new StyleColor(Color.white);
            _magnetDistance.RegisterValueChangedCallback(e => Surface?.Set(Tool, "magnetDistanceMode", (DistanceMode)e.newValue));
            _root.Add(_magnetDistance);
            _magnetFalloff = new EnumField("Falloff", FalloffType.Smooth);
            _magnetFalloff.style.color = new StyleColor(Color.white);
            _magnetFalloff.RegisterValueChangedCallback(e => Surface?.Set(Tool, "magnetFalloff", (FalloffType)e.newValue));
            _root.Add(_magnetFalloff);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop     = 4;
            var applyBtn  = new Button(Commit) { text = "Apply" };
            applyBtn.style.flexGrow = 1; applyBtn.style.marginRight = 2;
            // 確定後は角度が 0 に戻るので、Refresh で表示も 0 へ揃う
            // （commitViaCommand が取り出し時に 0 へ戻し、受け口も終了時に 0 へ戻す）。
            var revertBtn = new Button(() => { Surface?.Invoke(Tool, "revert"); Refresh(); }) { text = "Reset" };
            revertBtn.style.flexGrow = 1;
            btnRow.Add(applyBtn); btnRow.Add(revertBtn);
            _root.Add(btnRow);
            _applyBtn  = applyBtn;
            _revertBtn = revertBtn;
        }

        /// <summary>
        /// UI 自動操作の表示の下準備（UiControl の Reveal）。Euler の欄は「Axis-Angle」がオフの
        /// ときだけ表示されるので、利用者と同じくチェックを外す。既にオフなら false。
        /// </summary>
        private bool RevealEuler()
        {
            if (_axisToggle == null || !_axisToggle.value) return false;
            _axisToggle.value = false;
            return true;
        }

        /// <summary>Axis-Angle の欄は「Axis-Angle」がオンのときだけ表示される。既にオンなら false。</summary>
        private bool RevealAxisAngle()
        {
            if (_axisToggle == null || _axisToggle.value) return false;
            _axisToggle.value = true;
            return true;
        }

        public void Refresh()
        {
            if (Surface == null) return;
            _targetLabel.text = $"Target: {Surface.GetInt(Tool, "affectedCount")} vertices";
            var p = Surface.Get(Tool, "pivotPublic", Vector3.zero);
            _pivotLabel.text  = $"Pivot: ({p.x:F2}, {p.y:F2}, {p.z:F2})";
            float rx = Surface.GetFloat(Tool, "rotX"), ry = Surface.GetFloat(Tool, "rotY"), rz = Surface.GetFloat(Tool, "rotZ");
            _suppressSync = true;
            _sliderX?.SetValueWithoutNotify(rx);
            _sliderY?.SetValueWithoutNotify(ry);
            _sliderZ?.SetValueWithoutNotify(rz);
            _fieldX?.SetValueWithoutNotify(rx);
            _fieldY?.SetValueWithoutNotify(ry);
            _fieldZ?.SetValueWithoutNotify(rz);
            _suppressSync = false;
            _snapToggle?.SetValueWithoutNotify(Surface.GetBool(Tool, "useSnap"));
            _snapField?.SetValueWithoutNotify(Surface.GetFloat(Tool, "snapAngle"));
            _originToggle?.SetValueWithoutNotify(Surface.GetBool(Tool, "useOriginPivot"));
            _magnetToggle?.SetValueWithoutNotify(Surface.GetBool(Tool, "useMagnet"));
            _magnetRadius?.SetValueWithoutNotify(Surface.GetFloat(Tool, "magnetRadius"));
            _magnetFalloff?.SetValueWithoutNotify(Surface.Get(Tool, "magnetFalloff", FalloffType.Smooth));
            _magnetDistance?.SetValueWithoutNotify(Surface.Get(Tool, "magnetDistanceMode", DistanceMode.Euclidean));
            bool axisMode = Surface.GetBool(Tool, "axisMode");
            _axisToggle?.SetValueWithoutNotify(axisMode);
            UpdateModeVisibility(axisMode);
            _axisX?.SetValueWithoutNotify(Surface.GetFloat(Tool, "axisVecX"));
            _axisY?.SetValueWithoutNotify(Surface.GetFloat(Tool, "axisVecY"));
            _axisZ?.SetValueWithoutNotify(Surface.GetFloat(Tool, "axisVecZ"));
            float angle = Surface.GetFloat(Tool, "axisAngle");
            _suppressSync = true;
            _axisAngle?.SetValueWithoutNotify(angle);
            _fieldAngle?.SetValueWithoutNotify(angle);
            _suppressSync = false;
        }

        /// <summary>スライダー操作：プレビューを始めて（ロックを取り）値を入れる。</summary>
        private void Preview(string param, float v)
        {
            if (Surface == null) return;
            Surface.Invoke(Tool, "beginSliderDragFromPanel");
            Surface.Set(Tool, param, v);
        }

        /// <summary>確定：プレビュー中の回転を RotateSelectionCommand として送る。</summary>
        private void Commit()
        {
            Surface?.Invoke(Tool, "commitViaCommand");
            Refresh();
        }

        private float Snap(float v)
        {
            if (Surface == null || !Surface.GetBool(Tool, "useSnap")) return v;
            float step = Surface.GetFloat(Tool, "snapAngle");
            return Mathf.Round(v / step) * step;
        }

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