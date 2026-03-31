// ObjectMoveToolHandler.cs
// ObjectMoveTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    /// <summary>
    /// <see cref="ObjectMoveTool"/> を Player 入力に橋渡しする。
    /// <para>
    /// IPlayerToolHandler の左クリック/ドラッグを ObjectMoveTool.OnMouseDown/Drag/Up に変換する。
    /// ObjectMoveTool が必要とする ToolContext フィールド（Model・UndoController・
    /// SyncBoneTransforms 等）を PlayerToolContext 経由で補完する。
    /// </para>
    /// </summary>
    public class ObjectMoveToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly ObjectMoveTool    _tool = new ObjectMoveTool();
        private          ProjectContext    _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        /// <summary>毎回最新の ToolContext を返すコールバック。</summary>
        public Func<ToolContext> GetToolContext;

        public Action OnRepaint;
        public Action OnEnterTransformDragging;
        public Action OnExitTransformDragging;
        public Action OnMeshSelectionChanged;

        /// <summary>ボーン位置変更後に呼ぶ同期コールバック（NotifyPanels 等）。</summary>
        public Action OnSyncBoneTransforms;

        // ギズモ描画用スクリーン座標取得
        public Func<PlayerViewportPanel.GizmoData> TryGetGizmoData;

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods);
            if (ctx == null) return;
            // screenPos はY反転、delta はY反転不要（差分なので符号が打ち消し合う）
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods);
            if (ctx == null) return;
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }

        // ================================================================
        // ギズモスクリーン座標取得
        // ================================================================

        public bool TryGetGizmoScreenPositions(
            ToolContext ctx,
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            var builtCtx = BuildToolContext(default);
            if (builtCtx == null)
            {
                origin = xEnd = yEnd = zEnd = Vector2.zero;
                hoveredAxis = AxisGizmo.AxisType.None;
                return false;
            }
            return _tool.TryGetGizmoScreenPositions(
                builtCtx, out origin, out xEnd, out yEnd, out zEnd, out hoveredAxis);
        }

        public bool GetPivotScreenPos(out Vector2 pivotScreen)
        {
            var ctx = BuildToolContext(default);
            return _tool.GetPivotScreenPos(ctx, out pivotScreen);
        }

        public bool TryGetGizmoPivotPositions(
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            var ctx = BuildToolContext(default);
            if (ctx == null)
            {
                origin = xEnd = yEnd = zEnd = Vector2.zero;
                hoveredAxis = AxisGizmo.AxisType.None;
                return false;
            }
            return _tool.TryGetGizmoPivotPositions(
                ctx, out origin, out xEnd, out yEnd, out zEnd, out hoveredAxis);
        }

        // ================================================================
        // ギズモ更新（毎フレーム呼ぶ）
        // ================================================================

        /// <summary>
        /// ギズモを描画するためのスクリーン座標を取得する。
        /// PlayerViewportPanel.GizmoData を返す。
        /// Viewer の UpdateGizmoOverlay 相当。
        /// </summary>
        public bool TryGetGizmoScreenPositions(
            Vector2 mouseScreenPos,
            out PlayerViewportPanel.GizmoData data)
        {
            data = default;
            var ctx = BuildToolContext(default);
            if (ctx == null) return false;

            _tool.DrawGizmo(ctx);
            return false; // ギズモ座標の取り出しは AxisGizmo 公開プロパティ経由で行う
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            _tool.OnMouseDrag(ctx, screenPos, Vector2.zero);
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private ToolContext BuildToolContext(ModifierKeys mods)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;

            // GetToolContext からカメラ・投影情報を取得
            var baseCtx = GetToolContext?.Invoke();

            // baseCtx が null でも Model さえあれば続行できる
            var ctx = baseCtx ?? new ToolContext();

            ctx.Model                  = model;
            ctx.UndoController         = _undoController;
            ctx.SyncBoneTransforms     = OnSyncBoneTransforms;
            ctx.Repaint                = OnRepaint;
            ctx.EnterTransformDragging = OnEnterTransformDragging;
            ctx.ExitTransformDragging  = OnExitTransformDragging;
            ctx.OnMeshSelectionChanged = OnMeshSelectionChanged;
            ctx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld   = mods.Shift,
                IsControlHeld = mods.Ctrl,
            };
            return ctx;
        }

        // ================================================================
        // UndoController（Viewer から設定）
        // ================================================================

        private MeshUndoController _undoController;

        public void SetUndoController(MeshUndoController ctrl) =>
            _undoController = ctrl;

        // ================================================================
        // Y座標変換（PlayerViewportPanelはY=0下、AxisGizmoはY=0上）
        // ================================================================

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
