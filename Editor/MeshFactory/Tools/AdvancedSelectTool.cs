// Tools/AdvancedSelectTool.cs
// 特殊選択ツール - 接続領域、ベルト、連続エッジ、最短ルート
// SelectionFlags対応版（複数同時出力）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using MeshFactory.Selection;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 特殊選択ツールのモード
    /// </summary>
    public enum AdvancedSelectMode
    {
        /// <summary>接続領域選択</summary>
        Connected,
        /// <summary>ベルト選択</summary>
        Belt,
        /// <summary>連続エッジ選択</summary>
        EdgeLoop,
        /// <summary>最短ルート選択</summary>
        ShortestPath
    }

    /// <summary>
    /// 特殊選択ツール
    /// </summary>
    public class AdvancedSelectTool : IEditTool
    {
        public string Name => "Advanced Select";

        // === 設定 ===
        private AdvancedSelectMode _mode = AdvancedSelectMode.Connected;
        private float _edgeLoopThreshold = 0.7f;  // 連続エッジの内積しきい値
        private bool _addToSelection = true;      // true: 追加, false: 削除

        // === 状態 ===
        private int _firstVertex = -1;            // ShortestPath用: 1つ目の頂点
        private int _hoveredVertex = -1;          // ホバー中の頂点
        private VertexPair? _hoveredEdgePair = null; // ホバー中のエッジ
        private int _hoveredFace = -1;            // ホバー中の面
        private int _hoveredLine = -1;            // ホバー中のライン

        // === プレビュー ===
        private List<int> _previewVertices = new List<int>();
        private List<VertexPair> _previewEdges = new List<VertexPair>();
        private List<int> _previewFaces = new List<int>();
        private List<int> _previewLines = new List<int>();
        private List<int> _previewPath = new List<int>();

        // === 定数 ===
        private const float VERTEX_CLICK_THRESHOLD = 15f;
        private const float EDGE_CLICK_THRESHOLD = 10f;

        // === モード選択用 ===
        private static readonly string[] ModeNames = { "Connected", "Belt", "EdgeLoop", "Shortest" };
        private static readonly AdvancedSelectMode[] ModeValues = {
            AdvancedSelectMode.Connected,
            AdvancedSelectMode.Belt,
            AdvancedSelectMode.EdgeLoop,
            AdvancedSelectMode.ShortestPath
        };

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshData == null) return false;

            var selectMode = ctx.CurrentSelectMode;

            switch (_mode)
            {
                case AdvancedSelectMode.Connected:
                    return HandleConnectedClick(ctx, mousePos, selectMode);

                case AdvancedSelectMode.Belt:
                    return HandleBeltClick(ctx, mousePos, selectMode);

                case AdvancedSelectMode.EdgeLoop:
                    return HandleEdgeLoopClick(ctx, mousePos, selectMode);

                case AdvancedSelectMode.ShortestPath:
                    return HandleShortestPathClick(ctx, mousePos, selectMode);
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (ctx.MeshData == null) return false;

            ClearPreview();

            var selectMode = ctx.CurrentSelectMode;

            switch (_mode)
            {
                case AdvancedSelectMode.Connected:
                    UpdateConnectedPreview(ctx, mousePos, selectMode);
                    break;

                case AdvancedSelectMode.Belt:
                    UpdateBeltPreview(ctx, mousePos, selectMode);
                    break;

                case AdvancedSelectMode.EdgeLoop:
                    UpdateEdgeLoopPreview(ctx, mousePos, selectMode);
                    break;

                case AdvancedSelectMode.ShortestPath:
                    UpdateShortestPathPreview(ctx, mousePos, selectMode);
                    break;
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

            Handles.BeginGUI();

            Color previewColor = _addToSelection ? new Color(0, 1, 0, 0.7f) : new Color(1, 0, 0, 0.7f);

            // プレビュー頂点を描画
            GUI.color = previewColor;
            foreach (int vIdx in _previewVertices)
            {
                if (vIdx < 0 || vIdx >= ctx.MeshData.VertexCount) continue;
                Vector2 sp = ctx.WorldToScreen(ctx.MeshData.Vertices[vIdx].Position);
                float size = 8f;
                GUI.DrawTexture(new Rect(sp.x - size / 2, sp.y - size / 2, size, size), EditorGUIUtility.whiteTexture);
            }

            // プレビューエッジを描画
            Handles.color = previewColor;
            foreach (var edge in _previewEdges)
            {
                if (edge.V1 < 0 || edge.V1 >= ctx.MeshData.VertexCount) continue;
                if (edge.V2 < 0 || edge.V2 >= ctx.MeshData.VertexCount) continue;
                Vector2 sp1 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V1].Position);
                Vector2 sp2 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V2].Position);
                Handles.DrawAAPolyLine(3f, sp1, sp2);
            }

            // プレビュー面を描画
            foreach (int faceIdx in _previewFaces)
            {
                DrawFacePreview(ctx, faceIdx, previewColor);
            }

            // プレビューラインを描画
            Handles.color = new Color(previewColor.r, previewColor.g, previewColor.b, 0.9f);
            foreach (int lineIdx in _previewLines)
            {
                DrawLinePreview(ctx, lineIdx);
            }

            // 最短パスのプレビュー
            if (_mode == AdvancedSelectMode.ShortestPath && _previewPath.Count > 1)
            {
                Handles.color = previewColor;
                for (int i = 0; i < _previewPath.Count - 1; i++)
                {
                    int v1 = _previewPath[i];
                    int v2 = _previewPath[i + 1];
                    if (v1 < 0 || v1 >= ctx.MeshData.VertexCount) continue;
                    if (v2 < 0 || v2 >= ctx.MeshData.VertexCount) continue;

                    Vector2 sp1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);
                    Vector2 sp2 = ctx.WorldToScreen(ctx.MeshData.Vertices[v2].Position);
                    Handles.DrawAAPolyLine(3f, sp1, sp2);
                }

                GUI.color = previewColor;
                foreach (int vIdx in _previewPath)
                {
                    if (vIdx < 0 || vIdx >= ctx.MeshData.VertexCount) continue;
                    Vector2 sp = ctx.WorldToScreen(ctx.MeshData.Vertices[vIdx].Position);
                    float size = 8f;
                    GUI.DrawTexture(new Rect(sp.x - size / 2, sp.y - size / 2, size, size), EditorGUIUtility.whiteTexture);
                }
            }

            // ShortestPath: 1つ目の頂点をハイライト
            if (_mode == AdvancedSelectMode.ShortestPath && _firstVertex >= 0 && _firstVertex < ctx.MeshData.VertexCount)
            {
                GUI.color = Color.yellow;
                Vector2 sp = ctx.WorldToScreen(ctx.MeshData.Vertices[_firstVertex].Position);
                float size = 12f;
                GUI.DrawTexture(new Rect(sp.x - size / 2, sp.y - size / 2, size, size), EditorGUIUtility.whiteTexture);
            }

            // ホバー中のエッジをハイライト
            if (_hoveredEdgePair.HasValue)
            {
                Handles.color = Color.cyan;
                var edge = _hoveredEdgePair.Value;
                if (edge.V1 >= 0 && edge.V1 < ctx.MeshData.VertexCount &&
                    edge.V2 >= 0 && edge.V2 < ctx.MeshData.VertexCount)
                {
                    Vector2 sp1 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V1].Position);
                    Vector2 sp2 = ctx.WorldToScreen(ctx.MeshData.Vertices[edge.V2].Position);
                    Handles.DrawAAPolyLine(4f, sp1, sp2);
                }
            }

            // ホバー中の面をハイライト
            if (_hoveredFace >= 0)
            {
                DrawFacePreview(ctx, _hoveredFace, Color.cyan * 0.5f);
            }

            GUI.color = Color.white;
            Handles.EndGUI();
        }

        private void DrawFacePreview(ToolContext ctx, int faceIdx, Color color)
        {
            if (faceIdx < 0 || faceIdx >= ctx.MeshData.FaceCount) return;
            var face = ctx.MeshData.Faces[faceIdx];
            if (face.VertexCount < 3) return;

            Handles.color = color;
            for (int i = 0; i < face.VertexCount; i++)
            {
                int v1 = face.VertexIndices[i];
                int v2 = face.VertexIndices[(i + 1) % face.VertexCount];
                if (v1 < 0 || v1 >= ctx.MeshData.VertexCount) continue;
                if (v2 < 0 || v2 >= ctx.MeshData.VertexCount) continue;
                Vector2 sp1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);
                Vector2 sp2 = ctx.WorldToScreen(ctx.MeshData.Vertices[v2].Position);
                Handles.DrawAAPolyLine(2f, sp1, sp2);
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
            Handles.DrawAAPolyLine(4f, sp1, sp2);
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Advanced Select Tool", EditorStyles.boldLabel);

            // モード選択
            int currentIndex = Array.IndexOf(ModeValues, _mode);
            EditorGUI.BeginChangeCheck();
            int newIndex = GUILayout.Toolbar(currentIndex, ModeNames);
            if (EditorGUI.EndChangeCheck() && newIndex != currentIndex)
            {
                _mode = ModeValues[newIndex];
                Reset();
            }

            EditorGUILayout.Space(5);

            // モード別説明
            switch (_mode)
            {
                case AdvancedSelectMode.Connected:
                    EditorGUILayout.HelpBox(
                        "Click element to select all connected.\n" +
                        "Output: All enabled modes (V/E/F/L)",
                        MessageType.Info);
                    break;

                case AdvancedSelectMode.Belt:
                    EditorGUILayout.HelpBox(
                        "Click edge to select quad belt.\n" +
                        "• Vertex: belt vertices\n" +
                        "• Edge: ladder rungs (横方向)\n" +
                        "• Face: belt faces",
                        MessageType.Info);
                    break;

                case AdvancedSelectMode.EdgeLoop:
                    EditorGUILayout.HelpBox(
                        "Click edge to select edge loop.\n" +
                        "• Vertex: loop vertices\n" +
                        "• Edge: loop edges\n" +
                        "• Face: adjacent faces",
                        MessageType.Info);
                    _edgeLoopThreshold = EditorGUILayout.Slider("Direction Threshold", _edgeLoopThreshold, 0f, 1f);
                    break;

                case AdvancedSelectMode.ShortestPath:
                    EditorGUILayout.HelpBox(
                        "Click two vertices for shortest path.\n" +
                        "• Vertex: path vertices\n" +
                        "• Edge: path edges\n" +
                        "• Face: adjacent faces",
                        MessageType.Info);
                    if (_firstVertex >= 0)
                    {
                        EditorGUILayout.LabelField($"First vertex: {_firstVertex}");
                        if (GUILayout.Button("Clear First Point"))
                        {
                            _firstVertex = -1;
                        }
                    }
                    break;
            }

            EditorGUILayout.Space(5);

            // 追加/削除モード
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Action:", GUILayout.Width(50));
            if (GUILayout.Toggle(_addToSelection, "Add", EditorStyles.miniButtonLeft))
                _addToSelection = true;
            if (GUILayout.Toggle(!_addToSelection, "Remove", EditorStyles.miniButtonRight))
                _addToSelection = false;
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
            _firstVertex = -1;
            _hoveredVertex = -1;
            _hoveredEdgePair = null;
            _hoveredFace = -1;
            _hoveredLine = -1;
            ClearPreview();
        }

        private void ClearPreview()
        {
            _previewVertices.Clear();
            _previewEdges.Clear();
            _previewFaces.Clear();
            _previewLines.Clear();
            _previewPath.Clear();
        }

        // ================================================================
        // Connected モード
        // 入力: Flags優先順位で1つの要素
        // 出力: Flagsに応じて関連する全種類
        // ================================================================

        private bool HandleConnectedClick(ToolContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            // 入力: Flags優先順位でヒットテスト（Vertex > Edge > Face > Line）
            if (selectMode.Has(MeshSelectMode.Vertex))
            {
                int hitVertex = FindNearestVertex(ctx, mousePos);
                if (hitVertex >= 0)
                {
                    ApplyConnectedFromVertex(ctx, hitVertex, selectMode);
                    return true;
                }
            }

            if (selectMode.Has(MeshSelectMode.Edge))
            {
                var hitEdge = FindNearestEdgePair(ctx, mousePos);
                if (hitEdge.HasValue)
                {
                    ApplyConnectedFromEdge(ctx, hitEdge.Value, selectMode);
                    return true;
                }
            }

            if (selectMode.Has(MeshSelectMode.Face))
            {
                int hitFace = FindNearestFace(ctx, mousePos);
                if (hitFace >= 0)
                {
                    ApplyConnectedFromFace(ctx, hitFace, selectMode);
                    return true;
                }
            }

            if (selectMode.Has(MeshSelectMode.Line))
            {
                int hitLine = FindNearestLine(ctx, mousePos);
                if (hitLine >= 0)
                {
                    ApplyConnectedFromLine(ctx, hitLine, selectMode);
                    return true;
                }
            }

            return false;
        }

        private void ApplyConnectedFromVertex(ToolContext ctx, int startVertex, MeshSelectMode selectMode)
        {
            var connectedVerts = GetConnectedVertices(ctx.MeshData, startVertex);

            if (selectMode.Has(MeshSelectMode.Vertex))
                ApplyVertexSelection(ctx, connectedVerts);

            if (selectMode.Has(MeshSelectMode.Edge))
            {
                var edges = GetEdgesFromVertices(ctx, connectedVerts);
                ApplyEdgeSelection(ctx, edges);
            }

            if (selectMode.Has(MeshSelectMode.Face))
            {
                var faces = GetFacesFromVertices(ctx, connectedVerts);
                ApplyFaceSelection(ctx, faces);
            }

            if (selectMode.Has(MeshSelectMode.Line))
            {
                var lines = GetLinesFromVertices(ctx, connectedVerts);
                ApplyLineSelection(ctx, lines);
            }
        }

        private void ApplyConnectedFromEdge(ToolContext ctx, VertexPair startEdge, MeshSelectMode selectMode)
        {
            var connectedEdges = GetConnectedEdges(ctx, startEdge);
            var connectedVerts = new HashSet<int>();
            foreach (var e in connectedEdges)
            {
                connectedVerts.Add(e.V1);
                connectedVerts.Add(e.V2);
            }

            if (selectMode.Has(MeshSelectMode.Vertex))
                ApplyVertexSelection(ctx, connectedVerts.ToList());

            if (selectMode.Has(MeshSelectMode.Edge))
                ApplyEdgeSelection(ctx, connectedEdges);

            if (selectMode.Has(MeshSelectMode.Face))
            {
                var faces = GetFacesFromVertices(ctx, connectedVerts.ToList());
                ApplyFaceSelection(ctx, faces);
            }
        }

        private void ApplyConnectedFromFace(ToolContext ctx, int startFace, MeshSelectMode selectMode)
        {
            var connectedFaces = GetConnectedFaces(ctx, startFace);
            var connectedVerts = new HashSet<int>();
            foreach (int fIdx in connectedFaces)
            {
                foreach (int vIdx in ctx.MeshData.Faces[fIdx].VertexIndices)
                    connectedVerts.Add(vIdx);
            }

            if (selectMode.Has(MeshSelectMode.Vertex))
                ApplyVertexSelection(ctx, connectedVerts.ToList());

            if (selectMode.Has(MeshSelectMode.Edge))
            {
                var edges = GetEdgesFromFaces(ctx, connectedFaces);
                ApplyEdgeSelection(ctx, edges);
            }

            if (selectMode.Has(MeshSelectMode.Face))
                ApplyFaceSelection(ctx, connectedFaces);
        }

        private void ApplyConnectedFromLine(ToolContext ctx, int startLine, MeshSelectMode selectMode)
        {
            var connectedLines = GetConnectedLines(ctx, startLine);
            var connectedVerts = new HashSet<int>();
            foreach (int lIdx in connectedLines)
            {
                var face = ctx.MeshData.Faces[lIdx];
                if (face.VertexCount == 2)
                {
                    connectedVerts.Add(face.VertexIndices[0]);
                    connectedVerts.Add(face.VertexIndices[1]);
                }
            }

            if (selectMode.Has(MeshSelectMode.Vertex))
                ApplyVertexSelection(ctx, connectedVerts.ToList());

            if (selectMode.Has(MeshSelectMode.Line))
                ApplyLineSelection(ctx, connectedLines);
        }

        private void UpdateConnectedPreview(ToolContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            if (selectMode.Has(MeshSelectMode.Vertex))
            {
                _hoveredVertex = FindNearestVertex(ctx, mousePos);
                if (_hoveredVertex >= 0)
                {
                    var connectedVerts = GetConnectedVertices(ctx.MeshData, _hoveredVertex);
                    if (selectMode.Has(MeshSelectMode.Vertex))
                        _previewVertices = connectedVerts;
                    if (selectMode.Has(MeshSelectMode.Edge))
                        _previewEdges = GetEdgesFromVertices(ctx, connectedVerts);
                    if (selectMode.Has(MeshSelectMode.Face))
                        _previewFaces = GetFacesFromVertices(ctx, connectedVerts);
                    if (selectMode.Has(MeshSelectMode.Line))
                        _previewLines = GetLinesFromVertices(ctx, connectedVerts);
                    return;
                }
            }

            if (selectMode.Has(MeshSelectMode.Edge))
            {
                _hoveredEdgePair = FindNearestEdgePair(ctx, mousePos);
                if (_hoveredEdgePair.HasValue)
                {
                    var connectedEdges = GetConnectedEdges(ctx, _hoveredEdgePair.Value);
                    var connectedVerts = new HashSet<int>();
                    foreach (var e in connectedEdges) { connectedVerts.Add(e.V1); connectedVerts.Add(e.V2); }

                    if (selectMode.Has(MeshSelectMode.Vertex))
                        _previewVertices = connectedVerts.ToList();
                    if (selectMode.Has(MeshSelectMode.Edge))
                        _previewEdges = connectedEdges;
                    if (selectMode.Has(MeshSelectMode.Face))
                        _previewFaces = GetFacesFromVertices(ctx, connectedVerts.ToList());
                    return;
                }
            }

            if (selectMode.Has(MeshSelectMode.Face))
            {
                _hoveredFace = FindNearestFace(ctx, mousePos);
                if (_hoveredFace >= 0)
                {
                    var connectedFaces = GetConnectedFaces(ctx, _hoveredFace);
                    var connectedVerts = new HashSet<int>();
                    foreach (int fIdx in connectedFaces)
                        foreach (int vIdx in ctx.MeshData.Faces[fIdx].VertexIndices)
                            connectedVerts.Add(vIdx);

                    if (selectMode.Has(MeshSelectMode.Vertex))
                        _previewVertices = connectedVerts.ToList();
                    if (selectMode.Has(MeshSelectMode.Edge))
                        _previewEdges = GetEdgesFromFaces(ctx, connectedFaces);
                    if (selectMode.Has(MeshSelectMode.Face))
                        _previewFaces = connectedFaces;
                    return;
                }
            }

            if (selectMode.Has(MeshSelectMode.Line))
            {
                _hoveredLine = FindNearestLine(ctx, mousePos);
                if (_hoveredLine >= 0)
                {
                    var connectedLines = GetConnectedLines(ctx, _hoveredLine);
                    var connectedVerts = new HashSet<int>();
                    foreach (int lIdx in connectedLines)
                    {
                        var face = ctx.MeshData.Faces[lIdx];
                        if (face.VertexCount == 2)
                        {
                            connectedVerts.Add(face.VertexIndices[0]);
                            connectedVerts.Add(face.VertexIndices[1]);
                        }
                    }

                    if (selectMode.Has(MeshSelectMode.Vertex))
                        _previewVertices = connectedVerts.ToList();
                    if (selectMode.Has(MeshSelectMode.Line))
                        _previewLines = connectedLines;
                }
            }
        }

        // ================================================================
        // Belt モード
        // 入力: 常にエッジ
        // 出力: Flagsに応じて（Vertex, Edge=横方向, Face）
        // ================================================================

        private bool HandleBeltClick(ToolContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var edge = FindNearestEdgePair(ctx, mousePos);
            if (!edge.HasValue)
            {
                var legacyEdge = FindNearestEdgeLegacy(ctx, mousePos);
                if (legacyEdge.Item1 < 0) return false;
                edge = new VertexPair(legacyEdge.Item1, legacyEdge.Item2);
            }

            var beltData = GetBeltData(ctx.MeshData, edge.Value);

            if (selectMode.Has(MeshSelectMode.Vertex))
                ApplyVertexSelection(ctx, beltData.Vertices.ToList());

            if (selectMode.Has(MeshSelectMode.Edge))
                ApplyEdgeSelection(ctx, beltData.LadderEdges);

            if (selectMode.Has(MeshSelectMode.Face))
                ApplyFaceSelection(ctx, beltData.Faces);

            return true;
        }

        private void UpdateBeltPreview(ToolContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            _hoveredEdgePair = FindNearestEdgePair(ctx, mousePos);
            if (!_hoveredEdgePair.HasValue)
            {
                var legacyEdge = FindNearestEdgeLegacy(ctx, mousePos);
                if (legacyEdge.Item1 >= 0)
                    _hoveredEdgePair = new VertexPair(legacyEdge.Item1, legacyEdge.Item2);
            }

            if (!_hoveredEdgePair.HasValue) return;

            var beltData = GetBeltData(ctx.MeshData, _hoveredEdgePair.Value);

            if (selectMode.Has(MeshSelectMode.Vertex))
                _previewVertices = beltData.Vertices.ToList();
            if (selectMode.Has(MeshSelectMode.Edge))
                _previewEdges = beltData.LadderEdges;
            if (selectMode.Has(MeshSelectMode.Face))
                _previewFaces = beltData.Faces;
        }

        // ================================================================
        // EdgeLoop モード
        // 入力: 常にエッジ
        // 出力: Flagsに応じて（Vertex, Edge, Face=隣接面）
        // ================================================================

        private bool HandleEdgeLoopClick(ToolContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var edge = FindNearestEdgePair(ctx, mousePos);
            if (!edge.HasValue)
            {
                var legacyEdge = FindNearestEdgeLegacy(ctx, mousePos);
                if (legacyEdge.Item1 < 0) return false;
                edge = new VertexPair(legacyEdge.Item1, legacyEdge.Item2);
            }

            var loopEdges = GetEdgeLoopEdges(ctx.MeshData, edge.Value);

            if (selectMode.Has(MeshSelectMode.Vertex))
            {
                var verts = new HashSet<int>();
                foreach (var e in loopEdges) { verts.Add(e.V1); verts.Add(e.V2); }
                ApplyVertexSelection(ctx, verts.ToList());
            }

            if (selectMode.Has(MeshSelectMode.Edge))
                ApplyEdgeSelection(ctx, loopEdges);

            if (selectMode.Has(MeshSelectMode.Face))
            {
                var faces = GetAdjacentFaces(ctx, loopEdges);
                ApplyFaceSelection(ctx, faces);
            }

            return true;
        }

        private void UpdateEdgeLoopPreview(ToolContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            _hoveredEdgePair = FindNearestEdgePair(ctx, mousePos);
            if (!_hoveredEdgePair.HasValue)
            {
                var legacyEdge = FindNearestEdgeLegacy(ctx, mousePos);
                if (legacyEdge.Item1 >= 0)
                    _hoveredEdgePair = new VertexPair(legacyEdge.Item1, legacyEdge.Item2);
            }

            if (!_hoveredEdgePair.HasValue) return;

            var loopEdges = GetEdgeLoopEdges(ctx.MeshData, _hoveredEdgePair.Value);

            if (selectMode.Has(MeshSelectMode.Vertex))
            {
                var verts = new HashSet<int>();
                foreach (var e in loopEdges) { verts.Add(e.V1); verts.Add(e.V2); }
                _previewVertices = verts.ToList();
            }
            if (selectMode.Has(MeshSelectMode.Edge))
                _previewEdges = loopEdges;
            if (selectMode.Has(MeshSelectMode.Face))
                _previewFaces = GetAdjacentFaces(ctx, loopEdges);
        }

        // ================================================================
        // ShortestPath モード
        // 入力: 常に頂点
        // 出力: Flagsに応じて（Vertex, Edge, Face=隣接面）
        // ================================================================

        private bool HandleShortestPathClick(ToolContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            int vIdx = FindNearestVertex(ctx, mousePos);
            if (vIdx < 0) return false;

            if (_firstVertex < 0)
            {
                _firstVertex = vIdx;
                return true;
            }
            else
            {
                var path = GetShortestPath(ctx.MeshData, _firstVertex, vIdx);

                if (selectMode.Has(MeshSelectMode.Vertex))
                    ApplyVertexSelection(ctx, path);

                if (selectMode.Has(MeshSelectMode.Edge))
                {
                    var pathEdges = GetEdgesFromPath(path);
                    ApplyEdgeSelection(ctx, pathEdges);
                }

                if (selectMode.Has(MeshSelectMode.Face))
                {
                    var pathEdges = GetEdgesFromPath(path);
                    var faces = GetAdjacentFaces(ctx, pathEdges);
                    ApplyFaceSelection(ctx, faces);
                }

                _firstVertex = -1;
                return true;
            }
        }

        private void UpdateShortestPathPreview(ToolContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            _hoveredVertex = FindNearestVertex(ctx, mousePos);

            if (_firstVertex >= 0 && _hoveredVertex >= 0 && _firstVertex != _hoveredVertex)
            {
                _previewPath = GetShortestPath(ctx.MeshData, _firstVertex, _hoveredVertex);

                if (selectMode.Has(MeshSelectMode.Edge))
                    _previewEdges = GetEdgesFromPath(_previewPath);

                if (selectMode.Has(MeshSelectMode.Face))
                {
                    var pathEdges = GetEdgesFromPath(_previewPath);
                    _previewFaces = GetAdjacentFaces(ctx, pathEdges);
                }
            }
        }

        // ================================================================
        // 選択適用
        // ================================================================

        private void ApplyVertexSelection(ToolContext ctx, List<int> vertices)
        {
            var state = ctx.SelectionState;
            if (state != null)
            {
                foreach (int v in vertices)
                {
                    if (_addToSelection)
                        state.Vertices.Add(v);
                    else
                        state.Vertices.Remove(v);
                }
            }

            if (ctx.SelectedVertices != null)
            {
                foreach (int v in vertices)
                {
                    if (_addToSelection)
                        ctx.SelectedVertices.Add(v);
                    else
                        ctx.SelectedVertices.Remove(v);
                }
            }

            ctx.Repaint?.Invoke();
        }

        private void ApplyEdgeSelection(ToolContext ctx, List<VertexPair> edges)
        {
            var state = ctx.SelectionState;
            if (state == null) return;

            foreach (var edge in edges)
            {
                if (_addToSelection)
                    state.Edges.Add(edge);
                else
                    state.Edges.Remove(edge);
            }

            ctx.Repaint?.Invoke();
        }

        private void ApplyFaceSelection(ToolContext ctx, List<int> faces)
        {
            var state = ctx.SelectionState;
            if (state == null) return;

            foreach (int f in faces)
            {
                if (_addToSelection)
                    state.Faces.Add(f);
                else
                    state.Faces.Remove(f);
            }

            ctx.Repaint?.Invoke();
        }

        private void ApplyLineSelection(ToolContext ctx, List<int> lines)
        {
            var state = ctx.SelectionState;
            if (state == null) return;

            foreach (int l in lines)
            {
                if (_addToSelection)
                    state.Lines.Add(l);
                else
                    state.Lines.Remove(l);
            }

            ctx.Repaint?.Invoke();
        }

        // ================================================================
        // ヒットテスト
        // ================================================================

        private int FindNearestVertex(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshData == null) return -1;

            float minDist = VERTEX_CLICK_THRESHOLD;
            int nearest = -1;

            for (int i = 0; i < ctx.MeshData.VertexCount; i++)
            {
                Vector2 vScreen = ctx.WorldToScreen(ctx.MeshData.Vertices[i].Position);
                float dist = Vector2.Distance(screenPos, vScreen);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = i;
                }
            }

            return nearest;
        }

        private VertexPair? FindNearestEdgePair(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshData == null) return null;

            if (ctx.TopologyCache != null)
            {
                float minDist = EDGE_CLICK_THRESHOLD;
                VertexPair? nearest = null;

                foreach (var pair in ctx.TopologyCache.AllEdgePairs)
                {
                    Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[pair.V1].Position);
                    Vector2 p2 = ctx.WorldToScreen(ctx.MeshData.Vertices[pair.V2].Position);
                    float dist = DistanceToLineSegment(screenPos, p1, p2);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = pair;
                    }
                }

                return nearest;
            }

            var legacy = FindNearestEdgeLegacy(ctx, screenPos);
            if (legacy.Item1 >= 0)
                return new VertexPair(legacy.Item1, legacy.Item2);

            return null;
        }

        private (int, int) FindNearestEdgeLegacy(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshData == null) return (-1, -1);

            float minDist = EDGE_CLICK_THRESHOLD;
            (int, int) nearest = (-1, -1);

            foreach (var face in ctx.MeshData.Faces)
            {
                int n = face.VertexCount;
                if (n < 2) continue;

                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[v1].Position);
                    Vector2 p2 = ctx.WorldToScreen(ctx.MeshData.Vertices[v2].Position);
                    float dist = DistanceToLineSegment(screenPos, p1, p2);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = v1 < v2 ? (v1, v2) : (v2, v1);
                    }
                }
            }

            return nearest;
        }

        private int FindNearestFace(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshData == null) return -1;

            int nearest = -1;
            float nearestDepth = float.MaxValue;

            for (int faceIdx = 0; faceIdx < ctx.MeshData.FaceCount; faceIdx++)
            {
                var face = ctx.MeshData.Faces[faceIdx];
                if (face.VertexCount < 3) continue;

                var screenPoints = new Vector2[face.VertexCount];
                Vector3 centroid = Vector3.zero;

                for (int i = 0; i < face.VertexCount; i++)
                {
                    var worldPos = ctx.MeshData.Vertices[face.VertexIndices[i]].Position;
                    screenPoints[i] = ctx.WorldToScreen(worldPos);
                    centroid += worldPos;
                }
                centroid /= face.VertexCount;

                if (IsPointInPolygon(screenPos, screenPoints))
                {
                    float depth = Vector3.Distance(ctx.CameraPosition, centroid);
                    if (depth < nearestDepth)
                    {
                        nearestDepth = depth;
                        nearest = faceIdx;
                    }
                }
            }

            return nearest;
        }

        private int FindNearestLine(ToolContext ctx, Vector2 screenPos)
        {
            if (ctx.MeshData == null) return -1;

            float minDist = EDGE_CLICK_THRESHOLD;
            int nearest = -1;

            for (int faceIdx = 0; faceIdx < ctx.MeshData.FaceCount; faceIdx++)
            {
                var face = ctx.MeshData.Faces[faceIdx];
                if (face.VertexCount != 2) continue;

                Vector2 p1 = ctx.WorldToScreen(ctx.MeshData.Vertices[face.VertexIndices[0]].Position);
                Vector2 p2 = ctx.WorldToScreen(ctx.MeshData.Vertices[face.VertexIndices[1]].Position);
                float dist = DistanceToLineSegment(screenPos, p1, p2);
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = faceIdx;
                }
            }

            return nearest;
        }

        // ================================================================
        // Connected 実装
        // ================================================================

        private List<int> GetConnectedVertices(MeshData meshData, int startVertex)
        {
            var result = new HashSet<int>();
            var queue = new Queue<int>();
            var adjacency = BuildVertexAdjacency(meshData);

            queue.Enqueue(startVertex);
            result.Add(startVertex);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!adjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (int neighbor in neighbors)
                {
                    if (result.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return result.ToList();
        }

        private List<VertexPair> GetConnectedEdges(ToolContext ctx, VertexPair startEdge)
        {
            var result = new HashSet<VertexPair>();
            var queue = new Queue<VertexPair>();
            var edgeAdjacency = BuildEdgeAdjacency(ctx);

            queue.Enqueue(startEdge);
            result.Add(startEdge);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!edgeAdjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (var neighbor in neighbors)
                {
                    if (result.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return result.ToList();
        }

        private List<int> GetConnectedFaces(ToolContext ctx, int startFace)
        {
            var result = new HashSet<int>();
            var queue = new Queue<int>();
            var faceAdjacency = BuildFaceAdjacency(ctx.MeshData);

            queue.Enqueue(startFace);
            result.Add(startFace);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!faceAdjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (int neighbor in neighbors)
                {
                    if (result.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return result.ToList();
        }

        private List<int> GetConnectedLines(ToolContext ctx, int startLine)
        {
            if (ctx.MeshData == null) return new List<int> { startLine };

            var result = new HashSet<int>();
            var queue = new Queue<int>();
            var lineAdjacency = BuildLineAdjacency(ctx.MeshData);

            queue.Enqueue(startLine);
            result.Add(startLine);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (!lineAdjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (int neighbor in neighbors)
                {
                    if (result.Add(neighbor))
                        queue.Enqueue(neighbor);
                }
            }

            return result.ToList();
        }

        // ================================================================
        // Belt 実装
        // ================================================================

        private struct BeltData
        {
            public HashSet<int> Vertices;
            public List<VertexPair> LadderEdges;
            public List<int> Faces;
        }

        private BeltData GetBeltData(MeshData meshData, VertexPair startEdge)
        {
            var result = new BeltData
            {
                Vertices = new HashSet<int>(),
                LadderEdges = new List<VertexPair>(),
                Faces = new List<int>()
            };

            var edgeToFaces = BuildEdgeToFacesMap(meshData);
            var visitedEdges = new HashSet<VertexPair>();

            TraverseBeltData(meshData, startEdge, edgeToFaces, visitedEdges, result, true);
            TraverseBeltData(meshData, startEdge, edgeToFaces, visitedEdges, result, false);

            return result;
        }

        private void TraverseBeltData(MeshData meshData, VertexPair startEdge,
            Dictionary<VertexPair, List<int>> edgeToFaces, HashSet<VertexPair> visitedEdges,
            BeltData result, bool forward)
        {
            var currentEdge = startEdge;

            while (true)
            {
                if (visitedEdges.Contains(currentEdge)) break;
                visitedEdges.Add(currentEdge);

                result.Vertices.Add(currentEdge.V1);
                result.Vertices.Add(currentEdge.V2);
                result.LadderEdges.Add(currentEdge);

                if (!edgeToFaces.TryGetValue(currentEdge, out var faces)) break;

                VertexPair? nextEdge = null;

                foreach (int faceIdx in faces)
                {
                    var face = meshData.Faces[faceIdx];
                    if (face.VertexIndices.Count != 4) continue;

                    if (!result.Faces.Contains(faceIdx))
                        result.Faces.Add(faceIdx);

                    var opposite = FindOppositeEdge(face, currentEdge.V1, currentEdge.V2);
                    if (opposite.HasValue)
                    {
                        var oppPair = new VertexPair(opposite.Value.Item1, opposite.Value.Item2);
                        if (!visitedEdges.Contains(oppPair))
                        {
                            nextEdge = oppPair;
                            break;
                        }
                    }
                }

                if (!nextEdge.HasValue) break;
                currentEdge = nextEdge.Value;
            }
        }

        private (int, int)? FindOppositeEdge(Face face, int v1, int v2)
        {
            var verts = face.VertexIndices;
            int n = verts.Count;
            if (n != 4) return null;

            for (int i = 0; i < n; i++)
            {
                if ((verts[i] == v1 && verts[(i + 1) % n] == v2) ||
                    (verts[i] == v2 && verts[(i + 1) % n] == v1))
                {
                    int oppStart = (i + 2) % n;
                    int oppEnd = (i + 3) % n;
                    return (verts[oppStart], verts[oppEnd]);
                }
            }

            return null;
        }

        // ================================================================
        // EdgeLoop 実装
        // ================================================================

        private List<VertexPair> GetEdgeLoopEdges(MeshData meshData, VertexPair startEdge)
        {
            var result = new HashSet<VertexPair>();
            var visitedEdges = new HashSet<VertexPair>();

            Vector3 edgeDir = (meshData.Vertices[startEdge.V2].Position -
                              meshData.Vertices[startEdge.V1].Position).normalized;

            var adjacency = BuildVertexAdjacency(meshData);

            TraverseEdgeLoopEdges(meshData, startEdge.V1, startEdge.V2, edgeDir, adjacency, visitedEdges, result);
            TraverseEdgeLoopEdges(meshData, startEdge.V2, startEdge.V1, -edgeDir, adjacency, visitedEdges, result);

            return result.ToList();
        }

        private void TraverseEdgeLoopEdges(MeshData meshData, int fromV, int toV, Vector3 direction,
            Dictionary<int, HashSet<int>> adjacency, HashSet<VertexPair> visitedEdges, HashSet<VertexPair> result)
        {
            int current = toV;
            int prev = fromV;
            Vector3 currentDir = direction;

            while (true)
            {
                var edge = new VertexPair(prev, current);
                if (visitedEdges.Contains(edge)) break;
                visitedEdges.Add(edge);
                result.Add(edge);

                if (!adjacency.TryGetValue(current, out var neighbors)) break;

                int bestNext = -1;
                float bestDot = _edgeLoopThreshold;

                foreach (int next in neighbors)
                {
                    if (next == prev) continue;

                    Vector3 nextDir = (meshData.Vertices[next].Position - meshData.Vertices[current].Position).normalized;
                    float dot = Vector3.Dot(currentDir, nextDir);

                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestNext = next;
                    }
                }

                if (bestNext < 0) break;

                currentDir = (meshData.Vertices[bestNext].Position - meshData.Vertices[current].Position).normalized;
                prev = current;
                current = bestNext;
            }
        }

        // ================================================================
        // ShortestPath 実装
        // ================================================================

        private List<int> GetShortestPath(MeshData meshData, int start, int end)
        {
            var adjacency = BuildVertexAdjacency(meshData);
            var distances = new Dictionary<int, float>();
            var previous = new Dictionary<int, int>();
            var unvisited = new HashSet<int>();

            for (int i = 0; i < meshData.VertexCount; i++)
            {
                distances[i] = float.MaxValue;
                unvisited.Add(i);
            }
            distances[start] = 0;

            while (unvisited.Count > 0)
            {
                int current = -1;
                float minDist = float.MaxValue;
                foreach (int v in unvisited)
                {
                    if (distances[v] < minDist)
                    {
                        minDist = distances[v];
                        current = v;
                    }
                }

                if (current < 0 || current == end) break;
                unvisited.Remove(current);

                if (!adjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (int neighbor in neighbors)
                {
                    if (!unvisited.Contains(neighbor)) continue;

                    float edgeLength = Vector3.Distance(
                        meshData.Vertices[current].Position,
                        meshData.Vertices[neighbor].Position);

                    float alt = distances[current] + edgeLength;
                    if (alt < distances[neighbor])
                    {
                        distances[neighbor] = alt;
                        previous[neighbor] = current;
                    }
                }
            }

            var path = new List<int>();
            int node = end;
            while (previous.ContainsKey(node))
            {
                path.Add(node);
                node = previous[node];
            }
            path.Add(start);
            path.Reverse();

            return path;
        }

        private List<VertexPair> GetEdgesFromPath(List<int> path)
        {
            var result = new List<VertexPair>();
            for (int i = 0; i < path.Count - 1; i++)
            {
                result.Add(new VertexPair(path[i], path[i + 1]));
            }
            return result;
        }

        // ================================================================
        // 変換ヘルパー
        // ================================================================

        private List<VertexPair> GetEdgesFromVertices(ToolContext ctx, List<int> vertices)
        {
            var vertSet = new HashSet<int>(vertices);
            var result = new List<VertexPair>();

            foreach (var face in ctx.MeshData.Faces)
            {
                int n = face.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    if (vertSet.Contains(v1) && vertSet.Contains(v2))
                    {
                        var edge = new VertexPair(v1, v2);
                        if (!result.Contains(edge))
                            result.Add(edge);
                    }
                }
            }

            return result;
        }

        private List<int> GetFacesFromVertices(ToolContext ctx, List<int> vertices)
        {
            var vertSet = new HashSet<int>(vertices);
            var result = new List<int>();

            for (int fIdx = 0; fIdx < ctx.MeshData.FaceCount; fIdx++)
            {
                var face = ctx.MeshData.Faces[fIdx];
                if (face.VertexCount < 3) continue;

                bool allIn = face.VertexIndices.All(v => vertSet.Contains(v));
                if (allIn)
                    result.Add(fIdx);
            }

            return result;
        }

        private List<int> GetLinesFromVertices(ToolContext ctx, List<int> vertices)
        {
            var vertSet = new HashSet<int>(vertices);
            var result = new List<int>();

            for (int fIdx = 0; fIdx < ctx.MeshData.FaceCount; fIdx++)
            {
                var face = ctx.MeshData.Faces[fIdx];
                if (face.VertexCount != 2) continue;

                if (vertSet.Contains(face.VertexIndices[0]) && vertSet.Contains(face.VertexIndices[1]))
                    result.Add(fIdx);
            }

            return result;
        }

        private List<VertexPair> GetEdgesFromFaces(ToolContext ctx, List<int> faces)
        {
            var result = new HashSet<VertexPair>();

            foreach (int fIdx in faces)
            {
                var face = ctx.MeshData.Faces[fIdx];
                int n = face.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    var edge = new VertexPair(face.VertexIndices[i], face.VertexIndices[(i + 1) % n]);
                    result.Add(edge);
                }
            }

            return result.ToList();
        }

        private List<int> GetAdjacentFaces(ToolContext ctx, List<VertexPair> edges)
        {
            var edgeToFaces = BuildEdgeToFacesMap(ctx.MeshData);
            var result = new HashSet<int>();

            foreach (var edge in edges)
            {
                if (edgeToFaces.TryGetValue(edge, out var faces))
                {
                    foreach (int f in faces)
                        result.Add(f);
                }
            }

            return result.ToList();
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        private Dictionary<int, HashSet<int>> BuildVertexAdjacency(MeshData meshData)
        {
            var adjacency = new Dictionary<int, HashSet<int>>();

            foreach (var face in meshData.Faces)
            {
                int n = face.VertexIndices.Count;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    if (!adjacency.ContainsKey(v1))
                        adjacency[v1] = new HashSet<int>();
                    if (!adjacency.ContainsKey(v2))
                        adjacency[v2] = new HashSet<int>();

                    adjacency[v1].Add(v2);
                    adjacency[v2].Add(v1);
                }
            }

            return adjacency;
        }

        private Dictionary<VertexPair, HashSet<VertexPair>> BuildEdgeAdjacency(ToolContext ctx)
        {
            var adjacency = new Dictionary<VertexPair, HashSet<VertexPair>>();
            var vertexToEdges = new Dictionary<int, List<VertexPair>>();

            var allEdges = new HashSet<VertexPair>();
            foreach (var face in ctx.MeshData.Faces)
            {
                int n = face.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    var edge = new VertexPair(face.VertexIndices[i], face.VertexIndices[(i + 1) % n]);
                    allEdges.Add(edge);
                }
            }

            foreach (var edge in allEdges)
            {
                if (!vertexToEdges.ContainsKey(edge.V1))
                    vertexToEdges[edge.V1] = new List<VertexPair>();
                if (!vertexToEdges.ContainsKey(edge.V2))
                    vertexToEdges[edge.V2] = new List<VertexPair>();

                vertexToEdges[edge.V1].Add(edge);
                vertexToEdges[edge.V2].Add(edge);
            }

            foreach (var edge in allEdges)
            {
                adjacency[edge] = new HashSet<VertexPair>();

                if (vertexToEdges.TryGetValue(edge.V1, out var list1))
                {
                    foreach (var neighbor in list1)
                        if (neighbor != edge) adjacency[edge].Add(neighbor);
                }
                if (vertexToEdges.TryGetValue(edge.V2, out var list2))
                {
                    foreach (var neighbor in list2)
                        if (neighbor != edge) adjacency[edge].Add(neighbor);
                }
            }

            return adjacency;
        }

        private Dictionary<int, HashSet<int>> BuildFaceAdjacency(MeshData meshData)
        {
            var adjacency = new Dictionary<int, HashSet<int>>();
            var edgeToFaces = new Dictionary<VertexPair, List<int>>();

            for (int fIdx = 0; fIdx < meshData.FaceCount; fIdx++)
            {
                var face = meshData.Faces[fIdx];
                if (face.VertexCount < 3) continue;

                adjacency[fIdx] = new HashSet<int>();

                int n = face.VertexCount;
                for (int i = 0; i < n; i++)
                {
                    var edge = new VertexPair(face.VertexIndices[i], face.VertexIndices[(i + 1) % n]);
                    if (!edgeToFaces.ContainsKey(edge))
                        edgeToFaces[edge] = new List<int>();
                    edgeToFaces[edge].Add(fIdx);
                }
            }

            foreach (var kvp in edgeToFaces)
            {
                var faces = kvp.Value;
                for (int i = 0; i < faces.Count; i++)
                {
                    for (int j = i + 1; j < faces.Count; j++)
                    {
                        adjacency[faces[i]].Add(faces[j]);
                        adjacency[faces[j]].Add(faces[i]);
                    }
                }
            }

            return adjacency;
        }

        private Dictionary<int, HashSet<int>> BuildLineAdjacency(MeshData meshData)
        {
            var adjacency = new Dictionary<int, HashSet<int>>();
            var vertexToLines = new Dictionary<int, List<int>>();

            for (int fIdx = 0; fIdx < meshData.FaceCount; fIdx++)
            {
                var face = meshData.Faces[fIdx];
                if (face.VertexCount != 2) continue;

                adjacency[fIdx] = new HashSet<int>();

                int v1 = face.VertexIndices[0];
                int v2 = face.VertexIndices[1];

                if (!vertexToLines.ContainsKey(v1))
                    vertexToLines[v1] = new List<int>();
                if (!vertexToLines.ContainsKey(v2))
                    vertexToLines[v2] = new List<int>();

                vertexToLines[v1].Add(fIdx);
                vertexToLines[v2].Add(fIdx);
            }

            foreach (int fIdx in adjacency.Keys.ToList())
            {
                var face = meshData.Faces[fIdx];
                if (face.VertexCount != 2) continue;

                if (vertexToLines.TryGetValue(face.VertexIndices[0], out var list1))
                {
                    foreach (var neighbor in list1)
                        if (neighbor != fIdx) adjacency[fIdx].Add(neighbor);
                }
                if (vertexToLines.TryGetValue(face.VertexIndices[1], out var list2))
                {
                    foreach (var neighbor in list2)
                        if (neighbor != fIdx) adjacency[fIdx].Add(neighbor);
                }
            }

            return adjacency;
        }

        private Dictionary<VertexPair, List<int>> BuildEdgeToFacesMap(MeshData meshData)
        {
            var map = new Dictionary<VertexPair, List<int>>();

            for (int faceIdx = 0; faceIdx < meshData.FaceCount; faceIdx++)
            {
                var face = meshData.Faces[faceIdx];
                int n = face.VertexIndices.Count;
                if (n < 2) continue;

                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];
                    var key = new VertexPair(v1, v2);

                    if (!map.ContainsKey(key))
                        map[key] = new List<int>();

                    map[key].Add(faceIdx);
                }
            }

            return map;
        }

        private static float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float len = line.magnitude;
            if (len < 0.001f) return Vector2.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (len * len));
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }

        private static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return false;

            bool inside = false;
            int j = polygon.Length - 1;

            for (int i = 0; i < polygon.Length; i++)
            {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) /
                              (polygon[j].y - polygon[i].y) + polygon[i].x)
                {
                    inside = !inside;
                }
                j = i;
            }

            return inside;
        }
    }
}