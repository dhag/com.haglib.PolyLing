// ScenarioLibrary.cs
// 手本のオブジェクトグループ（手順の知識）の置き場。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜモデルの外に置くか】
//   ModelContext.ObjectGroups に入れたものは、モデルと一緒に保存され、
//   Undo で差し替えられ（MeshListRecords）、参照の生存を検査され
//   （ModelInvariantChecker.CheckObjectGroupReferences）、参照先を全て失うと
//   消される（ObjectGroupOps.PurgeMissing）。
//   手本は実体を持たないので、同じリストへ入れるとこの 3 か所すべてに
//   「手本は除く」という例外が要る。置き場を分ければ例外は 1 つも要らない。
//
// 【なぜプロジェクトの外に置くか】
//   手順の知識はプロジェクトをまたいで貯まる。ProjectContext.WorkAxes は
//   プロジェクトファイルと同じフォルダに置く形だが、こちらは
//   DisplaySettings / ParameterLimits と同じく persistentDataPath へ置く。
//
// 【型は分けない】
//   中身は ObjectGroup そのもの。実体付きとの違いは置き場と、
//   参照（MeshRefIds / OutputObjectIds）が埋まっているかどうかだけ。
//   型を分けるとステップ列の器が 2 つになり、ObjectGroupOps.CaptureStep の
//   出力先も 2 つになる。
//
// 【原本を渡さない】
//   Get は複製を返す。呼ぶ側が引数を書き換えても原本は変わらない。
//   書き戻したいときは Register で明示的に登録する。
//
// 【保存先】
//   <persistentDataPath>/PolyLing/scenarios/<まとまり名>.csv
//   ひとまとまりの作業（複数の手本）を 1 ファイルに入れる。
//   どのまとまりに属すかは、読んだファイルで決まる（ObjectGroup には持たせない）。
//   名前は全ファイルを通して一意。参照は名前で引くので、まとまりをまたいでよい。
//   形式は objectgroups.csv と同じ（ObjectGroupCsv が正典）。
//
// 【旧形式からの移行】
//   scenarios フォルダが無く、旧 scenarios.csv があれば、手本ごとに
//   1 ファイルへ分け、旧ファイルは scenarios.csv.bak へ名前を変えて残す。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Serialization;

namespace Poly_Ling.Data
{
    /// <summary>手本のオブジェクトグループの置き場。</summary>
    public static class ScenarioLibrary
    {
        private const string LogTag  = "[ScenarioLibrary]";
        private const string Header  = "PolyLing_Scenarios";

        private const string Ext     = ".csv";

        private static List<ObjectGroup> _items;
        /// <summary>手本の名前 → まとまり名（= ファイル名の拡張子抜き）。</summary>
        private static Dictionary<string, string> _bundleOf;
        private static readonly object _lock = new object();

        private static string Dir        => Path.Combine(Application.persistentDataPath, "PolyLing");
        private static string FolderName => Path.Combine(Dir, "scenarios");
        private static string LegacyFile => Path.Combine(Dir, "scenarios.csv");

        /// <summary>保存先フォルダの絶対パス（表示・手動バックアップ用）。</summary>
        public static string StorePath => FolderName;

        /// <summary>
        /// 置き場の版。保存・読み直しで 1 つ進む。
        /// 呼び出し側が「前に見たときから変わったか」を判定するために使う（queryRevisions）。
        /// 保存しない（起動ごとに 0 から数え直す）。
        /// </summary>
        public static int Revision { get; private set; }

        // ================================================================
        // 読み書き
        // ================================================================

        /// <summary>まだ読んでいなければ読む。</summary>
        private static void EnsureLoaded()
        {
            if (_items != null) return;
            lock (_lock)
            {
                if (_items != null) return;
                ReadAll();
            }
        }

        /// <summary>フォルダから読み直す。手で編集したあとに呼ぶ。</summary>
        public static void Reload()
        {
            lock (_lock) { ReadAll(); Revision++; }
        }

        /// <summary>今の中身を全まとまりのファイルへ書く。</summary>
        public static void Save()
        {
            EnsureLoaded();
            lock (_lock) { WriteBundles(new List<string>(_bundleOf.Values)); Revision++; }
        }

        /// <summary>_items と _bundleOf をフォルダから作り直す。旧形式なら先に移す。</summary>
        private static void ReadAll()
        {
            _items    = new List<ObjectGroup>();
            _bundleOf = new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                if (!Directory.Exists(FolderName) && File.Exists(LegacyFile))
                {
                    MigrateLegacy();
                    return;
                }
                if (!Directory.Exists(FolderName)) return;

                var files = Directory.GetFiles(FolderName, "*" + Ext);
                Array.Sort(files, StringComparer.Ordinal);

                foreach (var path in files)
                {
                    string bundle = Path.GetFileNameWithoutExtension(path);
                    List<ObjectGroup> list;
                    try { list = ObjectGroupCsv.Parse(File.ReadAllLines(path, Encoding.UTF8)); }
                    catch (Exception e)
                    {
                        Debug.LogError($"{LogTag} 読み込みに失敗しました: {path}: {e.Message}");
                        continue;
                    }
                    AddLoaded(list, bundle, path);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 読み込みに失敗しました: {e.Message}");
            }
        }

        /// <summary>読んだ手本を足す。名前が重なると Find がどちらを返すか決まらない。後から来た方を落とす。</summary>
        private static void AddLoaded(List<ObjectGroup> list, string bundle, string source)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var g = list[i];
                if (g == null || string.IsNullOrEmpty(g.Name) || _bundleOf.ContainsKey(g.Name))
                {
                    Debug.LogWarning($"{LogTag} 名前の無い／重なった手本を読み飛ばしました: {source} 位置 {i}");
                    continue;
                }
                _items.Add(g);
                _bundleOf[g.Name] = bundle;
            }
        }

        /// <summary>旧 scenarios.csv を手本ごとのファイルへ分け、旧ファイルは .bak へ名前を変える。</summary>
        private static void MigrateLegacy()
        {
            var list = ObjectGroupCsv.Parse(File.ReadAllLines(LegacyFile, Encoding.UTF8));
            foreach (var g in list)
            {
                if (g == null || string.IsNullOrEmpty(g.Name)) continue;
                AddLoaded(new List<ObjectGroup> { g }, CanonicalBundle(g.Name), LegacyFile);
            }

            Directory.CreateDirectory(FolderName);
            if (!WriteBundles(new List<string>(_bundleOf.Values)))
            {
                Debug.LogError($"{LogTag} 移行の書き込みに失敗したので旧ファイルを残します: {LegacyFile}");
                return;
            }

            string bak = LegacyFile + ".bak";
            for (int n = 1; File.Exists(bak); n++) bak = LegacyFile + ".bak" + n;
            File.Move(LegacyFile, bak);
            Debug.Log($"{LogTag} 旧形式から {_items.Count} 本を移行しました。旧ファイル: {bak}");
        }

        /// <summary>まとまりごとにファイルへ書く。手本が 0 本になったまとまりはファイルを消す。</summary>
        private static bool WriteBundles(IEnumerable<string> bundles)
        {
            bool ok = true;
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in bundles)
            {
                if (string.IsNullOrEmpty(bundle) || !done.Add(bundle)) continue;
                try
                {
                    Directory.CreateDirectory(FolderName);
                    string path = Path.Combine(FolderName, bundle + Ext);

                    var list = new List<ObjectGroup>();
                    foreach (var g in _items)
                        if (_bundleOf.TryGetValue(g.Name, out var b)
                            && string.Equals(b, bundle, StringComparison.OrdinalIgnoreCase))
                            list.Add(g);

                    if (list.Count > 0) File.WriteAllText(path, ObjectGroupCsv.Build(list, Header), Encoding.UTF8);
                    else if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception e)
                {
                    ok = false;
                    Debug.LogError($"{LogTag} 保存に失敗しました: {bundle}: {e.Message}");
                }
            }
            return ok;
        }

        /// <summary>
        /// まとまり名をファイル名として使える形にする。
        /// 既存のまとまりと大文字小文字だけ違うときは既存の綴りに合わせる（Windows では同じファイル）。
        /// </summary>
        private static string CanonicalBundle(string bundle)
        {
            var sb = new StringBuilder((bundle ?? "").Trim());
            foreach (char c in Path.GetInvalidFileNameChars()) sb.Replace(c, '_');
            string s = sb.ToString().TrimEnd('.', ' ');
            if (s.Length == 0) s = "_";

            if (_bundleOf != null)
                foreach (var b in _bundleOf.Values)
                    if (string.Equals(b, s, StringComparison.OrdinalIgnoreCase)) return b;
            return s;
        }

        /// <summary>この手本が入っているまとまり名。無ければ null。</summary>
        public static string BundleOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureLoaded();
            return _bundleOf.TryGetValue(name, out var b) ? b : null;
        }

        /// <summary>
        /// 手本をまとまりへ移す。移したら関係するファイルを書く。
        /// 1 本でも無い名前があれば何もしない。
        /// </summary>
        public static bool SetBundle(IList<string> names, string bundle, out string error)
        {
            error = null;
            if (names == null || names.Count == 0) { error = "手本の名前がありません"; return false; }
            if (string.IsNullOrWhiteSpace(bundle)) { error = "まとまり名が空です"; return false; }

            EnsureLoaded();
            lock (_lock)
            {
                foreach (var n in names)
                    if (string.IsNullOrEmpty(n) || !_bundleOf.ContainsKey(n)) { error = $"手本がありません: {n}"; return false; }

                string target = CanonicalBundle(bundle);
                var affected = new List<string> { target };
                foreach (var n in names)
                {
                    affected.Add(_bundleOf[n]);
                    _bundleOf[n] = target;
                }
                WriteBundles(affected);
                Revision++;
            }
            return true;
        }

        // ================================================================
        // 参照
        // ================================================================

        /// <summary>登録されている手本の数。</summary>
        public static int Count
        {
            get { EnsureLoaded(); return _items.Count; }
        }

        /// <summary>登録順の名前。</summary>
        public static List<string> Names()
        {
            EnsureLoaded();
            var names = new List<string>(_items.Count);
            foreach (var g in _items) names.Add(g.Name);
            return names;
        }

        /// <summary>名前で引いて複製を返す。無ければ null。</summary>
        public static ObjectGroup Get(string name)
        {
            var found = FindInternal(name);
            return found?.Clone();
        }

        /// <summary>この名前の手本があるか。</summary>
        public static bool Contains(string name) => FindInternal(name) != null;

        /// <summary>全件の複製を返す。</summary>
        public static List<ObjectGroup> GetAll()
        {
            EnsureLoaded();
            var list = new List<ObjectGroup>(_items.Count);
            foreach (var g in _items) list.Add(g.Clone());
            return list;
        }

        private static ObjectGroup FindInternal(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureLoaded();
            for (int i = 0; i < _items.Count; i++)
                if (string.Equals(_items[i].Name, name, StringComparison.Ordinal)) return _items[i];
            return null;
        }

        // ================================================================
        // 参照段
        // ================================================================

        /// <summary>参照を辿る深さの上限。これを超えたら組み方を疑う。</summary>
        public const int MaxRefDepth = 8;

        /// <summary>平たくした段 1 つ。どの手本の何段目から来たかを添える。</summary>
        public sealed class FlatStep
        {
            /// <summary>この段が載っている手本の名前。</summary>
            public string ScenarioName;

            /// <summary>参照の深さ。0 = 起点の手本。</summary>
            public int Depth;

            /// <summary>段そのもの（複製）。</summary>
            public ObjectGroupStep Step;
        }

        private static ObjectGroup FindIn(List<ObjectGroup> items, string name)
        {
            if (items == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && string.Equals(items[i].Name, name, StringComparison.Ordinal))
                    return items[i];
            return null;
        }

        /// <summary>今たどっている経路に同じ名前があるか。</summary>
        private static bool Contains(List<string> path, string name)
        {
            for (int i = 0; i < path.Count; i++)
                if (string.Equals(path[i], name, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// 参照段を辿って平たい段の列にする。参照段そのものは列に入れず、
        /// 参照先の段に置き換える。
        /// </summary>
        public static bool TryFlatten(string name, out List<FlatStep> steps, out string error)
        {
            steps = new List<FlatStep>();
            error = null;

            EnsureLoaded();

            var root = FindInternal(name);
            if (root == null) { error = $"手本がありません: {name}"; return false; }

            return Walk(_items, name, 0, new List<string>(), steps, out error);
        }

        private static bool Walk(
            List<ObjectGroup> items, string name, int depth,
            List<string> path, List<FlatStep> into, out string error)
        {
            error = null;

            if (depth > MaxRefDepth)
            { error = $"参照が {MaxRefDepth} 段より深くなりました: {string.Join(" → ", path)} → {name}"; return false; }

            if (Contains(path, name))
            { error = $"参照が循環しています: {string.Join(" → ", path)} → {name}"; return false; }

            var g = FindIn(items, name);
            if (g == null)
            { error = $"参照先の手本がありません: {name}"; return false; }

            path.Add(name);
            try
            {
                if (g.Steps == null) return true;

                foreach (var step in g.Steps)
                {
                    if (step == null) continue;

                    if (step.IsScenarioRef)
                    {
                        if (!Walk(items, step.RefName, depth + 1, path, into, out error)) return false;
                        continue;
                    }

                    into.Add(new FlatStep { ScenarioName = name, Depth = depth, Step = step.Clone() });
                }
            }
            finally
            {
                path.RemoveAt(path.Count - 1);
            }

            return true;
        }

        // ================================================================
        // 登録・削除
        // ================================================================

        /// <summary>
        /// 手本を登録する。渡されたものの複製を入れるので、呼ぶ側が
        /// あとで書き換えても登録済みの中身は変わらない。
        /// 登録できたらファイルへ書く。
        /// </summary>
        /// <param name="overwrite">同名があるとき差し替えるか。false なら失敗。</param>
        /// <param name="bundle">入れるまとまり名。null/空なら、既存の手本は今のまとまりのまま、新しい手本は自分の名前のまとまり。</param>
        public static bool Register(ObjectGroup group, bool overwrite, out string error, string bundle = null)
        {
            error = null;

            if (group == null)                     { error = "手本がありません";       return false; }
            if (string.IsNullOrEmpty(group.Name))  { error = "手本の名前が空です";     return false; }

            // ObjectGroup.IsValid は「段が 1 つ以上ある」ことも要求するが、
            // 手本は段 0 本から組み始める。ここでは各段の中身だけを見る。
            if (group.Steps != null)
            {
                for (int i = 0; i < group.Steps.Count; i++)
                {
                    var step = group.Steps[i];
                    if (step == null)  { error = $"段 {i} がありません"; return false; }
                    if (!step.IsValid) { error = $"段 {i}（{step.ElementId}）は実行する段なのに action が空です"; return false; }
                }
            }

            EnsureLoaded();
            lock (_lock)
            {
                var copy = group.Clone();
                copy.EnsureElementIds();

                int at = -1;
                for (int i = 0; i < _items.Count; i++)
                    if (string.Equals(_items[i].Name, copy.Name, StringComparison.Ordinal)) { at = i; break; }

                if (at >= 0 && !overwrite)
                { error = $"同じ名前の手本が既にあります: {copy.Name}"; return false; }

                // 参照先の不在と循環は、入れる前に見る。入れてしまうと
                // TryFlatten が回らなくなり、直す口も参照で詰まる。
                var probe = new List<ObjectGroup>(_items);
                if (at >= 0) probe[at] = copy;
                else         probe.Add(copy);

                var drain = new List<FlatStep>();
                if (!Walk(probe, copy.Name, 0, new List<string>(), drain, out error)) return false;

                if (at >= 0) _items[at] = copy;
                else         _items.Add(copy);

                _bundleOf.TryGetValue(copy.Name, out var oldBundle);
                string target = !string.IsNullOrWhiteSpace(bundle) ? CanonicalBundle(bundle)
                              : oldBundle ?? CanonicalBundle(copy.Name);
                _bundleOf[copy.Name] = target;

                WriteBundles(new List<string> { target, oldBundle });
            }
            return true;
        }

        /// <summary>
        /// 手本を消す。消したらファイルへ書く。
        /// 他の手本から参照されているものは消さない。消すと参照が宙に浮き、
        /// 参照している側を直そうとしても登録の検査で止まる。
        /// </summary>
        /// <param name="error">消さなかった理由。消したときは null。</param>
        public static bool Remove(string name, out string error)
        {
            error = null;

            EnsureLoaded();
            lock (_lock)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (!string.Equals(_items[i].Name, name, StringComparison.Ordinal)) continue;

                    string user = FirstReferrer(name);
                    if (user != null)
                    {
                        error = $"{name} は {user} から参照されているので消せません";
                        return false;
                    }

                    _items.RemoveAt(i);
                    _bundleOf.TryGetValue(name, out var oldBundle);
                    _bundleOf.Remove(name);
                    WriteBundles(new List<string> { oldBundle });
                    return true;
                }
            }

            error = $"手本がありません: {name}";
            return false;
        }

        /// <summary>この手本を参照している手本の名前。無ければ null。</summary>
        private static string FirstReferrer(string name)
        {
            foreach (var g in _items)
            {
                if (g == null || g.Steps == null) continue;
                if (string.Equals(g.Name, name, StringComparison.Ordinal)) continue;

                foreach (var step in g.Steps)
                    if (step != null && step.IsScenarioRef
                        && string.Equals(step.RefName, name, StringComparison.Ordinal))
                        return g.Name;
            }
            return null;
        }

        // ================================================================
        // 書き出し・取り込み（別ファイルとの出し入れ）
        // ================================================================
        //
        // 形式は scenarios.csv と同じ（ObjectGroupCsv が正典）。
        // 書き出しは参照先の手本も一緒に入れる。そのファイルだけで参照が切れずに取り込めるように。
        // 取り込みはファイルの手本をまとめて検査し、1 本でも問題があれば何も登録しない。

        /// <summary>
        /// 指定の手本と、それが参照している手本を 1 本の CSV 文字列にする。
        /// names が空なら全部。並びは登録順。
        /// </summary>
        public static bool TryExport(IList<string> names, out string csv, out List<string> exported, out string error)
        {
            csv = null;
            exported = new List<string>();
            error = null;

            EnsureLoaded();
            lock (_lock)
            {
                var want = new HashSet<string>(StringComparer.Ordinal);
                if (names == null || names.Count == 0)
                {
                    foreach (var g in _items) want.Add(g.Name);
                }
                else
                {
                    var stack = new Stack<string>();
                    foreach (var n in names)
                    {
                        if (string.IsNullOrEmpty(n)) continue;
                        if (FindIn(_items, n) == null) { error = $"手本がありません: {n}"; return false; }
                        stack.Push(n);
                    }
                    while (stack.Count > 0)
                    {
                        string n = stack.Pop();
                        if (!want.Add(n)) continue;
                        var g = FindIn(_items, n);
                        if (g == null) { error = $"参照先の手本がありません: {n}"; return false; }
                        if (g.Steps == null) continue;
                        foreach (var step in g.Steps)
                            if (step != null && step.IsScenarioRef && !string.IsNullOrEmpty(step.RefName))
                                stack.Push(step.RefName);
                    }
                }

                var list = new List<ObjectGroup>();
                foreach (var g in _items)
                {
                    if (!want.Contains(g.Name)) continue;
                    list.Add(g);
                    exported.Add(g.Name);
                }
                if (list.Count == 0) { error = "書き出す手本がありません"; return false; }

                csv = ObjectGroupCsv.Build(list, Header);
            }
            return true;
        }

        /// <summary>
        /// CSV の行から手本を取り込む。まとめて検査し、問題があれば何も登録しない。
        /// 同じ名前の手本は overwrite のときだけ差し替える。
        /// </summary>
        public static bool TryImport(IEnumerable<string> lines, bool overwrite,
                                     out List<string> added, out List<string> replaced, out string error)
        {
            added = new List<string>();
            replaced = new List<string>();
            error = null;

            List<ObjectGroup> incoming;
            try { incoming = ObjectGroupCsv.Parse(lines); }
            catch (Exception e) { error = $"手本として読めません: {e.Message}"; return false; }
            if (incoming == null || incoming.Count == 0) { error = "手本が入っていません"; return false; }

            // ファイルの中での名前の重なりと、各段の中身を見る。
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in incoming)
            {
                if (g == null || string.IsNullOrEmpty(g.Name)) { error = "名前の無い手本があります"; return false; }
                if (!names.Add(g.Name)) { error = $"ファイルの中で名前が重なっています: {g.Name}"; return false; }
                if (g.Steps == null) continue;
                for (int i = 0; i < g.Steps.Count; i++)
                {
                    var step = g.Steps[i];
                    if (step == null)  { error = $"{g.Name} の段 {i} がありません"; return false; }
                    if (!step.IsValid) { error = $"{g.Name} の段 {i}（{step.ElementId}）は実行する段なのに action が空です"; return false; }
                }
            }

            EnsureLoaded();
            lock (_lock)
            {
                // 既存との重なり。
                var clash = new List<string>();
                foreach (var g in incoming)
                    if (FindIn(_items, g.Name) != null) clash.Add(g.Name);
                if (clash.Count > 0 && !overwrite)
                {
                    error = $"同じ名前の手本が既にあります（差し替えるなら overwrite）: {string.Join(", ", clash)}";
                    return false;
                }

                // 取り込んだ後の姿を作って、全部の参照が辿れるかを見る。
                var probe = new List<ObjectGroup>(_items);
                var copies = new List<ObjectGroup>();
                foreach (var g in incoming)
                {
                    var copy = g.Clone();
                    copy.EnsureElementIds();
                    copies.Add(copy);

                    int at = -1;
                    for (int i = 0; i < probe.Count; i++)
                        if (string.Equals(probe[i].Name, copy.Name, StringComparison.Ordinal)) { at = i; break; }
                    if (at >= 0) probe[at] = copy;
                    else         probe.Add(copy);
                }
                foreach (var copy in copies)
                {
                    var drain = new List<FlatStep>();
                    if (!Walk(probe, copy.Name, 0, new List<string>(), drain, out error))
                    {
                        error = $"{copy.Name}: {error}";
                        return false;
                    }
                }

                foreach (var copy in copies)
                {
                    if (FindIn(_items, copy.Name) != null) replaced.Add(copy.Name);
                    else                                   added.Add(copy.Name);
                }
                _items = probe;

                // 既存の手本は今のまとまりのまま、新しい手本は自分の名前のまとまりへ。
                var affected = new List<string>();
                foreach (var copy in copies)
                {
                    if (!_bundleOf.TryGetValue(copy.Name, out var b))
                    {
                        b = CanonicalBundle(copy.Name);
                        _bundleOf[copy.Name] = b;
                    }
                    affected.Add(b);
                }
                WriteBundles(affected);
                Revision++;
            }
            return true;
        }
    }
}
