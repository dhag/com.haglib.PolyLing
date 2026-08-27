// PlayerThinPlateMorphSubPanel.cs
// TPSモーフ サブパネル（Player ビルド用）。
// ビフォー／アフター2オブジェクトの頂点対応から 3D Thin Plate Spline を解き、
// ターゲットオブジェクトを変形した結果を新規オブジェクトとして作る。
// Runtime/Poly_Ling_Player/View/SubPanels/ThinPlateMorph/ に配置
//
// 【設計】
// 係数算出は制御点数の3乗オーダーでプレビューに向かないため、プレビューは持たない。
// 「実行」で計算と確定をまとめて行い、結果は新規オブジェクトとして追加する。
// ターゲットは変更しないので、失敗しても元の形状は残る。
//
// 【前提】
// スキニング無し。空間変換はオブジェクト単位の WorldMatrix だけを使う
// （ThinPlateMorphOperation の冒頭コメントを参照）。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    public class PlayerThinPlateMorphSubPanel
    {
        // ================================================================
        // コールバック（Viewer から設定）
        // ================================================================

        /// <summary>再描画要求。</summary>
        public Action OnRepaint;

        // コマンド送信
        private PanelContext _panelContext;
        private Func<int> _getModelIndex;

        public void SetCommandContext(PanelContext ctx, Func<int> getModelIndex)
        {
            _panelContext  = ctx;
            _getModelIndex = getModelIndex;
        }

        // ================================================================
        // 内部状態
        // ================================================================

        private ModelContext _model;

        private int _beforeIndex = -1;
        private int _afterIndex  = -1;
        private int _targetIndex = -1;

        private float _lambda         = ThinPlateMorphOperation.DefaultLambda;
        private bool  _selectedOnly   = false;
        private bool  _recalcNormals  = true;

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
        private VisualElement _targetListContainer;
        private Toggle        _toggleSelectedOnly;
        private FloatField    _fieldLambda;
        private Toggle        _toggleRecalc;
        private Label         _infoLabel;
        private Button        _btnApply;
        private Label         _statusLabel;

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
            _mainContent.Add(SecLabel("ビフォー（変形前の対応点）"));
            _beforeListContainer = new VisualElement();
            _beforeListContainer.style.marginBottom = 4;
            _mainContent.Add(_beforeListContainer);

            // ── アフター
            _mainContent.Add(SecLabel("アフター（変形後の対応点／ビフォーと同頂点数）"));
            _afterListContainer = new VisualElement();
            _afterListContainer.style.marginBottom = 4;
            _mainContent.Add(_afterListContainer);

            _mainContent.Add(Sep());

            // ── ターゲット
            _mainContent.Add(SecLabel("ターゲット（変形させる／結果は新規オブジェクト）"));
            _targetListContainer = new VisualElement();
            _targetListContainer.style.marginBottom = 4;
            _mainContent.Add(_targetListContainer);

            _mainContent.Add(Sep());

            // ── 制御点
            _mainContent.Add(SecLabel("制御点"));

            _toggleSelectedOnly = new Toggle("選択頂点のみを制御点にする") { value = _selectedOnly };
            _toggleSelectedOnly.style.fontSize    = 10;
            _toggleSelectedOnly.style.marginBottom = 2;
            _toggleSelectedOnly.RegisterValueChangedCallback(e =>
            {
                _selectedOnly = e.newValue;
                RefreshInfo();
            });
            _mainContent.Add(_toggleSelectedOnly);

            _infoLabel = new Label();
            _infoLabel.style.fontSize    = 9;
            _infoLabel.style.whiteSpace  = WhiteSpace.Normal;
            _infoLabel.style.marginBottom = 4;
            _mainContent.Add(_infoLabel);

            // ── パラメータ
            _mainContent.Add(SecLabel("パラメータ"));

            // λ は 0 から 1 近くまで桁をまたいで使うため、スライダーではなく数値入力にする。
            _fieldLambda = new FloatField("λ（平滑化）") { value = _lambda };
            _fieldLambda.style.fontSize    = 10;
            _fieldLambda.style.marginBottom = 2;
            _fieldLambda.RegisterValueChangedCallback(e =>
            {
                float v = e.newValue;
                if (v < 0f)
                {
                    v = 0f;
                    _fieldLambda.SetValueWithoutNotify(v);
                }
                _lambda = v;
            });
            _mainContent.Add(_fieldLambda);

            _toggleRecalc = new Toggle("法線を再計算") { value = _recalcNormals };
            _toggleRecalc.style.fontSize     = 10;
            _toggleRecalc.style.marginBottom = 4;
            _toggleRecalc.RegisterValueChangedCallback(e => _recalcNormals = e.newValue);
            _mainContent.Add(_toggleRecalc);

            _mainContent.Add(Sep());

            _btnApply = new Button(OnApplyClicked) { text = "実行" };
            _btnApply.style.height       = 24;
            _btnApply.style.fontSize     = 10;
            _btnApply.style.marginBottom = 4;
            _mainContent.Add(_btnApply);

            _statusLabel = new Label();
            _statusLabel.style.fontSize   = 9;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.color      = new StyleColor(new Color(0.4f, 0.8f, 1f));
            _mainContent.Add(_statusLabel);
        }

        // ================================================================
        // モデル更新（Viewer から呼ぶ）
        // ================================================================

        public void SetModel(ModelContext model)
        {
            _model       = model;
            _beforeIndex = -1;
            _afterIndex  = -1;
            _targetIndex = -1;
            _candidates.Clear();
            if (_statusLabel != null) _statusLabel.text = string.Empty;
            Refresh();
        }

        /// <summary>選択変更・属性変更後に呼ぶ。</summary>
        public void OnSelectionChanged()
        {
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
            _mainContent.style.display  = DisplayStyle.Flex;

            RefreshPickList(_beforeListContainer, _beforeIndex, idx =>
            {
                if (_beforeIndex == idx) return;
                _beforeIndex = idx;
                if (_afterIndex  == idx) _afterIndex  = -1;
                if (_targetIndex == idx) _targetIndex = -1;
                Refresh();
            });

            RefreshPickList(_afterListContainer, _afterIndex, idx =>
            {
                if (_afterIndex == idx) return;
                _afterIndex = idx;
                if (_beforeIndex == idx) _beforeIndex = -1;
                if (_targetIndex == idx) _targetIndex = -1;
                Refresh();
            });

            RefreshPickList(_targetListContainer, _targetIndex, idx =>
            {
                if (_targetIndex == idx) return;
                _targetIndex = idx;
                if (_beforeIndex == idx) _beforeIndex = -1;
                if (_afterIndex  == idx) _afterIndex  = -1;
                Refresh();
            });

            RefreshInfo();
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
            if (_targetIndex >= 0 && !ContainsCandidate(_targetIndex)) _targetIndex = -1;
        }

        private bool ContainsCandidate(int index)
        {
            for (int i = 0; i < _candidates.Count; i++)
                if (_candidates[i].index == index) return true;
            return false;
        }

        /// <summary>制御点数の見積りと実行可否を更新する。</summary>
        private void RefreshInfo()
        {
            if (_infoLabel == null || _btnApply == null) return;

            string reason = ValidateSelection(out int controlCount);
            bool ok = reason == null;

            _btnApply.SetEnabled(ok);

            if (controlCount < 0)
            {
                _infoLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
                _infoLabel.text = reason ?? "ビフォー・アフター・ターゲットを選んでください";
                return;
            }

            string text = $"制御点 約 {controlCount} 点"
                        + $"（{ThinPlateMorphOperation.MinControlPointCount}"
                        + $"〜{ThinPlateMorphOperation.MaxControlPointCount} 点）";

            if (!ok)
            {
                _infoLabel.style.color = new StyleColor(new Color(1f, 0.6f, 0.3f));
                _infoLabel.text = text + " / " + reason;
            }
            else if (controlCount > ThinPlateMorphOperation.WarnControlPointCount)
            {
                _infoLabel.style.color = new StyleColor(new Color(1f, 0.8f, 0.4f));
                _infoLabel.text = text + " / 点数が多いため計算に時間がかかります";
            }
            else
            {
                _infoLabel.style.color = new StyleColor(new Color(0.7f, 0.7f, 0.7f));
                _infoLabel.text = text;
            }
        }

        /// <summary>
        /// 実行可能かを調べる。実行できるなら null、できないなら理由を返す。
        /// controlCount には見積り制御点数（算出できないときは -1）を入れる。
        /// </summary>
        private string ValidateSelection(out int controlCount)
        {
            controlCount = -1;

            if (_model == null) return "モデルがありません";
            if (_beforeIndex < 0 || _afterIndex < 0) return "ビフォーとアフターを選んでください";
            if (_targetIndex < 0) return "ターゲットを選んでください";
            if (_beforeIndex == _afterIndex) return "ビフォーとアフターが同一です";
            if (_targetIndex == _beforeIndex || _targetIndex == _afterIndex)
                return "ターゲットはビフォー／アフターと別のオブジェクトにしてください";

            var beforeCtx = _model.GetMeshContext(_beforeIndex);
            var afterCtx  = _model.GetMeshContext(_afterIndex);
            var targetCtx = _model.GetMeshContext(_targetIndex);
            if (beforeCtx?.MeshObject == null || afterCtx?.MeshObject == null || targetCtx?.MeshObject == null)
                return "オブジェクトが不正です";

            if (targetCtx.Type != MeshType.Mesh)
                return "ターゲットは通常メッシュを選んでください";

            int bv = beforeCtx.MeshObject.VertexCount;
            int av = afterCtx.MeshObject.VertexCount;
            if (bv != av) return $"頂点数が一致しません（{bv} / {av}）";

            controlCount = ThinPlateMorphOperation.EstimateControlPointCount(
                _model, _beforeIndex, _afterIndex, _selectedOnly);

            if (controlCount < ThinPlateMorphOperation.MinControlPointCount)
                return $"制御点が足りません（{ThinPlateMorphOperation.MinControlPointCount} 点以上必要）";
            if (controlCount > ThinPlateMorphOperation.MaxControlPointCount)
                return $"制御点が多すぎます（上限 {ThinPlateMorphOperation.MaxControlPointCount} 点）";

            return null;
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

        // ================================================================
        // 実行
        // ================================================================

        private void OnApplyClicked()
        {
            string reason = ValidateSelection(out int controlCount);
            if (reason != null)
            {
                SetStatus(reason, error: true);
                return;
            }

            if (_panelContext == null)
            {
                SetStatus("コマンド送信経路が未配線です", error: true);
                return;
            }

            _panelContext.SendCommand(new ApplyThinPlateMorphCommand(
                _getModelIndex?.Invoke() ?? 0,
                _beforeIndex, _afterIndex, _targetIndex,
                _lambda, _selectedOnly, _recalcNormals));

            // 追加された新規オブジェクトを候補リストへ反映する。
            Refresh();
            SetStatus($"制御点 約 {controlCount} 点で実行しました。メッシュリストを確認してください。", error: false);
            OnRepaint?.Invoke();
        }

        private void SetStatus(string text, bool error)
        {
            if (_statusLabel == null) return;
            _statusLabel.style.color = error
                ? new StyleColor(new Color(1f, 0.4f, 0.4f))
                : new StyleColor(new Color(0.4f, 0.8f, 1f));
            _statusLabel.text = text;
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
