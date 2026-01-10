// Assets/Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_MeshLoad.cs
// メッシュ読み込み機能
// - アセットから読み込み
// - プレファブから読み込み
// - 選択オブジェクトから読み込み（MeshFilter + SkinnedMeshRenderer対応）
// - 階層構造のインポート

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using MeshFactory;
using MeshFactory.Tools;

public partial class SimpleMeshFactory
{
    // ================================================================
    // メッシュ読み出し機能
    // ================================================================

    /// <summary>
    /// メッシュアセットから読み込み（Transformなし）
    /// </summary>
    private void LoadMeshFromAsset()
    {
        string path = EditorUtility.OpenFilePanel("Select UnityMesh Asset", "Assets", "asset,fbx,obj");
        if (string.IsNullOrEmpty(path))
            return;

        // プロジェクト相対パスに変換
        if (path.StartsWith(Application.dataPath))
        {
            path = "Assets" + path.Substring(Application.dataPath.Length);
        }

        Mesh loadedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (loadedMesh == null)
        {
            // FBX/OBJの場合、サブアセットからメッシュを探す
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var asset in allAssets)
            {
                if (asset is Mesh m)
                {
                    loadedMesh = m;
                    break;
                }
            }
        }

        if (loadedMesh != null)
        {
            AddLoadedMesh(loadedMesh, loadedMesh.name);// Transform渡せない
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "メッシュを読み込めませんでした", "OK");
        }
    }

    /// <summary>
    /// プレファブから読み込み（MeshFilter + SkinnedMeshRenderer対応）
    /// </summary>
    private void LoadMeshFromPrefab()
    {
        string path = EditorUtility.OpenFilePanel("Select Prefab", "Assets", "prefab");
        if (string.IsNullOrEmpty(path))
            return;

        if (path.StartsWith(Application.dataPath))
        {
            path = "Assets" + path.Substring(Application.dataPath.Length);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Error", "プレファブを読み込めませんでした", "OK");
            return;
        }

        // MeshFilterからメッシュを取得
        var meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
        var skinnedMeshRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        if (meshFilters.Length == 0 && skinnedMeshRenderers.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "プレファブにMeshFilterまたはSkinnedMeshRendererが見つかりませんでした", "OK");
            return;
        }

        // MeshFilterから追加
        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh != null)
            {
                string meshName = $"{prefab.name}_{mf.sharedMesh.name}";

                // マテリアル取得
                Material[] mats = null;
                var renderer = mf.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0)
                {
                    mats = renderer.sharedMaterials;
                }

                AddLoadedMesh(mf.sharedMesh, meshName, mats, mf.transform);
            }
        }

        // SkinnedMeshRendererから追加
        foreach (var smr in skinnedMeshRenderers)
        {
            if (smr.sharedMesh != null)
            {
                string meshName = $"{prefab.name}_{smr.sharedMesh.name}";

                // マテリアル取得
                Material[] mats = null;
                if (smr.sharedMaterials != null && smr.sharedMaterials.Length > 0)
                {
                    mats = smr.sharedMaterials;
                }

                AddLoadedMesh(smr.sharedMesh, meshName, mats, smr.transform);
            }
        }
    }

    /// <summary>
    /// 選択中のオブジェクトから階層構造を含めて読み込み
    /// GameObjectの親子関係をHierarchyParentIndexに保持
    /// </summary>
    private void LoadMeshFromSelection()
    {
        var selected = Selection.activeGameObject;
        if (selected == null)
        {
            // メッシュアセットが選択されている場合（Transformなし）
            var selectedMesh = Selection.activeObject as Mesh;
            if (selectedMesh != null)
            {
                AddLoadedMesh(selectedMesh, selectedMesh.name);
                return;
            }

            EditorUtility.DisplayDialog("Info", "GameObjectまたはMeshを選択してください", "OK");
            return;
        }

        // SkinnedMeshRendererがあるかチェック
        var skinnedRenderers = selected.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        if (skinnedRenderers.Length > 0)
        {
            // ボーン取り込みダイアログを表示
            ShowSkinnedMeshImportDialog(selected, skinnedRenderers);
        }
        else
        {
            // 従来通り階層構造をインポート
            LoadHierarchyFromGameObject(selected, null);
        }
    }

    // ================================================================
    // ボーン取り込みダイアログ
    // ================================================================

    /// <summary>
    /// SkinnedMeshRenderer検出時のインポートダイアログ表示
    /// </summary>
    private void ShowSkinnedMeshImportDialog(GameObject rootObject, SkinnedMeshRenderer[] skinnedRenderers)
    {
        // ルートボーンを自動検出
        Transform detectedRootBone = DetectBestRootBone(skinnedRenderers);
        int boneCount = detectedRootBone != null ? CountDescendants(detectedRootBone) + 1 : 0;

        // ダイアログ表示
        var dialog = SkinnedMeshImportDialog.Show(rootObject, detectedRootBone, boneCount, skinnedRenderers.Length);
        dialog.OnImport = (importMesh, importBones, selectedRootBone) =>
        {
            LoadHierarchyFromGameObject(rootObject, importBones ? selectedRootBone : null);
        };
    }

    /// <summary>
    /// 複数のSkinnedMeshRendererから最適なルートボーンを検出
    /// </summary>
    private Transform DetectBestRootBone(SkinnedMeshRenderer[] smrs)
    {
        if (smrs == null || smrs.Length == 0) return null;

        // 全smrのrootBoneを収集
        var rootBones = smrs
            .Where(s => s != null && s.rootBone != null)
            .Select(s => s.rootBone)
            .Distinct()
            .ToList();

        if (rootBones.Count == 0) return null;
        if (rootBones.Count == 1) return rootBones[0];

        // 複数ある場合 → 最も階層が高いものを選択
        return rootBones
            .OrderBy(b => GetHierarchyDepth(b))
            .First();
    }

    /// <summary>
    /// Transformの階層深度を取得
    /// </summary>
    private int GetHierarchyDepth(Transform t)
    {
        int depth = 0;
        while (t != null && t.parent != null)
        {
            depth++;
            t = t.parent;
        }
        return depth;
    }

    /// <summary>
    /// 子孫の数をカウント
    /// </summary>
    private int CountDescendants(Transform t)
    {
        if (t == null) return 0;
        int count = 0;
        foreach (Transform child in t)
        {
            count += 1 + CountDescendants(child);
        }
        return count;
    }

    /// <summary>
    /// GameObjectの階層構造をメッシュリストとしてインポート
    /// </summary>
    /// <param name="rootGameObject">ルートGameObject</param>
    /// <param name="boneRootTransform">ボーン取り込み時のルートTransform（nullの場合はボーン取り込みなし）</param>
    private void LoadHierarchyFromGameObject(GameObject rootGameObject, Transform boneRootTransform)
    {
        if (rootGameObject == null) return;

        // GameObjectを深さ優先で収集（Unity Hierarchyの表示順序に準拠）
        var gameObjects = new List<GameObject>();
        CollectGameObjectsDepthFirst(rootGameObject, gameObjects);

        if (gameObjects.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "インポート対象のGameObjectがありません", "OK");
            return;
        }

        // ボーン階層を収集（ボーン取り込み時）
        var boneTransforms = new List<Transform>();
        var boneToIndex = new Dictionary<Transform, int>();
        if (boneRootTransform != null)
        {
            CollectBoneTransformsDepthFirst(boneRootTransform, boneTransforms);
            for (int i = 0; i < boneTransforms.Count; i++)
            {
                boneToIndex[boneTransforms[i]] = i;
            }
            Debug.Log($"[LoadHierarchyFromGameObject] Collected {boneTransforms.Count} bones from '{boneRootTransform.name}'");
        }

        // Undo記録用：変更前の状態を保存
        int oldSelectedIndex = _selectedIndex;
        var oldMeshContextList = new List<MeshContext>(_meshContextList);

        // 既存のメッシュリストをクリア
        ClearMeshContextListInternal();

        // マテリアルを収集（重複排除）- MeshRenderer + SkinnedMeshRenderer両方対応
        var allMaterials = new List<Material>();
        var materialToIndex = new Dictionary<Material, int>();

        foreach (var go in gameObjects)
        {
            Material[] sharedMats = GetSharedMaterials(go);
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

        // マテリアルリストが空の場合はデフォルトを追加
        if (allMaterials.Count == 0)
        {
            allMaterials.Add(null);
        }

        // モデルのマテリアルを設定
        _model.SetMaterials(allMaterials);
        _model.CurrentMaterialIndex = 0;

        // ボーンを先に追加（PMXと同様）
        int boneStartIndex = 0;
        if (boneTransforms.Count > 0)
        {
            for (int i = 0; i < boneTransforms.Count; i++)
            {
                var boneCtx = CreateMeshContextFromBone(boneTransforms[i], boneToIndex);
                _meshContextList.Add(boneCtx);
                boneCtx.MaterialOwner = _model;
            }
            boneStartIndex = boneTransforms.Count;
        }

        // GameObjectからインデックスへのマッピング（ボーンオフセット込み）
        var goToIndex = new Dictionary<GameObject, int>();
        
        // ボーンTransformのGameObjectを除外するためのセット
        var boneGameObjects = new HashSet<GameObject>();
        foreach (var bone in boneTransforms)
        {
            boneGameObjects.Add(bone.gameObject);
        }
        
        // ボーンでないGameObjectのみをフィルタリング
        var meshGameObjects = new List<GameObject>();
        foreach (var go in gameObjects)
        {
            if (!boneGameObjects.Contains(go))
            {
                meshGameObjects.Add(go);
            }
        }
        
        // インデックスマッピング（メッシュGameObjectのみ）
        for (int i = 0; i < meshGameObjects.Count; i++)
        {
            goToIndex[meshGameObjects[i]] = boneStartIndex + i;
        }

        // 各GameObjectからMeshContextを作成（ボーンでないもののみ）
        for (int i = 0; i < meshGameObjects.Count; i++)
        {
            var go = meshGameObjects[i];
            var meshContext = CreateMeshContextFromGameObject(go, goToIndex, materialToIndex, boneToIndex);

            _meshContextList.Add(meshContext);
            meshContext.MaterialOwner = _model;
        }

        // 最初のメッシュを選択（ボーンがあれば最初のメッシュ、なければ0）
        _selectedIndex = boneStartIndex < _meshContextList.Count ? boneStartIndex : 0;

        // UndoControllerを更新
        if (_undoController != null && _meshContextList.Count > 0)
        {
            var firstMeshContext = _meshContextList[_selectedIndex];
            _undoController.MeshUndoContext.MeshObject = firstMeshContext.MeshObject;
            _undoController.MeshUndoContext.TargetMesh = firstMeshContext.UnityMesh;
            _undoController.MeshUndoContext.OriginalPositions = firstMeshContext.OriginalPositions;
            _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>();

            // Undo記録（メッシュリスト置換）
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;

            // 複数メッシュ追加をバッチ記録
            var addedContexts = new List<(int Index, MeshContext MeshContext)>();
            for (int i = 0; i < _meshContextList.Count; i++)
            {
                addedContexts.Add((i, _meshContextList[i]));
            }
            _undoController.RecordMeshContextsAdd(
                addedContexts,
                oldSelectedIndex,
                _selectedIndex,
                null, null,
                _model.Materials,
                _model.CurrentMaterialIndex);
        }

        InitVertexOffsets();
        
        // 統合システムにトポロジー変更を通知
        _unifiedAdapter?.NotifyTopologyChanged();
        
        Repaint();

        Debug.Log($"[LoadHierarchyFromGameObject] Imported {boneTransforms.Count} bones + {meshGameObjects.Count} meshes from '{rootGameObject.name}'");
    }

    /// <summary>
    /// ボーンTransformを深さ優先で収集
    /// </summary>
    private void CollectBoneTransformsDepthFirst(Transform bone, List<Transform> result)
    {
        if (bone == null) return;

        result.Add(bone);

        foreach (Transform child in bone)
        {
            CollectBoneTransformsDepthFirst(child, result);
        }
    }

    /// <summary>
    /// TransformからボーンMeshContextを作成
    /// </summary>
    private MeshContext CreateMeshContextFromBone(Transform bone, Dictionary<Transform, int> boneToIndex)
    {
        var meshObject = new MeshObject(bone.name)
        {
            Type = MeshType.Bone
        };

        var boneTransform = new BoneTransform
        {
            Position = bone.localPosition,
            Rotation = bone.localEulerAngles,
            Scale = bone.localScale,
            UseLocalTransform = true
        };
        meshObject.BoneTransform = boneTransform;

        var meshContext = new MeshContext
        {
            MeshObject = meshObject,
            Type = MeshType.Bone,
            IsVisible = true
        };

        // 親インデックスを設定
        if (bone.parent != null && boneToIndex.TryGetValue(bone.parent, out int parentIndex))
        {
            meshContext.ParentIndex = parentIndex;
        }
        else
        {
            meshContext.ParentIndex = -1;
        }

        // ローカルトランスフォームをMeshContextにも設定
        meshContext.BoneTransform.Position = bone.localPosition;
        meshContext.BoneTransform.Rotation = bone.localEulerAngles;
        meshContext.BoneTransform.Scale = bone.localScale;
        meshContext.BoneTransform.UseLocalTransform = true;

        // 空のOriginalPositionsとUnityMesh
        meshContext.OriginalPositions = new Vector3[0];
        meshContext.UnityMesh = new Mesh { name = bone.name };

        return meshContext;
    }

    /// <summary>
    /// GameObjectを深さ優先で収集（Unity Hierarchyの表示順序に準拠）
    /// </summary>
    private void CollectGameObjectsDepthFirst(GameObject go, List<GameObject> result)
    {
        if (go == null) return;

        // 自分自身を追加
        result.Add(go);

        // 子を再帰的に収集（Hierarchy表示順＝sibling index順）
        for (int i = 0; i < go.transform.childCount; i++)
        {
            var child = go.transform.GetChild(i).gameObject;
            CollectGameObjectsDepthFirst(child, result);
        }
    }

    /// <summary>
    /// GameObjectからMeshContextを作成（MeshFilter + SkinnedMeshRenderer対応）
    /// </summary>
    private MeshContext CreateMeshContextFromGameObject(
        GameObject go,
        Dictionary<GameObject, int> goToIndex,
        Dictionary<Material, int> materialToIndex,
        Dictionary<Transform, int> boneToIndex = null)
    {
        var meshContext = new MeshContext
        {
            MeshObject = new MeshObject(go.name)
        };

        // 親インデックスを設定（HierarchyParentIndex）
        var parentTransform = go.transform.parent;
        if (parentTransform != null && goToIndex.TryGetValue(parentTransform.gameObject, out int parentIndex))
        {
            meshContext.HierarchyParentIndex = parentIndex;
        }
        else
        {
            meshContext.HierarchyParentIndex = -1; // ルート
        }

        // ローカルトランスフォームをBoneTransformに設定
        meshContext.BoneTransform.Position = go.transform.localPosition;
        meshContext.BoneTransform.Rotation = go.transform.localEulerAngles;
        meshContext.BoneTransform.Scale = go.transform.localScale;

        // デフォルト値でなければUseLocalTransformを有効化
        bool isDefaultTransform =
            go.transform.localPosition == Vector3.zero &&
            go.transform.localEulerAngles == Vector3.zero &&
            go.transform.localScale == Vector3.one;
        meshContext.BoneTransform.UseLocalTransform = !isDefaultTransform;

        // メッシュとマテリアルを取得（MeshFilter優先、なければSkinnedMeshRenderer）
        Mesh sourceMesh = null;
        Material[] sharedMats = null;
        Dictionary<int, int> boneIndexRemap = null;

        var meshFilter = go.GetComponent<MeshFilter>();
        var skinnedMeshRenderer = go.GetComponent<SkinnedMeshRenderer>();

        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            // MeshFilterから取得
            sourceMesh = meshFilter.sharedMesh;
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                sharedMats = renderer.sharedMaterials;
            }
        }
        else if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
        {
            // SkinnedMeshRendererから取得
            sourceMesh = skinnedMeshRenderer.sharedMesh;
            sharedMats = skinnedMeshRenderer.sharedMaterials;

            // ボーンインデックスの再マッピング（boneToIndexがある場合）
            if (boneToIndex != null && skinnedMeshRenderer.bones != null)
            {
                // smr.bones[i] → boneToIndex[smr.bones[i]] のマッピングを構築
                boneIndexRemap = new Dictionary<int, int>();
                for (int i = 0; i < skinnedMeshRenderer.bones.Length; i++)
                {
                    var bone = skinnedMeshRenderer.bones[i];
                    if (bone != null && boneToIndex.TryGetValue(bone, out int meshCtxBoneIndex))
                    {
                        boneIndexRemap[i] = meshCtxBoneIndex;
                    }
                }
            }
        }

        // メッシュデータを変換
        if (sourceMesh != null)
        {
            // BoneWeight情報も含めて読み込み
            meshContext.MeshObject.FromUnityMesh(sourceMesh, true);

            // BoneWeightのインデックスを再マッピング
            if (boneIndexRemap != null && boneIndexRemap.Count > 0)
            {
                RemapBoneWeightIndices(meshContext.MeshObject, boneIndexRemap);
                Debug.Log($"[CreateMeshContextFromGameObject] Remapped {boneIndexRemap.Count} bone indices for '{go.name}'");
            }

            // マテリアルインデックスをFaceに設定
            if (sharedMats != null)
            {
                for (int subMeshIdx = 0; subMeshIdx < sourceMesh.subMeshCount; subMeshIdx++)
                {
                    Material mat = subMeshIdx < sharedMats.Length
                        ? sharedMats[subMeshIdx]
                        : null;

                    int globalMatIndex = 0;
                    if (mat != null && materialToIndex.TryGetValue(mat, out int idx))
                    {
                        globalMatIndex = idx;
                    }

                    // このサブメッシュに対応するFaceのMaterialIndexを設定
                    // FromUnityMeshで作成されたFaceはsubMeshIndexがMaterialIndexに設定されている
                    foreach (var face in meshContext.MeshObject.Faces)
                    {
                        if (face.MaterialIndex == subMeshIdx)
                        {
                            face.MaterialIndex = globalMatIndex;
                        }
                    }
                }
            }
        }

        // OriginalPositionsとUnityMeshを設定
        meshContext.OriginalPositions = meshContext.MeshObject.Vertices
            .Select(v => v.Position).ToArray();
        meshContext.UnityMesh = meshContext.MeshObject.ToUnityMesh();
        meshContext.UnityMesh.name = go.name;

        return meshContext;
    }

    /// <summary>
    /// MeshObjectのBoneWeightインデックスを再マッピング
    /// </summary>
    private void RemapBoneWeightIndices(MeshObject meshObject, Dictionary<int, int> remap)
    {
        foreach (var vertex in meshObject.Vertices)
        {
            if (!vertex.HasBoneWeight) continue;

            var bw = vertex.BoneWeight.Value;
            var newBw = new BoneWeight
            {
                boneIndex0 = remap.TryGetValue(bw.boneIndex0, out int idx0) ? idx0 : bw.boneIndex0,
                boneIndex1 = remap.TryGetValue(bw.boneIndex1, out int idx1) ? idx1 : bw.boneIndex1,
                boneIndex2 = remap.TryGetValue(bw.boneIndex2, out int idx2) ? idx2 : bw.boneIndex2,
                boneIndex3 = remap.TryGetValue(bw.boneIndex3, out int idx3) ? idx3 : bw.boneIndex3,
                weight0 = bw.weight0,
                weight1 = bw.weight1,
                weight2 = bw.weight2,
                weight3 = bw.weight3
            };
            vertex.BoneWeight = newBw;
        }
    }

    /// <summary>
    /// GameObjectからマテリアル配列を取得（MeshRenderer + SkinnedMeshRenderer対応）
    /// </summary>
    private Material[] GetSharedMaterials(GameObject go)
    {
        if (go == null) return null;

        // MeshRenderer優先
        var meshRenderer = go.GetComponent<MeshRenderer>();
        if (meshRenderer != null && meshRenderer.sharedMaterials != null && meshRenderer.sharedMaterials.Length > 0)
        {
            return meshRenderer.sharedMaterials;
        }

        // SkinnedMeshRenderer
        var skinnedMeshRenderer = go.GetComponent<SkinnedMeshRenderer>();
        if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMaterials != null && skinnedMeshRenderer.sharedMaterials.Length > 0)
        {
            return skinnedMeshRenderer.sharedMaterials;
        }

        return null;
    }

    /// <summary>
    /// メッシュリストをクリア（内部用・Undo記録なし）
    /// </summary>
    private void ClearMeshContextListInternal()
    {
        // 既存メッシュのリソースを解放
        foreach (var ctx in _meshContextList)
        {
            if (ctx.UnityMesh != null)
            {
                DestroyImmediate(ctx.UnityMesh);
            }
        }
        _meshContextList.Clear();
        _selectedIndex = -1;

        // 選択をクリア
        _selectedVertices.Clear();
        _selectionState?.ClearAll();
    }

    /// <summary>
    /// ロードしたメッシュを追加（マルチマテリアル対応）
    /// </summary>
    private void AddLoadedMesh(Mesh sourceMesh, string name, Material[] materials = null, Transform sourceTransform = null)
    {
        // Unity MeshからMeshObjectに変換
        var meshObject = new MeshObject(name);
        meshObject.FromUnityMesh(sourceMesh, true);

        var meshContext = new MeshContext
        {
            Name = name,
            MeshObject = meshObject,
            OriginalPositions = meshObject.Vertices.Select(v => v.Position).ToArray()
        };

        // BoneTransform: 元オブジェクトのTransformを設定
        if (sourceTransform != null)
        {
            meshContext.BoneTransform.Position = sourceTransform.localPosition;
            meshContext.BoneTransform.Rotation = sourceTransform.localEulerAngles;
            meshContext.BoneTransform.Scale = sourceTransform.localScale;

            // デフォルト値でなければ UseLocalTransform を有効化
            bool isDefault =
                sourceTransform.localPosition == Vector3.zero &&
                sourceTransform.localEulerAngles == Vector3.zero &&
                sourceTransform.localScale == Vector3.one;

            meshContext.BoneTransform.UseLocalTransform = !isDefault;
        }


        // マテリアル設定
        if (materials != null && materials.Length > 0)
        {
            // 引数で指定されたマテリアルを使用（読み込み元のマテリアル）
            _model.SetMaterials(materials);
            // 引数指定の場合はCurrentMaterialIndexは0のまま、FaceのMaterialIndexもそのまま
        }
        else if (_defaultMaterials != null && _defaultMaterials.Count > 0)
        {
            // デフォルトマテリアルをコピー
            _model.SetMaterials(_defaultMaterials);
            _model.CurrentMaterialIndex = Mathf.Clamp(_defaultCurrentMaterialIndex, 0, _model.MaterialCount - 1);

            // 全FaceにカレントマテリアルIndexを適用
            if (meshContext.MeshObject != null && _model.CurrentMaterialIndex > 0)
            {
                foreach (var face in meshContext.MeshObject.Faces)
                {
                    face.MaterialIndex = _model.CurrentMaterialIndex;
                }
            }
        }

        // 表示用Unity Meshを作成（MaterialIndex適用後）
        Mesh displayMesh = meshContext.MeshObject.ToUnityMesh();
        displayMesh.name = name;
        displayMesh.hideFlags = HideFlags.HideAndDontSave;
        meshContext.UnityMesh = displayMesh;

        // Undo記録用に変更前の状態を保存
        int oldSelectedIndex = _selectedIndex;
        int insertIndex = _meshContextList.Count;

        _meshContextList.Add(meshContext);
        meshContext.MaterialOwner = _model;  // Phase 1: Materials 委譲用
        _selectedIndex = _meshContextList.Count - 1;
        InitVertexOffsets();

        // 注意: LoadMeshContextToUndoControllerは呼ばない（VertexEditStack.Clear()を避けるため）
        // MeshContextに必要な情報だけを設定
        if (_undoController != null)
        {
            _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
            _undoController.MeshUndoContext.TargetMesh = meshContext.UnityMesh;
            _undoController.MeshUndoContext.OriginalPositions = meshContext.OriginalPositions;
            _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>();

            // Undo記録（メッシュリスト追加）
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
            _undoController.RecordMeshContextAdd(meshContext, insertIndex, oldSelectedIndex, _selectedIndex);
        }

        // 統合システムにトポロジー変更を通知
        _unifiedAdapter?.NotifyTopologyChanged();

        Repaint();
    }
}
