// Assets/Editor/Poly_Ling_/ToolPanels/BlendShapeSyncToVRM10.cs
// BlendShapeSync CSV → VRM10 Expression 生成ツール

#if POLY_LING_VRM10

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UniVRM10;
using Poly_Ling.EditorCore;

namespace Poly_Ling.Tools.Panels
{
    /// <summary>
    /// BlendShapeSyncのMappingCSVからVRM10 Expressionアセットを生成するEditorWindow
    /// </summary>
    public class BlendShapeSyncToVRM10 : EditorWindow
    {
        // ================================================================
        // 入力
        // ================================================================

        [SerializeField] private GameObject _blendShapeSyncObject;
        [SerializeField] private GameObject _vrm10Root;
        [SerializeField] private string _outputFolder = "Assets/Expressions";

        // ================================================================
        // 内部状態
        // ================================================================

        // CSV解析結果: ExpressionName -> List<(MeshName, BlendShapeName, Weight)>
        private Dictionary<string, List<(string meshName, string shapeName, float weight)>> _clipDefinitions;

        // マッチ結果: ExpressionName -> List<(MeshName, ShapeName, Weight, RelativePath, BlendShapeIndex, Matched)>
        private Dictionary<string, List<(string meshName, string shapeName, float weight,
            string relativePath, int blendShapeIndex, bool matched)>> _matchResults;

        private Vector2 _scrollPos;
        private string _statusMessage = "";
        private bool _analyzed = false;

        // ================================================================
        // メニュー
        // ================================================================

        [MenuItem("Tools/Utility/PolyLing/BlendShapeSync → VRM10 Expression")]
        public static void Open()
        {
            var window = GetWindow<BlendShapeSyncToVRM10>();
            window.titleContent = new GUIContent("BS→VRM10");
            window.minSize = new Vector2(400, 500);
            window.Show();
        }

        // ================================================================
        // GUI
        // ================================================================

        private void OnGUI()
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("BlendShapeSync → VRM10 Expression", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // 入力フィールド
            _blendShapeSyncObject = (GameObject)EditorGUILayout.ObjectField(
                "BlendShapeSync Object",
                _blendShapeSyncObject,
                typeof(GameObject),
                true);

            _vrm10Root = (GameObject)EditorGUILayout.ObjectField(
                "VRM10 Root",
                _vrm10Root,
                typeof(GameObject),
                true);

            EditorGUILayout.BeginHorizontal();
            _outputFolder = EditorGUILayout.TextField("出力フォルダ", _outputFolder);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string folder = EditorUtility.OpenFolderPanel("Expression保存先", "Assets", "Expressions");
                if (!string.IsNullOrEmpty(folder))
                {
                    if (folder.StartsWith(Application.dataPath))
                        _outputFolder = "Assets" + folder.Substring(Application.dataPath.Length);
                    else
                        _statusMessage = "Assets配下のフォルダを選択してください";
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 解析ボタン
            using (new EditorGUI.DisabledScope(_blendShapeSyncObject == null || _vrm10Root == null))
            {
                if (GUILayout.Button("解析", GUILayout.Height(28)))
                    Analyze();
            }

            // マッチ結果表示
            if (_analyzed && _matchResults != null)
            {
                EditorGUILayout.Space(10);
                DrawMatchResults();

                EditorGUILayout.Space(10);

                // 生成ボタン
                int matchedClips = _matchResults.Count(kv => kv.Value.Any(e => e.matched));
                using (new EditorGUI.DisabledScope(matchedClips == 0))
                {
                    if (GUILayout.Button($"Expression生成 ({matchedClips}件)", GUILayout.Height(28)))
                        GenerateExpressions();
                }
            }

            // ステータス
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
            }
        }

        // ================================================================
        // 解析
        // ================================================================

        private void Analyze()
        {
            _analyzed = false;
            _matchResults = null;
            _statusMessage = "";

            if (_blendShapeSyncObject == null) { _statusMessage = "BlendShapeSyncオブジェクトが設定されていない"; return; }
            if (_vrm10Root == null)            { _statusMessage = "VRM10 Rootが設定されていない"; return; }

            var sync  = _blendShapeSyncObject.GetComponent<Poly_Ling.Runtime.BlendShapeSync>();
            var vrm10 = _vrm10Root.GetComponent<Vrm10Instance>();

            if (sync  == null) { _statusMessage = "BlendShapeSyncコンポーネントが見つかりません"; return; }
            if (vrm10 == null) { _statusMessage = "Vrm10Instanceコンポーネントが見つかりません"; return; }

            _matchResults = EditorBlendShapeSyncToVRM10.Analyze(sync, vrm10, out _statusMessage);
            _analyzed = _matchResults != null;
        }

        // ================================================================
        // マッチ結果表示
        // ================================================================

        private void DrawMatchResults()
        {
            EditorGUILayout.LabelField("マッチ結果", EditorStyles.boldLabel);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.MaxHeight(300));

            foreach (var kv in _matchResults)
            {
                string expressionName = kv.Key;
                var entries = kv.Value;
                bool allMatched = entries.All(e => e.matched);
                bool anyMatched = entries.Any(e => e.matched);

                string icon = allMatched ? "✓" : anyMatched ? "△" : "✗";
                Color c = allMatched ? Color.green : anyMatched ? Color.yellow : Color.red;

                var prev = GUI.contentColor;
                GUI.contentColor = c;
                EditorGUILayout.LabelField($"{icon} {expressionName} ({entries.Count(e => e.matched)}/{entries.Count})");
                GUI.contentColor = prev;

                EditorGUI.indentLevel++;
                foreach (var entry in entries)
                {
                    string status = entry.matched
                        ? $"✓ {entry.meshName}/{entry.shapeName} → {entry.relativePath}[{entry.blendShapeIndex}]"
                        : $"✗ {entry.meshName}/{entry.shapeName} (未マッチ)";

                    prev = GUI.contentColor;
                    GUI.contentColor = entry.matched ? Color.white : Color.red;
                    EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
                    GUI.contentColor = prev;
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
        }

        // ================================================================
        // Expression生成
        // ================================================================

        private void GenerateExpressions()
        {
            if (_matchResults == null) return;
            _statusMessage = EditorBlendShapeSyncToVRM10.GenerateExpressions(_matchResults, _outputFolder);
        }
    }
}

#endif
