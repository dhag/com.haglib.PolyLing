// Assets/Editor/Poly_Ling_/ToolPanels/MQO/Export/MQOPartialExportPanel.cs
// MQO部分エクスポートパネル
// ロジックは MQOPartialExportOps に委譲。
// UI描画・ファイルIO・_matchHelper・ダイアログのみをここで行う。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Localization;
using Poly_Ling.Tools;

namespace Poly_Ling.MQO
{
    /// <summary>
    /// MQO部分エクスポートパネル
    /// </summary>
    public class MQOPartialExportPanel : EditorWindow, IToolContextReceiver
    {
        // ================================================================
        // ローカライズ辞書
        // ================================================================

        private static readonly Dictionary<string, Dictionary<string, string>> _localize = new()
        {
            ["WindowTitle"]        = new() { ["en"] = "MQO Partial Export",                    ["ja"] = "MQO部分エクスポート" },
            ["ReferenceMQO"]       = new() { ["en"] = "Reference MQO",                         ["ja"] = "リファレンスMQO" },
            ["Options"]            = new() { ["en"] = "Options",                               ["ja"] = "オプション" },
            ["ModelMeshes"]        = new() { ["en"] = "Model Meshes",                          ["ja"] = "モデルメッシュ" },
            ["MQOObjects"]         = new() { ["en"] = "MQO Objects",                           ["ja"] = "MQOオブジェクト" },
            ["MQOFile"]            = new() { ["en"] = "MQO File",                              ["ja"] = "MQOファイル" },
            ["ExportScale"]        = new() { ["en"] = "Export Scale",                          ["ja"] = "エクスポートスケール" },
            ["FlipZ"]              = new() { ["en"] = "Flip Z",                                ["ja"] = "Z反転" },
            ["SkipBakedMirror"]    = new() { ["en"] = "Skip Baked Mirror (flag only)",         ["ja"] = "ベイクミラーをスキップ（フラグのみ）" },
            ["SkipNamedMirror"]    = new() { ["en"] = "Skip Named Mirror (+)",                 ["ja"] = "名前ミラー(+)をスキップ" },
            ["WriteBack"]          = new() { ["en"] = "WriteBack Options",                     ["ja"] = "書き戻しオプション" },
            ["WriteBackPosition"]  = new() { ["en"] = "Position",                              ["ja"] = "位置" },
            ["WriteBackUV"]        = new() { ["en"] = "UV",                                    ["ja"] = "UV" },
            ["WriteBackBoneWeight"]= new() { ["en"] = "BoneWeight",                            ["ja"] = "ボーンウェイト" },
            ["SelectAll"]          = new() { ["en"] = "All",                                   ["ja"] = "全選択" },
            ["SelectNone"]         = new() { ["en"] = "None",                                  ["ja"] = "全解除" },
            ["Export"]             = new() { ["en"] = "Export MQO",                            ["ja"] = "MQOエクスポート" },
            ["Selection"]          = new() { ["en"] = "Selection: Model {0} ↔ MQO {1}",        ["ja"] = "選択: モデル {0} ↔ MQO {1}" },
            ["CountMismatch"]      = new() { ["en"] = "Count mismatch!",                       ["ja"] = "数が不一致！" },
            ["NoContext"]          = new() { ["en"] = "No context. Open from Poly_Ling.",       ["ja"] = "コンテキスト未設定" },
            ["NoModel"]            = new() { ["en"] = "No model loaded",                       ["ja"] = "モデルなし" },
            ["SelectMQOFirst"]     = new() { ["en"] = "Select MQO file",                       ["ja"] = "MQOファイルを選択" },
            ["ExportSuccess"]      = new() { ["en"] = "Export: {0}",                           ["ja"] = "エクスポート完了: {0}" },
            ["ExportFailed"]       = new() { ["en"] = "Export failed: {0}",                    ["ja"] = "エクスポート失敗: {0}" },
            ["VertexMismatch"]     = new() { ["en"] = "Vertex mismatch: {0}({1}) → {2}({3})",  ["ja"] = "頂点数不一致: {0}({1}) → {2}({3})" },
        };

        private static string T(string key) => L.GetFrom(_localize, key);
        private static string T(string key, params object[] args) => L.GetFrom(_localize, key, args);

        // ================================================================
        // フィールド
        // ================================================================

        private ToolContext            _context;
        private string                 _mqoFilePath  = "";
        private readonly MQOPartialMatchHelper  _matchHelper = new MQOPartialMatchHelper();
        private readonly MQOPartialExportOps    _ops         = new MQOPartialExportOps();

        // オプション
        private float _exportScale      = 0.01f;
        private bool  _flipZ            = true;
        private bool  _skipBakedMirror  = true;
        private bool  _skipNamedMirror  = true;

        // WriteBackオプション
        private bool _writeBackPosition   = true;
        private bool _writeBackUV         = false;
        private bool _writeBackBoneWeight = false;

        // UI 状態
        private Vector2 _scrollLeft;
        private Vector2 _scrollRight;
        private string  _lastResult = "";

        // ================================================================
        // プロパティ
        // ================================================================

        private ModelContext Model => _context?.Model;

        // ================================================================
        // Open
        // ================================================================

        public static void ShowWindow()
        {
            var panel = GetWindow<MQOPartialExportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize      = new Vector2(700, 500);
            panel.Show();
        }

        public static void Open(ToolContext ctx)
        {
            var panel = GetWindow<MQOPartialExportPanel>();
            panel.titleContent = new GUIContent(T("WindowTitle"));
            panel.minSize      = new Vector2(700, 500);
            panel._context     = ctx;
            panel._matchHelper.BuildModelList(ctx?.Model, panel._skipBakedMirror, panel._skipNamedMirror);
            panel.Show();
        }

        public void SetContext(ToolContext ctx)
        {
            _context = ctx;
            var es = ctx?.UndoController?.EditorState;
            if (es != null) _exportScale = es.MqoUnityRatio > 0f ? es.MqoUnityRatio : 0.01f;
            _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror);
            if (_matchHelper.MQODocument != null) _matchHelper.AutoMatch();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            DrawMQOFileSection();
            EditorGUILayout.Space(5);

            DrawOptionsSection();
            EditorGUILayout.Space(5);

            _matchHelper.DrawDualListSection(_context, position.width, ref _scrollLeft, ref _scrollRight);
            EditorGUILayout.Space(5);

            DrawExportSection();
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
            }

            if (_matchHelper.MQODocument != null)
            {
                int nonEmpty = _matchHelper.MQOObjects.Count;
                int total    = _matchHelper.MQODocument.Objects.Count;
                EditorGUILayout.LabelField($"Objects: {nonEmpty} / {total}", EditorStyles.miniLabel);
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
                _exportScale = EditorGUILayout.FloatField(T("ExportScale"), _exportScale, GUILayout.Width(200));
                _flipZ       = EditorGUILayout.ToggleLeft(T("FlipZ"), _flipZ, GUILayout.Width(80));
            }

            bool prevBaked = _skipBakedMirror;
            _skipBakedMirror = EditorGUILayout.ToggleLeft(T("SkipBakedMirror"), _skipBakedMirror);
            if (prevBaked != _skipBakedMirror)
            {
                _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror);
                if (_matchHelper.MQODocument != null) _matchHelper.AutoMatch();
            }

            bool prevNamed = _skipNamedMirror;
            _skipNamedMirror = EditorGUILayout.ToggleLeft(T("SkipNamedMirror"), _skipNamedMirror);
            if (prevNamed != _skipNamedMirror)
            {
                _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror);
                if (_matchHelper.MQODocument != null) _matchHelper.AutoMatch();
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("WriteBack"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _writeBackPosition   = EditorGUILayout.ToggleLeft(T("WriteBackPosition"),   _writeBackPosition,   GUILayout.Width(100));
                _writeBackUV         = EditorGUILayout.ToggleLeft(T("WriteBackUV"),          _writeBackUV,         GUILayout.Width(80));
                _writeBackBoneWeight = EditorGUILayout.ToggleLeft(T("WriteBackBoneWeight"),  _writeBackBoneWeight, GUILayout.Width(140));
            }
        }

        // ================================================================
        // エクスポートセクション
        // ================================================================

        private void DrawExportSection()
        {
            int modelCount = _matchHelper.ModelMeshes.Count(m => m.Selected);
            int mqoCount   = _matchHelper.MQOObjects.Count(m => m.Selected);
            int modelVerts = _matchHelper.SelectedModelVertexCount;
            int mqoVerts   = _matchHelper.SelectedMQOVertexCount;

            EditorGUILayout.LabelField(
                T("Selection", modelCount, mqoCount) + $"  Verts: {modelVerts} → {mqoVerts}");

            bool canExport = modelCount > 0 && mqoCount > 0 && _matchHelper.MQODocument != null;

            if (modelVerts != mqoVerts && canExport)
                EditorGUILayout.HelpBox(T("VertexMismatch", "Model", modelVerts, "MQO", mqoVerts), MessageType.Warning);

            using (new EditorGUI.DisabledScope(!canExport))
            {
                if (GUILayout.Button(T("Export"), GUILayout.Height(30)))
                    ExecuteExport();
            }

            if (!string.IsNullOrEmpty(_lastResult))
                EditorGUILayout.HelpBox(_lastResult, MessageType.Info);
        }

        // ================================================================
        // MQO読み込みと自動照合
        // ================================================================

        private void LoadMQOAndMatch()
        {
            _matchHelper.LoadMQO(_mqoFilePath, _flipZ, visibleOnly: false);

            if (_matchHelper.ModelMeshes.Count == 0)
                _matchHelper.BuildModelList(Model, _skipBakedMirror, _skipNamedMirror);

            if (_matchHelper.MQODocument != null)
                _matchHelper.AutoMatch();

            Repaint();
        }

        // ================================================================
        // エクスポート実行
        // ================================================================

        private void ExecuteExport()
        {
            try
            {
                string defaultName = Path.GetFileNameWithoutExtension(_mqoFilePath) + "_partial.mqo";
                string savePath    = EditorUtility.SaveFilePanel(
                    "Save MQO", Path.GetDirectoryName(_mqoFilePath), defaultName, "mqo");

                if (string.IsNullOrEmpty(savePath)) return;

                var selectedModels = _matchHelper.SelectedModelMeshes;
                var selectedMQOs   = _matchHelper.SelectedMQOObjects;

                int transferred = _ops.ExecuteExport(
                    selectedMQOs, selectedModels,
                    _matchHelper.MQODocument,
                    _exportScale, _flipZ,
                    _writeBackPosition, _writeBackUV, _writeBackBoneWeight);

                var writeResult = MQODocumentIO.WriteDocumentToFile(
                    _matchHelper.MQODocument, savePath);
                if (!writeResult.Success)
                    throw new Exception(writeResult.ErrorMessage);

                int totalModelVerts = _matchHelper.SelectedModelVertexCount;
                int totalMqoVerts   = _matchHelper.SelectedMQOVertexCount;

                _lastResult = T("ExportSuccess", $"{transferred} vertices → {Path.GetFileName(savePath)}");
                if (totalModelVerts != totalMqoVerts)
                    _lastResult += $"\n(Model:{totalModelVerts} ≠ MQO:{totalMqoVerts})";

                _matchHelper.LoadMQO(_mqoFilePath, _flipZ, visibleOnly: false);
            }
            catch (Exception ex)
            {
                _lastResult = T("ExportFailed", ex.Message);
                Debug.LogError($"[MQOPartialExport] {ex.Message}\n{ex.StackTrace}");
            }

            Repaint();
        }
    }
}
