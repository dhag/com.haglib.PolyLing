// MorphPreviewState.cs
// 表情の試し表示（重み付き）の状態。足し込みは ShapeBlendBuffer（形の試し表示・
// モーション再生と同じ 1 系統。PolyLing_モーフ整理計画.md 3.6）。
// UnityEditor非依存 → Runtime/に移行可能
//
// 置き場所は描画オブジェクトの WorkingPositions（表示は Position ＋ WorkingPositions）。
// 頂点位置・表情のデータ・Undo には触れない。始める前に基本の形へ切り替えるのは呼び出し側。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public class MorphPreviewState
    {
        private bool _isPreviewActive = false;
        private int  _previewMorphExpressionIndex = -1;
        private readonly ShapeBlendBuffer _buffer = new ShapeBlendBuffer();
        private readonly float[] _weight = new float[1];

        public bool IsActive       => _isPreviewActive;
        public int  ActiveSetIndex => _previewMorphExpressionIndex;

        /// <summary>差分の組（描画オブジェクト × 相手）の数。</summary>
        public int PairCount => _buffer.EntryCount;

        // ================================================================
        // プレビュー開始
        // ================================================================

        /// <summary>相手の並び（ShapeBlendOps.ExpandExpression の結果）で試し表示を始める（重みはまだ当てない）。</summary>
        public void Start(ModelContext model, List<MorphTargetTerm> terms, int setIndex)
        {
            End(model, null);
            _buffer.Reset();
            _buffer.AddChannel(model, terms, out _);
            _previewMorphExpressionIndex = setIndex;
            _isPreviewActive             = true;
        }

        // ================================================================
        // プレビュー適用
        // ================================================================

        public void Apply(ModelContext model, float weight, ToolContext toolCtx)
        {
            if (!_isPreviewActive || _buffer.EntryCount == 0) return;
            _weight[0] = weight;
            _buffer.Apply(model, _weight, toolCtx?.SyncMeshContextPositionsOnly);
            toolCtx?.Repaint?.Invoke();
        }

        // ================================================================
        // プレビュー終了
        // ================================================================

        public void End(ModelContext model, ToolContext toolCtx)
        {
            if (_isPreviewActive && model != null)
                _buffer.Clear(model, toolCtx?.SyncMeshContextPositionsOnly);

            _buffer.Reset();
            _previewMorphExpressionIndex = -1;
            _isPreviewActive             = false;
            toolCtx?.Repaint?.Invoke();
        }
    }
}
