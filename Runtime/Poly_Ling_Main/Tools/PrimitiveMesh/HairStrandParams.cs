// HairStrandParams.cs
// 髪の房生成パラメータ（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【生成物】
//   房 M 個。房 1 個は幅方向に N 分割された独立した筒 N 本になる。
//   分岐は無い。筒は根元から毛先まで並んだまま終わる。
//
// 【中心線】
//   土台（球 / 円筒）の表面に沿うパスとして定義する。
//     C(t) = 土台上の点(軸方向 a0 + Δa·t, 周方向 φ0 + Δφ·t) + (R + Lift)·er
//   フレームは土台由来にする。B = er（曲面法線）、T = dC/dt、N = B × T。
//   Frenet を使わないのは、直線部で法線が定まらないことと、捻れが累積することによる。
//
// 【軸方向パラメータの単位】
//   円筒のとき StartAxial / SpanAxial / PitchAxial は長さ。
//   球のとき は赤道からの仰角（度）。極は +Y に固定する。
//   同じフィールドで単位が変わるので、パネルは土台に応じて行を出し分ける。
//
// 【幅・厚み】
//   根元 / 中間 / 末端 の 3 点を独立に指定し、中間位置 tm で 2 分割した冪で結ぶ。
//     t ≦ tm : root + (mid − root)·(t/tm)^pRoot
//     t > tm : tip  + (mid − tip )·(1 − (t−tm)/(1−tm))^pTip
//   根元幅の下限を正にしてあるので、根元が潰れることはない。
//   末端幅を正にすると毛先が平ら（ぱっつん）、0 にすると尖る。

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.PrimitiveMesh;   // PrimitiveMeshPostProcess.PivotMin / PivotMax

namespace Poly_Ling.HairStrand
{
    /// <summary>房を貼り付ける土台の種類。</summary>
    public enum HairBaseType { Sphere, Cylinder }

    /// <summary>円筒の軸。斜めに傾いた円筒は扱わない。</summary>
    public enum HairBaseAxis { X, Y, Z }

    /// <summary>房インデックスに対する変化のさせ方。</summary>
    public enum HairSlopeMode
    {
        /// <summary>片端から反対端へ単調に変化する（u = m/(M−1)、0→1）。</summary>
        Linear,
        /// <summary>中央を基準に両端が対称に変化する（u = 2m/(M−1)−1、−1→+1）。</summary>
        Symmetric,
    }

    /// <summary>髪の房生成パラメータ。</summary>
    [Serializable]
    public struct HairStrandParams : IEquatable<HairStrandParams>
    {
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

        /// <summary>土台の半径の下限・上限</summary>
        public const float RadiusMin = 0.05f;
        public const float RadiusMax = 5f;

        /// <summary>房の本数の下限・上限</summary>
        public const int StrandCountMin = 1;
        public const int StrandCountMax = 32;

        /// <summary>1 房あたりの筒の本数の下限・上限</summary>
        public const int LobeCountMin = 1;
        public const int LobeCountMax = 8;

        /// <summary>根元→毛先の分割数の下限・上限</summary>
        public const int LengthSegmentsMin = 2;
        public const int LengthSegmentsMax = 64;

        /// <summary>断面の分割数の下限・上限</summary>
        public const int SectionSegmentsMin = 3;
        public const int SectionSegmentsMax = 24;

        /// <summary>軸方向の量の下限・上限（円筒＝長さ）</summary>
        public const float AxialLenMin = -5f;
        public const float AxialLenMax = 5f;

        /// <summary>軸方向の量の下限・上限（球＝赤道からの仰角の度数）</summary>
        public const float AxialDegMin = -180f;
        public const float AxialDegMax = 180f;

        /// <summary>周方向の角度の下限・上限（度）</summary>
        public const float AngleMin = -360f;
        public const float AngleMax = 360f;

        /// <summary>土台面からの浮かせ量の下限・上限</summary>
        public const float LiftMin = -0.5f;
        public const float LiftMax = 0.5f;

        /// <summary>根元幅の下限・上限。0 にできないので下限は正。</summary>
        public const float WidthRootMin = 0.001f;
        public const float WidthMax = 1f;

        /// <summary>中間幅・末端幅の下限。末端は 0 にできる（毛先が尖る）。</summary>
        public const float WidthMin = 0f;

        /// <summary>厚みの下限・上限。根元の下限は正。</summary>
        public const float ThickRootMin = 0.001f;
        public const float ThickMin = 0f;
        public const float ThickMax = 1f;

        /// <summary>中間位置の下限・上限</summary>
        public const float MidTMin = 0.05f;
        public const float MidTMax = 0.95f;

        /// <summary>幅・厚みの冪の下限・上限</summary>
        public const float PowMin = 0.1f;
        public const float PowMax = 8f;

        /// <summary>断面のエッジの立ち方の下限・上限</summary>
        public const float SectionPowerMin = 0.1f;
        public const float SectionPowerMax = 4f;

        /// <summary>内側の厚み比の下限・上限</summary>
        public const float InnerRatioMin = 0f;
        public const float InnerRatioMax = 2f;

        /// <summary>捻れの下限・上限（度）</summary>
        public const float TwistMin = -360f;
        public const float TwistMax = 360f;

        /// <summary>房ごとの変化率の下限・上限</summary>
        public const float SlopeMin = -1f;
        public const float SlopeMax = 1f;

        /// <summary>幅配分 1 個分の下限・上限。0 にすると幅 0 の筒ができるので下限は正。</summary>
        public const float LobeWidthMin = 0.01f;
        public const float LobeWidthMax = 1f;

        // ── 名前 ──────────────────────────────────────────────────

        [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
        public string MeshName;

        // ── 土台 ──────────────────────────────────────────────────

        [PLParam(TextKey = "HairBaseType", Description = "房を貼り付ける土台。球 または 円筒")]
        public HairBaseType BaseType;

        [PLParam(TextKey = "HairBaseAxis", Description = "円筒の軸。球のときは使わない（極は +Y 固定）")]
        public HairBaseAxis Axis;

        [PLParam(TextKey = "HairRadius", Description = "土台の半径", Min = RadiusMin, Max = RadiusMax)]
        public float Radius;

        [PLParam(TextKey = "HairStartAxial",
                 Description = "房の開始位置（軸方向）。円筒は長さ、球は赤道からの仰角の度数")]
        public float StartAxial;

        [PLParam(TextKey = "HairStartAngle", Description = "房の開始位置（周方向の角度）",
                 Min = AngleMin, Max = AngleMax)]
        public float StartAngle;

        [PLParam(TextKey = "HairSpanAxial",
                 Description = "根元から毛先までの軸方向の進み。円筒は長さ、球は仰角の度数")]
        public float SpanAxial;

        [PLParam(TextKey = "HairSpanAngle", Description = "根元から毛先までの周方向の進み（度）",
                 Min = AngleMin, Max = AngleMax)]
        public float SpanAngle;

        [PLParam(TextKey = "HairLift", Description = "土台面からの浮かせ量", Min = LiftMin, Max = LiftMax)]
        public float Lift;

        // ── 房の並び ──────────────────────────────────────────────

        [PLParam(TextKey = "HairStrandCount", Description = "房の本数",
                 Min = StrandCountMin, Max = StrandCountMax, Step = 1)]
        public int StrandCount;

        [PLParam(TextKey = "HairPitchAxial",
                 Description = "房と房の間隔（軸方向）。円筒は長さ、球は仰角の度数")]
        public float PitchAxial;

        [PLParam(TextKey = "HairPitchAngle", Description = "房と房の間隔（周方向の角度）",
                 Min = AngleMin, Max = AngleMax)]
        public float PitchAngle;

        // ── 筒の分割 ──────────────────────────────────────────────

        [PLParam(TextKey = "HairLobeCount", Description = "1 房を縦に割る本数",
                 Min = LobeCountMin, Max = LobeCountMax, Step = 1)]
        public int LobeCount;

        [PLParam(TextKey = "HairLobeWidths",
                 Description = "筒ごとの幅の配分。合計は生成時に正規化する。要素数は本数に合わせる")]
        public float[] LobeWidths;

        [PLParam(TextKey = "HairLengthSegments", Description = "根元→毛先の分割数",
                 Min = LengthSegmentsMin, Max = LengthSegmentsMax, Step = 1)]
        public int LengthSegments;

        [PLParam(TextKey = "HairSectionSegments", Description = "断面の分割数",
                 Min = SectionSegmentsMin, Max = SectionSegmentsMax, Step = 1)]
        public int SectionSegments;

        // ── 幅 ────────────────────────────────────────────────────

        [PLParam(TextKey = "HairWidthRoot", Description = "根元の幅", Min = WidthRootMin, Max = WidthMax)]
        public float WidthRoot;

        [PLParam(TextKey = "HairWidthMid", Description = "中間の幅", Min = WidthMin, Max = WidthMax)]
        public float WidthMid;

        [PLParam(TextKey = "HairWidthTip", Description = "末端の幅。0 で毛先が尖る", Min = WidthMin, Max = WidthMax)]
        public float WidthTip;

        [PLParam(TextKey = "HairWidthMidT", Description = "幅が中間値になる位置", Min = MidTMin, Max = MidTMax)]
        public float WidthMidT;

        [PLParam(TextKey = "HairWidthPowRoot", Description = "根元側の幅の変化の冪", Min = PowMin, Max = PowMax)]
        public float WidthPowRoot;

        [PLParam(TextKey = "HairWidthPowTip", Description = "末端側の幅の変化の冪", Min = PowMin, Max = PowMax)]
        public float WidthPowTip;

        // ── 厚み ──────────────────────────────────────────────────

        [PLParam(TextKey = "HairThickRoot", Description = "根元の厚み", Min = ThickRootMin, Max = ThickMax)]
        public float ThickRoot;

        [PLParam(TextKey = "HairThickMid", Description = "中間の厚み", Min = ThickMin, Max = ThickMax)]
        public float ThickMid;

        [PLParam(TextKey = "HairThickTip", Description = "末端の厚み", Min = ThickMin, Max = ThickMax)]
        public float ThickTip;

        [PLParam(TextKey = "HairThickMidT", Description = "厚みが中間値になる位置", Min = MidTMin, Max = MidTMax)]
        public float ThickMidT;

        [PLParam(TextKey = "HairThickPowRoot", Description = "根元側の厚みの変化の冪", Min = PowMin, Max = PowMax)]
        public float ThickPowRoot;

        [PLParam(TextKey = "HairThickPowTip", Description = "末端側の厚みの変化の冪", Min = PowMin, Max = PowMax)]
        public float ThickPowTip;

        // ── 断面 ──────────────────────────────────────────────────

        [PLParam(TextKey = "HairSectionPower",
                 Description = "断面の形。1 で楕円、小さいほど矩形寄り、大きいほど平板寄り",
                 Min = SectionPowerMin, Max = SectionPowerMax)]
        public float SectionPower;

        [PLParam(TextKey = "HairInnerRatio",
                 Description = "土台側（内側）の厚みの比。小さくするとかまぼこ断面になる",
                 Min = InnerRatioMin, Max = InnerRatioMax)]
        public float InnerRatio;

        [PLParam(TextKey = "HairTwist", Description = "根元から毛先までの捻れ（度）", Min = TwistMin, Max = TwistMax)]
        public float Twist;

        // ── 房ごとの変化 ──────────────────────────────────────────

        [PLParam(TextKey = "HairSlopeMode", Description = "房インデックスに対する変化のさせ方")]
        public HairSlopeMode SlopeMode;

        [PLParam(TextKey = "HairLenSlope", Description = "房ごとの長さの変化率", Min = SlopeMin, Max = SlopeMax)]
        public float LenSlope;

        [PLParam(TextKey = "HairWidthSlope", Description = "房ごとの中間幅の変化率", Min = SlopeMin, Max = SlopeMax)]
        public float WidthSlope;

        [PLParam(TextKey = "HairThickSlope", Description = "房ごとの中間厚みの変化率", Min = SlopeMin, Max = SlopeMax)]
        public float ThickSlope;

        [PLParam(TextKey = "HairLiftSlope", Description = "房ごとの浮かせ量の変化率", Min = SlopeMin, Max = SlopeMax)]
        public float LiftSlope;

        [PLParam(TextKey = "HairTwistSlope", Description = "房ごとの捻れの変化率", Min = SlopeMin, Max = SlopeMax)]
        public float TwistSlope;

        // ── 共通 ──────────────────────────────────────────────────

        [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
        public bool FlipFaces;

        [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                 Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
        public Vector3 Pivot;

        [PLParam(Ignore = true, Description = "プレビューの視点角。形状には影響しない")]
        public float RotationX;

        [PLParam(Ignore = true, Description = "プレビューの視点角。形状には影響しない")]
        public float RotationY;

        // ================================================================
        // 既定値
        // ================================================================

        public static HairStrandParams Default => new HairStrandParams
        {
            MeshName        = "HairStrand",

            BaseType        = HairBaseType.Sphere,
            Axis            = HairBaseAxis.Y,
            Radius          = 0.5f,
            StartAxial      = 60f,
            StartAngle      = 0f,
            SpanAxial       = -70f,
            SpanAngle       = 0f,
            Lift            = 0.01f,

            StrandCount     = 5,
            PitchAxial      = 0f,
            PitchAngle      = 22f,

            LobeCount       = 3,
            LobeWidths      = EqualLobeWidths(3),
            LengthSegments  = 16,
            SectionSegments = 8,

            WidthRoot       = 0.10f,
            WidthMid        = 0.12f,
            WidthTip        = 0.02f,
            WidthMidT       = 0.35f,
            WidthPowRoot    = 1f,
            WidthPowTip     = 1.5f,

            ThickRoot       = 0.03f,
            ThickMid        = 0.035f,
            ThickTip        = 0.008f,
            ThickMidT       = 0.35f,
            ThickPowRoot    = 1f,
            ThickPowTip     = 1.5f,

            SectionPower    = 1f,
            InnerRatio      = 0.6f,
            Twist           = 0f,

            SlopeMode       = HairSlopeMode.Symmetric,
            LenSlope        = 0f,
            WidthSlope      = 0f,
            ThickSlope      = 0f,
            LiftSlope       = 0f,
            TwistSlope      = 0f,

            FlipFaces       = false,
            Pivot           = Vector3.zero,
            RotationX       = 20f,
            RotationY       = 30f,
        };

        /// <summary>等分の幅配分を作る。</summary>
        public static float[] EqualLobeWidths(int count)
        {
            int n = Mathf.Clamp(count, LobeCountMin, LobeCountMax);
            var a = new float[n];
            float v = 1f / n;
            for (int i = 0; i < n; i++) a[i] = v;
            return a;
        }

        // ================================================================
        // 比較
        // ================================================================

        public bool Equals(HairStrandParams o)
        {
            if (MeshName != o.MeshName) return false;

            if (BaseType != o.BaseType || Axis != o.Axis) return false;
            if (!Mathf.Approximately(Radius,     o.Radius))     return false;
            if (!Mathf.Approximately(StartAxial, o.StartAxial)) return false;
            if (!Mathf.Approximately(StartAngle, o.StartAngle)) return false;
            if (!Mathf.Approximately(SpanAxial,  o.SpanAxial))  return false;
            if (!Mathf.Approximately(SpanAngle,  o.SpanAngle))  return false;
            if (!Mathf.Approximately(Lift,       o.Lift))       return false;

            if (StrandCount != o.StrandCount) return false;
            if (!Mathf.Approximately(PitchAxial, o.PitchAxial)) return false;
            if (!Mathf.Approximately(PitchAngle, o.PitchAngle)) return false;

            if (LobeCount       != o.LobeCount)       return false;
            if (LengthSegments  != o.LengthSegments)  return false;
            if (SectionSegments != o.SectionSegments) return false;

            if (!Mathf.Approximately(WidthRoot,    o.WidthRoot))    return false;
            if (!Mathf.Approximately(WidthMid,     o.WidthMid))     return false;
            if (!Mathf.Approximately(WidthTip,     o.WidthTip))     return false;
            if (!Mathf.Approximately(WidthMidT,    o.WidthMidT))    return false;
            if (!Mathf.Approximately(WidthPowRoot, o.WidthPowRoot)) return false;
            if (!Mathf.Approximately(WidthPowTip,  o.WidthPowTip))  return false;

            if (!Mathf.Approximately(ThickRoot,    o.ThickRoot))    return false;
            if (!Mathf.Approximately(ThickMid,     o.ThickMid))     return false;
            if (!Mathf.Approximately(ThickTip,     o.ThickTip))     return false;
            if (!Mathf.Approximately(ThickMidT,    o.ThickMidT))    return false;
            if (!Mathf.Approximately(ThickPowRoot, o.ThickPowRoot)) return false;
            if (!Mathf.Approximately(ThickPowTip,  o.ThickPowTip))  return false;

            if (!Mathf.Approximately(SectionPower, o.SectionPower)) return false;
            if (!Mathf.Approximately(InnerRatio,   o.InnerRatio))   return false;
            if (!Mathf.Approximately(Twist,        o.Twist))        return false;

            if (SlopeMode != o.SlopeMode) return false;
            if (!Mathf.Approximately(LenSlope,   o.LenSlope))   return false;
            if (!Mathf.Approximately(WidthSlope, o.WidthSlope)) return false;
            if (!Mathf.Approximately(ThickSlope, o.ThickSlope)) return false;
            if (!Mathf.Approximately(LiftSlope,  o.LiftSlope))  return false;
            if (!Mathf.Approximately(TwistSlope, o.TwistSlope)) return false;

            if (FlipFaces != o.FlipFaces) return false;
            if (Pivot != o.Pivot) return false;
            if (!Mathf.Approximately(RotationX, o.RotationX)) return false;
            if (!Mathf.Approximately(RotationY, o.RotationY)) return false;

            // 幅配分の比較
            if (LobeWidths == null && o.LobeWidths == null) return true;
            if (LobeWidths == null || o.LobeWidths == null) return false;
            if (LobeWidths.Length != o.LobeWidths.Length) return false;
            for (int i = 0; i < LobeWidths.Length; i++)
                if (!Mathf.Approximately(LobeWidths[i], o.LobeWidths[i])) return false;

            return true;
        }

        public override bool Equals(object obj) => obj is HairStrandParams p && Equals(p);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }
}
