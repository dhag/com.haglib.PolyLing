// ComfyUiWorkflow.cs
// ============================================================
// ComfyUI のワークフロー JSON を、実行依頼（POST /prompt）に渡す API 形式にする。
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/External/ に配置。コマンド comfyDescribeWorkflow / comfyQueuePrompt が使う。
//
// ■ 2 つの形式
//   API 形式   ：{ "<ノード番号>": { "class_type": …, "inputs": { 名前: 値 | [元ノード番号, 出力番号] } } }
//               そのまま使う。
//   画面保存形式：{ "nodes": [...], "links": [...] }（画面の「保存」で workflows フォルダに書かれるもの）
//               ComfyUI の画面（ComfyUI_frontend の executionUtil.ts graphToPrompt）と同じ規則で変換する。
//
// ■ 画面保存形式の変換規則（ComfyUI_frontend のソースで確かめたもの）
//   ・mode 2（ミュート）と 4（バイパス）のノードは出力しない。
//   ・Note / MarkdownNote / Reroute / PrimitiveNode は画面だけのノードで、出力しない。
//     Reroute は通り抜けて上流をたどり、PrimitiveNode はその値を入力の値にする。
//   ・バイパスのノードを元にする線は、そのノードの同じ型の入力の線へ通り抜ける
//     （同じ番号の入力の型が合えばそれ、無ければ型の合う最初の入力）。
//   ・入力の値（widgets_values）は名前の無い並び。並びはノードの入力の定義（/object_info の
//     required → optional の順）で入力欄を作った順。次の入力欄は値の並びに入るがプロンプトには入れない：
//       - control_after_generate（INT は control_after_generate 指定か名前が seed / noise_seed、
//         FLOAT・COMBO は指定があるとき）。COMBO はさらに control_filter_list が続く。
//       - アップロードボタン（required の COMBO に image_upload / video_upload / animated_image_upload
//         があると、required の末尾に upload が足される）。
//   ・どの入力が入力欄かは、ノードの inputs に widget の印があればそれを使い（今の画面の保存形式）、
//     無ければ（古い保存形式）入力の型で決める（INT・FLOAT・BOOLEAN・STRING・COMBO・選択肢の並び、
//     画面の入力欄の型。forceInput の付いたものは線だけ）。
//   ・値が配列のものは {"__value__": 配列} で包む（配列は線の印と区別するため）。
//   ・線は入力欄の値より優先する。出力に無いノードを指す線は消す。
//   ・inputs を持たないノード（手書きの JSON など）は、画面で読み込んだときと同じく、定義の線の入力
//     （入力欄でないもの）を required → optional の順に並べた番号で線をつなぐ。線の型の欄は無くてもよい。
//   ・値の並びの末尾には、カスタムノードの画面スクリプトが作成後に足した入力欄の値が続くことがある
//     （定義に無いので実行には使われない）。定義から求めた数より多い分は読み飛ばし、
//     少なければ推し量らずに失敗にする（理由にノードと数を出す）。
//   ・サブグラフ（definitions.subgraphs）は扱わない（失敗にする）。
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Poly_Ling.External
{
    public static class ComfyUiWorkflow
    {
        /// <summary>画面だけのノード（プロンプトに出さない）。</summary>
        private static readonly HashSet<string> VirtualTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Note", "MarkdownNote", "Reroute", "PrimitiveNode",
        };

        /// <summary>古い保存形式で入力欄とみなす型（ComfyUI_frontend の scripts/widgets.ts ComfyWidgets のキー）。</summary>
        private static readonly HashSet<string> WidgetTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "INT", "FLOAT", "BOOLEAN", "STRING", "MARKDOWN", "COMBO", "IMAGEUPLOAD", "COLOR", "IMAGECOMPARE",
            "BOUNDING_BOX", "CHART", "GALLERIA", "PAINTER", "COMPOSITOR", "TEXTAREA", "CURVE", "RANGE",
            "VIDEO_EDIT", "RESOLUTION_PREVIEW", "BOUNDING_BOXES", "LIGHT_INFO_PREVIEW", "COLORS",
        };

        private const int ModeNever  = 2;
        private const int ModeBypass = 4;

        /// <summary>画面保存形式か（nodes と links を持つ）。</summary>
        public static bool IsUiFormat(JObject wf) => wf?["nodes"] is JArray && wf["links"] != null;

        /// <summary>
        /// API 形式にする。API 形式ならそのまま複製を返す。画面保存形式なら getObjectInfo で
        /// ノード種類の定義を引いて変換する。失敗は null で error に理由。
        /// </summary>
        public static JObject ToApi(JObject wf, Func<string, (JObject info, string error)> getObjectInfo, out string error)
        {
            error = null;
            if (wf == null) { error = "ワークフローが空です"; return null; }

            if (!IsUiFormat(wf))
            {
                foreach (var p in wf.Properties())
                    if (!(p.Value is JObject n) || n["class_type"] == null)
                    { error = $"ワークフローの形式が分かりません（「{p.Name}」に class_type がありません）"; return null; }
                return (JObject)wf.DeepClone();
            }

            if (wf["definitions"]?["subgraphs"] is JArray sg && sg.Count > 0)
            { error = "サブグラフを含むワークフローは扱えません"; return null; }

            // ---- ノードと線の表 ----
            var nodes = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var t in (JArray)wf["nodes"])
                if (t is JObject n) nodes[Id(n["id"])] = n;

            var links = new Dictionary<string, Link>(StringComparer.Ordinal);
            foreach (var t in (JArray)wf["links"])
            {
                if (!TryReadLink(t, out var l)) { error = $"線の形式が分かりません: {t.ToString(Newtonsoft.Json.Formatting.None)}"; return null; }
                links[l.Id] = l;
            }

            var infoCache = new Dictionary<string, JObject>(StringComparer.Ordinal);
            var output = new JObject();

            foreach (var n in nodes.Values.OrderBy(x => (int?)x["order"] ?? 0))
            {
                string id   = Id(n["id"]);
                string type = (string)n["type"] ?? "";
                int    mode = (int?)n["mode"] ?? 0;
                if (mode == ModeNever || mode == ModeBypass || VirtualTypes.Contains(type)) continue;

                if (!infoCache.TryGetValue(type, out var info))
                {
                    var r = getObjectInfo(type);
                    if (r.info == null) { error = $"ノード {id}（{type}）: {r.error}"; return null; }
                    info = r.info;
                    infoCache[type] = info;
                }

                var inputs = new JObject();
                if (!MapWidgetValues(n, info, inputs, out string wErr)) { error = $"ノード {id}（{type}）: {wErr}"; return null; }

                if (n["inputs"] is JArray nin)
                {
                    foreach (var it in nin)
                    {
                        if (!(it is JObject slot)) continue;
                        string name = (string)slot["name"];
                        if (string.IsNullOrEmpty(name) || slot["link"] == null || slot["link"].Type == JTokenType.Null) continue;
                        var res = Resolve(Id(slot["link"]), nodes, links, 0);
                        if (res == null) continue;            // ミュートの先など。入力を作らない
                        inputs[name] = res;
                    }
                }
                else
                {
                    // inputs を持たないノード：画面で読み込んだときと同じく、定義の線の入力（入力欄でないもの）を
                    // required → optional の順に並べた番号で線をつなぐ
                    var linkInputs = LinkInputNames(info);
                    foreach (var l in links.Values)
                    {
                        if (l.TargetId != id) continue;
                        if (l.TargetSlot < 0 || l.TargetSlot >= linkInputs.Count)
                        { error = $"ノード {id}（{type}）: 線 {l.Id} の入力番号 {l.TargetSlot} に当たる入力がありません（線の入力は {string.Join(",", linkInputs)}）"; return null; }
                        var res = Resolve(l.Id, nodes, links, 0);
                        if (res == null) continue;
                        inputs[linkInputs[l.TargetSlot]] = res;
                    }
                }

                var node = new JObject
                {
                    ["inputs"]     = inputs,
                    ["class_type"] = type,
                    ["_meta"]      = new JObject { ["title"] = (string)n["title"] ?? (string)info["display_name"] ?? type },
                };
                output[id] = node;
            }

            // 出力に無いノードを指す線を消す
            foreach (var p in output.Properties())
            {
                var ins = (JObject)p.Value["inputs"];
                foreach (var ip in ins.Properties().ToList())
                    if (ip.Value is JArray a && a.Count == 2 && output[(string)a[0]] == null) ip.Remove();
            }
            return output;
        }

        /// <summary>overrides（{ "ノード番号": { "入力名": 値 } }）を API 形式のワークフローへ当てる。</summary>
        public static bool ApplyOverrides(JObject api, JObject overrides, out string error)
        {
            error = null;
            if (overrides == null) return true;
            foreach (var p in overrides.Properties())
            {
                if (!(api[p.Name] is JObject node)) { error = $"書き換え先のノード「{p.Name}」がワークフローにありません"; return false; }
                if (!(p.Value is JObject vals)) { error = $"ノード「{p.Name}」の書き換えは {{ 入力名: 値 }} で渡してください"; return false; }
                if (!(node["inputs"] is JObject ins)) { ins = new JObject(); node["inputs"] = ins; }
                foreach (var v in vals.Properties()) ins[v.Name] = v.Value.DeepClone();
            }
            return true;
        }

        /// <summary>API 形式のワークフローを 1 ノード 1 行で書く（番号 種類「題名」 入力名=値, …。線は ←番号:出力）。</summary>
        public static List<string> Describe(JObject api)
        {
            var lines = new List<string>();
            foreach (var p in api.Properties().OrderBy(x => int.TryParse(x.Name, out int k) ? k : int.MaxValue).ThenBy(x => x.Name, StringComparer.Ordinal))
            {
                var node  = (JObject)p.Value;
                string cls   = (string)node["class_type"] ?? "";
                string title = (string)node["_meta"]?["title"] ?? "";
                var parts = new List<string>();
                if (node["inputs"] is JObject ins)
                    foreach (var ip in ins.Properties())
                    {
                        if (ip.Value is JArray a && a.Count == 2 && (a[0].Type == JTokenType.String || a[0].Type == JTokenType.Integer))
                            parts.Add($"{ip.Name}←{a[0]}:{a[1]}");
                        else
                            parts.Add($"{ip.Name}={ComfyUiClient.Clip(ip.Value.ToString(Newtonsoft.Json.Formatting.None), 120)}");
                    }
                lines.Add($"{p.Name} {cls}「{title}」 {string.Join(", ", parts)}");
            }
            return lines;
        }

        // ================================================================
        // 入力欄の値
        // ================================================================

        private sealed class WidgetSlot
        {
            public string Name;
            public bool   Real;   // プロンプトに入れる
        }

        private static bool MapWidgetValues(JObject n, JObject info, JObject inputs, out string error)
        {
            error = null;
            JToken wv = n["widgets_values"];
            if (wv == null || wv.Type == JTokenType.Null) return true;

            // 辞書で保存しているノード（名前つき）はそのまま
            if (wv is JObject named)
            {
                foreach (var p in named.Properties())
                    if (HasInput(info, p.Name)) inputs[p.Name] = Wrap(p.Value);
                return true;
            }
            if (!(wv is JArray values)) { error = "widgets_values の形式が分かりません"; return false; }

            // 今の保存形式：inputs の widget 印で入力欄を決める
            HashSet<string> markedWidgets = null;
            if (n["inputs"] is JArray nin)
                foreach (var it in nin)
                    if (it is JObject s && s["widget"] is JObject)
                    {
                        markedWidgets ??= new HashSet<string>(StringComparer.Ordinal);
                        markedWidgets.Add((string)s["name"]);
                    }

            // 定義から作る入力欄が先頭に並び、その後ろにカスタムノードの画面スクリプトが足した入力欄
            // （例 Stable Fast 3D の visualization.js の preview）の値が続くことがある。後ろの分は
            // ノードの入力の定義に無いので実行には使われない。読み飛ばす。
            var slots = BuildWidgetSlots(info, markedWidgets);
            if (values.Count < slots.Count)
            {
                error = $"入力欄の値の数（{values.Count}）が、定義から求めた入力欄の数（{slots.Count}：{string.Join(",", slots.Select(s => s.Name))}）より少ない";
                return false;
            }
            for (int i = 0; i < slots.Count; i++)
                if (slots[i].Real) inputs[slots[i].Name] = Wrap(values[i]);
            return true;
        }

        private static List<WidgetSlot> BuildWidgetSlots(JObject info, HashSet<string> markedWidgets)
        {
            var slots = new List<WidgetSlot>();
            var required = info["input"]?["required"] as JObject;
            var optional = info["input"]?["optional"] as JObject;

            bool addUpload = false;
            if (required != null)
                foreach (var p in required.Properties())
                {
                    AddSlots(p.Name, p.Value);
                    if (IsMediaUploadCombo(p.Value)) addUpload = true;
                }
            if (addUpload && (markedWidgets == null || markedWidgets.Contains("upload")))
                slots.Add(new WidgetSlot { Name = "upload", Real = false });
            if (optional != null)
                foreach (var p in optional.Properties())
                    AddSlots(p.Name, p.Value);
            return slots;

            void AddSlots(string name, JToken spec)
            {
                if (!IsWidget(name, spec, markedWidgets)) return;
                slots.Add(new WidgetSlot { Name = name, Real = true });
                string kind = SpecType(spec);
                var opts = (spec as JArray)?.Count > 1 ? spec[1] as JObject : null;
                JToken cag = opts?["control_after_generate"];
                bool hasCag = cag != null && cag.Type != JTokenType.Null && !(cag.Type == JTokenType.Boolean && !(bool)cag);
                if (kind == "INT" && (hasCag || ((cag == null || cag.Type == JTokenType.Null) && (name == "seed" || name == "noise_seed"))))
                    slots.Add(new WidgetSlot { Name = "control_after_generate", Real = false });
                else if (kind == "FLOAT" && hasCag)
                    slots.Add(new WidgetSlot { Name = "control_after_generate", Real = false });
                else if (kind == "COMBO" && hasCag)
                {
                    slots.Add(new WidgetSlot { Name = "control_after_generate", Real = false });
                    slots.Add(new WidgetSlot { Name = "control_filter_list",    Real = false });
                }
            }
        }

        /// <summary>定義の線の入力（入力欄でないもの）の名前を required → optional の順で返す。</summary>
        private static List<string> LinkInputNames(JObject info)
        {
            var names = new List<string>();
            foreach (var key in new[] { "required", "optional" })
                if (info["input"]?[key] is JObject group)
                    foreach (var p in group.Properties())
                        if (!IsWidget(p.Name, p.Value, null)) names.Add(p.Name);
            return names;
        }

        private static bool IsWidget(string name, JToken spec, HashSet<string> markedWidgets)
        {
            if (markedWidgets != null) return markedWidgets.Contains(name);
            var opts = (spec as JArray)?.Count > 1 ? spec[1] as JObject : null;
            if (opts?["forceInput"]?.Type == JTokenType.Boolean && (bool)opts["forceInput"]) return false;
            return WidgetTypes.Contains(SpecType(spec));
        }

        /// <summary>入力の型。選択肢の並び（[[…], {…}]）は COMBO。</summary>
        private static string SpecType(JToken spec)
        {
            if (!(spec is JArray a) || a.Count == 0) return "";
            if (a[0] is JArray) return "COMBO";
            return (string)a[0] ?? "";
        }

        private static bool IsMediaUploadCombo(JToken spec)
        {
            var opts = (spec as JArray)?.Count > 1 ? spec[1] as JObject : null;
            if (opts == null || SpecType(spec) != "COMBO") return false;
            return IsTrue(opts["image_upload"]) || IsTrue(opts["video_upload"]) || IsTrue(opts["animated_image_upload"]);
            bool IsTrue(JToken t) => t != null && t.Type == JTokenType.Boolean && (bool)t;
        }

        private static bool HasInput(JObject info, string name)
            => info["input"]?["required"]?[name] != null || info["input"]?["optional"]?[name] != null;

        private static JToken Wrap(JToken v)
            => v is JArray ? new JObject { ["__value__"] = v.DeepClone() } : v?.DeepClone() ?? JValue.CreateNull();

        // ================================================================
        // 線
        // ================================================================

        private struct Link
        {
            public string Id, OriginId, TargetId, Type;
            public int    OriginSlot, TargetSlot;
        }

        private static bool TryReadLink(JToken t, out Link l)
        {
            l = default;
            // [線番号, 元ノード, 元の出力番号, 先ノード, 先の入力番号, 型]。型の無い 5 要素のものもある
            if (t is JArray a && a.Count >= 5)
            {
                l = new Link { Id = Id(a[0]), OriginId = Id(a[1]), OriginSlot = (int)a[2], TargetId = Id(a[3]), TargetSlot = (int)a[4],
                               Type = a.Count > 5 ? TypeText(a[5]) : "" };
                return true;
            }
            if (t is JObject o && o["id"] != null && o["origin_id"] != null)
            {
                l = new Link { Id = Id(o["id"]), OriginId = Id(o["origin_id"]), OriginSlot = (int)o["origin_slot"],
                               TargetId = Id(o["target_id"]), TargetSlot = (int)o["target_slot"], Type = TypeText(o["type"]) };
                return true;
            }
            return false;
        }

        /// <summary>線をたどり、入力の値（[元ノード番号, 出力番号] か PrimitiveNode の値）を返す。たどれなければ null。</summary>
        private static JToken Resolve(string linkId, Dictionary<string, JObject> nodes, Dictionary<string, Link> links, int depth)
        {
            if (depth > 256 || !links.TryGetValue(linkId, out var l)) return null;
            if (!nodes.TryGetValue(l.OriginId, out var origin)) return null;
            string type = (string)origin["type"] ?? "";
            int    mode = (int?)origin["mode"] ?? 0;

            if (type == "PrimitiveNode")
                return origin["widgets_values"] is JArray pv && pv.Count > 0 ? Wrap(pv[0]) : null;

            if (type == "Reroute" || mode == ModeBypass)
            {
                if (!(origin["inputs"] is JArray oin)) return null;
                JObject pick = null;
                if (type == "Reroute") pick = oin.Count > 0 ? oin[0] as JObject : null;
                else
                {
                    if (l.OriginSlot < oin.Count && oin[l.OriginSlot] is JObject same && TypeMatches(same, l.Type)) pick = same;
                    else foreach (var it in oin) if (it is JObject s && TypeMatches(s, l.Type)) { pick = s; break; }
                }
                if (pick == null || pick["link"] == null || pick["link"].Type == JTokenType.Null) return null;
                return Resolve(Id(pick["link"]), nodes, links, depth + 1);
            }

            if (mode == ModeNever || VirtualTypes.Contains(type)) return null;
            return new JArray(l.OriginId, l.OriginSlot);
        }

        private static bool TypeMatches(JObject slot, string linkType)
        {
            string t = TypeText(slot["type"]);
            return t == linkType || t == "*" || linkType == "*";
        }

        private static string TypeText(JToken t) => t == null ? "" : t.Type == JTokenType.String ? (string)t : t.ToString(Newtonsoft.Json.Formatting.None);

        private static string Id(JToken t)
            => t == null ? "" : t.Type == JTokenType.Integer ? ((long)t).ToString(CultureInfo.InvariantCulture) : (string)t ?? "";
    }
}
