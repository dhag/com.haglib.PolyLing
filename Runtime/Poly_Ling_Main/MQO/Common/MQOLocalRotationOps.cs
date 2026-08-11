// MQOLocalRotationOps.cs
// MQO の Object.rotation（ローカル回転）と Unity のオイラー角の相互変換。
// Runtime/Poly_Ling_Main/MQO/Common/ に配置
//
// 【なぜ専用の変換が要るか】
//   メタセコイア公式のファイル仕様（metaseq.net/en/format.html）では
//   Object チャンクの各属性が次のように定義されている。
//     scale       %.6f %.6f %.6f   Scaling factor for a local coordinate   XYZ
//     rotation    %.6f %.6f %.6f   Angle for a local coordinate            HPB
//     translation %.6f %.6f %.6f   Translation for a local coordinate      XYZ
//   scale と translation は XYZ だが、rotation だけは HPB
//   （Head = Y軸まわり / Pitch = X軸まわり / Bank = Z軸まわり）である。
//   Unity のオイラー角 (x, y, z) をそのまま書くと X と Y が入れ替わるため、
//   メタセコイアで開いたときに姿勢が崩れる。
//
// 【合成順】
//   HPB は Y → X → Z の順で合成する（R = Ry(H)·Rx(P)·Rz(B)）。
//   Unity の Quaternion.Euler(x, y, z) も Ry(y)·Rx(x)·Rz(z) と同じ合成順なので、
//   成分を入れ替えるだけで対応が付く（回転行列そのものは両者で同一の式）。
//     Unity euler (x, y, z)  ⇔  MQO rotation (H, P, B) = (y, x, z)
//
// 【座標系】
//   メタセコイア空間 ⇔ Unity 空間の変換は AxisFlipOps.Rotation に任せる
//   （R' = S·R·S / 自己逆元）。ここは HPB と XYZ の並べ替えだけを担う。
//
// 【単位・符号の逃げ道】
//   仕様書は rotation の単位を明記していない。同じ HPB 表記である材質の
//   proj_angle が「-180 to 180」と書かれていることから度と判断しているが、
//   もし実機と合わなければ UseRadians / 各 Sign を切り替えて調整する。
//   確認手順: メタセコイアでオブジェクトをローカル回転 90° させて保存し、
//   rotation の値が 90.000000 なら度、1.570796 ならラジアン。

using UnityEngine;
using Poly_Ling.Ops;

namespace Poly_Ling.MQO
{
    public static class MQOLocalRotationOps
    {
        // ================================================================
        // 調整用スイッチ
        // ================================================================

        /// <summary>MQO の rotation をラジアンとして扱う（既定は度）。</summary>
        public static bool UseRadians = false;

        /// <summary>Head（Y軸まわり）の符号。回転の向きが逆なら -1。</summary>
        public static float HeadSign = 1f;

        /// <summary>Pitch（X軸まわり）の符号。回転の向きが逆なら -1。</summary>
        public static float PitchSign = 1f;

        /// <summary>Bank（Z軸まわり）の符号。回転の向きが逆なら -1。</summary>
        public static float BankSign = 1f;

        // ================================================================
        // MQO → Unity
        // ================================================================

        /// <summary>
        /// MQO の rotation（HPB）を Unity のローカルオイラー角（XYZ・度）へ変換する。
        /// </summary>
        public static Vector3 ToUnityEuler(Vector3 mqoRotation, AxisFlip flip)
        {
            float h = mqoRotation.x * HeadSign;
            float p = mqoRotation.y * PitchSign;
            float b = mqoRotation.z * BankSign;

            if (UseRadians)
            {
                h *= Mathf.Rad2Deg;
                p *= Mathf.Rad2Deg;
                b *= Mathf.Rad2Deg;
            }

            // HPB → XYZ の並べ替え（メタセコイア空間のままの回転）
            Quaternion qMqo = Quaternion.Euler(p, h, b);

            // メタセコイア空間 → Unity 空間
            return AxisFlipOps.Rotation(flip, qMqo).eulerAngles;
        }

        // ================================================================
        // Unity → MQO
        // ================================================================

        /// <summary>
        /// Unity のローカルオイラー角（XYZ・度）を MQO の rotation（HPB）へ変換する。
        /// </summary>
        public static Vector3 ToMqoRotation(Vector3 unityEuler, AxisFlip flip)
        {
            // Unity 空間 → メタセコイア空間（AxisFlipOps.Rotation は自己逆元）
            Quaternion qMqo = AxisFlipOps.Rotation(flip, Quaternion.Euler(unityEuler));

            Vector3 e = qMqo.eulerAngles;   // x = Pitch, y = Head, z = Bank

            float h = Normalize180(e.y);
            float p = Normalize180(e.x);
            float b = Normalize180(e.z);

            if (UseRadians)
            {
                h *= Mathf.Deg2Rad;
                p *= Mathf.Deg2Rad;
                b *= Mathf.Deg2Rad;
            }

            return new Vector3(h * HeadSign, p * PitchSign, b * BankSign);
        }

        // ================================================================
        // 共通
        // ================================================================

        /// <summary>角度を -180 〜 180 に収める（メタセコイアの表示範囲に合わせる）。</summary>
        private static float Normalize180(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            else if (deg < -180f) deg += 360f;
            return deg;
        }
    }
}
