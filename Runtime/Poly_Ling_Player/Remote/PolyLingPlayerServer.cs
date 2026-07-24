// PolyLingPlayerServer.cs
// プレイヤービルド用 WebSocket サーバー（通常クラス版）
// PolyLingPlayerViewer にサブシステムとして格納する。
// Runtime/Poly_Ling_Player/Remote/ に配置

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Remote;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public class PolyLingPlayerServer
    {
        // ================================================================
        // サーバーコア
        // ================================================================

        private RemoteServerCore _server;

        public bool IsRunning   => _server?.IsRunning ?? false;
        public int  Port        => _server?.Port      ?? 0;
        public int  ClientCount => _server?.ClientCount ?? 0;

        // ================================================================
        // 公開 API（PlayerRemoteServerSubPanel から利用）
        // ================================================================

        public List<ImageEntry>          CapturedImages    => _server?.CapturedImages;
        public void SendProjectHeader()                    => _server?.SendProjectHeader();
        public void SendCapturedImages()                   => _server?.SendCapturedImages();
        public void BroadcastPositions(MeshObject mesh)    => _server?.BroadcastPositions(mesh);
        public void ClearCapturedImages()                  => _server?.ClearCapturedImages();

        private readonly List<string> _logMessages = new List<string>();
        private const int MaxLogLines = 100;
        public IReadOnlyList<string> LogMessages => _logMessages;
        public void ClearLog() => _logMessages.Clear();

        /// <summary>ログ表示更新の通知（メインスレッドで発火。UI 側が購読してログ欄を再描画する）。</summary>
        public System.Action OnLogChanged;

        // ================================================================
        // 初期化 / 破棄
        // ================================================================

        /// <summary>
        /// PolyLingPlayerViewer.Start() から呼ぶ。
        /// </summary>
        /// <param name="port">待ち受けポート番号</param>
        /// <param name="autoStart">true のとき Initialize 内でサーバを起動する</param>
        /// <param name="getToolContext">ToolContext を返すデリゲート（RemoteServerCore に渡す）</param>
        /// <param name="dispatchCommand">受信コマンドを処理するデリゲート</param>
        public void Initialize(
            int port,
            bool autoStart,
            System.Func<ToolContext> getToolContext,
            System.Action<PanelCommand> dispatchCommand)
        {
            _server = new RemoteServerCore(getToolContext, port)
            {
                DispatchCommand = dispatchCommand,
                OnRepaint       = () => OnLogChanged?.Invoke(),
                OnLog           = msg =>
                {
                    _logMessages.Add(msg);
                    while (_logMessages.Count > MaxLogLines) _logMessages.RemoveAt(0);
                },
            };

            if (autoStart) StartServer();
        }

        /// <summary>
        /// PolyLingPlayerViewer.OnDestroy() から呼ぶ。
        /// </summary>
        public void Dispose()
        {
            StopServer();
        }

        /// <summary>
        /// PolyLingPlayerViewer.Update() から毎フレーム呼ぶ。
        /// RemoteServerCore のコマンドキューを処理する。
        /// </summary>
        public void Tick()
        {
            _server?.Tick();
        }

        /// <summary>
        /// 本体が選択変更を検知した際に呼ぶ。接続中クライアントへ selectionChanged を配信する。
        /// </summary>
        public void NotifySelectionChanged()
        {
            _server?.NotifySelectionChanged();
        }

        // ================================================================
        // サーバー制御
        // ================================================================

        public void StartServer()
        {
            if (_server == null || _server.IsRunning) return;
            _server.Start();
            Debug.Log($"[PolyLingPlayerServer] Started on port {Port}");
        }

        public void StopServer()
        {
            if (_server == null || !_server.IsRunning) return;
            _server.Stop();
            Debug.Log("[PolyLingPlayerServer] Stopped.");
        }
    }
}
