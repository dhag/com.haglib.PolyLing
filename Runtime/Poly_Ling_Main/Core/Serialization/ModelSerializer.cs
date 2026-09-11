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
                isTriangulated = meshObject.IsTriangulated,
                skinKind = (int)meshObject.SkinKind
            };

            // BoneTransform
            meshDTO.exportSettingsDTO = ToBoneTransformDTO(exportSettings);

            // Vertices
            foreach (var vertex in meshObject.Vertices)
            {
                var vertexDTO = new VertexDTO();
                vertexDTO.id = vertex.Id;
                vertexDTO.sid = vertex.SubId;
                vertexDTO.pid = vertex.PartsId;
                vertexDTO.SetPosition(vertex.Position);
                vertexDTO.SetUVs(vertex.UVs);
                vertexDTO.SetNormals(vertex.Normals);
                vertexDTO.SetBoneWeight(vertex.BoneWeight);
                vertexDTO.SetMirrorBoneWeight(vertex.MirrorBoneWeight);
                vertexDTO.SetControlPoints(vertex.ControlPoints);
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

        /// <summary>
        /// WorkAxisContextをWorkAxisDTOに変換。
        /// Origin はワールド座標のまま保存する（座標系変換は行わない）。
        /// </summary>
        public static WorkAxisDTO ToWorkAxisData(WorkAxisContext workAxis)
        {
            if (workAxis == null)
                return WorkAxisDTO.CreateDefault();

            var o = workAxis.Origin;
            var r = workAxis.Rotation;

            return new WorkAxisDTO
            {
                origin    = new float[] { o.x, o.y, o.z },
                rotation  = new float[] { r.x, r.y, r.z, r.w },
                isVisible = workAxis.IsVisible,
                length    = workAxis.Length
            };
        }

        /// <summary>
        /// WorkAxisDTOをWorkAxisContextに適用。
        /// </summary>
        public static void ApplyToWorkAxis(WorkAxisDTO data, WorkAxisContext workAxis)
        {
            if (data == null || workAxis == null)
                return;

            if (data.origin != null && data.origin.Length >= 3)
                workAxis.Origin = new Vector3(data.origin[0], data.origin[1], data.origin[2]);

            if (data.rotation != null && data.rotation.Length >= 4)
                workAxis.Rotation = new Quaternion(
                    data.rotation[0], data.rotation[1], data.rotation[2], data.rotation[3]);

            workAxis.IsVisible = data.isVisible;

            // length が無い旧データは 0 で入ってくる。Length の下限クランプに
            // 任せると 1e-3 という使い物にならない長さになるため、ここで弾く。
            if (data.length > 0f) workAxis.Length = data.length;
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
                vertex.SubId = vd.sid;
                vertex.PartsId = vd.pid;
                vertex.UVs = vd.GetUVs();
                vertex.Normals = vd.GetNormals();
                vertex.BoneWeight = vd.GetBoneWeight();
                vertex.MirrorBoneWeight = vd.GetMirrorBoneWeight();
                vertex.ControlPoints = vd.GetControlPoints();
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

            // 描画オブジェクトの種別。
            //   欄がある     … 保存された明示状態をそのまま復元する。
            //                   ウェイト頂点が 0 でも Skinned のままにする（明示状態の往復）。
            //   欄が無い(null) … 旧データ。頂点のボーンウェイトから求め直す。
            //                   ここで再計算しないと、旧プロジェクトのスキンドメッシュが
            //                   MeshFilter 扱いになり WorldMatrix が二重に掛かる。
            if (meshDTO.skinKind.HasValue)
                meshObject.SetSkinKind((SkinKind)meshDTO.skinKind.Value);
            else
                meshObject.RecomputeSkinKind();

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
                alphaCutoff = data.AlphaCutoff,
                shaderName = data.ShaderName,
                baseMapST = CloneST(data.BaseMapST),
                normalMapST = CloneST(data.NormalMapST),
                emissionMapST = CloneST(data.EmissionMapST),
                renderQueueOffset = data.RenderQueueOffset,
                zWriteOverride = data.ZWriteOverride,
                zTest = data.ZTest,
                doubleSidedGI = data.DoubleSidedGI,
                enableGPUInstancing = data.EnableGPUInstancing,
                shaderProperties = ToMaterialPropertyDTOList(data.ShaderProperties)
            };
        }

        // ================================================================
        // マテリアル拡張の小ヘルパー（ST / シェーダー固有プロパティ）
        // ================================================================

        private static float[] CloneST(float[] src)
        {
            if (src == null || src.Length < 4) return new float[] { 1f, 1f, 0f, 0f };
            return (float[])src.Clone();
        }

        private static List<MaterialPropertyDTO> ToMaterialPropertyDTOList(List<MaterialProperty> src)
        {
            if (src == null || src.Count == 0) return null;

            var list = new List<MaterialPropertyDTO>(src.Count);
            foreach (var p in src)
            {
                if (p == null) continue;
                list.Add(new MaterialPropertyDTO
                {
                    name = p.Name ?? "",
                    kind = (int)p.Kind,
                    x = p.X,
                    y = p.Y,
                    z = p.Z,
                    w = p.W,
                    texturePath = p.TexturePath
                });
            }
            return list.Count > 0 ? list : null;
        }

        private static List<MaterialProperty> ToMaterialPropertyList(List<MaterialPropertyDTO> src)
        {
            if (src == null || src.Count == 0) return null;

            var list = new List<MaterialProperty>(src.Count);
            foreach (var d in src)
            {
                if (d == null || string.IsNullOrEmpty(d.name)) continue;
                list.Add(new MaterialProperty
                {
                    Name = d.name,
                    Kind = (MaterialPropertyKind)d.kind,
                    X = d.x,
                    Y = d.y,
                    Z = d.z,
                    W = d.w,
                    TexturePath = d.texturePath
                });
            }
            return list.Count > 0 ? list : null;
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
                AlphaCutoff = dto.alphaCutoff,
                ShaderName = dto.shaderName,
                BaseMapST = CloneST(dto.baseMapST),
                NormalMapST = CloneST(dto.normalMapST),
                EmissionMapST = CloneST(dto.emissionMapST),
                RenderQueueOffset = dto.renderQueueOffset,
                ZWriteOverride = dto.zWriteOverride,
                ZTest = dto.zTest,
                DoubleSidedGI = dto.doubleSidedGI,
                EnableGPUInstancing = dto.enableGPUInstancing,
                ShaderProperties = ToMaterialPropertyList(dto.shaderProperties)
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

            // 法線再計算 除外セット
            meshDTO.normalExcludeSets = new List<SelectionSetDTO>();

            if (meshContext.NormalRecalcExcludeList != null)
            {
                foreach (var set in meshContext.NormalRecalcExcludeList)
                {
                    var dto = SelectionSetDTO.FromSelectionSet(set);
                    if (dto != null)
                    {
                        meshDTO.normalExcludeSets.Add(dto);
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

            // 法線再計算 除外セット（実体は MeshObject 側）
            if (meshContext.MeshObject != null)
            {
                meshContext.MeshObject.NormalRecalcExcludeList = new List<Selection.PartsSelectionSet>();

                if (meshDTO.normalExcludeSets != null)
                {
                    foreach (var dto in meshDTO.normalExcludeSets)
                    {
                        var set = dto?.ToSelectionSet();
                        if (set != null)
                        {
                            meshContext.MeshObject.NormalRecalcExcludeList.Add(set);
                        }
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

            // モーフのミラー適用（規約は MorphMirrorPolicy.cs を正典とする）
            meshDTO.morphMirrorPolicy  = (int)meshContext.MorphMirrorPolicy;
            meshDTO.mirrorOfMorphIndex = meshContext.MirrorOfMorphIndex;

            // エクスポート除外フラグ
            meshDTO.excludeFromExport = meshContext.ExcludeFromExport;
            meshDTO.ignorePoseInArmature = meshContext.IgnorePoseInArmature;
            meshDTO.isMirrorBranchRoot   = meshContext.IsMirrorBranchRoot;
            meshDTO.preserveNormals      = meshContext.PreserveNormals;
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

            // モーフのミラー適用（旧データは既定 FollowParent / -1 になる）
            meshContext.MorphMirrorPolicy  = (MorphMirrorPolicy)meshDTO.morphMirrorPolicy;
            meshContext.MirrorOfMorphIndex = meshDTO.mirrorOfMorphIndex;

            // エクスポート除外フラグ
            meshContext.ExcludeFromExport = meshDTO.excludeFromExport;
            meshContext.IgnorePoseInArmature = meshDTO.ignorePoseInArmature;
            meshContext.IsMirrorBranchRoot   = meshDTO.isMirrorBranchRoot;
            meshContext.PreserveNormals      = meshDTO.preserveNormals;
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
        // DataStore シリアライズ
        //
        // 【索引の付け替えが要らない】
        //   項目は対象を MasterIndex と ObjectId の両方で持つ。ObjectGroups と
        //   同じく、読み込み後の索引補正を掛けない。
        //
        // 【差し替えではなく中身の入れ替え】
        //   ModelContext.DataStore は読み取り専用のプロパティなので、
        //   ReplaceAll で中身だけを入れ替える。
        // ================================================================

        /// <summary>ModelContextの結果辞書をModelDTOに保存</summary>
        public static void SaveDataStoreToDTO(ModelContext model, ModelDTO modelDTO)
        {
            if (model == null || modelDTO == null) return;

            modelDTO.dataStore = new List<PLDataEntryDTO>();

            var store = model.DataStore;
            if (store == null) return;

            foreach (var entry in store.Entries)
            {
                var dto = PLDataEntryDTO.From(entry);
                if (dto != null)
                    modelDTO.dataStore.Add(dto);
            }
        }

        /// <summary>ModelDTOの結果辞書をModelContextに復元</summary>
        public static void LoadDataStoreFromDTO(ModelDTO modelDTO, ModelContext model)
        {
            if (modelDTO == null || model == null) return;

            var store = model.DataStore;
            if (store == null) return;

            var entries = new List<Data.PLDataEntry>();
            if (modelDTO.dataStore != null)
            {
                foreach (var dto in modelDTO.dataStore)
                {
                    var entry = dto?.ToEntry();
                    if (entry != null)
                        entries.Add(entry);
                }
            }

            store.ReplaceAll(entries);
        }

        // ================================================================
        // ObjectGroups シリアライズ
        //
        // 【索引の付け替えが要らない】
        //   ObjectGroup の参照は ObjectId で、保存往復でも値が変わらない。
        //   MorphExpressions（索引参照）のように読み込み後の補正が要らない。
        //
        // 【引けない参照は残す】
        //   参照先が保存に含まれていなくてもグループは捨てない。
        //   部分書き出し／部分読み込みで一時的に引けないことがあるため。
        //   参照切れは ModelInvariantChecker が報告し、片づけは
        //   ObjectGroupOps.PurgeMissing（明示操作）で行う。
        // ================================================================

        /// <summary>
        /// ModelContextのオブジェクトグループをModelDTOに保存
        /// </summary>
        public static void SaveObjectGroupsToDTO(ModelContext model, ModelDTO modelDTO)
        {
            if (model == null || modelDTO == null) return;

            modelDTO.objectGroups = new List<ObjectGroupDTO>();

            if (model.ObjectGroups != null)
            {
                foreach (var g in model.ObjectGroups)
                {
                    var dto = ObjectGroupDTO.FromObjectGroup(g);
                    if (dto != null)
                        modelDTO.objectGroups.Add(dto);
                }
            }
        }

        /// <summary>
        /// ModelDTOのオブジェクトグループをModelContextに復元
        /// </summary>
        public static void LoadObjectGroupsFromDTO(ModelDTO modelDTO, ModelContext model)
        {
            if (modelDTO == null || model == null) return;

            model.ObjectGroups = new List<Data.ObjectGroup>();

            if (modelDTO.objectGroups != null)
            {
                foreach (var dto in modelDTO.objectGroups)
                {
                    var g = dto?.ToObjectGroup();
                    if (g != null)
                        model.ObjectGroups.Add(g);
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

        // ================================================================
        // VRM 1.0 設定 POCO⇔DTO 変換
        //   enum は int で持つ。定義外の値が来ても落とさず既定へ丸める
        //   （手で CSV/JSON を書き換えたときに読めなくなるのを避ける）。
        // ================================================================

        /// <summary>int → VrmFirstPersonType。範囲外は Auto。</summary>
        public static VrmFirstPersonType ToVrmFirstPersonType(int v)
        {
            switch (v)
            {
                case 1:  return VrmFirstPersonType.Both;
                case 2:  return VrmFirstPersonType.ThirdPersonOnly;
                case 3:  return VrmFirstPersonType.FirstPersonOnly;
                default: return VrmFirstPersonType.Auto;
            }
        }

        /// <summary>VRM メタ情報 POCO → DTO。null は null のまま。</summary>
        public static VrmMetaDTO ToVrmMetaDTO(VrmMetaData m)
        {
            if (m == null) return null;
            return new VrmMetaDTO
            {
                name                 = m.Name ?? "",
                version              = m.Version ?? "",
                authors              = (m.Authors != null)
                                       ? new List<string>(m.Authors) : new List<string>(),
                copyrightInformation = m.CopyrightInformation ?? "",
                contactInformation   = m.ContactInformation ?? "",
                references           = (m.References != null)
                                       ? new List<string>(m.References) : new List<string>(),
                thirdPartyLicenses   = m.ThirdPartyLicenses ?? "",
                thumbnailPath        = m.ThumbnailPath ?? "",

                avatarPermission          = (int)m.AvatarPermission,
                violentUsage              = m.ViolentUsage,
                sexualUsage               = m.SexualUsage,
                commercialUsage           = (int)m.CommercialUsage,
                politicalOrReligiousUsage = m.PoliticalOrReligiousUsage,
                antisocialOrHateUsage     = m.AntisocialOrHateUsage,

                creditNotation  = (int)m.CreditNotation,
                redistribution  = m.Redistribution,
                modification    = (int)m.Modification,
                otherLicenseUrl = m.OtherLicenseUrl ?? "",
            };
        }

        /// <summary>VRM メタ情報 DTO → POCO。null は null のまま。</summary>
        public static VrmMetaData FromVrmMetaDTO(VrmMetaDTO d)
        {
            if (d == null) return null;
            return new VrmMetaData
            {
                Name                 = d.name ?? "",
                Version              = d.version ?? "",
                Authors              = (d.authors != null)
                                       ? new List<string>(d.authors) : new List<string>(),
                CopyrightInformation = d.copyrightInformation ?? "",
                ContactInformation   = d.contactInformation ?? "",
                References           = (d.references != null)
                                       ? new List<string>(d.references) : new List<string>(),
                ThirdPartyLicenses   = d.thirdPartyLicenses ?? "",
                ThumbnailPath        = d.thumbnailPath ?? "",

                AvatarPermission          = ToVrmAvatarPermission(d.avatarPermission),
                ViolentUsage              = d.violentUsage,
                SexualUsage               = d.sexualUsage,
                CommercialUsage           = ToVrmCommercialUsage(d.commercialUsage),
                PoliticalOrReligiousUsage = d.politicalOrReligiousUsage,
                AntisocialOrHateUsage     = d.antisocialOrHateUsage,

                CreditNotation  = ToVrmCreditNotation(d.creditNotation),
                Redistribution  = d.redistribution,
                Modification    = ToVrmModification(d.modification),
                OtherLicenseUrl = d.otherLicenseUrl ?? "",
            };
        }

        /// <summary>int → VrmAvatarPermission。範囲外は OnlyAuthor。</summary>
        public static VrmAvatarPermission ToVrmAvatarPermission(int v)
        {
            switch (v)
            {
                case 1:  return VrmAvatarPermission.OnlySeparatelyLicensedPerson;
                case 2:  return VrmAvatarPermission.Everyone;
                default: return VrmAvatarPermission.OnlyAuthor;
            }
        }

        /// <summary>int → VrmCommercialUsage。範囲外は PersonalNonProfit。</summary>
        public static VrmCommercialUsage ToVrmCommercialUsage(int v)
        {
            switch (v)
            {
                case 1:  return VrmCommercialUsage.PersonalProfit;
                case 2:  return VrmCommercialUsage.Corporation;
                default: return VrmCommercialUsage.PersonalNonProfit;
            }
        }

        /// <summary>int → VrmCreditNotation。範囲外は Required。</summary>
        public static VrmCreditNotation ToVrmCreditNotation(int v)
            => (v == 1) ? VrmCreditNotation.Unnecessary : VrmCreditNotation.Required;

        /// <summary>int → VrmModification。範囲外は Prohibited。</summary>
        public static VrmModification ToVrmModification(int v)
        {
            switch (v)
            {
                case 1:  return VrmModification.AllowModification;
                case 2:  return VrmModification.AllowModificationRedistribution;
                default: return VrmModification.Prohibited;
            }
        }

        /// <summary>VRM 視線設定 POCO → DTO。null は null のまま。</summary>
        public static VrmLookAtDTO ToVrmLookAtDTO(VrmLookAtData l)
        {
            if (l == null) return null;
            return new VrmLookAtDTO
            {
                offsetFromHead  = SerVec3(l.OffsetFromHead),
                lookAtType      = (int)l.LookAtType,
                horizontalInner = ToRangeMapDTO(l.HorizontalInner),
                horizontalOuter = ToRangeMapDTO(l.HorizontalOuter),
                verticalDown    = ToRangeMapDTO(l.VerticalDown),
                verticalUp      = ToRangeMapDTO(l.VerticalUp),
            };
        }

        /// <summary>VRM 視線設定 DTO → POCO。null は null のまま。</summary>
        public static VrmLookAtData FromVrmLookAtDTO(VrmLookAtDTO d)
        {
            if (d == null) return null;
            return new VrmLookAtData
            {
                // 欄が無い場合だけ UniVRM の既定 (0, 0.06, 0) に戻す。
                // SerVec3(null) は原点になるが、原点は「頭ボーンそのもの」で
                // 目の基準点としては別の意味になるため、ここでは使わない。
                OffsetFromHead  = (d.offsetFromHead != null && d.offsetFromHead.Length >= 3)
                                  ? SerVec3(d.offsetFromHead)
                                  : new Vector3(0f, 0.06f, 0f),
                LookAtType      = (d.lookAtType == 1) ? VrmLookAtType.Expression : VrmLookAtType.Bone,
                HorizontalInner = FromRangeMapDTO(d.horizontalInner),
                HorizontalOuter = FromRangeMapDTO(d.horizontalOuter),
                VerticalDown    = FromRangeMapDTO(d.verticalDown),
                VerticalUp      = FromRangeMapDTO(d.verticalUp),
            };
        }

        private static VrmLookAtRangeMapDTO ToRangeMapDTO(VrmLookAtRangeMap m)
        {
            var src = m ?? new VrmLookAtRangeMap();
            return new VrmLookAtRangeMapDTO
            {
                inputMaxDegrees = src.InputMaxDegrees,
                outputScale     = src.OutputScale,
            };
        }

        private static VrmLookAtRangeMap FromRangeMapDTO(VrmLookAtRangeMapDTO d)
        {
            if (d == null) return new VrmLookAtRangeMap();
            return new VrmLookAtRangeMap(d.inputMaxDegrees, d.outputScale);
        }

        /// <summary>Avatar リターゲット設定 POCO → DTO。null は null のまま。</summary>
        public static AvatarRetargetDTO ToAvatarRetargetDTO(AvatarRetargetData a)
        {
            if (a == null) return null;
            return new AvatarRetargetDTO
            {
                upperArmTwist     = a.UpperArmTwist,
                lowerArmTwist     = a.LowerArmTwist,
                upperLegTwist     = a.UpperLegTwist,
                lowerLegTwist     = a.LowerLegTwist,
                armStretch        = a.ArmStretch,
                legStretch        = a.LegStretch,
                feetSpacing       = a.FeetSpacing,
                hasTranslationDoF = a.HasTranslationDoF,
            };
        }

        /// <summary>PMX / MQO 座標規約 POCO → DTO。null は null のまま。</summary>
        public static CoordinateConventionDTO ToCoordinateConventionDTO(CoordinateConventionData c)
        {
            if (c == null) return null;
            return new CoordinateConventionDTO
            {
                pmxUnityRatio = c.PmxUnityRatio,
                pmxFlipX      = c.PmxFlipX,
                pmxFlipZ      = c.PmxFlipZ,
                mqoUnityRatio = c.MqoUnityRatio,
                mqoFlipX      = c.MqoFlipX,
                mqoFlipZ      = c.MqoFlipZ,
            };
        }

        /// <summary>PMX / MQO 座標規約 DTO → POCO。null は null のまま。</summary>
        public static CoordinateConventionData FromCoordinateConventionDTO(CoordinateConventionDTO d)
        {
            if (d == null) return null;
            return new CoordinateConventionData
            {
                PmxUnityRatio = d.pmxUnityRatio,
                PmxFlipX      = d.pmxFlipX,
                PmxFlipZ      = d.pmxFlipZ,
                MqoUnityRatio = d.mqoUnityRatio,
                MqoFlipX      = d.mqoFlipX,
                MqoFlipZ      = d.mqoFlipZ,
            };
        }

        /// <summary>Avatar リターゲット設定 DTO → POCO。null は null のまま。</summary>
        public static AvatarRetargetData FromAvatarRetargetDTO(AvatarRetargetDTO d)
        {
            if (d == null) return null;
            return new AvatarRetargetData
            {
                UpperArmTwist     = d.upperArmTwist,
                LowerArmTwist     = d.lowerArmTwist,
                UpperLegTwist     = d.upperLegTwist,
                LowerLegTwist     = d.lowerLegTwist,
                ArmStretch        = d.armStretch,
                LegStretch        = d.legStretch,
                FeetSpacing       = d.feetSpacing,
                HasTranslationDoF = d.hasTranslationDoF,
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
                dragForce = j.DragForce,
                angleLimitType = (int)j.AngleLimitType,
                limitRotation = SerQuat(j.LimitRotation),
                pitch = j.Pitch,
                yaw = j.Yaw
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
                DragForce = jd.dragForce,
                AngleLimitType = (SpringBoneAngleLimitType)jd.angleLimitType,
                LimitRotation = SerQuat(jd.limitRotation),
                Pitch = jd.pitch,
                Yaw = jd.yaw
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

        // Quaternion ⇔ float[4]。旧 JSON には無いので、
        // 欠けているときと長さ 0 のときは無回転に直す。
        private static float[] SerQuat(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
        private static Quaternion SerQuat(float[] a)
        {
            if (a == null || a.Length < 4) return Quaternion.identity;
            var q = new Quaternion(a[0], a[1], a[2], a[3]);
            if (q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w < 1e-12f) return Quaternion.identity;
            return q;
        }

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
                mirrorGeometryDerived   = mc.MirrorGeometryDerived,
                detachedMirrorObjectId  = mc.DetachedMirrorObjectId,
                hasBakedMirrorChild     = mc.HasBakedMirrorChild,
                morphParentIndex        = mc.MorphParentIndex,
                morphMirrorPolicy       = (int)mc.MorphMirrorPolicy,
                mirrorOfMorphIndex      = mc.MirrorOfMorphIndex,
                excludeFromExport       = mc.ExcludeFromExport,
                ignorePoseInArmature    = mc.IgnorePoseInArmature,
                isMirrorBranchRoot      = mc.IsMirrorBranchRoot,
                preserveNormals         = mc.PreserveNormals,
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
                MirrorGeometryDerived  = meta.mirrorGeometryDerived,
                DetachedMirrorObjectId = meta.detachedMirrorObjectId,
                HasBakedMirrorChild    = meta.hasBakedMirrorChild,
                MorphParentIndex       = meta.morphParentIndex,
                MorphMirrorPolicy      = (MorphMirrorPolicy)meta.morphMirrorPolicy,
                MirrorOfMorphIndex     = meta.mirrorOfMorphIndex,
                ExcludeFromExport      = meta.excludeFromExport,
                IgnorePoseInArmature   = meta.ignorePoseInArmature,
                IsMirrorBranchRoot     = meta.isMirrorBranchRoot,
                PreserveNormals        = meta.preserveNormals,
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
                skinKind   = (int)meshObject.SkinKind,
            };

            foreach (var vertex in meshObject.Vertices)
            {
                var vd = new VertexDTO();
                vd.id = vertex.Id;
                vd.sid = vertex.SubId;
                vd.pid = vertex.PartsId;
                vd.SetPosition(vertex.Position);
                vd.SetUVs(vertex.UVs);
                vd.SetNormals(vertex.Normals);
                vd.SetBoneWeight(vertex.BoneWeight);
                vd.SetMirrorBoneWeight(vertex.MirrorBoneWeight);
                vd.SetControlPoints(vertex.ControlPoints);
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
                skinKind   = geo.skinKind,
                vertices   = geo.vertices ?? new System.Collections.Generic.List<VertexDTO>(),
                faces      = geo.faces    ?? new System.Collections.Generic.List<FaceDTO>(),
            };
            return ToMeshObject(tmp);
        }
    }
}
