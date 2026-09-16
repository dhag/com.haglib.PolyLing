// StlParser.cs
// STL ファイル → StlDocument。ASCII とバイナリの両方を読む。
// Runtime/Poly_Ling_Main/STL/Import/ に配置
//
// 【形式の判定】
//   バイナリ: 80 バイトのヘッダ + 三角形数(uint32) + 三角形 × 50 バイト。
//   バイナリのヘッダが "solid" で始まるファイルが実在するため、先頭の文字列では
//   判定しない。ファイル長が 84 + 50 × 三角形数 に一致すればバイナリとする。
//   一致しなければ ASCII として読み、facet が 1 つも無ければ失敗にする。
//
// 【ASCII】
//   solid 名 … facet normal nx ny nz / outer loop / vertex x y z ×3 /
//   endloop / endfacet … endsolid 名。
//   solid〜endsolid が複数並ぶファイルは solid ごとに分ける。
//   solid 行の無い facet や、endsolid の無い末尾も受け付ける。
//   vertex が 4 個以上ある facet は扇形に分割する。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.STL
{
    public static class StlParser
    {
        private const int HeaderSize   = 80;
        private const int TriangleSize = 50;

        public static StlDocument ParseFile(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            string name  = Path.GetFileNameWithoutExtension(filePath);
            return Parse(bytes, name);
        }

        public static StlDocument Parse(byte[] bytes, string fileName)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            if (IsBinary(bytes))
                return ParseBinary(bytes, fileName);

            var doc = ParseAscii(bytes, fileName);
            if (doc.TriangleCount == 0)
                throw new InvalidDataException("STL として解釈できません（バイナリの長さが合わず、ASCII の facet もありません）");
            return doc;
        }

        // ================================================================
        // バイナリ
        // ================================================================

        private static bool IsBinary(byte[] bytes)
        {
            if (bytes.Length < HeaderSize + 4) return false;

            uint count = BitConverter.ToUInt32(bytes, HeaderSize);
            long expected = HeaderSize + 4L + TriangleSize * (long)count;
            return expected == bytes.Length;
        }

        private static StlDocument ParseBinary(byte[] bytes, string fileName)
        {
            var doc   = new StlDocument { FileName = fileName, IsBinary = true };
            var solid = new StlSolid { Name = fileName };
            doc.Solids.Add(solid);

            uint count = BitConverter.ToUInt32(bytes, HeaderSize);
            int offset = HeaderSize + 4;

            for (uint i = 0; i < count; i++)
            {
                var tri = new StlTriangle
                {
                    Normal = ReadVector(bytes, offset),
                    V0     = ReadVector(bytes, offset + 12),
                    V1     = ReadVector(bytes, offset + 24),
                    V2     = ReadVector(bytes, offset + 36),
                };
                // 末尾 2 バイトの属性は使わない。
                solid.Triangles.Add(tri);
                offset += TriangleSize;
            }

            return doc;
        }

        private static Vector3 ReadVector(byte[] bytes, int offset)
        {
            return new Vector3(
                BitConverter.ToSingle(bytes, offset),
                BitConverter.ToSingle(bytes, offset + 4),
                BitConverter.ToSingle(bytes, offset + 8));
        }

        // ================================================================
        // ASCII
        // ================================================================

        private static StlDocument ParseAscii(byte[] bytes, string fileName)
        {
            var doc = new StlDocument { FileName = fileName, IsBinary = false };

            // ASCII STL の数値とキーワードは ASCII の範囲。名前だけは UTF-8 として読む。
            string text = new UTF8Encoding(false).GetString(bytes);

            StlSolid solid   = null;
            Vector3  normal  = Vector3.zero;
            var      corners = new List<Vector3>(3);
            bool     inFacet = false;

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;

                    string keyword = FirstToken(trimmed, out string rest);

                    switch (keyword.ToLowerInvariant())
                    {
                        case "solid":
                            solid = new StlSolid { Name = string.IsNullOrEmpty(rest) ? fileName : rest };
                            doc.Solids.Add(solid);
                            break;

                        case "endsolid":
                            solid = null;
                            break;

                        case "facet":
                        {
                            if (solid == null)
                            {
                                solid = new StlSolid { Name = fileName };
                                doc.Solids.Add(solid);
                            }

                            inFacet = true;
                            corners.Clear();
                            normal = Vector3.zero;

                            // rest = "normal nx ny nz"
                            string sub = FirstToken(rest, out string nums);
                            if (sub.Equals("normal", StringComparison.OrdinalIgnoreCase))
                                TryParseVector(nums, out normal);
                            break;
                        }

                        case "vertex":
                        {
                            if (!inFacet) break;
                            if (TryParseVector(rest, out Vector3 v))
                                corners.Add(v);
                            break;
                        }

                        case "endfacet":
                        {
                            if (inFacet && solid != null && corners.Count >= 3)
                            {
                                for (int k = 1; k < corners.Count - 1; k++)
                                {
                                    solid.Triangles.Add(new StlTriangle
                                    {
                                        Normal = normal,
                                        V0     = corners[0],
                                        V1     = corners[k],
                                        V2     = corners[k + 1],
                                    });
                                }
                            }
                            inFacet = false;
                            corners.Clear();
                            break;
                        }

                        // outer loop / endloop は区切りだけなので読み飛ばす。
                    }
                }
            }

            // 三角形を 1 枚も持たない solid は捨てる。
            doc.Solids.RemoveAll(s => s.Triangles.Count == 0);
            return doc;
        }

        /// <summary>先頭の語を返し、残り（前後の空白を除いたもの）を rest に入れる。</summary>
        private static string FirstToken(string s, out string rest)
        {
            if (string.IsNullOrEmpty(s)) { rest = ""; return ""; }

            int i = 0;
            while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;

            string head = s.Substring(0, i);
            rest = i < s.Length ? s.Substring(i).Trim() : "";
            return head;
        }

        private static readonly char[] Separators = { ' ', '\t' };

        private static bool TryParseVector(string s, out Vector3 v)
        {
            v = Vector3.zero;
            if (string.IsNullOrEmpty(s)) return false;

            string[] parts = s.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) return false;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) return false;

            v = new Vector3(x, y, z);
            return true;
        }
    }
}
