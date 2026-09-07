// Assets/Editor/Poly_Ling/Core/Data/SpringBoneTaper.cs
// ============================================================
// 揺れパラメータの段階変化（根元→末端）データ（純POCO）
// ============================================================
//
// 【役割】
//   鎖（チェーン）に並ぶ各段へ、1 つの値を配るための指定。
//   「根元の値」「末端の値」「配分のしかた」の 3 つだけを持つ。
//   実際に段へ書き込むのは SetSpringBoneJointCommand であり、
//   本クラスは値を決めるだけで、モデルには触れない。
//
// 【なぜ要るか】
//   VRM SpringBone のジョイント値は段ごとに別々に持てる。
//   髪も裾も、根元は硬く先は柔らかい、先だけ重い、といった配り方をするのが
//   一般的で、全段同値では自然に見えない。
//   同じ考え方は Dynamic Bone の *Distrib（AnimationCurve を基準値に乗算）、
//   VRChat PhysBones の各設定のカーブ、KawaiiPhysics の
//   「根元から先端でパラメータを変える」機能でも使われている。
//   いずれも「正規化した位置 0→1 に対する配分」を基準値へ効かせる形で共通する。
//
// 【配分の決め方】
//   u = 段の位置（根元 0、末端 1）。
//   Curve が 2 点以上あるときは Curve を折れ線として引く（x=位置, y=配分 0..1）。
//   Curve が無いときは y = u^Gamma を使う。
//     Gamma = 1   … まっすぐ（線形）
//     Gamma > 1   … 根元の値が長く残り、末端の近くで一気に変わる
//     Gamma < 1   … 根元をすぐ離れ、末端の値が長く続く
//   得られた y で Root と Tip を混ぜる（y=0 で Root、y=1 で Tip）。
//
// 【依存】
//   UnityEngine.Vector3/Mathf のみ。#if UNITY_EDITOR を含まない。
//
// ============================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Data
{
    /// <summary>
    /// 揺れパラメータの段階変化（根元→末端）の指定（純POCO）。
    /// </summary>
    [Serializable]
    public class SpringBoneTaper
    {
        /// <summary>根元（鎖の先頭）側の値。</summary>
        public float Root { get; set; }

        /// <summary>末端側の値。</summary>
        public float Tip { get; set; }

        /// <summary>
        /// 配分の曲がり具合。Curve が無いときだけ使う。
        /// 1 でまっすぐ。大きいほど根元の値が長く残る。
        /// </summary>
        public float Gamma { get; set; } = 1f;

        /// <summary>
        /// 配分の折れ線（x=段の位置 0..1、y=配分 0..1）。
        /// null または 2 点未満なら Gamma を使う。
        /// </summary>
        public List<Vector2> Curve { get; set; }

        public SpringBoneTaper() { }

        public SpringBoneTaper(float root, float tip, float gamma = 1f)
        {
            Root  = root;
            Tip   = tip;
            Gamma = gamma;
        }

        /// <summary>根元と末端が同じ値か（＝全段同値でよいか）。</summary>
        public bool IsFlat => Mathf.Approximately(Root, Tip);

        /// <summary>
        /// 段の位置 u（根元 0、末端 1）に対する値を返す。
        /// u は 0..1 に丸める。
        /// </summary>
        public float Evaluate(float u)
        {
            float t = Blend(Mathf.Clamp01(u));
            return Mathf.LerpUnclamped(Root, Tip, t);
        }

        /// <summary>
        /// 段番号と段数から値を返す。段数が 1 以下なら根元の値。
        /// </summary>
        public float EvaluateStep(int step, int stepCount)
        {
            if (stepCount <= 1) return Root;
            return Evaluate((float)step / (stepCount - 1));
        }

        /// <summary>位置 u に対する配分 0..1。</summary>
        public float Blend(float u)
        {
            if (Curve != null && Curve.Count >= 2) return SampleCurve(Curve, u);

            float g = Gamma > 0f ? Gamma : 1f;
            return Mathf.Clamp01(Mathf.Pow(Mathf.Clamp01(u), g));
        }

        /// <summary>ディープコピー。</summary>
        public SpringBoneTaper Clone()
        {
            return new SpringBoneTaper
            {
                Root  = this.Root,
                Tip   = this.Tip,
                Gamma = this.Gamma,
                Curve = this.Curve != null ? new List<Vector2>(this.Curve) : null,
            };
        }

        // ================================================================
        // 配分の見本
        // ================================================================
        //
        // 数値の出どころは、外部ツールで実際に使われている配り方。
        //   ・根元 1.0 →末端 0.2 前後（形を保ちたい値。かたさなど）
        //   ・根元 0.3 →末端 1.0 前後（先ほど効かせたい値。重力など）
        // Gamma はそれを「どこで切り替えるか」の指定にあたる。

        /// <summary>まっすぐ配る（線形）。</summary>
        public const float GammaStraight = 1f;

        /// <summary>根元の値を長く残す。根元が崩れるときに使う。</summary>
        public const float GammaHoldRoot = 2.2f;

        /// <summary>末端の値を長く効かせる。先だけ重くしたいときに使う。</summary>
        public const float GammaHoldTip = 0.45f;

        /// <summary>
        /// S 字。中ほどで一気に変わり、両端は落ち着く。
        /// 折れ線で持つので Gamma は使わない。
        /// </summary>
        public static List<Vector2> SCurve() => new List<Vector2>
        {
            new Vector2(0f,    0f),
            new Vector2(0.25f, 0.06f),
            new Vector2(0.5f,  0.5f),
            new Vector2(0.75f, 0.94f),
            new Vector2(1f,    1f),
        };

        /// <summary>まっすぐな折れ線。</summary>
        public static List<Vector2> LinearCurve() => new List<Vector2>
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 1f),
        };

        /// <summary>
        /// 折れ線を x で引く。x は昇順である前提。範囲外は端の値で止める。
        /// </summary>
        public static float SampleCurve(IList<Vector2> curve, float u)
        {
            if (curve == null || curve.Count == 0) return Mathf.Clamp01(u);
            if (curve.Count == 1) return Mathf.Clamp01(curve[0].y);

            if (u <= curve[0].x) return Mathf.Clamp01(curve[0].y);

            int last = curve.Count - 1;
            if (u >= curve[last].x) return Mathf.Clamp01(curve[last].y);

            for (int i = 0; i < last; i++)
            {
                float x0 = curve[i].x;
                float x1 = curve[i + 1].x;
                if (u < x0 || u > x1) continue;

                float span = x1 - x0;
                float t = span > 1e-6f ? (u - x0) / span : 0f;
                return Mathf.Clamp01(Mathf.Lerp(curve[i].y, curve[i + 1].y, t));
            }

            return Mathf.Clamp01(curve[last].y);
        }
    }
}
