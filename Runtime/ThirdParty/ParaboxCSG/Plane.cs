// Original CSG.JS library by Evan Wallace (http://madebyevan.com), under the MIT license.
// GitHub: https://github.com/evanw/csg.js/
//
// C++ port by Tomasz Dabrowski (http://28byteslater.com), under the MIT license.
// GitHub: https://github.com/dabroz/csgjs-cpp/
//
// C# port by Karl Henkel (parabox.co), under MIT license.
// GitHub: https://github.com/karl-/pb_CSG
//
// PolyLing 改変:
//   - namespace を Parabox.CSG -> Poly_Ling.CSG。
//   - Polygon 生成時の material (Material) を materialIndex (int) に追随。
// 詳細は同フォルダの LICENSE.txt を参照。
//
// 【Plane の法線を正規化していない点について】
// Plane(a,b,c) は Vector3.Cross(b-a, c-a) をそのまま法線に使う（元コードのまま）。
// このため SplitPolygon 内の t は三角形の面積に比例した量になり、
// CSG.epsilon の実効的な許容量は三角形の大きさに依存する。
// 挙動を変えないためここは元のままとし、BooleanOps 側で epsilon を可変にしている。

using UnityEngine;
using System.Collections.Generic;

namespace Poly_Ling.CSG
{
    /// <summary>
    /// Represents a plane in 3d space.
    /// <remarks>Does not include position.</remarks>
    /// </summary>
    sealed class Plane
    {
        public Vector3 normal;
        public float w;

        [System.Flags]
        enum EPolygonType
        {
            Coplanar    = 0,
            Front       = 1,
            Back        = 2,
            Spanning    = 3         /// 3 is Front | Back - not a separate entry
        };

        public Plane()
        {
            normal = Vector3.zero;
            w = 0f;
        }

        public Plane(Vector3 a, Vector3 b, Vector3 c)
        {
            normal = Vector3.Cross(b - a, c - a);//.normalized;
            w = Vector3.Dot(normal, a);
        }

        public override string ToString() => $"{normal} {w}";

        public bool Valid()
        {
            return normal.magnitude > 0f;
        }

        public void Flip()
        {
            normal *= -1f;
            w *= -1f;
        }

        // Split `polygon` by this plane if needed, then put the polygon or polygon
        // fragments in the appropriate lists. Coplanar polygons go into either
        // `coplanarFront` or `coplanarBack` depending on their orientation with
        // respect to this plane. Polygons in front or in back of this plane go into
        // either `front` or `back`.
        public void SplitPolygon(Polygon polygon, List<Polygon> coplanarFront, List<Polygon> coplanarBack, List<Polygon> front, List<Polygon> back)
        {
            // Classify each point as well as the entire polygon into one of the above
            // four classes.
            EPolygonType polygonType = 0;
            List<EPolygonType> types = new List<EPolygonType>();

            for (int i = 0; i < polygon.vertices.Count; i++)
            {
                float t = Vector3.Dot(this.normal, polygon.vertices[i].position) - this.w;
                EPolygonType type = (t < -CSG.epsilon) ? EPolygonType.Back : ((t > CSG.epsilon) ? EPolygonType.Front : EPolygonType.Coplanar);
                polygonType |= type;
                types.Add(type);
            }

            // Put the polygon in the correct list, splitting it when necessary.
            switch (polygonType)
            {
                case EPolygonType.Coplanar:
                {
                    if (Vector3.Dot(this.normal, polygon.plane.normal) > 0)
                        coplanarFront.Add(polygon);
                    else
                        coplanarBack.Add(polygon);
                }
                break;

                case EPolygonType.Front:
                {
                    front.Add(polygon);
                }
                break;

                case EPolygonType.Back:
                {
                    back.Add(polygon);
                }
                break;

                case EPolygonType.Spanning:
                {
                    List<Vertex> f = new List<Vertex>();
                    List<Vertex> b = new List<Vertex>();

                    for (int i = 0; i < polygon.vertices.Count; i++)
                    {
                        int j = (i + 1) % polygon.vertices.Count;

                        EPolygonType ti = types[i], tj = types[j];

                        Vertex vi = polygon.vertices[i], vj = polygon.vertices[j];

                        if (ti != EPolygonType.Back)
                        {
                            f.Add(vi);
                        }

                        if (ti != EPolygonType.Front)
                        {
                            b.Add(vi);
                        }

                        if ((ti | tj) == EPolygonType.Spanning)
                        {
                            float t = (this.w - Vector3.Dot(this.normal, vi.position)) / Vector3.Dot(this.normal, vj.position - vi.position);

                            Vertex v = VertexUtility.Mix(vi, vj, t);

                            f.Add(v);
                            b.Add(v);
                        }
                    }

                    if (f.Count >= 3)
                    {
                        front.Add(new Polygon(f, polygon.materialIndex));
                    }

                    if (b.Count >= 3)
                    {
                        back.Add(new Polygon(b, polygon.materialIndex));
                    }
                }
                break;
            }   // End switch(polygonType)
        }
    }
}
