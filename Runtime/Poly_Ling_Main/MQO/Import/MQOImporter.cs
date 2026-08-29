// Assets/Editor/Poly_Ling/MQO/Import/MQOImporter.cs
// MQODocument → MeshObject/MeshUndoContext 変換
// SimpleMeshFactoryのデータ構造に変換

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

        /// <summary>インポートされたマテリアル参照リスト（正式形式）</summary>
        public List<MaterialReference> MaterialReferences { get; } = new List<MaterialReference>();

        /// <summary>
        /// インポートされたマテリアルリスト（MaterialReferencesから導出）
        /// </summary>
        /// <remarks>新規コードではMaterialReferencesを使用してください</remarks>
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

        /// <summary>
        /// ミラー側マテリアルのオフセット
        /// ミラー側マテリアルインデックス = 実体側インデックス + MirrorMaterialOffset
        /// </summary>
        public int MirrorMaterialOffset { get; set; } = 0;

        /// <summary>元のMQOドキュメント</summary>
        public MQODocument Document { get; set; }

        /// <summary>インポート統計</summary>
        public MQOImportStats Stats { get; } = new MQOImportStats();

        /// <summary>構築されたMirrorPairリスト</summary>
        public List<MirrorPair> MirrorPairs { get; } = new List<MirrorPair>();

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

                // 親インデックスがある場合のみオフセット。
                // ParentIndex は HierarchyParentIndex と同じ入れ物なので 1 回だけ足す。
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
                        var mirrorMesh = CreateBakedMirrorMesh(ctx, i, settings);
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
                            mirrorMesh.Type = MeshType.MirrorSide;
                            mirrorMesh.Name = ctx.Name + "+";
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

        /// <summary>
        /// MQO の絶対座標の頂点を、PolyLing のローカル座標へ変換する。
        ///
        /// メタセコイアのローカル座標（translation/rotation/scale）はピボットであって、
        /// 形状も子オブジェクトも動かさない（頂点は常に絶対座標）。
        /// 一方 PolyLing は world = 親のworld × ローカル で頂点を動かすため、
        /// 読んだままの頂点を入れると階層の深さぶんだけ位置がずれる。
        /// ここで各オブジェクトのワールド行列の逆を掛けて辻褄を合わせる。
        ///
        /// ローカル変換が単位のオブジェクトではワールド行列も単位なので何も起きない。
        /// ボーンは頂点を持たないため対象外。
        /// </summary>
        private static void LocalizeVerticesFromWorld(List<MeshContext> meshContexts, int boneContextCount)
        {
            if (meshContexts == null) return;

            int n = meshContexts.Count;
            var world = new Matrix4x4[n];
            var done  = new bool[n];

            // 親から順に解決する。リスト順が前後どちらでも拾えるよう収束するまで回す。
            for (int pass = 0; pass < n; pass++)
            {
                bool progressed = false;
                for (int i = 0; i < n; i++)
                {
                    if (done[i]) continue;

                    var mc = meshContexts[i];
                    if (mc == null)
                    {
                        world[i] = Matrix4x4.identity;
                        done[i]  = true;
                        progressed = true;
                        continue;
                    }

                    // ここで組むのは「鏡像を掛ける前」の階層ワールド。
                    // ミラー側の頂点は実体側の素直な鏡像 v_M = S·v_R として焼かれており、
                    // 実体側と同じ階層ワールドで割らないとこの関係が崩れる。
                    // 実効ワールド S·H·S を使うのは描画側だけ。
                    int p = mc.HierarchyParentIndex;
                    if (p >= 0 && p < n && p != i)
                    {
                        if (!done[p]) continue;
                        world[i] = world[p] * mc.LocalMatrix;
                    }
                    else
                    {
                        world[i] = mc.LocalMatrix;
                    }

                    done[i]    = true;
                    progressed = true;
                }
                if (!progressed) break;
            }

            int changed = 0;
            for (int i = boneContextCount; i < n; i++)
            {
                var mc = meshContexts[i];
                var mo = mc?.MeshObject;
                if (mo?.Vertices == null || mo.Vertices.Count == 0) continue;
                if (!done[i] || world[i].isIdentity) continue;

                Matrix4x4 inv = world[i].inverse;
                for (int v = 0; v < mo.Vertices.Count; v++)
                {
                    var vert = mo.Vertices[v];
                    if (vert == null) continue;
                    vert.Position = inv.MultiplyPoint3x4(vert.Position);
                }
                mo.InvalidatePositionCache();

                // OriginalPositions と UnityMesh を作り直した頂点に合わせる
                mc.OriginalPositions = (Vector3[])mo.Positions.Clone();
                mc.ApplyVertexPositionsToMesh();

                changed++;
            }

            if (changed > 0)
                Debug.Log($"[MQOImporter] 頂点をローカル化: {changed} オブジェクト" +
                          "（メタセコイアの頂点は絶対座標のため）");

            // 生成ミラーの頂点は実体側のローカル頂点から取り直す。
            // 絶対座標のまま鏡像を焼くと、鏡映 S と階層ワールド H が可換なとき
            // （ピボット x=0・回転なし）しか v_M = S·v_R が成り立たない。
            int rebaked = MirrorBranchOps.RebakeDerivedMirrorVertices(meshContexts);
            if (rebaked > 0)
                Debug.Log($"[MQOImporter] 生成ミラーの頂点をローカル座標で取り直し: {rebaked} オブジェクト");
        }

        // ミラー分岐ルートとみなす名前パターン
        private const string MirrorBranchNamePrefix = "@@";
        private const string MirrorBranchNameSuffix = "ミラー分岐ルート";

        /// <summary>
        /// 名前からミラー分岐ルートフラグを設定する。
        /// 接頭句「@@」かつ接尾句「ミラー分岐ルート」を持つメッシュが対象。
        /// ボーンは対象外（先頭 boneContextCount 件をスキップ）。
        /// </summary>
        private static void ApplyMirrorBranchRootByName(List<MeshContext> meshContexts, int boneContextCount)
        {
            if (meshContexts == null) return;

            int hit = 0;
            for (int i = boneContextCount; i < meshContexts.Count; i++)
            {
                var ctx = meshContexts[i];
                string name = ctx?.Name;
                if (string.IsNullOrEmpty(name)) continue;

                if (!name.StartsWith(MirrorBranchNamePrefix) ||
                    !name.EndsWith(MirrorBranchNameSuffix)) continue;

                ctx.IsMirrorBranchRoot = true;
                hit++;
            }

            if (hit > 0)
                Debug.Log($"[MQOImporter] ミラー分岐ルートを自動設定: {hit} 件");
        }

        /// <summary>
        /// Depth値から親子関係（ParentIndex）を計算
        /// MQOのDepth値はリスト順序に依存するため、インポート時に親子関係を確定させる
        /// 実装は MeshHierarchyOps.RecalculateParentIndicesFromDepth に集約している。
        /// </summary>
        /// <param name="meshContexts">対象のMeshContextリスト</param>
        /// <param name="indexOffset">グローバルインデックスへのオフセット（ボーン数）</param>
        /// <param name="setHierarchyParent">
        /// true のとき HierarchyParentIndex（GameObject階層の親）にも同じ値を設定する。
        /// ボーンは ConvertBonesToMeshContexts が既に設定済みで、ここではメッシュのみ扱う。
        /// </param>
        private static void CalculateParentIndices(
            List<MeshContext> meshContexts, int indexOffset = 0, bool setHierarchyParent = true)
        {
            MeshHierarchyOps.RecalculateParentIndicesFromDepth(
                meshContexts, indexOffset, setHierarchyParent);
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
                boneWorldPositions[i] = AxisFlipOps.Position(
                    settings.Flip, bone.Position, pmxScale * settings.Scale);
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
                    HasBoneTransform = true  // ★スキンドメッシュとして出力
                };
                meshObject.BoneTransform = boneTransform;

                // MeshContextを作成
                var meshContext = new MeshContext
                {
                    MeshObject = meshObject,
                    Name = bone.Name,  // 明示的に設定（MeshObject.Nameとは別）
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

        /// <summary>
        /// __Armature__からボーンをインポートした結果
        /// </summary>
        private class ArmatureImportResult
        {
            /// <summary>ボーンのMeshContextリスト（リスト順＝インデックス順）</summary>
            public List<MeshContext> BoneContexts { get; } = new List<MeshContext>();
            /// <summary>ボーン名→インデックスのマップ</summary>
            public Dictionary<string, int> BoneNameToIndex { get; } = new Dictionary<string, int>();
        }

        /// <summary>
        /// MQOの__Armature__オブジェクト以下をボーン構造としてインポート
        /// __ArmatureName__がある場合はそちらのリスト順を使用
        /// </summary>
        private static ArmatureImportResult ImportBonesFromArmature(List<MQOObject> objects, MQOImportSettings settings)
        {
            var result = new ArmatureImportResult();

            // __Armature__オブジェクトを探す
            int armatureIndex = -1;
            int armatureNameIndex = -1;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i].Name == "__Armature__")
                {
                    armatureIndex = i;
                }
                else if (objects[i].Name == "__ArmatureName__")
                {
                    armatureNameIndex = i;
                }
            }

            if (armatureIndex < 0)
            {
                return result;  // __Armature__がない
            }

            Debug.Log($"[MQOImporter] Found __Armature__ at index {armatureIndex}");

            // __Armature__以降のオブジェクトでdepth > 0のものをボーンとして収集
            // depth=0が出現したらボーン収集終了（__Armature__ツリーの終わり）
            var boneObjects = new List<MQOObject>();
            var boneObjectNames = new HashSet<string>();
            for (int i = armatureIndex + 1; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (obj.Depth == 0)
                {
                    break;  // __Armature__ツリー終了
                }
                boneObjects.Add(obj);
                boneObjectNames.Add(obj.Name);
            }

            if (boneObjects.Count == 0)
            {
                Debug.Log($"[MQOImporter] No bones found under __Armature__");
                return result;
            }

            // リスト順（インデックス順）を決定
            // __ArmatureName__がある場合はそちらを使用、なければ__Armature__の出現順
            var boneListOrder = new List<string>();
            const string armatureNamePrefix = "__ArmatureName__";

            if (armatureNameIndex >= 0)
            {
                Debug.Log($"[MQOImporter] Found __ArmatureName__ at index {armatureNameIndex}");

                // __ArmatureName__以降のオブジェクトでdepth=1のものをリスト順として収集
                for (int i = armatureNameIndex + 1; i < objects.Count; i++)
                {
                    var obj = objects[i];
                    if (obj.Depth == 0)
                    {
                        break;  // __ArmatureName__ツリー終了
                    }
                    if (obj.Depth == 1)
                    {
                        // __ArmatureName__プレフィックスを除去してボーン名を取得
                        string boneName = obj.Name;
                        if (boneName.StartsWith(armatureNamePrefix))
                        {
                            boneName = boneName.Substring(armatureNamePrefix.Length);
                        }
                        boneListOrder.Add(boneName);
                    }
                }
                Debug.Log($"[MQOImporter] Bone list order from __ArmatureName__: {boneListOrder.Count} bones");
            }
            else
            {
                // __ArmatureName__がない場合は__Armature__の出現順をリスト順とする
                foreach (var obj in boneObjects)
                {
                    boneListOrder.Add(obj.Name);
                }
                Debug.Log($"[MQOImporter] Using __Armature__ order as list order: {boneListOrder.Count} bones");
            }

            // ボーン名→リストインデックスのマップを作成
            var listOrderIndex = new Dictionary<string, int>();
            for (int i = 0; i < boneListOrder.Count; i++)
            {
                if (!listOrderIndex.ContainsKey(boneListOrder[i]))
                {
                    listOrderIndex[boneListOrder[i]] = i;
                }
            }

            // ボーン名→__Armature__内でのインデックスのマップを作成（親子関係解決用）
            var boneObjIndex = new Dictionary<string, int>();
            for (int i = 0; i < boneObjects.Count; i++)
            {
                boneObjIndex[boneObjects[i].Name] = i;
            }

            // Depthから親子関係を計算（__Armature__の下なのでdepth=1がルート）
            // スタック: (オブジェクトインデックス, Depth)
            var parentStack = new Stack<(int index, int depth)>();
            var parentIndices = new int[boneObjects.Count];  // __Armature__内でのインデックス

            for (int i = 0; i < boneObjects.Count; i++)
            {
                var obj = boneObjects[i];
                int depth = obj.Depth;

                while (parentStack.Count > 0 && parentStack.Peek().depth >= depth)
                {
                    parentStack.Pop();
                }

                if (parentStack.Count > 0)
                {
                    parentIndices[i] = parentStack.Peek().index;
                }
                else
                {
                    parentIndices[i] = -1;  // ルートボーン
                }

                parentStack.Push((i, depth));
            }

            // 各ボーンをMeshContextに変換（リスト順で格納）
            var boneContextsTemp = new MeshContext[boneListOrder.Count];

            for (int i = 0; i < boneObjects.Count; i++)
            {
                var obj = boneObjects[i];

                // このボーンのリスト順インデックスを取得
                if (!listOrderIndex.TryGetValue(obj.Name, out int listIdx))
                {
                    Debug.LogWarning($"[MQOImporter] Bone '{obj.Name}' not found in list order, skipping");
                    continue;
                }

                // 親のリスト順インデックスを計算
                int parentListIdx = -1;
                int parentObjIdx = parentIndices[i];
                if (parentObjIdx >= 0)
                {
                    string parentName = boneObjects[parentObjIdx].Name;
                    if (listOrderIndex.TryGetValue(parentName, out int pIdx))
                    {
                        parentListIdx = pIdx;
                    }
                }

                // MeshObjectを作成
                var meshObject = new MeshObject(obj.Name)
                {
                    Type = MeshType.Bone,
                    HierarchyParentIndex = parentListIdx
                };

                // MQOのtranslation/rotation/scaleを取得してBoneTransformに設定
                Vector3 translation = obj.Translation;
                Vector3 rotation = obj.Rotation;
                Vector3 scale = obj.Scale;

                // 位置にスケールと軸反転を適用
                Vector3 localPosition = AxisFlipOps.Position(settings.Flip, translation, settings.Scale);

                // 回転。MQO の rotation は XYZ ではなく HPB なので並べ替える
                rotation = MQOLocalRotationOps.ToUnityEuler(rotation, settings.Flip);

                // BoneTransformを設定
                var boneTransform = new BoneTransform
                {
                    Position = localPosition,
                    Rotation = rotation,
                    Scale = scale,
                    UseLocalTransform = true,
                    HasBoneTransform = true
                };
                meshObject.BoneTransform = boneTransform;

                // MeshContextを作成（BindPoseは後で設定）
                var meshContext = new MeshContext
                {
                    MeshObject = meshObject,
                    Name = obj.Name,
                    Type = MeshType.Bone,
                    IsVisible = obj.IsVisible,
                    BoneTransform = boneTransform
                };

                meshContext.HierarchyParentIndex = parentListIdx;

                boneContextsTemp[listIdx] = meshContext;
            }

            // nullでない要素をリストに追加
            for (int i = 0; i < boneContextsTemp.Length; i++)
            {
                if (boneContextsTemp[i] != null)
                {
                    result.BoneContexts.Add(boneContextsTemp[i]);
                    result.BoneNameToIndex[boneContextsTemp[i].Name] = result.BoneContexts.Count - 1;
                }
            }

            // BindPoseを計算（ModelContext共通メソッド）
            ModelContext.ComputeBindPosesFromList(result.BoneContexts);

            Debug.Log($"[MQOImporter] Imported {result.BoneContexts.Count} bones from __Armature__");
            return result;
        }

        /// <summary>
        /// MQOの__IK__セクションからIK情報を読み取り、ボーンに適用
        /// 構造: __IK__ → __IK__ボーン名 (depth=1) → __IKTarget__ターゲット名 (depth=2), __IKLink__リンク名 (depth=2)
        /// </summary>
        private static void ApplyIKFromObjects(
            List<MQOObject> objects,
            List<MeshContext> boneContexts,
            Dictionary<string, int> boneNameToIndex)
        {
            if (boneNameToIndex == null || boneNameToIndex.Count == 0) return;

            // __IK__ルートオブジェクトを探す
            int ikRootIndex = -1;
            for (int i = 0; i < objects.Count; i++)
            {
                if (objects[i].Name == "__IK__")
                {
                    ikRootIndex = i;
                    break;
                }
            }
            if (ikRootIndex < 0) return;

            // __IK__以降を走査
            const string ikPrefix = "__IK__";
            const string targetPrefix = "__IKTarget__";
            const string linkPrefix = "__IKLink__";

            int ikCount = 0;
            for (int i = ikRootIndex + 1; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (obj.Depth == 0) break;  // __IK__ツリー終了

                // depth=1: IKボーン
                if (obj.Depth == 1 && obj.Name.StartsWith(ikPrefix))
                {
                    string ikBoneName = obj.Name.Substring(ikPrefix.Length);
                    if (!boneNameToIndex.TryGetValue(ikBoneName, out int ikBoneIdx)) continue;
                    if (ikBoneIdx < 0 || ikBoneIdx >= boneContexts.Count) continue;

                    var ikBone = boneContexts[ikBoneIdx];
                    ikBone.IsIK = true;
                    ikBone.IKLoopCount = 40;      // デフォルト値
                    ikBone.IKLimitAngle = 2.0f;   // デフォルト値（ラジアン）
                    ikBone.IKLinks = new List<IKLinkInfo>();

                    // depth=2の子オブジェクト（Target, Link）を収集
                    for (int j = i + 1; j < objects.Count; j++)
                    {
                        var child = objects[j];
                        if (child.Depth <= 1) break;  // このIKボーンの子ツリー終了

                        if (child.Name.StartsWith(targetPrefix))
                        {
                            string targetName = child.Name.Substring(targetPrefix.Length);
                            if (boneNameToIndex.TryGetValue(targetName, out int targetIdx))
                            {
                                ikBone.IKTargetIndex = targetIdx;
                            }
                        }
                        else if (child.Name.StartsWith(linkPrefix))
                        {
                            string linkName = child.Name.Substring(linkPrefix.Length);
                            if (boneNameToIndex.TryGetValue(linkName, out int linkIdx))
                            {
                                ikBone.IKLinks.Add(new IKLinkInfo
                                {
                                    BoneIndex = linkIdx,
                                    HasLimit = false
                                });
                            }
                        }
                    }

                    ikCount++;
                    Debug.Log($"[MQOImporter] IK: '{ikBoneName}' target={ikBone.IKTargetIndex}, links={ikBone.IKLinks.Count}");
                }
            }

            if (ikCount > 0)
            {
                Debug.Log($"[MQOImporter] Imported {ikCount} IK bones from __IK__");
            }
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
            meshObject.Type = MeshType.Mesh;  // 明示的に設定

            // MQO は形式として頂点法線を持たず、読み込み時に必ずスムージング角から
            // 計算する（下の NormalMode 分岐を参照）。保持すべき元の法線が存在しない
            // ため、自動計算を有効にする（= PreserveNormals を false にする）。
            //
            // NormalMode.Unity の場合は ToUnityMesh 側の RecalculateNormals に委ねるが、
            // そちらは PreserveNormals で gate されている（MeshBridgeDefault ほか）ため、
            // MeshObject の既定値 true のままだと法線が一切生成されない。
            // よって NormalMode に依存させず無条件に設定する。
            meshObject.PreserveNormals = false;

            // 頂点変換（IDは後で設定）
            foreach (var mqoVert in mqoObj.Vertices)
            {
                Vector3 pos = ConvertPosition(mqoVert.Position, settings);
                var vertex = new Vertex(pos);
                vertex.Id = -1;  // 初期値: IDなし
                meshObject.AddVertexRaw(vertex);  // ID管理なしで追加
                stats.TotalVertices++;
            }

            // 特殊面から頂点の識別子を抽出（VertexIdHelper使用）
            // 三角形特殊面の COL を (PartsID, SubID, ID) として読む。
            // COL の値によるパターン判定は行わない。
            var vertexIdMap = VertexIdHelper.ExtractIdsFromSpecialFaces(mqoObj.Faces);
            foreach (var kvp in vertexIdMap)
            {
                int vertIndex = kvp.Key;
                var ids = kvp.Value;

                if (vertIndex >= 0 && vertIndex < meshObject.Vertices.Count)
                {
                    var vertex = meshObject.Vertices[vertIndex];
                    vertex.Id      = ids.Id;
                    vertex.SubId   = ids.SubId;
                    vertex.PartsId = ids.PartsId;
                    meshObject.RegisterVertexId(ids.Id);
                }
            }

            // 特殊面の数をカウント
            stats.SkippedSpecialFaces += mqoObj.Faces.Count(f => f.IsSpecialFace);

            // 四角形特殊面からボーンウェイトを抽出（VertexIdHelper使用）
            // 実体側ウェイト（UV[3].y == 0）
            if (!settings.SkipMqoBoneIndices || !settings.SkipMqoBoneWeights)
            {
                //辞書形式で取得（頂点インデックス → ボーンウェイト情報）
                var boneWeightMap = VertexIdHelper.ExtractBoneWeightsFromSpecialFaces(mqoObj.Faces);
                foreach (var kvp in boneWeightMap)
                {
                    int vertIndex = kvp.Key;
                    var bw = kvp.Value;

                    if (vertIndex >= 0 && vertIndex < meshObject.Vertices.Count)
                    {
                        //Debug.Log($"[MQOImporter] Applying bone weight from special face: vertexIndex={vertIndex}, boneIndices=({bw.BoneIndex0},{bw.BoneIndex1},{bw.BoneIndex2},{bw.BoneIndex3}), weights=({bw.Weight0},{bw.Weight1},{bw.Weight2},{bw.Weight3})");
                        var vertex = meshObject.Vertices[vertIndex];
                        vertex.BoneWeight = new BoneWeight
                        {
                            boneIndex0 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex0,
                            boneIndex1 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex1,
                            boneIndex2 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex2,
                            boneIndex3 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex3,
                            weight0 = settings.SkipMqoBoneWeights ? 0f : bw.Weight0,
                            weight1 = settings.SkipMqoBoneWeights ? 0f : bw.Weight1,
                            weight2 = settings.SkipMqoBoneWeights ? 0f : bw.Weight2,
                            weight3 = settings.SkipMqoBoneWeights ? 0f : bw.Weight3
                        };
                    }
                }

                // ミラー側ウェイト（UV[3].y == 1）
                var mirrorBoneWeightMap = VertexIdHelper.ExtractMirrorBoneWeightsFromSpecialFaces(mqoObj.Faces);
                foreach (var kvp in mirrorBoneWeightMap)
                {
                    int vertIndex = kvp.Key;
                    var bw = kvp.Value;

                    if (vertIndex >= 0 && vertIndex < meshObject.Vertices.Count)
                    {
                        var vertex = meshObject.Vertices[vertIndex];
                        vertex.MirrorBoneWeight = new BoneWeight
                        {
                            boneIndex0 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex0,
                            boneIndex1 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex1,
                            boneIndex2 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex2,
                            boneIndex3 = settings.SkipMqoBoneIndices ? 0 : bw.BoneIndex3,
                            weight0 = settings.SkipMqoBoneWeights ? 0f : bw.Weight0,
                            weight1 = settings.SkipMqoBoneWeights ? 0f : bw.Weight1,
                            weight2 = settings.SkipMqoBoneWeights ? 0f : bw.Weight2,
                            weight3 = settings.SkipMqoBoneWeights ? 0f : bw.Weight3
                        };
                    }
                }
            }

            // 面変換
            // SmoothFacet モードではスロット確保を後段（NormalSmoothingOps）へ委ねるため、
            // ここでは面コーナーのUVだけを保持しておく（faceCornerUVs は Faces と同じ添字）。
            bool useFacetPath = settings.NormalMode == NormalMode.SmoothFacet;
            List<Vector2[]> faceCornerUVs = useFacetPath ? new List<Vector2[]>() : null;

            foreach (var mqoFace in mqoObj.Faces)
            {
                // 特殊面（メタデータ）はスキップ（既に処理済み）
                if (mqoFace.IsSpecialFace)
                    continue;

                // 1頂点（点）、2頂点（線）は補助線として扱う
                if (mqoFace.VertexCount < 3)
                {
                    ConvertLine(mqoFace, meshObject, settings);
                    if (useFacetPath)
                    {
                        while (faceCornerUVs.Count < meshObject.FaceCount)
                            faceCornerUVs.Add(null);
                    }
                    continue;
                }

                // 3頂点以上は面として変換
                if (useFacetPath)
                {
                    var cornerUVs = ConvertFaceDeferred(mqoFace, meshObject, settings);
                    while (faceCornerUVs.Count < meshObject.FaceCount - 1)
                        faceCornerUVs.Add(null);
                    faceCornerUVs.Add(cornerUVs);
                }
                else
                {
                    ConvertFace(mqoFace, meshObject, settings);
                }
                stats.TotalFaces++;
            }

            if (useFacetPath && faceCornerUVs.Count != meshObject.FaceCount)
            {
                Debug.LogError($"[MQOImporter] faceCornerUVs 不整合 obj=\"{mqoObj.Name}\" " +
                               $"cornerUVs={faceCornerUVs.Count} faces={meshObject.FaceCount}");
                while (faceCornerUVs.Count < meshObject.FaceCount)
                    faceCornerUVs.Add(null);
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

            // MQOオブジェクトのTRS（translation/rotation/scale）をBoneTransformに設定する。
            // ComputeWorldMatrices() が LocalMatrix → WorldMatrix を計算するために必要。
            // デフォルト値（位置ゼロ・回転ゼロ・スケール1）の場合は設定しない
            // （UseLocalTransform=false のまま → LocalMatrix=identity → WorldMatrix=identity）。
            // エディタ側 PolyLing_MeshLoad の MeshFilter 処理と同じ判定ロジック。
            {
                Vector3 translationScaled = AxisFlipOps.Position(settings.Flip, mqoObj.Translation, settings.Scale);

                // 回転。MQO の rotation は XYZ ではなく HPB なので並べ替える
                Vector3 rotationConverted = MQOLocalRotationOps.ToUnityEuler(mqoObj.Rotation, settings.Flip);

                bool isDefaultTransform =
                    translationScaled == Vector3.zero &&
                    mqoObj.Rotation == Vector3.zero &&
                    mqoObj.Scale == Vector3.one;

                if (!isDefaultTransform)
                {
                    var meshBoneTransform = new BoneTransform
                    {
                        Position = translationScaled,
                        Rotation = rotationConverted,
                        Scale    = mqoObj.Scale,
                        UseLocalTransform = true,
                    };
                    meshContext.BoneTransform = meshBoneTransform;
                }
            }

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

            // 法線スムージング
            if (settings.NormalMode == NormalMode.SmoothFacet)
            {
                // オブジェクト単位の shading / facet を使う（UseMqoFacet=false なら設定値で上書き）
                bool  flatShading = settings.UseMqoFacet && mqoObj.Shading == 0;
                float facetAngle  = settings.UseMqoFacet ? mqoObj.Facet : settings.SmoothingAngle;

                NormalSmoothingOps.ApplyFacetSmoothing(
                    meshObject, faceCornerUVs, facetAngle, flatShading, mqoObj.Name);
            }
            else if (settings.NormalMode == NormalMode.Smooth)
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
                // 法線サブindexはUVサブindexと同値に保つ（不変条件）。
                // 0固定にすると、AddOrGetUVIndex が確保したスロット1以降へ法線が書かれない。
                face.NormalIndices.Add(uvSubIndices[i]);
            }

            meshObject.Faces.Add(face);

            // 法線計算
            CalculateFaceNormal(face, meshObject);
        }

        /// <summary>
        /// SmoothFacet モード用の面変換。
        /// UV/法線スロットはまだ確保せず（AddOrGetUVIndex を呼ばない）、
        /// 面コーナーのUVだけを返す。スロット確保は NormalSmoothingOps が行う。
        /// </summary>
        private static Vector2[] ConvertFaceDeferred(
            MQOFace mqoFace, MeshObject meshObject, MQOImportSettings settings)
        {
            int vertexCount = mqoFace.VertexCount;

            var cornerUVs = new Vector2[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                cornerUVs[i] = (mqoFace.UVs != null && i < mqoFace.UVs.Length)
                    ? ConvertUV(mqoFace.UVs[i], settings)
                    : Vector2.zero;
            }

            var face = new Face
            {
                MaterialIndex = mqoFace.MaterialIndex >= 0 ? mqoFace.MaterialIndex : 0
            };

            for (int i = 0; i < vertexCount; i++)
            {
                face.VertexIndices.Add(mqoFace.VertexIndices[i]);
                // スロット番号は後段で確定させる。ここでは仮に 0 を入れておく。
                face.UVIndices.Add(0);
                face.NormalIndices.Add(0);
            }

            meshObject.Faces.Add(face);

            return cornerUVs;
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

            // アルファ処理（4ケース分岐）
            bool hasLowOpacity = color.a < 1f - 0.001f;
            bool hasTexture = !string.IsNullOrEmpty(mqoMat.TexturePath);

            if (hasLowOpacity && hasTexture)
            {
                // ケース4: 材質不透明度 < 1.0 かつ テクスチャあり（競合）
                if (settings.AlphaConflict == AlphaConflictMode.PreferTransparent)
                {
                    SetMaterialTransparent(material);
                }
                else
                {
                    SetMaterialAlphaClip(material, settings.AlphaCutoff);
                }
            }
            else if (hasLowOpacity)
            {
                // ケース1: 材質不透明度 < 1.0、テクスチャなし → Transparent
                SetMaterialTransparent(material);
            }
            else if (hasTexture)
            {
                // ケース2: 不透明度 = 1.0、テクスチャあり → AlphaClip
                SetMaterialAlphaClip(material, settings.AlphaCutoff);
            }
            // ケース3: 不透明度 = 1.0、テクスチャなし → Opaque, AlphaClip=OFF（何もしない）





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

            // ソースパスを絶対パスで設定
            if (!string.IsNullOrEmpty(mqoMat.TexturePath))
            {
                matRef.Data.SourceTexturePath = ResolveTexturePath(mqoMat.TexturePath, settings.BaseDir);
            }
            if (!string.IsNullOrEmpty(mqoMat.AlphaMapPath))
            {
                matRef.Data.SourceAlphaMapPath = ResolveTexturePath(mqoMat.AlphaMapPath, settings.BaseDir);
            }
            if (!string.IsNullOrEmpty(mqoMat.BumpMapPath))
            {
                matRef.Data.SourceBumpMapPath = ResolveTexturePath(mqoMat.BumpMapPath, settings.BaseDir);
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
        /// マテリアルをアルファクリップモードに設定（URP/Standard両対応）
        /// Opaque + AlphaTest で、しきい値以下のピクセルを切り抜く
        /// </summary>
        private static void SetMaterialAlphaClip(Material material, float cutoff)
        {
            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 1f);
                material.EnableKeyword("_ALPHATEST_ON");
            }
            if (material.HasProperty("_Cutoff"))
            {
                material.SetFloat("_Cutoff", cutoff);
            }
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
                texture = PLEditorBridge.I.LoadAssetAtPath<Texture2D>(assetPath);
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

                string[] guids = PLEditorBridge.I.FindAssets($"t:Texture2D {fileNameWithoutExt}",
                    new[] { searchFolder });
                foreach (var guid in guids)
                {
                    string foundPath = PLEditorBridge.I.GUIDToAssetPath(guid);
                    if (Path.GetFileName(foundPath).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        texture = PLEditorBridge.I.LoadAssetAtPath<Texture2D>(foundPath);
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
            // MQO座標系 → Unity座標系。規則は AxisFlipOps に集約。
            return AxisFlipOps.Position(settings.Flip, mqoPos, settings.Scale);
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

            Vector3 normal = NormalHelper.CalculateFaceNormal(p0, p1, p2);

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
            //Debug.Log($"[MQOImporter normal] Smooth normals calculated (angle={smoothingAngle}°)");
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
                faceNormals[fi] = NormalHelper.CalculateFaceNormal(p0, p1, p2);
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
                    //Debug.Log($"[MQOImporter] Smooth normals calculated (baseFaceNormal={smoothedNormal}°, positions={vertInFace})");

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
                    //Debug.Log($"[MQOImporter] Smooth normals calculated (normal={smoothedNormal}°, positions={vertInFace})");


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
        /// <summary>
        /// ミラー属性を持つメッシュからミラー側 MeshContext を生成する。
        /// 実装は MirrorBranchOps.CreateDerivedMirrorContext にある
        /// （ミラーの有効化からも呼ぶため Ops へ移した）。
        /// </summary>
        private static MeshContext CreateBakedMirrorMesh(MeshContext source, int sourceIndex, MQOImportSettings settings)
        {
            return Poly_Ling.Ops.MirrorBranchOps.CreateDerivedMirrorContext(source, sourceIndex);
        }

    }
}