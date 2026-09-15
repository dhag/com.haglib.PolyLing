// NutMeshGenerator.cs
// 六角・四角・丸ナットと、その内周を走る雌ねじを生成する。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【作り方】
//   断面を Z 方向へ積む形（GearLoftBuilder）で書ける。外周は多角形の外接半径、
//   穴は 1 ピッチを 1 周期とする台形の山で半径を振る。上下の半ピッチは
//   山を谷径へ寄せて不完全山にし、開口縁の欠けを抑える。
//
// 【生成経路】
//   パネルからは呼ばない。CreateNutCommand → PrimitiveMeshFactory がここを呼ぶ。
//   ローカル空間でピボット適用まで。平行移動・回転・拡大は Factory が行う。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>ナットの外形。</summary>
    public enum NutOuterType { Hex, Square, Round }

    public static class NutMeshGenerator
    {
        [Serializable]
        public struct NutParams : IEquatable<NutParams>
        {
            public const float DiameterMin = 0.001f, DiameterMax = 5f;
            public const float PitchMin = 0.0001f, PitchMax = 2f;
            public const float HeightMin = 0.001f, HeightMax = 5f;
            public const float ChamferMin = 0f, ChamferMax = 1f;
            public const int RadialSegmentsMin = 12, RadialSegmentsMax = 256;
            public const int SamplesPerPitchMin = 4, SamplesPerPitchMax = 64;
            public const int AxialDivisionsMax = 1024;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "NutOuterType", Description = "ナットの外形")]
            public NutOuterType OuterType;
            [PLParam(TextKey = "NutOuterDiameter", Description = "外形の最大径。六角・四角では対角寸法",
                     Min = DiameterMin, Max = DiameterMax)]
            public float OuterDiameter;
            [PLParam(TextKey = "NutHeight", Description = "ナットの厚さ", Min = HeightMin, Max = HeightMax)]
            public float Height;
            [PLParam(TextKey = "NutChamfer", Description = "上下外周の面取り幅", Min = ChamferMin, Max = ChamferMax)]
            public float Chamfer;
            [PLParam(TextKey = "NutThreadMajorDiameter", Description = "雌ねじの谷径。対応する雄ねじの外径",
                     Min = DiameterMin, Max = DiameterMax)]
            public float ThreadMajorDiameter;
            [PLParam(TextKey = "NutThreadMinorDiameter", Description = "雌ねじの内径（山径）",
                     Min = DiameterMin, Max = DiameterMax)]
            public float ThreadMinorDiameter;
            [PLParam(TextKey = "NutPitch", Description = "雌ねじのピッチ", Min = PitchMin, Max = PitchMax)]
            public float Pitch;
            [PLParam(TextKey = "NutRightHand", Description = "右ねじにする。外すと左ねじ")]
            public bool RightHand;
            [PLParam(TextKey = "NutRadialSegments", Description = "円周方向の分割数",
                     Min = RadialSegmentsMin, Max = RadialSegmentsMax, Step = 1)]
            public int RadialSegments;
            [PLParam(TextKey = "NutSamplesPerPitch", Description = "1 ピッチあたりの軸方向分割数",
                     Min = SamplesPerPitchMin, Max = SamplesPerPitchMax, Step = 1)]
            public int SamplesPerPitch;
            [PLParam(TextKey = "Orientation", Description = "軸を置く向き")]
            public PlaneOrientation Orientation;
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static NutParams Default => new NutParams
            {
                MeshName = "Nut",
                OuterType = NutOuterType.Hex,
                OuterDiameter = 0.55f,
                Height = 0.24f,
                Chamfer = 0.03f,
                ThreadMajorDiameter = 0.31f,
                ThreadMinorDiameter = 0.245f,
                Pitch = 0.05f,
                RightHand = true,
                RadialSegments = 64,
                SamplesPerPitch = 12,
                Orientation = PlaneOrientation.XY,
                FlipFaces = false,
                Pivot = Vector3.zero,
            };

            public bool Equals(NutParams o) =>
                MeshName == o.MeshName && OuterType == o.OuterType &&
                Mathf.Approximately(OuterDiameter, o.OuterDiameter) &&
                Mathf.Approximately(Height, o.Height) &&
                Mathf.Approximately(Chamfer, o.Chamfer) &&
                Mathf.Approximately(ThreadMajorDiameter, o.ThreadMajorDiameter) &&
                Mathf.Approximately(ThreadMinorDiameter, o.ThreadMinorDiameter) &&
                Mathf.Approximately(Pitch, o.Pitch) && RightHand == o.RightHand &&
                RadialSegments == o.RadialSegments && SamplesPerPitch == o.SamplesPerPitch &&
                Orientation == o.Orientation && FlipFaces == o.FlipFaces && Pivot == o.Pivot;

            public override bool Equals(object obj) => obj is NutParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>派生諸元。</summary>
        public struct NutInfo
        {
            public bool  Valid;
            public float ThreadTurns;
            public bool  ChamferClamped;
        }

        public static NutInfo GetInfo(NutParams p)
        {
            float outerR = p.OuterDiameter * 0.5f;
            float majorR = p.ThreadMajorDiameter * 0.5f;

            bool valid = p.Height > 0f && p.Pitch > 0f && p.ThreadMinorDiameter > 0f &&
                         p.ThreadMinorDiameter < p.ThreadMajorDiameter && majorR < outerR;

            float maxChamfer = Mathf.Min(p.Height * 0.45f, Mathf.Max(0f, outerR - majorR) * 0.45f);
            float chamfer = Mathf.Clamp(p.Chamfer, 0f, maxChamfer);

            return new NutInfo
            {
                Valid          = valid,
                ThreadTurns    = p.Pitch > 0f ? Mathf.Max(0f, p.Height) / p.Pitch : 0f,
                ChamferClamped = !Mathf.Approximately(chamfer, p.Chamfer),
            };
        }

        public static MeshObject Generate(NutParams p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "Nut" : p.MeshName;
            if (!GetInfo(p).Valid) return new MeshObject(name);

            int n   = Mathf.Clamp(p.RadialSegments,  NutParams.RadialSegmentsMin,  NutParams.RadialSegmentsMax);
            int spp = Mathf.Clamp(p.SamplesPerPitch, NutParams.SamplesPerPitchMin, NutParams.SamplesPerPitchMax);
            int axial = Mathf.Clamp(Mathf.CeilToInt(p.Height / p.Pitch * spp), 2, NutParams.AxialDivisionsMax);

            float outerR = p.OuterDiameter * 0.5f;
            float majorR = p.ThreadMajorDiameter * 0.5f;
            float minorR = p.ThreadMinorDiameter * 0.5f;
            float maxChamfer = Mathf.Min(p.Height * 0.45f, (outerR - majorR) * 0.45f);
            float chamfer = Mathf.Clamp(p.Chamfer, 0f, maxChamfer);
            float zMin = -p.Height * 0.5f;

            var sections = new List<GearLoftSection>(axial + 1);
            for (int s = 0; s <= axial; s++)
            {
                float localZ = p.Height * s / axial;
                float z = zMin + localZ;

                float edgeDistance = Mathf.Min(localZ, p.Height - localZ);
                float edgeInset = chamfer > 1e-8f ? Mathf.Max(0f, chamfer - edgeDistance) : 0f;
                float sectionOuterR = outerR - edgeInset;

                var outer = new Vector2[n];
                var hole  = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    float theta = 2f * Mathf.PI * i / n;
                    outer[i] = GearDiskBuilder.Polar(OuterRadius(p.OuterType, sectionOuterR, theta), theta);

                    float travel = (p.RightHand ? 1f : -1f) * p.Pitch * theta / (2f * Mathf.PI);
                    float phase  = Mathf.Repeat(localZ - travel, p.Pitch) / p.Pitch;
                    float holeR  = Mathf.Lerp(minorR, majorR, ThreadProfile(phase));

                    // 上下面の半ピッチで不完全山を作り、開口縁の欠けを抑える。
                    float runout = Mathf.Clamp01(edgeDistance / (p.Pitch * 0.5f));
                    holeR = Mathf.Lerp(minorR, holeR, runout);
                    hole[i] = GearDiskBuilder.Polar(holeR, theta);
                }
                sections.Add(new GearLoftSection(z, outer, hole));
            }

            return GearLoftBuilder.Build(name, sections, GearLoftCapMode.Triangulate,
                p.Orientation, p.FlipFaces, p.Pivot);
        }

        /// <summary>多角形（正 n 角形）の外接半径から、その角度での半径を返す。Round は素の半径。</summary>
        private static float OuterRadius(NutOuterType type, float cornerRadius, float angle)
        {
            int sides = type == NutOuterType.Hex ? 6 : type == NutOuterType.Square ? 4 : 0;
            if (sides == 0) return cornerRadius;

            float sector = 2f * Mathf.PI / sides;
            float local = Mathf.Repeat(angle + sector * 0.5f, sector) - sector * 0.5f;
            return cornerRadius * Mathf.Cos(Mathf.PI / sides) / Mathf.Cos(local);
        }

        /// <summary>1 ピッチを 0..1 とした位相から、谷(0)〜山(1) の台形プロファイルを返す。</summary>
        private static float ThreadProfile(float phase01)
        {
            float x = Mathf.Abs(phase01 - 0.5f);
            if (x <= 0.125f) return 1f;
            if (x >= 0.375f) return 0f;
            return 1f - (x - 0.125f) / 0.25f;
        }
    }
}
