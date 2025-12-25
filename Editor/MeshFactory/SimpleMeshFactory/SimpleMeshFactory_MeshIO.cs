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
    /// 選択中のオブジェクトから読み込み
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
                AddLoadedMesh(selectedMesh, selectedMesh.name);// Transform渡せない
                return;
            }

            EditorUtility.DisplayDialog("Info", "GameObjectまたはMeshを選択してください", "OK");
            return;
        }

        var meshFilters = selected.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "選択オブジェクトにMeshFilterが見つかりませんでした", "OK");
            return;
        }

        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh != null)
            {
                string meshName = $"{selected.name}_{mf.sharedMesh.name}";

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
            _undoController.MeshListContext.SelectedIndex = _selectedIndex;
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
        if (_addToCurrentMesh && _model.HasValidSelection)
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
            _undoController.MeshListContext.SelectedIndex = _selectedIndex;
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
            var snapshotAfter = MeshObjectSnapshot.Capture(_undoController.MeshUndoContext);

            // 直接VertexEditStackに記録（RecordTopologyChangeのEndGroup副作用を回避）
            var record = new MeshSnapshotRecord(snapshotBefore, snapshotAfter);
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
            _undoController.MeshListContext.SelectedIndex = _selectedIndex;
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
            _undoController.MeshListContext.SelectedIndex = _selectedIndex;
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

        // MeshObjectからUnity Meshを生成
        Mesh meshToSave = meshContext.MeshObject.ToUnityMesh();
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

        // MeshObjectからUnity Meshを生成して保存
        Mesh meshCopy = meshContext.MeshObject.ToUnityMesh();
        meshCopy.name = meshContext.Name;

        // メッシュを同じディレクトリに保存
        string meshPath = System.IO.Path.ChangeExtension(path, null) + "_Mesh.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(meshCopy, meshPath);

        mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

        // マテリアル設定（マルチマテリアル対応）
        mr.sharedMaterials = GetMaterialsForSave(meshContext);

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

        // MeshObjectからUnity Meshを生成
        Mesh meshCopy = meshContext.MeshObject.ToUnityMesh();
        meshCopy.name = meshContext.Name;
        mf.sharedMesh = meshCopy;

        // マテリアル設定（マルチマテリアル対応）
        mr.sharedMaterials = GetMaterialsForSave(meshContext);

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
        var projectData = ProjectData.Create(projectName);

        // EditorState を作成
        var editorState = new EditorStateData
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
        var modelData = ModelSerializer.FromModelContext(
            _model,
            _undoController?.WorkPlane,
            editorState
        );

        if (modelData != null)
        {
            // 選択頂点を設定（FromModelContext では設定されないため）
            if (_selectedIndex >= 0 && _selectedIndex < modelData.meshContextList.Count)
            {
                modelData.meshContextList[_selectedIndex].selectedVertices = _selectedVertices.ToList();
            }

            projectData.models.Add(modelData);
        }

        ProjectSerializer.ExportWithDialog(projectData, projectName);
    }

    /// <summary>
    /// ファイルからモデルをインポート（Undo対応）
    /// </summary>
    private void ImportModel()
    {
        var projectData = ProjectSerializer.ImportWithDialog();
        if (projectData == null || projectData.models.Count == 0) return;

        // 最初のモデルを使用
        var modelData = projectData.models[0];

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
        var removedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var snapshot = MeshContextSnapshot.Capture(_meshContextList[i]);
            removedSnapshots.Add((i, snapshot));
        }
        int oldSelectedIndex = _selectedIndex;

        // 変更前のカメラ状態を保存
        var oldCameraState = new CameraSnapshot
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
        ModelSerializer.ToModelContext(modelData, _model);

        // WorkPlane復元
        if (modelData.workPlane != null && _undoController?.WorkPlane != null)
        {
            ModelSerializer.ApplyToWorkPlane(modelData.workPlane, _undoController.WorkPlane);
        }

        // EditorState復元
        if (modelData.editorState != null)
        {
            var state = modelData.editorState;
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
                var selectedMeshContextData = modelData.meshContextList[state.selectedMeshIndex];
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
        var newCameraState = new CameraSnapshot
        {
            RotationX = _rotationX,
            RotationY = _rotationY,
            CameraDistance = _cameraDistance,
            CameraTarget = _cameraTarget
        };

        // Undo記録用：新メッシュのスナップショットを保存
        var addedSnapshots = new List<(int Index, MeshContextSnapshot Snapshot)>();
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var snapshot = MeshContextSnapshot.Capture(_meshContextList[i]);
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
            _undoController.MeshListStack.Record(record, $"Import Project: {projectData.name}");
            _undoController.FocusMeshList();
            _undoController.MeshListContext.SelectedIndex = _selectedIndex;
        }

        Debug.Log($"[SimpleMeshFactory] Imported project: {projectData.name} ({_meshContextList.Count} meshes, {_model.Materials?.Count ?? 0} materials)");
        Repaint();
    }



    /// <summary>
    /// 保存用のマテリアル配列を取得（マルチマテリアル対応）
    /// </summary>
    private Material[] GetMaterialsForSave(MeshContext meshContext)
    {
        if (meshContext != null && _model.Materials.Count > 0)
        {
            var result = new Material[_model.Materials.Count];
            for (int i = 0; i < _model.Materials.Count; i++)
            {
                result[i] = _model.Materials[i] ?? GetOrCreateDefaultMaterial();
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

}