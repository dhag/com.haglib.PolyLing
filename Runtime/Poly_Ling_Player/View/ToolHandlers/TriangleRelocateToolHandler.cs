// TriangleRelocateToolHandler.cs
// TriangleRelocateTool を Player の呼び出し口に橋渡しする IPlayerToolHandler 実装。
// マウス操作は持たず、パネル／コマンドからの実行のみを中継する（Quad4To1ToolHandler と同じ構成）。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    [Poly_Ling.Data.PLTool("triangleRelocate", Description = "TriangleRelocateTool（確定は TriangleRelocateCommand）")]
    public class TriangleRelocateToolHandler : IPlayerToolHandler
    {
        private readonly TriangleRelocateTool _tool = new TriangleRelocateTool();
        public  Func<ProjectContext> GetProject;
        private ProjectContext _project => GetProject?.Invoke();

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action            NotifyTopologyChanged;

        [Poly_Ling.Data.PLToolState(Description = "TriangleRelocateTool.SelectedVertexCount")]
        public int SelectedVertexCount => _tool.SelectedVertexCount;

        [Poly_Ling.Data.PLToolStateGroup(Name = "inspect", Description = "実行前の下調べ（TriangleRelocateTool.RelocateSummary）")]
        public TriangleRelocateTool.RelocateSummary Inspect() => _tool.Inspect();

        /// <summary>
        /// コマンドから実行する。対象は照合だけ行い、選択は書き換えない。
        /// </summary>
        public bool ExecuteFromCommand(Poly_Ling.Data.TriangleRelocateCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!(cmd.T1 > 0f && cmd.T1 < cmd.T2 && cmd.T2 < 1f))
            { reason = $"比率は 0 < t1 < t2 < 1 にしてください（t1={cmd.T1}, t2={cmd.T2}）"; return false; }

            if (!PlayerCommandTargets.MatchesSelectedDrawables(model, cmd.MasterIndices, out reason))
                return false;

            var ctx = GetToolContext?.Invoke();
            if (ctx != null) Activate(ctx);

            var summary = Inspect();
            if (!summary.CanExecute)
            {
                reason = string.IsNullOrEmpty(summary.Reason) ? "実行できる対象がありません" : summary.Reason;
                return false;
            }

            _tool.TriggerRelocate(cmd.T1, cmd.T2);
            return true;
        }

        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)         { _commandQueue   = queue; }

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
