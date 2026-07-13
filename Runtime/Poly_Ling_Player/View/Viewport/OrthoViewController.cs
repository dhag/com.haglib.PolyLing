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
    /// Top / Side / Front の3正投影ビューで共有する視点状態（連動用）。
    /// 同一インスタンスを各 <see cref="OrthoViewController"/> に注入すると、
    /// いずれかのパン／ズーム操作が3ビュー全てに反映される。
    /// </summary>
    public sealed class OrthoViewSharedState
    {
        public Vector3 Target = Vector3.zero;

        // ビューポート高さに依存しない共有ズーム：スクリーン1pxあたりのワールド高さ。
        // 各ビューの orthographicSize = WorldHeightPerPixel × pixelHeight ÷ 2。
        // これにより高さの異なるビュー間でも見かけのズーム（px/world）が一致する。
        public float WorldHeightPerPixel = 0.01f;

        // ResetToMesh の遅延解決用。pixelHeight 確定時に WorldHeightPerPixel へ変換する
        // 目標ワールド半高さ（<0 は解決不要）。
        public float PendingResetHalfHeight = -1f;
    }

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

        /// <summary>
        /// カメラドラッグ中（連続移動中）に発火する軽量コールバック。
        /// Phase 1: ApplyCameraTransform + PresentAll など軽量な更新のみを実行する想定。
        /// UpdateFrame（GPU ヒットテスト等の重い処理）はドラッグ終了時の OnCameraChanged で行う。
        /// </summary>
        public System.Action OnCameraDragging;

        // ================================================================
        // 状態
        // ================================================================

        // Top/Side/Front で共有する視点状態（連動）。既定は個別インスタンス。
        // Manager が Top/Side/Front に同一インスタンスを注入することで連動する。
        private OrthoViewSharedState _shared = new OrthoViewSharedState();

        public Vector3 Target { get => _shared.Target; private set => _shared.Target = value; }

        /// <summary>共有ズーム（スクリーン1pxあたりのワールド高さ）。高さ非依存。</summary>
        public float WorldHeightPerPixel
        {
            get => _shared.WorldHeightPerPixel;
            private set => _shared.WorldHeightPerPixel = value;
        }

        /// <summary>true のとき反対方向から見る（Top↔Bottom / Front↔Back / Right↔Left）。</summary>
        public bool Flipped { get; set; } = false;

        /// <summary>連動用の共有視点状態を注入する。Top/Side/Front に同一インスタンスを渡す。</summary>
        public void SetSharedState(OrthoViewSharedState shared)
        {
            if (shared != null) _shared = shared;
        }

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
            Target = bounds.center;
            // pixelHeight はここでは不明なため、目標ワールド半高さを保留し、
            // 次の ApplyCameraTransform（cam.pixelHeight 確定時）で解決する。
            _shared.PendingResetHalfHeight =
                Mathf.Clamp(bounds.size.magnitude * 0.6f, OrthoSizeMin, OrthoSizeMax);
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
            cam.orthographic = true;

            // ビューポート高さ補正：全ビューで px/world 比を一致させるため、
            // 高さ非依存の WorldHeightPerPixel から orthographicSize を算出する。
            float halfPix = Mathf.Max(1f, cam.pixelHeight * 0.5f);

            // ResetToMesh の遅延解決：pixelHeight が有効なこのタイミングで
            // 目標ワールド半高さ → WorldHeightPerPixel へ変換する。
            if (_shared.PendingResetHalfHeight >= 0f && cam.pixelHeight > 1f)
            {
                WorldHeightPerPixel = _shared.PendingResetHalfHeight / halfPix;
                _shared.PendingResetHalfHeight = -1f;
            }

            cam.orthographicSize =
                Mathf.Clamp(WorldHeightPerPixel * halfPix, OrthoSizeMin, OrthoSizeMax);

            const float camDist = 100f; // 十分遠い位置に置く（クリッピング回避）

            switch (_direction)
            {
                case OrthoViewDirection.Top:
                    if (!Flipped)
                    {
                        // Top: 上から見下ろす
                        cam.transform.position = Target + Vector3.up * camDist;
                        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                    }
                    else
                    {
                        // Bottom: 下から見上げる
                        cam.transform.position = Target + Vector3.down * camDist;
                        cam.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                    }
                    break;

                case OrthoViewDirection.Front:
                    // PMXモデルは-Z向き。正面(Front)ビューはモデルの正面を見るため
                    // カメラを -Z 側に置き +Z 方向を向く
                    if (!Flipped)
                    {
                        // Front
                        cam.transform.position = Target + Vector3.back * camDist;
                        cam.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                    }
                    else
                    {
                        // Back: +Z 側に置き -Z 方向を向く
                        cam.transform.position = Target + Vector3.forward * camDist;
                        cam.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                    }
                    break;

                case OrthoViewDirection.Side:
                    if (!Flipped)
                    {
                        // Right: +X 側に置き -X 方向を向く（右側面ビュー）
                        cam.transform.position = Target + Vector3.right * camDist;
                        cam.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
                    }
                    else
                    {
                        // Left: -X 側に置き +X 方向を向く（左側面ビュー）
                        cam.transform.position = Target + Vector3.left * camDist;
                        cam.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                    }
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

            // 連動一致のため、フリップはパン方向に影響させない（表示のみ反転）。
            // 移動量は高さ非依存の WorldHeightPerPixel による等倍（カーソル追従）。
            float wpp = WorldHeightPerPixel;

            Vector3 panDelta;
            switch (_direction)
            {
                case OrthoViewDirection.Top:
                    // Top: X→X, Y（スクリーン上下）→ Z
                    panDelta = new Vector3(-delta.x, 0f, -delta.y) * wpp;
                    break;
                case OrthoViewDirection.Front:
                default:
                    // Front: スクリーン右 → Target.x
                    panDelta = new Vector3(-delta.x, -delta.y, 0f) * wpp;
                    break;
                case OrthoViewDirection.Side:
                    // Side: スクリーン右 → Target.z
                    panDelta = new Vector3(0f, -delta.y, -delta.x) * wpp;
                    break;
            }
            Target += panDelta;

            // Phase 1: ドラッグ中はフレーム駆動で transform 反映していたが
            // Tick 廃止に伴い、軽量コールバックで event 駆動化する。
            OnCameraDragging?.Invoke();
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
            // 高さ非依存の共有ズームを更新する。実際の orthographicSize は
            // ApplyCameraTransform で各ビューの pixelHeight から算出される。
            WorldHeightPerPixel *= 1f - scroll * ZoomSensitivity;
            WorldHeightPerPixel  = Mathf.Max(1e-6f, WorldHeightPerPixel);
            // Phase 1: スクロールは単発イベントのため、フル更新を伴う OnCameraChanged を発火する。
            OnCameraChanged?.Invoke();
        }
    }
}
