// DeleteSelectionToolHandler.cs
// DeleteSelectionTool を Player の呼び出し口に橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// 【他のハンドラとの違い】
//   削除はマウス操作を伴わない即時実行なので、InteractionMode を切り替えて
//   このハンドラをアクティブにする必要が無い (矩形/投げ縄サブツールと違い、
//   ドラッグを奪う必要がない)。IPlayerToolHandler の入力系は全て空実装で、
//   実行は TriggerDelete() を外部から直接呼ぶ形にしている。
//   ToolContext も OnActivate 時ではなく TriggerDelete() の中で毎回組み立てる。

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class DeleteSelectionToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly DeleteSelectionTool _tool = new DeleteSelectionTool();
        private          ProjectContext      _project;
        private          MeshUndoController  _undoController;
        private          CommandQueue        _commandQueue;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action            NotifyTopologyChanged;

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)          => _project = project;
        public void SetUndoController(MeshUndoController ctrl)  { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)         { _commandQueue   = queue; }

        // ================================================================
        // IPlayerToolHandler（入力は使わない）
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) {}
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) {}

        // ================================================================
        // 公開 API
        // ================================================================

        /// <summary>
        /// 削除対象の要素数（頂点 + 面 + 線分）。0 なら実行しても何も起きない。
        /// 選択中の描画オブジェクト全部の合計。
        /// </summary>
        public int GetDeletableCount()
        {
            return DeleteSelectionTool.GetDeletableCount(_project?.CurrentModel);
        }

        /// <summary>
        /// 選択されている頂点 / 面 / 線分を削除する。
        ///
        /// private にしてある。ボタン・ショートカットからの直呼びは塞ぎ、
        /// DeleteSelectionCommand 経由に統一するため。
        /// </summary>
        private void TriggerDeleteCore()
        {
            var ctx = BuildCtx();
            if (ctx == null)
            {
                // ここに来る主因は SetProject の伝播漏れ (BuildLayout 時点の
                // ActiveProject は null のため、プロジェクト生成/切替/受信の各経路で
                // 再伝播していないと _project が null のまま取り残される)。
                Debug.LogWarning("[DeleteSelectionToolHandler] EARLY RETURN: "
                               + $"project={_project != null}, currentModel={_project?.CurrentModel != null}");
                return;
            }
            _tool.Execute(ctx);
        }

        /// <summary>
        /// 選択要素の削除コマンドを実行する。
        ///
        /// 【マウス／パネル経路と同じ実装を通す】
        ///   削除そのものは DeleteSelectionTool が正典。ここは対象の照合だけを行い、
        ///   同じ経路（BuildCtx → Execute）を呼ぶ。
        ///
        /// 【対象の照合】
        ///   MasterIndices で選択を書き換えず、実行時点の選択と一致するかだけを見る。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.DeleteSelectionCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesSelectedDrawables(model, cmd.MasterIndices, out reason))
                return false;

            if (GetDeletableCount() <= 0)
            {
                reason = "削除できる要素がありません。頂点・面・線分を選択してください";
                return false;
            }

            TriggerDeleteCore();
            return true;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private ToolContext BuildCtx()
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;

            var ctx = GetToolContext?.Invoke() ?? new ToolContext();

            // SelectedVertices / SelectionState は設定しない。
            // DeleteSelectionTool は選択中の描画オブジェクトを自分で走査し、
            // 各メッシュの Selection を直接見るため、アクティブメッシュ固定の
            // 単一 SelectionState を渡すと対象が 1 つに絞られてしまう。
            ctx.Model            = model;
            ctx.UndoController   = _undoController;
            ctx.CommandQueue     = _commandQueue;
            ctx.Repaint          = OnRepaint;
            ctx.NotifyTopologyChanged = NotifyTopologyChanged;
            ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();

            if (_undoController?.MeshUndoContext != null)
                _undoController.MeshUndoContext.ParentModelContext = model;

            return ctx;
        }
    }
}
