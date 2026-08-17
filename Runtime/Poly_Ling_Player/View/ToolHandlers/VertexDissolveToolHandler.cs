// VertexDissolveToolHandler.cs
// VertexDissolveTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// マウス操作は持たず、パネルからの実行のみを中継する。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class VertexDissolveToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly VertexDissolveTool _tool = new VertexDissolveTool();
        private          ProjectContext     _project;

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action            NotifyTopologyChanged;

        // ================================================================
        // 公開 API
        // ================================================================

        public int SelectedVertexCount => _tool.SelectedVertexCount;

        /// <summary>対象メッシュ全部を合わせた下調べ結果。</summary>
        public VertexDissolveTool.DissolveSummary Inspect() => _tool.Inspect();

        public void TriggerDissolve() => _tool.TriggerDissolve();

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)          => _project = project;
        public void SetUndoController(MeshUndoController ctrl)  { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)          { _commandQueue   = queue; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) { }
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) { }
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) { }
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) { }
        public void UpdateHover(Vector2 screenPos, ToolContext ctx) { }

        public void Activate(ToolContext ctx)
        {
            if (ctx != null)
            {
                var model = _project?.CurrentModel;
                var mc    = model?.ActiveMeshContext;
                ctx.Model            = model;
                ctx.SelectedVertices = mc?.SelectedVertices;
                ctx.SelectionState   = mc?.Selection;
                ctx.UndoController   = _undoController;
                ctx.CommandQueue     = _commandQueue;
                ctx.Repaint          = OnRepaint;
                ctx.NotifyTopologyChanged = NotifyTopologyChanged;
                ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();
                if (_undoController?.MeshUndoContext != null && model != null)
                    _undoController.MeshUndoContext.ParentModelContext = model;
            }
            _tool.OnActivate(ctx);
        }

        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }
    }
}
