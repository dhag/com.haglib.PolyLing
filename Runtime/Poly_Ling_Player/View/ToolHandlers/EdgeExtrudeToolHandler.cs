// EdgeExtrudeToolHandler.cs
// EdgeExtrudeTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    public class EdgeExtrudeToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly EdgeExtrudeTool _tool = new EdgeExtrudeTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;
        public Action            NotifyTopologyChanged;
        /// <summary>GPU ホバー結果取得。FindEdgeAtPosition 等 CPU 側探索の代替。</summary>
        public Func<MeshSelectMode, PlayerHoverElement> GetHoverElement;
        public Action            OnApplyCompleted;

        // ================================================================
        // 設定公開API
        // ================================================================

        public EdgeExtrudeSettings.ExtrudeMode Mode { get => _tool.Mode; set => _tool.Mode = value; }
        public bool SnapToAxis { get => _tool.SnapToAxis; set => _tool.SnapToAxis = value; }
        public float DragSensitivity { get => _tool.DragSensitivity; set => _tool.DragSensitivity = value; }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)         { _commandQueue   = queue; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var el = GetHoverElement?.Invoke(MeshSelectMode.Edge | MeshSelectMode.Line) ?? PlayerHoverElement.None;
            var edge = (el.Kind == PlayerHoverKind.Edge) ? new VertexPair(el.EdgeV1, el.EdgeV2) : (VertexPair?)null;
            int  line = (el.Kind == PlayerHoverKind.Line) ? el.FaceIndex : -1;
            _tool.PrepareHit(edge, line);
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
        }
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
        }

        /// <summary>
        /// ドラッグ確定。
        ///
        /// 【1 ドラッグ = 1 コマンド】
        ///   ドラッグ中の生成はプレビューとして扱い、確定時に開始状態へ戻してから
        ///   EdgeExtrudeCommand を 1 本発行する。実際の生成と Undo 記録は
        ///   EdgeExtrudeTool.ApplyExtrudeFromCommand が行う。
        ///
        /// 【SendCommand 未結線のとき】
        ///   取り出さず OnMouseUp（EndExtrude）に確定させる（3-d と同じ方針）。
        /// </summary>
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;

            bool taken = false;
            VertexPair? takenEdge = null;
            int     takenLine   = -1;
            Vector3 takenOffset = Vector3.zero;

            if (SendCommand != null && _tool.ExtrudePending)
                taken = _tool.TryTakeExtrudeFromDrag(ctx, out takenEdge, out takenLine, out takenOffset);

            // 取り出したときは _snapshotBefore が null なので EndExtrude は Undo を積まない。
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));

            if (taken)
            {
                var model = _project?.CurrentModel;
                var mc    = model?.ActiveMeshContext;
                if (model != null && mc != null)
                {
                    SendCommand.Invoke(new Poly_Ling.Data.EdgeExtrudeCommand(
                        _project.CurrentModelIndex,
                        new[] { model.IndexOf(mc) },
                        takenEdge.HasValue ? takenEdge.Value.V1 : -1,
                        takenEdge.HasValue ? takenEdge.Value.V2 : -1,
                        takenLine,
                        takenOffset));
                }
            }

            OnApplyCompleted?.Invoke();
        }

        /// <summary>コマンドの発行先（Viewer から結線）。</summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        /// <summary>
        /// 辺・線分の押し出しコマンドを実行する。
        /// 生成そのものは EdgeExtrudeTool が正典。ここは対象の照合だけを行う。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.EdgeExtrudeCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            var ctx = GetEnrichedCtx();
            if (ctx == null) { reason = "ツールコンテキストがありません"; return false; }

            VertexPair? edge = (cmd.EdgeV1 >= 0 && cmd.EdgeV2 >= 0)
                ? new VertexPair(cmd.EdgeV1, cmd.EdgeV2)
                : (VertexPair?)null;

            return _tool.ApplyExtrudeFromCommand(ctx, edge, cmd.LineIndex, cmd.LocalOffset, out reason);
        }
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            var el = GetHoverElement?.Invoke(MeshSelectMode.Edge | MeshSelectMode.Line) ?? PlayerHoverElement.None;
            var edge = (el.Kind == PlayerHoverKind.Edge) ? new VertexPair(el.EdgeV1, el.EdgeV2) : (VertexPair?)null;
            int  line = (el.Kind == PlayerHoverKind.Line) ? el.FaceIndex : -1;
            _tool.SetHoverEdge(edge, line);
        }

        // ── UIToolkit オーバーレイ用 ────────────────────────────────────
        public VertexPair? HoverEdge => _tool.HoverEdge;
        public void Activate(ToolContext ctx)
        {
            if (ctx != null)
            {
                var model = _project?.CurrentModel;
                ctx.Model            = model;
                ctx.SelectedVertices = model?.ActiveMeshContext?.SelectedVertices;
                ctx.SelectionState   = model?.ActiveMeshContext?.Selection;
                ctx.UndoController   = _undoController;
            ctx.GetVertexWorldPosition = GetVertexWorldPosition;
                ctx.CommandQueue     = _commandQueue;
                ctx.Repaint          = OnRepaint;
                ctx.NotifyTopologyChanged = NotifyTopologyChanged;
                ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();
                if (_undoController?.MeshUndoContext != null)
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
            ctx.Model            = model;
            ctx.SelectedVertices = model?.ActiveMeshContext?.SelectedVertices;
            ctx.SelectionState   = model?.ActiveMeshContext?.Selection;
            ctx.UndoController   = _undoController;
            ctx.GetVertexWorldPosition = GetVertexWorldPosition;
            ctx.CommandQueue     = _commandQueue;
            ctx.Repaint          = OnRepaint;
            ctx.NotifyTopologyChanged    = NotifyTopologyChanged;
            ctx.SyncMesh                 = () => NotifyTopologyChanged?.Invoke();
            ctx.SyncMeshPositionsOnly    = () =>
            {
                var mc = _project?.CurrentModel?.ActiveMeshContext;
                if (mc != null) OnSyncMeshPositions?.Invoke(mc);
            };
            if (_undoController?.MeshUndoContext != null)
                _undoController.MeshUndoContext.ParentModelContext = model;
            return ctx;
        }


        /// <summary>
        /// 操作対象メッシュの頂点について GPU が計算したワールド座標を返す
        /// （Viewer から PlayerViewportManager.TryGetVertexWorld を結線）。
        /// CPU でスキニングを計算し直さないこと。
        /// </summary>
        public System.Func<int, UnityEngine.Vector3?> GetVertexWorldPosition;

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        private ToolContext BuildCtx(ModifierKeys mods, Vector2 sp)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;
            var ctx = GetToolContext?.Invoke() ?? new ToolContext();
            ctx.Model          = model;
            ctx.UndoController = _undoController;
            ctx.GetVertexWorldPosition = GetVertexWorldPosition;
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
