// EdgeExtrudeTool.EditorUI.cs
// EdgeExtrudeToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class EdgeExtrudeTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(5);

            Mode = (EdgeExtrudeSettings.ExtrudeMode)EditorGUILayout.EnumPopup("Mode", Mode);
            SnapToAxis = EditorGUILayout.Toggle("Snap to Axis", SnapToAxis);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Selected edges will be extruded", EditorStyles.miniLabel);
        }
    }
}
#endif
