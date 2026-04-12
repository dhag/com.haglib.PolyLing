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

                BrushRadius = EditorGUILayout.Slider(T("BrushRadius"), BrushRadius,
                    _settings.MinBrushRadius, _settings.MaxBrushRadius);

                // フォールオフ
                Falloff = (FalloffType)EditorGUILayout.EnumPopup(T("Falloff"), Falloff);

                Strength = EditorGUILayout.Slider(T("Strength"), Strength,
                    _settings.MinStrength, _settings.MaxStrength);

                Invert = EditorGUILayout.Toggle(T("Invert"), Invert);

                EditorGUILayout.Space(3);

                EditorGUILayout.HelpBox(GetModeHelp(Mode), MessageType.Info);
            }
    }
}
#endif
