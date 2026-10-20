// VmdVrmAnimationExport.cs
// ============================================================
// VMD をモデルへ当てて採取するときの設定と共通の小物
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/VMD/Adapters/ に配置。
//
// ============================================================
// ■ 経路（PolyLing_VRMA整備計画.md 2.5）
// ============================================================
//
//   VMD → .vrma の直接書き出しは廃止した（2026-10-11、段 ④）。
//   VMD はマッスルを持たないので、モデルへ当てながら VmdMotionBake でマッスル＋二次骨＋表情の
//   PolyLing モーションに焼き、.plmotion → .vrma の出口（VrmaCodec）を通す。
//   姿勢の作り方（VMD の適用・IK・T ポーズ整列）は VmdPoseSession にある。
//   このファイルには、その設定（VmdVrmAnimationOptions）と採取時刻・診断の小物だけを置く。
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
//
// ============================================================
// ■ T ポーズ基準の補正（重要・削除禁止）
// ============================================================
//
//   PMX ボーンのレスト・ローカル回転は恒等（PMXImporter.cs の boneModelRotations）。
//   よって A ポーズと T ポーズの差はボーン「位置」だけに出て、
//   レスト・ワールド回転 R0_j は全ボーン恒等になり、
//   骨格が出す D_j = R_j·R0_j⁻¹ は R_j そのものになる。
//   補正しないと、VMD の無回転フレームが T ポーズとして解釈される。
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
//   そこで既定は ArmsOnly（肩・上腕・前腕・手首の 8 本）に絞る（2026-09-26 に None / All を廃止）。
//   選択肢はオプション「脚も合わせる」（AlignLegs：太もも・すねの 4 本を足す）だけ（2026-10-14）。
//   範囲の正本は UnityClipApplier.IsAlignBone。焼き込み・パネル再生がすべてこれを見る。
//   焼き込みと再生は同じ AlignLegs にそろえること。
//   再生は焼き込みの逆（右から A⁻¹）を掛ける（UnityClipApplier の DeltaOf）。
//
// ============================================================
// ■ 既知の制限
// ============================================================
//
//   ミラー枝で作られた半身モデルは、ミラー側の実効ワールドが S·H·S で決まる。
//   VMD のミラー側ボーンのキーは反映されない。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.VMD
{
    /// <summary>VMD 適用側の設定。</summary>
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

        /// <summary>
        /// T ポーズ整列を脚（太もも・すね）にも掛けるか。既定 false（腕 8 本だけ）。
        /// UnityClipApplier.AlignLegs へ渡る。焼き込みと再生で同じ値にそろえること。
        /// </summary>
        public bool AlignLegs = false;

        /// <summary>VMD デルタ位置に掛ける倍率（EditorStateContext.PmxUnityRatio）。</summary>
        public float PositionScale = 1f;

        /// <summary>X 軸反転（EditorStateContext.PmxFlipX）。</summary>
        public bool FlipX = true;

        /// <summary>Z 軸反転（EditorStateContext.PmxFlipZ）。</summary>
        public bool FlipZ = true;

        /// <summary>
        /// 切り分け用のログを出すか。既定 false。
        /// 出すのは開始時 1 回と、フレーム 0・中間フレームの 2 点だけ。
        /// </summary>
        public bool DiagnosticLog = false;

        public static VmdVrmAnimationOptions CreateDefault() => new VmdVrmAnimationOptions();
    }

    /// <summary>VMD の採取時刻と診断の小物。</summary>
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
        /// fpsIn が 0 以下なら 30、endSec が startSec 以下なら VMD の終端まで。
        /// </summary>
        public static float[] SampleTimes(VMDData vmd, float fpsIn, float startSec, float endSec,
                                          out float fps, out float start, out float end)
        {
            fps = fpsIn > 0f ? fpsIn : VmdFps;

            float vmdEnd = ComputeMaxTime(vmd);
            start = Mathf.Max(0f, startSec);
            end   = endSec > start ? endSec : vmdEnd;
            if (end < start) end = start;

            int frameCount = Mathf.Max(1, Mathf.RoundToInt((end - start) * fps) + 1);

            var times = new float[frameCount];
            for (int i = 0; i < frameCount; i++)
                times[i] = start + i / fps;
            return times;
        }

        // ================================================================
        // 診断の小物
        // ================================================================

        internal const float ProbeThresholdDeg = 0.5f;

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
