// VertexDissolveTool.cs
// 頂点溶解ツール - 選択した頂点を消し、その頂点を囲む面を1枚の N 角形に統合する。
// 実処理は VertexDissolveOps。ここは対象の集約・Undo 記録・通知だけを担う。
//
// 【対象】選択中の描画オブジェクト全部（マルチセレクト対応）× 各オブジェクトの
//   選択頂点全部（複数対応）。互いに干渉する頂点は VertexDissolveOps 側で除外される。
//
// 【トポロジカル変更の分類】
// 削除を伴う変更に該当するため、実行後は ctx.OnTopologyChanged() で全選択をクリアする。
//
// 【Undo】複数メッシュを書き換えるため MeshUndoContext.MeshObject 経由の
//   MeshObjectSnapshot は使えない。MeshContextIndex を持つ
//   MultiMeshTopologySnapshotRecord を MeshListStack へ 1 件だけ記録する
//   （メッシュが何個でも Undo は1手）。VertexHoleTool と同じ方針。

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
    /// 頂点溶解ツール。マウス操作は持たず、UI からの実行のみ。
    /// </summary>
    public class VertexDissolveTool : IEditTool
    {
        public string Name        => "VertexDissolve";
        public string DisplayName => "Vertex Dissolve";

        /// <summary>設定は持たない。</summary>
        public IToolSettings Settings => null;

        // ================================================================
        // コンテキスト
        // ================================================================

        private ToolContext _context;

        /// <summary>対象メッシュ全部を合わせた選択頂点数。</summary>
        public int SelectedVertexCount
        {
            get
            {
                int total = 0;
                foreach (var t in EnumerateTargets()) total += t.Apexes.Count;
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
        public struct DissolveTarget
        {
            public int         MeshIndex;
            public MeshContext MeshContext;
            public List<int>   Apexes;
        }

        /// <summary>
        /// 選択中の描画オブジェクトを走査し、選択頂点を持つものだけを返す。
        /// ボーン表示用メッシュは編集対象外。
        /// </summary>
        private List<DissolveTarget> EnumerateTargets()
        {
            var list = new List<DissolveTarget>();

            var model = _context?.Model;
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;

                var sel = mc.SelectedVertices;
                if (sel == null || sel.Count == 0) continue;

                list.Add(new DissolveTarget
                {
                    MeshIndex   = idx,
                    MeshContext = mc,
                    Apexes      = new List<int>(sel),
                });
            }

            return list;
        }

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>複数メッシュぶんを合計した下調べ結果。</summary>
        public struct DissolveSummary
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>実行対象のオブジェクト数。</summary>
            public int ObjectCount;
            /// <summary>実行対象の頂点数。</summary>
            public int TargetCount;
            /// <summary>条件不一致・干渉で除外した頂点数。</summary>
            public int SkippedCount;
            /// <summary>統合される面の合計。</summary>
            public int FaceTotal;
            /// <summary>作られる面の頂点数の合計。</summary>
            public int RingTotal;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        /// <summary>
        /// 対象の下調べ。選択頂点が無い・全部除外されるときは CanExecute=false。
        /// </summary>
        public DissolveSummary Inspect()
        {
            var sum = new DissolveSummary();

            var targets = EnumerateTargets();
            if (targets.Count == 0)
            {
                sum.Reason = "頂点を選択してください";
                return sum;
            }

            foreach (var t in targets)
            {
                var info = VertexDissolveOps.InspectMany(t.MeshContext.MeshObject, t.Apexes);

                sum.SkippedCount += info.SkippedCount;
                sum.FaceTotal    += info.FaceTotal;
                sum.RingTotal    += info.RingTotal;

                if (info.TargetCount <= 0) continue;

                sum.ObjectCount++;
                sum.TargetCount += info.TargetCount;
            }

            if (sum.TargetCount == 0)
            {
                sum.Reason = sum.SkippedCount > 0
                    ? "選択頂点が条件を満たさないか、互いに干渉しています"
                    : "頂点を選択してください";
                return sum;
            }

            sum.CanExecute = true;
            return sum;
        }

        /// <summary>頂点溶解を実行する。対象メッシュすべてを1回の Undo にまとめる。</summary>
        public void TriggerDissolve()
        {
            var model   = _context?.Model;
            var targets = EnumerateTargets();

            if (model == null || targets.Count == 0)
            {
                Debug.LogWarning($"[VertexDissolveTool] 実行中止: model={model != null}, targets={targets.Count}");
                return;
            }

            var undo = _context.UndoController;

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

            int dissolvedTotal = 0;
            int removedTotal   = 0;
            int skippedTotal   = 0;
            int okMeshes       = 0;
            string lastReason  = null;

            // ミラー側へ同じ操作を掛けるための入力（変更前の添字）。
            var opInputs = new Dictionary<int, List<int>>();
            foreach (var t in targets) opInputs[t.MeshIndex] = t.Apexes;

            foreach (var t in targets)
            {
                bool ok = VertexDissolveOps.ExecuteMany(
                    t.MeshContext.MeshObject, t.Apexes,
                    out int dissolved, out int removed, out int skipped, out string reason);

                skippedTotal += skipped;

                if (!ok)
                {
                    lastReason = reason;
                    continue;
                }

                dissolvedTotal += dissolved;
                removedTotal   += removed;
                okMeshes++;

                // 消えた頂点を指したままの選択を残さない。
                t.MeshContext.Selection?.ClearAll();
            }

            if (okMeshes == 0)
            {
                Debug.LogWarning($"[VertexDissolveTool] 実行失敗: {lastReason ?? "対象がありません"}");
                return;
            }

            // 実体側に掛けたのと同じ操作を、同じ添字でミラー側にも掛ける。
            // 検証を落ちたペアは触らず、理由を ApplyToMirrors が出す。
            int mirrorApplied = MirrorBranchOps.ApplyToMirrors(model, mirrorPlan, (realIdx, mirrorMo) =>
            {
                if (!opInputs.TryGetValue(realIdx, out var src)) return false;
                return VertexDissolveOps.ExecuteMany(mirrorMo, src,
                    out _, out _, out _, out _);
            });

            _context.OnTopologyChanged();

            if (undo != null)
            {
                var after = new MultiMeshTopologySnapshot();
                foreach (int idx in captureIndices) after.CaptureMesh(model, idx);

                // MeshListStack の Context を今回のモデルに合わせる（Undo 時の復元先）。
                undo.SetModelContext(model);

                string desc = $"Vertex Dissolve ({okMeshes} objs / {dissolvedTotal} verts / {removedTotal} faces)";
                var record = new MultiMeshTopologySnapshotRecord(before, after, desc);
                PLDiag.UndoRecord("MeshList", desc, record);
                undo.MeshListStack.Record(record, desc);
            }

            Debug.Log($"[VertexDissolveTool] 頂点溶解完了: オブジェクト {okMeshes} / 溶かした頂点 {dissolvedTotal} "
                    + $"/ 消えた面 {removedTotal} / 除外 {skippedTotal} / ミラー伝播 {mirrorApplied} (対象 {mirrorPlan.Entries.Count} / 検証落ち {mirrorPlan.RejectedCount})");
        }
    }
}
