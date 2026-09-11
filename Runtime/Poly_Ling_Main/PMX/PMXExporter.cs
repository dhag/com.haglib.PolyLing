// Assets/Editor/Poly_Ling/PMX/Export/PMXExporter.cs
// MeshContext → PMXDocument変換 & ファイル出力


///ミラー対応は後回し中。VertexHelperを使うときに考慮する。
//
// 【分割先】このファイルから次へ分けてある。
//   PMXExportResult.cs    PMX エクスポートの結果（PMXExporter から分離）。
//   PMXExporter.Mesh.cs   PMX エクスポート：メッシュ変換と座標変換。
//   PMXExporter.Morph.cs  PMX エクスポート：モーフと剛体・JOINT のエクスポート。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.Materials;
using Poly_Ling.MeshBridge;

namespace Poly_Ling.PMX
{

    /// <summary>
    /// PMXエクスポーター
    /// </summary>
    public static partial class PMXExporter
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
                // モデル情報は取り込み時に保持したものを使う。
                // 無い場合（新規作成など）だけモデル名で埋める。
                // モデル情報は取り込み時に保持したものをそのまま使う。
                // 英語名も元が空なら空で書く（名前で埋めると往復で書き換わる）。
                // PMX 由来でない場合（新規作成など）だけモデル名で埋める。
                ModelInfo = model.PmxModelInfo != null
                    ? new PMXModelInfo
                    {
                        Name           = model.PmxModelInfo.Name           ?? "",
                        NameEnglish    = model.PmxModelInfo.NameEnglish    ?? "",
                        Comment        = model.PmxModelInfo.Comment        ?? "",
                        CommentEnglish = model.PmxModelInfo.CommentEnglish ?? ""
                    }
                    : new PMXModelInfo
                    {
                        Name           = model.Name ?? "Exported Model",
                        NameEnglish    = model.Name ?? "Exported Model",
                        Comment        = "",
                        CommentEnglish = ""
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
                ConvertBones(boneContexts, meshContexts, document, settings);
            }

            // マテリアルを変換
            if (settings.ExportMaterials)
            {
                ConvertMaterials(model.MaterialReferences, document, settings);
            }

            // メッシュを変換（頂点・面）
            var vertexMaps = ConvertMeshes(meshOnlyContexts, document, boneNameToIndex, settings);

            // モーフを出力する。取り込み時に保持した内容から作り直し、
            // PolyLing が扱わない種類は元の PMX からそのまま写す。
            ConvertMorphs(model, document, vertexMaps, settings);

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
            List<MeshContext> allContexts,
            PMXDocument document,
            PMXExportSettings settings)
        {
            // ボーン名→インデックスマップ
            var boneNameToIndex = new Dictionary<string, int>();
            for (int i = 0; i < boneContexts.Count; i++)
            {
                boneNameToIndex[boneContexts[i].Name] = i;
            }

            // HierarchyParentIndex はモデル全体の MeshContextList 上の索引であって、
            // ボーンだけを抜き出したリスト上の索引ではない（PMXImporter.cs:145-165 の
            // ApplyMeshContextIndexOffset がメッシュ分のオフセットを足している）。
            // 直接 boneContexts[] を引くと範囲外になり、親が全部 -1 になる。
            var fullToBone = new Dictionary<int, int>();
            if (allContexts != null)
            {
                for (int i = 0; i < allContexts.Count; i++)
                {
                    var ctx = allContexts[i];
                    if (ctx == null || ctx.Type != MeshType.Bone) continue;
                    int bi = boneContexts.IndexOf(ctx);
                    if (bi >= 0) fullToBone[i] = bi;
                }
            }

            foreach (var ctx in boneContexts)
            {
                // ワールド位置を計算（LocalMatrixの累積）
                Vector3 worldPosition = ComputeBoneWorldPosition(ctx, boneContexts, fullToBone);

                // 座標変換
                Vector3 pmxPosition = ConvertPosition(worldPosition, settings);

                // 親ボーン名を取得
                string parentName = "";
                if (fullToBone.TryGetValue(ctx.HierarchyParentIndex, out int parentBoneIdx))
                {
                    parentName = boneContexts[parentBoneIdx].Name;
                }
                else if (ctx.HierarchyParentIndex >= 0 && ctx.HierarchyParentIndex < boneContexts.Count)
                {
                    // 全体リストを渡せない経路のための保険
                    parentName = boneContexts[ctx.HierarchyParentIndex].Name;
                }

                // PMX 固有欄は取り込み時に保持したものを使う。
                // 無い場合（PolyLing で作ったボーン）だけ従来の既定値で埋める。
                var extra = ctx.MeshObject?.PmxBone;

                var pmxBone = new PMXBone
                {
                    Name = ctx.Name,
                    NameEnglish    = extra?.NameEnglish ?? ctx.Name,
                    Position       = pmxPosition,
                    ParentBoneName = parentName,
                    TransformLevel = extra?.TransformLevel ?? 0,
                    Flags          = extra?.Flags ?? (0x0001 | 0x0002 | 0x0004 | 0x0008),
                    ConnectOffset  = extra?.ConnectOffset ?? Vector3.zero
                };

                if (extra != null)
                {
                    pmxBone.ConnectBoneName           = extra.ConnectBoneName;
                    pmxBone.GrantParentBoneName       = extra.GrantParentBoneName;
                    pmxBone.GrantRate                 = extra.GrantRate;
                    pmxBone.FixedAxis                 = extra.FixedAxis;
                    pmxBone.LocalAxisX                = extra.LocalAxisX;
                    pmxBone.LocalAxisZ                = extra.LocalAxisZ;
                    pmxBone.IsLocalAxisAutoCalculated = extra.IsLocalAxisAutoCalculated;
                    pmxBone.ExternalParentKey         = extra.ExternalParentKey;

                    // IK ビットは IKData の有無で立て直す（下の IK 設定に任せる）。
                    pmxBone.Flags &= ~0x0020;
                }

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
                        // 角度制限の座標系変換（Unity → PMX）。
                        // PMXImporter.ConvertBone と同じ AxisFlipOps.AngleLimits を通す。
                        // この変換は自己逆元なので、取込と同じ処理がそのまま逆変換になる。
                        // 通さないと Unity 空間の値がそのまま PMX に書き出され、
                        // 往復でひざの曲がる向きが反転する。
                        Vector3 limMin = link.LimitMin;
                        Vector3 limMax = link.LimitMax;
                        AxisFlipOps.AngleLimits(settings.Flip, ref limMin, ref limMax);

                        var pmxLink = new PMXIKLink
                        {
                            BoneIndex = link.BoneIndex,
                            HasLimit = link.HasLimit,
                            LimitMin = limMin,
                            LimitMax = limMax
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

            // 名前で持っている参照を番号にも入れておく。
            // ライタは名前で引き直すが、番号しか見ない読み手のために両方を埋める。
            ResolveBoneReferences(document);

            Debug.Log($"[PMXExporter] Converted {document.Bones.Count} bones");
        }

        /// <summary>
        /// ボーンが持つ他ボーンへの参照を、名前から番号へ解決する。
        /// 名前を主、番号を従とする（JointData.cs の規約）。
        /// </summary>
        private static void ResolveBoneReferences(PMXDocument document)
        {
            var index = new Dictionary<string, int>();
            for (int i = 0; i < document.Bones.Count; i++)
            {
                string n = document.Bones[i]?.Name;
                if (!string.IsNullOrEmpty(n) && !index.ContainsKey(n)) index[n] = i;
            }

            int Lookup(string name, int fallback)
                => (!string.IsNullOrEmpty(name) && index.TryGetValue(name, out int i)) ? i : fallback;

            foreach (var bone in document.Bones)
            {
                if (bone == null) continue;

                bone.ParentIndex       = Lookup(bone.ParentBoneName,      bone.ParentIndex);
                bone.ConnectBoneIndex  = Lookup(bone.ConnectBoneName,     bone.ConnectBoneIndex);
                bone.GrantParentIndex  = Lookup(bone.GrantParentBoneName, bone.GrantParentIndex);
                bone.IKTargetIndex     = Lookup(bone.IKTargetBoneName,    bone.IKTargetIndex);

                if (bone.IKLinks == null) continue;
                foreach (var link in bone.IKLinks)
                {
                    if (link == null) continue;
                    link.BoneIndex = Lookup(link.BoneName, link.BoneIndex);
                }
            }
        }

        private static Vector3 ComputeBoneWorldPosition(
            MeshContext ctx,
            List<MeshContext> boneContexts,
            Dictionary<int, int> fullToBone)
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

                if (fullToBone.TryGetValue(current.HierarchyParentIndex, out int bi))
                {
                    current = boneContexts[bi];
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
            List<MaterialReference> materialRefs,
            PMXDocument document,
            PMXExportSettings settings)
        {
            if (materialRefs == null) return;

            foreach (var matRef in materialRefs)
            {
                if (matRef == null) continue;

                var data = matRef.Data;
                string name = matRef.Name ?? "Material";

                Color  diffuse     = data?.GetBaseColor() ?? Color.white;
                string texturePath = data?.BaseMapPath ?? "";

                // アセットパスしか持たない場合はそこからファイル名を作る
                if (string.IsNullOrEmpty(texturePath) && !string.IsNullOrEmpty(data?.SourceTexturePath))
                {
                    texturePath = settings.UseRelativeTexturePath
                        ? Path.GetFileName(data.SourceTexturePath)
                        : data.SourceTexturePath;
                }
                else if (!string.IsNullOrEmpty(texturePath) && settings.UseRelativeTexturePath == false)
                {
                    // 相対指定を切っている場合はそのまま使う
                }

                // PMX 固有欄は取り込み時に保持したものを使う。
                // 無い場合（PolyLing で作った材質）だけ従来の既定値で埋める。
                var extra = data?.Pmx;

                var pmxMat = new PMXMaterial
                {
                    Name          = name,

                    // 英語名は取り込み時の値をそのまま。元が空なら空で書く。
                    // 名前で埋めると、英語名を持たない PMX が往復のたびに
                    // 日本語名で上書きされる。PMX 由来でない材質だけ名前で埋める。
                    NameEnglish   = extra != null ? (extra.NameEnglish ?? "") : name,
                    Diffuse       = diffuse,
                    Specular      = extra?.GetSpecular()  ?? Color.white,
                    SpecularPower = extra?.SpecularPower  ?? 5f,
                    Ambient       = extra?.GetAmbient()   ?? new Color(0.5f, 0.5f, 0.5f),
                    DrawFlags     = extra?.DrawFlags      ?? 0,
                    EdgeColor     = extra?.GetEdgeColor() ?? Color.black,
                    EdgeSize      = extra?.EdgeSize       ?? 1f,
                    TexturePath   = texturePath,

                    SphereTexturePath = extra?.SphereTexturePath ?? "",
                    SphereMode        = extra?.SphereMode        ?? 0,
                    SharedToon        = extra?.SharedToon        ?? false,
                    ToonTextureIndex  = extra?.ToonTextureIndex  ?? -1,
                    ToonTexturePath   = extra?.ToonTexturePath   ?? ""
                };

                document.Materials.Add(pmxMat);
            }

            Debug.Log($"[PMXExporter] Converted {document.Materials.Count} materials");
        }
    }
}
