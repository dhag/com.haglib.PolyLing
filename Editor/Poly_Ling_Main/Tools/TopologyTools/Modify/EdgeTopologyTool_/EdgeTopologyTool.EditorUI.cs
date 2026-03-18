// EdgeTopologyTool.EditorUI.cs
// EdgeTopologyToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class EdgeTopologyTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);

            // モード選択
            int currentIndex = Array.IndexOf(ModeValues, Mode);
            int newIndex = GUILayout.SelectionGrid(currentIndex, ModeNames, 3);
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < ModeValues.Length)
            {
                Mode = ModeValues[newIndex];
                Reset();
            }

            EditorGUILayout.Space(3);

            // モード説明
            switch (Mode)
            {
                case EdgeTopoMode.Flip:
                    EditorGUILayout.HelpBox(T("HelpFlip"), MessageType.Info);
                    break;
                case EdgeTopoMode.Split:
                    EditorGUILayout.HelpBox(T("HelpSplit"), MessageType.Info);
                    break;
                case EdgeTopoMode.Dissolve:
                    EditorGUILayout.HelpBox(T("HelpDissolve"), MessageType.Info);
                    break;
            }

            // ステータス表示
            EditorGUILayout.Space(5);
            DrawStatusUI();
        }
    }
}
#endif
