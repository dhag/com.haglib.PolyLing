// SmoothEdgesToolHandler.cs
// SmoothEdgesTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// AlignVerticesToolHandler を参考実装として同じ手順を踏む:
//   1. ctx.Model            = model
//   2. ctx.SelectedVertices = mc?.SelectedVertices / ctx.SelectionState = mc?.Selection
//      （本ツールは SelectionState.Edges / Lines を読むため SelectionState が必須）
//   3. _undoController.MeshUndoContext.ParentModelContext = model
//   4. ctx.SyncMesh = () => OnSyncMeshPositions(mc)   ← 位置のみの変更なので軽量パス

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class SmoothEdgesToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly SmoothEdgesTool _tool = new SmoothEdgesTool();
        private          ProjectContext  _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>                  GetToolContext;
        public Action                             OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;
        public Action                             OnApplyCompleted;

        // ================================================================
        // 設定公開 API
        // ================================================================

        public float Strength     { get => _tool.Strength;     set => _tool.Strength     = value; }
        public int   Iterations   { get => _tool.Iterations;   set => _tool.Iterations   = value; }
        public bool  FixEndpoints { get => _tool.FixEndpoints; set => _tool.FixEndpoints = value; }
        public bool  LockX        { get => _tool.LockX;        set => _tool.LockX        = value; }
        public bool  LockY        { get => _tool.LockY;        set => _tool.LockY        = value; }
        public bool  LockZ        { get => _tool.LockZ;        set => _tool.LockZ        = value; }

        public int  SegmentCount       => _tool.SegmentCount;
        public int  ChainVertexCount   => _tool.ChainVertexCount;
        public int  EndpointCount      => _tool.EndpointCount;
        public int  MovableVertexCount => _tool.MovableVertexCount;
        public bool StatsCalculated    => _tool.StatsCalculated;

        /// <summary>統計だけ再計算する（選択変更後のパネル更新用）。</summary>
        public void RefreshStats() => _tool.RecalculateStats();

        /// <summary>平滑化を実行する。</summary>
        public void TriggerSmooth()
        {
            _tool.TriggerSmooth();
            OnApplyCompleted?.Invoke();
        }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)         => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)        { _commandQueue = queue; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) { }
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) { }
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) { }
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) { }
        public void UpdateHover(Vector2 screenPos, ToolContext ctx) { }

        public void Activate(ToolContext ctx)
        {
            if (ctx != null)
            {
                var model = _project?.CurrentModel;
                var mc    = model?.ActiveMeshContext;
                ctx.Model            = model;
                ctx.SelectedVertices = mc?.SelectedVertices;
                ctx.SelectionState   = mc?.Selection;
                ctx.UndoController   = _undoController;
                ctx.CommandQueue     = _commandQueue;
                ctx.Repaint          = OnRepaint;
                if (_undoController?.MeshUndoContext != null && model != null)
                    _undoController.MeshUndoContext.ParentModelContext = model;
                ctx.SyncMesh = () =>
                {
                    var target = model?.ActiveMeshContext;
                    if (target != null) OnSyncMeshPositions?.Invoke(target);
                };
            }
            _tool.OnActivate(ctx);
        }

        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }

        // ================================================================
        // 内部
        // ================================================================

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;
    }
}
