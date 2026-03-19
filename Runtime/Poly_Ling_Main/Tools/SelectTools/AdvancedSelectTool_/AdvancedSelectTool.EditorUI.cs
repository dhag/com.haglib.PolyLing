// AdvancedSelectTool.EditorUI.cs
// AdvancedSelectToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class AdvancedSelectTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);

            // モード選択
            int currentIndex = Array.IndexOf(ModeValues, Mode);
            EditorGUI.BeginChangeCheck();
            int newIndex = GUILayout.Toolbar(currentIndex, GetLocalizedModeNames());
            if (EditorGUI.EndChangeCheck() && newIndex != currentIndex)
            {
                Mode = ModeValues[newIndex];
                ResetAllModes();
            }

            EditorGUILayout.Space(5);

            // モード別設定
            if (_modes.TryGetValue(Mode, out var mode))
            {
                (mode as IEditorAdvancedSelectModeUI)?.DrawModeSettingsUI();
            }

            // EdgeLoopモードの追加設定
            if (Mode == AdvancedSelectMode.EdgeLoop)
            {
                EdgeLoopThreshold = EditorGUILayout.Slider(T("DirectionThreshold"), EdgeLoopThreshold, 0f, 1f); //スライダーの上限下限
            }

            EditorGUILayout.Space(5);

            // 追加/削除モード
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(T("Action"), GUILayout.Width(50));
            if (GUILayout.Toggle(AddToSelection, T("Add"), EditorStyles.miniButtonLeft))
                AddToSelection = true;
            if (GUILayout.Toggle(!AddToSelection, T("Remove"), EditorStyles.miniButtonRight))
                AddToSelection = false;
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
