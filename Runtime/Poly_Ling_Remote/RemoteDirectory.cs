// Remote/RemoteDirectory.cs
// ============================================================
// パネルのリモートサーバの複数起動対応：サーバ一覧（マスター）
// ============================================================
//
// 【しくみ】
//   マスター用ポート（MasterPort）を 1 本だけ決めておく。
//   各サーバ（RemoteServerCore）は起動時にこのポートの Bind を試みる。
//     成功 → 自分がマスター。一覧の受付を行い、自分自身も一覧に載せる。
//     失敗 → 既にマスターがいる。そこへ接続して register し、接続を張ったままにする。
//   Bind の一意性は OS が保証する（同一アドレス・同一ポートへの 2 本目の Bind は失敗する）。
//   ここで SO_EXCLUSIVEADDRUSE は使わない。マスター側が先に閉じた接続は
//   TIME_WAIT に残り、排他指定だとその間このポートへ再 Bind できず、移譲が止まるため。
//
// 【登録の抹消】
//   登録用の接続が切れたら一覧から外す。心拍・ポーリングは行わない。
//
// 【移譲】
//   状態は受け渡さない。マスターとの接続が切れたメンバーは Bind からやり直し、
//   勝った 1 台が新マスターになり、残りはそこへ登録し直す。一覧は再登録で組み直される。
//   新マスターの待ち受けが立つ前の接続失敗に限り、短い間隔で上限付きの再試行を行う。
//
// 【一覧の鮮度】
//   list 要求を受けたマスターは、各メンバーへ info を問い合わせて最新の情報を返す
//   （プロジェクト名は起動後に変わるため）。応答が無ければ登録時の情報を返す。
//
// 【スレッド】
//   本クラスの処理は背景スレッドで走る。UnityEngine には依存しない。
//   自分の情報は SelfInfoAsync で受け取る（ホスト側がメインスレッドで組み立てる）。
//
// 【プロトコル】 1 要求 1 応答の JSON（DuplexMessage の Request/Response）
//   {"action":"register","server":{...}} → {"ok":true}
//   {"action":"list"}                    → {"ok":true,"servers":[{...},...]}
//   {"action":"info"}（マスター→メンバー）→ {"ok":true,"server":{...}}
// ============================================================

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using HagLib.NET.Duplex;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Poly_Ling.Remote
{
    /// <summary>一覧の 1 行。サーバ 1 台分の情報。</summary>
    public sealed class RemoteServerInfo
    {
        public int    Pid;
        public int    Port;
        public string HostUserName = "";
        public string ProjectName  = "";
        public string StartedAt    = "";
        /// <summary>一覧取得時点でマスターを務めているか（list 応答でのみ意味を持つ）。</summary>
        public bool   IsMaster;

        /// <summary>選択肢の表示用文字列。</summary>
        public string Label
        {
            get
            {
                string project = string.IsNullOrEmpty(ProjectName) ? "(無題)" : ProjectName;
                string user    = string.IsNullOrEmpty(HostUserName) ? "" : $" / {HostUserName}";
                return $"{project}{user}  (pid {Pid}, port {Port})";
            }
        }

        public RemoteServerInfo Clone() => (RemoteServerInfo)MemberwiseClone();

        public JObject ToJson() => new JObject
        {
            ["pid"]          = Pid,
            ["port"]         = Port,
            ["hostUserName"] = HostUserName ?? "",
            ["projectName"]  = ProjectName  ?? "",
            ["startedAt"]    = StartedAt    ?? "",
            ["isMaster"]     = IsMaster,
        };

        public static RemoteServerInfo FromJson(JToken token)
        {
            if (!(token is JObject o)) return null;
            var info = new RemoteServerInfo
            {
                Pid          = (int?)o["pid"]  ?? 0,
                Port         = (int?)o["port"] ?? 0,
                HostUserName = (string)o["hostUserName"] ?? "",
                ProjectName  = (string)o["projectName"]  ?? "",
                StartedAt    = (string)o["startedAt"]    ?? "",
                IsMaster     = (bool?)o["isMaster"] ?? false,
            };
            return info.Port > 0 ? info : null;
        }
    }

    /// <summary>マスターへの問い合わせ（クライアント側）と共通定数。</summary>
    public static class RemoteDirectory
    {
        /// <summary>マスターの問い合わせ・登録用ポート。8765（旧パネル既定）・8770（Editor 操作）とは別。</summary>
        public const int    MasterPort = 8760;

        /// <summary>接続先ホスト。マスター・各サーバともループバックだけで待ち受ける。</summary>
        public const string Host = "127.0.0.1";

        /// <summary>1 要求あたりの応答待ち上限。</summary>
        public const int    RequestTimeoutMs = 2000;

        /// <summary>
        /// マスターからサーバ一覧を得る。マスターが見つからない・応答しない場合は null。
        /// </summary>
        public static async Task<List<RemoteServerInfo>> QueryAsync(int timeoutMs = RequestTimeoutMs)
        {
            var client = new TcpDuplexClient(Host, MasterPort);
            try
            {
                try { await client.ConnectAsync().ConfigureAwait(false); }
                catch { return null; }

                var reply = await RequestAsync(client, new JObject { ["action"] = "list" }, timeoutMs)
                                  .ConfigureAwait(false);
                if (reply == null || (bool?)reply["ok"] != true) return null;

                var list = new List<RemoteServerInfo>();
                if (reply["servers"] is JArray arr)
                {
                    foreach (var t in arr)
                    {
                        var info = RemoteServerInfo.FromJson(t);
                        if (info != null) list.Add(info);
                    }
                }
                return list;
            }
            finally
            {
                try { client.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// 自動で接続先を決められるなら返す。1 台だけならそれ、
        /// 前回の接続先（pid）が 1 台だけ含まれていればそれ。決められなければ null。
        /// </summary>
        public static RemoteServerInfo PickAuto(List<RemoteServerInfo> servers, int preferredPid)
        {
            if (servers == null || servers.Count == 0) return null;
            if (servers.Count == 1) return servers[0];
            if (preferredPid <= 0) return null;

            RemoteServerInfo found = null;
            foreach (var s in servers)
            {
                if (s.Pid != preferredPid) continue;
                if (found != null) return null;   // 同一プロセス内に複数ある場合は選ばせる
                found = s;
            }
            return found;
        }

        // ================================================================
        // 内部共通
        // ================================================================

        /// <summary>要求を送り応答 JSON を返す。失敗・時間切れは null。</summary>
        internal static async Task<JObject> RequestAsync(IDuplexChannel channel, JObject request, int timeoutMs)
        {
            using (var cts = new CancellationTokenSource(timeoutMs))
            {
                try
                {
                    var reply = await channel.SendAndReceiveAsync(request.ToString(Formatting.None), cts.Token)
                                             .ConfigureAwait(false);
                    string text = reply?.PayloadString;
                    if (string.IsNullOrEmpty(text)) return null;
                    return JObject.Parse(text);
                }
                catch
                {
                    return null;
                }
            }
        }

        internal static JObject TryParse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try { return JObject.Parse(text); }
            catch { return null; }
        }

        internal static Task Reply(IDuplexChannel channel, DuplexMessage request, JObject body)
        {
            try { return channel.ReplyAsync(request, body.ToString(Formatting.None)); }
            catch { return Task.CompletedTask; }
        }
    }

    /// <summary>
    /// サーバ 1 台ぶんの参加者。マスターを務めるか、マスターへ登録する。
    /// 1 インスタンスにつき Start / Stop は 1 回ずつ。
    /// </summary>
    public sealed class RemoteDirectoryNode : IDisposable
    {
        public enum NodeRole { None, Joining, Master, Member }

        private const int JoinAttempts      = 25;
        private const int JoinRetryDelayMs  = 200;
        private const int MemberInfoTimeoutMs = 1000;

        private readonly Func<Task<RemoteServerInfo>> _selfInfoAsync;
        private readonly object _lock = new object();

        private bool            _started;
        private bool            _stopped;
        private TcpDuplexServer _master;
        private TcpDuplexClient _member;
        private readonly Dictionary<IDuplexChannel, RemoteServerInfo> _members
            = new Dictionary<IDuplexChannel, RemoteServerInfo>();

        private volatile NodeRole _role = NodeRole.None;

        /// <summary>現在の役割。</summary>
        public NodeRole Role => _role;

        /// <summary>ログ出力（背景スレッドから呼ばれる）。</summary>
        public Action<string> OnLog;

        /// <summary>役割が変わったとき（背景スレッドから呼ばれる）。</summary>
        public Action OnRoleChanged;

        /// <param name="selfInfoAsync">自分の最新情報を返す。背景スレッドから呼ばれる。</param>
        public RemoteDirectoryNode(Func<Task<RemoteServerInfo>> selfInfoAsync)
        {
            _selfInfoAsync = selfInfoAsync ?? throw new ArgumentNullException(nameof(selfInfoAsync));
        }

        // ================================================================
        // 開始 / 停止
        // ================================================================

        public void Start()
        {
            lock (_lock)
            {
                if (_started) return;
                _started = true;
            }
            _ = Task.Run(JoinAsync);
        }

        public void Stop()
        {
            TcpDuplexServer master;
            TcpDuplexClient member;
            lock (_lock)
            {
                if (_stopped) return;
                _stopped = true;
                master  = _master;  _master = null;
                member  = _member;  _member = null;
                _members.Clear();
            }

            // マスターを閉じると、メンバー側では接続切断として検知され、移譲が始まる。
            if (master != null)
            {
                try { master.OnReceived           -= OnMasterReceived; }     catch { }
                try { master.OnClientDisconnected -= OnMemberDisconnected; } catch { }
                try { master.Dispose(); } catch { }
            }
            if (member != null)
            {
                try { member.OnDisconnected -= OnMasterLost; }     catch { }
                try { member.OnReceived     -= OnMemberReceived; } catch { }
                try { member.Dispose(); } catch { }
            }

            SetRole(NodeRole.None);
        }

        public void Dispose() => Stop();

        private bool IsStopped { get { lock (_lock) return _stopped; } }

        // ================================================================
        // 参加（マスターになる or 登録する）
        // ================================================================

        private async Task JoinAsync()
        {
            SetRole(NodeRole.Joining);

            for (int attempt = 0; attempt < JoinAttempts; attempt++)
            {
                if (IsStopped) return;

                if (TryBecomeMaster()) return;
                if (await TryRegisterAsync().ConfigureAwait(false)) return;

                await Task.Delay(JoinRetryDelayMs).ConfigureAwait(false);
            }

            if (IsStopped) return;
            Log($"サーバ一覧: マスター（port {RemoteDirectory.MasterPort}）にもなれず登録もできませんでした。" +
                "他のアプリがこのポートを使っていないか確認してください。");
            SetRole(NodeRole.None);
        }

        private bool TryBecomeMaster()
        {
            var server = new TcpDuplexServer();
            // 受付開始直後に届く要求を取りこぼさないよう、開始前に結線する。
            server.OnReceived           += OnMasterReceived;
            server.OnClientDisconnected += OnMemberDisconnected;

            try
            {
                server.StartAsync(IPAddress.Loopback, RemoteDirectory.MasterPort);
            }
            catch (SocketException)
            {
                // 既に誰かが Bind している（= マスターがいる）。
                DisposeServer(server);
                return false;
            }
            catch (Exception ex)
            {
                Log($"サーバ一覧: マスター用ポートの待ち受けに失敗: {ex.Message}");
                DisposeServer(server);
                return false;
            }

            lock (_lock)
            {
                if (_stopped)
                {
                    DisposeServer(server);
                    return true;
                }
                _master = server;
            }

            Log($"サーバ一覧: マスターになりました（port {RemoteDirectory.MasterPort}）");
            SetRole(NodeRole.Master);
            return true;
        }

        private void DisposeServer(TcpDuplexServer server)
        {
            try { server.OnReceived           -= OnMasterReceived; }     catch { }
            try { server.OnClientDisconnected -= OnMemberDisconnected; } catch { }
            try { server.Dispose(); } catch { }
        }

        private async Task<bool> TryRegisterAsync()
        {
            var client = new TcpDuplexClient(RemoteDirectory.Host, RemoteDirectory.MasterPort);
            client.OnDisconnected += OnMasterLost;
            client.OnReceived     += OnMemberReceived;

            try
            {
                await client.ConnectAsync().ConfigureAwait(false);
            }
            catch
            {
                DisposeClient(client);
                return false;
            }

            var self = await GetSelfAsync().ConfigureAwait(false);
            if (self == null)
            {
                DisposeClient(client);
                return false;
            }

            var reply = await RemoteDirectory.RequestAsync(
                client,
                new JObject { ["action"] = "register", ["server"] = self.ToJson() },
                RemoteDirectory.RequestTimeoutMs).ConfigureAwait(false);

            if (reply == null || (bool?)reply["ok"] != true)
            {
                DisposeClient(client);
                return false;
            }

            lock (_lock)
            {
                if (_stopped)
                {
                    DisposeClient(client);
                    return true;
                }
                _member = client;
            }

            // 応答を受けてから _member に入れるまでの間に切れていた場合、
            // OnMasterLost は _member と一致しないため無視されている。ここで拾い直す。
            if (!client.IsConnected)
            {
                lock (_lock) { if (ReferenceEquals(_member, client)) _member = null; }
                DisposeClient(client);
                return false;
            }

            Log($"サーバ一覧: マスターへ登録しました（自分の port {self.Port}）");
            SetRole(NodeRole.Member);
            return true;
        }

        private void DisposeClient(TcpDuplexClient client)
        {
            try { client.OnDisconnected -= OnMasterLost; }     catch { }
            try { client.OnReceived     -= OnMemberReceived; } catch { }
            try { client.Dispose(); } catch { }
        }

        // ================================================================
        // メンバー側：マスターの喪失と info 要求
        // ================================================================

        private void OnMasterLost(IDuplexChannel channel)
        {
            TcpDuplexClient lost;
            lock (_lock)
            {
                if (_stopped || !ReferenceEquals(channel, _member)) return;
                lost    = _member;
                _member = null;
            }

            DisposeClient(lost);
            Log("サーバ一覧: マスターとの接続が切れました。引き継ぎます。");
            _ = Task.Run(JoinAsync);
        }

        private void OnMemberReceived(IDuplexChannel channel, DuplexMessage message)
        {
            if (message == null || message.Type != MessageType.Request) return;
            _ = HandleMemberRequestAsync(channel, message);
        }

        private async Task HandleMemberRequestAsync(IDuplexChannel channel, DuplexMessage message)
        {
            var req    = RemoteDirectory.TryParse(message.PayloadString);
            string act = (string)req?["action"];

            if (act == "info")
            {
                var self = await GetSelfAsync().ConfigureAwait(false);
                var body = self != null
                    ? new JObject { ["ok"] = true,  ["server"] = self.ToJson() }
                    : new JObject { ["ok"] = false, ["error"]  = "self info unavailable" };
                await RemoteDirectory.Reply(channel, message, body).ConfigureAwait(false);
                return;
            }

            await RemoteDirectory.Reply(channel, message,
                new JObject { ["ok"] = false, ["error"] = $"unknown action: {act}" }).ConfigureAwait(false);
        }

        // ================================================================
        // マスター側：register / list
        // ================================================================

        private void OnMemberDisconnected(IDuplexChannel channel)
        {
            RemoteServerInfo removed = null;
            lock (_lock)
            {
                if (_members.TryGetValue(channel, out removed))
                    _members.Remove(channel);
            }
            if (removed != null)
                Log($"サーバ一覧: 登録抹消 pid={removed.Pid} port={removed.Port}");
        }

        private void OnMasterReceived(IDuplexChannel channel, DuplexMessage message)
        {
            if (message == null || message.Type != MessageType.Request) return;
            _ = HandleMasterRequestAsync(channel, message);
        }

        private async Task HandleMasterRequestAsync(IDuplexChannel channel, DuplexMessage message)
        {
            var req    = RemoteDirectory.TryParse(message.PayloadString);
            string act = (string)req?["action"];

            switch (act)
            {
                case "register":
                {
                    var info = RemoteServerInfo.FromJson(req["server"]);
                    if (info == null)
                    {
                        await RemoteDirectory.Reply(channel, message,
                            new JObject { ["ok"] = false, ["error"] = "invalid server info" }).ConfigureAwait(false);
                        return;
                    }
                    lock (_lock) { _members[channel] = info; }
                    Log($"サーバ一覧: 登録 pid={info.Pid} port={info.Port}");
                    await RemoteDirectory.Reply(channel, message, new JObject { ["ok"] = true }).ConfigureAwait(false);
                    return;
                }

                case "list":
                {
                    var arr = new JArray();

                    var self = await GetSelfAsync().ConfigureAwait(false);
                    if (self != null)
                    {
                        self.IsMaster = true;
                        arr.Add(self.ToJson());
                    }

                    List<KeyValuePair<IDuplexChannel, RemoteServerInfo>> snapshot;
                    lock (_lock) { snapshot = new List<KeyValuePair<IDuplexChannel, RemoteServerInfo>>(_members); }

                    // 各メンバーへ並行に問い合わせ、応答が無ければ登録時の情報を使う。
                    var tasks = new List<Task<RemoteServerInfo>>();
                    foreach (var kv in snapshot)
                        tasks.Add(FetchMemberInfoAsync(kv.Key, kv.Value));
                    var infos = await Task.WhenAll(tasks).ConfigureAwait(false);

                    foreach (var info in infos)
                    {
                        if (info == null) continue;
                        info.IsMaster = false;
                        arr.Add(info.ToJson());
                    }

                    await RemoteDirectory.Reply(channel, message,
                        new JObject { ["ok"] = true, ["servers"] = arr }).ConfigureAwait(false);
                    return;
                }

                default:
                    await RemoteDirectory.Reply(channel, message,
                        new JObject { ["ok"] = false, ["error"] = $"unknown action: {act}" }).ConfigureAwait(false);
                    return;
            }
        }

        private static async Task<RemoteServerInfo> FetchMemberInfoAsync(IDuplexChannel channel, RemoteServerInfo registered)
        {
            var reply = await RemoteDirectory.RequestAsync(
                channel, new JObject { ["action"] = "info" }, MemberInfoTimeoutMs).ConfigureAwait(false);

            var fresh = reply != null && (bool?)reply["ok"] == true
                ? RemoteServerInfo.FromJson(reply["server"])
                : null;

            return fresh ?? registered.Clone();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private async Task<RemoteServerInfo> GetSelfAsync()
        {
            try
            {
                var task = _selfInfoAsync();
                var done = await Task.WhenAny(task, Task.Delay(RemoteDirectory.RequestTimeoutMs)).ConfigureAwait(false);
                if (done != task) return null;
                var info = await task.ConfigureAwait(false);
                return info?.Clone();
            }
            catch (Exception ex)
            {
                Log($"サーバ一覧: 自分の情報を取得できません: {ex.Message}");
                return null;
            }
        }

        private void SetRole(NodeRole role)
        {
            if (_role == role) return;
            _role = role;
            try { OnRoleChanged?.Invoke(); } catch { }
        }

        private void Log(string message)
        {
            try { OnLog?.Invoke(message); } catch { }
        }
    }
}
