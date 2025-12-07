// Assets/Editor/SimpleMeshFactory.MeshIO.cs
// メッシュ入出力（読み込み、保存、エクスポート、インポート）
// Phase2: マルチマテリアル対応版
// DefaultMaterials対応版

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools;
using MeshFactory.Serialization;

public partial class SimpleMeshFactory
{
    // ================================================================
    // メッシュ読み出し機能
    // ================================================================

    /// <summary>
    /// メッシュアセットから読み込み
    /// </summary>
    private void LoadMeshFromAsset()
    {
        string path = EditorUtility.OpenFilePanel("Select Mesh Asset", "Assets", "asset,fbx,obj");
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
            AddLoadedMesh(loadedMesh, loadedMesh.name);
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

                AddLoadedMesh(mf.sharedMesh, meshName, mats);
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
            // メッシュアセットが選択されている場合
            var selectedMesh = Selection.activeObject as Mesh;
            if (selectedMesh != null)
            {
                AddLoadedMesh(selectedMesh, selectedMesh.name);
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

                AddLoadedMesh(mf.sharedMesh, meshName, mats);
            }
        }
    }

    /// <summary>
    /// 読み込んだメッシュを追加（MeshDataに変換）
    /// </summary>
    /// <param name="sourceMesh">元のUnity Mesh</param>
    /// <param name="name">メッシュ名</param>
    /// <param name="materials">マテリアル配列（オプション）</param>
    private void AddLoadedMesh(Mesh sourceMesh, string name, Material[] materials = null)
    {
        // Unity MeshからMeshDataに変換
        var meshData = new MeshData(name);
        meshData.FromUnityMesh(sourceMesh, true);

        var entry = new MeshEntry
        {
            Name = name,
            Data = meshData,
            OriginalPositions = meshData.Vertices.Select(v => v.Position).ToArray()
        };

        // マテリアル設定
        if (materials != null && materials.Length > 0)
        {
            // 引数で指定されたマテリアルを使用（読み込み元のマテリアル）
            entry.Materials.Clear();
            foreach (var mat in materials)
            {
                entry.Materials.Add(mat);
            }
            // 引数指定の場合はCurrentMaterialIndexは0のまま、FaceのMaterialIndexもそのまま
        }
        else if (_defaultMaterials != null && _defaultMaterials.Count > 0)
        {
            // デフォルトマテリアルをコピー
            entry.Materials = new List<Material>(_defaultMaterials);
            entry.CurrentMaterialIndex = Mathf.Clamp(_defaultCurrentMaterialIndex, 0, entry.Materials.Count - 1);

            // 全FaceにカレントマテリアルIndexを適用
            if (entry.Data != null && entry.CurrentMaterialIndex > 0)
            {
                foreach (var face in entry.Data.Faces)
                {
                    face.MaterialIndex = entry.CurrentMaterialIndex;
                }
            }
        }

        // 表示用Unity Meshを作成（MaterialIndex適用後）
        Mesh displayMesh = entry.Data.ToUnityMesh();
        displayMesh.name = name;
        displayMesh.hideFlags = HideFlags.HideAndDontSave;
        entry.Mesh = displayMesh;

        // Undo記録
        var beforeSnapshot = _undoController?.CaptureMeshDataSnapshot();

        _meshList.Add(entry);
        _selectedIndex = _meshList.Count - 1;
        InitVertexOffsets();

        LoadEntryToUndoController(entry);

        // Undo記録（メッシュ追加）
        if (_undoController != null && beforeSnapshot != null)
        {
            var afterSnapshot = _undoController.CaptureMeshDataSnapshot();
            _undoController.RecordTopologyChange(beforeSnapshot, afterSnapshot, $"Load Mesh: {name}");
        }

        Repaint();
    }

    /// <summary>
    /// メッシュ作成ウインドウからのコールバック（MeshData版 - 四角形を保持）
    /// </summary>
    private void OnMeshDataCreated(MeshData meshData, string name)
    {
        var entry = new MeshEntry
        {
            Name = name,
            Data = meshData.Clone(),
            OriginalPositions = meshData.Vertices.Select(v => v.Position).ToArray()
        };

        // デフォルトマテリアルをコピー
        if (_defaultMaterials != null && _defaultMaterials.Count > 0)
        {
            entry.Materials = new List<Material>(_defaultMaterials);
            entry.CurrentMaterialIndex = Mathf.Clamp(_defaultCurrentMaterialIndex, 0, entry.Materials.Count - 1);

            // 全FaceにカレントマテリアルIndexを適用
            if (entry.Data != null && entry.CurrentMaterialIndex > 0)
            {
                foreach (var face in entry.Data.Faces)
                {
                    face.MaterialIndex = entry.CurrentMaterialIndex;
                }
            }
        }

        // MeshDataから表示用Unity Meshを生成（MaterialIndex適用後）
        Mesh mesh = entry.Data.ToUnityMesh();
        mesh.name = name;
        mesh.hideFlags = HideFlags.HideAndDontSave;
        entry.Mesh = mesh;

        Debug.Log($"[OnMeshDataCreated] name={name}, defaultMaterials={_defaultMaterials?.Count ?? 0}, currentIndex={entry.CurrentMaterialIndex}, faces={entry.Data?.FaceCount ?? 0}");

        _meshList.Add(entry);
        _selectedIndex = _meshList.Count - 1;
        InitVertexOffsets();

        LoadEntryToUndoController(entry);

        Repaint();
    }

    /// <summary>
    /// 空のメッシュを作成
    /// </summary>
    private void CreateEmptyMesh()
    {
        var meshData = new MeshData("Empty");
        OnMeshDataCreated(meshData, "Empty");
    }

    /// <summary>
    /// メッシュ作成ウインドウからのコールバック（従来版）
    /// </summary>
    private void OnMeshCreated(Mesh mesh, string name)
    {
        // Unity MeshからMeshDataに変換
        var meshData = new MeshData(name);
        meshData.FromUnityMesh(mesh, true);

        // エディタ専用の一時メッシュとしてマーク
        mesh.hideFlags = HideFlags.HideAndDontSave;

        // 元のMeshはそのまま表示用に使用
        var entry = new MeshEntry
        {
            Name = name,
            Mesh = mesh,
            Data = meshData,
            OriginalPositions = meshData.Vertices.Select(v => v.Position).ToArray()
        };

        // デフォルトマテリアルをコピー
        if (_defaultMaterials != null && _defaultMaterials.Count > 0)
        {
            entry.Materials = new List<Material>(_defaultMaterials);
            entry.CurrentMaterialIndex = Mathf.Clamp(_defaultCurrentMaterialIndex, 0, entry.Materials.Count - 1);

            // 全FaceにカレントマテリアルIndexを適用
            if (entry.Data != null && entry.CurrentMaterialIndex > 0)
            {
                foreach (var face in entry.Data.Faces)
                {
                    face.MaterialIndex = entry.CurrentMaterialIndex;
                }
                // Meshを再生成してサブメッシュを反映
                var newMesh = entry.Data.ToUnityMesh();
                newMesh.name = name;
                newMesh.hideFlags = HideFlags.HideAndDontSave;
                if (entry.Mesh != null) DestroyImmediate(entry.Mesh);
                entry.Mesh = newMesh;
            }
        }

        _meshList.Add(entry);
        _selectedIndex = _meshList.Count - 1;
        InitVertexOffsets();

        LoadEntryToUndoController(entry);

        Repaint();
    }

    private void RemoveMesh(int index)
    {
        if (index < 0 || index >= _meshList.Count)
            return;

        var entry = _meshList[index];
        if (entry.Mesh != null)
        {
            DestroyImmediate(entry.Mesh);
        }

        _meshList.RemoveAt(index);

        // 頂点選択と編集状態をリセット
        _selectedVertices.Clear();
        ResetEditState();

        if (_selectedIndex >= _meshList.Count)
        {
            _selectedIndex = _meshList.Count - 1;
        }

        if (_selectedIndex >= 0)
        {
            InitVertexOffsets();
            var newEntry = _meshList[_selectedIndex];
            LoadEntryToUndoController(newEntry);
        }
        else
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            _undoController?.VertexEditStack.Clear();
        }

        Repaint();
    }

    /// <summary>
    /// 頂点オフセット初期化（MeshDataベース）
    /// </summary>
    private void InitVertexOffsets()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count)
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            return;
        }

        var entry = _meshList[_selectedIndex];
        var meshData = entry.Data;

        if (meshData == null)
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            return;
        }

        // MeshDataのVertex数でオフセット配列を作成
        int vertexCount = meshData.VertexCount;
        _vertexOffsets = new Vector3[vertexCount];
        _groupOffsets = new Vector3[vertexCount];  // Vertexと1:1

        // カメラ設定
        var bounds = meshData.CalculateBounds();
        float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
        _cameraDistance = radius * 3.5f;
        _cameraTarget = bounds.center;
    }
    // ================================================================
    // 保存機能
    // ================================================================

    /// <summary>
    /// メッシュアセットとして保存
    /// </summary>
    private void SaveMesh(MeshEntry entry)
    {
        if (entry == null || entry.Data == null)
            return;

        string defaultName = string.IsNullOrEmpty(entry.Name) ? "Mesh" : entry.Name;
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Mesh",
            defaultName,
            "asset",
            "メッシュを保存する場所を選択してください");

        if (string.IsNullOrEmpty(path))
            return;

        // MeshDataからUnity Meshを生成
        Mesh meshToSave = entry.Data.ToUnityMesh();
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

        Debug.Log($"Mesh saved: {path}");
    }

    /// <summary>
    /// プレファブとして保存
    /// </summary>
    private void SaveAsPrefab(MeshEntry entry)
    {
        if (entry == null || entry.Data == null)
            return;

        string defaultName = string.IsNullOrEmpty(entry.Name) ? "MeshObject" : entry.Name;
        string path = EditorUtility.SaveFilePanelInProject(
            "Save as Prefab",
            defaultName,
            "prefab",
            "プレファブを保存する場所を選択してください");

        if (string.IsNullOrEmpty(path))
            return;

        // GameObjectを作成
        GameObject go = new GameObject(entry.Name);
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        // MeshDataからUnity Meshを生成して保存
        Mesh meshCopy = entry.Data.ToUnityMesh();
        meshCopy.name = entry.Name;

        // メッシュを同じディレクトリに保存
        string meshPath = System.IO.Path.ChangeExtension(path, null) + "_Mesh.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(meshCopy, meshPath);

        mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

        // マテリアル設定（マルチマテリアル対応）
        mr.sharedMaterials = GetMaterialsForSave(entry);

        // ExportSettings を適用
        entry.ExportSettings?.ApplyToGameObject(go, asLocal: false);

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
    private void AddToHierarchy(MeshEntry entry)
    {
        if (entry == null || entry.Data == null)
            return;

        // GameObjectを作成
        GameObject go = new GameObject(entry.Name);
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        // MeshDataからUnity Meshを生成
        Mesh meshCopy = entry.Data.ToUnityMesh();
        meshCopy.name = entry.Name;
        mf.sharedMesh = meshCopy;

        // マテリアル設定（マルチマテリアル対応）
        mr.sharedMaterials = GetMaterialsForSave(entry);

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
        if (entry.ExportSettings != null)
        {
            entry.ExportSettings.ApplyToGameObject(go, asLocal: parent != null);
        }
        else
        {
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }

        // Undo登録（Unity標準のUndo）
        Undo.RegisterCreatedObjectUndo(go, $"Create {entry.Name}");

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
        if (_meshList.Count == 0)
        {
            EditorUtility.DisplayDialog("Export Model", "エクスポートするメッシュがありません。", "OK");
            return;
        }

        // モデルデータを作成
        string modelName = _meshList.Count > 0 ? _meshList[0].Name : "Model";
        var modelData = ModelData.Create(modelName);

        // メッシュエントリを追加
        for (int i = 0; i < _meshList.Count; i++)
        {
            var entry = _meshList[i];
            var selectedVerts = (i == _selectedIndex) ? _selectedVertices : new HashSet<int>();

            var entryData = ModelSerializer.ToMeshEntryData(
                entry.Data,
                entry.Name,
                entry.ExportSettings,
                selectedVerts,
                entry.Materials,              // マテリアルリスト
                entry.CurrentMaterialIndex    // カレントマテリアルインデックス
            );
            modelData.meshEntries.Add(entryData);
        }

        // WorkPlane
        if (_undoController?.WorkPlane != null)
        {
            modelData.workPlane = ModelSerializer.ToWorkPlaneData(_undoController.WorkPlane);
        }

        // EditorState
        modelData.editorState = new EditorStateData
        {
            rotationX = _rotationX,
            rotationY = _rotationY,
            cameraDistance = _cameraDistance,
            cameraTarget = new float[] { _cameraTarget.x, _cameraTarget.y, _cameraTarget.z },
            showWireframe = _showWireframe,
            showVertices = _showVertices,
            vertexEditMode = _vertexEditMode,
            currentToolName = _currentTool?.Name ?? "Select",
            selectedMeshIndex = _selectedIndex,
            knifeMode = _knifeTool?.Mode.ToString() ?? "Cut",
            knifeEdgeSelect = _knifeTool?.EdgeSelect ?? false,
            knifeChainMode = _knifeTool?.ChainMode ?? false
        };

        // エクスポート
        ModelSerializer.ExportWithDialog(modelData, modelName);
    }

    /// <summary>
    /// ファイルからモデルをインポート
    /// </summary>
    private void ImportModel()
    {
        var modelData = ModelSerializer.ImportWithDialog();
        if (modelData == null) return;

        // 確認ダイアログ
        if (_meshList.Count > 0)
        {
            bool result = EditorUtility.DisplayDialog(
                "Import Model",
                "現在のデータを破棄して読み込みますか？\n（Undoはクリアされます）",
                "はい", "キャンセル"
            );
            if (!result) return;
        }

        // 既存データをクリア
        CleanupMeshes();
        _meshList.Clear();
        _selectedIndex = -1;
        _selectedVertices.Clear();
        _undoController?.VertexEditStack?.Clear();

        // メッシュエントリを復元
        foreach (var entryData in modelData.meshEntries)
        {
            var meshData = ModelSerializer.ToMeshData(entryData);
            if (meshData == null) continue;

            var entry = new MeshEntry
            {
                Name = entryData.name ?? "Mesh",
                Data = meshData,
                Mesh = meshData.ToUnityMesh(),
                OriginalPositions = meshData.Vertices.Select(v => v.Position).ToArray(),
                ExportSettings = ModelSerializer.ToExportSettings(entryData.exportSettings),
                Materials = ModelSerializer.ToMaterials(entryData),           // マテリアルリスト復元
                CurrentMaterialIndex = entryData.currentMaterialIndex         // カレントインデックス復元
            };
            _meshList.Add(entry);
        }

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
            if (state.selectedMeshIndex >= 0 && state.selectedMeshIndex < _meshList.Count)
            {
                _selectedIndex = state.selectedMeshIndex;

                // 選択頂点を復元
                var selectedEntryData = modelData.meshEntries[state.selectedMeshIndex];
                _selectedVertices = ModelSerializer.ToSelectedVertices(selectedEntryData);
            }
            else if (_meshList.Count > 0)
            {
                _selectedIndex = 0;
            }

            // ツールを復元（名前で検索）
            if (!string.IsNullOrEmpty(state.currentToolName))
            {
                SetToolByName(state.currentToolName);
            }

            // KnifeToolの設定を復元
            if (_knifeTool != null)
            {
                if (!string.IsNullOrEmpty(state.knifeMode) &&
                    System.Enum.TryParse<KnifeMode>(state.knifeMode, out var knifeMode))
                {
                    _knifeTool.Mode = knifeMode;
                }
                _knifeTool.EdgeSelect = state.knifeEdgeSelect;
                _knifeTool.ChainMode = state.knifeChainMode;

                if (_undoController != null)
                {
                    _undoController.EditorState.KnifeMode = _knifeTool.Mode;
                    _undoController.EditorState.KnifeEdgeSelect = _knifeTool.EdgeSelect;
                    _undoController.EditorState.KnifeChainMode = _knifeTool.ChainMode;
                }
            }
        }
        else if (_meshList.Count > 0)
        {
            _selectedIndex = 0;
        }

        // オフセット配列を初期化
        InitVertexOffsets();

        // UndoContextを更新
        if (_selectedIndex >= 0 && _selectedIndex < _meshList.Count)
        {
            var entry = _meshList[_selectedIndex];
            _undoController?.SetMeshData(entry.Data, entry.Mesh);
            _undoController.MeshContext.SelectedVertices = _selectedVertices;
        }

        Debug.Log($"[SimpleMeshFactory] Imported model: {modelData.name} ({_meshList.Count} meshes)");
        Repaint();
    }

    /// <summary>
    /// ツール名からツールを設定
    /// </summary>
    private void SetToolByName(string toolName)
    {
        switch (toolName)
        {
            case "Select":
                _currentTool = _selectTool;
                break;
            case "Move":
                _currentTool = _moveTool;
                break;
            case "Add Face":
                _currentTool = _addFaceTool;
                break;
            case "Knife":
                _currentTool = _knifeTool;
                break;
            case "Edge Topology":
                _currentTool = _edgeTopoTool;
                break;
            case "Advanced Select":
                _currentTool = _advancedSelectTool;
                break;
            case "Sculpt":
                _currentTool = _sculptTool;
                break;
            default:
                _currentTool = _selectTool;
                break;
        }

        if (_undoController != null)
        {
            _undoController.EditorState.CurrentToolName = _currentTool.Name;
        }
    }

    /// <summary>
    /// 保存用のマテリアル配列を取得（マルチマテリアル対応）
    /// </summary>
    private Material[] GetMaterialsForSave(MeshEntry entry)
    {
        if (entry != null && entry.Materials.Count > 0)
        {
            var result = new Material[entry.Materials.Count];
            for (int i = 0; i < entry.Materials.Count; i++)
            {
                result[i] = entry.Materials[i] ?? GetOrCreateDefaultMaterial();
            }
            return result;
        }
        return new Material[] { GetOrCreateDefaultMaterial() };
    }

    /// <summary>
    /// 保存用のマテリアルを取得（単一、後方互換用）
    /// </summary>
    private Material GetMaterialForSave(MeshEntry entry)
    {
        // エントリのマテリアルがあればそれを使用
        if (entry != null && entry.Materials.Count > 0 && entry.Materials[0] != null)
        {
            return entry.Materials[0];
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
