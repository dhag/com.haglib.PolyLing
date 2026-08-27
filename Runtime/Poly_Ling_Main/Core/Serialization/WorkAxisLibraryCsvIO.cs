// WorkAxisLibraryCsvIO.cs
// 作業軸辞書（WorkAxisLibrary）の CSV 入出力コア。
//
// 書式は ProfilePointsCsvIO / RevolutionCSVIO に合わせる。
//   # コメント行
//   $key=value
//   ヘッダ行
//   データ行（InvariantCulture）
//
// 列は Name,OX,OY,OZ,QX,QY,QZ,QW,Length。
// 名前にカンマが入ると列がずれるので、書き出し時にカンマを全角へ置き換える。
// 復元はしない（読み込んだ名前がそのままキーになる）。
//
// ファイルダイアログは呼び出し側が担当する（ProfilePointsCsvIO と同じ規約）。
//
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using Poly_Ling.Context;

namespace Poly_Ling.Serialization
{
    /// <summary>CSV 読み込み結果。</summary>
    public sealed class WorkAxisLibraryLoadResult
    {
        public int    Loaded;
        public int    Skipped;
        public bool   Success;
        public string ErrorMessage = "";
    }

    /// <summary>作業軸辞書 CSV の読み書きコア。</summary>
    public static class WorkAxisLibraryCsvIO
    {
        private const string Header = "Name,OX,OY,OZ,QX,QY,QZ,QW,Length";

        private static string F(float v) => v.ToString(CultureInfo.InvariantCulture);

        private static bool P(string s, out float v)
            => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        // ================================================================
        // 書き込み
        // ================================================================

        public static bool Save(string path, WorkAxisLibrary lib)
        {
            if (lib == null) return false;

            try
            {
                using (var w = new StreamWriter(path))
                {
                    w.WriteLine("# PolyLing WorkAxis Library");
                    w.WriteLine("$version=1");
                    w.WriteLine(Header);

                    foreach (var name in lib.Names)
                    {
                        if (!lib.TryGet(name, out var e)) continue;

                        // カンマは列区切りなので名前から除く。
                        string safe = name.Replace(',', '，');

                        w.WriteLine(
                            $"{safe},{F(e.Origin.x)},{F(e.Origin.y)},{F(e.Origin.z)}," +
                            $"{F(e.Rotation.x)},{F(e.Rotation.y)},{F(e.Rotation.z)},{F(e.Rotation.w)}," +
                            $"{F(e.Length)}");
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[WorkAxisLibraryCsvIO] Save failed: {e.Message}");
                return false;
            }
        }

        // ================================================================
        // 読み込み
        // ================================================================

        /// <summary>
        /// CSV を読んで辞書へ入れる。merge が false のときは先に Clear する。
        /// 同名は上書き。壊れた行は読み飛ばして Skipped に数える。
        /// </summary>
        public static WorkAxisLibraryLoadResult Load(string path, WorkAxisLibrary lib, bool merge)
        {
            var r = new WorkAxisLibraryLoadResult();
            if (lib == null) { r.ErrorMessage = "library is null"; return r; }

            try
            {
                string[] lines = File.ReadAllLines(path);
                if (!merge) lib.Clear();

                foreach (var raw in lines)
                {
                    string line = raw?.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("#") || line.StartsWith("$")) continue;
                    if (line.StartsWith("Name,")) continue;   // ヘッダ行

                    var c = line.Split(',');
                    if (c.Length < 9) { r.Skipped++; continue; }

                    string name = c[0].Trim();
                    if (name.Length == 0) { r.Skipped++; continue; }

                    if (!P(c[1], out float ox) || !P(c[2], out float oy) || !P(c[3], out float oz) ||
                        !P(c[4], out float qx) || !P(c[5], out float qy) || !P(c[6], out float qz) ||
                        !P(c[7], out float qw) || !P(c[8], out float len))
                    {
                        r.Skipped++;
                        continue;
                    }

                    var q = new Quaternion(qx, qy, qz, qw);
                    // 単位クォータニオンでないと姿勢として使えない。ゼロ長は捨てる。
                    if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 1e-8f)
                    {
                        r.Skipped++;
                        continue;
                    }
                    q.Normalize();

                    lib.Set(name, new WorkAxisEntry
                    {
                        Origin   = new Vector3(ox, oy, oz),
                        Rotation = q,
                        // 0 以下は WorkAxisContext.Length の下限クランプで
                        // 使い物にならない長さになるため既定値へ倒す。
                        Length   = len > 0f ? len : WorkAxisContext.DefaultLength,
                    });
                    r.Loaded++;
                }

                r.Success = true;
                return r;
            }
            catch (Exception e)
            {
                r.ErrorMessage = e.Message;
                Debug.LogWarning($"[WorkAxisLibraryCsvIO] Load failed: {e.Message}");
                return r;
            }
        }
    }
}
