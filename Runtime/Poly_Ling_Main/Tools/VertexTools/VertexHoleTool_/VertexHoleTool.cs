// VertexHoleTool.cs
// 頂点に穴あけツール - 選択した1頂点を消してそこに穴を開ける。
// 実処理は VertexHoleOps。ここは選択の検証・Undo 記録・通知だけを担う。
//
// 【トポロジカル変更の分類】
// 削除を伴う変更に該当するため、実行後は ctx.OnTopologyChanged() で全選択をクリアする。

using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

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

        public int SelectedVertexCount => _context?.SelectedVertices?.Count ?? 0;

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
        // 公開 API
        // ================================================================

        /// <summary>
        /// 対象の下調べ。選択が1頂点でないときは CanExecute=false を返す。
        /// </summary>
        public VertexHoleOps.HoleInfo Inspect()
        {
            var mesh = _context?.ActiveMeshObject;
            var sel  = _context?.SelectedVertices;

            if (mesh == null || sel == null || sel.Count == 0)
                return new VertexHoleOps.HoleInfo { Reason = "頂点を1つ選択してください" };

            if (sel.Count != 1)
                return new VertexHoleOps.HoleInfo { Reason = "選択は1頂点だけにしてください" };

            int apex = -1;
            foreach (int i in sel) { apex = i; break; }

            return VertexHoleOps.Inspect(mesh, apex);
        }

        /// <summary>穴あけを実行する。</summary>
        public void TriggerHole()
        {
            var mesh = _context?.ActiveMeshObject;
            var sel  = _context?.SelectedVertices;

            if (mesh == null || sel == null || sel.Count != 1)
            {
                Debug.LogWarning($"[VertexHoleTool] 実行中止: mesh={mesh != null}, selCount={sel?.Count ?? -1}");
                return;
            }

            int apex = -1;
            foreach (int i in sel) { apex = i; break; }

            var info = VertexHoleOps.Inspect(mesh, apex);
            if (!info.CanExecute)
            {
                Debug.LogWarning($"[VertexHoleTool] 実行中止: {info.Reason}");
                return;
            }

            MeshObjectSnapshot before =
                _context.UndoController != null && _context.ActiveMeshContext != null
                    ? MeshObjectSnapshot.Capture(
                        _context.ActiveMeshContext, _context.UndoController.MeshUndoContext, _context.SelectionState)
                    : default;

            bool ok = VertexHoleOps.Execute(
                mesh, apex, Ratio, out int created, out int modified, out string reason);

            if (!ok)
            {
                Debug.LogWarning($"[VertexHoleTool] 実行失敗: {reason}");
                return;
            }

            _context.OnTopologyChanged();

            if (_context.UndoController != null && _context.ActiveMeshContext != null)
            {
                var after = MeshObjectSnapshot.Capture(
                    _context.ActiveMeshContext, _context.UndoController.MeshUndoContext, _context.SelectionState);
                _context.CommandQueue?.Enqueue(new RecordTopologyChangeCommand(
                    _context.UndoController, before, after, _context.SelectionState,
                    $"Vertex Hole ({created} verts / {modified} faces)"));
            }

            Debug.Log($"[VertexHoleTool] 穴あけ完了: 新頂点 {created} / 張り替えた面 {modified} / ratio={Ratio:F2}");
        }
    }
}
