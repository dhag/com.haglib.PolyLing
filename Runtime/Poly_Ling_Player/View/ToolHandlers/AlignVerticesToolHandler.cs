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
//      mc = model?.ActiveMeshContext を使うこと
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

        /// <summary>
        /// 整列を実行する。
        ///
        /// private にしてある。パネルからの直呼びは塞ぎ、
        /// AlignVerticesCommand 経由に統一するため。
        /// </summary>
        private void TriggerAlignCore() => _tool.TriggerAlign();

        /// <summary>
        /// 統計から整列軸を推定してトグルへ入れる。メッシュも選択も書き換えない
        /// （AlignVerticesTool.cs:95-137）ので public のまま残す。
        /// </summary>
        public void TriggerAutoSelect() => _tool.TriggerAutoSelect();

        /// <summary>
        /// 頂点整列コマンドを実行する。
        ///
        /// 【マウス／パネル経路と同じ実装を通す】
        ///   整列そのものは AlignVerticesTool が正典。ここは対象の照合と
        ///   設定値の差し替えだけを行い、同じ経路を呼ぶ。
        ///
        /// 【設定の扱い】
        ///   コマンドの値を正典として実行し、終わったらパネルの値へ戻す。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.AlignVerticesCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            if (!cmd.AlignX && !cmd.AlignY && !cmd.AlignZ)
            {
                reason = "そろえる軸を 1 つ以上指定してください";
                return false;
            }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            // 実行時と同じコンテキストで対象数を見るため、先に Activate を通す。
            var ctx = GetToolContext?.Invoke();
            if (ctx != null) Activate(ctx);

            if (SelectedVertexCount < 2)
            {
                reason = "整列するには頂点を 2 個以上選択してください";
                return false;
            }

            bool savedX  = AlignX;
            bool savedY  = AlignY;
            bool savedZ  = AlignZ;
            var  savedMd = Mode;
            try
            {
                AlignX = cmd.AlignX;
                AlignY = cmd.AlignY;
                AlignZ = cmd.AlignZ;
                Mode   = cmd.Mode;

                TriggerAlignCore();
            }
            finally
            {
                AlignX = savedX;
                AlignY = savedY;
                AlignZ = savedZ;
                Mode   = savedMd;
            }

            return true;
        }

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
