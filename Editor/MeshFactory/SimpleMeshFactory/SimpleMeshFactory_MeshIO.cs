// Assets/Editor/SimpleMeshFactory.MeshIO.cs
// メッシュ入出力（読み込み、保存、エクスポート、インポート）
// Phase2: マルチマテリアル対応版
// DefaultMaterials対応版
// 追加モード・自動マージ対応版
// Phase4: MeshMergeHelper使用に変更

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools;
using MeshFactory.Serialization;
using MeshFactory.UndoSystem;
using MeshFactory.Utilities;
using MeshFactory.Symmetry;

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
        _model.Materials.Clear();
        _model.Materials.AddRange(allMaterials);
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
    /// GameObjectを深さ優先で収集（Unity Hierarchy表示順序に準拠）
    /// </summary>
    private void CollectGameObjectsDepthFirst(GameObject go, List<GameObject> result)
    {
        result.Add(go);

        // 子を GetSiblingIndex 順序で走査（Unity Hierarchy の表示順序）
        int childCount = go.transform.childCount;
        for (int i = 0; i < childCount; i++)
        {
            var child = go.transform.GetChild(i).gameObject;
            CollectGameObjectsDepthFirst(child, result);
        }
    }

    /// <summary>
    /// GameObjectからMeshContextを作成
    /// </summary>
    /// <param name="go">対象GameObject</param>
    /// <param name="goToIndex">GameObjectからインデックスへのマッピング</param>
    /// <param name="materialToIndex">マテリアルからインデックスへのマッピング</param>
    /// <returns>作成されたMeshContext</returns>
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

        // ローカルトランスフォームをExportSettingsに設定
        meshContext.ExportSettings.Position = go.transform.localPosition;
        meshContext.ExportSettings.Rotation = go.transform.localEulerAngles;
        meshContext.ExportSettings.Scale = go.transform.localScale;

        // デフォルト値でなければUseLocalTransformを有効化
        bool isDefaultTransform =
            go.transform.localPosition == Vector3.zero &&
            go.transform.localEulerAngles == Vector3.zero &&
            go.transform.localScale == Vector3.one;
        meshContext.ExportSettings.UseLocalTransform = !isDefaultTransform;

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

        // OriginalPositions設定
        meshContext.OriginalPositions = meshContext.MeshObject.Vertices
            .Select(v => v.Position)
            .ToArray();

        // 表示用Unity Meshを作成
        Mesh displayMesh = meshContext.MeshObject.ToUnityMesh();
        displayMesh.name = go.name;
        displayMesh.hideFlags = HideFlags.HideAndDontSave;
        meshContext.UnityMesh = displayMesh;

        return meshContext;
    }

    /// <summary>
    /// メッシュリストを内部的にクリア（Undo記録なし）
    /// </summary>
    private void ClearMeshContextListInternal()
    {
        foreach (var ctx in _meshContextList)
        {
            if (ctx.UnityMesh != null)
            {
                DestroyImmediate(ctx.UnityMesh);
            }
            ctx.ClearSymmetryCache();
        }
        _meshContextList.Clear();
        _selectedIndex = -1;
    }

    /// <summary>
    /// 読み込んだメッシュを追加（MeshObjectに変換）
    /// </summary>
    /// <param name="sourceMesh">元のUnity UnityMesh</param>
    /// <param name="name">メッシュ名</param>
    /// <param name="materials">マテリアル配列（オプション）</param>
    /// <param name="sourceTransform">元オブジェクトのTransform（オプション）</param>
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

        // ExportSettings: 元オブジェクトのTransformを設定
        if (sourceTransform != null)
        {
            meshContext.ExportSettings.Position = sourceTransform.localPosition;
            meshContext.ExportSettings.Rotation = sourceTransform.localEulerAngles;
            meshContext.ExportSettings.Scale = sourceTransform.localScale;

            // デフォルト値でなければ UseLocalTransform を有効化
            bool isDefault =
                sourceTransform.localPosition == Vector3.zero &&
                sourceTransform.localEulerAngles == Vector3.zero &&
                sourceTransform.localScale == Vector3.one;

            meshContext.ExportSettings.UseLocalTransform = !isDefault;
        }


        // マテリアル設定
        if (materials != null && materials.Length > 0)
        {
            // 引数で指定されたマテリアルを使用（読み込み元のマテリアル）
            _model.Materials.Clear();
            foreach (var mat in materials)
            {
                _model.Materials.Add(mat);
            }
            // 引数指定の場合はCurrentMaterialIndexは0のまま、FaceのMaterialIndexもそのまま
        }
        else if (_defaultMaterials != null && _defaultMaterials.Count > 0)
        {
            // デフォルトマテリアルをコピー
            _model.Materials = new List<Material>(_defaultMaterials);
            _model.CurrentMaterialIndex = Mathf.Clamp(_defaultCurrentMaterialIndex, 0, _model.Materials.Count - 1);

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

    /// <summary>
    /// メッシュ作成ウインドウからのコールバック（MeshObject版 - 四角形を保持）
    /// </summary>
    private void OnMeshObjectCreated(MeshObject meshObject, string name)
    {
        // 追加モードかつ有効なメッシュが選択されている場合
        if (_addToCurrentMesh && _model.HasValidMeshContextSelection)
        {
            AddMeshObjectToCurrent(meshObject, name);
        }
        else
        {
            CreateNewMeshContext(meshObject, name);
        }
    }

    /// <summary>
    /// 新しいメッシュコンテキストを作成
    /// </summary>
    private void CreateNewMeshContext(MeshObject meshObject, string name)
    {
        var meshContext = new MeshContext
        {
            Name = name,
            MeshObject = meshObject.Clone(),
            OriginalPositions = meshObject.Vertices.Select(v => v.Position).ToArray()
        };

        // デフォルトマテリアルをコピー
        if (_defaultMaterials != null && _defaultMaterials.Count > 0)
        {
            _model.Materials = new List<Material>(_defaultMaterials);
            _model.CurrentMaterialIndex = Mathf.Clamp(_defaultCurrentMaterialIndex, 0, _model.Materials.Count - 1);

            // 全FaceにカレントマテリアルIndexを適用
            if (meshContext.MeshObject != null && _model.CurrentMaterialIndex > 0)
            {
                foreach (var face in meshContext.MeshObject.Faces)
                {
                    face.MaterialIndex = _model.CurrentMaterialIndex;
                }
            }
        }

        // 自動マージ（全頂点対象）- MeshMergeHelper使用
        if (_autoMergeOnCreate && meshContext.MeshObject.VertexCount >= 2)
        {
            var result = MeshMergeHelper.MergeAllVerticesAtSamePosition(meshContext.MeshObject, _autoMergeThreshold);
            if (result.RemovedVertexCount > 0)
            {
                Debug.Log($"[CreateNewMeshContext] Auto-merged {result.RemovedVertexCount} vertices");
            }
            // OriginalPositionsを更新
            meshContext.OriginalPositions = meshContext.MeshObject.Vertices.Select(v => v.Position).ToArray();
        }

        // MeshObjectから表示用Unity Meshを生成（MaterialIndex適用後）
        Mesh mesh = meshContext.MeshObject.ToUnityMesh();
        mesh.name = name;
        mesh.hideFlags = HideFlags.HideAndDontSave;
        meshContext.UnityMesh = mesh;

        Debug.Log($"[CreateNewMeshContext] name={name}, vertices={meshContext.MeshObject.VertexCount}, faces={meshContext.MeshObject.FaceCount}");

        // Undo記録用に変更前の状態を保存
        int oldSelectedIndex = _selectedIndex;
        int insertIndex = _meshContextList.Count;

        // リストに追加
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

            // Undo記録（メッシュコンテキスト追加）
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
            _undoController.RecordMeshContextAdd(meshContext, insertIndex, oldSelectedIndex, _selectedIndex);
        }

        Repaint();
    }

    /// <summary>
    /// 現在選択中のメッシュにMeshObjectを追加
    /// </summary>
    private void AddMeshObjectToCurrent(MeshObject meshObject, string name)
    {
        var meshContext = _meshContextList[_selectedIndex];
        if (meshContext.MeshObject == null)
        {
            meshContext.MeshObject = new MeshObject(meshContext.Name);
        }

        // ================================================================
        // Undo: 開始時スナップショット（ツール標準方式）
        // ================================================================
        MeshObjectSnapshot snapshotBefore = null;
        if (_undoController != null)
        {
            _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
            snapshotBefore = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);
        }

        // 追加前の頂点数を記録
        int baseVertexIndex = meshContext.MeshObject.VertexCount;

        // 頂点を追加
        foreach (var vertex in meshObject.Vertices)
        {
            meshContext.MeshObject.Vertices.Add(new Vertex(vertex.Position));
        }

        // 面を追加（頂点インデックスをオフセット）
        int materialIndex = _model.CurrentMaterialIndex;
        foreach (var face in meshObject.Faces)
        {
            var newFace = new Face();
            newFace.VertexIndices = face.VertexIndices.Select(i => i + baseVertexIndex).ToList();
            newFace.UVIndices = new List<int>(face.UVIndices);
            newFace.NormalIndices = new List<int>(face.NormalIndices);
            newFace.MaterialIndex = materialIndex;  // 現在のマテリアルを適用
            meshContext.MeshObject.Faces.Add(newFace);
        }

        // 自動マージ（追加した頂点と既存頂点の境界をマージ）- MeshMergeHelper使用
        if (_autoMergeOnCreate && meshContext.MeshObject.VertexCount >= 2)
        {
            var allVertices = new HashSet<int>(Enumerable.Range(0, meshContext.MeshObject.VertexCount));
            var result = MeshMergeHelper.MergeVerticesAtSamePosition(meshContext.MeshObject, allVertices, _autoMergeThreshold);

            if (result.RemovedVertexCount > 0)
            {
                Debug.Log($"[AddMeshObjectToCurrent] Auto-merged {result.RemovedVertexCount} vertices at boundaries");
            }
        }

        // OriginalPositionsを更新
        meshContext.OriginalPositions = meshContext.MeshObject.Vertices.Select(v => v.Position).ToArray();

        // メッシュ更新
        SyncMeshFromData(meshContext);

        // ================================================================
        // Undo: 終了時スナップショット + 記録（ツール標準方式）
        // ================================================================
        if (_undoController != null && snapshotBefore != null)
        {
            _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
            MeshObjectSnapshot snapshotAfter = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

            // 直接VertexEditStackに記録（RecordTopologyChangeのEndGroup副作用を回避）
            MeshSnapshotRecord record = new MeshSnapshotRecord(snapshotBefore, snapshotAfter);
            _undoController.VertexEditStack.Record(record, $"Merge: {name}");
            _undoController.FocusVertexEdit();
        }

        // 選択更新（カメラは変更しない）
        InitVertexOffsets(updateCamera: false);

        // 注意: LoadMeshContextToUndoControllerは呼ばない
        // SetMeshObject内で_vertexEditStack.Clear()が呼ばれるため、Undo記録が消えてしまう
        // MeshContextは既に上で設定済み、追加で必要な設定のみ行う
        if (_undoController != null)
        {
            _undoController.MeshUndoContext.TargetMesh = meshContext.UnityMesh;
            _undoController.MeshUndoContext.OriginalPositions = meshContext.OriginalPositions;
            _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>(_selectedVertices);
        }

        Debug.Log($"[AddMeshObjectToCurrent] Added {name} to {meshContext.Name}, total vertices={meshContext.MeshObject.VertexCount}, faces={meshContext.MeshObject.FaceCount}");

        Repaint();
    }

    /// <summary>
    /// 空のメッシュを作成
    /// </summary>
    private void CreateEmptyMesh()
    {
        // 空メッシュは常に新規作成（追加モードでも）
        bool wasAddMode = _addToCurrentMesh;
        _addToCurrentMesh = false;

        var meshObject = new MeshObject("Empty");
        OnMeshObjectCreated(meshObject, "Empty");

        _addToCurrentMesh = wasAddMode;
    }

    /// <summary>
    /// メッシュ作成ウインドウからのコールバック（従来版）
    /// </summary>
    private void OnMeshCreated(Mesh mesh, string name)
    {
        // Unity MeshからMeshObjectに変換
        var meshObject = new MeshObject(name);
        meshObject.FromUnityMesh(mesh, true);

        // エディタ専用の一時メッシュとしてマーク
        mesh.hideFlags = HideFlags.HideAndDontSave;

        // 元のMeshはそのまま表示用に使用
        var meshContext = new MeshContext
        {
            Name = name,
            UnityMesh = mesh,
            MeshObject = meshObject,
            OriginalPositions = meshObject.Vertices.Select(v => v.Position).ToArray()
        };

        // デフォルトマテリアルをコピー
        if (_defaultMaterials != null && _defaultMaterials.Count > 0)
        {
            _model.Materials = new List<Material>(_defaultMaterials);
            _model.CurrentMaterialIndex = Mathf.Clamp(_defaultCurrentMaterialIndex, 0, _model.Materials.Count - 1);

            // 全FaceにカレントマテリアルIndexを適用
            if (meshContext.MeshObject != null && _model.CurrentMaterialIndex > 0)
            {
                foreach (var face in meshContext.MeshObject.Faces)
                {
                    face.MaterialIndex = _model.CurrentMaterialIndex;
                }
                // Meshを再生成してサブメッシュを反映
                var newMesh = meshContext.MeshObject.ToUnityMesh();
                newMesh.name = name;
                newMesh.hideFlags = HideFlags.HideAndDontSave;
                if (meshContext.UnityMesh != null) DestroyImmediate(meshContext.UnityMesh);
                meshContext.UnityMesh = newMesh;
            }
        }

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

    private void RemoveMesh(int index)
    {
        if (index < 0 || index >= _meshContextList.Count)
            return;

        var meshContext = _meshContextList[index];

        // Undo記録用にスナップショットを削除前に保存
        int oldSelectedIndex = _selectedIndex;
        MeshContextSnapshot snapshot = null;
        if (_undoController != null)
        {
            snapshot = MeshContextSnapshot.Capture(meshContext);
        }

        // Meshの破棄
        if (meshContext.UnityMesh != null)
        {
            DestroyImmediate(meshContext.UnityMesh);
        }

        _meshContextList.RemoveAt(index);

        // 頂点選択と編集状態をリセット
        _selectedVertices.Clear();
        ResetEditState();

        if (_selectedIndex >= _meshContextList.Count)
        {
            _selectedIndex = _meshContextList.Count - 1;
        }

        if (_selectedIndex >= 0)
        {
            InitVertexOffsets();
            var newMeshContext = _meshContextList[_selectedIndex];

            // 注意: LoadMeshContextToUndoControllerは呼ばない（VertexEditStack.Clear()を避けるため）
            // MeshContextに必要な情報だけを設定
            if (_undoController != null)
            {
                _undoController.MeshUndoContext.MeshObject = newMeshContext.MeshObject;
                _undoController.MeshUndoContext.TargetMesh = newMeshContext.UnityMesh;
                _undoController.MeshUndoContext.OriginalPositions = newMeshContext.OriginalPositions;
                _undoController.MeshUndoContext.SelectedVertices = new HashSet<int>();
                // Materials は ModelContext に集約済み
            }
        }
        else
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            // メッシュがなくなったときだけClear
            _undoController?.VertexEditStack.Clear();
        }

        // Undo記録（メッシュリスト削除）
        if (_undoController != null && snapshot != null)
        {
            var record = new MeshListChangeRecord
            {
                RemovedMeshContexts = new List<(int, MeshContextSnapshot)> { (index, snapshot) },
                OldSelectedIndex = oldSelectedIndex,
                NewSelectedIndex = _selectedIndex
            };
            _undoController.MeshListStack.Record(record, $"Remove UnityMesh: {meshContext.Name}");
            _undoController.FocusMeshList();
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
        }

        Repaint();
    }

    /// <summary>
    /// 頂点オフセット初期化（MeshObjectベース）
    /// </summary>
    /// <param name="updateCamera">trueの場合、カメラをメッシュに合わせて調整する</param>
    private void InitVertexOffsets(bool updateCamera = true)
    {
        var meshContext = _model.CurrentMeshContext;
        if (meshContext == null)
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            return;
        }

        var meshObject = meshContext.MeshObject;

        if (meshObject == null)
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            return;
        }

        // MeshObjectのVertex数でオフセット配列を作成
        int vertexCount = meshObject.VertexCount;
        _vertexOffsets = new Vector3[vertexCount];
        _groupOffsets = new Vector3[vertexCount];  // Vertexと1:1

        // カメラ設定（オプション）
        if (updateCamera)
        {
            var bounds = meshObject.CalculateBounds();
            float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
            _cameraDistance = radius * 3.5f;
            _cameraTarget = bounds.center;
        }
    }
    // ================================================================
    // 保存機能
    // ================================================================

    /// <summary>
    /// メッシュアセットとして保存
    /// </summary>
    private void SaveMesh(MeshContext meshContext)
    {
        if (meshContext == null || meshContext.MeshObject == null)
            return;

        string defaultName = string.IsNullOrEmpty(meshContext.Name) ? "UnityMesh" : meshContext.Name;
        string path = EditorUtility.SaveFilePanelInProject(
            "Save UnityMesh",
            defaultName,
            "asset",
            "メッシュを保存する場所を選択してください");

        if (string.IsNullOrEmpty(path))
            return;

        // MeshObjectからUnity Meshを生成（ベイクオプション対応）
        Mesh meshToSave;
        if (_bakeMirror && meshContext.IsMirrored)
        {
            meshToSave = BakeMirrorToUnityMesh(meshContext, _mirrorFlipU, out _);
        }
        else
        {
            meshToSave = meshContext.MeshObject.ToUnityMesh();
        }
        meshToSave.name = System.IO.Path.GetFileNameWithoutExtension(path);

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(meshToSave, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (savedMesh != null)
        {
            EditorGUIUtility.PingObject(savedMesh);
            Selection.activeObject = savedMesh;
        }

        Debug.Log($"UnityMesh saved: {path}");
    }

    /// <summary>
    /// プレファブとして保存
    /// </summary>
    private void SaveAsPrefab(MeshContext meshContext)
    {
        if (meshContext == null || meshContext.MeshObject == null)
            return;

        string defaultName = string.IsNullOrEmpty(meshContext.Name) ? "MeshObject" : meshContext.Name;
        string path = EditorUtility.SaveFilePanelInProject(
            "Save as Prefab",
            defaultName,
            "prefab",
            "プレファブを保存する場所を選択してください");

        if (string.IsNullOrEmpty(path))
            return;

        // GameObjectを作成
        GameObject go = new GameObject(meshContext.Name);
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        // MeshObjectからUnity Meshを生成して保存（ベイクオプション対応）
        Mesh meshCopy;
        List<int> usedMatIndices = null;
        if (_bakeMirror && meshContext.IsMirrored)
        {
            meshCopy = BakeMirrorToUnityMesh(meshContext, _mirrorFlipU, out usedMatIndices);
        }
        else
        {
            meshCopy = meshContext.MeshObject.ToUnityMesh();
        }
        meshCopy.name = meshContext.Name;

        // メッシュを同じディレクトリに保存
        string meshPath = System.IO.Path.ChangeExtension(path, null) + "_Mesh.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(meshCopy, meshPath);

        mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

        // マテリアル設定（マルチマテリアル対応）
        Material[] baseMaterials = GetMaterialsForSave(meshContext);
        if (_bakeMirror && meshContext.IsMirrored && usedMatIndices != null)
        {
            mr.sharedMaterials = GetMaterialsForBakedMirror(usedMatIndices, baseMaterials);
        }
        else
        {
            mr.sharedMaterials = baseMaterials;
        }

        // ExportSettings を適用
        meshContext.ExportSettings?.ApplyToGameObject(go, asLocal: false);

        // プレファブとして保存
        AssetDatabase.DeleteAsset(path);
        PrefabUtility.SaveAsPrefabAsset(go, path);

        // 一時オブジェクト削除
        DestroyImmediate(go);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (savedPrefab != null)
        {
            EditorGUIUtility.PingObject(savedPrefab);
            Selection.activeObject = savedPrefab;
        }

        Debug.Log($"Prefab saved: {path}");
    }

    /// <summary>
    /// ヒエラルキーに追加
    /// </summary>
    private void AddToHierarchy(MeshContext meshContext)
    {
        if (meshContext == null || meshContext.MeshObject == null)
            return;

        // GameObjectを作成
        GameObject go = new GameObject(meshContext.Name);
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        // MeshObjectからUnity Meshを生成（ベイクオプション対応）
        Mesh meshCopy;
        List<int> usedMatIndices = null;
        if (_bakeMirror && meshContext.IsMirrored)
        {
            meshCopy = BakeMirrorToUnityMesh(meshContext, _mirrorFlipU, out usedMatIndices);
        }
        else
        {
            meshCopy = meshContext.MeshObject.ToUnityMesh();
        }
        meshCopy.name = meshContext.Name;
        mf.sharedMesh = meshCopy;

        // マテリアル設定（マルチマテリアル対応）
        Material[] baseMaterials = GetMaterialsForSave(meshContext);
        if (_bakeMirror && meshContext.IsMirrored && usedMatIndices != null)
        {
            mr.sharedMaterials = GetMaterialsForBakedMirror(usedMatIndices, baseMaterials);
        }
        else
        {
            mr.sharedMaterials = baseMaterials;
        }

        // 選択中のオブジェクトがあれば子として追加
        Transform parent = null;
        if (Selection.gameObjects.Length > 0)
        {
            parent = Selection.gameObjects[0].transform;
        }

        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }

        // ExportSettings を適用
        if (meshContext.ExportSettings != null)
        {
            meshContext.ExportSettings.ApplyToGameObject(go, asLocal: parent != null);
        }
        else
        {
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }

        // Undo登録（Unity標準のUndo）
        Undo.RegisterCreatedObjectUndo(go, $"Create {meshContext.Name}");

        // 選択
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);

        Debug.Log($"Added to hierarchy: {go.name}" + (parent != null ? $" (parent: {parent.name})" : ""));
    }

    // ================================================================
    // モデル全体エクスポート（複数メッシュ対応）
    // ================================================================

    /// <summary>
    /// モデル全体をヒエラルキーに追加
    /// HierarchyParentIndexに基づいて親子関係を再現
    /// ExportAsSkinned が有効な場合は SkinnedMeshRenderer を使用
    /// </summary>
    private void AddModelToHierarchy()
    {
        if (_meshContextList.Count == 0)
            return;

        // ExportAsSkinned フラグをチェック（最初のメッシュの設定を使用）
        bool exportAsSkinned = false;
        var firstMeshContext = _meshContextList.FirstOrDefault(m => m?.ExportSettings != null);
        if (firstMeshContext?.ExportSettings != null)
        {
            exportAsSkinned = firstMeshContext.ExportSettings.ExportAsSkinned;
        }

        if (exportAsSkinned)
        {
            AddModelToHierarchyAsSkinned();
            return;
        }

        // === 通常の MeshRenderer エクスポート ===

        // 選択中のオブジェクトを親として取得
        Transform existingParent = null;
        if (Selection.gameObjects.Length > 0)
        {
            existingParent = Selection.gameObjects[0].transform;
        }

        // 共有マテリアル配列を取得
        Material[] sharedMaterials = GetMaterialsForSave(null);

        // GameObjectをインデックス順に作成（親子関係設定のため）
        var createdObjects = new GameObject[_meshContextList.Count];

        // Pass 1: すべてのGameObjectを作成（親子関係は後で設定）
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var meshContext = _meshContextList[i];
            if (meshContext == null) continue;

            // GameObjectを作成
            GameObject go = new GameObject(meshContext.Name);
            createdObjects[i] = go;

            // メッシュがある場合のみMeshFilter/MeshRendererを追加
            if (meshContext.MeshObject != null && meshContext.MeshObject.VertexCount > 0)
            {
                MeshFilter mf = go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();

                // MeshObjectからUnity Meshを生成（ベイクオプション対応）
                Mesh meshCopy;
                Material[] materialsToUse;
                if (_bakeMirror && meshContext.IsMirrored)
                {
                    meshCopy = BakeMirrorToUnityMesh(meshContext, _mirrorFlipU, out var usedMatIndices);
                    materialsToUse = GetMaterialsForBakedMirror(usedMatIndices, sharedMaterials);
                }
                else
                {
                    meshCopy = meshContext.MeshObject.ToUnityMesh();
                    materialsToUse = sharedMaterials;
                }
                meshCopy.name = meshContext.Name;
                mf.sharedMesh = meshCopy;

                // マテリアル設定
                mr.sharedMaterials = materialsToUse;
            }
        }

        // Pass 2: 親子関係を設定
        GameObject firstRootObject = null;
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var meshContext = _meshContextList[i];
            var go = createdObjects[i];
            if (go == null) continue;

            int parentIndex = meshContext.HierarchyParentIndex;

            if (parentIndex >= 0 && parentIndex < createdObjects.Length && createdObjects[parentIndex] != null)
            {
                // 親がメッシュリスト内にある場合
                go.transform.SetParent(createdObjects[parentIndex].transform, false);
            }
            else
            {
                // ルートオブジェクト（HierarchyParentIndex == -1 または無効な参照）
                if (existingParent != null)
                {
                    go.transform.SetParent(existingParent, false);
                }
                // else: シーンルートに配置

                if (firstRootObject == null)
                {
                    firstRootObject = go;
                }
            }

            // ExportSettings を適用（ローカルTransform）
            if (meshContext.ExportSettings != null)
            {
                meshContext.ExportSettings.ApplyToGameObject(go, asLocal: true);
            }
            else
            {
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }

            // Undo登録
            Undo.RegisterCreatedObjectUndo(go, $"Create {meshContext.Name}");
        }

        // 最初のルートオブジェクトを選択
        if (firstRootObject != null)
        {
            Selection.activeGameObject = firstRootObject;
            EditorGUIUtility.PingObject(firstRootObject);
        }

        Debug.Log($"Added model to hierarchy: {_meshContextList.Count} objects (with hierarchy structure)");
    }

    /// <summary>
    /// モデル全体を SkinnedMeshRenderer としてヒエラルキーに追加
    /// 各 MeshContext の HierarchyParentIndex をボーンインデックスとして使用
    /// </summary>
    private void AddModelToHierarchyAsSkinned()
    {
        if (_meshContextList.Count == 0)
            return;

        // 選択中のオブジェクトを親として取得
        Transform existingParent = null;
        if (Selection.gameObjects.Length > 0)
        {
            existingParent = Selection.gameObjects[0].transform;
        }

        // 共有マテリアル配列を取得
        Material[] sharedMaterials = GetMaterialsForSave(null);

        // GameObjectをインデックス順に作成
        var createdObjects = new GameObject[_meshContextList.Count];

        // === Pass 1: すべての GameObject を作成（コンポーネントなし） ===
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var meshContext = _meshContextList[i];
            if (meshContext == null) continue;

            GameObject go = new GameObject(meshContext.Name);
            createdObjects[i] = go;
        }

        // === Pass 2: 親子関係を設定し、ExportSettings を適用 ===
        GameObject firstRootObject = null;
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var meshContext = _meshContextList[i];
            var go = createdObjects[i];
            if (go == null) continue;

            int parentIndex = meshContext.HierarchyParentIndex;

            if (parentIndex >= 0 && parentIndex < createdObjects.Length && createdObjects[parentIndex] != null)
            {
                go.transform.SetParent(createdObjects[parentIndex].transform, false);
            }
            else
            {
                if (existingParent != null)
                {
                    go.transform.SetParent(existingParent, false);
                }

                if (firstRootObject == null)
                {
                    firstRootObject = go;
                }
            }

            // ExportSettings を適用
            if (meshContext.ExportSettings != null)
            {
                meshContext.ExportSettings.ApplyToGameObject(go, asLocal: true);
            }
            else
            {
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }

            // Undo登録
            Undo.RegisterCreatedObjectUndo(go, $"Create {meshContext.Name}");
        }

        // === Pass 3: ボーン配列とバインドポーズを計算 ===
        var bones = new Transform[_meshContextList.Count];
        var bindPoses = new Matrix4x4[_meshContextList.Count];

        for (int i = 0; i < _meshContextList.Count; i++)
        {
            if (createdObjects[i] != null)
            {
                bones[i] = createdObjects[i].transform;
                // バインドポーズ = ワールド→ローカル変換行列
                bindPoses[i] = bones[i].worldToLocalMatrix;
            }
        }

        // === Pass 4: 各メッシュに対して SkinnedMeshRenderer をセットアップ ===
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var meshContext = _meshContextList[i];
            var go = createdObjects[i];
            if (meshContext?.MeshObject == null || go == null) continue;
            if (meshContext.MeshObject.VertexCount == 0) continue;

            // BoneWeight が未設定の頂点にデフォルト値を設定
            EnsureBoneWeights(meshContext.MeshObject, i);

            // メッシュを生成
            Mesh meshCopy;
            Material[] materialsToUse;
            if (_bakeMirror && meshContext.IsMirrored)
            {
                meshCopy = BakeMirrorToUnityMesh(meshContext, _mirrorFlipU, out var usedMatIndices);
                materialsToUse = GetMaterialsForBakedMirror(usedMatIndices, sharedMaterials);
            }
            else
            {
                meshCopy = meshContext.MeshObject.ToUnityMesh();
                materialsToUse = sharedMaterials;
            }
            meshCopy.name = meshContext.Name;

            // SkinnedMeshRenderer をセットアップ
            SetupSkinnedMeshRenderer(go, meshCopy, bones, bindPoses, materialsToUse);
        }

        // 最初のルートオブジェクトを選択
        if (firstRootObject != null)
        {
            Selection.activeGameObject = firstRootObject;
            EditorGUIUtility.PingObject(firstRootObject);
        }

        Debug.Log($"Added model to hierarchy as SkinnedMesh: {_meshContextList.Count} bones");
    }

    /// <summary>
    /// モデル全体をプレファブとして保存
    /// メッシュアセットも同じディレクトリに保存
    /// </summary>
    private void SaveModelAsPrefab()
    {
        if (_meshContextList.Count == 0)
            return;

        // 保存先を選択
        string defaultName = !string.IsNullOrEmpty(_model.Name) ? _model.Name : "ExportedModel";
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Model as Prefab",
            defaultName,
            "prefab",
            "プレファブを保存する場所を選択してください");

        if (string.IsNullOrEmpty(path))
            return;

        // 保存先ディレクトリ
        string directory = System.IO.Path.GetDirectoryName(path);
        string baseName = System.IO.Path.GetFileNameWithoutExtension(path);

        // 共有マテリアル配列を取得
        Material[] sharedMaterials = GetMaterialsForSave(null);

        // GameObjectをインデックス順に作成（親子関係設定のため）
        var createdObjects = new GameObject[_meshContextList.Count];

        // Pass 1: すべてのGameObjectを作成してメッシュを設定
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var meshContext = _meshContextList[i];
            if (meshContext == null) continue;

            // GameObjectを作成
            GameObject go = new GameObject(meshContext.Name);
            createdObjects[i] = go;

            // メッシュがある場合のみMeshFilter/MeshRendererを追加
            if (meshContext.MeshObject != null && meshContext.MeshObject.VertexCount > 0)
            {
                // メッシュを生成（ベイクオプション対応）
                Mesh meshCopy;
                List<int> usedMatIndices = null;
                if (_bakeMirror && meshContext.IsMirrored)
                {
                    meshCopy = BakeMirrorToUnityMesh(meshContext, _mirrorFlipU, out usedMatIndices);
                }
                else
                {
                    meshCopy = meshContext.MeshObject.ToUnityMesh();
                }
                meshCopy.name = meshContext.Name;

                // メッシュアセットを保存
                string meshPath = System.IO.Path.Combine(directory, $"{baseName}_{i}_{meshContext.Name}.asset");
                AssetDatabase.DeleteAsset(meshPath);
                AssetDatabase.CreateAsset(meshCopy, meshPath);

                MeshFilter mf = go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();

                // 保存したメッシュアセットを参照
                mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

                // マテリアル設定（ベイク時は使用マテリアルのみ）
                if (_bakeMirror && meshContext.IsMirrored && usedMatIndices != null)
                {
                    mr.sharedMaterials = GetMaterialsForBakedMirror(usedMatIndices, sharedMaterials);
                }
                else
                {
                    mr.sharedMaterials = sharedMaterials;
                }
            }
        }

        // Pass 2: 親子関係を設定
        // ルートオブジェクトを格納する親を作成（複数ルートがある場合に備えて）
        GameObject rootContainer = null;
        int rootCount = 0;

        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var meshContext = _meshContextList[i];
            var go = createdObjects[i];
            if (go == null) continue;

            int parentIndex = meshContext.HierarchyParentIndex;

            if (parentIndex >= 0 && parentIndex < createdObjects.Length && createdObjects[parentIndex] != null)
            {
                // 親がメッシュリスト内にある場合
                go.transform.SetParent(createdObjects[parentIndex].transform, false);
            }
            else
            {
                // ルートオブジェクト
                rootCount++;
            }

            // ExportSettings を適用（ローカルTransform）
            if (meshContext.ExportSettings != null)
            {
                meshContext.ExportSettings.ApplyToGameObject(go, asLocal: true);
            }
            else
            {
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }
        }

        // 複数ルートがある場合、またはルートが1つでも親コンテナを作成
        if (rootCount > 1 || (rootCount == 1 && _meshContextList.Count > 1))
        {
            rootContainer = new GameObject(baseName);
            for (int i = 0; i < _meshContextList.Count; i++)
            {
                var go = createdObjects[i];
                if (go == null) continue;

                int parentIndex = _meshContextList[i].HierarchyParentIndex;
                if (parentIndex < 0 || parentIndex >= createdObjects.Length || createdObjects[parentIndex] == null)
                {
                    // ルートオブジェクトをコンテナの子に
                    go.transform.SetParent(rootContainer.transform, false);
                }
            }
        }
        else if (rootCount == 1)
        {
            // 単一ルートの場合はそれ自体をプレファブルートに
            for (int i = 0; i < createdObjects.Length; i++)
            {
                if (createdObjects[i] != null &&
                    (_meshContextList[i].HierarchyParentIndex < 0 ||
                     _meshContextList[i].HierarchyParentIndex >= createdObjects.Length))
                {
                    rootContainer = createdObjects[i];
                    break;
                }
            }
        }

        // プレファブとして保存
        if (rootContainer != null)
        {
            AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(rootContainer, path);

            // 一時オブジェクト削除
            DestroyImmediate(rootContainer);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (savedPrefab != null)
        {
            EditorGUIUtility.PingObject(savedPrefab);
            Selection.activeObject = savedPrefab;
        }

        Debug.Log($"Model prefab saved: {path} ({_meshContextList.Count} objects with hierarchy)");
    }

    /// <summary>
    /// モデル全体のメッシュアセットを保存
    /// </summary>
    private void SaveModelMeshAssets()
    {
        if (_meshContextList.Count == 0)
            return;

        // 保存先ディレクトリを選択
        string defaultName = !string.IsNullOrEmpty(_model.Name) ? _model.Name : "ExportedModel";
        string path = EditorUtility.SaveFolderPanel(
            "Save Mesh Assets",
            "Assets",
            defaultName);

        if (string.IsNullOrEmpty(path))
            return;

        // プロジェクト相対パスに変換
        if (path.StartsWith(Application.dataPath))
        {
            path = "Assets" + path.Substring(Application.dataPath.Length);
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "Assetsフォルダ内を選択してください", "OK");
            return;
        }

        // 各メッシュを保存
        int savedCount = 0;
        foreach (var meshContext in _meshContextList)
        {
            if (meshContext?.MeshObject == null)
                continue;

            // メッシュを生成（ベイクオプション対応）
            Mesh meshCopy;
            if (_bakeMirror && meshContext.IsMirrored)
            {
                meshCopy = BakeMirrorToUnityMesh(meshContext, _mirrorFlipU, out _);
            }
            else
            {
                meshCopy = meshContext.MeshObject.ToUnityMesh();
            }
            meshCopy.name = meshContext.Name;

            string meshPath = System.IO.Path.Combine(path, $"{meshContext.Name}.asset");

            // 既存ファイルがあれば番号付け
            int suffix = 1;
            while (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null)
            {
                meshPath = System.IO.Path.Combine(path, $"{meshContext.Name}_{suffix}.asset");
                suffix++;
            }

            AssetDatabase.CreateAsset(meshCopy, meshPath);
            savedCount++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 保存先フォルダを選択
        var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        if (folder != null)
        {
            EditorGUIUtility.PingObject(folder);
            Selection.activeObject = folder;
        }

        Debug.Log($"Mesh assets saved: {savedCount} meshes to {path}");
    }

    // ================================================================
    // モデルファイル入出力
    // ================================================================

    /// <summary>
    /// モデルをファイルにエクスポート
    /// </summary>
    private void ExportModel()
    {
        if (_meshContextList.Count == 0)
        {
            EditorUtility.DisplayDialog("Export Project", "エクスポートするメッシュがありません。", "OK");
            return;
        }

        // プロジェクトデータを作成
        string projectName = _model.Name ?? (_meshContextList.Count > 0 ? _meshContextList[0].Name : "Project");
        var projectDTO = ProjectDTO.Create(projectName);

        // EditorState を作成
        var editorStateDTO = new EditorStateDTO
        {
            rotationX = _rotationX,
            rotationY = _rotationY,
            cameraDistance = _cameraDistance,
            cameraTarget = new float[] { _cameraTarget.x, _cameraTarget.y, _cameraTarget.z },
            showWireframe = _showWireframe,
            showVertices = _showVertices,
            vertexEditMode = _vertexEditMode,
            currentToolName = _currentTool?.Name ?? "Select",
            selectedMeshIndex = _selectedIndex
        };

        // ModelSerializer.FromModelContext を使用してモデル全体をエクスポート
        // これにより Materials も正しく保存される
        var modelDTO = ModelSerializer.FromModelContext(
            _model,
            _undoController?.WorkPlane,
            editorStateDTO
        );

        if (modelDTO != null)
        {
            // 選択頂点を設定（FromModelContext では設定されないため）
            if (_selectedIndex >= 0 && _selectedIndex < modelDTO.meshDTOList.Count)
            {
                modelDTO.meshDTOList[_selectedIndex].selectedVertices = _selectedVertices.ToList();
            }

            projectDTO.models.Add(modelDTO);
        }

        ProjectSerializer.ExportWithDialog(projectDTO, projectName);
    }

    /// <summary>
    /// ファイルからモデルをインポート（Undo対応）
    /// </summary>
    private void ImportModel()
    {
        var projectDTO = ProjectSerializer.ImportWithDialog();
        if (projectDTO == null || projectDTO.models.Count == 0) return;

        // 最初のモデルを使用
        var modelDTO = projectDTO.models[0];

        // 確認ダイアログ
        if (_meshContextList.Count > 0)
        {
            bool result = EditorUtility.DisplayDialog(
                "Import Project",
                "現在のデータを破棄して読み込みますか？\n（Ctrl+Zで元に戻せます）",
                "はい", "キャンセル"
            );
            if (!result) return;
        }

        // Undo記録用：既存メッシュのスナップショットを保存
        List<(int Index, MeshContextSnapshot Snapshot)> removedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            MeshContextSnapshot snapshot = MeshContextSnapshot.Capture(_meshContextList[i]);
            removedSnapshots.Add((i, snapshot));
        }
        int oldSelectedIndex = _selectedIndex;

        // 変更前のカメラ状態を保存
        CameraSnapshot oldCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // 既存メッシュをクリア（UnityMeshを破棄）
        CleanupMeshes();
        _meshContextList.Clear();
        _selectedIndex = -1;
        _selectedVertices.Clear();
        // 注意: VertexEditStackはクリアしない（Undo可能にするため）

        // ModelSerializer.ToModelContext を使用してモデル全体を復元
        // これにより Materials も正しく復元される
        ModelSerializer.ToModelContext(modelDTO, _model);

        // WorkPlane復元
        if (modelDTO.workPlane != null && _undoController?.WorkPlane != null)
        {
            ModelSerializer.ApplyToWorkPlane(modelDTO.workPlane, _undoController.WorkPlane);
        }

        // EditorState復元
        if (modelDTO.editorStateDTO != null)
        {
            var state = modelDTO.editorStateDTO;
            _rotationX = state.rotationX;
            _rotationY = state.rotationY;
            _cameraDistance = state.cameraDistance;
            if (state.cameraTarget != null && state.cameraTarget.Length >= 3)
            {
                _cameraTarget = new Vector3(state.cameraTarget[0], state.cameraTarget[1], state.cameraTarget[2]);
            }
            _showWireframe = state.showWireframe;
            _showVertices = state.showVertices;
            _vertexEditMode = state.vertexEditMode;

            // 選択メッシュを復元
            if (state.selectedMeshIndex >= 0 && state.selectedMeshIndex < _meshContextList.Count)
            {
                _selectedIndex = state.selectedMeshIndex;

                // 選択頂点を復元
                var selectedMeshContextData = modelDTO.meshDTOList[state.selectedMeshIndex];
                _selectedVertices = ModelSerializer.ToSelectedVertices(selectedMeshContextData);
            }
            else if (_meshContextList.Count > 0)
            {
                _selectedIndex = 0;
            }

            // ツールを復元（名前で検索）
            if (!string.IsNullOrEmpty(state.currentToolName))
            {
                SetToolByName(state.currentToolName);
            }
        }
        else if (_meshContextList.Count > 0)
        {
            _selectedIndex = 0;
        }

        // 変更後のカメラ状態を保存
        CameraSnapshot newCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録用：新メッシュのスナップショットを保存
        List<(int Index, MeshContextSnapshot Snapshot)> addedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            MeshContextSnapshot snapshot = MeshContextSnapshot.Capture(_meshContextList[i]);
            addedSnapshots.Add((i, snapshot));
        }

        // オフセット配列を初期化
        InitVertexOffsets();

        // UndoContextを更新
        var meshContext = _model.CurrentMeshContext;
        if (meshContext != null)
        {
            // 注意: LoadMeshContextToUndoControllerは呼ばない（VertexEditStack.Clear()を避けるため）
            if (_undoController != null)
            {
                _undoController.MeshUndoContext.MeshObject = meshContext.MeshObject;
                _undoController.MeshUndoContext.TargetMesh = meshContext.UnityMesh;
                _undoController.MeshUndoContext.OriginalPositions = meshContext.OriginalPositions;
                _undoController.MeshUndoContext.SelectedVertices = _selectedVertices;
            }
        }

        // Undo記録（プロジェクトインポート）
        if (_undoController != null)
        {
            var record = new MeshListChangeRecord
            {
                RemovedMeshContexts = removedSnapshots,
                AddedMeshContexts = addedSnapshots,
                OldSelectedIndex = oldSelectedIndex,
                NewSelectedIndex = _selectedIndex,
                OldCameraState = oldCameraState,
                NewCameraState = newCameraState
            };
            _undoController.MeshListStack.Record(record, $"Import Project: {projectDTO.name}");
            _undoController.FocusMeshList();
            _undoController.MeshListContext.SelectedMeshContextIndex = _selectedIndex;
        }

        Debug.Log($"[SimpleMeshFactory] Imported project: {projectDTO.name} ({_meshContextList.Count} meshes, {_model.Materials?.Count ?? 0} materialPathList)");
        Repaint();
    }



    /// <summary>
    /// 保存用のマテリアル配列を取得（マルチマテリアル対応）
    /// meshContextがnullの場合はモデル全体のマテリアルを返す
    /// マテリアルはコピーして返す（シーン上のオブジェクトが独立したインスタンスを持つため）
    /// </summary>
    private Material[] GetMaterialsForSave(MeshContext meshContext)
    {
        // モデルのマテリアルを使用（meshContextがnullでも有効）
        if (_model.Materials.Count > 0)
        {
            var result = new Material[_model.Materials.Count];
            for (int i = 0; i < _model.Materials.Count; i++)
            {
                var srcMat = _model.Materials[i];
                if (srcMat != null)
                {
                    // マテリアルをコピー
                    result[i] = new Material(srcMat);
                    result[i].name = srcMat.name;
                }
                else
                {
                    result[i] = GetOrCreateDefaultMaterial();
                }
            }
            return result;
        }
        return new Material[] { GetOrCreateDefaultMaterial() };
    }

    /// <summary>
    /// 保存用のマテリアルを取得（単一、後方互換用）
    /// </summary>
    private Material GetMaterialForSave(MeshContext meshContext)
    {
        // メッシュコンテキストのマテリアルがあればそれを使用
        if (meshContext != null && _model.Materials.Count > 0 && _model.Materials[0] != null)
        {
            return _model.Materials[0];
        }

        // なければデフォルトマテリアルを作成/取得
        return GetOrCreateDefaultMaterial();
    }

    // ================================================================
    // ミラーベイク
    // ================================================================

    /// <summary>
    /// ミラー（対称）をベイクしたUnity Meshを生成
    /// 頂点数・面数が2倍になり、サブメッシュは左右ペアで並ぶ
    /// 例: 元が[mat0, mat1]なら、結果は[左mat0, 右mat0, 左mat1, 右mat1]
    /// </summary>
    private Mesh BakeMirrorToUnityMesh(MeshContext meshContext, bool flipU, out List<int> usedMaterialIndices)
    {
        var meshObject = meshContext.MeshObject;
        var mesh = new Mesh();
        mesh.name = meshObject.Name;
        usedMaterialIndices = new List<int>();

        if (meshObject.Vertices.Count == 0)
            return mesh;

        SymmetryAxis axis = meshContext.GetMirrorSymmetryAxis();

        // 頂点データ
        var unityVerts = new List<Vector3>();
        var unityUVs = new List<Vector2>();
        var unityNormals = new List<Vector3>();

        // マテリアルインデックス → サブメッシュインデックス（左）
        var matToLeftSubMesh = new Dictionary<int, int>();
        var subMeshTriangles = new List<List<int>>();

        // ============================================
        // パス1: 左側
        // ============================================
        foreach (var face in meshObject.Faces)
        {
            if (face.VertexCount < 3 || face.IsHidden)
                continue;

            int matIdx = face.MaterialIndex;

            // 新しいマテリアルなら左右のサブメッシュを追加
            if (!matToLeftSubMesh.ContainsKey(matIdx))
            {
                matToLeftSubMesh[matIdx] = subMeshTriangles.Count;
                subMeshTriangles.Add(new List<int>()); // 左
                subMeshTriangles.Add(new List<int>()); // 右
                usedMaterialIndices.Add(matIdx);
            }

            int leftSubMesh = matToLeftSubMesh[matIdx];

            var triangles = face.Triangulate();
            foreach (var tri in triangles)
            {
                int baseIndex = unityVerts.Count;

                for (int i = 0; i < 3; i++)
                {
                    int vIdx = tri.VertexIndices[i];
                    if (vIdx < 0 || vIdx >= meshObject.Vertices.Count)
                        vIdx = 0;

                    var vertex = meshObject.Vertices[vIdx];

                    unityVerts.Add(vertex.Position);

                    int uvIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                    Vector2 uv;
                    if (uvIdx >= 0 && uvIdx < vertex.UVs.Count)
                        uv = vertex.UVs[uvIdx];
                    else if (vertex.UVs.Count > 0)
                        uv = vertex.UVs[0];
                    else
                        uv = Vector2.zero;
                    unityUVs.Add(uv);

                    int nIdx = i < tri.NormalIndices.Count ? tri.NormalIndices[i] : 0;
                    Vector3 normal;
                    if (nIdx >= 0 && nIdx < vertex.Normals.Count)
                        normal = vertex.Normals[nIdx];
                    else if (vertex.Normals.Count > 0)
                        normal = vertex.Normals[0];
                    else
                        normal = Vector3.up;
                    unityNormals.Add(normal);
                }

                subMeshTriangles[leftSubMesh].Add(baseIndex);
                subMeshTriangles[leftSubMesh].Add(baseIndex + 1);
                subMeshTriangles[leftSubMesh].Add(baseIndex + 2);
            }
        }

        int leftVertexCount = unityVerts.Count;
        Debug.Log($"[BakeMirror] Pass1 done: leftVertexCount={leftVertexCount}");

        // ============================================
        // パス2: 右側
        // ============================================
        foreach (var face in meshObject.Faces)
        {
            if (face.VertexCount < 3 || face.IsHidden)
                continue;

            int rightSubMesh = matToLeftSubMesh[face.MaterialIndex] + 1;

            var triangles = face.Triangulate();
            foreach (var tri in triangles)
            {
                int baseIndex = unityVerts.Count;

                for (int i = 2; i >= 0; i--)
                {
                    int vIdx = tri.VertexIndices[i];
                    if (vIdx < 0 || vIdx >= meshObject.Vertices.Count)
                        vIdx = 0;

                    var vertex = meshObject.Vertices[vIdx];

                    unityVerts.Add(MirrorPosition(vertex.Position, axis));

                    int uvIdx = i < tri.UVIndices.Count ? tri.UVIndices[i] : 0;
                    Vector2 uv;
                    if (uvIdx >= 0 && uvIdx < vertex.UVs.Count)
                        uv = vertex.UVs[uvIdx];
                    else if (vertex.UVs.Count > 0)
                        uv = vertex.UVs[0];
                    else
                        uv = Vector2.zero;

                    if (flipU)
                        uv.x = 1f - uv.x;
                    unityUVs.Add(uv);

                    int nIdx = i < tri.NormalIndices.Count ? tri.NormalIndices[i] : 0;
                    Vector3 normal;
                    if (nIdx >= 0 && nIdx < vertex.Normals.Count)
                        normal = vertex.Normals[nIdx];
                    else if (vertex.Normals.Count > 0)
                        normal = vertex.Normals[0];
                    else
                        normal = Vector3.up;
                    unityNormals.Add(MirrorNormal(normal, axis));
                }

                subMeshTriangles[rightSubMesh].Add(baseIndex);
                subMeshTriangles[rightSubMesh].Add(baseIndex + 1);
                subMeshTriangles[rightSubMesh].Add(baseIndex + 2);
            }
        }

        // 頂点数が多い場合は32ビットインデックスを使用
        if (unityVerts.Count > 65535)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        // Meshに設定
        mesh.SetVertices(unityVerts);
        mesh.SetUVs(0, unityUVs);
        mesh.SetNormals(unityNormals);

        mesh.subMeshCount = subMeshTriangles.Count;
        for (int i = 0; i < subMeshTriangles.Count; i++)
        {
            mesh.SetTriangles(subMeshTriangles[i], i);
        }

        mesh.RecalculateBounds();

        // デバッグ: 各サブメッシュのインデックス範囲を出力
        Debug.Log($"[BakeMirror] {meshObject.Name}: totalVerts={unityVerts.Count}, leftVertexCount={leftVertexCount}, rightStart={leftVertexCount}");
        for (int i = 0; i < subMeshTriangles.Count; i++)
        {
            var tris = subMeshTriangles[i];
            if (tris.Count > 0)
            {
                int minIdx = tris.Min();
                int maxIdx = tris.Max();
                string side = (i % 2 == 0) ? "LEFT" : "RIGHT";
                string expected = (i % 2 == 0) ? $"should be < {leftVertexCount}" : $"should be >= {leftVertexCount}";
                Debug.Log($"[BakeMirror] SubMesh[{i}] ({side}): {tris.Count} indices, range [{minIdx} - {maxIdx}] {expected}");
            }
        }

        return mesh;
    }

    /// <summary>
    /// ベイクミラー用のマテリアル配列を構築
    /// </summary>
    private Material[] GetMaterialsForBakedMirror(List<int> usedMaterialIndices, Material[] baseMaterials)
    {
        var result = new Material[usedMaterialIndices.Count * 2];
        for (int i = 0; i < usedMaterialIndices.Count; i++)
        {
            int matIndex = usedMaterialIndices[i];
            Material mat = (matIndex >= 0 && matIndex < baseMaterials.Length)
                ? baseMaterials[matIndex]
                : GetOrCreateDefaultMaterial();
            result[i * 2] = mat;       // 左
            result[i * 2 + 1] = mat;   // 右
        }
        return result;
    }

    /// <summary>
    /// 位置をミラー
    /// </summary>
    private Vector3 MirrorPosition(Vector3 pos, SymmetryAxis axis)
    {
        switch (axis)
        {
            case SymmetryAxis.X: return new Vector3(-pos.x, pos.y, pos.z);
            case SymmetryAxis.Y: return new Vector3(pos.x, -pos.y, pos.z);
            case SymmetryAxis.Z: return new Vector3(pos.x, pos.y, -pos.z);
            default: return new Vector3(-pos.x, pos.y, pos.z);
        }
    }

    /// <summary>
    /// 法線をミラー
    /// </summary>
    private Vector3 MirrorNormal(Vector3 normal, SymmetryAxis axis)
    {
        switch (axis)
        {
            case SymmetryAxis.X: return new Vector3(-normal.x, normal.y, normal.z);
            case SymmetryAxis.Y: return new Vector3(normal.x, -normal.y, normal.z);
            case SymmetryAxis.Z: return new Vector3(normal.x, normal.y, -normal.z);
            default: return new Vector3(-normal.x, normal.y, normal.z);
        }
    }

    /// <summary>
    /// デフォルトマテリアルを取得または作成
    /// </summary>
    private Material GetOrCreateDefaultMaterial()
    {
        // 既存のデフォルトマテリアルを探す
        string[] guids = AssetDatabase.FindAssets("t:Material Default-Material");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
        }

        // URPのLitシェーダーでマテリアル作成
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
            shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            Material mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1f));
            mat.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f, 1f));
            return mat;
        }

        return null;
    }

    // ================================================================
    // SkinnedMeshRenderer 対応（Phase 2）
    // ================================================================

    /// <summary>
    /// BoneWeight が未設定の頂点にデフォルト値（boneIndex0 に 100%）を設定
    /// </summary>
    /// <param name="meshObject">対象の MeshObject</param>
    /// <param name="defaultBoneIndex">デフォルトのボーンインデックス（通常は自身のメッシュインデックス）</param>
    private void EnsureBoneWeights(MeshObject meshObject, int defaultBoneIndex)
    {
        if (meshObject == null) return;

        foreach (var vertex in meshObject.Vertices)
        {
            if (!vertex.HasBoneWeight)
            {
                vertex.BoneWeight = new BoneWeight
                {
                    boneIndex0 = defaultBoneIndex,
                    weight0 = 1f,
                    boneIndex1 = 0,
                    weight1 = 0f,
                    boneIndex2 = 0,
                    weight2 = 0f,
                    boneIndex3 = 0,
                    weight3 = 0f
                };
            }
        }
    }

    /// <summary>
    /// SkinnedMeshRenderer をセットアップ
    /// </summary>
    /// <param name="go">対象の GameObject</param>
    /// <param name="mesh">設定するメッシュ</param>
    /// <param name="bones">ボーン Transform 配列</param>
    /// <param name="bindPoses">バインドポーズ行列配列</param>
    /// <param name="materials">マテリアル配列</param>
    private void SetupSkinnedMeshRenderer(
        GameObject go,
        Mesh mesh,
        Transform[] bones,
        Matrix4x4[] bindPoses,
        Material[] materials)
    {
        if (go == null || mesh == null) return;

        // SkinnedMeshRenderer を追加
        var smr = go.AddComponent<SkinnedMeshRenderer>();

        // バインドポーズを設定
        mesh.bindposes = bindPoses;

        // メッシュとボーンを設定
        smr.sharedMesh = mesh;
        smr.bones = bones;

        // マテリアル設定
        if (materials != null && materials.Length > 0)
        {
            smr.sharedMaterials = materials;
        }

        // ルートボーンを設定（最初のボーン）
        if (bones != null && bones.Length > 0)
        {
            smr.rootBone = bones[0];
        }
    }

}