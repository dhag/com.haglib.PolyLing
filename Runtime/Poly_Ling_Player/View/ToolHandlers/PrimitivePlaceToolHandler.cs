// PrimitivePlaceToolHandler.cs
// 新図形生成サブツールの配置ギズモ。生成予定形状の位置 / 回転 / スケールを
// メイン3Dウインドウのギズモで操作する IPlayerToolHandler 実装。
//
// モデルには一切触れない。値の読み書きは全て外部コールバック経由で行うため、
// 本クラスは PlayerPrimitiveMeshSubPanel を直接参照しない。
//
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 図形の配置ギズモ。移動 / 回転 / スケールをサブモードで切り替える。
    /// <para>
    /// GizmoData（PlayerViewportPanel）は矢印 / リング / キューブを排他的にしか
    /// 描画できないため、3種を同時には出さずサブモードで切り替える設計とする。
    /// </para>
    /// </summary>
    public class PrimitivePlaceToolHandler : IPlayerToolHandler, IPlayerGizmoProvider
    {
        /// <summary>配置ギズモのサブモード。</summary>
        public enum PlaceGizmoMode { Move, Rotate, Scale }

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Func<float>       GetPanelHeight;
        public Action            OnRepaint;

        /// <summary>生成位置の取得 / 設定。AddToExisting のときは追加先ローカル空間。</summary>
        public Func<Vector3> GetPosition;
        public Action<Vector3> SetPosition;

        /// <summary>生成時の回転（オイラー角・度）の取得 / 設定。</summary>
        public Func<Vector3> GetRotation;
        public Action<Vector3> SetRotation;

        /// <summary>生成時のスケールの取得 / 設定。</summary>
        public Func<Vector3> GetScale;
        public Action<Vector3> SetScale;

        /// <summary>
        /// ギズモの中心（ワールド座標）。AddToExisting のときは追加先の WorldMatrix を
        /// 掛けた位置になる。未設定なら GetPosition の値をそのまま使う。
        /// </summary>
        public Func<Vector3> GetGizmoWorldCenter;

        /// <summary>
        /// ワールド差分をローカル差分へ変換する。AddToExisting のとき追加先の
        /// WorldMatrixInverse を掛けるために使う。未設定なら変換しない。
        /// </summary>
        public Func<Vector3, Vector3> WorldDeltaToLocal;

        /// <summary>ドラッグで値が変わったときに呼ぶ。UI 書き戻しとギズモ再描画に使う。</summary>
        public Action OnValueChanged;

        // ================================================================
        // 状態
        // ================================================================

        /// <summary>現在のサブモード。</summary>
        public PlaceGizmoMode Mode { get; set; } = PlaceGizmoMode.Move;

        private readonly AxisGizmo       _axisGizmo = new AxisGizmo();
        private readonly RotateRingGizmo _ringGizmo = new RotateRingGizmo();

        private AxisGizmo.AxisType _hoverAxis = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _dragAxis  = AxisGizmo.AxisType.None;

        // ドラッグ開始時のスナップショット
        private Vector3 _startRotation;
        private Vector3 _startScale;

        // ================================================================
        // 中心座標
        // ================================================================

        private Vector3 GizmoCenter()
        {
            if (GetGizmoWorldCenter != null) return GetGizmoWorldCenter();
            return GetPosition?.Invoke() ?? Vector3.zero;
        }

        // ================================================================
        // ホバー
        // ================================================================

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) { _hoverAxis = AxisGizmo.AxisType.None; return; }

            var imgui = ToImgui(screenPos);
            if (Mode == PlaceGizmoMode.Rotate)
            {
                _ringGizmo.Center = GizmoCenter();
                _hoverAxis = _ringGizmo.FindRingAtScreenPos(imgui, ctx);
            }
            else
            {
                _axisGizmo.Center = GizmoCenter();
                _hoverAxis = _axisGizmo.FindAxisAtScreenPos(imgui, ctx);
            }
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
            if (ctx == null) return;

            Vector3 center = GizmoCenter();
            Vector2 imgui  = ToImgui(screenPos);

            if (Mode == PlaceGizmoMode.Rotate)
            {
                _ringGizmo.Center = center;
                var axis = _ringGizmo.FindRingAtScreenPos(imgui, ctx);
                if (axis == AxisGizmo.AxisType.None) return;

                // 開始角・軸符号の算出は RotateRingGizmo の角度ドラッグセッションに集約。
                if (!_ringGizmo.BeginAngleDrag(ctx, imgui, axis)) return;

                _dragAxis = axis;
                _startRotation = GetRotation?.Invoke() ?? Vector3.zero;
                return;
            }

            _axisGizmo.Center = center;
            var hitAxis = _axisGizmo.FindAxisAtScreenPos(imgui, ctx);
            if (hitAxis == AxisGizmo.AxisType.None) return;

            _dragAxis = hitAxis;

            if (Mode == PlaceGizmoMode.Scale)
            {
                _startScale = GetScale?.Invoke() ?? Vector3.one;
                // 軸スクリーン方向の算出は AxisGizmo のスケールドラッグセッションに集約。
                _axisGizmo.BeginScaleDrag(ctx, hitAxis, screenPos);
            }
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (_dragAxis == AxisGizmo.AxisType.None) return;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;

            switch (Mode)
            {
                case PlaceGizmoMode.Move:   DragMove(delta, ctx);       break;
                case PlaceGizmoMode.Rotate: DragRotate(screenPos);      break;
                case PlaceGizmoMode.Scale:  DragScale(screenPos);       break;
            }

            OnValueChanged?.Invoke();
            OnRepaint?.Invoke();
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            _dragAxis = AxisGizmo.AxisType.None;
            _ringGizmo.EndAngleDrag();
            _axisGizmo.EndScaleDrag();
            OnRepaint?.Invoke();
        }

        // ================================================================
        // サブモード別のドラッグ処理
        // ================================================================

        private void DragMove(Vector2 screenDelta, ToolContext ctx)
        {
            _axisGizmo.Center = GizmoCenter();

            // screenDelta はパネルの ToViewportCoord 系（+Y が画面上）。
            // ComputeFreeDelta はこの系をそのまま要求するが、ComputeAxisDelta は
            // WorldToScreenPos 系（+Y が画面下）を要求するため Y を反転して渡す。
            // 反転しないと軸拘束移動の Y だけマウスと逆向きに動く。
            Vector3 worldDelta = (_dragAxis == AxisGizmo.AxisType.Center)
                ? _axisGizmo.ComputeFreeDelta(screenDelta, ctx)
                : _axisGizmo.ComputeAxisDelta(
                    new Vector2(screenDelta.x, -screenDelta.y), _dragAxis, ctx);

            if (worldDelta == Vector3.zero) return;

            // AddToExisting のとき _worldPos は追加先ローカル空間の値なので、
            // ワールド差分をローカル差分へ戻してから加算する。
            Vector3 localDelta = WorldDeltaToLocal != null
                ? WorldDeltaToLocal(worldDelta)
                : worldDelta;

            Vector3 pos = GetPosition?.Invoke() ?? Vector3.zero;
            SetPosition?.Invoke(pos + localDelta);
        }

        private void DragRotate(Vector2 screenPos)
        {
            float deltaDeg = _ringGizmo.ComputeAngleDeltaDeg(ToImgui(screenPos));

            Vector3 rot = _startRotation;
            switch (_dragAxis)
            {
                case AxisGizmo.AxisType.X: rot.x = _startRotation.x + deltaDeg; break;
                case AxisGizmo.AxisType.Y: rot.y = _startRotation.y + deltaDeg; break;
                case AxisGizmo.AxisType.Z: rot.z = _startRotation.z + deltaDeg; break;
                default: return;
            }
            SetRotation?.Invoke(rot);
        }

        private void DragScale(Vector2 screenPos)
        {
            float factor = _axisGizmo.ComputeScaleFactor(screenPos);

            Vector3 s = _startScale;
            switch (_dragAxis)
            {
                case AxisGizmo.AxisType.Center:
                    s = _startScale * factor;
                    break;
                case AxisGizmo.AxisType.X: s.x = _startScale.x * factor; break;
                case AxisGizmo.AxisType.Y: s.y = _startScale.y * factor; break;
                case AxisGizmo.AxisType.Z: s.z = _startScale.z * factor; break;
                default: return;
            }
            SetScale?.Invoke(s);
        }

        // ================================================================
        // ギズモスクリーン座標（UpdateGizmoOverlay 用）
        // ================================================================

        /// <summary>移動 / スケールの軸ギズモ座標。回転モードのときは false。</summary>
        public bool TryGetGizmoScreenPositions(
            ToolContext ctx,
            out Vector2 origin, out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            origin = xEnd = yEnd = zEnd = Vector2.zero;
            hoveredAxis = AxisGizmo.AxisType.None;

            if (ctx == null || Mode == PlaceGizmoMode.Rotate) return false;

            _axisGizmo.Center       = GizmoCenter();
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

            if (ctx == null || Mode != PlaceGizmoMode.Rotate) return false;

            _ringGizmo.Center = GizmoCenter();
            ringX = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.X);
            ringY = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Y);
            ringZ = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Z);
            hoveredAxis = _dragAxis != AxisGizmo.AxisType.None ? _dragAxis : _hoverAxis;
            return true;
        }

        /// <summary>
        /// ギズモ表示データを組み立てる（IPlayerGizmoProvider）。
        /// Rotate はリング、Scale はキューブ、Move はオブジェクト姿勢と同じ矢印。
        /// </summary>
        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;

            if (Mode == PlaceGizmoMode.Rotate)
            {
                if (!TryGetGizmoRings(ctx, out var prx, out var pry, out var prz, out var pha))
                    return false;

                data = new PlayerViewportPanel.GizmoData
                {
                    HasGizmo    = true,
                    IsRingStyle = true,
                    RingX = prx, RingY = pry, RingZ = prz,
                    HoveredAxis = pha,
                };
                return true;
            }

            if (!TryGetGizmoScreenPositions(ctx, out var po, out var pxe, out var pye, out var pze, out var pah))
                return false;

            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo       = true,
                IsCubeStyle    = Mode == PlaceGizmoMode.Scale,
                IsDiamondStyle = false,
                Origin         = po, XEnd = pxe, YEnd = pye, ZEnd = pze,
                HoveredAxis    = pah,
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
