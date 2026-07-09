// Editor/HierarchyIO/HierarchyImportWindow.cs
// ============================================================
// 段階B：インポートエディタ拡張（Unityヒエラルキー → プロジェクトファイル）
// ============================================================
//
// 【処理の流れ】
//   1. シーン上の GameObject 階層（SkinnedMeshRenderer＋ボーン／MeshFilter）を読み、
//      新規 ModelContext を構築する（BuildModelFromHierarchy）。
//   2. CsvModelSerializer.SaveModel でプロジェクトファイル（フォルダ形式）へ保存。
//
// 【設計方針（ガイダンス反映）】
//   - 「ファイル」＝プロジェクトファイル形式（座標変換なし＝非破壊）。
//   - Unityメッシュ読取は MeshObject.FromUnityMesh()（内部でメッシュブリッジ）経由。
//     UnityEngine.Mesh を直接解析しない。
//   - 本拡張は Editor アセンブリ（PolyLing.Editor）に閉じ、Runtime は無改変。
//   - ヒエラルキー⇔ModelContext の対応は段階A（ModelContext→ヒエラルキー）の逆。
//     BoneTransform はボーン Transform の local pos/rot/scale を、BindPose は
//     SkinnedMeshRenderer.sharedMesh.bindposes を採用する（座標変換は挟まない）。
//
// 【移植元】
//   旧 PolyLing_MeshLoad.LoadHierarchyFromGameObject（1043行版）。
//   ライブ編集状態・Undo・UnifiedAdapter 等の副作用を除去し、「新規 ModelContext を
//   構築して返す」純粋な組み立て処理に再構成した。アルゴリズム（ボーン階層／BindPose／
//   マテリアル収集／BoneWeight index リマップ）は現行 API のまま踏襲。
//
// 【ルートボーン検出（v1）】
//   明示指定が無い場合、root 直下の "Armature" の最初の子 → 各 SkinnedMeshRenderer の
//   rootBone の順で検出する。複数ルートボーン（Armature 直下に複数）は最初の subtree のみ
//   対象（v1制限。必要なら明示指定で対応）。
//
// ============================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.Ops;

namespace Poly_Ling.EditorIO
{
    /// <summary>
    /// Unityヒエラルキーを ModelContext に取り込み、プロジェクトファイルへ保存するエディタ拡張。
    /// </summary>
    public class HierarchyImportWindow : EditorWindow
    {
        // 入力
        private GameObject _rootObject;
        private Transform  _boneRoot;          // 任意。未指定なら自動検出。
        private bool _restoreHumanoid = true;  // Animator/Avatar から Humanoid+可動域を復元
        private string     _outputFolder = "";
        private bool       _detectNamedMirror = true;

        [MenuItem("PolyLing/IO/Hierarchy Import (Hierarchy → Project File)")]
        public static void Open()
        {
            var w = GetWindow<HierarchyImportWindow>(true, "Hierarchy Import", true);
            // 既定で選択中の GameObject をルートに採用
            if (w._rootObject == null && UnityEditor.Selection.activeGameObject != null)
                w._rootObject = UnityEditor.Selection.activeGameObject;
        }

        // ================================================================
        // UI（IMGUI）
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.LabelField("ヒエラルキー → プロジェクトファイル", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            _rootObject = (GameObject)EditorGUILayout.ObjectField("ルート GameObject", _rootObject, typeof(GameObject), true);
            _boneRoot   = (Transform)EditorGUILayout.ObjectField("ルートボーン（任意）", _boneRoot, typeof(Transform), true);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                _outputFolder = EditorGUILayout.TextField("保存先フォルダ", _outputFolder);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string sel = EditorUtility.OpenFolderPanel("保存先フォルダを選択", _outputFolder, "");
                    if (!string.IsNullOrEmpty(sel)) _outputFolder = sel;
                }
            }

            EditorGUILayout.Space();
            _detectNamedMirror = EditorGUILayout.Toggle("名前末尾\"+\"をミラー検出", _detectNamedMirror);
            _restoreHumanoid   = EditorGUILayout.Toggle("Avatarから Humanoid 復元", _restoreHumanoid);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_rootObject == null || string.IsNullOrEmpty(_outputFolder)))
            {
                if (GUILayout.Button("取り込んでプロジェクトファイルに保存", GUILayout.Height(28)))
                {
                    ImportAndSave();
                }
            }
        }

        // ================================================================
        // 取り込み → 保存
        // ================================================================

        private void ImportAndSave()
        {
            if (_rootObject == null)
            {
                EditorUtility.DisplayDialog("エラー", "ルート GameObject が未指定です。", "OK");
                return;
            }
            if (string.IsNullOrEmpty(_outputFolder))
            {
                EditorUtility.DisplayDialog("エラー", "保存先フォルダが未指定です。", "OK");
                return;
            }

            // プレファブ資産が指定された場合はシーンへ一時インスタンス化して読む
            //   （BindPose フォールバックの worldToLocalMatrix や剛体の world 位置は
            //     「原点評価」を前提とするため、資産直読みでは不正確になりうる。
            //     一時インスタンスを原点に置いて読取り、後で破棄する）。
            bool isAsset = EditorUtility.IsPersistent(_rootObject);
            GameObject workRoot = _rootObject;
            Transform boneRootHint = _boneRoot;
            bool temp = false;
            if (isAsset)
            {
                workRoot = PrefabUtility.InstantiatePrefab(_rootObject) as GameObject;
                if (workRoot == null) workRoot = Instantiate(_rootObject);
                temp = true;
                // 原点評価にするため一時インスタンスを原点・無回転へ
                workRoot.transform.position = Vector3.zero;
                workRoot.transform.rotation = Quaternion.identity;
                // 資産上の _boneRoot はインスタンスの Transform と別物なのでパスで再解決
                boneRootHint = RemapToInstance(_rootObject.transform, _boneRoot, workRoot.transform);
            }

            try
            {
                Transform boneRoot = boneRootHint != null ? boneRootHint : AutoDetectBoneRoot(workRoot);

                ModelContext model;
                try
                {
                    model = BuildModelFromHierarchy(workRoot, boneRoot, _detectNamedMirror);
                }
                catch (Exception e)
                {
                    EditorUtility.DisplayDialog("エラー", "取り込みに失敗しました:\n" + e.Message, "OK");
                    Debug.LogException(e);
                    return;
                }

                if (model == null || model.MeshContextCount == 0)
                {
                    EditorUtility.DisplayDialog("エラー", "取り込み対象（メッシュ/ボーン）が見つかりませんでした。", "OK");
                    return;
                }

                // Avatar から Humanoid + 可動域を復元（案X: Avatar が Humanoid の正本）。
                // ※ per-bone へ復元後、Dict を整合してから保存する（SaveModel の Sync が
                //   Dict→per-bone のため、Dict を先に埋めないと復元が消える）。
                if (_restoreHumanoid)
                {
                    int n = RestoreHumanoidFromAvatar(model, workRoot);
                    if (n > 0)
                        HumanoidMappingResolver.RebuildMappingFromPerBone(model);
                }

                // IK 付帯を attach.csv（プレファブ同居）から復元（プレファブ資産入力時のみ）
                if (isAsset)
                {
                    string assetPath = AssetDatabase.GetAssetPath(_rootObject);
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        string dir = Path.GetDirectoryName(assetPath);
                        string attachPath = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "attach.csv");
                        if (!string.IsNullOrEmpty(attachPath) && File.Exists(attachPath))
                        {
                            AttachSidecarCsv.Read(model, attachPath);
                            IKChainResolver.RebuildLinksFromPerBone(model);
                            Debug.Log("[HierarchyImport] attach.csv から IK 付帯を復元。");
                        }
                    }
                }

                Directory.CreateDirectory(_outputFolder);
                CsvModelSerializer.SaveModel(_outputFolder, model);

                // Assets 配下に保存した場合は反映
                if (_outputFolder.Replace("\\", "/").Contains("/Assets"))
                    AssetDatabase.Refresh();

                EditorUtility.DisplayDialog(
                    "完了",
                    $"保存しました:\n{_outputFolder}\n(コンテキスト数: {model.MeshContextCount})",
                    "OK");
            }
            finally
            {
                if (temp && workRoot != null) DestroyImmediate(workRoot);   // 一時インスタンスを後片付け
            }
        }

        /// <summary>
        /// 資産側の _boneRoot を、インスタンス側の同一相対パスの Transform へ再解決する。
        /// _boneRoot が assetRoot 配下でない/未指定なら null（呼び出し側で自動検出）。
        /// </summary>
        private static Transform RemapToInstance(Transform assetRoot, Transform boneRoot, Transform instRoot)
        {
            if (boneRoot == null || assetRoot == null || instRoot == null) return null;
            if (boneRoot == assetRoot) return instRoot;

            // assetRoot → boneRoot の相対パスを構築
            var names = new List<string>();
            var t = boneRoot;
            while (t != null && t != assetRoot)
            {
                names.Add(t.name);
                t = t.parent;
            }
            if (t != assetRoot) return null;   // assetRoot 配下でない
            names.Reverse();
            return instRoot.Find(string.Join("/", names));
        }

        /// <summary>
        /// workRoot の Animator(human Avatar) から Humanoid 割当 + 可動域を復元し、
        /// model のボーン MeshObject（名前一致）へ per-bone で設定する。返り値は復元件数。
        /// HumanBodyBone は enum 形（5a と一致）、HumanLimit は度→ラジアンで格納。
        /// </summary>
        private static int RestoreHumanoidFromAvatar(ModelContext model, GameObject workRoot)
        {
            if (model == null || workRoot == null) return 0;
            var animator = workRoot.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return 0;

            // 可動域を HumanTrait 名でマップ（度）
            var limitByTrait = new Dictionary<string, HumanLimit>();
            var human = animator.avatar.humanDescription.human;
            if (human != null)
                foreach (var hb in human)
                    if (!string.IsNullOrEmpty(hb.humanName)) limitByTrait[hb.humanName] = hb.limit;

            // ボーン名 → MeshObject（Type==Bone・先勝ち）
            var boneByName = new Dictionary<string, MeshObject>();
            var list = model.MeshContextList;
            for (int i = 0; i < list.Count; i++)
            {
                var ctx = list[i];
                var mo = ctx?.MeshObject;
                if (mo == null || mo.Type != MeshType.Bone) continue;
                if (!string.IsNullOrEmpty(mo.Name) && !boneByName.ContainsKey(mo.Name))
                    boneByName[mo.Name] = mo;
            }

            int count = 0;
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var hbb = (HumanBodyBones)i;
                var tf = animator.GetBoneTransform(hbb);
                if (tf == null) continue;
                if (!boneByName.TryGetValue(tf.name, out var mo)) continue;

                mo.HumanBodyBone = hbb.ToString();   // enum 形（5a と一致）

                string traitName = HumanTrait.BoneName[i];
                if (limitByTrait.TryGetValue(traitName, out var lim) && !lim.useDefaultValues)
                {
                    mo.HumanLimit = new HumanLimitData
                    {
                        UseDefaultValues = false,
                        Min    = lim.min * Mathf.Deg2Rad,
                        Max    = lim.max * Mathf.Deg2Rad,
                        Center = lim.center * Mathf.Deg2Rad,
                        AxisLength = lim.axisLength
                    };
                }
                count++;
            }
            if (count > 0) Debug.Log($"[HierarchyImport] Avatar から Humanoid 復元: {count} 件");
            return count;
        }

        /// <summary>
        /// ルートボーンの自動検出。
        /// "Armature" の最初の子 → 各 SkinnedMeshRenderer.rootBone の順。
        /// </summary>
        private static Transform AutoDetectBoneRoot(GameObject root)
        {
            var armature = root.transform.Find("Armature");
            if (armature != null && armature.childCount > 0)
                return armature.GetChild(0);

            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var smr in smrs)
                if (smr.rootBone != null) return smr.rootBone;

            return null;
        }

        // ================================================================
        // ヒエラルキー → ModelContext（移植: LoadHierarchyFromGameObject の組み立て部）
        // ================================================================

        private static ModelContext BuildModelFromHierarchy(
            GameObject rootGameObject, Transform boneRootTransform, bool detectNamedMirror)
        {
            var model = new ModelContext { Name = rootGameObject.name };

            var gameObjects = new List<GameObject>();
            CollectGameObjectsDepthFirst(rootGameObject, gameObjects);

            // ── ボーン収集 ＋ BindPose 収集 ──────────────────────────
            //   ボーン集合 = 全 SkinnedMeshRenderer の smr.bones（実スキニングボーン）
            //     ∪ boneRoot subtree（どのメッシュにも束ねられない揺れボーン等）。
            //   ※ 従来は boneRoot の単一 subtree のみを収集していたため、subtree 外の
            //     スキニングボーンが Mesh へ転落していた（例: Unity-chan の Character1_*）。
            //     smr.bones の和集合で必ずボーンとして拾い、全階層 DFS 順で整列する。
            var boneTransforms = new List<Transform>();
            var boneToIndex    = new Dictionary<Transform, int>();
            var boneBindPoses  = new Dictionary<Transform, Matrix4x4>();

            {
                var boneSet = new HashSet<Transform>();

                // boneRoot subtree（スキニングに使われない骨も拾う）
                if (boneRootTransform != null)
                {
                    var sub = new List<Transform>();
                    CollectBoneTransformsDepthFirst(boneRootTransform, sub);
                    foreach (var t in sub) boneSet.Add(t);
                }

                // 全 SMR の smr.bones を併合 ＋ BindPose 収集（boneRoot の有無に依らず実行）
                var smrs = rootGameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                foreach (var smr in smrs)
                {
                    if (smr.bones == null) continue;
                    foreach (var b in smr.bones)
                        if (b != null) boneSet.Add(b);

                    if (smr.sharedMesh == null) continue;
                    var bindposes = smr.sharedMesh.bindposes;
                    if (bindposes == null) continue;
                    for (int i = 0; i < smr.bones.Length && i < bindposes.Length; i++)
                    {
                        var bone = smr.bones[i];
                        if (bone != null && !boneBindPoses.ContainsKey(bone))
                            boneBindPoses[bone] = bindposes[i];
                    }
                }

                // gameObjects は全階層 DFS 順（親→子）。boneSet のものを順に採用。
                foreach (var go in gameObjects)
                    if (boneSet.Contains(go.transform))
                        boneTransforms.Add(go.transform);
                for (int i = 0; i < boneTransforms.Count; i++)
                    boneToIndex[boneTransforms[i]] = i;
            }

            // ── マテリアル収集（重複排除）──────────────────────────
            var allMaterials    = new List<Material>();
            var materialToIndex = new Dictionary<Material, int>();
            foreach (var go in gameObjects)
            {
                var sharedMats = GetSharedMaterials(go);
                if (sharedMats == null) continue;
                foreach (var mat in sharedMats)
                {
                    if (mat != null && !materialToIndex.ContainsKey(mat))
                    {
                        materialToIndex[mat] = allMaterials.Count;
                        allMaterials.Add(mat);
                    }
                }
            }
            if (allMaterials.Count == 0) allMaterials.Add(null);
            model.SetMaterials(allMaterials);
            model.CurrentMaterialIndex = 0;

            // ── ボーンを先に追加（PMXと同様の順序）──────────────────
            int boneStartIndex = 0;
            if (boneTransforms.Count > 0)
            {
                for (int i = 0; i < boneTransforms.Count; i++)
                {
                    var boneTransform = boneTransforms[i];
                    var boneCtx = CreateMeshContextFromBone(boneTransform, boneToIndex);
                    boneCtx.ParentModelContext = model;
                    model.Add(boneCtx);

                    // BindPose: 収集できた場合はそれを、無ければワールド逆行列を採用
                    boneCtx.BindPose = boneBindPoses.TryGetValue(boneTransform, out Matrix4x4 bindPose)
                        ? bindPose
                        : boneTransform.worldToLocalMatrix;

                    // BonePoseData は BoneTransform へのデルタ。IsActive のみ立て、Pre は零のまま
                    boneCtx.BonePoseData = new BonePoseData { IsActive = true };
                    // WorldMatrix は利用側で ComputeWorldMatrices() により計算される
                }
                boneStartIndex = boneTransforms.Count;
            }

            // ── メッシュ GameObject を抽出（ボーン／フォルダを除外）──
            var boneGameObjects = new HashSet<GameObject>();
            foreach (var bone in boneTransforms) boneGameObjects.Add(bone.gameObject);

            // "Armature"/"Meshes"/"RigidBodies" の空フォルダ GameObject は除外（書き出し時に自動生成されるもの）
            var folderObjects = new HashSet<GameObject>();
            foreach (var go in gameObjects)
            {
                if ((go.name == "Armature" || go.name == "Meshes" || go.name == "RigidBodies") &&
                    go.GetComponent<MeshFilter>() == null &&
                    go.GetComponent<SkinnedMeshRenderer>() == null)
                {
                    folderObjects.Add(go);
                }
            }

            // 剛体 GameObject（Collider 保有）を抽出（メッシュから除外）。順序保持。
            var rigidBodyGameObjects = new List<GameObject>();
            var rigidBodySet = new HashSet<GameObject>();
            foreach (var go in gameObjects)
            {
                if (boneGameObjects.Contains(go)) continue;
                var col = go.GetComponent<Collider>();
                if (col is SphereCollider || col is BoxCollider || col is CapsuleCollider)
                {
                    rigidBodyGameObjects.Add(go);
                    rigidBodySet.Add(go);
                }
            }

            var meshGameObjects = new List<GameObject>();
            foreach (var go in gameObjects)
                if (!boneGameObjects.Contains(go) && !folderObjects.Contains(go) && !rigidBodySet.Contains(go))
                    meshGameObjects.Add(go);

            var goToIndex = new Dictionary<GameObject, int>();
            for (int i = 0; i < meshGameObjects.Count; i++)
                goToIndex[meshGameObjects[i]] = boneStartIndex + i;

            // ── メッシュ MeshContext を追加 ─────────────────────────
            for (int i = 0; i < meshGameObjects.Count; i++)
            {
                var go = meshGameObjects[i];
                var meshContext = CreateMeshContextFromGameObject(go, goToIndex, materialToIndex, boneToIndex);
                meshContext.ParentModelContext = model;
                model.Add(meshContext);
            }

            // ── 剛体 / JOINT を追加（段階④-B：④-Aの逆変換）──────────
            AddRigidBodiesAndJoints(model, rigidBodyGameObjects, boneToIndex);

            // ── Depth 計算（タイプ別ツリー表示用）──────────────────
            var list = model.MeshContextList;
            for (int i = 0; i < list.Count; i++)
            {
                var ctx = list[i];
                if (ctx == null) continue;
                int depth = 0;
                int current = ctx.HierarchyParentIndex;
                int safety = 100;
                while (current >= 0 && current < list.Count && safety-- > 0)
                {
                    depth++;
                    current = list[current].HierarchyParentIndex;
                }
                ctx.Depth = depth;
            }

            // ── 名前末尾"+"のミラー検出 ─────────────────────────────
            if (detectNamedMirror)
                Poly_Ling.PMX.PMXImporter.DetectNamedMirrors(model.MeshContextList, boneStartIndex);

            Debug.Log($"[HierarchyImport] {boneTransforms.Count} bones + {meshGameObjects.Count} meshes + {rigidBodyGameObjects.Count} rigidbodies from '{rootGameObject.name}'");
            return model;
        }

        // ================================================================
        // 収集ヘルパー
        // ================================================================

        /// <summary>GameObject を深さ優先で収集（Hierarchy 表示順＝sibling index 順）。</summary>
        private static void CollectGameObjectsDepthFirst(GameObject go, List<GameObject> result)
        {
            if (go == null) return;
            result.Add(go);
            for (int i = 0; i < go.transform.childCount; i++)
                CollectGameObjectsDepthFirst(go.transform.GetChild(i).gameObject, result);
        }

        /// <summary>ボーン Transform を深さ優先で収集。</summary>
        private static void CollectBoneTransformsDepthFirst(Transform bone, List<Transform> result)
        {
            if (bone == null) return;
            result.Add(bone);
            foreach (Transform child in bone)
                CollectBoneTransformsDepthFirst(child, result);
        }

        // ================================================================
        // ボーン MeshContext 構築
        // ================================================================

        private static MeshContext CreateMeshContextFromBone(Transform bone, Dictionary<Transform, int> boneToIndex)
        {
            var meshObject = new MeshObject(bone.name)
            {
                Type = MeshType.Bone
            };

            var boneTransform = new BoneTransform
            {
                Position = bone.localPosition,
                Rotation = bone.localEulerAngles,
                Scale    = bone.localScale,
                UseLocalTransform = true
            };
            meshObject.BoneTransform = boneTransform;

            var meshContext = new MeshContext
            {
                MeshObject = meshObject,
                Type = MeshType.Bone,
                IsVisible = true
            };

            // 親インデックス（HierarchyParentIndex は ComputeWorldMatrices で使用）
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

            // ローカルトランスフォーム（MeshObject.BoneTransform へ反映）
            meshContext.BoneTransform.Position = bone.localPosition;
            meshContext.BoneTransform.Rotation = bone.localEulerAngles;
            meshContext.BoneTransform.Scale    = bone.localScale;
            meshContext.BoneTransform.UseLocalTransform = true;

            meshContext.OriginalPositions = new Vector3[0];
            meshContext.UnityMesh = new Mesh { name = bone.name };

            return meshContext;
        }

        // ================================================================
        // メッシュ MeshContext 構築（MeshFilter / SkinnedMeshRenderer 対応）
        // ================================================================

        private static MeshContext CreateMeshContextFromGameObject(
            GameObject go,
            Dictionary<GameObject, int> goToIndex,
            Dictionary<Material, int> materialToIndex,
            Dictionary<Transform, int> boneToIndex)
        {
            var meshContext = new MeshContext
            {
                MeshObject = new MeshObject(go.name)
            };

            // 親（HierarchyParentIndex）
            var parentTransform = go.transform.parent;
            meshContext.HierarchyParentIndex =
                (parentTransform != null && goToIndex.TryGetValue(parentTransform.gameObject, out int parentIndex))
                    ? parentIndex : -1;

            // ローカルトランスフォーム
            meshContext.BoneTransform.Position = go.transform.localPosition;
            meshContext.BoneTransform.Rotation = go.transform.localEulerAngles;
            meshContext.BoneTransform.Scale    = go.transform.localScale;

            bool isDefaultTransform =
                go.transform.localPosition == Vector3.zero &&
                go.transform.localEulerAngles == Vector3.zero &&
                go.transform.localScale == Vector3.one;
            meshContext.BoneTransform.UseLocalTransform = !isDefaultTransform;

            // メッシュ／マテリアル取得（MeshFilter 優先、無ければ SkinnedMeshRenderer）
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

                // smr.bones[i] → MeshContextList ボーン index の再マッピング
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

            // メッシュデータ変換（Unityメッシュ読取はブリッジ経由の FromUnityMesh）
            if (sourceMesh != null)
            {
                bool isSkinnedMesh = (boneIndexRemap != null && boneIndexRemap.Count > 0);
                meshContext.MeshObject.FromUnityMesh(sourceMesh, true, isSkinnedMesh);

                if (isSkinnedMesh)
                    RemapBoneWeightIndices(meshContext.MeshObject, boneIndexRemap);

                // サブメッシュ index → グローバル material index を Face へ反映
                if (sharedMats != null)
                {
                    for (int subMeshIdx = 0; subMeshIdx < sourceMesh.subMeshCount; subMeshIdx++)
                    {
                        Material mat = subMeshIdx < sharedMats.Length ? sharedMats[subMeshIdx] : null;
                        int globalMatIndex = 0;
                        if (mat != null && materialToIndex.TryGetValue(mat, out int idx))
                            globalMatIndex = idx;

                        // FromUnityMesh が作る Face は subMeshIndex を MaterialIndex に設定済み
                        foreach (var face in meshContext.MeshObject.Faces)
                            if (face.MaterialIndex == subMeshIdx)
                                face.MaterialIndex = globalMatIndex;
                    }
                }
            }

            meshContext.OriginalPositions = meshContext.MeshObject.Vertices.Select(v => v.Position).ToArray();
            meshContext.UnityMesh = meshContext.MeshObject.ToUnityMesh();
            meshContext.UnityMesh.name = go.name;

            return meshContext;
        }

        // ================================================================
        // BoneWeight インデックス再マッピング
        // ================================================================

        private static void RemapBoneWeightIndices(MeshObject meshObject, Dictionary<int, int> remap)
        {
            foreach (var vertex in meshObject.Vertices)
            {
                if (!vertex.HasBoneWeight) continue;

                var bw = vertex.BoneWeight.Value;

                // weight>0 のボーンのみリマップ、失敗時は 0（無効参照防止）
                var newBw = new BoneWeight
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
                vertex.BoneWeight = newBw;
            }
        }

        // ================================================================
        // マテリアル取得
        // ================================================================

        private static Material[] GetSharedMaterials(GameObject go)
        {
            if (go == null) return null;

            var meshRenderer = go.GetComponent<MeshRenderer>();
            if (meshRenderer != null && meshRenderer.sharedMaterials != null && meshRenderer.sharedMaterials.Length > 0)
                return meshRenderer.sharedMaterials;

            var skinnedMeshRenderer = go.GetComponent<SkinnedMeshRenderer>();
            if (skinnedMeshRenderer != null && skinnedMeshRenderer.sharedMaterials != null && skinnedMeshRenderer.sharedMaterials.Length > 0)
                return skinnedMeshRenderer.sharedMaterials;

            return null;
        }

        // ================================================================
        // 段階④-B：Unity 物理コンポーネント → ModelContext（④-Aの逆変換）
        // ================================================================

        private static void AddRigidBodiesAndJoints(
            ModelContext model,
            List<GameObject> rigidBodyGameObjects,
            Dictionary<Transform, int> boneToIndex)
        {
            if (model == null || rigidBodyGameObjects == null || rigidBodyGameObjects.Count == 0)
                return;

            // ── 剛体 ──（Type=RigidBody・頂点ゼロのメタデータコンテキスト）
            foreach (var go in rigidBodyGameObjects)
            {
                var rb = BuildRigidBodyData(go, boneToIndex);
                if (rb == null) continue;

                var meshObject = new MeshObject(go.name) { Type = MeshType.RigidBody };
                meshObject.RigidBodyData = rb;

                var ctx = new MeshContext
                {
                    MeshObject = meshObject,
                    Type = MeshType.RigidBody,
                    IsVisible = true
                };
                ctx.HierarchyParentIndex = -1;
                ctx.ParentIndex = -1;
                ctx.OriginalPositions = new Vector3[0];
                ctx.ParentModelContext = model;
                model.Add(ctx);
            }

            // ── JOINT ──（ConfigurableJoint は剛体GO上に付与されている）
            foreach (var go in rigidBodyGameObjects)
            {
                var joints = go.GetComponents<ConfigurableJoint>();
                foreach (var cj in joints)
                {
                    var jd = BuildJointData(go, cj);
                    if (jd == null) continue;

                    var name = string.IsNullOrEmpty(jd.NameEnglish) ? (go.name + "_joint") : jd.NameEnglish;
                    var meshObject = new MeshObject(name) { Type = MeshType.RigidBodyJoint };
                    meshObject.JointData = jd;

                    var ctx = new MeshContext
                    {
                        MeshObject = meshObject,
                        Type = MeshType.RigidBodyJoint,
                        IsVisible = true
                    };
                    ctx.HierarchyParentIndex = -1;
                    ctx.ParentIndex = -1;
                    ctx.OriginalPositions = new Vector3[0];
                    ctx.ParentModelContext = model;
                    model.Add(ctx);
                }
            }
        }

        /// <summary>
        /// Collider/Rigidbody → RigidBodyData（④-A 書き出しの逆変換）。
        /// Size 逆変換：Sphere=radius / Box=collider.size*0.5 / Capsule=(radius, height-radius*2)。
        /// Position/Rotation は working空間のまま（rotation は deg→rad）。
        /// </summary>
        private static RigidBodyData BuildRigidBodyData(GameObject go, Dictionary<Transform, int> boneToIndex)
        {
            var col = go.GetComponent<Collider>();
            if (col == null) return null;

            var rb = new RigidBodyData();
            rb.NameEnglish = go.name;

            if (col is SphereCollider sc)
            {
                rb.Shape = RigidBodyShape.Sphere;
                rb.Size = new Vector3(sc.radius, 0f, 0f);
            }
            else if (col is BoxCollider bc)
            {
                rb.Shape = RigidBodyShape.Box;
                rb.Size = bc.size * 0.5f;                       // ④-A: collider.size = Size*2
            }
            else if (col is CapsuleCollider cc)
            {
                rb.Shape = RigidBodyShape.Capsule;
                rb.Size = new Vector3(cc.radius, Mathf.Max(0f, cc.height - cc.radius * 2f), 0f); // ④-A: height = Size.y + radius*2
            }
            else
            {
                return null;
            }

            rb.Position = go.transform.position;
            rb.Rotation = go.transform.rotation.eulerAngles * Mathf.Deg2Rad;

            var body = go.GetComponent<Rigidbody>();
            if (body != null)
            {
                rb.Mass = body.mass;
                rb.LinearDamping = body.linearDamping;
                rb.AngularDamping = body.angularDamping;
                rb.PhysicsMode = body.isKinematic
                    ? RigidBodyPhysicsMode.FollowBone
                    : RigidBodyPhysicsMode.Physics;
            }

            // 関連ボーン（親がボーンなら）
            var parent = go.transform.parent;
            if (parent != null && boneToIndex != null && boneToIndex.TryGetValue(parent, out int boneIdx))
            {
                rb.RelatedBoneName = parent.name;
                rb.BoneIndex = boneIdx;
            }

            return rb;
        }

        /// <summary>
        /// ConfigurableJoint → JointData（④-A 書き出しの逆変換）。
        /// Position は host.TransformPoint(anchor)（④-A: anchor = host.InverseTransformPoint(Position) の逆）。
        /// 注意：JointType／Rotation／limits・spring は Unity コンポーネントへ完全には保持されないため
        ///       ベストエフォートの raw 復元（軸リマップは未対応）。プロジェクトファイルが無損失の正本。
        /// </summary>
        private static JointData BuildJointData(GameObject hostGo, ConfigurableJoint cj)
        {
            if (hostGo == null || cj == null) return null;

            var jd = new JointData();
            jd.NameEnglish = hostGo.name + "_joint";
            jd.BodyAName = hostGo.name;
            jd.BodyBName = (cj.connectedBody != null) ? cj.connectedBody.gameObject.name : "";

            jd.Position = hostGo.transform.TransformPoint(cj.anchor);
            jd.Rotation = hostGo.transform.rotation.eulerAngles * Mathf.Deg2Rad; // best-effort

            // limits / spring（raw・deg→rad。④-A のベストエフォート対称化の逆。軸リマップ未対応）
            jd.RotationMin = new Vector3(
                cj.lowAngularXLimit.limit * Mathf.Deg2Rad,
                -cj.angularYLimit.limit * Mathf.Deg2Rad,
                -cj.angularZLimit.limit * Mathf.Deg2Rad);
            jd.RotationMax = new Vector3(
                cj.highAngularXLimit.limit * Mathf.Deg2Rad,
                cj.angularYLimit.limit * Mathf.Deg2Rad,
                cj.angularZLimit.limit * Mathf.Deg2Rad);

            float lin = cj.linearLimit.limit;
            jd.TranslationMin = new Vector3(-lin, -lin, -lin);
            jd.TranslationMax = new Vector3(lin, lin, lin);

            jd.SpringTranslation = new Vector3(
                cj.linearLimitSpring.spring,
                cj.linearLimitSpring.spring,
                cj.linearLimitSpring.spring);
            jd.SpringRotation = new Vector3(
                cj.angularXLimitSpring.spring,
                cj.angularYZLimitSpring.spring,
                cj.angularYZLimitSpring.spring);

            return jd;
        }
    }
}
