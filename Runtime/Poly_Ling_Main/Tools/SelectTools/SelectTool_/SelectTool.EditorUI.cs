// SelectTool.EditorUI.cs
// SelectToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class SelectTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("ClickToSelect"), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(T("ShiftClick"), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(T("DragSelect"), EditorStyles.miniLabel);
        }
    }
}
#endif
