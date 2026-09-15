// BearingUnitMeshGenerator.cs
// 軸受ユニット（ピロー形 / 2 穴フランジ形 / 4 穴フランジ形）。
// ハウジング＋外輪＋内輪＋（任意で）玉で組む。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【作り方】
//   リングと板は「閉じた断面を Z へ積む」形（GearLoftBuilder）で作り、連結する。
//   玉は SphereMeshGenerator を並べて置く（転動体は見た目だけで、当たりは持たない）。
//
// 【生成経路】
//   パネルからは呼ばない。CreateBearingUnitCommand → PrimitiveMeshFactory がここを呼ぶ。
//   ローカル空間でピボット適用まで。平行移動・回転・拡大は Factory が行う。

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>ハウジングの形。</summary>
    public enum BearingUnitType { PillowBlock, TwoBoltFlange, FourBoltFlange }

    public static class BearingUnitMeshGenerator
    {
        [Serializable]
        public struct Params : IEquatable<Params>
        {
            public const float SizeMin = 0.001f, SizeMax = 10f;
            public const int BallCountMin = 3, BallCountMax = 64;
            public const int SegmentsMin = 12, SegmentsMax = 128;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "BearingUnitType", Description = "ハウジングの形")]
            public BearingUnitType Type;

            [PLParam(TextKey = "BearingBore", Description = "軸穴の直径", Min = SizeMin, Max = SizeMax)]
            public float BoreDiameter;
            [PLParam(TextKey = "BearingOuter", Description = "軸受の外径", Min = SizeMin, Max = SizeMax)]
            public float BearingOuterDiameter;
            [PLParam(TextKey = "BearingWidth", Description = "軸受の幅", Min = SizeMin, Max = SizeMax)]
            public float BearingWidth;
            [PLParam(TextKey = "BearingBallCount", Description = "玉の数",
                     Min = BallCountMin, Max = BallCountMax, Step = 1)]
            public int BallCount;
            [PLParam(TextKey = "BearingBallDiameter", Description = "玉の直径", Min = SizeMin, Max = SizeMax)]
            public float BallDiameter;

            [PLParam(TextKey = "BearingHousingOuter", Description = "ハウジングの外径", Min = SizeMin, Max = SizeMax)]
            public float HousingOuterDiameter;
            [PLParam(TextKey = "BearingHousingDepth", Description = "ハウジングの奥行き", Min = SizeMin, Max = SizeMax)]
            public float HousingDepth;
            [PLParam(TextKey = "BearingMountSpacing", Description = "取付穴の間隔", Min = SizeMin, Max = SizeMax)]
            public float MountSpacing;
            [PLParam(TextKey = "BearingMountHole", Description = "取付穴の直径", Min = SizeMin, Max = SizeMax)]
            public float MountHoleDiameter;
            [PLParam(TextKey = "BearingMountBoss", Description = "取付座の外径", Min = SizeMin, Max = SizeMax)]
            public float MountBossDiameter;

            [PLParam(TextKey = "BearingShowBalls", Description = "転動体（玉）を作る")]
            public bool ShowBalls;
            [PLParam(TextKey = "BearingSegments", Description = "円周方向の分割数",
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
                MeshName = "BearingUnit",
                Type = BearingUnitType.PillowBlock,
                BoreDiameter = 0.3f,
                BearingOuterDiameter = 0.62f,
                BearingWidth = 0.2f,
                BallCount = 10,
                BallDiameter = 0.07f,
                HousingOuterDiameter = 0.9f,
                HousingDepth = 0.28f,
                MountSpacing = 1.2f,
                MountHoleDiameter = 0.12f,
                MountBossDiameter = 0.28f,
                ShowBalls = true,
                Segments = 48,
                Orientation = PlaneOrientation.XY,
                FlipFaces = false,
                Pivot = Vector3.zero,
            };

            public bool Equals(Params o) =>
                MeshName == o.MeshName && Type == o.Type &&
                Mathf.Approximately(BoreDiameter, o.BoreDiameter) &&
                Mathf.Approximately(BearingOuterDiameter, o.BearingOuterDiameter) &&
                Mathf.Approximately(BearingWidth, o.BearingWidth) &&
                BallCount == o.BallCount &&
                Mathf.Approximately(BallDiameter, o.BallDiameter) &&
                Mathf.Approximately(HousingOuterDiameter, o.HousingOuterDiameter) &&
                Mathf.Approximately(HousingDepth, o.HousingDepth) &&
                Mathf.Approximately(MountSpacing, o.MountSpacing) &&
                Mathf.Approximately(MountHoleDiameter, o.MountHoleDiameter) &&
                Mathf.Approximately(MountBossDiameter, o.MountBossDiameter) &&
                ShowBalls == o.ShowBalls && Segments == o.Segments &&
                Orientation == o.Orientation && FlipFaces == o.FlipFaces && Pivot == o.Pivot;

            public override bool Equals(object x) => x is Params p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>派生諸元。</summary>
        public struct Info
        {
            public bool  Valid;
            public float BallPitchDiameter;
            public bool  BallTooLarge;
        }

        public static Info GetInfo(Params p)
        {
            float pitch = (p.BoreDiameter + p.BearingOuterDiameter) * 0.5f;
            bool tooLarge = p.BallDiameter > Mathf.Max(0f, (p.BearingOuterDiameter - p.BoreDiameter) * 0.45f);

            return new Info
            {
                Valid = p.BoreDiameter > 0f
                     && p.BearingOuterDiameter > p.BoreDiameter
                     && p.HousingOuterDiameter > p.BearingOuterDiameter
                     && p.BearingWidth > 0f
                     && p.HousingDepth >= p.BearingWidth
                     && p.MountBossDiameter > p.MountHoleDiameter,
                BallPitchDiameter = pitch,
                BallTooLarge      = tooLarge,
            };
        }

        public static MeshObject Generate(Params p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "BearingUnit" : p.MeshName;
            if (!GetInfo(p).Valid) return new MeshObject(name);

            var m = new MeshObject(name);
            int n = Mathf.Clamp(p.Segments, Params.SegmentsMin, Params.SegmentsMax);

            Append(m, Ring("Housing", p.HousingOuterDiameter * 0.5f,
                           p.BearingOuterDiameter * 0.505f, p.HousingDepth, n, Vector2.zero));

            float sx = p.MountSpacing * 0.5f;
            float sy = p.Type == BearingUnitType.PillowBlock ? -p.HousingOuterDiameter * 0.6f : 0f;

            if (p.Type == BearingUnitType.PillowBlock)
            {
                AddPillowBase(m, sx, sy, p);
                AddBoss(m, new Vector2(-sx, sy), p, n);
                AddBoss(m, new Vector2( sx, sy), p, n);
            }
            else if (p.Type == BearingUnitType.TwoBoltFlange)
            {
                AddBoss(m, new Vector2(-sx, 0f), p, n);
                AddBoss(m, new Vector2( sx, 0f), p, n);
                Append(m, Box(-sx + p.MountBossDiameter * 0.5f, sx - p.MountBossDiameter * 0.5f,
                              -p.MountBossDiameter * 0.35f, p.MountBossDiameter * 0.35f,
                              p.HousingDepth * 0.75f));
            }
            else
            {
                float q = p.MountSpacing * 0.5f;
                foreach (var c in new[] { new Vector2(-q, -q), new Vector2(q, -q),
                                          new Vector2( q,  q), new Vector2(-q, q) })
                    AddBoss(m, c, p, n);

                Append(m, Box(-q, q, -p.MountBossDiameter * 0.3f, p.MountBossDiameter * 0.3f,
                              p.HousingDepth * 0.7f));
                Append(m, Box(-p.MountBossDiameter * 0.3f, p.MountBossDiameter * 0.3f, -q, q,
                              p.HousingDepth * 0.7f));
            }

            float raceMid = (p.BoreDiameter + p.BearingOuterDiameter) * 0.25f;
            Append(m, Ring("OuterRace", p.BearingOuterDiameter * 0.5f, raceMid + p.BallDiameter * 0.3f,
                           p.BearingWidth, n, Vector2.zero));
            Append(m, Ring("InnerRace", raceMid - p.BallDiameter * 0.3f, p.BoreDiameter * 0.5f,
                           p.BearingWidth, n, Vector2.zero));

            if (p.ShowBalls)
            {
                int balls = Mathf.Clamp(p.BallCount, Params.BallCountMin, Params.BallCountMax);
                float br = Mathf.Min(p.BallDiameter * 0.5f,
                                     (p.BearingOuterDiameter - p.BoreDiameter) * 0.225f);
                for (int i = 0; i < balls; i++)
                {
                    float a = 2f * Mathf.PI * i / balls;
                    var sp = SphereMeshGenerator.SphereParams.Default;
                    sp.MeshName          = "Ball";
                    sp.Radius            = br;
                    sp.LongitudeSegments = 12;
                    sp.LatitudeSegments  = 8;
                    var b = SphereMeshGenerator.Generate(sp);
                    Move(b, new Vector3(raceMid * Mathf.Cos(a), raceMid * Mathf.Sin(a), 0f));
                    Append(m, b);
                }
            }

            GearDiskBuilder.ApplyOrientation(m, p.Orientation);
            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(m);
            PrimitiveMeshPostProcess.ApplyPivotOffset(m, p.Pivot);
            PrimitiveMeshPostProcess.SortVerticesCanonical(m);
            m.InvalidatePositionCache();
            return m;
        }

        private static void AddBoss(MeshObject m, Vector2 c, Params p, int n)
            => Append(m, Ring("Mount", p.MountBossDiameter * 0.5f, p.MountHoleDiameter * 0.5f,
                              p.HousingDepth * 0.75f, n, c));

        /// <summary>ピロー形の台座。取付座の穴を避けて板を 5 枚に割る。</summary>
        private static void AddPillowBase(MeshObject m, float sx, float sy, Params p)
        {
            float r = p.MountBossDiameter * 0.5f;
            float x0 = -sx - r, x1 = sx + r, y0 = sy - r, y1 = sy + r;
            float d = p.HousingDepth * 0.75f;

            Append(m, Box(x0, x1, y0, sy - r * 0.72f, d));
            Append(m, Box(x0, x1, sy + r * 0.72f, y1, d));
            Append(m, Box(x0, -sx - r * 0.72f, sy - r * 0.72f, sy + r * 0.72f, d));
            Append(m, Box(-sx + r * 0.72f, sx - r * 0.72f, sy - r * 0.72f, sy + r * 0.72f, d));
            Append(m, Box(sx + r * 0.72f, x1, sy - r * 0.72f, sy + r * 0.72f, d));
        }

        private static MeshObject Ring(string name, float ro, float ri, float d, int n, Vector2 c)
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
                new[] { new GearLoftSection(-d * 0.5f, o, h), new GearLoftSection(d * 0.5f, o, h) },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);
        }

        private static MeshObject Box(float x0, float x1, float y0, float y1, float d)
        {
            var o = new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
            return GearLoftBuilder.Build("Bridge",
                new[] { new GearLoftSection(-d * 0.5f, o), new GearLoftSection(d * 0.5f, o) },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);
        }

        private static void Move(MeshObject m, Vector3 v)
        {
            foreach (var x in m.Vertices) x.Position += v;
            m.InvalidatePositionCache();
        }

        private static void Append(MeshObject a, MeshObject b) => MeshObjectAppendOps.Append(a, b, true);
    }
}
