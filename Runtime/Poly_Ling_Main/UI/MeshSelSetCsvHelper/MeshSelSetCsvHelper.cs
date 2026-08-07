// MeshSelSetCsvHelper.cs
// メッシュ選択辞書（ModelContext.MeshSelectionSets）の CSV 保存 / 読込。
// 形式は CsvModelSerializer の meshselsets.csv と同一のため相互運用できる。
//   1 行目: #PolyLing_MeshSelSets,version,1.0
//   以降  : name,category,nameCount,meshName0,meshName1,...
// ダイアログ・GUIUtility.ExitGUI() は使わない（UIToolkit から呼べるようにするため）。
// Runtime/Poly_Ling_Main/UI/MeshSelSetCsvHelper/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.UI
{
    public static class MeshSelSetCsvHelper
    {
        private const string Header = "#PolyLing_MeshSelSets,version,1.0";

        // ================================================================
        // 保存
        // ================================================================

        /// <summary>
        /// メッシュ選択辞書を CSV ファイルへ書き出す。
        /// </summary>
        /// <returns>書き出したエントリ数。失敗時は -1</returns>
        public static int SaveToFile(ModelContext model, string filePath)
        {
            if (model == null || string.IsNullOrEmpty(filePath)) return -1;

            var sets = model.MeshSelectionSets;
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine(Header);

                int count = 0;
                if (sets != null)
                {
                    foreach (var ms in sets)
                    {
                        if (ms == null) continue;
                        var names = ms.MeshNames ?? new List<string>();
                        sb.Append($"{Esc(ms.Name)},{ms.Category},{names.Count}");
                        foreach (var meshName in names)
                            sb.Append($",{Esc(meshName)}");
                        sb.AppendLine();
                        count++;
                    }
                }

                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(false));
                return count;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MeshSelSetCsvHelper] 保存失敗: {ex.Message}");
                return -1;
            }
        }

        // ================================================================
        // 読込
        // ================================================================

        /// <summary>
        /// CSV ファイルからメッシュ選択辞書を読み込み、既存リストへ追加する。
        /// 名前が重複する場合はユニーク名を生成して追加する（既存は消さない）。
        /// </summary>
        /// <returns>追加したエントリ数。失敗時は -1</returns>
        public static int LoadFromFile(ModelContext model, string filePath)
        {
            if (model == null || string.IsNullOrEmpty(filePath)) return -1;
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[MeshSelSetCsvHelper] ファイルが見つかりません: {filePath}");
                return -1;
            }

            try
            {
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);
                if (model.MeshSelectionSets == null)
                    model.MeshSelectionSets = new List<MeshSelectionSet>();

                int added = 0;
                foreach (var line in lines)
                {
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    var cols = Split(line);
                    if (cols.Length < 3) continue;

                    var ms = new MeshSelectionSet(Unesc(cols[0]));

                    if (Enum.TryParse<ModelContext.SelectionCategory>(cols[1], out var cat))
                        ms.Category = cat;

                    int nameCount = PInt(cols, 2);
                    for (int i = 0; i < nameCount; i++)
                    {
                        int ci = 3 + i;
                        if (ci >= cols.Length) break;
                        string meshName = Unesc(cols[ci]);
                        if (!string.IsNullOrEmpty(meshName))
                            ms.MeshNames.Add(meshName);
                    }

                    if (string.IsNullOrEmpty(ms.Name))
                        ms.Name = model.GenerateUniqueMeshSelectionSetName("MeshSet");
                    else if (model.FindMeshSelectionSetByName(ms.Name) != null)
                        ms.Name = model.GenerateUniqueMeshSelectionSetName(ms.Name);

                    model.MeshSelectionSets.Add(ms);
                    added++;
                }
                return added;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MeshSelSetCsvHelper] 読込失敗: {ex.Message}");
                return -1;
            }
        }

        // ================================================================
        // CSV ヘルパー（CsvModelSerializer と同一ロジック）
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

        private static int PInt(string[] cols, int idx, int def = 0)
        {
            if (idx >= cols.Length || string.IsNullOrEmpty(cols[idx])) return def;
            return int.TryParse(cols[idx], System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
        }
    }
}
