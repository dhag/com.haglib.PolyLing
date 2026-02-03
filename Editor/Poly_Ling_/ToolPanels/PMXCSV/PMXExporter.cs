// Assets/Editor/Poly_Ling/PMX/Export/PMXExporter.cs
// MeshContext → PMXDocument変換 & ファイル出力


///ミラー対応は後回し中。VertexHelperを使うときに考慮する。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXエクスポート結果
    /// </summary>
    public class PMXExportResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string OutputPath { get; set; }

        // 統計情報
        public int VertexCount { get; set; }
        public int FaceCount { get; set; }
        public int MaterialCount { get; set; }
        public int BoneCount { get; set; }
        public int MorphCount { get; set; }
    }

    /// <summary>
    /// PMXエクスポーター
    /// </summary>
    public static class PMXExporter
    {
        // ================================================================
        // パブリックAPI
        // ================================================================

        /// <summary>
        /// ModelContextからPMXをエクスポート（フル出力）
        /// </summary>
        public static PMXExportResult Export(
            ModelContext model,
            string outputPath,
            PMXExportSettings settings = null)
        {
            var result = new PMXExportResult();
            settings = settings ?? PMXExportSettings.CreateFullExport();

            try
            {
                // PMXDocumentを構築
                var document = BuildPMXDocument(model, settings);

                // ファイル出力
                if (settings.OutputBinaryPMX)
                {
                    PMXWriter.Save(document, outputPath);
                }
                
                if (settings.OutputCSV)
                {
                    string csvPath = Path.ChangeExtension(outputPath, ".csv");
                    PMXCSVWriter.Save(document, csvPath, settings.DecimalPrecision);
                }

                result.Success = true;
                result.OutputPath = outputPath;
                result.VertexCount = document.Vertices.Count;
                result.FaceCount = document.Faces.Count;
                result.MaterialCount = document.Materials.Count;
                result.BoneCount = document.Bones.Count;
                result.MorphCount = document.Morphs.Count;

                Debug.Log($"[PMXExporter] Export successful: {result.VertexCount} vertices, {result.FaceCount} faces, {result.MaterialCount} materials, {result.BoneCount} bones");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[PMXExporter] Export failed: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// 部分差し替えエクスポート
        /// 元のPMXの指定材質の頂点データのみをMeshContextのデータで置き換える
        /// 頂点は材質順に連続して配置されていると仮定
        /// </summary>
        public static PMXExportResult ExportPartialReplace(
            ModelContext model,
            string sourcePMXPath,
            string outputPath,
            PMXExportSettings settings)
        {
            var result = new PMXExportResult();

            try
            {
                // 元のPMXを読み込み
                PMXDocument sourceDoc = PMXReader.Load(sourcePMXPath);

                // 差し替え対象の材質名を取得
                var replaceMaterialNames = new HashSet<string>(settings.ReplaceMaterialNames);
                if (replaceMaterialNames.Count == 0)
                {
                    throw new Exception("差し替え対象の材質が指定されていません");
                }

                // PMX側：材質ごとの頂点範囲を計算（材質順に連続配置を仮定）
                var pmxMaterialVertexRanges = CalculateMaterialVertexRanges(sourceDoc);

                // デバッグ出力
                Debug.Log($"[PMXExporter] PMX材質ごと頂点範囲:");
                foreach (var kvp in pmxMaterialVertexRanges)
                {
                    Debug.Log($"  {kvp.Key}: [{kvp.Value.startIndex}..{kvp.Value.startIndex + kvp.Value.count - 1}] ({kvp.Value.count}頂点)");
                }

                // MeshContextから差し替え用の頂点データを収集（材質名でフィルタ）
                var replaceData = CollectReplaceVertexDataByMaterial(model, replaceMaterialNames, settings);

                // 頂点数チェック
                foreach (var matName in replaceMaterialNames)
                {
                    if (!pmxMaterialVertexRanges.TryGetValue(matName, out var range))
                    {
                        throw new Exception($"材質 '{matName}' がPMXに存在しません");
                    }

                    int sourceCount = range.count;
                    int replaceCount = replaceData.TryGetValue(matName, out var verts) ? verts.Count : 0;

                    Debug.Log($"[PMXExporter] 材質 '{matName}': PMX={sourceCount}頂点, MeshContext={replaceCount}頂点");

                    if (sourceCount != replaceCount)
                    {
                        throw new Exception($"材質 '{matName}' の頂点数が一致しません (PMX: {sourceCount}, MeshContext: {replaceCount})");
                    }
                }

                // 頂点データを差し替え（材質の頂点範囲に順番に適用）
                ApplyVertexReplacementByRange(sourceDoc, pmxMaterialVertexRanges, replaceData, settings);

                // ファイル出力
                if (settings.OutputBinaryPMX)
                {
                    PMXWriter.Save(sourceDoc, outputPath);
                }
                
                if (settings.OutputCSV)
                {
                    string csvPath = Path.ChangeExtension(outputPath, ".csv");
                    PMXCSVWriter.Save(sourceDoc, csvPath, settings.DecimalPrecision);
                }

                result.Success = true;
                result.OutputPath = outputPath;
                result.VertexCount = sourceDoc.Vertices.Count;
                result.FaceCount = sourceDoc.Faces.Count;
                result.MaterialCount = sourceDoc.Materials.Count;
                result.BoneCount = sourceDoc.Bones.Count;

                Debug.Log($"[PMXExporter] Partial replace successful: replaced {replaceMaterialNames.Count} materials");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[PMXExporter] Partial replace failed: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// PMXの材質ごとの頂点範囲を計算
        /// 材質の面が使用する頂点は連続して配置されていると仮定
        /// </summary>
        private static Dictionary<string, (int startIndex, int count)> CalculateMaterialVertexRanges(PMXDocument document)
        {
            var result = new Dictionary<string, (int startIndex, int count)>();

            foreach (var mat in document.Materials)
            {
                int minIndex = int.MaxValue;
                int maxIndex = int.MinValue;

                foreach (var face in document.Faces)
                {
                    if (face.MaterialName != mat.Name) continue;

                    minIndex = Math.Min(minIndex, face.VertexIndex1);
                    minIndex = Math.Min(minIndex, face.VertexIndex2);
                    minIndex = Math.Min(minIndex, face.VertexIndex3);

                    maxIndex = Math.Max(maxIndex, face.VertexIndex1);
                    maxIndex = Math.Max(maxIndex, face.VertexIndex2);
                    maxIndex = Math.Max(maxIndex, face.VertexIndex3);
                }

                if (minIndex != int.MaxValue && maxIndex != int.MinValue)
                {
                    result[mat.Name] = (minIndex, maxIndex - minIndex + 1);
                }
            }

            return result;
        }

        /// <summary>
        /// MeshContextから材質ごとの頂点データを収集
        /// </summary>
        private static Dictionary<string, List<VertexReplaceData>> CollectReplaceVertexDataByMaterial(
            ModelContext model,
            HashSet<string> targetMaterialNames,
            PMXExportSettings settings)
        {
            var result = new Dictionary<string, List<VertexReplaceData>>();

            // 材質名から材質インデックスへのマッピング
            var matNameToIndex = new Dictionary<string, int>();
            for (int i = 0; i < model.Materials.Count; i++)
            {
                var mat = model.Materials[i];
                if (mat != null && !string.IsNullOrEmpty(mat.name))
                {
                    matNameToIndex[mat.name] = i;
                }
            }

            // 対象材質のインデックス一覧
            var targetMatIndices = new HashSet<int>();
            foreach (var matName in targetMaterialNames)
            {
                if (matNameToIndex.TryGetValue(matName, out int idx))
                {
                    targetMatIndices.Add(idx);
                }
            }

            // メッシュごとに処理
            foreach (var ctx in model.MeshContextList)
            {
                if (ctx?.MeshObject == null) continue;
                if (ctx.Type == MeshType.Bone) continue;

                // このメッシュで使用されている材質インデックスを取得
                var meshMatIndices = new HashSet<int>();
                foreach (var face in ctx.MeshObject.Faces)
                {
                    if (face.MaterialIndex >= 0)
                        meshMatIndices.Add(face.MaterialIndex);
                }

                // 対象材質がこのメッシュに含まれているか
                foreach (int matIdx in meshMatIndices)
                {
                    if (!targetMatIndices.Contains(matIdx)) continue;

                    string matName = model.Materials[matIdx]?.name ?? "";
                    if (string.IsNullOrEmpty(matName)) continue;

                    if (!result.ContainsKey(matName))
                        result[matName] = new List<VertexReplaceData>();

                    // この材質を使用する頂点を順番に収集
                    // 頂点リストを順番に走査し、その材質に属する頂点を追加
                    var vertexIndicesForMat = new HashSet<int>();
                    foreach (var face in ctx.MeshObject.Faces)
                    {
                        if (face.MaterialIndex != matIdx) continue;
                        foreach (int vIdx in face.VertexIndices)
                        {
                            vertexIndicesForMat.Add(vIdx);
                        }
                    }

                    // 頂点インデックス順にソートして追加
                    var sortedIndices = vertexIndicesForMat.OrderBy(x => x).ToList();
                    foreach (int vIdx in sortedIndices)
                    {
                        var vertex = ctx.MeshObject.Vertices[vIdx];
                        var data = new VertexReplaceData
                        {
                            Position = ConvertPosition(vertex.Position, settings),
                            Normal = vertex.Normals.Count > 0
                                ? ConvertNormal(vertex.Normals[0], settings)
                                : Vector3.up,
                            UV = vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero
                        };

                        if (settings.FlipUV_V)
                            data.UV.y = 1f - data.UV.y;

                        result[matName].Add(data);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 材質の頂点範囲に対して順番に頂点データを差し替え
        /// </summary>
        private static void ApplyVertexReplacementByRange(
            PMXDocument document,
            Dictionary<string, (int startIndex, int count)> materialRanges,
            Dictionary<string, List<VertexReplaceData>> replaceData,
            PMXExportSettings settings)
        {
            foreach (var matName in replaceData.Keys)
            {
                if (!materialRanges.TryGetValue(matName, out var range))
                {
                    Debug.LogWarning($"[PMXExporter] 材質 '{matName}' の範囲が見つかりません");
                    continue;
                }

                var data = replaceData[matName];
                int replaceCount = Math.Min(range.count, data.Count);

                Debug.Log($"[PMXExporter] 差し替え: {matName} [{range.startIndex}..{range.startIndex + replaceCount - 1}]");

                for (int i = 0; i < replaceCount; i++)
                {
                    int vIdx = range.startIndex + i;
                    var vertex = document.Vertices[vIdx];
                    var newData = data[i];

                    if (settings.ReplacePositions)
                        vertex.Position = newData.Position;
                    if (settings.ReplaceNormals)
                        vertex.Normal = newData.Normal;
                    if (settings.ReplaceUVs)
                        vertex.UV = newData.UV;
                }

                Debug.Log($"[PMXExporter] Replaced {replaceCount} vertices for material '{matName}'");
            }
        }

        // ================================================================
        // PMXDocument構築（フル出力用）
        // ================================================================

        private static PMXDocument BuildPMXDocument(ModelContext model, PMXExportSettings settings)
        {
            var document = new PMXDocument
            {
                Version = 2.1f,
                CharacterEncoding = 0,  // UTF-16
                ModelInfo = new PMXModelInfo
                {
                    Name = model.Name ?? "Exported Model",
                    NameEnglish = model.Name ?? "Exported Model",
                    Comment = "Exported from SimpleMeshFactory",
                    CommentEnglish = "Exported from SimpleMeshFactory"
                }
            };

            var meshContexts = model.MeshContextList;
            if (meshContexts == null || meshContexts.Count == 0)
            {
                throw new Exception("エクスポートするメッシュがありません");
            }

            // ボーンとメッシュを分離
            var boneContexts = meshContexts.Where(ctx => ctx?.Type == MeshType.Bone).ToList();
            var meshOnlyContexts = meshContexts.Where(ctx => ctx != null && ctx.Type != MeshType.Bone).ToList();

            // ボーン名→インデックスマップ
            var boneNameToIndex = new Dictionary<string, int>();
            for (int i = 0; i < boneContexts.Count; i++)
            {
                boneNameToIndex[boneContexts[i].Name] = i;
            }

            // ボーンを変換
            if (settings.ExportBones)
            {
                ConvertBones(boneContexts, document, settings);
            }

            // マテリアルを変換
            if (settings.ExportMaterials)
            {
                ConvertMaterials(model.Materials, document, settings);
            }

            // メッシュを変換（頂点・面）
            ConvertMeshes(meshOnlyContexts, document, boneNameToIndex, settings);

            return document;
        }

        // ================================================================
        // ボーン変換
        // ================================================================

        private static void ConvertBones(
            List<MeshContext> boneContexts,
            PMXDocument document,
            PMXExportSettings settings)
        {
            // ボーン名→インデックスマップ
            var boneNameToIndex = new Dictionary<string, int>();
            for (int i = 0; i < boneContexts.Count; i++)
            {
                boneNameToIndex[boneContexts[i].Name] = i;
            }

            foreach (var ctx in boneContexts)
            {
                // ワールド位置を計算（LocalMatrixの累積）
                Vector3 worldPosition = ComputeBoneWorldPosition(ctx, boneContexts, boneNameToIndex);

                // 座標変換
                Vector3 pmxPosition = ConvertPosition(worldPosition, settings);

                // 親ボーン名を取得
                string parentName = "";
                if (ctx.HierarchyParentIndex >= 0 && ctx.HierarchyParentIndex < boneContexts.Count)
                {
                    parentName = boneContexts[ctx.HierarchyParentIndex].Name;
                }

                var pmxBone = new PMXBone
                {
                    Name = ctx.Name,
                    NameEnglish = ctx.Name,
                    Position = pmxPosition,
                    ParentBoneName = parentName,
                    TransformLevel = 0,
                    Flags = 0x0001 | 0x0002 | 0x0004 | 0x0008,  // 基本フラグ
                    ConnectOffset = Vector3.zero
                };

                document.Bones.Add(pmxBone);
            }

            Debug.Log($"[PMXExporter] Converted {document.Bones.Count} bones");
        }

        private static Vector3 ComputeBoneWorldPosition(
            MeshContext ctx,
            List<MeshContext> boneContexts,
            Dictionary<string, int> boneNameToIndex)
        {
            // 累積位置を計算
            Vector3 worldPos = Vector3.zero;
            var current = ctx;

            while (current != null)
            {
                if (current.BoneTransform != null)
                {
                    worldPos += current.BoneTransform.Position;
                }

                int parentIdx = current.HierarchyParentIndex;
                if (parentIdx >= 0 && parentIdx < boneContexts.Count)
                {
                    current = boneContexts[parentIdx];
                }
                else
                {
                    break;
                }
            }

            return worldPos;
        }

        // ================================================================
        // マテリアル変換
        // ================================================================

        private static void ConvertMaterials(
            List<Material> materials,
            PMXDocument document,
            PMXExportSettings settings)
        {
            if (materials == null) return;

            foreach (var mat in materials)
            {
                if (mat == null) continue;

                Color diffuse = Color.white;
                string texturePath = "";

                // BaseColorまたはColorを取得
                if (mat.HasProperty("_BaseColor"))
                    diffuse = mat.GetColor("_BaseColor");
                else if (mat.HasProperty("_Color"))
                    diffuse = mat.GetColor("_Color");

                // テクスチャパスを取得
                Texture mainTex = null;
                if (mat.HasProperty("_BaseMap"))
                    mainTex = mat.GetTexture("_BaseMap");
                else if (mat.HasProperty("_MainTex"))
                    mainTex = mat.GetTexture("_MainTex");

                if (mainTex != null)
                {
                    string assetPath = UnityEditor.AssetDatabase.GetAssetPath(mainTex);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        texturePath = settings.UseRelativeTexturePath
                            ? Path.GetFileName(assetPath)
                            : assetPath;
                    }
                }

                var pmxMat = new PMXMaterial
                {
                    Name = mat.name,
                    NameEnglish = mat.name,
                    Diffuse = diffuse,
                    Specular = Color.white,
                    SpecularPower = 5f,
                    Ambient = new Color(0.5f, 0.5f, 0.5f),
                    TexturePath = texturePath,
                    EdgeColor = Color.black,
                    EdgeSize = 1f
                };

                document.Materials.Add(pmxMat);
            }

            Debug.Log($"[PMXExporter] Converted {document.Materials.Count} materials");
        }

        // ================================================================
        // メッシュ変換
        // ================================================================

        private static void ConvertMeshes(
            List<MeshContext> meshContexts,
            PMXDocument document,
            Dictionary<string, int> boneNameToIndex,
            PMXExportSettings settings)
        {
            // メッシュをObjectName別にグループ化
            // MeshContext.Name をObjectNameとして使用
            var objectGroups = new Dictionary<string, List<MeshContext>>();
            var groupOrder = new List<string>();

            foreach (var ctx in meshContexts)
            {
                if (ctx?.MeshObject == null || ctx.MeshObject.VertexCount == 0) continue;
                if (ctx.Type == MeshType.Morph) continue;  // モーフは除外
                if (ctx.ExcludeFromExport) continue;       // エクスポート除外

                string objectName = ctx.Name ?? "Unnamed";
                
                // BakedMirrorの場合、元のオブジェクト名を使用（"+"接尾辞を除去）
                bool isMirror = ctx.IsBakedMirror;
                if (isMirror && objectName.EndsWith("+"))
                {
                    objectName = objectName.TrimEnd('+');
                }

                if (!objectGroups.ContainsKey(objectName))
                {
                    objectGroups[objectName] = new List<MeshContext>();
                    groupOrder.Add(objectName);
                }
                objectGroups[objectName].Add(ctx);
            }

            // 実体側を先に、次にミラー側を出力（仕様通りの順序）
            var realMeshes = new List<(MeshContext ctx, string objectName, bool isMirror)>();
            var mirrorMeshes = new List<(MeshContext ctx, string objectName, bool isMirror)>();

            foreach (var objectName in groupOrder)
            {
                foreach (var ctx in objectGroups[objectName])
                {
                    bool isMirror = ctx.IsBakedMirror;
                    if (isMirror)
                        mirrorMeshes.Add((ctx, objectName, true));
                    else
                        realMeshes.Add((ctx, objectName, false));
                }
            }

            // 実体側を出力
            foreach (var (ctx, objectName, isMirror) in realMeshes)
            {
                ConvertSingleMeshWithObjectName(ctx, document, boneNameToIndex, settings, objectName, isMirror);
            }

            // ミラー側を出力
            foreach (var (ctx, objectName, isMirror) in mirrorMeshes)
            {
                ConvertSingleMeshWithObjectName(ctx, document, boneNameToIndex, settings, objectName, isMirror);
            }

            // 材質の面数を更新
            UpdateMaterialFaceCounts(document);

            Debug.Log($"[PMXExporter] Converted {document.Vertices.Count} vertices, {document.Faces.Count} faces");
        }

        /// <summary>
        /// 単一MeshContextをPMX形式に変換（ObjectName対応）
        /// </summary>
        private static void ConvertSingleMeshWithObjectName(
            MeshContext ctx,
            PMXDocument document,
            Dictionary<string, int> boneNameToIndex,
            PMXExportSettings settings,
            string objectName,
            bool isMirror)
        {
            var meshObject = ctx.MeshObject;
            int meshVertexStart = document.Vertices.Count;

            // 頂点を変換（順序保持）
            foreach (var vertex in meshObject.Vertices)
            {
                var pmxVertex = ConvertVertex(vertex, boneNameToIndex, settings);
                pmxVertex.Index = document.Vertices.Count;
                document.Vertices.Add(pmxVertex);
            }

            // 面を材質ごとにグループ化（同一材質内の面順序は保持）
            var facesByMaterial = new Dictionary<int, List<Face>>();
            foreach (var face in meshObject.Faces)
            {
                if (!facesByMaterial.ContainsKey(face.MaterialIndex))
                    facesByMaterial[face.MaterialIndex] = new List<Face>();
                facesByMaterial[face.MaterialIndex].Add(face);
            }

            // 材質インデックス順に面を追加
            foreach (var matIndex in facesByMaterial.Keys.OrderBy(k => k))
            {
                var faces = facesByMaterial[matIndex];
                string materialName = matIndex < document.Materials.Count
                    ? document.Materials[matIndex].Name
                    : $"Material_{matIndex}";

                foreach (var face in faces)
                {
                    if (face.VertexIndices.Count < 3) continue;

                    // 三角形に分割
                    for (int i = 0; i < face.VertexIndices.Count - 2; i++)
                    {
                        int v0 = face.VertexIndices[0] + meshVertexStart;
                        int v1 = face.VertexIndices[i + 1] + meshVertexStart;
                        int v2 = face.VertexIndices[i + 2] + meshVertexStart;

                        var pmxFace = new PMXFace
                        {
                            MaterialName = materialName,
                            MaterialIndex = matIndex,
                            FaceIndex = document.Faces.Count,
                            VertexIndex1 = settings.FlipZ ? v0 : v0,
                            VertexIndex2 = settings.FlipZ ? v2 : v1,
                            VertexIndex3 = settings.FlipZ ? v1 : v2
                        };

                        document.Faces.Add(pmxFace);
                    }
                }

                // 材質のMemo欄にObjectNameを設定
                SetMaterialObjectName(document, matIndex, objectName, isMirror);
            }
        }

        /// <summary>
        /// 材質のMemo欄にObjectNameを設定
        /// </summary>
        private static void SetMaterialObjectName(
            PMXDocument document,
            int materialIndex,
            string objectName,
            bool isMirror)
        {
            if (materialIndex < 0 || materialIndex >= document.Materials.Count)
                return;

            var mat = document.Materials[materialIndex];
            string newMemo = PMXHelper.BuildMaterialMemo(objectName, isMirror);

            if (string.IsNullOrEmpty(newMemo))
                return;

            // 既存のMemoがある場合、ObjectName関連の既存データを削除してから追加
            if (!string.IsNullOrEmpty(mat.Memo))
            {
                // 既存のObjectName/IsMirrorを除去
                var existingParts = mat.Memo.Split(',')
                    .Select(p => p.Trim())
                    .ToList();

                var cleanedParts = new List<string>();
                for (int i = 0; i < existingParts.Count; i++)
                {
                    var part = existingParts[i];
                    if (part.Equals("ObjectName", StringComparison.OrdinalIgnoreCase))
                    {
                        // ObjectName,値 の値部分もスキップ
                        if (i + 1 < existingParts.Count)
                            i++;
                        continue;
                    }
                    if (part.Equals("IsMirror", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    cleanedParts.Add(part);
                }

                // クリーンアップ後の既存データと新しいデータを結合
                if (cleanedParts.Count > 0)
                    mat.Memo = string.Join(",", cleanedParts) + "," + newMemo;
                else
                    mat.Memo = newMemo;
            }
            else
            {
                mat.Memo = newMemo;
            }
        }

        private static PMXVertex ConvertVertex(
            Vertex vertex,
            Dictionary<string, int> boneNameToIndex,
            PMXExportSettings settings)
        {
            // 座標変換
            Vector3 position = ConvertPosition(vertex.Position, settings);
            Vector3 normal = vertex.Normals.Count > 0
                ? ConvertNormal(vertex.Normals[0], settings)
                : Vector3.up;
            Vector2 uv = vertex.UVs.Count > 0 ? vertex.UVs[0] : Vector2.zero;
            if (settings.FlipUV_V)
                uv.y = 1f - uv.y;

            var pmxVertex = new PMXVertex
            {
                Position = position,
                Normal = normal,
                UV = uv,
                EdgeScale = 1f,
                WeightType = 0  // BDEF1
            };

            // ボーンウェイト変換
            if (vertex.HasBoneWeight)
            {
                var bw = vertex.BoneWeight.Value;
                var weights = new List<PMXBoneWeight>();

                // ウェイトがある場合のみ追加
                if (bw.weight0 > 0)
                    weights.Add(new PMXBoneWeight { BoneName = GetBoneName(bw.boneIndex0, boneNameToIndex), Weight = bw.weight0 });
                if (bw.weight1 > 0)
                    weights.Add(new PMXBoneWeight { BoneName = GetBoneName(bw.boneIndex1, boneNameToIndex), Weight = bw.weight1 });
                if (bw.weight2 > 0)
                    weights.Add(new PMXBoneWeight { BoneName = GetBoneName(bw.boneIndex2, boneNameToIndex), Weight = bw.weight2 });
                if (bw.weight3 > 0)
                    weights.Add(new PMXBoneWeight { BoneName = GetBoneName(bw.boneIndex3, boneNameToIndex), Weight = bw.weight3 });

                pmxVertex.BoneWeights = weights.ToArray();
                pmxVertex.WeightType = weights.Count switch
                {
                    1 => 0,  // BDEF1
                    2 => 1,  // BDEF2
                    _ => 2   // BDEF4
                };
            }
            else
            {
                // デフォルト：最初のボーンに100%
                pmxVertex.BoneWeights = new[]
                {
                    new PMXBoneWeight { BoneName = boneNameToIndex.Keys.FirstOrDefault() ?? "", Weight = 1f }
                };
            }

            return pmxVertex;
        }

        private static string GetBoneName(int boneIndex, Dictionary<string, int> boneNameToIndex)
        {
            foreach (var kvp in boneNameToIndex)
            {
                if (kvp.Value == boneIndex)
                    return kvp.Key;
            }
            return boneNameToIndex.Keys.FirstOrDefault() ?? "";
        }

        private static void UpdateMaterialFaceCounts(PMXDocument document)
        {
            // 材質ごとの面数をカウント
            var materialFaceCounts = new Dictionary<string, int>();
            foreach (var face in document.Faces)
            {
                if (!materialFaceCounts.ContainsKey(face.MaterialName))
                    materialFaceCounts[face.MaterialName] = 0;
                materialFaceCounts[face.MaterialName]++;
            }

            // 材質に設定
            foreach (var mat in document.Materials)
            {
                mat.FaceCount = materialFaceCounts.TryGetValue(mat.Name, out int count) ? count : 0;
            }
        }

        private class VertexReplaceData
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 UV;
        }

        // ================================================================
        // 座標変換
        // ================================================================

        private static Vector3 ConvertPosition(Vector3 pos, PMXExportSettings settings)
        {
            float x = pos.x * settings.Scale;
            float y = pos.y * settings.Scale;
            float z = pos.z * settings.Scale;

            if (settings.FlipZ)
                z = -z;

            return new Vector3(x, y, z);
        }

        private static Vector3 ConvertNormal(Vector3 normal, PMXExportSettings settings)
        {
            if (settings.FlipZ)
                return new Vector3(normal.x, normal.y, -normal.z).normalized;
            return normal.normalized;
        }
    }
}
