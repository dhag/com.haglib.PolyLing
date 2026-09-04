// CylindricalWormMeshGenerator.cs
// 円筒ウォームのメッシュ生成。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【形状】
//   円筒の上を台形のねじ山がらせんに走る。軸は Z。
//   軸断面（Z を含む平面での切り口）が台形歯になる、いわゆる ZA 形として作る。
//
//     ピッチ円半径 = q·mx/2
//     歯先円半径   = ピッチ円半径 + ha*·mx
//     歯底円半径   = ピッチ円半径 - hf*·mx
//     軸方向ピッチ px = π·mx、リード = px·z1
//     軸断面での半歯厚は ピッチ線上 px/4、歯先で -ha*·mx·tan(αx)、歯元で +hf*·mx·tan(αx)
//
// 【断面の作り方】
//   Z を一定にした切り口は、角度 θ ごとに半径が変わる閉じた輪郭になる。
//
//       位相 = z - hand·リード·θ/(2π) - 位相ずらし
//       半径 = 台形(位相)
//
//   この輪郭を Z 方向に並べてロフトする。台形の評価は Gears/RackToothSection と共用。
//
// 【バックラッシ】
//   元のエディタ版に合わせて持たせていない。歯厚はピッチ線上でちょうど px/2 になる。
//   すきまはかみ合う相手（ウォームホイール）側のバックラッシで付ける。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class CylindricalWormMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================

        [System.Serializable]
        public struct CylindricalWormParams : System.IEquatable<CylindricalWormParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>軸方向モジュールの下限・上限</summary>
            public const float ModuleMin = 0.01f;
            public const float ModuleMax = 1f;

            /// <summary>条数の下限・上限</summary>
            public const int StartsMin = 1;
            public const int StartsMax = 8;

            /// <summary>直径係数の下限・上限</summary>
            public const float DiameterFactorMin = 3f;
            public const float DiameterFactorMax = 30f;

            /// <summary>法線圧力角の下限・上限（度）</summary>
            public const float PressureAngleMin = 10f;
            public const float PressureAngleMax = 35f;

            /// <summary>ウォームの長さの下限・上限</summary>
            public const float LengthMin = 0.01f;
            public const float LengthMax = 5f;

            /// <summary>歯末のたけ係数・歯元のたけ係数の下限・上限</summary>
            public const float ToothDepthCoefMin = 0.1f;
            public const float ToothDepthCoefMax = 2f;

            /// <summary>軸穴半径の下限・上限</summary>
            public const float BoreRadiusMin = 0f;
            public const float BoreRadiusMax = 5f;

            /// <summary>円周分割数の下限・上限</summary>
            public const int CircumferentialSegmentsMin = 16;
            public const int CircumferentialSegmentsMax = 256;

            /// <summary>1 軸方向ピッチあたりの標本数の下限・上限</summary>
            public const int SamplesPerPitchMin = 4;
            public const int SamplesPerPitchMax = 64;

            /// <summary>
            /// 軸方向の総分割数の上限。長さ ÷ 軸方向ピッチ × 標本数がこれを超えたら抑える。
            /// 円周分割との掛け算になるので、頭を押さえないと頂点数が跳ね上がる。
            /// </summary>
            public const int AxialDivisionsMax = 512;

            /// <summary>回転オフセットの下限・上限（度）</summary>
            public const float RotationOffsetMin = 0f;
            public const float RotationOffsetMax = 360f;

            /// <summary>ねじ山の軸方向ずらしの下限・上限（軸方向ピッチ単位）</summary>
            public const float PhaseOffsetMin = -1f;
            public const float PhaseOffsetMax =  1f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            // ── 基本諸元 ──
            /// <summary>軸方向モジュール mx</summary>
            [PLParam(TextKey = "WormAxialModule", Description = "軸方向モジュール。相手ホイールの正面モジュールになる",
                     Min = ModuleMin, Max = ModuleMax)]
            public float AxialModule;
            /// <summary>条数 z1</summary>
            [PLParam(TextKey = "WormStarts", Description = "条数。多いほど進み角が大きくなる", Min = StartsMin,
                     Max = StartsMax, Step = 1)]
            public int Starts;
            /// <summary>直径係数 q</summary>
            [PLParam(TextKey = "WormDiameterFactor", Description = "直径係数 q。ピッチ円直径 = q × 軸方向モジュール",
                     Min = DiameterFactorMin, Max = DiameterFactorMax)]
            public float DiameterFactorQ;
            /// <summary>法線圧力角 αn（度）</summary>
            [PLParam(TextKey = "HelNormalPressureAngle", Description = "法線圧力角（度）", Min = PressureAngleMin,
                     Max = PressureAngleMax)]
            public float NormalPressureAngleDeg;
            /// <summary>右ねじなら true</summary>
            [PLParam(TextKey = "WormRightHand", Description = "右ねじにする。外すと左ねじ")]
            public bool RightHand;
            /// <summary>軸方向の長さ</summary>
            [PLParam(TextKey = "WormLength", Description = "軸方向の長さ", Min = LengthMin, Max = LengthMax)]
            public float Length;

            // ── 歯たけ ──
            /// <summary>歯末のたけ係数 ha*</summary>
            [PLParam(TextKey = "GearAddendumCoef", Description = "歯末のたけ係数。ピッチ円から歯先までの高さ ÷ 軸方向モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float AddendumCoef;
            /// <summary>歯元のたけ係数 hf*</summary>
            [PLParam(TextKey = "GearDedendumCoef", Description = "歯元のたけ係数。ピッチ円から歯底までの深さ ÷ 軸方向モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float DedendumCoef;

            // ── 穴 ──
            /// <summary>中心の丸穴半径。0 で穴なし。</summary>
            [PLParam(TextKey = "GearBoreRadius", Description = "軸穴の半径。0 で穴なし", Min = BoreRadiusMin,
                     Max = BoreRadiusMax)]
            public float BoreRadius;
            /// <summary>穴リングの分割数</summary>
            [PLParam(TextKey = "GearBoreSegments", Description = "軸穴の円周分割数",
                     Min = GearDiskBuilder.BoreSegmentsMin, Max = GearDiskBuilder.BoreSegmentsMax, Step = 1)]
            public int BoreSegments;

            // ── 標本数 ──
            /// <summary>円周方向の分割数</summary>
            [PLParam(TextKey = "WormCircumferentialSegments", Description = "円周方向の分割数",
                     Min = CircumferentialSegmentsMin, Max = CircumferentialSegmentsMax, Step = 1)]
            public int CircumferentialSegments;
            /// <summary>1 軸方向ピッチあたりの標本数</summary>
            [PLParam(TextKey = "WormSamplesPerPitch", Description = "1 軸方向ピッチあたりの標本数",
                     Min = SamplesPerPitchMin, Max = SamplesPerPitchMax, Step = 1)]
            public int SamplesPerPitch;

            // ── 配置 ──
            /// <summary>全体の回転オフセット（度）</summary>
            [PLParam(TextKey = "GearRotationOffset", Description = "全体の回転オフセット（度）", Min = RotationOffsetMin,
                     Max = RotationOffsetMax)]
            public float RotationOffsetDeg;
            /// <summary>ねじ山を軸方向へずらす量（軸方向ピッチ単位）</summary>
            [PLParam(TextKey = "WormPhaseOffset", Description = "ねじ山を軸方向へずらす量（ピッチ単位）",
                     Min = PhaseOffsetMin, Max = PhaseOffsetMax)]
            public float PhaseOffset;

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

            public static CylindricalWormParams Default => new CylindricalWormParams
            {
                MeshName                = "CylindricalWorm",
                AxialModule             = 0.1f,
                Starts                  = 1,
                DiameterFactorQ         = 11f,
                NormalPressureAngleDeg  = 20f,
                RightHand               = true,
                Length                  = 1f,
                AddendumCoef            = 1f,
                DedendumCoef            = 1.25f,
                BoreRadius              = 0f,
                BoreSegments            = 24,
                CircumferentialSegments = 64,
                SamplesPerPitch         = 16,
                RotationOffsetDeg       = 0f,
                PhaseOffset             = 0f,
                Orientation             = PlaneOrientation.XY,
                FlipFaces               = false,
                Pivot                   = Vector3.zero,
            };

            public bool Equals(CylindricalWormParams o) =>
                MeshName == o.MeshName &&
                Mathf.Approximately(AxialModule,            o.AxialModule)            &&
                Starts == o.Starts &&
                Mathf.Approximately(DiameterFactorQ,        o.DiameterFactorQ)        &&
                Mathf.Approximately(NormalPressureAngleDeg, o.NormalPressureAngleDeg) &&
                RightHand == o.RightHand &&
                Mathf.Approximately(Length,                 o.Length)                 &&
                Mathf.Approximately(AddendumCoef,           o.AddendumCoef)           &&
                Mathf.Approximately(DedendumCoef,           o.DedendumCoef)           &&
                Mathf.Approximately(BoreRadius,             o.BoreRadius)             &&
                BoreSegments            == o.BoreSegments            &&
                CircumferentialSegments == o.CircumferentialSegments &&
                SamplesPerPitch         == o.SamplesPerPitch         &&
                Mathf.Approximately(RotationOffsetDeg, o.RotationOffsetDeg) &&
                Mathf.Approximately(PhaseOffset,       o.PhaseOffset)       &&
                Orientation == o.Orientation &&
                FlipFaces   == o.FlipFaces   &&
                Pivot       == o.Pivot;

            public override bool Equals(object obj) => obj is CylindricalWormParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 内部データ
        // ================================================================

        private struct WormData
        {
            public WormPairSection.PairData pair;

            public float tipRadius;
            public float rootRadius;

            public float pitchHalfWidth;
            public float tipHalfWidth;
            public float rootHalfWidth;
        }

        private static bool TryGetWormData(CylindricalWormParams p, out WormData g)
        {
            g = default;

            if (p.Length <= 0f ||
                p.AddendumCoef <= 0f ||
                p.DedendumCoef <= 0f)
            {
                return false;
            }

            var input = new WormPairSection.PairInput
            {
                AxialModule         = p.AxialModule,
                Starts              = p.Starts,
                DiameterFactorQ     = p.DiameterFactorQ,
                NormalPressureAngle = p.NormalPressureAngleDeg * Mathf.Deg2Rad,
                Hand                = p.RightHand ? 1f : -1f,
            };

            if (!WormPairSection.TryGetPairData(input, out var pair)) return false;

            float tipRadius = pair.wormPitchRadius + p.AddendumCoef * pair.mx;
            float rootRadius = pair.wormPitchRadius - p.DedendumCoef * pair.mx;

            if (rootRadius <= 0f || tipRadius <= rootRadius) return false;

            float tanAx = Mathf.Tan(pair.alphaX);

            float pitchHalfWidth = pair.axialPitch * 0.25f;
            float tipHalfWidth = pitchHalfWidth - p.AddendumCoef * pair.mx * tanAx;
            float rootHalfWidth = pitchHalfWidth + p.DedendumCoef * pair.mx * tanAx;

            // 歯先がとがり切る、あるいは歯溝が閉じるところで打ち切る。
            if (tipHalfWidth <= 0f || rootHalfWidth >= pair.axialPitch * 0.5f) return false;

            g = new WormData
            {
                pair = pair,
                tipRadius = tipRadius,
                rootRadius = rootRadius,
                pitchHalfWidth = pitchHalfWidth,
                tipHalfWidth = tipHalfWidth,
                rootHalfWidth = rootHalfWidth,
            };

            return true;
        }

        /// <summary>軸方向の位相における、ねじ山の半径。</summary>
        private static float ThreadRadius(WormData g, float phase)
            => RackToothSection.EvaluateTrapezoid(
                phase, g.pair.axialPitch,
                g.tipHalfWidth, g.rootHalfWidth,
                g.tipRadius, g.rootRadius,
                Mathf.Tan(g.pair.alphaX));

        // ================================================================
        // 派生諸元（UI 表示用）
        // ================================================================

        /// <summary>パネルに出す派生諸元。</summary>
        public struct WormInfo
        {
            /// <summary>諸元が成立しているか。false のとき他のフィールドは無効。</summary>
            public bool Valid;

            public float PitchDiameter;
            public float TipDiameter;
            public float RootDiameter;

            public float AxialPitch;
            public float Lead;
            /// <summary>進み角（度）</summary>
            public float LeadAngleDeg;
            /// <summary>軸直角の圧力角（度）</summary>
            public float AxialPressureAngleDeg;

            /// <summary>長さのなかに入るねじ山の数。</summary>
            public float ThreadTurns;

            /// <summary>穴半径が歯底半径以上か。</summary>
            public bool BoreTooLarge;
        }

        /// <summary>派生諸元を求める。パラメータが不正なら Valid=false を返す。</summary>
        public static WormInfo GetInfo(CylindricalWormParams p)
        {
            var info = new WormInfo { Valid = false };

            if (!TryGetWormData(p, out WormData g)) return info;

            info.Valid = true;

            info.PitchDiameter = 2f * g.pair.wormPitchRadius;
            info.TipDiameter   = 2f * g.tipRadius;
            info.RootDiameter  = 2f * g.rootRadius;

            info.AxialPitch            = g.pair.axialPitch;
            info.Lead                  = g.pair.lead;
            info.LeadAngleDeg          = g.pair.gamma * Mathf.Rad2Deg;
            info.AxialPressureAngleDeg = g.pair.alphaX * Mathf.Rad2Deg;

            info.ThreadTurns = g.pair.axialPitch > 1e-9f
                ? Mathf.Max(0f, p.Length) / g.pair.axialPitch
                : 0f;

            info.BoreTooLarge = p.BoreRadius > 0f && p.BoreRadius >= g.rootRadius;

            return info;
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// 円筒ウォームメッシュを作る。パラメータが不正なときは空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(CylindricalWormParams p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "CylindricalWorm" : p.MeshName;

            if (!TryGetWormData(p, out WormData g)) return new MeshObject(name);

            int nt = Mathf.Clamp(p.CircumferentialSegments,
                CylindricalWormParams.CircumferentialSegmentsMin,
                CylindricalWormParams.CircumferentialSegmentsMax);

            int perPitch = Mathf.Clamp(p.SamplesPerPitch,
                CylindricalWormParams.SamplesPerPitchMin,
                CylindricalWormParams.SamplesPerPitchMax);

            float length = Mathf.Max(0f, p.Length);

            int nz = Mathf.Clamp(
                Mathf.CeilToInt(length / g.pair.axialPitch * perPitch),
                8, CylindricalWormParams.AxialDivisionsMax);

            // 穴は歯底円より小さくする。
            float bore = Mathf.Max(0f, p.BoreRadius);
            if (bore > 0f && bore >= g.rootRadius) bore = g.rootRadius * 0.95f;

            Vector2[] boreRing = GearLoftBuilder.MakeBoreRing(bore, p.BoreSegments);

            // ── 断面列 ──
            float rotation = p.RotationOffsetDeg * Mathf.Deg2Rad;
            float phaseOffset = p.PhaseOffset * g.pair.axialPitch;

            float zMin = -0.5f * length;
            float zMax = +0.5f * length;

            // 角度ごとのねじの進み。Z が変わっても同じなので先に求めておく。
            var angles = new float[nt];
            var screwShift = new float[nt];

            for (int i = 0; i < nt; i++)
            {
                float theta = rotation + 2f * Mathf.PI * i / nt;

                angles[i] = theta;
                screwShift[i] = g.pair.hand * g.pair.lead * theta / (2f * Mathf.PI);
            }

            var sections = new List<GearLoftSection>(nz + 1);

            for (int s = 0; s <= nz; s++)
            {
                float z = Mathf.Lerp(zMin, zMax, s / (float)nz);

                var loop = new Vector2[nt];
                for (int i = 0; i < nt; i++)
                {
                    float r = ThreadRadius(g, z - screwShift[i] - phaseOffset);
                    loop[i] = GearDiskBuilder.Polar(r, angles[i]);
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
