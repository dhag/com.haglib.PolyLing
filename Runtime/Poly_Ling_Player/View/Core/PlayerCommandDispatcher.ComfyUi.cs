// PlayerCommandDispatcher.ComfyUi.cs
// ComfyUI を操作するコマンド（comfy*）の受け口。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【モデルを見ない】
//   どれも ComfyUI との通信と作業フォルダのファイルだけを扱うので、モーション JSON と同じく
//   プロジェクト・モデルの null 門より前で捌く。何も読み込んでいない状態でも使える。
//
// 【パスは必ず PLSandbox を通す】

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.External;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        // パネルからの発行では結果を受け取れないため、失敗理由をログにも出す。
        private void ComfyFail(string reason)
        {
            UnityEngine.Debug.LogError($"[PolyLing] ComfyUI: {reason}");
            Fail(reason);
        }

        /// <summary>ComfyUI のコマンドなら処理して true を返す。</summary>
        private bool DispatchComfyUi(PanelCommand cmd)
        {
            switch (cmd)
            {
                case ComfyDescribeWorkflowCommand c: RunComfyDescribeWorkflow(c); return true;
                case ComfyUploadImageCommand c:      RunComfyUploadImage(c);      return true;
                case ComfyQueuePromptCommand c:      RunComfyQueuePrompt(c);      return true;
                case ComfyFetchOutputsCommand c:     RunComfyFetchOutputs(c);     return true;
                default: return false;
            }
        }

        private void RunComfyDescribeWorkflow(ComfyDescribeWorkflowCommand cmd)
        {
            var client = new ComfyUiClient(cmd.Host, cmd.TimeoutMs);
            var api = LoadComfyWorkflowAsApi(client, cmd.WorkflowName, cmd.WorkflowPath, out bool wasUi, out string err);
            if (api == null) { ComfyFail(err); return; }

            ReportData(CommandDataJson.New()
                .Text ("format",    wasUi ? "ui" : "api")
                .Int  ("nodeCount", api.Count)
                .Texts("nodes",     ComfyUiWorkflow.Describe(api))
                .Build());
        }

        private void RunComfyUploadImage(ComfyUploadImageCommand cmd)
        {
            if (!PLSandbox.TryResolveRead(cmd.FilePath, out string full, out string reason)) { ComfyFail(reason); return; }
            if (!File.Exists(full)) { ComfyFail($"画像がありません: {full}"); return; }

            var client = new ComfyUiClient(cmd.Host, cmd.TimeoutMs);
            string name = client.UploadImage(full, cmd.Overwrite, out string err);
            if (name == null) { ComfyFail(err); return; }

            ReportData(CommandDataJson.New().Text("name", name).Build());
        }

        private void RunComfyQueuePrompt(ComfyQueuePromptCommand cmd)
        {
            JObject overrides = null;
            if (!string.IsNullOrWhiteSpace(cmd.OverridesJson))
            {
                try { overrides = JObject.Parse(cmd.OverridesJson); }
                catch (Exception ex) { ComfyFail($"overridesJson を JSON として読めません: {ex.Message}"); return; }
            }

            var client = new ComfyUiClient(cmd.Host, cmd.TimeoutMs);
            var api = LoadComfyWorkflowAsApi(client, cmd.WorkflowName, cmd.WorkflowPath, out _, out string err);
            if (api == null) { ComfyFail(err); return; }
            if (!ComfyUiWorkflow.ApplyOverrides(api, overrides, out err)) { ComfyFail(err); return; }

            string promptId = client.QueuePrompt(api, out err);
            if (promptId == null) { ComfyFail(err); return; }

            ReportData(CommandDataJson.New()
                .Text("promptId",  promptId)
                .Int ("nodeCount", api.Count)
                .Build());
        }

        private void RunComfyFetchOutputs(ComfyFetchOutputsCommand cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd.PromptId)) { ComfyFail("promptId が空です"); return; }
            if (string.IsNullOrWhiteSpace(cmd.OutputFolder)) { ComfyFail("outputFolder が空です"); return; }

            var client = new ComfyUiClient(cmd.Host, cmd.TimeoutMs);
            var hist = client.GetHistory(cmd.PromptId, out string err);
            if (err != null) { ComfyFail(err); return; }

            if (hist == null)
            {
                string state = client.GetQueueState(cmd.PromptId, out err);
                if (state == null) { ComfyFail(err); return; }
                ReportData(CommandDataJson.New().Text("status", state).Build());
                return;
            }

            string statusStr = (string)hist["status"]?["status_str"] ?? "";
            bool   completed = (bool?)hist["status"]?["completed"] ?? false;
            string status = statusStr == "error" ? "error" : completed ? "success" : "running";

            var files   = new List<string>();
            var skipped = new List<string>();
            var outputs = new List<string>();
            if (hist["outputs"] is JObject outs)
            {
                foreach (var nodeOut in outs.Properties())
                {
                    if (!(nodeOut.Value is JObject o)) continue;
                    var kinds = new List<string>();
                    foreach (var k in o.Properties()) kinds.Add(k.Name);
                    outputs.Add($"{nodeOut.Name}: {string.Join(",", kinds)}");

                    if (!(o["images"] is JArray images)) continue;
                    foreach (var imgTok in images)
                    {
                        if (!(imgTok is JObject img)) continue;
                        string fn  = (string)img["filename"] ?? "";
                        string sub = (string)img["subfolder"] ?? "";
                        string typ = (string)img["type"] ?? "output";
                        if (fn.Length == 0) continue;

                        string rel = Path.Combine(cmd.OutputFolder ?? "", Path.GetFileName(fn));
                        if (!PLSandbox.TryResolveWrite(rel, out string dst, out string reason)) { skipped.Add($"{fn}: {reason}"); continue; }

                        byte[] bytes = client.GetView(fn, sub, typ, out err);
                        if (bytes == null) { skipped.Add($"{fn}: {err}"); continue; }
                        try
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(dst));
                            File.WriteAllBytes(dst, bytes);
                            files.Add(dst);
                        }
                        catch (Exception ex) { skipped.Add($"{fn}: 書けません: {ex.Message}"); }
                    }
                }
            }

            string message = null;
            if (status == "error" && hist["status"]?["messages"] is JArray msgs)
            {
                foreach (var m in msgs)
                {
                    if (!(m is JArray pair) || pair.Count < 2 || (string)pair[0] != "execution_error") continue;
                    var d = pair[1];
                    message = $"ノード {(string)d["node_id"]}（{(string)d["node_type"]}）: {(string)d["exception_type"]}: {(string)d["exception_message"]}";
                }
            }

            ReportData(CommandDataJson.New()
                .Text ("status",  status)
                .Texts("files",   files)
                .Texts("skipped", skipped)
                .Texts("outputs", outputs)
                .Text ("message", message)
                .Build());
        }

        /// <summary>ワークフローを読んで API 形式にする。読込元は workflowName と workflowPath のどちらか一方。</summary>
        private static JObject LoadComfyWorkflowAsApi(ComfyUiClient client, string workflowName, string workflowPath,
                                                      out bool wasUi, out string error)
        {
            wasUi = false;
            bool byName = !string.IsNullOrWhiteSpace(workflowName);
            bool byPath = !string.IsNullOrWhiteSpace(workflowPath);
            if (byName == byPath) { error = "workflowName と workflowPath のどちらか一方を渡してください"; return null; }

            JObject wf;
            if (byName)
            {
                wf = client.GetUserWorkflow(workflowName, out error);
                if (wf == null) return null;
            }
            else
            {
                if (!PLSandbox.TryResolveRead(workflowPath, out string full, out error)) return null;
                if (!File.Exists(full)) { error = $"ワークフローがありません: {full}"; return null; }
                try { wf = JObject.Parse(File.ReadAllText(full)); }
                catch (Exception ex) { error = $"ワークフローを JSON として読めません: {ex.Message}"; return null; }
            }

            wasUi = ComfyUiWorkflow.IsUiFormat(wf);
            return ComfyUiWorkflow.ToApi(wf, cls =>
            {
                var info = client.GetObjectInfo(cls, out string e);
                return (info, e);
            }, out error);
        }
    }
}
