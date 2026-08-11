// PlyFontLibrary.cs
// フォント置き場 <persistentDataPath>/PolyLing/Fonts/ の fonts.txt を読み、
// ファミリ名から .plgly を開く。開いたファイルはファミリ名でキャッシュする。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.GlyphText
{
    public static class PlyFontLibrary
    {
        public const string FontListName = "fonts.txt";

        /// <summary>フォント置き場。保存先の規約は他の設定ファイルと同じ。</summary>
        public static string RootDir => Path.Combine(Application.persistentDataPath, "PolyLing", "Fonts");

        /// <summary>fonts.txt の 1 行。</summary>
        public sealed class Entry
        {
            public string FileName;
            public string FamilyName;
        }

        private static List<Entry> _entries;
        private static readonly Dictionary<string, PlyGlyphFile> _opened =
            new Dictionary<string, PlyGlyphFile>(StringComparer.Ordinal);

        /// <summary>fonts.txt を読み込む。無い場合は空リスト。</summary>
        public static List<Entry> LoadList()
        {
            if (_entries != null) return _entries;

            var list = new List<Entry>();
            string path = Path.Combine(RootDir, FontListName);

            if (!File.Exists(path))
            {
                _entries = list;
                return list;
            }

            try
            {
                string all = File.ReadAllText(path, new UTF8Encoding(false));
                string[] lines = all.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].TrimEnd('\r');
                    if (line.Length == 0) continue;

                    int tab = line.IndexOf('\t');
                    if (tab <= 0 || tab >= line.Length - 1) continue;

                    var e = new Entry();
                    e.FileName = line.Substring(0, tab).Trim();
                    e.FamilyName = line.Substring(tab + 1).Trim();
                    if (e.FileName.Length == 0 || e.FamilyName.Length == 0) continue;

                    list.Add(e);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlyFontLibrary] {FontListName} の読み込みに失敗しました: {ex.Message}");
            }

            _entries = list;
            return list;
        }

        /// <summary>ファミリ名から .plgly を開く。見つからなければ null。</summary>
        public static PlyGlyphFile Open(string familyName)
        {
            if (string.IsNullOrEmpty(familyName)) return null;

            if (_opened.TryGetValue(familyName, out var cached))
                return cached;

            var list = LoadList();
            string fileName = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].FamilyName, familyName, StringComparison.Ordinal))
                {
                    fileName = list[i].FileName;
                    break;
                }
            }
            if (fileName == null) return null;

            var f = PlyGlyphFile.Open(Path.Combine(RootDir, fileName));
            _opened[familyName] = f;
            return f;
        }

        /// <summary>一覧と開いたファイルを破棄する。再読込ボタンから呼ぶ。</summary>
        public static void Clear()
        {
            _entries = null;
            _opened.Clear();
        }
    }
}
