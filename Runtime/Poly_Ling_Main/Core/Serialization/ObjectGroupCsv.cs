// ObjectGroupCsv.cs
// オブジェクトグループの CSV 本文（objectgroups.csv / scenarios.csv）を組み立て・読み取る。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置
//
// 【なぜ 1 か所にまとめるか】
//   同じ形のファイルを 2 か所が読み書きする。
//     モデルに属するグループ … CsvModelSerializer（モデルフォルダの objectgroups.csv）
//     手本のグループ         … ScenarioLibrary（persistentDataPath の scenarios.csv）
//   それぞれに構文解析を置くと、列を 1 つ足すたびに 2 か所を直すことになり、
//   必ず片方が取り残される。本文の組み立てと読み取りはここだけに置き、
//   ファイルの入出力は呼ぶ側が持つ。
//
// 【行の形】
//   g  … グループ 1 件の頭
//        g,name,action0,output0,stash,autoUpdate,sourceDigest,goal
//        action0 と output0 はステップ 0 の要約で、version 1.0 の読み手が
//        ここだけを見て 1 ステップのグループとして読めるようにしてある。
//   gp … 前提条件 1 件
//   gc … 成功条件 1 件
//   gt … 札 1 件
//   gv … 由来（gv,parentName,changeSummary,createdBy）
//   s  … ステップの頭（s,action,elementId,kind,purpose）。以降の o / a / r はこの段に付く
//   o  … その段の出力先 ObjectId 列
//   a  … パラメータ 1 件
//   r  … 描画オブジェクト参照 1 件（キーと ObjectId 列）
//
//   s の無いファイル（version 1.0）は、o / a / r が来た時点で g の控えから
//   ステップ 0 を作って読む。
//
// 【版】
//   1.0 … g / a / r のみ。1 グループ 1 コマンド。
//   1.1 … s / o を追加。ステップ列。
//   1.2 … g に goal、s に elementId / kind / purpose、gp / gc / gt / gv を追加。
//   足した列はすべて行の末尾なので、1.0 / 1.1 の本文もそのまま読める
//   （Split は行末の空欄を落とすため、列数は種別ごとに下限だけ見る）。
//
// 【並びを固定する】
//   Dictionary の列挙順は保証されない。書く前にキー順へ並べ替える
//   （ObjectGroupStep.SortedArgs / SortedMeshRefIds）。並べ替えないと
//   中身が同じでも保存のたびに差分が出る。
//
// 【参照は ObjectId のまま】
//   索引へは直さない。ObjectId はリスト位置にも名前にも依存しないので、
//   名前ベース保存でも変換が要らない。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Poly_Ling.Data;

namespace Poly_Ling.Serialization
{
    /// <summary>オブジェクトグループの CSV 本文を組み立て・読み取る。</summary>
    public static class ObjectGroupCsv
    {
        /// <summary>書き出す版。</summary>
        public const string Version = "1.2";

        // ================================================================
        // 書き
        // ================================================================

        /// <summary>グループ列を CSV 本文にする。</summary>
        /// <param name="header">先頭の # 行に入れる名前。</param>
        public static string Build(IEnumerable<ObjectGroup> groups, string header = "PolyLing_ObjectGroups")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"#{header},version,{Version}");

            if (groups == null) return sb.ToString();

            foreach (var g in groups)
            {
                if (g == null) continue;

                sb.AppendLine(
                    $"g,{Esc(g.Name ?? "")},{Esc(g.Action ?? "")}," +
                    $"{g.OutputObjectId},{g.StashObjectId}," +
                    $"{(g.AutoUpdate ? 1 : 0)},{Esc(g.SourceDigest ?? "")},{Esc(g.Goal ?? "")}");

                if (g.Preconditions != null)
                    foreach (string s in g.Preconditions)
                        if (!string.IsNullOrEmpty(s)) sb.AppendLine($"gp,{Esc(s)}");

                if (g.SuccessCriteria != null)
                    foreach (string s in g.SuccessCriteria)
                        if (!string.IsNullOrEmpty(s)) sb.AppendLine($"gc,{Esc(s)}");

                if (g.Tags != null)
                    foreach (string s in g.Tags)
                        if (!string.IsNullOrEmpty(s)) sb.AppendLine($"gt,{Esc(s)}");

                if (g.Provenance != null && !g.Provenance.IsEmpty)
                    sb.AppendLine(
                        $"gv,{Esc(g.Provenance.ParentName ?? "")}," +
                        $"{Esc(g.Provenance.ChangeSummary ?? "")}," +
                        $"{Esc(g.Provenance.CreatedBy ?? "")}");

                if (g.Steps == null) continue;

                foreach (var st in g.Steps)
                {
                    if (st == null) continue;

                    sb.AppendLine(
                        $"s,{Esc(st.Action ?? "")},{Esc(st.ElementId ?? "")}," +
                        $"{st.Kind},{Esc(st.Purpose ?? "")}");

                    if (st.OutputObjectIds != null && st.OutputObjectIds.Count > 0)
                    {
                        sb.Append("o");
                        foreach (ulong id in st.OutputObjectIds) sb.Append($",{id}");
                        sb.AppendLine();
                    }

                    foreach (var kv in st.SortedArgs())
                        sb.AppendLine($"a,{Esc(kv.Key)},{Esc(kv.Value ?? "")}");

                    foreach (var kv in st.SortedMeshRefIds())
                    {
                        sb.Append($"r,{Esc(kv.Key)}");
                        if (kv.Value != null)
                            foreach (ulong id in kv.Value) sb.Append($",{id}");
                        sb.AppendLine();
                    }
                }
            }

            return sb.ToString();
        }

        // ================================================================
        // 読み
        // ================================================================

        /// <summary>CSV の行列からグループ列を読む。壊れた行は捨てて次へ進む。</summary>
        public static List<ObjectGroup> Parse(IEnumerable<string> lines)
        {
            var result = new List<ObjectGroup>();
            if (lines == null) return result;

            ObjectGroup     cur     = null;
            ObjectGroupStep curStep = null;

            // s 行が無いファイル（version 1.0）のために、g 行の値を控えておく。
            string legacyAction = "";
            ulong  legacyOutput = 0UL;

            // s 行が無いまま o / a / r が来たら、g 行の控えからステップ 0 を作る。
            ObjectGroupStep EnsureStep()
            {
                if (curStep != null) return curStep;
                if (cur == null) return null;

                curStep = new ObjectGroupStep { Action = legacyAction };
                if (legacyOutput != 0UL) curStep.OutputObjectIds.Add(legacyOutput);
                cur.Steps.Add(curStep);
                return curStep;
            }

            // ステップが 1 つも書かれていないグループ（パラメータの無い 1 ステップ）。
            void CloseGroup()
            {
                if (cur == null) return;
                if (cur.Steps.Count == 0) EnsureStep();
                cur.EnsureElementIds();
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);

                // Split は行末の空欄を落とす（"s," は 1 列になる）。
                // action が空のステップがあるので、列数の下限は種別ごとに見る。
                if (cols.Length < 1) continue;

                switch (cols[0])
                {
                    case "g":
                        if (cols.Length < 2) break;
                        CloseGroup();

                        legacyAction = cols.Length > 2 ? Unesc(cols[2]) : "";
                        legacyOutput = PULong(cols, 3);

                        cur = new ObjectGroup(Unesc(cols[1]))
                        {
                            StashObjectId = PULong(cols, 4),
                            AutoUpdate    = PInt(cols, 5) != 0,
                            SourceDigest  = cols.Length > 6 ? Unesc(cols[6]) : "",
                            Goal          = cols.Length > 7 ? Unesc(cols[7]) : "",
                        };
                        cur.Steps.Clear();
                        curStep = null;
                        result.Add(cur);
                        break;

                    case "gp":
                        if (cur == null || cols.Length < 2) break;
                        cur.Preconditions.Add(Unesc(cols[1]));
                        break;

                    case "gc":
                        if (cur == null || cols.Length < 2) break;
                        cur.SuccessCriteria.Add(Unesc(cols[1]));
                        break;

                    case "gt":
                        if (cur == null || cols.Length < 2) break;
                        cur.Tags.Add(Unesc(cols[1]));
                        break;

                    case "gv":
                        if (cur == null) break;
                        cur.Provenance = new ObjectGroupProvenance
                        {
                            ParentName    = cols.Length > 1 ? Unesc(cols[1]) : "",
                            ChangeSummary = cols.Length > 2 ? Unesc(cols[2]) : "",
                            CreatedBy     = cols.Length > 3 ? Unesc(cols[3]) : "",
                        };
                        break;

                    case "s":
                        // 先頭が g でないファイルは壊れている。捨てて次へ。
                        if (cur == null) break;
                        curStep = new ObjectGroupStep
                        {
                            Action    = cols.Length > 1 ? Unesc(cols[1]) : "",
                            ElementId = cols.Length > 2 ? Unesc(cols[2]) : "",
                            Kind      = ParseKind(cols, 3),
                            Purpose   = cols.Length > 4 ? Unesc(cols[4]) : "",
                        };
                        cur.Steps.Add(curStep);
                        break;

                    case "o":
                    {
                        var st = EnsureStep();
                        if (st == null) break;
                        for (int i = 1; i < cols.Length; i++)
                        {
                            ulong id = PULong(cols, i);
                            if (id != 0UL) st.OutputObjectIds.Add(id);
                        }
                        break;
                    }

                    case "a":
                    {
                        if (cols.Length < 2) break;
                        var st = EnsureStep();
                        if (st == null) break;
                        st.SetArg(Unesc(cols[1]), cols.Length > 2 ? Unesc(cols[2]) : "");
                        break;
                    }

                    case "r":
                    {
                        if (cols.Length < 2) break;
                        var st = EnsureStep();
                        if (st == null) break;
                        var ids = new List<ulong>();
                        for (int i = 2; i < cols.Length; i++) ids.Add(PULong(cols, i));
                        st.SetMeshRefIds(Unesc(cols[1]), ids);
                        break;
                    }
                }
            }

            CloseGroup();
            return result;
        }

        // ================================================================
        // 列の読み
        // ================================================================

        /// <summary>
        /// 段の種別を読む。名前でも数字でも受ける。読めなければ実行する段とみなす
        /// （version 1.1 以前にはこの列が無く、全部が実行する段だった）。
        /// </summary>
        private static ObjectGroupStepKind ParseKind(string[] cols, int index)
        {
            if (cols == null || index < 0 || index >= cols.Length) return ObjectGroupStepKind.Command;
            string s = Unesc(cols[index]);
            if (string.IsNullOrEmpty(s)) return ObjectGroupStepKind.Command;
            return Enum.TryParse(s, ignoreCase: true, out ObjectGroupStepKind k)
                ? k : ObjectGroupStepKind.Command;
        }

        /// <summary>列を ulong として読む。読めなければ 0（＝参照なし）。</summary>
        private static ulong PULong(string[] cols, int index)
        {
            if (cols == null || index < 0 || index >= cols.Length) return 0UL;
            return ulong.TryParse(
                cols[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong v) ? v : 0UL;
        }

        /// <summary>列を int として読む。読めなければ 0。</summary>
        private static int PInt(string[] cols, int index)
        {
            if (cols == null || index < 0 || index >= cols.Length) return 0;
            return int.TryParse(
                cols[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
        }

        // ================================================================
        // CSV の 1 行
        // ================================================================

        /// <summary>区切り・引用・改行を含む値を引用符でくるむ。</summary>
        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0
                || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        /// <summary>Esc を戻す。</summary>
        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"')
                return s.Substring(1, s.Length - 2).Replace("\"\"", "\"");
            return s;
        }

        /// <summary>
        /// CSV の 1 行を列へ分ける。引用の中の区切りと二重引用を戻す。
        /// 行末の空欄は落ちる（"s," は 1 列）。読む側は列数の下限だけを見ること。
        /// </summary>
        private static string[] Split(string line)
        {
            var result = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                if (line[i] == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < line.Length)
                    {
                        if (line[i] == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; }
                            else { i++; break; }
                        }
                        else { sb.Append(line[i]); i++; }
                    }
                    result.Add(sb.ToString());
                    if (i < line.Length && line[i] == ',') i++;
                }
                else
                {
                    int start = i;
                    while (i < line.Length && line[i] != ',') i++;
                    result.Add(line.Substring(start, i - start));
                    if (i < line.Length) i++;
                }
            }
            return result.ToArray();
        }
    }
}
