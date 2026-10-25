// RemoteServerCore.ClientRequest.cs
// リモート：PolyLing からクライアントへの要求（Duplex の request / response）と、プロトコルの版。
// Runtime/Poly_Ling_Remote/ に配置
//
// ■ 流れ
//   1. クライアントは register の params に capabilities（受けられる action をカンマ区切り）を入れて登録する。
//   2. PolyLing は RequestClient で、そのユーザー（userName）の接続のうち action を受けられるものへ
//      Duplex の request（封筒の type が "request"）を送る。中身の JSON は
//        {"type":"request","action":"<action>","params":{…}}
//   3. クライアントは同じ封筒 id の response（封筒の type が "response"）で
//        {"success":true,"data":{…}}  または  {"success":false,"error":"…","code":"…"}
//      を返す。対応付けは部品（com.haglib.net_duplexchannel）の封筒 id が受け持つ。
//   4. 結果は要求番号ごとに残り、GetClientRequest で取り出す（コマンド queryClientRequest）。
//   要求は非同期で、コマンドは要求番号だけをすぐ返す（メインスレッドを待たせない）。
//
// ■ スレッド
//   RequestClient・GetClientRequest はメインスレッドから呼ぶ。返事は受信スレッドで届くので、
//   結果の書き込みは RunOnMainThread でメインスレッドへ戻す。

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HagLib.NET.Duplex;
using Newtonsoft.Json.Linq;
using Poly_Ling.Data;

namespace Poly_Ling.Remote
{
    public partial class RemoteServerCore
    {
        /// <summary>
        /// リモートのプロトコルの版。register の ack と server_info に載せる。
        /// メッセージの形を互換でなく変えたときに上げる（PolyLing_外部連携の契約.md も直す）。
        /// </summary>
        public const int ProtocolVersion = 1;

        /// <summary>PolyLing からクライアントへの要求 1 件の状態。</summary>
        public sealed class ClientRequestState
        {
            public const string Pending = "pending";
            public const string Done    = "done";
            public const string Failed  = "failed";

            public int    No;
            public string UserName;
            public string Action;
            /// <summary>pending / done / failed。</summary>
            public string Status = Pending;
            /// <summary>done のときクライアントが返した data（JSON オブジェクトの文字列。無ければ "{}"）。</summary>
            public string ResultJson;
            /// <summary>failed のときの理由。</summary>
            public string Error;
            /// <summary>failed のときの種類（CommandErrorCodes、またはクライアントが返した code）。</summary>
            public string Code;
        }

        private const int ClientRequestKeep = 64;
        private readonly Dictionary<int, ClientRequestState> _clientRequests = new Dictionary<int, ClientRequestState>();
        private int _clientRequestNo;

        /// <summary>
        /// 登録済みクライアントの顔ぶれが変わった（register・切断）。メインスレッドで発火する。
        /// 起動した子プロセス（Python アドオン）の register 待ちなどに使う。
        /// </summary>
        public event Action ClientsChanged;

        /// <summary>登録簿・切断の処理の後に呼ぶ（メインスレッド）。</summary>
        private void RaiseClientsChanged()
        {
            try { ClientsChanged?.Invoke(); }
            catch (Exception e) { Log("ClientsChanged の受け手で例外: " + e.Message); }
        }

        /// <summary>userName（空なら問わない）のクライアントのうち、action を capabilities に申告した接続があるか（メインスレッド）。</summary>
        public bool HasClientAction(string action, string userName = "")
        {
            if (string.IsNullOrEmpty(action)) return false;
            foreach (var kv in _clientRegistry)
            {
                if (!string.IsNullOrEmpty(userName) && kv.Value.UserName != userName) continue;
                if (kv.Value.Capabilities != null && Array.IndexOf(kv.Value.Capabilities, action) >= 0) return true;
            }
            return false;
        }

        /// <summary>
        /// userName のクライアントのうち action を受けられる接続へ要求を送る（メインスレッド）。
        /// paramsJson は JSON オブジェクトの文字列（空なら {}）。送れたら true と要求番号、送れなければ false と理由。
        /// onFinished を渡すと、結果が決まったとき（done / failed）にメインスレッドで呼ぶ（送れなかった false のときは呼ばない）。
        /// </summary>
        public bool RequestClient(string userName, string action, string paramsJson, int timeoutMs,
                                  out int requestNo, out string reason, out string code,
                                  Action<ClientRequestState> onFinished = null)
        {
            requestNo = 0;
            code = null;
            if (!IsRunning) { reason = "リモートサーバが起動していません"; code = CommandErrorCodes.NotReady; return false; }
            if (string.IsNullOrEmpty(action)) { reason = "action が空です"; code = CommandErrorCodes.InvalidArgs; return false; }

            JObject p;
            try { p = string.IsNullOrWhiteSpace(paramsJson) ? new JObject() : JObject.Parse(paramsJson); }
            catch (Exception e) { reason = "paramsJson が JSON オブジェクトではありません: " + e.Message; code = CommandErrorCodes.InvalidArgs; return false; }

            IDuplexChannel target = null;
            foreach (var kv in _clientRegistry)
            {
                if (!string.IsNullOrEmpty(userName) && kv.Value.UserName != userName) continue;
                if (kv.Value.Capabilities == null || Array.IndexOf(kv.Value.Capabilities, action) < 0) continue;
                target = kv.Key;
                break;
            }
            if (target == null)
            {
                reason = $"action \"{action}\" を受けられるクライアント（ユーザー \"{userName}\"）がつながっていません";
                code = CommandErrorCodes.NotReady;
                return false;
            }

            var body = new JObject { ["type"] = "request", ["action"] = action, ["params"] = p };
            var msg = TypedPayload.FromJson(body.ToString(Newtonsoft.Json.Formatting.None)).ToMessage();

            var state = new ClientRequestState { No = ++_clientRequestNo, UserName = userName ?? "", Action = action };
            _clientRequests[state.No] = state;
            if (_clientRequests.Count > ClientRequestKeep) _clientRequests.Remove(state.No - ClientRequestKeep);

            var cts = new CancellationTokenSource(timeoutMs > 0 ? timeoutMs : 30000);
            Task<DuplexMessage> task;
            try
            {
                if (target is TcpDuplexServerChannel tcp)        task = tcp.SendAndReceiveAsync(msg, WebSocketFrameKind.Text, cts.Token);
                else if (target is WebSocketDuplexChannel wsc)   task = wsc.SendAndReceiveAsync(msg, WebSocketFrameKind.Text, cts.Token);
                else                                             task = target.SendAndReceiveAsync(msg, cts.Token);
            }
            catch (Exception e)
            {
                cts.Dispose();
                state.Status = ClientRequestState.Failed;
                state.Error  = "送れません: " + e.Message;
                state.Code   = CommandErrorCodes.Failed;
                requestNo = state.No;
                reason = null;
                if (onFinished != null) RunOnMainThread(() => onFinished(state));
                return true;
            }

            task.ContinueWith(t =>
            {
                string resultJson = null, error = null, errCode = null;
                if (t.Status == TaskStatus.RanToCompletion)
                {
                    try
                    {
                        string json = TypedPayload.FromMessage(t.Result).GetJson();
                        var o = JObject.Parse(string.IsNullOrEmpty(json) ? "{}" : json);
                        if ((bool?)o["success"] == true)
                            resultJson = (o["data"] as JObject)?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}";
                        else
                        {
                            error   = (string)o["error"] ?? "クライアントが失敗を返しました";
                            errCode = (string)o["code"] ?? CommandErrorCodes.Failed;
                        }
                    }
                    catch (Exception e) { error = "返事が読めません: " + e.Message; errCode = CommandErrorCodes.Failed; }
                }
                else
                {
                    error   = t.IsCanceled ? "時間切れ（返事がありません）" : "送受信に失敗しました: " + t.Exception?.GetBaseException().Message;
                    errCode = CommandErrorCodes.Failed;
                }
                cts.Dispose();
                RunOnMainThread(() =>
                {
                    if (resultJson != null) { state.Status = ClientRequestState.Done; state.ResultJson = resultJson; }
                    else { state.Status = ClientRequestState.Failed; state.Error = error; state.Code = errCode; }
                    Log($"requestClient #{state.No} {state.Action} → {state.Status}{(error != null ? ": " + error : "")}");
                    if (onFinished != null)
                    {
                        try { onFinished(state); }
                        catch (Exception e) { Log($"requestClient #{state.No} の受け手で例外: {e.Message}"); }
                    }
                });
            });

            requestNo = state.No;
            reason = null;
            return true;
        }

        /// <summary>要求番号の状態（メインスレッド）。古くて捨てた・無い番号は null。</summary>
        public ClientRequestState GetClientRequest(int requestNo)
            => _clientRequests.TryGetValue(requestNo, out var s) ? s : null;

        /// <summary>登録済みクライアントの一覧（JSON 配列。clientType / userName / capabilities）。メインスレッド。</summary>
        public string BuildClientListJson()
        {
            var arr = new JArray();
            foreach (var kv in _clientRegistry)
                arr.Add(new JObject
                {
                    ["clientType"]   = kv.Value.ClientType ?? "",
                    ["userName"]     = kv.Value.UserName ?? "",
                    ["capabilities"] = new JArray(kv.Value.Capabilities ?? Array.Empty<string>()),
                });
            return arr.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
