// FaceMergeTool.cs
// 面結合（辺指定）ツール - 選択した辺を挟む2枚の面を1枚に結合する。
// 実処理は DeleteVertices で選ぶ。
//   true  … FaceMergeCollapseOps（共有頂点をほかの面が使っていても新しい面から外す）
//   false … FaceMergeOps（ほかの面が使っていない共有頂点だけを外して消す）
// ここは対象の集約・Undo 記録・通知だけを担う。
// 旧「面結合（頂点削除）」（FaceMergeCollapseTool）はこのツールの DeleteVertices = true へ統合した。
//
// 【対象】選択中の描画オブジェクト全部（マルチセレクト対応）× 各オブジェクトの
//   選択辺全部（複数対応）。同じ面に関わる辺どうしは Ops 側で除外される。
//
// 【トポロジカル変更の分類】
// 削除を伴う変更に該当するため、実行後は ctx.OnTopologyChanged() で全選択をクリアする。
//
// 【Undo】複数メッシュを書き換えるため MeshUndoContext.MeshObject 経由の
//   MeshObjectSnapshot は使えない。MeshContextIndex を持つ
//   MultiMeshTopologySnapshotRecord を MeshListStack へ 1 件だけ記録する
//   （メッシュが何個でも Undo は1手）。VertexDissolveTool / Tri4To1Tool と同じ方針。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 面結合（辺指定）ツール。マウス操作は持たず、UI からの実行のみ。
    /// </summary>
    public class FaceMergeTool : IEditTool
    {
        public string Name        => "FaceMerge";
        public string DisplayName => "Face Merge (Edge)";

        /// <summary>設定は持たない。</summary>
        public IToolSettings Settings => null;

        /// <summary>
        /// 共有頂点を新しい面から外すか。true で FaceMergeCollapseOps、false で FaceMergeOps を使う。
        /// 下調べ（Inspect）・実行・ミラー側への伝播のすべてがこの値に従う。
        /// </summary>
        public bool DeleteVertices { get; set; } = true;

        // ================================================================
        // コンテキスト
        // ================================================================

        private ToolContext _context;

        /// <summary>対象メッシュ全部を合わせた選択辺数。</summary>
        public int SelectedEdgeCount
        {
            get
            {
                int total = 0;
                foreach (var t in EnumerateTargets()) total += t.Edges.Count;
                return total;
            }
        }

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
        public void Reset() { }

        // ================================================================
        // 対象の集約
        // ================================================================

        /// <summary>1メッシュぶんの対象。</summary>
        public struct MergeTarget
        {
            public int             MeshIndex;
            public MeshContext     MeshContext;
            public List<VertexPair> Edges;
        }

        /// <summary>
        /// 選択中の描画オブジェクトを走査し、選択辺を持つものだけを返す。
        /// ボーン表示用メッシュは編集対象外。
        /// </summary>
        private List<MergeTarget> EnumerateTargets()
        {
            var list = new List<MergeTarget>();

            var model = _context?.Model;
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;

                var sel = mc.SelectedEdges;
                if (sel == null || sel.Count == 0) continue;

                list.Add(new MergeTarget
                {
                    MeshIndex   = idx,
                    MeshContext = mc,
                    Edges       = new List<VertexPair>(sel),
                });
            }

            return list;
        }

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>複数メッシュぶんを合計した下調べ結果。</summary>
        public struct MergeSummary
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>実行対象のオブジェクト数。</summary>
            public int ObjectCount;
            /// <summary>実行対象の辺数。</summary>
            public int TargetCount;
            /// <summary>条件不一致・干渉で除外した辺数。</summary>
            public int SkippedCount;
            /// <summary>消える面の合計。</summary>
            public int RemovedFaceTotal;
            /// <summary>消える頂点の合計。</summary>
            public int RemovedVertexTotal;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        /// <summary>
        /// 対象の下調べ。選択辺が無い・全部除外されるときは CanExecute=false。
        /// </summary>
        public MergeSummary Inspect()
        {
            var sum = new MergeSummary();

            var targets = EnumerateTargets();
            if (targets.Count == 0)
            {
                sum.Reason = "辺を選択してください";
                return sum;
            }

            foreach (var t in targets)
            {
                int skipped, removedFaces, removedVerts, targetCount;
                if (DeleteVertices)
                {
                    var info = FaceMergeCollapseOps.InspectMany(t.MeshContext.MeshObject, t.Edges);
                    skipped = info.SkippedCount; removedFaces = info.RemovedFaceTotal;
                    removedVerts = info.RemovedVertexTotal; targetCount = info.TargetCount;
                }
                else
                {
                    var info = FaceMergeOps.InspectMany(t.MeshContext.MeshObject, t.Edges);
                    skipped = info.SkippedCount; removedFaces = info.RemovedFaceTotal;
                    removedVerts = info.RemovedVertexTotal; targetCount = info.TargetCount;
                }

                sum.SkippedCount       += skipped;
                sum.RemovedFaceTotal   += removedFaces;
                sum.RemovedVertexTotal += removedVerts;

                if (targetCount <= 0) continue;

                sum.ObjectCount++;
                sum.TargetCount += targetCount;
            }

            if (sum.TargetCount == 0)
            {
                sum.Reason = sum.SkippedCount > 0
                    ? "選択辺が条件を満たさないか、互いに干渉しています"
                    : "辺を選択してください";
                return sum;
            }

            sum.CanExecute = true;
            return sum;
        }

        /// <summary>結合を実行する。対象メッシュすべてを1回の Undo にまとめる。</summary>
        public void TriggerMerge()
        {
            var model   = _context?.Model;
            var targets = EnumerateTargets();

            if (model == null || targets.Count == 0)
            {
                Debug.LogWarning($"[FaceMergeTool] 実行中止: model={model != null}, targets={targets.Count}");
                return;
            }

            var undo = _context.UndoController;

            // 実行中に値が変わっても、実体側とミラー側で同じ Ops を使うように控える。
            bool deleteVertices = DeleteVertices;

            // 生成ミラーは実体側から作り直すため、Undo の記録対象に含める。
            // 片側だけ記録すると Undo で実体とミラーが食い違う。
            var realIndices = new List<int>();
            foreach (var t in targets) realIndices.Add(t.MeshIndex);
            var captureIndices = MirrorBranchOps.CollectMirrorCaptureIndices(model, realIndices);

            // ミラー側への伝播計画。添字恒等対応の検証を含むため、位相を変える前に取る。
            // ミラー側には実体側とまったく同じ操作を同じ添字で掛ける（ApplyToMirrors）。
            var mirrorPlan = MirrorBranchOps.CaptureMirrorRebuildPlan(model, realIndices);

            var before = new MultiMeshTopologySnapshot();
            if (undo != null)
                foreach (int idx in captureIndices) before.CaptureMesh(model, idx);

            int mergedTotal        = 0;
            int removedFaceTotal   = 0;
            int removedVertexTotal = 0;
            int skippedTotal       = 0;
            int okMeshes           = 0;
            string lastReason      = null;

            // ミラー側へ同じ操作を掛けるための入力（変更前の添字）。
            var opInputs = new Dictionary<int, List<VertexPair>>();
            foreach (var t in targets) opInputs[t.MeshIndex] = t.Edges;

            foreach (var t in targets)
            {
                bool ok = ExecuteOps(deleteVertices,
                    t.MeshContext.MeshObject, t.Edges,
                    out int merged, out int removedFaces, out int removedVerts,
                    out int skipped, out string reason);

                skippedTotal += skipped;

                if (!ok)
                {
                    lastReason = reason;
                    continue;
                }

                mergedTotal        += merged;
                removedFaceTotal   += removedFaces;
                removedVertexTotal += removedVerts;
                okMeshes++;

                // 消えた面・頂点を指したままの選択を残さない。
                t.MeshContext.Selection?.ClearAll();
            }

            if (okMeshes == 0)
            {
                Debug.LogWarning($"[FaceMergeTool] 実行失敗: {lastReason ?? "対象がありません"}");
                return;
            }

            // 実体側に掛けたのと同じ操作を、同じ添字でミラー側にも掛ける。
            // 検証を落ちたペアは触らず、理由を ApplyToMirrors が出す。
            int mirrorApplied = MirrorBranchOps.ApplyToMirrors(model, mirrorPlan, (realIdx, mirrorMo) =>
            {
                if (!opInputs.TryGetValue(realIdx, out var src)) return false;
                return ExecuteOps(deleteVertices, mirrorMo, src,
                    out _, out _, out _, out _, out _);
            });

            _context.OnTopologyChanged();

            if (undo != null)
            {
                var after = new MultiMeshTopologySnapshot();
                foreach (int idx in captureIndices) after.CaptureMesh(model, idx);

                // MeshListStack の Context を今回のモデルに合わせる（Undo 時の復元先）。
                undo.SetModelContext(model);

                string desc = $"Face Merge ({(deleteVertices ? "delete verts" : "keep verts")} / "
                            + $"{okMeshes} objs / {mergedTotal} edges / {removedFaceTotal} faces)";
                var record = new MultiMeshTopologySnapshotRecord(before, after, desc);
                PLDiag.UndoRecord("MeshList", desc, record);
                undo.MeshListStack.Record(record, desc);
                // ルートは FocusPriority。FocusedChildId が未設定だと Ctrl+Z が届かない。
                undo.FocusMeshList();
            }

            Debug.Log($"[FaceMergeTool] 結合完了（頂点を削除する={deleteVertices}）: オブジェクト {okMeshes} / 結合 {mergedTotal} 箇所 "
                    + $"/ 消えた面 {removedFaceTotal} / 消えた頂点 {removedVertexTotal} / 除外 {skippedTotal} / ミラー伝播 {mirrorApplied} (対象 {mirrorPlan.Entries.Count} / 検証落ち {mirrorPlan.RejectedCount})");
        }

        /// <summary>DeleteVertices に応じた Ops で一括実行する（実体側・ミラー側で共用）。</summary>
        private static bool ExecuteOps(
            bool deleteVertices, MeshObject mo, IEnumerable<VertexPair> edges,
            out int merged, out int removedFaces, out int removedVerts,
            out int skipped, out string reason)
        {
            return deleteVertices
                ? FaceMergeCollapseOps.ExecuteMany(mo, edges,
                    out merged, out removedFaces, out removedVerts, out skipped, out reason)
                : FaceMergeOps.ExecuteMany(mo, edges,
                    out merged, out removedFaces, out removedVerts, out skipped, out reason);
        }
    }
}
