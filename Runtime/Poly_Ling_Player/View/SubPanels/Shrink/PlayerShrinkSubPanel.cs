// PlayerShrinkSubPanel.cs
// シュリンカー サブパネル（Player ビルド用）。
// 衝突対象オブジェクト群（複数）を指定し、ビフォー→アフターへ
// スライダーで頂点を動かす。衝突した頂点はそこで停止する。
// Runtime/Poly_Ling_Player/View/SubPanels/Shrink/ に配置
//
// 【設計】
// 停止パラメータはコライダー・ビフォー・アフターが固定である限り不変なので、
// 「衝突計算」ボタン押下時に1回だけ算出する。スライダー操作は Lerp のみで、
// 衝突計算も GPU 読み戻しも発生しない。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    public class PlayerShrinkSubPanel
    {
        // ================================================================
        // コールバック（Viewer から設定）
        // ================================================================

        /// <summary>頂点位置更新後の GPU バッファ同期。</summary>
        public Action<MeshContext> OnSyncMeshPositions;

        /// <summary>トポロジー変更後の再構築。</summary>
        public Action OnNotifyTopologyChanged;

        /// <summary>再描画要求。</summary>
        public Action OnRepaint;

        /// <summary>Undo記録用の UndoController 取得。</summary>
        public Func<Poly_Ling.UndoSystem.MeshUndoController> GetUndoController;

        /// <summary>Undo記録用の CommandQueue 取得。</summary>
        public Func<Poly_Ling.Commands.CommandQueue> GetCommandQueue;

        /// <summary>
        /// 指定 MeshContext の全頂点ワールド座標を返す。
        /// GPU が計算した値（GetDisplayPositions）を参照する経路を配線すること。
        /// </summary>
        public Func<MeshContext, Vector3[]> GetWorldPositions;

        /// <summary>
        /// ワールド座標の再計算要求（UpdateTransform）。
        /// 衝突計算の直前に1回だけ呼ぶ。毎フレーム呼んではならない。
        /// </summary>
        public Action OnRequestUpdateTransform;

        // コマンド送信
        private PanelContext _panelContext;
        private Func<int>    _getModelIndex;

        public void SetCommandContext(PanelContext ctx, Func<int> getModelIndex)
        {
            _panelContext  = ctx;
            _getModelIndex = getModelIndex;
        }

        // ================================================================
        // 内部状態
        // ================================================================

        private ModelContext _model;

        private int   _beforeIndex        = -1;
        private int   _afterIndex         = -1;
        private readonly HashSet<int> _colliderIndices = new HashSet<int>();

        private float _slider             = 0f;
        private float _surfaceOffset      = 0.001f;
        // 既定は「両面にぶつかる」。表面のみにすると、衝突対象の内側から外へ向かう
        // 移動は一切止まらなくなる（出口面が候補から外れるため）。
        private bool  _frontFaceOnly      = false;
        private bool  _recalculateNormals = true;
        private bool  _createNewObject    = true;

        private readonly ShrinkPreviewState _preview = new ShrinkPreviewState();
        private float[] _stopParams;

        private readonly List<(int index, string name, int vertexCount)> _candidates
            = new List<(int, string, int)>();

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label         _warningLabel;
        private VisualElement _mainContent;
        private VisualElement _beforeListContainer;
        private VisualElement _afterListContainer;
        private VisualElement _colliderListContainer;
        private FloatField    _offsetField;
        private RadioButtonGroup _backfaceModeGroup;
        private Toggle        _toggleRecalcNormals;
        private RadioButtonGroup _resultModeGroup;
        private Button        _btnCompute;
        private Label         _statusLabel;
        private VisualElement _shrinkSection;
        private Slider        _sliderShrink;
        private Label         _sliderValueLabel;
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
            _warningLabel.style.display      = DisplayStyle.None;
            _warningLabel.style.color        = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.whiteSpace   = WhiteSpace.Normal;
            _warningLabel.style.marginBottom = 4;
            _root.Add(_warningLabel);

            _mainContent = new VisualElement();
            _root.Add(_mainContent);

            // ── ビフォー
            _mainContent.Add(SecLabel("ビフォー（変形対象）"));
            _beforeListContainer = new VisualElement();
            _beforeListContainer.style.marginBottom = 4;
            _mainContent.Add(_beforeListContainer);

            // ── アフター
            _mainContent.Add(SecLabel("アフター（目標形状）"));
            _afterListContainer = new VisualElement();
            _afterListContainer.style.marginBottom = 4;
            _mainContent.Add(_afterListContainer);

            _mainContent.Add(Sep());

            // ── 衝突対象
            _mainContent.Add(SecLabel("衝突対象オブジェクト（複数可）"));
            _colliderListContainer = new VisualElement();
            _colliderListContainer.style.marginBottom = 4;
            _mainContent.Add(_colliderListContainer);

            _mainContent.Add(Sep());

            // ── オプション
            _offsetField = new FloatField("面からの余白") { value = _surfaceOffset };
            _offsetField.style.fontSize     = 10;
            _offsetField.style.marginBottom = 2;
            _offsetField.RegisterValueChangedCallback(e => _surfaceOffset = Mathf.Max(0f, e.newValue));
            _mainContent.Add(_offsetField);

            // ── 裏面判定（既定は両面）
            _mainContent.Add(SecLabel("衝突対象の裏面"));
            var backfaceChoices = new List<string>
            {
                "ぶつかる（既定）",
                "ぶつからない（表面のみ）",
            };
            _backfaceModeGroup = new RadioButtonGroup(null, backfaceChoices) { value = _frontFaceOnly ? 1 : 0 };
            _backfaceModeGroup.style.marginBottom = 4;
            _backfaceModeGroup.RegisterValueChangedCallback(e =>
            {
                bool newFrontFaceOnly = (e.newValue == 1);
                if (_frontFaceOnly == newFrontFaceOnly) return;
                _frontFaceOnly = newFrontFaceOnly;
                // 停止パラメータが変わるため、計算済みのプレビューは破棄する
                CancelComputation();
                _shrinkSection.style.display = DisplayStyle.None;
            });
            _mainContent.Add(_backfaceModeGroup);

            _toggleRecalcNormals = new Toggle("法線再計算") { value = _recalculateNormals };
            _toggleRecalcNormals.style.fontSize     = 10;
            _toggleRecalcNormals.style.marginBottom = 4;
            _toggleRecalcNormals.RegisterValueChangedCallback(e => _recalculateNormals = e.newValue);
            _mainContent.Add(_toggleRecalcNormals);

            // ── 生成物モード（既定は新規オブジェクト）
            _mainContent.Add(SecLabel("生成物"));
            var modeChoices = new List<string>
            {
                "新規オブジェクト（ビフォー/アフターは非表示）",
                "ビフォーを上書き（バックアップ作成）",
            };
            _resultModeGroup = new RadioButtonGroup(null, modeChoices) { value = 0 };
            _resultModeGroup.style.marginBottom = 4;
            _resultModeGroup.RegisterValueChangedCallback(e => _createNewObject = (e.newValue == 0));
            _mainContent.Add(_resultModeGroup);

            _btnCompute = new Button(OnComputeClicked) { text = "衝突計算" };
            _btnCompute.style.height       = 24;
            _btnCompute.style.fontSize     = 10;
            _btnCompute.style.marginBottom = 4;
            _mainContent.Add(_btnCompute);

            _statusLabel = new Label();
            _statusLabel.style.fontSize     = 9;
            _statusLabel.style.whiteSpace   = WhiteSpace.Normal;
            _statusLabel.style.color        = new StyleColor(new Color(0.4f, 0.8f, 1f));
            _statusLabel.style.marginBottom = 4;
            _mainContent.Add(_statusLabel);

            _mainContent.Add(Sep());

            // ── シュリンクスライダー
            _shrinkSection = new VisualElement();
            _shrinkSection.style.display = DisplayStyle.None;
            _mainContent.Add(_shrinkSection);

            _shrinkSection.Add(SecLabel("シュリンク量"));

            var slRow = new VisualElement();
            slRow.style.flexDirection = FlexDirection.Row;
            slRow.style.marginBottom  = 4;
            _sliderShrink = new Slider(0f, 1f) { value = 0f };
            _sliderShrink.style.flexGrow = 1;
            _sliderShrink.RegisterValueChangedCallback(e => OnSliderChanged(e.newValue));
            _sliderValueLabel = new Label("0.00");
            _sliderValueLabel.style.width          = 32;
            _sliderValueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            slRow.Add(_sliderShrink);
            slRow.Add(_sliderValueLabel);
            _shrinkSection.Add(slRow);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            _shrinkSection.Add(btnRow);

            _btnApply = new Button(OnApplyClicked) { text = "決定" };
            _btnApply.style.flexGrow    = 1;
            _btnApply.style.marginRight = 4;
            _btnApply.style.height      = 24;
            _btnApply.style.fontSize    = 10;
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
            if (_preview.IsActive) EndPreview();
            _model       = model;
            _beforeIndex = -1;
            _afterIndex  = -1;
            _colliderIndices.Clear();
            _stopParams  = null;
            _slider      = 0f;
            _candidates.Clear();
            Refresh();
        }

        /// <summary>選択変更後に呼ぶ。</summary>
        public void OnSelectionChanged()
        {
            if (_preview.IsActive) EndPreview();
            _stopParams = null;
            _slider     = 0f;
            Refresh();
        }

        // ================================================================
        // Refresh
        // ================================================================

        private void Refresh()
        {
            if (_warningLabel == null) return;

            if (_model == null)
            {
                ShowWarning("モデルがありません");
                return;
            }

            BuildCandidates(_model);
            if (_candidates.Count < 2)
            {
                ShowWarning("メッシュが2つ以上必要です");
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display  = DisplayStyle.Flex;

            RefreshPickList(_beforeListContainer, _beforeIndex, idx =>
            {
                if (_beforeIndex == idx) return;
                CancelComputation();
                _beforeIndex = idx;
                if (_afterIndex == idx) _afterIndex = -1;
                Refresh();
            });

            RefreshPickList(_afterListContainer, _afterIndex, idx =>
            {
                if (_afterIndex == idx) return;
                CancelComputation();
                _afterIndex = idx;
                if (_beforeIndex == idx) _beforeIndex = -1;
                Refresh();
            });

            RefreshColliderList();

            _btnCompute.SetEnabled(_beforeIndex >= 0 && _afterIndex >= 0 && _beforeIndex != _afterIndex);
            _shrinkSection.style.display = _preview.IsActive ? DisplayStyle.Flex : DisplayStyle.None;

            if (_preview.IsActive)
                _sliderShrink.SetValueWithoutNotify(_slider);
        }

        private void ShowWarning(string msg)
        {
            _warningLabel.text          = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display  = DisplayStyle.None;
        }

        private void BuildCandidates(ModelContext model)
        {
            _candidates.Clear();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var ctx = model.GetMeshContext(i);
                if (ctx?.MeshObject == null || ctx.MeshObject.VertexCount == 0) continue;
                if (ctx.Type != MeshType.Mesh &&
                    ctx.Type != MeshType.BakedMirror &&
                    ctx.Type != MeshType.MirrorSide) continue;
                _candidates.Add((i, ctx.Name, ctx.MeshObject.VertexCount));
            }

            if (_beforeIndex >= 0 && !ContainsCandidate(_beforeIndex)) _beforeIndex = -1;
            if (_afterIndex  >= 0 && !ContainsCandidate(_afterIndex))  _afterIndex  = -1;
            _colliderIndices.RemoveWhere(i => !ContainsCandidate(i));
        }

        private bool ContainsCandidate(int index)
        {
            for (int i = 0; i < _candidates.Count; i++)
                if (_candidates[i].index == index) return true;
            return false;
        }

        // ================================================================
        // リスト描画
        // ================================================================

        private void RefreshPickList(VisualElement container, int selectedIndex, Action<int> onPick)
        {
            container.Clear();

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c   = _candidates[i];
                int idx = c.index;

                var row = new Label($"  {c.name}  [V:{c.vertexCount}]");
                row.style.paddingTop    = 2;
                row.style.paddingBottom = 2;
                row.style.paddingLeft   = 4;
                row.style.fontSize      = 10;

                if (idx == selectedIndex)
                    row.style.backgroundColor = new StyleColor(new Color(0.24f, 0.48f, 0.9f, 0.5f));

                row.RegisterCallback<ClickEvent>(_ => onPick(idx));
                container.Add(row);
            }

            PlayerLayoutRoot.ApplyDarkTheme(container);
        }

        private void RefreshColliderList()
        {
            _colliderListContainer.Clear();

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c   = _candidates[i];
                int idx = c.index;

                if (idx == _beforeIndex || idx == _afterIndex) continue;

                var tg = new Toggle($"{c.name}  [V:{c.vertexCount}]")
                {
                    value = _colliderIndices.Contains(idx)
                };
                tg.style.fontSize     = 10;
                tg.style.marginBottom = 1;
                tg.RegisterValueChangedCallback(e =>
                {
                    CancelComputation();
                    if (e.newValue) _colliderIndices.Add(idx);
                    else            _colliderIndices.Remove(idx);
                    _shrinkSection.style.display = DisplayStyle.None;
                });
                _colliderListContainer.Add(tg);
            }

            PlayerLayoutRoot.ApplyDarkTheme(_colliderListContainer);
        }

        // ================================================================
        // 衝突計算
        // ================================================================

        private void OnComputeClicked()
        {
            if (_model == null) return;
            if (_beforeIndex < 0 || _afterIndex < 0 || _beforeIndex == _afterIndex) return;

            CancelComputation();

            // ワールド座標が要るのはこの時点だけ。毎フレームは呼ばない。
            OnRequestUpdateTransform?.Invoke();

            var stops = ShrinkOperation.ComputeStopParams(
                _model, _beforeIndex, _afterIndex, new List<int>(_colliderIndices),
                _surfaceOffset, _frontFaceOnly, GetWorldPositions, out string error);

            if (stops == null)
            {
                _statusLabel.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
                _statusLabel.text        = error ?? "衝突計算に失敗しました";
                _shrinkSection.style.display = DisplayStyle.None;
                return;
            }

            _stopParams = stops;

            if (!_preview.Start(_model, _beforeIndex, _afterIndex, _stopParams))
            {
                _statusLabel.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
                _statusLabel.text        = "プレビューを開始できません";
                return;
            }

            _slider = 0f;
            _sliderShrink.SetValueWithoutNotify(0f);
            _sliderValueLabel.text = "0.00";
            _preview.Apply(_model, 0f, BuildToolCtx());

            int stopped = _preview.CountStoppedVertices();
            _statusLabel.style.color = string.IsNullOrEmpty(error)
                ? new StyleColor(new Color(0.4f, 0.8f, 1f))
                : new StyleColor(new Color(1f, 0.7f, 0.3f));
            _statusLabel.text = string.IsNullOrEmpty(error)
                ? $"衝突で停止する頂点: {stopped} / {_stopParams.Length}"
                : error;

            // アフターを非表示にした状態を GPU バッファへ反映する（この時点で1回だけ）。
            OnNotifyTopologyChanged?.Invoke();

            _shrinkSection.style.display = DisplayStyle.Flex;
            OnRepaint?.Invoke();
        }

        /// <summary>プレビュー中なら破棄して座標を戻す。</summary>
        private void CancelComputation()
        {
            if (_preview.IsActive) EndPreview();
            _stopParams = null;
        }

        // ================================================================
        // スライダー
        // ================================================================

        private void OnSliderChanged(float newValue)
        {
            if (_model == null || !_preview.IsActive) return;

            _slider = newValue;
            _sliderValueLabel.text = newValue.ToString("F2");
            _preview.Apply(_model, _slider, BuildToolCtx());
        }

        // ================================================================
        // 決定 / キャンセル
        // ================================================================

        private void OnApplyClicked()
        {
            if (_model == null || !_preview.IsActive) return;

            int   beforeIndex = _beforeIndex;
            int   afterIndex  = _afterIndex;
            float slider      = _slider;
            var   colliders   = new List<int>(_colliderIndices).ToArray();

            // コマンド側で同じ計算をやり直すため、プレビューは先に破棄して
            // 元座標へ戻しておく（二重適用の防止）。
            EndPreview();

            if (_panelContext != null)
            {
                _panelContext.SendCommand(new ApplyShrinkCommand(
                    _getModelIndex?.Invoke() ?? 0,
                    beforeIndex, afterIndex, colliders,
                    slider, _surfaceOffset, _frontFaceOnly, _recalculateNormals,
                    _createNewObject));
            }
            else
            {
                // フォールバック（PanelContext未設定時）
                OnRequestUpdateTransform?.Invoke();
                var stops = ShrinkOperation.ComputeStopParams(
                    _model, beforeIndex, afterIndex, colliders,
                    _surfaceOffset, _frontFaceOnly, GetWorldPositions, out _);

                var ctx = BuildToolCtx();
                var pv  = new ShrinkPreviewState();
                if (pv.Start(_model, beforeIndex, afterIndex, stops, hideAfter: false))
                {
                    pv.Apply(_model, slider, ctx);
                    ShrinkOperation.Apply(
                        _model, pv, colliders, _createNewObject, _recalculateNormals, ctx);
                }
            }

            _stopParams = null;
            _slider     = 0f;
            Refresh();
        }

        private void OnCancelClicked()
        {
            EndPreview();
            _stopParams = null;
            _slider     = 0f;
            _sliderShrink?.SetValueWithoutNotify(0f);
            if (_sliderValueLabel != null) _sliderValueLabel.text = "0.00";
            _statusLabel.text = string.Empty;
            Refresh();
        }

        private void EndPreview()
        {
            if (!_preview.IsActive) return;
            _preview.End(_model, BuildToolCtx());
            // 可視状態を戻した結果を GPU バッファへ反映する。
            OnNotifyTopologyChanged?.Invoke();
        }

        // ================================================================
        // ToolContext 生成（最小構成）
        // ================================================================

        private Poly_Ling.Tools.ToolContext BuildToolCtx()
        {
            var ctx = new Poly_Ling.Tools.ToolContext();
            ctx.Model          = _model;
            ctx.Repaint        = OnRepaint;
            ctx.UndoController = GetUndoController?.Invoke();
            ctx.CommandQueue   = GetCommandQueue?.Invoke();
            ctx.SyncMeshContextPositionsOnly = mc => OnSyncMeshPositions?.Invoke(mc);
            ctx.NotifyTopologyChanged        = OnNotifyTopologyChanged;
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
    }
}
