// MoveToolHandler.Move.cs
// 移動ツールハンドラ：ギズモ・選択スナップショットと Undo・影響頂点・移動の開始／適用／確定。
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
            if (SelectOnly) return false;   // 選択専用モードではギズモを描画しない
            if (SuppressBuiltinGizmo) return false;   // 自前ギズモを使うモードでは組み込みギズモを描画しない
            if (ctx == null) return false;
            UpdateAffectedVertices();
            if (!HasAnyAffected()) return false;

            // 押下中は換算基準と同じ固定重心を使う。UpdateGizmoState を呼ぶと
            // 移動後の頂点位置から重心を計算し直すため、ギズモ原点が
            // 掴んだ対象と一緒に動いてしまう。
            if (_gizmoCenterFrozen) _axisGizmo.Center = _frozenGizmoCenter;
            else                    UpdateGizmoState(ctx);

            _axisGizmo.HoveredAxis  = _hoveredAxis;
            _axisGizmo.DraggingAxis = _draggingAxis;
            _axisGizmo.GetScreenPositions(ctx, out origin, out xEnd, out yEnd, out zEnd);
            hoveredAxis = _hoveredAxis;
            return true;
        }

        /// <summary>
        /// ギズモ表示データを組み立てる（IPlayerGizmoProvider）。
        /// 頂点移動は矢印スタイル（キューブ / ダイヤ / リングのいずれも立てない）。
        /// </summary>
        public bool TryBuildGizmoData(ToolContext ctx, out PlayerViewportPanel.GizmoData data)
        {
            data = default;
            if (!TryGetGizmoScreenPositions(ctx, out var o, out var xe, out var ye, out var ze, out var ha))
                return false;

            data = new PlayerViewportPanel.GizmoData
            {
                HasGizmo    = true,
                Origin      = o, XEnd = xe, YEnd = ye, ZEnd = ze,
                HoveredAxis = ha,
            };
            return true;
        }

        /// <summary>ポインター移動時に呼んでホバー軸を更新する。</summary>
        public void UpdateHover(Vector2 screenPos, ToolContext ctx)
        {
            _lastMousePos = ToImgui(screenPos);
            if (SelectOnly) return;   // 選択専用モードではギズモ軸ホバーを更新しない
            if (SuppressBuiltinGizmo) return;   // 自前ギズモを使うモードでは組み込みギズモをホバーしない
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

        /// <summary>
        /// 選択メッシュ全ての選択状態を、MeshContextList インデックス付きで控える。
        ///
        /// SelectionSnapshot はメッシュ内ローカル番号を持つため 1 メッシュしか表せない。
        /// 本ハンドラのクリック／矩形／投げ縄は複数メッシュにまたがるので、
        /// メッシュごとに 1 個ずつ控えて MultiMeshSelectionChangeRecord に渡す。
        /// </summary>
        private Dictionary<int, SelectionSnapshot> CaptureAllSelectionSnapshots()
            => CaptureAllSelectionSnapshots(null);

        /// <summary>
        /// 選択メッシュのスナップショットを取る。extra を渡すと、選択メッシュに
        /// 入っていないものも対象へ足す。
        ///
        /// コマンドは選択に入っていないメッシュも指定できる。足しておかないと
        /// before / after のどちらにも現れず、RecordSelectionChange が差分なしと
        /// 判断して Undo が積まれない。
        /// </summary>
        private Dictionary<int, SelectionSnapshot> CaptureAllSelectionSnapshots(
            IEnumerable<int> extra)
        {
            var result = new Dictionary<int, SelectionSnapshot>();
            var model  = _project?.CurrentModel;
            if (model == null) return result;

            var indices = new HashSet<int>(model.SelectedDrawableMeshIndices);
            if (extra != null) foreach (int i in extra) indices.Add(i);

            foreach (int ctxIdx in indices)
            {
                var mc  = model.GetMeshContext(ctxIdx);
                var sel = mc?.Selection;
                if (sel == null) continue;

                result[ctxIdx] = new SelectionSnapshot
                {
                    Mode     = sel.Mode,
                    Vertices = new HashSet<int>(sel.Vertices),
                    Edges    = new HashSet<VertexPair>(sel.Edges),
                    Faces    = new HashSet<int>(sel.Faces),
                    Lines    = new HashSet<int>(sel.Lines),
                };
            }
            return result;
        }

        /// <summary>指定モードの空スナップショット（片側にしか存在しないメッシュの補完用）。</summary>
        private static SelectionSnapshot EmptySelectionSnapshot(MeshSelectMode mode)
        {
            return new SelectionSnapshot
            {
                Mode     = mode,
                Vertices = new HashSet<int>(),
                Edges    = new HashSet<VertexPair>(),
                Faces    = new HashSet<int>(),
                Lines    = new HashSet<int>(),
            };
        }

        /// <summary>
        /// 選択変更を Undo スタックへ記録する。
        ///
        /// 変化のあったメッシュだけをエントリ化し、1 件も無ければ記録しない。
        /// Record は MultiMeshSelectionChangeRecord を使う。SelectionChangeRecord は
        /// 復元先が ActiveMeshContext 固定のため、複数メッシュには使えない。
        /// </summary>
        private void RecordSelectionChange(
            Dictionary<int, SelectionSnapshot> before,
            Dictionary<int, SelectionSnapshot> after)
        {
            if (_undoController == null || before == null || after == null) return;

            var model = _project?.CurrentModel;
            if (model == null) return;

            // before / after の和集合で比較する。
            // 途中でメッシュ選択自体が変わった場合に片側にしか無いキーが出るため。
            var keys = new HashSet<int>(before.Keys);
            keys.UnionWith(after.Keys);

            var entries = new List<MeshSelectionEntry>();
            foreach (int ctxIdx in keys)
            {
                before.TryGetValue(ctxIdx, out var b);
                after.TryGetValue(ctxIdx, out var a);

                if (b == null) b = EmptySelectionSnapshot(a?.Mode ?? MeshSelectMode.Vertex);
                if (a == null) a = EmptySelectionSnapshot(b.Mode);

                if (!b.IsDifferentFrom(a)) continue;

                entries.Add(new MeshSelectionEntry
                {
                    MeshContextIndex = ctxIdx,
                    Old              = b,
                    New              = a,
                });
            }

            if (entries.Count == 0) return;

            _undoController.MeshUndoContext.ParentModelContext = model;
            var record = new MultiMeshSelectionChangeRecord(entries.ToArray());
            _undoController.FocusVertexEdit();
            Poly_Ling.Diagnostics.PLDiag.UndoVerboseLog(
                $"Push MultiMeshSelectionChangeRecord (model={model.Name}, " +
                $"entries={entries.Count})");
            _undoController.VertexEditStack.Record(record, "選択変更");
            Poly_Ling.Diagnostics.PLDiag.UndoVerboseLog(
                $"  after Record: VertexEdit.Undo={_undoController.VertexEditStack.UndoCount}, " +
                $"VertexEdit.Pending={_undoController.VertexEditStack.PendingCount}, " +
                $"MeshList.Undo={_undoController.MeshListStack.UndoCount}, " +
                $"MeshList.Pending={_undoController.MeshListStack.PendingCount}");
        }

        /// <summary>
        /// 移動対象頂点を集計する。
        ///
        /// 選択メッシュ（ModelContext.SelectedDrawableMeshIndices）を全て走査し、
        /// 各 MeshContext.Selection から自メッシュぶんの頂点を集める。
        /// SelectionState の Vertices/Faces/Lines はメッシュ内ローカル番号のため、
        /// 単一 SelectionState を全メッシュに適用してはならない。
        ///
        /// 集計後の _affectedVertices は BeginMove / UpdateGizmoState / ApplyDelta /
        /// EndMove がそのまま複数メッシュとして扱う（いずれも元から辞書全走査）。
        /// </summary>
        private void UpdateAffectedVertices()
        {
            _affectedVertices.Clear();
            var model = _project?.CurrentModel;
            if (model == null) return;

            // _targetOverride が入っているときは選択メッシュではなくそちらを回す
            // （コマンド経由。ExecuteFromCommand が設定する）。
            // 各メッシュの中で「どの要素が選ばれているか」は SelectionState を見る点が
            // マウス経路と共通で、絞るのは対象メッシュだけ。
            IEnumerable<int> sourceIndices =
                _targetOverride ?? (IEnumerable<int>)model.SelectedDrawableMeshIndices;

            foreach (int ctxIdx in sourceIndices)
            {
                var mc = model.GetMeshContext(ctxIdx);
                if (mc?.MeshObject == null) continue;

                var sel = mc.Selection;
                if (sel == null) continue;

                var mo       = mc.MeshObject;
                var affected = new HashSet<int>();

                // 選択モードで無効な種別は移動対象に入れない。
                // これを見ないと「頂点だけチェックしているのに辺が動く」状態になる
                // （モードを絞るツールから戻った直後や、クリアが間に合っていない場合）。
                var selMode = sel.Mode;

                if (selMode.Has(MeshSelectMode.Vertex))
                    foreach (var v  in sel.Vertices) affected.Add(v);
                if (selMode.Has(MeshSelectMode.Edge))
                    foreach (var e  in sel.Edges)    { affected.Add(e.V1); affected.Add(e.V2); }
                if (selMode.Has(MeshSelectMode.Face))
                    foreach (var fi in sel.Faces)
                        if (fi >= 0 && fi < mo.FaceCount)
                            foreach (var vi in mo.Faces[fi].VertexIndices)
                                affected.Add(vi);
                if (selMode.Has(MeshSelectMode.Line))
                    foreach (var li in sel.Lines)
                        if (li >= 0 && li < mo.FaceCount)
                        {
                            var face = mo.Faces[li];
                            if (face.VertexCount == 2)
                            { affected.Add(face.VertexIndices[0]); affected.Add(face.VertexIndices[1]); }
                        }

                if (affected.Count > 0) _affectedVertices[ctxIdx] = affected;
            }
        }

        private bool HasAnyAffected()
        {
            foreach (var kv in _affectedVertices)
                if (kv.Value.Count > 0) return true;
            return false;
        }

        /// <summary>診断用: _affectedVertices の頂点総数。</summary>
        private int CountAffectedVertices()
        {
            int n = 0;
            foreach (var kv in _affectedVertices) n += kv.Value.Count;
            return n;
        }

        /// <summary>
        /// 診断用: 操作対象メッシュの選択要素総数（頂点 + 辺 + 面 + 線分）。
        /// _affectedVertices を変更しないため、クリック経路でも安全に呼べる。
        /// </summary>
        private int CountSelectedElements()
        {
            var model = _project?.CurrentModel;
            if (model == null) return -1;
            int n = 0;
            foreach (int ctxIdx in model.SelectedDrawableMeshIndices)
            {
                var sel = model.GetMeshContext(ctxIdx)?.Selection;
                if (sel == null) continue;
                n += sel.Vertices.Count + sel.Edges.Count + sel.Faces.Count + sel.Lines.Count;
            }
            return n;
        }

        /// <summary>
        /// 診断用: 指定 MeshContextList インデックスが操作対象
        /// (ModelContext.SelectedDrawableMeshIndices) に含まれるか。
        ///
        /// ホバーの可否は GPU 側 (FlagManager.IsMeshSelected) が決めるため、
        /// ここと食い違うと「ホバーはできるが移動対象にならない」状態になる。
        /// </summary>
        private bool IsMeshOperable(int meshContextIndex)
        {
            if (meshContextIndex < 0) return false;
            var list = _project?.CurrentModel?.SelectedDrawableMeshIndices;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] == meshContextIndex) return true;
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
                // ローカル頂点をワールド変換してから集計する。
                // WorldToScreenPos はワールド空間を期待するため、この変換が抜けると
                // Player（WorldMatrix 非 identity）でギズモが実頂点から離れて描画される。
                // 変換は頂点単位で行う。スキンド頂点に実際に適用される行列は
                // メッシュの WorldMatrix ではなくボーンの SkinningMatrix のブレンドであり
                // （MeshContext.VertexMatrix）、メッシュの行列を使うとギズモが
                // 親ボーンのワールド移動量ぶんずれる。
                foreach (int vi in kv.Value)
                    if (vi >= 0 && vi < mc.MeshObject.VertexCount)
                    { sum += mc.LocalToWorld(vi, mc.MeshObject.Vertices[vi].Position); count++; }
            }
            _axisGizmo.Center = count > 0 ? sum / count : Vector3.zero;
        }

        private void BeginMove()
        {
            _dragWorldTotal = Vector3.zero;
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

            _dragWorldTotal += worldDelta;

            // 診断: 移動量はあるのに transform が 0 件 → 画面に何も反映されない。
            PLDiag.PickRec("MTH.ApplyDelta", _meshTransforms.Count,
                x: worldDelta.x, y: worldDelta.y, z: worldDelta.z);
            if (_meshTransforms.Count == 0)
                PLDiag.PickDump("apply-delta-empty");

            var model = _project?.CurrentModel;
            foreach (var kv in _meshTransforms)
            {
                var mc = model?.GetMeshContext(kv.Key);
                // IVertexTransform.Apply は Vertices[].Position（ローカル座標）に直接加算する。
                // ワールドデルタをそのまま渡すと、WorldMatrix に回転／スケールがある場合に
                // ギズモの指す向きと実際の移動方向がずれる。メッシュごとにローカル化する。
                Vector3 localDelta = mc != null
                    ? mc.WorldMatrixInverse.MultiplyVector(worldDelta)
                    : worldDelta;
                kv.Value.Apply(localDelta);
                if (mc != null) OnSyncMeshPositions?.Invoke(mc);
            }
            OnRepaint?.Invoke();
        }

        /// <summary>
        /// ドラッグ移動を確定する。
        ///
        /// 【なぜ EndMove を直接呼ばないか】
        ///   1 ストローク = 1 コマンドにするため。ドラッグ中の適用はプレビュー
        ///   （Undo に積まない）として扱い、確定時に開始位置へ戻してから
        ///   MoveSelectedVerticesCommand を送る。実際の移動と Undo 記録は
        ///   ディスパッチャ経由で ExecuteFromCommand → ApplyNumericMove が行う。
        ///
        /// 【戻す方法】
        ///   IVertexTransform は開始スナップショットからの絶対計算なので、
        ///   SetTotalDelta(0) で開始位置に戻る。復元用の経路を別に作らない。
        ///
        /// 【送れないとき】
        ///   SendCommand が未結線なら、プレビューを消して終わりでは移動が失われる。
        ///   その場合は従来どおり EndMove で確定させる。
        /// </summary>
        private void CommitDragMove()
        {
            Vector3 total = _dragWorldTotal;

            if (SendCommand == null) { EndMove(); return; }

            if (total == Vector3.zero)
            {
                // 実質動いていない。プレビューを戻して Undo も積まない。
                SetTotalDeltaAll(Vector3.zero);
                _meshTransforms.Clear();
                return;
            }

            var model = _project?.CurrentModel;
            if (model == null) { EndMove(); return; }

            var targets = new List<int>(_meshTransforms.Keys);
            if (targets.Count == 0) { EndMove(); return; }

            // プレビューを開始位置へ戻す。
            SetTotalDeltaAll(Vector3.zero);
            _meshTransforms.Clear();

            SendCommand(new Poly_Ling.Data.MoveSelectedVerticesCommand(
                _project?.CurrentModelIndex ?? 0,
                targets.ToArray(),
                total,
                Poly_Ling.Data.MoveSelectedVerticesCommand.CoordSpace.World,
                recalcNormals:      false,
                useMagnet:          UseMagnet,
                magnetRadius:       MagnetRadius,
                magnetFalloff:      MagnetFalloff,
                magnetDistanceMode: MagnetDistanceMode));
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
    }
}
