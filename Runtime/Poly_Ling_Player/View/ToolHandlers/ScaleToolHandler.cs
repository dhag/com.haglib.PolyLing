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
        public Func<float>       GetPanelHeight;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;
        public Action                                     OnApplyCompleted;
        public Action                                     NotifyTopologyChanged;

        // ビューポート・スケールギズモ（AxisGizmo 再利用）
        private readonly AxisGizmo _axisGizmo = new AxisGizmo();
        private AxisGizmo.AxisType _gizmoHoverAxis = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _gizmoDragAxis  = AxisGizmo.AxisType.None;
        private Vector2 _gizmoDragStartScreen;
        private Vector2 _gizmoAxisScreenDir;
        private float   _gizmoStartScaleX = 1f, _gizmoStartScaleY = 1f, _gizmoStartScaleZ = 1f;
        private const float GizmoScaleSensitivity = 0.01f;

        // ================================================================
        // 設定公開API
        // ================================================================

        public float ScaleX        { get => _tool.ScaleX;       set { _tool.ScaleX = value; } }
        public float ScaleY        { get => _tool.ScaleY;       set { _tool.ScaleY = value; } }
        public float ScaleZ        { get => _tool.ScaleZ;       set { _tool.ScaleZ = value; } }
        public bool  UniformScale  { get => _tool.UniformScale; set => _tool.UniformScale = value; }
        public bool  UseOriginPivot{ get => _tool.UseOriginPivot; set => _tool.UseOriginPivot = value; }
        public bool         UseMagnet          { get => _tool.UseMagnet;          set => _tool.UseMagnet = value; }
        public float        MagnetRadius       { get => _tool.MagnetRadius;       set => _tool.MagnetRadius = value; }
        public Poly_Ling.Tools.FalloffType  MagnetFalloff      { get => _tool.MagnetFalloff;      set => _tool.MagnetFalloff = value; }
        public Poly_Ling.Tools.DistanceMode MagnetDistanceMode { get => _tool.MagnetDistanceMode; set => _tool.MagnetDistanceMode = value; }
        public float ScaleAxisX { get => _tool.ScaleAxisX; set => _tool.ScaleAxisX = value; }
        public float ScaleAxisY { get => _tool.ScaleAxisY; set => _tool.ScaleAxisY = value; }
        public float ScaleAxisZ { get => _tool.ScaleAxisZ; set => _tool.ScaleAxisZ = value; }
        public int   GetTotalAffectedCount() => _tool.GetTotalAffectedCountPublic();
        public void  BeginSliderDrag() => _tool.BeginSliderDrag();
        public void  EndSliderDrag()   { _tool.EndSliderDrag(); OnApplyCompleted?.Invoke(); }
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

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null || _tool.GetTotalAffectedCountPublic() == 0)
            {
                _gizmoHoverAxis = AxisGizmo.AxisType.None; return;
            }
            _axisGizmo.Center = _tool.PivotPublic;
            _gizmoHoverAxis = _axisGizmo.FindAxisAtScreenPos(ToImgui(screenPos), ctx);
            OnRepaint?.Invoke();
        }

        // ── ビューポート・スケールギズモ ──────────────────────────────────

        /// <summary>ギズモのスクリーン座標を返す（UpdateGizmoOverlay 用）。</summary>
        public bool TryGetGizmoScreenPositions(ToolContext ctx,
            out Vector2 origin, out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            origin = xEnd = yEnd = zEnd = Vector2.zero;
            hoveredAxis = AxisGizmo.AxisType.None;
            if (ctx == null || _tool.GetTotalAffectedCountPublic() == 0) return false;
            _axisGizmo.Center       = _tool.PivotPublic;
            _axisGizmo.HoveredAxis  = _gizmoHoverAxis;
            _axisGizmo.DraggingAxis = _gizmoDragAxis;
            _axisGizmo.GetScreenPositions(ctx, out origin, out xEnd, out yEnd, out zEnd);
            hoveredAxis = _gizmoHoverAxis;
            return true;
        }

        /// <summary>ギズモヒットテスト（MoveToolHandler.GizmoHitTestOverride 用）。</summary>
        public bool GizmoHitTest(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null || _tool.GetTotalAffectedCountPublic() == 0) return false;
            _axisGizmo.Center = _tool.PivotPublic;
            var axis = _axisGizmo.FindAxisAtScreenPos(ToImgui(screenPos), ctx);
            if (axis == AxisGizmo.AxisType.None) return false;
            _gizmoDragAxis        = axis;
            _gizmoDragStartScreen = screenPos;
            return true;
        }

        /// <summary>ドラッグセッション開始（OnDragStartExtra 用）。true でツール操作へ。</summary>
        public bool BeginGizmoDrag()
        {
            if (_gizmoDragAxis == AxisGizmo.AxisType.None) return false;
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) { _gizmoDragAxis = AxisGizmo.AxisType.None; return false; }
            _axisGizmo.Center = _tool.PivotPublic;
            _axisGizmo.GetScreenPositions(ctx, out var o, out var xe, out var ye, out var ze);
            Vector2 axisEnd = _gizmoDragAxis == AxisGizmo.AxisType.X ? xe
                            : _gizmoDragAxis == AxisGizmo.AxisType.Y ? ye : ze;
            Vector2 dir = axisEnd - o;
            // GetScreenPositions は imgui(Y上)。スクリーン(Y下)基準へ変換して符号を合わせる。
            _gizmoAxisScreenDir = dir.sqrMagnitude > 1e-4f ? new Vector2(dir.x, -dir.y).normalized : Vector2.right;
            _gizmoStartScaleX = _tool.ScaleX; _gizmoStartScaleY = _tool.ScaleY; _gizmoStartScaleZ = _tool.ScaleZ;
            _tool.BeginSliderDrag();
            return true;
        }

        /// <summary>ドラッグ中のスケール更新（OnToolDragExtra 用）。</summary>
        public void GizmoDrag(Vector2 screenPos)
        {
            if (_gizmoDragAxis == AxisGizmo.AxisType.None) return;
            Vector2 d = screenPos - _gizmoDragStartScreen;
            float along = _gizmoDragAxis == AxisGizmo.AxisType.Center
                        ? (d.x - d.y)
                        : Vector2.Dot(d, _gizmoAxisScreenDir);
            float factor = Mathf.Max(0.01f, 1f + along * GizmoScaleSensitivity);

            switch (_gizmoDragAxis)
            {
                case AxisGizmo.AxisType.Center:
                    _tool.UniformScale = true;
                    _tool.ScaleX = _gizmoStartScaleX * factor;
                    _tool.ScaleY = _gizmoStartScaleY * factor;
                    _tool.ScaleZ = _gizmoStartScaleZ * factor;
                    break;
                case AxisGizmo.AxisType.X: _tool.ScaleX = _gizmoStartScaleX * factor; break;
                case AxisGizmo.AxisType.Y: _tool.ScaleY = _gizmoStartScaleY * factor; break;
                case AxisGizmo.AxisType.Z: _tool.ScaleZ = _gizmoStartScaleZ * factor; break;
            }
            OnRepaint?.Invoke();
        }

        /// <summary>ドラッグ確定（OnToolDragEndExtra 用）。</summary>
        public void EndGizmoDrag()
        {
            if (_gizmoDragAxis == AxisGizmo.AxisType.None) return;
            _gizmoDragAxis = AxisGizmo.AxisType.None;
            EndSliderDrag();
        }

        private Vector2 ToImgui(Vector2 screenPosYDown)
        {
            float h = GetPanelHeight?.Invoke() ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }

        public void Activate(ToolContext ctx)
        {
            if (ctx != null)
            {
                var model = _project?.CurrentModel;
                ctx.Model            = model;
                var mc0  = model?.FirstDrawableMeshContext;
                ctx.SelectedVertices = mc0?.SelectedVertices;
                ctx.SelectionState   = mc0?.Selection;
                ctx.UndoController   = _undoController;
                ctx.Repaint          = OnRepaint;
                if (_undoController?.MeshUndoContext != null && model != null)
                    _undoController.MeshUndoContext.ParentModelContext = model;
                ctx.SyncMesh = () =>
                {
                    if (model == null) return;
                    foreach (int idx in model.SelectedDrawableMeshIndices)
                    {
                        var mc = model.GetMeshContext(idx);
                        if (mc?.MeshObject != null) { mc.MeshObject.InvalidatePositionCache(); OnSyncMeshPositions?.Invoke(mc); }
                    }
                };
                ctx.SyncMeshPositionsOnly = ctx.SyncMesh;
            }
            _tool.SetContextPublic(ctx);
        }
        public void Deactivate(ToolContext ctx) {}

        // ================================================================
        // 内部ヘルパー
        // ================================================================


        private ToolContext GetEnrichedCtx()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return null;
            var model = _project?.CurrentModel;
            ctx.Model            = model;
            ctx.SelectedVertices = model?.FirstSelectedMeshContext?.SelectedVertices;
            ctx.SelectionState   = model?.FirstSelectedMeshContext?.Selection;
            ctx.UndoController   = _undoController;
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
            ctx.SyncMeshPositionsOnly = ctx.SyncMesh;
            return ctx;
        }

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
