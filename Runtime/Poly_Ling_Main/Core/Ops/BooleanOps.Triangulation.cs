// T字解消で追加した境界頂点を、縮退三角形にせず出力へ残す。
// ブーリアン出力だけに適用する。汎用の Face.Triangulate の挙動は変更しない。
using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static partial class BooleanOps
    {
        private static bool TriangulateResult(MeshObject mesh)
        {
            var triangles = new List<Face>();
            foreach (var face in mesh.Faces)
            {
                int count = face.VertexCount;
                if (count < 3) return false;

                // 原点を面内に取り、doubleで投影面の向きを決める。
                var points = new Vector3[count];
                for (int i = 0; i < count; i++) points[i] = mesh.Vertices[face.VertexIndices[i]].Position;
                Vector3 origin = points[0];
                double nx = 0, ny = 0, nz = 0;
                for (int i = 1; i + 1 < count; i++)
                {
                    double ax = (double)points[i].x-origin.x, ay = (double)points[i].y-origin.y, az = (double)points[i].z-origin.z;
                    double bx = (double)points[i+1].x-origin.x, by = (double)points[i+1].y-origin.y, bz = (double)points[i+1].z-origin.z;
                    nx += ay*bz-az*by; ny += az*bx-ax*bz; nz += ax*by-ay*bx;
                }
                double largest = Math.Max(Math.Abs(nx), Math.Max(Math.Abs(ny), Math.Abs(nz)));
                if (!(largest > 0) || double.IsInfinity(largest)) return false;
                if (count == 3) { triangles.Add(face); continue; }

                int axis = Math.Abs(nx) >= Math.Abs(ny) && Math.Abs(nx) >= Math.Abs(nz) ? 0 : Math.Abs(ny) >= Math.Abs(nz) ? 1 : 2;
                var x = new double[count];
                var y = new double[count];
                var remaining = new List<int>(count);
                for (int i = 0; i < count; i++)
                {
                    Vector3 p = points[i];
                    x[i] = axis == 0 ? (double)p.y-origin.y : (double)p.x-origin.x;
                    y[i] = axis == 2 ? (double)p.y-origin.y : (double)p.z-origin.z;
                    remaining.Add(i);
                }
                double signedArea = axis == 0 ? nx : axis == 1 ? -ny : nz;
                double winding = signedArea > 0 ? 1 : -1;

                // 境界上の他頂点も「耳の中」に含める。
                // この判定がないと、対角線が既存の分割点を飛び越えてT字を再発させる。
                while (remaining.Count > 3)
                {
                    int ear = -1;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        int a = remaining[(i + remaining.Count - 1) % remaining.Count];
                        int b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                        if (Orientation(x, y, a, b, c) * winding <= 0) continue;
                        bool contains = false;
                        foreach (int p in remaining)
                        {
                            if (p == a || p == b || p == c) continue;
                            if (Orientation(x, y, a, b, p) * winding >= 0 &&
                                Orientation(x, y, b, c, p) * winding >= 0 &&
                                Orientation(x, y, c, a, p) * winding >= 0)
                            { contains = true; break; }
                        }
                        if (contains) continue;
                        triangles.Add(OutputTriangle(face, a, b, c));
                        ear = i;
                        break;
                    }
                    if (ear < 0) return false;
                    remaining.RemoveAt(ear);
                }
                if (Orientation(x, y, remaining[0], remaining[1], remaining[2]) * winding <= 0) return false;
                triangles.Add(OutputTriangle(face, remaining[0], remaining[1], remaining[2]));
            }
            mesh.Faces = triangles;
            mesh.IsTriangulated = true;
            // 面を入れ替えたので、線分が無くなった区間で線分群を切る（頂点索引は不変）。
            LineGroupOps.ReconcileWithFaces(mesh);
            return true;
        }

        private static double Orientation(double[] x, double[] y, int a, int b, int c)
            => (x[b]-x[a])*(y[c]-y[a]) - (y[b]-y[a])*(x[c]-x[a]);

        private static Face OutputTriangle(Face source, int a, int b, int c)
        {
            var face = Face.CreateTriangle(
                source.VertexIndices[a], source.VertexIndices[b], source.VertexIndices[c],
                a < source.UVIndices.Count ? source.UVIndices[a] : 0,
                b < source.UVIndices.Count ? source.UVIndices[b] : 0,
                c < source.UVIndices.Count ? source.UVIndices[c] : 0,
                a < source.NormalIndices.Count ? source.NormalIndices[a] : 0,
                b < source.NormalIndices.Count ? source.NormalIndices[b] : 0,
                c < source.NormalIndices.Count ? source.NormalIndices[c] : 0, source.MaterialIndex);
            face.Flags = source.Flags;
            // CSG出力のIDは元から未割当。新規IDは付けない。
            return face;
        }
    }
}
