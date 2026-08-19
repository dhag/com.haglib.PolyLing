// MtlParser.cs
// Wavefront MTL テキスト → ObjMaterial リスト。
// Runtime/Poly_Ling_Main/OBJ/Import/ に配置
//
// 【対応する記法】
//   newmtl 名前
//   Kd / Ka / Ks   r g b        （spectral / xyz 指定は既定値のまま無視）
//   Ns  0-1000     鏡面指数
//   d   0-1        不透明度
//   Tr  0-1        透明度（d = 1 - Tr）
//   illum n        照明モデル
//   map_Kd / map_d / map_Bump / bump   テクスチャ
//
// 【マップ行のオプション】
//   map_Kd は "-o 0 0 0 -s 1 1 1 file.png" のようにオプションを取れる。
//   オプションは -名前 の形で、続く引数の個数が名前ごとに異なる。
//   ここでは「- で始まるトークンとその引数を読み飛ばし、残りをファイル名とする」
//   方式にする。ファイル名に空白が入る場合に備え、残りは連結して 1 個として扱う。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.OBJ
{
    public static class MtlParser
    {
        // オプション名 → 続く引数の個数
        private static readonly Dictionary<string, int> OptionArgCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["-blendu"]   = 1,
            ["-blendv"]   = 1,
            ["-boost"]    = 1,
            ["-mm"]       = 2,
            ["-o"]        = 3,
            ["-s"]        = 3,
            ["-t"]        = 3,
            ["-texres"]   = 1,
            ["-clamp"]    = 1,
            ["-bm"]       = 1,
            ["-imfchan"]  = 1,
            ["-type"]     = 1,
        };

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>MTL ファイルを読み込む。見つからなければ空リストを返す。</summary>
        public static List<ObjMaterial> ParseFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return new List<ObjMaterial>();

            try
            {
                return Parse(File.ReadAllText(filePath, new UTF8Encoding(false)));
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[MtlParser] MTL を読めませんでした: {filePath} - {e.Message}");
                return new List<ObjMaterial>();
            }
        }

        /// <summary>MTL テキストを読み込む。</summary>
        public static List<ObjMaterial> Parse(string text)
        {
            var result = new List<ObjMaterial>();
            if (string.IsNullOrEmpty(text)) return result;

            ObjMaterial current = null;

            foreach (string rawLine in text.Split('\n'))
            {
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                string keyword = FirstToken(line, out string rest);
                rest = rest.Trim();

                switch (keyword.ToLowerInvariant())
                {
                    case "newmtl":
                        current = new ObjMaterial { Name = rest.Length > 0 ? rest : "material" };
                        result.Add(current);
                        break;

                    case "kd":
                        if (current != null) current.Diffuse = ParseColor(rest, Color.white);
                        break;

                    case "ka":
                        if (current != null) current.Ambient = ParseColor(rest, Color.black);
                        break;

                    case "ks":
                        if (current != null) current.Specular = ParseColor(rest, Color.black);
                        break;

                    case "ns":
                        if (current != null) current.SpecularExponent = ParseFloat(rest, 0f);
                        break;

                    case "d":
                        if (current != null) current.Alpha = Mathf.Clamp01(ParseFloat(rest, 1f));
                        break;

                    case "tr":
                        // Tr は透明度。d と両方書かれた場合は後に来た方が勝つ（仕様上の慣例）。
                        if (current != null) current.Alpha = Mathf.Clamp01(1f - ParseFloat(rest, 0f));
                        break;

                    case "illum":
                        if (current != null)
                            current.IlluminationModel =
                                int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) ? m : 2;
                        break;

                    case "map_kd":
                        if (current != null) current.DiffuseMapPath = ExtractMapPath(rest);
                        break;

                    case "map_d":
                        if (current != null) current.AlphaMapPath = ExtractMapPath(rest);
                        break;

                    case "map_bump":
                    case "bump":
                        if (current != null) current.BumpMapPath = ExtractMapPath(rest);
                        break;

                    default:
                        // map_Ka / map_Ks / map_Ns / refl / Ni / Ke などは使わない。
                        break;
                }
            }

            return result;
        }

        // ================================================================
        // 内部
        // ================================================================

        private static string StripComment(string line)
        {
            int idx = line.IndexOf('#');
            return idx < 0 ? line : line.Substring(0, idx);
        }

        private static string FirstToken(string line, out string rest)
        {
            int i = 0;
            int n = line.Length;

            while (i < n && IsSpace(line[i])) i++;
            int start = i;
            while (i < n && !IsSpace(line[i])) i++;

            string token = line.Substring(start, i - start);
            rest = i < n ? line.Substring(i) : "";
            return token;
        }

        private static bool IsSpace(char c) => c == ' ' || c == '\t' || c == '\r';

        private static Color ParseColor(string s, Color fallback)
        {
            var tokens = Tokenize(s);
            if (tokens.Count == 0) return fallback;

            // spectral / xyz 指定は変換しない（既定値のまま）
            if (tokens[0].StartsWith("spectral", StringComparison.OrdinalIgnoreCase) ||
                tokens[0].StartsWith("xyz",      StringComparison.OrdinalIgnoreCase))
                return fallback;

            float r = ParseFloat(tokens[0], fallback.r);
            float g = tokens.Count > 1 ? ParseFloat(tokens[1], r) : r;   // 1 個だけならグレー
            float b = tokens.Count > 2 ? ParseFloat(tokens[2], r) : r;
            return new Color(r, g, b, 1f);
        }

        private static float ParseFloat(string s, float fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;
        }

        private static List<string> Tokenize(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(s)) return list;

            int i = 0;
            int n = s.Length;
            while (i < n)
            {
                while (i < n && IsSpace(s[i])) i++;
                if (i >= n) break;

                int start = i;
                while (i < n && !IsSpace(s[i])) i++;
                list.Add(s.Substring(start, i - start));
            }
            return list;
        }

        /// <summary>
        /// マップ行からファイル名を取り出す。先頭のオプション（-名前 + 引数）を読み飛ばし、
        /// 残りのトークンを空白で連結して返す（空白入りファイル名への保険）。
        /// </summary>
        private static string ExtractMapPath(string s)
        {
            var tokens = Tokenize(s);
            int i = 0;

            while (i < tokens.Count && tokens[i].StartsWith("-", StringComparison.Ordinal))
            {
                int args = OptionArgCount.TryGetValue(tokens[i], out int c) ? c : 1;
                i += 1 + args;
            }

            if (i >= tokens.Count) return null;

            return string.Join(" ", tokens.GetRange(i, tokens.Count - i));
        }
    }
}
