// ShortestPathSelectMode.EditorUI.cs
// ShortestPathSelectModeのEditor専用設定UI
// IEditorAdvancedSelectModeUI実装

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using static Poly_Ling.Tools.SelectModeTexts;

namespace Poly_Ling.Tools
{
    public partial class ShortestPathSelectMode : IEditorAdvancedSelectModeUI
    {
        public void DrawModeSettingsUI()
        {
            EditorGUILayout.HelpBox(T("ShortestPathHelp"), MessageType.Info);

            if (_firstVertex >= 0)
            {
                EditorGUILayout.LabelField(T("FirstVertex", _firstVertex));
                if (GUILayout.Button(T("ClearFirstPoint")))
                {
                    _firstVertex = -1;
                }
            }
        }
    }
}
#endif
