// Assets/Editor/MeshFactory/Serialization/ModelSerializer.cs
// モデルファイル (.mfmodel) のインポート/エクスポート
// Phase7: マルチマテリアル対応版

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Unity.Plastic.Newtonsoft.Json;
using MeshFactory.Data;
using MeshFactory.Tools;

namespace MeshFactory.Serialization
{
    /// <summary>
    /// モデルファイルのシリアライザ
    /// </summary>
    public static class ModelSerializer
    {
        // ================================================================
        // 定数
        // ================================================================

        public const string FileExtension = "mfmodel";
        public const string FileFilter = "MeshFactory Model";
        public const string CurrentVersion = "1.1";  // マテリアル対応

        // ================================================================
        // エクスポート
        // ================================================================

        /// <summary>
        /// モデルをファイルにエクスポート
        /// </summary>
        public static bool Export(string path, ModelData modelData)
        {
            if (string.IsNullOrEmpty(path) || modelData == null)
                return false;

            try
            {
                modelData.updatedAt = DateTime.Now.ToString("o");

                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore
                };

                string json = JsonConvert.SerializeObject(modelData, settings);
                File.WriteAllText(path, json);

                Debug.Log($"[ModelSerializer] Exported: {path}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModelSerializer] Export failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ファイルダイアログを表示してエクスポート
        /// </summary>
        public static bool ExportWithDialog(ModelData modelData, string defaultName = "Model")
        {
            string path = EditorUtility.SaveFilePanel(
                "Export Model",
                Application.dataPath,
                defaultName,
                FileExtension
            );

            if (string.IsNullOrEmpty(path))
                return false;

            return Export(path, modelData);
        }

        // ================================================================
        // インポート
        // ================================================================

        /// <summary>
        /// ファイルからモデルをインポート
        /// </summary>
        public static ModelData Import(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogError($"[ModelSerializer] File not found: {path}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                var modelData = JsonConvert.DeserializeObject<ModelData>(json);

                if (modelData == null)
                {
                    Debug.LogError("[ModelSerializer] Failed to deserialize model data");
                    return null;
                }

                // バージョンチェック
                if (!IsVersionCompatible(modelData.version))
                {
                    Debug.LogWarning($"[ModelSerializer] Version mismatch: file={modelData.version}, current={CurrentVersion}");
                }

                Debug.Log($"[ModelSerializer] Imported: {path} (version: {modelData.version})");
                return modelData;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ModelSerializer] Import failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ファイルダイアログを表示してインポート
        /// </summary>
        public static ModelData ImportWithDialog()
        {
            string path = EditorUtility.OpenFilePanel(
                "Import Model",
                Application.dataPath,
                FileExtension
            );

            if (string.IsNullOrEmpty(path))
                return null;

            return Import(path);
        }

        /// <summary>
        /// バージョン互換性チェック
        /// </summary>
        private static bool IsVersionCompatible(string fileVersion)
        {
            if (string.IsNullOrEmpty(fileVersion))
                return false;

            // メジャーバージョンが同じなら互換性あり
            var fileParts = fileVersion.Split('.');
            var currentParts = CurrentVersion.Split('.');

            return fileParts.Length > 0 && currentParts.Length > 0 &&
                   fileParts[0] == currentParts[0];
        }

        // ================================================================
        // 変換: MeshData → MeshEntryData
        // ================================================================

        /// <summary>
        /// MeshDataをMeshEntryDataに変換
        /// </summary>
        public static MeshEntryData ToMeshEntryData(
            MeshData meshData,
            string name,
            ExportSettings exportSettings,
            HashSet<int> selectedVertices,
            List<Material> materials = null,
            int currentMaterialIndex = 0)
        {
            if (meshData == null)
                return null;

            var entryData = new MeshEntryData
            {
                name = name ?? meshData.Name ?? "Untitled"
            };

            // ExportSettings
            entryData.exportSettings = ToExportSettingsData(exportSettings);

            // Vertices
            foreach (var vertex in meshData.Vertices)
            {
                var vertexData = new VertexData();
                vertexData.SetPosition(vertex.Position);
                vertexData.SetUVs(vertex.UVs);
                vertexData.SetNormals(vertex.Normals);
                entryData.vertices.Add(vertexData);
            }

            // Faces（MaterialIndex含む）
            foreach (var face in meshData.Faces)
            {
                var faceData = new FaceData
                {
                    v = new List<int>(face.VertexIndices),
                    uvi = new List<int>(face.UVIndices),
                    ni = new List<int>(face.NormalIndices),
                    mi = face.MaterialIndex != 0 ? face.MaterialIndex : (int?)null  // 0はデフォルトなので省略
                };
                entryData.faces.Add(faceData);
            }

            // Selection
            if (selectedVertices != null && selectedVertices.Count > 0)
            {
                entryData.selectedVertices = selectedVertices.ToList();
            }

            // Materials（アセットパスとして保存）
            if (materials != null)
            {
                foreach (var mat in materials)
                {
                    if (mat != null)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(mat);
                        entryData.materials.Add(assetPath ?? "");
                    }
                    else
                    {
                        entryData.materials.Add("");  // null は空文字列
                    }
                }
            }

            entryData.currentMaterialIndex = currentMaterialIndex;

            return entryData;
        }

        /// <summary>
        /// ExportSettingsをExportSettingsDataに変換
        /// </summary>
        public static ExportSettingsData ToExportSettingsData(ExportSettings settings)
        {
            if (settings == null)
                return ExportSettingsData.CreateDefault();

            var data = new ExportSettingsData
            {
                useLocalTransform = settings.UseLocalTransform
            };
            data.SetPosition(settings.Position);
            data.SetRotation(settings.Rotation);
            data.SetScale(settings.Scale);

            return data;
        }

        /// <summary>
        /// WorkPlaneをWorkPlaneDataに変換
        /// </summary>
        public static WorkPlaneData ToWorkPlaneData(WorkPlane workPlane)
        {
            if (workPlane == null)
                return WorkPlaneData.CreateDefault();

            return new WorkPlaneData
            {
                mode = workPlane.Mode.ToString(),
                origin = new float[] { workPlane.Origin.x, workPlane.Origin.y, workPlane.Origin.z },
                axisU = new float[] { workPlane.AxisU.x, workPlane.AxisU.y, workPlane.AxisU.z },
                axisV = new float[] { workPlane.AxisV.x, workPlane.AxisV.y, workPlane.AxisV.z },
                isLocked = workPlane.IsLocked,
                lockOrientation = workPlane.LockOrientation,
                autoUpdateOriginOnSelection = workPlane.AutoUpdateOriginOnSelection
            };
        }

        // ================================================================
        // 変換: MeshEntryData → MeshData
        // ================================================================

        /// <summary>
        /// MeshEntryDataをMeshDataに変換
        /// </summary>
        public static MeshData ToMeshData(MeshEntryData entryData)
        {
            if (entryData == null)
                return null;

            var meshData = new MeshData(entryData.name);

            // Vertices
            foreach (var vd in entryData.vertices)
            {
                var vertex = new Vertex(vd.GetPosition());
                vertex.UVs = vd.GetUVs();
                vertex.Normals = vd.GetNormals();
                meshData.Vertices.Add(vertex);
            }

            // Faces（MaterialIndex含む）
            foreach (var fd in entryData.faces)
            {
                var face = new Face
                {
                    VertexIndices = new List<int>(fd.v ?? new List<int>()),
                    UVIndices = new List<int>(fd.uvi ?? new List<int>()),
                    NormalIndices = new List<int>(fd.ni ?? new List<int>()),
                    MaterialIndex = fd.mi ?? 0  // nullの場合は0
                };
                meshData.Faces.Add(face);
            }

            return meshData;
        }

        /// <summary>
        /// マテリアルリストを復元
        /// </summary>
        public static List<Material> ToMaterials(MeshEntryData entryData)
        {
            var result = new List<Material>();

            if (entryData?.materials == null || entryData.materials.Count == 0)
            {
                // デフォルトでスロット0を追加
                result.Add(null);
                return result;
            }

            foreach (var path in entryData.materials)
            {
                if (string.IsNullOrEmpty(path))
                {
                    result.Add(null);
                }
                else
                {
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (mat == null)
                    {
                        Debug.LogWarning($"[ModelSerializer] Material not found: {path}");
                    }
                    result.Add(mat);  // 見つからなくてもnullで追加
                }
            }

            // 空にならないように最低1つ確保
            if (result.Count == 0)
            {
                result.Add(null);
            }

            return result;
        }

        /// <summary>
        /// ExportSettingsDataをExportSettingsに変換
        /// </summary>
        public static ExportSettings ToExportSettings(ExportSettingsData data)
        {
            if (data == null)
                return new ExportSettings();

            return new ExportSettings
            {
                UseLocalTransform = data.useLocalTransform,
                Position = data.GetPosition(),
                Rotation = data.GetRotation(),
                Scale = data.GetScale()
            };
        }

        /// <summary>
        /// WorkPlaneDataをWorkPlaneに適用
        /// </summary>
        public static void ApplyToWorkPlane(WorkPlaneData data, WorkPlane workPlane)
        {
            if (data == null || workPlane == null)
                return;

            // Mode
            if (Enum.TryParse<WorkPlaneMode>(data.mode, out var mode))
            {
                workPlane.Mode = mode;
            }

            // Origin
            if (data.origin != null && data.origin.Length >= 3)
            {
                workPlane.Origin = new Vector3(data.origin[0], data.origin[1], data.origin[2]);
            }

            // AxisU
            if (data.axisU != null && data.axisU.Length >= 3)
            {
                workPlane.AxisU = new Vector3(data.axisU[0], data.axisU[1], data.axisU[2]);
            }

            // AxisV
            if (data.axisV != null && data.axisV.Length >= 3)
            {
                workPlane.AxisV = new Vector3(data.axisV[0], data.axisV[1], data.axisV[2]);
            }

            workPlane.IsLocked = data.isLocked;
            workPlane.LockOrientation = data.lockOrientation;
            workPlane.AutoUpdateOriginOnSelection = data.autoUpdateOriginOnSelection;
        }

        /// <summary>
        /// 選択状態を復元
        /// </summary>
        public static HashSet<int> ToSelectedVertices(MeshEntryData entryData)
        {
            if (entryData?.selectedVertices == null)
                return new HashSet<int>();

            return new HashSet<int>(entryData.selectedVertices);
        }
    }
}