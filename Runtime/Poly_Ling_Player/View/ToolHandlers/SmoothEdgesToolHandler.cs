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

        /// <summary>
        /// 平滑化を実行する。
        ///
        /// private にしてある。パネルからの直呼びは塞ぎ、
        /// SmoothEdgesCommand 経由に統一するため。
        /// </summary>
        private void TriggerSmoothCore()
        {
            _tool.TriggerSmooth();
            OnApplyCompleted?.Invoke();
        }

        /// <summary>
        /// 辺の平滑化コマンドを実行する。
        ///
        /// 【マウス／パネル経路と同じ実装を通す】
        ///   平滑化そのものは SmoothEdgesTool が正典。ここは対象の照合と
        ///   設定値の差し替えだけを行い、同じ経路を呼ぶ。
        ///
        /// 【設定の扱い】
        ///   コマンドの値を正典として実行し、終わったらパネルの値へ戻す。
        ///   統計（MovableVertexCount）は FixEndpoints に依存するので、
        ///   差し替えたあとに RefreshStats してから可否を見る。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.SmoothEdgesCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            // 実行時と同じコンテキストで統計を出すため、先に Activate を通す。
            var ctx = GetToolContext?.Invoke();
            if (ctx != null) Activate(ctx);

            float savedStrength = Strength;
            int   savedIter     = Iterations;
            bool  savedFix      = FixEndpoints;
            bool  savedLockX    = LockX;
            bool  savedLockY    = LockY;
            bool  savedLockZ    = LockZ;
            try
            {
                Strength     = cmd.Strength;
                Iterations   = cmd.Iterations;
                FixEndpoints = cmd.FixEndpoints;
                LockX        = cmd.LockX;
                LockY        = cmd.LockY;
                LockZ        = cmd.LockZ;

                RefreshStats();
                if (MovableVertexCount <= 0)
                {
                    reason = SegmentCount > 0
                        ? "動かせる頂点がありません。端点固定を外すか選択範囲を広げてください"
                        : "辺または線分を選択してください";
                    return false;
                }

                TriggerSmoothCore();
            }
            finally
            {
                Strength     = savedStrength;
                Iterations   = savedIter;
                FixEndpoints = savedFix;
                LockX        = savedLockX;
                LockY        = savedLockY;
                LockZ        = savedLockZ;
            }

            return true;
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
