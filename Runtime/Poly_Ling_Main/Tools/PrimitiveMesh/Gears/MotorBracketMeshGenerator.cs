// MotorBracketMeshGenerator.cs
// モータ取付ブラケット（丸クランプ / 面板 / L 形 / U 形）。
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ に配置
//
// 【作り方】
//   板とリングは「閉じた断面を Z へ積む」形（GearLoftBuilder）で作り、連結する。
//   穴は穴あきリングを差し込むことで表現する（真のブーリアンは行わない）。
//
// 【生成経路】
//   パネルからは呼ばない。CreateMotorBracketCommand → PrimitiveMeshFactory がここを呼ぶ。
//   ローカル空間でピボット適用まで。平行移動・回転・拡大は Factory が行う。

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>ブラケットの形。</summary>
    public enum MotorBracketType { RoundClamp, FacePlate, LBracket, UBracket }

    public static class MotorBracketMeshGenerator
    {
        [Serializable]
        public struct Params : IEquatable<Params>
        {
            public const float SizeMin = 0.001f, SizeMax = 10f;
            public const int SegmentsMin = 12, SegmentsMax = 128;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "MotorBracketType", Description = "ブラケットの形")]
            public MotorBracketType Type;

            [PLParam(TextKey = "MotorDiameter", Description = "モータ胴の直径（丸クランプ）", Min = SizeMin, Max = SizeMax)]
            public float MotorDiameter;
            [PLParam(TextKey = "MotorWidth", Description = "モータ面の幅", Min = SizeMin, Max = SizeMax)]
            public float MotorWidth;
            [PLParam(TextKey = "MotorHeight", Description = "モータ面の高さ", Min = SizeMin, Max = SizeMax)]
            public float MotorHeight;
            [PLParam(TextKey = "MotorBracketLength", Description = "台座の奥行き", Min = SizeMin, Max = SizeMax)]
            public float Length;
            [PLParam(TextKey = "MotorBracketThickness", Description = "板厚", Min = SizeMin, Max = SizeMax)]
            public float Thickness;

            [PLParam(TextKey = "MotorShaftClearance", Description = "軸の逃げ穴の直径", Min = SizeMin, Max = SizeMax)]
            public float ShaftClearance;
            [PLParam(TextKey = "MotorHolePitchX", Description = "モータ取付穴の横ピッチ", Min = SizeMin, Max = SizeMax)]
            public float HolePitchX;
            [PLParam(TextKey = "MotorHolePitchY", Description = "モータ取付穴の縦ピッチ", Min = SizeMin, Max = SizeMax)]
            public float HolePitchY;
            [PLParam(TextKey = "MotorMountHole", Description = "モータ取付穴の直径", Min = SizeMin, Max = SizeMax)]
            public float MountHoleDiameter;
            [PLParam(TextKey = "MotorBaseHole", Description = "台座取付穴の直径", Min = SizeMin, Max = SizeMax)]
            public float BaseHoleDiameter;
            [PLParam(TextKey = "MotorBracketSegments", Description = "円周方向の分割数",
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
                MeshName = "MotorBracket",
                Type = MotorBracketType.LBracket,
                MotorDiameter = 0.6f,
                MotorWidth = 0.72f,
                MotorHeight = 0.72f,
                Length = 1.0f,
                Thickness = 0.12f,
                ShaftClearance = 0.22f,
                HolePitchX = 0.5f,
                HolePitchY = 0.5f,
                MountHoleDiameter = 0.08f,
                BaseHoleDiameter = 0.1f,
                Segments = 48,
                Orientation = PlaneOrientation.XY,
                FlipFaces = false,
                Pivot = Vector3.zero,
            };

            public bool Equals(Params o) =>
                MeshName == o.MeshName && Type == o.Type &&
                Mathf.Approximately(MotorDiameter, o.MotorDiameter) &&
                Mathf.Approximately(MotorWidth, o.MotorWidth) &&
                Mathf.Approximately(MotorHeight, o.MotorHeight) &&
                Mathf.Approximately(Length, o.Length) &&
                Mathf.Approximately(Thickness, o.Thickness) &&
                Mathf.Approximately(ShaftClearance, o.ShaftClearance) &&
                Mathf.Approximately(HolePitchX, o.HolePitchX) &&
                Mathf.Approximately(HolePitchY, o.HolePitchY) &&
                Mathf.Approximately(MountHoleDiameter, o.MountHoleDiameter) &&
                Mathf.Approximately(BaseHoleDiameter, o.BaseHoleDiameter) &&
                Segments == o.Segments && Orientation == o.Orientation &&
                FlipFaces == o.FlipFaces && Pivot == o.Pivot;

            public override bool Equals(object x) => x is Params p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        /// <summary>派生諸元。</summary>
        public struct Info
        {
            public bool Valid;
            public int  MotorHoleCount;
            public int  BaseHoleCount;
        }

        public static Info GetInfo(Params p) => new Info
        {
            Valid = p.MotorDiameter > 0f
                 && p.MotorWidth  > p.ShaftClearance
                 && p.MotorHeight > p.ShaftClearance
                 && p.Length > p.Thickness
                 && p.Thickness > 0f
                 && p.ShaftClearance > 0f
                 && p.MountHoleDiameter > 0f
                 && p.BaseHoleDiameter > 0f
                 && p.HolePitchX > p.MountHoleDiameter
                 && p.HolePitchY > p.MountHoleDiameter,
            MotorHoleCount = p.Type == MotorBracketType.RoundClamp ? 1 : 4,
            BaseHoleCount  = p.Type == MotorBracketType.FacePlate ? 0
                           : p.Type == MotorBracketType.RoundClamp ? 2 : 4,
        };

        public static MeshObject Generate(Params p)
        {
            string name = string.IsNullOrEmpty(p.MeshName) ? "MotorBracket" : p.MeshName;
            if (!GetInfo(p).Valid) return new MeshObject(name);

            var m = new MeshObject(name);
            int n = Mathf.Clamp(p.Segments, Params.SegmentsMin, Params.SegmentsMax);
            float t = p.Thickness;

            if (p.Type == MotorBracketType.RoundClamp)
            {
                Append(m, Ring("MotorClamp", p.MotorDiameter * 0.5f + t, p.MotorDiameter * 0.5f,
                               p.Length, n, Vector2.zero, 0f));

                float x = p.MotorDiameter * 0.5f + t * 1.5f;
                Append(m, Box(-x, x, -p.MotorDiameter * 0.5f - t, p.MotorDiameter * 0.5f + t,
                              t, -p.Length * 0.5f));
                AddBaseHoles(m, p, n,
                    new[] { new Vector2(-p.HolePitchX * 0.5f, 0f), new Vector2(p.HolePitchX * 0.5f, 0f) },
                    -p.Length * 0.5f);
            }
            else
            {
                AddFacePlate(m, p, n, 0f);

                if (p.Type == MotorBracketType.LBracket || p.Type == MotorBracketType.UBracket)
                {
                    float y = -p.MotorHeight * 0.5f - t * 0.5f;
                    Append(m, Box(-p.MotorWidth * 0.6f, p.MotorWidth * 0.6f,
                                  -p.Length * 0.5f, p.Length * 0.5f, t, y));
                    AddBaseHoles(m, p, n, new[]
                    {
                        new Vector2(-p.HolePitchX * 0.5f, -p.Length * 0.25f),
                        new Vector2( p.HolePitchX * 0.5f, -p.Length * 0.25f),
                        new Vector2(-p.HolePitchX * 0.5f,  p.Length * 0.25f),
                        new Vector2( p.HolePitchX * 0.5f,  p.Length * 0.25f),
                    }, y);

                    if (p.Type == MotorBracketType.UBracket)
                    {
                        float x = p.MotorWidth * 0.5f + t * 0.5f;
                        Append(m, Box(-p.MotorHeight * 0.5f, p.MotorHeight * 0.5f,
                                      -p.Length * 0.5f, p.Length * 0.5f, t, x));
                    }
                }
            }

            GearDiskBuilder.ApplyOrientation(m, p.Orientation);
            if (p.FlipFaces) PrimitiveMeshPostProcess.FlipFaces(m);
            PrimitiveMeshPostProcess.ApplyPivotOffset(m, p.Pivot);
            PrimitiveMeshPostProcess.SortVerticesCanonical(m);
            m.InvalidatePositionCache();
            return m;
        }

        /// <summary>モータが当たる面板。軸の逃げ穴と 4 隅の取付穴を入れる。</summary>
        private static void AddFacePlate(MeshObject m, Params p, int n, float z)
        {
            float w = p.MotorWidth  * 0.5f;
            float h = p.MotorHeight * 0.5f;

            Append(m, Box(-w, w, -h, h, p.Thickness, z));
            Append(m, Ring("ShaftOpening", p.ShaftClearance * 0.65f, p.ShaftClearance * 0.5f,
                           p.Thickness * 1.02f, n, Vector2.zero, z));

            float qx = p.HolePitchX * 0.5f;
            float qy = p.HolePitchY * 0.5f;
            foreach (var c in new[] { new Vector2(-qx, -qy), new Vector2(qx, -qy),
                                      new Vector2( qx,  qy), new Vector2(-qx, qy) })
                Append(m, Ring("MotorHole", p.MountHoleDiameter, p.MountHoleDiameter * 0.5f,
                               p.Thickness * 1.02f, n, c, z));
        }

        private static void AddBaseHoles(MeshObject m, Params p, int n, Vector2[] centers, float z)
        {
            foreach (var c in centers)
                Append(m, Ring("BaseHole", p.BaseHoleDiameter, p.BaseHoleDiameter * 0.5f,
                               p.Thickness * 1.02f, n, c, z));
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

        private static MeshObject Box(float x0, float x1, float y0, float y1, float d, float z)
        {
            var o = new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) };
            return GearLoftBuilder.Build("Plate",
                new[] { new GearLoftSection(z - d * 0.5f, o), new GearLoftSection(z + d * 0.5f, o) },
                GearLoftCapMode.Triangulate, PlaneOrientation.XY, false, Vector3.zero);
        }

        private static void Append(MeshObject a, MeshObject b) => MeshObjectAppendOps.Append(a, b, true);
    }
}
