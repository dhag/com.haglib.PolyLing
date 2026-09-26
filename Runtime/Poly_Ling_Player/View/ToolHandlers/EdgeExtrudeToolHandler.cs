// EdgeExtrudeToolHandler.cs
// EdgeExtrudeTool を Player の入力イベントに橋渡しする IPlayerToolHandler 実装。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    [Poly_Ling.Data.PLTool("edgeExtrude", Description = "EdgeExtrudeTool（確定は EdgeExtrudeCommand）")]
    public class EdgeExtrudeToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 依存
        // ================================================================

        private readonly EdgeExtrudeTool _tool = new EdgeExtrudeTool();
        private          ProjectContext _project;

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;
        public Action            NotifyTopologyChanged;
        /// <summary>GPU ホバー結果取得。FindEdgeAtPosition 等 CPU 側探索の代替。</summary>
        public Func<MeshSelectMode, PlayerHoverElement> GetHoverElement;
        public Action            OnApplyCompleted;

        // ================================================================
        // 設定公開API
        // ================================================================

        [Poly_Ling.Data.PLToolParam(Description = "EdgeExtrudeTool.Mode")]
        public EdgeExtrudeSettings.ExtrudeMode Mode { get => _tool.Mode; set => _tool.Mode = value; }
        [Poly_Ling.Data.PLToolParam(Description = "EdgeExtrudeTool.SnapToAxis")]
        public bool SnapToAxis { get => _tool.SnapToAxis; set => _tool.SnapToAxis = value; }
        [Poly_Ling.Data.PLToolParam(Description = "EdgeExtrudeTool.DragSensitivity")]
        public float DragSensitivity { get => _tool.DragSensitivity; set => _tool.DragSensitivity = value; }

        // ================================================================
        // 初期化
        // ================================================================

        public void SetProject(ProjectContext project) => _project = project;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)         { _commandQueue   = queue; }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));
        }
        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var el = GetHoverElement?.Invoke(MeshSelectMode.Edge | MeshSelectMode.Line) ?? PlayerHoverElement.None;
            var edge = (el.Kind == PlayerHoverKind.Edge) ? new VertexPair(el.EdgeV1, el.EdgeV2) : (VertexPair?)null;
            int  line = (el.Kind == PlayerHoverKind.Line) ? el.FaceIndex : -1;
            _tool.PrepareHit(edge, line);
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            // screenPos は押した位置（MoveToolHandler.MouseDownPos）。以後の量はここからの全体差で決める。
            _pressOrigin = screenPos;
            _tool.OnMouseDown(ctx, ToImgui(screenPos, ctx));
        }
        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            // 押した位置から今の位置までの全体差（+Y が画面上。頂点移動の自由移動と同じ）。
            _tool.DragTo(ctx, screenPos - _pressOrigin);
        }

        private Vector2 _pressOrigin;

        /// <summary>
        /// ドラッグ確定。
        ///
        /// 【1 ドラッグ = 1 コマンド】
        ///   ドラッグ中の生成はプレビューとして扱い、確定時に開始状態へ戻してから
        ///   EdgeExtrudeCommand を 1 本発行する。実際の生成と Undo 記録は
        ///   EdgeExtrudeTool.ApplyExtrudeFromCommand が行う。
        ///
        /// 【SendCommand 未結線のとき】
        ///   取り出さず OnMouseUp（EndExtrude）に確定させる（3-d と同じ方針）。
        /// </summary>
        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;

            bool taken = false;
            List<VertexPair> takenEdges = null;
            List<int>        takenLines = null;
            List<int>        takenReversed = null;
            Vector3 takenOffset = Vector3.zero;

            if (SendCommand != null && _tool.ExtrudePending)
                taken = _tool.TryTakeExtrudeFromDrag(ctx, out takenEdges, out takenLines, out takenReversed, out takenOffset);

            // 取り出したときは _snapshotBefore が null なので EndExtrude は Undo を積まない。
            _tool.OnMouseUp(ctx, ToImgui(screenPos, ctx));

            if (taken)
            {
                var model = _project?.CurrentModel;
                var mc    = model?.ActiveMeshContext;
                if (model != null && mc != null)
                {
                    var pairs = new int[takenEdges.Count * 2];
                    for (int i = 0; i < takenEdges.Count; i++)
                    { pairs[i * 2] = takenEdges[i].V1; pairs[i * 2 + 1] = takenEdges[i].V2; }

                    // ドラッグ中に押し出していた辺・線分を全部載せる（確定後の形をドラッグ中と同じにする）。
                    SendCommand.Invoke(new Poly_Ling.Data.EdgeExtrudeCommand(
                        _project.CurrentModelIndex,
                        new[] { model.IndexOf(mc) },
                        pairs,
                        takenLines.ToArray(),
                        takenOffset,
                        takenReversed.ToArray()));
                }
            }

            OnApplyCompleted?.Invoke();
        }

        /// <summary>コマンドの発行先（Viewer から結線）。</summary>
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;

        /// <summary>
        /// 辺・線分の押し出しコマンドを実行する。
        /// 生成そのものは EdgeExtrudeTool が正典。ここは対象の照合だけを行う。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ExecuteFromCommand(Poly_Ling.Data.EdgeExtrudeCommand cmd, out string reason)
        {
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason))
                return false;

            var ctx = GetEnrichedCtx();
            if (ctx == null) { reason = "ツールコンテキストがありません"; return false; }

            var pairs = cmd.EdgeVertexPairs ?? System.Array.Empty<int>();
            if (pairs.Length % 2 != 0)
            { reason = "EdgeVertexPairs は頂点番号を 2 個ずつ並べてください"; return false; }
            var edges = new System.Collections.Generic.List<VertexPair>(pairs.Length / 2);
            for (int i = 0; i < pairs.Length; i += 2) edges.Add(new VertexPair(pairs[i], pairs[i + 1]));

            List<Vector3> positions = null;
            var flat = cmd.NewVertexPositions;
            if (flat != null && flat.Length > 0)
            {
                if (flat.Length % 3 != 0)
                { reason = "NewVertexPositions は x,y,z を 3 個ずつ並べてください"; return false; }
                positions = new List<Vector3>(flat.Length / 3);
                for (int i = 0; i < flat.Length; i += 3) positions.Add(new Vector3(flat[i], flat[i + 1], flat[i + 2]));
            }

            return _tool.ApplyExtrudeFromCommand(ctx, edges, cmd.LineIndices,
                cmd.ReversedLineIndices, cmd.LocalOffset, positions, out reason);
        }

        // ================================================================
        // ギズモでの押し出し（移動・回転・拡大縮小）
        // ================================================================

        public enum GizmoKind { None = 0, Move = 1, Rotate = 2, Scale = 3 }

        private GizmoKind _gizmo = GizmoKind.Move;

        /// <summary>辺押し出しの状態で出すギズモの種類（None はギズモを出さない）。</summary>
        [Poly_Ling.Data.PLToolParam(Description = "押し出しに使うギズモ（None / Move / Rotate / Scale）")]
        public GizmoKind Gizmo
        {
            get => _gizmo;
            set { if (_gizmo == value) return; _gizmo = value; OnGizmoKindChanged?.Invoke(); }
        }

        private bool _extrudePaused;

        /// <summary>
        /// 押し出しの一時停止。true の間はギズモも辺・線分のドラッグも押し出さず、
        /// 普通の移動・回転・拡大縮小になる。
        /// </summary>
        [Poly_Ling.Data.PLToolParam(Description = "押し出しの一時停止（true の間は普通の移動・回転・拡大縮小）")]
        public bool ExtrudePaused
        {
            get => _extrudePaused;
            set { if (_extrudePaused == value) return; _extrudePaused = value; OnGizmoKindChanged?.Invoke(); }
        }

        /// <summary>ギズモの種類・一時停止が変わった（Viewer が結線を組み直す）。</summary>
        public Action OnGizmoKindChanged;

        /// <summary>ギズモでの押し出し中か。</summary>
        public bool GizmoExtrudeActive => _tool.GizmoSessionActive;

        /// <summary>
        /// ギズモを掴んだ瞬間に呼ぶ。選択中の辺・線分を量 0 で押し出し、頂点選択を複製頂点にする。
        /// 押し出せる対象が無ければ false（何も変えない）。
        /// </summary>
        public bool BeginGizmoExtrude()
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return false;
            return _tool.BeginGizmoSession(ctx);
        }

        /// <summary>離したとき、ギズモ側を開始状態へ戻させる前に呼ぶ。ギズモでの押し出し中でなければ false。</summary>
        public bool CaptureGizmoExtrude()
        {
            if (!_tool.GizmoSessionActive) return false;
            var ctx = GetEnrichedCtx(); if (ctx == null) return false;
            return _tool.CaptureGizmoResult(ctx);
        }

        /// <summary>
        /// ギズモ側を開始状態へ戻させた後に呼ぶ。押す前へ戻し、動いていれば押し出しコマンドを 1 本送る
        /// （押し出しと変形をまとめて Undo 1 回）。
        /// </summary>
        public void FinishGizmoExtrude()
        {
            var ctx = GetEnrichedCtx(); if (ctx == null) return;
            _tool.FinishGizmoSession(ctx, out bool changed, out var edges, out var lines,
                                     out var reversed, out var positions);
            if (changed && SendCommand != null)
            {
                var model = _project?.CurrentModel;
                var mc    = model?.ActiveMeshContext;
                if (model != null && mc != null)
                {
                    var pairs = new int[edges.Count * 2];
                    for (int i = 0; i < edges.Count; i++) { pairs[i * 2] = edges[i].V1; pairs[i * 2 + 1] = edges[i].V2; }
                    var flat = new float[positions.Count * 3];
                    for (int i = 0; i < positions.Count; i++)
                    { flat[i * 3] = positions[i].x; flat[i * 3 + 1] = positions[i].y; flat[i * 3 + 2] = positions[i].z; }

                    SendCommand.Invoke(new Poly_Ling.Data.EdgeExtrudeCommand(
                        _project.CurrentModelIndex,
                        new[] { model.IndexOf(mc) },
                        pairs, lines.ToArray(), Vector3.zero, reversed.ToArray(),
                        null, flat));
                }
            }
            OnApplyCompleted?.Invoke();
        }
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            var el = GetHoverElement?.Invoke(MeshSelectMode.Edge | MeshSelectMode.Line) ?? PlayerHoverElement.None;
            var edge = (el.Kind == PlayerHoverKind.Edge) ? new VertexPair(el.EdgeV1, el.EdgeV2) : (VertexPair?)null;
            int  line = (el.Kind == PlayerHoverKind.Line) ? el.FaceIndex : -1;
            _tool.SetHoverEdge(edge, line);
        }

        // ── UIToolkit オーバーレイ用 ────────────────────────────────────
        public VertexPair? HoverEdge => _tool.HoverEdge;
        public void Activate(ToolContext ctx)
        {
            if (ctx != null)
            {
                var model = _project?.CurrentModel;
                ctx.Model            = model;
                ctx.SelectedVertices = model?.ActiveMeshContext?.SelectedVertices;
                ctx.SelectionState   = model?.ActiveMeshContext?.Selection;
                ctx.UndoController   = _undoController;
            ctx.GetVertexWorldPosition = GetVertexWorldPosition;
                ctx.CommandQueue     = _commandQueue;
                ctx.Repaint          = OnRepaint;
                ctx.NotifyTopologyChanged = NotifyTopologyChanged;
                ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();
                if (_undoController?.MeshUndoContext != null)
                    _undoController.MeshUndoContext.ParentModelContext = model;
            }
            _tool.OnActivate(ctx);
        }
        public void Deactivate(ToolContext ctx) { _tool.OnDeactivate(ctx); }

        // ================================================================
        // 内部ヘルパー
        // ================================================================


        private ToolContext GetEnrichedCtx()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return null;
            var model = _project?.CurrentModel;
            ctx.Model            = model;
            ctx.SelectedVertices = model?.ActiveMeshContext?.SelectedVertices;
            ctx.SelectionState   = model?.ActiveMeshContext?.Selection;
            ctx.UndoController   = _undoController;
            ctx.GetVertexWorldPosition = GetVertexWorldPosition;
            ctx.CommandQueue     = _commandQueue;
            ctx.Repaint          = OnRepaint;
            ctx.NotifyTopologyChanged    = NotifyTopologyChanged;
            ctx.SyncMesh                 = () => NotifyTopologyChanged?.Invoke();
            ctx.SyncMeshPositionsOnly    = () =>
            {
                var mc = _project?.CurrentModel?.ActiveMeshContext;
                if (mc != null) OnSyncMeshPositions?.Invoke(mc);
            };
            if (_undoController?.MeshUndoContext != null)
                _undoController.MeshUndoContext.ParentModelContext = model;
            return ctx;
        }


        /// <summary>
        /// 操作対象メッシュの頂点について GPU が計算したワールド座標を返す
        /// （Viewer から PlayerViewportManager.TryGetVertexWorld を結線）。
        /// CPU でスキニングを計算し直さないこと。
        /// </summary>
        public System.Func<int, UnityEngine.Vector3?> GetVertexWorldPosition;

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        private ToolContext BuildCtx(ModifierKeys mods, Vector2 sp)
        {
            var model = _project?.CurrentModel;
            if (model == null) return null;
            var ctx = GetToolContext?.Invoke() ?? new ToolContext();
            ctx.Model          = model;
            ctx.UndoController = _undoController;
            ctx.GetVertexWorldPosition = GetVertexWorldPosition;
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
