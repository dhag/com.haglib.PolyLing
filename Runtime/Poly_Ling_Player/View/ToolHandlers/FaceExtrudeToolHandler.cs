// FaceExtrudeToolHandler.cs
// FaceExtrudeTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class FaceExtrudeToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly FaceExtrudeTool _tool = new FaceExtrudeTool();
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
        public Action            OnEnterTransformDragging;
        public Action            OnExitTransformDragging;
        public Action            OnApplyCompleted;

        // ================================================================
        // 設定公開API
        // ================================================================

        public FaceExtrudeSettings.ExtrudeType Type { get => _tool.Type; set => _tool.Type = value; }
        public float BevelScale        { get => _tool.BevelScale;        set => _tool.BevelScale = value; }
        public bool  IndividualNormals { get => _tool.IndividualNormals; set => _tool.IndividualNormals = value; }
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
            var el = GetHoverElement?.Invoke(MeshSelectMode.Face) ?? PlayerHoverElement.None;
            _tool.PrepareHit(el.Kind == PlayerHoverKind.Face ? el.FaceIndex : -1);
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
        ///   FaceExtrudeCommand を 1 本発行する。実際の生成と Undo 記録は
        ///   FaceExtrudeTool.ApplyExtrudeFromCommand が行う。
        ///
        /// 【SendCommand 未結線のとき】
        ///   取り出さず OnMouseUp（EndExtrude）に確定させる（3-d と同じ方針）。
        /// </summary>
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;

            bool  taken = false;
            int   takenFace = -1;
            float takenDistance = 0f;
            var   type   = _tool.Type;
            float bevel  = _tool.BevelScale;
            bool  indiv  = _tool.IndividualNormals;

            if (SendCommand != null && _tool.ExtrudePending)
                taken = _tool.TryTakeExtrudeFromDrag(ctx, out takenFace, out takenDistance);

            // 取り出したときは _snapshotBefore が null なので EndExtrude は Undo を積まない。
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));

            if (taken)
            {
                var model = _project?.CurrentModel;
                var mc    = model?.ActiveMeshContext;
                if (model != null && mc != null)
                {
                    SendCommand.Invoke(new Poly_Ling.Data.FaceExtrudeCommand(
                        _project.CurrentModelIndex,
                        new[] { model.IndexOf(mc) },
                        takenFace, takenDistance,
                        type, bevel, indiv));
                }
            }

            OnApplyCompleted?.Invoke();
        }

        /// <summary>コマンドの発行先（Viewer から結線）。</summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        /// <summary>
        /// 面の押し出しコマンドを実行する。
        /// 生成そのものは FaceExtrudeTool が正典。ここは対象の照合だけを行う。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.FaceExtrudeCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            var ctx = GetEnrichedCtx();
            if (ctx == null) { reason = "ツールコンテキストがありません"; return false; }

            return _tool.ApplyExtrudeFromCommand(
                ctx, cmd.FaceIndex, cmd.Distance,
                cmd.Type, cmd.BevelScale, cmd.IndividualNormals, out reason);
        }
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            var el = GetHoverElement?.Invoke(MeshSelectMode.Face) ?? PlayerHoverElement.None;
            _tool.SetHoverFace(el.Kind == PlayerHoverKind.Face ? el.FaceIndex : -1);
        }

        // ── UIToolkit オーバーレイ用 ────────────────────────────────────
        public int HoverFace => _tool.HoverFace;
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
            ctx.EnterTransformDragging   = () => OnEnterTransformDragging?.Invoke();
            ctx.ExitTransformDragging    = () => OnExitTransformDragging?.Invoke();
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
