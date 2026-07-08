// Assets/Editor/Poly_Ling/Serialization/ModelSerializer.cs
// モデルファイル (.mfmodel) のインポート/エクスポート
// Phase7: マルチマテリアル対応版
// Phase5: ModelContext統合
// Phase Morph: モーフ基準データ対応
// Phase BonePose: BonePoseData対応

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Unity.Plastic.Newtonsoft.Json;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Materials;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;
using Poly_Ling.Ops;

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
                isTriangulated = meshObject.IsTriangulated
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
            //廃止　meshDTO.currentMaterialIndex = currentMaterialIndex;


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
            meshObject.IsTriangulated = meshDTO.isTriangulated;

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

            // IK: 集約 Links → per-bone（EffectorBoneName / MeshObject.IKLink）を同期してから保存
            Poly_Ling.Ops.IKChainResolver.SyncPerBoneFromLinks(model);

            // Humanoid: 集中 Dict → per-bone（MeshObject.HumanBodyBone）を同期してから保存
            Poly_Ling.Ops.HumanoidMappingResolver.SyncPerBoneFromMapping(model);

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
            //   ※#5b: モデルレベル Dict の保存は撤去。per-bone（MeshDTO.humanBodyBone）へ。
            //     FromModelContext 冒頭の SyncPerBoneFromMapping で per-bone を確定済み。

            // ================================================================
            // MorphExpressions
            // ================================================================

            SaveMorphExpressionsToDTO(model, modelDTO);

            // ================================================================
            // MeshSelectionSets
            // ================================================================

            SaveMeshSelectionSetsToDTO(model, modelDTO);

            // ================================================================
            // MirrorPairs
            // ================================================================

            SaveMirrorPairsToDTO(model, modelDTO);

            // ================================================================
            // スプリングボーン・コライダーグループ名（モデルレベル：名前のみ）
            // ================================================================

            modelDTO.springBoneColliderGroupNames =
                (model.SpringBoneColliderGroupNames != null)
                    ? new List<string>(model.SpringBoneColliderGroupNames)
                    : new List<string>();

            // ================================================================
            // TPoseバックアップ（規約4：CSV/JSON 対称）
            // ================================================================

            modelDTO.tPoseBackup = ToTPoseBackupDTO(model.TPoseBackup);

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
                    OriginalPositions = (Vector3[])meshObject.Positions.Clone(),
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

                // BonePoseData復元（Phase BonePose追加）
                LoadBonePoseDataFromDTO(meshContextData, context);

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
            {/*
                // 旧形式のデータがあれば警告
                if ((modelDTO.materials != null && modelDTO.materials.Count > 0) ||
                    (modelDTO.meshDTOList.Count > 0 && modelDTO.meshDTOList[0].materialPathList?.Count > 0))
                {
                    Debug.LogWarning("[ModelSerializer] 旧形式のマテリアルデータは廃止されました。デフォルトマテリアルで初期化します。");
                }
                */
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
                //if (modelDTO.defaultMaterials != null && modelDTO.defaultMaterials.Count > 0)
                //{
                //    Debug.LogWarning("[ModelSerializer] 旧形式のデフォルトマテリアルデータは廃止されました。");
                //}
                model.DefaultMaterialReferences = new List<MaterialReference> { new MaterialReference() };
                model.DefaultCurrentMaterialIndex = 0;
                model.AutoSetDefaultMaterials = modelDTO.autoSetDefaultMaterials;
            }

            // ================================================================
            // Humanoidボーンマッピング復元
            // ================================================================
            //   ※#5b: Dict の直接復元は撤去。per-bone（humanBodyBone）読込後、
            //     ToModelContext 末尾の RebuildMappingFromPerBone で Dict を再構築する。

            // ================================================================
            // MorphExpressions復元
            // ================================================================

            LoadMorphExpressionsFromDTO(modelDTO, model);

            // ================================================================
            // MeshSelectionSets復元
            // ================================================================

            LoadMeshSelectionSetsFromDTO(modelDTO, model);

            // ================================================================
            // MirrorPairs復元
            // ================================================================

            LoadMirrorPairsFromDTO(modelDTO, model);

            // ================================================================
            // スプリングボーン・コライダーグループ名復元（モデルレベル）
            // ================================================================

            model.SpringBoneColliderGroupNames =
                (modelDTO.springBoneColliderGroupNames != null)
                    ? new List<string>(modelDTO.springBoneColliderGroupNames)
                    : new List<string>();

            // ================================================================
            // TPoseバックアップ復元（規約4：CSV/JSON 対称）
            // ================================================================

            if (modelDTO.tPoseBackup != null)
                model.TPoseBackup = FromTPoseBackupDTO(modelDTO.tPoseBackup);

            // IK: per-bone → 集約 Links / TargetIndex を再構築（消費側は集約を読む）
            Poly_Ling.Ops.IKChainResolver.RebuildLinksFromPerBone(model);

            // Humanoid: per-bone → 集中 Dict を再構築（消費側は Dict を読む）
            Poly_Ling.Ops.HumanoidMappingResolver.RebuildMappingFromPerBone(model);

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

                // BonePoseData（Phase BonePose追加）
                SaveBonePoseDataToDTO(meshContext, contextData);

                // 永続化拡張（DTO単一真実源化）
                SaveIKDataToDTO(meshContext, contextData);
                SaveIKLinkDataToDTO(meshContext, contextData);
                SaveBindPoseToDTO(meshContext, contextData);
                SaveBoneModelRotationToDTO(meshContext, contextData);
                SaveRigidBodyDataToDTO(meshContext, contextData);
                SaveJointDataToDTO(meshContext, contextData);
                SaveSpringBoneDataToDTO(meshContext, contextData);

                // Humanoid 割当（per-bone・#5b）
                contextData.humanBodyBone = meshContext.MeshObject?.HumanBodyBone;

                // Humanoid マッスル可動域（per-bone・#5d-1）
                SaveHumanLimitDataToDTO(meshContext, contextData);
            }

            return contextData;
        }

        /// <summary>
        /// MeshContextDataからMeshContextを復元（簡易版）
        /// </summary>
        public static MeshContext ToMeshContext(MeshDTO meshDTO)
        {
            return ToMeshContext(meshDTO, true);
        }

        /// <summary>
        /// MeshDTO → MeshContext。buildUnityMesh=false で Unityメッシュ生成をスキップする
        /// （CSV直列化のように頂点/面データのみ必要な用途向け。保存時の不要なメッシュ生成を回避）。
        /// </summary>
        public static MeshContext ToMeshContext(MeshDTO meshDTO, bool buildUnityMesh)
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
                UnityMesh = buildUnityMesh ? meshObject.ToUnityMeshShared() : null,
                OriginalPositions = (Vector3[])meshObject.Positions.Clone(),
                BoneTransform = ToBoneTransform(meshDTO.exportSettingsDTO),
                // Phase 1: Materials は ModelContext に集約
                // オブジェクト属性
                Type = meshType,
                ParentIndex = meshDTO.parentIndex,
                HierarchyParentIndex = meshDTO.hierarchyParentIndex,
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

            // BonePoseData復元（Phase BonePose追加）
            LoadBonePoseDataFromDTO(meshDTO, meshContext);

            // 永続化拡張（DTO単一真実源化）
            LoadIKDataFromDTO(meshDTO, meshContext);
            LoadIKLinkDataFromDTO(meshDTO, meshContext);
            LoadBindPoseFromDTO(meshDTO, meshContext);
            LoadBoneModelRotationFromDTO(meshDTO, meshContext);
            LoadRigidBodyDataFromDTO(meshDTO, meshContext);
            LoadJointDataFromDTO(meshDTO, meshContext);
            LoadSpringBoneDataFromDTO(meshDTO, meshContext);

            // Humanoid 割当（per-bone・#5b）
            if (meshContext.MeshObject != null)
                meshContext.MeshObject.HumanBodyBone = meshDTO.humanBodyBone ?? "";

            // Humanoid マッスル可動域（per-bone・#5d-1）
            LoadHumanLimitDataFromDTO(meshDTO, meshContext);

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

            if (meshContext.PartsSelectionSetList != null)
            {
                foreach (var set in meshContext.PartsSelectionSetList)
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

            meshContext.PartsSelectionSetList = new List<Selection.PartsSelectionSet>();

            if (meshDTO.selectionSets != null)
            {
                foreach (var dto in meshDTO.selectionSets)
                {
                    var set = dto?.ToSelectionSet();
                    if (set != null)
                    {
                        meshContext.PartsSelectionSetList.Add(set);
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

            // モーフ親インデックス
            meshDTO.morphParentIndex = meshContext.MorphParentIndex;

            // エクスポート除外フラグ
            meshDTO.excludeFromExport = meshContext.ExcludeFromExport;
            meshDTO.ignorePoseInArmature = meshContext.IgnorePoseInArmature;
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

            // モーフ親インデックス
            meshContext.MorphParentIndex = meshDTO.morphParentIndex;

            // エクスポート除外フラグ
            meshContext.ExcludeFromExport = meshDTO.excludeFromExport;
            meshContext.IgnorePoseInArmature = meshDTO.ignorePoseInArmature;
        }

        // ================================================================
        // MorphExpressions シリアライズ
        // ================================================================

        /// <summary>
        /// ModelContextのモーフエクスプレッションをModelDTOに保存
        /// </summary>
        public static void SaveMorphExpressionsToDTO(ModelContext model, ModelDTO modelDTO)
        {
            if (model == null || modelDTO == null) return;

            modelDTO.morphExpressions = new List<MorphExpressionDTO>();

            if (model.MorphExpressions != null)
            {
                foreach (var set in model.MorphExpressions)
                {
                    var dto = MorphExpressionDTO.FromMorphExpression(set);
                    if (dto != null)
                    {
                        modelDTO.morphExpressions.Add(dto);
                    }
                }
            }
        }

        /// <summary>
        /// ModelDTOのモーフエクスプレッションをModelContextに復元
        /// </summary>
        public static void LoadMorphExpressionsFromDTO(ModelDTO modelDTO, ModelContext model)
        {
            if (modelDTO == null || model == null) return;

            model.MorphExpressions = new List<Data.MorphExpression>();

            if (modelDTO.morphExpressions != null)
            {
                foreach (var dto in modelDTO.morphExpressions)
                {
                    var set = dto?.ToMorphExpression();
                    if (set != null)
                    {
                        model.MorphExpressions.Add(set);
                    }
                }
            }
        }

        // ================================================================
        // MirrorPairs シリアライズ
        // ================================================================

        /// <summary>
        /// ModelContextのMirrorPairsをModelDTOに保存
        /// </summary>
        public static void SaveMirrorPairsToDTO(ModelContext model, ModelDTO modelDTO)
        {
            if (model == null || modelDTO == null) return;

            modelDTO.mirrorPairs = new List<MirrorPairDTO>();

            if (model.MirrorPairs != null)
            {
                foreach (var pair in model.MirrorPairs)
                {
                    int realIdx = model.MeshContextList.IndexOf(pair.Real);
                    int mirrorIdx = model.MeshContextList.IndexOf(pair.Mirror);
                    if (realIdx < 0 || mirrorIdx < 0) continue;

                    modelDTO.mirrorPairs.Add(new MirrorPairDTO
                    {
                        realIndex = realIdx,
                        mirrorIndex = mirrorIdx,
                        axis = (int)pair.Axis
                    });
                }
            }
        }

        /// <summary>
        /// ModelDTOのMirrorPairsをModelContextに復元
        /// MeshContextList構築後に呼ぶこと。Build()でVertexMap/BonePairMapを再構築する。
        /// </summary>
        public static void LoadMirrorPairsFromDTO(ModelDTO modelDTO, ModelContext model)
        {
            if (modelDTO == null || model == null) return;

            model.MirrorPairs = new List<Data.MirrorPair>();

            if (modelDTO.mirrorPairs == null) return;

            foreach (var dto in modelDTO.mirrorPairs)
            {
                if (dto.realIndex < 0 || dto.realIndex >= model.Count) continue;
                if (dto.mirrorIndex < 0 || dto.mirrorIndex >= model.Count) continue;

                var realCtx = model.GetMeshContext(dto.realIndex);
                var mirrorCtx = model.GetMeshContext(dto.mirrorIndex);
                if (realCtx == null || mirrorCtx == null) continue;

                var pair = new Data.MirrorPair
                {
                    Real = realCtx,
                    Mirror = mirrorCtx,
                    Axis = (Poly_Ling.Symmetry.SymmetryAxis)dto.axis
                };

                if (pair.Build())
                {
                    model.MirrorPairs.Add(pair);
                    Debug.Log($"[ModelSerializer] Restored MirrorPair: {realCtx.Name} ↔ {mirrorCtx.Name} (VertexMap={pair.VertexMap.Length}, BonePairMap={pair.BonePairMap.Count})");
                }
                else
                {
                    Debug.LogWarning($"[ModelSerializer] Failed to rebuild MirrorPair: {realCtx.Name} ↔ {mirrorCtx.Name}: {pair.BuildLog}");
                }
            }
        }

        // ================================================================
        // MeshSelectionSets シリアライズ
        // ================================================================

        /// <summary>
        /// ModelContextのメッシュ選択セットをModelDTOに保存
        /// </summary>
        public static void SaveMeshSelectionSetsToDTO(ModelContext model, ModelDTO modelDTO)
        {
            if (model == null || modelDTO == null) return;

            modelDTO.meshSelectionSets = new List<Data.MeshSelectionSetDTO>();

            if (model.MeshSelectionSets != null)
            {
                foreach (var set in model.MeshSelectionSets)
                {
                    var dto = Data.MeshSelectionSetDTO.FromMeshSelectionSet(set);
                    if (dto != null)
                        modelDTO.meshSelectionSets.Add(dto);
                }
            }
        }

        /// <summary>
        /// ModelDTOのメッシュ選択セットをModelContextに復元
        /// </summary>
        public static void LoadMeshSelectionSetsFromDTO(ModelDTO modelDTO, ModelContext model)
        {
            if (modelDTO == null || model == null) return;

            model.MeshSelectionSets = new List<Data.MeshSelectionSet>();

            if (modelDTO.meshSelectionSets != null)
            {
                foreach (var dto in modelDTO.meshSelectionSets)
                {
                    var set = dto?.ToMeshSelectionSet();
                    if (set != null)
                        model.MeshSelectionSets.Add(set);
                }
            }
        }

        // ================================================================
        // BonePoseData シリアライズ（Phase BonePose追加）
        // ================================================================

        /// <summary>
        /// MeshContextのBonePoseDataをMeshDTOに保存
        /// </summary>
        public static void SaveBonePoseDataToDTO(MeshContext meshContext, MeshDTO meshDTO)
        {
            if (meshContext == null || meshDTO == null) return;

            if (meshContext.BonePoseData != null)
            {
                meshDTO.bonePoseData = meshContext.BonePoseData.ToDTO();
            }
            else
            {
                meshDTO.bonePoseData = null;
            }
        }

        /// <summary>
        /// MeshDTOのBonePoseDataをMeshContextに復元
        /// </summary>
        public static void LoadBonePoseDataFromDTO(MeshDTO meshDTO, MeshContext meshContext)
        {
            if (meshDTO == null || meshContext == null) return;

            if (meshDTO.bonePoseData != null)
            {
                meshContext.BonePoseData = BonePoseData.FromDTO(meshDTO.bonePoseData);
            }
            else
            {
                meshContext.BonePoseData = null;
            }
        }

        // ================================================================
        // 永続化拡張（DTO単一真実源化）：IK / BindPose / BoneModelRotation / 剛体 / JOINT
        //   POCO（Poly_Ling.Data）⇔ DTO（フィールド型）の変換。
        // ================================================================

        public static void SaveIKDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var ik = mc?.MeshObject?.IKData;
            if (ik == null) { dto.ikData = null; return; }

            dto.ikData = new IKDataDTO
            {
                isIK = ik.IsIK,
                effectorBoneName = ik.EffectorBoneName ?? "",
                loopCount = ik.LoopCount,
                limitAngle = ik.LimitAngle
            };
        }

        public static void LoadIKDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.ikData == null || mc?.MeshObject == null) return;

            var d = dto.ikData;
            // Links / TargetIndex は読込後に IKChainResolver.RebuildLinksFromPerBone で
            // 再構築する（本段階では per-bone 表現のみ復元）。
            mc.MeshObject.IKData = new IKData
            {
                IsIK = d.isIK,
                EffectorBoneName = d.effectorBoneName ?? "",
                LoopCount = d.loopCount,
                LimitAngle = d.limitAngle,
                Links = new List<IKLinkInfo>()
            };
        }

        public static void SaveIKLinkDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var lk = mc?.MeshObject?.IKLink;
            if (lk == null) { dto.ikLink = null; return; }
            dto.ikLink = new IKLinkDataDTO
            {
                hasLimit = lk.HasLimit,
                limitMin = SerVec3(lk.LimitMin),
                limitMax = SerVec3(lk.LimitMax)
            };
        }

        public static void LoadIKLinkDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.ikLink == null || mc?.MeshObject == null) return;
            var d = dto.ikLink;
            mc.MeshObject.IKLink = new IKLinkData
            {
                HasLimit = d.hasLimit,
                LimitMin = SerVec3(d.limitMin),
                LimitMax = SerVec3(d.limitMax)
            };
        }

        public static void SaveHumanLimitDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var hl = mc?.MeshObject?.HumanLimit;
            if (hl == null) { dto.humanLimit = null; return; }
            dto.humanLimit = new HumanLimitDataDTO
            {
                min = SerVec3(hl.Min),
                max = SerVec3(hl.Max),
                center = SerVec3(hl.Center),
                axisLength = hl.AxisLength,
                useDefaultValues = hl.UseDefaultValues
            };
        }

        public static void LoadHumanLimitDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.humanLimit == null || mc?.MeshObject == null) return;
            var d = dto.humanLimit;
            mc.MeshObject.HumanLimit = new HumanLimitData
            {
                Min = SerVec3(d.min),
                Max = SerVec3(d.max),
                Center = SerVec3(d.center),
                AxisLength = d.axisLength,
                UseDefaultValues = d.useDefaultValues
            };
        }

        public static void SaveBindPoseToDTO(MeshContext mc, MeshDTO dto)
        {
            if (mc == null) return;
            var m = mc.BindPose;
            if (m == Matrix4x4.identity) { dto.bindPose = null; return; }
            dto.bindPose = new[]
            {
                m.m00, m.m01, m.m02, m.m03,
                m.m10, m.m11, m.m12, m.m13,
                m.m20, m.m21, m.m22, m.m23,
                m.m30, m.m31, m.m32, m.m33
            };
        }

        public static void LoadBindPoseFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.bindPose == null || dto.bindPose.Length < 16 || mc == null) return;
            var a = dto.bindPose;
            var m = new Matrix4x4();
            m.m00 = a[0];  m.m01 = a[1];  m.m02 = a[2];  m.m03 = a[3];
            m.m10 = a[4];  m.m11 = a[5];  m.m12 = a[6];  m.m13 = a[7];
            m.m20 = a[8];  m.m21 = a[9];  m.m22 = a[10]; m.m23 = a[11];
            m.m30 = a[12]; m.m31 = a[13]; m.m32 = a[14]; m.m33 = a[15];
            mc.BindPose = m;
        }

        public static void SaveBoneModelRotationToDTO(MeshContext mc, MeshDTO dto)
        {
            if (mc == null) return;
            var q = mc.BoneModelRotation;
            if (q == Quaternion.identity) { dto.boneModelRotation = null; return; }
            dto.boneModelRotation = new[] { q.x, q.y, q.z, q.w };
        }

        public static void LoadBoneModelRotationFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.boneModelRotation == null || dto.boneModelRotation.Length < 4 || mc == null) return;
            var a = dto.boneModelRotation;
            mc.BoneModelRotation = new Quaternion(a[0], a[1], a[2], a[3]);
        }

        public static void SaveRigidBodyDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var rb = mc?.MeshObject?.RigidBodyData;
            if (rb == null) { dto.rigidBodyData = null; return; }
            dto.rigidBodyData = new RigidBodyDataDTO
            {
                nameEnglish = rb.NameEnglish ?? "",
                relatedBoneName = rb.RelatedBoneName ?? "",
                boneIndex = rb.BoneIndex,
                group = rb.Group,
                collisionMask = rb.CollisionMask,
                shape = (int)rb.Shape,
                size = SerVec3(rb.Size),
                position = SerVec3(rb.Position),
                rotation = SerVec3(rb.Rotation),
                mass = rb.Mass,
                linearDamping = rb.LinearDamping,
                angularDamping = rb.AngularDamping,
                restitution = rb.Restitution,
                friction = rb.Friction,
                physicsMode = (int)rb.PhysicsMode
            };
        }

        public static void LoadRigidBodyDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.rigidBodyData == null || mc?.MeshObject == null) return;
            var d = dto.rigidBodyData;
            mc.MeshObject.RigidBodyData = new RigidBodyData
            {
                NameEnglish = d.nameEnglish ?? "",
                RelatedBoneName = d.relatedBoneName ?? "",
                BoneIndex = d.boneIndex,
                Group = d.group,
                CollisionMask = (ushort)d.collisionMask,
                Shape = (RigidBodyShape)d.shape,
                Size = SerVec3(d.size),
                Position = SerVec3(d.position),
                Rotation = SerVec3(d.rotation),
                Mass = d.mass,
                LinearDamping = d.linearDamping,
                AngularDamping = d.angularDamping,
                Restitution = d.restitution,
                Friction = d.friction,
                PhysicsMode = (RigidBodyPhysicsMode)d.physicsMode
            };
        }

        public static void SaveJointDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var jd = mc?.MeshObject?.JointData;
            if (jd == null) { dto.jointData = null; return; }
            dto.jointData = new JointDataDTO
            {
                nameEnglish = jd.NameEnglish ?? "",
                jointType = jd.JointType,
                bodyAName = jd.BodyAName ?? "",
                bodyBName = jd.BodyBName ?? "",
                rigidBodyIndexA = jd.RigidBodyIndexA,
                rigidBodyIndexB = jd.RigidBodyIndexB,
                position = SerVec3(jd.Position),
                rotation = SerVec3(jd.Rotation),
                translationMin = SerVec3(jd.TranslationMin),
                translationMax = SerVec3(jd.TranslationMax),
                rotationMin = SerVec3(jd.RotationMin),
                rotationMax = SerVec3(jd.RotationMax),
                springTranslation = SerVec3(jd.SpringTranslation),
                springRotation = SerVec3(jd.SpringRotation)
            };
        }

        public static void LoadJointDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            if (dto?.jointData == null || mc?.MeshObject == null) return;
            var d = dto.jointData;
            mc.MeshObject.JointData = new JointData
            {
                NameEnglish = d.nameEnglish ?? "",
                JointType = d.jointType,
                BodyAName = d.bodyAName ?? "",
                BodyBName = d.bodyBName ?? "",
                RigidBodyIndexA = d.rigidBodyIndexA,
                RigidBodyIndexB = d.rigidBodyIndexB,
                Position = SerVec3(d.position),
                Rotation = SerVec3(d.rotation),
                TranslationMin = SerVec3(d.translationMin),
                TranslationMax = SerVec3(d.translationMax),
                RotationMin = SerVec3(d.rotationMin),
                RotationMax = SerVec3(d.rotationMax),
                SpringTranslation = SerVec3(d.springTranslation),
                SpringRotation = SerVec3(d.springRotation)
            };
        }

        // ================================================================
        // スプリングボーン（VRM SpringBone）POCO⇔DTO 変換
        //   コライダー(複数)・ジョイント・チェーンルート をまとめて往復させる。
        //   いずれか非nullのボーンのみDTOに書き出す（null=当該属性なし）。
        // ================================================================

        public static void SaveSpringBoneDataToDTO(MeshContext mc, MeshDTO dto)
        {
            var mo = mc?.MeshObject;
            if (mo == null)
            {
                dto.springBoneColliders = null;
                dto.springBoneJoint = null;
                dto.springBoneChainRoot = null;
                return;
            }

            // コライダー（複数）
            if (mo.SpringBoneColliders != null && mo.SpringBoneColliders.Count > 0)
            {
                var list = new List<SpringBoneColliderDataDTO>(mo.SpringBoneColliders.Count);
                foreach (var c in mo.SpringBoneColliders)
                {
                    if (c == null) continue;
                    list.Add(new SpringBoneColliderDataDTO
                    {
                        shape = (int)c.Shape,
                        offset = SerVec3(c.Offset),
                        radius = c.Radius,
                        tail = SerVec3(c.Tail),
                        normal = SerVec3(c.Normal),
                        groupIndices = c.SpringBoneGroupIndices != null
                            ? new List<int>(c.SpringBoneGroupIndices)
                            : new List<int>()
                    });
                }
                dto.springBoneColliders = list.Count > 0 ? list : null;
            }
            else
            {
                dto.springBoneColliders = null;
            }

            // ジョイント
            var j = mo.SpringBoneJoint;
            dto.springBoneJoint = (j == null) ? null : new SpringBoneJointDataDTO
            {
                hitRadius = j.HitRadius,
                stiffnessForce = j.StiffnessForce,
                gravityPower = j.GravityPower,
                gravityDir = SerVec3(j.GravityDir),
                dragForce = j.DragForce
            };

            // チェーンルート
            var ch = mo.SpringBoneChainRoot;
            dto.springBoneChainRoot = (ch == null) ? null : new SpringBoneChainDataDTO
            {
                name = ch.Name ?? "",
                colliderGroupIndices = ch.SpringBoneColliderGroupIndices != null
                    ? new List<int>(ch.SpringBoneColliderGroupIndices)
                    : new List<int>(),
                centerBoneName = ch.CenterBoneName ?? ""
            };
        }

        public static void LoadSpringBoneDataFromDTO(MeshDTO dto, MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (dto == null || mo == null) return;

            // コライダー（複数）
            if (dto.springBoneColliders != null && dto.springBoneColliders.Count > 0)
            {
                var list = new List<SpringBoneColliderData>(dto.springBoneColliders.Count);
                foreach (var d in dto.springBoneColliders)
                {
                    if (d == null) continue;
                    list.Add(new SpringBoneColliderData
                    {
                        Shape = (SpringBoneColliderShape)d.shape,
                        Offset = SerVec3(d.offset),
                        Radius = d.radius,
                        Tail = SerVec3(d.tail),
                        Normal = SerVec3(d.normal),
                        SpringBoneGroupIndices = d.groupIndices != null
                            ? new List<int>(d.groupIndices)
                            : new List<int>()
                    });
                }
                mo.SpringBoneColliders = list.Count > 0 ? list : null;
            }
            else
            {
                mo.SpringBoneColliders = null;
            }

            // ジョイント
            var jd = dto.springBoneJoint;
            mo.SpringBoneJoint = (jd == null) ? null : new SpringBoneJointData
            {
                HitRadius = jd.hitRadius,
                StiffnessForce = jd.stiffnessForce,
                GravityPower = jd.gravityPower,
                GravityDir = SerVec3(jd.gravityDir),
                DragForce = jd.dragForce
            };

            // チェーンルート
            var cd = dto.springBoneChainRoot;
            mo.SpringBoneChainRoot = (cd == null) ? null : new SpringBoneChainData
            {
                Name = cd.name ?? "",
                SpringBoneColliderGroupIndices = cd.colliderGroupIndices != null
                    ? new List<int>(cd.colliderGroupIndices)
                    : new List<int>(),
                CenterBoneName = cd.centerBoneName ?? ""
            };
        }

        // Vector3 ⇔ float[3]（本拡張専用の小ヘルパ）
        private static float[] SerVec3(Vector3 v) => new[] { v.x, v.y, v.z };
        private static Vector3 SerVec3(float[] a) =>
            (a != null && a.Length >= 3) ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;

        // ================================================================
        // TPoseBackup ⇔ TPoseBackupDTO（規約4：CSV/JSON 対称）
        //   参照は MeshContext index（実体が index キー）。座標系変換なし（生値）。
        // ================================================================

        // Matrix4x4 ⇔ float[16]（row-major）
        private static float[] SerMat(Matrix4x4 m) => new[]
        {
            m.m00, m.m01, m.m02, m.m03,
            m.m10, m.m11, m.m12, m.m13,
            m.m20, m.m21, m.m22, m.m23,
            m.m30, m.m31, m.m32, m.m33
        };
        private static Matrix4x4 SerMat(float[] a)
        {
            var m = new Matrix4x4();
            if (a == null || a.Length < 16) return m;
            m.m00 = a[0];  m.m01 = a[1];  m.m02 = a[2];  m.m03 = a[3];
            m.m10 = a[4];  m.m11 = a[5];  m.m12 = a[6];  m.m13 = a[7];
            m.m20 = a[8];  m.m21 = a[9];  m.m22 = a[10]; m.m23 = a[11];
            m.m30 = a[12]; m.m31 = a[13]; m.m32 = a[14]; m.m33 = a[15];
            return m;
        }

        public static TPoseBackupDTO ToTPoseBackupDTO(TPoseBackup backup)
        {
            if (backup == null) return null;

            var dto = new TPoseBackupDTO();

            if (backup.BoneRotations != null)
                foreach (var kv in backup.BoneRotations)
                    dto.boneRotations.Add(new TPoseBoneRotDTO { index = kv.Key, rot = SerVec3(kv.Value) });

            if (backup.WorldMatrices != null)
                foreach (var kv in backup.WorldMatrices)
                    dto.worldMatrices.Add(new TPoseMatrixDTO { index = kv.Key, m = SerMat(kv.Value) });

            if (backup.BindPoses != null)
                foreach (var kv in backup.BindPoses)
                    dto.bindPoses.Add(new TPoseMatrixDTO { index = kv.Key, m = SerMat(kv.Value) });

            if (backup.VertexPositions != null)
            {
                foreach (var kv in backup.VertexPositions)
                {
                    var arr = kv.Value;
                    var flat = new float[(arr?.Length ?? 0) * 3];
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Length; i++)
                        {
                            flat[i * 3]     = arr[i].x;
                            flat[i * 3 + 1] = arr[i].y;
                            flat[i * 3 + 2] = arr[i].z;
                        }
                    }
                    dto.vertexPositions.Add(new TPoseVtxPosDTO { index = kv.Key, p = flat });
                }
            }

            return dto;
        }

        public static TPoseBackup FromTPoseBackupDTO(TPoseBackupDTO dto)
        {
            if (dto == null) return null;

            var backup = new TPoseBackup();

            if (dto.boneRotations != null)
                foreach (var d in dto.boneRotations)
                    if (d != null) backup.BoneRotations[d.index] = SerVec3(d.rot);

            if (dto.worldMatrices != null)
                foreach (var d in dto.worldMatrices)
                    if (d != null) backup.WorldMatrices[d.index] = SerMat(d.m);

            if (dto.bindPoses != null)
                foreach (var d in dto.bindPoses)
                    if (d != null) backup.BindPoses[d.index] = SerMat(d.m);

            if (dto.vertexPositions != null)
            {
                foreach (var d in dto.vertexPositions)
                {
                    if (d == null) continue;
                    var flat = d.p ?? System.Array.Empty<float>();
                    int count = flat.Length / 3;
                    var arr = new Vector3[count];
                    for (int i = 0; i < count; i++)
                        arr[i] = new Vector3(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]);
                    backup.VertexPositions[d.index] = arr;
                }
            }

            return backup;
        }

        // ================================================================
        // MeshMetaDTO 変換（Phase 1）
        // MeshContext ↔ MeshMetaDTO（ジオメトリなし）
        // ================================================================

        /// <summary>
        /// MeshContext → MeshMetaDTO（メタデータのみ抽出）
        /// ジオメトリ（頂点・面）は含まない
        /// </summary>
        public static MeshMetaDTO ToMeshMetaDTO(MeshContext mc)
        {
            if (mc == null) return null;

            var meta = new MeshMetaDTO
            {
                name                    = mc.Name,
                type                    = mc.Type.ToString(),
                isVisible               = mc.IsVisible,
                isLocked                = mc.IsLocked,
                isFolding               = mc.IsFolding,
                depth                   = mc.Depth,
                parentIndex             = mc.ParentIndex,
                hierarchyParentIndex    = mc.HierarchyParentIndex,
                mirrorType              = mc.MirrorType,
                mirrorAxis              = mc.MirrorAxis,
                mirrorDistance          = mc.MirrorDistance,
                mirrorMaterialOffset    = mc.MirrorMaterialOffset,
                bakedMirrorSourceIndex  = mc.BakedMirrorSourceIndex,
                hasBakedMirrorChild     = mc.HasBakedMirrorChild,
                morphParentIndex        = mc.MorphParentIndex,
                excludeFromExport       = mc.ExcludeFromExport,
                ignorePoseInArmature    = mc.IgnorePoseInArmature,
                exportSettingsDTO       = ToBoneTransformDTO(mc.BoneTransform),
            };

            // モーフ基準データ
            if (mc.IsMorph)
                meta.morphBaseData = ToMorphBaseDataDTO(mc.MorphBaseData);

            // BonePoseData
            if (mc.BonePoseData != null)
                meta.bonePoseData = mc.BonePoseData.ToDTO();

            // 選択セット
            meta.selectionSets = new System.Collections.Generic.List<SelectionSetDTO>();
            if (mc.PartsSelectionSetList != null)
            {
                foreach (var set in mc.PartsSelectionSetList)
                {
                    var dto = SelectionSetDTO.FromSelectionSet(set);
                    if (dto != null) meta.selectionSets.Add(dto);
                }
            }

            return meta;
        }

        /// <summary>
        /// MeshMetaDTO → MeshContext（MeshObject は空、呼び出し元で設定すること）
        /// </summary>
        public static MeshContext ToMeshContextFromMeta(MeshMetaDTO meta)
        {
            if (meta == null) return null;

            MeshType meshType = MeshType.Mesh;
            if (!string.IsNullOrEmpty(meta.type))
                Enum.TryParse(meta.type, out meshType);

            var mc = new MeshContext
            {
                Name                   = meta.name ?? "Untitled",
                Type                   = meshType,
                IsVisible              = meta.isVisible,
                IsLocked               = meta.isLocked,
                IsFolding              = meta.isFolding,
                Depth                  = meta.depth,
                ParentIndex            = meta.parentIndex,
                HierarchyParentIndex   = meta.hierarchyParentIndex,
                MirrorType             = meta.mirrorType,
                MirrorAxis             = meta.mirrorAxis,
                MirrorDistance         = meta.mirrorDistance,
                MirrorMaterialOffset   = meta.mirrorMaterialOffset,
                BakedMirrorSourceIndex = meta.bakedMirrorSourceIndex,
                HasBakedMirrorChild    = meta.hasBakedMirrorChild,
                MorphParentIndex       = meta.morphParentIndex,
                ExcludeFromExport      = meta.excludeFromExport,
                IgnorePoseInArmature   = meta.ignorePoseInArmature,
                BoneTransform          = meta.exportSettingsDTO != null
                                         ? ToBoneTransform(meta.exportSettingsDTO)
                                         : null,
            };

            // モーフ基準データ
            if (meta.morphBaseData != null)
                mc.MorphBaseData = ToMorphBaseData(meta.morphBaseData);

            // BonePoseData
            if (meta.bonePoseData != null)
                mc.BonePoseData = BonePoseData.FromDTO(meta.bonePoseData);

            // 選択セット
            mc.PartsSelectionSetList = new System.Collections.Generic.List<Selection.PartsSelectionSet>();
            if (meta.selectionSets != null)
            {
                foreach (var dto in meta.selectionSets)
                {
                    var set = dto?.ToSelectionSet();
                    if (set != null) mc.PartsSelectionSetList.Add(set);
                }
            }

            return mc;
        }

        // ================================================================
        // MeshGeoDTO 変換（Phase 1）
        // MeshObject ↔ MeshGeoDTO
        // ================================================================

        /// <summary>
        /// MeshObject → MeshGeoDTO
        /// </summary>
        public static MeshGeoDTO ToMeshGeoDTO(MeshObject meshObject, int meshIndex)
        {
            if (meshObject == null) return null;

            var geo = new MeshGeoDTO
            {
                meshIndex  = meshIndex,
                isTriangulated = meshObject.IsTriangulated,
            };

            foreach (var vertex in meshObject.Vertices)
            {
                var vd = new VertexDTO();
                vd.id = vertex.Id;
                vd.SetPosition(vertex.Position);
                vd.SetUVs(vertex.UVs);
                vd.SetNormals(vertex.Normals);
                vd.SetBoneWeight(vertex.BoneWeight);
                vd.SetMirrorBoneWeight(vertex.MirrorBoneWeight);
                vd.f = (byte)vertex.Flags;
                geo.vertices.Add(vd);
            }

            foreach (var face in meshObject.Faces)
            {
                geo.faces.Add(new FaceDTO
                {
                    id  = face.Id,
                    v   = new System.Collections.Generic.List<int>(face.VertexIndices),
                    uvi = new System.Collections.Generic.List<int>(face.UVIndices),
                    ni  = new System.Collections.Generic.List<int>(face.NormalIndices),
                    mi  = face.MaterialIndex != 0 ? face.MaterialIndex : (int?)null,
                    f   = (byte)face.Flags,
                });
            }

            return geo;
        }

        /// <summary>
        /// MeshGeoDTO → MeshObject
        /// </summary>
        public static MeshObject ToMeshObjectFromGeo(MeshGeoDTO geo)
        {
            if (geo == null) return null;

            // ToMeshObject はMeshDTOを受け取るため、最小限のMeshDTOに詰め替えて委譲
            var tmp = new MeshDTO
            {
                isTriangulated = geo.isTriangulated,
                vertices   = geo.vertices ?? new System.Collections.Generic.List<VertexDTO>(),
                faces      = geo.faces    ?? new System.Collections.Generic.List<FaceDTO>(),
            };
            return ToMeshObject(tmp);
        }
    }
}
