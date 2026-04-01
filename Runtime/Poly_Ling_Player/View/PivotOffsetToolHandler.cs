// PivotOffsetToolHandler.cs
// PivotOffsetTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    /// <summary>
    /// PivotOffsetTool を Player 入力に橋渡しする。
    /// ピボット移動（全頂点が逆方向に移動）をビューポートのドラッグ操作で行う。
    /// ギズモ座標は TryGetGizmoScreenPositions で取得し UIToolkit オーバーレイに渡す。
    /// </summary>
    public class PivotOffsetToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly PivotOffsetTool _tool = new PivotOffsetTool();
        private          ProjectContext  _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>  GetToolContext;
        public Action             OnRepaint;
        public Action             OnEnterTransformDragging;
        public Action             OnExitTransformDragging;

        /// <summary>BoneTransform / ComputeWorldMatrices / UpdateTransform を呼ぶコールバック。</summary>
        public Action OnSyncBoneTransforms;

        /// <summary>
        /// 頂点位置変更後に UnityMesh.vertices + GPU バッファを同期するコールバック。
        /// 引数は変更された MeshContext。
        /// </summary>
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

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
            var ctx = BuildToolContext(mods);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp  (ctx, ToImgui(screenPos, ctx));
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
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods);
            if (ctx == null) return;
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            _tool.OnMouseDrag(ctx, screenPos, Vector2.zero);
        }

        // ================================================================
        // ギズモスクリーン座標取得
        // PivotOffsetTool.DrawAxisGizmo と同じ計算をエディタ描画APIなしで行う。
        // ================================================================

        /// <summary>
        /// ギズモの X/Y/Z 軸エンド座標（スクリーン、Y=0 が上）と
        /// ホバー中の軸種別を返す。
        /// 選択なし・ctx 無効の場合は false を返す。
        /// </summary>
        public bool TryGetGizmoScreenPositions(
            ToolContext ctx,
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            origin = xEnd = yEnd = zEnd = Vector2.zero;
            hoveredAxis = AxisGizmo.AxisType.None;

            var builtCtx = BuildToolContext(default);
            if (builtCtx == null) return false;

            var mc = builtCtx.FirstSelectedMeshContext;
            if (mc == null) return false;

            var wm = mc.WorldMatrix;
            Vector3 pivotWorld = new Vector3(wm.m03, wm.m13, wm.m23);
            origin = builtCtx.WorldToScreenPos(pivotWorld, builtCtx.PreviewRect,
                                               builtCtx.CameraPosition, builtCtx.CameraTarget);

            xEnd = CalcAxisEnd(builtCtx, pivotWorld, origin,
                               new Vector3(wm.m00, wm.m10, wm.m20).normalized);
            yEnd = CalcAxisEnd(builtCtx, pivotWorld, origin,
                               new Vector3(wm.m01, wm.m11, wm.m21).normalized);
            zEnd = CalcAxisEnd(builtCtx, pivotWorld, origin,
                               new Vector3(wm.m02, wm.m12, wm.m22).normalized);

            // PivotOffsetTool は AxisType を直接公開していないため None を返す
            // （ホバーはツール内部で管理）
            hoveredAxis = AxisGizmo.AxisType.None;
            return true;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private const float ScreenAxisLength = 60f;

        private Vector2 CalcAxisEnd(ToolContext ctx, Vector3 pivotWorld,
                                    Vector2 originScreen, Vector3 axisWorldDir)
        {
            Vector3 axisEnd = pivotWorld + axisWorldDir * 0.1f;
            Vector2 axisEndScreen = ctx.WorldToScreenPos(axisEnd, ctx.PreviewRect,
                                                         ctx.CameraPosition, ctx.CameraTarget);
            Vector2 dir = (axisEndScreen - originScreen).normalized;
            if (dir.sqrMagnitude < 0.001f) dir = new Vector2(1, 0);
            return originScreen + dir * ScreenAxisLength;
        }

        private ToolContext BuildToolContext(ModifierKeys mods)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;

            var baseCtx = GetToolContext?.Invoke() ?? new ToolContext();

            baseCtx.Model              = model;
            baseCtx.UndoController     = _undoController;
            baseCtx.SyncBoneTransforms = OnSyncBoneTransforms;
            baseCtx.Repaint            = OnRepaint;
            baseCtx.EnterTransformDragging = OnEnterTransformDragging;
            baseCtx.ExitTransformDragging  = OnExitTransformDragging;
            baseCtx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld   = mods.Shift,
                IsControlHeld = mods.Ctrl,
            };

            // 頂点位置変更後に UnityMesh + GPU バッファを同期する
            baseCtx.SyncMesh = () =>
            {
                var mc = model.FirstSelectedMeshContext;
                if (mc != null) OnSyncMeshPositions?.Invoke(mc);
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
