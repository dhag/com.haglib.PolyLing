// ModelSerializer_Data.cs
// マテリアル参照・選択セット・モーフ・ミラーペア・データストア・オブジェクトグループ・BonePose の変換。
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
    }
}
