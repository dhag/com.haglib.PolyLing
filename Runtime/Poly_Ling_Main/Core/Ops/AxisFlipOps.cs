// AxisFlipOps.cs
// 軸反転（X / Z）の座標変換規則を1箇所に集約する。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【背景】
//   各系のキャラクタの置き方は次のとおり（実測で確認済み）。
//     PMX          左手系  正面 = -Z  上 = +Y  右 = -X
//     メタセコイア  右手系  正面 = +Z  上 = +Y  右 = -X
//     Unity        左手系  正面 = +Z  上 = +Y  右 = +X
//   したがって Unity 規約へ揃えるための変換は
//     PMX → Unity : X と Z を反転（= Y軸180°回転。純粋な回転）
//     MQO → Unity : X のみ反転（右手系→左手系の変換を兼ねる）
//   となる。どちらも符号ベクトル S = diag(sx, 1, sz) 一つで表せるため、
//   位置・方向・法線・回転・面の巻き順の規則をここへ集約する。
//
// 【規則】
//   位置 / 方向 / 法線 : 成分ごとに S を掛ける
//   面の巻き順        : sx * sz < 0（＝反転軸が奇数個）のときのみ反転
//   回転              : R' = S · R · S
//     クォータニオンでは、回転軸 n に S を掛け、鏡映（det S < 0）のときは
//     角度が反転するのでベクトル部の符号を反転する。
//       X,Z 両反転 → (x, y, z, w) → (-x,  y, -z, w)   ※Y軸180°回転
//       Z のみ反転 → (x, y, z, w) → (-x, -y,  z, w)
//       X のみ反転 → (x, y, z, w) → ( x, -y, -z, w)
//     いずれも自己逆元なので、インポートと同じ処理がそのまま逆変換になる。

using UnityEngine;

namespace Poly_Ling.Ops
{
    /// <summary>
    /// X / Z の軸反転指定。Y は常に反転しない（上方向は全ての系で +Y のため）。
    /// </summary>
    public readonly struct AxisFlip
    {
        public readonly bool FlipX;
        public readonly bool FlipZ;

        public AxisFlip(bool flipX, bool flipZ)
        {
            FlipX = flipX;
            FlipZ = flipZ;
        }

        /// <summary>PMX ⇔ Unity（X と Z を反転 = Y軸180°回転）。</summary>
        public static AxisFlip PmxToUnity => new AxisFlip(true, true);

        /// <summary>MQO ⇔ Unity（X のみ反転）。</summary>
        public static AxisFlip MqoToUnity => new AxisFlip(true, false);

        /// <summary>無変換。</summary>
        public static AxisFlip None => new AxisFlip(false, false);

        public float Sx => FlipX ? -1f : 1f;
        public float Sz => FlipZ ? -1f : 1f;

        public bool IsIdentity => !FlipX && !FlipZ;

        /// <summary>
        /// 鏡映（手系が入れ替わる）か。反転軸が奇数個のとき true。
        /// true のときは面の巻き順を反転しないと表裏が裏返る。
        /// </summary>
        public bool IsMirror => FlipX ^ FlipZ;
    }

    public static class AxisFlipOps
    {
        // ================================================================
        // 位置・方向・法線
        // ================================================================

        /// <summary>位置に軸反転を適用する。</summary>
        public static Vector3 Position(AxisFlip f, Vector3 p)
        {
            if (f.IsIdentity) return p;
            return new Vector3(p.x * f.Sx, p.y, p.z * f.Sz);
        }

        /// <summary>位置に軸反転とスケールを適用する。</summary>
        public static Vector3 Position(AxisFlip f, Vector3 p, float scale)
        {
            return new Vector3(p.x * f.Sx * scale, p.y * scale, p.z * f.Sz * scale);
        }

        /// <summary>方向ベクトルに軸反転を適用する（正規化しない）。</summary>
        public static Vector3 Direction(AxisFlip f, Vector3 v) => Position(f, v);

        /// <summary>法線に軸反転を適用して正規化する。</summary>
        public static Vector3 Normal(AxisFlip f, Vector3 n)
        {
            return f.IsIdentity ? n.normalized : Position(f, n).normalized;
        }

        // ================================================================
        // 回転
        // ================================================================

        /// <summary>
        /// 回転に軸反転を適用する（R' = S·R·S）。自己逆元。
        /// </summary>
        public static Quaternion Rotation(AxisFlip f, Quaternion q)
        {
            if (f.IsIdentity) return q;

            float sx = f.Sx;
            float sz = f.Sz;

            // 鏡映のときは角度が反転するためベクトル部の符号を反転する。
            float s = f.IsMirror ? -1f : 1f;

            return new Quaternion(s * sx * q.x, s * q.y, s * sz * q.z, q.w);
        }

        /// <summary>オイラー角（度）に軸反転を適用する。</summary>
        public static Vector3 EulerDeg(AxisFlip f, Vector3 eulerDeg)
        {
            if (f.IsIdentity) return eulerDeg;
            return Rotation(f, Quaternion.Euler(eulerDeg)).eulerAngles;
        }

        /// <summary>オイラー角（ラジアン）に軸反転を適用する。</summary>
        public static Vector3 EulerRad(AxisFlip f, Vector3 eulerRad)
        {
            if (f.IsIdentity) return eulerRad;
            Quaternion q = Quaternion.Euler(eulerRad * Mathf.Rad2Deg);
            return Rotation(f, q).eulerAngles * Mathf.Deg2Rad;
        }

        // ================================================================
        // 面の巻き順
        // ================================================================

        /// <summary>面の巻き順を反転する必要があるか。</summary>
        public static bool ReverseWinding(AxisFlip f) => f.IsMirror;

        /// <summary>
        /// 三角形の頂点順を必要に応じて入れ替える（v2 と v3 を交換）。
        /// 反転が不要なときは何もしない。
        /// </summary>
        public static void ApplyWinding(AxisFlip f, ref int v1, ref int v2, ref int v3)
        {
            if (!f.IsMirror) return;
            int t = v2;
            v2 = v3;
            v3 = t;
        }

        /// <summary>
        /// 多角形の頂点順を必要に応じて反転する（先頭を固定して残りを逆順）。
        /// 反転が不要なときは何もしない。
        /// </summary>
        public static void ApplyWinding(AxisFlip f, System.Collections.Generic.List<int> indices)
        {
            if (!f.IsMirror || indices == null || indices.Count < 3) return;
            indices.Reverse(1, indices.Count - 1);
        }

        // ================================================================
        // 基底ベクトル
        // ================================================================

        /// <summary>
        /// 直交基底（列ベクトル X, Y, Z からなる回転行列 M）に S·M·S を適用する。
        /// 列 j は S を掛けたうえで s_j 倍する（S·M·S の列 j = s_j · S · M_j）。
        /// Rotation(AxisFlip, Quaternion) と同一の変換を行列表現で行うもの。
        /// </summary>
        public static void Basis(AxisFlip f, ref Vector3 axisX, ref Vector3 axisY, ref Vector3 axisZ)
        {
            if (f.IsIdentity) return;

            float sx = f.Sx;
            float sz = f.Sz;

            axisX = Position(f, axisX) * sx;
            axisY = Position(f, axisY);
            axisZ = Position(f, axisZ) * sz;
        }

        // ================================================================
        // 角度制限
        // ================================================================

        /// <summary>
        /// オイラー角の角度制限 min/max に軸反転を適用する。
        ///
        /// 軸 k まわりの回転角は、S·R·S のもとで符号 σ_k = s_k · det(S) が掛かる
        /// （det(S) = sx · sz）。σ_k が負の軸では min/max が入れ替わる。
        ///   Z のみ反転 : σ = (-1, -1, +1)  … X,Y が入れ替わり
        ///   X,Z 両反転 : σ = (-1, +1, -1)  … X,Z が入れ替わり
        ///   X のみ反転 : σ = (+1, -1, -1)  … Y,Z が入れ替わり
        /// </summary>
        public static void AngleLimits(AxisFlip f, ref Vector3 min, ref Vector3 max)
        {
            if (f.IsIdentity) return;

            float det = f.Sx * f.Sz;
            float sigX = f.Sx * det;
            float sigY = det;
            float sigZ = f.Sz * det;

            Vector3 srcMin = min;
            Vector3 srcMax = max;

            min = new Vector3(
                sigX > 0f ? srcMin.x : -srcMax.x,
                sigY > 0f ? srcMin.y : -srcMax.y,
                sigZ > 0f ? srcMin.z : -srcMax.z);

            max = new Vector3(
                sigX > 0f ? srcMax.x : -srcMin.x,
                sigY > 0f ? srcMax.y : -srcMin.y,
                sigZ > 0f ? srcMax.z : -srcMin.z);
        }
    }
}
