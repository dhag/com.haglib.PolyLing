// PipeAlignToolHandler.cs
// PipeAlignTool（パイプの整列）を Player へ橋渡しする IPlayerToolHandler 実装。
// マウス操作は持たない。パネルの「開始」ボタンからのみ実行する。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// Activate() の設定は AlignVerticesToolHandler の手順書に従う。
// 本ツールは複数メッシュの頂点位置だけを書き換えるため、
// ctx.SyncMeshContextPositionsOnly（メッシュ指定の軽量更新パス）を使う。

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class PipeAlignToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly PipeAlignTool  _tool = new PipeAlignTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>                  GetToolContext;
        public Action                             OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        // ================================================================
        // 設定公開 API
        // ================================================================

        public int  RingVertexCount { get => _tool.RingVertexCount; set => _tool.RingVertexCount = value; }
        public bool CapStart        { get => _tool.CapStart;        set => _tool.CapStart        = value; }
        public bool CapEnd          { get => _tool.CapEnd;          set => _tool.CapEnd          = value; }

        public string PairText   { get => _tool.PairText;   set => _tool.PairText   = value; }
        public string WeightText { get => _tool.WeightText; set => _tool.WeightText = value; }
        public string TargetText { get => _tool.TargetText; set => _tool.TargetText = value; }

        public PipeAlignMode Mode
        {
            get => _tool.Mode;
            set => _tool.Mode = value;
        }

        public PipeAlignDirection Direction
        {
            get => _tool.Direction;
            set => _tool.Direction = value;
        }

        public PipeSmoothEdgeMode EdgeMode
        {
            get => _tool.EdgeMode;
            set => _tool.EdgeMode = value;
        }

        public string LastResult      => _tool.LastResult;
        public int    TargetMeshCount => _tool.TargetMeshCount;

        /// <summary>
        /// 整列を実行する。
        ///
        /// private にしてある。パネルからの直呼びは塞ぎ、
        /// PipeAlignCommand 経由に統一するため。
        /// </summary>
        private void TriggerExecuteCore() => _tool.TriggerExecute();

        /// <summary>
        /// パイプ整列コマンドを実行する。
        ///
        /// 【マウス／パネル経路と同じ実装を通す】
        ///   整列そのものは PipeAlignTool が正典。ここは対象の照合と設定値の
        ///   差し替えだけを行い、同じ経路を呼ぶ。
        ///
        /// 【失敗理由】
        ///   書式の検査は実行前に、ツールが使うのと同じ公開パーサ
        ///   （PipeAlignOps.ParsePairs / PipeSmoothOps.ParseWeights / ParseTargets）を
        ///   呼んで行う。検査を作り直さないためこの 3 本をそのまま使う。
        ///   実行後の内訳（何個のメッシュに効いたか）はツールが LastResult に入れる。
        ///   PipeAlignTool は成功可否を外へ出さないので、そこは判定できない。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.PipeAlignCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesSelectedDrawables(model, cmd.MasterIndices, out reason))
                return false;

            // 実行時と同じコンテキストを通す。
            var ctx = GetToolContext?.Invoke();
            if (ctx != null) Activate(ctx);

            if (TargetMeshCount <= 0)
            {
                reason = "対象オブジェクトがありません";
                return false;
            }

            // 書式の検査。ツールの Execute が使うのと同じパーサを先に通す
            // （PipeAlignTool.cs:203-223 と同じ判定）。
            if (cmd.Mode == PipeAlignMode.Manual)
            {
                if (!PipeAlignOps.ParsePairs(cmd.PairText, out _, out string perr))
                {
                    reason = $"ペアの指定が読めません: {perr}";
                    return false;
                }
            }
            else if (cmd.Mode == PipeAlignMode.Smooth)
            {
                if (!PipeSmoothOps.ParseWeights(cmd.WeightText, out _, out string werr))
                {
                    reason = $"重みの指定が読めません: {werr}";
                    return false;
                }
                if (!PipeSmoothOps.ParseTargets(cmd.TargetText, out _, out string terr))
                {
                    reason = $"対象パーツの指定が読めません: {terr}";
                    return false;
                }
            }

            var    savedMode   = Mode;
            var    savedDir    = Direction;
            var    savedEdge   = EdgeMode;
            int    savedRing   = RingVertexCount;
            bool   savedCapS   = CapStart;
            bool   savedCapE   = CapEnd;
            string savedPair   = PairText;
            string savedWeight = WeightText;
            string savedTarget = TargetText;
            try
            {
                Mode            = cmd.Mode;
                Direction       = cmd.Direction;
                EdgeMode        = cmd.EdgeMode;
                RingVertexCount = cmd.RingVertexCount;
                CapStart        = cmd.CapStart;
                CapEnd          = cmd.CapEnd;
                PairText        = cmd.PairText;
                WeightText      = cmd.WeightText;
                TargetText      = cmd.TargetText;

                TriggerExecuteCore();
            }
            finally
            {
                Mode            = savedMode;
                Direction       = savedDir;
                EdgeMode        = savedEdge;
                RingVertexCount = savedRing;
                CapStart        = savedCapS;
                CapEnd          = savedCapE;
                PairText        = savedPair;
                WeightText      = savedWeight;
                TargetText      = savedTarget;
            }

            return true;
        }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)         => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)        { _commandQueue   = queue; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) {}
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods) {}
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods) {}
        public void UpdateHover(Vector2 screenPos, ToolContext ctx) {}

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

                ctx.SyncMeshContextPositionsOnly = target =>
                {
                    if (target != null) OnSyncMeshPositions?.Invoke(target);
                };
            }
            _tool.OnActivate(ctx);
        }

        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;
    }
}
