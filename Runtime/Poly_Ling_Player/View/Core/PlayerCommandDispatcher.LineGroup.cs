// PlayerCommandDispatcher.LineGroup.cs
// コマンドディスパッチャ：線分群（PanelCommand.LineGroup.cs）。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 実処理は LineGroupEditOps。頂点・2 頂点の面・線分群を一緒に書き換えるので、
// Undo は MeshObject のスナップショット（前後）で取る（SetVertexPositions と同じ形）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>DispatchCore の分担：線分群。該当するコマンドなら処理して true を返す。</summary>
        private bool DispatchLineGroup(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case CreateLineGroupCommand c:
                    RunLineGroupEdit(project, model, c.MasterIndex, "線分群の作成", mo =>
                    {
                        if (!TryParsePoints(c.Points, out var pts, out string r)) return (false, r, -1);
                        if (!TryParseHandles(c.HandleOffsets, pts.Count, out var hs, out r)) return (false, r, -1);
                        if (hs != null && c.HandleConstraints != null && c.HandleConstraints.Length > 0)
                        {
                            if (c.HandleConstraints.Length != pts.Count * 6 || c.HandleRatios == null || c.HandleRatios.Length != pts.Count * 2)
                                return (false, $"handleConstraints は点の数 × 6、handleRatios は点の数 × 2 にしてください", -1);
                            for (int i = 0; i < pts.Count; i++)
                            {
                                hs[i].InConstraint = new HandleConstraint
                                {
                                    Direction = (HandleDirection)c.HandleConstraints[i * 6],
                                    Length    = (HandleLength)c.HandleConstraints[i * 6 + 1],
                                    LengthGroupId = c.HandleConstraints[i * 6 + 2],
                                    Ratio     = c.HandleRatios[i * 2],
                                };
                                hs[i].OutConstraint = new HandleConstraint
                                {
                                    Direction = (HandleDirection)c.HandleConstraints[i * 6 + 3],
                                    Length    = (HandleLength)c.HandleConstraints[i * 6 + 4],
                                    LengthGroupId = c.HandleConstraints[i * 6 + 5],
                                    Ratio     = c.HandleRatios[i * 2 + 1],
                                };
                            }
                        }
                        int gi = LineGroupEditOps.Create(mo, pts, c.Closed, c.GroupName, hs, out r, c.StartVertexIndex);
                        return (gi >= 0, r, gi);
                    });
                    return true;

                case EdgesToLinesCommand c:
                {
                    var etlGroups = new List<int>();
                    var etlLines  = new List<int>();
                    RunLineGroupEdit(project, model, c.MasterIndex, "辺の線分化", mo =>
                    {
                        IReadOnlyList<int> pairs = c.EdgeVertexPairs;
                        if (pairs == null || pairs.Count == 0)
                        {
                            var sel = model.GetMeshContext(c.MasterIndex)?.Selection;
                            if (sel == null || sel.Edges.Count == 0) return (false, "選択辺がありません", -1);
                            var flat = new List<int>(sel.Edges.Count * 2);
                            foreach (var e in sel.Edges) { flat.Add(e.V1); flat.Add(e.V2); }
                            pairs = flat;
                        }
                        bool ok = LineGroupEditOps.CreateFromEdges(mo, pairs, etlGroups, etlLines, out string r);
                        return (ok, r, -1);
                    },
                    onSuccess: mc =>
                    {
                        // 選択を作った線分だけにする（通知はトポロジ反映の後で出す）。
                        var sel = mc.Selection;
                        if (sel == null) return;
                        sel.Vertices.Clear(); sel.Edges.Clear(); sel.Faces.Clear(); sel.Lines.Clear();
                        foreach (int fi in etlLines) sel.Lines.Add(fi);
                    },
                    extraData: d => d.Int("lineCount", etlLines.Count).Ints("groupIndices", etlGroups));
                    return true;
                }

                case SetLineGroupPointsCommand c:
                    RunLineGroupEdit(project, model, c.MasterIndex, "線分群の点の差し替え", mo =>
                    {
                        if (!TryParsePoints(c.Points, out var pts, out string r)) return (false, r, -1);
                        if (!TryParseHandles(c.HandleOffsets, pts.Count, out var hs, out r)) return (false, r, -1);
                        bool ok = LineGroupEditOps.SetPoints(mo, c.GroupIndex, pts, c.Closed, hs, out r);
                        return (ok, r, c.GroupIndex);
                    });
                    return true;

                case DeleteLineGroupCommand c:
                    RunLineGroupEdit(project, model, c.MasterIndex, "線分群の削除", mo =>
                    {
                        bool ok = LineGroupEditOps.Delete(mo, c.GroupIndex, c.DeleteVertices, out string r);
                        return (ok, r, -1);
                    });
                    return true;

                case RenameLineGroupCommand c:
                    RunLineGroupEdit(project, model, c.MasterIndex, "線分群の名前の変更", mo =>
                    {
                        if (mo.LineGroups == null || c.GroupIndex < 0 || c.GroupIndex >= mo.LineGroups.Count)
                            return (false, $"線分群の番号が範囲外です: {c.GroupIndex}", -1);
                        if (string.IsNullOrEmpty(c.NewName)) return (false, "名前が空です", -1);
                        mo.LineGroups[c.GroupIndex].Name = c.NewName;
                        return (true, null, c.GroupIndex);
                    });
                    return true;

                case SetLineHandleConstraintCommand c:
                    RunLineGroupEdit(project, model, c.MasterIndex, "ハンドル拘束の変更", mo =>
                    {
                        var hc = new HandleConstraint
                        {
                            Direction = c.Direction, Length = c.Length,
                            Ratio = c.Ratio, LengthGroupId = c.LengthGroupId,
                        };
                        bool ok = LineGroupEditOps.SetConstraint(mo, c.GroupIndex, c.PointIndex, c.IsOut, hc, out string r);
                        return (ok, r, c.GroupIndex);
                    });
                    return true;

                case SetLineLengthGroupCommand c:
                    RunLineGroupEdit(project, model, c.MasterIndex, "長さの組の変更", mo =>
                    {
                        bool ok = LineGroupEditOps.SetLengthGroup(mo, c.GroupIndex, c.LengthGroupId, c.Value, out string r);
                        return (ok, r, c.GroupIndex);
                    });
                    return true;

                case BakeLineGroupCurveCommand c:
                    RunLineGroupEdit(project, model, c.MasterIndex, "曲線の焼き込み", mo =>
                    {
                        bool ok = LineGroupEditOps.BakeCurve(mo, c.GroupIndex, c.SegmentsPerSpan, out string r);
                        return (ok, r, c.GroupIndex);
                    });
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 線分群の編集を Undo（前後スナップショット）付きで行う。
        /// edit は (成功か, 失敗理由, 結果として返す群の番号 or -1) を返す。
        /// </summary>
        private void RunLineGroupEdit(
            ProjectContext project, ModelContext model, int masterIndex, string label,
            Func<MeshObject, (bool Ok, string Reason, int GroupIndex)> edit,
            Action<MeshContext> onSuccess = null,
            Action<CommandDataBuilder> extraData = null)
        {
            if (model == null) { Fail("no current model"); return; }
            var mc = model.GetMeshContext(masterIndex);
            var mo = mc?.MeshObject;
            if (mo == null) { Fail($"no object at masterIndex {masterIndex}"); return; }

            if (_undoController != null)
            {
                _undoController.SetMeshObject(mo, mc.UnityMesh);
                _undoController.MeshUndoContext.ParentModelContext = model;
            }
            var before = _undoController?.CaptureMeshObjectSnapshotOf(mc);

            var result = edit(mo);
            if (result.Ok) LineGroupEditOps.SyncChordVisibility(mo);
            if (!result.Ok)
            {
                // 途中まで書き換えた可能性があるので、前の状態へ戻す。
                before?.ApplyTo(mc, _undoController.MeshUndoContext, null);
                Fail(result.Reason ?? "線分群を編集できませんでした");
                return;
            }

            if (_undoController != null && before != null)
                _undoController.RecordTopologyChange(before, _undoController.CaptureMeshObjectSnapshotOf(mc), label);

            onSuccess?.Invoke(mc);

            model.IsDirty = true;
            _viewportManager.EnterTopologyChanged(project);
            _notifyPanels(ChangeKind.Attributes);
            if (onSuccess != null) mc.Selection?.NotifySelectionChanged();

            var data = CommandDataJson.New();
            if (result.GroupIndex >= 0) data.Int("groupIndex", result.GroupIndex);
            extraData?.Invoke(data);
            ReportData(data.Build(), new[] { masterIndex }, new[] { mc.ObjectId });
        }

        private static bool TryParsePoints(float[] flat, out List<Vector3> pts, out string reason)
        {
            pts = new List<Vector3>();
            reason = null;
            if (flat == null || flat.Length % 3 != 0) { reason = "points は 3 の倍数個にしてください"; return false; }
            for (int i = 0; i + 2 < flat.Length; i += 3) pts.Add(new Vector3(flat[i], flat[i + 1], flat[i + 2]));
            return true;
        }

        private static bool TryParseHandles(float[] flat, int pointCount, out List<LinePointHandle> hs, out string reason)
        {
            hs = null;
            reason = null;
            if (flat == null || flat.Length == 0) return true;
            if (flat.Length != pointCount * 6) { reason = $"handleOffsets は点の数 × 6（{pointCount * 6} 個）にしてください"; return false; }
            hs = new List<LinePointHandle>(pointCount);
            for (int i = 0; i < pointCount; i++)
            {
                int b = i * 6;
                hs.Add(new LinePointHandle
                {
                    InOffset  = new Vector3(flat[b],     flat[b + 1], flat[b + 2]),
                    OutOffset = new Vector3(flat[b + 3], flat[b + 4], flat[b + 5]),
                });
            }
            return true;
        }
    }
}
