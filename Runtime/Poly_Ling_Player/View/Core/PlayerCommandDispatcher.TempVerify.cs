// PlayerCommandDispatcher.TempVerify.cs
//
// ================================================================
// 【臨時】このファイルは検証専用である。製品の機能ではない。
// ================================================================
//   verifyBindMove / verifyBindRotate / verifyBindSculpt の受け口。
//   コマンド定義は PanelCommand.TempVerify.cs（そちらも臨時）。
//
//   ・製品の機能から呼ばないこと。参考実装として真似しないこと。
//   ・どれも破壊的。ResetProjectCommand を通すので開いているモデルを全部捨て、
//     頂点を動かす。
//   ・試験で状態を戻すときは GPU 側（_positions）も戻すこと。
//     Vertices[] を戻して InvalidatePositionCache するだけでは GPU に
//     打った結果が残り、次の測定がその汚れを拾う（2026-09-15 に実際に嵌った）。
//
//   残件は PolyLing_姿勢_残件.md の「臨時コマンドと調査用コードの後始末」。
//   検証が済んだらこのファイルごと削除する。
// ================================================================
// Runtime/Poly_Ling_Player/View/Core/ に配置

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.Player
{
    public partial class PlayerCommandDispatcher
    {
        /// <summary>
        /// 【臨時】DispatchCore の分担：姿勢まわりの検証。
        /// 段はすべて既存コマンドを Dispatch で通す。CommandQueue.Enqueue は同期実行
        /// （CommandQueue.cs:48-54）なので読み込みもこの呼び出しの中で完了する。
        /// </summary>
        private bool DispatchTempVerify(PanelCommand cmd, ProjectContext project, ModelContext model)
        {
            switch (cmd)
            {
                case VerifyBindSculptCommand c:
                {
                    var rReset = Dispatch(new ResetProjectCommand("Model"));
                    if (rReset == null || !rReset.Success) { Fail("reset に失敗"); return true; }

                    bool useOrigin = !string.IsNullOrEmpty(c.OriginCsvPath);
                    PanelCommand import;
                    if (!string.IsNullOrEmpty(c.MqoPath))
                        import = new ImportMqoFileCommand(
                            0, c.MqoPath, null, "", "", false, useOrigin, c.OriginCsvPath, false);
                    else
                        import = new ImportPmxFileCommand(
                            0, c.PmxPath, null, true, useOrigin, c.OriginCsvPath, false);

                    var rImport = Dispatch(import);
                    if (rImport == null || !rImport.Success) { Fail("読み込みに失敗"); return true; }

                    var m = project?.CurrentModel;
                    if (m == null || m.DrawableCount == 0) { Fail("読み込み後の描画オブジェクトが 0"); return true; }

                    int targetIdx = c.MasterIndex;
                    if (targetIdx < 0) targetIdx = FindNonSkinnedDrawable(m);
                    if (targetIdx < 0) { Fail("非スキンドの描画オブジェクトが見つからない"); return true; }

                    var mc = m.GetMeshContext(targetIdx);
                    if (mc?.MeshObject == null) { Fail("対象が引けない"); return true; }
                    int vcount = mc.MeshObject.VertexCount;
                    if (vcount == 0) { Fail("対象の頂点数が 0"); return true; }

                    // ── 読み込み直後（ポーズを入れる前・ブラシの前）に、
                    //    CPU の Vertices[].Position と GPU の _positions を突き合わせる。
                    float loadGap = -1f; int loadGapCount = -1;
                    var loadCpu = Vector3.zero; var loadGpu = Vector3.zero;
                    if (_viewportManager != null &&
                        _viewportManager.TryGetVertexBufferProbe(
                            m, mc, 0, out int lg0, out _, out _, out _, out _, out _, out _))
                    {
                        loadGapCount = 0;
                        var bmPos = _viewportManager.GetBufferPositionsForVerify();
                        if (bmPos != null)
                        {
                            for (int i = 0; i < vcount && lg0 + i < bmPos.Length; i++)
                            {
                                float g = Vector3.Distance(
                                    mc.MeshObject.Vertices[i].Position, bmPos[lg0 + i]);
                                if (g > c.Tolerance) loadGapCount++;
                                if (g > loadGap)
                                {
                                    loadGap = g;
                                    loadCpu = mc.MeshObject.Vertices[i].Position;
                                    loadGpu = bmPos[lg0 + i];
                                }
                            }
                        }
                    }

                    int poseIdx = c.PoseBoneMasterIndex >= 0 ? c.PoseBoneMasterIndex : targetIdx;
                    var rPose = Dispatch(new SetBonePoseValueCommand(
                        0, new[] { poseIdx },
                        SetBoneTransformValueCommand.Field.RotationZ, c.PoseRotationZ));
                    if (rPose == null || !rPose.Success) { Fail("ポーズ層の付与に失敗"); return true; }

                    // 姿勢が動いた頂点の 1 つをブラシ中心にする。
                    int anchor = -1;
                    float matrixMaxDiff = 0f;
                    for (int i = 0; i < vcount; i++)
                    {
                        float d = MatrixMaxAbsDiff(mc.VertexMatrix(i, false), mc.VertexMatrix(i, true));
                        if (d > matrixMaxDiff) matrixMaxDiff = d;
                        if (anchor < 0 && d > c.Tolerance) anchor = i;
                    }
                    if (anchor < 0) { Fail("姿勢が動いている頂点が無い"); return true; }

                    // 【臨時】この頂点のグローバル索引を求め、_positions への書き込みを記録させる。
                    if (_viewportManager != null &&
                        _viewportManager.TryGetVertexBufferProbe(
                            m, mc, anchor, out int probeGlobal, out _, out _, out _, out _, out _, out _))
                    {
                        _viewportManager.SetPositionWriteProbe(probeGlobal);
                    }

                    if (!RunOneSculpt(project, c, targetIdx, anchor, true,
                                      out int bindHit, out bool bindAnchor, out int bindLegacy, out float bindGap,
                                      out int bindGapCount, out float bindGapMax, out int bindGapWorst, out string bindReason))
                    { Fail("バインド表示でのブラシに失敗: " + bindReason); return true; }

                    if (!RunOneSculpt(project, c, targetIdx, anchor, false,
                                      out int poseHit, out bool poseAnchor, out int poseLegacy, out float poseGap,
                                      out int poseGapCount, out float poseGapMax, out int poseGapWorst, out string poseReason))
                    { Fail("現在ポーズ表示でのブラシに失敗: " + poseReason); return true; }

                    bool discriminating = (bindLegacy != bindHit) || (poseLegacy != poseHit);
                    bool pass = bindAnchor && poseAnchor && discriminating;

                    // ── 裏取り。食い違った頂点が「GPU では WorldMatrix が入る欄」を
                    //    引いているかを実データで数える。
                    //    GPU の欄の決め方は UnifiedBufferManager_Update.cs:85-101。
                    var list = m.MeshContextList;
                    var worstIdx  = new List<int>();
                    var worstW    = new List<float>();
                    var worstKind = new List<string>();
                    int refDirect = 0;

                    if (poseGapWorst >= 0 && poseGapWorst < mc.MeshObject.VertexCount)
                    {
                        var wv = mc.MeshObject.Vertices[poseGapWorst];
                        if (wv != null && wv.HasBoneWeight)
                        {
                            var bw = wv.BoneWeight.Value;
                            int[] bi = { bw.boneIndex0, bw.boneIndex1, bw.boneIndex2, bw.boneIndex3 };
                            float[] bwt = { bw.weight0, bw.weight1, bw.weight2, bw.weight3 };
                            for (int k = 0; k < 4; k++)
                            {
                                worstIdx.Add(bi[k]);
                                worstW.Add(bwt[k]);
                                worstKind.Add(DescribeBoneSlot(list, bi[k]));
                            }
                        }
                    }

                    for (int vi = 0; vi < mc.MeshObject.VertexCount; vi++)
                    {
                        var v = mc.MeshObject.Vertices[vi];
                        if (v == null || !v.HasBoneWeight) continue;
                        var bw = v.BoneWeight.Value;
                        if (UsesWorldMatrixDirect(list, bw.boneIndex0, bw.weight0) ||
                            UsesWorldMatrixDirect(list, bw.boneIndex1, bw.weight1) ||
                            UsesWorldMatrixDirect(list, bw.boneIndex2, bw.weight2) ||
                            UsesWorldMatrixDirect(list, bw.boneIndex3, bw.weight3))
                            refDirect++;
                    }

                    // ── 食い違う頂点と一致する頂点を分けて、両者の違いを見る。
                    //    直前の RunOneSculpt(false) で現在ポーズ表示のまま、位置も戻っている。
                    var gpuNow = ReadGpuWorld(project, mc);
                    int matchCount = 0;
                    var gapBones   = new List<int>();
                    var matchBones = new List<int>();
                    float gapWSumMin = float.MaxValue, gapWSumMax = 0f;
                    float mtcWSumMin = float.MaxValue, mtcWSumMax = 0f;
                    int gapNoWeight = 0, matchNoWeight = 0;

                    if (gpuNow != null)
                    {
                        int vn = mc.MeshObject.VertexCount;
                        for (int vi = 0; vi < vn && vi < gpuNow.Length; vi++)
                        {
                            var v = mc.MeshObject.Vertices[vi];
                            if (v == null) continue;
                            Vector3 cpu = mc.VertexMatrix(vi, false).MultiplyPoint3x4(v.Position);
                            bool gap = Vector3.Distance(cpu, gpuNow[vi]) > c.Tolerance;
                            if (!gap) matchCount++;

                            if (!v.HasBoneWeight)
                            {
                                if (gap) gapNoWeight++; else matchNoWeight++;
                                continue;
                            }
                            var bw = v.BoneWeight.Value;
                            float ws = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
                            int[] bi = { bw.boneIndex0, bw.boneIndex1, bw.boneIndex2, bw.boneIndex3 };
                            float[] bwt = { bw.weight0, bw.weight1, bw.weight2, bw.weight3 };

                            if (gap)
                            {
                                if (ws < gapWSumMin) gapWSumMin = ws;
                                if (ws > gapWSumMax) gapWSumMax = ws;
                                for (int k = 0; k < 4; k++)
                                    if (bwt[k] != 0f && gapBones.Count < 12 && !gapBones.Contains(bi[k]))
                                        gapBones.Add(bi[k]);
                            }
                            else
                            {
                                if (ws < mtcWSumMin) mtcWSumMin = ws;
                                if (ws > mtcWSumMax) mtcWSumMax = ws;
                                for (int k = 0; k < 4; k++)
                                    if (bwt[k] != 0f && matchBones.Count < 12 && !matchBones.Contains(bi[k]))
                                        matchBones.Add(bi[k]);
                            }
                        }
                    }
                    if (gapWSumMin == float.MaxValue) gapWSumMin = 0f;
                    if (mtcWSumMin == float.MaxValue) mtcWSumMin = 0f;

                    // ── 参照ボーンを名前つき・全件・出現数つきで集計し直す。
                    var gapUse   = new Dictionary<int, int>();
                    var matchUse = new Dictionary<int, int>();
                    if (gpuNow != null)
                    {
                        int vn = mc.MeshObject.VertexCount;
                        for (int vi = 0; vi < vn && vi < gpuNow.Length; vi++)
                        {
                            var v = mc.MeshObject.Vertices[vi];
                            if (v == null || !v.HasBoneWeight) continue;
                            Vector3 cpu = mc.VertexMatrix(vi, false).MultiplyPoint3x4(v.Position);
                            bool gap = Vector3.Distance(cpu, gpuNow[vi]) > c.Tolerance;
                            var dst = gap ? gapUse : matchUse;
                            var bw = v.BoneWeight.Value;
                            int[] bi = { bw.boneIndex0, bw.boneIndex1, bw.boneIndex2, bw.boneIndex3 };
                            float[] bwt = { bw.weight0, bw.weight1, bw.weight2, bw.weight3 };
                            for (int k = 0; k < 4; k++)
                            {
                                if (bwt[k] == 0f) continue;
                                dst.TryGetValue(bi[k], out int cnt);
                                dst[bi[k]] = cnt + 1;
                            }
                        }
                    }

                    // ── ボーン 20（＝最悪頂点が単独で参照している欄）の行列を並べる。
                    var gpuMat = _viewportManager?.GetTransformMatricesForVerify();
                    int probe = worstIdx.Count > 0 ? worstIdx[0] : -1;
                    var cpuSkin = new List<float>();
                    var cpuWorld = new List<float>();
                    var cpuBind = new List<float>();
                    var gpuSlot = new List<float>();
                    float slotGap = -1f;
                    if (probe >= 0 && list != null && probe < list.Count && list[probe] != null)
                    {
                        var bctx = list[probe];
                        AddMat(cpuSkin,  bctx.SkinningMatrix);
                        AddMat(cpuWorld, bctx.WorldMatrix);
                        AddMat(cpuBind,  bctx.BindPose);
                        if (gpuMat != null && probe < gpuMat.Length)
                        {
                            AddMat(gpuSlot, gpuMat[probe]);
                            slotGap = MatrixMaxAbsDiff(bctx.SkinningMatrix, gpuMat[probe]);
                        }
                    }

                    // ── GPU バッファの中身を頂点 1 つぶん取り出して突き合わせる。
                    int   pGlobal = -1, pExpStart = -1, pExpCount = -1, pBufMeshes = -1;
                    var   pInput  = Vector3.zero;
                    int[] pBoneIds = Array.Empty<int>();
                    float[] pBoneWts = Array.Empty<float>();
                    float pInputGap = -1f;
                    if (_viewportManager != null &&
                        _viewportManager.TryGetVertexBufferProbe(
                            m, mc, poseGapWorst,
                            out pGlobal, out pInput, out pBoneIds, out pBoneWts,
                            out pExpStart, out pExpCount, out pBufMeshes))
                    {
                        if (poseGapWorst >= 0 && poseGapWorst < mc.MeshObject.VertexCount)
                            pInputGap = Vector3.Distance(
                                pInput, mc.MeshObject.Vertices[poseGapWorst].Position);
                    }

                    // ── _positions を書いた経路の記録を読む。
                    string wPath = ""; var wVal = Vector3.zero;
                    int wCnt = 0, cB = 0, cUW = 0, cUB = 0, cAW = 0, cAC = 0;
                    _viewportManager?.TryGetPositionWriteLog(
                        out wPath, out wVal, out wCnt, out cB, out cUW, out cUB, out cAW, out cAC);

                    int srcCtx = -1, srcUni = -1, srcBase = -1; string srcName = "";
                    _viewportManager?.TryGetPositionWriter(out srcCtx, out srcUni, out srcBase, out srcName);

                    ReportData(CommandDataJson.New()
                        .Int ("targetIndex",     targetIdx)
                        .Text("targetName",      mc.MeshObject.Name ?? "")
                        .Int ("anchorVertex",    anchor)
                        .Num ("matrixMaxDiff",   matrixMaxDiff)
                        .Int ("bindHit",         bindHit)
                        .Flag("bindAnchorMoved", bindAnchor)
                        .Int ("bindLegacyHit",   bindLegacy)
                        .Int ("poseHit",         poseHit)
                        .Flag("poseAnchorMoved", poseAnchor)
                        .Int ("poseLegacyHit",   poseLegacy)
                        .Num ("bindCpuGpuGap",   bindGap)
                        .Num ("poseCpuGpuGap",   poseGap)
                        .Int ("bindGapCount",    bindGapCount)
                        .Num ("bindGapMax",      bindGapMax)
                        .Int ("bindGapWorst",    bindGapWorst)
                        .Int ("poseGapCount",    poseGapCount)
                        .Num ("poseGapMax",      poseGapMax)
                        .Int ("poseGapWorst",    poseGapWorst)
                        .Ints("worstBoneIndices", worstIdx.ToArray())
                        .Nums("worstBoneWeights", worstW.ToArray())
                        .Texts("worstBoneKinds",  worstKind.ToArray())
                        .Int ("gapVertsRefDirect", refDirect)
                        .Int ("matchCount",        matchCount)
                        .Ints("gapBones",          gapBones.ToArray())
                        .Ints("matchBones",        matchBones.ToArray())
                        .Num ("gapWeightSumMin",   gapWSumMin)
                        .Num ("gapWeightSumMax",   gapWSumMax)
                        .Num ("matchWeightSumMin", mtcWSumMin)
                        .Num ("matchWeightSumMax", mtcWSumMax)
                        .Int ("gapNoWeight",       gapNoWeight)
                        .Int ("matchNoWeight",     matchNoWeight)
                        .Texts("gapBoneUse",       DescribeUse(list, gapUse))
                        .Texts("matchBoneUse",     DescribeUse(list, matchUse))
                        .Int ("probeBone",         probe)
                        .Num ("probeSlotGap",      slotGap)
                        .Nums("probeCpuSkinning",  cpuSkin.ToArray())
                        .Nums("probeCpuWorld",     cpuWorld.ToArray())
                        .Nums("probeCpuBindPose",  cpuBind.ToArray())
                        .Nums("probeGpuSlot",      gpuSlot.ToArray())
                        .Int ("bufGlobalIndex",    pGlobal)
                        .Num ("bufInputGap",       pInputGap)
                        .Nums("bufInputLocal",     V(pInput))
                        .Nums("cpuLocal",          poseGapWorst >= 0 && poseGapWorst < mc.MeshObject.VertexCount
                                                     ? V(mc.MeshObject.Vertices[poseGapWorst].Position)
                                                     : new float[0])
                        .Int ("workingPositions",  mc.WorkingPositions == null ? 0 : mc.WorkingPositions.Length)
                        .Text("posLastPath",       wPath ?? "")
                        .Nums("posLastValue",      V(wVal))
                        .Int ("posWriteCount",     wCnt)
                        .Int ("cntBuild",          cB)
                        .Int ("cntUpdWorking",     cUW)
                        .Int ("cntUpdBase",        cUB)
                        .Int ("cntAllWorking",     cAW)
                        .Int ("cntAllCopy",        cAC)
                        .Int ("srcContextIndex",   srcCtx)
                        .Int ("srcUnifiedIndex",   srcUni)
                        .Int ("srcBaseOffset",     srcBase)
                        .Text("srcMeshName",       srcName ?? "")
                        .Ints("bufBoneIds",        pBoneIds)
                        .Nums("bufBoneWeights",    pBoneWts)
                        .Int ("bufExpandStart",    pExpStart)
                        .Int ("bufExpandCount",    pExpCount)
                        .Int ("bufMeshCount",      pBufMeshes)
                        .Int ("meshVertexCount",   mc.MeshObject.VertexCount)
                        .Num ("loadInputGapMax",   loadGap)
                        .Int ("loadInputGapCount", loadGapCount)
                        .Nums("loadCpuLocal",      V(loadCpu))
                        .Nums("loadGpuLocal",      V(loadGpu))
                        .Int ("contextCount",      m.MeshContextList.Count)
                        .Flag("discriminating",  discriminating)
                        .Flag("pass",            pass)
                        .Build());
                    return true;
                }

                case VerifyBindRotateCommand c:
                {
                    var rReset = Dispatch(new ResetProjectCommand("Model"));
                    if (rReset == null || !rReset.Success) { Fail("reset に失敗"); return true; }

                    bool useOrigin = !string.IsNullOrEmpty(c.OriginCsvPath);
                    PanelCommand import;
                    if (!string.IsNullOrEmpty(c.MqoPath))
                        import = new ImportMqoFileCommand(
                            0, c.MqoPath, null, "", "", false, useOrigin, c.OriginCsvPath, false);
                    else
                        import = new ImportPmxFileCommand(
                            0, c.PmxPath, null, true, useOrigin, c.OriginCsvPath, false);

                    var rImport = Dispatch(import);
                    if (rImport == null || !rImport.Success) { Fail("読み込みに失敗"); return true; }

                    var m = project?.CurrentModel;
                    if (m == null) { Fail("読み込み後のカレントモデルが null"); return true; }
                    if (m.DrawableCount == 0) { Fail("読み込み後の描画オブジェクトが 0"); return true; }

                    int targetIdx = c.MasterIndex;
                    if (targetIdx < 0) targetIdx = FindNonSkinnedDrawable(m);
                    if (targetIdx < 0) { Fail("非スキンドの描画オブジェクトが見つからない"); return true; }

                    var mc = m.GetMeshContext(targetIdx);
                    if (mc?.MeshObject == null)
                    { Fail($"masterIndex {targetIdx} に描画オブジェクトが無い"); return true; }

                    int vcount = mc.MeshObject.VertexCount;
                    if (vcount == 0) { Fail("対象の頂点数が 0"); return true; }

                    int poseIdx = c.PoseBoneMasterIndex >= 0 ? c.PoseBoneMasterIndex : targetIdx;
                    var rPose = Dispatch(new SetBonePoseValueCommand(
                        0, new[] { poseIdx },
                        SetBoneTransformValueCommand.Field.RotationZ, c.PoseRotationZ));
                    if (rPose == null || !rPose.Success) { Fail("ポーズ層の付与に失敗"); return true; }

                    // 姿勢が動いた頂点だけを候補にする（VerifyBindMove と同じ理由）。
                    var candidates      = new List<int>();
                    float matrixMaxDiff = 0f;
                    for (int i = 0; i < vcount; i++)
                    {
                        float d = MatrixMaxAbsDiff(mc.VertexMatrix(i, false), mc.VertexMatrix(i, true));
                        if (d > matrixMaxDiff) matrixMaxDiff = d;
                        if (d > c.Tolerance) candidates.Add(i);
                    }
                    if (candidates.Count == 0)
                    {
                        Fail("姿勢が動いている頂点が無い（ポーズの対象と検証対象が噛み合っていない）");
                        return true;
                    }

                    int want = Mathf.Clamp(c.SampleCount, 1, candidates.Count);
                    var targets = new List<int>(want);
                    int step = Mathf.Max(1, candidates.Count / want);
                    for (int i = 0; i < candidates.Count && targets.Count < want; i += step)
                        targets.Add(candidates[i]);

                    int weighted = 0;
                    int posed    = 0;
                    foreach (int vi in targets)
                    {
                        var v = mc.MeshObject.Vertices[vi];
                        if (v != null && v.HasBoneWeight) weighted++;
                        if (MatrixMaxAbsDiff(mc.VertexMatrix(vi, false), mc.VertexMatrix(vi, true)) > c.Tolerance)
                            posed++;
                    }

                    if (!RunOneRotate(project, c, targetIdx, targets, true,
                                      out float bindErr, out float bindLegacyErr, out string bindReason))
                    { Fail("バインド表示での回転に失敗: " + bindReason); return true; }

                    if (!RunOneRotate(project, c, targetIdx, targets, false,
                                      out float poseErr, out float poseLegacyErr, out string poseReason))
                    { Fail("現在ポーズ表示での回転に失敗: " + poseReason); return true; }

                    bool bindDiscriminating = bindLegacyErr > c.Tolerance;
                    bool poseDiscriminating = poseLegacyErr > c.Tolerance;
                    bool discriminating =
                        posed == targets.Count && (bindDiscriminating || poseDiscriminating);
                    bool pass = bindErr <= c.Tolerance && poseErr <= c.Tolerance && discriminating;

                    ReportData(CommandDataJson.New()
                        .Int ("targetIndex",      targetIdx)
                        .Text("targetName",       mc.MeshObject.Name ?? "")
                        .Int ("testedVertices",   targets.Count)
                        .Int ("posedVertices",    posed)
                        .Num ("matrixMaxDiff",    matrixMaxDiff)
                        .Int ("weightedVertices", weighted)
                        .Num ("bindMaxError",     bindErr)
                        .Num ("poseMaxError",     poseErr)
                        .Num ("bindLegacyError",  bindLegacyErr)
                        .Num ("poseLegacyError",  poseLegacyErr)
                        .Flag("bindDiscriminating", bindDiscriminating)
                        .Flag("poseDiscriminating", poseDiscriminating)
                        .Flag("discriminating",   discriminating)
                        .Flag("pass",             pass)
                        .Build());
                    return true;
                }

                case VerifyBindMoveCommand c:
                {
                    var rReset = Dispatch(new ResetProjectCommand("Model"));
                    if (rReset == null || !rReset.Success) { Fail("reset に失敗"); return true; }

                    bool useOrigin = !string.IsNullOrEmpty(c.OriginCsvPath);
                    PanelCommand import;
                    if (!string.IsNullOrEmpty(c.MqoPath))
                        import = new ImportMqoFileCommand(
                            0, c.MqoPath, null, "", "", false, useOrigin, c.OriginCsvPath, false);
                    else
                        import = new ImportPmxFileCommand(
                            0, c.PmxPath, null, true, useOrigin, c.OriginCsvPath, false);

                    var rImport = Dispatch(import);
                    if (rImport == null || !rImport.Success) { Fail("読み込みに失敗"); return true; }

                    var m = project?.CurrentModel;
                    if (m == null) { Fail("読み込み後のカレントモデルが null"); return true; }
                    if (m.DrawableCount == 0) { Fail("読み込み後の描画オブジェクトが 0"); return true; }

                    // ── 対象を決める。-1 のときはウェイトを持たない描画オブジェクトを選ぶ。
                    int targetIdx = c.MasterIndex;
                    if (targetIdx < 0) targetIdx = FindNonSkinnedDrawable(m);
                    if (targetIdx < 0) { Fail("非スキンドの描画オブジェクトが見つからない"); return true; }

                    var mc = m.GetMeshContext(targetIdx);
                    if (mc?.MeshObject == null)
                    { Fail($"masterIndex {targetIdx} に描画オブジェクトが無い"); return true; }

                    int vcount = mc.MeshObject.VertexCount;
                    if (vcount == 0) { Fail("対象の頂点数が 0"); return true; }

                    // ── ポーズ層を先に入れる。検査頂点は「姿勢が動いた頂点」から選ぶので、
                    //    行列がポーズを含んだ状態になっていないと選べない。
                    //    既定では検証対象そのものへ入れる（非スキンドはこれで WorldMatrix
                    //    だけが動き、BindWorldMatrix は動かない）。
                    int poseIdx = c.PoseBoneMasterIndex >= 0 ? c.PoseBoneMasterIndex : targetIdx;
                    var rPose = Dispatch(new SetBonePoseValueCommand(
                        0, new[] { poseIdx },
                        SetBoneTransformValueCommand.Field.RotationZ, c.PoseRotationZ));
                    if (rPose == null || !rPose.Success) { Fail("ポーズ層の付与に失敗"); return true; }

                    // ── 姿勢が動いた頂点だけを候補にする。
                    //    2 つの表示で行列が同じ頂点は、書き戻しが何であっても誤差 0 になる。
                    //    そこを測って「合った」と言ってはならない。
                    var candidates    = new List<int>();
                    float matrixMaxDiff = 0f;
                    for (int i = 0; i < vcount; i++)
                    {
                        float d = MatrixMaxAbsDiff(mc.VertexMatrix(i, false), mc.VertexMatrix(i, true));
                        if (d > matrixMaxDiff) matrixMaxDiff = d;
                        if (d > c.Tolerance) candidates.Add(i);
                    }
                    if (candidates.Count == 0)
                    {
                        Fail("姿勢が動いている頂点が無い（ポーズの対象と検証対象が噛み合っていない）");
                        return true;
                    }

                    int want = Mathf.Clamp(c.SampleCount, 1, candidates.Count);
                    var targets = new List<int>(want);
                    int step = Mathf.Max(1, candidates.Count / want);
                    for (int i = 0; i < candidates.Count && targets.Count < want; i += step)
                        targets.Add(candidates[i]);

                    int weighted = 0;
                    int posed    = 0;
                    foreach (int vi in targets)
                    {
                        var v = mc.MeshObject.Vertices[vi];
                        if (v != null && v.HasBoneWeight) weighted++;
                        if (MatrixMaxAbsDiff(mc.VertexMatrix(vi, false), mc.VertexMatrix(vi, true)) > c.Tolerance)
                            posed++;
                    }

                    var wm = mc.WorldMatrix;
                    var bm = mc.BindWorldMatrix;
                    bool matricesDiffer = MaxAbs(
                        new Vector3(wm.m00 - bm.m00, wm.m01 - bm.m01, wm.m03 - bm.m03)) > c.Tolerance
                        || MaxAbs(new Vector3(wm.m10 - bm.m10, wm.m11 - bm.m11, wm.m13 - bm.m13)) > c.Tolerance;

                    // ── 旧コード（オブジェクト単位の WorldMatrixInverse 直）ならどうなったか。
                    //    旧: 格納値 += WorldMatrixInverse × D
                    //    表示: VertexMatrix(頂点, showBindPose) × 格納値
                    //
                    //    バインド表示は非スキンドで、現在ポーズ表示はスキンドで差が出る。
                    //    スキンド頂点はバインド表示が単位行列なので legacyBind 側は 0 になり、
                    //    そちらだけでは合否を判定できない（理由は VerifyBindMoveCommand の注記）。
                    int head = targets[0];
                    Vector3 legacyLocal = mc.WorldMatrixInverse.MultiplyVector(c.Delta);

                    Vector3 legacyBindWorld = mc.VertexMatrix(head, true).MultiplyVector(legacyLocal);
                    float   legacyBindErr   = MaxAbs(legacyBindWorld - c.Delta);

                    Vector3 legacyPoseWorld = mc.VertexMatrix(head, false).MultiplyVector(legacyLocal);
                    float   legacyPoseErr   = MaxAbs(legacyPoseWorld - c.Delta);

                    if (!RunOneMove(project, c, targetIdx, targets, true,
                                    out float bindErr, out Vector3 bindSample,
                                    out float bindGpuErr, out float bindGap, out string bindReason))
                    { Fail("バインド表示での移動に失敗: " + bindReason); return true; }

                    if (!RunOneMove(project, c, targetIdx, targets, false,
                                    out float poseErr, out Vector3 poseSample,
                                    out float poseGpuErr, out float poseGap, out string poseReason))
                    { Fail("現在ポーズ表示での移動に失敗: " + poseReason); return true; }

                    bool bindDiscriminating = legacyBindErr > c.Tolerance;
                    bool poseDiscriminating = legacyPoseErr > c.Tolerance;
                    bool discriminating =
                        posed == targets.Count && (bindDiscriminating || poseDiscriminating);
                    bool pass = bindErr <= c.Tolerance && poseErr <= c.Tolerance && discriminating;

                    ReportData(CommandDataJson.New()
                        .Int ("targetIndex",          targetIdx)
                        .Text("targetName",           mc.MeshObject.Name ?? "")
                        .Int ("loadedBones",          m.BoneCount)
                        .Int ("loadedDrawables",      m.DrawableCount)
                        .Int ("testedVertices",       targets.Count)
                        .Int ("posedVertices",        posed)
                        .Num ("matrixMaxDiff",        matrixMaxDiff)
                        .Int ("weightedVertices",     weighted)
                        .Nums("worldMatrixT",         new[] { wm.m03, wm.m13, wm.m23 })
                        .Nums("bindWorldMatrixT",     new[] { bm.m03, bm.m13, bm.m23 })
                        .Flag("matricesDiffer",       matricesDiffer)
                        .Nums("bindWorldDelta",       V(bindSample))
                        .Num ("bindMaxError",         bindErr)
                        .Nums("poseWorldDelta",       V(poseSample))
                        .Num ("poseMaxError",         poseErr)
                        .Num ("bindGpuMaxError",      bindGpuErr)
                        .Num ("poseGpuMaxError",      poseGpuErr)
                        .Num ("bindCpuGpuGap",        bindGap)
                        .Num ("poseCpuGpuGap",        poseGap)
                        .Nums("legacyBindWorldDelta", V(legacyBindWorld))
                        .Num ("legacyBindError",      legacyBindErr)
                        .Nums("legacyPoseWorldDelta", V(legacyPoseWorld))
                        .Num ("legacyPoseError",      legacyPoseErr)
                        .Flag("bindDiscriminating",   bindDiscriminating)
                        .Flag("poseDiscriminating",   poseDiscriminating)
                        .Flag("discriminating",       discriminating)
                        .Flag("pass",                 pass)
                        .Build());
                    return true;
                }
            }
            return false;
        }

        /// <summary>【臨時】ウェイトを持たない描画オブジェクトを 1 つ探す。</summary>
        private static int FindNonSkinnedDrawable(ModelContext m)
        {
            for (int i = 0; i < m.Count; i++)
            {
                var mc = m.GetMeshContext(i);
                var mo = mc?.MeshObject;
                if (mo == null || mo.VertexCount == 0 || mo.FaceCount == 0) continue;

                bool anyWeight = false;
                for (int v = 0; v < mo.VertexCount; v++)
                {
                    var vt = mo.Vertices[v];
                    if (vt != null && vt.HasBoneWeight) { anyWeight = true; break; }
                }
                if (!anyWeight) return i;
            }
            return -1;
        }

        /// <summary>
        /// 【臨時】1 つの表示モードで、選択 → 表示ワールド位置の採取 → 移動 → 再採取を行い、
        /// 表示ワールドの差と与えたデルタの最大絶対誤差を返す。
        /// 選択は表示モード切替の「後」に行う（切替で選択が落ちるため）。
        /// </summary>
        private bool RunOneMove(
            ProjectContext project, VerifyBindMoveCommand c,
            int targetIdx, List<int> targets, bool showBindPose,
            out float maxError, out Vector3 sampleDelta,
            out float gpuMaxError, out float cpuGpuGap, out string reason)
        {
            maxError = 0f; sampleDelta = Vector3.zero;
            gpuMaxError = 0f; cpuGpuGap = 0f; reason = null;

            var rMode = Dispatch(new SetPoseDisplayModeCommand(0, showBindPose));
            if (rMode == null || !rMode.Success) { reason = "表示モード切替"; return false; }

            var meshIdx = new int[targets.Count];
            for (int i = 0; i < meshIdx.Length; i++) meshIdx[i] = targetIdx;

            var empty = Array.Empty<int>();
            var rSel = Dispatch(new SelectElementsCommand(
                0, new[] { targetIdx },
                targets.ToArray(), meshIdx,
                empty, empty, empty, empty, empty, empty));
            if (rSel == null || !rSel.Success) { reason = "選択"; return false; }

            var mc = project?.CurrentModel?.GetMeshContext(targetIdx);
            if (mc?.MeshObject == null) { reason = "対象が引けない"; return false; }

            var worldBefore = new Vector3[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                int vi = targets[i];
                worldBefore[i] = mc.VertexMatrix(vi, showBindPose)
                                   .MultiplyPoint3x4(mc.MeshObject.Vertices[vi].Position);
            }

            // GPU が実際に出したワールド座標も控える。CPU の VertexMatrix と
            // 突き合わせるため（規約 10.6 の限界に書いた「GPU と突き合わせていない」を潰す）。
            var gpuBefore = ReadGpuWorld(project, mc);
            if (gpuBefore != null)
                for (int i = 0; i < targets.Count; i++)
                {
                    int vi = targets[i];
                    if (vi < gpuBefore.Length)
                    {
                        float g = Vector3.Distance(worldBefore[i], gpuBefore[vi]);
                        if (g > cpuGpuGap) cpuGpuGap = g;
                    }
                }

            var rMove = Dispatch(new MoveSelectedVerticesCommand(
                0, new[] { targetIdx }, c.Delta,
                MoveSelectedVerticesCommand.CoordSpace.World));
            if (rMove == null || !rMove.Success) { reason = rMove?.Reason ?? "移動"; return false; }

            for (int i = 0; i < targets.Count; i++)
            {
                int vi = targets[i];
                Vector3 after = mc.VertexMatrix(vi, showBindPose)
                                  .MultiplyPoint3x4(mc.MeshObject.Vertices[vi].Position);
                Vector3 d = after - worldBefore[i];
                if (i == 0) sampleDelta = d;

                float err = MaxAbs(d - c.Delta);
                if (err > maxError) maxError = err;
            }

            // GPU 基準でも同じことを測る。こちらが CPU 基準と違えば、
            // CPU の VertexMatrix が GPU の規則と食い違っている。
            var gpuAfter = ReadGpuWorld(project, mc);
            if (gpuBefore != null && gpuAfter != null)
                for (int i = 0; i < targets.Count; i++)
                {
                    int vi = targets[i];
                    if (vi >= gpuBefore.Length || vi >= gpuAfter.Length) continue;
                    float err = MaxAbs((gpuAfter[vi] - gpuBefore[vi]) - c.Delta);
                    if (err > gpuMaxError) gpuMaxError = err;
                }
            return true;
        }

        /// <summary>【臨時】GPU が出したワールド座標を読む。鮮度のため直前に行列を配り直す。</summary>
        private Vector3[] ReadGpuWorld(ProjectContext project, MeshContext mc)
        {
            if (_viewportManager == null || project?.CurrentModel == null || mc == null) return null;
#pragma warning disable CS0618
            _viewportManager.UpdateTransform();
#pragma warning restore CS0618
            return _viewportManager.TryGetMeshWorldPositions(project.CurrentModel, mc, out var arr)
                ? arr : null;
        }

        /// <summary>
        /// 【臨時】1 つの表示モードで、選択 → 表示ワールド位置の採取 → 回転 → 再採取を行う。
        /// ピボットはツールが選択の重心から決めるため指定できない。そこで重心からの
        /// 相対ベクトルが与えた回転で写るかを見る（ピボットに依らない測り方）。
        /// </summary>
        private bool RunOneRotate(
            ProjectContext project, VerifyBindRotateCommand c,
            int targetIdx, List<int> targets, bool showBindPose,
            out float maxError, out float legacyError, out string reason)
        {
            maxError = 0f; legacyError = 0f; reason = null;

            var rMode = Dispatch(new SetPoseDisplayModeCommand(0, showBindPose));
            if (rMode == null || !rMode.Success) { reason = "表示モード切替"; return false; }

            var tgtMc = project?.CurrentModel?.GetMeshContext(targetIdx);
            if (tgtMc?.MeshObject == null) { reason = "対象が引けない"; return false; }

            // RotateSelection は model.SelectedDrawableMeshIndices を走査し、
            // MasterIndices が実行時点の選択と一致することを要求する
            // （PanelCommand.Selection.cs:41-45）。名前で選択を合わせる。
            var rPick = Dispatch(new SelectDrawablesByNameCommand(
                0, new[] { tgtMc.MeshObject.Name ?? "" }));
            if (rPick == null || !rPick.Success) { reason = "描画オブジェクトの選択"; return false; }

            var meshIdx = new int[targets.Count];
            for (int i = 0; i < meshIdx.Length; i++) meshIdx[i] = targetIdx;

            var empty = Array.Empty<int>();
            var rSel = Dispatch(new SelectElementsCommand(
                0, new[] { targetIdx },
                targets.ToArray(), meshIdx,
                empty, empty, empty, empty, empty, empty));
            if (rSel == null || !rSel.Success) { reason = "選択"; return false; }

            var mc = project?.CurrentModel?.GetMeshContext(targetIdx);
            if (mc?.MeshObject == null) { reason = "対象が引けない"; return false; }

            var before = new Vector3[targets.Count];
            var localBefore = new Vector3[targets.Count];
            Vector3 cBefore = Vector3.zero;
            for (int i = 0; i < targets.Count; i++)
            {
                int vi = targets[i];
                localBefore[i] = mc.MeshObject.Vertices[vi].Position;
                before[i] = mc.VertexMatrix(vi, showBindPose)
                              .MultiplyPoint3x4(localBefore[i]);
                cBefore += before[i];
            }
            cBefore /= targets.Count;

            var R0 = Quaternion.Euler(c.RotateEuler);

            // ── 旧コードならどうなったか。
            //    旧: startWorld = WorldMatrix × p、ピボットもその平均、
            //        書き戻しは WorldMatrixInverse。表示は VertexMatrix。
            //    往復が同じ行列なので打ち消し合うことがあり、そのときは差が出ない。
            //    差が出ないなら、この表示モードでは試験が無意味だということ。
            var legacyDisp = new Vector3[targets.Count];
            Vector3 legacyPivot = Vector3.zero;
            var legacyStart = new Vector3[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                legacyStart[i] = mc.WorldMatrix.MultiplyPoint3x4(localBefore[i]);
                legacyPivot += legacyStart[i];
            }
            legacyPivot /= targets.Count;

            Vector3 cLegacy = Vector3.zero;
            for (int i = 0; i < targets.Count; i++)
            {
                Vector3 rotW = legacyPivot + R0 * (legacyStart[i] - legacyPivot);
                Vector3 loc  = mc.WorldMatrixInverse.MultiplyPoint3x4(rotW);
                legacyDisp[i] = mc.VertexMatrix(targets[i], showBindPose).MultiplyPoint3x4(loc);
                cLegacy += legacyDisp[i];
            }
            cLegacy /= targets.Count;

            legacyError = 0f;
            for (int i = 0; i < targets.Count; i++)
            {
                Vector3 want = R0 * (before[i] - cBefore);
                float err = MaxAbs((legacyDisp[i] - cLegacy) - want);
                if (err > legacyError) legacyError = err;
            }

            var rRot = Dispatch(new RotateSelectionCommand(
                0, new[] { targetIdx },
                false, c.RotateEuler, Vector3.up, 0f));
            if (rRot == null || !rRot.Success) { reason = rRot?.Reason ?? "回転"; return false; }

            var after = new Vector3[targets.Count];
            Vector3 cAfter = Vector3.zero;
            for (int i = 0; i < targets.Count; i++)
            {
                int vi = targets[i];
                after[i] = mc.VertexMatrix(vi, showBindPose)
                             .MultiplyPoint3x4(mc.MeshObject.Vertices[vi].Position);
                cAfter += after[i];
            }
            cAfter /= targets.Count;

            var R = Quaternion.Euler(c.RotateEuler);
            for (int i = 0; i < targets.Count; i++)
            {
                Vector3 want = R * (before[i] - cBefore);
                float err = MaxAbs((after[i] - cAfter) - want);
                if (err > maxError) maxError = err;
            }
            return true;
        }

        /// <summary>
        /// 【臨時・簡易】1 つの表示モードでブラシを 1 打ちし、動いた頂点数と
        /// 中心に置いた頂点が動いたかを返す。旧コードなら何頂点入ったかも数える。
        /// 打ち終わったら開始位置へ戻す（もう一方の表示モードを同じ状態から測るため）。
        /// </summary>
        private bool RunOneSculpt(
            ProjectContext project, VerifyBindSculptCommand c,
            int targetIdx, int anchor, bool showBindPose,
            out int hit, out bool anchorMoved, out int legacyHit, out float cpuGpuGap,
            out int gapCount, out float gapMax, out int gapWorst, out string reason)
        {
            hit = 0; anchorMoved = false; legacyHit = 0; cpuGpuGap = 0f;
            gapCount = 0; gapMax = 0f; gapWorst = -1; reason = null;

            var rMode = Dispatch(new SetPoseDisplayModeCommand(0, showBindPose));
            if (rMode == null || !rMode.Success) { reason = "表示モード切替"; return false; }

            var mc = project?.CurrentModel?.GetMeshContext(targetIdx);
            var mo = mc?.MeshObject;
            if (mo == null) { reason = "対象が引けない"; return false; }

            int n = mo.VertexCount;
            var before = new Vector3[n];
            for (int i = 0; i < n; i++) before[i] = mo.Vertices[i].Position;

            // ブラシ中心は「中心に置く頂点の表示ワールド座標」。
            Vector3 center = mc.VertexMatrix(anchor, showBindPose).MultiplyPoint3x4(before[anchor]);

            // ── CPU と GPU の突き合わせは「ブラシを打つ前」に済ませる。
            //    打った後に測ると、RunOneSculpt 末尾の巻き戻しが Vertices[] しか戻さず
            //    _positions には打った結果が残るため、その差を食い違いと読み違える。
            var gpuArr = ReadGpuWorld(project, mc);
            if (gpuArr != null && anchor < gpuArr.Length)
                cpuGpuGap = Vector3.Distance(center, gpuArr[anchor]);

            // 全頂点で CPU の規則と GPU の値を突き合わせる。
            if (gpuArr != null)
                for (int i = 0; i < n && i < gpuArr.Length; i++)
                {
                    Vector3 cpu = mc.VertexMatrix(i, showBindPose).MultiplyPoint3x4(before[i]);
                    float g = Vector3.Distance(cpu, gpuArr[i]);
                    if (g > c.Tolerance) gapCount++;
                    if (g > gapMax) { gapMax = g; gapWorst = i; }
                }

            // 旧コードなら何頂点拾ったか。中心をメッシュ 1 個の行列でローカル化し、
            // ローカル距離で拾う。新しい実装は表示ワールド距離で拾う。
            Vector3 legacyCenter = mc.WorldMatrixInverse.MultiplyPoint3x4(center);
            for (int i = 0; i < n; i++)
                if (Vector3.Distance(before[i], legacyCenter) <= c.BrushRadius) legacyHit++;

            var rSculpt = Dispatch(new SculptStrokeCommand(
                0, new[] { targetIdx }, new[] { center },
                SculptMode.Inflate, c.BrushRadius, c.Strength));
            if (rSculpt == null || !rSculpt.Success) { reason = rSculpt?.Reason ?? "ブラシ"; return false; }

            for (int i = 0; i < n && i < mo.VertexCount; i++)
            {
                if ((mo.Vertices[i].Position - before[i]).sqrMagnitude > 1e-14f)
                {
                    hit++;
                    if (i == anchor) anchorMoved = true;
                }
            }

            // 開始位置へ戻す。Vertices[] だけでなく GPU の _positions も戻す。
            // Vertices[] を戻して InvalidatePositionCache するだけでは、GPU 側には
            // 打った結果が残る。次の表示モードの測定がその汚れを拾う。
            for (int i = 0; i < n && i < mo.VertexCount; i++) mo.Vertices[i].Position = before[i];
            mo.InvalidatePositionCache();
            _viewportManager?.EnterVerticesMoved(project, VerticesMovedPhase.Dragging, mc);
#pragma warning disable CS0618
            _viewportManager?.UpdateTransform();
#pragma warning restore CS0618
            return true;
        }

        /// <summary>
        /// 【臨時】GPU の行列表で「WorldMatrix が入る欄」かどうか。
        /// 判定は UnifiedBufferManager_Update.cs:85-89 と同じにすること。
        /// </summary>
        private static bool UsesWorldMatrixDirect(List<MeshContext> list, int boneIndex, float weight)
        {
            if (weight == 0f) return false;
            if (list == null || boneIndex < 0 || boneIndex >= list.Count) return false;
            var ctx = list[boneIndex];
            if (ctx == null) return false;
            return (ctx.Type == MeshType.Mesh ||
                    ctx.Type == MeshType.MirrorSide ||
                    ctx.Type == MeshType.BakedMirror) && !ctx.IsSkinned;
        }

        /// <summary>【臨時】ウェイトの指す先の型を人が読める形で返す。</summary>
        private static string DescribeBoneSlot(List<MeshContext> list, int boneIndex)
        {
            if (list == null || boneIndex < 0 || boneIndex >= list.Count) return "out-of-range";
            var ctx = list[boneIndex];
            if (ctx == null) return "null";
            string kind = ctx.Type.ToString() + (ctx.IsSkinned ? "/skinned" : "/nonskinned");
            bool direct = (ctx.Type == MeshType.Mesh ||
                           ctx.Type == MeshType.MirrorSide ||
                           ctx.Type == MeshType.BakedMirror) && !ctx.IsSkinned;
            return kind + (direct ? " => GPU:WorldMatrix / CPU:SkinningMatrix" : " => both:SkinningMatrix");
        }

        /// <summary>【臨時】行列を 16 要素の配列へ詰める。</summary>
        private static void AddMat(List<float> dst, Matrix4x4 m)
        {
            for (int i = 0; i < 16; i++) dst.Add(m[i]);
        }

        /// <summary>【臨時】参照ボーンの集計を「索引:名前 (型) ×件数」の並びにする。</summary>
        private static string[] DescribeUse(List<MeshContext> list, Dictionary<int, int> use)
        {
            var keys = new List<int>(use.Keys);
            keys.Sort((a, b) => use[b].CompareTo(use[a]));
            var outp = new List<string>();
            foreach (int k in keys)
            {
                string name = "?", kind = "?";
                if (list != null && k >= 0 && k < list.Count && list[k] != null)
                {
                    name = list[k].MeshObject?.Name ?? "(no name)";
                    kind = list[k].Type.ToString() + (list[k].IsSkinned ? "/skinned" : "/nonskinned");
                }
                outp.Add($"{k}:{name} ({kind}) x{use[k]}");
            }
            return outp.ToArray();
        }

        private static float[] V(Vector3 v) => new float[] { v.x, v.y, v.z };

        private static float MaxAbs(Vector3 v)
            => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));

        /// <summary>【臨時】2 つの行列の要素差の最大絶対値。</summary>
        private static float MatrixMaxAbsDiff(Matrix4x4 a, Matrix4x4 b)
        {
            float m = 0f;
            for (int i = 0; i < 16; i++)
            {
                float d = Mathf.Abs(a[i] - b[i]);
                if (d > m) m = d;
            }
            return m;
        }
    }
}
