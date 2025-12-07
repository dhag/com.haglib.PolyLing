// Tools/SelectTool.cs
// 選択専用ツール

using UnityEditor;
using UnityEngine;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 選択専用ツール（移動なし）
    /// </summary>
    public class SelectTool : IEditTool
    {
        public string Name => "Select";

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            // 選択処理はメイン側で共通処理
            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            // 矩形選択はメイン側で処理
            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            return false;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            // 選択ツールはギズモなし
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Click to select vertices", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Shift+Click: Add to selection", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Drag: Box select", EditorStyles.miniLabel);
        }

        public void OnActivate(ToolContext ctx) { }
        public void OnDeactivate(ToolContext ctx) { }
        public void Reset() { }
    }
}
