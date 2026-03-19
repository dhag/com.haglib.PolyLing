// EdgeLoopSelectMode.EditorUI.cs
// EdgeLoopSelectModeのEditor専用設定UI
// IEditorAdvancedSelectModeUI実装

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using static Poly_Ling.Tools.SelectModeTexts;

namespace Poly_Ling.Tools
{
    public partial class EdgeLoopSelectMode : IEditorAdvancedSelectModeUI
    {
        public void DrawModeSettingsUI()
        {
            EditorGUILayout.HelpBox(T("EdgeLoopHelp"), MessageType.Info);
        }
    }
}
#endif
