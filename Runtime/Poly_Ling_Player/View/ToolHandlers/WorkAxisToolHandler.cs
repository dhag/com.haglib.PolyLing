// WorkAxisToolHandler.cs
// 作業用ローカル軸 (WorkAxisContext) をメイン3Dウインドウのギズモで操作する
// IPlayerToolHandler 実装。
//
// モデルの頂点・選択状態には一切触れない。読み書きするのは WorkAxisContext だけ。
// 取得は外部コールバック (GetWorkAxis) 経由で行うため、本クラスは
// ProjectContext / ModelContext を直接参照しない。
//
// 移動 / 回転をサブモードで切り替える。GizmoData（PlayerViewportPanel）は
// 矢印とリングを排他的にしか描画できないため、2種を同時には出さない。
// これは PrimitivePlaceToolHandler と同じ制約・同じ方式。
//
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 作業軸ギズモ。移動 / 回転をサブモードで切り替える。
    /// </summary>
    public class WorkAxisToolHandler : IPlayerToolHandler, IPlayerGizmoProvider
    {
        /// <summary>作業軸ギズモのサブモード。</summary>
        public enum WorkAxisGizmoMode { Move, Rotate }

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>    GetToolContext;
        public Func<float>          GetPanelHeight;
        public Action               OnRepaint;

        /// <summary>操作対象の作業軸。null なら何もしない。</summary>
        public Func<WorkAxisContext> GetWorkAxis;

        /// <summary>ドラッグで値が変わったときに呼ぶ。UI 書き戻しとギズモ再描画に使う。</summary>
        public Action OnValueChanged;

        // ================================================================
        // 状態
        // ================================================================

        /// <summary>現在のサブモード。</summary>
        public WorkAxisGizmoMode Mode { get; set; } = WorkAxisGizmoMode.Move;

        // ScreenOffset は既定 (60,-60) だが、作業軸は「原点そのもの」が編集対象の
        // 値なので、ずらさず原点位置に描く。リング側は元から中心に描かれる。
        private readonly AxisGizmo       _axisGizmo = new AxisGizmo { ScreenOffset = Vector2.zero };
        private readonly RotateRingGizmo _ringGizmo = new RotateRingGizmo();

        private AxisGizmo.AxisType _hoverAxis = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _dragAxis  = AxisGizmo.AxisType.None;

        // ドラッグ開始時のスナップショット（絶対計算の基準）
        private Quaternion _startRotation = Quaternion.identity;

        // 回転スナップ（度）。0 以下でスナップ無効。
        public float RotateSnapDeg { get; set; } = 0f;

        // ================================================================
        // ギズモの中心・向き
        // ================================================================

        /// <summary>
        /// 作業軸の値をギズモへ流し込む。Origin はワールド座標、Rotation は
        /// AxisGizmo / RotateRingGizmo の Orientation に渡してローカル軸表示にする。
        /// </summary>
        private bool SyncGizmoFromAxis()
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return false;

            _axisGizmo.Center      = wa.Origin;
            _axisGizmo.Orientation = wa.Rotation;
            _ringGizmo.Center      = wa.Origin;
            _ringGizmo.Orientation = wa.Rotation;
            return true;
        }

        // ================================================================
        // ホバー
        // ================================================================

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null || !SyncGizmoFromAxis())
            {
                _hoverAxis = AxisGizmo.AxisType.None;
                return;
            }

            var imgui = ToImgui(screenPos);
            _hoverAxis = (Mode == WorkAxisGizmoMode.Rotate)
                ? _ringGizmo.FindRingAtScreenPos(imgui, ctx)
                : _axisGizmo.FindAxisAtScreenPos(imgui, ctx);

            OnRepaint?.Invoke();
        }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            // クリックのみでは何もしない（選択操作とは無関係のツール）。
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            _dragAxis = AxisGizmo.AxisType.None;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null || !SyncGizmoFromAxis()) return;

            var wa    = GetWorkAxis.Invoke();
            Vector2 imgui = ToImgui(screenPos);

            if (Mode == WorkAxisGizmoMode.Rotate)
            {
                var axis = _ringGizmo.FindRingAtScreenPos(imgui, ctx);
                if (axis == AxisGizmo.AxisType.None) return;

                // 開始角・軸符号の算出は RotateRingGizmo の角度ドラッグセッションに集約。
                if (!_ringGizmo.BeginAngleDrag(ctx, imgui, axis)) return;

                _dragAxis      = axis;
                _startRotation = wa.Rotation;
                return;
            }

            var hitAxis = _axisGizmo.FindAxisAtScreenPos(imgui, ctx);
            if (hitAxis == AxisGizmo.AxisType.None) return;

            _dragAxis = hitAxis;
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (_dragAxis == AxisGizmo.AxisType.None) return;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null || !SyncGizmoFromAxis()) return;

            if (Mode == WorkAxisGizmoMode.Rotate) DragRotate(screenPos);
            else                                  DragMove(delta, ctx);

            OnValueChanged?.Invoke();
            OnRepaint?.Invoke();
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            _dragAxis = AxisGizmo.AxisType.None;
            _ringGizmo.EndAngleDrag();
            OnRepaint?.Invoke();
        }

        // ================================================================
        // サブモード別のドラッグ処理
        // ================================================================

        private void DragMove(Vector2 screenDelta, ToolContext ctx)
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return;

            // screenDelta はパネルの ToViewportCoord 系（+Y が画面上）。
            // ComputeFreeDelta はこの系をそのまま要求するが、ComputeAxisDelta は
            // WorldToScreenPos 系（+Y が画面下）を要求するため Y を反転して渡す。
            Vector3 worldDelta = (_dragAxis == AxisGizmo.AxisType.Center)
                ? _axisGizmo.ComputeFreeDelta(screenDelta, ctx)
                : _axisGizmo.ComputeAxisDelta(
                    new Vector2(screenDelta.x, -screenDelta.y), _dragAxis, ctx);

            if (worldDelta == Vector3.zero) return;

            // Origin はワールド座標なので、ワールド差分をそのまま加算する。
            wa.Origin = wa.Origin + worldDelta;
        }

        private void DragRotate(Vector2 screenPos)
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return;

            float deltaDeg = _ringGizmo.ComputeAngleDeltaDeg(ToImgui(screenPos));
            if (RotateSnapDeg > 0f)
                deltaDeg = Mathf.Round(deltaDeg / RotateSnapDeg) * RotateSnapDeg;

            // リングは作業軸のローカル軸まわりに描かれている。開始姿勢を基準に
            // ローカル軸まわりの回転を右から掛けることで、見た目のリングと一致する。
            // 開始姿勢から絶対で計算するため、フレーム誤差が累積しない。
            Vector3 localAxis = RotateRingGizmo.AxisVector(_dragAxis);
            wa.Rotation = _startRotation * Quaternion.AngleAxis(deltaDeg, localAxis);
        }

        // ================================================================
        // ギズモスクリーン座標（UpdateGizmoOverlay 用）
        // ================================================================

        /// <summary>移動の軸ギズモ座標。回転モードのときは false。</summary>
        public bool TryGetGizmoScreenPositions(
            ToolContext ctx,
            out Vector2 origin, out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            origin = xEnd = yEnd = zEnd = Vector2.zero;
            hoveredAxis = AxisGizmo.AxisType.None;

            if (ctx == null || Mode == WorkAxisGizmoMode.Rotate) return false;
            if (!SyncGizmoFromAxis()) return false;

            _axisGizmo.HoveredAxis  = _hoverAxis;
            _axisGizmo.DraggingAxis = _dragAxis;
            _axisGizmo.GetScreenPositions(ctx, out origin, out xEnd, out yEnd, out zEnd);
            hoveredAxis = _dragAxis != AxisGizmo.AxisType.None ? _dragAxis : _hoverAxis;
            return true;
        }

        /// <summary>回転リング座標。回転モード以外のときは false。</summary>
        public bool TryGetGizmoRings(
            ToolContext ctx,
            out Vector2[] ringX, out Vector2[] ringY, out Vector2[] ringZ,
            out AxisGizmo.AxisType hoveredAxis)
        {
            ringX = ringY = ringZ = null;
            hoveredAxis = AxisGizmo.AxisType.None;

            if (ctx == null || Mode != WorkAxisGizmoMode.Rotate) return false;
            if (!SyncGizmoFromAxis()) return false;

            ringX = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.X);
            ringY = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Y);
            ringZ = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Z);
            hoveredAxis = _dragAxis != AxisGizmo.AxisType.None ? _dragAxis : _hoverAxis;
            return true;
        }

        /// <summary>
        /// ギズモ表示データを組み立てる（IPlayerGizmoProvider）。
        /// Rotate はリング、Move は矢印。IsVisible=false のときは非表示。
        /// </summary>
        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;

            var wa = GetWorkAxis?.Invoke();
            if (wa == null || !wa.IsVisible) return false;

            if (Mode == WorkAxisGizmoMode.Rotate)
            {
                if (!TryGetGizmoRings(ctx, out var rx, out var ry, out var rz, out var rha))
                    return false;

                data = new PlayerViewportPanel.GizmoData
                {
                    HasGizmo    = true,
                    IsRingStyle = true,
                    RingX = rx, RingY = ry, RingZ = rz,
                    HoveredAxis = rha,
                };
                return true;
            }

            if (!TryGetGizmoScreenPositions(ctx, out var o, out var xe, out var ye, out var ze, out var ah))
                return false;

            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo       = true,
                IsDiamondStyle = false,
                IsCubeStyle    = false,
                Origin         = o, XEnd = xe, YEnd = ye, ZEnd = ze,
                HoveredAxis    = ah,
            };
            return true;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        /// <summary>スクリーン系（Y 下）→ ctx 系（Y 上）。</summary>
        private Vector2 ToImgui(Vector2 screenPosYDown)
        {
            float h = GetPanelHeight?.Invoke() ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
