// EdgeRibbonFaceOps.cs
// 選択辺を中心線として、ワールド固定幅の帯面を生成する。
// 元の中心線は変更せず、生成頂点・四角形を末尾へ追加する。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    public static class EdgeRibbonFaceOps
    {
        private const float Eps = 1e-8f;

        public struct Result
        {
            public int RequestedEdges;
            public int ValidEdges;
            public int GeneratedVertices;
            public int GeneratedFaces;
            public int SkippedEdges;

            public bool Changed => GeneratedFaces > 0;
        }

        private sealed class EdgeData
        {
            public int V1;
            public int V2;
            public Vector3 X;
            public Vector3 Y;
            public Vector3 Z;
        }

        private struct RibbonVertexPair
        {
            public int LeftIndex;
            public int RightIndex;
            public Vector3 LeftWorld;
            public Vector3 RightWorld;
        }

        public static Result Append(
            MeshContext mc,
            IEnumerable<VertexPair> selectedEdges,
            float widthWorld,
            int materialIndex)
        {
            var result = new Result();
            var mo = mc?.MeshObject;
            if (mo == null || selectedEdges == null || widthWorld <= Eps)
                return result;

            var edges = new List<EdgeData>();
            var incident = new Dictionary<int, List<int>>();

            foreach (var pair in selectedEdges)
            {
                result.RequestedEdges++;

                int a = pair.V1;
                int b = pair.V2;

                if (a == b || a < 0 || b < 0 ||
                    a >= mo.Vertices.Count || b >= mo.Vertices.Count)
                {
                    result.SkippedEdges++;
                    continue;
                }

                Vector3 pa = VertexWorld(mc, a);
                Vector3 pb = VertexWorld(mc, b);
                Vector3 d = pb - pa;

                if (d.sqrMagnitude <= Eps)
                {
                    result.SkippedEdges++;
                    continue;
                }

                Vector3 y = d.normalized;
                BuildFrame(y, out Vector3 x, out Vector3 z);

                int edgeIndex = edges.Count;
                edges.Add(new EdgeData
                {
                    V1 = a,
                    V2 = b,
                    X = x,
                    Y = y,
                    Z = z
                });

                AddIncident(incident, a, edgeIndex);
                AddIncident(incident, b, edgeIndex);
            }

            result.ValidEdges = edges.Count;
            if (edges.Count == 0)
                return result;

            // 各グラフ頂点で幅方向 X を一つにまとめる。
            // これにより接続点の左右頂点を隣接面で共有する。
            var axisAtVertex = new Dictionary<int, Vector3>();

            foreach (var kv in incident)
            {
                int vertexIndex = kv.Key;
                var inc = kv.Value;

                Vector3 anchor = edges[inc[0]].X;
                Vector3 sum = Vector3.zero;

                for (int i = 0; i < inc.Count; i++)
                {
                    Vector3 x = edges[inc[i]].X;
                    if (Vector3.Dot(x, anchor) < 0f)
                        x = -x;
                    sum += x;
                }

                Vector3 xv = SafeNormalize(sum, anchor);

                // 通常の折れ線(次数2)では平均接線に直交させる。
                if (inc.Count == 2)
                {
                    Vector3 t0 = DirectionAwayFromVertex(edges[inc[0]], vertexIndex);
                    Vector3 t1 = DirectionAwayFromVertex(edges[inc[1]], vertexIndex);

                    Vector3 t = t0 + t1;
                    if (t.sqrMagnitude > Eps)
                    {
                        t.Normalize();
                        Vector3 projected = Vector3.ProjectOnPlane(xv, t);
                        if (projected.sqrMagnitude > Eps)
                            xv = projected.normalized;
                    }
                }

                axisAtVertex[vertexIndex] = xv;
            }

            AlignAxisSigns(edges, axisAtVertex);

            float half = widthWorld * 0.5f;
            var generated = new Dictionary<int, RibbonVertexPair>();

            foreach (var kv in axisAtVertex)
            {
                int sourceIndex = kv.Key;
                Vector3 center = VertexWorld(mc, sourceIndex);
                Vector3 x = kv.Value;

                Vector3 leftWorld = center - x * half;
                Vector3 rightWorld = center + x * half;

                int leftIndex = AppendDerivedVertex(mc, sourceIndex, leftWorld);
                int rightIndex = AppendDerivedVertex(mc, sourceIndex, rightWorld);

                generated[sourceIndex] = new RibbonVertexPair
                {
                    LeftIndex = leftIndex,
                    RightIndex = rightIndex,
                    LeftWorld = leftWorld,
                    RightWorld = rightWorld
                };

                result.GeneratedVertices += 2;
            }

            materialIndex = Mathf.Max(0, materialIndex);

            for (int i = 0; i < edges.Count; i++)
            {
                EdgeData e = edges[i];
                RibbonVertexPair a = generated[e.V1];
                RibbonVertexPair b = generated[e.V2];

                int[] vi =
                {
                    a.LeftIndex,
                    a.RightIndex,
                    b.RightIndex,
                    b.LeftIndex
                };

                int[] source =
                {
                    e.V1, e.V1, e.V2, e.V2
                };

                Vector3[] wp =
                {
                    a.LeftWorld,
                    a.RightWorld,
                    b.RightWorld,
                    b.LeftWorld
                };

                Vector2[] uv =
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 1f),
                    new Vector2(0f, 1f)
                };

                Vector3 n = QuadNormal(wp[0], wp[1], wp[2], wp[3], e.Z);

                if (Vector3.Dot(n, e.Z) < 0f)
                {
                    vi = new[]
                    {
                        a.RightIndex,
                        a.LeftIndex,
                        b.LeftIndex,
                        b.RightIndex
                    };

                    source = new[]
                    {
                        e.V1, e.V1, e.V2, e.V2
                    };

                    wp = new[]
                    {
                        a.RightWorld,
                        a.LeftWorld,
                        b.LeftWorld,
                        b.RightWorld
                    };

                    uv = new[]
                    {
                        new Vector2(1f, 0f),
                        new Vector2(0f, 0f),
                        new Vector2(0f, 1f),
                        new Vector2(1f, 1f)
                    };

                    n = QuadNormal(wp[0], wp[1], wp[2], wp[3], e.Z);
                }

                var face = new Face
                {
                    MaterialIndex = materialIndex
                };

                face.VertexIndices.AddRange(vi);

                for (int k = 0; k < 4; k++)
                {
                    Vector3 localNormal =
                        WorldNormalToVertexLocal(mc, source[k], n);

                    int slot =
                        mo.Vertices[vi[k]].GetOrAddUVNormal(uv[k], localNormal);

                    face.UVIndices.Add(slot);
                    face.NormalIndices.Add(slot);
                }

                mo.AddFace(face);
                result.GeneratedFaces++;
            }

            return result;
        }

        // ================================================================
        // ローカルフレーム
        // ================================================================

        private static void BuildFrame(
            Vector3 y,
            out Vector3 x,
            out Vector3 z)
        {
            // global Z と global Y のうち、y とより直交に近い方を選ぶ。
            // 奥行方向に近い線分では global Y、それ以外では global Z となる。
            Vector3 reference =
                Mathf.Abs(Vector3.Dot(y, Vector3.forward))
                <= Mathf.Abs(Vector3.Dot(y, Vector3.up))
                    ? Vector3.forward
                    : Vector3.up;

            z = Vector3.ProjectOnPlane(reference, y);

            if (z.sqrMagnitude <= Eps)
            {
                Vector3 other =
                    reference == Vector3.forward
                        ? Vector3.up
                        : Vector3.forward;

                z = Vector3.ProjectOnPlane(other, y);
            }

            if (z.sqrMagnitude <= Eps)
                z = Vector3.ProjectOnPlane(Vector3.right, y);

            z = SafeNormalize(z, Vector3.forward);

            x = Vector3.Cross(y, z);
            x = SafeNormalize(x, Vector3.right);

            // 数値誤差を除いて直交フレームへ戻す。
            z = Vector3.Cross(x, y);
            z = SafeNormalize(z, Vector3.forward);
        }

        private static Vector3 SafeNormalize(
            Vector3 v,
            Vector3 fallback)
        {
            if (v.sqrMagnitude > Eps)
                return v.normalized;

            if (fallback.sqrMagnitude > Eps)
                return fallback.normalized;

            return Vector3.right;
        }

        // ================================================================
        // 接続グラフ
        // ================================================================

        private static void AddIncident(
            Dictionary<int, List<int>> incident,
            int vertex,
            int edgeIndex)
        {
            if (!incident.TryGetValue(vertex, out var list))
            {
                list = new List<int>();
                incident[vertex] = list;
            }

            list.Add(edgeIndex);
        }

        private static Vector3 DirectionAwayFromVertex(
            EdgeData e,
            int vertex)
        {
            return vertex == e.V1 ? e.Y : -e.Y;
        }

        private static void AlignAxisSigns(
            List<EdgeData> edges,
            Dictionary<int, Vector3> axisAtVertex)
        {
            var adjacency = new Dictionary<int, List<int>>();

            for (int i = 0; i < edges.Count; i++)
            {
                AddAdjacent(adjacency, edges[i].V1, edges[i].V2);
                AddAdjacent(adjacency, edges[i].V2, edges[i].V1);
            }

            var visited = new HashSet<int>();
            var queue = new Queue<int>();

            foreach (int root in axisAtVertex.Keys)
            {
                if (visited.Contains(root))
                    continue;

                visited.Add(root);
                queue.Enqueue(root);

                while (queue.Count > 0)
                {
                    int a = queue.Dequeue();

                    if (!adjacency.TryGetValue(a, out var neighbours))
                        continue;

                    for (int i = 0; i < neighbours.Count; i++)
                    {
                        int b = neighbours[i];

                        if (!axisAtVertex.ContainsKey(b) ||
                            visited.Contains(b))
                            continue;

                        if (Vector3.Dot(axisAtVertex[a], axisAtVertex[b]) < 0f)
                            axisAtVertex[b] = -axisAtVertex[b];

                        visited.Add(b);
                        queue.Enqueue(b);
                    }
                }
            }
        }

        private static void AddAdjacent(
            Dictionary<int, List<int>> adjacency,
            int a,
            int b)
        {
            if (!adjacency.TryGetValue(a, out var list))
            {
                list = new List<int>();
                adjacency[a] = list;
            }

            list.Add(b);
        }

        // ================================================================
        // 座標変換
        // ================================================================

        private static Vector3 VertexWorld(
            MeshContext mc,
            int vertexIndex)
        {
            Vector3 local =
                mc.MeshObject.Vertices[vertexIndex].Position;

            return mc.VertexMatrix(vertexIndex)
                     .MultiplyPoint3x4(local);
        }

        private static int AppendDerivedVertex(
            MeshContext mc,
            int sourceIndex,
            Vector3 worldPosition)
        {
            var mo = mc.MeshObject;
            Vertex source = mo.Vertices[sourceIndex];

            Matrix4x4 vertexMatrix = mc.VertexMatrix(sourceIndex);
            Vector3 local =
                vertexMatrix.inverse.MultiplyPoint3x4(worldPosition);

            var v = new Vertex(local)
            {
                PartsId = source.PartsId,
                SubId = source.SubId,
                BoneWeight = source.BoneWeight,
                MirrorBoneWeight = source.MirrorBoneWeight,
                Flags = VertexFlags.None
            };

            return mo.AddVertex(v);
        }

        private static Vector3 WorldNormalToVertexLocal(
            MeshContext mc,
            int sourceIndex,
            Vector3 worldNormal)
        {
            Matrix4x4 m = mc.VertexMatrix(sourceIndex);

            // local normal -> world normal = inverse-transpose(M)
            // よって逆変換は transpose(M)。
            Vector3 n = m.transpose.MultiplyVector(worldNormal);

            return SafeNormalize(n, Vector3.forward);
        }

        private static Vector3 QuadNormal(
            Vector3 p0,
            Vector3 p1,
            Vector3 p2,
            Vector3 p3,
            Vector3 fallback)
        {
            Vector3 n0 = Vector3.Cross(p1 - p0, p2 - p0);
            Vector3 n1 = Vector3.Cross(p2 - p0, p3 - p0);
            Vector3 n = n0 + n1;

            if (n.sqrMagnitude <= Eps)
                n = n0.sqrMagnitude > Eps ? n0 : n1;

            return SafeNormalize(n, fallback);
        }
    }
}
