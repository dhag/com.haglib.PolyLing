// CylinderMeshGenerator.cs
// シリンダーメッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class CylinderMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct CylinderParams : IEquatable<CylinderParams>
        {
            public string MeshName;
            public float RadiusTop, RadiusBottom;
            public float Height;
            public int RadialSegments, HeightSegments;
            public bool CapTop, CapBottom;
            public float EdgeRadius;
            public int EdgeSegments;
            public Vector3 Pivot;
            public float RotationX, RotationY;

            public static CylinderParams Default => new CylinderParams
            {
                MeshName       = "Cylinder",
                RadiusTop      = 0.5f, RadiusBottom = 0.5f,
                Height         = 2f,
                RadialSegments = 24,   HeightSegments = 4,
                CapTop         = true, CapBottom      = true,
                EdgeRadius     = 0f,   EdgeSegments   = 4,
                Pivot          = Vector3.zero,
                RotationX      = 20f,  RotationY      = 30f,
            };

            public bool Equals(CylinderParams o) =>
                MeshName == o.MeshName &&
                Mathf.Approximately(RadiusTop,    o.RadiusTop)    &&
                Mathf.Approximately(RadiusBottom, o.RadiusBottom) &&
                Mathf.Approximately(Height,       o.Height)       &&
                RadialSegments == o.RadialSegments &&
                HeightSegments == o.HeightSegments &&
                CapTop == o.CapTop && CapBottom == o.CapBottom &&
                Mathf.Approximately(EdgeRadius, o.EdgeRadius) &&
                EdgeSegments == o.EdgeSegments &&
                Pivot == o.Pivot &&
                Mathf.Approximately(RotationX, o.RotationX) &&
                Mathf.Approximately(RotationY, o.RotationY);

            public override bool Equals(object obj) => obj is CylinderParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 生成
        // ================================================================
        public static MeshObject Generate(CylinderParams p)
        {
            var md = new MeshObject(p.MeshName);
            Vector3 pivotOffset = new Vector3(0, p.Pivot.y * p.Height, 0);
            if (p.EdgeRadius > 0 && (p.CapTop || p.CapBottom))
                GenerateRounded(md, p, pivotOffset);
            else
                GenerateSimple(md, p, pivotOffset);
            return md;
        }

        private static void GenerateSimple(MeshObject md, CylinderParams p, Vector3 pivot)
        {
            float halfH = p.Height * 0.5f;
            int cols = p.RadialSegments + 1;
            int ssi = md.VertexCount;
            for (int h = 0; h <= p.HeightSegments; h++)
            {
                float t = (float)h / p.HeightSegments;
                float y = halfH - t * p.Height;
                float radius = Mathf.Lerp(p.RadiusTop, p.RadiusBottom, t);
                float slope = (p.RadiusBottom - p.RadiusTop) / p.Height;
                for (int r = 0; r <= p.RadialSegments; r++)
                {
                    float a = r * 2f * Mathf.PI / p.RadialSegments;
                    float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    Vector3 n = new Vector3(cos, slope, sin).normalized;
                    md.Vertices.Add(new Vertex(new Vector3(cos*radius, y, sin*radius)-pivot, new Vector2((float)r/p.RadialSegments, 1f-t), n));
                }
            }
            for (int h = 0; h < p.HeightSegments; h++)
                for (int r = 0; r < p.RadialSegments; r++)
                { int i0 = ssi+h*cols+r; md.AddQuad(i0, i0+1, i0+cols+1, i0+cols); }

            if (p.CapTop    && p.RadiusTop    > 0) AddCapSimple(md, p,  halfH,  p.RadiusTop,    true,  pivot);
            if (p.CapBottom && p.RadiusBottom > 0) AddCapSimple(md, p, -halfH,  p.RadiusBottom, false, pivot);
        }

        private static void AddCapSimple(MeshObject md, CylinderParams p, float y, float radius, bool top, Vector3 pivot)
        {
            int ci = md.VertexCount;
            Vector3 n = top ? Vector3.up : Vector3.down;
            md.Vertices.Add(new Vertex(new Vector3(0, y, 0)-pivot, new Vector2(0.5f,0.5f), n));
            for (int r = 0; r <= p.RadialSegments; r++)
            {
                float a = r * 2f * Mathf.PI / p.RadialSegments;
                float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                md.Vertices.Add(new Vertex(new Vector3(cos*radius, y, sin*radius)-pivot, new Vector2(cos*0.5f+0.5f, sin*0.5f+0.5f), n));
            }
            for (int r = 0; r < p.RadialSegments; r++)
            {
                int v0 = ci, v1 = ci+1+r, v2 = ci+1+r+1;
                if (top) md.AddTriangle(v0, v2, v1); else md.AddTriangle(v0, v1, v2);
            }
        }

        private static void GenerateRounded(MeshObject md, CylinderParams p, Vector3 pivot)
        {
            float halfH = p.Height * 0.5f;
            float er = p.EdgeRadius;
            int eseg = p.EdgeSegments;
            float innerH = halfH - er;
            int cols = p.RadialSegments + 1;

            // 上部角丸め
            if (p.CapTop && p.RadiusTop > 0 && er > 0)
            {
                int tsi = md.VertexCount;
                float tcr = p.RadiusTop - er;
                for (int e = 0; e <= eseg; e++)
                {
                    float a = (float)e / eseg * Mathf.PI * 0.5f;
                    float y = innerH + Mathf.Sin(a) * er;
                    float cr = tcr + Mathf.Cos(a) * er;
                    for (int r = 0; r <= p.RadialSegments; r++)
                    {
                        float ra = r * 2f * Mathf.PI / p.RadialSegments;
                        float cos = Mathf.Cos(ra), sin = Mathf.Sin(ra);
                        Vector3 n = new Vector3(cos*Mathf.Cos(a), Mathf.Sin(a), sin*Mathf.Cos(a)).normalized;
                        float v = 1f - (float)e/eseg*(er/p.Height)*0.5f;
                        md.Vertices.Add(new Vertex(new Vector3(cos*cr, y, sin*cr)-pivot, new Vector2((float)r/p.RadialSegments, v), n));
                    }
                }
                for (int e = 0; e < eseg; e++)
                    for (int r = 0; r < p.RadialSegments; r++)
                    { int i0 = tsi+e*cols+r; md.AddQuad(i0, i0+cols, i0+cols+1, i0+1); }
            }

            // 側面
            int ssi = md.VertexCount;
            float sTop    = (p.CapTop    && p.RadiusTop    > 0 && er > 0) ?  innerH : halfH;
            float sBottom = (p.CapBottom && p.RadiusBottom > 0 && er > 0) ? -innerH : -halfH;
            float sHeight = sTop - sBottom;
            for (int h = 0; h <= p.HeightSegments; h++)
            {
                float t = (float)h / p.HeightSegments;
                float y = sTop - t * sHeight;
                float radius = Mathf.Lerp(p.RadiusTop, p.RadiusBottom, t);
                float slope = (p.RadiusBottom - p.RadiusTop) / p.Height;
                float vTop    = (p.CapTop    && p.RadiusTop    > 0 && er > 0) ? 1f - er/p.Height*0.5f : 1f;
                float vBottom = (p.CapBottom && p.RadiusBottom > 0 && er > 0) ?      er/p.Height*0.5f : 0f;
                for (int r = 0; r <= p.RadialSegments; r++)
                {
                    float a = r * 2f * Mathf.PI / p.RadialSegments;
                    float cos = Mathf.Cos(a), sin = Mathf.Sin(a);
                    Vector3 n = new Vector3(cos, slope, sin).normalized;
                    md.Vertices.Add(new Vertex(new Vector3(cos*radius, y, sin*radius)-pivot, new Vector2((float)r/p.RadialSegments, Mathf.Lerp(vTop, vBottom, t)), n));
                }
            }
            for (int h = 0; h < p.HeightSegments; h++)
                for (int r = 0; r < p.RadialSegments; r++)
                { int i0 = ssi+h*cols+r; md.AddQuad(i0, i0+1, i0+cols+1, i0+cols); }

            // 下部角丸め
            if (p.CapBottom && p.RadiusBottom > 0 && er > 0)
            {
                int bsi = md.VertexCount;
                float bcr = p.RadiusBottom - er;
                for (int e = 0; e <= eseg; e++)
                {
                    float a = (float)e / eseg * Mathf.PI * 0.5f;
                    float y = -innerH - Mathf.Sin(a) * er;
                    float cr = bcr + Mathf.Cos(a) * er;
                    for (int r = 0; r <= p.RadialSegments; r++)
                    {
                        float ra = r * 2f * Mathf.PI / p.RadialSegments;
                        float cos = Mathf.Cos(ra), sin = Mathf.Sin(ra);
                        Vector3 n = new Vector3(cos*Mathf.Cos(a), -Mathf.Sin(a), sin*Mathf.Cos(a)).normalized;
                        float v = (float)e/eseg*(er/p.Height)*0.5f;
                        md.Vertices.Add(new Vertex(new Vector3(cos*cr, y, sin*cr)-pivot, new Vector2((float)r/p.RadialSegments, v), n));
                    }
                }
                for (int e = 0; e < eseg; e++)
                    for (int r = 0; r < p.RadialSegments; r++)
                    { int i0 = bsi+e*cols+r; md.AddQuad(i0, i0+1, i0+cols+1, i0+cols); }
            }

            // キャップ
            if (p.CapTop    && p.RadiusTop    > 0) AddCapSimple(md, p,  halfH, er > 0 ? p.RadiusTop    - er : p.RadiusTop,    true,  pivot);
            if (p.CapBottom && p.RadiusBottom > 0) AddCapSimple(md, p, -halfH, er > 0 ? p.RadiusBottom - er : p.RadiusBottom, false, pivot);
        }
    }
}
