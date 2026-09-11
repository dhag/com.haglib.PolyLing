// ModelSerializer_ModelContext.cs
// ModelContext ⇔ ModelDTO の統合変換と復元（モデルレベルの付帯データを含む）。
// Runtime/Poly_Ling_Main/Core/Serialization/ に配置

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Materials;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;
using Poly_Ling.Ops;

namespace Poly_Ling.Serialization
{
    public static partial class ModelSerializer
    {
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

            // WorkAxisContext（作業用ローカル軸）
            //   WorkPlane と違い引数では受け取らず model から直接読む。
            //   引数渡しにすると呼び出し側が null を渡したときに黙って失われるため。
            modelDTO.workAxis = ToWorkAxisData(model.WorkAxis);

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
            // ObjectGroups
            // ================================================================

            SaveObjectGroupsToDTO(model, modelDTO);

            // ================================================================
            // DataStore
            // ================================================================

            SaveDataStoreToDTO(model, modelDTO);

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
            // スプリングボーン・評価設定（モデルレベル）
            // ================================================================

            modelDTO.springBoneFixedDeltaTime = model.SpringBoneFixedDeltaTime;
            modelDTO.springBoneWarmupFrames = model.SpringBoneWarmupFrames;

            // ================================================================
            // TPoseバックアップ（規約4：CSV/JSON 対称）
            // ================================================================

            modelDTO.tPoseBackup = ToTPoseBackupDTO(model.TPoseBackup);

            // ================================================================
            // VRM 1.0 モデルレベル設定（規約4：CSV/JSON 対称）
            // ================================================================

            modelDTO.vrmMeta   = ToVrmMetaDTO(model.VrmMeta);
            modelDTO.vrmLookAt = ToVrmLookAtDTO(model.VrmLookAt);

            // ================================================================
            // Avatar リターゲット設定（規約4：CSV/JSON 対称）
            // ================================================================

            modelDTO.avatarRetarget = ToAvatarRetargetDTO(model.AvatarRetarget);

            // ================================================================
            // PMX / MQO の座標規約（規約4：CSV/JSON 対称）
            // ================================================================

            modelDTO.coordinateConvention = ToCoordinateConventionDTO(model.CoordinateConvention);

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
                    MirrorGeometryDerived  = meshContextData.mirrorGeometryDerived,
                    DetachedMirrorObjectId = meshContextData.detachedMirrorObjectId,
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
            // ObjectGroups復元
            // ================================================================

            LoadObjectGroupsFromDTO(modelDTO, model);

            // ================================================================
            // DataStore復元
            // ================================================================

            LoadDataStoreFromDTO(modelDTO, model);

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
            // スプリングボーン・評価設定復元（モデルレベル）
            // ================================================================

            model.SpringBoneFixedDeltaTime = modelDTO.springBoneFixedDeltaTime;
            model.SpringBoneWarmupFrames = modelDTO.springBoneWarmupFrames;

            // ================================================================
            // TPoseバックアップ復元（規約4：CSV/JSON 対称）
            // ================================================================

            if (modelDTO.tPoseBackup != null)
                model.TPoseBackup = FromTPoseBackupDTO(modelDTO.tPoseBackup);

            // ================================================================
            // VRM 1.0 モデルレベル設定復元（規約4：CSV/JSON 対称）
            // ================================================================

            model.VrmMeta   = FromVrmMetaDTO(modelDTO.vrmMeta);
            model.VrmLookAt = FromVrmLookAtDTO(modelDTO.vrmLookAt);

            // ================================================================
            // Avatar リターゲット設定復元（規約4：CSV/JSON 対称）
            // ================================================================

            model.AvatarRetarget = FromAvatarRetargetDTO(modelDTO.avatarRetarget);

            // ================================================================
            // PMX / MQO の座標規約復元（規約4：CSV/JSON 対称）
            // ================================================================

            model.CoordinateConvention = FromCoordinateConventionDTO(modelDTO.coordinateConvention);

            // ================================================================
            // WorkAxis復元（作業用ローカル軸。規約4：CSV/JSON 対称）
            // ================================================================

            if (model.WorkAxis == null) model.WorkAxis = new WorkAxisContext();
            if (modelDTO.workAxis != null)
                ApplyToWorkAxis(modelDTO.workAxis, model.WorkAxis);
            else
                model.WorkAxis.Reset();

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
                contextData.mirrorGeometryDerived  = meshContext.MirrorGeometryDerived;
                contextData.detachedMirrorObjectId = meshContext.DetachedMirrorObjectId;
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
                contextData.mirrorBoneIndex = meshContext.MeshObject?.MirrorBoneIndex ?? -1;

                // Humanoid マッスル可動域（per-bone・#5d-1）
                SaveHumanLimitDataToDTO(meshContext, contextData);

                // 一人称カメラでの扱い（per-mesh）
                contextData.vrmFirstPersonType =
                    (int)(meshContext.MeshObject?.VrmFirstPerson ?? VrmFirstPersonType.Auto);

                // ノード制約（VRMC_node_constraint）
                SaveVrmConstraintToDTO(meshContext, contextData);
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
                MirrorGeometryDerived  = meshDTO.mirrorGeometryDerived,
                DetachedMirrorObjectId = meshDTO.detachedMirrorObjectId,
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
                meshContext.MeshObject.MirrorBoneIndex = meshDTO.mirrorBoneIndex;

            // Humanoid マッスル可動域（per-bone・#5d-1）
            LoadHumanLimitDataFromDTO(meshDTO, meshContext);

            // 一人称カメラでの扱い（per-mesh）。欄を持たない旧データは 0=Auto。
            if (meshContext.MeshObject != null)
                meshContext.MeshObject.VrmFirstPerson =
                    ToVrmFirstPersonType(meshDTO.vrmFirstPersonType);

            // ノード制約（VRMC_node_constraint）。欄を持たない旧データは null。
            LoadVrmConstraintFromDTO(meshDTO, meshContext);

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
    }
}
