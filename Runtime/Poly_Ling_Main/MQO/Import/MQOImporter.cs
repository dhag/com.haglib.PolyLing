// Assets/Editor/Poly_Ling/MQO/Import/MQOImporter.cs
// MQODocument → MeshObject/MeshUndoContext 変換
// SimpleMeshFactoryのデータ構造に変換
//
// 【分割先】このファイルから次へ分けてある。
//   MQOImportResult.cs         MQO インポートの結果・統計（MQOImporter から分離）。
//   MQOImporter.Material.cs    MQO インポート：マテリアル・座標の変換、ヘルパー、頂点デバッグ出力、ベイクミラー生成。
//   MQOImporter.MirrorBone.cs  MQO インポート：ミラー処理とボーン変換。
//   MQOImporter.Object.cs      MQO インポート：オブジェクト変換と面変換。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Poly_Ling.CSV;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.Tools;
using Poly_Ling.Materials;
using Poly_Ling.EditorBridge;
using Poly_Ling.PMX;
using Poly_Ling.Symmetry;
using Poly_Ling.MeshBridge;

// MeshContextはSimpleMeshFactoryのネストクラス
//using MeshContext = MeshContext;

namespace Poly_Ling.MQO
{

    /// <summary>
    /// MQOインポーター
    /// </summary>
    public static partial class MQOImporter
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
                    result.MaterialReferences.Add(matRef);
                }

                // ミラー側マテリアルオフセットを記録
                result.MirrorMaterialOffset = result.MaterialCount;

                // ミラー側マテリアル（実体側を複製、名前に"+"を付加、ソースパスを引き継ぐ）
                foreach (var mqoMat in document.Materials)
                {
                    var matRef = ConvertMaterialToRef(mqoMat, settings);
                    matRef.Data.Name = matRef.Data.Name + "+";
                    matRef.Material.name = matRef.Data.Name;
                    result.MaterialReferences.Add(matRef);
                }

                result.Stats.MaterialCount = result.MaterialCount;
                Debug.Log($"[MQOImporter] Materials: {result.MirrorMaterialOffset} original + {result.MirrorMaterialOffset} mirror = {result.MaterialCount} total");
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

            // __Armature__からボーンをインポート（ボーンCSVが無い場合のみ）
            HashSet<string> armatureBoneNames = null;
            if (settings.ImportBonesFromArmature && !settings.UseBoneCSV)
            {
                var armatureResult = ImportBonesFromArmature(document.Objects, settings);
                if (armatureResult.BoneContexts.Count > 0)
                {
                    // boneNameToIndexを作成
                    boneNameToIndex = new Dictionary<string, int>();
                    armatureBoneNames = new HashSet<string>();

                    for (int i = 0; i < armatureResult.BoneContexts.Count; i++)
                    {
                        var bc = armatureResult.BoneContexts[i];
                        result.MeshContexts.Add(bc);
                        boneNameToIndex[bc.Name] = i;
                        armatureBoneNames.Add(bc.Name);
                    }

                    boneContextCount = result.MeshContexts.Count;
                    result.Stats.BoneCount = armatureResult.BoneContexts.Count;
                    Debug.Log($"[MQOImporter] Imported {armatureResult.BoneContexts.Count} bones from __Armature__");

                    // __IK__セクションからIK情報をインポート
                    ApplyIKFromObjects(document.Objects, armatureResult.BoneContexts, boneNameToIndex);
                }
            }

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
                // __Armature__オブジェクトをスキップ
                if (mqoObj.Name == "__Armature__")
                    continue;

                // __ArmatureName__オブジェクトとその下のオブジェクトをスキップ
                if (mqoObj.Name == "__ArmatureName__" || mqoObj.Name.StartsWith("__ArmatureName__"))
                    continue;

                // __IK__オブジェクトとその子をスキップ
                if (mqoObj.Name == "__IK__" || mqoObj.Name.StartsWith("__IK__") ||
                    mqoObj.Name.StartsWith("__IKTarget__") || mqoObj.Name.StartsWith("__IKLink__"))
                    continue;

                // __Armature__からインポートされたボーンをスキップ
                if (armatureBoneNames != null && armatureBoneNames.Contains(mqoObj.Name))
                    continue;

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
            // ミラー処理
            // IsMirroredなメッシュに対してミラー側MeshContextを生成
            // BakeMirror=true: BakedMirror（独立メッシュ）
            // BakeMirror=false: MirrorPair（Real↔Mirror同期、MeshType.MirrorSide）
            // ================================================================
            {
                int insertedCount = 0;

                // BakedMirrorSourceIndex は CreateBakedMirrorMesh が「生成時点の実体側 index」で
                // 記録するが、より小さい i への Insert が起きるたびに実体側もミラー側も +1 ずれる。
                // 後方走査は「これから処理する要素」の index を保つだけで、
                // 「既に記録済みの index」までは保たない。
                // ループ中は参照で保持しておき、全挿入完了後に IndexOf で解決し直す。
                var mirrorSourcePairs = new List<(MeshContext mirror, MeshContext real)>();

                // 後ろから処理することでインデックスのずれを回避
                for (int i = result.MeshContexts.Count - 1; i >= 0; i--)
                {
                    var ctx = result.MeshContexts[i];
                    //Debug.Log($"[MQOImporter] mesh={ctx.Name} IsMirrored={ctx.IsMirrored} MirrorType={ctx.MirrorType} Type={ctx.Type}");
                    if (ctx.IsMirrored && ctx.Type == MeshType.Mesh)
                    {
                        // 同名の有無は、その時点の列（挿入済みのミラーも含む）で見る。
                        // 「左腕」→「右腕」が空いていればそれを使い、埋まっていれば接尾辞へ落ちる。
                        var snapshot = result.MeshContexts;
                        Func<string, bool> nameExists = n =>
                        {
                            if (string.IsNullOrEmpty(n)) return false;
                            for (int k = 0; k < snapshot.Count; k++)
                                if (snapshot[k] != null && snapshot[k].Name == n) return true;
                            return false;
                        };

                        var mirrorMesh = CreateBakedMirrorMesh(ctx, i, settings, nameExists);
                        if (mirrorMesh == null) continue;

                        Debug.Log($"[MQOImporter] BakeMirror={settings.BakeMirror} ctx={ctx.Name}");
                        if (settings.BakeMirror)
                        {
                            // ベイクドミラー: 独立メッシュ
                            // MeshType.BakedMirrorはCreateBakedMirrorMeshで設定済み
                            result.MeshContexts.Insert(i + 1, mirrorMesh);
                            ctx.HasBakedMirrorChild = true;
                            insertedCount++;
                            //Debug.Log($"[MQOImporter] Created baked mirror: {mirrorMesh.Name} (source: {ctx.Name})");
                        }
                        else
                        {
                            // MirrorPair: Real↔Mirror同期
                            // 名前は CreateDerivedMirrorContext が MirrorNameOps で決めている。
                            // ここで上書きしない（「左腕」のミラーは「右腕」）。
                            mirrorMesh.Type = MeshType.MirrorSide;
                            result.MeshContexts.Insert(i + 1, mirrorMesh);
                            insertedCount++;

                            // MirrorPairを構築
                            var pair = new MirrorPair
                            {
                                Real = ctx,
                                Mirror = mirrorMesh,
                                Axis = ctx.GetMirrorSymmetryAxis()
                            };

                            // PMX と同じ理由で、組み立て中の列を渡す
                            // （ParentModelContext はまだ null）。
                            // result.MeshContexts は先頭がボーン、以降がメッシュで、
                            // ModelContext へはこの順で Add される。
                            bool success = pair.Build(result.MeshContexts);
                            if (success)
                            {
                                result.MirrorPairs.Add(pair);
                                //Debug.Log($"[MQOImporter] MirrorPair built: '{ctx.Name}' ↔ '{mirrorMesh.Name}'\n{pair.BuildLog}");
                            }
                            else
                            {
                                // 失敗するとペアを登録しないので、このオブジェクトは
                                // ミラー同期が丸ごと効かなくなる。
                                Debug.LogWarning(
                                    $"[MQOImporter] MirrorPair build failed: '{ctx.Name}' ↔ '{mirrorMesh.Name}'"
                                    + " — このオブジェクトのミラー同期は無効になります"
                                    + $"\n{pair.BuildLog}");
                            }
                        }

                        // どちらの経路でも挿入済み。実体側を参照で覚えておく。
                        mirrorSourcePairs.Add((mirrorMesh, ctx));
                    }
                }

                // BakedMirrorSourceIndex を最終的なリスト位置で付け直す。
                // MeshContext は Equals を上書きしていないため IndexOf は参照一致で引ける。
                // 実体側が見つからない場合は -1（＝ベイクドミラーではない）に落とす。
                foreach (var entry in mirrorSourcePairs)
                {
                    entry.mirror.BakedMirrorSourceIndex = result.MeshContexts.IndexOf(entry.real);
                }

                if (insertedCount > 0)
                {
                    string mode = settings.BakeMirror ? "baked" : "mirror pair";
                    Debug.Log($"[MQOImporter] Created {insertedCount} {mode} meshes");
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
                // メッシュ部分のみ親子関係を計算（オフセット=ボーン数）
                var meshOnlyList = result.MeshContexts.GetRange(boneContextCount, result.MeshContexts.Count - boneContextCount);
                CalculateParentIndices(meshOnlyList, boneContextCount, settings.SetMeshHierarchyParent);
            }
            else
            {
                CalculateParentIndices(result.MeshContexts, 0, settings.SetMeshHierarchyParent);
            }

            // MQO の頂点は絶対座標なので、階層のワールド行列で割り戻してローカル化する。
            // 親子関係が確定した後、姿勢を書き換える処理（Tポーズ等）の前に行う。
            if (settings.ImportVerticesAsWorldSpace)
                LocalizeVerticesFromWorld(result.MeshContexts, boneContextCount);

            if (settings.AutoDetectMirrorBranchRoot)
                ApplyMirrorBranchRootByName(result.MeshContexts, boneContextCount);

            // 描画オブジェクトの種別を確定する。
            // ボーン索引のオフセット補正・CSVウェイト適用が全部終わったあとに 1 回だけ行う。
            // TPoseConverter は種別（IsSkinned）を見るため、その前に確定させておく。
            SkinKindOps.RecomputeAll(result.MeshContexts);

            // Tポーズ変換（オプション）
            if (settings.ConvertToTPose && boneContextCount > 0)
            {
                Debug.Log($"[MQOImporter] Converting to T-Pose...");
                TPoseConverter.ConvertToTPoseByBoneNames(result.MeshContexts);
            }
        }
    }
}