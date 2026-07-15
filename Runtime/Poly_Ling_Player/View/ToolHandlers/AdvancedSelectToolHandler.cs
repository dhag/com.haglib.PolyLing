// AdvancedSelectToolHandler.cs
// AdvancedSelectTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class AdvancedSelectToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly AdvancedSelectTool _tool = new AdvancedSelectTool();
        private          ProjectContext     _project;
        private          PlayerSelectionOps _selectionOps;

        // TopologyCache はメッシュごとにキャッシュ
        private readonly Dictionary<int, TopologyCache> _topoCaches
            = new Dictionary<int, TopologyCache>();

        // ================================================================
        // 外部コールバック
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action            OnSelectionChanged;

        /// <summary>
        /// GPU ホバー要素取得（Viewer から結線）。指定 SelectMode に対する
        /// 現在ホバー中の頂点/辺（メッシュローカルインデックス）を返す。
        /// クリック開始要素の確定に使う（CPU 探索の誤爆を避ける）。
        /// </summary>
        public Func<Poly_Ling.Selection.MeshSelectMode, PlayerHoverElement> GetHoverElement;

        // ================================================================
        // モード設定公開
        // ================================================================

        public AdvancedSelectMode Mode
        {
            get => ((AdvancedSelectSettings)_tool.Settings)?.Mode ?? AdvancedSelectMode.Connected;
            set { if (_tool.Settings is AdvancedSelectSettings s) s.Mode = value; }
        }

        public bool AddToSelection
        {
            get => ((AdvancedSelectSettings)_tool.Settings)?.AddToSelection ?? false;
            set { if (_tool.Settings is AdvancedSelectSettings s) s.AddToSelection = value; }
        }

        /// <summary>EdgeLoop モードの方向しきい値（0〜1）。</summary>
        public float EdgeLoopThreshold
        {
            get => ((AdvancedSelectSettings)_tool.Settings)?.EdgeLoopThreshold ?? 0.5f;
            set { if (_tool.Settings is AdvancedSelectSettings s) s.EdgeLoopThreshold = Mathf.Clamp01(value); }
        }

        /// <summary>
        /// ShortestPath モードで登録されている始点頂点インデックスを返す。未登録は -1。
        /// エディタ版 ShortestPathSelectMode.DrawModeSettingsUI() の始点表示に対応。
        /// </summary>
        public int GetShortestPathFirstVertex() => _tool.GetShortestPathFirstVertex();

        /// <summary>
        /// ShortestPath モードの始点をクリアする。
        /// エディタ版 ClearFirstPoint ボタンに対応。
        /// </summary>
        public void ClearShortestPathFirst() => _tool.Reset();

        /// <summary>
        /// すべての選択（頂点/辺/面/線）を解除する。
        /// 進行中の ShortestPath 始点等もリセットする。全モード共通のクリアボタン用。
        /// </summary>
        public void ClearAllSelection()
        {
            _selectionOps?.ClearAll();     // SelectionState 全解除 + 描画通知（内部 OnSelectionChanged）
            _tool.Reset();                 // ShortestPath 始点など進行中状態も破棄
            OnSelectionChanged?.Invoke();  // renderer 再通知 + RequestNormal
            OnRepaint?.Invoke();
        }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetSelectionOps(PlayerSelectionOps ops) => _selectionOps = ops;
        public void SetUndoController(MeshUndoController ctrl) => _undoController = ctrl;
        private MeshUndoController _undoController;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            ResolveGpuStart();
            var oldSnap = _selectionOps?.SelectionState?.CreateSnapshot();
            bool changed = _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
            if (changed)
            {
                RecordSelectionUndo(ctx, oldSnap);
                OnSelectionChanged?.Invoke();
            }
            OnRepaint?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            ResolveGpuStart();
            var oldSnap = _selectionOps?.SelectionState?.CreateSnapshot();
            bool changed = _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            if (changed)
            {
                RecordSelectionUndo(ctx, oldSnap);
                OnSelectionChanged?.Invoke();
            }
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
            OnRepaint?.Invoke();
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods, screenPos);
            if (ctx == null) return;
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), Vector2.zero);
            OnRepaint?.Invoke();
        }

        // ================================================================
        // プレビューデータ取得（オーバーレイ描画用）
        // ================================================================

        /// <summary>
        /// AdvancedSelectTool が保持するプレビューコンテキストを返す。
        /// PolyLingPlayerViewer.UpdateAdvancedSelectOverlay から毎フレーム参照する。
        /// </summary>
        public AdvancedSelectContext GetPreviewContext() => _tool.GetPreviewContext();

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        /// <summary>
        /// GPU ホバー結果から次回クリックの開始要素（頂点/辺）を確定し、_tool に渡す。
        /// 操作対象メッシュ（FirstSelected）と一致するホバーのみ採用する。
        /// 未ヒット時は vertex=-1 / edge=null（各モードは CPU 探索へフォールバック）。
        /// </summary>
        /// <summary>直近クリックで確定した頂点（辺ヒット時は端点 V1）。未ヒットは -1。クリック強調用。</summary>
        public int LastClickVertex { get; private set; } = -1;

        /// <summary>直近クリックで確定した辺（辺ヒット時のみ）。頂点クリック時は null。辺の強調用。</summary>
        public Poly_Ling.Selection.VertexPair? LastClickEdge { get; private set; }

        private void ResolveGpuStart()
        {
            int              gpuVertex = -1;
            Poly_Ling.Selection.VertexPair? gpuEdge = null;

            if (GetHoverElement != null)
            {
                int firstIdx = _project?.CurrentModel?.FirstSelectedIndex ?? -1;

                // モードに応じて頂点／辺を問い合わせる。
                var queryMode = Mode switch
                {
                    AdvancedSelectMode.Belt         => Poly_Ling.Selection.MeshSelectMode.Edge,
                    AdvancedSelectMode.EdgeLoop     => Poly_Ling.Selection.MeshSelectMode.Edge,
                    AdvancedSelectMode.ShortestPath => Poly_Ling.Selection.MeshSelectMode.Vertex,
                    _                               => (_selectionOps?.SelectionState?.Mode
                                                        ?? Poly_Ling.Selection.MeshSelectMode.Vertex),
                };

                var elem = GetHoverElement(queryMode);
                // メッシュローカルインデックスは操作対象メッシュに対してのみ有効。
                if (firstIdx >= 0 && elem.MeshIndex == firstIdx)
                {
                    if (elem.Kind == PlayerHoverKind.Vertex)
                        gpuVertex = elem.VertexIndex;
                    else if (elem.Kind == PlayerHoverKind.Edge)
                        gpuEdge = new Poly_Ling.Selection.VertexPair(elem.EdgeV1, elem.EdgeV2);
                }
            }

            LastClickEdge   = gpuEdge;
            LastClickVertex = gpuVertex >= 0 ? gpuVertex
                            : (gpuEdge.HasValue ? gpuEdge.Value.V1 : -1);
            _tool.SetGpuStart(gpuVertex, gpuEdge);
        }

        private ToolContext BuildToolContext(ModifierKeys mods, Vector2 screenPosYDown)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;

            var baseCtx = GetToolContext?.Invoke() ?? new ToolContext();
            baseCtx.Model          = model;
            baseCtx.SelectionState = _selectionOps?.SelectionState;
            baseCtx.Repaint        = OnRepaint;
            baseCtx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(screenPosYDown, baseCtx),
            };

            // TopologyCache（メッシュ変更時に自動再構築）
            var mc = model.FirstSelectedMeshContext;
            if (mc?.MeshObject != null)
            {
                int key = mc.MeshObject.GetHashCode();
                if (!_topoCaches.TryGetValue(key, out var topo))
                {
                    topo = new TopologyCache(mc.MeshObject);
                    _topoCaches[key] = topo;
                }
                else
                {
                    topo.SetMeshObject(mc.MeshObject);
                }
                baseCtx.TopologyCache = topo;
            }

            return baseCtx;
        }

        private void RecordSelectionUndo(ToolContext ctx, SelectionSnapshot oldSnap)
        {
            if (_undoController == null || oldSnap == null) return;
            var newSnap = _selectionOps?.SelectionState?.CreateSnapshot();
            if (newSnap == null) return;
            var model = ctx.Model;
            if (model == null) return;
            _undoController.MeshUndoContext.ParentModelContext = model;
            var record = new SelectionChangeRecord(oldSnap, newSnap);
            {
                string __dbgDesc = "詳細選択";
                UnityEngine.Debug.Log("[UndoDbg] VertexEdit.Record desc=" + __dbgDesc + " type=" + ((record)?.GetType().Name ?? "<null>"));
                _undoController.VertexEditStack.Record(record, __dbgDesc);
            }
            _undoController.FocusVertexEdit();
        }

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
