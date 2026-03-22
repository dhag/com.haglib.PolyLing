// SimpleBlendPanelV2.cs
// 簡易ブレンドパネル V2（コード構築 UIToolkit）
// PanelContext（選択変更通知）+ ToolContext（実処理）ハイブリッド

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Commands;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Context;
using Poly_Ling.Symmetry;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public class SimpleBlendPanelV2 : EditorWindow
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _panelCtx;
        private ToolContext  _toolCtx;

        private ModelContext Model              => _toolCtx?.Model;
        private bool         HasValidSelection  => _toolCtx?.HasValidMeshSelection ?? false;

        // ================================================================
        // 設定
        // ================================================================

        private int   _sourceIndex              = -1;
        private float _blendWeight              = 0f;
        private bool  _recalculateNormals       = true;
        private bool  _selectedVerticesOnly     = false;
        private bool  _matchByVertexId          = false;

        // ================================================================
        // プレビュー状態
        // ================================================================

        private readonly BlendPreviewState _blendPreview = new();
        private bool _isDragging      = false;

        // 候補リスト
        private readonly List<(int index, string name, int vertexCount)> _candidates = new();
        private int _selectedCandidateListIndex = -1;

        // ================================================================
        // UI 要素
        // ================================================================

        private Label         _warningLabel;
        private VisualElement _mainContent;
        private VisualElement _targetListContainer;
        private Toggle        _toggleRecalcNormals;
        private Toggle        _toggleSelectedOnly;
        private Toggle        _toggleMatchById;
        private Label         _noCandidateLabel;
        private ScrollView    _candidateScroll;
        private VisualElement _mismatchContainer;
        private Label         _previewingLabel;
        private VisualElement _blendSection;
        private Slider        _sliderBlend;
        private Button        _btnApply;
        private Button        _btnCancel;

        // ================================================================
        // Open
        // ================================================================

        public static SimpleBlendPanelV2 Open(PanelContext panelCtx, ToolContext toolCtx)
        {
            var w = GetWindow<SimpleBlendPanelV2>();
            w.titleContent = new GUIContent("簡易ブレンド");
            w.minSize = new Vector2(320, 380);
            w.SetContexts(panelCtx, toolCtx);
            w.Show();
            return w;
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        private void SetContexts(PanelContext panelCtx, ToolContext toolCtx)
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;

            EndPreview();
            _panelCtx   = panelCtx;
            _toolCtx    = toolCtx;
            _sourceIndex = -1;
            _blendWeight = 0f;
            _selectedCandidateListIndex = -1;
            _candidates.Clear();

            if (_panelCtx != null) _panelCtx.OnViewChanged += OnViewChanged;

            Refresh();
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            if (_panelCtx != null)
            {
                _panelCtx.OnViewChanged -= OnViewChanged;
                _panelCtx.OnViewChanged += OnViewChanged;
            }
        }

        private void OnDisable()
        {
            EndPreview();
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void OnDestroy()
        {
            EndPreview();
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void CreateGUI()
        {
            BuildUI(rootVisualElement);
            Refresh();
        }

        // ================================================================
        // ViewChanged
        // ================================================================

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (kind == ChangeKind.Selection || kind == ChangeKind.ModelSwitch)
            {
                if (_blendPreview.IsActive) EndPreview();
                _sourceIndex = -1;
                _selectedCandidateListIndex = -1;
                _candidates.Clear();
                Refresh();
            }
        }

        // ================================================================
        // UI 構築
        // ================================================================

        private void BuildUI(VisualElement root)
        {
            root.style.paddingLeft   = 6;
            root.style.paddingRight  = 6;
            root.style.paddingTop    = 6;
            root.style.paddingBottom = 6;

            // 警告ラベル
            _warningLabel = new Label();
            _warningLabel.style.display    = DisplayStyle.None;
            _warningLabel.style.color      = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.marginBottom = 4;
            root.Add(_warningLabel);

            // メインコンテンツ
            _mainContent = new VisualElement();
            _mainContent.style.flexGrow = 1;
            root.Add(_mainContent);

            // ターゲットメッシュセクション
            _mainContent.Add(MakeSectionLabel("ターゲットメッシュ"));
            _targetListContainer = new VisualElement();
            _targetListContainer.style.marginBottom = 6;
            _mainContent.Add(_targetListContainer);

            // オプション
            _toggleRecalcNormals  = new Toggle("法線を再計算")       { value = _recalculateNormals };
            _toggleSelectedOnly   = new Toggle("選択頂点のみ")         { value = _selectedVerticesOnly };
            _toggleMatchById      = new Toggle("頂点IDで照合")         { value = _matchByVertexId };
            _toggleRecalcNormals.RegisterValueChangedCallback(e  => _recalculateNormals   = e.newValue);
            _toggleSelectedOnly.RegisterValueChangedCallback(e   => _selectedVerticesOnly = e.newValue);
            _toggleMatchById.RegisterValueChangedCallback(e      => _matchByVertexId      = e.newValue);
            _mainContent.Add(_toggleRecalcNormals);
            _mainContent.Add(_toggleSelectedOnly);
            _mainContent.Add(_toggleMatchById);

            var sep1 = new VisualElement();
            sep1.style.height = 1;
            sep1.style.backgroundColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            sep1.style.marginTop = 4;
            sep1.style.marginBottom = 4;
            _mainContent.Add(sep1);

            // ソースメッシュセクション
            _mainContent.Add(MakeSectionLabel("ソースメッシュ"));

            _noCandidateLabel = new Label("一致するメッシュがありません");
            _noCandidateLabel.style.display = DisplayStyle.None;
            _noCandidateLabel.style.color   = new StyleColor(Color.gray);
            _noCandidateLabel.style.marginBottom = 4;
            _mainContent.Add(_noCandidateLabel);

            _candidateScroll = new ScrollView();
            _candidateScroll.style.maxHeight = 120;
            _candidateScroll.style.marginBottom = 4;
            _mainContent.Add(_candidateScroll);

            // 不一致警告コンテナ
            _mismatchContainer = new VisualElement();
            _mismatchContainer.style.marginBottom = 4;
            _mainContent.Add(_mismatchContainer);

            // プレビュー中ラベル
            _previewingLabel = new Label("プレビュー中...");
            _previewingLabel.style.display = DisplayStyle.None;
            _previewingLabel.style.color   = new StyleColor(new Color(0.4f, 0.8f, 1f));
            _previewingLabel.style.marginBottom = 4;
            _mainContent.Add(_previewingLabel);

            var sep2 = new VisualElement();
            sep2.style.height = 1;
            sep2.style.backgroundColor = new StyleColor(new Color(0.3f, 0.3f, 0.3f));
            sep2.style.marginTop = 4;
            sep2.style.marginBottom = 4;
            _mainContent.Add(sep2);

            // ブレンドセクション
            _blendSection = new VisualElement();
            _blendSection.style.display = DisplayStyle.None;
            _mainContent.Add(_blendSection);

            _blendSection.Add(MakeSectionLabel("ブレンドウェイト"));

            _sliderBlend = new Slider(0f, 1f) { value = 0f };
            _sliderBlend.style.marginBottom = 6;
            _sliderBlend.RegisterValueChangedCallback(e => OnBlendSliderChanged(e.newValue));
            _sliderBlend.RegisterCallback<PointerUpEvent>(_ => OnSliderDragEnd(), TrickleDown.TrickleDown);
            _blendSection.Add(_sliderBlend);

            // ボタン行
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.SpaceBetween;
            _blendSection.Add(btnRow);

            _btnApply = new Button(OnApplyClicked)  { text = "決定（バックアップ作成）" };
            _btnApply.style.flexGrow = 1;
            _btnApply.style.marginRight = 4;
            _btnCancel = new Button(OnCancelClicked) { text = "キャンセル" };
            _btnCancel.style.flexGrow = 1;
            btnRow.Add(_btnApply);
            btnRow.Add(_btnCancel);
        }

        private static Label MakeSectionLabel(string text)
        {
            var l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginBottom = 2;
            return l;
        }

        // ================================================================
        // リフレッシュ
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return;

            if (_toolCtx == null)
            {
                ShowWarning("ToolContext 未設定。Poly_Ling ウィンドウから開いてください。");
                return;
            }

            var model = Model;
            if (model == null)
            {
                ShowWarning("モデルがありません");
                return;
            }

            if (!HasValidSelection)
            {
                ShowWarning("メッシュが未選択です");
                return;
            }

            _warningLabel.style.display  = DisplayStyle.None;
            _mainContent.style.display   = DisplayStyle.Flex;

            RefreshTargetList(model);

            var targetIndices = model.SelectedMeshIndices;
            BuildCandidates(model, targetIndices);
            RefreshCandidateList();
            RefreshBlendSection(model, targetIndices);
        }

        private void ShowWarning(string message)
        {
            _warningLabel.text           = message;
            _warningLabel.style.display  = DisplayStyle.Flex;
            _mainContent.style.display   = DisplayStyle.None;
        }

        // ================================================================
        // ターゲットリスト
        // ================================================================

        private void RefreshTargetList(ModelContext model)
        {
            _targetListContainer.Clear();
            foreach (int idx in model.SelectedMeshIndices)
            {
                var ctx = model.GetMeshContext(idx);
                if (ctx?.MeshObject == null) continue;
                var lbl = new Label($"  {ctx.Name}  [V:{ctx.MeshObject.VertexCount}]");
                lbl.style.fontSize = 11;
                _targetListContainer.Add(lbl);
            }
        }

        // ================================================================
        // 候補リスト
        // ================================================================

        private void BuildCandidates(ModelContext model, List<int> targetIndices)
        {
            _candidates.Clear();
            var targetSet = new HashSet<int>(targetIndices);

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                if (targetSet.Contains(i)) continue;
                var ctx = model.GetMeshContext(i);
                if (ctx?.MeshObject == null || ctx.MeshObject.VertexCount == 0) continue;
                if (ctx.Type != MeshType.Mesh && ctx.Type != MeshType.BakedMirror && ctx.Type != MeshType.MirrorSide) continue;
                _candidates.Add((i, ctx.Name, ctx.MeshObject.VertexCount));
            }

            if (_selectedCandidateListIndex >= 0)
            {
                _selectedCandidateListIndex = _candidates.FindIndex(c => c.index == _sourceIndex);
                if (_selectedCandidateListIndex < 0) _sourceIndex = -1;
            }
        }

        private void RefreshCandidateList()
        {
            _candidateScroll.Clear();

            if (_candidates.Count == 0)
            {
                _noCandidateLabel.style.display  = DisplayStyle.Flex;
                _candidateScroll.style.display   = DisplayStyle.None;
                return;
            }

            _noCandidateLabel.style.display  = DisplayStyle.None;
            _candidateScroll.style.display   = DisplayStyle.Flex;

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c   = _candidates[i];
                int idx = i;

                var row = new Label($"  {c.name}  [V:{c.vertexCount}]");
                row.style.paddingTop    = 2;
                row.style.paddingBottom = 2;
                row.style.paddingLeft   = 4;

                if (i == _selectedCandidateListIndex)
                    row.style.backgroundColor = new StyleColor(new Color(0.24f, 0.48f, 0.9f, 0.5f));

                row.RegisterCallback<ClickEvent>(_ =>
                {
                    if (_selectedCandidateListIndex == idx) return;
                    _selectedCandidateListIndex = idx;
                    _sourceIndex = _candidates[idx].index;
                    OnSourceChanged();
                    RefreshCandidateList();
                    RefreshBlendSection(Model, Model?.SelectedMeshIndices);
                });

                _candidateScroll.Add(row);
            }
        }

        // ================================================================
        // ブレンドセクション
        // ================================================================

        private void RefreshBlendSection(ModelContext model, List<int> targetIndices)
        {
            _mismatchContainer.Clear();
            if (_sourceIndex >= 0 && targetIndices != null)
            {
                var sourceCtx     = model.GetMeshContext(_sourceIndex);
                int sourceVertCnt = sourceCtx?.MeshObject?.VertexCount ?? 0;
                foreach (int idx in targetIndices)
                {
                    var tctx = model.GetMeshContext(idx);
                    if (tctx?.MeshObject == null) continue;
                    if (tctx.MeshObject.VertexCount != sourceVertCnt)
                    {
                        var warn = new Label($"頂点数不一致: {tctx.Name} ({tctx.MeshObject.VertexCount}) ≠ ソース ({sourceVertCnt})");
                        warn.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
                        warn.style.fontSize = 10;
                        _mismatchContainer.Add(warn);
                    }
                }
            }

            _previewingLabel.style.display = _blendPreview.IsActive ? DisplayStyle.Flex : DisplayStyle.None;

            if (_sourceIndex < 0)
            {
                _blendSection.style.display = DisplayStyle.None;
                return;
            }

            _blendSection.style.display = DisplayStyle.Flex;
            _sliderBlend.SetValueWithoutNotify(_blendWeight);
            _btnApply.SetEnabled(_blendPreview.IsActive);
        }

        // ================================================================
        // ソース変更
        // ================================================================

        private void OnSourceChanged()
        {
            if (!_blendPreview.IsActive) return;
            var model = Model;
            if (model != null) ApplyPreview(model, model.SelectedMeshIndices);
        }

        // ================================================================
        // スライダー操作
        // ================================================================

        private void OnBlendSliderChanged(float newValue)
        {
            var model = Model;
            if (model == null) return;

            var targetIndices = model.SelectedMeshIndices;

            if (!_isDragging)
            {
                _isDragging = true;
                StartPreview(model, targetIndices);
            }

            _blendWeight = newValue;
            ApplyPreview(model, targetIndices);
            _btnApply?.SetEnabled(_blendPreview.IsActive);
            _previewingLabel.style.display = _blendPreview.IsActive ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnSliderDragEnd()
        {
            _isDragging = false;
        }

        // ================================================================
        // ボタンハンドラ
        // ================================================================

        private void OnApplyClicked()
        {
            var model = Model;
            if (model == null) return;
            ApplyAndCreateBackups(model, model.SelectedMeshIndices);
        }

        private void OnCancelClicked()
        {
            EndPreview();
            _blendWeight = 0f;
            _sliderBlend?.SetValueWithoutNotify(0f);
            Refresh();
        }

        // ================================================================
        // プレビュー開始
        // ================================================================

        private void StartPreview(ModelContext model, List<int> targetIndices)
        {
            _blendPreview.Start(model, targetIndices, _sourceIndex);
        }

        // ================================================================
        // プレビュー適用
        // ================================================================

        private void ApplyPreview(ModelContext model, List<int> targetIndices)
        {
            _blendPreview.Apply(model, _sourceIndex, _blendWeight,
                _selectedVerticesOnly, _toolCtx?.SelectedVertices,
                _matchByVertexId, _toolCtx);
        }

        // ================================================================
        // プレビュー終了
        // ================================================================

        private void EndPreview()
        {
            _blendPreview.End(Model, _toolCtx);
        }

        // ================================================================
        // 決定（バックアップ作成）
        // ================================================================

        private void ApplyAndCreateBackups(ModelContext model, List<int> targetIndices)
        {
            int backupCount = BlendOperation.ApplyAndCreateBackups(
                model, _blendPreview, targetIndices, _sourceIndex,
                _blendWeight, _recalculateNormals,
                _selectedVerticesOnly, _toolCtx?.SelectedVertices,
                _matchByVertexId, _toolCtx);

            _blendWeight                = 0f;
            _sourceIndex                = -1;
            _selectedCandidateListIndex = -1;

            UnityEngine.Debug.Log($"[PolyLing] ブレンド適用。バックアップ {backupCount} 個作成。");
            Refresh();
        }






    }
}
