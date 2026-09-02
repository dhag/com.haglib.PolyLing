// PrimitivePlaceToolHandler.cs
// 新図形生成サブツールの配置ギズモ。生成予定形状の位置 / 回転 / スケールを
// メイン3Dウインドウのギズモで操作する IPlayerToolHandler 実装。
//
// モデルには一切触れない。値の読み書きは全て外部コールバック経由で行うため、
// 本クラスは PlayerPrimitiveMeshSubPanel を直接参照しない。
//
// 【入力経路】ビューポート入力は MoveToolHandler が受け、そのフック
//   (GizmoHitTestOverride / OnDragStartExtra / OnToolDragExtra / OnToolDragEndExtra)
//   から GizmoHitTest / BeginGizmoDrag / GizmoDrag / EndGizmoDrag を呼ぶ。
//   RotateToolHandler・ScaleToolHandler と同じ構成。
//   本クラスを IPlayerToolHandler として直接ツールへ据えると、頂点・辺の選択が
//   一切できなくなる（穴つなぎの種取り込みが選択を要求するため致命的）。
//   IPlayerToolHandler 実装は上記4メソッドへの委譲として残す。
//
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 図形の配置ギズモ。表示は PrimitivePlaceSettings のチェックで決める。
    /// <para>
    /// 回転リングは矢印 / キューブとは別スロット（GizmoData.RingX/Y/Z）なので
    /// 同時に出せる。移動の矢印と拡大縮小のキューブは軸ギズモの座標 1 組を
    /// 共有するため同時には出せず、PrimitivePlaceSettings 側で排他になっている。
    /// </para>
    /// <para>
    /// 掴んだときの動作は「当たった要素」で決める。リング＝回転、
    /// 軸ギズモ＝拡大縮小表示中なら拡大縮小、そうでなければ移動。
    /// </para>
    /// </summary>
    public class PrimitivePlaceToolHandler : IPlayerToolHandler, IPlayerGizmoProvider
    {
        /// <summary>ドラッグ中の操作種別。</summary>
        private enum DragKind { None, Move, Rotate, Scale }

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

        /// <summary>
        /// 表示設定。Viewer が生成した 1 個をサブパネルと共有する。
        /// 未設定にしないため既定インスタンスを持たせておく。
        /// </summary>
        public PrimitivePlaceSettings Settings { get; set; } = new PrimitivePlaceSettings();

        /// <summary>軸ギズモ（矢印またはキューブ）を出すか。</summary>
        private bool ShowArrows
            => Settings != null && (Settings.ShowMoveGizmo || Settings.ShowScaleGizmo);

        /// <summary>軸ギズモを拡大縮小として扱うか。false なら移動。</summary>
        private bool ArrowIsScale => Settings != null && Settings.ShowScaleGizmo;

        /// <summary>回転リングを出すか。</summary>
        private bool ShowRings => Settings != null && Settings.ShowRotationGizmo;

        private readonly AxisGizmo       _axisGizmo = new AxisGizmo();
        private readonly RotateRingGizmo _ringGizmo = new RotateRingGizmo();

        private AxisGizmo.AxisType _hoverAxis = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _dragAxis  = AxisGizmo.AxisType.None;
        private DragKind           _dragKind  = DragKind.None;

        // GizmoHitTest で当てた軸を BeginGizmoDrag まで持ち越すための控え。
        // フック経由の呼び出しは (ヒットテスト) → (ドラッグ開始) の2段になり、
        // 後者はスクリーン座標を受け取らないため、ここで押下位置も控える。
        // 当てた要素で操作種別が決まるので、種別も一緒に控える。
        private AxisGizmo.AxisType _pendingAxis   = AxisGizmo.AxisType.None;
        private DragKind           _pendingKind   = DragKind.None;
        private Vector2            _pendingScreen = Vector2.zero;

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
            _hoverAxis = AxisGizmo.AxisType.None;

            // リングを先に見る。リングと矢印が重なる位置では回転を優先する
            // （GizmoHitTest と同じ順序にしないと、見た目と掴める対象がずれる）。
            if (ShowRings)
            {
                _ringGizmo.Center = GizmoCenter();
                _hoverAxis = _ringGizmo.FindRingAtScreenPos(imgui, ctx);
            }
            if (_hoverAxis == AxisGizmo.AxisType.None && ShowArrows)
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
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) { _dragAxis = AxisGizmo.AxisType.None; return; }

            if (!GizmoHitTest(screenPos, ctx)) { _dragAxis = AxisGizmo.AxisType.None; return; }
            BeginGizmoDrag();
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
            => GizmoDrag(screenPos, delta);

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
            => EndGizmoDrag();

        // ================================================================
        // MoveToolHandler フック用（選択と配置ギズモを両立させる経路）
        // ================================================================

        /// <summary>
        /// ギズモヒットテスト（MoveToolHandler.GizmoHitTestOverride 用）。
        /// 当たった軸／リングは BeginGizmoDrag まで控える。
        /// </summary>
        public bool GizmoHitTest(Vector2 screenPos, ToolContext ctx)
        {
            _pendingAxis = AxisGizmo.AxisType.None;
            _pendingKind = DragKind.None;
            if (ctx == null) return false;

            Vector3 center = GizmoCenter();
            Vector2 imgui  = ToImgui(screenPos);

            if (ShowRings)
            {
                _ringGizmo.Center = center;
                var ringAxis = _ringGizmo.FindRingAtScreenPos(imgui, ctx);
                if (ringAxis != AxisGizmo.AxisType.None)
                {
                    _pendingAxis   = ringAxis;
                    _pendingKind   = DragKind.Rotate;
                    _pendingScreen = screenPos;
                    return true;
                }
            }

            if (ShowArrows)
            {
                _axisGizmo.Center = center;
                var axis = _axisGizmo.FindAxisAtScreenPos(imgui, ctx);
                if (axis != AxisGizmo.AxisType.None)
                {
                    _pendingAxis   = axis;
                    _pendingKind   = ArrowIsScale ? DragKind.Scale : DragKind.Move;
                    _pendingScreen = screenPos;
                    return true;
                }
            }

            return false;
        }

        /// <summary>ドラッグセッション開始（OnDragStartExtra 用）。true でギズモ操作へ。</summary>
        public bool BeginGizmoDrag()
        {
            _dragAxis = AxisGizmo.AxisType.None;
            _dragKind = DragKind.None;
            if (_pendingAxis == AxisGizmo.AxisType.None) return false;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null)
            {
                _pendingAxis = AxisGizmo.AxisType.None;
                _pendingKind = DragKind.None;
                return false;
            }

            var axis = _pendingAxis;
            var kind = _pendingKind;
            _pendingAxis = AxisGizmo.AxisType.None;
            _pendingKind = DragKind.None;

            if (kind == DragKind.Rotate)
            {
                // 開始角・軸符号の算出は RotateRingGizmo の角度ドラッグセッションに集約。
                _ringGizmo.Center = GizmoCenter();
                if (!_ringGizmo.BeginAngleDrag(ctx, ToImgui(_pendingScreen), axis)) return false;

                _dragAxis      = axis;
                _dragKind      = DragKind.Rotate;
                _startRotation = GetRotation?.Invoke() ?? Vector3.zero;
                return true;
            }

            _axisGizmo.Center = GizmoCenter();
            _dragAxis = axis;
            _dragKind = kind;

            if (kind == DragKind.Scale)
            {
                _startScale = GetScale?.Invoke() ?? Vector3.one;
                // 軸スクリーン方向の算出は AxisGizmo のスケールドラッグセッションに集約。
                _axisGizmo.BeginScaleDrag(ctx, axis, _pendingScreen);
            }
            return true;
        }

        /// <summary>ドラッグ中の更新（OnToolDragExtra 用）。</summary>
        public void GizmoDrag(Vector2 screenPos, Vector2 screenDelta)
        {
            if (_dragAxis == AxisGizmo.AxisType.None) return;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;

            switch (_dragKind)
            {
                case DragKind.Move:   DragMove(screenDelta, ctx); break;
                case DragKind.Rotate: DragRotate(screenPos);      break;
                case DragKind.Scale:  DragScale(screenPos);       break;
                default: return;
            }

            OnValueChanged?.Invoke();
            OnRepaint?.Invoke();
        }

        /// <summary>ドラッグ確定（OnToolDragEndExtra 用）。</summary>
        public void EndGizmoDrag()
        {
            _dragAxis    = AxisGizmo.AxisType.None;
            _dragKind    = DragKind.None;
            _pendingAxis = AxisGizmo.AxisType.None;
            _pendingKind = DragKind.None;
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

        /// <summary>移動 / 拡大縮小の軸ギズモ座標。どちらも非表示のときは false。</summary>
        public bool TryGetGizmoScreenPositions(
            ToolContext ctx,
            out Vector2 origin, out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            origin = xEnd = yEnd = zEnd = Vector2.zero;
            hoveredAxis = AxisGizmo.AxisType.None;

            if (ctx == null || !ShowArrows) return false;

            _axisGizmo.Center       = GizmoCenter();
            _axisGizmo.HoveredAxis  = _hoverAxis;
            _axisGizmo.DraggingAxis = _dragAxis;
            _axisGizmo.GetScreenPositions(ctx, out origin, out xEnd, out yEnd, out zEnd);
            hoveredAxis = _dragAxis != AxisGizmo.AxisType.None ? _dragAxis : _hoverAxis;
            return true;
        }

        /// <summary>回転リング座標。回転ギズモ非表示のときは false。</summary>
        public bool TryGetGizmoRings(
            ToolContext ctx,
            out Vector2[] ringX, out Vector2[] ringY, out Vector2[] ringZ,
            out AxisGizmo.AxisType hoveredAxis)
        {
            ringX = ringY = ringZ = null;
            hoveredAxis = AxisGizmo.AxisType.None;

            if (ctx == null || !ShowRings) return false;

            _ringGizmo.Center = GizmoCenter();
            ringX = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.X);
            ringY = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Y);
            ringZ = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Z);
            hoveredAxis = _dragAxis != AxisGizmo.AxisType.None ? _dragAxis : _hoverAxis;
            return true;
        }

        /// <summary>
        /// ギズモ表示データを組み立てる（IPlayerGizmoProvider）。
        /// 回転はリング、拡大縮小はキューブ、移動はオブジェクト姿勢と同じ矢印。
        /// リングと軸ギズモは DrawAxisWithRing で同時に描く
        /// （ObjectMoveToolHandler.TryBuildGizmoData と同じ組み立て方）。
        /// どちらも無ければ false を返し、呼び出し側が HideGizmo する。
        /// </summary>
        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;

            bool hasAxis = TryGetGizmoScreenPositions(
                ctx, out var po, out var pxe, out var pye, out var pze, out var axisHover);
            bool hasRing = TryGetGizmoRings(
                ctx, out var prx, out var pry, out var prz, out var ringHover);

            if (!hasAxis && !hasRing) return false;

            // ホバーは UpdateHover でリング優先の排他にしてあるので、
            // 非 None の方をそのまま採用する。
            var shownHover = axisHover != AxisGizmo.AxisType.None ? axisHover : ringHover;

            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo         = true,
                IsCubeStyle      = hasAxis && ArrowIsScale,
                IsDiamondStyle   = false,
                Origin           = po, XEnd = pxe, YEnd = pye, ZEnd = pze,
                HoveredAxis      = shownHover,
                IsRingStyle      = hasRing,
                RingX = prx, RingY = pry, RingZ = prz,
                DrawAxisWithRing = hasAxis,
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
