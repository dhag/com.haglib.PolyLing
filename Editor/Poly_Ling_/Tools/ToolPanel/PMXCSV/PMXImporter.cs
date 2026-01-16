// Assets/Editor/Poly_Ling/PMX/Import/PMXImporter.cs
// PMXDocument → MeshObject/MeshContext 変換
// SimpleMeshFactoryのデータ構造に変換
// 頂点共有する材質をグループ化

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Tools;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXインポート結果
    /// </summary>
    public class PMXImportResult
    {
        /// <summary>成功したか</summary>
        public bool Success { get; set; }

        /// <summary>エラーメッセージ</summary>
        public string ErrorMessage { get; set; }

        /// <summary>インポートされたMeshContextリスト</summary>
        public List<MeshContext> MeshContexts { get; } = new List<MeshContext>();

        /// <summary>インポートされたマテリアルリスト</summary>
        public List<Material> Materials { get; } = new List<Material>();

        /// <summary>元のPMXドキュメント</summary>
        public PMXDocument Document { get; set; }

        /// <summary>インポート統計</summary>
        public PMXImportStats Stats { get; } = new PMXImportStats();

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

            Debug.Log($"[PMXImportResult] Applied material index offset: +{offset}");
        }
    }

    /// <summary>
    /// インポート統計情報
    /// </summary>
    public class PMXImportStats
    {
        public int MeshCount { get; set; }
        public int TotalVertices { get; set; }
        public int TotalFaces { get; set; }
        public int MaterialCount { get; set; }
        public int MaterialGroupCount { get; set; }
        public int BoneCount { get; set; }
        public int MorphCount { get; set; }
    }

    /// <summary>
    /// PMXインポーター
    /// </summary>
    public static class PMXImporter
    {
        // ================================================================
        // パブリックAPI
        // ================================================================

        /// <summary>
        /// ファイルからインポート
        /// </summary>
        public static PMXImportResult ImportFile(string filePath, PMXImportSettings settings = null)
        {
            var result = new PMXImportResult();
            settings = settings ?? new PMXImportSettings();

            try
            {
                // 拡張子で判定してパース
                PMXDocument document;
                string ext = Path.GetExtension(filePath).ToLower();
                if (ext == ".pmx")
                {
                    // バイナリPMX
                    document = PMXReader.Load(filePath);
                }
                else
                {
                    // CSV
                    document = PMXCSVParser.ParseFile(filePath);
                }
                result.Document = document;

                // 変換
                ConvertDocument(document, settings, result);

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[PMXImporter] Failed to import: {ex.Message}\n{ex.StackTrace}");
            }

            return result;
        }

        /// <summary>
        /// 文字列からインポート
        /// </summary>
        public static PMXImportResult ImportFromString(string content, PMXImportSettings settings = null)
        {
            var result = new PMXImportResult();
            settings = settings ?? new PMXImportSettings();

            try
            {
                var document = PMXCSVParser.Parse(content);
                result.Document = document;
                ConvertDocument(document, settings, result);
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[PMXImporter] Failed to import: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// PMXDocumentからインポート
        /// </summary>
        public static PMXImportResult Import(PMXDocument document, PMXImportSettings settings = null)
        {
            var result = new PMXImportResult();
            settings = settings ?? new PMXImportSettings();
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

        private static void ConvertDocument(PMXDocument document, PMXImportSettings settings, PMXImportResult result)
        {
            // 統計情報
            result.Stats.TotalVertices = document.Vertices.Count;
            result.Stats.TotalFaces = document.Faces.Count;
            result.Stats.MaterialCount = document.Materials.Count;
            result.Stats.BoneCount = document.Bones.Count;
            result.Stats.MorphCount = document.Morphs.Count;

            Debug.Log($"[PMXImporter] ImportTarget: {settings.ImportTarget}");

            // マテリアルをUnityマテリアルに変換（Mesh読み込み時のみ）
            if (settings.ShouldImportMesh && settings.ImportMaterials)
            {
                foreach (var pmxMat in document.Materials)
                {
                    var mat = ConvertMaterial(pmxMat, document, settings);
                    result.Materials.Add(mat);
                }
                Debug.Log($"[PMXImporter] Imported {result.Materials.Count} materials");
            }

            // ボーンをインポート（メッシュより先に追加）
            if (settings.ShouldImportBones && document.Bones.Count > 0)
            {
                ConvertBones(document, settings, result);
                Debug.Log($"[PMXImporter] Imported {document.Bones.Count} bones");
            }

            // ボーン数を記録（メッシュのインデックス計算用）
            int boneContextCount = result.MeshContexts.Count;

            // メッシュをインポート
            if (settings.ShouldImportMesh && document.Faces.Count > 0)
            {
                // 材質名から面リストへのマッピング
                var materialToFaces = new Dictionary<string, List<PMXFace>>();
                foreach (var face in document.Faces)
                {
                    if (!materialToFaces.ContainsKey(face.MaterialName))
                        materialToFaces[face.MaterialName] = new List<PMXFace>();
                    materialToFaces[face.MaterialName].Add(face);
                }

                // 材質名から使用頂点インデックスへのマッピング
                var materialToVertices = new Dictionary<string, HashSet<int>>();
                foreach (var kvp in materialToFaces)
                {
                    var vertexSet = new HashSet<int>();
                    foreach (var face in kvp.Value)
                    {
                        vertexSet.Add(face.VertexIndex1);
                        vertexSet.Add(face.VertexIndex2);
                        vertexSet.Add(face.VertexIndex3);
                    }
                    materialToVertices[kvp.Key] = vertexSet;
                }

                // 頂点共有による材質グループ化
                var materialGroups = GroupMaterialsBySharedVertices(materialToVertices);
                result.Stats.MaterialGroupCount = materialGroups.Count;

                Debug.Log($"[PMXImporter] {materialToFaces.Count} materials grouped into {materialGroups.Count} meshes");

                // デバッグ: 各グループの内容を出力
                for (int g = 0; g < materialGroups.Count && g < 5; g++)
                {
                    var groupMats = materialGroups[g];
                    Debug.Log($"[PMXImporter] Group[{g}] contains {groupMats.Count} materials: [{string.Join(", ", groupMats)}]");
                }

                // 各グループをMeshContextに変換
                int meshIndex = 0;
                foreach (var group in materialGroups)
                {
                    var meshContext = ConvertMaterialGroup(
                        document,
                        group,
                        materialToFaces,
                        result.Materials,
                        settings,
                        meshIndex
                    );

                    if (meshContext != null)
                    {
                        result.MeshContexts.Add(meshContext);
                        meshIndex++;
                    }
                }

                result.Stats.MeshCount = result.MeshContexts.Count - boneContextCount;
                Debug.Log($"[PMXImporter] Imported {result.Stats.MeshCount} mesh contexts");
            }

            // TODO: 剛体をインポート（将来実装）
            if (settings.ShouldImportBodies && document.RigidBodies.Count > 0)
            {
                Debug.Log($"[PMXImporter] Bodies import not yet implemented ({document.RigidBodies.Count} bodies)");
                // ConvertBodies(document, settings, result);
            }

            // TODO: ジョイントをインポート（将来実装）
            if (settings.ShouldImportJoints && document.Joints.Count > 0)
            {
                Debug.Log($"[PMXImporter] Joints import not yet implemented ({document.Joints.Count} joints)");
                // ConvertJoints(document, settings, result);
            }

            // TODO: モーフをインポート（将来実装）
            if (settings.ShouldImportMorphs && document.Morphs.Count > 0)
            {
                Debug.Log($"[PMXImporter] Morphs import not yet implemented ({document.Morphs.Count} morphs)");
                // ConvertMorphs(document, settings, result);
            }
        }

        // ================================================================
        // ボーン変換
        // ================================================================

        /// <summary>
        /// PMXボーンをMeshContext（Type=Bone）に変換
        /// </summary>
        private static void ConvertBones(PMXDocument document, PMXImportSettings settings, PMXImportResult result)
        {
            // ボーン名からインデックスへのマッピング（親子関係解決用）
            var boneNameToIndex = new Dictionary<string, int>();
            for (int i = 0; i < document.Bones.Count; i++)
            {
                boneNameToIndex[document.Bones[i].Name] = i;
            }

            // デバッグ: 主要ボーンのインデックスを出力
            string[] checkBones = { "頭", "首", "上半身", "下半身", "左腕", "右腕" };
            foreach (var boneName in checkBones)
            {
                int idx = document.GetBoneIndex(boneName);
                if (idx >= 0)
                    Debug.Log($"[PMXImporter] BoneIndex: '{boneName}' = {idx}");
            }

            // ボーンのワールド位置を変換済みで保持（ローカル座標計算用）
            var boneWorldPositions = new Vector3[document.Bones.Count];
            for (int i = 0; i < document.Bones.Count; i++)
            {
                boneWorldPositions[i] = ConvertPosition(document.Bones[i].Position, settings);
            }

            // 各ボーンをMeshContextに変換
            for (int i = 0; i < document.Bones.Count; i++)
            {
                var pmxBone = document.Bones[i];
                var meshContext = ConvertBone(
                    pmxBone,
                    i,
                    boneNameToIndex,
                    boneWorldPositions,
                    settings
                );
                result.MeshContexts.Add(meshContext);

                // デバッグ：親子関係を確認
                Debug.Log($"[PMXImporter] Bone[{i}] '{pmxBone.Name}' -> Parent='{pmxBone.ParentBoneName}' -> HierarchyParentIndex={meshContext.HierarchyParentIndex}");
            }

            Debug.Log($"[PMXImporter] Imported {document.Bones.Count} bones");
        }

        /// <summary>
        /// 単一のPMXボーンをMeshContextに変換
        /// </summary>
        private static MeshContext ConvertBone(
            PMXBone pmxBone,
            int boneIndex,
            Dictionary<string, int> boneNameToIndex,
            Vector3[] boneWorldPositions,
            PMXImportSettings settings)
        {
            // 親ボーンインデックスを解決
            int parentIndex = -1;
            if (!string.IsNullOrEmpty(pmxBone.ParentBoneName) &&
                boneNameToIndex.TryGetValue(pmxBone.ParentBoneName, out int pIdx))
            {
                parentIndex = pIdx;
            }

            // ワールド位置を取得
            Vector3 worldPosition = boneWorldPositions[boneIndex];

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

            // 空のMeshObjectを作成（ボーンは頂点/面を持たない）
            var meshObject = new MeshObject(pmxBone.Name)
            {
                HierarchyParentIndex = parentIndex
            };

            // BoneTransformを設定（ローカル座標）
            var boneTransform = new Poly_Ling.Tools.BoneTransform
            {
                Position = localPosition,
                Rotation = Vector3.zero,
                Scale = Vector3.one,
                UseLocalTransform = true,
                ExportAsSkinned = true  // ★スキンドメッシュとして出力
            };
            meshObject.BoneTransform = boneTransform;

            // MeshContext作成
            var meshContext = new MeshContext
            {
                MeshObject = meshObject,
                Type = MeshType.Bone,
                IsVisible = true
            };

            // BindPoseを設定（ワールド位置の逆変換）
            // BindPose = ボーンのワールド行列の逆行列
            // 回転・スケールなしの場合、これは単純な平行移動の逆
            meshContext.BindPose = Matrix4x4.Translate(-worldPosition);

            return meshContext;
        }

        /// <summary>
        /// 頂点を共有する材質をグループ化
        /// Union-Findアルゴリズムを使用
        /// </summary>
        private static List<List<string>> GroupMaterialsBySharedVertices(
            Dictionary<string, HashSet<int>> materialToVertices)
        {
            var materialNames = materialToVertices.Keys.ToList();
            int n = materialNames.Count;

            // Union-Find用の親配列
            int[] parent = new int[n];
            for (int i = 0; i < n; i++)
                parent[i] = i;

            // Find関数（経路圧縮付き）
            int Find(int x)
            {
                if (parent[x] != x)
                    parent[x] = Find(parent[x]);
                return parent[x];
            }

            // Union関数
            void Union(int x, int y)
            {
                int px = Find(x);
                int py = Find(y);
                if (px != py)
                    parent[px] = py;
            }

            // 頂点インデックスから材質インデックスへのマッピング
            var vertexToMaterials = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                foreach (int vIdx in materialToVertices[materialNames[i]])
                {
                    if (!vertexToMaterials.ContainsKey(vIdx))
                        vertexToMaterials[vIdx] = new List<int>();
                    vertexToMaterials[vIdx].Add(i);
                }
            }

            // 同じ頂点を使用する材質をUnion
            foreach (var kvp in vertexToMaterials)
            {
                var matIndices = kvp.Value;
                for (int i = 1; i < matIndices.Count; i++)
                {
                    Union(matIndices[0], matIndices[i]);
                }
            }

            // グループを収集
            var groups = new Dictionary<int, List<string>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!groups.ContainsKey(root))
                    groups[root] = new List<string>();
                groups[root].Add(materialNames[i]);
            }

            return groups.Values.ToList();
        }

        /// <summary>
        /// 材質グループをMeshContextに変換
        /// </summary>
        private static MeshContext ConvertMaterialGroup(
            PMXDocument document,
            List<string> materialNames,
            Dictionary<string, List<PMXFace>> materialToFaces,
            List<Material> unityMaterials,
            PMXImportSettings settings,
            int meshIndex)
        {
            // グループ内の全面を収集
            var allFaces = new List<PMXFace>();
            foreach (var matName in materialNames)
            {
                if (materialToFaces.TryGetValue(matName, out var faces))
                    allFaces.AddRange(faces);
            }

            if (allFaces.Count == 0)
                return null;

            // 使用する頂点インデックスを収集
            var usedVertexIndices = new HashSet<int>();
            foreach (var face in allFaces)
            {
                usedVertexIndices.Add(face.VertexIndex1);
                usedVertexIndices.Add(face.VertexIndex2);
                usedVertexIndices.Add(face.VertexIndex3);
            }

            // 元のインデックスから新しいインデックスへのマッピング
            var oldToNewIndex = new Dictionary<int, int>();
            var sortedIndices = usedVertexIndices.OrderBy(x => x).ToList();
            for (int i = 0; i < sortedIndices.Count; i++)
            {
                oldToNewIndex[sortedIndices[i]] = i;
            }

            // 材質名からグローバルインデックスへのマッピング（モデル全体での位置）
            var materialNameToGlobalIndex = new Dictionary<string, int>();
            for (int i = 0; i < materialNames.Count; i++)
            {
                int globalIndex = document.GetMaterialIndex(materialNames[i]);
                materialNameToGlobalIndex[materialNames[i]] = globalIndex >= 0 ? globalIndex : 0;
            }

            // MeshObjectを作成
            string meshName = materialNames.Count == 1
                ? materialNames[0]
                : $"Group_{meshIndex}_{materialNames[0]}";

            var meshObject = new MeshObject(meshName);

            // 頂点を追加（スケールなしで追加し、法線計算後にスケール適用）
            int debugCount = 0;
            int multiWeightDebugCount = 0;
            foreach (int oldIdx in sortedIndices)
            {
                var pmxVert = document.Vertices[oldIdx];
                // スケールなしで頂点を作成（法線計算の精度確保のため）
                var vertex = ConvertVertexUnscaled(pmxVert, document, settings);
                meshObject.Vertices.Add(vertex);

                // デバッグ: BoneWeight情報を出力
                if (vertex.BoneWeight.HasValue)
                {
                    var bw = vertex.BoneWeight.Value;

                    // 最初の5頂点
                    if (meshIndex == 0 && debugCount < 5)
                    {
                        Debug.Log($"[PMXImporter] Vertex[{oldIdx}] BoneWeight: " +
                                  $"({bw.boneIndex0}:{bw.weight0:F2}, {bw.boneIndex1}:{bw.weight1:F2}, " +
                                  $"{bw.boneIndex2}:{bw.weight2:F2}, {bw.boneIndex3}:{bw.weight3:F2})");
                        debugCount++;
                    }

                    // 複数ウェイトを持つ頂点（最初の3つ）
                    if (meshIndex == 0 && bw.weight1 > 0 && multiWeightDebugCount < 3)
                    {
                        Debug.Log($"[PMXImporter] MultiWeight Vertex[{oldIdx}]: " +
                                  $"({bw.boneIndex0}:{bw.weight0:F2}, {bw.boneIndex1}:{bw.weight1:F2}, " +
                                  $"{bw.boneIndex2}:{bw.weight2:F2}, {bw.boneIndex3}:{bw.weight3:F2})");
                        multiWeightDebugCount++;
                    }
                }
            }

            // 面を追加
            foreach (var pmxFace in allFaces)
            {
                int newV1 = oldToNewIndex[pmxFace.VertexIndex1];
                int newV2 = oldToNewIndex[pmxFace.VertexIndex2];
                int newV3 = oldToNewIndex[pmxFace.VertexIndex3];

                // 材質インデックスを取得（グローバルインデックス）
                int materialIndex = materialNameToGlobalIndex.TryGetValue(pmxFace.MaterialName, out int idx)
                    ? idx
                    : 0;

                var face = new Face
                {
                    MaterialIndex = materialIndex
                };

                // Z反転の場合は頂点順序を逆にする（法線反転）
                if (settings.FlipZ)
                {
                    face.VertexIndices.Add(newV1);
                    face.VertexIndices.Add(newV3);
                    face.VertexIndices.Add(newV2);
                }
                else
                {
                    face.VertexIndices.Add(newV1);
                    face.VertexIndices.Add(newV2);
                    face.VertexIndices.Add(newV3);
                }

                // UVインデックス（頂点と同じ）
                for (int i = 0; i < 3; i++)
                {
                    face.UVIndices.Add(0);
                    face.NormalIndices.Add(0);
                }

                meshObject.Faces.Add(face);
            }

            // 法線を再計算（スケール適用前の座標で計算 → 精度問題回避）
            if (settings.RecalculateNormals)
            {
                meshObject.RecalculateSmoothNormals();
            }

            // スケールを適用（法線計算後）
            if (Mathf.Abs(settings.Scale - 1f) > 0.0001f)
            {
                foreach (var vertex in meshObject.Vertices)
                {
                    vertex.Position *= settings.Scale;
                }
            }

            // デバッグ: 最初のメッシュの法線を確認
            if (meshIndex == 0 && meshObject.Vertices.Count > 0)
            {
                int checkCount = Mathf.Min(5, meshObject.Vertices.Count);
                for (int vi = 0; vi < checkCount; vi++)
                {
                    var v = meshObject.Vertices[vi];
                    if (v.Normals.Count > 0)
                    {
                        var n = v.Normals[0];
                        Debug.Log($"[PMXImporter] Normal[{vi}]: ({n.x:F3}, {n.y:F3}, {n.z:F3})");
                    }
                }
            }

            // MeshContext作成
            var meshContext = new MeshContext
            {
                Name = meshName,
                MeshObject = meshObject
            };

            // Unity Mesh生成
            // Face.MaterialIndexはグローバルインデックスなので、使用されている最大インデックス+1をサブメッシュ数とする
            int maxMatIndex = materialNameToGlobalIndex.Values.Max();
            int subMeshCount = maxMatIndex + 1;
            meshContext.UnityMesh = meshObject.ToUnityMesh(subMeshCount);

            // デバッグ: UnityMeshの各サブメッシュの三角形数を確認
            if (meshIndex < 3)
            {
                var unityMesh = meshContext.UnityMesh;
                Debug.Log($"[PMXImporter] Mesh '{meshName}' SubMeshCount={unityMesh.subMeshCount}, VertexCount={unityMesh.vertexCount}");
                for (int sm = 0; sm < Mathf.Min(unityMesh.subMeshCount, 10); sm++)
                {
                    int triCount = unityMesh.GetTriangles(sm).Length / 3;
                    if (triCount > 0)
                        Debug.Log($"[PMXImporter]   SubMesh[{sm}]: {triCount} triangles, Mat='{document.Materials[sm].Name}'");
                }
            }

            // マテリアル配列を設定（MaterialOwner設定前のフォールバック用、全マテリアル）
            // ※SimpleMeshFactory側でMaterialOwnerが設定されると、_model.Materialsが使用される
            meshContext.Materials = new List<Material>(unityMaterials);

            Debug.Log($"[PMXImporter] Created mesh '{meshName}': V={meshObject.VertexCount}, F={meshObject.FaceCount}, " +
                      $"LocalMat={materialNames.Count}, GlobalMatCount={document.Materials.Count}, " +
                      $"MatIndices=[{string.Join(",", materialNameToGlobalIndex.Values)}]");

            return meshContext;
        }

        // ================================================================
        // 頂点変換
        // ================================================================

        /// <summary>
        /// 頂点変換（スケールなし - 法線計算用）
        /// </summary>
        private static Vertex ConvertVertexUnscaled(PMXVertex pmxVert, PMXDocument document, PMXImportSettings settings)
        {
            // 座標変換（スケールなし）
            Vector3 pos = ConvertPositionUnscaled(pmxVert.Position, settings);
            Vector3 normal = ConvertNormal(pmxVert.Normal, settings);
            Vector2 uv = ConvertUV(pmxVert.UV, settings);

            var vertex = new Vertex(pos, uv, normal);

            // BoneWeight設定
            if (pmxVert.BoneWeights != null && pmxVert.BoneWeights.Length > 0)
            {
                var boneWeight = new BoneWeight();

                for (int i = 0; i < pmxVert.BoneWeights.Length && i < 4; i++)
                {
                    var pmxBw = pmxVert.BoneWeights[i];
                    int boneIndex = document.GetBoneIndex(pmxBw.BoneName);
                    if (boneIndex < 0) boneIndex = 0;

                    switch (i)
                    {
                        case 0:
                            boneWeight.boneIndex0 = boneIndex;
                            boneWeight.weight0 = pmxBw.Weight;
                            break;
                        case 1:
                            boneWeight.boneIndex1 = boneIndex;
                            boneWeight.weight1 = pmxBw.Weight;
                            break;
                        case 2:
                            boneWeight.boneIndex2 = boneIndex;
                            boneWeight.weight2 = pmxBw.Weight;
                            break;
                        case 3:
                            boneWeight.boneIndex3 = boneIndex;
                            boneWeight.weight3 = pmxBw.Weight;
                            break;
                    }
                }

                vertex.BoneWeight = boneWeight;
            }

            return vertex;
        }

        /// <summary>
        /// 頂点変換（スケール適用あり）
        /// </summary>
        private static Vertex ConvertVertex(PMXVertex pmxVert, PMXDocument document, PMXImportSettings settings)
        {
            // 座標変換
            Vector3 pos = ConvertPosition(pmxVert.Position, settings);
            Vector3 normal = ConvertNormal(pmxVert.Normal, settings);
            Vector2 uv = ConvertUV(pmxVert.UV, settings);

            var vertex = new Vertex(pos, uv, normal);

            // BoneWeight設定
            if (pmxVert.BoneWeights != null && pmxVert.BoneWeights.Length > 0)
            {
                var boneWeight = new BoneWeight();

                for (int i = 0; i < pmxVert.BoneWeights.Length && i < 4; i++)
                {
                    var pmxBw = pmxVert.BoneWeights[i];
                    int boneIndex = document.GetBoneIndex(pmxBw.BoneName);
                    if (boneIndex < 0) boneIndex = 0;

                    switch (i)
                    {
                        case 0:
                            boneWeight.boneIndex0 = boneIndex;
                            boneWeight.weight0 = pmxBw.Weight;
                            break;
                        case 1:
                            boneWeight.boneIndex1 = boneIndex;
                            boneWeight.weight1 = pmxBw.Weight;
                            break;
                        case 2:
                            boneWeight.boneIndex2 = boneIndex;
                            boneWeight.weight2 = pmxBw.Weight;
                            break;
                        case 3:
                            boneWeight.boneIndex3 = boneIndex;
                            boneWeight.weight3 = pmxBw.Weight;
                            break;
                    }
                }

                vertex.BoneWeight = boneWeight;
            }

            return vertex;
        }

        // ================================================================
        // マテリアル変換
        // ================================================================

        private static Material ConvertMaterial(PMXMaterial pmxMat, PMXDocument document, PMXImportSettings settings)
        {
            Shader shader = FindBestShader();
            var material = new Material(shader);
            material.name = pmxMat.Name;

            // 拡散色を設定（アルファ値も含む）
            Color color = pmxMat.Diffuse;
            SetMaterialColor(material, color);

            // 非透過度（Diffuse.a）が1未満の場合は透過マテリアルに設定
            if (color.a < 1f - 0.001f)
            {
                SetMaterialTransparent(material);
                Debug.Log($"[PMXImporter] Material '{pmxMat.Name}' set to Transparent (alpha={color.a:F2})");
            }

            // その他のプロパティ
            if (material.HasProperty("_Smoothness"))
            {
                // PMXのSpecularPowerを0-1に正規化
                float smoothness = Mathf.Clamp01(pmxMat.SpecularPower / 100f);
                material.SetFloat("_Smoothness", smoothness);
            }

            // テクスチャを設定
            string baseDir = GetBaseDirectory(document.FilePath);

            // メインテクスチャ（BaseMap）
            if (!string.IsNullOrEmpty(pmxMat.TexturePath))
            {
                var texture = LoadTexture(pmxMat.TexturePath, baseDir);
                if (texture != null)
                {
                    SetMaterialTexture(material, "_BaseMap", "_MainTex", texture);
                    Debug.Log($"[PMXImporter] Loaded texture: {pmxMat.TexturePath}");
                }
            }

            // スフィアテクスチャ（使用する場合）
            // TODO: スフィアマップ対応（必要に応じて）

            return material;
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
        /// CSVファイルのディレクトリを取得
        /// </summary>
        private static string GetBaseDirectory(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "";

            return Path.GetDirectoryName(filePath);
        }

        /// <summary>
        /// テクスチャを読み込み
        /// </summary>
        private static Texture2D LoadTexture(string texturePath, string baseDir)
        {
            if (string.IsNullOrEmpty(texturePath))
                return null;

            // パス区切り文字を正規化（\ → /）
            string normalizedPath = texturePath.Replace('\\', '/');
            string normalizedBaseDir = baseDir?.Replace('\\', '/') ?? "";

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
                    fullPath = Path.Combine(normalizedBaseDir, normalizedPath).Replace('\\', '/');
                }
                else
                {
                    fullPath = normalizedPath;
                }
            }

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
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
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

                string[] guids = AssetDatabase.FindAssets($"t:Texture2D {fileNameWithoutExt}", 
                    new[] { searchFolder });
                foreach (var guid in guids)
                {
                    string foundPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileName(foundPath).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        texture = AssetDatabase.LoadAssetAtPath<Texture2D>(foundPath);
                        if (texture != null)
                        {
                            Debug.Log($"[PMXImporter] Texture found in baseDir: {foundPath}");
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
                        Debug.Log($"[PMXImporter] Texture loaded from file: {fullPath}");
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                        texture = null;
                        Debug.LogWarning($"[PMXImporter] Failed to load image data: {fullPath}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[PMXImporter] Failed to read texture file: {fullPath} - {e.Message}");
                }
            }

            if (texture == null)
            {
                Debug.LogWarning($"[PMXImporter] Texture not found: {fullPath} (original: {texturePath})");
            }

            return texture;
        }

        /// <summary>
        /// マテリアルにテクスチャを設定
        /// </summary>
        private static void SetMaterialTexture(Material material, string urpPropertyName, string standardPropertyName, Texture texture)
        {
            if (material.HasProperty(urpPropertyName))
            {
                material.SetTexture(urpPropertyName, texture);
            }
            if (material.HasProperty(standardPropertyName))
            {
                material.SetTexture(standardPropertyName, texture);
            }
        }

        private static Shader FindBestShader()
        {
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

        /// <summary>
        /// 座標変換（スケール適用あり）
        /// </summary>
        private static Vector3 ConvertPosition(Vector3 pmxPos, PMXImportSettings settings)
        {
            float x = pmxPos.x * settings.Scale;
            float y = pmxPos.y * settings.Scale;
            float z = pmxPos.z * settings.Scale;

            if (settings.FlipZ)
                z = -z;

            return new Vector3(x, y, z);
        }

        /// <summary>
        /// 座標変換（スケールなし - 法線計算用）
        /// </summary>
        private static Vector3 ConvertPositionUnscaled(Vector3 pmxPos, PMXImportSettings settings)
        {
            float x = pmxPos.x;
            float y = pmxPos.y;
            float z = pmxPos.z;

            if (settings.FlipZ)
                z = -z;

            return new Vector3(x, y, z);
        }

        private static Vector3 ConvertNormal(Vector3 pmxNormal, PMXImportSettings settings)
        {
            float x = pmxNormal.x;
            float y = pmxNormal.y;
            float z = pmxNormal.z;

            if (settings.FlipZ)
                z = -z;

            return new Vector3(x, y, z).normalized;
        }

        private static Vector2 ConvertUV(Vector2 pmxUV, PMXImportSettings settings)
        {
            if (settings.FlipUV_V)
                return new Vector2(pmxUV.x, 1f - pmxUV.y);
            return pmxUV;
        }
    }
}