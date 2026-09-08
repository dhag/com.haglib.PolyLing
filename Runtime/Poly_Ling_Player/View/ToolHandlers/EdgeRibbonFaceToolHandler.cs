// EdgeRibbonFaceToolHandler.cs
// EdgeRibbonFaceTool を Player 側の即時実行口へ橋渡しする。
// InteractionMode は切り替えない。

using System;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Commands;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class EdgeRibbonFaceToolHandler : IPlayerToolHandler
    {
        private readonly EdgeRibbonFaceTool _tool =
            new EdgeRibbonFaceTool();

        private ProjectContext _project;
        private MeshUndoController _undoController;
        private CommandQueue _commandQueue;

        public Func<ToolContext> GetToolContext;
        public Action OnRepaint;
        public Action NotifyTopologyChanged;

        public EdgeRibbonFaceSettings Settings =>
            _tool.RibbonSettings;

        public void SetProject(ProjectContext project)
        {
            _project = project;
        }

        public void SetUndoController(MeshUndoController controller)
        {
            _undoController = controller;
        }

        public void SetCommandQueue(CommandQueue queue)
        {
            _commandQueue = queue;
        }

        public int GetSelectedEdgeCount()
        {
            return EdgeRibbonFaceTool.GetSelectedEdgeCount(
                _project?.CurrentModel);
        }

        public bool TriggerGenerate(out string reason)
        {
            ToolContext ctx = BuildContext();

            if (ctx == null)
            {
                reason = "ModelContext がありません";
                return false;
            }

            return _tool.Execute(ctx, out reason);
        }

        private ToolContext BuildContext()
        {
            var model = _project?.CurrentModel;

            if (model == null)
                return null;

            ToolContext ctx =
                GetToolContext?.Invoke() ?? new ToolContext();

            ctx.Model = model;
            ctx.UndoController = _undoController;
            ctx.CommandQueue = _commandQueue;
            ctx.CurrentMaterialIndex = model.CurrentMaterialIndex;
            ctx.Repaint = OnRepaint;
            ctx.NotifyTopologyChanged = NotifyTopologyChanged;

            // 追加のみだが、GPU側のトポロジ再構築用。
            ctx.SyncMesh = () => { };

            if (_undoController?.MeshUndoContext != null)
                _undoController.MeshUndoContext.ParentModelContext = model;

            return ctx;
        }

        // 即時実行ツールなのでマウス入力は使用しない。
        public void OnLeftClick(
            PlayerHitResult hit,
            Vector2 screenPos,
            ModifierKeys mods) { }

        public void OnLeftDragBegin(
            PlayerHitResult hit,
            Vector2 screenPos,
            ModifierKeys mods) { }

        public void OnLeftDrag(
            Vector2 screenPos,
            Vector2 delta,
            ModifierKeys mods) { }

        public void OnLeftDragEnd(
            Vector2 screenPos,
            ModifierKeys mods) { }
    }
}
