// InternalGearMeshGenerator.cs
// 内歯車（リングギア）のメッシュ生成。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【外歯車との違い】
//   歯が中心を向くので、歯先と歯元が入れ替わる。
//     歯先円半径 = ピッチ円半径 - ha*·m   （内側）
//     歯元円半径 = ピッチ円半径 + hf*·m   （外側）
//   角度半歯厚も符号が入れ替わる。
//     外歯車： β = βp + inv(α) - inv(φ)   歯先へ向かうほど細る
//     内歯車： β = βp + inv(φ) - inv(α)   歯先（内側）へ向かうほど細る
//
// 【歯元にトロコイドを置かない理由】
//   内歯車はラックでは創成できず、ピニオンカッタで削る。歯元のすみ肉はカッタの歯数で
//   変わるため、ラック角トロコイドを当てはめても正しくならない。
//   ここでは歯元をインボリュートのまま歯元円で止め、歯底は円弧でつなぐ。
//
// 【歯先が基礎円より内側のとき】
//   インボリュートはそれ以上内側へ伸ばせない。基礎円で止め、半径方向にまっすぐ
//   歯先円まで下ろしてつなぐ。
//
// 【立体】
//   外周は素の円筒。断面は「外周円 ＋ 内側の歯形」の環になる。
//   外周円の点は内側輪郭の各点と同じ角度に置く。フタは添字対応の四角形帯で塞げるので、
//   歯形の凹凸で三角化が不安定になることがない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class InternalGearMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================

        [System.Serializable]
        public struct InternalGearParams : System.IEquatable<InternalGearParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>歯数の下限・上限</summary>
            public const int ToothCountMin = 6;
            public const int ToothCountMax = 200;

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

            /// <summary>バックラッシの下限・上限</summary>
            public const float BacklashMin = 0f;
            public const float BacklashMax = 0.2f;

            /// <summary>リム（歯底から外周までの肉厚）の下限・上限</summary>
            public const float RimThicknessMin = 0.01f;
            public const float RimThicknessMax = 5f;

            /// <summary>インボリュート曲線の標本数の下限・上限</summary>
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
            /// <summary>歯末のたけ係数 ha*。歯先円半径 = ピッチ円半径 - ha*·m（内向き）</summary>
            [PLParam(TextKey = "GearAddendumCoef", Description = "歯末のたけ係数。ピッチ円から歯先までの高さ ÷ モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float AddendumCoef;
            /// <summary>歯元のたけ係数 hf*。歯元円半径 = ピッチ円半径 + hf*·m（外向き）</summary>
            [PLParam(TextKey = "GearDedendumCoef", Description = "歯元のたけ係数。ピッチ円から歯底までの深さ ÷ モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float DedendumCoef;

            /// <summary>ピッチ円上のバックラッシ</summary>
            [PLParam(TextKey = "InvBacklash", Description = "バックラッシ", Min = BacklashMin, Max = BacklashMax)]
            public float Backlash;

            // ── リム ──
            /// <summary>歯底円から外周までの肉厚</summary>
            [PLParam(TextKey = "IntRimThickness", Description = "歯底から外周までの肉厚", Min = RimThicknessMin,
                     Max = RimThicknessMax)]
            public float RimThickness;

            // ── 曲線のサンプル数 ──
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

            public static InternalGearParams Default => new InternalGearParams
            {
                MeshName          = "InternalGear",
                ToothCount        = 32,
                Module            = 0.1f,
                PressureAngleDeg  = 20f,
                Thickness         = 0.2f,
                AddendumCoef      = 1f,
                DedendumCoef      = 1.25f,
                Backlash          = 0f,
                RimThickness      = 0.15f,
                InvoluteSamples   = 12,
                TipArcSamples     = 3,
                RootArcSamples    = 4,
                RotationOffsetDeg = 0f,
                Orientation       = PlaneOrientation.XY,
                FlipFaces         = false,
                Pivot             = Vector3.zero,
            };

            public bool Equals(InternalGearParams o) =>
                MeshName == o.MeshName &&
                ToothCount == o.ToothCount &&
                Mathf.Approximately(Module,           o.Module)           &&
                Mathf.Approximately(PressureAngleDeg, o.PressureAngleDeg) &&
                Mathf.Approximately(Thickness,        o.Thickness)        &&
                Mathf.Approximately(AddendumCoef,     o.AddendumCoef)     &&
                Mathf.Approximately(DedendumCoef,     o.DedendumCoef)     &&
                Mathf.Approximately(Backlash,         o.Backlash)         &&
                Mathf.Approximately(RimThickness,     o.RimThickness)     &&
                InvoluteSamples == o.InvoluteSamples &&
                TipArcSamples   == o.TipArcSamples   &&
                RootArcSamples  == o.RootArcSamples  &&
                Mathf.Approximately(RotationOffsetDeg, o.RotationOffsetDeg) &&
                Orientation == o.Orientation &&
                FlipFaces   == o.FlipFaces   &&
                Pivot       == o.Pivot;

            public override bool Equals(object obj) => obj is InternalGearParams p && Equals(p);
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

            /// <summary>歯先円半径。ピッチ円より内側。</summary>
            public float rTip;
            /// <summary>歯元円半径。ピッチ円より外側。</summary>
            public float rRoot;
            /// <summary>外周半径。</summary>
            public float rOuter;

            public float pitchAngle;
            public float halfPitchAngle;
            public float toothThicknessPitch;
            public float halfToothAnglePitch;
            public float invAlpha;
        }

        // ================================================================
        // 諸元
        // ================================================================

        private static bool TryGetGearData(InternalGearParams p, out GearData g)
        {
            g = default;

            if (p.ToothCount < 3 ||
                p.Module <= 0f ||
                p.Thickness < 0f ||
                p.RimThickness <= 0f ||
                p.AddendumCoef <= 0f ||
                p.DedendumCoef <= 0f ||
                p.PressureAngleDeg <= 0f ||
                p.PressureAngleDeg >= 45f)
            {
                return false;
            }

            float alpha = p.PressureAngleDeg * Mathf.Deg2Rad;
            float rPitch = p.Module * p.ToothCount * 0.5f;
            float rBase = rPitch * Mathf.Cos(alpha);

            // 歯は中心を向く。歯先はピッチ円の内側、歯元は外側。
            float rTip = rPitch - p.AddendumCoef * p.Module;
            float rRoot = rPitch + p.DedendumCoef * p.Module;
            float rOuter = rRoot + p.RimThickness;

            if (rTip <= 0f || rRoot <= rTip || rOuter <= rRoot)
                return false;

            // ピッチ円上の標準歯厚。バックラッシは歯の肉を削る形で入れる。
            float toothThicknessPitch = Mathf.PI * p.Module * 0.5f - p.Backlash;

            if (toothThicknessPitch <= 0f)
                return false;

            float pitchAngle = 2f * Mathf.PI / p.ToothCount;
            float halfPitchAngle = 0.5f * pitchAngle;
            float halfToothAnglePitch = toothThicknessPitch / (2f * rPitch);

            if (halfToothAnglePitch >= halfPitchAngle)
                return false;

            g = new GearData
            {
                z = p.ToothCount,
                alpha = alpha,

                rPitch = rPitch,
                rBase = rBase,
                rTip = rTip,
                rRoot = rRoot,
                rOuter = rOuter,

                pitchAngle = pitchAngle,
                halfPitchAngle = halfPitchAngle,
                toothThicknessPitch = toothThicknessPitch,
                halfToothAnglePitch = halfToothAnglePitch,
                invAlpha = InvoluteTrochoidSection.InvoluteFunction(alpha),
            };

            return true;
        }

        /// <summary>
        /// 内歯車の角度半歯厚。基礎円以上で有効。
        /// 外歯車と符号が逆で、半径が大きいほど（＝歯元へ向かうほど）太る。
        /// </summary>
        private static float HalfToothAngleAtRadius(GearData g, float radius)
        {
            float r = Mathf.Max(radius, g.rBase);
            float c = Mathf.Clamp(g.rBase / r, -1f, 1f);
            float phi = Mathf.Acos(c);

            return g.halfToothAnglePitch
                 + InvoluteTrochoidSection.InvoluteFunction(phi)
                 - g.invAlpha;
        }

        // ================================================================
        // 派生諸元（UI 表示用）
        // ================================================================

        /// <summary>パネルに出す派生諸元。</summary>
        public struct InternalGearInfo
        {
            /// <summary>諸元が成立しているか。false のとき他のフィールドは無効。</summary>
            public bool Valid;

            public float PitchDiameter;
            public float BaseDiameter;
            /// <summary>歯先円直径（内側）</summary>
            public float TipDiameter;
            /// <summary>歯元円直径（外側）</summary>
            public float RootDiameter;
            public float OuterDiameter;
            public float CircularPitch;
            public float ToothThicknessPitch;

            /// <summary>歯先が基礎円より内側で、歯面の一部が半径方向の直線になっているか。</summary>
            public bool TipBelowBase;
        }

        /// <summary>派生諸元を求める。パラメータが不正なら Valid=false を返す。</summary>
        public static InternalGearInfo GetInfo(InternalGearParams p)
        {
            var info = new InternalGearInfo { Valid = false };

            if (!TryGetGearData(p, out GearData g))
                return info;

            info.Valid               = true;
            info.PitchDiameter       = 2f * g.rPitch;
            info.BaseDiameter        = 2f * g.rBase;
            info.TipDiameter         = 2f * g.rTip;
            info.RootDiameter        = 2f * g.rRoot;
            info.OuterDiameter       = 2f * g.rOuter;
            info.CircularPitch       = Mathf.PI * p.Module;
            info.ToothThicknessPitch = g.toothThicknessPitch;
            info.TipBelowBase        = g.rTip < g.rBase;

            return info;
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// 内歯車メッシュを作る。パラメータが不正なときは空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(InternalGearParams p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "InternalGear" : p.MeshName;

            if (!TryGetGearData(p, out GearData g))
                return new MeshObject(name);

            List<Vector2> inner = GenerateInnerOutline(g, p);
            if (inner.Count < 3) return new MeshObject(name);

            // 外周円は内側輪郭の各点と同じ角度に置く。
            // 添字がそのまま対応するので、フタを四角形帯で確実に塞げる。
            int n = inner.Count;

            var hole = new Vector2[n];
            var outer = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                hole[i] = inner[i];

                float a = Mathf.Atan2(inner[i].y, inner[i].x);
                outer[i] = GearDiskBuilder.Polar(g.rOuter, a);
            }

            // ── 断面列 ──
            float thickness = Mathf.Max(0f, p.Thickness);
            int ns = thickness <= 1e-6f ? 1 : 2;

            var sections = new List<GearLoftSection>(ns);

            if (ns == 1)
            {
                sections.Add(new GearLoftSection(0f, outer, hole));
            }
            else
            {
                sections.Add(new GearLoftSection(-0.5f * thickness, outer, hole));
                sections.Add(new GearLoftSection(+0.5f * thickness, outer, hole));
            }

            return GearLoftBuilder.Build(
                name,
                sections,
                GearLoftCapMode.IndexBand,
                p.Orientation,
                p.FlipFaces,
                p.Pivot);
        }

        // ================================================================
        // 輪郭
        // ================================================================

        /// <summary>
        /// 中心の開口を囲む閉じた輪郭（CCW）を 1 本作る。
        ///
        /// 歯 1 枚あたり：
        ///   歯元 → 左インボリュート（内向き） → 必要なら半径方向の渡り → 歯先円弧
        ///        → 必要なら半径方向の渡り → 右インボリュート → 歯底円弧
        /// </summary>
        private static List<Vector2> GenerateInnerOutline(GearData g, InternalGearParams p)
        {
            int invSamples = Mathf.Clamp(p.InvoluteSamples,
                InternalGearParams.CurveSamplesMin, InternalGearParams.CurveSamplesMax);
            int tipSamples = Mathf.Clamp(p.TipArcSamples,
                InternalGearParams.ArcSamplesMin, InternalGearParams.ArcSamplesMax);
            int rootSamples = Mathf.Clamp(p.RootArcSamples,
                InternalGearParams.ArcSamplesMin, InternalGearParams.ArcSamplesMax);

            var outline = new List<Vector2>(
                g.z * (invSamples * 2 + tipSamples + rootSamples + 8));

            float rotation = p.RotationOffsetDeg * Mathf.Deg2Rad;

            // インボリュートは基礎円より内側へは伸ばせない。
            float rInvoluteEnd = Mathf.Max(g.rTip, g.rBase);
            bool tipBelowBase = g.rTip < g.rBase;

            float betaEnd = HalfToothAngleAtRadius(g, rInvoluteEnd);
            float betaRoot = HalfToothAngleAtRadius(g, g.rRoot);

            for (int tooth = 0; tooth < g.z; tooth++)
            {
                float c = rotation + tooth * g.pitchAngle;

                // 左のインボリュート：歯元 → 歯先（または基礎円）
                for (int j = 0; j <= invSamples; j++)
                {
                    float u = j / (float)invSamples;
                    float r = Mathf.Lerp(g.rRoot, rInvoluteEnd, u);
                    float beta = HalfToothAngleAtRadius(g, r);

                    outline.Add(GearDiskBuilder.Polar(r, c - beta));
                }

                // 基礎円から歯先円までを半径方向にまっすぐ下ろす。
                if (tipBelowBase)
                    outline.Add(GearDiskBuilder.Polar(g.rTip, c - betaEnd));

                // 歯先：左 → 右
                for (int j = 1; j <= tipSamples; j++)
                {
                    float u = j / (float)tipSamples;
                    float a = Mathf.Lerp(c - betaEnd, c + betaEnd, u);

                    outline.Add(GearDiskBuilder.Polar(g.rTip, a));
                }

                // 右側の半径方向の渡り。
                if (tipBelowBase)
                    outline.Add(GearDiskBuilder.Polar(rInvoluteEnd, c + betaEnd));

                // 右のインボリュート：歯先（または基礎円） → 歯元
                for (int j = 1; j <= invSamples; j++)
                {
                    float u = j / (float)invSamples;
                    float r = Mathf.Lerp(rInvoluteEnd, g.rRoot, u);
                    float beta = HalfToothAngleAtRadius(g, r);

                    outline.Add(GearDiskBuilder.Polar(r, c + beta));
                }

                // 歯底円弧：この歯 → 次の歯
                float rootRight = c + betaRoot;
                float nextRootLeft = c + g.pitchAngle - betaRoot;

                for (int j = 1; j < rootSamples; j++)
                {
                    float u = j / (float)rootSamples;
                    float a = Mathf.Lerp(rootRight, nextRootLeft, u);

                    outline.Add(GearDiskBuilder.Polar(g.rRoot, a));
                }
            }

            GearDiskBuilder.RemoveNearlyDuplicateNeighbors(outline, 1e-12f);
            return outline;
        }
    }
}
