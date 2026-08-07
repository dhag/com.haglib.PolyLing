// ProfilePointsCsvIO.cs
// 断面プロファイル（単一折れ線 List<Vector2>）の CSV 入出力コア（EditorUtility 非依存）
// 書式は RevolutionCSVIO に合わせる（# コメント / $key=value / X,Y ヘッダー / InvariantCulture）。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>CSV 読み込み結果。</summary>
    public sealed class ProfilePointsLoadResult
    {
        public List<Vector2> Points = new List<Vector2>();
        public bool   ClosedLoop;
        public bool   Success;
        public string ErrorMessage = "";
    }

    /// <summary>断面プロファイル CSV の読み書きコア。ファイルダイアログは呼出し側が担当する。</summary>
    public static class ProfilePointsCsvIO
    {
        // ================================================================
        // 書き込み
        // ================================================================

        public static bool Save(string path, IReadOnlyList<Vector2> points, bool closedLoop)
        {
            try
            {
                using (var w = new StreamWriter(path))
                {
                    w.WriteLine("# PolyLing Profile");
                    w.WriteLine("$version=1");
                    w.WriteLine($"$closedLoop={closedLoop}");
                    w.WriteLine("X,Y");

                    if (points != null)
                    {
                        for (int i = 0; i < points.Count; i++)
                            w.WriteLine($"{points[i].x.ToString(CultureInfo.InvariantCulture)}," +
                                        $"{points[i].y.ToString(CultureInfo.InvariantCulture)}");
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProfilePointsCsvIO] Save failed: {e.Message}");
                return false;
            }
        }

        // ================================================================
        // 読み込み
        // ================================================================

        public static ProfilePointsLoadResult Load(string path, bool currentClosedLoop)
        {
            var result = new ProfilePointsLoadResult
            {
                ClosedLoop = currentClosedLoop,
                Success    = false,
            };

            try
            {
                var lines = File.ReadAllLines(path);

                foreach (var raw in lines)
                {
                    string t = raw.Trim();
                    if (string.IsNullOrEmpty(t)) continue;
                    if (t.StartsWith("#") || t.StartsWith("//")) continue;

                    if (t.StartsWith("$"))
                    {
                        var kv = t.Substring(1).Split(new[] { '=' }, 2);
                        if (kv.Length != 2) continue;
                        if (kv[0].Trim().ToLowerInvariant() == "closedloop" &&
                            bool.TryParse(kv[1].Trim(), out bool cl))
                            result.ClosedLoop = cl;
                        continue;
                    }

                    // ヘッダー行（先頭が英字）はスキップ
                    if (char.IsLetter(t[0])) continue;

                    var parts = t.Split(',');
                    if (parts.Length < 2) continue;
                    if (!TryF(parts[0], out float x)) continue;
                    if (!TryF(parts[1], out float y)) continue;

                    result.Points.Add(new Vector2(x, y));
                }

                if (result.Points.Count >= 2)
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
        // 内部
        // ================================================================

        private static bool TryF(string s, out float f)
            => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f);
    }
}
