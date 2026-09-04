// WormPairSection.cs
// ウォームとウォームホイールが共通で使う「組」の関係式。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【軸方向モジュールで組む】
//   ウォームの軸方向モジュール mx が、そのままウォームホイールの正面モジュールになる。
//   直交軸（軸角 90°）の組を前提にする。
//
// 【進み角】
//   ウォームのピッチ円直径は、直径係数 q を使って d1 = q·mx と決める。
//   条数 z1 に対して進み角は
//
//       tan(γ) = z1 / q
//
//   軸方向ピッチ px = π·mx、リード = px·z1。
//
// 【圧力角】
//   入力は法線圧力角 αn。進み角のところで軸直角の圧力角へ直す。
//
//       tan(αx) = tan(αn) / cos(γ)
//
//   ウォームの軸断面歯形の傾きにも、ウォームホイールの歯形にも同じ値を使う。
//   直交軸の組では、ホイールのねじれ角 β2 はウォームの進み角 γ と等しい。
//
// 【かみ合い中心距離】
//   a = d1/2 + d2/2 = (q·mx + z2·mx) / 2
//
//   ウォームホイールののど（throat）はこの中心距離まわりの円弧で決まる。

using UnityEngine;

namespace Poly_Ling.PrimitiveMesh
{
    public static class WormPairSection
    {
        // ================================================================
        // 入力
        // ================================================================

        /// <summary>ウォーム組の諸元。角度はラジアン。</summary>
        public struct PairInput
        {
            /// <summary>軸方向モジュール mx</summary>
            public float AxialModule;
            /// <summary>条数 z1</summary>
            public int Starts;
            /// <summary>直径係数 q。ウォームのピッチ円直径 = q·mx。</summary>
            public float DiameterFactorQ;
            /// <summary>法線圧力角 αn</summary>
            public float NormalPressureAngle;
            /// <summary>ねじの向き。+1 で右ねじ、-1 で左ねじ。</summary>
            public float Hand;
        }

        // ================================================================
        // 導出諸元
        // ================================================================

        /// <summary>入力から求まる、ウォーム組の値ひとそろい。</summary>
        public struct PairData
        {
            public float mx;
            public int z1;
            public float q;

            /// <summary>法線圧力角</summary>
            public float alphaN;
            /// <summary>軸直角の圧力角。ウォームの軸断面にも、ホイールの歯形にも使う。</summary>
            public float alphaX;
            /// <summary>進み角 γ</summary>
            public float gamma;

            /// <summary>+1 で右ねじ、-1 で左ねじ</summary>
            public float hand;

            /// <summary>ウォームのピッチ円半径</summary>
            public float wormPitchRadius;

            /// <summary>軸方向ピッチ px = π·mx</summary>
            public float axialPitch;
            /// <summary>リード = px · z1</summary>
            public float lead;
        }

        /// <summary>組の諸元を求める。成立しないときは false。</summary>
        public static bool TryGetPairData(PairInput p, out PairData g)
        {
            g = default;

            if (p.AxialModule <= 0f ||
                p.Starts < 1 ||
                p.DiameterFactorQ <= 0f ||
                p.NormalPressureAngle <= 0f ||
                p.NormalPressureAngle >= 45f * Mathf.Deg2Rad)
            {
                return false;
            }

            float gamma = Mathf.Atan(p.Starts / p.DiameterFactorQ);
            float cosGamma = Mathf.Cos(gamma);

            if (cosGamma <= 1e-6f) return false;

            float axialPitch = Mathf.PI * p.AxialModule;

            g = new PairData
            {
                mx = p.AxialModule,
                z1 = p.Starts,
                q = p.DiameterFactorQ,

                alphaN = p.NormalPressureAngle,
                alphaX = Mathf.Atan(Mathf.Tan(p.NormalPressureAngle) / cosGamma),
                gamma = gamma,

                hand = p.Hand < 0f ? -1f : 1f,

                wormPitchRadius = 0.5f * p.DiameterFactorQ * p.AxialModule,

                axialPitch = axialPitch,
                lead = axialPitch * p.Starts,
            };

            return true;
        }

        // ================================================================
        // 相手側の寸法
        // ================================================================

        /// <summary>ウォームホイールのピッチ円半径。軸方向モジュールがそのまま正面モジュールになる。</summary>
        public static float WheelPitchRadius(PairData g, int wheelToothCount)
            => 0.5f * wheelToothCount * g.mx;

        /// <summary>かみ合い中心距離。</summary>
        public static float CenterDistance(PairData g, int wheelToothCount)
            => g.wormPitchRadius + WheelPitchRadius(g, wheelToothCount);

        /// <summary>減速比 z2 / z1。</summary>
        public static float GearRatio(PairData g, int wheelToothCount)
            => g.z1 > 0 ? wheelToothCount / (float)g.z1 : 0f;
    }
}
