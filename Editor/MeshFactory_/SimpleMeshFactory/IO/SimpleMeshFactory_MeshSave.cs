// Assets/Editor/MeshFactory/SimpleMeshFactory/SimpleMeshFactory_MeshSave.cs
// 単一メッシュ保存機能
// - メッシュアセット保存
// - プレファブ保存
// - ヒエラルキー追加

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;

public partial class SimpleMeshFactory
{
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
            meshToSave = meshContext.MeshObject.ToUnityMeshShared();
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
            meshCopy = meshContext.MeshObject.ToUnityMeshShared();
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

        // BoneTransform を適用
        meshContext.BoneTransform?.ApplyToGameObject(go, asLocal: false);

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
    /// ヒエラルキーに追加（既存GameObjectがあれば差し替え）
    /// </summary>
    private void AddToHierarchy(MeshContext meshContext)
    {
        if (meshContext == null || meshContext.MeshObject == null)
            return;

        // MeshObjectからUnity Meshを生成（ベイクオプション対応）
        Mesh meshCopy;
        List<int> usedMatIndices = null;
        if (_bakeMirror && meshContext.IsMirrored)
        {
            meshCopy = BakeMirrorToUnityMesh(meshContext, _mirrorFlipU, out usedMatIndices);
        }
        else
        {
            meshCopy = meshContext.MeshObject.ToUnityMeshShared();
        }
        meshCopy.name = meshContext.Name;

        // マテリアル設定（マルチマテリアル対応）
        Material[] baseMaterials = GetMaterialsForSave(meshContext);
        Material[] materialsToApply;
        if (_bakeMirror && meshContext.IsMirrored && usedMatIndices != null)
        {
            materialsToApply = GetMaterialsForBakedMirror(usedMatIndices, baseMaterials);
        }
        else
        {
            materialsToApply = baseMaterials;
        }

        // Hierarchy選択があれば、子孫から同名のGameObjectを検索
        GameObject existingGO = null;
        Transform searchRoot = null;
        if (Selection.gameObjects.Length > 0)
        {
            searchRoot = Selection.gameObjects[0].transform;
            existingGO = FindDescendantByName(searchRoot, meshContext.Name);
        }

        if (existingGO != null)
        {
            // 既存GameObjectのメッシュを差し替え
            ReplaceMeshOnGameObject(existingGO, meshCopy, materialsToApply, meshContext);
            
            // 選択
            Selection.activeGameObject = existingGO;
            EditorGUIUtility.PingObject(existingGO);
            
            Debug.Log($"Replaced mesh on existing GameObject: {existingGO.name}");
        }
        else
        {
            // 新規作成
            CreateNewGameObject(meshContext, meshCopy, materialsToApply, searchRoot);
        }
    }

    /// <summary>
    /// 子孫から指定名のGameObjectを検索（自分自身も含む）
    /// </summary>
    private GameObject FindDescendantByName(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
            return null;

        // 自分自身をチェック
        if (root.name == name)
            return root.gameObject;

        // 子孫を再帰的に検索
        foreach (Transform child in root)
        {
            var found = FindDescendantByName(child, name);
            if (found != null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// 既存GameObjectのメッシュを差し替え
    /// </summary>
    private void ReplaceMeshOnGameObject(GameObject go, Mesh mesh, Material[] materials, MeshContext meshContext)
    {
        // Undo登録
        Undo.RecordObject(go, $"Replace Mesh {go.name}");

        // SkinnedMeshRendererがあればそちらを優先
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (smr != null)
        {
            Undo.RecordObject(smr, $"Replace Mesh {go.name}");
            smr.sharedMesh = mesh;
            smr.sharedMaterials = materials;
            Debug.Log($"[ReplaceMeshOnGameObject] Replaced SkinnedMeshRenderer mesh on '{go.name}'");
            return;
        }

        // MeshFilterがあれば差し替え、なければ追加
        var mf = go.GetComponent<MeshFilter>();
        if (mf == null)
        {
            mf = Undo.AddComponent<MeshFilter>(go);
        }
        else
        {
            Undo.RecordObject(mf, $"Replace Mesh {go.name}");
        }
        mf.sharedMesh = mesh;

        // MeshRendererがあれば差し替え、なければ追加
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null)
        {
            mr = Undo.AddComponent<MeshRenderer>(go);
        }
        else
        {
            Undo.RecordObject(mr, $"Replace Mesh {go.name}");
        }
        mr.sharedMaterials = materials;

        Debug.Log($"[ReplaceMeshOnGameObject] Replaced MeshFilter/MeshRenderer mesh on '{go.name}'");
    }

    /// <summary>
    /// 新規GameObjectを作成
    /// </summary>
    private void CreateNewGameObject(MeshContext meshContext, Mesh mesh, Material[] materials, Transform parent)
    {
        // GameObjectを作成
        GameObject go = new GameObject(meshContext.Name);
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        mf.sharedMesh = mesh;
        mr.sharedMaterials = materials;

        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }

        // BoneTransform を適用
        if (meshContext.BoneTransform != null)
        {
            meshContext.BoneTransform.ApplyToGameObject(go, asLocal: parent != null);
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
}
