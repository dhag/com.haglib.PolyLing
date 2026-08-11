// PlayerSmoothEdgesSubPanel.cs
// SmoothEdgesTool の Player 版サブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    public class PlayerSmoothEdgesSubPanel
    {
        public Func<SmoothEdgesToolHandler> GetH;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _segmentLabel;
        private Label         _vertexLabel;
        private Slider        _strengthSlider;
        private SliderInt     _iterationsSlider;
        private Toggle        _fixEndpointsToggle;
        private Toggle        _lockX, _lockY, _lockZ;
        private Button        _smoothBtn;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop    = 4;
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("Smooth Edges / 辺を滑らかに"));
            _root.Add(new HelpBox(
                "選択した辺・線分に沿って頂点を滑らかにします。隣接は選択したチェーンだけを辿ります。",
                HelpBoxMessageType.Info));

            // 統計
            _segmentLabel = InfoLabel();
            _root.Add(_segmentLabel);

            _vertexLabel = InfoLabel();
            _root.Add(_vertexLabel);

            // 強度
            float sMin = ParameterLimits.GetF("SmoothEdges.Strength.Min");
            float sMax = ParameterLimits.GetF("SmoothEdges.Strength.Max");
            _strengthSlider = new Slider("強度", sMin, sMax) { value = 0.5f };
            _strengthSlider.style.marginBottom = 3;
            _strengthSlider.tooltip = "1 反復あたり隣接平均へ寄せる量。0 で変化なし。";
            _strengthSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetH?.Invoke();
                if (h != null) h.Strength = e.newValue;
            });
            _root.Add(_strengthSlider);

            // 反復回数
            int iMin = ParameterLimits.GetI("SmoothEdges.Iterations.Min");
            int iMax = ParameterLimits.GetI("SmoothEdges.Iterations.Max");
            _iterationsSlider = new SliderInt("反復回数", iMin, iMax) { value = 1 };
            _iterationsSlider.style.marginBottom = 3;
            _iterationsSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetH?.Invoke();
                if (h != null) h.Iterations = e.newValue;
            });
            _root.Add(_iterationsSlider);

            // 端点固定
            _fixEndpointsToggle = new Toggle("開始点・終了点を固定") { value = true };
            _fixEndpointsToggle.style.marginBottom = 3;
            _fixEndpointsToggle.tooltip =
                "選択チェーン内で次数1の頂点を動かしません。閉ループには端点が無いため影響しません。";
            _fixEndpointsToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetH?.Invoke();
                if (h == null) return;
                h.FixEndpoints = e.newValue;
                h.RefreshStats();
                UpdateStats();
            });
            _root.Add(_fixEndpointsToggle);

            // 軸ロック
            _root.Add(SmallHeader("軸ロック:"));
            var lockRow = new VisualElement();
            lockRow.style.flexDirection = FlexDirection.Row;
            lockRow.style.marginBottom  = 4;

            _lockX = MakeToggle("X", v => { var h = GetH?.Invoke(); if (h != null) h.LockX = v; });
            _lockY = MakeToggle("Y", v => { var h = GetH?.Invoke(); if (h != null) h.LockY = v; });
            _lockZ = MakeToggle("Z", v => { var h = GetH?.Invoke(); if (h != null) h.LockZ = v; });
            lockRow.Add(_lockX);
            lockRow.Add(_lockY);
            lockRow.Add(_lockZ);
            _root.Add(lockRow);

            // 実行
            _smoothBtn = new Button(() =>
            {
                GetH?.Invoke()?.TriggerSmooth();
                Refresh();
            })
            { text = "平滑化実行" };
            _smoothBtn.style.height    = 30;
            _smoothBtn.style.marginTop = 6;
            _root.Add(_smoothBtn);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH?.Invoke();
            if (h == null) return;

            h.RefreshStats();

            _strengthSlider?.SetValueWithoutNotify(h.Strength);
            _iterationsSlider?.SetValueWithoutNotify(h.Iterations);
            _fixEndpointsToggle?.SetValueWithoutNotify(h.FixEndpoints);
            _lockX?.SetValueWithoutNotify(h.LockX);
            _lockY?.SetValueWithoutNotify(h.LockY);
            _lockZ?.SetValueWithoutNotify(h.LockZ);

            UpdateStats();
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void UpdateStats()
        {
            var h = GetH?.Invoke();
            if (h == null) return;

            if (!h.StatsCalculated || h.SegmentCount == 0)
            {
                if (_segmentLabel != null) _segmentLabel.text = "辺または線分を選択してください";
                if (_vertexLabel  != null) _vertexLabel.text  = "";
                _smoothBtn?.SetEnabled(false);
                return;
            }

            if (_segmentLabel != null)
                _segmentLabel.text = $"辺・線分: {h.SegmentCount} 本  /  チェーン頂点: {h.ChainVertexCount}";

            if (_vertexLabel != null)
                _vertexLabel.text = $"端点: {h.EndpointCount}  /  移動対象: {h.MovableVertexCount} 頂点";

            _smoothBtn?.SetEnabled(h.MovableVertexCount > 0);
        }

        // ================================================================
        // ウィジェットファクトリ
        // ================================================================

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop    = 4;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label SmallHeader(string text)
        {
            var l = new Label(text);
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }

        private static Toggle MakeToggle(string label, Action<bool> onChange)
        {
            var t = new Toggle(label) { value = false };
            t.style.marginRight = 8;
            t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }
    }
}
