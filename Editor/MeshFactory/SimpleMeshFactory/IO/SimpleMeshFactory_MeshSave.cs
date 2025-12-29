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
            meshCopy = meshContext.MeshObject.ToUnityMeshShared();
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
