// Editor/EditorControl/PolyLingEditorControlServer.cs
// ============================================================
// 外部プロセス（SimpleUnityMcpServer）から Editor を操作するための受信サーバ
// ============================================================
//
// 【役割】
//   名前付きパイプ 1 本を待ち受け、1 行 JSON の要求に 1 行 JSON で応答する。
//   TCP ポートは一切開かない。
//
// 【RemoteServerCore との違い】
//   RemoteServerCore はモデル・選択・所有権に密結合したパネル用サーバで、
//   ToolContext と DispatchCommand を要求する（RemoteServerCore.cs:59,145）。
//   本サーバは PolyLing ウィンドウが開いていなくても動く必要があるため、
//   モデルにも ToolContext にも一切依存しない別インスタンスとして立てる。
//
// 【スレッド】
//   PipeDuplexServer.OnReceived は背景スレッドで発火する。
//   UnityEditor API はメインスレッド専用のため、
//   RemoteServerCore.cs:284-300 と同じ SynchronizationContext 経由で回す。
//   毎フレームポーリングは行わない。
//
// 【ライフサイクル】
//   PolyLingEditorControlInstaller が [InitializeOnLoad] で Start し、
//   AssemblyReloadEvents.beforeAssemblyReload で Stop する。
//   ドメインリロード後は静的コンストラクタが再度走って再起動する。
//
// 【コンソールログ】
//   接頭辞は [PolyLingEditorControl] で統一し、無条件に常時出力する。
//   クライアント接続・切断 / 受信 JSON / 解決した action / 生成した応答 JSON /
//   応答送出完了 / afterReply の開始・中止理由 /
//   EnterPlaymode・ExitPlaymode の前後を記録する。
//   Debug.Log は背景スレッドからも呼べるが、EditorApplication を読む
//   DescribeState() はメインスレッドで実行される箇所からのみ呼ぶこと。
//
// ============================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using HagLib.NET.Duplex;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Remote;

namespace Poly_Ling.EditorControl
{
    /// <summary>外部プロセスからの Editor 操作要求を受け付ける名前付きパイプサーバ。</summary>
    public static class PolyLingEditorControlServer
    {
        /// <summary>パイプ名。クライアント側（SimpleUnityMcpServer）と一致させること。</summary>
        public const string PipeName = "PolyLing.EditorControl";

        /// <summary>コンソールログの共通接頭辞。</summary>
        private const string LogTag = "[PolyLingEditorControl]";

        private static PipeDuplexServer      _server;
        private static SynchronizationContext _syncCtx;

        public static bool IsRunning => _server != null && _server.IsListening;

        // ================================================================
        // ライフサイクル
        // ================================================================

        /// <summary>メインスレッドから呼ぶこと。SynchronizationContext をここでキャプチャする。</summary>
        public static void Start()
        {
            if (IsRunning) return;

            // 直前の Stop で取り残しがあれば掃除する。
            Stop();

            try
            {
                _syncCtx = SynchronizationContext.Current;

                // PipeDuplexServer は SimpleMcpServer.csproj にも Link されており
                // UnityEngine に依存できないため、出力先をここで与える。
                PipeDuplexServer.DiagnosticLog = message => Debug.Log(message);
                PipeDuplexChannel.DiagnosticLog = message => Debug.Log(message);

                var server = new PipeDuplexServer(PipeName);
                server.OnReceived           += OnReceived;
                server.OnClientConnected    += OnClientConnected;
                server.OnClientDisconnected += OnClientDisconnected;

                _server = server;
                _ = server.StartAsync();

                Debug.Log($"{LogTag} 起動: pipe={PipeName}");
            }
            catch (Exception ex)
            {
                _server = null;
                Debug.LogWarning($"{LogTag} 起動失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止する。ドメインリロード直前に呼ぶため、await せず同期的に完了させる。
        /// PipeDuplexServer.Dispose は CancellationTokenSource を Cancel し、
        /// 接続中クライアントを破棄し、IsListening を false にする
        /// （PipeDuplexServer.cs:152-167）。accept ループは
        /// WaitForConnectionAsync のキャンセルで抜ける（同 :208-212）。
        /// </summary>
        public static void Stop()
        {
            var server = _server;
            _server = null;

            if (server == null) return;

            try { server.OnReceived           -= OnReceived; }           catch { }
            try { server.OnClientConnected    -= OnClientConnected; }    catch { }
            try { server.OnClientDisconnected -= OnClientDisconnected; } catch { }
            try { server.Dispose(); } catch { }

            try { PipeDuplexServer.DiagnosticLog = null; } catch { }
            try { PipeDuplexChannel.DiagnosticLog = null; } catch { }

            Debug.Log($"{LogTag} 停止");
        }

        // ================================================================
        // 接続・切断
        // ================================================================

        /// <summary>
        /// クライアント接続。PipeDuplexServer.AcceptLoopAsync の受理直後に
        /// 背景スレッドで発火するため、EditorApplication には触れない。
        /// </summary>
        private static void OnClientConnected(IDuplexChannel channel)
        {
            Debug.Log($"{LogTag} 接続: id={channel?.Id}; 接続数={ClientCount()}");
        }

        /// <summary>
        /// クライアント切断。PipeDuplexChannel.ReceiveLoopAsync の finally から
        /// 背景スレッドで発火するため、EditorApplication には触れない。
        /// </summary>
        private static void OnClientDisconnected(IDuplexChannel channel)
        {
            Debug.Log($"{LogTag} 切断: id={channel?.Id}; 接続数={ClientCount()}");
        }

        /// <summary>サーバが保持しているクライアント数。停止済みなら -1。</summary>
        private static int ClientCount()
        {
            var server = _server;
            return server != null ? server.Clients.Length : -1;
        }

        // ================================================================
        // 受信
        // ================================================================

        private static void OnReceived(IDuplexChannel channel, DuplexMessage message)
        {
            // 応答を返せるのは Request だけ。Push は無視する。
            // MessageType は UnityEditor にも同名の型があるため完全修飾する（CS0104 回避）。
            if (message == null || message.Type != HagLib.NET.Duplex.MessageType.Request) return;

            var requestJson = message.PayloadString;

            // OnReceived は背景スレッド。Debug.Log は背景スレッドから呼べる。
            Debug.Log($"{LogTag} 受信: {requestJson}");

            RunOnMainThread(() =>
            {
                string responseJson;
                string action     = string.Empty;
                Action afterReply = null;

                try
                {
                    responseJson = Handle(requestJson, out afterReply, out action);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LogTag} 要求処理で例外: {ex.Message}");
                    responseJson = BuildError("", ex.Message);
                    afterReply   = null;
                }

                ReplyThen(channel, message, responseJson, afterReply, action);
            });
        }

        /// <summary>
        /// 応答を送出し、それが完了してから afterReply をメインスレッドで実行する。
        ///
        /// play / stop はドメインリロードを引き起こし、その際 Stop() がパイプを閉じる。
        /// 応答の書き込みが完了する前に遷移を起動すると、要求元は結果を受け取れない。
        /// そのため遷移は必ず ReplyAsync の完了後に回す。
        ///
        /// 送出完了ログは afterReply の有無にかかわらず出すため、
        /// ContinueWith は常に接続する。
        /// </summary>
        private static void ReplyThen(
            IDuplexChannel channel,
            DuplexMessage  request,
            string         responseJson,
            Action         afterReply,
            string         action)
        {
            Task replyTask;
            try
            {
                replyTask = channel.ReplyAsync(request, responseJson);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogTag} 応答失敗: action={action}; {ex.Message}");
                return;
            }

            replyTask.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    Debug.LogWarning($"{LogTag} 応答送出に失敗: action={action}; {t.Exception?.GetBaseException().Message}");
                    return;
                }

                Debug.Log($"{LogTag} 応答送出完了: action={action}; afterReply={(afterReply != null ? "あり" : "なし")}");

                if (afterReply == null) return;

                RunOnMainThread(afterReply);
            });
        }

        /// <summary>
        /// 背景スレッドからメインスレッドへ回す。
        /// SynchronizationContext が取れていない場合は EditorApplication.delayCall に積む
        /// （どちらもメインスレッドで 1 回だけ実行される。毎フレームポーリングはしない）。
        /// </summary>
        private static void RunOnMainThread(Action action)
        {
            if (action == null) return;

            var ctx = _syncCtx;
            if (ctx != null)
            {
                ctx.Post(_ =>
                {
                    try { action(); }
                    catch (Exception ex) { Debug.LogWarning($"{LogTag} メインスレッドエラー: {ex.Message}"); }
                }, null);
                return;
            }

            EditorApplication.delayCall += () =>
            {
                try { action(); }
                catch (Exception ex) { Debug.LogWarning($"{LogTag} メインスレッドエラー: {ex.Message}"); }
            };
        }

        // ================================================================
        // 要求の処理
        // ================================================================
        //
        // 要求: {"type":"command","action":"ping"|"play"|"stop"|"state"}
        // 応答: {"type":"response","success":true,"action":"...","data":"..."}
        //       {"type":"response","success":false,"action":"...","error":"..."}
        //
        // JSON は PolyLing.Runtime の JsonBuilder / JsonParser
        // （RemoteProtocol.cs:59,181）を使う。外部 JSON ライブラリは追加しない。
        // ================================================================

        /// <param name="afterReply">応答送出後にメインスレッドで実行する処理。不要なら null。</param>
        /// <param name="action">解決した action 名。ログ用に呼び出し側へ渡す。</param>
        private static string Handle(string requestJson, out Action afterReply, out string action)
        {
            afterReply = null;

            var msg = JsonParser.Parse(requestJson ?? string.Empty);
            var op  = msg.Action ?? string.Empty;

            action = op;

            Debug.Log($"{LogTag} 要求解決: action={op}");

            switch (op)
            {
                case "ping":
                    return LogResponse(BuildOk(op, "OK"));

                case "state":
                    return LogResponse(BuildOk(op, DescribeState()));

                case "play":
                    return LogResponse(HandlePlay(op, out afterReply));

                case "stop":
                    return LogResponse(HandleStop(op, out afterReply));

                default:
                    return LogResponse(BuildError(op, $"unknown action: {op}"));
            }
        }

        /// <summary>生成した応答 JSON をコンソールへ出力し、そのまま返す。</summary>
        private static string LogResponse(string responseJson)
        {
            Debug.Log($"{LogTag} 応答生成: {responseJson}");
            return responseJson;
        }

        // ================================================================
        // Play Mode 制御
        // ================================================================

        private static string HandlePlay(string op, out Action afterReply)
        {
            afterReply = null;

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"{LogTag} play 拒否: 既に Play モードです。{DescribeState()}");
                return BuildError(op, "既に Play モードです。" + DescribeState());
            }

            if (!IsEditorIdle(out var reason))
            {
                Debug.LogWarning($"{LogTag} play 拒否: {reason}{DescribeState()}");
                return BuildError(op, reason + DescribeState());
            }

            // EnterPlaymode は isPlaying = true と等価で、そのフレームの
            // スクリプトコードが完了してから遷移する。
            afterReply = () =>
            {
                Debug.Log($"{LogTag} play afterReply 開始: {DescribeState()}");

                // 応答送出中に状態が変わっていることがあるため、直前に再確認する。
                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Debug.LogWarning($"{LogTag} play afterReply 中止: 既に Play モードです。{DescribeState()}");
                    return;
                }

                if (!IsEditorIdle(out var afterReason))
                {
                    Debug.LogWarning($"{LogTag} play afterReply 中止: {afterReason}{DescribeState()}");
                    return;
                }

                Debug.Log($"{LogTag} EnterPlaymode 呼び出し前: {DescribeState()}");
                EditorApplication.EnterPlaymode();
                Debug.Log($"{LogTag} EnterPlaymode 呼び出し後: {DescribeState()}");
            };

            return BuildOk(op,
                "Play モードへの遷移を受理しました。この応答は遷移前に返ります。"
                + "結果の確認には state を使ってください。");
        }

        private static string HandleStop(string op, out Action afterReply)
        {
            afterReply = null;

            if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"{LogTag} stop 拒否: Play モードではありません。{DescribeState()}");
                return BuildError(op, "Play モードではありません。" + DescribeState());
            }

            if (EditorApplication.isCompiling)
            {
                Debug.LogWarning($"{LogTag} stop 拒否: スクリプトコンパイル中です。{DescribeState()}");
                return BuildError(op, "スクリプトコンパイル中です。" + DescribeState());
            }

            if (EditorApplication.isUpdating)
            {
                Debug.LogWarning($"{LogTag} stop 拒否: アセットデータベース更新中です。{DescribeState()}");
                return BuildError(op, "アセットデータベース更新中です。" + DescribeState());
            }

            afterReply = () =>
            {
                Debug.Log($"{LogTag} stop afterReply 開始: {DescribeState()}");

                if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Debug.LogWarning($"{LogTag} stop afterReply 中止: Play モードではありません。{DescribeState()}");
                    return;
                }

                Debug.Log($"{LogTag} ExitPlaymode 呼び出し前: {DescribeState()}");
                EditorApplication.ExitPlaymode();
                Debug.Log($"{LogTag} ExitPlaymode 呼び出し後: {DescribeState()}");
            };

            return BuildOk(op,
                "Edit モードへの遷移を受理しました。この応答は遷移前に返ります。"
                + "結果の確認には state を使ってください。");
        }

        /// <summary>
        /// いま Play モードを開始してよいか。不可なら理由を返す（可なら reason は空）。
        /// 判定条件は HierarchyExportClientWindow.cs:123-144 の CanExportNow に揃えている。
        /// Play 中かどうかは呼び出し側で先に見ているため、ここでは扱わない。
        /// </summary>
        private static bool IsEditorIdle(out string reason)
        {
            if (EditorApplication.isCompiling)
            {
                reason = "スクリプトコンパイル中です。";
                return false;
            }

            if (EditorApplication.isUpdating)
            {
                reason = "アセットデータベース更新中です。";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        /// <summary>現在のエディタ状態を 1 行の平文で返す。応答スキーマは変えない。</summary>
        private static string DescribeState()
        {
            return
                $"isPlaying={Flag(EditorApplication.isPlaying)}; "
                + $"isPlayingOrWillChangePlaymode={Flag(EditorApplication.isPlayingOrWillChangePlaymode)}; "
                + $"isPaused={Flag(EditorApplication.isPaused)}; "
                + $"isCompiling={Flag(EditorApplication.isCompiling)}; "
                + $"isUpdating={Flag(EditorApplication.isUpdating)}";
        }

        private static string Flag(bool value) => value ? "true" : "false";

        private static string BuildOk(string action, string data)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("type",    "response");
            jb.KeyValue("success", true);
            jb.KeyValue("action",  action);
            jb.KeyValue("data",    data);
            jb.EndObject();
            return jb.ToString();
        }

        private static string BuildError(string action, string error)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("type",    "response");
            jb.KeyValue("success", false);
            jb.KeyValue("action",  action);
            jb.KeyValue("error",   error);
            jb.EndObject();
            return jb.ToString();
        }
    }
}
