// PlayerCommandDispatcher.SpringBone.cs
// コマンドディスパッチャ：揺れもの（VRM SpringBone）のオーサリングとそのヘルパ。
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
        /// DispatchCore の分担：揺れもの（VRM SpringBone）のオーサリング。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchSpringBone(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── コライダーグループを足す
                case AddSpringBoneColliderGroupCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);
                    var sbgBefore = SpringBoneModelSettingsSnapshot.Capture(model);

                    int added = SpringBoneOps.EnsureGroup(model, c.GroupName);
                    if (added < 0) { Fail("コライダーグループを足せませんでした"); return true; }

                    RecordSpringBoneModelSettings(sbgBefore, model, "コライダーグループの追加");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── コライダーグループの名前を変える
                case RenameSpringBoneColliderGroupCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);
                    var sbrBefore = SpringBoneModelSettingsSnapshot.Capture(model);

                    if (!SpringBoneOps.RenameGroup(model, c.GroupIndex, c.NewName))
                    {
                        Fail("コライダーグループの名前を変えられませんでした");
                        return true;
                    }

                    RecordSpringBoneModelSettings(sbrBefore, model, "コライダーグループの名前変更");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── コライダーグループを消す
                //   参照索引の詰め直しを伴うので、モデル側と付帯側を
                //   同じ UndoGroup に積む（SpringBoneUndoRecords.cs の指示）。
                case DeleteSpringBoneColliderGroupCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);

                    var sbdAll     = AllIndices(model);
                    var sbdBeforeN = CaptureSpringBone(model, sbdAll);
                    var sbdBeforeM = SpringBoneModelSettingsSnapshot.Capture(model);

                    if (!SpringBoneOps.DeleteGroup(model, c.GroupIndex))
                    {
                        Fail("コライダーグループを消せませんでした");
                        return true;
                    }

                    if (_undoController != null)
                    {
                        _undoController.MeshListStack.BeginGroup("コライダーグループの削除");
                        RecordSpringBoneModelSettings(sbdBeforeM, model, "コライダーグループの削除");
                        RecordSpringBoneChange(model, sbdAll, sbdBeforeN, "参照索引の詰め直し");
                        _undoController.MeshListStack.EndGroup();
                    }

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── チェーンの起点にする
                case SetSpringBoneChainRootCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var sbcTargets = new List<int> { c.MasterIndex };
                    _undoController?.SetModelContext(model);
                    var sbcBefore = CaptureSpringBone(model, sbcTargets);

                    if (!SpringBoneOps.SetChainRoot(
                            model, c.MasterIndex, c.ChainName, c.CenterBoneName,
                            c.ColliderGroupIndices, out string sbcReason))
                    {
                        Fail(sbcReason);
                        return true;
                    }

                    RecordSpringBoneChange(model, sbcTargets, sbcBefore, "揺れチェーンの起点");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── チェーンの起点指定を外す
                case ClearSpringBoneChainRootCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0)
                    { Fail("対象が指定されていません"); return true; }

                    var sbxTargets = new List<int>(c.MasterIndices);
                    _undoController?.SetModelContext(model);
                    var sbxBefore = CaptureSpringBone(model, sbxTargets);

                    int sbxDone = SpringBoneOps.ClearChainRoot(model, sbxTargets);
                    if (sbxDone == 0) { Fail("起点になっているノードがありません"); return true; }

                    RecordSpringBoneChange(model, sbxTargets, sbxBefore, "揺れチェーンの起点解除");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── ジョイントを付ける
                case SetSpringBoneJointCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0)
                    { Fail("対象が指定されていません"); return true; }

                    var sbjTargets = new List<int>(c.MasterIndices);
                    _undoController?.SetModelContext(model);
                    var sbjBefore = CaptureSpringBone(model, sbjTargets);

                    int sbjDone = SpringBoneOps.SetJoint(
                        model, sbjTargets,
                        c.HitRadius, c.StiffnessForce, c.GravityPower, c.GravityDir, c.DragForce,
                        c.AngleLimitType, Quaternion.Euler(c.LimitRotationEuler),
                        c.Pitch, c.Yaw);

                    if (sbjDone == 0)
                    {
                        Fail("揺れジョイントを付けられる対象がありません"
                             + "（ボーンか非スキンドの描画オブジェクトのみ）");
                        return true;
                    }

                    RecordSpringBoneChange(model, sbjTargets, sbjBefore, $"揺れジョイント x{sbjDone}");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── ジョイントを外す
                case ClearSpringBoneJointCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0)
                    { Fail("対象が指定されていません"); return true; }

                    var sbkTargets = new List<int>(c.MasterIndices);
                    _undoController?.SetModelContext(model);
                    var sbkBefore = CaptureSpringBone(model, sbkTargets);

                    int sbkDone = SpringBoneOps.ClearJoint(model, sbkTargets);
                    if (sbkDone == 0) { Fail("揺れジョイントを持つノードがありません"); return true; }

                    RecordSpringBoneChange(model, sbkTargets, sbkBefore, $"揺れジョイントの解除 x{sbkDone}");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── 末端ボーンを足す
                //   ボーンが増えるのでリスト構造の変更として記録する。
                case AddSpringBoneTailBoneCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0)
                    { Fail("対象が指定されていません"); return true; }

                    _undoController?.SetModelContext(model);
                    var sbtlBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    int sbtlDone = 0;
                    string sbtlLastReason = "";

                    // 索引の大きい順に処理する。追加は末尾に積まれるので
                    // 途中で既存の索引はずれないが、対象の重複だけは避ける。
                    var sbtlTargets = new List<int>(new HashSet<int>(c.MasterIndices));
                    sbtlTargets.Sort();

                    foreach (int ti in sbtlTargets)
                    {
                        int made = SpringBoneOps.AddTailBone(
                            model, ti, c.TailLength, c.NameSuffix, c.AddJoint, out string r);
                        if (made >= 0) sbtlDone++;
                        else if (!string.IsNullOrEmpty(r)) sbtlLastReason = r;
                    }

                    if (sbtlDone == 0)
                    {
                        Fail(string.IsNullOrEmpty(sbtlLastReason)
                            ? "末端ボーンを足せる対象がありません"
                            : sbtlLastReason);
                        return true;
                    }

                    model.OnListChanged?.Invoke();
                    RecordMeshListSnapshot(sbtlBefore, model, $"末端ボーンの追加 x{sbtlDone}");

                    model.IsDirty = true;
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── ボーンの親を付け替える
                case SetBoneParentCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0)
                    { Fail("対象が指定されていません"); return true; }

                    _undoController?.SetModelContext(model);
                    var bpBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    int bpDone = 0;
                    foreach (int i in c.MasterIndices)
                    {
                        if (i < 0 || i >= model.MeshContextCount) continue;
                        var mc = model.GetMeshContext(i);
                        if (mc == null || mc.Type != MeshType.Bone) continue;
                        if (i == c.ParentMasterIndex) continue;

                        // ワールド位置を保つ。親のワールド行列の逆を掛けて
                        // 新しい親から見たローカル位置に置き直す。
                        Vector3 world = new Vector3(
                            mc.WorldMatrix.m03, mc.WorldMatrix.m13, mc.WorldMatrix.m23);

                        Vector3 parentWorld = Vector3.zero;
                        if (c.ParentMasterIndex >= 0 && c.ParentMasterIndex < model.MeshContextCount)
                        {
                            var pmc = model.GetMeshContext(c.ParentMasterIndex);
                            if (pmc != null)
                                parentWorld = new Vector3(
                                    pmc.WorldMatrix.m03, pmc.WorldMatrix.m13, pmc.WorldMatrix.m23);
                        }

                        mc.HierarchyParentIndex = c.ParentMasterIndex;
                        if (mc.MeshObject != null)
                            mc.MeshObject.HierarchyParentIndex = c.ParentMasterIndex;

                        if (mc.BoneTransform != null)
                            mc.BoneTransform.Position = world - parentWorld;

                        bpDone++;
                    }

                    if (bpDone == 0) { Fail("親を変えられるボーンがありません"); return true; }

                    model.ComputeWorldMatrices();
                    for (int k = 0; k < c.MasterIndices.Length; k++)
                    {
                        int i = c.MasterIndices[k];
                        if (i < 0 || i >= model.MeshContextCount) continue;
                        var mc = model.GetMeshContext(i);
                        if (mc != null) mc.BindPose = mc.WorldMatrix.inverse;
                    }

                    model.OnListChanged?.Invoke();
                    RecordMeshListSnapshot(bpBefore, model, $"ボーンの親を変える x{bpDone}");

                    model.IsDirty = true;
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── 揺れものの当たり判定を足す
                case AddSpringBoneColliderCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (!SpringBoneOps.IsCarrier(model, c.MasterIndex))
                    { Fail("当たり判定を付けられないオブジェクトです"); return true; }

                    var scTargets = new List<int> { c.MasterIndex };
                    _undoController?.SetModelContext(model);
                    var scBefore = CaptureSpringBone(model, scTargets);

                    var scMo = model.GetMeshContext(c.MasterIndex).MeshObject;
                    if (scMo.SpringBoneColliders == null)
                        scMo.SpringBoneColliders = new List<SpringBoneColliderData>();

                    var scGroups = new List<int>();
                    int scGroupCount = model.SpringBoneColliderGroupNames?.Count ?? 0;
                    if (c.GroupIndices != null)
                        foreach (int g in c.GroupIndices)
                            if (g >= 0 && g < scGroupCount && !scGroups.Contains(g)) scGroups.Add(g);

                    scMo.SpringBoneColliders.Add(new SpringBoneColliderData
                    {
                        Shape                 = c.Shape,
                        Offset                = c.Offset,
                        Radius                = Mathf.Max(0f, c.Radius),
                        Tail                  = c.Tail,
                        Normal                = c.Normal,
                        SpringBoneGroupIndices = scGroups,
                    });

                    RecordSpringBoneChange(model, scTargets, scBefore, "当たり判定の追加");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── 当たり判定を書き換える
                case UpdateSpringBoneColliderCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndex < 0 || c.MasterIndex >= model.MeshContextCount)
                    { Fail("対象のノードがありません"); return true; }

                    var suMo = model.GetMeshContext(c.MasterIndex)?.MeshObject;
                    var suList = suMo?.SpringBoneColliders;
                    if (suList == null || c.ColliderIndex < 0 || c.ColliderIndex >= suList.Count)
                    { Fail("その番号の当たり判定がありません"); return true; }

                    var suTargets = new List<int> { c.MasterIndex };
                    _undoController?.SetModelContext(model);
                    var suBefore = CaptureSpringBone(model, suTargets);

                    var suGroups = new List<int>();
                    int suGroupCount = model.SpringBoneColliderGroupNames?.Count ?? 0;
                    if (c.GroupIndices != null)
                        foreach (int g in c.GroupIndices)
                            if (g >= 0 && g < suGroupCount && !suGroups.Contains(g)) suGroups.Add(g);

                    var suTarget = suList[c.ColliderIndex];
                    suTarget.Shape                  = c.Shape;
                    suTarget.Offset                 = c.Offset;
                    suTarget.Radius                 = Mathf.Max(0f, c.Radius);
                    suTarget.Tail                   = c.Tail;
                    suTarget.Normal                 = c.Normal;
                    suTarget.SpringBoneGroupIndices = suGroups;

                    RecordSpringBoneChange(model, suTargets, suBefore, "当たり判定の変更");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── 当たり判定を消す
                //   同じボーンの後ろの当たり判定は 1 つずつ前へ詰まる。
                //   まとまり（グループ）側は名前しか持たないので触らない。
                case DeleteSpringBoneColliderCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndex < 0 || c.MasterIndex >= model.MeshContextCount)
                    { Fail("対象のノードがありません"); return true; }

                    var sdMo = model.GetMeshContext(c.MasterIndex)?.MeshObject;
                    var sdList = sdMo?.SpringBoneColliders;
                    if (sdList == null || c.ColliderIndex < 0 || c.ColliderIndex >= sdList.Count)
                    { Fail("その番号の当たり判定がありません"); return true; }

                    var sdTargets = new List<int> { c.MasterIndex };
                    _undoController?.SetModelContext(model);
                    var sdBefore = CaptureSpringBone(model, sdTargets);

                    sdList.RemoveAt(c.ColliderIndex);
                    if (sdList.Count == 0) sdMo.SpringBoneColliders = null;

                    RecordSpringBoneChange(model, sdTargets, sdBefore, "当たり判定の削除");

                    model.IsDirty = true;
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── はしごから揺れもの用のボーン鎖を置く
                //   ボーンが増えるのでリスト構造の変更として記録する。
                case PlaceSpringBoneLadderChainsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    if (c.SourceMasterIndex < 0 || c.SourceMasterIndex >= model.MeshContextCount)
                    { Fail("取り込み元の masterIndex が範囲外です"); return true; }

                    var sblSource = model.GetMeshContext(c.SourceMasterIndex);
                    if (sblSource?.MeshObject == null)
                    { Fail("取り込み元のメッシュが見つかりません"); return true; }

                    _undoController?.SetModelContext(model);
                    var sblBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    // ウェイトを塗るときは取り込み元メッシュの before を先に取る。
                    // ボーンの追加はこのメッシュを書き換えないので、Place の前で取ってよい。
                    // 記録の手順は ApplySkinWeightPerMesh と同じにする
                    //   SetMeshObjectFor → before → 適用 → ミラーへ写す → after → 記録。
                    // SetMeshObject(MeshObject,…) ではなく SetMeshObjectFor を使うこと
                    // （前者は書き込み先が先頭の選択メッシュになる）。
                    const string sblWeightLabel = "Paint Ladder Spring Weights";
                    MeshObjectSnapshot sblMeshBefore = null;
                    if (c.PaintWeights && _undoController != null)
                    {
                        _undoController.MeshUndoContext.ParentModelContext = model;
                        _undoController.SetMeshObjectFor(sblSource, sblSource.UnityMesh);
                        sblMeshBefore = _undoController.CaptureMeshObjectSnapshot();
                    }

                    // ミラー側にも鎖を作るときの写し行列。
                    // 取り込み元がミラーペアの実体側でなければ null（作らない）。
                    Matrix4x4? sblMirrorMatrix = null;
                    var sblPair = c.MakeMirrorChains ? model.GetMirrorPair(sblSource) : null;

                    if (sblPair != null && sblPair.Real == sblSource)
                    {
                        sblMirrorMatrix = Poly_Ling.Ops.MirrorBranchOps.MirrorMatrix(
                            sblSource.MirrorAxis, sblSource.MirrorDistance);
                    }
                    else if (c.MakeMirrorChains)
                    {
                        Debug.LogWarning(
                            "[PlaceSpringBoneLadderChains] 取り込み元がミラーペアの実体側ではないので、ミラー側の鎖は作りません");
                    }

                    // 鎖の根元を渡すとボーンを作らず位置だけ流し込む（冪等経路）。
                    // 照合に落ちたときは Place の中で新規作成へ切り替わり、
                    // 古い鎖はモデルに残る（Recreated が立つ）。
                    var sblResult = Poly_Ling.Tools.SpringBoneRig.SpringBoneLadderPlacer.Place(
                        model, sblSource, c.Method, c.SetName, c.Mode,
                        c.AttachMasterIndex, c.NamePrefix,
                        c.ReverseChain, c.AddTailBone, c.TailLength, c.PaintWeights,
                        c.RungStride, c.ChainStride, c.BundleMode,
                        c.ChainRootMasterIndices, sblMirrorMatrix);

                    if (sblResult.BoneCount == 0)
                    {
                        _undoController?.ClearTargetMeshContext();
                        Fail(string.IsNullOrEmpty(sblResult.Message)
                            ? "ボーンを作れませんでした"
                            : sblResult.Message);
                        return true;
                    }

                    // 左右の対を立て直したので、ミラーペアの対応表を作り直す。
                    // 作り直さないと BonePairMap は鎖を知らないままで、
                    // このあとの SyncSkinWeightToMirrors が何も写さない
                    // （MirrorPair.BuildBonePairMap は MirrorBoneIndex を読むだけ）。
                    if (sblPair != null && sblResult.MirrorChains.Count > 0)
                        sblPair.Build(model.MeshContextList);

                    var sblMirrors = new List<MeshContext>();
                    if (sblMeshBefore != null && sblResult.WeightedVertexCount > 0)
                    {
                        // ミラー側へ写してから実体側の after を取る。後に回すと
                        // Redo で実体側頂点の MirrorBoneWeight が古い値に戻る。
                        SyncSkinWeightToMirrors(model, sblSource, sblMirrors, sblWeightLabel);

                        _undoController.SetMeshObjectFor(sblSource, sblSource.UnityMesh);
                        var sblMeshAfter = _undoController.CaptureMeshObjectSnapshot();
                        _commandQueue?.Enqueue(new RecordTopologyChangeCommand(
                            _undoController, sblMeshBefore, sblMeshAfter, sblWeightLabel));
                    }
                    _undoController?.ClearTargetMeshContext();

                    model.OnListChanged?.Invoke();
                    RecordMeshListSnapshot(sblBefore, model, sblResult.Message);

                    // グループの出力は「鎖の根元ボーン」だけ。節点と tail は出力ではない。
                    // 作り直しではこの ObjectId 列が ChainRootMasterIndices へ書き戻され、
                    // ボーンを作らず位置だけ流し込む経路に入る。
                    if (c.KeepAsGroup)
                    {
                        // 並びは「実体側 N 本 → ミラー側 N 本」。作り直しでは
                        // この順のまま ChainRootMasterIndices へ書き戻される。
                        var sblRootIds = new List<ulong>(
                            sblResult.Chains.Count + sblResult.MirrorChains.Count);

                        foreach (var sblChain in sblResult.Chains)
                        {
                            if (sblChain == null || sblChain.Count == 0) continue;
                            ulong rid = model.GetMeshContext(sblChain[0])?.ObjectId ?? 0UL;
                            if (rid != 0UL) sblRootIds.Add(rid);
                        }

                        foreach (var sblChain in sblResult.MirrorChains)
                        {
                            if (sblChain == null || sblChain.Count == 0) continue;
                            ulong rid = model.GetMeshContext(sblChain[0])?.ObjectId ?? 0UL;
                            if (rid != 0UL) sblRootIds.Add(rid);
                        }

                        if (sblRootIds.Count > 0)
                            CaptureObjectGroup(c, null, -1, sblRootIds);
                        else
                            Debug.LogWarning("[ObjectGroup] 鎖の根元を引けないためグループを作りませんでした");
                    }

                    // 書き換えたウェイトを送る。ボーン追加ぶんの EnterTopologyChanged とは
                    // 別に、対象を明示して部分転送する（ミラー側は別メッシュなので個別に要る）。
                    if (sblResult.WeightedVertexCount > 0)
                    {
                        _viewportManager.EnterVertexAttributesChanged(
                            project, sblSource, weights: true, uvs: false);
                        foreach (var sblMirror in sblMirrors)
                            _viewportManager.EnterVertexAttributesChanged(
                                project, sblMirror, weights: true, uvs: false);
                    }

                    Debug.Log("[PlaceSpringBoneLadderChains] " + sblResult.Message);

                    model.IsDirty = true;
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── 揺れもの用のボーン鎖を置く
                //   ボーンが増えるのでリスト構造の変更として記録する。
                case PlaceSpringBoneChainsCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    _undoController?.SetModelContext(model);
                    var sbpBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var sbpResult = Poly_Ling.Tools.SpringBoneRig.SpringBoneChainPlacer.Place(
                        model, c.Layout, c.AttachMasterIndex, c.NamePrefix,
                        c.OriginMasterIndex,
                        c.ChainCount, c.Segments,
                        c.TopRadius, c.BottomRadius, c.Height, c.StartAngleDeg,
                        c.Profile, c.AddTailBone, c.TailLength);

                    if (sbpResult.BoneCount == 0)
                    {
                        Fail(string.IsNullOrEmpty(sbpResult.Message)
                            ? "ボーンを作れませんでした"
                            : sbpResult.Message);
                        return true;
                    }

                    model.OnListChanged?.Invoke();
                    RecordMeshListSnapshot(sbpBefore, model, sbpResult.Message);

                    Debug.Log("[PlaceSpringBoneChains] " + sbpResult.Message);

                    model.IsDirty = true;
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── 階層を辿ってノード列を選ぶ
                case SelectBoneChainCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var sbchain = SpringBoneOps.CollectChain(model, c.RootMasterIndex, c.Walk);
                    if (sbchain.Count == 0)
                    {
                        Fail("起点から辿れるノードがありません"
                             + "（ボーンか非スキンドの描画オブジェクトのみ）");
                        return true;
                    }

                    ApplyBoneSelection(project, model, sbchain, c.Additive);

                    var sbchainIds = new ulong[sbchain.Count];
                    for (int i = 0; i < sbchain.Count; i++)
                        sbchainIds[i] = model.GetMeshContext(sbchain[i])?.ObjectId ?? 0UL;

                    ReportData(
                        CommandDataJson.New()
                            .Int("nodes",           sbchain.Count)
                            .Int("rootMasterIndex", c.RootMasterIndex)
                            .Text("walk",           c.Walk.ToString())
                            .Flag("additive",       c.Additive)
                            .Build(),
                        sbchain.ToArray(), sbchainIds);
                    return true;
                }

                // ── 頂点に効いているボーンを選ぶ
                case SelectBonesByVertexWeightCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0)
                    { Fail("対象が指定されていません"); return true; }

                    var sbw = SpringBoneOps.CollectBonesByVertexWeight(
                        model, c.MasterIndices, c.MinWeight);
                    if (sbw.Count == 0) { Fail("ウェイトの掛かったボーンがありません"); return true; }

                    ApplyBoneSelection(project, model, sbw, c.Additive);
                    return true;
                }

                // ── Tポーズ変換
                case ApplyTPoseCommand _:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var mapping = model.HumanoidMapping;
                    if (mapping == null || mapping.IsEmpty) { Fail("ボーン対応表が空です"); return true; }

                    // SetModelContext（MeshListStack の context を現在のモデルに設定）
                    _undoController?.SetModelContext(model);

                    var beforeState    = new TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, beforeState);
                    var oldTPoseBackup = model.TPoseBackup;

                    var backup = new TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.ConvertToTPose(model.MeshContextList, mapping, backup);
                    model.TPoseBackup = backup;

                    var afterState = new TPoseBackup();
                    Poly_Ling.Ops.TPoseConverter.CaptureBackup(model.MeshContextList, afterState);

                    if (_undoController != null)
                    {
                        var record = new TPoseUndoRecord(beforeState, afterState, oldTPoseBackup, backup, "Apply T-Pose");
                        {
                            string __dbgDesc = "Apply T-Pose";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    model.IsDirty = true;
                    model.OnListChanged?.Invoke();
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// MeshContextList の丸ごとスナップショットで Undo を 1 件記録する。
        /// before は操作前に CaptureList で取っておくこと。
        /// </summary>
        // ================================================================
        // 揺れもの（VRM SpringBone）用のヘルパ
        // ================================================================

        /// <summary>モデルの全索引。グループ削除のように全ノードへ波及する操作で使う。</summary>
        private static List<int> AllIndices(ModelContext model)
        {
            int n = model?.MeshContextCount ?? 0;
            var list = new List<int>(n);
            for (int i = 0; i < n; i++) list.Add(i);
            return list;
        }

        /// <summary>指定索引の揺れ付帯データを控える。indices と同じ並びで返す。</summary>
        private static List<SpringBoneDataSnapshot> CaptureSpringBone(
            ModelContext model, IReadOnlyList<int> indices)
        {
            var list = new List<SpringBoneDataSnapshot>(indices?.Count ?? 0);
            if (model == null || indices == null) return list;

            foreach (int i in indices)
            {
                var mc = (i >= 0 && i < model.MeshContextCount) ? model.GetMeshContext(i) : null;
                list.Add(SpringBoneDataSnapshot.Capture(mc));
            }
            return list;
        }

        /// <summary>
        /// 揺れ付帯データの変更を Undo に積む。
        /// before は CaptureSpringBone の戻り値で、indices と同じ並びであること。
        /// </summary>
        private void RecordSpringBoneChange(
            ModelContext model, IReadOnlyList<int> indices,
            List<SpringBoneDataSnapshot> before, string desc)
        {
            if (_undoController == null || model == null || indices == null || before == null) return;

            var record = new MultiSpringBoneChangeRecord();

            for (int k = 0; k < indices.Count && k < before.Count; k++)
            {
                int i = indices[k];
                if (i < 0 || i >= model.MeshContextCount) continue;

                record.Entries.Add(new MultiSpringBoneChangeRecord.Entry
                {
                    MasterIndex = i,
                    OldSnapshot = before[k],
                    NewSnapshot = SpringBoneDataSnapshot.Capture(model.GetMeshContext(i)),
                });
            }

            if (record.Entries.Count == 0) return;

            PLDiag.UndoRecord("MeshList", desc, record);
            _undoController.MeshListStack.Record(record, desc);
            _undoController.FocusMeshList();
        }

        /// <summary>
        /// 可動域の変更前スナップショットを取る。
        /// 並びは indices と 1 対 1。RecordHumanLimitChange へそのまま渡すこと。
        /// </summary>
        private static List<HumanLimitSnapshot> CaptureHumanLimit(
            ModelContext model, IReadOnlyList<int> indices)
        {
            var list = new List<HumanLimitSnapshot>(indices?.Count ?? 0);
            if (model == null || indices == null) return list;

            foreach (int i in indices)
            {
                var mc = (i >= 0 && i < model.MeshContextCount) ? model.GetMeshContext(i) : null;
                list.Add(HumanLimitSnapshot.Capture(mc));
            }
            return list;
        }

        /// <summary>
        /// 可動域の変更を Undo に積む。
        /// before は CaptureHumanLimit の戻り値で、indices と同じ並びであること。
        /// </summary>
        private void RecordHumanLimitChange(
            ModelContext model, IReadOnlyList<int> indices,
            List<HumanLimitSnapshot> before, string desc)
        {
            if (_undoController == null || model == null || indices == null || before == null) return;

            var record = new MultiHumanLimitChangeRecord();

            for (int k = 0; k < indices.Count && k < before.Count; k++)
            {
                int i = indices[k];
                if (i < 0 || i >= model.MeshContextCount) continue;

                record.Entries.Add(new MultiHumanLimitChangeRecord.Entry
                {
                    MasterIndex = i,
                    OldSnapshot = before[k],
                    NewSnapshot = HumanLimitSnapshot.Capture(model.GetMeshContext(i)),
                });
            }

            if (record.Entries.Count == 0) return;

            PLDiag.UndoRecord("MeshList", desc, record);
            _undoController.MeshListStack.Record(record, desc);
            _undoController.FocusMeshList();
        }

        /// <summary>Avatar リターゲット設定の変更を Undo に積む。</summary>
        private void RecordAvatarRetarget(
            AvatarRetargetSnapshot before, ModelContext model, string desc)
        {
            if (_undoController == null || model == null || before == null) return;

            var record = new AvatarRetargetChangeRecord(
                before, AvatarRetargetSnapshot.Capture(model));

            PLDiag.UndoRecord("MeshList", desc, record);
            _undoController.MeshListStack.Record(record, desc);
            _undoController.FocusMeshList();
        }

        /// <summary>モデルレベルの VRM 設定（メタ情報・視線）の変更を Undo に積む。</summary>
        private void RecordVrmModelSettings(
            VrmModelSettingsSnapshot before, ModelContext model, string desc)
        {
            if (_undoController == null || model == null || before == null) return;

            var record = new VrmModelSettingsRecord(
                before, VrmModelSettingsSnapshot.Capture(model));

            PLDiag.UndoRecord("MeshList", desc, record);
            _undoController.MeshListStack.Record(record, desc);
            _undoController.FocusMeshList();
        }

        /// <summary>
        /// 一人称指定の変更前の値を取る。並びは indices と 1 対 1。
        /// </summary>
        private static List<VrmFirstPersonType> CaptureVrmFirstPerson(
            ModelContext model, IReadOnlyList<int> indices)
        {
            var list = new List<VrmFirstPersonType>(indices?.Count ?? 0);
            if (model == null || indices == null) return list;

            foreach (int i in indices)
            {
                var mo = (i >= 0 && i < model.MeshContextCount)
                    ? model.GetMeshContext(i)?.MeshObject : null;
                list.Add(mo?.VrmFirstPerson ?? VrmFirstPersonType.Auto);
            }
            return list;
        }

        /// <summary>
        /// 一人称指定の変更を Undo に積む。
        /// before は CaptureVrmFirstPerson の戻り値で、indices と同じ並びであること。
        /// </summary>
        private void RecordVrmFirstPersonChange(
            ModelContext model, IReadOnlyList<int> indices,
            List<VrmFirstPersonType> before, string desc)
        {
            if (_undoController == null || model == null || indices == null || before == null) return;

            var record = new MultiVrmFirstPersonChangeRecord();

            for (int k = 0; k < indices.Count && k < before.Count; k++)
            {
                int i = indices[k];
                if (i < 0 || i >= model.MeshContextCount) continue;

                var mo = model.GetMeshContext(i)?.MeshObject;
                if (mo == null) continue;

                record.Entries.Add(new MultiVrmFirstPersonChangeRecord.Entry
                {
                    MasterIndex = i,
                    OldType     = before[k],
                    NewType     = mo.VrmFirstPerson,
                });
            }

            if (record.Entries.Count == 0) return;

            PLDiag.UndoRecord("MeshList", desc, record);
            _undoController.MeshListStack.Record(record, desc);
            _undoController.FocusMeshList();
        }

        /// <summary>モデルレベルの揺れ設定（グループ名・評価設定）の変更を Undo に積む。</summary>
        private void RecordSpringBoneModelSettings(
            SpringBoneModelSettingsSnapshot before, ModelContext model, string desc)
        {
            if (_undoController == null || model == null || before == null) return;

            var record = new SpringBoneModelSettingsRecord(
                before, SpringBoneModelSettingsSnapshot.Capture(model));

            PLDiag.UndoRecord("MeshList", desc, record);
            _undoController.MeshListStack.Record(record, desc);
            _undoController.FocusMeshList();
        }

        /// <summary>
        /// ボーン選択を差し替える。SelectMeshCommand の Bone 分岐と同じ手順を通す。
        /// 選択 Undo は 3 カテゴリまとめて CaptureAllSelectedIndices で記録する。
        /// </summary>
        private void ApplyBoneSelection(
            ProjectContext project, ModelContext model, List<int> indices, bool additive)
        {
            var oldSelected = model.CaptureAllSelectedIndices();

            if (!additive) model.ClearBoneSelection();
            foreach (int i in indices) model.AddToBoneSelection(i);

            _viewportManager.EnterSelectionChanged(project);

            var newSelected = model.CaptureAllSelectedIndices();
            PLDiag.Cmd($"SelectBoneChain old={PLDiag.Ids(oldSelected)} new={PLDiag.Ids(newSelected)}");
            _undoController?.SetModelContext(model);
            _undoController?.RecordMeshSelectionChange(oldSelected, newSelected);

            _notifyPanels(ChangeKind.Selection);
        }

        private void RecordMeshListSnapshot(
            List<MeshContext> before, ModelContext model, string desc)
        {
            if (_undoController == null || before == null || model == null) return;

            var after  = MeshFilterToSkinnedRecord.CaptureList(model);
            var record = new MeshFilterToSkinnedRecord { BeforeList = before, AfterList = after };

            PLDiag.UndoRecord("MeshList", desc, record);
            _undoController.MeshListStack.Record(record, desc);
            _undoController.FocusMeshList();
        }

        /// <summary>
        /// 実体側がスキンドなら、ミラー側メッシュ本体のウェイトを左右対のボーンへ写す。
        ///
        /// 【なぜ Build() だけでは足りないか】
        ///   MirrorBranchOps.BuildMirroredMeshObject は、実体側頂点の MirrorBoneWeight が
        ///   あればそれを、無ければ実体側の BoneWeight をそのままミラー側へ複製する。
        ///   一方 MirrorPair.Build() の中で走る ApplyMirrorBoneWeights が書くのは
        ///   「実体側頂点の MirrorBoneWeight」だけで、ミラーメッシュ本体の BoneWeight は
        ///   触らない。順序として複製が先なので、初回生成時のミラー側は実体側と同じ
        ///   ボーンを指したままになり、右のメッシュが左のボーンで動く。
        ///
        ///   SyncBoneWeights() は BonePairMap を通した値をミラーメッシュ本体へ書く。
        ///   Build() が対応表を作り終えたこの時点で呼ぶ。
        ///
        /// 【対応表が空のとき】
        ///   BonePairMap は MirrorBoneIndex からしか作らない。全ボーンが -1 の
        ///   モデル（PMX インポート直後など）では写像できるスロットが 1 つも無く、
        ///   SyncBoneWeights は何も書かずに終わる。誤ったボーン番号を残すより良い。
        ///   左右対応は ResolveMirrorBoneIndexCommand で先に埋めること。
        /// </summary>
        private static void SyncMirrorWeightsIfSkinned(MirrorPair pair, MeshContext realCtx)
        {
            if (pair == null || realCtx == null) return;
            if (!realCtx.IsSkinned) return;
            pair.SyncBoneWeights();
        }

        /// <summary>モデル内に同名のメッシュが既に居るか（ミラー命名の衝突判定用）。</summary>
        private static bool ExistsMeshName(ModelContext model, string name)
        {
            if (model == null || string.IsNullOrEmpty(name)) return false;

            for (int i = 0; i < model.MeshContextCount; i++)
                if (string.Equals(model.GetMeshContext(i)?.Name, name, System.StringComparison.Ordinal))
                    return true;

            return false;
        }
    }
}
