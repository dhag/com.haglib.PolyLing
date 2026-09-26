// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/LineProfileExtractor.cs
// メッシュの2頂点ライン(補助線)群とプロファイル編集データを相互変換するユーティリティ。
// 図形生成パネルの「取り込み(メッシュ→プロファイル)」「反映(プロファイル→メッシュ)」用。
// 方針: 点は 3D（モデルのローカル座標）のまま扱う（座標変換なし）。
//   穴の判定など向きが要るところだけ xy で求める。
// 連結・ループ解析はこのクラス内で独立実装している(旧 LineExtrudeTool は廃止済み)。
// 【線分群】線分群（MeshObject.LineGroups）があれば取り込みはそれを正典として読み、
//   無いときだけ 2 頂点の面をつなぎ直す。反映は面と同じ並びの線分群も作る（LineGroupOps）。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
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
        // 正規化
        // ================================================================

        /// <summary>
        /// AABB（x,y,z）の最長辺が 1 になるよう等方スケールし、AABB の最小角を原点へ寄せる。
        /// 最長辺が 0（全点が同一位置）なら null。
        ///
        /// 【なぜ要るか】
        ///   フリル／パイプの断面座標は rung 長で正規化された系にある。
        ///   描画オブジェクト（2頂点ライン）から取り込んだ点列は元メッシュの
        ///   ローカル座標そのままなので、そのまま断面として使うと寸法が合わない。
        ///   取り込みのときだけこれを掛ける（反映は生データのまま書き出す）。
        ///   z も x・y と同じ扱いにする（最小を原点へ、同じ倍率）。
        /// </summary>
        public static List<Vector3> NormalizeToUnitSpan(IReadOnlyList<Vector3> src)
        {
            if (src == null || src.Count < 2) return null;

            Vector3 min = src[0], max = src[0];
            for (int i = 1; i < src.Count; i++)
            {
                min = Vector3.Min(min, src[i]);
                max = Vector3.Max(max, src[i]);
            }

            Vector3 size = max - min;
            float span = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (span <= 1e-6f) return null;

            float k = 1f / span;
            var dst = new List<Vector3>(src.Count);
            for (int i = 0; i < src.Count; i++)
                dst.Add((src[i] - min) * k);
            return dst;
        }

        // ================================================================
        // メッシュ → プロファイル
        // ================================================================

        /// <summary>
        /// 2頂点ライン群を順序連結し、開いた折れ線(Revolution プロファイル用)として 3D の点列を返す。
        /// 複数チェーンがある場合は頂点数が最多のものを採用。連結不能なら空リスト。
        /// </summary>
        public static List<Vector3> ExtractPolyline(MeshObject mesh, IEnumerable<int> lineFaceIndices)
        {
            var result = new List<Vector3>();
            if (mesh == null) return result;

            // 線分群があれば、それが順序の正典なので線分群から読む（点数が最多の群）。
            if (HasLineGroups(mesh))
            {
                LineGroup bestGroup = null;
                foreach (var g in mesh.LineGroups)
                {
                    if (g?.Order == null || g.Order.Count < 2) continue;
                    if (bestGroup == null || g.Order.Count > bestGroup.Order.Count) bestGroup = g;
                }
                if (bestGroup != null)
                    result.AddRange(LineCurveSampler.SampleLocal(mesh, bestGroup, LineCurveSampler.DefaultSegmentsPerSpan));
                return result;
            }

            var chains = BuildChains(mesh, lineFaceIndices);
            List<int> best = null;
            foreach (var c in chains)
            {
                if (best == null || c.Count > best.Count)
                    best = c;
            }
            if (best == null) return result;

            foreach (int vi in best)
                result.Add(mesh.Vertices[vi].Position);
            return result;
        }

        /// <summary>
        /// 2頂点ライン群を閉ループ解析し、Profile2D 用の Loop 群として 3D の点列を返す。
        /// hole 判定は xy の Shoelace 符号(Y上向き前提)。反時計回りが外周、時計回りが穴。
        /// </summary>
        public static List<Loop> ExtractLoops(MeshObject mesh, IEnumerable<int> lineFaceIndices)
        {
            var loops = new List<Loop>();
            if (mesh == null) return loops;

            // 線分群があれば、閉じた群をそのままループとして読む。
            if (HasLineGroups(mesh))
            {
                foreach (var g in mesh.LineGroups)
                {
                    if (g?.Order == null || !g.Closed || g.Order.Count < 3) continue;
                    var vidxG = new List<int>();
                    foreach (int vi in g.Order)
                        if (vi >= 0 && vi < mesh.Vertices.Count) vidxG.Add(vi);
                    if (vidxG.Count < 3) continue;

                    var loopG = new Loop();
                    loopG.Points.AddRange(LineCurveSampler.SampleLocal(mesh, g, LineCurveSampler.DefaultSegmentsPerSpan));
                    // 向きは曲線に分割した点列の符号付き面積で決める（負 = 時計回り = 穴）。
                    float area2 = 0f;
                    for (int i = 0; i < loopG.Points.Count; i++)
                    {
                        var a = loopG.Points[i];
                        var b = loopG.Points[(i + 1) % loopG.Points.Count];
                        area2 += a.x * b.y - b.x * a.y;
                    }
                    loopG.IsHole = area2 < 0f;
                    loops.Add(loopG);
                }
                return loops;
            }

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
                        loop.Points.Add(mesh.Vertices[vi].Position);
                    // 反時計回りが外周、時計回りが穴。
                    //
                    // 【以前は逆だった】
                    //   IsHole = !IsClockwise と書いていたため、外周を穴と判定していた。
                    //   図形生成パネルが持つ 2D 押し出しの既定の外周
                    //   （PlayerPrimitiveMeshSubPanel.cs:2916-2923 の
                    //    (-r,-r)→(r,-r)→(r,r)→(-r,r)、IsHole = false）は反時計回りで、
                    //   これを IsClockwise に掛けると false になる。
                    //   反転すると true になり、パネル自身の既定の外周が穴として返っていた。
                    //   「取り込み(メッシュ→プロファイル)」を通すたびに外周と穴が入れ替わり、
                    //   出来上がりが裏返って見える原因になっていた。
                    loop.IsHole = IsClockwise(mesh, vidx);
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
        /// 点は (x, y, z) のまま配置。
        /// </summary>
        public static MeshObject PolylineToLineMesh(IReadOnlyList<Vector3> points, string name, bool closed)
        {
            var mo = new MeshObject(string.IsNullOrEmpty(name) ? "Profile" : name);
            if (points == null || points.Count < 2) return mo;

            for (int i = 0; i < points.Count; i++)
                mo.Vertices.Add(NewLineVertex(points[i]));

            for (int i = 0; i < points.Count - 1; i++)
                mo.Faces.Add(NewLineFace(i, i + 1));

            if (closed && points.Count >= 3)
                mo.Faces.Add(NewLineFace(points.Count - 1, 0));

            // 2 頂点の面と同じ並びの線分群を作る（面と線分群は連携させる。LineGroupOps）。
            var order = new List<int>(points.Count);
            for (int i = 0; i < points.Count; i++) order.Add(i);
            LineGroupOps.AddGroup(mo, order, closed && points.Count >= 3);

            return mo;
        }

        /// <summary>
        /// Loop 群を2頂点 Face 群の MeshObject にする(各ループを閉じる)。
        /// 点は (x, y, z) のまま配置。
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

                // 面と同じ並びの線分群（閉じたループ）を作る。
                var order = new List<int>(lp.Points.Count);
                for (int i = 0; i < lp.Points.Count; i++) order.Add(baseIdx + i);
                LineGroupOps.AddGroup(mo, order, lp.Points.Count >= 3);
            }
            return mo;
        }

        /// <summary>線分（2 点以上）を持つ線分群が 1 本でもあるか。</summary>
        private static bool HasLineGroups(MeshObject mesh)
        {
            if (mesh?.LineGroups == null) return false;
            foreach (var g in mesh.LineGroups)
                if (g?.Order != null && g.Order.Count >= 2) return true;
            return false;
        }

        // ================================================================
        // 内部: 頂点/ライン生成
        // ================================================================

        private static Vertex NewLineVertex(Vector3 p)
        {
            // UV/Normal の index 0 を有効にするため 3引数コンストラクタを使用。
            return new Vertex(p, Vector2.zero, Vector3.forward);
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
        // 内部: 閉ループ探索(ExtractLoops 用)
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
        /// <summary>
        /// Y 上向きの系で時計回りか。Σ(x1-x0)(y1+y0) が正なら時計回り。
        /// 反時計回りが外周、時計回りが穴（ExtractLoops の注記を参照）。
        /// </summary>
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
