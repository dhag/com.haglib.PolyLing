// Assets/Editor/Poly_Ling/MQO/Utility/MQODepthRecoveryTool.cs
// =====================================================================
// 【緊急】MQO Depth復旧ツール
// 
// VertexIdToolで破壊されたdepth情報を元のMQOから復元する
// =====================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.MQO.Utility
{
    public class MQODepthRecoveryTool : EditorWindow
    {
        [MenuItem("Tools/Poly_Ling/【緊急】MQO Depth復旧")]
        public static void ShowWindow()
        {
            var window = GetWindow<MQODepthRecoveryTool>();
            window.titleContent = new GUIContent("Depth復旧");
            window.minSize = new Vector2(500, 300);
            window.Show();
        }

        private string _originalMqoPath = "";  // 元のMQO（depth情報あり）
        private string _brokenMqoPath = "";    // 壊れたMQO（頂点ID追加済み、depthなし）
        private string _outputMqoPath = "";    // 出力先
        private Vector2 _scrollPos;
        private string _log = "";

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox(
                "VertexIdToolで破壊されたdepth情報を復旧します。\n" +
                "・元のMQO: 頂点ID追加前のオリジナルファイル\n" +
                "・壊れたMQO: 頂点ID追加後、depthが消えたファイル\n" +
                "・出力先: 復旧後のファイル（上書き防止のため別ファイル推奨）",
                MessageType.Warning);

            EditorGUILayout.Space(10);

            // 元のMQO
            EditorGUILayout.LabelField("① 元のMQO（depth情報あり）", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var origRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _originalMqoPath = EditorGUI.TextField(origRect, _originalMqoPath);
                HandleDropOnRect(origRect, ".mqo", path => _originalMqoPath = path);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string path = EditorUtility.OpenFilePanel("元のMQOを選択", 
                        string.IsNullOrEmpty(_originalMqoPath) ? "" : Path.GetDirectoryName(_originalMqoPath), 
                        "mqo");
                    if (!string.IsNullOrEmpty(path))
                        _originalMqoPath = path;
                }
            }

            EditorGUILayout.Space(5);

            // 壊れたMQO
            EditorGUILayout.LabelField("② 壊れたMQO（頂点ID追加済み、depthなし）", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                var brokenRect = GUILayoutUtility.GetRect(GUIContent.none, EditorStyles.textField, GUILayout.ExpandWidth(true));
                _brokenMqoPath = EditorGUI.TextField(brokenRect, _brokenMqoPath);
                HandleDropOnRect(brokenRect, ".mqo", path =>
                {
                    _brokenMqoPath = path;
                    // 出力先を自動設定
                    if (string.IsNullOrEmpty(_outputMqoPath))
                    {
                        string dir = Path.GetDirectoryName(path);
                        string name = Path.GetFileNameWithoutExtension(path);
                        _outputMqoPath = Path.Combine(dir, name + "_recovered.mqo");
                    }
                });
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string path = EditorUtility.OpenFilePanel("壊れたMQOを選択",
                        string.IsNullOrEmpty(_brokenMqoPath) ? "" : Path.GetDirectoryName(_brokenMqoPath),
                        "mqo");
                    if (!string.IsNullOrEmpty(path))
                    {
                        _brokenMqoPath = path;
                        // 出力先を自動設定
                        if (string.IsNullOrEmpty(_outputMqoPath))
                        {
                            string dir = Path.GetDirectoryName(path);
                            string name = Path.GetFileNameWithoutExtension(path);
                            _outputMqoPath = Path.Combine(dir, name + "_recovered.mqo");
                        }
                    }
                }
            }

            EditorGUILayout.Space(5);

            // 出力先
            EditorGUILayout.LabelField("③ 出力先MQO", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _outputMqoPath = EditorGUILayout.TextField(_outputMqoPath);
                if (GUILayout.Button("...", GUILayout.Width(30)))
                {
                    string path = EditorUtility.SaveFilePanel("出力先を選択",
                        string.IsNullOrEmpty(_outputMqoPath) ? "" : Path.GetDirectoryName(_outputMqoPath),
                        string.IsNullOrEmpty(_outputMqoPath) ? "recovered.mqo" : Path.GetFileName(_outputMqoPath),
                        "mqo");
                    if (!string.IsNullOrEmpty(path))
                        _outputMqoPath = path;
                }
            }

            EditorGUILayout.Space(15);

            // 復旧ボタン
            bool canRecover = !string.IsNullOrEmpty(_originalMqoPath) &&
                              !string.IsNullOrEmpty(_brokenMqoPath) &&
                              !string.IsNullOrEmpty(_outputMqoPath) &&
                              File.Exists(_originalMqoPath) &&
                              File.Exists(_brokenMqoPath);

            using (new EditorGUI.DisabledScope(!canRecover))
            {
                if (GUILayout.Button("復旧実行", GUILayout.Height(40)))
                {
                    RecoverDepth();
                }
            }

            if (!canRecover)
            {
                EditorGUILayout.HelpBox("すべてのファイルパスを指定してください", MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // ログ表示
            EditorGUILayout.LabelField("ログ", EditorStyles.boldLabel);
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.Height(150));
            EditorGUILayout.TextArea(_log, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private void RecoverDepth()
        {
            _log = "";
            Log("=== Depth復旧開始 ===");

            try
            {
                // 元のMQOからdepth情報を抽出
                Log($"元のMQO読み込み: {_originalMqoPath}");
                var originalDepths = ExtractDepthInfo(_originalMqoPath);
                Log($"  → {originalDepths.Count} オブジェクトのdepth情報を取得");

                foreach (var kvp in originalDepths)
                {
                    Log($"    {kvp.Key}: depth={kvp.Value}");
                }

                // 壊れたMQOを読み込んでdepthを復元
                Log($"壊れたMQO読み込み: {_brokenMqoPath}");
                string brokenContent = File.ReadAllText(_brokenMqoPath, GetEncoding(_brokenMqoPath));

                int recoveredCount = 0;
                int notFoundCount = 0;

                // 各オブジェクトのdepthを復元
                string recoveredContent = RecoverDepthInContent(brokenContent, originalDepths, 
                    out recoveredCount, out notFoundCount);

                // 出力
                Log($"出力: {_outputMqoPath}");
                File.WriteAllText(_outputMqoPath, recoveredContent, Encoding.GetEncoding("shift_jis"));

                Log("=== 復旧完了 ===");
                Log($"復旧: {recoveredCount} オブジェクト");
                if (notFoundCount > 0)
                {
                    Log($"警告: {notFoundCount} オブジェクトは元MQOに存在しませんでした");
                }

                EditorUtility.DisplayDialog("復旧完了", 
                    $"復旧完了: {recoveredCount} オブジェクト\n出力先: {_outputMqoPath}", "OK");
            }
            catch (Exception ex)
            {
                Log($"エラー: {ex.Message}");
                EditorUtility.DisplayDialog("エラー", ex.Message, "OK");
            }
        }

        /// <summary>
        /// MQOファイルからオブジェクト名→depth のマッピングを抽出
        /// </summary>
        private Dictionary<string, int> ExtractDepthInfo(string filePath)
        {
            var result = new Dictionary<string, int>();
            string content = File.ReadAllText(filePath, GetEncoding(filePath));
            var lines = content.Split('\n');

            string currentObjectName = null;
            
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                // Object "名前" { を検出
                if (line.StartsWith("Object "))
                {
                    int start = line.IndexOf('"');
                    int end = line.LastIndexOf('"');
                    if (start >= 0 && end > start)
                    {
                        currentObjectName = line.Substring(start + 1, end - start - 1);
                        // デフォルトdepth=0
                        if (!result.ContainsKey(currentObjectName))
                            result[currentObjectName] = 0;
                    }
                }
                // depth N を検出
                else if (currentObjectName != null && line.StartsWith("depth "))
                {
                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int depth))
                    {
                        result[currentObjectName] = depth;
                    }
                }
                // オブジェクト終了（簡易判定）
                else if (line == "}")
                {
                    currentObjectName = null;
                }
            }

            return result;
        }

        /// <summary>
        /// 壊れたMQOの内容にdepth情報を復元
        /// </summary>
        private string RecoverDepthInContent(string content, Dictionary<string, int> originalDepths,
            out int recoveredCount, out int notFoundCount)
        {
            recoveredCount = 0;
            notFoundCount = 0;

            // 元の改行コードを検出して保持
            string newline = content.Contains("\r\n") ? "\r\n" : "\n";
            
            // 改行で分割（空エントリも保持）
            string[] lines = content.Replace("\r\n", "\n").Split('\n');
            
            var resultLines = new List<string>();

            string currentObjectName = null;
            bool depthWritten = false;
            bool inObject = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart('\t', ' ');

                // Object "名前" { を検出
                if (trimmed.StartsWith("Object "))
                {
                    int start = trimmed.IndexOf('"');
                    int end = trimmed.LastIndexOf('"');
                    if (start >= 0 && end > start)
                    {
                        currentObjectName = trimmed.Substring(start + 1, end - start - 1);
                        inObject = true;
                        depthWritten = false;
                    }
                    resultLines.Add(line);
                }
                // 既存のdepth行は元のdepthで置換
                else if (inObject && trimmed.StartsWith("depth "))
                {
                    if (currentObjectName != null && originalDepths.TryGetValue(currentObjectName, out int depth))
                    {
                        // 元のインデントを検出
                        string indent = line.Substring(0, line.Length - trimmed.Length);
                        resultLines.Add($"{indent}depth {depth}");
                        Log($"  復旧: {currentObjectName} → depth {depth}");
                        recoveredCount++;
                    }
                    else
                    {
                        // 元のdepth情報がない場合はそのまま
                        resultLines.Add(line);
                    }
                    depthWritten = true;
                }
                // visible行の後にdepthがない場合は挿入
                else if (inObject && !depthWritten && trimmed.StartsWith("visible "))
                {
                    resultLines.Add(line);
                    
                    // depth行を挿入（元のdepthがある場合のみ）
                    if (currentObjectName != null && originalDepths.TryGetValue(currentObjectName, out int depth))
                    {
                        if (depth > 0)
                        {
                            // 元のインデントを検出
                            string indent = line.Substring(0, line.Length - trimmed.Length);
                            resultLines.Add($"{indent}depth {depth}");
                            Log($"  復旧(挿入): {currentObjectName} → depth {depth}");
                            recoveredCount++;
                        }
                    }
                    else if (currentObjectName != null)
                    {
                        notFoundCount++;
                    }
                    depthWritten = true;
                }
                // オブジェクト終了
                else if (inObject && trimmed == "}")
                {
                    inObject = false;
                    currentObjectName = null;
                    depthWritten = false;
                    resultLines.Add(line);
                }
                else
                {
                    resultLines.Add(line);
                }
            }

            // 元の改行コードで結合
            return string.Join(newline, resultLines);
        }

        private Encoding GetEncoding(string filePath)
        {
            // Shift-JISを試行、失敗したらUTF-8
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                // BOMチェック
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                    return Encoding.UTF8;
                
                return Encoding.GetEncoding("shift_jis");
            }
            catch
            {
                return Encoding.UTF8;
            }
        }

        private void Log(string message)
        {
            _log += message + "\n";
            Debug.Log($"[MQODepthRecovery] {message}");
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
