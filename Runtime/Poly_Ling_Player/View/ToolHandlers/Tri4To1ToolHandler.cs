// Tri4To1ToolHandler.cs
// Tri4To1Tool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
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
    public class Tri4To1ToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly Tri4To1Tool    _tool = new Tri4To1Tool();
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

        public int SelectedFaceCount => _tool.SelectedFaceCount;

        /// <summary>対象メッシュ全部を合わせた下調べ結果。</summary>
        public Tri4To1Tool.MergeSummary Inspect() => _tool.Inspect();

        /// <summary>
        /// 三角形 4→1を実行する。
        ///
        /// private にしてある。パネル・ショートカットからの直呼びは塞ぎ、
        /// Tri4To1Command 経由に統一するため。
        /// </summary>
        private void TriggerMergeCore() => _tool.TriggerMerge();

        /// <summary>
        /// 三角形 4→1コマンドを実行する。
        ///
        /// 【マウス／パネル経路と同じ実装を通す】
        ///   実処理は Tri4To1Tool が正典。ここは対象の照合だけを行い、
        ///   同じ経路（Activate → Inspect → TriggerMerge）を呼ぶ。
        ///
        /// 【対象の照合】
        ///   MasterIndices で選択を書き換えず、実行時点の選択と一致するかだけを見る。
        ///   一致しなければ失敗理由を返す（照合方式）。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.Tri4To1Command cmd, out string reason)
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

            var summary = Inspect();
            if (!summary.CanExecute)
            {
                reason = string.IsNullOrEmpty(summary.Reason)
                    ? "実行できる対象がありません"
                    : summary.Reason;
                return false;
            }

            TriggerMergeCore();
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
