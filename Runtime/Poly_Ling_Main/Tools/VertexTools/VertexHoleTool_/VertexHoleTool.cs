// VertexHoleTool.cs
// 頂点に穴あけツール - 選択した頂点を消してそこに穴を開ける。
// 実処理は VertexHoleOps。ここは対象の集約・Undo 記録・通知だけを担う。
//
// 【対象】選択中の描画オブジェクト全部（マルチセレクト対応）× 各オブジェクトの
//   選択頂点全部（複数対応）。互いに干渉する頂点は VertexHoleOps 側で除外される。
//
// 【トポロジカル変更の分類】
// 削除を伴う変更に該当するため、実行後は ctx.OnTopologyChanged() で全選択をクリアする。
//
// 【Undo】複数メッシュを書き換えるため MeshUndoContext.MeshObject 経由の
//   MeshObjectSnapshot は使えない（MeshUndoContext.cs:41-47）。
//   MeshContextIndex を持つ MultiMeshTopologySnapshotRecord を MeshListStack へ
//   1件だけ記録する（メッシュが何個でも Undo は1手）。

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
    /// 頂点に穴あけツール。マウス操作は持たず、UI からの実行のみ。
    /// </summary>
    public class VertexHoleTool : IEditTool
    {
        public string Name        => "VertexHole";
        public string DisplayName => "Vertex Hole";

        // ================================================================
        // 設定
        // ================================================================

        private readonly VertexHoleSettings _settings = new VertexHoleSettings();
        public IToolSettings Settings => _settings;

        /// <summary>新頂点の位置比率（1.00 が指定頂点の位置）。</summary>
        public float Ratio
        {
            get => _settings.Ratio;
            set => _settings.Ratio = value;
        }

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

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)               => false;
        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta) => false;
        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)                 => false;

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)   { _context = ctx; }
        public void OnDeactivate(ToolContext ctx) { _context = null; }
        public void Reset() { }

        // ================================================================
        // 対象の集約
        // ================================================================

        /// <summary>1メッシュぶんの対象。</summary>
        public struct HoleTarget
        {
            public int        MeshIndex;
            public MeshContext MeshContext;
            public List<int>  Apexes;
        }

        /// <summary>
        /// 選択中の描画オブジェクトを走査し、選択頂点を持つものだけを返す。
        /// ボーン表示用メッシュは編集対象外。
        /// </summary>
        private List<HoleTarget> EnumerateTargets()
        {
            var list = new List<HoleTarget>();

            var model = _context?.Model;
            if (model == null) return list;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject == null) continue;
                if (mc.Type == MeshType.Bone) continue;

                var sel = mc.SelectedVertices;
                if (sel == null || sel.Count == 0) continue;

                list.Add(new HoleTarget
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
        public struct HoleSummary
        {
            /// <summary>実行できるか。</summary>
            public bool CanExecute;
            /// <summary>実行対象のオブジェクト数。</summary>
            public int ObjectCount;
            /// <summary>実行対象の頂点数。</summary>
            public int TargetCount;
            /// <summary>干渉・単独不可で除外した頂点数。</summary>
            public int SkippedCount;
            /// <summary>作られる新頂点の合計。</summary>
            public int NeighborTotal;
            /// <summary>張り替える面の合計。</summary>
            public int FaceTotal;
            /// <summary>実行できない理由（CanExecute==false のときのみ）。</summary>
            public string Reason;
        }

        /// <summary>
        /// 対象の下調べ。選択頂点が無い・全部干渉で落ちるときは CanExecute=false。
        /// </summary>
        public HoleSummary Inspect()
        {
            var sum = new HoleSummary();

            var targets = EnumerateTargets();
            if (targets.Count == 0)
            {
                sum.Reason = "頂点を選択してください";
                return sum;
            }

            foreach (var t in targets)
            {
                var info = VertexHoleOps.InspectMany(t.MeshContext.MeshObject, t.Apexes);

                sum.SkippedCount  += info.SkippedCount;
                sum.NeighborTotal += info.NeighborTotal;
                sum.FaceTotal     += info.FaceTotal;

                if (info.TargetCount <= 0) continue;

                sum.ObjectCount++;
                sum.TargetCount += info.TargetCount;
            }

            if (sum.TargetCount == 0)
            {
                sum.Reason = sum.SkippedCount > 0
                    ? "選択頂点が互いに干渉しているため実行できません"
                    : "頂点を選択してください";
                return sum;
            }

            sum.CanExecute = true;
            return sum;
        }

        /// <summary>穴あけを実行する。対象メッシュすべてを1回の Undo にまとめる。</summary>
        public void TriggerHole()
        {
            var model   = _context?.Model;
            var targets = EnumerateTargets();

            if (model == null || targets.Count == 0)
            {
                Debug.LogWarning($"[VertexHoleTool] 実行中止: model={model != null}, targets={targets.Count}");
                return;
            }

            var undo = _context.UndoController;

            var before = new MultiMeshTopologySnapshot();
            if (undo != null)
                foreach (var t in targets) before.CaptureMesh(model, t.MeshIndex);

            int createdTotal  = 0;
            int modifiedTotal = 0;
            int skippedTotal  = 0;
            int okMeshes      = 0;
            var doneIndices   = new List<int>();
            string lastReason = null;

            foreach (var t in targets)
            {
                bool ok = VertexHoleOps.ExecuteMany(
                    t.MeshContext.MeshObject, t.Apexes, Ratio,
                    out int created, out int modified, out int skipped, out string reason);

                skippedTotal += skipped;

                if (!ok)
                {
                    lastReason = reason;
                    continue;
                }

                createdTotal  += created;
                modifiedTotal += modified;
                okMeshes++;
                doneIndices.Add(t.MeshIndex);

                // 消えた頂点を指したままの選択を残さない。
                t.MeshContext.Selection?.ClearAll();
            }

            if (okMeshes == 0)
            {
                Debug.LogWarning($"[VertexHoleTool] 実行失敗: {lastReason ?? "対象がありません"}");
                return;
            }

            _context.OnTopologyChanged();

            if (undo != null)
            {
                var after = new MultiMeshTopologySnapshot();
                foreach (int idx in doneIndices) after.CaptureMesh(model, idx);

                // MeshListStack の Context を今回のモデルに合わせる（Undo 時の復元先）。
                undo.SetModelContext(model);

                string desc = $"Vertex Hole ({okMeshes} objs / {createdTotal} verts / {modifiedTotal} faces)";
                var record = new MultiMeshTopologySnapshotRecord(before, after, desc);
                PLDiag.UndoRecord("MeshList", desc, record);
                undo.MeshListStack.Record(record, desc);
            }

            Debug.Log($"[VertexHoleTool] 穴あけ完了: オブジェクト {okMeshes} / 新頂点 {createdTotal} "
                    + $"/ 張り替えた面 {modifiedTotal} / 除外 {skippedTotal} / ratio={Ratio:F2}");
        }
    }
}
