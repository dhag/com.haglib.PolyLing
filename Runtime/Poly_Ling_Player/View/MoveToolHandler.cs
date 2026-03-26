// MoveToolHandler.cs
// 移動モードの IPlayerToolHandler 実装。
// Editor MoveTool と同等の状態機・IVertexTransform・AxisGizmo を使用する。
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Selection;

namespace Poly_Ling.Player
{
    public class MoveToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 状態機（Editor MoveTool と同等）
        // ================================================================
        private enum MoveState { Idle, PendingAction, MovingVertices, AxisDragging, CenterDragging }
        private MoveState _state = MoveState.Idle;

        // ================================================================
        // 外部注入コールバック
        // ================================================================
        public Action<MeshContext> OnSyncMeshPositions;
        public Action OnRepaint;
        public Action<Vector2, Vector2> OnBoxSelectUpdate;
        public Action OnBoxSelectEnd;
        public Action OnEnterTransformDragging;
        public Action OnExitTransformDragging;
        public Action OnEnterBoxSelecting;
        public Action OnReadBackVertexFlags;
        public Action OnExitBoxSelecting;
        public Action OnRequestNormal;
        public Func<MeshSelectMode, PlayerHoverElement> GetHoverElement;
        public Func<Vector2[]>  GetScreenPositions;
        public Func<int, int>   GetVertexOffset;
        public Func<int, bool>  IsVertexVisible;
        public Func<float>      GetViewportHeight;
        public Func<Camera>     GetCamera;
        /// <summary>
        /// パネル高さ（ピクセル）。AxisGizmo のヒットテスト・描画用に
        /// ToViewportCoord（Y=0が下）→ IMGUI 系（Y=0が上）のY反転に使う。
        /// </summary>
        public Func<float>      GetPanelHeight;
        /// <summary>毎フレーム最新の ToolContext を返すコールバック（AxisGizmo 用）。</summary>
        public Func<ToolContext> GetToolContext;

        // ================================================================
        // マグネット設定
        // ================================================================
        public bool        UseMagnet     { get; set; } = false;
        public float       MagnetRadius  { get; set; } = 0.5f;
        public FalloffType MagnetFalloff { get; set; } = FalloffType.Smooth;

        // ================================================================
        // 内部
        // ================================================================
        private readonly PlayerSelectionOps               _selectionOps;
        private          ProjectContext                    _project;

        private const float DragThreshold = 4f;
        private Vector2  _mouseDownPos;
        private bool     _shiftHeld;
        private bool     _ctrlHeld;
        private PlayerHoverElement _elemOnMouseDown;

        private Dictionary<int, HashSet<int>>     _affectedVertices = new Dictionary<int, HashSet<int>>();
        private Dictionary<int, IVertexTransform> _meshTransforms   = new Dictionary<int, IVertexTransform>();

        private AxisGizmo          _axisGizmo      = new AxisGizmo();
        private AxisGizmo.AxisType _draggingAxis   = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _hoveredAxis    = AxisGizmo.AxisType.None;
        private Vector2            _lastAxisDragPos;
        private Vector2            _lastMousePos;

        private const float GizmoHandleHitRadius = 12f;
        private const float GizmoHandleSize      = 10f;
        private const float GizmoCenterSize      = 16f;
        private const float GizmoAxisLength      = 55f;

        private enum DragMode { None, Moving, BoxSelecting }
        private DragMode _dragMode = DragMode.None;

        // ================================================================
        // 初期化
        // ================================================================
        public MoveToolHandler(PlayerSelectionOps selectionOps, ProjectContext project)
        {
            _selectionOps = selectionOps ?? throw new ArgumentNullException(nameof(selectionOps));
            _project      = project;
            _axisGizmo.ScreenOffset     = new Vector2(60, -60);
            _axisGizmo.HandleHitRadius  = GizmoHandleHitRadius;
            _axisGizmo.HandleSize       = GizmoHandleSize;
            _axisGizmo.CenterSize       = GizmoCenterSize;
            _axisGizmo.ScreenAxisLength = GizmoAxisLength;
        }

        public void SetProject(ProjectContext project) => _project = project;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================
        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var mode = _selectionOps.SelectionState?.Mode
                    ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge |
                        MeshSelectMode.Face   | MeshSelectMode.Line);

            if (GetHoverElement != null)
                _selectionOps.ApplyElementClick(GetHoverElement(mode), mods);
            else
                _selectionOps.ApplyClick(hit, mods);

            OnRequestNormal?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            _mouseDownPos = screenPos;
            _shiftHeld    = mods.Shift;
            _ctrlHeld     = mods.Ctrl;
            _dragMode     = DragMode.None;

            var mode = _selectionOps.SelectionState?.Mode
                    ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge |
                        MeshSelectMode.Face   | MeshSelectMode.Line);

            _elemOnMouseDown = GetHoverElement != null
                ? GetHoverElement(mode)
                : (hit.HasHit
                    ? new PlayerHoverElement { Kind = PlayerHoverKind.Vertex,
                          MeshIndex = hit.MeshIndex, VertexIndex = hit.VertexIndex }
                    : PlayerHoverElement.None);

            // 軸ギズモヒットテスト（最優先）
            var ctx = GetToolContext?.Invoke();
            if (ctx != null)
            {
                UpdateAffectedVertices();
                if (HasAnyAffected())
                {
                    UpdateGizmoState(ctx);
                    var axisHit = _axisGizmo.FindAxisAtScreenPos(ToImgui(screenPos), ctx);
                    if (axisHit != AxisGizmo.AxisType.None)
                    {
                        _draggingAxis    = axisHit;
                        _lastAxisDragPos = ToImgui(screenPos);
                        BeginMove();
                        _state    = axisHit == AxisGizmo.AxisType.Center
                                    ? MoveState.CenterDragging : MoveState.AxisDragging;
                        _dragMode = DragMode.Moving;
                        OnEnterTransformDragging?.Invoke();
                        return;
                    }
                }
            }

            // 要素ヒット or 選択済み → PendingAction
            UpdateAffectedVertices();
            if (_elemOnMouseDown.HasHit || HasAnyAffected())
            {
                _state    = MoveState.PendingAction;
                _dragMode = DragMode.Moving;
                return;
            }

            // 矩形選択開始
            _dragMode = DragMode.BoxSelecting;
            _state    = MoveState.Idle;
            _selectionOps.BeginBoxSelect(screenPos);
            OnEnterBoxSelecting?.Invoke();
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (_dragMode == DragMode.BoxSelecting)
            {
                _selectionOps.UpdateBoxSelect(screenPos);
                OnBoxSelectUpdate?.Invoke(_selectionOps.BoxStart, screenPos);
                OnRepaint?.Invoke();
                return;
            }

            var ctx = GetToolContext?.Invoke();

            switch (_state)
            {
                case MoveState.PendingAction:
                    if (Vector2.Distance(screenPos, _mouseDownPos) > DragThreshold)
                    {
                        // ヒット要素が未選択なら選択
                        if (_elemOnMouseDown.HasHit && !IsElemSelected(_elemOnMouseDown))
                            _selectionOps.ApplyElementClick(_elemOnMouseDown, new ModifierKeys());

                        UpdateAffectedVertices();

                        // Ctrl + 未選択 → 移動キャンセル
                        if (_ctrlHeld && !HasAnyAffected()) { _state = MoveState.Idle; return; }

                        BeginMove();
                        _state = MoveState.MovingVertices;
                        OnRepaint?.Invoke();
                    }
                    break;

                case MoveState.MovingVertices:
                case MoveState.CenterDragging:
                    if (ctx != null) ApplyFreeDelta(delta, ctx);
                    break;

                case MoveState.AxisDragging:
                    if (ctx != null)
                    {
                        Vector2 imguiPos = ToImgui(screenPos);
                        Vector2 sd = imguiPos - _lastAxisDragPos;
                        _lastAxisDragPos = imguiPos;
                        if (sd.sqrMagnitude > 0.001f)
                        {
                            UpdateGizmoState(ctx);
                            Vector3 wd = _axisGizmo.ComputeAxisDelta(sd, _draggingAxis, ctx);
                            ApplyDelta(wd);
                        }
                    }
                    break;
            }
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            if (_dragMode == DragMode.BoxSelecting)
            {
                _selectionOps.UpdateBoxSelect(screenPos);
                OnReadBackVertexFlags?.Invoke();
                CommitBoxSelect(mods);
                OnBoxSelectEnd?.Invoke();
                OnExitBoxSelecting?.Invoke();
                _dragMode = DragMode.None;
                _state    = MoveState.Idle;
                return;
            }

            bool moved = _state == MoveState.MovingVertices
                      || _state == MoveState.AxisDragging
                      || _state == MoveState.CenterDragging;
            if (moved)
            {
                EndMove();
                OnExitTransformDragging?.Invoke();
            }

            _state        = MoveState.Idle;
            _dragMode     = DragMode.None;
            _draggingAxis = AxisGizmo.AxisType.None;
        }

        // ================================================================
        // ギズモスクリーン座標取得（UIToolkit generateVisualContent から呼ぶ）
        // ================================================================

        /// <summary>
        /// AxisGizmo のスクリーン座標を返す。
        /// 選択なし・ctx null の場合は false を返す。
        /// UIToolkit の generateVisualContent で軸を描画するために使う。
        /// </summary>
        public bool TryGetGizmoScreenPositions(
            ToolContext ctx,
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            origin = xEnd = yEnd = zEnd = Vector2.zero;
            hoveredAxis = AxisGizmo.AxisType.None;
            if (ctx == null) { Debug.LogWarning("[Gizmo] ctx is null"); return false; }
            UpdateAffectedVertices();
            if (!HasAnyAffected()) { Debug.LogWarning("[Gizmo] HasAnyAffected=false"); return false; }
            UpdateGizmoState(ctx);
            Debug.Log($"[Gizmo] Center={_axisGizmo.Center} CamPos={ctx.CameraPosition} CamDist={ctx.CameraDistance:F3} PreviewRect={ctx.PreviewRect}");
            _axisGizmo.HoveredAxis  = _hoveredAxis;
            _axisGizmo.DraggingAxis = _draggingAxis;
            _axisGizmo.GetScreenPositions(ctx, out origin, out xEnd, out yEnd, out zEnd);
            Debug.Log($"[Gizmo] origin={origin} xEnd={xEnd} yEnd={yEnd} zEnd={zEnd}");
            hoveredAxis = _hoveredAxis;
            return true;
        }

        /// <summary>ポインター移動時に呼んでホバー軸を更新する。</summary>
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            _lastMousePos = ToImgui(screenPos);
            if (ctx == null || _state != MoveState.Idle) return;
            UpdateAffectedVertices();
            if (!HasAnyAffected()) return;
            UpdateGizmoState(ctx);
            var newHovered = _axisGizmo.FindAxisAtScreenPos(ToImgui(screenPos), ctx);
            if (newHovered != _hoveredAxis)
            {
                _hoveredAxis = newHovered;
                OnRepaint?.Invoke();
            }
        }

        // ================================================================
        // 内部
        // ================================================================
        private void UpdateAffectedVertices()
        {
            _affectedVertices.Clear();
            var model = _project?.CurrentModel;
            if (model == null) return;

            int ctxIdx = model.FirstDrawableMeshIndex >= 0
                ? model.FirstDrawableMeshIndex
                : (model.SelectedMeshIndices.Count > 0 ? model.SelectedMeshIndices[0] : -1);
            if (ctxIdx < 0) return;

            var mc  = model.GetMeshContext(ctxIdx);
            if (mc?.MeshObject == null) return;

            var sel      = _selectionOps.SelectionState;
            var affected = new HashSet<int>();

            foreach (var v  in sel.Vertices) affected.Add(v);
            foreach (var e  in sel.Edges)    { affected.Add(e.V1); affected.Add(e.V2); }
            foreach (var fi in sel.Faces)
                if (fi >= 0 && fi < mc.MeshObject.FaceCount)
                    foreach (var vi in mc.MeshObject.Faces[fi].VertexIndices)
                        affected.Add(vi);
            foreach (var li in sel.Lines)
                if (li >= 0 && li < mc.MeshObject.FaceCount)
                {
                    var face = mc.MeshObject.Faces[li];
                    if (face.VertexCount == 2)
                    { affected.Add(face.VertexIndices[0]); affected.Add(face.VertexIndices[1]); }
                }

            if (affected.Count > 0) _affectedVertices[ctxIdx] = affected;
        }

        private bool HasAnyAffected()
        {
            foreach (var kv in _affectedVertices)
                if (kv.Value.Count > 0) return true;
            return false;
        }

        private void UpdateGizmoState(ToolContext ctx)
        {
            var model = _project?.CurrentModel;
            Vector3 sum = Vector3.zero; int count = 0;
            foreach (var kv in _affectedVertices)
            {
                var mc = model?.GetMeshContext(kv.Key);
                if (mc?.MeshObject == null) continue;
                foreach (int vi in kv.Value)
                    if (vi >= 0 && vi < mc.MeshObject.VertexCount)
                    { sum += mc.MeshObject.Vertices[vi].Position; count++; }
            }
            _axisGizmo.Center = count > 0 ? sum / count : Vector3.zero;
        }

        private void BeginMove()
        {
            _meshTransforms.Clear();
            var model = _project?.CurrentModel;
            if (model == null) return;

            foreach (var kv in _affectedVertices)
            {
                var mc = model.GetMeshContext(kv.Key);
                if (mc?.MeshObject == null) continue;

                var startPos = (Vector3[])mc.MeshObject.Positions.Clone();
                IVertexTransform t = UseMagnet
                    ? (IVertexTransform)new MagnetMoveTransform(MagnetRadius, MagnetFalloff)
                    : new SimpleMoveTransform();
                t.Begin(mc.MeshObject, kv.Value, startPos);
                _meshTransforms[kv.Key] = t;
            }
        }

        private void ApplyFreeDelta(Vector2 screenDelta, ToolContext ctx)
        {
            UpdateGizmoState(ctx);
            Vector3 wd = _axisGizmo.ComputeFreeDelta(screenDelta, ctx);
            ApplyDelta(wd);
        }

        private void ApplyDelta(Vector3 worldDelta)
        {
            if (worldDelta == Vector3.zero) return;
            var model = _project?.CurrentModel;
            foreach (var kv in _meshTransforms)
            {
                kv.Value.Apply(worldDelta);
                var mc = model?.GetMeshContext(kv.Key);
                if (mc != null) OnSyncMeshPositions?.Invoke(mc);
            }
            OnRepaint?.Invoke();
        }

        private void EndMove()
        {
            foreach (var kv in _meshTransforms) kv.Value.End();
            _meshTransforms.Clear();
            _affectedVertices.Clear();
        }

        private bool IsElemSelected(PlayerHoverElement elem)
        {
            var sel = _selectionOps.SelectionState;
            return elem.Kind switch
            {
                PlayerHoverKind.Vertex => sel.Vertices.Contains(elem.VertexIndex),
                PlayerHoverKind.Edge   => sel.Edges.Contains(
                    new VertexPair(elem.EdgeV1, elem.EdgeV2)),
                PlayerHoverKind.Face   => sel.Faces.Contains(elem.FaceIndex),
                PlayerHoverKind.Line   => sel.Lines.Contains(elem.FaceIndex),
                _                      => false,
            };
        }

        /// <summary>
        /// ToViewportCoord（Y=0が下）の座標を IMGUI 系（Y=0が上）に変換する。
        /// AxisGizmo は GL.LoadPixelMatrix（Y=0が上）を前提に描画・判定する。
        /// </summary>
        private Vector2 ToImgui(Vector2 screenPosYDown)
        {
            float h = GetPanelHeight?.Invoke() ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }

        private void CommitBoxSelect(ModifierKeys mods)
        {
            if (GetScreenPositions == null)
            { _selectionOps.EndBoxSelect(Enumerable.Empty<int>(), mods); return; }

            var model = _project?.CurrentModel;
            var mc    = model?.FirstSelectedDrawableMesh ?? model?.FirstSelectedMeshContext;
            if (mc?.MeshObject == null)
            { _selectionOps.EndBoxSelect(Enumerable.Empty<int>(), mods); return; }

            int ctxIdx      = model.FirstDrawableMeshIndex >= 0
                              ? model.FirstDrawableMeshIndex : model.FirstMeshIndex;
            int vertexOffset = GetVertexOffset?.Invoke(ctxIdx) ?? 0;
            var rect         = _selectionOps.BoxRect;
            var inBox        = new List<int>();
            var screenPos    = GetScreenPositions();
            var verts        = mc.MeshObject.Vertices;
            float vpH        = GetViewportHeight?.Invoke() ?? 0f;

            for (int i = 0; i < verts.Count; i++)
            {
                if (IsVertexVisible != null && !IsVertexVisible(vertexOffset + i)) continue;
                if (screenPos == null || vertexOffset + i >= screenPos.Length) continue;
                float sx = screenPos[vertexOffset + i].x;
                float sy = vpH - screenPos[vertexOffset + i].y;
                if (rect.Contains(new Vector2(sx, sy), true)) inBox.Add(i);
            }

            _selectionOps.EndBoxSelect(inBox, mods);
            OnRepaint?.Invoke();
        }
    }
}
