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
    [Poly_Ling.Data.PLTool("smoothEdges", Description = "SmoothEdgesTool（確定は SmoothEdgesCommand）")]
    public class SmoothEdgesToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly SmoothEdgesTool _tool = new SmoothEdgesTool();

        /// <summary>今のプロジェクト（Viewer から結線）。保持せず使うたびに取りに行く。</summary>
        public Func<ProjectContext> GetProject;
        private ProjectContext Project => GetProject?.Invoke();

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

        [Poly_Ling.Data.PLToolParam(Description = "SmoothEdgesTool.Strength")]
        public float Strength     { get => _tool.Strength;     set => _tool.Strength     = value; }
        [Poly_Ling.Data.PLToolParam(Description = "SmoothEdgesTool.Iterations")]
        public int   Iterations   { get => _tool.Iterations;   set => _tool.Iterations   = value; }
        [Poly_Ling.Data.PLToolParam(Description = "SmoothEdgesTool.FixEndpoints")]
        public bool  FixEndpoints { get => _tool.FixEndpoints; set => _tool.FixEndpoints = value; }
        [Poly_Ling.Data.PLToolParam(Description = "SmoothEdgesTool.LockX")]
        public bool  LockX        { get => _tool.LockX;        set => _tool.LockX        = value; }
        [Poly_Ling.Data.PLToolParam(Description = "SmoothEdgesTool.LockY")]
        public bool  LockY        { get => _tool.LockY;        set => _tool.LockY        = value; }
        [Poly_Ling.Data.PLToolParam(Description = "SmoothEdgesTool.LockZ")]
        public bool  LockZ        { get => _tool.LockZ;        set => _tool.LockZ        = value; }

        [Poly_Ling.Data.PLToolState(Description = "SmoothEdgesTool.SegmentCount")]
        public int  SegmentCount       => _tool.SegmentCount;
        [Poly_Ling.Data.PLToolState(Description = "SmoothEdgesTool.ChainVertexCount")]
        public int  ChainVertexCount   => _tool.ChainVertexCount;
        [Poly_Ling.Data.PLToolState(Description = "SmoothEdgesTool.EndpointCount")]
        public int  EndpointCount      => _tool.EndpointCount;
        [Poly_Ling.Data.PLToolState(Description = "SmoothEdgesTool.MovableVertexCount")]
        public int  MovableVertexCount => _tool.MovableVertexCount;
        [Poly_Ling.Data.PLToolState(Description = "SmoothEdgesTool.StatsCalculated")]
        public bool StatsCalculated    => _tool.StatsCalculated;

        /// <summary>統計だけ再計算する（選択変更後のパネル更新用）。</summary>
        [Poly_Ling.Data.PLToolAction(Description = "統計（SegmentCount ほか）を再計算する")]
        public void RefreshStats() => _tool.RecalculateStats();

        // ================================================================
        // プレビュー（SurfaceSnapToolHandler と同じ形。操作経路統一計画.md H-2）
        // ================================================================
        //
        // プレビューは画面上の確認であって確定操作ではないため、コマンド化しない。
        // 対象を直接動かすので、開始時に担当者判定とロック取得を通し、終了時に外す。

        /// <summary>パネル操作でプレビューを始めてよいか（選択の担当者判定とロック取得）。</summary>
        public Func<bool> TryBeginPreview;

        /// <summary>パネルのプレビューが終わったときに呼ぶ（ロックを外す）。</summary>
        public Action EndPreview;

        private bool _panelPreview;

        [Poly_Ling.Data.PLToolState(Description = "プレビュー中か")]
        public bool IsPreviewing => _tool.IsPreviewing;

        /// <summary>
        /// プレビューのオン／オフ。オンで今の選択と設定の結果を表示し、オフで元の形状へ戻す。
        /// </summary>
        [Poly_Ling.Data.PLToolParam(Description = "プレビューのオン／オフ（オンで対象のロックを取る）")]
        public bool Preview
        {
            get => _tool.IsPreviewing;
            set { if (value) UpdatePreview(); else CancelPreviewIfActive(); }
        }

        /// <summary>プレビューを開始または更新する（今の選択と設定で計算し直す）。</summary>
        [Poly_Ling.Data.PLToolAction(Description = "プレビューを開始または更新する（対象のロックを取る）")]
        public void UpdatePreview()
        {
            if (!_panelPreview && TryBeginPreview != null)
            {
                if (!TryBeginPreview()) return;
                _panelPreview = true;
            }

            // 選択の差し替えに追随するため、計算のたびに今のコンテキストを通す。
            var ctx = GetToolContext?.Invoke();
            if (ctx != null) Activate(ctx);

            _tool.UpdatePreview();
        }

        /// <summary>プレビュー中なら元の形状へ戻して終える。</summary>
        [Poly_Ling.Data.PLToolAction(Description = "プレビュー中なら取り消す")]
        public void CancelPreviewIfActive() => CancelIfActive();

        public void CancelIfActive()
        {
            _tool.EndPreview();
            EndPanelPreview();
        }

        private void EndPanelPreview()
        {
            if (!_panelPreview) return;
            _panelPreview = false;
            EndPreview?.Invoke();
        }

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

            var model = Project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            // プレビューが書いた座標を元へ戻し、ロックも外してから実行する。
            CancelIfActive();

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
                var model = Project?.CurrentModel;
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
                // プレビューは開始時のメッシュを指定して同期する（途中で編集対象が変わっても戻せるように）。
                ctx.SyncMeshContextPositionsOnly = target =>
                {
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
