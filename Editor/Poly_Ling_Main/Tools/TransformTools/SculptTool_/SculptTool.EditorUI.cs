// SculptTool.EditorUI.cs
// SculptToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class SculptTool : IEditorToolUI
    {
            public void DrawSettingsUI()
            {
                EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);

                // モード選択
                int currentIndex = Array.IndexOf(ModeValues, Mode);
                int newIndex = GUILayout.SelectionGrid(currentIndex, ModeNames, 2);
                if (newIndex != currentIndex && newIndex >= 0 && newIndex < ModeValues.Length)
                {
                    Mode = ModeValues[newIndex];
                }

                EditorGUILayout.Space(5);

                BrushRadius = EditorGUILayout.Slider(T("BrushSize"), BrushRadius,
                    SculptSettings.MIN_BRUSH_RADIUS, SculptSettings.MAX_BRUSH_RADIUS);

                Strength = EditorGUILayout.Slider(T("Strength"), Strength,
                    SculptSettings.MIN_STRENGTH, SculptSettings.MAX_STRENGTH);

                Invert = EditorGUILayout.Toggle(T("Invert"), Invert);

                EditorGUILayout.Space(3);

                EditorGUILayout.HelpBox(GetModeHelp(Mode), MessageType.Info);
            }
    }
}
#endif
