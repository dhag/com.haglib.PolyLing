// PlayerCommandDispatcher.RemoteClient.cs
// リモートのクライアントへの要求（requestClient / queryClientRequest / queryRemoteClients / sendModelToClient）と、
// 外のプログラム向けの照会（queryWorkFolder）の受け口。
// sendModelToClient はモデルを読むが、プロジェクトが無いときは自分で失敗を返すので、null 門より前で捌いてよい。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【モデルを見ない】
//   通信だけを扱うので、プロジェクト・モデルの null 門より前で捌く（DispatchMotionFile と同じ扱い）。
// 【実体はビューア側】
//   リモートサーバ（PolyLingPlayerServer）はビューア（PolyLingPlayerViewerCore）が持つので、GetPlayerServer で受け取る。

using Poly_Ling.Data;
using Poly_Ling.Remote;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>リモートサーバ（ビューアが配線する。サーバを立てていなければ null を返す）。</summary>
        public System.Func<PolyLingPlayerServer> GetPlayerServer;

        /// <summary>リモートのクライアントへの要求のコマンドなら処理して true を返す。</summary>
        private bool DispatchRemoteClient(PanelCommand cmd)
        {
            switch (cmd)
            {
                case RequestClientCommand c:      RunRequestClient(c);      return true;
                case QueryClientRequestCommand c: RunQueryClientRequest(c); return true;
                case QueryRemoteClientsCommand _: RunQueryRemoteClients();  return true;
                case SendModelToClientCommand c:  RunSendModelToClient(c);  return true;
                case QueryWorkFolderCommand _:    RunQueryWorkFolder();     return true;
                default: return false;
            }
        }

        private PolyLingPlayerServer RemoteServerOrFail()
        {
            var s = GetPlayerServer?.Invoke();
            if (s == null || !s.IsRunning)
            {
                _pendingResult = CommandResult.Fail("リモートサーバが起動していません", CommandErrorCodes.NotReady);
                return null;
            }
            return s;
        }

        private void RunRequestClient(RequestClientCommand cmd)
        {
            var s = RemoteServerOrFail();
            if (s == null) return;
            if (!s.RequestClient(cmd.UserName, cmd.Action, cmd.ParamsJson, cmd.TimeoutMs,
                                 out int no, out string reason, out string code))
            {
                _pendingResult = CommandResult.Fail(reason, code);
                return;
            }
            ReportData(CommandDataJson.New().Int("requestNo", no).Build());
        }

        private void RunQueryClientRequest(QueryClientRequestCommand cmd)
        {
            var s = RemoteServerOrFail();
            if (s == null) return;
            var st = s.GetClientRequest(cmd.RequestNo);
            if (st == null)
            {
                _pendingResult = CommandResult.Fail($"要求番号 {cmd.RequestNo} はありません（古い要求は捨てています）", CommandErrorCodes.InvalidArgs);
                return;
            }
            var b = CommandDataJson.New()
                .Text("status", st.Status)
                .Text("action", st.Action ?? "");
            if (st.ResultJson != null) b.Text("data",  st.ResultJson);
            if (st.Error != null)      b.Text("error", st.Error);
            if (st.Code != null)       b.Text("code",  st.Code);
            ReportData(b.Build());
        }

        /// <summary>
        /// モデル 1 つを丸ごと putModel としてクライアントへ押し出す。
        /// params は {modelIndex, modelName, byteCount}、本体はモデル 1 つ分の JSON（ModelBundle）を Binary の項目で添える。
        /// </summary>
        private void RunSendModelToClient(SendModelToClientCommand cmd)
        {
            var s = RemoteServerOrFail();
            if (s == null) return;

            var project = _getProject();
            var model = (project != null && cmd.ModelIndex >= 0 && cmd.ModelIndex < project.ModelCount)
                ? project.GetModel(cmd.ModelIndex) : null;
            if (model == null)
            {
                _pendingResult = CommandResult.Fail($"モデル {cmd.ModelIndex} がありません", CommandErrorCodes.InvalidArgs);
                return;
            }

            byte[] bundle;
            try { bundle = Poly_Ling.Serialization.ModelBundle.ToBytes(model); }
            catch (System.Exception e)
            {
                _pendingResult = CommandResult.Fail("モデルを組めません: " + e.Message, CommandErrorCodes.Exception);
                return;
            }
            if (bundle == null)
            {
                _pendingResult = CommandResult.Fail($"モデル {cmd.ModelIndex} を組めません", CommandErrorCodes.Failed);
                return;
            }

            var p0 = new Newtonsoft.Json.Linq.JObject
            {
                ["modelIndex"] = cmd.ModelIndex,
                ["modelName"]  = model.Name ?? "",
                ["byteCount"]  = bundle.Length,
            };

            if (!s.RequestClient(cmd.UserName, SendModelToClientCommand.ClientAction,
                                 p0.ToString(Newtonsoft.Json.Formatting.None), cmd.TimeoutMs,
                                 out int no, out string reason, out string code,
                                 null, bundle))
            {
                _pendingResult = CommandResult.Fail(reason, code);
                return;
            }
            ReportData(CommandDataJson.New().Int("requestNo", no).Int("byteCount", bundle.Length).Build());
        }

        /// <summary>作業フォルダの絶対パスと直下の既存フォルダ（サーバを要らない。モデルも見ない）。</summary>
        private void RunQueryWorkFolder()
        {
            string root = Poly_Ling.Core.PLSandbox.WorkFolder;
            bool isSet = !string.IsNullOrEmpty(root) && System.IO.Directory.Exists(root);
            var tops = new System.Collections.Generic.List<string>();
            if (isSet)
            {
                try
                {
                    foreach (var d in System.IO.Directory.GetDirectories(root))
                        tops.Add(System.IO.Path.GetFileName(d));
                    tops.Sort(System.StringComparer.OrdinalIgnoreCase);
                }
                catch (System.Exception e)
                {
                    _pendingResult = CommandResult.Fail("作業フォルダを読めません: " + e.Message, CommandErrorCodes.Failed);
                    return;
                }
            }
            ReportData(CommandDataJson.New()
                .Flag("isSet", isSet)
                .Text("path", isSet ? root : "")
                .Texts("topFolders", tops)
                .Build());
        }

        private void RunQueryRemoteClients()
        {
            var s = RemoteServerOrFail();
            if (s == null) return;
            string json = s.BuildClientListJson();
            int count = 0;
            try { count = Newtonsoft.Json.Linq.JArray.Parse(json).Count; } catch { }
            ReportData(CommandDataJson.New().Int("count", count).Text("clients", json).Build());
        }
    }
}
