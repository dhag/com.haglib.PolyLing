// Assets/Editor/Poly_Ling/Tools/Selection/AdvancedSelectTool.cs
// 特殊選択ツール - IToolSettings対応、モード別分離版

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Ops;
using Poly_Ling.Selection;
using Poly_Ling.Symmetry;
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

        private int UvNormalCountThreshold
        {
            get => _settings.UvNormalCountThreshold;
            set => _settings.UvNormalCountThreshold = value;
        }

        private float AxisDistanceThreshold
        {
            get => _settings.AxisDistanceThreshold;
            set => _settings.AxisDistanceThreshold = value;
        }

        private SymmetryAxis AxisKind
        {
            get => _settings.AxisKind;
            set => _settings.AxisKind = value;
        }

        private bool LimitToCurrentSelection
        {
            get => _settings.LimitToCurrentSelection;
            set => _settings.LimitToCurrentSelection = value;
        }

        // ================================================================
        // モード別処理
        // ================================================================

        private readonly Dictionary<AdvancedSelectMode, IAdvancedSelectMode> _modes;
        private AdvancedSelectContext _ctx = new AdvancedSelectContext();

        /// <summary>
        /// 直近に受け取った ToolContext。
        /// エディタ版 DrawSettingsUI() は引数で ToolContext を受け取れないため、
        /// ボタン実行（属性選択／選択反転）用にここへ保持する。
        /// ToolManager は _toolContext を 1 個だけ生成して使い回すので参照は安定している。
        /// Player はハンドラが毎回 ToolContext を組み立てて渡すため、この参照は使わない。
        /// </summary>
        private ToolContext _lastToolCtx;

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
            AdvancedSelectMode.ShortestPath,
            AdvancedSelectMode.UvNormalCount,
            AdvancedSelectMode.NearAxis,
            AdvancedSelectMode.BoundaryEdgeGroup,
            AdvancedSelectMode.BoundaryEdgeInSelection
        };

        /// <summary>クリックではなくボタン実行で動作するモードか。</summary>
        public static bool IsAttributeMode(AdvancedSelectMode mode)
            => mode == AdvancedSelectMode.UvNormalCount || mode == AdvancedSelectMode.NearAxis;

        /// <summary>ローカライズされたモード名配列を取得</summary>
        private string[] GetLocalizedModeNames() => new string[] {
            T("Connected"), T("Belt"), T("EdgeLoop"), T("Shortest"),
            T("UvNormalCount"), T("NearAxis"),
            T("BoundaryEdgeGroup"), T("BoundaryEdgeInSelection")
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
                { AdvancedSelectMode.ShortestPath, new ShortestPathSelectMode() },
                { AdvancedSelectMode.BoundaryEdgeGroup, new BoundaryEdgeSelectMode() }
            };
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.ActiveMeshObject == null) return false;

            UpdateContext(ctx);

            if (_modes.TryGetValue(Mode, out var mode))
            {
                return mode.HandleClick(_ctx, mousePos, ctx.CurrentSelectMode);
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (ctx.ActiveMeshObject == null) return false;

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
            _lastToolCtx = ctx;
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
            _ctx.GpuStartVertex = -1;
            _ctx.GpuStartEdge   = null;
            _ctx.GpuStartFace   = -1;
            _ctx.GpuStartLine   = -1;
            ResetAllModes();
        }

        // ================================================================
        // 属性選択 / 選択反転（クリック非依存。パネルのボタンから呼ぶ）
        // ================================================================

        /// <summary>
        /// UvNormalCount / NearAxis モードの選択を実行する。
        /// 上記以外のモードでは何もせず false を返す。
        ///
        /// LimitToCurrentSelection の扱い:
        ///   OFF … 判定に一致した頂点を AddToSelection に従って追加／削除する。
        ///   ON かつ AddToSelection=true  … 現在の選択のうち一致しなかった頂点を解除する（絞り込み）。
        ///   ON かつ AddToSelection=false … 現在の選択のうち一致した頂点を解除する。
        /// </summary>
        /// <returns>選択が変化したら true</returns>
        public bool ExecuteAttributeSelect(ToolContext ctx)
        {
            if (ctx?.ActiveMeshObject == null) return false;

            var mesh = ctx.ActiveMeshObject;
            var current = ctx.SelectionState?.Vertices;

            List<int> hits;
            switch (Mode)
            {
                case AdvancedSelectMode.UvNormalCount:
                    hits = AttributeSelectOps.SelectByUvNormalCount(
                        mesh, UvNormalCountThreshold, LimitToCurrentSelection, current);
                    break;

                case AdvancedSelectMode.NearAxis:
                    hits = AttributeSelectOps.SelectNearAxisPlane(
                        mesh, AxisKind, AxisDistanceThreshold, LimitToCurrentSelection, current);
                    break;

                default:
                    return false;
            }

            // 絞り込み: 一致しなかった選択頂点を解除する。
            // hits は current の部分集合なので追加すべき頂点は無い。
            if (LimitToCurrentSelection && AddToSelection)
            {
                if (current == null || current.Count == 0) return false;

                var hitSet = new HashSet<int>(hits);
                var drop = new List<int>();
                foreach (int v in current)
                {
                    if (!hitSet.Contains(v)) drop.Add(v);
                }
                if (drop.Count == 0) return false;

                SelectionHelper.ApplyVertexSelection(ctx, drop, false);
                return true;
            }

            if (hits.Count == 0) return false;
            SelectionHelper.ApplyVertexSelection(ctx, hits, AddToSelection);
            return true;
        }

        /// <summary>
        /// BoundaryEdgeInSelection モードの選択を実行する。
        /// 現在選択中の頂点だけで構成されるエッジ（1面だけが使う辺）を対象にし、
        /// AddToSelection に従って辺選択を追加／削除する。
        /// 頂点選択は変更しない（対象辺の端点はすでに選択済みのため）。
        /// 上記以外のモードでは何もせず false を返す。
        /// </summary>
        /// <returns>選択が変化したら true</returns>
        public bool ExecuteBoundaryEdgeInSelection(ToolContext ctx)
        {
            if (Mode != AdvancedSelectMode.BoundaryEdgeInSelection) return false;
            if (ctx?.ActiveMeshObject == null) return false;

            var selected = ctx.SelectionState?.Vertices;
            if (selected == null || selected.Count == 0) return false;

            var edges = BoundaryEdgeOps.EdgesWithinSelection(
                ctx.ActiveMeshObject, new HashSet<int>(selected));
            if (edges.Count == 0) return false;

            SelectionHelper.ApplyEdgeSelection(ctx, edges, AddToSelection);
            return true;
        }

        /// <summary>
        /// 現在の選択を反転する。SelectionState.Mode で有効なビットのみ対象。
        /// </summary>
        /// <returns>選択が変化したら true</returns>
        public bool InvertSelection(ToolContext ctx)
        {
            if (ctx?.ActiveMeshObject == null) return false;

            var state = ctx.SelectionState;
            if (state == null) return false;

            bool changed = InvertSelectionOps.Invert(state, ctx.ActiveMeshObject, ctx.TopologyCache);
            if (!changed) return false;

            // 後方互換の SelectedVertices を同期（頂点モードが有効なときのみ）
            if (state.Mode.Has(MeshSelectMode.Vertex) && ctx.SelectedVertices != null)
            {
                ctx.SelectedVertices.Clear();
                ctx.SelectedVertices.UnionWith(state.Vertices);
            }

            ctx.Repaint?.Invoke();
            return true;
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

        /// <summary>
        /// 次回クリック／プレビューの開始要素を GPU ホバー由来のインデックスで指定する。
        /// Player のハンドラが OnMouseDown / UpdateHover 直前に呼ぶ。未ヒットは -1 / null。
        /// 各モードは CPU 探索を使わず、この GPU 開始要素のみで解決する。
        /// </summary>
        public void SetGpuStart(int vertex, VertexPair? edge, int face = -1, int line = -1)
        {
            _ctx.GpuStartVertex = vertex;
            _ctx.GpuStartEdge   = edge;
            _ctx.GpuStartFace   = face;
            _ctx.GpuStartLine   = line;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private void UpdateContext(ToolContext ctx)
        {
            _lastToolCtx = ctx;
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
            if (faceIdx < 0 || faceIdx >= ctx.ActiveMeshObject.FaceCount) return;
            var face = ctx.ActiveMeshObject.Faces[faceIdx];
            if (face.VertexCount < 3) return;

            // UnityEditor_Handles 削除済み
            for (int i = 0; i < face.VertexCount; i++)
            {
                int v1 = face.VertexIndices[i];
                int v2 = face.VertexIndices[(i + 1) % face.VertexCount];
                if (v1 < 0 || v1 >= ctx.ActiveMeshObject.VertexCount) continue;
                if (v2 < 0 || v2 >= ctx.ActiveMeshObject.VertexCount) continue;
                Vector2 sp1 = ctx.LocalToScreen(ctx.ActiveMeshObject.Vertices[v1].Position);
                Vector2 sp2 = ctx.LocalToScreen(ctx.ActiveMeshObject.Vertices[v2].Position);
                // UnityEditor_Handles 削除済み
            }
        }

        private void DrawLinePreview(ToolContext ctx, int lineIdx)
        {
            if (lineIdx < 0 || lineIdx >= ctx.ActiveMeshObject.FaceCount) return;
            var face = ctx.ActiveMeshObject.Faces[lineIdx];
            if (face.VertexCount != 2) return;

            int v1 = face.VertexIndices[0];
            int v2 = face.VertexIndices[1];
            if (v1 < 0 || v1 >= ctx.ActiveMeshObject.VertexCount) return;
            if (v2 < 0 || v2 >= ctx.ActiveMeshObject.VertexCount) return;

            Vector2 sp1 = ctx.LocalToScreen(ctx.ActiveMeshObject.Vertices[v1].Position);
            Vector2 sp2 = ctx.LocalToScreen(ctx.ActiveMeshObject.Vertices[v2].Position);
            // UnityEditor_Handles 削除済み
        }
    }
}
