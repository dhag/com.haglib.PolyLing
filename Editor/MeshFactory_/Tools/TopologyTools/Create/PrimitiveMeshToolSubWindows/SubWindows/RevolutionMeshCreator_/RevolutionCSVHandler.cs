// Assets/Editor/MeshCreators/Revolution/RevolutionCSVHandler.cs
// 回転体メッシュ用のCSV入出力ハンドラ
// ローカライズ対応版

using static MeshFactory.Revolution.RevolutionTexts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeshFactory.Revolution
{
    /// <summary>
    /// CSV読み書き結果
    /// </summary>
    public class CSVLoadResult
    {
        public List<Vector2> Profile;
        public int RadialSegments;
        public bool CloseTop, CloseBottom, CloseLoop, Spiral;
        public float PivotY;
        public int SpiralTurns;
        public float SpiralPitch;
        public bool FlipY, FlipZ;
        public bool Success;
        public string ErrorMessage;
    }

    /// <summary>
    /// CSV入出力ハンドラ
    /// </summary>
    public static class RevolutionCSVHandler
    {
        /// <summary>
        /// CSVからプロファイルを読み込み
        /// </summary>
        public static CSVLoadResult LoadFromCSV(string path, RevolutionParams currentParams)
        {
            var result = new CSVLoadResult
            {
                Profile = new List<Vector2>(),
                RadialSegments = currentParams.RadialSegments,
                CloseTop = currentParams.CloseTop,
                CloseBottom = currentParams.CloseBottom,
                CloseLoop = currentParams.CloseLoop,
                Spiral = currentParams.Spiral,
                PivotY = currentParams.Pivot.y,
                SpiralTurns = currentParams.SpiralTurns,
                SpiralPitch = currentParams.SpiralPitch,
                FlipY = currentParams.FlipY,
                FlipZ = currentParams.FlipZ,
                Success = false
            };

            try
            {
                var lines = File.ReadAllLines(path);

                foreach (var line in lines)
                {
                    string trimmed = line.Trim();

                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    // コメント行
                    if (trimmed.StartsWith("#") || trimmed.StartsWith("//"))
                        continue;

                    // パラメータ行
                    if (trimmed.StartsWith("$"))
                    {
                        ParseParameter(trimmed.Substring(1).Trim(), result);
                        continue;
                    }

                    // ヘッダー行をスキップ
                    if (trimmed.ToLower().Contains("x") && trimmed.ToLower().Contains("y"))
                        continue;

                    // 座標データ
                    string[] parts = trimmed.Split(',');
                    if (parts.Length >= 2)
                    {
                        if (float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                            float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                        {
                            result.Profile.Add(new Vector2(x, y));
                        }
                    }
                }

                if (result.Profile.Count >= 2)
                {
                    result.Success = true;
                }
                else
                {
                    result.ErrorMessage = T("CSVNeedPoints");
                }
            }
            catch (Exception e)
            {
                result.ErrorMessage = e.Message;
            }

            return result;
        }

        /// <summary>
        /// ファイルダイアログを開いてCSVを読み込み
        /// </summary>
        public static CSVLoadResult LoadFromCSVWithDialog(RevolutionParams currentParams)
        {
            string path = EditorUtility.OpenFilePanel(T("CSVLoadTitle"), "", "csv");
            if (string.IsNullOrEmpty(path))
            {
                return new CSVLoadResult { Success = false, ErrorMessage = T("Cancelled") };
            }

            var result = LoadFromCSV(path, currentParams);

            if (!result.Success && !string.IsNullOrEmpty(result.ErrorMessage))
            {
                EditorUtility.DisplayDialog(T("Error"), T("CSVLoadError", result.ErrorMessage), T("OK"));
            }

            return result;
        }

        private static void ParseParameter(string paramLine, CSVLoadResult result)
        {
            string[] parts = paramLine.Split('=');
            if (parts.Length != 2) return;

            string key = parts[0].Trim().ToLower();
            string value = parts[1].Trim();

            switch (key)
            {
                case "radialsegments":
                    if (int.TryParse(value, out int rs)) result.RadialSegments = rs;
                    break;
                case "closetop":
                    if (bool.TryParse(value, out bool ct)) result.CloseTop = ct;
                    break;
                case "closebottom":
                    if (bool.TryParse(value, out bool cb)) result.CloseBottom = cb;
                    break;
                case "closeloop":
                    if (bool.TryParse(value, out bool cl)) result.CloseLoop = cl;
                    break;
                case "spiral":
                    if (bool.TryParse(value, out bool sp)) result.Spiral = sp;
                    break;
                case "pivoty":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float py))
                        result.PivotY = py;
                    break;
                case "spiralturns":
                    if (int.TryParse(value, out int st)) result.SpiralTurns = st;
                    break;
                case "spiralpitch":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float spt))
                        result.SpiralPitch = spt;
                    break;
                case "flipy":
                    if (bool.TryParse(value, out bool fy)) result.FlipY = fy;
                    break;
                case "flipz":
                    if (bool.TryParse(value, out bool fz)) result.FlipZ = fz;
                    break;
            }
        }

        /// <summary>
        /// プロファイルをCSVに保存
        /// </summary>
        public static bool SaveToCSV(string path, List<Vector2> profile, RevolutionParams p)
        {
            try
            {
                using (var writer = new StreamWriter(path))
                {
                    writer.WriteLine("# Revolution Profile");
                    writer.WriteLine($"$radialSegments={p.RadialSegments}");
                    writer.WriteLine($"$closeTop={p.CloseTop}");
                    writer.WriteLine($"$closeBottom={p.CloseBottom}");
                    writer.WriteLine($"$closeLoop={p.CloseLoop}");
                    writer.WriteLine($"$spiral={p.Spiral}");
                    writer.WriteLine($"$pivotY={p.Pivot.y.ToString(CultureInfo.InvariantCulture)}");
                    writer.WriteLine($"$spiralTurns={p.SpiralTurns}");
                    writer.WriteLine($"$spiralPitch={p.SpiralPitch.ToString(CultureInfo.InvariantCulture)}");
                    writer.WriteLine($"$flipY={p.FlipY}");
                    writer.WriteLine($"$flipZ={p.FlipZ}");
                    writer.WriteLine("X,Y");

                    foreach (var pt in profile)
                    {
                        writer.WriteLine($"{pt.x.ToString(CultureInfo.InvariantCulture)},{pt.y.ToString(CultureInfo.InvariantCulture)}");
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog(T("Error"), T("CSVSaveError", e.Message), T("OK"));
                return false;
            }
        }

        /// <summary>
        /// ファイルダイアログを開いてCSVを保存
        /// </summary>
        public static bool SaveToCSVWithDialog(List<Vector2> profile, RevolutionParams p)
        {
            string path = EditorUtility.SaveFilePanel(T("CSVSaveTitle"), "", "profile.csv", "csv");
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return SaveToCSV(path, profile, p);
        }
    }
}
