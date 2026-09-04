// HelicalRackMeshGenerator.cs
// はすばラックのメッシュ生成。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【入力は法線系】
//   mn  … 法線モジュール
//   αn  … 法線圧力角
//   β   … ねじれ角
//
// 【正面系への変換】
//   mt      = mn / cos(β)
//   tan(αt) = tan(αn) / cos(β)
//   正面ピッチ pt = π·mt
//   高さ方向の歯たけは法線モジュール基準（mn·ha* / mn·hf*）
//
// 【ねじれの向きの決め方】
//   ここで持つのは「ラック自身の」ねじれ角。歯すじの中心線は
//
//       x(z) = x0 + hand · z · tan(β)      hand = ねじれ角の符号
//
//   に沿う。つまり ねじれ角が正なら、Z が増えるほど歯が +X 側へ寄る。
//   かみ合う相手の歯車は逆手になる。相手の手に合わせて符号を選ぶこと。
//
// 【断面】
//   Z ごとに上面の位相がずれるので、平ラックのように折れ点だけでは足りない。
//   X を等間隔に刻んで上面をなぞる。刻みが粗いと歯先・歯元の角が丸まる。
//
// 【全長】
//   ちょうど 歯数 × 正面ピッチ。歯数は Z の中央での本数。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class HelicalRackMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================

        [System.Serializable]
        public struct HelicalRackParams : System.IEquatable<HelicalRackParams>
        {
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>歯数の下限・上限</summary>
            public const int ToothCountMin = 1;
            public const int ToothCountMax = 200;

            /// <summary>法線モジュールの下限・上限</summary>
            public const float ModuleMin = 0.01f;
            public const float ModuleMax = 1f;

            /// <summary>法線圧力角の下限・上限（度）</summary>
            public const float PressureAngleMin = 10f;
            public const float PressureAngleMax = 35f;

            /// <summary>
            /// ねじれ角の下限・上限（度）。正で Z が増えるほど歯が +X 側へ寄る。
            /// 60° 以上は正面圧力角が立ちすぎて歯形が成立しないので手前で止める。
            /// </summary>
            public const float HelixAngleMin = -59f;
            public const float HelixAngleMax =  59f;

            /// <summary>歯幅（Z 方向）の下限・上限</summary>
            public const float FaceWidthMin = 0f;
            public const float FaceWidthMax = 3f;

            /// <summary>歯底から本体の底までの肉厚の下限・上限</summary>
            public const float BodyHeightMin = 0.01f;
            public const float BodyHeightMax = 5f;

            /// <summary>歯末のたけ係数・歯元のたけ係数の下限・上限</summary>
            public const float ToothDepthCoefMin = 0.1f;
            public const float ToothDepthCoefMax = 2f;

            /// <summary>バックラッシの下限・上限</summary>
            public const float BacklashMin = 0f;
            public const float BacklashMax = 0.2f;

            /// <summary>1 ピッチあたりの X 方向標本数の下限・上限</summary>
            public const int SamplesPerPitchMin = 4;
            public const int SamplesPerPitchMax = 64;

            /// <summary>
            /// X 方向の総標本数の上限。歯数 × 1 ピッチあたりの標本数がこれを超えたら抑える。
            /// 歯幅方向の分割と掛け算になるので、頭を押さえないと頂点数が跳ね上がる。
            /// </summary>
            public const int TotalSamplesMax = 2048;

            /// <summary>歯幅方向の分割数の下限・上限</summary>
            public const int FaceSegmentsMin = 1;
            public const int FaceSegmentsMax = 64;

            /// <summary>歯の位相ずらしの下限・上限（ピッチ単位）</summary>
            public const float PhaseOffsetMin = -1f;
            public const float PhaseOffsetMax =  1f;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;

            // ── 基本諸元（法線系） ──
            /// <summary>Z 中央での歯数</summary>
            [PLParam(TextKey = "RackToothCount", Description = "歯数。全長は 歯数 × 正面ピッチ になる",
                     Min = ToothCountMin, Max = ToothCountMax, Step = 1)]
            public int ToothCount;
            /// <summary>法線モジュール mn</summary>
            [PLParam(TextKey = "HelNormalModule", Description = "法線モジュール（歯直角で見た歯の大きさ）",
                     Min = ModuleMin, Max = ModuleMax)]
            public float NormalModule;
            /// <summary>法線圧力角 αn（度）</summary>
            [PLParam(TextKey = "HelNormalPressureAngle", Description = "法線圧力角（度）",
                     Min = PressureAngleMin, Max = PressureAngleMax)]
            public float NormalPressureAngleDeg;
            /// <summary>ねじれ角 β（度）。正で Z が増えるほど歯が +X 側へ寄る。</summary>
            [PLParam(TextKey = "HelHelixAngle", Description = "ねじれ角（度）。正で Z が増えるほど歯が +X 側へ寄る",
                     Min = HelixAngleMin, Max = HelixAngleMax)]
            public float HelixAngleDeg;
            /// <summary>歯幅（Z 方向）</summary>
            [PLParam(TextKey = "RackFaceWidth", Description = "歯幅。0 で板", Min = FaceWidthMin, Max = FaceWidthMax)]
            public float FaceWidth;
            /// <summary>歯底から本体の底までの肉厚</summary>
            [PLParam(TextKey = "RackBodyHeight", Description = "歯底から本体の底までの肉厚",
                     Min = BodyHeightMin, Max = BodyHeightMax)]
            public float BodyHeight;

            // ── 歯たけ ──
            /// <summary>歯末のたけ係数 ha*</summary>
            [PLParam(TextKey = "GearAddendumCoef", Description = "歯末のたけ係数。ピッチ線から歯先までの高さ ÷ 法線モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float AddendumCoef;
            /// <summary>歯元のたけ係数 hf*</summary>
            [PLParam(TextKey = "GearDedendumCoef", Description = "歯元のたけ係数。ピッチ線から歯底までの深さ ÷ 法線モジュール",
                     Min = ToothDepthCoefMin, Max = ToothDepthCoefMax)]
            public float DedendumCoef;

            /// <summary>正面ピッチ線上のバックラッシ</summary>
            [PLParam(TextKey = "HelTransverseBacklash", Description = "正面バックラッシ",
                     Min = BacklashMin, Max = BacklashMax)]
            public float Backlash;

            // ── 標本数 ──
            /// <summary>1 ピッチあたりの X 方向標本数</summary>
            [PLParam(TextKey = "RackSamplesPerPitch", Description = "1 ピッチあたりの長さ方向の標本数",
                     Min = SamplesPerPitchMin, Max = SamplesPerPitchMax, Step = 1)]
            public int SamplesPerPitch;
            /// <summary>歯幅方向の分割数</summary>
            [PLParam(TextKey = "HelAxialSegments", Description = "歯幅方向の分割数",
                     Min = FaceSegmentsMin, Max = FaceSegmentsMax, Step = 1)]
            public int FaceSegments;

            // ── 配置 ──
            /// <summary>歯を本体に対してずらす量（ピッチ単位）</summary>
            [PLParam(TextKey = "RackPhaseOffset", Description = "歯を本体に対してずらす量（ピッチ単位）",
                     Min = PhaseOffsetMin, Max = PhaseOffsetMax)]
            public float PhaseOffset;

            /// <summary>断面を置く平面</summary>
            [PLParam(TextKey = "Orientation", Description = "板の向き（XY / XZ / YZ）")]
            public PlaneOrientation Orientation;
            /// <summary>生成後にメッシュ全体の面を反転する</summary>
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            /// <summary>AABB サイズ基準のピボット</summary>
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static HelicalRackParams Default => new HelicalRackParams
            {
                MeshName               = "HelicalRack",
                ToothCount             = 12,
                NormalModule           = 0.1f,
                NormalPressureAngleDeg = 20f,
                HelixAngleDeg          = 20f,
                FaceWidth              = 0.3f,
                BodyHeight             = 0.2f,
                AddendumCoef           = 1f,
                DedendumCoef           = 1.25f,
                Backlash               = 0f,
                SamplesPerPitch        = 16,
                FaceSegments           = 8,
                PhaseOffset            = 0f,
                Orientation            = PlaneOrientation.XY,
                FlipFaces              = false,
                Pivot                  = Vector3.zero,
            };

            public bool Equals(HelicalRackParams o) =>
                MeshName == o.MeshName &&
                ToothCount == o.ToothCount &&
                Mathf.Approximately(NormalModule,           o.NormalModule)           &&
                Mathf.Approximately(NormalPressureAngleDeg, o.NormalPressureAngleDeg) &&
                Mathf.Approximately(HelixAngleDeg,          o.HelixAngleDeg)          &&
                Mathf.Approximately(FaceWidth,              o.FaceWidth)              &&
                Mathf.Approximately(BodyHeight,             o.BodyHeight)             &&
                Mathf.Approximately(AddendumCoef,           o.AddendumCoef)           &&
                Mathf.Approximately(DedendumCoef,           o.DedendumCoef)           &&
                Mathf.Approximately(Backlash,               o.Backlash)               &&
                SamplesPerPitch == o.SamplesPerPitch &&
                FaceSegments    == o.FaceSegments    &&
                Mathf.Approximately(PhaseOffset, o.PhaseOffset) &&
                Orientation == o.Orientation &&
                FlipFaces   == o.FlipFaces   &&
                Pivot       == o.Pivot;

            public override bool Equals(object obj) => obj is HelicalRackParams p && Equals(p);
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
            /// <summary>ねじれ角の符号。+1 で Z が増えるほど歯が +X 側へ寄る。</summary>
            public float Hand;
            /// <summary>法線圧力角（ラジアン）</summary>
            public float AlphaN;
            /// <summary>正面圧力角（ラジアン）</summary>
            public float AlphaT;
            /// <summary>正面モジュール</summary>
            public float ModuleT;
        }

        private static bool TryGetTransverse(HelicalRackParams p, out Transverse t)
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

        private static bool TryGetRack(
            HelicalRackParams p, out RackToothSection.RackData g, out Transverse t)
        {
            g = default;

            if (!TryGetTransverse(p, out t)) return false;
            if (p.FaceWidth < 0f) return false;

            var input = new RackToothSection.RackInput
            {
                ToothCount              = p.ToothCount,
                TransverseModule        = t.ModuleT,
                RadialModule            = p.NormalModule,
                TransversePressureAngle = t.AlphaT,
                Backlash                = p.Backlash,
                AddendumCoef            = p.AddendumCoef,
                DedendumCoef            = p.DedendumCoef,
                BodyHeight              = p.BodyHeight,
            };

            return RackToothSection.TryGetRackData(input, out g);
        }

        // ================================================================
        // 派生諸元（UI 表示用）
        // ================================================================

        /// <summary>パネルに出す派生諸元。</summary>
        public struct HelicalRackInfo
        {
            /// <summary>諸元が成立しているか。false のとき他のフィールドは無効。</summary>
            public bool Valid;

            public float TransverseModule;
            public float TransversePressureAngleDeg;

            public float TransversePitch;
            /// <summary>歯直角で測ったピッチ pn = π·mn</summary>
            public float NormalPitch;
            public float Length;
            public float Addendum;
            public float Dedendum;
            public float TotalHeight;
            public float ToothThicknessPitchLine;
            public float TipWidth;
            public float RootWidth;

            /// <summary>歯幅ぶんで歯すじが X 方向にずれる量。</summary>
            public float ToothShiftAcrossFace;
            /// <summary>ずれが 1 ピッチを超えているか。標本数が足りないと歯先が崩れる。</summary>
            public bool ShiftExceedsPitch;
        }

        /// <summary>派生諸元を求める。パラメータが不正なら Valid=false を返す。</summary>
        public static HelicalRackInfo GetInfo(HelicalRackParams p)
        {
            var info = new HelicalRackInfo { Valid = false };

            if (!TryGetRack(p, out RackToothSection.RackData g, out Transverse t))
                return info;

            info.Valid                      = true;
            info.TransverseModule           = t.ModuleT;
            info.TransversePressureAngleDeg = t.AlphaT * Mathf.Rad2Deg;

            info.TransversePitch         = g.pitch;
            info.NormalPitch             = Mathf.PI * p.NormalModule;
            info.Length                  = g.length;
            info.Addendum                = g.addendum;
            info.Dedendum                = g.dedendum;
            info.TotalHeight             = g.tipY - g.bottomY;
            info.ToothThicknessPitchLine = 2f * g.pitchHalfThickness;
            info.TipWidth                = 2f * g.tipHalfThickness;
            info.RootWidth               = 2f * g.rootHalfThickness;

            info.ToothShiftAcrossFace = ToothShift(p, t);
            info.ShiftExceedsPitch    = Mathf.Abs(info.ToothShiftAcrossFace) > g.pitch;

            return info;
        }

        /// <summary>歯幅ぶんで歯すじが X 方向にずれる量。</summary>
        private static float ToothShift(HelicalRackParams p, Transverse t)
            => t.Hand * Mathf.Max(0f, p.FaceWidth) * Mathf.Tan(t.Beta);

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>
        /// はすばラックメッシュを作る。パラメータが不正なときは空メッシュを返す。
        /// </summary>
        public static MeshObject Generate(HelicalRackParams p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "HelicalRack" : p.MeshName;

            if (!TryGetRack(p, out RackToothSection.RackData g, out Transverse t))
                return new MeshObject(name);

            // X 方向の刻み数。歯数が多いと膨れるので総数で頭を押さえる。
            int perPitch = Mathf.Clamp(p.SamplesPerPitch,
                HelicalRackParams.SamplesPerPitchMin, HelicalRackParams.SamplesPerPitchMax);

            int nx = Mathf.Clamp(g.z * perPitch, 4, HelicalRackParams.TotalSamplesMax);

            float width = Mathf.Max(0f, p.FaceWidth);
            float phaseBase = p.PhaseOffset * g.pitch;

            int ns;
            if (width <= 1e-6f)
                ns = 1;                                                   // 歯幅 0 は板 1 枚
            else if (Mathf.Abs(t.Beta) <= 1e-6f)
                ns = 2;                                                   // ねじれ 0 は前後だけでよい
            else
                ns = Mathf.Clamp(p.FaceSegments,
                                 HelicalRackParams.FaceSegmentsMin,
                                 HelicalRackParams.FaceSegmentsMax) + 1;

            var sections = new List<GearLoftSection>(ns);

            float zMin = -0.5f * width;
            float zMax = +0.5f * width;
            float tanBeta = Mathf.Tan(t.Beta);

            for (int s = 0; s < ns; s++)
            {
                float u = ns > 1 ? s / (float)(ns - 1) : 0f;
                float z = Mathf.Lerp(zMin, zMax, u);

                // 歯すじは x(z) = x0 + hand·z·tan(β) をたどる。
                // 上面の位相はその逆符号だけずらす。
                float shift = phaseBase + t.Hand * z * tanBeta;

                Vector2[] top = RackToothSection.BuildSampledTopProfile(g, nx, shift);
                Vector2[] loop = RackToothSection.CloseSection(top, g.bottomY);

                if (loop == null || loop.Length < 3) return new MeshObject(name);

                sections.Add(new GearLoftSection(z, loop));
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
