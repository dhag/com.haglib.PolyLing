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
// 【Plane の法線を正規化する（元コードからの変更）】
// 元コードは Vector3.Cross(b-a, c-a) をそのまま法線に使っていた。
// その場合 SplitPolygon 内の t は「平面からの距離 × 三角形の面積の 2 倍」になり、
// CSG.epsilon の実効的な許容量が三角形の大きさに依存する。
//
// 実測（球 738 頂点 − 円柱 170 頂点、差）:
//   正規化前 epsilon 1e-5 … 2920 面 / 境界 32（96 面が落ちる）
//   正規化後 epsilon 1e-5 … 3016 面 / 境界 23（落ちない）
// 大きめの epsilon を指定したときに面が落ちる問題は、これで解消した。
//
// ただし epsilon 1e-6 での結果は正規化の前後で変わらない。
// そこで残る三角形 8 枚の脱落は同一平面の誤判定ではなく、別の原因による。
// epsilon を 1e-9 まで下げても、法線を正規化しても、同じ 8 枚が落ちる。
//
// w の意味は「原点から平面までの距離」に変わる。平方根のぶん僅かに遅くなる。
//
// 【法線が 0 になる（元コードからの変更 2）】
// 小さい三角形（外積の長さ 1e-5 未満。一辺 3mm ほど）や、分割で作った細い断片は、
// Vector3.normalized が法線を 0 にする。法線 0 の面が木の節になると、同じ節へ来た面が
// すべて「同一平面」に入り、木が壊れて面が落ちる（上の注記の「別の原因」。曲げた後のブーリアンで
// 境界辺が残った）。法線は double で正規化し、分割した断片は元の面の平面を受け継ぐ。
// 潰れた入力の三角形（面積 0）は木に入れない（Model）。

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
            // 正規化する理由はファイル冒頭の注記を参照。
            // Vector3.normalized は長さ 1e-5 未満を 0 にするので使わない（注記の「法線が 0 になる」）。
            double ux = (double)b.x - a.x, uy = (double)b.y - a.y, uz = (double)b.z - a.z;
            double vx = (double)c.x - a.x, vy = (double)c.y - a.y, vz = (double)c.z - a.z;
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            double len = System.Math.Sqrt(nx * nx + ny * ny + nz * nz);
            normal = len > 0.0 ? new Vector3((float)(nx / len), (float)(ny / len), (float)(nz / len)) : Vector3.zero;
            w = Vector3.Dot(normal, a);
        }

        /// <summary>写しを作る。分割した断片は元の面の平面を受け継ぐ（Flip が別々に効くように写す）。</summary>
        public Plane Clone() => new Plane { normal = normal, w = w };

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
                        front.Add(new Polygon(f, polygon.materialIndex, polygon.plane.Clone()));
                    }

                    if (b.Count >= 3)
                    {
                        back.Add(new Polygon(b, polygon.materialIndex, polygon.plane.Clone()));
                    }
                }
                break;
            }   // End switch(polygonType)
        }
    }
}
