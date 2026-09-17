// BooleanDiagnostics.cs
// ブーリアン演算で面が欠ける箇所を、段階ごとの数で突き止めるための計測。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【何を測るか】
//   BooleanOps.Perform と同じ手順を踏み、途中の段階ごとに数を取る。
//   モデルは変えない。演算の結果も捨てる。
//
//   a. 入力 A / B の穴      … 入力が閉じていないと CSG の内外が決まらない
//   b. BSP の手順ごとの多角形数（Node.Subtract / Union / Intersect と同じ順に撃つ）
//   c. 多角形の平面の質     … Polygon は平面を先頭 3 頂点から作る（Polygon.cs:43）。
//                              面積の小さい破片は法線が 0 に潰れ（Vector3.normalized の下限）、
//                              Plane.Valid() が偽になる。先頭 3 頂点が一直線に近いと、
//                              平面が多角形全体と合わない。どちらも数える。
//   d. 結果の穴             … 頂点をほぼ完全一致（MergeExactThreshold）でまとめた場合と、
//                              指定の mergeThreshold でまとめた場合の 2 通り。
//                              どちらも T 字を解消してから数える。
//                              差には頂点まとめと後続の T 字解消の両方が影響する。
//   e. 結果の頂点数         … Unity Mesh の 16 ビット索引の上限と比べるため
//   f. 辺の接続             … 頂点まとめ直後と T 字解消後を分け、境界・3面以上の共有・
//                              同方向の共有を数える。境界の連結成分は閉じた穴とは限らない。
//
// 【BSP の手順を自前で並べる理由】
//   Node の各手順の途中の数は外から取れない。Node.cs（取り込んだ外部コード）へ
//   計測を足さず、同じ順で公開メソッドを呼んで数える。
//   手順は Node.cs:206-264 と同じにすること。変えると測っているものが変わる。
//
// 【数え方の注意】
//   Node.AllPolygons は各節の polygons リストへ子の多角形を足し込む（Node.cs:183,196-197）。
//   途中で数えるときは AllPolygons を呼ばず、木をたどって数える。

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Node     = Poly_Ling.CSG.Node;
using Polygon  = Poly_Ling.CSG.Polygon;
using CsgModel = Poly_Ling.CSG.Model;
using CsgCore  = Poly_Ling.CSG.CSG;

namespace Poly_Ling.Ops
{
    /// <summary>ブーリアン演算の計測結果。</summary>
    public sealed class BooleanDiagnosticReport
    {
        public bool   Success;
        public string Message = "";

        public int InputHolesA;
        public int InputHolesB;

        public int PolygonsA;
        public int PolygonsB;
        public int InvalidPlanesA;
        public int InvalidPlanesB;

        /// <summary>BSP の手順名と、その直後に木に残っている多角形の数。</summary>
        public readonly List<string> StepNames  = new List<string>();
        public readonly List<int>    StepCounts = new List<int>();

        public int ResultPolygons;
        public int ResultInvalidPlanes;
        public int ResultSkewedPlanes;
        public int ResultVertices;

        public int HolesExact;
        public int HolesMerged;

        /// <summary>頂点まとめ直後／T字解消後の接続。各配列は同じ添字で対応する。</summary>
        public readonly List<string> TopologyStages = new List<string>();
        public readonly List<int> BoundaryEdgeCounts = new List<int>();
        public readonly List<int> NonManifoldEdgeCounts = new List<int>();
        public readonly List<int> InconsistentWindingEdgeCounts = new List<int>();

        /// <summary>段階名と所要時間（ミリ秒）。</summary>
        public readonly List<string> TimingNames = new List<string>();
        public readonly List<int>    TimingMs    = new List<int>();

        /// <summary>穴ごとの面積（ループの Newell 法）と、無効な平面の多角形の頂点に重なる穴の頂点数。</summary>
        public readonly List<float> HoleExactAreas        = new List<float>();
        public readonly List<int>   HoleExactInvalidTouch = new List<int>();

        /// <summary>写した BSP の結果と、元の Node の結果と一致したか。</summary>
        public int  ReplicaPolygons;
        public int  ReplicaInvalidPlanes;
        public bool ReplicaMatches;

        /// <summary>写した BSP で数えた分岐。</summary>
        public int LeafDiscardValid;
        public int LeafDiscardInvalid;
        public int CoplanarBackInvalid;
        public int InvalidNodeReturns;
        public int InvalidNodePassed;

        /// <summary>無効な平面を作り直して演算した結果（fixInvalidPlanes のときだけ）。</summary>
        public int FixedPolygons;
        public int FixedInvalidPlanes;
        public int FixedPlanesRebuilt;
        public int FixedHolesExact  = -1;
        public int FixedHolesMerged = -1;

        /// <summary>穴ごとの頂点数と重心（演算空間＝A のローカル）。</summary>
        public readonly List<int>   HoleExactSizes       = new List<int>();
        public readonly List<float> HoleExactCentroids   = new List<float>();
        public readonly List<int>   HoleMergedSizes      = new List<int>();
        public readonly List<float> HoleMergedCentroids  = new List<float>();
    }

    /// <summary>ブーリアン演算で面が欠ける箇所を段階ごとに数える。</summary>
    public static class BooleanDiagnostics
    {
        /// <summary>「ほぼ完全一致」とみなす頂点まとめの距離。float の丸めで割れた同一点だけを拾う。</summary>
        public const float MergeExactThreshold = 1e-7f;

        /// <summary>先頭 3 頂点の法線と多角形全体の法線がこれ以上ずれたら数える（度）。</summary>
        public const float SkewAngleDeg = 1f;

        /// <summary>返す穴の数の上限。</summary>
        private const int HoleListLimit = 40;

        public static BooleanDiagnosticReport Run(
            BooleanOpKind op,
            MeshObject a, Matrix4x4 aToWorld,
            MeshObject b, Matrix4x4 bToWorld,
            Matrix4x4 worldToResult,
            float epsilon, float mergeThreshold, float tJunctionTolerance,
            bool fixInvalidPlanes = false)
        {
            var rep = new BooleanDiagnosticReport();

            if (a == null || b == null) { rep.Message = "メッシュが指定されていない"; return rep; }
            if (a.FaceCount == 0 || b.FaceCount == 0) { rep.Message = "面を持たないメッシュは対象にできない"; return rep; }

            Matrix4x4 ma = worldToResult * aToWorld;
            Matrix4x4 mb = worldToResult * bToWorld;

            // 段階ごとの所要時間。MCP の応答上限を超えても Editor.log に残るよう、
            // 区切るたびにログへも出す。
            var sw = System.Diagnostics.Stopwatch.StartNew();
            void Lap(string stage)
            {
                long ms = sw.ElapsedMilliseconds;
                rep.TimingNames.Add(stage);
                rep.TimingMs.Add((int)ms);
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}",
                    $"[BooleanDiagnostics] {stage}: {ms} ms");
                sw.Restart();
            }

            Mesh meshA = null, meshB = null, meshResult = null;
            float savedEpsilon = CsgCore.epsilon;

            try
            {
                meshA = a.ToUnityMesh(ma, a.SubMeshCount);
                meshB = b.ToUnityMesh(mb, b.SubMeshCount);
                Lap("ToUnityMesh");

                // ── a. 入力の穴 ──
                rep.InputHolesA = CountHoles(meshA, MergeExactThreshold, tJunctionTolerance, null, null, out _, out _);
                Lap("入力 A の穴");
                rep.InputHolesB = CountHoles(meshB, MergeExactThreshold, tJunctionTolerance, null, null, out _, out _);
                Lap("入力 B の穴");

                CsgCore.epsilon = epsilon > 0f ? epsilon : BooleanOps.DefaultEpsilon;

                var polysA = new CsgModel(meshA, Matrix4x4.identity).ToPolygons();
                var polysB = new CsgModel(meshB, Matrix4x4.identity).ToPolygons();
                rep.PolygonsA      = polysA.Count;
                rep.PolygonsB      = polysB.Count;
                rep.InvalidPlanesA = CountInvalid(polysA);
                rep.InvalidPlanesB = CountInvalid(polysB);

                // ── b. BSP（Node.cs:206-264 と同じ順）──
                var a1 = new Node(polysA);
                var b1 = new Node(polysB);
                Record(rep, "build A", a1);
                Record(rep, "build B", b1);
                Lap("BSP 構築");

                Node na = a1.Clone();
                Node nb = b1.Clone();
                List<Polygon> result;

                switch (op)
                {
                    case BooleanOpKind.Union:
                        na.ClipTo(nb);  Record(rep, "A.ClipTo(B)", na);
                        nb.ClipTo(na);  Record(rep, "B.ClipTo(A)", nb);
                        nb.Invert();    Record(rep, "B.Invert",    nb);
                        nb.ClipTo(na);  Record(rep, "B.ClipTo(A)", nb);
                        nb.Invert();    Record(rep, "B.Invert",    nb);
                        na.Build(nb.AllPolygons()); Record(rep, "A.Build(B)", na);
                        result = new Node(na.AllPolygons()).AllPolygons();
                        break;

                    case BooleanOpKind.Subtract:
                        na.Invert();    Record(rep, "A.Invert",    na);
                        na.ClipTo(nb);  Record(rep, "A.ClipTo(B)", na);
                        nb.ClipTo(na);  Record(rep, "B.ClipTo(A)", nb);
                        nb.Invert();    Record(rep, "B.Invert",    nb);
                        nb.ClipTo(na);  Record(rep, "B.ClipTo(A)", nb);
                        nb.Invert();    Record(rep, "B.Invert",    nb);
                        na.Build(nb.AllPolygons()); Record(rep, "A.Build(B)", na);
                        na.Invert();    Record(rep, "A.Invert",    na);
                        result = new Node(na.AllPolygons()).AllPolygons();
                        break;

                    case BooleanOpKind.Intersect:
                        na.Invert();    Record(rep, "A.Invert",    na);
                        nb.ClipTo(na);  Record(rep, "B.ClipTo(A)", nb);
                        nb.Invert();    Record(rep, "B.Invert",    nb);
                        na.ClipTo(nb);  Record(rep, "A.ClipTo(B)", na);
                        nb.ClipTo(na);  Record(rep, "B.ClipTo(A)", nb);
                        na.Build(nb.AllPolygons()); Record(rep, "A.Build(B)", na);
                        na.Invert();    Record(rep, "A.Invert",    na);
                        result = new Node(na.AllPolygons()).AllPolygons();
                        break;

                    default:
                        rep.Message = "未知の演算種別: " + op;
                        return rep;
                }

                // ── c. 平面の質 ──
                Lap("BSP 演算");
                rep.ResultPolygons      = result.Count;
                rep.ResultInvalidPlanes = CountInvalid(result);
                rep.ResultSkewedPlanes  = CountSkewed(result);

                // ── d, e. 結果の穴と頂点数 ──
                var model = new CsgModel(result);
                meshResult = (Mesh)model;
                rep.ResultVertices = model.vertices?.Count ?? 0;
                Lap("結果の変換");

                rep.HolesExact  = CountHoles(meshResult, MergeExactThreshold, tJunctionTolerance,
                                             rep.HoleExactSizes, rep.HoleExactCentroids,
                                             out MeshObject exactMesh, out var exactHoles, rep, "exact");
                Lap("結果の穴（ほぼ完全一致）");
                rep.HolesMerged = CountHoles(meshResult, mergeThreshold, tJunctionTolerance,
                                             rep.HoleMergedSizes, rep.HoleMergedCentroids,
                                             out _, out _, rep, "merged");
                Lap("結果の穴（mergeThreshold）");

                // ── f. 穴ごとの性質（ほぼ完全一致でまとめた穴）──
                var invalidPoints = new List<Vector3>();
                foreach (var p in result)
                    if (p?.plane == null || !p.plane.Valid())
                        foreach (var v in p.vertices) invalidPoints.Add(v.position);

                for (int h = 0; h < exactHoles.Count && h < HoleListLimit; h++)
                {
                    rep.HoleExactAreas.Add(LoopArea(exactMesh, exactHoles[h]));

                    int touch = 0;
                    foreach (var hp in exactHoles[h].WorldPositions)
                    {
                        foreach (var ip in invalidPoints)
                            if ((hp - ip).sqrMagnitude <= mergeThreshold * mergeThreshold) { touch++; break; }
                    }
                    rep.HoleExactInvalidTouch.Add(touch);
                }
                Lap("穴の性質");

                // ── g. 写した BSP で同じ演算をして、写し違いが無いか突き合わせる ──
                var counters = new DiagBspCounters();
                var replica = RunReplica(op,
                    new CsgModel(meshA, Matrix4x4.identity).ToPolygons(),
                    new CsgModel(meshB, Matrix4x4.identity).ToPolygons(),
                    counters);

                rep.ReplicaPolygons      = replica.Count;
                rep.ReplicaInvalidPlanes = CountInvalid(replica);
                rep.ReplicaMatches =
                    replica.Count == result.Count &&
                    rep.ReplicaInvalidPlanes == rep.ResultInvalidPlanes &&
                    PositionChecksum(replica) == PositionChecksum(result);

                rep.LeafDiscardValid    = counters.LeafDiscardValid;
                rep.LeafDiscardInvalid  = counters.LeafDiscardInvalid;
                rep.CoplanarBackInvalid = counters.CoplanarBackInvalid;
                rep.InvalidNodeReturns  = counters.InvalidNodeReturns;
                rep.InvalidNodePassed   = counters.InvalidNodePassed;
                Lap("写した BSP");

                // ── h. 無効な平面を作り直して演算したら穴が変わるか（因果の試し）──
                if (fixInvalidPlanes)
                {
                    var fc = new DiagBspCounters { FixInvalidPlanes = true };
                    var fa = new CsgModel(meshA, Matrix4x4.identity).ToPolygons();
                    var fb = new CsgModel(meshB, Matrix4x4.identity).ToPolygons();
                    foreach (var p in fa) DiagNode.FixPlane(p, fc);
                    foreach (var p in fb) DiagNode.FixPlane(p, fc);

                    var fixedResult = RunReplica(op, fa, fb, fc);
                    rep.FixedPolygons      = fixedResult.Count;
                    rep.FixedInvalidPlanes = CountInvalid(fixedResult);
                    rep.FixedPlanesRebuilt = fc.PlanesFixed;

                    Mesh fixedMesh = (Mesh)new CsgModel(fixedResult);
                    try
                    {
                        rep.FixedHolesExact  = CountHoles(fixedMesh, MergeExactThreshold, tJunctionTolerance, null, null, out _, out _);
                        rep.FixedHolesMerged = CountHoles(fixedMesh, mergeThreshold,      tJunctionTolerance, null, null, out _, out _);
                    }
                    finally
                    {
                        if (Application.isPlaying) Object.Destroy(fixedMesh);
                        else                       Object.DestroyImmediate(fixedMesh);
                    }
                    Lap("無効な平面を作り直した演算");
                }

                rep.Success = true;
                return rep;
            }
            finally
            {
                CsgCore.epsilon = savedEpsilon;
                MeshContext.DestroyMesh(meshA);
                MeshContext.DestroyMesh(meshB);
                if (meshResult != null)
                {
                    if (Application.isPlaying) Object.Destroy(meshResult);
                    else                       Object.DestroyImmediate(meshResult);
                }
            }
        }

        // ================================================================
        // 数える
        // ================================================================

        /// <summary>Node.cs:206-264 と同じ順で、写した BSP を撃つ。</summary>
        private static List<Polygon> RunReplica(BooleanOpKind op, List<Polygon> pa, List<Polygon> pb, DiagBspCounters c)
        {
            var a = new DiagNode(c, pa).Clone();
            var b = new DiagNode(c, pb).Clone();

            switch (op)
            {
                case BooleanOpKind.Union:
                    a.ClipTo(b); b.ClipTo(a); b.Invert(); b.ClipTo(a); b.Invert();
                    a.Build(b.AllPolygons());
                    break;
                case BooleanOpKind.Subtract:
                    a.Invert(); a.ClipTo(b); b.ClipTo(a); b.Invert(); b.ClipTo(a); b.Invert();
                    a.Build(b.AllPolygons()); a.Invert();
                    break;
                case BooleanOpKind.Intersect:
                    a.Invert(); b.ClipTo(a); b.Invert(); a.ClipTo(b); b.ClipTo(a);
                    a.Build(b.AllPolygons()); a.Invert();
                    break;
            }
            return new DiagNode(c, a.AllPolygons()).AllPolygons();
        }

        /// <summary>多角形の頂点位置を並び順に重み付けして足した値。写し違いの突き合わせに使う。</summary>
        private static double PositionChecksum(List<Polygon> polys)
        {
            double s = 0;
            long k = 1;
            foreach (var p in polys)
                foreach (var v in p.vertices)
                {
                    s += (v.position.x * 1.0 + v.position.y * 3.0 + v.position.z * 7.0) * (k % 97 + 1);
                    k++;
                }
            return s;
        }

        /// <summary>
        /// 穴のループの面積。CollectHoles の頂点は番号順で輪の順ではないので
        /// （BridgeAutoPairOps.cs:33）、1 回しか使われていない辺をたどって輪の順に並べてから
        /// Newell 法で求める。たどれなければ -1。
        /// </summary>
        private static float LoopArea(MeshObject mo, BridgeAutoPairOps.HoleInfo hole)
        {
            var inHole = new HashSet<int>(hole.Vertices);
            var edgeCount = new Dictionary<(int, int), int>();
            var directed  = new List<(int, int)>();

            foreach (var face in mo.Faces)
            {
                var idx = face?.VertexIndices;
                if (idx == null || idx.Count < 2) continue;
                for (int e = 0; e < idx.Count; e++)
                {
                    int a = idx[e], b = idx[(e + 1) % idx.Count];
                    var key = a < b ? (a, b) : (b, a);
                    edgeCount[key] = edgeCount.TryGetValue(key, out int n) ? n + 1 : 1;
                    if (inHole.Contains(a) && inHole.Contains(b)) directed.Add((a, b));
                }
            }

            var next = new Dictionary<int, int>();
            foreach (var (a, b) in directed)
            {
                var key = a < b ? (a, b) : (b, a);
                if (edgeCount[key] == 1 && !next.ContainsKey(a)) next[a] = b;
            }
            if (next.Count < 3) return -1f;

            int start = next.Keys.First();
            var loop = new List<int> { start };
            int cur = next[start];
            while (cur != start && loop.Count <= next.Count)
            {
                loop.Add(cur);
                if (!next.TryGetValue(cur, out cur)) return -1f;
            }
            if (cur != start) return -1f;

            double nx = 0, ny = 0, nz = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector3 p = mo.Vertices[loop[i]].Position;
                Vector3 q = mo.Vertices[loop[(i + 1) % loop.Count]].Position;
                nx += ((double)p.y - q.y) * ((double)p.z + q.z);
                ny += ((double)p.z - q.z) * ((double)p.x + q.x);
                nz += ((double)p.x - q.x) * ((double)p.y + q.y);
            }
            return (float)(System.Math.Sqrt(nx * nx + ny * ny + nz * nz) * 0.5);
        }

        private static void Record(BooleanDiagnosticReport rep, string step, Node node)
        {
            rep.StepNames.Add(step);
            rep.StepCounts.Add(CountTree(node));
        }

        /// <summary>木の多角形を数える。AllPolygons はリストを書き換えるので使わない。</summary>
        private static int CountTree(Node n)
        {
            if (n == null) return 0;
            return (n.polygons?.Count ?? 0) + CountTree(n.front) + CountTree(n.back);
        }

        private static int CountInvalid(List<Polygon> polys)
        {
            int n = 0;
            foreach (var p in polys)
                if (p?.plane == null || !p.plane.Valid()) n++;
            return n;
        }

        /// <summary>先頭 3 頂点の法線が、頂点全体から求めた法線（Newell 法）と SkewAngleDeg 以上ずれている多角形の数。</summary>
        private static int CountSkewed(List<Polygon> polys)
        {
            int n = 0;
            foreach (var p in polys)
            {
                if (p?.vertices == null || p.vertices.Count < 3 || p.plane == null || !p.plane.Valid()) continue;

                Vector3 newell = Vector3.zero;
                int c = p.vertices.Count;
                for (int i = 0; i < c; i++)
                {
                    Vector3 cur = p.vertices[i].position;
                    Vector3 nxt = p.vertices[(i + 1) % c].position;
                    newell.x += (cur.y - nxt.y) * (cur.z + nxt.z);
                    newell.y += (cur.z - nxt.z) * (cur.x + nxt.x);
                    newell.z += (cur.x - nxt.x) * (cur.y + nxt.y);
                }
                if (newell.sqrMagnitude <= 0f) { n++; continue; }

                if (Vector3.Angle(p.plane.normal, newell) >= SkewAngleDeg) n++;
            }
            return n;
        }

        /// <summary>
        /// Unity Mesh を頂点ごとばらばらのまま MeshObject にし、threshold でまとめ、
        /// T 字を解消してから穴を数える。sizes / centroids が非 null なら穴ごとに書く。
        /// </summary>
        private static int CountHoles(
            Mesh mesh, float threshold, float tJunctionTolerance,
            List<int> sizes, List<float> centroids,
            out MeshObject resolved, out List<BridgeAutoPairOps.HoleInfo> holeList,
            BooleanDiagnosticReport report = null, string stage = null)
        {
            resolved = null;
            holeList = new List<BridgeAutoPairOps.HoleInfo>();

            var mo = new MeshObject("BooleanDiagnostics");
            mo.FromUnityMesh(mesh, mergeVertices: false, includeBoneWeights: false);
            if (mo.VertexCount == 0) return 0;
            resolved = mo;

            // 頂点まとめと T 字解消のどちらに時間がかかるかを分けて残す。
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int before = mo.VertexCount;
            MeshMergeHelper.MergeAllVerticesAtSamePosition(mo, threshold);
            long msMerge = sw.ElapsedMilliseconds;
            int afterMerge = mo.VertexCount;
            RecordTopology(report, stage + ":afterMerge", mo);

            sw.Restart();
            TJunctionOps.Resolve(mo, tJunctionTolerance);
            long msTJ = sw.ElapsedMilliseconds;
            RecordTopology(report, stage + ":afterTJunction", mo);

            sw.Restart();
            var holes = BridgeAutoPairOps.CollectHoles(mo, Matrix4x4.identity);
            holeList = holes;
            long msHoles = sw.ElapsedMilliseconds;

            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}",
                $"[BooleanDiagnostics]   頂点 {before}→{afterMerge} / 面 {mo.FaceCount} / まとめ {msMerge} ms / T字解消 {msTJ} ms / 穴列挙 {msHoles} ms");
            if (sizes != null && centroids != null)
            {
                for (int i = 0; i < holes.Count && i < HoleListLimit; i++)
                {
                    sizes.Add(holes[i].Count);
                    centroids.Add(holes[i].WorldCentroid.x);
                    centroids.Add(holes[i].WorldCentroid.y);
                    centroids.Add(holes[i].WorldCentroid.z);
                }
            }
            return holes.Count;
        }

        private static void RecordTopology(BooleanDiagnosticReport report, string stage, MeshObject mesh)
        {
            if (report == null) return;
            var counts = BoundaryEdgeOps.AnalyzeTopology(mesh);
            report.TopologyStages.Add(stage);
            report.BoundaryEdgeCounts.Add(counts.BoundaryEdges);
            report.NonManifoldEdgeCounts.Add(counts.NonManifoldEdges);
            report.InconsistentWindingEdgeCounts.Add(counts.InconsistentWindingEdges);
        }
    }
}
