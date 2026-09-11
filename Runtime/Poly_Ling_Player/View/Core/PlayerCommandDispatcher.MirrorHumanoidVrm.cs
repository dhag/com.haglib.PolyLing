// PlayerCommandDispatcher.MirrorHumanoidVrm.cs
// コマンドディスパッチャ：Quad減面・ミラー・Humanoid・マッスル可動域・VRM 設定・Avatar リターゲット。
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
        /// DispatchCore の分担：Quad減面・ミラー（Bake／解除）・Humanoid・マッスル可動域・VRM 設定・Avatar リターゲット・検証用ダミー装備。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchMirrorHumanoidVrm(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── Mirror Bake
                case BakeMirrorCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var srcMc = model.GetMeshContext(c.SourceMasterIndex);
                    if (srcMc?.MeshObject == null)
                    {
                        Debug.LogWarning($"[MirrorBake] 対象メッシュが見つかりません masterIndex={c.SourceMasterIndex}");
                        return true;
                    }

                    var bakeMo = srcMc.MeshObject;

                    if (bakeMo.MirrorBakeState != null)
                    {
                        Debug.LogWarning($"[MirrorBake] \"{srcMc.Name}\" は既に実体化済みです。先に解除してください");
                        return true;
                    }

                    // ミラー平面の決定。
                    // メッシュが見た目・エクスポート用のミラーモード（MirrorType > 0）なら
                    // メッシュ自身の軸・距離を使う。そうでなければパネル指定を使う。
                    int   bakeAxis      = c.MirrorAxis;
                    float bakeOffset    = c.PlaneOffset;
                    float bakeThreshold = c.Threshold;

                    if (srcMc.MirrorType > 0)
                    {
                        bakeAxis   = srcMc.MirrorAxis == 2 ? 1 : (srcMc.MirrorAxis == 4 ? 2 : 0);
                        bakeOffset = 0f;
                        // MQO の結合ミラー(2)は mirror_dis が溶接距離。分離ミラー(1)は溶接しない。
                        bakeThreshold = srcMc.MirrorType == 2 ? srcMc.MirrorDistance : 0f;
                    }

                    // 境界頂点（選択頂点モードのときだけ渡す。メッシュ設定より優先）
                    System.Collections.Generic.List<int> bakeBoundary = null;
                    if (c.BoundaryMode == MirrorBoundaryMode.SelectedVertices)
                    {
                        var bakeSel = srcMc.Selection;
                        if (bakeSel == null || bakeSel.Vertices.Count == 0)
                        {
                            Debug.LogWarning("[MirrorBake] 選択頂点モードですが頂点が選択されていません");
                            return true;
                        }
                        bakeBoundary = new System.Collections.Generic.List<int>(bakeSel.Vertices);
                    }

                    int bakeVertsBefore = bakeMo.VertexCount;
                    int bakeFacesBefore = bakeMo.FaceCount;

                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(bakeMo, srcMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var bakeBefore = _undoController?.CaptureMeshObjectSnapshot();

                    var bakeResult = MirrorBaker.BakeInPlace(
                        bakeMo, bakeAxis, bakeOffset, bakeThreshold, c.FlipU,
                        bakeBoundary, c.ProjectBoundaryToPlane);

                    if (bakeResult == null)
                    {
                        Debug.LogWarning($"[MirrorBake] 実体化に失敗しました src=\"{srcMc.Name}\"");
                        return true;
                    }

                    // 見た目・エクスポート用のミラーモードは解除する（実体を持ったため）。
                    // 解除に備えて元の設定を退避しておく。
                    bakeResult.SavedMirrorType           = srcMc.MirrorType;
                    bakeResult.SavedMirrorAxis           = srcMc.MirrorAxis;
                    bakeResult.SavedMirrorDistance       = srcMc.MirrorDistance;
                    bakeResult.SavedMirrorMaterialOffset = srcMc.MirrorMaterialOffset;

                    srcMc.MirrorType = 0;
                    srcMc.InvalidateSymmetryCache();

                    bakeMo.MirrorBakeState = bakeResult;

                    SyncMeshContextAfterMirrorEdit(srcMc);

                    if (_undoController != null && bakeBefore != null)
                    {
                        var bakeAfter = _undoController.CaptureMeshObjectSnapshot();
                        _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                            _undoController, bakeBefore, bakeAfter, "ミラー実体化"));
                    }

                    int bakeMergedCount = 0;
                    if (bakeResult.NewVertexOrigin != null)
                        foreach (var o in bakeResult.NewVertexOrigin)
                            if (o == VertexOrigin.Merged) bakeMergedCount++;

                    Debug.Log(
                        $"[MirrorBake] \"{srcMc.Name}\" 実体化 " +
                        $"verts {bakeVertsBefore} → {bakeMo.VertexCount} " +
                        $"faces {bakeFacesBefore} → {bakeMo.FaceCount} " +
                        $"merged={bakeMergedCount} axis={bakeAxis} threshold={bakeThreshold} " +
                        $"boundary={c.BoundaryMode} project={c.ProjectBoundaryToPlane} " +
                        $"savedMirrorType={bakeResult.SavedMirrorType} → 0 " +
                        $"unityMeshVerts={(srcMc.UnityMesh != null ? srcMc.UnityMesh.vertexCount : -1)}");

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── Mirror 実体化の解除（半身へ戻す）
                case UnbakeMirrorCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var ubMc = model.GetMeshContext(c.SourceMasterIndex);
                    if (ubMc?.MeshObject == null) { Fail("対象メッシュがありません"); return true; }

                    var ubMo = ubMc.MeshObject;
                    var ubState = ubMo.MirrorBakeState;
                    if (ubState == null)
                    {
                        Debug.LogWarning($"[MirrorBake] \"{ubMc.Name}\" は実体化されていません");
                        return true;
                    }

                    int ubVertsBefore = ubMo.VertexCount;
                    int ubFacesBefore = ubMo.FaceCount;

                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(ubMo, ubMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }
                    var ubBefore = _undoController?.CaptureMeshObjectSnapshot();

                    if (!MirrorBaker.UnbakeInPlace(ubMo, ubState, c.Mode))
                    {
                        Debug.LogWarning($"[MirrorBake] 解除に失敗しました src=\"{ubMc.Name}\"");
                        return true;
                    }

                    if (c.RestoreSavedMirrorSettings)
                    {
                        // ツール内の「一時ミラー」による解除。
                        // 実体化前のミラー設定をそのまま戻す（恒久設定を変えない）。
                        ubMc.MirrorType           = ubState.SavedMirrorType;
                        ubMc.MirrorAxis           = ubState.SavedMirrorAxis;
                        ubMc.MirrorDistance       = ubState.SavedMirrorDistance;
                        ubMc.MirrorMaterialOffset = ubState.SavedMirrorMaterialOffset;
                    }
                    else
                    {
                        // 半身に戻したので、見た目・エクスポート用のミラーモードを強制的に付ける。
                        // 軸は実体化に使った軸から決める（0:X→1, 1:Y→2, 2:Z→4）。
                        ubMc.MirrorType = 2; // 結合
                        ubMc.MirrorAxis = ubState.BakeAxis == 1 ? 2 : (ubState.BakeAxis == 2 ? 4 : 1);
                        ubMc.MirrorDistance = ubState.SavedMirrorType == 2
                            ? ubState.SavedMirrorDistance
                            : ubState.Threshold;
                        ubMc.MirrorMaterialOffset = ubState.SavedMirrorMaterialOffset;
                    }
                    ubMc.InvalidateSymmetryCache();

                    ubMo.MirrorBakeState = null;

                    // 実体化中に増えていた頂点・面の選択を捨てる
                    ubMc.Selection?.ClearAll();

                    SyncMeshContextAfterMirrorEdit(ubMc);

                    if (_undoController != null && ubBefore != null)
                    {
                        var ubAfter = _undoController.CaptureMeshObjectSnapshot();
                        _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                            _undoController, ubBefore, ubAfter, "ミラー実体化の解除"));
                    }

                    Debug.Log(
                        $"[MirrorBake] \"{ubMc.Name}\" 解除 " +
                        $"verts {ubVertsBefore} → {ubMo.VertexCount} " +
                        $"faces {ubFacesBefore} → {ubMo.FaceCount} " +
                        $"mode={c.Mode} restoreSaved={c.RestoreSavedMirrorSettings} " +
                        $"mirrorType={ubMc.MirrorType} axis={ubMc.MirrorAxis} dist={ubMc.MirrorDistance}");

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── Humanoidマッピング適用
                case ApplyHumanoidMappingCommand c:
                {
                    if (model == null || c.Mapping == null) { Fail("Mapping が空です"); return true; }
                    _undoController?.SetModelContext(model);
                    var hmBefore = model.HumanoidMapping.Clone();
                    model.HumanoidMapping.CopyFrom(c.Mapping);
                    var hmAfter = model.HumanoidMapping.Clone();
                    if (_undoController != null)
                    {
                        var record = new HumanoidMappingChangedRecord(hmBefore, hmAfter, "Apply Humanoid Mapping");
                        {
                            string __dbgDesc = "Apply Humanoid Mapping";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── Humanoidマッピングクリア
                case ClearHumanoidMappingCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    _undoController?.SetModelContext(model);
                    var hmcBefore = model.HumanoidMapping.Clone();
                    model.HumanoidMapping.ClearAll();
                    var hmcAfter = model.HumanoidMapping.Clone();
                    if (_undoController != null)
                    {
                        var record = new HumanoidMappingChangedRecord(hmcBefore, hmcAfter, "Clear Humanoid Mapping");
                        {
                            string __dbgDesc = "Clear Humanoid Mapping";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── マッスル可動域を書き込む
                //   角度はコマンドが度、格納がラジアン。変換はここで行う
                //   （SetHumanLimitCommand の「単位は度」を参照）。
                case SetHumanLimitCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0)
                    { Fail("対象が指定されていません"); return true; }

                    // 下限が上限を超えていたら黙って入れ替えない。取り違えを隠すため。
                    for (int axis = 0; axis < 3; axis++)
                    {
                        if (c.MinDegrees[axis] > c.MaxDegrees[axis])
                        {
                            Fail($"可動域の下限が上限を超えています（軸 {axis}: "
                                 + $"{c.MinDegrees[axis]} > {c.MaxDegrees[axis]}）");
                            return true;
                        }
                    }

                    var hlTargets = new List<int>(c.MasterIndices);
                    _undoController?.SetModelContext(model);
                    var hlBefore = CaptureHumanLimit(model, hlTargets);

                    int hlDone = HumanLimitOps.SetLimit(
                        model, hlTargets,
                        c.MinDegrees    * Mathf.Deg2Rad,
                        c.MaxDegrees    * Mathf.Deg2Rad,
                        c.CenterDegrees * Mathf.Deg2Rad,
                        c.AxisLength);

                    if (hlDone == 0)
                    { Fail("可動域を付けられる対象がありません（ボーンのみ）"); return true; }

                    RecordHumanLimitChange(model, hlTargets, hlBefore, $"マッスル可動域 x{hlDone}");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── マッスル可動域を外す（Unity 既定へ戻す）
                case ClearHumanLimitCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0)
                    { Fail("対象が指定されていません"); return true; }

                    var hlcTargets = new List<int>(c.MasterIndices);
                    _undoController?.SetModelContext(model);
                    var hlcBefore = CaptureHumanLimit(model, hlcTargets);

                    int hlcDone = HumanLimitOps.ClearLimit(model, hlcTargets);
                    if (hlcDone == 0) { Fail("可動域を持つボーンがありません"); return true; }

                    RecordHumanLimitChange(model, hlcTargets, hlcBefore, $"マッスル可動域の解除 x{hlcDone}");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── VRM メタ情報を書き込む
                case SetVrmMetaCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);
                    var vmBefore = VrmModelSettingsSnapshot.Capture(model);

                    var meta = new VrmMetaData
                    {
                        Name                 = c.Name ?? "",
                        Version              = c.Version ?? "",
                        Authors              = new List<string>(c.Authors ?? Array.Empty<string>()),
                        CopyrightInformation = c.CopyrightInformation ?? "",
                        ContactInformation   = c.ContactInformation ?? "",
                        References           = new List<string>(c.References ?? Array.Empty<string>()),
                        ThirdPartyLicenses   = c.ThirdPartyLicenses ?? "",
                        ThumbnailPath        = c.ThumbnailPath ?? "",

                        AvatarPermission          = ModelSerializer.ToVrmAvatarPermission(c.AvatarPermission),
                        ViolentUsage              = c.ViolentUsage,
                        SexualUsage               = c.SexualUsage,
                        CommercialUsage           = ModelSerializer.ToVrmCommercialUsage(c.CommercialUsage),
                        PoliticalOrReligiousUsage = c.PoliticalOrReligiousUsage,
                        AntisocialOrHateUsage     = c.AntisocialOrHateUsage,

                        CreditNotation  = ModelSerializer.ToVrmCreditNotation(c.CreditNotation),
                        Redistribution  = c.Redistribution,
                        Modification    = ModelSerializer.ToVrmModification(c.Modification),
                        OtherLicenseUrl = c.OtherLicenseUrl ?? "",
                    };

                    if (!VrmSettingsOps.SetMeta(model, meta))
                    { Fail("VRM メタ情報を書き込めませんでした"); return true; }

                    RecordVrmModelSettings(vmBefore, model, "VRM メタ情報");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── VRM メタ情報を未設定へ戻す
                case ClearVrmMetaCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);
                    var vmcBefore = VrmModelSettingsSnapshot.Capture(model);

                    if (!VrmSettingsOps.ClearMeta(model))
                    { Fail("VRM メタ情報は設定されていません"); return true; }

                    RecordVrmModelSettings(vmcBefore, model, "VRM メタ情報の解除");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── VRM 視線設定を書き込む
                case SetVrmLookAtCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);
                    var vlBefore = VrmModelSettingsSnapshot.Capture(model);

                    var lookAt = new VrmLookAtData
                    {
                        OffsetFromHead  = c.OffsetFromHead,
                        LookAtType      = (c.LookAtType == 1) ? VrmLookAtType.Expression : VrmLookAtType.Bone,
                        HorizontalInner = new VrmLookAtRangeMap(c.HorizontalInner.x, c.HorizontalInner.y),
                        HorizontalOuter = new VrmLookAtRangeMap(c.HorizontalOuter.x, c.HorizontalOuter.y),
                        VerticalDown    = new VrmLookAtRangeMap(c.VerticalDown.x,    c.VerticalDown.y),
                        VerticalUp      = new VrmLookAtRangeMap(c.VerticalUp.x,      c.VerticalUp.y),
                    };

                    if (!VrmSettingsOps.SetLookAt(model, lookAt))
                    { Fail("VRM 視線設定を書き込めませんでした"); return true; }

                    RecordVrmModelSettings(vlBefore, model, "VRM 視線設定");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── VRM 視線設定を未設定へ戻す
                case ClearVrmLookAtCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);
                    var vlcBefore = VrmModelSettingsSnapshot.Capture(model);

                    if (!VrmSettingsOps.ClearLookAt(model))
                    { Fail("VRM 視線設定は設定されていません"); return true; }

                    RecordVrmModelSettings(vlcBefore, model, "VRM 視線設定の解除");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── 一人称カメラでの扱いを決める
                case SetVrmFirstPersonCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0)
                    { Fail("対象が指定されていません"); return true; }

                    var fpTargets = new List<int>(c.MasterIndices);
                    var fpType    = ModelSerializer.ToVrmFirstPersonType(c.FirstPersonType);

                    _undoController?.SetModelContext(model);
                    var fpBefore = CaptureVrmFirstPerson(model, fpTargets);

                    int fpDone = VrmSettingsOps.SetFirstPerson(model, fpTargets, fpType);
                    if (fpDone == 0)
                    { Fail("一人称の指定を持てる対象がありません（描画オブジェクトのみ）"); return true; }

                    RecordVrmFirstPersonChange(model, fpTargets, fpBefore, $"一人称の扱い x{fpDone}");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── Avatar リターゲット設定を書き込む
                case SetAvatarRetargetCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);
                    var arBefore = AvatarRetargetSnapshot.Capture(model);

                    var data = new AvatarRetargetData
                    {
                        UpperArmTwist     = c.UpperArmTwist,
                        LowerArmTwist     = c.LowerArmTwist,
                        UpperLegTwist     = c.UpperLegTwist,
                        LowerLegTwist     = c.LowerLegTwist,
                        ArmStretch        = c.ArmStretch,
                        LegStretch        = c.LegStretch,
                        FeetSpacing       = c.FeetSpacing,
                        HasTranslationDoF = c.HasTranslationDoF,
                    };

                    if (!AvatarRetargetOps.SetRetarget(model, data))
                    { Fail("Avatar リターゲット設定を書き込めませんでした"); return true; }

                    RecordAvatarRetarget(arBefore, model, "Avatar リターゲット設定");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── Avatar リターゲット設定を未設定へ戻す
                case ClearAvatarRetargetCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);
                    var arcBefore = AvatarRetargetSnapshot.Capture(model);

                    if (!AvatarRetargetOps.ClearRetarget(model))
                    { Fail("Avatar リターゲット設定は設定されていません"); return true; }

                    RecordAvatarRetarget(arcBefore, model, "Avatar リターゲット設定の解除");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── スプリングボーン検証用ダミー装備の生成（システムデバッグ）
                //   揺れデータのオーサリング UI が無いので、検証用に生成する。
                //   トポロジが変わるので MeshFilterToSkinnedRecord で丸ごと記録する
                //   （AddMeshCommand と同じ扱い）。
                case BuildSpringBoneTestRigCommand sbtCmd:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);
                    var sbtBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var sbtParams = sbtCmd.Params
                        ?? new Poly_Ling.Tools.SpringBoneTest.SpringBoneTestRigParams();

                    if (sbtCmd.ClearExisting)
                        Poly_Ling.Tools.SpringBoneTest.SpringBoneTestRigBuilder
                            .RemoveGenerated(model, sbtParams.Prefix);

                    var sbtResult = Poly_Ling.Tools.SpringBoneTest.SpringBoneTestRigBuilder
                        .Build(model, sbtParams);

                    Debug.Log(
                        "[BuildSpringBoneTestRig] " + sbtResult.Message +
                        $" ボーン {sbtResult.AddedBoneCount} / メッシュ {sbtResult.AddedMeshCount}" +
                        $" / チェーン {sbtResult.ChainCount} / ジョイント {sbtResult.JointCount}" +
                        $" / コライダー {sbtResult.ColliderCount}");

                    // 生成側はログ方針を持たないので、ここで流す。
                    foreach (string sbtNote in sbtResult.Notes)
                        Debug.Log("[BuildSpringBoneTestRig] " + sbtNote);
                    foreach (string sbtWarn in sbtResult.Warnings)
                        Debug.LogWarning("[BuildSpringBoneTestRig] " + sbtWarn);

                    model.OnListChanged?.Invoke();

                    if (_undoController != null)
                    {
                        var sbtAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var sbtRecord = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = sbtBefore,
                            AfterList  = sbtAfter
                        };
                        {
                            string __dbgDesc = "Build SpringBone Test Rig";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, sbtRecord);
                            _undoController.MeshListStack.Record(sbtRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    model.IsDirty = true;
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ================================================================
                // 揺れもの（VRM SpringBone）のオーサリング
                // ================================================================
                //
                // 実処理は Core/Ops/SpringBoneOps.cs。ここは Undo 記録と通知だけを持つ。
                // 付帯先はボーンに限らない（MeshObject.cs の SpringBone 節）。

                // ── 評価設定（モデル全体で 1 組）
                case SetSpringBoneSettingsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);
                    var sbsBefore = SpringBoneModelSettingsSnapshot.Capture(model);

                    model.SpringBoneFixedDeltaTime = Mathf.Max(0f, c.FixedDeltaTime);
                    model.SpringBoneWarmupFrames   = Mathf.Max(0, c.WarmupFrames);

                    RecordSpringBoneModelSettings(sbsBefore, model, "揺れの評価設定");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // ミラーの有無
        // ================================================================

        /// <summary>
        /// ミラーの有無を切り替える。ミラー側 MeshContext を作る／始末する。
        ///
        /// 解消の扱いを MirrorGeometryDerived で分ける。
        ///   true （MQO 系）… 実体側から再生成できるので破棄する。
        ///                     ミラーの付け外しを繰り返す使い方で、残すとゴミが増える。
        ///   false（PMX 系）… ボーンウェイト等を持つので独立メッシュとして残す。
        ///                     実体側に ObjectId を控え、再ミラー化で引き当てる。
        ///
        /// リスト構造が変わるため ChangeKind.ListStructure で通知する。
        /// </summary>
        private void ApplyMirrorEnabled(ModelContext model, int[] masterIndices, bool enabled)
        {
            var oldSel = model.CaptureAllSelectedIndices();

            var removed = new List<(int, MeshContext)>();
            var added   = new List<(int Index, MeshContext MeshContext)>();
            int changed = 0;

            // 破棄・挿入で index がずれるため降順に処理する
            foreach (int realIdx in masterIndices.OrderByDescending(i => i))
            {
                var realCtx = model.GetMeshContext(realIdx);
                if (realCtx == null) continue;

                if (enabled)
                {
                    if (EnableMirror(model, realIdx, realCtx, added)) changed++;
                }
                else
                {
                    if (DisableMirror(model, realIdx, realCtx, removed)) changed++;
                }
            }

            if (changed == 0) return;

            if (_undoController != null)
            {
                var newSel = model.CaptureAllSelectedIndices();
                _undoController.SetModelContext(model);
                if (removed.Count > 0) _undoController.RecordMeshContextsRemove(removed, oldSel, newSel);
                if (added.Count   > 0) _undoController.RecordMeshContextsAdd(added, oldSel, newSel);
            }

            // 生成・破棄でリスト構造と階層が変わったのでワールド行列を組み直す。
            //   ComputeWorldMatrices の冒頭で SyncDerivedMirrorTransforms が走り、
            //   ミラー側の姿勢と階層親を実体側からそろえる。これを通さないと
            //   生成直後のミラーが未計算の行列のまま描画される。
            //   他の姿勢変更コマンドは軒並みこれを呼んでおり、ここだけ抜けていた。
            model.ComputeWorldMatrices();

            _notifyPanels(ChangeKind.ListStructure);
        }

        /// <summary>ミラーを解消する。戻り値は変化があったか。</summary>
        private bool DisableMirror(ModelContext model, int realIdx, MeshContext realCtx,
                                   List<(int, MeshContext)> removed)
        {
            var peers = new List<int>();
            MirrorBranchOps.CollectMirrorPeers(model, realIdx, peers);

            bool touched = false;

            foreach (int mirrorIdx in peers.OrderByDescending(i => i))
            {
                var mirrorCtx = model.GetMeshContext(mirrorIdx);
                if (mirrorCtx == null) continue;

                // ペアの登録は先に外す（破棄・独立化のどちらでも不要になる）
                model.MirrorPairs?.RemoveAll(pr => pr.Mirror == mirrorCtx || pr.Real == realCtx);

                if (mirrorCtx.MirrorGeometryDerived)
                {
                    PLDiag.AttrChange("MirrorDiscard", mirrorIdx, mirrorCtx.Name, "mirror", "removed");
                    removed.Add((mirrorIdx, mirrorCtx));
                    model.RemoveAt(mirrorIdx);
                }
                else
                {
                    PLDiag.AttrChange("MirrorDetach", mirrorIdx, mirrorCtx.Name, "mirror", "mesh");
                    mirrorCtx.Type = MeshType.Mesh;
                    if (mirrorCtx.MeshObject != null) mirrorCtx.MeshObject.Type = MeshType.Mesh;
                    mirrorCtx.BakedMirrorSourceIndex = -1;
                    realCtx.DetachedMirrorObjectId = mirrorCtx.ObjectId;
                }
                touched = true;
            }

            if (realCtx.MirrorType != 0 || realCtx.HasBakedMirrorChild) touched = true;
            realCtx.MirrorType = 0;
            realCtx.HasBakedMirrorChild = false;
            realCtx.InvalidateSymmetryCache();

            return touched;
        }

        /// <summary>ミラーを有効にする。戻り値は変化があったか。</summary>
        private bool EnableMirror(ModelContext model, int realIdx, MeshContext realCtx,
                                  List<(int Index, MeshContext MeshContext)> added)
        {
            // 既にミラー側を持っているなら属性だけ戻す
            var existing = new List<int>();
            MirrorBranchOps.CollectMirrorPeers(model, realIdx, existing);
            if (existing.Count > 0)
            {
                if (realCtx.MirrorType != 0) return false;
                realCtx.MirrorType = 1;
                return true;
            }

            if (realCtx.MirrorAxis == 0) realCtx.MirrorAxis = 1;
            realCtx.MirrorType = 1;

            // 切り離してあった PMX 系ミラーを引き当てる
            int detachedIdx = ObjectIdAllocator.IndexOfId(model.MeshContextList, realCtx.DetachedMirrorObjectId);
            if (detachedIdx >= 0)
            {
                var mirrorCtx = model.GetMeshContext(detachedIdx);
                if (mirrorCtx != null)
                {
                    mirrorCtx.Type = MeshType.MirrorSide;
                    if (mirrorCtx.MeshObject != null) mirrorCtx.MeshObject.Type = MeshType.MirrorSide;

                    var pair = new MirrorPair
                    {
                        Real   = realCtx,
                        Mirror = mirrorCtx,
                        Axis   = realCtx.GetMirrorSymmetryAxis()
                    };
                    if (pair.Build())
                    {
                        SyncMirrorWeightsIfSkinned(pair, realCtx);
                        model.MirrorPairs.Add(pair);
                        realCtx.DetachedMirrorObjectId = 0;
                        PLDiag.AttrChange("MirrorReattach", detachedIdx, mirrorCtx.Name, "mesh", "mirror");
                        return true;
                    }

                    // 頂点数が合わないなど張れない場合は元へ戻す
                    Debug.LogWarning($"[Mirror] 再ペアに失敗しました real=\"{realCtx.Name}\" mirror=\"{mirrorCtx.Name}\"\n{pair.BuildLog}");
                    mirrorCtx.Type = MeshType.Mesh;
                    if (mirrorCtx.MeshObject != null) mirrorCtx.MeshObject.Type = MeshType.Mesh;
                    realCtx.MirrorType = 0;
                    return false;
                }
            }

            // 生成ミラーを作る
            var generated = MirrorBranchOps.CreateDerivedMirrorContext(realCtx, realIdx);
            if (generated == null)
            {
                // 頂点を持たないメッシュなど。属性だけ立てて終わる。
                return true;
            }

            generated.Type = MeshType.MirrorSide;
            if (generated.MeshObject != null) generated.MeshObject.Type = MeshType.MirrorSide;

            // 左右対応が付く名前は「左腕 → 右腕」にする。付かない名前だけ従来の
            // 接尾辞（"+"）へ落とす。既に同名が居る場合も接尾辞へ落として衝突を避ける。
            generated.Name = MirrorNameOps.MakeMirrorName(
                realCtx.Name,
                MirrorBranchOps.MirrorBranchSuffix,
                n => ExistsMeshName(model, n));
            if (generated.MeshObject != null) generated.MeshObject.Name = generated.Name;

            int insertAt = realIdx + 1;
            model.Insert(insertAt, generated);

            var genPair = new MirrorPair
            {
                Real   = realCtx,
                Mirror = generated,
                Axis   = realCtx.GetMirrorSymmetryAxis()
            };
            if (genPair.Build())
            {
                SyncMirrorWeightsIfSkinned(genPair, realCtx);
                model.MirrorPairs.Add(genPair);
            }

            added.Add((insertAt, generated));
            PLDiag.AttrChange("MirrorGenerate", insertAt, generated.Name, "none", "mirror");
            return true;
        }

        // ================================================================
        // 共通ヘルパー
        // ================================================================

        /// <summary>選択中の描画メッシュを列挙する。未選択時は編集対象メッシュ単体。</summary>
        // ================================================================
        // ミラー実体化 / 解除の後処理
        // ================================================================
        // 頂点数が変わるので Unity Mesh を作り直す必要がある。
        // RebuildAdapter は ctx.UnityMesh を作らないため、ここで明示的に差し替える。
        private static void SyncMeshContextAfterMirrorEdit(MeshContext mc)
        {
            var mo = mc?.MeshObject;
            if (mo == null) return;

            mc.OriginalPositions = new Vector3[mo.VertexCount];
            for (int i = 0; i < mo.VertexCount; i++)
                mc.OriginalPositions[i] = mo.Vertices[i].Position;

            var newMesh = mo.ToUnityMesh();
            newMesh.name      = mo.Name;
            newMesh.hideFlags = HideFlags.HideAndDontSave;
            mc.ReplaceUnityMesh(newMesh);

            mc.InvalidateSymmetryCache();
        }
    }
}
