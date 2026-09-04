// MergeVerticesToolHandler.cs
// MergeVerticesTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class MergeVerticesToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly MergeVerticesTool _tool = new MergeVerticesTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;
        public Action            NotifyTopologyChanged;

        // ================================================================
        // 設定公開API
        // ================================================================

        public float Threshold   { get => _tool.Threshold;   set => _tool.Threshold   = value; }
        public bool  ShowPreview { get => _tool.ShowPreview; set => _tool.ShowPreview = value; }
        public MergePreviewInfo PreviewInfo => _tool.PreviewInfo;

        /// <summary>
        /// 旧経路（遅延実行）。_lastContext が無ければ _pendingMerge を立て、
        /// 次の Update(ctx) で実行される。頂点マージパネルを開いている間しか
        /// Update が回らないため、パネル外からは Trigger*Now を使うこと。
        /// </summary>
        private void TriggerMergeLazyCore() => _tool.TriggerMerge();

        // ================================================================
        // 即時実行 API（ショートカット / ボタンから）
        //   ツールをアクティブにせず、その場で ToolContext を組み立てて実行する。
        //   DeleteSelectionToolHandler.TriggerDelete と同じ方針。
        // ================================================================

        /// <summary>
        /// 距離を見ず、選択頂点を 1 点（重心）へ結合する。
        ///
        /// private にしてある。パネル・ショートカットからの直呼びは塞ぎ、
        /// MergeVerticesCommand 経由に統一するため。
        /// </summary>
        private void TriggerMergeToCentroidNow()
        {
            var ctx = BuildImmediateCtx();
            if (ctx == null)
            {
                Debug.LogWarning("[MergeVerticesToolHandler] EARLY RETURN: "
                               + $"project={_project != null}, currentModel={_project?.CurrentModel != null}");
                return;
            }
            _tool.ExecuteMergeToCentroid(ctx);
        }

        /// <summary>
        /// 選択頂点のうち、しきい値以下の距離にあるものを結合する。
        ///
        /// private にしてある。理由は TriggerMergeToCentroidNow と同じ。
        /// </summary>
        private void TriggerMergeByThresholdNow()
        {
            var ctx = BuildImmediateCtx();
            if (ctx == null)
            {
                Debug.LogWarning("[MergeVerticesToolHandler] EARLY RETURN: "
                               + $"project={_project != null}, currentModel={_project?.CurrentModel != null}");
                return;
            }
            _tool.ExecuteMergeByThreshold(ctx);
        }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)         => _project = project;
        public void SetUndoController(MeshUndoController ctrl)  { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)           { _commandQueue   = queue; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) {}
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) {}
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            // Update前にctxを補完する（UndoController・SyncMesh等が必要）
            FillCtx(ctx);
            _tool.Update(ctx);
        }
        public void Activate(ToolContext ctx)
        {
            FillCtx(ctx);
            _tool.OnActivate(ctx);
        }
        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }

        // ================================================================
        // コマンド受け口
        // ================================================================

        /// <summary>
        /// 頂点結合コマンドを実行する。
        ///
        /// 【マウス／パネル経路と同じ実装を通す】
        ///   結合そのものは MergeVerticesTool が正典。ここは対象の照合と
        ///   設定値の差し替えだけを行い、即時実行と同じ経路を呼ぶ。
        ///
        /// 【Threshold の扱い】
        ///   Centroid では読まれないが、Threshold モードと同じく退避・復元する。
        ///   1 呼び出しがパネルの状態に依存しないようにするため。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.MergeVerticesCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            var sel = model.ActiveMeshContext?.SelectedVertices;
            if (sel == null || sel.Count < 2)
            {
                reason = "結合するには頂点を 2 個以上選択してください";
                return false;
            }

            if (cmd.Mode == Poly_Ling.Data.MergeVerticesCommand.MergeMode.Threshold
                && cmd.Threshold <= 0f)
            {
                reason = "Threshold は 0 より大きい値を指定してください";
                return false;
            }

            float savedThreshold = Threshold;
            try
            {
                Threshold = cmd.Threshold;

                if (cmd.Mode == Poly_Ling.Data.MergeVerticesCommand.MergeMode.Centroid)
                    TriggerMergeToCentroidNow();
                else
                    TriggerMergeByThresholdNow();
            }
            finally
            {
                Threshold = savedThreshold;
            }

            return true;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        /// <summary>
        /// ツール実行に必要な参照を ToolContext へ流し込む。
        /// Activate と即時実行の両方から使う（内容が食い違わないよう一本化）。
        /// </summary>
        private void FillCtx(ToolContext ctx)
        {
            if (ctx == null) return;

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

        /// <summary>
        /// 即時実行用の ToolContext をその場で組み立てる。
        /// プロジェクト / モデルが無ければ null。
        /// </summary>
        private ToolContext BuildImmediateCtx()
        {
            if (_project?.CurrentModel == null) return null;
            var ctx = GetToolContext?.Invoke() ?? new ToolContext();
            FillCtx(ctx);
            return ctx;
        }

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
