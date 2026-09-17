// TJunctionOps.cs
// 境界の辺を、逆向きの境界辺と接続するように分割して T 字接合を解消する。
// 距離だけで全ての面へ頂点を挿入すると、細い面の反対側や、既に2面で共有される
// 辺まで分割して非多様体を作る。境界の接続と巻き順を必ず併用する。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class TJunctionOps
    {
        public const float DefaultTolerance = 1e-4f;

        public struct Result
        {
            public int Inserted;
            public int TouchedFaces;
        }

        /// <summary>
        /// 境界辺 A→B に対し、B→V または V→A という別の面の境界辺があり、
        /// V が線分 AB 上（tolerance 以内）なら A→V→B に分割する。
        /// 新しい2辺は未使用、または逆向きに1回だけ使われている場合に限る。
        /// 1回の分割で境界辺が必ず1本以上減るため、連鎖する T 字も有限回で解消する。
        /// 頂点位置・頂点数・面数・面の巻き順は変えない。UV/法線の隅参照も補間する。
        /// 孤立頂点の挿入や、接続の手掛かりが無い開いた隙間の補修は行わない。
        /// </summary>
        public static Result Resolve(MeshObject mesh, float tolerance = DefaultTolerance)
        {
            if (mesh == null || mesh.FaceCount == 0 || mesh.VertexCount == 0)
                return new Result();
            if (float.IsNaN(tolerance) || float.IsInfinity(tolerance) || tolerance < 0f)
                throw new ArgumentOutOfRangeException(nameof(tolerance));
            return new Resolver(mesh, Math.Max(tolerance, 1e-7f)).Run();
        }

        private sealed class Edge
        {
            public int A, B, Face, Order;
            public double LengthSquared;
        }

        private sealed class EdgeOrder : IComparer<Edge>
        {
            public int Compare(Edge a, Edge b)
            {
                int c = b.LengthSquared.CompareTo(a.LengthSquared);
                return c != 0 ? c : a.Order.CompareTo(b.Order);
            }
        }

        private sealed class Resolver
        {
            private readonly MeshObject mesh;
            private readonly double toleranceSquared;
            private readonly Dictionary<(int, int), List<Edge>> uses = new Dictionary<(int, int), List<Edge>>();
            private readonly Dictionary<int, HashSet<Edge>> incoming = new Dictionary<int, HashSet<Edge>>();
            private readonly Dictionary<int, HashSet<Edge>> outgoing = new Dictionary<int, HashSet<Edge>>();
            private readonly SortedSet<Edge> pending = new SortedSet<Edge>(new EdgeOrder());
            private int nextOrder;

            public Resolver(MeshObject mesh, double tolerance)
            {
                this.mesh = mesh;
                toleranceSquared = tolerance * tolerance;
            }

            private static (int, int) Key(int a, int b) => a < b ? (a, b) : (b, a);

            public Result Run()
            {
                for (int fi = 0; fi < mesh.FaceCount; fi++)
                {
                    var indices = mesh.Faces[fi]?.VertexIndices;
                    if (indices == null || indices.Count < 3) continue;
                    for (int i = 0; i < indices.Count; i++)
                    {
                        int a = indices[i], b = indices[(i + 1) % indices.Count];
                        if (a == b) continue;
                        var key = Key(a, b);
                        if (!uses.TryGetValue(key, out var list)) uses[key] = list = new List<Edge>(2);
                        list.Add(NewEdge(a, b, fi));
                    }
                }
                foreach (var list in uses.Values)
                    if (list.Count == 1) AddBoundary(list[0]);

                var result = new Result();
                var touched = new HashSet<int>();
                while (pending.Count > 0)
                {
                    Edge edge = pending.Min;
                    pending.Remove(edge);
                    if (!IsBoundary(edge)) continue;

                    int vertex = -1;
                    double bestDistance = double.PositiveInfinity, parameter = 0;
                    if (outgoing.TryGetValue(edge.B, out var fromB))
                        foreach (var other in fromB)
                            Consider(edge, other, other.B, ref vertex, ref parameter, ref bestDistance);
                    if (incoming.TryGetValue(edge.A, out var toA))
                        foreach (var other in toA)
                            Consider(edge, other, other.A, ref vertex, ref parameter, ref bestDistance);
                    if (vertex < 0) continue;

                    var face = mesh.Faces[edge.Face];
                    int corner = FindCorner(face, edge.A, edge.B);
                    if (corner < 0) continue;
                    InsertCorner(face, corner, mesh.Vertices[vertex], vertex, (float)parameter);

                    RemoveBoundary(edge);
                    uses.Remove(Key(edge.A, edge.B)); // 分割対象は1面だけが使う辺
                    AddEdge(NewEdge(edge.A, vertex, edge.Face));
                    AddEdge(NewEdge(vertex, edge.B, edge.Face));
                    result.Inserted++;
                    touched.Add(edge.Face);
                }
                result.TouchedFaces = touched.Count;
                return result;
            }

            private Edge NewEdge(int a, int b, int face)
            {
                Vector3 p = mesh.Vertices[a].Position, q = mesh.Vertices[b].Position;
                double x = (double)q.x - p.x, y = (double)q.y - p.y, z = (double)q.z - p.z;
                return new Edge { A = a, B = b, Face = face, Order = nextOrder++, LengthSquared = x*x + y*y + z*z };
            }

            private bool IsBoundary(Edge edge)
                => uses.TryGetValue(Key(edge.A, edge.B), out var list) && list.Count == 1 && ReferenceEquals(list[0], edge);

            private bool CanPair(int a, int b)
            {
                if (!uses.TryGetValue(Key(a, b), out var list)) return true;
                return list.Count == 1 && list[0].A == b && list[0].B == a;
            }

            private void Consider(Edge edge, Edge other, int v, ref int vertex, ref double parameter, ref double bestDistance)
            {
                if (other.Face == edge.Face || edge.LengthSquared <= 0 || !IsBoundary(other)) return;
                if (mesh.Faces[edge.Face].VertexIndices.Contains(v)) return;
                if (!CanPair(edge.A, v) || !CanPair(v, edge.B)) return;

                // float の位置を double にしてから差と射影を求める。
                Vector3 a = mesh.Vertices[edge.A].Position, b = mesh.Vertices[edge.B].Position, p = mesh.Vertices[v].Position;
                double dx = (double)b.x - a.x, dy = (double)b.y - a.y, dz = (double)b.z - a.z;
                double px = (double)p.x - a.x, py = (double)p.y - a.y, pz = (double)p.z - a.z;
                double t = (px*dx + py*dy + pz*dz) / edge.LengthSquared;
                if (t <= 0 || t >= 1) return;
                double x = px-t*dx, y = py-t*dy, z = pz-t*dz;
                double distance = x*x + y*y + z*z;
                if (distance > toleranceSquared) return;
                // HashSet の走査順に依存させない。
                if (distance > bestDistance || (distance == bestDistance && vertex >= 0 && v >= vertex)) return;
                vertex = v;
                parameter = t;
                bestDistance = distance;
            }

            private static void AddAt(Dictionary<int, HashSet<Edge>> map, int vertex, Edge edge)
            {
                if (!map.TryGetValue(vertex, out var set)) map[vertex] = set = new HashSet<Edge>();
                set.Add(edge);
            }

            private void AddBoundary(Edge edge)
            {
                AddAt(outgoing, edge.A, edge);
                AddAt(incoming, edge.B, edge);
                pending.Add(edge);
                // 新しい逆向きの隣接辺ができた端点では、以前分割できなかった辺も再検査する。
                if (incoming.TryGetValue(edge.A, out var toA))
                    foreach (var adjacent in toA) pending.Add(adjacent);
                if (outgoing.TryGetValue(edge.B, out var fromB))
                    foreach (var adjacent in fromB) pending.Add(adjacent);
            }

            private void RemoveBoundary(Edge edge)
            {
                if (outgoing.TryGetValue(edge.A, out var from)) from.Remove(edge);
                if (incoming.TryGetValue(edge.B, out var to)) to.Remove(edge);
                pending.Remove(edge);
            }

            private void AddEdge(Edge edge)
            {
                var key = Key(edge.A, edge.B);
                if (!uses.TryGetValue(key, out var list))
                {
                    uses[key] = new List<Edge> { edge };
                    AddBoundary(edge);
                }
                else
                {
                    // Consider が、相手が逆向きの境界辺1本だけであることを検査済み。
                    RemoveBoundary(list[0]);
                    list.Add(edge);
                }
            }

            private static int FindCorner(Face face, int a, int b)
            {
                for (int i = 0; i < face.VertexCount; i++)
                    if (face.VertexIndices[i] == a && face.VertexIndices[(i + 1) % face.VertexCount] == b) return i;
                return -1;
            }

            private void InsertCorner(Face face, int corner, Vertex vertex, int vertexIndex, float t)
            {
                int count = face.VertexCount, next = (corner + 1) % count;
                var a = mesh.Vertices[face.VertexIndices[corner]];
                var b = mesh.Vertices[face.VertexIndices[next]];
                Vector2 uv = Vector2.LerpUnclamped(ReadUV(a, face, corner), ReadUV(b, face, next), t);
                Vector3 normal = Vector3.LerpUnclamped(ReadNormal(a, face, corner), ReadNormal(b, face, next), t).normalized;

                // 描画は UV と法線を同じスロットで展開するため、ペアで追加する。
                int slotCount = Math.Max(vertex.UVs.Count, vertex.Normals.Count);
                while (vertex.UVs.Count < slotCount) vertex.UVs.Add(Vector2.zero);
                while (vertex.Normals.Count < slotCount) vertex.Normals.Add(Vector3.zero);
                int slot = slotCount;
                for (int i = 0; i < slotCount; i++)
                    if (vertex.UVs[i].Equals(uv) && vertex.Normals[i].Equals(normal)) { slot = i; break; }
                if (slot == slotCount) { vertex.UVs.Add(uv); vertex.Normals.Add(normal); }

                while (face.UVIndices.Count < count) face.UVIndices.Add(0);
                while (face.NormalIndices.Count < count) face.NormalIndices.Add(0);
                face.VertexIndices.Insert(corner + 1, vertexIndex);
                face.UVIndices.Insert(corner + 1, slot);
                face.NormalIndices.Insert(corner + 1, slot);
            }

            private static Vector2 ReadUV(Vertex v, Face face, int corner)
            {
                int slot = corner < face.UVIndices.Count ? face.UVIndices[corner] : 0;
                return slot >= 0 && slot < v.UVs.Count ? v.UVs[slot] : Vector2.zero;
            }

            private static Vector3 ReadNormal(Vertex v, Face face, int corner)
            {
                int slot = corner < face.NormalIndices.Count ? face.NormalIndices[corner] : 0;
                return slot >= 0 && slot < v.Normals.Count ? v.Normals[slot] : Vector3.zero;
            }
        }
    }
}
