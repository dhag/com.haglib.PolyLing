// BeltCsvIO.cs
// 梯子状ベルト（基準ベルト）の CSV 入出力コア（EditorUtility 非依存）
// 書式は RevolutionCSVIO に合わせる（# コメント / $key=value / InvariantCulture）。
// 座標は取り込み元メッシュのローカル座標をそのまま入出力する（変換しない）。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>CSV 1本ぶんの梯子データ。</summary>
    public sealed class BeltCsvEntry
    {
        public List<Vector3> Left  = new List<Vector3>();
        public List<Vector3> Right = new List<Vector3>();

        public bool Closed;
        public bool FlipWinding;

        /// <summary>
        /// フリルの高さ倍率（法線方向成分に掛ける）。$heightScale が無い旧CSVでは 1。
        /// フリル以外（パイプ・配置）は読み書きするだけで使わない。
        /// </summary>
        public float HeightScale = 1f;

        /// <summary>自動検索で得た先端（rung には含めない）。無ければ null。</summary>
        public Vector3? StartPoint;
        public Vector3? EndPoint;

        public bool HasData => Left != null && Right != null
                               && Left.Count >= 2 && Left.Count == Right.Count;
    }

    /// <summary>CSV 読み込み結果。</summary>
    public sealed class BeltCsvLoadResult
    {
        public List<BeltCsvEntry> Belts = new List<BeltCsvEntry>();
        public bool   Success;
        public string ErrorMessage = "";
    }

    /// <summary>梯子 CSV の読み書きコア。ファイルダイアログは呼出し側が担当する。</summary>
    public static class BeltCsvIO
    {
        private const string Header = "LX,LY,LZ,RX,RY,RZ";

        // ================================================================
        // 書き込み
        // ================================================================

        public static bool Save(string path, IReadOnlyList<BeltCsvEntry> belts)
        {
            try
            {
                using (var w = new StreamWriter(path))
                {
                    w.WriteLine("# PolyLing Belt");
                    w.WriteLine("$version=1");

                    int index = 0;
                    if (belts != null)
                    {
                        for (int i = 0; i < belts.Count; i++)
                        {
                            var b = belts[i];
                            if (b == null || !b.HasData) continue;

                            w.WriteLine($"$belt={index}");
                            w.WriteLine($"$closed={b.Closed}");
                            w.WriteLine($"$flipWinding={b.FlipWinding}");
                            w.WriteLine($"$heightScale={b.HeightScale.ToString(CultureInfo.InvariantCulture)}");
                            if (b.StartPoint.HasValue) w.WriteLine($"$startPoint={V3(b.StartPoint.Value)}");
                            if (b.EndPoint.HasValue)   w.WriteLine($"$endPoint={V3(b.EndPoint.Value)}");
                            w.WriteLine(Header);

                            int n = b.Left.Count;
                            for (int r = 0; r < n; r++)
                                w.WriteLine($"{V3(b.Left[r])},{V3(b.Right[r])}");

                            index++;
                        }
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BeltCsvIO] Save failed: {e.Message}");
                return false;
            }
        }

        // ================================================================
        // 読み込み
        // ================================================================

        public static BeltCsvLoadResult Load(string path)
        {
            var result = new BeltCsvLoadResult { Success = false };

            try
            {
                var lines = File.ReadAllLines(path);

                BeltCsvEntry current = null;

                foreach (var raw in lines)
                {
                    string t = raw.Trim();
                    if (string.IsNullOrEmpty(t)) continue;
                    if (t.StartsWith("#") || t.StartsWith("//")) continue;

                    if (t.StartsWith("$"))
                    {
                        var kv = t.Substring(1).Split(new[] { '=' }, 2);
                        if (kv.Length != 2) continue;

                        string key = kv[0].Trim().ToLowerInvariant();
                        string val = kv[1].Trim();

                        if (key == "belt")
                        {
                            Flush(result, current);
                            current = new BeltCsvEntry();
                            continue;
                        }

                        if (current == null) continue;   // $version など、梯子開始前のキーは無視

                        switch (key)
                        {
                            case "closed":
                                if (bool.TryParse(val, out bool cl)) current.Closed = cl;
                                break;
                            case "flipwinding":
                                if (bool.TryParse(val, out bool fw)) current.FlipWinding = fw;
                                break;
                            case "heightscale":
                                if (TryF(val, out float hs)) current.HeightScale = hs;
                                break;
                            case "startpoint":
                                if (TryParseV3(val, out Vector3 sp)) current.StartPoint = sp;
                                break;
                            case "endpoint":
                                if (TryParseV3(val, out Vector3 ep)) current.EndPoint = ep;
                                break;
                        }
                        continue;
                    }

                    // ヘッダー行（先頭が英字）はスキップ
                    if (char.IsLetter(t[0])) continue;

                    if (current == null) continue;

                    var parts = t.Split(',');
                    if (parts.Length < 6) continue;
                    if (!TryF(parts[0], out float lx)) continue;
                    if (!TryF(parts[1], out float ly)) continue;
                    if (!TryF(parts[2], out float lz)) continue;
                    if (!TryF(parts[3], out float rx)) continue;
                    if (!TryF(parts[4], out float ry)) continue;
                    if (!TryF(parts[5], out float rz)) continue;

                    current.Left .Add(new Vector3(lx, ly, lz));
                    current.Right.Add(new Vector3(rx, ry, rz));
                }

                Flush(result, current);

                if (result.Belts.Count > 0)
                    result.Success = true;
                else
                    result.ErrorMessage = "はしごデータが見つかりません";
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

        /// <summary>rung 数が揃っていない梯子は捨てる。</summary>
        private static void Flush(BeltCsvLoadResult result, BeltCsvEntry entry)
        {
            if (entry == null) return;
            if (!entry.HasData) return;
            result.Belts.Add(entry);
        }

        private static string V3(Vector3 v)
            => $"{v.x.ToString(CultureInfo.InvariantCulture)}," +
               $"{v.y.ToString(CultureInfo.InvariantCulture)}," +
               $"{v.z.ToString(CultureInfo.InvariantCulture)}";

        private static bool TryParseV3(string s, out Vector3 v)
        {
            v = Vector3.zero;
            var p = s.Split(',');
            if (p.Length < 3) return false;
            if (!TryF(p[0], out float x)) return false;
            if (!TryF(p[1], out float y)) return false;
            if (!TryF(p[2], out float z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        private static bool TryF(string s, out float f)
            => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out f);
    }
}
