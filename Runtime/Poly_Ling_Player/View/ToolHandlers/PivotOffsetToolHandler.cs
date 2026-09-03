// PivotOffsetToolHandler.cs
// 「原点だけ移動」を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// 内部は ObjectMoveTool(OriginOnly=true) へ委譲する（案1: ピボットモードの再ルーティング）。
// 表示ラベルは「原点だけ移動」。内部識別子(Pivot 系)は据え置き。
// モード配線(入力/ギズモ/ホバー/パネル/ボタン)は Core 側で不変。

using System;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    /// <summary>
    /// 「原点だけ移動」(OriginOnly)。原点(BoneTransform.Position)だけを動かし、
    /// 対象メッシュの見た目と直接の子は据え置く。内部は ObjectMoveTool へ委譲。
    /// ギズモ座標/当たり判定は ObjectMoveTool の同一 _axisGizmo を用いるため一致する。
    /// </summary>
    public class PivotOffsetToolHandler : IPlayerToolHandler, IPlayerGizmoProvider
    {
        // ObjectMoveTool を専用設定(OriginOnly=true, 子は据え置き, ピック無効=ギズモ専用)で保持
        private readonly ObjectMoveTool _tool = new ObjectMoveTool();

        private ProjectContext    _project;
        private MeshUndoController _undoController;

        public PivotOffsetToolHandler()
        {
            _tool.SetSettings(new ObjectMoveSettings
            {
                OriginOnly        = true,
                MoveWithChildren  = false,   // 直接の子は据え置き(world 固定でローカル逆算)
                PickBones         = false,   // ギズモ専用(空クリックでのピックはしない)
                PickMeshesNoSkin  = false,
                PickMeshesSkinned = false,
                AllowRotationGizmo = false,  // 原点だけ移動: 回転リングは出さない
            });
        }

        // ================================================================
        // 外部コールバック(Viewer から設定) ─ Core は本クラスの公開面に依存するため維持
        // ================================================================

        public Func<ToolContext>  GetToolContext;
        public Action             OnRepaint;
        public Action             OnEnterTransformDragging;
        public Action             OnExitTransformDragging;
        public Action             OnSyncBoneTransforms;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) => _undoController = ctrl;

        /// <summary>
        /// コマンド送信口。ドラッグ確定をコマンド発行に寄せるために使う。
        /// PolyLingPlayerViewerCore が DispatchPanelCommand を刺す。
        /// </summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        // ================================================================
        // コマンド経路
        // ================================================================

        /// <summary>
        /// 原点移動コマンドを実行する。
        ///
        /// 【なぜ要るか】
        ///   マウス経路はギズモの軸を画面座標で当てるので、コマンド経由
        ///   （自動検証・MCP）からは通せない。対象と移動量だけを渡せる入口を置く。
        ///   SculptToolHandler.ExecuteFromCommand と同じ形。
        ///
        /// 【マウス経路と同じ実装を通す】
        ///   変形・子の補償・Undo は ObjectMoveTool.ApplyOriginOnlyFromCommand が
        ///   ドラッグ確定時と同じ SaveSnapshots → ApplyWorldDelta → CommitUndo を通す。
        ///
        /// 【Local の基準】
        ///   Space == Local のとき、Delta は MasterIndices[0] のローカル量として
        ///   解釈し、そのメッシュの WorldMatrix でワールドへ変換する。対象ごとに
        ///   行列が違うため基準は先頭の 1 本に固定する。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.MovePivotCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            var indices = cmd.MasterIndices;
            if (indices == null || indices.Length == 0)
            { reason = "対象が指定されていません"; return false; }

            Vector3 worldDelta;
            if (cmd.Space == Poly_Ling.Data.MoveSelectedVerticesCommand.CoordSpace.World)
            {
                worldDelta = cmd.Delta;
            }
            else
            {
                var baseMc = model.GetMeshContext(indices[0]);
                if (baseMc == null)
                { reason = $"masterIndex {indices[0]} のオブジェクトがありません"; return false; }
                worldDelta = baseMc.WorldMatrix.MultiplyVector(cmd.Delta);
            }

            var ctx = BuildToolContext(default(ModifierKeys));
            if (ctx == null) { reason = "モデルがありません"; return false; }

            return _tool.ApplyOriginOnlyFromCommand(ctx, indices, worldDelta, out reason);
        }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp  (ctx, ToImgui(screenPos, ctx));
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods);
            if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods);
            if (ctx == null) return;
            _tool.OnMouseDrag(ctx, ToImgui(screenPos, ctx), delta);
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = BuildToolContext(mods);
            if (ctx == null) return;

            // 【1 ストローク = 1 コマンド】
            //   ドラッグ中の適用はプレビュー扱い。確定時に開始状態へ戻して
            //   総移動量だけを取り出し、MovePivotCommand として送る。実際の移動と
            //   Undo 記録は ApplyOriginOnlyFromCommand が行う。
            //   送信口が無いときは取り出さず、ObjectMoveTool.OnMouseUp が
            //   従来どおり CommitUndo で確定させる。
            bool taken = false;
            int[] targets = null;
            Vector3 worldTotal = Vector3.zero;

            if (SendCommand != null && _tool.OriginOnlyDragPending)
                taken = _tool.TryTakeOriginOnlyDrag(ctx, out targets, out worldTotal);

            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));

            if (!taken) return;

            SendCommand(new Poly_Ling.Data.MovePivotCommand(
                _project?.CurrentModelIndex ?? 0,
                targets,
                worldTotal,
                Poly_Ling.Data.MoveSelectedVerticesCommand.CoordSpace.World));
        }

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            _tool.UpdateHoverOnly(ctx, ToImgui(screenPos, ctx));
        }

        // ================================================================
        // ギズモスクリーン座標: ObjectMoveTool の _axisGizmo をそのまま使う
        // (表示と当たり判定が同一計算になり、ズレて反応しない不具合を避ける)
        // ================================================================

        public bool TryGetGizmoScreenPositions(
            ToolContext ctx,
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            origin = xEnd = yEnd = zEnd = Vector2.zero;
            hoveredAxis = AxisGizmo.AxisType.None;

            var builtCtx = BuildToolContext(default);
            if (builtCtx == null) return false;

            return _tool.TryGetGizmoScreenPositions(
                builtCtx, out origin, out xEnd, out yEnd, out zEnd, out hoveredAxis);
        }

        /// <summary>
        /// ギズモ表示データを組み立てる（IPlayerGizmoProvider）。
        /// 「原点だけ移動」はダイヤスタイルの軸ギズモのみ。
        /// </summary>
        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;
            if (!TryGetGizmoScreenPositions(
                    ctx, out var origin, out var xEnd, out var yEnd, out var zEnd, out var hovAxis))
                return false;

            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo       = true,
                IsDiamondStyle = true,
                Origin         = origin, XEnd = xEnd, YEnd = yEnd, ZEnd = zEnd,
                HoveredAxis    = hovAxis,
            };
            return true;
        }

        // ================================================================
        // 内部ヘルパー
        // ================================================================

        private ToolContext BuildToolContext(ModifierKeys mods)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;

            var baseCtx = GetToolContext?.Invoke() ?? new ToolContext();

            baseCtx.Model                  = model;
            baseCtx.UndoController         = _undoController;
            baseCtx.SyncBoneTransforms     = OnSyncBoneTransforms;
            baseCtx.Repaint                = OnRepaint;
            baseCtx.EnterTransformDragging = OnEnterTransformDragging;
            baseCtx.ExitTransformDragging  = OnExitTransformDragging;
            baseCtx.InputState = new Poly_Ling.Data.ViewportInputState
            {
                IsShiftHeld   = mods.Shift,
                IsControlHeld = mods.Ctrl,
            };

            // OriginOnly は頂点を書き換えるので GPU 同期が必須
            baseCtx.SyncMesh = () =>
            {
                var mc = model.ActiveMeshContext;
                if (mc != null) OnSyncMeshPositions?.Invoke(mc);
            };

            return baseCtx;
        }

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
