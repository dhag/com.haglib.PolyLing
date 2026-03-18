// PivotOffsetTool.EditorUI.cs
// PivotOffsetToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class PivotOffsetTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.miniBoldLabel);  // ← 変更
            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);  // ← 変更

            if (_state != ToolState.Idle)
            {
                EditorGUILayout.LabelField(T("Moving", _totalOffset.ToString("F3")));  // ← 変更
            }
        }
    }
}
#endif
