// KnifeTool.EditorUI.cs
// KnifeToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Tools
{
    public partial class KnifeTool : IEditorToolUI
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

            EditorGUILayout.Space(5);

            // オプション
            if (Mode != KnifeMode.Erase)
            {
                EdgeSelect = EditorGUILayout.ToggleLeft(T("EdgeSelect"), EdgeSelect);
                AutoChain = EditorGUILayout.ToggleLeft(T("AutoChain"), AutoChain);
            }

            EditorGUILayout.Space(5);

            // Cut + EdgeSelect用
            if (EdgeSelect && Mode == KnifeMode.Cut)
            {
                _edgeBisectMode = EditorGUILayout.ToggleLeft(T("Bisect"), _edgeBisectMode);
                if (_edgeBisectMode)
                {
                    _cutRatio = EditorGUILayout.Slider(T("CutPosition"), _cutRatio, 0.1f, 0.9f);//スライダーの上限下限
                }
            }

            // Vertex用
            if (Mode == KnifeMode.Vertex)
            {
                _vertexBisectMode = EditorGUILayout.ToggleLeft(T("Bisect"), _vertexBisectMode);
            }

            // 選択状態表示
            if (EdgeSelect)
            {
                if (Mode == KnifeMode.Cut && _firstEdgeWorldPos.HasValue)
                {
                    EditorGUILayout.LabelField(T("FirstEdgeSelected"));
                    if (_beltEdgePositions.Count > 0)
                    {
                        EditorGUILayout.LabelField(T("EdgesToCut", _beltEdgePositions.Count));
                    }
                }
                else if (Mode == KnifeMode.Vertex && _firstVertexWorldPos.HasValue)
                {
                    EditorGUILayout.LabelField(T("VertexSelected"));
                }
            }

            EditorGUILayout.Space(5);

            // ヘルプ
            string helpText = GetHelpText();
            if (!string.IsNullOrEmpty(helpText))
            {
                EditorGUILayout.HelpBox(helpText, MessageType.Info);
            }
        }
    }
}
#endif
