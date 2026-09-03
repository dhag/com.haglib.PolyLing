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
        /// <summary>プロジェクト全体を PLRF 束にして "hierarchyExport" クライアントへ push する。</summary>
        public void SendHierarchyBundle()                  => _server?.SendHierarchyBundle();
        public void SendCapturedImages()                   => _server?.SendCapturedImages();
        public void BroadcastPositions(MeshObject mesh)    => _server?.BroadcastPositions(mesh);
        /// <summary>対象を明示して位置を配信する（協働編集ではこちらを使う）。</summary>
        public void BroadcastPositions(MeshContext mc, int modelIndex = -1)
            => _server?.BroadcastPositions(mc, modelIndex);
        public void ClearCapturedImages()                  => _server?.ClearCapturedImages();

        /// <summary>
        /// 表示更新の通知（メインスレッドで発火。UI 側が購読して状態表示を更新する）。
        /// ログ本文は PlayerLog（統合ログ）へ集約したため、ここでは扱わない。
        /// </summary>
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
        /// <param name="dispatchCommand">受信コマンドを処理し、実行結果を返すデリゲート</param>
        public void Initialize(
            int port,
            bool autoStart,
            System.Func<ToolContext> getToolContext,
            System.Func<PanelCommand, CommandResult> dispatchCommand,
            System.Action requestPanelRefresh = null,
            string hostUserName = null)
        {
            _server = new RemoteServerCore(getToolContext, port)
            {
                DispatchCommand = dispatchCommand,
                OnRepaint       = () => OnLogChanged?.Invoke(),
                // RemoteServerCore.Log は "[HH:mm:ss] 本文" 形式で渡してくる。
                // PlayerLog 側でも時刻を付けるため、先頭の時刻を取り除いてから投入する。
                OnLog           = msg => PlayerLog.Add("Server", StripTimeStamp(msg)),
            };

            // 協働編集: 選択スコープ差し替えからの復帰時にホストUIを再同期する。
            // 未指定なら OnRepaint にフォールバックする（RemoteServerCore 側で処理）。
            if (requestPanelRefresh != null)
                _server.RequestPanelRefresh = requestPanelRefresh;

            // ホスト自身のユーザー名。クライアントと重複しない名前にすること。
            if (!string.IsNullOrEmpty(hostUserName))
                _server.HostUserName = hostUserName;

            if (autoStart) StartServer();
        }

        /// <summary>ホストのユーザー名（協働編集の担当者名・選択スロットのキー）。</summary>
        public string HostUserName
        {
            get => _server?.HostUserName ?? "";
            set { if (_server != null && !string.IsNullOrEmpty(value)) _server.HostUserName = value; }
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

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// 先頭の "[HH:mm:ss] " を取り除く。該当しない場合は原文をそのまま返す。
        /// </summary>
        private static string StripTimeStamp(string msg)
        {
            if (string.IsNullOrEmpty(msg) || msg.Length < 11) return msg;
            if (msg[0] != '[' || msg[3] != ':' || msg[6] != ':' || msg[9] != ']' || msg[10] != ' ')
                return msg;
            return msg.Substring(11);
        }
    }
}
