// QuadPreservingDecimator.cs
// Quadトポロジ優先減数化 - コアアルゴリズム
// MeshObjectのFace（quad/tri直接保持）を操作する

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.UI.QuadDecimator;

namespace Poly_Ling.Tools.Panels.QuadDecimator
{
    public static class QuadPreservingDecimator
    {
        // ================================================================
        // エントリポイント（Multi-pass）
        // ================================================================

        /// <summary>
        /// MeshObjectを減数化して新しいMeshObjectを返す。
        /// 元のMeshObjectは変更しない。頂点IDは保持される。
        /// </summary>
        public static DecimatorResult Decimate(MeshObject source, DecimatorParams prms, out MeshObject result)
        {
            // ディープコピー（頂点IDそのまま）
            result = source.Clone();

            var dr = new DecimatorResult
            {
                OriginalFaceCount = source.Faces.Count,
            };

            int targetFaceCount = Mathf.Max(1, Mathf.RoundToInt(source.Faces.Count * prms.TargetRatio));
            int prevCollapsed = int.MaxValue;

            for (int pass = 0; pass < prms.MaxPasses; pass++)
            {
                if (result.Faces.Count <= targetFaceCount) break;

                int collapsed = RunOnePass(result, prms, pass);

                dr.PassLogs.Add($"Pass {pass}: collapsed {collapsed}, faces {result.Faces.Count}");
                dr.TotalCollapsed += collapsed;
                dr.PassCount = pass + 1;

                if (collapsed == 0) break;
                if (collapsed < prevCollapsed * 0.3f) break;
                prevCollapsed = collapsed;

                if (result.Faces.Count <= targetFaceCount) break;
            }

            dr.ResultFaceCount = result.Faces.Count;
            return dr;
        }

        // ================================================================
        // 1パス処理
        // ================================================================

        private static int RunOnePass(MeshObject mesh, DecimatorParams prms, int pass)
        {
            var verts = mesh.Vertices;
            var faces = mesh.Faces;

            // --- A. 隣接構築 ---
            var edges = BuildAdjacency(faces, out var edgeDict, out var faceEdges);

            // --- B. Cut判定 ---
            MarkCutEdges(edges, faces, verts, prms, edgeDict);

            // --- C. QuadGraph構築 ---
            var quadGraph = BuildQuadGraph(faces, verts, edges, faceEdges, prms);
            if (quadGraph.Nodes.Count < 2) return 0;

            // --- D. Patch抽出 ---
            var patches = BuildQuadPatches(quadGraph);

            // --- E. ストリップ抽出 + Target生成 ---
            var allOps = new List<ReplaceOp>();
            foreach (var patch in patches)
            {
                if (patch.QuadFaceIndices.Count < 2) continue;
                var ops = BuildTargetsForPatch(mesh, quadGraph, patch, edges, prms);
                allOps.AddRange(ops);
            }

            if (allOps.Count == 0) return 0;

            // --- F. スコア降順ソート + 衝突排除 ---
            allOps.Sort((a, b) => b.Score.CompareTo(a.Score));
            var accepted = FilterOps(allOps);

            if (accepted.Count == 0) return 0;

            // --- G. Face置換 ---
            ApplyOps(mesh, accepted);

            return accepted.Count;
        }

        // ================================================================
        // A. 隣接構築
        // ================================================================

        private static List<MeshEdge> BuildAdjacency(
            List<Face> faces,
            out Dictionary<UndirectedEdge, int> edgeDict,
            out List<List<int>> faceEdges)
        {
            var edges = new List<MeshEdge>();
            edgeDict = new Dictionary<UndirectedEdge, int>();
            faceEdges = new List<List<int>>(faces.Count);

            for (int fi = 0; fi < faces.Count; fi++)
            {
                var fe = new List<int>();
                var face = faces[fi];
                int vc = face.VertexCount;

                for (int j = 0; j < vc; j++)
                {
                    int a = face.VertexIndices[j];
                    int b = face.VertexIndices[(j + 1) % vc];
                    var key = new UndirectedEdge(a, b);

                    if (!edgeDict.TryGetValue(key, out int edgeId))
                    {
                        edgeId = edges.Count;
                        var me = new MeshEdge { Id = edgeId, A = key.A, B = key.B, FaceL = fi };
                        edges.Add(me);
                        edgeDict[key] = edgeId;
                    }
                    else
                    {
                        var me = edges[edgeId];
                        if (me.FaceR == -1)
                            me.FaceR = fi;
                        // 3面以上共有 = 非多様体 → 無視（boundaryとして扱う）
                    }
                    fe.Add(edgeId);
                }
                faceEdges.Add(fe);
            }

            // Boundary判定
            foreach (var e in edges)
            {
                e.IsBoundary = (e.FaceL == -1 || e.FaceR == -1);
            }

            return edges;
        }

        // ================================================================
        // B. Cut判定
        // ================================================================

        private static void MarkCutEdges(
            List<MeshEdge> edges,
            List<Face> faces,
            List<Vertex> verts,
            DecimatorParams prms,
            Dictionary<UndirectedEdge, int> edgeDict)
        {
            // --- 位置グループ構築（UVシーム検出用） ---
            float posQuant = ComputePosQuant(verts, prms.PosQuantFactor);
            var posGroups = BuildPositionGroups(verts, posQuant);
            var posEdgeUvPairs = BuildPosEdgeToUvPairs(faces, verts, posQuant, prms.UvSeamThreshold);

            foreach (var e in edges)
            {
                if (e.IsBoundary) continue; // 既にCut

                // Hard edge
                e.IsHard = IsHardEdge(e, faces, verts, prms.HardAngleDeg);

                // UV seam
                if (!e.IsHard)
                    e.IsSeam = IsUvSeam(e, verts, posGroups, posEdgeUvPairs, posQuant, prms.UvSeamThreshold);
            }
        }

        private static bool IsHardEdge(MeshEdge e, List<Face> faces, List<Vertex> verts, float hardAngleDeg)
        {
            if (e.FaceL < 0 || e.FaceR < 0) return true;
            var nL = ComputeFaceNormal(faces[e.FaceL], verts);
            var nR = ComputeFaceNormal(faces[e.FaceR], verts);
            return Vector3.Angle(nL, nR) >= hardAngleDeg;
        }

        // --- UV Seam ---

        /// <summary>
        /// 位置同一頂点グループ
        /// </summary>
        private static Dictionary<long, List<int>> BuildPositionGroups(List<Vertex> verts, float quant)
        {
            var groups = new Dictionary<long, List<int>>();
            for (int i = 0; i < verts.Count; i++)
            {
                long key = QuantizePos(verts[i].Position, quant);
                if (!groups.TryGetValue(key, out var list))
                {
                    list = new List<int>();
                    groups[key] = list;
                }
                list.Add(i);
            }
            return groups;
        }

        /// <summary>
        /// 位置エッジ → UVペア集合
        /// </summary>
        private static Dictionary<long, HashSet<long>> BuildPosEdgeToUvPairs(
            List<Face> faces, List<Vertex> verts, float posQuant, float uvThreshold)
        {
            var map = new Dictionary<long, HashSet<long>>();

            foreach (var face in faces)
            {
                int vc = face.VertexCount;
                for (int j = 0; j < vc; j++)
                {
                    int va = face.VertexIndices[j];
                    int vb = face.VertexIndices[(j + 1) % vc];
                    int uvIdxA = (j < face.UVIndices.Count) ? face.UVIndices[j] : 0;
                    int uvIdxB = ((j + 1) % vc < face.UVIndices.Count) ? face.UVIndices[(j + 1) % vc] : 0;

                    Vector2 uvA = GetVertexUV(verts[va], uvIdxA);
                    Vector2 uvB = GetVertexUV(verts[vb], uvIdxB);

                    long posEdgeKey = MakePosEdgeKey(verts[va].Position, verts[vb].Position, posQuant);
                    long uvPairKey = MakeUVPairKey(uvA, uvB, uvThreshold);

                    if (!map.TryGetValue(posEdgeKey, out var set))
                    {
                        set = new HashSet<long>();
                        map[posEdgeKey] = set;
                    }
                    set.Add(uvPairKey);
                }
            }

            return map;
        }

        private static bool IsUvSeam(
            MeshEdge e, List<Vertex> verts,
            Dictionary<long, List<int>> posGroups,
            Dictionary<long, HashSet<long>> posEdgeUvPairs,
            float posQuant, float uvThreshold)
        {
            // 端点の位置グループでUVが複数種類あるかチェック
            if (HasMultipleUVInGroup(verts, posGroups, verts[e.A].Position, posQuant, uvThreshold))
                return true;
            if (HasMultipleUVInGroup(verts, posGroups, verts[e.B].Position, posQuant, uvThreshold))
                return true;

            // 位置エッジに複数UVペアがあるかチェック
            long peKey = MakePosEdgeKey(verts[e.A].Position, verts[e.B].Position, posQuant);
            if (posEdgeUvPairs.TryGetValue(peKey, out var uvPairs) && uvPairs.Count > 1)
                return true;

            return false;
        }

        private static bool HasMultipleUVInGroup(
            List<Vertex> verts,
            Dictionary<long, List<int>> posGroups,
            Vector3 pos, float posQuant, float uvThreshold)
        {
            long key = QuantizePos(pos, posQuant);
            if (!posGroups.TryGetValue(key, out var group)) return false;
            if (group.Count <= 1) return false;

            // グループ内の全UVを比較
            Vector2? firstUV = null;
            foreach (int vi in group)
            {
                Vector2 uv = (verts[vi].UVs.Count > 0) ? verts[vi].UVs[0] : Vector2.zero;
                if (!firstUV.HasValue)
                {
                    firstUV = uv;
                }
                else
                {
                    if (Vector2.Distance(uv, firstUV.Value) >= uvThreshold)
                        return true;
                }
            }
            return false;
        }

        // ================================================================
        // C. QuadGraph構築
        // ================================================================

        private static QuadGraph BuildQuadGraph(
            List<Face> faces, List<Vertex> verts,
            List<MeshEdge> edges, List<List<int>> faceEdges,
            DecimatorParams prms)
        {
            var graph = new QuadGraph();

            // Quad面のみノード化
            for (int fi = 0; fi < faces.Count; fi++)
            {
                if (!faces[fi].IsQuad) continue;
                var node = new QuadNode
                {
                    FaceIndex = fi,
                    Center = ComputeFaceCenter(faces[fi], verts),
                    Normal = ComputeFaceNormal(faces[fi], verts),
                };
                graph.Nodes[fi] = node;
            }

            // Cutでないエッジを通じて隣接リンクを張る
            foreach (var e in edges)
            {
                if (e.IsCut) continue;
                if (e.FaceL < 0 || e.FaceR < 0) continue;

                bool lIsQuad = graph.Nodes.ContainsKey(e.FaceL);
                bool rIsQuad = graph.Nodes.ContainsKey(e.FaceR);
                if (!lIsQuad || !rIsQuad) continue;

                // 隣接面の法線角度チェック
                float angle = Vector3.Angle(graph.Nodes[e.FaceL].Normal, graph.Nodes[e.FaceR].Normal);
                if (angle > prms.NormalAngleDeg) continue;

                graph.Nodes[e.FaceL].Links.Add(new QuadLink
                {
                    ToQuadIndex = e.FaceR,
                    SharedEdgeId = e.Id,
                });
                graph.Nodes[e.FaceR].Links.Add(new QuadLink
                {
                    ToQuadIndex = e.FaceL,
                    SharedEdgeId = e.Id,
                });
            }

            return graph;
        }

        // ================================================================
        // D. Patch抽出（BFS連結成分）
        // ================================================================

        private static List<QuadPatch> BuildQuadPatches(QuadGraph graph)
        {
            var patches = new List<QuadPatch>();
            var visited = new HashSet<int>();

            foreach (var kv in graph.Nodes)
            {
                if (visited.Contains(kv.Key)) continue;

                var patch = new QuadPatch();
                var queue = new Queue<int>();
                queue.Enqueue(kv.Key);
                visited.Add(kv.Key);
                Vector3 normalSum = Vector3.zero;

                while (queue.Count > 0)
                {
                    int fi = queue.Dequeue();
                    patch.QuadFaceIndices.Add(fi);
                    normalSum += graph.Nodes[fi].Normal;

                    foreach (var link in graph.Nodes[fi].Links)
                    {
                        if (!visited.Contains(link.ToQuadIndex))
                        {
                            visited.Add(link.ToQuadIndex);
                            queue.Enqueue(link.ToQuadIndex);
                        }
                    }
                }

                patch.AvgNormal = (normalSum.sqrMagnitude > 1e-8f) ? normalSum.normalized : Vector3.up;
                patches.Add(patch);
            }

            return patches;
        }

        // ================================================================
        // E. PatchごとのTarget生成
        // ================================================================

        private static List<ReplaceOp> BuildTargetsForPatch(
            MeshObject mesh, QuadGraph graph, QuadPatch patch,
            List<MeshEdge> edges, DecimatorParams prms)
        {
            // --- F1. 主方向推定 ---
            BuildPlaneBasis(patch.AvgNormal, out Vector3 basisU, out Vector3 basisV);

            // Quad中心を2Dへ投影
            var centers2D = new Dictionary<int, Vector2>();
            foreach (int fi in patch.QuadFaceIndices)
            {
                var c3d = graph.Nodes[fi].Center;
                centers2D[fi] = new Vector2(Vector3.Dot(c3d, basisU), Vector3.Dot(c3d, basisV));
            }

            // PCA主方向
            ComputePrincipalAxis(patch.QuadFaceIndices, centers2D, out Vector2 e1, out Vector2 e2);

            // --- F2. U/Vラベリング ---
            LabelDirections(graph, patch, basisU, basisV, e1, e2);

            // --- G. ストリップ抽出 ---
            var stripsU = ExtractStrips(graph, patch, DirClass.U);
            var stripsV = ExtractStrips(graph, patch, DirClass.V);

            // --- H. 間引き方向選択 ---
            DirClass dirRemove = (stripsU.Count >= stripsV.Count) ? DirClass.U : DirClass.V;
            var strips = (dirRemove == DirClass.U) ? stripsU : stripsV;

            if (strips.Count < 2) return new List<ReplaceOp>();

            // 直交軸でソート
            Vector2 perpAxis = (dirRemove == DirClass.U) ? e2 : e1;
            foreach (var strip in strips)
            {
                float sum = 0;
                foreach (int fi in strip.QuadFaceIndices)
                    sum += Vector2.Dot(centers2D[fi], perpAxis);
                strip.SortKey = sum / strip.QuadFaceIndices.Count;
            }
            strips.Sort((a, b) => a.SortKey.CompareTo(b.SortKey));

            // 1本おきに選択
            var selectedStrips = new List<QuadStrip>();
            for (int i = 1; i < strips.Count; i += 2)
                selectedStrips.Add(strips[i]);

            // --- I. ReplaceOp生成 ---
            var ops = new List<ReplaceOp>();
            var perpDir = (dirRemove == DirClass.U) ? DirClass.V : DirClass.U;

            foreach (var strip in selectedStrips)
            {
                foreach (int fi in strip.QuadFaceIndices)
                {
                    if (!graph.Nodes.ContainsKey(fi)) continue;
                    var node = graph.Nodes[fi];

                    // perpDir方向の隣接を探す（collapseするペア）
                    foreach (var link in node.Links)
                    {
                        if (link.Dir != perpDir) continue;
                        // 選択ストリップ内のQuad同士は潰さない（perpDirの隣接 = 別ストリップのQuad）
                        // 隣接先がselectedStripsに含まれていないことを確認
                        bool neighborInSelected = false;
                        foreach (var ss in selectedStrips)
                        {
                            if (ss.QuadFaceIndices.Contains(link.ToQuadIndex))
                            {
                                neighborInSelected = true;
                                break;
                            }
                        }
                        if (neighborInSelected) continue;

                        var op = TryBuildReplaceOp(mesh, fi, link.ToQuadIndex, link.SharedEdgeId, edges, prms);
                        if (op != null)
                            ops.Add(op);
                    }
                }
            }

            return ops;
        }

        // ================================================================
        // F1. 平面基底構築
        // ================================================================

        private static void BuildPlaneBasis(Vector3 normal, out Vector3 u, out Vector3 w)
        {
            Vector3 n = normal.normalized;
            Vector3 up = (Mathf.Abs(Vector3.Dot(n, Vector3.up)) > 0.9f) ? Vector3.forward : Vector3.up;
            u = Vector3.Cross(n, up).normalized;
            w = Vector3.Cross(n, u).normalized;
        }

        // ================================================================
        // F1. PCA主方向（2D）
        // ================================================================

        private static void ComputePrincipalAxis(
            List<int> quadIndices,
            Dictionary<int, Vector2> centers2D,
            out Vector2 e1, out Vector2 e2)
        {
            // 平均
            Vector2 mean = Vector2.zero;
            int count = 0;
            foreach (int fi in quadIndices)
            {
                mean += centers2D[fi];
                count++;
            }
            if (count > 0) mean /= count;

            // 共分散
            float xx = 0, xy = 0, yy = 0;
            foreach (int fi in quadIndices)
            {
                Vector2 d = centers2D[fi] - mean;
                xx += d.x * d.x;
                xy += d.x * d.y;
                yy += d.y * d.y;
            }

            // 最大固有ベクトル
            float theta = 0.5f * Mathf.Atan2(2f * xy, xx - yy);
            e1 = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta));
            e2 = new Vector2(-e1.y, e1.x); // 直交
        }

        // ================================================================
        // F2. U/Vラベリング
        // ================================================================

        private static void LabelDirections(
            QuadGraph graph, QuadPatch patch,
            Vector3 basisU, Vector3 basisV,
            Vector2 e1, Vector2 e2)
        {
            foreach (int fi in patch.QuadFaceIndices)
            {
                if (!graph.Nodes.ContainsKey(fi)) continue;
                var node = graph.Nodes[fi];

                foreach (var link in node.Links)
                {
                    if (!graph.Nodes.ContainsKey(link.ToQuadIndex)) continue;
                    var toNode = graph.Nodes[link.ToQuadIndex];

                    Vector3 d3 = (toNode.Center - node.Center).normalized;
                    Vector2 d2 = new Vector2(Vector3.Dot(d3, basisU), Vector3.Dot(d3, basisV));
                    if (d2.sqrMagnitude > 1e-8f) d2.Normalize();

                    float a = Mathf.Abs(Vector2.Dot(d2, e1));
                    float b = Mathf.Abs(Vector2.Dot(d2, e2));
                    link.Dir = (a >= b) ? DirClass.U : DirClass.V;
                }
            }
        }

        // ================================================================
        // G. ストリップ抽出
        // ================================================================

        private static List<QuadStrip> ExtractStrips(
            QuadGraph graph, QuadPatch patch, DirClass dir)
        {
            var patchSet = new HashSet<int>(patch.QuadFaceIndices);
            var visited = new HashSet<int>();
            var strips = new List<QuadStrip>();

            // 端点（dir方向の次数<=1）から開始
            foreach (int fi in patch.QuadFaceIndices)
            {
                if (visited.Contains(fi)) continue;
                int dirDeg = CountDirDegree(graph, fi, dir, patchSet);
                if (dirDeg > 1) continue; // 分岐点はスキップ（端点から辿る）

                var strip = FollowStrip(graph, fi, dir, patchSet, visited);
                if (strip.QuadFaceIndices.Count > 0)
                    strips.Add(strip);
            }

            // 残りのループ
            foreach (int fi in patch.QuadFaceIndices)
            {
                if (visited.Contains(fi)) continue;
                var strip = FollowStrip(graph, fi, dir, patchSet, visited);
                if (strip.QuadFaceIndices.Count > 0)
                    strips.Add(strip);
            }

            return strips;
        }

        private static int CountDirDegree(QuadGraph graph, int fi, DirClass dir, HashSet<int> patchSet)
        {
            int deg = 0;
            if (!graph.Nodes.ContainsKey(fi)) return 0;
            foreach (var link in graph.Nodes[fi].Links)
            {
                if (link.Dir == dir && patchSet.Contains(link.ToQuadIndex))
                    deg++;
            }
            return deg;
        }

        private static QuadStrip FollowStrip(
            QuadGraph graph, int startFi, DirClass dir,
            HashSet<int> patchSet, HashSet<int> visited)
        {
            var strip = new QuadStrip();
            int current = startFi;

            while (current >= 0 && !visited.Contains(current))
            {
                visited.Add(current);
                strip.QuadFaceIndices.Add(current);

                int next = -1;
                if (graph.Nodes.ContainsKey(current))
                {
                    foreach (var link in graph.Nodes[current].Links)
                    {
                        if (link.Dir == dir && patchSet.Contains(link.ToQuadIndex) && !visited.Contains(link.ToQuadIndex))
                        {
                            // 分岐しない（次数>=2）→ 最初の未訪問を取る
                            next = link.ToQuadIndex;
                            break;
                        }
                    }
                }
                current = next;
            }

            return strip;
        }

        // ================================================================
        // I. ReplaceOp生成
        // ================================================================

        private static ReplaceOp TryBuildReplaceOp(
            MeshObject mesh, int faceAIdx, int faceBIdx, int sharedEdgeId,
            List<MeshEdge> edges, DecimatorParams prms)
        {
            var faceA = mesh.Faces[faceAIdx];
            var faceB = mesh.Faces[faceBIdx];

            if (!faceA.IsQuad || !faceB.IsQuad) return null;

            var sharedEdge = edges[sharedEdgeId];

            // 共有エッジの2頂点
            int s0 = sharedEdge.A;
            int s1 = sharedEdge.B;

            // 外周頂点を取得（共有エッジ端点以外の頂点）
            if (!GetOuterVertices(faceA, faceB, s0, s1,
                    out int[] outerVerts, out int[] outerUVIndices, out int[] outerNormalIndices))
                return null;

            // UV裏返りチェック
            if (UvFlipsOrDegenerate(mesh, faceA, faceB, outerVerts, outerUVIndices))
                return null;

            // Winding一致チェック
            EnsureConsistentWinding(mesh, faceA, faceB, ref outerVerts, ref outerUVIndices, ref outerNormalIndices);

            // スコア計算（平面性 + quad形状品質）
            float score = ComputeScore(mesh, outerVerts, faceA, faceB);

            return new ReplaceOp
            {
                QuadAFaceIndex = faceAIdx,
                QuadBFaceIndex = faceBIdx,
                SharedEdgeId = sharedEdgeId,
                NewVertexIndices = outerVerts,
                NewUVIndices = outerUVIndices,
                NewNormalIndices = outerNormalIndices,
                MaterialIndex = faceA.MaterialIndex,
                Score = score,
            };
        }

        /// <summary>
        /// 2つのQuadから共有エッジ端点を除いた外周4頂点を周回順で返す。
        /// UV/法線インデックスも対応する元面の値を引き継ぐ。
        /// </summary>
        private static bool GetOuterVertices(
            Face faceA, Face faceB, int s0, int s1,
            out int[] outerVerts, out int[] outerUVIndices, out int[] outerNormalIndices)
        {
            outerVerts = null;
            outerUVIndices = null;
            outerNormalIndices = null;

            // 各面の辺を列挙（外周 = 両面合わせて出現回数1の辺）
            var edgeCount = new Dictionary<UndirectedEdge, int>();
            CountFaceEdges(faceA, edgeCount);
            CountFaceEdges(faceB, edgeCount);

            // 外周エッジ（出現回数1）
            var boundaryEdges = new List<UndirectedEdge>();
            foreach (var kv in edgeCount)
            {
                if (kv.Value == 1)
                    boundaryEdges.Add(kv.Key);
            }

            // 外周ループ復元
            var adj = new Dictionary<int, List<int>>();
            foreach (var be in boundaryEdges)
            {
                if (!adj.ContainsKey(be.A)) adj[be.A] = new List<int>();
                if (!adj.ContainsKey(be.B)) adj[be.B] = new List<int>();
                adj[be.A].Add(be.B);
                adj[be.B].Add(be.A);
            }

            // ループをトレース
            var loop = TraceLoop(adj);
            if (loop == null || loop.Count != 6) return false;

            // 共有エッジ端点を除去
            var reduced = new List<int>();
            foreach (int v in loop)
            {
                if (v != s0 && v != s1)
                    reduced.Add(v);
            }
            if (reduced.Count != 4) return false;

            // UV/法線インデックスを元面から引き継ぐ
            outerVerts = reduced.ToArray();
            outerUVIndices = new int[4];
            outerNormalIndices = new int[4];

            for (int i = 0; i < 4; i++)
            {
                int vi = outerVerts[i];
                // faceAに含まれるか
                int idxInA = faceA.VertexIndices.IndexOf(vi);
                if (idxInA >= 0)
                {
                    outerUVIndices[i] = (idxInA < faceA.UVIndices.Count) ? faceA.UVIndices[idxInA] : 0;
                    outerNormalIndices[i] = (idxInA < faceA.NormalIndices.Count) ? faceA.NormalIndices[idxInA] : 0;
                }
                else
                {
                    int idxInB = faceB.VertexIndices.IndexOf(vi);
                    if (idxInB < 0) return false; // 頂点が見つからない
                    outerUVIndices[i] = (idxInB < faceB.UVIndices.Count) ? faceB.UVIndices[idxInB] : 0;
                    outerNormalIndices[i] = (idxInB < faceB.NormalIndices.Count) ? faceB.NormalIndices[idxInB] : 0;
                }
            }

            return true;
        }

        private static void CountFaceEdges(Face face, Dictionary<UndirectedEdge, int> edgeCount)
        {
            int vc = face.VertexCount;
            for (int j = 0; j < vc; j++)
            {
                var key = new UndirectedEdge(face.VertexIndices[j], face.VertexIndices[(j + 1) % vc]);
                if (!edgeCount.ContainsKey(key))
                    edgeCount[key] = 0;
                edgeCount[key]++;
            }
        }

        /// <summary>
        /// 隣接辞書からループをトレース
        /// </summary>
        private static List<int> TraceLoop(Dictionary<int, List<int>> adj)
        {
            if (adj.Count == 0) return null;

            int start = adj.Keys.First();
            var loop = new List<int>();
            var visited = new HashSet<int>();
            int current = start;
            int prev = -1;

            while (true)
            {
                if (visited.Contains(current) && current == start && loop.Count > 2)
                    break;
                if (visited.Contains(current))
                    return null; // 不正

                visited.Add(current);
                loop.Add(current);

                if (!adj.ContainsKey(current)) return null;
                var neighbors = adj[current];

                int next = -1;
                foreach (int n in neighbors)
                {
                    if (n != prev)
                    {
                        next = n;
                        break;
                    }
                }

                if (next == -1) return null;
                if (next == start && loop.Count > 2)
                    break;

                prev = current;
                current = next;
            }

            return loop;
        }

        // ================================================================
        // I3. UV裏返り/潰れチェック
        // ================================================================

        private static bool UvFlipsOrDegenerate(
            MeshObject mesh, Face faceA, Face faceB,
            int[] outerVerts, int[] outerUVIndices)
        {
            // 元面からUV符号（面積の向き）を取得
            float refSign = GetUvSignedArea(mesh, faceA);
            if (Mathf.Abs(refSign) < 1e-10f)
                refSign = GetUvSignedArea(mesh, faceB);
            if (Mathf.Abs(refSign) < 1e-10f)
                return false; // 面積ゼロ → チェック不能、許容

            float refSignValue = Mathf.Sign(refSign);

            // 新Quad → tri2枚に分解してチェック
            // tri1: [0,1,2], tri2: [0,2,3]
            for (int t = 0; t < 2; t++)
            {
                int i0 = 0, i1 = (t == 0) ? 1 : 2, i2 = (t == 0) ? 2 : 3;
                Vector2 uv0 = GetVertexUV(mesh.Vertices[outerVerts[i0]], outerUVIndices[i0]);
                Vector2 uv1 = GetVertexUV(mesh.Vertices[outerVerts[i1]], outerUVIndices[i1]);
                Vector2 uv2 = GetVertexUV(mesh.Vertices[outerVerts[i2]], outerUVIndices[i2]);

                float area = SignedArea2(uv0, uv1, uv2);
                if (Mathf.Abs(area) < 1e-8f) return true;  // 潰れ
                if (Mathf.Sign(area) != refSignValue) return true;  // 裏返り
            }

            return false;
        }

        private static float GetUvSignedArea(MeshObject mesh, Face face)
        {
            if (face.VertexCount < 3) return 0;
            Vector2 uv0 = GetVertexUV(mesh.Vertices[face.VertexIndices[0]],
                (0 < face.UVIndices.Count) ? face.UVIndices[0] : 0);
            Vector2 uv1 = GetVertexUV(mesh.Vertices[face.VertexIndices[1]],
                (1 < face.UVIndices.Count) ? face.UVIndices[1] : 0);
            Vector2 uv2 = GetVertexUV(mesh.Vertices[face.VertexIndices[2]],
                (2 < face.UVIndices.Count) ? face.UVIndices[2] : 0);
            return SignedArea2(uv0, uv1, uv2);
        }

        private static float SignedArea2(Vector2 a, Vector2 b, Vector2 c)
        {
            return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        }

        // ================================================================
        // I2. Winding一致
        // ================================================================

        private static void EnsureConsistentWinding(
            MeshObject mesh, Face faceA, Face faceB,
            ref int[] outerVerts, ref int[] outerUVIndices, ref int[] outerNormalIndices)
        {
            // 新Quadの法線
            Vector3 p0 = mesh.Vertices[outerVerts[0]].Position;
            Vector3 p1 = mesh.Vertices[outerVerts[1]].Position;
            Vector3 p2 = mesh.Vertices[outerVerts[2]].Position;
            Vector3 nNew = NormalHelper.CalculateFaceNormal(p0, p1, p2);

            // 元面の法線の平均
            Vector3 nA = ComputeFaceNormal(faceA, mesh.Vertices);
            Vector3 nB = ComputeFaceNormal(faceB, mesh.Vertices);
            Vector3 nOld = ((nA + nB) * 0.5f).normalized;
            if (nOld.sqrMagnitude < 1e-6f) nOld = Vector3.up;

            if (Vector3.Dot(nNew, nOld) < 0)
            {
                // 反転: [0,1,2,3] → [0,3,2,1]
                Array.Reverse(outerVerts, 1, 3);
                Array.Reverse(outerUVIndices, 1, 3);
                Array.Reverse(outerNormalIndices, 1, 3);
            }
        }

        // ================================================================
        // スコア計算
        // ================================================================

        private static float ComputeScore(MeshObject mesh, int[] outerVerts, Face faceA, Face faceB)
        {
            // 平面性スコア（高い方が良い）
            Vector3 pa = mesh.Vertices[outerVerts[0]].Position;
            Vector3 pb = mesh.Vertices[outerVerts[1]].Position;
            Vector3 pc = mesh.Vertices[outerVerts[2]].Position;
            Vector3 pd = mesh.Vertices[outerVerts[3]].Position;

            Vector3 n = Vector3.Cross(pb - pa, pc - pa);
            if (n.sqrMagnitude < 1e-12f) return 0;
            n.Normalize();
            float dist = Mathf.Abs(Vector3.Dot(pd - pa, n));
            float edgeLen = ((pb - pa).magnitude + (pc - pb).magnitude +
                             (pd - pc).magnitude + (pa - pd).magnitude) * 0.25f;
            float planarScore = (edgeLen > 1e-6f) ? (1f - dist / edgeLen) : 0;

            // quad形状品質（正方形に近いほど高い）
            float d1 = (pc - pa).magnitude;
            float d2 = (pd - pb).magnitude;
            float aspect = (Mathf.Max(d1, d2) > 1e-6f)
                ? Mathf.Min(d1, d2) / Mathf.Max(d1, d2)
                : 0;

            return planarScore * 0.7f + aspect * 0.3f;
        }

        // ================================================================
        // J. 衝突排除
        // ================================================================

        private static List<ReplaceOp> FilterOps(List<ReplaceOp> ops)
        {
            var usedFaces = new HashSet<int>();
            var accepted = new List<ReplaceOp>();

            foreach (var op in ops)
            {
                if (usedFaces.Contains(op.QuadAFaceIndex)) continue;
                if (usedFaces.Contains(op.QuadBFaceIndex)) continue;

                accepted.Add(op);
                usedFaces.Add(op.QuadAFaceIndex);
                usedFaces.Add(op.QuadBFaceIndex);
            }

            return accepted;
        }

        // ================================================================
        // K. Face置換
        // ================================================================

        private static void ApplyOps(MeshObject mesh, List<ReplaceOp> accepted)
        {
            // 削除対象面インデックスを収集
            var removeFaces = new HashSet<int>();
            foreach (var op in accepted)
            {
                removeFaces.Add(op.QuadAFaceIndex);
                removeFaces.Add(op.QuadBFaceIndex);
            }

            // 新しい面リスト構築（削除対象を飛ばして、末尾に新規追加）
            var newFaces = new List<Face>();
            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                if (!removeFaces.Contains(i))
                    newFaces.Add(mesh.Faces[i]);
            }

            foreach (var op in accepted)
            {
                var newFace = new Face
                {
                    Id = mesh.GenerateFaceId(),
                    VertexIndices = new List<int>(op.NewVertexIndices),
                    UVIndices = new List<int>(op.NewUVIndices),
                    NormalIndices = new List<int>(op.NewNormalIndices),
                    MaterialIndex = op.MaterialIndex,
                    Flags = FaceFlags.None,
                };
                newFaces.Add(newFace);
            }

            mesh.Faces = newFaces;
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        private static Vector3 ComputeFaceNormal(Face face, List<Vertex> verts)
        {
            if (face.VertexCount < 3) return Vector3.up;
            return NormalHelper.CalculateFaceNormal(
                verts[face.VertexIndices[0]].Position,
                verts[face.VertexIndices[1]].Position,
                verts[face.VertexIndices[2]].Position);
        }

        private static Vector3 ComputeFaceCenter(Face face, List<Vertex> verts)
        {
            Vector3 sum = Vector3.zero;
            foreach (int vi in face.VertexIndices)
                sum += verts[vi].Position;
            return sum / face.VertexCount;
        }

        private static Vector2 GetVertexUV(Vertex v, int uvSubIndex)
        {
            if (v.UVs.Count == 0) return Vector2.zero;
            int idx = Mathf.Clamp(uvSubIndex, 0, v.UVs.Count - 1);
            return v.UVs[idx];
        }

        private static float ComputePosQuant(List<Vertex> verts, float factor)
        {
            if (verts.Count == 0) return 1e-5f;
            Vector3 min = verts[0].Position, max = verts[0].Position;
            foreach (var v in verts)
            {
                min = Vector3.Min(min, v.Position);
                max = Vector3.Max(max, v.Position);
            }
            float size = (max - min).magnitude;
            return Mathf.Max(size * factor, 1e-7f);
        }

        private static long QuantizePos(Vector3 p, float quant)
        {
            long x = (long)Mathf.RoundToInt(p.x / quant);
            long y = (long)Mathf.RoundToInt(p.y / quant);
            long z = (long)Mathf.RoundToInt(p.z / quant);
            return x * 73856093L ^ y * 19349663L ^ z * 83492791L;
        }

        private static long MakePosEdgeKey(Vector3 pa, Vector3 pb, float quant)
        {
            long ka = QuantizePos(pa, quant);
            long kb = QuantizePos(pb, quant);
            if (ka > kb) (ka, kb) = (kb, ka);
            return ka * 1000000007L ^ kb;
        }

        private static long MakeUVPairKey(Vector2 uvA, Vector2 uvB, float uvQuant)
        {
            long a = QuantizeUV(uvA, uvQuant);
            long b = QuantizeUV(uvB, uvQuant);
            if (a > b) (a, b) = (b, a);
            return a * 1000000007L ^ b;
        }

        private static long QuantizeUV(Vector2 uv, float quant)
        {
            if (quant < 1e-9f) quant = 1e-6f;
            long u = (long)Mathf.RoundToInt(uv.x / quant);
            long v = (long)Mathf.RoundToInt(uv.y / quant);
            return u * 73856093L ^ v * 19349663L;
        }
    }
}
