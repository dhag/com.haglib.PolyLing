// PMXImporter.Morph.cs
// PMX インポート：モーフ変換と剛体・JOINT のインポート。
// Runtime/Poly_Ling_Main/PMX/ に配置

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
    public static partial class PMXImporter
    {
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
        /// <summary>
        /// PolyLingメタUVモーフ（__PLM_ プレフィックス）を適用する。
        /// 各MeshContextの頂点IDとUVサブインデックスを復元する。
        /// </summary>
        private static void ApplyPolyLingMetaMorphs(PMXDocument document, PMXImportResult result)
        {
            foreach (var pmxMorph in document.Morphs)
            {
                if (pmxMorph.MorphType != 3) continue;
                if (pmxMorph.Name?.StartsWith("__PLM_") != true) continue;

                // 対象MeshContextを特定（MaterialGroupInfoのPmxToLocalIndexで検索）
                foreach (var offset in pmxMorph.Offsets)
                {
                    if (offset is not PMXUVMorphOffset uvOffset) continue;

                    int pmxVertexIndex = uvOffset.VertexIndex;
                    int uvSubIndex = Mathf.RoundToInt(uvOffset.Offset.y);
                    int localIndex = Mathf.RoundToInt(uvOffset.Offset.z);
                    int vertexId = Mathf.RoundToInt(uvOffset.Offset.w);

                    // このPMX頂点インデックスが属するMeshContextを探す
                    for (int gi = 0; gi < result.MaterialGroupInfos.Count; gi++)
                    {
                        var groupInfo = result.MaterialGroupInfos[gi];
                        if (!groupInfo.PmxToLocalIndex.TryGetValue(pmxVertexIndex, out int mappedLocal)) continue;
                        // mappedLocalはインポート時に再構築されたローカルインデックス
                        // localIndexはエクスポート時のMeshObjectインデックス（一致するはず）

                        if (groupInfo.MeshContextIndex < 0 || groupInfo.MeshContextIndex >= result.MeshContexts.Count) break;
                        var meshContext = result.MeshContexts[groupInfo.MeshContextIndex];
                        if (meshContext?.MeshObject == null) break;
                        var meshObject = meshContext.MeshObject;

                        // 頂点IDを復元
                        if (mappedLocal < meshObject.VertexCount)
                        {
                            meshObject.Vertices[mappedLocal].Id = vertexId;
                            meshObject.RegisterVertexId(vertexId);
                        }

                        // 面コーナーのUVサブインデックスを復元
                        foreach (var face in meshObject.Faces)
                        {
                            for (int ci = 0; ci < face.VertexIndices.Count; ci++)
                            {
                                if (face.VertexIndices[ci] == mappedLocal)
                                {
                                    while (face.UVIndices.Count <= ci)
                                        face.UVIndices.Add(0);
                                    face.UVIndices[ci] = uvSubIndex;
                                }
                            }
                        }
                        break;
                    }
                }
            }
        }

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
                    // PolyLingメタモーフはApplyPolyLingMetaMorphsで処理するのでスキップ
                    if (pmxMorph.Name?.StartsWith("__PLM_") == true) continue;
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

                    // 子モーフの名前は PMX ドキュメントから取る。
                    // 取り込めた MorphExpression の名前から取ると、オフセットが
                    // 1 件も無い空モーフの子が記録から漏れる（実測: まばたきの子
                    // まばたき_MD / まばたき_ME が両方 0 件で、4 → 2 に減っていた）。
                    string childNameForGroup =
                        (childMorphIndex >= 0 && childMorphIndex < document.Morphs.Count)
                            ? document.Morphs[childMorphIndex].Name
                            : groupOffset.MorphName;

                    if (!string.IsNullOrEmpty(childNameForGroup))
                        groupSet.GroupChildren.Add(new MorphGroupChild(childNameForGroup, groupOffset.Weight));

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

                // 子がすべて空モーフでもグループ自体は残す。
                // 落とすと書き戻しでグループモーフが丸ごと消える。
                if (groupSet.MeshCount > 0 || groupSet.GroupChildren.Count > 0)
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

                // どの描画オブジェクトから作ったかを残す。
                // 書き出しでオフセットを頂点番号へ写すときにこの名前で引く。
                if (morphMesh.MorphBaseData != null)
                    morphMesh.MorphBaseData.BaseMeshName = baseMesh.Name;

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

                // どの描画オブジェクトから作ったかを残す。
                // 書き出しでオフセットを頂点番号へ写すときにこの名前で引く。
                if (morphMesh.MorphBaseData != null)
                    morphMesh.MorphBaseData.BaseMeshName = baseMesh.Name;

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

        // ================================================================
        // 剛体・JOINT インポート（段階②）
        // ================================================================
        //
        // 【方針】
        //   剛体/JOINTを頂点ゼロの空 MeshObject（Type=RigidBody / RigidBodyJoint）
        //   ＋付帯POCO（RigidBodyData / JointData）として取り込み、ボーン・モーフと
        //   同様に result.MeshContexts へ追加する。形状はギズモとして利用時に生成し、
        //   ここではジオメトリを持たせない（Drawableカテゴリ外のためGPU構築に入らない）。
        //
        // 【参照系：name主・index従】
        //   剛体→関連ボーン：RelatedBoneName（PMXReaderで解決済み）。BoneIndexはPMX
        //   ボーンindexで、ボーンを先頭に追加する本インポータでは MeshContextList の
        //   ボーンindexと一致する。
        //   JOINT→剛体A/B：BodyAName/BodyBName（解決済み）。indexは
        //   「剛体コンテキスト開始index ＋ PMX剛体index」で MeshContextList 上に解決する。
        //
        // 【座標変換】
        //   位置：ConvertPosition（×Scale ＋ FlipZ で z=-z）。頂点・ボーンと同一。
        //   回転：ConvertEulerRotation（PMX Euler(rad)→Quaternion→FlipZ共役→Euler(rad)）。
        //   サイズ：×Scale のみ（範囲量のためZ反転しない）。
        //   質量・減衰・反発・摩擦・Group・Mask・JointType・各min/max・Spring：生値。
        //   （JOINTのmin/max・SpringのFlipZ軸入替えは段階③エクスポート整合と併せて扱う）
        // ----------------------------------------------------------------

        /// <summary>剛体をMeshContext（Type=RigidBody）に変換して追加する。</summary>
        private static void ConvertRigidBodies(PMXDocument document, PMXImportSettings settings, PMXImportResult result)
        {
            foreach (var body in document.RigidBodies)
            {
                // 頂点/面を持たない空MeshObject（ボーンと同形）
                var meshObject = new MeshObject(body.Name)
                {
                    Type = MeshType.RigidBody
                };

                meshObject.RigidBodyData = new RigidBodyData
                {
                    NameEnglish     = body.NameEnglish ?? "",
                    RelatedBoneName = body.RelatedBoneName ?? "",
                    // ボーンを先頭に追加する本インポータでは PMXボーンindex == MeshContextListボーンindex
                    BoneIndex       = body.BoneIndex,
                    Group           = body.Group,
                    CollisionMask   = body.CollisionMask,
                    Shape           = (RigidBodyShape)body.Shape,
                    Size            = body.Size * settings.Scale,
                    Position        = ConvertPosition(body.Position, settings),
                    Rotation        = ConvertEulerRotation(body.Rotation, settings),
                    Mass            = body.Mass,
                    LinearDamping   = body.LinearDamping,
                    AngularDamping  = body.AngularDamping,
                    Restitution     = body.Restitution,
                    Friction        = body.Friction,
                    PhysicsMode     = (RigidBodyPhysicsMode)body.PhysicsMode
                };

                // MeshContextでラップ（MeshObjectを先に設定 → Name/Type委譲が成立）
                var meshContext = new MeshContext
                {
                    MeshObject = meshObject,
                    Name       = body.Name,
                    Type       = MeshType.RigidBody,
                    IsVisible  = true
                };

                result.MeshContexts.Add(meshContext);
            }
        }

        /// <summary>
        /// ジョイントをMeshContext（Type=RigidBodyJoint）に変換して追加する。
        /// </summary>
        /// <param name="rigidBodyContextBase">
        /// 剛体コンテキストの開始index（MeshContextList上）。-1=剛体未インポート。
        /// </param>
        private static void ConvertJoints(PMXDocument document, PMXImportSettings settings, PMXImportResult result, int rigidBodyContextBase)
        {
            foreach (var joint in document.Joints)
            {
                var meshObject = new MeshObject(joint.Name)
                {
                    Type = MeshType.RigidBodyJoint
                };

                // 剛体A/BのMeshContextList index（剛体未インポート時は-1のまま）
                int idxA = (rigidBodyContextBase >= 0 && joint.RigidBodyIndexA >= 0)
                    ? rigidBodyContextBase + joint.RigidBodyIndexA : -1;
                int idxB = (rigidBodyContextBase >= 0 && joint.RigidBodyIndexB >= 0)
                    ? rigidBodyContextBase + joint.RigidBodyIndexB : -1;

                meshObject.JointData = new JointData
                {
                    NameEnglish       = joint.NameEnglish ?? "",
                    JointType         = joint.JointType,
                    BodyAName         = joint.BodyAName ?? "",
                    BodyBName         = joint.BodyBName ?? "",
                    RigidBodyIndexA   = idxA,
                    RigidBodyIndexB   = idxB,
                    Position          = ConvertPosition(joint.Position, settings),
                    Rotation          = ConvertEulerRotation(joint.Rotation, settings),
                    TranslationMin    = joint.TranslationMin,
                    TranslationMax    = joint.TranslationMax,
                    RotationMin       = joint.RotationMin,
                    RotationMax       = joint.RotationMax,
                    SpringTranslation = joint.SpringTranslation,
                    SpringRotation    = joint.SpringRotation
                };

                var meshContext = new MeshContext
                {
                    MeshObject = meshObject,
                    Name       = joint.Name,
                    Type       = MeshType.RigidBodyJoint,
                    IsVisible  = true
                };

                result.MeshContexts.Add(meshContext);
            }
        }

        /// <summary>
        /// PMXのオイラー角回転（ラジアン）をモデル空間のオイラー角（ラジアン）へ変換する。
        ///
        /// ■ ボーン基底とは別扱いであること（注意）
        ///   剛体・JOINT の回転は「その物体の姿勢」であり、ボーンの局所軸ラベルでは
        ///   ないため、共役変換 S·R·S（AxisFlipOps.EulerRad）のままで正しい。
        ///   ボーン側は局所軸の合成そのものを廃止した（BoneModelRotation は恒等）。
        ///   こちらは追随させない。両者を同一視した旧コメントは誤りだった。
        /// 入力/出力ともラジアン。
        /// </summary>
        private static Vector3 ConvertEulerRotation(Vector3 pmxEulerRad, PMXImportSettings settings)
        {
            return AxisFlipOps.EulerRad(settings.Flip, pmxEulerRad);
        }
    }
}
