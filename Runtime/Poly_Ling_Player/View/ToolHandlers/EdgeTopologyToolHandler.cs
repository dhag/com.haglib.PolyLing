// EdgeTopologyToolHandler.cs
// EdgeTopologyTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class EdgeTopologyToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly EdgeTopologyTool _tool = new EdgeTopologyTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;
        public Action            NotifyTopologyChanged;
        /// <summary>GPU ホバー結果取得。FindEdgeAtPosition 等 CPU 側探索の代替。</summary>
        public Func<MeshSelectMode, PlayerHoverElement> GetHoverElement;

        // ================================================================
        // 設定公開API
        // ================================================================

        public EdgeTopoMode ModePublic { get => _tool.ModePublic; set => _tool.ModePublic = value; }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)         { _commandQueue   = queue; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
        }
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
        }
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            _lastHoverScreenPos = screenPos; // UIToolkit Y（Y=0 上）
            // ToToolContext 由来の ctx には Model が設定されていないため、
            // ctx.FirstSelectedMeshObject は null を返す。MeshObject は _project から直接取得する。
            var mo = _project?.CurrentModel?.FirstSelectedMeshContext?.MeshObject;
            if (_tool.ModePublic == EdgeTopoMode.Split)
            {
                // Split モード: GPU 頂点ホバー
                var v = GetHoverElement?.Invoke(MeshSelectMode.Vertex) ?? PlayerHoverElement.None;
                int hv = (v.Kind == PlayerHoverKind.Vertex ? v.VertexIndex : -1);
                _tool.SetSplitHoverVertex(hv);
                _tool.SetHoverEdge(-1, -1, null);
            }
            else
            {
                // Flip / Dissolve: GPU 辺ホバー
                var el = GetHoverElement?.Invoke(MeshSelectMode.Edge) ?? PlayerHoverElement.None;
                if (el.Kind == PlayerHoverKind.Edge)
                    _tool.SetHoverEdge(el.EdgeV1, el.EdgeV2, mo);
                else
                    _tool.SetHoverEdge(-1, -1, null);
                _tool.SetSplitHoverVertex(-1);
            }
        }

        // ── UIToolkit オーバーレイ用 ────────────────────────────────────
        public bool HasHoverEdge => _tool.HasHoverEdge;
        public int  HoverEdgeV1  => _tool.HoverEdgeV1;
        public int  HoverEdgeV2  => _tool.HoverEdgeV2;
        /// <summary>Split モード: 1クリック目が確定済みか</summary>
        public bool HasSplitFirstVertex => _tool.HasSplitFirstVertex;
        /// <summary>Split モード: 1クリック目で確定した頂点インデックス（-1=未確定）</summary>
        public int  SplitFirstVertex    => _tool.SplitFirstVertex;
        /// <summary>Split モード: 現在ホバー中の頂点インデックス（-1=なし）</summary>
        public int  SplitHoverVertex    => _tool.SplitHoverVertex;
        /// <summary>
        /// Split モード: 第 1 頂点確定時にキャッシュされた対向点候補。
        /// Key = 対角頂点 index、Value = 対応する四角形 face index。
        /// 第 2 クリック時の判定および overlay で候補ハイライトに使用する。
        /// </summary>
        public System.Collections.Generic.IReadOnlyDictionary<int, int> SplitOpponentCandidates
            => _tool.SplitOpponentCandidates;
        public void Activate(ToolContext ctx)
        {
            if (ctx != null)
            {
                var model = _project?.CurrentModel;
                ctx.Model            = model;
                ctx.SelectedVertices = model?.FirstSelectedMeshContext?.SelectedVertices;
                ctx.SelectionState   = model?.FirstSelectedMeshContext?.Selection;
                ctx.UndoController   = _undoController;
                ctx.CommandQueue     = _commandQueue;
                ctx.Repaint          = OnRepaint;
                ctx.NotifyTopologyChanged = NotifyTopologyChanged;
                ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();
                if (_undoController?.MeshUndoContext != null)
                    _undoController.MeshUndoContext.ParentModelContext = model;
            }
            _tool.OnActivate(ctx);
        }
        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }

        // ================================================================
        // 内部ヘルパー
        // ================================================================


        private ToolContext GetEnrichedCtx()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return null;
            var model = _project?.CurrentModel;
            ctx.Model            = model;
            ctx.SelectedVertices = model?.FirstSelectedMeshContext?.SelectedVertices;
            ctx.SelectionState   = model?.FirstSelectedMeshContext?.Selection;
            ctx.UndoController   = _undoController;
            ctx.CommandQueue     = _commandQueue;
            ctx.Repaint          = OnRepaint;
            ctx.NotifyTopologyChanged = NotifyTopologyChanged;
            ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();
            if (_undoController?.MeshUndoContext != null)
                _undoController.MeshUndoContext.ParentModelContext = model;
            return ctx;
        }

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;
        /// <summary>
        /// 最後の UpdateHover で受け取ったスクリーン位置（UIToolkit Y、Y=0 上）。
        /// UpdateHover は viewport の pointer move イベント駆動で呼ばれるため、
        /// ここへの更新は Tick 経由ではない。Split overlay で第 1 頂点と
        /// マウス位置を結ぶ線分の終点に使用する。
        /// </summary>
        private UnityEngine.Vector2 _lastHoverScreenPos;

        public UnityEngine.Vector2 LastHoverScreenPos => _lastHoverScreenPos;

        private ToolContext BuildCtx(ModifierKeys mods, Vector2 sp)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;
            var ctx = GetToolContext?.Invoke() ?? new ToolContext();
            ctx.Model          = model;
            ctx.UndoController = _undoController;
            ctx.Repaint        = OnRepaint;
            ctx.SyncMesh = () =>
            {
                foreach (int idx in model.SelectedDrawableMeshIndices)
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc != null) OnSyncMeshPositions?.Invoke(mc);
                }
            };
            ctx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(sp, ctx),
            };
            return ctx;
        }

        private static Vector2 ToImgui(Vector2 sp, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(sp.x, h - sp.y);
        }
    }
}
