// SkinWeightPaintToolHandler.cs
// SkinWeightPaintTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class SkinWeightPaintToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly SkinWeightPaintTool _tool = new SkinWeightPaintTool();
        private          ProjectContext      _project;
        private          MeshUndoController  _undoController;
        private          CommandQueue        _commandQueue;

        // ================================================================
        // 外部コールバック
        // ================================================================

        public Func<ToolContext>              GetToolContext;
        public Action                         OnRepaint;
        public Action                         OnEnterTransformDragging;
        public Action                         OnExitTransformDragging;

        /// <summary>頂点位置変更後に UnityMesh + GPU バッファを同期するコールバック。</summary>
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>ブラシ円の表示更新（center: スクリーン座標Y=0下, radius: px）。</summary>
        public Action<Vector2, float, Color>  OnUpdateBrushCircle;

        /// <summary>ブラシ円を非表示にする。</summary>
        public Action                         OnHideBrushCircle;

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)     => _project       = project;
        public void SetUndoController(MeshUndoController ctrl) => _undoController = ctrl;
        public void SetCommandQueue(CommandQueue queue)    => _commandQueue   = queue;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp  (ctx, ToImgui(screenPos, ctx));
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
            UpdateBrushOverlay(ctx, screenPos);
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            UpdateBrushOverlay(ctx, screenPos);
        }

        // ================================================================
        // Activate / Deactivate
        // ================================================================

        public void OnActivate()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;
            ctx.Model          = _project?.CurrentModel;
            ctx.UndoController = _undoController;
            _tool.OnActivate(ctx);
        }

        public void OnDeactivate()
        {
            // ウェイト可視化のクリア
            var model = _project?.CurrentModel;
            if (model != null)
            {
                var mc = model.FirstDrawableMeshContext;
                if (mc?.UnityMesh != null)
                    mc.UnityMesh.colors = null;
            }
            SkinWeightPaintTool.SetVisualizationActive(false);
            OnHideBrushCircle?.Invoke();
            OnRepaint?.Invoke();
        }

        // ================================================================
        // 毎フレーム：ウェイト可視化の適用
        // ================================================================

        /// <summary>
        /// Update から毎フレーム呼ぶ。
        /// ウェイト可視化がアクティブなとき UnityMesh に頂点カラーを適用する。
        /// </summary>
        public void TickVisualization()
        {
            if (!SkinWeightPaintTool.IsVisualizationActive) return;
            var model = _project?.CurrentModel;
            if (model == null) return;

            var mc = model.FirstDrawableMeshContext;
            if (mc?.UnityMesh == null || mc.MeshObject == null) return;

            int targetBone = SkinWeightPaintTool.VisualizationTargetBone;
            SkinWeightPaintTool.ApplyVisualizationColors(mc.UnityMesh, mc.MeshObject, targetBone);
        }

        // ================================================================
        // ブラシ円更新
        // ================================================================

        private void UpdateBrushOverlay(ToolContext ctx, Vector2 screenPosYDown)
        {
            if (OnUpdateBrushCircle == null) return;
            var panel = SkinWeightPaintTool.ActivePanel;
            float radius = panel?.CurrentBrushRadius ?? 0.3f;
            float screenR = EstimateBrushScreenRadius(ctx, radius);
            Color col = GetBrushColor(panel);
            OnUpdateBrushCircle.Invoke(screenPosYDown, screenR, col);
        }

        private float EstimateBrushScreenRadius(ToolContext ctx, float worldRadius)
        {
            Vector3 testPoint = ctx.CameraTarget;
            Vector3 camRight  = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;
            Vector3 offsetPoint = testPoint + camRight * worldRadius;

            Vector2 sp1 = ctx.WorldToScreenPos(testPoint,   ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(offsetPoint, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            float panelH = ctx.PreviewRect.height;
            sp1.y = panelH - sp1.y;
            sp2.y = panelH - sp2.y;
            return Mathf.Max(Vector2.Distance(sp1, sp2), 10f);
        }

        private static Color GetBrushColor(Poly_Ling.UI.ISkinWeightPaintPanel panel)
        {
            if (panel == null) return new Color(0.6f, 0.6f, 0.6f, 0.5f);
            switch (panel.CurrentPaintMode)
            {
                case Poly_Ling.UI.SkinWeightPaintMode.Replace: return new Color(0.3f, 0.7f, 1.0f, 0.6f);
                case Poly_Ling.UI.SkinWeightPaintMode.Add:     return new Color(0.3f, 1.0f, 0.5f, 0.6f);
                case Poly_Ling.UI.SkinWeightPaintMode.Scale:   return new Color(1.0f, 0.8f, 0.3f, 0.6f);
                case Poly_Ling.UI.SkinWeightPaintMode.Smooth:  return new Color(0.8f, 0.5f, 1.0f, 0.6f);
                default: return new Color(1f, 1f, 1f, 0.5f);
            }
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private ToolContext BuildToolContext(ModifierKeys mods, Vector2 screenPosYDown)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;

            var baseCtx = GetToolContext?.Invoke() ?? new ToolContext();
            baseCtx.Model              = model;
            baseCtx.UndoController     = _undoController;
            baseCtx.CommandQueue       = _commandQueue;
            baseCtx.Repaint            = OnRepaint;
            baseCtx.EnterTransformDragging = OnEnterTransformDragging;
            baseCtx.ExitTransformDragging  = OnExitTransformDragging;
            baseCtx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(screenPosYDown, baseCtx),
            };

            // SyncMesh: 頂点位置変更後に UnityMesh + GPU バッファを同期
            baseCtx.SyncMesh = () =>
            {
                var mc = model.FirstDrawableMeshContext;
                if (mc != null) OnSyncMeshPositions?.Invoke(mc);
            };

            return baseCtx;
        }

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
