// PlanarizeAlongBonesToolHandler.cs
// PlanarizeAlongBonesTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class PlanarizeAlongBonesToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly PlanarizeAlongBonesTool _tool    = new PlanarizeAlongBonesTool();
        private          ProjectContext          _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>                          GetToolContext;
        public Action                                     OnRepaint;
        public Action<Poly_Ling.Data.MeshContext>         OnSyncMeshPositions;

        // ================================================================
        // 設定公開 API
        // ================================================================

        public int               BoneIndexA { get => _tool.BoneIndexA; set => _tool.BoneIndexA = value; }
        public int               BoneIndexB { get => _tool.BoneIndexB; set => _tool.BoneIndexB = value; }
        public PlanePlacementMode PlaneMode  { get => _tool.PlaneMode;  set => _tool.PlaneMode  = value; }
        public float             Blend       { get => _tool.Blend;      set => _tool.Blend      = value; }

        public string[] BoneNames         => _tool.BoneNames;
        public int      SelectedVertexCount => _tool.SelectedVertexCount;

        public Vector3 GetBoneWorldPosition(int listIndex) => _tool.GetBoneWorldPosition(listIndex);
        public void    TriggerPlanarize()                  => _tool.TriggerPlanarize();
        public void    RebuildBoneList()                   => _tool.RebuildBoneListIfNeeded();

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)       => _project = project;
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
                ctx.Model            = model;
                ctx.SelectedVertices = model?.FirstSelectedMeshContext?.SelectedVertices;
                ctx.SelectionState   = model?.FirstSelectedMeshContext?.Selection;
                ctx.UndoController   = _undoController;
                ctx.CommandQueue     = _commandQueue;
                ctx.Repaint          = OnRepaint;
                ctx.SyncMesh = () =>
                {
                    if (model == null) return;
                    foreach (int idx in model.SelectedDrawableMeshIndices)
                    {
                        var mc = model.GetMeshContext(idx);
                        if (mc != null) OnSyncMeshPositions?.Invoke(mc);
                    }
                };
            }
            _tool.OnActivate(ctx);
        }
        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private MeshUndoController _undoController;
        private CommandQueue        _commandQueue;

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
