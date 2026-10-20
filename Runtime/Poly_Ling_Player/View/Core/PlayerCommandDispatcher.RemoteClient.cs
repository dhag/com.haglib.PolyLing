// PlayerCommandDispatcher.RemoteClient.cs
// リモートのクライアントへの要求（requestClient / queryClientRequest / queryRemoteClients / sendMeshToClient）の受け口。
// sendMeshToClient はモデルを読むが、プロジェクトが無いときは自分で失敗を返すので、null 門より前で捌いてよい。
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
                case SendMeshToClientCommand c:   RunSendMeshToClient(c);   return true;
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
        /// 描画オブジェクト 1 つを putMesh としてクライアントへ押し出す。
        /// params は getRawData の結果と同じ平坦な形（座標は PolyLing のまま）。
        /// </summary>
        private void RunSendMeshToClient(SendMeshToClientCommand cmd)
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
            var mc = (cmd.MasterIndex >= 0 && cmd.MasterIndex < model.MeshContextCount)
                ? model.GetMeshContext(cmd.MasterIndex) : null;
            var mo = mc?.MeshObject;
            if (mo == null || mo.VertexCount == 0)
            {
                _pendingResult = CommandResult.Fail($"masterIndex {cmd.MasterIndex} に頂点を持つメッシュがありません", CommandErrorCodes.InvalidArgs);
                return;
            }

            var positions = new Newtonsoft.Json.Linq.JArray();
            foreach (var v in mo.Vertices)
            {
                var p = v != null ? v.Position : UnityEngine.Vector3.zero;
                positions.Add(p.x); positions.Add(p.y); positions.Add(p.z);
            }
            var faceSizes    = new Newtonsoft.Json.Linq.JArray();
            var faceVertices = new Newtonsoft.Json.Linq.JArray();
            foreach (var f in mo.Faces)
            {
                if (f?.VertexIndices == null) continue;
                faceSizes.Add(f.VertexIndices.Count);
                foreach (int vi in f.VertexIndices) faceVertices.Add(vi);
            }

            var p0 = new Newtonsoft.Json.Linq.JObject
            {
                ["name"]         = mc.Name ?? "",
                ["modelIndex"]   = cmd.ModelIndex,
                ["masterIndex"]  = cmd.MasterIndex,
                ["objectId"]     = mc.ObjectId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["positions"]    = positions,
                ["faceSizes"]    = faceSizes,
                ["faceVertices"] = faceVertices,
            };

            if (!s.RequestClient(cmd.UserName, SendMeshToClientCommand.ClientAction,
                                 p0.ToString(Newtonsoft.Json.Formatting.None), cmd.TimeoutMs,
                                 out int no, out string reason, out string code))
            {
                _pendingResult = CommandResult.Fail(reason, code);
                return;
            }
            ReportData(CommandDataJson.New().Int("requestNo", no).Build());
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
