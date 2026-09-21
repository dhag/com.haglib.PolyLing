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
            Debug.Log($"{LogTag} 受信: {ClipForLog(requestJson)}");

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
        //                                  |"tools_generic"|"tools_search"|"tools_describe"
        //                                  |"refresh"|"recompile"
        //                                  |"prefab_export"|"prefab_instantiate"|"prefab_import"}
        //       call と prefab_* は "params" を伴う（HandleCall・「プレファブ」節の注記を参照）
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

                case "tools_generic":
                    return LogResponse(HandleToolsGeneric(op));

                case "tools_search":
                    return LogResponse(HandleToolsSearch(op, msg));

                case "tools_describe":
                    return LogResponse(HandleToolsDescribe(op, msg));

                case "scenes":
                    return LogResponse(HandleScenes(op));

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

                case "prefab_export":
                    return LogResponse(HandlePrefabExport(op, msg));

                case "prefab_instantiate":
                    return LogResponse(HandlePrefabInstantiate(op, msg));

                case "prefab_import":
                    return LogResponse(HandlePrefabImport(op, msg));

                default:
                    return LogResponse(BuildError(op, $"unknown action: {op}"));
            }
        }

        /// <summary>生成した応答 JSON をコンソールへ出力し、そのまま返す。</summary>
        private static string LogResponse(string responseJson)
        {
            Debug.Log($"{LogTag} 応答生成: {ClipForLog(responseJson)}");
            return responseJson;
        }

        /// <summary>コンソールへ出す要求・応答の最大文字数。</summary>
        private const int MaxLoggedJsonChars = 2000;

        /// <summary>
        /// 要求・応答をコンソールへ出すときに切り詰める。
        ///
        /// tools の応答は全コマンドの Schema を含み数百 KB になる。全文を Debug.Log すると
        /// Editor.log がその文字列で埋まり、read_unity_log で末尾を読んだときに
        /// コンパイルエラーや例外がその後ろへ押し出される。
        /// 応答そのものは切り詰めない（ここで切るのはログに出す文字列だけ）。
        /// </summary>
        private static string ClipForLog(string json)
        {
            if (json == null || json.Length <= MaxLoggedJsonChars) return json;
            return json.Substring(0, MaxLoggedJsonChars) + $"…(+{json.Length - MaxLoggedJsonChars} chars)";
        }

        // ================================================================
        // コマンド実行（MCP）
        // ================================================================
        //
        // 要求: {"type":"command","action":"call",
        //        "params":{"command":"smoothEdges","modelIndex":"0","strength":"0.5"}}
        //
        //   command      … 道具名（PanelCommandFactory.ActionOf が作る名前）
        //   modelIndex   … 対象モデル。省くと 0
        //   wantRevision … 1 のとき応答に modelRevision / createdObjectIds / deletedObjectIds を足す
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

        // ================================================================
        // 道具一覧の段階的な取得（MCP のプロファイル別）
        // ================================================================
        //
        // tools は現行（profile=current）の応答をそのまま保つため変えない。
        // 以下 3 つはどれも "result" にオブジェクトを入れて返す（call と同じ置き場）。
        //
        //   tools_generic  … profile=generic 用。全件 + schemaRevision
        //                     {"schemaRevision":"sha256:…","usable":N,"skipped":M,"tools":[…]}
        //   tools_search   … profile=optimized 用。params: query / category / scene / offset / limit
        //   tools_describe … profile=optimized 用。params: names（カンマ区切り、最大 10 件）
        //   scenes         … profile=optimized 用。利用シーン（SceneLibrary）の定義を全部返す
        //                     {"storePath":"…","scenes":[{"name","description","commands":[],"categories":[],"tags":[],"tools":[]}]}
        //                     サーバが固定の道具を絞るのに使う。パネルが開いていなくても答える。
        // ================================================================

        /// <summary>tools_search の limit の既定と上限。</summary>
        private const int ToolsSearchDefaultLimit = 8;
        private const int ToolsSearchMaxLimit     = 50;

        /// <summary>tools_describe で一度に引ける件数の上限。</summary>
        private const int ToolsDescribeMaxNames = 10;

        private static string HandleToolsGeneric(string op)
        {
            try
            {
                string json = PanelCommandFactory.BuildToolsListJson();
                PanelCommandFactory.CountTools(out int usable, out int skipped);

                var inner = new JsonBuilder();
                inner.BeginObject();
                inner.KeyValue("schemaRevision", PanelCommandFactory.ComputeSchemaRevision(json));
                inner.KeyValue("usable",  usable);
                inner.KeyValue("skipped", skipped);
                inner.KeyRaw("tools", json);
                inner.EndObject();

                return BuildResult(op, inner.ToString());
            }
            catch (Exception ex)
            {
                return BuildError(op, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string HandleToolsSearch(string op, RemoteMessage msg)
        {
            try
            {
                var p = msg?.Params;
                string query     = GetParam(p, "query");
                string category  = GetParam(p, "category");
                string sceneName = GetParam(p, "scene");

                SceneDefinition scene = null;
                if (!string.IsNullOrWhiteSpace(sceneName))
                {
                    scene = SceneLibrary.Get(sceneName.Trim());
                    if (scene == null)
                        return BuildError(op, $"利用シーンがありません: {sceneName}");
                }

                int offset = 0;
                int limit  = ToolsSearchDefaultLimit;

                string offsetText = GetParam(p, "offset");
                if (!string.IsNullOrEmpty(offsetText) && !int.TryParse(offsetText, out offset))
                    return BuildError(op, $"offset を整数にできません: {offsetText}");

                string limitText = GetParam(p, "limit");
                if (!string.IsNullOrEmpty(limitText) && !int.TryParse(limitText, out limit))
                    return BuildError(op, $"limit を整数にできません: {limitText}");

                offset = Math.Max(0, offset);
                limit  = Math.Clamp(limit, 1, ToolsSearchMaxLimit);

                return BuildResult(op, PanelCommandFactory.BuildToolsSearchJson(query, category, scene, offset, limit));
            }
            catch (Exception ex)
            {
                return BuildError(op, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string HandleToolsDescribe(string op, RemoteMessage msg)
        {
            try
            {
                string namesText = GetParam(msg?.Params, "names");
                if (string.IsNullOrWhiteSpace(namesText))
                    return BuildError(op, "params.names がありません（カンマ区切りのコマンド名）");

                var names = new List<string>();
                foreach (var part in namesText.Split(','))
                {
                    var n = part.Trim();
                    if (n.Length > 0 && !names.Contains(n)) names.Add(n);
                }

                if (names.Count == 0)
                    return BuildError(op, "params.names が空です");
                if (names.Count > ToolsDescribeMaxNames)
                    return BuildError(op, $"names は {ToolsDescribeMaxNames} 件までです（{names.Count} 件）");

                return BuildResult(op, PanelCommandFactory.BuildToolsDescribeJson(names));
            }
            catch (Exception ex)
            {
                return BuildError(op, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string HandleScenes(string op)
        {
            try
            {
                var inner = new JsonBuilder();
                inner.BeginObject();
                inner.KeyValue("storePath", SceneLibrary.StorePath);
                inner.Key("scenes");
                inner.BeginArray();
                foreach (var s in SceneLibrary.GetAll())
                {
                    inner.BeginObject();
                    inner.KeyValue("name",        s.Name);
                    inner.KeyValue("description", s.Description);
                    AppendStringArray(inner, "commands",   s.Commands);
                    AppendStringArray(inner, "categories", s.Categories);
                    AppendStringArray(inner, "tags",       s.Tags);
                    AppendStringArray(inner, "tools",      s.Tools);
                    inner.EndObject();
                }
                inner.EndArray();
                inner.EndObject();
                return BuildResult(op, inner.ToString());
            }
            catch (Exception ex)
            {
                return BuildError(op, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>文字列の配列を足す。空でも [] を書く（受け側が有無を判定しなくて済む）。</summary>
        private static void AppendStringArray(JsonBuilder jb, string key, List<string> values)
        {
            jb.Key(key);
            jb.BeginArray();
            if (values != null)
                foreach (var v in values) jb.Value(v);
            jb.EndArray();
        }

        private static string GetParam(Dictionary<string, string> p, string key)
            => p != null && p.TryGetValue(key, out string v) ? v : null;

        /// <summary>成功応答の "result" に JSON オブジェクトを入れて返す。</summary>
        private static string BuildResult(string op, string resultJson)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("type",    "response");
            jb.KeyValue("success", true);
            jb.KeyValue("action",  op);
            jb.KeyRaw("result", resultJson);
            jb.EndObject();
            return jb.ToString();
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

            // 封筒の値。コマンドの引数ではないので外す。
            //   wantRevision … 応答に版と差分（modelRevision / createdObjectIds / deletedObjectIds）を足す。
            //                  既定は付けない。付けると応答が変わるので、要求した呼び出しにだけ足す。
            bool wantRevision = raw.TryGetValue("wantRevision", out string wr)
                                && (wr == "1" || string.Equals(wr, "true", StringComparison.OrdinalIgnoreCase));

            var args = new Dictionary<string, string>();
            foreach (var kv in raw)
            {
                if (kv.Key == "command" || kv.Key == "modelIndex" || kv.Key == "wantRevision") continue;
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

            if (wantRevision)
            {
                jb.KeyValue("modelRevision", result.ModelRevision);
                AppendUlongArray(jb, "createdObjectIds", result.CreatedObjectIds);
                AppendUlongArray(jb, "deletedObjectIds", result.DeletedObjectIds);
            }

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

        // ================================================================
        // プレファブ（書き出し・ヒエラルキーへの登録・取り込み）
        // ================================================================
        //
        // prefab_export      … プロジェクトファイル → プレファブ
        //   params: modelFolder（必須。作業フォルダ基準。PLSandbox を通す）
        //           outputRoot（任意。Assets/ 以下）
        //           createArmature / useBindpose / exportVisibleOnly / includeInvisibleAncestors /
        //           exportMeshOnly / exportPhysics / buildAvatar / attachAnimator /
        //           supplementHumanoid / writeAttach / tolerantMirrorBranch（任意。true/false）
        //           rendererMode（任意。Auto / ForceMeshFilter）
        //           animatorController（任意。Assets/ 以下のアセットパス）
        //   省いたオプションは HierarchyExportOptions の既定値。
        //
        // prefab_instantiate … プレファブ → シーンのヒエラルキー
        //   params: prefabPath（必須。Assets/... .prefab）
        //           parentPath（任意。"Root/Child" 形式）
        //           position（任意。"x,y,z"。親からの位置）
        //
        // prefab_import      … プレファブ → プロジェクトファイル
        //   params: prefabPath（必須。Assets/... .prefab）
        //           outputFolder（必須。作業フォルダ基準。PLSandbox を通す）
        //           detectNamedMirror / restoreHumanoid（任意。true/false。既定 true）
        //
        // 【Play 中は受けない】
        //   Play 中に作った GameObject は Play 終了で破棄される
        //   （HierarchyExportClientWindow.CanExportNow と同じ理由）。
        //   Play 中なら先に stop するのは要求元（MCP サーバ）の役目。
        //
        // 【ダイアログを出さない】
        //   メインスレッドがダイアログで止まると応答が返らない。
        //   本体（HierarchyPrefabExporter / HierarchyImportWindow.ImportToProjectFile /
        //   PrefabInstantiateOps）はダイアログを出さず、文言を返す。
        // ================================================================

        private static string HandlePrefabExport(string op, RemoteMessage msg)
        {
            if (!CheckEditModeIdle(op, out string busy)) return busy;

            var raw = msg?.Params;

            if (!TryGetParam(raw, "modelFolder", out string modelFolder))
                return BuildError(op, "params.modelFolder がありません");

            if (!Poly_Ling.Core.PLSandbox.TryResolveFolder(modelFolder, out string modelFull, out string reason))
                return BuildError(op, "modelFolder: " + reason);

            var opt = new Poly_Ling.EditorIO.HierarchyExportOptions { SaveAsPrefab = true };

            if (TryGetParam(raw, "outputRoot", out string outputRoot))
            {
                if (!TryNormalizeAssetsPath(outputRoot, null, out string normalizedRoot, out reason))
                    return BuildError(op, "outputRoot: " + reason);
                opt.PrefabOutputRoot = normalizedRoot;
            }

            string err = null;
            ReadBool(raw, "createArmature",            ref opt.CreateArmature,            ref err);
            ReadBool(raw, "useBindpose",               ref opt.UseBindpose,               ref err);
            ReadBool(raw, "exportVisibleOnly",         ref opt.ExportVisibleOnly,         ref err);
            ReadBool(raw, "includeInvisibleAncestors", ref opt.IncludeInvisibleAncestors, ref err);
            ReadBool(raw, "exportMeshOnly",            ref opt.ExportMeshOnly,            ref err);
            ReadBool(raw, "exportPhysics",             ref opt.ExportPhysics,             ref err);
            ReadBool(raw, "buildAvatar",               ref opt.BuildAvatar,               ref err);
            ReadBool(raw, "attachAnimator",            ref opt.AttachAnimator,            ref err);
            ReadBool(raw, "supplementHumanoid",        ref opt.SupplementHumanoid,        ref err);
            ReadBool(raw, "writeAttach",               ref opt.WriteAttach,               ref err);
            ReadBool(raw, "tolerantMirrorBranch",      ref opt.TolerantMirrorBranch,      ref err);
            if (err != null) return BuildError(op, err);

            if (TryGetParam(raw, "rendererMode", out string rendererMode))
            {
                if (!Enum.TryParse(rendererMode, true, out Poly_Ling.HierarchyIO.HierarchyRendererMode mode)
                    || !Enum.IsDefined(typeof(Poly_Ling.HierarchyIO.HierarchyRendererMode), mode))
                    return BuildError(op, "rendererMode は Auto / ForceMeshFilter のどちらかです: " + rendererMode);
                opt.RendererMode = mode;
            }

            if (TryGetParam(raw, "animatorController", out string controllerPath))
            {
                if (!TryNormalizeAssetsPath(controllerPath, null, out string normalizedController, out reason))
                    return BuildError(op, "animatorController: " + reason);

                opt.AnimatorController =
                    AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(normalizedController);
                if (opt.AnimatorController == null)
                    return BuildError(op, "Animator Controller を読み込めません: " + normalizedController);
            }

            var outcome = new Poly_Ling.EditorIO.HierarchyPrefabExporter(opt).ExportFolder(modelFull);

            string text =
                $"{outcome.Title}\nprefabs={string.Join(",", outcome.PrefabPaths)}\n{outcome.Text}";

            return outcome.Success ? BuildOk(op, text) : BuildError(op, text);
        }

        private static string HandlePrefabInstantiate(string op, RemoteMessage msg)
        {
            if (!CheckEditModeIdle(op, out string busy)) return busy;

            var raw = msg?.Params;

            if (!TryGetParam(raw, "prefabPath", out string prefabPath))
                return BuildError(op, "params.prefabPath がありません");

            if (!TryNormalizeAssetsPath(prefabPath, ".prefab", out string normalizedPrefab, out string reason))
                return BuildError(op, "prefabPath: " + reason);

            TryGetParam(raw, "parentPath", out string parentPath);

            Vector3? position = null;
            if (TryGetParam(raw, "position", out string positionText))
            {
                if (!TryParseVector3(positionText, out Vector3 p))
                    return BuildError(op, "position は \"x,y,z\" の形で指定してください: " + positionText);
                position = p;
            }

            bool ok = Poly_Ling.EditorIO.PrefabInstantiateOps.Instantiate(
                normalizedPrefab, parentPath, position, out _, out string message);

            return ok ? BuildOk(op, message) : BuildError(op, message);
        }

        private static string HandlePrefabImport(string op, RemoteMessage msg)
        {
            if (!CheckEditModeIdle(op, out string busy)) return busy;

            var raw = msg?.Params;

            if (!TryGetParam(raw, "prefabPath", out string prefabPath))
                return BuildError(op, "params.prefabPath がありません");

            if (!TryNormalizeAssetsPath(prefabPath, ".prefab", out string normalizedPrefab, out string reason))
                return BuildError(op, "prefabPath: " + reason);

            if (!TryGetParam(raw, "outputFolder", out string outputFolder))
                return BuildError(op, "params.outputFolder がありません");

            if (!Poly_Ling.Core.PLSandbox.TryResolveFolder(outputFolder, out string outputFull, out reason))
                return BuildError(op, "outputFolder: " + reason);

            bool detectNamedMirror = true;
            bool restoreHumanoid   = true;
            string err = null;
            ReadBool(raw, "detectNamedMirror", ref detectNamedMirror, ref err);
            ReadBool(raw, "restoreHumanoid",   ref restoreHumanoid,   ref err);
            if (err != null) return BuildError(op, err);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(normalizedPrefab);
            if (prefab == null)
                return BuildError(op, "プレファブを読み込めません: " + normalizedPrefab);

            bool ok = Poly_Ling.EditorIO.HierarchyImportWindow.ImportToProjectFile(
                prefab, null, outputFull, detectNamedMirror, restoreHumanoid,
                out string title, out string message);

            string text = $"{title}\n{message}";
            return ok ? BuildOk(op, text) : BuildError(op, text);
        }

        /// <summary>Edit モードで、コンパイル中・インポート中でなければ true。不可なら応答 JSON を返す。</summary>
        private static bool CheckEditModeIdle(string op, out string errorResponse)
        {
            errorResponse = null;

            if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning($"{LogTag} {op} 拒否: Play モード中です。{DescribeState()}");
                errorResponse = BuildError(op, "Play モード中は受け付けません。先に stop してください。" + DescribeState());
                return false;
            }

            if (!IsEditorIdle(out var reason))
            {
                Debug.LogWarning($"{LogTag} {op} 拒否: {reason}{DescribeState()}");
                errorResponse = BuildError(op, reason + DescribeState());
                return false;
            }

            return true;
        }

        /// <summary>空でない値があれば true。</summary>
        private static bool TryGetParam(Dictionary<string, string> raw, string key, out string value)
        {
            value = null;
            if (raw == null) return false;
            if (!raw.TryGetValue(key, out value)) return false;
            if (string.IsNullOrWhiteSpace(value)) { value = null; return false; }
            value = value.Trim();
            return true;
        }

        /// <summary>
        /// true / false を読む。キーが無ければ target を変えない。
        /// 解釈できなければ err に理由を入れる（先に入った理由は上書きしない）。
        /// </summary>
        private static void ReadBool(Dictionary<string, string> raw, string key, ref bool target, ref string err)
        {
            if (!TryGetParam(raw, key, out string text)) return;

            if (bool.TryParse(text, out bool value))
            {
                target = value;
                return;
            }

            if (err == null) err = $"{key} は true / false で指定してください: {text}";
        }

        /// <summary>
        /// "Assets" 以下のアセットパスへ正規化する。
        /// 区切りを '/' にそろえ、".." を含むもの・Assets の外を拒否する。
        /// requiredExtension を渡した場合は拡張子も検査する。
        /// </summary>
        private static bool TryNormalizeAssetsPath(
            string path, string requiredExtension, out string normalized, out string reason)
        {
            normalized = null;
            reason     = null;

            string p = (path ?? string.Empty).Replace('\\', '/').Trim().TrimEnd('/');

            if (p != "Assets" && !p.StartsWith("Assets/", StringComparison.Ordinal))
            {
                reason = "Assets/ 以下のパスを指定してください: " + path;
                return false;
            }

            foreach (string segment in p.Split('/'))
            {
                if (segment == ".." || segment == "." || segment.Length == 0)
                {
                    reason = "パスに空・\".\"・\"..\" の要素は使えません: " + path;
                    return false;
                }
            }

            if (requiredExtension != null
                && !p.EndsWith(requiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                reason = $"拡張子は {requiredExtension} にしてください: {path}";
                return false;
            }

            normalized = p;
            return true;
        }

        /// <summary>"x,y,z" を読む。</summary>
        private static bool TryParseVector3(string text, out Vector3 value)
        {
            value = Vector3.zero;

            string[] parts = text.Split(',');
            if (parts.Length != 3) return false;

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var style = System.Globalization.NumberStyles.Float;

            if (!float.TryParse(parts[0].Trim(), style, inv, out float x)) return false;
            if (!float.TryParse(parts[1].Trim(), style, inv, out float y)) return false;
            if (!float.TryParse(parts[2].Trim(), style, inv, out float z)) return false;

            value = new Vector3(x, y, z);
            return true;
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
