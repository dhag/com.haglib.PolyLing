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
    /// 中心(Target)とズーム倍率(WorldHeightPerPixel)を共有し、いずれかの
    /// パン／ズーム操作が3ビュー全てに反映される（鏡面連動）。
    /// </summary>
    public sealed class OrthoViewSharedState
    {
        public Vector3 Target = Vector3.zero;

        // Front列のペインが BACK 表示（Front コントローラーが Flipped）か。
        // たて並びの TOP を BACK と揃えるため、TOP の X連動反転判定に使う。
        public bool FrontFlipped = false;

        // Front/Side 列の水平傾き（度）。UIトグルで切替。0=正対、45=斜め。Top/Bottomは無視。
        public float HorizontalTilt = 0f;

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

        // 中心(Target)とズーム倍率を Top/Side/Front で共有する（既定は個別インスタンス）。
        // Manager が SetSharedState で同一インスタンスを注入すると3面が連動（鏡面）する。
        private OrthoViewSharedState _shared = new OrthoViewSharedState();

        public Vector3 Target { get => _shared.Target; private set => _shared.Target = value; }

        /// <summary>共有ズーム（スクリーン1pxあたりのワールド高さ）。高さ非依存。</summary>
        public float WorldHeightPerPixel
        {
            get => _shared.WorldHeightPerPixel;
            private set => _shared.WorldHeightPerPixel = value;
        }

        /// <summary>Front/Side 列の水平傾き（度）。共有状態。UIトグルから設定する。</summary>
        public float HorizontalTilt
        {
            get => _shared.HorizontalTilt;
            set => _shared.HorizontalTilt = value;
        }

        /// <summary>true のとき反対方向から見る（Top↔Bottom / Front↔Back / Right↔Left）。</summary>
        private bool _flipped = false;
        public bool Flipped
        {
            get => _flipped;
            set
            {
                _flipped = value;
                // Front→Back 反転を共有状態へ伝え、TOP 側の X連動反転を切り替える。
                if (_direction == OrthoViewDirection.Front) _shared.FrontFlipped = value;
            }
        }

        // 例外規則：この面が TOP（非反転）で、かつ Front列が BACK 表示のとき true。
        // このとき TOP の X連動のみ反転させ、たて並びの BACK と移動方向を揃える。
        private bool TopXInverted =>
            _direction == OrthoViewDirection.Top && !_flipped && _shared.FrontFlipped;

        /// <summary>共有ズーム状態を注入する。Top/Side/Front に同一インスタンスを渡すとスケールが揃う。</summary>
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

        /// <summary>
        /// 現在の方向 (_direction) と Flipped から視点回転を返す。
        /// ApplyCameraTransform（カメラ姿勢）と OnDrag（パン方向）が同じ基底を
        /// 参照することで、Flip 時に表示とパン方向がズレる問題を防ぐ。
        /// </summary>
        // Front/Side 列の水平傾き既定角（トグルON時に共有状態へ設定する値）。
        // Top/Bottom は対象外。符号を反転すると向くコーナーが左右入れ替わる。
        public const float DefaultHorizontalTiltDeg = 45f;

        private Quaternion ViewRotation()
        {
            // 実際の傾きは共有状態（UIトグルで切替）。既定0=正対。
            float t = _shared.HorizontalTilt;
            switch (_direction)
            {
                case OrthoViewDirection.Top:
                    // Top(上から見下ろす) / Bottom(下から見上げる)。傾きは付けない。
                    return Flipped ? Quaternion.Euler(-90f, 0f, 0f)
                                   : Quaternion.Euler( 90f, 0f, 0f);
                case OrthoViewDirection.Side:
                    // Right(+X側→-X方向) / Left(-X側→+X方向) に水平45°を加算。
                    return Flipped ? Quaternion.Euler(0f,  90f + t, 0f)
                                   : Quaternion.Euler(0f, -90f + t, 0f);
                case OrthoViewDirection.Front:
                default:
                    // Front(-Z側→+Z方向) / Back(+Z側→-Z方向) に水平45°を加算。
                    return Flipped ? Quaternion.Euler(0f, 180f + t, 0f)
                                   : Quaternion.Euler(0f,   0f + t, 0f);
            }
        }

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

            // 視点回転は ViewRotation() を唯一の真実として参照する（パンと共通基底）。
            // position は「カメラ前方の逆」に camDist だけ引いた位置。6方向とも従来の
            // position/rotation と数値一致する。
            Quaternion rot = ViewRotation();
            cam.transform.rotation = rot;
            // 例外：Front列がBACK表示のとき、TOPは中心のXを反転して配置する。
            // 像は反転せず（モデル形状のXはそのまま）、パン連動方向だけが反転する。
            Vector3 center = Target;
            if (TopXInverted) center.x = -center.x;
            cam.transform.position = center - rot * Vector3.forward * camDist;
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

            // パン方向はカメラ基底ベクトル（ViewRotation）から算出する。
            // Flip（Bottom/Back/Left）でも表示とパン方向が一致し、全方向でカーソル追従になる。
            // 移動量は高さ非依存の WorldHeightPerPixel 等倍。
            // delta は viewport 座標（Y=0 下、上方向が正）。
            float wpp = WorldHeightPerPixel;

            Quaternion rot = ViewRotation();
            // Target を「カメラ右／上」の逆へ動かすとコンテンツがカーソルに追従する。
            Vector3 pan = rot * Vector3.right * delta.x + rot * Vector3.up * delta.y;
            // 例外：Front列がBACK表示のとき、TOPのX連動は反転（中心X反転と対）。
            // TOP自身をドラッグしても自カーソル追従を保つため書き込み側Xも反転する。
            if (TopXInverted) pan.x = -pan.x;
            Target -= pan * wpp;

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
