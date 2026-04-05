// PlayerVertexMoveSubPanel.cs
// 頂点移動ツール用サブパネル（Player ビルド用）。
// エディタ版 MoveTool.DrawSettingsUI() と同等の内容を UIToolkit で実装する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 頂点移動ツールのサブパネル。
    /// エディタ版 MoveTool.DrawSettingsUI() と同等の内容を提供する：
    /// マグネット設定・ギズモオフセット・移動対象頂点数。
    /// </summary>
    public class PlayerVertexMoveSubPanel
    {
        // ================================================================
        // 外部注入（Viewer から設定）
        // ================================================================

        public Func<MoveToolHandler> GetHandler;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Toggle        _magnetToggle;
        private Slider        _magnetRadiusSlider;
        private DropdownField _falloffDropdown;
        private VisualElement _magnetParamsGroup;
        private Slider        _gizmoOffsetXSlider;
        private Slider        _gizmoOffsetYSlider;
        private Label         _targetLabel;
        private Toggle        _lassoToggle;

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

            // ── 選択ドラッグモード ────────────────────────────────────
            AddHeader("Select Mode");

            _lassoToggle = new Toggle("Lasso Select") { value = false };
            _lassoToggle.style.marginBottom = 3;
            _lassoToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h == null) return;
                h.DragSelectMode = e.newValue
                    ? MoveToolHandler.SelectionDragMode.Lasso
                    : MoveToolHandler.SelectionDragMode.Box;
            });
            _root.Add(_lassoToggle);

            // ── マグネット ───────────────────────────────────────────
            AddHeader("Magnet");

            _magnetToggle = new Toggle("Enable") { value = false };
            _magnetToggle.style.marginBottom = 2;
            _magnetToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.UseMagnet = e.newValue;
                SetMagnetParamsVisible(e.newValue);
            });
            _root.Add(_magnetToggle);

            _magnetParamsGroup = new VisualElement();
            _root.Add(_magnetParamsGroup);

            // Radius: 0.01 〜 1.0（MoveSettings の MIN/MAX に対応）
            _magnetRadiusSlider = MakeSlider("Radius", 0.01f, 1.0f, 0.5f, v =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.MagnetRadius = v;
            });
            _magnetParamsGroup.Add(_magnetRadiusSlider);

            _falloffDropdown = new DropdownField("Falloff",
                new List<string> { "Linear", "Smooth", "Sharp" }, 1);
            _falloffDropdown.style.marginBottom = 3;
            _falloffDropdown.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h == null) return;
                h.MagnetFalloff = e.newValue switch
                {
                    "Linear" => FalloffType.Linear,
                    "Sharp"  => FalloffType.Sharp,
                    _        => FalloffType.Smooth,
                };
            });
            _magnetParamsGroup.Add(_falloffDropdown);

            SetMagnetParamsVisible(false);

            // ── ギズモ ───────────────────────────────────────────────
            AddHeader("Gizmo");

            // Offset X/Y: -100 〜 100（MoveSettings の MIN/MAX_SCREEN_OFFSET_X/Y に対応）
            _gizmoOffsetXSlider = MakeSlider("Offset X", -100f, 100f, 60f, v =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.GizmoScreenOffsetX = v;
            });
            _root.Add(_gizmoOffsetXSlider);

            _gizmoOffsetYSlider = MakeSlider("Offset Y", -100f, 100f, -60f, v =>
            {
                var h = GetHandler?.Invoke();
                if (h != null) h.GizmoScreenOffsetY = v;
            });
            _root.Add(_gizmoOffsetYSlider);

            // ── 移動対象頂点数 ───────────────────────────────────────
            _targetLabel = new Label();
            _targetLabel.style.color        = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
            _targetLabel.style.fontSize     = 10;
            _targetLabel.style.marginTop    = 4;
            _targetLabel.style.marginBottom = 2;
            _targetLabel.style.display      = DisplayStyle.None;
            _root.Add(_targetLabel);
        }

        // ================================================================
        // 更新（選択変更時・毎フレームに呼ぶ）
        // ================================================================

        public void Refresh()
        {
            var h = GetHandler?.Invoke();
            if (h == null) return;

            _magnetToggle?.SetValueWithoutNotify(h.UseMagnet);
            _magnetRadiusSlider?.SetValueWithoutNotify(h.MagnetRadius);

            if (_lassoToggle != null)
                _lassoToggle.SetValueWithoutNotify(
                    h.DragSelectMode == MoveToolHandler.SelectionDragMode.Lasso);

            if (_falloffDropdown != null)
            {
                _falloffDropdown.SetValueWithoutNotify(h.MagnetFalloff switch
                {
                    FalloffType.Linear => "Linear",
                    FalloffType.Sharp  => "Sharp",
                    _                  => "Smooth",
                });
            }

            SetMagnetParamsVisible(h.UseMagnet);

            _gizmoOffsetXSlider?.SetValueWithoutNotify(h.GizmoScreenOffsetX);
            _gizmoOffsetYSlider?.SetValueWithoutNotify(h.GizmoScreenOffsetY);

            // 移動対象頂点数
            if (_targetLabel != null)
            {
                int count = h.GetTotalAffectedCount();
                if (count > 0)
                {
                    _targetLabel.text    = $"Target: {count} vertices";
                    _targetLabel.style.display = DisplayStyle.Flex;
                }
                else
                {
                    _targetLabel.style.display = DisplayStyle.None;
                }
            }
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void SetMagnetParamsVisible(bool v)
        {
            if (_magnetParamsGroup != null)
                _magnetParamsGroup.style.display = v ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void AddHeader(string text)
        {
            var l = new Label(text);
            l.style.marginTop    = 6;
            l.style.marginBottom = 2;
            l.style.color        = new StyleColor(Color.white);
            l.style.fontSize     = 10;
            _root.Add(l);
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
