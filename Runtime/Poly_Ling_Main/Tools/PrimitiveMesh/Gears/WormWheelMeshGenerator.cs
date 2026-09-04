// WormWheelMeshGenerator.cs
// ウォームホイールのメッシュ生成。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【中央断面】
//   ウォームの軸方向モジュール mx が、そのままホイールの正面モジュールになる。
//   中央（歯幅の真ん中）の断面は、圧力角 αx のふつうのインボリュート歯車と同じ。
//   歯形は Gears/InvoluteTrochoidSection がそのまま作る。
//
// 【のど（throat）】
//   ホイールはウォームの円筒を抱き込むので、歯幅の外側へ行くほど半径が増える。
//   中心距離を a、中央断面での半径を r0 とすると、軸方向位置 z での半径は
//
//       r(z) = a - sqrt((a - r0)² - z²)
//
//   z=0 で r=r0、|z| が増えると r が増える。これで歯すじが円弧に沿って外へ開く。
//   |z| < a - r0 でないと平方根の中が負になる。いちばん厳しいのは歯先なので、
//   歯幅の半分が「中心距離 - 歯先半径」より小さいことを条件にする。
//
// 【ねじれ】
//   直交軸の組では、ホイールのねじれ角 β2 はウォームの進み角 γ と等しい。
//   歯幅ぶんのねじれ量は hand · 歯幅 · tan(γ) / ピッチ円半径。
//
// 【断面の作り方】
//   中央断面の各点を、その Z ぶんだけ回してから半径方向に歪める。
//   歪みは半径について単調増加なので、輪郭の前後関係が入れ替わることはない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class WormWheelMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================

        [System.Serializable]
        public struct WormWheelParams : System.IEquatable<WormWheelParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>軸方向モジュールの下限・上限</summary>
            public const float ModuleMin = 0.01f;
            public const float ModuleMax = 1f;

            /// <summary>ウォームの条数の下限・上限</summary>
            public const int StartsMin = 1;
            public const int StartsMax = 8;

            /// <summary>直径係数の下限・上限</summary>
            public const float DiameterFactorMin = 3f;
            public const float DiameterFactorMax = 30f;

            /// <summary>ホイールの歯数の下限・上限</summary>
            public const int ToothCountMin = 3;
            public const int ToothCountMax = 200;

            /// <summary>法線圧力角の下限・上限（度）</summary>
            public const float PressureAngleMin = 10f;
            public const float PressureAngleMax = 35f;

            /// <summary>歯幅の下限・上限</summary>
            public const float FaceWidthMin = 0.01f;
            public const float FaceWidthMax = 3f;

            /// <summary>歯末のたけ係数・歯元のたけ係数の下限・上限</summary>
            public const float ToothDepthCoefMin = 0.1f;
            public const float ToothDepthCoefMax = 2f;

            /// <summary>転位係数の下限・上限</summary>
            public const float ProfileShiftMin = -1f;
            public const float ProfileShiftMax = 1f;

            /// <summary>バックラッシの下限・上限</summary>
            public const float BacklashMin = 0f;
            public const float BacklashMax = 0.2f;

            /// <summary>軸穴半径の下限・上限</summary>
            public const float BoreRadiusMin = 0f;
            public const float BoreRadiusMax = 5f;

            /// <summary>トロコイド曲線・インボリュート曲線の標本数の下限・上限</summary>
            public const int CurveSamplesMin = 3;
            public const int CurveSamplesMax = 64;

            /// <summary>歯先円弧・歯底円弧の標本数の下限・上限</summary>
            public const int ArcSamplesMin = 1;
            public const int ArcSamplesMax = 16;

            /// <summary>歯幅方向の分割数の下限・上限</summary>
            public const int FaceSegmentsMin = 2;
            public const int FaceSegmentsMax = 64;

            /// <summary>回転オフセットの下限・上限（度）</summary>
            public const float RotationOffsetMin = 0f;
            public const float RotationOffsetMax = 360f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            // ── かみ合う相手（ウォーム） ──
            /// <summary>軸方向モジュール mx</summary>
            [PLParam(TextKey = "WormAxialModule", Description = "ウォームの軸方向モジュール。ホイールの正面モジュールになる",
                     Min = ModuleMin, Max = ModuleMax)]
            public float AxialModule;
            /// <summary>ウォームの条数 z1</summary>
            [PLParam(TextKey = "WormStarts", Description = "ウォームの条数", Min = StartsMin, Max = StartsMax,
                     Step = 1)]
            public int WormStarts;
            /// <summary>ウォームの直径係数 q</summary>
            [PLParam(TextKey = "WormDiameterFactor", Description = "ウォームの直径係数 q。中心距離が決まる",
                     Min = DiameterFactorMin, Max = DiameterFactorMax)]
            public float WormDiameterFactorQ;
            /// <summary>ウォームが右ねじなら true</summary>
            [PLParam(TextKey = "WormRightHand", Description = "ウォームを右ねじとして扱う。外すと左ねじ")]
            public bool RightHandWorm;

            // ── ホイール ──
            /// <summary>ホイールの歯数 z2</summary>
            [PLParam(TextKey = "InvToothCount", Description = "ホイールの歯数", Min = ToothCountMin,
                     Max = ToothCountMax, Step = 1)]
            public int ToothCount;
            /// <summary>法線圧力角 αn（度）</summary>
            [PLParam(TextKey = "HelNormalPressureAngle", Description = "法線圧力角（度）", Min = PressureAngleMin,
                     Max = PressureAngleMax)]
            public float NormalPressureAngleDeg;
            /// <summary>歯幅</summary>
            [PLParam(TextKey = "WhlFaceWidth", Description = "歯幅。半分が「中心距離 - 歯先半径」より小さいこと",
                     Min = FaceWidthMin, Max = FaceWidthMax)]
            public float FaceWidth;

            // ── 歯たけ ──
            /// <summary>歯末のたけ係数 ha*</summary>
            [PLParam(TextKey = "GearAddendumCoef", Description = "歯末のたけ係数。ピッチ円から歯先までの高さ ÷ モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float AddendumCoef;
            /// <summary>歯元のたけ係数 hf*</summary>
            [PLParam(TextKey = "GearDedendumCoef", Description = "歯元のたけ係数。ピッチ円から歯底までの深さ ÷ モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float DedendumCoef;

            // ── 転位・バックラッシ ──
            /// <summary>転位係数 x</summary>
            [PLParam(TextKey = "InvProfileShift", Description = "転位係数", Min = ProfileShiftMin,
                     Max = ProfileShiftMax)]
            public float ProfileShift;
            /// <summary>ピッチ円上のバックラッシ</summary>
            [PLParam(TextKey = "InvBacklash", Description = "バックラッシ", Min = BacklashMin, Max = BacklashMax)]
            public float Backlash;

            // ── 穴 ──
            /// <summary>中心の丸穴半径。0 で穴なし。</summary>
            [PLParam(TextKey = "GearBoreRadius", Description = "軸穴の半径。0 で穴なし", Min = BoreRadiusMin,
                     Max = BoreRadiusMax)]
            public float BoreRadius;
            /// <summary>穴リングの分割数</summary>
            [PLParam(TextKey = "GearBoreSegments", Description = "軸穴の円周分割数",
                     Min = GearDiskBuilder.BoreSegmentsMin, Max = GearDiskBuilder.BoreSegmentsMax, Step = 1)]
            public int BoreSegments;

            // ── 曲線のサンプル数 ──
            /// <summary>歯面 1 本あたりのトロコイド分割数</summary>
            [PLParam(TextKey = "InvTrochoidSamples", Description = "歯元トロコイド曲線の標本数", Min = CurveSamplesMin,
                     Max = CurveSamplesMax, Step = 1)]
            public int TrochoidSamples;
            /// <summary>歯面 1 本あたりのインボリュート分割数</summary>
            [PLParam(TextKey = "InvInvoluteSamples", Description = "インボリュート曲線の標本数", Min = CurveSamplesMin,
                     Max = CurveSamplesMax, Step = 1)]
            public int InvoluteSamples;
            /// <summary>歯先円弧の分割数</summary>
            [PLParam(TextKey = "InvTipArcSamples", Description = "歯先円弧の標本数", Min = ArcSamplesMin,
                     Max = ArcSamplesMax, Step = 1)]
            public int TipArcSamples;
            /// <summary>歯元円弧の分割数</summary>
            [PLParam(TextKey = "InvRootArcSamples", Description = "歯底円弧の標本数", Min = ArcSamplesMin,
                     Max = ArcSamplesMax, Step = 1)]
            public int RootArcSamples;
            /// <summary>歯幅方向の分割数。のどの曲がりをなめらかに見せるために使う。</summary>
            [PLParam(TextKey = "WhlFaceSegments", Description = "歯幅方向の分割数", Min = FaceSegmentsMin,
                     Max = FaceSegmentsMax, Step = 1)]
            public int FaceSegments;

            // ── 配置 ──
            /// <summary>全体の回転オフセット（度）</summary>
            [PLParam(TextKey = "GearRotationOffset", Description = "全体の回転オフセット（度）", Min = RotationOffsetMin,
                     Max = RotationOffsetMax)]
            public float RotationOffsetDeg;

            /// <summary>軸を置く平面</summary>
            [PLParam(TextKey = "Orientation", Description = "板の向き（XY / XZ / YZ）")]
            public PlaneOrientation Orientation;
            /// <summary>生成後にメッシュ全体の面を反転する</summary>
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            /// <summary>AABB サイズ基準のピボット</summary>
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static WormWheelParams Default => new WormWheelParams
            {
                MeshName               = "WormWheel",
                AxialModule            = 0.1f,
                WormStarts             = 1,
                WormDiameterFactorQ    = 11f,
                RightHandWorm          = true,
                ToothCount             = 40,
                NormalPressureAngleDeg = 20f,
                FaceWidth              = 0.3f,
                AddendumCoef           = 1f,
                DedendumCoef           = 1.25f,
                ProfileShift           = 0f,
                Backlash               = 0f,
                BoreRadius             = 0.3f,
                BoreSegments           = 24,
                TrochoidSamples        = 12,
                InvoluteSamples        = 16,
                TipArcSamples          = 3,
                RootArcSamples         = 4,
                FaceSegments           = 16,
                RotationOffsetDeg      = 0f,
                Orientation            = PlaneOrientation.XY,
                FlipFaces              = false,
                Pivot                  = Vector3.zero,
            };

            public bool Equals(WormWheelParams o) =>
                MeshName == o.MeshName &&
                Mathf.Approximately(AxialModule,         o.AxialModule)         &&
                WormStarts == o.WormStarts &&
                Mathf.Approximately(WormDiameterFactorQ, o.WormDiameterFactorQ) &&
                RightHandWorm == o.RightHandWorm &&
                ToothCount == o.ToothCount &&
                Mathf.Approximately(NormalPressureAngleDeg, o.NormalPressureAngleDeg) &&
                Mathf.Approximately(FaceWidth,              o.FaceWidth)              &&
                Mathf.Approximately(AddendumCoef,           o.AddendumCoef)           &&
                Mathf.Approximately(DedendumCoef,           o.DedendumCoef)           &&
                Mathf.Approximately(ProfileShift,           o.ProfileShift)           &&
                Mathf.Approximately(Backlash,               o.Backlash)               &&
                Mathf.Approximately(BoreRadius,             o.BoreRadius)             &&
                BoreSegments    == o.BoreSegments    &&
                TrochoidSamples == o.TrochoidSamples &&
                InvoluteSamples == o.InvoluteSamples &&
                TipArcSamples   == o.TipArcSamples   &&
                RootArcSamples  == o.RootArcSamples  &&
                FaceSegments    == o.FaceSegments    &&
                Mathf.Approximately(RotationOffsetDeg, o.RotationOffsetDeg) &&
                Orientation == o.Orientation &&
                FlipFaces   == o.FlipFaces   &&
                Pivot       == o.Pivot;

            public override bool Equals(object obj) => obj is WormWheelParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 内部データ
        // ================================================================

        private struct WheelData
        {
            public WormPairSection.PairData pair;

            public float centerDistance;
            /// <summary>中心距離 - 歯先半径。のどの平方根が成り立つ限界。</summary>
            public float throatSurfaceRadius;

            /// <summary>歯幅ぶんのねじれ量（ラジアン）</summary>
            public float totalTwist;

            public InvoluteTrochoidSection.GearData Section;
        }

        private static bool TryGetWheelData(WormWheelParams p, out WheelData g)
        {
            g = default;

            if (p.ToothCount < 3 ||
                p.FaceWidth <= 0f ||
                p.AddendumCoef <= 0f ||
                p.DedendumCoef <= 0f)
            {
                return false;
            }

            var pairInput = new WormPairSection.PairInput
            {
                AxialModule         = p.AxialModule,
                Starts              = p.WormStarts,
                DiameterFactorQ     = p.WormDiameterFactorQ,
                NormalPressureAngle = p.NormalPressureAngleDeg * Mathf.Deg2Rad,
                Hand                = p.RightHandWorm ? 1f : -1f,
            };

            if (!WormPairSection.TryGetPairData(pairInput, out var pair)) return false;

            // ホイールの中央断面はふつうのインボリュート歯車。圧力角は軸直角のもの。
            var sectionInput = new InvoluteTrochoidSection.SectionInput
            {
                ToothCount              = p.ToothCount,
                VirtualToothCount       = 0f,
                TransverseModule        = pair.mx,
                RadialModule            = pair.mx,
                TransversePressureAngle = pair.alphaX,
                NormalPressureAngle     = pair.alphaX,
                ProfileShift            = p.ProfileShift,
                Backlash                = p.Backlash,
                AddendumCoef            = p.AddendumCoef,
                DedendumCoef            = p.DedendumCoef,
            };

            if (!InvoluteTrochoidSection.TryGetGearData(sectionInput, out var section))
                return false;

            if (section.rAddendum <= section.rBase) return false;

            float centerDistance = pair.wormPitchRadius + section.rPitch;
            float throatSurfaceRadius = centerDistance - section.rAddendum;

            // 歯先がウォームの軸に届く／越えると、のどが作れない。
            if (throatSurfaceRadius <= 0f) return false;

            // 歯幅の半分がのどの半径以上だと、平方根の中が負になる。
            if (0.5f * p.FaceWidth >= throatSurfaceRadius) return false;

            g = new WheelData
            {
                pair = pair,
                centerDistance = centerDistance,
                throatSurfaceRadius = throatSurfaceRadius,

                // 直交軸の組ではホイールのねじれ角 β2 はウォームの進み角 γ と等しい。
                totalTwist = pair.hand * p.FaceWidth * Mathf.Tan(pair.gamma) / section.rPitch,

                Section = section,
            };

            return true;
        }

        private static InvoluteTrochoidSection.Samples ToSamples(WormWheelParams p)
            => new InvoluteTrochoidSection.Samples
            {
                Trochoid = p.TrochoidSamples,
                Involute = p.InvoluteSamples,
                TipArc   = p.TipArcSamples,
                RootArc  = p.RootArcSamples,
            };

        // ================================================================
        // のどの歪み
        // ================================================================

        /// <summary>
        /// 中央断面での半径 r0 が、軸方向位置 z でいくつになるか。
        ///
        ///   r(z) = a - sqrt((a - r0)² - z²)
        ///
        /// 平方根の中が負になる範囲では中心距離そのものを返す（呼ぶ前に歯幅で弾いてある）。
        /// r0 について単調増加なので、輪郭の前後関係は入れ替わらない。
        /// </summary>
        private static float ThroatRadius(WheelData g, float r0, float z)
        {
            float rho = g.centerDistance - r0;
            float inside = rho * rho - z * z;

            if (inside <= 0f) return g.centerDistance;

            return g.centerDistance - Mathf.Sqrt(inside);
        }

        // ================================================================
        // 派生諸元（UI 表示用）
        // ================================================================

        /// <summary>パネルに出す派生諸元。</summary>
        public struct WormWheelInfo
        {
            /// <summary>諸元が成立しているか。false のとき他のフィールドは無効。</summary>
            public bool Valid;

            public float PitchDiameter;
            public float BaseDiameter;
            /// <summary>中央断面での歯先円直径</summary>
            public float TipDiameter;
            /// <summary>中央断面での歯元円直径</summary>
            public float RootDiameter;

            public float WormPitchDiameter;
            public float CenterDistance;
            public float ThroatSurfaceRadius;
            /// <summary>歯幅の端での歯先半径。のどで外へ開いたぶん。</summary>
            public float RimRadiusAtFaceEdge;

            /// <summary>減速比 z2 / z1</summary>
            public float GearRatio;
            /// <summary>進み角（度）</summary>
            public float LeadAngleDeg;
            /// <summary>軸直角の圧力角（度）</summary>
            public float AxialPressureAngleDeg;
            /// <summary>歯幅ぶんのねじれ量（度）</summary>
            public float TotalTwistDeg;

            /// <summary>切り下げが起きているか。</summary>
            public bool Undercut;
            /// <summary>インボリュート歯面がほとんど残らないほどの切り下げか。</summary>
            public bool SevereUndercut;
            /// <summary>穴半径が歯元半径以上か。</summary>
            public bool BoreTooLarge;
        }

        /// <summary>派生諸元を求める。パラメータが不正なら Valid=false を返す。</summary>
        public static WormWheelInfo GetInfo(WormWheelParams p)
        {
            var info = new WormWheelInfo { Valid = false };

            if (!TryGetWheelData(p, out WheelData g)) return info;

            InvoluteTrochoidSection.JoinData join =
                InvoluteTrochoidSection.FindTrochoidInvoluteJoin(g.Section);

            info.Valid = true;

            info.PitchDiameter = 2f * g.Section.rPitch;
            info.BaseDiameter  = 2f * g.Section.rBase;
            info.TipDiameter   = 2f * g.Section.rAddendum;
            info.RootDiameter  = 2f * g.Section.rRoot;

            info.WormPitchDiameter   = 2f * g.pair.wormPitchRadius;
            info.CenterDistance      = g.centerDistance;
            info.ThroatSurfaceRadius = g.throatSurfaceRadius;
            info.RimRadiusAtFaceEdge =
                ThroatRadius(g, g.Section.rAddendum, 0.5f * Mathf.Max(0f, p.FaceWidth));

            info.GearRatio             = WormPairSection.GearRatio(g.pair, p.ToothCount);
            info.LeadAngleDeg          = g.pair.gamma * Mathf.Rad2Deg;
            info.AxialPressureAngleDeg = g.pair.alphaX * Mathf.Rad2Deg;
            info.TotalTwistDeg         = g.totalTwist * Mathf.Rad2Deg;

            info.Undercut       = join.undercut;
            info.SevereUndercut = join.severeUndercut;
            info.BoreTooLarge   = p.BoreRadius > 0f && p.BoreRadius >= g.Section.rRoot;

            return info;
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// ウォームホイールメッシュを作る。パラメータが不正なときは空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(WormWheelParams p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "WormWheel" : p.MeshName;

            if (!TryGetWheelData(p, out WheelData g)) return new MeshObject(name);

            List<Vector2> center = InvoluteTrochoidSection.GenerateOutline(
                g.Section, ToSamples(p), p.RotationOffsetDeg * Mathf.Deg2Rad);

            if (center.Count < 3) return new MeshObject(name);

            // 穴は歯元円より小さくする。のどは半径を広げる向きにしか働かないので、
            // いちばん細い中央断面で測ればよい。
            float bore = Mathf.Max(0f, p.BoreRadius);
            if (bore > 0f && bore >= g.Section.rRoot) bore = g.Section.rRoot * 0.95f;
            bore = GearLoftBuilder.ClampBoreRadius(center, bore);

            Vector2[] boreRing = GearLoftBuilder.MakeBoreRing(bore, p.BoreSegments);

            // ── 断面列 ──
            float width = Mathf.Max(0f, p.FaceWidth);

            int ns = Mathf.Clamp(p.FaceSegments,
                WormWheelParams.FaceSegmentsMin, WormWheelParams.FaceSegmentsMax) + 1;

            int n = center.Count;

            // 中央断面の各点の半径は Z によらないので先に求めておく。
            var radius0 = new float[n];
            for (int i = 0; i < n; i++) radius0[i] = center[i].magnitude;

            var sections = new List<GearLoftSection>(ns);

            float zMin = -0.5f * width;
            float zMax = +0.5f * width;

            for (int s = 0; s < ns; s++)
            {
                float u = s / (float)(ns - 1);

                float z = Mathf.Lerp(zMin, zMax, u);
                float twist = Mathf.Lerp(-0.5f * g.totalTwist, +0.5f * g.totalTwist, u);

                float c = Mathf.Cos(twist);
                float si = Mathf.Sin(twist);

                var loop = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    Vector2 q = center[i];

                    // 回してから半径方向へ歪める。回転は半径を変えないので順序は問わない。
                    Vector2 rotated = new Vector2(c * q.x - si * q.y, si * q.x + c * q.y);

                    float r0 = radius0[i];
                    if (r0 <= 1e-8f) { loop[i] = rotated; continue; }

                    float r = ThroatRadius(g, r0, z);
                    loop[i] = rotated * (r / r0);
                }

                sections.Add(new GearLoftSection(z, loop, boreRing));
            }

            return GearLoftBuilder.Build(
                name,
                sections,
                GearLoftCapMode.Triangulate,
                p.Orientation,
                p.FlipFaces,
                p.Pivot);
        }
    }
}
