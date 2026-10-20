// FbxAsciiWriter.cs
// ============================================================
// FBX ノード木 → FBX 7.4 ASCII
// ------------------------------------------------------------
// 規則の正本は PolyLing_FBX対応計画.md 段 7。
//
// ■ 形
//   先頭に「; FBX 7.4.0 project file」。ノードは「名前: プロパティ, … {」子「}」。
//   文字列は "…"（" は &quot;）。オブジェクト名「名前\0\x01クラス」は「クラス::名前」。
//   数は不変カルチャ（double は "R"）。bool は 1 / 0。
//   配列は「*要素数 { a: v,v,… }」。バイト列（R）は Base64 の文字列（埋め込みはバイナリのときだけなので
//   ふつうは現れない）。
// ============================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Poly_Ling.Fbx
{
    internal static class FbxAsciiWriter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Write(string path, IList<FbxNode> top)
        {
            using (var sw = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                sw.NewLine = "\n";
                sw.WriteLine("; FBX 7.4.0 project file");
                sw.WriteLine("; ----------------------------------------------------");
                sw.WriteLine();
                foreach (var n in top) WriteNode(sw, n, 0);
            }
        }

        private static void WriteNode(StreamWriter sw, FbxNode n, int depth)
        {
            string indent = new string('\t', depth);
            var sb = new StringBuilder();
            sb.Append(indent).Append(n.Name).Append(": ");

            // 配列は 1 つだけ・最後に来る（FbxModelWriter の使い方）。配列のノードは子を持たない。
            object array = null;
            bool first = true;
            foreach (var p in n.Props)
            {
                if (p is Array && !(p is byte[])) { array = p; continue; }
                if (!first) sb.Append(", ");
                sb.Append(Scalar(p));
                first = false;
            }

            if (array != null)
            {
                var a = (Array)array;
                sb.Append('*').Append(a.Length).Append(" {");
                sw.WriteLine(sb.ToString());
                sw.Write(indent);
                sw.Write("\ta: ");
                WriteArrayBody(sw, a);
                sw.WriteLine();
                sw.Write(indent);
                sw.WriteLine("}");
                return;
            }

            if (n.HasBlock)
            {
                sb.Append(" {");
                sw.WriteLine(sb.ToString());
                if (n.Children != null)
                    foreach (var c in n.Children) WriteNode(sw, c, depth + 1);
                sw.Write(indent);
                sw.WriteLine("}");
            }
            else
            {
                sw.WriteLine(sb.ToString());
            }
        }

        private static string Scalar(object p)
        {
            switch (p)
            {
                case short v:  return v.ToString(Inv);
                case bool v:   return v ? "1" : "0";
                case int v:    return v.ToString(Inv);
                case float v:  return v.ToString("R", Inv);
                case double v: return v.ToString("R", Inv);
                case long v:   return v.ToString(Inv);
                case string v: return "\"" + Text(v) + "\"";
                case byte[] v: return "\"" + Convert.ToBase64String(v) + "\"";
                default:
                    throw new InvalidOperationException($"FBX に書けない型です: {p?.GetType().Name ?? "null"}");
            }
        }

        /// <summary>「名前\0\x01クラス」を「クラス::名前」に、" を &amp;quot; にする。</summary>
        private static string Text(string s)
        {
            int sep = s.IndexOf("\u0000\u0001", StringComparison.Ordinal);
            if (sep >= 0) s = s.Substring(sep + 2) + "::" + s.Substring(0, sep);
            return s.Replace("\"", "&quot;");
        }

        private static void WriteArrayBody(StreamWriter sw, Array a)
        {
            for (int i = 0; i < a.Length; i++)
            {
                if (i > 0) sw.Write(',');
                switch (a)
                {
                    case float[] f:  sw.Write(f[i].ToString("R", Inv)); break;
                    case double[] d: sw.Write(d[i].ToString("R", Inv)); break;
                    case long[] l:   sw.Write(l[i].ToString(Inv)); break;
                    case int[] n:    sw.Write(n[i].ToString(Inv)); break;
                    case bool[] b:   sw.Write(b[i] ? '1' : '0'); break;
                    default: throw new InvalidOperationException($"FBX に書けない配列です: {a.GetType().Name}");
                }
            }
        }
    }
}
