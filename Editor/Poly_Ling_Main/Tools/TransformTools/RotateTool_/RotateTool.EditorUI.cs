// RotateTool.EditorUI.cs
// RotateToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class RotateTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            if (_ctx == null) return;

            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);

            UpdateAffected();
            int totalAffected = GetTotalAffectedCount();
            EditorGUILayout.LabelField(T("TargetVertices", totalAffected), EditorStyles.miniLabel);

            if (totalAffected == 0)
            {
                EditorGUILayout.HelpBox("頂点を選択してください", MessageType.Info);
                return;
            }

            // ピボット
            EditorGUI.BeginChangeCheck();
            _useOriginPivot = EditorGUILayout.Toggle(T("UseOrigin"), _useOriginPivot);
            if (EditorGUI.EndChangeCheck())
            {
                UpdatePivot();
                if (_isDirty) UpdatePreview();
            }

            EditorGUILayout.LabelField($"{T("Pivot")}: ({_pivot.x:F2}, {_pivot.y:F2}, {_pivot.z:F2})", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);

            // 回転スライダー
            EditorGUI.BeginChangeCheck();
            float newX = EditorGUILayout.Slider("X", _rotX, -180f, 180f);
            float newY = EditorGUILayout.Slider("Y", _rotY, -180f, 180f);
            float newZ = EditorGUILayout.Slider("Z", _rotZ, -180f, 180f);

            if (EditorGUI.EndChangeCheck())
            {
                if (!_isSliderDragging)
                {
                    _isSliderDragging = true;
                    _ctx?.EnterTransformDragging?.Invoke();
                }
                if (_useSnap)
                {
                    newX = Mathf.Round(newX / _snapAngle) * _snapAngle;
                    newY = Mathf.Round(newY / _snapAngle) * _snapAngle;
                    newZ = Mathf.Round(newZ / _snapAngle) * _snapAngle;
                }
                _rotX = newX;
                _rotY = newY;
                _rotZ = newZ;
                UpdatePreview();
            }

            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            _useSnap = EditorGUILayout.Toggle(T("Snap"), _useSnap, GUILayout.Width(100));
            if (_useSnap) _snapAngle = EditorGUILayout.FloatField(_snapAngle, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(T("Apply")))
            {
                ExitSliderDragging();
                ApplyRotation(_ctx);
                _rotX = _rotY = _rotZ = 0f;
            }
            if (GUILayout.Button(T("Reset")))
            {
                ExitSliderDragging();
                RevertToStart();
                _rotX = _rotY = _rotZ = 0f;
            }
            EditorGUILayout.EndHorizontal();

            // スライダードラッグ終了検出
            if (_isSliderDragging && Event.current.type == EventType.MouseUp)
            {
                ExitSliderDragging();
            }
        }
    }
}
#endif
