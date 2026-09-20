// PlayerCommandDispatcher.Query.cs
// コマンドディスパッチャ：照会（モデルを変えない）・生データの取得／送信と、照会結果の組み立て。
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Core;
using Poly_Ling.Data;
using Poly_Ling.Commands;
using Poly_Ling.UndoSystem;
using Poly_Ling.Selection;
using Poly_Ling.Tools;
using Poly_Ling.Tools.ObjectPose;
using Poly_Ling.Ops;
using Poly_Ling.UI;
using Poly_Ling.Diagnostics;
using Poly_Ling.Serialization;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// DispatchCore の分担：照会（モデルを変えない）・選択を結果辞書へ写す・生データの取得／送信。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchQuery(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── ツールの公開層（操作経路統一計画.md B・C・D）
                case QueryToolStateCommand qts:
                {
                    MarkNotRecorded();
                    if (string.IsNullOrEmpty(qts.ToolId))
                    {
                        var ids = new List<string>(ListTools?.Invoke() ?? Array.Empty<string>());
                        ReportData(CommandDataJson.New().Texts("tools", ids).Build());
                        return true;
                    }
                    var th = ResolveTool?.Invoke(qts.ToolId);
                    if (th == null) { Fail($"ツールがありません: {qts.ToolId}"); return true; }
                    var ps = PLToolSurface.Params(th);
                    var ss = PLToolSurface.States(th);
                    var acts = PLToolSurface.Actions(th);
                    ReportData(CommandDataJson.New()
                        .Texts("paramNames",  ps.ConvertAll(e => e.Name))
                        .Texts("paramTypes",  ps.ConvertAll(e => e.Type))
                        .Texts("paramValues", ps.ConvertAll(e => e.Value))
                        .Texts("stateNames",  ss.ConvertAll(e => e.Name))
                        .Texts("stateValues", ss.ConvertAll(e => e.Value))
                        .Texts("actions",     acts.ConvertAll(e => e.Name))
                        .Build());
                    return true;
                }

                case SetToolParamCommand stp:
                {
                    MarkNotRecorded();
                    var th = ResolveTool?.Invoke(stp.ToolId);
                    if (th == null) { Fail($"ツールがありません: {stp.ToolId}"); return true; }
                    if (!PLToolSurface.TrySet(th, stp.Name, stp.Value, out string actual, out string why))
                    { Fail(why); return true; }
                    // パネルへの通知はしない。スライダーのドラッグ中は 1 目盛りごとにここを通り、
                    // 通知するとパネルの Refresh が毎回走って、プレビュー中の回転中心などを
                    // 読み直す（読み取りが計算を伴うハンドラがある）。変化の通知は別途（push）扱う。
                    ReportData(CommandDataJson.New().Text("value", actual ?? "").Build());
                    return true;
                }

                case InvokeToolActionCommand ita:
                {
                    // 操作そのものは Undo に積まない（操作の中で送られるコマンドがそれぞれ積む）。
                    // パネルへの通知もしない。統計の再計算のようにパネルの再描画から呼ばれる操作があり、
                    // ここで通知すると再描画 → 操作 → 通知 … と回り続ける。
                    MarkNotRecorded();
                    var th = ResolveTool?.Invoke(ita.ToolId);
                    if (th == null) { Fail($"ツールがありません: {ita.ToolId}"); return true; }
                    if (!PLToolSurface.TryInvoke(th, ita.Action, ita.ArgKeys, ita.ArgValues, out string why)) { Fail(why); return true; }
                    return true;
                }

                // ── 担当者判定の照会（実行しない）
                //   RemoteOwnership.TryAuthorize をそのまま呼ぶ。照会対象は組み立てるだけで
                //   Dispatch しないので、モデルも Undo も変わらない。
                case QueryOwnershipVerdictCommand qov:
                {
                    MarkNotRecorded();
                    if (project == null) { Fail("no project"); return true; }

                    int n = Math.Min(qov.TargetArgKeys?.Length ?? 0, qov.TargetArgValues?.Length ?? 0);
                    if ((qov.TargetArgKeys?.Length ?? 0) != (qov.TargetArgValues?.Length ?? 0))
                    { Fail("targetArgKeys と targetArgValues の長さが違います"); return true; }

                    var qovArgs = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (int i = 0; i < n; i++) qovArgs[qov.TargetArgKeys[i]] = qov.TargetArgValues[i];

                    var target = PanelCommandFactory.Create(
                        qov.TargetAction, qov.ModelIndex, qovArgs, out string qovErr);
                    if (target == null) { Fail(qovErr ?? "コマンドを組み立てられません"); return true; }

                    ulong[] qovIds = null;
                    if (qov.ObjectIds != null && qov.ObjectIds.Length > 0)
                    {
                        qovIds = new ulong[qov.ObjectIds.Length];
                        for (int i = 0; i < qovIds.Length; i++)
                            if (!ulong.TryParse(qov.ObjectIds[i], System.Globalization.NumberStyles.Integer,
                                    System.Globalization.CultureInfo.InvariantCulture, out qovIds[i]))
                            { Fail($"objectIds[{i}] を整数にできません: {qov.ObjectIds[i]}"); return true; }
                    }

                    var verdict = Poly_Ling.Remote.RemoteOwnership.TryAuthorize(
                        project, target,
                        new CommandActor(qov.RequesterName, qov.ActorKind, qovIds));

                    var scope = Poly_Ling.Remote.RemoteOwnership.WriteScopeOf(target);
                    var primaryTargets = new List<int>();
                    var checkedTargets = new List<int>();
                    bool resolved = false;
                    if (scope == PLWriteScope.Targets &&
                        Poly_Ling.Remote.RemoteOwnership.TryCollectWriteTargets(
                            project, target, out var prim, out var byModel))
                    {
                        resolved = true;
                        primaryTargets.AddRange(prim);
                        var mainModel = project.GetModel(qov.ModelIndex);
                        if (mainModel != null && byModel.TryGetValue(mainModel, out var mainList))
                            checkedTargets.AddRange(Poly_Ling.Remote.RemoteOwnership.WritesMirrorSide(target)
                                ? MirrorBranchOps.CollectMirrorCaptureIndices(mainModel, mainList)
                                : mainList);
                    }

                    ReportData(CommandDataJson.New()
                        .Flag ("allowed",         verdict.Allowed)
                        .Flag ("staleView",       verdict.StaleView)
                        .Text ("reason",          verdict.Reason ?? "")
                        .Text ("writeScope",      scope.ToString())
                        .Flag ("targetsResolved", resolved)
                        .Ints ("primaryTargets",  primaryTargets)
                        .Ints ("checkedTargets",  checkedTargets)
                        .Build());
                    return true;
                }
                // ── 照会（モデルを変えない）
                //
                // Undo に記録しない。RemoteOwnership の判定では素通し
                // （PLCommand.Writes = None）。ComputeWorldMatrices を呼ばない。
                // パネルへの通知もしない。表示は何も変わらないため。
                //
                // 受け口（PolyLingPlayerViewerCore の Execute*）を置いていないのは、
                // ModelContext を読んで DataStore へ書くだけで、外部ハンドラに
                // 頼るものが無いため。フックを 1 本増やしても素通しになる。
                //
                // 中身はこのファイル末尾の「照会の組み立て」節にまとめてある。
                // switch の中へ直接書くと、既に長いこのメソッドがさらに伸びる。
                case QueryModelStructureCommand c:
                {
                    var qModel = project.GetModel(c.ModelIndex);
                    if (qModel == null) { Fail($"no model at index {c.ModelIndex}"); return true; }

                    ReportData(BuildModelStructureData(project, qModel, c));
                    return true;
                }

                case QueryObjectGroupsCommand c:
                {
                    var qgModel = project.GetModel(c.ModelIndex);
                    if (qgModel == null) { Fail($"no model at index {c.ModelIndex}"); return true; }

                    ReportData(BuildObjectGroupsData(project, qgModel, c));
                    return true;
                }

                case QueryBoneCommand c:
                {
                    var qbModel = project.GetModel(c.ModelIndex);
                    if (qbModel == null) { Fail($"no model at index {c.ModelIndex}"); return true; }

                    int found = -1;
                    string matchedBy = "";

                    // 名前を先に見る。理由は QueryBoneCommand の注記。
                    if (c.Names != null)
                    {
                        foreach (string n in c.Names)
                        {
                            if (string.IsNullOrEmpty(n)) continue;
                            for (int i = 0; i < qbModel.MeshContextCount; i++)
                            {
                                var mc = qbModel.GetMeshContext(i);
                                if (mc == null || mc.Type != MeshType.Bone) continue;
                                // 比べるのは Name。EditorName は表示用の別名で、
                                // 空のことがある（PlayerSpringBoneTestSubPanel.cs:995 と同じ）。
                                if (!string.Equals(mc.Name, n, StringComparison.Ordinal)) continue;
                                found = i; matchedBy = "name"; break;
                            }
                            if (found >= 0) break;
                        }
                    }

                    if (found < 0 && !string.IsNullOrEmpty(c.HumanoidBone))
                    {
                        var mapping = qbModel.HumanoidMapping;
                        if (mapping != null && !mapping.IsEmpty)
                        {
                            int idx = mapping.Get(c.HumanoidBone);
                            if (idx >= 0 && idx < qbModel.MeshContextCount &&
                                qbModel.GetMeshContext(idx)?.Type == MeshType.Bone)
                            { found = idx; matchedBy = "humanoid"; }
                        }
                    }

                    if (found < 0)
                    {
                        ReportData(CommandDataJson.New()
                            .Flag("found",     false)
                            .Int ("boneIndex", -1)
                            .Text("boneName",  "")
                            .Text("matchedBy", "")
                            .Build());
                        return true;
                    }

                    var w = qbModel.GetMeshContext(found).WorldMatrix;
                    var wb = qbModel.GetMeshContext(found).BindWorldMatrix;
                    // 撮られている BindPose が示すワールド位置。bindPosition と食い違うときは
                    // ポーズ中に撮った（取り違えた）ことを意味する。
                    var bp = qbModel.GetMeshContext(found).BindPose.inverse;

                    ReportData(CommandDataJson.New()
                        .Flag("found",         true)
                        .Int ("boneIndex",     found)
                        .Text("boneName",      qbModel.GetMeshContext(found).Name ?? "")
                        .Text("matchedBy",     matchedBy)
                        .Nums("worldPosition", new float[] { w.m03, w.m13, w.m23 })
                        .Nums("bindPosition",  new float[] { wb.m03, wb.m13, wb.m23 })
                        .Nums("bindPosePosition", new float[] { bp.m03, bp.m13, bp.m23 })
                        .Build(),
                        new[] { found }, new[] { qbModel.GetMeshContext(found).ObjectId });
                    return true;
                }

                case QuerySkinWeightSummaryCommand c:
                {
                    if (!TryGetQueryTarget(project, c.ModelIndex, c.MasterIndex,
                                           out var qwModel, out var qwMc, out string qwReason))
                    { Fail(qwReason); return true; }

                    ReportData(BuildSkinWeightSummaryData(qwModel, qwMc, c),
                               new[] { c.MasterIndex }, new[] { qwMc.ObjectId });
                    return true;
                }

                case QueryDrawableStatsCommand c:
                {
                    if (!TryGetQueryTarget(project, c.ModelIndex, c.MasterIndex,
                                           out var qsModel, out var qsMc, out string qsReason))
                    { Fail(qsReason); return true; }

                    ReportData(BuildDrawableStatsData(qsModel, qsMc, c),
                               new[] { c.MasterIndex }, new[] { qsMc.ObjectId });
                    return true;
                }

                case QueryHolesCommand c:
                {
                    if (!TryGetQueryTarget(project, c.ModelIndex, c.MasterIndex,
                                           out var qhModel, out var qhMc, out string qhReason))
                    { Fail(qhReason); return true; }

                    ReportData(BuildHolesData(qhModel, qhMc, c),
                               new[] { c.MasterIndex }, new[] { qhMc.ObjectId });
                    return true;
                }

                case QueryLineGroupsCommand c:
                {
                    if (!TryGetQueryTarget(project, c.ModelIndex, c.MasterIndex,
                                           out var qlModel, out var qlMc, out string qlReason))
                    { Fail(qlReason); return true; }

                    var qlMo = qlMc.MeshObject;
                    var names   = new List<string>();
                    var counts  = new List<int>();
                    var closed  = new List<int>();
                    var parents = new List<int>();
                    var order   = new List<int>();

                    // 2 頂点の面の組（向きを問わない）
                    var linePairs = new HashSet<long>();
                    int lineFaces = 0;
                    if (qlMo != null)
                        foreach (var f in qlMo.Faces)
                        {
                            if (f?.VertexIndices == null || f.VertexIndices.Count != 2) continue;
                            lineFaces++;
                            linePairs.Add(Poly_Ling.Ops.LineGroupOps.PairKey(f.VertexIndices[0], f.VertexIndices[1]));
                        }

                    int missing = 0;
                    var hasHandles = new List<int>();
                    var hOffsets   = new List<float>();
                    var hCons      = new List<int>();
                    var lgCounts   = new List<int>();
                    if (qlMo?.LineGroups != null)
                        foreach (var g in qlMo.LineGroups)
                        {
                            if (g?.Order == null) continue;
                            names.Add(g.Name ?? "");
                            counts.Add(g.Order.Count);
                            closed.Add(g.Closed ? 1 : 0);
                            parents.Add(g.ParentVertex);
                            order.AddRange(g.Order);
                            hasHandles.Add(g.HasHandles ? 1 : 0);
                            lgCounts.Add(g.LengthGroups?.Count ?? 0);
                            if (g.HasHandles)
                                foreach (var h in g.PointHandles)
                                {
                                    hOffsets.Add(h.InOffset.x);  hOffsets.Add(h.InOffset.y);  hOffsets.Add(h.InOffset.z);
                                    hOffsets.Add(h.OutOffset.x); hOffsets.Add(h.OutOffset.y); hOffsets.Add(h.OutOffset.z);
                                    hCons.Add((int)h.InConstraint.Direction);  hCons.Add((int)h.InConstraint.Length);  hCons.Add(h.InConstraint.LengthGroupId);
                                    hCons.Add((int)h.OutConstraint.Direction); hCons.Add((int)h.OutConstraint.Length); hCons.Add(h.OutConstraint.LengthGroupId);
                                }

                            int n = g.Order.Count;
                            int segs = g.Closed ? n : n - 1;
                            for (int k = 0; k < segs; k++)
                                if (!linePairs.Contains(Poly_Ling.Ops.LineGroupOps.PairKey(g.Order[k], g.Order[(k + 1) % n])))
                                    missing++;
                        }

                    ReportData(CommandDataJson.New()
                        .Int  ("masterIndex",  c.MasterIndex)
                        .Int  ("groups",       names.Count)
                        .Texts("names",        names)
                        .Ints ("counts",       counts)
                        .Ints ("closed",       closed)
                        .Ints ("parents",      parents)
                        .Ints ("order",        order)
                        .Int  ("lineFaces",    lineFaces)
                        .Int  ("missingFaces", missing)
                        .Ints ("hasHandles",   hasHandles)
                        .Nums ("handleOffsets", hOffsets)
                        .Ints ("handleConstraints", hCons)
                        .Ints ("lengthGroupCounts", lgCounts)
                        .Build(),
                        new[] { c.MasterIndex }, new[] { qlMc.ObjectId });
                    return true;
                }

                case AcquireBeltStripsCommand c:
                {
                    if (!TryGetQueryTarget(project, c.ModelIndex, c.MasterIndex,
                                           out var abModel, out var abMc, out string abReason))
                    { Fail(abReason); return true; }

                    // パネルが C# の中で呼んでいる 2 つを、そのままここで呼ぶ。
                    // 取り込み（BeltAcquire）と平坦化（SplitBelts）を別の場所へ
                    // 書き写すと、生成コマンドが受ける形と必ず食い違う。
                    var acquired = Poly_Ling.PrimitiveMesh.BeltAcquire.Acquire(
                        abMc, c.Method, c.CrossRows, c.SetName);

                    if (!acquired.Ok || acquired.Belts == null || acquired.Belts.Count == 0)
                    {
                        ReportData(CommandDataJson.New()
                            .Flag("ok",      false)
                            .Text("message", acquired.Message ?? "")
                            .Int ("belts",   0)
                            .Int ("points",  0)
                            .Build(),
                            new[] { abMc == null ? -1 : abModel.MeshContextList.IndexOf(abMc) },
                            new[] { abMc?.ObjectId ?? 0UL });
                        return true;
                    }

                    CreateBeltPrimitiveCommand.SplitBelts(
                        acquired.Belts.ToArray(),
                        out var bLeft, out var bRight, out var bStarts,
                        out var bClosed, out var bFlip, out var bHeight);

                    var rungs = new List<int>();
                    for (int i = 0; i < bStarts.Length; i++)
                    {
                        int end = (i + 1 < bStarts.Length) ? bStarts[i + 1] : (bLeft.Length / 3);
                        rungs.Add(end - bStarts[i]);
                    }

                    ReportData(CommandDataJson.New()
                        .Flag ("ok",              true)
                        .Text ("message",         acquired.Message ?? "")
                        .Int  ("belts",           bStarts.Length)
                        .Int  ("points",          bLeft.Length / 3)
                        .Nums ("beltLeftPoints",  bLeft)
                        .Nums ("beltRightPoints", bRight)
                        .Ints ("beltStarts",      bStarts)
                        .Ints ("beltClosed",      BoolsToInts(bClosed))
                        .Ints ("beltFlipWinding", BoolsToInts(bFlip))
                        .Nums ("beltHeightScale", bHeight)
                        .Ints ("rungCounts",      rungs)
                        .Build(),
                        new[] { abModel.MeshContextList.IndexOf(abMc) },
                        new[] { abMc.ObjectId });
                    return true;
                }

                case QuerySeedElementCommand c:
                {                    if (!TryGetQueryTarget(project, c.ModelIndex, c.MasterIndex,
                                           out var qeModel, out var qeMc, out string qeReason))
                    { Fail(qeReason); return true; }

                    ReportData(BuildSeedElementData(qeModel, qeMc, c),
                               new[] { c.MasterIndex }, new[] { qeMc.ObjectId });
                    return true;
                }

                case QueryBoneSkinCommand c:
                {
                    var qbModel = project.GetModel(c.ModelIndex);
                    if (qbModel == null) { Fail($"no model at index {c.ModelIndex}"); return true; }

                    ReportData(BuildBoneSkinData(qbModel, c));
                    return true;
                }

                // ── 選択を結果辞書へ写す
                //
                // 行き先が MeshContext.PartsSelectionSetList ではなく
                // ModelContext.DataStore である点が SavePartsSet と違う。
                // 対象を項目に控えるので、getRawData / setRawData の setName から引ける。
                case SaveSelectionToDataStoreCommand c:
                {
                    var ssModel = project.GetModel(c.ModelIndex);
                    if (ssModel == null) { Fail($"no model at index {c.ModelIndex}"); return true; }

                    var ssMc = c.MasterIndex >= 0
                        ? ssModel.GetMeshContext(c.MasterIndex)
                        : ssModel.ActiveMeshContext;
                    if (ssMc == null) { Fail("編集対象メッシュがありません"); return true; }

                    int ssIndex = ssModel.IndexOf(ssMc);
                    if (ssIndex < 0) { Fail("対象がモデルに属していません"); return true; }

                    PartsSelectionSet ssSet;
                    if (c.PartsSetIndex >= 0)
                    {
                        var ssList = ssMc.PartsSelectionSetList;
                        if (ssList == null || c.PartsSetIndex >= ssList.Count)
                        { Fail($"セット番号 {c.PartsSetIndex} が範囲外です"); return true; }

                        var ssSrc = ssList[c.PartsSetIndex];
                        if (ssSrc == null) { Fail($"セット番号 {c.PartsSetIndex} が空です"); return true; }

                        // 辞書へ入れたものが後から書き換わらないよう写しを作る。
                        ssSet = PartsSelectionSet.FromCurrentSelection(
                            ssSrc.Name, ssSrc.Vertices, ssSrc.Edges, ssSrc.Faces, ssSrc.Lines, ssSrc.Mode);
                    }
                    else
                    {
                        var ssSel = ssMc.Selection;
                        if (ssSel == null || !ssSel.HasAnySelection) { Fail("選択がありません"); return true; }

                        // 作り方は SavePartsSetCommand と同じ経路にそろえる。
                        var ssSnap = ssSel.CreateSnapshot();
                        ssSet = PartsSelectionSet.FromCurrentSelection(
                            "Selection", ssSnap.Vertices, ssSnap.Edges, ssSnap.Faces, ssSnap.Lines, ssSnap.Mode);
                    }

                    // 索引がずれたときに引き直せるよう、作った時点で識別子を控える。
                    if (ssMc.MeshObject != null) ssSet.CaptureIds(ssMc.MeshObject);

                    var ssStore = ssModel.DataStore;
                    string ssName = string.IsNullOrEmpty(c.ResultName)
                        ? ssStore.GenerateUniqueName("selection")
                        : c.ResultName;
                    ssSet.Name = ssName;

                    var ssEntry = ssStore.Put(PLDataEntry.FromIndexSet(
                        ssName, ssSet,
                        masterIndex: ssIndex, objectId: ssMc.ObjectId,
                        source: PanelCommandFactory.ActionOf(typeof(SaveSelectionToDataStoreCommand))));

                    ReportData(CommandDataJson.New()
                        .Entry("entry",     ssEntry)
                        .Int("masterIndex", ssIndex)
                        .Int("vertices",    ssSet.Vertices.Count)
                        .Int("edges",       ssSet.Edges.Count)
                        .Int("faces",       ssSet.Faces.Count)
                        .Int("lines",       ssSet.Lines.Count)
                        .Build(),
                        new[] { ssIndex }, new[] { ssMc.ObjectId });
                    return true;
                }

                // ── 生データの取得・送信
                //
                // 量のあるものを回線に乗せる唯一の経路。取得はモデルを変えない。
                // 送信は位相を変えず、数が合わなければ書く前に拒否する。
                case GetRawDataCommand c:
                {
                    var grModel = project.GetModel(c.ModelIndex);
                    if (grModel == null) { Fail($"no model at index {c.ModelIndex}"); return true; }

                    if (!RawDataOps.TryResolve(grModel, c.Scope, c.MasterIndex, c.SetName,
                                               out var grTargets, out var grVerts, out var grFaces,
                                               out string grReason))
                    { Fail(grReason); return true; }

                    ReportData(RawDataOps.BuildGet(grModel, c, grTargets, grVerts, grFaces),
                               grTargets.ToArray());
                    return true;
                }

                case SetRawDataCommand c:
                {
                    var srModel = project.GetModel(c.ModelIndex);
                    if (srModel == null) { Fail($"no model at index {c.ModelIndex}"); return true; }

                    if (!RawDataOps.TryResolve(srModel, c.Scope, c.MasterIndex, c.SetName,
                                               out var srTargets, out var srVerts, out var srFaces,
                                               out string srReason))
                    { Fail(srReason); return true; }

                    // 位相は変えないが、頂点の中身は総取り替えになる。
                    // 属性だけの記録用スタックが無いので、位相変更と同じ
                    // MeshListStack のスナップショットで残す。
                    MultiMeshTopologySnapshot srBefore = null;
                    if (_undoController != null)
                    {
                        srBefore = new MultiMeshTopologySnapshot();
                        foreach (int idx in srTargets) srBefore.CaptureMesh(srModel, idx);
                    }

                    if (!RawDataOps.TryApplySet(srModel, c, srTargets, srVerts, srFaces,
                                                out string srData, out string srFailure))
                    { Fail(srFailure); return true; }

                    if (_undoController != null)
                    {
                        var srAfter = new MultiMeshTopologySnapshot();
                        foreach (int idx in srTargets) srAfter.CaptureMesh(srModel, idx);

                        const string srDesc = "Set Raw Data";
                        var srRecord = new MultiMeshTopologySnapshotRecord(srBefore, srAfter, srDesc);
                        PLDiag.UndoRecord("MeshList", srDesc, srRecord);
                        _undoController.SetModelContext(srModel);
                        _undoController.MeshListStack.Record(srRecord, srDesc);
                    }

                    srModel.IsDirty = true;
                    _viewportManager.EnterTopologyChanged(project);

                    ReportData(srData, srTargets.ToArray());
                    return true;
                }

                // ── モデル選択
                case SwitchModelCommand c:
                {
                    // Undo 記録のため切替前の CurrentModelIndex を保存。
                    int __oldIdx = project.CurrentModelIndex;
                    project.SelectModel(c.TargetModelIndex);
                    int __newIdx = project.CurrentModelIndex;
                    PLDiag.Cmd($"SwitchModel {__oldIdx} -> {__newIdx} " +
                               $"current=\"{project.CurrentModel?.Name ?? "<null>"}\"");

                    var switchedModel = project.CurrentModel;
                    if (switchedModel != null)
                    {
                        // Phase 2a-2g-1: ClearScene + RebuildAdapter + SetSelectionState +
                        // UpdateSelectedDrawableMesh + NotifyCameraChanged を集約。
                        _viewportManager.EnterSceneReset(project, clearScene: true);
                        _viewportManager.EnterCameraChanged(
                            _viewportManager.PerspectiveViewport,
                            CameraChangePhase.Committed);
                    }

                    // 問題 A/B: モデル切替を Undo 記録し、UndoController の内部 Context を
                    // 新しい ActiveProject / CurrentModel に同期する。
                    if (_undoController != null)
                    {
                        _undoController.SetProjectContext(project);
                        _undoController.SetModelContext(project.CurrentModel);
                        _undoController.RecordModelSwitch(__oldIdx, __newIdx);
                    }

                    _notifyPanels(ChangeKind.ModelSwitch);
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // 照会の組み立て
        // ================================================================
        //
        // 【置き場所】
        //   DispatchCore の switch から呼ぶ。switch の中へ直接書くと
        //   既に長い DispatchCore がさらに伸びるため、ここへ寄せてある。
        //
        // 【守ること】
        //   ・モデルを書き換えない。書くのは ModelContext.DataStore だけ
        //   ・Undo を記録しない。パネルへ通知しない
        //   ・ComputeWorldMatrices を呼ばない。MeshContext.WorldMatrix を読むだけ
        //   ・量のあるもの（番号列・座標列）は戻り値に載せず、DataStore へ書く
        //
        // 【安定 ID を文字列で持つ理由】
        //   ObjectId は DateTime.UtcNow.Ticks から採番する（ObjectIdAllocator.cs:32）ので
        //   10^17 台になる。double の整数表現の上限 2^53 を超えるため、
        //   結果辞書（PLDataValue の数値は double）には文字列で入れる。
        // ================================================================

        /// <summary>
        /// 書き出し先の実経路と大きさを数えて戻り値の JSON にする。
        ///
        /// 【なぜもう一度関門を通すか】
        ///   受け口が使った実経路はディスパッチャへ返ってこない。
        ///   PLSandbox.TryResolveWrite は副作用の無い純粋な解決なので、
        ///   同じ引数でもう一度通せば同じ答えになる。
        ///
        /// 【フォルダを書くコマンドには使えない】
        ///   関門は拡張子を要求する。CSV プロジェクトのように「.csv を指定すると
        ///   同じディレクトリ直下にモデルフォルダができる」形でも、
        ///   指定される経路はファイル。フォルダを数えると無関係なファイルまで拾う。
        ///
        /// 【bytes を文字列で持つ理由】
        ///   FileInfo.Length は long。CommandDataBuilder の数値は int と double で、
        ///   double では大きなファイルで丸めが起きる。10 進の文字列で返す。
        /// </summary>
        private static string BuildWriteResultData(string requestedPath)
        {
            bool resolved = PLSandbox.TryResolveWrite(requestedPath, out string full, out _);

            var b = CommandDataJson.New()
                .Text("requestedPath", requestedPath ?? "")
                .Flag("resolved",      resolved);

            if (!resolved)
                return b.Flag("exists", false).Int("files", 0).Text("bytes", "0").Build();

            b.Text("path", full);

            long bytes  = 0;
            int  files  = 0;
            bool exists = false;

            // 書けたかどうかの確認だけなので、読めない事情は結果へ返して握る。
            try
            {
                if (System.IO.File.Exists(full))
                {
                    exists = true;
                    files  = 1;
                    bytes  = new System.IO.FileInfo(full).Length;
                }
            }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }

            return b
                .Flag("exists", exists)
                .Int("files",   files)
                .Text("bytes",  bytes.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Build();
        }

        /// <summary>
        /// 位相変更の後で、対象の描画オブジェクトの規模を数えて戻り値の JSON にする。
        /// 穴の数は BridgeAutoPairOps.CollectHoles を呼んで数える。
        /// 同じ masterIndex が重複して渡っても二重に数えない。
        /// </summary>
        private static string BuildTopologyCountsData(ModelContext model, params int[] masterIndices)
        {
            int objects = 0, vertices = 0, faces = 0, holes = 0;

            if (model != null && masterIndices != null)
            {
                var seen = new HashSet<int>();
                foreach (int mi in masterIndices)
                {
                    if (mi < 0 || !seen.Add(mi)) continue;

                    var mc   = model.GetMeshContext(mi);
                    var mesh = mc?.MeshObject;
                    if (mesh == null) continue;

                    objects++;
                    vertices += mesh.Vertices.Count;
                    faces    += mesh.Faces.Count;
                    holes    += BridgeAutoPairOps.CollectHoles(mesh, mc.WorldMatrix).Count;
                }
            }

            return CommandDataJson.New()
                .Int("objects",  objects)
                .Int("vertices", vertices)
                .Int("faces",    faces)
                .Int("holes",    holes)
                .Build();
        }

        /// <summary>
        /// モデル全体の要素選択の件数を数え、戻り値の JSON にする。
        /// 選択系コマンドが実行後に呼ぶ。選択そのものは書き換えない。
        /// </summary>
        private static string BuildSelectionCountsData(ModelContext model)
        {
            int v = 0, e = 0, f = 0, l = 0;
            if (model != null)
            {
                foreach (var ent in model.DrawableMeshes)
                {
                    var sel = ent.Context?.Selection;
                    if (sel == null) continue;
                    v += sel.Vertices.Count;
                    e += sel.Edges.Count;
                    f += sel.Faces.Count;
                    l += sel.Lines.Count;
                }
            }
            return CommandDataJson.SelectionCounts(v, e, f, l);
        }

        /// <summary>不変文化圏の 10 進表記。安定 ID を文字列で持つときに使う。</summary>
        private static string IdText(ulong id)
            => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// 照会の対象を引く。モデルと描画オブジェクトの両方が取れたときだけ true。
        ///
        /// masterIndex が負なら、編集対象（ActiveMeshContext）を使う。
        /// 引数の説明に「省くと現在の編集対象」と書いたコマンドがここを通るため
        /// （acquireBeltStrips / resolveTJunctions / queryBoundaryEdges /
        ///   queryFacesInBox / queryNearestBoundaryVertex）。
        /// 規則は saveSelectionToDataStore と同じ。
        /// 呼び出し側は、報告する対象の索引を c.MasterIndex ではなく
        /// model.MeshContextList.IndexOf(mc) から取ること（負のまま報告しないため）。
        /// </summary>
        private static bool TryGetQueryTarget(
            ProjectContext project, int modelIndex, int masterIndex,
            out ModelContext model, out MeshContext mc, out string reason)
        {
            model  = null;
            mc     = null;
            reason = null;

            model = project?.GetModel(modelIndex);
            if (model == null) { reason = $"no model at index {modelIndex}"; return false; }

            if (masterIndex < 0)
            {
                mc = model.ActiveMeshContext;
                if (mc == null) { reason = "masterIndex を省きましたが、編集対象がありません"; return false; }
            }
            else
            {
                mc = model.GetMeshContext(masterIndex);
                if (mc == null) { reason = $"no object at masterIndex {masterIndex}"; return false; }
            }

            if (mc.MeshObject == null)
            {
                reason = $"masterIndex {masterIndex} has no mesh";
                mc = null;
                return false;
            }
            return true;
        }

        /// <summary>結果辞書へ書き込む名前を決める。省略時は種別ごとの自動名。</summary>
        private static string ResolveResultName(PLDataStore store, string requested, string fallback)
            => string.IsNullOrEmpty(requested) ? store.GenerateUniqueName(fallback) : requested;

        /// <summary>
        /// 真偽の列を 0 / 1 の整数列にする。
        /// PLResultKind に真偽の配列が無いため。受け側の bool[] は
        /// "1,0" でも "true,false" でも TryParse が受ける。
        /// </summary>
        private static List<int> BoolsToInts(bool[] values)
        {
            var list = new List<int>(values?.Length ?? 0);
            if (values == null) return list;
            foreach (bool b in values) list.Add(b ? 1 : 0);
            return list;
        }
    }
}
