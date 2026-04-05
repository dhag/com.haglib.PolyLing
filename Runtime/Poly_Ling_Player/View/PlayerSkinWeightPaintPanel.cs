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

namespace Poly_Ling.Player
{
    public class PlayerSkinWeightPaintPanel : ISkinWeightPaintPanel
    {
        // ================================================================
        // ISkinWeightPaintPanel 実装
        // ================================================================

        public SkinWeightPaintMode CurrentPaintMode   { get; private set; } = SkinWeightPaintMode.Replace;
        public float               CurrentBrushRadius { get; private set; } = 0.3f;
        public float               CurrentStrength    { get; private set; } = 0.5f;
        public BrushFalloff        CurrentFalloff     { get; private set; } = BrushFalloff.Smooth;
        public float               CurrentWeightValue { get; private set; } = 1f;
        public int                 CurrentTargetBone  { get; private set; } = -1;

        public void NotifyWeightChanged()
        {
            // ウェイト変更後にUIを更新する（将来的には選択頂点のウェイト表示など）
        }

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>パネル操作でウェイト可視化の再描画が必要なとき呼ばれる。</summary>
        public Action OnRepaint;

        /// <summary>Flood/Normalize/Prune 実行時に ToolContext を取得するコールバック。</summary>
        public Func<Poly_Ling.Tools.ToolContext> GetToolContext;

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
            _boneDropdown.style.marginBottom = 4;
            _boneDropdown.RegisterValueChangedCallback(e =>
            {
                int sel = _boneDropdown.index;
                CurrentTargetBone = (sel <= 0) ? -1 : _boneMasterIndices[sel - 1];
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
            _root.Add(SR("半径",  0.05f, 1.0f, () => CurrentBrushRadius, v => { CurrentBrushRadius = v; OnRepaint?.Invoke(); }));
            _root.Add(SR("強度",  0.01f, 1.0f, () => CurrentStrength,    v => { CurrentStrength    = v; }));
            _root.Add(SR("値",    0f,    1.0f, () => CurrentWeightValue,  v => { CurrentWeightValue  = v; }));

            AddSep();

            // ── フォールオフ
            AddSectionLabel("フォールオフ");
            var foRow = new VisualElement();
            foRow.style.flexDirection = FlexDirection.Row;
            foRow.style.marginBottom  = 4;
            AddFalloffBtn(foRow, "Const",  BrushFalloff.Constant);
            AddFalloffBtn(foRow, "Linear", BrushFalloff.Linear);
            AddFalloffBtn(foRow, "Smooth", BrushFalloff.Smooth);
            _root.Add(foRow);
            UpdateFalloffBtns();

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
            _statusLabel.style.fontSize = 10;
            _root.Add(_statusLabel);
        }

        // ================================================================
        // Flood / Normalize / Prune
        // ================================================================

        private void OnFlood()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx?.Model == null) { SetStatus("コンテキスト未設定"); return; }
            SkinWeightOperations.ExecuteFlood(
                ctx.Model, ctx,
                CurrentTargetBone, CurrentPaintMode,
                CurrentWeightValue, CurrentStrength,
                msg => SetStatus(msg));
            SetStatus("Flood 完了");
        }

        private void OnNormalize()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx?.Model == null) { SetStatus("コンテキスト未設定"); return; }
            SkinWeightOperations.ExecuteNormalize(ctx.Model, ctx,
                msg => SetStatus(msg));
            SetStatus("Normalize 完了");
        }

        private void OnPrune()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx?.Model == null) { SetStatus("コンテキスト未設定"); return; }
            int count = SkinWeightOperations.ExecutePrune(
                ctx.Model, ctx, _pruneThreshold,
                msg => SetStatus(msg));
            SetStatus($"Prune 完了: {count} 頂点");
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
                        _boneNames.Add(entry.Name ?? $"[{entry.MasterIndex}]");
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
        private readonly Button[] _falloffBtns = new Button[3];

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

        private void UpdateModeBtns()
        {
            var active   = new StyleColor(Color.white);
            var inactive = new StyleColor(StyleKeyword.Null);
            for (int i = 0; i < _modeBtns.Length; i++)
                if (_modeBtns[i] != null)
                    _modeBtns[i].style.backgroundColor =
                        ((int)CurrentPaintMode == i) ? active : inactive;
        }

        private void AddFalloffBtn(VisualElement row, string label, BrushFalloff fo)
        {
            int idx = (int)fo;
            var b = new Button(() =>
            {
                CurrentFalloff = fo;
                UpdateFalloffBtns();
            }) { text = label };
            b.style.flexGrow    = 1;
            b.style.marginRight = 2;
            b.style.fontSize    = 9;
            b.style.height      = 20;
            _falloffBtns[idx] = b;
            row.Add(b);
        }

        private void UpdateFalloffBtns()
        {
            var active   = new StyleColor(Color.white);
            var inactive = new StyleColor(StyleKeyword.Null);
            for (int i = 0; i < _falloffBtns.Length; i++)
                if (_falloffBtns[i] != null)
                    _falloffBtns[i].style.backgroundColor =
                        ((int)CurrentFalloff == i) ? active : inactive;
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

        private static VisualElement SR(string label, float min, float max, Func<float> get, Action<float> set)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginBottom  = 2;

            var lbl = new Label(label);
            lbl.style.width          = 32;
            lbl.style.unityTextAlign = TextAnchor.MiddleLeft;
            lbl.style.fontSize       = 10;
            row.Add(lbl);

            var sl = new Slider(min, max) { value = get() };
            sl.style.flexGrow = 1;
            var nf = new FloatField { value = get() };
            nf.style.width = 42;

            sl.RegisterValueChangedCallback(e =>
            {
                nf.SetValueWithoutNotify((float)Math.Round(e.newValue, 3));
                set(e.newValue);
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
