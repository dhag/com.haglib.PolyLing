// EdgeTriangleToolHandler.cs
// 辺から三角形。辺上を押してドラッグで新しい頂点を引き出し、離すと三角形を作る。
// 確定は EdgeTriangleCommand（実処理は EdgeTriangleOps）。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// 【押した位置】押す直前のホバー辺を掴み、辺の両端を画面へ投影した線分上で
//   押した位置に最も近い点を辺上の位置 t とする。新しい頂点はそこから出る。
// 【ドラッグ】押した位置からの画面上の全体差を、カメラと平行な面の上の移動に直す
//   （辺押し出しと同じ ScreenDeltaToWorldDelta）。
// 【確定】ほとんど動かさずに離したとき（DragMinPixels 未満）は何もしない。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Commands;
using Poly_Ling.Selection;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    [Poly_Ling.Data.PLTool("edgeTriangle", Description = "辺から三角形（確定は EdgeTriangleCommand）")]
    public class EdgeTriangleToolHandler : IPlayerToolHandler, IPlayerPressHandler
    {
        private const float DragMinPixels = 3f;

        public  Func<ProjectContext> GetProject;
        private ProjectContext _project => GetProject?.Invoke();

        private MeshUndoController _undoController;
        private CommandQueue       _commandQueue;

        public Func<ToolContext> GetToolContext;
        public Action            OnRepaint;
        public Action            NotifyTopologyChanged;
        public Func<MeshSelectMode, PlayerHoverElement> GetHoverElement;
        /// <summary>重ね表示の作り直し（Viewer の UpdateTopologyToolsOverlay）。</summary>
        public Action            OnRefreshOverlay;
        public Action<Poly_Ling.Data.PanelCommand> SendCommand;
        /// <summary>直前の実行結果が変わった（パネル更新用）。</summary>
        public Action            OnResultChanged;

        // ================================================================
        // 設定
        // ================================================================

        [Poly_Ling.Data.PLToolParam(Description = "辺が属する面がちょうど 1 枚の三角形なら、三角形を足さずに四角形にする")]
        public bool MakeQuad { get; set; } = true;

        // ================================================================
        // 状態（重ね表示用）
        // ================================================================

        private int     _hoverV1 = -1, _hoverV2 = -1;
        private bool    _pressed;
        private bool    _dragging;
        private int     _edgeV1 = -1, _edgeV2 = -1;
        private float   _edgeT;
        private Vector3 _pressLocal;
        private Vector3 _previewLocal;
        private Vector2 _pressScreen;

        public bool    HasHoverEdge  => _hoverV1 >= 0 && _hoverV2 >= 0;
        public int     HoverEdgeV1   => _hoverV1;
        public int     HoverEdgeV2   => _hoverV2;
        public bool    IsDragging    => _dragging;
        public int     DragEdgeV1    => _edgeV1;
        public int     DragEdgeV2    => _edgeV2;
        public Vector3 PreviewLocal  => _previewLocal;

        [Poly_Ling.Data.PLToolState(Description = "直前の実行結果")]
        public string LastResult { get; private set; } = "";

        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }
        public void SetCommandQueue(CommandQueue queue)         { _commandQueue   = queue; }

        // ================================================================
        // 入力
        // ================================================================

        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (_pressed) return;   // 押している間は掴んだ辺を保つ
            var el = GetHoverElement?.Invoke(MeshSelectMode.Edge) ?? PlayerHoverElement.None;
            if (el.Kind == PlayerHoverKind.Edge) { _hoverV1 = el.EdgeV1; _hoverV2 = el.EdgeV2; }
            else                                 { _hoverV1 = -1;        _hoverV2 = -1; }
        }

        public void OnLeftButtonDown(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            _pressed = false;
            _dragging = false;
            if (!HasHoverEdge) return;

            var ctx = GetEnrichedCtx();
            var mo  = ctx?.ActiveMeshObject;
            if (mo == null || _hoverV1 >= mo.VertexCount || _hoverV2 >= mo.VertexCount) return;

            _edgeV1 = _hoverV1;
            _edgeV2 = _hoverV2;

            Vector3 la = mo.Vertices[_edgeV1].Position;
            Vector3 lb = mo.Vertices[_edgeV2].Position;
            Vector2 sa = ctx.LocalToScreen(la);
            Vector2 sb = ctx.LocalToScreen(lb);
            Vector2 m  = ToImgui(screenPos, ctx);
            Vector2 ab = sb - sa;
            float len2 = ab.sqrMagnitude;
            _edgeT = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(m - sa, ab) / len2) : 0.5f;

            _pressLocal   = Vector3.Lerp(la, lb, _edgeT);
            _previewLocal = _pressLocal;
            _pressScreen  = screenPos;
            _pressed      = true;
        }

        public void OnLeftPressMove(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (!_pressed) return;
            UpdatePreview(screenPos);
        }

        public void OnLeftPressCancel(Vector2 screenPos, ModifierKeys mods)
        {
            _pressed = false;
            _dragging = false;
            OnRefreshOverlay?.Invoke();
        }

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods) { }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (!_pressed) return;
            _dragging = true;
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (!_pressed) return;
            _dragging = true;
            UpdatePreview(screenPos);
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            if (!_pressed) { _dragging = false; return; }
            UpdatePreview(screenPos);
            bool moved = (screenPos - _pressScreen).magnitude >= DragMinPixels;
            _pressed = false;
            _dragging = false;

            if (moved) SendConfirm();
            OnRefreshOverlay?.Invoke();
        }

        private void UpdatePreview(Vector2 screenPos)
        {
            var ctx = GetEnrichedCtx();
            if (ctx?.ScreenDeltaToWorldDelta == null) return;
            Vector2 total = screenPos - _pressScreen;   // +Y が画面上
            Vector3 worldDelta = ctx.ScreenDeltaToWorldDelta(
                total, ctx.CameraPosition, ctx.CameraTarget, ctx.CameraDistance, ctx.PreviewRect);
            _previewLocal = _pressLocal + ctx.ActiveWorldToLocalVector(worldDelta);
            OnRefreshOverlay?.Invoke();
        }

        private void SendConfirm()
        {
            var model = _project?.CurrentModel;
            var mc    = model?.ActiveMeshContext;
            var ctx   = GetEnrichedCtx();
            if (model == null || mc == null || ctx == null) return;

            var cmd = new Poly_Ling.Data.EdgeTriangleCommand(
                _project.CurrentModelIndex, new[] { model.IndexOf(mc) },
                _edgeV1, _edgeV2, _previewLocal, ctx.CameraPosition,
                _edgeT, MakeQuad, model.CurrentMaterialIndex);

            if (SendCommand != null) SendCommand(cmd);
            else ExecuteFromCommand(cmd, out _);
        }

        // ================================================================
        // コマンド経路
        // ================================================================

        public bool ExecuteFromCommand(Poly_Ling.Data.EdgeTriangleCommand cmd,
                                       out Poly_Ling.Ops.EdgeTriangleOps.Result result, out string reason)
        {
            result = default;
            reason = null;
            if (cmd == null) { reason = "コマンドが null"; return false; }

            var model = _project?.CurrentModel;
            if (model == null) { reason = "モデルがありません"; return false; }
            if (!PlayerCommandTargets.MatchesActiveMesh(model, cmd.MasterIndices, out reason)) return false;

            var ctx = GetEnrichedCtx();
            if (ctx == null) { reason = "ビューポートがありません"; return false; }

            int idx = cmd.MasterIndices[0];
            var mc  = model.GetMeshContext(idx);
            var mo  = mc?.MeshObject;
            if (mo == null) { reason = "メッシュがありません"; return false; }

            var before = new MultiMeshTopologySnapshot();
            if (_undoController != null) before.CaptureMesh(model, idx);

            Vector3 viewLocal = ctx.ActiveWorldToLocal(cmd.ViewPosition);
            if (!Poly_Ling.Ops.EdgeTriangleOps.Execute(
                    mo, cmd.EdgeV1, cmd.EdgeV2, cmd.Position, cmd.EdgeT,
                    cmd.MakeQuad, viewLocal, cmd.MaterialIndex, out result, out reason))
                return false;

            NotifyTopologyChanged?.Invoke();

            if (_undoController != null)
            {
                var after = new MultiMeshTopologySnapshot();
                after.CaptureMesh(model, idx);
                _undoController.SetModelContext(model);
                string desc = result.MadeQuad
                    ? $"Edge Triangle (quad) v{cmd.EdgeV1}-v{cmd.EdgeV2}"
                    : $"Edge Triangle v{cmd.EdgeV1}-v{cmd.EdgeV2}";
                var record = new MultiMeshTopologySnapshotRecord(before, after, desc);
                PLDiag.UndoRecord("MeshList", desc, record);
                _undoController.MeshListStack.Record(record, desc);
            }

            LastResult = result.MadeQuad
                ? $"四角形にしました（面 {result.FaceIndex}、頂点 {result.VertexIndex}）"
                : $"三角形を作りました（面 {result.FaceIndex}、頂点 {result.VertexIndex}、表裏: "
                  + (result.Winding == "view" ? "視点から見える向き" : "隣の面にそろえた") + "）";
            OnResultChanged?.Invoke();
            return true;
        }

        public bool ExecuteFromCommand(Poly_Ling.Data.EdgeTriangleCommand cmd, out string reason)
            => ExecuteFromCommand(cmd, out _, out reason);

        // ================================================================
        // 有効化
        // ================================================================

        public void Activate(ToolContext ctx)
        {
            _pressed = false;
            _dragging = false;
            _hoverV1 = _hoverV2 = -1;
        }

        public void Deactivate(ToolContext ctx)
        {
            _pressed = false;
            _dragging = false;
            _hoverV1 = _hoverV2 = -1;
        }

        private ToolContext GetEnrichedCtx()
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return null;
            var model = _project?.CurrentModel;
            ctx.Model            = model;
            ctx.SelectedVertices = model?.ActiveMeshContext?.SelectedVertices;
            ctx.SelectionState   = model?.ActiveMeshContext?.Selection;
            ctx.UndoController   = _undoController;
            ctx.CommandQueue     = _commandQueue;
            ctx.Repaint          = OnRepaint;
            ctx.NotifyTopologyChanged = NotifyTopologyChanged;
            ctx.SyncMesh              = () => NotifyTopologyChanged?.Invoke();
            if (_undoController?.MeshUndoContext != null)
                _undoController.MeshUndoContext.ParentModelContext = model;
            return ctx;
        }

        private static Vector2 ToImgui(Vector2 sp, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(sp.x, h - sp.y);
        }
    }
}
