// Assets/Editor/MeshCreators/Profile2DExtrude/Profile2DCSVHandler.cs
// 2D閉曲線プロファイルのCSV入出力ハンドラ
// ローカライズ対応版

using static MeshFactory.Profile2DExtrude.Profile2DExtrudeTexts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MeshFactory.Profile2DExtrude
{
    /// <summary>
    /// CSV読み込み結果
    /// </summary>
    public class Profile2DCSVLoadResult
    {
        public List<Loop> Loops = new List<Loop>();
        public bool Success;
        public string ErrorMessage;
    }

    /// <summary>
    /// CSV入出力ハンドラ
    /// </summary>
    public static class Profile2DCSVHandler
    {
        /// <summary>
        /// CSVからループを読み込み
        /// </summary>
        public static Profile2DCSVLoadResult LoadFromCSV(string path)
        {
            var result = new Profile2DCSVLoadResult();

            if (string.IsNullOrEmpty(path))
            {
                result.ErrorMessage = T("CSVPathEmpty");
                return result;
            }

            if (!File.Exists(path))
            {
                result.ErrorMessage = T("CSVNotFound", path);
                return result;
            }

            try
            {
                var lines = File.ReadAllLines(path);
                Loop current = null;
                bool nextIsHole = false;
                int pointCount = 0;

                foreach (var raw in lines)
                {
                    var line = raw.Trim();

                    // 空行でループ区切り
                    if (string.IsNullOrEmpty(line))
                    {
                        if (current != null && current.Points.Count >= 3)
                        {
                            result.Loops.Add(current);
                        }
                        current = null;
                        continue;
                    }

                    // コメント行
                    if (line.StartsWith("#"))
                    {
                        string comment = line.Substring(1).Trim().ToUpper();

                        if (comment == "HOLE")
                        {
                            nextIsHole = true;
                        }
                        else if (comment == "OUTER")
                        {
                            nextIsHole = false;
                        }
                        continue;
                    }

                    if (current == null)
                    {
                        current = new Loop();
                        current.IsHole = nextIsHole;
                        nextIsHole = false;
                    }

                    var tokens = line.Split(
                        new[] { ',', '\t', ';', ' ' },
                        StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length < 2)
                        continue;

                    if (TryParseFloat(tokens[0], out float x) &&
                        TryParseFloat(tokens[1], out float y))
                    {
                        current.Points.Add(new Vector2(x, y));
                        pointCount++;
                    }
                }

                // 最後のループを追加
                if (current != null && current.Points.Count >= 3)
                {
                    result.Loops.Add(current);
                }

                // 先頭と末尾が同じなら末尾を削る
                foreach (var loop in result.Loops)
                {
                    if (loop.Points.Count >= 4)
                    {
                        Vector2 first = loop.Points[0];
                        Vector2 last = loop.Points[loop.Points.Count - 1];
                        if (Vector2.Distance(first, last) < 1e-6f)
                        {
                            loop.Points.RemoveAt(loop.Points.Count - 1);
                        }
                    }
                }

                // #OUTER/#HOLEコメントがなかった場合のフォールバック
                bool hasExplicitType = result.Loops.Any(l => l.IsHole);
                if (!hasExplicitType && result.Loops.Count > 1)
                {
                    for (int i = 1; i < result.Loops.Count; i++)
                    {
                        result.Loops[i].IsHole = true;
                    }
                }

                result.Success = result.Loops.Count > 0;
                if (!result.Success)
                {
                    result.ErrorMessage = T("NoValidLoops");
                }
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// ファイルダイアログを開いてCSVを読み込み
        /// </summary>
        public static Profile2DCSVLoadResult LoadFromCSVWithDialog(string defaultPath = "")
        {
            string path = EditorUtility.OpenFilePanel(
                T("CSVLoadTitle"),
                string.IsNullOrEmpty(defaultPath) ? Application.dataPath : Path.GetDirectoryName(defaultPath),
                "csv");

            if (string.IsNullOrEmpty(path))
            {
                return new Profile2DCSVLoadResult { ErrorMessage = T("Cancelled") };
            }

            var result = LoadFromCSV(path);
            result.ErrorMessage = path; // パスを保存用に返す

            if (!result.Success && result.Loops.Count == 0)
            {
                EditorUtility.DisplayDialog(T("Error"), T("CSVLoadError", result.ErrorMessage), T("OK"));
            }

            return result;
        }

        /// <summary>
        /// ループをCSVに保存
        /// </summary>
        public static bool SaveToCSV(string path, List<Loop> loops, string meshName)
        {
            try
            {
                var sb = new StringBuilder();

                // ヘッダーコメント
                sb.AppendLine($"# Profile2D CSV - {meshName}");
                sb.AppendLine($"# Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine($"# Loops: {loops.Count}");
                sb.AppendLine();

                // 外側ループを先に出力
                foreach (var loop in loops.Where(l => !l.IsHole))
                {
                    sb.AppendLine("# OUTER");
                    foreach (var pt in loop.Points)
                    {
                        sb.AppendLine($"{pt.x.ToString("F6", CultureInfo.InvariantCulture)},{pt.y.ToString("F6", CultureInfo.InvariantCulture)}");
                    }
                    sb.AppendLine();
                }

                // ホールを出力
                foreach (var loop in loops.Where(l => l.IsHole))
                {
                    sb.AppendLine("# HOLE");
                    foreach (var pt in loop.Points)
                    {
                        sb.AppendLine($"{pt.x.ToString("F6", CultureInfo.InvariantCulture)},{pt.y.ToString("F6", CultureInfo.InvariantCulture)}");
                    }
                    sb.AppendLine();
                }

                File.WriteAllText(path, sb.ToString());
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to save CSV: {ex.Message}");
                EditorUtility.DisplayDialog(T("Error"), T("CSVSaveError", ex.Message), T("OK"));
                return false;
            }
        }

        /// <summary>
        /// ファイルダイアログを開いてCSVを保存
        /// </summary>
        public static string SaveToCSVWithDialog(List<Loop> loops, string meshName, string currentPath)
        {
            string defaultName = string.IsNullOrEmpty(meshName) ? "profile" : meshName;
            string path = EditorUtility.SaveFilePanel(
                T("CSVSaveTitle"),
                string.IsNullOrEmpty(currentPath) ? Application.dataPath : Path.GetDirectoryName(currentPath),
                defaultName,
                "csv");

            if (string.IsNullOrEmpty(path))
                return null;

            if (SaveToCSV(path, loops, meshName))
            {
                Debug.Log($"Saved {loops.Count} loops to: {path}");
                return path;
            }

            return null;
        }

        private static bool TryParseFloat(string s, out float value)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
                   float.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }
    }
}
