// UnityClipRootMotion.cs
// ============================================================
// Humanoid クリップの Root 情報（RootT / RootQ）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/UnityClip/ に配置。
//
// ============================================================
// ■ PolyLing モーションでの置き場所（2026-10-06 決定）
// ============================================================
//
//   Root は MotionClipDTO.body トラック（targetKind "body"）に置く。
//     key.pos = HumanPose.bodyPosition（RootT）
//     key.rot = HumanPose.bodyRotation（RootQ）
//   muscles に "RootT.x" などの名前で入れてはならない（MotionClipSerializer が拒否する）。
//   理由：成分ごとのスカラー補間では四元数が単位長を保たない。body トラックなら
//   回転は MotionCurveMath.TryEvaluateRotation（Slerp／正規化つき）で評価される。
//   ライブ（MotionLiveJson）の rootT / rootQ とも 1 対 1 に対応する。
//
//   下の NameTx などは Unity の AnimationClip が持つ Animator カーブの名前。
//   AnimationClip から読み取るとき（Editor の書き出し）と、検査で旧表現を拒否するときだけに使う。
//
// ============================================================
// ■ RootT / RootQ とは何か（実物で確認した事実）
// ============================================================
//
//   Humanoid の AnimationClip は Animator 型のカーブとして
//     RootT.x / RootT.y / RootT.z
//     RootQ.x / RootQ.y / RootQ.z / RootQ.w
//   を持つ。これは HumanPose の
//     bodyPosition（RootT）/ bodyRotation（RootQ）
//   に対応する。UniVRM の UniHumanoid.AnimationClipUtility が
//   pose.bodyPosition を "RootT.x/y/z"、pose.bodyRotation を "RootQ.x/y/z/w" として
//   SetCurve している（com.vrmc.gltf/Runtime/UniHumanoid/AnimationClipUtility.cs:63-114）。
//
//   これらは HumanTrait.MuscleName に**含まれない**。
//   含まれないことは「不要」を意味しない。マッスルが関節の正規化自由度であるのに対し、
//   RootT / RootQ は身体全体の位置と向きで、別系統の情報である。
//
// ============================================================
// ■ RootT の単位（ここを外すと移動量が合わない）
// ============================================================
//
//   bodyPosition は humanScale で正規化された無次元量で、メートルにするには
//   animator.humanScale を掛ける（Unity Discussions
//   "HumanPose bodyPosition ... is drifting away" スレッドの
//   worldPosition = humanPose.bodyPosition * animator.humanScale）。
//
//   実測でも裏づけがある。WALK00_F.json の t=0 は
//     RootT = (0.00323, 0.97853, 0.00583)
//   で、y が 1 近傍。メートルではなく「腰高を 1 とする正規化量」である。
//
//   この変換器はモデルを持たないので humanScale を知らない。
//   代わりに、正準骨格の Hips レスト高（＝骨長）を基準にして
//     倍率 = Hips レスト高 / RootT.y(基準時刻)
//   を掛ける。こうすると基準時刻で Hips がちょうどレスト高に載り、
//   以後の移動量は「自分の腰高の何倍か」で表される。
//
//   VRMA の受け側は Hips の平行移動を
//     scaling_factor = y_dst / y_src（src はファイル内レスト姿勢の腰高）
//   で読み替える（VRM 仕様 Humanoid Animation Retargeting）。
//   src はこの正準骨格のレスト高そのものなので、上の倍率と噛み合う。
//
// ============================================================
// ■ RootQ の基準
// ============================================================
//
//   bodyRotation はアバター根から見た身体の向きで、素立ちではほぼ単位。
//   したがって差分を取らず、そのまま身体の向きとして使う。
//
//   ただし w の符号は反転しうる。VRMA の受け側はフレーム間を補間するので、
//   前フレームと内積が負なら符号を揃える（回転は変わらない）。
//
// ============================================================

using UnityEngine;
using Poly_Ling.Motion;

namespace Poly_Ling.UnityClip
{
    /// <summary>
    /// クリップの Root（body トラック）。マッスルとは別系統として扱う。
    /// </summary>
    public sealed class UnityClipRootMotion
    {
        // Unity の AnimationClip の Animator カーブ名（PolyLing モーションの muscles には入れない）
        public const string NameTx = "RootT.x";
        public const string NameTy = "RootT.y";
        public const string NameTz = "RootT.z";
        public const string NameQx = "RootQ.x";
        public const string NameQy = "RootQ.y";
        public const string NameQz = "RootQ.z";
        public const string NameQw = "RootQ.w";

        /// <summary>Unity の Animator カーブの Root 名か。</summary>
        public static bool IsRootCurveName(string name)
            => name == NameTx || name == NameTy || name == NameTz
            || name == NameQx || name == NameQy || name == NameQz || name == NameQw;

        private MotionTrackDTO _body;

        /// <summary>位置（RootT）を持つキーが 1 つでもあるか。</summary>
        public bool HasTranslation { get; private set; }

        /// <summary>回転（RootQ）を持つキーが 1 つでもあるか。</summary>
        public bool HasRotation { get; private set; }

        private UnityClipRootMotion() { }

        /// <summary>
        /// body トラックから作る。null・キー無しなら「持たない」インスタンスを返す（null は返さない）。
        /// </summary>
        public static UnityClipRootMotion From(MotionTrackDTO body)
        {
            var r = new UnityClipRootMotion();
            if (body?.keys == null || body.keys.Count == 0) return r;
            r._body = body;
            foreach (var k in body.keys)
            {
                if (k == null) continue;
                if (k.pos != null && k.pos.Length == 3) r.HasTranslation = true;
                if (k.rot != null && k.rot.Length == 4) r.HasRotation = true;
            }
            return r;
        }

        // ================================================================
        // サンプリング
        // ================================================================

        /// <summary>正規化されたままの RootT。メートルではない。倍率は呼び出し側で掛ける。無ければ 0。</summary>
        public Vector3 SampleNormalizedTranslation(float timeSec)
            => HasTranslation
               && MotionCurveMath.TryEvaluateVector3(_body, MotionCurveMath.VectorChannel.Position, timeSec, out var v)
                ? v : Vector3.zero;

        /// <summary>RootQ。無いとき・長さが 0 に近いときは単位を返す（不正値で骨格を壊さない）。</summary>
        public Quaternion SampleRotation(float timeSec)
        {
            if (!HasRotation || !MotionCurveMath.TryEvaluateRotation(_body, timeSec, out var q))
                return Quaternion.identity;
            return NormalizeSafe(q);
        }

        /// <summary>長さで割る。0 長・NaN・無限大は単位へ落とす。</summary>
        public static Quaternion NormalizeSafe(Quaternion q)
        {
            float sq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (float.IsNaN(sq) || float.IsInfinity(sq) || sq < 1e-8f) return Quaternion.identity;

            float inv = 1f / Mathf.Sqrt(sq);
            return new Quaternion(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        /// <summary>
        /// prev と内積が負なら符号を反転して連続にする。回転そのものは変わらない。
        /// フレーム間を補間する受け側のために揃える。
        /// </summary>
        public static Quaternion MatchSign(Quaternion q, Quaternion prev)
        {
            float dot = q.x * prev.x + q.y * prev.y + q.z * prev.z + q.w * prev.w;
            if (dot >= 0f) return q;
            return new Quaternion(-q.x, -q.y, -q.z, -q.w);
        }

        // ================================================================
        // 倍率
        // ================================================================

        /// <summary>
        /// 正規化 RootT を正準骨格の寸法へ移す倍率。
        /// 基準時刻の RootT.y が Hips レスト高に一致するように決める。
        ///
        /// 決められないとき（RootT が無い・y が 0 近傍）は false。
        /// そのときは平行移動を載せないこと。基準が無いまま掛けると寸法が化ける。
        /// </summary>
        public bool TryComputeTranslationScale(
            float restHipsHeight, float referenceTimeSec, out float scale, out string note)
        {
            scale = 1f;

            if (!HasTranslation) { note = "RootT が無い"; return false; }

            float y0 = SampleNormalizedTranslation(referenceTimeSec).y;
            if (Mathf.Abs(y0) < 1e-4f)
            {
                note = $"基準時刻の RootT.y が 0 近傍（{y0:F5}）なので倍率を決められない";
                return false;
            }

            if (restHipsHeight <= 0f)
            { note = $"Hips のレスト高が 0 以下（{restHipsHeight:F5}）"; return false; }

            scale = restHipsHeight / y0;
            note  = $"倍率 = Hips レスト高 {restHipsHeight:F4} ÷ RootT.y({referenceTimeSec:F3}s)={y0:F5} → {scale:F5}";
            return true;
        }

        /// <summary>ログ用。基準時刻の値を 1 行で返す。</summary>
        public string Describe(float timeSec)
        {
            if (!HasTranslation && !HasRotation) return "body トラックなし";

            var t = SampleNormalizedTranslation(timeSec);
            var q = SampleRotation(timeSec);

            return $"t={timeSec:F3}s RootT(正規化)=({t.x:F5}, {t.y:F5}, {t.z:F5})"
                 + $" RootQ=({q.x:F5}, {q.y:F5}, {q.z:F5}, {q.w:F5})"
                 + $" [{(HasTranslation ? "T あり" : "T なし")}/{(HasRotation ? "Q あり" : "Q なし")}]";
        }
    }
}
