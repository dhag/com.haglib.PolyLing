// ObjParser.cs
// Wavefront OBJ テキスト → ObjDocument。
// Runtime/Poly_Ling_Main/OBJ/Import/ に配置
//
// 【対応する記法】
//   v  x y z [w]        位置（w は無視）
//   vt u [v] [w]        UV（w は無視。v 省略時は 0）
//   vn x y z            法線
//   f   コーナー列       v / v/vt / v//vn / v/vt/vn の 4 形式・N角形
//   l   コーナー列       折れ線（v または v/vt）
//   p   コーナー列       点（読み飛ばす）
//   o / g / s / usemtl / mtllib
//   #                   コメント
//   行末 \              次行へ継続
//
// 【索引】
//   OBJ の索引は 1 始まりで、負値は「その行までに出てきた要素の末尾からの相対」。
//   相対参照は後から解決できないため、行を読んだ時点で 0 始まりへ解決する。
//
// 【数値】
//   ロケール非依存（InvariantCulture）で解釈する。
//   壊れた数値は 0 として扱い、行そのものは捨てない（一部が壊れた実ファイルが多い）。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.OBJ
{
    public static class ObjParser
    {
        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>OBJ ファイルを読み込む。MTL は読み込まない（ObjImporter が行う）。</summary>
        public static ObjDocument ParseFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException($"OBJ ファイルが見つかりません: {filePath}");

            string text = File.ReadAllText(filePath, DetectEncoding(filePath));
            var doc = Parse(text);
            doc.FileName = Path.GetFileNameWithoutExtension(filePath);
            return doc;
        }

        /// <summary>OBJ テキストを読み込む。</summary>
        public static ObjDocument Parse(string text)
        {
            var doc = new ObjDocument();
            if (string.IsNullOrEmpty(text)) return doc;

            // 現在の状態（o / g / usemtl / s）。面へ畳んで持たせる。
            string currentObject   = null;
            string currentGroup    = null;
            int    currentMaterial = -1;
            int    currentSmooth   = 0;

            foreach (string rawLine in EnumerateLogicalLines(text))
            {
                string line = StripComment(rawLine).Trim();
                if (line.Length == 0) continue;

                string keyword = FirstToken(line, out string rest);

                switch (keyword)
                {
                    case "v":
                        doc.Positions.Add(ParseVector3(rest));
                        break;

                    case "vt":
                        doc.UVs.Add(ParseVector2(rest));
                        break;

                    case "vn":
                        doc.Normals.Add(ParseVector3(rest));
                        break;

                    case "f":
                    {
                        var face = ParseFace(rest, doc);
                        if (face == null || face.CornerCount < 2) break;

                        // 2 コーナー以下の f は面として成立しない。折れ線として拾う。
                        face.IsLine        = face.CornerCount < 3;
                        face.ObjectName    = currentObject;
                        face.GroupName     = currentGroup;
                        face.MaterialIndex = currentMaterial;
                        face.SmoothingGroup = currentSmooth;
                        doc.Faces.Add(face);
                        break;
                    }

                    case "l":
                    {
                        var line2 = ParseFace(rest, doc);
                        if (line2 == null || line2.CornerCount < 2) break;

                        line2.IsLine        = true;
                        line2.ObjectName    = currentObject;
                        line2.GroupName     = currentGroup;
                        line2.MaterialIndex = currentMaterial;
                        line2.SmoothingGroup = currentSmooth;
                        doc.Faces.Add(line2);
                        break;
                    }

                    case "o":
                        currentObject = rest.Trim();
                        if (currentObject.Length == 0) currentObject = null;
                        if (currentObject != null) doc.HasObjectNames = true;
                        break;

                    case "g":
                    {
                        // g は空白区切りで複数名を書ける。先頭だけを採る。
                        string g = FirstToken(rest.Trim(), out _);
                        currentGroup = string.IsNullOrEmpty(g) ? null : g;
                        if (currentGroup != null &&
                            !string.Equals(currentGroup, "default", StringComparison.OrdinalIgnoreCase))
                            doc.HasGroupNames = true;
                        break;
                    }

                    case "s":
                    {
                        string s = rest.Trim();
                        if (s.Length == 0 ||
                            string.Equals(s, "off", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(s, "0",   StringComparison.Ordinal))
                            currentSmooth = 0;
                        else if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out currentSmooth))
                            currentSmooth = 0;
                        break;
                    }

                    case "usemtl":
                    {
                        string name = rest.Trim();
                        currentMaterial = doc.IndexOfMaterial(name);
                        if (currentMaterial < 0 && name.Length > 0)
                        {
                            // MTL より先に usemtl が現れる場合がある。名前だけの器を作り、
                            // MTL 読み込み時に同名で埋める（ObjImporter.MergeMaterials）。
                            doc.Materials.Add(new ObjMaterial { Name = name });
                            currentMaterial = doc.Materials.Count - 1;
                        }
                        break;
                    }

                    case "mtllib":
                    {
                        // mtllib は空白区切りで複数書ける。空白入りファイル名は区別できないため、
                        // まず全体を 1 個として扱い、存在しなければ分割して試す（Importer 側）。
                        string libs = rest.Trim();
                        if (libs.Length > 0 && !doc.MtlLibs.Contains(libs))
                            doc.MtlLibs.Add(libs);
                        break;
                    }

                    default:
                        // p / vp / curv / surf / mg / bevel / c_interp などは読み飛ばす。
                        break;
                }
            }

            return doc;
        }

        // ================================================================
        // 行の取り出し
        // ================================================================

        /// <summary>
        /// 行末の \ による継続を畳んで論理行を列挙する。
        /// CR / LF / CRLF のいずれにも対応する。
        /// </summary>
        private static IEnumerable<string> EnumerateLogicalLines(string text)
        {
            var sb = new StringBuilder();
            int i = 0;
            int n = text.Length;

            while (i < n)
            {
                int lineStart = i;
                while (i < n && text[i] != '\n' && text[i] != '\r') i++;

                string line = text.Substring(lineStart, i - lineStart);

                // 改行を 1 つ読み飛ばす（CRLF は 2 文字で 1 改行）
                if (i < n && text[i] == '\r') i++;
                if (i < n && text[i] == '\n') i++;

                string trimmedEnd = line.TrimEnd();
                if (trimmedEnd.EndsWith("\\", StringComparison.Ordinal))
                {
                    sb.Append(trimmedEnd, 0, trimmedEnd.Length - 1);
                    sb.Append(' ');
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(line);
                    yield return sb.ToString();
                    sb.Length = 0;
                }
                else
                {
                    yield return line;
                }
            }

            if (sb.Length > 0)
                yield return sb.ToString();
        }

        /// <summary># 以降を落とす。</summary>
        private static string StripComment(string line)
        {
            int idx = line.IndexOf('#');
            return idx < 0 ? line : line.Substring(0, idx);
        }

        /// <summary>先頭トークンを返し、残りを rest に入れる。</summary>
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

        private static bool IsSpace(char c) => c == ' ' || c == '\t';

        // ================================================================
        // 値のパース
        // ================================================================

        private static Vector3 ParseVector3(string s)
        {
            SplitNumbers(s, 3, out float a, out float b, out float c);
            return new Vector3(a, b, c);
        }

        private static Vector2 ParseVector2(string s)
        {
            SplitNumbers(s, 2, out float a, out float b, out _);
            return new Vector2(a, b);
        }

        /// <summary>空白区切りの数値を最大 3 個まで取り出す。足りない分は 0。</summary>
        private static void SplitNumbers(string s, int count, out float a, out float b, out float c)
        {
            a = 0f; b = 0f; c = 0f;

            int i = 0;
            int n = s?.Length ?? 0;
            int found = 0;

            while (i < n && found < count)
            {
                while (i < n && IsSpace(s[i])) i++;
                if (i >= n) break;

                int start = i;
                while (i < n && !IsSpace(s[i])) i++;

                float v = ParseFloat(s.Substring(start, i - start));
                if      (found == 0) a = v;
                else if (found == 1) b = v;
                else                 c = v;
                found++;
            }
        }

        private static float ParseFloat(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0f;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }

        // ================================================================
        // 面のパース
        // ================================================================

        /// <summary>
        /// f / l の残り部分をコーナー列へ変換する。
        /// 索引は 1 始まり／負値の相対参照のいずれも 0 始まりへ解決する。
        /// 範囲外を指すコーナーは捨てる（面ごと捨てない）。
        /// </summary>
        private static ObjFace ParseFace(string s, ObjDocument doc)
        {
            if (string.IsNullOrEmpty(s)) return null;

            var face = new ObjFace();

            int i = 0;
            int n = s.Length;

            while (i < n)
            {
                while (i < n && IsSpace(s[i])) i++;
                if (i >= n) break;

                int start = i;
                while (i < n && !IsSpace(s[i])) i++;
                string token = s.Substring(start, i - start);

                if (!TryParseCorner(token, doc, out ObjCorner corner)) continue;
                face.Corners.Add(corner);
            }

            return face.CornerCount > 0 ? face : null;
        }

        /// <summary>"v"、"v/vt"、"v//vn"、"v/vt/vn" を解決する。</summary>
        private static bool TryParseCorner(string token, ObjDocument doc, out ObjCorner corner)
        {
            corner = new ObjCorner(-1, -1, -1);
            if (string.IsNullOrEmpty(token)) return false;

            string sv = token, svt = null, svn = null;

            int slash1 = token.IndexOf('/');
            if (slash1 >= 0)
            {
                sv = token.Substring(0, slash1);
                int slash2 = token.IndexOf('/', slash1 + 1);
                if (slash2 >= 0)
                {
                    svt = token.Substring(slash1 + 1, slash2 - slash1 - 1);
                    svn = token.Substring(slash2 + 1);
                }
                else
                {
                    svt = token.Substring(slash1 + 1);
                }
            }

            int v = ResolveIndex(sv, doc.Positions.Count);
            if (v < 0) return false;

            corner = new ObjCorner(
                v,
                ResolveIndex(svt, doc.UVs.Count),
                ResolveIndex(svn, doc.Normals.Count));
            return true;
        }

        /// <summary>
        /// OBJ の索引（1 始まり／負値は末尾からの相対）を 0 始まりへ解決する。
        /// 空文字・解釈不能・範囲外は -1。
        /// </summary>
        private static int ResolveIndex(string s, int count)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int raw)) return -1;
            if (raw == 0) return -1;

            int idx = raw > 0 ? raw - 1 : count + raw;
            return (idx >= 0 && idx < count) ? idx : -1;
        }

        // ================================================================
        // エンコーディング
        // ================================================================

        /// <summary>
        /// BOM があればそれに従い、無ければ UTF-8 として読む。
        /// OBJ は ASCII が基本で、名前に非 ASCII が入るのはまれ。
        /// </summary>
        private static Encoding DetectEncoding(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    var bom = new byte[3];
                    int read = fs.Read(bom, 0, 3);

                    if (read >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
                        return new UTF8Encoding(true);
                    if (read >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
                        return Encoding.Unicode;
                    if (read >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
                        return Encoding.BigEndianUnicode;
                }
            }
            catch (IOException)
            {
                // 読めない場合は既定へ倒す
            }

            return new UTF8Encoding(false);
        }
    }
}
