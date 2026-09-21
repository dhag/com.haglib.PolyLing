// SceneLibrary.cs
// 利用シーンの定義の置き場。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（ScenarioLibrary と同じ場所）
//
// 【利用シーンとは】
//   スキニングやモーフ編集のような作業の一局面について、検索するコマンド・見せる MCP の道具・
//   危険な操作の扱い・実行後の確認をあらかじめ決めておくプリセット。
//   手本（シナリオ）が作業全体、コマンドが 1 つの操作なら、利用シーンはその中間の一区間にあたる。
//   Unity のシーンとは無関係。考え方は PolyLing_利用シーン_カテゴライズ設計方針.md。
//
// 【何を持つか】（方針書 8.2）
//   name / description    … 名前（大小を区別しない一意）と説明
//   explicitCommands      … 分類に関係なく候補に入れるコマンド名
//   includeCategories     … 候補にする分類。階層で書ける（"geometry" は "geometry.topology" も含む）
//   boostTags             … 当たるコマンドの順位を上げるタグ。候補は増やさない
//   tools                 … 見せる MCP の固定の道具。空なら絞らない（サーバが tools/list で使う）
//   excludeCommands       … 候補から外すコマンド名
//   stateAssumptions      … 想定するモデル状態（ModelStateSnapshot.Names の名前 = true/false）
//   hazardPolicy          … 危険性（PLCommandHazard）ごとの扱い。allow / warn / require-confirmation / hide
//   verificationPolicy    … この利用シーンで実行後に確かめること（PLCommandVerification）
//   relatedScenarios      … 関係する手本（シナリオ）の名前
//   notes                 … 注意書き。禁忌を承知で破る事情など、機械的に表せない例外をここに書く
//
// 【候補の決まり方】（方針書 9.2）
//   excludeCommands に名前があれば外す。
//   そうでなければ、explicitCommands に名前がある、または includeCategories に属すれば候補。
//   explicitCommands と includeCategories が両方空なら全コマンドが候補。
//   hazardPolicy の扱い（hide なら外す、warn / require-confirmation なら印を付ける）は検索側が適用する。
//
// 【既製品】
//   BuiltinScenes が持つ既製の利用シーンは、ファイルに書かず、ここで利用者の定義と合わせて返す
//   （既製品が先、利用者の定義が後）。既製品と同じ名前での登録・削除は断る。
//   ファイルに既製品と同じ名前の行があれば、読み込み時に読み飛ばす。
//
// 【保存】
//   persistentDataPath/PolyLing/scenes.csv。1 行 1 利用シーン（利用者が作ったものだけ）。
//   v2 の列：name,description,explicitCommands,includeCategories,boostTags,tools,
//            excludeCommands,stateAssumptions,hazardPolicy,verificationPolicy,relatedScenarios,notes
//   一覧の列は ';' 区切り。stateAssumptions と hazardPolicy は "キー=値" を ';' で並べる。
//   値にカンマ・引用符があれば "…" で囲み、" は "" にする。改行は保存時に空白へ置き換える。
//   v1（6 列：name,description,commands,categories,tags,tools）は読み込み時に v2 へ読み替え、
//   次の保存から v2 で書く。
//
// 【スレッド】
//   ScenarioLibrary と同じく lock で守る。呼び出しはメインスレッドから。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>危険性に対する扱い。強い順に Hide ＞ RequireConfirmation ＞ Warn ＞ Allow。</summary>
    public enum PLHazardAction
    {
        Allow = 0,
        Warn = 1,
        RequireConfirmation = 2,
        Hide = 3,
    }

    /// <summary>利用シーン 1 つの定義。</summary>
    public sealed class SceneDefinition
    {
        public string Name        = "";
        public string Description = "";
        public List<string> ExplicitCommands  = new List<string>();
        public List<string> IncludeCategories = new List<string>();
        public List<string> BoostTags         = new List<string>();
        public List<string> Tools             = new List<string>();
        public List<string> ExcludeCommands   = new List<string>();
        public Dictionary<string, bool> StateAssumptions = new Dictionary<string, bool>(StringComparer.Ordinal);
        public Dictionary<PLCommandHazard, PLHazardAction> HazardPolicy = new Dictionary<PLCommandHazard, PLHazardAction>();
        public PLCommandVerification VerificationPolicy = PLCommandVerification.None;
        public List<string> RelatedScenarios  = new List<string>();
        public string Notes = "";

        /// <summary>explicitCommands と includeCategories が両方空か（= 全コマンドが候補）。</summary>
        public bool CoversAllCommands => ExplicitCommands.Count == 0 && IncludeCategories.Count == 0;

        public SceneDefinition Clone() => new SceneDefinition
        {
            Name               = Name,
            Description        = Description,
            ExplicitCommands   = new List<string>(ExplicitCommands),
            IncludeCategories  = new List<string>(IncludeCategories),
            BoostTags          = new List<string>(BoostTags),
            Tools              = new List<string>(Tools),
            ExcludeCommands    = new List<string>(ExcludeCommands),
            StateAssumptions   = new Dictionary<string, bool>(StateAssumptions, StringComparer.Ordinal),
            HazardPolicy       = new Dictionary<PLCommandHazard, PLHazardAction>(HazardPolicy),
            VerificationPolicy = VerificationPolicy,
            RelatedScenarios   = new List<string>(RelatedScenarios),
            Notes              = Notes,
        };

        // ================================================================
        // 候補・順位・扱い
        // ================================================================

        /// <summary>
        /// コマンドがこの利用シーンの候補か。boostTags と hazardPolicy は見ない（TagBoost / ActionFor を使う）。
        /// </summary>
        public bool Covers(string commandName, string category)
        {
            if (ContainsIgnoreCase(ExcludeCommands, commandName)) return false;
            if (CoversAllCommands) return true;
            if (ContainsIgnoreCase(ExplicitCommands, commandName)) return true;

            foreach (var requested in IncludeCategories)
                if (CategoryMatches(category, requested)) return true;

            return false;
        }

        /// <summary>
        /// 候補に入った理由。"explicit"（explicitCommands）、当たった includeCategories の値、
        /// 全コマンドが候補の利用シーンなら "all"。候補でなければ空。
        /// </summary>
        public string MatchedBy(string commandName, string category)
        {
            if (ContainsIgnoreCase(ExcludeCommands, commandName)) return "";
            if (ContainsIgnoreCase(ExplicitCommands, commandName)) return "explicit";
            foreach (var requested in IncludeCategories)
                if (CategoryMatches(category, requested)) return requested;
            return CoversAllCommands ? "all" : "";
        }

        /// <summary>コマンドのタグのうち boostTags と一致したもの。</summary>
        public List<string> MatchedTags(string tagsCsv)
        {
            var list = new List<string>();
            if (BoostTags.Count == 0 || string.IsNullOrEmpty(tagsCsv)) return list;
            foreach (var raw in tagsCsv.Split(','))
            {
                var t = raw.Trim();
                if (t.Length > 0 && ContainsIgnoreCase(BoostTags, t)) list.Add(t);
            }
            return list;
        }

        /// <summary>コマンドのタグのうち boostTags と一致する数。検索順位の補正に使う。</summary>
        public int TagBoost(string tagsCsv)
        {
            if (BoostTags.Count == 0 || string.IsNullOrEmpty(tagsCsv)) return 0;
            int hits = 0;
            foreach (var raw in tagsCsv.Split(','))
            {
                var t = raw.Trim();
                if (t.Length > 0 && ContainsIgnoreCase(BoostTags, t)) hits++;
            }
            return hits;
        }

        /// <summary>
        /// コマンドの危険性に対するこの利用シーンの扱い。複数の危険性を持つときは最も強い扱い。
        /// hazardPolicy に書いていない危険性は Allow。
        /// </summary>
        public PLHazardAction ActionFor(PLCommandHazard hazards)
        {
            var result = PLHazardAction.Allow;
            if (hazards == PLCommandHazard.None) return result;
            foreach (var kv in HazardPolicy)
                if ((hazards & kv.Key) != 0 && kv.Value > result) result = kv.Value;
            return result;
        }

        /// <summary>stateAssumptions と実際のモデル状態の食い違いを文にして返す。無ければ空。</summary>
        public List<string> StateMismatches(ModelStateSnapshot actual)
        {
            var list = new List<string>();
            if (actual == null) return list;
            foreach (var kv in StateAssumptions)
            {
                if (!actual.TryGet(kv.Key, out bool value)) continue;
                if (value != kv.Value)
                    list.Add($"{kv.Key}: expected {(kv.Value ? "true" : "false")}, actual {(value ? "true" : "false")}");
            }
            return list;
        }

        /// <summary>
        /// 分類の階層一致。requested が "geometry" なら "geometry" と "geometry.xxx" に当たる。
        /// "geo" のような途中までの一致は当てない（区切りの "." まで見る）。大小文字は区別しない。
        /// </summary>
        public static bool CategoryMatches(string commandCategory, string requested)
        {
            if (string.IsNullOrEmpty(commandCategory) || string.IsNullOrEmpty(requested)) return false;
            if (string.Equals(commandCategory, requested, StringComparison.OrdinalIgnoreCase)) return true;
            return commandCategory.Length > requested.Length
                && commandCategory[requested.Length] == '.'
                && commandCategory.StartsWith(requested, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (var s in list)
                if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // ================================================================
        // 文字列との変換（setScene の引数と CSV の列で共用）
        // ================================================================

        /// <summary>"allow" / "warn" / "require-confirmation" / "hide" を扱いへ。</summary>
        public static bool TryParseAction(string text, out PLHazardAction action)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case "allow":                action = PLHazardAction.Allow;               return true;
                case "warn":                 action = PLHazardAction.Warn;                return true;
                case "require-confirmation": action = PLHazardAction.RequireConfirmation; return true;
                case "hide":                 action = PLHazardAction.Hide;                return true;
                default:                     action = PLHazardAction.Allow;               return false;
            }
        }

        public static string ActionText(PLHazardAction action)
        {
            switch (action)
            {
                case PLHazardAction.Warn:                return "warn";
                case PLHazardAction.RequireConfirmation: return "require-confirmation";
                case PLHazardAction.Hide:                return "hide";
                default:                                 return "allow";
            }
        }

        /// <summary>"名前=true" の並びを stateAssumptions へ。名前は ModelStateSnapshot.Names のどれか。</summary>
        public static bool TryParseStateAssumptions(IEnumerable<string> items, Dictionary<string, bool> into, out string error)
        {
            error = null;
            if (items == null) return true;
            foreach (var raw in items)
            {
                var item = (raw ?? "").Trim();
                if (item.Length == 0) continue;
                int eq = item.IndexOf('=');
                string key = eq < 0 ? item : item.Substring(0, eq).Trim();
                string val = eq < 0 ? "true" : item.Substring(eq + 1).Trim().ToLowerInvariant();

                if (!ModelStateSnapshot.IsKnownName(key))
                { error = $"stateAssumptions の名前が正しくありません: {key}（{string.Join(", ", ModelStateSnapshot.Names)}）"; return false; }
                if (val != "true" && val != "false")
                { error = $"stateAssumptions の値は true か false です: {item}"; return false; }

                into[key] = val == "true";
            }
            return true;
        }

        /// <summary>"危険性=扱い" の並びを hazardPolicy へ。危険性は PLCommandHazard の名前（None を除く）。</summary>
        public static bool TryParseHazardPolicy(IEnumerable<string> items, Dictionary<PLCommandHazard, PLHazardAction> into, out string error)
        {
            error = null;
            if (items == null) return true;
            foreach (var raw in items)
            {
                var item = (raw ?? "").Trim();
                if (item.Length == 0) continue;
                int eq = item.IndexOf('=');
                if (eq < 0) { error = $"hazardPolicy は 危険性=扱い の形です: {item}"; return false; }

                string key = item.Substring(0, eq).Trim();
                if (!Enum.TryParse(key, false, out PLCommandHazard hazard) || hazard == PLCommandHazard.None
                    || !Enum.IsDefined(typeof(PLCommandHazard), hazard))
                { error = $"hazardPolicy の危険性が正しくありません: {key}"; return false; }

                if (!TryParseAction(item.Substring(eq + 1), out var action))
                { error = $"hazardPolicy の扱いは allow / warn / require-confirmation / hide です: {item}"; return false; }

                into[hazard] = action;
            }
            return true;
        }

        /// <summary>検証の名前の並びを旗へ。名前は PLCommandVerification（None を除く）。</summary>
        public static bool TryParseVerification(IEnumerable<string> items, out PLCommandVerification flags, out string error)
        {
            flags = PLCommandVerification.None;
            error = null;
            if (items == null) return true;
            foreach (var raw in items)
            {
                var name = (raw ?? "").Trim();
                if (name.Length == 0) continue;
                if (!Enum.TryParse(name, false, out PLCommandVerification v) || v == PLCommandVerification.None
                    || !Enum.IsDefined(typeof(PLCommandVerification), v))
                { error = $"verificationPolicy の名前が正しくありません: {name}"; return false; }
                flags |= v;
            }
            return true;
        }

        public List<string> StateAssumptionItems()
        {
            var list = new List<string>();
            foreach (var n in ModelStateSnapshot.Names)
                if (StateAssumptions.TryGetValue(n, out bool v)) list.Add(n + "=" + (v ? "true" : "false"));
            return list;
        }

        public List<string> HazardPolicyItems()
        {
            var list = new List<string>();
            foreach (PLCommandHazard h in Enum.GetValues(typeof(PLCommandHazard)))
                if (h != PLCommandHazard.None && HazardPolicy.TryGetValue(h, out var a)) list.Add(h + "=" + ActionText(a));
            return list;
        }

        public List<string> VerificationItems()
        {
            var list = new List<string>();
            foreach (PLCommandVerification v in Enum.GetValues(typeof(PLCommandVerification)))
                if (v != PLCommandVerification.None && (VerificationPolicy & v) != 0) list.Add(v.ToString());
            return list;
        }
    }

    /// <summary>利用シーンの定義の置き場。</summary>
    public static class SceneLibrary
    {
        private const string LogTag   = "[SceneLibrary]";
        private const string HeaderV1 = "PolyLing_Scenes,v1";
        private const string HeaderV2 = "PolyLing_Scenes,v2";
        private const string ColumnsV1 = "name,description,commands,categories,tags,tools";
        private const string ColumnsV2 = "name,description,explicitCommands,includeCategories,boostTags,tools,"
                                       + "excludeCommands,stateAssumptions,hazardPolicy,verificationPolicy,relatedScenarios,notes";

        private static List<SceneDefinition> _items;
        private static readonly object _lock = new object();

        private static string Dir      => Path.Combine(Application.persistentDataPath, "PolyLing");
        private static string FileName => Path.Combine(Dir, "scenes.csv");

        /// <summary>保存先の絶対パス（表示・手動編集用）。</summary>
        public static string StorePath => FileName;

        /// <summary>
        /// 置き場の版。登録・削除・読み直しで 1 つ進む。
        /// 呼び出し側が「前に見たときから変わったか」を 1 つの数で判定するために使う。
        /// 保存しない（起動ごとに 0 から数え直す）。
        /// </summary>
        public static int Revision { get; private set; }

        // ================================================================
        // 読み書き
        // ================================================================

        private static void EnsureLoaded()
        {
            if (_items != null) return;
            lock (_lock)
            {
                if (_items != null) return;
                _items = ReadFile();
            }
        }

        /// <summary>ファイルから読み直す。手で編集したあとに呼ぶ。</summary>
        public static void Reload()
        {
            lock (_lock) { _items = ReadFile(); Revision++; }
        }

        private static List<SceneDefinition> ReadFile()
        {
            var list = new List<SceneDefinition>();
            try
            {
                if (!File.Exists(FileName)) return list;

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                bool v1 = false;

                foreach (var line in File.ReadAllLines(FileName, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith(HeaderV1, StringComparison.Ordinal)) { v1 = true; continue; }
                    if (line.StartsWith(HeaderV2, StringComparison.Ordinal)) { v1 = false; continue; }
                    if (line == ColumnsV1 || line == ColumnsV2) continue;

                    var f = SplitCsvLine(line);
                    string problem = null;
                    var s = v1 ? FromV1(f) : FromV2(f, out problem);
                    if (problem != null)
                        Debug.LogWarning($"{LogTag} 読めない値を読み飛ばしました（{s.Name}）: {problem}");

                    // 名前が無い・重なる行、既製品と同じ名前の行は読み飛ばす（Get がどちらを返すか決まらなくなる）。
                    if (s.Name.Length == 0 || !seen.Add(s.Name) || BuiltinScenes.IsBuiltin(s.Name))
                    {
                        Debug.LogWarning($"{LogTag} 名前が無い・重なっている・既製品と同じ名前の利用シーンを読み飛ばしました: {s.Name}");
                        continue;
                    }
                    list.Add(s);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 読み込みに失敗しました: {e.Message}");
            }
            return list;
        }

        /// <summary>v1 の 6 列を v2 の定義へ読み替える。意味は変えない（名前だけ変わった）。</summary>
        private static SceneDefinition FromV1(List<string> f)
        {
            while (f.Count < 6) f.Add("");
            return new SceneDefinition
            {
                Name              = f[0].Trim(),
                Description       = f[1],
                ExplicitCommands  = SplitList(f[2]),
                IncludeCategories = SplitList(f[3]),
                BoostTags         = SplitList(f[4]),
                Tools             = SplitList(f[5]),
            };
        }

        private static SceneDefinition FromV2(List<string> f, out string problem)
        {
            problem = null;
            while (f.Count < 12) f.Add("");
            var s = new SceneDefinition
            {
                Name              = f[0].Trim(),
                Description       = f[1],
                ExplicitCommands  = SplitList(f[2]),
                IncludeCategories = SplitList(f[3]),
                BoostTags         = SplitList(f[4]),
                Tools             = SplitList(f[5]),
                ExcludeCommands   = SplitList(f[6]),
                RelatedScenarios  = SplitList(f[10]),
                Notes             = f[11],
            };

            // 手で書き換えたファイルに誤りがあっても、他の列は生かす。誤った列だけ空にする。
            if (!SceneDefinition.TryParseStateAssumptions(SplitList(f[7]), s.StateAssumptions, out string e1))
            { s.StateAssumptions.Clear(); problem = e1; }
            if (!SceneDefinition.TryParseHazardPolicy(SplitList(f[8]), s.HazardPolicy, out string e2))
            { s.HazardPolicy.Clear(); problem = e2; }
            if (!SceneDefinition.TryParseVerification(SplitList(f[9]), out var v, out string e3))
            { problem = e3; }
            else s.VerificationPolicy = v;

            return s;
        }

        private static void WriteFile(List<SceneDefinition> list)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.Append(HeaderV2).Append('\n');
                sb.Append(ColumnsV2).Append('\n');
                foreach (var s in list)
                {
                    sb.Append(CsvField(s.Name)).Append(',')
                      .Append(CsvField(s.Description)).Append(',')
                      .Append(CsvField(JoinList(s.ExplicitCommands))).Append(',')
                      .Append(CsvField(JoinList(s.IncludeCategories))).Append(',')
                      .Append(CsvField(JoinList(s.BoostTags))).Append(',')
                      .Append(CsvField(JoinList(s.Tools))).Append(',')
                      .Append(CsvField(JoinList(s.ExcludeCommands))).Append(',')
                      .Append(CsvField(JoinList(s.StateAssumptionItems()))).Append(',')
                      .Append(CsvField(JoinList(s.HazardPolicyItems()))).Append(',')
                      .Append(CsvField(JoinList(s.VerificationItems()))).Append(',')
                      .Append(CsvField(JoinList(s.RelatedScenarios))).Append(',')
                      .Append(CsvField(s.Notes)).Append('\n');
                }
                File.WriteAllText(FileName, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 保存に失敗しました: {e.Message}");
            }
        }

        // ================================================================
        // 参照
        // ================================================================

        /// <summary>既製品と利用者の定義を合わせた数。</summary>
        public static int Count
        {
            get { EnsureLoaded(); lock (_lock) { return BuiltinScenes.All().Count + _items.Count; } }
        }

        /// <summary>全利用シーンの写し。既製品が先、利用者の定義が登録順で後。</summary>
        public static List<SceneDefinition> GetAll()
        {
            EnsureLoaded();
            var list = BuiltinScenes.All();
            lock (_lock)
            {
                foreach (var s in _items) list.Add(s.Clone());
            }
            return list;
        }

        /// <summary>既製品か利用者が作ったものか。"builtin" / "user"。</summary>
        public static string OriginOf(string name) => BuiltinScenes.IsBuiltin(name) ? "builtin" : "user";

        /// <summary>名前で引いた写し。無ければ null。大小を区別しない。既製品を先に探す。</summary>
        public static SceneDefinition Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var b in BuiltinScenes.All())
                if (string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)) return b;

            EnsureLoaded();
            lock (_lock)
            {
                var s = FindInternal(name);
                return s?.Clone();
            }
        }

        private static SceneDefinition FindInternal(string name)
        {
            foreach (var s in _items)
                if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return s;
            return null;
        }

        // ================================================================
        // 変更
        // ================================================================

        /// <summary>登録する。同名があれば overwrite のときだけ差し替える。保存まで行う。</summary>
        public static bool Register(SceneDefinition scene, bool overwrite, out string error)
        {
            error = null;
            if (scene == null) { error = "利用シーンが null"; return false; }

            string name = (scene.Name ?? "").Trim();
            if (name.Length == 0) { error = "利用シーンの名前が空です"; return false; }
            if (BuiltinScenes.IsBuiltin(name))
            { error = $"既製の利用シーンは変えられません: {name}（別の名前で作ってください）"; return false; }

            var copy = scene.Clone();
            copy.Name              = name;
            copy.Description       = OneLine(copy.Description);
            copy.Notes             = OneLine(copy.Notes);
            copy.ExplicitCommands  = Clean(copy.ExplicitCommands);
            copy.IncludeCategories = Clean(copy.IncludeCategories);
            copy.BoostTags         = Clean(copy.BoostTags);
            copy.Tools             = Clean(copy.Tools);
            copy.ExcludeCommands   = Clean(copy.ExcludeCommands);
            copy.RelatedScenarios  = Clean(copy.RelatedScenarios);

            EnsureLoaded();
            lock (_lock)
            {
                var existing = FindInternal(name);
                if (existing != null)
                {
                    if (!overwrite) { error = $"同じ名前の利用シーンがあります: {name}（overwrite を立てると差し替える）"; return false; }
                    _items[_items.IndexOf(existing)] = copy;
                }
                else
                {
                    _items.Add(copy);
                }
                WriteFile(_items);
                Revision++;
            }
            return true;
        }

        /// <summary>消す。保存まで行う。</summary>
        public static bool Remove(string name, out string error)
        {
            error = null;
            if (BuiltinScenes.IsBuiltin(name))
            { error = $"既製の利用シーンは消せません: {name}"; return false; }

            EnsureLoaded();
            lock (_lock)
            {
                var s = FindInternal(name);
                if (s == null) { error = $"利用シーンがありません: {name}"; return false; }
                _items.Remove(s);
                WriteFile(_items);
                Revision++;
            }
            return true;
        }

        // ================================================================
        // CSV
        // ================================================================

        /// <summary>一覧の区切り。値の中の ';' は使えない（Clean が落とす）。</summary>
        private const char ListSeparator = ';';

        private static string OneLine(string text) => (text ?? "").Replace("\r", " ").Replace("\n", " ");

        private static List<string> Clean(List<string> list)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (list == null) return result;
            foreach (var raw in list)
            {
                var v = OneLine(raw).Trim();
                if (v.Length == 0 || v.IndexOf(ListSeparator) >= 0) continue;
                if (seen.Add(v)) result.Add(v);
            }
            return result;
        }

        private static string JoinList(List<string> list) => string.Join(";", list);

        private static List<string> SplitList(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (var raw in text.Split(ListSeparator))
            {
                var v = raw.Trim();
                if (v.Length > 0) result.Add(v);
            }
            return result;
        }

        private static string CsvField(string value)
        {
            value = value ?? "";
            if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Length = 0; }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }
}
