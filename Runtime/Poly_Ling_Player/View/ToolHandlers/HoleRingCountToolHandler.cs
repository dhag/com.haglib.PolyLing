// HoleRingCountToolHandler.cs
// HoleRingCountTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// マウス操作は持たず、パネルからの取り込み・実行を中継する。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class HoleRingCountToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly HoleRingCountTool _tool = new HoleRingCountTool();
        private          ProjectContext    _project;

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action            NotifyTopologyChanged;

        /// <summary>
        /// 選択中の描画オブジェクトから種を拾う。穴ごとに 1 つ、最大 2 件。
        /// ブリッジと同じ PickHoleSeeds を配線する。
        /// </summary>
        public Func<List<HoleSeedPick>> PickHoleSeeds;

        /// <summary>メッシュインデックス → 表示名。</summary>
        public Func<int, string> GetMeshNameAt;

        /// <summary>種が変わったときに呼ぶ。ビューポートのマーカーを即時更新させる。</summary>
        public Action OnSeedsChanged;

        // ================================================================
        // 公開 API
        // ================================================================

        public HoleRingCountTool.Seed BaseSeed   => _tool.BaseSeed;
        public HoleRingCountTool.Seed TargetSeed => _tool.TargetSeed;

        public bool SplitTriangleIntoTriangles
        {
            get => _tool.SplitTriangleIntoTriangles;
            set => _tool.SplitTriangleIntoTriangles = value;
        }

        public HoleRingCountTool.Summary Inspect() => _tool.Inspect();

        /// <summary>基準穴を現在の選択から取り込む。</summary>
        public bool ImportBase() => Import(isBase: true);

        /// <summary>対象穴を現在の選択から取り込む。</summary>
        public bool ImportTarget() => Import(isBase: false);

        /// <summary>取り込み済みの種を捨てる。</summary>
        public void ClearSeeds()
        {
            _tool.ClearSeeds();
            OnSeedsChanged?.Invoke();
        }

        /// <summary>
        /// 種を直接指定して取り込む。既存の種は捨てる。
        ///
        /// 【なぜ要るか】
        ///   Import は選択と GPU ホバーに依存するので、コマンド経由
        ///   （自動検証・MCP）からは通せない。穴の復元と検証は Tool 側の
        ///   ImportBase / ImportTarget が行うので、そこへ直接渡す入口を置く。
        ///
        /// 【実行時と同じ配線を通す】
        ///   Import と同じく、先に Activate を通してから取り込む。
        ///   種の検証は実行時と同じコンテキストで行わないと結果がずれる。
        /// </summary>
        /// <param name="reason">取り込めなかった理由。成功時は null。</param>
        public bool SetSeeds(
            int baseMeshIndex, int baseVertex, int baseDirectionHint,
            int targetMeshIndex, int targetVertex, int targetDirectionHint,
            out string reason)
        {
            reason = null;

            var ctx = GetToolContext?.Invoke();
            if (ctx != null) Activate(ctx);

            _tool.ClearSeeds();

            if (!_tool.ImportBase(baseMeshIndex, baseVertex, baseDirectionHint,
                                  GetMeshNameAt?.Invoke(baseMeshIndex)))
            {
                reason = "基準穴の取り込みに失敗: " + _tool.BaseSeed.Info;
                OnSeedsChanged?.Invoke();
                return false;
            }

            if (!_tool.ImportTarget(targetMeshIndex, targetVertex, targetDirectionHint,
                                    GetMeshNameAt?.Invoke(targetMeshIndex)))
            {
                reason = "対象穴の取り込みに失敗: " + _tool.TargetSeed.Info;
                OnSeedsChanged?.Invoke();
                return false;
            }

            OnSeedsChanged?.Invoke();
            return true;
        }

        /// <summary>実行する。成功可否と説明を返す。</summary>
        public bool Execute(out string message)
        {
            bool ok = _tool.Execute(out message);
            OnSeedsChanged?.Invoke();
            return ok;
        }

        // ================================================================
        // 取り込み
        // ================================================================

        /// <summary>
        /// 選択から種を 1 件拾って基準穴 / 対象穴へ入れる。
        /// 拾えた穴が 2 つあっても、ここで使うのは先頭の 1 件だけ。
        /// 基準と対象は別々のボタンで確定させる（ブリッジの「A/B同時取り込み」は持たない）。
        /// </summary>
        private bool Import(bool isBase)
        {
            // 実行時と同じコンテキストで種を検証するため、先に Activate と同じ配線を通す。
            var ctx = GetToolContext?.Invoke();
            if (ctx != null) Activate(ctx);

            if (PickHoleSeeds == null)
            {
                Fail(isBase, "選択の取り込みが配線されていません");
                return false;
            }

            var picks = PickHoleSeeds();
            if (picks == null || picks.Count == 0)
            {
                Fail(isBase, "エッジ上の頂点または辺を選択してください");
                return false;
            }
            if (!picks[0].Ok)
            {
                Fail(isBase, picks[0].Message);
                return false;
            }

            var p    = picks[0];
            string n = GetMeshNameAt?.Invoke(p.MeshIndex);

            bool ok = isBase
                ? _tool.ImportBase(p.MeshIndex, p.Vertex, p.DirectionHint, n)
                : _tool.ImportTarget(p.MeshIndex, p.Vertex, p.DirectionHint, n);

            OnSeedsChanged?.Invoke();
            return ok;
        }

        private void Fail(bool isBase, string message)
        {
            if (isBase) _tool.FailBase(message);
            else        _tool.FailTarget(message);
            OnSeedsChanged?.Invoke();
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
