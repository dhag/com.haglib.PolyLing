// PlayerSculptSubPanel.cs
// スカルプトツール用サブパネル（Player ビルド用）。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Tools;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    public class PlayerSculptSubPanel
    {
        // ================================================================
        // 外部注入
        // ================================================================

        public Func<SculptToolHandler> GetHandler;

        /// <summary>一時ミラーのコントローラ取得。</summary>
        public Func<TempMirrorController> GetTempMirror;

        /// <summary>このツールの識別値（InteractionMode を int 化したもの）。</summary>
        public Func<int> GetTempMirrorOwnerToken;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private RadioButtonGroup _modeGroup;
        private Slider      _brushRadiusSlider;
        private FloatField  _brushRadiusField;
        private DropdownField _falloffDropdown;
        private DropdownField _distanceModeDropdown;
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

        // フォールオフ／距離モードの選択肢は BrushFalloffControls に集約した。
        // マグネット・スキンWペイントも同じものを使う。
        private static string[]       FalloffLabels      => BrushFalloffControls.FalloffLabels;
        private static FalloffType[]  FalloffValues      => BrushFalloffControls.FalloffValues;
        private static string[]       DistanceModeLabels => BrushFalloffControls.DistanceModeLabels;
        private static DistanceMode[] DistanceModeValues => BrushFalloffControls.DistanceModeValues;

        /// <summary>距離モード／フォールオフの共通 UI。</summary>
        private readonly BrushFalloffControls _falloffControls = new BrushFalloffControls();

        /// <summary>一時ミラーのトグルボタン（共通 UI）。</summary>
        private readonly TempMirrorControls _tempMirrorControls = new TempMirrorControls();

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

            // ハンドラの実値（= ParameterLimits の上下限、SculptSettings の現在値）から作る。
            // 固定値でスライダを作ると、上下限を変更した状態で開き直したときに
            // つまみの位置と数字が食い違う。
            var h0 = GetHandler?.Invoke();
            float radMin0 = h0?.MinBrushRadius ?? 0.05f;
            float radMax0 = h0?.MaxBrushRadius ?? 1.0f;
            float rad0    = h0?.BrushRadius    ?? 0.1f;

            _brushRadiusSlider = new Slider(radMin0, radMax0) { value = Mathf.Clamp(rad0, radMin0, radMax0) };
            _brushRadiusSlider.style.flexGrow = 1;
            _brushRadiusSlider.style.color = new StyleColor(Color.white);
            _brushRadiusSlider.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                if (h == null) return;
                h.BrushRadius = e.newValue;
                float applied = h.BrushRadius; // setter でクランプ済みの実値
                _suppressSync = true;
                _brushRadiusSlider?.SetValueWithoutNotify(applied);
                _brushRadiusField?.SetValueWithoutNotify(applied);
                _suppressSync = false;
            });
            radiusRow.Add(_brushRadiusSlider);

            _brushRadiusField = new FloatField { value = rad0 };
            _brushRadiusField.style.width = 52;
            _brushRadiusField.style.color = new StyleColor(Color.white);
            _brushRadiusField.tooltip = "上下限の外の値を入れると、上下限のほうを広げて入力値を採用する。";
            _brushRadiusField.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                if (h == null) return;
                ApplyRadiusInput(h, e.newValue);
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
                // ドラッグ終了・クリック終了時にハンドラーから通知を受けてボタン色を戻す
                h.OnRadiusDragModeExited = () => UpdateRadiusDragButtonStyle(false);
                UpdateRadiusDragButtonStyle(true);
            });
            _radiusDragButton.text = "ドラッグで範囲指定";
            _radiusDragButton.style.marginBottom = 3;
            _radiusDragButton.style.fontSize     = 10;
            _root.Add(_radiusDragButton);

            // ── 距離モード／フォールオフ（共通 UI）─────────────────
            // 並びはマグネットに合わせて「距離モード」→「フォールオフ」。
            _distanceModeDropdown = _falloffControls.BuildDistanceDropdown(
                () => GetHandler?.Invoke()?.DistanceMode ?? DistanceMode.Euclidean,
                v  => { var h = GetHandler?.Invoke(); if (h != null) h.DistanceMode = v; });
            _root.Add(_distanceModeDropdown);

            _falloffDropdown = _falloffControls.BuildFalloffDropdown(
                () => GetHandler?.Invoke()?.Falloff ?? FalloffType.Gaussian,
                v  => { var h = GetHandler?.Invoke(); if (h != null) h.Falloff = v; });
            _root.Add(_falloffDropdown);

            // ── 強度（スライダー + テキストボックス）────────────────
            AddSectionLabel("強度 (Strength)");

            var strengthRow = new VisualElement();
            strengthRow.style.flexDirection = FlexDirection.Row;
            strengthRow.style.marginBottom  = 3;
            _root.Add(strengthRow);

            float strMin0 = h0?.MinStrength ?? 0.01f;
            float strMax0 = h0?.MaxStrength ?? 0.05f;
            float str0    = h0?.Strength    ?? 0.02f;

            _strengthSlider = new Slider(strMin0, strMax0) { value = Mathf.Clamp(str0, strMin0, strMax0) };
            _strengthSlider.style.flexGrow = 1;
            _strengthSlider.style.color = new StyleColor(Color.white);
            _strengthSlider.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                if (h == null) return;
                h.Strength = e.newValue;
                float applied = h.Strength; // setter でクランプ済みの実値
                _suppressSync = true;
                _strengthSlider?.SetValueWithoutNotify(applied);
                _strengthField?.SetValueWithoutNotify(applied);
                _suppressSync = false;
            });
            strengthRow.Add(_strengthSlider);

            _strengthField = new FloatField { value = str0 };
            _strengthField.style.width = 52;
            _strengthField.style.color = new StyleColor(Color.white);
            _strengthField.tooltip = "上下限の外の値を入れると、上下限のほうを広げて入力値を採用する。";
            _strengthField.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                if (h == null) return;
                ApplyStrengthInput(h, e.newValue);
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

            // ── 一時ミラー ───────────────────────────────────────────
            // 対称面をまたぐスカルプト（なめらか等）を正しく効かせるための一時的な実体化。
            // パラメータは左ペイン「一時ミラー」の設定を共有する。
            // 他のツールへ移ると PolyLingPlayerViewerCore が自動で解除する。
            _root.Add(_tempMirrorControls.Build(
                () => GetTempMirror?.Invoke(),
                () => GetTempMirrorOwnerToken?.Invoke() ?? -1));

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
            _minRadiusField = new FloatField { value = radMin0 };
            _minRadiusField.style.flexGrow = 1;
            _minRadiusField.style.color = new StyleColor(Color.white);
            _minRadiusField.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                if (h == null) return;
                h.MinBrushRadius = e.newValue;      // setter で 0.001 以上にクランプ
                ApplyRadiusRange(h);
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
            _maxRadiusField = new FloatField { value = radMax0 };
            _maxRadiusField.style.flexGrow = 1;
            _maxRadiusField.style.color = new StyleColor(Color.white);
            _maxRadiusField.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                if (h == null) return;
                h.MaxBrushRadius = e.newValue;      // setter で Min+0.001 以上にクランプ
                ApplyRadiusRange(h);
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
            _minStrengthField = new FloatField { value = strMin0 };
            _minStrengthField.style.flexGrow = 1;
            _minStrengthField.style.color = new StyleColor(Color.white);
            _minStrengthField.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                if (h == null) return;
                h.MinStrength = e.newValue;         // setter で 0.001 以上にクランプ
                ApplyStrengthRange(h);
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
            _maxStrengthField = new FloatField { value = strMax0 };
            _maxStrengthField.style.flexGrow = 1;
            _maxStrengthField.style.color = new StyleColor(Color.white);
            _maxStrengthField.RegisterValueChangedCallback(e =>
            {
                if (_suppressSync) return;
                var h = GetHandler?.Invoke();
                if (h == null) return;
                h.MaxStrength = e.newValue;         // setter で Min+0.001 以上にクランプ
                ApplyStrengthRange(h);
            });
            maxStrRow.Add(_maxStrengthField);

            // 「上下限の保存」UI（保存先＋バックアップ/復元/既定/再読込）はスカルプト画面から撤去（不自然なため）。
            // 機能は ParameterLimits.Backup/Restore/ResetToDefaults/Reload に残置。

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
            // 一時ミラーの表示はハンドラの有無に依らないので先に同期する。
            _tempMirrorControls.Refresh();

            var h = GetHandler?.Invoke();
            if (h == null) return;

            int modeIdx = System.Array.IndexOf(ModeValues, h.Mode);
            _modeGroup?.SetValueWithoutNotify(modeIdx >= 0 ? modeIdx : 0);

            // フォールオフ
            if (_falloffDropdown != null)
            {
                int fidx = System.Array.IndexOf(FalloffValues, h.Falloff);
                _falloffDropdown.SetValueWithoutNotify(fidx >= 0 ? FalloffLabels[fidx] : FalloffLabels[1]);
            }

            // 距離モード
            if (_distanceModeDropdown != null)
            {
                int didx = System.Array.IndexOf(DistanceModeValues, h.DistanceMode);
                _distanceModeDropdown.SetValueWithoutNotify(didx >= 0 ? DistanceModeLabels[didx] : DistanceModeLabels[0]);
            }

            _invertToggle?.SetValueWithoutNotify(h.Invert);

            // レンジ → 値 の順で入れる（逆順だと旧レンジで値が切り詰められ、
            // つまみの位置とテキストボックスの数字が食い違う）。
            _suppressSync = true;

            SliderRangeUtil.SetRangeAndValue(
                _brushRadiusSlider, h.MinBrushRadius, h.MaxBrushRadius, h.BrushRadius);
            _brushRadiusField?.SetValueWithoutNotify(h.BrushRadius);
            _minRadiusField?.SetValueWithoutNotify(h.MinBrushRadius);
            _maxRadiusField?.SetValueWithoutNotify(h.MaxBrushRadius);

            SliderRangeUtil.SetRangeAndValue(
                _strengthSlider, h.MinStrength, h.MaxStrength, h.Strength);
            _strengthField?.SetValueWithoutNotify(h.Strength);
            _minStrengthField?.SetValueWithoutNotify(h.MinStrength);
            _maxStrengthField?.SetValueWithoutNotify(h.MaxStrength);

            _suppressSync = false;

            UpdateRadiusDragButtonStyle(h.IsRadiusDragMode);
            UpdateHelp(h.Mode);
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private void UpdateRadiusDragButtonStyle(bool active)
        {
            if (_radiusDragButton == null) return;
            // 非 active に StyleKeyword.Null を入れると ApplyDarkTheme のインライン背景が
            // 外れて USS 既定の明るい灰色になり、白文字が読めなくなる。明示色を入れる。
            _radiusDragButton.style.backgroundColor = active
                ? new StyleColor(new Color(0.3f, 0.6f, 1.0f, 0.8f))
                : PlayerLayoutRoot.BtnInactiveColor;
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

        // ── 半径 ─────────────────────────────────────────────────────

        /// <summary>
        /// テキストボックスの入力をそのまま採用する。上下限の外なら上下限を広げる。
        /// 黙ってクランプすると、上下限が折りたたみの中にあるため
        /// 「入れた数字が勝手に変わる」ように見える。
        /// </summary>
        private void ApplyRadiusInput(SculptToolHandler h, float requested)
        {
            float min = h.MinBrushRadius;
            float max = h.MaxBrushRadius;
            SliderRangeUtil.ExpandToInclude(requested, ref min, ref max);

            // setter 側にもクランプがあるので、書いた後に実値を読み戻す。
            h.MinBrushRadius = min;
            h.MaxBrushRadius = max;
            min = h.MinBrushRadius;
            max = h.MaxBrushRadius;

            h.BrushRadius = requested;
            SyncRadiusWidgets(h, min, max);
        }

        /// <summary>上下限が変わったとき、現在値を新レンジへ収めて UI を揃える。</summary>
        private void ApplyRadiusRange(SculptToolHandler h)
        {
            float min = h.MinBrushRadius;
            float max = h.MaxBrushRadius;
            if (max < min + SliderRangeUtil.MinSpan)
            {
                h.MaxBrushRadius = min + SliderRangeUtil.MinSpan;
                max = h.MaxBrushRadius;
            }
            h.BrushRadius = Mathf.Clamp(h.BrushRadius, min, max);
            SyncRadiusWidgets(h, min, max);
        }

        private void SyncRadiusWidgets(SculptToolHandler h, float min, float max)
        {
            _suppressSync = true;
            SliderRangeUtil.SetRangeAndValue(_brushRadiusSlider, min, max, h.BrushRadius);
            _brushRadiusField?.SetValueWithoutNotify(h.BrushRadius);
            _minRadiusField?.SetValueWithoutNotify(min);
            _maxRadiusField?.SetValueWithoutNotify(max);
            _suppressSync = false;
        }

        // ── 強度 ─────────────────────────────────────────────────────

        /// <summary>半径と同じ方針。入力値を採用し、必要なら上下限を広げる。</summary>
        private void ApplyStrengthInput(SculptToolHandler h, float requested)
        {
            float min = h.MinStrength;
            float max = h.MaxStrength;
            SliderRangeUtil.ExpandToInclude(requested, ref min, ref max);

            h.MinStrength = min;
            h.MaxStrength = max;
            min = h.MinStrength;
            max = h.MaxStrength;

            h.Strength = requested;
            SyncStrengthWidgets(h, min, max);
        }

        private void ApplyStrengthRange(SculptToolHandler h)
        {
            float min = h.MinStrength;
            float max = h.MaxStrength;
            if (max < min + SliderRangeUtil.MinSpan)
            {
                h.MaxStrength = min + SliderRangeUtil.MinSpan;
                max = h.MaxStrength;
            }
            h.Strength = Mathf.Clamp(h.Strength, min, max);
            SyncStrengthWidgets(h, min, max);
        }

        private void SyncStrengthWidgets(SculptToolHandler h, float min, float max)
        {
            _suppressSync = true;
            SliderRangeUtil.SetRangeAndValue(_strengthSlider, min, max, h.Strength);
            _strengthField?.SetValueWithoutNotify(h.Strength);
            _minStrengthField?.SetValueWithoutNotify(min);
            _maxStrengthField?.SetValueWithoutNotify(max);
            _suppressSync = false;
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
