// ProjectSyncDiff.cs
// 規約 4（CSV / JSON / バイナリ対称）の検査（QueryProjectSyncDiffCommand の実体）。
//
// 同じプロジェクトを 2 通りの JSON にして項目ごとに比べる。
//   A：保存の JSON（ProjectSerializer.FromProjectContext → ToJson）
//   B：もう一方の形式で往復させたものを、A と同じ方法で JSON にしたもの
//      via=binary … リモート送信と同じバイナリ（RemoteProgressiveSerializer.SerializeWholeProject）を
//                   受け側（RemoteProjectReceiver.ProcessBatch）で復元する。ファイルは介さない
//      via=csv    … 一時フォルダへ CSV 保存（CsvProjectSerializer.ExportToFile）して読み戻す
//                   （ImportFromFile）。一時フォルダは最後に消す
// B に無い・値が違う項目は、その形式が運んでいない（または読みで戻していない）項目である。
// 往復で作った Unity のオブジェクト（メッシュ・材質）は最後に破棄する。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Remote;
using Poly_Ling.Serialization;
using Poly_Ling.Serialization.FolderSerializer;

namespace Poly_Ling.Player
{
    internal static class ProjectSyncDiff
    {
        public struct Result
        {
            public string Report;
            public int    DiffCount;
            public int    Patterns;
            public int    JsonBytesA;
            public int    JsonBytesB;
            /// <summary>via=binary のときバイナリの大きさ。via=csv では 0。</summary>
            public int    BinaryBytes;
        }

        /// <summary>保存のたびに変わる項目。比べない（プロジェクト直下だけ）。</summary>
        private static readonly HashSet<string> VolatileRootKeys = new HashSet<string> { "createdAt", "modifiedAt" };

        private static readonly Regex IndexPattern = new Regex(@"\[\d+\]", RegexOptions.Compiled);

        /// <summary>
        /// via は "binary"（既定）か "csv"。
        /// 失敗時は例外を投げずに Report に理由を入れ、DiffCount を -1 にする。
        /// </summary>
        public static Result Run(ProjectContext project, int maxLinesPerPattern, string via)
        {
            var res = new Result { DiffCount = -1 };
            if (project == null) { res.Report = "プロジェクトがありません"; return res; }

            bool useCsv;
            if (string.IsNullOrEmpty(via) || via == "binary") useCsv = false;
            else if (via == "csv")                            useCsv = true;
            else { res.Report = $"via は binary / csv のどちらかです: {via}"; return res; }

            string jsonA = ProjectSerializer.ToJson(ProjectSerializer.FromProjectContext(project)) ?? "";
            res.JsonBytesA = Encoding.UTF8.GetByteCount(jsonA);

            string jsonB;
            string reason = useCsv
                ? RoundTripCsv(project, out jsonB)
                : RoundTripBinary(project, out jsonB, out res.BinaryBytes);
            if (reason != null) { res.Report = reason; return res; }
            res.JsonBytesB = Encoding.UTF8.GetByteCount(jsonB);

            var a = JToken.Parse(jsonA);
            var b = JToken.Parse(jsonB);
            if (a is JObject ao) foreach (var k in VolatileRootKeys) ao.Remove(k);
            if (b is JObject bo) foreach (var k in VolatileRootKeys) bo.Remove(k);

            // パターン（索引を [] にまとめたパス）→ 件数と例
            var counts   = new Dictionary<string, int>();
            var examples = new Dictionary<string, List<string>>();
            var order    = new List<string>();
            int total    = 0;

            void Add(string path, string detail)
            {
                total++;
                string pat = IndexPattern.Replace(path, "[]");
                if (!counts.TryGetValue(pat, out int c))
                {
                    c = 0;
                    order.Add(pat);
                    examples[pat] = new List<string>();
                }
                counts[pat] = c + 1;
                if (examples[pat].Count < maxLinesPerPattern)
                    examples[pat].Add($"{path}: {detail}");
            }

            Compare(a, b, "", Add);

            res.DiffCount = total;
            res.Patterns  = order.Count;
            if (total == 0) { res.Report = "差なし"; return res; }

            var sb = new StringBuilder();
            foreach (var pat in order)
            {
                sb.AppendLine($"[{counts[pat]}] {pat}");
                foreach (var ex in examples[pat]) sb.AppendLine("    " + ex);
            }
            res.Report = sb.ToString();
            return res;
        }

        // ================================================================
        // 往復
        // ================================================================

        private static string RoundTripBinary(ProjectContext project, out string json, out int binaryBytes)
        {
            json = null;
            binaryBytes = 0;

            byte[] bin = RemoteProgressiveSerializer.SerializeWholeProject(project);
            if (bin == null) return "バイナリを組めません";
            binaryBytes = bin.Length;

            var receiver = new RemoteProjectReceiver();
            try
            {
                receiver.ProcessBatch(bin);
                if (receiver.Project == null) return "バイナリを復元できません";
                json = ProjectSerializer.ToJson(ProjectSerializer.FromProjectContext(receiver.Project)) ?? "";
                return null;
            }
            finally
            {
                // 受け側で作ったメッシュ・材質・テクスチャを破棄する（描画には使っていない）。
                receiver.Reset();
            }
        }

        private static string RoundTripCsv(ProjectContext project, out string json)
        {
            json = null;
            string dir = Path.Combine(Application.temporaryCachePath, "PolyLingSyncDiff_" + Guid.NewGuid().ToString("N"));
            ProjectContext loaded = null;
            try
            {
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "project.csv");
                if (!CsvProjectSerializer.ExportToFile(path, project)) return "CSV に保存できません";

                loaded = CsvProjectSerializer.ImportFromFile(path, out _, out _);
                if (loaded == null) return "CSV を読み戻せません";
                json = ProjectSerializer.ToJson(ProjectSerializer.FromProjectContext(loaded)) ?? "";
                return null;
            }
            finally
            {
                DestroyRuntimeObjects(loaded);
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch (Exception ex) { Debug.LogWarning($"[queryProjectSyncDiff] 一時フォルダを消せません: {dir} {ex.Message}"); }
            }
        }

        /// <summary>読み戻しで作った Unity のメッシュと実行時の材質を破棄する（描画には使っていない）。</summary>
        private static void DestroyRuntimeObjects(ProjectContext p)
        {
            if (p?.Models == null) return;
            foreach (var m in p.Models)
            {
                if (m == null) continue;
                if (m.MeshContextList != null)
                    foreach (var mc in m.MeshContextList)
                        if (mc?.UnityMesh != null) mc.ReplaceUnityMesh(null);
                if (m.MaterialReferences != null)
                    foreach (var mr in m.MaterialReferences)
                        mr?.DestroyRuntimeMaterial();
            }
        }

        // ================================================================
        // 比較
        // ================================================================

        private static void Compare(JToken a, JToken b, string path, Action<string, string> add)
        {
            if (a == null && b == null) return;
            if (a == null) { add(path, "A に無い"); return; }
            if (b == null) { add(path, "B に無い（往復で落ちた）"); return; }

            if (a is JObject oa && b is JObject ob)
            {
                foreach (var p in oa.Properties())
                    Compare(p.Value, ob[p.Name], Join(path, p.Name), add);
                foreach (var p in ob.Properties())
                    if (oa[p.Name] == null) add(Join(path, p.Name), "A に無い（B にだけある）");
                return;
            }

            if (a is JArray la && b is JArray lb)
            {
                if (la.Count != lb.Count)
                    add(path, $"要素数 A={la.Count} B={lb.Count}");
                int n = Math.Min(la.Count, lb.Count);
                for (int i = 0; i < n; i++)
                    Compare(la[i], lb[i], $"{path}[{i}]", add);
                return;
            }

            if (a.Type != b.Type && !(IsNumber(a) && IsNumber(b)))
            {
                add(path, $"A={Short(a)} B={Short(b)}");
                return;
            }

            if (IsNumber(a) && IsNumber(b))
            {
                if (a.Value<double>() != b.Value<double>())
                    add(path, $"A={Short(a)} B={Short(b)}");
                return;
            }

            if (!JToken.DeepEquals(a, b))
                add(path, $"A={Short(a)} B={Short(b)}");
        }

        private static bool IsNumber(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;

        private static string Join(string path, string key) => string.IsNullOrEmpty(path) ? key : path + "." + key;

        private static string Short(JToken t)
        {
            string s = t.Type == JTokenType.String ? "\"" + (string)t + "\"" : t.ToString(Newtonsoft.Json.Formatting.None);
            return s.Length > 80 ? s.Substring(0, 77) + "..." : s;
        }
    }
}
