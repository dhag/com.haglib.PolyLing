// PlayerSculptSubPanel.cs
// スカルプトツール用サブパネル（Player ビルド用）。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerSculptSubPanel
    {
        // ================================================================
        // 外部注入
        // ================================================================

        public Func<SculptToolHandler> GetHandler;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private RadioButtonGroup _modeGroup;
        private Slider      _brushRadiusSlider;
        private FloatField  _brushRadiusField;
        private DropdownField _falloffDropdown;
        private Slider      _strengthSlider;
        private FloatField  _strengthField;
        private Toggle      _invertToggle;
        private HelpBox     _helpBox;
        private Button      _radiusDragButton;

        // 詳細設定
        private FloatField  _minRadiusField;
        private FloatField  _maxRadiusField;
        private FloatField  _minStrengthField;
        private FloatField  _maxStrengthField;

        private bool _suppressSync;

        private static readonly SculptMode[] ModeValues =
        {
            SculptMode.Draw, SculptMode.Smooth, SculptMode.Inflate, SculptMode.Flatten,
        };
        private static readonly string[] ModeNames =
        {
            "盛り上げ", "なめらか", "膨らみ", "平ら",
        };

        // フォールオフ選択肢（表示名 ↔ FalloffType の対応）
        private static readonly string[]      FalloffLabels = { "リニア", "ガウス", "円", "シャープ" };
        private static readonly FalloffType[] FalloffValues = { FalloffType.Linear, FalloffType.Gaussian, FalloffType.Sphere, FalloffType.Sharp };

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop   = 4;
            _root.style.paddingLeft  = 4;
            _root.style.paddingRight = 4;
            parent.Add(_root);

            var title = new Label("Sculpt Tool");
            title.style.color = new StyleColor(Color.white);
            title.style.marginBottom = 4;
            _root.Add(title);

            // ── モード選択 ────────────────────────────────────────────
            var modeChoices = new List<string>(ModeNames);
            _modeGroup = new RadioButtonGroup(null, modeChoices) { value = 0 };
            _modeGroup.style.marginBottom = 4;
            _modeGroup.RegisterValueChangedCallback(e =>
            {
                if (e.newValue < 0 || e.newValue >= ModeValues.Length) return;
                var h = GetHandler?.Invoke();
                if (h != null) h.Mode = ModeValues[e.newValue];
                UpdateHelp(ModeValues[e.newValue]);
            });
            _root.Add(_modeGroup);

            // ── ブラシ半径（スライダー + テキストボックス + ドラッグボタン）────
            AddSectionLabel("ブラシ半径 (Brush Radius)");

            var radiusRow = new VisualElement();
            radiusRow.style.flexDirection = FlexDirection.Row;
            radiusRow.style.marginBottom  = 3;
            _root.Add(radiusRow);

            _brushRadiusSlider = new Slider(0.05f, 1.0f) { value = 0.5f };
            _brushRadiusSlider.style.flexGrow = 1;
            _brushRadiusSlider.style.color = new StyleColor(Color.white);
            _brushRadiusSlider.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                if (h != null) h.BrushRadius = e.newValue;
                _suppressSync = true;
                _brushRadiusField?.SetValueWithoutNotify(e.newValue);
                _suppressSync = false;
            });
            radiusRow.Add(_brushRadiusSlider);

            _brushRadiusField = new FloatField { value = 0.5f };
            _brushRadiusField.style.width = 52;
            _brushRadiusField.style.color = new StyleColor(Color.white);
            _brushRadiusField.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                float clamped = h != null ? Mathf.Clamp(e.newValue, h.MinBrushRadius, h.MaxBrushRadius) : e.newValue;
                if (h != null) h.BrushRadius = clamped;
                _suppressSync = true;
                _brushRadiusSlider?.SetValueWithoutNotify(clamped);
                _brushRadiusField.SetValueWithoutNotify(clamped);
                _suppressSync = false;
            });
            radiusRow.Add(_brushRadiusField);

            _radiusDragButton = new Button(() =>
            {
                var h = GetHandler?.Invoke();
                if (h == null) return;
                h.IsRadiusDragMode = true;
                h.OnRadiusChanged  = r =>
                {
                    _suppressSync = true;
                    _brushRadiusSlider?.SetValueWithoutNotify(r);
                    _brushRadiusField?.SetValueWithoutNotify(r);
                    _suppressSync = false;
                };
                UpdateRadiusDragButtonStyle(true);
                // ドラッグ終了はハンドラー側で IsRadiusDragMode = false にするため、
                // 次フレームの Refresh() でボタンスタイルが戻る
            });
            _radiusDragButton.text = "ドラッグで範囲指定";
            _radiusDragButton.style.marginBottom = 3;
            _radiusDragButton.style.fontSize     = 10;
            _root.Add(_radiusDragButton);

            // ── フォールオフ ─────────────────────────────────────────
            _falloffDropdown = new DropdownField("フォールオフ", new List<string>(FalloffLabels), 1);
            _falloffDropdown.style.color = new StyleColor(Color.white);
            _falloffDropdown.style.marginBottom = 3;
            _falloffDropdown.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h == null) return;
                int idx = System.Array.IndexOf(FalloffLabels, e.newValue);
                if (idx >= 0) h.Falloff = FalloffValues[idx];
            });
            _root.Add(_falloffDropdown);

            // ── 強度（スライダー + テキストボックス）────────────────
            AddSectionLabel("強度 (Strength)");

            var strengthRow = new VisualElement();
            strengthRow.style.flexDirection = FlexDirection.Row;
            strengthRow.style.marginBottom  = 3;
            _root.Add(strengthRow);

            _strengthSlider = new Slider(0.01f, 0.05f) { value = 0.1f };
            _strengthSlider.style.flexGrow = 1;
            _strengthSlider.style.color = new StyleColor(Color.white);
            _strengthSlider.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                if (h != null) h.Strength = e.newValue;
                _suppressSync = true;
                _strengthField?.SetValueWithoutNotify(e.newValue);
                _suppressSync = false;
            });
            strengthRow.Add(_strengthSlider);

            _strengthField = new FloatField { value = 0.1f };
            _strengthField.style.width = 52;
            _strengthField.style.color = new StyleColor(Color.white);
            _strengthField.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                float clamped = h != null ? Mathf.Clamp(e.newValue, h.MinStrength, h.MaxStrength) : e.newValue;
                if (h != null) h.Strength = clamped;
                _suppressSync = true;
                _strengthSlider?.SetValueWithoutNotify(clamped);
                _strengthField.SetValueWithoutNotify(clamped);
                _suppressSync = false;
            });
            strengthRow.Add(_strengthField);

            // ── 反転 ─────────────────────────────────────────────────
            _invertToggle = new Toggle("反転 (Invert)") { value = false };
            _invertToggle.style.color = new StyleColor(Color.white);
            _invertToggle.style.marginBottom = 4;
            _invertToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.Invert = e.newValue;
            });
            _root.Add(_invertToggle);

            // ── 詳細設定（折りたたみ）─────────────────────────────────
            var foldout = new Foldout { text = "詳細設定", value = false };
            foldout.style.color = new StyleColor(Color.white);
            _root.Add(foldout);

            AddSectionLabel("半径範囲", foldout.contentContainer);

            var minRow = new VisualElement();
            minRow.style.flexDirection = FlexDirection.Row;
            minRow.style.alignItems    = Align.Center;
            minRow.style.marginBottom  = 2;
            foldout.contentContainer.Add(minRow);
            var minLabel = new Label("最小値");
            minLabel.style.color = new StyleColor(Color.white);
            minLabel.style.width = 50;
            minRow.Add(minLabel);
            _minRadiusField = new FloatField { value = 0.05f };
            _minRadiusField.style.flexGrow = 1;
            _minRadiusField.style.color = new StyleColor(Color.white);
            _minRadiusField.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h == null) return;
                float v = Mathf.Max(0.001f, e.newValue);
                h.MinBrushRadius = v;
                _brushRadiusSlider.lowValue = v;
                _minRadiusField.SetValueWithoutNotify(v);
            });
            minRow.Add(_minRadiusField);

            var maxRow = new VisualElement();
            maxRow.style.flexDirection = FlexDirection.Row;
            maxRow.style.alignItems    = Align.Center;
            maxRow.style.marginBottom  = 2;
            foldout.contentContainer.Add(maxRow);
            var maxLabel = new Label("最大値");
            maxLabel.style.color = new StyleColor(Color.white);
            maxLabel.style.width = 50;
            maxRow.Add(maxLabel);
            _maxRadiusField = new FloatField { value = 1.0f };
            _maxRadiusField.style.flexGrow = 1;
            _maxRadiusField.style.color = new StyleColor(Color.white);
            _maxRadiusField.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h == null) return;
                float v = Mathf.Max(h.MinBrushRadius + 0.001f, e.newValue);
                h.MaxBrushRadius = v;
                _brushRadiusSlider.highValue = v;
                _maxRadiusField.SetValueWithoutNotify(v);
            });
            maxRow.Add(_maxRadiusField);

            // 強度範囲
            AddSectionLabel("強度範囲", foldout.contentContainer);

            var minStrRow = new VisualElement();
            minStrRow.style.flexDirection = FlexDirection.Row;
            minStrRow.style.alignItems    = Align.Center;
            minStrRow.style.marginBottom  = 2;
            foldout.contentContainer.Add(minStrRow);
            var minStrLabel = new Label("最小値");
            minStrLabel.style.color = new StyleColor(Color.white);
            minStrLabel.style.width = 50;
            minStrRow.Add(minStrLabel);
            _minStrengthField = new FloatField { value = 0.01f };
            _minStrengthField.style.flexGrow = 1;
            _minStrengthField.style.color = new StyleColor(Color.white);
            _minStrengthField.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h == null) return;
                float v = Mathf.Max(0.001f, e.newValue);
                h.MinStrength = v;
                _strengthSlider.lowValue = v;
                _minStrengthField.SetValueWithoutNotify(v);
            });
            minStrRow.Add(_minStrengthField);

            var maxStrRow = new VisualElement();
            maxStrRow.style.flexDirection = FlexDirection.Row;
            maxStrRow.style.alignItems    = Align.Center;
            maxStrRow.style.marginBottom  = 2;
            foldout.contentContainer.Add(maxStrRow);
            var maxStrLabel = new Label("最大値");
            maxStrLabel.style.color = new StyleColor(Color.white);
            maxStrLabel.style.width = 50;
            maxStrRow.Add(maxStrLabel);
            _maxStrengthField = new FloatField { value = 0.05f };
            _maxStrengthField.style.flexGrow = 1;
            _maxStrengthField.style.color = new StyleColor(Color.white);
            _maxStrengthField.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h == null) return;
                float v = Mathf.Max(h.MinStrength + 0.001f, e.newValue);
                h.MaxStrength = v;
                _strengthSlider.highValue = v;
                _maxStrengthField.SetValueWithoutNotify(v);
            });
            maxStrRow.Add(_maxStrengthField);

            // ── ヘルプ ───────────────────────────────────────────────
            _helpBox = new HelpBox("", HelpBoxMessageType.Info);
            _helpBox.style.color = new StyleColor(Color.white);
            _helpBox.style.backgroundColor = new StyleColor(new Color(0.18f, 0.18f, 0.22f));
            _root.Add(_helpBox);

            UpdateHelp(SculptMode.Draw);
        }

        // ================================================================
        // 更新
        // ================================================================

        public void Refresh()
        {
            var h = GetHandler?.Invoke();
            if (h == null) return;

            int modeIdx = System.Array.IndexOf(ModeValues, h.Mode);
            _modeGroup?.SetValueWithoutNotify(modeIdx >= 0 ? modeIdx : 0);

            _suppressSync = true;
            _brushRadiusSlider?.SetValueWithoutNotify(h.BrushRadius);
            _brushRadiusField?.SetValueWithoutNotify(h.BrushRadius);
            _suppressSync = false;

            // フォールオフ
            if (_falloffDropdown != null)
            {
                int fidx = System.Array.IndexOf(FalloffValues, h.Falloff);
                _falloffDropdown.SetValueWithoutNotify(fidx >= 0 ? FalloffLabels[fidx] : FalloffLabels[1]);
            }

            _invertToggle?.SetValueWithoutNotify(h.Invert);
            _minRadiusField?.SetValueWithoutNotify(h.MinBrushRadius);
            _maxRadiusField?.SetValueWithoutNotify(h.MaxBrushRadius);

            if (_brushRadiusSlider != null)
            {
                _brushRadiusSlider.lowValue  = h.MinBrushRadius;
                _brushRadiusSlider.highValue = h.MaxBrushRadius;
            }

            _suppressSync = true;
            _strengthSlider?.SetValueWithoutNotify(h.Strength);
            _strengthField?.SetValueWithoutNotify(h.Strength);
            _suppressSync = false;

            if (_strengthSlider != null)
            {
                _strengthSlider.lowValue  = h.MinStrength;
                _strengthSlider.highValue = h.MaxStrength;
            }

            _minStrengthField?.SetValueWithoutNotify(h.MinStrength);
            _maxStrengthField?.SetValueWithoutNotify(h.MaxStrength);

            UpdateRadiusDragButtonStyle(h.IsRadiusDragMode);
            UpdateHelp(h.Mode);
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void UpdateRadiusDragButtonStyle(bool active)
        {
            if (_radiusDragButton == null) return;
            _radiusDragButton.style.backgroundColor = active
                ? new StyleColor(new Color(0.3f, 0.6f, 1.0f, 0.8f))
                : new StyleColor(StyleKeyword.Null);
        }

        private void UpdateHelp(SculptMode mode)
        {
            if (_helpBox == null) return;
            _helpBox.text = mode switch
            {
                SculptMode.Draw    => "ドラッグで表面を盛り上げ/盛り下げ",
                SculptMode.Smooth  => "ドラッグで表面を滑らかにする",
                SculptMode.Inflate => "ドラッグで膨らませる/縮ませる",
                SculptMode.Flatten => "ドラッグで表面を平らにする",
                _                  => "",
            };
        }

        private void AddSectionLabel(string text, VisualElement target = null)
        {
            var l = new Label(text);
            l.style.color     = new StyleColor(Color.white);
            l.style.fontSize  = 10;
            l.style.marginTop = 4;
            (target ?? _root).Add(l);
        }

        private Slider MakeSlider(string label, float min, float max, float init, Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = init };
            s.style.color = new StyleColor(Color.white);
            s.style.marginBottom = 3;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }
    }
}
