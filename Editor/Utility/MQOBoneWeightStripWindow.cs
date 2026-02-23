// MQOBoneWeightStripWindow.cs
// MQOファイルからボーンウェイト特殊面（四角形）のみを除去する
// 頂点・UV・通常面・属性等は一切変更しない
// Menu: Tools/Poly_Ling/Utility/MQO BoneWeight Strip

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Editor.Utility
{
    public class MQOBoneWeightStripWindow : EditorWindow
    {
        [MenuItem("Tools/Poly_Ling/Utility/MQO BoneWeight Strip")]
        public static void ShowWindow()
        {
            GetWindow<MQOBoneWeightStripWindow>("MQO BoneWeight Strip");
        }

        private string _path = "";
        private string _result = "";

        // 四角形特殊面の判定: "4 V(N N N N)" で N が全て同一、かつ COL あり
        // ボーンウェイト特殊面 = 四角形 + 全頂点同一 + COL属性
        private static readonly Regex _faceLineRegex = new Regex(
            @"^\s*4\s+V\(\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\)",
            RegexOptions.Compiled);

        private void OnGUI()
        {
            EditorGUILayout.LabelField("MQO BoneWeight Strip", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "MQOファイルからボーンウェイト特殊面（四角形・全頂点同一・COL付き）のみを除去します。\n" +
                "頂点・UV・通常面・属性等は一切変更しません。",
                MessageType.Info);

            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("MQO File", _path);
            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                string path = EditorUtility.OpenFilePanel("Select MQO", "", "mqo");
                if (!string.IsNullOrEmpty(path)) _path = path;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Strip BoneWeight Faces"))
            {
                Execute();
            }

            if (!string.IsNullOrEmpty(_result))
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(_result, MessageType.None);
            }
        }

        private void Execute()
        {
            _result = "";

            if (!File.Exists(_path))
            {
                EditorUtility.DisplayDialog("Error", $"File not found:\n{_path}", "OK");
                return;
            }

            // Shift-JISで読み込み（MQO標準）
            var encoding = System.Text.Encoding.GetEncoding("shift_jis");
            string[] lines = File.ReadAllLines(_path, encoding);

            var output = new List<string>();
            int totalStripped = 0;
            bool inFaceBlock = false;
            int faceBlockIndent = -1;
            int declaredFaceCount = 0;
            int strippedInCurrentBlock = 0;
            int faceCountLineIndex = -1; // output内のface宣言行インデックス
            string faceCountPrefix = "";  // "	face " 等

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();

                // faceブロック開始検出: "face N {"
                if (!inFaceBlock && trimmed.StartsWith("face ") && trimmed.Contains("{"))
                {
                    inFaceBlock = true;
                    strippedInCurrentBlock = 0;

                    // face数を取得
                    var m = Regex.Match(trimmed, @"face\s+(\d+)\s*\{");
                    if (m.Success)
                    {
                        declaredFaceCount = int.Parse(m.Groups[1].Value);
                    }

                    // インデント保持
                    faceBlockIndent = line.Length - line.TrimStart().Length;
                    faceCountLineIndex = output.Count;
                    faceCountPrefix = line.Substring(0, faceBlockIndent);

                    output.Add(line); // 後で書き換える
                    continue;
                }

                if (inFaceBlock)
                {
                    // faceブロック終了: インデントレベルが同じかそれ以上の "}"
                    if (trimmed == "}")
                    {
                        // face数を更新
                        int newCount = declaredFaceCount - strippedInCurrentBlock;
                        output[faceCountLineIndex] = $"{faceCountPrefix}face {newCount} {{";

                        totalStripped += strippedInCurrentBlock;
                        inFaceBlock = false;
                        output.Add(line);
                        continue;
                    }

                    // ボーンウェイト特殊面か判定
                    if (IsBoneWeightSpecialFace(trimmed))
                    {
                        strippedInCurrentBlock++;
                        continue; // この行を出力しない
                    }

                    output.Add(line);
                    continue;
                }

                output.Add(line);
            }

            if (totalStripped == 0)
            {
                _result = "ボーンウェイト特殊面は見つかりませんでした。";
                return;
            }

            // 保存
            string dir = Path.GetDirectoryName(_path);
            string name = Path.GetFileNameWithoutExtension(_path) + "_stripped.mqo";
            string savePath = EditorUtility.SaveFilePanel("Save Stripped MQO", dir, name, "mqo");

            if (string.IsNullOrEmpty(savePath)) return;

            File.WriteAllLines(savePath, output.ToArray(), encoding);

            _result = $"除去完了: {totalStripped} 面を除去\n保存先: {savePath}";
        }

        /// <summary>
        /// ボーンウェイト特殊面の判定
        /// 条件: 四角形 + 全頂点インデックス同一 + COL属性あり
        /// </summary>
        private static bool IsBoneWeightSpecialFace(string trimmedLine)
        {
            var m = _faceLineRegex.Match(trimmedLine);
            if (!m.Success) return false;

            // 全頂点インデックスが同一か
            string v0 = m.Groups[1].Value;
            if (v0 != m.Groups[2].Value || v0 != m.Groups[3].Value || v0 != m.Groups[4].Value)
                return false;

            // COL属性があるか
            if (trimmedLine.IndexOf("COL(", System.StringComparison.Ordinal) < 0)
                return false;

            return true;
        }
    }
}
