// Tools/SelectTool.cs
// 選択専用ツール
// IToolSettings対応版（設定なし）

using UnityEditor;
using UnityEngine;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 選択専用ツール（移動なし）
    /// </summary>
    public partial class SelectTool : IEditTool
    {
        public string Name => "Select";
        public string DisplayName => "Select";
        //public ToolCategory Category => ToolCategory.Selection;

        /// <summary>
        /// 設定なし（nullを返す）
        /// </summary>
        public IToolSettings Settings => null;

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
            EditorGUILayout.LabelField(T("ClickToSelect"), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(T("ShiftClick"), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(T("DragSelect"), EditorStyles.miniLabel);
        }

        public void OnActivate(ToolContext ctx) { }
        public void OnDeactivate(ToolContext ctx) { }
        public void Reset() { }
    }
}
