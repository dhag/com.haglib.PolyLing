// PlayerCommandDispatcher.DeformSkin.cs
// コマンドディスパッチャ：特殊な変形（シュリンカー・法線移植・TPS・MediaPipe）とスキン関連。
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
        /// DispatchCore の分担：シュリンカー・法線移植・TPSモーフ・UV 変更・スキンウェイト・MeshFilter→Skinned・種別変換・左右対応・MediaPipe。
        /// 該当するコマンドなら処理して true を返す。区画の本文は元の switch のままで、
        /// DispatchCore を抜けていた return; だけを return true; にしてある。
        /// </summary>
        private bool DispatchDeformSkin(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                // ── 法線移植適用
                case ApplyNormalTransplantCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    // プリズムの構築に使うワールド座標をこの時点で1回だけ更新する。
                    _viewportManager.UpdateTransform();

                    var ntSamples = NormalTransplantOperation.ComputeSamples(
                        model, c.BeforeMasterIndex, c.AfterMasterIndex, c.TargetMasterIndices,
                        c.Spherical
                            ? NormalPrismSolver.TriangleBlendMode.Spherical
                            : NormalPrismSolver.TriangleBlendMode.Linear,
                        c.AllowNearest,
                        mc => _viewportManager.TryGetMeshWorldPositions(model, mc, out var w) ? w : null,
                        out string ntError);

                    if (ntSamples == null)
                    {
                        Debug.LogWarning($"[NormalTransplant] 法線を算出できません: {ntError}");
                        return true;
                    }

                    var ntCtx = BuildSkinWeightToolCtx(model);

                    // パネル側はコマンド送信前にプレビューを破棄して元法線へ戻している。
                    var ntPreview = new NormalTransplantPreviewState();
                    if (!ntPreview.Start(model, ntSamples)) { Fail("法線移植を開始できませんでした"); return true; }

                    int ntApplied = NormalTransplantOperation.Apply(
                        model, ntPreview, c.Strength, ntCtx);
                    if (ntApplied <= 0) { Fail("法線を移植できる頂点がありません"); return true; }

                    // ミラー再ベイクで UnityMesh を作り直し得るため、再構築で揃える。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── TPSモーフ適用
                case ApplyThinPlateMorphCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var tpsLocal = ThinPlateMorphOperation.ComputeWarpedLocalPositions(
                        model, c.BeforeMasterIndex, c.AfterMasterIndex, c.TargetMasterIndex,
                        c.Lambda, c.SelectedControlPointsOnly,
                        out var tpsControlPoints, out string tpsError);

                    if (tpsLocal == null)
                    {
                        Debug.LogWarning($"[ThinPlateMorph] 変形を算出できません: {tpsError}");
                        return true;
                    }

                    if (tpsControlPoints != null && tpsControlPoints.DuplicateCount > 0)
                    {
                        Debug.Log($"[ThinPlateMorph] 位置が重複する制御点 {tpsControlPoints.DuplicateCount} 点を除きました" +
                                  $"（{tpsControlPoints.Count} 点を使用）");
                    }

                    var tpsCtx = BuildSkinWeightToolCtx(model);
                    int tpsNewIndex = ThinPlateMorphOperation.ApplyAsNewObject(
                        model, c.TargetMasterIndex, tpsLocal, c.RecalculateNormals, tpsCtx);

                    if (tpsNewIndex < 0)
                    {
                        Debug.LogWarning("[ThinPlateMorph] 結果オブジェクトを作成できませんでした");
                        return true;
                    }

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── TPSモーフ 算出済み結果の適用（局所モードのバックグラウンド計算の受け口）
                case ApplyThinPlateMorphResultCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.LocalPositions == null)
                    {
                        Debug.LogWarning("[ThinPlateMorph] 変形結果が空です");
                        return true;
                    }

                    var tpsrCtx = BuildSkinWeightToolCtx(model);
                    int tpsrNewIndex = ThinPlateMorphOperation.ApplyAsNewObject(
                        model, c.TargetMasterIndex, c.LocalPositions, c.RecalculateNormals, tpsrCtx);

                    if (tpsrNewIndex < 0)
                    {
                        Debug.LogWarning("[ThinPlateMorph] 結果オブジェクトを作成できませんでした");
                        return true;
                    }

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }

                // ── UV 変更（移動・一括変換）
                case ApplyUVChangesCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var uvMc = model.GetMeshContext(c.MasterIndex);
                    if (uvMc?.MeshObject == null) { Fail("対象メッシュがありません"); return true; }

                    // UndoController にターゲットメッシュを設定
                    if (_undoController != null)
                    {
                        _undoController.SetMeshObject(uvMc.MeshObject, uvMc.UnityMesh);
                        _undoController.MeshUndoContext.ParentModelContext = model;
                    }

                    // before スナップショット（AfterUVs を MeshObject に書き込む前に取得）
                    var before = _undoController?.CaptureMeshObjectSnapshotOf(uvMc);

                    // AfterUVs を MeshObject に適用
                    var mo = uvMc.MeshObject;
                    for (int i = 0; i < c.VertexIndices.Length; i++)
                    {
                        int vi = c.VertexIndices[i];
                        int ui = c.UVIndices[i];
                        if (vi < 0 || vi >= mo.VertexCount) continue;
                        var vx = mo.Vertices[vi];
                        if (ui >= 0 && ui < vx.UVs.Count)
                            vx.UVs[ui] = c.AfterUVs[i];
                    }

                    // after スナップショット → VertexEditStack に記録
                    if (_undoController != null && before != null)
                    {
                        var after = _undoController.CaptureMeshObjectSnapshotOf(uvMc);
                        _commandQueue?.Enqueue(
                            new RecordTopologyChangeCommand(
                                _undoController, before, after, c.OperationName));
                    }

                    // UnityMesh + GPU 更新
                    // 面の描画は ctx.UnityMesh の UV をそのまま使う。頂点移動用の経路
                    // （EnterVerticesMoved）は位置しか送らないので、UV が表示に反映されなかった。
                    // xyzToUv（PolyLingCore_UvHandlers.HandleXyzToUv）と同じく MeshObject から
                    // 作った一時メッシュを UnityMesh へその場で書き込む。差し替えにしないのは、
                    // Undo 側（SetMeshObject）が書き込み前の UnityMesh の参照を持っているため。
                    // 位置はローカルのままになるが、EnterTopologyChanged の RebuildAdapter 末尾の
                    // WritebackTransformedVertices がワールド座標で上書きする。
                    if (uvMc.UnityMesh != null)
                    {
                        var uvRebuilt = mo.ToUnityMesh(model.MaterialCount);
                        try
                        {
                            var um = uvMc.UnityMesh;
                            // ToUnityMesh が作るもの（位置・UV・法線・ボーンウェイト・面）をすべて写す。
                            // バインドポーズは ToUnityMesh が作らないので、元の値を残す。
                            var keepBindposes = um.bindposes;
                            um.Clear();
                            um.indexFormat  = uvRebuilt.indexFormat;
                            um.vertices     = uvRebuilt.vertices;
                            um.normals      = uvRebuilt.normals;
                            um.uv           = uvRebuilt.uv;
                            var rebuiltWeights = uvRebuilt.boneWeights;
                            if (rebuiltWeights != null && rebuiltWeights.Length == uvRebuilt.vertexCount)
                                um.boneWeights = rebuiltWeights;
                            if (keepBindposes != null && keepBindposes.Length > 0)
                                um.bindposes = keepBindposes;
                            um.subMeshCount = uvRebuilt.subMeshCount;
                            for (int s = 0; s < uvRebuilt.subMeshCount; s++)
                                um.SetTriangles(uvRebuilt.GetTriangles(s), s);
                            um.RecalculateBounds();
                        }
                        finally
                        {
                            UnityEngine.Object.Destroy(uvRebuilt);
                        }
                    }

                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── スキンウェイト Flood / Normalize / Prune
                //    いずれも対象は選択中の描画オブジェクト全件。
                //    メッシュごとに UndoController を差し替えて before/after を取る
                //    （SetFaceHiddenCommand / SetSkinWeightNumericCommand と同型）。
                case FloodSkinWeightCommand c:
                    ApplySkinWeightPerMesh(project, model, "Flood Skin Weight",
                        mc => SkinWeightOperations.ApplyFloodToMesh(
                            mc, c.TargetBoneMaster, c.PaintMode, c.WeightValue, c.Strength));
                    return true;

                // ── 写し元メッシュの最も近い面の点からウェイトを写す
                //    対象・Undo・ミラー同期は Flood と同じ共通経路（ApplySkinWeightPerMesh）。
                case CopySkinWeightsFromNearestCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    int src = c.SourceMasterIndex;
                    var srcMc = (src >= 0 && src < model.MeshContextCount) ? model.GetMeshContext(src) : null;
                    if (srcMc?.MeshObject == null) { Fail($"写し元 {src} がありません"); return true; }

                    var cwTargets = SkinWeightOperations.CollectTargetMeshContexts(model);
                    if (cwTargets.Count == 0) { Fail("対象の描画オブジェクトがありません。"); return true; }

                    var cwCheck = new List<MeshContext>(cwTargets) { srcMc };
                    var cwNonSkinned = SkinWeightVolumeOps.FindNonSkinned(cwCheck);
                    if (cwNonSkinned.Count > 0)
                    {
                        Fail("スキンドでない描画オブジェクトが含まれています: " + string.Join(" / ", cwNonSkinned));
                        return true;
                    }

                    int cwVerts = 0, cwMeshes = 0;
                    ApplySkinWeightPerMesh(project, model, "Copy Skin Weights From Nearest", mc =>
                    {
                        if (mc == srcMc) return 0;
                        int n = Poly_Ling.Ops.SkinWeightTransferOps.CopyFromNearest(srcMc.MeshObject, mc.MeshObject);
                        if (n > 0) { cwVerts += n; cwMeshes++; }
                        return n;
                    });

                    ReportData(CommandDataJson.New()
                        .Int("vertices", cwVerts)
                        .Int("meshes",   cwMeshes)
                        .Build());
                    return true;
                }

                case NormalizeSkinWeightCommand _:
                    ApplySkinWeightPerMesh(project, model, "Normalize Skin Weights",
                        mc => SkinWeightOperations.ApplyNormalizeToMesh(mc));
                    return true;

                case PruneSkinWeightCommand c:
                    ApplySkinWeightPerMesh(project, model, "Prune Skin Weights",
                        mc => SkinWeightOperations.ApplyPruneToMesh(mc, c.Threshold));
                    return true;

                // ── スキンウェイト 数値設定（最大 4 ボーンを直接上書き）
                case SetSkinWeightNumericCommand c:
                    ApplySkinWeightPerMesh(project, model, "Set Skin Weight (Numeric)",
                        mc => SkinWeightOperations.ApplyNumericToMesh(mc, c.BoneMasters, c.Weights));
                    return true;

                // ── 対象メッシュ全件の全頂点を正規化
                //    SetSkinWeightNumericCommand と同じくメッシュごとに Undo を取る。
                case NormalizeAllSkinWeightsCommand:
                    ApplySkinWeightPerMesh(project, model, "Normalize All Skin Weights",
                        mc => SkinWeightOperations.NormalizeAllInMesh(mc));
                    return true;

                // ── スキンW範囲塗り（円筒・球の範囲へ親・自ボーンを直線補間で配分）
                //    対象・Undo・ミラー同期は Flood と同じ共通経路（ApplySkinWeightPerMesh）。
                //    非スキンドは頂点がローカル空間で計算の前提が崩れるため、適用前に全体を弾く。
                case SkinWeightSegmentFillCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    if (!SkinWeightVolumeOps.TryBuildSegment(model, c.JointBone, c.ParentBone, c.Radius,
                            out var sfFrame, out string sfErr))
                    { Fail(sfErr); return true; }

                    var sfTargets = SkinWeightOperations.CollectTargetMeshContexts(model);
                    if (sfTargets.Count == 0) { Fail("対象の描画オブジェクトがありません。"); return true; }

                    var sfNonSkinned = SkinWeightVolumeOps.FindNonSkinned(sfTargets);
                    if (sfNonSkinned.Count > 0)
                    {
                        Fail("スキンドでない描画オブジェクトが含まれています: "
                             + string.Join(" / ", sfNonSkinned));
                        return true;
                    }

                    int sfVerts = 0;
                    var sfChanged = new List<MeshContext>();
                    ApplySkinWeightPerMesh(project, model, "Skin Weight Segment Fill", mc =>
                    {
                        int n = SkinWeightVolumeOps.ApplySegmentFillToMesh(mc, sfFrame, c.SelectedOnly);
                        if (n > 0) { sfVerts += n; sfChanged.Add(mc); }
                        return n;
                    });

                    var sfIdx = new int[sfChanged.Count];
                    var sfIds = new ulong[sfChanged.Count];
                    for (int i = 0; i < sfChanged.Count; i++)
                    {
                        sfIdx[i] = model.MeshContextList.IndexOf(sfChanged[i]);
                        sfIds[i] = sfChanged[i].ObjectId;
                    }

                    ReportData(CommandDataJson.New()
                        .Int("vertices",   sfVerts)
                        .Int("meshes",     sfChanged.Count)
                        .Int("parentBone", sfFrame.ParentBone)
                        .Build(), sfIdx, sfIds);
                    return true;
                }

                // ── 骨の列に沿った関節ぼかし（太さで幅／関節から関節まで滑らか）
                //    対象・Undo・ミラー同期は Flood と同じ共通経路（ApplySkinWeightPerMesh）。
                case SkinWeightChainBlendCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (!SkinWeightChainOps.ResolveBones(model, c.BoneNames, out var cbBones, out string cbErr))
                    { Fail(cbErr); return true; }

                    var cbTargets = SkinWeightOperations.CollectTargetMeshContexts(model);
                    if (cbTargets.Count == 0) { Fail("対象の描画オブジェクトがありません。"); return true; }
                    var cbNonSkinned = SkinWeightVolumeOps.FindNonSkinned(cbTargets);
                    if (cbNonSkinned.Count > 0)
                    {
                        Fail("スキンドでない描画オブジェクトが含まれています: " + string.Join(" / ", cbNonSkinned));
                        return true;
                    }

                    if (!SkinWeightChainOps.TryBuildFrame(model, cbBones, cbTargets,
                            c.FullSmooth, c.WidthFactor, c.SliceRatio, out var cbFrame, out cbErr))
                    { Fail(cbErr); return true; }

                    int cbVerts = 0;
                    var cbChanged = new List<MeshContext>();
                    ApplySkinWeightPerMesh(project, model, "Skin Weight Chain Blend", mc =>
                    {
                        int n = SkinWeightChainOps.ApplyToMesh(mc, cbFrame);
                        if (n > 0) { cbVerts += n; cbChanged.Add(mc); }
                        return n;
                    });

                    var cbIdx = new int[cbChanged.Count];
                    var cbIds = new ulong[cbChanged.Count];
                    for (int i = 0; i < cbChanged.Count; i++)
                    {
                        cbIdx[i] = model.MeshContextList.IndexOf(cbChanged[i]);
                        cbIds[i] = cbChanged[i].ObjectId;
                    }

                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    ReportData(CommandDataJson.New()
                        .Int("vertices",    cbVerts)
                        .Int("meshes",      cbChanged.Count)
                        .Text("radii",      string.Join(",", cbFrame.Radii.Select(v => v.ToString("0.#####", inv))))
                        .Text("halfWidths", string.Join(",", cbFrame.HalfWidths.Select(v => v.ToString("0.#####", inv))))
                        .Build(), cbIdx, cbIds);
                    return true;
                }

                // ── ボーンヒートによる自動ウェイト
                //    全対象を先に計算し、1 件でも解けなければ何も書かない。
                //    書き込み・Undo・ミラー同期は共通経路（ApplySkinWeightPerMesh）。
                case SkinWeightAutoHeatCommand c:
                {
                    // パネルの結果表示用（LastSkinWeightAutoHeatResult）。失敗も成功もここへ残す。
                    var ahRun = new HeatRunResult();
                    LastSkinWeightAutoHeatResult = ahRun;
                    void AhFail(string reason) { ahRun.Success = false; ahRun.Reason = reason; Fail(reason); }

                    if (model == null) { AhFail("モデルがありません。"); return true; }

                    int[] ahBones;
                    string ahErr;
                    if (c.BoneNames != null && c.BoneNames.Length > 0)
                    {
                        if (!SkinWeightChainOps.ResolveBones(model, c.BoneNames, out ahBones, out ahErr))
                        { AhFail(ahErr); return true; }
                    }
                    else if (!SkinWeightHeatOps.TryResolveScope(model, c.BoneScope, model.SelectedBoneIndices,
                                 out ahBones, out ahErr))
                    { AhFail(ahErr); return true; }

                    if (!SkinWeightHeatOps.TryBuildSources(model, ahBones, out var ahSources, out ahErr))
                    { AhFail(ahErr); return true; }

                    var ahTargets = SkinWeightOperations.CollectTargetMeshContexts(model);
                    if (ahTargets.Count == 0) { AhFail("対象の描画オブジェクトがありません。"); return true; }
                    var ahNonSkinned = SkinWeightVolumeOps.FindNonSkinned(ahTargets);
                    if (ahNonSkinned.Count > 0)
                    {
                        AhFail("スキンドでない描画オブジェクトが含まれています: " + string.Join(" / ", ahNonSkinned));
                        return true;
                    }

                    var ahResults = new Dictionary<MeshContext, HeatWeightResult>();
                    int ahUnreached = 0;
                    foreach (var mc in ahTargets)
                    {
                        if (!SkinWeightHeatOps.TryCompute(mc, ahSources, c.SelectedOnly, out var r, out ahErr))
                        { AhFail(ahErr); return true; }
                        ahResults[mc] = r;
                        ahUnreached += r.Unreached;
                    }

                    int ahVerts = 0;
                    var ahChanged = new List<MeshContext>();
                    ApplySkinWeightPerMesh(project, model, "Skin Weight Auto (Bone Heat)", mc =>
                    {
                        if (!ahResults.TryGetValue(mc, out var r)) return 0;
                        int n = SkinWeightHeatOps.Apply(r);
                        if (n > 0) { ahVerts += n; ahChanged.Add(mc); }
                        return n;
                    });

                    var ahIdx = new int[ahChanged.Count];
                    var ahIds = new ulong[ahChanged.Count];
                    for (int i = 0; i < ahChanged.Count; i++)
                    {
                        ahIdx[i] = model.MeshContextList.IndexOf(ahChanged[i]);
                        ahIds[i] = ahChanged[i].ObjectId;
                    }

                    var ahBoneSet = new HashSet<int>();
                    foreach (var s in ahSources) ahBoneSet.Add(s.Bone);

                    ahRun.Success   = true;
                    ahRun.Vertices  = ahVerts;
                    ahRun.Meshes    = ahChanged.Count;
                    ahRun.Unreached = ahUnreached;
                    ahRun.Bones     = ahBoneSet.Count;

                    ReportData(CommandDataJson.New()
                        .Int("vertices",  ahVerts)
                        .Int("meshes",    ahChanged.Count)
                        .Int("unreached", ahUnreached)
                        .Int("bones",     ahBoneSet.Count)
                        .Build(), ahIdx, ahIds);
                    return true;
                }

                // ── 関節のぼかし方を測る（モデルは変えない）
                case MeasureJointBlendCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    int mjJoint = SkinWeightChainOps.ResolveBone(model, c.JointBone);
                    int mjFrom  = SkinWeightChainOps.ResolveBone(model, c.AxisFromBone);
                    if (mjJoint < 0) { Fail($"ボーン「{c.JointBone}」が見つかりません。"); return true; }
                    if (mjFrom  < 0) { Fail($"ボーン「{c.AxisFromBone}」が見つかりません。"); return true; }
                    if (!SkinWeightChainOps.ResolveBones(model, c.SideABones, out var mjA, out string mjErr)
                     || !SkinWeightChainOps.ResolveBones(model, c.SideBBones, out var mjB, out mjErr))
                    { Fail(mjErr); return true; }
                    if (!SkinWeightChainOps.Measure(model, mjJoint, mjFrom, mjA, mjB, c.SliceRatio,
                            out var mj, out mjErr))
                    { Fail(mjErr); return true; }

                    ReportData(CommandDataJson.New()
                        .Num("radius",     mj.Radius)
                        .Num("sMin",       mj.SMin)
                        .Num("sMax",       mj.SMax)
                        .Int("blendCount", mj.BlendCount)
                        .Int("sliceCount", mj.SliceCount)
                        .Num("boneLength", mj.BoneLength)
                        .Build());
                    return true;
                }

                case SkinWeightVolumePaintCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    if (!SkinWeightVolumeOps.TryBuildFrame(model, c.ToSpec(), out var swvFrame, out string swvErr))
                    { Fail(swvErr); return true; }

                    var swvTargets = SkinWeightOperations.CollectTargetMeshContexts(model);
                    if (swvTargets.Count == 0) { Fail("対象の描画オブジェクトがありません。"); return true; }

                    var swvNonSkinned = SkinWeightVolumeOps.FindNonSkinned(swvTargets);
                    if (swvNonSkinned.Count > 0)
                    {
                        Fail("スキンドでない描画オブジェクトが含まれています: "
                             + string.Join(" / ", swvNonSkinned));
                        return true;
                    }

                    int swvVerts = 0;
                    var swvChanged = new List<MeshContext>();
                    ApplySkinWeightPerMesh(project, model, "Skin Weight Volume Paint", mc =>
                    {
                        int n = SkinWeightVolumeOps.ApplyToMesh(mc, swvFrame, c.SelectedOnly);
                        if (n > 0) { swvVerts += n; swvChanged.Add(mc); }
                        return n;
                    });

                    var swvIdx = new int[swvChanged.Count];
                    var swvIds = new ulong[swvChanged.Count];
                    for (int i = 0; i < swvChanged.Count; i++)
                    {
                        swvIdx[i] = model.MeshContextList.IndexOf(swvChanged[i]);
                        swvIds[i] = swvChanged[i].ObjectId;
                    }

                    ReportData(CommandDataJson.New()
                        .Int("vertices",   swvVerts)
                        .Int("meshes",     swvChanged.Count)
                        .Int("parentBone", swvFrame.ParentBone)
                        .Build(), swvIdx, swvIds);
                    return true;
                }

                // ── MeshFilter → Skinned 変換
                case ConvertMeshFilterToSkinnedCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var entries = MeshFilterToSkinnedConverter.CollectMeshEntries(model);
                    if (entries.Count == 0) { Fail("変換できる対象がありません"); return true; }

                    // 変換前スナップショット
                    var beforeList = MeshFilterToSkinnedRecord.CaptureList(model);

                    // 変換する対象の ObjectId を先に控える。変換でボーンが増えて
                    // 索引が組み替わるため、あとから索引で引くと別のものを指す。
                    // MeshContext そのものは作り直されない（new MeshContext はボーンだけ）
                    // ので ObjectId は保たれる。
                    var mfsChangedIds = new List<ulong>(entries.Count);
                    foreach (var mfsEntry in entries)
                    {
                        ulong oid = mfsEntry.Context?.ObjectId ?? 0UL;
                        if (oid != 0UL) mfsChangedIds.Add(oid);
                    }

                    // 変換実行。内側の記録は止め、自動更新まで終わってから 1 件積む。
                    _undoController?.SuspendRecording();
                    try
                    {
                        MeshFilterToSkinnedConverter.Execute(
                            model, entries, c.SwapAxisForRotated, c.SetAxisForIdentity,
                            c.TolerantMirrorBranch
                                ? MirrorBranchTolerance.Tolerant
                                : MirrorBranchTolerance.Strict);

                        // スキンド化はソースの頂点をワールドへ焼き直すので、
                        // それを取り込むグループのダイジェストが変わる。
                        // ウェイトを塗り直すのはここ（変換が {bone,1.0} を書いたあと）。
                        RunAutoUpdateGroups(project, model, c.ModelIndex, mfsChangedIds);
                    }
                    finally
                    {
                        _undoController?.ResumeRecording();
                    }

                    // 変換後スナップショット。自動更新のあとに取る
                    // （先に取ると自動更新ぶんが Undo で戻らない）。
                    var afterList = MeshFilterToSkinnedRecord.CaptureList(model);

                    // Undo 記録
                    if (_undoController != null)
                    {
                        var record = new MeshFilterToSkinnedRecord
                        {
                            BeforeList = beforeList,
                            AfterList  = afterList,
                        };
                        {
                            string __dbgDesc = "MeshFilter → Skinned 変換";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, record);
                            _undoController.MeshListStack.Record(record, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }

                    // Phase 2a-2g-1: ClearScene + RebuildAdapter + SetSelectionState +
                    // UpdateSelectedDrawableMesh を EnterSceneReset(clearScene: true) に集約。
                    _viewportManager.EnterSceneReset(project, clearScene: true);
                    _viewportManager.EnterCameraChanged(_viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return true;
                }

                // ── 描画オブジェクト単位: SkinnedMesh 系 → MeshFilter 系
                case ConvertToMeshFilterCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0) { Fail("対象が指定されていません"); return true; }

                    var mfBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var mfResults = SkinKindConverter.ToMeshFilter(
                        model, c.MasterIndices, c.ParentMode);

                    int mfDone = 0;
                    foreach (var r in mfResults) if (r.Converted) mfDone++;
                    if (mfDone == 0) { Fail("MeshFilter へ変換できる対象がありません"); return true; }

                    RecordMeshListSnapshot(mfBefore, model,
                        $"ウェイト破棄 → MeshFilter x{mfDone}");

                    // 階層と頂点の格納空間が変わったので、GPU バッファを作り直す。
                    _viewportManager.EnterSceneReset(project, clearScene: true);
                    _viewportManager.EnterCameraChanged(
                        _viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return true;
                }

                // ── 描画オブジェクト単位: MeshFilter 系 → SkinnedMesh 系
                case ConvertToSkinnedCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.MasterIndices == null || c.MasterIndices.Length == 0) { Fail("対象が指定されていません"); return true; }

                    var skBone = model.GetMeshContext(c.BoneMasterIndex);
                    if (skBone == null || skBone.Type != MeshType.Bone)
                    {
                        Debug.LogWarning(
                            $"[SkinKind] バインド先がボーンではありません idx={c.BoneMasterIndex}");
                        return true;
                    }

                    var skBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    // 変換する対象の ObjectId を先に控える。索引は変換で動きうる。
                    var skChangedIds = new List<ulong>(c.MasterIndices.Length);
                    foreach (int skIdx in c.MasterIndices)
                    {
                        ulong oid = model.GetMeshContext(skIdx)?.ObjectId ?? 0UL;
                        if (oid != 0UL) skChangedIds.Add(oid);
                    }

                    int skDone = 0;

                    // 内側の記録は止め、自動更新まで終わってから 1 件積む。
                    _undoController?.SuspendRecording();
                    try
                    {
                        var skResults = SkinKindConverter.ToSkinned(
                            model, c.MasterIndices, c.BoneMasterIndex);

                        foreach (var r in skResults) if (r.Converted) skDone++;

                        // ToSkinned は全頂点へ {bone, 1.0} を書く（SkinKindConverter.cs:284）。
                        // ウェイトを塗り直すのはそのあと。
                        if (skDone > 0)
                            RunAutoUpdateGroups(project, model, c.ModelIndex, skChangedIds);
                    }
                    finally
                    {
                        _undoController?.ResumeRecording();
                    }

                    if (skDone == 0) { Fail("Skinned へ変換できる対象がありません"); return true; }

                    RecordMeshListSnapshot(skBefore, model,
                        $"スキンド化 → \"{skBone.Name}\" x{skDone}");

                    _viewportManager.EnterSceneReset(project, clearScene: true);
                    _viewportManager.EnterCameraChanged(
                        _viewportManager.PerspectiveViewport, CameraChangePhase.Committed);
                    _notifyPanels(ChangeKind.ModelSwitch);
                    return true;
                }

                // ── ボーンの左右対応を名前から補完
                case ResolveMirrorBoneIndexCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }

                    var mbiBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var mbiResult = MirrorBoneIndexResolver.Resolve(model);
                    if (mbiResult.Resolved == 0) { _notifyPanels(ChangeKind.Attributes); Fail("対応するミラー側ボーンが見つかりません"); return true; }

                    RecordMeshListSnapshot(mbiBefore, model,
                        $"左右ボーン対応の補完 x{mbiResult.Resolved}");

                    _notifyPanels(ChangeKind.Attributes);
                    return true;
                }

                // ── MediaPipe フェイス変形
                case MediaPipeFaceDeformCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var mpSrcMc = model.GetMeshContext(c.SourceMasterIndex);
                    var srcMesh = mpSrcMc?.MeshObject;
                    if (srcMesh == null) { Fail("変形元のメッシュがありません"); return true; }

                    // 3 本とも読み込む前に関門を通す。
                    if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                            c.BeforePath, out string mpBeforePath, out string mpSbReason))
                    { Fail($"BeforePath: {mpSbReason}"); return true; }
                    if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                            c.AfterPath, out string mpAfterPath, out mpSbReason))
                    { Fail($"AfterPath: {mpSbReason}"); return true; }
                    if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                            c.TrianglesPath, out string mpTriPath, out mpSbReason))
                    { Fail($"TrianglesPath: {mpSbReason}"); return true; }

                    try
                    {
                        var mpBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                        var beforeLM  = Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer.LoadLandmarks(mpBeforePath);
                        var afterLM   = Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer.LoadLandmarks(mpAfterPath);
                        var triangles = Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer.ParseTrianglesJson(
                            System.IO.File.ReadAllText(mpTriPath));

                        int vertexCount = srcMesh.VertexCount;
                        var positions   = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++) positions[i] = srcMesh.Vertices[i].Position;

                        var deformer = new Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer();
                        deformer.SetBaseMesh(beforeLM, triangles);
                        deformer.Bind(positions);
                        deformer.Apply(afterLM, positions);

                        var cloned = srcMesh.Clone();
                        cloned.Name = srcMesh.Name + "_MP";
                        for (int i = 0; i < vertexCount; i++) cloned.Vertices[i].Position = positions[i];

                        var mpNewMc = new MeshContext
                        {
                            MeshObject = cloned,
                            Materials  = new System.Collections.Generic.List<Material>(
                                mpSrcMc.Materials ?? new System.Collections.Generic.List<Material>()),
                        };
                        mpNewMc.UnityMesh           = cloned.ToUnityMesh();
                        mpNewMc.UnityMesh.name      = cloned.Name;
                        mpNewMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                        mpNewMc.ParentModelContext  = model;
                        model.Add(mpNewMc);
                        model.OnListChanged?.Invoke();

                        if (_undoController != null)
                        {
                            var mpAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                            var mpRecord = new MeshFilterToSkinnedRecord { BeforeList = mpBefore, AfterList = mpAfter };
                            {
                                string __dbgDesc = "MediaPipe変形";
                                PLDiag.UndoRecord("MeshList", __dbgDesc, mpRecord);
                                _undoController.MeshListStack.Record(mpRecord, __dbgDesc);
                            }
                            _undoController.FocusMeshList();
                        }
                        // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                        _viewportManager.EnterTopologyChanged(project);
                        _notifyPanels(ChangeKind.ListStructure);
                    }
                    catch (Exception ex)
                    {
                        Fail($"MediaPipe 変形に失敗しました: {ex.Message}");
                    }
                    return true;
                }

                // ── MediaPipe の顔から頭部を作る
                case MediaPipeHeadBuildCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var hbFaceMc   = model.GetMeshContext(c.FaceMasterIndex);
                    var hbBeforeMc = model.GetMeshContext(c.BeforeMasterIndex);
                    var hbHeadMc   = model.GetMeshContext(c.HeadMasterIndex);
                    if (hbFaceMc?.MeshObject   == null) { Fail($"MediaPipe の顔がありません（{c.FaceMasterIndex}）"); return true; }
                    if (hbBeforeMc?.MeshObject == null) { Fail($"貼り付け用の顔がありません（{c.BeforeMasterIndex}）"); return true; }
                    if (hbHeadMc?.MeshObject   == null) { Fail($"頭部のメッシュがありません（{c.HeadMasterIndex}）"); return true; }

                    try
                    {
                        // 頂点（静止時）をワールドへ。描画と同じ VertexToWorldMatrix。
                        Vector3[] ToWorld(MeshContext mc)
                        {
                            var mo = mc.MeshObject;
                            var m  = mc.VertexToWorldMatrix;
                            var w  = new Vector3[mo.VertexCount];
                            for (int i = 0; i < w.Length; i++) w[i] = m.MultiplyPoint3x4(mo.Vertices[i].Position);
                            return w;
                        }
                        var hbBeforeFaces = new List<int[]>();
                        foreach (var f in hbBeforeMc.MeshObject.Faces)
                            if (f?.VertexIndices != null && f.VertexIndices.Count >= 3) hbBeforeFaces.Add(f.VertexIndices.ToArray());

                        var hbWorld = Poly_Ling.Tools.MediaPipe.MediaPipeHeadBuilder.Build(
                            ToWorld(hbBeforeMc), hbBeforeFaces, ToWorld(hbFaceMc), ToWorld(hbHeadMc),
                            out var hbRep, c.DebugVertexIndices, c.DepthBlend, c.InsideDepthTolerance);

                        var hbList = MeshFilterToSkinnedRecord.CaptureList(model);
                        var hbSrc  = hbHeadMc.MeshObject;
                        var cloned = hbSrc.Clone();
                        cloned.Name = hbSrc.Name + "_MP";
                        var toLocal = hbHeadMc.WorldToVertexMatrix;
                        for (int i = 0; i < cloned.VertexCount; i++) cloned.Vertices[i].Position = toLocal.MultiplyPoint3x4(hbWorld[i]);
                        int hbTextured = Poly_Ling.Tools.MediaPipe.MediaPipeHeadBuilder.ApplyFaceTexture(
                            cloned, hbRep.Copied, hbFaceMc.MeshObject, c.FaceMaterialIndex);

                        var hbNewMc = new MeshContext
                        {
                            MeshObject = cloned,
                            Materials  = new System.Collections.Generic.List<Material>(
                                hbHeadMc.Materials ?? new System.Collections.Generic.List<Material>()),
                        };
                        hbNewMc.UnityMesh           = cloned.ToUnityMesh();
                        hbNewMc.UnityMesh.name      = cloned.Name;
                        hbNewMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                        hbNewMc.ParentModelContext  = model;
                        int hbIdx = model.Add(hbNewMc);

                        // 左右対称の頭部の半身（鏡写しは bakeMirror で別に行う）
                        int hsIdx = -1, hsDeleted = 0, hsSeam = 0, hsClamped = 0; float hsMaxShift = 0f;
                        if (c.MakeSymmetric)
                        {
                            var hbHeadFaces = new List<int[]>();
                            foreach (var f in hbSrc.Faces) hbHeadFaces.Add(f.VertexIndices.ToArray());
                            var halfWorld = Poly_Ling.Tools.MediaPipe.MediaPipeHeadBuilder.BuildHalf(
                                ToWorld(hbHeadMc), hbWorld, hbHeadFaces, hbBeforeMc.MeshObject.VertexCount,
                                hbRep.MirrorPlaneX, c.SeamX, c.SeamTolerance,
                                out bool[] keepFace, out hsDeleted, out hsSeam, out _, out hsMaxShift, out hsClamped);

                            var half = hbSrc.Clone();
                            half.Name = hbSrc.Name + "_MP_対称";
                            for (int i = 0; i < half.VertexCount; i++) half.Vertices[i].Position = toLocal.MultiplyPoint3x4(halfWorld[i]);
                            // 顔面の材質と UV（詰める前の、元の頭部と同じ頂点番号のうちに当てる）
                            Poly_Ling.Tools.MediaPipe.MediaPipeHeadBuilder.ApplyFaceTexture(
                                half, hbRep.Copied, hbFaceMc.MeshObject, c.FaceMaterialIndex);
                            // 消した面を除き、使われなくなった頂点を詰める
                            var keptFaces = new List<Face>();
                            for (int fi = 0; fi < half.Faces.Count; fi++) if (keepFace[fi]) keptFaces.Add(half.Faces[fi]);
                            var remap = new int[half.VertexCount];
                            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
                            var keptVerts = new List<Vertex>();
                            foreach (var f in keptFaces)
                                foreach (int v in f.VertexIndices)
                                    if (remap[v] < 0) { remap[v] = keptVerts.Count; keptVerts.Add(half.Vertices[v]); }
                            foreach (var f in keptFaces)
                                for (int k = 0; k < f.VertexIndices.Count; k++) f.VertexIndices[k] = remap[f.VertexIndices[k]];
                            half.Vertices = keptVerts;
                            half.Faces    = keptFaces;

                            var hsMc = new MeshContext
                            {
                                MeshObject = half,
                                Materials  = new System.Collections.Generic.List<Material>(
                                    hbHeadMc.Materials ?? new System.Collections.Generic.List<Material>()),
                            };
                            hsMc.UnityMesh           = half.ToUnityMesh();
                            hsMc.UnityMesh.name      = half.Name;
                            hsMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                            hsMc.ParentModelContext  = model;
                            hsIdx = model.Add(hsMc);
                        }

                        // 写す先の顔メッシュ（A・B タイプなど）
                        int tgIdx = -1, tgInside = 0, tgOutside = 0, tgTextured = 0;
                        if (c.TargetMasterIndex >= 0)
                        {
                            var tgMc = model.GetMeshContext(c.TargetMasterIndex);
                            if (tgMc?.MeshObject == null) throw new InvalidOperationException($"写す先の顔メッシュがありません（{c.TargetMasterIndex}）");
                            var fm = hbFaceMc.MeshObject;
                            var faceUVs = new Vector2[fm.VertexCount];
                            for (int i = 0; i < faceUVs.Length; i++)
                                faceUVs[i] = fm.Vertices[i].UVs != null && fm.Vertices[i].UVs.Count > 0 ? fm.Vertices[i].UVs[0] : Vector2.zero;
                            var tgWorld = Poly_Ling.Tools.MediaPipe.MediaPipeHeadBuilder.BuildTarget(
                                ToWorld(hbBeforeMc), hbBeforeFaces, ToWorld(hbFaceMc), ToWorld(tgMc),
                                c.DepthBlend, faceUVs, out var tgRep, out var tgUv, out var tgUvValid,
                                null, c.InsideDepthTolerance);
                            tgOutside = tgRep.OutsideVertices;
                            tgInside  = tgRep.AffineVertices - tgRep.OutsideVertices;

                            var tgSrc = tgMc.MeshObject;
                            var tg = tgSrc.Clone();
                            tg.Name = tgSrc.Name + "_MP";
                            var tgToLocal = tgMc.WorldToVertexMatrix;
                            for (int i = 0; i < tg.VertexCount; i++) tg.Vertices[i].Position = tgToLocal.MultiplyPoint3x4(tgWorld[i]);
                            tgTextured = Poly_Ling.Tools.MediaPipe.MediaPipeHeadBuilder.ApplyTargetTexture(
                                tg, tgUv, tgUvValid, c.FaceMaterialIndex);

                            var tgNewMc = new MeshContext
                            {
                                MeshObject = tg,
                                Materials  = new System.Collections.Generic.List<Material>(
                                    tgMc.Materials ?? new System.Collections.Generic.List<Material>()),
                            };
                            tgNewMc.UnityMesh           = tg.ToUnityMesh();
                            tgNewMc.UnityMesh.name      = tg.Name;
                            tgNewMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                            tgNewMc.ParentModelContext  = model;
                            tgIdx = model.Add(tgNewMc);
                        }
                        var hbOutIdx = new List<int> { hbIdx };
                        var hbOutIds = new List<ulong> { hbNewMc.ObjectId };
                        foreach (int oi in new[] { hsIdx, tgIdx })
                            if (oi >= 0) { hbOutIdx.Add(oi); hbOutIds.Add(model.GetMeshContext(oi).ObjectId); }
                        model.OnListChanged?.Invoke();

                        if (_undoController != null)
                        {
                            var hbAfterList = MeshFilterToSkinnedRecord.CaptureList(model);
                            var hbRecord = new MeshFilterToSkinnedRecord { BeforeList = hbList, AfterList = hbAfterList };
                            PLDiag.UndoRecord("MeshList", "MediaPipe 頭部生成", hbRecord);
                            _undoController.MeshListStack.Record(hbRecord, "MediaPipe 頭部生成");
                            _undoController.FocusMeshList();
                        }
                        _viewportManager.EnterTopologyChanged(project);
                        _notifyPanels(ChangeKind.ListStructure);

                        Debug.Log($"[MediaPipeHead] 拡大率 {hbRep.FitScale:G4} / 対称面 x={hbRep.MirrorPlaneX:G4} / 組 {hbRep.Pairs}・中央 {hbRep.Midline}・組なし {hbRep.Unpaired}"
                                  + $"（ずれ最大 {hbRep.MaxPairError:G3}）/ 鏡写しの面 {hbRep.MirrorFacesOk}/{hbRep.MirrorFaces} / 三角形 {hbRep.Triangles}"
                                  + $" / そのまま {hbRep.CopiedVertices}・アフィン {hbRep.AffineVertices}（三角形の外 {hbRep.OutsideVertices}）");

                        ReportData(CommandDataJson.New()
                            .Num("fitScale", hbRep.FitScale).Num("mirrorPlaneX", hbRep.MirrorPlaneX)
                            .Int("pairs", hbRep.Pairs).Int("midline", hbRep.Midline).Int("unpaired", hbRep.Unpaired)
                            .Num("maxPairError", hbRep.MaxPairError)
                            .Int("mirrorFacesOk", hbRep.MirrorFacesOk).Int("mirrorFaces", hbRep.MirrorFaces)
                            .Int("triangles", hbRep.Triangles)
                            .Int("copiedVertices", hbRep.CopiedVertices).Int("affineVertices", hbRep.AffineVertices)
                            .Int("outsideVertices", hbRep.OutsideVertices)
                            .Num("maxBeforeSymShift", hbRep.MaxBeforeSymShift)
                            .Int("targetMasterIndex", tgIdx).Int("targetInsideVertices", tgInside)
                            .Int("targetOutsideVertices", tgOutside).Int("targetTexturedFaces", tgTextured)
                            .Int("texturedFaces", hbTextured)
                            .Int("symmetricMasterIndex", hsIdx).Int("halfDeletedFaces", hsDeleted)
                            .Int("seamVertices", hsSeam).Num("seamMaxShift", hsMaxShift)
                            .Int("clampedVertices", hsClamped).Build(),
                            hbOutIdx.ToArray(), hbOutIds.ToArray());
                    }
                    catch (Exception ex)
                    {
                        Fail($"MediaPipe 頭部生成に失敗しました: {ex.Message}");
                    }
                    return true;
                }

                // ── 表情転写（MediaPipe。BEFORE・AFTER・カメラは faceTransfer の窓口から）
                case FaceExpressionTransferCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    if (c.SourceMasterIndices == null || c.SourceMasterIndices.Length == 0)
                    { Fail("変形するメッシュが選ばれていません"); return true; }
                    var ftSources = new List<MeshContext>();
                    foreach (int mi in c.SourceMasterIndices.Distinct())
                    {
                        var mc = model.GetMeshContext(mi);
                        if (mc?.MeshObject == null) { Fail($"変形元のメッシュがありません（{mi}）"); return true; }
                        ftSources.Add(mc);
                    }

                    if (FaceTransfer == null) { Fail("faceTransfer の窓口がありません"); return true; }
                    var ftInputs = FaceTransfer.GetInputs(out string ftReason);
                    if (ftInputs == null) { Fail(ftReason); return true; }

                    // 三角形：指定が無ければ組み込み（MediaPipe の FACEMESH_TESSELATION）
                    string ftTriJson;
                    if (string.IsNullOrWhiteSpace(c.TrianglesPath))
                        ftTriJson = Poly_Ling.NohMask.FaceTrianglesData.Json;
                    else
                    {
                        if (!Poly_Ling.Core.PLSandbox.TryResolveRead(
                                c.TrianglesPath, out string ftTriPath, out string ftSbReason))
                        { Fail($"TrianglesPath: {ftSbReason}"); return true; }
                        ftTriJson = System.IO.File.ReadAllText(ftTriPath);
                    }

                    try
                    {
                        var ftBefore  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var triangles = Poly_Ling.Tools.MediaPipe.MediaPipeFaceDeformer.ParseTrianglesJson(ftTriJson);

                        foreach (var ftSrcMc in ftSources)
                        {
                            var ftSrc = ftSrcMc.MeshObject;

                            // 頂点（静止時）をワールドへ。描画と同じ VertexToWorldMatrix（スキンドは単位）。
                            int vertexCount = ftSrc.VertexCount;
                            var toWorld = ftSrcMc.VertexToWorldMatrix;
                            var toLocal = ftSrcMc.WorldToVertexMatrix;
                            var world   = new Vector3[vertexCount];
                            for (int i = 0; i < vertexCount; i++) world[i] = toWorld.MultiplyPoint3x4(ftSrc.Vertices[i].Position);

                            var moved = Poly_Ling.Tools.MediaPipe.FaceExpressionTransfer.Transfer(
                                world, ftInputs.Camera, ftInputs.BeforePixels, ftInputs.AfterPixels, triangles,
                                out bool[] movedFlags, out int bound);

                            var cloned = ftSrc.Clone();
                            cloned.Name = ftSrc.Name + "_MP";
                            int movedCount = 0;
                            for (int i = 0; i < vertexCount; i++)
                            {
                                if (!movedFlags[i]) continue;
                                cloned.Vertices[i].Position = toLocal.MultiplyPoint3x4(moved[i]);
                                movedCount++;
                            }

                            var ftNewMc = new MeshContext
                            {
                                MeshObject = cloned,
                                Materials  = new System.Collections.Generic.List<Material>(
                                    ftSrcMc.Materials ?? new System.Collections.Generic.List<Material>()),
                            };
                            ftNewMc.UnityMesh           = cloned.ToUnityMesh();
                            ftNewMc.UnityMesh.name      = cloned.Name;
                            ftNewMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                            ftNewMc.ParentModelContext  = model;
                            model.Add(ftNewMc);
                            Debug.Log($"[FaceTransfer] {ftSrc.Name}: 結び付いた頂点 {bound} / 動かした頂点 {movedCount} / 全 {vertexCount}");
                        }
                        model.OnListChanged?.Invoke();

                        if (_undoController != null)
                        {
                            var ftAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                            var ftRecord = new MeshFilterToSkinnedRecord { BeforeList = ftBefore, AfterList = ftAfter };
                            PLDiag.UndoRecord("MeshList", "表情転写", ftRecord);
                            _undoController.MeshListStack.Record(ftRecord, "表情転写");
                            _undoController.FocusMeshList();
                        }
                        _viewportManager.EnterTopologyChanged(project);
                        _notifyPanels(ChangeKind.ListStructure);
                    }
                    catch (Exception ex)
                    {
                        Fail($"表情転写に失敗しました: {ex.Message}");
                    }
                    return true;
                }

                // ── Quad減面
                case QuadDecimateCommand c:
                {
                    if (model == null) { Fail("no current model"); return true; }
                    var qdSrcMc = model.GetMeshContext(c.SourceMasterIndex);
                    if (qdSrcMc?.MeshObject == null) { Fail("対象メッシュがありません"); return true; }

                    var qdBefore = MeshFilterToSkinnedRecord.CaptureList(model);

                    var prms = new Poly_Ling.UI.QuadDecimator.DecimatorParams
                    {
                        TargetRatio     = c.TargetRatio,
                        MaxPasses       = c.MaxPasses,
                        NormalAngleDeg  = c.NormalAngleDeg,
                        HardAngleDeg    = c.HardAngleDeg,
                        UvSeamThreshold = c.UvSeamThreshold,
                    };
                    var result = Poly_Ling.Tools.Panels.QuadDecimator.QuadPreservingDecimator.Decimate(
                        qdSrcMc.MeshObject, prms, out MeshObject resultMesh);
                    if (resultMesh == null) { Fail("四角形化に失敗しました"); return true; }

                    resultMesh.Name = qdSrcMc.MeshObject.Name + "_decimated";
                    var qdNewMc = new MeshContext
                    {
                        Name       = resultMesh.Name,
                        MeshObject = resultMesh,
                        Materials  = new System.Collections.Generic.List<Material>(
                            qdSrcMc.Materials ?? new System.Collections.Generic.List<Material>()),
                    };
                    qdNewMc.UnityMesh           = resultMesh.ToUnityMesh();
                    qdNewMc.UnityMesh.name      = resultMesh.Name;
                    qdNewMc.UnityMesh.hideFlags = HideFlags.HideAndDontSave;
                    qdNewMc.ParentModelContext  = model;
                    model.Add(qdNewMc);
                    model.OnListChanged?.Invoke();

                    if (_undoController != null)
                    {
                        var qdAfter  = MeshFilterToSkinnedRecord.CaptureList(model);
                        var qdRecord = new MeshFilterToSkinnedRecord { BeforeList = qdBefore, AfterList = qdAfter };
                        {
                            string __dbgDesc = "Quad減面";
                            PLDiag.UndoRecord("MeshList", __dbgDesc, qdRecord);
                            _undoController.MeshListStack.Record(qdRecord, __dbgDesc);
                        }
                        _undoController.FocusMeshList();
                    }
                    // Phase 2a-2g-1: RebuildAdapter + UpdateSelectedDrawableMesh の連鎖を EnterTopologyChanged に集約。
                    _viewportManager.EnterTopologyChanged(project);
                    _notifyPanels(ChangeKind.ListStructure);
                    return true;
                }
            }
            return false;
        }
    }
}
