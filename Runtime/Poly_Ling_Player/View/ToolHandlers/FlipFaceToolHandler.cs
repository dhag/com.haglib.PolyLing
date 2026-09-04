// FlipFaceToolHandler.cs
// FlipFaceTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class FlipFaceToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly FlipFaceTool _tool = new FlipFaceTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;
        public Action            NotifyTopologyChanged;

        // ================================================================
        // 設定公開API
        // ================================================================

        /// <summary>
        /// 面を反転する。
        ///
        /// private にしてある。パネルからの直呼びは塞ぎ、
        /// FlipFaceCommand 経由に統一するため。
        /// </summary>
        private void FlipSelectedCore() => _tool.FlipSelectedFaces();
        private void FlipAllCore()      => _tool.FlipAllFaces();

        /// <summary>
        /// 面反転コマンドを実行する。
        ///
        /// 【マウス／パネル経路と同じ実装を通す】
        ///   反転そのものは FlipFaceTool が正典。ここは対象の照合と
        ///   前提条件の確認だけを行い、同じ経路を呼ぶ。
        ///
        /// 【前提条件をここで見る理由】
        ///   FlipFaceTool は Inspect を持たず、失敗内容は private な _lastMessage に
        ///   入るだけで外から読めない（FlipFaceTool.cs:31, 95, 102）。
        ///   そのため、ツールが内部で見ているのと同じデータ（選択面 / 面数）を
        ///   ここでも確かめて失敗理由を作る。反転処理は複製しない。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.FlipFaceCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            var mc = model.ActiveMeshContext;

            if (cmd.Scope == Poly_Ling.Data.FlipFaceCommand.FlipScope.Selected)
            {
                var faces = mc.Selection?.Faces;
                if (faces == null || faces.Count == 0)
                {
                    reason = "面を選択してください";
                    return false;
                }
            }
            else if (mc.MeshObject.FaceCount == 0)
            {
                reason = "反転できる面がありません";
                return false;
            }

            // 実行時と同じコンテキストを通す。
            var ctx = GetToolContext?.Invoke();
            if (ctx != null) Activate(ctx);

            if (cmd.Scope == Poly_Ling.Data.FlipFaceCommand.FlipScope.Selected)
                FlipSelectedCore();
            else
                FlipAllCore();

            return true;
        }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)         { _commandQueue   = queue; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) {}
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) {}
        public void UpdateHover(Vector2 screenPos, ToolContext ctx) {}
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

        // ================================================================
        // 内部ヘルパー
        // ================================================================


        private ToolContext GetEnrichedCtx()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return null;
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
            return ctx;
        }

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        private ToolContext BuildCtx(ModifierKeys mods, Vector2 sp)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;
            var ctx = GetToolContext?.Invoke() ?? new ToolContext();
            ctx.Model          = model;
            ctx.UndoController = _undoController;
            ctx.Repaint        = OnRepaint;
            ctx.SyncMesh = () =>
            {
                foreach (int idx in model.SelectedDrawableMeshIndices)
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc != null) OnSyncMeshPositions?.Invoke(mc);
                }
            };
            ctx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(sp, ctx),
            };
            return ctx;
        }

        private static Vector2 ToImgui(Vector2 sp, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(sp.x, h - sp.y);
        }
    }
}
