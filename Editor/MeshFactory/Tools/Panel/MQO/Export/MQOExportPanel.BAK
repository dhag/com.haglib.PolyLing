// Assets/Editor/MeshFactory/MQO/Export/MQOExportPanel.cs
// MQOエクスポートパネル

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Model;
using MeshFactory.Localization;

using MeshContext = SimpleMeshFactory.MeshContext;

namespace MeshFactory.MQO
{
    /// <summary>
    /// MQOエクスポートパネル
    /// </summary>
    public class MQOExportPanel : Tools.IToolPanelBase
    {
        // ================================================================
        // 定数・ローカライズ
        // ================================================================

        public override string Name => "MQOExport";
        public override string Title => "MQO Export";

        /// <summary>
        /// ローカライズされたタイトルを取得
        /// </summary>
        public override string GetLocalizedTitle() => T("WindowTitle");

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "MQO Export", ["ja"] = "MQOエクスポート" },
            ["File"] = new() { ["en"] = "File", ["ja"] = "ファイル" },
            ["OutputPath"] = new() { ["en"] = "Output Path", ["ja"] = "出力先" },
            ["Browse"] = new() { ["en"] = "Browse...", ["ja"] = "参照..." },
            ["Export"] = new() { ["en"] = "Export", ["ja"] = "エクスポート" },

            // 設定セクション
            ["Settings"] = new() { ["en"] = "Settings", ["ja"] = "設定" },
            ["Coordinate"] = new() { ["en"] = "Coordinate", ["ja"] = "座標系" },
            ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール" },
            ["SwapYZ"] = new() { ["en"] = "Swap Y/Z Axis", ["ja"] = "Y/Z軸入れ替え" },
            ["FlipZAxis"] = new() { ["en"] = "Flip Z Axis", ["ja"] = "Z軸反転" },
            ["FlipUV_V"] = new() { ["en"] = "Flip UV V", ["ja"] = "UV V反転" },
            ["Options"] = new() { ["en"] = "Options", ["ja"] = "オプション" },
            ["ExportMaterials"] = new() { ["en"] = "Export Materials", ["ja"] = "マテリアル出力" },
            ["SkipEmptyObjects"] = new() { ["en"] = "Skip Empty Objects", ["ja"] = "空オブジェクトをスキップ" },
            ["ExportSelectedOnly"] = new() { ["en"] = "Selected Mesh Only", ["ja"] = "選択メッシュのみ" },
            ["MergeAllObjects"] = new() { ["en"] = "Merge All Objects", ["ja"] = "全オブジェクト統合" },
            ["Format"] = new() { ["en"] = "Format", ["ja"] = "出力形式" },
            ["DecimalPrecision"] = new() { ["en"] = "Decimal Precision", ["ja"] = "小数点以下桁数" },
            ["UseShiftJIS"] = new() { ["en"] = "Shift-JIS Encoding", ["ja"] = "Shift-JISエンコード" },

            // 結果セクション
            ["Result"] = new() { ["en"] = "Result", ["ja"] = "結果" },
            ["ExportSuccessful"] = new() { ["en"] = "Export successful!", ["ja"] = "エクスポート成功！" },
            ["ExportFailed"] = new() { ["en"] = "Export failed: {0}", ["ja"] = "エクスポート失敗: {0}" },
            ["Objects"] = new() { ["en"] = "Objects", ["ja"] = "オブジェクト数" },
            ["Vertices"] = new() { ["en"] = "Vertices", ["ja"] = "頂点数" },
            ["Faces"] = new() { ["en"] = "Faces", ["ja"] = "面数" },
            ["Materials"] = new() { ["en"] = "Materials", ["ja"] = "マテリアル数" },
            ["OpenFolder"] = new() { ["en"] = "Open Folder", ["ja"] = "フォルダを開く" },

            // 警告
            ["NoMeshContext"] = new() { ["en"] = "No mesh to export. Create or import a mesh first.", ["ja"] = "エクスポートするメッシュがありません。先にメッシュを作成またはインポートしてください。" },
            ["NoContextWarning"] = new() { ["en"] = "No context set. Open from MeshFactory window to export.", ["ja"] = "コンテキスト未設定。エクスポートするにはMeshFactoryウィンドウから開いてください。" },
        };

        /// <summary>ローカライズ取得</summary>
        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // フィールド
        // ================================================================

        private MQOExportSettings _settings = new MQOExportSettings();
        private MQOExportResult _lastResult;
        private string _lastFilePath = "";
        private Vector2 _scrollPosition;

        private bool _foldSettings = true;
        private bool _foldResult = false;

        // ================================================================
        // Open
        // ================================================================

        [MenuItem("MeshFactory/Export/MQO Export...")]
        public static void ShowWindow()
        {
            Open(null);
        }

        /// <summary>
        /// ToolContextから開く
        /// </summary>
        public static void Open(Tools.ToolContext ctx)
        {
            var window = GetWindow<MQOExportPanel>();
            window.titleContent = new GUIContent(T("WindowTitle"));
            window.minSize = new Vector2(320, 350);
            if (ctx != null)
                window.SetContext(ctx);
            window.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawFileSection();
            EditorGUILayout.Space(5);

            DrawSettingsSection();
            EditorGUILayout.Space(5);

            DrawResultSection();

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // ファイルセクション
        // ================================================================

        private void DrawFileSection()
        {
            EditorGUILayout.LabelField(T("File"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _lastFilePath = EditorGUILayout.TextField(T("OutputPath"), _lastFilePath);
                if (GUILayout.Button(T("Browse"), GUILayout.Width(70)))
                {
                    string dir = string.IsNullOrEmpty(_lastFilePath)
                        ? Application.dataPath
                        : Path.GetDirectoryName(_lastFilePath);
                    string defaultName = GetDefaultFileName();
                    string path = EditorUtility.SaveFilePanel("Save MQO File", dir, defaultName, "mqo");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _lastFilePath = path;
                    }
                }
            }

            EditorGUILayout.Space(5);

            // コンテキスト警告
            if (_context == null)
            {
                EditorGUILayout.HelpBox(T("NoContextWarning"), MessageType.Warning);
            }
            else if (!HasMeshToExport())
            {
                EditorGUILayout.HelpBox(T("NoMeshContext"), MessageType.Warning);
            }

            // エクスポートボタン
            using (new EditorGUI.DisabledScope(!CanExport()))
            {
                if (GUILayout.Button(T("Export"), GUILayout.Height(30)))
                {
                    ExecuteExport();
                }
            }
        }

        private string GetDefaultFileName()
        {
            if (_context?.Model?.CurrentMeshContext != null)
            {
                return _context.Model.CurrentMeshContext.Name ?? "export";
            }
            return "export";
        }

        private bool HasMeshToExport()
        {
            if (_context?.Model == null) return false;
            var meshContexts = GetMeshContextsToExport();
            return meshContexts != null && meshContexts.Count > 0;
        }

        private bool CanExport()
        {
            return _context != null &&
                   !string.IsNullOrEmpty(_lastFilePath) &&
                   HasMeshToExport();
        }

        private List<MeshContext> GetMeshContextsToExport()
        {
            if (_context?.Model == null) return null;

            var result = new List<MeshContext>();

            if (_settings.ExportSelectedOnly)
            {
                // 選択中のメッシュのみ
                var current = _context.Model.CurrentMeshContext;
                if (current != null)
                {
                    result.Add(current);
                }
            }
            else
            {
                // 全メッシュ
                var meshList = _context.MeshList;
                if (meshList != null)
                {
                    foreach (var mc in meshList)
                    {
                        if (mc != null)
                        {
                            result.Add(mc);
                        }
                    }
                }
            }

            return result;
        }

        // ================================================================
        // 設定セクション
        // ================================================================

        private void DrawSettingsSection()
        {
            _foldSettings = EditorGUILayout.Foldout(_foldSettings, T("Settings"), true);
            if (!_foldSettings) return;

            EditorGUI.indentLevel++;

            // 座標系
            EditorGUILayout.LabelField(T("Coordinate"), EditorStyles.miniLabel);
            _settings.Scale = EditorGUILayout.FloatField(T("Scale"), _settings.Scale);
            _settings.SwapYZ = EditorGUILayout.Toggle(T("SwapYZ"), _settings.SwapYZ);
            _settings.FlipZ = EditorGUILayout.Toggle(T("FlipZAxis"), _settings.FlipZ);
            _settings.FlipUV_V = EditorGUILayout.Toggle(T("FlipUV_V"), _settings.FlipUV_V);

            EditorGUILayout.Space(3);

            // オプション
            EditorGUILayout.LabelField(T("Options"), EditorStyles.miniLabel);
            _settings.ExportMaterials = EditorGUILayout.Toggle(T("ExportMaterials"), _settings.ExportMaterials);
            _settings.SkipEmptyObjects = EditorGUILayout.Toggle(T("SkipEmptyObjects"), _settings.SkipEmptyObjects);
            _settings.ExportSelectedOnly = EditorGUILayout.Toggle(T("ExportSelectedOnly"), _settings.ExportSelectedOnly);
            _settings.MergeObjects = EditorGUILayout.Toggle(T("MergeAllObjects"), _settings.MergeObjects);

            EditorGUILayout.Space(3);

            // 出力形式
            EditorGUILayout.LabelField(T("Format"), EditorStyles.miniLabel);
            _settings.DecimalPrecision = EditorGUILayout.IntSlider(T("DecimalPrecision"), _settings.DecimalPrecision, 1, 8);
            _settings.UseShiftJIS = EditorGUILayout.Toggle(T("UseShiftJIS"), _settings.UseShiftJIS);

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // 結果セクション
        // ================================================================

        private void DrawResultSection()
        {
            if (_lastResult == null) return;

            _foldResult = EditorGUILayout.Foldout(_foldResult, T("Result"), true);
            if (!_foldResult) return;

            EditorGUI.indentLevel++;

            if (_lastResult.Success)
            {
                EditorGUILayout.HelpBox(T("ExportSuccessful"), MessageType.Info);

                var stats = _lastResult.Stats;
                EditorGUILayout.LabelField(T("Objects"), stats.ObjectCount.ToString());
                EditorGUILayout.LabelField(T("Vertices"), stats.TotalVertices.ToString());
                EditorGUILayout.LabelField(T("Faces"), stats.TotalFaces.ToString());
                EditorGUILayout.LabelField(T("Materials"), stats.MaterialCount.ToString());

                EditorGUILayout.Space(3);

                if (GUILayout.Button(T("OpenFolder")))
                {
                    string folder = Path.GetDirectoryName(_lastResult.FilePath);
                    EditorUtility.RevealInFinder(_lastResult.FilePath);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(T("ExportFailed", _lastResult.ErrorMessage), MessageType.Error);
            }

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // エクスポート実行
        // ================================================================

        private void ExecuteExport()
        {
            var meshContexts = GetMeshContextsToExport();

            if (meshContexts == null || meshContexts.Count == 0)
            {
                Debug.LogError("[MQOExportPanel] No meshes to export");
                return;
            }

            Debug.Log($"[MQOExportPanel] Exporting {meshContexts.Count} mesh(es) to: {_lastFilePath}");

            _lastResult = MQOExporter.ExportFile(_lastFilePath, meshContexts, _settings);

            if (_lastResult.Success)
            {
                _foldResult = true;
            }

            Repaint();
        }
    }
}