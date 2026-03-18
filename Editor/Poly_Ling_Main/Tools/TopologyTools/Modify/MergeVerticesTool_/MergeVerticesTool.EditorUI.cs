// MergeVerticesTool.EditorUI.cs
// MergeVerticesToolのEditor専用設定UI
// IEditorToolUI実装 - Runtime環境では存在しない

#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Poly_Ling.Tools
{
    public partial class MergeVerticesTool : IEditorToolUI
    {
        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField(T("Title"), EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(T("Help"), MessageType.Info);

            EditorGUILayout.Space(5);

            // しきい値
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(T("Threshold"), GUILayout.Width(70));
            EditorGUI.BeginChangeCheck();
            Threshold = EditorGUILayout.FloatField(Threshold, GUILayout.Width(80));
            if (EditorGUI.EndChangeCheck())
            {
                Threshold = Mathf.Max(0.0001f, Threshold);
                _previewDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            // プリセット
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("0.001", EditorStyles.miniButtonLeft)) { Threshold = 0.001f; _previewDirty = true; }
            if (GUILayout.Button("0.01", EditorStyles.miniButtonMid)) { Threshold = 0.01f; _previewDirty = true; }
            if (GUILayout.Button("0.1", EditorStyles.miniButtonRight)) { Threshold = 0.1f; _previewDirty = true; }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // プレビュー表示切替
            ShowPreview = EditorGUILayout.Toggle(T("ShowPreview"), ShowPreview);

            EditorGUILayout.Space(5);

            // プレビュー情報
            if (_preview.GroupCount > 0)
            {
                EditorGUILayout.LabelField(T("Groups", _preview.GroupCount), EditorStyles.miniLabel);
                EditorGUILayout.LabelField(T("VerticesToRemove", _preview.TotalVerticesToMerge), EditorStyles.miniLabel);

                // グループ詳細
                EditorGUILayout.Space(3);
                for (int i = 0; i < Mathf.Min(_preview.Groups.Count, 5); i++)
                {
                    var group = _preview.Groups[i];
                    EditorGUILayout.LabelField(
                        $"  [{i}] {group.Count} verts: {string.Join(",", group.Take(8))}{(group.Count > 8 ? "..." : "")}",
                        EditorStyles.miniLabel);
                }
                if (_preview.Groups.Count > 5)
                {
                    EditorGUILayout.LabelField(T("MoreGroups", _preview.Groups.Count - 5), EditorStyles.miniLabel);
                }
            }
            else
            {
                EditorGUILayout.LabelField(T("NoMerge"), EditorStyles.miniLabel);
            }

            EditorGUILayout.Space(10);

            // マージボタン
            EditorGUI.BeginDisabledGroup(_preview.GroupCount == 0);
            if (GUILayout.Button(T("Merge"), GUILayout.Height(30)))
            {
                _pendingMerge = true;
            }
            EditorGUI.EndDisabledGroup();
        }
    }
}
#endif
