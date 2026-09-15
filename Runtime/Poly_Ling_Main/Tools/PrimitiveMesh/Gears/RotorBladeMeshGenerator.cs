// RotorBladeMeshGenerator.cs
// プロペラ・ファン（軸流ファン / 航空プロペラ / 舶用スクリュー / ダクテッドファン）。
// ハブ＋翼＋（任意で）ダクトで組む。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【作り方】
//   ハブとダクトは「閉じた断面を Z へ積む」形（GearLoftBuilder）。
//   翼は翼弦方向 × 翼幅方向の格子を上下 2 面ぶん張り、前後縁と根元・先端を閉じる。
//   翼型は NACA 4 桁系の厚み分布に、単純な円弧キャンバを足したもの。
//
// 【生成経路】
//   パネルからは呼ばない。CreateRotorBladeCommand → PrimitiveMeshFactory がここを呼ぶ。
//   ローカル空間でピボット適用まで。平行移動・回転・拡大は Factory が行う。

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>翼の用途。選ぶと諸元がその用途の既定値へ入れ替わる。</summary>
    public enum RotorBladeType { AxialFan, AircraftPropeller, MarineScrew, DuctedJetFan }

    public static class RotorBladeMeshGenerator
    {
        [Serializable]
        public struct Params : IEquatable<Params>
        {
            public const float SizeMin = 0.001f, SizeMax = 20f;
            public const float PartMin = 0.001f, PartMax = 10f;
            public const int BladeCountMin = 2, BladeCountMax = 32;
            public const int SpanSegmentsMin = 2, SpanSegmentsMax = 32;
            public const int ChordSegmentsMin = 4, ChordSegmentsMax = 32;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "RotorBladeType", Description = "翼の用途")]
            public RotorBladeType Type;
            [PLParam(TextKey = "RotorBladeCount", Description = "翼の枚数",
                     Min = BladeCountMin, Max = BladeCountMax, Step = 1)]
            public int BladeCount;

            [PLParam(TextKey = "RotorDiameter", Description = "翼を含む外径", Min = SizeMin, Max = SizeMax)]
            public float RotorDiameter;
            [PLParam(TextKey = "RotorHubDiameter", Description = "ハブの外径", Min = PartMin, Max = PartMax)]
            public float HubDiameter;
            [PLParam(TextKey = "RotorHubLength", Description = "ハブの長さ", Min = PartMin, Max = PartMax)]
            public float HubLength;
            [PLParam(TextKey = "RotorShaftBore", Description = "軸穴の直径。0 で穴なし", Min = 0, Max = PartMax)]
            public float ShaftBore;

            [PLParam(TextKey = "RotorRootChord", Description = "翼根の翼弦長", Min = PartMin, Max = PartMax)]
            public float RootChord;
            [PLParam(TextKey = "RotorTipChord", Description = "翼端の翼弦長", Min = PartMin, Max = PartMax)]
            public float TipChord;
            [PLParam(TextKey = "RotorRootPitch", Description = "翼根のピッチ角", Min = -80, Max = 80)]
            public float RootPitchDeg;
            [PLParam(TextKey = "RotorTipPitch", Description = "翼端のピッチ角", Min = -80, Max = 80)]
            public float TipPitchDeg;
            [PLParam(TextKey = "RotorThickness", Description = "翼厚比", Min = 0.01, Max = 0.4)]
            public float ThicknessRatio;
            [PLParam(TextKey = "RotorCamber", Description = "キャンバ比", Min = 0, Max = 0.3)]
            public float CamberRatio;
            [PLParam(TextKey = "RotorSkew", Description = "スキュー角（翼端の回転方向への倒し）", Min = -70, Max = 70)]
            public float SkewDeg;
            [PLParam(TextKey = "RotorRake", Description = "レーキ角（軸方向への倒し）", Min = -45, Max = 45)]
            public float RakeDeg;

            [PLParam(TextKey = "RotorSpanSegments", Description = "翼幅方向の分割数",
                     Min = SpanSegmentsMin, Max = SpanSegmentsMax, Step = 1)]
            public int SpanSegments;
            [PLParam(TextKey = "RotorChordSegments", Description = "翼弦方向の分割数",
                     Min = ChordSegmentsMin, Max = ChordSegmentsMax, Step = 1)]
            public int ChordSegments;

            [PLParam(TextKey = "RotorShowDuct", Description = "外周のダクトを作る")]
            public bool ShowDuct;
            [PLParam(TextKey = "RotorDuctThickness", Description = "ダクトの肉厚", Min = PartMin, Max = PartMax)]
            public float DuctThickness;
            [PLParam(TextKey = "RotorDuctLength", Description = "ダクトの長さ", Min = PartMin, Max = PartMax)]
            public float DuctLength;

            [PLParam(TextKey = "Orientation", Description = "回転軸を置く向き")]
            public PlaneOrientation Orientation;
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static Params Default => Preset(RotorBladeType.AxialFan);

            /// <summary>用途ごとの既定値。翼の枚数・弦長・ピッチ・スキューが大きく違う。</summary>
            public static Params Preset(RotorBladeType t)
            {
                var p = new Params
                {
                    MeshName = "RotorBlade",
                    Type = t,
                    BladeCount = 7,
                    RotorDiameter = 1.6f,
                    HubDiameter = 0.48f,
                    HubLength = 0.34f,
                    ShaftBore = 0.16f,
                    RootChord = 0.42f,
                    TipChord = 0.24f,
                    RootPitchDeg = 38f,
                    TipPitchDeg = 18f,
                    ThicknessRatio = 0.12f,
                    CamberRatio = 0.035f,
                    SkewDeg = 18f,
                    RakeDeg = 2f,
                    SpanSegments = 10,
                    ChordSegments = 12,
                    ShowDuct = false,
                    DuctThickness = 0.06f,
                    DuctLength = 0.42f,
                    Orientation = PlaneOrientation.XY,
                    FlipFaces = false,
                    Pivot = Vector3.zero,
                };

                if (t == RotorBladeType.AircraftPropeller)
                {
                    p.BladeCount = 3; p.RootChord = 0.3f; p.TipChord = 0.16f;
                    p.RootPitchDeg = 48f; p.TipPitchDeg = 17f; p.SkewDeg = 6f; p.RakeDeg = 3f;
                }
                else if (t == RotorBladeType.MarineScrew)
                {
                    p.BladeCount = 5; p.RootChord = 0.5f; p.TipChord = 0.3f;
                    p.RootPitchDeg = 45f; p.TipPitchDeg = 24f;
                    p.ThicknessRatio = 0.16f; p.CamberRatio = 0.06f;
                    p.SkewDeg = 34f; p.RakeDeg = 12f;
                }
                else if (t == RotorBladeType.DuctedJetFan)
                {
                    p.BladeCount = 12; p.RootChord = 0.28f; p.TipChord = 0.2f;
                    p.RootPitchDeg = 42f; p.TipPitchDeg = 28f;
                    p.ThicknessRatio = 0.09f; p.CamberRatio = 0.025f;
                    p.SkewDeg = 12f; p.ShowDuct = true;
                }
                return p;
            }

            public bool Equals(Params o) =>
                MeshName == o.MeshName && Type == o.Type && BladeCount == o.BladeCount &&
                Mathf.Approximately(RotorDiameter, o.RotorDiameter) &&
                Mathf.Approximately(HubDiameter, o.HubDiameter) &&
                Mathf.Approximately(HubLength, o.HubLength) &&
                Mathf.Approximately(ShaftBore, o.ShaftBore) &&
                Mathf.Approximately(RootChord, o.RootChord) &&
                Mathf.Approximately(TipChord, o.TipChord) &&
                Mathf.Approximately(RootPitchDeg, o.RootPitchDeg) &&
                Mathf.Approximately(TipPitchDeg, o.TipPitchDeg) &&
                Mathf.Approximately(ThicknessRatio, o.ThicknessRatio) &&
                Mathf.Approximately(CamberRatio, o.CamberRatio) &&
                Mathf.Approximately(SkewDeg, o.SkewDeg) &&
                Mathf.Approximately(RakeDeg, o.RakeDeg) &&
                SpanSegments == o.SpanSegments && ChordSegments == o.ChordSegments &&
                ShowDuct == o.ShowDuct &&
                Mathf.Approximately(DuctThickness, o.DuctThickness) &&
                Mathf.Approximately(DuctLength, o.DuctLength) &&
                Orientation == o.Orientation && FlipFaces == o.FlipFaces && Pivot == o.Pivot;

            public override bool Equals(object x) => x is Params p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>派生諸元。</summary>
        public struct Info
        {
            public bool  Valid;
            public float TipClearance;
            public float Solidity;
        }

        public static Info GetInfo(Params p)
        {
            float rt = p.RotorDiameter * 0.5f;
            float rh = p.HubDiameter   * 0.5f;

            bool valid = p.BladeCount >= Params.BladeCountMin
                      && rt > rh
                      && p.HubDiameter > p.ShaftBore
                      && p.HubLength > 0f
                      && p.RootChord > 0f && p.TipChord > 0f
                      && p.ThicknessRatio > 0f
                      && p.SpanSegments  >= Params.SpanSegmentsMin
                      && p.ChordSegments >= Params.ChordSegmentsMin
                      && (!p.ShowDuct || (p.DuctThickness > 0f && p.DuctLength > 0f));

            return new Info
            {
                Valid        = valid,
                TipClearance = p.ShowDuct ? p.DuctThickness * 0.35f : 0f,
                Solidity     = p.BladeCount * (p.RootChord + p.TipChord) * 0.5f
                             / (Mathf.PI * (rt + rh)),
            };
        }

        public static MeshObject Generate(Params p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "RotorBlade" : p.MeshName;
            if (!GetInfo(p).Valid) return new MeshObject(name);

            var m = new MeshObject(name);
            int radial = Mathf.Clamp(p.ChordSegments * 2, 16, 96);

            Append(m, Ring("Hub", p.HubDiameter * 0.5f, p.ShaftBore * 0.5f, p.HubLength, radial, 0f));

            int blades = Mathf.Clamp(p.BladeCount, Params.BladeCountMin, Params.BladeCountMax);
            for (int b = 0; b < blades; b++)
                AddBlade(m, p, 2f * Mathf.PI * b / blades);

            if (p.ShowDuct || p.Type == RotorBladeType.DuctedJetFan)
            {
                float ri = p.RotorDiameter * 0.5f + p.DuctThickness * 0.35f;
                Append(m, Ring("Duct", ri + p.DuctThickness, ri, p.DuctLength, radial, 0f));
            }

            GearDiskBuilder.ApplyOrientation(m, p.Orientation);
            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(m);
            PrimitiveMeshPostProcess.ApplyPivotOffset(m, p.Pivot);
            PrimitiveMeshPostProcess.SortVerticesCanonical(m);
            m.InvalidatePositionCache();
            return m;
        }

        /// <summary>方位角 az に翼を 1 枚張る。</summary>
        private static void AddBlade(MeshObject m, Params p, float az)
        {
            int ns = Mathf.Clamp(p.SpanSegments,  Params.SpanSegmentsMin,  Params.SpanSegmentsMax);
            int nc = Mathf.Clamp(p.ChordSegments, Params.ChordSegmentsMin, Params.ChordSegmentsMax);
            int cols = nc + 1;
            int start = m.VertexCount;

            Vector3 axis   = Vector3.forward;
            Vector3 radial = new Vector3(Mathf.Cos(az),  Mathf.Sin(az), 0f);
            Vector3 tan    = new Vector3(-Mathf.Sin(az), Mathf.Cos(az), 0f);

            float r0 = p.HubDiameter   * 0.47f;
            float r1 = p.RotorDiameter * 0.5f;

            // side 0 = 正圧面側、side 1 = 負圧面側。同じ格子を 2 枚張る。
            for (int side = 0; side < 2; side++)
            {
                for (int j = 0; j <= ns; j++)
                {
                    float t = (float)j / ns;
                    float s = t * t * (3f - 2f * t);            // 根元・先端をなだらかにする
                    float r = Mathf.Lerp(r0, r1, t);
                    float chord = Mathf.Lerp(p.RootChord, p.TipChord, s);
                    float pitch = Mathf.Lerp(p.RootPitchDeg, p.TipPitchDeg, s) * Mathf.Deg2Rad;

                    Vector3 chordDir = (tan * Mathf.Cos(pitch) + axis * Mathf.Sin(pitch)).normalized;
                    Vector3 normal   = Vector3.Cross(radial, chordDir).normalized;

                    float skew = Mathf.Tan(p.SkewDeg * Mathf.Deg2Rad) * r1 * t * t * 0.42f;
                    float rake = Mathf.Tan(p.RakeDeg * Mathf.Deg2Rad) * (r - r0);
                    Vector3 center = radial * r + tan * skew + axis * rake;

                    for (int i = 0; i <= nc; i++)
                    {
                        float x = (float)i / nc;
                        float yt = 5f * p.ThicknessRatio *
                                   (0.2969f * Mathf.Sqrt(x) - 0.126f * x - 0.3516f * x * x
                                    + 0.2843f * x * x * x - 0.1015f * x * x * x * x);
                        float camber = 4f * p.CamberRatio * x * (1f - x);
                        float y = (camber + (side == 0 ? yt : -yt)) * chord;

                        Vector3 pos = center + chordDir * ((x - 0.28f) * chord) + normal * y;
                        m.Vertices.Add(new Vertex(pos, new Vector2(t, x), side == 0 ? normal : -normal));
                    }
                }
            }

            int sheet = (ns + 1) * cols;

            // 上下 2 面
            for (int side = 0; side < 2; side++)
                for (int j = 0; j < ns; j++)
                    for (int i = 0; i < nc; i++)
                    {
                        int q = start + side * sheet + j * cols + i;
                        if (side == 0) m.AddQuad(q, q + cols, q + cols + 1, q + 1);
                        else           m.AddQuad(q, q + 1, q + cols + 1, q + cols);
                    }

            // 前縁・後縁
            for (int j = 0; j < ns; j++)
            {
                int a = start + j * cols;
                int b = start + (j + 1) * cols;
                int c = start + sheet + j * cols;
                int d = start + sheet + (j + 1) * cols;
                m.AddQuad(a, c, d, b);
                a += nc; b += nc; c += nc; d += nc;
                m.AddQuad(a, b, d, c);
            }

            // 根元・先端
            for (int end = 0; end < 2; end++)
            {
                int row = end == 0 ? 0 : ns * cols;
                for (int i = 0; i < nc; i++)
                {
                    int a = start + row + i;
                    int b = a + 1;
                    int c = start + sheet + row + i;
                    int d = c + 1;
                    if (end == 0) m.AddQuad(a, b, d, c);
                    else          m.AddQuad(a, c, d, b);
                }
            }
        }

        private static Vector2[] Circle(float r, int n)
        {
            var a = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float t = 2f * Mathf.PI * i / n;
                a[i] = new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r;
            }
            return a;
        }

        /// <summary>外径 ro、内径 ri（0 なら穴なし）、厚み d のリング。</summary>
        private static MeshObject Ring(string name, float ro, float ri, float d, int n, float z)
            => GearLoftBuilder.Build(name,
                new[]
                {
                    new GearLoftSection(z - d * 0.5f, Circle(ro, n), ri > 0f ? Circle(ri, n) : null),
                    new GearLoftSection(z + d * 0.5f, Circle(ro, n), ri > 0f ? Circle(ri, n) : null),
                },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);

        private static void Append(MeshObject a, MeshObject b) => MeshObjectAppendOps.Append(a, b, true);
    }
}
