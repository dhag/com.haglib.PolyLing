// BridgeAutoPairOps.cs
// ブリッジ（穴つなぎ）の種を自動で選ぶための位相・幾何計算。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【手順】
//   1. 物体A・物体B の穴（エッジグループ）をそれぞれ列挙し、
//      穴を構成する頂点と、その頂点のワールド重心を求める。
//   2. 頂点数が同数の穴ペアだけを候補にする。
//   3. 候補のうち重心距離が最小のものを採る。等距離のものはすべて残す。
//   4. 残ったペアの全頂点を総当たりし、最短距離の頂点ペア (A0, B0) を選ぶ。
//      これがブリッジの種になる。
//
// 【並びの固定】BoundaryEdgeOps.BuildGroups は HashSet を走査するので列挙順が
//   保証されない。穴は「構成頂点の最小番号」で、穴内の頂点は番号昇順で並べ直し、
//   同じメッシュに対して毎回同じ結果が出るようにする。
//
// 座標はすべてワールド空間で扱う。物体ごとに WorldMatrix が違うため。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class BridgeAutoPairOps
    {
        /// <summary>重心距離を「等距離」とみなす許容差。</summary>
        public const float DistanceEpsilon = 1e-5f;

        /// <summary>穴 1 つ分の情報。座標はワールド空間。</summary>
        public sealed class HoleInfo
        {
            /// <summary>穴を構成する頂点（番号昇順）。</summary>
            public List<int> Vertices = new List<int>();

            /// <summary>Vertices と同じ並びのワールド座標。</summary>
            public List<Vector3> WorldPositions = new List<Vector3>();

            /// <summary>構成頂点のワールド重心。</summary>
            public Vector3 WorldCentroid;

            /// <summary>穴を構成する頂点数。</summary>
            public int Count => Vertices.Count;
        }

        /// <summary>ペアを選べなかった理由。</summary>
        public enum PairFailure
        {
            None = 0,
            /// <summary>どちらかに穴が無い。</summary>
            NoHoles,
            /// <summary>頂点数が同数の穴ペアが無い。</summary>
            NoSameCountPair,
            /// <summary>頂点ペアを決められなかった。</summary>
            NoVertexPair,
        }

        /// <summary>ペア選択の結果。</summary>
        public struct PairResult
        {
            public bool        Ok;
            public PairFailure Failure;
            public string      Message;

            /// <summary>採用した穴の番号（CollectHoles が返したリストの添字）。</summary>
            public int HoleA, HoleB;

            /// <summary>種にする頂点（メッシュ内の頂点番号）。</summary>
            public int VertexA, VertexB;

            /// <summary>採用ペアの重心距離。</summary>
            public float CentroidDistance;

            /// <summary>採用した頂点ペアの距離。</summary>
            public float VertexDistance;
        }

        // ================================================================
        // 穴の列挙
        // ================================================================

        /// <summary>
        /// メッシュの穴（エッジグループ）を列挙する。頂点が 3 未満のグループは捨てる。
        /// toWorld は頂点をワールドへ出す行列。
        /// </summary>
        public static List<HoleInfo> CollectHoles(MeshObject mesh, Matrix4x4 toWorld)
        {
            var holes = new List<HoleInfo>();
            if (mesh == null) return holes;

            var groups = BoundaryEdgeOps.BuildGroups(BoundaryEdgeOps.CollectBoundaryEdges(mesh));

            foreach (var g in groups)
            {
                var verts = BoundaryEdgeOps.VerticesOf(g);
                if (verts.Count < 3) continue;
                verts.Sort();

                var hole = new HoleInfo();
                var sum  = Vector3.zero;

                foreach (int v in verts)
                {
                    if (v < 0 || v >= mesh.Vertices.Count) continue;
                    Vector3 p = toWorld.MultiplyPoint3x4(mesh.Vertices[v].Position);
                    hole.Vertices.Add(v);
                    hole.WorldPositions.Add(p);
                    sum += p;
                }

                if (hole.Vertices.Count < 3) continue;

                hole.WorldCentroid = sum / hole.Vertices.Count;
                holes.Add(hole);
            }

            holes.Sort((a, b) => a.Vertices[0].CompareTo(b.Vertices[0]));
            return holes;
        }

        // ================================================================
        // ペア選択
        // ================================================================

        /// <summary>
        /// 穴リスト A・B から、ブリッジの種にする穴ペアと頂点ペアを選ぶ。
        /// sameMesh が true のとき（同一物体内の 2 つの穴を対象にするとき）は、
        /// 同じ穴どうしの組み合わせと重複する組み合わせを除く。
        /// </summary>
        public static PairResult SelectPair(
            IReadOnlyList<HoleInfo> holesA, IReadOnlyList<HoleInfo> holesB, bool sameMesh)
        {
            var r = new PairResult
            {
                HoleA = -1, HoleB = -1, VertexA = -1, VertexB = -1,
            };

            if (holesA == null || holesA.Count == 0 || holesB == null || holesB.Count == 0)
            {
                r.Failure = PairFailure.NoHoles;
                r.Message = "穴が見つかりません";
                return r;
            }

            // ── 1. 頂点数が同数のペアを候補にする ──
            var cands = new List<(int A, int B, float D)>();

            for (int i = 0; i < holesA.Count; i++)
            {
                for (int j = 0; j < holesB.Count; j++)
                {
                    if (sameMesh && j <= i) continue;                  // 自分自身と重複を除く
                    if (holesA[i].Count != holesB[j].Count) continue;   // 頂点数が違う

                    float d = Vector3.Distance(holesA[i].WorldCentroid, holesB[j].WorldCentroid);
                    cands.Add((i, j, d));
                }
            }

            if (cands.Count == 0)
            {
                r.Failure = PairFailure.NoSameCountPair;
                r.Message = "頂点数が同じ穴のペアがありません";
                return r;
            }

            // ── 2. 重心距離が最小のもの（等距離はすべて残す） ──
            float dMin = float.MaxValue;
            foreach (var c in cands)
                if (c.D < dMin) dMin = c.D;

            // ── 3. 残った候補の全頂点を総当たりし、最短の頂点ペアを採る ──
            float bestSq = float.MaxValue;

            foreach (var c in cands)
            {
                if (c.D > dMin + DistanceEpsilon) continue;

                var ha = holesA[c.A];
                var hb = holesB[c.B];

                for (int p = 0; p < ha.WorldPositions.Count; p++)
                {
                    for (int q = 0; q < hb.WorldPositions.Count; q++)
                    {
                        float sq = (ha.WorldPositions[p] - hb.WorldPositions[q]).sqrMagnitude;
                        if (sq >= bestSq) continue;   // 等距離は先に見つけた方を残す

                        bestSq  = sq;
                        r.HoleA = c.A;
                        r.HoleB = c.B;
                        r.VertexA = ha.Vertices[p];
                        r.VertexB = hb.Vertices[q];
                        r.CentroidDistance = c.D;
                    }
                }
            }

            if (r.VertexA < 0 || r.VertexB < 0)
            {
                r.Failure = PairFailure.NoVertexPair;
                r.Message = "頂点ペアを決められません";
                return r;
            }

            r.VertexDistance = Mathf.Sqrt(bestSq);
            r.Ok      = true;
            r.Failure = PairFailure.None;
            r.Message = $"穴 {holesA[r.HoleA].Count} 頂点 / 重心距離 {r.CentroidDistance:0.####}";
            return r;
        }
    }
}
