// LineExtrudeTool.EditorUI.cs
// LineExtrudeToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class LineExtrudeTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(5);

            // 選択情報
            EditorGUILayout.LabelField(T("SelectedLines", _selectedLineIndices.Count), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(T("DetectedLoops", _detectedLoops.Count), EditorStyles.miniLabel);

            // ループ詳細
            if (_detectedLoops.Count > 0)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField(T("Loops"), EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;
                for (int i = 0; i < _detectedLoops.Count; i++)
                {
                    var loop = _detectedLoops[i];
                    string typeStr = loop.IsHole ? T("Hole") : T("Outer");
                    EditorGUILayout.LabelField(T("LoopInfo", i + 1, loop.VertexIndices.Count, typeStr));
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            // 解析ボタン
            if (GUILayout.Button(T("AnalyzeLoops"), GUILayout.Height(25)))
            {
                AnalyzeLoops();
            }

            EditorGUILayout.Space(5);

            // 保存ボタン
            EditorGUI.BeginDisabledGroup(_detectedLoops.Count == 0);
            if (GUILayout.Button(T("SaveAsCSV"), GUILayout.Height(30)))
            {
                SaveAsCSV();
            }
            EditorGUI.EndDisabledGroup();

            if (_selectedLineIndices.Count < 3)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(T("SelectMinLines"), MessageType.Warning);
            }
        }
    }
}
#endif
