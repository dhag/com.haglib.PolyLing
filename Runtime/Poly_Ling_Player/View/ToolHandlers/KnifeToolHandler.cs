// KnifeToolHandler.cs
// KnifeTool（一新版）を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// ヒットテスト・巡回はインデックスベース（AdvancedSelectToolHandler と同方式、TopologyCache を構築）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class KnifeToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly KnifeTool _tool = new KnifeTool();
        private          ProjectContext _project;

        // TopologyCache はメッシュごとにキャッシュ
        private readonly Dictionary<int, TopologyCache> _topoCaches = new Dictionary<int, TopologyCache>();

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;
        public Action            NotifyTopologyChanged;
        /// <summary>GPU ホバー結果取得（互換のため保持。現実装は SelectionHelper を使用）。</summary>
        public Func<MeshSelectMode, PlayerHoverElement> GetHoverElement;

        // ================================================================
        // 設定公開 API（サブパネル用）
        // ================================================================

        public KnifeMode Mode { get => _tool.Mode; set => _tool.Mode = value; }

        /// <summary>状態説明テキスト（サブパネル用）。</summary>
        public string StageText() => _tool.StageText();

        /// <summary>プレビュー（オーバーレイ描画用）。</summary>
        public KnifeTool.KnifePreview GetPreview() => _tool.Preview;

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) => _undoController = ctrl;
        public void SetCommandQueue(CommandQueue queue)        => _commandQueue   = queue;

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildCtx(mods, screenPos); if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            OnRepaint?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            // クリック指定のみ。ドラッグ開始は単発クリックと同じ扱い。
            OnLeftClick(hit, screenPos, mods);
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) { }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) { }

        public void UpdateHover(Vector2 screenPos, ToolContext baseCtx)
        {
            var ctx = EnrichCtx(baseCtx, default, screenPos); if (ctx == null) return;
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), Vector2.zero);
            OnRepaint?.Invoke();
        }

        public void Activate(ToolContext ctx)
        {
            EnrichCtx(ctx, default, Vector2.zero);
            _tool.OnActivate(ctx);
        }

        public void Deactivate(ToolContext ctx) => _tool.OnDeactivate(ctx);

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private ToolContext BuildCtx(ModifierKeys mods, Vector2 screenPos)
        {
            var ctx = GetToolContext?.Invoke();
            return EnrichCtx(ctx, mods, screenPos);
        }

        private ToolContext EnrichCtx(ToolContext ctx, ModifierKeys mods, Vector2 screenPos)
        {
            if (ctx == null) return null;
            var model = _project?.CurrentModel;
            if (model == null) return null;

            ctx.Model          = model;
            ctx.SelectionState = model.FirstSelectedMeshContext?.Selection;
            ctx.SelectedVertices = model.FirstSelectedMeshContext?.SelectedVertices;
            ctx.UndoController = _undoController;
            ctx.CommandQueue   = _commandQueue;
            ctx.Repaint        = OnRepaint;
            ctx.NotifyTopologyChanged = NotifyTopologyChanged;
            // トポロジ変更は NotifyTopologyChanged 一本で再構築する（二重リビルド回避）。
            ctx.SyncMesh = null;
            ctx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld          = mods.Shift,
                IsControlHeld        = mods.Ctrl,
                CurrentMousePosition = ToImgui(screenPos, ctx),
            };
            if (_undoController?.MeshUndoContext != null)
                _undoController.MeshUndoContext.ParentModelContext = model;

            // TopologyCache（メッシュ変更時に自動再構築）— AdvancedSelectToolHandler と同方式
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
                ctx.TopologyCache = topo;
            }

            return ctx;
        }

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
