// TriangleRelocateTool.cs
// 三角形の移し替えツール。三角形 1 枚と四角形 3 枚に囲まれた選択頂点について、
// 周りの三角形を向かいの四角形の対角側へ移す。
// 実処理は TriangleRelocateOps。ここは対象の集約・Undo 記録・通知だけを担う
// （Quad4To1Tool と同じ構成）。
//
// 【対象】選択中の描画オブジェクト全部 × 各オブジェクトの選択頂点全部。
//   同じ面を共有する頂点どうしは TriangleRelocateOps 側で除外される。
//
// 【Undo】MultiMeshTopologySnapshotRecord を MeshListStack へ 1 件だけ記録する。
// 【ミラー】実体側と同じ操作を同じ添字でミラー側にも掛ける（MirrorBranchOps.ApplyToMirrors）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Diagnostics;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Tools
{
    /// <summary>三角形の移し替えツール。マウス操作は持たず、UI からの実行のみ。</summary>
    public class TriangleRelocateTool : IEditTool
    {
        public string Name        => "TriangleRelocate";
        public string DisplayName => "Triangle Relocate";

        /// <summary>設定は持たない（比率はコマンドで渡す）。</summary>
        public IToolSettings Settings => null;

        private ToolContext _context;

        /// <summary>対象メッシュ全部を合わせた選択頂点数。</summary>
        public int SelectedVertexCount
        {
            get
            {
                int total = 0;
                foreach (var t in EnumerateTargets()) total += t.Vertices.Count;
                return total;
            }
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)                => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)                  => false;
        public void DrawGizmo(ToolContext ctx) { }
        public void OnActivate(ToolContext ctx)   { _context = ctx; }
        public void OnDeactivate(ToolContext ctx) { _context = null; }
        public void Reset() { }

        // ================================================================
        // 対象の集約
        // ================================================================

        private struct Target
        {
            public int         MeshIndex;
            public MeshContext MeshContext;
            public List<int>   Vertices;
        }

        private List<Target> EnumerateTargets()
        {
            var list = new List<Target>();
            var model = _context?.Model;
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;

                var sel = mc.SelectedVertices;
                if (sel == null || sel.Count == 0) continue;

                list.Add(new Target { MeshIndex = idx, MeshContext = mc, Vertices = new List<int>(sel) });
            }
            return list;
        }

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>複数メッシュぶんを合計した下調べ結果。</summary>
        public struct RelocateSummary
        {
            public bool   CanExecute;
            public int    ObjectCount;
            public int    TargetCount;
            public int    SkippedCount;
            public string Reason;
        }

        public RelocateSummary Inspect()
        {
            var sum = new RelocateSummary();
            var targets = EnumerateTargets();
            if (targets.Count == 0) { sum.Reason = "頂点を選択してください"; return sum; }

            foreach (var t in targets)
            {
                var info = TriangleRelocateOps.InspectMany(t.MeshContext.MeshObject, t.Vertices);
                sum.SkippedCount += info.SkippedCount;
                if (info.TargetCount <= 0) continue;
                sum.ObjectCount++;
                sum.TargetCount += info.TargetCount;
            }

            if (sum.TargetCount == 0)
            {
                sum.Reason = sum.SkippedCount > 0
                    ? "選択頂点が条件（三角形 1 枚と四角形 3 枚に囲まれた頂点）を満たさないか、互いに干渉しています"
                    : "頂点を選択してください";
                return sum;
            }
            sum.CanExecute = true;
            return sum;
        }

        /// <summary>移し替えを実行する。対象メッシュすべてを 1 回の Undo にまとめる。</summary>
        public void TriggerRelocate(float t1, float t2)
        {
            var model   = _context?.Model;
            var targets = EnumerateTargets();
            if (model == null || targets.Count == 0)
            {
                Debug.LogWarning($"[TriangleRelocateTool] 実行中止: model={model != null}, targets={targets.Count}");
                return;
            }

            var undo = _context.UndoController;

            var realIndices = new List<int>();
            foreach (var t in targets) realIndices.Add(t.MeshIndex);
            var captureIndices = MirrorBranchOps.CollectMirrorCaptureIndices(model, realIndices);
            var mirrorPlan     = MirrorBranchOps.CaptureMirrorRebuildPlan(model, realIndices);

            var before = new MultiMeshTopologySnapshot();
            if (undo != null)
                foreach (int idx in captureIndices) before.CaptureMesh(model, idx);

            int doneTotal = 0, skippedTotal = 0, okMeshes = 0;
            string lastReason = null;

            var opInputs = new Dictionary<int, List<int>>();
            foreach (var t in targets) opInputs[t.MeshIndex] = t.Vertices;

            foreach (var t in targets)
            {
                bool ok = TriangleRelocateOps.ExecuteMany(
                    t.MeshContext.MeshObject, t.Vertices, t1, t2,
                    out int done, out int skipped, out string reason);
                skippedTotal += skipped;
                if (!ok) { lastReason = reason; continue; }

                doneTotal += done;
                okMeshes++;
                // 消えた頂点を指したままの選択を残さない。
                t.MeshContext.Selection?.ClearAll();
            }

            if (okMeshes == 0)
            {
                Debug.LogWarning($"[TriangleRelocateTool] 実行失敗: {lastReason ?? "対象がありません"}");
                return;
            }

            int mirrorApplied = MirrorBranchOps.ApplyToMirrors(model, mirrorPlan, (realIdx, mirrorMo) =>
            {
                if (!opInputs.TryGetValue(realIdx, out var src)) return false;
                return TriangleRelocateOps.ExecuteMany(mirrorMo, src, t1, t2, out _, out _, out _);
            });

            _context.OnTopologyChanged();

            if (undo != null)
            {
                var after = new MultiMeshTopologySnapshot();
                foreach (int idx in captureIndices) after.CaptureMesh(model, idx);
                undo.SetModelContext(model);

                string desc = $"Triangle Relocate ({okMeshes} objs / {doneTotal} vertices)";
                var record = new MultiMeshTopologySnapshotRecord(before, after, desc);
                PLDiag.UndoRecord("MeshList", desc, record);
                undo.MeshListStack.Record(record, desc);
            }

            Debug.Log($"[TriangleRelocateTool] 完了: オブジェクト {okMeshes} / 実行 {doneTotal} 頂点 / 除外 {skippedTotal}"
                    + $" / ミラー伝播 {mirrorApplied} (対象 {mirrorPlan.Entries.Count} / 検証落ち {mirrorPlan.RejectedCount})");
        }
    }
}
