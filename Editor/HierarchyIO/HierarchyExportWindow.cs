// Editor/HierarchyIO/HierarchyExportWindow.cs
// ============================================================
// 段階A：エクスポートエディタ拡張（プロジェクトファイル → Unityヒエラルキー）
// ============================================================
//
// 【処理の流れ】
//   1. プロジェクトファイル（フォルダ形式）を CsvModelSerializer.LoadModel で
//      ModelContext として復元する。
//   2. ModelContext.ComputeWorldMatrices() でボーン階層から WorldMatrix を構築。
//      （読込直後の ModelContext は WorldMatrix 未計算のため必須）
//   3. Export() で Unity GameObject 階層（Armature＋ボーン＋SkinnedMeshRenderer/
//      MeshFilter）を生成する。
//
// 【設計方針（再開時のガイダンス反映）】
//   - 「ファイル」＝プロジェクトファイル形式（CsvModelSerializer/CsvProjectSerializer）。
//     座標変換が入らないため非破壊（PMXのような ×Scale/FlipZ による破壊が起きない）。
//   - Unityメッシュ生成は MeshObject.ToUnityMesh()（内部でメッシュブリッジ）経由。
//     UnityEngine.Mesh を直接組み立てない。
//   - 本拡張は Editor アセンブリ（PolyLing.Editor）に閉じ、Runtime は無改変。
//     Runtime API（CsvModelSerializer / ModelContext / MeshObject）のみを呼ぶ。
//
// 【移植元】
//   旧 LiteHierarchyExportSubPanel.Export（現行 ModelContext API 準拠）。
//   UI を IMGUI の EditorWindow に置き換え、Export ロジックは現行 API のまま移植。
//
// 【出力構造】
//   <ModelName>                 ← ルート GameObject
//     Armature                  ← ボーン階層ルート（ボーンが存在する場合のみ）
//       <BoneName> ...          ← ボーン Transform ツリー（WorldMatrix で配置）
//     <MeshName> ...            ← SkinnedMeshRenderer または MeshFilter+MeshRenderer
//       スキニング: MeshObject.HasBoneWeight==true → SkinnedMeshRenderer
//                  （BindPose を bindposes に設定、ボーン Transform を bones に設定）
//       それ以外 → MeshFilter + MeshRenderer（WorldMatrix で配置）
//
// ============================================================

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.AssetIO;

namespace Poly_Ling.EditorIO
{
    /// <summary>
    /// プロジェクトファイル（フォルダ形式）を読み込み、Unityヒエラルキーへ書き出すエディタ拡張。
    /// </summary>
    public class HierarchyExportWindow : EditorWindow
    {
        // 入力
        private string _modelFolderPath = "";

        // オプション（旧 LiteHierarchyExportSubPanel 準拠）
        private bool _createArmature    = true;   // ボーン階層（Armature）を生成
        private bool _useBindpose       = true;   // MeshContext.BindPose を bindposes に使用
        private bool _exportVisibleOnly = false;  // 可視メッシュのみ書き出し
        private bool _exportMeshOnly    = false;  // ボーンを除外しメッシュのみ
        private bool _exportPhysics     = true;   // 剛体/JOINT を Unity 物理部品として出力
        private bool _saveAsPrefab      = false;  // シーンではなくプレファブとして保存（アセット化）
        private bool _buildAvatar       = false;  // プレファブと同時に Humanoid Avatar(.asset) を生成
        private bool _writeAttach       = true;   // IK 付帯を attach.csv でプレファブ同居出力

        // --- プレファブ保存時のみ有効な一時状態（Export→Attach 間で共有） ---
        private bool   _prefabExportActive = false;  // このExportがアセット化を伴うか
        private string _meshesDir = "";              // メッシュ .asset 出力先（Assets/...）
        private readonly HashSet<string> _usedMeshNames = new HashSet<string>(); // 同名衝突回避

        [MenuItem("PolyLing/IO/Hierarchy Export (Project File → Hierarchy)")]
        public static void Open()
        {
            GetWindow<HierarchyExportWindow>(true, "Hierarchy Export", true);
        }

        // ================================================================
        // UI（IMGUI）
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.LabelField("プロジェクトファイル → ヒエラルキー", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                _modelFolderPath = EditorGUILayout.TextField("モデルフォルダ", _modelFolderPath);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string sel = EditorUtility.OpenFolderPanel("モデルフォルダを選択", _modelFolderPath, "");
                    if (!string.IsNullOrEmpty(sel)) _modelFolderPath = sel;
                }
            }

            EditorGUILayout.Space();
            _createArmature    = EditorGUILayout.Toggle("Armatureを生成", _createArmature);
            _useBindpose       = EditorGUILayout.Toggle("BindPoseを使用", _useBindpose);
            _exportVisibleOnly = EditorGUILayout.Toggle("可視メッシュのみ", _exportVisibleOnly);
            _exportMeshOnly    = EditorGUILayout.Toggle("メッシュのみ（ボーン除外）", _exportMeshOnly);
            _exportPhysics     = EditorGUILayout.Toggle("剛体/JOINTを出力", _exportPhysics);
            _saveAsPrefab      = EditorGUILayout.Toggle("プレファブとして保存", _saveAsPrefab);
            if (_saveAsPrefab)
            {
                _buildAvatar = EditorGUILayout.Toggle("Avatar も生成", _buildAvatar);
                _writeAttach = EditorGUILayout.Toggle("IK付帯(attach.csv)も出力", _writeAttach);
                EditorGUILayout.HelpBox(
                    "Assets/PolyLing/<モデル名>/ にメッシュ/マテリアルを共有アセット化し、" +
                    "同名プレファブへ上書き保存します（繰り返しても増えません）。" +
                    (_buildAvatar ? "\nHumanoid 割当から Avatar(.asset) も同時生成します。" : ""),
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_modelFolderPath)))
            {
                string label = _saveAsPrefab ? "ロードしてプレファブに保存" : "ロードしてヒエラルキーに書き出し";
                if (GUILayout.Button(label, GUILayout.Height(28)))
                {
                    LoadAndExport();
                }
            }
        }

        // ================================================================
        // ロード → 書き出し
        // ================================================================

        private void LoadAndExport()
        {
            if (!Directory.Exists(_modelFolderPath))
            {
                EditorUtility.DisplayDialog("エラー", "フォルダが存在しません:\n" + _modelFolderPath, "OK");
                return;
            }

            // プロジェクトファイル（フォルダ形式）→ ModelContext
            // out パラメータ（EditorState / WorkPlane / 追加エントリ）は本処理では不要のため破棄。
            ModelContext model = CsvModelSerializer.LoadModel(_modelFolderPath, out _, out _, out _);
            if (model == null)
            {
                EditorUtility.DisplayDialog("エラー", "モデルの読み込みに失敗しました（model.csv 不在など）。", "OK");
                return;
            }

            // 読込直後の ModelContext はボーンの WorldMatrix が未計算。
            // BoneTransform の親子関係から WorldMatrix を構築してから書き出す。
            model.ComputeWorldMatrices();

            if (_saveAsPrefab)
            {
                ExportAsPrefab(model);
                return;
            }

            var root = Export(model);
            if (root != null)
            {
                UnityEditor.Selection.activeGameObject = root;
                EditorGUIUtility.PingObject(root);
            }
        }

        // ================================================================
        // プレファブ保存（決定論パス・上書き・アセット化）
        // ================================================================

        private void ExportAsPrefab(ModelContext model)
        {
            string modelName = SanitizeName(model.Name ?? "Model");
            string baseDir      = $"Assets/PolyLing/{modelName}";
            string materialsDir = $"{baseDir}/materials";
            string meshesDir    = $"{baseDir}/meshes";
            string prefabPath   = $"{baseDir}/{modelName}.prefab";

            // フォルダ作成（ModelContext.SaveOnMemoryMaterialsAsAssets と同パターン）
            Directory.CreateDirectory(materialsDir);
            Directory.CreateDirectory(meshesDir);
            AssetDatabase.Refresh();

            // マテリアルを共有アセット化（→ matRef.Material が共有アセットになり BuildMaterials が参照）
            int matCount = model.SaveOnMemoryMaterialsAsAssets(materialsDir);

            // メッシュのアセット化は Attach 系で行う（一時状態を設定）
            _prefabExportActive = true;
            _meshesDir = meshesDir;
            _usedMeshNames.Clear();

            GameObject root = null;
            try
            {
                root = Export(model);
                if (root == null)
                {
                    EditorUtility.DisplayDialog("エラー", "ヒエラルキー生成に失敗しました。", "OK");
                    return;
                }

                // 同名プレファブへ上書き保存（繰り返しても増えない）
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.SaveAssets();

                // Avatar も生成（Humanoid 割当 + 可動域を model から直接）
                if (_buildAvatar)
                {
                    BuildAvatarMapsFromModel(model, out var avMap, out var avLimits);
                    if (avMap.Count == 0)
                    {
                        Debug.LogWarning("[HierarchyExport] Humanoid 割当が無いため Avatar 生成をスキップ。");
                    }
                    else
                    {
                        string avatarPath = $"{baseDir}/{modelName}.asset";
                        AvatarBuildCore.BuildAndSaveAvatar(root, avMap, avLimits, avatarPath,
                            m => Debug.Log("[HierarchyExport] " + m));
                    }
                }

                UnityEditor.Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log($"[HierarchyExport] プレファブ保存: {prefabPath}（材料アセット {matCount}）");

                // IK 付帯を attach.csv でプレファブ同居出力（案X: Humanoid/HumanLimit は Avatar が正）
                if (_writeAttach)
                {
                    AttachSidecarCsv.Write(model, $"{baseDir}/attach.csv");
                    AssetDatabase.Refresh();
                }
            }
            finally
            {
                _prefabExportActive = false;
                _meshesDir = "";
                // シーン上の一時ルートは破棄（プレファブが成果物）
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ファイル名に使えない文字を '_' に置換
        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Model";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        // メッシュ .asset のパス（同一 export 内の同名衝突は _n を付与）
        private string ResolveMeshAssetPath(string meshName)
        {
            string baseName = SanitizeName(string.IsNullOrEmpty(meshName) ? "Mesh" : meshName);
            string name = baseName;
            int n = 1;
            while (!_usedMeshNames.Add(name))
            {
                name = $"{baseName}_{n}";
                n++;
            }
            return $"{_meshesDir}/{name}.asset";
        }

        // model の Humanoid 割当・可動域から Avatar 用 map/limits を構築。
        //   map    : humanName(HumanTrait.BoneName 形式) → ボーン名
        //   limits : humanName → HumanLimit（度・custom のみ）
        //   ※ model の humanName は HumanBodyBones 列挙形（"LeftUpperArm" 等）なので
        //     HumanTrait.BoneName 形式（指はスペース付き）へ正規化する。
        private static void BuildAvatarMapsFromModel(
            ModelContext model,
            out Dictionary<string, string> map,
            out Dictionary<string, HumanLimit> limits)
        {
            map = new Dictionary<string, string>();
            limits = new Dictionary<string, HumanLimit>();

            var mapping = model?.HumanoidMapping;
            if (mapping == null || mapping.IsEmpty) return;

            foreach (var kv in mapping.BoneIndexMap)
            {
                string traitName = ToHumanTraitName(kv.Key);
                var ctx = model.GetMeshContext(kv.Value);
                if (ctx == null || string.IsNullOrEmpty(ctx.Name)) continue;

                map[traitName] = ctx.Name;

                var hl = ctx.MeshObject?.HumanLimit;
                if (hl != null && !hl.UseDefaultValues)
                {
                    limits[traitName] = new HumanLimit
                    {
                        useDefaultValues = false,
                        min    = hl.Min * Mathf.Rad2Deg,
                        max    = hl.Max * Mathf.Rad2Deg,
                        center = hl.Center * Mathf.Rad2Deg,
                        axisLength = hl.AxisLength
                    };
                }
            }
        }

        // HumanBodyBones 列挙形 → HumanTrait.BoneName 形式（解釈できなければそのまま）
        private static string ToHumanTraitName(string enumName)
        {
            if (!string.IsNullOrEmpty(enumName) &&
                System.Enum.TryParse<HumanBodyBones>(enumName, out var hbb))
            {
                int i = (int)hbb;
                if (i >= 0 && i < HumanTrait.BoneName.Length)
                    return HumanTrait.BoneName[i];
            }
            return enumName;
        }

        // ================================================================
        // ModelContext → Unityヒエラルキー（移植: LiteHierarchyExportSubPanel.Export）
        // ================================================================

        /// <summary>ModelContext を Unity ヒエラルキーに書き出し、ルート GameObject を返す。</summary>
        private GameObject Export(ModelContext model)
        {
            Undo.SetCurrentGroupName("PolyLing: Export to Hierarchy");
            int undoGroup = Undo.GetCurrentGroup();

            // ── ルート ────────────────────────────────────────────────
            var rootGo = new GameObject(string.IsNullOrEmpty(model.Name) ? "Model" : model.Name);
            Undo.RegisterCreatedObjectUndo(rootGo, "Create Root");

            // ── ボーン Transform ツリーを構築 ─────────────────────────
            // boneTransformMap[ctxIndex] = MeshContextList インデックス ctxIndex の Transform
            Transform armatureRoot = null;
            var boneTransformMap = new Dictionary<int, Transform>();

            if (_createArmature && !_exportMeshOnly)
            {
                bool hasBones = false;
                for (int i = 0; i < model.MeshContextCount; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc?.Type == MeshType.Bone) { hasBones = true; break; }
                }

                if (hasBones)
                {
                    var armatureGo = new GameObject("Armature");
                    Undo.RegisterCreatedObjectUndo(armatureGo, "Create Armature");
                    armatureGo.transform.SetParent(rootGo.transform, worldPositionStays: false);
                    armatureRoot = armatureGo.transform;

                    // 1パス目: 全ボーンの Transform を生成
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;

                        var boneGo = new GameObject(mc.Name ?? $"Bone_{i}");
                        Undo.RegisterCreatedObjectUndo(boneGo, "Create Bone");
                        boneTransformMap[i] = boneGo.transform;
                    }

                    // 2パス目: 親子関係設定（HierarchyParentIndex）
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;

                        var boneTf = boneTransformMap[i];
                        int parentIdx = mc.HierarchyParentIndex;

                        if (parentIdx >= 0 && boneTransformMap.TryGetValue(parentIdx, out var parentTf))
                            boneTf.SetParent(parentTf, worldPositionStays: false);
                        else
                            boneTf.SetParent(armatureRoot, worldPositionStays: false);
                    }

                    // 3パス目: ワールド位置設定（親子確定後に WorldMatrix で配置）
                    for (int i = 0; i < model.MeshContextCount; i++)
                    {
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;

                        var boneTf = boneTransformMap[i];
                        var wm = mc.WorldMatrix;
                        boneTf.position   = new Vector3(wm.m03, wm.m13, wm.m23);
                        boneTf.rotation   = wm.rotation;
                        boneTf.localScale = Vector3.one;
                    }
                }
            }

            // ── メッシュ書き出し ──────────────────────────────────────
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                if (mc.Type != MeshType.Mesh) continue;   // ボーン・モーフ・剛体・JOINT は除外
                if (mc.MeshObject == null) continue;
                if (_exportVisibleOnly && !mc.IsVisible) continue;

                // Unityメッシュはブリッジ経由（MeshObject.ToUnityMesh）で取得。
                var unityMesh = mc.UnityMesh != null ? mc.UnityMesh : mc.MeshObject.ToUnityMesh();
                if (unityMesh == null) continue;

                var meshGo = new GameObject(mc.Name ?? $"Mesh_{i}");
                Undo.RegisterCreatedObjectUndo(meshGo, "Create Mesh GameObject");
                meshGo.transform.SetParent(rootGo.transform, worldPositionStays: false);

                if (mc.MeshObject.HasBoneWeight && boneTransformMap.Count > 0)
                    AttachSkinnedMesh(meshGo, mc, unityMesh, model, boneTransformMap, armatureRoot);
                else
                    AttachStaticMesh(meshGo, mc, unityMesh, model);
            }

            // ── 剛体/JOINT 書き出し ──────────────────────────────────
            if (_exportPhysics && !_exportMeshOnly)
                ExportPhysics(model, rootGo, boneTransformMap);

            Undo.CollapseUndoOperations(undoGroup);
            return rootGo;
        }

        // ================================================================
        // SkinnedMeshRenderer アタッチ
        // ================================================================

        private void AttachSkinnedMesh(
            GameObject go,
            MeshContext mc,
            Mesh unityMesh,
            ModelContext model,
            Dictionary<int, Transform> boneTransformMap,
            Transform armatureRoot)
        {
            var smr = Undo.AddComponent<SkinnedMeshRenderer>(go);

            // BoneWeight の bone0-3 インデックスは MeshContextList 内のボーン index に対応。
            var boneList = new List<Transform>();
            var bindposes = new List<Matrix4x4>();

            for (int bi = 0; bi < model.MeshContextCount; bi++)
            {
                var bmc = model.GetMeshContext(bi);
                if (bmc == null || bmc.Type != MeshType.Bone) continue;
                if (!boneTransformMap.TryGetValue(bi, out var boneTf)) continue;

                boneList.Add(boneTf);
                bindposes.Add(_useBindpose ? bmc.BindPose : boneTf.worldToLocalMatrix);
            }

            // bindposes はメッシュ複製側に設定（共有メッシュを汚さない）。
            var mesh = UnityEngine.Object.Instantiate(unityMesh);
            mesh.name = unityMesh.name;
            mesh.bindposes = bindposes.ToArray();

            // プレファブ保存時は共有アセット化（決定論パス・上書き）。
            if (_prefabExportActive)
                mesh = MeshAssetUtil.SaveDeterministic(mesh, ResolveMeshAssetPath(mesh.name));

            smr.sharedMesh = mesh;
            smr.bones      = boneList.ToArray();
            smr.rootBone   = armatureRoot;

            smr.sharedMaterials = BuildMaterials(mc, model);
        }

        // ================================================================
        // 静的メッシュ（MeshFilter + MeshRenderer）アタッチ
        // ================================================================

        private void AttachStaticMesh(GameObject go, MeshContext mc, Mesh unityMesh, ModelContext model)
        {
            var mf = Undo.AddComponent<MeshFilter>(go);
            var mr = Undo.AddComponent<MeshRenderer>(go);

            // プレファブ保存時は共有アセット化（決定論パス・上書き）。
            var staticMesh = _prefabExportActive
                ? MeshAssetUtil.SaveDeterministic(unityMesh, ResolveMeshAssetPath(unityMesh.name))
                : unityMesh;
            mf.sharedMesh = staticMesh;

            var wm = mc.WorldMatrix;
            go.transform.position   = new Vector3(wm.m03, wm.m13, wm.m23);
            go.transform.rotation   = wm.rotation;
            go.transform.localScale = wm.lossyScale;

            mr.sharedMaterials = BuildMaterials(mc, model);
        }

        // ================================================================
        // マテリアル配列生成
        // ================================================================

        private static Material[] BuildMaterials(MeshContext mc, ModelContext model)
        {
            int subMeshCount = Mathf.Max(1, mc.MeshObject?.SubMeshCount ?? 1);
            var mats = new Material[subMeshCount];

            var matRefs = model?.MaterialReferences;
            var defaultMat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");

            for (int m = 0; m < subMeshCount; m++)
            {
                Material mat = null;
                if (matRefs != null && m < matRefs.Count)
                    mat = matRefs[m]?.Material;
                mats[m] = mat != null ? mat : defaultMat;
            }
            return mats;
        }

        // ================================================================
        // 剛体 / JOINT 書き出し（段階④）
        // ================================================================
        //
        // 方針：Unityネイティブ部品へマップ（剛体→Rigidbody＋Collider、JOINT→ConfigurableJoint）。
        //   Group / CollisionMask / PhysicsMode / NameEnglish / JointType 等の
        //   Unity非対応パラメータはヒエラルキーには出さない（非破壊の正本はプロジェクトファイル側）。
        //
        // 座標：RigidBodyData / JointData の Position/Rotation/Size は PMXImport 時に
        //   working空間へ変換済み（頂点・ボーンと同一空間）。よって追加変換は不要で、
        //   ボーンと同様に world 座標へそのまま適用する（Rotation のみ rad→deg）。
        //
        private void ExportPhysics(ModelContext model, GameObject rootGo, Dictionary<int, Transform> boneTransformMap)
        {
            // ボーン名 → Transform（関連ボーン解決用。先勝ち）
            var boneByName = new Dictionary<string, Transform>();
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.Bone) continue;
                if (!boneTransformMap.TryGetValue(i, out var tf)) continue;
                if (!string.IsNullOrEmpty(mc.Name) && !boneByName.ContainsKey(mc.Name))
                    boneByName[mc.Name] = tf;
            }

            // ── 剛体 ──────────────────────────────────────────────────
            GameObject rigidFolder = null;
            var rigidbodyByName = new Dictionary<string, Rigidbody>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.RigidBody) continue;
                var rb = mc.MeshObject?.RigidBodyData;
                if (rb == null) continue;

                var go = new GameObject(mc.Name ?? $"RigidBody_{i}");
                Undo.RegisterCreatedObjectUndo(go, "Create RigidBody");

                // 親：関連ボーン配下（解決時）。未解決は root 直下 "RigidBodies" フォルダ
                Transform parent;
                if (!string.IsNullOrEmpty(rb.RelatedBoneName) && boneByName.TryGetValue(rb.RelatedBoneName, out var boneTf))
                {
                    parent = boneTf;
                }
                else
                {
                    if (rigidFolder == null)
                    {
                        rigidFolder = new GameObject("RigidBodies");
                        Undo.RegisterCreatedObjectUndo(rigidFolder, "Create RigidBodies");
                        rigidFolder.transform.SetParent(rootGo.transform, worldPositionStays: false);
                    }
                    parent = rigidFolder.transform;
                }
                go.transform.SetParent(parent, worldPositionStays: false);

                // working空間の値を world 座標として適用
                go.transform.position = rb.Position;
                go.transform.rotation = Quaternion.Euler(rb.Rotation * Mathf.Rad2Deg);

                AttachCollider(go, rb);

                var body = Undo.AddComponent<Rigidbody>(go);
                body.mass           = rb.Mass;
                body.linearDamping  = rb.LinearDamping;
                body.angularDamping = rb.AngularDamping;
                body.isKinematic    = (rb.PhysicsMode == RigidBodyPhysicsMode.FollowBone);
                // 反発/摩擦(Restitution/Friction)は v1 では PhysicsMaterial 未割当（必要なら別途）。

                if (!string.IsNullOrEmpty(mc.Name) && !rigidbodyByName.ContainsKey(mc.Name))
                    rigidbodyByName[mc.Name] = body;
            }

            // ── JOINT ─────────────────────────────────────────────────
            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.RigidBodyJoint) continue;
                var jd = mc.MeshObject?.JointData;
                if (jd == null) continue;

                Rigidbody bodyA = null, bodyB = null;
                if (!string.IsNullOrEmpty(jd.BodyAName)) rigidbodyByName.TryGetValue(jd.BodyAName, out bodyA);
                if (!string.IsNullOrEmpty(jd.BodyBName)) rigidbodyByName.TryGetValue(jd.BodyBName, out bodyB);

                // ConfigurableJoint は Rigidbody を持つ GO に付与。基準＝剛体A（無ければ剛体B）。
                Rigidbody host      = bodyA != null ? bodyA : bodyB;
                Rigidbody connected = bodyA != null ? bodyB : bodyA;
                if (host == null)
                {
                    Debug.LogWarning($"[ExportPhysics] JOINT '{mc.Name}' の接続剛体が見つからずスキップ。");
                    continue;
                }

                var joint = Undo.AddComponent<ConfigurableJoint>(host.gameObject);
                joint.connectedBody = connected; // null可（ワールド固定）
                joint.autoConfigureConnectedAnchor = false;
                joint.anchor = host.transform.InverseTransformPoint(jd.Position);
                joint.connectedAnchor = connected != null
                    ? connected.transform.InverseTransformPoint(jd.Position)
                    : jd.Position;

                joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Limited;
                joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Limited;

                // ============================================================
                // 【注記：座標軸リマップ未実施（段階②③からの保留）】
                //   TranslationMin/Max・RotationMin/Max・SpringTranslation/Rotation は raw値。
                //   PMX（左手・モデル-Z向き）軸と Unity ConfigurableJoint 軸の対応リマップは未実施。
                //   加えて ConfigurableJoint の並進リミットは軸別 min/max ではなく単一の対称リミット
                //   のため、PMX の軸別 min/max を厳密表現できない。下記は対称近似のベストエフォート。
                //   厳密な物理整合が必要なら別途、軸リマップ対応が要る。
                //   （※非破壊の正本はプロジェクトファイル側に保持済み。本出力は使用/可視化用途）
                // ============================================================
                joint.lowAngularXLimit  = new SoftJointLimit { limit = jd.RotationMin.x * Mathf.Rad2Deg };
                joint.highAngularXLimit = new SoftJointLimit { limit = jd.RotationMax.x * Mathf.Rad2Deg };
                joint.angularYLimit = new SoftJointLimit
                {
                    limit = Mathf.Max(Mathf.Abs(jd.RotationMin.y), Mathf.Abs(jd.RotationMax.y)) * Mathf.Rad2Deg
                };
                joint.angularZLimit = new SoftJointLimit
                {
                    limit = Mathf.Max(Mathf.Abs(jd.RotationMin.z), Mathf.Abs(jd.RotationMax.z)) * Mathf.Rad2Deg
                };

                float linMax = Mathf.Max(
                    Mathf.Max(Mathf.Abs(jd.TranslationMin.x), Mathf.Abs(jd.TranslationMax.x)),
                    Mathf.Max(
                        Mathf.Max(Mathf.Abs(jd.TranslationMin.y), Mathf.Abs(jd.TranslationMax.y)),
                        Mathf.Max(Mathf.Abs(jd.TranslationMin.z), Mathf.Abs(jd.TranslationMax.z))));
                joint.linearLimit = new SoftJointLimit { limit = linMax };

                joint.linearLimitSpring = new SoftJointLimitSpring
                {
                    spring = Mathf.Max(jd.SpringTranslation.x, Mathf.Max(jd.SpringTranslation.y, jd.SpringTranslation.z))
                };
                joint.angularXLimitSpring  = new SoftJointLimitSpring { spring = jd.SpringRotation.x };
                joint.angularYZLimitSpring = new SoftJointLimitSpring { spring = Mathf.Max(jd.SpringRotation.y, jd.SpringRotation.z) };
            }
        }

        // ================================================================
        // Collider 付与（形状別）
        // ================================================================
        //
        // 【PMX サイズ意味の前提】
        //   球    : Size.x = 半径
        //   箱    : Size   = 半幅（half-extent）
        //   カプセル: Size.x = 半径, Size.y = 高さ（円筒部長）
        // 【Unity 換算】
        //   BoxCollider.size      = 全幅 = 半幅 × 2
        //   CapsuleCollider.height = 全高 = 円筒部長 + 半径 × 2
        //
        private static void AttachCollider(GameObject go, RigidBodyData rb)
        {
            switch (rb.Shape)
            {
                case RigidBodyShape.Sphere:
                {
                    var c = Undo.AddComponent<SphereCollider>(go);
                    c.radius = rb.Size.x;
                    break;
                }
                case RigidBodyShape.Box:
                {
                    var c = Undo.AddComponent<BoxCollider>(go);
                    c.size = rb.Size * 2f;
                    break;
                }
                case RigidBodyShape.Capsule:
                {
                    var c = Undo.AddComponent<CapsuleCollider>(go);
                    c.radius    = rb.Size.x;
                    c.height    = rb.Size.y + rb.Size.x * 2f;
                    c.direction = 1; // Y軸
                    break;
                }
            }
        }
    }
}
