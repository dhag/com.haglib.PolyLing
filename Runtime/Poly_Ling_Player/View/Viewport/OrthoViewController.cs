// OrthoViewController.cs
// Top / Front 正投影ビュー用のパン・ズームコントローラー。
// 右ボタンドラッグ・中ボタンドラッグ → パン
// スクロール → OrthographicSize ズーム
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;

namespace Poly_Ling.Player
{
    public enum OrthoViewDirection { Top, Front, Side }

    /// <summary>
    /// 正投影カメラ用パン・ズームコントローラー。
    /// <see cref="IMouseEventSource"/> のイベントを購読する。
    /// 毎フレーム <see cref="ApplyCameraTransform"/> を呼ぶこと。
    /// </summary>
    public class OrthoViewController
    {
        // ================================================================
        // 設定
        // ================================================================

        public float PanSensitivity  = 0.002f;
        public float ZoomSensitivity = 0.1f;
        public float OrthoSizeMin    = 0.05f;
        public float OrthoSizeMax    = 200f;

        // ================================================================
        // コールバック
        // ================================================================

        /// <summary>
        /// カメラドラッグ（パン）開始時に呼ばれる。
        /// UnifiedSystemAdapter.EnterCameraDragging() の呼び出しに使う。
        /// OnCameraChanged より先に発火する。
        /// </summary>
        public System.Action OnCameraDragBegin;

        /// <summary>
        /// カメラドラッグ（パン）終了時に呼ばれる。
        /// UnifiedSystemAdapter.ExitCameraDragging() の呼び出しに使う。
        /// OnCameraChanged より先に発火する。
        /// </summary>
        public System.Action OnCameraDragEnd;

        /// <summary>
        /// カメラパラメータ確定時（パン・ズーム終了後）に呼ばれる。
        /// UnifiedSystemAdapter.UpdateFrame() の呼び出しに使う。
        /// </summary>
        public System.Action OnCameraChanged;

        // ================================================================
        // 状態
        // ================================================================

        public Vector3 Target    { get; private set; } = Vector3.zero;
        public float   OrthoSize { get; private set; } = 1f;

        private readonly OrthoViewDirection _direction;
        private bool _isDragging;

        // ================================================================
        // 初期化
        // ================================================================

        public OrthoViewController(OrthoViewDirection direction)
        {
            _direction = direction;
        }

        public void ResetToMesh(Bounds bounds)
        {
            Target   = bounds.center;
            OrthoSize = Mathf.Clamp(bounds.size.magnitude * 0.6f, OrthoSizeMin, OrthoSizeMax);
        }

        // ================================================================
        // IMouseEventSource 接続
        // ================================================================

        public void Connect(IMouseEventSource source)
        {
            source.OnDragBegin += OnDragBegin;
            source.OnDrag      += OnDrag;
            source.OnDragEnd   += OnDragEnd;
            source.OnScroll    += OnScroll;
        }

        public void Disconnect(IMouseEventSource source)
        {
            if (source == null) return;
            source.OnDragBegin -= OnDragBegin;
            source.OnDrag      -= OnDrag;
            source.OnDragEnd   -= OnDragEnd;
            source.OnScroll    -= OnScroll;
        }

        // ================================================================
        // カメラ配置
        // ================================================================

        public void ApplyCameraTransform(Camera cam)
        {
            if (cam == null) return;
            cam.orthographic     = true;
            cam.orthographicSize = OrthoSize;

            const float camDist = 100f; // 十分遠い位置に置く（クリッピング回避）

            switch (_direction)
            {
                case OrthoViewDirection.Top:
                    cam.transform.position = Target + Vector3.up * camDist;
                    cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    break;

                case OrthoViewDirection.Front:
                    // PMXモデルは-Z向き。正面(Front)ビューはモデルの正面を見るため
                    // カメラを -Z 側に置き +Z 方向を向く
                    cam.transform.position = Target + Vector3.back * camDist;
                    cam.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                    break;

                case OrthoViewDirection.Side:
                    // Side: +X 側に置き -X 方向を向く（右側面ビュー）
                    cam.transform.position = Target + Vector3.right * camDist;
                    cam.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
                    break;
            }
        }

        // ================================================================
        // イベントハンドラー
        // ================================================================

        private void OnDragBegin(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if (btn != 1 && btn != 2) return;
            _isDragging = true;
            OnCameraDragBegin?.Invoke();
        }

        private void OnDrag(int btn, Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            // 右ボタン(1) または 中ボタン(2) → パン
            if (btn != 1 && btn != 2) return;

            Vector3 panDelta;
            switch (_direction)
            {
                case OrthoViewDirection.Top:
                    // Top: X→X, Y（スクリーン上下）→ Z
                    panDelta = new Vector3(-delta.x, 0f, -delta.y) * OrthoSize * PanSensitivity;
                    break;
                case OrthoViewDirection.Front:
                default:
                    // Front: カメラ -Z 側、+Z 向き。
                    // スクリーン右 → Target.x 減少（カメラが右に動く＝シーンが左に見える）
                    panDelta = new Vector3(-delta.x, -delta.y, 0f) * OrthoSize * PanSensitivity;
                    break;
                case OrthoViewDirection.Side:
                    // Side: カメラ +X 側、-X 向き。
                    // スクリーン右 → Target.z 減少
                    panDelta = new Vector3(0f, -delta.y, -delta.x) * OrthoSize * PanSensitivity;
                    break;
            }
            Target += panDelta;
        }

        private void OnDragEnd(int btn, Vector2 screenPos, ModifierKeys mods)
        {
            if (btn != 1 && btn != 2) return;
            if (!_isDragging) return;
            _isDragging = false;
            OnCameraDragEnd?.Invoke();
            OnCameraChanged?.Invoke();
        }

        private void OnScroll(float scroll, ModifierKeys mods)
        {
            OrthoSize *= 1f - scroll * ZoomSensitivity;
            OrthoSize  = Mathf.Clamp(OrthoSize, OrthoSizeMin, OrthoSizeMax);
        }
    }
}
