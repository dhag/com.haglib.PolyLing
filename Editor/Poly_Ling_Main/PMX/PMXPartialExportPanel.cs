// Assets/Editor/Poly_Ling/PMX/PMXPartialExportPanel.cs
// PMX部分エクスポートパネル
// ロジックは PMXPartialExportOps に委譲。
// UI描画・ファイルIO・PMX読み込み・コンテキスト同期のみをここで行う。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Localization;
using Poly_Ling.Tools;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMX部分エクスポートパネル
    /// </summary>
    public class PMXPartialExportPanel : IToolPanelBase
    {
        // ================================================================
        // IToolPanel実装
        // ================================================================

        public override string Name => "PMXPartialExport";
        public override string Title => "PMX Partial Export";
        public override string GetLocalizedTitle() => T("WindowTitle");

        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"]        = new() { ["en"] = "PMX Partial Export",              ["ja"] = "PMX部分エクスポート" },
            ["ReferencePMX"]       = new() { ["en"] = "Reference PMX",                   ["ja"] = "リファレンスPMX" },
            ["MeshMapping"]        = new() { ["en"] = "Mesh ↔ Material Mapping",          ["ja"] = "メッシュ ↔ 材質対応" },
            ["ExportOptions"]      = new() { ["en"] = "Export Options",                   ["ja"] = "出力オプション" },
            ["PMXFile"]            = new() { ["en"] = "PMX File",                         ["ja"] = "PMXファイル" },
            ["ModelMeshes"]        = new() { ["en"] = "Model Meshes",                     ["ja"] = "モデルメッシュ" },
            ["PMXMaterials"]       = new() { ["en"] = "PMX Materials",                    ["ja"] = "PMX材質" },
            ["Vertices"]           = new() { ["en"] = "V",                                ["ja"] = "V" },
            ["Match"]              = new() { ["en"] = "✓",                               ["ja"] = "✓" },
            ["Mismatch"]           = new() { ["en"] = "✗",                               ["ja"] = "✗" },
            ["Scale"]              = new() { ["en"] = "Scale",                            ["ja"] = "スケール" },
            ["FlipZ"]              = new() { ["en"] = "Flip Z",                           ["ja"] = "Z軸反転" },
            ["FlipUV_V"]           = new() { ["en"] = "Flip UV V",                        ["ja"] = "UV V反転" },
            ["ReplacePositions"]   = new() { ["en"] = "Replace Positions",                ["ja"] = "座標を出力" },
            ["ReplaceNormals"]     = new() { ["en"] = "Replace Normals",                  ["ja"] = "法線を出力" },
            ["ReplaceUVs"]         = new() { ["en"] = "Replace UVs",                      ["ja"] = "UVを出力" },
            ["ReplaceBoneWeights"] = new() { ["en"] = "Replace Weights",                  ["ja"] = "ウェイトを出力" },
            ["OutputCSV"]          = new() { ["en"] = "Also output CSV",                  ["ja"] = "CSVも出力" },
            ["SelectAll"]          = new() { ["en"] = "Select All",                       ["ja"] = "全選択" },
            ["SelectNone"]         = new() { ["en"] = "Select None",                      ["ja"] = "全解除" },
            ["SelectMatched"]      = new() { ["en"] = "Select Matched",                   ["ja"] = "一致のみ選択" },
            ["Export"]             = new() { ["en"] = "Export PMX",                       ["ja"] = "PMXエクスポート" },
            ["NoContext"]          = new() { ["en"] = "No context set.",                   ["ja"] = "コンテキスト未設定" },
            ["NoModel"]            = new() { ["en"] = "No model loaded",                  ["ja"] = "モデルがありません" },
            ["NoDrawableMesh"]     = new() { ["en"] = "No drawable meshes",               ["ja"] = "Drawableメッシュがありません" },
            ["SelectPMXFirst"]     = new() { ["en"] = "Select reference PMX file",        ["ja"] = "リファレンスPMXを選択してください" },
            ["NoMeshSelected"]     = new() { ["en"] = "Select meshes to export",          ["ja"] = "エクスポートするメッシュを選択" },
            ["ExportSuccess"]      = new() { ["en"] = "Export successful: {0}",           ["ja"] = "エクスポート成功: {0}" },
            ["ExportFailed"]       = new() { ["en"] = "Export failed: {0}",               ["ja"] = "エクスポート失敗: {0}" },
            ["ScaleTooltip"]       = new() { ["en"] = "Unity coordinates × Scale = PMX coordinates", ["ja"] = "Unity座標 × スケール = PMX座標" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // フィールド
        // ================================================================

        private string      _pmxFilePath = "";
        private PMXDocument _pmxDocument;

        private readonly PMXPartialExportOps      _ops      = new PMXPartialExportOps();
        private          List<MeshMaterialMapping> _mappings = new List<MeshMaterialMapping>();

        // 出力オプション
        private float _scale              = 10f;
        private bool  _flipZ              = true;
        private bool  _flipUV_V           = true;
        private bool  _replacePositions   = true;
        private bool  _replaceNormals     = true;
        private bool  _replaceUVs         = true;
        private bool  _replaceBoneWeights = true;
        private bool  _outputCSV          = false;

        // UI 状態
        private Vector2 _scrollPosition;
        private string  _lastResult = "";

        // ================================================================
        // Open
        // ================================================================

        public static void ShowWindow() => Open(null);

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<PMXPartialExportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize      = new Vector2(550, 450);
            if (ctx != null) panel.SetContext(ctx);
            panel.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawPMXFileSection();
            EditorGUILayout.Space(10);

            DrawMappingSection();
            EditorGUILayout.Space(10);

            DrawOptionsSection();
            EditorGUILayout.Space(10);

            DrawExportSection();

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // PMXファイルセクション
        // ================================================================

        private void DrawPMXFileSection()
        {
            EditorGUILayout.LabelField(T("ReferencePMX"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(T("PMXFile"));
                var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _pmxFilePath = EditorGUI.TextField(rect, _pmxFilePath);

                HandleDropOnRect(rect, ".pmx", path =>
                {
                    _pmxFilePath = path;
                    LoadPMX();
                    RebuildMappings();
                });

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir  = string.IsNullOrEmpty(_pmxFilePath) ? Application.dataPath : Path.GetDirectoryName(_pmxFilePath);
                    string path = EditorUtility.OpenFilePanel("Select Reference PMX", dir, "pmx");
                    if (!string.IsNullOrEmpty(path)) { _pmxFilePath = path; LoadPMX(); RebuildMappings(); }
                }
            }

            if (_pmxDocument != null)
            {
                EditorGUILayout.LabelField(
                    $"Materials: {_pmxDocument.Materials.Count}, Vertices: {_pmxDocument.Vertices.Count}",
                    EditorStyles.miniLabel);
            }
        }

        // ================================================================
        // マッピングセクション
        // ================================================================

        private void DrawMappingSection()
        {
            EditorGUILayout.LabelField(T("MeshMapping"), EditorStyles.boldLabel);

            if (_context == null)          { EditorGUILayout.HelpBox(T("NoContext"),      MessageType.Warning); return; }
            if (Model == null)             { EditorGUILayout.HelpBox(T("NoModel"),        MessageType.Warning); return; }
            var drawables = Model.DrawableMeshes;
            if (drawables == null || drawables.Count == 0)
                                           { EditorGUILayout.HelpBox(T("NoDrawableMesh"), MessageType.Info);    return; }
            if (_pmxDocument == null)      { EditorGUILayout.HelpBox(T("SelectPMXFirst"), MessageType.Info);    return; }

            if (_mappings.Count == 0) RebuildMappings();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(T("SelectAll"),     GUILayout.Width(80)))  foreach (var m in _mappings) m.Selected = true;
                if (GUILayout.Button(T("SelectNone"),    GUILayout.Width(80)))  foreach (var m in _mappings) m.Selected = false;
                if (GUILayout.Button(T("SelectMatched"), GUILayout.Width(100))) foreach (var m in _mappings) m.Selected = m.IsMatched;
            }

            EditorGUILayout.Space(5);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("",              GUILayout.Width(20));
                EditorGUILayout.LabelField(T("ModelMeshes"), EditorStyles.miniLabel, GUILayout.Width(150));
                EditorGUILayout.LabelField(T("Vertices"),    EditorStyles.miniLabel, GUILayout.Width(60));
                EditorGUILayout.LabelField("→",             EditorStyles.miniLabel, GUILayout.Width(20));
                EditorGUILayout.LabelField(T("PMXMaterials"),EditorStyles.miniLabel, GUILayout.Width(150));
                EditorGUILayout.LabelField(T("Vertices"),    EditorStyles.miniLabel, GUILayout.Width(60));
                EditorGUILayout.LabelField("",              GUILayout.Width(30));
            }

            foreach (var mapping in _mappings) DrawMappingRow(mapping);
        }

        private void DrawMappingRow(MeshMaterialMapping mapping)
        {
            var originalBg = GUI.backgroundColor;
            if (!mapping.IsMatched && !string.IsNullOrEmpty(mapping.PMXMaterialName))
                GUI.backgroundColor = new Color(1f, 0.7f, 0.7f);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                mapping.Selected = EditorGUILayout.Toggle(mapping.Selected, GUILayout.Width(20));
                EditorGUILayout.LabelField(mapping.MeshName,                            GUILayout.Width(150));
                EditorGUILayout.LabelField(mapping.MeshExpandedVertexCount.ToString(),  GUILayout.Width(60));
                EditorGUILayout.LabelField("→",                                         GUILayout.Width(20));
                EditorGUILayout.LabelField(mapping.PMXMaterialName ?? "(none)",         GUILayout.Width(150));
                EditorGUILayout.LabelField(mapping.PMXVertexCount.ToString(),           GUILayout.Width(60));

                if (!string.IsNullOrEmpty(mapping.PMXMaterialName))
                {
                    GUI.contentColor = mapping.IsMatched ? Color.green : Color.red;
                    EditorGUILayout.LabelField(mapping.IsMatched ? T("Match") : T("Mismatch"), GUILayout.Width(30));
                    GUI.contentColor = Color.white;
                }
                else
                {
                    EditorGUILayout.LabelField("", GUILayout.Width(30));
                }
            }

            GUI.backgroundColor = originalBg;
        }

        // ================================================================
        // オプションセクション
        // ================================================================

        private void DrawOptionsSection()
        {
            EditorGUILayout.LabelField(T("ExportOptions"), EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            _scale              = EditorGUILayout.FloatField(new GUIContent(T("Scale"), T("ScaleTooltip")), _scale);
            _flipZ              = EditorGUILayout.Toggle(T("FlipZ"),              _flipZ);
            _flipUV_V           = EditorGUILayout.Toggle(T("FlipUV_V"),           _flipUV_V);
            EditorGUILayout.Space(3);
            _replacePositions   = EditorGUILayout.Toggle(T("ReplacePositions"),   _replacePositions);
            _replaceNormals     = EditorGUILayout.Toggle(T("ReplaceNormals"),     _replaceNormals);
            _replaceUVs         = EditorGUILayout.Toggle(T("ReplaceUVs"),         _replaceUVs);
            _replaceBoneWeights = EditorGUILayout.Toggle(T("ReplaceBoneWeights"), _replaceBoneWeights);
            EditorGUILayout.Space(3);
            _outputCSV          = EditorGUILayout.Toggle(T("OutputCSV"),          _outputCSV);

            EditorGUI.indentLevel--;
        }

        // ================================================================
        // エクスポートセクション
        // ================================================================

        private void DrawExportSection()
        {
            int selectedCount        = _mappings.Count(m => m.Selected);
            int matchedSelectedCount = _mappings.Count(m => m.Selected && m.IsMatched);

            if (selectedCount == 0)
                EditorGUILayout.HelpBox(T("NoMeshSelected"), MessageType.Info);
            else if (matchedSelectedCount < selectedCount)
                EditorGUILayout.HelpBox($"Selected: {selectedCount}, Matched: {matchedSelectedCount}", MessageType.Warning);

            using (new EditorGUI.DisabledScope(matchedSelectedCount == 0 || _pmxDocument == null))
            {
                if (GUILayout.Button(T("Export"), GUILayout.Height(30)))
                    ExecuteExport();
            }

            if (!string.IsNullOrEmpty(_lastResult))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(_lastResult, MessageType.Info);
            }
        }

        // ================================================================
        // PMX読み込み
        // ================================================================

        private void LoadPMX()
        {
            _pmxDocument = null;
            if (string.IsNullOrEmpty(_pmxFilePath) || !File.Exists(_pmxFilePath)) return;

            try
            {
                _pmxDocument = PMXReader.Load(_pmxFilePath);
                Debug.Log(
                    $"[PMXPartialExport] Loaded PMX: " +
                    $"{_pmxDocument.Materials.Count} materials, {_pmxDocument.Vertices.Count} vertices");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXPartialExport] Failed to load PMX: {ex.Message}");
            }
        }

        private void RebuildMappings()
        {
            _mappings = _ops.BuildMappings(Model, _pmxDocument);
        }

        // ================================================================
        // エクスポート実行
        // ================================================================

        private void ExecuteExport()
        {
            try
            {
                string defaultName = Path.GetFileNameWithoutExtension(_pmxFilePath) + "_modified.pmx";
                string savePath    = EditorUtility.SaveFilePanel(
                    "Save PMX", Path.GetDirectoryName(_pmxFilePath), defaultName, "pmx");

                if (string.IsNullOrEmpty(savePath)) return;

                int totalTransferred = _ops.ExecuteExport(
                    _mappings, _pmxDocument,
                    _scale, _flipZ, _flipUV_V,
                    _replacePositions, _replaceNormals, _replaceUVs, _replaceBoneWeights);

                PMXWriter.Save(_pmxDocument, savePath);

                if (_outputCSV)
                {
                    string csvPath = Path.ChangeExtension(savePath, ".csv");
                    PMXCSVWriter.Save(_pmxDocument, csvPath);
                }

                _lastResult = T("ExportSuccess",
                    $"{totalTransferred} vertices → {Path.GetFileName(savePath)}");
                Debug.Log($"[PMXPartialExport] Export completed: {totalTransferred} vertices");

                // PMXを再ロードして編集内容をリセット
                LoadPMX();
            }
            catch (Exception ex)
            {
                _lastResult = T("ExportFailed", ex.Message);
                Debug.LogError($"[PMXPartialExport] {ex.Message}\n{ex.StackTrace}");
            }

            Repaint();
        }

        // ================================================================
        // ドロップ処理
        // ================================================================

        private static void HandleDropOnRect(Rect rect, string extension, Action<string> onDrop)
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

        // ================================================================
        // コンテキスト変更時
        // ================================================================

        protected override void OnContextSet()
        {
            var es = _context?.UndoController?.EditorState;
            if (es != null)
            {
                _scale = es.PmxUnityRatio > 0f ? 1f / es.PmxUnityRatio : 10f;
                _flipZ = es.PmxFlipZ;
            }

            _mappings.Clear();
            if (_pmxDocument != null) RebuildMappings();
        }
    }
}
