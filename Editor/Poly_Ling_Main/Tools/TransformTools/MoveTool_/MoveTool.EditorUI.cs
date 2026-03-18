// MoveTool.EditorUI.cs
// MoveToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using Poly_Ling.Tools;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class MoveTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Magnet"), EditorStyles.miniBoldLabel);

            // MoveSettingsを直接編集（Undo検出はGUI_Tools側で行う）
            _settings.UseMagnet = EditorGUILayout.Toggle(T("Enable"), _settings.UseMagnet);

            using (new EditorGUI.DisabledScope(!_settings.UseMagnet))
            {
                _settings.MagnetRadius = EditorGUILayout.Slider(T("Radius"), _settings.MagnetRadius, _settings.MIN_MAGNET_RADIUS, _settings.MAX_MAGNET_RADIUS);//スライダーの上限下限
                _settings.MagnetFalloff = (FalloffType)EditorGUILayout.EnumPopup(T("Falloff"), _settings.MagnetFalloff);
            }

            // ギズモ設定（Undo対象外）
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField(T("Gizmo"), EditorStyles.miniBoldLabel);
            _gizmoScreenOffset.x = EditorGUILayout.Slider(T("OffsetX"), _gizmoScreenOffset.x, _settings.MIN_SCREEN_OFFSET_X, _settings.MAX_SCREEN_OFFSET_X);//スライダーの上限下限
            _gizmoScreenOffset.y = EditorGUILayout.Slider(T("OffsetY"), _gizmoScreenOffset.y, _settings.MIN_SCREEN_OFFSET_Y, _settings.MAX_SCREEN_OFFSET_Y);//スライダーの上限下限

            // 選択情報表示
            EditorGUILayout.Space(5);
            int totalAffected = GetTotalAffectedCount(_lastContext);
            if (totalAffected > 0)
            {
                EditorGUILayout.HelpBox(T("TargetVertices", totalAffected), MessageType.None);
            }
        }
    }
}
#endif
