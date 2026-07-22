// PivotOffsetToolHandler.cs
// 「原点だけ移動」を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// 内部は ObjectMoveTool(OriginOnly=true) へ委譲する（案1: ピボットモードの再ルーティング）。
// 表示ラベルは「原点だけ移動」。内部識別子(Pivot 系)は据え置き。
// モード配線(入力/ギズモ/ホバー/パネル/ボタン)は Core 側で不変。

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 「原点だけ移動」(OriginOnly)。原点(BoneTransform.Position)だけを動かし、
    /// 対象メッシュの見た目と直接の子は据え置く。内部は ObjectMoveTool へ委譲。
    /// ギズモ座標/当たり判定は ObjectMoveTool の同一 _axisGizmo を用いるため一致する。
    /// </summary>
    public class PivotOffsetToolHandler : IPlayerToolHandler
    {
        // ObjectMoveTool を専用設定(OriginOnly=true, 子は据え置き, ピック無効=ギズモ専用)で保持
        private readonly ObjectMoveTool _tool = new ObjectMoveTool();

        private ProjectContext    _project;
        private MeshUndoController _undoController;

        public PivotOffsetToolHandler()
        {
            _tool.SetSettings(new ObjectMoveSettings
            {
                OriginOnly        = true,
                MoveWithChildren  = false,   // 直接の子は据え置き(world 固定でローカル逆算)
                PickBones         = false,   // ギズモ専用(空クリックでのピックはしない)
                PickMeshesNoSkin  = false,
                PickMeshesSkinned = false,
            });
        }

        // ================================================================
        // 外部コールバック(Viewer から設定) ─ Core は本クラスの公開面に依存するため維持
        // ================================================================

        public Func<ToolContext>  GetToolContext;
        public Action             OnRepaint;
        public Action             OnEnterTransformDragging;
        public Action             OnExitTransformDragging;
        public Action             OnSyncBoneTransforms;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

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
            _tool.UpdateHoverOnly(ctx, ToImgui(screenPos, ctx));
        }

        // ================================================================
        // ギズモスクリーン座標: ObjectMoveTool の _axisGizmo をそのまま使う
        // (表示と当たり判定が同一計算になり、ズレて反応しない不具合を避ける)
        // ================================================================

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

            return _tool.TryGetGizmoScreenPositions(
                builtCtx, out origin, out xEnd, out yEnd, out zEnd, out hoveredAxis);
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private ToolContext BuildToolContext(ModifierKeys mods)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;

            var baseCtx = GetToolContext?.Invoke() ?? new ToolContext();

            baseCtx.Model                  = model;
            baseCtx.UndoController         = _undoController;
            baseCtx.SyncBoneTransforms     = OnSyncBoneTransforms;
            baseCtx.Repaint                = OnRepaint;
            baseCtx.EnterTransformDragging = OnEnterTransformDragging;
            baseCtx.ExitTransformDragging  = OnExitTransformDragging;
            baseCtx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld   = mods.Shift,
                IsControlHeld = mods.Ctrl,
            };

            // OriginOnly は頂点を書き換えるので GPU 同期が必須
            baseCtx.SyncMesh = () =>
            {
                var mc = model.FirstSelectedMeshContext;
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
