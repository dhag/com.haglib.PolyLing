// AddFaceToolHandler.cs
// AddFaceTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class AddFaceToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly AddFaceTool _tool = new AddFaceTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>面追加後のGPUバッファ再構築コールバック（ViewerCoreから設定）</summary>
        public Action NotifyTopologyChanged;
        /// <summary>
        /// クリック時にモデル・描画メッシュがなければ自動生成するコールバック。
        /// true を返したら生成成功（以降の処理を続行）、false なら失敗（処理中断）。
        /// </summary>
        public Func<bool> EnsureDrawableMesh;
        /// <summary>点が配置されるたびに呼ばれる（SubPanel更新用）</summary>
        public Action OnPointPlaced;
        /// <summary>GLギズモ描画用: 描画対象カメラのツールコンテキストを返す</summary>
        public Func<Camera, ToolContext> GetGizmoContext;

        // ================================================================
        // 設定公開API
        // ================================================================

        public AddFaceMode ModePublic    { get => _tool.ModePublic;    set => _tool.ModePublic = value; }
        public bool ContinuousLinePublic { get => _tool.ContinuousLinePublic; set => _tool.ContinuousLinePublic = value; }
        public int  PlacedPointCount     => _tool.PlacedPointCount;
        public int  RequiredPointsPublic => _tool.RequiredPointsPublic;
        public void ClearPointsPublic()  => _tool.ClearPointsPublic();
        public System.Collections.Generic.List<string> GetPointLabels() => _tool.GetPointLabels();
        public AddFaceTool.AddFacePreviewData GetPreviewData() => _tool.GetPreviewData();

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (EnsureDrawableMesh != null && !EnsureDrawableMesh()) return;
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
            OnPointPlaced?.Invoke();
        }
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (EnsureDrawableMesh != null && !EnsureDrawableMesh()) return;
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
        }
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
        }
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            EnrichCtxForHover(ctx);
            // UpdateHover に渡される screenPos は UIToolkit Y（Y=0上）= IMGUI Y（Y=0上）。
            // ドラッグ時は GPU Y（Y=0下）→ ToImgui で IMGUI Y に変換するが、
            // ホバー時は既に IMGUI Y なので ToImgui 不要。
            _tool.OnMouseDrag(ctx, screenPos, Vector2.zero);
        }

        /// <summary>Camera.onPostRenderから呼ぶ: GLギズモをRenderTextureに描画</summary>
        public void DrawGizmoForCamera(Camera cam)
        {
            var ctx = GetGizmoContext?.Invoke(cam);
            if (ctx == null) return;
            EnrichCtxForHover(ctx);
            _tool.DrawGizmo(ctx);
        }

        private void EnrichCtxForHover(ToolContext ctx)
        {
            var model = _project?.CurrentModel;
            ctx.Model            = model;
            ctx.SelectedVertices = model?.FirstSelectedMeshContext?.SelectedVertices;
            ctx.SelectionState   = model?.FirstSelectedMeshContext?.Selection;
            ctx.Repaint          = OnRepaint;
            // WorkPlane: カメラ注視点を原点とするカメラ平行平面
            var wp = new Poly_Ling.Context.WorkPlaneContext();
            wp.UpdateFromCamera(ctx.CameraPosition, ctx.CameraTarget);
            wp.Origin = ctx.CameraTarget;
            ctx.WorkPlane = wp;
        }
        public void Activate(ToolContext ctx)   { _tool.OnActivate(ctx); }
        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private MeshUndoController _undoController;

        /// <summary>
        /// GetToolContext の戻り値に必要なフィールドを全て補完して返す。
        /// AddFaceTool の OnMouseDown/Up はこのコンテキストを使う。
        /// </summary>
        private ToolContext GetEnrichedCtx()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return null;
            var model = _project?.CurrentModel;
            ctx.Model            = model;
            ctx.SelectedVertices = model?.FirstSelectedMeshContext?.SelectedVertices;
            ctx.SelectionState   = model?.FirstSelectedMeshContext?.Selection;
            ctx.UndoController   = _undoController;
            if (_undoController?.MeshUndoContext != null)
            {
                _undoController.MeshUndoContext.OnTopologyChanged = NotifyTopologyChanged;
                _undoController.MeshUndoContext.ParentModelContext = model;
            }
            // SyncMesh は面追加後のトポロジー再構築に置き換える
            ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();
            ctx.NotifyTopologyChanged = NotifyTopologyChanged;
            ctx.Repaint               = OnRepaint;
            // WorkPlane: カメラ注視点を原点とするカメラ平行平面を設定。
            // WorkPlaneがnullだと新規頂点がカメラから1.5*CameraDistance離れた位置に置かれる。
            var wp = new Poly_Ling.Context.WorkPlaneContext();
            wp.UpdateFromCamera(ctx.CameraPosition, ctx.CameraTarget);
            wp.Origin = ctx.CameraTarget;
            ctx.WorkPlane = wp;
            return ctx;
        }

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
