// Assets/Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_MeshLoad.cs
// メッシュ読み込み機能
// - アセットから読み込み
// - プレファブから読み込み
// - 選択オブジェクトから読み込み
// - 階層構造のインポート

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;

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
    /// プレファブから読み込み
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
        if (meshFilters.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "プレファブにMeshFilterが見つかりませんでした", "OK");
            return;
        }

        // 複数メッシュがある場合は全て追加
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

        // 階層構造をインポート
        LoadHierarchyFromGameObject(selected);
    }

    /// <summary>
    /// GameObjectの階層構造をメッシュリストとしてインポート
    /// </summary>
    /// <param name="rootGameObject">ルートGameObject</param>
    private void LoadHierarchyFromGameObject(GameObject rootGameObject)
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

        // Undo記録用：変更前の状態を保存
        int oldSelectedIndex = _selectedIndex;
        var oldMeshContextList = new List<MeshContext>(_meshContextList);

        // 既存のメッシュリストをクリア
        ClearMeshContextListInternal();

        // GameObjectからインデックスへのマッピング
        var goToIndex = new Dictionary<GameObject, int>();
        for (int i = 0; i < gameObjects.Count; i++)
        {
            goToIndex[gameObjects[i]] = i;
        }

        // マテリアルを収集（重複排除）
        var allMaterials = new List<Material>();
        var materialToIndex = new Dictionary<Material, int>();

        foreach (var go in gameObjects)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterials != null)
            {
                foreach (var mat in renderer.sharedMaterials)
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

        // 各GameObjectからMeshContextを作成
        for (int i = 0; i < gameObjects.Count; i++)
        {
            var go = gameObjects[i];
            var meshContext = CreateMeshContextFromGameObject(go, goToIndex, materialToIndex);

            _meshContextList.Add(meshContext);
            meshContext.MaterialOwner = _model;
        }

        // 最初のメッシュを選択
        _selectedIndex = _meshContextList.Count > 0 ? 0 : -1;

        // UndoControllerを更新
        if (_undoController != null && _meshContextList.Count > 0)
        {
            var firstContext = _meshContextList[0];
            _undoController.MeshUndoContext.MeshObject = firstContext.MeshObject;
            _undoController.MeshUndoContext.TargetMesh = firstContext.UnityMesh;
            _undoController.MeshUndoContext.OriginalPositions = firstContext.OriginalPositions;
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
        Repaint();

        Debug.Log($"[LoadHierarchyFromGameObject] Imported {gameObjects.Count} objects from '{rootGameObject.name}'");
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
    /// GameObjectからMeshContextを作成
    /// </summary>
    private MeshContext CreateMeshContextFromGameObject(
        GameObject go,
        Dictionary<GameObject, int> goToIndex,
        Dictionary<Material, int> materialToIndex)
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

        // MeshFilterからメッシュを取得
        var meshFilter = go.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            meshContext.MeshObject.FromUnityMesh(meshFilter.sharedMesh, true);

            // マテリアルインデックスをFaceに設定
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterials != null)
            {
                // サブメッシュごとのマテリアルインデックスを設定
                var sourceMesh = meshFilter.sharedMesh;
                for (int subMeshIdx = 0; subMeshIdx < sourceMesh.subMeshCount; subMeshIdx++)
                {
                    Material mat = subMeshIdx < renderer.sharedMaterials.Length
                        ? renderer.sharedMaterials[subMeshIdx]
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

        Repaint();
    }
}