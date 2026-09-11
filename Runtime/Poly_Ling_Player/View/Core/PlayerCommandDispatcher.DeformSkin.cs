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
                    var before = _undoController?.CaptureMeshObjectSnapshot();

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
                        var after = _undoController.CaptureMeshObjectSnapshot();
                        _commandQueue?.Enqueue(
                            new RecordTopologyChangeCommand(
                                _undoController, before, after, c.OperationName));
                    }

                    // UnityMesh + GPU 更新
                    // Phase 2a-2g-1: SyncMeshPositionsAndTransform + UpdateTransform を EnterVerticesMoved(Dragging) に集約。

                    _viewportManager.EnterVerticesMoved(project, VerticesMovedPhase.Dragging, uvMc);
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
