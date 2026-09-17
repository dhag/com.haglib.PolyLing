// BooleanDiagnosticsBsp.cs
// BooleanDiagnostics 用に、pb_CSG の BSP（Node.cs / Plane.SplitPolygon）を処理を変えずに写したもの。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【なぜ写すか】
//   面が欠ける箇所の見当として、次の 3 つを数えたい。どれも Node の中の分岐で、
//   外から呼んだだけでは数が取れない。取り込んだ外部コード（ThirdParty/ParaboxCSG）へ
//   計測を足さないため、ここに写して数える。
//     1. 葉（back が null）で捨てた多角形（平面が有効か無効かで分ける）   … Node.cs:164-171
//     2. 平面が無効なまま「同一平面・裏向き」に振り分けられた多角形       … Plane.cs:105-108
//     3. 平面が無効な節で、下の節を見ずに返した回数と素通りした多角形の数 … Node.cs:146-149
//
// 【写し方の約束】
//   分岐・順序・リストの書き換え（AllPolygons が節のリストへ足し込むこと、Clone が浅いこと）
//   まで元と同じにする。違えば測っているものが変わる。
//   写し違いは BooleanDiagnostics が元の Node の結果（多角形数・無効数・頂点位置の和）と
//   突き合わせて検出する。
//
// 【FixInvalidPlanes（因果の試し。既定 off）】
//   Plane のコンストラクタは Vector3.normalized を使う（Plane.cs:63）。Unity の normalized は
//   長さが小さいベクトルを 0 にするので、面積の小さい破片の法線が 0 になり平面が無効になる。
//   on にすると、多角形を作った直後に平面が無効なら、頂点全体から求めた法線（Newell 法）を
//   自前で割って平面を作り直す。穴の数が変わるかで、無効な平面が穴の原因かを確かめる。

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Vertex   = Poly_Ling.CSG.Vertex;
using Polygon  = Poly_Ling.CSG.Polygon;
using CsgPlane = Poly_Ling.CSG.Plane;
using CsgCore  = Poly_Ling.CSG.CSG;

namespace Poly_Ling.Ops
{
    /// <summary>写した BSP の計数。</summary>
    internal sealed class DiagBspCounters
    {
        public bool FixInvalidPlanes;

        public int LeafDiscardValid;
        public int LeafDiscardInvalid;
        public int CoplanarBackInvalid;
        public int InvalidNodeReturns;
        public int InvalidNodePassed;
        public int PlanesFixed;
    }

    /// <summary>Node.cs を写した節。</summary>
    internal sealed class DiagNode
    {
        public List<Polygon> polygons;
        public DiagNode front;
        public DiagNode back;
        public CsgPlane plane;

        private readonly DiagBspCounters _c;

        public DiagNode(DiagBspCounters c) { _c = c; polygons = new List<Polygon>(); }

        public DiagNode(DiagBspCounters c, List<Polygon> list) { _c = c; polygons = new List<Polygon>(); Build(list); }

        private DiagNode(DiagBspCounters c, List<Polygon> list, CsgPlane plane, DiagNode front, DiagNode back)
        {
            _c = c;
            polygons   = list;
            this.plane = plane;
            this.front = front;
            this.back  = back;
        }

        // Node.cs:48-53（浅い写し）
        public DiagNode Clone() => new DiagNode(_c, polygons, plane, front, back);

        // Node.cs:57-70
        public void ClipTo(DiagNode other)
        {
            polygons = other.ClipPolygons(polygons);
            front?.ClipTo(other);
            back?.ClipTo(other);
        }

        // Node.cs:73-93
        public void Invert()
        {
            for (int i = 0; i < polygons.Count; i++) polygons[i].Flip();
            plane?.Flip();
            front?.Invert();
            back?.Invert();
            var tmp = front; front = back; back = tmp;
        }

        // Node.cs:99-141
        public void Build(List<Polygon> list)
        {
            if (list.Count < 1) return;

            bool newNode = plane == null || !plane.Valid();
            if (newNode)
            {
                plane = new CsgPlane();
                plane.normal = list[0].plane.normal;
                plane.w      = list[0].plane.w;
            }

            if (polygons == null) polygons = new List<Polygon>();

            var listFront = new List<Polygon>();
            var listBack  = new List<Polygon>();

            for (int i = 0; i < list.Count; i++)
                Split(plane, list[i], polygons, polygons, listFront, listBack);

            if (listFront.Count > 0)
            {
                if (newNode && list.SequenceEqual(listFront)) polygons.AddRange(listFront);
                else (front ?? (front = new DiagNode(_c))).Build(listFront);
            }

            if (listBack.Count > 0)
            {
                if (newNode && list.SequenceEqual(listBack)) polygons.AddRange(listBack);
                else (back ?? (back = new DiagNode(_c))).Build(listBack);
            }
        }

        // Node.cs:144-178
        public List<Polygon> ClipPolygons(List<Polygon> list)
        {
            if (plane == null || !plane.Valid())
            {
                _c.InvalidNodeReturns++;
                _c.InvalidNodePassed += list.Count;
                return list;
            }

            var listFront = new List<Polygon>();
            var listBack  = new List<Polygon>();

            for (int i = 0; i < list.Count; i++)
                Split(plane, list[i], listFront, listBack, listFront, listBack);

            if (front != null) listFront = front.ClipPolygons(listFront);

            if (back != null)
            {
                listBack = back.ClipPolygons(listBack);
            }
            else
            {
                foreach (var p in listBack)
                {
                    if (p.plane != null && p.plane.Valid()) _c.LeafDiscardValid++;
                    else                                     _c.LeafDiscardInvalid++;
                }
                listBack.Clear();
            }

            listFront.AddRange(listBack);
            return listFront;
        }

        // Node.cs:181-200（節のリストへ足し込むところも同じ）
        public List<Polygon> AllPolygons()
        {
            List<Polygon> list = polygons;
            List<Polygon> listFront = new List<Polygon>(), listBack = new List<Polygon>();
            if (front != null) listFront = front.AllPolygons();
            if (back  != null) listBack  = back.AllPolygons();
            list.AddRange(listFront);
            list.AddRange(listBack);
            return list;
        }

        // ================================================================
        // Plane.cs:85-170
        // ================================================================

        private const int Coplanar = 0, Front = 1, Back = 2, Spanning = 3;

        private void Split(CsgPlane pl, Polygon polygon,
                           List<Polygon> coplanarFront, List<Polygon> coplanarBack,
                           List<Polygon> front, List<Polygon> back)
        {
            int polygonType = 0;
            var types = new List<int>();

            for (int i = 0; i < polygon.vertices.Count; i++)
            {
                float t = Vector3.Dot(pl.normal, polygon.vertices[i].position) - pl.w;
                int type = (t < -CsgCore.epsilon) ? Back : ((t > CsgCore.epsilon) ? Front : Coplanar);
                polygonType |= type;
                types.Add(type);
            }

            switch (polygonType)
            {
                case Coplanar:
                    if (Vector3.Dot(pl.normal, polygon.plane.normal) > 0)
                    {
                        coplanarFront.Add(polygon);
                    }
                    else
                    {
                        if (!polygon.plane.Valid()) _c.CoplanarBackInvalid++;
                        coplanarBack.Add(polygon);
                    }
                    break;

                case Front: front.Add(polygon); break;
                case Back:  back.Add(polygon);  break;

                case Spanning:
                {
                    var f = new List<Vertex>();
                    var b = new List<Vertex>();

                    for (int i = 0; i < polygon.vertices.Count; i++)
                    {
                        int j = (i + 1) % polygon.vertices.Count;
                        int ti = types[i], tj = types[j];
                        Vertex vi = polygon.vertices[i], vj = polygon.vertices[j];

                        if (ti != Back)  f.Add(vi);
                        if (ti != Front) b.Add(vi);

                        if ((ti | tj) == Spanning)
                        {
                            float t = (pl.w - Vector3.Dot(pl.normal, vi.position)) /
                                      Vector3.Dot(pl.normal, vj.position - vi.position);
                            Vertex v = Poly_Ling.CSG.VertexUtility.Mix(vi, vj, t);
                            f.Add(v);
                            b.Add(v);
                        }
                    }

                    if (f.Count >= 3) front.Add(MakePolygon(f, polygon.materialIndex));
                    if (b.Count >= 3) back.Add(MakePolygon(b, polygon.materialIndex));
                    break;
                }
            }
        }

        private Polygon MakePolygon(List<Vertex> verts, int materialIndex)
        {
            var p = new Polygon(verts, materialIndex);
            if (_c.FixInvalidPlanes) FixPlane(p, _c);
            return p;
        }

        /// <summary>平面が無効なら、Newell 法の法線を自前で割って作り直す。</summary>
        internal static void FixPlane(Polygon p, DiagBspCounters c)
        {
            if (p?.plane == null || p.plane.Valid() || p.vertices == null || p.vertices.Count < 3) return;

            double nx = 0, ny = 0, nz = 0;
            int n = p.vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 cur = p.vertices[i].position;
                Vector3 nxt = p.vertices[(i + 1) % n].position;
                nx += ((double)cur.y - nxt.y) * ((double)cur.z + nxt.z);
                ny += ((double)cur.z - nxt.z) * ((double)cur.x + nxt.x);
                nz += ((double)cur.x - nxt.x) * ((double)cur.y + nxt.y);
            }
            double len = System.Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (len <= 0) return;

            var normal = new Vector3((float)(nx / len), (float)(ny / len), (float)(nz / len));
            p.plane.normal = normal;
            p.plane.w      = Vector3.Dot(normal, p.vertices[0].position);
            if (p.plane.Valid()) c.PlanesFixed++;
        }
    }
}
