// Assets/Editor/Poly_Ling/Tools/Selection/AdvancedSelectTool.cs
// 特殊選択ツール - IToolSettings対応、モード別分離版

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;
using static Poly_Ling.Gizmo.GLGizmoDrawer;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// 特殊選択ツール
    /// </summary>
    public partial class AdvancedSelectTool : IEditTool
    {
        public string Name => "SelectAdvanced";//"Sel+";
        public string DisplayName => "SelectAdvanced";//"Sel+";
        //public ToolCategory Category => ToolCategory.Selection; 

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private AdvancedSelectSettings _settings = new AdvancedSelectSettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        private AdvancedSelectMode Mode
        {
            get => _settings.Mode;
            set => _settings.Mode = value;
        }

        private float EdgeLoopThreshold
        {
            get => _settings.EdgeLoopThreshold;
            set => _settings.EdgeLoopThreshold = value;
        }

        private bool AddToSelection
        {
            get => _settings.AddToSelection;
            set => _settings.AddToSelection = value;
        }

        // ================================================================
        // モード別処理
        // ================================================================

        private readonly Dictionary<AdvancedSelectMode, IAdvancedSelectMode> _modes;
        private AdvancedSelectContext _ctx = new AdvancedSelectContext();

        /// <summary>
        /// 現在のプレビューコンテキストを返す。
        /// Player のオーバーレイ描画用。
        /// </summary>
        public AdvancedSelectContext GetPreviewContext() => _ctx;

        // モード選択用
        private static readonly AdvancedSelectMode[] ModeValues = {
            AdvancedSelectMode.Connected,
            AdvancedSelectMode.Belt,
            AdvancedSelectMode.EdgeLoop,
            AdvancedSelectMode.ShortestPath
        };

        /// <summary>ローカライズされたモード名配列を取得</summary>
        private string[] GetLocalizedModeNames() => new string[] {
            T("Connected"), T("Belt"), T("EdgeLoop"), T("Shortest")
        };

        // ================================================================
        // コンストラクタ
        // ================================================================

        public AdvancedSelectTool()
        {
            _modes = new Dictionary<AdvancedSelectMode, IAdvancedSelectMode>
            {
                { AdvancedSelectMode.Connected, new ConnectedSelectMode() },
                { AdvancedSelectMode.Belt, new BeltSelectMode() },
                { AdvancedSelectMode.EdgeLoop, new EdgeLoopSelectMode() },
                { AdvancedSelectMode.ShortestPath, new ShortestPathSelectMode() }
            };
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.FirstSelectedMeshObject == null) return false;

            UpdateContext(ctx);

            if (_modes.TryGetValue(Mode, out var mode))
            {
                return mode.HandleClick(_ctx, mousePos, ctx.CurrentSelectMode);
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (ctx.FirstSelectedMeshObject == null) return false;

            UpdateContext(ctx);
            _ctx.ClearPreview();
            _ctx.ClearHover();

            if (_modes.TryGetValue(Mode, out var mode))
            {
                mode.UpdatePreview(_ctx, mousePos, ctx.CurrentSelectMode);
            }

            ctx.Repaint?.Invoke();
            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            return false;
        }

        /// <summary>IMGUI 削除済み。Player は UIToolkit オーバーレイを使用。UnityEditor_Handles 使用禁止。</summary>
        public void DrawGizmo(ToolContext ctx) { }

        public void OnActivate(ToolContext ctx)
        {
            Reset();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
        }

        public void Reset()
        {
            _ctx.ClearPreview();
            _ctx.ClearHover();
            ResetAllModes();
        }

        /// <summary>
        /// ShortestPath モードで登録されている始点頂点インデックスを返す。
        /// 未登録の場合は -1。
        /// エディタ版 ShortestPathSelectMode.DrawModeSettingsUI() の始点表示に対応。
        /// </summary>
        public int GetShortestPathFirstVertex()
        {
            if (_modes.TryGetValue(AdvancedSelectMode.ShortestPath, out var mode) &&
                mode is ShortestPathSelectMode sp)
                return sp.FirstVertex;
            return -1;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void UpdateContext(ToolContext ctx)
        {
            _ctx.ToolCtx = ctx;
            _ctx.AddToSelection = AddToSelection;
            _ctx.EdgeLoopThreshold = EdgeLoopThreshold;
        }

        private void ResetAllModes()
        {
            foreach (var mode in _modes.Values)
            {
                mode.Reset();
            }
        }

        private void DrawFacePreview(ToolContext ctx, int faceIdx, Color color)
        {
            if (faceIdx < 0 || faceIdx >= ctx.FirstSelectedMeshObject.FaceCount) return;
            var face = ctx.FirstSelectedMeshObject.Faces[faceIdx];
            if (face.VertexCount < 3) return;

            // UnityEditor_Handles 削除済み
            for (int i = 0; i < face.VertexCount; i++)
            {
                int v1 = face.VertexIndices[i];
                int v2 = face.VertexIndices[(i + 1) % face.VertexCount];
                if (v1 < 0 || v1 >= ctx.FirstSelectedMeshObject.VertexCount) continue;
                if (v2 < 0 || v2 >= ctx.FirstSelectedMeshObject.VertexCount) continue;
                Vector2 sp1 = ctx.WorldToScreen(ctx.FirstSelectedMeshObject.Vertices[v1].Position);
                Vector2 sp2 = ctx.WorldToScreen(ctx.FirstSelectedMeshObject.Vertices[v2].Position);
                // UnityEditor_Handles 削除済み
            }
        }

        private void DrawLinePreview(ToolContext ctx, int lineIdx)
        {
            if (lineIdx < 0 || lineIdx >= ctx.FirstSelectedMeshObject.FaceCount) return;
            var face = ctx.FirstSelectedMeshObject.Faces[lineIdx];
            if (face.VertexCount != 2) return;

            int v1 = face.VertexIndices[0];
            int v2 = face.VertexIndices[1];
            if (v1 < 0 || v1 >= ctx.FirstSelectedMeshObject.VertexCount) return;
            if (v2 < 0 || v2 >= ctx.FirstSelectedMeshObject.VertexCount) return;

            Vector2 sp1 = ctx.WorldToScreen(ctx.FirstSelectedMeshObject.Vertices[v1].Position);
            Vector2 sp2 = ctx.WorldToScreen(ctx.FirstSelectedMeshObject.Vertices[v2].Position);
            // UnityEditor_Handles 削除済み
        }
    }
}
