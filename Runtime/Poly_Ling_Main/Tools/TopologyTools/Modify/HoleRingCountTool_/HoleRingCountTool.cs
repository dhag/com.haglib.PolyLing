// HoleRingCountTool.cs
// 穴頂点数合わせツール - 基準穴の頂点数に合わせて、対象穴の頂点数を増減させる。
// ブリッジ（BridgeLoopOps）が要求する「2つの穴の頂点数が同じ」を満たすための前処理。
// 実処理は HoleRingCountOps。ここは種の保持・Undo 記録・ミラー伝播・通知を担う。
//
// 【種の選び方】ブリッジと同じ。ビューポートでエッジ上の頂点（または辺）を選び、
//   パネルの取り込みボタンで基準穴 / 対象穴を確定する。自動選択は持たない。
//
// 【変更されるのは対象穴のメッシュだけ】基準穴は頂点数を読むだけで触らない。
//   基準穴と対象穴が同じメッシュにあってもよい。
//
// 【トポロジカル変更の分類】
//   頂点の追加・削除を伴うため、実行後は ctx.OnTopologyChanged() で全選択をクリアする。
//
// 【Undo】VertexDissolveTool と同じ方針。MeshContextIndex を持つ
//   MultiMeshTopologySnapshotRecord を MeshListStack へ 1 件だけ記録する。
//   HoleRingCountOps は途中で止まったときもそこまでの変更を残すため、
//   スナップショットは必ず操作の前後で撮る（部分適用も 1 手で戻せる）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 穴頂点数合わせツール。マウス操作は持たず、UI からの実行のみ。
    /// </summary>
    public class HoleRingCountTool : IEditTool
    {
        public string Name        => "HoleRingCount";
        public string DisplayName => "Hole Ring Count";

        /// <summary>設定は持たない（分割方法は SplitTriangleIntoTriangles で直接持つ）。</summary>
        public IToolSettings Settings => null;

        // ================================================================
        // 種
        // ================================================================

        /// <summary>取り込んだ穴 1 つぶん。</summary>
        public class Seed
        {
            public bool   Valid;
            public int    MeshIndex     = -1;
            public int    Vertex        = -1;
            /// <summary>辺で取り込んだときの進行方向側の頂点。頂点で取り込んだときは -1。</summary>
            public int    DirectionHint = -1;
            /// <summary>表示用の説明。取り込み失敗の理由もここへ入れる。</summary>
            public string Info          = "";

            public void Clear()
            {
                Valid = false; MeshIndex = -1; Vertex = -1; DirectionHint = -1; Info = "";
            }
        }

        private readonly Seed _base   = new Seed();
        private readonly Seed _target = new Seed();

        /// <summary>基準穴（頂点数を読むだけ。変更しない）。</summary>
        public Seed BaseSeed => _base;
        /// <summary>対象穴（頂点数を増減させる）。</summary>
        public Seed TargetSeed => _target;

        // ================================================================
        // 穴のループの控え
        // ================================================================

        /// <summary>
        /// 穴のループの控え。
        ///
        /// 【なぜ要るか】Inspect はサブパネルの Refresh から呼ばれ、Refresh は
        /// 選択が変わるたびに走る（PolyLingPlayerViewerCore の _sectionRefreshPairs）。
        /// ループの構築は境界辺の総当たり（面数に比例、半辺の辞書を毎回作る）なので、
        /// 矩形選択のドラッグ中に毎回走らせると大きなメッシュで目に見えて重くなる。
        ///
        /// 【作り直す条件】メッシュの実体・頂点数・面数・種のいずれかが変わったとき。
        /// 位相を変える操作は必ず頂点数か面数を動かすため、この 4 つで足りる。
        /// 位相を変えずに座標だけ動かす操作はループの構成を変えないので数え直す必要がない。
        /// </summary>
        private class RingCache
        {
            private MeshObject _mo;
            private int        _vertexCount = -1;
            private int        _faceCount   = -1;
            private int        _vertex      = -1;
            private int        _hint        = -2;

            /// <summary>環状に並んだ頂点。数えられなかったときは空。</summary>
            public List<int> Loop { get; private set; } = new List<int>();
            /// <summary>数えられなかった理由、または OrderBoundaryLoop の説明。</summary>
            public string Message { get; private set; }

            public bool Ok    => Loop.Count >= 3;
            public int  Count => Loop.Count;

            public void Update(MeshObject mo, int vertex, int hint)
            {
                if (ReferenceEquals(mo, _mo)
                    && mo != null
                    && _vertexCount == mo.VertexCount
                    && _faceCount   == mo.FaceCount
                    && _vertex      == vertex
                    && _hint        == hint)
                    return;

                _mo          = mo;
                _vertexCount = mo?.VertexCount ?? -1;
                _faceCount   = mo?.FaceCount   ?? -1;
                _vertex      = vertex;
                _hint        = hint;

                if (mo == null)
                {
                    Loop    = new List<int>();
                    Message = "メッシュがありません";
                    return;
                }

                Loop = BridgeLoopOps.OrderBoundaryLoop(mo, vertex, hint, out string message);
                Message = message;
            }

            public void Invalidate()
            {
                _mo = null; _vertexCount = -1; _faceCount = -1; _vertex = -1; _hint = -2;
                Loop = new List<int>();
                Message = null;
            }
        }

        private readonly RingCache _baseRing   = new RingCache();
        private readonly RingCache _targetRing = new RingCache();

        /// <summary>三角形の面へ中点を入れたとき、四角形のまま残さず 2 枚の三角形へ割る。</summary>
        public bool SplitTriangleIntoTriangles = true;

        // ================================================================
        // コンテキスト
        // ================================================================

        private ToolContext _context;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)                => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)                  => false;

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)   { _context = ctx; }
        public void OnDeactivate(ToolContext ctx) { _context = null; }
        public void Reset() { ClearSeeds(); }

        // ================================================================
        // 種の取り込み
        // ================================================================

        /// <summary>
        /// 種を 1 つ入れる。エッジをたどれなければ Valid=false のまま理由を Info へ入れる。
        /// </summary>
        private void Apply(Seed seed, RingCache cache, int meshIndex, int vertex, int directionHint, string meshName)
        {
            seed.Clear();
            cache.Invalidate();

            var mo = _context?.Model?.GetMeshContext(meshIndex)?.MeshObject;
            if (mo == null) { seed.Info = "描画オブジェクトが見つかりません"; return; }

            cache.Update(mo, vertex, directionHint);
            if (!cache.Ok) { seed.Info = cache.Message; return; }

            seed.Valid         = true;
            seed.MeshIndex     = meshIndex;
            seed.Vertex        = vertex;
            seed.DirectionHint = directionHint;
            seed.Info          = $"{meshName ?? $"#{meshIndex}"} / 頂点 {vertex} / {cache.Count} 頂点";
        }

        /// <summary>基準穴を取り込む。</summary>
        public bool ImportBase(int meshIndex, int vertex, int directionHint, string meshName)
        {
            Apply(_base, _baseRing, meshIndex, vertex, directionHint, meshName);
            return _base.Valid;
        }

        /// <summary>対象穴を取り込む。</summary>
        public bool ImportTarget(int meshIndex, int vertex, int directionHint, string meshName)
        {
            Apply(_target, _targetRing, meshIndex, vertex, directionHint, meshName);
            return _target.Valid;
        }

        /// <summary>取り込み失敗の理由を基準穴側へ記録する（選択から拾えなかったとき）。</summary>
        public void FailBase(string message)
        {
            _base.Clear(); _baseRing.Invalidate(); _base.Info = message ?? "";
        }

        /// <summary>取り込み失敗の理由を対象穴側へ記録する。</summary>
        public void FailTarget(string message)
        {
            _target.Clear(); _targetRing.Invalidate(); _target.Info = message ?? "";
        }

        /// <summary>取り込み済みの種を捨てる。</summary>
        public void ClearSeeds()
        {
            _base.Clear();   _baseRing.Invalidate();
            _target.Clear(); _targetRing.Invalidate();
        }

        // ================================================================
        // 下調べ
        // ================================================================

        /// <summary>パネル表示とボタン活性に使う下調べ結果。</summary>
        public struct Summary
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>基準穴の頂点数。読めないときは 0。</summary>
            public int  BaseCount;
            /// <summary>対象穴の頂点数。読めないときは 0。</summary>
            public int  TargetCount;
            /// <summary>対象穴に対する増減（正＝割る / 負＝潰す）。</summary>
            public int  Delta;
            /// <summary>実行できない理由、または実行内容の説明。</summary>
            public string Reason;
        }

        /// <summary>現在の種から実行可否を調べる。メッシュは変更しない。</summary>
        public Summary Inspect()
        {
            var sum = new Summary();

            var model = _context?.Model;
            if (model == null) { sum.Reason = "モデルがありません"; return sum; }

            if (!_base.Valid)   { sum.Reason = "基準穴を取り込んでください"; return sum; }

            var baseMo = model.GetMeshContext(_base.MeshIndex)?.MeshObject;
            if (baseMo == null) { sum.Reason = "基準穴の描画オブジェクトが見つかりません"; return sum; }

            _baseRing.Update(baseMo, _base.Vertex, _base.DirectionHint);
            if (!_baseRing.Ok) { sum.Reason = "基準穴: " + _baseRing.Message; return sum; }
            sum.BaseCount = _baseRing.Count;

            if (!_target.Valid) { sum.Reason = "対象穴を取り込んでください"; return sum; }

            var targetMo = model.GetMeshContext(_target.MeshIndex)?.MeshObject;
            if (targetMo == null) { sum.Reason = "対象穴の描画オブジェクトが見つかりません"; return sum; }

            _targetRing.Update(targetMo, _target.Vertex, _target.DirectionHint);
            if (!_targetRing.Ok) { sum.Reason = "対象穴: " + _targetRing.Message; return sum; }
            sum.TargetCount = _targetRing.Count;

            // 同じ穴（同じメッシュの同じエッジグループ）を 2 回取り込んだ状態は弾く。
            // 基準穴のループに対象穴の種が乗っているかで判定する（控えを使うので追加の走査は無い）。
            if (_base.MeshIndex == _target.MeshIndex && _baseRing.Loop.Contains(_target.Vertex))
            {
                sum.Reason = "基準穴と対象穴が同じ穴です";
                return sum;
            }

            sum.Delta = sum.BaseCount - sum.TargetCount;

            if (sum.Delta == 0)
            {
                sum.Reason = "頂点数は既に一致しています";
                return sum;
            }

            sum.CanExecute = true;
            sum.Reason = sum.Delta > 0
                ? $"対象穴の辺を長い順に {sum.Delta} 箇所割ります"
                : $"対象穴の辺を短い順に {-sum.Delta} 箇所潰します";
            return sum;
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>
        /// 対象穴の頂点数を基準穴に合わせる。成功可否と説明を返す。
        /// </summary>
        public bool Execute(out string message)
        {
            message = null;

            var sum = Inspect();
            if (!sum.CanExecute) { message = sum.Reason; return false; }

            var model = _context?.Model;
            var mc    = model?.GetMeshContext(_target.MeshIndex);
            var mo    = mc?.MeshObject;
            if (mo == null) { message = "対象穴の描画オブジェクトが見つかりません"; return false; }

            var undo = _context.UndoController;

            // 生成ミラーは実体側から作り直すため、Undo の記録対象に含める。
            // 片側だけ記録すると Undo で実体とミラーが食い違う。
            var realIndices = new List<int> { _target.MeshIndex };
            var captureIndices = MirrorBranchOps.CollectMirrorCaptureIndices(model, realIndices);

            // ミラー側への伝播計画。添字恒等対応の検証を含むため、位相を変える前に取る。
            var mirrorPlan = MirrorBranchOps.CaptureMirrorRebuildPlan(model, realIndices);

            var before = new MultiMeshTopologySnapshot();
            if (undo != null)
                foreach (int idx in captureIndices) before.CaptureMesh(model, idx);

            var opt = new HoleRingCountOps.Options
            {
                SplitTriangleIntoTriangles = SplitTriangleIntoTriangles,
            };

            // ミラー側へ同じ操作を掛けるための入力（変更前の添字）。
            int seedBefore = _target.Vertex;
            int hintBefore = _target.DirectionHint;

            var result = HoleRingCountOps.Execute(mo, seedBefore, hintBefore, sum.BaseCount, opt);

            if (!result.Ok)
            {
                message = result.Message;
                Debug.LogWarning($"[HoleRingCountTool] 実行失敗: {message}");
                return false;
            }

            // 種の添字は結合で詰め直されている。次の操作へ持ち越すため書き戻す。
            _target.Vertex        = result.SeedVertex;
            _target.DirectionHint = result.SeedDirectionHint;

            // 位相が変わったので控えを捨てる。基準穴が同じメッシュにある場合もあるため両方。
            _baseRing.Invalidate();
            _targetRing.Invalidate();

            // 実体側に掛けたのと同じ操作を、同じ添字でミラー側にも掛ける。
            // 辺の長さはミラーで保たれるので、選ばれる辺も添字で一致する。
            int mirrorApplied = MirrorBranchOps.ApplyToMirrors(model, mirrorPlan, (realIdx, mirrorMo) =>
            {
                if (realIdx != _target.MeshIndex) return false;
                var mr = HoleRingCountOps.Execute(mirrorMo, seedBefore, hintBefore, sum.BaseCount, opt);
                return mr.Ok;
            });

            // 消えた頂点を指したままの選択を残さない。
            mc.Selection?.ClearAll();

            _context.OnTopologyChanged();

            if (undo != null)
            {
                var after = new MultiMeshTopologySnapshot();
                foreach (int idx in captureIndices) after.CaptureMesh(model, idx);

                // MeshListStack の Context を今回のモデルに合わせる（Undo 時の復元先）。
                undo.SetModelContext(model);

                string desc = $"Hole Ring Count ({result.StartCount} -> {result.FinalCount})";
                var record = new MultiMeshTopologySnapshotRecord(before, after, desc);
                PLDiag.UndoRecord("MeshList", desc, record);
                undo.MeshListStack.Record(record, desc);
            }

            message = result.Message;

            Debug.Log($"[HoleRingCountTool] 完了: {result.StartCount} → {result.FinalCount} "
                    + $"(目標 {sum.BaseCount}) / 割った {result.SplitCount} / 潰した {result.MergeCount} "
                    + $"/ 頂点 +{result.AddedVertexCount} -{result.RemovedVertexCount} "
                    + $"/ 面 +{result.AddedFaceCount} -{result.RemovedFaceCount} "
                    + $"/ ミラー伝播 {mirrorApplied} (対象 {mirrorPlan.Entries.Count} / 検証落ち {mirrorPlan.RejectedCount})");

            return result.Reached;
        }
    }
}
