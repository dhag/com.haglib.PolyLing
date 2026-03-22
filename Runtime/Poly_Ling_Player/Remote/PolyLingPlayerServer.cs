// PolyLingPlayerServer.cs
// プレイヤービルド用 RemoteServerCore ホスト
// PolyLingPlayerCore と連携して WebSocket サーバーを起動する
// Runtime/Poly_Ling_Player/Remote/ に配置

using UnityEngine;
using Poly_Ling.Remote;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    [RequireComponent(typeof(PolyLingPlayerCore))]
    public class PolyLingPlayerServer : MonoBehaviour
    {
        // ================================================================
        // Inspector設定
        // ================================================================

        [SerializeField] private int  _port          = 8765;
        [SerializeField] private bool _autoStart     = true;

        // ================================================================
        // サーバーコア
        // ================================================================

        private RemoteServerCore _server;
        private PolyLingPlayerCore _playerCore;

        public bool IsRunning => _server?.IsRunning ?? false;

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void Awake()
        {
            _playerCore = GetComponent<PolyLingPlayerCore>();
        }

        private void Start()
        {
            _server = new RemoteServerCore(
                contextProvider: () => _playerCore?.Core?.CurrentToolContext,
                port: _port)
            {
                DispatchCommand = OnCommandReceived,
                OnRepaint       = () => { },
            };

            if (_autoStart)
                StartServer();
        }

        private void OnDestroy()
        {
            StopServer();
        }

        // ================================================================
        // サーバー制御
        // ================================================================

        public void StartServer()
        {
            if (_server == null || _server.IsRunning) return;
            _server.Start();
            Debug.Log($"[PolyLingPlayerServer] Started on port {_port}");
        }

        public void StopServer()
        {
            if (_server == null || !_server.IsRunning) return;
            _server.Stop();
            Debug.Log("[PolyLingPlayerServer] Stopped.");
        }

        // ================================================================
        // コマンド受信
        // ================================================================

        private void OnCommandReceived(PanelCommand cmd)
        {
            if (_playerCore == null) return;
            _playerCore.DispatchCommand(cmd);
        }

        // ================================================================
        // Update — RemoteServerCoreのキュー処理
        // ================================================================

        private void Update()
        {
            _server?.Tick();
        }
    }
}
