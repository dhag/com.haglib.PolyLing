// HelicalGearMeshGenerator.cs
// はすば歯車（ヘリカルギア）のメッシュ生成。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【入力は法線系】
//   mn  … 法線モジュール
//   αn  … 法線圧力角
//   β   … ねじれ角。正で右ねじれ、負で左ねじれ。
//
// 【正面系への変換】
//   mt      = mn / cos(β)
//   tan(αt) = tan(αn) / cos(β)
//   rp      = z · mt / 2
//
// 【正面断面】
//   歯面はインボリュート、歯元は創成ラックカッタの尖った角が描くトロコイド。
//   少歯数では切り下げ（アンダーカット）が自動で表れる。
//   断面そのものは Gears/InvoluteTrochoidSection が作る（平歯車と同じ部品）。
//
//   半径方向の歯たけは法線モジュール基準（mn·ha* / mn·hf*）、
//   円周方向の寸法は正面モジュール基準（mt）。この使い分けは共有断面へ渡す
//   TransverseModule / RadialModule の 2 本で表している。
//
// 【3D】
//   完成した正面断面をねじりながら押し出す。
//
//       θ(z) = hand · z · tan(β) / rp
//
//   軸穴はねじらない素の円筒。
//
// 【歯元の丸み】
//   ラックカッタの角は尖っているものとして扱う。実際のホブは先端に丸みがあり、
//   その包絡線（二次トロコイド）が本来の歯元曲線になる。そこまでは追わない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class HelicalGearMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================

        [System.Serializable]
        public struct HelicalGearParams : System.IEquatable<HelicalGearParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>歯数の下限・上限</summary>
            public const int ToothCountMin = 3;
            public const int ToothCountMax = 200;

            /// <summary>法線モジュールの下限・上限</summary>
            public const float ModuleMin = 0.01f;
            public const float ModuleMax = 1f;

            /// <summary>法線圧力角の下限・上限（度）</summary>
            public const float PressureAngleMin = 10f;
            public const float PressureAngleMax = 35f;

            /// <summary>
            /// ねじれ角の下限・上限（度）。正で右ねじれ、負で左ねじれ。
            /// 60° 以上は正面圧力角が立ちすぎて歯形が成立しないので手前で止める。
            /// </summary>
            public const float HelixAngleMin = -59f;
            public const float HelixAngleMax =  59f;

            /// <summary>歯幅の下限・上限</summary>
            public const float ThicknessMin = 0f;
            public const float ThicknessMax = 3f;

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
            public const int AxialSegmentsMin = 1;
            public const int AxialSegmentsMax = 64;

            /// <summary>回転オフセットの下限・上限（度）</summary>
            public const float RotationOffsetMin = 0f;
            public const float RotationOffsetMax = 360f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            // ── 基本諸元（法線系） ──
            /// <summary>歯数 z</summary>
            [PLParam(TextKey = "InvToothCount", Description = "歯数", Min = ToothCountMin, Max = ToothCountMax,
                     Step = 1)]
            public int ToothCount;
            /// <summary>法線モジュール mn</summary>
            [PLParam(TextKey = "HelNormalModule", Description = "法線モジュール（歯直角で見た歯の大きさ）",
                     Min = ModuleMin, Max = ModuleMax)]
            public float NormalModule;
            /// <summary>法線圧力角 αn（度）</summary>
            [PLParam(TextKey = "HelNormalPressureAngle", Description = "法線圧力角（度）", Min = PressureAngleMin,
                     Max = PressureAngleMax)]
            public float NormalPressureAngleDeg;
            /// <summary>ねじれ角 β（度）。正で右ねじれ、負で左ねじれ。</summary>
            [PLParam(TextKey = "HelHelixAngle", Description = "ねじれ角（度）。正で右ねじれ、負で左ねじれ",
                     Min = HelixAngleMin, Max = HelixAngleMax)]
            public float HelixAngleDeg;
            /// <summary>歯幅</summary>
            [PLParam(TextKey = "HelFaceWidth", Description = "歯幅。0 で板", Min = ThicknessMin, Max = ThicknessMax)]
            public float Thickness;

            // ── 歯たけ ──
            /// <summary>歯末のたけ係数 ha*</summary>
            [PLParam(TextKey = "GearAddendumCoef", Description = "歯末のたけ係数。ピッチ円から歯先までの高さ ÷ 法線モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float AddendumCoef;
            /// <summary>歯元のたけ係数 hf*</summary>
            [PLParam(TextKey = "GearDedendumCoef", Description = "歯元のたけ係数。ピッチ円から歯底までの深さ ÷ 法線モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float DedendumCoef;

            // ── 転位・バックラッシ ──
            /// <summary>転位係数 x</summary>
            [PLParam(TextKey = "InvProfileShift", Description = "転位係数", Min = ProfileShiftMin,
                     Max = ProfileShiftMax)]
            public float ProfileShift;
            /// <summary>正面ピッチ円上のバックラッシ</summary>
            [PLParam(TextKey = "HelTransverseBacklash", Description = "正面バックラッシ", Min = BacklashMin,
                     Max = BacklashMax)]
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
            [PLParam(TextKey = "HelAxialSegments", Description = "歯幅方向の分割数", Min = AxialSegmentsMin,
                     Max = AxialSegmentsMax, Step = 1)]
            public int AxialSegments;

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

            public static HelicalGearParams Default => new HelicalGearParams
            {
                MeshName               = "HelicalGear",
                ToothCount             = 24,
                NormalModule           = 0.1f,
                NormalPressureAngleDeg = 20f,
                HelixAngleDeg          = 20f,
                Thickness              = 0.4f,
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
                AxialSegments          = 16,
                RotationOffsetDeg      = 0f,
                Orientation            = PlaneOrientation.XY,
                FlipFaces              = false,
                Pivot                  = Vector3.zero,
            };

            public bool Equals(HelicalGearParams o) =>
                MeshName == o.MeshName &&
                ToothCount == o.ToothCount &&
                Mathf.Approximately(NormalModule,           o.NormalModule)           &&
                Mathf.Approximately(NormalPressureAngleDeg, o.NormalPressureAngleDeg) &&
                Mathf.Approximately(HelixAngleDeg,          o.HelixAngleDeg)          &&
                Mathf.Approximately(Thickness,              o.Thickness)              &&
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
                AxialSegments   == o.AxialSegments   &&
                Mathf.Approximately(RotationOffsetDeg, o.RotationOffsetDeg) &&
                Orientation == o.Orientation &&
                FlipFaces   == o.FlipFaces   &&
                Pivot       == o.Pivot;

            public override bool Equals(object obj) => obj is HelicalGearParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 正面系への変換
        // ================================================================

        /// <summary>法線系 → 正面系の換算結果。</summary>
        public struct Transverse
        {
            /// <summary>ねじれ角の大きさ（ラジアン）</summary>
            public float Beta;
            /// <summary>右ねじれで +1、左ねじれで -1</summary>
            public float Hand;
            /// <summary>法線圧力角（ラジアン）</summary>
            public float AlphaN;
            /// <summary>正面圧力角（ラジアン）</summary>
            public float AlphaT;
            /// <summary>正面モジュール</summary>
            public float ModuleT;
        }

        private static bool TryGetTransverse(HelicalGearParams p, out Transverse t)
        {
            t = default;

            if (Mathf.Abs(p.HelixAngleDeg) >= 60f) return false;

            float beta = Mathf.Abs(p.HelixAngleDeg) * Mathf.Deg2Rad;
            float cosBeta = Mathf.Cos(beta);

            if (cosBeta <= 1e-6f) return false;

            float alphaN = p.NormalPressureAngleDeg * Mathf.Deg2Rad;

            t = new Transverse
            {
                Beta    = beta,
                Hand    = p.HelixAngleDeg < 0f ? -1f : 1f,
                AlphaN  = alphaN,
                AlphaT  = Mathf.Atan(Mathf.Tan(alphaN) / cosBeta),
                ModuleT = p.NormalModule / cosBeta,
            };

            return true;
        }

        /// <summary>
        /// 諸元を求める。厚みとねじれ角は断面の数学に関わらないので、ここだけで見る。
        /// </summary>
        private static bool TryGetSection(
            HelicalGearParams p,
            out InvoluteTrochoidSection.GearData g,
            out Transverse t)
        {
            g = default;

            if (!TryGetTransverse(p, out t)) return false;
            if (p.Thickness < 0f) return false;

            var input = new InvoluteTrochoidSection.SectionInput
            {
                ToothCount              = p.ToothCount,
                TransverseModule        = t.ModuleT,
                RadialModule            = p.NormalModule,
                TransversePressureAngle = t.AlphaT,
                NormalPressureAngle     = t.AlphaN,
                ProfileShift            = p.ProfileShift,
                Backlash                = p.Backlash,
                AddendumCoef            = p.AddendumCoef,
                DedendumCoef            = p.DedendumCoef,
            };

            return InvoluteTrochoidSection.TryGetGearData(input, out g);
        }

        private static InvoluteTrochoidSection.Samples ToSamples(HelicalGearParams p)
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

        /// <summary>パネルに出す派生諸元。</summary>
        public struct HelicalGearInfo
        {
            /// <summary>諸元が成立しているか。false のとき他のフィールドは無効。</summary>
            public bool Valid;

            public float TransverseModule;
            public float TransversePressureAngleDeg;

            public float PitchDiameter;
            public float BaseDiameter;
            public float TipDiameter;
            public float RootDiameter;
            public float TransverseCircularPitch;
            public float ToothThicknessPitch;
            public float JoinRadius;

            /// <summary>歯幅ぶんのねじれ量（度）。</summary>
            public float TotalTwistDeg;
            /// <summary>リード。ねじれ角 0 のときは無限大。</summary>
            public float Lead;
            /// <summary>相当平歯車の歯数 zv = z / cos³β。</summary>
            public float VirtualToothCount;

            /// <summary>切り下げが起きているか。</summary>
            public bool Undercut;
            /// <summary>インボリュート歯面がほとんど残らないほどの切り下げか。</summary>
            public bool SevereUndercut;
            /// <summary>転位なしで、相当平歯車の歯数が切り下げ限界を下回っているか。</summary>
            public bool BelowMinToothCount;
            /// <summary>切り下げ限界歯数の目安（法線断面）。</summary>
            public float MinToothCountApprox;
            /// <summary>穴半径が歯元半径以上か。</summary>
            public bool BoreTooLarge;
        }

        /// <summary>派生諸元を求める。パラメータが不正なら Valid=false を返す。</summary>
        public static HelicalGearInfo GetInfo(HelicalGearParams p)
        {
            var info = new HelicalGearInfo { Valid = false };

            if (!TryGetSection(p, out InvoluteTrochoidSection.GearData g, out Transverse t))
                return info;

            InvoluteTrochoidSection.JoinData join =
                InvoluteTrochoidSection.FindTrochoidInvoluteJoin(g);

            info.Valid                      = true;
            info.TransverseModule           = t.ModuleT;
            info.TransversePressureAngleDeg = t.AlphaT * Mathf.Rad2Deg;

            info.PitchDiameter           = 2f * g.rPitch;
            info.BaseDiameter            = 2f * g.rBase;
            info.TipDiameter             = 2f * g.rAddendum;
            info.RootDiameter            = 2f * g.rRoot;
            info.TransverseCircularPitch = Mathf.PI * g.mt;
            info.ToothThicknessPitch     = g.toothThicknessPitch;
            info.JoinRadius              = join.rJoin;

            info.TotalTwistDeg = TotalTwist(p, g, t) * Mathf.Rad2Deg;

            float tanBeta = Mathf.Tan(t.Beta);
            info.Lead = Mathf.Abs(tanBeta) < 1e-7f
                ? float.PositiveInfinity
                : 2f * Mathf.PI * g.rPitch / tanBeta;

            float cosBeta = Mathf.Cos(t.Beta);
            info.VirtualToothCount = p.ToothCount / (cosBeta * cosBeta * cosBeta);

            info.Undercut       = join.undercut;
            info.SevereUndercut = join.severeUndercut;
            info.BoreTooLarge   = p.BoreRadius > 0f && p.BoreRadius >= g.rRoot;

            // 切り下げ限界は法線断面で決まる。比べる相手は相当平歯車の歯数。
            float zMin = InvoluteTrochoidSection.MinToothCountApprox(t.AlphaN, p.AddendumCoef);

            if (zMin > 0f)
            {
                info.MinToothCountApprox = zMin;
                info.BelowMinToothCount =
                    Mathf.Abs(p.ProfileShift) < 1e-6f &&
                    info.VirtualToothCount < zMin;
            }

            return info;
        }

        /// <summary>歯幅ぶんのねじれ量（ラジアン）。右ねじれで正。</summary>
        private static float TotalTwist(
            HelicalGearParams p, InvoluteTrochoidSection.GearData g, Transverse t)
        {
            if (g.rPitch <= 1e-9f) return 0f;

            return t.Hand * Mathf.Max(0f, p.Thickness) * Mathf.Tan(t.Beta) / g.rPitch;
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// はすば歯車メッシュを作る。パラメータが不正なときは空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(HelicalGearParams p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "HelicalGear" : p.MeshName;

            if (!TryGetSection(p, out InvoluteTrochoidSection.GearData g, out Transverse t))
                return new MeshObject(name);

            List<Vector2> outline = InvoluteTrochoidSection.GenerateOutline(
                g, ToSamples(p), p.RotationOffsetDeg * Mathf.Deg2Rad);

            if (outline.Count < 3) return new MeshObject(name);

            // 穴は歯元円より小さくする（外形へ食い込ませない）。
            // ねじっても半径は変わらないので、素の断面で測ってよい。
            float bore = Mathf.Max(0f, p.BoreRadius);
            if (bore > 0f && bore >= g.rRoot) bore = g.rRoot * 0.95f;
            bore = GearLoftBuilder.ClampBoreRadius(outline, bore);

            Vector2[] boreRing = GearLoftBuilder.MakeBoreRing(bore, p.BoreSegments);

            // ── 断面列 ──
            float thickness = Mathf.Max(0f, p.Thickness);
            float totalTwist = TotalTwist(p, g, t);

            int ns;
            if (thickness <= 1e-6f)
                ns = 1;                                                   // 厚み 0 は板 1 枚
            else if (Mathf.Abs(totalTwist) <= 1e-6f)
                ns = 2;                                                   // ねじれ 0 は前後だけでよい
            else
                ns = Mathf.Clamp(p.AxialSegments,
                                 HelicalGearParams.AxialSegmentsMin,
                                 HelicalGearParams.AxialSegmentsMax) + 1;

            var sections = new List<GearLoftSection>(ns);

            float zMin = -0.5f * thickness;
            float zMax = +0.5f * thickness;

            for (int s = 0; s < ns; s++)
            {
                float u = ns > 1 ? s / (float)(ns - 1) : 0f;

                float z = Mathf.Lerp(zMin, zMax, u);
                float twist = Mathf.Lerp(-0.5f * totalTwist, +0.5f * totalTwist, u);

                sections.Add(new GearLoftSection(z, Rotate(outline, twist), boreRing));
            }

            return GearLoftBuilder.Build(
                name,
                sections,
                GearLoftCapMode.Triangulate,
                p.Orientation,
                p.FlipFaces,
                p.Pivot);
        }

        /// <summary>輪郭を原点まわりに回した写しを作る。</summary>
        private static Vector2[] Rotate(IReadOnlyList<Vector2> outline, float angleRad)
        {
            int n = outline.Count;
            var dst = new Vector2[n];

            float c = Mathf.Cos(angleRad);
            float s = Mathf.Sin(angleRad);

            for (int i = 0; i < n; i++)
            {
                Vector2 p = outline[i];
                dst[i] = new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            return dst;
        }
    }
}
