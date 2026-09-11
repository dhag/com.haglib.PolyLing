// PMXImporter.Bone.cs
// PMX インポート：ボーン変換と T ポーズ変換。
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

            // ボーンのレスト回転は恒等とする（ローカル軸を合成しない）
            // ----------------------------------------------------------------
            // ■ 経緯
            //   旧実装は CalculateBoneModelRotation で、ローカル軸フラグ(0x0800)の
            //   有無に関わらず全ボーンに「接続先への方向」から局所軸を合成していた。
            //   PMX の実データにローカル軸はほぼ入っておらず、入っていても操作ハンドルの
            //   都合によるもので実体を伴わない。合成した軸は AxisFlipOps.Basis の
            //   共役変換と組み合わさって規約が食い違い、
            //     - ローカル X が骨方向の真逆（実測 180.00 度）
            //     - Unity クリップの dof→軸 対応が成立しない
            //   といった不具合の温床になっていた。
            //
            // ■ 現在の規約
            //   ボーンの局所座標系 ＝ モデル空間。全ボーン共通。
            //   BoneTransform.Rotation も恒等になり、BonePoseData のデルタは
            //   モデル空間の量として素直に解釈できる。
            //   レスト表示は BindPose = worldMatrix.inverse で相殺されるため不変。
            //
            // ■ 捨てた情報
            //   ローカル軸フラグ(0x0800)由来の軸のみ。
            //   付与親(GrantParentIndex/GrantRate)・固定軸・IK 角度制限は別系統で、
            //   ここで捨てているわけではない（付与親と固定軸は未実装）。
            var boneModelRotations = new Quaternion[document.Bones.Count];
            for (int i = 0; i < document.Bones.Count; i++)
            {
                boneModelRotations[i] = Quaternion.identity;
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

        // ================================================================
        // ローカル軸の合成は廃止した
        // ----------------------------------------------------------------
        //   削除したメソッド:
        //     CalculateBoneModelRotation / CalculateDefaultLocalAxisX / CreateRotationFromAxes
        //   BoneModelRotation は恒等固定。復活させないこと。
        //   同等の重複実装が PmxBoneBuilder.cs / PmxBoneImporter.cs にもあったが、
        //   いずれも外部から未参照のためファイルごと削除した。
        // ================================================================

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

            // PMX ボーンの付帯データを保持する。変形階層・フラグ・接続先・付与親・
            // 固定軸・ローカル軸・外部親は BoneTransform では表現できないため、
            // ここで落とすと書き出しで既定値になる。参照は名前を主とする。
            meshObject.PmxBone = new PmxBoneAttrData
            {
                NameEnglish               = pmxBone.NameEnglish ?? "",
                TransformLevel            = pmxBone.TransformLevel,
                Flags                     = pmxBone.Flags,
                ConnectBoneName           = pmxBone.ConnectBoneName ?? "",
                ConnectOffset             = pmxBone.ConnectOffset,
                GrantParentBoneName       = pmxBone.GrantParentBoneName ?? "",
                GrantRate                 = pmxBone.GrantRate,
                FixedAxis                 = pmxBone.FixedAxis,
                LocalAxisX                = pmxBone.LocalAxisX,
                LocalAxisZ                = pmxBone.LocalAxisZ,
                IsLocalAxisAutoCalculated = pmxBone.IsLocalAxisAutoCalculated,
                ExternalParentKey         = pmxBone.ExternalParentKey
            };

            // BoneTransformを設定（ローカル座標・ローカル回転）
            var boneTransform = new Poly_Ling.Data.BoneTransform
            {
                Position = localPosition,
                Rotation = localRotationEuler,
                Scale = Vector3.one,
                UseLocalTransform = true,
                HasBoneTransform = true  // ★スキンドメッシュとして出力
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

            // ★BonePoseDataを生成（IsActive=trueのみ。PreBindPoseはゼロのまま）
            // BonePoseDataはBoneTransformへのデルタとして設計されているため、
            // PreBindPoseに値を入れるとBoneTransformと二重適用になる。
            meshContext.BonePoseData = new Data.BonePoseData { IsActive = true };

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

                    // 角度制限の座標系変換。軸反転のみ（det(S) < 0 のとき min/max を入れ替え）。
                    // ボーンの局所軸は恒等になったため、この値はモデル空間の角度制限として
                    // 扱う。軸規約の置換は行わない。
                    Vector3 limMin = link.LimitMin;
                    Vector3 limMax = link.LimitMax;
                    AxisFlipOps.AngleLimits(settings.Flip, ref limMin, ref limMax);

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
                    var (objName, _, _) = PMXHelper.ParseMaterialMemo(mat.Memo);
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

            //Debug.Log($"[PMXImporter] Union-Find result: {mergedMaterialGroups.Count} groups");
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
            meshObject.IsTriangulated = true;  // PMX は三角形化済み形式

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

                // 反転軸が奇数個（鏡映）のときだけ頂点順序を逆にする。
                // X・Z の両反転（Y軸180°回転）では巻き順を変えない。
                if (AxisFlipOps.ReverseWinding(settings.Flip))
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

            // PMX材質名リストを保存（空メッシュのエクスポート時に使用）
            meshContext.PMXMaterialNames = new List<string>(materialNames);

            // Unity Mesh生成
            // Face.MaterialIndexはグローバルインデックスなので、使用されている最大インデックス+1をサブメッシュ数とする
            int maxMatIndex = materialNameToGlobalIndex.Count > 0 ? materialNameToGlobalIndex.Values.Max() : 0;
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
    }
}
