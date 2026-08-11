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
        public void  TriggerMerge() => _tool.TriggerMerge();

        // ================================================================
        // 即時実行 API（ショートカット / ボタンから）
        //   ツールをアクティブにせず、その場で ToolContext を組み立てて実行する。
        //   DeleteSelectionToolHandler.TriggerDelete と同じ方針。
        // ================================================================

        /// <summary>距離を見ず、選択頂点を 1 点（重心）へ結合する。</summary>
        public void TriggerMergeToCentroidNow()
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

        /// <summary>選択頂点のうち、しきい値以下の距離にあるものを結合する。</summary>
        public void TriggerMergeByThresholdNow()
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
