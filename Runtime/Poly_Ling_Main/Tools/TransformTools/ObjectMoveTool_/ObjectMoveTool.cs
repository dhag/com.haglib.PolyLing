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
                        ApplyFreeDelta(totalDelta, ctx);
                        _lastDragScreenPos = mousePos;
                    }
                    ctx.Repaint?.Invoke();
                    return true;
                }

                case DragState.CenterDragging:
                {
                    Vector2 frameDelta = mousePos - _lastDragScreenPos;
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
            return model.SelectedBoneIndices.Count + model.SelectedMeshIndices.Count;
        }

        /// <summary>選択中アイテム全インデックス（ボーン + メッシュ）</summary>
        private IEnumerable<int> AllSelectedIndices(ToolContext ctx)
        {
            var model = ctx?.Model;
            if (model == null) yield break;
            foreach (int i in model.SelectedBoneIndices) yield return i;
            foreach (int i in model.SelectedMeshIndices)
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
                // ボーンとメッシュ（Bone/Mesh/BakedMirrorなど）を対象にする
                // モーフ・剛体・ジョイント・グループは除外
                var t = mc.Type;
                if (t == MeshType.Morph || t == MeshType.RigidBody ||
                    t == MeshType.RigidBodyJoint || t == MeshType.Group)
                    continue;

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
                model.ToggleSelection(bestIndex);
            }
            else if (shift)
            {
                model.AddToSelection(bestIndex);
            }
            else
            {
                // 単一選択：カテゴリに応じて既存選択をクリアして選択
                model.SelectByTypeExclusive(bestIndex);
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
            ApplyWorldDelta(worldDelta, ctx);
        }

        private void ApplyAxisDelta(Vector2 screenDelta, ToolContext ctx)
        {
            Vector3 worldDelta = _axisGizmo.ComputeAxisDelta(screenDelta, _draggingAxis, ctx);
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
                mc.BoneTransform.UseLocalTransform = true;
                mc.BoneTransform.Position += worldDelta;
            }

            // 親の新 WorldMatrix を確定
            model.ComputeWorldMatrices();

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

            ctx.SyncBoneTransforms?.Invoke();
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
        }

        private void CommitUndo(ToolContext ctx)
        {
            if (_beforeSnapshots.Count == 0) return;
            var model = ctx?.Model;
            if (model == null) return;

            var undoCtrl = ctx.UndoController;
            if (undoCtrl == null) return;

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
                undoCtrl.MeshListStack.Record(record, "オブジェクト移動");
                undoCtrl.FocusMeshList();
            }

            model.OnListChanged?.Invoke();
            _beforeSnapshots.Clear();
            ctx.ExitTransformDragging?.Invoke();
        }
    }
}
