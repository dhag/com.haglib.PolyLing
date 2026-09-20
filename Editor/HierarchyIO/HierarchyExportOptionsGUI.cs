// Editor/HierarchyIO/HierarchyExportOptionsGUI.cs
// ============================================================
// ヒエラルキー書き出しオプションの IMGUI と設定の読み書き（共有部品）
// ============================================================
//
// 【使う窓】
//   HierarchyExportWindow        … プロジェクトファイル → ヒエラルキー
//   HierarchyRemoteExportWindow  … リモートのプロジェクト → ヒエラルキー
//   両者は同じ設定（EditorPrefs / SaveDest）を読み書きするため、どちらで変えても共通になる。
//
// 【持たないもの】
//   入力元（モデルフォルダ・接続）と実行ボタンは各窓が持つ。
//   書き出し本体は HierarchyPrefabExporter。
// ============================================================

using UnityEditor;
using UnityEngine;
using Poly_Ling.Core;
using Poly_Ling.EditorTools;
using Poly_Ling.HierarchyIO;

namespace Poly_Ling.EditorIO
{
    public static class HierarchyExportOptionsGUI
    {
        // --- レンダラ種別の UI ラベル（種別の定義は Runtime の HierarchyRendererMode） ---
        private static readonly string[] RendererModeLabels =
            { "自動（ウェイト有無で判定）", "MeshFilter を強制" };

        // --- ミラー分岐の許容モード（EditorPrefs で保持・既定は許容） ------
        private const string PrefsKeyTolerantMirrorBranch = "PolyLing.HierarchyExport.TolerantMirrorBranch";

        // --- Animator Controller（任意・EditorPrefs にアセットパスで保持） ---
        private const string PrefsKeyAnimatorController = "PolyLing.HierarchyExport.AnimatorController";

        // ================================================================
        // 設定の読み書き
        // ================================================================

        /// <summary>保存済みの設定を opt へ読み込む（窓の OnEnable で呼ぶ）。</summary>
        public static void Load(HierarchyExportOptions opt)
        {
            // 書き込み先の置き場は settings（RecentPaths）。規約は SaveDest.cs を参照。
            opt.PrefabOutputRoot = HierarchyPrefabExporter.NormalizeOutputRoot(
                EditorSaveDestField.Load(SaveDest.Keys.HierarchyPrefab, HierarchyPrefabExporter.DefaultPrefabOutputRoot));

            string acPath = EditorPrefs.GetString(PrefsKeyAnimatorController, "");
            opt.AnimatorController = string.IsNullOrEmpty(acPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(acPath);

            opt.TolerantMirrorBranch = EditorPrefs.GetBool(PrefsKeyTolerantMirrorBranch, true);
        }

        /// <summary>opt の設定を保存する（窓の OnDisable で呼ぶ）。</summary>
        public static void Save(HierarchyExportOptions opt)
        {
            SaveOutputRoot(opt);
            SaveAnimatorController(opt);
            EditorPrefs.SetBool(PrefsKeyTolerantMirrorBranch, opt.TolerantMirrorBranch);
        }

        // ================================================================
        // UI（IMGUI）
        // ================================================================

        /// <summary>オプション欄を描く。入力元の欄の下、実行ボタンの上に置く。</summary>
        public static void Draw(HierarchyExportOptions opt)
        {
            opt.CreateArmature    = EditorGUILayout.Toggle("Armatureを生成", opt.CreateArmature);
            opt.UseBindpose       = EditorGUILayout.Toggle("BindPoseを使用", opt.UseBindpose);
            opt.ExportVisibleOnly = EditorGUILayout.Toggle("可視メッシュのみ", opt.ExportVisibleOnly);
            using (new EditorGUI.DisabledScope(!opt.ExportVisibleOnly))
            {
                opt.IncludeInvisibleAncestors = EditorGUILayout.Toggle(
                    "不可視の親を補完", opt.IncludeInvisibleAncestors);
            }
            if (opt.ExportVisibleOnly && opt.IncludeInvisibleAncestors)
            {
                EditorGUILayout.HelpBox(
                    "可視ノードの親をたどり、不可視の親も Transform のみのノードとして出力します"
                    + "（隠した形状は出力しません）。切ると子がルート直下へ平坦化されます。",
                    MessageType.None);
            }
            opt.RendererMode = (HierarchyRendererMode)EditorGUILayout.Popup(
                "レンダラ", (int)opt.RendererMode, RendererModeLabels);
            if (opt.RendererMode == HierarchyRendererMode.ForceMeshFilter)
            {
                EditorGUILayout.HelpBox(
                    "ボーンウェイトを無視して MeshFilter + MeshRenderer で出力します"
                    + "（ボーン階層は Armature として出ます）。",
                    MessageType.None);
            }

            EditorGUI.BeginChangeCheck();
            opt.TolerantMirrorBranch = EditorGUILayout.Toggle(
                "ミラー設定漏れを許容", opt.TolerantMirrorBranch);
            if (EditorGUI.EndChangeCheck())
                EditorPrefs.SetBool(PrefsKeyTolerantMirrorBranch, opt.TolerantMirrorBranch);
            if (opt.TolerantMirrorBranch)
            {
                EditorGUILayout.HelpBox(
                    "ミラー分岐ルート配下は、個々のオブジェクトのミラー設定が無くても"
                    + "実体側から鏡像を作ってミラー枝へ出力します"
                    + "（軸・距離はオブジェクトごとの設定を使います）。\n"
                    + "切ると、ミラー側メッシュが実在するオブジェクトだけがミラー枝に出ます。",
                    MessageType.None);
            }

            opt.ExportMeshOnly = EditorGUILayout.Toggle("メッシュのみ（ボーン除外）", opt.ExportMeshOnly);
            opt.ExportPhysics  = EditorGUILayout.Toggle("剛体/JOINTを出力", opt.ExportPhysics);
            opt.SaveAsPrefab   = EditorGUILayout.Toggle("プレファブとして保存", opt.SaveAsPrefab);
            if (opt.SaveAsPrefab)
            {
                // [...] は書き込み先フォルダを決めるだけ。ここで保存はしない。
                opt.PrefabOutputRoot = EditorSaveDestField.Draw(
                    "書き込み先フォルダ", opt.PrefabOutputRoot, SaveDest.Keys.HierarchyPrefab,
                    "プレファブの書き込み先（Assets 以下）", "model.prefab", "prefab",
                    MapPickedOutputRoot);

                opt.BuildAvatar = EditorGUILayout.Toggle("Avatar も生成", opt.BuildAvatar);
                if (opt.BuildAvatar)
                {
                    opt.AttachAnimator     = EditorGUILayout.Toggle("Animator を付与して割当", opt.AttachAnimator);
                    opt.SupplementHumanoid = EditorGUILayout.Toggle("不足関節を補完", opt.SupplementHumanoid);
                }
                opt.WriteAttach = EditorGUILayout.Toggle("IK付帯(attach.csv)も出力", opt.WriteAttach);
                EditorGUILayout.HelpBox(
                    HierarchyPrefabExporter.NormalizeOutputRoot(opt.PrefabOutputRoot) +
                    "/<モデル名>/ にメッシュ/マテリアルを共有アセット化し、" +
                    "同名プレファブへ上書き保存します（繰り返しても増えません）。" +
                    (opt.BuildAvatar ? "\nHumanoid 割当から Avatar(.asset) も同時生成します。" : "") +
                    (opt.BuildAvatar && opt.AttachAnimator
                        ? "\n生成した Avatar を Animator に割り当ててからプレファブ化します。" : "") +
                    (opt.BuildAvatar && opt.SupplementHumanoid
                        ? "\n不足する必須関節をダミーの空オブジェクトで補ってから生成します。" : ""),
                    MessageType.Info);
            }
            else
            {
                opt.SceneAnimator = EditorGUILayout.Toggle("空の Animator を付与", opt.SceneAnimator);
                EditorGUILayout.HelpBox(
                    "シーンに書き出します。アセット・ファイルは一切生成しません。" +
                    (opt.SceneAnimator ? "\nルートに Animator を付与します（Avatar は未設定）。" : ""),
                    MessageType.Info);
            }

            // Animator Controller（任意・プレファブ／シーン共通）
            EditorGUI.BeginChangeCheck();
            opt.AnimatorController = (RuntimeAnimatorController)EditorGUILayout.ObjectField(
                "Animator Controller", opt.AnimatorController,
                typeof(RuntimeAnimatorController), allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck()) SaveAnimatorController(opt);

            if (opt.AnimatorController != null)
            {
                EditorGUILayout.HelpBox(
                    "ルートの Animator に上記コントローラを設定します（未指定なら設定しません）。",
                    MessageType.None);
            }
        }

        // ================================================================
        // 内部
        // ================================================================

        /// <summary>
        /// [...] で選ばれたフォルダを Assets 相対へ直す。
        /// Assets の外なら理由を出して採用しない（null を返す）。
        /// </summary>
        private static string MapPickedOutputRoot(string absoluteFolder)
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

        private static void SaveOutputRoot(HierarchyExportOptions opt)
        {
            SaveDest.SetFolder(SaveDest.Keys.HierarchyPrefab,
                HierarchyPrefabExporter.NormalizeOutputRoot(opt.PrefabOutputRoot));
        }

        private static void SaveAnimatorController(HierarchyExportOptions opt)
        {
            string path = opt.AnimatorController != null
                ? AssetDatabase.GetAssetPath(opt.AnimatorController)
                : "";
            EditorPrefs.SetString(PrefsKeyAnimatorController, path ?? "");
        }
    }
}
