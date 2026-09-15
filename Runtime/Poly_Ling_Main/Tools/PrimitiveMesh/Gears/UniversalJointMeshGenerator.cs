// UniversalJointMeshGenerator.cs
// 自在継手（単一カルダン / ダブルカルダン）。
// ハブ 2 本＋フォーク 4 枚＋十字軸で 1 節を作り、ダブルは 2 節を中間軸でつなぐ。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【作り方】
//   ハブとフォークは「閉じた断面を Z へ積む」形（GearLoftBuilder）、
//   十字軸は CylinderMeshGenerator。作った部品を回して置いてから連結する。
//   姿勢は形状そのものなので、ここでの回転はジェネレータ内の許容範囲
//   （生成器が入れてはいけないのは「配置としての」平行移動・回転・拡大）。
//
// 【生成経路】
//   パネルからは呼ばない。CreateUniversalJointCommand → PrimitiveMeshFactory がここを呼ぶ。

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>継手の段数。</summary>
    public enum UniversalJointType { SingleCardan, DoubleCardan }

    public static class UniversalJointMeshGenerator
    {
        [Serializable]
        public struct Params : IEquatable<Params>
        {
            public const float SizeMin = 0.001f, SizeMax = 10f;
            public const float AngleMin = 0f, AngleMax = 80f;
            public const float PhaseMin = 0f, PhaseMax = 180f;
            public const int SegmentsMin = 12, SegmentsMax = 96;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "UniversalJointType", Description = "継手の段数")]
            public UniversalJointType Type;

            [PLParam(TextKey = "UniversalJointInputBore", Description = "入力側の軸穴径", Min = SizeMin, Max = SizeMax)]
            public float InputBore;
            [PLParam(TextKey = "UniversalJointOutputBore", Description = "出力側の軸穴径", Min = SizeMin, Max = SizeMax)]
            public float OutputBore;
            [PLParam(TextKey = "UniversalJointHubDiameter", Description = "ハブの外径", Min = SizeMin, Max = SizeMax)]
            public float HubDiameter;
            [PLParam(TextKey = "UniversalJointHubLength", Description = "ハブの長さ", Min = SizeMin, Max = SizeMax)]
            public float HubLength;
            [PLParam(TextKey = "UniversalJointForkSpan", Description = "フォークの開き（十字軸の差し渡し）",
                     Min = SizeMin, Max = SizeMax)]
            public float ForkSpan;
            [PLParam(TextKey = "UniversalJointForkThickness", Description = "フォークの厚み", Min = SizeMin, Max = SizeMax)]
            public float ForkThickness;
            [PLParam(TextKey = "UniversalJointCrossDiameter", Description = "十字軸の径", Min = SizeMin, Max = SizeMax)]
            public float CrossDiameter;
            [PLParam(TextKey = "UniversalJointAngle", Description = "入力軸と出力軸のなす角", Min = AngleMin, Max = AngleMax)]
            public float JointAngleDeg;
            [PLParam(TextKey = "UniversalJointPhase", Description = "フォークの位相", Min = PhaseMin, Max = PhaseMax)]
            public float PhaseDeg;
            [PLParam(TextKey = "UniversalJointCenterLength", Description = "ダブルのときの中間軸の長さ",
                     Min = SizeMin, Max = SizeMax)]
            public float CenterLength;
            [PLParam(TextKey = "UniversalJointSegments", Description = "円周方向の分割数",
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
                MeshName = "UniversalJoint",
                Type = UniversalJointType.SingleCardan,
                InputBore = 0.18f,
                OutputBore = 0.18f,
                HubDiameter = 0.42f,
                HubLength = 0.35f,
                ForkSpan = 0.56f,
                ForkThickness = 0.12f,
                CrossDiameter = 0.1f,
                JointAngleDeg = 25f,
                PhaseDeg = 0f,
                CenterLength = 0.65f,
                Segments = 32,
                Orientation = PlaneOrientation.XY,
                FlipFaces = false,
                Pivot = Vector3.zero,
            };

            public bool Equals(Params o) =>
                MeshName == o.MeshName && Type == o.Type &&
                Mathf.Approximately(InputBore, o.InputBore) &&
                Mathf.Approximately(OutputBore, o.OutputBore) &&
                Mathf.Approximately(HubDiameter, o.HubDiameter) &&
                Mathf.Approximately(HubLength, o.HubLength) &&
                Mathf.Approximately(ForkSpan, o.ForkSpan) &&
                Mathf.Approximately(ForkThickness, o.ForkThickness) &&
                Mathf.Approximately(CrossDiameter, o.CrossDiameter) &&
                Mathf.Approximately(JointAngleDeg, o.JointAngleDeg) &&
                Mathf.Approximately(PhaseDeg, o.PhaseDeg) &&
                Mathf.Approximately(CenterLength, o.CenterLength) &&
                Segments == o.Segments && Orientation == o.Orientation &&
                FlipFaces == o.FlipFaces && Pivot == o.Pivot;

            public override bool Equals(object x) => x is Params p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>派生諸元。</summary>
        public struct Info
        {
            public bool  Valid;
            public int   CrossCount;
            public float OverallLength;
        }

        public static Info GetInfo(Params p)
        {
            bool valid = p.InputBore > 0f && p.OutputBore > 0f
                      && p.HubDiameter > Mathf.Max(p.InputBore, p.OutputBore)
                      && p.HubLength > 0f
                      && p.ForkSpan > p.CrossDiameter
                      && p.ForkThickness > 0f
                      && p.CrossDiameter > 0f && p.CrossDiameter < p.HubDiameter
                      && p.JointAngleDeg >= Params.AngleMin && p.JointAngleDeg <= Params.AngleMax;

            return new Info
            {
                Valid         = valid,
                CrossCount    = p.Type == UniversalJointType.DoubleCardan ? 2 : 1,
                OverallLength = p.HubLength * 2f
                              + (p.Type == UniversalJointType.DoubleCardan ? p.CenterLength : p.ForkSpan),
            };
        }

        public static MeshObject Generate(Params p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "UniversalJoint" : p.MeshName;
            if (!GetInfo(p).Valid) return new MeshObject(name);

            var m = new MeshObject(name);

            // ダブルは 1 節あたり半分の折れ角を持つ。
            float a = p.JointAngleDeg * (p.Type == UniversalJointType.DoubleCardan ? 0.5f : 1f);
            Vector3 input  = Quaternion.AngleAxis(-a * 0.5f, Vector3.up) * Vector3.left;
            Vector3 output = Quaternion.AngleAxis( a * 0.5f, Vector3.up) * Vector3.right;

            if (p.Type == UniversalJointType.SingleCardan)
            {
                AddJoint(m, Vector3.zero, input, output, p, p.InputBore, p.OutputBore, p.PhaseDeg);
            }
            else
            {
                float d = p.CenterLength * 0.5f;
                Vector3 mid = Vector3.right;
                AddJoint(m, new Vector3(-d, 0f, 0f),  input,  mid,    p, p.InputBore,  p.InputBore,  p.PhaseDeg);
                AddJoint(m, new Vector3( d, 0f, 0f), -mid,    output, p, p.OutputBore, p.OutputBore, p.PhaseDeg + 90f);
                AddRing(m, Vector3.zero, Vector3.right, p.HubDiameter * 0.42f,
                        Mathf.Min(p.InputBore, p.OutputBore) * 0.5f, p.CenterLength, p.Segments);
            }

            GearDiskBuilder.ApplyOrientation(m, p.Orientation);
            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(m);
            PrimitiveMeshPostProcess.ApplyPivotOffset(m, p.Pivot);
            PrimitiveMeshPostProcess.SortVerticesCanonical(m);
            m.InvalidatePositionCache();
            return m;
        }

        /// <summary>1 節ぶん（ハブ 2・フォーク 4・十字軸）を中心 c に置く。</summary>
        private static void AddJoint(MeshObject m, Vector3 c, Vector3 a, Vector3 b,
                                     Params p, float boreA, float boreB, float phase)
        {
            float off = p.ForkSpan * 0.5f + p.HubLength * 0.5f;
            AddRing(m, c + a * off, a, p.HubDiameter * 0.5f, boreA * 0.5f, p.HubLength, p.Segments);
            AddRing(m, c + b * off, b, p.HubDiameter * 0.5f, boreB * 0.5f, p.HubLength, p.Segments);

            Vector3 fa = Quaternion.AngleAxis(phase,        a) * Vector3.up;
            Vector3 fb = Quaternion.AngleAxis(phase + 90f,  b) * Vector3.up;

            for (int s = -1; s <= 1; s += 2)
            {
                AddBox(m, c + a * p.ForkSpan * 0.25f + fa * (s * p.ForkSpan * 0.38f),
                       Quaternion.LookRotation(a, fa),
                       new Vector3(p.ForkThickness, p.ForkThickness, p.ForkSpan * 0.55f));
                AddBox(m, c + b * p.ForkSpan * 0.25f + fb * (s * p.ForkSpan * 0.38f),
                       Quaternion.LookRotation(b, fb),
                       new Vector3(p.ForkThickness, p.ForkThickness, p.ForkSpan * 0.55f));
            }

            AddCylinder(m, c, fa, p.CrossDiameter * 0.5f, p.ForkSpan, p.Segments);
            AddCylinder(m, c, fb, p.CrossDiameter * 0.5f, p.ForkSpan, p.Segments);

            foreach (var v in new[] { fa, -fa, fb, -fb })
                AddCylinder(m, c + v * p.ForkSpan * 0.48f, v,
                            p.CrossDiameter * 0.72f, p.ForkThickness, p.Segments);
        }

        private static void AddRing(MeshObject m, Vector3 c, Vector3 axis,
                                    float ro, float ri, float len, int n)
        {
            n = Mathf.Clamp(n, Params.SegmentsMin, Params.SegmentsMax);
            var o = new Vector2[n];
            var h = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float t = 2f * Mathf.PI * i / n;
                var dir = new Vector2(Mathf.Cos(t), Mathf.Sin(t));
                o[i] = dir * ro;
                h[i] = dir * ri;
            }
            var x = GearLoftBuilder.Build("Hub",
                new[] { new GearLoftSection(-len * 0.5f, o, h), new GearLoftSection(len * 0.5f, o, h) },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);
            Transform(x, c, Quaternion.FromToRotation(Vector3.forward, axis.normalized));
            Append(m, x);
        }

        private static void AddCylinder(MeshObject m, Vector3 c, Vector3 axis, float r, float len, int n)
        {
            var cp = CylinderMeshGenerator.CylinderParams.Default;
            cp.MeshName       = "Cross";
            cp.RadiusTop      = r;
            cp.RadiusBottom   = r;
            cp.Height         = len;
            cp.RadialSegments = Mathf.Clamp(n, 12, 48);
            cp.HeightSegments = 1;
            var x = CylinderMeshGenerator.Generate(cp);
            Transform(x, c, Quaternion.FromToRotation(Vector3.up, axis.normalized));
            Append(m, x);
        }

        private static void AddBox(MeshObject m, Vector3 c, Quaternion q, Vector3 size)
        {
            var o = new[]
            {
                new Vector2(-size.x * 0.5f, -size.y * 0.5f),
                new Vector2( size.x * 0.5f, -size.y * 0.5f),
                new Vector2( size.x * 0.5f,  size.y * 0.5f),
                new Vector2(-size.x * 0.5f,  size.y * 0.5f),
            };
            var x = GearLoftBuilder.Build("Fork",
                new[] { new GearLoftSection(-size.z * 0.5f, o), new GearLoftSection(size.z * 0.5f, o) },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);
            Transform(x, c, q);
            Append(m, x);
        }

        /// <summary>部品を回して置く。法線も一緒に回す（連結時に法線を写すため）。</summary>
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
