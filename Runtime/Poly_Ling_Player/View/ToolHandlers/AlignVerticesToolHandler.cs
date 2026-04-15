// AlignVerticesToolHandler.cs
// AlignVerticesTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// ================================================================
// 【Player移植時の必要手順】（このファイルを参考実装として使うこと）
//
// Activate() で必須の設定:
//   1. ctx.Model = model
//      → FirstDrawableMeshContext を使うために必要
//         （FirstSelectedMeshContext は ActiveCategory 依存で null になる）
//   2. ctx.SelectedVertices = mc?.SelectedVertices
//      ctx.SelectionState   = mc?.Selection
//      mc = model?.FirstDrawableMeshContext を使うこと
//   3. _undoController.MeshUndoContext.ParentModelContext = model
//      → OnUndoRedoPerformed で targetModel を解決するために必須
//         これが null だと Undo が無効のまま動かない
//   4. ctx.SyncMesh = () => { OnSyncMeshPositions(mc); }
//      → 位置変更後の軽量GPU更新パス
//         OnSyncMeshPositions = mc => SyncMeshPositionsAndTransform(mc, model) + UpdateTransform()
//         ※トポロジー変更ツールは NotifyTopologyChanged → RebuildAdapter を使うこと
//
// Apply/確定操作の後:
//   5. OnApplyCompleted?.Invoke() → NotifyPanels(ChangeKind.Attributes)
//      → Undoボタンの有効化に必要（NotifyPanels を呼ばないと更新されない）
//
// ViewerCore 側で必要な設定（PolyLingPlayerViewerCore 初期化ブロック）:
//   OnSyncMeshPositions = mc => { SyncMeshPositionsAndTransform(mc, model); UpdateTransform(); }
//   OnApplyCompleted    = () => NotifyPanels(ChangeKind.Attributes)
// ================================================================

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class AlignVerticesToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly AlignVerticesTool _tool    = new AlignVerticesTool();
        private          ProjectContext    _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>                          GetToolContext;
        public Action                                     OnRepaint;
        public Action<Poly_Ling.Data.MeshContext>         OnSyncMeshPositions;

        // ================================================================
        // 設定公開 API
        // ================================================================

        public bool      AlignX          { get => _tool.AlignX;          set => _tool.AlignX = value; }
        public bool      AlignY          { get => _tool.AlignY;          set => _tool.AlignY = value; }
        public bool      AlignZ          { get => _tool.AlignZ;          set => _tool.AlignZ = value; }
        public AlignMode Mode            { get => _tool.Mode;            set => _tool.Mode   = value; }

        public float StdDevX         => _tool.StdDevX;
        public float StdDevY         => _tool.StdDevY;
        public float StdDevZ         => _tool.StdDevZ;
        public bool  StatsCalculated => _tool.StatsCalculated;

        public int     SelectedVertexCount => _tool.SelectedVertexCount;
        public Vector3 GetAlignTarget()    => _tool.GetAlignTarget();

        public void TriggerAlign()      => _tool.TriggerAlign();
        public void TriggerAutoSelect() => _tool.TriggerAutoSelect();

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project)       => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)         { _commandQueue   = queue; }

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
                var mc    = model?.FirstDrawableMeshContext;
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
                    var target = model?.FirstDrawableMeshContext;
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
        private CommandQueue        _commandQueue;

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
