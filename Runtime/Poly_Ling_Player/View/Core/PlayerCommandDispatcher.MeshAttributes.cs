// PlayerCommandDispatcher.MeshAttributes.cs
// コマンドディスパッチャ：モデル操作と、描画オブジェクトの属性（可視・ロック・名前・並び等）、
// 生成系の Viewer への委譲、可視・ロックの適用。
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
        /// DispatchCore の分担：モデルの選択・名前変更・削除と、空メッシュの追加。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchModel(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── モデル名前変更
                case RenameModelCommand c:
                    var renameTarget = project.GetModel(c.ModelIndex);
                    if (renameTarget != null && !string.IsNullOrEmpty(c.NewName))
                        renameTarget.Name = c.NewName;
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;

                // ── モデル削除
                case DeleteModelCommand c:
                    project.RemoveModelAt(c.ModelIndex);
                    _rebuildModelList();
                    return true;

                // ── メッシュ追加（空メッシュ）
                case AddMeshCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var addBefore = MeshFilterToSkinnedRecord.CaptureList(model);
                    var newMc = new MeshContext
                    {
                        MeshObject        = new MeshObject("New Mesh"),
                        UnityMesh         = new Mesh(),
                        OriginalPositions = new Vector3[0],
                    };
                    newMc.ParentModelContext = model;
                    model.Add(newMc);
                    model.OnListChanged?.Invoke();
                    if (_undoController != null)
                    {
                        var addAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var addRecord = new MeshFilterToSkinnedRecord { BeforeList = addBefore, AfterList = addAfter };
                        {
                            string __dbgDesc = "Add Mesh";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, addRecord);
                            _undoController.MeshListStack.Record(addRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── メッシュ選択
                case SelectMeshCommand sel:
                    if (model == null) { Fail("no current model"); return true; }
                    {
                        // Undo 記録のため選択前のインデックスをキャプチャ
                        var __oldSelected = model.CaptureAllSelectedIndices();

                        switch (sel.Category)
                        {
                            case MeshCategory.Drawable:
                                model.ClearMeshSelection();
                                foreach (int idx in sel.Indices) model.AddToMeshSelection(idx);
                                // ModelContext.SelectMesh() は先頭で ClearMeshSelection() を呼ぶ
                                // 単一選択メソッド。ここで呼ぶと直前の AddToMeshSelection ループの
                                // 結果が破棄され、SelectedDrawableMeshIndices が常に 1 個になる。
                                // メッシュリストの複数選択を受け取る本経路では呼んではならない。
                                // ActiveCategory は AddToMeshSelection が Mesh に設定する。
                                var selMc = model.ActiveMeshContext;
                                if (selMc != null)
                                {
                                    _selectionOps?.SetSelectionState(selMc.Selection);
                                    _renderer?.SetSelectionState(selMc.Selection);
                                }
                                // Phase 2a-2g-1: UpdateSelectedDrawableMesh を EnterTopologyChanged に集約。
                                _viewportManager.EnterTopologyChanged(project);
                                break;
                            case MeshCategory.Bone:
                                model.ClearBoneSelection();
                                foreach (int idx in sel.Indices) model.AddToBoneSelection(idx);
                                // 描画メッシュ側と違い、ここは Enter〜 を呼んでいなかった。
                                // そのため原点マーカー（水色ダイヤ）とギズモが組み直されず、
                                // ボーンを選んでも視点を動かすまで表示が変わらなかった。
                                _viewportManager.EnterSelectionChanged(project);
                                break;
                            case MeshCategory.Morph:
                                model.ClearMorphSelection();
                                foreach (int idx in sel.Indices) model.AddToMorphSelection(idx);
                                _viewportManager.EnterSelectionChanged(project);
                                break;
                        }

                        // Undo 記録: 3 カテゴリ全部 CaptureAllSelectedIndices で一元管理。
                        // SequenceEqual で差分なしなら記録されない (RecordMeshSelectionChange 内部で判定)。
                        var __newSelected = model.CaptureAllSelectedIndices();
                        PLDiag.Cmd($"SelectMesh {sel.Category} old={PLDiag.Ids(__oldSelected)} " +
                                   $"new={PLDiag.Ids(__newSelected)}");
                        _undoController?.SetModelContext(model);
                        _undoController?.RecordMeshSelectionChange(__oldSelected, __newSelected);
                    }
                    _notifyPanels(ChangeKind.Selection);
                    return true;
            }
            return false;
        }

        /// <summary>
        /// DispatchCore の分担：可視・ロック・担当者・生成系（Viewer へ委譲）・原点・姿勢くさび・法線保持・ミラー設定・名前・折りたたみ・複製・順序。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchMeshAttributes(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── 一括可視性
                case SetBatchVisibilityCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    ApplyVisibility(model, c.MasterIndices, c.Visible,
                        $"Set Visibility: {(c.Visible ? "on" : "off")}");
                    return true;
                }

                // ── ロックトグル
                case ToggleLockCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var lckCtx = model.GetMeshContext(c.MasterIndex);
                    if (lckCtx == null) { Fail($"masterIndex {c.MasterIndex} のオブジェクトがありません"); return true; }
                    ApplyLock(model, new[] { c.MasterIndex }, !lckCtx.IsLocked, "Toggle Lock");
                    return true;
                }

                // ── 一括ロック
                case SetBatchLockCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    ApplyLock(model, c.MasterIndices, c.Locked,
                        $"Set Lock: {(c.Locked ? "on" : "off")}");
                    return true;
                }

                // ── IgnorePoseInArmature 設定
                case SetIgnorePoseCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;
                        ctx.IgnorePoseInArmature = c.Value;
                        if (c.Value && ctx.BoneTransform != null)
                            ctx.BoneTransform.Rotation = Vector3.zero;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return true;

                // ── 担当者（EditorName）の設定・解放
                // 到達時点で権限判定は完了している:
                //   リモート発 → RemoteOwnership.TryAuthorize が可否を決めて弾く
                //   ローカル発 → ホスト自身の操作なので無条件に許可
                // よってここは確定適用（force）。ObjectIds の照合だけは残す。
                case SetObjectEditorCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    GetMeshListOps(model).SetObjectEditor(
                        c.MasterIndices, c.EditorName, c.ObjectIds,
                        requesterName: null, force: true);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;

                // ── 生成系。実処理は Viewer 側にあるので委譲する
                // モデルが無くても通す（実処理側が作る）。上の createsOwnProject を参照。
                case CreatePrimitiveMeshCommand c:
                {
                    if (OnCreatePrimitiveMesh == null) { Fail("primitive mesh handler not wired"); return true; }

                    // 「維持する」が立っているときだけ、実行前後の ObjectId を比べて
                    // 出来た出力先を突き止める。立っていなければ従来どおり何も残さない。
                    var __beforeIds = c.Placement.KeepAsGroup ? SnapshotObjectIds() : null;

                    string cpmReason = OnCreatePrimitiveMesh.Invoke(c);
                    if (cpmReason != null) { Fail(cpmReason); return true; }

                    if (c.Placement.KeepAsGroup)
                        CaptureObjectGroup(c, __beforeIds, FallbackOutputIndex(c));

                    // 生成系は受け口（PolyLingPlayerViewerCore.ReportCreatedMesh）が
                    // ReportTargets で対象を報告済み。その対象を保ったまま、
                    // 出来たものの規模だけを足す。
                    //
                    // モデルはここで初めて出来ていることがある（生成系は
                    // プロジェクトを持たない状態から呼べる）ので、
                    // 手前で取った model ではなく取り直す。
                    ReportDataKeepingTargets(BuildTopologyCountsData(
                        _getProject()?.CurrentModel, _pendingResult?.MasterIndices));
                    return true;
                }

                case AddGeneratedMeshCommand c:
                {
                    if (OnAddGeneratedMesh == null) { Fail("add generated mesh handler not wired"); return true; }
                    string agmReason = OnAddGeneratedMesh.Invoke(c);
                    if (agmReason != null) { Fail(agmReason); return true; }
                    return true;
                }

                case CreateHoleBridgeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnCreateHoleBridge == null) { Fail("hole bridge handler not wired"); return true; }
                    string chbReason = OnCreateHoleBridge.Invoke(c);
                    if (chbReason != null) { Fail(chbReason); return true; }
                    ReportData(BuildTopologyCountsData(model, c.MeshA, c.MeshB));
                    return true;
                }

                case CreateEdgeBridgeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnCreateEdgeBridge == null) { Fail("edge bridge handler not wired"); return true; }
                    string cebReason = OnCreateEdgeBridge.Invoke(c);
                    if (cebReason != null) { Fail(cebReason); return true; }
                    ReportData(BuildTopologyCountsData(model, c.MeshIndex));
                    return true;
                }

                case DeleteFacesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnDeleteFaces == null) { Fail("delete faces handler not wired"); return true; }
                    string dfReason = OnDeleteFaces.Invoke(c);
                    if (dfReason != null) { Fail(dfReason); return true; }
                    ReportData(BuildTopologyCountsData(model, c.MeshIndex));
                    return true;
                }

                case MatchHoleRingCountCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnMatchHoleRingCount == null) { Fail("hole ring count handler not wired"); return true; }
                    string mhrReason = OnMatchHoleRingCount.Invoke(c);
                    if (mhrReason != null) { Fail(mhrReason); return true; }
                    ReportData(BuildTopologyCountsData(model, c.BaseMeshIndex, c.TargetMeshIndex));
                    return true;
                }

                case CreateObjectArrayCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (OnCreateObjectArray == null) { Fail("object array handler not wired"); return true; }
                    string coaReason = OnCreateObjectArray.Invoke(c);
                    if (coaReason != null) { Fail(coaReason); return true; }
                    return true;
                }

                // ── オブジェクト原点の一括設定（CSV読み込み）
                case ApplyObjectOriginsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    ApplyObjectOrigins(model, c);
                    return true;
                }

                // ── 姿勢くさびの生成
                case GenerateObjectPoseWedgesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    GenerateObjectPoseWedges(project, model, c);
                    return true;
                }

                // ── 姿勢くさびの取り込み
                case ApplyObjectPoseWedgesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    ApplyObjectPoseWedges(model, c);
                    return true;
                }

                // ── PreserveNormals 設定
                case SetPreserveNormalsCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    foreach (int idx in c.MasterIndices)
                    {
                        var pnCtx = model.GetMeshContext(idx);
                        if (pnCtx == null) continue;
                        pnCtx.PreserveNormals = c.Value;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return true;

                // ── ミラー分岐ルート設定
                case SetMirrorBranchRootCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;
                        ctx.IsMirrorBranchRoot = c.Value;
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return true;

                // ── ミラータイプ
                case CycleMirrorTypeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var mirCtx = model.GetMeshContext(c.MasterIndex);
                    if (mirCtx == null) { Fail($"masterIndex {c.MasterIndex} のオブジェクトがありません"); return true; }

                    int mirOld = mirCtx.MirrorType;
                    // なし→分離→結合→なし。3 以上は MeshContext.MirrorType の定義に無く、
                    // MQO の mirror 属性へそのまま書き出されてしまうため作らない。
                    mirCtx.MirrorType = Poly_Ling.View.MirrorViewUtil.NextType(mirOld);
                    PLDiag.AttrChange("MirrorType", c.MasterIndex, mirCtx.Name,
                        mirOld.ToString(), mirCtx.MirrorType.ToString());
                    RecordAttributeChange(
                        new MeshAttributeChange { Index = c.MasterIndex, MirrorType = mirOld },
                        new MeshAttributeChange { Index = c.MasterIndex, MirrorType = mirCtx.MirrorType },
                        "Cycle Mirror Type");
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── ミラーの有無そのものを切り替える
                case SetMirrorEnabledCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0) { Fail("対象が指定されていません"); return true; }
                    ApplyMirrorEnabled(model, c.MasterIndices, c.Enabled);
                    return true;
                }

                // ── 一括ミラータイプ
                case SetBatchMirrorTypeCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    int mirValue = Poly_Ling.View.MirrorViewUtil.ClampType(c.MirrorType);
                    var mirOldList = new List<MeshAttributeChange>();
                    var mirNewList = new List<MeshAttributeChange>();
                    foreach (int mi in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(mi);
                        if (ctx == null || ctx.MirrorType == mirValue) continue;

                        PLDiag.AttrChange("MirrorType", mi, ctx.Name, ctx.MirrorType.ToString(), mirValue.ToString());
                        mirOldList.Add(new MeshAttributeChange { Index = mi, MirrorType = ctx.MirrorType });
                        ctx.MirrorType = mirValue;
                        mirNewList.Add(new MeshAttributeChange { Index = mi, MirrorType = mirValue });
                    }
                    if (mirOldList.Count == 0) { Fail("ミラー種別を変えられる対象がありません"); return true; }
                    RecordAttributeChanges(mirOldList, mirNewList,
                        $"Set Mirror Type: {mirValue} x{mirOldList.Count}");
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── メッシュ名前変更
                case RenameMeshCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var renCtx = model.GetMeshContext(c.MasterIndex);
                    if (renCtx == null) { Fail($"masterIndex {c.MasterIndex} のオブジェクトがありません"); return true; }
                    if (string.IsNullOrEmpty(c.NewName)) { Fail("NewName が空です"); return true; }
                    string __oldName = renCtx.Name;
                    // 変更なし。何もしないが失敗ではないので Fail は呼ばない
            // （同じ名前へ改名しただけでリモートがエラーを受け取らないようにする）。
            if (__oldName == c.NewName) return true;
                    renCtx.Name = c.NewName;
                    // Undo 記録 (MeshAttributesBatchChangeRecord は Name 属性に対応済み)
                    if (_undoController != null)
                    {
                        var __oldList = new List<MeshAttributeChange> {
                            new MeshAttributeChange { Index = c.MasterIndex, Name = __oldName }
                        };
                        var __newList = new List<MeshAttributeChange> {
                            new MeshAttributeChange { Index = c.MasterIndex, Name = c.NewName }
                        };
                        var __record = new MeshAttributesBatchChangeRecord(__oldList, __newList);
                        string __desc = $"Rename Mesh: {__oldName} -> {c.NewName}";
                        PLDiag.UndoRecord("MeshList", __desc, __record);
                        _undoController.MeshListStack.Record(__record, __desc);
                        _undoController.FocusMeshList();
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── メッシュ名の一括変更（名称一括変更 CSV）
                // 希望名は MeshRenameCsvHelper.ResolveUniqueNames でモデル全体に対して
                // 一意化してから適用する。Undo は1レコードにまとめる。
                case RenameMeshesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.NewNames == null) { Fail("MasterIndices と NewNames を指定してください"); return true; }

                    var rnsResolved = MeshRenameCsvHelper.ResolveUniqueNames(
                        model, c.MasterIndices, c.NewNames);

                    var rnsOldList = new List<MeshAttributeChange>();
                    var rnsNewList = new List<MeshAttributeChange>();
                    for (int i = 0; i < rnsResolved.Length; i++)
                    {
                        string rnsName = rnsResolved[i];
                        if (string.IsNullOrEmpty(rnsName)) continue;
                        int rnsIndex = c.MasterIndices[i];
                        var rnsCtx = model.GetMeshContext(rnsIndex);
                        if (rnsCtx == null) continue;
                        if (rnsCtx.Name == rnsName) continue;
                        PLDiag.AttrChange("Name", rnsIndex, rnsCtx.Name, rnsCtx.Name, rnsName);
                        rnsOldList.Add(new MeshAttributeChange { Index = rnsIndex, Name = rnsCtx.Name });
                        rnsCtx.Name = rnsName;
                        rnsNewList.Add(new MeshAttributeChange { Index = rnsIndex, Name = rnsName });
                    }
                    if (rnsOldList.Count == 0) { Fail("改名できる対象がありません"); return true; }
                    RecordAttributeChanges(rnsOldList, rnsNewList,
                        $"Rename Meshes: x{rnsOldList.Count}");
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── メッシュ折りたたみ状態変更 (TreeView の展開/折りたたみ)
                // MeshContext.IsFolding を Undo 記録付きで更新する。
                // MeshAttributesBatchChangeRecord は IsFolding 属性に対応済み。
                case SetMeshFoldingCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var fldCtx = model.GetMeshContext(c.MasterIndex);
                    if (fldCtx == null) { Fail($"masterIndex {c.MasterIndex} のオブジェクトがありません"); return true; }
                    // 変更なし。上と同じ理由で Fail は呼ばない。
            if (fldCtx.IsFolding == c.IsFolding) return true;
                    bool __oldFolding = fldCtx.IsFolding;
                    fldCtx.IsFolding = c.IsFolding;
                    if (_undoController != null)
                    {
                        var __oldList = new List<MeshAttributeChange> {
                            new MeshAttributeChange { Index = c.MasterIndex, IsFolding = __oldFolding }
                        };
                        var __newList = new List<MeshAttributeChange> {
                            new MeshAttributeChange { Index = c.MasterIndex, IsFolding = c.IsFolding }
                        };
                        var __record = new MeshAttributesBatchChangeRecord(__oldList, __newList);
                        string __desc = $"Set Folding [{c.MasterIndex}]: {__oldFolding} -> {c.IsFolding}";
                        PLDiag.UndoRecord("MeshList", __desc, __record);
                        _undoController.MeshListStack.Record(__record, __desc);
                        _undoController.FocusMeshList();
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                case DeleteMeshesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0) { Fail("対象が指定されていません"); return true; }
                    // 削除前の選択状態をキャプチャ
                    var __oldSel = model.CaptureAllSelectedIndices();
                    var __removed = new List<(int, MeshContext)>();
                    // 降順で削除 (上位 index の削除で下位 index がずれないように)
                    foreach (int idx in c.MasterIndices.OrderByDescending(i => i))
                    {
                        if (idx < 0 || idx >= model.MeshContextCount) continue;
                        var __mc = model.GetMeshContext(idx);
                        if (__mc == null) continue;
                        __removed.Add((idx, __mc));
                        model.RemoveAt(idx);
                    }
                    if (__removed.Count > 0 && _undoController != null)
                    {
                        var __newSel = model.CaptureAllSelectedIndices();
                        _undoController.RecordMeshContextsRemove(__removed, __oldSel, __newSel);
                    }
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── メッシュ複製
                case DuplicateMeshesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0) { Fail("対象が指定されていません"); return true; }
                    var __oldSel = model.CaptureAllSelectedIndices();
                    var __added = new List<(int, MeshContext)>();
                    foreach (int idx in c.MasterIndices)
                    {
                        var srcCtx = model.GetMeshContext(idx);
                        if (srcCtx == null) continue;

                        // 名前は必ずモデル内で一意にする。
                        // 【以前の不具合】ここは new MeshContext { Name = ..., MeshObject = ... }
                        //   と書いていた。MeshContext.Name は MeshObject への委譲プロパティで、
                        //   MeshObject が null のとき setter は何もしない（MeshContext.cs:32-36）。
                        //   オブジェクト初期化子は書いた順に走るので Name の代入が捨てられ、
                        //   複製物は元と同名のまま出来ていた。名前で引く仕組み
                        //   （MeshSelectionSet＝オブジェクト辞書）が複製物まで巻き込む。
                        string __dupName = model.GenerateUniqueMeshName(srcCtx.Name + "_copy");

                        // 別オブジェクトとしての複製。ObjectId と EditorName は引き継がず、
                        // model.Add が新しい ObjectId を振る。
                        var dup = Poly_Ling.Ops.MeshContextCloneOps.Clone(
                            srcCtx, Poly_Ling.Ops.MeshContextCloneKind.NewObject, __dupName);
                        if (dup == null) continue;

                        int __addedIdx = model.Add(dup);
                        __added.Add((__addedIdx, dup));
                    }
                    if (__added.Count > 0 && _undoController != null)
                    {
                        var __newSel = model.CaptureAllSelectedIndices();
                        _undoController.RecordMeshContextsAdd(__added, __oldSel, __newSel);
                    }
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── メッシュリスト順序変更 (D&D/上下移動/Indent/Outdent)
                // Editor と同一ロジック (MeshListOps.ReorderMeshes) を使用。
                // Undo 記録 (MeshReorderChangeRecord) も内部で実行される。
                case ReorderMeshesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    // Entries は EntryValues から毎回組み立てる算出プロパティ。
                    // 2 回読むと 2 回作るので、1 回だけ取る。
                    var __entries = c.Entries;
                    if (__entries == null || __entries.Length == 0) { Fail("Entries が空です"); return true; }
                    var __ops = GetMeshListOps(model);
                    __ops.ReorderMeshes(c.Category, __entries, c.PreserveWorldTransform);
                    model.OnListChanged?.Invoke();
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── BonePose 初期化
                case InitBonePoseCommand c:
                    if (model == null) { Fail("no current model"); return true; }
                    foreach (int idx in c.MasterIndices)
                    {
                        var ctx = model.GetMeshContext(idx);
                        if (ctx == null) continue;
                        if (ctx.BonePoseData == null)
                        {
                            ctx.BonePoseData          = new BonePoseData();
                            ctx.BonePoseData.IsActive = true;
                        }
                    }
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
            }
            return false;
        }

        // ================================================================
        // 可視・ロックの適用
        // ================================================================

        /// <summary>
        /// 可視性を設定する。ミラー側メッシュへも同じ値を広げる。
        ///
        /// ミラー側は実体側の従属だが、IsVisible / IsLocked を追随させる経路が
        /// 元々存在せず、実体を消してもミラーだけ残っていた。
        /// 姿勢（SyncDerivedMirrorTransforms）と同じ「実体側が正」の方針に合わせる。
        /// Undo にはミラー側の変更も含める。含めないと戻したときに片側だけ残る。
        /// </summary>
        private void ApplyVisibility(ModelContext model, IReadOnlyList<int> masterIndices, bool visible, string desc)
        {
            if (model == null || masterIndices == null) return;

            var targets = ExpandToMirrorPeers(model, masterIndices);
            var oldList = new List<MeshAttributeChange>();
            var newList = new List<MeshAttributeChange>();

            foreach (int mi in targets)
            {
                var ctx = model.GetMeshContext(mi);
                if (ctx == null || ctx.IsVisible == visible) continue;
                PLDiag.AttrChange("IsVisible", mi, ctx.Name, ctx.IsVisible.ToString(), visible.ToString());
                oldList.Add(new MeshAttributeChange { Index = mi, IsVisible = ctx.IsVisible });
                ctx.IsVisible = visible;
                newList.Add(new MeshAttributeChange { Index = mi, IsVisible = visible });
            }

            if (oldList.Count == 0) return;
            RecordAttributeChanges(oldList, newList, $"{desc} x{oldList.Count}");
            _notifyPanels(ChangeKind.Attributes);
        }

        /// <summary>ロックを設定する。ミラー側メッシュへも同じ値を広げる。</summary>
        private void ApplyLock(ModelContext model, IReadOnlyList<int> masterIndices, bool locked, string desc)
        {
            if (model == null || masterIndices == null) return;

            var targets = ExpandToMirrorPeers(model, masterIndices);
            var oldList = new List<MeshAttributeChange>();
            var newList = new List<MeshAttributeChange>();

            foreach (int mi in targets)
            {
                var ctx = model.GetMeshContext(mi);
                if (ctx == null || ctx.IsLocked == locked) continue;
                PLDiag.AttrChange("IsLocked", mi, ctx.Name, ctx.IsLocked.ToString(), locked.ToString());
                oldList.Add(new MeshAttributeChange { Index = mi, IsLocked = ctx.IsLocked });
                ctx.IsLocked = locked;
                newList.Add(new MeshAttributeChange { Index = mi, IsLocked = locked });
            }

            if (oldList.Count == 0) return;
            RecordAttributeChanges(oldList, newList, $"{desc} x{oldList.Count}");
            _notifyPanels(ChangeKind.Attributes);
        }

        /// <summary>指定インデックスに、対応するミラー側インデックスを足した一覧を返す。</summary>
        private static List<int> ExpandToMirrorPeers(ModelContext model, IReadOnlyList<int> masterIndices)
        {
            var targets = new List<int>(masterIndices.Count * 2);
            foreach (int mi in masterIndices)
            {
                if (mi < 0) continue;
                if (!targets.Contains(mi)) targets.Add(mi);
                MirrorBranchOps.CollectMirrorPeers(model, mi, targets);
            }
            return targets;
        }
    }
}
