// InvoluteTrochoidGearMeshGenerator.cs
// インボリュート歯形＋ラックカッタ生成トロコイド歯元の平歯車メッシュ生成（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
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
// 【押し出し】GearDiskBuilder が受け持つ。中心の丸穴もそこで開ける。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class InvoluteTrochoidGearMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct InvoluteGearParams : IEquatable<InvoluteGearParams>
        {
            public string MeshName;

            // ── 基本諸元 ──
            /// <summary>歯数 z</summary>
            public int ToothCount;
            /// <summary>モジュール m</summary>
            public float Module;
            /// <summary>圧力角 α（度）</summary>
            public float PressureAngleDeg;
            /// <summary>厚み</summary>
            public float Thickness;

            // ── 転位・バックラッシ ──
            /// <summary>転位係数 x</summary>
            public float ProfileShift;
            /// <summary>ピッチ円上のバックラッシ</summary>
            public float Backlash;

            // ── 穴 ──
            /// <summary>中心の丸穴半径。0 で穴なし。</summary>
            public float BoreRadius;
            /// <summary>穴リングの分割数</summary>
            public int BoreSegments;

            // ── 曲線のサンプル数 ──
            /// <summary>歯面 1 本あたりのトロコイド分割数</summary>
            public int TrochoidSamples;
            /// <summary>歯面 1 本あたりのインボリュート分割数</summary>
            public int InvoluteSamples;
            /// <summary>歯先円弧の分割数</summary>
            public int TipArcSamples;
            /// <summary>歯元円弧の分割数</summary>
            public int RootArcSamples;

            // ── 配置 ──
            /// <summary>全体の回転オフセット（度）</summary>
            public float RotationOffsetDeg;

            /// <summary>板を置く平面</summary>
            public PlaneOrientation Orientation;
            /// <summary>生成後にメッシュ全体の面を反転する</summary>
            public bool FlipFaces;
            /// <summary>AABB サイズ基準のピボット</summary>
            public Vector3 Pivot;

            public static InvoluteGearParams Default => new InvoluteGearParams
            {
                MeshName          = "InvoluteGear",
                ToothCount        = 16,
                Module            = 0.1f,
                PressureAngleDeg  = 20f,
                Thickness         = 0.2f,
                ProfileShift      = 0f,
                Backlash          = 0f,
                BoreRadius        = 0.3f,
                BoreSegments      = 24,
                TrochoidSamples   = 12,
                InvoluteSamples   = 16,
                TipArcSamples     = 3,
                RootArcSamples    = 4,
                RotationOffsetDeg = 0f,
                Orientation       = PlaneOrientation.XY,
                FlipFaces         = false,
                Pivot             = Vector3.zero,
            };

            public bool Equals(InvoluteGearParams o) =>
                MeshName == o.MeshName &&
                ToothCount == o.ToothCount &&
                Mathf.Approximately(Module,            o.Module)            &&
                Mathf.Approximately(PressureAngleDeg,  o.PressureAngleDeg)  &&
                Mathf.Approximately(Thickness,         o.Thickness)         &&
                Mathf.Approximately(ProfileShift,      o.ProfileShift)      &&
                Mathf.Approximately(Backlash,          o.Backlash)          &&
                Mathf.Approximately(BoreRadius,        o.BoreRadius)        &&
                BoreSegments == o.BoreSegments &&
                TrochoidSamples == o.TrochoidSamples &&
                InvoluteSamples == o.InvoluteSamples &&
                TipArcSamples   == o.TipArcSamples   &&
                RootArcSamples  == o.RootArcSamples  &&
                Mathf.Approximately(RotationOffsetDeg, o.RotationOffsetDeg) &&
                Orientation == o.Orientation &&
                FlipFaces   == o.FlipFaces   &&
                Pivot       == o.Pivot;

            public override bool Equals(object obj) => obj is InvoluteGearParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 内部データ
        // ================================================================

        private struct GearData
        {
            public int z;
            public float alpha;

            public float rPitch;
            public float rBase;
            public float rAddendum;
            public float rRoot;

            /// <summary>歯元を削るラックカッタの歯末のたけ。標準全歯たけなら 1.25 m。</summary>
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

        private struct JoinData
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
        // 派生諸元（UI 表示用）
        // ================================================================

        /// <summary>パネルに出す派生諸元。</summary>
        public struct GearInfo
        {
            /// <summary>諸元が成立しているか。false のとき他のフィールドは無効。</summary>
            public bool Valid;

            public float PitchDiameter;
            public float BaseDiameter;
            public float TipDiameter;
            public float RootDiameter;
            public float CircularPitch;
            public float ToothThicknessPitch;
            public float JoinRadius;

            /// <summary>切り下げが起きているか。</summary>
            public bool Undercut;
            /// <summary>インボリュート歯面がほとんど残らないほどの切り下げか。</summary>
            public bool SevereUndercut;
            /// <summary>転位なしで切り下げ限界歯数を下回っているか。</summary>
            public bool BelowMinToothCount;
            /// <summary>切り下げ限界歯数の目安。</summary>
            public float MinToothCountApprox;
            /// <summary>穴半径が歯元半径以上か。</summary>
            public bool BoreTooLarge;
        }

        /// <summary>派生諸元を求める。パラメータが不正なら Valid=false を返す。</summary>
        public static GearInfo GetInfo(InvoluteGearParams p)
        {
            var info = new GearInfo { Valid = false };

            if (!TryGetGearData(p, out GearData g))
                return info;

            JoinData join = FindTrochoidInvoluteJoin(g);

            info.Valid               = true;
            info.PitchDiameter       = 2f * g.rPitch;
            info.BaseDiameter        = 2f * g.rBase;
            info.TipDiameter         = 2f * g.rAddendum;
            info.RootDiameter        = 2f * g.rRoot;
            info.CircularPitch       = Mathf.PI * p.Module;
            info.ToothThicknessPitch = g.toothThicknessPitch;
            info.JoinRadius          = join.rJoin;
            info.Undercut            = join.undercut;
            info.SevereUndercut      = join.severeUndercut;
            info.BoreTooLarge        = p.BoreRadius > 0f && p.BoreRadius >= g.rRoot;

            float sinA = Mathf.Sin(g.alpha);
            if (sinA > 1e-5f)
            {
                float zMin = 2f / (sinA * sinA);
                info.MinToothCountApprox = zMin;
                info.BelowMinToothCount =
                    Mathf.Abs(p.ProfileShift) < 1e-6f &&
                    p.ToothCount < Mathf.CeilToInt(zMin);
            }

            return info;
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// 歯車メッシュを作る。パラメータが不正なときは空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(InvoluteGearParams p)
        {
            if (!TryGetGearData(p, out GearData g))
                return new MeshObject(string.IsNullOrEmpty(p.MeshName) ? "InvoluteGear" : p.MeshName);

            // 穴は歯元円より小さくする（外形へ食い込ませない）。
            float bore = Mathf.Max(0f, p.BoreRadius);
            if (bore > 0f && bore >= g.rRoot) bore = g.rRoot * 0.95f;

            var outline = GenerateOutline(g, p);

            return GearDiskBuilder.Build(
                p.MeshName,
                outline,
                p.Thickness,
                bore,
                p.BoreSegments,
                p.Orientation,
                p.FlipFaces,
                p.Pivot);
        }

        // ================================================================
        // 諸元
        // ================================================================

        private static bool TryGetGearData(InvoluteGearParams p, out GearData g)
        {
            g = default;

            if (p.ToothCount < 3 ||
                p.Module <= 0f ||
                p.Thickness < 0f ||
                p.PressureAngleDeg <= 0f ||
                p.PressureAngleDeg >= 45f)
            {
                return false;
            }

            float alpha = p.PressureAngleDeg * Mathf.Deg2Rad;
            float rPitch = p.Module * p.ToothCount * 0.5f;

            // 標準全歯たけのラックカッタ：歯元へ食い込む量 = 1.25 m。
            float rackCutterAddendum = 1.25f * p.Module;

            // 転位係数 x はラックを半径方向へ x*m 動かす。
            // 生成される歯元半径： rf = rp - m(1.25 - x)
            float effectiveDedendum = rackCutterAddendum - p.ProfileShift * p.Module;

            float rAddendum = rPitch + p.Module * (1f + p.ProfileShift);
            float rRoot = rPitch - effectiveDedendum;
            float rBase = rPitch * Mathf.Cos(alpha);

            if (rRoot <= 0f || rAddendum <= 0f || rAddendum <= rRoot)
                return false;

            // 転位外歯車のピッチ円上歯厚。
            float toothThicknessPitch =
                p.Module * (Mathf.PI * 0.5f + 2f * p.ProfileShift * Mathf.Tan(alpha))
                - p.Backlash;

            if (toothThicknessPitch <= 0f)
                return false;

            float pitchAngle = 2f * Mathf.PI / p.ToothCount;
            float halfPitchAngle = 0.5f * pitchAngle;
            float halfToothAnglePitch = toothThicknessPitch / (2f * rPitch);

            if (2f * halfToothAnglePitch >= pitchAngle)
                return false;

            // 尖ったラック角の位置：
            //   ラック基準線上での半歯厚は πm/4。
            //   基準線からカッタ先端へ 1.25m 進むと、接線方向座標が 1.25m*tan(α) だけ減る。
            //   転位はラックの半径方向位置を変えるが、このラック内ローカル座標は変えない。
            float rackCornerTangential =
                Mathf.PI * p.Module * 0.25f
                - rackCutterAddendum * Mathf.Tan(alpha);

            g = new GearData
            {
                z = p.ToothCount,
                alpha = alpha,

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
                backlashHalfAngle = p.Backlash / (2f * rPitch)
            };

            return true;
        }

        private static float InvoluteFunction(float phi) => Mathf.Tan(phi) - phi;

        /// <summary>
        /// 半径 r における、歯の中心線からの角度半歯厚。基礎円以上で有効。
        /// </summary>
        private static float HalfThicknessAngleAtRadius(GearData g, float r)
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
        ///   B = rp*t + rackCornerTangential
        ///
        /// 返す角度はラック歯空間の基準から、生成される歯の中心線基準へ直したもの。
        /// </summary>
        private static void EvaluateTrochoid(
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
        private static float TrochoidTAtRadius(GearData g, float radius)
        {
            float A = g.rRoot;
            float rr = Mathf.Max(radius, Mathf.Abs(A));

            float b2 = rr * rr - A * A;
            float B = Mathf.Sqrt(Mathf.Max(0f, b2));

            return (B - g.rackCornerTangential) / g.rPitch;
        }

        /// <summary>トロコイドの最小半径は B=0 のとき。その半径は歯元半径。</summary>
        private static float TrochoidRootParameter(GearData g)
            => -g.rackCornerTangential / g.rPitch;

        /// <summary>
        /// ラック歯面の接触点が尖った角に到達するパラメータ。
        /// 角を通る歯面の法線は瞬間ピッチ点を通る。半径方向距離 h と圧力角 α で B = h*cot(α)。
        /// </summary>
        private static float TrochoidCornerContactParameter(GearData g)
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
        private static JoinData FindTrochoidInvoluteJoin(GearData g)
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
        /// XY 平面の閉じた輪郭（CCW）を 1 本作る。
        ///
        /// 歯 1 枚あたり：
        ///   歯元円弧 → 左トロコイド → 左インボリュート → 歯先円弧
        ///            → 右インボリュート → 右トロコイド → 次の歯元円弧
        /// </summary>
        private static List<Vector2> GenerateOutline(GearData g, InvoluteGearParams p)
        {
            JoinData join = FindTrochoidInvoluteJoin(g);

            int troSamples = Mathf.Clamp(p.TrochoidSamples, 3, 64);
            int invSamples = Mathf.Clamp(p.InvoluteSamples, 3, 64);
            int tipSamples = Mathf.Clamp(p.TipArcSamples, 1, 16);
            int rootSamples = Mathf.Clamp(p.RootArcSamples, 1, 16);

            var outline = new List<Vector2>(
                g.z * (troSamples * 2 + invSamples * 2 + tipSamples + rootSamples + 8));

            float rotation = p.RotationOffsetDeg * Mathf.Deg2Rad;

            EvaluateTrochoid(g, join.tRoot, out float rootR, out float betaRoot);
            EvaluateTrochoid(g, join.tJoin, out float joinR, out float betaJoin);

            float betaTip = HalfThicknessAngleAtRadius(g, g.rAddendum);

            for (int tooth = 0; tooth < g.z; tooth++)
            {
                float c = rotation + tooth * g.pitchAngle;

                // 左の歯元トロコイド：歯元 → インボリュート接続点
                for (int j = 0; j <= troSamples; j++)
                {
                    float u = j / (float)troSamples;
                    float t = Mathf.Lerp(join.tRoot, join.tJoin, u);

                    EvaluateTrochoid(g, t, out float r, out float beta);

                    outline.Add(GearDiskBuilder.Polar(r, c - beta));
                }

                // 左のインボリュート：接続点 → 歯先
                if (join.involuteExists)
                {
                    for (int j = 1; j <= invSamples; j++)
                    {
                        float u = j / (float)invSamples;
                        float r = Mathf.Lerp(joinR, g.rAddendum, u);
                        float beta = HalfThicknessAngleAtRadius(g, r);

                        outline.Add(GearDiskBuilder.Polar(r, c - beta));
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

                    outline.Add(GearDiskBuilder.Polar(g.rAddendum, a));
                }

                // 右のインボリュート：歯先 → 接続点
                if (join.involuteExists)
                {
                    for (int j = 1; j <= invSamples; j++)
                    {
                        float u = j / (float)invSamples;
                        float r = Mathf.Lerp(g.rAddendum, joinR, u);
                        float beta = HalfThicknessAngleAtRadius(g, r);

                        outline.Add(GearDiskBuilder.Polar(r, c + beta));
                    }
                }

                // 右の歯元トロコイド：接続点 → 歯元（左の鏡像）
                for (int j = 1; j <= troSamples; j++)
                {
                    float u = j / (float)troSamples;
                    float t = Mathf.Lerp(join.tJoin, join.tRoot, u);

                    EvaluateTrochoid(g, t, out float r, out float beta);

                    outline.Add(GearDiskBuilder.Polar(r, c + beta));
                }

                // 歯元円弧：この歯 → 次の歯
                float rootRight = c + betaRoot;
                float nextRootLeft = c + g.pitchAngle - betaRoot;

                for (int j = 1; j < rootSamples; j++)
                {
                    float u = j / (float)rootSamples;
                    float a = Mathf.Lerp(rootRight, nextRootLeft, u);

                    outline.Add(GearDiskBuilder.Polar(rootR, a));
                }
            }

            GearDiskBuilder.RemoveNearlyDuplicateNeighbors(outline, 1e-12f);
            return outline;
        }
    }
}
