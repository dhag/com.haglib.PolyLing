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
//   - ヒエラルキー生成そのものは Runtime の HierarchyBuilder が持つ。
//     Editor / Runtime の切り分けの規約は
//     Runtime/Poly_Ling_Main/HierarchyIO/HierarchyBuilder.cs 冒頭を正典とする。
//     本ウィンドウに残すのは Editor でしか成立しない決定だけ：
//     IMGUI・EditorPrefs・プレファブ化・テクスチャのアセット化・Avatar 生成・
//     Undo のグループ化・レポート。
//
// 【移植元】
//   旧 LiteHierarchyExportSubPanel.Export（現行 ModelContext API 準拠）。
//   UI を IMGUI の EditorWindow に置き換え、Export ロジックは現行 API のまま移植。
//
// 【出力構造】
//   HierarchyBuilder.cs 冒頭を参照。ここには書き写さない（二重管理になるため）。
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
using Poly_Ling.HierarchyIO;
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
        private bool _includeInvisibleAncestors = true; // 可視ノードの親が不可視なら補完して出力
        private bool _exportMeshOnly    = false;  // ボーンを除外しメッシュのみ
        private bool _exportPhysics     = true;   // 剛体/JOINT を Unity 物理部品として出力
        private bool _saveAsPrefab      = true;   // シーンではなくプレファブとして保存（アセット化）
        private bool _buildAvatar       = true;   // プレファブと同時に Humanoid Avatar(.asset) を生成
        private bool _supplementHumanoid = false; // 不足する Humanoid 必須関節をダミーで補完
        private bool _writeAttach       = true;   // IK 付帯を attach.csv でプレファブ同居出力
        private bool _attachAnimator    = true;   // 生成した Avatar を Animator に割り当てる（プレファブ時）
        private bool _sceneAnimator     = true;   // シーン出力時にルートへ空の Animator を付与

        // --- レンダラ種別 ------------------------------------------------
        //   従来は「頂点がボーンウェイトを持つか」だけで自動決定していたため、
        //   同じモデルから MeshFilter 版とスキンド版を作り分ける手段が無かった。
        //   Auto            : 従来どおり（ウェイトがあればスキンド）
        //   ForceMeshFilter : ウェイトがあっても MeshFilter+MeshRenderer で出す
        //   ※「スキンド強制」は用意しない。ウェイトが無いメッシュはスキンドに
        //     できないため、その場合は先に MeshFilter → Skinned 変換が必要。
        //   種別そのものの定義は Runtime（HierarchyRendererMode）が正本。
        //   ここに残すのは UI のラベルだけ。
        private static readonly string[] RendererModeLabels =
            { "自動（ウェイト有無で判定）", "MeshFilter を強制" };
        private HierarchyRendererMode _rendererMode = HierarchyRendererMode.Auto;

        // --- ミラー分岐の許容モード（EditorPrefs で保持・既定は許容） ------
        //   部品数が多いモデルでは、個々のオブジェクトのミラー設定漏れ（もしくは
        //   作業中に切って戻し忘れ）がほぼ必ず起きる。許容モードでは、分岐ルート
        //   配下の実体側ノードにミラー側コンテキストが無くても、実体側から鏡像を
        //   生成してミラー枝へ出す。厳密モードは従来動作。
        private const string PrefsKeyTolerantMirrorBranch = "PolyLing.HierarchyExport.TolerantMirrorBranch";
        private bool _tolerantMirrorBranch = true;

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

        // --- 直近の Export() の生成結果（Export→Avatar 間で共有） ---
        //   Avatar 割当先を model 側の名前ではなく実際に生成した Transform で引くために持つ。
        //   索引→Transform 表の規約は HierarchyBuildResult.cs を正典とする。
        private HierarchyBuildResult _build;

        // --- 出力パスに挟むプロジェクト名（空なら挟まない） ---
        //   プロジェクトが特定できた場合に Assets/PolyLing/<プロジェクト名>/<モデル名> とする。
        //   別プロジェクトに同名モデルがあるときの衝突を避けるため。
        private string _prefabProjectFolder = "";

        // --- 結果レポート（コンソールとダイアログの両方へ出す） ---
        private readonly HierarchyExportReport _report = new HierarchyExportReport();


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

            _tolerantMirrorBranch = EditorPrefs.GetBool(PrefsKeyTolerantMirrorBranch, true);
        }

        private void OnDisable()
        {
            SaveOutputRootPref();
            SaveAnimatorControllerPref();
            EditorPrefs.SetBool(PrefsKeyTolerantMirrorBranch, _tolerantMirrorBranch);
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
            using (new EditorGUI.DisabledScope(!_exportVisibleOnly))
            {
                _includeInvisibleAncestors = EditorGUILayout.Toggle(
                    "不可視の親を補完", _includeInvisibleAncestors);
            }
            if (_exportVisibleOnly && _includeInvisibleAncestors)
            {
                EditorGUILayout.HelpBox(
                    "可視ノードの親をたどり、不可視の親も Transform のみのノードとして出力します"
                    + "（隠した形状は出力しません）。切ると子がルート直下へ平坦化されます。",
                    MessageType.None);
            }
            _rendererMode = (HierarchyRendererMode)EditorGUILayout.Popup(
                "レンダラ", (int)_rendererMode, RendererModeLabels);
            if (_rendererMode == HierarchyRendererMode.ForceMeshFilter)
            {
                EditorGUILayout.HelpBox(
                    "ボーンウェイトを無視して MeshFilter + MeshRenderer で出力します"
                    + "（ボーン階層は Armature として出ます）。",
                    MessageType.None);
            }

            EditorGUI.BeginChangeCheck();
            _tolerantMirrorBranch = EditorGUILayout.Toggle(
                "ミラー設定漏れを許容", _tolerantMirrorBranch);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetBool(PrefsKeyTolerantMirrorBranch, _tolerantMirrorBranch);
            if (_tolerantMirrorBranch)
            {
                EditorGUILayout.HelpBox(
                    "ミラー分岐ルート配下は、個々のオブジェクトのミラー設定が無くても"
                    + "実体側から鏡像を作ってミラー枝へ出力します"
                    + "（軸・距離はオブジェクトごとの設定を使います）。\n"
                    + "切ると、ミラー側メッシュが実在するオブジェクトだけがミラー枝に出ます。",
                    MessageType.None);
            }

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
                {
                    _attachAnimator = EditorGUILayout.Toggle("Animator を付与して割当", _attachAnimator);
                    _supplementHumanoid = EditorGUILayout.Toggle("不足関節を補完", _supplementHumanoid);
                }
                _writeAttach = EditorGUILayout.Toggle("IK付帯(attach.csv)も出力", _writeAttach);
                EditorGUILayout.HelpBox(
                    NormalizeOutputRoot(_prefabOutputRoot) +
                    "/<モデル名>/ にメッシュ/マテリアルを共有アセット化し、" +
                    "同名プレファブへ上書き保存します（繰り返しても増えません）。" +
                    (_buildAvatar ? "\nHumanoid 割当から Avatar(.asset) も同時生成します。" : "") +
                    (_buildAvatar && _attachAnimator
                        ? "\n生成した Avatar を Animator に割り当ててからプレファブ化します。" : "") +
                    (_buildAvatar && _supplementHumanoid
                        ? "\n不足する必須関節をダミーの空オブジェクトで補ってから生成します。" : ""),
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
        // 外部エントリ（リモート受信クライアント等）
        // ================================================================

        /// <summary>
        /// フォルダを指定して書き出す。ボタン押下と同一経路（LoadAndExport）を通るため、
        /// オプション・EditorPrefs・単体/一括判定は既存のまま適用される。
        /// 既にウィンドウが開いていれば、その時点のオプション設定がそのまま使われる。
        /// </summary>
        public static void ExportFromFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;

            var window = GetWindow<HierarchyExportWindow>(true, "Hierarchy Export", true);
            window._modelFolderPath = folderPath;
            window.LoadAndExport();
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
            var failed   = new List<string>();
            var problems = new List<string>();   // 成功したが警告・エラーが出たモデル

            _prefabProjectFolder = ResolveProjectFolderName(projectFolder);
            try
            {
                foreach (string dir in modelFolders)
                {
                    string name = Path.GetFileName(dir);

                    if (ExportSingleModel(dir, showDialogOnError: false))
                    {
                        ok++;
                        if (_report.HasProblem)
                            problems.Add($"{name}: {_report.BuildOneLineSummary()}");
                    }
                    else
                    {
                        failed.Add($"{name}: {_report.BuildOneLineSummary()}");
                    }
                }
            }
            finally
            {
                _prefabProjectFolder = "";
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{modelFolders.Count} 件中 {ok} 件を書き出しました。");

            if (failed.Count > 0)
            {
                sb.AppendLine().AppendLine($"── 失敗 ({failed.Count}) ──");
                foreach (string f in failed) sb.AppendLine("・" + f);
            }

            if (problems.Count > 0)
            {
                sb.AppendLine().AppendLine($"── 警告あり ({problems.Count}) ──");
                foreach (string w in problems) sb.AppendLine("・" + w);
            }

            if (failed.Count == 0 && problems.Count == 0)
                sb.AppendLine().Append("警告・エラーはありません。");
            else
                sb.AppendLine().Append("詳細はコンソールを確認してください。");

            string msg = sb.ToString();
            Debug.Log("[HierarchyExport] " + msg);
            EditorUtility.DisplayDialog(
                failed.Count > 0 ? "一括エクスポート（失敗あり）" : "一括エクスポート", msg, "OK");
        }

        /// <summary>モデルフォルダ1件を書き出す。成功したら true。</summary>
        /// <param name="showDialogOnError">
        /// 単体書き出しのとき true。失敗時のエラーに加えて、成功時もレポートを出す。
        /// 一括のときは false（まとめのダイアログを呼び出し側が出す）。
        /// </param>
        private bool ExportSingleModel(string modelFolder, bool showDialogOnError)
        {
            // レポートはモデル1件ごとに作り直す。
            _report.Reset();

            // プロジェクトファイル（フォルダ形式）→ ModelContext
            // out パラメータ（EditorState / WorkPlane / 追加エントリ）は本処理では不要のため破棄。
            ModelContext model = CsvModelSerializer.LoadModel(modelFolder, out _, out _, out _);
            if (model == null)
            {
                string m = "モデルの読み込みに失敗しました（model.csv 不在など）:\n" + modelFolder;
                _report.Error(m);
                if (showDialogOnError) EditorUtility.DisplayDialog("エラー", m, "OK");
                return false;
            }

            // 読込直後の ModelContext はボーンの WorldMatrix が未計算。
            // BoneTransform の親子関係から WorldMatrix を構築してから書き出す。
            model.ComputeWorldMatrices();

            WarnAboutExpectations(model);

            if (_saveAsPrefab)
            {
                bool prefabOk = ExportAsPrefab(model);
                if (showDialogOnError) ShowExportReportDialog(model, prefabOk);
                return prefabOk;
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
            else
            {
                _report.Error("ヒエラルキー生成に失敗しました。");
            }

            if (showDialogOnError) ShowExportReportDialog(model, root != null);
            return root != null;
        }

        /// <summary>単体書き出しの結果ダイアログ。成功・失敗どちらでもレポートを出す。</summary>
        private void ShowExportReportDialog(ModelContext model, bool ok)
        {
            string modelName = string.IsNullOrEmpty(model?.Name) ? "(名称なし)" : model.Name;

            string title = !ok                 ? "書き出し失敗"
                         : _report.HasProblem  ? "書き出し完了（警告あり）"
                                               : "書き出し完了";

            string header = ok
                ? $"{modelName} を書き出しました。"
                : $"{modelName} の書き出しに失敗しました。";

            EditorUtility.DisplayDialog(title, _report.BuildDialogText(header), "OK");
        }

        /// <summary>
        /// レポートをエクスポート先フォルダへ export_report.txt として書き出す。
        ///   書き出し自体の失敗は本体の成否に影響させない（ログに落とすだけ）。
        ///   パスは _report.LogFilePath に入れ、結果ダイアログの案内先にする。
        /// </summary>
        private void WriteExportReportFile(ModelContext model, string baseDir, bool ok)
        {
            if (string.IsNullOrEmpty(baseDir)) return;

            string modelName = string.IsNullOrEmpty(model?.Name) ? "(名称なし)" : model.Name;
            string header    = ok ? "書き出し成功" : "書き出し失敗";
            string path      = $"{baseDir}/{ReportFileName}";

            try
            {
                // BOM 付き UTF-8。Windows のテキストエディタで文字化けさせないため。
                File.WriteAllText(
                    path,
                    _report.BuildLogText(header, modelName),
                    new System.Text.UTF8Encoding(true));

                _report.LogFilePath = path;
                AssetDatabase.Refresh();
                _report.Log($"レポート書き出し: {path}");
            }
            catch (System.Exception e)
            {
                // ここで Warn にすると「レポートが書けなかった」という警告が
                // レポートに載らないまま件数だけ増える。ログに留める。
                _report.Log($"レポートの書き出しに失敗: {path} → {e.Message}");
            }
        }

        /// <summary>エクスポート先へ残すレポートのファイル名。</summary>
        private const string ReportFileName = "export_report.txt";

        // ================================================================
        // プレファブ保存（決定論パス・上書き・アセット化）
        // ================================================================

        /// <summary>プレファブとして保存する。成功したら true。</summary>
        private bool ExportAsPrefab(ModelContext model)
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

            // マテリアルを共有アセット化（→ matRef.Material が共有アセットになり HierarchyBuilder が参照）
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
                    _report.Error("ヒエラルキー生成に失敗しました。");
                    return false;
                }

                // Avatar も生成（Humanoid 割当 + 可動域を model から直接）。
                //   プレファブ保存より前に実行する。保存後だと Animator への割当結果が
                //   プレファブに含まれない。
                if (_buildAvatar)
                {
                    var humanoid = HumanoidTransformMap.Build(model, _build);
                    foreach (string w in humanoid.Warnings) _report.Warn(w);
                    if (humanoid.SupplementedLog.Count > 0)
                    {
                        Debug.Log(
                            $"[HierarchyExport] 半身モデルのミラー側 {humanoid.SupplementedLog.Count} 件を補完:\n  "
                            + string.Join("\n  ", humanoid.SupplementedLog));
                    }

                    var avMap    = humanoid.Map;
                    var avLimits = humanoid.Limits;

                    if (avMap.Count == 0)
                    {
                        _report.AvatarResult = "スキップ（Humanoid 割当なし）";
                        _report.Warn("Humanoid 割当が無いため Avatar 生成をスキップ。");
                    }
                    else
                    {
                        // 不足する必須関節をダミーで補完（既定 OFF）。
                        //   名前重複検査より前に実行する。補完で追加した名前も検査対象にするため。
                        if (_supplementHumanoid)
                        {
                            _report.SupplementedJointCount = HumanoidSupplementBuilder.Supplement(
                                root, avMap, m => _report.Log(m), m => _report.Warn(m));
                        }

                        if (!HumanoidTransformMap.ValidateHumanoidBoneNames(root, avMap, out string dupNames))
                        {
                            _report.AvatarResult = "スキップ（ボーン名重複）";
                            _report.Warn(
                                "Humanoid 割当先のボーン名が階層内で重複しているため " +
                                "Avatar 生成をスキップ: " + dupNames);
                        }
                        else
                        {
                            string avatarPath = $"{baseDir}/{modelName}.asset";
                            var avatar = AvatarBuildCore.BuildAndSaveAvatar(root, avMap, avLimits, avatarPath,
                                m => _report.Log(m));

                            _report.AvatarResult = avatar != null
                                ? $"生成しました（{avMap.Count} ボーン）"
                                : "生成に失敗";
                            if (avatar == null) _report.Warn("Avatar の生成に失敗しました。");

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
                                    _report.Warn("Avatar 生成に失敗したため Animator を付与しない。");
                                }
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
                _report.Log($"プレファブ保存: {prefabPath}（材料アセット {matCount} / テクスチャ {texCount}）");

                if (prefab == null) _report.Error("プレファブの保存に失敗しました: " + prefabPath);

                // IK 付帯を attach.csv でプレファブ同居出力（案X: Humanoid/HumanLimit は Avatar が正）
                if (_writeAttach)
                {
                    AttachSidecarCsv.Write(model, $"{baseDir}/attach.csv");
                    AssetDatabase.Refresh();
                }

                // 警告・エラーをプレファブと同じフォルダへテキストで残す。
                //   コンソールはスタックトレースが混ざって読みにくく、他のログにも流される。
                //   ここまでで警告は出揃っている（WarnAboutExpectations は ExportSingleModel、
                //   Avatar 関連は上の HumanoidTransformMap.Build / AvatarBuildCore）。
                WriteExportReportFile(model, baseDir, prefab != null);

                return prefab != null;
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
        // レポートへ警告を積むためインスタンスメソッド。呼び出し元は ExportTexturesAsAssets のみ。
        private string WriteTextureFile(Texture tex, string texturesDir, HashSet<string> usedFileNames)
        {
            string sourcePath = AssetDatabase.GetAssetPath(tex);
            bool hasSourceFile = !string.IsNullOrEmpty(sourcePath) && File.Exists(sourcePath);

            string baseName = SanitizeName(
                !string.IsNullOrEmpty(tex.name) ? tex.name
                : (hasSourceFile ? Path.GetFileNameWithoutExtension(sourcePath) : "Texture"));

            string ext = hasSourceFile ? Path.GetExtension(sourcePath) : ".png";
            if (string.IsNullOrEmpty(ext)) ext = ".png";

            string fileName = HierarchyBuilder.MakeUniqueName(baseName, usedFileNames) + ext;
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
                    _report.Warn($"テクスチャを書き出せない: {tex.name}");
                    return null;
                }

                File.WriteAllBytes(dstPath, png);
                return dstPath;
            }
            catch (System.Exception e)
            {
                _report.Warn($"テクスチャ書き出しに失敗: {tex.name} → {e.Message}");
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
        // レポートへ警告を積むためインスタンスメソッド。
        private string ResolveProjectFolderName(string projectFolder)
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
                _report.Warn($"project.csv の読み取りに失敗: {e.Message}");
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

        /// <summary>
        /// 「そのつもりで出したのに出ない」典型パターンを、出力の前に明示する。
        ///
        /// ミラー・ウェイトに関する判定は生成そのものの性質なので Runtime 側
        /// （HierarchyBuilder.WarnAboutExpectations）が持つ。
        /// Avatar 生成は Editor でしか行わないため、その分だけここで足す。
        /// </summary>
        private void WarnAboutExpectations(ModelContext model)
        {
            if (model == null) return;

            var pre = new HierarchyBuildResult();
            new HierarchyBuilder(BuildHierarchyOptions()).WarnAboutExpectations(model, pre);
            foreach (string w in pre.Warnings) _report.Warn(w);

            if (_buildAvatar && (model.HumanoidMapping == null || model.HumanoidMapping.IsEmpty))
            {
                _report.Warn(
                    "Humanoid 割当が空です。Avatar は生成できません。\n"
                    + "先に Humanoid 割当（最低でも Hips）を設定してください。"
                    + "「不足関節を補完」は Hips を起点に脚・腕を補うため、Hips が無いと動きません。");
            }
        }

        // ================================================================
        // ModelContext → Unityヒエラルキー
        // ================================================================
        //
        // 生成そのものは Runtime の HierarchyBuilder が行う。
        // ここに残すのは Editor でしか成立しない決定だけ。
        //   ・Undo を 1 操作にまとめる
        //   ・メッシュを共有アセット化するか／どのパスへ保存するか
        //   ・生成側が溜めた警告・補足をレポート（コンソール／ダイアログ）へ流す

        /// <summary>ModelContext を Unity ヒエラルキーに書き出し、ルート GameObject を返す。</summary>
        private GameObject Export(ModelContext model)
        {
            Undo.SetCurrentGroupName("PolyLing: Export to Hierarchy");
            int undoGroup = Undo.GetCurrentGroup();

            // プレファブ保存のときだけメッシュを共有アセット化する。
            // どのパスへ保存するかは Editor の都合なので Runtime へ渡さない。
            System.Func<Mesh, Mesh> persistMesh = _prefabExportActive
                ? (m => MeshAssetUtil.SaveDeterministic(m, ResolveMeshAssetPath(m.name)))
                : (System.Func<Mesh, Mesh>)null;

            _build = new HierarchyBuilder(BuildHierarchyOptions(), persistMesh).Build(model);

            Undo.CollapseUndoOperations(undoGroup);

            // 生成側はログ方針を持たない。ここでレポートへ流す（＝コンソールにも出る）。
            foreach (string w in _build.Warnings) _report.Warn(w);
            foreach (string n in _build.Notes)    _report.Note(n);

            _report.BoneCount                 = _build.BoneCount;
            _report.ExportedNodeCount         = _build.ExportedNodeCount;
            _report.SkippedInvisibleCount     = _build.SkippedInvisibleCount;
            _report.SupplementedAncestorCount = _build.SupplementedAncestorCount;

            return _build.Root;
        }

        /// <summary>UI のトグルから Runtime 側の生成設定を組む。</summary>
        private HierarchyBuildOptions BuildHierarchyOptions()
        {
            return new HierarchyBuildOptions
            {
                CreateArmature            = _createArmature,
                UseBindpose               = _useBindpose,
                ExportVisibleOnly         = _exportVisibleOnly,
                IncludeInvisibleAncestors = _includeInvisibleAncestors,
                ExportMeshOnly            = _exportMeshOnly,
                ExportPhysics             = _exportPhysics,
                TolerantMirrorBranch      = _tolerantMirrorBranch,
                RendererMode              = _rendererMode,
            };
        }
    }
}
