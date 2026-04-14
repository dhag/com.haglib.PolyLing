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


        /// <summary>ブラシ半径が変更されたときに呼ばれるコールバック（UIパネル更新用）。</summary>
        public Action<float> OnRadiusChanged;

        /// <summary>
        /// スカルプトブラシ用ヒットテスト。PlayerViewportManager.GetBrushHit を設定する。
        /// Normal モード時は HoverVertexIndex を、TransformDragging 時は _screenPositions から直接検索する。
        /// </summary>
        public Func<Vector2, float, PlayerHitResult> GetBrushHit;

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
            set
            {
                if (_tool.Settings is SculptSettings s)
                    s.BrushRadius = Mathf.Clamp(value, s.MinBrushRadius, s.MaxBrushRadius);
            }
        }

        public float Strength
        {
            get => ((SculptSettings)_tool.Settings)?.Strength ?? 0.1f;
            set { if (_tool.Settings is SculptSettings s) s.Strength = Mathf.Clamp(value, s.MinStrength, s.MaxStrength); }
        }

        public float MinStrength
        {
            get => ((SculptSettings)_tool.Settings)?.MinStrength ?? 0.01f;
            set { if (_tool.Settings is SculptSettings s) s.MinStrength = Mathf.Max(0.001f, value); }
        }

        public float MaxStrength
        {
            get => ((SculptSettings)_tool.Settings)?.MaxStrength ?? 0.05f;
            set { if (_tool.Settings is SculptSettings s) s.MaxStrength = Mathf.Max(MinStrength + 0.001f, value); }
        }

        public bool Invert
        {
            get => ((SculptSettings)_tool.Settings)?.Invert ?? false;
            set { if (_tool.Settings is SculptSettings s) s.Invert = value; }
        }

        public FalloffType Falloff
        {
            get => ((SculptSettings)_tool.Settings)?.Falloff ?? FalloffType.Gaussian;
            set { if (_tool.Settings is SculptSettings s) s.Falloff = value; }
        }

        public float MinBrushRadius
        {
            get => ((SculptSettings)_tool.Settings)?.MinBrushRadius ?? 0.05f;
            set { if (_tool.Settings is SculptSettings s) s.MinBrushRadius = Mathf.Max(0.001f, value); }
        }

        public float MaxBrushRadius
        {
            get => ((SculptSettings)_tool.Settings)?.MaxBrushRadius ?? 1.0f;
            set { if (_tool.Settings is SculptSettings s) s.MaxBrushRadius = Mathf.Max(MinBrushRadius + 0.001f, value); }
        }

        // ================================================================
        // ドラッグによる半径指定モード
        // ================================================================

        /// <summary>
        /// true の間、次のドラッグ操作はスカルプトではなく
        /// ブラシ半径の設定として扱われる。ドラッグ終了後に自動的に false に戻る。
        /// </summary>
        public bool IsRadiusDragMode { get; set; } = false;

        private Vector2 _radiusDragStartPos;
        private bool    _inRadiusDrag;

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
            if (IsRadiusDragMode) { IsRadiusDragMode = false; return; }
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp  (ctx, ToImgui(screenPos, ctx));
            OnHideBrushCircle?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (IsRadiusDragMode)
            {
                _radiusDragStartPos = screenPos;
                _inRadiusDrag       = true;
                return;
            }
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (_inRadiusDrag)
            {
                var ctx = BuildToolContext(mods, screenPos);
                if (ctx != null)
                {
                    float screenDist = Vector2.Distance(screenPos, _radiusDragStartPos);
                    float newRadius  = ScreenDistToWorldRadius(screenDist, ctx);
                    if (_tool.Settings is SculptSettings s)
                        newRadius = Mathf.Clamp(newRadius, s.MinBrushRadius, s.MaxBrushRadius);
                    BrushRadius = newRadius;
                    OnRadiusChanged?.Invoke(newRadius);
                    // ドラッグ開始位置を中心にブラシ円をプレビュー
                    float previewPx = ScreenRadiusFromWorldRadius(newRadius, ctx);
                    OnUpdateBrushCircle?.Invoke(_radiusDragStartPos, previewPx);
                }
                return;
            }

            var ctx2 = BuildToolContext(mods, screenPos);
            if (ctx2 == null) return;
            _tool.OnMouseDrag(ctx2, ToImgui(screenPos, ctx2), delta);
            UpdateBrushCircleOverlay(ctx2, screenPos);
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            if (_inRadiusDrag)
            {
                _inRadiusDrag       = false;
                IsRadiusDragMode    = false;
                OnHideBrushCircle?.Invoke();
                return;
            }

            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
            OnHideBrushCircle?.Invoke();
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            // OnPointerHover は UIToolkit 座標（Y=0 が上）で渡される。
            // UpdateBrushCircleOverlay / ShowBrushCircle は Y=0 が下を期待するため反転する。
            // ドラッグ中は PlayerViewportPanel.ToViewportCoord で反転済みなので一致する。
            float panelH = ctx.PreviewRect.height;
            var screenPosYDown = new Vector2(screenPos.x, panelH - screenPos.y);
            UpdateBrushCircleOverlay(ctx, screenPosYDown);
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
            return ScreenRadiusFromWorldRadius(BrushRadius, ctx);
        }

        private float ScreenRadiusFromWorldRadius(float worldRadius, ToolContext ctx)
        {
            Vector3 testPoint = ctx.CameraTarget;
            Vector3 camRight  = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;
            Vector3 offsetPoint = testPoint + camRight * worldRadius;

            Vector2 sp1 = ctx.WorldToScreenPos(testPoint,    ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(offsetPoint,  ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            float panelH = ctx.PreviewRect.height;
            sp1.y = panelH - sp1.y;
            sp2.y = panelH - sp2.y;

            return Mathf.Max(Vector2.Distance(sp1, sp2), 10f);
        }

        private float ScreenDistToWorldRadius(float screenDist, ToolContext ctx)
        {
            Vector3 target   = ctx.CameraTarget;
            Vector3 camRight = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;

            Vector2 sp1 = ctx.WorldToScreenPos(target,          ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(target + camRight, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            float pxPerUnit = Vector2.Distance(sp1, sp2);
            if (pxPerUnit < 0.001f) return screenDist * 0.01f;
            return screenDist / pxPerUnit;
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

            baseCtx.SyncMesh = () =>
            {
                foreach (int idx in model.SelectedDrawableMeshIndices)
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc != null) OnSyncMeshPositions?.Invoke(mc);
                }
            };

            // ブラシ中心算出用: Normal モード時は HoverVertexIndex、TransformDragging 時は直接検索
            var capturedScreenPos = screenPosYDown;
            baseCtx.GetHoverWorldPosition = () =>
            {
                if (GetBrushHit == null) return null;
                var hit = GetBrushHit(capturedScreenPos, 12f);
                if (!hit.HasHit) return null;
                var mc = model.GetMeshContext(hit.MeshIndex);
                if (mc?.MeshObject == null) return null;
                if (hit.VertexIndex < 0 || hit.VertexIndex >= mc.MeshObject.VertexCount) return null;
                return (Vector3?)mc.MeshObject.Vertices[hit.VertexIndex].Position;
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
