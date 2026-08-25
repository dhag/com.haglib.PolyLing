// PlayerVertexMoveSubPanel.cs
// 頂点移動ツール用サブパネル（Player ビルド用）。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PlayerVertexMoveSubPanel
    {
        // ================================================================
        // 外部注入
        // ================================================================

        public Func<MoveToolHandler> GetHandler;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Toggle        _magnetToggle;
        private Slider        _magnetRadiusSlider;
        private FloatField    _magnetRadiusField;
        private DropdownField _falloffDropdown;
        private DropdownField _distanceModeDropdown;
        private VisualElement _magnetParamsGroup;
        private Slider        _gizmoOffsetXSlider;
        private Slider        _gizmoOffsetYSlider;
        private Label         _targetLabel;
        private Toggle        _lassoToggle;
        private Button        _radiusDragButton;

        // 詳細設定
        private FloatField _minRadiusField;
        private FloatField _maxRadiusField;

        // 数値移動 (ワールド空間の増分)
        private FloatField _moveXField;
        private FloatField _moveYField;
        private FloatField _moveZField;

        private bool _suppressSync;

        // フォールオフ／距離モードの選択肢は BrushFalloffControls に集約した。
        // スカルプト・スキンWペイントも同じものを使う。
        private static string[]      FalloffLabels      => BrushFalloffControls.FalloffLabels;
        private static FalloffType[] FalloffValues      => BrushFalloffControls.FalloffValues;
        private static string[]      DistanceModeLabels => BrushFalloffControls.DistanceModeLabels;
        private static DistanceMode[] DistanceModeValues => BrushFalloffControls.DistanceModeValues;

        /// <summary>距離モード／フォールオフの共通 UI。</summary>
        private readonly BrushFalloffControls _falloffControls = new BrushFalloffControls();

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
            _lassoToggle.style.color = new StyleColor(Color.white);
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
            _magnetToggle.style.color = new StyleColor(Color.white);
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

            // ブラシ半径（スライダー + テキストボックス）
            AddHeader("ブラシ半径 (Brush Radius)", _magnetParamsGroup);

            var radiusRow = new VisualElement();
            radiusRow.style.flexDirection = FlexDirection.Row;
            radiusRow.style.marginBottom  = 3;
            _magnetParamsGroup.Add(radiusRow);

            _magnetRadiusSlider = new Slider(0.01f, 1.0f) { value = 0.5f };
            _magnetRadiusSlider.style.flexGrow = 1;
            _magnetRadiusSlider.style.color = new StyleColor(Color.white);
            _magnetRadiusSlider.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                if (h != null) h.MagnetRadius = e.newValue;
                _suppressSync = true;
                _magnetRadiusField?.SetValueWithoutNotify(e.newValue);
                _suppressSync = false;
            });
            radiusRow.Add(_magnetRadiusSlider);

            _magnetRadiusField = new FloatField { value = 0.5f };
            _magnetRadiusField.style.width = 52;
            _magnetRadiusField.style.color = new StyleColor(Color.white);
            _magnetRadiusField.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                float clamped = h != null ? Mathf.Clamp(e.newValue, h.MinMagnetRadius, h.MaxMagnetRadius) : e.newValue;
                if (h != null) h.MagnetRadius = clamped;
                _suppressSync = true;
                _magnetRadiusSlider?.SetValueWithoutNotify(clamped);
                _magnetRadiusField.SetValueWithoutNotify(clamped);
                _suppressSync = false;
            });
            radiusRow.Add(_magnetRadiusField);

            // ドラッグで範囲指定
            _radiusDragButton = new Button(() =>
            {
                var h = GetHandler?.Invoke();
                if (h == null) return;
                h.IsRadiusDragMode = true;
                h.OnRadiusChanged  = r =>
                {
                    _suppressSync = true;
                    _magnetRadiusSlider?.SetValueWithoutNotify(r);
                    _magnetRadiusField?.SetValueWithoutNotify(r);
                    _suppressSync = false;
                };
                UpdateRadiusDragButtonStyle(true);
            });
            _radiusDragButton.text = "ドラッグで範囲指定";
            _radiusDragButton.style.marginBottom = 3;
            _radiusDragButton.style.fontSize     = 10;
            _magnetParamsGroup.Add(_radiusDragButton);

            // 距離モード／フォールオフ（共通 UI）
            _distanceModeDropdown = _falloffControls.BuildDistanceDropdown(
                () => GetHandler?.Invoke()?.MagnetDistanceMode ?? DistanceMode.Euclidean,
                v  => { var h = GetHandler?.Invoke(); if (h != null) h.MagnetDistanceMode = v; });
            _magnetParamsGroup.Add(_distanceModeDropdown);

            _falloffDropdown = _falloffControls.BuildFalloffDropdown(
                () => GetHandler?.Invoke()?.MagnetFalloff ?? FalloffType.Gaussian,
                v  => { var h = GetHandler?.Invoke(); if (h != null) h.MagnetFalloff = v; });
            _magnetParamsGroup.Add(_falloffDropdown);

            // 詳細設定（半径範囲）
            var foldout = new Foldout { text = "詳細設定", value = false };
            foldout.style.color = new StyleColor(Color.white);
            _magnetParamsGroup.Add(foldout);

            AddHeader("半径範囲", foldout.contentContainer);

            var minRow = new VisualElement();
            minRow.style.flexDirection = FlexDirection.Row;
            minRow.style.alignItems    = Align.Center;
            minRow.style.marginBottom  = 2;
            foldout.contentContainer.Add(minRow);
            var minLabel = new Label("最小値");
            minLabel.style.color = new StyleColor(Color.white);
            minLabel.style.width = 50;
            minRow.Add(minLabel);
            _minRadiusField = new FloatField { value = 0.01f };
            _minRadiusField.style.flexGrow = 1;
            _minRadiusField.style.color = new StyleColor(Color.white);
            _minRadiusField.RegisterValueChangedCallback(e =>
            {
                var h = GetHandler?.Invoke();
                if (h == null) return;
                float v = Mathf.Max(0.001f, e.newValue);
                h.MinMagnetRadius = v;
                _magnetRadiusSlider.lowValue = v;
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
                float v = Mathf.Max(h.MinMagnetRadius + 0.001f, e.newValue);
                h.MaxMagnetRadius = v;
                _magnetRadiusSlider.highValue = v;
                _maxRadiusField.SetValueWithoutNotify(v);
            });
            maxRow.Add(_maxRadiusField);

            SetMagnetParamsVisible(false);

            // ── 数値移動 ─────────────────────────────────────────────
            // ワールド空間の「増分」を入力して選択要素を移動する（絶対座標ではない）。
            // 適用は MoveToolHandler.ApplyNumericMove。Undo は 1 件にまとまる。
            AddHeader("数値移動 (ワールド増分)");

            var moveRow = new VisualElement();
            moveRow.style.flexDirection = FlexDirection.Row;
            moveRow.style.marginBottom  = 3;
            _root.Add(moveRow);

            _moveXField = MakeMoveField("X");
            _moveYField = MakeMoveField("Y");
            _moveZField = MakeMoveField("Z");
            moveRow.Add(_moveXField); moveRow.Add(_moveYField); moveRow.Add(_moveZField);

            var moveBtnRow = new VisualElement();
            moveBtnRow.style.flexDirection = FlexDirection.Row;
            moveBtnRow.style.marginBottom  = 3;
            _root.Add(moveBtnRow);

            var applyMoveBtn = new Button(() =>
            {
                var h = GetHandler?.Invoke();
                if (h == null) return;
                h.ApplyNumericMove(new Vector3(
                    _moveXField?.value ?? 0f,
                    _moveYField?.value ?? 0f,
                    _moveZField?.value ?? 0f));
            }) { text = "移動" };
            applyMoveBtn.style.flexGrow = 1; applyMoveBtn.style.marginRight = 2;
            moveBtnRow.Add(applyMoveBtn);

            // 入力欄を 0 に戻すだけ。適用済みの移動は取り消さない（Undo を使うこと）。
            var clearMoveBtn = new Button(() =>
            {
                _moveXField?.SetValueWithoutNotify(0f);
                _moveYField?.SetValueWithoutNotify(0f);
                _moveZField?.SetValueWithoutNotify(0f);
            }) { text = "クリア" };
            clearMoveBtn.style.flexGrow = 1;
            moveBtnRow.Add(clearMoveBtn);

            // ── ギズモ ───────────────────────────────────────────────
            AddHeader("Gizmo");

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
            _targetLabel.style.color = new StyleColor(Color.white);
            _targetLabel.style.fontSize     = 10;
            _targetLabel.style.marginTop    = 4;
            _targetLabel.style.marginBottom = 2;
            _targetLabel.style.display      = DisplayStyle.None;
            _root.Add(_targetLabel);
        }

        // ================================================================
        // 更新
        // ================================================================

        public void Refresh()
        {
            var h = GetHandler?.Invoke();
            if (h == null) return;

            _magnetToggle?.SetValueWithoutNotify(h.UseMagnet);

            _suppressSync = true;
            _magnetRadiusSlider?.SetValueWithoutNotify(h.MagnetRadius);
            _magnetRadiusField?.SetValueWithoutNotify(h.MagnetRadius);
            _suppressSync = false;

            if (_lassoToggle != null)
                _lassoToggle.SetValueWithoutNotify(
                    h.DragSelectMode == MoveToolHandler.SelectionDragMode.Lasso);

            if (_falloffDropdown != null)
            {
                int fidx = System.Array.IndexOf(FalloffValues, h.MagnetFalloff);
                _falloffDropdown.SetValueWithoutNotify(fidx >= 0 ? FalloffLabels[fidx] : FalloffLabels[1]);
            }

            if (_distanceModeDropdown != null)
            {
                int didx = System.Array.IndexOf(DistanceModeValues, h.MagnetDistanceMode);
                _distanceModeDropdown.SetValueWithoutNotify(didx >= 0 ? DistanceModeLabels[didx] : DistanceModeLabels[0]);
            }

            SetMagnetParamsVisible(h.UseMagnet);

            _gizmoOffsetXSlider?.SetValueWithoutNotify(h.GizmoScreenOffsetX);
            _gizmoOffsetYSlider?.SetValueWithoutNotify(h.GizmoScreenOffsetY);

            _minRadiusField?.SetValueWithoutNotify(h.MinMagnetRadius);
            _maxRadiusField?.SetValueWithoutNotify(h.MaxMagnetRadius);

            if (_magnetRadiusSlider != null)
            {
                _magnetRadiusSlider.lowValue  = h.MinMagnetRadius;
                _magnetRadiusSlider.highValue = h.MaxMagnetRadius;
            }

            UpdateRadiusDragButtonStyle(h.IsRadiusDragMode);

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

        private void UpdateRadiusDragButtonStyle(bool active)
        {
            if (_radiusDragButton == null) return;
            _radiusDragButton.style.backgroundColor = active
                ? new StyleColor(new Color(0.3f, 0.6f, 1.0f, 0.8f))
                : new StyleColor(StyleKeyword.Null);
        }

        private void AddHeader(string text, VisualElement target = null)
        {
            var l = new Label(text);
            l.style.marginTop    = 6;
            l.style.marginBottom = 2;
            l.style.color        = new StyleColor(Color.white);
            l.style.fontSize     = 10;
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

        /// <summary>数値移動用の入力欄。値の適用は「移動」ボタン押下時のみ行う。</summary>
        private static FloatField MakeMoveField(string label)
        {
            var f = new FloatField(label) { value = 0f };
            f.style.flexGrow    = 1;
            f.style.marginRight = 2;
            f.style.color       = new StyleColor(Color.white);
            return f;
        }
    }
}
