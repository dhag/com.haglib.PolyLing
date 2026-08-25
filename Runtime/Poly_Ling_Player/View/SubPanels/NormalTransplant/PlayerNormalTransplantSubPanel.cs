// PlayerNormalTransplantSubPanel.cs
// 法線移植 サブパネル（Player ビルド用）。
// ビフォー／アフターの2オブジェクトが作るシェル（プリズム群）から、
// ターゲットオブジェクトの各頂点へ法線を移植する。
// Runtime/Poly_Ling_Player/View/SubPanels/NormalTransplant/ に配置
//
// 【設計】
// 移植法線はビフォー・アフター・ターゲットが動かない限り不変なので、
// 「法線を計算」ボタン押下時に1回だけ算出する。適用率スライダーの操作は
// 退避値との Slerp のみで、再計算も GPU 読み戻しも発生しない。
//
// 【前提】
// スキニング無し。法線の空間変換はオブジェクト単位の WorldMatrix だけを使う
// （NormalTransplantOperation の冒頭コメントを参照）。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    public class PlayerNormalTransplantSubPanel
    {
        // ================================================================
        // コールバック（Viewer から設定）
        // ================================================================

        /// <summary>法線更新後の Unity Mesh / GPU 反映。</summary>
        public Action<MeshContext> OnSyncMeshNormals;

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
        /// 法線計算の直前に1回だけ呼ぶ。毎フレーム呼んではならない。
        /// </summary>
        public Action OnRequestUpdateTransform;

        // コマンド送信
        private PanelContext _panelContext;
        private Func<int> _getModelIndex;

        public void SetCommandContext(PanelContext ctx, Func<int> getModelIndex)
        {
            _panelContext = ctx;
            _getModelIndex = getModelIndex;
        }

        // ================================================================
        // 内部状態
        // ================================================================

        private ModelContext _model;

        private int _beforeIndex = -1;
        private int _afterIndex = -1;
        private readonly HashSet<int> _targetIndices = new HashSet<int>();

        private float _strength = 1f;
        private bool _spherical = false;
        private bool _allowNearest = false;

        private readonly NormalTransplantPreviewState _preview = new NormalTransplantPreviewState();

        private readonly List<(int index, string name, int vertexCount)> _candidates
            = new List<(int, string, int)>();

        // ================================================================
        // UI 要素
        // ================================================================

        private VisualElement _root;
        private Label _warningLabel;
        private VisualElement _mainContent;
        private VisualElement _beforeListContainer;
        private VisualElement _afterListContainer;
        private VisualElement _targetListContainer;
        private RadioButtonGroup _blendModeGroup;
        private Toggle _toggleAllowNearest;
        private Button _btnCompute;
        private Label _statusLabel;
        private VisualElement _applySection;
        private Slider _sliderStrength;
        private Label _sliderValueLabel;
        private Button _btnApply;

        // ================================================================
        // Build
        // ================================================================

        public void Build(VisualElement parent)
        {
            _root = new VisualElement();
            _root.style.paddingLeft = 4;
            _root.style.paddingRight = 4;
            _root.style.paddingTop = 4;
            _root.style.paddingBottom = 4;
            parent.Add(_root);

            _warningLabel = new Label();
            _warningLabel.style.display = DisplayStyle.None;
            _warningLabel.style.color = new StyleColor(new Color(1f, 0.5f, 0.2f));
            _warningLabel.style.whiteSpace = WhiteSpace.Normal;
            _warningLabel.style.marginBottom = 4;
            _root.Add(_warningLabel);

            _mainContent = new VisualElement();
            _root.Add(_mainContent);

            // ── ビフォー
            _mainContent.Add(SecLabel("ビフォー（内側の面）"));
            _beforeListContainer = new VisualElement();
            _beforeListContainer.style.marginBottom = 4;
            _mainContent.Add(_beforeListContainer);

            // ── アフター
            _mainContent.Add(SecLabel("アフター（外側の面）"));
            _afterListContainer = new VisualElement();
            _afterListContainer.style.marginBottom = 4;
            _mainContent.Add(_afterListContainer);

            _mainContent.Add(Sep());

            // ── ターゲット
            _mainContent.Add(SecLabel("ターゲット（法線を差し替える／複数可）"));
            _targetListContainer = new VisualElement();
            _targetListContainer.style.marginBottom = 4;
            _mainContent.Add(_targetListContainer);

            _mainContent.Add(Sep());

            // ── 三角形内の補間
            _mainContent.Add(SecLabel("三角形内の補間"));
            var blendChoices = new List<string>
            {
                "線形補間（既定）",
                "球面補間",
            };
            _blendModeGroup = new RadioButtonGroup(null, blendChoices) { value = _spherical ? 1 : 0 };
            _blendModeGroup.style.marginBottom = 4;
            _blendModeGroup.RegisterValueChangedCallback(e =>
            {
                bool newSpherical = (e.newValue == 1);
                if (_spherical == newSpherical) return;
                _spherical = newSpherical;
                // 結果が変わるため、計算済みのプレビューは破棄する
                CancelComputation();
                _applySection.style.display = DisplayStyle.None;
            });
            _mainContent.Add(_blendModeGroup);

            _toggleAllowNearest = new Toggle("プリズム外の頂点は最も近いプリズムへ寄せる")
            {
                value = _allowNearest
            };
            _toggleAllowNearest.style.fontSize = 10;
            _toggleAllowNearest.style.marginBottom = 4;
            _toggleAllowNearest.RegisterValueChangedCallback(e =>
            {
                if (_allowNearest == e.newValue) return;
                _allowNearest = e.newValue;
                CancelComputation();
                _applySection.style.display = DisplayStyle.None;
            });
            _mainContent.Add(_toggleAllowNearest);

            _btnCompute = new Button(OnComputeClicked) { text = "法線を計算" };
            _btnCompute.style.height = 24;
            _btnCompute.style.fontSize = 10;
            _btnCompute.style.marginBottom = 4;
            _mainContent.Add(_btnCompute);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color = new StyleColor(new Color(0.4f, 0.8f, 1f));
            _statusLabel.style.marginBottom = 4;
            _mainContent.Add(_statusLabel);

            _mainContent.Add(Sep());

            // ── 適用率スライダー
            _applySection = new VisualElement();
            _applySection.style.display = DisplayStyle.None;
            _mainContent.Add(_applySection);

            _applySection.Add(SecLabel("適用率"));

            var slRow = new VisualElement();
            slRow.style.flexDirection = FlexDirection.Row;
            slRow.style.marginBottom = 4;
            _sliderStrength = new Slider(0f, 1f) { value = 1f };
            _sliderStrength.style.flexGrow = 1;
            _sliderStrength.RegisterValueChangedCallback(e => OnSliderChanged(e.newValue));
            _sliderValueLabel = new Label("1.00");
            _sliderValueLabel.style.width = 32;
            _sliderValueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            slRow.Add(_sliderStrength);
            slRow.Add(_sliderValueLabel);
            _applySection.Add(slRow);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            _applySection.Add(btnRow);

            _btnApply = new Button(OnApplyClicked) { text = "決定" };
            _btnApply.style.flexGrow = 1;
            _btnApply.style.marginRight = 4;
            _btnApply.style.height = 24;
            _btnApply.style.fontSize = 10;
            var btnCancel = new Button(OnCancelClicked) { text = "キャンセル" };
            btnCancel.style.flexGrow = 1;
            btnCancel.style.height = 24;
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
            _model = model;
            _beforeIndex = -1;
            _afterIndex = -1;
            _targetIndices.Clear();
            _strength = 1f;
            _candidates.Clear();
            Refresh();
        }

        /// <summary>選択変更・属性変更後に呼ぶ。</summary>
        public void OnSelectionChanged()
        {
            if (_preview.IsActive) EndPreview();
            _strength = 1f;
            Refresh();
        }

        // ================================================================
        // Refresh
        // ================================================================

        public void Refresh()
        {
            if (_warningLabel == null) return;

            if (_model == null)
            {
                ShowWarning("モデルがありません");
                return;
            }

            BuildCandidates(_model);
            if (_candidates.Count < 3)
            {
                ShowWarning("メッシュが3つ以上必要です（ビフォー・アフター・ターゲット）");
                return;
            }

            _warningLabel.style.display = DisplayStyle.None;
            _mainContent.style.display = DisplayStyle.Flex;

            RefreshPickList(_beforeListContainer, _beforeIndex, idx =>
            {
                if (_beforeIndex == idx) return;
                CancelComputation();
                _beforeIndex = idx;
                if (_afterIndex == idx) _afterIndex = -1;
                _targetIndices.Remove(idx);
                Refresh();
            });

            RefreshPickList(_afterListContainer, _afterIndex, idx =>
            {
                if (_afterIndex == idx) return;
                CancelComputation();
                _afterIndex = idx;
                if (_beforeIndex == idx) _beforeIndex = -1;
                _targetIndices.Remove(idx);
                Refresh();
            });

            RefreshTargetList();

            _btnCompute.SetEnabled(
                _beforeIndex >= 0 && _afterIndex >= 0 &&
                _beforeIndex != _afterIndex && _targetIndices.Count > 0);

            _applySection.style.display = _preview.IsActive ? DisplayStyle.Flex : DisplayStyle.None;

            if (_preview.IsActive)
                _sliderStrength.SetValueWithoutNotify(_strength);
        }

        private void ShowWarning(string msg)
        {
            _warningLabel.text = msg;
            _warningLabel.style.display = DisplayStyle.Flex;
            _mainContent.style.display = DisplayStyle.None;
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
            if (_afterIndex >= 0 && !ContainsCandidate(_afterIndex)) _afterIndex = -1;
            _targetIndices.RemoveWhere(i => !ContainsCandidate(i));
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
                var c = _candidates[i];
                int idx = c.index;

                var row = new Label($"  {c.name}  [V:{c.vertexCount}]");
                row.style.paddingTop = 2;
                row.style.paddingBottom = 2;
                row.style.paddingLeft = 4;
                row.style.fontSize = 10;

                if (idx == selectedIndex)
                    row.style.backgroundColor = new StyleColor(new Color(0.24f, 0.48f, 0.9f, 0.5f));

                row.RegisterCallback<ClickEvent>(_ => onPick(idx));
                container.Add(row);
            }

            PlayerLayoutRoot.ApplyDarkTheme(container);
        }

        private void RefreshTargetList()
        {
            _targetListContainer.Clear();

            for (int i = 0; i < _candidates.Count; i++)
            {
                var c = _candidates[i];
                int idx = c.index;

                if (idx == _beforeIndex || idx == _afterIndex) continue;

                var tg = new Toggle($"{c.name}  [V:{c.vertexCount}]")
                {
                    value = _targetIndices.Contains(idx)
                };
                tg.style.fontSize = 10;
                tg.style.marginBottom = 1;
                tg.RegisterValueChangedCallback(e =>
                {
                    CancelComputation();
                    if (e.newValue) _targetIndices.Add(idx);
                    else _targetIndices.Remove(idx);
                    _applySection.style.display = DisplayStyle.None;
                    _btnCompute.SetEnabled(
                        _beforeIndex >= 0 && _afterIndex >= 0 &&
                        _beforeIndex != _afterIndex && _targetIndices.Count > 0);
                });
                _targetListContainer.Add(tg);
            }

            PlayerLayoutRoot.ApplyDarkTheme(_targetListContainer);
        }

        // ================================================================
        // 法線計算
        // ================================================================

        private void OnComputeClicked()
        {
            if (_model == null) return;
            if (_beforeIndex < 0 || _afterIndex < 0 || _beforeIndex == _afterIndex) return;
            if (_targetIndices.Count == 0) return;

            CancelComputation();

            // ワールド座標が要るのはこの時点だけ。毎フレームは呼ばない。
            OnRequestUpdateTransform?.Invoke();

            var samples = NormalTransplantOperation.ComputeSamples(
                _model, _beforeIndex, _afterIndex, new List<int>(_targetIndices),
                _spherical
                    ? NormalPrismSolver.TriangleBlendMode.Spherical
                    : NormalPrismSolver.TriangleBlendMode.Linear,
                _allowNearest, GetWorldPositions, out string error);

            if (samples == null)
            {
                _statusLabel.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
                _statusLabel.text = error ?? "法線を計算できませんでした";
                _applySection.style.display = DisplayStyle.None;
                return;
            }

            if (!_preview.Start(_model, samples))
            {
                _statusLabel.style.color = new StyleColor(new Color(1f, 0.4f, 0.4f));
                _statusLabel.text = "プレビューを開始できません";
                _applySection.style.display = DisplayStyle.None;
                return;
            }

            _strength = 1f;
            _sliderStrength.SetValueWithoutNotify(1f);
            _sliderValueLabel.text = "1.00";
            ApplyPreview(1f);

            int total = _preview.TotalVertexCount();
            int resolved = _preview.TotalResolvedCount();
            int inside = _preview.TotalInsideCount();

            bool allResolved = resolved >= total;
            _statusLabel.style.color = allResolved
                ? new StyleColor(new Color(0.4f, 0.8f, 1f))
                : new StyleColor(new Color(1f, 0.7f, 0.3f));
            _statusLabel.text = $"移植: {resolved} / {total} 頂点（プリズム内包 {inside}）";

            _applySection.style.display = DisplayStyle.Flex;
            OnRepaint?.Invoke();
        }

        /// <summary>プレビュー中なら破棄して法線を戻す。</summary>
        private void CancelComputation()
        {
            if (_preview.IsActive) EndPreview();
        }

        // ================================================================
        // スライダー
        // ================================================================

        private void OnSliderChanged(float newValue)
        {
            if (_model == null || !_preview.IsActive) return;

            _strength = newValue;
            _sliderValueLabel.text = newValue.ToString("F2");
            ApplyPreview(_strength);
        }

        /// <summary>プレビュー値を書き込み、ターゲットの表示へ反映する。</summary>
        private void ApplyPreview(float strength)
        {
            _preview.Apply(_model, strength);
            SyncTargets();
            OnRepaint?.Invoke();
        }

        private void SyncTargets()
        {
            var samples = _preview.Samples;
            if (samples == null || _model == null) return;

            foreach (var s in samples)
            {
                var ctx = _model.GetMeshContext(s.MasterIndex);
                if (ctx?.MeshObject == null) continue;
                OnSyncMeshNormals?.Invoke(ctx);
            }
        }

        // ================================================================
        // 決定 / キャンセル
        // ================================================================

        private void OnApplyClicked()
        {
            if (_model == null || !_preview.IsActive) return;

            int beforeIndex = _beforeIndex;
            int afterIndex = _afterIndex;
            float strength = _strength;
            var targets = new List<int>(_targetIndices).ToArray();

            // コマンド側で同じ計算をやり直すため、プレビューは先に破棄して
            // 元法線へ戻しておく（二重適用の防止）。
            EndPreview();

            if (_panelContext != null)
            {
                _panelContext.SendCommand(new ApplyNormalTransplantCommand(
                    _getModelIndex?.Invoke() ?? 0,
                    beforeIndex, afterIndex, targets,
                    strength, _spherical, _allowNearest));
            }
            else
            {
                // フォールバック（PanelContext未設定時）
                OnRequestUpdateTransform?.Invoke();
                var samples = NormalTransplantOperation.ComputeSamples(
                    _model, beforeIndex, afterIndex, targets,
                    _spherical
                        ? NormalPrismSolver.TriangleBlendMode.Spherical
                        : NormalPrismSolver.TriangleBlendMode.Linear,
                    _allowNearest, GetWorldPositions, out _);

                if (samples != null)
                {
                    var pv = new NormalTransplantPreviewState();
                    if (pv.Start(_model, samples))
                    {
                        NormalTransplantOperation.Apply(_model, pv, strength, BuildToolCtx());
                        OnNotifyTopologyChanged?.Invoke();
                    }
                }
            }

            _strength = 1f;
            Refresh();
        }

        private void OnCancelClicked()
        {
            EndPreview();
            _strength = 1f;
            _sliderStrength?.SetValueWithoutNotify(1f);
            if (_sliderValueLabel != null) _sliderValueLabel.text = "1.00";
            if (_statusLabel != null) _statusLabel.text = string.Empty;
            Refresh();
        }

        private void EndPreview()
        {
            if (!_preview.IsActive) return;

            _preview.Restore(_model);
            SyncTargets();
            _preview.End(_model);
            OnRepaint?.Invoke();
        }

        // ================================================================
        // ToolContext 生成（最小構成）
        // ================================================================

        private Poly_Ling.Tools.ToolContext BuildToolCtx()
        {
            var ctx = new Poly_Ling.Tools.ToolContext();
            ctx.Model = _model;
            ctx.Repaint = OnRepaint;
            ctx.UndoController = GetUndoController?.Invoke();
            ctx.CommandQueue = GetCommandQueue?.Invoke();
            ctx.NotifyTopologyChanged = OnNotifyTopologyChanged;
            return ctx;
        }

        // ================================================================
        // UIヘルパー
        // ================================================================

        private static Label SecLabel(string text)
        {
            var l = new Label(text);
            l.style.color = new StyleColor(new Color(0.65f, 0.8f, 1f));
            l.style.fontSize = 10;
            l.style.marginTop = 4;
            l.style.marginBottom = 2;
            return l;
        }

        private static VisualElement Sep()
        {
            var v = new VisualElement();
            v.style.height = 1;
            v.style.marginTop = 3;
            v.style.marginBottom = 3;
            v.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.08f));
            return v;
        }
    }
}
