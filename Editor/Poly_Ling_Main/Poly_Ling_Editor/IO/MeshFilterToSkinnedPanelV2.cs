// MeshFilterToSkinnedPanelV2.cs
// MeshFilter → Skinned 変換パネル V2
// PanelContext（通知）+ ToolContext（実処理）ハイブリッド。IMGUI継続。

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.View;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Localization;

namespace Poly_Ling.Tools.Panels
{

    public class MeshFilterToSkinnedPanelV2 : EditorWindow
    {
        // ================================================================
        // コンテキスト
        // ================================================================

        private PanelContext _panelCtx;
        private ToolContext  _toolCtx;

        private ModelContext Model => _toolCtx?.Model;

        // ================================================================
        // ローカライズ辞書（V1 と同一）
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"]       = new() { ["en"] = "MeshFilter → Skinned",              ["ja"] = "MeshFilter → Skinned変換" },
            ["ModelNotAvailable"] = new() { ["en"] = "Model not available",                ["ja"] = "モデルがありません" },
            ["NoMeshFound"]       = new() { ["en"] = "No mesh objects found",              ["ja"] = "メッシュオブジェクトがありません" },
            ["AlreadyHasBones"]   = new() { ["en"] = "Model already has bones",            ["ja"] = "既にボーンが存在します" },
            ["RootBone"]          = new() { ["en"] = "Root Bone (top mesh)",               ["ja"] = "ルートボーン (トップメッシュ)" },
            ["Convert"]           = new() { ["en"] = "Convert",                            ["ja"] = "変換実行" },
            ["ConvertWarning"]    = new() { ["en"] = "This operation cannot be undone.\nProceed with conversion?",
                                           ["ja"] = "この操作は元に戻せません。\n変換を実行しますか？" },
            ["ConvertSuccess"]    = new() { ["en"] = "Conversion completed: {0} bones created",
                                           ["ja"] = "変換完了: {0}個のボーンを作成" },
            ["Preview"]           = new() { ["en"] = "Preview",                            ["ja"] = "プレビュー" },
            ["BoneHierarchy"]     = new() { ["en"] = "Bone Hierarchy",                     ["ja"] = "ボーン階層" },
            ["BoneAxisSettings"]  = new() { ["en"] = "Bone Axis Settings",                 ["ja"] = "ボーン軸設定" },
            ["SwapAxisRotated"]   = new() { ["en"] = "Rotated bones: Swap to PMX axis (Y→X)",
                                           ["ja"] = "回転ありボーン: PMX軸に入替 (Y→X)" },
            ["SetAxisIdentity"]   = new() { ["en"] = "Identity bones: Set X=Up, Y=Side (PMX style)",
                                           ["ja"] = "回転なしボーン: X軸上向き・Y軸横向きに設定" },
        };

        private static string T(string key)                        => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args)  => L.GetFrom(_localize, key, args);

        // ================================================================
        // UI 状態
        // ================================================================

        private Vector2 _scrollPosition;
        private bool    _foldPreview               = true;
        private bool    _swapAxisForRotatedBones   = false;
        private bool    _setAxisForIdentityBones   = false;

        // ================================================================
        // Open
        // ================================================================

        public static MeshFilterToSkinnedPanelV2 Open(PanelContext panelCtx, ToolContext toolCtx)
        {
            var w = GetWindow<MeshFilterToSkinnedPanelV2>();
            w.titleContent = new GUIContent(T("WindowTitle"));
            w.minSize = new Vector2(350, 300);
            w.SetContexts(panelCtx, toolCtx);
            w.Show();
            return w;
        }

        // ================================================================
        // コンテキスト設定
        // ================================================================

        private void SetContexts(PanelContext panelCtx, ToolContext toolCtx)
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;

            _panelCtx = panelCtx;
            _toolCtx  = toolCtx;

            if (_panelCtx != null) _panelCtx.OnViewChanged += OnViewChanged;

            Repaint();
        }

        // ================================================================
        // ライフサイクル
        // ================================================================

        private void OnEnable()
        {
            if (_panelCtx != null)
            {
                _panelCtx.OnViewChanged -= OnViewChanged;
                _panelCtx.OnViewChanged += OnViewChanged;
            }
        }

        private void OnDisable()
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void OnDestroy()
        {
            if (_panelCtx != null) _panelCtx.OnViewChanged -= OnViewChanged;
        }

        private void OnViewChanged(IProjectView view, ChangeKind kind)
        {
            if (kind == ChangeKind.ModelSwitch || kind == ChangeKind.ListStructure)
                Repaint();
        }

        // ================================================================
        // GUI（V1 の OnGUI と同一ロジック）
        // ================================================================

        private void OnGUI()
        {
            if (_toolCtx == null)
            {
                EditorGUILayout.HelpBox("ToolContext が未設定です。PolyLing ウィンドウから開いてください。",
                    MessageType.Warning);
                return;
            }

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
                EditorGUILayout.HelpBox(T("AlreadyHasBones"), MessageType.Warning);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

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
                    string indent     = new string(' ', depth * 4);
                    string vertexInfo = entry.Context.MeshObject?.VertexCount > 0
                        ? $" ({entry.Context.MeshObject.VertexCount}V)"
                        : " (empty)";
                    EditorGUILayout.LabelField($"{indent}[{i}] {entry.Context.Name}{vertexInfo}");
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField(T("BoneAxisSettings"), EditorStyles.boldLabel);
            _swapAxisForRotatedBones = EditorGUILayout.ToggleLeft(T("SwapAxisRotated"), _swapAxisForRotatedBones);
            _setAxisForIdentityBones = EditorGUILayout.ToggleLeft(T("SetAxisIdentity"),  _setAxisForIdentityBones);
            EditorGUILayout.Space(10);

            EditorGUI.BeginDisabledGroup(hasBones);
            if (GUILayout.Button(T("Convert"), GUILayout.Height(30)))
            {
                if (EditorUtility.DisplayDialog(T("WindowTitle"), T("ConvertWarning"), "OK", "Cancel"))
                    ExecuteConversion(model, meshEntries, _swapAxisForRotatedBones, _setAxisForIdentityBones);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // データ収集（V1 と同一）
        // ================================================================

        private struct MeshEntry
        {
            public int         Index;
            public MeshContext Context;
        }

        private List<MeshEntry> CollectMeshEntries(ModelContext model)
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

        private int CalculateDepth(int index, ModelContext model)
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
        // 変換実行（V1 の ExecuteConversion と同一ロジック）
        // ================================================================

        private void ExecuteConversion(ModelContext model, List<MeshEntry> meshEntries,
            bool swapAxisForRotated, bool setAxisForIdentity)
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
                    Position         = srcCtx.BoneTransform.Position,
                    Rotation         = srcCtx.BoneTransform.Rotation,
                    Scale            = srcCtx.BoneTransform.Scale,
                    UseLocalTransform = true
                };
                boneMeshObject.BoneTransform = boneBt;

                var boneCtx = new MeshContext
                {
                    MeshObject        = boneMeshObject,
                    Type              = MeshType.Bone,
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
                int n = boneContexts.Count;
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
                        Matrix4x4 parentWorld = parentIdx < 0 ? Matrix4x4.identity
                            : computed[parentIdx] ? worldMats[parentIdx] : (Matrix4x4?)null ?? Matrix4x4.zero;
                        if (parentIdx >= 0 && !computed[parentIdx]) continue;
                        if (parentIdx >= 0) parentWorld = worldMats[parentIdx]; else parentWorld = Matrix4x4.identity;
                        worldMats[i]     = parentWorld * Matrix4x4.TRS(bt.Position, Quaternion.Euler(bt.Rotation), bt.Scale);
                        savedWorldPos[i] = new Vector3(worldMats[i].m03, worldMats[i].m13, worldMats[i].m23);
                        computed[i] = true; anyAdded = true;
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

                computed   = new bool[n];
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
                        bt.Position        = parentWorld.inverse.MultiplyPoint3x4(savedWorldPos[i]);
                        newWorldMats[i]    = parentWorld * Matrix4x4.TRS(bt.Position, Quaternion.Euler(bt.Rotation), bt.Scale);
                        computed[i] = true; anyAdded = true;
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
                if (ctx.ParentIndex >= 0)          ctx.ParentIndex          += boneCount;
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
                int oldIndex     = meshEntries[i].Index;
                int newMeshIndex = oldIndex + boneCount;
                int boneMasterIndex = i;

                var meshCtx = model.MeshContextList[newMeshIndex];
                var meshObj = meshCtx.MeshObject;
                if (meshObj == null) continue;

                meshCtx.HierarchyParentIndex = boneMasterIndex;
                meshCtx.ParentIndex          = boneMasterIndex;

                Matrix4x4 originalWorld = savedWorldMatrices[oldIndex];
                if (meshObj.VertexCount > 0)
                {
                    foreach (var vertex in meshObj.Vertices)
                        vertex.Position = originalWorld.MultiplyPoint3x4(vertex.Position);
                }

                meshCtx.BoneTransform.Position         = Vector3.zero;
                meshCtx.BoneTransform.Rotation         = Vector3.zero;
                meshCtx.BoneTransform.Scale            = Vector3.one;
                meshCtx.BoneTransform.UseLocalTransform = false;

                foreach (var vertex in meshObj.Vertices)
                {
                    vertex.BoneWeight = new BoneWeight
                    {
                        boneIndex0 = boneMasterIndex, weight0 = 1f,
                        boneIndex1 = 0, weight1 = 0f,
                        boneIndex2 = 0, weight2 = 0f,
                        boneIndex3 = 0, weight3 = 0f
                    };
                }

                meshCtx.UnityMesh      = meshObj.ToUnityMesh();
                meshCtx.UnityMesh.name = meshCtx.Name;
                meshCtx.OriginalPositions = (Vector3[])meshObj.Positions.Clone();
            }

            // Phase 5: 最終ワールド行列計算
            model.ComputeWorldMatrices();
            _toolCtx?.OnTopologyChanged();

            // Phase 6: HasBoneTransform フラグ
            foreach (var ctx in model.MeshContextList)
            {
                if (ctx?.BoneTransform != null)
                    ctx.BoneTransform.HasBoneTransform = true;
            }

            Debug.Log($"[MeshFilterToSkinnedV2] Created {boneCount} bones from {meshEntries.Count} meshes");
            EditorUtility.DisplayDialog(T("WindowTitle"), T("ConvertSuccess", boneCount), "OK");

            _toolCtx?.Repaint?.Invoke();
            Repaint();
        }
    }
}
