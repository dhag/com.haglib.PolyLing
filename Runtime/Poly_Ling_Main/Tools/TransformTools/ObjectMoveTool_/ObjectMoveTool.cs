// Tools/TransformTools/ObjectMoveTool_/ObjectMoveTool.cs
// MeshFilter オブジェクトおよび SkinnedMeshRenderer ボーン位置を移動するツール
// 頂点移動ツール(MoveTool)と同じ操作感を目指す
// - AxisGizmo による軸拘束移動・中央自由移動
// - Shift/Ctrl による複数選択
// - 子ボーン一緒に移動 / 独立モード
// - Undo 対応 (MultiBoneTransformChangeRecord)

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Localization;
using Poly_Ling.Context;
using Poly_Ling.UndoSystem;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Tools
{
    /// <summary>
    /// MeshFilter オブジェクトおよびボーン位置を移動するツール
    /// </summary>
    public partial class ObjectMoveTool : IEditTool
    {
        public string Name => "ObjectMove";
        public string DisplayName => "Obj.Move";
        public string GetLocalizedDisplayName() => L.Get("Tool_ObjectMove");

        // ================================================================
        // 設定
        // ================================================================

        private ObjectMoveSettings _settings = new ObjectMoveSettings();
        public IToolSettings Settings => _settings;

        /// <summary>
        /// 設定インスタンスを外部から差し替える。
        /// BoneEditor サブパネル側とオブジェ移動 UI 側で ObjectMoveSettings を
        /// 共有したい場合に使う (両方のチェックボックスを同じ設定に結びつける)。
        /// </summary>
        public void SetSettings(ObjectMoveSettings settings)
        {
            if (settings != null) _settings = settings;
        }

        public ObjectMoveSettings GetSettings() => _settings;

        public bool MoveWithChildren
        {
            get => _settings.MoveWithChildren;
            set => _settings.MoveWithChildren = value;
        }

        // ================================================================
        // 状態
        // ================================================================

        private enum DragState { Idle, PendingDrag, AxisDragging, CenterDragging, RingDragging }
        private DragState _state = DragState.Idle;

        private AxisGizmo _axisGizmo = new AxisGizmo();
        private AxisGizmo.AxisType _draggingAxis = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _hoveredAxis  = AxisGizmo.AxisType.None;

        // 回転リングギズモ（RotateToolHandler と同じ RotateRingGizmo を使う）。
        // 軸ギズモは ScreenOffset(60,-60) だけずらして描かれるのに対し、
        // リングはピボット位置そのものに描く。
        private readonly RotateRingGizmo _ringGizmo = new RotateRingGizmo();
        private AxisGizmo.AxisType _ringDragAxis  = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _ringHoverAxis = AxisGizmo.AxisType.None;

        private Vector2 _mouseDownPos;
        private Vector2 _lastDragScreenPos;
        private Vector2 _lastMousePos;
        private ToolContext _lastCtx;

        private const float DragThreshold = 4f;
        private const float PickRadius    = 18f;

        // Undo 用スナップショット（ドラッグ開始時保存）
        private Dictionary<int, BoneTransformSnapshot> _beforeSnapshots
            = new Dictionary<int, BoneTransformSnapshot>();

        // バインド連動(スキン固定)用: ドラッグ開始時の全ボーンの SkinningMatrix / BindPose
        private readonly Dictionary<int, Matrix4x4> _rebindStartSkinning
            = new Dictionary<int, Matrix4x4>();
        private readonly Dictionary<int, Matrix4x4> _rebindStartBindPose
            = new Dictionary<int, Matrix4x4>();

        // B(スキンごと確定)用: ドラッグ開始時の頂点/ボーン状態バックアップ
        private Poly_Ling.Data.TPoseBackup _freezeBefore;

        // 原点だけ移動(OriginOnly, MeshFilter)用: ドラッグ開始時の対象メッシュ頂点位置と開始WorldMatrix
        private readonly Dictionary<int, UnityEngine.Vector3[]> _originStartPositions
            = new Dictionary<int, UnityEngine.Vector3[]>();
        private readonly Dictionary<int, Matrix4x4> _originStartWorld
            = new Dictionary<int, Matrix4x4>();

        // 原点だけ移動のドラッグ 1 ストロークのワールド総移動量。
        // SaveSnapshots で 0、ApplyWorldDelta で加算する。
        // 確定をコマンド 1 本に寄せるために、ワールドの総量をここで持つ。
        private Vector3 _originWorldTotal;

        // コマンド経由の対象指定。null のときはモデルの現在の選択を使う（マウス経路）。
        // SaveSnapshots / ApplyWorldDelta / CommitUndo はいずれも AllSelectedIndices を
        // 見るので、ここを差し替えるだけで 3 つとも同じ集合を対象にできる。
        private HashSet<int> _targetOverride;

        // 回転ドラッグ用: ドラッグ開始時の状態。
        // 回転はフレーム差分を累積せず「開始状態 + 累計ΔR」で毎フレーム再計算する。
        // (BoneTransform.Rotation はオイラー保持のため、差分を毎フレーム往復させると
        //  ジンバル境界で値が崩れる)
        private readonly Dictionary<int, Quaternion> _rotStartLocalRot
            = new Dictionary<int, Quaternion>();
        private readonly Dictionary<int, Vector3> _rotStartWorldPos
            = new Dictionary<int, Vector3>();
        // 開始時の親 WorldMatrix。親子を同時選択しても二重回転しないよう、
        // 適用中は「動く前の親」を基準にする。
        private readonly Dictionary<int, Matrix4x4> _rotStartParentWorld
            = new Dictionary<int, Matrix4x4>();
        // 子補正(MoveWithChildren==false)用: 開始時の直接の子の状態。
        // 「子のワールド 4x4 を保存して新親の逆行列で分解する」方式は、子自身が
        // 非一様スケールを持つと分解が正確でなくなる。そこで分解を一切使わずに済むよう、
        // ローカル回転・ワールド原点・親のワールド行列を分けて保存する。
        private struct ChildRotStart
        {
            public Quaternion LocalRot;     // 子のローカル回転
            public Vector3    WorldPos;     // 子のワールド原点
            public Matrix4x4  ParentWorld;  // 親(=選択要素)の開始ワールド行列
        }
        private readonly Dictionary<int, ChildRotStart> _rotChildStart
            = new Dictionary<int, ChildRotStart>();
        private Vector3 _rotPivotWorld;
        private bool    _rotWarnedNonUniform;

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            _lastCtx = ctx;
            _lastMousePos = mousePos;
            _mouseDownPos = mousePos;
            if (ctx?.Model == null) return false;

            // 1. 選択があればギズモのヒットテスト（最優先）
            if (HasAnySelection(ctx))
            {
                UpdateGizmoCenter(ctx);
                var hitAxis = IsMoveGizmoEnabled()
                    ? _axisGizmo.FindAxisAtScreenPos(mousePos, ctx)
                    : AxisGizmo.AxisType.None;
                if (hitAxis != AxisGizmo.AxisType.None)
                {
                    SaveSnapshots(ctx);
                    _draggingAxis = hitAxis;
                    _lastDragScreenPos = mousePos;
                    _state = hitAxis == AxisGizmo.AxisType.Center
                        ? DragState.CenterDragging
                        : DragState.AxisDragging;
                    _axisGizmo.DraggingAxis = _draggingAxis;
                    ctx.EnterTransformDragging?.Invoke();
                    return true;
                }

                // 1b. 軸ギズモに当たらなかった場合のみ回転リングを判定する。
                //     この順序により移動側のピック挙動は従来と完全に同一のままになる。
                if (TryBeginRingDrag(ctx, mousePos))
                    return true;
            }

            // 2. ピッキング
            bool picked = TryPickObject(ctx, mousePos,
                ctx.IsShiftHeld,
                ctx.IsControlHeld);
            if (picked)
            {
                _state = DragState.PendingDrag;
                return true;
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            _lastCtx = ctx;
            _lastMousePos = mousePos;
            switch (_state)
            {
                case DragState.PendingDrag:
                {
                    if (Vector2.Distance(mousePos, _mouseDownPos) > DragThreshold)
                    {
                        // ドラッグ開始：中央自由移動
                        SaveSnapshots(ctx);
                        UpdateGizmoCenter(ctx);
                        _draggingAxis = AxisGizmo.AxisType.Center;
                        _lastDragScreenPos = _mouseDownPos;
                        _state = DragState.CenterDragging;
                        ctx.EnterTransformDragging?.Invoke();

                        Vector2 totalDelta = mousePos - _mouseDownPos;
                        totalDelta.y = -totalDelta.y;
                        ApplyFreeDelta(totalDelta, ctx);
                        _lastDragScreenPos = mousePos;
                    }
                    ctx.Repaint?.Invoke();
                    return true;
                }

                case DragState.CenterDragging:
                {
                    Vector2 frameDelta = mousePos - _lastDragScreenPos;
                    frameDelta.y = -frameDelta.y;
                    ApplyFreeDelta(frameDelta, ctx);
                    _lastDragScreenPos = mousePos;
                    ctx.Repaint?.Invoke();
                    return true;
                }

                case DragState.AxisDragging:
                {
                    Vector2 frameDelta = mousePos - _lastDragScreenPos;
                    ApplyAxisDelta(frameDelta, ctx);
                    _lastDragScreenPos = mousePos;
                    ctx.Repaint?.Invoke();
                    return true;
                }

                case DragState.RingDragging:
                {
                    UpdateRingDrag(ctx, mousePos);
                    ctx.Repaint?.Invoke();
                    return true;
                }
            }

            // Idle 時はホバー更新
            if (_state == DragState.Idle && HasAnySelection(ctx))
            {
                UpdateGizmoCenter(ctx);
                var hovered = IsMoveGizmoEnabled()
                    ? _axisGizmo.FindAxisAtScreenPos(mousePos, ctx)
                    : AxisGizmo.AxisType.None;
                if (hovered != _hoveredAxis)
                {
                    _hoveredAxis = hovered;
                    _axisGizmo.HoveredAxis = _hoveredAxis;
                    ctx.Repaint?.Invoke();
                }
                UpdateRingHover(ctx, mousePos);
            }

            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            bool handled = false;

            switch (_state)
            {
                case DragState.AxisDragging:
                case DragState.CenterDragging:
                    // 原点だけ移動は 1 ストローク = 1 コマンドに寄せてある。
                    // 呼び出し側（PivotOffsetToolHandler.OnMouseUp）が
                    // TryTakeOriginOnlyDrag で結果を取り出してコマンドを送るので、
                    // ここでは確定しない。取り出されなかった場合のみ従来どおり積む。
                    if (!(_settings.OriginOnly && OriginOnlyDragPending))
                        CommitUndo(ctx);
                    handled = true;
                    break;
                case DragState.RingDragging:
                    CommitUndo(ctx);
                    ClearRotationStart();
                    handled = true;
                    break;
                case DragState.PendingDrag:
                    // クリックのみ → 選択は済み
                    handled = true;
                    break;
            }

            _state = DragState.Idle;
            _draggingAxis = AxisGizmo.AxisType.None;
            _axisGizmo.DraggingAxis = AxisGizmo.AxisType.None;
            _ringDragAxis = AxisGizmo.AxisType.None;
            _ringGizmo.DraggingAxis = AxisGizmo.AxisType.None;
            _ringGizmo.EndAngleDrag();
            ctx.Repaint?.Invoke();
            return handled;
        }

        /// <summary>
        /// 取り出せる「原点だけ移動」のドラッグ結果があるか。
        /// TryTakeOriginOnlyDrag が true を返す条件と同じ。
        /// </summary>
        public bool OriginOnlyDragPending
            => _settings.OriginOnly
            && _originStartPositions.Count > 0
            && _originWorldTotal.sqrMagnitude >= 1e-10f;

        /// <summary>
        /// 「原点だけ移動」のドラッグ結果を取り出し、開始状態へ戻す。
        ///
        /// 【なぜ要るか】
        ///   1 ストローク = 1 コマンドにするため。ドラッグ中の適用はプレビューとして
        ///   扱い、確定時はここで開始状態へ戻して総移動量だけを返す。呼び出し側
        ///   （PivotOffsetToolHandler）が MovePivotCommand を送り、実際の移動と
        ///   Undo 記録は ApplyOriginOnlyFromCommand が行う。
        ///
        /// 【戻す方法】
        ///   CommitUndo が Undo 記録に使うのと同じ _originStartPositions と
        ///   _beforeSnapshots をそのまま書き戻す。復元用の経路を別に作らない。
        ///
        /// 【何も返さない場合】
        ///   OriginOnly でない、スナップショットが無い、総移動量が 0 のときは
        ///   false を返し、状態も戻さない。呼び出し側は従来どおり確定させる。
        /// </summary>
        public bool TryTakeOriginOnlyDrag(
            ToolContext ctx, out int[] masterIndices, out Vector3 worldTotal)
        {
            masterIndices = System.Array.Empty<int>();
            worldTotal    = Vector3.zero;

            if (!_settings.OriginOnly) return false;
            if (_originStartPositions.Count == 0) return false;
            if (_originWorldTotal.sqrMagnitude < 1e-10f) return false;

            var model = ctx?.Model;
            if (model == null) return false;

            worldTotal = _originWorldTotal;

            var targets = new List<int>(_originStartPositions.Keys);
            masterIndices = targets.ToArray();

            // 対象メッシュの頂点を開始位置へ戻す。
            foreach (var kv in _originStartPositions)
            {
                var mc = model.GetMeshContext(kv.Key);
                var mo = mc?.MeshObject;
                if (mo == null) continue;
                int n = Mathf.Min(mo.VertexCount, kv.Value.Length);
                for (int i = 0; i < n; i++) mo.Vertices[i].Position = kv.Value[i];
            }

            // 対象と補償した子の BoneTransform を開始状態へ戻す。
            foreach (var kv in _beforeSnapshots)
            {
                var mc = model.GetMeshContext(kv.Key);
                if (mc?.BoneTransform == null) continue;
                mc.BoneTransform.ApplySnapshot(kv.Value);
            }

            model.ComputeWorldMatrices();
            ctx.SyncMesh?.Invoke();

            _beforeSnapshots.Clear();
            _rebindStartSkinning.Clear();
            _rebindStartBindPose.Clear();
            _originStartPositions.Clear();
            _originStartWorld.Clear();
            _originWorldTotal = Vector3.zero;
            ctx.ExitTransformDragging?.Invoke();

            return true;
        }

        /// <summary>
        /// 「原点だけ移動」をコマンドから実行する。
        ///
        /// 【なぜ要るか】
        ///   OnMouseDown はギズモの軸を画面座標で当てて対象を決めるので、
        ///   コマンド経由（自動検証・MCP）からは通せない。対象と移動量だけを
        ///   渡せる入口をここに置く。EdgeBridgeToolHandler.SetPicks と同じ形。
        ///
        /// 【マウス経路と同じ実装を通す】
        ///   SaveSnapshots → ApplyWorldDelta → CommitUndo の順序はドラッグ確定時と
        ///   同一。子 BoneTransform の補償・スキン判定・Undo のグループ化はすべて
        ///   その 3 つの中にあるので、ここで書き足すものは無い。
        ///
        /// 【対象の渡し方】
        ///   3 つとも AllSelectedIndices を見るため、_targetOverride を立ててから
        ///   呼ぶ。モデルの選択状態は書き換えない（呼び出し後も画面の選択は不変）。
        /// </summary>
        /// <param name="masterIndices">対象の MeshContextList インデックス。</param>
        /// <param name="worldDelta">ワールド空間での移動量。</param>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ApplyOriginOnlyFromCommand(
            ToolContext ctx, IReadOnlyList<int> masterIndices, Vector3 worldDelta, out string reason)
        {
            reason = null;

            if (!_settings.OriginOnly)
            { reason = "このツールは原点だけ移動の設定になっていません"; return false; }

            var model = ctx?.Model;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (masterIndices == null || masterIndices.Count == 0)
            { reason = "対象が指定されていません"; return false; }

            var targets = new HashSet<int>();
            foreach (int idx in masterIndices)
            {
                if (model.GetMeshContext(idx) == null)
                { reason = $"masterIndex {idx} のオブジェクトがありません"; return false; }
                targets.Add(idx);
            }

            if (worldDelta.sqrMagnitude < 1e-10f)
            { reason = "移動量が 0 です"; return false; }

            _targetOverride = targets;
            try
            {
                ctx.EnterTransformDragging?.Invoke();
                SaveSnapshots(ctx);
                ApplyWorldDelta(worldDelta, ctx);
                CommitUndo(ctx);
            }
            finally
            {
                _targetOverride = null;
            }

            ctx.Repaint?.Invoke();
            return true;
        }

        /// <summary>
        /// ポインター移動時のホバー更新専用（ドラッグ中は何もしない）。
        /// ObjectMoveToolHandler.UpdateHover から呼ぶ。
        /// OnMouseDrag を呼ぶと _lastDragScreenPos が破壊されるため、この専用メソッドを使う。
        /// </summary>
        public void UpdateHoverOnly(ToolContext ctx, Vector2 mousePos)
        {
            _lastMousePos = mousePos;
            if (_state != DragState.Idle) return;
            if (!HasAnySelection(ctx)) return;
            UpdateGizmoCenter(ctx);
            var hovered = IsMoveGizmoEnabled()
                ? _axisGizmo.FindAxisAtScreenPos(mousePos, ctx)
                : AxisGizmo.AxisType.None;
            if (hovered != _hoveredAxis)
            {
                _hoveredAxis = hovered;
                _axisGizmo.HoveredAxis = _hoveredAxis;
                ctx.Repaint?.Invoke();
            }
            UpdateRingHover(ctx, mousePos);
        }

        public void DrawGizmo(ToolContext ctx)
        {
            _lastCtx = ctx;
            if (!HasAnySelection(ctx)) return;

            UpdateGizmoCenter(ctx);

            if (_state == DragState.Idle)
            {
                _hoveredAxis = IsMoveGizmoEnabled()
                    ? _axisGizmo.FindAxisAtScreenPos(_lastMousePos, ctx)
                    : AxisGizmo.AxisType.None;
                _axisGizmo.HoveredAxis = _hoveredAxis;
                UpdateRingHover(ctx, _lastMousePos);
            }

            if (IsMoveGizmoEnabled()) _axisGizmo.Draw(ctx);
        }

        /// <summary>
        /// AxisGizmo のスクリーン座標を返す。Player の UpdateGizmoOverlay から呼ぶ。
        /// 選択がない場合は false を返す。
        /// </summary>
        public bool TryGetGizmoScreenPositions(
            ToolContext ctx,
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            origin = xEnd = yEnd = zEnd = Vector2.zero;
            hoveredAxis = AxisGizmo.AxisType.None;
            if (ctx == null || !HasAnySelection(ctx)) return false;
            if (!IsMoveGizmoEnabled()) return false;

            UpdateGizmoCenter(ctx);
            _axisGizmo.HoveredAxis  = _hoveredAxis;
            _axisGizmo.DraggingAxis = _draggingAxis;
            _axisGizmo.GetScreenPositions(ctx, out origin, out xEnd, out yEnd, out zEnd);
            hoveredAxis = _hoveredAxis;
            return true;
        }

        /// <summary>
        /// 回転リング 3 軸のスクリーン点列を返す。Player の UpdateGizmoOverlay から呼ぶ。
        /// 選択がない場合、および回転を許可しない設定のときは false を返す。
        /// </summary>
        public bool TryGetGizmoRings(
            ToolContext ctx,
            out Vector2[] ringX, out Vector2[] ringY, out Vector2[] ringZ,
            out AxisGizmo.AxisType hoveredAxis)
        {
            ringX = ringY = ringZ = null;
            hoveredAxis = AxisGizmo.AxisType.None;
            if (ctx == null || !HasAnySelection(ctx)) return false;
            if (!IsRotationEnabled()) return false;

            UpdateGizmoCenter(ctx);
            _ringGizmo.Center = _axisGizmo.Center;
            ringX = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.X);
            ringY = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Y);
            ringZ = _ringGizmo.GetRingScreen(ctx, AxisGizmo.AxisType.Z);
            hoveredAxis = _ringDragAxis != AxisGizmo.AxisType.None
                ? _ringDragAxis
                : _ringHoverAxis;
            return true;
        }

        /// <summary>ピボット位置のスクリーン座標（オフセットなし）を返す。</summary>
        public bool GetPivotScreenPos(ToolContext ctx, out Vector2 pivotScreen)
        {
            pivotScreen = Vector2.zero;
            if (ctx == null || !HasAnySelection(ctx)) return false;
            UpdateGizmoCenter(ctx);
            pivotScreen = ctx.WorldToScreenPos(
                _axisGizmo.Center, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
            return true;
        }

        /// <summary>ピボット位置（ScreenOffset=0）のダイヤ型ギズモスクリーン座標を返す。</summary>
        public bool TryGetGizmoPivotPositions(
            ToolContext ctx,
            out Vector2 origin,
            out Vector2 xEnd, out Vector2 yEnd, out Vector2 zEnd,
            out AxisGizmo.AxisType hoveredAxis)
        {
            origin = xEnd = yEnd = zEnd = Vector2.zero;
            hoveredAxis = AxisGizmo.AxisType.None;
            if (ctx == null || !HasAnySelection(ctx)) return false;

            UpdateGizmoCenter(ctx);
            var savedOffset = _axisGizmo.ScreenOffset;
            _axisGizmo.ScreenOffset = Vector2.zero;
            _axisGizmo.HoveredAxis  = _hoveredAxis;
            _axisGizmo.DraggingAxis = _draggingAxis;
            _axisGizmo.GetScreenPositions(ctx, out origin, out xEnd, out yEnd, out zEnd);
            _axisGizmo.ScreenOffset = savedOffset;
            hoveredAxis = _hoveredAxis;
            return true;
        }

        public void OnActivate(ToolContext ctx)
        {
            _lastCtx = ctx;
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
        }

        public void Reset()
        {
            _state = DragState.Idle;
            _draggingAxis = AxisGizmo.AxisType.None;
            _hoveredAxis  = AxisGizmo.AxisType.None;
            _axisGizmo.DraggingAxis = AxisGizmo.AxisType.None;
            _axisGizmo.HoveredAxis  = AxisGizmo.AxisType.None;
            _ringDragAxis  = AxisGizmo.AxisType.None;
            _ringHoverAxis = AxisGizmo.AxisType.None;
            _ringGizmo.DraggingAxis = AxisGizmo.AxisType.None;
            _ringGizmo.HoveredAxis  = AxisGizmo.AxisType.None;
            _ringGizmo.EndAngleDrag();
            ClearRotationStart();
            _beforeSnapshots.Clear();
        }

        // ================================================================
        // 選択ヘルパー
        // ================================================================

        /// <summary>ボーン選択またはメッシュ選択があるか</summary>
        private bool HasAnySelection(ToolContext ctx)
        {
            var model = ctx?.Model;
            if (model == null) return false;
            return model.HasBoneSelection || model.HasMeshSelection;
        }

        private int GetSelectedCount(ToolContext ctx)
        {
            var model = ctx?.Model;
            if (model == null) return 0;
            return model.SelectedBoneIndices.Count + model.SelectedDrawableMeshIndices.Count;
        }

        /// <summary>
        /// 選択中アイテム全インデックス（ボーン + メッシュ）。
        /// _targetOverride が入っているときは選択ではなくそちらを返す
        /// （コマンド経由。ApplyOriginOnlyFromCommand が設定する）。
        /// </summary>
        private IEnumerable<int> AllSelectedIndices(ToolContext ctx)
        {
            if (_targetOverride != null)
            {
                foreach (int i in _targetOverride) yield return i;
                yield break;
            }

            var model = ctx?.Model;
            if (model == null) yield break;
            foreach (int i in model.SelectedBoneIndices) yield return i;
            foreach (int i in model.SelectedDrawableMeshIndices)
            {
                // ボーン選択に既に含まれていれば重複しない
                if (!model.SelectedBoneIndices.Contains(i))
                    yield return i;
            }
        }

        // ================================================================
        // ピッキング
        // ================================================================

        /// <summary>
        /// マウス位置から最近傍オブジェクト（ボーン or MeshFilter）をピック
        /// </summary>
        /// <summary>
        /// ピック対象フィルタを 1 つの MeshContext に対して評価する。
        ///
        /// 【1 か所に集約する理由】
        ///   同じ判定を「クリックピック」「矩形・投げ縄選択」「原点マーカーの表示」の
        ///   3 か所で書くと、片方だけ直したときに『掴めるのにマーカーが出ない』
        ///   『マーカーは出るのに掴めない』が起きる。判定はここだけに置くこと。
        /// </summary>
        public static bool PassesPickFilter(MeshContext mc, ObjectMoveSettings s)
        {
            if (mc == null || s == null) return false;

            var t = mc.Type;

            // モーフ・剛体・ジョイント・グループは常に除外
            if (t == MeshType.Morph || t == MeshType.RigidBody ||
                t == MeshType.RigidBodyJoint || t == MeshType.Group)
                return false;

            // ミラー側は実体側と原点が重なるため既定で除外
            if (t == MeshType.MirrorSide || t == MeshType.BakedMirror)
                return s.PickMirrorSides;

            if (t == MeshType.Bone) return s.PickBones;

            // 判定は MeshContext.IsSkinned に集約する。
            if (t == MeshType.Mesh)
                return mc.IsSkinned ? s.PickMeshesSkinned : s.PickMeshesNoSkin;

            // Helper は従来互換で常にピック対象。
            return true;
        }

        private bool TryPickObject(ToolContext ctx, Vector2 mousePos, bool shift, bool ctrl)
        {
            var model = ctx?.Model;
            if (model == null) return false;

            int bestIndex = -1;
            float bestDist = PickRadius;

            for (int i = 0; i < model.Count; i++)
            {
                var mc = model.GetMeshContext(i);
                if (mc == null) continue;
                if (!PassesPickFilter(mc, _settings)) continue;

                var wm = mc.WorldMatrix;
                Vector3 worldPos = new Vector3(wm.m03, wm.m13, wm.m23);
                Vector2 sp = ctx.WorldToScreen(worldPos);

                float dist = Vector2.Distance(mousePos, sp);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0) return false;

            var picked = model.GetMeshContext(bestIndex);
            if (picked == null) return false;

            if (ctrl)
            {
                model.ToggleMeshContextSelection(bestIndex);
            }
            else if (shift)
            {
                model.AddToSelection(bestIndex);
            }
            else
            {
                // 単一選択：カテゴリに応じて既存選択をクリアして選択
                model.SelectMeshContextExclusive(bestIndex);
            }

            model.IsDirty = true;
            model.OnListChanged?.Invoke();
            ctx.OnMeshSelectionChanged?.Invoke();
            ctx.Repaint?.Invoke();
            return true;
        }

        // ================================================================
        // ギズモ中心計算
        // ================================================================

        private void UpdateGizmoCenter(ToolContext ctx)
        {
            var model = ctx?.Model;
            if (model == null) return;

            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (int idx in AllSelectedIndices(ctx))
            {
                var mc = model.GetMeshContext(idx);
                if (mc == null) continue;
                var wm = mc.WorldMatrix;
                sum += new Vector3(wm.m03, wm.m13, wm.m23);
                count++;
            }

            _axisGizmo.Center = count > 0 ? sum / count : Vector3.zero;
        }

        // ================================================================
        // 移動適用
        // ================================================================

        private void ApplyFreeDelta(Vector2 screenDelta, ToolContext ctx)
        {
            Vector3 worldDelta = _axisGizmo.ComputeFreeDelta(screenDelta, ctx);
            UnityEngine.Debug.Log($"[MoveDbg] FREE screenDelta={screenDelta} worldDelta={worldDelta} camDist={ctx.CameraDistance} display={(ctx.DisplayMatrix != UnityEngine.Matrix4x4.identity ? "nonId" : "id")}");
            ApplyWorldDelta(worldDelta, ctx);
        }

        private void ApplyAxisDelta(Vector2 screenDelta, ToolContext ctx)
        {
            Vector3 worldDelta = _axisGizmo.ComputeAxisDelta(screenDelta, _draggingAxis, ctx);
            UnityEngine.Debug.Log($"[MoveDbg] AXIS axis={_draggingAxis} screenDelta={screenDelta} worldDelta={worldDelta} center={_axisGizmo.Center}");
            ApplyWorldDelta(worldDelta, ctx);
        }

        /// <summary>
        /// ワールドデルタを選択アイテムの BoneTransform.Position に加算する
        ///
        /// MoveWithChildren == false の場合：
        ///   1. 移動前に直接の子のワールド位置を保存
        ///   2. 選択アイテムを移動して ComputeWorldMatrices()
        ///   3. 新しい親の WorldMatrixInverse で保存したワールド位置をローカルに逆算し、
        ///      子の Position を上書き → 子の世界位置が変わらない
        /// </summary>
        private void ApplyWorldDelta(Vector3 worldDelta, ToolContext ctx)
        {
            if (worldDelta.sqrMagnitude < 1e-10f) return;
            var model = ctx?.Model;
            if (model == null) return;

            _originWorldTotal += worldDelta;

            var selectedSet = new HashSet<int>(AllSelectedIndices(ctx));

            // 子補正モード: 移動前に直接の子のワールド位置を保存
            Dictionary<int, Vector3> childSavedWorldPos = null;
            if (!_settings.MoveWithChildren)
            {
                childSavedWorldPos = new Dictionary<int, Vector3>();
                for (int i = 0; i < model.Count; i++)
                {
                    if (selectedSet.Contains(i)) continue;
                    var mc = model.GetMeshContext(i);
                    if (mc?.BoneTransform == null) continue;
                    if (!selectedSet.Contains(mc.HierarchyParentIndex)) continue;
                    var wm = mc.WorldMatrix;
                    childSavedWorldPos[i] = new Vector3(wm.m03, wm.m13, wm.m23);
                }
            }

            // 選択アイテムを移動
            foreach (int idx in selectedSet)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;
                var __wmB = mc.WorldMatrix;
                UnityEngine.Vector3 __beforeLocal = mc.BoneTransform.Position;
                UnityEngine.Vector3 __beforeWorld = new UnityEngine.Vector3(__wmB.m03, __wmB.m13, __wmB.m23);
                bool __useLocalWas = mc.BoneTransform.UseLocalTransform;
                UnityEngine.Debug.Log($"[MoveDbg] BEFORE idx={idx} useLocal={__useLocalWas} local={__beforeLocal} world={__beforeWorld} parent={mc.HierarchyParentIndex} worldDelta={worldDelta}");

                mc.BoneTransform.UseLocalTransform = true;
                mc.BoneTransform.Position += worldDelta;
                UnityEngine.Debug.Log($"[MoveDbg] AFTER_POS idx={idx} local={mc.BoneTransform.Position}");
            }

            // 親の新 WorldMatrix を確定
            model.ComputeWorldMatrices();
            foreach (int idx in selectedSet)
            {
                var mc = model.GetMeshContext(idx);
                if (mc == null) continue;
                var __wmC = mc.WorldMatrix;
                UnityEngine.Debug.Log($"[MoveDbg] AFTER_COMPUTE idx={idx} world={new UnityEngine.Vector3(__wmC.m03, __wmC.m13, __wmC.m23)}");
            }

            // 子補正: 新しい親 WorldMatrixInverse でワールド位置をローカルに逆算
            if (childSavedWorldPos != null && childSavedWorldPos.Count > 0)
            {
                foreach (var kvp in childSavedWorldPos)
                {
                    int childIdx = kvp.Key;
                    Vector3 targetWorld = kvp.Value;

                    var childMc = model.GetMeshContext(childIdx);
                    if (childMc?.BoneTransform == null) continue;

                    var parentMc = model.GetMeshContext(childMc.HierarchyParentIndex);
                    if (parentMc == null) continue;

                    // 新親の逆行列でワールド位置 → 新ローカル位置
                    Vector3 newLocal = parentMc.WorldMatrixInverse.MultiplyPoint3x4(targetWorld);
                    childMc.BoneTransform.UseLocalTransform = true;
                    childMc.BoneTransform.Position = newLocal;
                }

                // 子補正後に再計算
                model.ComputeWorldMatrices();
            }

            // バインド連動(スキン固定): World が変わったボーンの BindPose を更新して
            // SkinningMatrix を移動前と同一に保つ（メッシュは画面上不変）。
            if (_settings.MoveMode == BoneMoveMode.BoneOnlyRebind && _rebindStartSkinning.Count > 0)
            {
                foreach (var kv in _rebindStartSkinning)
                {
                    var mc = model.GetMeshContext(kv.Key);
                    if (mc == null || mc.Type != MeshType.Bone) continue;
                    mc.BindPose = mc.WorldMatrix.inverse * kv.Value;
                }
            }

            // 原点だけ移動(OriginOnly): MeshFilter の自頂点を「開始ワールド位置を保つ」よう再局所化する。
            // 原点(BoneTransform.Position)は動くが対象メッシュの見た目は不変になる（位置のみ）。
            // ComputeWorldMatrices 済みの現 WorldMatrixInverse を使う。
            if (_settings.OriginOnly && _originStartWorld.Count > 0)
            {
                foreach (int idx in selectedSet)
                {
                    if (!_originStartWorld.TryGetValue(idx, out var startWorld)) continue;
                    if (!_originStartPositions.TryGetValue(idx, out var startPos)) continue;
                    var mc = model.GetMeshContext(idx);
                    var mo = mc?.MeshObject;
                    if (mo == null) continue;
                    Matrix4x4 curInv = mc.WorldMatrixInverse;
                    int n = Mathf.Min(mo.VertexCount, startPos.Length);
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 worldPos = startWorld.MultiplyPoint3x4(startPos[i]);
                        var v = mo.Vertices[i];
                        v.Position = curInv.MultiplyPoint3x4(worldPos);
                        mo.Vertices[i] = v;
                    }
                    mo.InvalidatePositionCache();
                }
                // 書き換えた頂点を GPU へ同期する（これが無いと自形状補償が描画されず、
                // メッシュが原点に追従して動いて見える）。
                ctx.SyncMesh?.Invoke();
            }

            ctx.SyncBoneTransforms?.Invoke();
            foreach (int idx in selectedSet)
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;
                var __wmS = mc.WorldMatrix;
                UnityEngine.Debug.Log($"[MoveDbg] AFTER_SYNC idx={idx} useLocal={mc.BoneTransform.UseLocalTransform} local={mc.BoneTransform.Position} world={new UnityEngine.Vector3(__wmS.m03, __wmS.m13, __wmS.m23)}");
            }
            ctx.Repaint?.Invoke();
        }

        // ================================================================
        // 回転適用
        // ================================================================
        //
        // 【前提】BoneTransform は Position / Rotation(オイラー) / Scale を分離保持し、
        // LocalMatrix = Matrix4x4.TRS(...) である (BoneTransform.cs:87)。
        // したがって「4x4 をそのまま local へ焼き戻す」ことはできず、
        // 回転成分は純回転のまま合成しなければならない。
        //
        // 純回転のまま合成できる条件:
        //   deltaLocal = Inverse(P.rotation) * deltaWorld * P.rotation
        // が回転になるのは、親 P のスケールが一様なときに限る。
        // 祖先チェーンに非一様スケールがあるとシアーが生じ、この式では表現できない。
        // → HasNonUniformScaleInAncestors() で検出し、その要素は対象から外す。
        // ================================================================

        /// <summary>
        /// 回転操作を許可する状態か。
        /// 「原点だけ移動(OriginOnly)」とは連動しない。OriginOnly 時は
        /// ApplyWorldRotation 側で自頂点を再ローカル化して見た目を固定する。
        /// PivotOffsetToolHandler のように回転リングを描かない呼び出し元だけが false にする。
        /// </summary>
        private bool IsRotationEnabled() => _settings.AllowRotationGizmo;

        /// <summary>
        /// 移動（矢印）ギズモを許可する状態か。
        /// false のときは描画・当たり判定・ホバーの全てを止める。
        /// オブジェクト原点が矢印や中央ハンドルに隠れて掴めない場合に OFF にする。
        /// </summary>
        private bool IsMoveGizmoEnabled() => _settings.AllowMoveGizmo;

        /// <summary>
        /// 回転リングのヒットテストを行い、当たっていれば回転ドラッグを開始する。
        /// 軸ギズモのヒットテストが外れた後にのみ呼ぶこと。
        /// </summary>
        private bool TryBeginRingDrag(ToolContext ctx, Vector2 mousePos)
        {
            if (!IsRotationEnabled()) return false;
            if (ctx?.WorldToScreenPos == null) return false;

            _ringGizmo.Center = _axisGizmo.Center;
            var ringAxis = _ringGizmo.FindRingAtScreenPos(mousePos, ctx);
            if (ringAxis == AxisGizmo.AxisType.None) return false;

            SaveRotationStart(ctx, _axisGizmo.Center);
            if (_rotStartLocalRot.Count == 0) return false;   // 全要素が除外された
            SaveSnapshots(ctx);

            // 開始角・軸符号の算出は RotateRingGizmo の角度ドラッグセッションに集約。
            if (!_ringGizmo.BeginAngleDrag(ctx, mousePos, ringAxis)) return false;

            _ringDragAxis = ringAxis;
            _ringGizmo.DraggingAxis = ringAxis;
            _state = DragState.RingDragging;
            ctx.EnterTransformDragging?.Invoke();
            return true;
        }

        /// <summary>
        /// 回転ドラッグ中の更新。開始角からの累計角を求めて ApplyWorldRotation に渡す。
        /// フレーム差分ではなく毎回「開始角からの絶対角」を渡す。
        /// </summary>
        private void UpdateRingDrag(ToolContext ctx, Vector2 mousePos)
        {
            if (_ringDragAxis == AxisGizmo.AxisType.None) return;

            float deltaDeg = _ringGizmo.ComputeAngleDeltaDeg(mousePos);
            Vector3 worldAxis = RotateRingGizmo.AxisVector(_ringDragAxis);
            ApplyWorldRotation(Quaternion.AngleAxis(deltaDeg, worldAxis), ctx);
        }

        /// <summary>Idle 時のリングホバー更新。軸ギズモに当たっている間は評価しない。</summary>
        private void UpdateRingHover(ToolContext ctx, Vector2 mousePos)
        {
            AxisGizmo.AxisType hovered = AxisGizmo.AxisType.None;

            if (IsRotationEnabled() && _hoveredAxis == AxisGizmo.AxisType.None)
            {
                _ringGizmo.Center = _axisGizmo.Center;
                hovered = _ringGizmo.FindRingAtScreenPos(mousePos, ctx);
            }

            if (hovered != _ringHoverAxis)
            {
                _ringHoverAxis = hovered;
                _ringGizmo.HoveredAxis = hovered;
                ctx.Repaint?.Invoke();
            }
        }

        /// <summary>
        /// 回転ドラッグの開始状態を保存する。
        /// ピボットはワールド座標で受け取る (通常は _axisGizmo.Center)。
        /// 祖先チェーンに非一様スケールを持つ要素は対象から除外する。
        /// </summary>
        private void SaveRotationStart(ToolContext ctx, Vector3 pivotWorld)
        {
            ClearRotationStart();

            var model = ctx?.Model;
            if (model == null) return;

            _rotPivotWorld = pivotWorld;

            foreach (int idx in AllSelectedIndices(ctx))
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;

                if (HasNonUniformScaleInAncestors(model, idx))
                {
                    if (!_rotWarnedNonUniform)
                    {
                        _rotWarnedNonUniform = true;
                        UnityEngine.Debug.LogWarning(
                            "[ObjectMoveTool] 祖先に非一様スケールを持つ要素は回転対象から除外しました。" +
                            "TRS 分離保持のため、シアーを含む姿勢を表現できません。");
                    }
                    continue;
                }

                int parentIdx = mc.HierarchyParentIndex;
                var parentMc  = (parentIdx >= 0 && parentIdx < model.Count)
                    ? model.GetMeshContext(parentIdx)
                    : null;

                _rotStartLocalRot[idx]    = Quaternion.Euler(mc.BoneTransform.Rotation);
                _rotStartWorldPos[idx]    = (Vector3)mc.WorldMatrix.GetColumn(3);
                _rotStartParentWorld[idx] = parentMc != null ? parentMc.WorldMatrix : Matrix4x4.identity;
            }

            // 子補正モード: 直接の子の開始状態を保存する。
            // 判定条件は ApplyWorldDelta の子補正と同一 (選択外かつ親が選択中)。
            if (!_settings.MoveWithChildren && _rotStartLocalRot.Count > 0)
            {
                for (int i = 0; i < model.Count; i++)
                {
                    if (_rotStartLocalRot.ContainsKey(i)) continue;

                    var childMc = model.GetMeshContext(i);
                    if (childMc?.BoneTransform == null) continue;

                    int pIdx = childMc.HierarchyParentIndex;
                    if (!_rotStartLocalRot.ContainsKey(pIdx)) continue;

                    // 親のワールドが一様スケールでないと共役変換が回転にならない。
                    // この判定は子から上へ遡るので親チェーン全体を覆う。
                    if (HasNonUniformScaleInAncestors(model, i)) continue;

                    var parentMc = model.GetMeshContext(pIdx);
                    if (parentMc == null) continue;

                    _rotChildStart[i] = new ChildRotStart
                    {
                        LocalRot    = Quaternion.Euler(childMc.BoneTransform.Rotation),
                        WorldPos    = (Vector3)childMc.WorldMatrix.GetColumn(3),
                        ParentWorld = parentMc.WorldMatrix,
                    };
                }
            }
        }

        /// <summary>
        /// 開始状態からの累計回転 <paramref name="deltaWorld"/> を選択アイテムへ適用する。
        /// フレーム差分ではなく開始状態からの絶対値を毎回受け取ること。
        /// SaveRotationStart() を先に呼んでいない場合は何もしない。
        /// </summary>
        private void ApplyWorldRotation(Quaternion deltaWorld, ToolContext ctx)
        {
            if (_rotStartLocalRot.Count == 0) return;
            var model = ctx?.Model;
            if (model == null) return;

            foreach (var kv in _rotStartLocalRot)
            {
                int idx = kv.Key;
                var mc  = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;

                if (!_rotStartParentWorld.TryGetValue(idx, out var parentWorld)) continue;
                if (!_rotStartWorldPos.TryGetValue(idx, out var startWorldPos)) continue;

                // ワールド回転 → 親ローカル回転へ共役変換
                Quaternion parentRot = parentWorld.rotation;
                Quaternion deltaLocal = Quaternion.Inverse(parentRot) * deltaWorld * parentRot;

                Quaternion newLocalRot = deltaLocal * kv.Value;

                // ピボット周りの公転をワールドで解いてから親ローカルへ戻す
                Vector3 newWorldPos = _rotPivotWorld + deltaWorld * (startWorldPos - _rotPivotWorld);
                Vector3 newLocalPos = parentWorld.inverse.MultiplyPoint3x4(newWorldPos);

                mc.BoneTransform.UseLocalTransform = true;
                mc.BoneTransform.Rotation = NormEuler180(newLocalRot.eulerAngles);
                mc.BoneTransform.Position = newLocalPos;
            }

            model.ComputeWorldMatrices();

            // 子補正(MoveWithChildren==false): 直接の子のワールド姿勢・位置を開始時に戻す。
            // 4x4 の分解は使わず、親のワールド回転どうしの差分で子のローカル回転を作る。
            //   R_child_new = Inv(P_new.rotation) * P_old.rotation * R_child_old
            //   P_new / P_old は一様スケールなので .rotation は厳密。
            // ローカルスケールは変化しないので書き換えない。
            if (_rotChildStart.Count > 0)
            {
                foreach (var ckv in _rotChildStart)
                {
                    var childMc = model.GetMeshContext(ckv.Key);
                    if (childMc?.BoneTransform == null) continue;

                    var parentMc = model.GetMeshContext(childMc.HierarchyParentIndex);
                    if (parentMc == null) continue;

                    Matrix4x4 parentNew = parentMc.WorldMatrix;
                    Quaternion carry = Quaternion.Inverse(parentNew.rotation)
                                     * ckv.Value.ParentWorld.rotation;

                    childMc.BoneTransform.UseLocalTransform = true;
                    childMc.BoneTransform.Rotation =
                        NormEuler180((carry * ckv.Value.LocalRot).eulerAngles);
                    childMc.BoneTransform.Position =
                        parentNew.inverse.MultiplyPoint3x4(ckv.Value.WorldPos);
                }

                // 子補正後に再計算
                model.ComputeWorldMatrices();
            }

            // 原点だけ移動(OriginOnly): 対象 MeshFilter の自頂点を「開始ワールド位置を保つ」よう
            // 再ローカル化する。ApplyWorldDelta と同じ式で、回転でもそのまま成立する。
            if (_settings.OriginOnly && _originStartWorld.Count > 0)
            {
                foreach (var kv in _rotStartLocalRot)
                {
                    int idx = kv.Key;
                    if (!_originStartWorld.TryGetValue(idx, out var startWorld)) continue;
                    if (!_originStartPositions.TryGetValue(idx, out var startPos)) continue;
                    var mc = model.GetMeshContext(idx);
                    var mo = mc?.MeshObject;
                    if (mo == null) continue;

                    Matrix4x4 curInv = mc.WorldMatrixInverse;
                    int n = Mathf.Min(mo.VertexCount, startPos.Length);
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 worldPos = startWorld.MultiplyPoint3x4(startPos[i]);
                        var v = mo.Vertices[i];
                        v.Position = curInv.MultiplyPoint3x4(worldPos);
                        mo.Vertices[i] = v;
                    }
                    mo.InvalidatePositionCache();
                }
                // 書き換えた頂点を GPU へ同期する。
                ctx.SyncMesh?.Invoke();
            }

            // バインド連動(スキン固定): ApplyWorldDelta と同じ扱い。
            // World が変わったボーンの BindPose を更新し SkinningMatrix を開始時と同一に保つ。
            if (_settings.MoveMode == BoneMoveMode.BoneOnlyRebind && _rebindStartSkinning.Count > 0)
            {
                foreach (var kv in _rebindStartSkinning)
                {
                    var bmc = model.GetMeshContext(kv.Key);
                    if (bmc == null || bmc.Type != MeshType.Bone) continue;
                    bmc.BindPose = bmc.WorldMatrix.inverse * kv.Value;
                }
            }

            ctx.SyncBoneTransforms?.Invoke();
            ctx.Repaint?.Invoke();
        }

        /// <summary>回転ドラッグの開始状態を破棄する。</summary>
        private void ClearRotationStart()
        {
            _rotStartLocalRot.Clear();
            _rotStartWorldPos.Clear();
            _rotStartParentWorld.Clear();
            _rotChildStart.Clear();
            _rotPivotWorld = Vector3.zero;
            _rotWarnedNonUniform = false;
        }

        /// <summary>
        /// 自分を除く祖先チェーンに非一様スケール (Scale の x/y/z が不一致) が
        /// 含まれるかを判定する。循環階層でも止まるよう反復回数に上限を設ける。
        /// </summary>
        private static bool HasNonUniformScaleInAncestors(ModelContext model, int index)
        {
            if (model == null) return false;

            int count = model.Count;
            var self = model.GetMeshContext(index);
            if (self == null) return false;

            int cur = self.HierarchyParentIndex;
            for (int guard = 0; guard < count && cur >= 0 && cur < count; guard++)
            {
                var mc = model.GetMeshContext(cur);
                if (mc == null) break;

                var bt = mc.BoneTransform;
                if (bt != null && bt.UseLocalTransform && !IsUniformScale(bt.Scale))
                    return true;

                int next = mc.HierarchyParentIndex;
                if (next == cur) break;   // 自己参照
                cur = next;
            }

            return false;
        }

        private static bool IsUniformScale(Vector3 s)
        {
            const float Eps = 1e-5f;
            return Mathf.Abs(s.x - s.y) <= Eps && Mathf.Abs(s.y - s.z) <= Eps;
        }

        private static Vector3 NormEuler180(Vector3 e)
            => new Vector3(NormAngle180(e.x), NormAngle180(e.y), NormAngle180(e.z));

        private static float NormAngle180(float a)
        {
            a %= 360f;
            if (a >  180f) a -= 360f;
            else if (a < -180f) a += 360f;
            return a;
        }

        // ================================================================
        // Undo
        // ================================================================

        private void SaveSnapshots(ToolContext ctx)
        {
            _beforeSnapshots.Clear();
            var model = ctx?.Model;
            if (model == null) return;

            _originWorldTotal = Vector3.zero;

            // 選択アイテムのスナップショット
            foreach (int idx in AllSelectedIndices(ctx))
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;
                _beforeSnapshots[idx] = mc.BoneTransform.CreateSnapshot();
            }

            // MoveWithChildren == false の場合は直接の子も保存
            if (!_settings.MoveWithChildren)
            {
                var selectedSet = new HashSet<int>(_beforeSnapshots.Keys);
                for (int i = 0; i < model.Count; i++)
                {
                    if (_beforeSnapshots.ContainsKey(i)) continue;
                    var mc = model.GetMeshContext(i);
                    if (mc?.BoneTransform == null) continue;
                    if (selectedSet.Contains(mc.HierarchyParentIndex))
                        _beforeSnapshots[i] = mc.BoneTransform.CreateSnapshot();
                }
            }

            // A(スキン固定): 移動前の全ボーンの SkinningMatrix / BindPose をキャッシュ
            _rebindStartSkinning.Clear();
            _rebindStartBindPose.Clear();
            if (_settings.MoveMode == BoneMoveMode.BoneOnlyRebind)
            {
                for (int i = 0; i < model.Count; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.Bone) continue;
                    _rebindStartSkinning[i] = mc.SkinningMatrix;   // World × BindPose
                    _rebindStartBindPose[i] = mc.BindPose;
                }
            }

            // B(スキンごと確定): 移動前の頂点/ボーン状態をバックアップ（確定時の Undo 用）
            _freezeBefore = null;
            if (_settings.MoveMode == BoneMoveMode.SkinBakeRebind)
            {
                _freezeBefore = new Poly_Ling.Data.TPoseBackup();
                Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, _freezeBefore);
            }

            // 原点だけ移動(OriginOnly): MeshFilter(非スキン)の自頂点補償用に開始状態を保存。
            _originStartPositions.Clear();
            _originStartWorld.Clear();
            if (_settings.OriginOnly)
            {
                foreach (int idx in AllSelectedIndices(ctx))
                {
                    var mc = model.GetMeshContext(idx);
                    if (mc?.MeshObject == null) continue;
                    if (mc.Type != MeshType.Mesh || mc.IsSkinned) continue; // MeshFilterのみ
                    _originStartPositions[idx] = (UnityEngine.Vector3[])mc.MeshObject.Positions.Clone();
                    _originStartWorld[idx]     = mc.WorldMatrix;
                }
            }
        }

        private void CommitUndo(ToolContext ctx)
        {
            if (_beforeSnapshots.Count == 0) return;
            var model = ctx?.Model;
            if (model == null) return;

            var undoCtrl = ctx.UndoController;
            if (undoCtrl == null) return;

            // 原点だけ移動(OriginOnly): 対象MeshFilterの頂点+BoneTransform、および補償した子のBoneTransformを
            // 1グループ(1回のUndo)で記録する。MoveMode の記録経路はバイパスする。
            if (_settings.OriginOnly && _originStartPositions.Count > 0)
            {
                undoCtrl.SetModelContext(model);
                undoCtrl.MeshListStack.BeginGroup("原点だけ移動");
                var targetSet = new HashSet<int>(_originStartPositions.Keys);

                // 対象メッシュ: 頂点 + BoneTransform
                foreach (var kv in _originStartPositions)
                {
                    int idx = kv.Key;
                    var mc = model.GetMeshContext(idx);
                    if (mc?.MeshObject == null || mc.BoneTransform == null) continue;
                    int vc = mc.MeshObject.VertexCount;
                    var indices = new int[vc];
                    var newPos  = new Vector3[vc];
                    for (int i = 0; i < vc; i++) { indices[i] = i; newPos[i] = mc.MeshObject.Vertices[i].Position; }
                    undoCtrl.MeshListStack.Record(new Poly_Ling.UndoSystem.PivotMoveRecord
                    {
                        MasterIndex        = idx,
                        VertexIndices      = indices,
                        OldVertexPositions = kv.Value,
                        NewVertexPositions = newPos,
                        OldBoneTransform   = _beforeSnapshots.TryGetValue(idx, out var ob0) ? ob0 : mc.BoneTransform.CreateSnapshot(),
                        NewBoneTransform   = mc.BoneTransform.CreateSnapshot(),
                    }, "原点だけ移動");
                }

                // 補償した子(選択外): BoneTransform のみ（VertexIndices 空）
                foreach (var kv in _beforeSnapshots)
                {
                    int idx = kv.Key;
                    if (targetSet.Contains(idx)) continue;
                    var mc = model.GetMeshContext(idx);
                    if (mc?.BoneTransform == null) continue;
                    var after = mc.BoneTransform.CreateSnapshot();
                    if (!kv.Value.IsDifferentFrom(after)) continue;
                    undoCtrl.MeshListStack.Record(new Poly_Ling.UndoSystem.PivotMoveRecord
                    {
                        MasterIndex        = idx,
                        VertexIndices      = System.Array.Empty<int>(),
                        OldVertexPositions = System.Array.Empty<Vector3>(),
                        NewVertexPositions = System.Array.Empty<Vector3>(),
                        OldBoneTransform   = kv.Value,
                        NewBoneTransform   = after,
                    }, "原点だけ移動(子)");
                }

                undoCtrl.MeshListStack.EndGroup();
                undoCtrl.FocusMeshList();

                _beforeSnapshots.Clear();
                _rebindStartSkinning.Clear();
                _rebindStartBindPose.Clear();
                _originStartPositions.Clear();
                _originStartWorld.Clear();
                ctx.ExitTransformDragging?.Invoke();
                return;
            }

            // B(スキンごと確定): 頂点焼き込み＋リバインド。Tポーズ変換と同じ処理。
            if (_settings.MoveMode == BoneMoveMode.SkinBakeRebind)
            {
                model.ComputeWorldMatrices();
                Poly_Ling.Ops.TPoseConverter.BakeSkinnedVertices(model.MeshContextList);
                for (int i = 0; i < model.Count; i++)
                {
                    var mc = model.GetMeshContext(i);
                    if (mc == null || mc.Type != MeshType.Bone) continue;
                    mc.BindPose = mc.WorldMatrix.inverse;
                }

                if (_freezeBefore != null)
                {
                    var afterBackup = new Poly_Ling.Data.TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, afterBackup);

                    undoCtrl.SetModelContext(model);
                    var freezeRec = new TPoseUndoRecord(_freezeBefore, afterBackup,
                        model.TPoseBackup, model.TPoseBackup, "スキンごと確定");
                    {
                        string __dbgDesc = "スキンごと確定";
                        PLDiag.UndoRecord("MeshList", __dbgDesc, freezeRec);
                        undoCtrl.MeshListStack.Record(freezeRec, __dbgDesc);
                    }
                    undoCtrl.FocusMeshList();
                }

                _freezeBefore = null;
                model.IsDirty = true;
                model.OnListChanged?.Invoke();
                ctx.NotifyTopologyChanged?.Invoke();
                _beforeSnapshots.Clear();
                _rebindStartSkinning.Clear();
                _rebindStartBindPose.Clear();
                ctx.ExitTransformDragging?.Invoke();
                return;
            }

            // A(スキン固定): BoneTransform と BindPose を複合レコードで記録
            if (_settings.MoveMode == BoneMoveMode.BoneOnlyRebind)
            {
                var rebindRecord = new MultiBoneMoveRebindRecord();
                var handled = new HashSet<int>();

                foreach (var kvp in _beforeSnapshots)
                {
                    int idx = kvp.Key;
                    var mc = model.GetMeshContext(idx);
                    if (mc?.BoneTransform == null) continue;

                    var afterBT = mc.BoneTransform.CreateSnapshot();
                    bool btChanged = kvp.Value.IsDifferentFrom(afterBT);

                    Matrix4x4? oldBind = null, newBind = null;
                    if (_rebindStartBindPose.TryGetValue(idx, out var ob) && ob != mc.BindPose)
                    {
                        oldBind = ob; newBind = mc.BindPose;
                    }
                    if (!btChanged && oldBind == null) continue;

                    rebindRecord.Entries.Add(new MultiBoneMoveRebindRecord.Entry
                    {
                        MasterIndex      = idx,
                        OldBoneTransform = btChanged ? kvp.Value : (BoneTransformSnapshot?)null,
                        NewBoneTransform = btChanged ? afterBT   : (BoneTransformSnapshot?)null,
                        OldBindPose      = oldBind,
                        NewBindPose      = newBind,
                    });
                    handled.Add(idx);
                }

                foreach (var kv in _rebindStartBindPose)
                {
                    int idx = kv.Key;
                    if (handled.Contains(idx)) continue;
                    var mc = model.GetMeshContext(idx);
                    if (mc == null || kv.Value == mc.BindPose) continue;
                    rebindRecord.Entries.Add(new MultiBoneMoveRebindRecord.Entry
                    {
                        MasterIndex = idx,
                        OldBindPose = kv.Value,
                        NewBindPose = mc.BindPose,
                    });
                }

                if (rebindRecord.Entries.Count > 0)
                {
                    undoCtrl.SetModelContext(model);
                    {
                        string __dbgDesc = "ボーン移動(バインド連動)";
                        PLDiag.UndoRecord("MeshList", __dbgDesc, rebindRecord);
                        undoCtrl.MeshListStack.Record(rebindRecord, __dbgDesc);
                    }
                    undoCtrl.FocusMeshList();
                }

                model.OnListChanged?.Invoke();
                _beforeSnapshots.Clear();
                _rebindStartSkinning.Clear();
                _rebindStartBindPose.Clear();
                ctx.ExitTransformDragging?.Invoke();
                return;
            }

            var record = new MultiBoneTransformChangeRecord();
            foreach (var kvp in _beforeSnapshots)
            {
                int idx = kvp.Key;
                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;

                var after = mc.BoneTransform.CreateSnapshot();
                if (!kvp.Value.IsDifferentFrom(after)) continue;

                record.Entries.Add(new MultiBoneTransformChangeRecord.Entry
                {
                    MasterIndex = idx,
                    OldSnapshot = kvp.Value,
                    NewSnapshot = after,
                });
            }

            if (record.Entries.Count > 0)
            {
                undoCtrl.SetModelContext(model);
                {
                    string __dbgDesc = "オブジェクト移動";
                    PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                    undoCtrl.MeshListStack.Record(record, __dbgDesc);
                }
                undoCtrl.FocusMeshList();
            }

            model.OnListChanged?.Invoke();
            _beforeSnapshots.Clear();
            ctx.ExitTransformDragging?.Invoke();
        }
    }
}
