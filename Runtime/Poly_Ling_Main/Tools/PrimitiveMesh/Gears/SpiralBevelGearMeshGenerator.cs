// SpiralBevelGearMeshGenerator.cs
// まがりばかさ歯車のメッシュ生成。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【すぐばとの違い】
//   歯すじが円錐面上で対数らせんを描く。円錐距離 ρ のところでの回転量は
//
//       θ(ρ) = hand · tan(ψ) / sin(δ) · ln(ρ / Rm)
//
//   Rm は平均円錐距離で、そこで回転量 0、ねじれ角がちょうど ψ になる。
//   外端では +、小端では - に振れるので、歯すじが歯幅の中でねじれて見える。
//
// 【圧力角】
//   入力は法線圧力角 αn。平均ねじれ角のところで正面圧力角へ直してから
//   相当平歯車へ渡す。
//
//       tan(αt) = tan(αn) / cos(ψ)
//
// 【ねじれの向き】
//   ねじれ角が正で右ねじれ。かみ合う相手は逆手になる。
//
// 【断面】
//   ねじれで断面が回るので、歯幅方向の分割はすぐばより多めに要る。既定は 16。
//
// 【共有部品】
//   円錐まわりと断面列は Gears/BevelGearSection、歯形は Gears/InvoluteTrochoidSection。
//   すぐばかさ歯車（StraightBevelGearMeshGenerator）とは、ねじれ角 0 かどうかだけが違う。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class SpiralBevelGearMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================

        [System.Serializable]
        public struct SpiralBevelGearParams : System.IEquatable<SpiralBevelGearParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>歯数の下限・上限</summary>
            public const int ToothCountMin = 3;
            public const int ToothCountMax = 200;

            /// <summary>軸角の下限・上限（度）</summary>
            public const float ShaftAngleMin = 10f;
            public const float ShaftAngleMax = 170f;

            /// <summary>
            /// 平均ねじれ角の下限・上限（度）。正で右ねじれ。
            /// 60° 以上は正面圧力角が立ちすぎて歯形が成立しないので手前で止める。
            /// </summary>
            public const float SpiralAngleMin = -59f;
            public const float SpiralAngleMax =  59f;

            /// <summary>モジュールの下限・上限</summary>
            public const float ModuleMin = 0.01f;
            public const float ModuleMax = 1f;

            /// <summary>法線圧力角の下限・上限（度）</summary>
            public const float PressureAngleMin = 10f;
            public const float PressureAngleMax = 35f;

            /// <summary>歯幅の下限・上限</summary>
            public const float FaceWidthMin = 0.01f;
            public const float FaceWidthMax = 3f;

            /// <summary>歯末のたけ係数・歯元のたけ係数の下限・上限</summary>
            public const float ToothDepthCoefMin = 0.1f;
            public const float ToothDepthCoefMax = 2f;

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
            public const int FaceSegmentsMin = 1;
            public const int FaceSegmentsMax = 64;

            /// <summary>回転オフセットの下限・上限（度）</summary>
            public const float RotationOffsetMin = 0f;
            public const float RotationOffsetMax = 360f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            // ── かみ合う組 ──
            /// <summary>歯数 z</summary>
            [PLParam(TextKey = "InvToothCount", Description = "歯数", Min = ToothCountMin, Max = ToothCountMax,
                     Step = 1)]
            public int ToothCount;
            /// <summary>相手の歯数 z2。ピッチ円錐角を決めるのに使う。</summary>
            [PLParam(TextKey = "BevMatingToothCount", Description = "かみ合う相手の歯数。ピッチ円錐角が決まる",
                     Min = ToothCountMin, Max = ToothCountMax, Step = 1)]
            public int MatingToothCount;
            /// <summary>軸角 Σ（度）</summary>
            [PLParam(TextKey = "BevShaftAngle", Description = "軸角（度）。90 で直交", Min = ShaftAngleMin,
                     Max = ShaftAngleMax)]
            public float ShaftAngleDeg;

            // ── 歯すじ ──
            /// <summary>平均ねじれ角 ψ（度）。正で右ねじれ。</summary>
            [PLParam(TextKey = "BevSpiralAngle", Description = "平均ねじれ角（度）。正で右ねじれ",
                     Min = SpiralAngleMin, Max = SpiralAngleMax)]
            public float SpiralAngleDeg;

            // ── 基本諸元 ──
            /// <summary>外端モジュール m</summary>
            [PLParam(TextKey = "BevModule", Description = "外端モジュール（大端での歯の大きさ）", Min = ModuleMin,
                     Max = ModuleMax)]
            public float Module;
            /// <summary>法線圧力角 αn（度）</summary>
            [PLParam(TextKey = "HelNormalPressureAngle", Description = "法線圧力角（度）", Min = PressureAngleMin,
                     Max = PressureAngleMax)]
            public float NormalPressureAngleDeg;
            /// <summary>歯幅 b</summary>
            [PLParam(TextKey = "BevFaceWidth", Description = "歯幅。円錐距離より小さくすること", Min = FaceWidthMin,
                     Max = FaceWidthMax)]
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
            /// <summary>歯幅方向の分割数。ねじれをなめらかに見せるために使う。</summary>
            [PLParam(TextKey = "BevFaceSegments", Description = "歯幅方向の分割数", Min = FaceSegmentsMin,
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

            public static SpiralBevelGearParams Default => new SpiralBevelGearParams
            {
                MeshName               = "SpiralBevelGear",
                ToothCount             = 20,
                MatingToothCount       = 40,
                ShaftAngleDeg          = 90f,
                SpiralAngleDeg         = 35f,
                Module                 = 0.1f,
                NormalPressureAngleDeg = 20f,
                FaceWidth              = 0.3f,
                AddendumCoef           = 1f,
                DedendumCoef           = 1.25f,
                Backlash               = 0f,
                BoreRadius             = 0.2f,
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

            public bool Equals(SpiralBevelGearParams o) =>
                MeshName == o.MeshName &&
                ToothCount == o.ToothCount &&
                MatingToothCount == o.MatingToothCount &&
                Mathf.Approximately(ShaftAngleDeg,          o.ShaftAngleDeg)          &&
                Mathf.Approximately(SpiralAngleDeg,         o.SpiralAngleDeg)         &&
                Mathf.Approximately(Module,                 o.Module)                 &&
                Mathf.Approximately(NormalPressureAngleDeg, o.NormalPressureAngleDeg) &&
                Mathf.Approximately(FaceWidth,              o.FaceWidth)              &&
                Mathf.Approximately(AddendumCoef,           o.AddendumCoef)           &&
                Mathf.Approximately(DedendumCoef,           o.DedendumCoef)           &&
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

            public override bool Equals(object obj) => obj is SpiralBevelGearParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 共有部品への受け渡し
        // ================================================================

        private static bool TryGetBevel(
            SpiralBevelGearParams p, out BevelGearSection.BevelData g)
        {
            var input = new BevelGearSection.BevelInput
            {
                ToothCount          = p.ToothCount,
                MatingToothCount    = p.MatingToothCount,
                ShaftAngle          = p.ShaftAngleDeg * Mathf.Deg2Rad,
                Module              = p.Module,
                NormalPressureAngle = p.NormalPressureAngleDeg * Mathf.Deg2Rad,

                SpiralAngle         = Mathf.Abs(p.SpiralAngleDeg) * Mathf.Deg2Rad,
                SpiralHand          = p.SpiralAngleDeg < 0f ? -1f : 1f,

                FaceWidth           = p.FaceWidth,
                Backlash            = p.Backlash,
                AddendumCoef        = p.AddendumCoef,
                DedendumCoef        = p.DedendumCoef,
            };

            return BevelGearSection.TryGetBevelData(input, out g);
        }

        private static InvoluteTrochoidSection.Samples ToSamples(SpiralBevelGearParams p)
            => new InvoluteTrochoidSection.Samples
            {
                Trochoid = p.TrochoidSamples,
                Involute = p.InvoluteSamples,
                TipArc   = p.TipArcSamples,
                RootArc  = p.RootArcSamples,
            };

        // ================================================================
        // 派生諸元（UI 表示用）
        // ================================================================

        /// <summary>派生諸元を求める。パラメータが不正なら Valid=false を返す。</summary>
        public static BevelGearSection.BevelInfo GetInfo(SpiralBevelGearParams p)
        {
            if (!TryGetBevel(p, out BevelGearSection.BevelData g))
                return new BevelGearSection.BevelInfo { Valid = false };

            return BevelGearSection.GetInfo(g, p.BoreRadius);
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// まがりばかさ歯車メッシュを作る。パラメータが不正なときは空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(SpiralBevelGearParams p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "SpiralBevelGear" : p.MeshName;

            if (!TryGetBevel(p, out BevelGearSection.BevelData g))
                return new MeshObject(name);

            List<Vector2> outer = BevelGearSection.BuildOuterOutline(
                g, ToSamples(p), p.RotationOffsetDeg * Mathf.Deg2Rad);

            if (outer.Count < 3) return new MeshObject(name);

            float bore = BevelGearSection.ClampBore(g, outer, p.BoreRadius);
            Vector2[] boreRing = GearLoftBuilder.MakeBoreRing(bore, p.BoreSegments);

            List<GearLoftSection> sections =
                BevelGearSection.BuildSections(g, outer, p.FaceSegments, boreRing);

            if (sections == null) return new MeshObject(name);

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
