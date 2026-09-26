// VmdPoseSession.cs
// ============================================================
// VMD をモデルへ適用し、VRMA 用の骨格（UnityClipVrmAnimationSource）へ写す一式
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/VMD/Adapters/ に配置。
//
// ■ 何のためか
//   VMD → VRMA の直接書き出し（VmdVrmAnimationExport）と、
//   VMD → マッスル焼き込み（VmdMotionBake）が同じ姿勢を使うための共通部分。
//   以前は VmdVrmAnimationExport.ExportToFile の中にあったものを、そのまま移した。
//
//   Open   … VMDApplier・VmdNodeWorldSampler・骨格・T ポーズ整列を用意する。
//   Pose   … 1 時刻ぶん VMD を当て（IK 込み）、骨格へ写す。
//   Close  … 骨格を捨て、モデルの VMD 層を必ず戻す。
//
//   T ポーズ整列の考え方と範囲の理由は VmdVrmAnimationExport.cs 冒頭を見ること。
// ============================================================

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Ops;
using Poly_Ling.UnityClip;

namespace Poly_Ling.VMD
{
    public sealed class VmdPoseSession
    {
        public VMDApplier                  Applier { get; private set; }
        public VmdNodeWorldSampler         Sampler { get; private set; }
        public UnityClipVrmAnimationSource Src     { get; private set; }

        private AlignedNodeWorld _getter;

        private VmdPoseSession() { }

        // ================================================================
        // 用意
        // ================================================================

        /// <summary>
        /// 失敗時は null。モデルは Close と同じ後始末まで済ませてから返す。
        /// </summary>
        /// <param name="skeletonScale">骨格の位置に掛ける倍率（VrmAnimationExportSettings.Scale）。</param>
        public static VmdPoseSession Open(
            ModelContext model, VMDData vmd, float skeletonScale,
            VmdVrmAnimationOptions options, out string reason)
        {
            reason = null;
            var s = new VmdPoseSession();

            // VMD 適用側。トレースは出さない。
            // DebugLog は BuildMapping の 1 行を拾うためだけに上げ、
            // フレーム走査に入る前へ必ず戻す（IK の毎フレーム出力を止めるため）。
            // IK の残差を採るときだけトレースを開く。
            // TraceEnabled と TraceDirectory の両方が要る（CCDIKSolver の EnsureSummaryWriter）。
            bool ikTrace = options.EnableIK && !string.IsNullOrEmpty(options.IkTraceDirectory);

            s.Applier = new VMDApplier
            {
                PositionScale             = options.PositionScale,
                CoordinateFlip            = new AxisFlip(options.FlipX, options.FlipZ),
                ApplyCoordinateConversion = options.FlipX || options.FlipZ,
                EnableIK                  = options.EnableIK,
                IgnoreAngleLimits         = options.IgnoreAngleLimits,
                KneePreBend               = options.KneePreBend,
                DebugLog                  = options.DiagnosticLog,
                TraceDirectory            = ikTrace ? options.IkTraceDirectory : null,
                TraceEnabled              = ikTrace,
            };
            s.Applier.BuildMapping(model);

            if (ikTrace)
                Debug.Log($"[VmdVrmAnimationExport] IK 設定: 角度制限を無視={options.IgnoreAngleLimits} / " +
                          $"ひざ事前曲げ={options.KneePreBend}");
            if (ikTrace)
                Debug.Log($"[VmdVrmAnimationExport] IK トレース出力先: {options.IkTraceDirectory} " +
                          $"({CCDIKSolver.SummaryFileName} / {CCDIKSolver.TraceFileName} / " +
                          $"{VMDApplier.TraceFileName})");

            if (options.DiagnosticLog)
            {
                var report = s.Applier.DiagnoseMatching(vmd);
                Debug.Log($"[VmdVrmAnimationExport] マッピング: VMD ボーン {report.MatchedBones.Count} 一致 / " +
                          $"{report.UnmatchedVMDBones.Count} 不一致（{report.BoneMatchRate:P0}）");
                if (report.UnmatchedVMDBones.Count > 0)
                    Debug.Log($"[VmdVrmAnimationExport] 不一致の VMD ボーン(先頭10): " +
                              $"{VmdVrmAnimationExport.Head(report.UnmatchedVMDBones, 10)}");

                Debug.Log($"[VmdVrmAnimationExport] VMD: モデル名 \"{vmd.ModelName}\" / " +
                          $"ボーンキー {vmd.TotalBoneFrameCount} / モーフキー {vmd.TotalMorphFrameCount} / " +
                          $"最終フレーム {vmd.MaxFrameNumber}");
                Debug.Log($"[VmdVrmAnimationExport] VMD ボーントラック名(先頭10): " +
                          $"{VmdVrmAnimationExport.Head(report.MatchedBones, 10)}");
            }

            s.Applier.DebugLog = false;

            s.Sampler = VmdNodeWorldSampler.Build(model, out reason);
            if (s.Sampler == null) { s.Close(model); return null; }

            // レストは BonePoseData を含まない値から取るので、
            // ここでポーズを外す必要はない。
            s.Src = UnityClipVrmAnimationSource.Build(model, s.Sampler.Skeleton, skeletonScale, out reason);
            if (s.Src == null) { s.Close(model); return null; }

            if (options.DiagnosticLog)
            {
                Debug.Log($"[VmdVrmAnimationExport] 骨格: ノード {s.Sampler.Skeleton.Nodes.Count} / " +
                          $"Humanoid 割当 {s.Sampler.Skeleton.HumanoidToNode.Count} / " +
                          $"骨格に載った Humanoid {s.Src.HumanBones.Count}");
                if (s.Src.Dropped.Count > 0)
                    Debug.Log($"[VmdVrmAnimationExport] 載らなかった Humanoid: " +
                              $"{VmdVrmAnimationExport.Head(s.Src.Dropped, 10)}");
            }

            s._getter = new AlignedNodeWorld(s.Sampler, BuildAlign(model, s.Sampler, options));
            return s;
        }

        // 正準 T ポーズへの整列 A をノード索引で引ける形にする。
        // 値と範囲（腕 8 本だけ）は UnityClipApplier が正本。BuildMapping はモデルを書き換えない。
        private static Quaternion[] BuildAlign(
            ModelContext model, VmdNodeWorldSampler sampler, VmdVrmAnimationOptions options)
        {
            var canon = new UnityClipApplier { DebugLog = false };
            canon.BuildMapping(model);

            var align = new Quaternion[sampler.Skeleton.Nodes.Count];
            for (int i = 0; i < align.Length; i++) align[i] = Quaternion.identity;

            var applied = new List<KeyValuePair<string, float>>();
            foreach (var kv in sampler.Skeleton.HumanoidToNode)
            {
                if (kv.Value < 0 || kv.Value >= align.Length) continue;
                if (!canon.TryGetCanonAlignment(kv.Key, out Quaternion a)) continue;
                align[kv.Value] = a;

                float deg = Quaternion.Angle(Quaternion.identity, a);
                if (deg > VmdVrmAnimationExport.ProbeThresholdDeg)
                    applied.Add(new KeyValuePair<string, float>(kv.Key, deg));
            }

            if (options.DiagnosticLog)
                Debug.Log($"[VmdVrmAnimationExport] T ポーズ整列（腕 8 本）: 補正 {applied.Count} 本 / " +
                          $"{VmdVrmAnimationExport.TopAngles(applied, 10)}");
            return align;
        }

        // ================================================================
        // 1 時刻
        // ================================================================

        /// <summary>timeSec 秒の VMD をモデルへ当て（IK 込み）、骨格へ写す。</summary>
        public void Pose(ModelContext model, VMDData vmd, float timeSec)
        {
            // 秒 → VMD フレーム番号。VMD は 30fps 固定。
            Applier.ApplyFrame(model, vmd, timeSec * VmdVrmAnimationExport.VmdFps);
            Sampler.Capture(model);
            Src.PoseFrom(_getter.TryGet);
        }

        // ================================================================
        // 後始末
        // ================================================================

        /// <summary>骨格を捨て、モデルの VMD 層を戻す。何度呼んでもよい。</summary>
        public void Close(ModelContext model)
        {
            if (Src != null) { Src.Dispose(); Src = null; }
            Sampler?.Invalidate();
            if (Applier != null)
            {
                Applier.CloseTrace();
                Applier.ResetAllBones(model);
            }
        }

        // ================================================================
        // 骨格へ渡すノード・ワールド行列
        // ----------------------------------------------------------------
        //   回転だけ R_j·A_j に差し替える。位置はサンプラの値をそのまま通す。
        //   align が null のときは素通し（補正なし）。
        // ================================================================
        private sealed class AlignedNodeWorld
        {
            private readonly VmdNodeWorldSampler _sampler;
            private readonly Quaternion[]        _align;

            public AlignedNodeWorld(VmdNodeWorldSampler sampler, Quaternion[] align)
            {
                _sampler = sampler;
                _align   = align;
            }

            public bool TryGet(int node, out Matrix4x4 world)
            {
                if (_sampler == null || !_sampler.TryGetNodeWorldMatrix(node, out Matrix4x4 w))
                {
                    world = Matrix4x4.identity;
                    return false;
                }

                if (_align == null)
                {
                    world = w;
                    return true;
                }

                Quaternion a = (node >= 0 && node < _align.Length)
                    ? _align[node]
                    : Quaternion.identity;

                world = Matrix4x4.TRS(
                    new Vector3(w.m03, w.m13, w.m23),
                    VmdVrmAnimationExport.ProbeRotation(w) * a,
                    Vector3.one);
                return true;
            }
        }
    }
}
