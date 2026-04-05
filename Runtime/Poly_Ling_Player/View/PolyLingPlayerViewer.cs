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

        private void Update()      => _core?.Tick();
        private void LateUpdate()  => _core?.LateTick();
        private void OnDestroy()   => _core?.Dispose();
    }
}
