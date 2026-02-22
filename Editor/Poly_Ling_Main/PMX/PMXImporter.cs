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
using Poly_Ling.Materials;
using Poly_Ling.Core;

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

        /// <summary>
        /// 各ボーンのPMXワールド位置（インポート時の初期位置）
        /// CCDIKSolverのSetBonePositionsに渡す用
        /// </summary>
        public Vector3[] BoneWorldPositions { get; set; }

        /// <summary>インポートされたマテリアル参照リスト（正式形式）</summary>
        public List<MaterialReference> MaterialReferences { get; } = new List<MaterialReference>();

        /// <summary>
        /// インポートされたマテリアルリスト（MaterialReferencesから導出）
        /// </summary>
        public List<Material> Materials
        {
            get
            {
                var list = new List<Material>();
                foreach (var matRef in MaterialReferences)
                {
                    list.Add(matRef?.Material);
                }
                return list;
            }
        }

        /// <summary>マテリアル数</summary>
        public int MaterialCount => MaterialReferences.Count;

        /// <summary>元のPMXドキュメント</summary>
        public PMXDocument Document { get; set; }

        /// <summary>インポートされたモーフエクスプレッション</summary>
        public List<MorphExpression> MorphExpressions { get; } = new List<MorphExpression>();

        /// <summary>材質グループ情報（モーフ変換で使用）</summary>
        public List<MaterialGroupInfo> MaterialGroupInfos { get; } = new List<MaterialGroupInfo>();

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

        /// <summary>
        /// 全MeshContextの頂点のBoneWeightインデックスにオフセットを加算
        /// Appendモードで既存MeshContextがある場合に使用
        /// </summary>
        /// <param name="offset">加算するオフセット（既存MeshContext数）</param>
        public void ApplyBoneWeightIndexOffset(int offset)
        {
            if (offset <= 0) return;

            int convertedCount = 0;
            foreach (var meshContext in MeshContexts)
            {
                if (meshContext?.MeshObject == null) continue;

                // ボーンタイプの場合はBoneWeightを持たない
                if (meshContext.Type == MeshType.Bone) continue;

                foreach (var vertex in meshContext.MeshObject.Vertices)
                {
                    if (vertex.BoneWeight.HasValue)
                    {
                        var bw = vertex.BoneWeight.Value;
                        vertex.BoneWeight = new UnityEngine.BoneWeight
                        {
                            boneIndex0 = bw.boneIndex0 + offset,
                            boneIndex1 = bw.weight1 > 0 ? bw.boneIndex1 + offset : bw.boneIndex1,
                            boneIndex2 = bw.weight2 > 0 ? bw.boneIndex2 + offset : bw.boneIndex2,
                            boneIndex3 = bw.weight3 > 0 ? bw.boneIndex3 + offset : bw.boneIndex3,
                            weight0 = bw.weight0,
                            weight1 = bw.weight1,
                            weight2 = bw.weight2,
                            weight3 = bw.weight3
                        };
                        convertedCount++;
                    }
                }
            }

            Debug.Log($"[PMXImportResult] Applied bone weight index offset: +{offset} ({convertedCount} vertices)");
        }

        /// <summary>
        /// ボーンのHierarchyParentIndexにオフセットを加算
        /// Appendモードで既存MeshContextがある場合に使用
        /// </summary>
        /// <param name="offset">加算するオフセット（既存MeshContext数）</param>
        public void ApplyBoneHierarchyOffset(int offset)
        {
            if (offset <= 0) return;

            int convertedCount = 0;
            foreach (var meshContext in MeshContexts)
            {
                if (meshContext == null) continue;

                // ボーンの親インデックスにオフセットを適用
                if (meshContext.Type == MeshType.Bone && meshContext.HierarchyParentIndex >= 0)
                {
                    meshContext.HierarchyParentIndex += offset;
                    if (meshContext.MeshObject != null)
                    {
                        meshContext.MeshObject.HierarchyParentIndex = meshContext.HierarchyParentIndex;
                    }
                    convertedCount++;
                }
            }

            Debug.Log($"[PMXImportResult] Applied bone hierarchy offset: +{offset} ({convertedCount} bones)");
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
    /// 材質グループ情報（メッシュ/モーフ分割で共有）
    /// </summary>
    public class MaterialGroupInfo
    {
        /// <summary>グループに含まれる材質名リスト</summary>
        public List<string> MaterialNames { get; set; } = new List<string>();

        /// <summary>グループが使用するPMX頂点インデックス</summary>
        public HashSet<int> UsedVertexIndices { get; set; } = new HashSet<int>();

        /// <summary>PMX頂点インデックス → ローカル頂点インデックス</summary>
        public Dictionary<int, int> PmxToLocalIndex { get; set; } = new Dictionary<int, int>();

        /// <summary>result.MeshContexts 内のインデックス</summary>
        public int MeshContextIndex { get; set; } = -1;

        /// <summary>グループ名（メッシュ名）</summary>
        public string Name { get; set; }
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

            //Debug.Log($"[PMXImporter] ImportTarget: {settings.ImportTarget}");

            // マテリアルをUnityマテリアルに変換（Mesh読み込み時のみ）
            if (settings.ShouldImportMesh && settings.ImportMaterials)
            {
                foreach (var pmxMat in document.Materials)
                {
                    var mat = ConvertMaterial(pmxMat, document, settings);
                    // MaterialReferenceでラップして追加
                    result.MaterialReferences.Add(new MaterialReference(mat));
                }
                //Debug.Log($"[PMXImporter] Imported {result.MaterialCount} materials");
            }

            // ボーンをインポート（メッシュより先に追加）
            if (settings.ShouldImportBones && document.Bones.Count > 0)
            {
                ConvertBones(document, settings, result);
                //Debug.Log($"[PMXImporter] Imported {document.Bones.Count} bones");
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

                // PMX追加仕様：ObjectNameでグループ化
                var objectGroups = PMX.PMXHelper.BuildObjectGroups(document);

                // ObjectName未設定のグループ同士で共有頂点によるマージを適用
                objectGroups = MergeGroupsBySharedVertices(document, objectGroups, materialToVertices);

                result.Stats.MaterialGroupCount = objectGroups.Count;

                //Debug.Log($"[PMXImporter] {materialToFaces.Count} materials grouped into {objectGroups.Count} meshes by ObjectName");

                // デバッグ: 各グループの内容を出力
                for (int g = 0; g < objectGroups.Count && g < 5; g++)
                {
                    var grp = objectGroups[g];
                    var matNames = string.Join(", ", grp.Materials.ConvertAll(m => m.MaterialName));
                    //Debug.Log($"[PMXImporter] ObjectGroup[{g}] '{grp.ObjectName}' contains {grp.Materials.Count} materials: [{matNames}]");
                }

                // 各ObjectGroupをMeshContextに変換
                int meshIndex = 0;
                foreach (var objectGroup in objectGroups)
                {
                    // ObjectGroupから材質名リストを取得
                    var materialNames = objectGroup.Materials.ConvertAll(m => m.MaterialName);

                    // MaterialGroupInfo を構築
                    var groupInfo = BuildMaterialGroupInfo(materialNames, materialToFaces, meshIndex);
                    groupInfo.MeshContextIndex = boneContextCount + meshIndex;

                    var meshContext = ConvertMaterialGroup(
                        document,
                        materialNames,
                        materialToFaces,
                        result.Materials,
                        settings,
                        meshIndex
                    );

                    if (meshContext != null)
                    {
                        // ObjectNameをMeshContext名に設定
                        meshContext.Name = objectGroup.ObjectName;
                        groupInfo.Name = meshContext.Name;
                        result.MeshContexts.Add(meshContext);
                        result.MaterialGroupInfos.Add(groupInfo);
                        meshIndex++;
                    }
                }

                result.Stats.MeshCount = result.MeshContexts.Count - boneContextCount;
                //Debug.Log($"[PMXImporter] Imported {result.Stats.MeshCount} mesh contexts");
            }

            // Tポーズ変換（メッシュ作成後に実行）
            if (settings.ConvertToTPose && settings.ShouldImportBones && document.Bones.Count > 0)
            {
                // ボーン名からインデックスへのマッピングを再構築
                var boneNameToIndex = new Dictionary<string, int>();
                for (int i = 0; i < document.Bones.Count; i++)
                {
                    boneNameToIndex[document.Bones[i].Name] = i;
                }

                ConvertToTPose(result.MeshContexts, document, boneNameToIndex, settings);
                //Debug.Log($"[PMXImporter] Converted to T-Pose");
            }

            // TODO: 剛体をインポート（将来実装）
            if (settings.ShouldImportBodies && document.RigidBodies.Count > 0)
            {
                //Debug.Log($"[PMXImporter] Bodies import not yet implemented ({document.RigidBodies.Count} bodies)");
                // ConvertBodies(document, settings, result);
            }

            // TODO: ジョイントをインポート（将来実装）
            if (settings.ShouldImportJoints && document.Joints.Count > 0)
            {
                //Debug.Log($"[PMXImporter] Joints import not yet implemented ({document.Joints.Count} joints)");
                // ConvertJoints(document, settings, result);
            }

            // モーフをインポート
            if (settings.ShouldImportMorphs && document.Morphs.Count > 0)
            {
                ConvertMorphs(document, settings, result);
                //Debug.Log($"[PMXImporter] Imported {result.MorphExpressions.Count} morph sets");
            }

            // 名前末尾+のメッシュをBakedMirrorとして検出
            if (settings.DetectNamedMirror)
            {
                //DetectNamedMirrors(result.MeshContexts, boneContextCount);
            }
        }

        // ================================================================
        // 名前ミラー検出（Cタイプ: 名前末尾+）
        // ================================================================

        /// <summary>
        /// 名前末尾が+のメッシュをBakedMirrorとして設定
        /// +を除いた名前のメッシュをソースとし、BakedMirrorSourceIndexを設定
        /// </summary>
        public static void DetectNamedMirrors(List<MeshContext> meshContexts, int boneContextCount)
        {
            // メッシュ名→インデックスのマッピング（ボーン以降のメッシュのみ）
            var nameToIndex = new Dictionary<string, int>();
            for (int i = boneContextCount; i < meshContexts.Count; i++)
            {
                var ctx = meshContexts[i];
                if (ctx == null || ctx.Type != MeshType.Mesh) continue;
                if (!nameToIndex.ContainsKey(ctx.Name))
                    nameToIndex[ctx.Name] = i;
            }

            int mirrorCount = 0;
            for (int i = boneContextCount; i < meshContexts.Count; i++)
            {
                var ctx = meshContexts[i];
                if (ctx == null || ctx.Type != MeshType.Mesh) continue;
                if (!ctx.Name.EndsWith("+")) continue;

                string sourceName = ctx.Name.Substring(0, ctx.Name.Length - 1);
                if (nameToIndex.TryGetValue(sourceName, out int sourceIndex))
                {
                    ctx.MeshObject.Type = MeshType.BakedMirror;
                    ctx.Type = MeshType.BakedMirror;
                    ctx.BakedMirrorSourceIndex = sourceIndex;
                    meshContexts[sourceIndex].HasBakedMirrorChild = true;
                    meshContexts[sourceIndex].MirrorType = 1;  // 左右対称
                    meshContexts[sourceIndex].MirrorAxis = 1;  // X軸
                    mirrorCount++;
                    //Debug.Log($"[PMXImporter] Named mirror: '{ctx.Name}' → source '{sourceName}' (index {sourceIndex})");
                }
            }

            if (mirrorCount > 0)
            {
                //Debug.Log($"[PMXImporter] Detected {mirrorCount} named mirror meshes (+)");
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
                //if (idx >= 0)
                    //Debug.Log($"[PMXImporter] BoneIndex: '{boneName}' = {idx}");
            }

            // ボーンのワールド位置を変換済みで保持（ローカル座標計算用）
            var boneWorldPositions = new Vector3[document.Bones.Count];
            for (int i = 0; i < document.Bones.Count; i++)
            {
                boneWorldPositions[i] = ConvertPosition(document.Bones[i].Position, settings);
            }

            // ボーンのモデル空間回転を計算（ローカル軸から）
            var boneModelRotations = new Quaternion[document.Bones.Count];
            for (int i = 0; i < document.Bones.Count; i++)
            {
                boneModelRotations[i] = CalculateBoneModelRotation(document.Bones[i], document, i, settings);
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
                    boneModelRotations,
                    settings
                );
                result.MeshContexts.Add(meshContext);

                // デバッグ：親子関係と回転を確認
                bool hasLocalAxis = (pmxBone.Flags & 0x0800) != 0;
                //Debug.Log($"[PMXImporter] Bone[{i}] '{pmxBone.Name}' -> Parent='{pmxBone.ParentBoneName}' -> HierarchyParentIndex={meshContext.HierarchyParentIndex}, Flags=0x{pmxBone.Flags:X4}, HasLocalAxis={hasLocalAxis}");
            }

            //Debug.Log($"[PMXImporter] Imported {document.Bones.Count} bones");

            // CCDIKSolver用にPMXワールド位置を保存
            result.BoneWorldPositions = boneWorldPositions;
        }

        /// <summary>
        /// ボーンのモデル空間回転を計算（ローカル軸から）
        /// </summary>
        public static Quaternion CalculateBoneModelRotation(PMXBone pmxBone, PMXDocument document, int boneIndex, PMXImportSettings settings)
        {
            const int FLAG_LOCAL_AXIS = 0x0800;
            const int FLAG_IK = 0x0020;
            bool hasLocalAxis = (pmxBone.Flags & FLAG_LOCAL_AXIS) != 0;
            bool isIK = (pmxBone.Flags & FLAG_IK) != 0;

            // 全ボーンに対してローカル軸回転を計算する
            // ローカル軸フラグ(0x0800)の有無に関わらず、デフォルト軸を自動計算する
            //
            // 例外: IKボーンはidentityを返す
            // IKボーンは操作用ハンドルであり、親子関係は操作の便宜上のもの。
            // VMDの位置・回転はグローバル空間での値として扱う必要がある。
            // ローカル軸回転を設定するとVMDApplierのR^-1*Q*R変換で
            // グローバル空間の値がローカル軸空間に誤変換されてしまう。
            if (isIK)
            {
                return Quaternion.identity;
            }

            Vector3 localX, localZ;

            // デバッグ対象ボーン
            bool isDebugBone = pmxBone.Name.Contains("腕") || pmxBone.Name.Contains("ひじ") || pmxBone.Name.Contains("手首");

            if (hasLocalAxis)
            {
                // ローカル軸が定義されている場合
                localX = pmxBone.LocalAxisX;
                localZ = pmxBone.LocalAxisZ;

                if (isDebugBone)
                {
                    //Debug.Log($"[PMX AXIS DEBUG] {pmxBone.Name}: hasLocalAxis=true");
                    //Debug.Log($"[PMX AXIS DEBUG]   PMX LocalAxisX = {localX}");
                    //Debug.Log($"[PMX AXIS DEBUG]   PMX LocalAxisZ = {localZ}");
                }
            }
            else
            {
                // ローカル軸が定義されていない場合、デフォルト軸を計算
                localX = CalculateDefaultLocalAxisX(pmxBone, document, boneIndex);
                localZ = Vector3.forward; // PMX座標系でのZ+（前方向）

                // 自動計算結果をボーンに記録
                pmxBone.LocalAxisX = localX.normalized;
                pmxBone.LocalAxisZ = localZ;
                pmxBone.IsLocalAxisAutoCalculated = true;

                if (isDebugBone)
                {
                    //Debug.Log($"[PMX AXIS DEBUG] {pmxBone.Name}: hasLocalAxis=false, using default");
                    //Debug.Log($"[PMX AXIS DEBUG]   Calculated LocalAxisX = {localX}");
                    //Debug.Log($"[PMX AXIS DEBUG]   Default LocalAxisZ = {localZ}");
                }
            }

            // 正規化
            localX = localX.normalized;
            localZ = localZ.normalized;

            // 右手系(PMX)のままでY軸を計算: Y = Z × X
            Vector3 localY = Vector3.Cross(localZ, localX);

            // 数値誤差チェック
            if (localY.sqrMagnitude < 1e-10f)
            {
                // 軸が平行または退化している場合、デフォルトで復元
                //Debug.LogWarning($"[PMXImporter] Bone '{pmxBone.Name}' has degenerate local axis. Using default.");
                localY = Vector3.up;
                localZ = Vector3.Cross(localX, localY).normalized;
                localY = Vector3.Cross(localZ, localX).normalized;
            }
            else
            {
                localY = localY.normalized;
                // Zを直交化
                Vector3 originalZ = localZ;
                localZ = Vector3.Cross(localX, localY).normalized;

                if (isDebugBone)
                {
                    //Debug.Log($"[PMX AXIS DEBUG]   Computed Y = {localY}");
                    //Debug.Log($"[PMX AXIS DEBUG]   Original Z = {originalZ}, Recomputed Z = {localZ}");
                    float zDot = Vector3.Dot(originalZ, localZ);
                    //Debug.Log($"[PMX AXIS DEBUG]   Z dot product = {zDot:F4} (negative means flipped!)");
                }
            }

            // デバッグ: Z反転前のdet
            if (isDebugBone || pmxBone.Name.Contains("肩"))
            {
                float detBefore = Vector3.Dot(localX, Vector3.Cross(localY, localZ));
                //Debug.Log($"[PMX DET] {pmxBone.Name}: PMX(右手系) det={detBefore:F4}  X=({localX.x:F4},{localX.y:F4},{localX.z:F4}) Y=({localY.x:F4},{localY.y:F4},{localY.z:F4}) Z=({localZ.x:F4},{localZ.y:F4},{localZ.z:F4})");
            }

            // 座標系変換（右手系→左手系）: 共役変換 R_unity = S * R_rh * S, S=diag(1,1,-1)
            // X,Y軸: Z成分のみ反転 / Z軸: XY成分反転、Z成分そのまま
            if (settings.FlipZ)
            {
                localX = new Vector3(localX.x, localX.y, -localX.z);
                localY = new Vector3(localY.x, localY.y, -localY.z);
                localZ = new Vector3(-localZ.x, -localZ.y, localZ.z);
            }

            // デバッグ: Z反転後のdet
            if (isDebugBone || pmxBone.Name.Contains("肩"))
            {
                float detAfter = Vector3.Dot(localX, Vector3.Cross(localY, localZ));
                //Debug.Log($"[PMX DET] {pmxBone.Name}: Unity(左手系) det={detAfter:F4}  X=({localX.x:F4},{localX.y:F4},{localX.z:F4}) Y=({localY.x:F4},{localY.y:F4},{localY.z:F4}) Z=({localZ.x:F4},{localZ.y:F4},{localZ.z:F4})");
            }

            // 回転行列からQuaternionを生成
            return CreateRotationFromAxes(localX, localY, localZ);
        }

        /// <summary>
        /// ローカル軸が未定義の場合のデフォルトX軸を計算
        /// </summary>
        public static Vector3 CalculateDefaultLocalAxisX(PMXBone pmxBone, PMXDocument document, int boneIndex)
        {
            // 接続先がある場合、その方向をX軸とする
            bool connected = (pmxBone.Flags & 0x0001) != 0;

            if (connected && pmxBone.ConnectBoneIndex >= 0 && pmxBone.ConnectBoneIndex < document.Bones.Count)
            {
                // 接続先ボーンへの方向
                var connectBone = document.Bones[pmxBone.ConnectBoneIndex];
                Vector3 direction = connectBone.Position - pmxBone.Position;
                if (direction.sqrMagnitude > 1e-10f)
                {
                    return direction.normalized;
                }
            }
            else if (pmxBone.ConnectOffset.sqrMagnitude > 1e-10f)
            {
                // オフセットが定義されている場合
                return pmxBone.ConnectOffset.normalized;
            }

            // 子ボーンを探す
            for (int i = 0; i < document.Bones.Count; i++)
            {
                if (document.Bones[i].ParentIndex == boneIndex)
                {
                    Vector3 direction = document.Bones[i].Position - pmxBone.Position;
                    if (direction.sqrMagnitude > 1e-10f)
                    {
                        return direction.normalized;
                    }
                }
            }

            // どれも見つからない場合、PMX座標系のX+
            return Vector3.right;
        }

        /// <summary>
        /// 3つの軸からQuaternionを生成
        /// </summary>
        public static Quaternion CreateRotationFromAxes(Vector3 x, Vector3 y, Vector3 z)
        {
            var m = new Matrix4x4();
            m.SetColumn(0, new Vector4(x.x, x.y, x.z, 0f));
            m.SetColumn(1, new Vector4(y.x, y.y, y.z, 0f));
            m.SetColumn(2, new Vector4(z.x, z.y, z.z, 0f));
            m.SetColumn(3, new Vector4(0f, 0f, 0f, 1f));

            // 共役変換 R_unity = S * R_rh * S により det=+1 の正規直交行列が渡されるため
            // Inverseは不要。m.rotation がそのまま正しい回転を返す。
            // （旧実装では3軸全Z反転でdet=-1となり、Inverseで補正していたが誤りだった）
            return m.rotation;
        }

        // ================================================================
        // Tポーズ変換
        // ================================================================

        /// <summary>
        /// AポーズをTポーズに変換（PMXインポート時専用）
        /// GPU処理を使用してスキニング変換を適用
        /// </summary>
        private static void ConvertToTPose(
            List<MeshContext> meshContexts,
            PMXDocument document,
            Dictionary<string, int> boneNameToIndex,
            PMXImportSettings settings)
        {
            // 一時的なHumanoidBoneMappingを作成してボーン名から自動マッピング
            var tempMapping = new HumanoidBoneMapping();
            var boneNames = new List<string>();
            for (int i = 0; i < meshContexts.Count; i++)
                boneNames.Add(meshContexts[i]?.Name ?? "");
            tempMapping.AutoMapFromEmbeddedCSV(boneNames);

            TPoseConverter.ConvertToTPose(meshContexts, tempMapping);
        }

        /// <summary>
        /// AポーズをTポーズに変換（MeshContextのみ使用、PMXDocument不要）
        /// MQOImporter等から呼び出し可能
        /// </summary>
        public static void ConvertToTPoseFromMeshContexts(List<MeshContext> meshContexts)
        {
            TPoseConverter.ConvertToTPoseByBoneNames(meshContexts);
        }

        /// <summary>
        /// GPU処理を使用してスキンドメッシュの頂点座標をベイク
        /// </summary>
        public static void BakeSkinnedVertices(List<MeshContext> meshContexts)
        {
            TPoseConverter.BakeSkinnedVertices(meshContexts);
        }

        /// <summary>
        /// 全ボーンのワールド変換行列を計算
        /// </summary>
        public static Dictionary<int, Matrix4x4> CalculateWorldMatrices(List<MeshContext> meshContexts)
        {
            return ModelContext.CalculateWorldMatrices(meshContexts);
        }

        /// <summary>
        /// 単一のPMXボーンをMeshContextに変換
        /// </summary>
        private static MeshContext ConvertBone(
            PMXBone pmxBone,
            int boneIndex,
            Dictionary<string, int> boneNameToIndex,
            Vector3[] boneWorldPositions,
            Quaternion[] boneModelRotations,
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

            // モデル空間回転を取得
            Quaternion modelRotation = boneModelRotations[boneIndex];

            // ローカル位置・ローカル回転を計算
            // ローカル回転 = Inverse(親ワールド回転) * 自身ワールド回転
            // ローカル位置 = Inverse(親ワールド回転) * (自身ワールド位置 - 親ワールド位置)
            Vector3 localPosition;
            Quaternion localRotation;
            if (parentIndex >= 0)
            {
                Quaternion parentModelRotation = boneModelRotations[parentIndex];
                Vector3 parentWorldPos = boneWorldPositions[parentIndex];
                Quaternion invParentRot = Quaternion.Inverse(parentModelRotation);
                localPosition = invParentRot * (worldPosition - parentWorldPos);
                localRotation = invParentRot * modelRotation;
            }
            else
            {
                localPosition = worldPosition;
                localRotation = modelRotation;
            }

            // オイラー角に変換
            Vector3 localRotationEuler = localRotation.eulerAngles;

            // 空のMeshObjectを作成（ボーンは頂点/面を持たない）
            var meshObject = new MeshObject(pmxBone.Name)
            {
                Type = MeshType.Bone,
                HierarchyParentIndex = parentIndex
            };

            // BoneTransformを設定（ローカル座標・ローカル回転）
            var boneTransform = new Poly_Ling.Tools.BoneTransform
            {
                Position = localPosition,
                Rotation = localRotationEuler,
                Scale = Vector3.one,
                UseLocalTransform = true,
                ExportAsSkinned = true  // ★スキンドメッシュとして出力
            };
            meshObject.BoneTransform = boneTransform;

            // BindPoseを設定（ワールド位置+回転の逆行列）
            Matrix4x4 worldMatrix = Matrix4x4.TRS(worldPosition, modelRotation, Vector3.one);
            Matrix4x4 bindPose = worldMatrix.inverse;

            // MeshContext作成（★MQOと同様に全プロパティを設定）
            var meshContext = new MeshContext
            {
                MeshObject = meshObject,
                Name = pmxBone.Name,  // ★名前を設定
                Type = MeshType.Bone,
                IsVisible = true,
                BindPose = bindPose,
                BoneTransform = boneTransform,  // ★BoneTransformを設定
                BoneModelRotation = modelRotation  // ローカル軸のワールド空間回転（VMDApplierのR^-1*Q*R変換に使用）
            };

            // ★MeshContextにもHierarchyParentIndexを設定（重要！）
            meshContext.HierarchyParentIndex = parentIndex;

            // IKデータを設定
            const int FLAG_IK = 0x0020;
            if ((pmxBone.Flags & FLAG_IK) != 0 && pmxBone.IKLinks != null && pmxBone.IKLinks.Count > 0)
            {
                meshContext.IsIK = true;
                // IKターゲット（エフェクタ）のインデックス解決
                if (!string.IsNullOrEmpty(pmxBone.IKTargetBoneName) &&
                    boneNameToIndex.TryGetValue(pmxBone.IKTargetBoneName, out int ikTargetIdx))
                {
                    meshContext.IKTargetIndex = ikTargetIdx;
                }
                else if (pmxBone.IKTargetIndex >= 0)
                {
                    meshContext.IKTargetIndex = pmxBone.IKTargetIndex;
                }
                meshContext.IKLoopCount = pmxBone.IKLoopCount;
                meshContext.IKLimitAngle = pmxBone.IKLimitAngle;

                meshContext.IKLinks = new List<IKLinkInfo>();
                foreach (var link in pmxBone.IKLinks)
                {
                    int linkIdx = -1;
                    if (!string.IsNullOrEmpty(link.BoneName) &&
                        boneNameToIndex.TryGetValue(link.BoneName, out int nameIdx))
                    {
                        linkIdx = nameIdx;
                    }
                    else if (link.BoneIndex >= 0)
                    {
                        linkIdx = link.BoneIndex;
                    }

                    // 角度制限の座標系変換
                    // FlipZ=true時、position.z = -zでインポートするため、
                    // X軸回転とY軸回転の符号が反転する。
                    // Min/Maxを入れ替えて符号反転で対応。
                    Vector3 limMin = link.LimitMin;
                    Vector3 limMax = link.LimitMax;
                    if (settings.FlipZ)
                    {
                        limMin = new Vector3(-link.LimitMax.x, -link.LimitMax.y, link.LimitMin.z);
                        limMax = new Vector3(-link.LimitMin.x, -link.LimitMin.y, link.LimitMax.z);
                    }

                    meshContext.IKLinks.Add(new IKLinkInfo
                    {
                        BoneIndex = linkIdx,
                        HasLimit = link.HasLimit,
                        LimitMin = limMin,
                        LimitMax = limMax
                    });
                }

                //Debug.Log($"[PMXImporter] IK Bone '{pmxBone.Name}': target={meshContext.IKTargetIndex}, loops={meshContext.IKLoopCount}, links={meshContext.IKLinks.Count}");
                foreach (var lnk in meshContext.IKLinks)
                {
                    //Debug.Log($"[PMXImporter]   Link: resolvedIdx={lnk.BoneIndex} hasLimit={lnk.HasLimit} min={lnk.LimitMin} max={lnk.LimitMax}");
                }
                // PMXのIKLinkの元データも出力
                foreach (var link in pmxBone.IKLinks)
                {
                    //Debug.Log($"[PMXImporter]   RawLink: BoneIndex={link.BoneIndex} BoneName='{link.BoneName}'");
                }
            }

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

            // 各材質の頂点インデックス範囲（min, max）を算出
            var ranges = new (int min, int max)[n];
            for (int i = 0; i < n; i++)
            {
                var verts = materialToVertices[materialNames[i]];
                int min = int.MaxValue, max = int.MinValue;
                foreach (int vIdx in verts)
                {
                    if (vIdx < min) min = vIdx;
                    if (vIdx > max) max = vIdx;
                }
                ranges[i] = (min, max);
            }

            // 頂点インデックス範囲がオーバーラップする材質をUnion
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (ranges[i].min <= ranges[j].max && ranges[j].min <= ranges[i].max)
                    {
                        Union(i, j);
                    }
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
        /// 共有頂点を持つグループをマージ（ObjectName未設定のグループのみ対象）
        /// </summary>
        private static List<ObjectGroup> MergeGroupsBySharedVertices(
            PMXDocument document,
            List<ObjectGroup> objectGroups,
            Dictionary<string, HashSet<int>> materialToVertices)
        {
            //Debug.Log($"[PMXImporter] MergeGroupsBySharedVertices: called with {objectGroups.Count} groups");

            // ObjectName未設定の非ミラーグループを特定
            var fallbackIndices = new List<int>();
            var fallbackGroups = new List<ObjectGroup>();

            for (int i = 0; i < objectGroups.Count; i++)
            {
                var group = objectGroups[i];
                if (group.IsBakedMirror)
                {
                    Debug.Log($"[PMXImporter] Skipping group '{group.ObjectName}' (IsBakedMirror)");
                    continue;
                }

                // Memo欄にObjectNameが明示的に設定されているか確認
                bool hasExplicitObjectName = group.Materials.Any(m =>
                {
                    var mat = document.Materials[m.MaterialIndex];
                    var (objName, _) = PMXHelper.ParseMaterialMemo(mat.Memo);
                    return !string.IsNullOrEmpty(objName);
                });

                if (!hasExplicitObjectName)
                {
                    fallbackIndices.Add(i);
                    fallbackGroups.Add(group);
                }
                else
                {
                    //Debug.Log($"[PMXImporter] Skipping group '{group.ObjectName}' (has explicit ObjectName in Memo)");
                }
            }

            // 対象グループが1以下ならマージ不要
            if (fallbackGroups.Count <= 1)
            {
               // Debug.Log($"[PMXImporter] MergeGroupsBySharedVertices: early return (fallbackGroups.Count={fallbackGroups.Count}, total objectGroups={objectGroups.Count})");
                return objectGroups;
            }

            //Debug.Log($"[PMXImporter] MergeGroupsBySharedVertices: {fallbackGroups.Count} groups without ObjectName");
            foreach (var g in fallbackGroups)
            {
                var mats = string.Join(", ", g.Materials.ConvertAll(m => m.MaterialName));
               // Debug.Log($"  Group '{g.ObjectName}' IsBakedMirror={g.IsBakedMirror}: [{mats}]");
            }

            // フォールバック材質の頂点セットを収集
            var fallbackMatToVerts = new Dictionary<string, HashSet<int>>();
            foreach (var group in fallbackGroups)
            {
                foreach (var matInfo in group.Materials)
                {
                    if (materialToVertices.TryGetValue(matInfo.MaterialName, out var verts))
                    {
                        fallbackMatToVerts[matInfo.MaterialName] = verts;
                       // Debug.Log($"[PMXImporter] Material '{matInfo.MaterialName}': {verts.Count} vertices");
                    }
                    else
                    {
                       // Debug.LogWarning($"[PMXImporter] Material '{matInfo.MaterialName}' not found in materialToVertices!");
                    }
                }
            }

            // Union-Findで共有頂点を持つ材質をグループ化
            var mergedMaterialGroups = GroupMaterialsBySharedVertices(fallbackMatToVerts);

            Debug.Log($"[PMXImporter] Union-Find result: {mergedMaterialGroups.Count} groups");
            foreach (var mg in mergedMaterialGroups)
            {
                //Debug.Log($"  Merged group: [{string.Join(", ", mg)}]");
            }

            // マージが発生したか確認
            if (!mergedMaterialGroups.Any(g => g.Count > 1))
                return objectGroups;

            // 強い警告を出力
            foreach (var mergedGroup in mergedMaterialGroups)
            {
                if (mergedGroup.Count > 1)
                {
                    Debug.LogWarning(
                        $"[PMXImporter] ⚠⚠⚠ 共有頂点により材質がマージされました ⚠⚠⚠\n" +
                        $"  材質: [{string.Join(", ", mergedGroup)}]\n" +
                        $"  これらの材質は頂点を共有しているため、1つのMeshContextに格納されます。\n" +
                        $"  意図しない場合は、PMXエディタで材質Memo欄にObjectNameを設定してください。");
                }
            }

            // 材質名→マージグループインデックスのマッピング
            var matToMergedIdx = new Dictionary<string, int>();
            for (int gi = 0; gi < mergedMaterialGroups.Count; gi++)
            {
                foreach (var matName in mergedMaterialGroups[gi])
                {
                    matToMergedIdx[matName] = gi;
                }
            }

            // マージされたObjectGroupを生成
            var newMergedGroups = new ObjectGroup[mergedMaterialGroups.Count];
            for (int gi = 0; gi < mergedMaterialGroups.Count; gi++)
            {
                var matNames = mergedMaterialGroups[gi];
                if (matNames.Count == 1)
                {
                    // マージなし: 元のグループをそのまま使用
                    newMergedGroups[gi] = fallbackGroups.First(
                        g => g.Materials.Any(m => m.MaterialName == matNames[0]));
                }
                else
                {
                    // マージ: 新しいObjectGroupを作成
                    var newGroup = new ObjectGroup
                    {
                        ObjectName = matNames[0],
                        IsBakedMirror = false
                    };

                    // 材質を元の順序（MaterialIndex昇順）で追加
                    var allMatInfos = new List<MaterialObjectInfo>();
                    foreach (var matName in matNames)
                    {
                        var srcGroup = fallbackGroups.First(
                            g => g.Materials.Any(m => m.MaterialName == matName));
                        allMatInfos.Add(srcGroup.Materials.First(m => m.MaterialName == matName));
                    }
                    allMatInfos.Sort((a, b) => a.MaterialIndex.CompareTo(b.MaterialIndex));

                    foreach (var matInfo in allMatInfos)
                    {
                        newGroup.Materials.Add(matInfo);
                    }

                    // 頂点インデックスを再収集
                    PMXHelper.CollectGroupVertices(document, newGroup);

                    newMergedGroups[gi] = newGroup;
                }
            }

            // objectGroupsリストを再構築（元の順序を保持）
            var result = new List<ObjectGroup>();
            var processedMergeIndices = new HashSet<int>();

            for (int i = 0; i < objectGroups.Count; i++)
            {
                int fallbackPos = fallbackIndices.IndexOf(i);
                if (fallbackPos < 0)
                {
                    // フォールバックでないグループはそのまま
                    result.Add(objectGroups[i]);
                }
                else
                {
                    // フォールバックグループ → マージ結果に差し替え
                    var firstMatName = fallbackGroups[fallbackPos].Materials[0].MaterialName;
                    int mergedIdx = matToMergedIdx[firstMatName];

                    if (!processedMergeIndices.Contains(mergedIdx))
                    {
                        // このマージグループの最初の出現位置に挿入
                        result.Add(newMergedGroups[mergedIdx]);
                        processedMergeIndices.Add(mergedIdx);
                    }
                    // 以降の出現はスキップ（既にマージ済み）
                }
            }

            return result;
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
            meshObject.IsExpanded = true;  // PMXは展開済み形式

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
                        //Debug.Log($"[PMXImporter] Vertex[{oldIdx}] BoneWeight: " +
                        //          $"({bw.boneIndex0}:{bw.weight0:F2}, {bw.boneIndex1}:{bw.weight1:F2}, " +
                        //          $"{bw.boneIndex2}:{bw.weight2:F2}, {bw.boneIndex3}:{bw.weight3:F2})");
                        debugCount++;
                    }

                    // 複数ウェイトを持つ頂点（最初の3つ）
                    if (meshIndex == 0 && bw.weight1 > 0 && multiWeightDebugCount < 3)
                    {
                        //Debug.Log($"[PMXImporter] MultiWeight Vertex[{oldIdx}]: " +
                        //          $"({bw.boneIndex0}:{bw.weight0:F2}, {bw.boneIndex1}:{bw.weight1:F2}, " +
                        //          $"{bw.boneIndex2}:{bw.weight2:F2}, {bw.boneIndex3}:{bw.weight3:F2})");
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
                        //Debug.Log($"[PMXImporter] Normal[{vi}]: ({n.x:F3}, {n.y:F3}, {n.z:F3})");
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
                //Debug.Log($"[PMXImporter] Mesh '{meshName}' SubMeshCount={unityMesh.subMeshCount}, VertexCount={unityMesh.vertexCount}");
                for (int sm = 0; sm < Mathf.Min(unityMesh.subMeshCount, 10); sm++)
                {
                    int triCount = unityMesh.GetTriangles(sm).Length / 3;
                    //if (triCount > 0)
                        //Debug.Log($"[PMXImporter]   SubMesh[{sm}]: {triCount} triangles, Mat='{document.Materials[sm].Name}'");
                }
            }

            // マテリアルはインポート後に ReplaceMaterials 経由で ModelContext に設定される

            //Debug.Log($"[PMXImporter] Created mesh '{meshName}': V={meshObject.VertexCount}, F={meshObject.FaceCount}, " +
            //          $"LocalMat={materialNames.Count}, GlobalMatCount={document.Materials.Count}, " +
            //          $"MatIndices=[{string.Join(",", materialNameToGlobalIndex.Values)}]");

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
                //Debug.Log($"[PMXImporter] Material '{pmxMat.Name}' set to Transparent (alpha={color.a:F2})");
            }
            // アルファクリップ設定
            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 1f);
                material.EnableKeyword("_ALPHATEST_ON");
            }
            if (material.HasProperty("_Cutoff"))
            {
                material.SetFloat("_Cutoff", 0.5f);
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
                    //Debug.Log($"[PMXImporter] Loaded texture: {pmxMat.TexturePath}");
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
                            //Debug.Log($"[PMXImporter] Texture found in baseDir: {foundPath}");
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
                        //Debug.Log($"[PMXImporter] Texture loaded from file: {fullPath}");
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                        texture = null;
                        //Debug.LogWarning($"[PMXImporter] Failed to load image data: {fullPath}");
                    }
                }
                catch// (System.Exception e)
                {
                    //Debug.LogWarning($"[PMXImporter] Failed to read texture file: {fullPath} - {e.Message}");
                }
            }

            if (texture == null)
            {
                //Debug.LogWarning($"[PMXImporter] Texture not found: {fullPath} (original: {texturePath})");
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
        public static Vector3 ConvertPosition(Vector3 pmxPos, PMXImportSettings settings)
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

        // ================================================================
        // MaterialGroupInfo 構築
        // ================================================================

        /// <summary>
        /// 材質グループから MaterialGroupInfo を構築
        /// </summary>
        private static MaterialGroupInfo BuildMaterialGroupInfo(
            List<string> materialNames,
            Dictionary<string, List<PMXFace>> materialToFaces,
            int meshIndex)
        {
            var info = new MaterialGroupInfo
            {
                MaterialNames = new List<string>(materialNames)
            };

            // グループ内の全面から使用頂点を収集
            foreach (var matName in materialNames)
            {
                if (materialToFaces.TryGetValue(matName, out var faces))
                {
                    foreach (var face in faces)
                    {
                        info.UsedVertexIndices.Add(face.VertexIndex1);
                        info.UsedVertexIndices.Add(face.VertexIndex2);
                        info.UsedVertexIndices.Add(face.VertexIndex3);
                    }
                }
            }

            // PMX頂点インデックス → ローカルインデックス のマッピングを構築
            // ConvertMaterialGroup と同じロジック
            var sortedIndices = info.UsedVertexIndices.OrderBy(x => x).ToList();
            for (int i = 0; i < sortedIndices.Count; i++)
            {
                info.PmxToLocalIndex[sortedIndices[i]] = i;
            }

            return info;
        }

        // ================================================================
        // モーフ変換
        // ================================================================

        /// <summary>
        /// PMXモーフをMeshContext + MorphExpressionに変換
        /// グループモーフ対応：
        /// 1. 頂点/UVモーフを仮MorphExpressionとして生成
        /// 2. グループモーフを読み、子モーフのMeshEntriesをweight付きでフラット展開した親MorphExpressionを作成
        /// 3. グループに属さない仮MorphExpressionはweight=1.0で正規のMorphExpressionにする
        /// </summary>
        private static void ConvertMorphs(PMXDocument document, PMXImportSettings settings, PMXImportResult result)
        {
            if (result.MaterialGroupInfos.Count == 0)
            {
                Debug.LogWarning("[PMXImporter] No material group info available for morph conversion");
                return;
            }

            // Phase 1: 頂点/UVモーフを仮MorphExpressionとして生成
            // PMXモーフインデックス → 仮MorphExpression のマッピング
            var tempMorphExpressions = new Dictionary<int, MorphExpression>();

            for (int i = 0; i < document.Morphs.Count; i++)
            {
                var pmxMorph = document.Morphs[i];
                int beforeCount = result.MorphExpressions.Count;

                if (pmxMorph.MorphType == 1)
                {
                    ConvertVertexMorph(document, pmxMorph, settings, result);
                }
                else if (pmxMorph.MorphType >= 3 && pmxMorph.MorphType <= 7)
                {
                    ConvertUVMorph(document, pmxMorph, settings, result);
                }
                // ボーンモーフ(2)、マテリアルモーフ(8)等は未対応

                // 直前の呼び出しでMorphExpressionが追加された場合のみ記録
                if (result.MorphExpressions.Count > beforeCount)
                {
                    tempMorphExpressions[i] = result.MorphExpressions[result.MorphExpressions.Count - 1];
                }
            }

            // Phase 2: グループモーフを処理
            // グループに所属した仮MorphExpressionを追跡
            var groupedMorphIndices = new HashSet<int>();

            //Debug.Log($"[PMXImporter] Phase 1 complete: {tempMorphExpressions.Count} temp morph sets created, {result.MorphExpressions.Count} total morph sets");

            for (int i = 0; i < document.Morphs.Count; i++)
            {
                var pmxMorph = document.Morphs[i];
                if (pmxMorph.MorphType != 0) continue;  // グループモーフのみ

                //Debug.Log($"[PMXImporter] Processing group morph [{i}] '{pmxMorph.Name}': {pmxMorph.Offsets.Count} offsets");

                var groupSet = new MorphExpression(pmxMorph.Name, MorphType.Group)
                {
                    NameEnglish = pmxMorph.NameEnglish ?? "",
                    Panel = pmxMorph.Panel
                };

                foreach (var offset in pmxMorph.Offsets)
                {
                    if (offset is not PMXGroupMorphOffset groupOffset) continue;

                    int childMorphIndex = groupOffset.MorphIndex;

                    // 名前ベースのフォールバック（CSV入力でインデックスが-1の場合）
                    if (childMorphIndex < 0 && !string.IsNullOrEmpty(groupOffset.MorphName))
                    {
                        for (int j = 0; j < document.Morphs.Count; j++)
                        {
                            if (document.Morphs[j].Name == groupOffset.MorphName)
                            {
                                childMorphIndex = j;
                                break;
                            }
                        }
                    }

                    MorphExpression childSet = null;
                    bool found = childMorphIndex >= 0 && tempMorphExpressions.TryGetValue(childMorphIndex, out childSet);
                    if (!found)
                    {
                        string childName = (childMorphIndex >= 0 && childMorphIndex < document.Morphs.Count) 
                            ? document.Morphs[childMorphIndex].Name : "?";
                        int childType = (childMorphIndex >= 0 && childMorphIndex < document.Morphs.Count) 
                            ? document.Morphs[childMorphIndex].MorphType : -1;
                        //Debug.Log($"[PMXImporter]   Child [{childMorphIndex}] '{childName}' (type={childType}): NOT in tempMorphExpressions (tempKeys: {string.Join(",", tempMorphExpressions.Keys.Take(10))})");
                        continue;
                    }

                    float groupWeight = groupOffset.Weight;

                    // 子MorphExpressionのMeshEntriesを親にフラット展開（weight乗算）
                    foreach (var childEntry in childSet.MeshEntries)
                    {
                        groupSet.AddMesh(childEntry.MeshIndex, childEntry.Weight * groupWeight);
                    }

                    //Debug.Log($"[PMXImporter]   Child [{childMorphIndex}] '{childSet.Name}': {childSet.MeshEntries.Count} entries, weight={groupWeight}");
                    groupedMorphIndices.Add(childMorphIndex);
                }

                if (groupSet.MeshCount > 0)
                {
                    result.MorphExpressions.Add(groupSet);
                    //Debug.Log($"[PMXImporter] Group morph '{pmxMorph.Name}': {groupSet.MeshCount} meshes from {pmxMorph.Offsets.Count} children");
                }
            }

            // Phase 3: グループに属さない仮MorphExpressionはそのまま残す（weight=1.0で既に追加済み）
            // グループに属した仮MorphExpressionをresult.MorphExpressionsから除去
            if (groupedMorphIndices.Count > 0)
            {
                var groupedSets = new HashSet<MorphExpression>();
                foreach (var idx in groupedMorphIndices)
                {
                    if (tempMorphExpressions.TryGetValue(idx, out var set))
                        groupedSets.Add(set);
                }
                result.MorphExpressions.RemoveAll(s => groupedSets.Contains(s));

                //Debug.Log($"[PMXImporter] Removed {groupedSets.Count} child morph sets absorbed by group morphs");
            }
        }

        /// <summary>
        /// 頂点モーフを変換
        /// </summary>
        private static void ConvertVertexMorph(
            PMXDocument document,
            PMXMorph pmxMorph,
            PMXImportSettings settings,
            PMXImportResult result)
        {
            // 各MaterialGroupに対するオフセットを分類
            // key: MaterialGroupInfo のインデックス, value: (ローカル頂点Index, オフセット) のリスト
            var groupOffsets = new Dictionary<int, List<(int localIndex, Vector3 offset)>>();

            foreach (var offset in pmxMorph.Offsets)
            {
                if (offset is PMXVertexMorphOffset vertexOffset)
                {
                    int pmxVertexIndex = vertexOffset.VertexIndex;

                    // この頂点がどのグループに属するか検索
                    for (int gi = 0; gi < result.MaterialGroupInfos.Count; gi++)
                    {
                        var groupInfo = result.MaterialGroupInfos[gi];
                        if (groupInfo.PmxToLocalIndex.TryGetValue(pmxVertexIndex, out int localIndex))
                        {
                            if (!groupOffsets.ContainsKey(gi))
                                groupOffsets[gi] = new List<(int, Vector3)>();

                            // オフセットを座標変換
                            Vector3 convertedOffset = ConvertPosition(vertexOffset.Offset, settings);
                            groupOffsets[gi].Add((localIndex, convertedOffset));
                            break;  // 1つの頂点は1つのグループにのみ属する
                        }
                    }
                }
            }

            if (groupOffsets.Count == 0)
            {
                //Debug.LogWarning($"[PMXImporter] Vertex morph '{pmxMorph.Name}' has no valid offsets");
                return;
            }

            // MorphExpressionを作成
            var morphExpression = new MorphExpression(pmxMorph.Name, MorphType.Vertex)
            {
                NameEnglish = pmxMorph.NameEnglish ?? "",
                Panel = pmxMorph.Panel
            };

            // 影響する各グループについてモーフメッシュを作成
            foreach (var kvp in groupOffsets)
            {
                int groupIndex = kvp.Key;
                var offsets = kvp.Value;
                var groupInfo = result.MaterialGroupInfos[groupIndex];

                if (groupInfo.MeshContextIndex < 0 || groupInfo.MeshContextIndex >= result.MeshContexts.Count)
                    continue;

                var baseMesh = result.MeshContexts[groupInfo.MeshContextIndex];
                if (baseMesh?.MeshObject == null) continue;

                // メッシュをクローン
                var morphMesh = new MeshContext
                {
                    MeshObject = baseMesh.MeshObject.Clone(),
                    Type = MeshType.Morph,
                    IsVisible = false,  // モーフメッシュは非表示
                    ExcludeFromExport = true  // エクスポートから除外
                };
                morphMesh.MeshObject.Type = MeshType.Morph;  // MeshObject.Typeも設定（TypedMeshIndices用）
                morphMesh.MeshObject.Name = $"{baseMesh.Name}_{pmxMorph.Name}";
                morphMesh.Name = morphMesh.MeshObject.Name;

                // MorphBaseDataを設定（現在の位置を基準として保存）
                morphMesh.SetAsMorph(pmxMorph.Name);
                morphMesh.MorphPanel = pmxMorph.Panel;

                // オフセットを適用
                foreach (var (localIndex, offset) in offsets)
                {
                    if (localIndex < morphMesh.MeshObject.VertexCount)
                    {
                        morphMesh.MeshObject.Vertices[localIndex].Position += offset;
                    }
                }

                // Unity Meshを再構築
                morphMesh.UnityMesh = morphMesh.MeshObject.ToUnityMeshShared();
                morphMesh.UnityMesh.name = morphMesh.MeshObject.Name;
                morphMesh.UnityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;

                // 結果に追加
                int morphMeshIndex = result.MeshContexts.Count;
                result.MeshContexts.Add(morphMesh);
                morphExpression.AddMesh(morphMeshIndex);
            }

            if (morphExpression.MeshCount > 0)
            {
                result.MorphExpressions.Add(morphExpression);
            }
        }

        /// <summary>
        /// UVモーフを変換
        /// </summary>
        private static void ConvertUVMorph(
            PMXDocument document,
            PMXMorph pmxMorph,
            PMXImportSettings settings,
            PMXImportResult result)
        {
            // 各MaterialGroupに対するオフセットを分類
            var groupOffsets = new Dictionary<int, List<(int localIndex, Vector2 offset)>>();

            foreach (var offset in pmxMorph.Offsets)
            {
                if (offset is PMXUVMorphOffset uvOffset)
                {
                    int pmxVertexIndex = uvOffset.VertexIndex;

                    // この頂点がどのグループに属するか検索
                    for (int gi = 0; gi < result.MaterialGroupInfos.Count; gi++)
                    {
                        var groupInfo = result.MaterialGroupInfos[gi];
                        if (groupInfo.PmxToLocalIndex.TryGetValue(pmxVertexIndex, out int localIndex))
                        {
                            if (!groupOffsets.ContainsKey(gi))
                                groupOffsets[gi] = new List<(int, Vector2)>();

                            // UV オフセット（Vector4のXYのみ使用）
                            Vector2 uvOffsetValue = new Vector2(uvOffset.Offset.x, uvOffset.Offset.y);

                            // UV V反転設定に応じて調整
                            if (settings.FlipUV_V)
                                uvOffsetValue.y = -uvOffsetValue.y;

                            groupOffsets[gi].Add((localIndex, uvOffsetValue));
                            break;
                        }
                    }
                }
            }

            if (groupOffsets.Count == 0)
            {
                Debug.LogWarning($"[PMXImporter] UV morph '{pmxMorph.Name}' has no valid offsets");
                return;
            }

            // MorphTypeを決定
            MorphType morphType = pmxMorph.MorphType switch
            {
                3 => MorphType.UV,
                4 => MorphType.UV1,
                5 => MorphType.UV2,
                6 => MorphType.UV3,
                7 => MorphType.UV4,
                _ => MorphType.UV
            };

            // MorphExpressionを作成
            var MorphExpression = new MorphExpression(pmxMorph.Name, morphType)
            {
                NameEnglish = pmxMorph.NameEnglish ?? "",
                Panel = pmxMorph.Panel
            };

            // 影響する各グループについてモーフメッシュを作成
            foreach (var kvp in groupOffsets)
            {
                int groupIndex = kvp.Key;
                var offsets = kvp.Value;
                var groupInfo = result.MaterialGroupInfos[groupIndex];

                if (groupInfo.MeshContextIndex < 0 || groupInfo.MeshContextIndex >= result.MeshContexts.Count)
                    continue;

                var baseMesh = result.MeshContexts[groupInfo.MeshContextIndex];
                if (baseMesh?.MeshObject == null) continue;

                // メッシュをクローン
                var morphMesh = new MeshContext
                {
                    MeshObject = baseMesh.MeshObject.Clone(),
                    Type = MeshType.Morph,
                    IsVisible = false,
                    ExcludeFromExport = true
                };
                morphMesh.MeshObject.Type = MeshType.Morph;  // MeshObject.Typeも設定（TypedMeshIndices用）
                morphMesh.MeshObject.Name = $"{baseMesh.Name}_{pmxMorph.Name}";
                morphMesh.Name = morphMesh.MeshObject.Name;

                // MorphBaseDataを設定
                morphMesh.SetAsMorph(pmxMorph.Name);
                morphMesh.MorphPanel = pmxMorph.Panel;

                // UVオフセットを適用
                foreach (var (localIndex, uvOffset) in offsets)
                {
                    if (localIndex < morphMesh.MeshObject.VertexCount)
                    {
                        var vertex = morphMesh.MeshObject.Vertices[localIndex];
                        if (vertex.UVs.Count > 0)
                        {
                            vertex.UVs[0] += uvOffset;
                        }
                    }
                }

                // Unity Meshを再構築
                morphMesh.UnityMesh = morphMesh.MeshObject.ToUnityMeshShared();
                morphMesh.UnityMesh.name = morphMesh.MeshObject.Name;
                morphMesh.UnityMesh.hideFlags = UnityEngine.HideFlags.HideAndDontSave;

                // 結果に追加
                int morphMeshIndex = result.MeshContexts.Count;
                result.MeshContexts.Add(morphMesh);
                MorphExpression.AddMesh(morphMeshIndex);
            }

            if (MorphExpression.MeshCount > 0)
            {
                result.MorphExpressions.Add(MorphExpression);
            }
        }
    }
}