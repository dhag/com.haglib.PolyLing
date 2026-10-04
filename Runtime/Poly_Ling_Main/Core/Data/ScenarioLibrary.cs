// ScenarioLibrary.cs
// シナリオのオブジェクトグループ（手順の知識）の置き場。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜモデルの外に置くか】
//   ModelContext.ObjectGroups に入れたものは、モデルと一緒に保存され、
//   Undo で差し替えられ（MeshListRecords）、参照の生存を検査され
//   （ModelInvariantChecker.CheckObjectGroupReferences）、参照先を全て失うと
//   消される（ObjectGroupOps.PurgeMissing）。
//   シナリオは実体を持たないので、同じリストへ入れるとこの 3 か所すべてに
//   「シナリオは除く」という例外が要る。置き場を分ければ例外は 1 つも要らない。
//
// 【なぜプロジェクトの外に置くか】
//   手順の知識はプロジェクトをまたいで貯まる。ProjectContext.WorkAxes は
//   プロジェクトファイルと同じフォルダに置く形だが、こちらは
//   DisplaySettings / ParameterLimits と同じく persistentDataPath へ置く。
//
// 【型は分けない】
//   中身は ObjectGroup そのもの。実体付きとの違いは置き場と、
//   参照（MeshRefIds / OutputObjectIds）が埋まっているかどうかだけ。
//   型を分けると項目列の器が 2 つになり、ObjectGroupOps.CaptureStep の
//   出力先も 2 つになる。
//
// 【原本を渡さない】
//   Get は複製を返す。呼ぶ側が引数を書き換えても原本は変わらない。
//   書き戻したいときは Register で明示的に登録する。
//
// 【保存先】
//   <persistentDataPath>/PolyLing/scenarios/<フォルダ>/<名前>.csv
//   1 シナリオ 1 ファイル。フォルダは何階層でもよく、置き場所（整理）だけを表す。
//   どのフォルダにあるかは、読んだファイルの場所で決まる（ObjectGroup には持たせない）。
//   名前は全フォルダを通して一意。親は名前で子を引くので、フォルダを移しても参照は切れない。
//   形式は objectgroups.csv と同じ（ObjectGroupCsv が正典）。
//
// 【フォルダと親子】
//   フォルダ＝置き場所（1 本は 1 か所。順番を持たない）。
//   親子＝使い方（ScenarioRef。順番を持ち、1 本の子を何本の親からでも呼べる）。
//
// 【旧形式からの移行】
//   scenarios フォルダが無く、旧 scenarios.csv があれば、シナリオごとに
//   1 ファイルへ分け、旧ファイルは scenarios.csv.bak へ名前を変えて残す。
//   1 ファイルに複数入っている旧「まとまり」は、ファイル名のフォルダへ 1 本ずつ分け、
//   元のファイルは .bak へ名前を変えて残す。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Serialization;

namespace Poly_Ling.Data
{
    /// <summary>シナリオのオブジェクトグループの置き場。</summary>
    public static class ScenarioLibrary
    {
        private const string LogTag  = "[ScenarioLibrary]";
        private const string Header  = "PolyLing_Scenarios";

        private const string Ext     = ".csv";

        private static List<ObjectGroup> _items;
        /// <summary>シナリオの名前 → 置いているファイル（scenarios からの相対パス。区切りは '/'）。</summary>
        private static Dictionary<string, string> _fileOf;
        /// <summary>ディスク上のフォルダ（空のものも含む。scenarios からの相対。区切りは '/'）。</summary>
        private static HashSet<string> _folders;
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

        /// <summary>今の中身を全部ファイルへ書く。</summary>
        public static void Save()
        {
            EnsureLoaded();
            lock (_lock) { foreach (var g in _items) WriteOne(g); Revision++; }
        }

        /// <summary>_items・_fileOf・_folders をディスクから作り直す。旧形式なら先に移す。</summary>
        private static void ReadAll()
        {
            _items   = new List<ObjectGroup>();
            _fileOf  = new Dictionary<string, string>(StringComparer.Ordinal);
            _folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!Directory.Exists(FolderName) && File.Exists(LegacyFile))
                {
                    MigrateLegacy();
                    return;
                }
                if (!Directory.Exists(FolderName)) return;

                foreach (var dir in Directory.GetDirectories(FolderName, "*", SearchOption.AllDirectories))
                    _folders.Add(ToRel(dir));

                var files = Directory.GetFiles(FolderName, "*" + Ext, SearchOption.AllDirectories);
                Array.Sort(files, StringComparer.Ordinal);

                foreach (var path in files)
                {
                    List<ObjectGroup> list;
                    try { list = ObjectGroupCsv.Parse(File.ReadAllLines(path, Encoding.UTF8)); }
                    catch (Exception e)
                    {
                        Debug.LogError($"{LogTag} 読み込みに失敗しました: {path}: {e.Message}");
                        continue;
                    }
                    if (list == null || list.Count == 0) continue;

                    // 1 ファイルに複数入っているのは旧形式のまとまり。同名のフォルダへ 1 本ずつ分ける。
                    if (list.Count > 1) { SplitBundleFile(path, list); continue; }

                    AddLoaded(list[0], ToRel(path), path);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 読み込みに失敗しました: {e.Message}");
            }
        }

        /// <summary>読んだシナリオを足す。名前が重なると Find がどちらを返すか決まらない。後から来た方を落とす。</summary>
        private static bool AddLoaded(ObjectGroup g, string file, string source)
        {
            if (g == null || string.IsNullOrEmpty(g.Name) || _fileOf.ContainsKey(g.Name))
            {
                Debug.LogWarning($"{LogTag} 名前の無い／重なったシナリオを読み飛ばしました: {source}");
                return false;
            }
            _items.Add(g);
            _fileOf[g.Name] = file;
            AddFolderChain(FolderOfFile(file));
            return true;
        }

        /// <summary>旧形式の「複数入りのファイル」を、拡張子を除いた名前のフォルダへ 1 本ずつ分ける。元は .bak にする。</summary>
        private static void SplitBundleFile(string path, List<ObjectGroup> list)
        {
            string fileRel = ToRel(path);
            string parent  = FolderOfFile(fileRel);
            string folder  = JoinRel(parent, SanitizeSegment(Path.GetFileNameWithoutExtension(path)));

            bool ok = true;
            foreach (var g in list)
            {
                if (g == null || string.IsNullOrEmpty(g.Name) || _fileOf.ContainsKey(g.Name))
                {
                    Debug.LogWarning($"{LogTag} 名前の無い／重なったシナリオを読み飛ばしました: {path}");
                    continue;
                }
                string file = NewFileFor(g.Name, folder);
                _items.Add(g);
                _fileOf[g.Name] = file;
                AddFolderChain(folder);
                if (!WriteOne(g)) ok = false;
            }

            if (!ok)
            {
                Debug.LogError($"{LogTag} 分割の書き込みに失敗したので元のファイルを残します: {path}");
                return;
            }
            string bak = path + ".bak";
            for (int n = 1; File.Exists(bak); n++) bak = path + ".bak" + n;
            File.Move(path, bak);
            Debug.Log($"{LogTag} {fileRel} の {list.Count} 本をフォルダ {folder} へ分けました。元のファイル: {bak}");
        }

        /// <summary>旧 scenarios.csv をシナリオごとのファイルへ分け、旧ファイルは .bak へ名前を変える。</summary>
        private static void MigrateLegacy()
        {
            var list = ObjectGroupCsv.Parse(File.ReadAllLines(LegacyFile, Encoding.UTF8));
            bool ok = true;
            Directory.CreateDirectory(FolderName);
            foreach (var g in list)
            {
                if (g == null || string.IsNullOrEmpty(g.Name)) continue;
                if (!AddLoaded(g, NewFileFor(g.Name, ""), LegacyFile)) continue;
                if (!WriteOne(g)) ok = false;
            }

            if (!ok)
            {
                Debug.LogError($"{LogTag} 移行の書き込みに失敗したので旧ファイルを残します: {LegacyFile}");
                return;
            }

            string bak = LegacyFile + ".bak";
            for (int n = 1; File.Exists(bak); n++) bak = LegacyFile + ".bak" + n;
            File.Move(LegacyFile, bak);
            Debug.Log($"{LogTag} 旧形式から {_items.Count} 本を移行しました。旧ファイル: {bak}");
        }

        /// <summary>1 本を自分のファイルへ書く。</summary>
        private static bool WriteOne(ObjectGroup g)
        {
            if (g == null || !_fileOf.TryGetValue(g.Name, out var file)) return false;
            try
            {
                string path = ToAbs(file);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, ObjectGroupCsv.Build(new List<ObjectGroup> { g }, Header), Encoding.UTF8);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} 保存に失敗しました: {file}: {e.Message}");
                return false;
            }
        }

        /// <summary>シナリオのファイルを消す（移動・削除のとき）。</summary>
        private static void DeleteFile(string file)
        {
            if (string.IsNullOrEmpty(file)) return;
            try
            {
                string path = ToAbs(file);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"{LogTag} ファイルを消せませんでした: {file}: {e.Message}");
            }
        }

        // ================================================================
        // パスとフォルダ名
        // ================================================================

        private static string ToAbs(string rel)
            => string.IsNullOrEmpty(rel) ? FolderName : Path.Combine(FolderName, rel.Replace('/', Path.DirectorySeparatorChar));

        private static string ToRel(string abs)
        {
            string root = Path.GetFullPath(FolderName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(abs);
            string rel  = full.Length > root.Length ? full.Substring(root.Length + 1) : "";
            return rel.Replace('\\', '/');
        }

        private static string JoinRel(string a, string b)
            => string.IsNullOrEmpty(a) ? (b ?? "") : string.IsNullOrEmpty(b) ? a : a + "/" + b;

        /// <summary>ファイルの相対パスからフォルダ部分を返す。直下なら空。</summary>
        private static string FolderOfFile(string file)
        {
            if (string.IsNullOrEmpty(file)) return "";
            int at = file.LastIndexOf('/');
            return at < 0 ? "" : file.Substring(0, at);
        }

        /// <summary>フォルダとその親を全部 _folders へ入れる。</summary>
        private static void AddFolderChain(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            string acc = "";
            foreach (var seg in folder.Split('/'))
            {
                acc = JoinRel(acc, seg);
                _folders.Add(acc);
            }
        }

        /// <summary>フォルダ名・ファイル名の 1 段をファイル名に使える形にする。</summary>
        private static string SanitizeSegment(string s)
        {
            var sb = new StringBuilder((s ?? "").Trim());
            foreach (char c in Path.GetInvalidFileNameChars()) sb.Replace(c, '_');
            string r = sb.ToString().TrimEnd('.', ' ');
            return r.Length == 0 ? "_" : r;
        }

        /// <summary>
        /// フォルダ名を「a/b/c」の形にそろえる。区切りは '/' でも '\' でもよい。
        /// 空の段は捨てる。「.」「..」は受けない。既にあるフォルダと大文字小文字だけ違うときは既存の綴りに合わせる。
        /// 直下は空文字。
        /// </summary>
        private static bool TryCanonicalFolder(string folder, out string canonical, out string error)
        {
            canonical = "";
            error     = null;
            string acc = "";
            foreach (var raw in (folder ?? "").Split('/', '\\'))
            {
                string seg = raw.Trim();
                if (seg.Length == 0) continue;
                if (seg == "." || seg == "..") { error = $"フォルダ名に「{seg}」は使えません: {folder}"; return false; }
                string next = JoinRel(acc, SanitizeSegment(seg));
                foreach (var f in _folders)
                    if (string.Equals(f, next, StringComparison.OrdinalIgnoreCase)) { next = f; break; }
                acc = next;
            }
            canonical = acc;
            return true;
        }

        /// <summary>
        /// 新しく置くファイルの相対パスを決める。名前をファイル名に使える形にし、
        /// ほかのシナリオのファイルと（大文字小文字を無視して）重なるなら番号を付ける。
        /// </summary>
        private static string NewFileFor(string name, string folder)
        {
            string stem = SanitizeSegment(name);
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _fileOf)
                if (!string.Equals(kv.Key, name, StringComparison.Ordinal)) taken.Add(kv.Value);

            string file = JoinRel(folder, stem + Ext);
            for (int n = 2; taken.Contains(file); n++) file = JoinRel(folder, $"{stem}_{n}{Ext}");
            return file;
        }

        /// <summary>フォルダ f が folder 自身か、その下にあるか。folder が空なら全部。</summary>
        private static bool IsUnder(string f, string folder)
            => string.IsNullOrEmpty(folder)
            || string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)
            || (f != null && f.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));

        // ================================================================
        // フォルダ
        // ================================================================

        /// <summary>このシナリオを置いているフォルダ。直下なら空文字。無ければ null。</summary>
        public static string FolderOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            EnsureLoaded();
            return _fileOf.TryGetValue(name, out var f) ? FolderOfFile(f) : null;
        }

        /// <summary>既にあるフォルダを引く。綴りを既存のものにそろえて返す。直下（空）は無いものとして扱う。</summary>
        public static bool TryFindFolder(string folder, out string canonical)
        {
            EnsureLoaded();
            lock (_lock)
            {
                if (!TryCanonicalFolder(folder, out canonical, out _)) return false;
                return canonical.Length > 0 && _folders.Contains(canonical);
            }
        }

        /// <summary>全フォルダ（空のものも含む）。名前順。</summary>
        public static List<string> Folders()
        {
            EnsureLoaded();
            lock (_lock)
            {
                var list = new List<string>(_folders);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        /// <summary>空のフォルダを作る（親も作る）。既にあれば何もしない。</summary>
        public static bool CreateFolder(string folder, out string canonical, out string error)
        {
            EnsureLoaded();
            lock (_lock)
            {
                if (!TryCanonicalFolder(folder, out canonical, out error)) return false;
                if (canonical.Length == 0) { error = "フォルダ名が空です"; return false; }
                try { Directory.CreateDirectory(ToAbs(canonical)); }
                catch (Exception e) { error = $"フォルダを作れませんでした: {e.Message}"; return false; }
                AddFolderChain(canonical);
                Revision++;
            }
            return true;
        }

        /// <summary>
        /// シナリオをフォルダへ移す。folder が空なら直下。無いフォルダは作る。
        /// 1 本でも無い名前があれば何もしない。親は名前で子を引くので、移しても参照は切れない。
        /// </summary>
        public static bool Move(IList<string> names, string folder, out string canonical, out string error)
        {
            canonical = "";
            error = null;
            if (names == null || names.Count == 0) { error = "シナリオの名前がありません"; return false; }

            EnsureLoaded();
            lock (_lock)
            {
                foreach (var n in names)
                    if (string.IsNullOrEmpty(n) || !_fileOf.ContainsKey(n)) { error = $"シナリオがありません: {n}"; return false; }
                if (!TryCanonicalFolder(folder, out canonical, out error)) return false;

                foreach (var n in names)
                {
                    string oldFile = _fileOf[n];
                    if (string.Equals(FolderOfFile(oldFile), canonical, StringComparison.Ordinal)) continue;
                    _fileOf[n] = NewFileFor(n, canonical);
                    if (!WriteOne(FindInternal(n))) { _fileOf[n] = oldFile; error = $"書き込めませんでした: {n}"; return false; }
                    DeleteFile(oldFile);
                }
                AddFolderChain(canonical);
                if (canonical.Length > 0) Directory.CreateDirectory(ToAbs(canonical));
                Revision++;
            }
            return true;
        }

        /// <summary>
        /// フォルダの名前を変える（中のシナリオとフォルダごと）。「a/b」→「c/b」のように別の親の下へも移せる。
        /// 行き先が既にあるとき・自分の下へ移すときは失敗する。
        /// </summary>
        public static bool RenameFolder(string folder, string newFolder, out string canonical, out string error)
        {
            canonical = "";
            error = null;

            EnsureLoaded();
            lock (_lock)
            {
                if (!TryCanonicalFolder(folder, out var src, out error)) return false;
                if (src.Length == 0 || !_folders.Contains(src)) { error = $"フォルダがありません: {folder}"; return false; }
                if (!TryCanonicalFolder(newFolder, out canonical, out error)) return false;
                if (canonical.Length == 0) { error = "新しいフォルダ名が空です"; return false; }
                if (string.Equals(src, canonical, StringComparison.OrdinalIgnoreCase)) { error = "名前が変わっていません"; return false; }
                if (IsUnder(canonical, src)) { error = $"自分の下へは移せません: {src} → {canonical}"; return false; }
                if (_folders.Contains(canonical)) { error = $"フォルダが既にあります: {canonical}"; return false; }

                string dst = canonical;
                string Rebase(string f) => dst + f.Substring(src.Length);

                // 中のシナリオを新しい場所へ書いてから古いファイルを消す。
                var names = new List<string>();
                foreach (var kv in _fileOf)
                    if (IsUnder(FolderOfFile(kv.Value), src)) names.Add(kv.Key);
                foreach (var n in names)
                {
                    string oldFile = _fileOf[n];
                    _fileOf[n] = NewFileFor(n, Rebase(FolderOfFile(oldFile)));
                    if (!WriteOne(FindInternal(n))) { _fileOf[n] = oldFile; error = $"書き込めませんでした: {n}"; return false; }
                    DeleteFile(oldFile);
                }

                // 空のフォルダも移す。古いフォルダは深い方から、空なら消す。
                var oldFolders = new List<string>();
                foreach (var f in _folders) if (IsUnder(f, src)) oldFolders.Add(f);
                foreach (var f in oldFolders)
                {
                    string nf = Rebase(f);
                    Directory.CreateDirectory(ToAbs(nf));
                    AddFolderChain(nf);
                }
                oldFolders.Sort((a, b) => b.Length.CompareTo(a.Length));
                foreach (var f in oldFolders)
                {
                    string abs = ToAbs(f);
                    try
                    {
                        if (Directory.Exists(abs) && Directory.GetFileSystemEntries(abs).Length == 0) Directory.Delete(abs);
                    }
                    catch (Exception e) { Debug.LogWarning($"{LogTag} 古いフォルダを消せませんでした: {f}: {e.Message}"); }
                    if (!Directory.Exists(abs)) _folders.Remove(f);
                }
                Revision++;
            }
            return true;
        }

        /// <summary>
        /// 空のフォルダを消す（下の空のフォルダも）。シナリオが入っているとき、
        /// シナリオ以外のファイル（.bak など）が入っているときは消さない。
        /// </summary>
        public static bool DeleteFolder(string folder, out string error)
        {
            error = null;
            EnsureLoaded();
            lock (_lock)
            {
                if (!TryCanonicalFolder(folder, out var src, out error)) return false;
                if (src.Length == 0 || !_folders.Contains(src)) { error = $"フォルダがありません: {folder}"; return false; }

                int inside = 0;
                foreach (var kv in _fileOf) if (IsUnder(FolderOfFile(kv.Value), src)) inside++;
                if (inside > 0) { error = $"{src} にはシナリオが {inside} 本入っているので消せません（先に移すか消す）"; return false; }

                string abs = ToAbs(src);
                if (Directory.Exists(abs))
                {
                    var files = Directory.GetFiles(abs, "*", SearchOption.AllDirectories);
                    if (files.Length > 0) { error = $"{src} にはシナリオ以外のファイルがあるので消しません: {ToRel(files[0])}"; return false; }
                    try { Directory.Delete(abs, true); }
                    catch (Exception e) { error = $"フォルダを消せませんでした: {e.Message}"; return false; }
                }

                var gone = new List<string>();
                foreach (var f in _folders) if (IsUnder(f, src)) gone.Add(f);
                foreach (var f in gone) _folders.Remove(f);
                Revision++;
            }
            return true;
        }

        // ================================================================
        // 参照
        // ================================================================

        /// <summary>登録されているシナリオの数。</summary>
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

        /// <summary>この名前のシナリオがあるか。</summary>
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
        // 参照項目
        // ================================================================

        /// <summary>参照を辿る深さの上限。これを超えたら組み方を疑う。</summary>
        public const int MaxRefDepth = 8;

        /// <summary>平たくした項目 1 つ。どのシナリオの何番目から来たかを添える。</summary>
        public sealed class FlatStep
        {
            /// <summary>この項目が載っているシナリオの名前。</summary>
            public string ScenarioName;

            /// <summary>参照の深さ。0 = 起点のシナリオ。</summary>
            public int Depth;

            /// <summary>項目そのもの（複製）。</summary>
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
        /// 参照項目を辿って平たい項目の列にする。参照項目そのものは列に入れず、
        /// 参照先の項目に置き換える。
        /// </summary>
        public static bool TryFlatten(string name, out List<FlatStep> steps, out string error)
        {
            steps = new List<FlatStep>();
            error = null;

            EnsureLoaded();

            var root = FindInternal(name);
            if (root == null) { error = $"シナリオがありません: {name}"; return false; }

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
            { error = $"参照先のシナリオがありません: {name}"; return false; }

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
        /// シナリオを登録する。渡されたものの複製を入れるので、呼ぶ側が
        /// あとで書き換えても登録済みの中身は変わらない。
        /// 登録できたらファイルへ書く。
        /// </summary>
        /// <param name="overwrite">同名があるとき差し替えるか。false なら失敗。</param>
        /// <param name="folder">置くフォルダ。null なら、既存のシナリオは今の場所のまま、新しいシナリオは直下。空文字は直下。</param>
        public static bool Register(ObjectGroup group, bool overwrite, out string error, string folder = null)
        {
            error = null;

            if (group == null)                     { error = "シナリオがありません";       return false; }
            if (string.IsNullOrEmpty(group.Name))  { error = "シナリオの名前が空です";     return false; }

            // ObjectGroup.IsValid は「項目が 1 つ以上ある」ことも要求するが、
            // シナリオは項目 0 個から組み始める。ここでは各項目の中身だけを見る。
            if (group.Steps != null)
            {
                for (int i = 0; i < group.Steps.Count; i++)
                {
                    var step = group.Steps[i];
                    if (step == null)  { error = $"項目 {i} がありません"; return false; }
                    if (!step.IsValid) { error = $"項目 {i}（{step.ScenarioItemId}）は実行する項目なのに action が空です"; return false; }
                }
            }

            EnsureLoaded();
            lock (_lock)
            {
                var copy = group.Clone();
                copy.EnsureScenarioItemIds();

                int at = -1;
                for (int i = 0; i < _items.Count; i++)
                    if (string.Equals(_items[i].Name, copy.Name, StringComparison.Ordinal)) { at = i; break; }

                if (at >= 0 && !overwrite)
                { error = $"同じ名前のシナリオが既にあります: {copy.Name}"; return false; }

                // 参照先の不在と循環は、入れる前に見る。入れてしまうと
                // TryFlatten が回らなくなり、直す口も参照で詰まる。
                var probe = new List<ObjectGroup>(_items);
                if (at >= 0) probe[at] = copy;
                else         probe.Add(copy);

                var drain = new List<FlatStep>();
                if (!Walk(probe, copy.Name, 0, new List<string>(), drain, out error)) return false;

                string target = null;
                if (folder != null && !TryCanonicalFolder(folder, out target, out error)) return false;

                _fileOf.TryGetValue(copy.Name, out var oldFile);
                string file = (oldFile != null && (target == null || string.Equals(FolderOfFile(oldFile), target, StringComparison.Ordinal)))
                            ? oldFile
                            : NewFileFor(copy.Name, target ?? "");

                if (at >= 0) _items[at] = copy;
                else         _items.Add(copy);

                _fileOf[copy.Name] = file;
                AddFolderChain(FolderOfFile(file));
                if (!WriteOne(copy)) { error = $"書き込めませんでした: {copy.Name}"; return false; }
                if (oldFile != null && !string.Equals(oldFile, file, StringComparison.Ordinal)) DeleteFile(oldFile);
            }
            return true;
        }

        /// <summary>
        /// シナリオを消す。消したらファイルへ書く。
        /// 他のシナリオから参照されているものは消さない。消すと参照が宙に浮き、
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
                    _fileOf.TryGetValue(name, out var oldFile);
                    _fileOf.Remove(name);
                    DeleteFile(oldFile);
                    return true;
                }
            }

            error = $"シナリオがありません: {name}";
            return false;
        }

        /// <summary>このシナリオを参照しているシナリオの名前。無ければ null。</summary>
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
        // 形式は置き場のファイルと同じ（ObjectGroupCsv が正典）。
        // 書き出しは参照先のシナリオも一緒に入れる。そのファイルだけで参照が切れずに取り込めるように。
        // 取り込みはファイルのシナリオをまとめて検査し、1 本でも問題があれば何も登録しない。

        /// <summary>
        /// 指定のシナリオと、それが参照しているシナリオを 1 本の CSV 文字列にする。
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
                        if (FindIn(_items, n) == null) { error = $"シナリオがありません: {n}"; return false; }
                        stack.Push(n);
                    }
                    while (stack.Count > 0)
                    {
                        string n = stack.Pop();
                        if (!want.Add(n)) continue;
                        var g = FindIn(_items, n);
                        if (g == null) { error = $"参照先のシナリオがありません: {n}"; return false; }
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
                if (list.Count == 0) { error = "書き出すシナリオがありません"; return false; }

                csv = ObjectGroupCsv.Build(list, Header);
            }
            return true;
        }

        /// <summary>
        /// CSV の行からシナリオを取り込む。まとめて検査し、問題があれば何も登録しない。
        /// 同じ名前のシナリオは overwrite のときだけ差し替える。
        /// </summary>
        /// <param name="folder">新しく入るシナリオを置くフォルダ。null/空なら直下。差し替えるシナリオは今の場所のまま。</param>
        public static bool TryImport(IEnumerable<string> lines, bool overwrite,
                                     out List<string> added, out List<string> replaced, out string error,
                                     string folder = null)
        {
            added = new List<string>();
            replaced = new List<string>();
            error = null;

            List<ObjectGroup> incoming;
            try { incoming = ObjectGroupCsv.Parse(lines); }
            catch (Exception e) { error = $"シナリオとして読めません: {e.Message}"; return false; }
            if (incoming == null || incoming.Count == 0) { error = "シナリオが入っていません"; return false; }

            // ファイルの中での名前の重なりと、各項目の中身を見る。
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in incoming)
            {
                if (g == null || string.IsNullOrEmpty(g.Name)) { error = "名前の無いシナリオがあります"; return false; }
                if (!names.Add(g.Name)) { error = $"ファイルの中で名前が重なっています: {g.Name}"; return false; }
                if (g.Steps == null) continue;
                for (int i = 0; i < g.Steps.Count; i++)
                {
                    var step = g.Steps[i];
                    if (step == null)  { error = $"{g.Name} の項目 {i} がありません"; return false; }
                    if (!step.IsValid) { error = $"{g.Name} の項目 {i}（{step.ScenarioItemId}）は実行する項目なのに action が空です"; return false; }
                }
            }

            EnsureLoaded();
            lock (_lock)
            {
                if (!TryCanonicalFolder(folder, out var target, out error)) return false;

                // 既存との重なり。
                var clash = new List<string>();
                foreach (var g in incoming)
                    if (FindIn(_items, g.Name) != null) clash.Add(g.Name);
                if (clash.Count > 0 && !overwrite)
                {
                    error = $"同じ名前のシナリオが既にあります（差し替えるなら overwrite）: {string.Join(", ", clash)}";
                    return false;
                }

                // 取り込んだ後の姿を作って、全部の参照が辿れるかを見る。
                var probe = new List<ObjectGroup>(_items);
                var copies = new List<ObjectGroup>();
                foreach (var g in incoming)
                {
                    var copy = g.Clone();
                    copy.EnsureScenarioItemIds();
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

                // 既存のシナリオは今の場所のまま、新しいシナリオは指定のフォルダへ。
                foreach (var copy in copies)
                {
                    if (!_fileOf.ContainsKey(copy.Name)) _fileOf[copy.Name] = NewFileFor(copy.Name, target);
                    AddFolderChain(FolderOfFile(_fileOf[copy.Name]));
                    WriteOne(copy);
                }
                Revision++;
            }
            return true;
        }
    }
}
