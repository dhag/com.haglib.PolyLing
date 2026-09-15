// RodEndMeshGenerator.cs
// ロッドエンド（雄ねじ / 雌ねじ）とボールスタッドジョイント。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【作り方】
//   球面軸受の玉は、両端を平面で切り落とした中空の球（HollowBall）で作る。
//   ハウジング・首・軸は「閉じた断面を Z へ積む」形（GearLoftBuilder）と円柱。
//   ねじ山は輪を軸方向に並べて表現する（本物のらせんは切らない）。
//
// 【生成経路】
//   パネルからは呼ばない。CreateRodEndCommand → PrimitiveMeshFactory がここを呼ぶ。

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>ロッドエンドの種類。</summary>
    public enum RodEndType { MaleRodEnd, FemaleRodEnd, BallStudJoint }

    public static class RodEndMeshGenerator
    {
        [Serializable]
        public struct Params : IEquatable<Params>
        {
            public const float SizeMin = 0.001f, SizeMax = 10f;
            public const float StudAngleMin = 0f, StudAngleMax = 45f;
            public const int SegmentsMin = 12, SegmentsMax = 96;

            /// <summary>ねじ山を表す輪の上限。多すぎると面数が跳ね上がる。</summary>
            public const int ThreadRingMax = 48;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "RodEndType", Description = "ロッドエンドの種類")]
            public RodEndType Type;

            [PLParam(TextKey = "RodEndBallDiameter", Description = "玉の直径", Min = SizeMin, Max = SizeMax)]
            public float BallDiameter;
            [PLParam(TextKey = "RodEndBallBore", Description = "玉の軸穴", Min = SizeMin, Max = SizeMax)]
            public float BallBore;
            [PLParam(TextKey = "RodEndHousingDiameter", Description = "ハウジングの外径", Min = SizeMin, Max = SizeMax)]
            public float HousingDiameter;
            [PLParam(TextKey = "RodEndHousingWidth", Description = "ハウジングの幅", Min = SizeMin, Max = SizeMax)]
            public float HousingWidth;
            [PLParam(TextKey = "RodEndNeckWidth", Description = "首の幅", Min = SizeMin, Max = SizeMax)]
            public float NeckWidth;
            [PLParam(TextKey = "RodEndShankDiameter", Description = "軸の直径", Min = SizeMin, Max = SizeMax)]
            public float ShankDiameter;
            [PLParam(TextKey = "RodEndShankLength", Description = "軸の長さ", Min = SizeMin, Max = SizeMax)]
            public float ShankLength;
            [PLParam(TextKey = "RodEndThreadPitch", Description = "ねじのピッチ", Min = SizeMin, Max = SizeMax)]
            public float ThreadPitch;
            [PLParam(TextKey = "RodEndStudAngle", Description = "スタッドの傾き",
                     Min = StudAngleMin, Max = StudAngleMax)]
            public float StudAngleDeg;
            [PLParam(TextKey = "RodEndShowThread", Description = "ねじ山を作る")]
            public bool ShowThread;
            [PLParam(TextKey = "RodEndSegments", Description = "円周方向の分割数",
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
                MeshName = "RodEnd",
                Type = RodEndType.MaleRodEnd,
                BallDiameter = 0.42f,
                BallBore = 0.18f,
                HousingDiameter = 0.62f,
                HousingWidth = 0.28f,
                NeckWidth = 0.24f,
                ShankDiameter = 0.2f,
                ShankLength = 0.7f,
                ThreadPitch = 0.06f,
                StudAngleDeg = 0f,
                ShowThread = true,
                Segments = 40,
                Orientation = PlaneOrientation.XY,
                FlipFaces = false,
                Pivot = Vector3.zero,
            };

            public bool Equals(Params o) =>
                MeshName == o.MeshName && Type == o.Type &&
                Mathf.Approximately(BallDiameter, o.BallDiameter) &&
                Mathf.Approximately(BallBore, o.BallBore) &&
                Mathf.Approximately(HousingDiameter, o.HousingDiameter) &&
                Mathf.Approximately(HousingWidth, o.HousingWidth) &&
                Mathf.Approximately(NeckWidth, o.NeckWidth) &&
                Mathf.Approximately(ShankDiameter, o.ShankDiameter) &&
                Mathf.Approximately(ShankLength, o.ShankLength) &&
                Mathf.Approximately(ThreadPitch, o.ThreadPitch) &&
                Mathf.Approximately(StudAngleDeg, o.StudAngleDeg) &&
                ShowThread == o.ShowThread && Segments == o.Segments &&
                Orientation == o.Orientation && FlipFaces == o.FlipFaces && Pivot == o.Pivot;

            public override bool Equals(object x) => x is Params p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>派生諸元。</summary>
        public struct Info
        {
            public bool  Valid;
            public float MaxTiltDeg;
            public int   ThreadTurns;
        }

        public static Info GetInfo(Params p)
        {
            float r  = p.BallDiameter * 0.5f;
            float ri = p.BallBore     * 0.5f;
            float max = ri < r ? Mathf.Acos(Mathf.Clamp(ri / r, 0f, 1f)) * Mathf.Rad2Deg : 0f;

            return new Info
            {
                Valid = p.BallDiameter > p.BallBore && p.BallBore > 0f
                     && p.HousingDiameter > p.BallDiameter
                     && p.HousingWidth > 0f && p.NeckWidth > 0f
                     && p.ShankDiameter > 0f && p.ShankLength > 0f
                     && p.ThreadPitch > 0f,
                MaxTiltDeg  = max,
                ThreadTurns = Mathf.Max(1, Mathf.FloorToInt(p.ShankLength / Mathf.Max(1e-6f, p.ThreadPitch))),
            };
        }

        public static MeshObject Generate(Params p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "RodEnd" : p.MeshName;
            if (!GetInfo(p).Valid) return new MeshObject(name);

            var m = new MeshObject(name);
            int n = Mathf.Clamp(p.Segments, Params.SegmentsMin, Params.SegmentsMax);

            if (p.Type == RodEndType.BallStudJoint) BuildBallStud(m, p, n);
            else                                    BuildRodEnd(m, p, n);

            GearDiskBuilder.ApplyOrientation(m, p.Orientation);
            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(m);
            PrimitiveMeshPostProcess.ApplyPivotOffset(m, p.Pivot);
            PrimitiveMeshPostProcess.SortVerticesCanonical(m);
            m.InvalidatePositionCache();
            return m;
        }

        private static void BuildRodEnd(MeshObject m, Params p, int n)
        {
            Append(m, HollowBall("BearingBall", p.BallDiameter * 0.5f, p.BallBore * 0.5f, n));
            Append(m, Ring("Housing", p.HousingDiameter * 0.5f, p.BallDiameter * 0.505f,
                           p.HousingWidth, n, Vector2.zero, 0f));

            float y = -p.HousingDiameter * 0.5f - p.ShankLength * 0.5f;
            AddBox(m, new Vector3(0f, -p.HousingDiameter * 0.42f, 0f), Quaternion.identity,
                   new Vector3(p.NeckWidth, p.HousingDiameter * 0.45f, p.HousingWidth));

            if (p.Type == RodEndType.MaleRodEnd)
            {
                AddCylinder(m, new Vector3(0f, y, 0f), Vector3.up, p.ShankDiameter * 0.5f, p.ShankLength, n);
                if (p.ShowThread) AddThreadRings(m, y, p, n);
            }
            else
            {
                AddRingAxis(m, new Vector3(0f, y, 0f), Vector3.up,
                            p.ShankDiameter * 0.72f, p.ShankDiameter * 0.42f, p.ShankLength, n);
            }
        }

        private static void BuildBallStud(MeshObject m, Params p, int n)
        {
            var sp = SphereMeshGenerator.SphereParams.Default;
            sp.MeshName          = "Ball";
            sp.Radius            = p.BallDiameter * 0.5f;
            sp.LongitudeSegments = Mathf.Clamp(n, 12, 48);
            sp.LatitudeSegments  = Mathf.Clamp(n / 2, 8, 24);
            var b = SphereMeshGenerator.Generate(sp);
            Transform(b, new Vector3(0f, p.ShankLength * 0.12f, 0f),
                      Quaternion.AngleAxis(p.StudAngleDeg, Vector3.forward));
            Append(m, b);

            Append(m, Ring("Socket", p.HousingDiameter * 0.5f, p.BallDiameter * 0.47f,
                           p.HousingWidth, n, Vector2.zero, 0f));

            Vector3 axis = Quaternion.AngleAxis(p.StudAngleDeg, Vector3.forward) * Vector3.down;
            Vector3 c = axis * (p.BallDiameter * 0.5f + p.ShankLength * 0.5f);
            AddCylinder(m, c, axis, p.ShankDiameter * 0.5f, p.ShankLength, n);

            if (!p.ShowThread) return;

            int turns = Mathf.Min(Params.ThreadRingMax, GetInfo(p).ThreadTurns);
            for (int i = 0; i <= turns; i++)
            {
                float t = (float)i / turns;
                AddCylinder(m, c + axis * ((t - 0.5f) * p.ShankLength), axis,
                            p.ShankDiameter * 0.55f, p.ThreadPitch * 0.18f, n);
            }
        }

        /// <summary>上下を平面で切り落とし、中心に軸穴を通した球。</summary>
        private static MeshObject HollowBall(string name, float r, float ri, int n)
        {
            var m = new MeshObject(name);
            int lat = Mathf.Clamp(n / 2, 6, 32);
            int cols = n + 1;
            float zmax = Mathf.Sqrt(Mathf.Max(0f, r * r - ri * ri));

            for (int j = 0; j <= lat; j++)
            {
                float z = Mathf.Lerp(-zmax, zmax, (float)j / lat);
                float rr = Mathf.Sqrt(Mathf.Max(0f, r * r - z * z));
                for (int i = 0; i <= n; i++)
                {
                    float a = 2f * Mathf.PI * i / n;
                    var pos = new Vector3(rr * Mathf.Cos(a), rr * Mathf.Sin(a), z);
                    m.Vertices.Add(new Vertex(pos, new Vector2((float)i / n, (float)j / lat), pos.normalized));
                }
            }
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < n; i++)
                {
                    int q = j * cols + i;
                    m.AddQuad(q, q + 1, q + cols + 1, q + cols);
                }

            int inner = m.VertexCount;
            for (int j = 0; j <= 1; j++)
            {
                float z = j == 0 ? -zmax : zmax;
                for (int i = 0; i <= n; i++)
                {
                    float a = 2f * Mathf.PI * i / n;
                    m.Vertices.Add(new Vertex(
                        new Vector3(ri * Mathf.Cos(a), ri * Mathf.Sin(a), z),
                        new Vector2((float)i / n, j),
                        new Vector3(-Mathf.Cos(a), -Mathf.Sin(a), 0f)));
                }
            }
            for (int i = 0; i < n; i++)
                m.AddQuad(inner + i, inner + cols + i, inner + cols + i + 1, inner + i + 1);

            return m;
        }

        private static void AddThreadRings(MeshObject m, float cy, Params p, int n)
        {
            int turns = Mathf.Min(Params.ThreadRingMax, GetInfo(p).ThreadTurns);
            for (int i = 0; i <= turns; i++)
            {
                float y = cy - p.ShankLength * 0.5f + p.ShankLength * i / turns;
                AddCylinder(m, new Vector3(0f, y, 0f), Vector3.up,
                            p.ShankDiameter * 0.55f, p.ThreadPitch * 0.18f, n);
            }
        }

        private static MeshObject Ring(string name, float ro, float ri, float d, int n, Vector2 c, float z)
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
                new[] { new GearLoftSection(z - d * 0.5f, o, h), new GearLoftSection(z + d * 0.5f, o, h) },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);
        }

        private static void AddRingAxis(MeshObject m, Vector3 c, Vector3 axis,
                                        float ro, float ri, float len, int n)
        {
            var x = Ring("FemaleShank", ro, ri, len, n, Vector2.zero, 0f);
            Transform(x, c, Quaternion.FromToRotation(Vector3.forward, axis));
            Append(m, x);
        }

        private static void AddCylinder(MeshObject m, Vector3 c, Vector3 axis, float r, float len, int n)
        {
            var cp = CylinderMeshGenerator.CylinderParams.Default;
            cp.MeshName       = "Shank";
            cp.RadiusTop      = r;
            cp.RadiusBottom   = r;
            cp.Height         = len;
            cp.RadialSegments = Mathf.Clamp(n, 12, 48);
            cp.HeightSegments = 1;
            var x = CylinderMeshGenerator.Generate(cp);
            Transform(x, c, Quaternion.FromToRotation(Vector3.up, axis));
            Append(m, x);
        }

        private static void AddBox(MeshObject m, Vector3 c, Quaternion q, Vector3 s)
        {
            var o = new[]
            {
                new Vector2(-s.x * 0.5f, -s.y * 0.5f),
                new Vector2( s.x * 0.5f, -s.y * 0.5f),
                new Vector2( s.x * 0.5f,  s.y * 0.5f),
                new Vector2(-s.x * 0.5f,  s.y * 0.5f),
            };
            var x = GearLoftBuilder.Build("Neck",
                new[] { new GearLoftSection(-s.z * 0.5f, o), new GearLoftSection(s.z * 0.5f, o) },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);
            Transform(x, c, q);
            Append(m, x);
        }

        private static void Transform(MeshObject x, Vector3 c, Quaternion q)
        {
            foreach (var v in x.Vertices)
            {
                v.Position = q * v.Position + c;
                for (int i = 0; i < v.Normals.Count; i++) v.Normals[i] = q * v.Normals[i];
            }
            x.InvalidatePositionCache();
        }

        private static void Append(MeshObject a, MeshObject b) => MeshObjectAppendOps.Append(a, b, true);
    }
}
