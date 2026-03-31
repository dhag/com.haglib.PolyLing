// PlaneMeshGenerator.cs
// プレーンメッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public enum PlaneOrientation { XY, XZ, YZ }

    public static class PlaneMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct PlaneParams : IEquatable<PlaneParams>
        {
            public string MeshName;
            public float Width, Height;
            public int WidthSegments, HeightSegments;
            public bool DoubleSided;
            public PlaneOrientation Orientation;
            public Vector3 Pivot;
            public float RotationX, RotationY;

            public static PlaneParams Default => new PlaneParams
            {
                MeshName       = "Plane",
                Width          = 1f, Height = 1f,
                WidthSegments  = 4,  HeightSegments = 4,
                DoubleSided    = false,
                Orientation    = PlaneOrientation.XZ,
                Pivot          = Vector3.zero,
                RotationX      = 20f, RotationY = 30f,
            };

            public bool Equals(PlaneParams o) =>
                MeshName == o.MeshName &&
                Mathf.Approximately(Width,  o.Width)  &&
                Mathf.Approximately(Height, o.Height) &&
                WidthSegments  == o.WidthSegments  &&
                HeightSegments == o.HeightSegments &&
                DoubleSided == o.DoubleSided &&
                Orientation == o.Orientation &&
                Pivot == o.Pivot &&
                Mathf.Approximately(RotationX, o.RotationX) &&
                Mathf.Approximately(RotationY, o.RotationY);

            public override bool Equals(object obj) => obj is PlaneParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 生成
        // ================================================================
        public static MeshObject Generate(PlaneParams p)
        {
            var md = new MeshObject(p.MeshName);
            Vector3 pivotOffset = new Vector3(p.Pivot.x * p.Width, p.Pivot.y * p.Height, 0);
            AddFace(md, p, pivotOffset, false);
            if (p.DoubleSided) AddFace(md, p, pivotOffset, true);
            return md;
        }

        private static void AddFace(MeshObject md, PlaneParams p, Vector3 pivotOffset, bool flip)
        {
            int start = md.VertexCount;
            Vector3 normal;
            switch (p.Orientation)
            {
                case PlaneOrientation.XY: normal = flip ? Vector3.back    : Vector3.forward; break;
                case PlaneOrientation.XZ: normal = flip ? Vector3.down    : Vector3.up;      break;
                case PlaneOrientation.YZ: normal = flip ? Vector3.left    : Vector3.right;   break;
                default:                  normal = Vector3.up; break;
            }

            for (int h = 0; h <= p.HeightSegments; h++)
            for (int w = 0; w <= p.WidthSegments; w++)
            {
                float u = (float)w / p.WidthSegments;
                float v = (float)h / p.HeightSegments;
                float x = (u - 0.5f) * p.Width;
                float y = (v - 0.5f) * p.Height;
                Vector3 pos;
                switch (p.Orientation)
                {
                    case PlaneOrientation.XY: pos = new Vector3(x - pivotOffset.x, y - pivotOffset.y, 0);         break;
                    case PlaneOrientation.XZ: pos = new Vector3(x - pivotOffset.x, 0, -y + pivotOffset.y);        break;
                    case PlaneOrientation.YZ: pos = new Vector3(0, y - pivotOffset.y, -x + pivotOffset.x);        break;
                    default:                  pos = new Vector3(x, 0, -y); break;
                }
                md.Vertices.Add(new Vertex(pos, new Vector2(flip ? 1f-u : u, v), normal));
            }

            int cols = p.WidthSegments + 1;
            for (int h = 0; h < p.HeightSegments; h++)
            for (int w = 0; w < p.WidthSegments; w++)
            {
                int i0 = start + h * cols + w;
                if (flip) md.AddQuad(i0, i0+1,      i0+cols+1, i0+cols);
                else       md.AddQuad(i0, i0+cols,   i0+cols+1, i0+1);
            }
        }
    }
}
