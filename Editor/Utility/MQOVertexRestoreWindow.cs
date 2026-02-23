// MQOVertexRestoreWindow.cs
// AのMQOからBのMQOへ頂点位置のみを書き戻す
// テキスト行単位で処理し、頂点位置行以外は1バイトも変更しない
// Menu: Tools/Poly_Ling/Utility/MQO Vertex Restore

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Editor.Utility
{
    public class MQOVertexRestoreWindow : EditorWindow
    {
        [MenuItem("Tools/Poly_Ling/Utility/MQO Vertex Restore")]
        public static void ShowWindow()
        {
            GetWindow<MQOVertexRestoreWindow>("MQO Vertex Restore");
        }

        private string _pathA = "";
        private string _pathB = "";
        private Vector2 _scroll;

        private class ObjectEntry
        {
            public string Name;
            public int VertCountA;
            public int VertCountB;
            public bool Checked;
            public bool Match;
        }

        // Aから抽出したオブジェクト名→頂点行リスト
        private Dictionary<string, List<string>> _vertexLinesA;
        // Aから抽出したオブジェクト名→頂点数
        private Dictionary<string, int> _vertexCountA;

        // Bの行配列
        private string[] _linesB;

        private List<ObjectEntry> _entries = new List<ObjectEntry>();

        private static readonly Regex _objectRegex = new Regex(
            @"^Object\s+""([^""]+)""\s*\{", RegexOptions.Compiled);
        private static readonly Regex _vertexDeclRegex = new Regex(
            @"^(\s*)vertex\s+(\d+)\s*\{", RegexOptions.Compiled);

        private void OnGUI()
        {
            EditorGUILayout.LabelField("MQO Vertex Position Restore", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // A
            EditorGUILayout.LabelField("A: 頂点位置（人工知能がUVをこわしたMQO）");
            EditorGUILayout.BeginHorizontal();
            _pathA = EditorGUILayout.TextField(_pathA);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string path = EditorUtility.OpenFilePanel("Select MQO (A)", "", "mqo");
                if (!string.IsNullOrEmpty(path)) _pathA = path;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // B
            EditorGUILayout.LabelField("B: 復旧用リファレンス（頂点位置を書き戻すMQO）");
            EditorGUILayout.BeginHorizontal();
            _pathB = EditorGUILayout.TextField(_pathB);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string path = EditorUtility.OpenFilePanel("Select MQO (B)", "", "mqo");
                if (!string.IsNullOrEmpty(path)) _pathB = path;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Load"))
            {
                LoadFiles();
            }

            if (_entries.Count == 0) return;

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All ON", GUILayout.Width(60)))
            {
                foreach (var e in _entries) if (e.Match) e.Checked = true;
            }
            if (GUILayout.Button("All OFF", GUILayout.Width(60)))
            {
                foreach (var e in _entries) e.Checked = false;
            }
            EditorGUILayout.EndHorizontal();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var e in _entries)
            {
                EditorGUILayout.BeginHorizontal();
                GUI.enabled = e.Match;
                e.Checked = EditorGUILayout.ToggleLeft(
                    $"{e.Name}  (A:{e.VertCountA}  B:{e.VertCountB}){(e.Match ? "" : "  ✗ 頂点数不一致")}",
                    e.Checked);
                GUI.enabled = true;
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8);

            int checkedCount = 0;
            foreach (var e in _entries) if (e.Checked) checkedCount++;

            GUI.enabled = checkedCount > 0;
            if (GUILayout.Button($"Restore Positions ({checkedCount} objects)"))
            {
                Execute();
            }
            GUI.enabled = true;
        }

        private void LoadFiles()
        {
            _entries.Clear();
            _vertexLinesA = null;
            _vertexCountA = null;
            _linesB = null;

            if (!File.Exists(_pathA))
            {
                EditorUtility.DisplayDialog("Error", $"File A not found:\n{_pathA}", "OK");
                return;
            }
            if (!File.Exists(_pathB))
            {
                EditorUtility.DisplayDialog("Error", $"File B not found:\n{_pathB}", "OK");
                return;
            }

            var encoding = System.Text.Encoding.GetEncoding("shift_jis");
            string[] linesA = File.ReadAllLines(_pathA, encoding);
            _linesB = File.ReadAllLines(_pathB, encoding);

            // A: オブジェクト名→頂点行を抽出
            _vertexLinesA = new Dictionary<string, List<string>>();
            _vertexCountA = new Dictionary<string, int>();
            ExtractVertexLines(linesA, _vertexLinesA, _vertexCountA);

            // B: オブジェクト名→頂点数を抽出
            var vertexCountB = new Dictionary<string, int>();
            ExtractVertexLines(_linesB, null, vertexCountB);

            // エントリ構築
            foreach (var kvp in _vertexCountA)
            {
                string name = kvp.Key;
                int countA = kvp.Value;
                int countB = vertexCountB.ContainsKey(name) ? vertexCountB[name] : -1;
                bool match = countA == countB && countB >= 0;
                _entries.Add(new ObjectEntry
                {
                    Name = name,
                    VertCountA = countA,
                    VertCountB = countB,
                    Checked = match,
                    Match = match,
                });
            }
        }

        /// <summary>
        /// MQO行配列からオブジェクト名→頂点行リスト/頂点数を抽出
        /// vertexLinesがnullの場合は頂点数のみカウント
        /// </summary>
        private static void ExtractVertexLines(
            string[] lines,
            Dictionary<string, List<string>> vertexLines,
            Dictionary<string, int> vertexCounts)
        {
            string currentObject = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();

                var om = _objectRegex.Match(trimmed);
                if (om.Success)
                {
                    currentObject = om.Groups[1].Value;
                    continue;
                }

                if (currentObject != null)
                {
                    var vm = _vertexDeclRegex.Match(trimmed);
                    if (vm.Success)
                    {
                        int count = int.Parse(vm.Groups[2].Value);
                        vertexCounts[currentObject] = count;

                        if (vertexLines != null)
                        {
                            var vlines = new List<string>(count);
                            for (int j = 0; j < count; j++)
                            {
                                i++;
                                if (i < lines.Length)
                                    vlines.Add(lines[i]);
                            }
                            vertexLines[currentObject] = vlines;
                        }
                        continue;
                    }
                }
            }
        }

        private void Execute()
        {
            var output = new List<string>(_linesB.Length);
            string currentObject = null;
            var checkedSet = new HashSet<string>();
            foreach (var e in _entries)
            {
                if (e.Checked) checkedSet.Add(e.Name);
            }

            int restored = 0;
            int i = 0;
            while (i < _linesB.Length)
            {
                string trimmed = _linesB[i].TrimStart();

                var om = _objectRegex.Match(trimmed);
                if (om.Success)
                {
                    currentObject = om.Groups[1].Value;
                    output.Add(_linesB[i]);
                    i++;
                    continue;
                }

                if (currentObject != null && checkedSet.Contains(currentObject))
                {
                    var vm = _vertexDeclRegex.Match(trimmed);
                    if (vm.Success)
                    {
                        int count = int.Parse(vm.Groups[2].Value);
                        output.Add(_linesB[i]); // "vertex N {" 行はそのまま
                        i++;

                        // Aの頂点行で差し替え
                        var aLines = _vertexLinesA[currentObject];
                        for (int j = 0; j < count; j++)
                        {
                            output.Add(aLines[j]);
                            i++;
                        }

                        restored++;
                        continue;
                    }
                }

                output.Add(_linesB[i]);
                i++;
            }

            string dir = Path.GetDirectoryName(_pathB);
            string name = Path.GetFileNameWithoutExtension(_pathB) + "_restored.mqo";
            string savePath = EditorUtility.SaveFilePanel("Save Restored MQO", dir, name, "mqo");

            if (string.IsNullOrEmpty(savePath)) return;

            var encoding = System.Text.Encoding.GetEncoding("shift_jis");
            File.WriteAllLines(savePath, output.ToArray(), encoding);

            EditorUtility.DisplayDialog("Complete",
                $"Restored {restored} objects.\nSaved to:\n{savePath}", "OK");
        }
    }
}
