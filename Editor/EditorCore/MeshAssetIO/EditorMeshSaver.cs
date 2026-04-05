using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// メッシュ保存のEditorCore実装。
    /// SaveMesh / SaveAsPrefab / AddToHierarchy と純粋ヘルパーを提供する。
    /// </summary>
    public static class EditorMeshSaver
    {
        // ================================================================
        // エントリーポイント（PolyLing_MeshSave.cs から移動）
        // ================================================================

        /// <summary>メッシュアセットとして保存</summary>
        public static void SaveMesh(MeshContext meshContext, IEditorMeshSaveHost host)
        {
            if (meshContext == null || meshContext.MeshObject == null) return;

            string defaultName = string.IsNullOrEmpty(meshContext.Name) ? "UnityMesh" : meshContext.Name;
            string path = PLEditorBridge.I.SaveFilePanelInProject(
                "Save UnityMesh", defaultName, "asset", "メッシュを保存する場所を選択してください");
            if (string.IsNullOrEmpty(path)) return;

            Mesh meshToSave;
            if (host.BakeMirror && meshContext.IsMirrored)
                meshToSave = host.BakeMirrorToUnityMesh(meshContext, host.MirrorFlipU, out _);
            else
                meshToSave = meshContext.MeshObject.ToUnityMeshShared();
            meshToSave.name = System.IO.Path.GetFileNameWithoutExtension(path);

            PLEditorBridge.I.DeleteAsset(path);
            PLEditorBridge.I.CreateAsset(meshToSave, path);
            PLEditorBridge.I.SaveAssets();
            PLEditorBridge.I.Refresh();

            var savedMesh = PLEditorBridge.I.LoadAssetAtPath<Mesh>(path);
            if (savedMesh != null)
            {
                PLEditorBridge.I.PingObject(savedMesh);
                PLEditorBridge.I.SetActiveObject(savedMesh);
            }

            Debug.Log($"UnityMesh saved: {path}");
        }

        /// <summary>プレファブとして保存</summary>
        public static void SaveAsPrefab(MeshContext meshContext, IEditorMeshSaveHost host)
        {
            if (meshContext == null || meshContext.MeshObject == null) return;

            string defaultName = string.IsNullOrEmpty(meshContext.Name) ? "MeshObject" : meshContext.Name;
            string path = PLEditorBridge.I.SaveFilePanelInProject(
                "Save as Prefab", defaultName, "prefab", "プレファブを保存する場所を選択してください");
            if (string.IsNullOrEmpty(path)) return;

            GameObject go = new GameObject(meshContext.Name);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();

            Mesh meshCopy;
            List<int> usedMatIndices = null;
            if (host.BakeMirror && meshContext.IsMirrored)
                meshCopy = host.BakeMirrorToUnityMesh(meshContext, host.MirrorFlipU, out usedMatIndices);
            else
                meshCopy = meshContext.MeshObject.ToUnityMeshShared();
            meshCopy.name = meshContext.Name;

            string meshPath = System.IO.Path.ChangeExtension(path, null) + "_Mesh.asset";
            PLEditorBridge.I.DeleteAsset(meshPath);
            PLEditorBridge.I.CreateAsset(meshCopy, meshPath);

            mf.sharedMesh = PLEditorBridge.I.LoadAssetAtPath<Mesh>(meshPath);

            Material[] baseMaterials = host.GetMaterialsForSave(meshContext);
            if (host.BakeMirror && meshContext.IsMirrored && usedMatIndices != null)
                mr.sharedMaterials = host.GetMaterialsForBakedMirror(usedMatIndices, baseMaterials);
            else
                mr.sharedMaterials = baseMaterials;

            meshContext.BoneTransform?.ApplyToGameObject(go, asLocal: false);

            PLEditorBridge.I.DeleteAsset(path);
            PLEditorBridge.I.SaveAsPrefabAsset(go, path);

            Object.DestroyImmediate(go);

            PLEditorBridge.I.SaveAssets();
            PLEditorBridge.I.Refresh();

            var savedPrefab = PLEditorBridge.I.LoadAssetAtPath<GameObject>(path);
            if (savedPrefab != null)
            {
                PLEditorBridge.I.PingObject(savedPrefab);
                PLEditorBridge.I.SetActiveObject(savedPrefab);
            }

            Debug.Log($"Prefab saved: {path}");
        }

        /// <summary>ヒエラルキーに追加（既存GameObjectがあれば差し替え）</summary>
        public static void AddToHierarchy(MeshContext meshContext, IEditorMeshSaveHost host)
        {
            if (meshContext == null || meshContext.MeshObject == null) return;

            Mesh meshCopy;
            List<int> usedMatIndices = null;
            if (host.BakeMirror && meshContext.IsMirrored)
                meshCopy = host.BakeMirrorToUnityMesh(meshContext, host.MirrorFlipU, out usedMatIndices);
            else
                meshCopy = meshContext.MeshObject.ToUnityMeshShared();
            meshCopy.name = meshContext.Name;

            Material[] baseMaterials = host.GetMaterialsForSave(meshContext);
            Material[] materialsToApply;
            if (host.BakeMirror && meshContext.IsMirrored && usedMatIndices != null)
                materialsToApply = host.GetMaterialsForBakedMirror(usedMatIndices, baseMaterials);
            else
                materialsToApply = baseMaterials;

            GameObject existingGO = null;
            Transform searchRoot = null;
            var selectedGOs = host.GetSelectedGameObjects();
            if (selectedGOs != null && selectedGOs.Length > 0)
            {
                searchRoot = selectedGOs[0].transform;
                existingGO = FindDescendantByName(searchRoot, meshContext.Name);
            }

            if (existingGO != null)
            {
                ReplaceMeshOnGameObject(existingGO, meshCopy, materialsToApply, meshContext);
                PLEditorBridge.I.SetActiveGameObject(existingGO);
                PLEditorBridge.I.PingObject(existingGO);
                Debug.Log($"Replaced mesh on existing GameObject: {existingGO.name}");
            }
            else
            {
                CreateNewGameObject(meshContext, meshCopy, materialsToApply, searchRoot);
            }
        }

        // ================================================================
        // 純粋ヘルパー（PolyLing_MeshSave.cs から移動）
        // ================================================================

        /// <summary>子孫から指定名のGameObjectを検索（自分自身も含む）</summary>
        public static GameObject FindDescendantByName(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            if (root.name == name) return root.gameObject;
            foreach (Transform child in root)
            {
                var found = FindDescendantByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>既存GameObjectのメッシュを差し替え</summary>
        public static void ReplaceMeshOnGameObject(GameObject go, Mesh mesh, Material[] materials, MeshContext meshContext)
        {
            PLEditorBridge.I.RecordObject(go, $"Replace Mesh {go.name}");

            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null)
            {
                PLEditorBridge.I.RecordObject(smr, $"Replace Mesh {go.name}");
                smr.sharedMesh = mesh;
                smr.sharedMaterials = materials;
                Debug.Log($"[ReplaceMeshOnGameObject] Replaced SkinnedMeshRenderer mesh on '{go.name}'");
                return;
            }

            var mf = go.GetComponent<MeshFilter>();
            if (mf == null)
                mf = PLEditorBridge.I.AddComponent<MeshFilter>(go);
            else
                PLEditorBridge.I.RecordObject(mf, $"Replace Mesh {go.name}");
            mf.sharedMesh = mesh;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null)
                mr = PLEditorBridge.I.AddComponent<MeshRenderer>(go);
            else
                PLEditorBridge.I.RecordObject(mr, $"Replace Mesh {go.name}");
            mr.sharedMaterials = materials;

            Debug.Log($"[ReplaceMeshOnGameObject] Replaced MeshFilter/MeshRenderer mesh on '{go.name}'");
        }

        /// <summary>新規GameObjectを作成</summary>
        public static void CreateNewGameObject(MeshContext meshContext, Mesh mesh, Material[] materials, Transform parent)
        {
            GameObject go = new GameObject(meshContext.Name);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();

            mf.sharedMesh = mesh;
            mr.sharedMaterials = materials;

            if (parent != null) go.transform.SetParent(parent, false);

            if (meshContext.BoneTransform != null)
                meshContext.BoneTransform.ApplyToGameObject(go, asLocal: parent != null);
            else
            {
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
            }

            PLEditorBridge.I.RegisterCreatedObjectUndo(go, $"Create {meshContext.Name}");
            PLEditorBridge.I.SetActiveGameObject(go);
            PLEditorBridge.I.PingObject(go);

            Debug.Log($"Added to hierarchy: {go.name}" + (parent != null ? $" (parent: {parent.name})" : ""));
        }
    }
}
