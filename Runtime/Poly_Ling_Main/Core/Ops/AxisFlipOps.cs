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
        // ----------------------------------------------------------------
        //   Basis / MmdAxisToStandard / MmdComponentToStandard は削除した。
        //   PMX 取込でボーンの局所軸を合成する処理そのものを廃止したため
        //   （PMXImporter: BoneModelRotation は恒等固定）、呼び出し元が存在しない。
        //
        //   Rotation(AxisFlip, Quaternion) は残す。用途がデルタ／演算子の変換
        //   （VMD のフレーム回転値、MQO の HPB 姿勢）であり、基底のラベル付けでは
        //   ないため、両側共役 S·q·S のままで正しい。
        // ================================================================

        // ================================================================
        // 角度制限
        // ================================================================

        /// <summary>
        /// オイラー角の角度制限 min/max に軸反転を適用する。
        ///
        /// 局所軸の合成を廃止し、ボーンのレスト回転が恒等になったため、
        /// 角度制限はモデル空間の値として扱う。軸 k まわりの回転角に掛かる符号は
        /// σ_k = det(S) = sx · sz で、3 軸とも共通。
        ///   X,Z 両反転 : det = +1  … 符号反転なし。min/max はそのまま
        ///   Z のみ反転 : det = -1  … 3 軸とも min/max が入れ替わる
        ///   X のみ反転 : det = -1  … 同上
        ///
        /// 旧実装は両側共役 S·M·S 前提で σ_k = s_k · det(S) としており、
        /// X と Z だけ入れ替えていた。局所軸の廃止に合わせて作り直したもの。
        ///
        /// ■ 未実装（恒久メモ）
        ///   付与親（GrantParentIndex / GrantRate）と固定軸はポーズ適用側に
        ///   評価コードが無い。IK 角度制限とは別系統。
        /// </summary>
        public static void AngleLimits(AxisFlip f, ref Vector3 min, ref Vector3 max)
        {
            if (f.IsIdentity) return;

            float det = f.Sx * f.Sz;
            if (det > 0f) return;   // 符号反転なし

            Vector3 srcMin = min;
            Vector3 srcMax = max;

            min = -srcMax;
            max = -srcMin;
        }
    }
}
