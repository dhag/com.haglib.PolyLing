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
            // ── 値域 ─────────────────────────────────────────────────
            // PLParam 属性と図形生成パネルの行ヘルパの双方がここを参照する。

            /// <summary>上面・底面の半径の下限・上限</summary>
            public const float RadiusMin = 0f;
            public const float RadiusMax = 5f;

            /// <summary>高さの下限・上限</summary>
            public const float HeightMin = 0.1f;
            public const float HeightMax = 10f;

            /// <summary>円周方向の分割数の下限・上限</summary>
            public const int RadialSegmentsMin = 3;
            public const int RadialSegmentsMax = 48;

            /// <summary>高さ方向の分割数の下限・上限</summary>
            public const int HeightSegmentsMin = 1;
            public const int HeightSegmentsMax = 16;

            /// <summary>縁の丸めの下限。上限は高さと半径から決まるので定数にできない</summary>
            public const float EdgeRadiusMin = 0f;

            /// <summary>縁の丸めの分割数の下限・上限</summary>
            public const int EdgeSegmentsMin = 1;
            public const int EdgeSegmentsMax = 16;

            [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
            public string MeshName;
            [PLParam(TextKey = "RadiusTop", Description = "上面の半径。0 で円錐になる", Min = RadiusMin, Max = RadiusMax)]
            public float RadiusTop;
            [PLParam(TextKey = "RadiusBottom", Description = "底面の半径。0 で円錐になる", Min = RadiusMin, Max = RadiusMax)]
            public float RadiusBottom;
            [PLParam(TextKey = "Height", Description = "高さ", Min = HeightMin, Max = HeightMax)]
            public float Height;
            [PLParam(TextKey = "Radial", Description = "円周方向の分割数", Min = RadialSegmentsMin,
                     Max = RadialSegmentsMax, Step = 1)]
            public int RadialSegments;
            [PLParam(TextKey = "Lateral", Description = "高さ方向の分割数", Min = HeightSegmentsMin,
                     Max = HeightSegmentsMax, Step = 1)]
            public int HeightSegments;
            [PLParam(TextKey = "CapTop", Description = "上面にフタを張る")]
            public bool CapTop;
            [PLParam(TextKey = "CapBottom", Description = "底面にフタを張る")]
            public bool CapBottom;
            [PLParam(TextKey = "EdgeRadius", Description = "上下の縁の丸め半径。0 で丸めなし。上限は高さの半分と半径のうち小さい方",
                     Min = EdgeRadiusMin)]
            public float EdgeRadius;
            [PLParam(TextKey = "EdgeSeg", Description = "縁の丸めの分割数", Min = EdgeSegmentsMin,
                     Max = EdgeSegmentsMax, Step = 1)]
            public int EdgeSegments;
            [PLParam(TextKey = "PivotOffset", Description = "AABB サイズ基準のピボット。生成後に -Pivot × サイズ だけ平行移動する",
                     Min = PrimitiveMeshPostProcess.PivotMin, Max = PrimitiveMeshPostProcess.PivotMax)]
            public Vector3 Pivot;
            [PLParam(Ignore = true, Description = "プレビューの視点角。形状には影響しない")]
            public float RotationX;
            [PLParam(Ignore = true, Description = "プレビューの視点角。形状には影響しない")]
            public float RotationY;

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
            PrimitiveMeshPostProcess.SortVerticesCanonical(md);
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
