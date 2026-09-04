// VertexHoleToolHandler.cs
// VertexHoleTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// マウス操作は持たず、パネルからの実行のみを中継する。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class VertexHoleToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly VertexHoleTool _tool = new VertexHoleTool();
        private          ProjectContext _project;

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action            NotifyTopologyChanged;

        // ================================================================
        // 公開 API
        // ================================================================

        public float Ratio
        {
            get => _tool.Ratio;
            set => _tool.Ratio = value;
        }

        public int SelectedVertexCount => _tool.SelectedVertexCount;

        /// <summary>対象メッシュ全部を合わせた下調べ結果。</summary>
        public VertexHoleTool.HoleSummary Inspect() => _tool.Inspect();

        /// <summary>
        /// 穴あけを実行する。
        ///
        /// private にしてある。パネルからの直呼びは塞ぎ、
        /// VertexHoleCommand 経由に統一するため。
        /// </summary>
        private void TriggerHoleCore() => _tool.TriggerHole();

        /// <summary>
        /// 頂点に穴あけコマンドを実行する。
        ///
        /// 【マウス／パネル経路と同じ実装を通す】
        ///   実処理は VertexHoleTool が正典。ここは対象の照合と設定値の差し替えだけを行い、
        ///   同じ経路（Activate → Inspect → TriggerHole）を呼ぶ。
        ///
        /// 【設定の扱い】
        ///   コマンドの値を正典として実行し、終わったらパネルの値へ戻す。
        ///   1 呼び出しがパネルの状態に依存しないようにするため。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.VertexHoleCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesSelectedDrawables(model, cmd.MasterIndices, out reason))
                return false;

            // 実行時と同じコンテキストで下調べするため、先に Activate を通す。
            var ctx = GetToolContext?.Invoke();
            if (ctx != null) Activate(ctx);

            float savedRatio = Ratio;
            try
            {
                Ratio = cmd.Ratio;

                var summary = Inspect();
                if (!summary.CanExecute)
                {
                    reason = string.IsNullOrEmpty(summary.Reason)
                        ? "実行できる対象がありません"
                        : summary.Reason;
                    return false;
                }

                TriggerHoleCore();
            }
            finally
            {
                Ratio = savedRatio;
            }

            return true;
        }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)          => _project = project;
        public void SetUndoController(MeshUndoController ctrl)  { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)          { _commandQueue   = queue; }

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
                ctx.NotifyTopologyChanged = NotifyTopologyChanged;
                ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();
                if (_undoController?.MeshUndoContext != null && model != null)
                    _undoController.MeshUndoContext.ParentModelContext = model;
            }
            _tool.OnActivate(ctx);
        }

        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }
    }
}
