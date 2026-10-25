// PlayerCommandDispatcher.PyAddon.cs
// Python アドオンの受け口（queryPyAddons / startPyAddon / stopPyAddon / invokePyAddon / queryPyAddonCall）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【モデルを見ない】
//   子プロセスと通信だけを扱うので、プロジェクト・モデルの null 門より前で捌く（DispatchRemoteClient と同じ扱い）。
//   アドオンがモデルを変えるのは、アドオン自身がリモートで送るコマンド（それぞれ Undo に残る）。
// 【実体はビューア側】
//   PyAddonHost はビューア（PolyLingPlayerViewerCore）が持つので、GetPyAddonHost で受け取る。

using Newtonsoft.Json.Linq;
using Poly_Ling.Data;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>Python アドオンの係（ビューアが配線する。無ければ null を返す）。</summary>
        public System.Func<PyAddonHost> GetPyAddonHost;

        private bool DispatchPyAddon(PanelCommand cmd)
        {
            switch (cmd)
            {
                case QueryPyAddonsCommand _:      RunQueryPyAddons();      return true;
                case StartPyAddonCommand c:       RunStartPyAddon(c);      return true;
                case StopPyAddonCommand c:        RunStopPyAddon(c);       return true;
                case InvokePyAddonCommand c:      RunInvokePyAddon(c);     return true;
                case QueryPyAddonCallCommand c:   RunQueryPyAddonCall(c);  return true;
                default: return false;
            }
        }

        private PyAddonHost PyAddonHostOrFail()
        {
            var h = GetPyAddonHost?.Invoke();
            if (h == null)
                _pendingResult = CommandResult.Fail("Python アドオンの係がありません（リモートサーバのある画面でだけ使えます）", CommandErrorCodes.NotReady);
            return h;
        }

        private void RunQueryPyAddons()
        {
            var h = PyAddonHostOrFail();
            if (h == null) return;
            var arr = new JArray();
            foreach (var d in h.Settings.All())
            {
                h.Settings.Specs.TryGetValue(d.Id, out var spec);
                arr.Add(new JObject
                {
                    ["args"]      = spec?["args"]    as JArray ?? new JArray(),
                    ["outputs"]   = spec?["outputs"] as JArray ?? new JArray(),
                    ["id"]        = d.Id,
                    ["title"]     = d.Title ?? d.Id,
                    ["official"]  = d.Official,
                    ["script"]    = h.Settings.ResolveScript(d),
                    ["state"]     = h.StateOf(d.Id),
                    ["lastError"] = h.LastErrorOf(d.Id),
                    ["pid"]       = h.ProcessIdOf(d.Id),
                    ["hasSpec"]   = h.Settings.Specs.ContainsKey(d.Id),
                });
            }
            ReportData(CommandDataJson.New()
                .Text("python", h.Settings.ResolvePython())
                .Text("root",   h.Settings.Root ?? "")
                .Int("count",   arr.Count)
                .Text("addons", arr.ToString(Newtonsoft.Json.Formatting.None))
                .Build());
        }

        private void RunStartPyAddon(StartPyAddonCommand cmd)
        {
            var h = PyAddonHostOrFail();
            if (h == null) return;
            string err = h.Start(cmd.AddonId);
            if (err != null) { _pendingResult = CommandResult.Fail(err, CommandErrorCodes.NotReady); return; }
            ReportData(CommandDataJson.New().Text("state", h.StateOf(cmd.AddonId)).Build());
        }

        private void RunStopPyAddon(StopPyAddonCommand cmd)
        {
            var h = PyAddonHostOrFail();
            if (h == null) return;
            string err = h.Stop(cmd.AddonId);
            if (err != null) { _pendingResult = CommandResult.Fail(err, CommandErrorCodes.InvalidArgs); return; }
            ReportData(CommandDataJson.New().Text("state", h.StateOf(cmd.AddonId)).Build());
        }

        private void RunInvokePyAddon(InvokePyAddonCommand cmd)
        {
            var h = PyAddonHostOrFail();
            if (h == null) return;

            string paramsJson = "{}";
            if (cmd.Action == PyAddonHost.ActionRun)
            {
                JObject args;
                try { args = string.IsNullOrWhiteSpace(cmd.ArgsJson) ? new JObject() : JObject.Parse(cmd.ArgsJson); }
                catch (System.Exception e)
                {
                    _pendingResult = CommandResult.Fail("argsJson が JSON オブジェクトではありません: " + e.Message, CommandErrorCodes.InvalidArgs);
                    return;
                }
                var keys   = cmd.ArgKeys   ?? new string[0];
                var values = cmd.ArgValues ?? new string[0];
                if (keys.Length != values.Length)
                {
                    _pendingResult = CommandResult.Fail($"argKeys が {keys.Length} 件、argValues が {values.Length} 件で長さが合いません", CommandErrorCodes.InvalidArgs);
                    return;
                }
                for (int i = 0; i < keys.Length; i++)
                    if (!string.IsNullOrEmpty(keys[i])) args[keys[i]] = values[i] ?? "";
                paramsJson = new JObject { ["args"] = args, ["modelIndex"] = cmd.ModelIndex }
                    .ToString(Newtonsoft.Json.Formatting.None);
            }

            int no = h.Invoke(cmd.AddonId, cmd.Action, paramsJson, cmd.TimeoutMs, out string reason, out string code);
            if (no == 0) { _pendingResult = CommandResult.Fail(reason, code); return; }
            var st = h.GetCall(no);
            ReportData(CommandDataJson.New()
                .Int("callNo", no)
                .Text("status", st?.Status ?? PyAddonHost.CallState.Failed)
                .Build());
        }

        private void RunQueryPyAddonCall(QueryPyAddonCallCommand cmd)
        {
            var h = PyAddonHostOrFail();
            if (h == null) return;
            var st = h.GetCall(cmd.CallNo);
            if (st == null)
            {
                _pendingResult = CommandResult.Fail($"呼び出しの番号 {cmd.CallNo} はありません（古い呼び出しは捨てています）", CommandErrorCodes.InvalidArgs);
                return;
            }
            var b = CommandDataJson.New()
                .Text("status",  st.Status)
                .Text("addonId", st.AddonId)
                .Text("action",  st.Action);
            if (st.ResultJson != null) b.Text("data",  st.ResultJson);
            if (st.Error != null)      b.Text("error", st.Error);
            if (st.Code != null)       b.Text("code",  st.Code);
            ReportData(b.Build());
        }
    }
}
