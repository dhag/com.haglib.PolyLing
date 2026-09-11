// EdgeRibbonFaceToolHandler.cs
// EdgeRibbonFaceTool を Player 側の口へ橋渡しする。
// InteractionMode は切り替えない。
//
// 【役割】
//   帯面のメッシュを組んで返すだけ。モデルへの追加・Undo・再構築は
//   受け口（PolyLingPlayerViewerCore.ExecuteEdgeRibbonFace）が
//   PlaceGeneratedMesh を通して行う。

using System;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Commands;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class EdgeRibbonFaceToolHandler : IPlayerToolHandler
    {
        private readonly EdgeRibbonFaceTool _tool =
            new EdgeRibbonFaceTool();

        private ProjectContext _project;
        private MeshUndoController _undoController;
        private CommandQueue _commandQueue;

        public Func<ToolContext> GetToolContext;
        public Action OnRepaint;
        public Action NotifyTopologyChanged;

        public EdgeRibbonFaceSettings Settings =>
            _tool.RibbonSettings;

        public void SetProject(ProjectContext project)
        {
            _project = project;
        }

        public void SetUndoController(MeshUndoController controller)
        {
            _undoController = controller;
        }

        public void SetCommandQueue(CommandQueue queue)
        {
            _commandQueue = queue;
        }

        public int GetSelectedEdgeCount()
        {
            return EdgeRibbonFaceTool.GetSelectedEdgeCount(
                _project?.CurrentModel);
        }

        /// <summary>
        /// 帯面のメッシュを組む。プレビューと実生成で同じ実装を通す。
        /// </summary>
        /// <param name="widthWorld">帯の幅。実行中だけ差し替え、終わったら元へ戻す。</param>
        /// <param name="meshObject">組んだメッシュ。失敗時は null。</param>
        /// <param name="reason">組めなかった理由。成功時は null。</param>
        public bool Build(float widthWorld, out MeshObject meshObject, out string reason)
        {
            float saved = Settings.WidthWorld;
            try
            {
                Settings.WidthWorld = widthWorld;
                return _tool.Build(_project?.CurrentModel, out meshObject, out reason);
            }
            finally
            {
                Settings.WidthWorld = saved;
            }
        }

        /// <summary>
        /// 帯面コマンドからメッシュを組む。
        /// 対象は選択中の描画オブジェクトなので、コマンドの MasterIndices と照合する。
        /// </summary>
        /// <param name="meshObject">組んだメッシュ。失敗時は null。</param>
        /// <param name="reason">組めなかった理由。成功時は null。</param>
        public bool BuildFromCommand(
            Poly_Ling.Data.EdgeRibbonFaceCommand cmd,
            out MeshObject meshObject, out string reason)
        {
            meshObject = null;
            reason = null;

            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesSelectedDrawables(
                    model, cmd.MasterIndices, out reason))
                return false;

            return Build(cmd.WidthWorld, out meshObject, out reason);
        }

        // 即時実行ツールなのでマウス入力は使用しない。
        public void OnLeftClick(
            PlayerHitResult hit,
            Vector2 screenPos,
            ModifierKeys mods) { }

        public void OnLeftDragBegin(
            PlayerHitResult hit,
            Vector2 screenPos,
            ModifierKeys mods) { }

        public void OnLeftDrag(
            Vector2 screenPos,
            Vector2 delta,
            ModifierKeys mods) { }

        public void OnLeftDragEnd(
            Vector2 screenPos,
            ModifierKeys mods) { }
    }
}
