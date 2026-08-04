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
using Poly_Ling.Ops;

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
        private bool _exportVisibleOnly = true;   // 可視メッシュのみ書き出し
        private bool _exportMeshOnly    = false;  // ボーンを除外しメッシュのみ
        private bool _exportPhysics     = true;   // 剛体/JOINT を Unity 物理部品として出力
        private bool _saveAsPrefab      = true;   // シーンではなくプレファブとして保存（アセット化）
        private bool _buildAvatar       = true;   // プレファブと同時に Humanoid Avatar(.asset) を生成
        private bool _writeAttach       = true;   // IK 付帯を attach.csv でプレファブ同居出力
        private bool _attachAnimator    = true;   // 生成した Avatar を Animator に割り当てる（プレファブ時）
        private bool _sceneAnimator     = true;   // シーン出力時にルートへ空の Animator を付与

        // --- プレファブ／アセットの出力ルート（Assets/ 以下・EditorPrefs で保持） ---
        private const string DefaultPrefabOutputRoot  = "Assets/PolyLing";
        private const string PrefsKeyPrefabOutputRoot = "PolyLing.HierarchyExport.PrefabOutputRoot";
        private string _prefabOutputRoot = DefaultPrefabOutputRoot;

        // --- Animator Controller（任意・EditorPrefs にアセットパスで保持） ---
        //   指定されていれば Animator の runtimeAnimatorController に設定する。
        //   Avatar 生成の成否や _buildAvatar のオン／オフとは独立。
        private const string PrefsKeyAnimatorController = "PolyLing.HierarchyExport.AnimatorController";
        private RuntimeAnimatorController _animatorController = null;

        // --- プレファブ保存時のみ有効な一時状態（Export→Attach 間で共有） ---
        private bool   _prefabExportActive = false;  // このExportがアセット化を伴うか
        private string _meshesDir = "";              // メッシュ .asset 出力先（Assets/...）
        private readonly HashSet<string> _usedMeshNames = new HashSet<string>(); // 同名衝突回避

        // --- 出力パスに挟むプロジェクト名（空なら挟まない） ---
        //   プロジェクトが特定できた場合に Assets/PolyLing/<プロジェクト名>/<モデル名> とする。
        //   別プロジェクトに同名モデルがあるときの衝突を避けるため。
        private string _prefabProjectFolder = "";

        [MenuItem("PolyLing/IO/Hierarchy Export (Project File → Hierarchy)")]
        public static void Open()
        {
            GetWindow<HierarchyExportWindow>(true, "Hierarchy Export", true);
        }

        private void OnEnable()
        {
            _prefabOutputRoot = NormalizeOutputRoot(
                EditorPrefs.GetString(PrefsKeyPrefabOutputRoot, DefaultPrefabOutputRoot));

            string acPath = EditorPrefs.GetString(PrefsKeyAnimatorController, "");
            _animatorController = string.IsNullOrEmpty(acPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(acPath);
        }

        private void OnDisable()
        {
            SaveOutputRootPref();
            SaveAnimatorControllerPref();
        }

        // ================================================================
        // UI（IMGUI）
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.LabelField("プロジェクトファイル → ヒエラルキー", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "モデルフォルダ（model.csv のあるフォルダ）を指定すると単体、" +
                "その親フォルダを指定すると配下のモデルを全て書き出します。",
                MessageType.None);
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
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    _prefabOutputRoot = EditorGUILayout.TextField("出力先フォルダ", _prefabOutputRoot);
                    if (EditorGUI.EndChangeCheck()) SaveOutputRootPref();

                    if (GUILayout.Button("...", GUILayout.Width(30))) BrowseOutputRoot();
                }

                _buildAvatar = EditorGUILayout.Toggle("Avatar も生成", _buildAvatar);
                if (_buildAvatar)
                    _attachAnimator = EditorGUILayout.Toggle("Animator を付与して割当", _attachAnimator);
                _writeAttach = EditorGUILayout.Toggle("IK付帯(attach.csv)も出力", _writeAttach);
                EditorGUILayout.HelpBox(
                    NormalizeOutputRoot(_prefabOutputRoot) +
                    "/<モデル名>/ にメッシュ/マテリアルを共有アセット化し、" +
                    "同名プレファブへ上書き保存します（繰り返しても増えません）。" +
                    (_buildAvatar ? "\nHumanoid 割当から Avatar(.asset) も同時生成します。" : "") +
                    (_buildAvatar && _attachAnimator
                        ? "\n生成した Avatar を Animator に割り当ててからプレファブ化します。" : ""),
                    MessageType.Info);
            }
            else
            {
                _sceneAnimator = EditorGUILayout.Toggle("空の Animator を付与", _sceneAnimator);
                EditorGUILayout.HelpBox(
                    "シーンに書き出します。アセット・ファイルは一切生成しません。" +
                    (_sceneAnimator ? "\nルートに Animator を付与します（Avatar は未設定）。" : ""),
                    MessageType.Info);
            }

            // Animator Controller（任意・プレファブ／シーン共通）
            EditorGUI.BeginChangeCheck();
            _animatorController = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                "Animator Controller", _animatorController,
                typeof(RuntimeAnimatorController), allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck()) SaveAnimatorControllerPref();

            if (_animatorController != null)
            {
                EditorGUILayout.HelpBox(
                    "ルートの Animator に上記コントローラを設定します（未指定なら設定しません）。",
                    MessageType.None);
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

            // 指定フォルダ自身がモデルなら単体、そうでなければ配下のモデルを全て書き出す。
            if (!File.Exists(Path.Combine(_modelFolderPath, "model.csv")))
            {
                ExportAllModelsUnder(_modelFolderPath);
                return;
            }

            // 単体のモデルフォルダ指定でも、親がプロジェクトフォルダなら名前を挟む。
            _prefabProjectFolder = ResolveProjectFolderName(
                Path.GetDirectoryName(Path.GetFullPath(_modelFolderPath)));
            try
            {
                ExportSingleModel(_modelFolderPath, showDialogOnError: true);
            }
            finally
            {
                _prefabProjectFolder = "";
            }
        }

        /// <summary>
        /// フォルダ配下（1階層下）のモデルフォルダを全て書き出す。
        /// モデルフォルダの判定は model.csv の有無。
        /// </summary>
        private void ExportAllModelsUnder(string projectFolder)
        {
            var modelFolders = new List<string>();
            foreach (string dir in Directory.GetDirectories(projectFolder))
                if (File.Exists(Path.Combine(dir, "model.csv"))) modelFolders.Add(dir);

            if (modelFolders.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "エラー",
                    "モデルが見つかりません（フォルダ自身にも配下にも model.csv がありません）:\n" + projectFolder,
                    "OK");
                return;
            }

            int ok = 0;
            var failed = new List<string>();

            _prefabProjectFolder = ResolveProjectFolderName(projectFolder);
            try
            {
                foreach (string dir in modelFolders)
                {
                    if (ExportSingleModel(dir, showDialogOnError: false)) ok++;
                    else failed.Add(Path.GetFileName(dir));
                }
            }
            finally
            {
                _prefabProjectFolder = "";
            }

            string msg = $"{modelFolders.Count} 件中 {ok} 件を書き出しました。";
            if (failed.Count > 0) msg += "\n失敗: " + string.Join(", ", failed);

            Debug.Log("[HierarchyExport] " + msg);
            EditorUtility.DisplayDialog("一括エクスポート", msg, "OK");
        }

        /// <summary>モデルフォルダ1件を書き出す。成功したら true。</summary>
        private bool ExportSingleModel(string modelFolder, bool showDialogOnError)
        {
            // プロジェクトファイル（フォルダ形式）→ ModelContext
            // out パラメータ（EditorState / WorkPlane / 追加エントリ）は本処理では不要のため破棄。
            ModelContext model = CsvModelSerializer.LoadModel(modelFolder, out _, out _, out _);
            if (model == null)
            {
                string m = "モデルの読み込みに失敗しました（model.csv 不在など）:\n" + modelFolder;
                if (showDialogOnError) EditorUtility.DisplayDialog("エラー", m, "OK");
                else Debug.LogWarning("[HierarchyExport] " + m);
                return false;
            }

            // 読込直後の ModelContext はボーンの WorldMatrix が未計算。
            // BoneTransform の親子関係から WorldMatrix を構築してから書き出す。
            model.ComputeWorldMatrices();

            if (_saveAsPrefab)
            {
                ExportAsPrefab(model);
                return true;
            }

            var root = Export(model);
            if (root != null)
            {
                // シーン出力ではアセットを一切作らないため Avatar は生成せず、
                // 空の Animator（avatar 未設定）のみを付与する。
                // Animator Controller が指定されている場合は、トグルに関わらず確保して設定する。
                if (_sceneAnimator || _animatorController != null)
                {
                    var animator = root.GetComponent<Animator>();
                    if (animator == null) animator = Undo.AddComponent<Animator>(root);
                    if (_animatorController != null)
                        animator.runtimeAnimatorController = _animatorController;
                }

                UnityEditor.Selection.activeGameObject = root;
                EditorGUIUtility.PingObject(root);
            }

            return root != null;
        }

        // ================================================================
        // プレファブ保存（決定論パス・上書き・アセット化）
        // ================================================================

        private void ExportAsPrefab(ModelContext model)
        {
            string modelName = SanitizeName(model.Name ?? "Model");
            string outputRoot   = NormalizeOutputRoot(_prefabOutputRoot);
            // プロジェクトが特定できていれば1階層挟む
            string projectDir   = string.IsNullOrEmpty(_prefabProjectFolder)
                                    ? outputRoot
                                    : $"{outputRoot}/{_prefabProjectFolder}";
            string baseDir      = $"{projectDir}/{modelName}";
            string materialsDir = $"{baseDir}/materials";
            string meshesDir    = $"{baseDir}/meshes";
            string prefabPath   = $"{baseDir}/{modelName}.prefab";

            string texturesDir = $"{baseDir}/textures";

            // フォルダ作成（ModelContext.SaveOnMemoryMaterialsAsAssets と同パターン）
            Directory.CreateDirectory(materialsDir);
            Directory.CreateDirectory(meshesDir);
            Directory.CreateDirectory(texturesDir);
            AssetDatabase.Refresh();

            // テクスチャをモデル配下へ複製してアセット化する。
            //   プロジェクトファイルのテクスチャは PNG から生成したメモリ上の Texture2D で
            //   アセットではないため、先にファイル化しないと .mat に参照が保存されず
            //   _BaseMap 等が空になる。
            int texCount = ExportTexturesAsAssets(model, texturesDir);

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

                // Avatar も生成（Humanoid 割当 + 可動域を model から直接）。
                //   プレファブ保存より前に実行する。保存後だと Animator への割当結果が
                //   プレファブに含まれない。
                if (_buildAvatar)
                {
                    BuildAvatarMapsFromModel(model, out var avMap, out var avLimits);
                    if (avMap.Count == 0)
                    {
                        Debug.LogWarning("[HierarchyExport] Humanoid 割当が無いため Avatar 生成をスキップ。");
                    }
                    else if (!ValidateHumanoidBoneNames(root, avMap, out string dupNames))
                    {
                        Debug.LogWarning(
                            "[HierarchyExport] Humanoid 割当先のボーン名が階層内で重複しているため " +
                            "Avatar 生成をスキップ: " + dupNames);
                    }
                    else
                    {
                        string avatarPath = $"{baseDir}/{modelName}.asset";
                        var avatar = AvatarBuildCore.BuildAndSaveAvatar(root, avMap, avLimits, avatarPath,
                            m => Debug.Log("[HierarchyExport] " + m));

                        if (_attachAnimator)
                        {
                            if (avatar != null)
                            {
                                // root はプレファブ化後に破棄する一時オブジェクトのため Undo 登録しない。
                                var animator = root.GetComponent<Animator>();
                                if (animator == null) animator = root.AddComponent<Animator>();
                                animator.avatar = avatar;
                            }
                            else
                            {
                                Debug.LogWarning("[HierarchyExport] Avatar 生成に失敗したため Animator を付与しない。");
                            }
                        }
                    }
                }

                // Animator Controller の割当（指定時のみ）。
                //   Avatar 生成の成否や _buildAvatar のオン／オフに依存させない。
                //   root はプレファブ化後に破棄する一時オブジェクトのため Undo 登録しない。
                if (_animatorController != null)
                {
                    var animator = root.GetComponent<Animator>();
                    if (animator == null) animator = root.AddComponent<Animator>();
                    animator.runtimeAnimatorController = _animatorController;
                    Debug.Log($"[HierarchyExport] Animator Controller を設定: {_animatorController.name}");
                }

                // 同名プレファブへ上書き保存（繰り返しても増えない）
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.SaveAssets();

                UnityEditor.Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                Debug.Log($"[HierarchyExport] プレファブ保存: {prefabPath}（材料アセット {matCount} / テクスチャ {texCount}）");

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

        // ================================================================
        // ミラー分岐
        // ================================================================

        /// <summary>
        /// ミラー分岐のミラー側 GameObject に付ける接尾辞。
        /// 規則は Runtime の MirrorBranchOps を正本とする。
        /// </summary>
        private const string MirrorBranchSuffix = MirrorBranchOps.MirrorBranchSuffix;

        // MirrorPeerIndex / AnalyzeMirrorBranches / AssignBranchSide / MirrorLocalTRS は
        // Runtime の Poly_Ling.Ops.MirrorBranchOps へ移設した（Player と共通化）。

        /// <summary>
        /// メッシュ／関節の GameObject を1つ生成する。
        /// mirror=true のときはミラー側の枝に配置し、関節のローカル姿勢を鏡像化する。
        /// </summary>
        private void CreateMeshGameObject(
            ModelContext model, MeshContext mc, int index, Mesh unityMesh,
            bool isSkinned, bool isJoint, bool mirror,
            GameObject rootGo, Transform armatureRoot,
            Dictionary<int, Transform> boneTransformMap,
            Dictionary<int, Transform> meshTransformByIndex,
            Dictionary<int, Transform> mirrorTransformByIndex,
            HashSet<string> usedMeshNames,
            int[] parentIndices,
            MirrorPeerIndex peers)
        {
            string rawName = string.IsNullOrEmpty(mc.Name) ? $"Mesh_{index}" : mc.Name;

            // ミラー側の関節は元と同名になるため接尾辞を付ける。
            // ミラー側メッシュ（MirrorSide / BakedMirror）は元から実体側と別名なので付けない。
            if (mirror && !MirrorBranchOps.IsMirrorSideContext(mc))
                rawName += MirrorBranchSuffix;

            string goName = usedMeshNames.Contains(rawName)
                ? MakeUniqueName(rawName + MeshNameSuffix, usedMeshNames)
                : MakeUniqueName(rawName, usedMeshNames);

            var go = new GameObject(goName);
            Undo.RegisterCreatedObjectUndo(go, "Create Mesh GameObject");

            // 親の解決（同じ側を優先し、無ければ実体側 → ボーン → ルートの順）
            Transform parentTf = rootGo.transform;
            if (!isSkinned)
            {
                int hp = (parentIndices != null && index < parentIndices.Length)
                    ? parentIndices[index]
                    : mc.HierarchyParentIndex;
                // 階層親がミラー側相方を持つ場合はそちらを親にする。
                //   例）ゆびB1+ の階層親は実体側 ゆびA1 だが、ミラー枝では ゆびA1+ にぶら下げる。
                //   相方の無い関節（両側に複製される）は階層親そのものをミラー枝で引く。
                if (MirrorBranchOps.TryResolveMirrorParent(
                        peers, hp, mirror,
                        idx => mirrorTransformByIndex.ContainsKey(idx),
                        out int parentIndex, out bool parentIsMirrorSide))
                {
                    if (parentIsMirrorSide)
                    {
                        if (mirrorTransformByIndex.TryGetValue(parentIndex, out var mTf))
                            parentTf = mTf;
                    }
                    else if (meshTransformByIndex.TryGetValue(parentIndex, out var nTf))
                        parentTf = nTf;
                    else if (boneTransformMap.TryGetValue(parentIndex, out var bTf))
                        parentTf = bTf;
                }
            }

            go.transform.SetParent(parentTf, worldPositionStays: false);

            if (mirror) mirrorTransformByIndex[index] = go.transform;
            else        meshTransformByIndex[index]   = go.transform;

            bool hasMeshParent = parentTf != rootGo.transform;

            // ミラー側は自分の BoneTransform が既に反転済みの値を持つ（原点CSVで設定される）。
            // そのまま鏡像化に通すと二重反転になるため、実体側相方の値を使う。
            MeshContext realPeer = null;
            if (mirror && MirrorBranchOps.IsMirrorSideContext(mc) &&
                peers != null && peers.TryGetReal(index, out int realIdx))
                realPeer = model.GetMeshContext(realIdx);

            MeshContext trsSource = realPeer ?? mc;

            if (isJoint)
            {
                // 関節はレンダラを持たない。姿勢のみ設定する。
                ApplyJointTransform(go, mc, trsSource, hasMeshParent, mirror);
                return;
            }

            if (isSkinned)
            {
                AttachSkinnedMesh(go, mc, unityMesh, model, boneTransformMap, armatureRoot);
                return;
            }

            // ミラー側は姿勢を鏡像化するため、頂点も新しいピボット基準に直す。
            Mesh meshForGo = mirror ? BuildMirrorSideMesh(mc, unityMesh, realPeer) : unityMesh;

            AttachStaticMesh(go, mc, meshForGo, model, hasMeshParent, mirror, trsSource);
        }

        /// <summary>
        /// ミラー側 GameObject 用のメッシュを生成する。
        ///
        /// PolyLing の MirrorSide 頂点は「実体側と同じ親（＝実体側のピボット）」を基準にした
        /// ローカル座標で保持されている。ミラー側の枝ではピボットを鏡像位置へ動かすため、
        /// その差分だけ頂点を平行移動しないと位置がずれる。
        ///
        /// ミラー側 GO のピボットは「実体側ピボットの鏡像」に置かれる。
        /// mc のワールド原点を W、実体側相方のワールド原点を R、ミラー軸の面を x = d とすると、
        /// 鏡像ピボットは mirror(R) で、頂点に必要な補正は W - mirror(R)。
        ///
        /// 原点CSV未適用（MirrorSide の原点が実体側と同一）なら W == R となり
        /// 補正量は 2(W - d) で従来と一致する。相方が取れない場合も同式にフォールバックする。
        /// 形状自体は MirrorSide が既に反転済みのため、反転や巻き順の変更は行わない。
        /// </summary>
        private static Mesh BuildMirrorSideMesh(MeshContext mc, Mesh source, MeshContext realPeer)
        {
            if (source == null) return null;

            var wm = mc.WorldMatrix;
            var w  = new Vector3(wm.m03, wm.m13, wm.m23);

            // ミラー軸・距離は実体側が正本
            var axisSource = realPeer ?? mc;
            float d = axisSource.MirrorDistance;

            Vector3 pivot = w;
            if (realPeer != null)
            {
                var rwm = realPeer.WorldMatrix;
                pivot = new Vector3(rwm.m03, rwm.m13, rwm.m23);
            }

            Vector3 mirrored;
            switch (axisSource.MirrorAxis)
            {
                case 2:  mirrored = new Vector3(pivot.x, 2f * d - pivot.y, pivot.z); break;   // Y
                case 4:  mirrored = new Vector3(pivot.x, pivot.y, 2f * d - pivot.z); break;   // Z
                default: mirrored = new Vector3(2f * d - pivot.x, pivot.y, pivot.z); break;   // X
            }

            Vector3 offset = w - mirrored;

            if (offset.sqrMagnitude < 1e-12f) return source;

            var mesh = UnityEngine.Object.Instantiate(source);
            mesh.name = source.name + MirrorBranchSuffix;

            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++) verts[i] += offset;
            mesh.vertices = verts;

            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 関節ノードの Transform を設定する。mirror=true ではピボットを鏡像化する。
        /// trsSource は鏡像化の元にする姿勢の持ち主（MirrorSide の場合は実体側相方）。
        /// </summary>
        private static void ApplyJointTransform(
            GameObject go, MeshContext mc, MeshContext trsSource, bool hasMeshParent, bool mirror)
        {
            var src = trsSource ?? mc;

            if (hasMeshParent)
            {
                // ミラー側は回転中心を反対側へ持っていく必要があるため、
                // ローカル姿勢を鏡像化する。配下メッシュの頂点は
                // BuildMirrorSideMesh 側でこのピボット基準に合わせる。
                var bt  = src.BoneTransform;
                var pos = bt.Position;
                var rot = bt.Rotation;

                if (mirror) MirrorBranchOps.MirrorLocalTRS(src, ref pos, ref rot);

                go.transform.localPosition    = pos;
                go.transform.localEulerAngles = rot;
                go.transform.localScale       = bt.Scale;
                return;
            }

            // 親を持たない場合はワールド指定。
            //   ここで鏡像化するとモデル原点基準で反転してしまうため行わない。
            //   ミラー分岐のルートは必ず実体側の親を持つ想定。
            var wm = mc.WorldMatrix;
            go.transform.position    = new Vector3(wm.m03, wm.m13, wm.m23);
            go.transform.rotation    = wm.rotation;
            go.transform.localScale  = wm.lossyScale;
        }

        // ================================================================
        // テクスチャのアセット化
        // ================================================================

        // マテリアルから拾うテクスチャプロパティ（CsvModelSerializer の読込側と同じ組）
        private static readonly string[] TexturePropertyNames =
        {
            "_BaseMap", "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap"
        };

        /// <summary>
        /// モデルのマテリアルが参照するテクスチャを texturesDir へ複製してアセット化し、
        /// マテリアルの参照先を複製後のアセットへ差し替える。戻り値は書き出したファイル数。
        ///   - 既に Assets 内のアセットでも元ファイルを複製する（モデル単位で完結させるため）
        ///   - メモリ上のテクスチャは PNG へエンコードして保存する
        /// </summary>
        private int ExportTexturesAsAssets(ModelContext model, string texturesDir)
        {
            var matRefs = model?.MaterialReferences;
            if (matRefs == null || matRefs.Count == 0) return 0;

            var pathByTexture = new Dictionary<Texture, string>();   // 同一テクスチャは1ファイルへ集約
            var usedFileNames = new HashSet<string>();
            var normalMapPaths = new HashSet<string>();              // 法線マップとして再インポートする対象
            var assignments = new List<(Material mat, string prop, string assetPath)>();

            foreach (var matRef in matRefs)
            {
                var mat = matRef?.Material;
                if (mat == null) continue;

                foreach (string prop in TexturePropertyNames)
                {
                    if (!mat.HasProperty(prop)) continue;

                    var tex = mat.GetTexture(prop);
                    if (tex == null) continue;

                    if (!pathByTexture.TryGetValue(tex, out string assetPath))
                    {
                        assetPath = WriteTextureFile(tex, texturesDir, usedFileNames);
                        if (string.IsNullOrEmpty(assetPath)) continue;

                        pathByTexture[tex] = assetPath;
                    }

                    if (prop == "_BumpMap") normalMapPaths.Add(assetPath);

                    assignments.Add((mat, prop, assetPath));
                }
            }

            if (pathByTexture.Count == 0) return 0;

            AssetDatabase.Refresh();

            // 法線マップはインポート設定を変更する
            foreach (string path in normalMapPaths)
            {
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null || importer.textureType == TextureImporterType.NormalMap) continue;

                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }

            // 複製後のアセットをマテリアルへ再代入
            foreach (var (mat, prop, path) in assignments)
            {
                var loaded = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (loaded != null) mat.SetTexture(prop, loaded);
            }

            return pathByTexture.Count;
        }

        /// <summary>
        /// テクスチャ1枚を texturesDir へ書き出し、そのアセットパスを返す。失敗時は null。
        /// </summary>
        private static string WriteTextureFile(Texture tex, string texturesDir, HashSet<string> usedFileNames)
        {
            string sourcePath = AssetDatabase.GetAssetPath(tex);
            bool hasSourceFile = !string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath);

            string baseName = SanitizeName(
                !string.IsNullOrEmpty(tex.name) ? tex.name
                : (hasSourceFile ? Path.GetFileNameWithoutExtension(sourcePath) : "Texture"));

            string ext = hasSourceFile ? Path.GetExtension(sourcePath) : ".png";
            if (string.IsNullOrEmpty(ext)) ext = ".png";

            string fileName = MakeUniqueName(baseName, usedFileNames) + ext;
            string dstPath  = $"{texturesDir}/{fileName}";

            try
            {
                if (hasSourceFile)
                {
                    // 元ファイルをそのまま複製（画像形式を保つ）。
                    //   .meta は複製しない（GUID が重複するため）。インポート設定は既定になる。
                    File.Copy(sourcePath, dstPath, overwrite: true);
                    return dstPath;
                }

                byte[] png = EncodePng(tex);
                if (png == null)
                {
                    Debug.LogWarning($"[HierarchyExport] テクスチャを書き出せない: {tex.name}");
                    return null;
                }

                File.WriteAllBytes(dstPath, png);
                return dstPath;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HierarchyExport] テクスチャ書き出しに失敗: {tex.name} → {e.Message}");
                return null;
            }
        }

        /// <summary>テクスチャを PNG バイト列へ変換する。読み取り不可の場合は RenderTexture 経由で取得。</summary>
        private static byte[] EncodePng(Texture tex)
        {
            if (tex is Texture2D t2d && t2d.isReadable)
                return t2d.EncodeToPNG();

            var rt = RenderTexture.GetTemporary(
                tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

            var prev = RenderTexture.active;
            Texture2D readable = null;

            try
            {
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;

                readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                readable.Apply();

                return readable.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        // ================================================================
        // 名前の一意化 / Humanoid 名の重複検査
        // ================================================================

        // 剛体 GameObject に付ける接尾辞（ボーン名との衝突回避）
        private const string RigidBodyNameSuffix = "_RB";

        // メッシュ GameObject 名がボーン名と衝突した時に付ける接尾辞
        private const string MeshNameSuffix = "_skinned";

        // root 配下に存在する GameObject 名を収集する。
        private static HashSet<string> CollectHierarchyNames(GameObject root)
        {
            var set = new HashSet<string>();
            if (root == null) return set;

            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                set.Add(t.name);

            return set;
        }

        // used に含まれない名前を返し、使用済みとして登録する。
        private static string MakeUniqueName(string baseName, HashSet<string> used)
        {
            string name = string.IsNullOrEmpty(baseName) ? "Object" : baseName;
            if (used.Add(name)) return name;

            for (int n = 1; ; n++)
            {
                string candidate = $"{name}_{n}";
                if (used.Add(candidate)) return candidate;
            }
        }

        // Humanoid 割当先のボーン名が root 配下で一意かを検査する。
        //   重複していると AvatarBuilder が Ambiguous Transform で失敗するため、
        //   Unity 側のエラーより先に該当名を通知する。
        private static bool ValidateHumanoidBoneNames(
            GameObject root, Dictionary<string, string> map, out string duplicatedNames)
        {
            duplicatedNames = string.Empty;
            if (root == null || map == null || map.Count == 0) return true;

            var count = new Dictionary<string, int>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                count.TryGetValue(t.name, out int c);
                count[t.name] = c + 1;
            }

            var dup = new List<string>();
            foreach (var kv in map)
            {
                if (string.IsNullOrEmpty(kv.Value)) continue;
                if (count.TryGetValue(kv.Value, out int c) && c > 1 && !dup.Contains(kv.Value))
                    dup.Add(kv.Value);
            }

            if (dup.Count == 0) return true;

            duplicatedNames = string.Join(", ", dup);
            return false;
        }

        // ================================================================
        // 出力先フォルダ（Assets/ 以下）
        // ================================================================

        // 空・Assets 外・末尾スラッシュを正規化して "Assets/..." 形式へ揃える。
        private static string NormalizeOutputRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return DefaultPrefabOutputRoot;

            string p = path.Replace('\\', '/').TrimEnd('/');
            if (p.Length == 0) return DefaultPrefabOutputRoot;
            if (p != "Assets" && !p.StartsWith("Assets/")) return DefaultPrefabOutputRoot;

            return p;
        }

        // 絶対パス → "Assets/..." 形式。Assets 外なら null。
        private static string ToAssetsRelative(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return null;

            string abs  = absolutePath.Replace('\\', '/').TrimEnd('/');
            string data = Application.dataPath.Replace('\\', '/').TrimEnd('/');

            if (abs == data) return "Assets";
            if (abs.StartsWith(data + "/")) return "Assets" + abs.Substring(data.Length);

            return null;
        }

        private void BrowseOutputRoot()
        {
            string sel = EditorUtility.OpenFolderPanel(
                "出力先フォルダを選択（Assets 以下）", NormalizeOutputRoot(_prefabOutputRoot), "");
            if (string.IsNullOrEmpty(sel)) return;

            string rel = ToAssetsRelative(sel);
            if (rel == null)
            {
                EditorUtility.DisplayDialog(
                    "エラー",
                    "出力先はこのプロジェクトの Assets フォルダ以下を指定してください:\n" + sel,
                    "OK");
                return;
            }

            _prefabOutputRoot = rel;
            SaveOutputRootPref();
            GUI.changed = true;
        }

        private void SaveOutputRootPref()
        {
            EditorPrefs.SetString(PrefsKeyPrefabOutputRoot, NormalizeOutputRoot(_prefabOutputRoot));
        }

        private void SaveAnimatorControllerPref()
        {
            string path = _animatorController != null
                ? AssetDatabase.GetAssetPath(_animatorController)
                : "";
            EditorPrefs.SetString(PrefsKeyAnimatorController, path ?? "");
        }

        // ファイル名に使えない文字を '_' に置換
        /// <summary>
        /// プロジェクトフォルダから出力パスに挟む名前を決める。
        /// project.csv の name 行を優先し、無ければフォルダ名を使う。
        /// プロジェクトと判定できない場合は空文字（＝挟まない）。
        /// CsvProjectSerializer.Import は配下の全モデルを読み込むためここでは使わない。
        /// </summary>
        private static string ResolveProjectFolderName(string projectFolder)
        {
            if (string.IsNullOrEmpty(projectFolder) || !Directory.Exists(projectFolder))
                return "";

            string csv = Path.Combine(projectFolder, "project.csv");
            if (!File.Exists(csv)) return "";

            string name = null;
            try
            {
                foreach (string line in File.ReadAllLines(csv))
                {
                    if (!line.StartsWith("name,")) continue;
                    name = line.Substring("name,".Length).Trim();
                    break;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[HierarchyExport] project.csv の読み取りに失敗: {e.Message}");
            }

            if (string.IsNullOrWhiteSpace(name))
                name = new DirectoryInfo(projectFolder).Name;

            return string.IsNullOrWhiteSpace(name) ? "" : SanitizeName(name);
        }

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
            // メッシュ名がボーン名と衝突すると Humanoid 割当先の Transform 名が
            // 一意でなくなり AvatarBuilder が失敗する。衝突時のみ接尾辞を付ける。
            var usedMeshNames = CollectHierarchyNames(rootGo);

            // 静的メッシュは Depth から補正した親インデックスに従って親子にする。
            //   モデルの HierarchyParentIndex は、旧 MQO インポータの不具合により
            //   ミラー実体（例: ゆびA1）が親になる箇所でグループまで巻き戻っていることがある。
            //   ここではモデルを変更せず、Depth から解決し直した配列を使う。
            //   WorldMatrix も同じ index で累積されるため、GO 側は BoneTransform（ローカル）
            //   を設定すれば同じワールド位置になる。
            //   スキンドメッシュは BindPose 前提のためルート直下のまま。
            var parentIndices = MeshHierarchyOps.BuildParentIndicesFromDepth(model);

            // 実体側 ↔ ミラー側の index 対応表（ミラー枝の親解決・姿勢の鏡像化に使う）
            var mirrorPeers = MirrorPeerIndex.Build(model);

            var meshTransformByIndex   = new Dictionary<int, Transform>();
            var mirrorTransformByIndex = new Dictionary<int, Transform>();

            // ミラー分岐の解析（分岐ルート配下を実体側／ミラー側に振り分ける）
            var branchSide = MirrorBranchOps.AnalyzeMirrorBranches(model, parentIndices);

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                // 実体メッシュに加えてミラー側も出力する。
                //   MirrorSide / BakedMirror は実頂点を持つ（PMXエクスポートと同じ扱い）。
                //   ボーン・モーフ・剛体・JOINT は除外。
                if (mc.Type != MeshType.Mesh &&
                    mc.Type != MeshType.MirrorSide &&
                    mc.Type != MeshType.BakedMirror) continue;
                if (mc.MeshObject == null) continue;
                if (_exportVisibleOnly && !mc.IsVisible) continue;

                bool isSkinned = mc.MeshObject.HasBoneWeight && boneTransformMap.Count > 0;

                // 頂点を持たないノードは関節（グループ）として扱い、空の GameObject にする。
                bool isJoint = mc.MeshObject.Vertices.Count == 0;

                Mesh unityMesh = null;
                if (!isJoint)
                {
                    // Unityメッシュはブリッジ経由（MeshObject.ToUnityMesh）で毎回生成する。
                    //   mc.UnityMesh は表示用に WorldMatrix を焼き込んだメッシュのため
                    //   （UnifiedSystemAdapter が ToUnityMesh(xform) で作る）、
                    //   これを使うと GameObject の Transform と二重に位置が適用される。
                    //
                    //   行列版を単位行列で呼ぶ。引数なし版は頂点の名寄せキーに
                    //   法線サブindexを含まないため、法線が分岐する頂点で
                    //   三角形の参照が引けず面が欠落する（穴が開く）。
                    unityMesh = mc.MeshObject.ToUnityMesh(Matrix4x4.identity);
                    if (unityMesh == null) continue;
                }

                // 分岐内での所属側。ミラー分岐はスキンドでは扱わない。
                int  side     = 0;
                bool inBranch = !isSkinned && branchSide.TryGetValue(i, out side);
                if (!inBranch) side = 0;

                // 関節は両側に複製する。メッシュは所属側のみ。
                bool makeNormal = !inBranch || isJoint || side == 0;
                bool makeMirror = inBranch && (isJoint || side == 1);

                if (makeNormal)
                    CreateMeshGameObject(
                        model, mc, i, unityMesh, isSkinned, isJoint, mirror: false,
                        rootGo, armatureRoot, boneTransformMap,
                        meshTransformByIndex, mirrorTransformByIndex, usedMeshNames,
                        parentIndices, mirrorPeers);

                if (makeMirror)
                    CreateMeshGameObject(
                        model, mc, i, unityMesh, isSkinned, isJoint, mirror: true,
                        rootGo, armatureRoot, boneTransformMap,
                        meshTransformByIndex, mirrorTransformByIndex, usedMeshNames,
                        parentIndices, mirrorPeers);
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

        private void AttachStaticMesh(
            GameObject go, MeshContext mc, Mesh unityMesh, ModelContext model,
            bool hasMeshParent, bool mirror = false, MeshContext trsSource = null)
        {
            var src = trsSource ?? mc;

            var mf = Undo.AddComponent<MeshFilter>(go);
            var mr = Undo.AddComponent<MeshRenderer>(go);

            // プレファブ保存時は共有アセット化（決定論パス・上書き）。
            var staticMesh = _prefabExportActive
                ? MeshAssetUtil.SaveDeterministic(unityMesh, ResolveMeshAssetPath(unityMesh.name))
                : unityMesh;
            mf.sharedMesh = staticMesh;

            if (hasMeshParent)
            {
                // 親側で累積されるのでローカル値を設定する。
                //   ミラー側はピボットを鏡像化する（頂点は BuildMirrorSideMesh で補正済み）。
                //   MirrorSide は自分の値が既に反転済みなので実体側相方の値を使う。
                var bt  = src.BoneTransform;
                var pos = bt.Position;
                var rot = bt.Rotation;

                if (mirror) MirrorBranchOps.MirrorLocalTRS(src, ref pos, ref rot);

                go.transform.localPosition    = pos;
                go.transform.localEulerAngles = rot;
                go.transform.localScale       = bt.Scale;
            }
            else
            {
                var wm = mc.WorldMatrix;
                go.transform.position   = new Vector3(wm.m03, wm.m13, wm.m23);
                go.transform.rotation   = wm.rotation;
                go.transform.localScale = wm.lossyScale;
            }

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
            // 剛体 GameObject 名がボーン名と衝突すると、Humanoid 割当先の
            // Transform 名が一意でなくなり AvatarBuilder が失敗する。
            //   例）ボーン「頭」の配下に剛体「頭」→ Ambiguous Transform
            // よって接尾辞を付け、さらに既存名と重ならないよう一意化する。
            var usedNames = CollectHierarchyNames(rootGo);

            GameObject rigidFolder = null;
            var rigidbodyByName = new Dictionary<string, Rigidbody>();

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null || mc.Type != MeshType.RigidBody) continue;
                var rb = mc.MeshObject?.RigidBodyData;
                if (rb == null) continue;

                // メッシュ側と同じ規則：衝突した時だけ接尾辞を付ける。
                //   無条件に付けると、ヒエラルキーインポートで名前を取り込んだ後に
                //   再エクスポートするたび "_RB_RB" と伸びていく。
                string rawName = string.IsNullOrEmpty(mc.Name) ? $"RigidBody_{i}" : mc.Name;
                string goName  = usedNames.Contains(rawName)
                    ? MakeUniqueName(rawName + RigidBodyNameSuffix, usedNames)
                    : MakeUniqueName(rawName, usedNames);

                var go = new GameObject(goName);
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
