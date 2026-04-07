// RevolutionCSVIO.cs
// 回転体メッシュ用 CSV 入出力コア（EditorUtility 非依存）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Poly_Ling.Revolution
{
    /// <summary>CSV 読み込み結果</summary>
    public class CSVLoadResult
    {
        public List<Vector2> Profile;
        public int   RadialSegments;
        public bool  CloseTop, CloseBottom, CloseLoop, Spiral;
        public float PivotY;
        public int   SpiralTurns;
        public float SpiralPitch;
        public bool  FlipY, FlipZ;
        public bool  Success;
        public string ErrorMessage;
    }

    /// <summary>CSV 読み書きコア。ファイルダイアログは Editor 側のラッパー (RevolutionCSVHandler) が担当。</summary>
    public static class RevolutionCSVIO
    {
        // ================================================================
        // 読み込み
        // ================================================================

        public static CSVLoadResult Load(string path, RevolutionParams currentParams)
        {
            var result = new CSVLoadResult
            {
                Profile        = new List<Vector2>(),
                RadialSegments = currentParams.RadialSegments,
                CloseTop       = currentParams.CloseTop,
                CloseBottom    = currentParams.CloseBottom,
                CloseLoop      = currentParams.CloseLoop,
                Spiral         = currentParams.Spiral,
                PivotY         = currentParams.Pivot.y,
                SpiralTurns    = currentParams.SpiralTurns,
                SpiralPitch    = currentParams.SpiralPitch,
                FlipY          = currentParams.FlipY,
                FlipZ          = currentParams.FlipZ,
                Success        = false,
            };

            try
            {
                var lines = File.ReadAllLines(path);

                foreach (var line in lines)
                {
                    string t = line.Trim();
                    if (string.IsNullOrEmpty(t)) continue;
                    if (t.StartsWith("#") || t.StartsWith("//")) continue;

                    if (t.StartsWith("$"))
                    {
                        ParseParameter(t.Substring(1).Trim(), result);
                        continue;
                    }

                    // ヘッダー行スキップ（"X,Y" など、先頭が英字の行）
                    if (t.Length > 0 && char.IsLetter(t[0])) continue;

                    var parts = t.Split(',');
                    if (parts.Length >= 2 &&
                        float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                        float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                    {
                        result.Profile.Add(new Vector2(x, y));
                    }
                }

                if (result.Profile.Count >= 2)
                    result.Success = true;
                else
                    result.ErrorMessage = "CSV には 2 点以上必要です";
            }
            catch (Exception e)
            {
                result.ErrorMessage = e.Message;
            }

            return result;
        }

        // ================================================================
        // 書き込み
        // ================================================================

        public static bool Save(string path, List<Vector2> profile, RevolutionParams p)
        {
            try
            {
                using (var w = new StreamWriter(path))
                {
                    w.WriteLine("# Revolution Profile");
                    w.WriteLine($"$radialSegments={p.RadialSegments}");
                    w.WriteLine($"$closeTop={p.CloseTop}");
                    w.WriteLine($"$closeBottom={p.CloseBottom}");
                    w.WriteLine($"$closeLoop={p.CloseLoop}");
                    w.WriteLine($"$spiral={p.Spiral}");
                    w.WriteLine($"$pivotY={p.Pivot.y.ToString(CultureInfo.InvariantCulture)}");
                    w.WriteLine($"$spiralTurns={p.SpiralTurns}");
                    w.WriteLine($"$spiralPitch={p.SpiralPitch.ToString(CultureInfo.InvariantCulture)}");
                    w.WriteLine($"$flipY={p.FlipY}");
                    w.WriteLine($"$flipZ={p.FlipZ}");
                    w.WriteLine("X,Y");
                    foreach (var pt in profile)
                        w.WriteLine($"{pt.x.ToString(CultureInfo.InvariantCulture)},{pt.y.ToString(CultureInfo.InvariantCulture)}");
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RevolutionCSVIO] Save failed: {e.Message}");
                return false;
            }
        }

        // ================================================================
        // 内部
        // ================================================================

        private static void ParseParameter(string paramLine, CSVLoadResult result)
        {
            var parts = paramLine.Split('=');
            if (parts.Length != 2) return;
            string key = parts[0].Trim().ToLower();
            string val = parts[1].Trim();

            switch (key)
            {
                case "radialsegments": if (int.TryParse(val, out int rs))   result.RadialSegments = rs; break;
                case "closetop":       if (bool.TryParse(val, out bool ct)) result.CloseTop  = ct; break;
                case "closebottom":    if (bool.TryParse(val, out bool cb)) result.CloseBottom = cb; break;
                case "closeloop":      if (bool.TryParse(val, out bool cl)) result.CloseLoop = cl; break;
                case "spiral":         if (bool.TryParse(val, out bool sp)) result.Spiral    = sp; break;
                case "pivoty":
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float py))
                        result.PivotY = py;
                    break;
                case "spiralturns": if (int.TryParse(val, out int st))   result.SpiralTurns  = st; break;
                case "spiralpitch":
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float spt))
                        result.SpiralPitch = spt;
                    break;
                case "flipy": if (bool.TryParse(val, out bool fy)) result.FlipY = fy; break;
                case "flipz": if (bool.TryParse(val, out bool fz)) result.FlipZ = fz; break;
            }
        }
    }
}
