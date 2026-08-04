// PlayerSolidifySubPanel.cs
// SolidifyToolHandler（厚み付け）用のサブパネル（UIToolkit）。
// エッジ（角処理）のパラメータ構成は 2D押し出し（Profile2D）と同じ。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Poly_Ling.Player
{
    public class PlayerSolidifySubPanel
    {
        public Func<SolidifyToolHandler> GetH;

        private VisualElement _root;
        private Label         _infoLabel;
        private Label         _resultLabel;

        private FloatField    _thicknessField;
        private Toggle        _addToExistingToggle;

        private SliderInt     _segFrontSlider;
        private SliderInt     _segBackSlider;
        private VisualElement _edgeParamsGroup;
        private FloatField    _edgeFrontField;
        private FloatField    _edgeBackField;
        private Toggle        _edgeInwardToggle;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop = _root.style.paddingLeft =
            _root.style.paddingRight = _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("Solidify / 厚み付け"));
            _root.Add(new HelpBox(
                "選択した薄い面群に厚みを付けます。表裏2枚のコピーを厚みの半分ずつ移動し、" +
                "孤立エッジを側面でつなぎます。元の面はそのまま残ります。",
                HelpBoxMessageType.Info));

            _infoLabel = InfoLabel("選択面: 0");
            _root.Add(_infoLabel);

            // ── 厚み ───────────────────────────────────────────────────
            var thickRow = MakeLabeledRow("厚み:");
            _thicknessField = new FloatField { value = 0.1f };
            _thicknessField.style.flexGrow = 1;
            _thicknessField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.Thickness = e.newValue;
                _thicknessField.SetValueWithoutNotify(h.Thickness);
            });
            thickRow.Add(_thicknessField);
            _root.Add(thickRow);

            // ── 追加先 ─────────────────────────────────────────────────
            _addToExistingToggle = new Toggle("既存メッシュに追加") { value = false };
            _addToExistingToggle.style.marginTop = 3;
            _addToExistingToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.AddToExisting = e.newValue;
            });
            _root.Add(_addToExistingToggle);

            // ── エッジ（角処理） ───────────────────────────────────────
            _root.Add(SectionLabel("エッジ（0=無効 / 1=面取り / 2以上=ラウンド）"));

            _root.Add(SmallLabel("前面エッジ分割数:"));
            _segFrontSlider = new SliderInt(0, 8) { value = 0 };
            _segFrontSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.SegmentsFront = e.newValue;
                _segFrontSlider.SetValueWithoutNotify(h.SegmentsFront);
                UpdateEdgeParamVisibility();
            });
            _root.Add(_segFrontSlider);

            _root.Add(SmallLabel("背面エッジ分割数:"));
            _segBackSlider = new SliderInt(0, 8) { value = 0 };
            _segBackSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.SegmentsBack = e.newValue;
                _segBackSlider.SetValueWithoutNotify(h.SegmentsBack);
                UpdateEdgeParamVisibility();
            });
            _root.Add(_segBackSlider);

            // 分割数が両方 0 のときは以下を隠す
            _edgeParamsGroup = new VisualElement();
            _root.Add(_edgeParamsGroup);

            var efRow = MakeLabeledRow("前面エッジサイズ:");
            _edgeFrontField = new FloatField { value = 0.02f };
            _edgeFrontField.style.flexGrow = 1;
            _edgeFrontField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.EdgeSizeFront = e.newValue;
                _edgeFrontField.SetValueWithoutNotify(h.EdgeSizeFront);
            });
            efRow.Add(_edgeFrontField);
            _edgeParamsGroup.Add(efRow);

            var ebRow = MakeLabeledRow("背面エッジサイズ:");
            _edgeBackField = new FloatField { value = 0.02f };
            _edgeBackField.style.flexGrow = 1;
            _edgeBackField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.EdgeSizeBack = e.newValue;
                _edgeBackField.SetValueWithoutNotify(h.EdgeSizeBack);
            });
            ebRow.Add(_edgeBackField);
            _edgeParamsGroup.Add(ebRow);

            _edgeInwardToggle = new Toggle("内向きエッジ") { value = false };
            _edgeInwardToggle.style.marginTop = 3;
            _edgeInwardToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h != null) h.EdgeInward = e.newValue;
            });
            _edgeParamsGroup.Add(_edgeInwardToggle);

            _edgeParamsGroup.Add(SmallLabel(
                "エッジサイズは厚みの半分未満に丸められます。"));

            UpdateEdgeParamVisibility();

            // ── 実行 ───────────────────────────────────────────────────
            var execBtn = new Button(() =>
            {
                GetH()?.Execute();
                Refresh();
            }) { text = "厚み付け実行" };
            execBtn.style.height    = 30;
            execBtn.style.marginTop = 6;
            _root.Add(execBtn);

            _resultLabel = InfoLabel("");
            _root.Add(_resultLabel);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH();
            if (h == null) return;

            if (_infoLabel != null)
                _infoLabel.text = $"選択面: {h.SelectedFaceCount}";

            if (_resultLabel != null)
                _resultLabel.text = h.LastMessage ?? "";

            _thicknessField?.SetValueWithoutNotify(h.Thickness);
            _addToExistingToggle?.SetValueWithoutNotify(h.AddToExisting);
            _segFrontSlider?.SetValueWithoutNotify(h.SegmentsFront);
            _segBackSlider?.SetValueWithoutNotify(h.SegmentsBack);
            _edgeFrontField?.SetValueWithoutNotify(h.EdgeSizeFront);
            _edgeBackField?.SetValueWithoutNotify(h.EdgeSizeBack);
            _edgeInwardToggle?.SetValueWithoutNotify(h.EdgeInward);

            UpdateEdgeParamVisibility();
        }

        // ================================================================
        // 内部
        // ================================================================

        private void UpdateEdgeParamVisibility()
        {
            if (_edgeParamsGroup == null) return;

            var h = GetH();
            int segF = h?.SegmentsFront ?? (_segFrontSlider?.value ?? 0);
            int segB = h?.SegmentsBack  ?? (_segBackSlider?.value  ?? 0);

            bool show = segF > 0 || segB > 0;
            _edgeParamsGroup.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.marginTop = 4;
            l.style.marginBottom = 3;
            return l;
        }

        private static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10;
            l.style.marginTop = 8;
            l.style.marginBottom = 2;
            return l;
        }

        private static Label SmallLabel(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10;
            l.style.marginTop = 2;
            return l;
        }

        private static Label InfoLabel(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 10;
            l.style.marginTop = 3;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement MakeLabeledRow(string labelText)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems    = Align.Center;
            row.style.marginTop     = 3;

            var l = new Label(labelText);
            l.style.color = new StyleColor(Color.white);
            l.style.fontSize = 11;
            l.style.width = 110;
            row.Add(l);

            return row;
        }
    }
}
