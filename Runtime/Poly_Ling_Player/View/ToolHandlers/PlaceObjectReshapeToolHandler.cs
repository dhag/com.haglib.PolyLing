// PlaceObjectReshapeToolHandler.cs
// PlaceObjectReshapeTool（藤壺の整形）を Player へ橋渡しする IPlayerToolHandler 実装。
// マウス操作は持たない。パネルの「開始」ボタンからのみ実行する。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// Activate() の設定は PipeAlignToolHandler の手順書に従う。
// 本ツールは複数メッシュの頂点位置だけを書き換えるため、
// ctx.SyncMeshContextPositionsOnly（メッシュ指定の軽量更新パス）を使う。
//
// 原型メッシュはパネルが持つ（描画オブジェクトの複数選択を結合したもの）。
// 実行の直前に GetPrototype から取り出してツールへ渡す。

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class PlaceObjectReshapeToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly PlaceObjectReshapeTool _tool = new PlaceObjectReshapeTool();
        private          ProjectContext         _project;

        // ================================================================
        // 外部コールバック（Viewer / SubPanel から設定）
        // ================================================================

        public Func<ToolContext>                  GetToolContext;
        public Action                             OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>原型メッシュの供給。パネルが結合済みの MeshObject を返す。</summary>
        public Func<MeshObject> GetPrototype;

        // ================================================================
        // 設定公開 API
        // ================================================================

        public PlaceObjectReshapeMode Mode
        {
            get => _tool.Mode;
            set => _tool.Mode = value;
        }

        public float  Lambda     { get => _tool.Lambda;     set => _tool.Lambda     = value; }
        public string TargetText { get => _tool.TargetText; set => _tool.TargetText = value; }

        public string LastResult      => _tool.LastResult;
        public int    TargetMeshCount => _tool.TargetMeshCount;

        /// <summary>選択頂点が属するパーツIDを「1,3,5」形式で返す。</summary>
        public string CollectSelectedPartsIdText() => _tool.CollectSelectedPartsIdText();

        public void TriggerExecute()
        {
            _tool.Prototype = GetPrototype?.Invoke();
            _tool.TriggerExecute();
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
