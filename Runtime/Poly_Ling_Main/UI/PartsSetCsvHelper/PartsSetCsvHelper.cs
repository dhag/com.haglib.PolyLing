// PartsSetCsvHelper.cs
// パーツ選択辞書 CSV エクスポート / インポートの共通処理
// 旧 PartsSelectionSetPanel の CSV ロジックをそのまま移植

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.EditorBridge;
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

            try
            {
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
                int count = 0;
                foreach (var set in meshContext.PartsSelectionSetList)
                {
                    string safeName = SanitizeFileName(set.Name);
                    string filePath = Path.Combine(folderPath, $"Selected_{safeName}.csv");
                    var lines = new List<string>();
                    lines.Add($"# {meshContext.Name}");

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
                PLEditorBridge.I.DisplayDialog("Export Complete",
                    $"Exported {count} selection sets to:\n{folderPath}", "OK");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PartsSetCsvHelper] Export failed: {ex.Message}");
                PLEditorBridge.I.DisplayDialog("Error", $"Failed to export:\n{ex.Message}", "OK");
            }
            GUIUtility.ExitGUI();
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

            try
            {
                string[] fileLines = File.ReadAllLines(filePath);
                if (fileLines.Length < 2) return;

                string setName = Path.GetFileNameWithoutExtension(filePath);
                var dataType = CSVDataType.Vertex;
                var numbers = new List<int>();
                var edges = new List<VertexPair>();

                foreach (string line in fileLines)
                {
                    string trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    if (trimmed.StartsWith("#"))
                    {
                        string comment = trimmed.Substring(1).Trim();
                        if (comment.Equals("vertex",   StringComparison.OrdinalIgnoreCase)) dataType = CSVDataType.Vertex;
                        else if (comment.Equals("vertexId", StringComparison.OrdinalIgnoreCase)) dataType = CSVDataType.VertexId;
                        else if (comment.Equals("edge",     StringComparison.OrdinalIgnoreCase)) dataType = CSVDataType.Edge;
                        else if (comment.Equals("face",     StringComparison.OrdinalIgnoreCase)) dataType = CSVDataType.Face;
                        else if (comment.Equals("line",     StringComparison.OrdinalIgnoreCase)) dataType = CSVDataType.Line;
                        continue;
                    }
                    if (dataType == CSVDataType.Edge)
                    {
                        var parts = trimmed.Split(',');
                        if (parts.Length >= 2 &&
                            int.TryParse(parts[0].Trim(), out int v1) &&
                            int.TryParse(parts[1].Trim(), out int v2))
                            edges.Add(new VertexPair(v1, v2));
                    }
                    else
                    {
                        if (int.TryParse(trimmed, out int n)) numbers.Add(n);
                    }
                }

                var set = new PartsSelectionSet(setName) { Mode = DataTypeToMode(dataType) };
                switch (dataType)
                {
                    case CSVDataType.Vertex:   set.Vertices = new HashSet<int>(numbers); break;
                    case CSVDataType.VertexId:
                        var indices = ConvertVertexIdsToIndices(meshContext, numbers);
                        set.Vertices = new HashSet<int>(indices);
                        if (indices.Count < numbers.Count)
                            Debug.LogWarning($"[PartsSetCsvHelper] {numbers.Count - indices.Count} vertex IDs not found.");
                        break;
                    case CSVDataType.Edge:  set.Edges = new HashSet<VertexPair>(edges); break;
                    case CSVDataType.Face:  set.Faces = new HashSet<int>(numbers); break;
                    case CSVDataType.Line:  set.Lines = new HashSet<int>(numbers); break;
                }

                if (meshContext.FindSelectionSetByName(set.Name) != null)
                    set.Name = meshContext.GenerateUniqueSelectionSetName(set.Name);
                meshContext.PartsSelectionSetList.Add(set);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PartsSetCsvHelper] Import failed: {ex.Message}");
                PLEditorBridge.I.DisplayDialog("Error", $"Failed to import:\n{ex.Message}", "OK");
            }
            GUIUtility.ExitGUI();
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
