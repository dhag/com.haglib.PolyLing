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
using Poly_Ling.Context;

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

                if (settings.OutputFaceMeta)
                {
                    var meshOnlyContexts = model.MeshContextList?
                        .Where(ctx => ctx != null && ctx.Type != MeshType.Bone)
                        .ToList();
                    if (meshOnlyContexts != null)
                        PMXFaceMetaWriter.Save(meshOnlyContexts, outputPath);
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

            // 剛体・JOINT を MeshObject データから再構築（段階③）。
            // RigidBodyData/JointData を持つコンテキストが1つでも存在すれば
            // そちらを正として再構築し、無ければ従来通り SourceDocument から
            // 剛体・JOINTをパススルーする（後方互換）。
            var rigidBodyContexts = meshContexts
                .Where(ctx => ctx != null && ctx.Type == MeshType.RigidBody && ctx.MeshObject?.RigidBodyData != null)
                .ToList();
            var jointContexts = meshContexts
                .Where(ctx => ctx != null && ctx.Type == MeshType.RigidBodyJoint && ctx.MeshObject?.JointData != null)
                .ToList();

            bool hasPhysicsData = rigidBodyContexts.Count > 0 || jointContexts.Count > 0;

            if (hasPhysicsData)
            {
                // 剛体を先に出力し、剛体名→PMX剛体index のマップを得る。
                var rigidBodyNameToIndex = ConvertRigidBodies(rigidBodyContexts, document, boneNameToIndex, settings);
                // JOINTは剛体index解決のため上記マップを使用。
                ConvertJoints(jointContexts, document, rigidBodyNameToIndex, settings);
            }
            else if (model.SourceDocument is PMXDocument fallbackPmx)
            {
                // データ非保持時のみ従来パススルー。
                foreach (var body in fallbackPmx.RigidBodies)
                    document.RigidBodies.Add(body);
                foreach (var joint in fallbackPmx.Joints)
                    document.Joints.Add(joint);
            }

            // 表示枠・ソフトボディは現状 MeshObject 未対応のため SourceDocument からパススルー。
            if (model.SourceDocument is PMXDocument sourcePmx)
            {
                // 表示枠
                foreach (var frame in sourcePmx.DisplayFrames)
                    document.DisplayFrames.Add(frame);

                // ソフトボディ
                foreach (var softBody in sourcePmx.SoftBodies)
                    document.SoftBodies.Add(softBody);
            }

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

                // IK設定
                if (ctx.IsIK && ctx.IKLinks != null && ctx.IKLinks.Count > 0)
                {
                    pmxBone.Flags |= 0x0020;  // FLAG_IK

                    // IKターゲット
                    if (ctx.IKTargetIndex >= 0 && ctx.IKTargetIndex < boneContexts.Count)
                    {
                        pmxBone.IKTargetBoneName = boneContexts[ctx.IKTargetIndex].Name;
                        pmxBone.IKTargetIndex = ctx.IKTargetIndex;
                    }

                    pmxBone.IKLoopCount = ctx.IKLoopCount;
                    pmxBone.IKLimitAngle = ctx.IKLimitAngle;

                    // IKリンク
                    foreach (var link in ctx.IKLinks)
                    {
                        var pmxLink = new PMXIKLink
                        {
                            BoneIndex = link.BoneIndex,
                            HasLimit = link.HasLimit,
                            LimitMin = link.LimitMin,
                            LimitMax = link.LimitMax
                        };
                        if (link.BoneIndex >= 0 && link.BoneIndex < boneContexts.Count)
                        {
                            pmxLink.BoneName = boneContexts[link.BoneIndex].Name;
                        }
                        pmxBone.IKLinks.Add(pmxLink);
                    }
                }

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
                    string assetPath = Poly_Ling.EditorBridge.PLEditorBridge.I.GetAssetPath(mainTex);
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
                if (ctx?.MeshObject == null) continue;
                // 空メッシュかつミラーはスキップ（ミラー材質を作らない）
                if (ctx.MeshObject.VertexCount == 0 && (ctx.IsBakedMirror || ctx.Type == MeshType.MirrorSide)) continue;
                if (ctx.Type == MeshType.Morph) continue;  // モーフは除外
                if (ctx.Type == MeshType.RigidBody || ctx.Type == MeshType.RigidBodyJoint) continue;  // 剛体/JOINTはメッシュ変換対象外（頂点ゼロのメタデータ専用）
                if (ctx.ExcludeFromExport) continue;       // エクスポート除外

                string objectName = ctx.Name ?? "Unnamed";
                
                // BakedMirror/MirrorSideはミラー側として扱う
                bool isMirror = ctx.IsBakedMirror || ctx.Type == MeshType.MirrorSide;
                // BakedMirrorのみ"+"除去して実体側と同一グループに統合
                if (ctx.IsBakedMirror && objectName.EndsWith("+"))
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
                    bool isMirror = ctx.IsBakedMirror || ctx.Type == MeshType.MirrorSide;
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

            // UV展開しながら頂点をdocumentに追加
            var vertexMapping = AppendExpandedVertices(meshObject, document, boneNameToIndex, settings);

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

                    // 三角形に分割（fan triangulation）
                    for (int i = 0; i < face.VertexIndices.Count - 2; i++)
                    {
                        int vi0 = face.VertexIndices[0];
                        int vi1 = face.VertexIndices[i + 1];
                        int vi2 = face.VertexIndices[i + 2];
                        int uv0 = face.UVIndices.Count > 0 ? face.UVIndices[0] : 0;
                        int uv1 = face.UVIndices.Count > i + 1 ? face.UVIndices[i + 1] : 0;
                        int uv2 = face.UVIndices.Count > i + 2 ? face.UVIndices[i + 2] : 0;

                        if (!vertexMapping.TryGetValue((vi0, uv0), out int v0)) continue;
                        if (!vertexMapping.TryGetValue((vi1, uv1), out int v1)) continue;
                        if (!vertexMapping.TryGetValue((vi2, uv2), out int v2)) continue;

                        var pmxFace = new PMXFace
                        {
                            MaterialName = materialName,
                            MaterialIndex = matIndex,
                            FaceIndex = document.Faces.Count,
                            VertexIndex1 = v0,
                            VertexIndex2 = settings.FlipZ ? v2 : v1,
                            VertexIndex3 = settings.FlipZ ? v1 : v2
                        };

                        document.Faces.Add(pmxFace);
                    }
                }

                // 材質のMemo欄にObjectNameを設定
                SetMaterialObjectName(document, matIndex, objectName, isMirror, ctx.Depth);
            }

            // 空メッシュの場合: facesByMaterialが空のためMemoが設定されない → PMXMaterialNamesから設定
            if (meshObject.Faces.Count == 0 && ctx.PMXMaterialNames != null)
            {
                foreach (var matName in ctx.PMXMaterialNames)
                {
                    int matIdx = document.GetMaterialIndex(matName);
                    if (matIdx >= 0)
                        SetMaterialObjectName(document, matIdx, objectName, isMirror, ctx.Depth);
                }
            }

            // PolyLingメタUVモーフを生成（頂点ID・UVサブインデックス保存用）
            BuildPolyLingMetaMorph(ctx, document, vertexMapping, objectName);
        }

        /// <summary>
        /// MeshObject を (vIdx, uvIdx) で UV展開しながら PMX 頂点を document に追加する。
        /// 戻り値: (vIdx, uvIdx) → document上のPMX頂点インデックス
        /// </summary>
        private static Dictionary<(int vIdx, int uvIdx), int> AppendExpandedVertices(
            MeshObject meshObject,
            PMXDocument document,
            Dictionary<string, int> boneNameToIndex,
            PMXExportSettings settings)
        {
            int meshVertexStart = document.Vertices.Count;
            var localMap = meshObject.BuildExpansionMap();

            // ローカルインデックスをdocumentグローバルインデックスにオフセット
            var vertexMapping = new Dictionary<(int vIdx, int uvIdx), int>(localMap.Count);
            foreach (var kv in localMap)
                vertexMapping[kv.Key] = kv.Value + meshVertexStart;

            // 孤立頂点除外
            var nonIsolated = new HashSet<int>();
            foreach (var face in meshObject.Faces)
            {
                if (face.VertexCount < 3) continue;
                foreach (int vi in face.VertexIndices) nonIsolated.Add(vi);
            }

            // 展開順（vIdx→uvIdx）に頂点を追加（孤立頂点はスキップ）
            for (int vIdx = 0; vIdx < meshObject.Vertices.Count; vIdx++)
            {
                if (!nonIsolated.Contains(vIdx)) continue;
                var vertex = meshObject.Vertices[vIdx];
                int uvCount = vertex.UVs.Count > 0 ? vertex.UVs.Count : 1;

                for (int uvIdx = 0; uvIdx < uvCount; uvIdx++)
                {
                    var pmxVertex = ConvertVertex(vertex, boneNameToIndex, settings);
                    Vector2 uv = uvIdx < vertex.UVs.Count ? vertex.UVs[uvIdx] : Vector2.zero;
                    if (settings.FlipUV_V) uv.y = 1f - uv.y;
                    pmxVertex.UV = uv;
                    pmxVertex.Index = document.Vertices.Count;
                    document.Vertices.Add(pmxVertex);
                }
            }

            return vertexMapping;
        }

        /// <summary>
        /// 材質のMemo欄にObjectNameを設定
        /// </summary>
        // PolyLingメタモーフのプレフィックス
        private const string PolyLingMetaMorphPrefix = "__PLM_";

        /// <summary>
        /// PolyLingメタUVモーフを生成。
        /// 頂点ID・UVサブインデックスをPMXのUVモーフに記録する。
        /// Offset = (-1, uvSubIndex, localVertexIndex, Vertex.Id)
        /// </summary>
        private static void BuildPolyLingMetaMorph(
            MeshContext ctx,
            PMXDocument document,
            Dictionary<(int vIdx, int uvIdx), int> vertexMapping,
            string objectName)
        {
            var meshObject = ctx.MeshObject;
            if (meshObject == null || meshObject.VertexCount == 0) return;

            // 頂点インデックス → UVサブインデックス（最初に見つかった面コーナーの値）
            var vertexUVSubIndex = new Dictionary<int, int>();
            foreach (var face in meshObject.Faces)
            {
                for (int ci = 0; ci < face.VertexIndices.Count && ci < face.UVIndices.Count; ci++)
                {
                    int vi = face.VertexIndices[ci];
                    if (!vertexUVSubIndex.ContainsKey(vi))
                        vertexUVSubIndex[vi] = face.UVIndices[ci];
                }
            }

            var morph = new PMXMorph
            {
                Name = PolyLingMetaMorphPrefix + objectName,
                NameEnglish = PolyLingMetaMorphPrefix + objectName,
                Panel = 0,
                MorphType = 3  // UVモーフ
            };

            for (int localIndex = 0; localIndex < meshObject.VertexCount; localIndex++)
            {
                var vertex = meshObject.Vertices[localIndex];
                int uvSubIndex = vertexUVSubIndex.TryGetValue(localIndex, out int s) ? s : 0;

                if (!vertexMapping.TryGetValue((localIndex, uvSubIndex), out int pmxIdx)) continue;

                morph.Offsets.Add(new PMXUVMorphOffset
                {
                    VertexIndex = pmxIdx,
                    Offset = new Vector4(-1f, uvSubIndex, localIndex, vertex.Id)
                });
            }

            document.Morphs.Add(morph);
        }

        private static void SetMaterialObjectName(
            PMXDocument document,
            int materialIndex,
            string objectName,
            bool isMirror,
            int depth = -1)
        {
            if (materialIndex < 0 || materialIndex >= document.Materials.Count)
                return;

            var mat = document.Materials[materialIndex];
            string newMemo = PMXHelper.BuildMaterialMemo(objectName, isMirror, depth);

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

        // ================================================================
        // 剛体・JOINT エクスポート（段階③）
        // ================================================================
        //
        // 【方針】
        //   RigidBodyData/JointData を持つ MeshObject から PMXRigidBody/PMXJoint を
        //   再構築する。インポート（段階②）の逆変換であり、座標規約は対称：
        //     位置 = ConvertPosition（×Scale ＋ FlipZ）… import の逆（Scaleは逆数）
        //     回転 = ConvertEulerRotation（FlipZ共役は自己逆元のため import と同一処理）
        //     サイズ = ×Scale（Z反転なし）
        //     質量・減衰・反発・摩擦・Group・Mask・JointType・min/max・Spring = 生値
        //
        // 【参照系：name主】
        //   剛体→ボーン：RelatedBoneName を boneNameToIndex（PMX出力ボーン順）で解決。
        //   JOINT→剛体A/B：BodyAName/BodyBName を「剛体名→PMX剛体index」マップで解決。
        //   PMXWriter のJOINT名補完は発火条件が index<-1 のため index 解決は必須
        //   （ここで正しい index を設定する）。
        // ----------------------------------------------------------------

        /// <summary>
        /// 剛体コンテキスト群を PMXRigidBody に変換して document に追加する。
        /// </summary>
        /// <returns>剛体名 → PMX剛体index のマップ（JOINTの剛体参照解決に使用）。</returns>
        private static Dictionary<string, int> ConvertRigidBodies(
            List<MeshContext> rigidBodyContexts,
            PMXDocument document,
            Dictionary<string, int> boneNameToIndex,
            PMXExportSettings settings)
        {
            var nameToIndex = new Dictionary<string, int>();

            foreach (var ctx in rigidBodyContexts)
            {
                var data = ctx.MeshObject.RigidBodyData;

                // 関連ボーンは name主で解決（PMX出力ボーンindexは boneNameToIndex に一致）
                int boneIndex = -1;
                if (!string.IsNullOrEmpty(data.RelatedBoneName) &&
                    boneNameToIndex.TryGetValue(data.RelatedBoneName, out int bi))
                {
                    boneIndex = bi;
                }

                var pmxBody = new PMXRigidBody
                {
                    Name            = ctx.Name,
                    NameEnglish     = string.IsNullOrEmpty(data.NameEnglish) ? ctx.Name : data.NameEnglish,
                    BoneIndex       = boneIndex,
                    RelatedBoneName = data.RelatedBoneName ?? "",
                    Group           = data.Group,
                    CollisionMask   = data.CollisionMask,
                    Shape           = (int)data.Shape,
                    Size            = data.Size * settings.Scale,            // ×Scale（範囲量のためZ反転しない）
                    Position        = ConvertPosition(data.Position, settings),
                    Rotation        = ConvertEulerRotation(data.Rotation, settings),
                    Mass            = data.Mass,
                    LinearDamping   = data.LinearDamping,
                    AngularDamping  = data.AngularDamping,
                    Restitution     = data.Restitution,
                    Friction        = data.Friction,
                    PhysicsMode     = (int)data.PhysicsMode
                };

                // 名前→index（同名は最初の出現を採用。Writerの名前検索と整合）
                if (!nameToIndex.ContainsKey(pmxBody.Name))
                    nameToIndex[pmxBody.Name] = document.RigidBodies.Count;

                document.RigidBodies.Add(pmxBody);
            }

            return nameToIndex;
        }

        /// <summary>
        /// JOINTコンテキスト群を PMXJoint に変換して document に追加する。
        /// 剛体A/Bは rigidBodyNameToIndex（剛体名→PMX剛体index）で解決する。
        /// </summary>
        private static void ConvertJoints(
            List<MeshContext> jointContexts,
            PMXDocument document,
            Dictionary<string, int> rigidBodyNameToIndex,
            PMXExportSettings settings)
        {
            foreach (var ctx in jointContexts)
            {
                var data = ctx.MeshObject.JointData;

                // 剛体A/BのPMX剛体index（未解決は-1。WriterはJOINTで名前補完しないため必須）
                int idxA = (!string.IsNullOrEmpty(data.BodyAName) &&
                            rigidBodyNameToIndex.TryGetValue(data.BodyAName, out int a)) ? a : -1;
                int idxB = (!string.IsNullOrEmpty(data.BodyBName) &&
                            rigidBodyNameToIndex.TryGetValue(data.BodyBName, out int b)) ? b : -1;

                var pmxJoint = new PMXJoint
                {
                    Name              = ctx.Name,
                    NameEnglish       = string.IsNullOrEmpty(data.NameEnglish) ? ctx.Name : data.NameEnglish,
                    JointType         = data.JointType,
                    RigidBodyIndexA   = idxA,
                    BodyAName         = data.BodyAName ?? "",
                    RigidBodyIndexB   = idxB,
                    BodyBName         = data.BodyBName ?? "",
                    Position          = ConvertPosition(data.Position, settings),
                    Rotation          = ConvertEulerRotation(data.Rotation, settings),
                    TranslationMin    = data.TranslationMin,
                    TranslationMax    = data.TranslationMax,
                    RotationMin       = data.RotationMin,
                    RotationMax       = data.RotationMax,
                    SpringTranslation = data.SpringTranslation,
                    SpringRotation    = data.SpringRotation
                };

                document.Joints.Add(pmxJoint);
            }
        }

        /// <summary>
        /// モデル空間のオイラー角回転（ラジアン）を PMX のオイラー角（ラジアン）へ変換する。
        /// FlipZ時は右手系⇔左手系のZ鏡映共役（クォータニオン (x,y,z,w)→(-x,-y,z,w)）を適用する。
        /// この共役は自己逆元のため、インポート側 ConvertEulerRotation と同一処理で逆変換になる。
        /// 入力/出力ともラジアン（Unity APIは度のため内部で度に変換して扱う）。
        /// </summary>
        private static Vector3 ConvertEulerRotation(Vector3 modelEulerRad, PMXExportSettings settings)
        {
            Quaternion q = Quaternion.Euler(modelEulerRad * Mathf.Rad2Deg);

            if (settings.FlipZ)
                q = new Quaternion(-q.x, -q.y, q.z, q.w);

            return q.eulerAngles * Mathf.Deg2Rad;
        }
    }
}
