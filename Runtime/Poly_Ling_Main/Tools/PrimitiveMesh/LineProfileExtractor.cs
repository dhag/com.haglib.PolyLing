// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/LineProfileExtractor.cs
// メッシュの2頂点ライン(補助線)群とプロファイル編集データを相互変換するユーティリティ。
// 図形生成パネルの「取り込み(メッシュ→プロファイル)」「反映(プロファイル→メッシュ)」用。
// 方針: Z を破棄し XY をそのまま扱う(座標変換なし)。
// 連結・ループ解析は LineExtrudeTool と同方式を独立実装(LineExtrudeTool 本体は不変=回帰回避)。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Profile2DExtrude;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>
    /// 2頂点Face(補助線)群 ⇔ プロファイル点列/ループ の変換ユーティリティ。
    /// </summary>
    public static class LineProfileExtractor
    {
        // ================================================================
        // 収集
        // ================================================================

        /// <summary>指定 MeshObject 内の全2頂点 Face のインデックスを返す。</summary>
        public static List<int> CollectLineFaceIndices(MeshObject mesh)
        {
            var result = new List<int>();
            if (mesh == null) return result;
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                if (mesh.Faces[i].VertexCount == 2)
                    result.Add(i);
            }
            return result;
        }

        // ================================================================
        // メッシュ → プロファイル
        // ================================================================

        /// <summary>
        /// 2頂点ライン群を順序連結し、開いた折れ線(Revolution プロファイル用)として XY を返す。
        /// 複数チェーンがある場合は頂点数が最多のものを採用。連結不能なら空リスト。
        /// </summary>
        public static List<Vector2> ExtractPolyline(MeshObject mesh, IEnumerable<int> lineFaceIndices)
        {
            var result = new List<Vector2>();
            if (mesh == null) return result;

            var chains = BuildChains(mesh, lineFaceIndices);
            List<int> best = null;
            foreach (var c in chains)
            {
                if (best == null || c.Count > best.Count)
                    best = c;
            }
            if (best == null) return result;

            foreach (int vi in best)
            {
                Vector3 p = mesh.Vertices[vi].Position;
                result.Add(new Vector2(p.x, p.y));
            }
            return result;
        }

        /// <summary>
        /// 2頂点ライン群を閉ループ解析し、Profile2D 用の Loop 群として XY を返す。
        /// hole 判定は Shoelace 符号(Y上向き前提。LineExtrudeTool と同基準)。
        /// </summary>
        public static List<Loop> ExtractLoops(MeshObject mesh, IEnumerable<int> lineFaceIndices)
        {
            var loops = new List<Loop>();
            if (mesh == null) return loops;

            var lineIdx = new List<int>();
            foreach (int fi in lineFaceIndices)
            {
                if (fi >= 0 && fi < mesh.Faces.Count && mesh.Faces[fi].VertexCount == 2)
                    lineIdx.Add(fi);
            }
            if (lineIdx.Count < 3) return loops;

            var remaining = new HashSet<int>(lineIdx);

            // 頂点 → 接続ライン マップ
            var vertexToLines = new Dictionary<int, List<int>>();
            foreach (int fi in lineIdx)
            {
                var f = mesh.Faces[fi];
                int v0 = f.VertexIndices[0];
                int v1 = f.VertexIndices[1];
                if (!vertexToLines.TryGetValue(v0, out var l0)) { l0 = new List<int>(); vertexToLines[v0] = l0; }
                if (!vertexToLines.TryGetValue(v1, out var l1)) { l1 = new List<int>(); vertexToLines[v1] = l1; }
                l0.Add(fi);
                l1.Add(fi);
            }

            while (remaining.Count >= 3)
            {
                var vidx = TryBuildLoop(mesh, remaining, vertexToLines);
                if (vidx != null && vidx.Count >= 3)
                {
                    var loop = new Loop();
                    foreach (int vi in vidx)
                    {
                        Vector3 p = mesh.Vertices[vi].Position;
                        loop.Points.Add(new Vector2(p.x, p.y));
                    }
                    loop.IsHole = !IsClockwise(mesh, vidx);
                    loops.Add(loop);
                }
                else
                {
                    break;
                }
            }
            return loops;
        }

        // ================================================================
        // プロファイル → メッシュ
        // ================================================================

        /// <summary>
        /// 折れ線(点列)を2頂点 Face 群の MeshObject にする。closed=true で末尾→先頭も閉じる。
        /// 点は (x, y, 0) として配置(Z=0)。
        /// </summary>
        public static MeshObject PolylineToLineMesh(IReadOnlyList<Vector2> points, string name, bool closed)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(name) ? "Profile" : name);
            if (points == null || points.Count < 2) return mo;

            for (int i = 0; i < points.Count; i++)
                mo.Vertices.Add(NewLineVertex(points[i]));

            for (int i = 0; i < points.Count - 1; i++)
                mo.Faces.Add(NewLineFace(i, i + 1));

            if (closed && points.Count >= 3)
                mo.Faces.Add(NewLineFace(points.Count - 1, 0));

            return mo;
        }

        /// <summary>
        /// Loop 群を2頂点 Face 群の MeshObject にする(各ループを閉じる)。
        /// 点は (x, y, 0) として配置(Z=0)。
        /// </summary>
        public static MeshObject LoopsToLineMesh(IEnumerable<Loop> loops, string name)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(name) ? "Profile2D" : name);
            if (loops == null) return mo;

            foreach (var lp in loops)
            {
                if (lp == null || lp.Points == null || lp.Points.Count < 2) continue;

                int baseIdx = mo.Vertices.Count;
                for (int i = 0; i < lp.Points.Count; i++)
                    mo.Vertices.Add(NewLineVertex(lp.Points[i]));

                for (int i = 0; i < lp.Points.Count - 1; i++)
                    mo.Faces.Add(NewLineFace(baseIdx + i, baseIdx + i + 1));

                if (lp.Points.Count >= 3)
                    mo.Faces.Add(NewLineFace(baseIdx + lp.Points.Count - 1, baseIdx));
            }
            return mo;
        }

        // ================================================================
        // 内部: 頂点/ライン生成
        // ================================================================

        private static Vertex NewLineVertex(Vector2 xy)
        {
            // UV/Normal の index 0 を有効にするため 3引数コンストラクタを使用。
            return new Vertex(new Vector3(xy.x, xy.y, 0f), Vector2.zero, Vector3.forward);
        }

        private static Face NewLineFace(int a, int b)
        {
            var f = new Face { MaterialIndex = 0 };
            f.VertexIndices.Add(a); f.UVIndices.Add(0); f.NormalIndices.Add(0);
            f.VertexIndices.Add(b); f.UVIndices.Add(0); f.NormalIndices.Add(0);
            return f;
        }

        // ================================================================
        // 内部: 開いたチェーン探索(ExtractPolyline 用)
        // ================================================================

        /// <summary>
        /// ライン群を極大チェーン(頂点インデックス列)へ分解して返す。
        /// 端点(次数1)から優先的に開始し、残りは閉路として辿る。
        /// </summary>
        private static List<List<int>> BuildChains(MeshObject mesh, IEnumerable<int> lineFaceIndices)
        {
            var chains = new List<List<int>>();

            var adj = new Dictionary<int, List<int>>();      // 頂点 → 接続エッジ(=lineFace index)
            var edgeV0 = new Dictionary<int, int>();
            var edgeV1 = new Dictionary<int, int>();
            var edgeIds = new List<int>();

            foreach (int fi in lineFaceIndices)
            {
                if (fi < 0 || fi >= mesh.Faces.Count) continue;
                var f = mesh.Faces[fi];
                if (f.VertexCount != 2) continue;
                int a = f.VertexIndices[0];
                int b = f.VertexIndices[1];
                if (a == b) continue;

                edgeV0[fi] = a; edgeV1[fi] = b; edgeIds.Add(fi);
                if (!adj.TryGetValue(a, out var la)) { la = new List<int>(); adj[a] = la; }
                if (!adj.TryGetValue(b, out var lb)) { lb = new List<int>(); adj[b] = lb; }
                la.Add(fi);
                lb.Add(fi);
            }
            if (edgeIds.Count == 0) return chains;

            var usedEdges = new HashSet<int>();

            // 1) 端点(次数1)から開いたチェーンを辿る
            foreach (var kv in adj)
            {
                if (kv.Value.Count != 1) continue;
                var chain = WalkChain(kv.Key, adj, edgeV0, edgeV1, usedEdges);
                if (chain != null && chain.Count >= 2) chains.Add(chain);
            }

            // 2) 残り(閉路)を任意の未使用エッジから辿る
            foreach (int e in edgeIds)
            {
                if (usedEdges.Contains(e)) continue;
                var chain = WalkChain(edgeV0[e], adj, edgeV0, edgeV1, usedEdges);
                if (chain != null && chain.Count >= 2) chains.Add(chain);
            }

            return chains;
        }

        private static List<int> WalkChain(
            int start,
            Dictionary<int, List<int>> adj,
            Dictionary<int, int> edgeV0,
            Dictionary<int, int> edgeV1,
            HashSet<int> usedEdges)
        {
            var chain = new List<int>();
            int cur = start;
            chain.Add(cur);

            while (true)
            {
                if (!adj.TryGetValue(cur, out var incident)) break;

                int nextEdge = -1;
                foreach (int e in incident)
                {
                    if (!usedEdges.Contains(e)) { nextEdge = e; break; }
                }
                if (nextEdge < 0) break;

                usedEdges.Add(nextEdge);
                int other = (edgeV0[nextEdge] == cur) ? edgeV1[nextEdge] : edgeV0[nextEdge];
                cur = other;
                if (cur == start) break;   // 閉路: 先頭を重複追加しない
                chain.Add(cur);
            }
            return chain;
        }

        // ================================================================
        // 内部: 閉ループ探索(ExtractLoops 用。LineExtrudeTool と同方式)
        // ================================================================

        private static List<int> TryBuildLoop(
            MeshObject mesh,
            HashSet<int> remainingLines,
            Dictionary<int, List<int>> vertexToLines)
        {
            if (remainingLines.Count == 0) return null;

            var vertexIndices = new List<int>();
            var usedLines = new HashSet<int>();

            int firstLineIdx = -1;
            foreach (int idx in remainingLines) { firstLineIdx = idx; break; }
            if (firstLineIdx < 0) return null;

            var firstFace = mesh.Faces[firstLineIdx];
            int startVertex = firstFace.VertexIndices[0];
            int currentVertex = startVertex;

            vertexIndices.Add(currentVertex);
            usedLines.Add(firstLineIdx);
            currentVertex = firstFace.VertexIndices[1];

            int maxIterations = remainingLines.Count + 1;
            int iterations = 0;

            while (currentVertex != startVertex && iterations < maxIterations)
            {
                iterations++;
                vertexIndices.Add(currentVertex);

                if (!vertexToLines.TryGetValue(currentVertex, out var connectedLines))
                    break;

                int nextLineIdx = -1;
                foreach (int lineIdx in connectedLines)
                {
                    if (remainingLines.Contains(lineIdx) && !usedLines.Contains(lineIdx))
                    {
                        nextLineIdx = lineIdx;
                        break;
                    }
                }
                if (nextLineIdx < 0) break;

                usedLines.Add(nextLineIdx);

                var nextFace = mesh.Faces[nextLineIdx];
                currentVertex = (nextFace.VertexIndices[0] == currentVertex)
                    ? nextFace.VertexIndices[1]
                    : nextFace.VertexIndices[0];
            }

            if (currentVertex == startVertex && vertexIndices.Count >= 3)
            {
                foreach (int lineIdx in usedLines)
                    remainingLines.Remove(lineIdx);
                return vertexIndices;
            }
            return null;
        }

        /// <summary>ループが時計回りか(Shoelace, XY平面, Y上向き)。</summary>
        private static bool IsClockwise(MeshObject mesh, List<int> vertexIndices)
        {
            if (vertexIndices.Count < 3) return true;

            float sum = 0f;
            for (int i = 0; i < vertexIndices.Count; i++)
            {
                int next = (i + 1) % vertexIndices.Count;
                Vector3 p0 = mesh.Vertices[vertexIndices[i]].Position;
                Vector3 p1 = mesh.Vertices[vertexIndices[next]].Position;
                sum += (p1.x - p0.x) * (p1.y + p0.y);
            }
            return sum > 0f;
        }
    }
}
