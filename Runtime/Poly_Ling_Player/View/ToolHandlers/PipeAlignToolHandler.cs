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

        public void TriggerExecute() => _tool.TriggerExecute();

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
