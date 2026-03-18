// AddFaceTool.EditorUI.cs
// AddFaceToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class AddFaceTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            // モード選択
            EditorGUILayout.LabelField(T("FaceType"), EditorStyles.miniBoldLabel);  // ← 変更
            int currentIndex = System.Array.IndexOf(ModeValues, Mode);
            int newIndex = GUILayout.SelectionGrid(currentIndex, LocalizedModeNames, 3);  // ← 変更
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < ModeValues.Length)
            {
                Mode = ModeValues[newIndex];
                _points.Clear();
                _lastLinePoint = null;
            }

            // Lineモード専用オプション
            if (Mode == AddFaceMode.Line)
            {
                EditorGUILayout.Space(4);
                EditorGUI.BeginChangeCheck();
                ContinuousLine = EditorGUILayout.Toggle(T("ContinuousLine"), ContinuousLine);  // ← 変更
                if (EditorGUI.EndChangeCheck())
                {
                    _lastLinePoint = null;
                }

                if (ContinuousLine && _lastLinePoint.HasValue)
                {
                    EditorGUILayout.LabelField(T("ContinuousHint"), EditorStyles.miniLabel);  // ← 変更
                }
            }

            EditorGUILayout.Space(4);

            // 進捗表示
            string progressText;
            if (Mode == AddFaceMode.Line && ContinuousLine && _lastLinePoint.HasValue)
            {
                progressText = T("ClickToContinue");  // ← 変更
            }
            else
            {
                progressText = T("Progress", _points.Count, RequiredPoints);  // ← 変更
            }
            EditorGUILayout.LabelField(progressText, EditorStyles.miniLabel);

            // 配置済み点の座標表示
            if (_points.Count > 0)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(T("PlacedPoints"), EditorStyles.miniBoldLabel);  // ← 変更

                for (int i = 0; i < _points.Count; i++)
                {
                    var p = _points[i];
                    string label = p.IsExistingVertex
                        ? T("PointExisting", i + 1, p.ExistingVertexIndex)  // ← 変更
                        : T("PointNew", i + 1);  // ← 変更
                    EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(4);

            // クリアボタン
            bool hasData = _points.Count > 0 || (ContinuousLine && _lastLinePoint.HasValue);
            if (hasData)
            {
                if (GUILayout.Button(T("ClearPoints")))  // ← 変更
                {
                    _points.Clear();
                    _lastLinePoint = null;
                }
            }
        }
    }
}
#endif
