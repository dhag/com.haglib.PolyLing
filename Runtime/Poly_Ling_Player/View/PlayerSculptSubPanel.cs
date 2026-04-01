// PlayerSculptSubPanel.cs
// スカルプトツール用サブパネル（Player ビルド用）。
// エディタ版 SculptTool.DrawSettingsUI() と同等の内容を UIToolkit で実装する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    /// <summary>
    /// スカルプトツールのサブパネル。
    /// モード選択・ブラシサイズ・強度・反転・ヘルプを提供する。
    /// エディタ版 SculptTool.DrawSettingsUI() と同等の内容。
    /// </summary>
    public class PlayerSculptSubPanel
    {
        // ================================================================
        // 外部注入（Viewer から設定）
        // ================================================================

        public Func<SculptToolHandler> GetHandler;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private RadioButtonGroup _modeGroup;
        private Slider        _brushRadiusSlider;
        private Slider        _strengthSlider;
        private Toggle        _invertToggle;
        private HelpBox       _helpBox;

        private static readonly SculptMode[] ModeValues =
        {
            SculptMode.Draw,
            SculptMode.Smooth,
            SculptMode.Inflate,
            SculptMode.Flatten,
        };

        private static readonly string[] ModeNames =
        {
            "盛り上げ", "なめらか", "膨らみ", "平ら",
        };

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
            title.style.marginBottom = 4;
            title.style.color        = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            _root.Add(title);

            // ── モード選択（RadioButtonGroup）────────────────────────
            // UIToolkit に SelectionGrid 相当がないため RadioButtonGroup を使う
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

            // ── ブラシサイズ ────────────────────────────────────────
            _brushRadiusSlider = MakeSlider("Brush Size",
                SculptSettings.MIN_BRUSH_RADIUS,
                SculptSettings.MAX_BRUSH_RADIUS,
                0.5f,
                v =>
                {
                    var h = GetHandler?.Invoke();
                    if (h != null) h.BrushRadius = v;
                });
            _root.Add(_brushRadiusSlider);

            // ── 強度 ────────────────────────────────────────────────
            _strengthSlider = MakeSlider("Strength",
                SculptSettings.MIN_STRENGTH,
                SculptSettings.MAX_STRENGTH,
                0.1f,
                v =>
                {
                    var h = GetHandler?.Invoke();
                    if (h != null) h.Strength = v;
                });
            _root.Add(_strengthSlider);

            // ── 反転 ────────────────────────────────────────────────
            _invertToggle = new Toggle("Invert") { value = false };
            _invertToggle.style.marginBottom = 4;
            _invertToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.Invert = e.newValue;
            });
            _root.Add(_invertToggle);

            // ── ヘルプ ───────────────────────────────────────────────
            _helpBox = new HelpBox("", HelpBoxMessageType.Info);
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
            _brushRadiusSlider?.SetValueWithoutNotify(h.BrushRadius);
            _strengthSlider?.SetValueWithoutNotify(h.Strength);
            _invertToggle?.SetValueWithoutNotify(h.Invert);
            UpdateHelp(h.Mode);
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

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

        private Slider MakeSlider(string label, float min, float max, float init,
                                  Action<float> onChange)
        {
            var s = new Slider(label, min, max) { value = init };
            s.style.marginBottom = 3;
            s.RegisterValueChangedCallback(e => onChange(e.newValue));
            return s;
        }
    }
}
