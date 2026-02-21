// Assets/Editor/Poly_Ling/PolyLing/MeshFilterToSkinnedPanel.cs
// MeshFilterメッシュ群をSkinnedMeshRenderer構成に変換するツールパネル
//
// 処理概要:
//   1. MeshType.Meshの各MeshContextに対応するMeshType.Boneを作成
//      - 元のMeshContextのローカルトランスフォーム（Position/Rotation/Scale）をそのまま引き継ぐ
//   2. トップメッシュをルートボーンとする
//   3. 頂点を元のワールド行列（Scale/Rotation込み）でワールド空間に変換
//   4. BindPose = ComputeWorldAndBindPoses()で一括計算
//   5. BoneWeightは各頂点が対応ボーン100%（MeshContextListインデックス）
//
// PMXインポートと同じ構造:
//   - ボーン: 元のMeshContextのPosition/Rotation/Scaleを引き継ぐ
//   - 頂点: ワールド空間
//   - BindPose: ワールド行列の逆行列

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Model;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools.Panels
{
    public class MeshFilterToSkinnedPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "MeshFilterToSkinned";
        public override string Title => "MeshFilter → Skinned";
        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "MeshFilter → Skinned", ["ja"] = "MeshFilter → Skinned変換" },
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available", ["ja"] = "モデルがありません" },
            ["NoMeshFound"] = new() { ["en"] = "No mesh objects found", ["ja"] = "メッシュオブジェクトがありません" },
            ["AlreadyHasBones"] = new() { ["en"] = "Model already has bones", ["ja"] = "既にボーンが存在します" },
            ["RootBone"] = new() { ["en"] = "Root Bone (top mesh)", ["ja"] = "ルートボーン (トップメッシュ)" },
            ["Convert"] = new() { ["en"] = "Convert", ["ja"] = "変換実行" },
            ["ConvertSuccess"] = new() { ["en"] = "Conversion completed: {0} bones created", ["ja"] = "変換完了: {0}個のボーンを作成" },
            ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー" },
            ["BoneHierarchy"] = new() { ["en"] = "Bone Hierarchy", ["ja"] = "ボーン階層" },
            ["BoneAxisSettings"] = new() { ["en"] = "Bone Axis Settings", ["ja"] = "ボーン軸設定" },
            ["SwapAxisRotated"] = new()
            {
                ["en"] = "Rotated bones: Swap to PMX axis (Y→X)",
                ["ja"] = "回転ありボーン: PMX軸に入替 (Y→X)"
            },
            ["SetAxisIdentity"] = new()
            {
                ["en"] = "Identity bones: Set X=Up, Y=Side (PMX style)",
                ["ja"] = "回転なしボーン: X軸上向き・Y軸横向きに設定"
            },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // UI状態
        // ================================================================

        private Vector2 _scrollPosition;
        private bool _foldPreview = true;
        private bool _swapAxisForRotatedBones = false;
        private bool _setAxisForIdentityBones = false;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<MeshFilterToSkinnedPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize = new Vector2(350, 300);
            panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            if (!DrawNoContextWarning())
                return;

            var model = Model;
            if (model == null)
            {
                EditorGUILayout.HelpBox(T("ModelNotAvailable"), MessageType.Warning);
                return;
            }

            var meshEntries = CollectMeshEntries(model);

            if (meshEntries.Count == 0)
            {
                EditorGUILayout.HelpBox(T("NoMeshFound"), MessageType.Warning);
                return;
            }

            bool hasBones = model.MeshContextList.Any(ctx => ctx?.Type == MeshType.Bone);
            if (hasBones)
            {
                EditorGUILayout.HelpBox(T("AlreadyHasBones"), MessageType.Warning);
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            // === プレビュー ===
            EditorGUILayout.Space(10);
            _foldPreview = EditorGUILayout.Foldout(_foldPreview, T("Preview"), true);
            if (_foldPreview)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField(T("RootBone"), meshEntries[0].Context.Name);
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField(T("BoneHierarchy"), EditorStyles.miniLabel);

                for (int i = 0; i < meshEntries.Count; i++)
                {
                    var entry = meshEntries[i];
                    int depth = CalculateDepth(entry.Index, model);
                    string indent = new string(' ', depth * 4);
                    string vertexInfo = entry.Context.MeshObject?.VertexCount > 0
                        ? $" ({entry.Context.MeshObject.VertexCount}V)"
                        : " (empty)";
                    EditorGUILayout.LabelField($"{indent}[{i}] {entry.Context.Name}{vertexInfo}");
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(15);

            // === ボーン軸設定 ===
            EditorGUILayout.LabelField(T("BoneAxisSettings"), EditorStyles.boldLabel);
            _swapAxisForRotatedBones = EditorGUILayout.ToggleLeft(T("SwapAxisRotated"), _swapAxisForRotatedBones);
            _setAxisForIdentityBones = EditorGUILayout.ToggleLeft(T("SetAxisIdentity"), _setAxisForIdentityBones);

            EditorGUILayout.Space(10);

            EditorGUI.BeginDisabledGroup(hasBones);
            if (GUILayout.Button(T("Convert"), GUILayout.Height(30)))
            {
                ExecuteConversion(model, meshEntries, _swapAxisForRotatedBones, _setAxisForIdentityBones);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // データ収集
        // ================================================================

        private struct MeshEntry
        {
            public int Index;
            public MeshContext Context;
        }

        private List<MeshEntry> CollectMeshEntries(ModelContext model)
        {
            var result = new List<MeshEntry>();
            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx != null && ctx.Type == MeshType.Mesh)
                {
                    result.Add(new MeshEntry { Index = i, Context = ctx });
                }
            }
            return result;
        }

        private int CalculateDepth(int index, ModelContext model)
        {
            int depth = 0;
            int current = index;
            int safety = 100;
            while (safety-- > 0)
            {
                var ctx = model.MeshContextList[current];
                int parent = ctx.HierarchyParentIndex;
                if (parent < 0 || parent >= model.MeshContextList.Count)
                    break;
                depth++;
                current = parent;
            }
            return depth;
        }

        // ================================================================
        // 変換実行
        // ================================================================

        private void ExecuteConversion(ModelContext model, List<MeshEntry> meshEntries,
            bool swapAxisForRotated, bool setAxisForIdentity)
        {
            int boneCount = meshEntries.Count;

            // --- 変換前のワールド行列を保存 ---
            model.ComputeWorldMatrices();
            var savedWorldMatrices = new Dictionary<int, Matrix4x4>();
            for (int i = 0; i < meshEntries.Count; i++)
            {
                int idx = meshEntries[i].Index;
                savedWorldMatrices[idx] = model.MeshContextList[idx].WorldMatrix;
            }

            // 旧インデックス → ボーン番号
            var oldIndexToBoneNum = new Dictionary<int, int>();
            for (int i = 0; i < meshEntries.Count; i++)
            {
                oldIndexToBoneNum[meshEntries[i].Index] = i;
            }

            // --- Phase 1: ボーンMeshContextを作成 ---
            // 元のMeshContextのローカルトランスフォーム（Position/Rotation/Scale）をそのまま引き継ぐ
            var boneContexts = new List<MeshContext>(boneCount);

            for (int i = 0; i < meshEntries.Count; i++)
            {
                var srcCtx = meshEntries[i].Context;

                var boneMeshObject = new MeshObject(srcCtx.Name)
                {
                    Type = MeshType.Bone
                };

                // 親を決定
                int srcParent = srcCtx.HierarchyParentIndex;
                int parentBoneNum = -1;
                if (srcParent >= 0 && oldIndexToBoneNum.TryGetValue(srcParent, out int pbn))
                {
                    parentBoneNum = pbn;
                }

                // 元のローカルトランスフォームをそのまま使用
                var boneBt = new BoneTransform
                {
                    Position = srcCtx.BoneTransform.Position,
                    Rotation = srcCtx.BoneTransform.Rotation,
                    Scale = srcCtx.BoneTransform.Scale,
                    UseLocalTransform = true
                };
                boneMeshObject.BoneTransform = boneBt;

                var boneCtx = new MeshContext
                {
                    MeshObject = boneMeshObject,
                    Type = MeshType.Bone,
                    IsVisible = true,
                    OriginalPositions = new Vector3[0],
                    UnityMesh = null
                };

                boneCtx.ParentIndex = parentBoneNum;
                boneCtx.HierarchyParentIndex = parentBoneNum;

                boneContexts.Add(boneCtx);
            }

            // --- Phase 1.5: ボーン軸の調整 ---
            if (swapAxisForRotated || setAxisForIdentity)
            {
                int n = boneContexts.Count;

                // 1. 変更前のワールド位置を保存
                var savedWorldPos = new Vector3[n];
                var worldMats = new Matrix4x4[n];
                var computed = new bool[n];

                for (int pass = 0; pass < n; pass++)
                {
                    bool anyAdded = false;
                    for (int i = 0; i < n; i++)
                    {
                        if (computed[i]) continue;
                        var bt = boneContexts[i].BoneTransform;
                        int parentIdx = boneContexts[i].HierarchyParentIndex;

                        Matrix4x4 parentWorld;
                        if (parentIdx < 0)
                            parentWorld = Matrix4x4.identity;
                        else if (computed[parentIdx])
                            parentWorld = worldMats[parentIdx];
                        else
                            continue;

                        worldMats[i] = parentWorld * Matrix4x4.TRS(bt.Position, Quaternion.Euler(bt.Rotation), bt.Scale);
                        savedWorldPos[i] = new Vector3(worldMats[i].m03, worldMats[i].m13, worldMats[i].m23);
                        computed[i] = true;
                        anyAdded = true;
                    }
                    if (!anyAdded) break;
                }

                // 2. 回転を変更
                Quaternion swapYtoX = Quaternion.Euler(0f, 0f, 90f);
                for (int i = 0; i < n; i++)
                {
                    var bt = boneContexts[i].BoneTransform;
                    if (bt == null) continue;

                    bool isIdentity = bt.Rotation == Vector3.zero;

                    if (!isIdentity && swapAxisForRotated)
                    {
                        Quaternion original = Quaternion.Euler(bt.Rotation);
                        bt.Rotation = (original * swapYtoX).eulerAngles;
                    }
                    else if (isIdentity && setAxisForIdentity)
                    {
                        bt.Rotation = new Vector3(0f, 0f, 90f);
                    }
                }

                // 3. Positionを再計算（各ボーンが元のワールド位置を維持するように）
                computed = new bool[n];
                var newWorldMats = new Matrix4x4[n];

                for (int pass = 0; pass < n; pass++)
                {
                    bool anyAdded = false;
                    for (int i = 0; i < n; i++)
                    {
                        if (computed[i]) continue;
                        var bt = boneContexts[i].BoneTransform;
                        int parentIdx = boneContexts[i].HierarchyParentIndex;

                        Matrix4x4 parentWorld;
                        if (parentIdx < 0)
                            parentWorld = Matrix4x4.identity;
                        else if (computed[parentIdx])
                            parentWorld = newWorldMats[parentIdx];
                        else
                            continue;

                        // 親の新ワールド行列の逆で元のワールド位置を逆変換 → 新しいローカル位置
                        bt.Position = parentWorld.inverse.MultiplyPoint3x4(savedWorldPos[i]);

                        newWorldMats[i] = parentWorld * Matrix4x4.TRS(bt.Position, Quaternion.Euler(bt.Rotation), bt.Scale);
                        computed[i] = true;
                        anyAdded = true;
                    }
                    if (!anyAdded) break;
                }
            }

            // --- Phase 2: リスト再構築 ---
            var oldList = new List<MeshContext>(model.MeshContextList);
            model.MeshContextList.Clear();

            for (int i = 0; i < boneCount; i++)
            {
                model.MeshContextList.Add(boneContexts[i]);
                boneContexts[i].ParentModelContext = model;
            }

            model.MeshContextList.AddRange(oldList);
            model.InvalidateTypedIndices();

            // 既存要素のインデックス参照をboneCount分ずらす
            for (int i = boneCount; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx == null) continue;
                if (ctx.HierarchyParentIndex >= 0)
                    ctx.HierarchyParentIndex += boneCount;
                if (ctx.ParentIndex >= 0)
                    ctx.ParentIndex += boneCount;

                // 既存BoneWeightのインデックスもずらす
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
                            weight0 = bw.weight0,
                            weight1 = bw.weight1,
                            weight2 = bw.weight2,
                            weight3 = bw.weight3
                        };
                    }
                }
            }

            // --- Phase 3: ワールド行列 + BindPose一括計算 ---
            model.ComputeWorldAndBindPoses();

            // --- Phase 4: 頂点ワールド変換 + BoneWeight設定 ---
            for (int i = 0; i < meshEntries.Count; i++)
            {
                int oldIndex = meshEntries[i].Index;
                int newMeshIndex = oldIndex + boneCount;
                int boneMasterIndex = i;

                var meshCtx = model.MeshContextList[newMeshIndex];
                var meshObj = meshCtx.MeshObject;
                if (meshObj == null) continue;

                // メッシュの親をボーンに変更
                meshCtx.HierarchyParentIndex = boneMasterIndex;
                meshCtx.ParentIndex = boneMasterIndex;

                // 頂点をワールド空間に変換（スケール・回転含む）
                Matrix4x4 originalWorld = savedWorldMatrices[oldIndex];
                if (meshObj.VertexCount > 0)
                {
                    foreach (var vertex in meshObj.Vertices)
                    {
                        vertex.Position = originalWorld.MultiplyPoint3x4(vertex.Position);
                    }
                }

                // メッシュのBoneTransformを無効化（頂点はワールド空間に変換済み）
                meshCtx.BoneTransform.Position = Vector3.zero;
                meshCtx.BoneTransform.Rotation = Vector3.zero;
                meshCtx.BoneTransform.Scale = Vector3.one;
                meshCtx.BoneTransform.UseLocalTransform = false;

                // BoneWeight: 全頂点が対応ボーンのMeshContextListインデックスを参照
                foreach (var vertex in meshObj.Vertices)
                {
                    vertex.BoneWeight = new BoneWeight
                    {
                        boneIndex0 = boneMasterIndex,
                        weight0 = 1f,
                        boneIndex1 = 0,
                        weight1 = 0f,
                        boneIndex2 = 0,
                        weight2 = 0f,
                        boneIndex3 = 0,
                        weight3 = 0f
                    };
                }

                // UnityMeshを再構築
                meshCtx.UnityMesh = meshObj.ToUnityMesh();
                meshCtx.UnityMesh.name = meshCtx.Name;
                meshCtx.OriginalPositions = (Vector3[])meshObj.Positions.Clone();
            }

            // --- Phase 5: 最終ワールド行列計算 + GPUバッファ再構築 ---
            model.ComputeWorldMatrices();
            _context?.OnTopologyChanged();

            // --- Phase 6: ExportAsSkinned フラグを設定 ---
            foreach (var ctx in model.MeshContextList)
            {
                if (ctx?.BoneTransform != null)
                    ctx.BoneTransform.ExportAsSkinned = true;
            }

            Debug.Log($"[MeshFilterToSkinned] Created {boneCount} bones from {meshEntries.Count} meshes");
            EditorUtility.DisplayDialog(T("WindowTitle"), T("ConvertSuccess", boneCount), "OK");

            _context?.Repaint?.Invoke();
            Repaint();
        }
    }
}
