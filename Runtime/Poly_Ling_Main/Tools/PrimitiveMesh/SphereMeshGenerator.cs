// SphereMeshGenerator.cs
// 球メッシュ生成ロジック（Runtime / Editor 共有）
// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置

using System;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PrimitiveMesh
{
    public static class SphereMeshGenerator
    {
        // ================================================================
        // パラメータ構造体
        // ================================================================
        [Serializable]
        public struct SphereParams : IEquatable<SphereParams>
        {
            public string MeshName;
            public float Radius;
            public int LongitudeSegments;
            public int LatitudeSegments;
            public int CubeSubdivisions;
            public bool CubeSphere;
            public Vector3 Pivot;
            public float RotationX, RotationY;

            public static SphereParams Default => new SphereParams
            {
                MeshName           = "Sphere",
                Radius             = 0.5f,
                LongitudeSegments  = 24,
                LatitudeSegments   = 16,
                CubeSubdivisions   = 8,
                CubeSphere         = false,
                Pivot              = Vector3.zero,
                RotationX          = 20f,
                RotationY          = 30f,
            };

            public bool Equals(SphereParams o) =>
                MeshName == o.MeshName &&
                Mathf.Approximately(Radius, o.Radius) &&
                LongitudeSegments == o.LongitudeSegments &&
                LatitudeSegments  == o.LatitudeSegments  &&
                CubeSubdivisions  == o.CubeSubdivisions  &&
                CubeSphere == o.CubeSphere &&
                Pivot == o.Pivot &&
                Mathf.Approximately(RotationX, o.RotationX) &&
                Mathf.Approximately(RotationY, o.RotationY);

            public override bool Equals(object obj) => obj is SphereParams p && Equals(p);
            public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
        }

        // ================================================================
        // 生成
        // ================================================================
        public static MeshObject Generate(SphereParams p) =>
            p.CubeSphere
                ? GenerateCubeSphere(p.Radius, p.CubeSubdivisions, p.Pivot, p.MeshName)
                : GenerateSphere(p.Radius, p.LongitudeSegments, p.LatitudeSegments, p.Pivot, p.MeshName);

        private static MeshObject GenerateSphere(float radius, int lonSeg, int latSeg, Vector3 pivot, string name)
        {
            var md = new MeshObject(name);
            Vector3 pivotOffset = pivot * radius * 2f;
            int cols = lonSeg + 1;

            for (int lat = 0; lat <= latSeg; lat++)
            {
                float theta = lat * Mathf.PI / latSeg;
                float sinT = Mathf.Sin(theta), cosT = Mathf.Cos(theta);
                for (int lon = 0; lon <= lonSeg; lon++)
                {
                    float phi = lon * 2f * Mathf.PI / lonSeg;
                    Vector3 n = new Vector3(Mathf.Cos(phi)*sinT, cosT, Mathf.Sin(phi)*sinT);
                    md.Vertices.Add(new Vertex(n*radius - pivotOffset,
                        new Vector2((float)lon/lonSeg, 1f-(float)lat/latSeg), n));
                }
            }
            for (int lat = 0; lat < latSeg; lat++)
                for (int lon = 0; lon < lonSeg; lon++)
                { int i0 = lat*cols+lon; md.AddQuad(i0, i0+1, i0+cols+1, i0+cols); }
            return md;
        }

        private static MeshObject GenerateCubeSphere(float radius, int sub, Vector3 pivot, string name)
        {
            var md = new MeshObject(name);
            Vector3 pivotOffset = pivot * radius * 2f;
            Vector3[] faceN = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3[] tanU  = { Vector3.forward, Vector3.back, Vector3.right, Vector3.right, Vector3.left, Vector3.right };
            Vector3[] tanV  = { Vector3.up, Vector3.up, Vector3.forward, Vector3.back, Vector3.up, Vector3.up };
            int vpr = sub + 1;

            for (int face = 0; face < 6; face++)
            {
                int fsi = md.VertexCount;
                for (int v = 0; v <= sub; v++)
                    for (int u = 0; u <= sub; u++)
                    {
                        float cu = (float)u/sub*2f-1f, cv = (float)v/sub*2f-1f;
                        Vector3 sn = (faceN[face] + tanU[face]*cu + tanV[face]*cv).normalized;
                        md.Vertices.Add(new Vertex(sn*radius - pivotOffset, new Vector2((float)u/sub,(float)v/sub), sn));
                    }
                for (int v = 0; v < sub; v++)
                    for (int u = 0; u < sub; u++)
                    { int i0 = fsi+v*vpr+u; md.AddQuad(i0, i0+1, i0+vpr+1, i0+vpr); }
            }
            return md;
        }
    }
}
