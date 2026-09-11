// Tools/TransformTools/ObjectMoveTool_/ObjectMoveTool.cs
// MeshFilter オブジェクトおよび SkinnedMeshRenderer ボーン位置を移動するツール
// 頂点移動ツール(MoveTool)と同じ操作感を目指す
// - AxisGizmo による軸拘束移動・中央自由移動
// - Shift/Ctrl による複数選択
// - 子ボーン一緒に移動 / 独立モード
// - Undo 対応 (MultiBoneTransformChangeRecord)
//
// 【分割先】このファイルから次へ分けてある。
//   ObjectMoveTool.Apply.cs   オブジェクト移動ツール：移動・回転の適用と Undo。
//   ObjectMoveTool.Origin.cs  オブジェクト移動ツール：オブジェクトごと移動・回転（原点）・選択ヘルパー・ピッキング・ギズモ中心。

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

        // ドラッグ開始時点の対象集合（補償した子は含まない）。SaveSnapshots で作る。
        // _beforeSnapshots には MoveWithChildren == false のとき直接の子も混ざるため、
        // コマンドの MasterIndices にはこちらを使う。
        private readonly HashSet<int> _dragTargets = new HashSet<int>();

        // リングドラッグの累計角と、そのとき使ったワールド軸。UpdateRingDrag で更新する。
        // 1 ドラッグ = 1 コマンドにするとき、確定時にこの 2 つがコマンドの値になる。
        private float   _ringLastAngleDeg;
        private Vector3 _ringLastAxisWorld;

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
                    //
                    // オブジェクトごと移動・回転も同じ形にしてあるが、そちらは
                    // TryTake*Drag が _beforeSnapshots を空にするため CommitUndo が
                    // 先頭で戻る。ガードを足すと、送信口が無いときに Undo が
                    // 積まれなくなるので足さない。
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
    }
}
