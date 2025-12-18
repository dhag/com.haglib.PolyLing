// Tools/KnifeTool.cs
// ナイフツール - 面を切断する（メインファイル）

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;

namespace MeshFactory.Tools
{
    /// <summary>
    /// ナイフツールのモード
    /// </summary>
    public enum KnifeMode
    {
        /// <summary>面切断</summary>
        Cut,
        /// <summary>頂点ナイフ</summary>
        Vertex,
        /// <summary>辺消去</summary>
        Erase
    }

    /// <summary>
    /// ナイフツール
    /// </summary>
    public partial class KnifeTool : IEditTool
    {
        public string Name => "Knife";
        public string DisplayName => "Knife";
        //public ToolCategory Category => ToolCategory.Topology;

        // ================================================================
        // 設定（IToolSettings対応）
        // ================================================================

        private KnifeSettings _settings = new KnifeSettings();
        public IToolSettings Settings => _settings;

        // 設定へのショートカットプロパティ
        private KnifeMode Mode
        {
            get => _settings.Mode;
            set => _settings.Mode = value;
        }

        private bool EdgeSelect
        {
            get => _settings.EdgeSelect;
            set => _settings.EdgeSelect = value;
        }

        private bool ChainMode
        {
            get => _settings.ChainMode;
            set => _settings.ChainMode = value;
        }

        private bool AutoChain
        {
            get => _settings.AutoChain;
            set => _settings.AutoChain = value;
        }

        // ================================================================
        // ドラッグ状態
        // ================================================================

        private bool _isDragging;
        private Vector2 _startScreenPos;
        private Vector2 _currentScreenPos;
        private bool _isShiftHeld;

        // ================================================================
        // 検出結果
        // ================================================================

        private int _targetFaceIndex = -1;
        private List<EdgeIntersection> _intersections = new List<EdgeIntersection>();
        private List<(int FaceIndex, List<EdgeIntersection> Intersections)> _chainTargets =
            new List<(int, List<EdgeIntersection>)>();

        // ================================================================
        // Cut + EdgeSelect用
        // ================================================================

        private (Vector3, Vector3)? _firstEdgeWorldPos = null;
        private (Vector3, Vector3)? _hoveredEdgeWorldPos = null;
        private List<(Vector3, Vector3)> _beltEdgePositions = new List<(Vector3, Vector3)>();
        private float _cutRatio = 0.5f;
        private float _firstCutRatio = 0.5f;
        private bool _edgeBisectMode = false;

        // ================================================================
        // Vertex用
        // ================================================================

        private Vector3? _firstVertexWorldPos = null;
        private (Vector3, Vector3)? _targetEdgeWorldPos = null;
        private bool _vertexBisectMode = false;

        // ================================================================
        // Erase用
        // ================================================================

        private (int, int) _hoveredEdge = (-1, -1);

        // ================================================================
        // 定数
        // ================================================================

        private const float POSITION_EPSILON = 0.0001f;
        private const float SNAP_ANGLE_THRESHOLD = 15f;
        private const float EDGE_CLICK_THRESHOLD = 12f;
        private const float VERTEX_CLICK_THRESHOLD = 15f;

        // ================================================================
        // UI用
        // ================================================================

        private static readonly string[] ModeNames = { "Cut", "Vertex", "Erase" };
        private static readonly KnifeMode[] ModeValues = { KnifeMode.Cut, KnifeMode.Vertex, KnifeMode.Erase };

        // ================================================================
        // データ構造
        // ================================================================

        /// <summary>
        /// 辺と交点の情報
        /// </summary>
        private struct EdgeIntersection
        {
            public int EdgeStartIndex;
            public int EdgeEndIndex;
            public float T;
            public Vector3 WorldPos;
            public Vector2 ScreenPos;
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshData == null) return false;

            switch (Mode)
            {
                case KnifeMode.Cut:
                    return EdgeSelect 
                        ? HandleEdgeSelectMouseDown(ctx, mousePos)
                        : HandleDragCutMouseDown(ctx, mousePos);

                case KnifeMode.Vertex:
                    return EdgeSelect
                        ? HandleVertexEdgeSelectMouseDown(ctx, mousePos)
                        : HandleVertexDragMouseDown(ctx, mousePos);

                case KnifeMode.Erase:
                    return HandleEraseMouseDown(ctx, mousePos);
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (ctx.MeshData == null) return false;

            switch (Mode)
            {
                case KnifeMode.Cut:
                    return EdgeSelect
                        ? HandleEdgeSelectMouseDrag(ctx, mousePos)
                        : HandleDragCutMouseDrag(ctx, mousePos);

                case KnifeMode.Vertex:
                    return EdgeSelect
                        ? HandleVertexEdgeSelectMouseDrag(ctx, mousePos)
                        : HandleVertexDragMouseDrag(ctx, mousePos);

                case KnifeMode.Erase:
                    return HandleEraseMouseDrag(ctx, mousePos);
            }

            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshData == null)
            {
                _isDragging = false;
                return false;
            }

            switch (Mode)
            {
                case KnifeMode.Cut:
                    return EdgeSelect ? false : HandleDragCutMouseUp(ctx, mousePos);

                case KnifeMode.Vertex:
                    return EdgeSelect ? false : HandleVertexDragMouseUp(ctx, mousePos);

                case KnifeMode.Erase:
                    return false;
            }

            return false;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.MeshData == null) return;

            switch (Mode)
            {
                case KnifeMode.Cut:
                    if (EdgeSelect) DrawEdgeSelectGizmo(ctx);
                    else DrawDragCutGizmo(ctx);
                    break;

                case KnifeMode.Vertex:
                    if (EdgeSelect) DrawVertexEdgeSelectGizmo(ctx);
                    else DrawVertexDragGizmo(ctx);
                    break;

                case KnifeMode.Erase:
                    DrawEraseGizmo(ctx);
                    break;
            }
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Knife Tool", EditorStyles.boldLabel);

            // モード選択
            int currentIndex = Array.IndexOf(ModeValues, Mode);
            int newIndex = GUILayout.SelectionGrid(currentIndex, ModeNames, 3);
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < ModeValues.Length)
            {
                Mode = ModeValues[newIndex];
                Reset();
            }

            EditorGUILayout.Space(5);

            // オプション
            if (Mode != KnifeMode.Erase)
            {
                EdgeSelect = EditorGUILayout.ToggleLeft("Edge Select (click 2 points)", EdgeSelect);
                AutoChain = EditorGUILayout.ToggleLeft("Auto (continuous)", AutoChain);
            }

            EditorGUILayout.Space(5);

            // Cut + EdgeSelect用
            if (EdgeSelect && Mode == KnifeMode.Cut)
            {
                _edgeBisectMode = EditorGUILayout.ToggleLeft("Bisect (center)", _edgeBisectMode);
                if (_edgeBisectMode)
                {
                    _cutRatio = EditorGUILayout.Slider("Cut Position", _cutRatio, 0.1f, 0.9f);
                }
            }

            // Vertex用
            if (Mode == KnifeMode.Vertex)
            {
                _vertexBisectMode = EditorGUILayout.ToggleLeft("Bisect (center)", _vertexBisectMode);
            }

            // 選択状態表示
            if (EdgeSelect)
            {
                if (Mode == KnifeMode.Cut && _firstEdgeWorldPos.HasValue)
                {
                    EditorGUILayout.LabelField("First edge selected");
                    if (_beltEdgePositions.Count > 0)
                    {
                        EditorGUILayout.LabelField($"Edges to cut: {_beltEdgePositions.Count}");
                    }
                }
                else if (Mode == KnifeMode.Vertex && _firstVertexWorldPos.HasValue)
                {
                    EditorGUILayout.LabelField("Vertex selected");
                }
            }

            EditorGUILayout.Space(5);

            // ヘルプ
            string helpText = GetHelpText();
            if (!string.IsNullOrEmpty(helpText))
            {
                EditorGUILayout.HelpBox(helpText, MessageType.Info);
            }
        }

        private string GetHelpText()
        {
            switch (Mode)
            {
                case KnifeMode.Cut:
                    return EdgeSelect
                        ? "Click 2 edges to cut faces.\nESC: Cancel"
                        : "Drag to cut faces.\nShift: Snap to axis";

                case KnifeMode.Vertex:
                    return EdgeSelect
                        ? "Click vertex, then edge.\nESC: Cancel"
                        : "Drag from vertex to cut.\nShift: Snap to axis";

                case KnifeMode.Erase:
                    return "Click shared edge to erase.";
            }
            return "";
        }

        public void OnActivate(ToolContext ctx) => Reset();
        public void OnDeactivate(ToolContext ctx) => Reset();

        public void Reset()
        {
            _isDragging = false;
            _targetFaceIndex = -1;
            _intersections.Clear();
            _chainTargets.Clear();

            _firstEdgeWorldPos = null;
            _hoveredEdgeWorldPos = null;
            _beltEdgePositions.Clear();
            _firstCutRatio = 0.5f;

            _firstVertexWorldPos = null;
            _targetEdgeWorldPos = null;

            _hoveredEdge = (-1, -1);
        }
    }
}
