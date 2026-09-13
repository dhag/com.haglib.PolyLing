// Editor/EditorControl/PolyLingEditorControlServer.cs
// ============================================================
// 外部プロセス（SimpleUnityMcpServer）から Editor を操作するための受信サーバ
// ============================================================
//
// 【役割】
//   ローカルループバックの TCP ポート 1 本を待ち受け、1 行 JSON の要求に
//   1 行 JSON で応答する。
//
// 【なぜ名前付きパイプをやめたか】 2026-09-08
//   PipeDuplexServer では 3 回に 2 回、クライアントの ConnectAsync は成功するのに
//   サーバの WaitForConnectionAsync が返らず、要求が誰にも読まれない状態になった。
//   accepted カウンタで受理数を数えて確定した（4 回呼んで受理 2 回）。
//   受理前に次のリスナを立てる形へ直しても再現したため、
//   バックログを持つ TCP へ移した。TcpDuplexServer は Listen(100) の
//   バックログを持ち、AcceptAsync を繰り返すだけなので取りこぼしが起きない。
//   PipeDuplex* は削除せず残してある。
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
// 【アセット更新・再コンパイル】 2026-09-11
//   refresh / recompile を受ける。完了の判定方法は「アセット更新・再コンパイル」節の注記を参照。
//
// ============================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HagLib.NET.Duplex;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Remote;

namespace Poly_Ling.EditorControl
{
    /// <summary>外部プロセスからの Editor 操作要求を受け付けるループバック TCP サーバ。</summary>
    public static class PolyLingEditorControlServer
    {
        /// <summary>
        /// 待ち受けポート。クライアント側（SimpleUnityMcpServer）と一致させること。
        /// PolyLingPlayerServer（WebSocket）の既定 8765 と衝突しない値にしてある。
        /// </summary>
        public const int Port = 8770;

        /// <summary>コンソールログの共通接頭辞。</summary>
        private const string LogTag = "[PolyLingEditorControl]";

        private static TcpDuplexServer       _server;
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

                // Tcp/PipeDuplex* は SimpleMcpServer.csproj にも Link されており
                // UnityEngine に依存できないため、出力先をここで与える。
                TcpDuplexServer.DiagnosticLog = message => Debug.Log(message);

                var server = new TcpDuplexServer();
                server.OnReceived           += OnReceived;
                server.OnClientConnected    += OnClientConnected;
                server.OnClientDisconnected += OnClientDisconnected;

                _server = server;

                // 外部から届かないようループバックだけを bind する。
                _ = server.StartAsync(System.Net.IPAddress.Loopback, Port);

                Debug.Log($"{LogTag} 起動: port={Port}（ループバックのみ）");
            }
            catch (Exception ex)
            {
                _server = null;
                Debug.LogWarning($"{LogTag} 起動失敗: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止する。ドメインリロード直前に呼ぶため、await せず同期的に完了させる。
        /// TcpDuplexServer.Dispose は CancellationTokenSource を Cancel し、
        /// リスナを閉じ、接続中クライアントを破棄する。
        /// accept ループは AcceptAsync の ObjectDisposedException で抜ける。
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

            try { TcpDuplexServer.DiagnosticLog = null; } catch { }

            Debug.Log($"{LogTag} 停止");
        }

        // ================================================================
        // 接続・切断
        // ================================================================

        /// <summary>
        /// クライアント接続。TcpDuplexServer.AcceptLoopAsync の受理直後に
        /// 背景スレッドで発火するため、EditorApplication には触れない。
        /// </summary>
        private static void OnClientConnected(IDuplexChannel channel)
        {
            Debug.Log($"{LogTag} 接続: id={channel?.Id}; 接続数={ClientCount()}");
        }

        /// <summary>
        /// クライアント切断。TcpDuplexChannel.ReceiveLoopAsync の finally から
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
        /// play / stop はドメインリロードを引き起こし、その際 Stop() が接続を閉じる。
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
        // 要求: {"type":"command","action":"ping"|"play"|"stop"|"state"|"tools"|"call"
        //                                  |"refresh"|"recompile"}
        //       call のみ "params" を伴う（HandleCall の注記を参照）
        // 応答: {"type":"response","success":true,"action":"...","data":"..."}
        //       {"type":"response","success":false,"action":"...","error":"..."}
        //
        // "data" は必ず文字列。call がコマンドの戻り値を返すときは、
        // オブジェクトを入れる別のキー "result" を使う（HandleCall の注記を参照）。
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

                case "tools":
                    return LogResponse(HandleTools(op));

                case "call":
                    return LogResponse(HandleCall(op, msg));

                case "play":
                    return LogResponse(HandlePlay(op, out afterReply));

                case "stop":
                    return LogResponse(HandleStop(op, out afterReply));

                case "refresh":
                    return LogResponse(HandleRefresh(op, false, out afterReply));

                case "recompile":
                    return LogResponse(HandleRefresh(op, true, out afterReply));

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
        // コマンド実行（MCP）
        // ================================================================
        //
        // 要求: {"type":"command","action":"call",
        //        "params":{"command":"smoothEdges","modelIndex":"0","strength":"0.5"}}
        //
        //   command    … 道具名（PanelCommandFactory.ActionOf が作る名前）
        //   modelIndex … 対象モデル。省くと 0
        //   それ以外   … コマンドの引数。入れ子はドット区切り（params.widthTop）
        //
        // JsonParser.ParseFlat は値が '[' や '{' で始まるものを辞書へ入れない
        // （RemoteProtocol.cs の ParseFlat）。配列は "1,2,3" のように
        // 文字列 1 個で送ること。
        //
        // 本サーバはモデルに触れない。実行は PolyLingCommandGateway 経由で、
        // パネルが開いていなければ理由付きで失敗する。
        // ================================================================

        /// <summary>道具一覧（JSON Schema）をそのまま返す。</summary>
        private static string HandleTools(string op)
        {
            try
            {
                string json = PanelCommandFactory.BuildToolsListJson();
                PanelCommandFactory.CountTools(out int usable, out int skipped);

                Debug.Log($"{LogTag} tools: 出せる {usable} / 出せない {skipped}");

                var jb = new JsonBuilder();
                jb.BeginObject();
                jb.KeyValue("type",    "response");
                jb.KeyValue("success", true);
                jb.KeyValue("action",  op);
                jb.KeyValue("usable",  usable);
                jb.KeyValue("skipped", skipped);
                jb.KeyRaw("tools", json);
                jb.EndObject();
                return jb.ToString();
            }
            catch (Exception ex)
            {
                return BuildError(op, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>コマンドを組み立てて実行する。メインスレッドで呼ばれる。</summary>
        private static string HandleCall(string op, RemoteMessage msg)
        {
            var raw = msg?.Params;
            if (raw == null || raw.Count == 0)
                return BuildError(op, "params がありません");

            if (!raw.TryGetValue("command", out string command) || string.IsNullOrEmpty(command))
                return BuildError(op, "params.command がありません");

            int modelIndex = 0;
            if (raw.TryGetValue("modelIndex", out string mi) && !string.IsNullOrEmpty(mi))
            {
                if (!int.TryParse(mi, out modelIndex))
                    return BuildError(op, $"modelIndex を整数にできません: {mi}");
            }

            // command / modelIndex はコマンドの引数ではないので外す。
            var args = new Dictionary<string, string>();
            foreach (var kv in raw)
            {
                if (kv.Key == "command" || kv.Key == "modelIndex") continue;
                args[kv.Key] = kv.Value;
            }

            if (!PolyLingCommandGateway.IsReady)
                return BuildError(op, "PolyLing のパネルが開いていません");

            var cmd = PanelCommandFactory.Create(command, modelIndex, args, out string createError);
            if (cmd == null)
                return BuildError(op, createError ?? "コマンドを組み立てられませんでした");

            Debug.Log($"{LogTag} call: {PanelCommandDump.Describe(cmd)}");

            var result = PolyLingCommandGateway.Invoke(cmd);
            if (result == null)
                return BuildError(op, "結果がありません");

            if (!result.Success)
                return BuildError(op, result.Reason ?? "unknown error");

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("type",    "response");
            jb.KeyValue("success", true);
            jb.KeyValue("action",  op);
            jb.KeyValue("command", command);
            AppendIntArray(jb, "masterIndices", result.MasterIndices);
            AppendUlongArray(jb, "objectIds",   result.ObjectIds);

            // コマンドが返した実データ。既に JSON オブジェクトの文字列なので
            // そのまま差し込む（tools と同じ KeyRaw の経路）。
            //
            // 【なぜ "data" ではないか】
            //   "data" は BuildOk が文字列を入れる欄で、ping / state / play / stop が
            //   使っている。クライアント側の EditorControlClient.Parse は
            //   d.GetString() で無条件に文字列として読むため、同じ欄へ
            //   オブジェクトを入れると InvalidOperationException で落ちる
            //   （JsonElement.GetString は JSON 文字列以外を変換しない）。
            //   call は生 JSON がそのまま呼び出し側へ渡るので、
            //   Parse が触らない別のキーにしておけば型の食い違いが起きない。
            //   C# 側のプロパティ名は CommandResult.Data のままにしてある。
            if (!string.IsNullOrEmpty(result.Data)) jb.KeyRaw("result", result.Data);

            jb.EndObject();
            return jb.ToString();
        }

        /// <summary>int 配列を JSON 配列として足す。null・空なら足さない。</summary>
        private static void AppendIntArray(JsonBuilder jb, string key, int[] values)
        {
            if (values == null || values.Length == 0) return;

            jb.Key(key);
            jb.BeginArray();
            for (int i = 0; i < values.Length; i++) jb.Value(values[i]);
            jb.EndArray();
        }

        /// <summary>
        /// ulong 配列を JSON 配列として足す。null・空なら足さない。
        /// JsonBuilder に ulong の Value が無いため、数値のまま RawValue で入れる
        /// （文字列にすると受け側で型が変わる）。
        /// </summary>
        private static void AppendUlongArray(JsonBuilder jb, string key, ulong[] values)
        {
            if (values == null || values.Length == 0) return;

            jb.Key(key);
            jb.BeginArray();
            for (int i = 0; i < values.Length; i++)
                jb.RawValue(values[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
            jb.EndArray();
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

        // ================================================================
        // アセット更新・再コンパイル
        // ================================================================
        //
        // refresh   … AssetDatabase.Refresh() のあと CompilationPipeline.RequestScriptCompilation()。
        // recompile … CompilationPipeline.RequestScriptCompilation(CleanBuildCache)。
        //             変更が無くても全スクリプトを再コンパイルする。
        //
        // 【なぜ refresh でも RequestScriptCompilation を呼ぶか】
        //   AssetDatabase.Refresh は取り込みを同期で、スクリプトのコンパイルを非同期で行う
        //   （Unity 6 スクリプトリファレンス AssetDatabase.Refresh）。Refresh が戻った時点で
        //   コンパイルが終わっている保証は無く、isCompiling だけでは完了を判定できない。
        //   RequestScriptCompilation はコンパイル要求をその場で立て、isCompiling は
        //   要求が立っている間も true を返す（UnityCsReference 6000.0：
        //   CompilationPipeline.cs:674-675、EditorCompilation.cs:199-207, 1062-1069）。
        //   よって「refreshDone が受付番号に達し、isCompiling と isUpdating がともに false」
        //   になった時点で、取り込みとコンパイルは終わっている。
        //
        // 【受付番号と結果】
        //   受付時に番号を振って応答で返し、afterReply の最後に refreshDone として記録する。
        //   結果（executed / skipped / failed）は refreshResult に残す。
        //   どちらも state の末尾に載る。
        //   置き場は SessionState。静的フィールドはドメインリロードで初期化されるが、
        //   SessionState はリロードをまたいで残る（Unity の終了で消える）。
        //
        // 【Play 中は受けない】
        //   再生中の再コンパイルでドメインリロードが走り、PolyLing の実行入口が
        //   失われたことがある。受付時と実行直前の 2 回、Play 中かどうかを見る。
        //
        // 【実行は応答の後】
        //   コンパイルが終わるとドメインリロードで本サーバが止まる。
        //   play / stop と同じく、応答を送り終えてから afterReply で実行する。
        // ================================================================

        private const string RefreshRequestedKey = "PolyLing.EditorControl.RefreshRequested";
        private const string RefreshDoneKey      = "PolyLing.EditorControl.RefreshDone";
        private const string RefreshResultKey    = "PolyLing.EditorControl.RefreshResult";

        /// <param name="clean">true で recompile（ビルドキャッシュを捨てて全スクリプトを再コンパイル）。</param>
        private static string HandleRefresh(string op, bool clean, out Action afterReply)
        {
            afterReply = null;

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"{LogTag} {op} 拒否: Play モード中です。{DescribeState()}");
                return BuildError(op, "Play モード中は受け付けません。先に stop してください。" + DescribeState());
            }

            if (!IsEditorIdle(out var reason))
            {
                Debug.LogWarning($"{LogTag} {op} 拒否: {reason}{DescribeState()}");
                return BuildError(op, reason + DescribeState());
            }

            int id = SessionState.GetInt(RefreshRequestedKey, 0) + 1;
            SessionState.SetInt(RefreshRequestedKey, id);

            afterReply = () =>
            {
                Debug.Log($"{LogTag} {op} afterReply 開始: id={id}; {DescribeState()}");

                string result;

                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    // 応答を返してからここまでの間に Play が始まった。
                    result = "skipped: 実行前に Play モードへ入ったため実行しませんでした。";
                    Debug.LogWarning($"{LogTag} {op} afterReply 中止: id={id}; {DescribeState()}");
                }
                else
                {
                    try
                    {
                        if (clean)
                        {
                            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(
                                UnityEditor.Compilation.RequestScriptCompilationOptions.CleanBuildCache);
                        }
                        else
                        {
                            AssetDatabase.Refresh();
                            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
                        }

                        result = "executed";
                    }
                    catch (Exception ex)
                    {
                        result = $"failed: {ex.GetType().Name}: {ex.Message}";
                        Debug.LogWarning($"{LogTag} {op} afterReply 例外: id={id}; {ex}");
                    }
                }

                // 結果を先に書き、refreshDone を最後に書く。
                // どちらもメインスレッドで書き、state もメインスレッドで読むので、
                // refreshDone が id に達していれば refreshResult はこの要求のもの。
                SessionState.SetString(RefreshResultKey, result);
                SessionState.SetInt(RefreshDoneKey, id);

                Debug.Log($"{LogTag} {op} afterReply 終了: id={id}; {DescribeState()}");
            };

            return BuildOk(op,
                $"id={id}; "
                + (clean ? "全スクリプトの再コンパイルを受理しました。" : "アセット更新を受理しました。")
                + "この応答は実行前に返ります。完了は state の refreshDone が id 以上になり、"
                + "isCompiling と isUpdating がともに false になったことで確認してください。");
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
                + $"isUpdating={Flag(EditorApplication.isUpdating)}; "
                + $"accepted={(_server != null ? _server.AcceptedCount : -1)}; "
                + $"clients={ClientCount()}; "
                + $"refreshDone={SessionState.GetInt(RefreshDoneKey, 0)}; "
                // refreshResult は例外文を含みうるので末尾に置き、改行だけ潰す。
                // 読む側は refreshResult= から行末までを 1 つの値として扱う。
                + $"refreshResult={OneLine(SessionState.GetString(RefreshResultKey, string.Empty))}";
        }

        /// <summary>改行を空白にして 1 行にする。</summary>
        private static string OneLine(string text)
            => string.IsNullOrEmpty(text) ? string.Empty : text.Replace("\r", " ").Replace("\n", " ");

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

        // ================================================================
        // 【外から動かすときに画面が止まる件・調査済み】
        //
        //   症状: MCP からモデルを作っても、Unity のウインドウが前面でないと
        //         画面に出ない。作ったものが見えず、動いていないように見える。
        //
        //   原因: Player Settings の Run In Background がオフだと、
        //         Editor は再生中でも背面ではフレームを進めない。
        //         これを有効にすれば背面のままでも正しく描かれる（確認済み）。
        //
        //   ここでフレームを進める仕掛けは持たないこと。試した結果は次のとおり。
        //     - EditorApplication.QueuePlayerLoopUpdate()
        //         Run In Background がオフのときは効かなかった。
        //         オンなら不要なので、どちらにせよ置く意味が無い。
        //     - UnityEditorInternal.InternalEditorUtility.RepaintAllViews()
        //         全ビューの描き直しを同期的に要求する。コマンドを受け付けて
        //         いる最中から呼ぶと絡んで Editor が応答しなくなる（実際に固めた）。
        //         呼んではいけない。
        // ================================================================

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
