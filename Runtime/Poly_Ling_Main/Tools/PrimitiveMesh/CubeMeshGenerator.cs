// CubeMeshGenerator.cs
// 角丸直方体メッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class CubeMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct CubeParams : IEquatable<CubeParams>
        {
            public string MeshName;
            public float WidthTop, DepthTop;
            public float WidthBottom, DepthBottom;
            public float Height;
            public float CornerRadius;
            public int CornerSegments;
            public Vector3Int Subdivisions;
            public Vector3 Pivot;
            public float RotationX, RotationY;
            public bool LinkTopBottom;
            public bool LinkWHD;

            public static CubeParams Default => new CubeParams
            {
                MeshName      = "RoundedCube",
                WidthTop      = 1f, DepthTop    = 1f,
                WidthBottom   = 1f, DepthBottom = 1f,
                Height        = 1f,
                CornerRadius  = 0.1f,
                CornerSegments = 4,
                Subdivisions  = Vector3Int.one,
                Pivot         = Vector3.zero,
                RotationX     = 20f, RotationY = 30f,
                LinkTopBottom = false, LinkWHD  = false,
            };

            public bool Equals(CubeParams o) =>
                MeshName == o.MeshName &&
                Mathf.Approximately(WidthTop,     o.WidthTop)     &&
                Mathf.Approximately(DepthTop,     o.DepthTop)     &&
                Mathf.Approximately(WidthBottom,  o.WidthBottom)  &&
                Mathf.Approximately(DepthBottom,  o.DepthBottom)  &&
                Mathf.Approximately(Height,       o.Height)       &&
                Mathf.Approximately(CornerRadius, o.CornerRadius) &&
                CornerSegments == o.CornerSegments &&
                Subdivisions == o.Subdivisions &&
                Pivot == o.Pivot &&
                Mathf.Approximately(RotationX, o.RotationX) &&
                Mathf.Approximately(RotationY, o.RotationY) &&
                LinkTopBottom == o.LinkTopBottom &&
                LinkWHD == o.LinkWHD;

            public override bool Equals(object obj) => obj is CubeParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 生成エントリポイント
        // ================================================================
        public static MeshObject Generate(CubeParams p) =>
            p.CornerRadius <= 0f ? GenerateSimple(p) : GenerateRounded(p);

        // ================================================================
        // シンプルキューブ
        // ================================================================
        private static MeshObject GenerateSimple(CubeParams p)
        {
            var md = new MeshObject(p.MeshName);
            float halfH = p.Height * 0.5f;
            Vector3 pivot = new Vector3(
                p.Pivot.x * Mathf.Max(p.WidthTop, p.WidthBottom),
                p.Pivot.y * p.Height,
                p.Pivot.z * Mathf.Max(p.DepthTop, p.DepthBottom));

            float halfWT = p.WidthTop    * 0.5f; float halfWB = p.WidthBottom * 0.5f;
            float halfDT = p.DepthTop    * 0.5f; float halfDB = p.DepthBottom * 0.5f;

            AddQuadFace(md, new Vector3( halfWB,-halfH, halfDB)-pivot, new Vector3( halfWB,-halfH,-halfDB)-pivot, new Vector3( halfWT, halfH,-halfDT)-pivot, new Vector3( halfWT, halfH, halfDT)-pivot, Vector3.right,   p.Subdivisions.z, p.Subdivisions.y);
            AddQuadFace(md, new Vector3(-halfWB,-halfH,-halfDB)-pivot, new Vector3(-halfWB,-halfH, halfDB)-pivot, new Vector3(-halfWT, halfH, halfDT)-pivot, new Vector3(-halfWT, halfH,-halfDT)-pivot, Vector3.left,    p.Subdivisions.z, p.Subdivisions.y);
            AddQuadFace(md, new Vector3(-halfWT, halfH, halfDT)-pivot, new Vector3( halfWT, halfH, halfDT)-pivot, new Vector3( halfWT, halfH,-halfDT)-pivot, new Vector3(-halfWT, halfH,-halfDT)-pivot, Vector3.up,      p.Subdivisions.x, p.Subdivisions.z);
            AddQuadFace(md, new Vector3(-halfWB,-halfH,-halfDB)-pivot, new Vector3( halfWB,-halfH,-halfDB)-pivot, new Vector3( halfWB,-halfH, halfDB)-pivot, new Vector3(-halfWB,-halfH, halfDB)-pivot, Vector3.down,    p.Subdivisions.x, p.Subdivisions.z);
            AddQuadFace(md, new Vector3(-halfWB,-halfH, halfDB)-pivot, new Vector3( halfWB,-halfH, halfDB)-pivot, new Vector3( halfWT, halfH, halfDT)-pivot, new Vector3(-halfWT, halfH, halfDT)-pivot, Vector3.forward, p.Subdivisions.x, p.Subdivisions.y);
            AddQuadFace(md, new Vector3( halfWB,-halfH,-halfDB)-pivot, new Vector3(-halfWB,-halfH,-halfDB)-pivot, new Vector3(-halfWT, halfH,-halfDT)-pivot, new Vector3( halfWT, halfH,-halfDT)-pivot, Vector3.back,    p.Subdivisions.x, p.Subdivisions.y);
            return md;
        }

        // ================================================================
        // 角丸キューブ
        // ================================================================
        private static MeshObject GenerateRounded(CubeParams p)
        {
            var md = new MeshObject(p.MeshName);
            float halfH = p.Height * 0.5f;
            float r = p.CornerRadius; int seg = p.CornerSegments;
            Vector3 pivot = new Vector3(
                p.Pivot.x * Mathf.Max(p.WidthTop, p.WidthBottom),
                p.Pivot.y * p.Height,
                p.Pivot.z * Mathf.Max(p.DepthTop, p.DepthBottom));

            float inXT = p.WidthTop   *0.5f-r; float inZT = p.DepthTop   *0.5f-r;
            float inXB = p.WidthBottom*0.5f-r; float inZB = p.DepthBottom*0.5f-r;
            float inY  = halfH - r;

            AddCornerSphere(md, new Vector3( inXT, inY, inZT), new Vector3( 1, 1, 1), r, seg, pivot);
            AddCornerSphere(md, new Vector3(-inXT, inY, inZT), new Vector3(-1, 1, 1), r, seg, pivot);
            AddCornerSphere(md, new Vector3( inXT, inY,-inZT), new Vector3( 1, 1,-1), r, seg, pivot);
            AddCornerSphere(md, new Vector3(-inXT, inY,-inZT), new Vector3(-1, 1,-1), r, seg, pivot);
            AddCornerSphere(md, new Vector3( inXB,-inY, inZB), new Vector3( 1,-1, 1), r, seg, pivot);
            AddCornerSphere(md, new Vector3(-inXB,-inY, inZB), new Vector3(-1,-1, 1), r, seg, pivot);
            AddCornerSphere(md, new Vector3( inXB,-inY,-inZB), new Vector3( 1,-1,-1), r, seg, pivot);
            AddCornerSphere(md, new Vector3(-inXB,-inY,-inZB), new Vector3(-1,-1,-1), r, seg, pivot);

            AddEdgeCylinder(md, new Vector3(-inXT, inY, inZT), new Vector3( inXT, inY, inZT), new Vector3( 0, 1, 1), r, seg, pivot);
            AddEdgeCylinder(md, new Vector3( inXT, inY, inZT), new Vector3( inXT, inY,-inZT), new Vector3( 1, 1, 0), r, seg, pivot);
            AddEdgeCylinder(md, new Vector3( inXT, inY,-inZT), new Vector3(-inXT, inY,-inZT), new Vector3( 0, 1,-1), r, seg, pivot);
            AddEdgeCylinder(md, new Vector3(-inXT, inY,-inZT), new Vector3(-inXT, inY, inZT), new Vector3(-1, 1, 0), r, seg, pivot);
            AddEdgeCylinder(md, new Vector3(-inXB,-inY, inZB), new Vector3( inXB,-inY, inZB), new Vector3( 0,-1, 1), r, seg, pivot);
            AddEdgeCylinder(md, new Vector3( inXB,-inY, inZB), new Vector3( inXB,-inY,-inZB), new Vector3( 1,-1, 0), r, seg, pivot);
            AddEdgeCylinder(md, new Vector3( inXB,-inY,-inZB), new Vector3(-inXB,-inY,-inZB), new Vector3( 0,-1,-1), r, seg, pivot);
            AddEdgeCylinder(md, new Vector3(-inXB,-inY,-inZB), new Vector3(-inXB,-inY, inZB), new Vector3(-1,-1, 0), r, seg, pivot);
            AddEdgeCylinder(md, new Vector3( inXB,-inY, inZB), new Vector3( inXT, inY, inZT), new Vector3( 1, 0, 1), r, seg, pivot);
            AddEdgeCylinder(md, new Vector3(-inXB,-inY, inZB), new Vector3(-inXT, inY, inZT), new Vector3(-1, 0, 1), r, seg, pivot);
            AddEdgeCylinder(md, new Vector3( inXB,-inY,-inZB), new Vector3( inXT, inY,-inZT), new Vector3( 1, 0,-1), r, seg, pivot);
            AddEdgeCylinder(md, new Vector3(-inXB,-inY,-inZB), new Vector3(-inXT, inY,-inZT), new Vector3(-1, 0,-1), r, seg, pivot);

            AddQuadFace(md, new Vector3(p.WidthBottom*0.5f,-inY, inZB)-pivot, new Vector3(p.WidthBottom*0.5f,-inY,-inZB)-pivot, new Vector3(p.WidthTop*0.5f, inY,-inZT)-pivot, new Vector3(p.WidthTop*0.5f, inY, inZT)-pivot, Vector3.right,   p.Subdivisions.z, p.Subdivisions.y);
            AddQuadFace(md, new Vector3(-p.WidthBottom*0.5f,-inY,-inZB)-pivot, new Vector3(-p.WidthBottom*0.5f,-inY, inZB)-pivot, new Vector3(-p.WidthTop*0.5f, inY, inZT)-pivot, new Vector3(-p.WidthTop*0.5f, inY,-inZT)-pivot, Vector3.left,    p.Subdivisions.z, p.Subdivisions.y);
            AddQuadFace(md, new Vector3(-inXT, halfH, inZT)-pivot, new Vector3( inXT, halfH, inZT)-pivot, new Vector3( inXT, halfH,-inZT)-pivot, new Vector3(-inXT, halfH,-inZT)-pivot, Vector3.up,      p.Subdivisions.x, p.Subdivisions.z);
            AddQuadFace(md, new Vector3(-inXB,-halfH,-inZB)-pivot, new Vector3( inXB,-halfH,-inZB)-pivot, new Vector3( inXB,-halfH, inZB)-pivot, new Vector3(-inXB,-halfH, inZB)-pivot, Vector3.down,    p.Subdivisions.x, p.Subdivisions.z);
            AddQuadFace(md, new Vector3(-inXB,-inY,p.DepthBottom*0.5f)-pivot, new Vector3( inXB,-inY,p.DepthBottom*0.5f)-pivot, new Vector3( inXT, inY,p.DepthTop*0.5f)-pivot, new Vector3(-inXT, inY,p.DepthTop*0.5f)-pivot, Vector3.forward, p.Subdivisions.x, p.Subdivisions.y);
            AddQuadFace(md, new Vector3( inXB,-inY,-p.DepthBottom*0.5f)-pivot, new Vector3(-inXB,-inY,-p.DepthBottom*0.5f)-pivot, new Vector3(-inXT, inY,-p.DepthTop*0.5f)-pivot, new Vector3( inXT, inY,-p.DepthTop*0.5f)-pivot, Vector3.back,    p.Subdivisions.x, p.Subdivisions.y);
            return md;
        }

        // ================================================================
        // 共有ヘルパー（static）
        // ================================================================
        public static void AddQuadFace(MeshObject md, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, int divU, int divV)
        {
            int start = md.VertexCount;
            for (int iv = 0; iv <= divV; iv++)
            {
                float vt = (float)iv / divV;
                Vector3 L = Vector3.Lerp(v0, v3, vt), R = Vector3.Lerp(v1, v2, vt);
                for (int iu = 0; iu <= divU; iu++)
                {
                    float ut = (float)iu / divU;
                    md.Vertices.Add(new Vertex(Vector3.Lerp(L, R, ut), new Vector2(ut, vt), normal));
                }
            }
            int cols = divU + 1;
            for (int iv = 0; iv < divV; iv++)
                for (int iu = 0; iu < divU; iu++)
                { int i0 = start + iv*cols+iu; md.AddQuad(i0, i0+1, i0+cols+1, i0+cols); }
        }

        private static void AddCornerSphere(MeshObject md, Vector3 center, Vector3 dir, float radius, int seg, Vector3 pivot)
        {
            int start = md.VertexCount;
            bool rev = (dir.x * dir.y * dir.z) < 0;
            for (int lat = 0; lat <= seg; lat++)
            {
                float la = lat * Mathf.PI * 0.5f / seg;
                float cosLat = Mathf.Cos(la), sinLat = Mathf.Sin(la);
                for (int lon = 0; lon <= seg; lon++)
                {
                    float lo = rev ? (seg-lon)*Mathf.PI*0.5f/seg : lon*Mathf.PI*0.5f/seg;
                    Vector3 n = new Vector3(sinLat*Mathf.Cos(lo)*dir.x, cosLat*dir.y, sinLat*Mathf.Sin(lo)*dir.z).normalized;
                    md.Vertices.Add(new Vertex(center + n*radius - pivot, new Vector2((float)lon/seg,(float)lat/seg), n));
                }
            }
            int cols = seg+1;
            for (int lat = 0; lat < seg; lat++)
                for (int lon = 0; lon < seg; lon++)
                { int i0 = start+lat*cols+lon; md.AddQuad(i0, i0+1, i0+cols+1, i0+cols); }
        }

        private static void AddEdgeCylinder(MeshObject md, Vector3 start3, Vector3 end3, Vector3 cornerDir, float radius, int seg, Vector3 pivot)
        {
            int startIdx = md.VertexCount;
            Vector3 axis = (end3 - start3).normalized;
            Vector3 p1, p2;
            if      (Mathf.Abs(axis.x) > 0.9f) { p1 = new Vector3(0, cornerDir.y, 0).normalized; p2 = new Vector3(0, 0, cornerDir.z).normalized; }
            else if (Mathf.Abs(axis.y) > 0.9f) { p1 = new Vector3(cornerDir.x, 0, 0).normalized; p2 = new Vector3(0, 0, cornerDir.z).normalized; }
            else                                { p1 = new Vector3(cornerDir.x, 0, 0).normalized; p2 = new Vector3(0, cornerDir.y, 0).normalized; }

            bool rev = Vector3.Dot(Vector3.Cross(p1, p2), axis) < 0;
            Vector3 rs = rev ? end3 : start3, re = rev ? start3 : end3;

            for (int ring = 0; ring <= 1; ring++)
            {
                Vector3 bp = ring == 0 ? rs : re;
                for (int j = 0; j <= seg; j++)
                {
                    float a = j * Mathf.PI * 0.5f / seg;
                    Vector3 n = p1 * Mathf.Cos(a) + p2 * Mathf.Sin(a);
                    md.Vertices.Add(new Vertex(bp + n*radius - pivot, new Vector2(ring, (float)j/seg), n));
                }
            }
            int cols = seg + 1;
            for (int j = 0; j < seg; j++)
            {
                int i0 = startIdx + j;
                md.AddQuad(i0, i0+1, startIdx+cols+j+1, startIdx+cols+j);
            }
        }
    }
}
