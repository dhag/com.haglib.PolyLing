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
////using MeshContext = MeshContext;

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
        public static bool Export(string path, ProjectDTO projectDTO)
        {
            if (string.IsNullOrEmpty(path) || projectDTO == null)
                return false;

            try
            {
                projectDTO.UpdateModifiedAt();

                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore
                };

                string json = JsonConvert.SerializeObject(projectDTO, settings);
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
        public static bool ExportWithDialog(ProjectDTO projectDTO, string defaultName = "Project")
        {
            string path = EditorUtility.SaveFilePanel(
                "Export Project",
                Application.dataPath,
                defaultName,
                FileExtension
            );

            if (string.IsNullOrEmpty(path))
                return false;

            return Export(path, projectDTO);
        }

        // ================================================================
        // インポート
        // ================================================================

        /// <summary>
        /// ファイルからプロジェクトをインポート
        /// </summary>
        public static ProjectDTO Import(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError($"[ProjectSerializer] File not found: {path}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                var projectDTO = JsonConvert.DeserializeObject<ProjectDTO>(json);

                if (projectDTO == null)
                {
                    Debug.LogError("[ProjectSerializer] Failed to deserialize project data");
                    return null;
                }

                Debug.Log($"[ProjectSerializer] Imported: {path} (version: {projectDTO.version})");
                return projectDTO;
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
        public static ProjectDTO ImportWithDialog()
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
        public static ProjectDTO FromModelContext(
            ModelContext model,
            string projectName,
            WorkPlaneContext workPlaneContext = null,
            EditorStateDTO editorStateDTO = null,
            bool saveOnMemoryMaterials = false,
            string materialSaveDirectory = null)
        {
            if (model == null)
                return null;

            var projectDTO = ProjectDTO.Create(projectName);

            // ModelDataを作成して追加
            var modelDTO = ModelSerializer.FromModelContext(model, workPlaneContext, editorStateDTO, saveOnMemoryMaterials, materialSaveDirectory);
            if (modelDTO != null)
            {
                projectDTO.models.Add(modelDTO);
            }

            return projectDTO;
        }

        /// <summary>
        /// 複数のModelContextからProjectDataを作成
        /// </summary>
        public static ProjectDTO FromModelContexts(
            List<ModelContext> models,
            string projectName,
            List<WorkPlaneContext> workPlanes = null,
            List<EditorStateDTO> editorStates = null,
            bool saveOnMemoryMaterials = false,
            string materialSaveDirectory = null)
        {
            if (models == null || models.Count == 0)
                return null;

            var projectDTO = ProjectDTO.Create(projectName);

            for (int i = 0; i < models.Count; i++)
            {
                var model = models[i];
                if (model == null) continue;

                var workPlane = (workPlanes != null && i < workPlanes.Count) ? workPlanes[i] : null;
                var editorState = (editorStates != null && i < editorStates.Count) ? editorStates[i] : null;

                var modelDTO = ModelSerializer.FromModelContext(model, workPlane, editorState, saveOnMemoryMaterials, materialSaveDirectory);
                if (modelDTO != null)
                {
                    projectDTO.models.Add(modelDTO);
                }
            }

            return projectDTO;
        }

        // ================================================================
        // 変換: ProjectContext ↔ ProjectData
        // ================================================================

        /// <summary>
        /// ProjectContextからProjectDataを作成（エクスポート用）
        /// </summary>
        public static ProjectDTO FromProjectContext(
            ProjectContext project,
            List<WorkPlaneContext> workPlanes = null,
            List<EditorStateDTO> editorStates = null,
            bool saveOnMemoryMaterials = false,
            string materialSaveDirectory = null)
        {
            if (project == null)
                return null;

            var projectDTO = ProjectDTO.Create(project.Name ?? "Untitled");

            for (int i = 0; i < project.ModelCount; i++)
            {
                var model = project.Models[i];
                if (model == null) continue;

                var workPlane = (workPlanes != null && i < workPlanes.Count) ? workPlanes[i] : null;
                var editorState = (editorStates != null && i < editorStates.Count) ? editorStates[i] : null;

                var modelDTO = ModelSerializer.FromModelContext(model, workPlane, editorState, saveOnMemoryMaterials, materialSaveDirectory);
                if (modelDTO != null)
                {
                    projectDTO.models.Add(modelDTO);
                }
            }

            return projectDTO;
        }

        /// <summary>
        /// ProjectDataからProjectContextを復元（インポート用）
        /// </summary>
        public static ProjectContext ToProjectContext(ProjectDTO projectDTO)
        {
            if (projectDTO == null)
                return null;

            var project = new ProjectContext(projectDTO.name ?? "Untitled");
            
            // デフォルト Model をクリア（コンストラクタで自動作成されるため）
            project.Models.Clear();

            // ProjectData から Models を復元
            foreach (var modelDTO in projectDTO.models)
            {
                var model = ModelSerializer.ToModelContext(modelDTO);
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
        public static ModelContext ToModelContext(ProjectDTO projectDTO)
        {
            if (projectDTO == null || projectDTO.models.Count == 0)
                return null;

            return ModelSerializer.ToModelContext(projectDTO.models[0]);
        }

        /// <summary>
        /// ProjectDataから全てのModelContextを復元
        /// </summary>
        public static List<ModelContext> ToModelContexts(ProjectDTO projectDTO)
        {
            var result = new List<ModelContext>();

            if (projectDTO == null)
                return result;

            foreach (var modelDTO in projectDTO.models)
            {
                var model = ModelSerializer.ToModelContext(modelDTO);
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
        public static void AddModel(ProjectDTO projectDTO, ModelContext model, WorkPlaneContext workPlane = null, EditorStateDTO editorStateDTO = null, bool saveOnMemoryMaterials = false, string materialSaveDirectory = null)
        {
            if (projectDTO == null || model == null)
                return;

            var modelDTO = ModelSerializer.FromModelContext(model, workPlane, editorStateDTO, saveOnMemoryMaterials, materialSaveDirectory);
            if (modelDTO != null)
            {
                projectDTO.models.Add(modelDTO);
                projectDTO.UpdateModifiedAt();
            }
        }

        /// <summary>
        /// プロジェクトからモデルを削除
        /// </summary>
        public static bool RemoveModel(ProjectDTO projectDTO, int index)
        {
            if (projectDTO == null || index < 0 || index >= projectDTO.models.Count)
                return false;

            projectDTO.models.RemoveAt(index);
            projectDTO.UpdateModifiedAt();
            return true;
        }

        /// <summary>
        /// モデル数を取得
        /// </summary>
        public static int GetModelCount(ProjectDTO projectDTO)
        {
            return projectDTO?.models?.Count ?? 0;
        }
    }
}
