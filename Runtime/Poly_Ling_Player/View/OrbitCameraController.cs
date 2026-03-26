// OrbitCameraController.cs
// カメラオービット・パン・ズーム操作を担うクラス。
// Input.* は直接読まず、PlayerMouseDispatcher のイベントを購読する。
// - 右ボタンドラッグ (btn=1) → オービット
// - 中ボタンドラッグ (btn=2) → パン
// - スクロール           → ズーム
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;

namespace Poly_Ling.Player
{
    /// <summary>
    /// カメラオービット・パン・ズーム操作を担うクラス。
    /// MonoBehaviour に依存しない純粋クラス。
    /// Viewer から <see cref="Connect"/> / <see cref="Disconnect"/> で
    /// <see cref="PlayerMouseDispatcher"/> に接続する。
    /// 毎フレーム <see cref="ApplyCameraTransform"/> を呼ぶこと。
    /// </summary>
    public class OrbitCameraController
    {
        // ================================================================
        // 定数
        // ================================================================

        public const float DefaultOrbitSensitivity = 0.5f;
        public const float DefaultZoomSensitivity  = 0.05f;
        public const float DefaultPanSensitivity   = 0.002f;
        public const float DefaultZoomMin          = 0.05f;
        public const float DefaultZoomMax          = 100f;

        // ================================================================
        // パラメータ
        // ================================================================

        public float OrbitSensitivity = DefaultOrbitSensitivity;
        public float ZoomSensitivity  = DefaultZoomSensitivity;
        public float PanSensitivity   = DefaultPanSensitivity;
        public float ZoomMin          = DefaultZoomMin;
        public float ZoomMax          = DefaultZoomMax;

        // ================================================================
        // 状態（読み取り専用公開）
        // ================================================================

        public float   RotX     { get; private set; } =  20f;
        public float   RotY     { get; private set; } =   0f;
        public float   Distance { get; private set; } =   3f;
        public Vector3 Target   { get; private set; } = Vector3.zero;

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>
        /// カメラパラメータが確定したときに呼ばれる。
        ///
        /// 【呼び出しタイミング】
        ///   - 右/中ボタンドラッグ終了（オービット・パン操作が完了したとき）
        ///   - ResetToMesh()（ファイルロードやリモートフェッチ後のカメラリセット）
        ///
        /// 【用途】
        ///   PolyLingPlayerViewer はこのコールバックで
        ///   UnifiedSystemAdapter.UpdateFrame() を1回だけ呼ぶ。
        ///   UpdateFrame はカメラパラメータをGPUヒットテストシステムに設定する
        ///   唯一の口であり、パラメータが変化したタイミングにのみ呼ぶ。
        ///   毎フレーム呼ぶのは禁忌（1FPS以下になる）。
        /// </summary>
        public System.Action OnCameraChanged;

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

        /// <summary>
        /// <see cref="PlayerMouseDispatcher"/> のイベントを購読する。
        /// Viewer の Start / Awake から呼ぶ。
        /// </summary>
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
            if (dispatcher == null) return;
            dispatcher.OnDragBegin -= OnDragBegin;
            dispatcher.OnDrag      -= OnDrag;
            dispatcher.OnDragEnd   -= OnDragEnd;
            dispatcher.OnScroll    -= OnScroll;
        }

        /// <summary>
        /// 毎フレーム呼ぶ。Camera transform に RotX/RotY/Distance/Target を反映する。
        /// </summary>
        public void ApplyCameraTransform(Camera cam)
        {
            if (cam == null) return;
            Quaternion camRot      = Quaternion.Euler(RotX, RotY, 0f);
            cam.transform.position = Target + camRot * (Vector3.back * Distance);
            cam.transform.LookAt(Target);
        }

        // ================================================================
        // イベントハンドラー
        // ================================================================

        private void OnDragBegin(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if      (btn == 1) _isOrbiting = true;
            else if (btn == 2) _isPanning  = true;
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
    }
}
