// InvoluteTrochoidSection.cs
// インボリュート歯面＋ラックカッタ生成トロコイド歯元の「正面断面」を作る共有部品。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【何をするか】
//   正面諸元（歯数・正面モジュール・正面圧力角・転位・バックラッシ・歯たけ係数）から
//   XY 平面の閉じた歯形輪郭（CCW）を 1 本作る。押し出しやロフトは呼出し側が受け持つ。
//
// 【形状】
//   歯面   … インボリュート曲線
//   歯元   … 創成ラックカッタの尖った角が描く軌跡（トロコイド）
//   少歯数 … トロコイドとインボリュートの交点を自動で探し、切り下げ（アンダーカット）を表現する。
//            インボリュートを基礎円まで無理に伸ばさない。
//
// 【前提】
//   ラックカッタの角は尖っているものとして扱う。実際のホブ／ラックは先端に丸みがあり、
//   その丸みの包絡線（二次トロコイド）が本来の歯元曲線になる。ここではそこまでは追わない。
//
// 【モジュールを 2 つ受け取る理由】
//   はすば歯車では、円周方向の寸法は正面モジュール mt = mn / cos(β) で決まるのに対し、
//   半径方向の歯たけ（歯末のたけ・歯元のたけ）は法線モジュール mn を基準に決まる。
//   この 2 つを別々に受け取る。平歯車では両方に同じ値を入れればよい。
//
// 【圧力角も 2 つ受け取る理由】
//   ピッチ円上の歯厚は正面で st = mt(π/2 + 2x·tanαn) となり、tan に入るのは
//   法線圧力角 αn。基礎円半径やラック歯面の傾きに使うのは正面圧力角 αt。
//   平歯車では両方に同じ値を入れればよい。
//
// 【歯数も 2 つ受け取る理由】
//   かさ歯車はトレッドゴルドの相当平歯車で歯形を決める。その歯数 zv = z/cos(δ) は
//   一般に整数にならない。ピッチ円半径とピッチ角は zv で決まる一方、実際に並べる歯は
//   z 枚（整数）で、虚数平面での 1 周ぶんは 2π·cos(δ) にしかならない。
//   VirtualToothCount に zv、ToothCount に z を入れるとこの使い分けになる。
//   平歯車・はすば歯車は VirtualToothCount を 0 のままにすればよい。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.PrimitiveMesh
{
    public static class InvoluteTrochoidSection
    {
        // ================================================================
        // 入力
        // ================================================================

        /// <summary>正面断面の諸元。角度はラジアン。</summary>
        public struct SectionInput
        {
            /// <summary>歯数 z。輪郭に並べる歯の枚数。</summary>
            public int ToothCount;

            /// <summary>
            /// 歯形の寸法を決める歯数。ピッチ円半径とピッチ角がこれで決まる。
            /// 0 以下なら ToothCount と同じ扱いになる（平歯車・はすば歯車）。
            /// かさ歯車は相当平歯車の歯数 zv = z/cos(δ) を入れる。
            /// </summary>
            public float VirtualToothCount;

            /// <summary>正面モジュール mt。円周方向の寸法を決める。</summary>
            public float TransverseModule;

            /// <summary>
            /// 半径方向の歯たけを決めるモジュール。
            /// 平歯車では正面モジュールと同じ、はすば歯車では法線モジュール mn。
            /// </summary>
            public float RadialModule;

            /// <summary>正面圧力角 αt。基礎円とラック歯面の傾きに使う。</summary>
            public float TransversePressureAngle;

            /// <summary>法線圧力角 αn。転位ぶんの歯厚増分に使う。</summary>
            public float NormalPressureAngle;

            /// <summary>転位係数 x。半径方向へ RadialModule × x だけラックを動かす。</summary>
            public float ProfileShift;

            /// <summary>正面バックラッシ（長さ）。ピッチ円上の歯厚をこの分だけ削る。</summary>
            public float Backlash;

            /// <summary>歯末のたけ係数 ha*。歯先円半径 = ピッチ円半径 + RadialModule(ha* + x)。</summary>
            public float AddendumCoef;

            /// <summary>歯元のたけ係数 hf*。ラックカッタが食い込む量 = RadialModule × hf*。</summary>
            public float DedendumCoef;
        }

        /// <summary>輪郭の標本数。</summary>
        public struct Samples
        {
            public int Trochoid;
            public int Involute;
            public int TipArc;
            public int RootArc;
        }

        /// <summary>
        /// 輪郭上の 1 点を極座標で持つ。
        /// 角度は歯をまたいで増え続ける値で、±π へ丸めていない。
        /// </summary>
        public struct PolarPoint
        {
            public float Radius;
            public float Angle;

            public PolarPoint(float radius, float angle)
            {
                Radius = radius;
                Angle = angle;
            }
        }

        // ================================================================
        // 導出諸元
        // ================================================================

        /// <summary>入力から求まる、輪郭生成に必要な値ひとそろい。</summary>
        public struct GearData
        {
            /// <summary>輪郭に並べる歯の枚数。</summary>
            public int z;

            /// <summary>寸法を決める歯数。平歯車では z と同じ、かさ歯車では zv。</summary>
            public float zGeom;

            /// <summary>正面圧力角</summary>
            public float alpha;

            /// <summary>正面モジュール</summary>
            public float mt;
            /// <summary>半径方向の歯たけを決めるモジュール</summary>
            public float mr;

            public float rPitch;
            public float rBase;
            public float rAddendum;
            public float rRoot;

            /// <summary>歯元を削るラックカッタの歯末のたけ。標準全歯たけなら 1.25 mr。</summary>
            public float rackCutterAddendum;

            /// <summary>転位を含めた、ピッチ円から歯元円までの半径方向距離。</summary>
            public float effectiveDedendum;

            /// <summary>ラック歯の中心から見た、尖った角の接線方向座標。</summary>
            public float rackCornerTangential;

            public float toothThicknessPitch;
            public float halfToothAnglePitch;
            public float halfPitchAngle;
            public float pitchAngle;
            public float invAlpha;
            public float backlashHalfAngle;
        }

        /// <summary>トロコイドがインボリュートへ受け渡す位置。</summary>
        public struct JoinData
        {
            public float tRoot;
            public float tJoin;
            public float tContact;
            public float rJoin;
            public bool undercut;
            public bool involuteExists;
            public bool severeUndercut;
        }

        // ================================================================
        // 諸元
        // ================================================================

        /// <summary>
        /// 正面諸元を求める。成立しないときは false。
        ///
        /// 判定は平歯車（InvoluteTrochoidGearMeshGenerator）が元から持っていたものと同じにしてある。
        /// 歯先円が基礎円より内側という条件はここでは弾かない。その場合はインボリュート歯面が
        /// 残らないだけで、切り下げの経路が輪郭を作れる。
        /// </summary>
        public static bool TryGetGearData(SectionInput s, out GearData g)
        {
            g = default;

            if (s.ToothCount < 3 ||
                s.TransverseModule <= 0f ||
                s.RadialModule <= 0f ||
                s.AddendumCoef <= 0f ||
                s.DedendumCoef <= 0f ||
                s.TransversePressureAngle <= 0f ||
                s.TransversePressureAngle >= 45f * Mathf.Deg2Rad)
            {
                return false;
            }

            float alpha = s.TransversePressureAngle;
            float mt = s.TransverseModule;
            float mr = s.RadialModule;

            // 寸法を決める歯数。指定がなければ並べる歯数をそのまま使う。
            float zGeom = s.VirtualToothCount > 0f ? s.VirtualToothCount : s.ToothCount;

            if (zGeom <= 0f) return false;

            float rPitch = mt * zGeom * 0.5f;

            // 標準全歯たけのラックカッタ：歯元へ食い込む量 = hf* × mr。
            float rackCutterAddendum = s.DedendumCoef * mr;

            // 転位係数 x はラックを半径方向へ x·mr 動かす。
            // 生成される歯元半径： rf = rp - mr(hf* - x)
            float effectiveDedendum = rackCutterAddendum - s.ProfileShift * mr;

            float rAddendum = rPitch + mr * (s.AddendumCoef + s.ProfileShift);
            float rRoot = rPitch - effectiveDedendum;
            float rBase = rPitch * Mathf.Cos(alpha);

            if (rRoot <= 0f || rAddendum <= 0f || rAddendum <= rRoot)
                return false;

            // 転位外歯車のピッチ円上歯厚。転位ぶんの増分は法線圧力角で決まる。
            float toothThicknessPitch =
                mt * (Mathf.PI * 0.5f + 2f * s.ProfileShift * Mathf.Tan(s.NormalPressureAngle))
                - s.Backlash;

            if (toothThicknessPitch <= 0f)
                return false;

            float pitchAngle = 2f * Mathf.PI / zGeom;
            float halfPitchAngle = 0.5f * pitchAngle;
            float halfToothAnglePitch = toothThicknessPitch / (2f * rPitch);

            if (2f * halfToothAnglePitch >= pitchAngle)
                return false;

            // 尖ったラック角の位置：
            //   ラック基準線上での半歯厚は正面ピッチの 1/4、すなわち π·mt/4。
            //   基準線からカッタ先端へ rackCutterAddendum 進むと、
            //   接線方向座標が rackCutterAddendum·tan(αt) だけ減る。
            //   転位はラックの半径方向位置を変えるが、このラック内ローカル座標は変えない。
            float rackCornerTangential =
                Mathf.PI * mt * 0.25f
                - rackCutterAddendum * Mathf.Tan(alpha);

            g = new GearData
            {
                z = s.ToothCount,
                zGeom = zGeom,
                alpha = alpha,

                mt = mt,
                mr = mr,

                rPitch = rPitch,
                rBase = rBase,
                rAddendum = rAddendum,
                rRoot = rRoot,

                rackCutterAddendum = rackCutterAddendum,
                effectiveDedendum = effectiveDedendum,
                rackCornerTangential = rackCornerTangential,

                toothThicknessPitch = toothThicknessPitch,
                halfToothAnglePitch = halfToothAnglePitch,
                halfPitchAngle = halfPitchAngle,
                pitchAngle = pitchAngle,
                invAlpha = InvoluteFunction(alpha),

                // バックラッシは全歯厚を Backlash だけ減らすので、
                // 片側の歯面はピッチ円上で Backlash/2 ぶん内側へ寄る。
                backlashHalfAngle = s.Backlash / (2f * rPitch)
            };

            return true;
        }

        /// <summary>インボリュート関数 inv(φ) = tanφ - φ。</summary>
        public static float InvoluteFunction(float phi) => Mathf.Tan(phi) - phi;

        /// <summary>
        /// 半径 r における、歯の中心線からの角度半歯厚。基礎円以上で有効。
        /// </summary>
        public static float HalfThicknessAngleAtRadius(GearData g, float r)
        {
            float rr = Mathf.Max(r, g.rBase);
            float c = Mathf.Clamp(g.rBase / rr, -1f, 1f);
            float phi = Mathf.Acos(c);

            return g.halfToothAnglePitch
                 + g.invAlpha
                 - InvoluteFunction(phi);
        }

        // ================================================================
        // トロコイド
        // ================================================================

        /// <summary>
        /// ラック角トロコイドのパラメータ表示。
        ///
        ///   X =  A cos(t) + B sin(t)
        ///   Y = -A sin(t) + B cos(t)
        ///   A = rp - effectiveDedendum = 歯元半径
        ///   B = rp·t + rackCornerTangential
        ///
        /// 返す角度はラック歯空間の基準から、生成される歯の中心線基準へ直したもの。
        /// </summary>
        public static void EvaluateTrochoid(
            GearData g, float t, out float radius, out float flankHalfAngle)
        {
            float A = g.rRoot;
            float B = g.rPitch * t + g.rackCornerTangential;

            float ct = Mathf.Cos(t);
            float st = Mathf.Sin(t);

            float x = A * ct + B * st;
            float y = -A * st + B * ct;

            radius = Mathf.Sqrt(x * x + y * y);

            float angleFromSpaceCenter = Mathf.Atan2(y, x);

            // ラックカッタの歯は歯溝の中心に来る。隣の歯の中心線から測った半角へ直す。
            flankHalfAngle =
                g.halfPitchAngle
                - angleFromSpaceCenter
                - g.backlashHalfAngle;
        }

        /// <summary>
        /// トロコイドが指定半径に達するパラメータ t。最小半径点より後の枝を使う。
        /// </summary>
        public static float TrochoidTAtRadius(GearData g, float radius)
        {
            float A = g.rRoot;
            float rr = Mathf.Max(radius, Mathf.Abs(A));

            float b2 = rr * rr - A * A;
            float B = Mathf.Sqrt(Mathf.Max(0f, b2));

            return (B - g.rackCornerTangential) / g.rPitch;
        }

        /// <summary>トロコイドの最小半径は B=0 のとき。その半径は歯元半径。</summary>
        public static float TrochoidRootParameter(GearData g)
            => -g.rackCornerTangential / g.rPitch;

        /// <summary>
        /// ラック歯面の接触点が尖った角に到達するパラメータ。
        /// 角を通る歯面の法線は瞬間ピッチ点を通る。半径方向距離 h と圧力角 α で B = h·cot(α)。
        /// </summary>
        public static float TrochoidCornerContactParameter(GearData g)
        {
            float tanA = Mathf.Tan(g.alpha);

            if (Mathf.Abs(tanA) < 1e-8f)
                return TrochoidRootParameter(g);

            float B = g.effectiveDedendum / tanA;

            return (B - g.rackCornerTangential) / g.rPitch;
        }

        /// <summary>
        /// パラメータ t のトロコイド点における、トロコイド半角とインボリュート半角の差。
        /// 0 なら両曲線が交わる。
        /// </summary>
        private static float TrochoidInvoluteAngleDifference(GearData g, float t)
        {
            EvaluateTrochoid(g, t, out float r, out float trochoidAngle);

            if (r < g.rBase)
                return float.NaN;

            float involuteAngle = HalfThicknessAngleAtRadius(g, r);
            return trochoidAngle - involuteAngle;
        }

        private static float BisectTrochoidInvoluteIntersection(
            GearData g, float ta, float tb, int iterations = 48)
        {
            float fa = TrochoidInvoluteAngleDifference(g, ta);
            float fb = TrochoidInvoluteAngleDifference(g, tb);

            if (float.IsNaN(fa)) return tb;
            if (float.IsNaN(fb)) return ta;

            for (int i = 0; i < iterations; i++)
            {
                float tm = 0.5f * (ta + tb);
                float fm = TrochoidInvoluteAngleDifference(g, tm);

                if (float.IsNaN(fm))
                {
                    ta = tm;
                    continue;
                }

                if (Mathf.Abs(fm) < 1e-8f)
                    return tm;

                if (fa * fm <= 0f)
                {
                    tb = tm;
                    fb = fm;
                }
                else
                {
                    ta = tm;
                    fa = fm;
                }
            }

            return 0.5f * (ta + tb);
        }

        /// <summary>
        /// 歯元がインボリュートへ受け渡す位置を求める。
        ///
        ///   通常の歯   … ラック角の接触点を使う。
        ///   切り下げ歯 … その手前でトロコイドがインボリュートを横切る。基礎円より上の最初の交点を使い、
        ///                 ラック角が削り取ったぶんの下側インボリュートを落とす。
        /// </summary>
        public static JoinData FindTrochoidInvoluteJoin(GearData g)
        {
            float tRoot = TrochoidRootParameter(g);
            float tContact = TrochoidCornerContactParameter(g);
            float tTip = TrochoidTAtRadius(g, g.rAddendum);

            float searchEnd = Mathf.Min(tContact, tTip);

            float tBase;
            if (g.rBase <= g.rRoot)
                tBase = tRoot;
            else
                tBase = TrochoidTAtRadius(g, g.rBase);

            float searchStart = Mathf.Max(tRoot, tBase);

            bool foundIntersection = false;
            float tIntersection = searchEnd;

            if (searchStart < searchEnd)
            {
                const int scanSteps = 256;

                float prevT = searchStart;
                float prevF = TrochoidInvoluteAngleDifference(g, prevT);

                for (int i = 1; i <= scanSteps; i++)
                {
                    float u = i / (float)scanSteps;
                    float t = Mathf.Lerp(searchStart, searchEnd, u);
                    float f = TrochoidInvoluteAngleDifference(g, t);

                    if (!float.IsNaN(prevF) && !float.IsNaN(f))
                    {
                        if (Mathf.Abs(prevF) < 1e-7f)
                        {
                            tIntersection = prevT;
                            foundIntersection = true;
                            break;
                        }

                        if (prevF * f < 0f || Mathf.Abs(f) < 1e-7f)
                        {
                            tIntersection = BisectTrochoidInvoluteIntersection(g, prevT, t);
                            foundIntersection = true;
                            break;
                        }
                    }

                    prevT = t;
                    prevF = f;
                }
            }

            float tJoin;
            bool undercut = false;
            bool severeUndercut = false;

            if (foundIntersection)
            {
                // 理論上のラック角接触点よりだいぶ手前で交わったなら、カッタがインボリュートを削っている。
                tJoin = tIntersection;
                undercut = tJoin < tContact - 1e-4f;
            }
            else
            {
                tJoin = Mathf.Min(tContact, tTip);

                if (tContact > tTip + 1e-5f)
                    severeUndercut = true;
            }

            EvaluateTrochoid(g, tJoin, out float rJoin, out _);

            bool involuteExists =
                rJoin < g.rAddendum - 1e-6f &&
                rJoin >= g.rBase - 1e-5f;

            return new JoinData
            {
                tRoot = tRoot,
                tJoin = tJoin,
                tContact = tContact,
                rJoin = rJoin,
                undercut = undercut,
                involuteExists = involuteExists,
                severeUndercut = severeUndercut
            };
        }

        // ================================================================
        // 輪郭
        // ================================================================

        /// <summary>
        /// 極座標のまま輪郭を作る。角度は歯をまたいで増え続け、±π で折り返さない。
        ///
        /// かさ歯車は、この角度を cos(δ) で割って実際の断面へ写す。
        /// 直交座標へ直してから atan2 で角度を取り直すと折り返しで壊れるため、
        /// 写す側は必ずこちらを使うこと。
        ///
        /// 歯 1 枚あたり：
        ///   歯元 → 左トロコイド → 左インボリュート → 歯先円弧
        ///        → 右インボリュート → 右トロコイド → 次の歯元までの円弧
        /// </summary>
        /// <param name="g">TryGetGearData が返した諸元。</param>
        /// <param name="smp">標本数。値域は内部でクランプする。</param>
        /// <param name="rotationRad">全体の回転オフセット（ラジアン）。</param>
        public static List<PolarPoint> GeneratePolarOutline(
            GearData g, Samples smp, float rotationRad)
        {
            JoinData join = FindTrochoidInvoluteJoin(g);

            int troSamples = Mathf.Clamp(smp.Trochoid, 3, 64);
            int invSamples = Mathf.Clamp(smp.Involute, 3, 64);
            int tipSamples = Mathf.Clamp(smp.TipArc, 1, 16);
            int rootSamples = Mathf.Clamp(smp.RootArc, 1, 16);

            var outline = new List<PolarPoint>(
                g.z * (troSamples * 2 + invSamples * 2 + tipSamples + rootSamples + 8));

            EvaluateTrochoid(g, join.tRoot, out float rootR, out float betaRoot);
            EvaluateTrochoid(g, join.tJoin, out float joinR, out float betaJoin);

            float betaTip = HalfThicknessAngleAtRadius(g, g.rAddendum);

            for (int tooth = 0; tooth < g.z; tooth++)
            {
                float c = rotationRad + tooth * g.pitchAngle;

                // 左の歯元トロコイド：歯元 → インボリュート接続点
                for (int j = 0; j <= troSamples; j++)
                {
                    float u = j / (float)troSamples;
                    float t = Mathf.Lerp(join.tRoot, join.tJoin, u);

                    EvaluateTrochoid(g, t, out float r, out float beta);

                    outline.Add(new PolarPoint(r, c - beta));
                }

                // 左のインボリュート：接続点 → 歯先
                if (join.involuteExists)
                {
                    for (int j = 1; j <= invSamples; j++)
                    {
                        float u = j / (float)invSamples;
                        float r = Mathf.Lerp(joinR, g.rAddendum, u);
                        float beta = HalfThicknessAngleAtRadius(g, r);

                        outline.Add(new PolarPoint(r, c - beta));
                    }
                }
                else
                {
                    // 激しい切り下げ：トロコイドがすでに歯先域まで達している。
                    betaTip = betaJoin;
                }

                // 歯先：左 → 右
                for (int j = 1; j <= tipSamples; j++)
                {
                    float u = j / (float)tipSamples;
                    float a = Mathf.Lerp(c - betaTip, c + betaTip, u);

                    outline.Add(new PolarPoint(g.rAddendum, a));
                }

                // 右のインボリュート：歯先 → 接続点
                if (join.involuteExists)
                {
                    for (int j = 1; j <= invSamples; j++)
                    {
                        float u = j / (float)invSamples;
                        float r = Mathf.Lerp(g.rAddendum, joinR, u);
                        float beta = HalfThicknessAngleAtRadius(g, r);

                        outline.Add(new PolarPoint(r, c + beta));
                    }
                }

                // 右の歯元トロコイド：接続点 → 歯元（左の鏡像）
                for (int j = 1; j <= troSamples; j++)
                {
                    float u = j / (float)troSamples;
                    float t = Mathf.Lerp(join.tJoin, join.tRoot, u);

                    EvaluateTrochoid(g, t, out float r, out float beta);

                    outline.Add(new PolarPoint(r, c + beta));
                }

                // 歯元円弧：この歯 → 次の歯
                float rootRight = c + betaRoot;
                float nextRootLeft = c + g.pitchAngle - betaRoot;

                for (int j = 1; j < rootSamples; j++)
                {
                    float u = j / (float)rootSamples;
                    float a = Mathf.Lerp(rootRight, nextRootLeft, u);

                    outline.Add(new PolarPoint(rootR, a));
                }
            }

            return outline;
        }

        /// <summary>
        /// XY 平面の閉じた輪郭（CCW）を 1 本作る。
        /// GeneratePolarOutline を直交座標へ直し、隣り合う重複点を落としたもの。
        /// </summary>
        /// <param name="g">TryGetGearData が返した諸元。</param>
        /// <param name="smp">標本数。値域は内部でクランプする。</param>
        /// <param name="rotationRad">全体の回転オフセット（ラジアン）。</param>
        public static List<Vector2> GenerateOutline(GearData g, Samples smp, float rotationRad)
        {
            List<PolarPoint> polar = GeneratePolarOutline(g, smp, rotationRad);

            var outline = new List<Vector2>(polar.Count);
            for (int i = 0; i < polar.Count; i++)
                outline.Add(GearDiskBuilder.Polar(polar[i].Radius, polar[i].Angle));

            GearDiskBuilder.RemoveNearlyDuplicateNeighbors(outline, 1e-12f);
            return outline;
        }

        // ================================================================
        // 切り下げ限界歯数
        // ================================================================

        /// <summary>
        /// 転位なしでの切り下げ限界歯数の目安 z_min = 2·ha* / sin²α。
        /// 圧力角が 0 に近いときは 0 を返す。
        /// </summary>
        public static float MinToothCountApprox(float pressureAngleRad, float addendumCoef)
        {
            float sinA = Mathf.Sin(pressureAngleRad);
            if (sinA <= 1e-5f) return 0f;

            return 2f * addendumCoef / (sinA * sinA);
        }
    }
}
