// Assets/Editor/Poly_Ling/MQO/Import/MQOPartialImportPanel.cs
// MQO部分インポートパネル
// ロジックは MQOPartialImportOps に委譲。
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

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQO部分インポートパネル
    /// </summary>
    public class MQOPartialImportPanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"]       = new() { ["en"] = "MQO Partial Import",                  ["ja"] = "MQO部分インポート" },
            ["ReferenceMQO"]      = new() { ["en"] = "Reference MQO",                       ["ja"] = "リファレンスMQO" },
            ["Options"]           = new() { ["en"] = "Options",                             ["ja"] = "オプション" },
            ["ImportTarget"]      = new() { ["en"] = "Import Target",                       ["ja"] = "インポート対象" },
            ["MQOFile"]           = new() { ["en"] = "MQO File",                            ["ja"] = "MQOファイル" },
            ["Scale"]             = new() { ["en"] = "Scale",                               ["ja"] = "スケール" },
            ["FlipZ"]             = new() { ["en"] = "Flip Z",                              ["ja"] = "Z反転" },
            ["FlipUV_V"]          = new() { ["en"] = "Flip UV V",                           ["ja"] = "UV V反転" },
            ["SkipBakedMirror"]   = new() { ["en"] = "Skip Baked Mirror (flag only)",       ["ja"] = "ベイクミラーをスキップ（フラグのみ）" },
            ["SkipNamedMirror"]   = new() { ["en"] = "Skip Named Mirror (+)",               ["ja"] = "名前ミラー(+)をスキップ" },
            ["NormalMode"]        = new() { ["en"] = "Normal Mode",                         ["ja"] = "法線モード" },
            ["SmoothingAngle"]    = new() { ["en"] = "Smoothing Angle",                     ["ja"] = "スムージング角度" },
            ["RecalcNormals"]     = new() { ["en"] = "Recalculate Normals",                 ["ja"] = "法線再計算" },
            ["VertexPosition"]    = new() { ["en"] = "Vertex Position",                     ["ja"] = "頂点位置" },
            ["VertexId"]          = new() { ["en"] = "Vertex ID",                           ["ja"] = "頂点ID" },
            ["MeshStructure"]     = new() { ["en"] = "Mesh Structure (Faces + UV)",         ["ja"] = "メッシュ構造（面＋UV）" },
            ["MaterialContent"]   = new() { ["en"] = "Material Content (by name)",          ["ja"] = "材質内容（名前マッチング）" },
            ["BakeMirror"]        = new() { ["en"] = "Bake Mirror",                         ["ja"] = "ミラーベイク" },
            ["Import"]            = new() { ["en"] = "Import",                              ["ja"] = "インポート" },
            ["Selection"]         = new() { ["en"] = "Selection: Model {0} ↔ MQO {1}",     ["ja"] = "選択: モデル {0} ↔ MQO {1}" },
            ["VertexMismatch"]    = new() { ["en"] = "Vertex mismatch: Model({0}) ≠ MQO({1})", ["ja"] = "頂点数不一致: モデル({0}) ≠ MQO({1})" },
            ["NothingSelected"]   = new() { ["en"] = "Select at least one import target",   ["ja"] = "インポート対象を1つ以上選択してください" },
            ["ImportSuccess"]     = new() { ["en"] = "Import: {0}",                         ["ja"] = "インポート完了: {0}" },
            ["ImportFailed"]      = new() { ["en"] = "Import failed: {0}",                  ["ja"] = "インポート失敗: {0}" },
            ["MaterialMatchResult"] = new() { ["en"] = "Material: {0} / {1} matched",       ["ja"] = "材質: {0} / {1} マッチ" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // フィールド
        // ================================================================

        private ToolContext               _context;
        private string                    _mqoFilePath  = "";
        private readonly MQOPartialMatchHelper _matchHelper = new MQOPartialMatchHelper();
        private readonly MQOPartialImportOps   _ops         = new MQOPartialImportOps();

        // オプション
        private float      _importScale    = 0.01f;
        private bool       _flipZ          = true;
        private bool       _flipUV_V       = true;
        private bool       _skipBakedMirror = true;
        private bool       _skipNamedMirror = true;
        private NormalMode _normalMode      = NormalMode.Smooth;
        private float      _smoothingAngle  = 60f;

        // インポート対象チェックボックス
        private bool _importVertexPosition  = true;
        private bool _importVertexId        = false;
        private bool _importMeshStructure   = false;
        private bool _importMaterialContent = false;
        private bool _bakeMirror            = true;
        private bool _recalcNormals         = true;

        // UI 状態
        private Vector2 _scrollLeft;
        private Vector2 _scrollRight;
        private string  _lastResult = "";

        // ================================================================
        // プロパティ
        // ================================================================

        private ModelContext Model => _context?.Model;

        private bool NeedsMeshMatching =>
            _importVertexPosition || _importVertexId || _importMeshStructure;

        private bool HasAnyTarget =>
            _importVertexPosition || _importVertexId || _importMeshStructure || _importMaterialContent;

        // ================================================================
        // Open
        // ================================================================

        public static void ShowWindow()
        {
            var panel = GetWindow<MQOPartialImportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize      = new Vector2(700, 500);
            panel.Show();
        }

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<MQOPartialImportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize      = new Vector2(700, 500);
            panel._context     = ctx;
            panel.InitFromContext();
            panel._matchHelper.BuildModelList(ctx?.Model, panel._skipBakedMirror, panel._skipNamedMirror, pairMirrors: true);
            panel.Show();
        }

        public void SetContext(ToolContext ctx)
        {
            _context = ctx;
            InitFromContext();
            _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror, pairMirrors: true);
            if (_matchHelper.MQODocument != null)
                _matchHelper.AutoMatch();
        }

        private void InitFromContext()
        {
            var es = _context?.UndoController?.EditorState;
            if (es == null) return;
            _importScale = es.MqoUnityRatio > 0f ? es.MqoUnityRatio : 0.01f;
            _flipZ       = es.MqoFlipZ;
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            DrawMQOFileSection();
            EditorGUILayout.Space(5);

            DrawImportTargetSection();
            EditorGUILayout.Space(5);

            DrawOptionsSection();
            EditorGUILayout.Space(5);

            if (NeedsMeshMatching)
            {
                _matchHelper.DrawDualListSection(_context, position.width, ref _scrollLeft, ref _scrollRight);
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
        // MQOファイルセクション
        // ================================================================

        private void DrawMQOFileSection()
        {
            EditorGUILayout.LabelField(T("ReferenceMQO"), EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(T("MQOFile"));
                var rect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _mqoFilePath = EditorGUI.TextField(rect, _mqoFilePath);

                MQOPartialMatchHelperEditorExt.HandleDropOnRect(rect, ".mqo", path =>
                {
                    _mqoFilePath = path;
                    LoadMQOAndMatch();
                });

                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string dir  = string.IsNullOrEmpty(_mqoFilePath) ? Application.dataPath : Path.GetDirectoryName(_mqoFilePath);
                    string path = EditorUtility.OpenFilePanel("Select MQO", dir, "mqo");
                    if (!string.IsNullOrEmpty(path)) { _mqoFilePath = path; LoadMQOAndMatch(); }
                }

                using (new EditorGUI.DisabledScope(
                    string.IsNullOrEmpty(_mqoFilePath) || !File.Exists(_mqoFilePath)))
                {
                    if (GUILayout.Button("↻", GUILayout.Width(25)))
                        LoadMQOAndMatch();
                }
            }

            if (_matchHelper.MQODocument != null)
            {
                int nonEmpty = _matchHelper.MQOObjects.Count;
                int total    = _matchHelper.MQODocument.Objects.Count;
                EditorGUILayout.LabelField($"Objects: {nonEmpty} / {total}", EditorStyles.miniLabel);
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
                _importVertexPosition  = EditorGUILayout.ToggleLeft(T("VertexPosition"),  _importVertexPosition,  GUILayout.Width(120));
                _importVertexId        = EditorGUILayout.ToggleLeft(T("VertexId"),        _importVertexId,        GUILayout.Width(100));
                _importMeshStructure   = EditorGUILayout.ToggleLeft(T("MeshStructure"),   _importMeshStructure,   GUILayout.Width(220));
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

            if (_importMeshStructure)
            {
                _flipUV_V   = EditorGUILayout.ToggleLeft(T("FlipUV_V"),  _flipUV_V);
                _bakeMirror = EditorGUILayout.ToggleLeft(T("BakeMirror"), _bakeMirror);
            }

            if (_importVertexPosition || _importMeshStructure)
            {
                _recalcNormals = EditorGUILayout.ToggleLeft(T("RecalcNormals"), _recalcNormals);
                if (_recalcNormals)
                {
                    _normalMode = (NormalMode)EditorGUILayout.EnumPopup(T("NormalMode"), _normalMode);
                    if (_normalMode == NormalMode.Smooth)
                        _smoothingAngle = EditorGUILayout.Slider(T("SmoothingAngle"), _smoothingAngle, 0f, 180f);
                }
            }

            if (NeedsMeshMatching)
            {
                bool prev = _skipNamedMirror;
                _skipNamedMirror = EditorGUILayout.ToggleLeft(T("SkipNamedMirror"), _skipNamedMirror);
                if (prev != _skipNamedMirror)
                {
                    _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror, pairMirrors: true);
                    if (_matchHelper.MQODocument != null) _matchHelper.AutoMatch();
                }
            }
        }

        // ================================================================
        // 材質マッチングプレビュー
        // ================================================================

        private void DrawMaterialMatchPreview()
        {
            if (_matchHelper.MQODocument == null || Model == null) return;

            var matches = _ops.BuildMaterialMatches(Model, _matchHelper.MQODocument);
            EditorGUILayout.LabelField(
                T("MaterialMatchResult", matches.Count, _matchHelper.MQODocument.Materials.Count),
                EditorStyles.miniLabel);

            foreach (var match in matches)
            {
                EditorGUILayout.LabelField(
                    $"  {match.MqoMaterial.Name} → {match.ModelMaterialRef.Name}",
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
                int modelCount = _matchHelper.ModelMeshes.Count(m => m.Selected);
                int mqoCount   = _matchHelper.MQOObjects.Count(m => m.Selected);
                int modelVerts = _matchHelper.SelectedModelVertexCount;
                int mqoVerts   = _matchHelper.SelectedMQOVertexCount;

                EditorGUILayout.LabelField(
                    T("Selection", modelCount, mqoCount) + $"  Verts: {modelVerts} ← {mqoVerts}");

                if ((_importVertexPosition || _importVertexId) && !_importMeshStructure
                    && modelVerts != mqoVerts && modelCount > 0 && mqoCount > 0)
                {
                    EditorGUILayout.HelpBox(T("VertexMismatch", modelVerts, mqoVerts), MessageType.Warning);
                }
            }

            bool canImport = _matchHelper.MQODocument != null;
            if (NeedsMeshMatching)
            {
                canImport &= _matchHelper.ModelMeshes.Any(m => m.Selected) &&
                             _matchHelper.MQOObjects.Any(m => m.Selected);
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
        // MQO読み込みと自動照合
        // ================================================================

        private void LoadMQOAndMatch()
        {
            _matchHelper.LoadMQO(_mqoFilePath, _flipZ, visibleOnly: true);

            if (_matchHelper.ModelMeshes.Count == 0)
                _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror, pairMirrors: true);

            if (_matchHelper.MQODocument != null)
                _matchHelper.AutoMatch();

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
                var selectedModels = _matchHelper.SelectedModelMeshes;
                var selectedMQOs   = _matchHelper.SelectedMQOObjects;
                bool topologyChanged = false;

                var beforeSnapshot = MultiMeshVertexSnapshot.Capture(Model);

                // メッシュ構造インポート（頂点位置も同時処理可）
                if (_importMeshStructure && selectedModels.Count > 0 && selectedMQOs.Count > 0)
                {
                    int count = _ops.ExecuteMeshStructureImport(
                        selectedModels, selectedMQOs,
                        alsoImportPosition: _importVertexPosition,
                        importScale: _importScale,
                        flipZ: _flipZ,
                        flipUV_V: _flipUV_V,
                        bakeMirror: _bakeMirror,
                        recalcNormals: _recalcNormals,
                        normalMode: _normalMode,
                        smoothingAngle: _smoothingAngle);

                    results.Add($"Structure: {count} meshes");
                    if (_importVertexPosition)
                        results.Add("Position: included in structure");
                    topologyChanged = true;
                }
                // 頂点位置のみ（メッシュ構造なし）
                else if (_importVertexPosition && selectedModels.Count > 0 && selectedMQOs.Count > 0)
                {
                    int count = _ops.ExecuteVertexPositionImport(
                        selectedModels, selectedMQOs, _importScale, _flipZ);
                    results.Add($"Position: {count} vertices");

                    if (_recalcNormals)
                    {
                        foreach (var entry in selectedModels)
                        {
                            var mo = entry.Context?.MeshObject;
                            if (mo != null)
                                _ops.RecalculateNormals(mo, _normalMode, _smoothingAngle);
                        }
                        results.Add("Normals: recalculated");
                    }
                }

                // 頂点IDインポート
                if (_importVertexId && selectedModels.Count > 0 && selectedMQOs.Count > 0)
                {
                    int count = _ops.ExecuteVertexIdImport(selectedModels, selectedMQOs);
                    if (count > 0) results.Add($"VertexID: {count} vertices");
                }

                // 材質インポート
                if (_importMaterialContent)
                {
                    int count = _ops.ExecuteMaterialImport(Model, _matchHelper.MQODocument);
                    results.Add(T("MaterialMatchResult",
                        count, _matchHelper.MQODocument.Materials.Count));
                }

                // 同期
                if (topologyChanged)
                    _context?.OnTopologyChanged();
                else if (_importVertexPosition)
                    _context?.SyncMesh?.Invoke();

                if (_importVertexId || _importMaterialContent)
                    Model.IsDirty = true;

                _context?.Repaint?.Invoke();
                SceneView.RepaintAll();

                // Undo記録
                var undo = _context?.UndoController;
                if (undo != null)
                {
                    var afterSnapshot = MultiMeshVertexSnapshot.Capture(Model);
                    var record = new MultiMeshVertexSnapshotRecord(
                        beforeSnapshot, afterSnapshot, "MQO Partial Import");
                    undo.MeshListStack.Record(record, "MQO Partial Import");
                }

                _lastResult = T("ImportSuccess", string.Join(", ", results));
            }
            catch (Exception ex)
            {
                _lastResult = T("ImportFailed", ex.Message);
                Debug.LogError($"[MQOPartialImport] {ex.Message}\n{ex.StackTrace}");
            }

            Repaint();
        }
    }
}
