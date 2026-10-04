// NormalEditToolHandler.cs
// 法線編集パネルのビューポート操作（InteractionMode.NormalEdit）。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置
//
// 【サブモード】
//   Select  … 選択だけ（このハンドラは使わず、Viewer が MoveToolHandler を選択専用で割り当てる）
//   Handle  … 球状化の中心／ターゲット指向の向き先を、ダイヤ型の軸ギズモで動かす
//   Rotate  … ドラッグで対象法線を回す（画面上のドラッグ方向へ倒れる。1 ドラッグ＝1 コマンド）
//   Brush   … 法線ブラシ（1 ストローク＝NormalBrushStrokeCommand 1 本）
//   Spoit   … クリックした頂点の法線を拾ってパネルの方向欄へ入れる
//
// 【プレビューと確定】
//   回転とブラシのドラッグ中はパネルの NormalEditPreviewState で表示だけ変え、
//   ドラッグ終了時に元へ戻してからコマンドを送る（スカルプト・原点移動と同じ「1 ストローク 1 コマンド」）。
//   パネル側の口は下の Func / Action で受ける（Viewer が配線する）。
//
// 【座標】
//   OnLeft* の screenPos はビューポート座標（Y=0 が下）。ctx.WorldToScreenPos は Y=0 が上なので、
//   比較・ギズモには ToImgui で直してから使う（PivotOffsetToolHandler と同じ）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    [PLTool("normalEdit", Description = "法線編集のビューポート操作（ハンドル・直接回転・ブラシ・スポイト）")]
    public class NormalEditToolHandler : IPlayerToolHandler, IPlayerGizmoProvider
    {
        public enum SubMode { Select, Handle, Rotate, Brush, Spoit }

        [PLToolParam(Description = "ビューポート操作の種類（Select / Handle / Rotate / Brush / Spoit）")]
        public SubMode Mode { get; set; } = SubMode.Select;

        /// <summary>直接回転の感度（画面 1px あたりの度）。</summary>
        [PLToolParam(Description = "直接回転の感度（画面 1px あたりの度）")]
        public float RotateDegreesPerPixel { get; set; } = 0.5f;

        // ================================================================
        // 配線（Viewer が設定する）
        // ================================================================

        public Func<ProjectContext> GetProject;
        public Func<ToolContext> GetToolContext;
        public Func<Vector2, float, PlayerHitResult> GetBrushHit;
        public Action OnRepaint;
        public Action OnGizmoRefresh;
        public Action<Vector2, float> OnUpdateBrushCircle;
        public Action OnHideBrushCircle;

        // パネル側の口
        /// <summary>ハンドルの位置（ワールド）。出せないときは null。</summary>
        public Func<Vector3?> GetHandleWorld;
        /// <summary>ハンドルを動かした（ワールド）。パネルが座標欄とプレビューを更新する。</summary>
        public Action<Vector3> OnHandleMoved;
        /// <summary>ハンドルのドラッグを終えた。</summary>
        public Action OnHandleDragEnd;
        /// <summary>スポイトで拾った方向（ワールド）。</summary>
        public Action<Vector3> OnDirectionPicked;
        /// <summary>回転・ブラシのプレビューを始める。始められなければ false。</summary>
        public Func<bool> BeginLivePreview;
        /// <summary>回転のプレビュー（軸はワールド、角度は度）。</summary>
        public Action<Vector3, float> PreviewRotate;
        /// <summary>回転の確定（プレビューを戻してからコマンドを送る）。</summary>
        public Action<Vector3, float> CommitRotate;
        /// <summary>ブラシのプレビュー（ここまでの中心列。ワールド）。</summary>
        public Action<List<Vector3>> PreviewBrush;
        /// <summary>ブラシの確定（プレビューを戻してからコマンドを送る）。</summary>
        public Action<List<Vector3>> CommitBrush;
        /// <summary>プレビューを捨てる。</summary>
        public Action CancelLivePreview;
        /// <summary>ブラシ半径（対象のローカル空間単位）。</summary>
        public Func<float> GetBrushRadius;

        // ================================================================
        // 状態
        // ================================================================

        private readonly AxisGizmo _gizmo = new AxisGizmo { ScreenOffset = Vector2.zero };
        private AxisGizmo.AxisType _dragAxis = AxisGizmo.AxisType.None;
        private Vector2 _lastImgui;

        private bool    _rotating;
        private Vector2 _rotateStart;
        private Vector3 _rotateAxis;
        private float   _rotateDeg;

        private bool _brushing;
        private readonly List<Vector3> _brushCenters = new List<Vector3>();

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            switch (Mode)
            {
                case SubMode.Spoit:
                    PickDirection(screenPos);
                    break;
                case SubMode.Brush:
                    // クリックは 1 点のストローク
                    if (TryBrushCenter(screenPos, out var c) && (BeginLivePreview?.Invoke() ?? false))
                    {
                        var list = new List<Vector3> { c };
                        CommitBrush?.Invoke(list);
                    }
                    UpdateBrushCircle(screenPos);
                    break;
            }
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;
            Vector2 imgui = ToImgui(screenPos, ctx);
            _lastImgui = imgui;

            switch (Mode)
            {
                case SubMode.Handle:
                {
                    var center = GetHandleWorld?.Invoke();
                    if (!center.HasValue) return;
                    _gizmo.Center = center.Value;
                    _dragAxis = _gizmo.FindAxisAtScreenPos(imgui, ctx);
                    _gizmo.DraggingAxis = _dragAxis;
                    break;
                }
                case SubMode.Rotate:
                    if (!(BeginLivePreview?.Invoke() ?? false)) return;
                    _rotating    = true;
                    _rotateStart = screenPos;
                    _rotateAxis  = Vector3.zero;
                    _rotateDeg   = 0f;
                    break;
                case SubMode.Brush:
                    if (!(BeginLivePreview?.Invoke() ?? false)) return;
                    _brushing = true;
                    _brushCenters.Clear();
                    if (TryBrushCenter(screenPos, out var c)) { _brushCenters.Add(c); PreviewBrush?.Invoke(_brushCenters); }
                    UpdateBrushCircle(screenPos);
                    break;
            }
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;
            Vector2 imgui = ToImgui(screenPos, ctx);
            Vector2 d = imgui - _lastImgui;   // Y 下系の差分
            _lastImgui = imgui;

            switch (Mode)
            {
                case SubMode.Handle:
                {
                    if (_dragAxis == AxisGizmo.AxisType.None) return;
                    var center = GetHandleWorld?.Invoke();
                    if (!center.HasValue) return;
                    _gizmo.Center = center.Value;
                    Vector3 move = _dragAxis == AxisGizmo.AxisType.Center
                        ? _gizmo.ComputeFreeDelta(new Vector2(d.x, -d.y), ctx)
                        : _gizmo.ComputeAxisDelta(d, _dragAxis, ctx);
                    if (move.sqrMagnitude < 1e-14f) return;
                    OnHandleMoved?.Invoke(center.Value + move);
                    OnGizmoRefresh?.Invoke();
                    OnRepaint?.Invoke();
                    break;
                }
                case SubMode.Rotate:
                {
                    if (!_rotating) return;
                    // 開始点からの総ドラッグ量で 1 本の軸・角度にする（1 ドラッグ＝1 回転）
                    Vector2 total = screenPos - _rotateStart;   // Y 上系
                    if (!TryCameraBasis(ctx, out var fwd, out var right, out var up)) return;
                    Vector3 m = right * total.x + up * total.y;
                    if (m.sqrMagnitude < 1e-8f) return;
                    // 視点を向く法線（-fwd）がドラッグ方向へ倒れる向きに回す
                    _rotateAxis = Vector3.Cross(-fwd, m).normalized;
                    _rotateDeg  = total.magnitude * RotateDegreesPerPixel;
                    PreviewRotate?.Invoke(_rotateAxis, _rotateDeg);
                    OnRepaint?.Invoke();
                    break;
                }
                case SubMode.Brush:
                {
                    if (!_brushing) return;
                    if (TryBrushCenter(screenPos, out var c))
                    {
                        // 半径の 1/4 以上動いたら次の塗りにする（同じ所を何度も塗らない）
                        float step = Mathf.Max(1e-5f, (GetBrushRadius?.Invoke() ?? 0.1f) * 0.25f);
                        if (_brushCenters.Count == 0 || (c - _brushCenters[_brushCenters.Count - 1]).magnitude >= step)
                        {
                            _brushCenters.Add(c);
                            PreviewBrush?.Invoke(_brushCenters);
                        }
                    }
                    UpdateBrushCircle(screenPos);
                    OnRepaint?.Invoke();
                    break;
                }
            }
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            switch (Mode)
            {
                case SubMode.Handle:
                    if (_dragAxis != AxisGizmo.AxisType.None) OnHandleDragEnd?.Invoke();
                    _dragAxis = AxisGizmo.AxisType.None;
                    _gizmo.DraggingAxis = AxisGizmo.AxisType.None;
                    OnGizmoRefresh?.Invoke();
                    break;
                case SubMode.Rotate:
                    if (!_rotating) return;
                    _rotating = false;
                    if (_rotateAxis.sqrMagnitude > 0f && _rotateDeg > 0f)
                        CommitRotate?.Invoke(_rotateAxis, _rotateDeg);
                    else
                        CancelLivePreview?.Invoke();
                    break;
                case SubMode.Brush:
                    if (!_brushing) return;
                    _brushing = false;
                    if (_brushCenters.Count > 0) CommitBrush?.Invoke(new List<Vector3>(_brushCenters));
                    else CancelLivePreview?.Invoke();
                    _brushCenters.Clear();
                    UpdateBrushCircle(screenPos);
                    break;
            }
            OnRepaint?.Invoke();
        }

        /// <summary>ホバー更新（ギズモの当たり表示・ブラシ円）。</summary>
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (ctx == null) return;
            if (Mode == SubMode.Handle)
            {
                var center = GetHandleWorld?.Invoke();
                if (!center.HasValue) { _gizmo.HoveredAxis = AxisGizmo.AxisType.None; return; }
                _gizmo.Center = center.Value;
                _gizmo.HoveredAxis = _gizmo.FindAxisAtScreenPos(ToImgui(screenPos, ctx), ctx);
            }
            else if (Mode == SubMode.Brush)
            {
                UpdateBrushCircle(screenPos);
            }
        }

        /// <summary>モードを抜けるとき・サブモードを変えるときの後始末。</summary>
        public void Deactivate()
        {
            if (_rotating || _brushing) CancelLivePreview?.Invoke();
            _rotating = _brushing = false;
            _dragAxis = AxisGizmo.AxisType.None;
            _gizmo.DraggingAxis = _gizmo.HoveredAxis = AxisGizmo.AxisType.None;
            OnHideBrushCircle?.Invoke();
        }

        // ================================================================
        // IPlayerGizmoProvider（ハンドルのときだけダイヤ型ギズモを出す）
        // ================================================================

        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;
            if (Mode != SubMode.Handle || ctx == null) return false;
            var center = GetHandleWorld?.Invoke();
            if (!center.HasValue) return false;

            _gizmo.Center = center.Value;
            _gizmo.GetScreenPositions(ctx, out var o, out var x, out var y, out var z);
            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo       = true,
                IsDiamondStyle = true,
                Origin = o, XEnd = x, YEnd = y, ZEnd = z,
                HoveredAxis    = _gizmo.HoveredAxis,
                DraggingAxis   = _gizmo.DraggingAxis,
            };
            return true;
        }

        // ================================================================
        // 内部
        // ================================================================

        private void PickDirection(Vector2 screenPos)
        {
            if (GetBrushHit == null) return;
            var hit = GetBrushHit(screenPos, 12f);
            if (!hit.HasHit) return;
            var mc = GetProject?.Invoke()?.CurrentModel?.GetMeshContext(hit.MeshIndex);
            var mo = mc?.MeshObject;
            if (mo == null || hit.VertexIndex < 0 || hit.VertexIndex >= mo.VertexCount) return;

            Vector3 sum = Vector3.zero;
            foreach (var n in mo.Vertices[hit.VertexIndex].Normals) sum += n;
            if (sum.sqrMagnitude < 1e-12f) return;

            Matrix4x4 m = NormalEditOps.ObjectToWorld(mc);
            Vector3 w = m.inverse.transpose.MultiplyVector(sum.normalized);
            if (w.sqrMagnitude < 1e-12f) return;
            OnDirectionPicked?.Invoke(w.normalized);
        }

        private bool TryBrushCenter(Vector2 screenPos, out Vector3 world)
        {
            world = default;
            if (GetBrushHit == null) return false;
            var hit = GetBrushHit(screenPos, 12f);
            if (!hit.HasHit) return false;
            var mc = GetProject?.Invoke()?.CurrentModel?.GetMeshContext(hit.MeshIndex);
            var mo = mc?.MeshObject;
            if (mo == null || hit.VertexIndex < 0 || hit.VertexIndex >= mo.VertexCount) return false;
            world = NormalEditOps.ObjectToWorld(mc).MultiplyPoint3x4(mo.Vertices[hit.VertexIndex].Position);
            return true;
        }

        private void UpdateBrushCircle(Vector2 screenPosYDown)
        {
            if (Mode != SubMode.Brush || OnUpdateBrushCircle == null) return;
            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;
            float r = GetBrushRadius?.Invoke() ?? 0.1f;

            Vector3 camRight = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;
            Vector2 a = ctx.WorldToScreenPos(ctx.CameraTarget, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 b = ctx.WorldToScreenPos(ctx.CameraTarget + camRight * r, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            OnUpdateBrushCircle(screenPosYDown, Mathf.Max(Vector2.Distance(a, b), 6f));
        }

        private static bool TryCameraBasis(ToolContext ctx, out Vector3 fwd, out Vector3 right, out Vector3 up)
        {
            fwd = ctx.CameraTarget - ctx.CameraPosition;
            right = up = Vector3.zero;
            if (fwd.sqrMagnitude < 1e-12f) return false;
            fwd.Normalize();
            Vector3 seed = Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            right = Vector3.Cross(seed, fwd).normalized;   // 左手系：Cross(up, forward) = right
            up    = Vector3.Cross(fwd, right).normalized;
            return true;
        }

        private static Vector2 ToImgui(Vector2 screenPosYDown, ToolContext ctx)
        {
            float h = ctx?.PreviewRect.height ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }
    }
}
