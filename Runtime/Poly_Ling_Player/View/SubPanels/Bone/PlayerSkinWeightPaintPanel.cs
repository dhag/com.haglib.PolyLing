// PlayerSkinWeightPaintPanel.cs
// スキンウェイトペイントパネル（Player ビルド用）。
// ISkinWeightPaintPanel を実装し SkinWeightPaintTool.ActivePanel に接続する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.UI;
using Poly_Ling.Tools;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public class PlayerSkinWeightPaintPanel : ISkinWeightPaintPanel
    {
        // ================================================================
        // ISkinWeightPaintPanel 実装
        // ================================================================

        public SkinWeightPaintMode CurrentPaintMode   { get; private set; } = SkinWeightPaintMode.Replace;
        // ブラシ半径はワールド単位。範囲・既定値はマグネット（MoveSettings）に揃える。
        public float               CurrentBrushRadius { get; private set; } = 0.1f;
        public float               CurrentStrength    { get; private set; } = 0.5f;
        public FalloffType         CurrentFalloff      { get; private set; } = FalloffType.Gaussian;
        public DistanceMode        CurrentDistanceMode { get; private set; } = DistanceMode.Euclidean;
        public float               CurrentWeightValue { get; private set; } = 1f;
        public int                 CurrentTargetBone  { get; private set; } = -1;
        /// <summary>
        /// 常に -1。ペイント対象はオブジェクトリストの選択に従う。
        ///
        /// 以前はパネル独自の「ターゲットメッシュ」ドロップダウンを持っていたが、
        /// オブジェクトリストと連動せず、可視化されるメッシュと塗られるメッシュが
        /// 食い違う原因になっていたため削除した。
        /// 対象の解決は SkinWeightOperations.CollectTargetMeshContexts に一本化してあり、
        /// ウェイト可視化（MeshSceneRenderer.CollectWeightVisTargets）と同じ集合になる。
        /// </summary>
        public int                 CurrentTargetMesh  => -1;

        public void NotifyWeightChanged()
        {
        }

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>パネル操作でウェイト可視化の再描画が必要なとき呼ばれる。</summary>
        public Action OnRepaint;

        /// <summary>ターゲットボーン変更時に呼ばれる（色再計算トリガー用）。</summary>
        public Action OnTargetBoneChanged;

        /// <summary>メッシュドロップダウン変更時に呼ばれる。</summary>

        /// <summary>Flood/Normalize/Prune 実行時に ToolContext を取得するコールバック。</summary>
        public Func<Poly_Ling.Tools.ToolContext> GetToolContext;

        // コマンド送信
        private PanelContext _panelContext;
        private Func<int>    _getModelIndex;

        public void SetCommandContext(PanelContext ctx, Func<int> getModelIndex)
        {
            _panelContext   = ctx;
            _getModelIndex  = getModelIndex;
        }

        private void SendCmd(PanelCommand cmd) => _panelContext?.SendCommand(cmd);

        // ================================================================
        // 内部 UI
        // ================================================================

        private VisualElement _root;

        // ターゲットボーン
        private DropdownField  _boneDropdown;
        private List<string>   _boneNames  = new List<string>();
        private List<int>      _boneMasterIndices = new List<int>();

        // Prune
        private float      _pruneThreshold = 0.01f;
        private FloatField _pruneThreshField;
        private Label      _statusLabel;

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

            AddSectionLabel("スキンウェイトペイント");
            AddSep();

            // ── ターゲットボーン
            AddSectionLabel("ターゲットボーン");
            _boneDropdown = new DropdownField(new List<string> { "（未選択）" }, 0);
            _boneDropdown.style.color = new StyleColor(Color.white);
            _boneDropdown.style.marginBottom = 4;
            _boneDropdown.RegisterValueChangedCallback(e =>
            {
                int sel = _boneDropdown.index;
                CurrentTargetBone = (sel <= 0) ? -1 : _boneMasterIndices[sel - 1];
                OnTargetBoneChanged?.Invoke();
                OnRepaint?.Invoke();
            });
            _root.Add(_boneDropdown);

            AddSep();

            // ── ペイントモード
            AddSectionLabel("モード");
            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginBottom  = 4;
            AddModeBtn(modeRow, "Replace", SkinWeightPaintMode.Replace);
            AddModeBtn(modeRow, "Add",     SkinWeightPaintMode.Add);
            AddModeBtn(modeRow, "Scale",   SkinWeightPaintMode.Scale);
            AddModeBtn(modeRow, "Smooth",  SkinWeightPaintMode.Smooth);
            _root.Add(modeRow);
            UpdateModeBtns();

            AddSep();

            // ── ブラシ設定
            AddSectionLabel("ブラシ");
            // 半径のみ刻みなし（step: 0f）。強度・値は SliderStep 刻み。
            _root.Add(SR("半径",  MagnetRadiusMin, MagnetRadiusMax,
                () => CurrentBrushRadius, v => { CurrentBrushRadius = v; OnRepaint?.Invoke(); }, 0f));
            _root.Add(SR("強度",  0.01f, 1.0f, () => CurrentStrength,    v => { CurrentStrength    = v; }, SliderStep));
            _root.Add(SR("値",    0f,    1.0f, () => CurrentWeightValue,  v => { CurrentWeightValue  = v; }, SliderStep));

            AddSep();

            // ── 距離モード／フォールオフ（マグネット・スカルプトと共通 UI）
            // 並び・ラベル・選択肢はマグネット（PlayerVertexMoveSubPanel）に合わせる。
            _root.Add(_falloffControls.BuildDistanceDropdown(
                () => CurrentDistanceMode, v => CurrentDistanceMode = v));
            _root.Add(_falloffControls.BuildFalloffDropdown(
                () => CurrentFalloff, v => CurrentFalloff = v));

            // ── 操作ボタン（エディタ版 SkinWeightPaintPanelV2 の Flood/Normalize/Prune に対応）
            AddSep();
            AddSectionLabel("操作");

            var floodBtn = new Button(OnFlood) { text = "Flood" };
            floodBtn.style.height       = 24;
            floodBtn.style.marginBottom = 3;
            _root.Add(floodBtn);

            var normRow = new VisualElement();
            normRow.style.flexDirection = FlexDirection.Row;
            normRow.style.marginBottom  = 3;
            var normBtn  = new Button(OnNormalize) { text = "Normalize" };
            normBtn.style.flexGrow    = 1;
            normBtn.style.marginRight = 2;
            var pruneBtn = new Button(OnPrune) { text = "Prune" };
            pruneBtn.style.flexGrow = 1;
            normRow.Add(normBtn);
            normRow.Add(pruneBtn);
            _root.Add(normRow);

            // Prune しきい値フィールド
            var pruneRow = new VisualElement();
            pruneRow.style.flexDirection = FlexDirection.Row;
            pruneRow.style.marginBottom  = 3;
            var pruneLbl = new Label("Threshold");
            pruneLbl.style.color = new StyleColor(Color.white);
            pruneLbl.style.width             = 70;
            pruneLbl.style.unityTextAlign    = TextAnchor.MiddleLeft;
            pruneLbl.style.fontSize          = 10;
            _pruneThreshField = new FloatField { value = _pruneThreshold };
            _pruneThreshField.style.flexGrow = 1;
            _pruneThreshField.RegisterValueChangedCallback(e =>
                _pruneThreshold = Mathf.Clamp(e.newValue, 0.0001f, 0.5f));
            pruneRow.Add(pruneLbl);
            pruneRow.Add(_pruneThreshField);
            _root.Add(pruneRow);

            _statusLabel = new Label();
            _statusLabel.style.color = new StyleColor(Color.white);
            _statusLabel.style.fontSize = 10;
            _root.Add(_statusLabel);
        }

        // ================================================================
        // Flood / Normalize / Prune
        // ================================================================

        // Flood / Normalize / Prune はいずれも PanelCommand 経由で実行する。
        // 対象は選択中の描画オブジェクト全件で、メッシュごとの Undo 記録と
        // GPU 転送は PlayerCommandDispatcher.ApplySkinWeightPerMesh が行う。
        // 以前あった PanelContext 未設定時のフォールバック（SkinWeightOperations を
        // 直接呼ぶ経路）は、1 メッシュしか処理できず Undo も片方だけ記録される
        // 別経路になっていたため削除した。

        private void OnFlood()
        {
            if (_panelContext == null) { SetStatus("コンテキスト未設定"); return; }
            if (CurrentTargetBone < 0)  { SetStatus("ターゲットボーンが未選択です。"); return; }

            SendCmd(new FloodSkinWeightCommand(_getModelIndex?.Invoke() ?? 0,
                CurrentTargetBone, CurrentPaintMode,
                CurrentWeightValue, CurrentStrength));
            SetStatus("Flood 実行");
        }

        private void OnNormalize()
        {
            if (_panelContext == null) { SetStatus("コンテキスト未設定"); return; }

            SendCmd(new NormalizeSkinWeightCommand(_getModelIndex?.Invoke() ?? 0));
            SetStatus("Normalize 実行");
        }

        private void OnPrune()
        {
            if (_panelContext == null) { SetStatus("コンテキスト未設定"); return; }

            SendCmd(new PruneSkinWeightCommand(_getModelIndex?.Invoke() ?? 0, _pruneThreshold));
            SetStatus("Prune 実行");
        }

        private void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        // ================================================================
        // モデル変更時にボーンリストを更新する
        // ================================================================

        public void RefreshBoneList(ModelContext model)
        {
            _boneNames.Clear();
            _boneMasterIndices.Clear();

            if (model != null)
            {
                var bones = model.Bones;
                if (bones != null)
                {
                    foreach (var entry in bones)
                    {
                        string bname = string.IsNullOrEmpty(entry.Name) ? $"Bone_{entry.MasterIndex}" : entry.Name;
                        _boneNames.Add($"{bname} [{entry.MasterIndex}]");
                        _boneMasterIndices.Add(entry.MasterIndex);
                    }
                }
            }

            // ドロップダウンを再構築
            if (_boneDropdown == null) return;

            var choices = new List<string> { "（未選択）" };
            choices.AddRange(_boneNames);

            _boneDropdown.choices = choices;

            // 現在のターゲットボーンが有効か確認
            int selIdx = 0;
            if (CurrentTargetBone >= 0)
            {
                int found = _boneMasterIndices.IndexOf(CurrentTargetBone);
                selIdx = found >= 0 ? found + 1 : 0;
            }
            _boneDropdown.SetValueWithoutNotify(choices[selIdx]);
            if (selIdx == 0) CurrentTargetBone = -1;
        }

        // ================================================================
        // モードボタン
        // ================================================================

        private readonly Button[] _modeBtns  = new Button[4];

        /// <summary>フォールオフ／距離モードの共通 UI（マグネット・スカルプトと共有）。</summary>
        private readonly BrushFalloffControls _falloffControls = new BrushFalloffControls();

        // ブラシ半径の範囲。マグネット（MoveSettings.MIN/MAX_MAGNET_RADIUS）と同じ
        // ParameterLimits のキーを参照し、3 ツールで同一レンジにする。
        private static float MagnetRadiusMin => Poly_Ling.Core.ParameterLimits.GetF("Move.MagnetRadius.Min");
        private static float MagnetRadiusMax => Poly_Ling.Core.ParameterLimits.GetF("Move.MagnetRadius.Max");

        /// <summary>強度・値スライダのドラッグ時の刻み幅。半径には適用しない。</summary>
        private const float SliderStep = 0.05f;

        // 選択中／非選択のボタン色。
        // 旧実装は選択中に Color.white、非選択に StyleKeyword.Null を入れていた。
        // 文字色は ApplyDarkTheme が白にするため白背景では読めず、Null は
        // インライン値を外すだけで USS 既定の明るい灰色になり、どちらも白く見えていた。
        // 色は PolyLingPlayerViewerCore の
        // InteractionActiveBtnColor / InactiveBtnColor と同じ値に揃える。
        private static readonly StyleColor SegActiveColor   = PlayerLayoutRoot.BtnActiveColor;
        private static readonly StyleColor SegInactiveColor = PlayerLayoutRoot.BtnInactiveColor;

        private void AddModeBtn(VisualElement row, string label, SkinWeightPaintMode mode)
        {
            int idx = (int)mode;
            var b = new Button(() =>
            {
                CurrentPaintMode = mode;
                UpdateModeBtns();
                OnRepaint?.Invoke();
            }) { text = label };
            b.style.flexGrow     = 1;
            b.style.marginRight  = 2;
            b.style.fontSize     = 9;
            b.style.height       = 20;
            _modeBtns[idx] = b;
            row.Add(b);
        }

        /// <summary>
        /// セグメント型ボタン（モード／フォールオフ）の色を塗り直す。
        /// PlayerLayoutRoot.ApplyDarkTheme は全 Button を既定色へ戻すため、
        /// それが走った後に外部から呼ぶ。
        /// </summary>
        public void RepaintSegmentButtons()
        {
            UpdateModeBtns();
            _falloffControls.Sync();
        }

        private void UpdateModeBtns()
        {
            for (int i = 0; i < _modeBtns.Length; i++)
                if (_modeBtns[i] != null)
                    _modeBtns[i].style.backgroundColor =
                        ((int)CurrentPaintMode == i) ? SegActiveColor : SegInactiveColor;
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private void AddSectionLabel(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            _root.Add(l);
        }

        private void AddSep()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 3;
            v.style.marginBottom    = 3;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            _root.Add(v);
        }

        /// <param name="step">スライダのドラッグ時の刻み幅。0 のとき刻みなし。</param>
        private static VisualElement SR(string label, float min, float max, Func<float> get, Action<float> set, float step = 0f)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.color = new StyleColor(Color.white);
            lbl.style.width          = 32;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.fontSize       = 10;
            row.Add(lbl);

            var sl = new Slider(min, max) { value = get() };
            sl.style.color = new StyleColor(Color.white);
            sl.style.flexGrow = 1;
            var nf = new FloatField { value = get() };
            nf.style.width = 63;

            sl.RegisterValueChangedCallback(e =>
            {
                // step > 0 のときだけドラッグ値を刻みへ丸める。
                // 数値フィールドの直接入力（下の nf 側）は丸めない。
                float v = e.newValue;
                if (step > 0f)
                {
                    v = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
                    if (!Mathf.Approximately(v, e.newValue)) sl.SetValueWithoutNotify(v);
                }
                nf.SetValueWithoutNotify((float)Math.Round(v, 3));
                set(v);
            });
            nf.RegisterValueChangedCallback(e =>
            {
                float v = Mathf.Clamp(e.newValue, min, max);
                sl.SetValueWithoutNotify(v);
                set(v);
            });
            row.Add(sl);
            row.Add(nf);
            return row;
        }
    }
}
