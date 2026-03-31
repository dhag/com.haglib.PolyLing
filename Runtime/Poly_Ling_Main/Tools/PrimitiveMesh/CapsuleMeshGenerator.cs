// CapsuleMeshGenerator.cs
// カプセルメッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class CapsuleMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct CapsuleParams : IEquatable<CapsuleParams>
        {
            public string MeshName;
            public float RadiusTop, RadiusBottom;
            public float Height;
            public int RadialSegments, HeightSegments, CapSegments;
            public Vector3 Pivot;
            public float RotationX, RotationY;

            public static CapsuleParams Default => new CapsuleParams
            {
                MeshName       = "Capsule",
                RadiusTop      = 0.5f, RadiusBottom = 0.5f,
                Height         = 2f,
                RadialSegments = 24,   HeightSegments = 4, CapSegments = 8,
                Pivot          = Vector3.zero,
                RotationX      = 20f,  RotationY      = 30f,
            };

            public bool Equals(CapsuleParams o) =>
                MeshName == o.MeshName &&
                Mathf.Approximately(RadiusTop,    o.RadiusTop)    &&
                Mathf.Approximately(RadiusBottom, o.RadiusBottom) &&
                Mathf.Approximately(Height,       o.Height)       &&
                RadialSegments == o.RadialSegments &&
                HeightSegments == o.HeightSegments &&
                CapSegments    == o.CapSegments    &&
                Pivot == o.Pivot &&
                Mathf.Approximately(RotationX, o.RotationX) &&
                Mathf.Approximately(RotationY, o.RotationY);

            public override bool Equals(object obj) => obj is CapsuleParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 生成
        // ================================================================
        public static MeshObject Generate(CapsuleParams p)
        {
            var md = new MeshObject(p.MeshName);

            float cylH = Mathf.Max(0f, p.Height - p.RadiusTop - p.RadiusBottom);
            float halfH = p.Height * 0.5f;
            Vector3 pivot = new Vector3(0, p.Pivot.y * p.Height, 0);
            float cylTop    =  halfH - p.RadiusTop;
            float cylBottom = -halfH + p.RadiusBottom;
            float uvTop = p.RadiusTop    / p.Height;
            float uvBot = p.RadiusBottom / p.Height;
            float uvCyl = cylH           / p.Height;
            int radSeg = p.RadialSegments, capSeg = p.CapSegments, hSeg = p.HeightSegments;
            int cols = radSeg + 1;

            // 上半球
            int tsi = md.VertexCount;
            for (int lat = 0; lat <= capSeg; lat++)
            {
                float theta = lat * Mathf.PI * 0.5f / capSeg;
                float sinT = Mathf.Sin(theta), cosT = Mathf.Cos(theta);
                for (int lon = 0; lon <= radSeg; lon++)
                {
                    float phi = lon * 2f * Mathf.PI / radSeg;
                    Vector3 n = new Vector3(Mathf.Cos(phi)*sinT, cosT, Mathf.Sin(phi)*sinT);
                    Vector3 pos = n * p.RadiusTop + new Vector3(0, cylTop, 0) - pivot;
                    float v = 1f - (float)lat/capSeg * uvTop;
                    md.Vertices.Add(new Vertex(pos, new Vector2((float)lon/radSeg, v), n));
                }
            }

            // 円筒部
            int csi = md.VertexCount;
            for (int h = 0; h <= hSeg; h++)
            {
                float t = (float)h / hSeg;
                float y = cylTop - t * cylH;
                float radius = Mathf.Lerp(p.RadiusTop, p.RadiusBottom, t);
                float slope = (p.RadiusBottom - p.RadiusTop) / (cylH > 0 ? cylH : 1f);
                for (int lon = 0; lon <= radSeg; lon++)
                {
                    float phi = lon * 2f * Mathf.PI / radSeg;
                    float cos = Mathf.Cos(phi), sin = Mathf.Sin(phi);
                    Vector3 n = new Vector3(cos, slope, sin).normalized;
                    float v = 1f - uvTop - t * uvCyl;
                    md.Vertices.Add(new Vertex(new Vector3(cos*radius, y, sin*radius)-pivot, new Vector2((float)lon/radSeg, v), n));
                }
            }

            // 下半球
            int bsi = md.VertexCount;
            for (int lat = 0; lat <= capSeg; lat++)
            {
                float theta = Mathf.PI * 0.5f + lat * Mathf.PI * 0.5f / capSeg;
                float sinT = Mathf.Sin(theta), cosT = Mathf.Cos(theta);
                for (int lon = 0; lon <= radSeg; lon++)
                {
                    float phi = lon * 2f * Mathf.PI / radSeg;
                    Vector3 n = new Vector3(Mathf.Cos(phi)*sinT, cosT, Mathf.Sin(phi)*sinT);
                    Vector3 pos = n * p.RadiusBottom + new Vector3(0, cylBottom, 0) - pivot;
                    float v = uvBot - (float)lat / capSeg * uvBot;
                    md.Vertices.Add(new Vertex(pos, new Vector2((float)lon/radSeg, v), n));
                }
            }

            // 面
            for (int lat = 0; lat < capSeg; lat++)
                for (int lon = 0; lon < radSeg; lon++)
                { int i0 = tsi+lat*cols+lon; md.AddQuad(i0, i0+1, i0+cols+1, i0+cols); }
            for (int h = 0; h < hSeg; h++)
                for (int lon = 0; lon < radSeg; lon++)
                { int i0 = csi+h*cols+lon; md.AddQuad(i0, i0+1, i0+cols+1, i0+cols); }
            for (int lat = 0; lat < capSeg; lat++)
                for (int lon = 0; lon < radSeg; lon++)
                { int i0 = bsi+lat*cols+lon; md.AddQuad(i0, i0+1, i0+cols+1, i0+cols); }

            return md;
        }
    }
}
