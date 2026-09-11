// ObjectMoveTool.Origin.cs
// オブジェクト移動ツール：オブジェクトごと移動・回転（原点）・選択ヘルパー・ピッキング・ギズモ中心。
// Runtime/Poly_Ling_Main/Tools/TransformTools/ObjectMoveTool_/ に配置

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
    public partial class ObjectMoveTool
    {
        // ================================================================
        // オブジェクトごと移動・回転（OriginOnly でない通常の ObjectMove）
        //
        // 1 ドラッグ = 1 コマンド。ドラッグ中の適用はプレビュー扱いで、
        // 確定時に TryTake*Drag で開始状態へ戻し、Apply*FromCommand が
        // マウス経路と同じ SaveSnapshots → Apply → CommitUndo を通す。
        // ================================================================

        /// <summary>
        /// 開始状態を書き戻す。移動・回転どちらのドラッグでも材料は同じで、
        /// SaveSnapshots が保存した BoneTransform と BindPose を戻すだけ。
        /// 復元用の経路を別に作らない。
        /// </summary>
        private void RestoreDragStart(ModelContext model)
        {
            foreach (var kv in _beforeSnapshots)
            {
                var mc = model.GetMeshContext(kv.Key);
                if (mc?.BoneTransform == null) continue;
                mc.BoneTransform.ApplySnapshot(kv.Value);
            }

            // バインド連動（A）はドラッグ中に BindPose を書き換えるので、こちらも戻す。
            foreach (var kv in _rebindStartBindPose)
            {
                var mc = model.GetMeshContext(kv.Key);
                if (mc == null) continue;
                mc.BindPose = kv.Value;
            }

            model.ComputeWorldMatrices();
        }

        /// <summary>ドラッグ開始状態を捨てる。TryTake*Drag の後始末。</summary>
        private void ClearDragStart()
        {
            _beforeSnapshots.Clear();
            _rebindStartSkinning.Clear();
            _rebindStartBindPose.Clear();
            _originStartPositions.Clear();
            _originStartWorld.Clear();
            _originWorldTotal = Vector3.zero;
            _dragTargets.Clear();
            _freezeBefore = null;
        }

        /// <summary>
        /// 取り出せる「オブジェクトごと移動」のドラッグ結果があるか。
        /// TryTakeObjectMoveDrag が true を返す条件と同じ。
        /// </summary>
        public bool ObjectMoveDragPending
            => !_settings.OriginOnly
            && _dragTargets.Count > 0
            && _beforeSnapshots.Count > 0
            && _originWorldTotal.sqrMagnitude >= 1e-10f;

        /// <summary>
        /// 「オブジェクトごと移動」のドラッグ結果を取り出し、開始状態へ戻す。
        /// ObjectMoveToolHandler が MoveObjectsCommand を送り、実際の移動と
        /// Undo 記録は ApplyMoveFromCommand が行う。
        /// TryTakeOriginOnlyDrag と同じ形。
        /// </summary>
        public bool TryTakeObjectMoveDrag(
            ToolContext ctx, out int[] masterIndices, out Vector3 worldTotal)
        {
            masterIndices = System.Array.Empty<int>();
            worldTotal    = Vector3.zero;

            if (!ObjectMoveDragPending) return false;

            var model = ctx?.Model;
            if (model == null) return false;

            worldTotal    = _originWorldTotal;
            masterIndices = new List<int>(_dragTargets).ToArray();

            RestoreDragStart(model);
            ClearDragStart();

            ctx.SyncBoneTransforms?.Invoke();
            ctx.ExitTransformDragging?.Invoke();
            return true;
        }

        /// <summary>
        /// 取り出せる回転リングのドラッグ結果があるか。
        /// TryTakeObjectRotateDrag が true を返す条件と同じ。
        /// </summary>
        public bool ObjectRotateDragPending
            => _rotStartLocalRot.Count > 0
            && _beforeSnapshots.Count > 0
            && Mathf.Abs(_ringLastAngleDeg) > 1e-4f
            && _ringLastAxisWorld.sqrMagnitude > 1e-8f;

        /// <summary>
        /// 回転リングのドラッグ結果を取り出し、開始状態へ戻す。
        ///
        /// 【ピボット】
        ///   ドラッグ開始時の _rotPivotWorld（= _axisGizmo.Center = 対象の重心）を
        ///   そのまま返す。受け口が計算し直すとドラッグ後の重心になってしまい、
        ///   同じ結果にならない。
        /// </summary>
        public bool TryTakeObjectRotateDrag(
            ToolContext ctx,
            out int[] masterIndices, out Vector3 pivotWorld,
            out Vector3 axisWorld, out float angleDeg)
        {
            masterIndices = System.Array.Empty<int>();
            pivotWorld    = Vector3.zero;
            axisWorld     = Vector3.up;
            angleDeg      = 0f;

            if (!ObjectRotateDragPending) return false;

            var model = ctx?.Model;
            if (model == null) return false;

            pivotWorld    = _rotPivotWorld;
            axisWorld     = _ringLastAxisWorld;
            angleDeg      = _ringLastAngleDeg;
            masterIndices = new List<int>(_dragTargets).ToArray();

            RestoreDragStart(model);
            ClearDragStart();
            ClearRotationStart();
            _ringLastAngleDeg = 0f;

            ctx.SyncBoneTransforms?.Invoke();
            ctx.ExitTransformDragging?.Invoke();
            return true;
        }

        /// <summary>
        /// 「オブジェクトごと移動」をコマンドから実行する。
        /// ApplyOriginOnlyFromCommand と同じ形で、OriginOnly でないことだけが違う。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ApplyMoveFromCommand(
            ToolContext ctx, IReadOnlyList<int> masterIndices, Vector3 worldDelta, out string reason)
        {
            reason = null;

            if (_settings.OriginOnly)
            { reason = "このツールは原点だけ移動の設定です。MovePivotCommand を使ってください"; return false; }

            if (!TryResolveTargets(ctx, masterIndices, out var targets, out reason))
                return false;

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
        /// ピボット周りの回転をコマンドから実行する。
        ///
        /// 【マウス経路と同じ実装を通す】
        ///   TryBeginRingDrag（SaveRotationStart → SaveSnapshots）と
        ///   UpdateRingDrag（ApplyWorldRotation）と OnMouseUp（CommitUndo）を
        ///   同じ順序で呼ぶ。非一様スケールの祖先を持つ要素の除外は
        ///   SaveRotationStart の中にあるので、ここで書き足すものは無い。
        ///
        /// 【ピボット】
        ///   useSelectionCentroid が true のときは UpdateGizmoCenter が出す
        ///   対象の重心を使う。重心は対象だけで決まるので、隠れた状態には依存しない。
        /// </summary>
        /// <param name="reason">実行できなかった理由。成功時は null。</param>
        public bool ApplyRotateFromCommand(
            ToolContext ctx, IReadOnlyList<int> masterIndices,
            Vector3 pivotWorld, bool useSelectionCentroid,
            Vector3 axisWorld, float angleDeg, out string reason)
        {
            reason = null;

            if (!TryResolveTargets(ctx, masterIndices, out var targets, out reason))
                return false;

            if (axisWorld.sqrMagnitude < 1e-8f)
            { reason = "Axis が 0 ベクトルです"; return false; }
            if (Mathf.Abs(angleDeg) < 1e-4f)
            { reason = "回転角が 0 です"; return false; }

            _targetOverride = targets;
            try
            {
                if (useSelectionCentroid)
                {
                    UpdateGizmoCenter(ctx);
                    pivotWorld = _axisGizmo.Center;
                }

                SaveRotationStart(ctx, pivotWorld);
                if (_rotStartLocalRot.Count == 0)
                {
                    reason = "回転できる対象がありません（祖先に非一様スケールがある要素は除外されます）";
                    return false;
                }
                SaveSnapshots(ctx);

                ctx.EnterTransformDragging?.Invoke();
                ApplyWorldRotation(
                    Quaternion.AngleAxis(angleDeg, axisWorld.normalized), ctx);
                CommitUndo(ctx);
            }
            finally
            {
                ClearRotationStart();
                _targetOverride = null;
            }

            ctx.Repaint?.Invoke();
            return true;
        }

        /// <summary>
        /// コマンドの masterIndices を実在確認して集合にする。
        /// ApplyOriginOnlyFromCommand の同じ処理と規則をそろえる。
        /// </summary>
        private static bool TryResolveTargets(
            ToolContext ctx, IReadOnlyList<int> masterIndices,
            out HashSet<int> targets, out string reason)
        {
            targets = null;
            reason  = null;

            var model = ctx?.Model;
            if (model == null) { reason = "モデルがありません"; return false; }

            if (masterIndices == null || masterIndices.Count == 0)
            { reason = "対象が指定されていません"; return false; }

            targets = new HashSet<int>();
            foreach (int idx in masterIndices)
            {
                if (model.GetMeshContext(idx) == null)
                { reason = $"masterIndex {idx} のオブジェクトがありません"; return false; }
                targets.Add(idx);
            }
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
    }
}
