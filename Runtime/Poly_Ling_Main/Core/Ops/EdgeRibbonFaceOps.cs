// EdgeRibbonFaceOps.cs
// 選択辺を中心線として、ワールド固定幅の帯面を組む。
//
// 【元オブジェクトは変更しない】
//   組んだ頂点と四角形は引数 dest（新しい MeshObject）へ入れる。
//   置き場所（新規オブジェクト / 既存へ追加 / 新規モデル）は呼び出し側が決める。
//
// 【座標系】
//   dest の頂点はワールド座標。元頂点のワールド位置は呼び出し側が GPU から
//   読んだ値（worldPositions）をそのまま使う。CPU で行列を掛けて求め直さない。
//   法線もワールドのまま入れる。
//
// 【材質】
//   ここでは触らない。面の MaterialIndex は既定のままにして、
//   配置側の ApplyGeneratedMaterialIndex が指定スロットを入れる
//   （他の図形生成と同じ規則）。
//
// 【梯子タグ】BeltStackDetector の規約に合わせる（RibbonBowMeshGenerator と同じ形）。
//   開始三角 = (Pstart, L, R)   … P を含まない辺 (L,R) が最初の rung
//   開始タグ = (Pstart, A, B)   … 3辺とも非共有・共有頂点は Pstart の1個だけ
//   終了三角 = (Pend,   L, R)   … 縦走査はここで終わり、Pend が終了点になる
//   付けるのは「分岐の無い開いた連なり」（端点がちょうど2個・次数3以上の頂点が無い）だけ。
//   閉路と分岐を含む連なりには付けない（Result.UntaggedChains に数える）。
//
// 【開始側の決め方】端点 2 個のワールド位置を比べる。
//   1. 上（+Y）から下
//   2. Y が同じなら 手前（-Z）から奥（+Z）
//   3. Z も同じなら 左（-X）から右（+X）
//   「同じ」は差が DirectionTolerance 以下。3 軸とも同じなら決まらないので付けない。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    public static class EdgeRibbonFaceOps
    {
        private const float Eps = 1e-8f;

        /// <summary>
        /// 先端三角・タグ三角の最小サイズ。
        /// 図形生成の重複頂点の結合（許容 0.001）で潰れないための下限。
        /// RibbonBowMeshGenerator.MinTagSize と同じ値。
        /// </summary>
        public const float MinTagSize = 0.005f;

        /// <summary>先端三角の長さ（帯の幅比）。RibbonBowParams の既定と同じ。</summary>
        public const float TipLengthScale = 0.50f;

        /// <summary>開始タグ三角の大きさ（帯の幅比）。RibbonBowParams の既定と同じ。</summary>
        public const float TagSizeScale = 0.40f;

        /// <summary>開始側の判定で「同じ座標」とみなす差（ワールド単位）。</summary>
        public const float DirectionTolerance = 1e-4f;

        public struct Result
        {
            public int RequestedEdges;
            public int ValidEdges;
            public int GeneratedVertices;
            public int GeneratedFaces;
            public int SkippedEdges;

            /// <summary>タグを付けた連なりの数。</summary>
            public int TaggedChains;

            /// <summary>タグを求められたが付けなかった連なりの数（閉路・分岐・開始側が決まらない）。</summary>
            public int UntaggedChains;

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

        /// <summary>
        /// mc の選択辺から帯面を組み、dest へ足す。mc は読むだけで変更しない。
        /// 複数オブジェクトぶんを 1 つの dest へまとめられる。
        /// </summary>
        /// <param name="worldPositions">mc の全頂点のワールド位置（GPU から読んだ値）。頂点番号で引く。</param>
        /// <param name="addStartTag">開いた連なりの開始端に開始三角と開始タグ三角を付ける。</param>
        /// <param name="addEndTag">開いた連なりの終了端に終了三角を付ける。</param>
        public static Result BuildInto(
            MeshContext mc,
            IEnumerable<VertexPair> selectedEdges,
            IReadOnlyList<Vector3> worldPositions,
            float widthWorld,
            bool addStartTag,
            bool addEndTag,
            MeshObject dest)
        {
            var result = new Result();
            var mo = mc?.MeshObject;
            if (mo == null || dest == null || selectedEdges == null ||
                worldPositions == null || widthWorld <= Eps)
                return result;

            if (worldPositions.Count < mo.Vertices.Count)
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

                Vector3 pa = worldPositions[a];
                Vector3 pb = worldPositions[b];
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
                Vector3 center = worldPositions[sourceIndex];
                Vector3 x = kv.Value;

                Vector3 leftWorld = center - x * half;
                Vector3 rightWorld = center + x * half;

                int leftIndex = AppendRibbonVertex(dest, mo.Vertices[sourceIndex], leftWorld);
                int rightIndex = AppendRibbonVertex(dest, mo.Vertices[sourceIndex], rightWorld);

                generated[sourceIndex] = new RibbonVertexPair
                {
                    LeftIndex = leftIndex,
                    RightIndex = rightIndex,
                    LeftWorld = leftWorld,
                    RightWorld = rightWorld
                };

                result.GeneratedVertices += 2;
            }

            // 辺ごとの四角形の頂点並び。タグの三角形の巻き順を揃えるために控える。
            var quadVertices = new List<int[]>(edges.Count);

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

                var face = new Face();
                face.VertexIndices.AddRange(vi);

                for (int k = 0; k < 4; k++)
                {
                    // dest はワールド座標なので法線もワールドのまま入れる。
                    int slot = dest.Vertices[vi[k]].GetOrAddUVNormal(uv[k], n);

                    face.UVIndices.Add(slot);
                    face.NormalIndices.Add(slot);
                }

                dest.AddFace(face);
                quadVertices.Add(vi);
                result.GeneratedFaces++;
            }

            if (addStartTag || addEndTag)
            {
                AppendTags(
                    mo, worldPositions, edges, incident, generated, quadVertices,
                    widthWorld, addStartTag, addEndTag, dest, ref result);
            }

            return result;
        }

        // ================================================================
        // 開始側の判定
        // ================================================================

        /// <summary>
        /// 端点 a と b のどちらを開始側にするか。
        /// a が開始なら 1、b が開始なら -1、決まらなければ 0。
        /// 上（+Y）→ 手前（-Z）→ 左（-X）の順で比べる。
        /// </summary>
        public static int CompareStart(Vector3 a, Vector3 b)
        {
            float dy = a.y - b.y;
            if (Mathf.Abs(dy) > DirectionTolerance) return dy > 0f ? 1 : -1;

            float dz = a.z - b.z;
            if (Mathf.Abs(dz) > DirectionTolerance) return dz < 0f ? 1 : -1;

            float dx = a.x - b.x;
            if (Mathf.Abs(dx) > DirectionTolerance) return dx < 0f ? 1 : -1;

            return 0;
        }

        // ================================================================
        // 梯子タグ
        // ================================================================

        private static void AppendTags(
            MeshObject mo,
            IReadOnlyList<Vector3> world,
            List<EdgeData> edges,
            Dictionary<int, List<int>> incident,
            Dictionary<int, RibbonVertexPair> generated,
            List<int[]> quadVertices,
            float widthWorld,
            bool addStartTag,
            bool addEndTag,
            MeshObject dest,
            ref Result result)
        {
            float tipLen = Mathf.Max(MinTagSize, widthWorld * TipLengthScale);
            float tagLen = Mathf.Max(MinTagSize, widthWorld * TagSizeScale);

            // 走査順を頂点番号順に固定する（Dictionary の列挙順に依存させない）。
            var roots = new List<int>(incident.Keys);
            roots.Sort();

            var visited = new HashSet<int>();

            foreach (int root in roots)
            {
                if (visited.Contains(root))
                    continue;

                var component = CollectComponent(root, edges, incident, visited);

                int endA = -1;
                int endB = -1;
                int endCount = 0;
                bool branched = false;

                for (int i = 0; i < component.Count; i++)
                {
                    int v = component[i];
                    int degree = incident[v].Count;

                    if (degree >= 3)
                    {
                        branched = true;
                    }
                    else if (degree == 1)
                    {
                        if (endCount == 0) endA = v;
                        else if (endCount == 1) endB = v;
                        endCount++;
                    }
                }

                if (branched || endCount != 2)
                {
                    result.UntaggedChains++;
                    continue;
                }

                int order = CompareStart(world[endA], world[endB]);
                if (order == 0)
                {
                    result.UntaggedChains++;
                    continue;
                }

                int start = order > 0 ? endA : endB;
                int end = order > 0 ? endB : endA;

                if (addStartTag)
                {
                    AppendStartTag(
                        mo, world, edges, incident, generated, quadVertices,
                        start, tipLen, tagLen, dest, ref result);
                }

                if (addEndTag)
                {
                    AppendEndTriangle(
                        mo, world, edges, incident, generated, quadVertices,
                        end, tipLen, dest, ref result);
                }

                result.TaggedChains++;
            }
        }

        private static List<int> CollectComponent(
            int root,
            List<EdgeData> edges,
            Dictionary<int, List<int>> incident,
            HashSet<int> visited)
        {
            var list = new List<int>();
            var queue = new Queue<int>();

            visited.Add(root);
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                int v = queue.Dequeue();
                list.Add(v);

                var inc = incident[v];
                for (int i = 0; i < inc.Count; i++)
                {
                    EdgeData e = edges[inc[i]];
                    int other = e.V1 == v ? e.V2 : e.V1;

                    if (visited.Add(other))
                        queue.Enqueue(other);
                }
            }

            return list;
        }

        /// <summary>開始端 s に開始三角と開始タグ三角を足す。</summary>
        private static void AppendStartTag(
            MeshObject mo,
            IReadOnlyList<Vector3> world,
            List<EdgeData> edges,
            Dictionary<int, List<int>> incident,
            Dictionary<int, RibbonVertexPair> generated,
            List<int[]> quadVertices,
            int s,
            float tipLen,
            float tagLen,
            MeshObject dest,
            ref Result result)
        {
            int ei = incident[s][0];
            EdgeData e = edges[ei];
            int n = e.V1 == s ? e.V2 : e.V1;

            Vector3 c = world[s];
            Vector3 inward = SafeNormalize(world[n] - c, DirectionAwayFromVertex(e, s));
            RibbonVertexPair pair = generated[s];
            Vector3 across = SafeNormalize(pair.RightWorld - pair.LeftWorld, e.X);

            // 開始三角
            Vector3 pTip = c - inward * tipLen;
            int ip = AppendRibbonVertex(dest, mo.Vertices[s], pTip);
            result.GeneratedVertices++;

            Vector3 tipNormal = AddRungTriangle(
                dest, quadVertices[ei],
                pair.LeftIndex, pair.RightIndex, ip,
                pair.LeftWorld, pair.RightWorld, pTip, e.Z);
            result.GeneratedFaces++;

            // 開始タグ。開始三角と共有するのは頂点 pTip だけ。
            Vector3 tagBase = pTip - inward * tagLen;
            Vector3 pa = tagBase + across * (tagLen * 0.5f);
            Vector3 pb = tagBase - across * (tagLen * 0.5f);

            int ia = AppendRibbonVertex(dest, mo.Vertices[s], pa);
            int ib = AppendRibbonVertex(dest, mo.Vertices[s], pb);
            result.GeneratedVertices += 2;

            AddTriangleFacing(dest, ip, ia, ib, pTip, pa, pb, tipNormal);
            result.GeneratedFaces++;
        }

        /// <summary>終了端 s に終了三角を足す。</summary>
        private static void AppendEndTriangle(
            MeshObject mo,
            IReadOnlyList<Vector3> world,
            List<EdgeData> edges,
            Dictionary<int, List<int>> incident,
            Dictionary<int, RibbonVertexPair> generated,
            List<int[]> quadVertices,
            int s,
            float tipLen,
            MeshObject dest,
            ref Result result)
        {
            int ei = incident[s][0];
            EdgeData e = edges[ei];
            int n = e.V1 == s ? e.V2 : e.V1;

            Vector3 c = world[s];
            Vector3 outward = SafeNormalize(c - world[n], -DirectionAwayFromVertex(e, s));
            RibbonVertexPair pair = generated[s];

            Vector3 pEnd = c + outward * tipLen;
            int ip = AppendRibbonVertex(dest, mo.Vertices[s], pEnd);
            result.GeneratedVertices++;

            AddRungTriangle(
                dest, quadVertices[ei],
                pair.LeftIndex, pair.RightIndex, ip,
                pair.LeftWorld, pair.RightWorld, pEnd, e.Z);
            result.GeneratedFaces++;
        }

        /// <summary>
        /// rung（左右の頂点の組）と頂点 apex で三角形を足す。
        /// 隣の四角形が L→R の順に持っていれば R→L でたどり、共有辺で面の向きを揃える。
        /// </summary>
        /// <returns>足した三角形の法線。</returns>
        private static Vector3 AddRungTriangle(
            MeshObject dest,
            int[] quad,
            int leftIndex,
            int rightIndex,
            int apex,
            Vector3 leftWorld,
            Vector3 rightWorld,
            Vector3 apexWorld,
            Vector3 fallbackNormal)
        {
            bool quadLeftToRight = false;
            for (int k = 0; k < quad.Length; k++)
            {
                if (quad[k] == leftIndex && quad[(k + 1) % quad.Length] == rightIndex)
                {
                    quadLeftToRight = true;
                    break;
                }
            }

            int[] vi;
            Vector3 p0, p1;

            if (quadLeftToRight)
            {
                vi = new[] { rightIndex, leftIndex, apex };
                p0 = rightWorld;
                p1 = leftWorld;
            }
            else
            {
                vi = new[] { leftIndex, rightIndex, apex };
                p0 = leftWorld;
                p1 = rightWorld;
            }

            Vector3 n = SafeNormalize(Vector3.Cross(p1 - p0, apexWorld - p0), fallbackNormal);
            AddTriangle(dest, vi, n);
            return n;
        }

        /// <summary>三角形を足す。法線が referenceNormal と逆向きなら巻き順を入れ替える。</summary>
        private static void AddTriangleFacing(
            MeshObject dest,
            int i0, int i1, int i2,
            Vector3 p0, Vector3 p1, Vector3 p2,
            Vector3 referenceNormal)
        {
            Vector3 n = SafeNormalize(Vector3.Cross(p1 - p0, p2 - p0), referenceNormal);

            if (Vector3.Dot(n, referenceNormal) < 0f)
            {
                AddTriangle(dest, new[] { i0, i2, i1 }, -n);
                return;
            }

            AddTriangle(dest, new[] { i0, i1, i2 }, n);
        }

        private static void AddTriangle(MeshObject dest, int[] vi, Vector3 normal)
        {
            Vector2[] uv =
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 1f)
            };

            var face = new Face();
            face.VertexIndices.AddRange(vi);

            for (int k = 0; k < 3; k++)
            {
                int slot = dest.Vertices[vi[k]].GetOrAddUVNormal(uv[k], normal);
                face.UVIndices.Add(slot);
                face.NormalIndices.Add(slot);
            }

            dest.AddFace(face);
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
        // 頂点
        // ================================================================

        /// <summary>
        /// 生成頂点を dest へ足す。位置はワールド座標のまま入れる。
        /// パーツ ID とボーンウェイトは元頂点から写す。
        /// </summary>
        private static int AppendRibbonVertex(
            MeshObject dest,
            Vertex source,
            Vector3 worldPosition)
        {
            var v = new Vertex(worldPosition)
            {
                PartsId = source.PartsId,
                SubId = source.SubId,
                BoneWeight = source.BoneWeight,
                MirrorBoneWeight = source.MirrorBoneWeight,
                Flags = VertexFlags.None
            };

            return dest.AddVertex(v);
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
