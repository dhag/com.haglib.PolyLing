// Assets/Editor/Poly_Ling/Serialization/ModelSerializer.cs
// モデルファイル (.mfmodel) のインポート/エクスポート
// Phase7: マルチマテリアル対応版
// Phase5: ModelContext統合
// Phase Morph: モーフ基準データ対応

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Unity.Plastic.Newtonsoft.Json;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Model;
using Poly_Ling.Materials;
using Poly_Ling.Selection;

namespace Poly_Ling.Serialization
{
    /// <summary>
    /// モデルファイルのシリアライザ
    /// </summary>
    public static partial class ModelSerializer
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
            BoneTransform exportSettings,
            HashSet<int> selectedVertices,
            List<Material> materials = null,
            int currentMaterialIndex = 0)
        {
            if (meshObject == null)
                return null;

            var meshDTO = new MeshDTO
            {
                name = name ?? meshObject.Name ?? "Untitled",
                isExpanded = meshObject.IsExpanded
            };

            // BoneTransform
            meshDTO.exportSettingsDTO = ToBoneTransformDTO(exportSettings);

            // Vertices
            foreach (var vertex in meshObject.Vertices)
            {
                var vertexDTO = new VertexDTO();
                vertexDTO.id = vertex.Id;
                vertexDTO.SetPosition(vertex.Position);
                vertexDTO.SetUVs(vertex.UVs);
                vertexDTO.SetNormals(vertex.Normals);
                vertexDTO.SetBoneWeight(vertex.BoneWeight);
                vertexDTO.SetMirrorBoneWeight(vertex.MirrorBoneWeight);
                vertexDTO.f = (byte)vertex.Flags;
                meshDTO.vertices.Add(vertexDTO);
            }

            // Faces（MaterialIndex含む）
            foreach (var face in meshObject.Faces)
            {
                var faceData = new FaceDTO
                {
                    id = face.Id,
                    v = new List<int>(face.VertexIndices),
                    uvi = new List<int>(face.UVIndices),
                    ni = new List<int>(face.NormalIndices),
                    mi = face.MaterialIndex != 0 ? face.MaterialIndex : (int?)null,  // 0はデフォルトなので省略
                    f = (byte)face.Flags
                };
                meshDTO.faces.Add(faceData);
            }

            // Selection
            if (selectedVertices != null && selectedVertices.Count > 0)
            {
                meshDTO.selectedVertices = selectedVertices.ToList();
            }

            // 注: materialPathList への書き込みは廃止
            // マテリアルは ModelDTO.materialReferences で一元管理
            meshDTO.currentMaterialIndex = currentMaterialIndex;

            return meshDTO;
        }

        /// <summary>
        /// BoneTransformをBoneTransformDTOに変換
        /// </summary>
        public static BoneTransformDTO ToBoneTransformDTO(BoneTransform settings)
        {
            if (settings == null)
                return BoneTransformDTO.CreateDefault();

            var data = new BoneTransformDTO
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
            meshObject.IsExpanded = meshDTO.isExpanded;

            // Vertices
            foreach (var vd in meshDTO.vertices)
            {
                var vertex = new Vertex(vd.GetPosition());
                vertex.Id = vd.id;
                vertex.UVs = vd.GetUVs();
                vertex.Normals = vd.GetNormals();
                vertex.BoneWeight = vd.GetBoneWeight();
                vertex.MirrorBoneWeight = vd.GetMirrorBoneWeight();
                vertex.Flags = (VertexFlags)vd.f;
                meshObject.Vertices.Add(vertex);
            }

            // Faces（MaterialIndex含む）
            foreach (var fd in meshDTO.faces)
            {
                var face = new Face
                {
                    Id = fd.id,
                    VertexIndices = new List<int>(fd.v ?? new List<int>()),
                    UVIndices = new List<int>(fd.uvi ?? new List<int>()),
                    NormalIndices = new List<int>(fd.ni ?? new List<int>()),
                    MaterialIndex = fd.mi ?? 0,  // nullの場合は0
                    Flags = (FaceFlags)fd.f
                };
                meshObject.Faces.Add(face);
            }

            return meshObject;
        }

        /// <summary>
        /// マテリアルリストを復元
        /// </summary>
        /// <remarks>
        /// [廃止] MeshDTO.materialPathList形式は廃止されました。
        /// マテリアルはModelDTO.materialReferencesで一元管理されます。
        /// </remarks>
        [System.Obsolete("MeshDTO.materialPathList形式は廃止されました。ModelDTO.materialReferencesを使用してください。")]
        public static List<Material> ToMaterials(MeshDTO meshDTO)
        {
            Debug.LogWarning("[ModelSerializer] ToMaterials()は廃止されました。");
            return new List<Material> { null };
        }

        /// <summary>
        /// BoneTransformDTOをBoneTransformに変換
        /// </summary>
        public static BoneTransform ToBoneTransform(BoneTransformDTO data)
        {
            if (data == null)
                return new BoneTransform();

            return new BoneTransform
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

            // 新形式: MaterialReferences → MaterialReferenceDTO
            if (model.MaterialReferences != null)
            {
                foreach (var matRef in model.MaterialReferences)
                {
                    var dto = ToMaterialReferenceDTO(matRef);
                    modelDTO.materialReferences.Add(dto);
                    // 注: 旧形式(materials)への書き込みは廃止
                }
            }
            modelDTO.currentMaterialIndex = model.CurrentMaterialIndex;

            // DefaultMaterialReferences
            if (model.DefaultMaterialReferences != null)
            {
                foreach (var matRef in model.DefaultMaterialReferences)
                {
                    var dto = ToMaterialReferenceDTO(matRef);
                    modelDTO.defaultMaterialReferences.Add(dto);
                    // 注: 旧形式(defaultMaterials)への書き込みは廃止
                }
            }
            modelDTO.defaultCurrentMaterialIndex = model.DefaultCurrentMaterialIndex;
            modelDTO.autoSetDefaultMaterials = model.AutoSetDefaultMaterials;

            // ================================================================
            // Humanoidボーンマッピング
            // ================================================================
            
            if (model.HumanoidMapping != null && !model.HumanoidMapping.IsEmpty)
            {
                modelDTO.humanoidBoneMapping = model.HumanoidMapping.ToDictionary();
            }

            // ================================================================
            // MorphSets
            // ================================================================

            SaveMorphSetsToDTO(model, modelDTO);

            return modelDTO;
        }

        // ================================================================
        // ModelSerializer.cs Part 2 - ToModelContext以降
        // このファイルはPart1と結合して使用してください
        // ================================================================

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
                    BoneTransform = ToBoneTransform(meshContextData.exportSettingsDTO),
                    // Materials は ModelData から復元するため、ここでは設定しない
                    // オブジェクト属性
                    Type = meshType,
                    ParentIndex = meshContextData.parentIndex,
                    Depth = meshContextData.depth,
                    HierarchyParentIndex = meshContextData.hierarchyParentIndex,
                    IsVisible = meshContextData.isVisible,
                    IsLocked = meshContextData.isLocked,
                    IsFolding = meshContextData.isFolding,
                    // ミラー設定
                    MirrorType = meshContextData.mirrorType,
                    MirrorAxis = meshContextData.mirrorAxis,
                    MirrorDistance = meshContextData.mirrorDistance,
                    MirrorMaterialOffset = meshContextData.mirrorMaterialOffset,
                    // ベイクミラー
                    BakedMirrorSourceIndex = meshContextData.bakedMirrorSourceIndex,
                    HasBakedMirrorChild = meshContextData.hasBakedMirrorChild
                };

                // 選択セットを復元
                LoadSelectionSetsFromDTO(meshContextData, context);

                // モーフデータを復元（Phase Morph追加）
                LoadMorphDataFromDTO(meshContextData, context);

                model.Add(context);
            }

            // ================================================================
            // Materials 復元（Phase 1: モデル単位に集約）
            // ================================================================

            // 新形式: materialReferences から復元
            if (modelDTO.materialReferences != null && modelDTO.materialReferences.Count > 0)
            {
                var matRefs = new List<MaterialReference>();
                foreach (var dto in modelDTO.materialReferences)
                {
                    matRefs.Add(ToMaterialReference(dto));
                }
                model.MaterialReferences = matRefs;
                model.CurrentMaterialIndex = modelDTO.currentMaterialIndex;
            }
            // 旧形式・最古形式は廃止: デフォルトマテリアルで初期化
            else
            {
                // 旧形式のデータがあれば警告
                if ((modelDTO.materials != null && modelDTO.materials.Count > 0) ||
                    (modelDTO.meshDTOList.Count > 0 && modelDTO.meshDTOList[0].materialPathList?.Count > 0))
                {
                    Debug.LogWarning("[ModelSerializer] 旧形式のマテリアルデータは廃止されました。デフォルトマテリアルで初期化します。");
                }
                // デフォルトのマテリアル参照を設定
                model.MaterialReferences = new List<MaterialReference> { new MaterialReference() };
                model.CurrentMaterialIndex = 0;
            }

            // DefaultMaterialReferences 復元
            if (modelDTO.defaultMaterialReferences != null && modelDTO.defaultMaterialReferences.Count > 0)
            {
                var matRefs = new List<MaterialReference>();
                foreach (var dto in modelDTO.defaultMaterialReferences)
                {
                    matRefs.Add(ToMaterialReference(dto));
                }
                model.DefaultMaterialReferences = matRefs;
                model.DefaultCurrentMaterialIndex = modelDTO.defaultCurrentMaterialIndex;
                model.AutoSetDefaultMaterials = modelDTO.autoSetDefaultMaterials;
            }
            // 旧形式は廃止: デフォルト値で初期化
            else
            {
                if (modelDTO.defaultMaterials != null && modelDTO.defaultMaterials.Count > 0)
                {
                    Debug.LogWarning("[ModelSerializer] 旧形式のデフォルトマテリアルデータは廃止されました。");
                }
                model.DefaultMaterialReferences = new List<MaterialReference> { new MaterialReference() };
                model.DefaultCurrentMaterialIndex = 0;
                model.AutoSetDefaultMaterials = modelDTO.autoSetDefaultMaterials;
            }

            // ================================================================
            // Humanoidボーンマッピング復元
            // ================================================================
            
            if (modelDTO.humanoidBoneMapping != null && modelDTO.humanoidBoneMapping.Count > 0)
            {
                model.HumanoidMapping.FromDictionary(modelDTO.humanoidBoneMapping);
            }

            // ================================================================
            // MorphSets復元
            // ================================================================

            LoadMorphSetsFromDTO(modelDTO, model);

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
                meshContext.BoneTransform,
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
                contextData.isFolding = meshContext.IsFolding;

                // ミラー設定
                contextData.mirrorType = meshContext.MirrorType;
                contextData.mirrorAxis = meshContext.MirrorAxis;
                contextData.mirrorDistance = meshContext.MirrorDistance;
                contextData.mirrorMaterialOffset = meshContext.MirrorMaterialOffset;

                // ベイクミラー
                contextData.bakedMirrorSourceIndex = meshContext.BakedMirrorSourceIndex;
                contextData.hasBakedMirrorChild = meshContext.HasBakedMirrorChild;

                // 選択セット
                SaveSelectionSetsToDTO(meshContext, contextData);

                // モーフデータ（Phase Morph追加）
                SaveMorphDataToDTO(meshContext, contextData);
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

            var meshContext = new MeshContext
            {
                Name = meshDTO.name ?? "UnityMesh",
                MeshObject = meshObject,
                UnityMesh = meshObject.ToUnityMeshShared(),
                OriginalPositions = meshObject.Vertices.Select(v => v.Position).ToArray(),
                BoneTransform = ToBoneTransform(meshDTO.exportSettingsDTO),
                // Phase 1: Materials は ModelContext に集約
                // オブジェクト属性
                Type = meshType,
                ParentIndex = meshDTO.parentIndex,
                Depth = meshDTO.depth,
                IsVisible = meshDTO.isVisible,
                IsLocked = meshDTO.isLocked,
                IsFolding = meshDTO.isFolding,
                // ミラー設定
                MirrorType = meshDTO.mirrorType,
                MirrorAxis = meshDTO.mirrorAxis,
                MirrorDistance = meshDTO.mirrorDistance,
                MirrorMaterialOffset = meshDTO.mirrorMaterialOffset,
                // ベイクミラー
                BakedMirrorSourceIndex = meshDTO.bakedMirrorSourceIndex,
                HasBakedMirrorChild = meshDTO.hasBakedMirrorChild
            };

            // 選択セットを復元
            LoadSelectionSetsFromDTO(meshDTO, meshContext);

            // モーフデータを復元（Phase Morph追加）
            LoadMorphDataFromDTO(meshDTO, meshContext);

            return meshContext;
        }

        /// <summary>
        /// EditorStateDTOを作成
        /// v2.0: カテゴリ別選択インデックス対応
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
            int selectedBoneIndex = -1,
            int selectedVertexMorphIndex = -1,
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
                selectedBoneIndex = selectedBoneIndex,
                selectedVertexMorphIndex = selectedVertexMorphIndex,
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

        // ================================================================
        // MaterialReference ⇔ MaterialReferenceDTO 変換
        // ================================================================

        /// <summary>
        /// MaterialReference → MaterialReferenceDTO
        /// </summary>
        public static MaterialReferenceDTO ToMaterialReferenceDTO(MaterialReference matRef)
        {
            if (matRef == null)
                return MaterialReferenceDTO.Create();

            var dto = new MaterialReferenceDTO
            {
                assetPath = matRef.AssetPath,
                data = ToMaterialDataDTO(matRef.Data)
            };

            return dto;
        }

        /// <summary>
        /// MaterialReferenceDTO → MaterialReference
        /// </summary>
        public static MaterialReference ToMaterialReference(MaterialReferenceDTO dto)
        {
            if (dto == null)
                return new MaterialReference();

            var matRef = new MaterialReference
            {
                AssetPath = dto.assetPath,
                Data = ToMaterialData(dto.data)
            };

            return matRef;
        }

        /// <summary>
        /// MaterialData → MaterialDataDTO
        /// </summary>
        public static MaterialDataDTO ToMaterialDataDTO(MaterialData data)
        {
            if (data == null)
                return new MaterialDataDTO();

            return new MaterialDataDTO
            {
                name = data.Name,
                shaderType = data.ShaderType.ToString(),
                baseColor = data.BaseColor,
                baseMapPath = data.BaseMapPath,
                sourceTexturePath = data.SourceTexturePath,
                sourceAlphaMapPath = data.SourceAlphaMapPath,
                sourceBumpMapPath = data.SourceBumpMapPath,
                metallic = data.Metallic,
                smoothness = data.Smoothness,
                metallicMapPath = data.MetallicMapPath,
                normalMapPath = data.NormalMapPath,
                normalScale = data.NormalScale,
                occlusionMapPath = data.OcclusionMapPath,
                occlusionStrength = data.OcclusionStrength,
                emissionEnabled = data.EmissionEnabled,
                emissionColor = data.EmissionColor,
                emissionMapPath = data.EmissionMapPath,
                surface = (int)data.Surface,
                blendMode = (int)data.BlendMode,
                cullMode = (int)data.CullMode,
                alphaClipEnabled = data.AlphaClipEnabled,
                alphaCutoff = data.AlphaCutoff
            };
        }

        /// <summary>
        /// MaterialDataDTO → MaterialData
        /// </summary>
        public static MaterialData ToMaterialData(MaterialDataDTO dto)
        {
            if (dto == null)
                return new MaterialData();

            var data = new MaterialData
            {
                Name = dto.name ?? "New Material",
                BaseColor = dto.baseColor ?? new float[] { 1f, 1f, 1f, 1f },
                BaseMapPath = dto.baseMapPath,
                SourceTexturePath = dto.sourceTexturePath,
                SourceAlphaMapPath = dto.sourceAlphaMapPath,
                SourceBumpMapPath = dto.sourceBumpMapPath,
                Metallic = dto.metallic,
                Smoothness = dto.smoothness,
                MetallicMapPath = dto.metallicMapPath,
                NormalMapPath = dto.normalMapPath,
                NormalScale = dto.normalScale,
                OcclusionMapPath = dto.occlusionMapPath,
                OcclusionStrength = dto.occlusionStrength,
                EmissionEnabled = dto.emissionEnabled,
                EmissionColor = dto.emissionColor ?? new float[] { 0f, 0f, 0f, 1f },
                EmissionMapPath = dto.emissionMapPath,
                Surface = (SurfaceType)dto.surface,
                BlendMode = (BlendModeType)dto.blendMode,
                CullMode = (CullModeType)dto.cullMode,
                AlphaClipEnabled = dto.alphaClipEnabled,
                AlphaCutoff = dto.alphaCutoff
            };

            // ShaderType をパース
            if (!string.IsNullOrEmpty(dto.shaderType) &&
                Enum.TryParse<ShaderType>(dto.shaderType, out var shaderType))
            {
                data.ShaderType = shaderType;
            }

            return data;
        }

        // ================================================================
        // SelectionSets シリアライズ
        // ================================================================

        /// <summary>
        /// MeshContextの選択セットをMeshDTOに保存
        /// </summary>
        public static void SaveSelectionSetsToDTO(MeshContext meshContext, MeshDTO meshDTO)
        {
            if (meshContext == null || meshDTO == null) return;

            meshDTO.selectionSets = new List<SelectionSetDTO>();

            if (meshContext.SelectionSets != null)
            {
                foreach (var set in meshContext.SelectionSets)
                {
                    var dto = SelectionSetDTO.FromSelectionSet(set);
                    if (dto != null)
                    {
                        meshDTO.selectionSets.Add(dto);
                    }
                }
            }
        }

        /// <summary>
        /// MeshDTOの選択セットをMeshContextに復元
        /// </summary>
        public static void LoadSelectionSetsFromDTO(MeshDTO meshDTO, MeshContext meshContext)
        {
            if (meshDTO == null || meshContext == null) return;

            meshContext.SelectionSets = new List<Selection.SelectionSet>();

            if (meshDTO.selectionSets != null)
            {
                foreach (var dto in meshDTO.selectionSets)
                {
                    var set = dto?.ToSelectionSet();
                    if (set != null)
                    {
                        meshContext.SelectionSets.Add(set);
                    }
                }
            }
        }

        // ================================================================
        // MorphBaseData シリアライズ（Phase: Morph対応）
        // ================================================================

        /// <summary>
        /// MorphBaseData → MorphBaseDataDTO
        /// </summary>
        public static MorphBaseDataDTO ToMorphBaseDataDTO(MorphBaseData data)
        {
            if (data == null || !data.IsValid)
                return null;

            var dto = new MorphBaseDataDTO
            {
                morphName = data.MorphName ?? "",
                panel = data.Panel,
                createdAt = data.CreatedAt.ToString("o")
            };

            // 基準位置
            dto.SetBasePositions(data.BasePositions);

            // 基準法線（存在する場合）
            if (data.HasNormals)
            {
                dto.SetBaseNormals(data.BaseNormals);
            }

            // 基準UV（存在する場合）
            if (data.HasUVs)
            {
                dto.SetBaseUVs(data.BaseUVs);
            }

            return dto;
        }

        /// <summary>
        /// MorphBaseDataDTO → MorphBaseData
        /// </summary>
        public static MorphBaseData ToMorphBaseData(MorphBaseDataDTO dto)
        {
            if (dto == null || dto.basePositions == null || dto.basePositions.Length == 0)
                return null;

            var data = new MorphBaseData
            {
                MorphName = dto.morphName ?? "",
                Panel = dto.panel,
                BasePositions = dto.GetBasePositions(),
                BaseNormals = dto.GetBaseNormals(),
                BaseUVs = dto.GetBaseUVs()
            };

            // 作成日時を復元
            if (!string.IsNullOrEmpty(dto.createdAt) && 
                DateTime.TryParse(dto.createdAt, out var createdAt))
            {
                data.CreatedAt = createdAt;
            }

            return data;
        }

        /// <summary>
        /// MeshContextのモーフデータをMeshDTOに保存
        /// </summary>
        public static void SaveMorphDataToDTO(MeshContext meshContext, MeshDTO meshDTO)
        {
            if (meshContext == null || meshDTO == null) return;

            // モーフ基準データ
            if (meshContext.IsMorph)
            {
                meshDTO.morphBaseData = ToMorphBaseDataDTO(meshContext.MorphBaseData);
            }
            else
            {
                meshDTO.morphBaseData = null;
            }

            // エクスポート除外フラグ
            meshDTO.excludeFromExport = meshContext.ExcludeFromExport;
        }

        /// <summary>
        /// MeshDTOのモーフデータをMeshContextに復元
        /// </summary>
        public static void LoadMorphDataFromDTO(MeshDTO meshDTO, MeshContext meshContext)
        {
            if (meshDTO == null || meshContext == null) return;

            // モーフ基準データ
            if (meshDTO.morphBaseData != null)
            {
                meshContext.MorphBaseData = ToMorphBaseData(meshDTO.morphBaseData);
            }
            else
            {
                meshContext.MorphBaseData = null;
            }

            // エクスポート除外フラグ
            meshContext.ExcludeFromExport = meshDTO.excludeFromExport;
        }

        // ================================================================
        // MorphSets シリアライズ
        // ================================================================

        /// <summary>
        /// ModelContextのモーフセットをModelDTOに保存
        /// </summary>
        public static void SaveMorphSetsToDTO(Model.ModelContext model, ModelDTO modelDTO)
        {
            if (model == null || modelDTO == null) return;

            modelDTO.morphSets = new List<MorphSetDTO>();

            if (model.MorphSets != null)
            {
                foreach (var set in model.MorphSets)
                {
                    var dto = MorphSetDTO.FromMorphSet(set);
                    if (dto != null)
                    {
                        modelDTO.morphSets.Add(dto);
                    }
                }
            }
        }

        /// <summary>
        /// ModelDTOのモーフセットをModelContextに復元
        /// </summary>
        public static void LoadMorphSetsFromDTO(ModelDTO modelDTO, Model.ModelContext model)
        {
            if (modelDTO == null || model == null) return;

            model.MorphSets = new List<Data.MorphSet>();

            if (modelDTO.morphSets != null)
            {
                foreach (var dto in modelDTO.morphSets)
                {
                    var set = dto?.ToMorphSet();
                    if (set != null)
                    {
                        model.MorphSets.Add(set);
                    }
                }
            }
        }
    }
}
