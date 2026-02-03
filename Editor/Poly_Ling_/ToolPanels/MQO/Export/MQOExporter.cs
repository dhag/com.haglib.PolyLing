// Assets/Editor/Poly_Ling/MQO/Export/MQOExporter.cs
// MQOエクスポーター
// Phase 5: ModelContext対応

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQOエクスポーター
    /// </summary>
    public static class MQOExporter
    {
        // ================================================================
        // パブリックAPI
        // ================================================================

        /// <summary>
        /// ModelContextをMQOファイルに出力（推奨）
        /// Phase 5: ModelContext.Materialsを使用
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            ModelContext model,
            MQOExportSettings settings = null)
        {
            if (model == null)
            {
                return new MQOExportResult
                {
                    Success = false,
                    ErrorMessage = "Model is null"
                };
            }

            return ExportFile(filePath, model.MeshContextList, model.Materials, settings, model.MaterialReferences);
        }

        /// <summary>
        /// MeshContextリストをMQOファイルに出力（マテリアルリスト指定）
        /// Phase 5: グローバルマテリアルリストを明示的に指定
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            IList<MeshContext> meshContexts,
            IList<Material> materials,
            MQOExportSettings settings = null,
            IList<Poly_Ling.Materials.MaterialReference> materialRefs = null)
        {
            var result = new MQOExportResult();

            if (meshContexts == null || meshContexts.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No mesh contexts to export";
                return result;
            }

            settings = settings ?? new MQOExportSettings();

            try
            {
                // MQOドキュメント作成（マテリアルリストとMaterialReferencesを渡す）
                var document = ConvertToDocument(meshContexts, materials, settings, result.Stats, materialRefs);

                // テキスト生成
                string mqoText = GenerateMQOText(document, settings);

                // ファイル出力
                Encoding encoding = settings.UseShiftJIS
                    ? Encoding.GetEncoding("shift_jis")
                    : Encoding.UTF8;

                File.WriteAllText(filePath, mqoText, encoding);

                result.Success = true;
                result.FilePath = filePath;
                result.Stats.ObjectCount = document.Objects.Count;
                result.Stats.MaterialCount = document.Materials.Count;

                Debug.Log($"[MQOExporter] Export successful: {filePath}");
                Debug.Log($"  - Objects: {result.Stats.ObjectCount}");
                Debug.Log($"  - Vertices: {result.Stats.TotalVertices}");
                Debug.Log($"  - Faces: {result.Stats.TotalFaces}");
                Debug.Log($"  - Materials: {result.Stats.MaterialCount}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[MQOExporter] Export failed: {ex}");
            }

            return result;
        }

        /// <summary>
        /// MeshContextリストをMQOファイルに出力（後方互換）
        /// 注意: MeshContext.Materialsを使用（Modelが設定されていればModelContext.Materialsに委譲）
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            IList<MeshContext> meshContexts,
            MQOExportSettings settings = null)
        {
            var result = new MQOExportResult();

            if (meshContexts == null || meshContexts.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No mesh contexts to export";
                return result;
            }

            settings = settings ?? new MQOExportSettings();

            try
            {
                // MQOドキュメント作成（後方互換モード）
                var document = ConvertToDocumentLegacy(meshContexts, settings, result.Stats);

                // テキスト生成
                string mqoText = GenerateMQOText(document, settings);

                // ファイル出力
                Encoding encoding = settings.UseShiftJIS
                    ? Encoding.GetEncoding("shift_jis")
                    : Encoding.UTF8;

                File.WriteAllText(filePath, mqoText, encoding);

                result.Success = true;
                result.FilePath = filePath;
                result.Stats.ObjectCount = document.Objects.Count;
                result.Stats.MaterialCount = document.Materials.Count;

                Debug.Log($"[MQOExporter] Export successful: {filePath}");
                Debug.Log($"  - Objects: {result.Stats.ObjectCount}");
                Debug.Log($"  - Vertices: {result.Stats.TotalVertices}");
                Debug.Log($"  - Faces: {result.Stats.TotalFaces}");
                Debug.Log($"  - Materials: {result.Stats.MaterialCount}");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[MQOExporter] Export failed: {ex}");
            }

            return result;
        }

        /// <summary>
        /// 単一のMeshContextをMQOファイルに出力
        /// </summary>
        public static MQOExportResult ExportFile(
            string filePath,
            MeshContext meshContext,
            MQOExportSettings settings = null)
        {
            return ExportFile(filePath, new[] { meshContext }, settings);
        }

        // ================================================================
        // WriteBack API
        // ================================================================

        /// <summary>
        /// 編集後のMeshContextからMQOObjectへ頂点属性を書き戻す
        /// 面の構造（頂点インデックス、マテリアル）は変更しない
        /// </summary>
        /// <param name="sourceMeshContext">編集後のモデル側MeshContext（展開済み）</param>
        /// <param name="targetMqoObject">更新対象のMQOObject</param>
        /// <param name="mqoMeshContext">MQOインポート結果のMeshContext（UV数情報用）</param>
        /// <param name="settings">エクスポート設定</param>
        /// <param name="flags">何を更新するかのフラグ</param>
        /// <returns>更新した頂点数</returns>
        public static int WriteBack(
            MeshContext sourceMeshContext,
            MQOObject targetMqoObject,
            MeshContext mqoMeshContext,
            MQOExportSettings settings,
            WriteBackFlags flags)
        {
            if (sourceMeshContext?.MeshObject == null) return 0;
            if (targetMqoObject == null) return 0;
            if (mqoMeshContext?.MeshObject == null) return 0;

            settings = settings ?? new MQOExportSettings();
            var srcMo = sourceMeshContext.MeshObject;
            var mqoMo = mqoMeshContext.MeshObject;

            int transferred = 0;
            int srcOffset = 0;

            // MQO側MeshContextの頂点を走査（UV展開対応）
            for (int vIdx = 0; vIdx < mqoMo.VertexCount && vIdx < targetMqoObject.Vertices.Count; vIdx++)
            {
                var mqoVertex = mqoMo.Vertices[vIdx];
                int uvCount = mqoVertex.UVs.Count > 0 ? mqoVertex.UVs.Count : 1;

                // Position更新
                if ((flags & WriteBackFlags.Position) != 0 && srcOffset < srcMo.VertexCount)
                {
                    Vector3 pos = srcMo.Vertices[srcOffset].Position;

                    // 座標変換: Model → MQO
                    if (settings.FlipZ) pos.z = -pos.z;
                    pos *= settings.Scale;

                    targetMqoObject.Vertices[vIdx].Position = pos;
                    mqoVertex.Position = pos;
                    transferred++;
                }

                srcOffset += uvCount;
            }

            // UV更新（面のUVs配列を更新）
            if ((flags & WriteBackFlags.UV) != 0)
            {
                WriteBackUVs(sourceMeshContext, targetMqoObject, mqoMeshContext, settings);
            }

            // BoneWeight更新（特殊面を削除して再追加）
            if ((flags & WriteBackFlags.BoneWeight) != 0)
            {
                WriteBackBoneWeights(sourceMeshContext, targetMqoObject, mqoMeshContext, settings);
            }

            return transferred;
        }

        /// <summary>
        /// UVを面のUVs配列に書き戻す
        /// 既存のConvertObject（1180-1192行）と同じロジックを使用
        /// </summary>
        private static void WriteBackUVs(
            MeshContext sourceMeshContext,
            MQOObject targetMqoObject,
            MeshContext mqoMeshContext,
            MQOExportSettings settings)
        {
            var srcMo = sourceMeshContext.MeshObject;
            var mqoMo = mqoMeshContext.MeshObject;

            // MQO側の頂点インデックス→展開後インデックス開始位置のマッピングを構築
            var vertexToExpandedStart = new Dictionary<int, int>();
            int expandedIdx = 0;
            for (int vIdx = 0; vIdx < mqoMo.VertexCount; vIdx++)
            {
                vertexToExpandedStart[vIdx] = expandedIdx;
                int uvCount = mqoMo.Vertices[vIdx].UVs.Count > 0 ? mqoMo.Vertices[vIdx].UVs.Count : 1;
                expandedIdx += uvCount;
            }

            // MQOObject側の面とMeshContext側の面を並行して走査
            int mqoFaceIdx = 0;
            foreach (var targetFace in targetMqoObject.Faces)
            {
                if (targetFace.IsSpecialFace) continue;
                if (targetFace.VertexIndices == null) continue;

                // 対応するMeshContext側の面を取得
                if (mqoFaceIdx >= mqoMo.FaceCount) break;
                var meshFace = mqoMo.Faces[mqoFaceIdx];
                mqoFaceIdx++;

                // 面のUVs配列を確保
                if (targetFace.UVs == null || targetFace.UVs.Length != targetFace.VertexIndices.Length)
                {
                    targetFace.UVs = new Vector2[targetFace.VertexIndices.Length];
                }

                for (int i = 0; i < targetFace.VertexIndices.Length && i < meshFace.VertexIndices.Count; i++)
                {
                    int vIdx = targetFace.VertexIndices[i];
                    if (!vertexToExpandedStart.TryGetValue(vIdx, out int expStart)) continue;

                    // UVIndicesからUVスロット番号を取得（既存のConvertObjectと同じロジック）
                    int uvSlot = (i < meshFace.UVIndices.Count) ? meshFace.UVIndices[i] : 0;

                    // 展開後インデックス = 頂点の展開開始位置 + UVスロット番号
                    int srcIdx = expStart + uvSlot;
                    if (srcIdx < srcMo.VertexCount)
                    {
                        var srcVertex = srcMo.Vertices[srcIdx];
                        Vector2 uv = srcVertex.UVs.Count > 0 ? srcVertex.UVs[0] : Vector2.zero;

                        // UV V座標反転
                        if (settings.FlipUV_V)
                        {
                            uv.y = 1f - uv.y;
                        }

                        targetFace.UVs[i] = uv;
                    }
                }
            }
        }

        /// <summary>
        /// ボーンウェイトを特殊面として書き戻す（既存削除→新規追加）
        /// </summary>
        private static void WriteBackBoneWeights(
            MeshContext sourceMeshContext,
            MQOObject targetMqoObject,
            MeshContext mqoMeshContext,
            MQOExportSettings settings)
        {
            var srcMo = sourceMeshContext.MeshObject;
            var mqoMo = mqoMeshContext.MeshObject;

            // 既存の特殊面を削除（頂点ID特殊面とボーンウェイト特殊面）
            targetMqoObject.Faces.RemoveAll(f => f.IsSpecialFace);

            // 頂点ID特殊面を再追加
            int srcOffset = 0;
            for (int vIdx = 0; vIdx < mqoMo.VertexCount && vIdx < targetMqoObject.Vertices.Count; vIdx++)
            {
                var mqoVertex = mqoMo.Vertices[vIdx];
                int uvCount = mqoVertex.UVs.Count > 0 ? mqoVertex.UVs.Count : 1;

                if (srcOffset < srcMo.VertexCount)
                {
                    var srcVertex = srcMo.Vertices[srcOffset];

                    // 頂点ID特殊面
                    if (srcVertex.Id != -1)
                    {
                        targetMqoObject.Faces.Add(
                            VertexIdHelper.CreateSpecialFaceForVertexId(vIdx, srcVertex.Id, 0));
                    }

                    // ボーンウェイト特殊面
                    if (srcVertex.HasBoneWeight)
                    {
                        var boneWeightData = VertexIdHelper.BoneWeightData.FromUnityBoneWeight(srcVertex.BoneWeight.Value);
                        targetMqoObject.Faces.Add(
                            VertexIdHelper.CreateSpecialFaceForBoneWeight(vIdx, boneWeightData, false, 0));
                    }
                }

                srcOffset += uvCount;
            }
        }

        /// <summary>
        /// WriteBack可能か検証
        /// </summary>
        /// <param name="sourceMeshContext">ソースMeshContext</param>
        /// <param name="mqoMeshContext">MQO側MeshContext</param>
        /// <returns>エラーメッセージ（nullなら問題なし）</returns>
        public static string ValidateWriteBack(MeshContext sourceMeshContext, MeshContext mqoMeshContext)
        {
            if (sourceMeshContext?.MeshObject == null)
                return "Source MeshContext is null";
            if (mqoMeshContext?.MeshObject == null)
                return "MQO MeshContext is null";

            // 展開後頂点数の計算
            var mqoMo = mqoMeshContext.MeshObject;
            int expectedExpandedCount = 0;
            for (int i = 0; i < mqoMo.VertexCount; i++)
            {
                int uvCount = mqoMo.Vertices[i].UVs.Count > 0 ? mqoMo.Vertices[i].UVs.Count : 1;
                expectedExpandedCount += uvCount;
            }

            int srcCount = sourceMeshContext.MeshObject.VertexCount;
            if (srcCount != expectedExpandedCount)
            {
                return $"Vertex count mismatch: source={srcCount}, expected={expectedExpandedCount}";
            }

            return null;
        }

        // ================================================================
        // ドキュメント変換
        // ================================================================

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
                
                // メッシュを出力
                foreach (var mc in meshOnlyContexts)
                {
                    // ベイクミラースキップ（SkipBakedMirrorがtrueかつType=BakedMirrorの場合）
                    if (settings.SkipBakedMirror && mc.Type == MeshType.BakedMirror)
                    {
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
                    // 位置（スケールを適用）
                    Vector3 pos = bt.Position * settings.Scale;
                    if (settings.FlipZ)
                    {
                        pos.z = -pos.z;
                    }
                    mqoObj.Attributes.Add(new MQOAttribute("translation", pos.x, pos.y, pos.z));
                    
                    // 回転（度数法）
                    Vector3 rot = bt.Rotation;
                    mqoObj.Attributes.Add(new MQOAttribute("rotation", rot.x, rot.y, rot.z));
                    
                    // スケール
                    Vector3 scale = bt.Scale;
                    mqoObj.Attributes.Add(new MQOAttribute("scale", scale.x, scale.y, scale.z));
                }
            }
            
            return mqoObj;
        }

        /// <summary>
        /// MQOドキュメント変換（後方互換: MeshContext.Materialsを使用）
        /// </summary>
        private static MQODocument ConvertToDocumentLegacy(
            IList<MeshContext> meshContexts,
            MQOExportSettings settings,
            MQOExportStats stats)
        {
            var document = new MQODocument
            {
                Version = 1.1m,
            };

            // デフォルトシーン情報
            document.Scene = CreateDefaultScene();

            // マテリアル収集（MeshContext.Materialsから）
            var materialMap = new Dictionary<Material, int>();
            if (settings.ExportMaterials)
            {
                foreach (var mc in meshContexts)
                {
                    if (mc?.Materials == null) continue;
                    foreach (var mat in mc.Materials)
                    {
                        if (mat != null && !materialMap.ContainsKey(mat))
                        {
                            materialMap[mat] = document.Materials.Count;
                            document.Materials.Add(ConvertMaterial(mat, settings.TextureFolder));
                        }
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

            // オブジェクト変換（Legacy版もマテリアル数を渡す）
            int materialCount = document.Materials.Count;
            
            if (settings.MergeObjects)
            {
                // 全メッシュを統合
                var merged = MergeMeshContexts(meshContexts, "MergedObject");
                var mqoObj = ConvertObject(merged, materialCount, settings, stats);
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
                
                // メッシュを出力
                foreach (var mc in meshOnlyContexts)
                {
                    // ベイクミラースキップ（SkipBakedMirrorがtrueかつType=BakedMirrorの場合）
                    if (settings.SkipBakedMirror && mc.Type == MeshType.BakedMirror)
                    {
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

                    var mqoObj = ConvertObject(mc, materialCount, settings, stats);
                    if (mqoObj != null)
                    {
                        document.Objects.Add(mqoObj);
                    }
                }
            }

            return document;
        }

        private static MQOScene CreateDefaultScene()
        {
            var scene = new MQOScene();
            scene.Attributes.Add(new MQOAttribute("pos", 0, 0, 1500));
            scene.Attributes.Add(new MQOAttribute("lookat", 0, 0, 0));
            scene.Attributes.Add(new MQOAttribute("head", -0.5236f));
            scene.Attributes.Add(new MQOAttribute("pich", 0.5236f));
            scene.Attributes.Add(new MQOAttribute("ortho", 0));
            scene.Attributes.Add(new MQOAttribute("zoom2", 5));
            scene.Attributes.Add(new MQOAttribute("amb", 0.25f, 0.25f, 0.25f));
            return scene;
        }

        private static MQOMaterial ConvertMaterial(Material mat, string textureFolder, Poly_Ling.Materials.MaterialReference matRef = null)
        {
            var mqoMat = new MQOMaterial
            {
                Name = mat.name,
                Color = mat.HasProperty("_Color") ? mat.color : Color.white,
                Diffuse = 0.8f,
                Ambient = 0.6f,
                Specular = mat.HasProperty("_SpecColor") ? mat.GetColor("_SpecColor").grayscale : 0f,
                Power = mat.HasProperty("_Shininess") ? mat.GetFloat("_Shininess") : 5f,
            };

            // ソーステクスチャパスを優先的に使用（インポート時のパスを保持）
            if (matRef?.Data != null && !string.IsNullOrEmpty(matRef.Data.SourceTexturePath))
            {
                mqoMat.TexturePath = matRef.Data.SourceTexturePath;
            }
            // フォールバック: 現在のテクスチャから生成
            else if (mat.HasProperty("_MainTex") && mat.mainTexture != null)
            {
                string texName = mat.mainTexture.name;
                // 拡張子がなければ.pngを追加
                if (!texName.Contains("."))
                {
                    texName += ".png";
                }
                // フォルダパスを付加
                if (!string.IsNullOrEmpty(textureFolder))
                {
                    // フォルダパスの末尾にスラッシュがなければ追加
                    string folder = textureFolder;
                    if (!folder.EndsWith("/") && !folder.EndsWith("\\"))
                    {
                        folder += "/";
                    }
                    texName = folder + texName;
                }
                mqoMat.TexturePath = texName;
            }

            // バンプマップのソースパス
            if (matRef?.Data != null && !string.IsNullOrEmpty(matRef.Data.SourceBumpMapPath))
            {
                mqoMat.BumpMapPath = matRef.Data.SourceBumpMapPath;
            }

            // アルファマップのソースパス
            if (matRef?.Data != null && !string.IsNullOrEmpty(matRef.Data.SourceAlphaMapPath))
            {
                mqoMat.AlphaMapPath = matRef.Data.SourceAlphaMapPath;
            }

            return mqoMat;
        }

        /// <summary>
        /// MeshContextをMQOObjectに変換
        /// Phase 5: materialMapの代わりにmaterialCountを使用
        /// </summary>
        private static MQOObject ConvertObject(
            MeshContext meshContext,
            int materialCount,
            MQOExportSettings settings,
            MQOExportStats stats,
            Dictionary<int, int> materialIndexMap = null)
        {
            var meshObject = meshContext.MeshObject;
            
            // 空オブジェクトでも属性は出力する（階層構造保持のため）
            var mqoObj = new MQOObject
            {
                Name = meshContext.Name ?? "Object",
            };

            // 属性設定（オブジェクト属性を保持する場合）
            if (settings.PreserveObjectAttributes)
            {
                // depth（階層深度）
                if (meshContext.Depth > 0)
                {
                    mqoObj.Attributes.Add(new MQOAttribute("depth", meshContext.Depth));
                }
                
                // folding（折りたたみ状態）
                if (meshContext.IsFolding)
                {
                    mqoObj.Attributes.Add(new MQOAttribute("folding", 1));
                }
                
                // visible（表示状態）
                // MQOのvisible: 15=表示, 0=非表示
                int visibleValue = meshContext.IsVisible ? 15 : 0;
                mqoObj.Attributes.Add(new MQOAttribute("visible", visibleValue));
                
                // locking（ロック状態）
                int lockingValue = meshContext.IsLocked ? 1 : 0;
                mqoObj.Attributes.Add(new MQOAttribute("locking", lockingValue));
                
                // mirror（ミラー設定）
                if (meshContext.MirrorType > 0)
                {
                    mqoObj.Attributes.Add(new MQOAttribute("mirror", meshContext.MirrorType));
                    mqoObj.Attributes.Add(new MQOAttribute("mirror_axis", meshContext.MirrorAxis));
                    if (meshContext.MirrorDistance != 0f)
                    {
                        mqoObj.Attributes.Add(new MQOAttribute("mirror_dis", meshContext.MirrorDistance));
                    }
                }
            }
            else
            {
                // デフォルト属性
                mqoObj.Attributes.Add(new MQOAttribute("visible", 15));
                mqoObj.Attributes.Add(new MQOAttribute("locking", 0));
            }
            
            // 共通属性
            mqoObj.Attributes.Add(new MQOAttribute("shading", 1));
            mqoObj.Attributes.Add(new MQOAttribute("facet", 59.5f));
            mqoObj.Attributes.Add(new MQOAttribute("color", 1, 1, 1));
            mqoObj.Attributes.Add(new MQOAttribute("color_type", 0));

            // ローカルトランスフォーム出力
            if (settings.ExportLocalTransform && meshContext.BoneTransform != null)
            {
                var bt = meshContext.BoneTransform;
                if (bt.UseLocalTransform)
                {
                    // 位置（スケールを適用）
                    Vector3 pos = bt.Position * settings.Scale;
                    if (settings.FlipZ)
                    {
                        pos.z = -pos.z;
                    }
                    mqoObj.Attributes.Add(new MQOAttribute("translation", pos.x, pos.y, pos.z));
                    
                    // 回転（度数法）
                    Vector3 rot = bt.Rotation;
                    mqoObj.Attributes.Add(new MQOAttribute("rotation", rot.x, rot.y, rot.z));
                    
                    // スケール
                    Vector3 scale = bt.Scale;
                    mqoObj.Attributes.Add(new MQOAttribute("scale", scale.x, scale.y, scale.z));
                }
            }

            // MeshObjectがある場合のみ頂点・面を出力
            if (meshObject != null)
            {
                // 頂点変換
                foreach (var v in meshObject.Vertices)
                {
                    var mqoVert = new MQOVertex
                    {
                        Position = ConvertPosition(v.Position, settings),
                        Index = mqoObj.Vertices.Count,
                    };
                    mqoObj.Vertices.Add(mqoVert);
                    stats.TotalVertices++;
                }

                // 面変換
                foreach (var face in meshObject.Faces)
                {
                    if (face.VertexIndices == null || face.VertexIndices.Count == 0)
                        continue;

                    // マテリアルインデックスを変換（マッピングがあれば使用）
                    int exportMatIdx = 0;
                    if (face.MaterialIndex >= 0)
                    {
                        if (materialIndexMap != null && materialIndexMap.TryGetValue(face.MaterialIndex, out int mappedIdx))
                        {
                            exportMatIdx = mappedIdx;
                        }
                        else if (face.MaterialIndex < materialCount)
                        {
                            exportMatIdx = face.MaterialIndex;
                        }
                    }

                    var mqoFace = new MQOFace
                    {
                        VertexIndices = face.VertexIndices.ToArray(),
                        MaterialIndex = exportMatIdx,
                    };

                    // UV変換
                    if (face.UVIndices != null && face.UVIndices.Count > 0)
                    {
                        var uvs = new Vector2[face.UVIndices.Count];
                        for (int i = 0; i < face.UVIndices.Count; i++)
                        {
                            int vertIdx = face.VertexIndices[i];
                            int uvIdx = face.UVIndices[i];
                            if (vertIdx >= 0 && vertIdx < meshObject.Vertices.Count)
                            {
                                var vertex = meshObject.Vertices[vertIdx];
                                // UVインデックスが有効範囲内か確認
                                Vector2 uv = (uvIdx >= 0 && uvIdx < vertex.UVs.Count)
                                    ? vertex.UVs[uvIdx]
                                    : (vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero);
                                uvs[i] = ConvertUV(uv, settings);
                            }
                        }
                        mqoFace.UVs = uvs;
                    }

                    mqoObj.Faces.Add(mqoFace);
                    stats.TotalFaces++;
                }

                // 頂点ID用の特殊面を追加（ID != -1 の頂点のみ）
                // VertexIdHelper.CreateSpecialFaceForVertexIdを使用
                for (int i = 0; i < meshObject.Vertices.Count; i++)
                {
                    var vertex = meshObject.Vertices[i];
                    if (vertex.Id != -1)
                    {
                        mqoObj.Faces.Add(VertexIdHelper.CreateSpecialFaceForVertexId(i, vertex.Id, 0));
                    }
                }

                // ボーンウェイト用の四角形特殊面を追加（BoneWeightを持つ頂点のみ）
                // VertexIdHelper.CreateSpecialFaceForBoneWeightを使用
                if (settings.EmbedBoneWeightsInMQO)
                {
                    for (int i = 0; i < meshObject.Vertices.Count; i++)
                    {
                        var vertex = meshObject.Vertices[i];
                        if (vertex.HasBoneWeight)
                        {
                            var boneWeightData = VertexIdHelper.BoneWeightData.FromUnityBoneWeight(vertex.BoneWeight.Value);
                            mqoObj.Faces.Add(VertexIdHelper.CreateSpecialFaceForBoneWeight(i, boneWeightData, false, 0));
                        }
                    }
                }
            }

            return mqoObj;
        }

        /// <summary>
        /// マテリアルインデックスを取得
        /// Phase 5: Face.MaterialIndexはグローバルマテリアルインデックス
        /// </summary>
        private static int GetMaterialIndex(
            MeshContext meshContext,
            int materialIndex,
            Dictionary<Material, int> materialMap)
        {
            // Phase 5以降: Face.MaterialIndexはグローバルインデックス
            // materialMapのサイズ（エクスポートされたマテリアル数）を超えなければそのまま使用
            if (materialIndex >= 0 && materialIndex < materialMap.Count)
            {
                return materialIndex;
            }

            // 範囲外の場合はデフォルト（0）
            return 0;
        }

        // ================================================================
        // 座標変換
        // ================================================================

        private static Vector3 ConvertPosition(Vector3 pos, MQOExportSettings settings)
        {
            // スケール
            pos *= settings.Scale;

            // Y-Z入れ替え（Unity Y-up → MQO Z-up）
            if (settings.SwapYZ)
            {
                pos = new Vector3(pos.x, pos.z, pos.y);
            }

            // Z反転
            if (settings.FlipZ)
            {
                pos.z = -pos.z;
            }

            return pos;
        }

        private static Vector2 ConvertUV(Vector2 uv, MQOExportSettings settings)
        {
            if (settings.FlipUV_V)
            {
                uv.y = 1f - uv.y;
            }
            return uv;
        }

        // ================================================================
        // メッシュ統合
        // ================================================================

        private static MeshContext MergeMeshContexts(
            IList<MeshContext> meshContexts,
            string name)
        {
            var mergedData = new MeshObject(name);
            var mergedMaterials = new List<Material>();

            foreach (var mc in meshContexts)
            {
                if (mc?.MeshObject == null) continue;

                int vertexOffset = mergedData.VertexCount;

                // 頂点コピー
                foreach (var v in mc.MeshObject.Vertices)
                {
                    Vector2 uv = v.UVs.Count > 0 ? v.UVs[0] : Vector2.zero;
                    Vector3 normal = v.Normals.Count > 0 ? v.Normals[0] : Vector3.zero;
                    mergedData.AddVertex(v.Position, uv, normal);
                }

                // 面コピー（インデックスオフセット）
                foreach (var face in mc.MeshObject.Faces)
                {
                    var newFace = new Face
                    {
                        MaterialIndex = face.MaterialIndex
                    };
                    foreach (int idx in face.VertexIndices)
                    {
                        newFace.VertexIndices.Add(idx + vertexOffset);
                    }
                    // UVインデックスもコピー（オフセットなし、頂点内インデックスのため）
                    newFace.UVIndices.AddRange(face.UVIndices);
                    newFace.NormalIndices.AddRange(face.NormalIndices);
                    mergedData.AddFace(newFace);
                }

                // マテリアルコピー
                if (mc.Materials != null)
                {
                    foreach (var mat in mc.Materials)
                    {
                        if (mat != null && !mergedMaterials.Contains(mat))
                        {
                            mergedMaterials.Add(mat);
                        }
                    }
                }
            }

            return new MeshContext
            {
                Name = name,
                MeshObject = mergedData,
                Materials = mergedMaterials,
            };
        }

        // ================================================================
        // テキスト生成
        // ================================================================

        private static string GenerateMQOText(MQODocument document, MQOExportSettings settings)
        {
            var sb = new StringBuilder();
            string fmt = $"F{settings.DecimalPrecision}";

            // ヘッダー
            sb.AppendLine("Metasequoia Document");
            sb.AppendLine("Format Text Ver 1.1");
            sb.AppendLine();

            // Scene
            sb.AppendLine("Scene {");
            foreach (var attr in document.Scene.Attributes)
            {
                sb.Append($"\t{attr.Name}");
                foreach (var v in attr.Values)
                {
                    sb.Append($" {v.ToString(fmt, CultureInfo.InvariantCulture)}");
                }
                sb.AppendLine();
            }
            sb.AppendLine("}");

            // Material
            if (document.Materials.Count > 0)
            {
                sb.AppendLine($"Material {document.Materials.Count} {{");
                foreach (var mat in document.Materials)
                {
                    sb.Append($"\t\"{mat.Name}\"");
                    sb.Append($" col({mat.Color.r.ToString(fmt, CultureInfo.InvariantCulture)}");
                    sb.Append($" {mat.Color.g.ToString(fmt, CultureInfo.InvariantCulture)}");
                    sb.Append($" {mat.Color.b.ToString(fmt, CultureInfo.InvariantCulture)}");
                    sb.Append($" {mat.Color.a.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" dif({mat.Diffuse.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" amb({mat.Ambient.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" emi({mat.Emissive.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" spc({mat.Specular.ToString(fmt, CultureInfo.InvariantCulture)})");
                    sb.Append($" power({mat.Power.ToString("F2", CultureInfo.InvariantCulture)})");
                    if (!string.IsNullOrEmpty(mat.TexturePath))
                    {
                        sb.Append($" tex(\"{mat.TexturePath}\")");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine("}");
            }

            // Objects
            foreach (var obj in document.Objects)
            {
                sb.AppendLine($"Object \"{obj.Name}\" {{");

                // 属性
                foreach (var attr in obj.Attributes)
                {
                    sb.Append($"\t{attr.Name}");
                    foreach (var v in attr.Values)
                    {
                        if (v == (int)v)
                            sb.Append($" {(int)v}");
                        else
                            sb.Append($" {v.ToString(fmt, CultureInfo.InvariantCulture)}");
                    }
                    sb.AppendLine();
                }

                // 頂点
                sb.AppendLine($"\tvertex {obj.Vertices.Count} {{");
                foreach (var v in obj.Vertices)
                {
                    sb.Append("\t\t");
                    sb.Append(v.Position.x.ToString(fmt, CultureInfo.InvariantCulture));
                    sb.Append(" ");
                    sb.Append(v.Position.y.ToString(fmt, CultureInfo.InvariantCulture));
                    sb.Append(" ");
                    sb.AppendLine(v.Position.z.ToString(fmt, CultureInfo.InvariantCulture));
                }
                sb.AppendLine("\t}");

                // 面
                sb.AppendLine($"\tface {obj.Faces.Count} {{");
                foreach (var face in obj.Faces)
                {
                    sb.Append($"\t\t{face.VertexCount} V(");
                    for (int i = 0; i < face.VertexCount; i++)
                    {
                        if (i > 0) sb.Append(" ");
                        sb.Append(face.VertexIndices[i]);
                    }
                    sb.Append(")");

                    if (face.MaterialIndex >= 0)
                    {
                        sb.Append($" M({face.MaterialIndex})");
                    }

                    if (face.UVs != null && face.UVs.Length > 0)
                    {
                        sb.Append(" UV(");
                        for (int i = 0; i < face.UVs.Length; i++)
                        {
                            if (i > 0) sb.Append(" ");
                            sb.Append(face.UVs[i].x.ToString(fmt, CultureInfo.InvariantCulture));
                            sb.Append(" ");
                            sb.Append(face.UVs[i].y.ToString(fmt, CultureInfo.InvariantCulture));
                        }
                        sb.Append(")");
                    }

                    // COL属性（頂点カラー/頂点ID用）
                    if (face.VertexColors != null && face.VertexColors.Length > 0)
                    {
                        sb.Append(" COL(");
                        for (int i = 0; i < face.VertexColors.Length; i++)
                        {
                            if (i > 0) sb.Append(" ");
                            sb.Append(face.VertexColors[i]);
                        }
                        sb.Append(")");
                    }

                    sb.AppendLine();
                }
                sb.AppendLine("\t}");

                sb.AppendLine("}");
            }

            sb.AppendLine("Eof");

            return sb.ToString();
        }
    }

    // ================================================================
    // WriteBackフラグ
    // ================================================================

    /// <summary>
    /// WriteBack時に何を更新するかのフラグ
    /// </summary>
    [Flags]
    public enum WriteBackFlags
    {
        /// <summary>何も更新しない</summary>
        None = 0,

        /// <summary>頂点位置を更新</summary>
        Position = 1 << 0,

        /// <summary>面のUVを更新</summary>
        UV = 1 << 1,

        /// <summary>ボーンウェイト特殊面を更新（頂点IDも含む）</summary>
        BoneWeight = 1 << 2,

        /// <summary>全て更新</summary>
        All = Position | UV | BoneWeight
    }

    // ================================================================
    // 結果クラス
    // ================================================================

    /// <summary>
    /// エクスポート結果
    /// </summary>
    public class MQOExportResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string FilePath { get; set; }
        public MQOExportStats Stats { get; } = new MQOExportStats();
    }

    /// <summary>
    /// エクスポート統計
    /// </summary>
    public class MQOExportStats
    {
        public int ObjectCount { get; set; }
        public int TotalVertices { get; set; }
        public int TotalFaces { get; set; }
        public int MaterialCount { get; set; }
    }
}