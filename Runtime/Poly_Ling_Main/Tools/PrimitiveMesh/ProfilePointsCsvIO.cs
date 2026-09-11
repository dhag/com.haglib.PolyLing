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

    /// <summary>
    /// A/B 2 本ぶんの CSV 読み込み結果。
    /// 2 列書式（X,Y）を読んだときは PointsA だけが埋まり、HasB は false。
    /// </summary>
    public sealed class ProfilePointsPairLoadResult
    {
        public List<Vector2> PointsA = new List<Vector2>();
        public List<Vector2> PointsB = new List<Vector2>();

        /// <summary>B 列を持つ書式で、B が 2 点以上あったか。</summary>
        public bool   HasB;
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

        /// <summary>
        /// A/B 2 本を 1 ファイルへ書く（4 列書式）。
        /// 行数は点数の多い側に合わせ、足りない側の列は空欄にする。
        /// </summary>
        public static bool SavePair(string path, IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b, bool closedLoop)
        {
            try
            {
                int ca = a?.Count ?? 0;
                int cb = b?.Count ?? 0;
                int n  = Mathf.Max(ca, cb);

                using (var w = new StreamWriter(path))
                {
                    w.WriteLine("# PolyLing Profile");
                    w.WriteLine("$version=2");
                    w.WriteLine($"$closedLoop={closedLoop}");
                    w.WriteLine("XA,YA,XB,YB");

                    for (int i = 0; i < n; i++)
                    {
                        string sa = i < ca ? $"{F(a[i].x)},{F(a[i].y)}" : ",";
                        string sb = i < cb ? $"{F(b[i].x)},{F(b[i].y)}" : ",";
                        w.WriteLine(sa + "," + sb);
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ProfilePointsCsvIO] SavePair failed: {e.Message}");
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

        /// <summary>
        /// 2 列書式（X,Y）と 4 列書式（XA,YA,XB,YB）の両方を読む。
        /// 書式はヘッダー行が XA,YA,XB,YB かで決め、ヘッダーが無ければ
        /// 最初のデータ行の列数で決める。B 列は空欄可（その行は B へ足さない）。
        /// </summary>
        public static ProfilePointsPairLoadResult LoadPair(string path, bool currentClosedLoop)
        {
            var result = new ProfilePointsPairLoadResult
            {
                ClosedLoop = currentClosedLoop,
                Success    = false,
            };

            try
            {
                var lines = File.ReadAllLines(path);

                bool pair    = false;   // 4 列書式か
                bool decided = false;   // 書式が決まったか

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

                    var parts = t.Split(',');

                    // ヘッダー行（先頭が英字）。XA,YA,XB,YB なら 4 列書式と決める。
                    if (char.IsLetter(t[0]))
                    {
                        if (!decided && parts.Length >= 4 &&
                            parts[0].Trim().ToLowerInvariant() == "xa" &&
                            parts[1].Trim().ToLowerInvariant() == "ya" &&
                            parts[2].Trim().ToLowerInvariant() == "xb" &&
                            parts[3].Trim().ToLowerInvariant() == "yb")
                        {
                            pair    = true;
                            decided = true;
                        }
                        continue;
                    }

                    // ヘッダーが無いCSVは、最初のデータ行の列数で決める。
                    if (!decided)
                    {
                        pair    = parts.Length >= 4;
                        decided = true;
                    }

                    if (parts.Length < 2) continue;
                    if (TryF(parts[0], out float ax) && TryF(parts[1], out float ay))
                        result.PointsA.Add(new Vector2(ax, ay));

                    if (!pair || parts.Length < 4) continue;
                    if (TryF(parts[2], out float bx) && TryF(parts[3], out float by))
                        result.PointsB.Add(new Vector2(bx, by));
                }

                result.HasB = pair && result.PointsB.Count >= 2;

                if (result.PointsA.Count >= 2)
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

        private static string F(float v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
