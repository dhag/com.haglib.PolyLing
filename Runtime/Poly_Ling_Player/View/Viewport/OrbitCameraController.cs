// OrbitCameraController.cs
// オービットカメラ操作コントローラー。
// - 右ボタンドラッグ (btn=1) → オービット
// - 中ボタンドラッグ (btn=2) → パン
// - スクロール → ズーム
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Core;

namespace Poly_Ling.Player
{
    public class OrbitCameraController
    {
        // ================================================================
        // 感度定数
        // ================================================================

        public const float DefaultOrbitSensitivity = 0.5f;
        public const float DefaultZoomSensitivity  = 0.05f;
        public const float DefaultPanSensitivity   = 0.002f;
        public const float DefaultZoomMin          = 0.05f;
        public const float DefaultZoomMax          = 100f;

        /// <summary>
        /// 正投影時にカメラを Target から引く固定距離。
        /// 正投影では投影 XY がカメラの前後位置に依存しないため、
        /// 近クリップ面（nearClipPlane）による手前側の切り落としを避ける目的で
        /// Distance とは切り離した固定値を使う。
        /// OrthoViewController の camDist と同値。
        /// </summary>
        public const float OrthoCameraPullback     = 100f;

        // ================================================================
        // 公開パラメータ
        // ================================================================

        public float OrbitSensitivity = DefaultOrbitSensitivity;
        public float ZoomSensitivity  = DefaultZoomSensitivity;
        public float PanSensitivity   = DefaultPanSensitivity;
        public float ZoomMin          = DefaultZoomMin;
        public float ZoomMax          = DefaultZoomMax;

        // ================================================================
        // カメラパラメータ
        // ================================================================

        // カメラ調整ツール（CameraToolHandler / PlayerCameraSubPanel）から
        // 数値・ギズモで直接書き込むため公開セッタを持つ。範囲制限は
        // ドラッグ経路と同じ値をセッタ側に集約する。
        private float   _rotX     =  20f;
        private float   _rotY     = 180f;
        private float   _rotZ     =   0f;
        private float   _distance =   3f;

        public float RotX
        {
            get => _rotX;
            set => _rotX = Mathf.Clamp(value, -89f, 89f);
        }

        // 既定は +Z 側からの視点。モデルは Unity 規約（正面 = +Z）で扱うため、
        // カメラを +Z 側に置いて -Z 方向を見ることで正面が映る。
        public float RotY
        {
            get => _rotY;
            set => _rotY = NormalizeDeg(value);
        }

        /// <summary>カメラのロール（Z軸周り・視線軸回転）。</summary>
        public float RotZ
        {
            get => _rotZ;
            set => _rotZ = NormalizeDeg(value);
        }

        public float Distance
        {
            get => _distance;
            set => _distance = Mathf.Clamp(value, ZoomMin, ZoomMax);
        }

        public Vector3 Target { get; set; } = Vector3.zero;

        /// <summary>
        /// 透視投影時の画角（度）。ApplyCameraTransform で Camera.fieldOfView に反映する。
        /// 正投影時の orthographicSize もこの値から算出される。
        /// </summary>
        public float Fov = 60f;

        /// <summary>角度を (-180, 180] へ正規化する。回転そのものは変化しない。</summary>
        private static float NormalizeDeg(float deg)
        {
            deg -= 360f * Mathf.Floor((deg + 180f) / 360f);
            return deg <= -180f ? deg + 360f : deg;
        }

        /// <summary>
        /// true のとき透視カメラを正投影(orthographic)で描画する。
        /// 視点(RotX/RotY/RotZ/Target/Distance)は透視と共有し、
        /// orthographicSize は現在の Distance と fov から算出する。
        /// </summary>
        public bool    Orthographic { get; set; } = false;

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>
        /// カメラドラッグ終了時に発火する。
        /// アダプターへの1回のUpdateFrame要求に使う。
        /// </summary>
        public System.Action OnCameraChanged;

        /// <summary>
        /// OnCameraChanged より先に発火する。
        /// カメラドラッグ開始時（UpdateFrame の停止に使う）。
        /// </summary>
        public System.Action OnCameraDragBegin;

        /// <summary>
        /// カメラドラッグ中（連続移動中）に発火する軽量コールバック。
        /// Phase 1: ApplyCameraTransform + PresentAll など軽量な更新のみを実行する想定。
        /// UpdateFrame（GPU ヒットテスト等の重い処理）はドラッグ終了時の OnCameraChanged で行う。
        /// </summary>
        public System.Action OnCameraDragging;

        // ================================================================
        // 内部状態
        // ================================================================

        // オービット（右ボタン）
        private bool    _isOrbiting;

        // パン（中ボタン）
        private bool    _isPanning;

        // ================================================================
        // 初期化
        // ================================================================

        public OrbitCameraController()
        {
            // 拡大限界は外部CSV（DisplaySettings.csv）から取得する。
            ZoomMin = DisplaySettings.GetF(DisplaySettings.KeyCameraZoomDistanceMin);
        }

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>
        /// カメラ位置をバウンディングボックスに合わせてリセットする。
        /// </summary>
        public void ResetToMesh(Bounds bounds)
        {
            Target   = bounds.center;
            Distance = UnityEngine.Mathf.Clamp(bounds.size.magnitude * 1.5f, ZoomMin, ZoomMax);
            // カメラパラメータが確定したのでアダプターへの反映を要求
            OnCameraChanged?.Invoke();
        }

        /// <summary>カメラターゲット位置を設定する（ボーンフォーカス等に使用）。</summary>
        public void SetTarget(Vector3 target)
        {
            Target = target;
            OnCameraChanged?.Invoke();
        }

        /// <summary>
        /// 視線を反転する（Target を挟んで反対側へ回り込む）。
        /// Distance / Target / RotZ は変えない。
        /// </summary>
        public void FlipView()
        {
            RotX = -RotX;
            RotY = RotY + 180f;
        }

        // ================================================================
        // IMouseEventSource 接続
        // ================================================================

        public void Connect(IMouseEventSource dispatcher)
        {
            dispatcher.OnDragBegin += OnDragBegin;
            dispatcher.OnDrag      += OnDrag;
            dispatcher.OnDragEnd   += OnDragEnd;
            dispatcher.OnScroll    += OnScroll;
        }

        /// <summary>
        /// イベント購読を解除する。
        /// Viewer の OnDestroy から呼ぶ。
        /// </summary>
        public void Disconnect(IMouseEventSource dispatcher)
        {
            dispatcher.OnDragBegin -= OnDragBegin;
            dispatcher.OnDrag      -= OnDrag;
            dispatcher.OnDragEnd   -= OnDragEnd;
            dispatcher.OnScroll    -= OnScroll;
        }

        // ================================================================
        // カメラ transform 更新（毎フレーム呼ぶ）
        // ================================================================

        /// <summary>
        /// 毎フレーム呼ぶ。Camera transform に RotX/RotY/Distance/Target を反映する。
        /// </summary>
        public void ApplyCameraTransform(Camera cam)
        {
            if (cam == null) return;
            Quaternion camRot = Quaternion.Euler(RotX, RotY, 0f);
            // 正投影時はカメラを固定距離だけ引く。正投影では投影 XY が前後位置に
            // 依存しないため見かけは変わらず、近クリップ面による手前側の
            // 切り落とし（拡大時に形状が消える）だけが解消する。
            float posDistance = Orthographic ? OrthoCameraPullback : Distance;
            cam.transform.position = Target + camRot * (Vector3.back * posDistance);
            // ロール（RotZ）は視線軸周りの up ベクトル回転で反映する。
            //   Euler(RotX,RotY,RotZ) = Euler(RotX,RotY,0) * Rz(RotZ) なので、
            //   その up は「視線軸周りに RotZ 回した up」に一致する。
            Vector3 up = Quaternion.Euler(RotX, RotY, RotZ) * Vector3.up;
            cam.transform.LookAt(Target, up);

            // 透視／正投影の切替。
            // 正投影時は、現在の Distance と fov から見かけのスケールが
            // 一致する orthographicSize を算出する（Target 平面での半画面高）。
            cam.orthographic = Orthographic;
            cam.fieldOfView  = Mathf.Clamp(Fov, 1f, 179f);
            if (Orthographic)
            {
                float halfFovRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                cam.orthographicSize = Mathf.Max(0.0001f, Distance * Mathf.Tan(halfFovRad));
            }
        }

        // ================================================================
        // カメラ初期位置リセット
        // ================================================================

        public void ResetToMesh(Bounds bounds, float zoomMin, float zoomMax)
        {
            Target   = bounds.center;
            Distance = Mathf.Clamp(bounds.size.magnitude * 1.5f, zoomMin, zoomMax);
            OnCameraChanged?.Invoke();
        }

        // ================================================================
        // IMouseEventSource 経由のイベントハンドラ
        // ================================================================

        private void OnDragBegin(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if      (btn == 1) { _isOrbiting = true; OnCameraDragBegin?.Invoke(); }
            else if (btn == 2) { _isPanning  = true; OnCameraDragBegin?.Invoke(); }
        }

        private void OnDrag(int btn, Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            bool changed = false;
            // 粗微動: Shift=粗動 / Ctrl=微動。倍率は DisplaySettings.csv のプリセット値。
            float speed = CameraSpeedModifier.Factor(mods);
            if (btn == 1 && _isOrbiting)
            {
                if (mods.Alt)
                {
                    // Alt＋右ドラッグの左右移動 → カメラのZ軸周り回転（ロール）。
                    RotZ += delta.x * OrbitSensitivity * speed;
                }
                else
                {
                    RotY  += delta.x * OrbitSensitivity * speed;
                    RotX  -= delta.y * OrbitSensitivity * speed;
                    RotX   = Mathf.Clamp(RotX, -89f, 89f);
                }
                changed = true;
            }
            else if (btn == 2 && _isPanning)
            {
                Quaternion rot      = Quaternion.Euler(RotX, RotY, 0f);
                float      panScale = Distance * PanSensitivity * speed;
                Target -= rot * Vector3.right * delta.x * panScale;
                Target -= rot * Vector3.up    * delta.y * panScale;
                changed = true;
            }

            // Phase 1: ドラッグ中はフレーム駆動で transform 反映していたが
            // Tick 廃止に伴い、軽量コールバックで event 駆動化する。
            if (changed) OnCameraDragging?.Invoke();
        }

        private void OnDragEnd(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            bool wasCameraOp = false;
            if      (btn == 1) { _isOrbiting = false; wasCameraOp = true; }
            else if (btn == 2) { _isPanning  = false; wasCameraOp = true; }

            if (!wasCameraOp) return;

            // カメラドラッグ終了 → パラメータ確定 → アダプター更新を要求。
            // UpdateFrame はこのコールバック経由で1回だけ呼ばれる。
            OnCameraChanged?.Invoke();
        }

        private void OnScroll(float scroll, ModifierKeys mods)
        {
            // 粗微動: Shift=粗動 / Ctrl=微動。倍率は DisplaySettings.csv のプリセット値。
            // 倍率が大きいと 1-scroll*sens*speed が 0 以下になり距離の符号が反転するため、
            // 縮尺係数に下限を設ける。
            float zoomScale = Mathf.Max(0.01f, 1f - scroll * ZoomSensitivity * CameraSpeedModifier.Factor(mods));
            Distance *= zoomScale;
            Distance  = Mathf.Clamp(Distance, ZoomMin, ZoomMax);
            // Phase 1: スクロールは単発イベントのため、フル更新（UpdateFrame 含む）を
            // 伴う OnCameraChanged を発火する。
            OnCameraChanged?.Invoke();
        }

        // ================================================================
        // Direct API（IMouseEventSource を経由しない直接操作）
        // ================================================================

        /// <summary>オービット（回転）を直接適用する。delta はスクリーンピクセル差分。</summary>
        public void SimulateOrbit(float deltaX, float deltaY)
        {
            RotY += deltaX * OrbitSensitivity;
            RotX -= deltaY * OrbitSensitivity;
            RotX  = Mathf.Clamp(RotX, -89f, 89f);
        }

        /// <summary>ズームを直接適用する。scroll は -WheelEvent.delta.y * 0.1f 相当の値。</summary>
        public void SimulateScroll(float scroll)
        {
            Distance *= 1f - scroll * ZoomSensitivity;
            Distance  = Mathf.Clamp(Distance, ZoomMin, ZoomMax);
        }

        /// <summary>パンを直接適用する。delta はスクリーンピクセル差分。</summary>
        public void SimulatePan(float deltaX, float deltaY)
        {
            Quaternion rot      = Quaternion.Euler(RotX, RotY, 0f);
            float      panScale = Distance * PanSensitivity;
            Target -= rot * Vector3.right * deltaX * panScale;
            Target -= rot * Vector3.up    * deltaY * panScale;
        }
    }
}
