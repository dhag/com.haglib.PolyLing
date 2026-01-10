// Assets/Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_ModelExport.cs
// モデル全体エクスポート機能（複数メッシュ対応）
// - ヒエラルキーに追加
// - SkinnedMeshRendererとしてエクスポート
// - プレファブ保存
// - メッシュアセット保存

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;

public partial class SimpleMeshFactory
{
    // ================================================================
    // モデル全体エクスポート（複数メッシュ対応）
    // ================================================================

    /// <summary>
    /// モデル全体をヒエラルキーに追加
    /// HierarchyParentIndexに基づいて親子関係を再現
    /// _exportAsSkinned が有効な場合は SkinnedMeshRenderer を使用
    /// </summary>
    private void AddModelToHierarchy()
    {
        if (_meshContextList.Count == 0)
            return;

        // _exportAsSkinned フィールドをチェック
        if (_exportAsSkinned)
        {
            AddModelToHierarchyAsSkinned();
            return;
        }

        // === 通常の MeshRenderer エクスポート ===

        // 選択中のオブジェクトを親として取得、なければ空のルートを作成
        Transform rootParent = null;
        GameObject createdRoot = null;
        if (Selection.gameObjects.Length > 0)
        {
            rootParent = Selection.gameObjects[0].transform;
        }
        else
        {
            // 選択がない場合は空のルートオブジェクトを作成
            string rootName = !string.IsNullOrEmpty(_model.Name) ? _model.Name : "Model";
            createdRoot = new GameObject(rootName);
            rootParent = createdRoot.transform;
            Undo.RegisterCreatedObjectUndo(createdRoot, $"Create {rootName}");
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
                    meshCopy = meshContext.MeshObject.ToUnityMeshShared(_model.MaterialCount > 0 ? _model.MaterialCount : meshContext.MeshObject.SubMeshCount);
                    materialsToUse = sharedMaterials;
                }
                meshCopy.name = meshContext.Name;
                mf.sharedMesh = meshCopy;

                // デバッグ: マテリアルとサブメッシュの対応を確認
                if (i < 10) // 最初の10メッシュのみ
                {
                    var matIndices = meshContext.MeshObject.Faces
                        .Where(f => !f.IsHidden && f.VertexCount >= 3)
                        .Select(f => f.MaterialIndex)
                        .Distinct()
                        .OrderBy(x => x)
                        .ToList();
                    Debug.Log($"[Export] Mesh '{meshContext.Name}': " +
                              $"SubMeshCount={meshCopy.subMeshCount}, " +
                              $"FaceMatIndices=[{string.Join(",", matIndices)}], " +
                              $"SharedMatsCount={sharedMaterials.Length}, " +
                              $"ToUseCount={materialsToUse.Length}");

                    // 各サブメッシュの三角形数を確認
                    for (int sm = 0; sm < meshCopy.subMeshCount && sm < 5; sm++)
                    {
                        int triCount = meshCopy.GetTriangles(sm).Length / 3;
                        string matName = sm < materialsToUse.Length && materialsToUse[sm] != null
                            ? materialsToUse[sm].name : "null";
                        if (triCount > 0)
                            Debug.Log($"[Export]   SubMesh[{sm}]: {triCount} tris, Mat='{matName}'");
                    }
                }

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
                go.transform.SetParent(rootParent, false);

                if (firstRootObject == null)
                {
                    firstRootObject = go;
                }
            }

            // BoneTransform を適用（ローカルTransform）
            if (meshContext.BoneTransform != null)
            {
                meshContext.BoneTransform.ApplyToGameObject(go, asLocal: true);
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

        // 作成したルートオブジェクト、または最初のルートオブジェクトを選択
        GameObject objectToSelect = createdRoot ?? firstRootObject;
        if (objectToSelect != null)
        {
            Selection.activeGameObject = objectToSelect;
            EditorGUIUtility.PingObject(objectToSelect);
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

        // 選択中のオブジェクトを親として取得、なければ空のルートを作成
        Transform rootParent = null;
        GameObject createdRoot = null;
        if (Selection.gameObjects.Length > 0)
        {
            rootParent = Selection.gameObjects[0].transform;
        }
        else
        {
            // 選択がない場合は空のルートオブジェクトを作成
            string rootName = !string.IsNullOrEmpty(_model.Name) ? _model.Name : "Model";
            createdRoot = new GameObject(rootName);
            rootParent = createdRoot.transform;
            Undo.RegisterCreatedObjectUndo(createdRoot, $"Create {rootName}");
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

        // === Pass 2: 親子関係を設定し、BoneTransform を適用 ===
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
                // ルートオブジェクトはrootParentの子に
                go.transform.SetParent(rootParent, false);

                if (firstRootObject == null)
                {
                    firstRootObject = go;
                }
            }

            // BoneTransform を適用
            if (meshContext.BoneTransform != null)
            {
                meshContext.BoneTransform.ApplyToGameObject(go, asLocal: true);
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
                meshCopy = meshContext.MeshObject.ToUnityMeshShared(_model.MaterialCount > 0 ? _model.MaterialCount : meshContext.MeshObject.SubMeshCount);
                materialsToUse = sharedMaterials;
            }
            meshCopy.name = meshContext.Name;

            // デバッグ: マテリアルとサブメッシュの対応を確認（最初の10メッシュのみ）
            if (i < 10)
            {
                var matIndices = meshContext.MeshObject.Faces
                    .Where(f => !f.IsHidden && f.VertexCount >= 3)
                    .Select(f => f.MaterialIndex)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();
                Debug.Log($"[ExportSkinned] Mesh[{i}] '{meshContext.Name}': " +
                          $"SubMeshCount={meshCopy.subMeshCount}, " +
                          $"FaceMatIndices=[{string.Join(",", matIndices)}], " +
                          $"SharedMatsCount={sharedMaterials.Length}, " +
                          $"ToUseCount={materialsToUse.Length}");

                // 各サブメッシュの三角形数を確認
                for (int sm = 0; sm < meshCopy.subMeshCount && sm < 5; sm++)
                {
                    int triCount = meshCopy.GetTriangles(sm).Length / 3;
                    string matName = sm < materialsToUse.Length && materialsToUse[sm] != null
                        ? materialsToUse[sm].name : "null";
                    if (triCount > 0)
                        Debug.Log($"[ExportSkinned]   SubMesh[{sm}]: {triCount} tris, Mat='{matName}'");
                }
            }

            // SkinnedMeshRenderer をセットアップ
            SetupSkinnedMeshRenderer(go, meshCopy, bones, bindPoses, materialsToUse);
        }

        // 作成したルートオブジェクト、または最初のルートオブジェクトを選択
        GameObject objectToSelect = createdRoot ?? firstRootObject;
        if (objectToSelect != null)
        {
            Selection.activeGameObject = objectToSelect;
            EditorGUIUtility.PingObject(objectToSelect);
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

        // 共有マテリアル配列を取得してアセットとして保存
        Material[] sharedMaterials = SaveMaterialsAsAssets(directory, baseName);

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
                    meshCopy = meshContext.MeshObject.ToUnityMeshShared(_model.MaterialCount > 0 ? _model.MaterialCount : meshContext.MeshObject.SubMeshCount);
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

            // BoneTransform を適用（ローカルTransform）
            if (meshContext.BoneTransform != null)
            {
                meshContext.BoneTransform.ApplyToGameObject(go, asLocal: true);
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
    /// マテリアルをアセットとして保存し、保存されたマテリアル配列を返す
    /// オンメモリマテリアルは_saveOnMemoryMaterialsがtrueの場合のみ保存
    /// </summary>
    private Material[] SaveMaterialsAsAssets(string directory, string baseName)
    {
        var materialRefs = _model.MaterialReferences;
        if (materialRefs.Count == 0)
        {
            return new Material[] { GetOrCreateDefaultMaterial() };
        }

        // オンメモリマテリアルを保存（オプションがONの場合）
        if (_saveOnMemoryMaterials && _model.HasOnMemoryMaterials())
        {
            int savedCount = _model.SaveOnMemoryMaterialsAsAssets(directory);
            if (savedCount > 0)
            {
                Debug.Log($"[SaveMaterialsAsAssets] Saved {savedCount} on-memory materials as assets");
            }
        }

        // マテリアル配列を構築
        var result = new Material[materialRefs.Count];
        for (int i = 0; i < materialRefs.Count; i++)
        {
            var matRef = materialRefs[i];
            result[i] = matRef?.Material ?? GetOrCreateDefaultMaterial();
        }

        return result;
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
                meshCopy = meshContext.MeshObject.ToUnityMeshShared(_model.MaterialCount > 0 ? _model.MaterialCount : meshContext.MeshObject.SubMeshCount);
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
}