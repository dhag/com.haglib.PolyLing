// Tools/MergeVerticesTool.cs
// 頂点マージツール - 選択頂点のうち距離がしきい値以下のものを統合
// Phase 4: MeshMergeHelper使用に変更
// Phase 5: OnTopologyChanged()による標準的な選択クリア処理
//
// 【トポロジカル変更の分類】
// このツールは「削除を伴う変更」に該当するため、
// 実行後は ctx.OnTopologyChanged() で全選択をクリアする。

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling.Ops;
using Poly_Ling.Commands;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 頂点マージツール
    /// </summary>
    public partial class MergeVerticesTool : IEditTool
    {
        public string Name => "Merge";
        public string DisplayName => "Merge";
        // ToolCategory Category => ToolCategory.Topology;

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private MergeVerticesSettings _settings = new MergeVerticesSettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        public float Threshold
        {
            get => _settings.Threshold;
            set => _settings.Threshold = value;
        }

        public bool ShowPreview
        {
            get => _settings.ShowPreview;
            set => _settings.ShowPreview = value;
        }

        // Player ビュー用公開 API
        public void TriggerMerge()
        {
            if (_lastContext != null)
                ExecuteMergeByThreshold(_lastContext);
            else
                _pendingMerge = true;
        }
        public MergePreviewInfo PreviewInfo => _preview;

        // === プレビュー ===
        private MergePreviewInfo _preview = new MergePreviewInfo { Groups = new List<List<int>>() };
#pragma warning disable CS0414
        private bool _previewDirty = true;  // 将来の最適化用
#pragma warning restore CS0414

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            // クリックでは何もしない（UIからマージを実行）
            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            return false;
        }

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        private bool _pendingMerge = false;
        private ToolContext _lastContext;

        public void OnActivate(ToolContext ctx)
        {
            _previewDirty = true;
            _lastContext = ctx;
        }

        public void OnDeactivate(ToolContext ctx)
        {
            _preview = default;
        }

        public void Reset()
        {
            _preview = default;
            _previewDirty = true;
            _pendingMerge = false;
        }

        /// <summary>
        /// 毎フレーム呼ばれる更新処理（SimpleMeshFactory側から呼び出す）
        /// </summary>
        public void Update(ToolContext ctx)
        {
            _lastContext = ctx;

            // プレビュー更新（毎フレーム再計算 - 選択変更を検出するため）
            if (ctx.ActiveMeshObject != null && ctx.SelectedVertices != null)
            {
                _preview = CalculatePreview(ctx.ActiveMeshObject, ctx.SelectedVertices, Threshold);
            }

            // マージ実行
            if (_pendingMerge && ctx.ActiveMeshObject != null)
            {
                ExecuteMergeByThreshold(ctx);
                _pendingMerge = false;
            }
        }

        /// <summary>
        /// 選択変更時に呼び出し
        /// </summary>
        public void OnSelectionChanged()
        {
            _previewDirty = true;
        }

        // ================================================================
        // マージ実行（UIボタン / ショートカットから）
        //
        // 2 種類ある:
        //   ExecuteMergeByThreshold … 選択頂点のうち距離が Threshold 以下のものだけを
        //                             グループごとに結合する（従来の「頂点マージ」）。
        //   ExecuteMergeToCentroid  … 距離を一切見ず、選択頂点を 1 点（重心）へ結合する。
        //
        // どちらも ctx を引数で受け取り、ツールをアクティブにしなくても呼べる。
        // ショートカット単発コマンドから使うため、_pendingMerge 経由の遅延実行
        // （Update() 待ち）には依存しない。
        // ================================================================

        /// <summary>
        /// 選択頂点のうち、距離が Threshold 以下のものを結合する。
        /// </summary>
        public void ExecuteMergeByThreshold(ToolContext ctx)
        {
            if (ctx == null) return;
            if (ctx.ActiveMeshObject == null || ctx.SelectedVertices == null) return;
            if (ctx.SelectedVertices.Count < 2) return;

            // Undo用スナップショット
            MeshObjectSnapshot before = ctx.UndoController?.VertexEditStack != null && ctx.ActiveMeshContext != null
                ? MeshObjectSnapshot.Capture(ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext)
                : default;

            // MeshMergeHelper使用
            var result = MeshMergeHelper.MergeVerticesAtSamePosition(ctx.ActiveMeshObject, ctx.SelectedVertices, Threshold);

            if (result.Success)
            {
                // トポロジカル変更後の標準処理（削除を伴うため選択クリア）
                ctx.OnTopologyChanged();

                // Undo記録（キュー経由）
                if (ctx.UndoController != null && ctx.CommandQueue != null)
                {
                    MeshObjectSnapshot after = MeshObjectSnapshot.Capture(ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext);
                    ctx.CommandQueue.Enqueue(new RecordTopologyChangeCommand(
                        ctx.UndoController, before, after, "Merge Vertices"));
                }

                Debug.Log($"[MergeTool] {result.Message}");
            }
        }

        /// <summary>
        /// 距離を見ず、選択頂点をまとめて 1 点（重心）へ結合する。
        ///
        /// 結合後は OnTopologyChanged() が全選択をクリアするので、
        /// 連続操作しやすいよう、生成された頂点だけを選択し直す。
        /// この再選択は Undo スナップショット after を撮った後に行うため、
        /// Undo/Redo で復元される選択状態には含まれない。
        /// </summary>
        public void ExecuteMergeToCentroid(ToolContext ctx)
        {
            if (ctx == null) return;
            if (ctx.ActiveMeshObject == null || ctx.SelectedVertices == null) return;
            if (ctx.SelectedVertices.Count < 2)
            {
                Debug.LogWarning("[MergeTool] EARLY RETURN: 結合には 2 頂点以上の選択が必要です "
                               + $"(selected={ctx.SelectedVertices?.Count ?? 0})");
                return;
            }

            // Undo用スナップショット
            MeshObjectSnapshot before = ctx.UndoController?.VertexEditStack != null && ctx.ActiveMeshContext != null
                ? MeshObjectSnapshot.Capture(ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext)
                : default;

            int selectedCount = ctx.SelectedVertices.Count;
            int mergedVertex  = MeshMergeHelper.MergeVerticesToCentroid(
                ctx.ActiveMeshObject, new HashSet<int>(ctx.SelectedVertices));

            if (mergedVertex < 0)
            {
                Debug.LogWarning("[MergeTool] 結合に失敗しました（有効な選択頂点が 2 未満）");
                return;
            }

            // トポロジカル変更後の標準処理（削除を伴うため選択クリア）
            ctx.OnTopologyChanged();

            // Undo記録（キュー経由）
            if (ctx.UndoController != null && ctx.CommandQueue != null)
            {
                MeshObjectSnapshot after = MeshObjectSnapshot.Capture(ctx.ActiveMeshContext, ctx.UndoController.MeshUndoContext);
                ctx.CommandQueue.Enqueue(new RecordTopologyChangeCommand(
                    ctx.UndoController, before, after, "Merge Vertices (Centroid)"));
            }

            // 結合後の頂点を選択し直す（連続操作用）
            var sel = ctx.SelectionState;
            if (sel != null && mergedVertex < ctx.ActiveMeshObject.VertexCount)
                sel.SelectVertex(mergedVertex, false);

            Debug.Log($"[MergeTool] Merged {selectedCount} vertices into 1 (centroid), result index = {mergedVertex}");
        }

        // ================================================================
        // プレビュー計算
        // ================================================================

        private MergePreviewInfo CalculatePreview(MeshObject meshObject, HashSet<int> selectedVertices, float threshold)
        {
            var result = new MergePreviewInfo { Groups = new List<List<int>>() };

            if (meshObject == null || selectedVertices == null || selectedVertices.Count < 2)
                return result;

            var validSelected = selectedVertices
                .Where(v => v >= 0 && v < meshObject.VertexCount)
                .ToList();

            if (validSelected.Count < 2)
                return result;

            // Union-Find
            var parent = new int[meshObject.VertexCount];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;

            int Find(int x)
            {
                if (parent[x] != x) parent[x] = Find(parent[x]);
                return parent[x];
            }

            void Unite(int x, int y)
            {
                int rx = Find(x), ry = Find(y);
                if (rx != ry) parent[rx] = ry;
            }

            // 距離計算
            for (int i = 0; i < validSelected.Count; i++)
            {
                for (int j = i + 1; j < validSelected.Count; j++)
                {
                    float dist = Vector3.Distance(
                        meshObject.Vertices[validSelected[i]].Position,
                        meshObject.Vertices[validSelected[j]].Position);

                    if (dist <= threshold)
                    {
                        Unite(validSelected[i], validSelected[j]);
                    }
                }
            }

            // グループ収集
            var groups = new Dictionary<int, List<int>>();
            foreach (int v in validSelected)
            {
                int root = Find(v);
                if (!groups.ContainsKey(root))
                    groups[root] = new List<int>();
                groups[root].Add(v);
            }

            result.Groups = groups.Values.Where(g => g.Count >= 2).ToList();
            result.GroupCount = result.Groups.Count;
            result.TotalVerticesToMerge = result.Groups.Sum(g => g.Count - 1);

            return result;
        }

        // ================================================================
        // 静的マージメソッド（外部から呼び出し可能）- MeshMergeHelperへのラッパー
        // ================================================================

        /// <summary>
        /// 指定された頂点のうち、しきい値以下の距離にあるものをマージする（静的版）
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="targetVertices">マージ対象の頂点インデックス</param>
        /// <param name="threshold">距離しきい値</param>
        /// <returns>マージ結果</returns>
        public static MergeResult MergeVerticesAtSamePosition(MeshObject meshObject, HashSet<int> targetVertices, float threshold = 0.001f)
        {
            return MeshMergeHelper.MergeVerticesAtSamePosition(meshObject, targetVertices, threshold);
        }

        /// <summary>
        /// メッシュ内の全頂点を対象に、しきい値以下の距離にあるものをマージする
        /// </summary>
        /// <param name="meshObject">対象メッシュ</param>
        /// <param name="threshold">距離しきい値</param>
        /// <returns>マージ結果</returns>
        public static MergeResult MergeAllVerticesAtSamePosition(MeshObject meshObject, float threshold = 0.001f)
        {
            return MeshMergeHelper.MergeAllVerticesAtSamePosition(meshObject, threshold);
        }
    }

    /// <summary>
    /// マージプレビュー情報
    /// </summary>
    public struct MergePreviewInfo
    {
        public int GroupCount;
        public int TotalVerticesToMerge;
        public List<List<int>> Groups;
    }
}
