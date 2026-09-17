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

        /// <summary>
        /// 描画オブジェクトの全頂点のワールド位置を GPU から読む口。読めなければ null。
        /// Viewer が配線する。帯の座標はこの値だけから組む。
        /// </summary>
        public Func<MeshContext, Vector3[]> GetWorldPositions;

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
        /// 選択中の描画オブジェクトの選択辺から帯面のメッシュを組む。プレビューが使う。
        /// </summary>
        /// <param name="widthWorld">帯の幅。実行中だけ差し替え、終わったら元へ戻す。</param>
        /// <param name="meshObject">組んだメッシュ。失敗時は null。</param>
        /// <param name="reason">組めなかった理由。成功時は null。</param>
        public bool Build(
            float widthWorld, bool addStartTag, bool addEndTag,
            out MeshObject meshObject, out string reason)
            => BuildCore(widthWorld, null, "", addStartTag, addEndTag, out meshObject, out reason);

        /// <summary>
        /// 帯面コマンドからメッシュを組む。
        /// 辞書名が空なら対象は選択中の描画オブジェクトなので、コマンドの MasterIndices と照合する。
        /// 辞書名があれば MasterIndices の各オブジェクトの辞書から辺を読み、選択とは照合しない。
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

            if (string.IsNullOrEmpty(cmd.EdgeSetName))
            {
                if (!PlayerCommandTargets.MatchesSelectedDrawables(
                        model, cmd.MasterIndices, out reason))
                    return false;

                return BuildCore(
                    cmd.WidthWorld, null, "", cmd.AddStartTag, cmd.AddEndTag,
                    out meshObject, out reason);
            }

            if (cmd.MasterIndices == null || cmd.MasterIndices.Length == 0)
            { reason = "MasterIndices が空です"; return false; }

            foreach (int idx in cmd.MasterIndices)
            {
                if (model.GetMeshContext(idx) == null)
                { reason = $"描画オブジェクトが見つかりません (masterIndex={idx})"; return false; }
            }

            return BuildCore(
                cmd.WidthWorld, cmd.MasterIndices, cmd.EdgeSetName,
                cmd.AddStartTag, cmd.AddEndTag, out meshObject, out reason);
        }

        private bool BuildCore(
            float widthWorld, int[] targetIndices, string edgeSetName,
            bool addStartTag, bool addEndTag,
            out MeshObject meshObject, out string reason)
        {
            float saved = Settings.WidthWorld;
            try
            {
                Settings.WidthWorld = widthWorld;
                return _tool.Build(
                    _project?.CurrentModel, targetIndices, edgeSetName, GetWorldPositions,
                    addStartTag, addEndTag, out meshObject, out reason);
            }
            finally
            {
                Settings.WidthWorld = saved;
            }
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
