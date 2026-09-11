// PlayerCommandDispatcher.SelectionSets.cs
// コマンドディスパッチャ：パーツ選択辞書・法線再計算の除外辞書・面の表示／非表示・メッシュ選択辞書。
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
        /// DispatchCore の分担：パーツ選択辞書・法線再計算の除外辞書・面の表示／非表示。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchSelectionSets(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case LoadPartsSetCommand c:
                    PartsSetApply(model, c.SetIndex, additive: false, subtract: false);
                    return true;

                case AddPartsSetCommand c:
                    PartsSetApply(model, c.SetIndex, additive: true, subtract: false);
                    return true;

                case SubtractPartsSetCommand c:
                    PartsSetApply(model, c.SetIndex, additive: false, subtract: true);
                    return true;

                case DeletePartsSetCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var delMc = model.ActiveMeshContext;
                    var delSets = delMc?.PartsSelectionSetList;
                    if (delSets == null || c.SetIndex < 0 || c.SetIndex >= delSets.Count) { Fail($"セット番号 {c.SetIndex} が範囲外です"); return true; }
                    delSets.RemoveAt(c.SetIndex);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case CapturePartsSetVertexIdsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var cvMc   = model.ActiveMeshContext;
                    var cvSets = cvMc?.PartsSelectionSetList;
                    if (cvSets == null || c.SetIndex < 0 || c.SetIndex >= cvSets.Count)
                    { Fail($"セット番号 {c.SetIndex} が範囲外です"); return true; }
                    if (cvMc.MeshObject == null) { Fail("編集対象メッシュがありません"); return true; }

                    int cvCount = cvSets[c.SetIndex].CaptureVertexIds(cvMc.MeshObject);
                    if (cvCount == 0) { Fail("控える頂点がありません"); return true; }

                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case ResolvePartsSetByVertexIdCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var rvMc   = model.ActiveMeshContext;
                    var rvSets = rvMc?.PartsSelectionSetList;
                    if (rvSets == null || c.SetIndex < 0 || c.SetIndex >= rvSets.Count)
                    { Fail($"セット番号 {c.SetIndex} が範囲外です"); return true; }
                    if (rvMc.MeshObject == null) { Fail("編集対象メッシュがありません"); return true; }

                    bool rvDone = rvSets[c.SetIndex].ResolveByVertexId(
                        rvMc.MeshObject, out int rvResolved, out int rvLost);

                    if (!rvDone)
                    { Fail("引き当てに使える頂点IDが控えられていません"); return true; }
                    if (rvResolved == 0)
                    { Fail($"控えた頂点IDが 1 件も見つかりません（見失い {rvLost} 件）"); return true; }

                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case RenamePartsSetCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var rnMc   = model.ActiveMeshContext;
                    var rnSets = rnMc?.PartsSelectionSetList;
                    if (rnSets == null || c.SetIndex < 0 || c.SetIndex >= rnSets.Count) { Fail($"セット番号 {c.SetIndex} が範囲外です"); return true; }
                    string rnName = c.NewName;
                    if (rnMc.FindSelectionSetByName(rnName) != null && rnName != rnSets[c.SetIndex].Name)
                        rnName = rnMc.GenerateUniqueSelectionSetName(rnName);
                    rnSets[c.SetIndex].Name = rnName;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── 法線再計算 除外辞書 ─────────────────────────────────────────
                case SaveNormalExcludeSetCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var nxMc = model.ActiveMeshContext;
                    var nxMo = nxMc?.MeshObject;
                    if (nxMo == null) { Fail("編集対象メッシュがありません"); return true; }
                    var nxSel = nxMc.Selection;
                    if (nxSel == null || !nxSel.HasAnySelection) { Fail("選択がありません"); return true; }
                    if (nxMo.NormalRecalcExcludeList == null)
                        nxMo.NormalRecalcExcludeList = new List<PartsSelectionSet>();
                    string nxName = GenerateUniqueNormalExcludeName(
                        nxMo, string.IsNullOrEmpty(c.SetName) ? "NormalExclude" : c.SetName);
                    var nxSnap = nxSel.CreateSnapshot();
                    var nxSet  = PartsSelectionSet.FromCurrentSelection(
                        nxName, nxSnap.Vertices, nxSnap.Edges, nxSnap.Faces, nxSnap.Lines, nxSnap.Mode);
                    nxMo.NormalRecalcExcludeList.Add(nxSet);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case LoadNormalExcludeSetCommand c:
                    NormalExcludeSetApply(model, c.SetIndex);
                    return true;

                case DeleteNormalExcludeSetCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var nxdList = model.ActiveMeshContext?.MeshObject?.NormalRecalcExcludeList;
                    if (nxdList == null || c.SetIndex < 0 || c.SetIndex >= nxdList.Count) { Fail($"セット番号 {c.SetIndex} が範囲外です"); return true; }
                    nxdList.RemoveAt(c.SetIndex);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case RenameNormalExcludeSetCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var nxrMo   = model.ActiveMeshContext?.MeshObject;
                    var nxrList = nxrMo?.NormalRecalcExcludeList;
                    if (nxrList == null || c.SetIndex < 0 || c.SetIndex >= nxrList.Count) { Fail($"セット番号 {c.SetIndex} が範囲外です"); return true; }
                    if (string.IsNullOrEmpty(c.NewName)) { Fail("NewName が空です"); return true; }
                    string nxrName = c.NewName;
                    if (nxrName != nxrList[c.SetIndex].Name)
                        nxrName = GenerateUniqueNormalExcludeName(nxrMo, nxrName);
                    nxrList[c.SetIndex].Name = nxrName;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case ExportPartsSetsCsvCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (string.IsNullOrEmpty(c.FolderPath)) { Fail("FolderPath が空です"); return true; }
                    // ファイルへ触る前に必ず関門を通す。ここを飛ばすと
                    // 作業フォルダの外へ書けてしまう。
                    if (!Poly_Ling.Core.PLSandbox.TryResolveFolder(
                            c.FolderPath, out string exFolder, out string exSbReason))
                    { Fail(exSbReason); return true; }
                    var exTargets = CollectSelectedMeshContexts(model);
                    if (exTargets.Count == 0) { Fail("書き出す対象がありません"); return true; }
                    PartsSetCsvHelper.ExportSetsToFolder(exTargets, exFolder);
                    return true;
                }

                case ImportPartsSetCsvCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (string.IsNullOrEmpty(c.FolderPath)) { Fail("FolderPath が空です"); return true; }
                    if (!Poly_Ling.Core.PLSandbox.TryResolveFolder(
                            c.FolderPath, out string imFolder, out string imSbReason))
                    { Fail(imSbReason); return true; }
                    var imTargets = c.ByObjectName ? null : CollectSelectedMeshContexts(model);
                    if (!c.ByObjectName && imTargets.Count == 0) { Fail("読み込む対象がありません"); return true; }
                    if (PartsSetCsvHelper.ImportSetsFromFolder(model, imFolder, c.ByObjectName, imTargets) > 0)
                        _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── 面の表示・非表示 ───────────────────────────────────────────
                case SetFaceHiddenCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var fhTargets = CollectSelectedMeshContexts(model);
                    if (fhTargets.Count == 0) { Fail("対象がありません"); return true; }

                    int fhTotal = 0;
                    var fhChanged = new List<MeshContext>();

                    foreach (var mc in fhTargets)
                    {
                        var mo = mc?.MeshObject;
                        if (mo == null) continue;

                        // Undo は MeshObject 丸ごとのスナップショットで戻す
                        // （面フラグは MeshObject.Clone が引き継ぐ）。
                        if (_undoController != null)
                        {
                            _undoController.SetMeshObject(mo, mc.UnityMesh);
                            _undoController.MeshUndoContext.ParentModelContext = model;
                        }
                        var fhBefore = _undoController?.CaptureMeshObjectSnapshot();

                        int changed = ApplyFaceHidden(mc, c.Operation);
                        if (changed <= 0) continue;

                        fhTotal += changed;
                        fhChanged.Add(mc);

                        if (_undoController != null && fhBefore != null)
                        {
                            var fhAfter = _undoController.CaptureMeshObjectSnapshot();
                            _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                                _undoController, fhBefore, fhAfter, $"Face Hide ({c.Operation})"));
                        }
                    }

                    if (fhTotal > 0)
                    {
                        // 面ポリゴンの取捨は Unity Mesh の三角形、
                        // 辺・頂点・ヒットテストは GPU バッファ側で決まる。
                        // 前者は三角形だけ張り直し、後者は EnterTopologyChanged で再構築する。
                        foreach (var mc in fhChanged)
                        {
                            if (mc.UnityMesh == null) continue;
                            mc.MeshObject.ApplyTrianglesToUnityMesh(mc.UnityMesh, model.MaterialCount);
                        }

                        _viewportManager.EnterTopologyChanged(project);
                        _notifyPanels(ChangeKind.Attributes);
                    }

                    Debug.Log($"[FaceHide] {c.Operation}: {fhTargets.Count} オブジェクト / {fhTotal} 面");
                    return true;
                }

                // ── 法線編集 ───────────────────────────────────────────────────
                case NormalEditCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var neTargets = CollectSelectedMeshContexts(model);
                    if (neTargets.Count == 0) { Fail("対象がありません"); return true; }

                    // RecalcByAngle / Break はスロット数が変わり得る。その場合は
                    // Unity Mesh を作り直す必要があるので描画更新の段を分ける。
                    bool slotCountMayChange =
                        c.Operation == NormalEditCommand.Op.RecalcByAngle ||
                        c.Operation == NormalEditCommand.Op.Break;

                    int neTotal = 0;
                    var neSynced = new List<MeshContext>();

                    foreach (var mc in neTargets)
                    {
                        var mo = mc?.MeshObject;
                        if (mo == null) continue;

                        // Undo は MeshObject 丸ごとのスナップショットで戻す。
                        // スロット（UV/法線）の増減も含めて復元する必要があるため。
                        if (_undoController != null)
                        {
                            _undoController.SetMeshObject(mo, mc.UnityMesh);
                            _undoController.MeshUndoContext.ParentModelContext = model;
                        }
                        var neBefore = _undoController?.CaptureMeshObjectSnapshot();

                        int changed = ApplyNormalEdit(mc, c);
                        if (changed <= 0) continue;

                        neTotal += changed;
                        neSynced.Add(mc);

                        // 手で編集した法線は、頂点移動時の自動再計算で消えてしまう
                        // （MeshUndoContext.ApplyVertexPositionsToMesh）。維持フラグを立てる。
                        mo.PreserveNormals = true;

                        if (_undoController != null && neBefore != null)
                        {
                            var neAfter = _undoController.CaptureMeshObjectSnapshot();
                            _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                                _undoController, neBefore, neAfter, $"Normal Edit ({c.Operation})"));
                        }
                    }

                    if (neTotal > 0)
                    {
                        // ミラー側の面は選択できないため、実体側の編集結果を写す。
                        // スロット数が変わる操作でも実体側と 1:1 に張り直される。
                        // 生成ミラー（MirrorGeometryDerived）のみが対象。
                        int neMirrored = MirrorBranchOps.RebakeDerivedMirrorNormals(
                            model.MeshContextList, model.MaterialCount);

                        // ミラー側の UnityMesh を作り直した場合は GPU も再構築が要る。
                        bool neRebuild = slotCountMayChange || neMirrored > 0;

                        // スロット数が変わらない操作でも、Unity Mesh の法線だけは
                        // 差し替える必要がある。差し替えられなければ作り直す。
                        if (!neRebuild)
                        {
                            foreach (var mc in neSynced)
                            {
                                if (mc.UnityMesh == null) { neRebuild = true; break; }
                                if (!mc.MeshObject.ApplyNormalsToUnityMesh(mc.UnityMesh))
                                {
                                    neRebuild = true;
                                    break;
                                }
                            }
                        }

                        if (neRebuild)
                        {
                            _viewportManager.EnterTopologyChanged(project);
                        }
                        else
                        {
                            foreach (var mc in neSynced)
                                _viewportManager.EnterVertexAttributesChanged(
                                    project, mc, weights: false, uvs: false);
                        }

                        _notifyPanels(ChangeKind.Attributes);
                    }

                    Debug.Log($"[NormalEdit] {c.Operation}: {neTargets.Count} オブジェクト / {neTotal} コーナー");
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// DispatchCore の分担：メッシュ選択辞書。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchMeshSelectionSets(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case ApplySelectionDictionaryCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var sdSets = model.MeshSelectionSets;
                    if (c.SetIndex < 0 || c.SetIndex >= sdSets.Count) { Fail($"セット番号 {c.SetIndex} が範囲外です"); return true; }

                    // Undo 用：適用前のメッシュ選択を記録
                    var sdOldSel = new System.Collections.Generic.List<int>(model.SelectedDrawableMeshIndices);
                    if (c.AddToExisting)
                        sdSets[c.SetIndex].AddTo(model);
                    else
                        sdSets[c.SetIndex].ApplyTo(model);
                    var sdNewSel = new System.Collections.Generic.List<int>(model.SelectedDrawableMeshIndices);
                    if (_undoController != null)
                    {
                        var sdRecord = new MeshSelectionChangeRecord(sdOldSel, sdNewSel);
                        {
                            string __dbgDesc = "メッシュ選択辞書適用";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, sdRecord);
                            _undoController.MeshListStack.Record(sdRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    // Phase 2a-2g-1: UpdateSelectedDrawableMesh を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Selection);
                    return true;
                }

                case DeleteSelectionDictionaryCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var dsdSets = model.MeshSelectionSets;
                    if (c.SetIndex < 0 || c.SetIndex >= dsdSets.Count) { Fail($"セット番号 {c.SetIndex} が範囲外です"); return true; }
                    dsdSets.RemoveAt(c.SetIndex);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case RenameSelectionDictionaryCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var rsdSets = model.MeshSelectionSets;
                    if (c.SetIndex < 0 || c.SetIndex >= rsdSets.Count) { Fail($"セット番号 {c.SetIndex} が範囲外です"); return true; }
                    string rsdName = c.NewName;
                    if (model.FindMeshSelectionSetByName(rsdName) != null && rsdName != rsdSets[c.SetIndex].Name)
                        rsdName = model.GenerateUniqueMeshSelectionSetName(rsdName);
                    rsdSets[c.SetIndex].Name = rsdName;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // パーツ選択辞書ヘルパー
        // ================================================================

        /// <summary>
        /// パーツ選択辞書を現在の選択へ適用する。
        /// </summary>
        /// <remarks>
        /// 【単一メッシュ前提 — 変更時の注意】
        ///
        /// 対象は model.ActiveMeshContext の Selection のみ。
        /// SelectionChangeRecord の復元先も ActiveMeshContext 固定なので整合する。
        ///
        /// 将来これを複数メッシュへ広げる場合、Undo 記録も
        /// MultiMeshSelectionChangeRecord へ移すこと。
        /// 記録側だけ複数メッシュ化すると Undo が先頭メッシュしか戻さなくなる。
        /// </remarks>
        private void PartsSetApply(ModelContext model, int setIndex, bool additive, bool subtract)
        {
            if (model == null) { Fail("no current model"); return; }
            var mc   = model.ActiveMeshContext;
            var sets = mc?.PartsSelectionSetList;
            if (sets == null || setIndex < 0 || setIndex >= sets.Count)
            { Fail($"セット番号 {setIndex} が範囲外です"); return; }
            var sel = mc.Selection;
            if (sel == null) { Fail("編集対象メッシュに選択状態がありません"); return; }

            // Undo 用：適用前スナップショット
            SelectionSnapshot oldSnap = sel.CreateSnapshot();

            var set = sets[setIndex];
            SelectionSnapshot newSnap;
            if (additive)
            {
                var snap = sel.CreateSnapshot();
                snap.Vertices.UnionWith(set.Vertices);
                snap.Edges.UnionWith(set.Edges);
                snap.Faces.UnionWith(set.Faces);
                snap.Lines.UnionWith(set.Lines);
                sel.RestoreFromSnapshot(snap);
                newSnap = snap;
            }
            else if (subtract)
            {
                var snap = sel.CreateSnapshot();
                snap.Vertices.ExceptWith(set.Vertices);
                snap.Edges.ExceptWith(set.Edges);
                snap.Faces.ExceptWith(set.Faces);
                snap.Lines.ExceptWith(set.Lines);
                sel.RestoreFromSnapshot(snap);
                newSnap = snap;
            }
            else
            {
                newSnap = new SelectionSnapshot
                {
                    Mode     = set.Mode,
                    Vertices = new HashSet<int>(set.Vertices),
                    Edges    = new HashSet<VertexPair>(set.Edges),
                    Faces    = new HashSet<int>(set.Faces),
                    Lines    = new HashSet<int>(set.Lines),
                };
                sel.RestoreFromSnapshot(newSnap);
            }

            // Undo 記録（VertexEditStack の SelectionChangeRecord）
            if (_undoController != null)
            {
                var record = new SelectionChangeRecord(oldSnap, newSnap);
                {
                    string __dbgDesc = "パーツ選択辞書 適用";
                    PLDiag.UndoRecord("VertexEdit", __dbgDesc, record);
                    _undoController.VertexEditStack.Record(record, __dbgDesc);
                }
                _undoController.FocusVertexEdit();
            }

            _selectionOps?.SetSelectionState(sel);
            _renderer?.SetSelectionState(sel);
            _notifyPanels(ChangeKind.Selection);
        }

        // ================================================================
        // 法線再計算 除外辞書ヘルパー
        // ================================================================

        /// <summary>除外辞書内で重複しない名前を返す。</summary>
        private static string GenerateUniqueNormalExcludeName(MeshObject meshObject, string baseName)
        {
            var list = meshObject?.NormalRecalcExcludeList;
            if (list == null) return baseName;

            var used = new HashSet<string>();
            foreach (var set in list)
                if (set != null) used.Add(set.Name);

            if (!used.Contains(baseName)) return baseName;

            int suffix = 1;
            string name;
            do
            {
                name = baseName + "_" + suffix;
                suffix++;
            } while (used.Contains(name));
            return name;
        }

        /// <summary>
        /// 除外辞書エントリを現在の選択へ適用する（置き換え）。
        /// 対象は model.ActiveMeshContext の Selection のみ（PartsSetApply と同じ前提）。
        /// </summary>
        private void NormalExcludeSetApply(ModelContext model, int setIndex)
        {
            if (model == null) { Fail("no current model"); return; }
            var mc   = model.ActiveMeshContext;
            var list = mc?.MeshObject?.NormalRecalcExcludeList;
            if (list == null || setIndex < 0 || setIndex >= list.Count)
            { Fail($"セット番号 {setIndex} が範囲外です"); return; }
            var sel = mc.Selection;
            if (sel == null) { Fail("編集対象メッシュに選択状態がありません"); return; }

            SelectionSnapshot oldSnap = sel.CreateSnapshot();

            var set = list[setIndex];
            var newSnap = new SelectionSnapshot
            {
                Mode     = set.Mode,
                Vertices = new HashSet<int>(set.Vertices),
                Edges    = new HashSet<VertexPair>(set.Edges),
                Faces    = new HashSet<int>(set.Faces),
                Lines    = new HashSet<int>(set.Lines),
            };
            sel.RestoreFromSnapshot(newSnap);

            if (_undoController != null)
            {
                _undoController.VertexEditStack.Record(
                    new SelectionChangeRecord(oldSnap, newSnap), "法線再計算 除外辞書 適用");
                _undoController.FocusVertexEdit();
            }

            _selectionOps?.SetSelectionState(sel);
            _renderer?.SetSelectionState(sel);
            _notifyPanels(ChangeKind.Selection);
        }

        // ================================================================
        // 面の非表示フラグ操作
        // ================================================================
        // HideSelected / HideUnselected は面選択が必須（面選択が無ければ何もしない）。
        // メッシュ丸ごとの非表示は既存のオブジェクト可視性で行う。
        // 隠した面は選択から外す（選択が残ると移動系ツールが動かしてしまうため）。
        private static int ApplyFaceHidden(MeshContext mc, SetFaceHiddenCommand.Mode mode)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return 0;

            var sel = mc.Selection;
            int changed = 0;

            switch (mode)
            {
                case SetFaceHiddenCommand.Mode.HideSelected:
                {
                    if (sel == null || sel.Faces.Count == 0) return 0;
                    foreach (int fi in sel.Faces)
                    {
                        if (fi < 0 || fi >= mo.FaceCount) continue;
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 3 || face.IsHidden) continue;
                        face.SetFlag(FaceFlags.Hidden);
                        changed++;
                    }
                    break;
                }

                case SetFaceHiddenCommand.Mode.HideUnselected:
                {
                    if (sel == null || sel.Faces.Count == 0) return 0;
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 3 || face.IsHidden) continue;
                        if (sel.Faces.Contains(fi)) continue;
                        face.SetFlag(FaceFlags.Hidden);
                        changed++;
                    }
                    break;
                }

                case SetFaceHiddenCommand.Mode.ShowAll:
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (!face.IsHidden) continue;
                        face.ClearFlag(FaceFlags.Hidden);
                        changed++;
                    }
                    break;
                }

                case SetFaceHiddenCommand.Mode.InvertHidden:
                {
                    for (int fi = 0; fi < mo.FaceCount; fi++)
                    {
                        var face = mo.Faces[fi];
                        if (face.VertexCount < 3) continue;
                        face.ToggleFlag(FaceFlags.Hidden);
                        changed++;
                    }
                    break;
                }
            }

            if (changed > 0 && sel != null && sel.Faces.Count > 0)
            {
                var stillHidden = new List<int>();
                foreach (int fi in sel.Faces)
                {
                    if (fi >= 0 && fi < mo.FaceCount && mo.Faces[fi].IsHidden)
                        stillHidden.Add(fi);
                }
                foreach (int fi in stillHidden)
                    sel.DeselectFace(fi);
            }

            return changed;
        }
    }
}
