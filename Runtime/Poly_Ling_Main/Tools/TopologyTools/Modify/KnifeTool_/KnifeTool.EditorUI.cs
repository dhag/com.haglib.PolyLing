// Tools/TopologyTools/Modify/KnifeTool_/KnifeTool.EditorUI.cs
// KnifeTool の Editor 専用設定 UI（IEditorToolUI 実装）。Runtime では存在しない。

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class KnifeTool : IEditorToolUI
    {
        private static readonly string[] EditorModeNames = { "Ladder Cut", "Erase" };
        private static readonly KnifeMode[] EditorModeValues = { KnifeMode.LadderCut, KnifeMode.Erase };

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);

            int cur = Array.IndexOf(EditorModeValues, Mode);
            int next = GUILayout.SelectionGrid(cur, EditorModeNames, 2);
            if (next != cur && next >= 0 && next < EditorModeValues.Length)
                Mode = EditorModeValues[next];

            EditorGUILayout.Space(5);

            string help = StageText();
            if (!string.IsNullOrEmpty(help))
                EditorGUILayout.HelpBox(help, MessageType.Info);
        }
    }
}
#endif
