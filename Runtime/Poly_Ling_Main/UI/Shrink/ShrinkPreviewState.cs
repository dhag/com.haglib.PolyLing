// ShrinkPreviewState.cs
// シュリンカー プレビュー状態管理・適用ロジック
// UnityEditor非依存
//
// BlendPreviewState と同じ構造。相違点は次の1点のみ。
//   スライダー値 s に対し、頂点 i の実効パラメータを min(s, StopParams[i]) とする。
//   StopParams はプレビュー開始時に1回だけ算出済みのものを受け取る
//   （ShrinkCollisionSolver.ComputeStopParams）。
//   コライダー・ビフォー・アフターはプレビュー中に動かないため、
//   スライダー操作ごとに衝突計算をやり直す必要はない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Tools;

namespace Poly_Ling.UI
{
    public class ShrinkPreviewState
    {
        private bool  _isActive;
        private int   _beforeIndex = -1;
        private int   _afterIndex  = -1;

        /// <summary>ビフォー（変形対象）の元ローカル座標</summary>
        private Vector3[] _backup;

        /// <summary>アフターのローカル座標（ビフォーと同一インデックス対応）</summary>
        private Vector3[] _afterLocal;

        /// <summary>頂点ごとの停止パラメータ [0,1]</summary>
        private float[] _stopParams;

        private readonly Dictionary<int, bool> _savedVisibility = new Dictionary<int, bool>();

        public bool  IsActive    => _isActive;
        public int   BeforeIndex => _beforeIndex;
        public int   AfterIndex  => _afterIndex;
        public Vector3[] Backup     => _backup;
        public float[]   StopParams => _stopParams;

        // ================================================================
        // 開始
        // ================================================================

        /// <summary>
        /// プレビューを開始する。ビフォーの元座標を控え、アフターを非表示にする。
        /// </summary>
        /// <param name="stopParams">
        /// ShrinkCollisionSolver.ComputeStopParams の戻り値。
        /// null の場合は全頂点がアフターまで到達する（衝突なし）扱い。
        /// </param>
        public bool Start(
            ModelContext model, int beforeIndex, int afterIndex,
            float[] stopParams, bool hideAfter = true)
        {
            if (_isActive) return false;
            if (model == null) return false;

            var beforeCtx = model.GetMeshContext(beforeIndex);
            var afterCtx  = model.GetMeshContext(afterIndex);
            if (beforeCtx?.MeshObject == null || afterCtx?.MeshObject == null) return false;

            var bmo = beforeCtx.MeshObject;
            var amo = afterCtx.MeshObject;

            _savedVisibility.Clear();

            int count = bmo.VertexCount;
            _backup     = new Vector3[count];
            _afterLocal = new Vector3[count];

            int shared = Mathf.Min(count, amo.VertexCount);
            for (int i = 0; i < count; i++)
            {
                _backup[i]     = bmo.Vertices[i].Position;
                // 対応頂点が無い分は動かさない（＝ビフォー位置のまま）
                _afterLocal[i] = i < shared ? amo.Vertices[i].Position : bmo.Vertices[i].Position;
            }

            _stopParams  = stopParams;
            _beforeIndex = beforeIndex;
            _afterIndex  = afterIndex;

            _savedVisibility[beforeIndex] = beforeCtx.IsVisible;
            beforeCtx.IsVisible = true;

            if (hideAfter && afterIndex != beforeIndex)
            {
                _savedVisibility[afterIndex] = afterCtx.IsVisible;
                afterCtx.IsVisible = false;
            }

            _isActive = true;
            return true;
        }

        // ================================================================
        // 適用（スライダー）
        // ================================================================

        /// <summary>
        /// スライダー値 [0,1] を反映する。頂点 i の実効パラメータは
        /// min(slider, StopParams[i])。
        /// </summary>
        public void Apply(ModelContext model, float slider, ToolContext toolCtx)
        {
            if (!_isActive || model == null) return;

            var ctx = model.GetMeshContext(_beforeIndex);
            if (ctx?.MeshObject == null) return;

            var mo = ctx.MeshObject;
            int count = Mathf.Min(_backup.Length, mo.VertexCount);
            float s = Mathf.Clamp01(slider);

            for (int i = 0; i < count; i++)
            {
                float stop = (_stopParams != null && i < _stopParams.Length) ? _stopParams[i] : 1f;
                float t = s < stop ? s : stop;
                mo.Vertices[i].Position = Vector3.Lerp(_backup[i], _afterLocal[i], t);
            }

            toolCtx?.SyncMeshContextPositionsOnly?.Invoke(ctx);
            BlendOperation.SyncMirrorSide(model, ctx, toolCtx);
            toolCtx?.Repaint?.Invoke();
        }

        // ================================================================
        // 終了（元に戻す）
        // ================================================================

        public void End(ModelContext model, ToolContext toolCtx)
        {
            if (!_isActive) return;

            if (model != null)
            {
                var ctx = model.GetMeshContext(_beforeIndex);
                if (ctx?.MeshObject != null && _backup != null)
                {
                    var mo = ctx.MeshObject;
                    int count = Mathf.Min(_backup.Length, mo.VertexCount);
                    for (int i = 0; i < count; i++) mo.Vertices[i].Position = _backup[i];
                    toolCtx?.SyncMeshContextPositionsOnly?.Invoke(ctx);
                    BlendOperation.SyncMirrorSide(model, ctx, toolCtx);
                }

                foreach (var (idx, visible) in _savedVisibility)
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc != null) mc.IsVisible = visible;
                }
            }

            _savedVisibility.Clear();
            _backup      = null;
            _afterLocal  = null;
            _stopParams  = null;
            _beforeIndex = -1;
            _afterIndex  = -1;
            _isActive    = false;
            toolCtx?.Repaint?.Invoke();
        }

        // ================================================================
        // 可視状態の復元（確定時に使用。頂点座標は戻さない）
        // ================================================================

        public void RestoreVisibility(ModelContext model)
        {
            if (model == null) { _savedVisibility.Clear(); return; }

            foreach (var (idx, visible) in _savedVisibility)
            {
                var mc = model.GetMeshContext(idx);
                if (mc != null) mc.IsVisible = visible;
            }
            _savedVisibility.Clear();
        }

        // ================================================================
        // 統計（UI表示用）
        // ================================================================

        /// <summary>停止パラメータが 1 未満（＝どこかで衝突する）頂点数。</summary>
        public int CountStoppedVertices()
        {
            if (_stopParams == null) return 0;
            int n = 0;
            for (int i = 0; i < _stopParams.Length; i++)
                if (_stopParams[i] < 1f) n++;
            return n;
        }
    }
}
