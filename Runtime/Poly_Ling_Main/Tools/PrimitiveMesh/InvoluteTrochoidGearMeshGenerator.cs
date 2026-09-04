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
//
// 【歯形そのものは共有部品にある】
//   断面の数学は Gears/InvoluteTrochoidSection が持つ。はすば歯車・ウォームホイール・
//   かさ歯車も同じ断面を使うため、ここに写しを置かない。
//   このファイルはパネルのパラメータを共有断面の入力へ直し、押し出しを頼むだけ。

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
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>歯数の下限・上限</summary>
            public const int ToothCountMin = 3;
            public const int ToothCountMax = 120;

            /// <summary>モジュールの下限・上限</summary>
            public const float ModuleMin = 0.01f;
            public const float ModuleMax = 1f;

            /// <summary>圧力角の下限・上限（度）</summary>
            public const float PressureAngleMin = 10f;
            public const float PressureAngleMax = 35f;

            /// <summary>厚みの下限・上限</summary>
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

            /// <summary>回転オフセットの下限・上限（度）</summary>
            public const float RotationOffsetMin = 0f;
            public const float RotationOffsetMax = 360f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            // ── 基本諸元 ──
            /// <summary>歯数 z</summary>
            [PLParam(TextKey = "InvToothCount", Description = "歯数", Min = ToothCountMin, Max = ToothCountMax,
                     Step = 1)]
            public int ToothCount;
            /// <summary>モジュール m</summary>
            [PLParam(TextKey = "InvModule", Description = "モジュール（歯の大きさ）", Min = ModuleMin, Max = ModuleMax)]
            public float Module;
            /// <summary>圧力角 α（度）</summary>
            [PLParam(TextKey = "InvPressureAngle", Description = "圧力角（度）", Min = PressureAngleMin,
                     Max = PressureAngleMax)]
            public float PressureAngleDeg;
            /// <summary>厚み</summary>
            [PLParam(TextKey = "Thickness", Description = "厚み。0 で板", Min = ThicknessMin, Max = ThicknessMax)]
            public float Thickness;

            // ── 歯たけ ──
            /// <summary>歯末のたけ係数 ha*。歯先円半径 = ピッチ円半径 + m(ha* + x)</summary>
            [PLParam(TextKey = "GearAddendumCoef", Description = "歯末のたけ係数。ピッチ円から歯先までの高さ ÷ モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float AddendumCoef;
            /// <summary>歯元のたけ係数 hf*。ラックカッタが歯元へ食い込む量 = hf* × m</summary>
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

            // ── 配置 ──
            /// <summary>全体の回転オフセット（度）</summary>
            [PLParam(TextKey = "GearRotationOffset", Description = "全体の回転オフセット（度）", Min = RotationOffsetMin,
                     Max = RotationOffsetMax)]
            public float RotationOffsetDeg;

            /// <summary>板を置く平面</summary>
            [PLParam(TextKey = "Orientation", Description = "板の向き（XY / XZ / YZ）")]
            public PlaneOrientation Orientation;
            /// <summary>生成後にメッシュ全体の面を反転する</summary>
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            /// <summary>AABB サイズ基準のピボット</summary>
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static InvoluteGearParams Default => new InvoluteGearParams
            {
                MeshName          = "InvoluteGear",
                ToothCount        = 16,
                Module            = 0.1f,
                PressureAngleDeg  = 20f,
                Thickness         = 0.2f,
                AddendumCoef      = 1f,
                DedendumCoef      = 1.25f,
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
                Mathf.Approximately(AddendumCoef,      o.AddendumCoef)      &&
                Mathf.Approximately(DedendumCoef,      o.DedendumCoef)      &&
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
        // 共有断面への受け渡し
        // ================================================================

        /// <summary>
        /// パネルのパラメータを共有断面の入力へ直す。
        ///
        /// 平歯車では正面と法線が一致するので、モジュールも圧力角も同じ値を両方へ入れる。
        /// はすば歯車だけが正面モジュール mt = mn/cos(β)、正面圧力角 tan(αt) = tan(αn)/cos(β)
        /// を使う。
        /// </summary>
        private static InvoluteTrochoidSection.SectionInput ToSectionInput(InvoluteGearParams p)
        {
            float alpha = p.PressureAngleDeg * Mathf.Deg2Rad;

            return new InvoluteTrochoidSection.SectionInput
            {
                ToothCount              = p.ToothCount,
                TransverseModule        = p.Module,
                RadialModule            = p.Module,
                TransversePressureAngle = alpha,
                NormalPressureAngle     = alpha,
                ProfileShift            = p.ProfileShift,
                Backlash                = p.Backlash,
                AddendumCoef            = p.AddendumCoef,
                DedendumCoef            = p.DedendumCoef,
            };
        }

        private static InvoluteTrochoidSection.Samples ToSamples(InvoluteGearParams p)
            => new InvoluteTrochoidSection.Samples
            {
                Trochoid = p.TrochoidSamples,
                Involute = p.InvoluteSamples,
                TipArc   = p.TipArcSamples,
                RootArc  = p.RootArcSamples,
            };

        /// <summary>
        /// 諸元を求める。厚みは断面の数学に関わらないので、ここだけで見る。
        /// </summary>
        private static bool TryGetSection(
            InvoluteGearParams p, out InvoluteTrochoidSection.GearData g)
        {
            g = default;

            if (p.Thickness < 0f) return false;

            return InvoluteTrochoidSection.TryGetGearData(ToSectionInput(p), out g);
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

            if (!TryGetSection(p, out InvoluteTrochoidSection.GearData g))
                return info;

            InvoluteTrochoidSection.JoinData join =
                InvoluteTrochoidSection.FindTrochoidInvoluteJoin(g);

            info.Valid               = true;
            info.PitchDiameter       = 2f * g.rPitch;
            info.BaseDiameter        = 2f * g.rBase;
            info.TipDiameter         = 2f * g.rAddendum;
            info.RootDiameter        = 2f * g.rRoot;
            info.CircularPitch       = Mathf.PI * g.mt;
            info.ToothThicknessPitch = g.toothThicknessPitch;
            info.JoinRadius          = join.rJoin;
            info.Undercut            = join.undercut;
            info.SevereUndercut      = join.severeUndercut;
            info.BoreTooLarge        = p.BoreRadius > 0f && p.BoreRadius >= g.rRoot;

            float zMin = InvoluteTrochoidSection.MinToothCountApprox(g.alpha, p.AddendumCoef);

            if (zMin > 0f)
            {
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
            if (!TryGetSection(p, out InvoluteTrochoidSection.GearData g))
                return new MeshObject(string.IsNullOrEmpty(p.MeshName) ? "InvoluteGear" : p.MeshName);

            // 穴は歯元円より小さくする（外形へ食い込ませない）。
            float bore = Mathf.Max(0f, p.BoreRadius);
            if (bore > 0f && bore >= g.rRoot) bore = g.rRoot * 0.95f;

            List<Vector2> outline = InvoluteTrochoidSection.GenerateOutline(
                g, ToSamples(p), p.RotationOffsetDeg * Mathf.Deg2Rad);

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
    }
}
