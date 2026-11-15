// MorphExpressionHandler.cs
// 表情の試し表示（プレビュー）と、表情が指す形の重みの調整を受け持つ
// （操作経路統一計画.md E。M-2 と同じ「プレビュー（見た目だけ）・確定（コマンド）・取消」の方式）。
//
// 【プレビューは見た目だけ】
//   MorphPreviewState は描画オブジェクトの WorkingPositions（表示用の足し込み）だけを書き、
//   頂点位置・表情のデータ・Undo には触れない。足し込みは ShapeBlendBuffer（1 系統。
//   PolyLing_モーフ整理計画.md 3.6）。重みの調整中は、このハンドラが持つ上書き値で組み直す。
//   確定は SetExpressionShapesCommand で行う。
//
// 【基本の形への切り替え】足し込みは「基本の形 ＋ Σ 重み ×（形 − 基本の形）」なので、
//   今の形が基本の形でないと二重に動く。指す相手のある表情の試し表示を始めるとき、
//   SetCurrentShapeCommand で基本の形へ切り替える（切り替えの記録が 1 件残る。計画書 6 の 3）。
//   形が切り替わったら（OnShapeSwitched）、自分で起こした切り替えでなければ試し表示を終える。

using System;
using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Tools;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    [PLTool("morphExpression", Description = "表情の試し表示と、表情が指す形の重みの調整")]
    public sealed class MorphExpressionHandler
    {
        public Func<ModelContext>   GetModel;
        public Func<ToolContext>    GetToolContext;
        public Func<int>            GetModelIndex;
        public Action<PanelCommand> SendCommand;

        private readonly MorphPreviewState _preview = new MorphPreviewState();
        // 形の子の上書き（形の名前 → 重み）
        private readonly Dictionary<string, float> _shapeOverrides = new Dictionary<string, float>();
        private int   _setIndex = -1;
        private float _weight;
        private bool  _switching;

        [PLToolState(Description = "プレビュー中の表情の番号（-1 はプレビューなし）")]
        public int PreviewSetIndex => _preview.IsActive ? _setIndex : -1;

        /// <summary>モーフのプレビュー中か（queryRevisions の activeModes が読む）。</summary>
        public bool IsPreviewing => _preview.IsActive;

        [PLToolState(Description = "プレビューのウェイト")]
        public float PreviewWeight => _weight;

        [PLToolState(Description = "プレビューの対象（描画オブジェクトと形の組）の数")]
        public int PreviewPairCount { get; private set; }

        [PLToolAction(Description = "指定した表情のプレビューを始める（ウェイト 0）。指す相手があり、今の形が基本の形でなければ基本の形へ切り替える")]
        public void StartPreview(int setIndex)
        {
            _shapeOverrides.Clear();
            _setIndex = setIndex;
            _weight   = 0f;
            Rebuild();
        }

        [PLToolAction(Description = "プレビューのウェイトを変える（見た目だけ）")]
        public void ApplyPreview(float weight)
        {
            _weight = weight;
            var model = GetModel?.Invoke();
            if (model != null) _preview.Apply(model, _weight, GetToolContext?.Invoke());
        }

        [PLToolAction(Description = "プレビューを終える（見た目を元へ戻す）")]
        public void EndPreview()
        {
            var model = GetModel?.Invoke();
            if (model != null) _preview.End(model, GetToolContext?.Invoke());
            _shapeOverrides.Clear();
            _setIndex = -1;
            _weight   = 0f;
            PreviewPairCount = 0;
        }

        [PLToolAction(Description = "表情が指す形（子）の重みを仮に変えてプレビューへ反映する（保存データは変えない）")]
        public void PreviewShapeWeight(int setIndex, string shapeName, float weight)
        {
            if (setIndex != _setIndex) { _shapeOverrides.Clear(); _setIndex = setIndex; }
            if (string.IsNullOrEmpty(shapeName)) return;
            _shapeOverrides[shapeName] = weight;
            Rebuild();
        }

        [PLToolAction(Description = "仮に変えた重みを確定する（SetExpressionShapesCommand）")]
        public void CommitEntryWeights()
        {
            var model = GetModel?.Invoke();
            if (model == null || _setIndex < 0 || _setIndex >= model.MorphExpressionCount) { _shapeOverrides.Clear(); return; }
            int mi = GetModelIndex?.Invoke() ?? 0;
            var set = model.MorphExpressions[_setIndex];

            if (_shapeOverrides.Count > 0)
            {
                var names = new List<string>();
                var weights = new List<float>();
                foreach (var c in set.GroupChildren)
                {
                    if (model.FindShapeByName(c.Name) == null) continue;
                    names.Add(c.Name);
                    weights.Add(_shapeOverrides.TryGetValue(c.Name, out float ow) ? ow : c.Weight);
                }
                _shapeOverrides.Clear();
                SendCommand?.Invoke(new SetExpressionShapesCommand(mi, _setIndex, names.ToArray(), weights.ToArray()));
            }
            Rebuild();
        }

        [PLToolAction(Description = "仮に変えた重みを捨てる")]
        public void RevertEntryWeights()
        {
            _shapeOverrides.Clear();
            Rebuild();
        }

        /// <summary>
        /// 形が切り替わったときに Viewer が呼ぶ。切り替えで足し込みは捨てられているので、
        /// 自分で起こした切り替え（試し表示の開始）でなければ試し表示を終える（計画書 3.4）。
        /// </summary>
        public void OnShapeSwitched()
        {
            if (_switching || !_preview.IsActive) return;
            EndPreview();
        }

        /// <summary>今のセットと上書き値からプレビューを組み直し、今のウェイトで反映する。</summary>
        private void Rebuild()
        {
            var model = GetModel?.Invoke();
            if (model == null || _setIndex < 0 || _setIndex >= model.MorphExpressionCount)
            {
                if (model != null) _preview.End(model, GetToolContext?.Invoke());
                PreviewPairCount = 0;
                return;
            }

            var set = model.MorphExpressions[_setIndex];

            var terms = new List<MorphTargetTerm>();
            ShapeBlendOps.ExpandExpression(model, set, 1f, terms, _shapeOverrides.Count > 0 ? _shapeOverrides : null);

            if (terms.Count == 0) { _preview.End(model, GetToolContext?.Invoke()); PreviewPairCount = 0; return; }

            // 基本の形へ（3.6）。自分で起こした切り替えでは試し表示を終えない。
            if (model.CurrentShapeId != ShapeOps.BaseShapeId)
            {
                _switching = true;
                try { SendCommand?.Invoke(new SetCurrentShapeCommand(GetModelIndex?.Invoke() ?? 0, "")); }
                finally { _switching = false; }
                // 切り替えられなかったら始めない（今の形の位置に足すと二重に動く）。
                if (model.CurrentShapeId != ShapeOps.BaseShapeId)
                {
                    _preview.End(model, GetToolContext?.Invoke());
                    PreviewPairCount = 0;
                    return;
                }
            }

            _preview.Start(model, terms, _setIndex);
            PreviewPairCount = _preview.PairCount;
            if (PreviewPairCount == 0) { _preview.End(model, GetToolContext?.Invoke()); return; }
            _preview.Apply(model, _weight, GetToolContext?.Invoke());
        }
    }
}
