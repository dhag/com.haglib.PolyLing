// MQOImporter.MirrorBone.cs
// MQO インポート：ミラー処理とボーン変換。
// Runtime/Poly_Ling_Main/MQO/Import/ に配置

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

namespace Poly_Ling.MQO
{
    public static partial class MQOImporter
    {
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
    }
}
