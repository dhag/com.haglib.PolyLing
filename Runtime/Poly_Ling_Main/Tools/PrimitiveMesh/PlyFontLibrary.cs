// PlyFontLibrary.cs
// フォント置き場の fonts.txt を読み、ファミリ名から .plgly を開く。
// 開いたファイルはファミリ名でキャッシュする。
//
// 置き場は複数のフォルダを指定できる。既定は
// <persistentDataPath>/PolyLing/Fonts/ の 1 件で、従来と同じ。
// 一覧は RecentPaths（<persistentDataPath>/PolyLing/RecentPaths.csv）へ保存する。
//
// フォルダごとの読み方は次の 1 つの規則で決める。
//   fonts.txt がある  → その一覧を使う（従来どおり）
//   fonts.txt が無い  → *.plgly を列挙し、ヘッダの FamilyName を使う
//                       （PlyGlyphFile.Open はヘッダと索引だけ読む）
//
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Core;

namespace Poly_Ling.GlyphText
{
    public static class PlyFontLibrary
    {
        public const string FontListName = "fonts.txt";

        /// <summary>グリフファイルの拡張子。</summary>
        public const string GlyphExtension = ".plgly";

        /// <summary>置き場フォルダ数の上限。RecentPaths を無制限に増やさないための歯止め。</summary>
        public const int MaxDirs = 16;

        private const string DirCountKey  = "Primitive.Text.FontDirCount";
        private const string DirKeyPrefix = "Primitive.Text.FontDir.";

        /// <summary>既定のフォント置き場。保存先の規約は他の設定ファイルと同じ。</summary>
        public static string DefaultRootDir =>
            Path.Combine(Application.persistentDataPath, "PolyLing", "Fonts");

        /// <summary>既定のフォント置き場（従来名）。</summary>
        public static string RootDir => DefaultRootDir;

        /// <summary>fonts.txt の 1 行、または走査で見つけた .plgly 1 個。</summary>
        public sealed class Entry
        {
            /// <summary>このフォントが置かれているフォルダ。</summary>
            public string Dir;
            public string FileName;
            public string FamilyName;
        }

        private static List<string> _dirs;
        private static List<Entry> _entries;
        private static readonly Dictionary<string, PlyGlyphFile> _opened =
            new Dictionary<string, PlyGlyphFile>(StringComparer.Ordinal);

        // ================================================================
        // 置き場フォルダ
        // ================================================================

        /// <summary>置き場フォルダ一覧を取得する。編集用にコピーを返す。</summary>
        public static List<string> GetDirs() => new List<string>(EnsureDirs());

        /// <summary>
        /// 置き場フォルダ一覧を差し替えて保存する。
        /// 空文字と重複は落とす。結果が 0 件になったら既定フォルダ 1 件へ戻す。
        /// </summary>
        public static void SetDirs(IEnumerable<string> dirs)
        {
            var list = Normalize(dirs);
            SaveDirs(list);
            _dirs = list;
            Clear();
        }

        /// <summary>置き場フォルダを既定の 1 件へ戻す。</summary>
        public static void ResetDirs() => SetDirs(null);

        /// <summary>空文字・重複を落とし、上限で切る。0 件なら既定フォルダを入れる。</summary>
        private static List<string> Normalize(IEnumerable<string> dirs)
        {
            var list = new List<string>();
            if (dirs != null)
            {
                foreach (var d in dirs)
                {
                    if (string.IsNullOrWhiteSpace(d)) continue;

                    string t = d.Trim();
                    if (Contains(list, t)) continue;

                    list.Add(t);
                    if (list.Count >= MaxDirs) break;
                }
            }
            if (list.Count == 0) list.Add(DefaultRootDir);
            return list;
        }

        /// <summary>
        /// 同じフォルダが既にあるか。Windows のパスは大文字小文字を区別しないため
        /// OrdinalIgnoreCase で比べる。
        /// </summary>
        private static bool Contains(List<string> list, string dir)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], dir, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static List<string> EnsureDirs()
        {
            if (_dirs != null) return _dirs;
            _dirs = LoadDirs();
            return _dirs;
        }

        private static List<string> LoadDirs()
        {
            int count = ParseCount(RecentPaths.Get(DirCountKey, ""));

            var raw = new List<string>();
            for (int i = 0; i < count; i++)
                raw.Add(RecentPaths.Get(DirKeyPrefix + i.ToString(CultureInfo.InvariantCulture), ""));

            return Normalize(raw);
        }

        private static void SaveDirs(List<string> dirs)
        {
            // 件数が減ったときに古いキーが残らないよう、前回件数まで消してから書く。
            int oldCount = ParseCount(RecentPaths.Get(DirCountKey, ""));
            int n        = dirs.Count;

            for (int i = 0; i < n; i++)
                RecentPaths.Set(DirKeyPrefix + i.ToString(CultureInfo.InvariantCulture), dirs[i]);

            for (int i = n; i < oldCount; i++)
                RecentPaths.Set(DirKeyPrefix + i.ToString(CultureInfo.InvariantCulture), "");

            RecentPaths.Set(DirCountKey, n.ToString(CultureInfo.InvariantCulture));
        }

        private static int ParseCount(string text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                return 0;
            if (n < 0) return 0;
            if (n > MaxDirs) return MaxDirs;
            return n;
        }

        // ================================================================
        // 一覧
        // ================================================================

        /// <summary>全ての置き場からフォント一覧を作る。1 件も無ければ空リスト。</summary>
        public static List<Entry> LoadList()
        {
            if (_entries != null) return _entries;

            var list = new List<Entry>();
            // ファミリ名は先勝ち。先に指定したフォルダを優先する。
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var dirs = EnsureDirs();
            for (int i = 0; i < dirs.Count; i++)
            {
                string dir = dirs[i];
                if (string.IsNullOrEmpty(dir)) continue;

                bool exists;
                try { exists = Directory.Exists(dir); }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PlyFontLibrary] フォルダを確認できませんでした: {dir} : {ex.Message}");
                    continue;
                }
                if (!exists) continue;

                string listPath = Path.Combine(dir, FontListName);
                if (File.Exists(listPath)) AppendFromList(list, seen, dir, listPath);
                else                       AppendFromScan(list, seen, dir);
            }

            _entries = list;
            return list;
        }

        /// <summary>fonts.txt から取り込む。1 行 = ファイル名 TAB ファミリ名。</summary>
        private static void AppendFromList(
            List<Entry> list, HashSet<string> seen, string dir, string listPath)
        {
            try
            {
                string all = File.ReadAllText(listPath, new UTF8Encoding(false));
                string[] lines = all.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    if (line.Length == 0) continue;

                    int tab = line.IndexOf('\t');
                    if (tab <= 0 || tab >= line.Length - 1) continue;

                    string fileName   = line.Substring(0, tab).Trim();
                    string familyName = line.Substring(tab + 1).Trim();
                    if (fileName.Length == 0 || familyName.Length == 0) continue;

                    if (!seen.Add(familyName)) continue;

                    list.Add(new Entry
                    {
                        Dir        = dir,
                        FileName   = fileName,
                        FamilyName = familyName,
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlyFontLibrary] {FontListName} の読み込みに失敗しました: {listPath} : {ex.Message}");
            }
        }

        /// <summary>
        /// fonts.txt が無いフォルダは .plgly を列挙し、ヘッダの FamilyName を使う。
        /// PlyGlyphFile.Open はヘッダと索引だけ読むので、グリフ本体は読まない。
        /// 開いたファイルはそのままキャッシュへ入れ、Open() で開き直さない。
        /// </summary>
        private static void AppendFromScan(List<Entry> list, HashSet<string> seen, string dir)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*" + GlyphExtension, SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlyFontLibrary] フォルダを列挙できませんでした: {dir} : {ex.Message}");
                return;
            }

            // 列挙順を環境に依存させない。
            Array.Sort(files, StringComparer.Ordinal);

            for (int i = 0; i < files.Length; i++)
            {
                var f = PlyGlyphFile.Open(files[i]);
                if (f == null) continue;

                // ヘッダにファミリ名が無いファイルはファイル名で代用する。
                string familyName = string.IsNullOrEmpty(f.FamilyName)
                    ? Path.GetFileNameWithoutExtension(files[i])
                    : f.FamilyName;
                if (familyName.Length == 0) continue;

                if (!seen.Add(familyName)) continue;

                list.Add(new Entry
                {
                    Dir        = dir,
                    FileName   = Path.GetFileName(files[i]),
                    FamilyName = familyName,
                });
                _opened[familyName] = f;
            }
        }

        /// <summary>ファミリ名から .plgly を開く。見つからなければ null。</summary>
        public static PlyGlyphFile Open(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;

            if (_opened.TryGetValue(familyName, out var cached))
                return cached;

            var list = LoadList();

            // 走査で開いたものは LoadList の中でキャッシュへ入る。開き直さない。
            if (_opened.TryGetValue(familyName, out cached))
                return cached;

            Entry hit = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].FamilyName, familyName, StringComparison.Ordinal))
                {
                    hit = list[i];
                    break;
                }
            }
            if (hit == null) return null;

            var f = PlyGlyphFile.Open(Path.Combine(hit.Dir, hit.FileName));
            _opened[familyName] = f;
            return f;
        }

        /// <summary>
        /// 一覧と開いたファイルを破棄する。再読込ボタンから呼ぶ。
        /// 置き場フォルダも捨てるので、RecentPaths.csv を直接編集した場合も次回反映される。
        /// </summary>
        public static void Clear()
        {
            _dirs = null;
            _entries = null;
            _opened.Clear();
        }
    }
}
