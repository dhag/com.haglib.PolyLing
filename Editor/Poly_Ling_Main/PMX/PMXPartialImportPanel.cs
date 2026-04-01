// Assets/Editor/Poly_Ling/PMX/PMXPartialImportPanel.cs
// PMX部分インポートパネル
// ロジックは PMXPartialImportOps に委譲。
// UI描画・ファイルIO・Undo記録・_context同期のみをここで行う。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Localization;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;
using Poly_Ling.MQO;  // PartialMeshEntry

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMX部分インポートパネル
    /// </summary>
    public class PMXPartialImportPanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"]       = new() { ["en"] = "PMX Partial Import",         ["ja"] = "PMX部分インポート" },
            ["ReferencePMX"]      = new() { ["en"] = "Reference PMX",              ["ja"] = "リファレンスPMX" },
            ["Options"]           = new() { ["en"] = "Options",                    ["ja"] = "オプション" },
            ["ImportTarget"]      = new() { ["en"] = "Import Target",              ["ja"] = "インポート対象" },
            ["PMXFile"]           = new() { ["en"] = "PMX File",                   ["ja"] = "PMXファイル" },
            ["Scale"]             = new() { ["en"] = "Scale",                      ["ja"] = "スケール" },
            ["FlipZ"]             = new() { ["en"] = "Flip Z",                     ["ja"] = "Z反転" },
            ["PMXMeshes"]         = new() { ["en"] = "PMX Meshes",                 ["ja"] = "PMXメッシュ" },
            ["ModelMeshes"]       = new() { ["en"] = "Model Meshes",               ["ja"] = "モデルメッシュ" },
            ["VertexPosition"]    = new() { ["en"] = "Vertex Position",            ["ja"] = "頂点位置" },
            ["UV"]                = new() { ["en"] = "UV",                         ["ja"] = "UV" },
            ["BoneWeight"]        = new() { ["en"] = "BoneWeight",                 ["ja"] = "BoneWeight" },
            ["FaceStructure"]     = new() { ["en"] = "Face Structure",             ["ja"] = "面構成" },
            ["MaterialContent"]   = new() { ["en"] = "Material Content (by name)", ["ja"] = "材質内容（名前マッチング）" },
            ["Import"]            = new() { ["en"] = "Import",                     ["ja"] = "インポート" },
            ["SelectAll"]         = new() { ["en"] = "All",                        ["ja"] = "全" },
            ["SelectNone"]        = new() { ["en"] = "None",                       ["ja"] = "無" },
            ["AutoMatch"]         = new() { ["en"] = "Auto",                       ["ja"] = "自動" },
            ["Selection"]         = new() { ["en"] = "Selection: Model {0} ↔ PMX {1}", ["ja"] = "選択: モデル {0} ↔ PMX {1}" },
            ["VertexMismatch"]    = new() { ["en"] = "Vertex mismatch: Model({0}) ≠ PMX({1}) — min imported", ["ja"] = "頂点数不一致: モデル({0}) ≠ PMX({1}) — 少ない方を転送" },
            ["NothingSelected"]   = new() { ["en"] = "Select at least one import target", ["ja"] = "インポート対象を1つ以上選択してください" },
            ["ImportSuccess"]     = new() { ["en"] = "Import: {0}",               ["ja"] = "インポート完了: {0}" },
            ["ImportFailed"]      = new() { ["en"] = "Import failed: {0}",        ["ja"] = "インポート失敗: {0}" },
            ["MaterialMatchResult"] = new() { ["en"] = "Material: {0} / {1} matched", ["ja"] = "材質: {0} / {1} マッチ" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // フィールド
        // ================================================================

        private ToolContext            _context;
        private string                 _pmxFilePath = "";
        private readonly PMXPartialImportOps _ops = new PMXPartialImportOps();

        // ファイル読み込み用オプション（ImportSettings に渡す）
        private float _importScale = 1.0f;
        private bool  _flipZ       = false;

        // インポート対象チェックボックス
        private bool _importVertexPosition  = true;
        private bool _importUV              = false;
        private bool _importBoneWeight      = false;
        private bool _importFaceStructure   = false;
        private bool _importMaterialContent = false;

        // UI 状態
        private Vector2 _scrollLeft;
        private Vector2 _scrollRight;
        private string  _lastResult = "";

        // ================================================================
        // プロパティ
        // ================================================================

        private ModelContext Model => _context?.Model;

        private bool NeedsMeshMatching =>
            _importVertexPosition || _importUV || _importBoneWeight || _importFaceStructure;

        private bool HasAnyTarget =>
            _importVertexPosition || _importUV || _importBoneWeight ||
            _importFaceStructure  || _importMaterialContent;

        // ================================================================
        // Open
        // ================================================================

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<PMXPartialImportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize      = new Vector2(700, 500);
            panel._context     = ctx;
            panel.InitFromContext();
            panel._ops.BuildModelList(ctx?.Model);
            panel.Show();
        }

        public void SetContext(ToolContext ctx)
        {
            _context = ctx;
            InitFromContext();
            _ops.BuildModelList(Model);
            if (_ops.PMXImportResult != null)
                _ops.AutoMatch();
        }

        private void InitFromContext()
        {
            // PMXのデフォルト: Scale=1, FlipZ=false
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            DrawPMXFileSection();
            EditorGUILayout.Space(5);

            DrawImportTargetSection();
            EditorGUILayout.Space(5);

            DrawOptionsSection();
            EditorGUILayout.Space(5);

            if (NeedsMeshMatching)
            {
                DrawDualListSection();
                EditorGUILayout.Space(5);
            }

            if (_importMaterialContent)
            {
                DrawMaterialMatchPreview();
                EditorGUILayout.Space(5);
            }

            DrawImportSection();
        }

        // ================================================================
        // PMXファイルセクション
        // ================================================================

        private void DrawPMXFileSection()
        {
            EditorGUILayout.LabelField(T("ReferencePMX"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                string newPath = EditorGUILayout.TextField(T("PMXFile"), _pmxFilePath);
                if (newPath != _pmxFilePath)
                {
                    _pmxFilePath = newPath;
                    if (File.Exists(_pmxFilePath)) LoadPMXAndMatch();
                }

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string path = EditorUtility.OpenFilePanel("Select PMX", "", "pmx");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _pmxFilePath = path;
                        LoadPMXAndMatch();
                    }
                }

                using (new EditorGUI.DisabledScope(
                    string.IsNullOrEmpty(_pmxFilePath) || !File.Exists(_pmxFilePath)))
                {
                    if (GUILayout.Button("↻", GUILayout.Width(25)))
                        LoadPMXAndMatch();
                }
            }

            // ドラッグ＆ドロップ
            var dropArea = GUILayoutUtility.GetLastRect();
            var evt      = Event.current;
            if (evt.type == EventType.DragUpdated && dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                evt.Use();
            }
            else if (evt.type == EventType.DragPerform && dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.AcceptDrag();
                foreach (var path in DragAndDrop.paths)
                {
                    if (path.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase))
                    {
                        _pmxFilePath = path;
                        LoadPMXAndMatch();
                        break;
                    }
                }
                evt.Use();
            }
        }

        // ================================================================
        // インポート対象セクション
        // ================================================================

        private void DrawImportTargetSection()
        {
            EditorGUILayout.LabelField(T("ImportTarget"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _importVertexPosition  = EditorGUILayout.ToggleLeft(T("VertexPosition"),  _importVertexPosition,  GUILayout.Width(100));
                _importUV              = EditorGUILayout.ToggleLeft(T("UV"),              _importUV,              GUILayout.Width(60));
                _importBoneWeight      = EditorGUILayout.ToggleLeft(T("BoneWeight"),      _importBoneWeight,      GUILayout.Width(110));
                _importFaceStructure   = EditorGUILayout.ToggleLeft(T("FaceStructure"),   _importFaceStructure,   GUILayout.Width(100));
                _importMaterialContent = EditorGUILayout.ToggleLeft(T("MaterialContent"), _importMaterialContent);
            }
        }

        // ================================================================
        // オプションセクション
        // ================================================================

        private void DrawOptionsSection()
        {
            EditorGUILayout.LabelField(T("Options"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                _importScale = EditorGUILayout.FloatField(T("Scale"), _importScale, GUILayout.Width(200));
                _flipZ       = EditorGUILayout.ToggleLeft(T("FlipZ"), _flipZ, GUILayout.Width(80));
            }
        }

        // ================================================================
        // 左右リスト
        // ================================================================

        private void DrawDualListSection()
        {
            if (_context == null || Model == null) return;
            if (_ops.PMXImportResult == null)
            {
                EditorGUILayout.HelpBox("Select a PMX file first", MessageType.Info);
                return;
            }

            float halfWidth = (position.width - 30) / 2;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(T("AutoMatch"), GUILayout.Width(60)))
                    _ops.AutoMatch();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                // 左: PMXメッシュ
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(halfWidth)))
                {
                    DrawListHeader(T("PMXMeshes"), isModel: false);
                    _scrollLeft = EditorGUILayout.BeginScrollView(_scrollLeft, GUILayout.Height(300));
                    DrawPMXList();
                    EditorGUILayout.EndScrollView();
                }

                // 右: モデルメッシュ
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(halfWidth)))
                {
                    DrawListHeader(T("ModelMeshes"), isModel: true);
                    _scrollRight = EditorGUILayout.BeginScrollView(_scrollRight, GUILayout.Height(300));
                    DrawModelList();
                    EditorGUILayout.EndScrollView();
                }
            }
        }

        private void DrawListHeader(string title, bool isModel)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(T("SelectAll"), GUILayout.Width(50)))
                {
                    if (isModel)
                        foreach (var e in _ops.ModelMeshes) e.Selected = true;
                    else
                        foreach (var e in _ops.PMXMeshes)   e.Selected = true;
                }
                if (GUILayout.Button(T("SelectNone"), GUILayout.Width(50)))
                {
                    if (isModel)
                        foreach (var e in _ops.ModelMeshes) e.Selected = false;
                    else
                        foreach (var e in _ops.PMXMeshes)   e.Selected = false;
                }
            }
        }

        private void DrawPMXList()
        {
            foreach (var entry in _ops.PMXMeshes)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));
                    EditorGUILayout.LabelField($"{entry.Name} ({entry.VertexCount})");
                }
            }
        }

        private void DrawModelList()
        {
            foreach (var entry in _ops.ModelMeshes)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(20));

                    string label;
                    if (entry.BakedMirrorPeer != null)
                    {
                        label = $"{entry.Name} (+ {entry.BakedMirrorPeer.Name}) [{entry.TotalExpandedVertexCount}]";
                        GUI.color = new Color(1f, 0.85f, 0.6f);
                    }
                    else
                    {
                        label = $"{entry.Name} ({entry.ExpandedVertexCount})";
                    }

                    EditorGUILayout.LabelField(label);
                    GUI.color = Color.white;
                }
            }
        }

        // ================================================================
        // 材質マッチングプレビュー
        // ================================================================

        private void DrawMaterialMatchPreview()
        {
            if (_ops.PMXImportResult?.Document == null || Model == null) return;

            var matches = _ops.BuildMaterialMatches(Model);
            EditorGUILayout.LabelField(
                T("MaterialMatchResult", matches.Count, _ops.PMXImportResult.Document.Materials.Count),
                EditorStyles.miniLabel);

            foreach (var match in matches)
            {
                EditorGUILayout.LabelField(
                    $"  {match.PmxMatName} → {match.ModelMatRef.Name}",
                    EditorStyles.miniLabel);
            }
        }

        // ================================================================
        // インポートセクション
        // ================================================================

        private void DrawImportSection()
        {
            if (!HasAnyTarget)
            {
                EditorGUILayout.HelpBox(T("NothingSelected"), MessageType.Info);
                return;
            }

            if (NeedsMeshMatching)
            {
                int modelCount = _ops.ModelMeshes.Count(m => m.Selected);
                int pmxCount   = _ops.PMXMeshes.Count(m => m.Selected);
                int modelVerts = _ops.SelectedModelVertexCount;
                int pmxVerts   = _ops.SelectedPMXVertexCount;

                EditorGUILayout.LabelField(
                    T("Selection", modelCount, pmxCount) + $"  Verts: {modelVerts} ← {pmxVerts}");

                bool vertexAttrOnly =
                    (_importVertexPosition || _importUV || _importBoneWeight) && !_importFaceStructure;
                if (vertexAttrOnly && modelVerts != pmxVerts && modelCount > 0 && pmxCount > 0)
                {
                    EditorGUILayout.HelpBox(
                        T("VertexMismatch", modelVerts, pmxVerts), MessageType.Warning);
                }
            }

            bool canImport = _ops.PMXImportResult != null;
            if (NeedsMeshMatching)
            {
                canImport &= _ops.ModelMeshes.Any(m => m.Selected) &&
                             _ops.PMXMeshes.Any(m => m.Selected);
            }

            using (new EditorGUI.DisabledScope(!canImport))
            {
                if (GUILayout.Button(T("Import"), GUILayout.Height(30)))
                    ExecuteImport();
            }

            if (!string.IsNullOrEmpty(_lastResult))
                EditorGUILayout.HelpBox(_lastResult, MessageType.Info);
        }

        // ================================================================
        // PMX読み込みと自動照合
        // ================================================================

        private void LoadPMXAndMatch()
        {
            if (string.IsNullOrEmpty(_pmxFilePath) || !File.Exists(_pmxFilePath))
                return;

            try
            {
                var settings = new PMXImportSettings
                {
                    ImportMode           = PMXImportMode.NewModel,
                    ImportTarget         = PMXImportTarget.Mesh,
                    ImportMaterials      = false,
                    FlipZ                = _flipZ,
                    Scale                = _importScale,
                    RecalculateNormals   = false,
                    DetectNamedMirror    = true,
                    UseObjectNameGrouping = true
                };

                var result = PMXImporter.ImportFile(_pmxFilePath, settings);

                if (result == null || !result.Success)
                {
                    Debug.LogError($"[PMXPartialImport] Import failed: {result?.ErrorMessage}");
                    return;
                }

                _ops.LoadPMXResult(result);

                if (_ops.ModelMeshes.Count == 0)
                    _ops.BuildModelList(Model);

                _ops.AutoMatch();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PMXPartialImport] Load failed: {ex.Message}");
            }

            Repaint();
        }

        // ================================================================
        // インポート実行
        // ================================================================

        private void ExecuteImport()
        {
            try
            {
                var results        = new List<string>();
                var selectedModels = _ops.SelectedModelMeshes;
                var selectedPMXs   = _ops.SelectedPMXMeshes;
                bool topologyChanged = false;

                var beforeSnapshot = MultiMeshVertexSnapshot.Capture(Model);

                if (selectedModels.Count > 0 && selectedPMXs.Count > 0)
                {
                    if (_importFaceStructure)
                    {
                        int count = _ops.ExecuteFaceStructureImport(selectedModels, selectedPMXs);
                        results.Add($"Faces: {count} meshes");
                        topologyChanged = true;
                    }

                    int vertCount = _ops.ExecuteVertexAttributeImport(
                        selectedModels, selectedPMXs,
                        _importVertexPosition, _importUV, _importBoneWeight);
                    if (vertCount > 0)
                    {
                        var attrs = new List<string>();
                        if (_importVertexPosition) attrs.Add("Pos");
                        if (_importUV)             attrs.Add("UV");
                        if (_importBoneWeight)     attrs.Add("BW");
                        results.Add($"{string.Join("+", attrs)}: {vertCount} verts");
                    }
                }

                if (_importMaterialContent)
                {
                    int count = _ops.ExecuteMaterialImport(Model);
                    results.Add(T("MaterialMatchResult",
                        count, _ops.PMXImportResult.Document.Materials.Count));
                }

                if (topologyChanged)
                    _context?.OnTopologyChanged();
                else if (_importVertexPosition || _importUV || _importBoneWeight)
                    _context?.SyncMesh?.Invoke();

                _context?.Repaint?.Invoke();
                SceneView.RepaintAll();

                var undo = _context?.UndoController;
                if (undo != null)
                {
                    var afterSnapshot = MultiMeshVertexSnapshot.Capture(Model);
                    var record = new MultiMeshVertexSnapshotRecord(
                        beforeSnapshot, afterSnapshot, "PMX Partial Import");
                    undo.MeshListStack.Record(record, "PMX Partial Import");
                }

                _lastResult = T("ImportSuccess", string.Join(", ", results));
            }
            catch (Exception ex)
            {
                _lastResult = T("ImportFailed", ex.Message);
                Debug.LogError($"[PMXPartialImport] {ex.Message}\n{ex.StackTrace}");
            }

            Repaint();
        }
    }
}
