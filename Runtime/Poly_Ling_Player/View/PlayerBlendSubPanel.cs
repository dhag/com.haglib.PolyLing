// PlayerBlendSubPanel.cs
// 簡易ブレンドサブパネル（Player ビルド用）。
// SimpleBlendPanelV2 の機能を UIToolkit サブパネルとして移植。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    public class PlayerBlendSubPanel
    {
        // ================================================================
        // コールバック（Viewer から設定）
        // ================================================================

        /// <summary>ブレンド適用後に GPU バッファ更新と通知を行うコールバック。</summary>
        public Action<MeshContext> OnSyncMeshPositions;

        /// <summary>トポロジー変更後の再構築コールバック（RebuildAdapter相当）。</summary>
        public Action OnNotifyTopologyChanged;

        /// <summary>再描画要求コールバック。</summary>
        public Action OnRepaint;

        // ================================================================
        // 内部状態
        // ================================================================

        private ModelContext _model;

        private int   _sourceIndex              = -1;
        private float _blendWeight              = 0f;
        private bool  _recalculateNormals       = true;
        private bool  _selectedVerticesOnly     = false;
        private bool  _matchByVertexId          = false;

        private readonly BlendPreviewState _blendPreview = new BlendPreviewState();
        private bool _isDragging = false;

        private readonly List<(int index, string name, int vertexCount)> _candidates = new List<(int, string, int)>();
        private int _selectedCandidateListIndex = -1;

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _warningLabel;
        private VisualElement _mainContent;
        private VisualElement _targetListContainer;
        private Toggle        _toggleRecalcNormals;
        private Toggle        _toggleSelectedOnly;
        private Toggle        _toggleMatchById;
        private Label         _noCandidateLabel;
        private VisualElement _candidateContainer;
        private VisualElement _mismatchContainer;
        private Label         _previewingLabel;
        private VisualElement _blendSection;
        private Slider        _sliderBlend;
        private Button        _btnApply;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingLeft   = 4;
            _root.style.paddingRight  = 4;
            _root.style.paddingTop    = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _warningLabel = new Label();
            _warningLabel.style.display     = DisplayStyle.None;
            _warningLabel.style.color       = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.whiteSpace  = WhiteSpace.Normal;
            _warningLabel.style.marginBottom = 4;
            _root.Add(_warningLabel);

            _mainContent = new VisualElement();
            _root.Add(_mainContent);

            // ── ターゲットメッシュセクション
            _mainContent.Add(SecLabel("ターゲットメッシュ"));
            _targetListContainer = new VisualElement();
            _targetListContainer.style.marginBottom = 4;
            _mainContent.Add(_targetListContainer);

            // ── オプション
            _toggleRecalcNormals = new Toggle("法線再計算")    { value = _recalculateNormals };
            _toggleSelectedOnly  = new Toggle("選択頂点のみ")   { value = _selectedVerticesOnly };
            _toggleMatchById     = new Toggle("頂点IDで照合")   { value = _matchByVertexId };
            _toggleRecalcNormals.style.marginBottom = 2;
            _toggleSelectedOnly .style.marginBottom = 2;
            _toggleMatchById    .style.marginBottom = 2;
            StyleToggle(_toggleRecalcNormals);
            StyleToggle(_toggleSelectedOnly);
            StyleToggle(_toggleMatchById);
            _toggleRecalcNormals.RegisterValueChangedCallback(e => _recalculateNormals   = e.newValue);
            _toggleSelectedOnly .RegisterValueChangedCallback(e => _selectedVerticesOnly = e.newValue);
            _toggleMatchById    .RegisterValueChangedCallback(e => _matchByVertexId      = e.newValue);
            _mainContent.Add(_toggleRecalcNormals);
            _mainContent.Add(_toggleSelectedOnly);
            _mainContent.Add(_toggleMatchById);

            _mainContent.Add(Sep());

            // ── ソースメッシュセクション
            _mainContent.Add(SecLabel("ソースメッシュ"));

            _noCandidateLabel = new Label("候補がありません");
            _noCandidateLabel.style.color   = new StyleColor(Color.gray);
            _noCandidateLabel.style.fontSize = 10;
            _noCandidateLabel.style.marginBottom = 4;
            _mainContent.Add(_noCandidateLabel);

            _candidateContainer = new VisualElement();
            _candidateContainer.style.marginBottom = 4;
            _mainContent.Add(_candidateContainer);

            _mismatchContainer = new VisualElement();
            _mismatchContainer.style.marginBottom = 4;
            _mainContent.Add(_mismatchContainer);

            _previewingLabel = new Label("プレビュー中...");
            _previewingLabel.style.display  = DisplayStyle.None;
            _previewingLabel.style.color    = new StyleColor(new Color(0.4f, 0.8f, 1f));
            _previewingLabel.style.fontSize = 10;
            _previewingLabel.style.marginBottom = 4;
            _mainContent.Add(_previewingLabel);

            _mainContent.Add(Sep());

            // ── ブレンドセクション
            _blendSection = new VisualElement();
            _blendSection.style.display = DisplayStyle.None;
            _mainContent.Add(_blendSection);

            _blendSection.Add(SecLabel("ブレンドウェイト"));

            var slRow = new VisualElement();
            slRow.style.flexDirection = FlexDirection.Row;
            slRow.style.marginBottom  = 4;
            _sliderBlend = new Slider(0f, 1f) { value = 0f };
            _sliderBlend.style.flexGrow = 1;
            _sliderBlend.RegisterValueChangedCallback(e => OnBlendSliderChanged(e.newValue));
            _sliderBlend.RegisterCallback<PointerUpEvent>(_ => _isDragging = false);
            var wLbl = new Label("0.00"); wLbl.style.width = 32; wLbl.style.unityTextAlign = TextAnchor.MiddleRight;
            _sliderBlend.RegisterValueChangedCallback(e => wLbl.text = e.newValue.ToString("F2"));
            slRow.Add(_sliderBlend); slRow.Add(wLbl);
            _blendSection.Add(slRow);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            _blendSection.Add(btnRow);

            _btnApply = new Button(OnApplyClicked) { text = "決定（バックアップ作成）" };
            _btnApply.style.flexGrow      = 1;
            _btnApply.style.marginRight   = 4;
            _btnApply.style.height        = 24;
            _btnApply.style.fontSize      = 10;
            var btnCancel = new Button(OnCancelClicked) { text = "キャンセル" };
            btnCancel.style.flexGrow = 1;
            btnCancel.style.height   = 24;
            btnCancel.style.fontSize = 10;
            btnRow.Add(_btnApply);
            btnRow.Add(btnCancel);
        }

        // ================================================================
        // モデル更新（Viewer から呼ぶ）
        // ================================================================

        public void SetModel(ModelContext model)
        {
            if (_blendPreview.IsActive) EndPreview();
            _model                      = model;
            _sourceIndex                = -1;
            _selectedCandidateListIndex = -1;
            _blendWeight                = 0f;
            _candidates.Clear();
            Refresh();
        }

        /// <summary>選択変更後に呼ぶ。</summary>
        public void OnSelectionChanged()
        {
            if (_blendPreview.IsActive) EndPreview();
            _sourceIndex                = -1;
            _selectedCandidateListIndex = -1;
            _blendWeight                = 0f;
            Refresh();
        }

        // ================================================================
        // Refresh
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return;
            var model = _model;

            if (model == null)
            {
                ShowWarning("モデルがありません"); return;
            }
            if (model.SelectedMeshIndices.Count == 0)
            {
                ShowWarning("メッシュが未選択です"); return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display  = DisplayStyle.Flex;

            RefreshTargetList(model);
            BuildCandidates(model, model.SelectedMeshIndices);
            RefreshCandidateList();
            RefreshBlendSection(model);
        }

        private void ShowWarning(string msg)
        {
            _warningLabel.text          = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display  = DisplayStyle.None;
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
                lbl.style.fontSize = 10;
                lbl.style.color    = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
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
                if (ctx.Type != MeshType.Mesh &&
                    ctx.Type != MeshType.BakedMirror &&
                    ctx.Type != MeshType.MirrorSide) continue;
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
            _candidateContainer.Clear();

            if (_candidates.Count == 0)
            {
                _noCandidateLabel.style.display  = DisplayStyle.Flex;
                _candidateContainer.style.display = DisplayStyle.None;
                return;
            }

            _noCandidateLabel.style.display  = DisplayStyle.None;
            _candidateContainer.style.display = DisplayStyle.Flex;

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c   = _candidates[i];
                int idx = i;

                var row = new Label($"  {c.name}  [V:{c.vertexCount}]");
                row.style.paddingTop    = 2;
                row.style.paddingBottom = 2;
                row.style.paddingLeft   = 4;
                row.style.fontSize      = 10;
                row.style.color         = new StyleColor(new Color(0.85f, 0.85f, 0.85f));

                if (i == _selectedCandidateListIndex)
                    row.style.backgroundColor = new StyleColor(new Color(0.24f, 0.48f, 0.9f, 0.5f));

                row.RegisterCallback<ClickEvent>(_ =>
                {
                    if (_selectedCandidateListIndex == idx) return;
                    _selectedCandidateListIndex = idx;
                    _sourceIndex = _candidates[idx].index;
                    OnSourceChanged();
                    RefreshCandidateList();
                    RefreshBlendSection(_model);
                });

                _candidateContainer.Add(row);
            }
        }

        // ================================================================
        // ブレンドセクション
        // ================================================================

        private void RefreshBlendSection(ModelContext model)
        {
            _mismatchContainer.Clear();
            if (_sourceIndex >= 0)
            {
                var srcCtx = model.GetMeshContext(_sourceIndex);
                int srcVc  = srcCtx?.MeshObject?.VertexCount ?? 0;
                foreach (int idx in model.SelectedMeshIndices)
                {
                    var tctx = model.GetMeshContext(idx);
                    if (tctx?.MeshObject == null) continue;
                    if (tctx.MeshObject.VertexCount != srcVc)
                    {
                        var warn = new Label(
                            $"頂点数不一致: {tctx.Name} ({tctx.MeshObject.VertexCount}) ≠ ソース ({srcVc})");
                        warn.style.color   = new StyleColor(new Color(1f, 0.4f, 0.4f));
                        warn.style.fontSize = 9;
                        warn.style.whiteSpace = WhiteSpace.Normal;
                        _mismatchContainer.Add(warn);
                    }
                }
            }

            _previewingLabel.style.display = _blendPreview.IsActive ? DisplayStyle.Flex : DisplayStyle.None;
            _blendSection.style.display    = _sourceIndex >= 0 ? DisplayStyle.Flex : DisplayStyle.None;

            if (_sourceIndex >= 0)
            {
                _sliderBlend.SetValueWithoutNotify(_blendWeight);
                _btnApply.SetEnabled(_blendPreview.IsActive);
            }
        }

        // ================================================================
        // スライダー操作
        // ================================================================

        private void OnBlendSliderChanged(float newValue)
        {
            var model = _model;
            if (model == null) return;

            if (!_isDragging)
            {
                _isDragging = true;
                _blendPreview.Start(model, model.SelectedMeshIndices, _sourceIndex);
            }

            _blendWeight = newValue;
            _blendPreview.Apply(model, _sourceIndex, _blendWeight,
                _selectedVerticesOnly, null, _matchByVertexId, BuildToolCtx());

            _btnApply?.SetEnabled(_blendPreview.IsActive);
            if (_previewingLabel != null)
                _previewingLabel.style.display = _blendPreview.IsActive ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ================================================================
        // ソース変更
        // ================================================================

        private void OnSourceChanged()
        {
            if (!_blendPreview.IsActive) return;
            var model = _model;
            if (model != null)
                _blendPreview.Apply(model, _sourceIndex, _blendWeight,
                    _selectedVerticesOnly, null, _matchByVertexId, BuildToolCtx());
        }

        // ================================================================
        // 決定 / キャンセル
        // ================================================================

        private void OnApplyClicked()
        {
            var model = _model;
            if (model == null) return;
            BlendOperation.ApplyAndCreateBackups(
                model, _blendPreview, model.SelectedMeshIndices, _sourceIndex,
                _blendWeight, _recalculateNormals,
                _selectedVerticesOnly, null, _matchByVertexId, BuildToolCtx());

            _blendWeight                = 0f;
            _sourceIndex                = -1;
            _selectedCandidateListIndex = -1;
            Refresh();
        }

        private void OnCancelClicked()
        {
            EndPreview();
            _blendWeight = 0f;
            _sliderBlend?.SetValueWithoutNotify(0f);
            Refresh();
        }

        private void EndPreview()
        {
            _blendPreview.End(_model, BuildToolCtx());
        }

        // ================================================================
        // ToolContext 生成（最小構成）
        // ================================================================

        private Poly_Ling.Tools.ToolContext BuildToolCtx()
        {
            var ctx = new Poly_Ling.Tools.ToolContext();
            ctx.Model   = _model;
            ctx.Repaint = OnRepaint;

            // SyncMeshContextPositionsOnly: UnityMesh + GPU バッファを更新
            ctx.SyncMeshContextPositionsOnly = mc =>
            {
                if (mc?.MeshObject == null || mc.UnityMesh == null) return;
                var wm = mc.WorldMatrix;
                if (mc.MeshObject.VertexCount == mc.UnityMesh.vertexCount)
                {
                    var verts = new Vector3[mc.MeshObject.VertexCount];
                    for (int i = 0; i < verts.Length; i++)
                        verts[i] = wm.MultiplyPoint3x4(mc.MeshObject.Vertices[i].Position);
                    mc.UnityMesh.vertices = verts;
                    mc.UnityMesh.RecalculateBounds();
                }
                OnSyncMeshPositions?.Invoke(mc);
            };

            // NotifyTopologyChanged: RebuildAdapter 相当
            ctx.NotifyTopologyChanged = OnNotifyTopologyChanged;

            return ctx;
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private static Label SecLabel(string text)
        {
            var l = new Label(text);
            l.style.color        = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize     = 10;
            l.style.marginTop    = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement Sep()
        {
            var v = new VisualElement();
            v.style.height          = 1;
            v.style.marginTop       = 3;
            v.style.marginBottom    = 3;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }

        private static void StyleToggle(Toggle t)
        {
            t.style.color    = new StyleColor(new Color(0.85f, 0.85f, 0.85f));
            t.style.fontSize = 10;
        }
    }
}
