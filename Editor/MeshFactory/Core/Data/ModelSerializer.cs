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
using MeshContext = SimpleMeshFactory.MeshContext;

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
        // 変換: MeshObject → MeshContextData
        // ================================================================

        /// <summary>
        /// MeshObjectをMeshContextDataに変換
        /// </summary>
        public static MeshContextData ToMeshContextData(
            MeshObject meshObject,
            string name,
            ExportSettings exportSettings,
            HashSet<int> selectedVertices,
            List<Material> materials = null,
            int currentMaterialIndex = 0)
        {
            if (meshObject == null)
                return null;

            var contextData = new MeshContextData
            {
                name = name ?? meshObject.Name ?? "Untitled"
            };

            // ExportSettings
            contextData.exportSettings = ToExportSettingsData(exportSettings);

            // Vertices
            foreach (var vertex in meshObject.Vertices)
            {
                var vertexData = new VertexData();
                vertexData.SetPosition(vertex.Position);
                vertexData.SetUVs(vertex.UVs);
                vertexData.SetNormals(vertex.Normals);
                contextData.vertices.Add(vertexData);
            }

            // Faces（MaterialIndex含む）
            foreach (var face in meshObject.Faces)
            {
                var faceData = new FaceData
                {
                    v = new List<int>(face.VertexIndices),
                    uvi = new List<int>(face.UVIndices),
                    ni = new List<int>(face.NormalIndices),
                    mi = face.MaterialIndex != 0 ? face.MaterialIndex : (int?)null  // 0はデフォルトなので省略
                };
                contextData.faces.Add(faceData);
            }

            // Selection
            if (selectedVertices != null && selectedVertices.Count > 0)
            {
                contextData.selectedVertices = selectedVertices.ToList();
            }

            // Materials（アセットパスとして保存）
            if (materials != null)
            {
                foreach (var mat in materials)
                {
                    if (mat != null)
                    {
                        string assetPath = AssetDatabase.GetAssetPath(mat);
                        contextData.materials.Add(assetPath ?? "");
                    }
                    else
                    {
                        contextData.materials.Add("");  // null は空文字列
                    }
                }
            }

            contextData.currentMaterialIndex = currentMaterialIndex;

            return contextData;
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
        // 変換: MeshContextData → MeshObject
        // ================================================================

        /// <summary>
        /// MeshContextDataをMeshObjectに変換
        /// </summary>
        public static MeshObject ToMeshObject(MeshContextData meshContext)
        {
            if (meshContext == null)
                return null;

            var meshObject = new MeshObject(meshContext.name);

            // Vertices
            foreach (var vd in meshContext.vertices)
            {
                var vertex = new Vertex(vd.GetPosition());
                vertex.UVs = vd.GetUVs();
                vertex.Normals = vd.GetNormals();
                meshObject.Vertices.Add(vertex);
            }

            // Faces（MaterialIndex含む）
            foreach (var fd in meshContext.faces)
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
        public static List<Material> ToMaterials(MeshContextData meshContextData)
        {
            var result = new List<Material>();

            if (meshContextData?.materials == null || meshContextData.materials.Count == 0)
            {
                // デフォルトでスロット0を追加
                result.Add(null);
                return result;
            }

            foreach (var path in meshContextData.materials)
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
        public static HashSet<int> ToSelectedVertices(MeshContextData meshContext)
        {
            if (meshContext?.selectedVertices == null)
                return new HashSet<int>();

            return new HashSet<int>(meshContext.selectedVertices);
        }

        // ================================================================
        // ModelContext統合（Phase 5追加）
        // ================================================================

        /// <summary>
        /// ModelContextからModelDataを作成（エクスポート用）
        /// </summary>
        /// <param name="model">ModelContext</param>
        /// <param name="workPlane">WorkPlane（オプション）</param>
        /// <param name="editorState">EditorStateData（オプション）</param>
        /// <returns>シリアライズ可能なModelData</returns>
        public static ModelData FromModelContext(
            ModelContext model,
            WorkPlane workPlane = null,
            EditorStateData editorState = null)
        {
            if (model == null)
                return null;

            var modelData = new ModelData
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
                    modelData.meshContextList.Add(meshContextData);
                }
            }

            // WorkPlane
            if (workPlane != null)
            {
                modelData.workPlane = ToWorkPlaneData(workPlane);
            }

            // EditorState
            modelData.editorState = editorState;

            // ================================================================
            // Materials（Phase 1: モデル単位に集約）
            // ================================================================
            if (model.Materials != null)
            {
                foreach (var mat in model.Materials)
                {
                    if (mat == null)
                    {
                        modelData.materials.Add("");
                    }
                    else
                    {
                        string assetPath = UnityEditor.AssetDatabase.GetAssetPath(mat);
                        modelData.materials.Add(assetPath ?? "");
                    }
                }
            }
            modelData.currentMaterialIndex = model.CurrentMaterialIndex;

            // DefaultMaterials
            if (model.DefaultMaterials != null)
            {
                foreach (var mat in model.DefaultMaterials)
                {
                    if (mat == null)
                    {
                        modelData.defaultMaterials.Add("");
                    }
                    else
                    {
                        string assetPath = UnityEditor.AssetDatabase.GetAssetPath(mat);
                        modelData.defaultMaterials.Add(assetPath ?? "");
                    }
                }
            }
            modelData.defaultCurrentMaterialIndex = model.DefaultCurrentMaterialIndex;
            modelData.autoSetDefaultMaterials = model.AutoSetDefaultMaterials;

            return modelData;
        }

        /// <summary>
        /// ModelDataからModelContextを復元（インポート用）
        /// </summary>
        /// <param name="modelData">インポートしたModelData</param>
        /// <param name="model">復元先のModelContext（nullの場合は新規作成）</param>
        /// <returns>復元されたModelContext</returns>
        public static ModelContext ToModelContext(ModelData modelData, ModelContext model = null)
        {
            if (modelData == null)
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

            model.Name = modelData.name;
            model.FilePath = null;  // 呼び出し元で設定

            // MeshContextDataからMeshContextを復元
            foreach (var meshContextData in modelData.meshContextList)
            {
                var meshObject = ToMeshObject(meshContextData);
                if (meshObject == null) continue;

                // MeshTypeをパース
                SimpleMeshFactory.MeshType meshType = SimpleMeshFactory.MeshType.Mesh;
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
                    ExportSettings = ToExportSettings(meshContextData.exportSettings),
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
            if (modelData.materials != null && modelData.materials.Count > 0)
            {
                // 新形式: ModelData.materials から復元
                model.Materials.Clear();
                foreach (var path in modelData.materials)
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
                model.CurrentMaterialIndex = modelData.currentMaterialIndex;
            }
            else if (modelData.meshContextList.Count > 0)
            {
                // 旧形式: 最初の MeshContextData.materials から復元（後方互換）
                var firstMeshData = modelData.meshContextList[0];
                model.Materials = ToMaterials(firstMeshData);
                model.CurrentMaterialIndex = firstMeshData.currentMaterialIndex;
            }

            // DefaultMaterials 復元
            if (modelData.defaultMaterials != null && modelData.defaultMaterials.Count > 0)
            {
                model.DefaultMaterials.Clear();
                foreach (var path in modelData.defaultMaterials)
                {
                    Material mat = null;
                    if (!string.IsNullOrEmpty(path))
                    {
                        mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                    }
                    model.DefaultMaterials.Add(mat);
                }
                model.DefaultCurrentMaterialIndex = modelData.defaultCurrentMaterialIndex;
                model.AutoSetDefaultMaterials = modelData.autoSetDefaultMaterials;
            }

            return model;
        }

        /// <summary>
        /// MeshContextをMeshContextDataに変換（簡易版）
        /// </summary>
        public static MeshContextData FromMeshContext(MeshContext meshContext, HashSet<int> selectedVertices = null)
        {
            if (meshContext == null)
                return null;

            var contextData = ToMeshContextData(
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
        public static MeshContext ToMeshContext(MeshContextData contextData)
        {
            if (contextData == null)
                return null;

            var meshObject = ToMeshObject(contextData);
            if (meshObject == null)
                return null;

            // MeshTypeをパース
            SimpleMeshFactory.MeshType meshType = SimpleMeshFactory.MeshType.Mesh;
            if (!string.IsNullOrEmpty(contextData.type))
            {
                Enum.TryParse(contextData.type, out meshType);
            }

            return new MeshContext
            {
                Name = contextData.name ?? "UnityMesh",
                MeshObject = meshObject,
                UnityMesh = meshObject.ToUnityMesh(),
                OriginalPositions = meshObject.Vertices.Select(v => v.Position).ToArray(),
                ExportSettings = ToExportSettings(contextData.exportSettings),
                // Phase 1: Materials は ModelContext に集約
                // オブジェクト属性
                Type = meshType,
                ParentIndex = contextData.parentIndex,
                Depth = contextData.depth,
                IsVisible = contextData.isVisible,
                IsLocked = contextData.isLocked,
                // ミラー設定
                MirrorType = contextData.mirrorType,
                MirrorAxis = contextData.mirrorAxis,
                MirrorDistance = contextData.mirrorDistance
            };
        }

        /// <summary>
        /// EditorStateDataを作成
        /// </summary>
        public static EditorStateData CreateEditorStateData(
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
            return new EditorStateData
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
            ModelData modelData,
            int meshIndex,
            HashSet<int> selectedVertices)
        {
            if (modelData == null || meshIndex < 0 || meshIndex >= modelData.meshContextList.Count)
                return;

            if (selectedVertices != null && selectedVertices.Count > 0)
            {
                modelData.meshContextList[meshIndex].selectedVertices = selectedVertices.ToList();
            }
        }
    }
}