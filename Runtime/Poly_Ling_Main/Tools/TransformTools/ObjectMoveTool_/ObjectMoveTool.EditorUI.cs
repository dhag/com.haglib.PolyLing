// ObjectMoveTool.EditorUI.cs
// ObjectMoveToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class ObjectMoveTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.miniBoldLabel);
            _settings.MoveWithChildren = EditorGUILayout.Toggle(T("MoveWithChildren"), _settings.MoveWithChildren);

            EditorGUILayout.Space(4);
            if (_lastCtx != null)
            {
                int count = GetSelectedCount(_lastCtx);
                if (count > 0)
                    EditorGUILayout.HelpBox(T("TargetObjects", count), MessageType.None);
            }
        }
    }
}
#endif
