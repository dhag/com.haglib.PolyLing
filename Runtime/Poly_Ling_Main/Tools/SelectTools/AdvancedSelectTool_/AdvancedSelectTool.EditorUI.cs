// AdvancedSelectTool.EditorUI.cs
// AdvancedSelectToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Core;
using Poly_Ling.Symmetry;

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

            // 属性選択モード（クリック不要。実行ボタンで動作）
            if (IsAttributeMode(Mode))
            {
                if (Mode == AdvancedSelectMode.UvNormalCount)
                {
                    UvNormalCountThreshold = EditorGUILayout.IntSlider(
                        T("UvNormalThreshold"), UvNormalCountThreshold,
                        0, ParameterLimits.GetI("AdvancedSelect.UvNormalCount.Max"));
                }
                else
                {
                    AxisKind = (SymmetryAxis)EditorGUILayout.EnumPopup(T("Axis"), AxisKind);
                    AxisDistanceThreshold = EditorGUILayout.FloatField(
                        T("AxisDistanceThreshold"), AxisDistanceThreshold);
                }

                LimitToCurrentSelection = EditorGUILayout.Toggle(
                    T("LimitToSelection"), LimitToCurrentSelection);

                if (GUILayout.Button(T("Execute")))
                {
                    ExecuteAttributeSelect(_lastToolCtx);
                }
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

            EditorGUILayout.Space(5);

            // 選択反転（全モード共通）
            if (GUILayout.Button(T("InvertSelection")))
            {
                InvertSelection(_lastToolCtx);
            }
        }
    }
}
#endif
