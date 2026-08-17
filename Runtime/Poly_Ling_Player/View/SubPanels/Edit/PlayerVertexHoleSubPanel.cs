// PlayerVertexHoleSubPanel.cs
// VertexHoleTool の Player 版サブパネル（UIToolkit）。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    public class PlayerVertexHoleSubPanel
    {
        public Func<VertexHoleToolHandler> GetH;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Slider        _ratioSlider;
        private Label         _targetLabel;
        private Label         _statusLabel;
        private Button        _holeBtn;

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

            _root.Add(Header("Vertex Hole / 頂点に穴あけ"));
            _root.Add(new HelpBox(
                "選択した頂点を消して穴を開けます。\n" +
                "頂点につながる各辺の上に新しい頂点を作り、元の面を張り替えます。\n" +
                "複数オブジェクト・複数頂点に対応。同じ面を共有する頂点どうしは干渉するため除外します。",
                HelpBoxMessageType.Info));

            // 位置比率
            float rMin = ParameterLimits.GetF("VertexHole.Ratio.Min");
            float rMax = ParameterLimits.GetF("VertexHole.Ratio.Max");
            _ratioSlider = new Slider("位置比率", rMin, rMax) { value = 0.5f };
            _ratioSlider.style.marginBottom = 3;
            _ratioSlider.tooltip = "1.00 が選択頂点の位置、0 が辺の反対側（根元）の位置。小さいほど穴が大きくなります。";
            _ratioSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetH?.Invoke();
                if (h != null) h.Ratio = e.newValue;
                UpdateStats();
            });
            _root.Add(_ratioSlider);

            _targetLabel = InfoLabel();
            _root.Add(_targetLabel);

            _statusLabel = InfoLabel();
            _root.Add(_statusLabel);

            _holeBtn = new Button(() =>
            {
                GetH?.Invoke()?.TriggerHole();
                Refresh();
            })
            { text = "穴あけ実行" };
            _holeBtn.style.height    = 30;
            _holeBtn.style.marginTop = 6;
            _root.Add(_holeBtn);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH?.Invoke();
            if (h == null) return;

            _ratioSlider?.SetValueWithoutNotify(h.Ratio);
            UpdateStats();
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void UpdateStats()
        {
            var h = GetH?.Invoke();
            if (h == null) return;

            var info = h.Inspect();

            if (_ratioSlider != null)
                _ratioSlider.label = $"位置比率 ({h.Ratio:F2})";

            if (!info.CanExecute)
            {
                if (_targetLabel != null)
                    _targetLabel.text = $"選択中: {h.SelectedVertexCount} 頂点  /  除外: {info.SkippedCount} 頂点";
                if (_statusLabel != null) _statusLabel.text = info.Reason ?? "";
                _holeBtn?.SetEnabled(false);
                return;
            }

            if (_targetLabel != null)
                _targetLabel.text = $"対象: {info.ObjectCount} オブジェクト / {info.TargetCount} 頂点"
                                  + (info.SkippedCount > 0 ? $"  （干渉で除外 {info.SkippedCount}）" : "");

            if (_statusLabel != null)
                _statusLabel.text = $"新しい頂点を {info.NeighborTotal} 個作り、{info.FaceTotal} 面を張り替えます";

            _holeBtn?.SetEnabled(true);
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

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            return l;
        }
    }
}
