// SkinWeightPaintTool.EditorUI.cs
// SkinWeightPaintToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using Poly_Ling.UI;

namespace Poly_Ling.Tools
{
    public partial class SkinWeightPaintTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Skin Weight Paint", EditorStyles.boldLabel);

            if (ActivePanel != null)
            {
                EditorGUILayout.HelpBox("設定はSkin Weight Paintパネルで変更してください。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Skin Weight Paintパネルを開くと詳細設定が使えます。", MessageType.Info);

                // 最低限の設定UI
                _settings.BrushRadius = EditorGUILayout.Slider("Radius", _settings.BrushRadius,
                    SkinWeightPaintSettings.MIN_BRUSH_RADIUS, SkinWeightPaintSettings.MAX_BRUSH_RADIUS);
                _settings.Strength = EditorGUILayout.Slider("Strength", _settings.Strength,
                    SkinWeightPaintSettings.MIN_STRENGTH, SkinWeightPaintSettings.MAX_STRENGTH);
                _settings.WeightValue = EditorGUILayout.Slider("Value", _settings.WeightValue, 0f, 1f);
            }
        }
    }
}
#endif
