// PlayerSurfaceSnapSubPanel.cs
// SurfaceSnapTool（面に張り付け）の Player 版サブパネル（UIToolkit）。
// リファレンスを選び、カメラを決めて「計算」→ スライダーで確認 →「決定」。
// Runtime/Poly_Ling_Player/View/SubPanels/Edit/ に配置
//
// 【計算とスライダーを分ける理由】
//   行き先は「計算」時に確定させる。スライダー操作中にカメラ姿勢が変わっても
//   結果が動かないようにするため、スライダーは補間しかしない。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerSurfaceSnapSubPanel
    {
        public Func<SurfaceSnapToolHandler> GetH;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _targetLabel;

        private VisualElement _referenceListContainer;

        private RadioButtonGroup _cameraGroup;
        private RadioButtonGroup _backfaceGroup;
        private Toggle           _selectedOnlyToggle;
        private FloatField       _offsetField;

        private Button _computeBtn;
        private Label  _statusLabel;

        private VisualElement _previewSection;
        private Slider        _slider;
        private Label         _sliderValueLabel;
        private Button        _applyBtn;

        private readonly List<(int index, string name, int vertexCount)> _candidates
            = new List<(int, string, int)>();

        private static readonly List<string> CameraChoices =
            new List<string> { "カレント", "透視", "上面", "正面", "側面" };

        private static readonly List<string> BackfaceChoices =
            new List<string> { "裏面も対象（既定）", "表面のみ" };

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

            _root.Add(Header("Snap To Surface / 面に張り付け"));

            _root.Add(new HelpBox(
                "選択中のオブジェクトの頂点を、指定カメラ目線でリファレンスの最前面の面上へ移します。\n"
                + "もともと手前にあった頂点は後退し、奥にあった頂点は前進します。",
                HelpBoxMessageType.Info));

            _targetLabel = InfoLabel();
            _root.Add(_targetLabel);

            // ── リファレンス ─────────────────────────────────────────
            _root.Add(SmallHeader("リファレンスオブジェクト（複数可）:"));
            _referenceListContainer = new VisualElement();
            _referenceListContainer.style.marginBottom = 4;
            _root.Add(_referenceListContainer);

            // ── カメラ ───────────────────────────────────────────────
            _root.Add(SmallHeader("カメラ:"));
            _cameraGroup = new RadioButtonGroup(null, CameraChoices) { value = 0 };
            _cameraGroup.style.marginBottom = 4;
            _cameraGroup.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.CancelIfActive();
                h.CameraKind = ToCameraKind(e.newValue);
                HidePreview();
                Refresh();
            });
            _root.Add(_cameraGroup);

            // ── 対象頂点 ─────────────────────────────────────────────
            _selectedOnlyToggle = new Toggle("選択頂点のみ") { value = false };
            _selectedOnlyToggle.style.fontSize     = 10;
            _selectedOnlyToggle.style.marginBottom = 2;
            _selectedOnlyToggle.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.CancelIfActive();
                h.SelectedVerticesOnly = e.newValue;
                HidePreview();
            });
            _root.Add(_selectedOnlyToggle);

            // ── 余白 ─────────────────────────────────────────────────
            _offsetField = new FloatField("面からの余白") { value = 0f };
            _offsetField.style.fontSize     = 10;
            _offsetField.style.marginBottom = 2;
            _offsetField.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                float v = Mathf.Max(0f, e.newValue);
                if (v != e.newValue) _offsetField.SetValueWithoutNotify(v);
                h.CancelIfActive();
                h.SurfaceOffset = v;
                HidePreview();
            });
            _root.Add(_offsetField);

            // ── 裏面 ─────────────────────────────────────────────────
            _root.Add(SmallHeader("リファレンスの裏面:"));
            _backfaceGroup = new RadioButtonGroup(null, BackfaceChoices) { value = 0 };
            _backfaceGroup.style.marginBottom = 4;
            _backfaceGroup.RegisterValueChangedCallback(e =>
            {
                var h = GetH();
                if (h == null) return;
                h.CancelIfActive();
                h.Backface = e.newValue == 1
                    ? SurfaceSnapBackface.FrontOnly
                    : SurfaceSnapBackface.Both;
                HidePreview();
            });
            _root.Add(_backfaceGroup);

            // ── 計算 ─────────────────────────────────────────────────
            _computeBtn = new Button(OnComputeClicked) { text = "計算" };
            _computeBtn.style.height       = 26;
            _computeBtn.style.fontSize     = 10;
            _computeBtn.style.marginBottom = 4;
            _root.Add(_computeBtn);

            _statusLabel = new Label();
            _statusLabel.style.fontSize     = 9;
            _statusLabel.style.whiteSpace   = WhiteSpace.Normal;
            _statusLabel.style.color        = new StyleColor(new Color(0.4f, 0.8f, 1f));
            _statusLabel.style.marginBottom = 4;
            _root.Add(_statusLabel);

            // ── プレビュー ───────────────────────────────────────────
            _previewSection = new VisualElement();
            _previewSection.style.display = DisplayStyle.None;
            _root.Add(_previewSection);

            _previewSection.Add(SmallHeader("張り付け量:"));

            var slRow = new VisualElement();
            slRow.style.flexDirection = FlexDirection.Row;
            slRow.style.marginBottom  = 4;
            _slider = new Slider(0f, 1f) { value = 0f };
            _slider.style.flexGrow = 1;
            _slider.RegisterValueChangedCallback(e => OnSliderChanged(e.newValue));
            _sliderValueLabel = new Label("0.00");
            _sliderValueLabel.style.width          = 32;
            _sliderValueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            slRow.Add(_slider);
            slRow.Add(_sliderValueLabel);
            _previewSection.Add(slRow);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            _previewSection.Add(btnRow);

            _applyBtn = new Button(OnApplyClicked) { text = "決定" };
            _applyBtn.style.flexGrow    = 1;
            _applyBtn.style.marginRight = 4;
            _applyBtn.style.height      = 24;
            _applyBtn.style.fontSize    = 10;

            var cancelBtn = new Button(OnCancelClicked) { text = "キャンセル" };
            cancelBtn.style.flexGrow = 1;
            cancelBtn.style.height   = 24;
            cancelBtn.style.fontSize = 10;

            btnRow.Add(_applyBtn);
            btnRow.Add(cancelBtn);

            PlayerLayoutRoot.ApplyDarkTheme(_root);
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            var h = GetH();
            if (h == null || _targetLabel == null) return;

            BuildCandidates(h);

            int targets = h.TargetMeshCount;
            _targetLabel.text = targets > 0
                ? $"ターゲット: {targets} 個（選択中のオブジェクト）"
                : "ターゲットなし（リファレンス以外のオブジェクトを選択してください）";

            RefreshReferenceList(h);

            _cameraGroup?.SetValueWithoutNotify(ToCameraIndex(h.CameraKind));
            _selectedOnlyToggle?.SetValueWithoutNotify(h.SelectedVerticesOnly);
            _offsetField?.SetValueWithoutNotify(h.SurfaceOffset);
            _backfaceGroup?.SetValueWithoutNotify(
                h.Backface == SurfaceSnapBackface.FrontOnly ? 1 : 0);

            if (_statusLabel != null) _statusLabel.text = h.LastResult ?? "";

            if (h.IsPreviewing)
            {
                _previewSection.style.display = DisplayStyle.Flex;
                _slider?.SetValueWithoutNotify(h.Slider);
                if (_sliderValueLabel != null) _sliderValueLabel.text = h.Slider.ToString("F2");
            }
            else
            {
                HidePreview();
            }

            RefreshComputeEnabled(h);
        }

        // ================================================================
        // 候補リスト
        // ================================================================

        private void BuildCandidates(SurfaceSnapToolHandler h)
        {
            _candidates.Clear();

            var model = h.Model;
            if (model == null) return;

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var ctx = model.GetMeshContext(i);
                if (ctx?.MeshObject == null || ctx.MeshObject.VertexCount == 0) continue;
                if (ctx.Type != MeshType.Mesh &&
                    ctx.Type != MeshType.BakedMirror &&
                    ctx.Type != MeshType.MirrorSide) continue;

                _candidates.Add((i, ctx.Name, ctx.MeshObject.VertexCount));
            }

            h.PruneReferences(ContainsCandidate);
        }

        private bool ContainsCandidate(int index)
        {
            for (int i = 0; i < _candidates.Count; i++)
                if (_candidates[i].index == index) return true;
            return false;
        }

        private void RefreshReferenceList(SurfaceSnapToolHandler h)
        {
            _referenceListContainer.Clear();

            if (_candidates.Count == 0)
            {
                var empty = InfoLabel();
                empty.text = "候補オブジェクトがありません";
                _referenceListContainer.Add(empty);
                PlayerLayoutRoot.ApplyDarkTheme(_referenceListContainer);
                return;
            }

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c   = _candidates[i];
                int idx = c.index;

                var tg = new Toggle($"{c.name}  [V:{c.vertexCount}]")
                {
                    value = h.IsReference(idx)
                };
                tg.style.fontSize     = 10;
                tg.style.marginBottom = 1;
                tg.RegisterValueChangedCallback(e =>
                {
                    var hh = GetH();
                    if (hh == null) return;
                    hh.SetReference(idx, e.newValue);
                    HidePreview();
                    Refresh();
                });
                _referenceListContainer.Add(tg);
            }

            PlayerLayoutRoot.ApplyDarkTheme(_referenceListContainer);
        }

        // ================================================================
        // 操作
        // ================================================================

        private void OnComputeClicked()
        {
            var h = GetH();
            if (h == null) return;

            h.TriggerCompute();

            if (h.IsPreviewing)
            {
                _previewSection.style.display = DisplayStyle.Flex;
                _slider?.SetValueWithoutNotify(0f);
                if (_sliderValueLabel != null) _sliderValueLabel.text = "0.00";
            }
            else
            {
                HidePreview();
            }

            if (_statusLabel != null)
            {
                _statusLabel.style.color = h.IsPreviewing
                    ? new StyleColor(new Color(0.4f, 0.8f, 1f))
                    : new StyleColor(new Color(1f, 0.4f, 0.4f));
                _statusLabel.text = h.LastResult ?? "";
            }

            RefreshComputeEnabled(h);
        }

        private void OnSliderChanged(float newValue)
        {
            var h = GetH();
            if (h == null || !h.IsPreviewing) return;

            if (_sliderValueLabel != null) _sliderValueLabel.text = newValue.ToString("F2");
            h.SetSlider(newValue);
        }

        private void OnApplyClicked()
        {
            var h = GetH();
            if (h == null || !h.IsPreviewing) return;

            h.TriggerApply();
            HidePreview();
            Refresh();
        }

        private void OnCancelClicked()
        {
            var h = GetH();
            if (h == null) return;

            h.TriggerCancel();
            HidePreview();
            Refresh();
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void HidePreview()
        {
            if (_previewSection != null) _previewSection.style.display = DisplayStyle.None;
            _slider?.SetValueWithoutNotify(0f);
            if (_sliderValueLabel != null) _sliderValueLabel.text = "0.00";
        }

        private void RefreshComputeEnabled(SurfaceSnapToolHandler h)
        {
            if (_computeBtn == null) return;
            if (h == null) { _computeBtn.SetEnabled(false); return; }

            _computeBtn.SetEnabled(h.TargetMeshCount > 0 && h.ReferenceIndices.Count > 0);
        }

        private static SurfaceSnapCameraKind ToCameraKind(int index)
        {
            switch (index)
            {
                case 1:  return SurfaceSnapCameraKind.Perspective;
                case 2:  return SurfaceSnapCameraKind.Top;
                case 3:  return SurfaceSnapCameraKind.Front;
                case 4:  return SurfaceSnapCameraKind.Side;
                default: return SurfaceSnapCameraKind.Current;
            }
        }

        private static int ToCameraIndex(SurfaceSnapCameraKind kind)
        {
            switch (kind)
            {
                case SurfaceSnapCameraKind.Perspective: return 1;
                case SurfaceSnapCameraKind.Top:         return 2;
                case SurfaceSnapCameraKind.Front:       return 3;
                case SurfaceSnapCameraKind.Side:        return 4;
                default:                                return 0;
            }
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
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            l.style.whiteSpace   = WhiteSpace.Normal;
            return l;
        }

        private static Label InfoLabel()
        {
            var l = new Label();
            l.style.fontSize     = 10;
            l.style.marginBottom = 2;
            l.style.whiteSpace   = WhiteSpace.Normal;
            return l;
        }
    }
}
