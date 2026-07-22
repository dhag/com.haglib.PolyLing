// RotateToolHandler.cs
// RotateTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class RotateToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly RotateTool _tool = new RotateTool();
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

        // ================================================================
        // 設定公開API
        // ================================================================

        public float RotX      { get => _tool.RotX;         set { _tool.RotX = value; } }
        public float RotY      { get => _tool.RotY;         set { _tool.RotY = value; } }
        public float RotZ      { get => _tool.RotZ;         set { _tool.RotZ = value; } }
        public bool  UseSnap       { get => _tool.UseSnap;      set => _tool.UseSnap = value; }
        public float SnapAngle     { get => _tool.SnapAngle;    set => _tool.SnapAngle = value; }
        public bool  UseOriginPivot{ get => _tool.UseOriginPivot; set => _tool.UseOriginPivot = value; }
        public bool         UseMagnet          { get => _tool.UseMagnet;          set => _tool.UseMagnet = value; }
        public float        MagnetRadius       { get => _tool.MagnetRadius;       set => _tool.MagnetRadius = value; }
        public Poly_Ling.Tools.FalloffType  MagnetFalloff      { get => _tool.MagnetFalloff;      set => _tool.MagnetFalloff = value; }
        public Poly_Ling.Tools.DistanceMode MagnetDistanceMode { get => _tool.MagnetDistanceMode; set => _tool.MagnetDistanceMode = value; }
        public bool  AxisMode  { get => _tool.AxisMode;  set => _tool.AxisMode = value; }
        public float AxisVecX  { get => _tool.AxisVecX;  set => _tool.AxisVecX = value; }
        public float AxisVecY  { get => _tool.AxisVecY;  set => _tool.AxisVecY = value; }
        public float AxisVecZ  { get => _tool.AxisVecZ;  set => _tool.AxisVecZ = value; }
        public float AxisAngle { get => _tool.AxisAngle; set => _tool.AxisAngle = value; }
        public UnityEngine.Vector3 PivotPublic => _tool.PivotPublic;
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

        // ── ビューポート・回転リングギズモ ────────────────────────────────

        private readonly RotateRingGizmo _ringGizmo = new RotateRingGizmo();
        private AxisGizmo.AxisType _gizmoHoverAxis = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _gizmoDragAxis  = AxisGizmo.AxisType.None;
        private Vector2 _gizmoPivotScreen;
        private float   _gizmoStartAngle;
        private float   _gizmoAxisSign = 1f;
        private bool    _prevAxisMode;

        /// <summary>
        /// 回転ピボット(_tool.PivotPublic はローカル空間)を対象メッシュの WorldMatrix で
        /// ワールド変換して返す。WorldToScreenPos はワールド空間を期待するため、この変換が
        /// 無いと Player（WorldMatrix 非 identity）でリングが実頂点から離れて描画される。
        /// 内部の回転数学が使うローカル _pivot は変更しない。
        /// </summary>
        private Vector3 WorldPivot()
        {
            var mc = _project?.CurrentModel?.FirstDrawableMeshContext;
            var local = _tool.PivotPublic;
            return mc != null ? mc.LocalToWorld(local) : local;
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null || _tool.GetTotalAffectedCountPublic() == 0)
            {
                _gizmoHoverAxis = AxisGizmo.AxisType.None; return;
            }
            _ringGizmo.Center = WorldPivot();
            _gizmoHoverAxis = _ringGizmo.FindRingAtScreenPos(ToImgui(screenPos), ctx);
            OnRepaint?.Invoke();
        }

        /// <summary>3軸リングのスクリーン点列を返す（UpdateGizmoOverlay 用）。</summary>
        public bool TryGetGizmoRings(ToolContext ctx,
            out Vector2[] ringX, out Vector2[] ringY, out Vector2[] ringZ,
            out AxisGizmo.AxisType hoveredAxis)
        {
            ringX = ringY = ringZ = null;
            hoveredAxis = AxisGizmo.AxisType.None;
            if (ctx == null || _tool.GetTotalAffectedCountPublic() == 0) return false;
            _ringGizmo.Center = WorldPivot();
            ringX = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.X);
            ringY = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Y);
            ringZ = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Z);
            hoveredAxis = _gizmoDragAxis != AxisGizmo.AxisType.None ? _gizmoDragAxis : _gizmoHoverAxis;
            return true;
        }

        public bool GizmoHitTest(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null || _tool.GetTotalAffectedCountPublic() == 0) return false;
            _ringGizmo.Center = WorldPivot();
            var axis = _ringGizmo.FindRingAtScreenPos(ToImgui(screenPos), ctx);
            if (axis == AxisGizmo.AxisType.None) return false;
            _gizmoDragAxis = axis;
            return true;
        }

        public bool BeginGizmoDrag()
        {
            if (_gizmoDragAxis == AxisGizmo.AxisType.None) return false;
            var ctx = GetToolContext?.Invoke();
            if (ctx == null || ctx.WorldToScreenPos == null) { _gizmoDragAxis = AxisGizmo.AxisType.None; return false; }

            Vector3 center = WorldPivot();
            _gizmoPivotScreen = ctx.WorldToScreenPos(center, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            // 開始角（ピボットスクリーン基準、ctx系=Y上）
            var cursor = LastImguiCursor;
            _gizmoStartAngle = Mathf.Atan2(cursor.y - _gizmoPivotScreen.y, cursor.x - _gizmoPivotScreen.x);

            // 符号: 軸がカメラ側を向くとき +1
            Vector3 worldAxis = RotateRingGizmo.AxisVector(_gizmoDragAxis);
            Vector3 camDir = (ctx.CameraPosition - center).normalized;
            _gizmoAxisSign = Vector3.Dot(worldAxis, camDir) >= 0f ? 1f : -1f;

            _prevAxisMode = _tool.AxisMode;
            _tool.AxisMode = true;
            _tool.AxisVecX = worldAxis.x; _tool.AxisVecY = worldAxis.y; _tool.AxisVecZ = worldAxis.z;
            _tool.AxisAngle = 0f;
            _tool.BeginSliderDrag();
            return true;
        }

        public void GizmoDrag(Vector2 screenPos)
        {
            if (_gizmoDragAxis == AxisGizmo.AxisType.None) return;
            Vector2 cur = ToImgui(screenPos);
            float ang = Mathf.Atan2(cur.y - _gizmoPivotScreen.y, cur.x - _gizmoPivotScreen.x);
            float delta = Mathf.DeltaAngle(_gizmoStartAngle * Mathf.Rad2Deg, ang * Mathf.Rad2Deg);
            _tool.AxisAngle = delta * _gizmoAxisSign;
            OnRepaint?.Invoke();
        }

        public void EndGizmoDrag()
        {
            if (_gizmoDragAxis == AxisGizmo.AxisType.None) return;
            _gizmoDragAxis = AxisGizmo.AxisType.None;
            EndSliderDrag();
            _tool.AxisMode  = _prevAxisMode;
            _tool.AxisAngle = 0f;
        }

        // ドラッグ中の最新カーソル（ctx系=Y上）。GizmoHitTest / hover で更新。
        private Vector2 LastImguiCursor;

        private Vector2 ToImgui(Vector2 screenPosYDown)
        {
            float h = GetPanelHeight?.Invoke() ?? 0f;
            var v = new Vector2(screenPosYDown.x, h - screenPosYDown.y);
            LastImguiCursor = v;
            return v;
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
