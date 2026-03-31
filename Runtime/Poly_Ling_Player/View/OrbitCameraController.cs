// OrbitCameraController.cs
// オービットカメラ操作コントローラー。
// - 右ボタンドラッグ (btn=1) → オービット
// - 中ボタンドラッグ (btn=2) → パン
// - スクロール → ズーム
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;

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

        public float   RotX     { get; private set; } =  20f;
        public float   RotY     { get; private set; } =   0f;
        public float   Distance { get; private set; } =   3f;
        public Vector3 Target   { get; private set; } = Vector3.zero;

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

        // ================================================================
        // 内部状態
        // ================================================================

        // オービット（右ボタン）
        private bool    _isOrbiting;

        // パン（中ボタン）
        private bool    _isPanning;

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
            cam.transform.position = Target + camRot * (Vector3.back * Distance);
            cam.transform.LookAt(Target);
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
            if (btn == 1 && _isOrbiting)
            {
                RotY  += delta.x * OrbitSensitivity;
                RotX  -= delta.y * OrbitSensitivity;
                RotX   = Mathf.Clamp(RotX, -89f, 89f);
            }
            else if (btn == 2 && _isPanning)
            {
                Quaternion rot      = Quaternion.Euler(RotX, RotY, 0f);
                float      panScale = Distance * PanSensitivity;
                Target -= rot * Vector3.right * delta.x * panScale;
                Target -= rot * Vector3.up    * delta.y * panScale;
            }
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
            Distance *= 1f - scroll * ZoomSensitivity;
            Distance  = Mathf.Clamp(Distance, ZoomMin, ZoomMax);
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
