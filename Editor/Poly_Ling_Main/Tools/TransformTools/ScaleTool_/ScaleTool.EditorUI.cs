// ScaleTool.EditorUI.cs
// ScaleToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class ScaleTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            if (_ctx == null) return;
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);
            UpdateAffected();
            int totalAffected = GetTotalAffectedCount();
            EditorGUILayout.LabelField(T("TargetVertices", totalAffected), EditorStyles.miniLabel);
            if (totalAffected == 0) { EditorGUILayout.HelpBox("頂点を選択してください", MessageType.Info); return; }

            EditorGUI.BeginChangeCheck();
            _useOriginPivot = EditorGUILayout.Toggle(T("UseOrigin"), _useOriginPivot);
            if (EditorGUI.EndChangeCheck()) { UpdatePivot(); if (_isDirty) UpdatePreview(); }
            EditorGUILayout.LabelField($"{T("Pivot")}: ({_pivot.x:F2}, {_pivot.y:F2}, {_pivot.z:F2})", EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            EditorGUI.BeginChangeCheck();
            bool newUniform = EditorGUILayout.Toggle(T("Uniform"), _uniform);
            if (EditorGUI.EndChangeCheck() && newUniform != _uniform) { _uniform = newUniform; if (_uniform) { _scaleY = _scaleZ = _scaleX; } UpdatePreview(); }
            EditorGUILayout.Space(2);

            EditorGUI.BeginChangeCheck();
            if (_uniform)
            {
                float newScale = EditorGUILayout.Slider("XYZ", _scaleX, _settings.MIN_SCALE_XYZ, _settings.MAX_SCALE_XYZ);
                if (EditorGUI.EndChangeCheck()) { if (!_isSliderDragging) { _isSliderDragging = true; _ctx?.EnterTransformDragging?.Invoke(); } _scaleX = _scaleY = _scaleZ = newScale; UpdatePreview(); }
            }
            else
            {
                float newX = EditorGUILayout.Slider("X", _scaleX, _settings.MIN_SCALE_X, _settings.MAX_SCALE_X);
                float newY = EditorGUILayout.Slider("Y", _scaleY, _settings.MIN_SCALE_Y, _settings.MAX_SCALE_Y);
                float newZ = EditorGUILayout.Slider("Z", _scaleZ, _settings.MIN_SCALE_Z, _settings.MAX_SCALE_Z);
                if (EditorGUI.EndChangeCheck()) { if (!_isSliderDragging) { _isSliderDragging = true; _ctx?.EnterTransformDragging?.Invoke(); } _scaleX = newX; _scaleY = newY; _scaleZ = newZ; UpdatePreview(); }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Apply"))) { ExitSliderDragging(); ApplyScale(_ctx); _scaleX = _scaleY = _scaleZ = 1f; }
            if (GUILayout.Button(T("Reset"))) { ExitSliderDragging(); RevertToStart(); _scaleX = _scaleY = _scaleZ = 1f; }
            EditorGUILayout.EndHorizontal();
            if (_isSliderDragging && Event.current.type == EventType.MouseUp) ExitSliderDragging();
        }
    }
}
#endif
