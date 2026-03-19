// BeltSelectMode.EditorUI.cs
// BeltSelectModeのEditor専用設定UI
// IEditorAdvancedSelectModeUI実装

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using static Poly_Ling.Tools.SelectModeTexts;

namespace Poly_Ling.Tools
{
    public partial class BeltSelectMode : IEditorAdvancedSelectModeUI
    {
        public void DrawModeSettingsUI()
        {
            EditorGUILayout.HelpBox(T("BeltHelp"), MessageType.Info);
        }
    }
}
#endif
