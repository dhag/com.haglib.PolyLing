// PlayerLineExtrudeSubPanel.cs
// LineExtrudeTool の Player 版サブパネル（UIToolkit）。
// 選択ライン→ループ解析→パラメータ設定→押し出し実行 の一貫したワークフロー。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerLineExtrudeSubPanel
    {
        public Func<LineExtrudeToolHandler> GetH;
        public Func<ProjectContext>         GetView;
        public Action<PanelCommand>         SendCommand;

        /// <summary>コマンドに載せるモデル索引。</summary>
        private int ModelIndex => GetView?.Invoke()?.CurrentModelIndex ?? 0;

        /// <summary>
        /// 編集対象メッシュを 1 本だけコマンドの対象として載せる。
        /// 対象が決まらないときは null（呼び出し側が送信を止める）。
        /// </summary>
        private int[] ActiveMasterIndices()
        {
            var model = GetView?.Invoke()?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            if (model == null || mc == null) return null;
            return new[] { model.IndexOf(mc) };
        }

        // ================================================================
        // UI 要素
        // ================================================================

        // UI 自動操作の ID は "lineExtrude.<下の Id>"（UiControlAttribute.cs）。
        // エッジ（分割数・サイズ・向き）は厚みが 0.001 より大きいときだけ表示される。
        [UiControl(Ignore = true)]
        private VisualElement _root;
        [UiControl("info", Safety = UiSafety.ReadOnly, Description = "選択ライン数と検出ループ数")]
        private Label         _infoLabel;
        [UiControl(Ignore = true, Rows = true)]
        private VisualElement _loopListContainer;
        [UiControl("noLoopsHint", Safety = UiSafety.ReadOnly, Description = "ループが未検出のときの案内")]
        private Label         _noLoopsHint;
        [UiControl("analyze", Safety = UiSafety.SafeWrite, Description = "選択ラインからループを検出する（メッシュは変えない）")]
        private Button        _analyzeBtn;

        // パラメータ
        [UiControl("thickness", Description = "厚み")]
        private FloatField    _thicknessField;
        [UiControl("scale", Description = "スケール")]
        private FloatField    _scaleField;
        [UiControl("flipY", Description = "Y 軸を反転する")]
        private Toggle        _flipYToggle;
        [UiControl("addToCurrent", Description = "現在のメッシュに追加する")]
        private Toggle        _addToCurrentToggle;
        [UiControl(Ignore = true)]
        private VisualElement _edgeParamsGroup;
        [UiControl("segmentsFront", Reveal = nameof(RevealEdgeParams), Description = "前面エッジの分割数")]
        private SliderInt     _segFrontSlider;
        [UiControl("segmentsBack", Reveal = nameof(RevealEdgeParams), Description = "背面エッジの分割数")]
        private SliderInt     _segBackSlider;
        [UiControl("edgeSizeFront", Reveal = nameof(RevealEdgeParams), Description = "前面エッジサイズ")]
        private FloatField    _edgeFrontField;
        [UiControl("edgeSizeBack", Reveal = nameof(RevealEdgeParams), Description = "背面エッジサイズ")]
        private FloatField    _edgeBackField;
        [UiControl("edgeInward", Reveal = nameof(RevealEdgeParams), Description = "内向きエッジ")]
        private Toggle        _edgeInwardToggle;

        // 実行
        [UiControl("run", Safety = UiSafety.SafeWrite, Description = "押し出しを実行する")]
        private Button        _executeBtn;

        /// <summary>
        /// UI 自動操作の表示の下準備（UiControl の Reveal）。エッジの欄は厚みが 0.001 より
        /// 大きいときだけ表示されるので、利用者と同じく厚みを 0.1 にする。表示中なら false。
        /// </summary>
        private bool RevealEdgeParams()
        {
            if (_thicknessField == null || _thicknessField.value > 0.001f) return false;
            _thicknessField.value = 0.1f;
            return true;
        }

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingTop = _root.style.paddingLeft =
            _root.style.paddingRight = _root.style.paddingBottom = 4;
            parent.Add(_root);

            _root.Add(Header("Profile Solidify / プロファイル立体化"));
            _root.Add(new HelpBox(
                "辺数=2の面（ライン）を選択し、ループを解析して押し出します。",
                HelpBoxMessageType.Info));

            // ── Step 1: Analyze ────────────────────────────────────────
            _root.Add(SectionLabel("Step 1: Analyze Loops"));
            _infoLabel = InfoLabel("選択ライン: 0  /  検出ループ: 0");
            _root.Add(_infoLabel);

            var analyzeBtn = new Button(() => { GetH()?.AnalyzeLoops(); Refresh(); })
                { text = "Analyze Loops" };
            analyzeBtn.style.marginTop = 3; analyzeBtn.style.marginBottom = 4;
            _root.Add(analyzeBtn);
            _analyzeBtn = analyzeBtn;

            // ループ詳細リスト
            _loopListContainer = new VisualElement();
            _root.Add(_loopListContainer);

            _noLoopsHint = new Label("ループが未検出です");
            _noLoopsHint.style.fontSize = 10;
            _noLoopsHint.style.color    = new StyleColor(new Color(1f, 0.6f, 0.2f));
            _noLoopsHint.style.display  = DisplayStyle.None;
            _root.Add(_noLoopsHint);

            // ── Step 2: Parameters ─────────────────────────────────────
            _root.Add(SectionLabel("Step 2: Parameters"));

            // Thickness
            var thickRow = MakeLabeledRow("厚み:");
            _thicknessField = new FloatField { value = 0.1f };
            _thicknessField.style.flexGrow = 1;
            _thicknessField.RegisterValueChangedCallback(e =>
            {
                var h = GetH(); if (h != null) h.Thickness = Mathf.Max(0f, e.newValue);
                _thicknessField.SetValueWithoutNotify(Mathf.Max(0f, e.newValue));
                UpdateEdgeParamVisibility();
            });
            thickRow.Add(_thicknessField);
            _root.Add(thickRow);

            // Scale
            var scaleRow = MakeLabeledRow("スケール:");
            _scaleField = new FloatField { value = 1.0f };
            _scaleField.style.flexGrow = 1;
            _scaleField.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Max(0.001f, e.newValue);
                var h = GetH(); if (h != null) h.Scale = v;
                _scaleField.SetValueWithoutNotify(v);
            });
            scaleRow.Add(_scaleField);
            _root.Add(scaleRow);

            // FlipY
            _flipYToggle = new Toggle("Y軸反転") { value = false };
            _flipYToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.FlipY = e.newValue; });
            _root.Add(_flipYToggle);

            // エッジ設定（Thickness > 0 のときのみ表示）
            _edgeParamsGroup = new VisualElement();
            _root.Add(_edgeParamsGroup);

            _root.Add(SmallLabel("前面エッジ分割数:"));
            _segFrontSlider = new SliderInt(0, 8) { value = 0 };
            _segFrontSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetH(); if (h != null) h.SegmentsFront = e.newValue;
            });
            _edgeParamsGroup.Add(_segFrontSlider);

            var efRow = MakeLabeledRow("前面エッジサイズ:");
            _edgeFrontField = new FloatField { value = 0.1f };
            _edgeFrontField.style.flexGrow = 1;
            _edgeFrontField.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Max(0.001f, e.newValue);
                var h = GetH(); if (h != null) h.EdgeSizeFront = v;
                _edgeFrontField.SetValueWithoutNotify(v);
            });
            efRow.Add(_edgeFrontField);
            _edgeParamsGroup.Add(efRow);

            _root.Add(SmallLabel("背面エッジ分割数:"));
            _segBackSlider = new SliderInt(0, 8) { value = 0 };
            _segBackSlider.RegisterValueChangedCallback(e =>
            {
                var h = GetH(); if (h != null) h.SegmentsBack = e.newValue;
            });
            _edgeParamsGroup.Add(_segBackSlider);

            var ebRow = MakeLabeledRow("背面エッジサイズ:");
            _edgeBackField = new FloatField { value = 0.1f };
            _edgeBackField.style.flexGrow = 1;
            _edgeBackField.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Max(0.001f, e.newValue);
                var h = GetH(); if (h != null) h.EdgeSizeBack = v;
                _edgeBackField.SetValueWithoutNotify(v);
            });
            ebRow.Add(_edgeBackField);
            _edgeParamsGroup.Add(ebRow);

            _edgeInwardToggle = new Toggle("内向きエッジ") { value = false };
            _edgeInwardToggle.RegisterValueChangedCallback(e => { var h = GetH(); if (h != null) h.EdgeInward = e.newValue; });
            _edgeParamsGroup.Add(_edgeInwardToggle);

            UpdateEdgeParamVisibility();

            // ── Step 3: Execute ────────────────────────────────────────
            _root.Add(SectionLabel("Step 3: Execute"));

            _addToCurrentToggle = new Toggle("現在のメッシュに追加") { value = false };
            _root.Add(_addToCurrentToggle);

            _executeBtn = new Button(() =>
            {
                var h = GetH();
                var targets = ActiveMasterIndices();
                if (h == null || targets == null) return;

                SendCommand?.Invoke(new LineExtrudeCommand(
                    ModelIndex, targets,
                    meshName:      "LineExtrude",
                    addToCurrent:  _addToCurrentToggle?.value ?? false,
                    thickness:     h.Thickness,
                    scale:         h.Scale,
                    offset:        h.Offset,
                    flipY:         h.FlipY,
                    segmentsFront: h.SegmentsFront,
                    segmentsBack:  h.SegmentsBack,
                    edgeSizeFront: h.EdgeSizeFront,
                    edgeSizeBack:  h.EdgeSizeBack,
                    edgeInward:    h.EdgeInward));
                Refresh();
            }) { text = "押し出し実行" };
            _executeBtn.style.height    = 30;
            _executeBtn.style.marginTop = 6;
            _root.Add(_executeBtn);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH();
            if (h == null) return;

            int lineCount = h.SelectedLineCount;
            int loopCount = h.DetectedLoopCount;
            _infoLabel.text = $"選択ライン: {lineCount}  /  検出ループ: {loopCount}";

            // ループ詳細リスト更新
            _loopListContainer?.Clear();
            var summaries = h.GetLoopSummaries();
            bool hasLoops = summaries != null && summaries.Count > 0;

            if (_noLoopsHint != null)
                _noLoopsHint.style.display = hasLoops ? DisplayStyle.None : DisplayStyle.Flex;

            if (hasLoops)
            {
                foreach (var loop in summaries)
                {
                    string t = loop.IsHole ? "Hole" : "Outer";
                    var lbl = new Label($"  Loop {loop.Index + 1}: {loop.VertexCount} verts ({t})");
                    lbl.style.fontSize = 10;
                    _loopListContainer.Add(lbl);
                }
            }

            // パラメータ同期
            _thicknessField?.SetValueWithoutNotify(h.Thickness);
            _scaleField?.SetValueWithoutNotify(h.Scale);
            _flipYToggle?.SetValueWithoutNotify(h.FlipY);
            _segFrontSlider?.SetValueWithoutNotify(h.SegmentsFront);
            _segBackSlider?.SetValueWithoutNotify(h.SegmentsBack);
            _edgeFrontField?.SetValueWithoutNotify(h.EdgeSizeFront);
            _edgeBackField?.SetValueWithoutNotify(h.EdgeSizeBack);
            _edgeInwardToggle?.SetValueWithoutNotify(h.EdgeInward);

            UpdateEdgeParamVisibility();

            if (_executeBtn != null)
                _executeBtn.SetEnabled(hasLoops);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void UpdateEdgeParamVisibility()
        {
            if (_edgeParamsGroup == null) return;
            float thickness = GetH()?.Thickness ?? _thicknessField?.value ?? 0f;
            _edgeParamsGroup.style.display = thickness > 0.001f ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static VisualElement MakeLabeledRow(string labelText)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 3;
            var lbl = new Label(labelText);
            lbl.style.width = 100;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(lbl);
            return row;
        }

        private static Label Header(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginTop = 4; l.style.marginBottom = 3;
            return l;
        }

        private static Label SectionLabel(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.fontSize     = 10;
            l.style.marginTop    = 6;
            l.style.marginBottom = 2;
            return l;
        }

        private static Label InfoLabel(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 10; l.style.marginBottom = 2;
            return l;
        }

        private static Label SmallLabel(string text)
        {
            var l = new Label(text);
            l.style.fontSize = 10; l.style.marginBottom = 1;
            return l;
        }
    }
}
