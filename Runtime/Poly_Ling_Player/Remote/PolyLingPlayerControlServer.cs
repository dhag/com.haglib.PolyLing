// PolyLingPlayerControlServer.cs
// ============================================================
// ビルド版（Player）で MCP サーバ（PolyLingMcpServer --target player）の要求を受ける
// ============================================================
//
// 【役割】
//   Editor の PolyLingEditorControlServer（ポート 8770）と同じ 1 行 JSON を、
//   ビルド版ではポート 8772 で受ける。処理は Editor と共通の
//   PolyLingControlProtocol（ping / tools* / scenes / call）に任せる。
//   state は host=player を返す。Editor にしか無い action は理由付きで断る。
//
// 【なぜ Editor と別ポートか】
//   ビルド版から Editor 拡張へ送る経路（sendHierarchyBundle、MCP の unity_prefab_*）を
//   使うときは Editor とビルド版を同時に動かす。同じポートだと後から起動した側が待ち受けられない。
//
// 【Editor では立てない】
//   Editor では PolyLingEditorControlServer が受ける。PolyLingPlayerViewerCore は
//   Editor の Play 中にも動くため、Application.isEditor で判定して何もしない。
//
// 【ライフサイクル】
//   PolyLingPlayerViewerCore が PolyLingCommandGateway を設定した直後に Start、
//   解除する箇所で Stop する（PolyLingPlayerViewerCore.Lifecycle.cs）。
//
// 【スレッド】
//   TcpDuplexServer.OnReceived は背景スレッドで発火する。コマンドはメインスレッド専用なので、
//   Start 時に取った SynchronizationContext へ回す。毎フレームのポーリングはしない。
//
// Runtime/Poly_Ling_Player/Remote/ に配置。#if UNITY_EDITOR 不使用。
// ============================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using HagLib.NET.Duplex;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Remote;

namespace Poly_Ling.Player
{
    /// <summary>ビルド版で MCP サーバの要求を受けるループバック TCP サーバ。</summary>
    public static class PolyLingPlayerControlServer
    {
        /// <summary>
        /// 待ち受けポート。MCP サーバ側（EditorControlClient.PlayerPort）と一致させること。
        /// Editor の 8770、MCP サーバの HTTP の例（README の --http 8771）と重ならない値。
        /// </summary>
        public const int Port = 8772;

        /// <summary>コンソールログの共通接頭辞。</summary>
        private const string LogTag = "[PolyLingPlayerControl]";

        private static TcpDuplexServer        _server;
        private static SynchronizationContext _syncCtx;

        public static bool IsRunning => _server != null && _server.IsListening;

        // ================================================================
        // ライフサイクル
        // ================================================================

        /// <summary>メインスレッドから呼ぶこと。Editor では何もしない。</summary>
        public static void Start()
        {
            if (Application.isEditor) return;
            if (IsRunning) return;

            Stop();

            _syncCtx = SynchronizationContext.Current;
            if (_syncCtx == null)
            {
                Debug.LogWarning($"{LogTag} 起動失敗: メインスレッドの SynchronizationContext が取れません");
                return;
            }

            var server = new TcpDuplexServer();
            server.OnReceived += OnReceived;

            try
            {
                // 外部から届かないようループバックだけを bind する。
                // 使用中のポートは SocketException が同期的に出る（RemoteDirectory.cs と同じ扱い）。
                server.StartAsync(System.Net.IPAddress.Loopback, Port);
            }
            catch (Exception ex)
            {
                try { server.OnReceived -= OnReceived; } catch { }
                try { server.Dispose(); } catch { }
                Debug.LogWarning($"{LogTag} 起動失敗: port={Port}; {ex.Message}");
                return;
            }

            _server = server;
            Debug.Log($"{LogTag} 起動: port={Port}（ループバックのみ）");
        }

        /// <summary>停止する。起動していなければ何もしない。</summary>
        public static void Stop()
        {
            var server = _server;
            _server = null;

            if (server == null) return;

            try { server.OnReceived -= OnReceived; } catch { }
            try { server.Dispose(); } catch { }

            Debug.Log($"{LogTag} 停止");
        }

        // ================================================================
        // 受信
        // ================================================================

        private static void OnReceived(IDuplexChannel channel, DuplexMessage message)
        {
            // 応答を返せるのは Request だけ。Push は無視する。
            if (message == null || message.Type != MessageType.Request) return;

            var requestJson = message.PayloadString;
            var ctx = _syncCtx;
            if (ctx == null) return;

            ctx.Post(_ =>
            {
                string responseJson;
                try
                {
                    responseJson = Handle(requestJson);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LogTag} 要求処理で例外: {ex.Message}");
                    responseJson = PolyLingControlProtocol.BuildError("", ex.Message);
                }

                Task reply;
                try
                {
                    reply = channel.ReplyAsync(message, responseJson);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LogTag} 応答失敗: {ex.Message}");
                    return;
                }

                reply.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        Debug.LogWarning($"{LogTag} 応答送出に失敗: {t.Exception?.GetBaseException().Message}");
                });
            }, null);
        }

        /// <summary>要求 1 件を処理して応答 JSON を返す。メインスレッドで呼ばれる。</summary>
        private static string Handle(string requestJson)
        {
            var msg = JsonParser.Parse(requestJson ?? string.Empty);
            var op  = msg.Action ?? string.Empty;

            if (PolyLingControlProtocol.TryHandleCommon(op, msg, LogTag, out string common))
                return common;

            switch (op)
            {
                case "state":
                    return PolyLingControlProtocol.BuildOk(op,
                        $"host=player; panelReady={(PolyLingCommandGateway.IsReady ? "true" : "false")}");

                case "play":
                case "stop":
                case "refresh":
                case "recompile":
                case "prefab_export":
                case "prefab_instantiate":
                case "prefab_import":
                    return PolyLingControlProtocol.BuildError(op,
                        $"{op} は Unity Editor の操作なので、ビルド版では使えません");

                default:
                    return PolyLingControlProtocol.BuildError(op, $"unknown action: {op}");
            }
        }
    }
}
