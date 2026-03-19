// EdgeBevelTool.EditorUI.cs
// EdgeBevelToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class EdgeBevelTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(T("Amount"), GUILayout.Width(60));
            Amount = EditorGUILayout.FloatField(Amount, GUILayout.Width(60));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("0.05", EditorStyles.miniButtonLeft)) Amount = 0.05f;
            if (GUILayout.Button("0.1", EditorStyles.miniButtonMid)) Amount = 0.1f;
            if (GUILayout.Button("0.2", EditorStyles.miniButtonRight)) Amount = 0.2f;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Segments", GUILayout.Width(70));
            Segments = EditorGUILayout.IntSlider(Segments, 1, 10);
            EditorGUILayout.EndHorizontal();

            if (Segments >= 2)
            {
                EditorGUILayout.Space(3);
                Fillet = EditorGUILayout.Toggle("Fillet (Round)", Fillet);
            }
        }
    }
}
#endif
