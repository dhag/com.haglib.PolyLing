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

        [MenuItem("PolyLing/BlendShapeSync → VRM10 Expression")]
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

            // BlendShapeSync取得
            var sync = _blendShapeSyncObject.GetComponent<Poly_Ling.Runtime.BlendShapeSync>();
            if (sync == null)
            {
                _statusMessage = "BlendShapeSyncコンポーネントが見つかりません";
                return;
            }

            if (string.IsNullOrEmpty(sync.MappingCSV))
            {
                _statusMessage = "MappingCSVが空です";
                return;
            }

            // Vrm10Instance確認
            var vrm10 = _vrm10Root.GetComponent<Vrm10Instance>();
            if (vrm10 == null)
            {
                _statusMessage = "Vrm10Instanceコンポーネントが見つかりません";
                return;
            }

            // CSV解析
            _clipDefinitions = ParseCSV(sync.MappingCSV);
            if (_clipDefinitions.Count == 0)
            {
                _statusMessage = "CSVにExpressionが見つかりません";
                return;
            }

            // VRM10側のSMR → BlendShape辞書構築
            // key: (smrName, blendShapeName) → (relativePath, index)
            var blendShapeMap = BuildBlendShapeMap(_vrm10Root.transform);

            // マッチング
            _matchResults = new Dictionary<string, List<(string, string, float, string, int, bool)>>();

            foreach (var kv in _clipDefinitions)
            {
                string expressionName = kv.Key;
                var entries = new List<(string, string, float, string, int, bool)>();

                foreach (var (meshName, shapeName, weight) in kv.Value)
                {
                    var key = (meshName, shapeName);
                    if (blendShapeMap.TryGetValue(key, out var info))
                    {
                        entries.Add((meshName, shapeName, weight, info.relativePath, info.index, true));
                    }
                    else
                    {
                        entries.Add((meshName, shapeName, weight, "", -1, false));
                    }
                }

                _matchResults[expressionName] = entries;
            }

            int totalEntries = _matchResults.Values.Sum(e => e.Count);
            int matchedEntries = _matchResults.Values.Sum(e => e.Count(x => x.matched));
            _statusMessage = $"解析完了: {_clipDefinitions.Count}Expression, {matchedEntries}/{totalEntries}エントリ マッチ";
            _analyzed = true;
        }

        // ================================================================
        // CSV解析
        // ================================================================

        private Dictionary<string, List<(string meshName, string shapeName, float weight)>> ParseCSV(string csv)
        {
            var result = new Dictionary<string, List<(string, string, float)>>();

            var lines = csv.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                var parts = trimmed.Split(',');
                if (parts.Length < 4) continue;

                string expressionName = parts[0].Trim();
                var targets = new List<(string, string, float)>();

                for (int i = 1; i + 2 < parts.Length; i += 3)
                {
                    string meshName = parts[i].Trim();
                    string shapeName = parts[i + 1].Trim();
                    if (float.TryParse(parts[i + 2].Trim(), out float weight))
                        targets.Add((meshName, shapeName, weight));
                }

                if (targets.Count > 0)
                    result[expressionName] = targets;
            }

            return result;
        }

        // ================================================================
        // BlendShapeマップ構築
        // ================================================================

        private Dictionary<(string smrName, string shapeName), (string relativePath, int index)>
            BuildBlendShapeMap(Transform root)
        {
            var map = new Dictionary<(string, string), (string, int)>();
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            foreach (var smr in smrs)
            {
                if (smr.sharedMesh == null) continue;

                string relativePath = GetRelativePath(root, smr.transform);
                int count = smr.sharedMesh.blendShapeCount;

                for (int i = 0; i < count; i++)
                {
                    string shapeName = smr.sharedMesh.GetBlendShapeName(i);
                    var key = (smr.name, shapeName);
                    if (!map.ContainsKey(key))
                        map[key] = (relativePath, i);
                }
            }

            return map;
        }

        private string GetRelativePath(Transform root, Transform target)
        {
            var parts = new List<string>();
            var current = target;

            while (current != null && current != root)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            parts.Reverse();
            return string.Join("/", parts);
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
            if (_matchResults == null || _matchResults.Count == 0) return;

            // 出力フォルダ作成
            if (!AssetDatabase.IsValidFolder(_outputFolder))
            {
                string parent = Path.GetDirectoryName(_outputFolder);
                string folderName = Path.GetFileName(_outputFolder);
                if (!AssetDatabase.IsValidFolder(parent))
                    AssetDatabase.CreateFolder("Assets", parent.Replace("Assets/", ""));
                AssetDatabase.CreateFolder(parent, folderName);
            }

            int created = 0;

            foreach (var kv in _matchResults)
            {
                string expressionName = kv.Key;
                var entries = kv.Value;

                var matchedEntries = entries.Where(e => e.matched).ToList();
                if (matchedEntries.Count == 0) continue;

                // VRM10Expression ScriptableObject作成
                var expression = ScriptableObject.CreateInstance<VRM10Expression>();
                expression.name = expressionName;

                // MorphTargetBindings設定
                var bindings = new List<MorphTargetBinding>();
                foreach (var entry in matchedEntries)
                {
                    bindings.Add(new MorphTargetBinding
                    {
                        RelativePath = entry.relativePath,
                        Index = entry.blendShapeIndex,
                        Weight = entry.weight, // CSV: 0-1, MorphTargetBinding: 0-1
                    });
                }
                expression.MorphTargetBindings = bindings.ToArray();

                // アセット保存
                string assetPath = $"{_outputFolder}/{expressionName}.asset";
                assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
                AssetDatabase.CreateAsset(expression, assetPath);
                created++;

                Debug.Log($"[VRM10Expression] Created: {assetPath} ({bindings.Count} bindings)");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _statusMessage = $"Expression生成完了: {created}アセット → {_outputFolder}";
        }
    }
}

#endif
