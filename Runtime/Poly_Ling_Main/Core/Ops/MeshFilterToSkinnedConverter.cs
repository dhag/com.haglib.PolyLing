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
            int boneCount = meshEntries.Count;

            model.ComputeWorldMatrices();
            var savedWorldMatrices = new Dictionary<int, Matrix4x4>();
            for (int i = 0; i < meshEntries.Count; i++)
            {
                int idx = meshEntries[i].Index;
                savedWorldMatrices[idx] = model.MeshContextList[idx].WorldMatrix;
            }

            var oldIndexToBoneNum = new Dictionary<int, int>();
            for (int i = 0; i < meshEntries.Count; i++)
                oldIndexToBoneNum[meshEntries[i].Index] = i;

            // Phase 1: ボーン MeshContext 作成
            var boneContexts = new List<MeshContext>(boneCount);
            for (int i = 0; i < meshEntries.Count; i++)
            {
                var srcCtx = meshEntries[i].Context;
                var boneMeshObject = new MeshObject(srcCtx.Name) { Type = MeshType.Bone };

                int srcParent    = srcCtx.HierarchyParentIndex;
                int parentBoneNum = -1;
                if (srcParent >= 0 && oldIndexToBoneNum.TryGetValue(srcParent, out int pbn))
                    parentBoneNum = pbn;

                var boneBt = new BoneTransform
                {
                    Position          = srcCtx.BoneTransform.Position,
                    Rotation          = srcCtx.BoneTransform.Rotation,
                    Scale             = srcCtx.BoneTransform.Scale,
                    UseLocalTransform = true
                };
                boneMeshObject.BoneTransform = boneBt;

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

            // Phase 1.5: ボーン軸調整
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

            // Phase 4: 頂点ワールド変換 + BoneWeight
            for (int i = 0; i < meshEntries.Count; i++)
            {
                int oldIndex      = meshEntries[i].Index;
                int newMeshIndex  = oldIndex + boneCount;
                int boneMasterIdx = i;

                var meshCtx = model.MeshContextList[newMeshIndex];
                var meshObj = meshCtx.MeshObject;
                if (meshObj == null) continue;

                meshCtx.HierarchyParentIndex = boneMasterIdx;
                meshCtx.ParentIndex          = boneMasterIdx;

                Matrix4x4 originalWorld = savedWorldMatrices[oldIndex];
                if (meshObj.VertexCount > 0)
                    foreach (var vertex in meshObj.Vertices)
                        vertex.Position = originalWorld.MultiplyPoint3x4(vertex.Position);

                meshCtx.BoneTransform.Position          = Vector3.zero;
                meshCtx.BoneTransform.Rotation          = Vector3.zero;
                meshCtx.BoneTransform.Scale             = Vector3.one;
                meshCtx.BoneTransform.UseLocalTransform = false;

                foreach (var vertex in meshObj.Vertices)
                    vertex.BoneWeight = new BoneWeight
                        { boneIndex0 = boneMasterIdx, weight0 = 1f };

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

            Debug.Log($"[MeshFilterToSkinnedConverter] Created {boneCount} bones");
            return boneCount;
        }
    }
}
