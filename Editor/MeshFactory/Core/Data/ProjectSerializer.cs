// Assets/Editor/MeshFactory/Serialization/ProjectSerializer.cs
// プロジェクトファイル (.mfproj) のインポート/エクスポート
// v1.0: 初期バージョン

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Unity.Plastic.Newtonsoft.Json;
using MeshFactory.Data;
using MeshFactory.Tools;
using MeshFactory.Model;

// MeshContextはSimpleMeshFactoryのネストクラスを参照
using MeshContext = SimpleMeshFactory.MeshContext;

namespace MeshFactory.Serialization
{
    /// <summary>
    /// プロジェクトファイルのシリアライザ
    /// </summary>
    public static class ProjectSerializer
    {
        // ================================================================
        // 定数
        // ================================================================

        public const string FileExtension = "mfproj";
        public const string FileFilter = "MeshFactory Project";
        public const string CurrentVersion = "1.0";

        // ================================================================
        // エクスポート
        // ================================================================

        /// <summary>
        /// プロジェクトをファイルにエクスポート
        /// </summary>
        public static bool Export(string path, ProjectData projectData)
        {
            if (string.IsNullOrEmpty(path) || projectData == null)
                return false;

            try
            {
                projectData.UpdateModifiedAt();

                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore
                };

                string json = JsonConvert.SerializeObject(projectData, settings);
                File.WriteAllText(path, json);

                Debug.Log($"[ProjectSerializer] Exported: {path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProjectSerializer] Export failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ファイルダイアログを表示してエクスポート
        /// </summary>
        public static bool ExportWithDialog(ProjectData projectData, string defaultName = "Project")
        {
            string path = EditorUtility.SaveFilePanel(
                "Export Project",
                Application.dataPath,
                defaultName,
                FileExtension
            );

            if (string.IsNullOrEmpty(path))
                return false;

            return Export(path, projectData);
        }

        // ================================================================
        // インポート
        // ================================================================

        /// <summary>
        /// ファイルからプロジェクトをインポート
        /// </summary>
        public static ProjectData Import(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError($"[ProjectSerializer] File not found: {path}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                var projectData = JsonConvert.DeserializeObject<ProjectData>(json);

                if (projectData == null)
                {
                    Debug.LogError("[ProjectSerializer] Failed to deserialize project data");
                    return null;
                }

                Debug.Log($"[ProjectSerializer] Imported: {path} (version: {projectData.version})");
                return projectData;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ProjectSerializer] Import failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ファイルダイアログを表示してインポート
        /// </summary>
        public static ProjectData ImportWithDialog()
        {
            string path = EditorUtility.OpenFilePanel(
                "Import Project",
                Application.dataPath,
                FileExtension
            );

            if (string.IsNullOrEmpty(path))
                return null;

            return Import(path);
        }

        // ================================================================
        // 変換: ModelContext → ProjectData
        // ================================================================

        /// <summary>
        /// ModelContextからProjectDataを作成
        /// </summary>
        public static ProjectData FromModelContext(
            ModelContext model,
            string projectName,
            WorkPlane workPlane = null,
            EditorStateData editorState = null)
        {
            if (model == null)
                return null;

            var projectData = ProjectData.Create(projectName);

            // ModelDataを作成して追加
            var modelData = ModelSerializer.FromModelContext(model, workPlane, editorState);
            if (modelData != null)
            {
                projectData.models.Add(modelData);
            }

            return projectData;
        }

        /// <summary>
        /// 複数のModelContextからProjectDataを作成
        /// </summary>
        public static ProjectData FromModelContexts(
            List<ModelContext> models,
            string projectName,
            List<WorkPlane> workPlanes = null,
            List<EditorStateData> editorStates = null)
        {
            if (models == null || models.Count == 0)
                return null;

            var projectData = ProjectData.Create(projectName);

            for (int i = 0; i < models.Count; i++)
            {
                var model = models[i];
                if (model == null) continue;

                var workPlane = (workPlanes != null && i < workPlanes.Count) ? workPlanes[i] : null;
                var editorState = (editorStates != null && i < editorStates.Count) ? editorStates[i] : null;

                var modelData = ModelSerializer.FromModelContext(model, workPlane, editorState);
                if (modelData != null)
                {
                    projectData.models.Add(modelData);
                }
            }

            return projectData;
        }

        // ================================================================
        // 変換: ProjectContext ↔ ProjectData
        // ================================================================

        /// <summary>
        /// ProjectContextからProjectDataを作成（エクスポート用）
        /// </summary>
        public static ProjectData FromProjectContext(
            ProjectContext project,
            List<WorkPlane> workPlanes = null,
            List<EditorStateData> editorStates = null)
        {
            if (project == null)
                return null;

            var projectData = ProjectData.Create(project.Name ?? "Untitled");

            for (int i = 0; i < project.ModelCount; i++)
            {
                var model = project.Models[i];
                if (model == null) continue;

                var workPlane = (workPlanes != null && i < workPlanes.Count) ? workPlanes[i] : null;
                var editorState = (editorStates != null && i < editorStates.Count) ? editorStates[i] : null;

                var modelData = ModelSerializer.FromModelContext(model, workPlane, editorState);
                if (modelData != null)
                {
                    projectData.models.Add(modelData);
                }
            }

            return projectData;
        }

        /// <summary>
        /// ProjectDataからProjectContextを復元（インポート用）
        /// </summary>
        public static ProjectContext ToProjectContext(ProjectData projectData)
        {
            if (projectData == null)
                return null;

            var project = new ProjectContext(projectData.name ?? "Untitled");
            
            // デフォルト Model をクリア（コンストラクタで自動作成されるため）
            project.Models.Clear();

            // ProjectData から Models を復元
            foreach (var modelData in projectData.models)
            {
                var model = ModelSerializer.ToModelContext(modelData);
                if (model != null)
                {
                    project.Models.Add(model);
                }
            }

            // Models が空の場合はデフォルト Model を追加
            if (project.Models.Count == 0)
            {
                project.Models.Add(new ModelContext("Model"));
            }

            // CurrentModelIndex を設定
            project.CurrentModelIndex = 0;

            return project;
        }

        // ================================================================
        // 変換: ProjectData → ModelContext（後方互換）
        // ================================================================

        /// <summary>
        /// ProjectDataから最初のModelContextを復元
        /// </summary>
        public static ModelContext ToModelContext(ProjectData projectData)
        {
            if (projectData == null || projectData.models.Count == 0)
                return null;

            return ModelSerializer.ToModelContext(projectData.models[0]);
        }

        /// <summary>
        /// ProjectDataから全てのModelContextを復元
        /// </summary>
        public static List<ModelContext> ToModelContexts(ProjectData projectData)
        {
            var result = new List<ModelContext>();

            if (projectData == null)
                return result;

            foreach (var modelData in projectData.models)
            {
                var model = ModelSerializer.ToModelContext(modelData);
                if (model != null)
                {
                    result.Add(model);
                }
            }

            return result;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>
        /// プロジェクトにモデルを追加
        /// </summary>
        public static void AddModel(ProjectData projectData, ModelContext model, WorkPlane workPlane = null, EditorStateData editorState = null)
        {
            if (projectData == null || model == null)
                return;

            var modelData = ModelSerializer.FromModelContext(model, workPlane, editorState);
            if (modelData != null)
            {
                projectData.models.Add(modelData);
                projectData.UpdateModifiedAt();
            }
        }

        /// <summary>
        /// プロジェクトからモデルを削除
        /// </summary>
        public static bool RemoveModel(ProjectData projectData, int index)
        {
            if (projectData == null || index < 0 || index >= projectData.models.Count)
                return false;

            projectData.models.RemoveAt(index);
            projectData.UpdateModifiedAt();
            return true;
        }

        /// <summary>
        /// モデル数を取得
        /// </summary>
        public static int GetModelCount(ProjectData projectData)
        {
            return projectData?.models?.Count ?? 0;
        }
    }
}
