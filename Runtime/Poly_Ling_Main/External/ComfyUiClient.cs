// ComfyUiClient.cs
// ============================================================
// ComfyUI の HTTP API を呼ぶ（画像の送信、ワークフローの取得、実行依頼、実行履歴・出力画像の取得）。
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/External/ に配置。コマンド comfy* が使う。
//
// ■ 使う入口（ComfyUI 本体 server.py / app/user_manager.py）
//   POST /upload/image            画像を入力フォルダへ送る（multipart：image・type・overwrite）
//   GET  /userdata/{file}         ユーザーフォルダのファイル（workflows/<名前> を %2F でつないで渡す）
//   GET  /object_info/{class}     ノード種類の入力の定義
//   POST /prompt                  実行依頼（{"prompt": API 形式, "client_id": …}）
//   GET  /history/{prompt_id}     実行履歴（終わるまでは空の {}）
//   GET  /queue                   実行中・待ちの一覧
//   GET  /view                    出力画像（filename・subfolder・type）
//
// ■ 待ち方
//   VoicevoxLipSync と同じく、応答を待つ間は呼び出し元（Unity の主スレッド）が止まる。
//   生成の完了は待たない（実行依頼はすぐ返し、完了は FetchHistory を呼び直して確かめる）。
// ============================================================

using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Poly_Ling.External
{
    public sealed class ComfyUiClient
    {
        public const string DefaultHost = "http://127.0.0.1:8188";

        private static readonly HttpClient _http = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        private readonly string _baseUrl;
        private readonly int    _timeoutMs;

        /// <summary>client_id（実行依頼に付ける。同じセッションの依頼をまとめる印）。</summary>
        public static readonly string ClientId = Guid.NewGuid().ToString("N");

        public ComfyUiClient(string host, int timeoutMs)
        {
            _baseUrl   = string.IsNullOrWhiteSpace(host) ? DefaultHost : host.Trim().TrimEnd('/');
            _timeoutMs = Math.Max(100, timeoutMs);
        }

        public string BaseUrl => _baseUrl;

        // ================================================================
        // 通信の共通部
        // ================================================================

        private struct Response
        {
            public bool   Ok;
            public int    Code;
            public byte[] Body;
            public string Text => Body == null ? "" : Encoding.UTF8.GetString(Body);
        }

        private Response Send(Func<HttpRequestMessage> makeRequest, out string error)
        {
            error = null;
            try
            {
                var task = Task.Run(async () =>
                {
                    using (var req = makeRequest())
                    using (var res = await _http.SendAsync(req).ConfigureAwait(false))
                    {
                        byte[] body = await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                        return new Response { Ok = res.IsSuccessStatusCode, Code = (int)res.StatusCode, Body = body };
                    }
                });
                if (!task.Wait(_timeoutMs))
                {
                    error = $"ComfyUI が {_timeoutMs} ms 以内に応答しません（{_baseUrl}）";
                    return default;
                }
                return task.Result;
            }
            catch (AggregateException ex)
            {
                error = $"ComfyUI に接続できません（{_baseUrl}）: {ex.GetBaseException().Message}";
                return default;
            }
            catch (Exception ex)
            {
                error = $"ComfyUI に接続できません（{_baseUrl}）: {ex.Message}";
                return default;
            }
        }

        private Response Get(string pathAndQuery, out string error)
            => Send(() => new HttpRequestMessage(HttpMethod.Get, _baseUrl + pathAndQuery), out error);

        private JToken GetJson(string pathAndQuery, out string error)
        {
            var r = Get(pathAndQuery, out error);
            if (error != null) return null;
            if (!r.Ok) { error = $"ComfyUI がエラーを返しました（{pathAndQuery}、HTTP {r.Code}）: {Clip(r.Text, 300)}"; return null; }
            try { return JToken.Parse(r.Text); }
            catch (Exception ex) { error = $"ComfyUI の応答を JSON として読めません（{pathAndQuery}）: {ex.Message}"; return null; }
        }

        // ================================================================
        // 画像の送信
        // ================================================================

        /// <summary>
        /// 画像を ComfyUI の入力フォルダへ送る。成功したら LoadImage の image に入れる名前
        /// （subfolder があれば「subfolder/name」）を返す。
        /// </summary>
        public string UploadImage(string fullPath, bool overwrite, out string error)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(fullPath); }
            catch (Exception ex) { error = $"画像を読めません: {ex.Message}"; return null; }
            string fileName = Path.GetFileName(fullPath);

            var r = Send(() =>
            {
                var form = new MultipartFormDataContent();
                var file = new ByteArrayContent(bytes);
                file.Headers.ContentType = new MediaTypeHeaderValue(GuessImageMime(fileName));
                form.Add(file, "image", fileName);
                form.Add(new StringContent("input"), "type");
                form.Add(new StringContent(overwrite ? "true" : "false"), "overwrite");
                return new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/upload/image") { Content = form };
            }, out error);
            if (error != null) return null;
            if (!r.Ok) { error = $"画像の送信を ComfyUI が拒否しました（HTTP {r.Code}）: {Clip(r.Text, 300)}"; return null; }

            JObject o;
            try { o = JObject.Parse(r.Text); }
            catch (Exception ex) { error = $"画像の送信の応答を読めません: {ex.Message}"; return null; }
            string name      = (string)o["name"];
            string subfolder = (string)o["subfolder"] ?? "";
            if (string.IsNullOrEmpty(name)) { error = $"画像の送信の応答に name がありません: {Clip(r.Text, 300)}"; return null; }
            return string.IsNullOrEmpty(subfolder) ? name : subfolder + "/" + name;
        }

        // ================================================================
        // ワークフロー
        // ================================================================

        /// <summary>ComfyUI のユーザーフォルダの workflows/&lt;name&gt; を取得する（.json が無ければ足す）。</summary>
        public JObject GetUserWorkflow(string name, out string error)
        {
            string n = name.Trim().Replace('\\', '/');
            if (!n.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) n += ".json";
            string file = Uri.EscapeDataString("workflows/" + n);   // "/" は %2F になる（画面側と同じ）
            var r = Get("/userdata/" + file, out error);
            if (error != null) return null;
            if (r.Code == 404) { error = $"ComfyUI の workflows フォルダに「{n}」がありません"; return null; }
            if (!r.Ok) { error = $"ワークフローを取得できません（HTTP {r.Code}）: {Clip(r.Text, 300)}"; return null; }
            try { return JObject.Parse(r.Text); }
            catch (Exception ex) { error = $"ワークフロー「{n}」を JSON として読めません: {ex.Message}"; return null; }
        }

        /// <summary>ノード種類の定義（/object_info/{class} の中身の、そのクラスの項目）。無ければ null で error。</summary>
        public JObject GetObjectInfo(string classType, out string error)
        {
            var tok = GetJson("/object_info/" + Uri.EscapeDataString(classType), out error);
            if (tok == null) return null;
            if (tok is JObject o && o[classType] is JObject info) return info;
            error = $"ComfyUI にノードの種類「{classType}」がありません（画面専用のノードか、カスタムノードが入っていません）";
            return null;
        }

        // ================================================================
        // 実行
        // ================================================================

        /// <summary>API 形式のワークフローの実行を依頼し、prompt_id を返す。</summary>
        public string QueuePrompt(JObject apiPrompt, out string error)
        {
            var body = new JObject { ["prompt"] = apiPrompt, ["client_id"] = ClientId };
            string json = body.ToString(Newtonsoft.Json.Formatting.None);
            var r = Send(() => new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/prompt")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            }, out error);
            if (error != null) return null;

            JObject o = null;
            try { o = JObject.Parse(r.Text); } catch { }
            if (!r.Ok)
            {
                error = $"ComfyUI が実行依頼を受け付けません（HTTP {r.Code}）: {DescribePromptError(o, r.Text)}";
                return null;
            }
            string id = (string)o?["prompt_id"];
            if (string.IsNullOrEmpty(id)) { error = $"実行依頼の応答に prompt_id がありません: {Clip(r.Text, 300)}"; return null; }
            return id;
        }

        /// <summary>実行履歴（/history/{id} の、その id の項目）。まだ無ければ null（error も null）。</summary>
        public JObject GetHistory(string promptId, out string error)
        {
            var tok = GetJson("/history/" + Uri.EscapeDataString(promptId), out error);
            if (tok == null) return null;
            return tok[promptId] as JObject;
        }

        /// <summary>実行中なら "running"、待ちなら "pending"、どちらにも無ければ "unknown"。失敗は null。</summary>
        public string GetQueueState(string promptId, out string error)
        {
            var tok = GetJson("/queue", out error);
            if (tok == null) return null;
            if (Contains(tok["queue_running"], promptId)) return "running";
            if (Contains(tok["queue_pending"], promptId)) return "pending";
            return "unknown";

            bool Contains(JToken list, string id)
            {
                if (!(list is JArray arr)) return false;
                foreach (var item in arr)
                    if (item is JArray e && e.Count > 1 && (string)e[1] == id) return true;
                return false;
            }
        }

        /// <summary>出力画像 1 枚を取得する。</summary>
        public byte[] GetView(string filename, string subfolder, string type, out string error)
        {
            string q = "/view?filename=" + Uri.EscapeDataString(filename ?? "")
                     + "&subfolder=" + Uri.EscapeDataString(subfolder ?? "")
                     + "&type="      + Uri.EscapeDataString(type ?? "output");
            var r = Get(q, out error);
            if (error != null) return null;
            if (!r.Ok) { error = $"画像「{filename}」を取得できません（HTTP {r.Code}）"; return null; }
            return r.Body;
        }

        // ================================================================
        // 補助
        // ================================================================

        private static string DescribePromptError(JObject o, string raw)
        {
            if (o == null) return Clip(raw, 300);
            var sb = new StringBuilder();
            string msg = (string)o["error"]?["message"];
            string det = (string)o["error"]?["details"];
            if (!string.IsNullOrEmpty(msg)) sb.Append(msg);
            if (!string.IsNullOrEmpty(det)) sb.Append("（").Append(det).Append("）");
            if (o["node_errors"] is JObject ne)
            {
                foreach (var p in ne.Properties())
                {
                    string cls = (string)p.Value["class_type"] ?? "";
                    if (p.Value["errors"] is JArray errs)
                        foreach (var e in errs)
                            sb.Append(" / ノード ").Append(p.Name).Append(' ').Append(cls).Append(": ")
                              .Append((string)e["message"]).Append(' ').Append((string)e["details"]);
                }
            }
            return sb.Length > 0 ? Clip(sb.ToString(), 600) : Clip(raw, 300);
        }

        private static string GuessImageMime(string fileName)
        {
            switch (Path.GetExtension(fileName).ToLowerInvariant())
            {
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".webp": return "image/webp";
                default: return "image/png";
            }
        }

        internal static string Clip(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? (s ?? "") : s.Substring(0, max) + "…";
    }
}
