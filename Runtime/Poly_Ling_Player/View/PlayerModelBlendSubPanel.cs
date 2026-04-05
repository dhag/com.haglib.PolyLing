// PlayerModelBlendSubPanel.cs
// モデルブレンドサブパネル（Player ビルド用）。
// MultiModelBlendPanelV2 の機能を UIToolkit サブパネルとして移植。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    public class PlayerModelBlendSubPanel
    {
        // ================================================================
        // コールバック（Viewer から設定）
        // ================================================================

        /// <summary>PanelCommand を送信するコールバック。</summary>
        public Action<PanelCommand> SendCommand;

        /// <summary>IProjectView を取得するコールバック。</summary>
        public Func<IProjectView> GetProjectView;

        // ================================================================
        // 内部状態
        // ================================================================

        private int  _sourceModelIndex   = -1;
        private int  _cloneModelIndex    = -1;
        private int  _preCloneModelCount = -1;

        private readonly Dictionary<int, float> _srcWeightMap = new Dictionary<int, float>();
        private List<bool> _meshEnabled = new List<bool>();

        private bool _recalcNormals    = true;
        private bool _realtimePreview  = true;
        private bool _blendBones       = false;
        private bool _selectedMeshOnly = false;
        private bool _visibleOnly      = true;

        private readonly Dictionary<int, Slider> _sliderMap = new Dictionary<int, Slider>();

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _warningLabel;
        private Label         _targetInfoLabel;
        private Label         _cloneInfoLabel;
        private VisualElement _meshToggleContainer;
        private VisualElement _sliderContainer;
        private Toggle        _toggleRecalc;
        private Toggle        _toggleRealtime;
        private Toggle        _toggleBlendBones;
        private Toggle        _toggleSelectedOnly;
        private Toggle        _toggleVisibleOnly;
        private Label         _totalWeightLabel;
        private Button        _btnApply;
        private Button        _btnCancel;
        private Label         _statusLabel;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingLeft = _root.style.paddingRight =
            _root.style.paddingTop  = _root.style.paddingBottom = 4;
            parent.Add(_root);

            _warningLabel = new Label();
            _warningLabel.style.color        = new StyleColor(new Color(0.8f, 0.8f, 0.3f));
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.marginBottom = 4;
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            _root.Add(_warningLabel);

            _targetInfoLabel = new Label();
            _targetInfoLabel.style.marginBottom = 2;
            _targetInfoLabel.style.fontSize     = 10;
            _root.Add(_targetInfoLabel);

            _cloneInfoLabel = new Label();
            _cloneInfoLabel.style.color       = new StyleColor(new Color(0.5f, 0.9f, 0.5f));
            _cloneInfoLabel.style.marginBottom = 4;
            _cloneInfoLabel.style.fontSize     = 10;
            _root.Add(_cloneInfoLabel);

            // ── メッシュトグル Foldout
            var foldout = new Foldout { text = "ブレンド対象メッシュ", value = false };
            foldout.style.marginBottom = 4;
            _meshToggleContainer = new VisualElement();
            foldout.Add(_meshToggleContainer);
            _root.Add(foldout);

            // ── オプション1行目
            var optRow = new VisualElement();
            optRow.style.flexDirection  = FlexDirection.Row;
            optRow.style.marginBottom   = 4;
            _toggleRecalc   = MkToggle("法線再計算",      _recalcNormals);
            _toggleRealtime = MkToggle("リアルタイム",    _realtimePreview);
            _toggleRecalc  .style.flexGrow = 1;
            _toggleRealtime.style.flexGrow = 1;
            _toggleRecalc  .RegisterValueChangedCallback(e => _recalcNormals   = e.newValue);
            _toggleRealtime.RegisterValueChangedCallback(e => _realtimePreview = e.newValue);
            optRow.Add(_toggleRecalc); optRow.Add(_toggleRealtime);
            _root.Add(optRow);

            // ── オプション2行目
            var optRow2 = new VisualElement();
            optRow2.style.flexDirection = FlexDirection.Row;
            optRow2.style.marginBottom  = 4;
            _toggleBlendBones   = MkToggle("ボーン",      _blendBones);
            _toggleSelectedOnly = MkToggle("選択のみ",    _selectedMeshOnly);
            _toggleVisibleOnly  = MkToggle("可視のみ",    _visibleOnly);
            _toggleBlendBones  .style.flexGrow = 1;
            _toggleSelectedOnly.style.flexGrow = 1;
            _toggleVisibleOnly .style.flexGrow = 1;
            _toggleBlendBones.RegisterValueChangedCallback(e =>
            { _blendBones = e.newValue; if (_realtimePreview) SendPreview(); });
            _toggleSelectedOnly.RegisterValueChangedCallback(e =>
            { _selectedMeshOnly = e.newValue; if (_realtimePreview) SendPreview(); });
            _toggleVisibleOnly.RegisterValueChangedCallback(e =>
            { _visibleOnly = e.newValue; if (_realtimePreview) SendPreview(); });
            optRow2.Add(_toggleBlendBones); optRow2.Add(_toggleSelectedOnly); optRow2.Add(_toggleVisibleOnly);
            _root.Add(optRow2);

            // ── スライダーコンテナ
            _sliderContainer = new VisualElement();
            _sliderContainer.style.marginBottom = 4;
            _root.Add(_sliderContainer);

            // ── 合計ウェイトラベル
            _totalWeightLabel = new Label("合計: 0.000");
            _totalWeightLabel.style.fontSize     = 10;
            _totalWeightLabel.style.marginBottom = 4;
            _root.Add(_totalWeightLabel);

            // ── ウェイト操作ボタン
            var wRow = new VisualElement();
            wRow.style.flexDirection = FlexDirection.Row;
            wRow.style.marginBottom  = 6;
            wRow.Add(MkBtn("均等",           () => OnEqualWeights()));
            wRow.Add(MkBtn("正規化",          () => OnNormalize()));
            wRow.Add(MkBtn("ソースにリセット", () => OnResetFirst()));
            _root.Add(wRow);

            // ── 適用ボタン
            _btnApply = new Button(OnApply) { text = "クローンに適用" };
            _btnApply.style.height   = 28;
            _btnApply.style.fontSize = 11;
            _root.Add(_btnApply);

            // ── キャンセルボタン
            _btnCancel = new Button(OnCancel) { text = "クローンを削除" };
            _btnCancel.style.height   = 24;
            _btnCancel.style.marginTop = 4;
            _btnCancel.style.color    = new StyleColor(new Color(1f, 0.55f, 0.55f));
            _root.Add(_btnCancel);

            // ── ステータスラベル
            _statusLabel = new Label("");
            _statusLabel.style.marginTop = 4;
            _statusLabel.style.fontSize  = 10;
            _statusLabel.style.color     = new StyleColor(Color.white);
            _root.Add(_statusLabel);
        }

        // ================================================================
        // 初期化（Viewer から呼ぶ）
        // ================================================================

        public void Init()
        {
            var view = GetProjectView?.Invoke();
            if (view == null || view.ModelCount < 2)
            {
                ShowWarning(view == null ? "プロジェクトがありません" : "モデルが2つ以上必要です");
                return;
            }

            _sourceModelIndex   = view.CurrentModelIndex;
            _cloneModelIndex    = -1;
            _preCloneModelCount = view.ModelCount;

            _srcWeightMap.Clear();
            for (int i = 0; i < view.ModelCount; i++)
                _srcWeightMap[i] = (i == _sourceModelIndex) ? 1f : 0f;

            SyncMeshEnabled(view);

            string srcName = view.CurrentModel?.Name ?? "Model";
            SendCommand?.Invoke(new CreateBlendCloneCommand(_sourceModelIndex, srcName + "_blend"));
        }

        /// <summary>ViewChanged 相当。Viewer の NotifyPanels から呼ぶ。</summary>
        public void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (view == null) return;

            // クローン確定検出
            if (_cloneModelIndex < 0
                && _preCloneModelCount >= 0
                && view.ModelCount == _preCloneModelCount + 1)
            {
                _cloneModelIndex = view.ModelCount - 1;
                _srcWeightMap[_cloneModelIndex] = 0f;
                SendCommand?.Invoke(new SwitchModelCommand(_cloneModelIndex));
                Refresh(view);
                return;
            }

            Refresh(view);
        }

        // ================================================================
        // Refresh
        // ================================================================

        private void Refresh(IProjectView view)
        {
            if (_warningLabel == null) return;

            if (view == null || view.ModelCount < 2)
            {
                ShowWarning(view == null ? "プロジェクトがありません" : "モデルが2つ以上必要です"); return;
            }
            _warningLabel.style.display = DisplayStyle.None;

            var srcMv = (_sourceModelIndex >= 0 && _sourceModelIndex < view.ModelCount)
                ? view.GetModelView(_sourceModelIndex) : null;
            _targetInfoLabel.text = $"ソース: {srcMv?.Name ?? "---"}  ({srcMv?.DrawableCount ?? 0} mesh)";

            if (_cloneModelIndex >= 0 && _cloneModelIndex < view.ModelCount)
                _cloneInfoLabel.text = $"クローン[{_cloneModelIndex}]: {view.GetModelView(_cloneModelIndex)?.Name ?? "---"}";
            else
                _cloneInfoLabel.text = "クローン作成中...";

            SyncWeightMapToModelCount(view.ModelCount);
            SyncMeshEnabled(view);
            RebuildMeshToggles(view);
            RebuildSliders(view);
            UpdateTotalWeightLabel();
            _btnApply?.SetEnabled(_cloneModelIndex >= 0);
            _btnCancel?.SetEnabled(_cloneModelIndex >= 0);
        }

        private void ShowWarning(string msg)
        {
            if (_warningLabel == null) return;
            _warningLabel.text          = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
        }

        // ================================================================
        // メッシュトグル
        // ================================================================

        private void RebuildMeshToggles(IProjectView view)
        {
            _meshToggleContainer.Clear();
            var srcMv = (_sourceModelIndex >= 0) ? view.GetModelView(_sourceModelIndex) : null;
            int count = srcMv?.DrawableCount ?? 0;
            for (int i = 0; i < count; i++)
            {
                string meshName = srcMv?.DrawableList?[i]?.Name ?? $"Mesh[{i}]";
                int ci = i;
                var tog = MkToggle($"{meshName}", i < _meshEnabled.Count && _meshEnabled[i]);
                tog.RegisterValueChangedCallback(e =>
                {
                    if (ci < _meshEnabled.Count) _meshEnabled[ci] = e.newValue;
                    if (_realtimePreview) SendPreview();
                });
                _meshToggleContainer.Add(tog);
            }
        }

        // ================================================================
        // スライダー
        // ================================================================

        private void RebuildSliders(IProjectView view)
        {
            _sliderContainer.Clear();
            _sliderMap.Clear();

            for (int i = 0; i < view.ModelCount; i++)
            {
                if (i == _cloneModelIndex) continue;
                var mv = view.GetModelView(i);
                if (mv == null) continue;

                bool isSrc = (i == _sourceModelIndex);
                float weight = _srcWeightMap.TryGetValue(i, out var w) ? w : 0f;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems    = Align.Center;
                row.style.marginBottom  = 2;

                var nameLbl = new Label(isSrc ? $"★ {mv.Name}" : mv.Name);
                nameLbl.style.width        = 100;
                nameLbl.style.overflow     = Overflow.Hidden;
                nameLbl.style.fontSize     = 9;
                row.Add(nameLbl);

                int ci = i;
                var slider = new Slider(0f, 1f) { value = weight };
                slider.style.flexGrow = 1;
                slider.RegisterValueChangedCallback(e =>
                {
                    _srcWeightMap[ci] = e.newValue;
                    UpdateTotalWeightLabel();
                    if (_realtimePreview) SendPreview();
                });
                row.Add(slider);

                var valLbl = new Label(weight.ToString("F2"));
                valLbl.style.width = 28;
                valLbl.style.unityTextAlign = TextAnchor.MiddleRight;
                valLbl.style.fontSize = 9;
                slider.RegisterValueChangedCallback(e => valLbl.text = e.newValue.ToString("F2"));
                row.Add(valLbl);

                _sliderMap[i] = slider;
                _sliderContainer.Add(row);
            }
            PlayerLayoutRoot.ApplyDarkTheme(_sliderContainer);
        }

        // ================================================================
        // ウェイト操作
        // ================================================================

        private void OnEqualWeights()
        {
            var keys = _srcWeightMap.Keys.Where(k => k != _cloneModelIndex).ToList();
            if (keys.Count == 0) return;
            float eq = 1f / keys.Count;
            foreach (var k in keys) _srcWeightMap[k] = eq;
            SyncSlidersFromWeights();
            UpdateTotalWeightLabel();
            if (_realtimePreview) SendPreview();
        }

        private void OnNormalize()
        {
            var keys = _srcWeightMap.Keys.Where(k => k != _cloneModelIndex).ToList();
            float t  = keys.Sum(k => _srcWeightMap[k]);
            if (t <= 0f) { OnEqualWeights(); return; }
            foreach (var k in keys) _srcWeightMap[k] /= t;
            SyncSlidersFromWeights();
            UpdateTotalWeightLabel();
            if (_realtimePreview) SendPreview();
        }

        private void OnResetFirst()
        {
            var keys = _srcWeightMap.Keys.Where(k => k != _cloneModelIndex).ToList();
            foreach (var k in keys) _srcWeightMap[k] = (k == _sourceModelIndex) ? 1f : 0f;
            SyncSlidersFromWeights();
            UpdateTotalWeightLabel();
            if (_realtimePreview) SendPreview();
        }

        private void SyncSlidersFromWeights()
        {
            foreach (var kv in _sliderMap)
                if (_srcWeightMap.TryGetValue(kv.Key, out float w))
                    kv.Value.SetValueWithoutNotify(w);
        }

        private void UpdateTotalWeightLabel()
        {
            if (_totalWeightLabel == null) return;
            float t = _srcWeightMap
                .Where(kv => kv.Key != _cloneModelIndex)
                .Sum(kv => kv.Value);
            _totalWeightLabel.text = $"合計: {t:F3}";
        }

        // ================================================================
        // プレビュー / Apply / Cancel
        // ================================================================

        private void SendPreview()
        {
            if (_cloneModelIndex < 0) return;
            SendCommand?.Invoke(new PreviewModelBlendCommand(
                _sourceModelIndex, _cloneModelIndex,
                BuildWeightArray(), BuildMeshEnabledArray(), _blendBones));
        }

        private void OnApply()
        {
            if (_cloneModelIndex < 0) { SetStatus("クローンが未作成です"); return; }
            SendCommand?.Invoke(new ApplyModelBlendCommand(
                _sourceModelIndex, _cloneModelIndex,
                BuildWeightArray(), BuildMeshEnabledArray(), _recalcNormals, _blendBones));
            SetStatus("適用しました");
        }

        private void OnCancel()
        {
            if (_cloneModelIndex >= 0)
            {
                SendCommand?.Invoke(new DeleteModelCommand(_cloneModelIndex));
                SendCommand?.Invoke(new SwitchModelCommand(_sourceModelIndex));
            }
            _cloneModelIndex    = -1;
            _sourceModelIndex   = -1;
            _preCloneModelCount = -1;
            _srcWeightMap.Clear();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private float[] BuildWeightArray()
        {
            var view  = GetProjectView?.Invoke();
            int count = view?.ModelCount ?? 0;
            var arr   = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (i == _cloneModelIndex) { arr[i] = 0f; continue; }
                arr[i] = _srcWeightMap.TryGetValue(i, out var w) ? w : 0f;
            }
            return arr;
        }

        private bool[] BuildMeshEnabledArray()
        {
            var result = _meshEnabled.ToArray();
            if (result.Length == 0) return result;
            var view   = GetProjectView?.Invoke();
            var srcMv  = (_sourceModelIndex >= 0 && view != null)
                ? view.GetModelView(_sourceModelIndex) : null;
            if (srcMv == null) return result;

            if (_selectedMeshOnly)
            {
                var selSet = new HashSet<int>(srcMv.SelectedDrawableIndices ?? Array.Empty<int>());
                for (int i = 0; i < result.Length; i++)
                    result[i] = result[i] && selSet.Contains(i);
            }
            if (_visibleOnly)
            {
                var drawables = srcMv.DrawableList;
                for (int i = 0; i < result.Length; i++)
                {
                    if (!result[i]) continue;
                    if (drawables?[i] != null && !drawables[i].IsVisible)
                        result[i] = false;
                }
            }
            return result;
        }

        private void SyncWeightMapToModelCount(int modelCount)
        {
            for (int i = 0; i < modelCount; i++)
                if (!_srcWeightMap.ContainsKey(i))
                    _srcWeightMap[i] = 0f;
            var remove = _srcWeightMap.Keys.Where(k => k >= modelCount).ToList();
            foreach (var k in remove) _srcWeightMap.Remove(k);
        }

        private void SyncMeshEnabled(IProjectView view)
        {
            var srcMv = (_sourceModelIndex >= 0) ? view.GetModelView(_sourceModelIndex) : null;
            int count = srcMv?.DrawableCount ?? 0;
            while (_meshEnabled.Count < count) _meshEnabled.Add(true);
            while (_meshEnabled.Count > count) _meshEnabled.RemoveAt(_meshEnabled.Count - 1);
        }

        private void SetStatus(string msg) { if (_statusLabel != null) _statusLabel.text = msg; }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private static Toggle MkToggle(string label, bool val)
        {
            var t = new Toggle(label) { value = val };
            t.style.fontSize = 10;
            return t;
        }

        private static Button MkBtn(string label, Action click)
        {
            var b = new Button(click) { text = label };
            b.style.flexGrow  = 1;
            b.style.minHeight = 20;
            b.style.fontSize  = 9;
            return b;
        }
    }
}
