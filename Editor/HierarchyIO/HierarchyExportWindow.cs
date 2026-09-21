// Editor/HierarchyIO/HierarchyExportWindow.cs
// ============================================================
// 段階A：エクスポートエディタ拡張（プロジェクトファイル → Unityヒエラルキー）
// ============================================================
//
// 【役割】
//   入力元（モデルフォルダ）・実行ボタン・結果ダイアログだけを持つ。
//   オプション欄と設定の読み書きは HierarchyExportOptionsGUI（同フォルダ・共有部品）。
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
        private readonly HierarchyExportOptions _opt = new HierarchyExportOptions
        {
            AddToHierarchy = true,
        };


        [MenuItem("PolyLing/IO/Hierarchy Export (Project File → Hierarchy)")]
        public static void Open()
        {
            GetWindow<HierarchyExportWindow>(true, "Hierarchy Export", true);
        }

        private void OnEnable()
        {
            HierarchyExportOptionsGUI.Load(_opt);
        }

        private void OnDisable()
        {
            HierarchyExportOptionsGUI.Save(_opt);
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
            HierarchyExportOptionsGUI.Draw(_opt);

            EditorGUILayout.Space();
            bool hasOutput = _opt.AddToHierarchy || _opt.SaveAsPrefab;
            using (new EditorGUI.DisabledScope(
                       string.IsNullOrEmpty(_modelFolderPath) || !hasOutput))
            {
                string label = BuildExecuteLabel();
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

        private string BuildExecuteLabel()
        {
            if (_opt.AddToHierarchy && _opt.SaveAsPrefab)
                return "ロードしてプレファブ保存＋ヒエラルキー追加";
            if (_opt.SaveAsPrefab)
                return "ロードしてプレファブに保存";
            if (_opt.AddToHierarchy)
                return "ロードしてヒエラルキーに追加";
            return "出力方法を選択してください";
        }
    }
}
