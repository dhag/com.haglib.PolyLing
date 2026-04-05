// Assets/Editor/Poly_Ling/PolyLing/SimpleMeshFactory_MeshLoad.cs
// メッシュ読み込み機能（エントリーポイント）
// 純粋ヘルパー群は EditorCore/MeshAssetIO/EditorMeshLoader.cs に移動済み。
// 状態変更を伴う実装（AddLoadedMesh / LoadHierarchyFromGameObject）は本ファイルに保持。

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UndoSystem;
using Poly_Ling;
using Poly_Ling.Tools;
using Poly_Ling.EditorBridge;
using Poly_Ling.EditorCore;
using Poly_Ling.PMX;

public partial class PolyLing : IEditorMeshLoadHost
{
    // ================================================================
    // エントリーポイント（EditorMeshLoaderに委譲）
    // ================================================================

    private void LoadMeshFromAsset()     => EditorMeshLoader.LoadFromAsset(this);
    private void LoadMeshFromPrefab()    => EditorMeshLoader.LoadFromPrefab(this);
    private void LoadMeshFromHierarchy() => EditorMeshLoader.LoadFromHierarchy(this);
    private void LoadMeshFromSelection() => LoadMeshFromHierarchy();

    // ================================================================
    // IEditorMeshLoadHost 明示的実装
    // ================================================================

    void IEditorMeshLoadHost.AddLoadedMesh(Mesh mesh, string name, Material[] materials, Transform sourceTransform)
        => AddLoadedMesh(mesh, name, materials, sourceTransform);

    void IEditorMeshLoadHost.LoadHierarchyFromGameObject(GameObject rootGameObject, Transform boneRootTransform, bool detectNamedMirror)
        => LoadHierarchyFromGameObject(rootGameObject, boneRootTransform, detectNamedMirror);

    void IEditorMeshLoadHost.ShowSkinnedMeshImportDialogInternal(GameObject rootObject, SkinnedMeshRenderer[] skinnedRenderers)
    {
        // SMRなし・Armatureフォルダあり の場合 skinnedRenderers は空配列で渡される
        if (skinnedRenderers.Length == 0)
        {
            Transform armatureRoot = EditorMeshLoader.DetectArmatureFolder(rootObject.transform);
            if (armatureRoot != null)
            {
                int boneCount = EditorMeshLoader.CountDescendants(armatureRoot) + 1;
                var dialog = SkinnedMeshImportDialog.Show(rootObject, armatureRoot, boneCount, 0);
                dialog.OnImport = (importMesh, importBones, selectedRootBone) =>
                {
                    LoadHierarchyFromGameObject(rootObject, importBones ? selectedRootBone : null, dialog.DetectNamedMirror);
                };
            }
            else
            {
                LoadHierarchyFromGameObject(rootObject, null);
            }
        }
        else
        {
            ShowSkinnedMeshImportDialog(rootObject, skinnedRenderers);
        }
    }

    // ================================================================
    // ボーン取り込みダイアログ（Editor UI）
    // ================================================================

    private void ShowSkinnedMeshImportDialog(GameObject rootObject, SkinnedMeshRenderer[] skinnedRenderers)
    {
        Transform detectedRootBone = EditorMeshLoader.DetectBestRootBone(skinnedRenderers);
        int boneCount = detectedRootBone != null ? EditorMeshLoader.CountDescendants(detectedRootBone) + 1 : 0;

        var dialog = SkinnedMeshImportDialog.Show(rootObject, detectedRootBone, boneCount, skinnedRenderers.Length);
        dialog.OnImport = (importMesh, importBones, selectedRootBone) =>
        {
            LoadHierarchyFromGameObject(rootObject, importBones ? selectedRootBone : null, dialog.DetectNamedMirror);
        };
    }

    // ================================================================
    // 状態変更を伴う実装（PolyLing内部状態を直接操作するため本ファイルに保持）
    // ================================================================

    /// <summary>
    /// GameObjectの階層構造をメッシュリストとしてインポート。
    /// 純粋計算は EditorMeshLoader の静的メソッドを呼び出す。
    /// </summary>
    private void LoadHierarchyFromGameObject(GameObject rootGameObject, Transform boneRootTransform, bool detectNamedMirror = true)
    {
        if (rootGameObject == null) return;

        var gameObjects = new List<GameObject>();
        EditorMeshLoader.CollectGameObjectsDepthFirst(rootGameObject, gameObjects);

        if (gameObjects.Count == 0)
        {
            PLEditorBridge.I.DisplayDialog("Error", "インポート対象のGameObjectがありません", "OK");
            return;
        }

        var boneTransforms  = new List<Transform>();
        var boneToIndex     = new Dictionary<Transform, int>();
        var boneBindPoses   = new Dictionary<Transform, Matrix4x4>();

        if (boneRootTransform != null)
        {
            EditorMeshLoader.CollectBoneTransformsDepthFirst(boneRootTransform, boneTransforms);
            for (int i = 0; i < boneTransforms.Count; i++)
                boneToIndex[boneTransforms[i]] = i;

            Debug.Log($"[LoadHierarchyFromGameObject] Collected {boneTransforms.Count} bones from '{boneRootTransform.name}'");

            var smrs = rootGameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh == null || smr.bones == null) continue;
                var bindposes = smr.sharedMesh.bindposes;
                if (bindposes == null) continue;
                for (int i = 0; i < smr.bones.Length && i < bindposes.Length; i++)
                {
                    var bone = smr.bones[i];
                    if (bone != null && !boneBindPoses.ContainsKey(bone))
                        boneBindPoses[bone] = bindposes[i];
                }
            }
        }

        var oldSelectedIndices = _model.CaptureAllSelectedIndices();
        var oldMeshContextList = new List<MeshContext>(_meshContextList);
        ClearMeshContextListInternal();

        var allMaterials   = new List<Material>();
        var materialToIndex = new Dictionary<Material, int>();

        foreach (var go in gameObjects)
        {
            Material[] sharedMats = EditorMeshLoader.GetSharedMaterials(go);
            if (sharedMats != null)
            {
                foreach (var mat in sharedMats)
                {
                    if (mat != null && !materialToIndex.ContainsKey(mat))
                    {
                        materialToIndex[mat] = allMaterials.Count;
                        allMaterials.Add(mat);
                    }
                }
            }
        }

        if (allMaterials.Count == 0) allMaterials.Add(null);
        _model.SetMaterials(allMaterials);
        _model.CurrentMaterialIndex = 0;

        int boneStartIndex = 0;
        if (boneTransforms.Count > 0)
        {
            for (int i = 0; i < boneTransforms.Count; i++)
            {
                var boneTransform = boneTransforms[i];
                var boneCtx = EditorMeshLoader.CreateMeshContextFromBone(boneTransform, boneToIndex);
                boneCtx.ParentModelContext = _model;
                _model.Add(boneCtx);

                boneCtx.BindPose = boneBindPoses.TryGetValue(boneTransform, out Matrix4x4 bindPose)
                    ? bindPose
                    : boneTransform.worldToLocalMatrix;

                boneCtx.BonePoseData = new BonePoseData { IsActive = true };
            }
            boneStartIndex = boneTransforms.Count;
        }

        var goToIndex    = new Dictionary<GameObject, int>();
        var boneGOs      = new HashSet<GameObject>(boneTransforms.Select(b => b.gameObject));
        var folderObjects = new HashSet<GameObject>();

        foreach (var go in gameObjects)
        {
            if ((go.name == "Armature" || go.name == "Meshes") &&
                go.GetComponent<MeshFilter>() == null &&
                go.GetComponent<SkinnedMeshRenderer>() == null)
                folderObjects.Add(go);
        }

        var meshGameObjects = gameObjects
            .Where(go => !boneGOs.Contains(go) && !folderObjects.Contains(go))
            .ToList();

        for (int i = 0; i < meshGameObjects.Count; i++)
            goToIndex[meshGameObjects[i]] = boneStartIndex + i;

        for (int i = 0; i < meshGameObjects.Count; i++)
        {
            var meshContext = EditorMeshLoader.CreateMeshContextFromGameObject(
                meshGameObjects[i], goToIndex, materialToIndex, boneToIndex);
            meshContext.ParentModelContext = _model;
            _model.Add(meshContext);
        }

        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var ctx = _meshContextList[i];
            if (ctx == null) continue;
            int depth = 0, current = ctx.HierarchyParentIndex, safety = 100;
            while (current >= 0 && current < _meshContextList.Count && safety-- > 0)
            {
                depth++;
                current = _meshContextList[current].HierarchyParentIndex;
            }
            ctx.Depth = depth;
        }

        SetSelectedIndex(boneStartIndex < _meshContextList.Count ? boneStartIndex : 0);

        if (_undoController != null && _meshContextList.Count > 0)
        {
            _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>();
            var newSelectedIndices = _model.CaptureAllSelectedIndices();
            var addedContexts = new List<(int Index, MeshContext MeshContext)>();
            for (int i = 0; i < _meshContextList.Count; i++)
                addedContexts.Add((i, _meshContextList[i]));
            _undoController.RecordMeshContextsAdd(
                addedContexts, oldSelectedIndices, newSelectedIndices,
                null, null, _model.Materials, _model.CurrentMaterialIndex);
        }

        InitVertexOffsets();

        if (detectNamedMirror)
            PMXImporter.DetectNamedMirrors(_meshContextList, boneStartIndex);

        _model?.OnListChanged?.Invoke();
        _unifiedAdapter?.NotifyTopologyChanged();
        Repaint();

        Debug.Log($"[LoadHierarchyFromGameObject] Imported {boneTransforms.Count} bones + {meshGameObjects.Count} meshes from '{rootGameObject.name}'");
    }

    /// <summary>メッシュリストをクリア（内部用・Undo記録なし）</summary>
    private void ClearMeshContextListInternal()
    {
        foreach (var ctx in _meshContextList)
        {
            if (ctx.UnityMesh != null)
                DestroyImmediate(ctx.UnityMesh);
        }
        _meshContextList.Clear();
        SetSelectedIndex(-1);
        _selectionState?.ClearAll();
    }

    /// <summary>ロードしたメッシュを追加（マルチマテリアル対応）</summary>
    private void AddLoadedMesh(Mesh sourceMesh, string name, Material[] materials = null, Transform sourceTransform = null)
    {
        var meshObject = new MeshObject(name);
        meshObject.FromUnityMesh(sourceMesh, true);

        var meshContext = new MeshContext
        {
            Name = name,
            MeshObject = meshObject,
            OriginalPositions = (Vector3[])meshObject.Positions.Clone()
        };

        if (sourceTransform != null)
        {
            meshContext.BoneTransform.Position = sourceTransform.localPosition;
            meshContext.BoneTransform.Rotation = sourceTransform.localEulerAngles;
            meshContext.BoneTransform.Scale    = sourceTransform.localScale;

            bool isDefault =
                sourceTransform.localPosition == Vector3.zero &&
                sourceTransform.localEulerAngles == Vector3.zero &&
                sourceTransform.localScale == Vector3.one;
            meshContext.BoneTransform.UseLocalTransform = !isDefault;
        }

        if (materials != null && materials.Length > 0)
        {
            _model.SetMaterials(materials);
        }
        else if (_defaultMaterials != null && _defaultMaterials.Count > 0)
        {
            _model.SetMaterials(_defaultMaterials);
            _model.CurrentMaterialIndex = Mathf.Clamp(_defaultCurrentMaterialIndex, 0, _model.MaterialCount - 1);
            if (meshContext.MeshObject != null && _model.CurrentMaterialIndex > 0)
            {
                foreach (var face in meshContext.MeshObject.Faces)
                    face.MaterialIndex = _model.CurrentMaterialIndex;
            }
        }

        Mesh displayMesh = meshContext.MeshObject.ToUnityMesh();
        displayMesh.name = name;
        displayMesh.hideFlags = HideFlags.HideAndDontSave;
        meshContext.UnityMesh = displayMesh;

        var oldSelectedIndices2 = _model.CaptureAllSelectedIndices();
        int insertIndex = _meshContextList.Count;

        meshContext.ParentModelContext = _model;
        _model.Add(meshContext);
        SetSelectedIndex(_meshContextList.Count - 1);
        InitVertexOffsets();

        if (_undoController != null)
        {
            _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>();
            var newSelectedIndices2 = _model.CaptureAllSelectedIndices();
            _undoController.RecordMeshContextAdd(meshContext, insertIndex, oldSelectedIndices2, newSelectedIndices2);
        }

        _unifiedAdapter?.NotifyTopologyChanged();
        Repaint();
    }
}
