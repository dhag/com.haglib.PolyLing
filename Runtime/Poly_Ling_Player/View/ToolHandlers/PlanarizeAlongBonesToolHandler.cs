// PlanarizeAlongBonesToolHandler.cs
// PlanarizeAlongBonesTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;

namespace Poly_Ling.Player
{
    public class PlanarizeAlongBonesToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly PlanarizeAlongBonesTool _tool    = new PlanarizeAlongBonesTool();
        private          ProjectContext          _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext>                          GetToolContext;
        public Action                                     OnRepaint;
        public Action<Poly_Ling.Data.MeshContext>         OnSyncMeshPositions;

        // ================================================================
        // 設定公開 API
        // ================================================================

        public int               BoneIndexA { get => _tool.BoneIndexA; set => _tool.BoneIndexA = value; }
        public int               BoneIndexB { get => _tool.BoneIndexB; set => _tool.BoneIndexB = value; }
        public PlanePlacementMode PlaneMode  { get => _tool.PlaneMode;  set => _tool.PlaneMode  = value; }
        public float             Blend       { get => _tool.Blend;      set => _tool.Blend      = value; }

        public string[] BoneNames         => _tool.BoneNames;
        public int      SelectedVertexCount => _tool.SelectedVertexCount;

        public Vector3 GetBoneWorldPosition(int listIndex) => _tool.GetBoneWorldPosition(listIndex);
        /// <summary>
        /// 平面化を実行する。
        ///
        /// private にしてある。パネルからの直呼びは塞ぎ、
        /// PlanarizeAlongBonesCommand 経由に統一するため。
        /// </summary>
        private void   TriggerPlanarizeCore()              => _tool.TriggerPlanarize();
        public void    RebuildBoneList()                   => _tool.RebuildBoneListIfNeeded();

        /// <summary>
        /// ボーン平面への平面化コマンドを実行する。
        ///
        /// 【マウス／パネル経路と同じ実装を通す】
        ///   平面化そのものは PlanarizeAlongBonesTool が正典。ここは対象の照合と
        ///   設定値の差し替えだけを行い、同じ経路を呼ぶ。
        ///
        /// 【ボーン索引】
        ///   BoneIndexA / BoneIndexB はツールが組むボーン一覧（BoneNames）の索引で、
        ///   MeshContextList の索引ではない。照合前に RebuildBoneList で一覧を作る。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(
            Poly_Ling.Data.PlanarizeAlongBonesCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            if (cmd.BoneIndexA == cmd.BoneIndexB)
            {
                reason = "BoneIndexA と BoneIndexB は別のボーンを指定してください";
                return false;
            }
            if (cmd.Blend <= 0f)
            {
                reason = "Blend は 0 より大きい値を指定してください";
                return false;
            }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            // 実行時と同じコンテキストでボーン一覧と対象数を出すため、先に Activate を通す。
            var ctx = GetToolContext?.Invoke();
            if (ctx != null) Activate(ctx);

            RebuildBoneList();

            var names = BoneNames;
            if (names == null || names.Length == 0)
            {
                reason = "ボーンがありません";
                return false;
            }
            if (cmd.BoneIndexA < 0 || cmd.BoneIndexA >= names.Length ||
                cmd.BoneIndexB < 0 || cmd.BoneIndexB >= names.Length)
            {
                reason = $"ボーン索引が範囲外です（0〜{names.Length - 1}）";
                return false;
            }
            if (SelectedVertexCount < 1)
            {
                reason = "頂点を選択してください";
                return false;
            }

            int  savedA     = BoneIndexA;
            int  savedB     = BoneIndexB;
            var  savedMode  = PlaneMode;
            float savedBlend = Blend;
            try
            {
                BoneIndexA = cmd.BoneIndexA;
                BoneIndexB = cmd.BoneIndexB;
                PlaneMode  = cmd.PlaneMode;
                Blend      = cmd.Blend;

                TriggerPlanarizeCore();
            }
            finally
            {
                BoneIndexA = savedA;
                BoneIndexB = savedB;
                PlaneMode  = savedMode;
                Blend      = savedBlend;
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
