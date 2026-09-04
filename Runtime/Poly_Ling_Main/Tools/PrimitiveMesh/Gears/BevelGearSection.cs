// BevelGearSection.cs
// かさ歯車（すぐば／まがりば）のピッチ円錐まわりと断面列を作る共有部品。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【ピッチ円錐】
//   軸が交わる 2 軸の一般式。軸角 Σ、相手の歯数 z2 に対して
//
//       tan(δ1) = sin(Σ) / (z2/z1 + cos(Σ))
//       δ2 = Σ - δ1
//
//   外端のピッチ円半径 rp = m·z/2、円錐距離 R = rp / sin(δ)。
//   歯幅 b だけ内側へ寄った小端の円錐距離は R - b。相似比は (R-b)/R。
//
// 【歯形はトレッドゴルドの相当平歯車で決める】
//   背円錐を展開すると、半径 rv = rp / cos(δ) の平歯車になる。その歯数は
//
//       zv = z / cos(δ)
//
//   で、一般に整数にならない。歯形はこの相当平歯車として作り、あとで実際の断面へ写す。
//   歯形そのもの（インボリュート＋トロコイド歯元＋切り下げ判定）は
//   Gears/InvoluteTrochoidSection がそのまま受け持つ。
//
// 【展開の写し戻し】
//       実半径 = 仮想半径 · cos(δ)
//       実角度 = 仮想角度 / cos(δ)
//
//   仮想平面では z 枚の歯が 2π·cos(δ) ぶんしか占めない。写すとちょうど 1 周になる。
//   角度は ±π で折り返してはいけないので、輪郭は極座標のまま受け取る。
//
// 【まがりば】
//   歯すじが対数らせんを描く。円錐距離 ρ における回転量は
//
//       θ(ρ) = hand · tan(ψ) / sin(δ) · ln(ρ / Rm)
//
//   Rm は平均円錐距離。ψ はその位置での設計ねじれ角（平均ねじれ角）。
//   すぐばは ψ=0 として同じ式に載る。
//
// 【まがりばの圧力角】
//   法線圧力角 αn を、平均ねじれ角のところで正面圧力角へ直してから相当平歯車へ渡す。
//
//       tan(αt) = tan(αn) / cos(ψ)
//
//   すぐばは ψ=0 なので αt = αn。

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.PrimitiveMesh
{
    public static class BevelGearSection
    {
        // ================================================================
        // 入力
        // ================================================================

        /// <summary>かさ歯車の諸元。角度はラジアン。</summary>
        public struct BevelInput
        {
            /// <summary>歯数 z</summary>
            public int ToothCount;
            /// <summary>相手の歯数 z2。ピッチ円錐角を決めるためだけに使う。</summary>
            public int MatingToothCount;
            /// <summary>軸角 Σ</summary>
            public float ShaftAngle;

            /// <summary>外端モジュール m</summary>
            public float Module;
            /// <summary>法線圧力角 αn</summary>
            public float NormalPressureAngle;

            /// <summary>平均ねじれ角 ψ の大きさ。すぐばは 0。</summary>
            public float SpiralAngle;
            /// <summary>ねじれの向き。+1 / -1。すぐばでは使わない。</summary>
            public float SpiralHand;

            /// <summary>歯幅 b</summary>
            public float FaceWidth;

            /// <summary>バックラッシ（長さ）</summary>
            public float Backlash;
            /// <summary>歯末のたけ係数 ha*</summary>
            public float AddendumCoef;
            /// <summary>歯元のたけ係数 hf*</summary>
            public float DedendumCoef;
        }

        // ================================================================
        // 導出諸元
        // ================================================================

        /// <summary>入力から求まる、断面生成に必要な値ひとそろい。</summary>
        public struct BevelData
        {
            public int z;
            public int zMate;

            public float sigma;
            /// <summary>自分のピッチ円錐角 δ</summary>
            public float delta;
            /// <summary>相手のピッチ円錐角</summary>
            public float deltaMate;

            public float m;
            public float alphaN;
            /// <summary>相当平歯車へ渡す正面圧力角</summary>
            public float alphaT;

            /// <summary>歯末のたけ係数 ha*。切り下げ限界歯数の目安に使う。</summary>
            public float addendumCoef;

            public float spiralAngle;
            public float spiralHand;

            /// <summary>外端のピッチ円半径</summary>
            public float rPitchOuter;

            public float faceWidth;
            public float coneDistance;
            public float innerConeDistance;
            public float meanConeDistance;
            /// <summary>小端 / 外端の相似比</summary>
            public float innerScale;

            /// <summary>相当平歯車の歯数 zv = z/cos(δ)</summary>
            public float virtualToothCount;
            /// <summary>ねじれも含めた相当歯数 z/(cos δ · cos³ψ)。切り下げの目安に使う。</summary>
            public float formativeToothCount;

            /// <summary>外端での実歯先半径</summary>
            public float actualTipRadiusOuter;
            /// <summary>外端での実歯元半径</summary>
            public float actualRootRadiusOuter;

            /// <summary>頂点から外端ピッチ平面までの Z</summary>
            public float outerZFromApex;
            /// <summary>頂点から小端ピッチ平面までの Z</summary>
            public float innerZFromApex;

            /// <summary>相当平歯車の歯形諸元。</summary>
            public InvoluteTrochoidSection.GearData Section;
        }

        /// <summary>諸元を求める。成立しないときは false。</summary>
        public static bool TryGetBevelData(BevelInput b, out BevelData g)
        {
            g = default;

            if (b.ToothCount < 3 ||
                b.MatingToothCount < 3 ||
                b.Module <= 0f ||
                b.FaceWidth <= 0f ||
                b.AddendumCoef <= 0f ||
                b.DedendumCoef <= 0f ||
                b.NormalPressureAngle <= 0f ||
                b.NormalPressureAngle >= 45f * Mathf.Deg2Rad ||
                b.ShaftAngle <= 1f * Mathf.Deg2Rad ||
                b.ShaftAngle >= 179f * Mathf.Deg2Rad ||
                Mathf.Abs(b.SpiralAngle) >= 60f * Mathf.Deg2Rad)
            {
                return false;
            }

            float sigma = b.ShaftAngle;
            float ratio = b.MatingToothCount / (float)b.ToothCount;

            float delta = Mathf.Atan2(Mathf.Sin(sigma), ratio + Mathf.Cos(sigma));
            float deltaMate = sigma - delta;

            // どちらも外かさ歯車として成り立つ範囲でだけ作る。
            if (delta <= 0f || delta >= Mathf.PI * 0.5f ||
                deltaMate <= 0f || deltaMate >= Mathf.PI * 0.5f)
            {
                return false;
            }

            float sinDelta = Mathf.Sin(delta);
            float cosDelta = Mathf.Cos(delta);

            if (sinDelta <= 1e-6f || cosDelta <= 1e-6f) return false;

            float spiralAngle = Mathf.Abs(b.SpiralAngle);
            float cosSpiral = Mathf.Cos(spiralAngle);

            if (cosSpiral <= 1e-6f) return false;

            // 平均ねじれ角のところで法線圧力角を正面圧力角へ直す。
            float alphaT = Mathf.Atan(Mathf.Tan(b.NormalPressureAngle) / cosSpiral);

            float rPitchOuter = b.Module * b.ToothCount * 0.5f;
            float coneDistance = rPitchOuter / sinDelta;

            // 歯幅が円錐距離を超えると頂点を突き抜ける。
            if (b.FaceWidth >= coneDistance) return false;

            float innerConeDistance = coneDistance - b.FaceWidth;
            float innerScale = innerConeDistance / coneDistance;
            float meanConeDistance = coneDistance - 0.5f * b.FaceWidth;

            float virtualToothCount = b.ToothCount / cosDelta;

            // 歯形は相当平歯車として作る。転位は扱わない。
            var input = new InvoluteTrochoidSection.SectionInput
            {
                ToothCount              = b.ToothCount,
                VirtualToothCount       = virtualToothCount,
                TransverseModule        = b.Module,
                RadialModule            = b.Module,
                TransversePressureAngle = alphaT,
                NormalPressureAngle     = alphaT,
                ProfileShift            = 0f,
                Backlash                = b.Backlash,
                AddendumCoef            = b.AddendumCoef,
                DedendumCoef            = b.DedendumCoef,
            };

            if (!InvoluteTrochoidSection.TryGetGearData(input, out var section))
                return false;

            // 相当平歯車として歯面が残らない諸元は弾く。
            if (section.rAddendum <= section.rBase) return false;

            g = new BevelData
            {
                z = b.ToothCount,
                zMate = b.MatingToothCount,

                sigma = sigma,
                delta = delta,
                deltaMate = deltaMate,

                m = b.Module,
                alphaN = b.NormalPressureAngle,
                alphaT = alphaT,

                addendumCoef = b.AddendumCoef,

                spiralAngle = spiralAngle,
                spiralHand = b.SpiralHand < 0f ? -1f : 1f,

                rPitchOuter = rPitchOuter,

                faceWidth = b.FaceWidth,
                coneDistance = coneDistance,
                innerConeDistance = innerConeDistance,
                meanConeDistance = meanConeDistance,
                innerScale = innerScale,

                virtualToothCount = virtualToothCount,
                formativeToothCount =
                    b.ToothCount / (cosDelta * cosSpiral * cosSpiral * cosSpiral),

                // 背円錐の写し戻し：実半径 = 仮想半径 · cos(δ)
                actualTipRadiusOuter = section.rAddendum * cosDelta,
                actualRootRadiusOuter = section.rRoot * cosDelta,

                outerZFromApex = coneDistance * cosDelta,
                innerZFromApex = innerConeDistance * cosDelta,

                Section = section,
            };

            return true;
        }

        // ================================================================
        // まがりばの回転
        // ================================================================

        /// <summary>
        /// 円錐距離 ρ における歯すじの回転量。平均円錐距離で 0 になる。
        /// ねじれ角 0 のときは常に 0。
        /// </summary>
        public static float SpiralRotationAtConeDistance(BevelData g, float rho)
        {
            if (Mathf.Abs(g.spiralAngle) < 1e-8f) return 0f;

            float safeRho = Mathf.Max(rho, 1e-6f);
            float safeMean = Mathf.Max(g.meanConeDistance, 1e-6f);
            float sinDelta = Mathf.Max(Mathf.Sin(g.delta), 1e-6f);

            return g.spiralHand
                 * Mathf.Tan(g.spiralAngle) / sinDelta
                 * Mathf.Log(safeRho / safeMean);
        }

        // ================================================================
        // 外端の輪郭
        // ================================================================

        /// <summary>
        /// 外端（大端）の XY 断面を作る。
        ///
        /// 相当平歯車の輪郭を極座標のまま受け取り、背円錐の展開を写し戻す。
        /// 直交座標へ直してから角度を取り直すと ±π の折り返しで壊れるので、
        /// 必ず極座標のまま写すこと。
        /// </summary>
        /// <param name="g">TryGetBevelData が返した諸元。</param>
        /// <param name="smp">標本数。</param>
        /// <param name="rotationRad">実際の断面での回転オフセット（ラジアン）。</param>
        public static List<Vector2> BuildOuterOutline(
            BevelData g, InvoluteTrochoidSection.Samples smp, float rotationRad)
        {
            float cosDelta = Mathf.Cos(g.delta);

            // 実際の回転を、仮想平面では cos(δ) 倍に縮めて渡す。
            float virtualRotation = rotationRad * cosDelta;

            List<InvoluteTrochoidSection.PolarPoint> polar =
                InvoluteTrochoidSection.GeneratePolarOutline(g.Section, smp, virtualRotation);

            var outline = new List<Vector2>(polar.Count);

            for (int i = 0; i < polar.Count; i++)
            {
                float r = polar[i].Radius * cosDelta;
                float a = polar[i].Angle / cosDelta;

                outline.Add(GearDiskBuilder.Polar(r, a));
            }

            GearDiskBuilder.RemoveNearlyDuplicateNeighbors(outline, 1e-12f);
            return outline;
        }

        // ================================================================
        // 断面列
        // ================================================================

        /// <summary>
        /// 小端（-Z）から外端（+Z）へ並ぶ断面列を作る。
        ///
        /// 各断面は外端の輪郭を相似比で縮め、まがりばならさらに回したもの。
        /// Z は頂点からの距離を中心そろえした値。
        /// </summary>
        /// <param name="g">諸元</param>
        /// <param name="outerOutline">BuildOuterOutline が返した外端の輪郭。</param>
        /// <param name="faceSegments">歯幅方向の分割数。</param>
        /// <param name="boreRing">中心の丸穴。null で穴なし。全断面で共有する。</param>
        public static List<GearLoftSection> BuildSections(
            BevelData g,
            IReadOnlyList<Vector2> outerOutline,
            int faceSegments,
            Vector2[] boreRing)
        {
            int n = outerOutline?.Count ?? 0;
            if (n < 3) return null;

            int ns = Mathf.Max(1, faceSegments) + 1;

            // 頂点からの Z を中心そろえする。
            float zCenter = 0.5f * (g.outerZFromApex + g.innerZFromApex);
            float zOuter = g.outerZFromApex - zCenter;
            float zInner = g.innerZFromApex - zCenter;

            var sections = new List<GearLoftSection>(ns);

            for (int s = 0; s < ns; s++)
            {
                float u = ns > 1 ? s / (float)(ns - 1) : 1f;

                float scale = Mathf.Lerp(g.innerScale, 1f, u);
                float z = Mathf.Lerp(zInner, zOuter, u);

                float spiral = SpiralRotationAtConeDistance(g, g.coneDistance * scale);

                float c = Mathf.Cos(spiral);
                float si = Mathf.Sin(spiral);

                var loop = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = outerOutline[i] * scale;
                    loop[i] = new Vector2(c * p.x - si * p.y, si * p.x + c * p.y);
                }

                sections.Add(new GearLoftSection(z, loop, boreRing));
            }

            return sections;
        }

        /// <summary>
        /// 軸穴の半径を、小端の断面へ食い込まない値まで抑える。
        /// 小端がいちばん細いので、そこを基準にする。
        /// </summary>
        public static float ClampBore(
            BevelData g, IReadOnlyList<Vector2> outerOutline, float boreRadius)
        {
            float bore = Mathf.Max(0f, boreRadius);
            if (bore <= 0f) return 0f;

            float smallEndRoot = g.actualRootRadiusOuter * g.innerScale;
            if (bore >= smallEndRoot) bore = smallEndRoot * 0.95f;

            if (outerOutline != null && outerOutline.Count >= 3)
            {
                float minEdge =
                    GearDiskBuilder.MinDistanceToOutline(outerOutline, outerOutline.Count)
                    * g.innerScale;

                if (bore >= minEdge) bore = minEdge * 0.99f;
            }

            return Mathf.Max(0f, bore);
        }

        // ================================================================
        // 派生諸元（UI 表示用）
        // ================================================================

        /// <summary>パネルに出す派生諸元。すぐばはねじれ関係が 0 になる。</summary>
        public struct BevelInfo
        {
            /// <summary>諸元が成立しているか。false のとき他のフィールドは無効。</summary>
            public bool Valid;

            /// <summary>自分のピッチ円錐角（度）</summary>
            public float PitchConeAngleDeg;
            /// <summary>相手のピッチ円錐角（度）</summary>
            public float MatePitchConeAngleDeg;

            public float ConeDistance;
            public float InnerConeDistance;
            public float MeanConeDistance;

            /// <summary>外端のピッチ円直径</summary>
            public float OuterPitchDiameter;
            /// <summary>外端の歯先円直径</summary>
            public float OuterTipDiameter;
            /// <summary>外端の歯元円直径</summary>
            public float OuterRootDiameter;

            /// <summary>相当平歯車の歯数 zv</summary>
            public float VirtualToothCount;
            /// <summary>ねじれも含めた相当歯数</summary>
            public float FormativeToothCount;

            /// <summary>正面圧力角（度）。すぐばでは入力の圧力角と同じ。</summary>
            public float TransversePressureAngleDeg;

            /// <summary>外端と小端での歯すじの回転量の差（度）。すぐばは 0。</summary>
            public float SpiralTwistDeg;

            /// <summary>切り下げが起きているか。</summary>
            public bool Undercut;
            /// <summary>インボリュート歯面がほとんど残らないほどの切り下げか。</summary>
            public bool SevereUndercut;
            /// <summary>相当歯数が切り下げ限界を下回っているか。</summary>
            public bool BelowMinToothCount;
            /// <summary>切り下げ限界歯数の目安。</summary>
            public float MinToothCountApprox;
            /// <summary>穴半径が小端の歯元半径以上か。</summary>
            public bool BoreTooLarge;
        }

        /// <summary>派生諸元を求める。</summary>
        public static BevelInfo GetInfo(BevelData g, float boreRadius)
        {
            var info = new BevelInfo { Valid = true };

            InvoluteTrochoidSection.JoinData join =
                InvoluteTrochoidSection.FindTrochoidInvoluteJoin(g.Section);

            info.PitchConeAngleDeg     = g.delta * Mathf.Rad2Deg;
            info.MatePitchConeAngleDeg = g.deltaMate * Mathf.Rad2Deg;

            info.ConeDistance      = g.coneDistance;
            info.InnerConeDistance = g.innerConeDistance;
            info.MeanConeDistance  = g.meanConeDistance;

            info.OuterPitchDiameter = 2f * g.rPitchOuter;
            info.OuterTipDiameter   = 2f * g.actualTipRadiusOuter;
            info.OuterRootDiameter  = 2f * g.actualRootRadiusOuter;

            info.VirtualToothCount   = g.virtualToothCount;
            info.FormativeToothCount = g.formativeToothCount;

            info.TransversePressureAngleDeg = g.alphaT * Mathf.Rad2Deg;

            info.SpiralTwistDeg =
                (SpiralRotationAtConeDistance(g, g.coneDistance)
               - SpiralRotationAtConeDistance(g, g.innerConeDistance)) * Mathf.Rad2Deg;

            info.Undercut       = join.undercut;
            info.SevereUndercut = join.severeUndercut;

            float smallEndRoot = g.actualRootRadiusOuter * g.innerScale;
            info.BoreTooLarge = boreRadius > 0f && boreRadius >= smallEndRoot;

            // 切り下げ限界は法線断面で決まる。比べる相手はねじれも含めた相当歯数。
            float zMin = InvoluteTrochoidSection.MinToothCountApprox(g.alphaN, g.addendumCoef);

            if (zMin > 0f)
            {
                info.MinToothCountApprox = zMin;
                info.BelowMinToothCount = g.formativeToothCount < zMin;
            }

            return info;
        }
    }
}
