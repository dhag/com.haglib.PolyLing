// CameraToolHandler.cs
// ビューポートのカメラ（メインカメラ / 3面カメラ）をギズモで調整する
// IPlayerToolHandler 実装。
//
// モデルの頂点・選択状態には一切触れない。読み書きするのは
// OrbitCameraController（メインカメラ）と OrthoViewController の共有状態
// （3面カメラ）だけ。取得は外部コールバック経由で行うため、本クラスは
// ProjectContext / ModelContext を直接参照しない。
//
// 【ギズモを出すビューポート】
//   メインカメラ調整時 … Perspective 以外（Top/Front/Side）
//   3面カメラ調整時   … Perspective のみ
//   注視点(LookAt)操作 … 4面すべて（「どこからでも移動できる」ため）
//
// GizmoData はリング（姿勢）と軸ギズモ（位置）の同時描画に対応するため
// （PlayerViewportPanel.GizmoData.DrawAxisWithRing）、カメラ操作時は
// 姿勢リングと位置矢印を同時に出す。
//
// 【カメラ向き表示】
//   本体＝直方体（ロールが判る）＋ レンズ＝円錐台（透視）/ 円筒（正射影）を
//   GizmoData.ExtraLines（色付きスクリーン折れ線）として組み立てる。
//   表示専用でヒットテストは行わない。画角は形状に反映しない。
//
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    /// <summary>
    /// カメラ調整ギズモ。調整対象（メイン / 3面）と操作種別（カメラ / 注視点）を切り替える。
    /// </summary>
    public class CameraToolHandler : IPlayerToolHandler, IPlayerGizmoProvider
    {
        /// <summary>調整対象のカメラ。</summary>
        public enum CameraTargetKind
        {
            /// <summary>メインカメラ（左上 Perspective ビュー）。</summary>
            Main,
            /// <summary>3面カメラ（Top / Front / Side。軸の相対関係を固定して連動）。</summary>
            Tri,
        }

        /// <summary>ギズモの操作種別。</summary>
        public enum CameraGizmoOp
        {
            /// <summary>カメラ本体（姿勢リング + 位置矢印）。</summary>
            Camera,
            /// <summary>注視点（Target）の移動。</summary>
            LookAt,
        }

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>    GetToolContext;
        public Func<float>          GetPanelHeight;
        public Action               OnRepaint;

        /// <summary>現在マウスが乗っているビューポート。ギズモの表示可否判定に使う。</summary>
        public Func<PlayerViewport> GetActiveViewport;

        /// <summary>メインカメラのコントローラー。</summary>
        public Func<OrbitCameraController> GetOrbit;

        /// <summary>3面カメラのコントローラー（共有状態にアクセスするための代表1台）。</summary>
        public Func<OrthoViewController> GetTri;

        /// <summary>
        /// 3面カメラのコントローラー3台（0=Top / 1=Front / 2=Side）。
        /// 向き表示は Flip がビューごとに違うため、共有状態だけでは足りず全台必要。
        /// </summary>
        public Func<OrthoViewController[]> GetTriViews;

        /// <summary>対象カメラの再描画要求。フェーズをそのまま EnterCameraChanged へ渡す。</summary>
        public Action<CameraChangePhase> OnCameraPhase;

        /// <summary>値が変わったときに呼ぶ。右ペインの書き戻しとギズモ再描画に使う。</summary>
        public Action OnValueChanged;

        // ================================================================
        // 状態
        // ================================================================

        public CameraTargetKind TargetKind { get; set; } = CameraTargetKind.Main;
        public CameraGizmoOp    GizmoOp    { get; set; } = CameraGizmoOp.Camera;

        // カメラ位置・注視点そのものが編集対象の値なので、ScreenOffset は
        // ずらさず対象位置に描く（作業軸ギズモと同じ扱い）。
        private readonly AxisGizmo       _axisGizmo = new AxisGizmo { ScreenOffset = Vector2.zero };
        private readonly RotateRingGizmo _ringGizmo = new RotateRingGizmo();

        private AxisGizmo.AxisType _hoverAxis = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _dragAxis  = AxisGizmo.AxisType.None;
        private bool               _dragIsRing;

        // ドラッグ開始時のスナップショット（絶対計算の基準）
        private Quaternion _startRotation = Quaternion.identity;

        /// <summary>回転スナップ（度）。0 以下でスナップ無効。</summary>
        public float RotateSnapDeg { get; set; } = 0f;

        // ================================================================
        // ギズモ構成（種別ごとに何を描くか）
        // ================================================================

        /// <summary>姿勢リングを持つか。</summary>
        private bool HasRing => GizmoOp == CameraGizmoOp.Camera;

        /// <summary>
        /// 軸ギズモ（矢印）を持つか。
        /// 3面カメラの「カメラ」操作は位置を独立に持たない（Target とリグ回転から
        /// 3台が決まる）ため、矢印は出さない。
        /// </summary>
        private bool HasAxis =>
            GizmoOp == CameraGizmoOp.LookAt ||
            (GizmoOp == CameraGizmoOp.Camera && TargetKind == CameraTargetKind.Main);

        /// <summary>
        /// 現在のアクティブビューポートにギズモを出してよいか。
        /// 調整対象のカメラ自身のビューにギズモを出しても操作できないため除外する。
        /// </summary>
        private bool IsGizmoViewport()
        {
            var vp = GetActiveViewport?.Invoke();
            if (vp == null) return false;

            // 注視点は4面すべてから動かせるようにする。
            if (GizmoOp == CameraGizmoOp.LookAt) return true;

            bool isPersp = vp.Mode == ViewportMode.Perspective;
            return TargetKind == CameraTargetKind.Main ? !isPersp : isPersp;
        }

        // ================================================================
        // ギズモの中心・向き
        // ================================================================

        /// <summary>
        /// カメラの現在値をギズモへ流し込む。対象が取得できないときは false。
        /// </summary>
        private bool SyncGizmoFromCamera()
        {
            if (TargetKind == CameraTargetKind.Main)
            {
                var orbit = GetOrbit?.Invoke();
                if (orbit == null) return false;

                if (GizmoOp == CameraGizmoOp.LookAt)
                {
                    // 注視点はワールド軸で動かす。
                    _axisGizmo.Center      = orbit.Target;
                    _axisGizmo.Orientation = Quaternion.identity;
                }
                else
                {
                    // 姿勢リングは Target 中心（回転の中心）にカメラのローカル軸で描く。
                    // 位置矢印はカメラ位置にワールド軸で描く。
                    _ringGizmo.Center      = orbit.Target;
                    _ringGizmo.Orientation = CameraRotation(orbit);
                    _axisGizmo.Center      = CameraPosition(orbit);
                    _axisGizmo.Orientation = Quaternion.identity;
                }
                return true;
            }

            var tri = GetTri?.Invoke();
            if (tri == null) return false;

            if (GizmoOp == CameraGizmoOp.LookAt)
            {
                _axisGizmo.Center      = tri.Target;
                _axisGizmo.Orientation = Quaternion.identity;
            }
            else
            {
                // 3面はリグ回転を回す。3視線の直交関係はリグ回転によらず保たれる。
                _ringGizmo.Center      = tri.Target;
                _ringGizmo.Orientation = tri.RigRotation;
            }
            return true;
        }

        /// <summary>
        /// メインカメラの姿勢。OrbitCameraController.ApplyCameraTransform の
        /// 結果（LookAt 後の transform.rotation）と一致する。
        /// </summary>
        private static Quaternion CameraRotation(OrbitCameraController orbit)
            => Quaternion.Euler(orbit.RotX, orbit.RotY, orbit.RotZ);

        /// <summary>メインカメラのワールド位置。</summary>
        private static Vector3 CameraPosition(OrbitCameraController orbit)
            => orbit.Target
             + Quaternion.Euler(orbit.RotX, orbit.RotY, 0f) * (Vector3.back * orbit.Distance);

        // ================================================================
        // ホバー
        // ================================================================

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null || !IsGizmoViewport() || !SyncGizmoFromCamera())
            {
                _hoverAxis = AxisGizmo.AxisType.None;
                return;
            }

            var imgui = ToImgui(screenPos);
            var axis  = AxisGizmo.AxisType.None;

            if (HasRing) axis = _ringGizmo.FindRingAtScreenPos(imgui, ctx);
            if (axis == AxisGizmo.AxisType.None && HasAxis)
                axis = _axisGizmo.FindAxisAtScreenPos(imgui, ctx);

            _hoverAxis = axis;
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
            _dragAxis   = AxisGizmo.AxisType.None;
            _dragIsRing = false;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null || !IsGizmoViewport() || !SyncGizmoFromCamera()) return;

            Vector2 imgui = ToImgui(screenPos);

            if (HasRing)
            {
                var ringAxis = _ringGizmo.FindRingAtScreenPos(imgui, ctx);
                if (ringAxis != AxisGizmo.AxisType.None &&
                    _ringGizmo.BeginAngleDrag(ctx, imgui, ringAxis))
                {
                    _dragAxis      = ringAxis;
                    _dragIsRing    = true;
                    _startRotation = CurrentRotation();
                    OnCameraPhase?.Invoke(CameraChangePhase.DragBegin);
                    return;
                }
            }

            if (!HasAxis) return;

            var hitAxis = _axisGizmo.FindAxisAtScreenPos(imgui, ctx);
            if (hitAxis == AxisGizmo.AxisType.None) return;

            _dragAxis = hitAxis;
            OnCameraPhase?.Invoke(CameraChangePhase.DragBegin);
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (_dragAxis == AxisGizmo.AxisType.None) return;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null || !SyncGizmoFromCamera()) return;

            if (_dragIsRing) DragRotate(screenPos);
            else             DragMove(delta, ctx);

            OnCameraPhase?.Invoke(CameraChangePhase.Dragging);
            OnValueChanged?.Invoke();
            OnRepaint?.Invoke();
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            bool wasDragging = _dragAxis != AxisGizmo.AxisType.None;

            _dragAxis   = AxisGizmo.AxisType.None;
            _dragIsRing = false;
            _ringGizmo.EndAngleDrag();

            if (wasDragging)
            {
                OnCameraPhase?.Invoke(CameraChangePhase.DragEnd);
                OnValueChanged?.Invoke();
            }
            OnRepaint?.Invoke();
        }

        // ================================================================
        // ドラッグ処理
        // ================================================================

        /// <summary>ドラッグ開始時に保存する回転（対象別）。</summary>
        private Quaternion CurrentRotation()
        {
            if (TargetKind == CameraTargetKind.Main)
            {
                var orbit = GetOrbit?.Invoke();
                return orbit != null ? CameraRotation(orbit) : Quaternion.identity;
            }
            var tri = GetTri?.Invoke();
            return tri != null ? tri.RigRotation : Quaternion.identity;
        }

        private void DragMove(Vector2 screenDelta, ToolContext ctx)
        {
            // screenDelta はパネルの ToViewportCoord 系（+Y が画面上）。
            // ComputeFreeDelta はこの系をそのまま要求するが、ComputeAxisDelta は
            // WorldToScreenPos 系（+Y が画面下）を要求するため Y を反転して渡す。
            Vector3 worldDelta = (_dragAxis == AxisGizmo.AxisType.Center)
                ? _axisGizmo.ComputeFreeDelta(screenDelta, ctx)
                : _axisGizmo.ComputeAxisDelta(
                    new Vector2(screenDelta.x, -screenDelta.y), _dragAxis, ctx);

            if (worldDelta == Vector3.zero) return;

            if (TargetKind == CameraTargetKind.Tri)
            {
                // 3面は共有 Target を動かす（3台が連動して平行移動する）。
                var tri = GetTri?.Invoke();
                if (tri == null) return;
                tri.Target = tri.Target + worldDelta;
                return;
            }

            var orbit = GetOrbit?.Invoke();
            if (orbit == null) return;

            if (GizmoOp == CameraGizmoOp.LookAt)
            {
                // 注視点だけ動かす。カメラ位置は固定し、そこから新しい注視点を見る
                // 姿勢・距離に組み直す。
                Vector3 camPos = CameraPosition(orbit);
                orbit.Target = orbit.Target + worldDelta;
                AimFrom(orbit, camPos);
            }
            else
            {
                // カメラ位置だけ動かす。注視点は固定。
                AimFrom(orbit, CameraPosition(orbit) + worldDelta);
            }
        }

        private void DragRotate(Vector2 screenPos)
        {
            float deltaDeg = _ringGizmo.ComputeAngleDeltaDeg(ToImgui(screenPos));
            if (RotateSnapDeg > 0f)
                deltaDeg = Mathf.Round(deltaDeg / RotateSnapDeg) * RotateSnapDeg;

            // リングは対象のローカル軸まわりに描かれている。開始姿勢を基準に
            // ローカル軸まわりの回転を右から掛けることで見た目のリングと一致する。
            // 開始姿勢から絶対で計算するため、フレーム誤差が累積しない。
            Vector3    localAxis = RotateRingGizmo.AxisVector(_dragAxis);
            Quaternion rot       = _startRotation * Quaternion.AngleAxis(deltaDeg, localAxis);

            if (TargetKind == CameraTargetKind.Tri)
            {
                var tri = GetTri?.Invoke();
                if (tri == null) return;
                tri.RigRotation = rot;
                return;
            }

            var orbit = GetOrbit?.Invoke();
            if (orbit == null) return;
            ApplyRotation(orbit, rot);
        }

        // ================================================================
        // メインカメラのパラメータ組み直し
        // ================================================================

        /// <summary>
        /// カメラ位置 camPos から Target を見る姿勢・距離へ組み直す。
        /// OrbitCameraController は position を Target + Euler(RotX,RotY,0)*back*Distance
        /// で決めるため、その逆算を行う。
        /// </summary>
        private static void AimFrom(OrbitCameraController orbit, Vector3 camPos)
        {
            Vector3 dir  = camPos - orbit.Target;
            float   dist = dir.magnitude;
            if (dist < 1e-4f) return;

            Vector3 n = dir / dist;

            // Euler(x,y,0) * back = (-sin y cos x, sin x, -cos y cos x)
            orbit.RotX     = Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) * Mathf.Rad2Deg;
            orbit.RotY     = Mathf.Atan2(-n.x, -n.z) * Mathf.Rad2Deg;
            orbit.Distance = dist;
        }

        /// <summary>
        /// カメラ姿勢 rot を RotX / RotY / RotZ へ分解して設定する。
        /// Target / Distance は変えない（Target を中心に回り込む）。
        /// </summary>
        private static void ApplyRotation(OrbitCameraController orbit, Quaternion rot)
        {
            Vector3 e = rot.eulerAngles;
            orbit.RotX = Normalize180(e.x);
            orbit.RotY = Normalize180(e.y);
            orbit.RotZ = Normalize180(e.z);
        }

        /// <summary>0..360 の角度を (-180, 180] へ直す。</summary>
        private static float Normalize180(float deg)
        {
            deg -= 360f * Mathf.Floor((deg + 180f) / 360f);
            return deg <= -180f ? deg + 360f : deg;
        }

        // ================================================================
        // IPlayerGizmoProvider
        // ================================================================

        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;

            if (ctx == null || !IsGizmoViewport() || !SyncGizmoFromCamera()) return false;
            if (!HasRing && !HasAxis) return false;

            var hovered = _dragAxis != AxisGizmo.AxisType.None ? _dragAxis : _hoverAxis;

            data.HasGizmo     = true;
            data.HoveredAxis  = hovered;
            data.DraggingAxis = _dragAxis;

            if (HasRing)
            {
                data.IsRingStyle      = true;
                data.RingX            = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.X);
                data.RingY            = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Y);
                data.RingZ            = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Z);
                // 姿勢リングと位置矢印は同時に描く（メインカメラのみ矢印あり）。
                data.DrawAxisWithRing = HasAxis;
            }

            if (HasAxis)
            {
                _axisGizmo.HoveredAxis  = hovered;
                _axisGizmo.DraggingAxis = _dragAxis;
                _axisGizmo.GetScreenPositions(ctx, out var o, out var xe, out var ye, out var ze);

                data.Origin = o; data.XEnd = xe; data.YEnd = ye; data.ZEnd = ze;
                // 注視点はダイヤ型、カメラ位置は矢印で区別する。
                data.IsDiamondStyle = GizmoOp == CameraGizmoOp.LookAt;
            }

            // 注視点操作中は、参考としてカメラ位置をダイヤで示す（操作対象外）。
            if (TargetKind == CameraTargetKind.Main && GizmoOp == CameraGizmoOp.LookAt)
            {
                var orbit = GetOrbit?.Invoke();
                if (orbit != null)
                {
                    data.HasPivotGizmo = true;
                    data.PivotOrigin   = ctx.WorldToScreen(CameraPosition(orbit));
                }
            }

            data.ExtraLines = BuildCameraBodies(ctx);
            return true;
        }

        // ================================================================
        // カメラ向き表示（直方体＋円錐台/円筒）
        // ================================================================

        /// <summary>メインカメラの表示色。</summary>
        private static readonly Color MainCameraColor = new Color(1f, 1f, 1f, 0.9f);

        /// <summary>3面カメラの表示色（0=Top / 1=Front / 2=Side）。</summary>
        private static readonly Color[] TriCameraColors =
        {
            new Color(1f,   0.85f, 0.2f, 0.9f),   // Top
            new Color(0.3f, 0.9f,  1f,   0.9f),   // Front
            new Color(1f,   0.4f,  0.9f, 0.9f),   // Side
        };

        /// <summary>
        /// 3面カメラの表示位置。実距離（正射影は固定 100、透視は画面高依存）を
        /// そのまま使うとメインカメラの画面外へ出るため、見やすい距離に置く。
        /// 共有 Target から視線方向の逆へこの係数 × ctx.CameraDistance だけ引く。
        /// </summary>
        private const float TriCameraPlaceFactor = 0.45f;

        /// <summary>
        /// 現在のビューポートに描くカメラ本体（向き表示）を組み立てる。
        /// 対象が無いときは null。
        /// </summary>
        private PlayerViewportPanel.ScreenPolyline[] BuildCameraBodies(ToolContext ctx)
        {
            var vp = GetActiveViewport?.Invoke();
            if (vp == null) return null;

            bool isPersp = vp.Mode == ViewportMode.Perspective;
            var  list    = new List<PlayerViewportPanel.ScreenPolyline>();

            if (TargetKind == CameraTargetKind.Main)
            {
                // 自分自身のビュー（Perspective）には出さない。視点そのものの位置になるため。
                if (isPersp) return null;

                var orbit = GetOrbit?.Invoke();
                if (orbit == null) return null;

                AppendCameraBody(
                    list, ctx,
                    CameraPosition(orbit), CameraRotation(orbit),
                    !orbit.Orthographic, MainCameraColor);
            }
            else
            {
                // 3面カメラはメインカメラの画面にだけ出す。
                if (!isPersp) return null;

                var views = GetTriViews?.Invoke();
                var tri   = GetTri?.Invoke();
                if (views == null || tri == null) return null;

                float dist = Mathf.Max(1e-4f, ctx.CameraDistance) * TriCameraPlaceFactor;

                for (int i = 0; i < views.Length && i < TriCameraColors.Length; i++)
                {
                    var v = views[i];
                    if (v == null) continue;

                    Quaternion rot = v.CurrentViewRotation();
                    Vector3    eye = tri.Target - rot * Vector3.forward * dist;

                    AppendCameraBody(list, ctx, eye, rot, tri.Perspective, TriCameraColors[i]);
                }
            }

            return list.Count > 0 ? list.ToArray() : null;
        }

        /// <summary>
        /// カメラ本体を1台分追加する。
        /// eye は視点位置（レンズ前面）、rot はカメラ姿勢（ロールを含む）。
        /// perspective のときレンズは前方へ広がる円錐台、正射影のときは円筒。
        /// 画角は形状に反映しない。寸法は ctx.CameraDistance 比例で、
        /// 倍率が変わってもほぼ一定の画面サイズになる。
        /// </summary>
        private static void AppendCameraBody(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx,
            Vector3 eye, Quaternion rot, bool perspective, Color color)
        {
            Vector3 fwd   = rot * Vector3.forward;
            Vector3 right = rot * Vector3.right;
            Vector3 up    = rot * Vector3.up;

            float s = Mathf.Max(1e-4f, ctx.CameraDistance);

            float lensLen = 0.050f * s;
            float rBack   = 0.028f * s;
            float rFront  = perspective ? 0.050f * s : rBack;
            float bodyLen = 0.090f * s;
            float halfW   = 0.050f * s;
            float halfH   = 0.035f * s;

            Vector3 lensBack = eye      - fwd * lensLen;
            Vector3 boxBack  = lensBack - fwd * bodyLen;

            // ── レンズ（円錐台 / 円筒） ────────────────────────────
            AppendCircle(dst, ctx, eye,      right, up, rFront, color);
            AppendCircle(dst, ctx, lensBack, right, up, rBack,  color);
            for (int i = 0; i < 4; i++)
            {
                float   a = Mathf.PI * 0.5f * i;
                Vector3 d = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                AppendSegment(dst, ctx, lensBack + d * rBack, eye + d * rFront, color);
            }

            // ── 本体（直方体） ────────────────────────────────────
            // right / up に沿った矩形なので、ロール（RotZ）が姿勢として見える。
            var front = RectCorners(lensBack, right, up, halfW, halfH);
            var back  = RectCorners(boxBack,  right, up, halfW, halfH);
            AppendClosedPoly(dst, ctx, front, color);
            AppendClosedPoly(dst, ctx, back,  color);
            for (int i = 0; i < 4; i++)
                AppendSegment(dst, ctx, back[i], front[i], color);
        }

        /// <summary>矩形4隅（右上→左上→左下→右下）。</summary>
        private static Vector3[] RectCorners(
            Vector3 center, Vector3 right, Vector3 up, float halfW, float halfH)
        {
            return new[]
            {
                center + right * halfW + up * halfH,
                center - right * halfW + up * halfH,
                center - right * halfW - up * halfH,
                center + right * halfW - up * halfH,
            };
        }

        private const int CameraCircleSegments = 16;

        private static void AppendCircle(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx,
            Vector3 center, Vector3 u, Vector3 v, float radius, Color color)
        {
            var pts = new Vector2[CameraCircleSegments + 1];
            for (int i = 0; i < CameraCircleSegments; i++)
            {
                float a = 2f * Mathf.PI * i / CameraCircleSegments;
                pts[i] = ctx.WorldToScreen(
                    center + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius);
            }
            pts[CameraCircleSegments] = pts[0];
            dst.Add(new PlayerViewportPanel.ScreenPolyline
            {
                Points = pts, Color = color, Width = 1.4f,
            });
        }

        private static void AppendClosedPoly(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx,
            Vector3[] corners, Color color)
        {
            var pts = new Vector2[corners.Length + 1];
            for (int i = 0; i < corners.Length; i++) pts[i] = ctx.WorldToScreen(corners[i]);
            pts[corners.Length] = pts[0];
            dst.Add(new PlayerViewportPanel.ScreenPolyline
            {
                Points = pts, Color = color, Width = 1.4f,
            });
        }

        private static void AppendSegment(
            List<PlayerViewportPanel.ScreenPolyline> dst, ToolContext ctx,
            Vector3 a, Vector3 b, Color color)
        {
            dst.Add(new PlayerViewportPanel.ScreenPolyline
            {
                Points = new[] { ctx.WorldToScreen(a), ctx.WorldToScreen(b) },
                Color  = color,
                Width  = 1.4f,
            });
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
