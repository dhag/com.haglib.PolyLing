// MotorCouplingMeshGenerator.cs
// 軸継手（剛性スリーブ / クランプ / ジョー / オルダム / ベローズ）。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【作り方】
//   どの型も「入力側ハブ＋中間要素＋出力側ハブ」を Z 方向に並べた形。
//   ハブとベローズは「閉じた断面を Z へ積む」形（GearLoftBuilder）、
//   爪・耳・すべり子は角柱を回して置く。
//
// 【生成経路】
//   パネルからは呼ばない。CreateMotorCouplingCommand → PrimitiveMeshFactory がここを呼ぶ。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>継手の型。</summary>
    public enum MotorCouplingType { RigidSleeve, Clamp, Jaw, Oldham, Bellows }

    public static class MotorCouplingMeshGenerator
    {
        [Serializable]
        public struct Params : IEquatable<Params>
        {
            public const float SizeMin = 0.001f, SizeMax = 10f;
            public const int JawCountMin = 3, JawCountMax = 12;
            public const int BellowsCountMin = 2, BellowsCountMax = 24;
            public const int SegmentsMin = 12, SegmentsMax = 128;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "MotorCouplingType", Description = "継手の型")]
            public MotorCouplingType Type;

            [PLParam(TextKey = "CouplingInputBore", Description = "入力側の軸穴", Min = SizeMin, Max = SizeMax)]
            public float InputBore;
            [PLParam(TextKey = "CouplingOutputBore", Description = "出力側の軸穴", Min = SizeMin, Max = SizeMax)]
            public float OutputBore;
            [PLParam(TextKey = "CouplingOuterDiameter", Description = "外径", Min = SizeMin, Max = SizeMax)]
            public float OuterDiameter;
            [PLParam(TextKey = "CouplingLength", Description = "全長", Min = SizeMin, Max = SizeMax)]
            public float Length;
            [PLParam(TextKey = "CouplingCenterGap", Description = "中央の隙間", Min = 0, Max = SizeMax)]
            public float CenterGap;
            [PLParam(TextKey = "CouplingClampHole", Description = "クランプ穴の直径", Min = SizeMin, Max = SizeMax)]
            public float ClampHoleDiameter;
            [PLParam(TextKey = "CouplingJawCount", Description = "爪の数",
                     Min = JawCountMin, Max = JawCountMax, Step = 1)]
            public int JawCount;
            [PLParam(TextKey = "CouplingJawDepth", Description = "爪・すべり子の厚み", Min = SizeMin, Max = SizeMax)]
            public float JawDepth;
            [PLParam(TextKey = "CouplingBellowsCount", Description = "ベローズの山数",
                     Min = BellowsCountMin, Max = BellowsCountMax, Step = 1)]
            public int BellowsCount;
            [PLParam(TextKey = "CouplingBellowsDepth", Description = "ベローズの山の高さ", Min = SizeMin, Max = SizeMax)]
            public float BellowsDepth;
            [PLParam(TextKey = "CouplingSegments", Description = "円周方向の分割数",
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
                MeshName = "MotorCoupling",
                Type = MotorCouplingType.Clamp,
                InputBore = 0.2f,
                OutputBore = 0.25f,
                OuterDiameter = 0.55f,
                Length = 0.7f,
                CenterGap = 0.04f,
                ClampHoleDiameter = 0.07f,
                JawCount = 3,
                JawDepth = 0.12f,
                BellowsCount = 6,
                BellowsDepth = 0.07f,
                Segments = 48,
                Orientation = PlaneOrientation.XY,
                FlipFaces = false,
                Pivot = Vector3.zero,
            };

            public bool Equals(Params o) =>
                MeshName == o.MeshName && Type == o.Type &&
                Mathf.Approximately(InputBore, o.InputBore) &&
                Mathf.Approximately(OutputBore, o.OutputBore) &&
                Mathf.Approximately(OuterDiameter, o.OuterDiameter) &&
                Mathf.Approximately(Length, o.Length) &&
                Mathf.Approximately(CenterGap, o.CenterGap) &&
                Mathf.Approximately(ClampHoleDiameter, o.ClampHoleDiameter) &&
                JawCount == o.JawCount &&
                Mathf.Approximately(JawDepth, o.JawDepth) &&
                BellowsCount == o.BellowsCount &&
                Mathf.Approximately(BellowsDepth, o.BellowsDepth) &&
                Segments == o.Segments && Orientation == o.Orientation &&
                FlipFaces == o.FlipFaces && Pivot == o.Pivot;

            public override bool Equals(object x) => x is Params p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>派生諸元。</summary>
        public struct Info
        {
            public bool  Valid;
            public float InputWall;
            public float OutputWall;
            public int   FlexibleElements;
        }

        public static Info GetInfo(Params p)
        {
            // 爪・すべり子を持つ型では、中間要素がハブを食い破らないことを確かめる。
            bool needsCenter = p.Type == MotorCouplingType.Jaw || p.Type == MotorCouplingType.Oldham;
            bool centerOk = !needsCenter || p.JawDepth < p.Length * 0.5f - p.CenterGap * 0.5f;

            return new Info
            {
                Valid = p.InputBore > 0f && p.OutputBore > 0f
                     && p.OuterDiameter > Mathf.Max(p.InputBore, p.OutputBore)
                     && p.Length > 0f
                     && p.CenterGap >= 0f && p.CenterGap < p.Length * 0.5f
                     && p.ClampHoleDiameter > 0f
                     && p.JawDepth > 0f && p.BellowsDepth > 0f
                     && centerOk,
                InputWall  = (p.OuterDiameter - p.InputBore)  * 0.5f,
                OutputWall = (p.OuterDiameter - p.OutputBore) * 0.5f,
                FlexibleElements = p.Type == MotorCouplingType.Jaw     ? p.JawCount
                                 : p.Type == MotorCouplingType.Bellows ? p.BellowsCount
                                 : p.Type == MotorCouplingType.Oldham  ? 2 : 0,
            };
        }

        public static MeshObject Generate(Params p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "MotorCoupling" : p.MeshName;
            if (!GetInfo(p).Valid) return new MeshObject(name);

            var m = new MeshObject(name);
            int n = Mathf.Clamp(p.Segments, Params.SegmentsMin, Params.SegmentsMax);
            float r = p.OuterDiameter * 0.5f;
            float half = p.Length * 0.5f;
            float g = p.CenterGap * 0.5f;

            if (p.Type == MotorCouplingType.RigidSleeve)
            {
                Append(m, Ring("InputHalf",  r, p.InputBore  * 0.5f, half, n, -half * 0.5f));
                Append(m, Ring("OutputHalf", r, p.OutputBore * 0.5f, half, n,  half * 0.5f));
            }
            else if (p.Type == MotorCouplingType.Clamp)
            {
                Append(m, Ring("InputClamp",  r, p.InputBore  * 0.5f, half - g, n, -(half + g) * 0.5f));
                Append(m, Ring("OutputClamp", r, p.OutputBore * 0.5f, half - g, n,  (half + g) * 0.5f));
                AddClampEars(m, p, n, -half * 0.55f);
                AddClampEars(m, p, n,  half * 0.55f);
            }
            else if (p.Type == MotorCouplingType.Jaw)
            {
                float hub = half - g - p.JawDepth;
                Append(m, Ring("InputHub",  r, p.InputBore  * 0.5f, hub, n, -half + hub * 0.5f));
                Append(m, Ring("OutputHub", r, p.OutputBore * 0.5f, hub, n,  half - hub * 0.5f));
                AddJaws(m, p, -1);
                AddJaws(m, p,  1);
                AddSpider(m, p, n);
            }
            else if (p.Type == MotorCouplingType.Oldham)
            {
                float hub = half - g - p.JawDepth;
                Append(m, Ring("InputHub",  r, p.InputBore  * 0.5f, hub, n, -half + hub * 0.5f));
                Append(m, Ring("OutputHub", r, p.OutputBore * 0.5f, hub, n,  half - hub * 0.5f));
                Append(m, Ring("CenterDisk", r * 0.82f, 0f, p.JawDepth, n, 0f));
                AddBox(m, new Vector3(0f, r * 0.38f, -g * 0.5f), Quaternion.identity,
                       new Vector3(r * 1.35f, r * 0.28f, p.JawDepth));
                AddBox(m, new Vector3(r * 0.38f, 0f, g * 0.5f), Quaternion.AngleAxis(90f, Vector3.forward),
                       new Vector3(r * 1.35f, r * 0.28f, p.JawDepth));
            }
            else
            {
                float hub = Mathf.Max(p.Length * 0.16f, p.BellowsDepth);
                Append(m, Ring("InputHub",  r, p.InputBore  * 0.5f, hub, n, -half + hub * 0.5f));
                Append(m, Ring("OutputHub", r, p.OutputBore * 0.5f, hub, n,  half - hub * 0.5f));
                AddBellows(m, p, n, -half + hub, half - hub);
            }

            GearDiskBuilder.ApplyOrientation(m, p.Orientation);
            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(m);
            PrimitiveMeshPostProcess.ApplyPivotOffset(m, p.Pivot);
            PrimitiveMeshPostProcess.SortVerticesCanonical(m);
            m.InvalidatePositionCache();
            return m;
        }

        /// <summary>クランプの締めねじが通る耳。</summary>
        private static void AddClampEars(MeshObject m, Params p, int n, float z)
        {
            float r = p.OuterDiameter * 0.5f;
            AddBox(m, new Vector3(r * 1.02f, 0f, z), Quaternion.identity,
                   new Vector3(r * 0.42f, p.ClampHoleDiameter * 2.2f, p.Length * 0.2f));
            Append(m, Ring("ClampHole", p.ClampHoleDiameter, p.ClampHoleDiameter * 0.5f,
                           p.Length * 0.21f, n, new Vector2(r * 1.02f, 0f), z));
        }

        /// <summary>ジョー継手の爪。入力側と出力側で半ピッチずらして噛み合わせる。</summary>
        private static void AddJaws(MeshObject m, Params p, int side)
        {
            int c = Mathf.Clamp(p.JawCount, Params.JawCountMin, Params.JawCountMax);
            float r = p.OuterDiameter * 0.5f;
            float z = side * (p.CenterGap * 0.5f + p.JawDepth * 0.5f);

            for (int i = 0; i < c; i++)
            {
                float a = 2f * Mathf.PI * (i + (side > 0 ? 0.5f : 0f)) / c;
                Vector3 pos = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r * 0.68f + Vector3.forward * z;
                AddBox(m, pos, Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.forward),
                       new Vector3(r * 0.62f, r * 0.34f, p.JawDepth));
            }
        }

        /// <summary>ジョー継手の間に入る弾性体（スパイダ）。</summary>
        private static void AddSpider(MeshObject m, Params p, int n)
        {
            int c = Mathf.Clamp(p.JawCount, Params.JawCountMin, Params.JawCountMax);
            float r = p.OuterDiameter * 0.5f;

            Append(m, Ring("SpiderCore", r * 0.3f, 0f, p.CenterGap + p.JawDepth * 0.55f, n, 0f));
            for (int i = 0; i < c; i++)
            {
                float a = 2f * Mathf.PI * (i + 0.25f) / c;
                AddBox(m, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r * 0.48f,
                       Quaternion.AngleAxis(a * Mathf.Rad2Deg, Vector3.forward),
                       new Vector3(r * 0.48f, r * 0.22f, p.CenterGap + p.JawDepth * 0.5f));
            }
        }

        /// <summary>山と谷を交互に積んだ蛇腹。</summary>
        private static void AddBellows(MeshObject m, Params p, int n, float z0, float z1)
        {
            int folds = Mathf.Clamp(p.BellowsCount, Params.BellowsCountMin, Params.BellowsCountMax);
            int count = folds * 2 + 1;

            var sections = new List<GearLoftSection>(count);
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                float z = Mathf.Lerp(z0, z1, t);
                float ro = p.OuterDiameter * 0.5f + (i % 2 == 1 ? p.BellowsDepth : 0f);
                float ri = Mathf.Max(p.InputBore, p.OutputBore) * 0.5f;
                sections.Add(new GearLoftSection(z, Circle(ro, n), Circle(ri, n)));
            }
            Append(m, GearLoftBuilder.Build("Bellows", sections, GearLoftCapMode.Triangulate,
                PlaneOrientation.XY, false, Vector3.zero));
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

        private static MeshObject Ring(string name, float ro, float ri, float d, int n, float z)
            => GearLoftBuilder.Build(name,
                new[]
                {
                    new GearLoftSection(z - d * 0.5f, Circle(ro, n), ri > 0f ? Circle(ri, n) : null),
                    new GearLoftSection(z + d * 0.5f, Circle(ro, n), ri > 0f ? Circle(ri, n) : null),
                },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);

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

        private static void AddBox(MeshObject m, Vector3 c, Quaternion q, Vector3 s)
        {
            var o = new[]
            {
                new Vector2(-s.x * 0.5f, -s.y * 0.5f),
                new Vector2( s.x * 0.5f, -s.y * 0.5f),
                new Vector2( s.x * 0.5f,  s.y * 0.5f),
                new Vector2(-s.x * 0.5f,  s.y * 0.5f),
            };
            var x = GearLoftBuilder.Build("Element",
                new[] { new GearLoftSection(-s.z * 0.5f, o), new GearLoftSection(s.z * 0.5f, o) },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);

            foreach (var v in x.Vertices)
            {
                v.Position = q * v.Position + c;
                for (int i = 0; i < v.Normals.Count; i++) v.Normals[i] = q * v.Normals[i];
            }
            x.InvalidatePositionCache();
            Append(m, x);
        }

        private static void Append(MeshObject a, MeshObject b) => MeshObjectAppendOps.Append(a, b, true);
    }
}
