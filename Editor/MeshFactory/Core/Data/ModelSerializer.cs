// Assets/Editor/MeshFactory/Serialization/ModelSerializer.cs
// モデルファイル (.mfmodel) のインポート/エクスポート
// Phase7: マルチマテリアル対応版
// Phase5: ModelContext統合

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
    /// モデルファイルのシリアライザ
    /// </summary>
    public static class ModelSerializer
    {
        // ================================================================
        // 注意: このクラスはModelDataの変換処理のみを提供します
        // ファイルの読み書きはProjectSerializerを使用してください
        // ================================================================

        // ================================================================
        // 変換: MeshObject → MeshDTO
        // ================================================================

        /// <summary>
        /// MeshObjectをMeshDTOに変換
        /// </summary>
        public static MeshDTO ToMeshDTO(
            MeshObject meshObject,
            string name,
            ExportSettings exportSettings,
            HashSet<int> selectedVertices,
            List<Material> materials = null,
            int currentMaterialIndex = 0)
        {
            if (meshObject == null)
                return null;

            var meshDTO = new MeshDTO
            {
                name = name ?? meshObject.Name ?? "Untitled"
            };

            // ExportSettings
            meshDTO.exportSettingsDTO = ToExportSettingsDTO(exportSettings);

            // Vertices
            foreach (var vertex in meshObject.Vertices)
            {
                var vertexDTO = new VertexDTO();
                vertexDTO.SetPosition(vertex.Position);
                vertexDTO.SetUVs(vertex.UVs);
                vertexDTO.SetNormals(vertex.Normals);
                vertexDTO.SetBoneWeight(vertex.BoneWeight);
                meshDTO.vertices.Add(vertexDTO);
            }

            // Faces（MaterialIndex含む）
            foreach (var face in meshObject.Faces)
            {
                var faceData = new FaceDTO
                {
                    v = new List<int>(face.VertexIndices),
                    uvi = new List<int>(face.UVIndices),
                    ni = new List<int>(face.NormalIndices),
                    mi = face.MaterialIndex != 0 ? face.MaterialIndex : (int?)null  // 0はデフォルトなので省略
                };
                meshDTO.faces.Add(faceData);
            }

            // Selection
            if (selectedVertices != null && selectedVertices.Count > 0)
            {
                meshDTO.selectedVertices = selectedVertices.ToList();
            }

            // Materials（アセットパスとして保存）
            if (materials != null)
            {
                foreach (var mat in materials)
                {
                    if (mat != null)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(mat);
                        meshDTO.materialPathList.Add(assetPath ?? "");
                    }
                    else
                    {
                        meshDTO.materialPathList.Add("");  // null は空文字列
                    }
                }
            }

            meshDTO.currentMaterialIndex = currentMaterialIndex;

            return meshDTO;
        }

        /// <summary>
        /// ExportSettingsをExportSettingsDataに変換
        /// </summary>
        public static ExportSettingsDTO ToExportSettingsDTO(ExportSettings settings)
        {
            if (settings == null)
                return ExportSettingsDTO.CreateDefault();

            var data = new ExportSettingsDTO
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
        public static WorkPlaneDTO ToWorkPlaneData(WorkPlaneContext workPlaneContext)
        {
            if (workPlaneContext == null)
                return WorkPlaneDTO.CreateDefault();

            return new WorkPlaneDTO
            {
                mode = workPlaneContext.Mode.ToString(),
                origin = new float[] { workPlaneContext.Origin.x, workPlaneContext.Origin.y, workPlaneContext.Origin.z },
                axisU = new float[] { workPlaneContext.AxisU.x, workPlaneContext.AxisU.y, workPlaneContext.AxisU.z },
                axisV = new float[] { workPlaneContext.AxisV.x, workPlaneContext.AxisV.y, workPlaneContext.AxisV.z },
                isLocked = workPlaneContext.IsLocked,
                lockOrientation = workPlaneContext.LockOrientation,
                autoUpdateOriginOnSelection = workPlaneContext.AutoUpdateOriginOnSelection
            };
        }

        // ================================================================
        // 変換: MeshDTO → MeshObject
        // ================================================================

        /// <summary>
        /// MeshDTOをMeshObjectに変換
        /// </summary>
        public static MeshObject ToMeshObject(MeshDTO meshDTO)
        {
            if (meshDTO == null)
                return null;

            var meshObject = new MeshObject(meshDTO.name);

            // Vertices
            foreach (var vd in meshDTO.vertices)
            {
                var vertex = new Vertex(vd.GetPosition());
                vertex.UVs = vd.GetUVs();
                vertex.Normals = vd.GetNormals();
                vertex.BoneWeight = vd.GetBoneWeight();
                meshObject.Vertices.Add(vertex);
            }

            // Faces（MaterialIndex含む）
            foreach (var fd in meshDTO.faces)
            {
                var face = new Face
                {
                    VertexIndices = new List<int>(fd.v ?? new List<int>()),
                    UVIndices = new List<int>(fd.uvi ?? new List<int>()),
                    NormalIndices = new List<int>(fd.ni ?? new List<int>()),
                    MaterialIndex = fd.mi ?? 0  // nullの場合は0
                };
                meshObject.Faces.Add(face);
            }

            return meshObject;
        }

        /// <summary>
        /// マテリアルリストを復元
        /// </summary>
        public static List<Material> ToMaterials(MeshDTO meshDTO)
        {
            var result = new List<Material>();

            if (meshDTO?.materialPathList == null || meshDTO.materialPathList.Count == 0)
            {
                // デフォルトでスロット0を追加
                result.Add(null);
                return result;
            }

            foreach (var path in meshDTO.materialPathList)
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
        public static ExportSettings ToExportSettings(ExportSettingsDTO data)
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
        public static void ApplyToWorkPlane(WorkPlaneDTO data, WorkPlaneContext workPlane)
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
        public static HashSet<int> ToSelectedVertices(MeshDTO meshDTO)
        {
            if (meshDTO?.selectedVertices == null)
                return new HashSet<int>();

            return new HashSet<int>(meshDTO.selectedVertices);
        }

        // ================================================================
        // ModelContext統合（Phase 5追加）
        // ================================================================

        /// <summary>
        /// ModelContextからModelDataを作成（エクスポート用）
        /// </summary>
        /// <param name="model">ModelContext</param>
        /// <param name="workPlaneContext">WorkPlaneContext（オプション）</param>
        /// <param name="editorStateDTO">EditorStateDTO（オプション）</param>
        /// <returns>シリアライズ可能なModelData</returns>
        public static ModelDTO FromModelContext(
            ModelContext model,
            WorkPlaneContext workPlaneContext = null,
            EditorStateDTO editorStateDTO = null)
        {
            if (model == null)
                return null;

            var modelDTO = new ModelDTO
            {
                name = model.Name ?? "Untitled"
            };

            // MeshContextをMeshContextDataに変換
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var meshContext = model.GetMeshContext(i);
                if (meshContext == null) continue;

                // FromMeshContextを使用（オブジェクト属性・ミラー設定も含む）
                var meshContextData = FromMeshContext(meshContext, null);

                if (meshContextData != null)
                {
                    modelDTO.meshDTOList.Add(meshContextData);
                }
            }

            // WorkPlaneContext
            if (workPlaneContext != null)
            {
                modelDTO.workPlane = ToWorkPlaneData(workPlaneContext);
            }

            // EditorState
            modelDTO.editorStateDTO = editorStateDTO;

            // ================================================================
            // Materials（Phase 1: モデル単位に集約）
            // ================================================================
            if (model.Materials != null)
            {
                foreach (var mat in model.Materials)
                {
                    if (mat == null)
                    {
                        modelDTO.materials.Add("");
                    }
                    else
                    {
                        string assetPath = UnityEditor.AssetDatabase.GetAssetPath(mat);
                        modelDTO.materials.Add(assetPath ?? "");
                    }
                }
            }
            modelDTO.currentMaterialIndex = model.CurrentMaterialIndex;

            // DefaultMaterials
            if (model.DefaultMaterials != null)
            {
                foreach (var mat in model.DefaultMaterials)
                {
                    if (mat == null)
                    {
                        modelDTO.defaultMaterials.Add("");
                    }
                    else
                    {
                        string assetPath = UnityEditor.AssetDatabase.GetAssetPath(mat);
                        modelDTO.defaultMaterials.Add(assetPath ?? "");
                    }
                }
            }
            modelDTO.defaultCurrentMaterialIndex = model.DefaultCurrentMaterialIndex;
            modelDTO.autoSetDefaultMaterials = model.AutoSetDefaultMaterials;

            return modelDTO;
        }

        /// <summary>
        /// ModelDataからModelContextを復元（インポート用）
        /// </summary>
        /// <param name="modelDTO">インポートしたModelData</param>
        /// <param name="model">復元先のModelContext（nullの場合は新規作成）</param>
        /// <returns>復元されたModelContext</returns>
        public static ModelContext ToModelContext(ModelDTO modelDTO, ModelContext model = null)
        {
            if (modelDTO == null)
                return null;

            // ModelContextを準備
            if (model == null)
            {
                model = new ModelContext();
            }
            else
            {
                model.Clear();
            }

            model.Name = modelDTO.name;
            model.FilePath = null;  // 呼び出し元で設定

            // MeshContextDataからMeshContextを復元
            foreach (var meshContextData in modelDTO.meshDTOList)
            {
                var meshObject = ToMeshObject(meshContextData);
                if (meshObject == null) continue;

                // MeshTypeをパース
                MeshType meshType = MeshType.Mesh;
                if (!string.IsNullOrEmpty(meshContextData.type))
                {
                    Enum.TryParse(meshContextData.type, out meshType);
                }

                var context = new MeshContext
                {
                    Name = meshContextData.name ?? "UnityMesh",
                    MeshObject = meshObject,
                    UnityMesh = meshObject.ToUnityMesh(),
                    OriginalPositions = meshObject.Vertices.Select(v => v.Position).ToArray(),
                    ExportSettings = ToExportSettings(meshContextData.exportSettingsDTO),
                    // Materials は ModelData から復元するため、ここでは設定しない
                    // オブジェクト属性
                    Type = meshType,
                    ParentIndex = meshContextData.parentIndex,
                    Depth = meshContextData.depth,
                    HierarchyParentIndex = meshContextData.hierarchyParentIndex,
                    IsVisible = meshContextData.isVisible,
                    IsLocked = meshContextData.isLocked,
                    // ミラー設定
                    MirrorType = meshContextData.mirrorType,
                    MirrorAxis = meshContextData.mirrorAxis,
                    MirrorDistance = meshContextData.mirrorDistance
                };

                model.Add(context);
            }

            // ================================================================
            // Materials 復元（Phase 1: モデル単位に集約）
            // ================================================================
            if (modelDTO.materials != null && modelDTO.materials.Count > 0)
            {
                // 新形式: ModelData.materialPathList から復元
                model.Materials.Clear();
                foreach (var path in modelDTO.materials)
                {
                    Material mat = null;
                    if (!string.IsNullOrEmpty(path))
                    {
                        mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (mat == null)
                        {
                            Debug.LogWarning($"[ModelSerializer] Material not found: {path}");
                        }
                    }
                    model.Materials.Add(mat);
                }
                model.CurrentMaterialIndex = modelDTO.currentMaterialIndex;
            }
            else if (modelDTO.meshDTOList.Count > 0)
            {
                // 旧形式: 最初の MeshDTO.materialPathList から復元（後方互換）
                var firstMeshData = modelDTO.meshDTOList[0];
                model.Materials = ToMaterials(firstMeshData);
                model.CurrentMaterialIndex = firstMeshData.currentMaterialIndex;
            }

            // DefaultMaterials 復元
            if (modelDTO.defaultMaterials != null && modelDTO.defaultMaterials.Count > 0)
            {
                model.DefaultMaterials.Clear();
                foreach (var path in modelDTO.defaultMaterials)
                {
                    Material mat = null;
                    if (!string.IsNullOrEmpty(path))
                    {
                        mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                    }
                    model.DefaultMaterials.Add(mat);
                }
                model.DefaultCurrentMaterialIndex = modelDTO.defaultCurrentMaterialIndex;
                model.AutoSetDefaultMaterials = modelDTO.autoSetDefaultMaterials;
            }

            return model;
        }

        /// <summary>
        /// MeshContextをMeshDTOに変換（簡易版）
        /// </summary>
        public static MeshDTO FromMeshContext(MeshContext meshContext, HashSet<int> selectedVertices = null)
        {
            if (meshContext == null)
                return null;

            var contextData = ToMeshDTO(
                meshContext.MeshObject,
                meshContext.Name,
                meshContext.ExportSettings,
                selectedVertices,
                null,  // Phase 1: Materials は ModelContext に集約
                0
            );

            if (contextData != null)
            {
                // オブジェクト属性
                contextData.type = meshContext.Type.ToString();
                contextData.parentIndex = meshContext.ParentIndex;
                contextData.depth = meshContext.Depth;
                contextData.hierarchyParentIndex = meshContext.HierarchyParentIndex;
                contextData.isVisible = meshContext.IsVisible;
                contextData.isLocked = meshContext.IsLocked;

                // ミラー設定
                contextData.mirrorType = meshContext.MirrorType;
                contextData.mirrorAxis = meshContext.MirrorAxis;
                contextData.mirrorDistance = meshContext.MirrorDistance;
            }

            return contextData;
        }

        /// <summary>
        /// MeshContextDataからMeshContextを復元（簡易版）
        /// </summary>
        public static MeshContext ToMeshContext(MeshDTO meshDTO)
        {
            if (meshDTO == null)
                return null;

            var meshObject = ToMeshObject(meshDTO);
            if (meshObject == null)
                return null;

            // MeshTypeをパース
            MeshType meshType = MeshType.Mesh;
            if (!string.IsNullOrEmpty(meshDTO.type))
            {
                Enum.TryParse(meshDTO.type, out meshType);
            }

            return new MeshContext
            {
                Name = meshDTO.name ?? "UnityMesh",
                MeshObject = meshObject,
                UnityMesh = meshObject.ToUnityMesh(),
                OriginalPositions = meshObject.Vertices.Select(v => v.Position).ToArray(),
                ExportSettings = ToExportSettings(meshDTO.exportSettingsDTO),
                // Phase 1: Materials は ModelContext に集約
                // オブジェクト属性
                Type = meshType,
                ParentIndex = meshDTO.parentIndex,
                Depth = meshDTO.depth,
                IsVisible = meshDTO.isVisible,
                IsLocked = meshDTO.isLocked,
                // ミラー設定
                MirrorType = meshDTO.mirrorType,
                MirrorAxis = meshDTO.mirrorAxis,
                MirrorDistance = meshDTO.mirrorDistance
            };
        }

        /// <summary>
        /// EditorStateDTOを作成
        /// </summary>
        public static EditorStateDTO CreateEditorStateDTO(
            float rotationX,
            float rotationY,
            float cameraDistance,
            Vector3 cameraTarget,
            bool showWireframe,
            bool showVertices,
            bool vertexEditMode,
            int selectedMeshIndex,
            string currentToolName = null)
        {
            return new EditorStateDTO
            {
                rotationX = rotationX,
                rotationY = rotationY,
                cameraDistance = cameraDistance,
                cameraTarget = new float[] { cameraTarget.x, cameraTarget.y, cameraTarget.z },
                showWireframe = showWireframe,
                showVertices = showVertices,
                vertexEditMode = vertexEditMode,
                selectedMeshIndex = selectedMeshIndex,
                currentToolName = currentToolName
            };
        }

        /// <summary>
        /// MeshContextに選択頂点情報を含めてMeshContextDataに変換し、ModelDataに設定
        /// </summary>
        public static void SetSelectedVerticesForMeshContext(
            ModelDTO modelDTO,
            int meshIndex,
            HashSet<int> selectedVertices)
        {
            if (modelDTO == null || meshIndex < 0 || meshIndex >= modelDTO.meshDTOList.Count)
                return;

            if (selectedVertices != null && selectedVertices.Count > 0)
            {
                modelDTO.meshDTOList[meshIndex].selectedVertices = selectedVertices.ToList();
            }
        }
    }
}