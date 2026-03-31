// PyramidMeshGenerator.cs
// 角錐メッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.PrimitiveMesh
{
    public static class PyramidMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct PyramidParams : IEquatable<PyramidParams>
        {
            public string MeshName;
            public float BaseRadius;
            public float Height;
            public int Sides;
            public float ApexOffset;
            public bool CapBottom;
            public Vector3 Pivot;
            public float RotationX, RotationY;

            public static PyramidParams Default => new PyramidParams
            {
                MeshName    = "Pyramid",
                BaseRadius  = 0.5f,
                Height      = 1f,
                Sides       = 4,
                ApexOffset  = 0f,
                CapBottom   = true,
                Pivot       = Vector3.zero,
                RotationX   = 20f, RotationY = 30f,
            };

            public bool Equals(PyramidParams o) =>
                MeshName == o.MeshName &&
                Mathf.Approximately(BaseRadius,  o.BaseRadius)  &&
                Mathf.Approximately(Height,      o.Height)      &&
                Sides == o.Sides &&
                Mathf.Approximately(ApexOffset,  o.ApexOffset)  &&
                CapBottom == o.CapBottom &&
                Pivot == o.Pivot &&
                Mathf.Approximately(RotationX, o.RotationX) &&
                Mathf.Approximately(RotationY, o.RotationY);

            public override bool Equals(object obj) => obj is PyramidParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 生成
        // ================================================================
        public static MeshObject Generate(PyramidParams p)
        {
            var md = new MeshObject(p.MeshName);
            float halfH = p.Height * 0.5f;
            Vector3 pivotOffset = new Vector3(0, p.Pivot.y * p.Height, 0);
            Vector3 apex = new Vector3(p.ApexOffset * p.BaseRadius, halfH, 0) - pivotOffset;

            var base3 = new Vector3[p.Sides];
            for (int i = 0; i < p.Sides; i++)
            {
                float a = i * 2f * Mathf.PI / p.Sides;
                base3[i] = new Vector3(Mathf.Cos(a)*p.BaseRadius, -halfH, Mathf.Sin(a)*p.BaseRadius) - pivotOffset;
            }

            // 側面
            for (int i = 0; i < p.Sides; i++)
            {
                int si = md.VertexCount;
                Vector3 p0 = base3[i], p1 = base3[(i+1) % p.Sides];
                Vector3 n = NormalHelper.CalculateFaceNormal(p0, p1, apex);
                md.Vertices.Add(new Vertex(p0,   new Vector2(0,   0), n));
                md.Vertices.Add(new Vertex(p1,   new Vector2(1,   0), n));
                md.Vertices.Add(new Vertex(apex, new Vector2(0.5f,1), n));
                md.AddTriangle(si, si+2, si+1);
            }

            // 底面キャップ
            if (p.CapBottom)
            {
                int ci = md.VertexCount;
                md.Vertices.Add(new Vertex(new Vector3(0,-halfH,0)-pivotOffset, new Vector2(0.5f,0.5f), Vector3.down));
                for (int i = 0; i < p.Sides; i++)
                {
                    float a = i * 2f * Mathf.PI / p.Sides;
                    Vector2 uv = new Vector2(Mathf.Cos(a)*0.5f+0.5f, Mathf.Sin(a)*0.5f+0.5f);
                    md.Vertices.Add(new Vertex(base3[i], uv, Vector3.down));
                }
                for (int i = 0; i < p.Sides; i++)
                    md.AddTriangle(ci, ci+1+i, ci+1+(i+1)%p.Sides);
            }

            return md;
        }
    }
}
