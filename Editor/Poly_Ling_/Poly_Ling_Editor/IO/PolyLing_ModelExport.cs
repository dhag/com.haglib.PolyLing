// Assets/Editor/Poly_Ling/PolyLing/SimpleMeshFactory_ModelExport.cs
// モデル全体エクスポート機能（複数メッシュ対応）
// - ヒエラルキーに追加
// - SkinnedMeshRendererとしてエクスポート
// - プレファブ保存
// - メッシュアセット保存

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;

public partial class PolyLing
{
    // ================================================================
    // モデル全体エクスポート（複数メッシュ対応）
    // ================================================================

    /// <summary>
    /// モデル全体をヒエラルキーに追加
    /// HierarchyParentIndexに基づいて親子関係を再現
    /// BoneTransform.ExportAsSkinned が有効な場合は SkinnedMeshRenderer を使用
    /// </summary>
    private void AddModelToHierarchy()
    {
        if (_meshContextList.Count == 0)
            return;

        // スキンメッシュとして出力するかチェック
        // いずれかのMeshContextのBoneTransform.ExportAsSkinnedがtrueならSkinnedMeshRendererで出力
        bool shouldExportAsSkinned = 
            _meshContextList.Any(ctx => ctx?.BoneTransform != null && ctx.BoneTransform.ExportAsSkinned);
        
        if (shouldExportAsSkinned)
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

        // Armature/Meshes構造を使用するかどうか
        bool useArmatureMeshesStructure = _createArmatureMeshesFolder;
        
        // Armature/Meshes構造用の親オブジェクト
        GameObject armatureParent = null;
        GameObject meshesParent = null;
        
        // ボーンがあるかどうか確認
        bool hasBone = _meshContextList.Any(ctx => ctx?.Type == MeshType.Bone);
        bool hasMesh = _meshContextList.Any(ctx => 
            ctx?.MeshObject != null && ctx.MeshObject.VertexCount > 0);
        
        if (useArmatureMeshesStructure && hasBone)
        {
            // Armatureフォルダを作成
            armatureParent = new GameObject("Armature");
            armatureParent.transform.SetParent(rootParent, false);
            Undo.RegisterCreatedObjectUndo(armatureParent, "Create Armature");
            
            // Meshesフォルダを作成（メッシュがある場合のみ）
            if (hasMesh)
            {
                meshesParent = new GameObject("Meshes");
                meshesParent.transform.SetParent(rootParent, false);
                Undo.RegisterCreatedObjectUndo(meshesParent, "Create Meshes");
            }
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
                    materialsToUse = GetMaterialsForBakedMirror(usedMatIndices, sharedMaterials, meshContext.MirrorMaterialOffset);
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
            bool isBone = (meshContext.Type == MeshType.Bone);
            bool hasVertices = (meshContext.MeshObject != null && meshContext.MeshObject.VertexCount > 0);

            if (parentIndex >= 0 && parentIndex < createdObjects.Length && createdObjects[parentIndex] != null)
            {
                // 親がメッシュリスト内にある場合
                go.transform.SetParent(createdObjects[parentIndex].transform, false);
            }
            else
            {
                // ルートオブジェクト
                if (useArmatureMeshesStructure && hasBone)
                {
                    if (isBone && armatureParent != null)
                    {
                        // ボーンはArmature下に配置
                        go.transform.SetParent(armatureParent.transform, false);
                    }
                    else if (!isBone && meshesParent != null)
                    {
                        // ボーン以外（メッシュ）はMeshes下に配置
                        go.transform.SetParent(meshesParent.transform, false);
                    }
                    else
                    {
                        // どちらでもない場合はrootParent直下
                        go.transform.SetParent(rootParent, false);
                    }
                }
                else
                {
                    // 従来通りrootParentの子に
                    go.transform.SetParent(rootParent, false);
                }

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
    /// 選択中のヒエラルキーに同名メッシュを上書き
    /// ヒエラルキーからインポートしたものを編集後、元に戻す用途
    /// </summary>
    private void OverwriteToHierarchy()
    {
        var targetRoot = Selection.activeGameObject;
        if (targetRoot == null)
        {
            EditorUtility.DisplayDialog("Error", "ヒエラルキーでGameObjectを選択してください", "OK");
            return;
        }

        if (_meshContextList.Count == 0)
        {
            EditorUtility.DisplayDialog("Error", "エクスポートするメッシュがありません", "OK");
            return;
        }

        // 共有マテリアル配列を取得
        Material[] sharedMaterials = GetMaterialsForSave(null);

        int overwriteCount = 0;
        int skipCount = 0;
        var messages = new List<string>();

        foreach (var meshContext in _meshContextList)
        {
            if (meshContext?.MeshObject == null || meshContext.MeshObject.VertexCount == 0)
                continue;

            string meshName = meshContext.Name;

            // 同名のGameObjectを検索（子孫含む）
            Transform found = FindChildByName(targetRoot.transform, meshName);
            if (found == null)
            {
                skipCount++;
                continue;
            }

            GameObject targetGO = found.gameObject;

            // MeshObjectからUnity Meshを生成
            Mesh newMesh;
            Material[] materialsToUse;
            if (_bakeMirror && meshContext.IsMirrored)
            {
                newMesh = BakeMirrorToUnityMesh(meshContext, _mirrorFlipU, out var usedMatIndices);
                materialsToUse = GetMaterialsForBakedMirror(usedMatIndices, sharedMaterials, meshContext.MirrorMaterialOffset);
            }
            else
            {
                newMesh = meshContext.MeshObject.ToUnityMeshShared(
                    _model.MaterialCount > 0 ? _model.MaterialCount : meshContext.MeshObject.SubMeshCount);
                materialsToUse = sharedMaterials;
            }
            newMesh.name = meshName;

            // SkinnedMeshRenderer をチェック
            var smr = targetGO.GetComponent<SkinnedMeshRenderer>();
            if (smr != null)
            {
                Undo.RecordObject(smr, $"Overwrite Mesh {meshName}");
                smr.sharedMesh = newMesh;
                smr.sharedMaterials = materialsToUse;
                overwriteCount++;
                messages.Add($"[SMR] {meshName}");
                continue;
            }

            // MeshFilter をチェック
            var mf = targetGO.GetComponent<MeshFilter>();
            if (mf != null)
            {
                Undo.RecordObject(mf, $"Overwrite Mesh {meshName}");
                mf.sharedMesh = newMesh;

                var mr = targetGO.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    Undo.RecordObject(mr, $"Overwrite Materials {meshName}");
                    mr.sharedMaterials = materialsToUse;
                }
                overwriteCount++;
                messages.Add($"[MF] {meshName}");
                continue;
            }

            // メッシュコンポーネントがない
            skipCount++;
        }

        if (overwriteCount > 0)
        {
            Debug.Log($"[OverwriteToHierarchy] Overwritten {overwriteCount} meshes:\n" + string.Join("\n", messages));
        }

        if (skipCount > 0)
        {
            Debug.Log($"[OverwriteToHierarchy] Skipped {skipCount} meshes (not found or no mesh component)");
        }

        EditorUtility.DisplayDialog("完了", 
            $"上書き: {overwriteCount} メッシュ\nスキップ: {skipCount} メッシュ", "OK");
    }

    /// <summary>
    /// 子孫から指定名のTransformを検索（自分自身も含む）
    /// </summary>
    private Transform FindChildByName(Transform parent, string name)
    {
        if (parent.name == name)
            return parent;

        foreach (Transform child in parent)
        {
            var found = FindChildByName(child, name);
            if (found != null)
                return found;
        }

        return null;
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

        // Armature/Meshes構造を使用するかどうか
        bool useArmatureMeshesStructure = _createArmatureMeshesFolder;
        
        // Armature/Meshes構造用の親オブジェクト
        GameObject armatureParent = null;
        GameObject meshesParent = null;
        
        // ボーンがあるかどうか確認
        bool hasBone = _meshContextList.Any(ctx => ctx?.Type == MeshType.Bone);
        bool hasMesh = _meshContextList.Any(ctx => 
            ctx?.MeshObject != null && ctx.MeshObject.VertexCount > 0);
        
        if (useArmatureMeshesStructure && hasBone)
        {
            // Armatureフォルダを作成
            armatureParent = new GameObject("Armature");
            armatureParent.transform.SetParent(rootParent, false);
            Undo.RegisterCreatedObjectUndo(armatureParent, "Create Armature");
            
            // Meshesフォルダを作成（メッシュがある場合のみ）
            if (hasMesh)
            {
                meshesParent = new GameObject("Meshes");
                meshesParent.transform.SetParent(rootParent, false);
                Undo.RegisterCreatedObjectUndo(meshesParent, "Create Meshes");
            }
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
            bool isBone = (meshContext.Type == MeshType.Bone);
            bool hasVertices = (meshContext.MeshObject != null && meshContext.MeshObject.VertexCount > 0);

            if (parentIndex >= 0 && parentIndex < createdObjects.Length && createdObjects[parentIndex] != null)
            {
                // 親がメッシュリスト内にある場合
                go.transform.SetParent(createdObjects[parentIndex].transform, false);
            }
            else
            {
                // ルートオブジェクト
                if (useArmatureMeshesStructure && hasBone)
                {
                    if (isBone && armatureParent != null)
                    {
                        // ボーンはArmature下に配置
                        go.transform.SetParent(armatureParent.transform, false);
                    }
                    else if (!isBone && meshesParent != null)
                    {
                        // ボーン以外（メッシュ）はMeshes下に配置
                        go.transform.SetParent(meshesParent.transform, false);
                    }
                    else
                    {
                        // どちらでもない場合はrootParent直下
                        go.transform.SetParent(rootParent, false);
                    }
                }
                else
                {
                    // 従来通りrootParentの子に
                    go.transform.SetParent(rootParent, false);
                }

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

        // === Pass 3: ボーン配列とバインドポーズを計算（ボーンタイプのみ） ===
        // TypedIndices を使ってボーンリストを取得
        var boneEntries = _model.Bones;
        int boneCount = boneEntries.Count;
        
        var bones = new Transform[boneCount];
        var bindPoses = new Matrix4x4[boneCount];

        for (int bi = 0; bi < boneCount; bi++)
        {
            var entry = boneEntries[bi];
            int masterIndex = entry.MasterIndex;
            
            if (masterIndex >= 0 && masterIndex < createdObjects.Length && createdObjects[masterIndex] != null)
            {
                bones[bi] = createdObjects[masterIndex].transform;
                
                // MeshContext.BindPoseが設定されていればそれを使用、なければ計算
                var boneCtx = entry.Context;
                if (boneCtx != null && boneCtx.BindPose != Matrix4x4.identity)
                {
                    bindPoses[bi] = boneCtx.BindPose;
                }
                else
                {
                    // バインドポーズ = ワールド→ローカル変換行列
                    bindPoses[bi] = bones[bi].worldToLocalMatrix;
                }
            }
        }
        
        // デバッグ: ボーン配列のサマリー
        Debug.Log($"[ExportSkinned] Bone Array: {boneCount} bones (from {_meshContextList.Count} total objects)");
        if (boneCount > 0 && boneCount <= 10)
        {
            for (int bi = 0; bi < boneCount; bi++)
            {
                Debug.Log($"  Bone[{bi}]: '{boneEntries[bi].Name}' (master index: {boneEntries[bi].MasterIndex})");
            }
        }

        // ボーンがない場合の警告
        if (boneCount == 0)
        {
            Debug.LogWarning("[ExportSkinned] No bones found. SkinnedMeshRenderer will have empty bone array.");
        }

        // === Pass 4: 各メッシュに対して SkinnedMeshRenderer をセットアップ ===
        for (int i = 0; i < _meshContextList.Count; i++)
        {
            var meshContext = _meshContextList[i];
            var go = createdObjects[i];
            if (meshContext?.MeshObject == null || go == null) continue;
            if (meshContext.MeshObject.VertexCount == 0) continue;

            // ボーンインデックスを変換するためのTypedIndices参照
            var typedIndices = _model.TypedIndices;

            // BoneWeight が未設定の頂点にデフォルト値を設定
            // デフォルトボーンインデックスは、このメッシュのマスターインデックスをボーンインデックスに変換
            int defaultBoneIndex = typedIndices.MasterToBoneIndex(i);
            if (defaultBoneIndex < 0)
            {
                // このオブジェクト自体がボーンでない場合、最初のボーンをデフォルトとする
                defaultBoneIndex = 0;
            }
            EnsureBoneWeightsWithConversion(meshContext.MeshObject, defaultBoneIndex, typedIndices);

            // メッシュを生成
            Mesh meshCopy;
            Material[] materialsToUse;
            if (_bakeMirror && meshContext.IsMirrored)
            {
                meshCopy = BakeMirrorToUnityMesh(meshContext, _mirrorFlipU, out var usedMatIndices);
                materialsToUse = GetMaterialsForBakedMirror(usedMatIndices, sharedMaterials, meshContext.MirrorMaterialOffset);
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

        // Animatorコンポーネント追加オプションがONの場合、rootParentに追加
        if (_addAnimatorComponent && rootParent != null && rootParent.GetComponent<Animator>() == null)
        {
            var animator = rootParent.gameObject.AddComponent<Animator>();
            
            // Avatar生成オプションがONの場合
            if (_createAvatarOnExport && _model?.HumanoidMapping != null && !_model.HumanoidMapping.IsEmpty)
            {
                // HumanoidBoneMappingからTransformマッピングを作成
                var boneTransformMapping = new Dictionary<string, Transform>();
                
                Debug.Log($"[ExportSkinned] Building Avatar bone mapping from HumanoidMapping ({_model.HumanoidMapping.Count} entries)");
                
                foreach (var humanoidBone in Poly_Ling.Data.HumanoidBoneMapping.AllHumanoidBones)
                {
                    int meshContextIndex = _model.HumanoidMapping.Get(humanoidBone);
                    if (meshContextIndex >= 0 && meshContextIndex < createdObjects.Length && createdObjects[meshContextIndex] != null)
                    {
                        var boneTransform = createdObjects[meshContextIndex].transform;
                        boneTransformMapping[humanoidBone] = boneTransform;
                        Debug.Log($"  {humanoidBone} -> [{meshContextIndex}] {boneTransform.name}");
                    }
                }
                
                // 必須ボーンのチェック
                var missingRequired = Poly_Ling.Data.HumanoidBoneMapping.RequiredBones
                    .Where(b => !boneTransformMapping.ContainsKey(b))
                    .ToList();
                
                if (missingRequired.Count > 0)
                {
                    Debug.LogWarning($"[ExportSkinned] Missing required bones for Avatar: {string.Join(", ", missingRequired)}");
                }
                
                if (boneTransformMapping.Count > 0)
                {
                    // マテリアル保存フォルダを取得してAvatar保存先を決定
                    string saveFolder = GetMaterialSaveDirectory();
                    string avatarName = !string.IsNullOrEmpty(_model.Name) ? _model.Name : "Model";
                    string avatarPath = $"{saveFolder}/{avatarName}_Avatar.asset";
                    
                    Debug.Log($"[ExportSkinned] Creating Avatar with {boneTransformMapping.Count} bone mappings, root: {rootParent.name}");
                    
                    // AvatarCreatorPanelの静的メソッドを使用してAvatar生成
                    var avatar = Poly_Ling.MISC.AvatarCreatorPanel.BuildAndSaveAvatar(
                        rootParent.gameObject,
                        boneTransformMapping,
                        avatarPath);
                    
                    if (avatar != null && avatar.isValid && avatar.isHuman)
                    {
                        animator.avatar = avatar;
                        Debug.Log($"[ExportSkinned] Avatar created and assigned: {avatarPath}");
                    }
                    else if (avatar != null)
                    {
                        Debug.LogWarning($"[ExportSkinned] Avatar created but not valid for humanoid (isValid: {avatar.isValid}, isHuman: {avatar.isHuman})");
                        // 無効なAvatarは設定しない
                    }
                }
                else
                {
                    Debug.LogWarning("[ExportSkinned] No valid bone mappings found for Avatar creation");
                }
            }
        }

        // 作成したルートオブジェクト、または最初のルートオブジェクトを選択
        GameObject objectToSelect = createdRoot ?? firstRootObject;
        if (objectToSelect != null)
        {
            Selection.activeGameObject = objectToSelect;
            EditorGUIUtility.PingObject(objectToSelect);
        }

        Debug.Log($"Added model to hierarchy as SkinnedMesh: {boneCount} bones, {_meshContextList.Count} objects");
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
                    mr.sharedMaterials = GetMaterialsForBakedMirror(usedMatIndices, sharedMaterials, meshContext.MirrorMaterialOffset);
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