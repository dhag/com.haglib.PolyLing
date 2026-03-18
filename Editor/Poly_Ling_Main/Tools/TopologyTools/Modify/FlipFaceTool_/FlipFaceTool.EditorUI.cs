// FlipFaceTool.EditorUI.cs
// FlipFaceToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class FlipFaceTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(5);

            // 反転ボタン
            if (GUILayout.Button(T("FlipSelected"), GUILayout.Height(30)))
            {
                FlipSelectedFaces();
            }

            // 全面反転ボタン
            EditorGUILayout.Space(3);
            if (GUILayout.Button(T("FlipAll")))
            {
                FlipAllFaces();
            }

            // 結果メッセージ
            if (!string.IsNullOrEmpty(_lastMessage))
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.HelpBox(_lastMessage, MessageType.None);
            }
        }
    }
}
#endif
