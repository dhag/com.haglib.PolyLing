// FaceExtrudeTool.EditorUI.cs
// FaceExtrudeToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class FaceExtrudeTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(5);

            Type = (FaceExtrudeSettings.ExtrudeType)EditorGUILayout.EnumPopup("Type", Type);

            if (Type == FaceExtrudeSettings.ExtrudeType.Bevel)
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField(T("BevelSettings"), EditorStyles.miniBoldLabel);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(T("Scale"), GUILayout.Width(50));
                BevelScale = EditorGUILayout.Slider(BevelScale, 0.01f, 1f);//スライダーの上限下限
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("0.5", EditorStyles.miniButtonLeft)) BevelScale = 0.5f;
                if (GUILayout.Button("0.8", EditorStyles.miniButtonMid)) BevelScale = 0.8f;
                if (GUILayout.Button("1.0", EditorStyles.miniButtonRight)) BevelScale = 1.0f;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);
            IndividualNormals = EditorGUILayout.Toggle("Individual Normals", IndividualNormals);
        }
    }
}
#endif
