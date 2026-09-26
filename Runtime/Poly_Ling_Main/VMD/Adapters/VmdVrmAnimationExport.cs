// VmdVrmAnimationExport.cs
// ============================================================
// VMD モーション → VRM アニメーション（.vrma）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/VMD/Adapters/ に配置。
//
// ============================================================
// ■ 2 つの経路（VmdVrmAnimationOptions.ViaMuscles）
// ============================================================
//
//   マッスル経由（既定）
//     VmdMotionBake で VMD＋モデル → マッスル＋RootT/RootQ を作り、
//     UnityClipCanonVrmAnimation.ConvertToFile（Unity クリップ経路と同じ）で書き出す。
//     骨格はモデル非依存の正準骨格になり、VrmAnimationExportSettings.Scale は使わない
//     （Hips の平行移動は受け側が Hips の高さ比で正規化する）。
//
//   直接（ViaMuscles = false）
//     以下「何をするか」の手順で、モデルの骨格のボーン回転をそのまま書き出す。
//
//   姿勢の作り方（VMD の適用・IK・T ポーズ整列）は両経路で共通で、VmdPoseSession にある。
//
// ============================================================
// ■ 何をするか（直接の経路）
// ============================================================
//
//   1. UnityClipVirtualSkeleton から「ボーンだけの GameObject 骨格」を組む
//      （UnityClipVrmAnimationSource.Build。UnityClip 経路と共通）。
//   2. VMDApplier でモデルへ 1 フレームずつ VMD を適用し、
//      VmdNodeWorldSampler でノードのワールド行列を控えて骨格へ写す。
//   3. PLVrmAnimationBridge 経由で .vrma を書き出す。
//
//   骨格の組み方・レスト・出力する量は UnityClip 経路と同じである。
//   違うのは「誰がモデルを動かすか」だけ。
//
// ============================================================
// ■ 単位と座標（恒久メモ）
// ============================================================
//
//   VMD は 30fps 固定（VmdMotionSerializer.cs:29 の VmdFps と同値）。
//   秒 → VMD フレーム番号は t × 30。
//
//   VMDApplier へ渡す PositionScale / CoordinateFlip は
//   EditorStateContext の PmxUnityRatio / PmxFlipX / PmxFlipZ から作る。
//   PlayerVMDTestSubPanel.LoadVMD と同じ規則で、ここでは新しい変換を足さない。
//   VrmAnimationExportSettings.Scale は骨格側（メートル合わせ）にだけ掛かる。
//
// ============================================================
// ■ T ポーズ基準の補正（重要・削除禁止）
// ============================================================
//
//   PMX ボーンのレスト・ローカル回転は恒等（PMXImporter.cs の boneModelRotations）。
//   よって A ポーズと T ポーズの差はボーン「位置」だけに出て、
//   レスト・ワールド回転 R0_j は全ボーン恒等になり、
//   骨格が出す D_j = R_j·R0_j⁻¹ は R_j そのものになる。
//   補正しないと、VMD の無回転フレームが受け側で T ポーズとして解釈される。
//   MMD の無回転は A ポーズなので、腕がその差のぶんずれる。
//
//   そこで骨格へ渡すワールド回転を R_j·A_j に差し替える（VmdPoseSession）。
//     A_j … 正準（T ポーズ）のボーン方向 → モデルの rest 方向 の最短弧。
//            UnityClipApplier.TryGetCanonAlignment が正本。ここでは算出しない。
//   posed_dir = R_j·A_j·(T ポーズ方向) となり、無回転フレームでは A_j だけが残る。
//   位置は素通しにする（Hips の平行移動を壊さないため）。
//
//   A_j を引けないノードは恒等のまま（補正しない）。
//   ここに場当たりのオフセットを足さないこと。合わないときは A_j の出所を直す。
//
// ============================================================
// ■ 補正の範囲を絞る理由（実測・恒久メモ）
// ============================================================
//
//   A_j は「そのボーン 1 本の方向合わせ」しか見ない。
//   腕は上腕の A が支配的で子もほぼ同じ向きを向くため辻褄が合うが、
//   指のように関節ごとに A が違う鎖では、書き出される親相対ローカル
//   A_parent⁻¹·A_j が joint ごとにばらける。
//   上半身は、モデルが元から持つ反りがそのまま A に乗る。
//
//   実測（__AちゃんH）: 全 Humanoid へ掛けると 33 本に 0.5° 超の補正が乗り、
//   最大は RightIndexProximal の 44.87 度。腕は合うが上半身が反り返り、
//   指は根元と先端で逆向きに曲がった。
//
//   そこで既定は ArmsOnly（肩・上腕・前腕・手首の 8 本）に絞る。
//   範囲は固定で、選択肢は置かない（2026-09-26 に None / All を廃止）。
//   範囲の正本は UnityClipApplier.IsArmAlignBone。焼き込み・VRMA・パネル再生がすべてこれを見る。
//   再生は焼き込みの逆（右から A⁻¹）を掛ける（UnityClipApplier の DeltaOf）。
//
// ============================================================
// ■ 既知の制限
// ============================================================
//
//   ミラー枝で作られた半身モデルは、ミラー側の実効ワールドが S·H·S で決まる。
//   VMD のミラー側ボーンのキーは反映されない。
//
//   出力は Hips の平行移動と Humanoid ボーンの回転だけ。
//   表情（モーフ）・視線・二次骨は .vrma に載らない
//   （UniVRM の VrmAnimationExporter がそれしか書かないため）。
//   表情と二次骨まで残したいときは VmdMotionBake で .plmotion.json に出す。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.UnityClip;
using Poly_Ling.Vrm;

namespace Poly_Ling.VMD
{
    /// <summary>
    /// VMD 適用側の設定。VrmAnimationExportSettings（骨格側）とは役割が別。
    /// </summary>
    public class VmdVrmAnimationOptions
    {
        /// <summary>
        /// IK を解いてから採取するか。既定 true。
        /// 2026-09-07 の実測で収束を確認したため既定を有効にした。詳細は CCDIKSolver.cs 冒頭。
        /// </summary>
        public bool EnableIK = true;

        /// <summary>
        /// IK の残差 CSV（vmd_summary.csv / vmd_ik.csv / vmd_trace.csv）の出力先フォルダ。
        /// 空なら出力しない。EnableIK が false のときは無視される。
        ///
        /// 出力は CCDIKSolver / VMDApplier のトレース機構がそのまま行う。
        /// 条件は TraceEnabled と TraceDirectory の両方で、
        /// VMDApplier.PushIkSettings が applier 側の値をソルバへ渡す。
        /// </summary>
        public string IkTraceDirectory = string.Empty;

        /// <summary>
        /// IK の角度制限を無視するか。既定 false。
        /// 収束が止まる原因が角度制限かどうかを切り分けるための実験用。
        /// CCDIKSolver.IgnoreAngleLimits へそのまま渡る。
        /// </summary>
        public bool IgnoreAngleLimits = false;

        /// <summary>
        /// 角度制限を持つリンク（ひざ）を解く前に微小量だけ曲げるか。既定 false。
        /// CCDIKSolver.KneePreBend へそのまま渡る。
        /// IgnoreAngleLimits が true のときソルバ側で無視される。
        /// </summary>
        public bool KneePreBend = false;

        /// <summary>VMD デルタ位置に掛ける倍率（EditorStateContext.PmxUnityRatio）。</summary>
        public float PositionScale = 1f;

        /// <summary>X 軸反転（EditorStateContext.PmxFlipX）。</summary>
        public bool FlipX = true;

        /// <summary>Z 軸反転（EditorStateContext.PmxFlipZ）。</summary>
        public bool FlipZ = true;

        /// <summary>
        /// 切り分け用のログを出すか。既定 false。
        /// 出すのは開始時 1 回と、フレーム 0・中間フレームの 2 点だけ。
        /// フレームごとには出さない。
        /// </summary>
        public bool DiagnosticLog = false;

        /// <summary>
        /// マッスルへ焼き込んでから書き出すか。既定 true。
        /// true … VmdMotionBake でマッスル＋RootT/RootQ を作り、正準骨格経由で書き出す
        ///         （UnityClipCanonVrmAnimation.ConvertToFile。Unity クリップ経路と同じ）。
        /// false … モデルの骨格のボーン回転をそのまま書き出す（従来の経路）。
        /// </summary>
        public bool ViaMuscles = true;

        public static VmdVrmAnimationOptions CreateDefault() => new VmdVrmAnimationOptions();
    }

    /// <summary>
    /// VMD → .vrma の書き出し。モデルのポーズ層は終了時に必ず戻す。
    /// </summary>
    public static class VmdVrmAnimationExport
    {
        /// <summary>VMD の毎秒枚数。規格で 30 固定。</summary>
        public const float VmdFps = 30f;

        /// <summary>VMD の最終キー時刻（秒）。</summary>
        public static float ComputeMaxTime(VMDData vmd)
        {
            if (vmd == null) return 0f;
            return vmd.MaxFrameNumber / VmdFps;
        }

        /// <summary>
        /// 秒の区間と毎秒枚数から採取時刻の列を作る。
        /// fps が 0 以下なら 30、endSec が startSec 以下なら VMD の終端まで。
        /// </summary>
        public static float[] SampleTimes(VMDData vmd, VrmAnimationExportSettings settings,
                                          out float fps, out float start, out float end)
        {
            fps = settings.Fps > 0f ? settings.Fps : VmdFps;

            float vmdEnd = ComputeMaxTime(vmd);
            start = Mathf.Max(0f, settings.StartSec);
            end   = settings.EndSec > start ? settings.EndSec : vmdEnd;
            if (end < start) end = start;

            int frameCount = Mathf.Max(1, Mathf.RoundToInt((end - start) * fps) + 1);

            var times = new float[frameCount];
            for (int i = 0; i < frameCount; i++)
                times[i] = start + i / fps;
            return times;
        }

        /// <summary>
        /// VMD をモデルへ適用しながら .vrma を書き出す。
        /// </summary>
        /// <param name="model">対象モデル。Humanoid 割り当てが要る。</param>
        /// <param name="vmd">適用する VMD。</param>
        /// <param name="outputPath">出力先（実経路）。</param>
        /// <param name="settings">倍率・毎秒枚数・区間。null なら既定値。</param>
        /// <param name="options">VMD 適用側の設定。null なら既定値。</param>
        public static VrmAnimationExportResult ExportToFile(
            ModelContext model,
            VMDData vmd,
            string outputPath,
            VrmAnimationExportSettings settings,
            VmdVrmAnimationOptions options)
        {
            if (model == null) return VrmAnimationExportResult.Failed("モデルがありません");
            if (vmd == null)   return VrmAnimationExportResult.Failed("VMD がありません");
            if (string.IsNullOrEmpty(outputPath))
                return VrmAnimationExportResult.Failed("出力パスが空です");

            settings = settings ?? VrmAnimationExportSettings.CreateDefault();
            options  = options  ?? VmdVrmAnimationOptions.CreateDefault();

            if (!PLVrmAnimationBridge.I.IsAvailable)
                return VrmAnimationExportResult.Failed("VRM アニメーション エクスポータが利用できません");

            if (options.ViaMuscles)
                return ExportViaMuscles(model, vmd, outputPath, settings, options);

            var times = SampleTimes(vmd, settings, out float fps, out float start, out float end);
            int frameCount = times.Length;

            var session = VmdPoseSession.Open(model, vmd, settings.Scale, options, out string openReason);
            if (session == null) return VrmAnimationExportResult.Failed(openReason);

            try
            {
                var src = session.Src;

                // 計測するのはこの 2 点だけ。フレームごとには出さない。
                int probeA = 0;
                int probeB = frameCount / 2;

                var result = PLVrmAnimationBridge.I.Export(
                    src.Root,
                    src.HumanBones,
                    src.Root.transform,
                    times,
                    i =>
                    {
                        session.Pose(model, vmd, times[i]);

                        if (options.DiagnosticLog && (i == probeA || i == probeB))
                            LogProbe(i, times[i] * VmdFps, model, vmd,
                                     session.Applier, session.Sampler, src);
                    },
                    outputPath);

                if (result != null && result.Success)
                {
                    result.HumanoidBoneCount = src.HumanBones.Count;
                    result.FrameCount        = frameCount;
                    result.DurationSec       = end - start;
                    result.OutputPath        = outputPath;
                    if (src.Dropped.Count > 0)
                        result.Warning = "骨格に載せられなかった Humanoid: "
                                       + string.Join(", ", src.Dropped.ToArray());
                }
                return result ?? VrmAnimationExportResult.Failed("書き出し結果がありません");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VmdVrmAnimationExport] {ex}");
                return VrmAnimationExportResult.Failed(ex.Message);
            }
            finally
            {
                session.Close(model);
            }
        }

        // ================================================================
        // マッスル経由
        // ----------------------------------------------------------------
        //   焼き込んだクリップは開始時刻を 0 に寄せてある。
        //   書き出しは焼いた範囲の全体を同じ毎秒枚数で行う。
        // ================================================================
        private static VrmAnimationExportResult ExportViaMuscles(
            ModelContext model, VMDData vmd, string outputPath,
            VrmAnimationExportSettings settings, VmdVrmAnimationOptions options)
        {
            var clip = VmdMotionBake.Bake(model, vmd, settings, options, out var report, out string reason,
                                          withBonesAndExpressions: false);
            if (clip == null) return VrmAnimationExportResult.Failed(reason);

            var canonSettings = new VrmAnimationExportSettings
            {
                Fps      = clip.frameRate,
                StartSec = 0f,
                EndSec   = 0f,
            };
            var result = UnityClipCanonVrmAnimation.ConvertToFile(
                clip, outputPath, canonSettings, VmdMotionBake.CanonBoneLength, true);

            if (result != null && result.Success && report.DroppedHumanoid.Count > 0)
                result.Warning = "骨格に載せられなかった Humanoid: "
                               + string.Join(", ", report.DroppedHumanoid.ToArray());
            return result ?? VrmAnimationExportResult.Failed("書き出し結果がありません");
        }

        // ================================================================
        // 切り分け用の計測
        // ----------------------------------------------------------------
        //   モデル側 … Humanoid ノードのワールド回転が、レスト・ワールド回転から
        //               どれだけ離れたか。VMD がモデルへ届いているかを見る。
        //   骨格側   … 書き出し先ノードのローカル回転が単位からどれだけ離れたか。
        //               モデル → 骨格の写しが効いているかを見る。
        //   両方 0 本ならモデルまで、モデルだけ動いていれば写しまでが原因。
        // ================================================================

        internal const float ProbeThresholdDeg = 0.5f;

        private static void LogProbe(
            int frameIndex, float vmdFrame,
            ModelContext model, VMDData vmd, VMDApplier applier,
            VmdNodeWorldSampler sampler, UnityClipVrmAnimationSource src)
        {
            var skeleton = sampler.Skeleton;

            // ── VMD トラック単位（生値 → デルタ → ワールド の 3 段）────────
            //   生値が 0 なら VMD 自体が無回転。
            //   生値は非 0 でデルタが 0 なら ApplyBonePose までの間。
            //   デルタは非 0 でワールドが 0 なら ComputeWorldMatrices か参照先。
            int shown = 0;
            foreach (var boneName in vmd.BoneNames)
            {
                if (shown++ >= 10) break;

                var (rawPos, rawRot) = vmd.GetBonePoseAtFrame(boneName, vmdFrame);
                float rawDeg = Quaternion.Angle(Quaternion.identity, rawRot);

                int ci = applier.GetBoneIndex(boneName);
                float deltaDeg = -1f;
                float worldDeg = -1f;

                if (ci >= 0 && model.MeshContextList != null && ci < model.MeshContextList.Count)
                {
                    var ctx = model.MeshContextList[ci];
                    var layer = ctx?.BonePoseData?.GetLayer("VMD");
                    if (layer != null)
                        deltaDeg = Quaternion.Angle(Quaternion.identity, layer.DeltaRotation);

                    int node = skeleton.NodeOfContext(ci);
                    if (node >= 0 && sampler.TryGetNodeWorldMatrix(node, out Matrix4x4 nw))
                        worldDeg = Quaternion.Angle(skeleton.RestWorldRotation(model, node), ProbeRotation(nw));
                }

                Debug.Log(
                    $"[VmdVrmAnimationExport] track f={frameIndex} \"{boneName}\" " +
                    $"生 rot {rawDeg:F2}° pos {rawPos.magnitude:F4} / " +
                    $"ctx {ci} / VMD 層 {deltaDeg:F2}° / ワールド {worldDeg:F2}°");
            }

            int   modelMoved = 0;
            float modelMax   = 0f;
            string modelMaxName = "-";

            foreach (var kv in skeleton.HumanoidToNode)
            {
                int node = kv.Value;
                if (!sampler.TryGetNodeWorldMatrix(node, out Matrix4x4 w)) continue;

                Quaternion cur  = ProbeRotation(w);
                Quaternion rest = skeleton.RestWorldRotation(model, node);
                float deg = Quaternion.Angle(rest, cur);

                if (deg > ProbeThresholdDeg) modelMoved++;
                if (deg > modelMax) { modelMax = deg; modelMaxName = kv.Key; }
            }

            int   boneMoved = 0;
            float boneMax   = 0f;
            string boneMaxName = "-";

            foreach (var kv in src.HumanBones)
            {
                var t = kv.Value;
                if (t == null) continue;
                float deg = Quaternion.Angle(Quaternion.identity, t.localRotation);
                if (deg > ProbeThresholdDeg) boneMoved++;
                if (deg > boneMax) { boneMax = deg; boneMaxName = kv.Key.ToString(); }
            }

            Debug.Log(
                $"[VmdVrmAnimationExport] probe f={frameIndex} (vmd {vmdFrame:F1}) " +
                $"モデル側 {modelMoved}/{skeleton.HumanoidToNode.Count} 本が動 " +
                $"(max {modelMax:F2}° {modelMaxName}) / " +
                $"骨格側 {boneMoved}/{src.HumanBones.Count} 本が動 " +
                $"(max {boneMax:F2}° {boneMaxName})");
        }

        // 行列の回転部。列が縮退しているときだけ単位を返す
        // （UnityClipVrmAnimationSource.SafeRotation と同じ規則）。
        internal static Quaternion ProbeRotation(Matrix4x4 m)
        {
            Vector3 fwd = new Vector3(m.m02, m.m12, m.m22);
            Vector3 up  = new Vector3(m.m01, m.m11, m.m21);
            if (fwd.sqrMagnitude <= 1e-12f || up.sqrMagnitude <= 1e-12f)
                return Quaternion.identity;
            return m.rotation;
        }

        // 角度の大きい順に n 件を "名前 12.34°, …" の形へ。
        internal static string TopAngles(List<KeyValuePair<string, float>> list, int n)
        {
            if (list == null || list.Count == 0) return "(なし)";

            var sorted = new List<KeyValuePair<string, float>>(list);
            sorted.Sort((x, y) => y.Value.CompareTo(x.Value));

            int take = Mathf.Min(n, sorted.Count);
            var buf = new string[take];
            for (int i = 0; i < take; i++)
                buf[i] = $"{sorted[i].Key} {sorted[i].Value:F2}°";

            string s = string.Join(", ", buf);
            return sorted.Count > take ? s + $" … 他 {sorted.Count - take} 件" : s;
        }

        internal static string Head(List<string> list, int n)
        {
            if (list == null || list.Count == 0) return "(なし)";
            int take = Mathf.Min(n, list.Count);
            var buf = new string[take];
            for (int i = 0; i < take; i++) buf[i] = list[i];
            string s = string.Join(", ", buf);
            return list.Count > take ? s + $" … 他 {list.Count - take} 件" : s;
        }
    }
}
