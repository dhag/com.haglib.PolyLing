// PolyLingPlayerViewer.cs
// プレイヤービルド用メッシュビューア MonoBehaviour ラッパー。
// ロジック本体は PolyLingPlayerViewerCore に委譲する。
//
// Runtime/Poly_Ling_Player/View/ に配置

using UnityEngine;
using UnityEngine.UIElements;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.Player
{
    public class PolyLingPlayerViewer : MonoBehaviour
    {
        // ================================================================
        // Inspector 設定
        // ================================================================

        [SerializeField] private PolyLingPlayerViewerCore.RemoteMode _remoteMode = PolyLingPlayerViewerCore.RemoteMode.None;

        [SerializeField] private string _clientHost        = "127.0.0.1";
        [SerializeField] private int    _clientPort        = 8765;
        [SerializeField] private bool   _clientAutoConnect = true;

        [SerializeField] private int  _serverPort      = 8765;
        [SerializeField] private bool _serverAutoStart = true;

        [SerializeField] private UIDocument _uiDocument;
        [SerializeField] private Transform  _sceneRoot;

        // ================================================================
        // コア
        // ================================================================

        private PolyLingPlayerViewerCore _core;

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void Awake()
        {
            if (_sceneRoot == null) _sceneRoot = transform;

            if (_uiDocument == null) _uiDocument = GetComponent<UIDocument>();
            if (_uiDocument == null) _uiDocument = gameObject.AddComponent<UIDocument>();

            if (_uiDocument.panelSettings == null)
                Debug.LogError("[PolyLingPlayerViewer] UIDocument に PanelSettings が未設定です。");

            // PlayerBridge を登録
            PLEditorBridge.Register(new PolyLingPlayerBridge());
        }

        private void Start()
        {
            if (_uiDocument == null || _uiDocument.panelSettings == null) return;

            _core = new PolyLingPlayerViewerCore();
            _core.Initialize(
                _uiDocument.rootVisualElement,
                _sceneRoot,
                new PolyLingPlayerViewerCore.RemoteConfig
                {
                    Mode             = _remoteMode,
                    ClientHost       = _clientHost,
                    ClientPort       = _clientPort,
                    ClientAutoConnect = _clientAutoConnect,
                    ServerPort       = _serverPort,
                    ServerAutoStart  = _serverAutoStart,
                });
        }

        // Phase 2a-2f: MonoBehaviour.Update / LateUpdate はイベント駆動規約に違反するため削除。
        // ここから旧 Tick / LateTick が毎フレーム呼ばれ、dead 実装だったにもかかわらず
        // Debug.Log が毎フレーム走る深刻な問題があった。
        // 描画提出は OnBeginCameraRendering 経由でカメラ毎に呼ばれる。
        // 計算処理は各イベント駆動ハンドラに分散済み (Phase 2a-2 系で完了)。

        private void OnEnable()
        {
            // Camera イベント購読（user 承認範囲: Camera.onPreCull 等のコールバック購読は違反ではない）。
            // URP 互換のため RenderPipelineManager.beginCameraRendering を使う。
            // OnRenderObject() は URP 環境で発火タイミング・Camera.current の扱いが
            // 不確実なため、ここでは RenderPipelineManager 経由で確実に各カメラ描画前に呼ぶ。
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        private void OnDisable()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        private void OnDestroy()
        {
            UnityEngine.Rendering.RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            _core?.Dispose();
        }

        /// <summary>
        /// ★★★ 厳守: この関数は Graphics.DrawMesh 等の描画提出のみを行う。 ★★★
        /// 計算処理（Mesh 再構築、ComputeBuffer 更新、Dispatch、フラグ計算、
        /// ホバー判定、選択フラグ反映、マテリアル色設定等）は一切禁止。
        /// 違反は重大な規約違反。
        /// 全ての準備は対応するイベント駆動ハンドラ側で完了させておくこと
        /// （具体的には _core.PresentAll() の呼出し契機）。
        ///
        /// RenderPipelineManager.beginCameraRendering は各カメラ描画の直前に
        /// 呼ばれるコールバック（URP 互換）。引数で対象カメラが渡されるため
        /// slot 特定が可能。
        /// ★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★★
        /// </summary>
        private void OnBeginCameraRendering(UnityEngine.Rendering.ScriptableRenderContext ctx, Camera cam)
        {
            if (cam == null) return;
            _core?.SubmitDrawForCamera(cam);
        }
    }
}
