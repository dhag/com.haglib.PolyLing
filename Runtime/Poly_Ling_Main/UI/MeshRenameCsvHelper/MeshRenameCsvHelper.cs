// MeshRenameCsvHelper.cs
// 名称一括変更 CSV（旧名→新名の対応表）の読み書きと、重複を自動回避した
// 最終名の決定を行う。
//
//   1 行目: #PolyLing_MeshRename,version,1.0
//   以降  : 旧名,新名
//   '#' で始まる行はコメントとして読み飛ばす。
//
// CSV のエスケープ規則（Esc/Unesc/Split）は MeshSelSetCsvHelper /
// CsvModelSerializer と同一。相互に開いても壊れない。
//
// 【重複の自動回避】
//   一意性の範囲は ModelContext.GenerateUniqueMeshName と同じ「モデル全体」。
//   カテゴリ（メッシュ/ボーン/モーフ…）をまたいで一意にする。
//   予約名集合から「今回改名される対象の旧名」をあらかじめ外すため、
//   A→B / B→A の入れ替えでも _1 は付かない。
//
// ダイアログ・GUIUtility.ExitGUI() は使わない（UIToolkit から呼べるようにするため）。
// Runtime/Poly_Ling_Main/UI/MeshRenameCsvHelper/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Context;

namespace Poly_Ling.UI
{
    public static class MeshRenameCsvHelper
    {
        private const string Header = "#PolyLing_MeshRename,version,1.0";

        /// <summary>旧名→新名の1エントリ。</summary>
        public struct RenamePair
        {
            public string OldName;
            public string NewName;
            public RenamePair(string oldName, string newName)
            {
                OldName = oldName;
                NewName = newName;
            }
        }

        // ================================================================
        // 読込
        // ================================================================

        /// <summary>
        /// 対応表 CSV を読み込む。旧名・新名のどちらかが空の行は捨てる。
        /// 失敗時は null を返す（呼び出し側でステータス表示する）。
        /// </summary>
        public static List<RenamePair> LoadPairs(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[MeshRenameCsvHelper] ファイルが見つかりません: {filePath}");
                return null;
            }

            try
            {
                var lines  = File.ReadAllLines(filePath, Encoding.UTF8);
                var result = new List<RenamePair>();
                foreach (var line in lines)
                {
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    var cols = Split(line);
                    if (cols.Length < 2) continue;

                    string oldName = Unesc(cols[0]).Trim();
                    string newName = Unesc(cols[1]).Trim();
                    if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName)) continue;

                    result.Add(new RenamePair(oldName, newName));
                }
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MeshRenameCsvHelper] 読込失敗: {ex.Message}");
                return null;
            }
        }

        // ================================================================
        // 書出（雛形）
        // ================================================================

        /// <summary>
        /// 現在の名前を「旧名,新名（＝旧名と同じ）」で書き出す。編集の雛形用。
        /// </summary>
        /// <returns>書き出した行数。失敗時は -1</returns>
        public static int SaveTemplate(IReadOnlyList<string> names, string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return -1;

            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine(Header);

                int count = 0;
                if (names != null)
                {
                    foreach (var n in names)
                    {
                        if (string.IsNullOrEmpty(n)) continue;
                        sb.AppendLine($"{Esc(n)},{Esc(n)}");
                        count++;
                    }
                }

                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
                return count;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MeshRenameCsvHelper] 書出失敗: {ex.Message}");
                return -1;
            }
        }

        // ================================================================
        // 重複の自動回避
        // ================================================================

        /// <summary>
        /// 希望名を、モデル内で重複しない最終名へ解決する。
        ///
        /// 手順:
        ///   1. 予約名集合 = MeshContextList 全体の名前 − 今回改名される対象の現在名
        ///   2. 与えられた順に処理し、予約集合と衝突するなら _1, _2 … を付ける
        ///   3. 確定した名前を予約集合へ加える
        ///
        /// masterIndices と desiredNames は同じ並び・同じ長さであること。
        /// 範囲外インデックスや空の希望名の位置には null を返す（呼び出し側でスキップする）。
        /// </summary>
        public static string[] ResolveUniqueNames(
            ModelContext model, int[] masterIndices, string[] desiredNames)
        {
            if (model == null || masterIndices == null || desiredNames == null)
                return Array.Empty<string>();

            int n = Mathf.Min(masterIndices.Length, desiredNames.Length);
            var resolved = new string[n];

            var list = model.MeshContextList;
            if (list == null) return resolved;

            // 改名対象の位置を集める（予約名集合から自分の現在名を外すため）
            var targetIndices = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                int mi = masterIndices[i];
                if (mi < 0 || mi >= list.Count) continue;
                if (string.IsNullOrEmpty(desiredNames[i])) continue;
                targetIndices.Add(mi);
            }

            var reserved = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++)
            {
                if (targetIndices.Contains(i)) continue;
                var mc = list[i];
                if (mc == null || string.IsNullOrEmpty(mc.Name)) continue;
                reserved.Add(mc.Name);
            }

            for (int i = 0; i < n; i++)
            {
                int mi = masterIndices[i];
                if (mi < 0 || mi >= list.Count) { resolved[i] = null; continue; }

                string want = desiredNames[i];
                if (string.IsNullOrEmpty(want)) { resolved[i] = null; continue; }

                string name    = want;
                int    counter = 1;
                while (reserved.Contains(name))
                {
                    name = $"{want}_{counter}";
                    counter++;
                }
                reserved.Add(name);
                resolved[i] = name;
            }

            return resolved;
        }

        // ================================================================
        // CSV ヘルパー（MeshSelSetCsvHelper と同一ロジック）
        // ================================================================

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
            {
                s = s.Substring(1, s.Length - 2);
                s = s.Replace("\"\"", "\"");
            }
            return s;
        }

        private static string[] Split(string line)
        {
            var result = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < line.Length)
                    {
                        if (line[i] == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; }
                            else { i++; break; }
                        }
                        else { sb.Append(line[i]); i++; }
                    }
                    result.Add(sb.ToString());
                    if (i < line.Length && line[i] == ',') i++;
                }
                else
                {
                    int start = i;
                    while (i < line.Length && line[i] != ',') i++;
                    result.Add(line.Substring(start, i - start));
                    if (i < line.Length) i++;
                }
            }
            return result.ToArray();
        }
    }
}
