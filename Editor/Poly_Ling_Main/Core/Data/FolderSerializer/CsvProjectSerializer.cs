// Assets/Editor/Poly_Ling/Serialization/FolderSerializer/CsvProjectSerializer.cs
// プロジェクトフォルダ全体の読み書き
// ProjectFolder/
//   project.csv
//   ModelName/
//     model.csv, materials.csv, humanoid.csv, morphgroups.csv,
//     editorstate.csv, workplane.csv,
//     ModelName.mesh.csv, ModelName.bone.csv, ModelName.morph.csv

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.Serialization;

namespace Poly_Ling.Serialization.FolderSerializer
{
    /// <summary>
    /// プロジェクトフォルダのCSVシリアライザ
    /// </summary>
    public static class CsvProjectSerializer
    {
        public const string ProjectFileName = "project.csv";
        public const string CurrentVersion = "1.0";

        // ================================================================
        // Export: ダイアログ付き
        // ================================================================

        /// <summary>
        /// フォルダ選択ダイアログを表示してプロジェクトをエクスポート
        /// </summary>
        public static bool ExportWithDialog(
            ProjectContext project,
            List<WorkPlaneContext> workPlanes = null,
            List<EditorStateDTO> editorStates = null,
            string defaultName = "Project")
        {
            string folderPath = EditorUtility.SaveFolderPanel(
                "Export Project Folder",
                Application.dataPath,
                defaultName
            );

            if (string.IsNullOrEmpty(folderPath))
                return false;

            return Export(folderPath, project, workPlanes, editorStates);
        }

        /// <summary>
        /// 単一ModelContextをフォルダ選択ダイアログ付きでエクスポート
        /// </summary>
        public static bool ExportModelWithDialog(
            ModelContext model,
            WorkPlaneContext workPlane = null,
            EditorStateDTO editorState = null,
            string defaultName = "Project")
        {
            string folderPath = EditorUtility.SaveFolderPanel(
                "Export Project Folder",
                Application.dataPath,
                defaultName
            );

            if (string.IsNullOrEmpty(folderPath))
                return false;

            // 単一モデルのProjectContextを構築
            var project = new ProjectContext(model.Name ?? defaultName);
            project.Models.Clear();
            project.Models.Add(model);

            var workPlanes = workPlane != null ? new List<WorkPlaneContext> { workPlane } : null;
            var editorStates = editorState != null ? new List<EditorStateDTO> { editorState } : null;

            return Export(folderPath, project, workPlanes, editorStates);
        }

        // ================================================================
        // Import: ダイアログ付き
        // ================================================================

        /// <summary>
        /// フォルダ選択ダイアログを表示してプロジェクトをインポート
        /// </summary>
        public static ProjectContext ImportWithDialog(
            out List<EditorStateDTO> editorStates,
            out List<WorkPlaneContext> workPlanes)
        {
            string folderPath = EditorUtility.OpenFolderPanel(
                "Import Project Folder",
                Application.dataPath,
                ""
            );

            editorStates = null;
            workPlanes = null;

            if (string.IsNullOrEmpty(folderPath))
                return null;

            return Import(folderPath, out editorStates, out workPlanes);
        }

        /// <summary>
        /// フォルダ選択ダイアログで単一ModelContextをインポート
        /// </summary>
        public static ModelContext ImportModelWithDialog(
            out EditorStateDTO editorState,
            out WorkPlaneContext workPlane,
            out List<CsvMeshEntry> additionalEntries)
        {
            editorState = null;
            workPlane = null;
            additionalEntries = new List<CsvMeshEntry>();

            string folderPath = EditorUtility.OpenFolderPanel(
                "Import Project Folder",
                Application.dataPath,
                ""
            );

            if (string.IsNullOrEmpty(folderPath))
                return null;

            // project.csv があればプロジェクトとして読み込み（追加エントリは無視）
            string projectCsvPath = Path.Combine(folderPath, ProjectFileName);
            if (File.Exists(projectCsvPath))
            {
                var project = Import(folderPath, out var editorStates, out var workPlanes);
                if (project == null || project.ModelCount == 0)
                    return null;

                editorState = editorStates != null && editorStates.Count > 0 ? editorStates[0] : null;
                workPlane = workPlanes != null && workPlanes.Count > 0 ? workPlanes[0] : null;
                return project.Models[0];
            }

            // model.csv があれば単一モデルフォルダとして読み込み（追加エントリあり）
            string modelCsvPath = Path.Combine(folderPath, "model.csv");
            if (File.Exists(modelCsvPath))
            {
                var model = CsvModelSerializer.LoadModel(folderPath, out editorState, out workPlane, out additionalEntries);
                return model;
            }

            Debug.LogError($"[CsvProjectSerializer] No project.csv or model.csv found in: {folderPath}");
            return null;
        }

        // ================================================================
        // Export: 本体
        // ================================================================

        /// <summary>
        /// プロジェクトをフォルダに保存
        /// </summary>
        public static bool Export(
            string projectFolderPath,
            ProjectContext project,
            List<WorkPlaneContext> workPlanes = null,
            List<EditorStateDTO> editorStates = null)
        {
            if (string.IsNullOrEmpty(projectFolderPath) || project == null)
                return false;

            try
            {
                Directory.CreateDirectory(projectFolderPath);

                // project.csv
                WriteProjectCsv(projectFolderPath, project);

                // 各モデルフォルダ
                for (int i = 0; i < project.ModelCount; i++)
                {
                    var model = project.Models[i];
                    if (model == null) continue;

                    string modelName = SanitizeFileName(model.Name ?? $"Model_{i}");
                    string modelFolderPath = Path.Combine(projectFolderPath, modelName);

                    var wp = workPlanes != null && i < workPlanes.Count ? workPlanes[i] : null;
                    var es = editorStates != null && i < editorStates.Count ? editorStates[i] : null;

                    CsvModelSerializer.SaveModel(modelFolderPath, model, es, wp);
                }

                Debug.Log($"[CsvProjectSerializer] Exported to: {projectFolderPath} ({project.ModelCount} models)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CsvProjectSerializer] Export failed: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        // ================================================================
        // Import: 本体
        // ================================================================

        /// <summary>
        /// フォルダからプロジェクトを読み込み
        /// </summary>
        public static ProjectContext Import(
            string projectFolderPath,
            out List<EditorStateDTO> editorStates,
            out List<WorkPlaneContext> workPlanes)
        {
            editorStates = new List<EditorStateDTO>();
            workPlanes = new List<WorkPlaneContext>();

            if (string.IsNullOrEmpty(projectFolderPath) || !Directory.Exists(projectFolderPath))
            {
                Debug.LogError($"[CsvProjectSerializer] Folder not found: {projectFolderPath}");
                return null;
            }

            try
            {
                // project.csv 読み込み
                string projectCsvPath = Path.Combine(projectFolderPath, ProjectFileName);
                if (!File.Exists(projectCsvPath))
                {
                    // project.csv がない場合、フォルダ直下に model.csv があるか試みる
                    // （単一モデルフォルダを直接指定した場合のフォールバック）
                    string modelCsv = Path.Combine(projectFolderPath, "model.csv");
                    if (File.Exists(modelCsv))
                    {
                        return ImportSingleModelFolder(projectFolderPath, out editorStates, out workPlanes);
                    }

                    Debug.LogError($"[CsvProjectSerializer] project.csv not found in: {projectFolderPath}");
                    return null;
                }

                var (projectName, currentModelIndex, modelFolders) = ReadProjectCsv(projectCsvPath);

                var project = new ProjectContext(projectName);
                project.Models.Clear();

                foreach (var folder in modelFolders)
                {
                    string modelFolderPath = Path.Combine(projectFolderPath, folder);
                    if (!Directory.Exists(modelFolderPath))
                    {
                        Debug.LogWarning($"[CsvProjectSerializer] Model folder not found: {modelFolderPath}");
                        continue;
                    }

                    var model = CsvModelSerializer.LoadModel(modelFolderPath, out var es, out var wp, out _);
                    if (model != null)
                    {
                        project.Models.Add(model);
                        editorStates.Add(es);
                        workPlanes.Add(wp);
                    }
                }

                if (project.Models.Count == 0)
                {
                    project.Models.Add(new ModelContext("Model"));
                    editorStates.Add(null);
                    workPlanes.Add(null);
                }

                project.CurrentModelIndex = Mathf.Clamp(currentModelIndex, 0, project.ModelCount - 1);

                Debug.Log($"[CsvProjectSerializer] Imported: {projectFolderPath} ({project.ModelCount} models)");
                return project;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CsvProjectSerializer] Import failed: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// 単一モデルフォルダからProjectContextを構築（フォールバック）
        /// </summary>
        private static ProjectContext ImportSingleModelFolder(
            string folderPath,
            out List<EditorStateDTO> editorStates,
            out List<WorkPlaneContext> workPlanes)
        {
            editorStates = new List<EditorStateDTO>();
            workPlanes = new List<WorkPlaneContext>();

            var model = CsvModelSerializer.LoadModel(folderPath, out var es, out var wp, out _);
            if (model == null)
            {
                editorStates.Add(null);
                workPlanes.Add(null);
                return null;
            }

            var project = new ProjectContext(model.Name ?? "Imported");
            project.Models.Clear();
            project.Models.Add(model);
            editorStates.Add(es);
            workPlanes.Add(wp);

            return project;
        }

        // ================================================================
        // project.csv 読み書き
        // ================================================================

        private static void WriteProjectCsv(string folderPath, ProjectContext project)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#PolyLing,version,1.0");
            sb.AppendLine($"name,{Esc(project.Name)}");
            sb.AppendLine($"createdAt,{DateTime.Now:o}");
            sb.AppendLine($"modifiedAt,{DateTime.Now:o}");
            sb.AppendLine($"currentModelIndex,{project.CurrentModelIndex}");
            sb.AppendLine($"modelCount,{project.ModelCount}");

            for (int i = 0; i < project.ModelCount; i++)
            {
                var model = project.Models[i];
                string folderName = SanitizeFileName(model?.Name ?? $"Model_{i}");
                sb.AppendLine($"model,{i},{Esc(folderName)}");
            }

            File.WriteAllText(Path.Combine(folderPath, ProjectFileName), sb.ToString(), Encoding.UTF8);
        }

        private static (string projectName, int currentModelIndex, List<string> modelFolders) ReadProjectCsv(string path)
        {
            string projectName = "Project";
            int currentModelIndex = 0;
            var modelFolders = new List<string>();

            if (!File.Exists(path))
                return (projectName, currentModelIndex, modelFolders);

            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var cols = Split(line);
                if (cols.Length == 0) continue;

                switch (cols[0])
                {
                    case "name":
                        projectName = cols.Length > 1 ? Unesc(cols[1]) : "Project";
                        break;
                    case "currentModelIndex":
                        if (cols.Length > 1 && int.TryParse(cols[1], out var ci))
                            currentModelIndex = ci;
                        break;
                    case "model":
                        // model,index,folderName
                        if (cols.Length >= 3)
                            modelFolders.Add(Unesc(cols[2]));
                        break;
                }
            }

            return (projectName, currentModelIndex, modelFolders);
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private static string Unesc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.StartsWith("\"") && s.EndsWith("\"") && s.Length >= 2)
            {
                s = s.Substring(1, s.Length - 2);
                s = s.Replace("\"\"", "\"");
            }
            return s;
        }

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
