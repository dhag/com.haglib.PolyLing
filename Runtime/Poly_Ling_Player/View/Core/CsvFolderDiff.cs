// CsvFolderDiff.cs
// 2 つの CSV 保存フォルダを 1 行ずつ比べる（QueryCsvFolderDiffCommand の実体）。
// シリアライザの往復確認（PolyLing_追加作業の必読.md の E）で、
// 「CSV 保存 → バイナリ保存 → バイナリ読込 → CSV 保存」の前後を比べるために使う。
// ファイルの大きさが同じでも中身が違うことがある（例：-1 と 0、True と False の差が打ち消し合う）ので、
// 大きさではなく行で比べる。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Poly_Ling.Player
{
    internal static class CsvFolderDiff
    {
        /// <summary>保存のたびに変わる行。比べない。</summary>
        private static bool IsVolatile(string line)
            => line.StartsWith("createdAt,", StringComparison.Ordinal)
            || line.StartsWith("modifiedAt,", StringComparison.Ordinal);

        public static string Run(string dirA, string dirB, int maxLines, out int diffFiles, out int diffLines)
        {
            diffFiles = 0;
            diffLines = 0;
            var sb = new StringBuilder();

            var filesA = ListFiles(dirA);
            var filesB = ListFiles(dirB);

            foreach (var rel in filesA)
            {
                if (!filesB.Contains(rel))
                {
                    diffFiles++;
                    sb.AppendLine($"[B に無い] {rel}");
                    continue;
                }
                if (!rel.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;

                var a = File.ReadAllLines(Path.Combine(dirA, rel), Encoding.UTF8);
                var b = File.ReadAllLines(Path.Combine(dirB, rel), Encoding.UTF8);

                int n = Math.Max(a.Length, b.Length);
                int fileDiff = 0;
                var examples = new StringBuilder();
                for (int i = 0; i < n; i++)
                {
                    string la = i < a.Length ? a[i] : "(無し)";
                    string lb = i < b.Length ? b[i] : "(無し)";
                    if (la == lb) continue;
                    if (IsVolatile(la) && IsVolatile(lb)) continue;

                    fileDiff++;
                    if (fileDiff <= maxLines)
                        examples.AppendLine($"    {i + 1}: A={Clip(la)}\n    {i + 1}: B={Clip(lb)}");
                }

                if (a.Length != b.Length)
                    examples.AppendLine($"    行数 A={a.Length} B={b.Length}");

                if (fileDiff > 0)
                {
                    diffFiles++;
                    diffLines += fileDiff;
                    sb.AppendLine($"[差 {fileDiff} 行] {rel}");
                    sb.Append(examples);
                }
            }

            foreach (var rel in filesB)
            {
                if (filesA.Contains(rel)) continue;
                diffFiles++;
                sb.AppendLine($"[A に無い] {rel}");
            }

            return sb.Length == 0 ? "差なし" : sb.ToString();
        }

        private static HashSet<string> ListFiles(string dir)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                set.Add(f.Substring(dir.Length).TrimStart('\\', '/'));
            return set;
        }

        private static string Clip(string s) => s.Length <= 160 ? s : s.Substring(0, 160) + "…";
    }
}
