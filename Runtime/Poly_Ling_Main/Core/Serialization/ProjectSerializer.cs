// Assets/Editor/Poly_Ling/Serialization/ProjectSerializer.cs
// プロジェクトファイル (.mfproj) のインポート/エクスポート
// v1.0: 初期バージョン

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.EditorBridge;
using Newtonsoft.Json;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Core;

// MeshContextはSimpleMeshFactoryのネストクラスを参照
////using MeshContext = MeshContext;

namespace Poly_Ling.Serialization
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
        public const string FileFilter = "Poly_Ling Project";
        public const string CurrentVersion = "1.0";

        // RecentPaths のキー（.mfproj 保存/読込フォルダを起動間で記録する）
        public const string JsonFolderKey = "Project.JsonFolder";

        /// <summary>
        /// プロジェクトJSONの「最後に使ったフルパス」。
        /// JsonFolderKey はフォルダだけを覚えるため、ファイル名が毎回消えていた。
        /// </summary>
        public const string JsonFileKey   = "Project.JsonFilePath";

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
            // JsonFolderKey はフォルダだけを覚えるためファイル名が毎回消えていた。
            // 初期値はフルパスのキーから取り、旧キーも従来どおり更新する。
            string path = RecentFileDialog.AskSave(
                "Export Project", JsonFileKey, defaultName, FileExtension);

            if (string.IsNullOrEmpty(path))
                return false;

            RecentPaths.Set(JsonFolderKey, Path.GetDirectoryName(path));
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
            string path = RecentFileDialog.AskLoad("Import Project", JsonFileKey, FileExtension);

            if (string.IsNullOrEmpty(path))
                return null;

            RecentPaths.Set(JsonFolderKey, Path.GetDirectoryName(path));
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
            EditorStateDTO editorStateDTO = null)
        {
            if (model == null)
                return null;

            var projectDTO = ProjectDTO.Create(projectName);

            // ModelDataを作成して追加
            var modelDTO = ModelSerializer.FromModelContext(model, workPlaneContext, editorStateDTO);
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
            List<EditorStateDTO> editorStates = null)
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

                var modelDTO = ModelSerializer.FromModelContext(model, workPlane, editorState);
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
            List<EditorStateDTO> editorStates = null)
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

                var modelDTO = ModelSerializer.FromModelContext(model, workPlane, editorState);
                if (modelDTO != null)
                {
                    projectDTO.models.Add(modelDTO);
                }
            }

            // 作業軸辞書（プロジェクト単位。規約4：CSV/JSON 対称）
            projectDTO.workAxisLibrary = ToWorkAxisLibraryData(project.WorkAxes);

            return projectDTO;
        }

        // ================================================================
        // 変換: 作業軸辞書
        // ================================================================

        /// <summary>
        /// WorkAxisLibrary を DTO のリストへ。登録順を保つ。
        /// </summary>
        public static List<WorkAxisEntryDTO> ToWorkAxisLibraryData(WorkAxisLibrary lib)
        {
            var list = new List<WorkAxisEntryDTO>();
            if (lib == null) return list;

            foreach (var name in lib.Names)
            {
                if (!lib.TryGet(name, out var e)) continue;

                list.Add(new WorkAxisEntryDTO
                {
                    name     = name,
                    origin   = new float[] { e.Origin.x, e.Origin.y, e.Origin.z },
                    rotation = new float[] { e.Rotation.x, e.Rotation.y, e.Rotation.z, e.Rotation.w },
                    length   = e.Length,
                });
            }
            return list;
        }

        /// <summary>
        /// DTO のリストを WorkAxisLibrary へ流し込む。読み込み前に Clear する。
        /// 壊れた要素は読み飛ばす（WorkAxisLibraryCsvIO.Load と同じ扱い）。
        /// </summary>
        public static void ApplyToWorkAxisLibrary(List<WorkAxisEntryDTO> list, WorkAxisLibrary lib)
        {
            if (lib == null) return;

            lib.Clear();
            if (list == null) return;

            foreach (var d in list)
            {
                if (d == null) continue;
                if (WorkAxisLibrary.Normalize(d.name).Length == 0) continue;
                if (d.origin   == null || d.origin.Length   < 3) continue;
                if (d.rotation == null || d.rotation.Length < 4) continue;

                var q = new Quaternion(d.rotation[0], d.rotation[1], d.rotation[2], d.rotation[3]);
                // 単位クォータニオンでないと姿勢として使えない。ゼロ長は捨てる。
                if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 1e-8f) continue;
                q.Normalize();

                lib.Set(d.name, new WorkAxisEntry
                {
                    Origin   = new Vector3(d.origin[0], d.origin[1], d.origin[2]),
                    Rotation = q,
                    // 0 以下は WorkAxisContext.Length の下限クランプで
                    // 使い物にならない長さになるため既定値へ倒す。
                    Length   = d.length > 0f ? d.length : WorkAxisContext.DefaultLength,
                });
            }
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

            // 作業軸辞書（項目が無い旧データでは空のまま）
            if (project.WorkAxes == null) project.WorkAxes = new WorkAxisLibrary();
            ApplyToWorkAxisLibrary(projectDTO.workAxisLibrary, project.WorkAxes);

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
        public static void AddModel(ProjectDTO projectDTO, ModelContext model, WorkPlaneContext workPlane = null, EditorStateDTO editorStateDTO = null)
        {
            if (projectDTO == null || model == null)
                return;

            var modelDTO = ModelSerializer.FromModelContext(model, workPlane, editorStateDTO);
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
