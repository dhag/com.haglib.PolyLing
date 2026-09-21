// PanelCommandSchemaIndex.cs
// 道具一覧の段階的な取得（検索 → 個別の記述）と、道具一覧の版（schemaRevision）。
//
// 【なぜ要るか】
//   BuildToolsListJson は全コマンドの完全な JSON Schema を一度に返す。
//   MCP 側がこれを毎回取得すると、結果が会話履歴に残って以後の呼び出しの入力にも載る。
//   ここでは
//     ・検索   … 名前・説明・分類・タグ・引数の説明で照合し、名前と要約だけを返す
//     ・記述   … 指定したコマンドだけ完全な Schema を返す
//   の 2 段に分ける。Schema を組み立てるのは TryBuildToolJson の 1 本だけで、
//   第 2 の台帳は作らない。
//
// 【照合の方法】
//   日本語は分かち書きが無いため、語の完全一致では当たらない。
//   問い合わせを空白・句読点で語に分け、
//     ・ASCII だけの語 … 小文字化した部分一致。名前で当たれば 3 点、それ以外で 1 点
//     ・それ以外の語   … 文字 2-gram に分け、本文に含まれる 2-gram の割合 × 2 点
//                        （1 文字の語はその 1 文字で判定）
//   を足し合わせる。0 点のものは返さない。同点は名前順。
//   問い合わせが空なら全件を名前順に返す（ページングつきの索引として使える）。
//
// 【版】
//   schemaRevision は BuildToolsListJson と同じ並び・同じ文字列の SHA-256（先頭 16 桁）。
//   索引はドメインリロードまで保持する。ParameterLimits を実行中に変えても
//   索引側の版は変わらない（記述は毎回組み立て直すので値は最新）。
//
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommandSchema.cs と同じ場所）

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Poly_Ling.Data
{
    public static partial class PanelCommandFactory
    {
        // ================================================================
        // 索引
        // ================================================================

        private sealed class ToolIndexEntry
        {
            public string Name;
            public string NameLower;
            public string Description;
            public string Category;
            public string Tags;
            public string Writes;
            /// <summary>PLCommand.Hazards。None なら空（検索結果に出さない）。</summary>
            public string Hazards;
            public PLCommandHazard HazardFlags;
            /// <summary>照合用の本文（説明・分類・タグ・道具定義 JSON）を小文字化したもの。</summary>
            public string Haystack;
        }

        private static List<ToolIndexEntry> _toolIndex;
        private static string _toolIndexRevision;
        private static readonly object _toolIndexLock = new object();

        private static void EnsureToolIndex()
        {
            if (_toolIndex != null) return;
            lock (_toolIndexLock)
            {
                if (_toolIndex != null) return;

                var list = new List<ToolIndexEntry>();
                var all  = new StringBuilder();
                all.Append('[');
                bool first = true;

                foreach (var t in PLParamAudit.FindCommandTypes())
                {
                    if (!TryBuildToolJson(t, out string json, out _)) continue;

                    if (!first) all.Append(',');
                    all.Append(json);
                    first = false;

                    var attr = t.GetCustomAttribute<PLCommandAttribute>(inherit: false);
                    string name = ActionOf(t);
                    string desc = attr?.Description ?? "";
                    string cat  = attr?.Category ?? "";
                    string tags = attr?.Tags ?? "";

                    list.Add(new ToolIndexEntry
                    {
                        Name        = name,
                        NameLower   = name.ToLowerInvariant(),
                        Description = desc,
                        Category    = cat,
                        Tags        = tags,
                        Writes      = (attr?.Writes ?? PLWriteScope.Unspecified).ToString(),
                        Hazards     = FlagText(attr?.Hazards ?? PLCommandHazard.None),
                        HazardFlags = attr?.Hazards ?? PLCommandHazard.None,
                        Haystack    = (desc + "\n" + cat + "\n" + tags + "\n" + json).ToLowerInvariant(),
                    });
                }

                all.Append(']');

                _toolIndexRevision = ComputeSchemaRevision(all.ToString());
                _toolIndex = list;
            }
        }

        /// <summary>索引を組んだ時点の道具一覧の版。</summary>
        public static string ToolIndexRevision
        {
            get { EnsureToolIndex(); return _toolIndexRevision; }
        }

        /// <summary>道具一覧 JSON の版。SHA-256 の先頭 16 桁（小文字 16 進）に "sha256:" を付ける。</summary>
        public static string ComputeSchemaRevision(string toolsListJson)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(toolsListJson ?? ""));
                var sb = new StringBuilder("sha256:");
                for (int i = 0; i < 8; i++) sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // ================================================================
        // 検索
        // ================================================================

        /// <summary>
        /// 検索結果を JSON オブジェクトとして返す。
        /// {"schemaRevision","query","category"?,"scene"?,"total","offset","returned","hasMore",
        ///  "matches":[{"name","summary","writes","category"?}]}
        /// </summary>
        /// <param name="query">問い合わせ。空なら全件（名前順）。</param>
        /// <param name="category">分類。階層一致（"geometry" は "geometry.topology" も含む）。空なら絞らない。</param>
        /// <param name="scene">利用シーン（SceneLibrary）。null なら絞らない。対象外のコマンドは返さない。</param>
        /// <param name="state">
        /// カレントモデルの状態。利用シーンの stateAssumptions と突き合わせる。
        /// null ならモデル状態を取れなかった（パネルが開いていない）として、照合しなかったことを結果に書く。
        /// </param>
        public static string BuildToolsSearchJson(string query, string category, SceneDefinition scene,
                                                  ModelStateSnapshot state, int offset, int limit)
        {
            EnsureToolIndex();

            if (offset < 0) offset = 0;
            if (limit < 1) limit = 1;

            string[] terms = SplitQuery(query);
            var hits = new List<KeyValuePair<float, ToolIndexEntry>>();
            var actions = new Dictionary<ToolIndexEntry, PLHazardAction>();

            foreach (var e in _toolIndex)
            {
                // 分類は階層一致（"geometry" で "geometry.topology" も当たる）。
                if (!string.IsNullOrEmpty(category) && !SceneDefinition.CategoryMatches(e.Category, category))
                    continue;

                if (scene != null && !scene.Covers(e.Name, e.Category))
                    continue;

                // 利用シーンの hazardPolicy。hide は候補から外し、warn / require-confirmation は印を付ける。
                var action = scene?.ActionFor(e.HazardFlags) ?? PLHazardAction.Allow;
                if (action == PLHazardAction.Hide) continue;

                float score = terms.Length == 0 ? 1f : Score(e.NameLower, e.Haystack, terms);
                if (score <= 0f) continue;

                // 利用シーンの tags は候補を増やさず、順位だけを上げる。
                // 文字列照合の 1 語ぶん（ASCII の名前一致 3 点）より小さく、同点を割る程度にする。
                if (scene != null) score += TagBoostPerHit * scene.TagBoost(e.Tags);
                if (action != PLHazardAction.Allow) actions[e] = action;
                hits.Add(new KeyValuePair<float, ToolIndexEntry>(score, e));
            }

            hits.Sort((a, b) =>
            {
                int c = b.Key.CompareTo(a.Key);
                return c != 0 ? c : string.CompareOrdinal(a.Value.Name, b.Value.Name);
            });

            int end = Math.Min(hits.Count, offset + limit);

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"schemaRevision\":").Append(Quote(_toolIndexRevision));
            sb.Append(",\"query\":").Append(Quote(query ?? ""));
            if (!string.IsNullOrEmpty(category)) sb.Append(",\"category\":").Append(Quote(category));
            if (scene != null)
            {
                sb.Append(",\"scene\":").Append(Quote(scene.Name));

                // 想定するモデル状態との照合。食い違いは候補を変えず、警告として返す。
                if (scene.StateAssumptions.Count > 0)
                {
                    sb.Append(",\"stateChecked\":").Append(state != null ? "true" : "false");
                    var mismatches = scene.StateMismatches(state);
                    if (mismatches.Count > 0)
                    {
                        sb.Append(",\"stateWarnings\":[");
                        for (int i = 0; i < mismatches.Count; i++)
                        {
                            if (i > 0) sb.Append(',');
                            sb.Append(Quote(mismatches[i]));
                        }
                        sb.Append(']');
                    }
                }

                var verify = scene.VerificationItems();
                if (verify.Count > 0) sb.Append(",\"verificationPolicy\":").Append(Quote(string.Join(",", verify)));
                if (!string.IsNullOrEmpty(scene.Notes)) sb.Append(",\"notes\":").Append(Quote(scene.Notes));
            }
            sb.Append(",\"total\":").Append(hits.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"offset\":").Append(offset.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"returned\":").Append(Math.Max(0, end - offset).ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"hasMore\":").Append(end < hits.Count ? "true" : "false");
            sb.Append(",\"matches\":[");

            for (int i = offset; i < end; i++)
            {
                var e = hits[i].Value;
                if (i > offset) sb.Append(',');
                sb.Append("{\"name\":").Append(Quote(e.Name));
                sb.Append(",\"summary\":").Append(Quote(e.Description));
                sb.Append(",\"writes\":").Append(Quote(e.Writes));
                if (!string.IsNullOrEmpty(e.Category)) sb.Append(",\"category\":").Append(Quote(e.Category));
                // 壊れ得るものがあるときだけ付ける（要約を太らせないため。設計方針 14.4）。
                if (!string.IsNullOrEmpty(e.Hazards)) sb.Append(",\"hazards\":").Append(Quote(e.Hazards));
                // 利用シーンが警告・承認要求にしている危険性を持つとき、その扱いを添える。
                if (actions.TryGetValue(e, out var act))
                    sb.Append(",\"policy\":").Append(Quote(SceneDefinition.ActionText(act)));

                // 利用シーンを指定したときだけ：候補に入った理由、当たったタグ、いまのモデルで実際に壊すもの。
                if (scene != null)
                {
                    string by = scene.MatchedBy(e.Name, e.Category);
                    if (by.Length > 0 && by != "all") sb.Append(",\"matchedCategory\":").Append(Quote(by));

                    var tagsHit = scene.MatchedTags(e.Tags);
                    if (tagsHit.Count > 0) sb.Append(",\"matchedTags\":").Append(Quote(string.Join(",", tagsHit)));

                    if (state != null)
                    {
                        var conflicts = state.ConflictsWith(e.HazardFlags);
                        if (conflicts.Count > 0) sb.Append(",\"stateConflicts\":").Append(Quote(string.Join(",", conflicts)));
                    }
                }
                sb.Append('}');
            }

            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>利用シーンの tags が 1 つ当たるごとに足す点。</summary>
        private const float TagBoostPerHit = 0.5f;

        private static readonly char[] QuerySeparators =
        {
            ' ', '\t', '\r', '\n', ',', '.', ';', ':', '/', '\\', '(', ')', '[', ']', '{', '}', '"', '\'',
            '　', '、', '。', '・', '，', '．', '「', '」', '（', '）', '？', '！', '?', '!'
        };

        private static string[] SplitQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new string[0];
            var parts = query.ToLowerInvariant().Split(QuerySeparators, StringSplitOptions.RemoveEmptyEntries);
            return parts;
        }

        private static bool IsAscii(string s)
        {
            foreach (char c in s) if (c > 0x7F) return false;
            return true;
        }

        private static float Score(string nameLower, string haystack, string[] terms)
        {
            float score = 0f;

            foreach (var term in terms)
            {
                if (IsAscii(term))
                {
                    if (nameLower.Contains(term)) score += 3f;
                    else if (haystack.Contains(term)) score += 1f;
                    continue;
                }

                if (term.Length == 1)
                {
                    if (haystack.IndexOf(term[0]) >= 0) score += 2f;
                    continue;
                }

                int grams = term.Length - 1;
                int found = 0;
                for (int i = 0; i < grams; i++)
                {
                    if (haystack.Contains(term.Substring(i, 2))) found++;
                }
                score += 2f * found / grams;
            }

            return score;
        }

        /// <summary>
        /// 同じ照合を道具一覧の外でも使う（手本の検索 queryScenarios）。
        /// 問い合わせが空なら常に 1（絞らない）。0 なら当たらなかった。
        /// name は当たりの重みが大きい欄、body はそれ以外の本文。
        /// </summary>
        public static float ScoreText(string query, string name, string body)
        {
            var terms = SplitQuery(query);
            if (terms.Length == 0) return 1f;
            return Score((name ?? "").ToLowerInvariant(), (body ?? "").ToLowerInvariant(), terms);
        }

        /// <summary>旗の列挙を "A,B" の形にする。None なら空。</summary>
        private static string FlagText(Enum value)
        {
            if (Convert.ToInt64(value) == 0) return "";
            return value.ToString().Replace(" ", "");
        }

        private static void AppendFlags(StringBuilder sb, string key, string text)
        {
            if (!string.IsNullOrEmpty(text)) sb.Append(",\"").Append(key).Append("\":").Append(Quote(text));
        }

        // ================================================================
        // 記述
        // ================================================================

        /// <summary>
        /// 指定したコマンドだけの完全な道具定義を返す。
        /// {"schemaRevision","tools":[{"writes","category"?,"tags"?,"definition":{name,description,inputSchema,outputSchema?}}],
        ///  "notFound":[{"name","reason"}]}
        /// 名前は ResolveType と同じ規則（大小無視・別名あり）で引く。
        /// </summary>
        public static string BuildToolsDescribeJson(IList<string> names)
        {
            EnsureToolIndex();

            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"schemaRevision\":").Append(Quote(_toolIndexRevision));
            sb.Append(",\"tools\":[");

            var notFound = new List<KeyValuePair<string, string>>();
            bool first = true;

            if (names != null)
            {
                foreach (var raw in names)
                {
                    string name = raw?.Trim();
                    if (string.IsNullOrEmpty(name)) continue;

                    Type t = ResolveType(name);
                    if (t == null)
                    {
                        notFound.Add(new KeyValuePair<string, string>(name, "unknown command"));
                        continue;
                    }

                    if (!TryBuildToolJson(t, out string json, out string reason))
                    {
                        notFound.Add(new KeyValuePair<string, string>(name, reason ?? "schema unavailable"));
                        continue;
                    }

                    var attr = t.GetCustomAttribute<PLCommandAttribute>(inherit: false);

                    if (!first) sb.Append(',');
                    first = false;

                    sb.Append("{\"writes\":").Append(Quote((attr?.Writes ?? PLWriteScope.Unspecified).ToString()));
                    if (!string.IsNullOrEmpty(attr?.Category)) sb.Append(",\"category\":").Append(Quote(attr.Category));
                    if (!string.IsNullOrEmpty(attr?.Tags))     sb.Append(",\"tags\":").Append(Quote(attr.Tags));
                    AppendFlags(sb, "effects",       FlagText(attr?.Effects       ?? PLCommandEffect.None));
                    AppendFlags(sb, "hazards",       FlagText(attr?.Hazards       ?? PLCommandHazard.None));
                    AppendFlags(sb, "verification",  FlagText(attr?.Verification  ?? PLCommandVerification.None));
                    AppendFlags(sb, "preconditions", FlagText(attr?.Preconditions ?? PLCommandPrecondition.None));
                    sb.Append(",\"definition\":").Append(json);
                    sb.Append('}');
                }
            }

            sb.Append("],\"notFound\":[");
            for (int i = 0; i < notFound.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Quote(notFound[i].Key));
                sb.Append(",\"reason\":").Append(Quote(notFound[i].Value)).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
