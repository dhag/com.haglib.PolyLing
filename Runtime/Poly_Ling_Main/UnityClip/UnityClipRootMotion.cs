// UnityClipRootMotion.cs
// ============================================================
// Humanoid クリップの Root 情報（RootT / RootQ）
// ------------------------------------------------------------
// Runtime/Poly_Ling_Main/UnityClip/ に配置。
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
//   実測でも WALK00_F.json の t=0 は
//     RootQ = (-0.01459, 0.00839, 0.00008, -0.99986)
//   で、符号違いの単位近傍（q と -q は同じ回転）。
//   したがって差分を取らず、そのまま身体の向きとして使う。
//
//   ただし w の符号は反転しうる。VRMA の受け側はフレーム間を補間するので、
//   前フレームと内積が負なら符号を揃える（回転は変わらない）。
//
// ============================================================

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.UnityClip
{
    /// <summary>
    /// クリップの Root 系トラック（RootT / RootQ）。マッスルとは別系統として扱う。
    /// </summary>
    public sealed class UnityClipRootMotion
    {
        public const string NameTx = "RootT.x";
        public const string NameTy = "RootT.y";
        public const string NameTz = "RootT.z";
        public const string NameQx = "RootQ.x";
        public const string NameQy = "RootQ.y";
        public const string NameQz = "RootQ.z";
        public const string NameQw = "RootQ.w";

        private UnityMuscleTrackDTO _tx, _ty, _tz;
        private UnityMuscleTrackDTO _qx, _qy, _qz, _qw;

        /// <summary>RootT が 1 本でもあるか。</summary>
        public bool HasTranslation { get; private set; }

        /// <summary>RootQ が 4 本そろっているか。w が無いと四元数にならないので全部要る。</summary>
        public bool HasRotation { get; private set; }

        /// <summary>見つかったトラック名。ログ用。</summary>
        public readonly List<string> FoundNames = new List<string>();

        private UnityClipRootMotion() { }

        /// <summary>
        /// 名前引きの辞書から Root 系だけを抜き出す。
        /// 既存 JSON は Root も muscles に入っているので、辞書はそのまま渡してよい。
        /// 1 本も無ければ null ではなく「持たない」インスタンスを返す。
        /// </summary>
        public static UnityClipRootMotion From(
            IReadOnlyDictionary<string, UnityMuscleTrackDTO> trackByName)
        {
            var r = new UnityClipRootMotion();
            if (trackByName == null) return r;

            r._tx = Pick(trackByName, NameTx, r.FoundNames);
            r._ty = Pick(trackByName, NameTy, r.FoundNames);
            r._tz = Pick(trackByName, NameTz, r.FoundNames);

            r._qx = Pick(trackByName, NameQx, r.FoundNames);
            r._qy = Pick(trackByName, NameQy, r.FoundNames);
            r._qz = Pick(trackByName, NameQz, r.FoundNames);
            r._qw = Pick(trackByName, NameQw, r.FoundNames);

            r.HasTranslation = r._tx != null || r._ty != null || r._tz != null;
            r.HasRotation    = r._qx != null && r._qy != null && r._qz != null && r._qw != null;

            return r;
        }

        private static UnityMuscleTrackDTO Pick(
            IReadOnlyDictionary<string, UnityMuscleTrackDTO> map, string name, List<string> found)
        {
            if (!map.TryGetValue(name, out var t)) return null;
            if (t == null || t.w == null || t.w.Count == 0) return null;
            found.Add(name);
            return t;
        }

        // ================================================================
        // サンプリング
        // ================================================================

        /// <summary>
        /// 正規化されたままの RootT。メートルではない。倍率は呼び出し側で掛ける。
        /// 欠けている軸は 0。
        /// </summary>
        public Vector3 SampleNormalizedTranslation(float timeSec)
            => new Vector3(
                UnityClipApplier.SampleTrackValue(_tx, timeSec),
                UnityClipApplier.SampleTrackValue(_ty, timeSec),
                UnityClipApplier.SampleTrackValue(_tz, timeSec));

        /// <summary>
        /// RootQ。長さが 0 に近いときは単位を返す（不正値で骨格を壊さない）。
        /// </summary>
        public Quaternion SampleRotation(float timeSec)
        {
            if (!HasRotation) return Quaternion.identity;

            var q = new Quaternion(
                UnityClipApplier.SampleTrackValue(_qx, timeSec),
                UnityClipApplier.SampleTrackValue(_qy, timeSec),
                UnityClipApplier.SampleTrackValue(_qz, timeSec),
                UnityClipApplier.SampleTrackValue(_qw, timeSec));

            return NormalizeSafe(q);
        }

        /// <summary>
        /// 長さで割る。0 長・NaN・無限大は単位へ落とす。
        /// </summary>
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
        /// 決められないとき（RootT.y が無い・0 近傍）は false。
        /// そのときは平行移動を載せないこと。基準が無いまま掛けると寸法が化ける。
        /// </summary>
        public bool TryComputeTranslationScale(
            float restHipsHeight, float referenceTimeSec, out float scale, out string note)
        {
            scale = 1f;

            if (!HasTranslation) { note = "RootT が無い"; return false; }

            float y0 = UnityClipApplier.SampleTrackValue(_ty, referenceTimeSec);
            if (_ty == null || Mathf.Abs(y0) < 1e-4f)
            {
                note = _ty == null
                    ? "RootT.y が無いので倍率を決められない"
                    : $"基準時刻の RootT.y が 0 近傍（{y0:F5}）なので倍率を決められない";
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
            if (!HasTranslation && !HasRotation) return "Root トラックなし";

            var t = SampleNormalizedTranslation(timeSec);
            var q = SampleRotation(timeSec);

            return $"t={timeSec:F3}s RootT(正規化)=({t.x:F5}, {t.y:F5}, {t.z:F5})"
                 + $" RootQ=({q.x:F5}, {q.y:F5}, {q.z:F5}, {q.w:F5})"
                 + $" [{(HasTranslation ? "T あり" : "T なし")}/{(HasRotation ? "Q あり" : "Q なし")}]";
        }
    }
}
