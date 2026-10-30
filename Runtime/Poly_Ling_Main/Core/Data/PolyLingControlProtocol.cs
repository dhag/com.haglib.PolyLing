// PolyLingControlProtocol.cs
// ============================================================
// MCP サーバ（PolyLingMcpServer）との 1 行 JSON プロトコルのうち、
// UnityEditor に依存しない部分
// ============================================================
//
// 【なぜ分けたか】
//   ビルド版（Player）からも MCP サーバへ同じ要求を受けられるようにするため。
//   受信側は 2 つある。
//     Editor … Editor/EditorControl/PolyLingEditorControlServer.cs
//              （play / stop / refresh / recompile / prefab_* / state を自前で持つ）
//     Player … Runtime/Poly_Ling_Player/Remote/PolyLingPlayerControlServer.cs
//   両者が共通に受ける action はここだけで処理する。
//   以前は PolyLingEditorControlServer に書かれていたものを、中身を変えずに移した。
//
//   共通に受ける action:
//     ping / tools / tools_generic / tools_search / tools_describe / scenes / call
//
// 【スレッド】
//   メインスレッドから呼ぶこと（call は PolyLingCommandGateway を叩く）。
//   スレッドの付け替えは受信側の仕事。
//
// Runtime/Poly_Ling_Main/Core/Data/ に配置。#if UNITY_EDITOR 不使用。毎フレーム処理なし。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Remote;

namespace Poly_Ling.Data
{
    /// <summary>Editor・Player 共通の MCP 要求処理。</summary>
    public static class PolyLingControlProtocol
    {
        /// <summary>
        /// 共通の action なら処理して応答 JSON を返す。共通でなければ false（受信側が自前で処理する）。
        /// </summary>
        /// <param name="op">要求の action。</param>
        /// <param name="msg">解析済みの要求。</param>
        /// <param name="logTag">コンソールログの接頭辞（受信側のものを使う）。</param>
        /// <param name="responseJson">応答 JSON。false のときは null。</param>
        public static bool TryHandleCommon(string op, RemoteMessage msg, string logTag, out string responseJson)
        {
            switch (op)
            {
                case "ping":           responseJson = BuildOk(op, "OK");                     return true;
                case "tools":          responseJson = HandleTools(op, logTag);               return true;
                case "tools_generic":  responseJson = HandleToolsGeneric(op);                return true;
                case "tools_search":   responseJson = HandleToolsSearch(op, msg, logTag);    return true;
                case "tools_describe": responseJson = HandleToolsDescribe(op, msg);          return true;
                case "scenes":         responseJson = HandleScenes(op);                      return true;
                case "call":           responseJson = HandleCall(op, msg, logTag);           return true;
                default:               responseJson = null;                                  return false;
            }
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
        // 受信サーバはモデルに触れない。実行は PolyLingCommandGateway 経由で、
        // パネルが開いていなければ理由付きで失敗する。
        // ================================================================

        /// <summary>道具一覧（JSON Schema）をそのまま返す。</summary>
        private static string HandleTools(string op, string logTag)
        {
            try
            {
                string json = PanelCommandFactory.BuildToolsListJson();
                PanelCommandFactory.CountTools(out int usable, out int skipped);

                Debug.Log($"{logTag} tools: 出せる {usable} / 出せない {skipped}");

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
        //   tools_search   … profile=optimized 用。params: query / kind / category / folder / scene / offset / limit
        //                     コマンドとシナリオと Python アドオンを 1 本の列で返す（kind = command / scenario / addon で絞れる）。
        //   tools_describe … profile=optimized 用。params: names（コマンド名）/ scenarios（シナリオ名）/ addons（アドオンの id）/ resolve
        //                     / detail（outline 既定・full）/ items（項目 ID。シナリオ 1 本のときだけ）
        //                     names・scenarios はカンマ区切りで、合わせて最大 10 件
        //   scenes         … profile=optimized 用。利用シーン（SceneLibrary）の定義を全部返す
        //                     {"storePath":"…","scenes":[{"name","description","explicitCommands":[],"includeCategories":[],
        //                       "boostTags":[],"tools":[],"excludeCommands":[],"stateAssumptions":[],"hazardPolicy":[],
        //                       "verificationPolicy":[],"relatedScenarios":[],"notes"}],
        //                      "categories":[{"name","description"}],"stateNames":[],"hazardNames":[],
        //                      "hazardActions":[],"verificationNames":[]}（書ける名前の一覧）
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

        private static string HandleToolsSearch(string op, RemoteMessage msg, string logTag)
        {
            try
            {
                var p = msg?.Params;
                string query     = GetParam(p, "query");
                string category  = GetParam(p, "category");
                string folder    = GetParam(p, "folder");
                string sceneName = GetParam(p, "scene");

                string kind = (GetParam(p, "kind") ?? "").Trim().ToLowerInvariant();
                if (kind.Length > 0 && kind != PanelCommandFactory.SearchKindCommand && kind != PanelCommandFactory.SearchKindScenario
                    && kind != PanelCommandFactory.SearchKindAddon)
                    return BuildError(op, $"kind は {PanelCommandFactory.SearchKindCommand} / {PanelCommandFactory.SearchKindScenario} / {PanelCommandFactory.SearchKindAddon} / 空のどれかです: {kind}");
                if (kind == PanelCommandFactory.SearchKindAddon
                    && (!string.IsNullOrWhiteSpace(folder) || !string.IsNullOrWhiteSpace(category) || !string.IsNullOrWhiteSpace(sceneName)))
                    return BuildError(op, "アドオンは分類・フォルダ・利用シーンを持たないので、kind=addon とは一緒に使えません");
                if (kind == PanelCommandFactory.SearchKindCommand && !string.IsNullOrWhiteSpace(folder))
                    return BuildError(op, "folder はシナリオのフォルダです。kind=command とは一緒に使えません");
                if (kind == PanelCommandFactory.SearchKindScenario && !string.IsNullOrWhiteSpace(category))
                    return BuildError(op, "category はコマンドの分類です。kind=scenario とは一緒に使えません");
                if (!string.IsNullOrWhiteSpace(folder) && !string.IsNullOrWhiteSpace(category))
                    return BuildError(op, "category（コマンド）と folder（シナリオ）は一緒に使えません");

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

                // 利用シーンを指定したときは、パネルからモデル状態を取る。想定との照合（stateWarnings）と、
                // 危険性がいまのモデルで実際に壊すものの表示（stateConflicts）に使う。
                // パネルが開いていなければ null のまま渡し、照合しなかったことを結果に書かせる。
                ModelStateSnapshot state = null;
                if (scene != null)
                {
                    try { state = PolyLingCommandGateway.ModelState?.Invoke(); }
                    catch (Exception ex) { Debug.LogWarning($"{logTag} モデル状態を取れませんでした: {ex.Message}"); }
                }

                return BuildResult(op, PanelCommandFactory.BuildToolsSearchJson(query, kind, category, folder, scene, state, offset, limit));
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
                // names … コマンド名、scenarios … シナリオ名（どちらもカンマ区切り）。どちらか一方があればよい。
                // 件数の上限は両方の合計。シナリオ名にカンマは使えない前提（MCP サーバ側でも弾く）。
                var names     = SplitNames(GetParam(msg?.Params, "names"));
                var scenarios = SplitNames(GetParam(msg?.Params, "scenarios"));
                var addons    = SplitNames(GetParam(msg?.Params, "addons"));
                bool resolve  = CommandResultJson.IsTrue(GetParam(msg?.Params, "resolve"));
                string detail = GetParam(msg?.Params, "detail") ?? "";
                string items  = GetParam(msg?.Params, "items") ?? "";

                // 項目 ID はシナリオごとの名前なので、items はシナリオ 1 本のときだけ受ける。
                if (!string.IsNullOrWhiteSpace(items) && scenarios.Count != 1)
                    return BuildError(op, $"items はシナリオを 1 本だけ指定したときに使えます（{scenarios.Count} 本）");

                if (names.Count == 0 && scenarios.Count == 0 && addons.Count == 0)
                    return BuildError(op, "params.names（コマンド名）か params.scenarios（シナリオ名）か params.addons（アドオンの id）がありません（カンマ区切り）");
                int requested = names.Count + scenarios.Count + addons.Count;
                if (requested > ToolsDescribeMaxNames)
                    return BuildError(op, $"names・scenarios・addons は合わせて {ToolsDescribeMaxNames} 件までです（{requested} 件）");

                // コマンドの記述は従来どおり。シナリオは "scenarios" と "scenarioNotFound"、
                // アドオンは "addons" と "addonNotFound" を足す。
                string toolsJson = PanelCommandFactory.BuildToolsDescribeJson(names);
                if (scenarios.Count == 0 && addons.Count == 0) return BuildResult(op, toolsJson);

                var sb = new System.Text.StringBuilder(toolsJson.Length + 256);
                sb.Append(toolsJson, 0, toolsJson.Length - 1);   // 末尾の '}' を外して足す

                if (addons.Count > 0)
                {
                    sb.Append(",\"addons\":").Append(PanelCommandFactory.BuildAddonDescribeJson(addons, out var addonMissing));
                    sb.Append(",\"addonNotFound\":[");
                    for (int i = 0; i < addonMissing.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('"').Append(addonMissing[i].Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                    }
                    sb.Append(']');
                }
                if (scenarios.Count == 0) { sb.Append('}'); return BuildResult(op, sb.ToString()); }

                sb.Append(",\"scenarioRevision\":").Append(ScenarioLibrary.Revision);
                sb.Append(",\"resolve\":").Append(resolve ? "true" : "false");
                sb.Append(",\"scenarios\":[");
                var missing = new List<KeyValuePair<string, string>>();
                bool first = true;
                foreach (var n in scenarios)
                {
                    string json = Poly_Ling.Player.PlayerCommandDispatcher.BuildScenarioDescribeJson(n, resolve, detail, items, out string err);
                    if (json == null) { missing.Add(new KeyValuePair<string, string>(n, err ?? "unknown scenario")); continue; }
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(json);
                }
                sb.Append("],\"scenarioNotFound\":[");
                for (int i = 0; i < missing.Count; i++)
                {
                    var jb = new JsonBuilder();
                    jb.BeginObject();
                    jb.KeyValue("name",   missing[i].Key);
                    jb.KeyValue("reason", missing[i].Value);
                    jb.EndObject();
                    if (i > 0) sb.Append(',');
                    sb.Append(jb.ToString());
                }
                sb.Append("]}");
                return BuildResult(op, sb.ToString());
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
                    inner.KeyValue("origin",      SceneLibrary.OriginOf(s.Name));
                    AppendStringArray(inner, "explicitCommands",   s.ExplicitCommands);
                    AppendStringArray(inner, "includeCategories",  s.IncludeCategories);
                    AppendStringArray(inner, "boostTags",          s.BoostTags);
                    AppendStringArray(inner, "tools",              s.Tools);
                    AppendStringArray(inner, "excludeCommands",    s.ExcludeCommands);
                    AppendStringArray(inner, "stateAssumptions",   s.StateAssumptionItems());
                    AppendStringArray(inner, "hazardPolicy",       s.HazardPolicyItems());
                    AppendStringArray(inner, "verificationPolicy", s.VerificationItems());
                    AppendStringArray(inner, "relatedScenarios",   s.RelatedScenarios);
                    inner.KeyValue("notes", s.Notes);
                    inner.EndObject();
                }
                inner.EndArray();

                // 利用シーンの categories に書ける分類の一覧（正典）。人工知能が綴りを推測しないで済むように添える。
                inner.Key("categories");
                inner.BeginArray();
                foreach (var kv in PLCommandCategories.All)
                {
                    inner.BeginObject();
                    inner.KeyValue("name",        kv.Key);
                    inner.KeyValue("description", kv.Value);
                    inner.EndObject();
                }
                inner.EndArray();

                // stateAssumptions / hazardPolicy / verificationPolicy に書ける名前。綴りを推測させないために添える。
                AppendStringArray(inner, "stateNames", new List<string>(ModelStateSnapshot.Names));
                AppendStringArray(inner, "hazardNames", EnumNames<PLCommandHazard>());
                AppendStringArray(inner, "hazardActions", new List<string> { "allow", "warn", "require-confirmation", "hide" });
                AppendStringArray(inner, "verificationNames", EnumNames<PLCommandVerification>());

                inner.EndObject();
                return BuildResult(op, inner.ToString());
            }
            catch (Exception ex)
            {
                return BuildError(op, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>旗の列挙の名前（None を除く）。</summary>
        private static List<string> EnumNames<T>() where T : Enum
        {
            var list = new List<string>();
            foreach (var v in Enum.GetValues(typeof(T)))
                if (Convert.ToInt64(v) != 0) list.Add(v.ToString());
            return list;
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

        /// <summary>カンマ区切りの名前を、空と重複を除いて分ける。null は空の一覧。</summary>
        private static List<string> SplitNames(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return list;
            foreach (var part in text.Split(','))
            {
                var n = part.Trim();
                if (n.Length > 0 && !list.Contains(n)) list.Add(n);
            }
            return list;
        }

        /// <summary>成功応答の "result" に JSON オブジェクトを入れて返す。</summary>
        public static string BuildResult(string op, string resultJson)
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
        private static string HandleCall(string op, RemoteMessage msg, string logTag)
        {
            var raw = msg?.Params;
            if (raw == null || raw.Count == 0)
                return BuildError(op, "params がありません", CommandErrorCodes.InvalidArgs);

            if (!raw.TryGetValue("command", out string command) || string.IsNullOrEmpty(command))
                return BuildError(op, "params.command がありません", CommandErrorCodes.InvalidArgs);

            // modelIndex を省くか負にしたら、カレントのモデルを対象にする（以前は 0 に決め打ちで、
            // カレントでないモデル 0 を指して「参照先が別のモデルにあります」で断られることがあった）。
            // モデルが無ければ 0（モデルを要らないコマンドはそのまま通る）。
            int modelIndex = -1;
            if (raw.TryGetValue("modelIndex", out string mi) && !string.IsNullOrEmpty(mi))
            {
                if (!int.TryParse(mi, out modelIndex))
                    return BuildError(op, $"modelIndex を整数にできません: {mi}", CommandErrorCodes.InvalidArgs);
            }
            if (modelIndex < 0)
            {
                int cur = PolyLingCommandGateway.CurrentModelIndex?.Invoke() ?? -1;
                modelIndex = cur >= 0 ? cur : 0;
            }

            // 封筒の値。コマンドの引数ではないので外す。
            //   wantRevision … 応答に版と差分（modelRevision / createdObjectIds / deletedObjectIds）を足す。
            //                  既定は付けない。付けると応答が変わるので、要求した呼び出しにだけ足す。
            bool wantRevision = raw.TryGetValue("wantRevision", out string wr) && CommandResultJson.IsTrue(wr);

            var args = new Dictionary<string, string>();
            foreach (var kv in raw)
            {
                if (kv.Key == "command" || kv.Key == "modelIndex" || kv.Key == "wantRevision") continue;
                args[kv.Key] = kv.Value;
            }

            if (!PolyLingCommandGateway.IsReady)
                return BuildError(op, "PolyLing のパネルが開いていません", CommandErrorCodes.NotReady);

            var cmd = PanelCommandFactory.Create(command, modelIndex, args, out string createError);
            if (cmd == null)
                return BuildError(op, createError ?? "コマンドを組み立てられませんでした",
                                  CommandResultJson.CodeForCreateFailure(command));

            Debug.Log($"{logTag} call: {PanelCommandDump.Describe(cmd)}");

            var result = PolyLingCommandGateway.Invoke(cmd);
            if (result == null)
                return BuildError(op, "結果がありません", CommandErrorCodes.Failed);

            if (!result.Success)
                return BuildError(op, result.Reason ?? "unknown error", result.Code);

            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("type",    "response");
            jb.KeyValue("success", true);
            jb.KeyValue("action",  op);
            jb.KeyValue("command", command);
            // masterIndices / objectIds / 版 / result（欄の形はリモートの command と共通：CommandResultJson）
            CommandResultJson.AppendSuccess(jb, result, wantRevision);

            // result（コマンドが返した実データ）について。
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

            jb.EndObject();
            return jb.ToString();
        }

        // ================================================================
        // 応答の組み立て
        // ================================================================

        /// <summary>成功応答。"data" は必ず文字列。</summary>
        public static string BuildOk(string action, string data)
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

        /// <summary>失敗応答。code は CommandErrorCodes（省くと Failed）。</summary>
        public static string BuildError(string action, string error, string code = CommandErrorCodes.Failed)
        {
            var jb = new JsonBuilder();
            jb.BeginObject();
            jb.KeyValue("type",    "response");
            jb.KeyValue("success", false);
            jb.KeyValue("action",  action);
            CommandResultJson.AppendFailure(jb, error, code);
            jb.EndObject();
            return jb.ToString();
        }
    }
}
