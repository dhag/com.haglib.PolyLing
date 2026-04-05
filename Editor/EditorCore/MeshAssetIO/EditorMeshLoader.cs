using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.EditorBridge;

namespace Poly_Ling.EditorCore
{
    /// <summary>
    /// メッシュ読み込みのEditorCore実装。
    /// - LoadFromAsset / LoadFromPrefab / LoadFromHierarchy : エントリーポイント（IEditorMeshLoadHostに委譲）
    /// - その他 : Hierarchy走査・MeshContext生成の純粋計算ヘルパー
    /// </summary>
    public static class EditorMeshLoader
    {
        // ================================================================
        // エントリーポイント（PolyLing_MeshLoad.cs から移動）
        // ================================================================

        /// <summary>メッシュアセットから読み込み（Transformなし）</summary>
        public static void LoadFromAsset(IEditorMeshLoadHost host)
        {
            string path = PLEditorBridge.I.OpenFilePanel("Select UnityMesh Asset", "Assets", "asset,fbx,obj");
            if (string.IsNullOrEmpty(path)) return;

            if (path.StartsWith(Application.dataPath))
                path = "Assets" + path.Substring(Application.dataPath.Length);

            Mesh loadedMesh = PLEditorBridge.I.LoadAssetAtPath<Mesh>(path);
            if (loadedMesh == null)
            {
                var allAssets = PLEditorBridge.I.LoadAllAssetsAtPath(path);
                foreach (var asset in allAssets)
                {
                    if (asset is Mesh m) { loadedMesh = m; break; }
                }
            }

            if (loadedMesh != null)
                host.AddLoadedMesh(loadedMesh, loadedMesh.name);
            else
                PLEditorBridge.I.DisplayDialog("Error", "メッシュを読み込めませんでした", "OK");
        }

        /// <summary>プレファブから読み込み（MeshFilter + SkinnedMeshRenderer対応）</summary>
        public static void LoadFromPrefab(IEditorMeshLoadHost host)
        {
            string path = PLEditorBridge.I.OpenFilePanel("Select Prefab", "Assets", "prefab");
            if (string.IsNullOrEmpty(path)) return;

            if (path.StartsWith(Application.dataPath))
                path = "Assets" + path.Substring(Application.dataPath.Length);

            GameObject prefab = PLEditorBridge.I.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                PLEditorBridge.I.DisplayDialog("Error", "プレファブを読み込めませんでした", "OK");
                return;
            }

            var meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
            var skinnedMeshRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            if (meshFilters.Length == 0 && skinnedMeshRenderers.Length == 0)
            {
                PLEditorBridge.I.DisplayDialog("Error", "プレファブにMeshFilterまたはSkinnedMeshRendererが見つかりませんでした", "OK");
                return;
            }

            foreach (var mf in meshFilters)
            {
                if (mf.sharedMesh == null) continue;
                string meshName = $"{prefab.name}_{mf.sharedMesh.name}";
                Material[] mats = null;
                var renderer = mf.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0)
                    mats = renderer.sharedMaterials;
                host.AddLoadedMesh(mf.sharedMesh, meshName, mats, mf.transform);
            }

            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh == null) continue;
                string meshName = $"{prefab.name}_{smr.sharedMesh.name}";
                Material[] mats = null;
                if (smr.sharedMaterials != null && smr.sharedMaterials.Length > 0)
                    mats = smr.sharedMaterials;
                host.AddLoadedMesh(smr.sharedMesh, meshName, mats, smr.transform);
            }
        }

        /// <summary>ヒエラルキーからメッシュを読み込み</summary>
        public static void LoadFromHierarchy(IEditorMeshLoadHost host)
        {
            var selected = PLEditorBridge.I.GetActiveGameObject();
            if (selected == null)
            {
                var selectedMesh = PLEditorBridge.I.GetActiveObject() as Mesh;
                if (selectedMesh != null)
                {
                    host.AddLoadedMesh(selectedMesh, selectedMesh.name);
                    return;
                }

                GameObject foundObject = FindFirstMeshInHierarchy();
                if (foundObject != null)
                {
                    Debug.Log($"[LoadFromHierarchy] ヒエラルキーから自動検出: {foundObject.name}");
                    selected = foundObject;
                }
                else
                {
                    PLEditorBridge.I.DisplayDialog("Info",
                        "GameObjectまたはMeshを選択してください\nヒエラルキー内にメッシュが見つかりませんでした", "OK");
                    return;
                }
            }

            var skinnedRenderers = selected.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinnedRenderers.Length > 0)
            {
                host.ShowSkinnedMeshImportDialogInternal(selected, skinnedRenderers);
            }
            else
            {
                Transform armatureRoot = DetectArmatureFolder(selected.transform);
                if (armatureRoot != null)
                {
                    int boneCount = CountDescendants(armatureRoot) + 1;
                    // SkinnedMeshImportDialogはEditor専用なのでhost経由で呼ぶ
                    // ダイアログなしでArmatureルートをそのまま渡す形でhost.LoadHierarchyを呼ぶ
                    // （ArmatureフォルダがあるがSMRなしの場合は仮想SMR配列で経由する）
                    host.ShowSkinnedMeshImportDialogInternal(selected, new SkinnedMeshRenderer[0]);
                }
                else
                {
                    host.LoadHierarchyFromGameObject(selected, null);
                }
            }
        }

        // ================================================================
        // 純粋ヘルパー（PolyLing_MeshLoad.cs から移動）
        // ================================================================

        /// <summary>Armatureフォルダを検出し、その最初の子（実際のルートボーン）を返す</summary>
        public static Transform DetectArmatureFolder(Transform root)
        {
            if (root == null) return null;
            foreach (Transform child in root)
            {
                if (child.name == "Armature")
                    return child.childCount > 0 ? child.GetChild(0) : child;
            }
            return null;
        }

        /// <summary>ヒエラルキーのルートオブジェクトを順に検索し、メッシュを持つ最初のルートを返す</summary>
        public static GameObject FindFirstMeshInHierarchy()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();
            foreach (var root in rootObjects)
            {
                if (HasMeshInDescendants(root)) return root;
            }
            return null;
        }

        /// <summary>指定オブジェクトまたはその子孫にメッシュがあるかチェック</summary>
        public static bool HasMeshInDescendants(GameObject obj)
        {
            foreach (var mf in obj.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null) return true;
            foreach (var smr in obj.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null) return true;
            return false;
        }

        /// <summary>複数SkinnedMeshRendererから最適なルートボーンを検出</summary>
        public static Transform DetectBestRootBone(SkinnedMeshRenderer[] smrs)
        {
            if (smrs == null || smrs.Length == 0) return null;

            foreach (var smr in smrs)
            {
                if (smr == null) continue;
                var animator = smr.GetComponentInParent<Animator>();
                if (animator != null && animator.avatar != null && animator.isHuman)
                {
                    var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                    if (hips != null)
                    {
                        Transform current = hips;
                        while (current.parent != null)
                        {
                            if (current.parent.name == "Armature")
                            {
                                Debug.Log($"[DetectBestRootBone] Found root bone under Armature: {current.name}");
                                return current;
                            }
                            if (current.parent == animator.transform)
                            {
                                if (current.name == "Armature" && current.childCount > 0)
                                {
                                    Debug.Log($"[DetectBestRootBone] Found Armature folder, returning first child: {current.GetChild(0).name}");
                                    return current.GetChild(0);
                                }
                                Debug.Log($"[DetectBestRootBone] Found root bone under Animator: {current.name}");
                                return current;
                            }
                            current = current.parent;
                        }
                        Debug.Log($"[DetectBestRootBone] Fallback to Hips: {hips.name}");
                        return hips;
                    }
                }
            }

            var rootBones = smrs
                .Where(s => s != null && s.rootBone != null)
                .Select(s => s.rootBone)
                .Distinct()
                .ToList();

            if (rootBones.Count == 0)
            {
                var allBones = new HashSet<Transform>();
                foreach (var smr in smrs)
                {
                    if (smr?.bones == null) continue;
                    foreach (var bone in smr.bones)
                        if (bone != null) allBones.Add(bone);
                }
                if (allBones.Count > 0)
                {
                    var topBone = allBones.OrderBy(b => GetHierarchyDepth(b)).First();
                    Transform current = topBone;
                    while (current.parent != null && allBones.Contains(current.parent))
                        current = current.parent;
                    if (current.parent != null && current.parent.name == "Armature")
                        return current;
                    return current;
                }
            }

            if (rootBones.Count == 0) return null;
            if (rootBones.Count == 1) return rootBones[0];
            return rootBones.OrderBy(b => GetHierarchyDepth(b)).First();
        }

        /// <summary>Transformの階層深度を返す</summary>
        public static int GetHierarchyDepth(Transform t)
        {
            int depth = 0;
            while (t != null && t.parent != null) { depth++; t = t.parent; }
            return depth;
        }

        /// <summary>子孫の数をカウント</summary>
        public static int CountDescendants(Transform t)
        {
            if (t == null) return 0;
            int count = 0;
            foreach (Transform child in t) count += 1 + CountDescendants(child);
            return count;
        }

        /// <summary>GameObjectを深さ優先で収集</summary>
        public static void CollectGameObjectsDepthFirst(GameObject go, List<GameObject> result)
        {
            if (go == null) return;
            result.Add(go);
            for (int i = 0; i < go.transform.childCount; i++)
                CollectGameObjectsDepthFirst(go.transform.GetChild(i).gameObject, result);
        }

        /// <summary>ボーンTransformを深さ優先で収集</summary>
        public static void CollectBoneTransformsDepthFirst(Transform bone, List<Transform> result)
        {
            if (bone == null) return;
            result.Add(bone);
            foreach (Transform child in bone)
                CollectBoneTransformsDepthFirst(child, result);
        }

        /// <summary>TransformからボーンMeshContextを作成</summary>
        public static MeshContext CreateMeshContextFromBone(Transform bone, Dictionary<Transform, int> boneToIndex)
        {
            var meshObject = new MeshObject(bone.name) { Type = MeshType.Bone };
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

            if (bone.parent != null && boneToIndex.TryGetValue(bone.parent, out int parentIndex))
            {
                meshContext.ParentIndex = parentIndex;
                meshContext.HierarchyParentIndex = parentIndex;
            }
            else
            {
                meshContext.ParentIndex = -1;
                meshContext.HierarchyParentIndex = -1;
            }

            meshContext.BoneTransform.Position = bone.localPosition;
            meshContext.BoneTransform.Rotation = bone.localEulerAngles;
            meshContext.BoneTransform.Scale = bone.localScale;
            meshContext.BoneTransform.UseLocalTransform = true;
            meshContext.OriginalPositions = new Vector3[0];
            meshContext.UnityMesh = new Mesh { name = bone.name };

            return meshContext;
        }

        /// <summary>GameObjectからMeshContextを作成（MeshFilter + SkinnedMeshRenderer対応）</summary>
        public static MeshContext CreateMeshContextFromGameObject(
            GameObject go,
            Dictionary<GameObject, int> goToIndex,
            Dictionary<Material, int> materialToIndex,
            Dictionary<Transform, int> boneToIndex = null)
        {
            var meshContext = new MeshContext
            {
                MeshObject = new MeshObject(go.name)
            };

            var parentTransform = go.transform.parent;
            if (parentTransform != null && goToIndex.TryGetValue(parentTransform.gameObject, out int parentIndex))
                meshContext.HierarchyParentIndex = parentIndex;
            else
                meshContext.HierarchyParentIndex = -1;

            meshContext.BoneTransform.Position = go.transform.localPosition;
            meshContext.BoneTransform.Rotation = go.transform.localEulerAngles;
            meshContext.BoneTransform.Scale = go.transform.localScale;

            bool isDefaultTransform =
                go.transform.localPosition == Vector3.zero &&
                go.transform.localEulerAngles == Vector3.zero &&
                go.transform.localScale == Vector3.one;
            meshContext.BoneTransform.UseLocalTransform = !isDefaultTransform;

            Mesh sourceMesh = null;
            Material[] sharedMats = null;
            Dictionary<int, int> boneIndexRemap = null;

            var meshFilter = go.GetComponent<MeshFilter>();
            var skinnedMeshRenderer = go.GetComponent<SkinnedMeshRenderer>();

            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                sourceMesh = meshFilter.sharedMesh;
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null) sharedMats = renderer.sharedMaterials;
            }
            else if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMesh != null)
            {
                sourceMesh = skinnedMeshRenderer.sharedMesh;
                sharedMats = skinnedMeshRenderer.sharedMaterials;
                if (boneToIndex != null && skinnedMeshRenderer.bones != null)
                {
                    boneIndexRemap = new Dictionary<int, int>();
                    for (int i = 0; i < skinnedMeshRenderer.bones.Length; i++)
                    {
                        var bone = skinnedMeshRenderer.bones[i];
                        if (bone != null && boneToIndex.TryGetValue(bone, out int meshCtxBoneIndex))
                            boneIndexRemap[i] = meshCtxBoneIndex;
                    }
                }
            }

            if (sourceMesh != null)
            {
                bool isSkinnedMesh = boneIndexRemap != null && boneIndexRemap.Count > 0;
                meshContext.MeshObject.FromUnityMesh(sourceMesh, true, isSkinnedMesh);

                if (boneIndexRemap != null && boneIndexRemap.Count > 0)
                {
                    RemapBoneWeightIndices(meshContext.MeshObject, boneIndexRemap);
                    Debug.Log($"[CreateMeshContextFromGameObject] Remapped {boneIndexRemap.Count} bone indices for '{go.name}'");
                }

                if (sharedMats != null)
                {
                    for (int subMeshIdx = 0; subMeshIdx < sourceMesh.subMeshCount; subMeshIdx++)
                    {
                        Material mat = subMeshIdx < sharedMats.Length ? sharedMats[subMeshIdx] : null;
                        int globalMatIndex = 0;
                        if (mat != null && materialToIndex.TryGetValue(mat, out int idx))
                            globalMatIndex = idx;
                        foreach (var face in meshContext.MeshObject.Faces)
                        {
                            if (face.MaterialIndex == subMeshIdx)
                                face.MaterialIndex = globalMatIndex;
                        }
                    }
                }
            }

            meshContext.OriginalPositions = meshContext.MeshObject.Vertices.Select(v => v.Position).ToArray();
            meshContext.UnityMesh = meshContext.MeshObject.ToUnityMesh();
            meshContext.UnityMesh.name = go.name;

            return meshContext;
        }

        /// <summary>MeshObjectのBoneWeightインデックスを再マッピング</summary>
        public static void RemapBoneWeightIndices(MeshObject meshObject, Dictionary<int, int> remap)
        {
            foreach (var vertex in meshObject.Vertices)
            {
                if (!vertex.HasBoneWeight) continue;
                var bw = vertex.BoneWeight.Value;
                vertex.BoneWeight = new BoneWeight
                {
                    boneIndex0 = (bw.weight0 > 0 && remap.TryGetValue(bw.boneIndex0, out int idx0)) ? idx0 : 0,
                    boneIndex1 = (bw.weight1 > 0 && remap.TryGetValue(bw.boneIndex1, out int idx1)) ? idx1 : 0,
                    boneIndex2 = (bw.weight2 > 0 && remap.TryGetValue(bw.boneIndex2, out int idx2)) ? idx2 : 0,
                    boneIndex3 = (bw.weight3 > 0 && remap.TryGetValue(bw.boneIndex3, out int idx3)) ? idx3 : 0,
                    weight0 = bw.weight0,
                    weight1 = bw.weight1,
                    weight2 = bw.weight2,
                    weight3 = bw.weight3
                };
            }
        }

        /// <summary>GameObjectからマテリアル配列を取得（MeshRenderer + SkinnedMeshRenderer対応）</summary>
        public static Material[] GetSharedMaterials(GameObject go)
        {
            if (go == null) return null;
            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.sharedMaterials != null && meshRenderer.sharedMaterials.Length > 0)
                return meshRenderer.sharedMaterials;
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMaterials != null && smr.sharedMaterials.Length > 0)
                return smr.sharedMaterials;
            return null;
        }
    }
}
