// Assets/Editor/MeshCreators/BoneTransformUI.cs
// BoneTransform用UI描画クラス（UnityEditor依存部分）
// BoneTransform.cs から分離

using System;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Localization;
using Poly_Ling.Data;

namespace Poly_Ling.Data
{
    public static partial class BoneTransformUI
    {
        // === イベント ===

        public static event Action<BoneTransformSnapshot, BoneTransformSnapshot, string> OnChanged;
        public static event Action OnResetClicked;
        public static event Action OnFromSelectionClicked;

        // === スタイル ===
        private static GUIStyle _headerStyle;
        private static GUIStyle _compactLabelStyle;

        public static bool DrawUI(BoneTransform settings)
        {
            if (settings == null) return false;

            InitStyles();

            bool changed = false;
            string changeDescription = null;
            BoneTransformSnapshot before = settings.CreateSnapshot();

            // === ヘッダー + 折りたたみ ===
            EditorGUILayout.BeginHorizontal();
            {
                settings.IsExpanded = EditorGUILayout.Foldout(
                    settings.IsExpanded,
                    T("Title"),
                    true
                );

                bool newUse = EditorGUILayout.Toggle(settings.UseLocalTransform, GUILayout.Width(20));
                if (newUse != settings.UseLocalTransform)
                {
                    settings.UseLocalTransform = newUse;
                    changed = true;
                    changeDescription = newUse ? T("EnableLocalTransform") : T("DisableLocalTransform");
                }
            }
            EditorGUILayout.EndHorizontal();

            if (settings.IsExpanded)
            {
                EditorGUI.BeginDisabledGroup(!settings.UseLocalTransform);
                {
                    EditorGUI.indentLevel++;

                    // Position
                    EditorGUILayout.LabelField(T("Position"), EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUIUtility.labelWidth = 14;
                        float px = EditorGUILayout.FloatField("X", settings.Position.x);
                        float py = EditorGUILayout.FloatField("Y", settings.Position.y);
                        float pz = EditorGUILayout.FloatField("Z", settings.Position.z);
                        EditorGUIUtility.labelWidth = 0;

                        Vector3 newPos = new Vector3(px, py, pz);
                        if (newPos != settings.Position)
                        {
                            settings.Position = newPos;
                            changed = true;
                            changeDescription = T("ChangePosition");
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    // Rotation
                    EditorGUILayout.LabelField(T("Rotation"), EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUIUtility.labelWidth = 14;
                        float rx = EditorGUILayout.FloatField("X", settings.Rotation.x);
                        float ry = EditorGUILayout.FloatField("Y", settings.Rotation.y);
                        float rz = EditorGUILayout.FloatField("Z", settings.Rotation.z);
                        EditorGUIUtility.labelWidth = 0;

                        Vector3 newRot = new Vector3(rx, ry, rz);
                        if (newRot != settings.Rotation)
                        {
                            settings.Rotation = newRot;
                            changed = true;
                            changeDescription = T("ChangeRotation");
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    // 回転軸選択 + スライダー
                    EditorGUILayout.BeginHorizontal();
                    {
                        GUILayout.Label("Axis:", GUILayout.Width(35));

                        bool selX = (settings.SelectedRotationAxis == 0);
                        bool selY = (settings.SelectedRotationAxis == 1);
                        bool selZ = (settings.SelectedRotationAxis == 2);

                        bool newSelX = GUILayout.Toggle(selX, "X", EditorStyles.miniButton, GUILayout.Width(28));
                        bool newSelY = GUILayout.Toggle(selY, "Y", EditorStyles.miniButton, GUILayout.Width(28));
                        bool newSelZ = GUILayout.Toggle(selZ, "Z", EditorStyles.miniButton, GUILayout.Width(28));

                        if (newSelX && !selX) settings.SelectedRotationAxis = 0;
                        else if (newSelY && !selY) settings.SelectedRotationAxis = 1;
                        else if (newSelZ && !selZ) settings.SelectedRotationAxis = 2;

                        GUILayout.FlexibleSpace();
                    }
                    EditorGUILayout.EndHorizontal();

                    // 回転スライダー
                    EditorGUILayout.BeginHorizontal();
                    {
                        string axisLabel = settings.SelectedRotationAxis == 0 ? "X" :
                                           (settings.SelectedRotationAxis == 1 ? "Y" : "Z");
                        float currentValue = settings.SelectedRotationAxis == 0 ? settings.Rotation.x :
                                             (settings.SelectedRotationAxis == 1 ? settings.Rotation.y : settings.Rotation.z);

                        GUILayout.Label(axisLabel + ":", GUILayout.Width(20));
                        float newValue = GUILayout.HorizontalSlider(currentValue, -180f, 180f);
                        GUILayout.Label(newValue.ToString("F1") + "°", GUILayout.Width(50));

                        if (Mathf.Abs(newValue - currentValue) > 0.01f)
                        {
                            Vector3 rot = settings.Rotation;
                            if (settings.SelectedRotationAxis == 0) rot.x = newValue;
                            else if (settings.SelectedRotationAxis == 1) rot.y = newValue;
                            else rot.z = newValue;
                            settings.Rotation = rot;
                            changed = true;
                            changeDescription = T("ChangeRotation");
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    // Scale
                    EditorGUILayout.LabelField(T("Scale"), EditorStyles.miniLabel);
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUIUtility.labelWidth = 14;
                        float sx = EditorGUILayout.FloatField("X", settings.Scale.x);
                        float sy = EditorGUILayout.FloatField("Y", settings.Scale.y);
                        float sz = EditorGUILayout.FloatField("Z", settings.Scale.z);
                        EditorGUIUtility.labelWidth = 0;

                        Vector3 newScale = new Vector3(sx, sy, sz);
                        if (newScale != settings.Scale)
                        {
                            settings.Scale = newScale;
                            changed = true;
                            changeDescription = T("ChangeScale");
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUI.indentLevel--;
                }
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(4);

                EditorGUILayout.BeginHorizontal();
                {
                    if (GUILayout.Button(T("FromSelection"), GUILayout.Height(18)))
                        OnFromSelectionClicked?.Invoke();
                    if (GUILayout.Button(T("Reset"), GUILayout.Height(18)))
                        OnResetClicked?.Invoke();
                }
                EditorGUILayout.EndHorizontal();
            }

            if (changed)
            {
                BoneTransformSnapshot after = settings.CreateSnapshot();
                if (before.IsDifferentFrom(after))
                    OnChanged?.Invoke(before, after, changeDescription);
            }

            return changed;
        }

        public static bool DrawCompactUI(BoneTransform settings)
        {
            if (settings == null) return false;

            bool changed = false;
            BoneTransformSnapshot before = settings.CreateSnapshot();

            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField($"{T("Title")}:", GUILayout.Width(100));

                bool newUse = EditorGUILayout.Toggle(settings.UseLocalTransform, GUILayout.Width(20));
                if (newUse != settings.UseLocalTransform)
                {
                    settings.UseLocalTransform = newUse;
                    changed = true;
                }

                if (settings.UseLocalTransform)
                    EditorGUILayout.LabelField(
                        $"P:{settings.Position:F1} R:{settings.Rotation:F1} S:{settings.Scale:F1}",
                        EditorStyles.miniLabel);
                else
                    EditorGUILayout.LabelField(T("Default"), EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();

            if (changed)
            {
                BoneTransformSnapshot after = settings.CreateSnapshot();
                if (before.IsDifferentFrom(after))
                    OnChanged?.Invoke(before, after, T("ChangeSettings"));
            }

            return changed;
        }

        private static void InitStyles()
        {
            if (_compactLabelStyle == null)
            {
                _compactLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight
                };
            }
        }

        public static void NotifyChanged(BoneTransformSnapshot before, BoneTransformSnapshot after, string description)
        {
            if (before.IsDifferentFrom(after))
                OnChanged?.Invoke(before, after, description);
        }
    }
}
