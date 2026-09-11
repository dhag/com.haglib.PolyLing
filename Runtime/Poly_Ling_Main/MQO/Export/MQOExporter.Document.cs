// MQOExporter.Document.cs
// MQO エクスポート：ドキュメント変換（ワールド行列・ボーン深さと並べ替え・ボーンオブジェクト）。
// Runtime/Poly_Ling_Main/MQO/Export/ に配置

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;

namespace Poly_Ling.MQO
{
    public static partial class MQOExporter
    {
        // ================================================================
        // ドキュメント変換
        // ================================================================

        /// <summary>
        /// 頂点をワールド座標で出す前に、階層のワールド行列を最新化する。
        /// モデルに属していないコンテキスト（部分エクスポート用の一時リスト等）では
        /// 何もしない。その場合 WorldMatrix は単位のままなので従来どおりの出力になる。
        /// </summary>
        private static void EnsureWorldMatrices(IList<MeshContext> meshContexts)
        {
            if (meshContexts == null) return;

            Poly_Ling.Context.ModelContext model = null;
            foreach (var mc in meshContexts)
            {
                if (mc?.ParentModelContext == null) continue;
                model = mc.ParentModelContext;
                break;
            }
            model?.ComputeWorldMatrices();
        }

        /// <summary>
        /// MQOドキュメント変換（Phase 5: グローバルマテリアルリスト対応）
        /// </summary>
        private static MQODocument ConvertToDocument(
            IList<MeshContext> meshContexts,
            IList<Material> materials,
            MQOExportSettings settings,
            MQOExportStats stats,
            IList<Poly_Ling.Materials.MaterialReference> materialRefs = null)
        {
            EnsureWorldMatrices(meshContexts);

            var document = new MQODocument
            {
                Version = 1.1m,
            };

            // デフォルトシーン情報
            document.Scene = CreateDefaultScene();

            // 使用されているマテリアルインデックスを収集
            var usedMaterialIndices = new HashSet<int>();
            foreach (var mc in meshContexts)
            {
                if (mc?.MeshObject == null) continue;
                foreach (var face in mc.MeshObject.Faces)
                {
                    if (face.MaterialIndex >= 0)
                    {
                        usedMaterialIndices.Add(face.MaterialIndex);
                    }
                }
            }

            // マテリアル設定（グローバルマテリアルリストから）
            // oldIndex -> newIndex のマッピング
            var materialIndexMap = new Dictionary<int, int>();
            
            if (settings.ExportMaterials && materials != null && materials.Count > 0)
            {
                for (int i = 0; i < materials.Count; i++)
                {
                    var mat = materials[i];
                    var matRef = (materialRefs != null && i < materialRefs.Count) ? materialRefs[i] : null;
                    
                    // 未使用ミラーマテリアル除外チェック
                    if (settings.ExcludeUnusedMirrorMaterials)
                    {
                        string matName = mat?.name ?? matRef?.Data?.Name ?? "";
                        bool isMirrorMaterial = matName.EndsWith("+");
                        bool isUsed = usedMaterialIndices.Contains(i);
                        
                        if (isMirrorMaterial && !isUsed)
                        {
                            // 未使用のミラーマテリアルはスキップ
                            continue;
                        }
                    }
                    
                    // マッピングを記録
                    materialIndexMap[i] = document.Materials.Count;
                    
                    if (mat != null)
                    {
                        document.Materials.Add(ConvertMaterial(mat, settings.TextureFolder, matRef));
                    }
                    else
                    {
                        // nullマテリアルの場合はデフォルトを追加
                        document.Materials.Add(new MQOMaterial
                        {
                            Name = $"Material{i}",
                            Color = Color.white,
                            Diffuse = 0.8f,
                            Ambient = 0.6f,
                        });
                    }
                }
            }

            // デフォルトマテリアルがない場合は追加
            if (document.Materials.Count == 0)
            {
                document.Materials.Add(new MQOMaterial
                {
                    Name = "Default",
                    Color = Color.white,
                    Diffuse = 0.8f,
                    Ambient = 0.6f,
                });
            }

            // オブジェクト変換（Phase 5: マテリアルインデックスマップを渡す）
            int materialCount = document.Materials.Count;
            
            if (settings.MergeObjects)
            {
                // 全メッシュを統合
                var merged = MergeMeshContexts(meshContexts, "MergedObject");
                var mqoObj = ConvertObject(merged, materialCount, settings, stats, materialIndexMap);
                if (mqoObj != null)
                {
                    document.Objects.Add(mqoObj);
                }
            }
            else
            {
                // ボーンとメッシュを分離
                var boneContexts = new List<MeshContext>();
                var meshOnlyContexts = new List<MeshContext>();
                
                foreach (var mc in meshContexts)
                {
                    if (mc == null) continue;
                    
                    if (mc.Type == MeshType.Bone)
                    {
                        boneContexts.Add(mc);
                    }
                    else
                    {
                        meshOnlyContexts.Add(mc);
                    }
                }
                
                // ボーン出力（ExportBones=trueの場合）
                if (settings.ExportBones && boneContexts.Count > 0)
                {
                    // __Armature__オブジェクトを作成（ツリー構造用）
                    var armatureObj = new MQOObject
                    {
                        Name = "__Armature__"
                    };
                    armatureObj.Attributes.Add(new MQOAttribute("depth", 0));
                    armatureObj.Attributes.Add(new MQOAttribute("visible", 15));
                    armatureObj.Attributes.Add(new MQOAttribute("locking", 0));
                    armatureObj.Attributes.Add(new MQOAttribute("shading", 1));
                    armatureObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
                    armatureObj.Attributes.Add(new MQOAttribute("color", 1, 1, 1));
                    armatureObj.Attributes.Add(new MQOAttribute("color_type", 0));
                    document.Objects.Add(armatureObj);
                    
                    // ボーンをツリー順（深さ優先）でソート
                    var sortedBones = SortBonesDepthFirst(boneContexts, meshContexts);
                    var boneDepths = CalculateBoneDepths(boneContexts, meshContexts);
                    
                    // ボーンを出力（ツリー順）
                    foreach (var bc in sortedBones)
                    {
                        var mqoObj = ConvertBoneObject(bc, boneDepths, settings, stats);
                        if (mqoObj != null)
                        {
                            document.Objects.Add(mqoObj);
                        }
                    }
                    
                    // __ArmatureName__オブジェクトを作成（リスト順インデックス用）
                    var armatureNameObj = new MQOObject
                    {
                        Name = "__ArmatureName__"
                    };
                    armatureNameObj.Attributes.Add(new MQOAttribute("depth", 0));
                    armatureNameObj.Attributes.Add(new MQOAttribute("visible", 0));  // 非表示
                    armatureNameObj.Attributes.Add(new MQOAttribute("locking", 1));  // ロック
                    armatureNameObj.Attributes.Add(new MQOAttribute("shading", 1));
                    armatureNameObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
                    armatureNameObj.Attributes.Add(new MQOAttribute("color", 0.5f, 0.5f, 0.5f));
                    armatureNameObj.Attributes.Add(new MQOAttribute("color_type", 0));
                    document.Objects.Add(armatureNameObj);
                    
                    // ボーン名をリスト順（元のインデックス順）で出力
                    foreach (var bc in boneContexts)
                    {
                        var nameObj = new MQOObject
                        {
                            Name = "__ArmatureName__" + (bc.Name ?? "Bone")
                        };
                        nameObj.Attributes.Add(new MQOAttribute("depth", 1));
                        nameObj.Attributes.Add(new MQOAttribute("visible", 0));
                        nameObj.Attributes.Add(new MQOAttribute("locking", 1));
                        nameObj.Attributes.Add(new MQOAttribute("shading", 1));
                        nameObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
                        nameObj.Attributes.Add(new MQOAttribute("color", 0.5f, 0.5f, 0.5f));
                        nameObj.Attributes.Add(new MQOAttribute("color_type", 0));
                        document.Objects.Add(nameObj);
                    }
                }
                    
                    // __IK__セクション: IKボーン情報を出力
                    EmitIKObjects(document, boneContexts, meshContexts);
                
                // メッシュを出力（2パス: 実体を先に出力し、ミラーは後でウェイト保存）
                var skippedMirrors = new List<MeshContext>();
                var mcToMqoObj = new Dictionary<MeshContext, MQOObject>();

                foreach (var mc in meshOnlyContexts)
                {
                    // ミラースキップ判定（B: Type=BakedMirror, C: 名前末尾+）
                    if (ShouldSkipAsMirror(mc, settings))
                    {
                        skippedMirrors.Add(mc);
                        continue;
                    }

                    // 空オブジェクトスキップ（SkipEmptyObjectsがtrueかつMeshObjectがnullまたは空の場合）
                    if (settings.SkipEmptyObjects)
                    {
                        if (mc.MeshObject == null ||
                            (mc.MeshObject.VertexCount == 0 && mc.MeshObject.FaceCount == 0))
                        {
                            continue;
                        }
                    }

                    var mqoObj = ConvertObject(mc, materialCount, settings, stats, materialIndexMap);
                    if (mqoObj != null)
                    {
                        document.Objects.Add(mqoObj);
                        mcToMqoObj[mc] = mqoObj;
                    }
                }

                // スキップしたミラーメッシュのウェイトを実体側に保存
                foreach (var mirrorMc in skippedMirrors)
                {
                    var sourceMc = FindMirrorSource(mirrorMc, meshContexts);
                    if (sourceMc != null && mcToMqoObj.TryGetValue(sourceMc, out var sourceMqoObj))
                    {
                        SaveMirrorWeightsToSource(mirrorMc, sourceMqoObj, settings);
                    }
                    else
                    {
                        Debug.LogWarning($"[MQOExporter] Mirror source not found for '{mirrorMc.Name}', weights will be lost");
                    }
                }
            }

            return document;
        }

        /// <summary>
        /// ボーンのデプスを計算（HierarchyParentIndexに基づく）
        /// __Armature__の下なのでルートボーンはdepth=1
        /// </summary>
        private static Dictionary<MeshContext, int> CalculateBoneDepths(
            List<MeshContext> boneContexts,
            IList<MeshContext> allContexts)
        {
            var depths = new Dictionary<MeshContext, int>();
            var contextToIndex = new Dictionary<MeshContext, int>();
            
            // インデックスマップを作成
            for (int i = 0; i < allContexts.Count; i++)
            {
                contextToIndex[allContexts[i]] = i;
            }
            
            // 各ボーンのデプスを再帰的に計算
            foreach (var bc in boneContexts)
            {
                depths[bc] = CalculateSingleBoneDepth(bc, allContexts, contextToIndex, depths);
            }
            
            return depths;
        }

        private static int CalculateSingleBoneDepth(
            MeshContext bone,
            IList<MeshContext> allContexts,
            Dictionary<MeshContext, int> contextToIndex,
            Dictionary<MeshContext, int> cachedDepths)
        {
            // キャッシュがあれば返す
            if (cachedDepths.TryGetValue(bone, out int cached))
            {
                return cached;
            }
            
            int parentIndex = bone.HierarchyParentIndex;
            
            // ルートボーン（親がいない）→ depth=1（__Armature__の下）
            if (parentIndex < 0 || parentIndex >= allContexts.Count)
            {
                return 1;
            }
            
            var parent = allContexts[parentIndex];
            
            // 親がボーンでない場合もルートとして扱う
            if (parent.Type != MeshType.Bone)
            {
                return 1;
            }
            
            // 親のデプス + 1
            int parentDepth = CalculateSingleBoneDepth(parent, allContexts, contextToIndex, cachedDepths);
            return parentDepth + 1;
        }

        /// <summary>
        /// ボーンをツリー順（深さ優先）でソート
        /// MQOのdepth属性は出現順に基づくため、親→子の順で出力する必要がある
        /// </summary>
        private static List<MeshContext> SortBonesDepthFirst(
            List<MeshContext> boneContexts,
            IList<MeshContext> allContexts)
        {
            var result = new List<MeshContext>();
            var boneSet = new HashSet<MeshContext>(boneContexts);
            var visited = new HashSet<MeshContext>();
            
            // ボーンのインデックスマップを作成
            var boneToIndex = new Dictionary<MeshContext, int>();
            for (int i = 0; i < allContexts.Count; i++)
            {
                if (allContexts[i].Type == MeshType.Bone)
                {
                    boneToIndex[allContexts[i]] = i;
                }
            }
            
            // 親→子のマップを構築
            var childrenMap = new Dictionary<int, List<MeshContext>>();  // parentIndex → children
            var rootBones = new List<MeshContext>();
            
            foreach (var bone in boneContexts)
            {
                int parentIndex = bone.HierarchyParentIndex;
                
                // 親がボーンかどうかを確認
                bool parentIsBone = parentIndex >= 0 && 
                                    parentIndex < allContexts.Count && 
                                    allContexts[parentIndex].Type == MeshType.Bone;
                
                if (!parentIsBone)
                {
                    // ルートボーン
                    rootBones.Add(bone);
                }
                else
                {
                    // 子ボーン
                    if (!childrenMap.ContainsKey(parentIndex))
                    {
                        childrenMap[parentIndex] = new List<MeshContext>();
                    }
                    childrenMap[parentIndex].Add(bone);
                }
            }
            
            // 深さ優先でトラバース
            void TraverseDepthFirst(MeshContext bone)
            {
                if (visited.Contains(bone))
                    return;
                
                visited.Add(bone);
                result.Add(bone);
                
                // このボーンのインデックスを取得
                if (boneToIndex.TryGetValue(bone, out int boneIndex))
                {
                    // 子ボーンを処理
                    if (childrenMap.TryGetValue(boneIndex, out var children))
                    {
                        foreach (var child in children)
                        {
                            TraverseDepthFirst(child);
                        }
                    }
                }
            }
            
            // ルートボーンから開始
            foreach (var root in rootBones)
            {
                TraverseDepthFirst(root);
            }
            
            // 訪問されなかったボーン（孤立ボーン）を追加
            foreach (var bone in boneContexts)
            {
                if (!visited.Contains(bone))
                {
                    result.Add(bone);
                }
            }
            
            return result;
        }

        /// <summary>
        /// ボーン用のMQOObject変換
        /// </summary>
        private static MQOObject ConvertBoneObject(
            MeshContext boneContext,
            Dictionary<MeshContext, int> boneDepths,
            MQOExportSettings settings,
            MQOExportStats stats)
        {
            var mqoObj = new MQOObject
            {
                Name = boneContext.Name ?? "Bone"
            };
            
            // デプス設定
            int depth = boneDepths.TryGetValue(boneContext, out int d) ? d : 1;
            mqoObj.Attributes.Add(new MQOAttribute("depth", depth));
            
            // 基本属性
            mqoObj.Attributes.Add(new MQOAttribute("visible", boneContext.IsVisible ? 15 : 0));
            mqoObj.Attributes.Add(new MQOAttribute("locking", boneContext.IsLocked ? 1 : 0));
            mqoObj.Attributes.Add(new MQOAttribute("shading", 1));
            mqoObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
            mqoObj.Attributes.Add(new MQOAttribute("color", 1, 1, 1));
            mqoObj.Attributes.Add(new MQOAttribute("color_type", 0));
            
            // ローカルトランスフォーム出力
            if (settings.ExportLocalTransform && boneContext.BoneTransform != null)
            {
                var bt = boneContext.BoneTransform;
                if (bt.UseLocalTransform)
                {
                    // 位置（スケールと軸反転を適用）
                    Vector3 pos = AxisFlipOps.Position(settings.Flip, bt.Position, settings.Scale);
                    mqoObj.Attributes.Add(new MQOAttribute("translation", pos.x, pos.y, pos.z));
                    
                    // 回転。MQO の rotation は XYZ ではなく HPB なので並べ替える
                    Vector3 rot = MQOLocalRotationOps.ToMqoRotation(bt.Rotation, settings.Flip);
                    mqoObj.Attributes.Add(new MQOAttribute("rotation", rot.x, rot.y, rot.z));
                    
                    // スケール
                    Vector3 scale = bt.Scale;
                    mqoObj.Attributes.Add(new MQOAttribute("scale", scale.x, scale.y, scale.z));
                }
            }
            
            return mqoObj;
        }
    }
}
