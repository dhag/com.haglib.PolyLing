// SculptToolHandler.cs
// SculptTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class SculptToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly SculptTool   _tool = new SculptTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>  GetToolContext;
        public Action             OnRepaint;
        public Action             OnEnterTransformDragging;
        public Action             OnExitTransformDragging;

        /// <summary>頂点位置変更後に UnityMesh + GPU バッファを同期するコールバック。</summary>
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>
        /// ブラシ円の表示更新コールバック（center: スクリーン座標Y=0下, radius: px）。
        /// </summary>
        public Action<Vector2, float> OnUpdateBrushCircle;

        /// <summary>ブラシ円を非表示にするコールバック。</summary>
        public Action OnHideBrushCircle;

        // ================================================================
        // ブラシ設定公開
        // ================================================================

        public SculptMode Mode
        {
            get => ((SculptSettings)_tool.Settings)?.Mode ?? SculptMode.Draw;
            set { if (_tool.Settings is SculptSettings s) s.Mode = value; }
        }

        public float BrushRadius
        {
            get => ((SculptSettings)_tool.Settings)?.BrushRadius ?? 0.5f;
            set { if (_tool.Settings is SculptSettings s) s.BrushRadius = Mathf.Clamp(value, SculptSettings.MIN_BRUSH_RADIUS, SculptSettings.MAX_BRUSH_RADIUS); }
        }

        public float Strength
        {
            get => ((SculptSettings)_tool.Settings)?.Strength ?? 0.1f;
            set { if (_tool.Settings is SculptSettings s) s.Strength = Mathf.Clamp(value, SculptSettings.MIN_STRENGTH, SculptSettings.MAX_STRENGTH); }
        }

        public bool Invert
        {
            get => ((SculptSettings)_tool.Settings)?.Invert ?? false;
            set { if (_tool.Settings is SculptSettings s) s.Invert = value; }
        }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) => _undoController = ctrl;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp  (ctx, ToImgui(screenPos, ctx));
            OnHideBrushCircle?.Invoke();
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
            UpdateBrushCircleOverlay(ctx, screenPos);
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
            OnHideBrushCircle?.Invoke();
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            UpdateBrushCircleOverlay(ctx, screenPos);
        }

        // ================================================================
        // ブラシ円更新
        // ================================================================

        private void UpdateBrushCircleOverlay(ToolContext ctx, Vector2 screenPosYDown)
        {
            if (OnUpdateBrushCircle == null) return;
            float radius = EstimateBrushScreenRadius(ctx);
            OnUpdateBrushCircle.Invoke(screenPosYDown, radius);
        }

        private float EstimateBrushScreenRadius(ToolContext ctx)
        {
            Vector3 testPoint = ctx.CameraTarget;
            Vector3 camRight  = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;
            Vector3 offsetPoint = testPoint + camRight * BrushRadius;

            Vector2 sp1 = ctx.WorldToScreenPos(testPoint,    ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(offsetPoint,  ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            // WorldToScreenPos は Y=0 が上（IMGUI 系）→ Y=0 が下に変換して返す
            float panelH   = ctx.PreviewRect.height;
            sp1.y = panelH - sp1.y;
            sp2.y = panelH - sp2.y;

            return Mathf.Max(Vector2.Distance(sp1, sp2), 10f);
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
            baseCtx.Repaint            = OnRepaint;
            baseCtx.EnterTransformDragging = OnEnterTransformDragging;
            baseCtx.ExitTransformDragging  = OnExitTransformDragging;
            baseCtx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(screenPosYDown, baseCtx),
            };

            // 頂点位置変更後に UnityMesh + GPU バッファを同期
            baseCtx.SyncMesh = () =>
            {
                // 全選択メッシュを同期（SculptToolは複数メッシュ対応）
                foreach (int idx in model.SelectedMeshIndices)
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc != null) OnSyncMeshPositions?.Invoke(mc);
                }
            };

            return baseCtx;
        }

        private MeshUndoController _undoController;

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
