// MoveToolHandler.cs
// 移動モードの IPlayerToolHandler 実装。
// Editor MoveTool と同等の状態機・IVertexTransform・AxisGizmo を使用する。
// Runtime/Poly_Ling_Player/View/ に配置
//
// ================================================================
// 【他ツールでの流用について】
//
// 本ハンドラは VertexMove モード専用ではなく、カテゴリ 1 の編集ツール
// (EdgeBevel / FlipFace / FaceExtrude / LineExtrude 等) の「選択・矩形選択・
// Shift/Ctrl 修飾による選択追加・Ctrl 抑止ロジック」の共通基盤としても使う。
//
// 各ツールハンドラは内部に MoveToolHandler を 1 つ参照し、以下の 2 種類のフック
// を差し込むことでツール固有の動作を乗せる:
//
//   1) OnLeftClickExtra:
//        クリック (ドラッグ閾値を超えずに離した瞬間) でツール動作を発火させたい
//        ツール用 (例: FlipFace は対象面クリックで即反転)。
//        OnLeftClick の選択処理が終わった末尾で呼ばれる。
//
//   2) OnDragStartExtra:
//        ドラッグ開始時 (PendingAction → 移動開始の直前) で「移動」の代わりに
//        ツール動作を発火させたいツール用 (例: EdgeBevel は辺ドラッグで幅調整)。
//        戻り値 true で通常の移動処理 (BeginMove + MovingVertices 遷移) を抑制する。
//
// Selection.Mode を各ツール進入時に絞る (EdgeBevel → Edge、FlipFace → Face 等)
// ことで、MoveToolHandler 内部の GetHoverElement / 矩形選択 / 単独クリック選択は
// 自動的にその要素タイプだけに応答する。要素タイプ切替は MoveToolHandler の
// 既存ロジック (mode.Has(...) 判定) がそのまま活きる。
// ================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    public class MoveToolHandler : IPlayerToolHandler
    {
        // ================================================================
        // 状態機（Editor MoveTool と同等）
        // ================================================================
        private enum MoveState { Idle, PendingAction, MovingVertices, AxisDragging, CenterDragging, ToolDragging }
        private MoveState _state = MoveState.Idle;

        // ================================================================
        // 外部注入コールバック
        // ================================================================
        public Action<MeshContext> OnSyncMeshPositions;
        public Action OnRepaint;
        public Action<Vector2, Vector2> OnBoxSelectUpdate;
        public Action OnBoxSelectEnd;
        public Action<System.Collections.Generic.List<Vector2>> OnLassoSelectUpdate;
        public Action OnLassoSelectEnd;
        public Action OnEnterTransformDragging;
        public Action OnExitTransformDragging;
        public Action OnEnterBoxSelecting;
        public Action OnReadBackVertexFlags;
        public Action OnExitBoxSelecting;
        public Action OnRequestNormal;
        public Action OnClearMouseHover;

        // ================================================================
        // ツール流用フック (EdgeBevel / FlipFace / FaceExtrude / LineExtrude 等)
        // 未設定 (null) なら MoveToolHandler は純粋な移動モードとして動作。
        // 各ツール進入時に設定、脱出時に null に戻すこと。
        // ================================================================
        /// <summary>
        /// クリック (ドラッグ閾値を超えずに離した) 完了時に呼ばれる追加フック。
        /// 引数: (クリック時のヒット要素, 修飾キー)。
        /// 用途例: FlipFace が対象面の単独クリックで即座に反転を実行するなど。
        /// 選択処理 (ApplyElementClick) は既に終わっているので、
        /// このフックはツール固有の追加動作だけを書く。
        /// </summary>
        public Action<PlayerHoverElement, ModifierKeys> OnLeftClickExtra;

        /// <summary>
        /// ドラッグ開始確定時 (閾値超え、未選択要素の選択処理も完了) に呼ばれる追加フック。
        /// 引数: (ドラッグ開始時のヒット要素, 修飾キー)。
        /// 戻り値: true を返すと通常の移動処理 (BeginMove + MovingVertices 遷移) を
        ///         抑制し、_state を ToolDragging に遷移させ、以降の OnLeftDrag /
        ///         OnLeftDragEnd は OnToolDragExtra / OnToolDragEndExtra に委譲する。
        ///         false なら通常の移動動作が発火する。
        /// 用途例: EdgeBevel は辺上ドラッグ開始でベベルセッションを開始し true を返す。
        /// </summary>
        public Func<PlayerHoverElement, ModifierKeys, bool> OnDragStartExtra;

        /// <summary>
        /// OnDragStartExtra が true を返した後のドラッグ継続時に呼ばれる。
        /// 引数: (現在スクリーン座標, 前フレームからの差分, 修飾キー)。
        /// 用途例: EdgeBevel の幅更新、FaceExtrude の押し出し距離更新。
        /// </summary>
        public Action<Vector2, Vector2, ModifierKeys> OnToolDragExtra;

        /// <summary>
        /// OnDragStartExtra が true を返した後のドラッグ終了時に呼ばれる。
        /// 引数: (終了時スクリーン座標, 修飾キー)。
        /// 用途例: EdgeBevel のベベル確定 + Undo 記録。
        /// </summary>
        public Action<Vector2, ModifierKeys> OnToolDragEndExtra;


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
        public DistanceMode MagnetDistanceMode { get; set; } = DistanceMode.Euclidean;

        // ================================================================
        // ギズモオフセット設定（エディタ版 MoveTool.DrawSettingsUI のギズモ設定に対応）
        // ================================================================

        /// <summary>ギズモのスクリーンオフセット X。</summary>
        public float GizmoScreenOffsetX
        {
            get => _axisGizmo.ScreenOffset.x;
            set => _axisGizmo.ScreenOffset = new Vector2(value, _axisGizmo.ScreenOffset.y);
        }

        /// <summary>ギズモのスクリーンオフセット Y。</summary>
        public float GizmoScreenOffsetY
        {
            get => _axisGizmo.ScreenOffset.y;
            set => _axisGizmo.ScreenOffset = new Vector2(_axisGizmo.ScreenOffset.x, value);
        }

        /// <summary>
        /// 現在の選択状態で移動対象となる頂点の総数を返す。
        /// エディタ版 MoveTool.GetTotalAffectedCount() に対応。
        /// </summary>
        public int GetTotalAffectedCount()
        {
            int total = 0;
            foreach (var kv in _affectedVertices)
                total += kv.Value.Count;
            return total;
        }

        // ================================================================
        // 内部
        // ================================================================
        private readonly PlayerSelectionOps               _selectionOps;
        private          ProjectContext                    _project;
        private          MeshUndoController               _undoController;

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

        // ================================================================
        // ドラッグ選択モード（Box / Lasso）
        // ================================================================
        public enum SelectionDragMode { Box, Lasso }
        public SelectionDragMode DragSelectMode { get; set; } = SelectionDragMode.Box;

        private enum DragMode { None, Moving, BoxSelecting, LassoSelecting }
        private DragMode _dragMode = DragMode.None;

        // ================================================================
        // マグネット半径範囲・ドラッグ指定モード
        // ================================================================
        public float MinMagnetRadius { get; set; } = 0.01f;
        public float MaxMagnetRadius { get; set; } = 1.0f;

        /// <summary>マグネット半径が変更されたときに呼ばれるコールバック（UIパネル更新用）。</summary>
        public Action<float> OnRadiusChanged;

        /// <summary>
        /// true の間、次のドラッグ操作は移動ではなくマグネット半径の設定として扱われる。
        /// ドラッグ終了後に自動的に false に戻る。
        /// </summary>
        public bool IsRadiusDragMode { get; set; } = false;

        private Vector2 _radiusDragStartPos;
        private bool    _inRadiusDrag;

        /// <summary>頂点移動確定時に、影響した各メッシュコンテキストを通知する（リモート連動用）。</summary>
        public Action<MeshContext> OnVerticesCommitted;

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
        public void SetUndoController(MeshUndoController ctrl) => _undoController = ctrl;

        // ================================================================
        // IPlayerToolHandler
        // ================================================================
        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            var before = CaptureSelectionSnapshot();

            var mode = _selectionOps.SelectionState?.Mode
                    ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge |
                        MeshSelectMode.Face   | MeshSelectMode.Line);

            var clickedElem = GetHoverElement != null
                ? GetHoverElement(mode)
                : PlayerHoverElement.None;

            if (GetHoverElement != null)
                _selectionOps.ApplyElementClick(clickedElem, mods);
            else
                _selectionOps.ApplyClick(hit, mods);

            ExpandLinkedVertices();

            OnRequestNormal?.Invoke();
            RecordSelectionChange(before, CaptureSelectionSnapshot());

            // ツール流用フック: クリック系ツール (FlipFace 等) がここで発火
            OnLeftClickExtra?.Invoke(clickedElem, mods);
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (IsRadiusDragMode)
            {
                _radiusDragStartPos = screenPos;
                _inRadiusDrag       = true;
                return;
            }
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

            // 要素ヒット → PendingAction
            UpdateAffectedVertices();
            if (_elemOnMouseDown.HasHit)
            {
                _state    = MoveState.PendingAction;
                _dragMode = DragMode.Moving;
                return;
            }

            // 矩形/投げ縄選択開始
            if (DragSelectMode == SelectionDragMode.Lasso)
            {
                _dragMode = DragMode.LassoSelecting;
                _state    = MoveState.Idle;
                _selectionOps.BeginLassoSelect(screenPos);
                OnEnterBoxSelecting?.Invoke();
            }
            else
            {
                _dragMode = DragMode.BoxSelecting;
                _state    = MoveState.Idle;
                _selectionOps.BeginBoxSelect(screenPos);
                OnEnterBoxSelecting?.Invoke();
            }
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (_inRadiusDrag)
            {
                var rdCtx = GetToolContext?.Invoke();
                if (rdCtx != null)
                {
                    float screenDist = Vector2.Distance(screenPos, _radiusDragStartPos);
                    float newRadius  = MoveScreenDistToWorldRadius(screenDist, rdCtx);
                    newRadius = Mathf.Clamp(newRadius, MinMagnetRadius, MaxMagnetRadius);
                    MagnetRadius = newRadius;
                    OnRadiusChanged?.Invoke(newRadius);
                }
                return;
            }
            if (_dragMode == DragMode.BoxSelecting)
            {
                _selectionOps.UpdateBoxSelect(screenPos);
                OnBoxSelectUpdate?.Invoke(_selectionOps.BoxStart, screenPos);
                OnRepaint?.Invoke();
                return;
            }

            if (_dragMode == DragMode.LassoSelecting)
            {
                _selectionOps.UpdateLassoSelect(screenPos);
                OnLassoSelectUpdate?.Invoke(_selectionOps.LassoPoints);
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
                        {
                            var selBefore = CaptureSelectionSnapshot();
                            _selectionOps.ApplyElementClick(_elemOnMouseDown, new ModifierKeys());
                            RecordSelectionChange(selBefore, CaptureSelectionSnapshot());
                        }

                        UpdateAffectedVertices();

                        // Ctrl + 未選択 → 移動キャンセル
                        if (_ctrlHeld && !HasAnyAffected()) { _state = MoveState.Idle; return; }

                        // ツール流用フック: ドラッグ系ツール (EdgeBevel / FaceExtrude 等) が
                        // ここで発火し、true を返したら通常の移動処理を抑制する。
                        // ツール側で独自のドラッグセッションを開始するため、
                        // 以降の OnLeftDrag / OnLeftDragEnd はツールハンドラ側で処理される想定。
                        bool suppressMove = false;
                        if (OnDragStartExtra != null)
                        {
                            var modsForHook = new ModifierKeys
                            {
                                Shift = _shiftHeld,
                                Ctrl  = _ctrlHeld,
                            };
                            suppressMove = OnDragStartExtra(_elemOnMouseDown, modsForHook);
                        }
                        if (suppressMove)
                        {
                            // ツール固有ドラッグセッション開始。以降の OnLeftDrag /
                            // OnLeftDragEnd は OnToolDragExtra / OnToolDragEndExtra に委譲。
                            _state = MoveState.ToolDragging;
                            return;
                        }

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

                case MoveState.ToolDragging:
                    // ツール流用フック: EdgeBevel / FaceExtrude 等がドラッグ中の
                    // 幅調整・押し出し量更新をここで受け取る
                    OnToolDragExtra?.Invoke(screenPos, delta, mods);
                    break;
            }
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            if (_inRadiusDrag)
            {
                _inRadiusDrag    = false;
                IsRadiusDragMode = false;
                return;
            }
            if (_dragMode == DragMode.BoxSelecting)
            {
                _selectionOps.UpdateBoxSelect(screenPos);
                OnReadBackVertexFlags?.Invoke();
                CommitBoxSelect(mods);
                OnBoxSelectEnd?.Invoke();
                OnExitBoxSelecting?.Invoke();
                _dragMode = DragMode.None;
                _state    = MoveState.Idle;
                OnClearMouseHover?.Invoke();
                return;
            }

            if (_dragMode == DragMode.LassoSelecting)
            {
                _selectionOps.UpdateLassoSelect(screenPos);
                OnReadBackVertexFlags?.Invoke();
                CommitLassoSelect(mods);
                OnLassoSelectEnd?.Invoke();
                OnExitBoxSelecting?.Invoke();
                _dragMode = DragMode.None;
                _state    = MoveState.Idle;
                OnClearMouseHover?.Invoke();
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

            // ツール流用フック: ツール固有ドラッグの終了 (Bevel 確定、Extrude 確定等)
            if (_state == MoveState.ToolDragging)
            {
                OnToolDragEndExtra?.Invoke(screenPos, mods);
            }

            _state        = MoveState.Idle;
            _dragMode     = DragMode.None;
            _draggingAxis = AxisGizmo.AxisType.None;
            OnClearMouseHover?.Invoke();
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
            if (ctx == null) return false;
            UpdateAffectedVertices();
            if (!HasAnyAffected()) return false;
            UpdateGizmoState(ctx);
            _axisGizmo.HoveredAxis  = _hoveredAxis;
            _axisGizmo.DraggingAxis = _draggingAxis;
            _axisGizmo.GetScreenPositions(ctx, out origin, out xEnd, out yEnd, out zEnd);
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
        // ================================================================
        // 選択変更 Undo ヘルパー
        // ================================================================

        private SelectionSnapshot CaptureSelectionSnapshot()
        {
            var sel = _selectionOps.SelectionState;
            if (sel == null) return null;
            return new SelectionSnapshot
            {
                Mode     = sel.Mode,
                Vertices = new HashSet<int>(sel.Vertices),
                Edges    = new HashSet<VertexPair>(sel.Edges),
                Faces    = new HashSet<int>(sel.Faces),
                Lines    = new HashSet<int>(sel.Lines),
            };
        }

        private void RecordSelectionChange(SelectionSnapshot before, SelectionSnapshot after)
        {
            if (_undoController == null || before == null || after == null) return;
            if (!before.IsDifferentFrom(after)) return;
            var model = _project?.CurrentModel;
            if (model != null)
                _undoController.MeshUndoContext.ParentModelContext = model;
            var record = new SelectionChangeRecord(before, after);
            _undoController.FocusVertexEdit();
            UnityEngine.Debug.Log(
                $"[UndoDbg] Push SelectionChangeRecord (model={model?.Name ?? "<null>"}, " +
                $"beforeV={before.Vertices?.Count ?? 0}, afterV={after.Vertices?.Count ?? 0}, " +
                $"beforeE={before.Edges?.Count ?? 0}, afterE={after.Edges?.Count ?? 0}, " +
                $"mode={after.Mode})");
            _undoController.VertexEditStack.Record(record, "選択変更");
            UnityEngine.Debug.Log(
                $"[UndoDbg]   after Record: VertexEdit.Undo={_undoController.VertexEditStack.UndoCount}, " +
                $"VertexEdit.Pending={_undoController.VertexEditStack.PendingCount}, " +
                $"MeshList.Undo={_undoController.MeshListStack.UndoCount}, " +
                $"MeshList.Pending={_undoController.MeshListStack.PendingCount}");
        }

        private void UpdateAffectedVertices()
        {
            _affectedVertices.Clear();
            var model = _project?.CurrentModel;
            if (model == null) return;

            int ctxIdx = model.FirstMeshIndex >= 0
                ? model.FirstMeshIndex
                : (model.SelectedDrawableMeshIndices.Count > 0 ? model.SelectedDrawableMeshIndices[0] : -1);
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
                    ? (IVertexTransform)new MagnetMoveTransform(MagnetRadius, MagnetFalloff, MagnetDistanceMode)
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
            if (_undoController != null)
            {
                var model = _project?.CurrentModel;
                if (model != null)
                {
                    var entries = new List<MeshMoveEntry>();
                    foreach (var kv in _meshTransforms)
                    {
                        var mc = model.GetMeshContext(kv.Key);
                        if (mc?.MeshObject == null) continue;
                        var indices = kv.Value.GetAffectedIndices();
                        var oldPos  = kv.Value.GetOriginalPositions();
                        var newPos  = kv.Value.GetCurrentPositions();
                        if (indices.Length == 0) continue;
                        entries.Add(new MeshMoveEntry
                        {
                            MeshContextIndex = kv.Key,
                            Indices          = indices,
                            OldPositions     = oldPos,
                            NewPositions     = newPos,
                        });
                    }
                    if (entries.Count > 0)
                    {
                        _undoController.MeshUndoContext.ParentModelContext = model;
                        var record = new MultiMeshVertexMoveRecord(entries.ToArray());
                        _undoController.FocusVertexEdit();
                        int totalVerts = 0;
                        foreach (var e in entries) totalVerts += e.Indices?.Length ?? 0;
                        UnityEngine.Debug.Log(
                            $"[UndoDbg] Push MultiMeshVertexMoveRecord (model={model.Name}, " +
                            $"entries={entries.Count}, totalVerts={totalVerts})");
                        _undoController.VertexEditStack.Record(record, "Move Vertices");
                        UnityEngine.Debug.Log(
                            $"[UndoDbg]   after Record: VertexEdit.Undo={_undoController.VertexEditStack.UndoCount}, " +
                            $"VertexEdit.Pending={_undoController.VertexEditStack.PendingCount}, " +
                            $"MeshList.Undo={_undoController.MeshListStack.UndoCount}, " +
                            $"MeshList.Pending={_undoController.MeshListStack.PendingCount}");

                        // リモート連動: 影響した各メッシュを通知する。
                        if (OnVerticesCommitted != null)
                        {
                            UnityEngine.Debug.Log($"[EditSync] OnVerticesCommitted fire: entries={entries.Count}");
                            foreach (var e in entries)
                            {
                                var emc = model.GetMeshContext(e.MeshContextIndex);
                                if (emc?.MeshObject != null) OnVerticesCommitted.Invoke(emc);
                            }
                        }
                    }
                }
            }
            foreach (var kv in _meshTransforms) kv.Value.End();
            _meshTransforms.Clear();
            _affectedVertices.Clear();
        }

        private void ExpandLinkedVertices()
        {
            var sel = _selectionOps.SelectionState;
            if (sel == null) return;
            var meshObject = _project?.CurrentModel?.FirstSelectedMeshContext?.MeshObject;
            if (meshObject == null) return;

            foreach (var edge in sel.Edges)
            {
                sel.Vertices.Add(edge.V1);
                sel.Vertices.Add(edge.V2);
            }
            foreach (var faceIdx in sel.Faces)
            {
                if (faceIdx >= 0 && faceIdx < meshObject.FaceCount)
                    foreach (var vIdx in meshObject.Faces[faceIdx].VertexIndices)
                        sel.Vertices.Add(vIdx);
            }
            foreach (var lineIdx in sel.Lines)
            {
                if (lineIdx >= 0 && lineIdx < meshObject.FaceCount)
                {
                    var face = meshObject.Faces[lineIdx];
                    if (face.VertexCount == 2)
                    {
                        sel.Vertices.Add(face.VertexIndices[0]);
                        sel.Vertices.Add(face.VertexIndices[1]);
                    }
                }
            }
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

        private float MoveScreenDistToWorldRadius(float screenDist, ToolContext ctx)
        {
            Vector3 target   = ctx.CameraTarget;
            Vector3 camRight = Vector3.Cross(
                (ctx.CameraTarget - ctx.CameraPosition).normalized, Vector3.up).normalized;
            if (camRight.sqrMagnitude < 0.001f) camRight = Vector3.right;
            Vector2 sp1 = ctx.WorldToScreenPos(target,           ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            Vector2 sp2 = ctx.WorldToScreenPos(target + camRight, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            float pxPerUnit = Vector2.Distance(sp1, sp2);
            if (pxPerUnit < 0.001f) return screenDist * 0.01f;
            return screenDist / pxPerUnit;
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
            var mc    = model?.FirstDrawableMeshContext ?? model?.FirstSelectedMeshContext;
            if (mc?.MeshObject == null)
            { _selectionOps.EndBoxSelect(Enumerable.Empty<int>(), mods); return; }

            int ctxIdx      = model.FirstMeshIndex >= 0
                              ? model.FirstMeshIndex : model.FirstMeshIndex;
            int vertexOffset = GetVertexOffset?.Invoke(ctxIdx) ?? 0;
            var rect         = _selectionOps.BoxRect;
            var screenPos    = GetScreenPositions();
            var mo           = mc.MeshObject;
            float vpH        = GetViewportHeight?.Invoke() ?? 0f;

            // スクリーン座標取得ヘルパー
            Func<int, Vector2> vertexScreen = (i) =>
            {
                if (screenPos == null || vertexOffset + i >= screenPos.Length)
                    return new Vector2(-10000, -10000);
                return new Vector2(screenPos[vertexOffset + i].x, vpH - screenPos[vertexOffset + i].y);
            };

            var selBefore = CaptureSelectionSnapshot();

            bool additive = mods.Shift || mods.Ctrl;
            if (!additive)
            {
                mc.Selection.ClearAll();
            }

            var mode = _selectionOps.SelectionState?.Mode
                    ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge | MeshSelectMode.Face | MeshSelectMode.Line);

            // 頂点選択
            var inBox = new List<int>();
            if (mode.Has(MeshSelectMode.Vertex))
            {
                for (int i = 0; i < mo.Vertices.Count; i++)
                {
                    if (IsVertexVisible != null && !IsVertexVisible(vertexOffset + i)) continue;
                    if (rect.Contains(vertexScreen(i), true)) inBox.Add(i);
                }
            }

            // 辺選択
            // GPU 計算済みの頂点可視フラグ (IsVertexVisible) で両端頂点を判定し、
            // 表面の面に属さない頂点から成る辺は除外する。
            // 厳密には「両端が可視でも辺自体は裏を通る」ケースも稀に拾うが、
            // GPU 側でも辺単位の可視判定は無く頂点ベースなので同じ挙動で OK。
            if (mode.Has(MeshSelectMode.Edge))
            {
                for (int fi = 0; fi < mo.FaceCount; fi++)
                {
                    var face = mo.Faces[fi];
                    if (face.VertexCount < 2) continue;
                    for (int ei = 0; ei < face.VertexCount; ei++)
                    {
                        int v1 = face.VertexIndices[ei];
                        int v2 = face.VertexIndices[(ei + 1) % face.VertexCount];
                        if (IsVertexVisible != null
                            && (!IsVertexVisible(vertexOffset + v1) || !IsVertexVisible(vertexOffset + v2)))
                            continue;
                        if (rect.Contains(vertexScreen(v1), true) &&
                            rect.Contains(vertexScreen(v2), true))
                        {
                            mc.Selection.SelectEdge(v1, v2, true);
                        }
                    }
                }
            }

            // 面選択
            // 面の全頂点が IsVertexVisible で可視のとき表面扱い (裏側に属する面は除外)。
            if (mode.Has(MeshSelectMode.Face))
            {
                for (int fi = 0; fi < mo.FaceCount; fi++)
                {
                    var face = mo.Faces[fi];
                    if (face.VertexCount < 3) continue;
                    bool allIn = true;
                    foreach (int vi in face.VertexIndices)
                    {
                        if (IsVertexVisible != null && !IsVertexVisible(vertexOffset + vi)) { allIn = false; break; }
                        if (!rect.Contains(vertexScreen(vi), true)) { allIn = false; break; }
                    }
                    if (allIn) mc.Selection.SelectFace(fi, true);
                }
            }

            // 線分選択
            // 両端頂点が可視のときのみ選択対象 (辺と同じ扱い)。
            if (mode.Has(MeshSelectMode.Line))
            {
                for (int fi = 0; fi < mo.FaceCount; fi++)
                {
                    var face = mo.Faces[fi];
                    if (face.VertexCount != 2) continue;
                    int v1 = face.VertexIndices[0];
                    int v2 = face.VertexIndices[1];
                    if (IsVertexVisible != null
                        && (!IsVertexVisible(vertexOffset + v1) || !IsVertexVisible(vertexOffset + v2)))
                        continue;
                    if (rect.Contains(vertexScreen(v1), true) &&
                        rect.Contains(vertexScreen(v2), true))
                    {
                        mc.Selection.SelectLine(fi, true);
                    }
                }
            }

            _selectionOps.EndBoxSelect(inBox, mods);
            ExpandLinkedVertices();
            RecordSelectionChange(selBefore, CaptureSelectionSnapshot());
            OnRepaint?.Invoke();
        }

        private void CommitLassoSelect(ModifierKeys mods)
        {
            var lasso = _selectionOps.LassoPoints;
            if (lasso.Count < 3)
            { _selectionOps.EndLassoSelect(Enumerable.Empty<int>(), mods); return; }

            if (GetScreenPositions == null)
            { _selectionOps.EndLassoSelect(Enumerable.Empty<int>(), mods); return; }

            var model = _project?.CurrentModel;
            var mc    = model?.FirstDrawableMeshContext ?? model?.FirstSelectedMeshContext;
            if (mc?.MeshObject == null)
            { _selectionOps.EndLassoSelect(Enumerable.Empty<int>(), mods); return; }

            int ctxIdx       = model.FirstMeshIndex >= 0
                               ? model.FirstMeshIndex : model.FirstMeshIndex;
            int vertexOffset = GetVertexOffset?.Invoke(ctxIdx) ?? 0;
            var screenPos    = GetScreenPositions();
            var mo           = mc.MeshObject;
            float vpH        = GetViewportHeight?.Invoke() ?? 0f;

            Func<int, Vector2> vertexScreen = (i) =>
            {
                if (screenPos == null || vertexOffset + i >= screenPos.Length)
                    return new Vector2(-10000, -10000);
                return new Vector2(screenPos[vertexOffset + i].x, vpH - screenPos[vertexOffset + i].y);
            };

            // 座標系の確認：
            // GetScreenPositions() は NDC から screenY = (1 - ndcY) * height で計算 → UIToolkit Y（Y=0上）
            // vertexScreen は vpH - UIToolkitY → GPU Y（Y=0下）
            // LassoPoints は ToViewportCoord()（h - local.y）→ GPU Y（Y=0下）
            // → vertexScreen と LassoPoints は同じ GPU Y。変換不要。
            var lassoGPU = lasso;

            var selBefore = CaptureSelectionSnapshot();

            bool additive = mods.Shift || mods.Ctrl;
            if (!additive)
                mc.Selection.ClearAll();

            var mode = _selectionOps.SelectionState?.Mode
                    ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge | MeshSelectMode.Face | MeshSelectMode.Line);

            // 頂点選択
            var inLasso = new List<int>();
            if (mode.Has(MeshSelectMode.Vertex))
            {
                for (int i = 0; i < mo.Vertices.Count; i++)
                {
                    if (IsVertexVisible != null && !IsVertexVisible(vertexOffset + i)) continue;
                    if (IsPointInLasso(vertexScreen(i), lassoGPU)) inLasso.Add(i);
                }
            }

            // 辺選択
            // GPU 計算済みの頂点可視フラグ (IsVertexVisible) で両端頂点を判定し、
            // 表面の面に属さない頂点から成る辺は除外する。矩形選択と同じ扱い。
            if (mode.Has(MeshSelectMode.Edge))
            {
                for (int fi = 0; fi < mo.FaceCount; fi++)
                {
                    var face = mo.Faces[fi];
                    if (face.VertexCount < 2) continue;
                    for (int ei = 0; ei < face.VertexCount; ei++)
                    {
                        int v1 = face.VertexIndices[ei];
                        int v2 = face.VertexIndices[(ei + 1) % face.VertexCount];
                        if (IsVertexVisible != null
                            && (!IsVertexVisible(vertexOffset + v1) || !IsVertexVisible(vertexOffset + v2)))
                            continue;
                        if (IsPointInLasso(vertexScreen(v1), lassoGPU) &&
                            IsPointInLasso(vertexScreen(v2), lassoGPU))
                        {
                            mc.Selection.SelectEdge(v1, v2, true);
                        }
                    }
                }
            }

            // 面選択
            // 面の全頂点が IsVertexVisible で可視のとき表面扱い (裏側に属する面は除外)。
            if (mode.Has(MeshSelectMode.Face))
            {
                for (int fi = 0; fi < mo.FaceCount; fi++)
                {
                    var face = mo.Faces[fi];
                    if (face.VertexCount < 3) continue;
                    bool allIn = true;
                    foreach (int vi in face.VertexIndices)
                    {
                        if (IsVertexVisible != null && !IsVertexVisible(vertexOffset + vi)) { allIn = false; break; }
                        if (!IsPointInLasso(vertexScreen(vi), lassoGPU)) { allIn = false; break; }
                    }
                    if (allIn) mc.Selection.SelectFace(fi, true);
                }
            }

            // 線分選択
            // 両端頂点が可視のときのみ選択対象 (辺と同じ扱い)。
            if (mode.Has(MeshSelectMode.Line))
            {
                for (int fi = 0; fi < mo.FaceCount; fi++)
                {
                    var face = mo.Faces[fi];
                    if (face.VertexCount != 2) continue;
                    int v1 = face.VertexIndices[0];
                    int v2 = face.VertexIndices[1];
                    if (IsVertexVisible != null
                        && (!IsVertexVisible(vertexOffset + v1) || !IsVertexVisible(vertexOffset + v2)))
                        continue;
                    if (IsPointInLasso(vertexScreen(v1), lassoGPU) &&
                        IsPointInLasso(vertexScreen(v2), lassoGPU))
                    {
                        mc.Selection.SelectLine(fi, true);
                    }
                }
            }

            _selectionOps.EndLassoSelect(inLasso, mods);
            ExpandLinkedVertices();
            RecordSelectionChange(selBefore, CaptureSelectionSnapshot());
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// Ray Casting アルゴリズムによる投げ縄内外判定。
        /// エディタ側 PolyLing_Input.IsPointInLasso と同一実装。
        /// </summary>
        private static bool IsPointInLasso(Vector2 point, System.Collections.Generic.List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;
            bool inside = false;
            int count = polygon.Count;
            int j = count - 1;
            for (int i = 0; i < count; i++)
            {
                if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                    point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) /
                              (polygon[j].y - polygon[i].y) + polygon[i].x)
                {
                    inside = !inside;
                }
                j = i;
            }
            return inside;
        }
    }
}
