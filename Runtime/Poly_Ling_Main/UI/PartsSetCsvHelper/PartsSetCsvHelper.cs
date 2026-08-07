// PartsSetCsvHelper.cs
// パーツ選択辞書 CSV エクスポート / インポートの共通処理
// 旧 PartsSelectionSetPanel の CSV ロジックをそのまま移植

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.EditorBridge;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.UI
{
    public static class PartsSetCsvHelper
    {
        private enum CSVDataType { Vertex, VertexId, Edge, Face, Line }

        // ================================================================
        // エクスポート
        // ================================================================

        public static void ExportSets(MeshContext meshContext)
        {
            if (meshContext == null || meshContext.PartsSelectionSetList.Count == 0) return;

            string folderPath = PLEditorBridge.I.SaveFolderPanel(
                "Select Folder for CSV Export",
                Application.dataPath,
                $"SelectionSets_{meshContext.Name}");
            if (string.IsNullOrEmpty(folderPath)) { GUIUtility.ExitGUI(); return; }

            int count = ExportSetsToFolder(meshContext, folderPath);
            if (count >= 0)
                PLEditorBridge.I.DisplayDialog("Export Complete",
                    $"Exported {count} selection sets to:\n{folderPath}", "OK");
            else
                PLEditorBridge.I.DisplayDialog("Error", "Failed to export.", "OK");

            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// 指定フォルダへ選択辞書 CSV を書き出す（ダイアログなし）。
        /// ファイル名は Selected_&lt;オブジェクト名&gt;_&lt;辞書名&gt;.csv。
        /// 1 行目に "# object &lt;オブジェクト名&gt;"、2 行目に "# set &lt;辞書名&gt;" を出力する。
        /// UIToolkit（Player）から呼べるよう GUIUtility.ExitGUI() は使わない。
        /// </summary>
        /// <returns>書き出したファイル数。失敗時は -1</returns>
        public static int ExportSetsToFolder(MeshContext meshContext, string folderPath)
        {
            if (meshContext == null) return -1;
            return ExportSetsToFolder(new[] { meshContext }, folderPath);
        }

        /// <summary>
        /// 複数オブジェクトの選択辞書 CSV をまとめて書き出す（ダイアログなし）。
        /// </summary>
        /// <returns>書き出したファイル数。失敗時は -1</returns>
        public static int ExportSetsToFolder(IEnumerable<MeshContext> meshContexts, string folderPath)
        {
            if (meshContexts == null || string.IsNullOrEmpty(folderPath)) return -1;

            try
            {
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
                int count = 0;

                foreach (var meshContext in meshContexts)
                {
                    if (meshContext == null || meshContext.PartsSelectionSetList == null) continue;
                    string safeObj = SanitizeFileName(meshContext.Name ?? "Object");

                    foreach (var set in meshContext.PartsSelectionSetList)
                    {
                        string safeSet  = SanitizeFileName(set.Name);
                        string filePath = Path.Combine(folderPath, $"Selected_{safeObj}_{safeSet}.csv");

                        var lines = new List<string>();
                        lines.Add($"# object {meshContext.Name}");
                        lines.Add($"# set {set.Name}");

                        if (set.Vertices.Count > 0)
                        {
                            if (HasValidVertexIds(meshContext, set.Vertices))
                            {
                                lines.Add("# vertexId");
                                foreach (int vi in set.Vertices)
                                    lines.Add(GetVertexId(meshContext, vi).ToString());
                            }
                            else
                            {
                                lines.Add("# vertex");
                                foreach (int vi in set.Vertices) lines.Add(vi.ToString());
                            }
                        }
                        else if (set.Edges.Count > 0)
                        {
                            lines.Add("# edge");
                            foreach (var e in set.Edges) lines.Add($"{e.V1},{e.V2}");
                        }
                        else if (set.Faces.Count > 0)
                        {
                            lines.Add("# face");
                            foreach (int fi in set.Faces) lines.Add(fi.ToString());
                        }
                        else if (set.Lines.Count > 0)
                        {
                            lines.Add("# line");
                            foreach (int li in set.Lines) lines.Add(li.ToString());
                        }
                        else continue;

                        File.WriteAllLines(filePath, lines);
                        count++;
                    }
                }
                return count;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PartsSetCsvHelper] Export failed: {ex.Message}");
                return -1;
            }
        }

        // ================================================================
        // インポート
        // ================================================================

        public static void ImportSet(MeshContext meshContext)
        {
            if (meshContext == null) return;
            string filePath = PLEditorBridge.I.OpenFilePanel(
                "Import Selection Set CSV", Application.dataPath, "csv");
            if (string.IsNullOrEmpty(filePath)) { GUIUtility.ExitGUI(); return; }

            var added = ImportSetFromFile(meshContext, filePath);
            if (added == null)
                PLEditorBridge.I.DisplayDialog("Error", "Failed to import.", "OK");

            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// 指定 CSV ファイルから選択辞書を 1 件読み込み、既存リストへ追加する（ダイアログなし）。
        /// 名前が重複する場合はユニーク名を生成する（Editor のダイアログ経路と同じ挙動）。
        /// UIToolkit（Player）から呼べるよう GUIUtility.ExitGUI() は使わない。
        /// </summary>
        /// <returns>追加した辞書。失敗時は null</returns>
        public static PartsSelectionSet ImportSetFromFile(MeshContext meshContext, string filePath)
        {
            if (meshContext == null || string.IsNullOrEmpty(filePath)) return null;
            if (!File.Exists(filePath))
            {
                Debug.LogError($"[PartsSetCsvHelper] ファイルが見つかりません: {filePath}");
                return null;
            }

            var parsed = ParseFile(filePath);
            if (parsed == null) return null;

            var set = BuildSet(meshContext, parsed);
            if (set == null) return null;

            if (meshContext.FindSelectionSetByName(set.Name) != null)
                set.Name = meshContext.GenerateUniqueSelectionSetName(set.Name);
            meshContext.PartsSelectionSetList.Add(set);
            return set;
        }

        /// <summary>
        /// CSV ファイルのヘッダから書き出し元オブジェクト名を取り出す。
        /// 記載が無い場合は空文字を返す。
        /// </summary>
        public static string ReadFileHeader(string filePath)
        {
            var parsed = ParseFile(filePath);
            return parsed?.ObjectName ?? string.Empty;
        }

        /// <summary>
        /// フォルダ内の Selected_*.csv を一括読込する。
        /// 同名の辞書が既にある場合は上書きする。
        /// </summary>
        /// <param name="model">対象モデル（byObjectName == true のときの名前解決に使う）</param>
        /// <param name="folderPath">読込元フォルダ</param>
        /// <param name="byObjectName">
        /// true: ファイル内の "# object" 名と一致するオブジェクトへ読み込む。
        ///       一致するオブジェクトが無いファイルは無視する。
        /// false: オブジェクト名を無視し、targets のすべてへ同じ辞書を読み込む。
        /// </param>
        /// <param name="targets">byObjectName == false のときの読込先</param>
        /// <returns>読み込んだ辞書の適用件数。失敗時は -1</returns>
        public static int ImportSetsFromFolder(ModelContext model, string folderPath,
                                               bool byObjectName, IList<MeshContext> targets)
        {
            if (string.IsNullOrEmpty(folderPath)) return -1;
            if (!Directory.Exists(folderPath))
            {
                Debug.LogError($"[PartsSetCsvHelper] フォルダが見つかりません: {folderPath}");
                return -1;
            }
            if (byObjectName && model == null) return -1;
            if (!byObjectName && (targets == null || targets.Count == 0)) return -1;

            try
            {
                var files = Directory.GetFiles(folderPath, "Selected_*.csv");
                Array.Sort(files, StringComparer.Ordinal);

                int applied = 0;
                foreach (var file in files)
                {
                    var parsed = ParseFile(file);
                    if (parsed == null) continue;

                    if (byObjectName)
                    {
                        var mc = FindDrawableByName(model, parsed.ObjectName);
                        if (mc == null) continue;   // 同名オブジェクトが無ければ無視
                        if (ApplySet(mc, parsed)) applied++;
                    }
                    else
                    {
                        foreach (var mc in targets)
                        {
                            if (mc == null) continue;
                            if (ApplySet(mc, parsed)) applied++;
                        }
                    }
                }
                return applied;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PartsSetCsvHelper] Import failed: {ex.Message}");
                return -1;
            }
        }

        // ================================================================
        // 読込の内部処理
        // ================================================================

        /// <summary>ファイル 1 件分の解析結果。</summary>
        private class ParsedCsv
        {
            public string             ObjectName = string.Empty;
            public string             SetName    = string.Empty;
            public CSVDataType        Type       = CSVDataType.Vertex;
            public List<int>          Numbers    = new List<int>();
            public List<VertexPair>   Edges      = new List<VertexPair>();
        }

        /// <summary>CSV を解析する。失敗時は null。</summary>
        private static ParsedCsv ParseFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return null;

            try
            {
                string[] fileLines = File.ReadAllLines(filePath);
                if (fileLines.Length < 2) return null;

                var p = new ParsedCsv();
                bool firstComment = true;

                foreach (string line in fileLines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    if (trimmed.StartsWith("#"))
                    {
                        string comment = trimmed.Substring(1).Trim();

                        if (comment.Equals("vertex",   StringComparison.OrdinalIgnoreCase)) { p.Type = CSVDataType.Vertex;   firstComment = false; continue; }
                        if (comment.Equals("vertexId", StringComparison.OrdinalIgnoreCase)) { p.Type = CSVDataType.VertexId; firstComment = false; continue; }
                        if (comment.Equals("edge",     StringComparison.OrdinalIgnoreCase)) { p.Type = CSVDataType.Edge;     firstComment = false; continue; }
                        if (comment.Equals("face",     StringComparison.OrdinalIgnoreCase)) { p.Type = CSVDataType.Face;     firstComment = false; continue; }
                        if (comment.Equals("line",     StringComparison.OrdinalIgnoreCase)) { p.Type = CSVDataType.Line;     firstComment = false; continue; }

                        if (comment.StartsWith("object ", StringComparison.OrdinalIgnoreCase))
                        { p.ObjectName = comment.Substring(7).Trim(); firstComment = false; continue; }

                        if (comment.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
                        { p.SetName = comment.Substring(4).Trim(); firstComment = false; continue; }

                        // 旧形式（1 行目が "# <オブジェクト名>" のみ）への互換。
                        if (firstComment) p.ObjectName = comment;
                        firstComment = false;
                        continue;
                    }

                    if (p.Type == CSVDataType.Edge)
                    {
                        var parts = trimmed.Split(',');
                        if (parts.Length >= 2 &&
                            int.TryParse(parts[0].Trim(), out int v1) &&
                            int.TryParse(parts[1].Trim(), out int v2))
                            p.Edges.Add(new VertexPair(v1, v2));
                    }
                    else
                    {
                        if (int.TryParse(trimmed, out int n)) p.Numbers.Add(n);
                    }
                }

                // "# set" が無い旧形式はファイル名を辞書名として使う。
                if (string.IsNullOrEmpty(p.SetName))
                    p.SetName = Path.GetFileNameWithoutExtension(filePath);

                return p;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PartsSetCsvHelper] Parse failed ({Path.GetFileName(filePath)}): {ex.Message}");
                return null;
            }
        }

        /// <summary>解析結果から辞書を組み立てる（リストへは追加しない）。</summary>
        private static PartsSelectionSet BuildSet(MeshContext meshContext, ParsedCsv p)
        {
            if (meshContext == null || p == null) return null;

            var set = new PartsSelectionSet(p.SetName) { Mode = DataTypeToMode(p.Type) };
            switch (p.Type)
            {
                case CSVDataType.Vertex:   set.Vertices = new HashSet<int>(p.Numbers); break;
                case CSVDataType.VertexId:
                    var indices = ConvertVertexIdsToIndices(meshContext, p.Numbers);
                    set.Vertices = new HashSet<int>(indices);
                    if (indices.Count < p.Numbers.Count)
                        Debug.LogWarning($"[PartsSetCsvHelper] {p.Numbers.Count - indices.Count} vertex IDs not found. ({meshContext.Name})");
                    break;
                case CSVDataType.Edge:  set.Edges = new HashSet<VertexPair>(p.Edges);   break;
                case CSVDataType.Face:  set.Faces = new HashSet<int>(p.Numbers);        break;
                case CSVDataType.Line:  set.Lines = new HashSet<int>(p.Numbers);        break;
            }
            return set;
        }

        /// <summary>辞書を適用する。同名があれば上書きする。</summary>
        private static bool ApplySet(MeshContext meshContext, ParsedCsv p)
        {
            var set = BuildSet(meshContext, p);
            if (set == null) return false;

            var existing = meshContext.FindSelectionSetByName(set.Name);
            if (existing != null)
            {
                int idx = meshContext.PartsSelectionSetList.IndexOf(existing);
                meshContext.PartsSelectionSetList[idx] = set;
            }
            else
            {
                meshContext.PartsSelectionSetList.Add(set);
            }
            return true;
        }

        /// <summary>モデル内から描画メッシュを名前で探す。</summary>
        private static MeshContext FindDrawableByName(ModelContext model, string name)
        {
            if (model?.MeshContextList == null || string.IsNullOrEmpty(name)) return null;
            foreach (var mc in model.MeshContextList)
            {
                if (mc == null) continue;
                if (mc.Type != MeshType.Mesh && mc.Type != MeshType.BakedMirror) continue;
                if (mc.Name == name) return mc;
            }
            return null;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private static bool HasValidVertexIds(MeshContext ctx, HashSet<int> indices)
        {
            if (ctx?.MeshObject == null) return false;
            foreach (int idx in indices)
                if (idx >= 0 && idx < ctx.MeshObject.VertexCount && ctx.MeshObject.Vertices[idx].Id != 0)
                    return true;
            return false;
        }

        private static int GetVertexId(MeshContext ctx, int index)
        {
            if (ctx?.MeshObject == null || index < 0 || index >= ctx.MeshObject.VertexCount) return index;
            return ctx.MeshObject.Vertices[index].Id;
        }

        private static List<int> ConvertVertexIdsToIndices(MeshContext ctx, List<int> ids)
        {
            var result = new List<int>();
            if (ctx?.MeshObject == null) return result;
            var idToIndex = new Dictionary<int, int>();
            for (int i = 0; i < ctx.MeshObject.VertexCount; i++)
            {
                int id = ctx.MeshObject.Vertices[i].Id;
                if (!idToIndex.ContainsKey(id)) idToIndex[id] = i;
            }
            foreach (int id in ids)
                if (idToIndex.TryGetValue(id, out int idx)) result.Add(idx);
            return result;
        }

        private static MeshSelectMode DataTypeToMode(CSVDataType t) => t switch
        {
            CSVDataType.Vertex   => MeshSelectMode.Vertex,
            CSVDataType.VertexId => MeshSelectMode.Vertex,
            CSVDataType.Edge     => MeshSelectMode.Edge,
            CSVDataType.Face     => MeshSelectMode.Face,
            CSVDataType.Line     => MeshSelectMode.Line,
            _                    => MeshSelectMode.Vertex
        };
    }
}
