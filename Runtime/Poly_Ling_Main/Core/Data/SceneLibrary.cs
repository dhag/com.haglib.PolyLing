// SceneLibrary.cs
// 利用シーンの定義の置き場。MCP の道具と PolyLing のコマンドを、作業の場面ごとに絞るために使う。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（ScenarioLibrary と同じ場所）
//
// 【何を持つか】
//   シーン 1 つは次を持つ。
//     name        … 名前（大小を区別しない一意）
//     description … 説明
//     commands    … 対象にするコマンド名（polyling_search の結果に出るもの）
//     categories  … 対象にする分類（PLCommand.Category と照合）
//     tags        … 対象にするタグ（PLCommand.Tags のどれかと一致すれば対象）
//     tools       … MCP の固定の道具のうち見せるもの（サーバが tools/list で使う）
//
// 【対象の決まり方】
//   コマンドは commands / categories / tags のどれかに当たれば対象。
//   3 つとも空なら全コマンドが対象（道具だけを絞るシーンに使う）。
//   tools が空なら固定の道具は絞らない。
//   属性の Category / Tags はコードに書いた既定値で、シーンはそれを束ねるだけ。
//   属性を書き換えずに、場面ごとの束ね方を実行中に変えられる。
//
// 【保存】
//   ScenarioLibrary と同じく persistentDataPath/PolyLing/ に置く（scenes.csv）。
//   1 行 1 シーン。列は name,description,commands,categories,tags,tools。
//   一覧の列は ';' 区切り。値にカンマ・引用符があれば "…" で囲み、" は "" にする。
//   改行は保存時に空白へ置き換える（1 行 1 シーンを崩さないため）。
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
    /// <summary>利用シーン 1 つの定義。</summary>
    public sealed class SceneDefinition
    {
        public string Name        = "";
        public string Description = "";
        public List<string> Commands   = new List<string>();
        public List<string> Categories = new List<string>();
        public List<string> Tags       = new List<string>();
        public List<string> Tools      = new List<string>();

        /// <summary>commands / categories / tags がすべて空か（= 全コマンドが対象）。</summary>
        public bool CoversAllCommands => Commands.Count == 0 && Categories.Count == 0 && Tags.Count == 0;

        public SceneDefinition Clone() => new SceneDefinition
        {
            Name        = Name,
            Description = Description,
            Commands    = new List<string>(Commands),
            Categories  = new List<string>(Categories),
            Tags        = new List<string>(Tags),
            Tools       = new List<string>(Tools),
        };

        /// <summary>
        /// コマンドがこのシーンの対象か。
        /// </summary>
        /// <param name="commandName">道具名（PanelCommandFactory.ActionOf）</param>
        /// <param name="category">PLCommand.Category</param>
        /// <param name="tagsCsv">PLCommand.Tags（カンマ区切り）</param>
        public bool Covers(string commandName, string category, string tagsCsv)
        {
            if (CoversAllCommands) return true;

            if (ContainsIgnoreCase(Commands, commandName)) return true;
            if (!string.IsNullOrEmpty(category) && ContainsIgnoreCase(Categories, category)) return true;

            if (Tags.Count > 0 && !string.IsNullOrEmpty(tagsCsv))
            {
                foreach (var raw in tagsCsv.Split(','))
                {
                    var t = raw.Trim();
                    if (t.Length > 0 && ContainsIgnoreCase(Tags, t)) return true;
                }
            }
            return false;
        }

        private static bool ContainsIgnoreCase(List<string> list, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (var s in list)
                if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    /// <summary>利用シーンの定義の置き場。</summary>
    public static class SceneLibrary
    {
        private const string LogTag = "[SceneLibrary]";
        private const string Header = "PolyLing_Scenes,v1";
        private const string Columns = "name,description,commands,categories,tags,tools";

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
                foreach (var line in File.ReadAllLines(FileName, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("PolyLing_Scenes", StringComparison.Ordinal)) continue;
                    if (line == Columns) continue;

                    var f = SplitCsvLine(line);
                    while (f.Count < 6) f.Add("");

                    var s = new SceneDefinition
                    {
                        Name        = f[0].Trim(),
                        Description = f[1],
                        Commands    = SplitList(f[2]),
                        Categories  = SplitList(f[3]),
                        Tags        = SplitList(f[4]),
                        Tools       = SplitList(f[5]),
                    };

                    // 名前が無い・重なる行は読み飛ばす（Get がどちらを返すか決まらなくなる）。
                    if (s.Name.Length == 0 || !seen.Add(s.Name))
                    {
                        Debug.LogWarning($"{LogTag} 名前の無い／重なったシーンを読み飛ばしました: {s.Name}");
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

        private static void WriteFile(List<SceneDefinition> list)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.Append(Header).Append('\n');
                sb.Append(Columns).Append('\n');
                foreach (var s in list)
                {
                    sb.Append(CsvField(s.Name)).Append(',')
                      .Append(CsvField(s.Description)).Append(',')
                      .Append(CsvField(JoinList(s.Commands))).Append(',')
                      .Append(CsvField(JoinList(s.Categories))).Append(',')
                      .Append(CsvField(JoinList(s.Tags))).Append(',')
                      .Append(CsvField(JoinList(s.Tools))).Append('\n');
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

        public static int Count
        {
            get { EnsureLoaded(); lock (_lock) { return _items.Count; } }
        }

        /// <summary>全シーンの写し（登録順）。</summary>
        public static List<SceneDefinition> GetAll()
        {
            EnsureLoaded();
            lock (_lock)
            {
                var list = new List<SceneDefinition>(_items.Count);
                foreach (var s in _items) list.Add(s.Clone());
                return list;
            }
        }

        /// <summary>名前で引いた写し。無ければ null。大小を区別しない。</summary>
        public static SceneDefinition Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
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
            if (scene == null) { error = "シーンが null"; return false; }

            string name = (scene.Name ?? "").Trim();
            if (name.Length == 0) { error = "シーンの名前が空です"; return false; }

            var copy = scene.Clone();
            copy.Name        = name;
            copy.Description = (copy.Description ?? "").Replace("\r", " ").Replace("\n", " ");
            copy.Commands    = Clean(copy.Commands);
            copy.Categories  = Clean(copy.Categories);
            copy.Tags        = Clean(copy.Tags);
            copy.Tools       = Clean(copy.Tools);

            EnsureLoaded();
            lock (_lock)
            {
                var existing = FindInternal(name);
                if (existing != null)
                {
                    if (!overwrite) { error = $"同じ名前のシーンがあります: {name}（overwrite を立てると差し替える）"; return false; }
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
            EnsureLoaded();
            lock (_lock)
            {
                var s = FindInternal(name);
                if (s == null) { error = $"シーンがありません: {name}"; return false; }
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

        private static List<string> Clean(List<string> list)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (list == null) return result;
            foreach (var raw in list)
            {
                var v = (raw ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
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
