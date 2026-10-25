// PyAddonHost.cs
// Python アドオンの起動・停止・呼び出しの係。PolyLingPlayerViewerCore が 1 つ持つ。
// Runtime/Poly_Ling_Player/PyAddon/ に配置
//
// ■ 流れ（Invoke）
//   1. 起動中か … リモートの登録簿に "<id>.run" を capabilities に申告した接続があるか（PolyLingPlayerServer.HasClientAction）。
//   2. 起動していなければ、venv の python でスクリプトを "--serve --port <リモートサーバのポート>" 付きで子プロセスとして起動する。
//      呼び出しは待ち行列に積む。
//   3. 登録簿が変わった通知（ClientsChanged。register・切断で発火）で起動を知り、積んでいた呼び出しを送る。
//      毎フレームの見回りはしない。上限時間（StartTimeoutMs）は Task.Delay で 1 回だけ確かめる。
//   4. 呼び出しは RequestClient（"<id>.run" / "<id>.describe"）。返事は onFinished でメインスレッドに届く。
//   呼び出しの状態は番号（CallState.No）で引ける（コマンド queryPyAddonCall）。
//
// ■ 子プロセス
//   標準出力・標準エラーは統合ログ（カテゴリ PyAddon）へ流し、アドオンごとに最後の数十行を持つ。
//   PolyLing の終了時（Dispose）には、起動した子プロセスを終わらせる。アドオンは接続が切れても自分で終わる。
//   外で起動した常駐（端末から --serve）も、登録簿にあれば起動中として扱う（停止は quit を送るだけ）。
//
// ■ スレッド
//   公開メソッドはメインスレッドから呼ぶ。子プロセスの出力・終了は別スレッドで届くので、
//   コンストラクタで捕まえた SynchronizationContext でメインスレッドへ戻す。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Poly_Ling.Data;
using Poly_Ling.Remote;

namespace Poly_Ling.Player
{
    public sealed class PyAddonHost : IDisposable
    {
        public const string StateStopped  = "stopped";
        public const string StateStarting = "starting";
        public const string StateRunning  = "running";
        public const string StateFailed   = "failed";

        public const string ActionRun      = "run";
        public const string ActionDescribe = "describe";
        public const string ActionQuit     = "quit";

        /// <summary>起動してから register が届くまでの上限。</summary>
        public int StartTimeoutMs = 60000;
        /// <summary>quit を送ってから子プロセスが終わらなければ強制終了するまでの時間。</summary>
        public int QuitGraceMs = 5000;

        private const int KeepLines = 60;
        private const int KeepCalls = 64;

        /// <summary>呼び出し 1 件。</summary>
        public sealed class CallState
        {
            public const string Starting = "starting";   // 起動待ち
            public const string Pending  = "pending";    // 返事待ち
            public const string Done     = "done";
            public const string Failed   = "failed";

            public int    No;
            public string AddonId;
            public string Action;
            public string Status = Starting;
            public string ParamsJson;
            public int    TimeoutMs;
            /// <summary>done のときアドオンが返した data（JSON オブジェクトの文字列）。</summary>
            public string ResultJson;
            public string Error;
            public string Code;
        }

        /// <summary>アドオン 1 つの実行時の様子。</summary>
        private sealed class Runtime
        {
            public Process       Process;
            public bool          Starting;
            /// <summary>Stop で止めた（終了コードを失敗として扱わない）。</summary>
            public bool          Stopping;
            public string        LastError;
            public int           StartSerial;
            public readonly List<CallState> Queue = new List<CallState>();
            public readonly LinkedList<string> Lines = new LinkedList<string>();
        }

        private readonly Func<PolyLingPlayerServer> _getServer;
        private readonly SynchronizationContext _ctx;
        private readonly Dictionary<string, Runtime> _rt = new Dictionary<string, Runtime>();
        private readonly Dictionary<int, CallState> _calls = new Dictionary<int, CallState>();
        private PolyLingPlayerServer _hooked;
        private int _callNo;
        private bool _disposed;

        /// <summary>登録簿と Python 環境の設定。</summary>
        public PyAddonSettings Settings { get; }

        /// <summary>状態・呼び出しの結果・出力が変わった（メインスレッド）。画面の更新に使う。</summary>
        public event Action Changed;

        /// <summary>呼び出し 1 件の結果が決まった（done / failed。メインスレッド）。シナリオの待ちを解くのに使う。</summary>
        public event Action<CallState> CallFinished;

        public PyAddonHost(Func<PolyLingPlayerServer> getServer)
        {
            _getServer = getServer;
            _ctx = SynchronizationContext.Current;
            Settings = PyAddonSettings.Load();
        }

        // ================================================================
        // 状態
        // ================================================================

        private PolyLingPlayerServer Server
        {
            get
            {
                var s = _getServer?.Invoke();
                if (s != _hooked)
                {
                    if (_hooked != null) _hooked.ClientsChanged -= OnClientsChanged;
                    _hooked = s;
                    if (_hooked != null) _hooked.ClientsChanged += OnClientsChanged;
                }
                return s;
            }
        }

        /// <summary>サーバの購読を始める（ビューアの初期化の最後に 1 回呼ぶ）。</summary>
        public void Attach() => _ = Server;

        private Runtime Rt(string id)
        {
            if (!_rt.TryGetValue(id, out var r)) { r = new Runtime(); _rt[id] = r; }
            return r;
        }

        private static string Cap(string id, string action) => id + "." + action;

        public bool IsRunning(string id) => Server?.HasClientAction(Cap(id, ActionRun)) ?? false;

        /// <summary>stopped / starting / running / failed。</summary>
        public string StateOf(string id)
        {
            if (IsRunning(id)) return StateRunning;
            if (_rt.TryGetValue(id, out var r))
            {
                if (r.Starting) return StateStarting;
                if (!string.IsNullOrEmpty(r.LastError)) return StateFailed;
            }
            return StateStopped;
        }

        public string LastErrorOf(string id) => _rt.TryGetValue(id, out var r) ? r.LastError ?? "" : "";

        /// <summary>子プロセスの最後の出力（新しいものが後）。</summary>
        public IReadOnlyCollection<string> OutputOf(string id)
            => _rt.TryGetValue(id, out var r) ? (IReadOnlyCollection<string>)r.Lines : Array.Empty<string>();

        /// <summary>このホストが起動した子プロセスの PID（無ければ 0）。</summary>
        public int ProcessIdOf(string id)
        {
            if (!_rt.TryGetValue(id, out var r) || r.Process == null) return 0;
            try { return r.Process.HasExited ? 0 : r.Process.Id; } catch { return 0; }
        }

        public CallState GetCall(int no) => _calls.TryGetValue(no, out var c) ? c : null;

        /// <summary>
        /// 「呼べるもの」の一覧（tools_search / tools_describe）に載せる形（PanelCommandAddonCatalog.cs）。
        /// 宣言は describe で受けて保存したもの。まだ受けていなければ args / outputs は空で hasSpec=false。
        /// </summary>
        public List<AddonCatalogEntry> Catalog()
        {
            var list = new List<AddonCatalogEntry>();
            foreach (var d in Settings.All())
            {
                Settings.Specs.TryGetValue(d.Id, out var spec);
                var args    = spec?["args"]    as JArray ?? new JArray();
                var outputs = spec?["outputs"] as JArray ?? new JArray();
                string desc = (string)spec?["description"] ?? "";

                var hay = new StringBuilder();
                hay.Append(d.Id).Append('\n').Append(d.Title ?? "").Append('\n').Append(desc).Append('\n');
                foreach (var a in args)
                    hay.Append((string)a["name"]).Append('\n').Append((string)a["label"]).Append('\n').Append((string)a["help"]).Append('\n');
                foreach (var o in outputs)
                    hay.Append((string)o["name"]).Append('\n').Append((string)o["description"]).Append('\n');

                list.Add(new AddonCatalogEntry
                {
                    Id          = d.Id,
                    Title       = d.Title ?? d.Id,
                    Description = desc,
                    Official    = d.Official,
                    State       = StateOf(d.Id),
                    HasSpec     = spec != null,
                    ArgsJson    = args.ToString(Newtonsoft.Json.Formatting.None),
                    OutputsJson = outputs.ToString(Newtonsoft.Json.Formatting.None),
                    Haystack    = hay.ToString().ToLowerInvariant(),
                });
            }
            return list;
        }

        // ================================================================
        // 起動・停止
        // ================================================================

        /// <summary>起動していなければ起動する。起動中・起動待ちなら何もしない。始められなければ理由を返す。</summary>
        public string Start(string id)
        {
            var def = Settings.Find(id);
            if (def == null) return $"アドオン \"{id}\" は登録されていません";
            if (IsRunning(id)) return null;
            var r = Rt(id);
            if (r.Starting) return null;

            var server = Server;
            if (server == null || !server.IsRunning) return "リモートサーバが起動していません（アドオンはリモートサーバにつなぎます）";

            string python = Settings.ResolvePython();
            if (string.IsNullOrEmpty(python) || !File.Exists(python))
                return $"Python が見つかりません: \"{python}\"（Python アドオンのパネルで poly_ling_py のフォルダか python.exe を選んでください）";
            string script = Settings.ResolveScript(def);
            if (!File.Exists(script)) return $"スクリプトがありません: {script}";
            string cwd = Settings.ResolveWorkingDirectory(def);

            var psi = new ProcessStartInfo(python)
            {
                Arguments              = $"{Quote(script)} --serve --port {server.Port}",
                WorkingDirectory       = cwd,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8,
            };
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
            // venv の python.exe を直に起動すれば venv の site-packages を使う。DLL を探す PATH も venv を先にする。
            string bin = Path.GetDirectoryName(python);
            string venv = bin != null ? Path.GetDirectoryName(bin) : null;
            if (venv != null && File.Exists(Path.Combine(venv, "pyvenv.cfg")))
            {
                psi.EnvironmentVariables["VIRTUAL_ENV"] = venv;
                string path = psi.EnvironmentVariables["PATH"] ?? "";
                psi.EnvironmentVariables["PATH"] = bin + Path.PathSeparator + path;
            }

            Process p;
            try
            {
                p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (_, e) => { if (e.Data != null) Post(() => AddLine(id, e.Data, false)); };
                p.ErrorDataReceived  += (_, e) => { if (e.Data != null) Post(() => AddLine(id, e.Data, true)); };
                p.Exited += (_, __) => Post(() => OnExited(id, p));
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                r.LastError = "起動できません: " + e.Message;
                Changed?.Invoke();
                return r.LastError;
            }

            r.Process   = p;
            r.Starting  = true;
            r.Stopping  = false;
            r.LastError = null;
            int serial = ++r.StartSerial;
            PlayerLog.Add("PyAddon", $"[{id}] 起動: {python} {psi.Arguments}（作業フォルダ {cwd}）");

            // 上限時間の確かめは 1 回だけ（見回りはしない）。
            int timeout = StartTimeoutMs;
            Task.Delay(timeout).ContinueWith(_ => Post(() =>
            {
                if (_disposed || !r.Starting || r.StartSerial != serial) return;
                FailStart(id, $"{timeout / 1000} 秒待っても PolyLing につながりませんでした（出力はログのカテゴリ PyAddon）");
                TryKill(r.Process);
            }));

            Changed?.Invoke();
            return null;
        }

        /// <summary>止める。起動中なら quit を送り、このホストが起動した子プロセスなら時間を置いて終わらせる。</summary>
        public string Stop(string id)
        {
            if (Settings.Find(id) == null) return $"アドオン \"{id}\" は登録されていません";
            var r = Rt(id);
            var p = r.Process;
            r.Stopping = true;
            if (r.Starting) FailStart(id, "起動を取りやめました");
            r.LastError = null;

            var server = Server;
            if (server != null && server.HasClientAction(Cap(id, ActionQuit)))
                server.RequestClient("", Cap(id, ActionQuit), "{}", QuitGraceMs, out _, out _, out _);

            if (p != null && !SafeHasExited(p))
            {
                Task.Delay(QuitGraceMs).ContinueWith(_ => Post(() => { if (!SafeHasExited(p)) TryKill(p); }));
            }
            Changed?.Invoke();
            return null;
        }

        // ================================================================
        // 呼び出し
        // ================================================================

        /// <summary>
        /// アドオンの action（run / describe）を呼ぶ。起動していなければ起動してから送る。
        /// 受け付けたら呼び出しの番号、受け付けられなければ 0 と理由。
        /// run の paramsJson は {"args":{…},"modelIndex":n}。
        /// </summary>
        public int Invoke(string id, string action, string paramsJson, int timeoutMs, out string reason, out string code)
        {
            reason = null; code = null;
            if (Settings.Find(id) == null)
            {
                reason = $"アドオン \"{id}\" は登録されていません"; code = CommandErrorCodes.InvalidArgs; return 0;
            }
            if (action != ActionRun && action != ActionDescribe)
            {
                reason = $"action は {ActionRun} か {ActionDescribe} です（{action}）"; code = CommandErrorCodes.InvalidArgs; return 0;
            }
            if (!string.IsNullOrWhiteSpace(paramsJson))
            {
                try { JObject.Parse(paramsJson); }
                catch (Exception e) { reason = "引数が JSON オブジェクトではありません: " + e.Message; code = CommandErrorCodes.InvalidArgs; return 0; }
            }

            var call = new CallState
            {
                No = ++_callNo, AddonId = id, Action = action,
                ParamsJson = string.IsNullOrWhiteSpace(paramsJson) ? "{}" : paramsJson,
                TimeoutMs = timeoutMs > 0 ? timeoutMs : 300000,
            };
            _calls[call.No] = call;
            if (_calls.Count > KeepCalls) _calls.Remove(call.No - KeepCalls);

            if (IsRunning(id))
            {
                Send(call);
            }
            else
            {
                string err = Start(id);
                if (err != null)
                {
                    Finish(call, null, err, CommandErrorCodes.NotReady);
                }
                else if (IsRunning(id))
                {
                    Send(call);
                }
                else
                {
                    Rt(id).Queue.Add(call);
                }
            }
            Changed?.Invoke();
            return call.No;
        }

        private void Send(CallState call)
        {
            var server = Server;
            if (server == null)
            {
                Finish(call, null, "リモートサーバがありません", CommandErrorCodes.NotReady);
                return;
            }
            call.Status = CallState.Pending;
            if (!server.RequestClient("", Cap(call.AddonId, call.Action), call.ParamsJson, call.TimeoutMs,
                                      out _, out string reason, out string code,
                                      st => OnReply(call, st)))
            {
                Finish(call, null, reason, code);
            }
        }

        private void OnReply(CallState call, RemoteServerCore.ClientRequestState st)
        {
            if (st.Status == RemoteServerCore.ClientRequestState.Done)
            {
                if (call.Action == ActionDescribe) StoreSpec(call.AddonId, st.ResultJson);
                Finish(call, st.ResultJson, null, null);
            }
            else
            {
                Finish(call, null, st.Error, st.Code);
            }
        }

        private void StoreSpec(string id, string json)
        {
            try
            {
                var o = JObject.Parse(string.IsNullOrEmpty(json) ? "{}" : json);
                if (o["args"] is JArray)
                {
                    Settings.Specs[id] = o;
                    Settings.Save();
                }
            }
            catch (Exception e)
            {
                PlayerLog.Add("PyAddon", $"[{id}] 引数の宣言を読めません: {e.Message}", PlayerLogLevel.Warning);
            }
        }

        private void Finish(CallState call, string resultJson, string error, string code)
        {
            if (error == null)
            {
                call.Status = CallState.Done;
                call.ResultJson = resultJson ?? "{}";
            }
            else
            {
                call.Status = CallState.Failed;
                call.Error = error;
                call.Code = code ?? CommandErrorCodes.Failed;
                PlayerLog.Add("PyAddon", $"[{call.AddonId}] {call.Action} #{call.No} 失敗: {error}", PlayerLogLevel.Warning);
            }
            try { CallFinished?.Invoke(call); }
            catch (Exception e) { PlayerLog.Add("PyAddon", $"CallFinished の受け手で例外: {e.Message}", PlayerLogLevel.Warning); }
            Changed?.Invoke();
        }

        // ================================================================
        // 事象
        // ================================================================

        private void OnClientsChanged()
        {
            if (_disposed) return;
            foreach (var kv in _rt)
            {
                var r = kv.Value;
                if (!IsRunning(kv.Key)) continue;
                if (r.Starting)
                {
                    r.Starting = false;
                    PlayerLog.Add("PyAddon", $"[{kv.Key}] 起動しました");
                }
                if (r.Queue.Count > 0)
                {
                    var q = new List<CallState>(r.Queue);
                    r.Queue.Clear();
                    foreach (var c in q) Send(c);
                }
            }
            Changed?.Invoke();
        }

        private void OnExited(string id, Process p)
        {
            var r = Rt(id);
            if (r.Process != p) return;
            int exit = -1;
            try { exit = p.ExitCode; } catch { }
            PlayerLog.Add("PyAddon", $"[{id}] 終了しました（終了コード {exit}）",
                          exit == 0 ? PlayerLogLevel.Info : PlayerLogLevel.Warning);
            if (r.Starting)
            {
                string tail = "";
                foreach (var l in r.Lines) tail = l;     // 最後の 1 行
                FailStart(id, $"PolyLing につながる前に終了しました（終了コード {exit}）" + (tail != "" ? ": " + tail : ""));
            }
            else if (exit != 0 && !r.Stopping)
            {
                r.LastError = $"終了しました（終了コード {exit}）";
            }
            r.Process = null;
            try { p.Dispose(); } catch { }
            Changed?.Invoke();
        }

        private void FailStart(string id, string reason)
        {
            var r = Rt(id);
            r.Starting  = false;
            r.LastError = reason;
            PlayerLog.Add("PyAddon", $"[{id}] {reason}", PlayerLogLevel.Warning);
            var q = new List<CallState>(r.Queue);
            r.Queue.Clear();
            foreach (var c in q) Finish(c, null, reason, CommandErrorCodes.NotReady);
            Changed?.Invoke();
        }

        private void AddLine(string id, string line, bool isError)
        {
            var r = Rt(id);
            r.Lines.AddLast(line);
            while (r.Lines.Count > KeepLines) r.Lines.RemoveFirst();
            PlayerLog.Add("PyAddon", $"[{id}] {line}", isError ? PlayerLogLevel.Warning : PlayerLogLevel.Info);
            Changed?.Invoke();
        }

        // ================================================================
        // 片付け
        // ================================================================

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_hooked != null) _hooked.ClientsChanged -= OnClientsChanged;
            foreach (var r in _rt.Values) TryKill(r.Process);
            _rt.Clear();
        }

        // ================================================================
        // 小道具
        // ================================================================

        private void Post(Action a)
        {
            if (_ctx != null) _ctx.Post(_ => { if (!_disposed) a(); }, null);
            else a();
        }

        private static bool SafeHasExited(Process p)
        {
            try { return p.HasExited; } catch { return true; }
        }

        private static void TryKill(Process p)
        {
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
        }

        private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
    }
}
