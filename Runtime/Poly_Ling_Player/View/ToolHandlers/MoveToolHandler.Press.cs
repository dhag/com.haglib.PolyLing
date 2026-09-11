// MoveToolHandler.Press.cs
// 移動ツールハンドラ：押下・ドラッグ・クリックの入力処理と、押下時点から始める矩形／投げ縄選択。
// Runtime/Poly_Ling_Player/View/ToolHandlers/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Selection;
using Poly_Ling.UndoSystem;
using Poly_Ling.Diagnostics;

namespace Poly_Ling.Player
{
    public partial class MoveToolHandler
    {
        // ================================================================
        // IPlayerPressHandler（押下時追従）
        // ================================================================

        /// <summary>
        /// 左ボタン押下。押下位置を原点として対象を確定し、移動の準備まで済ませる。
        /// ここで BeginMove まで行うため、押下のたびに対象メッシュの Positions が
        /// 1 回コピーされる。移動せず離した場合はそのコピーが無駄になるが、
        /// 移動中につまづくよりクリック時に払う方が操作感が良い、という判断による。
        /// </summary>
        public void OnLeftButtonDown(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            ResetPressState();

            // 押下時追従の対象外（従来経路）。
            // OnDragStartExtra / GizmoHitTestOverride を持つツールは DragBegin で
            // 独自のドラッグセッションを張るため、押下時に先回りすると
            // そのフックが一度も呼ばれなくなる。ここは従来どおりに残す。
            if (IsRadiusDragMode) return;
            if (OnDragStartExtra != null || GizmoHitTestOverride != null) return;

            _shiftHeld = mods.Shift;
            _ctrlHeld  = mods.Ctrl;

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;

            var elem    = PlayerHoverElement.None;
            var axisHit = AxisGizmo.AxisType.None;

            // 選択専用モードは要素もギズモも掴まない。常に矩形／投げ縄へ進む。
            if (!SelectOnly)
            {
                var mode = _selectionOps.SelectionState?.Mode
                        ?? (MeshSelectMode.Vertex | MeshSelectMode.Edge |
                            MeshSelectMode.Face   | MeshSelectMode.Line);

                // 直前の PointerMove で GPU が確定したホバーをそのまま使う。
                // PointerDown ではホバーを再計算しない（判定位置が飛ぶため）。
                elem = GetHoverElement != null
                    ? GetHoverElement(mode)
                    : (hit.HasHit
                        ? new PlayerHoverElement { Kind = PlayerHoverKind.Vertex,
                              MeshIndex = hit.MeshIndex, VertexIndex = hit.VertexIndex }
                        : PlayerHoverElement.None);

                PLDiag.PickRec("MTH.Press",
                    (int)elem.Kind, elem.MeshIndex, elem.VertexIndex,
                    elem.EdgeV1, elem.EdgeV2, elem.FaceIndex,
                    screenPos.x, screenPos.y);

                // 軸ギズモに当たっていたら、押下追従のまま軸ドラッグとして開始する。
                //
                // 判定は elem.HasHit の判定より前で行う。ギズモの矢印は
                // 重心から画面上へオフセットして描かれるため、その上に頂点が
                // 無いのが普通で、後段に置くと軸を掴めなくなる。
                if (!SuppressBuiltinGizmo)
                {
                    UpdateAffectedVertices();
                    if (HasAnyAffected())
                    {
                        UpdateGizmoState(ctx);
                        axisHit = _axisGizmo.FindAxisAtScreenPos(ToImgui(screenPos), ctx);
                    }
                }
            }

            // 要素にもギズモにも当たっていない → 矩形／投げ縄選択を押下時点で開始する。
            if (axisHit == AxisGizmo.AxisType.None && !elem.HasHit)
            {
                BeginPressDragSelect(screenPos);
                return;
            }

            _elemOnMouseDown = elem;
            _pressOriginPos  = screenPos;

            // 未選択の要素を掴んだ場合はここで選択する。
            // Undo はまだ積まない（しきい値未満で離したら巻き戻すため）。
            // 軸ギズモを掴んだときは既存選択をその軸へ動かす操作なので選択を変えない。
            if (axisHit == AxisGizmo.AxisType.None && !IsElemSelected(elem))
            {
                _pressSelectionBefore  = CaptureAllSelectionSnapshots();
                _selectionOps.ApplyElementClick(elem, new ModifierKeys());
                _pressSelectionApplied = true;
            }

            UpdateAffectedVertices();
            if (!HasAnyAffected())
            {
                PLDiag.PickDump("no-affected-after-press");
                RollbackPressSelection();
                return;
            }

            BeginMove();
            if (_meshTransforms.Count == 0)
            {
                PLDiag.PickDump("press-begin-move-empty");
                RollbackPressSelection();
                return;
            }

            // 換算基準を固定する（以降 UpdateGizmoState は呼ばない）。
            // ギズモ表示もこの固定中心を使う。移動に応じて重心が動くと、
            // 換算倍率とギズモ原点の両方が毎フレームずれる。
            UpdateGizmoState(ctx);
            _frozenGizmoCenter = _axisGizmo.Center;
            _gizmoCenterFrozen = true;

            _pressAxis        = axisHit;
            _pressOriginImgui = ToImgui(screenPos);
            _draggingAxis     = axisHit;

            _mouseDownPos = screenPos;
            _dragMode     = DragMode.Moving;
            _state        = axisHit == AxisGizmo.AxisType.Center
                            ? MoveState.CenterDragging
                            : (axisHit == AxisGizmo.AxisType.None
                                ? MoveState.MovingVertices
                                : MoveState.AxisDragging);
            _pressActive  = true;

            // 掴んだ瞬間に選択を変えた場合は、TransformDragging へ入る前に
            // GPU へ同期させる。詳細は OnCommitSelectionSync の宣言部を参照。
            if (_pressSelectionApplied) OnCommitSelectionSync?.Invoke();

            // 以降ドラッグ終了まで GPU ヒットテストを止め、ホバー索引を凍結する。
            OnEnterTransformDragging?.Invoke();

            PLDiag.PickRec("MTH.PressBegin",
                _meshTransforms.Count, _affectedVertices.Count, CountAffectedVertices());
        }

        /// <summary>
        /// しきい値未満の移動。押下位置からの総差分を毎回まとめて換算し、絶対量で適用する。
        /// </summary>
        public void OnLeftPressMove(Vector2 screenPos, Vector2 delta, ModifierKeys mods)
        {
            if (!_pressActive) return;

            if (_pressDragSelect)
            {
                UpdatePressDragSelect(screenPos);
                return;
            }

            var ctx = GetToolContext?.Invoke();
            if (ctx == null) return;
            ApplyAbsoluteFromOrigin(screenPos, ctx);
        }

        // ================================================================
        // 押下時点から始める矩形／投げ縄選択
        // ================================================================

        /// <summary>
        /// 空振り押下で矩形／投げ縄選択を開始する。
        /// 移動と同じく押下位置を原点にし、1px の移動から伸び始める。
        /// SuppressDragSelect のときは何もしない（従来どおりドラッグ全体が無反応）。
        /// </summary>
        private void BeginPressDragSelect(Vector2 screenPos)
        {
            if (SuppressDragSelect) return;

            _pressOriginPos = screenPos;
            _mouseDownPos   = screenPos;
            _state          = MoveState.Idle;

            if (DragSelectMode == SelectionDragMode.Lasso)
            {
                _dragMode = DragMode.LassoSelecting;
                _selectionOps.BeginLassoSelect(screenPos);
            }
            else
            {
                _dragMode = DragMode.BoxSelecting;
                _selectionOps.BeginBoxSelect(screenPos);
            }
            OnEnterBoxSelecting?.Invoke();

            _pressActive     = true;
            _pressDragSelect = true;

            PLDiag.PickRec("MTH.PressDragSelect",
                (int)_dragMode, x: screenPos.x, y: screenPos.y);
        }

        /// <summary>押下フェーズ中の矩形／投げ縄更新。OnLeftDrag の分岐と同じ内容。</summary>
        private void UpdatePressDragSelect(Vector2 screenPos)
        {
            if (_dragMode == DragMode.BoxSelecting)
            {
                _selectionOps.UpdateBoxSelect(screenPos);
                OnBoxSelectUpdate?.Invoke(_selectionOps.BoxStart, screenPos);
                OnRepaint?.Invoke();
            }
            else if (_dragMode == DragMode.LassoSelecting)
            {
                _selectionOps.UpdateLassoSelect(screenPos);
                OnLassoSelectUpdate?.Invoke(_selectionOps.LassoPoints);
                OnRepaint?.Invoke();
            }
        }

        /// <summary>
        /// しきい値未満で離されたときの矩形／投げ縄の取り消し。
        /// 頂点位置も選択も触っていないので、オーバーレイを消して状態を戻すだけでよい。
        /// </summary>
        private void CancelPressDragSelect()
        {
            if (_dragMode == DragMode.LassoSelecting) OnLassoSelectEnd?.Invoke();
            else                                      OnBoxSelectEnd?.Invoke();
            OnExitBoxSelecting?.Invoke();

            _dragMode = DragMode.None;
            _state    = MoveState.Idle;
        }

        /// <summary>
        /// しきい値を越えずに離された。押下時に開始した移動と選択を巻き戻す。
        /// 呼び出し元（PlayerVertexInteractor）はこの直後に OnLeftClick を呼ぶ。
        /// </summary>
        public void OnLeftPressCancel(Vector2 screenPos, ModifierKeys mods)
        {
            if (!_pressActive) return;

            if (_pressDragSelect)
            {
                PLDiag.PickRec("MTH.CancelDragSelect",
                    (int)_dragMode, x: screenPos.x, y: screenPos.y);
                CancelPressDragSelect();
                ResetPressState();
                return;
            }

            PLDiag.PickRec("MTH.Cancel",
                _meshTransforms.Count, _pressMoveStarted ? 1 : 0,
                _pressSelectionApplied ? 1 : 0,
                x: screenPos.x, y: screenPos.y);

            // 頂点位置を押下時点へ戻す。IVertexTransform は元位置を保持しているので
            // 累積量をゼロにするだけで復元できる。
            if (_pressMoveStarted)
                SetTotalDeltaAll(Vector3.zero);

            RollbackPressSelection();

            _meshTransforms.Clear();
            _state        = MoveState.Idle;
            _dragMode     = DragMode.None;
            _draggingAxis = AxisGizmo.AxisType.None;
            ResetPressState();

            // OnLeftButtonDown で入った TransformDragging を必ず抜ける。
            // 抜けないと GPU ヒットテストが止まったままホバーが更新されない。
            OnExitTransformDragging?.Invoke();
        }

        /// <summary>押下時に変更した選択を戻す。変更していなければ何もしない。</summary>
        private void RollbackPressSelection()
        {
            if (!_pressSelectionApplied || _pressSelectionBefore == null) return;

            var model = _project?.CurrentModel;
            if (model != null)
            {
                foreach (var kv in _pressSelectionBefore)
                {
                    var sel = model.GetMeshContext(kv.Key)?.Selection;
                    sel?.RestoreFromSnapshot(kv.Value);
                }
            }
            _pressSelectionApplied = false;
            _pressSelectionBefore  = null;
        }

        private void ResetPressState()
        {
            _pressActive           = false;
            _pressMoveStarted      = false;
            _pressSelectionApplied = false;
            _pressSelectionBefore  = null;
            _gizmoCenterFrozen     = false;
            _pressAxis             = AxisGizmo.AxisType.None;
            _pressDragSelect       = false;
        }

        /// <summary>
        /// 押下位置から現在位置までの総スクリーン差分を 1 回で換算し、絶対量として適用する。
        /// 差分の足し込みではないため、換算誤差が累積しない。
        ///
        /// 軸ギズモを掴んでいる場合も同じ規則で、押下位置からの総差分を
        /// 軸方向へ 1 回で射影する。Center は押下時の重心で固定してあるので、
        /// 移動しても換算倍率と軸のスクリーン方向が変わらない。
        /// </summary>
        private void ApplyAbsoluteFromOrigin(Vector2 screenPos, ToolContext ctx)
        {
            if (_gizmoCenterFrozen) _axisGizmo.Center = _frozenGizmoCenter;

            Vector3 worldTotal;
            if (_pressAxis != AxisGizmo.AxisType.None && _pressAxis != AxisGizmo.AxisType.Center)
            {
                // ComputeAxisDelta は +Y が画面下の差分を要求する（AxisGizmo の規約）。
                // ToImgui 済み座標どうしの差分をそのまま渡す。
                Vector2 sdYDown = ToImgui(screenPos) - _pressOriginImgui;
                worldTotal = _axisGizmo.ComputeAxisDelta(sdYDown, _pressAxis, ctx);
            }
            else
            {
                // 自由移動（ギズモ中央を掴んだ場合も含む）。
                // ComputeFreeDelta は +Y が画面上の差分を要求する。
                worldTotal = _axisGizmo.ComputeFreeDelta(screenPos - _pressOriginPos, ctx);
            }

            // 絶対指定なので加算ではなく代入する。
            _dragWorldTotal = worldTotal;
            SetTotalDeltaAll(worldTotal);
            _pressMoveStarted = true;
        }

        /// <summary>累積移動量を全対象メッシュへ絶対値で設定し、GPU へ同期する。</summary>
        private void SetTotalDeltaAll(Vector3 worldTotal)
        {
            var model = _project?.CurrentModel;
            foreach (var kv in _meshTransforms)
            {
                var mc = model?.GetMeshContext(kv.Key);
                // IVertexTransform はローカル座標へ加算するため、メッシュごとにローカル化する。
                Vector3 localTotal = mc != null
                    ? mc.WorldMatrixInverse.MultiplyVector(worldTotal)
                    : worldTotal;
                kv.Value.SetTotalDelta(localTotal);
                if (mc != null) OnSyncMeshPositions?.Invoke(mc);
            }
            OnRepaint?.Invoke();
        }

        public void OnLeftDragBegin(PlayerHitResult hit, Vector2 screenPos, ModifierKeys mods)
        {
            // 押下時に対象・原点・transform を確定済みなら、ここでやり直さない。
            // やり直すと BeginMove が「移動後の位置」を元位置として取り込んでしまう。
            if (_pressActive)
            {
                PLDiag.PickRec("MTH.DragBeginPress",
                    (int)_elemOnMouseDown.Kind, _elemOnMouseDown.MeshIndex,
                    _elemOnMouseDown.VertexIndex, _meshTransforms.Count,
                    _affectedVertices.Count, CountAffectedVertices(),
                    _pressOriginPos.x, _pressOriginPos.y);
                return;
            }

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

            // 診断: 掴んだ要素と押下位置を記録する。_mouseDownPos は押下位置、
            // _elemOnMouseDown は「しきい値を越えた現在位置」のホバー由来。
            PLDiag.PickRec("MTH.DragBegin",
                (int)_elemOnMouseDown.Kind, _elemOnMouseDown.MeshIndex,
                _elemOnMouseDown.VertexIndex, _elemOnMouseDown.EdgeV1,
                _elemOnMouseDown.EdgeV2, _elemOnMouseDown.FaceIndex,
                _mouseDownPos.x, _mouseDownPos.y);

            // 診断: ドラッグ開始と同じフレームでホバー要素が入れ替わっていたら、
            // 「押下時に見えていた色」と「掴んだ要素」が食い違いうる状態だった。
            if (PLDiag.LastHoverFrame == UnityEngine.Time.frameCount && PLDiag.LastHoverChanged)
                PLDiag.PickDump("hover-changed-at-dragbegin");

            // 診断: 掴んだメッシュが操作対象 (SelectedDrawableMeshIndices) に無い場合、
            // 選択は書けるが移動対象にならない。UpdateAffectedVertices と経路が食い違う。
            if (_elemOnMouseDown.HasHit && !IsMeshOperable(_elemOnMouseDown.MeshIndex))
                PLDiag.PickDump("hover-mesh-not-operable");

            // 軸ギズモヒットテスト（最優先）。SelectOnly 時は移動を一切行わないためスキップし、
            // 常に box/lasso 選択へ進む。SuppressBuiltinGizmo 時も組み込み移動ギズモは使わない。
            var ctx = GetToolContext?.Invoke();
            if (!SelectOnly && !SuppressBuiltinGizmo && ctx != null)
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

            // 将来ギズモ（回転/拡大等）のヒットテスト差し替え。当たったらツール操作フックへ委譲する。
            // （PendingAction 経由で OnDragStartExtra が呼ばれ、ツール側が処理する）。
            if (!SelectOnly && GizmoHitTestOverride != null && ctx != null
                && GizmoHitTestOverride(screenPos, ctx))
            {
                // 掴んだのはギズモであって要素ではない。ここで _elemOnMouseDown を
                // 残すと、しきい値超過時の選択差し替え（PendingAction の
                // ApplyElementClick）が走り、カーソル下の未選択頂点1個へ選択が
                // 潰れる。複数頂点を選んでギズモを回しても1頂点しか動かなくなる。
                _elemOnMouseDown = PlayerHoverElement.None;

                UpdateAffectedVertices();
                _state    = MoveState.PendingAction;
                _dragMode = DragMode.Moving;
                return;
            }

            // 要素ヒット → PendingAction（SelectOnly 時は移動/ツール操作を行わず box/lasso 選択へ）
            UpdateAffectedVertices();
            if (!SelectOnly && _elemOnMouseDown.HasHit)
            {
                _state    = MoveState.PendingAction;
                _dragMode = DragMode.Moving;
                return;
            }

            // 矩形/投げ縄選択開始
            // SuppressDragSelect 時は開始しない。_dragMode/_state を Idle のまま残すので
            // 以降の OnLeftDrag は switch のどの case にも入らず、OnLeftDragEnd も
            // 末尾のリセットへ落ちるだけになる（＝ドラッグ全体が無反応）。
            if (SuppressDragSelect)
            {
                _dragMode = DragMode.None;
                _state    = MoveState.Idle;
                return;
            }

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
                        // ヒット要素が未選択なら選択。
                        // ここはしきい値を越えてドラッグへ移行した時点の確定選択で、
                        // 巻き戻らない（押下時の暫定選択は RollbackPressSelection 側）。
                        // よってコマンドとして発行し、書き込み・頂点展開・選択 Undo は
                        // ディスパッチャ経由で ExecuteFromCommand へ戻す。
                        if (_elemOnMouseDown.HasHit && !IsElemSelected(_elemOnMouseDown))
                        {
                            if (SendCommand != null &&
                                _selectionOps.ResolveElementClick(
                                    _elemOnMouseDown, new ModifierKeys(), out var pr))
                            {
                                SendCommand(new Poly_Ling.Data.SelectElementsCommand(
                                    _project?.CurrentModelIndex ?? 0,
                                    pr.ClearTargets,
                                    pr.VertexIndices, pr.VertexMeshIndices,
                                    pr.EdgePairs,     pr.EdgeMeshIndices,
                                    pr.FaceIndices,   pr.FaceMeshIndices,
                                    pr.LineIndices,   pr.LineMeshIndices,
                                    pr.Op));
                            }

                            // 下の OnEnterTransformDragging で TransformDragging へ入ると
                            // 選択が GPU へ届かなくなるため、ここで同期的に反映させる。
                            // 詳細は OnCommitSelectionSync の宣言部を参照。
                            OnCommitSelectionSync?.Invoke();
                        }

                        UpdateAffectedVertices();

                        // 診断: 選択適用後の対象集計結果を記録する。
                        PLDiag.PickRec("MTH.Pending",
                            (int)_elemOnMouseDown.Kind, _elemOnMouseDown.MeshIndex,
                            IsElemSelected(_elemOnMouseDown) ? 1 : 0,
                            _project?.CurrentModel?.SelectedDrawableMeshIndices?.Count ?? -1,
                            _affectedVertices.Count, CountAffectedVertices(),
                            screenPos.x, screenPos.y);

                        // 診断: 要素を掴んでいるのに移動対象が 0 件。
                        // 「選択と同時の移動が反映されない」の直接条件。
                        if (_elemOnMouseDown.HasHit && !HasAnyAffected())
                            PLDiag.PickDump("no-affected-after-select");

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

                        // TransformDragging へ入る。ここで呼ばないと、この経路だけ
                        // アダプタが Normal モードのままになり、ドラッグ中も発火し続ける
                        // OnPointerHover（PlayerViewportPanel.OnPointerMove）から
                        // NotifyPointerHover → ProcessMouseUpdate が走って
                        // ホバー索引が別の要素へ書き換わる。
                        // UpdateModeProfile.TransformDragging は AllowHitTest=false なので、
                        // 入ってさえいれば再計算は止まる。
                        // 終了側 OnLeftDragEnd は MovingVertices でも
                        // OnExitTransformDragging を呼んでおり、これで対になる。
                        OnEnterTransformDragging?.Invoke();

                        // 診断: transform が 0 件のまま MovingVertices へ入ると、
                        // 以降の ApplyDelta が何も動かさずドラッグが空振りになる。
                        PLDiag.PickRec("MTH.BeginMove",
                            _meshTransforms.Count, _affectedVertices.Count, CountAffectedVertices());
                        if (_meshTransforms.Count == 0)
                            PLDiag.PickDump("begin-move-empty");

                        // 押下位置から現在位置までの移動量を取りこぼさずに適用する。
                        // ここで適用しないと、ドラッグ開始しきい値（パネル側
                        // PlayerViewportPanel.DragThreshold と本クラスの DragThreshold）
                        // ぶんの移動が捨てられ、カーソルと対象の間に恒久的なオフセットが残る。
                        // _mouseDownPos は OnLeftDragBegin で受け取った押下位置。
                        if (ctx != null) ApplyFreeDelta(screenPos - _mouseDownPos, ctx);

                        OnRepaint?.Invoke();
                    }
                    break;

                case MoveState.MovingVertices:
                case MoveState.CenterDragging:
                    if (ctx == null) break;
                    // 押下時追従が有効なら押下位置からの総差分を絶対量で適用する。
                    // 無効な経路（旧ディスパッチャ等）は従来の差分累積のまま。
                    if (_pressActive) ApplyAbsoluteFromOrigin(screenPos, ctx);
                    else              ApplyFreeDelta(delta, ctx);
                    break;

                case MoveState.AxisDragging:
                    if (ctx == null) break;
                    if (_pressActive)
                    {
                        // 押下位置からの総差分を軸方向へ 1 回で射影して絶対量で適用する。
                        ApplyAbsoluteFromOrigin(screenPos, ctx);
                    }
                    else
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
            // 押下時に適用した選択変更は、ドラッグが確定したこの時点で Undo へ積む。
            // （しきい値未満で離した場合は OnLeftPressCancel で巻き戻すため積まない）
            if (_pressSelectionApplied && _pressSelectionBefore != null)
            {
                RecordSelectionChange(_pressSelectionBefore, CaptureAllSelectionSnapshots());
                _pressSelectionApplied = false;
                _pressSelectionBefore  = null;
            }

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
                ResetPressState();   // 押下時に開始した場合の後片付け
                OnClearMouseHover?.Invoke();
                FireOneShotFinished();
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
                ResetPressState();   // 押下時に開始した場合の後片付け
                OnClearMouseHover?.Invoke();
                FireOneShotFinished();
                return;
            }

            bool moved = _state == MoveState.MovingVertices
                      || _state == MoveState.AxisDragging
                      || _state == MoveState.CenterDragging;
            if (moved)
            {
                CommitDragMove();
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
            ResetPressState();
            OnClearMouseHover?.Invoke();
        }
    }
}
