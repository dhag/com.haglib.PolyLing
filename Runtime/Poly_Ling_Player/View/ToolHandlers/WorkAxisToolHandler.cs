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
// 【六角錐表示】
//   矢印 / リングに加えて、軸そのものを六角錐ワイヤで描く
//   （WorkAxisGizmoShape。GizmoData.ExtraLines へ入れるので排他制約の外側）。
//   矢印は画面固定長だが六角錐は WorkAxisContext.Length のワールド長で、
//   Y 軸先端はワールド位置を持つ。
//
// 【吸着】
//   移動モードで次の2つを操作する。どちらも掴んだまま頂点／ボーンへ重ねると
//   吸着し、外せば自由移動へ戻る（吸着から抜けられること）。
//     原点ハンドル（Center）… Origin がハンドル位置へ移る。
//     Y 先端ハンドル       … Rotation を最小回転で向け直す。Length は変えない。
//                              ハンドル自身は軸から離れて追従し、離すと軸の先端へ戻る。
//   吸着先の取得は外部コールバック GetSnapTargetWorld に委ねる（本クラスは
//   ModelContext を直接参照しない方針を維持する）。
//   吸着用 GPU ヒットテストはドラッグ中だけ有効化し、終了時に必ず切る
//   （AddFaceToolHandler と同じ規約。有効な間は頂点数ぶんの読み戻しが増えるため）。
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

        /// <summary>
        /// 吸着先（頂点／ボーン）のワールド座標を返す。無ければ null。
        /// 引数はギズモ判定と同じ ctx 系スクリーン座標（ToImgui 済み）。
        /// 頂点は GPU 吸着ヒットテスト、ボーンは MeshContext の WorldMatrix 投影で
        /// 取る想定。実装は Viewer 側に置く。
        /// </summary>
        public Func<Vector2, Vector3?> GetSnapTargetWorld;

        /// <summary>
        /// 吸着用ヒットテストの有効／無効を Viewer へ伝える
        /// （PlayerViewportManager.SetSnapHitTestEnabled を結線）。
        /// </summary>
        public Action<bool> OnSnapHitTestEnabledChanged;

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

        // Y 先端ハンドル（吸着専用）のホバー／ドラッグ状態。
        // 矢印の Y 先端とは別物なので _hoverAxis / _dragAxis とは分けて持つ。
        private bool _hoverYTip;
        private bool _dragYTip;

        // ロールハンドル（ローカル Y 軸まわりの円弧）のホバー／ドラッグ状態。
        // X/Z の向きだけを回すためのもので、回転モードのリング3本とは別に
        // 移動モードでも掴める。
        private bool _hoverRoll;
        private bool _dragRoll;

        // ドラッグ中の吸着候補（ワールド座標）。表示強調にも使う。
        private Vector3? _snapTarget;

        // Y 先端ハンドルのドラッグ中位置（ワールド座標）。
        // 軸の先端とは切り離してポインタへ追従させるため別に持つ。
        // ドラッグ終了で null に戻し、描画は軸の先端へ戻る。
        private Vector3? _tipDragWorld;

        // ドラッグ開始時のスナップショット（絶対計算の基準）
        private Quaternion _startRotation = Quaternion.identity;

        // 回転スナップ（度）。0 以下でスナップ無効。
        public float RotateSnapDeg { get; set; } = 0f;

        /// <summary>Y 先端ハンドルの当たり半径（px）。</summary>
        public float TipHitRadius { get; set; } = 10f;

        // ================================================================
        // 吸着対象
        // ================================================================

        /// <summary>頂点へ吸着するか。GPU 吸着ヒットテストを使う。</summary>
        public bool SnapToVertex { get; set; } = false;

        /// <summary>ボーンへ吸着するか。ボーンの原点（WorldMatrix の平行移動成分）。</summary>
        public bool SnapToBone { get; set; } = true;

        /// <summary>描画オブジェクトへ吸着するか。オブジェクトの原点。</summary>
        public bool SnapToObject { get; set; } = false;

        /// <summary>吸着先が1つでも選ばれているか。</summary>
        public bool HasAnySnapTarget => SnapToVertex || SnapToBone || SnapToObject;

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
        // Y 先端ハンドル
        // ================================================================

        /// <summary>
        /// Y 先端ハンドルのスクリーン座標（ctx 系）。移動モード以外では掴めない。
        /// 六角錐はワールド長なので、矢印の画面固定長とは別に投影する。
        /// </summary>
        private bool TryGetYTipScreen(ToolContext ctx, out Vector2 screen)
        {
            screen = Vector2.zero;
            if (ctx == null || Mode != WorkAxisGizmoMode.Move) return false;

            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return false;

            screen = ctx.WorldToScreen(wa.YTip);
            return true;
        }

        /// <summary>Y 先端ハンドルに当たっているか。imgui は ctx 系スクリーン座標。</summary>
        private bool HitYTip(Vector2 imgui, ToolContext ctx)
        {
            if (!TryGetYTipScreen(ctx, out var tip)) return false;
            return Vector2.Distance(imgui, tip) < TipHitRadius;
        }

        // ================================================================
        // ロールハンドル（ローカル Y 軸まわり）
        // ================================================================

        /// <summary>ロールハンドルの半径。軸長に対する比。一番短い Z の六角錐と同じ。</summary>
        private const float RollHandleRadiusRatio = 0.3f;

        /// <summary>ロールハンドルの当たり半径（px）。</summary>
        public float RollHitRadius { get; set; } = 9f;

        private static float RollRadius(WorkAxisContext wa)
            => Mathf.Max(WorkAxisContext.MinLength, wa.Length) * RollHandleRadiusRatio;

        /// <summary>
        /// ロールハンドルに当たっているか。imgui は ctx 系スクリーン座標。
        /// 表示と同じ形状を組み立てて判定するので、見えている弧とずれない。
        /// </summary>
        private bool HitRoll(Vector2 imgui, ToolContext ctx)
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null || ctx == null) return false;

            WorkAxisGizmoShape.BuildRotateHandle(
                wa, ctx, Vector3.zero, Vector3.up, RollRadius(wa), 0f,
                WorkAxisGizmoShape.RotateHandleColor, false, out var ring);
            if (ring == null || ring.Length < 2) return false;

            for (int i = 0; i < ring.Length - 1; i++)
            {
                float d = AxisGizmo.DistanceToSegment(
                    imgui, ctx.WorldToScreen(ring[i]), ctx.WorldToScreen(ring[i + 1]));
                if (d < RollHitRadius) return true;
            }
            return false;
        }

        /// <summary>
        /// ロールドラッグ。開始姿勢を基準に、ローカル Y 軸まわりの回転を右から掛ける。
        /// 絶対計算なのでフレーム誤差が累積しない。
        /// </summary>
        private void DragRoll(Vector2 screenPos)
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return;

            float deltaDeg = _ringGizmo.ComputeAngleDeltaDeg(ToImgui(screenPos));
            if (RotateSnapDeg > 0f)
                deltaDeg = Mathf.Round(deltaDeg / RotateSnapDeg) * RotateSnapDeg;

            wa.Rotation = _startRotation * Quaternion.AngleAxis(deltaDeg, Vector3.up);
        }

        /// <summary>
        /// 吸着用ヒットテストの有効／無効を切り替える。
        /// 頂点吸着が OFF のときは立てない。有効な間はポインタ移動ごとに
        /// 頂点数ぶんの読み戻しが増えるため、要らないなら止めておく。
        /// </summary>
        private void SetSnapHitTest(bool on)
        {
            OnSnapHitTestEnabledChanged?.Invoke(on && SnapToVertex);
        }

        // ================================================================
        // ホバー
        // ================================================================

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null || !SyncGizmoFromAxis())
            {
                _hoverAxis = AxisGizmo.AxisType.None;
                _hoverYTip = false;
                _hoverRoll = false;
                return;
            }

            // Y 先端ハンドルを掴んでいる間はホバー判定を止める。先端は吸着で
            // 動くため、ポインタが軸線分の上を通ると別の軸が強調されてしまう。
            // 軸ドラッグ側は _dragAxis が強調に優先するので同じ手当ては要らない。
            if (_dragYTip || _dragRoll) return;

            var imgui = ToImgui(screenPos);

            // ロールハンドルは移動モードでのみ出す。回転モードではリング3本と
            // 重なって紛らわしいため。
            _hoverRoll = Mode == WorkAxisGizmoMode.Move && HitRoll(imgui, ctx);
            if (_hoverRoll)
            {
                _hoverAxis = AxisGizmo.AxisType.None;
                _hoverYTip = false;
                OnRepaint?.Invoke();
                return;
            }

            // Y 先端ハンドルを先に見る。矢印の Y 先端と画面上で重なっても、
            // Y 軸拘束移動は軸線分のドラッグで従来どおり掴めるため実害がない。
            // 逆順にすると倍率によってハンドルが取れなくなる。
            _hoverYTip = HitYTip(imgui, ctx);
            if (_hoverYTip)
            {
                _hoverAxis = AxisGizmo.AxisType.None;
                OnRepaint?.Invoke();
                return;
            }

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
            _dragAxis     = AxisGizmo.AxisType.None;
            _dragYTip     = false;
            _dragRoll     = false;
            _snapTarget   = null;
            _tipDragWorld = null;

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

            // ロールハンドル。ホバーと同じ順序で最初に見る。
            if (HitRoll(imgui, ctx))
            {
                // リングの姿勢はローカル Y 軸まわり。軸種別は Y スロットを使う。
                _ringGizmo.Center      = wa.Origin;
                _ringGizmo.Orientation = wa.Rotation;

                if (_ringGizmo.BeginAngleDrag(ctx, imgui, AxisGizmo.AxisType.Y))
                {
                    _dragRoll      = true;
                    _startRotation = wa.Rotation;
                    return;
                }
            }

            // Y 先端ハンドル。ホバーと同じ順序で先に見る。
            if (HitYTip(imgui, ctx))
            {
                _dragYTip     = true;
                _tipDragWorld = wa.YTip;   // 掴んだ位置から動かし始める
                SetSnapHitTest(true);
                return;
            }

            var hitAxis = _axisGizmo.FindAxisAtScreenPos(imgui, ctx);
            if (hitAxis == AxisGizmo.AxisType.None) return;

            _dragAxis = hitAxis;

            // 原点ハンドルは自由移動中に吸着させる。軸拘束移動は吸着させない
            // （軸から外れた位置へ飛ぶことになり拘束の意味が無くなるため）。
            if (hitAxis == AxisGizmo.AxisType.Center)
                SetSnapHitTest(true);
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (_dragAxis == AxisGizmo.AxisType.None && !_dragYTip && !_dragRoll) return;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null || !SyncGizmoFromAxis()) return;

            if (_dragRoll)                             DragRoll(screenPos);
            else if (_dragYTip)                        DragYTip(screenPos, delta, ctx);
            else if (Mode == WorkAxisGizmoMode.Rotate) DragRotate(screenPos);
            else                                       DragMove(screenPos, delta, ctx);

            OnValueChanged?.Invoke();
            OnRepaint?.Invoke();
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            _dragAxis     = AxisGizmo.AxisType.None;
            _dragYTip     = false;
            _dragRoll     = false;
            _snapTarget   = null;
            // ハンドルは軸の先端へ戻す。
            _tipDragWorld = null;
            _ringGizmo.EndAngleDrag();

            // 吸着ヒットテストは掴んでいる間だけ。取り残すとポインタ移動ごとに
            // 頂点数ぶんの読み戻しが走り続ける。
            SetSnapHitTest(false);

            OnRepaint?.Invoke();
        }

        // ================================================================
        // サブモード別のドラッグ処理
        // ================================================================

        private void DragMove(Vector2 screenPos, Vector2 screenDelta, ToolContext ctx)
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return;

            // 原点ハンドルは吸着を優先する。掴んだまま頂点／ボーンへ重ねると
            // そこへ吸い付き、外すと通常の自由移動へ戻る。
            if (_dragAxis == AxisGizmo.AxisType.Center)
            {
                _snapTarget = ResolveSnapTarget(screenPos);
                if (_snapTarget.HasValue)
                {
                    wa.Origin = _snapTarget.Value;
                    return;
                }
            }

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

        /// <summary>
        /// Y 先端ハンドルのドラッグ。ハンドルを軸から切り離してポインタへ追従させ、
        /// その位置へ Y 軸を向ける。Length は変えない（軸の長さは「長さ」欄で指定する）。
        ///
        /// 頂点／ボーンに重なっている間はそこへ吸着し、外れれば自由移動へ戻る。
        /// 原点ハンドルと同じ挙動で、吸着したまま抜けられなくなることはない。
        /// </summary>
        private void DragYTip(Vector2 screenPos, Vector2 screenDelta, ToolContext ctx)
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return;

            _snapTarget = ResolveSnapTarget(screenPos);

            if (_snapTarget.HasValue)
            {
                _tipDragWorld = _snapTarget.Value;
            }
            else
            {
                // 自由移動。視線直交平面内でポインタ移動量に一致させる。
                // px ↔ ワールド換算は AxisGizmo.Center 近傍で実測されるため、
                // 原点ではなくハンドル自身の奥行きで測る必要がある。
                Vector3 from = _tipDragWorld ?? wa.YTip;

                Vector3 saved = _axisGizmo.Center;
                _axisGizmo.Center = from;
                Vector3 worldDelta = _axisGizmo.ComputeFreeDelta(screenDelta, ctx);
                _axisGizmo.Center = saved;

                _tipDragWorld = from + worldDelta;
            }

            // 最小回転で Y を向け直す（WorkAxisContext 側。Length は不変）。
            wa.AimYAt(_tipDragWorld.Value);
        }

        /// <summary>
        /// 現在のポインタ位置の吸着先を取得する。screenPos は画面系（Y 下）。
        /// </summary>
        private Vector3? ResolveSnapTarget(Vector2 screenPos)
        {
            if (GetSnapTargetWorld == null || !HasAnySnapTarget) return null;
            return GetSnapTargetWorld(ToImgui(screenPos));
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
                    ExtraLines  = BuildPrismLines(ctx, rha, _dragYTip || _hoverYTip),
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
                ExtraLines     = BuildPrismLines(ctx, ah, _dragYTip || _hoverYTip),
            };
            return true;
        }

        /// <summary>
        /// 六角錐ワイヤ（＋ Y 先端ハンドル、吸着候補）を組み立てる。
        /// ExtraLines は軸／リングの描画分岐より前に無条件で描かれるため、
        /// 移動モードでも回転モードでも同じものを渡してよい。
        ///
        /// ホバー強調（shownAxis / tipHi）はポインタが乗っているビューポートの
        /// 話なので、呼び出し側から明示的に渡す。
        /// </summary>
        private PlayerViewportPanel.ScreenPolyline[] BuildPrismLines(
            ToolContext ctx, AxisGizmo.AxisType shownAxis, bool tipHi)
        {
            var wa = GetWorkAxis?.Invoke();
            if (wa == null) return null;

            // 作業軸ツールでは Y 先端ハンドルを掴めるので表示する。
            var body = WorkAxisGizmoShape.Build(
                wa, ctx, shownAxis, tipHi, _tipDragWorld, _snapTarget, true);

            // ロールハンドルは移動モードのみ。X/Z の向きだけを回す。
            if (Mode != WorkAxisGizmoMode.Move) return body;

            bool rollHi = _dragRoll || _hoverRoll;
            var  roll   = WorkAxisGizmoShape.BuildRotateHandle(
                wa, ctx, Vector3.zero, Vector3.up, RollRadius(wa), 0f,
                rollHi ? WorkAxisGizmoShape.RotateHandleColorHi
                       : WorkAxisGizmoShape.RotateHandleColor,
                rollHi, out _);

            if (roll == null) return body;
            if (body == null) return roll;

            var all = new PlayerViewportPanel.ScreenPolyline[body.Length + roll.Length];
            System.Array.Copy(body, 0, all, 0,           body.Length);
            System.Array.Copy(roll, 0, all, body.Length, roll.Length);
            return all;
        }

        /// <summary>
        /// 表示専用のギズモデータ。ポインタが乗っていないビューポートへ出す。
        ///
        /// 六角錐だけを描き、矢印・リング・中心ハンドルは出さない
        /// （そのビューポートでは掴めないため、掴めるように見せない）。
        /// ホバー強調も付けない。
        /// </summary>
        public bool TryBuildDisplayOnlyGizmoData(
            ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;

            var wa = GetWorkAxis?.Invoke();
            if (ctx == null || wa == null || !wa.IsVisible) return false;

            var lines = BuildPrismLines(ctx, AxisGizmo.AxisType.None, false);
            if (lines == null) return false;

            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo           = true,
                ExtraLines         = lines,
                SuppressAxisShapes = true,
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
