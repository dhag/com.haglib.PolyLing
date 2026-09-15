// SplineMeshGenerator.cs
// 平行歯面スプラインの軸と、それに対応する内スプラインナットを生成する。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【作り方】
//   断面を Z へ積む形（GearLoftBuilder）。1 断面は、角度を歯数で折り返した
//   台形プロファイルで半径を振った歯形リング。端の面取りは、面取りぶんだけ
//   半径を縮めた断面をもう 1 枚足して表す。
//
// 【生成経路】
//   パネルからは呼ばない。CreateSplineCommand → PrimitiveMeshFactory がここを呼ぶ。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>作るのが軸か、かみ合うナットか。</summary>
    public enum SplinePart { Shaft, Nut }

    /// <summary>スプラインナットの外形。</summary>
    public enum SplineNutOuterType { Round, Hex, Square }

    public static class SplineMeshGenerator
    {
        [Serializable]
        public struct Params : IEquatable<Params>
        {
            public const float SizeMin = 0.001f, SizeMax = 10f;
            public const float FlatMin = 0.05f, FlatMax = 0.8f;
            public const int TeethMin = 3, TeethMax = 96;
            public const int SegmentsPerToothMin = 4, SegmentsPerToothMax = 24;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "SplinePart", Description = "軸かナットか")]
            public SplinePart Part;
            [PLParam(TextKey = "SplineTeeth", Description = "歯数", Min = TeethMin, Max = TeethMax, Step = 1)]
            public int Teeth;
            [PLParam(TextKey = "SplineMajorDiameter", Description = "歯先径", Min = SizeMin, Max = SizeMax)]
            public float MajorDiameter;
            [PLParam(TextKey = "SplineMinorDiameter", Description = "歯底径", Min = SizeMin, Max = SizeMax)]
            public float MinorDiameter;
            [PLParam(TextKey = "SplineLength", Description = "軸方向の長さ", Min = SizeMin, Max = SizeMax)]
            public float Length;
            [PLParam(TextKey = "SplineToothFlat", Description = "角ピッチに対する歯先平坦部の比", Min = FlatMin, Max = FlatMax)]
            public float ToothFlatRatio;
            [PLParam(TextKey = "SplineRootFlat", Description = "角ピッチに対する歯底平坦部の比", Min = FlatMin, Max = FlatMax)]
            public float RootFlatRatio;
            [PLParam(TextKey = "SplineClearance", Description = "ナット側の半径方向すきま", Min = 0, Max = SizeMax)]
            public float Clearance;
            [PLParam(TextKey = "SplineNutOuterType", Description = "ナットの外形")]
            public SplineNutOuterType NutOuterType;
            [PLParam(TextKey = "SplineNutOuterDiameter", Description = "ナットの外径。多角形では対角寸法",
                     Min = SizeMin, Max = SizeMax)]
            public float NutOuterDiameter;
            [PLParam(TextKey = "SplineChamfer", Description = "端の面取り", Min = 0, Max = SizeMax)]
            public float Chamfer;
            [PLParam(TextKey = "SplineSegmentsPerTooth", Description = "1 歯あたりの分割数",
                     Min = SegmentsPerToothMin, Max = SegmentsPerToothMax, Step = 1)]
            public int SegmentsPerTooth;

            [PLParam(TextKey = "Orientation", Description = "軸を置く向き")]
            public PlaneOrientation Orientation;
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static Params Default => new Params
            {
                MeshName = "Spline",
                Part = SplinePart.Shaft,
                Teeth = 10,
                MajorDiameter = 0.5f,
                MinorDiameter = 0.4f,
                Length = 1f,
                ToothFlatRatio = 0.35f,
                RootFlatRatio = 0.35f,
                Clearance = 0.005f,
                NutOuterType = SplineNutOuterType.Round,
                NutOuterDiameter = 0.75f,
                Chamfer = 0.03f,
                SegmentsPerTooth = 8,
                Orientation = PlaneOrientation.XY,
                FlipFaces = false,
                Pivot = Vector3.zero,
            };

            public bool Equals(Params o) =>
                MeshName == o.MeshName && Part == o.Part && Teeth == o.Teeth &&
                Mathf.Approximately(MajorDiameter, o.MajorDiameter) &&
                Mathf.Approximately(MinorDiameter, o.MinorDiameter) &&
                Mathf.Approximately(Length, o.Length) &&
                Mathf.Approximately(ToothFlatRatio, o.ToothFlatRatio) &&
                Mathf.Approximately(RootFlatRatio, o.RootFlatRatio) &&
                Mathf.Approximately(Clearance, o.Clearance) &&
                NutOuterType == o.NutOuterType &&
                Mathf.Approximately(NutOuterDiameter, o.NutOuterDiameter) &&
                Mathf.Approximately(Chamfer, o.Chamfer) &&
                SegmentsPerTooth == o.SegmentsPerTooth &&
                Orientation == o.Orientation && FlipFaces == o.FlipFaces && Pivot == o.Pivot;

            public override bool Equals(object obj) => obj is Params p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>派生諸元。</summary>
        public struct Info
        {
            public bool  Valid;
            public float AngularPitchDeg;
            public float ToothHeight;
            public bool  ChamferClamped;
        }

        public static Info GetInfo(Params p)
        {
            float major = p.MajorDiameter * 0.5f;
            float minor = p.MinorDiameter * 0.5f;

            bool ratios = p.ToothFlatRatio > 0f && p.RootFlatRatio > 0f
                       && p.ToothFlatRatio + p.RootFlatRatio < 0.95f;
            bool fit = p.Part == SplinePart.Shaft
                    || p.NutOuterDiameter * 0.5f > major + Mathf.Max(0f, p.Clearance);

            float maxChamfer = Mathf.Min(p.Length * 0.45f, Mathf.Max(0f, RadialLimit(p)) * 0.45f);

            return new Info
            {
                Valid           = major > minor && minor > 0f && p.Length > 0f && ratios && fit,
                AngularPitchDeg = 360f / Mathf.Max(1, p.Teeth),
                ToothHeight     = major - minor,
                ChamferClamped  = !Mathf.Approximately(Mathf.Clamp(p.Chamfer, 0f, maxChamfer), p.Chamfer),
            };
        }

        /// <summary>面取りに使える半径方向の余地。軸は歯たけ、ナットは外径までの肉厚。</summary>
        private static float RadialLimit(Params p)
        {
            float major = p.MajorDiameter * 0.5f;
            float minor = p.MinorDiameter * 0.5f;
            return p.Part == SplinePart.Shaft
                ? major - minor
                : p.NutOuterDiameter * 0.5f - major - Mathf.Max(0f, p.Clearance);
        }

        public static MeshObject Generate(Params p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "Spline" : p.MeshName;
            if (!GetInfo(p).Valid) return new MeshObject(name);

            int teeth = Mathf.Clamp(p.Teeth, Params.TeethMin, Params.TeethMax);
            int spt = Mathf.Clamp(p.SegmentsPerTooth, Params.SegmentsPerToothMin, Params.SegmentsPerToothMax);
            int n = teeth * spt;

            float major = p.MajorDiameter * 0.5f;
            float minor = p.MinorDiameter * 0.5f;
            float clearance = Mathf.Max(0f, p.Clearance);
            float chamfer = Mathf.Clamp(p.Chamfer, 0f,
                Mathf.Min(p.Length * 0.45f, Mathf.Max(0f, RadialLimit(p)) * 0.45f));

            float[] zs = chamfer > 1e-8f
                ? new[] { -p.Length * 0.5f, -p.Length * 0.5f + chamfer,
                           p.Length * 0.5f - chamfer, p.Length * 0.5f }
                : new[] { -p.Length * 0.5f, p.Length * 0.5f };

            var sections = new List<GearLoftSection>(zs.Length);
            for (int s = 0; s < zs.Length; s++)
            {
                float edge = (s == 0 || s == zs.Length - 1) ? chamfer : 0f;

                Vector2[] tooth = p.Part == SplinePart.Shaft
                    ? ToothRing(n, teeth, minor, major - edge, p.ToothFlatRatio, p.RootFlatRatio)
                    : ToothRing(n, teeth, minor + edge + clearance, major + edge + clearance,
                                p.ToothFlatRatio, p.RootFlatRatio);

                sections.Add(p.Part == SplinePart.Shaft
                    ? new GearLoftSection(zs[s], tooth)
                    : new GearLoftSection(zs[s],
                        Outer(p.NutOuterType, p.NutOuterDiameter * 0.5f - edge, n), tooth));
            }

            return GearLoftBuilder.Build(name, sections, GearLoftCapMode.Triangulate,
                p.Orientation, p.FlipFaces, p.Pivot);
        }

        /// <summary>角度を歯数で折り返した台形プロファイルで半径を振ったリング。</summary>
        private static Vector2[] ToothRing(int n, int teeth, float minor, float major, float top, float root)
        {
            var ring = new Vector2[n];
            float ht = top  * 0.5f;
            float hr = root * 0.5f;

            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                float phase = Mathf.Repeat(a * teeth / (2f * Mathf.PI), 1f);
                float x = Mathf.Abs(phase - 0.5f);

                float v;
                if (x <= ht) v = 1f;
                else if (x >= 0.5f - hr) v = 0f;
                else v = 1f - (x - ht) / (0.5f - hr - ht);

                ring[i] = GearDiskBuilder.Polar(Mathf.Lerp(minor, major, v), a);
            }
            return ring;
        }

        /// <summary>ナットの外形リング。多角形は外接半径から角度ごとの半径を出す。</summary>
        private static Vector2[] Outer(SplineNutOuterType type, float r, int n)
        {
            var ring = new Vector2[n];
            int sides = type == SplineNutOuterType.Hex ? 6 : type == SplineNutOuterType.Square ? 4 : 0;

            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                float rr = r;
                if (sides > 0)
                {
                    float sec = 2f * Mathf.PI / sides;
                    float loc = Mathf.Repeat(a + sec * 0.5f, sec) - sec * 0.5f;
                    rr = r * Mathf.Cos(Mathf.PI / sides) / Mathf.Cos(loc);
                }
                ring[i] = GearDiskBuilder.Polar(rr, a);
            }
            return ring;
        }
    }
}
