// Assets/Editor/Poly_Ling/Tools/Panels/MQO/Import/MQOImportPanel.cs
// MQOインポートパネル
// IToolPanelBase継承、MeshListPanelに準拠
// v1.1: ImportMode（追加/置換）対応

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Localization;

//using MeshContext = MeshContext;

namespace Poly_Ling.MQO
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
            ["NormalMode"] = new() { ["en"] = "Normal Mode", ["ja"] = "法線モード" },
            ["SmoothingAngle"] = new() { ["en"] = "Smoothing Angle", ["ja"] = "スムージング角度" },

            // インポートモード（v1.1追加、v1.2: NewModel追加）
            ["ImportMode"] = new() { ["en"] = "Import Mode", ["ja"] = "インポートモード" },
            ["ModeAppend"] = new() { ["en"] = "Append (Add to existing)", ["ja"] = "追加（既存に追加）" },
            ["ModeReplace"] = new() { ["en"] = "Replace (Clear existing)", ["ja"] = "置換（既存を削除）" },
            ["ModeNewModel"] = new() { ["en"] = "New Model (Add as separate)", ["ja"] = "新規モデル（別モデルとして追加）" },

            // ボーンウェイトCSV
            ["BoneWeight"] = new() { ["en"] = "Bone Weight", ["ja"] = "ボーンウェイト" },
            ["BoneWeightCSV"] = new() { ["en"] = "Bone Weight CSV", ["ja"] = "ボーンウェイトCSV" },
            ["BoneCSV"] = new() { ["en"] = "Bone CSV (PmxBone)", ["ja"] = "ボーンCSV (PmxBone)" },
            ["Browse"] = new() { ["en"] = "Browse...", ["ja"] = "参照..." },
            ["Clear"] = new() { ["en"] = "Clear", ["ja"] = "クリア" },
            ["CSVNotSet"] = new() { ["en"] = "(Not set)", ["ja"] = "（未設定）" },

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
            ["NoContextWarning"] = new() { ["en"] = "No context set. Open from Poly_Ling window to import directly.", ["ja"] = "コンテキスト未設定。直接インポートするにはMeshFactoryウィンドウから開いてください。" },

            // 結果セクション
            ["LastImportResult"] = new() { ["en"] = "Last Import Result", ["ja"] = "前回のインポート結果" },
            ["ImportSuccessful"] = new() { ["en"] = "Import Successful!", ["ja"] = "インポート成功！" },
            ["ImportFailed"] = new() { ["en"] = "Import Failed: {0}", ["ja"] = "インポート失敗: {0}" },
            ["TotalVertices"] = new() { ["en"] = "Total Vertices", ["ja"] = "総頂点数" },
            ["TotalFaces"] = new() { ["en"] = "Total Faces", ["ja"] = "総面数" },
            ["SkippedSpecialFaces"] = new() { ["en"] = "Skipped Special Faces", ["ja"] = "スキップした特殊面" },
            ["ImportedMeshes"] = new() { ["en"] = "Imported Meshes:", ["ja"] = "インポートしたメッシュ:" },
            
            // ミラー設定
            ["BakeMirror"] = new() { ["en"] = "Bake Mirror", ["ja"] = "ミラーをベイク" },
            
            // デバッグ設定
            ["DebugSettings"] = new() { ["en"] = "Debug Settings", ["ja"] = "デバッグ設定" },
            ["DebugVertexInfo"] = new() { ["en"] = "Output Vertex Debug Info", ["ja"] = "頂点デバッグ情報を出力" },
            ["DebugVertexNearUVCount"] = new() { ["en"] = "Near UV Pair Count", ["ja"] = "近接UVペア出力件数" },
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

        //[MenuItem("Poly_Ling/Import/MQO Import...")]
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

            // インポートモード（v1.1追加）
            EditorGUILayout.LabelField(T("ImportMode"), EditorStyles.miniLabel);
            DrawImportModeToggle();

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
            _settings.NormalMode = (NormalMode)EditorGUILayout.EnumPopup(T("NormalMode"), _settings.NormalMode);
            if (_settings.NormalMode == NormalMode.Smooth)
            {
                _settings.SmoothingAngle = EditorGUILayout.Slider(T("SmoothingAngle"), _settings.SmoothingAngle, 0f, 180f);//スライダーの上限下限
            }

            EditorGUILayout.Space(3);

            // ボーンウェイトCSV（ドラッグ&ドロップ対応）
            EditorGUILayout.LabelField(T("BoneWeightCSV"), EditorStyles.miniLabel);

            // ドラッグ&ドロップエリア
            Rect dropAreaWeight = EditorGUILayout.BeginHorizontal();
            {
                // ファイル名表示（ドロップエリアとしても機能）
                string displayPath = string.IsNullOrEmpty(_settings.BoneWeightCSVPath)
                    ? T("CSVNotSet")
                    : Path.GetFileName(_settings.BoneWeightCSVPath);
                EditorGUILayout.LabelField(displayPath, EditorStyles.textField);

                // 参照ボタン
                if (GUILayout.Button(T("Browse"), GUILayout.Width(60)))
                {
                    string path = EditorUtility.OpenFilePanel(
                        T("BoneWeightCSV"),
                        string.IsNullOrEmpty(_settings.BoneWeightCSVPath) ? "" : Path.GetDirectoryName(_settings.BoneWeightCSVPath),
                        "csv");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _settings.BoneWeightCSVPath = path;
                    }
                }

                // クリアボタン
                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_settings.BoneWeightCSVPath));
                if (GUILayout.Button(T("Clear"), GUILayout.Width(50)))
                {
                    _settings.BoneWeightCSVPath = "";
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();

            // ドラッグ&ドロップ処理
            HandleCSVDragAndDrop(dropAreaWeight, path => _settings.BoneWeightCSVPath = path);

            // ボーンCSV（PmxBone形式、ドラッグ&ドロップ対応）
            EditorGUILayout.LabelField(T("BoneCSV"), EditorStyles.miniLabel);

            Rect dropAreaBone = EditorGUILayout.BeginHorizontal();
            {
                // ファイル名表示
                string displayPath = string.IsNullOrEmpty(_settings.BoneCSVPath)
                    ? T("CSVNotSet")
                    : Path.GetFileName(_settings.BoneCSVPath);
                EditorGUILayout.LabelField(displayPath, EditorStyles.textField);

                // 参照ボタン
                if (GUILayout.Button(T("Browse"), GUILayout.Width(60)))
                {
                    string path = EditorUtility.OpenFilePanel(
                        T("BoneCSV"),
                        string.IsNullOrEmpty(_settings.BoneCSVPath) ? "" : Path.GetDirectoryName(_settings.BoneCSVPath),
                        "csv");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _settings.BoneCSVPath = path;
                    }
                }

                // クリアボタン
                EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(_settings.BoneCSVPath));
                if (GUILayout.Button(T("Clear"), GUILayout.Width(50)))
                {
                    _settings.BoneCSVPath = "";
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();

            // ドラッグ&ドロップ処理
            HandleCSVDragAndDrop(dropAreaBone, path => _settings.BoneCSVPath = path);

            // BoneScale設定（BoneCSVが指定されている場合のみ表示）
            if (!string.IsNullOrEmpty(_settings.BoneCSVPath))
            {
                EditorGUILayout.Space(4);
                _settings.BoneScale = EditorGUILayout.FloatField(
                    new GUIContent("Bone Scale", "PMXボーン座標に適用するスケール（MQOメッシュとの座標系合わせ用、通常10.0）"),
                    _settings.BoneScale);
            }

            EditorGUI.indentLevel--;
            
            // ================================================================
            // ミラー設定
            // ================================================================
            EditorGUILayout.Space(8);
            _settings.BakeMirror = EditorGUILayout.Toggle(
                new GUIContent(T("BakeMirror"), "ミラー属性を持つメッシュのミラー側を実体メッシュとして生成"),
                _settings.BakeMirror);
            
            // ================================================================
            // デバッグ設定
            // ================================================================
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(T("DebugSettings"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            
            _settings.DebugVertexInfo = EditorGUILayout.Toggle(
                new GUIContent(T("DebugVertexInfo"), "メッシュオブジェクトごとの頂点情報をコンソールに出力"),
                _settings.DebugVertexInfo);
            
            if (_settings.DebugVertexInfo)
            {
                _settings.DebugVertexNearUVCount = EditorGUILayout.IntSlider(
                    new GUIContent(T("DebugVertexNearUVCount"), "同一頂点で異なるUVを持つペアの出力件数（近い順）"),
                    _settings.DebugVertexNearUVCount, 1, 100);
            }
            
            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// CSVファイルのドラッグ&ドロップを処理
        /// </summary>
        private void HandleCSVDragAndDrop(Rect dropArea, System.Action<string> onDropped)
        {
            Event evt = Event.current;

            if (!dropArea.Contains(evt.mousePosition))
                return;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                    // CSVファイルがドラッグされているか確認
                    bool hasCSV = false;
                    foreach (var path in DragAndDrop.paths)
                    {
                        if (path.EndsWith(".csv", System.StringComparison.OrdinalIgnoreCase))
                        {
                            hasCSV = true;
                            break;
                        }
                    }
                    DragAndDrop.visualMode = hasCSV ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                    evt.Use();
                    break;

                case EventType.DragPerform:
                    DragAndDrop.AcceptDrag();
                    foreach (var path in DragAndDrop.paths)
                    {
                        if (path.EndsWith(".csv", System.StringComparison.OrdinalIgnoreCase))
                        {
                            // Assets内のパスを絶対パスに変換
                            string fullPath = path;
                            if (path.StartsWith("Assets/"))
                            {
                                fullPath = Path.GetFullPath(path);
                            }
                            onDropped?.Invoke(fullPath);
                            break;
                        }
                    }
                    evt.Use();
                    break;
            }
        }

        /// <summary>
        /// インポートモードのトグルボタン描画
        /// </summary>
        private void DrawImportModeToggle()
        {
            EditorGUILayout.BeginHorizontal();

            // NewModelボタン（デフォルト）
            bool isNewModel = _settings.ImportMode == MQOImportMode.NewModel;
            GUI.backgroundColor = isNewModel ? new Color(0.6f, 1f, 0.6f) : Color.white;
            if (GUILayout.Button(T("ModeNewModel"), EditorStyles.miniButtonLeft))
            {
                _settings.ImportMode = MQOImportMode.NewModel;
            }

            // Appendボタン
            bool isAppend = _settings.ImportMode == MQOImportMode.Append;
            GUI.backgroundColor = isAppend ? new Color(0.6f, 0.8f, 1f) : Color.white;
            if (GUILayout.Button(T("ModeAppend"), EditorStyles.miniButtonMid))
            {
                _settings.ImportMode = MQOImportMode.Append;
            }

            // Replaceボタン
            bool isReplace = _settings.ImportMode == MQOImportMode.Replace;
            GUI.backgroundColor = isReplace ? new Color(1f, 0.8f, 0.6f) : Color.white;
            if (GUILayout.Button(T("ModeReplace"), EditorStyles.miniButtonRight))
            {
                _settings.ImportMode = MQOImportMode.Replace;
            }

            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
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
            EditorGUILayout.Space(3);
            int shown = 0;
            int maxShow = 10;
            foreach (var obj in _previewDocument.Objects)
            {
                if (shown >= maxShow)
                {
                    int remaining = _previewDocument.Objects.Count - maxShow;
                    EditorGUILayout.LabelField(T("AndMore", remaining), EditorStyles.miniLabel);
                    break;
                }

                string label = obj.Name;
                if (!obj.IsVisible)
                    label += " " + T("Hidden");

                EditorGUILayout.LabelField($"  {label} (V:{obj.Vertices.Count} F:{obj.Faces.Count})",
                    EditorStyles.miniLabel);
                shown++;
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

            // Replaceモードの場合はボタン色を変える
            if (_settings.ImportMode == MQOImportMode.Replace)
            {
                GUI.backgroundColor = new Color(1f, 0.7f, 0.5f);
            }

            if (GUILayout.Button(T("Import"), buttonStyle))
            {
                Debug.Log($"[MQOImportPanel] Import button clicked. File: {_lastFilePath}");
                ExecuteImport();
            }

            GUI.backgroundColor = Color.white;

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
                          $"{_lastResult.Stats.TotalVertices} vertices, {_lastResult.Stats.TotalFaces} faces, " +
                          $"{_lastResult.Materials.Count} materialPathList");

                // コンテキストがあれば追加
                if (_context != null)
                {
                    bool handled = false;

                    // ================================================================
                    // Replaceモード: 1回のUndoで戻せる置換
                    // ================================================================
                    if (_settings.ImportMode == MQOImportMode.Replace)
                    {
                        if (_context.ReplaceAllMeshContexts != null)
                        {
                            Debug.Log($"[MQOImportPanel] Replace mode: Replacing with {_lastResult.MeshContexts.Count} meshes");
                            _context.ReplaceAllMeshContexts.Invoke(_lastResult.MeshContexts);
                            handled = true;
                        }
                        else if (_context.ClearAllMeshContexts != null && _context.AddMeshContexts != null)
                        {
                            // フォールバック: Clear + Add（2回のUndo）
                            Debug.LogWarning("[MQOImportPanel] ReplaceAllMeshContexts not available, using Clear + Add");
                            _context.ClearAllMeshContexts.Invoke();
                            _context.AddMeshContexts.Invoke(_lastResult.MeshContexts);
                            handled = true;
                        }
                        else
                        {
                            Debug.LogWarning("[MQOImportPanel] Replace not available, falling back to Append mode");
                        }

                        // Replaceモードではマテリアルも置換（MaterialReferences優先）
                        if (handled && _lastResult.MaterialReferences.Count > 0 && _context.ReplaceMaterialReferences != null)
                        {
                            Debug.Log($"[MQOImportPanel] Replacing materialRefs: {_lastResult.MaterialReferences.Count}");
                            _context.ReplaceMaterialReferences.Invoke(_lastResult.MaterialReferences);
                        }
                        else if (handled && _lastResult.Materials.Count > 0 && _context.ReplaceMaterials != null)
                        {
                            Debug.Log($"[MQOImportPanel] Replacing materialPathList: {_lastResult.Materials.Count}");
                            _context.ReplaceMaterials.Invoke(_lastResult.Materials);
                        }
                    }
                    // ================================================================
                    // NewModelモード: 新規MeshContextとして追加（マテリアルはインポート分のみ）
                    // ================================================================
                    else if (_settings.ImportMode == MQOImportMode.NewModel)
                    {
                        // NewModelモードでは既存マテリアルに関係なく、インポート分のみで新規モデル
                        // マテリアルインデックスはそのまま（0から開始）
                        if (_context.AddMeshContexts != null)
                        {
                            Debug.Log($"[MQOImportPanel] NewModel mode: Adding {_lastResult.MeshContexts.Count} meshes as new model");
                            _context.AddMeshContexts.Invoke(_lastResult.MeshContexts);
                            handled = true;
                        }
                        else
                        {
                            foreach (var meshContext in _lastResult.MeshContexts)
                            {
                                Debug.Log($"[MQOImportPanel] Adding MeshContext: {meshContext.Name}");
                                _context.AddMeshContext?.Invoke(meshContext);
                            }
                            handled = true;
                        }

                        // ボーンは既にMeshContextsに含まれている（PMXと同じ方式）
                        // BoneMeshContextsは使用しない

                        // NewModelモードではマテリアルを追加（インポート分のみ）
                        if (_lastResult.MaterialReferences.Count > 0 && _context.AddMaterialReferences != null)
                        {
                            _context.AddMaterialReferences.Invoke(_lastResult.MaterialReferences);
                        }
                        else if (_lastResult.Materials.Count > 0 && _context.AddMaterials != null)
                        {
                            _context.AddMaterials.Invoke(_lastResult.Materials);
                        }
                    }

                    // ================================================================
                    // Appendモード: 既存メッシュに追加（マテリアルインデックス補正あり）
                    // ================================================================
                    if (!handled)
                    {
                        // ★ 既存マテリアル数を取得してオフセットを適用
                        int existingMaterialCount = 0;
                        if (_context.Model?.Materials != null)
                        {
                            existingMaterialCount = _context.Model.Materials.Count;
                        }

                        if (existingMaterialCount > 0 && _lastResult.Materials.Count > 0)
                        {
                            Debug.Log($"[MQOImportPanel] Append mode: Applying material index offset +{existingMaterialCount}");
                            _lastResult.ApplyMaterialIndexOffset(existingMaterialCount);
                        }

                        // 既存メッシュ数を取得（ボーンの親インデックス用）
                        int existingMeshCount = _context.Model?.MeshContextList?.Count ?? 0;

                        if (_context.AddMeshContexts != null)
                        {
                            Debug.Log($"[MQOImportPanel] Append mode: Adding {_lastResult.MeshContexts.Count} meshes");
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

                        // ボーンは既にMeshContextsに含まれている（PMXと同じ方式）
                        // BoneMeshContextsは使用しない

                        // Appendモードではマテリアルを追加（既存にマージ）
                        if (_lastResult.MaterialReferences.Count > 0 && _context.AddMaterialReferences != null)
                        {
                            Debug.Log($"[MQOImportPanel] Adding materialRefs: {_lastResult.MaterialReferences.Count}");
                            _context.AddMaterialReferences.Invoke(_lastResult.MaterialReferences);
                        }
                        else if (_lastResult.Materials.Count > 0 && _context.AddMaterials != null)
                        {
                            Debug.Log($"[MQOImportPanel] Adding materials: {_lastResult.Materials.Count}");
                            _context.AddMaterials.Invoke(_lastResult.Materials);
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
                        EditorGUILayout.LabelField($"  {mc.Name} (V:{mc.MeshObject?.VertexCount ?? 0} F:{mc.MeshObject?.FaceCount ?? 0})",
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