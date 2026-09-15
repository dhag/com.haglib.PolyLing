// ShaftHolderMeshGenerator.cs
// 軸受ホルダ（シャフトホルダ）。ボス＋フランジ＋取付穴＋締結部で組む。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【作り方】
//   部品ごとに「閉じた断面を Z へ積む」形（GearLoftBuilder）で作り、連結する。
//   取付穴・締結穴は、穴あきリングを差し込むことで表現する（真のブーリアンは行わない）。
//
// 【生成経路】
//   パネルからは呼ばない。CreateShaftHolderCommand → PrimitiveMeshFactory がここを呼ぶ。
//   ローカル空間でピボット適用まで。平行移動・回転・拡大は Factory が行う。

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>フランジの形。</summary>
    public enum ShaftHolderType { RoundFlange, TwoFlatFlange, SquareFlange, BaseMount }

    /// <summary>軸の留め方。</summary>
    public enum ShaftHolderClampType { SetScrew, SplitClamp }

    public static class ShaftHolderMeshGenerator
    {
        [Serializable]
        public struct Params : IEquatable<Params>
        {
            public const float SizeMin = 0.001f, SizeMax = 10f;
            public const int SegmentsMin = 12, SegmentsMax = 128;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "ShaftHolderType", Description = "フランジの形")]
            public ShaftHolderType Type;
            [PLParam(TextKey = "ShaftHolderClampType", Description = "軸の留め方")]
            public ShaftHolderClampType ClampType;

            [PLParam(TextKey = "ShaftHolderBore", Description = "軸穴の直径", Min = SizeMin, Max = SizeMax)]
            public float BoreDiameter;
            [PLParam(TextKey = "ShaftHolderBoss", Description = "ボスの外径", Min = SizeMin, Max = SizeMax)]
            public float BossDiameter;
            [PLParam(TextKey = "ShaftHolderBossLength", Description = "ボスの長さ", Min = SizeMin, Max = SizeMax)]
            public float BossLength;

            [PLParam(TextKey = "ShaftHolderFlangeSize", Description = "フランジの差し渡し", Min = SizeMin, Max = SizeMax)]
            public float FlangeSize;
            [PLParam(TextKey = "ShaftHolderFlangeThickness", Description = "フランジの厚さ", Min = SizeMin, Max = SizeMax)]
            public float FlangeThickness;
            [PLParam(TextKey = "ShaftHolderMountPitch", Description = "取付穴のピッチ円直径", Min = SizeMin, Max = SizeMax)]
            public float MountPitch;
            [PLParam(TextKey = "ShaftHolderMountHole", Description = "取付穴の直径", Min = SizeMin, Max = SizeMax)]
            public float MountHoleDiameter;

            [PLParam(TextKey = "ShaftHolderClampHole", Description = "締結穴の直径", Min = SizeMin, Max = SizeMax)]
            public float ClampHoleDiameter;
            [PLParam(TextKey = "ShaftHolderSplitWidth", Description = "割りの隙間", Min = SizeMin, Max = SizeMax)]
            public float SplitWidth;
            [PLParam(TextKey = "ShaftHolderSegments", Description = "円周方向の分割数",
                     Min = SegmentsMin, Max = SegmentsMax, Step = 1)]
            public int Segments;

            [PLParam(TextKey = "Orientation", Description = "軸を置く向き")]
            public PlaneOrientation Orientation;
            [PLParam(TextKey = "FlipFaces", Description = "生成後にメッシュ全体の面を反転する")]
            public bool FlipFaces;
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;

            public static Params Default => new Params
            {
                MeshName = "ShaftHolder",
                Type = ShaftHolderType.RoundFlange,
                ClampType = ShaftHolderClampType.SetScrew,
                BoreDiameter = 0.3f,
                BossDiameter = 0.62f,
                BossLength = 0.42f,
                FlangeSize = 1.15f,
                FlangeThickness = 0.16f,
                MountPitch = 0.82f,
                MountHoleDiameter = 0.11f,
                ClampHoleDiameter = 0.09f,
                SplitWidth = 0.045f,
                Segments = 48,
                Orientation = PlaneOrientation.XY,
                FlipFaces = false,
                Pivot = Vector3.zero,
            };

            public bool Equals(Params o) =>
                MeshName == o.MeshName && Type == o.Type && ClampType == o.ClampType &&
                Mathf.Approximately(BoreDiameter, o.BoreDiameter) &&
                Mathf.Approximately(BossDiameter, o.BossDiameter) &&
                Mathf.Approximately(BossLength, o.BossLength) &&
                Mathf.Approximately(FlangeSize, o.FlangeSize) &&
                Mathf.Approximately(FlangeThickness, o.FlangeThickness) &&
                Mathf.Approximately(MountPitch, o.MountPitch) &&
                Mathf.Approximately(MountHoleDiameter, o.MountHoleDiameter) &&
                Mathf.Approximately(ClampHoleDiameter, o.ClampHoleDiameter) &&
                Mathf.Approximately(SplitWidth, o.SplitWidth) &&
                Segments == o.Segments && Orientation == o.Orientation &&
                FlipFaces == o.FlipFaces && Pivot == o.Pivot;

            public override bool Equals(object x) => x is Params p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>派生諸元。</summary>
        public struct Info
        {
            public bool  Valid;
            public int   MountHoleCount;
            public float WallThickness;
        }

        public static Info GetInfo(Params p)
        {
            int count = p.Type == ShaftHolderType.RoundFlange ? 3
                      : p.Type == ShaftHolderType.TwoFlatFlange ? 2 : 4;

            bool valid = p.BoreDiameter > 0f
                      && p.BossDiameter > p.BoreDiameter
                      && p.BossLength >= p.FlangeThickness
                      && p.FlangeSize > p.BossDiameter
                      && p.MountPitch > p.BossDiameter * 0.55f
                      && p.MountHoleDiameter > 0f
                      && p.MountHoleDiameter < p.FlangeSize * 0.3f
                      && p.ClampHoleDiameter > 0f;

            return new Info
            {
                Valid          = valid,
                MountHoleCount = count,
                WallThickness  = (p.BossDiameter - p.BoreDiameter) * 0.5f,
            };
        }

        public static MeshObject Generate(Params p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "ShaftHolder" : p.MeshName;
            if (!GetInfo(p).Valid) return new MeshObject(name);

            var m = new MeshObject(name);
            int n = Mathf.Clamp(p.Segments, Params.SegmentsMin, Params.SegmentsMax);

            float rb = p.BossDiameter * 0.5f;
            float rf = p.FlangeSize   * 0.5f;
            float rm = Mathf.Max(p.MountHoleDiameter * 0.9f,
                                 p.MountHoleDiameter * 0.5f + p.FlangeSize * 0.07f);
            float z  = p.FlangeThickness * 0.5f;

            Append(m, Ring("Boss", rb, p.BoreDiameter * 0.5f, p.BossLength, n,
                           Vector2.zero, p.BossLength * 0.5f - z));

            Vector2[] holes;
            if (p.Type == ShaftHolderType.RoundFlange)
            {
                holes = new Vector2[3];
                for (int i = 0; i < 3; i++)
                {
                    float a = Mathf.PI * 0.5f + i * Mathf.PI * 2f / 3f;
                    holes[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * p.MountPitch * 0.5f;
                }
                Append(m, Ring("Flange", rf, p.BoreDiameter * 0.5f, p.FlangeThickness, n, Vector2.zero, 0f));
            }
            else if (p.Type == ShaftHolderType.TwoFlatFlange)
            {
                holes = new[] { new Vector2(-p.MountPitch * 0.5f, 0f), new Vector2(p.MountPitch * 0.5f, 0f) };
                Append(m, Box(-rf, rf, -rb, rb, p.FlangeThickness, 0f));
            }
            else if (p.Type == ShaftHolderType.SquareFlange)
            {
                float q = p.MountPitch * 0.5f;
                holes = new[] { new Vector2(-q, -q), new Vector2(q, -q), new Vector2(q, q), new Vector2(-q, q) };
                Append(m, Box(-rf, rf, -rf, rf, p.FlangeThickness, 0f));
            }
            else
            {
                float q = p.MountPitch * 0.5f;
                holes = new[] { new Vector2(-q, -rf * 0.7f), new Vector2(q, -rf * 0.7f),
                                new Vector2(-q,  rf * 0.7f), new Vector2(q,  rf * 0.7f) };
                Append(m, Box(-rf, rf, -rf, rf, p.FlangeThickness, 0f));
                Append(m, Box(-rb, rb, -rf * 0.7f, rf * 0.7f, p.BossLength * 0.55f, p.BossLength * 0.275f));
            }

            foreach (var h in holes)
                Append(m, Ring("MountHole", rm, p.MountHoleDiameter * 0.5f, p.FlangeThickness * 1.02f, n, h, 0f));

            if (p.ClampType == ShaftHolderClampType.SplitClamp)
            {
                float y = rb + p.SplitWidth * 0.5f;
                Append(m, Box(-p.ClampHoleDiameter, p.ClampHoleDiameter, rb * 0.72f, rb * 1.35f,
                              p.BossLength * 0.42f, p.BossLength * 0.58f - z));
                Append(m, Ring("ClampHole", p.ClampHoleDiameter, p.ClampHoleDiameter * 0.5f,
                               p.BossLength * 0.44f, n, new Vector2(0f, y), p.BossLength * 0.58f - z));
            }
            else
            {
                Append(m, Ring("SetScrew", p.ClampHoleDiameter, p.ClampHoleDiameter * 0.5f,
                               p.BossLength * 0.28f, n, new Vector2(0f, rb * 0.92f), p.BossLength * 0.62f - z));
            }

            GearDiskBuilder.ApplyOrientation(m, p.Orientation);
            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(m);
            PrimitiveMeshPostProcess.ApplyPivotOffset(m, p.Pivot);
            PrimitiveMeshPostProcess.SortVerticesCanonical(m);
            m.InvalidatePositionCache();
            return m;
        }

        /// <summary>中心 c、外径 ro、内径 ri、厚み d の穴あきリング。cz は Z 中心。</summary>
        private static MeshObject Ring(string name, float ro, float ri, float d, int n, Vector2 c, float cz)
        {
            var o = new Vector2[n];
            var h = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = 2f * Mathf.PI * i / n;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                o[i] = c + dir * ro;
                h[i] = c + dir * ri;
            }
            return GearLoftBuilder.Build(name,
                new[] { new GearLoftSection(cz - d * 0.5f, o, h), new GearLoftSection(cz + d * 0.5f, o, h) },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);
        }

        /// <summary>矩形断面の厚み付き板。</summary>
        private static MeshObject Box(float x0, float x1, float y0, float y1, float d, float cz)
        {
            var o = new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
            return GearLoftBuilder.Build("Body",
                new[] { new GearLoftSection(cz - d * 0.5f, o), new GearLoftSection(cz + d * 0.5f, o) },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);
        }

        private static void Append(MeshObject a, MeshObject b) => MeshObjectAppendOps.Append(a, b, true);
    }
}
