// TrapezoidalThreadMeshGenerator.cs
// 台形ねじの雄ねじ軸と、それに対応する雌ねじナットを生成する。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【作り方】
//   断面を Z へ積む形（GearLoftBuilder）。各断面の半径を、
//   「軸方向位置 - らせんの進み」から求めた位相の台形プロファイルで振る。
//   上下の半ピッチは谷径へ寄せて不完全山にし、端面の欠けを抑える。
//
// 【生成経路】
//   パネルからは呼ばない。CreateTrapezoidalThreadCommand → PrimitiveMeshFactory がここを呼ぶ。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>作るのが雄ねじ軸か、かみ合うナットか。</summary>
    public enum TrapezoidalThreadPart { Screw, Nut }

    /// <summary>ねじナットの外形。</summary>
    public enum ThreadNutOuterType { Hex, Round, Square }

    public static class TrapezoidalThreadMeshGenerator
    {
        [Serializable]
        public struct Params : IEquatable<Params>
        {
            public const float SizeMin = 0.001f, SizeMax = 10f;
            public const float FlatMin = 0.02f, FlatMax = 0.8f;
            public const int StartsMin = 1, StartsMax = 8;
            public const int SegmentsMin = 12, SegmentsMax = 256;
            public const int SamplesMin = 4, SamplesMax = 64;
            public const int AxialDivisionsMax = 1024;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "TrapPart", Description = "雄ねじ軸かナットか")]
            public TrapezoidalThreadPart Part;
            [PLParam(TextKey = "TrapMajorDiameter", Description = "ねじ外径（ナットでは谷径）", Min = SizeMin, Max = SizeMax)]
            public float MajorDiameter;
            [PLParam(TextKey = "TrapMinorDiameter", Description = "ねじ谷径（ナットでは内径）", Min = SizeMin, Max = SizeMax)]
            public float MinorDiameter;
            [PLParam(TextKey = "TrapPitch", Description = "軸方向ピッチ", Min = SizeMin, Max = SizeMax)]
            public float Pitch;
            [PLParam(TextKey = "TrapStarts", Description = "条数", Min = StartsMin, Max = StartsMax, Step = 1)]
            public int Starts;
            [PLParam(TextKey = "TrapLength", Description = "軸またはナットの長さ", Min = SizeMin, Max = SizeMax)]
            public float Length;
            [PLParam(TextKey = "TrapCrestFlat", Description = "1 ピッチに対する山頂平坦部の比", Min = FlatMin, Max = FlatMax)]
            public float CrestFlatRatio;
            [PLParam(TextKey = "TrapRootFlat", Description = "1 ピッチに対する谷底平坦部の比", Min = FlatMin, Max = FlatMax)]
            public float RootFlatRatio;
            [PLParam(TextKey = "TrapRightHand", Description = "右ねじにする。外すと左ねじ")]
            public bool RightHand;
            [PLParam(TextKey = "TrapClearance", Description = "ナット側の半径方向すきま", Min = 0, Max = SizeMax)]
            public float Clearance;
            [PLParam(TextKey = "TrapNutOuterType", Description = "ナットの外形")]
            public ThreadNutOuterType NutOuterType;
            [PLParam(TextKey = "TrapNutOuterDiameter", Description = "ナットの外径。多角形では対角寸法", Min = SizeMin, Max = SizeMax)]
            public float NutOuterDiameter;
            [PLParam(TextKey = "TrapRadialSegments", Description = "円周方向の分割数",
                     Min = SegmentsMin, Max = SegmentsMax, Step = 1)]
            public int RadialSegments;
            [PLParam(TextKey = "TrapSamplesPerPitch", Description = "1 ピッチあたりの軸方向分割数",
                     Min = SamplesMin, Max = SamplesMax, Step = 1)]
            public int SamplesPerPitch;

            [PLParam(TextKey = "Orientation", Description = "軸を置く向き")]
            public PlaneOrientation Orientation;
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static Params Default => new Params
            {
                MeshName = "TrapezoidalThread",
                Part = TrapezoidalThreadPart.Screw,
                MajorDiameter = 0.4f,
                MinorDiameter = 0.3f,
                Pitch = 0.08f,
                Starts = 1,
                Length = 1.2f,
                CrestFlatRatio = 0.33f,
                RootFlatRatio = 0.33f,
                RightHand = true,
                Clearance = 0.005f,
                NutOuterType = ThreadNutOuterType.Hex,
                NutOuterDiameter = 0.7f,
                RadialSegments = 64,
                SamplesPerPitch = 16,
                Orientation = PlaneOrientation.XY,
                FlipFaces = false,
                Pivot = Vector3.zero,
            };

            public bool Equals(Params o) =>
                MeshName == o.MeshName && Part == o.Part &&
                Mathf.Approximately(MajorDiameter, o.MajorDiameter) &&
                Mathf.Approximately(MinorDiameter, o.MinorDiameter) &&
                Mathf.Approximately(Pitch, o.Pitch) && Starts == o.Starts &&
                Mathf.Approximately(Length, o.Length) &&
                Mathf.Approximately(CrestFlatRatio, o.CrestFlatRatio) &&
                Mathf.Approximately(RootFlatRatio, o.RootFlatRatio) &&
                RightHand == o.RightHand &&
                Mathf.Approximately(Clearance, o.Clearance) &&
                NutOuterType == o.NutOuterType &&
                Mathf.Approximately(NutOuterDiameter, o.NutOuterDiameter) &&
                RadialSegments == o.RadialSegments && SamplesPerPitch == o.SamplesPerPitch &&
                Orientation == o.Orientation && FlipFaces == o.FlipFaces && Pivot == o.Pivot;

            public override bool Equals(object obj) => obj is Params p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>派生諸元。</summary>
        public struct Info
        {
            public bool  Valid;
            public float Lead;
            public float Turns;
            public float IncludedAngleDeg;
            public bool  NutTooThin;
        }

        public static Info GetInfo(Params p)
        {
            bool ratios = p.CrestFlatRatio > 0f && p.RootFlatRatio > 0f
                       && p.CrestFlatRatio + p.RootFlatRatio < 0.95f;
            bool basic = p.MajorDiameter > p.MinorDiameter && p.MinorDiameter > 0f
                      && p.Pitch > 0f && p.Length > 0f && ratios;

            bool thin = p.Part == TrapezoidalThreadPart.Nut
                     && p.NutOuterDiameter <= p.MajorDiameter + 2f * Mathf.Max(0f, p.Clearance);

            float flankAxial = p.Pitch * (1f - p.CrestFlatRatio - p.RootFlatRatio) * 0.5f;
            float depth = (p.MajorDiameter - p.MinorDiameter) * 0.5f;
            float included = depth > 1e-8f ? 2f * Mathf.Atan(flankAxial / depth) * Mathf.Rad2Deg : 0f;

            return new Info
            {
                Valid            = basic && !thin,
                Lead             = p.Pitch * Mathf.Clamp(p.Starts, Params.StartsMin, Params.StartsMax),
                Turns            = p.Pitch > 0f ? p.Length / p.Pitch : 0f,
                IncludedAngleDeg = included,
                NutTooThin       = thin,
            };
        }

        public static MeshObject Generate(Params p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "TrapezoidalThread" : p.MeshName;
            if (!GetInfo(p).Valid) return new MeshObject(name);

            int n = Mathf.Clamp(p.RadialSegments, Params.SegmentsMin, Params.SegmentsMax);
            int spp = Mathf.Clamp(p.SamplesPerPitch, Params.SamplesMin, Params.SamplesMax);
            int axial = Mathf.Clamp(Mathf.CeilToInt(p.Length / p.Pitch * spp), 2, Params.AxialDivisionsMax);

            float major = p.MajorDiameter * 0.5f;
            float minor = p.MinorDiameter * 0.5f;
            float z0 = -p.Length * 0.5f;
            float hand = p.RightHand ? 1f : -1f;
            float lead = p.Pitch * Mathf.Clamp(p.Starts, Params.StartsMin, Params.StartsMax);
            float clearance = p.Part == TrapezoidalThreadPart.Nut ? Mathf.Max(0f, p.Clearance) : 0f;

            var sections = new List<GearLoftSection>(axial + 1);
            for (int s = 0; s <= axial; s++)
            {
                float local = p.Length * s / axial;
                float z = z0 + local;

                var thread = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    float a = 2f * Mathf.PI * i / n;
                    float phase = Mathf.Repeat(local - hand * lead * a / (2f * Mathf.PI), p.Pitch) / p.Pitch;
                    float r = Mathf.Lerp(minor, major, Profile(phase, p.CrestFlatRatio, p.RootFlatRatio)) + clearance;

                    float runout = Mathf.Clamp01(Mathf.Min(local, p.Length - local) / (p.Pitch * 0.5f));
                    r = Mathf.Lerp(minor + clearance, r, runout);
                    thread[i] = GearDiskBuilder.Polar(r, a);
                }

                sections.Add(p.Part == TrapezoidalThreadPart.Screw
                    ? new GearLoftSection(z, thread)
                    : new GearLoftSection(z, Outer(p.NutOuterType, p.NutOuterDiameter * 0.5f, n), thread));
            }

            return GearLoftBuilder.Build(name, sections, GearLoftCapMode.Triangulate,
                p.Orientation, p.FlipFaces, p.Pivot);
        }

        /// <summary>1 ピッチを 0..1 とした位相から、谷(0)〜山(1) の台形プロファイルを返す。</summary>
        private static float Profile(float phase, float crest, float root)
        {
            float x = Mathf.Abs(phase - 0.5f);
            float hc = crest * 0.5f;
            float hr = root  * 0.5f;
            if (x <= hc) return 1f;
            if (x >= 0.5f - hr) return 0f;
            return 1f - (x - hc) / (0.5f - hr - hc);
        }

        /// <summary>ナットの外形リング。多角形は外接半径から角度ごとの半径を出す。</summary>
        private static Vector2[] Outer(ThreadNutOuterType type, float r, int n)
        {
            var a = new Vector2[n];
            int sides = type == ThreadNutOuterType.Hex ? 6 : type == ThreadNutOuterType.Square ? 4 : 0;
            for (int i = 0; i < n; i++)
            {
                float q = 2f * Mathf.PI * i / n;
                float rr = r;
                if (sides > 0)
                {
                    float sec = 2f * Mathf.PI / sides;
                    float loc = Mathf.Repeat(q + sec * 0.5f, sec) - sec * 0.5f;
                    rr = r * Mathf.Cos(Mathf.PI / sides) / Mathf.Cos(loc);
                }
                a[i] = GearDiskBuilder.Polar(rr, q);
            }
            return a;
        }
    }
}
