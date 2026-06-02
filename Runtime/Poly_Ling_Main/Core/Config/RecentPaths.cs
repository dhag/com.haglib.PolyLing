// RecentPaths.cs
// テキストボックスに入力したファイル名／フォルダ（パス文字列）を保存・復元する。
// 保存先: Application.persistentDataPath/PolyLing/RecentPaths.csv
//   - 1行1項目 "key,value"
//   - 行頭が # の行はコメント、空行は無視
//   - value は最初のカンマ以降すべて（パス内のカンマも保持）
//   - テキストエディタで編集可能
// 起動時(初回アクセス時)に1回だけ読込。Set 時に即書込（write-through）。
// Editor/Player 両対応（#if UNITY_EDITOR 不使用、毎フレーム処理なし）。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.Core
{
    public static class RecentPaths
    {
        private static Dictionary<string, string> _values;
        private static readonly object _lock = new object();

        private static string Dir      => Path.Combine(Application.persistentDataPath, "PolyLing");
        private static string FilePath => Path.Combine(Dir, "RecentPaths.csv");

        // ================================================================
        // 公開API
        // ================================================================

        /// <summary>保存済みのパスを取得（未登録なら fallback）</summary>
        public static string Get(string key, string fallback = "")
        {
            EnsureLoaded();
            if (_values.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
                return v;
            return fallback;
        }

        /// <summary>パスを保存（即ファイル書込）。空文字はキー削除扱い。</summary>
        public static void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            EnsureLoaded();

            lock (_lock)
            {
                if (string.IsNullOrEmpty(value))
                    _values.Remove(key);
                else
                    _values[key] = value;
                Write();
            }
        }

        /// <summary>ファイルを再読込（テキスト編集後に反映したい場合）</summary>
        public static void Reload()
        {
            lock (_lock) { _values = null; }
            EnsureLoaded();
        }

        // ================================================================
        // 読込・書込
        // ================================================================

        private static void EnsureLoaded()
        {
            if (_values != null) return;
            lock (_lock)
            {
                if (_values != null) return;
                _values = Load();
            }
        }

        private static Dictionary<string, string> Load()
        {
            var dict = new Dictionary<string, string>();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (var raw in File.ReadAllLines(FilePath))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#') continue;

                        int comma = line.IndexOf(',');
                        if (comma <= 0) continue;

                        string key = line.Substring(0, comma).Trim();
                        string val = line.Substring(comma + 1);   // カンマ以降はそのまま（Trimしない）
                        if (key.Length == 0) continue;

                        dict[key] = val;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RecentPaths] 読込失敗: {e.Message}");
                dict.Clear();
            }
            return dict;
        }

        private static void Write()
        {
            try
            {
                Directory.CreateDirectory(Dir);

                var sb = new StringBuilder();
                sb.AppendLine("# PolyLing 最近使用したファイル名／フォルダ");
                sb.AppendLine("# 形式: key,value （value はパス文字列。行頭 # はコメント）");
                sb.AppendLine();

                foreach (var kv in _values)
                {
                    sb.Append(kv.Key);
                    sb.Append(',');
                    sb.AppendLine(kv.Value);
                }

                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RecentPaths] 書込失敗: {e.Message}");
            }
        }
    }
}
