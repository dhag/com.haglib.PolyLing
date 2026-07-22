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

        private enum DragState { Idle, PendingDrag, AxisDragging, CenterDragging }
        private DragState _state = DragState.Idle;

        private AxisGizmo _axisGizmo = new AxisGizmo();
        private AxisGizmo.AxisType _draggingAxis = AxisGizmo.AxisType.None;
        private AxisGizmo.AxisType _hoveredAxis  = AxisGizmo.AxisType.None;

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
                var hitAxis = _axisGizmo.FindAxisAtScreenPos(mousePos, ctx);
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
            }

            // Idle 時はホバー更新
            if (_state == DragState.Idle && HasAnySelection(ctx))
            {
                UpdateGizmoCenter(ctx);
                var hovered = _axisGizmo.FindAxisAtScreenPos(mousePos, ctx);
                if (hovered != _hoveredAxis)
                {
                    _hoveredAxis = hovered;
                    _axisGizmo.HoveredAxis = _hoveredAxis;
                    ctx.Repaint?.Invoke();
                }
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
                    CommitUndo(ctx);
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
            ctx.Repaint?.Invoke();
            return handled;
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
            var hovered = _axisGizmo.FindAxisAtScreenPos(mousePos, ctx);
            if (hovered != _hoveredAxis)
            {
                _hoveredAxis = hovered;
                _axisGizmo.HoveredAxis = _hoveredAxis;
                ctx.Repaint?.Invoke();
            }
        }

        public void DrawGizmo(ToolContext ctx)
        {
            _lastCtx = ctx;
            if (!HasAnySelection(ctx)) return;

            UpdateGizmoCenter(ctx);

            if (_state == DragState.Idle)
            {
                _hoveredAxis = _axisGizmo.FindAxisAtScreenPos(_lastMousePos, ctx);
                _axisGizmo.HoveredAxis = _hoveredAxis;
            }

            _axisGizmo.Draw(ctx);
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

            UpdateGizmoCenter(ctx);
            _axisGizmo.HoveredAxis  = _hoveredAxis;
            _axisGizmo.DraggingAxis = _draggingAxis;
            _axisGizmo.GetScreenPositions(ctx, out origin, out xEnd, out yEnd, out zEnd);
            hoveredAxis = _hoveredAxis;
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

        /// <summary>選択中アイテム全インデックス（ボーン + メッシュ）</summary>
        private IEnumerable<int> AllSelectedIndices(ToolContext ctx)
        {
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
                // モーフ・剛体・ジョイント・グループは従来通り除外
                var t = mc.Type;
                if (t == MeshType.Morph || t == MeshType.RigidBody ||
                    t == MeshType.RigidBodyJoint || t == MeshType.Group)
                    continue;

                // ObjectMoveSettings のピック対象フィルタ:
                //   PickBones         : MeshType.Bone
                //   PickMeshesNoSkin  : MeshType.Mesh かつ HasBoneWeight == false
                //   PickMeshesSkinned : MeshType.Mesh かつ HasBoneWeight == true
                // Helper / BakedMirror / MirrorSide は従来互換で常にピック対象。
                if (t == MeshType.Bone)
                {
                    if (!_settings.PickBones) continue;
                }
                else if (t == MeshType.Mesh)
                {
                    bool skinned = mc.MeshObject != null && mc.MeshObject.HasBoneWeight;
                    if (skinned)
                    {
                        if (!_settings.PickMeshesSkinned) continue;
                    }
                    else
                    {
                        if (!_settings.PickMeshesNoSkin) continue;
                    }
                }

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
        // Undo
        // ================================================================

        private void SaveSnapshots(ToolContext ctx)
        {
            _beforeSnapshots.Clear();
            var model = ctx?.Model;
            if (model == null) return;

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
                    if (mc.Type != MeshType.Mesh || mc.MeshObject.HasBoneWeight) continue; // MeshFilterのみ
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
                        UnityEngine.Debug.Log("[UndoDbg] MeshList.Record desc=" + __dbgDesc + " type=" + ((freezeRec)?.GetType().Name ?? "<null>"));
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
                        UnityEngine.Debug.Log("[UndoDbg] MeshList.Record desc=" + __dbgDesc + " type=" + ((rebindRecord)?.GetType().Name ?? "<null>"));
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
                    UnityEngine.Debug.Log("[UndoDbg] MeshList.Record desc=" + __dbgDesc + " type=" + ((record)?.GetType().Name ?? "<null>"));
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
