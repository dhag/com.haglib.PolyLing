// Assets/Editor/Poly_Ling/PMX/Export/PMXExportPanel.cs
// PMXエクスポートパネル

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Model;
using Poly_Ling.Localization;

namespace Poly_Ling.PMX
{
    public class PMXExportPanel : Tools.IToolPanelBase
    {
        public override string Name => "PMXExport";
        public override string Title => "PMX Export";
        public override string GetLocalizedTitle() => T("WindowTitle");

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"] = new() { ["en"] = "PMX Export", ["ja"] = "PMXエクスポート" },
            ["File"] = new() { ["en"] = "File", ["ja"] = "ファイル" },
            ["OutputPath"] = new() { ["en"] = "Output Path", ["ja"] = "出力先" },
            ["Browse"] = new() { ["en"] = "Browse...", ["ja"] = "参照..." },
            ["Export"] = new() { ["en"] = "Export", ["ja"] = "エクスポート" },
            ["ExportMode"] = new() { ["en"] = "Export Mode", ["ja"] = "エクスポートモード" },
            ["FullExport"] = new() { ["en"] = "Full Export", ["ja"] = "丸ごと出力" },
            ["PartialReplace"] = new() { ["en"] = "Partial Replace", ["ja"] = "部分差し替え" },
            ["Settings"] = new() { ["en"] = "Settings", ["ja"] = "設定" },
            ["Scale"] = new() { ["en"] = "Scale", ["ja"] = "スケール" },
            ["FlipZ"] = new() { ["en"] = "Flip Z Axis", ["ja"] = "Z軸反転" },
            ["FlipUV_V"] = new() { ["en"] = "Flip UV V", ["ja"] = "UV V反転" },
            ["FullExportOptions"] = new() { ["en"] = "Full Export Options", ["ja"] = "フル出力オプション" },
            ["ExportMaterials"] = new() { ["en"] = "Export Materials", ["ja"] = "マテリアル出力" },
            ["ExportBones"] = new() { ["en"] = "Export Bones", ["ja"] = "ボーン出力" },
            ["ExportMorphs"] = new() { ["en"] = "Export Morphs", ["ja"] = "モーフ出力" },
            ["UseRelativeTexturePath"] = new() { ["en"] = "Relative Texture Path", ["ja"] = "テクスチャ相対パス" },
            ["PartialReplaceOptions"] = new() { ["en"] = "Partial Replace Options", ["ja"] = "部分差し替えオプション" },
            ["SourcePMXFile"] = new() { ["en"] = "Source PMX File", ["ja"] = "元PMXファイル" },
            ["TargetMaterials"] = new() { ["en"] = "Target Materials", ["ja"] = "対象材質" },
            ["ReplacePositions"] = new() { ["en"] = "Replace Positions", ["ja"] = "座標を差し替え" },
            ["ReplaceNormals"] = new() { ["en"] = "Replace Normals", ["ja"] = "法線を差し替え" },
            ["ReplaceUVs"] = new() { ["en"] = "Replace UVs", ["ja"] = "UVを差し替え" },
            ["ReplaceBoneWeights"] = new() { ["en"] = "Replace Bone Weights", ["ja"] = "ウェイトを差し替え" },
            ["OutputFormat"] = new() { ["en"] = "Output Format", ["ja"] = "出力形式" },
            ["BinaryPMX"] = new() { ["en"] = "Binary PMX", ["ja"] = "バイナリPMX" },
            ["AlsoCSV"] = new() { ["en"] = "Also output CSV", ["ja"] = "CSVも出力" },
            ["Result"] = new() { ["en"] = "Result", ["ja"] = "結果" },
            ["ExportSuccess"] = new() { ["en"] = "Export successful!", ["ja"] = "エクスポート成功！" },
            ["ExportFailed"] = new() { ["en"] = "Export failed: {0}", ["ja"] = "失敗: {0}" },
            ["Vertices"] = new() { ["en"] = "Vertices", ["ja"] = "頂点数" },
            ["Faces"] = new() { ["en"] = "Faces", ["ja"] = "面数" },
            ["Materials"] = new() { ["en"] = "Materials", ["ja"] = "材質数" },
            ["Bones"] = new() { ["en"] = "Bones", ["ja"] = "ボーン数" },
            ["OpenFolder"] = new() { ["en"] = "Open Folder", ["ja"] = "フォルダを開く" },
            ["NoMeshContext"] = new() { ["en"] = "No mesh to export.", ["ja"] = "エクスポートするメッシュがありません。" },
            ["NoContextWarning"] = new() { ["en"] = "No context set.", ["ja"] = "コンテキスト未設定。" },
            ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        [NonSerialized] private PMXExportSettings _settings = new PMXExportSettings();
        [NonSerialized] private PMXExportResult _lastResult;
        [NonSerialized] private string _lastFilePath = "";
        [NonSerialized] private Vector2 _scrollPosition;
        [NonSerialized] private bool _foldSettings = true;
        [NonSerialized] private bool _foldFullOptions = true;
        [NonSerialized] private bool _foldPartialOptions = true;
        [NonSerialized] private bool _foldResult = false;

        [NonSerialized] private PMXDocument _sourcePMXDocument;
        [NonSerialized] private Dictionary<string, bool> _materialSelection = new Dictionary<string, bool>();

        public static void ShowWindow() => Open(null);

        public static void Open(Tools.ToolContext ctx)
        {
            var window = GetWindow<PMXExportPanel>();
            window.titleContent = new GUIContent(T("WindowTitle"));
            window.minSize = new Vector2(350, 450);
            if (ctx != null) window.SetContext(ctx);
            window.Show();
        }

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawFileSection();
            EditorGUILayout.Space(5);
            DrawModeSection();
            EditorGUILayout.Space(5);
            DrawSettingsSection();
            EditorGUILayout.Space(5);

            if (_settings.ExportMode == PMXExportMode.Full)
                DrawFullExportOptions();
            else
                DrawPartialReplaceOptions();

            EditorGUILayout.Space(10);
            DrawExportButton();
            EditorGUILayout.Space(5);
            DrawResultSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawFileSection()
        {
            EditorGUILayout.LabelField(T("File"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _lastFilePath = EditorGUILayout.TextField(T("OutputPath"), _lastFilePath);
                if (GUILayout.Button(T("Browse"), GUILayout.Width(70)))
                {
                    string dir = string.IsNullOrEmpty(_lastFilePath) ? Application.dataPath : Path.GetDirectoryName(_lastFilePath);
                    string path = EditorUtility.SaveFilePanel("Save PMX", dir, GetDefaultFileName(), "pmx");
                    if (!string.IsNullOrEmpty(path)) _lastFilePath = path;
                }
            }

            if (_context == null)
                EditorGUILayout.HelpBox(T("NoContextWarning"), MessageType.Warning);
            else if (!HasMeshToExport())
                EditorGUILayout.HelpBox(T("NoMeshContext"), MessageType.Warning);
        }

        private void DrawModeSection()
        {
            EditorGUILayout.LabelField(T("ExportMode"), EditorStyles.boldLabel);
            int modeIndex = (int)_settings.ExportMode;
            string[] modeNames = { T("FullExport"), T("PartialReplace") };
            int newMode = GUILayout.Toolbar(modeIndex, modeNames);
            if (newMode != modeIndex)
            {
                _settings.ExportMode = (PMXExportMode)newMode;
            }
        }

        private void DrawSettingsSection()
        {
            _foldSettings = EditorGUILayout.Foldout(_foldSettings, T("Settings"), true);
            if (!_foldSettings) return;

            EditorGUI.indentLevel++;
            _settings.Scale = EditorGUILayout.FloatField(T("Scale"), _settings.Scale);
            _settings.FlipZ = EditorGUILayout.Toggle(T("FlipZ"), _settings.FlipZ);
            _settings.FlipUV_V = EditorGUILayout.Toggle(T("FlipUV_V"), _settings.FlipUV_V);
            EditorGUI.indentLevel--;
        }

        private void DrawFullExportOptions()
        {
            _foldFullOptions = EditorGUILayout.Foldout(_foldFullOptions, T("FullExportOptions"), true);
            if (!_foldFullOptions) return;

            EditorGUI.indentLevel++;
            _settings.ExportMaterials = EditorGUILayout.Toggle(T("ExportMaterials"), _settings.ExportMaterials);
            _settings.ExportBones = EditorGUILayout.Toggle(T("ExportBones"), _settings.ExportBones);
            _settings.ExportMorphs = EditorGUILayout.Toggle(T("ExportMorphs"), _settings.ExportMorphs);
            _settings.UseRelativeTexturePath = EditorGUILayout.Toggle(T("UseRelativeTexturePath"), _settings.UseRelativeTexturePath);

            EditorGUILayout.Space(3);
            EditorGUILayout.LabelField(T("OutputFormat"));
            _settings.OutputBinaryPMX = EditorGUILayout.Toggle(T("BinaryPMX"), _settings.OutputBinaryPMX);
            _settings.OutputCSV = EditorGUILayout.Toggle(T("AlsoCSV"), _settings.OutputCSV);
            EditorGUI.indentLevel--;
        }

        private void DrawPartialReplaceOptions()
        {
            _foldPartialOptions = EditorGUILayout.Foldout(_foldPartialOptions, T("PartialReplaceOptions"), true);
            if (!_foldPartialOptions) return;

            EditorGUI.indentLevel++;

            // 元PMXファイル選択
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(T("SourcePMXFile"));
                var pmxRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _settings.SourcePMXPath = EditorGUI.TextField(pmxRect, _settings.SourcePMXPath);
                HandleDropOnRect(pmxRect, ".pmx", path =>
                {
                    _settings.SourcePMXPath = path;
                    LoadSourcePMX(path);
                });
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string path = EditorUtility.OpenFilePanel("Select Source PMX", "", "pmx");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _settings.SourcePMXPath = path;
                        LoadSourcePMX(path);
                    }
                }
            }

            // 材質選択
            if (_sourcePMXDocument != null && _sourcePMXDocument.Materials.Count > 0)
            {
                EditorGUILayout.LabelField(T("TargetMaterials"));
                EditorGUI.indentLevel++;
                foreach (var mat in _sourcePMXDocument.Materials)
                {
                    if (!_materialSelection.ContainsKey(mat.Name))
                        _materialSelection[mat.Name] = false;

                    _materialSelection[mat.Name] = EditorGUILayout.Toggle(mat.Name, _materialSelection[mat.Name]);
                }
                EditorGUI.indentLevel--;

                // 更新設定リスト
                _settings.ReplaceMaterialNames = _materialSelection
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();
            }

            EditorGUILayout.Space(3);
            _settings.ReplacePositions = EditorGUILayout.Toggle(T("ReplacePositions"), _settings.ReplacePositions);
            _settings.ReplaceNormals = EditorGUILayout.Toggle(T("ReplaceNormals"), _settings.ReplaceNormals);
            _settings.ReplaceUVs = EditorGUILayout.Toggle(T("ReplaceUVs"), _settings.ReplaceUVs);
            _settings.ReplaceBoneWeights = EditorGUILayout.Toggle(T("ReplaceBoneWeights"), _settings.ReplaceBoneWeights);

            EditorGUILayout.Space(3);
            _settings.OutputBinaryPMX = EditorGUILayout.Toggle(T("BinaryPMX"), _settings.OutputBinaryPMX);
            _settings.OutputCSV = EditorGUILayout.Toggle(T("AlsoCSV"), _settings.OutputCSV);

            EditorGUI.indentLevel--;
        }

        private void DrawExportButton()
        {
            using (new EditorGUI.DisabledScope(!CanExport()))
            {
                if (GUILayout.Button(T("Export"), GUILayout.Height(30)))
                {
                    ExecuteExport();
                }
            }
        }

        private void DrawResultSection()
        {
            if (_lastResult == null) return;

            _foldResult = EditorGUILayout.Foldout(_foldResult, T("Result"), true);
            if (!_foldResult) return;

            EditorGUI.indentLevel++;

            if (_lastResult.Success)
            {
                EditorGUILayout.HelpBox(T("ExportSuccess"), MessageType.Info);
                EditorGUILayout.LabelField(T("Vertices"), _lastResult.VertexCount.ToString());
                EditorGUILayout.LabelField(T("Faces"), _lastResult.FaceCount.ToString());
                EditorGUILayout.LabelField(T("Materials"), _lastResult.MaterialCount.ToString());
                EditorGUILayout.LabelField(T("Bones"), _lastResult.BoneCount.ToString());

                if (GUILayout.Button(T("OpenFolder")))
                {
                    string dir = Path.GetDirectoryName(_lastResult.OutputPath);
                    EditorUtility.RevealInFinder(dir);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(T("ExportFailed", _lastResult.ErrorMessage), MessageType.Error);
            }

            EditorGUI.indentLevel--;
        }

        private void LoadSourcePMX(string path)
        {
            try
            {
                _sourcePMXDocument = PMXReader.Load(path);
                _materialSelection.Clear();
                foreach (var mat in _sourcePMXDocument.Materials)
                {
                    _materialSelection[mat.Name] = false;
                }
                Debug.Log($"[PMXExportPanel] Loaded source PMX: {_sourcePMXDocument.Materials.Count} materials");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXExportPanel] Failed to load source PMX: {ex.Message}");
                _sourcePMXDocument = null;
            }
        }

        private void ExecuteExport()
        {
            if (string.IsNullOrEmpty(_lastFilePath))
            {
                Debug.LogError("[PMXExportPanel] Output path not set");
                return;
            }

            var model = GetModelContext();
            if (model == null)
            {
                Debug.LogError("[PMXExportPanel] No model context");
                return;
            }

            if (_settings.ExportMode == PMXExportMode.Full)
            {
                _lastResult = PMXExporter.Export(model, _lastFilePath, _settings);
            }
            else
            {
                if (string.IsNullOrEmpty(_settings.SourcePMXPath))
                {
                    _lastResult = new PMXExportResult { Success = false, ErrorMessage = "Source PMX not set" };
                    return;
                }
                _lastResult = PMXExporter.ExportPartialReplace(model, _settings.SourcePMXPath, _lastFilePath, _settings);
            }

            _foldResult = true;
            Repaint();
        }

        private bool CanExport()
        {
            if (_context == null) return false;
            if (!HasMeshToExport()) return false;
            if (string.IsNullOrEmpty(_lastFilePath)) return false;

            if (_settings.ExportMode == PMXExportMode.PartialReplace)
            {
                if (string.IsNullOrEmpty(_settings.SourcePMXPath)) return false;
                if (_settings.ReplaceMaterialNames.Count == 0) return false;
            }

            return true;
        }

        private bool HasMeshToExport()
        {
            var model = GetModelContext();
            return model?.MeshContextList != null && model.MeshContextList.Count > 0;
        }

        private ModelContext GetModelContext()
        {
            return _context?.Model;
        }

        private string GetDefaultFileName()
        {
            var model = GetModelContext();
            string name = model?.Name ?? "export";
            return name + ".pmx";
        }

        /// <summary>
        /// 指定矩形へのドロップを処理
        /// </summary>
        private void HandleDropOnRect(Rect rect, string extension, Action<string> onDrop)
        {
            var evt = Event.current;
            if (!rect.Contains(evt.mousePosition)) return;

            switch (evt.type)
            {
                case EventType.DragUpdated:
                    if (DragAndDrop.paths.Length > 0 &&
                        Path.GetExtension(DragAndDrop.paths[0]).ToLower() == extension)
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.Use();
                    }
                    break;

                case EventType.DragPerform:
                    if (DragAndDrop.paths.Length > 0)
                    {
                        string path = DragAndDrop.paths[0];
                        if (Path.GetExtension(path).ToLower() == extension)
                        {
                            DragAndDrop.AcceptDrag();
                            onDrop(path);
                            evt.Use();
                        }
                    }
                    break;
            }
        }
    }
}
