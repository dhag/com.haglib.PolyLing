// ConnectedSelectMode.EditorUI.cs
// ConnectedSelectModeのEditor専用設定UI
// IEditorAdvancedSelectModeUI実装

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using static Poly_Ling.Tools.SelectModeTexts;

namespace Poly_Ling.Tools
{
    public partial class ConnectedSelectMode : IEditorAdvancedSelectModeUI
    {
        public void DrawModeSettingsUI()
        {
            EditorGUILayout.HelpBox(T("ConnectedHelp"), MessageType.Info);
        }
    }
}
#endif
