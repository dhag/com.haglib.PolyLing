// CsvSelectModeFormat.cs
// 選択セット行（ss / nx / s）の選択モード欄の書式。
//
// MeshSelectMode はフラグ列挙で、複数立っていると ToString() が "Vertex, Face" のように
// カンマ入りの文字列になる。これをそのまま CSV の 1 欄に書くと列がずれ、
// 読み戻すと頂点数以降がすべて崩れていた。
// 書くときは区切りを '|' にして 1 欄に収める（"Vertex|Face"）。
// 読むときは '|' 区切りと、旧ファイルの「カンマで欄が割れた」書式の両方を受ける。

using System;
using Poly_Ling.Selection;

namespace Poly_Ling.Serialization.FolderSerializer
{
    public static class CsvSelectModeFormat
    {
        /// <summary>選択モードを CSV の 1 欄に収まる文字列にする。</summary>
        public static string Format(MeshSelectMode mode)
        {
            return mode.ToString().Replace(", ", "|");
        }

        /// <summary>
        /// cols[idx] から選択モードを読み、idx を次の欄へ進める。
        /// 旧ファイルでは "Vertex, Face" がカンマで割れて " Face" が次の欄に来ているので、
        /// 続く欄が列挙子の名前である間はモードの続きとして取り込む
        /// （続く本来の欄は個数なので数字であり、名前と取り違えることはない）。
        /// 読めなかったときは false を返し、mode は既定値のまま。
        /// </summary>
        public static bool Read(string[] cols, ref int idx, out MeshSelectMode mode)
        {
            mode = default;
            if (cols == null || idx < 0 || idx >= cols.Length) { idx++; return false; }

            string text = cols[idx].Trim().Replace('|', ',');
            idx++;

            while (idx < cols.Length && IsModeName(cols[idx]))
            {
                text += "," + cols[idx].Trim();
                idx++;
            }

            return Enum.TryParse(text, out mode);
        }

        private static bool IsModeName(string col)
        {
            if (string.IsNullOrEmpty(col)) return false;
            string t = col.Trim();
            if (t.Length == 0 || !char.IsLetter(t[0])) return false;
            return Enum.IsDefined(typeof(MeshSelectMode), t);
        }
    }
}
