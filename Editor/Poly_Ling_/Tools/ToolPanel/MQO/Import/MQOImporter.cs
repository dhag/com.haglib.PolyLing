// Assets/Editor/Poly_Ling/MQO/Import/MQOImporter.cs
// MQODocument → MeshObject/MeshUndoContext 変換
// SimpleMeshFactoryのデータ構造に変換

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;
using Poly_Ling.Materials;

// MeshContextはSimpleMeshFactoryのネストクラス
//using MeshContext = MeshContext;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQOインポート結果
    /// </summary>
    public class MQOImportResult
    {
        /// <summary>成功したか</summary>
        public bool Success { get; set; }

        /// <summary>エラーメッセージ</summary>
        public string ErrorMessage { get; set; }

        /// <summary>インポートされたMeshContextリスト</summary>
        public List<MeshContext> MeshContexts { get; } = new List<MeshContext>();

        /// <summary>インポートされたボーンMeshContextリスト</summary>
        public List<MeshContext> BoneMeshContexts { get; } = new List<MeshContext>();

        /// <summary>インポートされたマテリアルリスト</summary>
        public List<Material> Materials { get; } = new List<Material>();

        /// <summary>インポートされたマテリアル参照リスト（ソースパス情報付き）</summary>
        public List<MaterialReference> MaterialReferences { get; } = new List<MaterialReference>();

        /// <summary>
        /// ミラー側マテリアルのオフセット
        /// ミラー側マテリアルインデックス = 実体側インデックス + MirrorMaterialOffset
        /// </summary>
        public int MirrorMaterialOffset { get; set; } = 0;

        /// <summary>元のMQOドキュメント</summary>
        public MQODocument Document { get; set; }

        /// <summary>インポート統計</summary>
        public MQOImportStats Stats { get; } = new MQOImportStats();

        /// <summary>
        /// 全MeshContextの面のMaterialIndexにオフセットを加算
        /// Appendモードで既存マテリアルがある場合に使用
        /// </summary>
        /// <param name="offset">加算するオフセット（既存マテリアル数）</param>
        public void ApplyMaterialIndexOffset(int offset)
        {
            if (offset <= 0) return;

            foreach (var meshContext in MeshContexts)
            {
                if (meshContext?.MeshObject == null) continue;

                foreach (var face in meshContext.MeshObject.Faces)
                {
                    if (face.MaterialIndex >= 0)
                    {
                        face.MaterialIndex += offset;
                    }
                }
            }

            Debug.Log($"[MQOImportResult] Applied material index offset: +{offset}");
        }

        /// <summary>
        /// ボーンMeshContextsの親インデックスにオフセットを加算
        /// メッシュの後にボーンを追加する場合に使用
        /// </summary>
        /// <param name="offset">加算するオフセット（メッシュ数）</param>
        public void ApplyBoneParentIndexOffset(int offset)
        {
            if (offset <= 0) return;

            foreach (var boneCtx in BoneMeshContexts)
            {
                if (boneCtx == null) continue;

                // 親インデックスがある場合のみオフセット
                if (boneCtx.ParentIndex >= 0)
                {
                    boneCtx.ParentIndex += offset;
                }
                if (boneCtx.HierarchyParentIndex >= 0)
                {
                    boneCtx.HierarchyParentIndex += offset;
                }
            }

            Debug.Log($"[MQOImportResult] Applied bone parent index offset: +{offset}");
        }

        /// <summary>
        /// 全MeshContextのBoneWeightインデックスにオフセットを加算
        /// メッシュの後にボーンを追加する場合、BoneWeightのboneIndexを調整
        /// </summary>
        /// <param name="offset">加算するオフセット（メッシュ数）</param>
        public void ApplyBoneWeightIndexOffset(int offset)
        {
            if (offset <= 0) return;

            int adjustedVertices = 0;
            int adjustedMirrorVertices = 0;
            foreach (var meshContext in MeshContexts)
            {
                if (meshContext?.MeshObject == null) continue;

                foreach (var vertex in meshContext.MeshObject.Vertices)
                {
                    // 実体側BoneWeight
                    if (vertex.BoneWeight.HasValue)
                    {
                        var bw = vertex.BoneWeight.Value;
                        bw.boneIndex0 += offset;
                        bw.boneIndex1 += offset;
                        bw.boneIndex2 += offset;
                        bw.boneIndex3 += offset;
                        vertex.BoneWeight = bw;
                        adjustedVertices++;
                    }

                    // ミラー側BoneWeight
                    if (vertex.MirrorBoneWeight.HasValue)
                    {
                        var mbw = vertex.MirrorBoneWeight.Value;
                        mbw.boneIndex0 += offset;
                        mbw.boneIndex1 += offset;
                        mbw.boneIndex2 += offset;
                        mbw.boneIndex3 += offset;
                        vertex.MirrorBoneWeight = mbw;
                        adjustedMirrorVertices++;
                    }
                }
            }

            Debug.Log($"[MQOImportResult] Applied bone weight index offset: +{offset} to {adjustedVertices} vertices, {adjustedMirrorVertices} mirror vertices");
        }
    }

    /// <summary>
    /// インポート統計情報
    /// </summary>
    public class MQOImportStats
    {
        public int ObjectCount { get; set; }
        public int TotalVertices { get; set; }
        public int TotalFaces { get; set; }
        public int MaterialCount { get; set; }
        public int BoneCount { get; set; }
        public int SkippedSpecialFaces { get; set; }
    }

    /// <summary>
    /// MQOインポーター
    /// </summary>
    public static class MQOImporter
    {
        // ================================================================
        // パブリックAPI
        // ================================================================

        /// <summary>
        /// ファイルからインポート
        /// </summary>
        public static MQOImportResult ImportFile(string filePath, MQOImportSettings settings = null)
        {
            var result = new MQOImportResult();
            settings = settings ?? new MQOImportSettings();
            
            // ベースディレクトリを設定（テクスチャ読み込み用）
            settings.BaseDir = Path.GetDirectoryName(filePath)?.Replace('\\', '/') ?? "";

            try
            {
                // パース
                var document = MQOParser.ParseFile(filePath);
                result.Document = document;

                // 変換
                ConvertDocument(document, settings, result);

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[MQOImporter] Failed to import: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// 文字列からインポート
        /// </summary>
        public static MQOImportResult ImportFromString(string content, MQOImportSettings settings = null)
        {
            var result = new MQOImportResult();
            settings = settings ?? new MQOImportSettings();

            try
            {
                var document = MQOParser.Parse(content);
                result.Document = document;
                ConvertDocument(document, settings, result);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[MQOImporter] Failed to import: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// MQODocumentからインポート
        /// </summary>
        public static MQOImportResult Import(MQODocument document, MQOImportSettings settings = null)
        {
            var result = new MQOImportResult();
            settings = settings ?? new MQOImportSettings();
            result.Document = document;

            try
            {
                ConvertDocument(document, settings, result);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        // ================================================================
        // 変換処理
        // ================================================================

        private static void ConvertDocument(MQODocument document, MQOImportSettings settings, MQOImportResult result)
        {
            // マテリアル変換
            if (settings.ImportMaterials)
            {
                // 実体側マテリアル
                foreach (var mqoMat in document.Materials)
                {
                    var matRef = ConvertMaterialToRef(mqoMat, settings);
                    result.Materials.Add(matRef.Material);
                    result.MaterialReferences.Add(matRef);
                }
                
                // ミラー側マテリアルオフセットを記録
                result.MirrorMaterialOffset = result.Materials.Count;
                
                // ミラー側マテリアル（実体側を複製、名前に"+"を付加、ソースパスを引き継ぐ）
                foreach (var mqoMat in document.Materials)
                {
                    var matRef = ConvertMaterialToRef(mqoMat, settings);
                    matRef.Data.Name = matRef.Data.Name + "+";
                    matRef.Material.name = matRef.Data.Name;
                    result.Materials.Add(matRef.Material);
                    result.MaterialReferences.Add(matRef);
                }
                
                result.Stats.MaterialCount = result.Materials.Count;
                Debug.Log($"[MQOImporter] Materials: {result.MirrorMaterialOffset} original + {result.MirrorMaterialOffset} mirror = {result.Materials.Count} total");
            }

            // ボーンCSVを先にロード
            List<PmxBoneData> boneDataList = null;
            Dictionary<string, int> boneNameToIndex = null;
            
            if (settings.UseBoneCSV)
            {
                Debug.Log($"[MQOImporter] Loading bone CSV: {settings.BoneCSVPath}");
                boneDataList = PmxBoneCSVParser.ParseFile(settings.BoneCSVPath);
                if (boneDataList.Count > 0)
                {
                    Debug.Log($"[MQOImporter] Bone CSV loaded: {boneDataList.Count} bones");
                    
                    // === PMXと同じ方式: ボーンを先にMeshContextsに追加 ===
                    var boneMeshContexts = ConvertBonesToMeshContexts(boneDataList, settings);
                    
                    // boneNameToIndex: ボーン名 → result.MeshContexts内のインデックス
                    boneNameToIndex = new Dictionary<string, int>();
                    for (int i = 0; i < boneMeshContexts.Count; i++)
                    {
                        result.MeshContexts.Add(boneMeshContexts[i]);
                        boneNameToIndex[boneMeshContexts[i].Name] = i;
                    }
                    
                    result.Stats.BoneCount = boneMeshContexts.Count;
                    Debug.Log($"[MQOImporter] Added {boneMeshContexts.Count} bones to MeshContexts");
                }
                else
                {
                    Debug.LogWarning($"[MQOImporter] Bone CSV is empty or failed to load");
                }
            }

            // ボーン数を記録（メッシュのParentIndex計算用）
            int boneContextCount = result.MeshContexts.Count;

            // ボーンウェイトCSVをロード（設定されている場合）
            BoneWeightCSVData boneWeightData = null;
            if (settings.UseBoneWeightCSV)
            {
                Debug.Log($"[MQOImporter] Loading bone weight CSV: {settings.BoneWeightCSVPath}");
                boneWeightData = MQOBoneWeightCSVParser.ParseFile(settings.BoneWeightCSVPath);
                if (boneWeightData != null && boneWeightData.AllBoneNames.Count > 0)
                {
                    // boneNameToIndexがまだない場合（ボーンCSVなし）はウェイトCSVから作成
                    if (boneNameToIndex == null)
                    {
                        boneNameToIndex = MQOBoneWeightApplier.CreateBoneNameToIndexMap(boneWeightData.AllBoneNames);
                        Debug.Log($"[MQOImporter] Using bone weight CSV order for indices: {boneNameToIndex.Count} bones");
                    }
                    Debug.Log($"[MQOImporter] Bone weight CSV loaded: {boneWeightData.ObjectWeights.Count} objects");
                }
                else
                {
                    Debug.LogWarning($"[MQOImporter] Bone weight CSV is empty or failed to load");
                }
            }

            // オブジェクト変換（メッシュ）
            int boneWeightAppliedObjects = 0;
            int boneWeightSkippedObjects = 0;
            foreach (var mqoObj in document.Objects)
            {
                // 非表示オブジェクトをスキップ
                if (settings.SkipHiddenObjects && !mqoObj.IsVisible)
                    continue;

                var meshContext = ConvertObject(mqoObj, document.Materials, result.Materials, settings, result.Stats, result.MirrorMaterialOffset);
                if (meshContext != null)
                {
                    // ボーンウェイト適用
                    if (boneWeightData != null && boneNameToIndex != null)
                    {
                        // 実体側のウェイト適用
                        var objectWeights = boneWeightData.GetObjectWeights(mqoObj.Name);
                        if (objectWeights != null)
                        {
                            MQOBoneWeightApplier.ApplyBoneWeights(meshContext.MeshObject, objectWeights, boneNameToIndex);
                            boneWeightAppliedObjects++;
                        }
                        else
                        {
                            boneWeightSkippedObjects++;
                            Debug.Log($"[MQOImporter] No bone weight data for object '{mqoObj.Name}'");
                        }

                        // ミラー側のウェイト適用（オブジェクト名+"+"）
                        if (meshContext.IsMirrored)
                        {
                            var mirrorObjectWeights = boneWeightData.GetObjectWeights(mqoObj.Name + "+");
                            if (mirrorObjectWeights != null)
                            {
                                MQOBoneWeightApplier.ApplyMirrorBoneWeights(meshContext.MeshObject, mirrorObjectWeights, boneNameToIndex);
                                Debug.Log($"[MQOImporter] Applied mirror bone weights for '{mqoObj.Name}+'");
                            }
                            else
                            {
                                Debug.Log($"[MQOImporter] No mirror bone weight data for object '{mqoObj.Name}+'");
                            }
                        }
                    }

                    result.MeshContexts.Add(meshContext);
                }
            }

            // ボーンウェイト適用サマリ
            if (boneWeightData != null)
            {
                Debug.Log($"[MQOImporter] === Bone Weight Summary ===");
                Debug.Log($"[MQOImporter]   Applied: {boneWeightAppliedObjects} objects");
                Debug.Log($"[MQOImporter]   Skipped (no CSV data): {boneWeightSkippedObjects} objects");
            }

            result.Stats.ObjectCount = result.MeshContexts.Count - boneContextCount;

            // ================================================================
            // ベイクミラー処理
            // ================================================================
            if (settings.BakeMirror)
            {
                var bakedMirrorContexts = new List<MeshContext>();
                
                for (int i = 0; i < result.MeshContexts.Count; i++)
                {
                    var ctx = result.MeshContexts[i];
                    if (ctx.IsMirrored && ctx.Type == MeshType.Mesh)
                    {
                        var bakedMirror = CreateBakedMirrorMesh(ctx, i, settings);
                        if (bakedMirror != null)
                        {
                            bakedMirrorContexts.Add(bakedMirror);
                            Debug.Log($"[MQOImporter] Created baked mirror: {bakedMirror.Name} (source: {ctx.Name})");
                        }
                    }
                }
                
                // ベイクしたミラーメッシュを追加
                result.MeshContexts.AddRange(bakedMirrorContexts);
                
                if (bakedMirrorContexts.Count > 0)
                {
                    Debug.Log($"[MQOImporter] Baked {bakedMirrorContexts.Count} mirror meshes");
                }
            }

            // 統合オプション（ボーン以外のメッシュのみ対象）
            // 注意: MergeObjectsが有効な場合、ボーンウェイトの整合性に注意が必要
            if (settings.MergeObjects && result.MeshContexts.Count > boneContextCount + 1)
            {
                // ボーン部分を保持
                var boneContexts = result.MeshContexts.GetRange(0, boneContextCount);
                var meshContexts = result.MeshContexts.GetRange(boneContextCount, result.MeshContexts.Count - boneContextCount);
                
                var merged = MergeAllMeshContexts(meshContexts, document.FileName ?? "Merged");
                
                result.MeshContexts.Clear();
                result.MeshContexts.AddRange(boneContexts);
                result.MeshContexts.Add(merged);
            }

            // 親子関係を計算（DepthからParentIndexを算出）- メッシュ部分のみ
            // ボーンの親子関係はConvertBonesToMeshContextsで既に設定済み
            if (boneContextCount > 0)
            {
                // メッシュ部分のみ親子関係を計算
                var meshOnlyList = result.MeshContexts.GetRange(boneContextCount, result.MeshContexts.Count - boneContextCount);
                CalculateParentIndices(meshOnlyList);
            }
            else
            {
                CalculateParentIndices(result.MeshContexts);
            }
        }

        /// <summary>
        /// Depth値から親子関係（ParentIndex）を計算
        /// MQOのDepth値はリスト順序に依存するため、インポート時に親子関係を確定させる
        /// </summary>
        private static void CalculateParentIndices(List<MeshContext> meshContexts)
        {
            if (meshContexts == null || meshContexts.Count == 0)
                return;

            // スタック: (インデックス, Depth) を保持
            // 現在のDepth以下の最も近い親を見つけるために使用
            var parentStack = new Stack<(int index, int depth)>();

            for (int i = 0; i < meshContexts.Count; i++)
            {
                var ctx = meshContexts[i];
                int currentDepth = ctx.Depth;

                if (currentDepth == 0)
                {
                    // ルートオブジェクト
                    ctx.ParentIndex = -1;
                    parentStack.Clear();
                    parentStack.Push((i, currentDepth));
                }
                else
                {
                    // 現在のDepthより小さいDepthを持つ最も近い親を探す
                    while (parentStack.Count > 0 && parentStack.Peek().depth >= currentDepth)
                    {
                        parentStack.Pop();
                    }

                    if (parentStack.Count > 0)
                    {
                        ctx.ParentIndex = parentStack.Peek().index;
                    }
                    else
                    {
                        // 親が見つからない場合はルート扱い
                        ctx.ParentIndex = -1;
                    }

                    parentStack.Push((i, currentDepth));
                }
            }
        }

        // ================================================================
        // ボーン変換
        // ================================================================

        /// <summary>
        /// PmxBoneデータリストをMeshContextリストに変換
        /// </summary>
        private static List<MeshContext> ConvertBonesToMeshContexts(List<PmxBoneData> boneDataList, MQOImportSettings settings)
        {
            var result = new List<MeshContext>();
            var boneNameToIndex = new Dictionary<string, int>();

            // まず全ボーン名とインデックスのマップを作成
            for (int i = 0; i < boneDataList.Count; i++)
            {
                var bone = boneDataList[i];
                if (!string.IsNullOrEmpty(bone.Name) && !boneNameToIndex.ContainsKey(bone.Name))
                {
                    boneNameToIndex[bone.Name] = i;
                }
            }

            // ボーンのワールド位置を変換済みで保持（ローカル座標計算用）
            float pmxScale = settings.BoneScale;
            var boneWorldPositions = new Vector3[boneDataList.Count];
            for (int i = 0; i < boneDataList.Count; i++)
            {
                var bone = boneDataList[i];
                boneWorldPositions[i] = new Vector3(
                    bone.Position.x * pmxScale * settings.Scale,
                    bone.Position.y * pmxScale * settings.Scale,
                    settings.FlipZ ? -bone.Position.z * pmxScale * settings.Scale : bone.Position.z * pmxScale * settings.Scale
                );
            }

            // 各ボーンをMeshContextに変換
            for (int i = 0; i < boneDataList.Count; i++)
            {
                var bone = boneDataList[i];
                Vector3 worldPosition = boneWorldPositions[i];

                // 親インデックスを解決
                int parentIndex = -1;
                if (!string.IsNullOrEmpty(bone.ParentName) && boneNameToIndex.TryGetValue(bone.ParentName, out int pIdx))
                {
                    parentIndex = pIdx;
                }

                // ローカル位置を計算（親がいる場合は親からの相対位置）
                Vector3 localPosition;
                if (parentIndex >= 0)
                {
                    Vector3 parentWorldPos = boneWorldPositions[parentIndex];
                    localPosition = worldPosition - parentWorldPos;
                }
                else
                {
                    localPosition = worldPosition;
                }

                // MeshObjectを作成
                var meshObject = new MeshObject(bone.Name)
                {
                    Type = MeshType.Bone,
                    HierarchyParentIndex = parentIndex
                };

                // BindPose行列を計算（ワールド位置からの逆変換）
                // 回転・スケールなしの場合、worldToLocalMatrix = 平行移動(-worldPosition)
                Matrix4x4 bindPose = Matrix4x4.Translate(-worldPosition);

                // BoneTransformを設定（ローカル座標）
                var boneTransform = new BoneTransform
                {
                    Position = localPosition,
                    Rotation = Vector3.zero,
                    Scale = Vector3.one,
                    UseLocalTransform = true,
                    ExportAsSkinned = true  // ★スキンドメッシュとして出力
                };
                meshObject.BoneTransform = boneTransform;

                // MeshContextを作成
                var meshContext = new MeshContext
                {
                    MeshObject = meshObject,
                    Type = MeshType.Bone,
                    IsVisible = true,
                    BindPose = bindPose  // ★インポート時計算のBindPose
                };

                // 親インデックスを設定（MeshContextにも設定）
                meshContext.HierarchyParentIndex = parentIndex;

                result.Add(meshContext);
            }

            Debug.Log($"[MQOImporter] Converted {result.Count} bones to MeshContexts");
            return result;
        }

        // ================================================================
        // オブジェクト変換
        // ================================================================

        private static MeshContext ConvertObject(
            MQOObject mqoObj,
            List<MQOMaterial> mqoMaterials,
            List<Material> unityMaterials,
            MQOImportSettings settings,
            MQOImportStats stats,
            int mirrorMaterialOffset = 0)
        {
            var meshObject = new MeshObject();

            // 頂点変換（IDは後で設定）
            foreach (var mqoVert in mqoObj.Vertices)
            {
                Vector3 pos = ConvertPosition(mqoVert.Position, settings);
                var vertex = new Vertex(pos);
                vertex.Id = -1;  // 初期値: IDなし
                meshObject.AddVertexRaw(vertex);  // ID管理なしで追加
                stats.TotalVertices++;
            }

            // 特殊面から頂点IDを抽出（面変換の前に処理）
            foreach (var mqoFace in mqoObj.Faces)
            {
                if (!mqoFace.IsSpecialFace)
                    continue;

                // COL属性の3番目の値が頂点ID
                // 例: 3 V(0 0 0) M(0) COL(1 1 100000) → 頂点0のIDは100000
                if (mqoFace.VertexColors != null && mqoFace.VertexColors.Length >= 3)
                {
                    int vertIndex = mqoFace.VertexIndices[0];
                    int vertexId = (int)mqoFace.VertexColors[2];

                    if (vertIndex >= 0 && vertIndex < meshObject.Vertices.Count)
                    {
                        meshObject.Vertices[vertIndex].Id = vertexId;
                        meshObject.RegisterVertexId(vertexId);
                    }
                }

                stats.SkippedSpecialFaces++;
            }

            // 面変換
            foreach (var mqoFace in mqoObj.Faces)
            {
                // 特殊面（メタデータ）はスキップ（既に処理済み）
                if (mqoFace.IsSpecialFace)
                    continue;

                // 1頂点（点）、2頂点（線）は補助線として扱う
                if (mqoFace.VertexCount < 3)
                {
                    ConvertLine(mqoFace, meshObject, settings);
                    continue;
                }

                // 3頂点以上は面として変換
                ConvertFace(mqoFace, meshObject, settings);
                stats.TotalFaces++;
            }

            // IDが未設定（-1）の頂点はそのまま（外部からIDが与えられていない）

            // OriginalPositions作成
            var originalPositions = new Vector3[meshObject.VertexCount];
            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                originalPositions[i] = meshObject.Vertices[i].Position;
            }

            // MeshContext作成
            var meshContext = new MeshContext
            {
                Name = mqoObj.Name,
                MeshObject = meshObject,
                OriginalPositions = originalPositions,
                Materials = new List<Material>(),
                // オブジェクト属性をコピー
                Depth = mqoObj.Depth,
                IsVisible = mqoObj.IsVisible,
                IsLocked = mqoObj.IsLocked,
                IsFolding = mqoObj.IsFolding,
                // ミラー設定をコピー
                MirrorType = mqoObj.MirrorMode,
                MirrorAxis = mqoObj.MirrorAxis,
                MirrorDistance = mqoObj.MirrorDistance,
                MirrorMaterialOffset = mirrorMaterialOffset
            };

            // マテリアル設定
            // Phase 5: マテリアルはMQOImportResultにグローバルリストとして保存される
            // MeshContext.Materialsへの設定は不要（ModelContext.Materialsで管理）
            // 代わりに、ToUnityMeshにマテリアル数を渡してサブメッシュを正しく生成

            // 使用されているマテリアルの最大インデックスを取得
            int maxMaterialIndex = 0;
            foreach (var face in meshObject.Faces)
            {
                if (face.MaterialIndex > maxMaterialIndex)
                    maxMaterialIndex = face.MaterialIndex;
            }
            int materialCount = settings.ImportMaterials && unityMaterials.Count > 0
                ? unityMaterials.Count
                : maxMaterialIndex + 1;

            // メッシュ名を設定
            meshObject.Name = mqoObj.Name;

            // 法線スムージング（NormalMode.Smoothの場合のみ）
            if (settings.NormalMode == NormalMode.Smooth)
            {
                CalculateSmoothNormals(meshObject, settings.SmoothingAngle);
            }
            else if (settings.NormalMode == NormalMode.Unity)
            {
                // Unity標準のRecalculateNormalsを使用（ToUnityMeshShared後に呼ばれる）
            }
            // NormalMode.FaceNormalの場合はCalculateFaceNormalで設定済みの面法線をそのまま使用

            // Unity Mesh生成（マテリアル数を渡す）
            meshContext.UnityMesh = meshObject.ToUnityMeshShared(materialCount);
            
            // NormalMode.Unityの場合はUnity標準のRecalculateNormalsを使用
            if (settings.NormalMode == NormalMode.Unity && meshContext.UnityMesh != null)
            {
                meshContext.UnityMesh.RecalculateNormals();
                Debug.Log($"[MQOImporter] Unity RecalculateNormals applied");
            }
            
            // 頂点デバッグ出力
            if (settings.DebugVertexInfo)
            {
                OutputVertexDebugInfo(mqoObj.Name, mqoObj, meshObject, settings.DebugVertexNearUVCount);
            }
            
            /*
            // デバッグ出力（ミラー属性確認用）
            Debug.Log($"[MQOImporter] ConvertObject: {mqoObj.Name}");
            Debug.Log($"  - MeshObject: V={meshObject.VertexCount}, F={meshObject.FaceCount}");
            Debug.Log($"  - MQO Mirror: Mode={mqoObj.MirrorMode}, Axis={mqoObj.MirrorAxis}, Dist={mqoObj.MirrorDistance}");
            Debug.Log($"  - MeshUndoContext: IsMirrored={meshContext.IsMirrored}, MirrorType={meshContext.MirrorType}, MirrorAxis={meshContext.MirrorAxis}");
            Debug.Log($"  - UnityMesh: V={meshContext.UnityMesh?.vertexCount ?? 0}, T={meshContext.UnityMesh?.triangles?.Length ?? 0}");
            */
            return meshContext;
        }

        // ================================================================
        // 面変換
        // ================================================================

        private static void ConvertFace(MQOFace mqoFace, MeshObject meshObject, MQOImportSettings settings)
        {
            int vertexCount = mqoFace.VertexCount;

            // 頂点インデックス
            var vertexIndices = new List<int>(mqoFace.VertexIndices);

            // UVサブインデックスを計算
            var uvSubIndices = new List<int>();
            for (int i = 0; i < vertexCount; i++)
            {
                int vertIndex = mqoFace.VertexIndices[i];
                Vector2 uv = (mqoFace.UVs != null && i < mqoFace.UVs.Length)
                    ? ConvertUV(mqoFace.UVs[i], settings)
                    : Vector2.zero;

                // 頂点にUVを追加し、サブインデックスを取得
                var vertex = meshObject.Vertices[vertIndex];
                int uvSubIndex = AddOrGetUVIndex(vertex, uv);
                uvSubIndices.Add(uvSubIndex);
            }

            // Face作成
            var face = new Face
            {
                MaterialIndex = mqoFace.MaterialIndex >= 0 ? mqoFace.MaterialIndex : 0
            };

            // 頂点とUVサブインデックスを追加（元の順序のまま）
            for (int i = 0; i < vertexCount; i++)
            {
                face.VertexIndices.Add(vertexIndices[i]);
                face.UVIndices.Add(uvSubIndices[i]);
                face.NormalIndices.Add(0);
            }

            meshObject.Faces.Add(face);

            // 法線計算
            CalculateFaceNormal(face, meshObject);
        }

        private static void ConvertLine(MQOFace mqoFace, MeshObject meshObject, MQOImportSettings settings)
        {
            if (mqoFace.VertexCount < 2) return;

            // 2頂点の補助線として追加
            var face = new Face
            {
                MaterialIndex = 0
            };

            for (int i = 0; i < mqoFace.VertexCount; i++)
            {
                face.VertexIndices.Add(mqoFace.VertexIndices[i]);
                face.UVIndices.Add(0);
                face.NormalIndices.Add(0);
            }

            meshObject.Faces.Add(face);
        }

        // ================================================================
        // マテリアル変換
        // ================================================================

        private static MaterialReference ConvertMaterialToRef(MQOMaterial mqoMat, MQOImportSettings settings)
        {
            // URPシェーダーを優先
            Shader shader = FindBestShader();
            var material = new Material(shader);
            material.name = mqoMat.Name;

            // 色設定
            Color color = mqoMat.Color;
            SetMaterialColor(material, color);

            // 不透明度（Color.a）が1未満の場合は透過マテリアルに設定
            if (color.a < 1f - 0.001f)
            {
                SetMaterialTransparent(material);
                Debug.Log($"[MQOImporter] Material '{mqoMat.Name}' set to Transparent (alpha={color.a:F2})");
            }

            // その他のプロパティ
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", mqoMat.Specular);

            // テクスチャ読み込み
            if (!string.IsNullOrEmpty(mqoMat.TexturePath))
            {
                var texture = LoadTexture(mqoMat.TexturePath, settings.BaseDir);
                if (texture != null)
                {
                    SetMaterialTexture(material, "_BaseMap", "_MainTex", texture);
                }
            }

            // バンプマップ
            if (!string.IsNullOrEmpty(mqoMat.BumpMapPath))
            {
                var texture = LoadTexture(mqoMat.BumpMapPath, settings.BaseDir);
                if (texture != null)
                {
                    SetMaterialTexture(material, "_BumpMap", "_BumpMap", texture);
                }
            }

            // MaterialReferenceを作成し、ソースパスを設定
            var matRef = new MaterialReference(material);
            
            // ソースパスを設定（元のMQOファイルの相対パスをそのまま保存）
            if (!string.IsNullOrEmpty(mqoMat.TexturePath))
            {
                matRef.Data.SourceTexturePath = mqoMat.TexturePath;
            }
            if (!string.IsNullOrEmpty(mqoMat.AlphaMapPath))
            {
                matRef.Data.SourceAlphaMapPath = mqoMat.AlphaMapPath;
            }
            if (!string.IsNullOrEmpty(mqoMat.BumpMapPath))
            {
                matRef.Data.SourceBumpMapPath = mqoMat.BumpMapPath;
            }

            return matRef;
        }

        /// <summary>
        /// テクスチャパスを解決（相対パスならフルパスに変換）
        /// テクスチャ読み込み用
        /// </summary>
        private static string ResolveTexturePath(string texturePath, string baseDir)
        {
            if (string.IsNullOrEmpty(texturePath))
                return null;

            string normalizedPath = texturePath.Replace("\\", "/");
            
            // 既にフルパスの場合
            if (Path.IsPathRooted(normalizedPath))
                return normalizedPath;

            // 相対パスの場合、baseDirと結合
            if (!string.IsNullOrEmpty(baseDir))
            {
                return Path.GetFullPath(Path.Combine(baseDir, normalizedPath)).Replace("\\", "/");
            }

            return normalizedPath;
        }

        /// <summary>後方互換用：Material を返す</summary>
        private static Material ConvertMaterial(MQOMaterial mqoMat, MQOImportSettings settings)
        {
            return ConvertMaterialToRef(mqoMat, settings).Material;
        }

        /// <summary>
        /// マテリアルを透過モードに設定（URP/Standard両対応）
        /// </summary>
        private static void SetMaterialTransparent(Material material)
        {
            // URP Lit用設定
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1); // 0=Opaque, 1=Transparent
                material.SetOverrideTag("RenderType", "Transparent");
            }
            if (material.HasProperty("_Blend"))
            {
                material.SetFloat("_Blend", 0); // 0=Alpha, 1=Premultiply, 2=Additive, 3=Multiply
            }
            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0);
            }
            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }
            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            if (material.HasProperty("_SrcBlendAlpha"))
            {
                material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            }
            if (material.HasProperty("_DstBlendAlpha"))
            {
                material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0);
            }

            // Standard Shader用設定
            if (material.HasProperty("_Mode"))
            {
                material.SetFloat("_Mode", 3); // 0=Opaque, 1=Cutout, 2=Fade, 3=Transparent
            }

            // レンダーキュー設定
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            // キーワード設定（URP）
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            
            // Standard Shader用キーワード
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }
        
        /// <summary>
        /// テクスチャを読み込み
        /// Assets内 → AssetDatabase、Assets外 → File.ReadAllBytes
        /// </summary>
        private static Texture2D LoadTexture(string texturePath, string baseDir)
        {
            if (string.IsNullOrEmpty(texturePath))
                return null;

            // パス区切り文字を正規化（\ → /）
            // MQOファイルではバックスラッシュが使われることが多い
            string normalizedPath = texturePath.Replace("\\", "/");
            string normalizedBaseDir = baseDir?.Replace("\\", "/") ?? "";
            
            Debug.Log($"[MQOImporter] LoadTexture: original='{texturePath}', normalized='{normalizedPath}', baseDir='{normalizedBaseDir}'");

            // 実際のファイルパスを構築
            string fullPath;
            if (Path.IsPathRooted(normalizedPath))
            {
                fullPath = normalizedPath;
            }
            else
            {
                if (!string.IsNullOrEmpty(normalizedBaseDir))
                {
                    fullPath = Path.Combine(normalizedBaseDir, normalizedPath).Replace("\\", "/");
                }
                else
                {
                    fullPath = normalizedPath;
                }
            }
            
            Debug.Log($"[MQOImporter] LoadTexture: fullPath='{fullPath}'");

            // アセットパスを構築（Assets/から始まる形式）
            string assetPath = fullPath;
            bool isInsideAssets = false;
            if (!assetPath.StartsWith("Assets/"))
            {
                int assetsIdx = assetPath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                if (assetsIdx >= 0)
                {
                    assetPath = assetPath.Substring(assetsIdx + 1);
                    isInsideAssets = true;
                }
                else
                {
                    assetsIdx = assetPath.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                    if (assetsIdx >= 0)
                    {
                        assetPath = assetPath.Substring(assetsIdx);
                        isInsideAssets = true;
                    }
                }
            }
            else
            {
                isInsideAssets = true;
            }

            // 1. まずAssetDatabaseから読み込みを試す
            Texture2D texture = null;
            if (isInsideAssets)
            {
                texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            }

            // 2. Assets内の場合のみ、同じbaseDir内でファイル名検索
            if (texture == null && isInsideAssets)
            {
                string fileName = Path.GetFileName(normalizedPath);
                string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                
                // baseDirをAssets/形式に変換
                string searchFolder = normalizedBaseDir;
                if (!searchFolder.StartsWith("Assets/"))
                {
                    int idx = searchFolder.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        searchFolder = searchFolder.Substring(idx);
                    }
                }

                string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:Texture2D {fileNameWithoutExt}", 
                    new[] { searchFolder });
                foreach (var guid in guids)
                {
                    string foundPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileName(foundPath).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(foundPath);
                        if (texture != null)
                        {
                            Debug.Log($"[MQOImporter] Texture found in baseDir: {foundPath}");
                            break;
                        }
                    }
                }
            }

            // 3. それでも失敗した場合、File.ReadAllBytesで直接読み込み
            if (texture == null && File.Exists(fullPath))
            {
                try
                {
                    byte[] fileData = File.ReadAllBytes(fullPath);
                    texture = new Texture2D(2, 2);
                    if (texture.LoadImage(fileData))
                    {
                        texture.name = Path.GetFileNameWithoutExtension(fullPath);
                        Debug.Log($"[MQOImporter] Texture loaded from file: {fullPath}");
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                        texture = null;
                        Debug.LogWarning($"[MQOImporter] Failed to load image data: {fullPath}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MQOImporter] Failed to read texture file: {fullPath} - {e.Message}");
                }
            }

            if (texture == null)
            {
                Debug.LogWarning($"[MQOImporter] Texture not found: {fullPath} (original: {texturePath})");
            }

            return texture;
        }
        
        /// <summary>
        /// マテリアルにテクスチャを設定
        /// </summary>
        private static void SetMaterialTexture(Material material, string urpPropertyName, string standardPropertyName, Texture texture)
        {
            if (material == null || texture == null) return;

            if (material.HasProperty(urpPropertyName))
            {
                material.SetTexture(urpPropertyName, texture);
            }
            else if (material.HasProperty(standardPropertyName))
            {
                material.SetTexture(standardPropertyName, texture);
            }
        }

        private static Shader FindBestShader()
        {
            // 優先順位でシェーダーを探す
            string[] shaderNames = new[]
            {
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Simple Lit",
                "HDRP/Lit",
                "Standard",
                "Unlit/Color"
            };

            foreach (var name in shaderNames)
            {
                var shader = Shader.Find(name);
                if (shader != null)
                    return shader;
            }

            return Shader.Find("Standard");
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        private static Material CreateDefaultMaterial()
        {
            var shader = FindBestShader();
            var material = new Material(shader);
            material.name = "Default";
            SetMaterialColor(material, new Color(0.7f, 0.7f, 0.7f, 1f));
            return material;
        }

        // ================================================================
        // 座標変換
        // ================================================================

        private static Vector3 ConvertPosition(Vector3 mqoPos, MQOImportSettings settings)
        {
            // MQO座標系 → Unity座標系
            float x = mqoPos.x * settings.Scale;
            float y = mqoPos.y * settings.Scale;
            float z = mqoPos.z * settings.Scale;

            if (settings.FlipZ)
                z = -z;

            return new Vector3(x, y, z);
        }

        private static Vector2 ConvertUV(Vector2 mqoUV, MQOImportSettings settings)
        {
            // MQOのUVはそのまま使用（必要に応じてV反転）
            if (settings.FlipUV_V)
                return new Vector2(mqoUV.x, 1f - mqoUV.y);
            return mqoUV;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static int AddOrGetUVIndex(Vertex vertex, Vector2 uv)
        {
            // FPXと同じ比較方法: (uvl - uv).Length() == 0
            for (int i = 0; i < vertex.UVs.Count; i++)
            {
                if ((vertex.UVs[i] - uv).magnitude == 0f)
                    return i;
            }

            // 新規追加
            vertex.UVs.Add(uv);
            vertex.Normals.Add(Vector3.zero); // 後で計算
            return vertex.UVs.Count - 1;
        }

        private static void CalculateFaceNormal(Face face, MeshObject meshObject)
        {
            if (face.VertexCount < 3) return;

            // 最初の3頂点から法線計算
            Vector3 p0 = meshObject.Vertices[face.VertexIndices[0]].Position;
            Vector3 p1 = meshObject.Vertices[face.VertexIndices[1]].Position;
            Vector3 p2 = meshObject.Vertices[face.VertexIndices[2]].Position;

            Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0).normalized;

            // 各頂点の法線を更新
            for (int i = 0; i < face.VertexCount; i++)
            {
                int vertIndex = face.VertexIndices[i];
                int normalSubIndex = face.NormalIndices[i];

                var vertex = meshObject.Vertices[vertIndex];

                // 法線リストを確保
                while (vertex.Normals.Count <= normalSubIndex)
                    vertex.Normals.Add(Vector3.zero);

                // 法線を蓄積（後でスムージング可能）
                vertex.Normals[normalSubIndex] = normal;
            }
        }

        /// <summary>
        /// 頂点法線をスムージング
        /// 同一位置の頂点の法線を平均化（角度閾値付き）
        /// </summary>
        private static void CalculateSmoothNormals(MeshObject meshObject, float smoothingAngle)
        {
            float cosThreshold = Mathf.Cos(smoothingAngle * Mathf.Deg2Rad);

            // 各面の法線を計算して保持
            var faceNormals = new Vector3[meshObject.FaceCount];
            for (int fi = 0; fi < meshObject.FaceCount; fi++)
            {
                var face = meshObject.Faces[fi];
                if (face.VertexCount < 3)
                {
                    faceNormals[fi] = Vector3.up;
                    continue;
                }

                Vector3 p0 = meshObject.Vertices[face.VertexIndices[0]].Position;
                Vector3 p1 = meshObject.Vertices[face.VertexIndices[1]].Position;
                Vector3 p2 = meshObject.Vertices[face.VertexIndices[2]].Position;
                faceNormals[fi] = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            }

            // 位置→(面インデックス, 頂点インデックス in 面, NormalSubIndex) のマッピング
            var positionToFaceVerts = new Dictionary<Vector3, List<(int faceIdx, int vertInFace, int normalSubIdx)>>();

            for (int fi = 0; fi < meshObject.FaceCount; fi++)
            {
                var face = meshObject.Faces[fi];
                for (int vi = 0; vi < face.VertexCount; vi++)
                {
                    int vertIdx = face.VertexIndices[vi];
                    int normalSubIdx = face.NormalIndices[vi];
                    Vector3 pos = meshObject.Vertices[vertIdx].Position;

                    // 位置をキーにまとめる（微小誤差を許容するため丸める）
                    Vector3 roundedPos = new Vector3(
                        Mathf.Round(pos.x * 10000f) / 10000f,
                        Mathf.Round(pos.y * 10000f) / 10000f,
                        Mathf.Round(pos.z * 10000f) / 10000f
                    );

                    if (!positionToFaceVerts.ContainsKey(roundedPos))
                        positionToFaceVerts[roundedPos] = new List<(int, int, int)>();

                    positionToFaceVerts[roundedPos].Add((fi, vi, normalSubIdx));
                }
            }

            // 各位置で法線をスムージング
            foreach (var kvp in positionToFaceVerts)
            {
                var faceVerts = kvp.Value;
                if (faceVerts.Count <= 1) continue;

                // 各頂点について、角度閾値内の面法線を平均化
                foreach (var (faceIdx, vertInFace, normalSubIdx) in faceVerts)
                {
                    Vector3 baseFaceNormal = faceNormals[faceIdx];
                    Vector3 smoothedNormal = baseFaceNormal;

                    foreach (var (otherFaceIdx, _, _) in faceVerts)
                    {
                        if (otherFaceIdx == faceIdx) continue;

                        Vector3 otherFaceNormal = faceNormals[otherFaceIdx];
                        float dot = Vector3.Dot(baseFaceNormal, otherFaceNormal);

                        // 角度閾値内なら平均に加える
                        if (dot >= cosThreshold)
                        {
                            smoothedNormal += otherFaceNormal;
                        }
                    }

                    smoothedNormal = smoothedNormal.normalized;

                    // 頂点の法線を更新
                    int vertIdx = meshObject.Faces[faceIdx].VertexIndices[vertInFace];
                    var vertex = meshObject.Vertices[vertIdx];
                    if (normalSubIdx < vertex.Normals.Count)
                    {
                        vertex.Normals[normalSubIdx] = smoothedNormal;
                    }
                }
            }

            Debug.Log($"[MQOImporter] Smooth normals calculated (angle={smoothingAngle}°, positions={positionToFaceVerts.Count})");
        }

        // ================================================================
        // 頂点デバッグ出力
        // ================================================================

        /// <summary>
        /// 頂点デバッグ情報を出力（オブジェクトごとに1つのログでまとめて出力）
        /// MQOの元データから直接抽出
        /// </summary>
        /// <param name="objectName">オブジェクト名</param>
        /// <param name="mqoObj">MQOオブジェクト（元データ）</param>
        /// <param name="meshObject">変換後のメッシュオブジェクト</param>
        /// <param name="nearUVCount">近接UV出力件数</param>
        private static void OutputVertexDebugInfo(string objectName, MQOObject mqoObj, MeshObject meshObject, int nearUVCount)
        {
            int originalVertexCount = mqoObj.Vertices.Count;
            
            // 展開時の頂点数を計算（変換後のVertex.UVs.Countの合計）
            int expandedVertexCount = 0;
            foreach (var vertex in meshObject.Vertices)
            {
                expandedVertexCount += Math.Max(1, vertex.UVs.Count);
            }
            
            // MQOの面データから頂点ごとのUVを収集
            // Key: 頂点インデックス, Value: その頂点に割り当てられたUVのリスト
            var vertexUVs = new Dictionary<int, List<Vector2>>();
            
            foreach (var mqoFace in mqoObj.Faces)
            {
                if (mqoFace.IsSpecialFace) continue;
                if (mqoFace.UVs == null) continue;
                
                for (int i = 0; i < mqoFace.VertexCount && i < mqoFace.UVs.Length; i++)
                {
                    int vertIndex = mqoFace.VertexIndices[i];
                    Vector2 uv = mqoFace.UVs[i]; // MQOの元UV値（変換前）
                    
                    if (!vertexUVs.ContainsKey(vertIndex))
                    {
                        vertexUVs[vertIndex] = new List<Vector2>();
                    }
                    
                    // 同じUVが既にあるかチェック（完全一致）
                    bool found = false;
                    foreach (var existingUV in vertexUVs[vertIndex])
                    {
                        if (existingUV == uv)
                        {
                            found = true;
                            break;
                        }
                    }
                    if (!found)
                    {
                        vertexUVs[vertIndex].Add(uv);
                    }
                }
            }
            
            // 同一頂点で異なるUVを持つペアを収集
            var nearUVPairs = new List<(int vertIndex, int vertexId, Vector2 uv1, Vector2 uv2, float distance)>();
            
            foreach (var kvp in vertexUVs)
            {
                int vertIndex = kvp.Key;
                var uvList = kvp.Value;
                
                if (uvList.Count < 2) continue;
                
                // 頂点IDを取得（meshObjectから）
                int vertexId = (vertIndex < meshObject.Vertices.Count) ? meshObject.Vertices[vertIndex].Id : 0;
                
                // 全ペアの距離を計算
                for (int i = 0; i < uvList.Count; i++)
                {
                    for (int j = i + 1; j < uvList.Count; j++)
                    {
                        Vector2 uv1 = uvList[i];
                        Vector2 uv2 = uvList[j];
                        float dist = Vector2.Distance(uv1, uv2);
                        
                        nearUVPairs.Add((vertIndex, vertexId, uv1, uv2, dist));
                    }
                }
            }
            
            // 距離が近い順にソート
            nearUVPairs.Sort((a, b) => a.distance.CompareTo(b.distance));
            
            // 1つのログにまとめて出力（コピペしやすいように半角スペース区切り）
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[VertexDebug] {objectName}");
            sb.AppendLine($"OriginalVertexCount {originalVertexCount}");
            sb.AppendLine($"ExpandedVertexCount {expandedVertexCount}");
            sb.AppendLine($"NearUVPairCount {nearUVPairs.Count}");
            
            int outputCount = Math.Min(nearUVCount, nearUVPairs.Count);
            if (outputCount > 0)
            {
                sb.AppendLine("VertIndex VertexID U1 V1 U2 V2 Distance");
                for (int i = 0; i < outputCount; i++)
                {
                    var pair = nearUVPairs[i];
                    // MQOと同じ5桁形式（0.12345）で出力して文字列検索可能に
                    string u1 = pair.uv1.x.ToString("0.00000");
                    string v1 = pair.uv1.y.ToString("0.00000");
                    string u2 = pair.uv2.x.ToString("0.00000");
                    string v2 = pair.uv2.y.ToString("0.00000");
                    sb.AppendLine($"{pair.vertIndex} {pair.vertexId} {u1} {v1} {u2} {v2} {pair.distance:F6}");
                }
            }
            
            Debug.Log(sb.ToString());
        }

        private static MeshContext MergeAllMeshContexts(List<MeshContext> meshContexts, string name)
        {
            // TODO: 複数MeshContextを1つに統合
            // 現時点では最初のものを返す
            if (meshContexts.Count == 0)
                return null;

            var merged = meshContexts[0];
            merged.Name = name;
            return merged;
        }

        // ================================================================
        // ベイクミラー生成
        // ================================================================

        /// <summary>
        /// ミラー属性を持つメッシュからベイクミラーメッシュを生成
        /// </summary>
        /// <param name="source">ソースメッシュコンテキスト</param>
        /// <param name="sourceIndex">ソースのインデックス</param>
        /// <param name="settings">インポート設定</param>
        /// <returns>ベイクミラーメッシュコンテキスト</returns>
        private static MeshContext CreateBakedMirrorMesh(MeshContext source, int sourceIndex, MQOImportSettings settings)
        {
            if (source == null || source.MeshObject == null || !source.IsMirrored)
                return null;

            var srcMeshObj = source.MeshObject;
            var axis = source.GetMirrorSymmetryAxis();

            // 新しいMeshObjectを作成
            var mirrorMeshObj = new MeshObject
            {
                Name = source.Name + "_BakedMirror"
            };

            // 頂点をミラー変換してコピー
            foreach (var srcVertex in srcMeshObj.Vertices)
            {
                var mirrorVertex = new Vertex
                {
                    Id = srcVertex.Id,
                    Position = MirrorPosition(srcVertex.Position, axis)
                };

                // UVをコピー
                foreach (var uv in srcVertex.UVs)
                {
                    mirrorVertex.UVs.Add(uv);
                }

                // 法線をミラー変換してコピー
                foreach (var normal in srcVertex.Normals)
                {
                    mirrorVertex.Normals.Add(MirrorNormal(normal, axis));
                }

                // ボーンウェイト: ミラー側があればミラー側、なければ実体側
                if (srcVertex.HasMirrorBoneWeight)
                {
                    mirrorVertex.BoneWeight = srcVertex.MirrorBoneWeight;
                }
                else if (srcVertex.HasBoneWeight)
                {
                    mirrorVertex.BoneWeight = srcVertex.BoneWeight;
                }

                mirrorMeshObj.Vertices.Add(mirrorVertex);
            }

            // 面をコピー（頂点順序を反転して法線方向を維持）
            foreach (var srcFace in srcMeshObj.Faces)
            {
                var mirrorFace = new Face
                {
                    MaterialIndex = srcFace.MaterialIndex + source.MirrorMaterialOffset,
                };
                if (srcFace.IsHidden)
                    mirrorFace.SetFlag(FaceFlags.Hidden);

                // 頂点順序を反転（法線方向維持のため）
                for (int i = srcFace.VertexCount - 1; i >= 0; i--)
                {
                    mirrorFace.VertexIndices.Add(srcFace.VertexIndices[i]);
                    mirrorFace.UVIndices.Add(srcFace.UVIndices[i]);
                    mirrorFace.NormalIndices.Add(srcFace.NormalIndices[i]);
                }

                mirrorMeshObj.Faces.Add(mirrorFace);
            }

            // MeshContextを作成
            var mirrorContext = new MeshContext
            {
                MeshObject = mirrorMeshObj,
                Name = mirrorMeshObj.Name,
                Type = MeshType.BakedMirror,
                BakedMirrorSourceIndex = sourceIndex,
                // 階層情報は元メッシュに合わせる
                ParentIndex = source.ParentIndex,
                Depth = source.Depth,
                IsVisible = source.IsVisible,
                // ミラー属性はなし（実体化されているため）
                MirrorType = 0,
                MirrorAxis = 1,
                MirrorDistance = 0,
                MirrorMaterialOffset = 0
            };

            // UnityMesh生成
            mirrorContext.UnityMesh = mirrorMeshObj.ToUnityMesh();
            mirrorContext.OriginalPositions = mirrorMeshObj.Vertices.Select(v => v.Position).ToArray();

            return mirrorContext;
        }

        /// <summary>
        /// 位置をミラー変換
        /// </summary>
        private static Vector3 MirrorPosition(Vector3 pos, Poly_Ling.Symmetry.SymmetryAxis axis)
        {
            switch (axis)
            {
                case Poly_Ling.Symmetry.SymmetryAxis.X: return new Vector3(-pos.x, pos.y, pos.z);
                case Poly_Ling.Symmetry.SymmetryAxis.Y: return new Vector3(pos.x, -pos.y, pos.z);
                case Poly_Ling.Symmetry.SymmetryAxis.Z: return new Vector3(pos.x, pos.y, -pos.z);
                default: return new Vector3(-pos.x, pos.y, pos.z);
            }
        }

        /// <summary>
        /// 法線をミラー変換
        /// </summary>
        private static Vector3 MirrorNormal(Vector3 normal, Poly_Ling.Symmetry.SymmetryAxis axis)
        {
            switch (axis)
            {
                case Poly_Ling.Symmetry.SymmetryAxis.X: return new Vector3(-normal.x, normal.y, normal.z);
                case Poly_Ling.Symmetry.SymmetryAxis.Y: return new Vector3(normal.x, -normal.y, normal.z);
                case Poly_Ling.Symmetry.SymmetryAxis.Z: return new Vector3(normal.x, normal.y, -normal.z);
                default: return new Vector3(-normal.x, normal.y, normal.z);
            }
        }
    }
}