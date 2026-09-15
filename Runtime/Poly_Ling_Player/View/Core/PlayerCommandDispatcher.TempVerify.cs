// PlayerCommandDispatcher.TempVerify.cs
// 【臨時】VerifyBindMoveCommand の受け口。検証が済んだらこのファイルごと削除する。
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
                                      out float bindErr, out string bindReason))
                    { Fail("バインド表示での回転に失敗: " + bindReason); return true; }

                    if (!RunOneRotate(project, c, targetIdx, targets, false,
                                      out float poseErr, out string poseReason))
                    { Fail("現在ポーズ表示での回転に失敗: " + poseReason); return true; }

                    bool discriminating = posed == targets.Count && matrixMaxDiff > c.Tolerance;
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
                                    out float bindErr, out Vector3 bindSample, out string bindReason))
                    { Fail("バインド表示での移動に失敗: " + bindReason); return true; }

                    if (!RunOneMove(project, c, targetIdx, targets, false,
                                    out float poseErr, out Vector3 poseSample, out string poseReason))
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
            out float maxError, out Vector3 sampleDelta, out string reason)
        {
            maxError = 0f; sampleDelta = Vector3.zero; reason = null;

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
            return true;
        }

        /// <summary>
        /// 【臨時】1 つの表示モードで、選択 → 表示ワールド位置の採取 → 回転 → 再採取を行う。
        /// ピボットはツールが選択の重心から決めるため指定できない。そこで重心からの
        /// 相対ベクトルが与えた回転で写るかを見る（ピボットに依らない測り方）。
        /// </summary>
        private bool RunOneRotate(
            ProjectContext project, VerifyBindRotateCommand c,
            int targetIdx, List<int> targets, bool showBindPose,
            out float maxError, out string reason)
        {
            maxError = 0f; reason = null;

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
            Vector3 cBefore = Vector3.zero;
            for (int i = 0; i < targets.Count; i++)
            {
                int vi = targets[i];
                before[i] = mc.VertexMatrix(vi, showBindPose)
                              .MultiplyPoint3x4(mc.MeshObject.Vertices[vi].Position);
                cBefore += before[i];
            }
            cBefore /= targets.Count;

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
