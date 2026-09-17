// Editor/HierarchyIO/HierarchyExportWindow.cs
// ============================================================
// 段階A：エクスポートエディタ拡張（プロジェクトファイル → Unityヒエラルキー）
// ============================================================
//
// 【役割】
//   IMGUI・EditorPrefs・書き込み先の保存・結果ダイアログだけを持つ。
//   書き出しの本体は HierarchyPrefabExporter（同フォルダ）にある。
//   MCP（PolyLingEditorControlServer の prefab_export）も同じ本体を呼ぶため、
//   ダイアログはここでしか出さない。
//
// 【移植元】
//   旧 LiteHierarchyExportSubPanel.Export（現行 ModelContext API 準拠）。
//
// 【出力構造】
//   HierarchyBuilder.cs 冒頭を参照。ここには書き写さない（二重管理になるため）。
//
// ============================================================

using UnityEditor;
using UnityEngine;
using Poly_Ling.Core;
using Poly_Ling.EditorTools;
using Poly_Ling.HierarchyIO;

namespace Poly_Ling.EditorIO
{
    /// <summary>
    /// プロジェクトファイル（フォルダ形式）を読み込み、Unityヒエラルキーへ書き出すエディタ拡張。
    /// </summary>
    public class HierarchyExportWindow : EditorWindow
    {
        // 入力
        private string _modelFolderPath = "";

        // オプション。既定値は HierarchyExportOptions が持つ。
        private readonly HierarchyExportOptions _opt = new HierarchyExportOptions();

        // --- レンダラ種別の UI ラベル（種別の定義は Runtime の HierarchyRendererMode） ---
        private static readonly string[] RendererModeLabels =
            { "自動（ウェイト有無で判定）", "MeshFilter を強制" };

        // --- ミラー分岐の許容モード（EditorPrefs で保持・既定は許容） ------
        private const string PrefsKeyTolerantMirrorBranch = "PolyLing.HierarchyExport.TolerantMirrorBranch";

        // --- Animator Controller（任意・EditorPrefs にアセットパスで保持） ---
        private const string PrefsKeyAnimatorController = "PolyLing.HierarchyExport.AnimatorController";


        [MenuItem("PolyLing/IO/Hierarchy Export (Project File → Hierarchy)")]
        public static void Open()
        {
            GetWindow<HierarchyExportWindow>(true, "Hierarchy Export", true);
        }

        private void OnEnable()
        {
            // 書き込み先の置き場は settings（RecentPaths）。規約は SaveDest.cs を参照。
            _opt.PrefabOutputRoot = HierarchyPrefabExporter.NormalizeOutputRoot(
                EditorSaveDestField.Load(SaveDest.Keys.HierarchyPrefab, HierarchyPrefabExporter.DefaultPrefabOutputRoot));

            string acPath = EditorPrefs.GetString(PrefsKeyAnimatorController, "");
            _opt.AnimatorController = string.IsNullOrEmpty(acPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(acPath);

            _opt.TolerantMirrorBranch = EditorPrefs.GetBool(PrefsKeyTolerantMirrorBranch, true);
        }

        private void OnDisable()
        {
            SaveOutputRootPref();
            SaveAnimatorControllerPref();
            EditorPrefs.SetBool(PrefsKeyTolerantMirrorBranch, _opt.TolerantMirrorBranch);
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
            _opt.CreateArmature    = EditorGUILayout.Toggle("Armatureを生成", _opt.CreateArmature);
            _opt.UseBindpose       = EditorGUILayout.Toggle("BindPoseを使用", _opt.UseBindpose);
            _opt.ExportVisibleOnly = EditorGUILayout.Toggle("可視メッシュのみ", _opt.ExportVisibleOnly);
            using (new EditorGUI.DisabledScope(!_opt.ExportVisibleOnly))
            {
                _opt.IncludeInvisibleAncestors = EditorGUILayout.Toggle(
                    "不可視の親を補完", _opt.IncludeInvisibleAncestors);
            }
            if (_opt.ExportVisibleOnly && _opt.IncludeInvisibleAncestors)
            {
                EditorGUILayout.HelpBox(
                    "可視ノードの親をたどり、不可視の親も Transform のみのノードとして出力します"
                    + "（隠した形状は出力しません）。切ると子がルート直下へ平坦化されます。",
                    MessageType.None);
            }
            _opt.RendererMode = (HierarchyRendererMode)EditorGUILayout.Popup(
                "レンダラ", (int)_opt.RendererMode, RendererModeLabels);
            if (_opt.RendererMode == HierarchyRendererMode.ForceMeshFilter)
            {
                EditorGUILayout.HelpBox(
                    "ボーンウェイトを無視して MeshFilter + MeshRenderer で出力します"
                    + "（ボーン階層は Armature として出ます）。",
                    MessageType.None);
            }

            EditorGUI.BeginChangeCheck();
            _opt.TolerantMirrorBranch = EditorGUILayout.Toggle(
                "ミラー設定漏れを許容", _opt.TolerantMirrorBranch);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetBool(PrefsKeyTolerantMirrorBranch, _opt.TolerantMirrorBranch);
            if (_opt.TolerantMirrorBranch)
            {
                EditorGUILayout.HelpBox(
                    "ミラー分岐ルート配下は、個々のオブジェクトのミラー設定が無くても"
                    + "実体側から鏡像を作ってミラー枝へ出力します"
                    + "（軸・距離はオブジェクトごとの設定を使います）。\n"
                    + "切ると、ミラー側メッシュが実在するオブジェクトだけがミラー枝に出ます。",
                    MessageType.None);
            }

            _opt.ExportMeshOnly = EditorGUILayout.Toggle("メッシュのみ（ボーン除外）", _opt.ExportMeshOnly);
            _opt.ExportPhysics  = EditorGUILayout.Toggle("剛体/JOINTを出力", _opt.ExportPhysics);
            _opt.SaveAsPrefab   = EditorGUILayout.Toggle("プレファブとして保存", _opt.SaveAsPrefab);
            if (_opt.SaveAsPrefab)
            {
                // [...] は書き込み先フォルダを決めるだけ。ここで保存はしない。
                _opt.PrefabOutputRoot = EditorSaveDestField.Draw(
                    "書き込み先フォルダ", _opt.PrefabOutputRoot, SaveDest.Keys.HierarchyPrefab,
                    "プレファブの書き込み先（Assets 以下）", "model.prefab", "prefab",
                    MapPickedOutputRoot);

                _opt.BuildAvatar = EditorGUILayout.Toggle("Avatar も生成", _opt.BuildAvatar);
                if (_opt.BuildAvatar)
                {
                    _opt.AttachAnimator     = EditorGUILayout.Toggle("Animator を付与して割当", _opt.AttachAnimator);
                    _opt.SupplementHumanoid = EditorGUILayout.Toggle("不足関節を補完", _opt.SupplementHumanoid);
                }
                _opt.WriteAttach = EditorGUILayout.Toggle("IK付帯(attach.csv)も出力", _opt.WriteAttach);
                EditorGUILayout.HelpBox(
                    HierarchyPrefabExporter.NormalizeOutputRoot(_opt.PrefabOutputRoot) +
                    "/<モデル名>/ にメッシュ/マテリアルを共有アセット化し、" +
                    "同名プレファブへ上書き保存します（繰り返しても増えません）。" +
                    (_opt.BuildAvatar ? "\nHumanoid 割当から Avatar(.asset) も同時生成します。" : "") +
                    (_opt.BuildAvatar && _opt.AttachAnimator
                        ? "\n生成した Avatar を Animator に割り当ててからプレファブ化します。" : "") +
                    (_opt.BuildAvatar && _opt.SupplementHumanoid
                        ? "\n不足する必須関節をダミーの空オブジェクトで補ってから生成します。" : ""),
                    MessageType.Info);
            }
            else
            {
                _opt.SceneAnimator = EditorGUILayout.Toggle("空の Animator を付与", _opt.SceneAnimator);
                EditorGUILayout.HelpBox(
                    "シーンに書き出します。アセット・ファイルは一切生成しません。" +
                    (_opt.SceneAnimator ? "\nルートに Animator を付与します（Avatar は未設定）。" : ""),
                    MessageType.Info);
            }

            // Animator Controller（任意・プレファブ／シーン共通）
            EditorGUI.BeginChangeCheck();
            _opt.AnimatorController = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                "Animator Controller", _opt.AnimatorController,
                typeof(RuntimeAnimatorController), allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck()) SaveAnimatorControllerPref();

            if (_opt.AnimatorController != null)
            {
                EditorGUILayout.HelpBox(
                    "ルートの Animator に上記コントローラを設定します（未指定なら設定しません）。",
                    MessageType.None);
            }

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_modelFolderPath)))
            {
                string label = _opt.SaveAsPrefab ? "ロードしてプレファブに保存" : "ロードしてヒエラルキーに書き出し";
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
        /// その時点のウィンドウのオプション設定がそのまま使われ、結果ダイアログも出る。
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
            var outcome = new HierarchyPrefabExporter(_opt.Clone()).ExportFolder(_modelFolderPath);
            EditorUtility.DisplayDialog(outcome.Title, outcome.Text, "OK");
        }

        // ================================================================
        // 設定の保存
        // ================================================================

        /// <summary>
        /// [...] で選ばれたフォルダを Assets 相対へ直す。
        /// Assets の外なら理由を出して採用しない（null を返す）。
        /// </summary>
        private string MapPickedOutputRoot(string absoluteFolder)
        {
            string rel = HierarchyPrefabExporter.ToAssetsRelative(absoluteFolder);
            if (rel == null)
            {
                EditorUtility.DisplayDialog(
                    "エラー",
                    "書き込み先はこのプロジェクトの Assets フォルダ以下を指定してください:\n" + absoluteFolder,
                    "OK");
                return null;
            }
            return HierarchyPrefabExporter.NormalizeOutputRoot(rel);
        }

        private void SaveOutputRootPref()
        {
            SaveDest.SetFolder(SaveDest.Keys.HierarchyPrefab,
                HierarchyPrefabExporter.NormalizeOutputRoot(_opt.PrefabOutputRoot));
        }

        private void SaveAnimatorControllerPref()
        {
            string path = _opt.AnimatorController != null
                ? AssetDatabase.GetAssetPath(_opt.AnimatorController)
                : "";
            EditorPrefs.SetString(PrefsKeyAnimatorController, path ?? "");
        }
    }
}
