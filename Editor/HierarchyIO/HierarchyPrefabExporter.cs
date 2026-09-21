// Editor/HierarchyIO/HierarchyPrefabExporter.cs
// ============================================================
// プロジェクトファイル → Unity ヒエラルキー／プレファブ の書き出し本体
// ============================================================
//
// 【なぜウィンドウから分けたか】 2026-09-17
//   以前は HierarchyExportWindow が処理本体を持ち、オプションをウィンドウの
//   フィールドから読み、途中で EditorUtility.DisplayDialog を出していた。
//   MCP（PolyLingEditorControlServer の prefab_export）から呼ぶと
//   ダイアログでメインスレッドが止まり、応答が返らない。
//   本体をここへ移し、オプションは HierarchyExportOptions で受け取り、
//   ダイアログに出す文言は HierarchyExportOutcome として返す。
//   ダイアログを出すかどうかは呼び出し側（ウィンドウ）が決める。
//
// 【処理の流れ】
//   1. プロジェクトファイル（フォルダ形式）を CsvModelSerializer.LoadModel で
//      ModelContext として復元する。
//   2. ModelContext.ComputeWorldMatrices() でボーン階層から WorldMatrix を構築。
//      （読込直後の ModelContext は WorldMatrix 未計算のため必須）
//   3. Export() で Unity GameObject 階層を生成する。
//
// 【設計方針】
//   - 「ファイル」＝プロジェクトファイル形式（CsvModelSerializer/CsvProjectSerializer）。
//     座標変換が入らないため非破壊。
//   - Unityメッシュ生成は MeshObject.ToUnityMesh()（内部でメッシュブリッジ）経由。
//   - ヒエラルキー生成そのものは Runtime の HierarchyBuilder が持つ。
//     Editor / Runtime の切り分けの規約は
//     Runtime/Poly_Ling_Main/HierarchyIO/HierarchyBuilder.cs 冒頭を正典とする。
//     ここに残すのは Editor でしか成立しない決定だけ：
//     プレファブ化・テクスチャのアセット化・Avatar 生成・Undo のグループ化・レポート。
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
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Serialization.FolderSerializer;
using Poly_Ling.AssetIO;
using Poly_Ling.HierarchyIO;
using Poly_Ling.Ops;

namespace Poly_Ling.EditorIO
{
    /// <summary>書き出しのオプション一式。既定値は旧 HierarchyExportWindow のフィールド既定値と同じ。</summary>
    public sealed class HierarchyExportOptions
    {
        public bool CreateArmature            = true;   // ボーン階層（Armature）を生成
        public bool UseBindpose               = true;   // MeshContext.BindPose を bindposes に使用
        public bool ExportVisibleOnly         = true;   // 可視メッシュのみ書き出し
        public bool IncludeInvisibleAncestors = true;   // 可視ノードの親が不可視なら補完して出力
        public bool ExportMeshOnly            = false;  // ボーンを除外しメッシュのみ
        public bool ExportPhysics             = true;   // 剛体/JOINT を Unity 物理部品として出力
        public bool AddToHierarchy            = false;  // 生成物を現在のシーンへ追加
        public bool DirectSingleObjectToHierarchy = true; // 直下が1個ならモデル用ルートを省略
        public bool SaveAsPrefab              = true;   // プレファブとして保存（Hierarchy追加と併用可）
        public bool BuildAvatar               = true;   // プレファブと同時に Humanoid Avatar(.asset) を生成
        public bool SupplementHumanoid        = false;  // 不足する Humanoid 必須関節をダミーで補完
        public bool WriteAttach               = true;   // IK 付帯を attach.csv でプレファブ同居出力
        public bool AttachAnimator            = true;   // 生成した Avatar を Animator に割り当てる（プレファブ時）
        public bool SceneAnimator             = true;   // シーン出力時にルートへ空の Animator を付与
        public bool TolerantMirrorBranch      = true;   // ミラー設定漏れを許容

        /// <summary>レンダラ種別。定義は Runtime（HierarchyRendererMode）が正本。</summary>
        public HierarchyRendererMode RendererMode = HierarchyRendererMode.Auto;

        /// <summary>プレファブ／アセットの書き込み先ルート（Assets/ 以下）。</summary>
        public string PrefabOutputRoot = HierarchyPrefabExporter.DefaultPrefabOutputRoot;

        /// <summary>任意。指定されていればルートの Animator に設定する。</summary>
        public RuntimeAnimatorController AnimatorController;

        public HierarchyExportOptions Clone() => (HierarchyExportOptions)MemberwiseClone();
    }

    /// <summary>書き出し 1 回ぶんの結果。ダイアログに出す文言をそのまま持つ。</summary>
    public sealed class HierarchyExportOutcome
    {
        /// <summary>全件成功なら true。</summary>
        public bool Success;

        /// <summary>ダイアログのタイトルに相当する文言。</summary>
        public string Title = "";

        /// <summary>ダイアログ本文に相当する文言。</summary>
        public string Text = "";

        /// <summary>保存したプレファブのアセットパス（Assets/...）。</summary>
        public readonly List<string> PrefabPaths = new List<string>();
    }

    /// <summary>プロジェクトファイル（フォルダ形式）を読み込み、Unity ヒエラルキー／プレファブへ書き出す。</summary>
    public sealed class HierarchyPrefabExporter
    {
        public const string DefaultPrefabOutputRoot = "Assets/PolyLing";

        private readonly HierarchyExportOptions _opt;

        // --- プレファブ保存時のみ有効な一時状態（Export→Attach 間で共有） ---
        private bool   _prefabExportActive = false;  // このExportがアセット化を伴うか
        private string _meshesDir = "";              // メッシュ .asset 出力先（Assets/...）
        private readonly HashSet<string> _usedMeshNames = new HashSet<string>(); // 同名衝突回避

        // --- 直近の Export() の生成結果（Export→Avatar 間で共有） ---
        //   索引→Transform 表の規約は HierarchyBuildResult.cs を正典とする。
        private HierarchyBuildResult _build;

        // --- 出力パスに挟むプロジェクト名（空なら挟まない） ---
        private string _prefabProjectFolder = "";

        // ExportFolder 開始時点の選択を固定する。一括出力の途中で Selection が
        // 生成物へ移っても、全モデルを同じ親の下へ追加するため。
        private Transform _hierarchyParent;

        // --- 結果レポート ---
        private readonly HierarchyExportReport _report = new HierarchyExportReport();

        public HierarchyPrefabExporter(HierarchyExportOptions options)
        {
            _opt = options ?? new HierarchyExportOptions();
        }

        // ================================================================
        // 入口
        // ================================================================

        /// <summary>
        /// 指定フォルダ自身がモデル（model.csv あり）なら単体、そうでなければ配下のモデルを全て書き出す。
        /// ダイアログは出さない。
        /// </summary>
        public HierarchyExportOutcome ExportFolder(string folderPath)
        {
            var outcome = new HierarchyExportOutcome();

            if (!_opt.AddToHierarchy && !_opt.SaveAsPrefab)
            {
                outcome.Title = "エラー";
                outcome.Text = "「ヒエラルキーに追加」または「プレファブとして保存」を選択してください。";
                return outcome;
            }

            _hierarchyParent = ResolveSelectedSceneTransform();

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                outcome.Title = "エラー";
                outcome.Text  = "フォルダが存在しません:\n" + folderPath;
                return outcome;
            }

            if (!File.Exists(Path.Combine(folderPath, "model.csv")))
            {
                ExportAllModelsUnder(folderPath, outcome);
                return outcome;
            }

            // 単体のモデルフォルダ指定でも、親がプロジェクトフォルダなら名前を挟む。
            _prefabProjectFolder = ResolveProjectFolderName(
                Path.GetDirectoryName(Path.GetFullPath(folderPath)));
            try
            {
                outcome.Success = ExportSingleModel(folderPath, outcome, single: true);
            }
            finally
            {
                _prefabProjectFolder = "";
            }

            return outcome;
        }

        /// <summary>
        /// メモリ上のモデル群を書き出す（リモートから受け取ったプロジェクト用）。ファイルは介さない。
        /// 1 件なら単体、複数なら一括と同じ文言で outcome を埋める。ダイアログは出さない。
        /// </summary>
        /// <param name="projectName">出力パスに挟むプロジェクト名。空なら挟まない。</param>
        public HierarchyExportOutcome ExportModels(string projectName, IList<ModelContext> models)
        {
            var outcome = new HierarchyExportOutcome();

            if (!_opt.AddToHierarchy && !_opt.SaveAsPrefab)
            {
                outcome.Title = "エラー";
                outcome.Text = "「ヒエラルキーに追加」または「プレファブとして保存」を選択してください。";
                return outcome;
            }

            if (models == null || models.Count == 0)
            {
                outcome.Title = "エラー";
                outcome.Text  = "書き出すモデルがありません。";
                return outcome;
            }

            _hierarchyParent = ResolveSelectedSceneTransform();

            _prefabProjectFolder = string.IsNullOrWhiteSpace(projectName) ? "" : SanitizeName(projectName);
            try
            {
                if (models.Count == 1)
                {
                    _report.Reset();
                    outcome.Success = ExportLoadedModel(models[0], outcome, single: true);
                    return outcome;
                }

                int ok = 0;
                var failed   = new List<string>();
                var problems = new List<string>();

                foreach (var model in models)
                {
                    string name = string.IsNullOrEmpty(model?.Name) ? "(名称なし)" : model.Name;

                    _report.Reset();
                    if (model != null && ExportLoadedModel(model, outcome, single: false))
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

                FillBatchOutcome(outcome, models.Count, ok, failed, problems);
                return outcome;
            }
            finally
            {
                _prefabProjectFolder = "";
            }
        }

        // ================================================================
        // ロード → 書き出し
        // ================================================================

        /// <summary>
        /// フォルダ配下（1階層下）のモデルフォルダを全て書き出す。
        /// モデルフォルダの判定は model.csv の有無。
        /// </summary>
        private void ExportAllModelsUnder(string projectFolder, HierarchyExportOutcome outcome)
        {
            var modelFolders = new List<string>();
            foreach (string dir in Directory.GetDirectories(projectFolder))
                if (File.Exists(Path.Combine(dir, "model.csv"))) modelFolders.Add(dir);

            if (modelFolders.Count == 0)
            {
                outcome.Title = "エラー";
                outcome.Text  =
                    "モデルが見つかりません（フォルダ自身にも配下にも model.csv がありません）:\n" + projectFolder;
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

                    if (ExportSingleModel(dir, outcome, single: false))
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

            FillBatchOutcome(outcome, modelFolders.Count, ok, failed, problems);
        }

        /// <summary>一括書き出しのまとめの文言で outcome を埋める。</summary>
        private static void FillBatchOutcome(
            HierarchyExportOutcome outcome, int total, int ok,
            List<string> failed, List<string> problems)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{total} 件中 {ok} 件を書き出しました。");

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

            outcome.Success = failed.Count == 0;
            outcome.Title   = failed.Count > 0 ? "一括エクスポート（失敗あり）" : "一括エクスポート";
            outcome.Text    = msg;
        }

        /// <summary>モデルフォルダ1件を書き出す。成功したら true。</summary>
        /// <param name="single">
        /// 単体書き出しのとき true。outcome の Title / Text をこのモデルのレポートで埋める。
        /// 一括のときは false（まとめの文言を呼び出し側が作る）。
        /// </param>
        private bool ExportSingleModel(string modelFolder, HierarchyExportOutcome outcome, bool single)
        {
            // レポートはモデル1件ごとに作り直す。
            _report.Reset();

            // out パラメータ（EditorState / WorkPlane / 追加エントリ）は本処理では不要のため破棄。
            ModelContext model = CsvModelSerializer.LoadModel(modelFolder, out _, out _, out _);
            if (model == null)
            {
                string m = "モデルの読み込みに失敗しました（model.csv 不在など）:\n" + modelFolder;
                _report.Error(m);
                if (single)
                {
                    outcome.Title = "エラー";
                    outcome.Text  = m;
                }
                return false;
            }

            return ExportLoadedModel(model, outcome, single);
        }

        /// <summary>
        /// 読み込み済みの ModelContext 1 件を書き出す。成功したら true。
        /// フォルダからの書き出しとメモリ上のモデル（ExportModels）の共通部。
        /// レポートの Reset は呼び出し側で行う。
        /// </summary>
        private bool ExportLoadedModel(ModelContext model, HierarchyExportOutcome outcome, bool single)
        {
            // 読込直後の ModelContext はボーンの WorldMatrix が未計算。
            model.ComputeWorldMatrices();

            WarnAboutExpectations(model);

            if (_opt.SaveAsPrefab)
            {
                bool prefabOk = ExportAsPrefab(model, outcome);
                if (single) FillReportOutcome(outcome, model, prefabOk);
                return prefabOk;
            }

            var root = Export(model);
            if (root != null)
            {
                root = AddGeneratedRootToHierarchy(root);

                // シーン出力ではアセットを一切作らないため Avatar は生成せず、
                // 空の Animator（avatar 未設定）のみを付与する。
                if (_opt.SceneAnimator || _opt.AnimatorController != null)
                {
                    var animator = root.GetComponent<Animator>();
                    if (animator == null) animator = Undo.AddComponent<Animator>(root);
                    if (_opt.AnimatorController != null)
                        animator.runtimeAnimatorController = _opt.AnimatorController;
                }

                UnityEditor.Selection.activeGameObject = root;
                EditorGUIUtility.PingObject(root);
            }
            else
            {
                _report.Error("ヒエラルキー生成に失敗しました。");
            }

            if (single) FillReportOutcome(outcome, model, root != null);
            return root != null;
        }

        /// <summary>単体書き出しの結果文言。成功・失敗どちらでもレポートを入れる。</summary>
        private void FillReportOutcome(HierarchyExportOutcome outcome, ModelContext model, bool ok)
        {
            string modelName = string.IsNullOrEmpty(model?.Name) ? "(名称なし)" : model.Name;

            outcome.Title = !ok                ? "書き出し失敗"
                          : _report.HasProblem ? "書き出し完了（警告あり）"
                                               : "書き出し完了";

            string header = ok
                ? $"{modelName} を書き出しました。"
                : $"{modelName} の書き出しに失敗しました。";

            outcome.Text = _report.BuildDialogText(header);
        }

        /// <summary>
        /// レポートをエクスポート先フォルダへ export_report.txt として書き出す。
        ///   書き出し自体の失敗は本体の成否に影響させない（ログに落とすだけ）。
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

        /// <summary>プレファブとして保存する。成功したら true。保存先は outcome.PrefabPaths に足す。</summary>
        private bool ExportAsPrefab(ModelContext model, HierarchyExportOutcome outcome)
        {
            string modelName = SanitizeName(model.Name ?? "Model");
            string outputRoot   = NormalizeOutputRoot(_opt.PrefabOutputRoot);
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
            bool keepHierarchyObject = false;
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
                if (_opt.BuildAvatar)
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
                        if (_opt.SupplementHumanoid)
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

                            // リターゲット設定8項目はモデルが持つ（未設定なら Unity 既定）。
                            var avRetarget = AvatarRetargetSettings.FromData(model.AvatarRetarget);

                            var avatar = AvatarBuildCore.BuildAndSaveAvatar(
                                root, avMap, avLimits, avRetarget, avatarPath,
                                m => _report.Log(m));

                            _report.AvatarResult = avatar != null
                                ? $"生成しました（{avMap.Count} ボーン）"
                                : "生成に失敗";
                            if (avatar == null) _report.Warn("Avatar の生成に失敗しました。");

                            if (_opt.AttachAnimator)
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
                //   root はプレファブ化後に破棄する一時オブジェクトのため Undo 登録しない。
                if (_opt.AnimatorController != null)
                {
                    var animator = root.GetComponent<Animator>();
                    if (animator == null) animator = root.AddComponent<Animator>();
                    animator.runtimeAnimatorController = _opt.AnimatorController;
                    Debug.Log($"[HierarchyExport] Animator Controller を設定: {_opt.AnimatorController.name}");
                }

                // 同名プレファブへ上書き保存（繰り返しても増えない）
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                AssetDatabase.SaveAssets();

                if (!_opt.AddToHierarchy)
                {
                    UnityEditor.Selection.activeObject = prefab;
                    EditorGUIUtility.PingObject(prefab);
                }
                _report.Log($"プレファブ保存: {prefabPath}（材料アセット {matCount} / テクスチャ {texCount}）");

                if (prefab == null) _report.Error("プレファブの保存に失敗しました: " + prefabPath);
                else outcome.PrefabPaths.Add(prefabPath);

                // IK 付帯を attach.csv でプレファブ同居出力（案X: Humanoid/HumanLimit は Avatar が正）
                if (_opt.WriteAttach)
                {
                    AttachSidecarCsv.Write(model, $"{baseDir}/attach.csv");
                    AssetDatabase.Refresh();
                }

                // 警告・エラーをプレファブと同じフォルダへテキストで残す。
                WriteExportReportFile(model, baseDir, prefab != null);

                bool hierarchyOk = true;
                if (_opt.AddToHierarchy)
                {
                    root = AddGeneratedRootToHierarchy(root);
                    hierarchyOk = root != null;
                    keepHierarchyObject = hierarchyOk;

                    if (hierarchyOk)
                    {
                        UnityEditor.Selection.activeGameObject = root;
                        EditorGUIUtility.PingObject(root);
                    }
                }

                return prefab != null && hierarchyOk;
            }
            finally
            {
                _prefabExportActive = false;
                _meshesDir = "";
                // Hierarchyにも追加する場合は生成物を残す。Prefabだけなら一時ルートを破棄する。
                if (root != null && !keepHierarchyObject)
                    UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // ================================================================
        // Hierarchy への追加
        // ================================================================

        /// <summary>
        /// ExportFolder 開始時に選択されていた、シーン上の GameObject を親候補として返す。
        /// Project 上の Prefab アセットなどは親にできないため除外する。
        /// </summary>
        private static Transform ResolveSelectedSceneTransform()
        {
            var selected = UnityEditor.Selection.activeGameObject;
            if (selected == null) return null;
            if (EditorUtility.IsPersistent(selected)) return null;
            if (!selected.scene.IsValid()) return null;
            return selected.transform;
        }

        /// <summary>
        /// 生成物を選択中 GameObject の子へ追加する。
        /// 直下の子が1個だけでモデル用ルートに固有部品が無い場合は、その子を直接追加する。
        /// </summary>
        private GameObject AddGeneratedRootToHierarchy(GameObject modelRoot)
        {
            if (modelRoot == null) return null;

            GameObject result = modelRoot;

            if (_opt.DirectSingleObjectToHierarchy &&
                modelRoot.transform.childCount == 1 &&
                CanRemoveModelRoot(modelRoot))
            {
                var child = modelRoot.transform.GetChild(0);
                CopyRootAnimator(modelRoot, child.gameObject);

                Vector3 localPosition = child.localPosition;
                Quaternion localRotation = child.localRotation;
                Vector3 localScale = child.localScale;

                Undo.SetTransformParent(
                    child, _hierarchyParent,
                    "PolyLing: Add Single Object to Hierarchy");
                Undo.RecordObject(child, "PolyLing: Restore Local Transform");
                child.localPosition = localPosition;
                child.localRotation = localRotation;
                child.localScale = localScale;

                Undo.DestroyObjectImmediate(modelRoot);
                result = child.gameObject;

                _report.Note("直下のオブジェクトが1個のため、モデル用ルートを省略しました。");
            }
            else
            {
                Vector3 localPosition = modelRoot.transform.localPosition;
                Quaternion localRotation = modelRoot.transform.localRotation;
                Vector3 localScale = modelRoot.transform.localScale;

                Undo.SetTransformParent(
                    modelRoot.transform, _hierarchyParent,
                    "PolyLing: Add Model to Hierarchy");
                Undo.RecordObject(modelRoot.transform, "PolyLing: Restore Local Transform");
                modelRoot.transform.localPosition = localPosition;
                modelRoot.transform.localRotation = localRotation;
                modelRoot.transform.localScale = localScale;

                if (_opt.DirectSingleObjectToHierarchy &&
                    modelRoot.transform.childCount == 1)
                {
                    _report.Note(
                        "モデル用ルートに保持すべきコンポーネントがあるため、ルートを省略しませんでした。");
                }
            }

            return result;
        }

        /// <summary>
        /// Transform と Animator 以外が付いているルートは省略しない。
        /// Rigidbody、Collider、ユーザースクリプトなどを失わないためである。
        /// </summary>
        private static bool CanRemoveModelRoot(GameObject modelRoot)
        {
            foreach (var component in modelRoot.GetComponents<Component>())
            {
                if (component == null) return false;
                if (component is Transform) continue;
                if (component is Animator) continue;
                return false;
            }
            return true;
        }

        /// <summary>省略するモデル用ルートの Animator 設定を唯一の子へ移す。</summary>
        private static void CopyRootAnimator(GameObject sourceRoot, GameObject destination)
        {
            var source = sourceRoot.GetComponent<Animator>();
            if (source == null) return;

            var target = destination.GetComponent<Animator>();
            if (target == null)
                target = Undo.AddComponent<Animator>(destination);

            target.avatar = source.avatar;
            target.runtimeAnimatorController = source.runtimeAnimatorController;
            target.applyRootMotion = source.applyRootMotion;
            target.updateMode = source.updateMode;
            target.cullingMode = source.cullingMode;
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

        /// <summary>テクスチャ1枚を texturesDir へ書き出し、そのアセットパスを返す。失敗時は null。</summary>
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

        /// <summary>空・Assets 外・末尾スラッシュを正規化して "Assets/..." 形式へ揃える。</summary>
        public static string NormalizeOutputRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return DefaultPrefabOutputRoot;

            string p = path.Replace('\\', '/').TrimEnd('/');
            if (p.Length == 0) return DefaultPrefabOutputRoot;
            if (p != "Assets" && !p.StartsWith("Assets/")) return DefaultPrefabOutputRoot;

            return p;
        }

        /// <summary>絶対パス → "Assets/..." 形式。Assets 外なら null。</summary>
        public static string ToAssetsRelative(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return null;

            string abs  = absolutePath.Replace('\\', '/').TrimEnd('/');
            string data = Application.dataPath.Replace('\\', '/').TrimEnd('/');

            if (abs == data) return "Assets";
            if (abs.StartsWith(data + "/")) return "Assets" + abs.Substring(data.Length);

            return null;
        }

        /// <summary>
        /// プロジェクトフォルダから出力パスに挟む名前を決める。
        /// project.csv の name 行を優先し、無ければフォルダ名を使う。
        /// プロジェクトと判定できない場合は空文字（＝挟まない）。
        /// </summary>
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

        /// <summary>ファイル名に使えない文字を '_' に置換。</summary>
        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Model";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        /// <summary>メッシュ .asset のパス（同一 export 内の同名衝突は _n を付与）。</summary>
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
        /// ミラー・ウェイトに関する判定は Runtime 側（HierarchyBuilder.WarnAboutExpectations）が持つ。
        /// Avatar 生成は Editor でしか行わないため、その分だけここで足す。
        /// </summary>
        private void WarnAboutExpectations(ModelContext model)
        {
            if (model == null) return;

            var pre = new HierarchyBuildResult();
            new HierarchyBuilder(BuildHierarchyOptions()).WarnAboutExpectations(model, pre);
            foreach (string w in pre.Warnings) _report.Warn(w);

            if (_opt.SaveAsPrefab && _opt.BuildAvatar &&
                (model.HumanoidMapping == null || model.HumanoidMapping.IsEmpty))
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

        /// <summary>ModelContext を Unity ヒエラルキーに書き出し、ルート GameObject を返す。</summary>
        private GameObject Export(ModelContext model)
        {
            // 書き出し専用の Undo グループにする。失敗時にこのグループだけを取り消すため。
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("PolyLing: Export to Hierarchy");
            int undoGroup = Undo.GetCurrentGroup();

            // プレファブ保存のときだけメッシュを共有アセット化する。
            System.Func<Mesh, Mesh> persistMesh = _prefabExportActive
                ? (m => MeshAssetUtil.SaveDeterministic(m, ResolveMeshAssetPath(m.name)))
                : (System.Func<Mesh, Mesh>)null;

            try
            {
                _build = new HierarchyBuilder(BuildHierarchyOptions(), persistMesh).Build(model);
            }
            catch
            {
                // Build は GameObject を Undo 登録しながら作る（HierarchyBuilder.cs のルート生成など）。
                // 途中で例外が出ると作りかけがシーンに残るので、このグループごと取り消す。
                Undo.RevertAllDownToGroup(undoGroup);
                throw;
            }

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

        /// <summary>オプションから Runtime 側の生成設定を組む。</summary>
        private HierarchyBuildOptions BuildHierarchyOptions()
        {
            return new HierarchyBuildOptions
            {
                CreateArmature            = _opt.CreateArmature,
                UseBindpose               = _opt.UseBindpose,
                ExportVisibleOnly         = _opt.ExportVisibleOnly,
                IncludeInvisibleAncestors = _opt.IncludeInvisibleAncestors,
                ExportMeshOnly            = _opt.ExportMeshOnly,
                ExportPhysics             = _opt.ExportPhysics,
                TolerantMirrorBranch      = _opt.TolerantMirrorBranch,
                RendererMode              = _opt.RendererMode,
            };
        }
    }
}
