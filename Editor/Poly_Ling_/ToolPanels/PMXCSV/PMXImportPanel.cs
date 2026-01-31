// Assets/Editor/Poly_Ling/Tools/Panels/PMX/Import/PMXImportPanel.cs
// PMX CSVインポートパネル
// IToolPanelBase継承、MeshListPanelに準拠

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Localization;
using Poly_Ling.Materials;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMX CSVインポートパネル
    /// </summary>
    public class PMXImportPanel : Tools.IToolPanelBase
    {
        // ================================================================
        // IToolPanelBase 実装
        // ================================================================

        public override string Name => "PMXImport";
        public override string Title => "PMX Import";
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
            ["WindowTitle"] = new() { ["en"] = "PMX Import", ["ja"] = "PMXインポート" },

            // ファイルセクション
            ["File"] = new() { ["en"] = "File", ["ja"] = "ファイル" },
            ["PMXFile"] = new() { ["en"] = "PMX File", ["ja"] = "PMXファイル" },
            ["DragDropHere"] = new() { ["en"] = "Drag & Drop PMX/CSV file here", ["ja"] = "PMX/CSVファイルをここにドロップ" },
            ["DropFileHere"] = new() { ["en"] = "Drop PMX/CSV file here", ["ja"] = "ここにドロップ" },

            // 設定セクション
            ["ImportSettings"] = new() { ["en"] = "Import Settings", ["ja"] = "インポート設定" },
            ["Preset"] = new() { ["en"] = "Preset", ["ja"] = "プリセット" },
            ["Default"] = new() { ["en"] = "Default", ["ja"] = "デフォルト" },
            ["MMDCompatible"] = new() { ["en"] = "MMD Compatible", ["ja"] = "MMD互換" },
            ["NoScale"] = new() { ["en"] = "No Scale (1:1)", ["ja"] = "等倍（1:1）" },
            ["Coordinate"] = new() { ["en"] = "Coordinate", ["ja"] = "座標変換" },
            ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール" },
            ["FlipZAxis"] = new() { ["en"] = "Flip Z Axis", ["ja"] = "Z軸反転" },
            ["FlipUV_V"] = new() { ["en"] = "Flip UV V", ["ja"] = "UV V反転" },
            ["Options"] = new() { ["en"] = "Options", ["ja"] = "オプション" },
            ["ImportMaterials"] = new() { ["en"] = "Import Materials", ["ja"] = "マテリアル読込" },
            ["Normals"] = new() { ["en"] = "Normals", ["ja"] = "法線" },
            ["RecalculateNormals"] = new() { ["en"] = "Recalculate Normals", ["ja"] = "法線を再計算" },
            ["SmoothingAngle"] = new() { ["en"] = "Smoothing Angle", ["ja"] = "スムージング角度" },
            ["ConvertToTPose"] = new() { ["en"] = "Convert to T-Pose", ["ja"] = "Tポーズに変換" },

            // インポートモード（v1.2: NewModel追加）
            ["ImportMode"] = new() { ["en"] = "Import Mode", ["ja"] = "インポートモード" },
            ["ModeAppend"] = new() { ["en"] = "Append (Add to existing)", ["ja"] = "追加（既存に追加）" },
            ["ModeReplace"] = new() { ["en"] = "Replace (Clear existing)", ["ja"] = "置換（既存を削除）" },
            ["ModeNewModel"] = new() { ["en"] = "New Model (Add as separate)", ["ja"] = "新規モデル（別モデルとして追加）" },

            // インポート対象（v1.3追加）
            ["ImportTarget"] = new() { ["en"] = "Import Target", ["ja"] = "インポート対象" },
            ["TargetMesh"] = new() { ["en"] = "Mesh", ["ja"] = "メッシュ" },
            ["TargetBones"] = new() { ["en"] = "Bones", ["ja"] = "ボーン" },
            ["TargetMorphs"] = new() { ["en"] = "Morphs", ["ja"] = "モーフ" },
            ["TargetBodies"] = new() { ["en"] = "Bodies", ["ja"] = "剛体" },
            ["TargetJoints"] = new() { ["en"] = "Joints", ["ja"] = "ジョイント" },
            ["BonesOnly"] = new() { ["en"] = "Bones Only", ["ja"] = "ボーンのみ" },

            // プレビューセクション
            ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー" },
            ["SelectFileToPreview"] = new() { ["en"] = "Select a file to preview", ["ja"] = "ファイルを選択してください" },
            ["Version"] = new() { ["en"] = "Version", ["ja"] = "バージョン" },
            ["ModelName"] = new() { ["en"] = "Model Name", ["ja"] = "モデル名" },
            ["Vertices"] = new() { ["en"] = "Vertices", ["ja"] = "頂点数" },
            ["Faces"] = new() { ["en"] = "Faces", ["ja"] = "面数" },
            ["Materials"] = new() { ["en"] = "Materials", ["ja"] = "マテリアル" },
            ["Bones"] = new() { ["en"] = "Bones", ["ja"] = "ボーン" },
            ["Morphs"] = new() { ["en"] = "Morphs", ["ja"] = "モーフ" },
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
            ["MaterialGroups"] = new() { ["en"] = "Material Groups", ["ja"] = "マテリアルグループ" },
            ["ImportedMeshes"] = new() { ["en"] = "Imported Meshes:", ["ja"] = "インポートしたメッシュ:" },
        };

        /// <summary>ローカライズ取得</summary>
        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // フィールド
        // ================================================================

        private PMXImportSettings _settings = new PMXImportSettings();
        private string _lastFilePath = "";
        private PMXImportResult _lastResult;
        private Vector2 _scrollPosition;
        private bool _foldSettings = true;
        private bool _foldPreview = true;
        private bool _foldResult = false;
        private PMXDocument _previewDocument;

        // ================================================================
        // Open
        // ================================================================

        /// <summary>
        /// ToolContextから開く
        /// </summary>
        public static void Open(Tools.ToolContext ctx)
        {
            var window = GetWindow<PMXImportPanel>();
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

            // テキストフィールド行全体をドロップエリアとして使用
            Rect fieldRect;
            using (new EditorGUILayout.HorizontalScope())
            {
                _lastFilePath = EditorGUILayout.TextField(T("PMXFile"), _lastFilePath);

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir = string.IsNullOrEmpty(_lastFilePath)
                        ? Application.dataPath
                        : Path.GetDirectoryName(_lastFilePath);

                    string path = EditorUtility.OpenFilePanel("Select PMX File", dir, "pmx,csv");

                    if (!string.IsNullOrEmpty(path))
                    {
                        _lastFilePath = path;
                        LoadPreview();
                    }
                }
            }
            
            // 直前のHorizontalScopeのRectを取得してD&D処理
            fieldRect = GUILayoutUtility.GetLastRect();
            HandleFileDragAndDrop(fieldRect);
            
            // ドラッグ中のヒント表示
            if (_isDraggingFile)
            {
                EditorGUILayout.HelpBox(T("DropFileHere"), MessageType.Info);
            }
        }

        private bool _isDraggingFile = false;
        
        private void HandleFileDragAndDrop(Rect dropArea)
        {
            Event evt = Event.current;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                    // PMX/CSVファイルがドラッグされているかチェック
                    bool hasValidFile = IsPMXFile(DragAndDrop.paths);
                    
                    _isDraggingFile = hasValidFile;
                    
                    if (dropArea.Contains(evt.mousePosition) && hasValidFile)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.Use();
                    }
                    break;
                    
                case EventType.DragPerform:
                    if (!dropArea.Contains(evt.mousePosition))
                        return;
                        
                    if (IsPMXFile(DragAndDrop.paths))
                    {
                        DragAndDrop.AcceptDrag();
                        _isDraggingFile = false;
                        _lastFilePath = DragAndDrop.paths[0];
                        LoadPreview();
                        evt.Use();
                    }
                    break;
                    
                case EventType.DragExited:
                    _isDraggingFile = false;
                    break;
            }
        }

        private bool IsPMXFile(string[] paths)
        {
            if (paths == null || paths.Length == 0) return false;
            string ext = Path.GetExtension(paths[0]).ToLower();
            return ext == ".csv" || ext == ".pmx";
        }

        // ================================================================
        // 設定セクション
        // ================================================================

        private void DrawSettingsSection()
        {
            _foldSettings = EditorGUILayout.Foldout(_foldSettings, T("ImportSettings"), true);
            if (!_foldSettings) return;

            EditorGUI.indentLevel++;

            // インポートモード（v1.2: NewModelがデフォルト）
            EditorGUILayout.LabelField(T("ImportMode"), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                int modeIndex = (int)_settings.ImportMode;
                string[] modeOptions = new[] { T("ModeNewModel"), T("ModeAppend"), T("ModeReplace") };
                int newModeIndex = EditorGUILayout.Popup(modeIndex, modeOptions);
                _settings.ImportMode = (PMXImportMode)newModeIndex;
            }

            EditorGUILayout.Space(3);

            // プリセット
            EditorGUILayout.LabelField(T("Preset"), EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("Default")))
                    _settings = PMXImportSettings.CreateDefault();
                if (GUILayout.Button(T("MMDCompatible")))
                    _settings = PMXImportSettings.CreateMMDCompatible();
                if (GUILayout.Button(T("BonesOnly")))
                    _settings = PMXImportSettings.CreateBonesOnly();
            }

            EditorGUILayout.Space(3);

            // インポート対象（フラグ）
            EditorGUILayout.LabelField(T("ImportTarget"), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                DrawImportTargetToggle(PMXImportTarget.Mesh, T("TargetMesh"));
                DrawImportTargetToggle(PMXImportTarget.Bones, T("TargetBones"));
                DrawImportTargetToggle(PMXImportTarget.Morphs, T("TargetMorphs"));
                
                // 将来用（グレーアウト）
                EditorGUI.BeginDisabledGroup(true);
                DrawImportTargetToggle(PMXImportTarget.Bodies, T("TargetBodies"));
                DrawImportTargetToggle(PMXImportTarget.Joints, T("TargetJoints"));
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space(3);

            // 座標変換
            EditorGUILayout.LabelField(T("Coordinate"), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _settings.Scale = EditorGUILayout.FloatField(T("Scale"), _settings.Scale);
                _settings.FlipZ = EditorGUILayout.Toggle(T("FlipZAxis"), _settings.FlipZ);
                _settings.FlipUV_V = EditorGUILayout.Toggle(T("FlipUV_V"), _settings.FlipUV_V);
            }

            EditorGUILayout.Space(3);

            // オプション（Mesh読み込み時のみ有効）
            EditorGUI.BeginDisabledGroup(!_settings.ShouldImportMesh);
            EditorGUILayout.LabelField(T("Options"), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _settings.ImportMaterials = EditorGUILayout.Toggle(T("ImportMaterials"), _settings.ImportMaterials);
            }
            EditorGUI.EndDisabledGroup();

            // ボーンオプション（Bone読み込み時のみ有効）
            EditorGUI.BeginDisabledGroup(!_settings.ShouldImportBones);
            using (new EditorGUI.IndentLevelScope())
            {
                _settings.ConvertToTPose = EditorGUILayout.Toggle(T("ConvertToTPose"), _settings.ConvertToTPose);
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(3);

            // 法線（Mesh読み込み時のみ有効）
            EditorGUI.BeginDisabledGroup(!_settings.ShouldImportMesh);
            EditorGUILayout.LabelField(T("Normals"), EditorStyles.miniBoldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                _settings.RecalculateNormals = EditorGUILayout.Toggle(T("RecalculateNormals"), _settings.RecalculateNormals);

                if (_settings.RecalculateNormals)
                {
                    _settings.SmoothingAngle = EditorGUILayout.Slider(T("SmoothingAngle"), _settings.SmoothingAngle, 0f, 180f);
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// インポート対象トグル描画
        /// </summary>
        private void DrawImportTargetToggle(PMXImportTarget target, string label)
        {
            bool current = (_settings.ImportTarget & target) != 0;
            bool newValue = EditorGUILayout.Toggle(label, current);
            if (newValue != current)
            {
                if (newValue)
                    _settings.ImportTarget |= target;
                else
                    _settings.ImportTarget &= ~target;
            }
        }

        // ================================================================
        // プレビューセクション
        // ================================================================

        private void DrawPreviewSection()
        {
            _foldPreview = EditorGUILayout.Foldout(_foldPreview, T("Preview"), true);
            if (!_foldPreview) return;

            EditorGUI.indentLevel++;

            if (_previewDocument == null)
            {
                EditorGUILayout.LabelField(T("SelectFileToPreview"), EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField(T("Version"), _previewDocument.Version.ToString("F1"));
                EditorGUILayout.LabelField(T("ModelName"), _previewDocument.ModelInfo.Name);
                EditorGUILayout.LabelField(T("Vertices"), _previewDocument.Vertices.Count.ToString());
                EditorGUILayout.LabelField(T("Faces"), _previewDocument.Faces.Count.ToString());

                EditorGUILayout.Space(3);

                // マテリアルリスト
                EditorGUILayout.LabelField(T("Materials"), EditorStyles.miniBoldLabel);
                int displayCount = Mathf.Min(_previewDocument.Materials.Count, 8);
                for (int i = 0; i < displayCount; i++)
                {
                    EditorGUILayout.LabelField($"  {_previewDocument.Materials[i].Name}", EditorStyles.miniLabel);
                }
                if (_previewDocument.Materials.Count > displayCount)
                {
                    EditorGUILayout.LabelField(T("AndMore", _previewDocument.Materials.Count - displayCount), EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(3);

                // ボーン数・モーフ数
                EditorGUILayout.LabelField(T("Bones"), _previewDocument.Bones.Count.ToString());
                EditorGUILayout.LabelField(T("Morphs"), _previewDocument.Morphs.Count.ToString());
            }

            EditorGUI.indentLevel--;
        }

        private void LoadPreview()
        {
            if (string.IsNullOrEmpty(_lastFilePath) || !File.Exists(_lastFilePath))
            {
                _previewDocument = null;
                return;
            }

            try
            {
                string ext = Path.GetExtension(_lastFilePath).ToLower();
                if (ext == ".pmx")
                {
                    // バイナリPMX
                    _previewDocument = PMXReader.Load(_lastFilePath);
                }
                else
                {
                    // CSV
                    _previewDocument = PMXCSVParser.ParseFile(_lastFilePath);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PMXImportPanel] Failed to load preview: {ex.Message}");
                _previewDocument = null;
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
            if (_settings.ImportMode == PMXImportMode.Replace)
            {
                GUI.backgroundColor = new Color(1f, 0.7f, 0.5f);
            }

            if (GUILayout.Button(T("Import"), buttonStyle))
            {
                Debug.Log($"[PMXImportPanel] Import button clicked. File: {_lastFilePath}");
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
            _lastResult = PMXImporter.ImportFile(_lastFilePath, _settings);

            if (_lastResult.Success)
            {
                Debug.Log($"[PMXImportPanel] Import successful: {_lastResult.MeshContexts.Count} meshes, " +
                          $"{_lastResult.Stats.TotalVertices} vertices, {_lastResult.Stats.TotalFaces} faces, " +
                          $"{_lastResult.Materials.Count} materials");

                // コンテキストがあれば追加
                if (_context != null)
                {
                    bool handled = false;

                    // ================================================================
                    // Replaceモード: 1回のUndoで戻せる置換
                    // ================================================================
                    if (_settings.ImportMode == PMXImportMode.Replace)
                    {
                        if (_context.ReplaceAllMeshContexts != null)
                        {
                            Debug.Log($"[PMXImportPanel] Replace mode: Replacing with {_lastResult.MeshContexts.Count} meshes");
                            _context.ReplaceAllMeshContexts.Invoke(_lastResult.MeshContexts);
                            handled = true;
                        }
                        else if (_context.ClearAllMeshContexts != null && _context.AddMeshContexts != null)
                        {
                            // フォールバック: Clear + Add（2回のUndo）
                            Debug.LogWarning("[PMXImportPanel] ReplaceAllMeshContexts not available, using Clear + Add");
                            _context.ClearAllMeshContexts.Invoke();
                            _context.AddMeshContexts.Invoke(_lastResult.MeshContexts);
                            handled = true;
                        }
                        else
                        {
                            Debug.LogWarning("[PMXImportPanel] Replace not available, falling back to Append mode");
                        }

                        // Replaceモードではマテリアルも置換
                        if (handled && _lastResult.Materials.Count > 0 && _context.ReplaceMaterials != null)
                        {
                            Debug.Log($"[PMXImportPanel] Replacing materials: {_lastResult.Materials.Count}");
                            _context.ReplaceMaterials.Invoke(_lastResult.Materials);
                        }

                        // Replaceモードではモーフセットも置換
                        if (handled && _lastResult.MorphSets.Count > 0 && _context.Model != null)
                        {
                            _context.Model.MorphSets = new List<Data.MorphSet>(_lastResult.MorphSets);
                            Debug.Log($"[PMXImportPanel] Replaced morph sets: {_lastResult.MorphSets.Count}");
                        }
                    }
                    // ================================================================
                    // NewModelモード: 新規モデルを作成してそこにメッシュを追加
                    // ================================================================
                    else if (_settings.ImportMode == PMXImportMode.NewModel)
                    {
                        // 新規モデルを作成
                        if (_context.CreateNewModel != null)
                        {
                            string modelName = Path.GetFileNameWithoutExtension(_lastFilePath);
                            var newModel = _context.CreateNewModel(modelName);
                            
                            Debug.Log($"[PMXImportPanel] NewModel mode: Created new model '{newModel?.Name}'");
                            
                            // 新しいモデルにマテリアルを設定（先にマテリアルを追加）
                            if (_lastResult.MaterialReferences.Count > 0)
                            {
                                if (newModel != null)
                                {
                                    newModel.MaterialReferences = new List<Materials.MaterialReference>(_lastResult.MaterialReferences);
                                    Debug.Log($"[PMXImportPanel] Set {_lastResult.MaterialReferences.Count} materials to new model");
                                }
                            }

                            // 新しいモデルにモーフセットを設定
                            if (_lastResult.MorphSets.Count > 0)
                            {
                                if (newModel != null)
                                {
                                    newModel.MorphSets = new List<Data.MorphSet>(_lastResult.MorphSets);
                                    Debug.Log($"[PMXImportPanel] Set {_lastResult.MorphSets.Count} morph sets to new model");
                                }
                            }
                            
                            // 新しいモデルにメッシュを追加
                            // 注: _context.Model は CreateNewModel で更新されている
                            if (_context.AddMeshContexts != null)
                            {
                                Debug.Log($"[PMXImportPanel] NewModel mode: Adding {_lastResult.MeshContexts.Count} meshes to new model");
                                _context.AddMeshContexts.Invoke(_lastResult.MeshContexts);
                            }
                            else
                            {
                                foreach (var meshContext in _lastResult.MeshContexts)
                                {
                                    Debug.Log($"[PMXImportPanel] Adding MeshContext: {meshContext.Name}");
                                    _context.AddMeshContext?.Invoke(meshContext);
                                }
                            }
                            handled = true;
                        }
                        else
                        {
                            Debug.LogWarning("[PMXImportPanel] CreateNewModel not available, falling back to Append mode");
                        }
                    }

                    // ================================================================
                    // Appendモード: 既存メッシュに追加（マテリアルインデックス補正あり）
                    // ================================================================
                    if (!handled)
                    {
                        // ★ 既存MeshContext数を取得してオフセットを適用
                        int existingMeshContextCount = 0;
                        if (_context.Model?.MeshContextList != null)
                        {
                            existingMeshContextCount = _context.Model.MeshContextList.Count;
                        }

                        // BoneWeightとボーン階層にオフセットを適用（既存MeshContextがある場合）
                        if (existingMeshContextCount > 0)
                        {
                            Debug.Log($"[PMXImportPanel] Append mode: Applying bone weight/hierarchy index offset +{existingMeshContextCount}");
                            _lastResult.ApplyBoneWeightIndexOffset(existingMeshContextCount);
                            _lastResult.ApplyBoneHierarchyOffset(existingMeshContextCount);
                        }

                        // ★ 既存マテリアル数を取得してオフセットを適用
                        int existingMaterialCount = 0;
                        if (_context.Model?.Materials != null)
                        {
                            existingMaterialCount = _context.Model.Materials.Count;
                        }

                        if (existingMaterialCount > 0 && _lastResult.Materials.Count > 0)
                        {
                            Debug.Log($"[PMXImportPanel] Append mode: Applying material index offset +{existingMaterialCount}");
                            _lastResult.ApplyMaterialIndexOffset(existingMaterialCount);
                        }

                        if (_context.AddMeshContexts != null)
                        {
                            Debug.Log($"[PMXImportPanel] Append mode: Adding {_lastResult.MeshContexts.Count} meshes");
                            _context.AddMeshContexts.Invoke(_lastResult.MeshContexts);
                        }
                        else
                        {
                            // フォールバック：1つずつ追加
                            foreach (var meshContext in _lastResult.MeshContexts)
                            {
                                Debug.Log($"[PMXImportPanel] Adding MeshContext: {meshContext.Name}");
                                _context.AddMeshContext?.Invoke(meshContext);
                            }
                        }

                        // Appendモードではマテリアルを追加（既存にマージ）
                        if (_lastResult.Materials.Count > 0 && _context.AddMaterials != null)
                        {
                            Debug.Log($"[PMXImportPanel] Adding materials: {_lastResult.Materials.Count}");
                            _context.AddMaterials.Invoke(_lastResult.Materials);
                        }

                        // Appendモードではモーフセットを追加（インデックス調整済み）
                        if (_lastResult.MorphSets.Count > 0 && _context.Model != null)
                        {
                            // MorphSets内のメッシュインデックスにオフセットを適用
                            foreach (var morphSet in _lastResult.MorphSets)
                            {
                                for (int i = 0; i < morphSet.MeshIndices.Count; i++)
                                {
                                    morphSet.MeshIndices[i] += existingMeshContextCount;
                                }
                                _context.Model.MorphSets.Add(morphSet);
                            }
                            Debug.Log($"[PMXImportPanel] Added {_lastResult.MorphSets.Count} morph sets");
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
                Debug.LogError($"[PMXImportPanel] Import failed: {_lastResult.ErrorMessage}");
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
                EditorGUILayout.LabelField(T("TotalVertices"), stats.TotalVertices.ToString());
                EditorGUILayout.LabelField(T("TotalFaces"), stats.TotalFaces.ToString());
                EditorGUILayout.LabelField(T("Materials"), stats.MaterialCount.ToString());
                EditorGUILayout.LabelField(T("MaterialGroups"), stats.MaterialGroupCount.ToString());
                EditorGUILayout.LabelField(T("Bones"), stats.BoneCount.ToString());
                EditorGUILayout.LabelField(T("Morphs"), stats.MorphCount.ToString());

                // MeshContextリスト
                if (_lastResult.MeshContexts.Count > 0)
                {
                    EditorGUILayout.Space(3);
                    EditorGUILayout.LabelField(T("ImportedMeshes"), EditorStyles.miniLabel);

                    int displayCount = Mathf.Min(_lastResult.MeshContexts.Count, 10);
                    for (int i = 0; i < displayCount; i++)
                    {
                        var mc = _lastResult.MeshContexts[i];
                        EditorGUILayout.LabelField($"  {mc.Name} (V:{mc.MeshObject?.VertexCount ?? 0} F:{mc.MeshObject?.FaceCount ?? 0})",
                            EditorStyles.miniLabel);
                    }

                    if (_lastResult.MeshContexts.Count > displayCount)
                    {
                        EditorGUILayout.LabelField(T("AndMore", _lastResult.MeshContexts.Count - displayCount), EditorStyles.miniLabel);
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
                var stored = _context.UndoController.EditorState.ToolSettings.Get<PMXImportSettings>(Name);
                if (stored != null)
                {
                    _settings.CopyFrom(stored);
                }
            }
        }
    }
}
