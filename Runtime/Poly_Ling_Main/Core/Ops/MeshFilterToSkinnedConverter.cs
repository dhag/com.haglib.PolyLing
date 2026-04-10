// MeshFilterToSkinnedConverter.cs
// MeshFilter → Skinned 変換ロジック（Runtime / Editor 共有）。
// EditorGUI 依存なし。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Context;
using Poly_Ling.Localization;

namespace Poly_Ling.Ops
{
    // ================================================================
    // ローカライズ辞書
    // ================================================================

    public static class MeshFilterToSkinnedTexts
    {
        private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
        {
            ["WindowTitle"]       = new() { ["en"] = "MeshFilter → Skinned",                               ["ja"] = "MeshFilter → Skinned変換" },
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available",                                 ["ja"] = "モデルがありません" },
            ["NoMeshFound"]       = new() { ["en"] = "No mesh objects found",                               ["ja"] = "メッシュオブジェクトがありません" },
            ["AlreadyHasBones"]   = new() { ["en"] = "Model already has bones",                             ["ja"] = "既にボーンが存在します" },
            ["RootBone"]          = new() { ["en"] = "Root Bone (top mesh)",                               ["ja"] = "ルートボーン (トップメッシュ)" },
            ["Convert"]           = new() { ["en"] = "Convert",                                             ["ja"] = "変換実行" },
            ["ConvertWarning"]    = new() { ["en"] = "This operation cannot be undone.\nProceed?",          ["ja"] = "この操作は元に戻せません。\n変換を実行しますか？" },
            ["ConvertSuccess"]    = new() { ["en"] = "Conversion completed: {0} bones created",            ["ja"] = "変換完了: {0}個のボーンを作成" },
            ["Preview"]           = new() { ["en"] = "Preview",                                             ["ja"] = "プレビュー" },
            ["BoneHierarchy"]     = new() { ["en"] = "Bone Hierarchy",                                      ["ja"] = "ボーン階層" },
            ["BoneAxisSettings"]  = new() { ["en"] = "Bone Axis Settings",                                  ["ja"] = "ボーン軸設定" },
            ["SwapAxisRotated"]   = new() { ["en"] = "Rotated bones: Swap to PMX axis (Y→X)",              ["ja"] = "回転ありボーン: PMX軸に入替 (Y→X)" },
            ["SetAxisIdentity"]   = new() { ["en"] = "Identity bones: Set X=Up, Y=Side (PMX style)",       ["ja"] = "回転なしボーン: X軸上向き・Y軸横向きに設定" },
        };

        public static string T(string key)                       => L.GetFrom(Texts, key);
        public static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
    }

    // ================================================================
    // 変換ロジック
    // ================================================================

    public static class MeshFilterToSkinnedConverter
    {
        // ================================================================
        // 公開型
        // ================================================================

        public struct MeshEntry
        {
            public int         Index;
            public MeshContext Context;
        }

        // ================================================================
        // データ収集
        // ================================================================

        public static List<MeshEntry> CollectMeshEntries(ModelContext model)
        {
            var result = new List<MeshEntry>();
            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx != null && ctx.Type == MeshType.Mesh)
                    result.Add(new MeshEntry { Index = i, Context = ctx });
            }
            return result;
        }

        public static int CalculateDepth(int index, ModelContext model)
        {
            int depth = 0, current = index, safety = 100;
            while (safety-- > 0)
            {
                var ctx    = model.MeshContextList[current];
                int parent = ctx.HierarchyParentIndex;
                if (parent < 0 || parent >= model.MeshContextList.Count) break;
                depth++; current = parent;
            }
            return depth;
        }

        // ================================================================
        // 変換実行（Editor / Player 共通）
        // ================================================================

        /// <summary>
        /// MeshFilter → Skinned 変換を実行する。
        /// 成功時に作成したボーン数を返す。失敗時は 0 を返す。
        /// </summary>
        public static int Execute(
            ModelContext model,
            List<MeshEntry> meshEntries,
            bool swapAxisForRotated,
            bool setAxisForIdentity)
        {
            // IgnorePoseInArmature で分割
            var boneEntries    = meshEntries.Where(e => !e.Context.IgnorePoseInArmature).ToList();
            var ignoredEntries = meshEntries.Where(e =>  e.Context.IgnorePoseInArmature).ToList();
            int boneCount      = boneEntries.Count;

            // ワールド行列を全メッシュ分保存
            // BoneTransform.Position はプリミティブ作成時の絶対ワールド座標。
            // model.ComputeWorldMatrices() は HierarchyParentIndex を累積するため、
            // 親子関係設定済みの場合に二重加算になる。
            // MeshFilter メッシュは UseLocalTransform=true かつ Position=絶対座標なので
            // BoneTransform から直接 TRS 行列を作成する。
            var savedWorldMatrices = new Dictionary<int, Matrix4x4>();
            foreach (var e in meshEntries)
            {
                var bt = e.Context.BoneTransform;
                savedWorldMatrices[e.Index] = (bt != null && bt.UseLocalTransform)
                    ? Matrix4x4.TRS(bt.Position, Quaternion.Euler(bt.Rotation), bt.Scale)
                    : Matrix4x4.identity;
            }

            // ボーン生成対象のみ old index → boneEntries 内インデックス にマップ
            var oldIndexToBoneNum = new Dictionary<int, int>();
            for (int i = 0; i < boneEntries.Count; i++)
                oldIndexToBoneNum[boneEntries[i].Index] = i;

            // 元リストの親子関係を保存（Phase 2 前に参照するため）
            var originalList = new List<MeshContext>(model.MeshContextList);

            // HierarchyParentIndex を上に辿り、IgnorePose をスキップして
            // 最初のボーン生成対象の祖先の boneEntries インデックスを返す。
            // 見つからない場合は -1。
            int FindEffectiveBoneNum(int startMeshIndex)
            {
                int cur = startMeshIndex;
                int safety = 200;
                while (cur >= 0 && cur < originalList.Count && safety-- > 0)
                {
                    if (oldIndexToBoneNum.TryGetValue(cur, out int bn)) return bn;
                    cur = originalList[cur].HierarchyParentIndex;
                }
                return -1;
            }

            // 全 meshEntry の effective bone num を Phase 2 前に確定
            var effectiveBoneNumForEntry = new Dictionary<int, int>();
            int lastBoneNum = -1; // リスト順で直前のボーン番号
            foreach (var e in meshEntries)
            {
                if (!e.Context.IgnorePoseInArmature)
                {
                    int bn = oldIndexToBoneNum[e.Index];
                    effectiveBoneNumForEntry[e.Index] = bn;
                    lastBoneNum = bn;
                }
                else
                {
                    // HierarchyParent を上に辿って最初の非IgnorePose祖先を探す
                    int found = FindEffectiveBoneNum(originalList[e.Index].HierarchyParentIndex);
                    // 親子関係がない場合はリスト順で直前のボーンを使う
                    if (found < 0) found = lastBoneNum;
                    effectiveBoneNumForEntry[e.Index] = found;
                }
            }

            // Phase 1: ボーン MeshContext 作成（boneEntries のみ）
            var boneContexts = new List<MeshContext>(boneCount);
            for (int i = 0; i < boneEntries.Count; i++)
            {
                var srcCtx = boneEntries[i].Context;

                // 有効な親ボーン番号を求める（直接親が IgnorePose ならスキップして辿る）
                int parentBoneNum = FindEffectiveBoneNum(srcCtx.HierarchyParentIndex);

                // local TRS を決定
                // 直接の HierarchyParent がボーン対象かどうかで分岐
                bool directParentIsBone = srcCtx.HierarchyParentIndex >= 0 &&
                                          oldIndexToBoneNum.ContainsKey(srcCtx.HierarchyParentIndex);
                Vector3 localPos;
                Vector3 localRot;
                Vector3 localScl;

                if (directParentIsBone || srcCtx.HierarchyParentIndex < 0)
                {
                    // 直接親がボーン（または親なし）: BoneTransform をそのまま使う
                    localPos = srcCtx.BoneTransform.Position;
                    localRot = srcCtx.BoneTransform.Rotation;
                    localScl = srcCtx.BoneTransform.Scale;
                }
                else
                {
                    // 直接親が IgnorePose: ワールド行列から effective 親相対で再計算
                    Matrix4x4 childWorld = savedWorldMatrices[boneEntries[i].Index];
                    Matrix4x4 parentWorld = parentBoneNum >= 0
                        ? savedWorldMatrices[boneEntries[parentBoneNum].Index]
                        : Matrix4x4.identity;
                    Matrix4x4 localMat = parentWorld.inverse * childWorld;
                    localPos = new Vector3(localMat.m03, localMat.m13, localMat.m23);
                    Vector3 scaleX = new Vector3(localMat.m00, localMat.m10, localMat.m20);
                    Vector3 scaleY = new Vector3(localMat.m01, localMat.m11, localMat.m21);
                    Vector3 scaleZ = new Vector3(localMat.m02, localMat.m12, localMat.m22);
                    localScl = new Vector3(scaleX.magnitude, scaleY.magnitude, scaleZ.magnitude);
                    Quaternion localRotQ = Quaternion.LookRotation(
                        new Vector3(localMat.m02, localMat.m12, localMat.m22).normalized,
                        new Vector3(localMat.m01, localMat.m11, localMat.m21).normalized);
                    localRot = localRotQ.eulerAngles;
                }

                var boneMeshObject = new MeshObject(srcCtx.Name) { Type = MeshType.Bone };
                boneMeshObject.BoneTransform = new BoneTransform
                {
                    Position          = localPos,
                    Rotation          = localRot,
                    Scale             = localScl,
                    UseLocalTransform = true
                };

                var boneCtx = new MeshContext
                {
                    MeshObject        = boneMeshObject,
                    IsVisible         = true,
                    OriginalPositions = new Vector3[0],
                    UnityMesh         = null
                };
                boneCtx.ParentIndex          = parentBoneNum;
                boneCtx.HierarchyParentIndex = parentBoneNum;
                boneContexts.Add(boneCtx);
            }

            // Phase 1.5: ボーン軸調整（boneContexts のみ、変更なし）
            if (swapAxisForRotated || setAxisForIdentity)
            {
                int n             = boneContexts.Count;
                var savedWorldPos = new Vector3[n];
                var worldMats     = new Matrix4x4[n];
                var computed      = new bool[n];

                for (int pass = 0; pass < n; pass++)
                {
                    bool anyAdded = false;
                    for (int i = 0; i < n; i++)
                    {
                        if (computed[i]) continue;
                        var bt        = boneContexts[i].BoneTransform;
                        int parentIdx = boneContexts[i].HierarchyParentIndex;
                        if (parentIdx >= 0 && !computed[parentIdx]) continue;
                        Matrix4x4 parentWorld = parentIdx < 0 ? Matrix4x4.identity : worldMats[parentIdx];
                        worldMats[i]     = parentWorld * Matrix4x4.TRS(bt.Position, Quaternion.Euler(bt.Rotation), bt.Scale);
                        savedWorldPos[i] = new Vector3(worldMats[i].m03, worldMats[i].m13, worldMats[i].m23);
                        computed[i]      = true;
                        anyAdded         = true;
                    }
                    if (!anyAdded) break;
                }

                Quaternion swapYtoX = Quaternion.Euler(0f, 0f, 90f);
                for (int i = 0; i < n; i++)
                {
                    var bt = boneContexts[i].BoneTransform;
                    if (bt == null) continue;
                    bool isIdentity = bt.Rotation == Vector3.zero;
                    if (!isIdentity && swapAxisForRotated)
                        bt.Rotation = (Quaternion.Euler(bt.Rotation) * swapYtoX).eulerAngles;
                    else if (isIdentity && setAxisForIdentity)
                        bt.Rotation = new Vector3(0f, 0f, 90f);
                }

                computed        = new bool[n];
                var newWorldMats = new Matrix4x4[n];
                for (int pass = 0; pass < n; pass++)
                {
                    bool anyAdded = false;
                    for (int i = 0; i < n; i++)
                    {
                        if (computed[i]) continue;
                        var bt        = boneContexts[i].BoneTransform;
                        int parentIdx = boneContexts[i].HierarchyParentIndex;
                        if (parentIdx >= 0 && !computed[parentIdx]) continue;
                        Matrix4x4 parentWorld = parentIdx < 0 ? Matrix4x4.identity : newWorldMats[parentIdx];
                        bt.Position     = parentWorld.inverse.MultiplyPoint3x4(savedWorldPos[i]);
                        newWorldMats[i] = parentWorld * Matrix4x4.TRS(bt.Position, Quaternion.Euler(bt.Rotation), bt.Scale);
                        computed[i]     = true;
                        anyAdded        = true;
                    }
                    if (!anyAdded) break;
                }
            }

            // Phase 2: リスト再構築
            var oldList = new List<MeshContext>(model.MeshContextList);
            model.MeshContextList.Clear();
            for (int i = 0; i < boneCount; i++)
            {
                model.MeshContextList.Add(boneContexts[i]);
                boneContexts[i].ParentModelContext = model;
            }
            model.MeshContextList.AddRange(oldList);
            model.InvalidateTypedIndices();

            for (int i = boneCount; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx == null) continue;
                if (ctx.HierarchyParentIndex >= 0) ctx.HierarchyParentIndex += boneCount;
                if (ctx.ParentIndex          >= 0) ctx.ParentIndex          += boneCount;
                if (ctx.MeshObject != null)
                {
                    foreach (var vertex in ctx.MeshObject.Vertices)
                    {
                        if (!vertex.HasBoneWeight) continue;
                        var bw = vertex.BoneWeight.Value;
                        vertex.BoneWeight = new BoneWeight
                        {
                            boneIndex0 = bw.weight0 > 0 ? bw.boneIndex0 + boneCount : 0,
                            boneIndex1 = bw.weight1 > 0 ? bw.boneIndex1 + boneCount : 0,
                            boneIndex2 = bw.weight2 > 0 ? bw.boneIndex2 + boneCount : 0,
                            boneIndex3 = bw.weight3 > 0 ? bw.boneIndex3 + boneCount : 0,
                            weight0 = bw.weight0, weight1 = bw.weight1,
                            weight2 = bw.weight2, weight3 = bw.weight3
                        };
                    }
                }
            }

            // Phase 3: ワールド行列 + BindPose
            model.ComputeWorldAndBindPoses();

            // Phase 4: 頂点ワールド変換 + BoneWeight（全 meshEntries）
            foreach (var entry in meshEntries)
            {
                int oldIndex     = entry.Index;
                int newMeshIndex = oldIndex + boneCount;
                int boneMasterIdx = effectiveBoneNumForEntry[oldIndex]; // -1 の場合は親なし

                var meshCtx = model.MeshContextList[newMeshIndex];
                var meshObj = meshCtx.MeshObject;
                if (meshObj == null) continue;

                // IgnorePose メッシュは有効親ボーン配下に付け替え（-1 ならルートのまま）
                if (boneMasterIdx >= 0)
                {
                    meshCtx.HierarchyParentIndex = boneMasterIdx;
                    meshCtx.ParentIndex          = boneMasterIdx;
                }

                Matrix4x4 originalWorld = savedWorldMatrices[oldIndex];
                if (meshObj.VertexCount > 0)
                    foreach (var vertex in meshObj.Vertices)
                        vertex.Position = originalWorld.MultiplyPoint3x4(vertex.Position);

                meshCtx.BoneTransform.Position          = Vector3.zero;
                meshCtx.BoneTransform.Rotation          = Vector3.zero;
                meshCtx.BoneTransform.Scale             = Vector3.one;
                meshCtx.BoneTransform.UseLocalTransform = false;

                int assignBone = boneMasterIdx >= 0 ? boneMasterIdx : 0;
                foreach (var vertex in meshObj.Vertices)
                    vertex.BoneWeight = new BoneWeight { boneIndex0 = assignBone, weight0 = 1f };

                meshCtx.UnityMesh      = meshObj.ToUnityMesh();
                meshCtx.UnityMesh.name = meshCtx.Name;
                meshCtx.OriginalPositions = (Vector3[])meshObj.Positions.Clone();
            }

            // Phase 5: 最終ワールド行列
            model.ComputeWorldMatrices();

            // Phase 6: HasBoneTransform フラグ
            foreach (var ctx in model.MeshContextList)
                if (ctx?.BoneTransform != null)
                    ctx.BoneTransform.HasBoneTransform = true;

            Debug.Log($"[MeshFilterToSkinnedConverter] Created {boneCount} bones (ignored {ignoredEntries.Count})");
            return boneCount;
        }
    }
}
