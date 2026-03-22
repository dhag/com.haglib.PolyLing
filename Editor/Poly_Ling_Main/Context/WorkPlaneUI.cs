// WorkPlaneUI.cs
// WorkPlane UI描画（Editor専用）
// WorkPlaneContext.cs から分離

using System;
using UnityEditor;
using UnityEngine;
using Poly_Ling.Context;

namespace Poly_Ling.Context
{
    public static class WorkPlaneUI
    {
        private static readonly string[] ModeNames = new[]
        {
            "Camera Parallel",
            "World XY",
            "World XZ (Floor)",
            "World YZ",
            "Custom"
        };

        private static GUIStyle _compactLabelStyle;

        public static event Action OnFromSelectionClicked;
        public static event Action<WorkPlaneSnapshot, WorkPlaneSnapshot, string> OnChanged;

        public static bool DrawUI(WorkPlaneContext workPlane)
        {
            if (workPlane == null) return false;

            InitStyles();

            WorkPlaneSnapshot before = workPlane.CreateSnapshot();
            bool changed = false;
            string changeDescription = "";

            EditorGUILayout.BeginHorizontal();
            {
                workPlane.IsExpanded = EditorGUILayout.Foldout(workPlane.IsExpanded, "Work Plane", true);
                GUILayout.FlexibleSpace();
                string compactText = workPlane.IsExpanded ? "" : ModeNames[(int)workPlane.Mode];
                EditorGUILayout.LabelField(compactText, _compactLabelStyle, GUILayout.Width(70));
                string lockLabel = workPlane.IsLocked ? "🔒" : "🔓";
                if (GUILayout.Button(lockLabel, GUILayout.Width(24), GUILayout.Height(18)))
                {
                    workPlane.IsLocked = !workPlane.IsLocked;
                    changed = true;
                    changeDescription = workPlane.IsLocked ? "Lock WorkPlane" : "Unlock WorkPlane";
                }
                if (GUILayout.Button("⟲", GUILayout.Width(24), GUILayout.Height(18)))
                {
                    workPlane.Reset();
                    changed = true;
                    changeDescription = "Reset WorkPlane";
                }
            }
            EditorGUILayout.EndHorizontal();

            if (workPlane.IsExpanded)
            {
                EditorGUI.BeginDisabledGroup(workPlane.IsLocked);
                {
                    WorkPlaneMode newMode = (WorkPlaneMode)EditorGUILayout.Popup("Mode", (int)workPlane.Mode, ModeNames);
                    if (newMode != workPlane.Mode) { workPlane.Mode = newMode; changed = true; changeDescription = $"Change WorkPlane Mode to {newMode}"; }

                    EditorGUILayout.Space(2);
                    EditorGUILayout.LabelField("Origin", EditorStyles.miniLabel);
                    Vector3 origin = workPlane.Origin;
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUIUtility.labelWidth = 14;
                        float newX = EditorGUILayout.FloatField("X", origin.x);
                        float newY = EditorGUILayout.FloatField("Y", origin.y);
                        float newZ = EditorGUILayout.FloatField("Z", origin.z);
                        EditorGUIUtility.labelWidth = 0;
                        Vector3 newOrigin = new Vector3(newX, newY, newZ);
                        if (newOrigin != origin) { workPlane.Origin = newOrigin; changed = true; changeDescription = "Change WorkPlane Origin"; }
                    }
                    EditorGUILayout.EndHorizontal();

                    if (GUILayout.Button("⎔ From Selection", GUILayout.Height(18))) OnFromSelectionClicked?.Invoke();

                    EditorGUILayout.Space(2);
                    bool isCustomMode = workPlane.Mode == WorkPlaneMode.Custom;

                    EditorGUILayout.LabelField("Axis U", EditorStyles.miniLabel);
                    if (isCustomMode)
                    {
                        Vector3 axisU = workPlane.AxisU;
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUIUtility.labelWidth = 14;
                            float uX = EditorGUILayout.FloatField("X", axisU.x);
                            float uY = EditorGUILayout.FloatField("Y", axisU.y);
                            float uZ = EditorGUILayout.FloatField("Z", axisU.z);
                            EditorGUIUtility.labelWidth = 0;
                            Vector3 newAxisU = new Vector3(uX, uY, uZ);
                            if (newAxisU != axisU) { workPlane.AxisU = newAxisU; changed = true; changeDescription = "Change WorkPlane Axis U"; }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    else { Vector3 u = workPlane.AxisU; EditorGUILayout.LabelField($"  ({u.x:F2}, {u.y:F2}, {u.z:F2})", EditorStyles.miniLabel); }

                    EditorGUILayout.LabelField("Axis V", EditorStyles.miniLabel);
                    if (isCustomMode)
                    {
                        Vector3 axisV = workPlane.AxisV;
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUIUtility.labelWidth = 14;
                            float vX = EditorGUILayout.FloatField("X", axisV.x);
                            float vY = EditorGUILayout.FloatField("Y", axisV.y);
                            float vZ = EditorGUILayout.FloatField("Z", axisV.z);
                            EditorGUIUtility.labelWidth = 0;
                            Vector3 newAxisV = new Vector3(vX, vY, vZ);
                            if (newAxisV != axisV) { workPlane.AxisV = newAxisV; changed = true; changeDescription = "Change WorkPlane Axis V"; }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    else { Vector3 v = workPlane.AxisV; EditorGUILayout.LabelField($"  ({v.x:F2}, {v.y:F2}, {v.z:F2})", EditorStyles.miniLabel); }

                    Vector3 n = workPlane.Normal;
                    EditorGUILayout.LabelField("Normal", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"  ({n.x:F2}, {n.y:F2}, {n.z:F2})", EditorStyles.miniLabel);

                    if (isCustomMode && GUILayout.Button("Orthonormalize", GUILayout.Height(18)))
                    { workPlane.Orthonormalize(); changed = true; changeDescription = "Orthonormalize WorkPlane"; }

                    EditorGUILayout.Space(2);
                    bool newAutoUpdate = EditorGUILayout.ToggleLeft("Auto-update origin", workPlane.AutoUpdateOriginOnSelection);
                    if (newAutoUpdate != workPlane.AutoUpdateOriginOnSelection) { workPlane.AutoUpdateOriginOnSelection = newAutoUpdate; changed = true; changeDescription = newAutoUpdate ? "Enable Auto-update Origin" : "Disable Auto-update Origin"; }

                    if (workPlane.Mode == WorkPlaneMode.CameraParallel)
                    {
                        bool newLockOrientation = EditorGUILayout.ToggleLeft("Lock orientation", workPlane.LockOrientation);
                        if (newLockOrientation != workPlane.LockOrientation) { workPlane.LockOrientation = newLockOrientation; changed = true; changeDescription = newLockOrientation ? "Lock WorkPlane Orientation" : "Unlock WorkPlane Orientation"; }
                    }
                }
                EditorGUI.EndDisabledGroup();
            }

            if (changed)
            {
                WorkPlaneSnapshot after = workPlane.CreateSnapshot();
                if (before.IsDifferentFrom(after)) OnChanged?.Invoke(before, after, changeDescription);
            }
            return changed;
        }

        private static void InitStyles()
        {
            if (_compactLabelStyle == null)
                _compactLabelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        }
    }
}
