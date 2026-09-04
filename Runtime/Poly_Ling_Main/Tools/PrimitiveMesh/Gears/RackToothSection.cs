// RackToothSection.cs
// ラック歯（台形歯）の断面を作る共有部品。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【ラックとは】
//   半径を無限大にした歯車。歯面は直線になり、圧力角がそのまま歯面の傾きになる。
//   ピッチ線を Y=0 に置き、歯は +Y へ向く。
//
//     歯先の高さ   ha = ha* · m      （tipY  = +ha）
//     歯底の深さ   hf = hf* · m      （rootY = -hf）
//     ピッチ線上の半歯厚  = p/4 - バックラッシ/2
//     歯先の半歯厚        = ピッチ線上の半歯厚 - ha·tan(α)
//     歯元の半歯厚        = ピッチ線上の半歯厚 + hf·tan(α)
//
// 【はすばラックとの共通化】
//   はすばラックでは、円周方向の寸法が正面モジュール mt = mn/cos(β) で決まり、
//   半径（高さ）方向の歯たけは法線モジュール mn で決まる。
//   平ラックでは両方に同じ値を入れればよい。これは歯車側（InvoluteTrochoidSection）と同じ扱い。
//
// 【台形の評価】
//   EvaluateTrapezoid は「周期的な台形の山」を返す汎用関数。
//   ラックでは高さ Y を返し、ウォームでは半径 r を返す。どちらも同じ形なので 1 本にしてある。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.PrimitiveMesh
{
    public static class RackToothSection
    {
        // ================================================================
        // 入力
        // ================================================================

        /// <summary>ラック断面の諸元。角度はラジアン。</summary>
        public struct RackInput
        {
            /// <summary>歯数。ラックの長さは 歯数 × 正面ピッチ になる。</summary>
            public int ToothCount;

            /// <summary>正面モジュール mt。ピッチと歯厚を決める。</summary>
            public float TransverseModule;

            /// <summary>高さ方向の歯たけを決めるモジュール。平ラックでは正面モジュールと同じ。</summary>
            public float RadialModule;

            /// <summary>正面圧力角 αt。歯面の傾き。</summary>
            public float TransversePressureAngle;

            /// <summary>正面バックラッシ（長さ）。歯厚をこの分だけ削る。</summary>
            public float Backlash;

            /// <summary>歯末のたけ係数 ha*</summary>
            public float AddendumCoef;

            /// <summary>歯元のたけ係数 hf*</summary>
            public float DedendumCoef;

            /// <summary>歯底から本体の底までの肉厚。</summary>
            public float BodyHeight;
        }

        // ================================================================
        // 導出諸元
        // ================================================================

        /// <summary>入力から求まる、断面生成に必要な値ひとそろい。</summary>
        public struct RackData
        {
            public int z;

            /// <summary>正面モジュール</summary>
            public float mt;
            /// <summary>高さ方向の歯たけを決めるモジュール</summary>
            public float mr;
            /// <summary>正面圧力角</summary>
            public float alpha;

            /// <summary>正面ピッチ π·mt</summary>
            public float pitch;
            /// <summary>全長 = 歯数 × 正面ピッチ</summary>
            public float length;

            public float addendum;
            public float dedendum;

            public float pitchHalfThickness;
            public float tipHalfThickness;
            public float rootHalfThickness;

            /// <summary>歯先の高さ（+）</summary>
            public float tipY;
            /// <summary>歯底の高さ（-）</summary>
            public float rootY;
            /// <summary>本体の底</summary>
            public float bottomY;
        }

        /// <summary>諸元を求める。成立しないときは false。</summary>
        public static bool TryGetRackData(RackInput r, out RackData g)
        {
            g = default;

            if (r.ToothCount < 1 ||
                r.TransverseModule <= 0f ||
                r.RadialModule <= 0f ||
                r.BodyHeight <= 0f ||
                r.AddendumCoef <= 0f ||
                r.DedendumCoef <= 0f ||
                r.TransversePressureAngle <= 0f ||
                r.TransversePressureAngle >= 45f * Mathf.Deg2Rad)
            {
                return false;
            }

            float pitch = Mathf.PI * r.TransverseModule;
            float addendum = r.AddendumCoef * r.RadialModule;
            float dedendum = r.DedendumCoef * r.RadialModule;

            float pitchHalfThickness = pitch * 0.25f - r.Backlash * 0.5f;
            float tanA = Mathf.Tan(r.TransversePressureAngle);

            float tipHalfThickness = pitchHalfThickness - addendum * tanA;
            float rootHalfThickness = pitchHalfThickness + dedendum * tanA;

            // 歯先がとがり切る、あるいは歯溝が閉じるところで打ち切る。
            if (pitchHalfThickness <= 0f ||
                tipHalfThickness <= 0f ||
                rootHalfThickness >= pitch * 0.5f)
            {
                return false;
            }

            g = new RackData
            {
                z = r.ToothCount,

                mt = r.TransverseModule,
                mr = r.RadialModule,
                alpha = r.TransversePressureAngle,

                pitch = pitch,
                length = r.ToothCount * pitch,

                addendum = addendum,
                dedendum = dedendum,

                pitchHalfThickness = pitchHalfThickness,
                tipHalfThickness = tipHalfThickness,
                rootHalfThickness = rootHalfThickness,

                tipY = addendum,
                rootY = -dedendum,
                bottomY = -dedendum - r.BodyHeight,
            };

            return true;
        }

        // ================================================================
        // 台形の評価
        // ================================================================

        /// <summary>x を [-period/2, +period/2] へ折り返す。</summary>
        public static float WrapCentered(float x, float period)
            => x - period * Mathf.Floor(x / period + 0.5f);

        /// <summary>
        /// 周期的な台形の山を評価する。
        ///
        ///   |phase| ≤ tipHalfWidth       … 山の頂（tipValue）
        ///   |phase| ≥ rootHalfWidth      … 谷（rootValue）
        ///   その間                        … 傾き 1/flankTan の斜面
        ///
        /// ラックでは値が高さ Y、ウォームでは半径 r になる。
        /// </summary>
        public static float EvaluateTrapezoid(
            float phase, float period,
            float tipHalfWidth, float rootHalfWidth,
            float tipValue, float rootValue, float flankTan)
        {
            float x = Mathf.Abs(WrapCentered(phase, period));

            if (x <= tipHalfWidth) return tipValue;
            if (x >= rootHalfWidth) return rootValue;

            if (Mathf.Abs(flankTan) < 1e-8f) return tipValue;

            float drop = (x - tipHalfWidth) / flankTan;

            return Mathf.Clamp(tipValue - drop, rootValue, tipValue);
        }

        /// <summary>ラックの上面の高さ。phase はピッチ線に沿った歯中心からのずれ。</summary>
        public static float EvaluateHeight(RackData g, float phase)
            => EvaluateTrapezoid(
                phase, g.pitch,
                g.tipHalfThickness, g.rootHalfThickness,
                g.tipY, g.rootY,
                Mathf.Tan(g.alpha));

        // ================================================================
        // 上面プロファイル
        // ================================================================

        /// <summary>
        /// 折れ点だけを並べた正確な上面（左 → 右、X は単調増加）。
        /// 平ラックはこれを使う。歯面が直線なので標本数を増やす必要がない。
        ///
        /// 全長はちょうど 歯数 × ピッチ で、両端は歯溝の中心に来る。
        /// </summary>
        public static List<Vector2> BuildExactTopProfile(RackData g)
        {
            var points = new List<Vector2>(4 * g.z + 2);

            float xLeft = -0.5f * g.length;

            // 左端は歯溝の中心、歯底の高さ。
            points.Add(new Vector2(xLeft, g.rootY));

            for (int i = 0; i < g.z; i++)
            {
                float center = xLeft + (i + 0.5f) * g.pitch;

                points.Add(new Vector2(center - g.rootHalfThickness, g.rootY));
                points.Add(new Vector2(center - g.tipHalfThickness,  g.tipY));
                points.Add(new Vector2(center + g.tipHalfThickness,  g.tipY));
                points.Add(new Vector2(center + g.rootHalfThickness, g.rootY));
            }

            points.Add(new Vector2(xLeft + g.length, g.rootY));

            // しきい値は GearDiskBuilder / InvoluteTrochoidSection と同じ（距離で 1e-6）。
            RemoveDuplicateNeighbors(points, 1e-12f);
            return points;
        }

        /// <summary>
        /// X を等間隔に刻んだ上面。歯の位相を X ごとにずらせるので、はすばラックが使う。
        /// </summary>
        /// <param name="g">諸元</param>
        /// <param name="sampleCount">刻み数。返る点数は sampleCount + 1。</param>
        /// <param name="phaseShift">歯を本体に対してずらす量（長さ）。</param>
        public static Vector2[] BuildSampledTopProfile(
            RackData g, int sampleCount, float phaseShift)
        {
            int nx = Mathf.Max(1, sampleCount);

            float xMin = -0.5f * g.length;
            float xMax = +0.5f * g.length;

            var points = new Vector2[nx + 1];

            for (int i = 0; i <= nx; i++)
            {
                float x = Mathf.Lerp(xMin, xMax, i / (float)nx);

                // X=xMin から半ピッチのところに最初の歯の中心が来るようにそろえる。
                float phase = x - (xMin + 0.5f * g.pitch) - phaseShift;

                points[i] = new Vector2(x, EvaluateHeight(g, phase));
            }

            return points;
        }

        // ================================================================
        // 断面を閉じる
        // ================================================================

        /// <summary>
        /// 上面プロファイル（左 → 右）を本体の底で閉じ、CCW の断面にする。
        ///
        ///   左下 → 右下 → 上面を右から左へ → （左端へ戻る）
        ///
        /// この向きにしないと符号付き面積が負になり、GearLoftBuilder が面を裏返す。
        /// </summary>
        public static Vector2[] CloseSection(IReadOnlyList<Vector2> top, float bottomY)
        {
            int n = top?.Count ?? 0;
            if (n < 2) return null;

            float xMin = top[0].x;
            float xMax = top[n - 1].x;

            var loop = new Vector2[n + 2];

            loop[0] = new Vector2(xMin, bottomY);
            loop[1] = new Vector2(xMax, bottomY);

            for (int i = 0; i < n; i++)
                loop[2 + i] = top[n - 1 - i];

            return loop;
        }

        // ================================================================
        // 共有ユーティリティ
        // ================================================================

        /// <summary>隣り合う重複点を取り除く（開いた列として扱い、先頭と末尾は見ない）。</summary>
        public static void RemoveDuplicateNeighbors(List<Vector2> points, float sqrEpsilon)
        {
            if (points == null) return;

            for (int i = points.Count - 1; i > 0; i--)
            {
                if ((points[i] - points[i - 1]).sqrMagnitude <= sqrEpsilon)
                    points.RemoveAt(i);
            }
        }
    }
}
