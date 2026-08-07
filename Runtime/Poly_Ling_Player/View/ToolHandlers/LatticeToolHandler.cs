// LatticeToolHandler.cs
// 格子変形（Metasequoia の「格子」に相当）を Player へ橋渡しする
// IPlayerToolHandler 実装。
//
// 【状態機】
//   Idle ──「格子変形開始」──▶ Placement ──「変形開始」──▶ Deform
//     ▲                            │                        │
//     └────── 取消 / 適用 ──────────┴────────────────────────┘
//
//   Placement では格子の位置・大きさ・分割数だけを決める。メッシュは変形しない。
//   Deform では格子制御点を選択し、移動 / 拡大縮小 / 回転してメッシュを再計算する。
//
// 【Deform 中のギズモはサブモードで切り替える】
//   GizmoData（PlayerViewportPanel）は矢印 / キューブ / リングを排他的にしか
//   描画できないため、3 種を同時には出さない。PrimitivePlaceToolHandler と同じ方式。
//
// 【拡大縮小 / 回転は開始位置からの絶対計算】
//   ドラッグ開始時に選択制御点の位置と重心を記録し、毎フレームそこから計算し直す。
//   フレーム差分を積み上げないため、ドラッグを往復させても倍率や角度が縮まない。
//
// 【変形の中身は持たない】
//   座標往復（メッシュローカル ⇔ ワールド ⇔ 作業軸ローカル）、開始位置の記録、
//   絶対計算、Revert、Undo エントリ生成は DeformApplier が持つ。
//   補間は LatticeDeformer / ILatticeInterpolator が持つ。
//   本クラスは状態遷移・制御点の選択・ギズモ操作だけを面倒みる。
//
// 【格子フレームは作業軸そのもの】
//   制御点は作業軸ローカル座標で持つ。作業軸を動かせば格子ごとメッシュに対して
//   動く。これが仕様の「格子全体の移動・拡大縮小・回転」にあたる。
//   Base と Current は LatticeGrid が分離して持つため、配置操作が変形として
//   焼き付くことはない。
//
// 【制御点のヒットテストは CPU で行う】
//   制御点は GPU バッファに存在しないデータで、AxisGizmo と同じ扱いになる。
//   メッシュ頂点・辺の CPU ヒットテスト禁止規約には該当しない。
//
// 【座標系】
//   ・IPlayerToolHandler が受け取る screenPos … Y=0 が下
//   ・ctx.WorldToScreen が返す座標          … Y=0 が上（IMGUI 系）
//   両者の往復は ToImgui / ToHandlerScreen に集約する。
//   オーバーレイへ渡す座標は screenPos と同じ Y=0 下。
//
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Tools;
using Poly_Ling.Tools.Deformers;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;

namespace Poly_Ling.Player
{
    /// <summary>格子変形ハンドラ。</summary>
    public class LatticeToolHandler : IPlayerToolHandler, IPlayerGizmoProvider
    {
        /// <summary>Deform 中の格子点ギズモのサブモード。</summary>
        public enum PointGizmoMode
        {
            /// <summary>選択格子点を平行移動する。</summary>
            Move,
            /// <summary>選択格子点を重心基準で拡大縮小する。</summary>
            Scale,
            /// <summary>選択格子点を重心基準で回転する。</summary>
            Rotate
        }

        /// <summary>格子変形の状態。</summary>
        public enum LatticeState
        {
            /// <summary>未開始。メッシュ頂点を選んでから開始する。</summary>
            Idle,
            /// <summary>格子配置中。メッシュは変形しない。</summary>
            Placement,
            /// <summary>格子変形中。制御点の移動がメッシュへ反映される。</summary>
            Deform
        }

        // ================================================================
        // 外部コールバック（Viewer から設定）
        // ================================================================

        public Func<ToolContext> GetToolContext;
        public Func<float>       GetPanelHeight;
        public Action            OnRepaint;

        /// <summary>格子フレームになる作業軸。null なら何もしない。</summary>
        public Func<WorkAxisContext> GetWorkAxis;

        /// <summary>対象モデル。</summary>
        public Func<ModelContext> GetModel;

        /// <summary>頂点位置を GPU へ同期する。メッシュごとに呼ばれる。</summary>
        public Action<Poly_Ling.Data.MeshContext> OnSyncMeshPositions;

        /// <summary>状態・格子・選択が変わったときに呼ばれる。パネル更新に使う。</summary>
        public Action OnStateChanged;

        /// <summary>格子オーバーレイの再描画要求。</summary>
        public Action OnRefreshOverlay;

        /// <summary>矩形選択の枠表示。(start, end) はスクリーン座標（Y=0 下）。</summary>
        public Action<Vector2, Vector2> OnBoxSelectUpdate;

        /// <summary>矩形選択の枠を消す。</summary>
        public Action OnBoxSelectEnd;

        /// <summary>適用完了後に呼ばれる。パネル通知に使う。</summary>
        public Action OnApplyCompleted;

        // ================================================================
        // 設定
        // ================================================================

        private MeshUndoController _undoController;
        public void SetUndoController(MeshUndoController ctrl) { _undoController = ctrl; }

        /// <summary>制御点のヒット判定半径（ピクセル）。</summary>
        public float PointHitRadius { get; set; } = 10f;

        // ================================================================
        // 状態
        // ================================================================

        private readonly DeformApplier   _applier  = new DeformApplier();
        private readonly LatticeDeformer _deformer = new LatticeDeformer();

        /// <summary>現在の状態。</summary>
        public LatticeState State { get; private set; } = LatticeState.Idle;

        /// <summary>格子データ。パネルが分割数の表示に使う。</summary>
        public LatticeGrid Grid => _deformer.Grid;

        /// <summary>変形対象の頂点数。未開始は 0。</summary>
        public int AffectedCount => _applier.AffectedCount;

        /// <summary>選択中の制御点数。</summary>
        public int SelectedPointCount => _selectedPoints.Count;

        /// <summary>制御点の総数。未構築は 0。</summary>
        public int ControlPointCount => Grid.IsBuilt ? Grid.ControlPointCount : 0;

        // 格子制御点の選択。メッシュ頂点の SelectionState とは別に持つ。
        private readonly HashSet<int> _selectedPoints = new HashSet<int>();

        private int _hoverPoint = -1;

        // ギズモ（Deform 中の制御点操作用）。格子点そのものが基準なのでずらさない。
        private readonly AxisGizmo       _axisGizmo = new AxisGizmo { ScreenOffset = Vector2.zero };
        private readonly RotateRingGizmo _ringGizmo = new RotateRingGizmo();

        private AxisGizmo.AxisType _hoverAxis = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _dragAxis  = AxisGizmo.AxisType.None;

        private PointGizmoMode _pointMode = PointGizmoMode.Move;

        /// <summary>
        /// Deform 中のギズモのサブモード。切り替えるとホバー・ドラッグ状態は破棄する。
        /// </summary>
        public PointGizmoMode Mode
        {
            get => _pointMode;
            set
            {
                if (_pointMode == value) return;
                _pointMode = value;
                EndPointDrag();
                _hoverAxis = AxisGizmo.AxisType.None;
                NotifyChanged();
                OnRepaint?.Invoke();
            }
        }

        // 拡大縮小 / 回転はドラッグ開始時の位置から絶対計算する。
        // キーは制御点インデックス、値は開始位置（作業軸ローカル）。
        private readonly Dictionary<int, Vector3> _dragStartPoints = new Dictionary<int, Vector3>();

        // ドラッグ開始時の選択制御点の重心（作業軸ローカル）。拡大縮小 / 回転の基準点。
        private Vector3 _dragPivotLocal;

        // 矩形選択
        private bool    _boxSelecting;
        private bool    _boxAdditive;
        private Vector2 _boxStart;

        // ================================================================
        // 状態遷移
        // ================================================================

        /// <summary>
        /// 格子配置を開始する。選択メッシュ頂点が 0 個なら false。
        /// 対象頂点の開始位置を記録し、その AABB へ格子を合わせる。
        /// この時点でメッシュは変形しない。
        /// </summary>
        public bool BeginPlacement()
        {
            if (State != LatticeState.Idle) return false;

            var model = GetModel?.Invoke();
            var axis  = GetWorkAxis?.Invoke();
            if (model == null || axis == null) return false;

            _applier.Reset();
            if (!_applier.Begin(model, axis)) return false;

            if (!_deformer.FitToSelection(_applier.Context))
            {
                _applier.Reset();
                return false;
            }

            _selectedPoints.Clear();
            _hoverPoint = -1;
            State = LatticeState.Placement;

            NotifyChanged();
            return true;
        }

        /// <summary>
        /// 選択フィット。対象頂点を取り直し、その AABB へ格子を合わせ直す。
        /// 作業軸を動かした後はこれを押して合わせ直す。
        /// </summary>
        public bool FitToSelection()
        {
            if (State != LatticeState.Placement) return false;

            var model = GetModel?.Invoke();
            var axis  = GetWorkAxis?.Invoke();
            if (model == null || axis == null) return false;

            _applier.Reset();
            if (!_applier.Begin(model, axis))
            {
                // 選択が無くなった。配置を続けられないので Idle へ戻す。
                EndSession();
                return false;
            }

            bool ok = _deformer.FitToSelection(_applier.Context);
            _selectedPoints.Clear();
            _hoverPoint = -1;

            NotifyChanged();
            return ok;
        }

        /// <summary>
        /// 分割数を変更する。Placement 中のみ受け付ける（変形中の変更は禁止）。
        /// </summary>
        public bool SetCells(int x, int y, int z)
        {
            if (State != LatticeState.Placement) return false;
            if (!_deformer.SetCells(x, y, z)) return false;

            _selectedPoints.Clear();
            _hoverPoint = -1;

            NotifyChanged();
            return true;
        }

        /// <summary>
        /// 格子の中心と大きさを設定する。格子全体の移動・拡大縮小にあたる。
        /// 制御点が作り直されるため、分割数の変更と同じく Placement 中のみ受け付ける。
        /// </summary>
        public bool SetBounds(Vector3 center, Vector3 size)
        {
            if (State != LatticeState.Placement) return false;

            _deformer.SetBounds(center, size);

            _selectedPoints.Clear();
            _hoverPoint = -1;

            NotifyChanged();
            return true;
        }

        /// <summary>
        /// 格子変形へ移る。この瞬間の格子が基準格子になる。
        /// </summary>
        public bool BeginDeform()
        {
            if (State != LatticeState.Placement || !Grid.IsBuilt) return false;

            State = LatticeState.Deform;
            GetToolContext?.Invoke()?.EnterTransformDragging?.Invoke();

            // 無変形の状態で一度通しておく。結果は開始時と同じ位置になる。
            ApplyDeform();

            NotifyChanged();
            return true;
        }

        /// <summary>
        /// 格子フレーム（作業軸）が動いたときに呼ぶ。
        /// 制御点は作業軸ローカルで持つため、軸が動くと格子ごとメッシュに対して動く。
        /// Deform 中はメッシュを計算し直し、配置中は格子の表示だけ更新する。
        /// </summary>
        public void OnFrameChanged()
        {
            if (State == LatticeState.Idle) return;

            if (State == LatticeState.Deform) ApplyDeform();
            OnRefreshOverlay?.Invoke();
        }

        /// <summary>
        /// 制御点を基準位置へ戻す。格子の範囲と分割数は保つ。
        /// </summary>
        public void ResetDeformation()
        {
            if (State != LatticeState.Deform) return;

            _deformer.ResetDeformation();
            ApplyDeform();
            NotifyChanged();
        }

        /// <summary>
        /// 変形を確定して Undo に記録する。格子変形ひとまとまりで 1 回の Undo になる。
        /// </summary>
        public void Commit()
        {
            if (State == LatticeState.Idle) return;

            if (State == LatticeState.Deform)
            {
                var entries = _applier.BuildUndoEntries();

                if (entries.Length > 0 && _undoController != null)
                {
                    _undoController.FocusVertexEdit();
                    var record = new MultiMeshVertexMoveRecord(entries);
                    _undoController.VertexEditStack.Record(record, "Lattice Deform");
                }

                // VertexOffsets の基準を現在位置へ追従させる。
                _applier.SyncOriginalPositions();
            }

            EndSession();
            OnApplyCompleted?.Invoke();
        }

        /// <summary>
        /// 変形を捨てて開始前の頂点位置へ戻す。Placement 中は頂点を触っていないので
        /// 格子を捨てるだけ。
        /// </summary>
        public void Cancel()
        {
            if (State == LatticeState.Idle) return;

            if (State == LatticeState.Deform)
            {
                _applier.Revert();
                SyncMeshes();
            }

            EndSession();
        }

        /// <summary>
        /// セッションを終了して Idle へ戻す。頂点位置には触れない
        /// （確定・巻き戻しは呼び出し側が済ませておくこと）。
        /// </summary>
        private void EndSession()
        {
            bool wasDeform = State == LatticeState.Deform;

            _applier.Reset();
            _deformer.ResetDeformation();

            EndPointDrag();

            _selectedPoints.Clear();
            _hoverPoint   = -1;
            _hoverAxis    = AxisGizmo.AxisType.None;
            _boxSelecting = false;

            State = LatticeState.Idle;

            if (wasDeform) GetToolContext?.Invoke()?.ExitTransformDragging?.Invoke();

            OnBoxSelectEnd?.Invoke();
            NotifyChanged();
            OnRepaint?.Invoke();
        }

        // ================================================================
        // 変形の実行
        // ================================================================

        /// <summary>
        /// 現在の格子でメッシュを計算し直す。DeformApplier が開始位置から
        /// 絶対計算するため、何度呼んでも誤差は蓄積しない。
        /// </summary>
        private void ApplyDeform()
        {
            if (State != LatticeState.Deform) return;

            _applier.Apply(_deformer);
            SyncMeshes();
            OnRepaint?.Invoke();
        }

        /// <summary>変形した全メッシュを GPU へ同期する。</summary>
        private void SyncMeshes()
        {
            var model = GetModel?.Invoke();
            if (model == null || OnSyncMeshPositions == null) return;

            foreach (int idx in model.SelectedDrawableMeshIndices)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.MeshObject != null) OnSyncMeshPositions(mc);
            }
        }

        private void NotifyChanged()
        {
            OnStateChanged?.Invoke();
            OnRefreshOverlay?.Invoke();
        }

        // ================================================================
        // IPlayerToolHandler
        // ================================================================

        /// <summary>
        /// 制御点のクリック選択。Ctrl で追加・解除、外したら全解除。
        /// Placement 中は制御点を選べない（配置は数値と作業軸で行う）。
        /// </summary>
        public void OnLeftClick(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            if (State != LatticeState.Deform) return;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;

            int idx = PickControlPoint(screenPos, ctx);

            if (idx < 0)
            {
                if (!mods.Ctrl) _selectedPoints.Clear();
            }
            else if (mods.Ctrl)
            {
                if (!_selectedPoints.Remove(idx)) _selectedPoints.Add(idx);
            }
            else
            {
                _selectedPoints.Clear();
                _selectedPoints.Add(idx);
            }

            NotifyChanged();
            OnRepaint?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            _dragAxis     = AxisGizmo.AxisType.None;
            _boxSelecting = false;

            if (State != LatticeState.Deform) return;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;

            // ギズモ上から始まったら制御点の操作、そうでなければ矩形選択。
            if (TrySyncGizmo(ctx) && TryBeginPointDrag(ctx, screenPos)) return;

            _boxSelecting = true;
            _boxAdditive  = mods.Ctrl;
            _boxStart     = screenPos;
            OnBoxSelectUpdate?.Invoke(_boxStart, screenPos);
        }

        public void OnLeftDrag(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (State != LatticeState.Deform) return;

            if (_boxSelecting)
            {
                OnBoxSelectUpdate?.Invoke(_boxStart, screenPos);
                OnRepaint?.Invoke();
                return;
            }

            if (_dragAxis == AxisGizmo.AxisType.None) return;

            var ctx  = GetToolContext?.Invoke();
            var axis = GetWorkAxis?.Invoke();
            if (ctx == null || axis == null || !TrySyncGizmo(ctx)) return;

            switch (_pointMode)
            {
                case PointGizmoMode.Move:   DragMovePoints(delta, ctx, axis); break;
                case PointGizmoMode.Scale:  DragScalePoints(screenPos);       break;
                case PointGizmoMode.Rotate: DragRotatePoints(screenPos);      break;
            }

            ApplyDeform();
            OnRefreshOverlay?.Invoke();
        }

        // ================================================================
        // サブモード別のドラッグ処理
        // ================================================================

        /// <summary>選択制御点を平行移動する。フレーム差分をそのまま足す。</summary>
        private void DragMovePoints(Vector2 delta, ToolContext ctx, WorkAxisContext axis)
        {
            // delta はパネルの ToViewportCoord 系（+Y が画面上）。
            // ComputeFreeDelta はこの系をそのまま要求するが、ComputeAxisDelta は
            // WorldToScreenPos 系（+Y が画面下）を要求するため Y を反転して渡す。
            Vector3 worldDelta = (_dragAxis == AxisGizmo.AxisType.Center)
                ? _axisGizmo.ComputeFreeDelta(delta, ctx)
                : _axisGizmo.ComputeAxisDelta(new Vector2(delta.x, -delta.y), _dragAxis, ctx);

            if (worldDelta == Vector3.zero) return;

            // 制御点は作業軸ローカル座標。ワールド差分をその向きへ戻してから足す。
            Vector3 localDelta = axis.WorldToLocalDirection(worldDelta);

            foreach (int i in _selectedPoints)
                Grid.SetCurrent(i, Grid.GetCurrent(i) + localDelta);
        }

        /// <summary>
        /// 選択制御点を重心基準で拡大縮小する。ドラッグ開始位置から毎回計算し直す。
        /// </summary>
        private void DragScalePoints(Vector2 screenPos)
        {
            if (_dragStartPoints.Count == 0) return;

            float f = _axisGizmo.ComputeScaleFactor(screenPos);

            Vector3 s = Vector3.one;
            switch (_dragAxis)
            {
                case AxisGizmo.AxisType.Center: s = new Vector3(f, f, f); break;
                case AxisGizmo.AxisType.X:      s.x = f; break;
                case AxisGizmo.AxisType.Y:      s.y = f; break;
                case AxisGizmo.AxisType.Z:      s.z = f; break;
                default: return;
            }

            foreach (var kv in _dragStartPoints)
            {
                Vector3 d = kv.Value - _dragPivotLocal;
                Grid.SetCurrent(kv.Key, _dragPivotLocal
                    + new Vector3(d.x * s.x, d.y * s.y, d.z * s.z));
            }
        }

        /// <summary>
        /// 選択制御点を重心基準で回転する。ドラッグ開始位置から毎回計算し直す。
        /// 回転軸は作業軸ローカルの X / Y / Z。制御点も同じ空間にあるため
        /// Orientation を掛けない静的な軸ベクトルを使う。
        /// </summary>
        private void DragRotatePoints(Vector2 screenPos)
        {
            if (_dragStartPoints.Count == 0) return;

            float deg = _ringGizmo.ComputeAngleDeltaDeg(ToImgui(screenPos));
            Quaternion q = Quaternion.AngleAxis(deg, RotateRingGizmo.AxisVector(_dragAxis));

            foreach (var kv in _dragStartPoints)
                Grid.SetCurrent(kv.Key, _dragPivotLocal + q * (kv.Value - _dragPivotLocal));
        }

        /// <summary>
        /// ギズモ上でドラッグが始まったかを判定し、始まったなら操作を開始する。
        /// 拡大縮小 / 回転は開始位置を記録する。
        /// </summary>
        private bool TryBeginPointDrag(ToolContext ctx, Vector2 screenPos)
        {
            Vector2 imgui = ToImgui(screenPos);

            if (_pointMode == PointGizmoMode.Rotate)
            {
                var ring = _ringGizmo.FindRingAtScreenPos(imgui, ctx);
                if (ring == AxisGizmo.AxisType.None) return false;

                // 開始角・軸符号の算出は RotateRingGizmo の角度ドラッグセッションに集約。
                if (!_ringGizmo.BeginAngleDrag(ctx, imgui, ring)) return false;

                _dragAxis = ring;
                CaptureDragStart();
                return true;
            }

            var axis = _axisGizmo.FindAxisAtScreenPos(imgui, ctx);
            if (axis == AxisGizmo.AxisType.None) return false;

            if (_pointMode == PointGizmoMode.Scale)
            {
                // 軸スクリーン方向の算出は AxisGizmo のスケールドラッグセッションに集約。
                if (!_axisGizmo.BeginScaleDrag(ctx, axis, screenPos)) return false;
                _dragAxis = axis;
                CaptureDragStart();
                return true;
            }

            _dragAxis = axis;
            return true;
        }

        /// <summary>選択制御点の開始位置と重心を記録する。</summary>
        private void CaptureDragStart()
        {
            _dragStartPoints.Clear();

            Vector3 sum = Vector3.zero;
            foreach (int i in _selectedPoints)
            {
                Vector3 p = Grid.GetCurrent(i);
                _dragStartPoints[i] = p;
                sum += p;
            }

            _dragPivotLocal = _selectedPoints.Count > 0
                ? sum / _selectedPoints.Count
                : Vector3.zero;
        }

        /// <summary>ドラッグセッションを終了する。頂点位置には触れない。</summary>
        private void EndPointDrag()
        {
            _dragAxis = AxisGizmo.AxisType.None;
            _dragStartPoints.Clear();
            _ringGizmo.EndAngleDrag();
            _axisGizmo.EndScaleDrag();
        }

        public void OnLeftDragEnd(Vector2 screenPos, ModifierKeys mods)
        {
            if (_boxSelecting)
            {
                var ctx = GetToolContext?.Invoke();
                if (ctx != null) CommitBoxSelect(_boxStart, screenPos, ctx, _boxAdditive);

                _boxSelecting = false;
                OnBoxSelectEnd?.Invoke();
            }

            EndPointDrag();

            NotifyChanged();
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// 制御点とギズモ軸のホバー更新。呼び出し元（EnterHoverChanged）が
        /// 直後に overlay を再描画するため、ここでは再描画要求を出さない。
        /// </summary>
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            if (State != LatticeState.Deform || ctx == null)
            {
                _hoverPoint = -1;
                _hoverAxis  = AxisGizmo.AxisType.None;
                return;
            }

            _hoverAxis = AxisGizmo.AxisType.None;
            if (TrySyncGizmo(ctx))
            {
                Vector2 imgui = ToImgui(screenPos);
                _hoverAxis = (_pointMode == PointGizmoMode.Rotate)
                    ? _ringGizmo.FindRingAtScreenPos(imgui, ctx)
                    : _axisGizmo.FindAxisAtScreenPos(imgui, ctx);
            }

            // ギズモに乗っているときは制御点のホバーを出さない（掴む対象を明確にする）。
            _hoverPoint = (_hoverAxis == AxisGizmo.AxisType.None)
                ? PickControlPoint(screenPos, ctx)
                : -1;
        }

        // ================================================================
        // 制御点のピック / 矩形選択
        // ================================================================

        /// <summary>
        /// スクリーン座標に最も近い制御点を返す。半径外は -1。
        /// screenPos は Y=0 下。
        /// </summary>
        private int PickControlPoint(Vector2 screenPos, ToolContext ctx)
        {
            var axis = GetWorkAxis?.Invoke();
            if (axis == null || !Grid.IsBuilt) return -1;

            var cp = Grid.CurrentControlPoints;
            float h = ctx.PreviewRect.height;

            int   best   = -1;
            float bestSq = PointHitRadius * PointHitRadius;

            for (int i = 0; i < cp.Length; i++)
            {
                Vector2 sp = ToHandlerScreen(ctx, axis.LocalToWorld(cp[i]), h);
                float d = (sp - screenPos).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    best   = i;
                }
            }
            return best;
        }

        /// <summary>矩形内の制御点を選択する。additive のとき既存選択へ足す。</summary>
        private void CommitBoxSelect(Vector2 start, Vector2 end, ToolContext ctx, bool additive)
        {
            var axis = GetWorkAxis?.Invoke();
            if (axis == null || !Grid.IsBuilt) return;

            float x0 = Mathf.Min(start.x, end.x), x1 = Mathf.Max(start.x, end.x);
            float y0 = Mathf.Min(start.y, end.y), y1 = Mathf.Max(start.y, end.y);

            if (!additive) _selectedPoints.Clear();

            var cp = Grid.CurrentControlPoints;
            float h = ctx.PreviewRect.height;

            for (int i = 0; i < cp.Length; i++)
            {
                Vector2 sp = ToHandlerScreen(ctx, axis.LocalToWorld(cp[i]), h);
                if (sp.x >= x0 && sp.x <= x1 && sp.y >= y0 && sp.y <= y1)
                    _selectedPoints.Add(i);
            }
        }

        // ================================================================
        // ギズモ
        // ================================================================

        /// <summary>
        /// 選択制御点の重心へギズモを合わせる。選択が無ければ false。
        /// 軸の向きは作業軸に合わせる（制御点が作業軸ローカルで動くため）。
        /// </summary>
        private bool TrySyncGizmo(ToolContext ctx)
        {
            var axis = GetWorkAxis?.Invoke();
            if (axis == null || !Grid.IsBuilt || _selectedPoints.Count == 0) return false;

            Vector3 sum = Vector3.zero;
            foreach (int i in _selectedPoints) sum += Grid.GetCurrent(i);

            Vector3 center = axis.LocalToWorld(sum / _selectedPoints.Count);

            _axisGizmo.Center      = center;
            _axisGizmo.Orientation = axis.Rotation;
            _ringGizmo.Center      = center;
            _ringGizmo.Orientation = axis.Rotation;
            return true;
        }

        /// <summary>
        /// 制御点を選んでいるときだけ移動ギズモを出す（IPlayerGizmoProvider）。
        /// </summary>
        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;

            if (State != LatticeState.Deform || ctx == null) return false;
            if (!TrySyncGizmo(ctx)) return false;

            var shown = _dragAxis != AxisGizmo.AxisType.None ? _dragAxis : _hoverAxis;

            if (_pointMode == PointGizmoMode.Rotate)
            {
                data = new PlayerViewportPanel.GizmoData
                {
                    HasGizmo    = true,
                    IsRingStyle = true,
                    RingX = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.X),
                    RingY = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Y),
                    RingZ = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Z),
                    HoveredAxis  = shown,
                    DraggingAxis = _dragAxis,
                };
                return true;
            }

            _axisGizmo.HoveredAxis  = _hoverAxis;
            _axisGizmo.DraggingAxis = _dragAxis;
            _axisGizmo.GetScreenPositions(ctx, out var o, out var xe, out var ye, out var ze);

            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo    = true,
                IsCubeStyle = _pointMode == PointGizmoMode.Scale,
                Origin      = o, XEnd = xe, YEnd = ye, ZEnd = ze,
                HoveredAxis  = shown,
                DraggingAxis = _dragAxis,
            };
            return true;
        }

        // ================================================================
        // オーバーレイ（格子ワイヤと制御点）
        // ================================================================

        private static readonly Color LineColor     = new Color(0.35f, 0.75f, 1f, 0.55f);
        private static readonly Color PointColor    = new Color(0.9f,  0.9f,  0.9f, 0.9f);
        private static readonly Color SelectedColor = new Color(1f,    0.85f, 0.2f, 1f);
        private static readonly Color HoverColor    = new Color(0.3f,  1f,    1f,   1f);

        /// <summary>
        /// 格子の線と制御点をスクリーン座標（Y=0 下）で組み立てる。
        /// 未開始・未構築のときは false を返す（呼び出し側は overlay を隠す）。
        /// </summary>
        public bool TryBuildOverlay(
            ToolContext ctx,
            out List<(Vector2 a, Vector2 b, Color col)> lines,
            out List<(Vector2 p, Color col, float halfSize)> points)
        {
            lines  = null;
            points = null;

            if (State == LatticeState.Idle || ctx == null || !Grid.IsBuilt) return false;

            var axis = GetWorkAxis?.Invoke();
            if (axis == null) return false;

            var cp = Grid.CurrentControlPoints;
            float h = ctx.PreviewRect.height;

            // 制御点を一度だけ投影して使い回す。
            var sp = new Vector2[cp.Length];
            for (int i = 0; i < cp.Length; i++)
                sp[i] = ToHandlerScreen(ctx, axis.LocalToWorld(cp[i]), h);

            lines  = new List<(Vector2, Vector2, Color)>();
            points = new List<(Vector2, Color, float)>();

            int px = Grid.PointCountX, py = Grid.PointCountY, pz = Grid.PointCountZ;

            for (int iz = 0; iz < pz; iz++)
            for (int iy = 0; iy < py; iy++)
            for (int ix = 0; ix < px; ix++)
            {
                int i = Grid.PointIndex(ix, iy, iz);

                if (ix + 1 < px) lines.Add((sp[i], sp[Grid.PointIndex(ix + 1, iy, iz)], LineColor));
                if (iy + 1 < py) lines.Add((sp[i], sp[Grid.PointIndex(ix, iy + 1, iz)], LineColor));
                if (iz + 1 < pz) lines.Add((sp[i], sp[Grid.PointIndex(ix, iy, iz + 1)], LineColor));

                bool sel   = _selectedPoints.Contains(i);
                bool hover = (i == _hoverPoint);

                Color col = hover ? HoverColor : (sel ? SelectedColor : PointColor);
                float hs  = (hover || sel) ? 4.5f : 3f;

                points.Add((sp[i], col, hs));
            }

            return true;
        }

        // ================================================================
        // 座標変換
        // ================================================================

        /// <summary>スクリーン系（Y=0 下）→ ctx 系（Y=0 上）。</summary>
        private Vector2 ToImgui(Vector2 screenPosYDown)
        {
            float h = GetPanelHeight?.Invoke() ?? 0f;
            return new Vector2(screenPosYDown.x, h - screenPosYDown.y);
        }

        /// <summary>ワールド座標 → スクリーン系（Y=0 下）。</summary>
        private static Vector2 ToHandlerScreen(ToolContext ctx, Vector3 world, float panelHeight)
        {
            Vector2 s = ctx.WorldToScreen(world);
            return new Vector2(s.x, panelHeight - s.y);
        }
    }
}
