// VmdMotionBake.cs
// ============================================================
// VMD ＋ モデル → PolyLing モーション（マッスル＋ボーン＋表情）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/VMD/Adapters/ に配置。
//
// ============================================================
// ■ 何をするか
// ============================================================
//
//   1 フレームごとに、
//     ・VmdPoseSession で VMD をモデルへ当て（IK・T ポーズ整列込み）、VRMA 用の骨格へ写す。
//       ここまでは VMD → VRMA の直接経路と同じ姿勢である。
//     ・骨格の Humanoid ボーンのワールド回転を、正準骨格（CanonHumanPoseRig）へ
//       親から順に代入する。どちらもレストのワールド回転が単位なので、
//       同じワールド回転を入れればレスト相対の回転がそのまま移る。
//       モデルに無い Humanoid ボーンはローカル単位（親に付いていくだけ）。
//     ・Hips の位置は、Hips のレスト高さの比で正準骨格の寸法へ直す。
//     ・HumanPoseHandler.GetHumanPose で 95 マッスルと RootT / RootQ を取る
//       （Editor の UnityClipExportWindow のベイクと同じ取り方）。
//
//   ボーン（二次骨）… Humanoid でも IK ボーンでもなく、子孫に Humanoid ボーンを持たないもの
//     （髪・スカート等）だけを、モデルのワールド行列からレスト相対のローカルに直して書く。
//     子孫に Humanoid を持つもの（センター・グルーブ・腕捩など）は、その回転が
//     Humanoid ボーンのワールド回転に既に入っているので書かない（書くと二重になる）。
//     全フレームでレストのままのボーンは書かない。
//     位置は Unity 単位（モデル空間の値）。統合モーションパネルは .plmotion を倍率 1 で読む
//     （MotionClipHandler.Load）。
//
//   表情 … VMDApplier はモーフをモデルへ当てない（ApplyMorphWeight が未実装）ので、
//     VMD のモーフキーを MotionClipConverters.ExpressionsFromVMD で写す。
//
//   時刻は区間の開始を 0 秒に寄せる。キーはすべて線形（接線なし）。
//
// ============================================================
// ■ 切り分けログ（DiagnosticLog）
// ============================================================
//
//   焼いたモーションを統合モーションの再生経路（MotionClipApplier）で同じモデルへ当て、
//   VMD を直接当てた姿勢と比べる。比べるのは Humanoid ボーンの「Hips から見たワールド回転」。
//   フレーム 0 と中間の 2 点だけ。焼き込みと再生の整列（腕 8 本・右掛け）はそろえてあるので、
//   残る差はマッスル表現そのものが落とす分（親指・前腕ねじりなど。Unity の仕様）である。
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Motion;
using Poly_Ling.Ops;
using Poly_Ling.UnityClip;
using Poly_Ling.Vrm;

namespace Poly_Ling.VMD
{
    /// <summary>焼き込みの結果の数と診断。</summary>
    public sealed class VmdMotionBakeReport
    {
        /// <summary>骨格に載せられなかった Humanoid 名。</summary>
        public readonly List<string> DroppedHumanoid = new List<string>();

        public int FrameCount;
        public int BoneTrackCount;
        public int ExpressionTrackCount;

        /// <summary>切り分けログを出したときの、再生経路との最大角度差（度）。出していなければ -1。</summary>
        public float MaxPlaybackDiffDeg = -1f;
        public string MaxPlaybackDiffBone = "";
    }

    public static class VmdMotionBake
    {
        /// <summary>
        /// 正準骨格の骨長。マッスルと RootT は正規化量なので値に依らないが、
        /// VRMA 変換（UnityClipCanonVrmAnimation.ConvertToFile）と同じ値にそろえておく。
        /// </summary>
        public const float CanonBoneLength = 0.1f;

        /// <summary>
        /// VMD をモデルへ当てながらマッスル＋ボーン＋表情のモーションを作る。
        /// モデルの VMD 層は終了時に必ず戻す。失敗時は null と理由。
        /// </summary>
        /// <param name="settings">毎秒枚数と区間。Scale は使わない。null なら既定値。</param>
        /// <param name="options">VMD 適用側の設定。null なら既定値。</param>
        /// <param name="withBonesAndExpressions">false ならマッスルと Root だけ作る（VRMA 用）。</param>
        public static MotionClipDTO Bake(
            ModelContext model, VMDData vmd,
            VrmAnimationExportSettings settings, VmdVrmAnimationOptions options,
            out VmdMotionBakeReport report, out string reason,
            bool withBonesAndExpressions = true)
        {
            report = new VmdMotionBakeReport();
            reason = null;
            if (model == null) { reason = "モデルがありません"; return null; }
            if (vmd == null)   { reason = "VMD がありません";   return null; }

            settings = settings ?? VrmAnimationExportSettings.CreateDefault();
            options  = options  ?? VmdVrmAnimationOptions.CreateDefault();

            var times = VmdVrmAnimationExport.SampleTimes(vmd, settings, out float fps, out float start, out float end);

            var session = VmdPoseSession.Open(model, vmd, 1f, options, out reason);
            if (session == null) return null;

            var rig = new CanonHumanPoseRig();
            try
            {
                if (!rig.Build(CanonBoneLength, out reason)) return null;

                var src = session.Src;
                report.DroppedHumanoid.AddRange(src.Dropped);

                if (!src.HumanBones.TryGetValue(HumanBodyBones.Hips, out var srcHips) || srcHips == null)
                { reason = "Hips が骨格にありません"; return null; }
                if (!rig.Skeleton.HumanBones.TryGetValue(HumanBodyBones.Hips, out var canonHips) || canonHips == null)
                { reason = "正準骨格に Hips がありません"; return null; }

                // レスト高さの比。src は Open 直後でまだ姿勢を写していないのでレスト位置のまま。
                float srcHipsY   = srcHips.position.y;
                float canonHipsY = rig.Skeleton.HipsRestLocalPosition.y;
                if (srcHipsY <= 1e-6f)
                { reason = $"Hips のレスト高さが {srcHipsY:G4} のため、正準骨格の寸法へ合わせられません"; return null; }
                float hipsRatio = canonHipsY / srcHipsY;

                int mc = HumanTrait.MuscleCount;
                var muscleNames = HumanTrait.MuscleName;
                var muscleTracks = new MotionScalarTrackDTO[mc];
                for (int m = 0; m < mc; m++) muscleTracks[m] = new MotionScalarTrackDTO { name = muscleNames[m] };

                string[] rootNames =
                {
                    UnityClipRootMotion.NameTx, UnityClipRootMotion.NameTy, UnityClipRootMotion.NameTz,
                    UnityClipRootMotion.NameQx, UnityClipRootMotion.NameQy, UnityClipRootMotion.NameQz, UnityClipRootMotion.NameQw,
                };
                var rootTracks = new MotionScalarTrackDTO[rootNames.Length];
                for (int k = 0; k < rootNames.Length; k++) rootTracks[k] = new MotionScalarTrackDTO { name = rootNames[k] };

                var boneSampler = withBonesAndExpressions
                    ? new SecondaryBoneSampler(model, session.Sampler.Skeleton)
                    : null;

                var pose = new HumanPose();
                Quaternion prevQ = Quaternion.identity;
                bool hasPrev = false;
                int probeB = times.Length / 2;
                var probes = new List<(float t, Dictionary<string, Quaternion> rel)>();

                for (int f = 0; f < times.Length; f++)
                {
                    float tLocal = times[f] - start;
                    session.Pose(model, vmd, times[f]);

                    // 正準骨格へ写す（親から順）
                    foreach (var kv in rig.Ordered)
                    {
                        if (src.HumanBones.TryGetValue(kv.Key, out var st) && st != null)
                            kv.Value.rotation = st.rotation;
                        else
                            kv.Value.localRotation = Quaternion.identity;
                    }
                    canonHips.localPosition = srcHips.position * hipsRatio;

                    if (!rig.GetHumanPose(ref pose)) { reason = "HumanPoseHandler が使えません"; return null; }

                    for (int m = 0; m < mc && m < pose.muscles.Length; m++)
                        muscleTracks[m].keys.Add(new MotionScalarKeyDTO { t = tLocal, v = pose.muscles[m] });

                    Quaternion q = pose.bodyRotation;
                    if (hasPrev && Quaternion.Dot(prevQ, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    prevQ = q; hasPrev = true;
                    float[] rv = { pose.bodyPosition.x, pose.bodyPosition.y, pose.bodyPosition.z, q.x, q.y, q.z, q.w };
                    for (int k = 0; k < rv.Length; k++) rootTracks[k].keys.Add(new MotionScalarKeyDTO { t = tLocal, v = rv[k] });

                    boneSampler?.Sample(model, tLocal);

                    if (options.DiagnosticLog && (f == 0 || f == probeB))
                        probes.Add((tLocal, CaptureHipsRelative(session.Sampler)));
                }

                var dto = new MotionClipDTO
                {
                    name      = vmd.ModelName,
                    frameRate = fps,
                    duration  = end - start,
                    space     = "local",
                    loop      = false,
                    metadata  = new MotionMetadataDTO { createdWith = "PolyLing VmdMotionBake" },
                };
                dto.muscles.AddRange(muscleTracks);
                dto.muscles.AddRange(rootTracks);

                if (withBonesAndExpressions)
                {
                    dto.bones.AddRange(boneSampler.BuildTracks());
                    dto.expressions.AddRange(MotionClipConverters.ExpressionsFromVMD(vmd, start, end));
                }

                report.FrameCount           = times.Length;
                report.BoneTrackCount       = dto.bones.Count;
                report.ExpressionTrackCount = dto.expressions.Count;

                if (options.DiagnosticLog && probes.Count > 0)
                    LogPlaybackCheck(model, dto, session, probes, report);

                Debug.Log($"[VmdMotionBake] {times.Length} フレーム / マッスル {mc} ＋ Root 7 / " +
                          $"ボーン {dto.bones.Count} / 表情 {dto.expressions.Count} / " +
                          $"Hips 高さ比 {hipsRatio:G4}" +
                          (report.DroppedHumanoid.Count > 0
                              ? $" / 載らなかった Humanoid: {string.Join(", ", report.DroppedHumanoid.ToArray())}"
                              : ""));
                return dto;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VmdMotionBake] {ex}");
                reason = ex.Message;
                return null;
            }
            finally
            {
                rig.Dispose();
                session.Close(model);
            }
        }

        // ================================================================
        // 再生経路との比較（切り分けログ）
        // ================================================================

        // Humanoid ノードのワールド回転を Hips から見た値で控える（名前 → 回転）。
        private static Dictionary<string, Quaternion> CaptureHipsRelative(VmdNodeWorldSampler sampler)
        {
            var result = new Dictionary<string, Quaternion>();
            var sk = sampler.Skeleton;
            int hipsNode = sk.NodeOfHumanoid("Hips");
            if (hipsNode < 0 || !sampler.TryGetNodeWorldMatrix(hipsNode, out Matrix4x4 hw)) return result;
            Quaternion hipsInv = Quaternion.Inverse(VmdVrmAnimationExport.ProbeRotation(hw));
            foreach (var kv in sk.HumanoidToNode)
            {
                if (!sampler.TryGetNodeWorldMatrix(kv.Value, out Matrix4x4 w)) continue;
                result[kv.Key] = hipsInv * VmdVrmAnimationExport.ProbeRotation(w);
            }
            return result;
        }

        // 焼いたクリップを MotionClipApplier で当て、VMD を当てたときの姿勢と比べる。
        // モデルのポーズ層は最後に戻す（VMD 層は呼び出し側の Close が戻す）。
        private static void LogPlaybackCheck(
            ModelContext model, MotionClipDTO dto, VmdPoseSession session,
            List<(float t, Dictionary<string, Quaternion> rel)> probes, VmdMotionBakeReport report)
        {
            // VMD 層を外してから再生経路を当てる
            session.Applier.ResetAllBones(model);

            var applier = new MotionClipApplier();
            try
            {
                applier.SetClip(dto);
                applier.BuildMapping(model);
                foreach (var probe in probes)
                {
                    applier.ApplyFrame(model, probe.t);
                    session.Sampler.Capture(model);
                    var played = CaptureHipsRelative(session.Sampler);

                    var diffs = new List<KeyValuePair<string, float>>();
                    foreach (var kv in probe.rel)
                    {
                        if (kv.Key == "Hips" || !played.TryGetValue(kv.Key, out var q)) continue;
                        float deg = Quaternion.Angle(kv.Value, q);
                        diffs.Add(new KeyValuePair<string, float>(kv.Key, deg));
                        if (deg > report.MaxPlaybackDiffDeg) { report.MaxPlaybackDiffDeg = deg; report.MaxPlaybackDiffBone = kv.Key; }
                    }
                    Debug.Log($"[VmdMotionBake] 再生経路との差 t={probe.t:F3}s（VMD 直接 ⇔ 焼いたモーションの再生、Hips 相対ワールド）" +
                              $"{diffs.Count} 本: {VmdVrmAnimationExport.TopAngles(diffs, 12)}");
                }
            }
            finally
            {
                applier.ResetAllBones(model);
            }
        }

        // ================================================================
        // 二次骨
        // ================================================================
        private sealed class SecondaryBoneSampler
        {
            private readonly List<int>                 _ctx   = new List<int>();
            private readonly List<string>              _name  = new List<string>();
            private readonly List<List<MotionKeyDTO>>  _keys  = new List<List<MotionKeyDTO>>();
            private readonly List<bool>                _moved = new List<bool>();

            public SecondaryBoneSampler(ModelContext model, UnityClipVirtualSkeleton skeleton)
            {
                var list = model.MeshContextList;
                if (list == null) return;

                // Humanoid に割り当てられたコンテキスト（実体・ミラー側の両方）
                var humanoid = new HashSet<int>();
                foreach (var kv in skeleton.HumanoidToNode)
                {
                    int node = kv.Value;
                    if (node < 0 || node >= skeleton.Nodes.Count) continue;
                    var n = skeleton.Nodes[node];
                    if (n.ContextIndex       >= 0) humanoid.Add(n.ContextIndex);
                    if (n.SourceContextIndex >= 0) humanoid.Add(n.SourceContextIndex);
                }

                // 子孫に Humanoid を持つコンテキスト（Humanoid の祖先）
                var ancestor = new HashSet<int>();
                foreach (int ci in humanoid)
                {
                    int guard = list.Count + 1;
                    int p = (ci >= 0 && ci < list.Count && list[ci] != null) ? list[ci].HierarchyParentIndex : -1;
                    while (p >= 0 && p < list.Count && guard-- > 0 && ancestor.Add(p))
                        p = list[p] != null ? list[p].HierarchyParentIndex : -1;
                }

                var usedNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in model.Bones)
                {
                    int ci = entry.MasterIndex;
                    if (ci < 0 || ci >= list.Count) continue;
                    var ctx = list[ci];
                    if (ctx == null) continue;
                    if (humanoid.Contains(ci) || ancestor.Contains(ci)) continue;
                    if (ctx.IsIK) continue;
                    if (IsMirrorSide(ctx)) continue;
                    int pi = ctx.HierarchyParentIndex;
                    if (pi >= 0 && pi < list.Count && list[pi] != null && IsMirrorSide(list[pi])) continue;
                    string name = ctx.Name;
                    if (string.IsNullOrEmpty(name) || !usedNames.Add(name)) continue;

                    _ctx.Add(ci);
                    _name.Add(name);
                    _keys.Add(new List<MotionKeyDTO>());
                    _moved.Add(false);
                }
            }

            private static bool IsMirrorSide(MeshContext ctx)
                => ctx.MirrorGeometryDerived && MirrorBranchOps.IsMirrorSideContext(ctx);

            /// <summary>現在のモデル姿勢から、レスト相対のローカル（BonePoseData と同じ意味）を控える。</summary>
            public void Sample(ModelContext model, float t)
            {
                var list = model.MeshContextList;
                for (int i = 0; i < _ctx.Count; i++)
                {
                    var ctx = list[_ctx[i]];
                    int pi = ctx.HierarchyParentIndex;
                    Matrix4x4 parentWorld = (pi >= 0 && pi < list.Count && list[pi] != null)
                        ? list[pi].WorldMatrix
                        : Matrix4x4.identity;

                    // LocalMatrix = BindLocal · Pose なので Pose = BindLocal⁻¹ · 親ワールド⁻¹ · ワールド
                    Matrix4x4 d = ctx.BindLocalMatrix.inverse * parentWorld.inverse * ctx.WorldMatrix;
                    Vector3    pos = new Vector3(d.m03, d.m13, d.m23);
                    Quaternion rot = VmdVrmAnimationExport.ProbeRotation(d);

                    var keys = _keys[i];
                    if (keys.Count > 0)
                    {
                        var pr = keys[keys.Count - 1].rot;
                        if (pr[0] * rot.x + pr[1] * rot.y + pr[2] * rot.z + pr[3] * rot.w < 0f)
                            rot = new Quaternion(-rot.x, -rot.y, -rot.z, -rot.w);
                    }
                    keys.Add(new MotionKeyDTO
                    {
                        t   = t,
                        pos = new[] { pos.x, pos.y, pos.z },
                        rot = new[] { rot.x, rot.y, rot.z, rot.w },
                    });

                    if (!_moved[i] && (pos.sqrMagnitude > 1e-10f || Quaternion.Angle(Quaternion.identity, rot) > 1e-3f))
                        _moved[i] = true;
                }
            }

            /// <summary>一度でもレストから動いたボーンだけをトラックにする。</summary>
            public List<MotionTrackDTO> BuildTracks()
            {
                var tracks = new List<MotionTrackDTO>();
                for (int i = 0; i < _ctx.Count; i++)
                {
                    if (!_moved[i]) continue;
                    var tr = new MotionTrackDTO { id = _name[i], targetKind = "boneName" };
                    tr.keys.AddRange(_keys[i]);
                    tracks.Add(tr);
                }
                return tracks;
            }
        }
    }
}
