// OrthoViewController.cs
// Top / Front 正投影ビュー用のパン・ズームコントローラー。
// 右ボタンドラッグ・中ボタンドラッグ → パン
// スクロール → OrthographicSize ズーム
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;
using Poly_Ling.Core;

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

        // 3ビュー共通の視点姿勢（リグ回転）。identity = 軸整列（Top=+Y / Front=+Z / Right=+X）。
        // 各ビューの姿勢は RigRotation × BaseRotation(方向, 反転) で決まるため、
        // ここを回すと3ビューの相互関係（直交）を保ったまま全体が回る。
        public Quaternion RigRotation = Quaternion.identity;

        // ビューポート高さに依存しない共有ズーム：スクリーン1pxあたりのワールド高さ。
        // 各ビューの orthographicSize = WorldHeightPerPixel × pixelHeight ÷ 2。
        // これにより高さの異なるビュー間でも見かけのズーム（px/world）が一致する。
        public float WorldHeightPerPixel = 0.01f;

        // ResetToMesh の遅延解決用。pixelHeight 確定時に WorldHeightPerPixel へ変換する
        // 目標ワールド半高さ（<0 は解決不要）。
        public float PendingResetHalfHeight = -1f;

        // 3ビュー共通の投影方式。true で透視投影になる（カメラ調整ツールから切替）。
        // 3面の相互関係（直交・中心・ズーム）は投影方式によらず共有のまま保つ。
        public bool Perspective = false;

        // 透視投影時の画角（度）。3台連動のため共有状態に持つ。
        public float Fov = 60f;
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

        /// <summary>正投影時にカメラを Target から引く固定距離（クリッピング回避）。</summary>
        public const float OrthoCameraPullback = 100f;

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

        // カメラ調整ツール（CameraToolHandler / PlayerCameraSubPanel）から
        // 数値・ギズモで直接書き込むため公開セッタを持つ。
        public Vector3 Target { get => _shared.Target; set => _shared.Target = value; }

        /// <summary>共有ズーム（スクリーン1pxあたりのワールド高さ）。高さ非依存。</summary>
        public float WorldHeightPerPixel
        {
            get => _shared.WorldHeightPerPixel;
            set => _shared.WorldHeightPerPixel = Mathf.Max(1e-6f, value);
        }

        /// <summary>3ビュー共通の投影方式。true で透視投影。</summary>
        public bool Perspective
        {
            get => _shared.Perspective;
            set => _shared.Perspective = value;
        }

        /// <summary>3ビュー共通の画角（度）。透視投影時のみ意味を持つ。</summary>
        public float Fov
        {
            get => _shared.Fov;
            set => _shared.Fov = Mathf.Clamp(value, 1f, 179f);
        }

        /// <summary>
        /// 3ビュー共通の視点姿勢（リグ回転）。共有状態。identity = 軸整列。
        /// 3視線の直交関係は回転によらず保たれる。
        /// </summary>
        public Quaternion RigRotation
        {
            get => _shared.RigRotation;
            set => _shared.RigRotation = value;
        }

        /// <summary>true のとき反対方向から見る（Top↔Bottom / Front↔Back / Right↔Left）。</summary>
        private bool _flipped = false;
        public bool Flipped
        {
            get => _flipped;
            set => _flipped = value;
        }

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
            // 拡大限界は外部CSV（DisplaySettings.csv）から取得する。
            OrthoSizeMin = DisplaySettings.GetF(DisplaySettings.KeyCameraOrthoSizeMin);
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
        // 斜めトグルON時にリグへ与える水平回転角（度）。
        // RigRotation = Euler(0, -DefaultHorizontalTiltDeg, 0) として使う。
        // 符号を反転すると向くコーナーが左右入れ替わる。
        public const float DefaultHorizontalTiltDeg = 45f;

        /// <summary>
        /// 現在の視線回転（リグ回転 × 方向別基底 × Flip）。
        /// カメラ調整ツールがメインカメラ側に3面の向きを表示するために使う。
        /// </summary>
        public Quaternion CurrentViewRotation() => ViewRotation();

        private Quaternion ViewRotation()
        {
            // 共有リグ回転 × 方向別の基底姿勢。リグ回転は3ビュー共通なので、
            // 3視線の直交関係を保ったまま全体の見る角度を変えられる。
            return _shared.RigRotation * BaseRotation();
        }

        /// <summary>
        /// リグ回転 identity のときの方向別基底姿勢（軸整列）。
        /// Flipped は正反対の視点（Top↔Bottom / Front↔Back / Right↔Left）。
        /// </summary>
        private Quaternion BaseRotation()
        {
            switch (_direction)
            {
                case OrthoViewDirection.Top:
                    // Top(上から見下ろす、+Y軸上) / Bottom(下から見上げる、-Y軸上)。
                    // Y=180 は画面右を Front と同じ -X に揃えるため。これにより
                    // Front が正面向きのとき Top はうつ伏せ、Bottom はあおむけの像になり、
                    // 横パンの連動方向が Front と一致する。
                    return Flipped ? Quaternion.Euler(-90f, 180f, 0f)
                                   : Quaternion.Euler( 90f, 180f, 0f);
                case OrthoViewDirection.Side:
                    // Right(+X軸上→-X方向) / Left(-X軸上→+X方向)。
                    return Flipped ? Quaternion.Euler(0f,  90f, 0f)
                                   : Quaternion.Euler(0f, -90f, 0f);
                case OrthoViewDirection.Front:
                default:
                    // Front(+Z軸上→-Z方向) / Back(-Z軸上→+Z方向)。
                    // モデルは Unity 規約（正面 = +Z）なので、+Z 側から見たものが Front。
                    // Flipped == true が Back を表す関係は据え置く
                    // （下絵の方向対応がこの意味に依存しているため）。
                    return Flipped ? Quaternion.Euler(0f,   0f, 0f)
                                   : Quaternion.Euler(0f, 180f, 0f);
            }
        }

        public void ApplyCameraTransform(Camera cam)
        {
            if (cam == null) return;

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

            // Target 平面での半画面高。透視・正投影のどちらでも見かけのスケールが
            // 一致するよう、この値を唯一のズーム量として扱う。
            float halfHeight =
                Mathf.Clamp(WorldHeightPerPixel * halfPix, OrthoSizeMin, OrthoSizeMax);

            // 視点回転は ViewRotation() を唯一の真実として参照する（パンと共通基底）。
            // position は「カメラ前方の逆」に camDist だけ引いた位置。6方向とも従来の
            // position/rotation と数値一致する。
            Quaternion rot = ViewRotation();

            float camDist;
            cam.orthographic = !Perspective;
            if (Perspective)
            {
                // 透視投影：Target 平面での半画面高が正投影時と一致する距離に置く。
                // これによりズーム量（WorldHeightPerPixel）を共有したまま投影だけ
                // 切り替えても、見かけの大きさが連続する。
                cam.fieldOfView = Fov;
                float halfFovRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                camDist = halfHeight / Mathf.Max(1e-4f, Mathf.Tan(halfFovRad));
            }
            else
            {
                cam.orthographicSize = halfHeight;
                camDist = OrthoCameraPullback;
            }

            cam.transform.rotation = rot;
            cam.transform.position = Target - rot * Vector3.forward * camDist;
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
