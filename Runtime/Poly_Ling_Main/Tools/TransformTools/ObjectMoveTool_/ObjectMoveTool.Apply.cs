// ObjectMoveTool.Apply.cs
// オブジェクト移動ツール：移動・回転の適用と Undo。
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
            _ringLastAngleDeg  = 0f;
            _ringLastAxisWorld = RotateRingGizmo.AxisVector(ringAxis);
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
            // 確定時にコマンドへ載せるため、実際に適用した角と軸を控える。
            _ringLastAngleDeg  = deltaDeg;
            _ringLastAxisWorld = worldAxis;
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
            _dragTargets.Clear();

            // 選択アイテムのスナップショット
            foreach (int idx in AllSelectedIndices(ctx))
            {
                var mc = model.GetMeshContext(idx);
                if (mc?.BoneTransform == null) continue;
                _beforeSnapshots[idx] = mc.BoneTransform.CreateSnapshot();
                // 補償した子と混ざらないよう、対象だけを別に控える。
                _dragTargets.Add(idx);
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
