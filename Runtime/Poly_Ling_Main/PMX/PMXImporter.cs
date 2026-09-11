// Assets/Editor/Poly_Ling/PMX/Import/PMXImporter.cs
// PMXDocument → MeshObject/MeshContext 変換
// SimpleMeshFactoryのデータ構造に変換
// 頂点共有する材質をグループ化
//
// 【分割先】このファイルから次へ分けてある。
//   PMXImportResult.cs    PMX インポートの結果・統計・マテリアルグループ情報（PMXImporter から分離）。
//   PMXImporter.Bone.cs   PMX インポート：ボーン変換と T ポーズ変換。
//   PMXImporter.Mesh.cs   PMX インポート：頂点・マテリアル・座標の変換と MaterialGroupInfo の構築。
//   PMXImporter.Morph.cs  PMX インポート：モーフ変換と剛体・JOINT のインポート。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Materials;
using Poly_Ling.Core;
using Poly_Ling.Symmetry;

namespace Poly_Ling.PMX
{

    /// <summary>
    /// PMXインポーター
    /// </summary>
    public static partial class PMXImporter
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

            // モデル情報を保持する。ModelContext.Name はファイル名で上書きされるため、
            // PMX が持っていた表示名とコメントはここに残す。
            result.ModelInfo = new PmxModelInfoData
            {
                Name           = document.ModelInfo?.Name           ?? "",
                NameEnglish    = document.ModelInfo?.NameEnglish    ?? "",
                Comment        = document.ModelInfo?.Comment        ?? "",
                CommentEnglish = document.ModelInfo?.CommentEnglish ?? ""
            };

            //Debug.Log($"[PMXImporter] ImportTarget: {settings.ImportTarget}");

            // マテリアルをUnityマテリアルに変換（Mesh読み込み時のみ）
            if (settings.ShouldImportMesh && settings.ImportMaterials)
            {
                string baseDir = GetBaseDirectory(document.FilePath);
                foreach (var pmxMat in document.Materials)
                {
                    var mat = ConvertMaterial(pmxMat, document, settings);
                    var matRef = new MaterialReference(mat);

                    // ソーステクスチャパスを絶対パスで設定
                    if (!string.IsNullOrEmpty(pmxMat.TexturePath))
                        matRef.Data.SourceTexturePath = ResolveTextureFullPath(pmxMat.TexturePath, baseDir);

                    // 両面描画フラグを CullMode に反映（DrawMeshes での動的カリング制御に使用）
                    if ((pmxMat.DrawFlags & 0x01) != 0)
                        matRef.Data.CullMode = Poly_Ling.Materials.CullModeType.Off;

                    // PMX 固有欄を保持する。MaterialData は URP 用でこれらを表現できないため、
                    // ここで落とすと書き出しで既定値になる（反射色・環境色・描画フラグなど）。
                    matRef.Data.Pmx = new PmxMaterialData
                    {
                        NameEnglish       = pmxMat.NameEnglish ?? "",
                        SpecularPower     = pmxMat.SpecularPower,
                        DrawFlags         = pmxMat.DrawFlags,
                        EdgeSize          = pmxMat.EdgeSize,
                        SphereTexturePath = pmxMat.SphereTexturePath ?? "",
                        SphereMode        = pmxMat.SphereMode,
                        SharedToon        = pmxMat.SharedToon,
                        ToonTextureIndex  = pmxMat.ToonTextureIndex,
                        ToonTexturePath   = pmxMat.ToonTexturePath ?? "",
                        Memo              = pmxMat.Memo ?? ""
                    };
                    matRef.Data.Pmx.SetSpecular(pmxMat.Specular);
                    matRef.Data.Pmx.SetAmbient(pmxMat.Ambient);
                    matRef.Data.Pmx.SetEdgeColor(pmxMat.EdgeColor);

                    // テクスチャパスは MaterialData 側を正とする（書き出しでここから引く）。
                    if (!string.IsNullOrEmpty(pmxMat.TexturePath))
                        matRef.Data.BaseMapPath = pmxMat.TexturePath;

                    result.MaterialReferences.Add(matRef);
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
                        // Memo欄のIsMirrorフラグを記録
                        meshContext.IsMirrorFromMemo = objectGroup.IsBakedMirror;
                        // Memo欄のdepthを記録
                        meshContext.Depth = objectGroup.Depth;
                        groupInfo.Name = meshContext.Name;
                        result.MeshContexts.Add(meshContext);
                        result.MaterialGroupInfos.Add(groupInfo);
                        meshIndex++;
                    }
                }

                result.Stats.MeshCount = result.MeshContexts.Count - boneContextCount;

                // depthからParentIndexを計算
                if (result.Stats.MeshCount > 0)
                {
                    var meshOnly = result.MeshContexts.Skip(boneContextCount).ToList();
                    PMXHelper.CalcParentIndicesFromDepth(meshOnly, boneContextCount);
                }
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

            // 剛体をインポート（Type=RigidBody の空MeshObject + RigidBodyData）
            // ジョイントの剛体参照解決のため、剛体コンテキストの開始indexを記録する。
            int rigidBodyContextBase = -1;
            if (settings.ShouldImportBodies && document.RigidBodies.Count > 0)
            {
                rigidBodyContextBase = result.MeshContexts.Count;
                ConvertRigidBodies(document, settings, result);
            }

            // ジョイントをインポート（Type=RigidBodyJoint の空MeshObject + JointData）
            if (settings.ShouldImportJoints && document.Joints.Count > 0)
            {
                ConvertJoints(document, settings, result, rigidBodyContextBase);
            }

            // モーフをインポート
            if (settings.ShouldImportMorphs && document.Morphs.Count > 0)
            {
                ConvertMorphs(document, settings, result);
                //Debug.Log($"[PMXImporter] Imported {result.MorphExpressions.Count} morph sets");
            }

            // PolyLingメタモーフを適用（ShouldImportMorphsに関わらず常に実行）
            ApplyPolyLingMetaMorphs(document, result);

            // フェースメタ (.plmface.csv) が存在すれば適用
            if (!string.IsNullOrEmpty(document.FilePath))
            {
                var faceMeta = PMXFaceMetaReader.Load(document.FilePath);
                if (faceMeta != null)
                    PMXFaceMetaReader.Apply(faceMeta, result.MeshContexts);
            }

            // ミラーペア検出・構築（常に実行）
            DetectAndBuildMirrorPairs(result, boneContextCount, settings);

            // 描画オブジェクトの種別を確定する。
            // 頂点のウェイトが揃うのはここまでの変換が全部終わったあと
            // （ボーン索引のオフセット補正・ミラーペア構築を含む）なので、最後に 1 回だけ行う。
            SkinKindOps.RecomputeAll(result.MeshContexts);
        }

        // ================================================================
        // ミラーペア検出・構築（名前末尾+ から自動検出）
        // ================================================================

        /// <summary>
        /// ミラーメッシュを検出し、設定に応じてMirrorPairまたはBakedMirrorとして構成する。
        /// 
        /// DetectNamedMirror=false: Memo欄IsMirrorフラグのあるメッシュのみミラーとして検出
        /// DetectNamedMirror=true: Memo欄に加え、名前末尾「+」のメッシュもミラーとして検出
        /// 
        /// BakeMirror=false: MirrorPairを構築（Real↔Mirror同期）
        /// BakeMirror=true: ベイクドミラー（独立メッシュ、MeshType.BakedMirror）
        /// </summary>
        private static void DetectAndBuildMirrorPairs(PMXImportResult result, int boneContextCount, PMXImportSettings settings)
        {
            var meshContexts = result.MeshContexts;

            // メッシュ名→インデックスのマッピング（ボーン以降のメッシュのみ）
            var nameToIndex = new Dictionary<string, int>();
            for (int i = boneContextCount; i < meshContexts.Count; i++)
            {
                var ctx = meshContexts[i];
                if (ctx == null || ctx.Type != MeshType.Mesh) continue;
                if (!nameToIndex.ContainsKey(ctx.Name))
                    nameToIndex[ctx.Name] = i;
            }

            int pairCount = 0;
            int bakedCount = 0;
            for (int i = boneContextCount; i < meshContexts.Count; i++)
            {
                var ctx = meshContexts[i];
                if (ctx == null || ctx.Type != MeshType.Mesh) continue;
                if (!ctx.Name.EndsWith("+")) continue;

                // DetectNamedMirror=false: Memo欄IsMirrorのみ対象
                // DetectNamedMirror=true: Memo欄 + 名前末尾「+」も対象
                if (!settings.DetectNamedMirror && !ctx.IsMirrorFromMemo)
                    continue;

                string sourceName = ctx.Name.Substring(0, ctx.Name.Length - 1);
                if (!nameToIndex.TryGetValue(sourceName, out int sourceIndex))
                    continue;

                var realCtx = meshContexts[sourceIndex];
                var mirrorCtx = ctx;

                if (settings.BakeMirror)
                {
                    // ベイクドミラー: 独立メッシュとして扱う
                    mirrorCtx.Type = MeshType.BakedMirror;
                    mirrorCtx.BakedMirrorSourceIndex = sourceIndex;
                    realCtx.HasBakedMirrorChild = true;
                    bakedCount++;
                    //Debug.Log($"[PMXImporter] BakedMirror: '{sourceName}' → '{ctx.Name}'");
                }
                else
                {
                    // MirrorPairを構築
                    var pair = new MirrorPair
                    {
                        Real = realCtx,
                        Mirror = mirrorCtx,
                        Axis = Poly_Ling.Symmetry.SymmetryAxis.X
                    };

                    // 組み立て中の列を渡す。この時点では MeshContext がまだ
                    // ModelContext へ繋がっておらず ParentModelContext が null なので、
                    // 渡さないと BuildBonePairMap が失敗し Build() ごと落ちる。
                    // result.MeshContexts の並びは、この後 ModelContext へ
                    // 先頭から順に Add される並びと一致する。
                    bool success = pair.Build(result.MeshContexts);

                    if (success)
                    {
                        // 実体側にミラー情報を設定
                        realCtx.MirrorType = 1;    // 左右対称
                        realCtx.MirrorAxis = 1;    // X軸

                        // ミラー側: サーフェス描画のみ（頂点・辺・ヒットテスト対象外）
                        mirrorCtx.Type = MeshType.MirrorSide;

                        result.MirrorPairs.Add(pair);
                        pairCount++;
                        //Debug.Log($"[PMXImporter] MirrorPair built: '{sourceName}' ↔ '{ctx.Name}'\n{pair.BuildLog}");
                    }
                    else
                    {
                        // 失敗するとペアを登録しないので、このオブジェクトは
                        // ミラー同期（頂点移動・ウェイトの写し）が丸ごと効かなくなる。
                        Debug.LogWarning(
                            $"[PMXImporter] MirrorPair build failed: '{sourceName}' ↔ '{ctx.Name}'"
                            + " — このオブジェクトのミラー同期は無効になります"
                            + $"\n{pair.BuildLog}");
                    }
                }
            }

            if (pairCount > 0)
            {
                //Debug.Log($"[PMXImporter] Built {pairCount} mirror pairs");
            }
            if (bakedCount > 0)
            {
                //Debug.Log($"[PMXImporter] Created {bakedCount} baked mirrors");
            }
        }

        /// <summary>
        /// 旧メソッド（後方互換・参照用に残す）
        /// 名前末尾が+のメッシュをBakedMirrorとして設定
        /// </summary>
        [System.Obsolete("Use DetectAndBuildMirrorPairs instead")]
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
    }
}