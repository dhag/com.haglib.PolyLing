// ScaleToolHandler.cs
// ScaleTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class ScaleToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly ScaleTool _tool = new ScaleTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        // ================================================================
        // 設定公開API
        // ================================================================

        public float ScaleX        { get => _tool.ScaleX;       set { _tool.ScaleX = value; } }
        public float ScaleY        { get => _tool.ScaleY;       set { _tool.ScaleY = value; } }
        public float ScaleZ        { get => _tool.ScaleZ;       set { _tool.ScaleZ = value; } }
        public bool  UniformScale  { get => _tool.UniformScale; set => _tool.UniformScale = value; }
        public bool  UseOriginPivot{ get => _tool.UseOriginPivot; set => _tool.UseOriginPivot = value; }
        public int   GetTotalAffectedCount() => _tool.GetTotalAffectedCountPublic();
        public void  BeginSliderDrag() => _tool.BeginSliderDrag();
        public void  EndSliderDrag()   => _tool.EndSliderDrag();
        public void  Revert()          => _tool.RevertPublic();

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) {}
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) {}
        public void UpdateHover(Vector2 screenPos, ToolContext ctx) {}
        public void Activate(ToolContext ctx)   { _tool.SetContextPublic(ctx); }
        public void Deactivate(ToolContext ctx) {}

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private MeshUndoController _undoController;

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
                foreach (int idx in model.SelectedMeshIndices)
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
