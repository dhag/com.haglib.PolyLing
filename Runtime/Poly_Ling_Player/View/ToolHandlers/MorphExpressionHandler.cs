// MorphExpressionHandler.cs
// モーフエクスプレッションの試し表示（プレビュー）とエントリのウェイト調整を受け持つ
// （操作経路統一計画.md E。M-2 と同じ「プレビュー（見た目だけ）・確定（コマンド）・取消」の方式）。
//
// 【プレビューは見た目だけ】
//   MorphPreviewState は基準メッシュの WorkingPositions（表示用の足し込み）だけを書き、
//   頂点位置・モーフエクスプレッションのデータ・Undo には触れない。
//   エントリのウェイト調整中は、このハンドラが持つ上書き値でプレビューを組み直す。
//   確定は SetMorphEntryWeightsCommand で行い、上書き値は捨てる。

using System;
using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.UI;

namespace Poly_Ling.Player
{
    [PLTool("morphExpression", Description = "モーフエクスプレッションの試し表示とエントリのウェイト調整")]
    public sealed class MorphExpressionHandler
    {
        public Func<ModelContext>   GetModel;
        public Func<ToolContext>    GetToolContext;
        public Func<int>            GetModelIndex;
        public Action<PanelCommand> SendCommand;

        private readonly MorphPreviewState _preview = new MorphPreviewState();
        private readonly Dictionary<int, float> _weightOverrides = new Dictionary<int, float>();
        private int   _setIndex = -1;
        private float _weight;

        [PLToolState(Description = "プレビュー中のモーフエクスプレッション番号（-1 はプレビューなし）")]
        public int PreviewSetIndex => _preview.IsActive ? _setIndex : -1;

        [PLToolState(Description = "プレビューのウェイト")]
        public float PreviewWeight => _weight;

        [PLToolState(Description = "プレビューの対象（モーフと基準メッシュの組）の数")]
        public int PreviewPairCount { get; private set; }

        [PLToolAction(Description = "指定したモーフエクスプレッションのプレビューを始める（ウェイト 0）")]
        public void StartPreview(int setIndex)
        {
            _weightOverrides.Clear();
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
            _weightOverrides.Clear();
            _setIndex = -1;
            _weight   = 0f;
            PreviewPairCount = 0;
        }

        [PLToolAction(Description = "エントリのウェイトを仮に変えてプレビューへ反映する（保存データは変えない）")]
        public void PreviewEntryWeight(int setIndex, int entryIndex, float weight)
        {
            if (setIndex != _setIndex) { _weightOverrides.Clear(); _setIndex = setIndex; }
            _weightOverrides[entryIndex] = weight;
            Rebuild();
        }

        [PLToolAction(Description = "仮に変えたエントリのウェイトを SetMorphEntryWeightsCommand でまとめて確定する")]
        public void CommitEntryWeights()
        {
            if (_setIndex < 0 || _weightOverrides.Count == 0) return;
            var idx = new int[_weightOverrides.Count];
            var w   = new float[_weightOverrides.Count];
            int k = 0;
            foreach (var kv in _weightOverrides) { idx[k] = kv.Key; w[k] = kv.Value; k++; }
            _weightOverrides.Clear();
            SendCommand?.Invoke(new SetMorphEntryWeightsCommand(GetModelIndex?.Invoke() ?? 0, _setIndex, idx, w));
            Rebuild();
        }

        [PLToolAction(Description = "仮に変えたエントリのウェイトを捨てる")]
        public void RevertEntryWeights()
        {
            _weightOverrides.Clear();
            Rebuild();
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
            var pairs = MorphPreviewState.BuildMorphBasePairs(model, set);
            if (_weightOverrides.Count > 0)
            {
                // BuildMorphBasePairs は使えないエントリ（モーフでない・基準が無い）を飛ばすので、
                // 組の並びはエントリ番号と一致しない。エントリのモーフメッシュ番号で突き合わせる。
                var byMesh = new Dictionary<int, float>();
                foreach (var kv in _weightOverrides)
                    if (kv.Key >= 0 && kv.Key < set.MeshEntries.Count)
                        byMesh[set.MeshEntries[kv.Key].MeshIndex] = kv.Value;
                for (int i = 0; i < pairs.Count; i++)
                {
                    if (!byMesh.TryGetValue(pairs[i].morphIndex, out float ow)) continue;
                    var p = pairs[i];
                    pairs[i] = (p.morphIndex, p.baseIndex, p.morphCtx, p.baseCtx, ow);
                }
            }
            PreviewPairCount = pairs.Count;
            if (pairs.Count == 0) { _preview.End(model, GetToolContext?.Invoke()); return; }
            _preview.Start(model, pairs, _setIndex);
            _preview.Apply(model, _weight, GetToolContext?.Invoke());
        }
    }
}
