// Assets/Editor/MeshFactory/Tools/Panels/MQO/Import/MQOImportPanel.cs
// MQOインポートパネル
// IToolPanelBase継承、MeshListPanelに準拠

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
    /// MQOインポートパネル
    /// </summary>
    public class MQOImportPanel : Tools.IToolPanelBase
    {
        // ================================================================
        // IToolPanelBase 実装
        // ================================================================

        public override string Name => "MQOImport";
        public override string Title => "MQO Import";
        public override Tools.IToolSettings Settings => _settings;

        /// <summary>
        /// ローカライズされたタイトルを取得
        /// </summary>
        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            // ウィンドウ
            ["WindowTitle"] = new() { ["en"] = "MQO Import", ["ja"] = "MQOインポート" },

            // ファイルセクション
            ["File"] = new() { ["en"] = "File", ["ja"] = "ファイル" },
            ["MQOFile"] = new() { ["en"] = "MQO File", ["ja"] = "MQOファイル" },
            ["DragDropHere"] = new() { ["en"] = "Drag & Drop MQO file here", ["ja"] = "MQOファイルをここにドロップ" },

            // 設定セクション
            ["ImportSettings"] = new() { ["en"] = "Import Settings", ["ja"] = "インポート設定" },
            ["Preset"] = new() { ["en"] = "Preset", ["ja"] = "プリセット" },
            ["Default"] = new() { ["en"] = "Default", ["ja"] = "デフォルト" },
            ["Coordinate"] = new() { ["en"] = "Coordinate", ["ja"] = "座標変換" },
            ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール" },
            ["FlipZAxis"] = new() { ["en"] = "Flip Z Axis", ["ja"] = "Z軸反転" },
            ["FlipUV_V"] = new() { ["en"] = "Flip UV V", ["ja"] = "UV V反転" },
            ["Options"] = new() { ["en"] = "Options", ["ja"] = "オプション" },
            ["ImportMaterials"] = new() { ["en"] = "Import Materials", ["ja"] = "マテリアル読込" },
            ["SkipHiddenObjects"] = new() { ["en"] = "Skip Hidden Objects", ["ja"] = "非表示オブジェクトをスキップ" },
            ["SkipEmptyObjects"] = new() { ["en"] = "Skip Empty Objects", ["ja"] = "空オブジェクトをスキップ" },
            ["MergeAllObjects"] = new() { ["en"] = "Merge All Objects", ["ja"] = "全オブジェクト統合" },
            ["Normals"] = new() { ["en"] = "Normals", ["ja"] = "法線" },
            ["RecalculateNormals"] = new() { ["en"] = "Recalculate Normals", ["ja"] = "法線を再計算" },
            ["SmoothingAngle"] = new() { ["en"] = "Smoothing Angle", ["ja"] = "スムージング角度" },

            // プレビューセクション
            ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー" },
            ["SelectFileToPreview"] = new() { ["en"] = "Select a file to preview", ["ja"] = "ファイルを選択してください" },
            ["Version"] = new() { ["en"] = "Version", ["ja"] = "バージョン" },
            ["Objects"] = new() { ["en"] = "Objects", ["ja"] = "オブジェクト" },
            ["Materials"] = new() { ["en"] = "Materials", ["ja"] = "マテリアル" },
            ["Hidden"] = new() { ["en"] = "[Hidden]", ["ja"] = "[非表示]" },
            ["AndMore"] = new() { ["en"] = "... and {0} more", ["ja"] = "... 他 {0} 件" },

            // インポートボタン
            ["Import"] = new() { ["en"] = "Import", ["ja"] = "インポート" },
            ["NoContextWarning"] = new() { ["en"] = "No context set. Open from MeshFactory window to import directly.", ["ja"] = "コンテキスト未設定。直接インポートするにはMeshFactoryウィンドウから開いてください。" },

            // 結果セクション
            ["LastImportResult"] = new() { ["en"] = "Last Import Result", ["ja"] = "前回のインポート結果" },
            ["ImportSuccessful"] = new() { ["en"] = "Import Successful!", ["ja"] = "インポート成功！" },
            ["ImportFailed"] = new() { ["en"] = "Import Failed: {0}", ["ja"] = "インポート失敗: {0}" },
            ["TotalVertices"] = new() { ["en"] = "Total Vertices", ["ja"] = "総頂点数" },
            ["TotalFaces"] = new() { ["en"] = "Total Faces", ["ja"] = "総面数" },
            ["SkippedSpecialFaces"] = new() { ["en"] = "Skipped Special Faces", ["ja"] = "スキップした特殊面" },
            ["ImportedMeshes"] = new() { ["en"] = "Imported Meshes:", ["ja"] = "インポートしたメッシュ:" },
        };

        /// <summary>ローカライズ取得</summary>
        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // フィールド
        // ================================================================

        private MQOImportSettings _settings = new MQOImportSettings();
        private string _lastFilePath = "";
        private MQOImportResult _lastResult;
        private Vector2 _scrollPosition;
        private bool _foldSettings = true;
        private bool _foldPreview = true;
        private bool _foldResult = false;
        private MQODocument _previewDocument;

        // ================================================================
        // Open
        // ================================================================

        [MenuItem("MeshFactory/Import/MQO Import...")]
        public static void ShowWindow()
        {
            Open(null);
        }

        /// <summary>
        /// ToolContextから開く
        /// </summary>
        public static void Open(Tools.ToolContext ctx)
        {
            var window = GetWindow<MQOImportPanel>();
            window.titleContent = new GUIContent(T("WindowTitle"));
            window.minSize = new Vector2(320, 400);
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

            DrawPreviewSection();
            EditorGUILayout.Space(5);

            DrawImportButton();
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
                _lastFilePath = EditorGUILayout.TextField(T("MQOFile"), _lastFilePath);

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir = string.IsNullOrEmpty(_lastFilePath)
                        ? Application.dataPath
                        : Path.GetDirectoryName(_lastFilePath);

                    string path = EditorUtility.OpenFilePanel("Select MQO File", dir, "mqo");

                    if (!string.IsNullOrEmpty(path))
                    {
                        _lastFilePath = path;
                        LoadPreview();
                    }
                }
            }

            // ドラッグ&ドロップ
            var dropArea = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, T("DragDropHere"), EditorStyles.helpBox);
            HandleDragAndDrop(dropArea);
        }

        private void HandleDragAndDrop(Rect dropArea)
        {
            Event evt = Event.current;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropArea.Contains(evt.mousePosition))
                        return;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        foreach (string path in DragAndDrop.paths)
                        {
                            if (path.EndsWith(".mqo", System.StringComparison.OrdinalIgnoreCase))
                            {
                                _lastFilePath = path;
                                LoadPreview();
                                break;
                            }
                        }
                    }
                    evt.Use();
                    break;
            }
        }

        // ================================================================
        // 設定セクション
        // ================================================================

        private void DrawSettingsSection()
        {
            _foldSettings = EditorGUILayout.Foldout(_foldSettings, T("ImportSettings"), true);
            if (!_foldSettings) return;

            EditorGUI.indentLevel++;

            // プリセット
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(T("Preset"), GUILayout.Width(60));
            if (GUILayout.Button(T("Default"), EditorStyles.miniButtonLeft))
            {
                _settings = MQOImportSettings.CreateDefault();
            }
            if (GUILayout.Button("MMD", EditorStyles.miniButtonMid))
            {
                _settings = MQOImportSettings.CreateMMDCompatible();
            }
            if (GUILayout.Button("1:1", EditorStyles.miniButtonRight))
            {
                _settings = MQOImportSettings.CreateNoScale();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);

            // 座標変換
            EditorGUILayout.LabelField(T("Coordinate"), EditorStyles.miniLabel);
            _settings.Scale = EditorGUILayout.FloatField(T("Scale"), _settings.Scale);
            _settings.FlipZ = EditorGUILayout.Toggle(T("FlipZAxis"), _settings.FlipZ);
            _settings.FlipUV_V = EditorGUILayout.Toggle(T("FlipUV_V"), _settings.FlipUV_V);

            EditorGUILayout.Space(3);

            // オプション
            EditorGUILayout.LabelField(T("Options"), EditorStyles.miniLabel);
            _settings.ImportMaterials = EditorGUILayout.Toggle(T("ImportMaterials"), _settings.ImportMaterials);
            _settings.SkipHiddenObjects = EditorGUILayout.Toggle(T("SkipHiddenObjects"), _settings.SkipHiddenObjects);
            _settings.SkipEmptyObjects = EditorGUILayout.Toggle(T("SkipEmptyObjects"), _settings.SkipEmptyObjects);
            _settings.MergeObjects = EditorGUILayout.Toggle(T("MergeAllObjects"), _settings.MergeObjects);

            EditorGUILayout.Space(3);

            // 法線
            EditorGUILayout.LabelField(T("Normals"), EditorStyles.miniLabel);
            _settings.RecalculateNormals = EditorGUILayout.Toggle(T("RecalculateNormals"), _settings.RecalculateNormals);
            if (_settings.RecalculateNormals)
            {
                _settings.SmoothingAngle = EditorGUILayout.Slider(T("SmoothingAngle"), _settings.SmoothingAngle, 0f, 180f);
            }

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // プレビューセクション
        // ================================================================

        private void DrawPreviewSection()
        {
            _foldPreview = EditorGUILayout.Foldout(_foldPreview, T("Preview"), true);
            if (!_foldPreview) return;

            if (_previewDocument == null)
            {
                EditorGUILayout.HelpBox(T("SelectFileToPreview"), MessageType.Info);
                return;
            }

            EditorGUI.indentLevel++;

            EditorGUILayout.LabelField(T("File"), Path.GetFileName(_previewDocument.FilePath ?? "Unknown"));
            EditorGUILayout.LabelField(T("Version"), _previewDocument.Version.ToString());
            EditorGUILayout.LabelField(T("Objects"), _previewDocument.Objects.Count.ToString());
            EditorGUILayout.LabelField(T("Materials"), _previewDocument.Materials.Count.ToString());

            // オブジェクト一覧
            if (_previewDocument.Objects.Count > 0)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField($"{T("Objects")}:", EditorStyles.miniLabel);

                int maxShow = Mathf.Min(_previewDocument.Objects.Count, 10);
                for (int i = 0; i < maxShow; i++)
                {
                    var obj = _previewDocument.Objects[i];
                    string info = $"  {obj.Name} (V:{obj.Vertices.Count} F:{obj.Faces.Count})";
                    if (!obj.IsVisible) info += $" {T("Hidden")}";
                    EditorGUILayout.LabelField(info, EditorStyles.miniLabel);
                }

                if (_previewDocument.Objects.Count > maxShow)
                {
                    EditorGUILayout.LabelField($"  {T("AndMore", _previewDocument.Objects.Count - maxShow)}", EditorStyles.miniLabel);
                }
            }

            EditorGUI.indentLevel--;
        }

        private void LoadPreview()
        {
            _previewDocument = null;
            _lastResult = null;

            if (string.IsNullOrEmpty(_lastFilePath) || !File.Exists(_lastFilePath))
                return;

            try
            {
                _previewDocument = MQOParser.ParseFile(_lastFilePath);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[MQOImportPanel] Failed to parse: {ex.Message}");
            }

            Repaint();
        }

        // ================================================================
        // インポートボタン
        // ================================================================

        private void DrawImportButton()
        {
            bool fileExists = !string.IsNullOrEmpty(_lastFilePath) && File.Exists(_lastFilePath);

            EditorGUI.BeginDisabledGroup(!fileExists);

            var buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                fixedHeight = 32
            };

            if (GUILayout.Button(T("Import"), buttonStyle))
            {
                ExecuteImport();
            }

            EditorGUI.EndDisabledGroup();

            // コンテキストがない場合の警告
            if (_context == null)
            {
                EditorGUILayout.HelpBox(T("NoContextWarning"), MessageType.Info);
            }
        }

        private void ExecuteImport()
        {
            _lastResult = MQOImporter.ImportFile(_lastFilePath, _settings);

            if (_lastResult.Success)
            {
                Debug.Log($"[MQOImportPanel] Import successful: {_lastResult.MeshContexts.Count} objects, " +
                          $"{_lastResult.Stats.TotalVertices} vertices, {_lastResult.Stats.TotalFaces} faces");

                // コンテキストがあれば追加
                if (_context != null)
                {
                    // バッチ追加（1回のUndoで全部戻る）
                    if (_context.AddMeshContexts != null)
                    {
                        Debug.Log($"[MQOImportPanel] Batch adding {_lastResult.MeshContexts.Count} meshes");
                        _context.AddMeshContexts.Invoke(_lastResult.MeshContexts);
                    }
                    else
                    {
                        // フォールバック：1つずつ追加
                        foreach (var meshContext in _lastResult.MeshContexts)
                        {
                            Debug.Log($"[MQOImportPanel] Adding MeshContext: {meshContext.Name}");
                            _context.AddMeshContext?.Invoke(meshContext);
                        }
                    }
                    _context.Repaint?.Invoke();
                }

                // シーンビューも更新
                SceneView.RepaintAll();

                _foldResult = true;
            }
            else
            {
                Debug.LogError($"[MQOImportPanel] Import failed: {_lastResult.ErrorMessage}");
                EditorUtility.DisplayDialog(T("WindowTitle"), _lastResult.ErrorMessage, "OK");
            }

            Repaint();
        }

        // ================================================================
        // 結果セクション
        // ================================================================

        private void DrawResultSection()
        {
            if (_lastResult == null) return;

            _foldResult = EditorGUILayout.Foldout(_foldResult, T("LastImportResult"), true);
            if (!_foldResult) return;

            EditorGUI.indentLevel++;

            if (_lastResult.Success)
            {
                EditorGUILayout.HelpBox(T("ImportSuccessful"), MessageType.Info);

                var stats = _lastResult.Stats;
                EditorGUILayout.LabelField(T("Objects"), stats.ObjectCount.ToString());
                EditorGUILayout.LabelField(T("TotalVertices"), stats.TotalVertices.ToString());
                EditorGUILayout.LabelField(T("TotalFaces"), stats.TotalFaces.ToString());
                EditorGUILayout.LabelField(T("Materials"), stats.MaterialCount.ToString());

                if (stats.SkippedSpecialFaces > 0)
                {
                    EditorGUILayout.LabelField(T("SkippedSpecialFaces"), stats.SkippedSpecialFaces.ToString());
                }

                // MeshContextリスト
                if (_lastResult.MeshContexts.Count > 0)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField(T("ImportedMeshes"), EditorStyles.miniLabel);

                    foreach (var mc in _lastResult.MeshContexts)
                    {
                        EditorGUILayout.LabelField($"  {mc.Name} (V:{mc.Data?.VertexCount ?? 0} F:{mc.Data?.FaceCount ?? 0})",
                            EditorStyles.miniLabel);
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox(T("ImportFailed", _lastResult.ErrorMessage), MessageType.Error);
            }

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // コンテキスト設定時
        // ================================================================

        protected override void OnContextSet()
        {
            _scrollPosition = Vector2.zero;

            // 設定を復元
            if (_context?.UndoController?.EditorState?.ToolSettings != null)
            {
                var stored = _context.UndoController.EditorState.ToolSettings.Get<MQOImportSettings>(Name);
                if (stored != null)
                {
                    _settings.CopyFrom(stored);
                }
            }
        }
    }
}