// MultiModelBlendPanelV2.cs
// モデルブレンドパネル（新形式）
// - パネルオープン時にソースモデルのクローンを作成しカレントモデルに切り替え
// - スライダー変更でリアルタイムプレビュー（PreviewModelBlendCommand）
// - Apply ボタンで法線再計算付き確定（ApplyModelBlendCommand）
// - スライダーにはクローン自身を含めない

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Data;

namespace Poly_Ling.UI
{
    public class MultiModelBlendPanelV2 : EditorWindow
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _ctx;
        private bool _isReceiving;

        // ================================================================
        // ブレンド状態
        // ================================================================

        private int  _sourceModelIndex   = -1;
        private int  _cloneModelIndex    = -1;
        private int  _preCloneModelCount = -1;

        // プロジェクトインデックス → ウェイト（クローンは常に0）
        private readonly Dictionary<int, float> _srcWeightMap = new Dictionary<int, float>();
        private List<bool>  _meshEnabled  = new List<bool>();
        private bool _recalcNormals    = true;
        private bool _realtimePreview  = true;
        private bool _blendBones       = false;
        private bool _selectedMeshOnly = false;
        private bool _visibleOnly      = true;

        // スライダー参照: プロジェクトインデックス → Slider
        private readonly Dictionary<int, Slider> _sliderMap = new Dictionary<int, Slider>();

        // ================================================================
        // UI 要素
        // ================================================================

        private Label         _warningLabel;
        private Label         _cloneInfoLabel;
        private Label         _targetInfoLabel;
        private Foldout       _meshFoldout;
        private VisualElement _meshToggleContainer;
        private VisualElement _sliderContainer;
        private Toggle        _toggleRecalc;
        private Toggle        _toggleRealtime;
        private Toggle        _toggleBlendBones;
        private Toggle        _toggleSelectedOnly;
        private Toggle        _toggleVisibleOnly;
        private Label         _totalWeightLabel;
        private Button        _btnEqual, _btnNormalize, _btnResetFirst;
        private Button        _btnApply;
        private Button        _btnCancel;
        private Label         _statusLabel;

        // ================================================================
        // プロパティ
        // ================================================================

        private IProjectView CurrentView => _ctx?.CurrentView;

        // ================================================================
        // ウィンドウ
        // ================================================================

        [MenuItem("Tools/Poly_Ling/Model Blend V2")]
        public static void ShowWindow()
        {
            var w = GetWindow<MultiModelBlendPanelV2>();
            w.titleContent = new GUIContent("モデルブレンド V2");
            w.minSize = new Vector2(380, 360);
        }

        public static MultiModelBlendPanelV2 Open(PanelContext ctx)
        {
            var w = GetWindow<MultiModelBlendPanelV2>();
            w.titleContent = new GUIContent("モデルブレンド V2");
            w.minSize = new Vector2(380, 360);
            w.SetContext(ctx);
            w.Show();
            return w;
        }

        private void SetContext(PanelContext ctx)
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
            _ctx = ctx;
            if (_ctx != null)
            {
                _ctx.OnViewChanged += OnViewChanged;
                if (_ctx.CurrentView != null) InitBlend(_ctx.CurrentView);
            }
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            if (_ctx != null) { _ctx.OnViewChanged -= OnViewChanged; _ctx.OnViewChanged += OnViewChanged; }
        }

        private void OnDisable()
        {
            if (_ctx != null) _ctx.OnViewChanged -= OnViewChanged;
        }

        private void CreateGUI()
        {
            BuildUI(rootVisualElement);
            if (_ctx?.CurrentView != null) InitBlend(_ctx.CurrentView);
        }

        // ================================================================
        // ブレンド初期化
        // ================================================================

        private void InitBlend(IProjectView view)
        {
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

            var sourceName = view.CurrentModel?.Name ?? "Model";
            SendCmd(new CreateBlendCloneCommand(_sourceModelIndex, sourceName + "_blend"));
        }

        // ================================================================
        // ViewChanged
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (_isReceiving) return;
            _isReceiving = true;
            try
            {
                // クローン未確定 かつ モデル数が増加 → クローン確定 → カレントモデル切り替え
                if (_cloneModelIndex < 0
                    && _preCloneModelCount >= 0
                    && view.ModelCount == _preCloneModelCount + 1)
                {
                    _cloneModelIndex = view.ModelCount - 1;
                    _srcWeightMap[_cloneModelIndex] = 0f;
                    SendCmd(new SwitchModelCommand(_cloneModelIndex));
                    Refresh(CurrentView ?? view);
                    return;
                }

                Refresh(view);
            }
            finally { EditorApplication.delayCall += () => _isReceiving = false; }
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft = 4; root.style.paddingRight = 4;
            root.style.paddingTop  = 4; root.style.paddingBottom = 4;

            _warningLabel = new Label();
            _warningLabel.style.color = new Color(0.8f, 0.8f, 0.3f);
            _warningLabel.style.display = DisplayStyle.None;
            root.Add(_warningLabel);

            _targetInfoLabel = new Label();
            _targetInfoLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _targetInfoLabel.style.marginBottom = 2;
            root.Add(_targetInfoLabel);

            _cloneInfoLabel = new Label();
            _cloneInfoLabel.style.color = new Color(0.5f, 0.9f, 0.5f);
            _cloneInfoLabel.style.marginBottom = 6;
            root.Add(_cloneInfoLabel);

            _meshFoldout = new Foldout { text = "ブレンド対象メッシュ", value = false };
            _meshFoldout.style.marginBottom = 4;
            _meshToggleContainer = new VisualElement();
            _meshFoldout.Add(_meshToggleContainer);
            root.Add(_meshFoldout);

            var optRow = new VisualElement();
            optRow.style.flexDirection = FlexDirection.Row;
            optRow.style.marginBottom = 4; optRow.style.height = 22;
            _toggleRecalc = new Toggle("法線再計算") { value = _recalcNormals };
            _toggleRecalc.style.flexGrow = 1;
            _toggleRecalc.RegisterValueChangedCallback(e => _recalcNormals = e.newValue);
            _toggleRealtime = new Toggle("リアルタイム") { value = _realtimePreview };
            _toggleRealtime.style.flexGrow = 1;
            _toggleRealtime.RegisterValueChangedCallback(e => _realtimePreview = e.newValue);
            optRow.Add(_toggleRecalc); optRow.Add(_toggleRealtime);
            root.Add(optRow);

            var optRow2 = new VisualElement();
            optRow2.style.flexDirection = FlexDirection.Row;
            optRow2.style.marginBottom = 4; optRow2.style.height = 22;
            _toggleBlendBones = new Toggle("ボーンもブレンド") { value = _blendBones };
            _toggleBlendBones.style.flexGrow = 1;
            _toggleBlendBones.RegisterValueChangedCallback(e =>
            {
                _blendBones = e.newValue;
                CheckBoneWarning(CurrentView);
                if (_realtimePreview) SendPreview();
            });
            _toggleSelectedOnly = new Toggle("選択メッシュのみ") { value = _selectedMeshOnly };
            _toggleSelectedOnly.style.flexGrow = 1;
            _toggleSelectedOnly.RegisterValueChangedCallback(e =>
            {
                _selectedMeshOnly = e.newValue;
                if (_realtimePreview) SendPreview();
            });
            _toggleVisibleOnly = new Toggle("可視のみ") { value = _visibleOnly };
            _toggleVisibleOnly.style.flexGrow = 1;
            _toggleVisibleOnly.RegisterValueChangedCallback(e =>
            {
                _visibleOnly = e.newValue;
                if (_realtimePreview) SendPreview();
            });
            optRow2.Add(_toggleBlendBones);
            optRow2.Add(_toggleSelectedOnly);
            optRow2.Add(_toggleVisibleOnly);
            root.Add(optRow2);

            _sliderContainer = new VisualElement();
            _sliderContainer.style.marginBottom = 4;
            root.Add(_sliderContainer);

            _totalWeightLabel = new Label("合計: 0.000");
            _totalWeightLabel.style.marginBottom = 4;
            root.Add(_totalWeightLabel);

            var wRow = new VisualElement();
            wRow.style.flexDirection = FlexDirection.Row;
            wRow.style.marginBottom = 6; wRow.style.height = 24;
            _btnEqual      = MkBtn("均等",           OnEqualWeights);
            _btnNormalize  = MkBtn("正規化",          OnNormalize);
            _btnResetFirst = MkBtn("ソースにリセット", OnResetFirst);
            wRow.Add(_btnEqual); wRow.Add(_btnNormalize); wRow.Add(_btnResetFirst);
            root.Add(wRow);

            _btnApply = new Button(OnApply) { text = "クローンに適用" };
            _btnApply.style.height = 28;
            _btnApply.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(_btnApply);

            _btnCancel = new Button(OnCancel) { text = "クローンを削除して閉じる" };
            _btnCancel.style.height = 24;
            _btnCancel.style.marginTop = 4;
            _btnCancel.style.color = new Color(1f, 0.55f, 0.55f);
            root.Add(_btnCancel);

            _statusLabel = new Label("");
            _statusLabel.style.marginTop = 4; _statusLabel.style.fontSize = 10;
            _statusLabel.style.color = new Color(0.6f, 0.6f, 0.6f);
            root.Add(_statusLabel);
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void Refresh(IProjectView view)
        {
            if (_warningLabel == null) return;

            if (view == null || view.ModelCount < 2)
            {
                ShowWarning(view == null ? "プロジェクトがありません" : "モデルが2つ以上必要です");
                return;
            }
            _warningLabel.style.display = DisplayStyle.None;

            var srcModel = (_sourceModelIndex >= 0 && _sourceModelIndex < view.ModelCount)
                ? view.GetModelView(_sourceModelIndex) : null;
            _targetInfoLabel.text = $"ソース: {srcModel?.Name ?? "---"}  ({srcModel?.DrawableCount ?? 0} mesh)";

            if (_cloneModelIndex >= 0 && _cloneModelIndex < view.ModelCount)
            {
                var cloneModel = view.GetModelView(_cloneModelIndex);
                _cloneInfoLabel.text = $"クローン[{_cloneModelIndex}]: {cloneModel?.Name ?? "---"}";
            }
            else
            {
                _cloneInfoLabel.text = "クローン作成中...";
            }

            SyncWeightMapToModelCount(view.ModelCount);
            SyncMeshEnabled(view);
            RebuildMeshToggles(view);
            RebuildSliders(view);
            UpdateTotalWeightLabel();
            _btnApply.SetEnabled(_cloneModelIndex >= 0);
            _btnCancel?.SetEnabled(_cloneModelIndex >= 0);
        }

        private void ShowWarning(string msg)
        {
            if (_warningLabel == null) return;
            _warningLabel.text = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
        }

        // ================================================================
        // メッシュトグル
        // ================================================================

        private void RebuildMeshToggles(IProjectView view)
        {
            _meshToggleContainer.Clear();
            var srcModel = (_sourceModelIndex >= 0) ? view.GetModelView(_sourceModelIndex) : null;
            int count = srcModel?.DrawableCount ?? 0;
            _meshFoldout.text = $"ブレンド対象メッシュ ({_meshEnabled.Count(e => e)}/{count})";

            for (int i = 0; i < count; i++)
            {
                string meshName = srcModel?.DrawableList?[i]?.Name ?? $"Mesh[{i}]";
                int vertCount   = srcModel?.DrawableList?[i]?.VertexCount ?? 0;
                int ci = i;
                var tog = new Toggle($"{meshName} ({vertCount}V)") { value = _meshEnabled[i] };
                tog.RegisterValueChangedCallback(e =>
                {
                    if (ci < _meshEnabled.Count) _meshEnabled[ci] = e.newValue;
                    _meshFoldout.text = $"ブレンド対象メッシュ ({_meshEnabled.Count(x => x)}/{count})";
                    if (_realtimePreview) SendPreview();
                });
                _meshToggleContainer.Add(tog);
            }
        }

        // ================================================================
        // スライダー（クローン自身は除外）
        // ================================================================

        private void RebuildSliders(IProjectView view)
        {
            _sliderContainer.Clear();
            _sliderMap.Clear();

            int modelCount = view.ModelCount;
            for (int i = 0; i < modelCount; i++)
            {
                if (i == _cloneModelIndex) continue;

                var mv = view.GetModelView(i);
                if (mv == null) continue;

                bool isSrc = (i == _sourceModelIndex);
                float weight = _srcWeightMap.TryGetValue(i, out var w) ? w : 0f;

                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems    = Align.Center;
                row.style.height = 24; row.style.marginBottom = 2;

                var nameLbl = new Label(isSrc ? $"★ {mv.Name}" : mv.Name);
                nameLbl.style.width = 130;
                nameLbl.style.overflow = Overflow.Hidden;
                nameLbl.style.textOverflow = TextOverflow.Ellipsis;
                if (isSrc) nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                row.Add(nameLbl);

                int ci = i;
                var slider = new Slider(0f, 1f) { value = weight, showInputField = true };
                slider.style.flexGrow = 1;
                slider.RegisterValueChangedCallback(e =>
                {
                    _srcWeightMap[ci] = e.newValue;
                    UpdateTotalWeightLabel();
                    if (_realtimePreview) SendPreview();
                });
                row.Add(slider);
                _sliderMap[i] = slider;
                _sliderContainer.Add(row);
            }
        }

        // ================================================================
        // ウェイト操作
        // ================================================================

        private void OnEqualWeights()
        {
            var srcKeys = _srcWeightMap.Keys.Where(k => k != _cloneModelIndex).ToList();
            if (srcKeys.Count == 0) return;
            float eq = 1f / srcKeys.Count;
            foreach (var k in srcKeys) _srcWeightMap[k] = eq;
            SyncSlidersFromWeights();
            UpdateTotalWeightLabel();
            if (_realtimePreview) SendPreview();
        }

        private void OnNormalize()
        {
            var srcKeys = _srcWeightMap.Keys.Where(k => k != _cloneModelIndex).ToList();
            float total = srcKeys.Sum(k => _srcWeightMap[k]);
            if (total <= 0f) { OnEqualWeights(); return; }
            foreach (var k in srcKeys) _srcWeightMap[k] /= total;
            SyncSlidersFromWeights();
            UpdateTotalWeightLabel();
            if (_realtimePreview) SendPreview();
        }

        private void OnResetFirst()
        {
            var srcKeys = _srcWeightMap.Keys.Where(k => k != _cloneModelIndex).ToList();
            foreach (var k in srcKeys) _srcWeightMap[k] = (k == _sourceModelIndex) ? 1f : 0f;
            SyncSlidersFromWeights();
            UpdateTotalWeightLabel();
            if (_realtimePreview) SendPreview();
        }

        private void SyncSlidersFromWeights()
        {
            foreach (var kv in _sliderMap)
            {
                if (_srcWeightMap.TryGetValue(kv.Key, out float w))
                    kv.Value.SetValueWithoutNotify(w);
            }
        }

        private void UpdateTotalWeightLabel()
        {
            if (_totalWeightLabel == null) return;
            float total = _srcWeightMap
                .Where(kv => kv.Key != _cloneModelIndex)
                .Sum(kv => kv.Value);
            _totalWeightLabel.text = $"合計: {total:F3}";
        }

        // ================================================================
        // プレビュー / Apply
        // ================================================================

        private void SendPreview()
        {
            if (_cloneModelIndex < 0) return;
            SendCmd(new PreviewModelBlendCommand(
                _sourceModelIndex,
                _cloneModelIndex,
                BuildWeightArray(),
                BuildMeshEnabledArray(CurrentView),
                _blendBones));
        }


        private void OnApply()
        {
            if (_cloneModelIndex < 0) { SetStatus("クローンが未作成です"); return; }
            SendCmd(new ApplyModelBlendCommand(
                _sourceModelIndex,
                _cloneModelIndex,
                BuildWeightArray(),
                BuildMeshEnabledArray(CurrentView),
                _recalcNormals,
                _blendBones));
            SetStatus("適用しました");
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void OnCancel()
        {
            if (_cloneModelIndex >= 0)
            {
                SendCmd(new DeleteModelCommand(_cloneModelIndex));
                SendCmd(new SwitchModelCommand(_sourceModelIndex));
            }
            Close();
        }

        /// <summary>
        /// _meshEnabled を基に _visibleOnly / _selectedMeshOnly フィルタを適用した bool[] を返す。
        /// </summary>
        private bool[] BuildMeshEnabledArray(IProjectView view)
        {
            var result = _meshEnabled.ToArray();
            if (result.Length == 0) return result;

            var srcModel = (_sourceModelIndex >= 0 && view != null)
                ? view.GetModelView(_sourceModelIndex) : null;
            if (srcModel == null) return result;

            // 選択メッシュのみフィルタ
            if (_selectedMeshOnly)
            {
                var selectedSet = new HashSet<int>(srcModel.SelectedDrawableIndices ?? System.Array.Empty<int>());

                // ミラーペア補完: RealSide が選択されていたら隣接 MirrorSide も、逆も然り
                var drawables = srcModel.DrawableList;
                if (drawables != null)
                {
                    // Real → Mirror インデックスマップ（Name+"+" 照合）
                    var nameToIdx = new Dictionary<string, int>();
                    for (int i = 0; i < drawables.Count; i++)
                    {
                        var mv = drawables[i];
                        if (mv != null && !string.IsNullOrEmpty(mv.Name))
                            nameToIdx[mv.Name] = i;
                    }
                    var extraIndices = new HashSet<int>();
                    for (int i = 0; i < drawables.Count; i++)
                    {
                        if (!selectedSet.Contains(i)) continue;
                        var mv = drawables[i];
                        if (mv == null) continue;
                        if (mv.IsRealSide && nameToIdx.TryGetValue(mv.Name + "+", out int mirrorIdx))
                            extraIndices.Add(mirrorIdx);
                        if (mv.IsMirrorSide)
                        {
                            string realName = mv.Name.EndsWith("+")
                                ? mv.Name.Substring(0, mv.Name.Length - 1) : null;
                            if (realName != null && nameToIdx.TryGetValue(realName, out int realIdx))
                                extraIndices.Add(realIdx);
                        }
                    }
                    foreach (int idx in extraIndices) selectedSet.Add(idx);
                }

                for (int i = 0; i < result.Length; i++)
                    result[i] = result[i] && selectedSet.Contains(i);
            }

            // 可視のみフィルタ
            if (_visibleOnly)
            {
                var drawables = srcModel.DrawableList;
                for (int i = 0; i < result.Length; i++)
                {
                    if (!result[i]) continue;
                    var mv = drawables?[i];
                    if (mv != null && !mv.IsVisible)
                        result[i] = false;
                }
            }

            return result;
        }

        /// <summary>
        /// _blendBones==true かつウェイト>0 かつ BoneCount==0 のソースモデルがあれば警告表示。
        /// </summary>
        private void CheckBoneWarning(IProjectView view)
        {
            if (!_blendBones || view == null)
            {
                SetStatus("");
                return;
            }
            for (int i = 0; i < view.ModelCount; i++)
            {
                if (i == _cloneModelIndex) continue;
                if (!_srcWeightMap.TryGetValue(i, out float w) || w <= 0f) continue;
                var mv = view.GetModelView(i);
                if (mv != null && mv.BoneCount == 0)
                {
                    SetStatus($"警告: {mv.Name} はボーンがありません");
                    return;
                }
            }
            SetStatus("");
        }

        private float[] BuildWeightArray()
        {
            var view = CurrentView;
            int count = view?.ModelCount ?? 0;
            var arr = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (i == _cloneModelIndex) { arr[i] = 0f; continue; }
                arr[i] = _srcWeightMap.TryGetValue(i, out var w) ? w : 0f;
            }
            return arr;
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
            var srcModel = (_sourceModelIndex >= 0) ? view.GetModelView(_sourceModelIndex) : null;
            int count = srcModel?.DrawableCount ?? 0;
            while (_meshEnabled.Count < count)  _meshEnabled.Add(true);
            while (_meshEnabled.Count > count)  _meshEnabled.RemoveAt(_meshEnabled.Count - 1);
        }

        private void SendCmd(PanelCommand c) => _ctx?.SendCommand(c);
        private void SetStatus(string msg) { if (_statusLabel != null) _statusLabel.text = msg; }

        private static Button MkBtn(string label, Action click)
        {
            var b = new Button(click) { text = label };
            b.style.flexGrow = 1; b.style.minHeight = 22;
            return b;
        }
    }
}
