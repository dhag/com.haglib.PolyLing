// ProfilePointsCsvIO.cs
// 断面プロファイル（単一折れ線 List<Vector3>）の CSV 入出力コア（EditorUtility 非依存）
// 書式は RevolutionCSVIO に合わせる（# コメント / $key=value / X,Y,Z ヘッダー / InvariantCulture）。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【書式】
//   1 本：X,Y,Z（$version=3）。旧 X,Y（$version=1）も読み、z=0 とする。
//   2 本：XA,YA,ZA,XB,YB,ZB（$version=3）。旧 XA,YA,XB,YB（$version=2）も読み、z=0 とする。
//   ヘッダーが無いときは最初のデータ行の列数で決める
//   （2・3 列 = 1 本、4・5 列 = 旧 2 本、6 列以上 = 2 本）。

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
        public List<Vector3> Points = new List<Vector3>();
        public bool   ClosedLoop;
        public bool   Success;
        public string ErrorMessage = "";
    }

    /// <summary>
    /// A/B 2 本ぶんの CSV 読み込み結果。
    /// 1 本の書式を読んだときは PointsA だけが埋まり、HasB は false。
    /// </summary>
    public sealed class ProfilePointsPairLoadResult
    {
        public List<Vector3> PointsA = new List<Vector3>();
        public List<Vector3> PointsB = new List<Vector3>();

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

        public static bool Save(string path, IReadOnlyList<Vector3> points, bool closedLoop)
        {
            try
            {
                using (var w = new StreamWriter(path))
                {
                    w.WriteLine("# PolyLing Profile");
                    w.WriteLine("$version=3");
                    w.WriteLine($"$closedLoop={closedLoop}");
                    w.WriteLine("X,Y,Z");

                    if (points != null)
                    {
                        for (int i = 0; i < points.Count; i++)
                            w.WriteLine($"{F(points[i].x)},{F(points[i].y)},{F(points[i].z)}");
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
        /// A/B 2 本を 1 ファイルへ書く（6 列書式）。
        /// 行数は点数の多い側に合わせ、足りない側の列は空欄にする。
        /// </summary>
        public static bool SavePair(string path, IReadOnlyList<Vector3> a, IReadOnlyList<Vector3> b, bool closedLoop)
        {
            try
            {
                int ca = a?.Count ?? 0;
                int cb = b?.Count ?? 0;
                int n  = Mathf.Max(ca, cb);

                using (var w = new StreamWriter(path))
                {
                    w.WriteLine("# PolyLing Profile");
                    w.WriteLine("$version=3");
                    w.WriteLine($"$closedLoop={closedLoop}");
                    w.WriteLine("XA,YA,ZA,XB,YB,ZB");

                    for (int i = 0; i < n; i++)
                    {
                        string sa = i < ca ? $"{F(a[i].x)},{F(a[i].y)},{F(a[i].z)}" : ",,";
                        string sb = i < cb ? $"{F(b[i].x)},{F(b[i].y)},{F(b[i].z)}" : ",,";
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

        /// <summary>1 本として読む。2 本の書式なら A だけを返す。</summary>
        public static ProfilePointsLoadResult Load(string path, bool currentClosedLoop)
        {
            var pair = LoadPair(path, currentClosedLoop);
            return new ProfilePointsLoadResult
            {
                Points       = pair.PointsA,
                ClosedLoop   = pair.ClosedLoop,
                Success      = pair.Success,
                ErrorMessage = pair.ErrorMessage,
            };
        }

        /// <summary>書式（1 本 / 旧 2 本 / 2 本）。</summary>
        private enum Layout { Single, PairXY, PairXYZ }

        /// <summary>
        /// 1 本の書式（X,Y[,Z]）と 2 本の書式（XA,YA,XB,YB / XA,YA,ZA,XB,YB,ZB）を読む。
        /// 書式はヘッダー行で決め、ヘッダーが無ければ最初のデータ行の列数で決める。
        /// B 列は空欄可（その行は B へ足さない）。
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

                var  layout  = Layout.Single;
                bool hasZ    = false;   // 1 本の書式で z 列があるか
                bool decided = false;

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

                    // ヘッダー行（先頭が英字）。列名で書式を決める。
                    if (char.IsLetter(t[0]))
                    {
                        if (!decided)
                        {
                            string h0 = parts.Length > 0 ? parts[0].Trim().ToLowerInvariant() : "";
                            string h2 = parts.Length > 2 ? parts[2].Trim().ToLowerInvariant() : "";
                            if (h0 == "xa")
                            {
                                layout  = h2 == "za" ? Layout.PairXYZ : Layout.PairXY;
                                decided = true;
                            }
                            else if (h0 == "x")
                            {
                                layout  = Layout.Single;
                                hasZ    = h2 == "z";
                                decided = true;
                            }
                        }
                        continue;
                    }

                    // ヘッダーが無いCSVは、最初のデータ行の列数で決める。
                    if (!decided)
                    {
                        layout  = parts.Length >= 6 ? Layout.PairXYZ
                                : parts.Length >= 4 ? Layout.PairXY
                                : Layout.Single;
                        hasZ    = layout == Layout.Single && parts.Length >= 3;
                        decided = true;
                    }

                    switch (layout)
                    {
                        case Layout.Single:
                            if (TryPoint(parts, 0, hasZ, out var s)) result.PointsA.Add(s);
                            break;
                        case Layout.PairXY:
                            if (TryPoint(parts, 0, false, out var a2)) result.PointsA.Add(a2);
                            if (TryPoint(parts, 2, false, out var b2)) result.PointsB.Add(b2);
                            break;
                        case Layout.PairXYZ:
                            if (TryPoint(parts, 0, true, out var a3)) result.PointsA.Add(a3);
                            if (TryPoint(parts, 3, true, out var b3)) result.PointsB.Add(b3);
                            break;
                    }
                }

                result.HasB = layout != Layout.Single && result.PointsB.Count >= 2;

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

        /// <summary>
        /// parts[at] から 1 点を読む。hasZ が false なら z=0。
        /// x か y が空欄・読めないときは false（その行はその側へ足さない）。
        /// z の欄が空欄・読めないときは z=0。
        /// </summary>
        private static bool TryPoint(string[] parts, int at, bool hasZ, out Vector3 p)
        {
            p = Vector3.zero;
            if (parts.Length < at + 2) return false;
            if (!TryF(parts[at], out float x)) return false;
            if (!TryF(parts[at + 1], out float y)) return false;
            float z = 0f;
            if (hasZ && parts.Length > at + 2 && !TryF(parts[at + 2], out z)) z = 0f;
            p = new Vector3(x, y, z);
            return true;
        }

        private static bool TryF(string s, out float f)
            => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f);

        private static string F(float v) => v.ToString(CultureInfo.InvariantCulture);
    }
}
