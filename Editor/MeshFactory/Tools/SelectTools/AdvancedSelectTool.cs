// Assets/Editor/MeshFactory/Tools/Selection/AdvancedSelectTool.cs
// 特殊選択ツール - IToolSettings対応、モード別分離版

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Selection;
using static MeshFactory.Gizmo.GLGizmoDrawer;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 特殊選択ツール
    /// </summary>
    public class AdvancedSelectTool : IEditTool
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

        // モード選択用
        private static readonly string[] ModeNames = { "Connected", "Belt", "EdgeLoop", "Shortest" };
        private static readonly AdvancedSelectMode[] ModeValues = {
            AdvancedSelectMode.Connected,
            AdvancedSelectMode.Belt,
            AdvancedSelectMode.EdgeLoop,
            AdvancedSelectMode.ShortestPath
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
            if (ctx.MeshData == null) return false;

            UpdateContext(ctx);

            if (_modes.TryGetValue(Mode, out var mode))
            {
                return mode.HandleClick(_ctx, mousePos, ctx.CurrentSelectMode);
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (ctx.MeshData == null) return false;

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

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.MeshData == null) return;

            UnityEditor_Handles.BeginGUI();

            Color previewColor = AddToSelection ? new Color(0, 1, 0, 0.7f) : new Color(1, 0, 0, 0.7f);

            // プレビュー頂点を描画
            GUI.color = previewColor;
            foreach (int vIdx in _ctx.PreviewVertices)
            {
                if (vIdx < 0 || vIdx >= ctx.MeshData.VertexCount) continue;
                Vector2 sp = ctx.WorldToScreen(ctx.MeshData.Vertices[vIdx].Position);
                float size = 8f;
                GUI.DrawTexture(new Rect(sp.x - size / 2, sp.y - size / 2, size, size), EditorGUIUtility.whiteTexture);
            }

            // プレビューエッジを描画
            UnityEditor_Handles.color = previewColor;
            foreach (var edge in _ctx.PreviewEdges)
            {
                if (edge.V1 < 0 || edge.V1 >= ctx.MeshData.VertexCount) continue;
                if (edge.V2 < 0 || edge.V2 >= ctx.MeshData.VertexCount) continue;
                Vector2 sp1 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V1].Position);
                Vector2 sp2 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V2].Position);
                UnityEditor_Handles.DrawAAPolyLine(3f, sp1, sp2);
            }

            // プレビュー面を描画
            foreach (int faceIdx in _ctx.PreviewFaces)
            {
                DrawFacePreview(ctx, faceIdx, previewColor);
            }

            // プレビューラインを描画
            UnityEditor_Handles.color = new Color(previewColor.r, previewColor.g, previewColor.b, 0.9f);
            foreach (int lineIdx in _ctx.PreviewLines)
            {
                DrawLinePreview(ctx, lineIdx);
            }

            // 最短パスのプレビュー
            if (Mode == AdvancedSelectMode.ShortestPath && _ctx.PreviewPath.Count > 1)
            {
                UnityEditor_Handles.color = previewColor;
                for (int i = 0; i < _ctx.PreviewPath.Count - 1; i++)
                {
                    int v1 = _ctx.PreviewPath[i];
                    int v2 = _ctx.PreviewPath[i + 1];
                    if (v1 < 0 || v1 >= ctx.MeshData.VertexCount) continue;
                    if (v2 < 0 || v2 >= ctx.MeshData.VertexCount) continue;

                    Vector2 sp1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);
                    Vector2 sp2 = ctx.WorldToScreen(ctx.MeshData.Vertices[v2].Position);
                    UnityEditor_Handles.DrawAAPolyLine(3f, sp1, sp2);
                }

                GUI.color = previewColor;
                foreach (int vIdx in _ctx.PreviewPath)
                {
                    if (vIdx < 0 || vIdx >= ctx.MeshData.VertexCount) continue;
                    Vector2 sp = ctx.WorldToScreen(ctx.MeshData.Vertices[vIdx].Position);
                    float size = 8f;
                    GUI.DrawTexture(new Rect(sp.x - size / 2, sp.y - size / 2, size, size), EditorGUIUtility.whiteTexture);
                }
            }

            // ShortestPath: 1つ目の頂点をハイライト
            if (Mode == AdvancedSelectMode.ShortestPath)
            {
                var shortestMode = _modes[AdvancedSelectMode.ShortestPath] as ShortestPathSelectMode;
                if (shortestMode != null && shortestMode.FirstVertex >= 0 && shortestMode.FirstVertex < ctx.MeshData.VertexCount)
                {
                    GUI.color = Color.yellow;
                    Vector2 sp = ctx.WorldToScreen(ctx.MeshData.Vertices[shortestMode.FirstVertex].Position);
                    float size = 12f;
                    GUI.DrawTexture(new Rect(sp.x - size / 2, sp.y - size / 2, size, size), EditorGUIUtility.whiteTexture);
                }
            }

            // ホバー中のエッジをハイライト
            if (_ctx.HoveredEdgePair.HasValue)
            {
                UnityEditor_Handles.color = Color.cyan;
                var edge = _ctx.HoveredEdgePair.Value;
                if (edge.V1 >= 0 && edge.V1 < ctx.MeshData.VertexCount &&
                    edge.V2 >= 0 && edge.V2 < ctx.MeshData.VertexCount)
                {
                    Vector2 sp1 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V1].Position);
                    Vector2 sp2 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V2].Position);
                    UnityEditor_Handles.DrawAAPolyLine(4f, sp1, sp2);
                }
            }

            // ホバー中の面をハイライト
            if (_ctx.HoveredFace >= 0)
            {
                DrawFacePreview(ctx, _ctx.HoveredFace, Color.cyan * 0.5f);
            }

            GUI.color = Color.white;
            UnityEditor_Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Advanced Select Tool", EditorStyles.boldLabel);

            // モード選択
            int currentIndex = Array.IndexOf(ModeValues, Mode);
            EditorGUI.BeginChangeCheck();
            int newIndex = GUILayout.Toolbar(currentIndex, ModeNames);
            if (EditorGUI.EndChangeCheck() && newIndex != currentIndex)
            {
                Mode = ModeValues[newIndex];
                ResetAllModes();
            }

            EditorGUILayout.Space(5);

            // モード別設定
            if (_modes.TryGetValue(Mode, out var mode))
            {
                mode.DrawModeSettingsUI();
            }

            // EdgeLoopモードの追加設定
            if (Mode == AdvancedSelectMode.EdgeLoop)
            {
                EdgeLoopThreshold = EditorGUILayout.Slider("Direction Threshold", EdgeLoopThreshold, 0f, 1f);
            }

            EditorGUILayout.Space(5);

            // 追加/削除モード
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Action:", GUILayout.Width(50));
            if (GUILayout.Toggle(AddToSelection, "Add", EditorStyles.miniButtonLeft))
                AddToSelection = true;
            if (GUILayout.Toggle(!AddToSelection, "Remove", EditorStyles.miniButtonRight))
                AddToSelection = false;
            EditorGUILayout.EndHorizontal();
        }

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
            if (faceIdx < 0 || faceIdx >= ctx.MeshData.FaceCount) return;
            var face = ctx.MeshData.Faces[faceIdx];
            if (face.VertexCount < 3) return;

            UnityEditor_Handles.color = color;
            for (int i = 0; i < face.VertexCount; i++)
            {
                int v1 = face.VertexIndices[i];
                int v2 = face.VertexIndices[(i + 1) % face.VertexCount];
                if (v1 < 0 || v1 >= ctx.MeshData.VertexCount) continue;
                if (v2 < 0 || v2 >= ctx.MeshData.VertexCount) continue;
                Vector2 sp1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);
                Vector2 sp2 = ctx.WorldToScreen(ctx.MeshData.Vertices[v2].Position);
                UnityEditor_Handles.DrawAAPolyLine(2f, sp1, sp2);
            }
        }

        private void DrawLinePreview(ToolContext ctx, int lineIdx)
        {
            if (lineIdx < 0 || lineIdx >= ctx.MeshData.FaceCount) return;
            var face = ctx.MeshData.Faces[lineIdx];
            if (face.VertexCount != 2) return;

            int v1 = face.VertexIndices[0];
            int v2 = face.VertexIndices[1];
            if (v1 < 0 || v1 >= ctx.MeshData.VertexCount) return;
            if (v2 < 0 || v2 >= ctx.MeshData.VertexCount) return;

            Vector2 sp1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);
            Vector2 sp2 = ctx.WorldToScreen(ctx.MeshData.Vertices[v2].Position);
            UnityEditor_Handles.DrawAAPolyLine(4f, sp1, sp2);
        }
    }
}
