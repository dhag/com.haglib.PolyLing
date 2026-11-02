// BevelMeshModifier.cs
// 処理列の種類「ベベル」。範囲の辺、または面の角度で決めた辺をベベルする。実処理は EdgeBevelOps.Execute（ベベルツールと共用）。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 段 7a・段 9b（辺の決め方「角度」）
//
// 【対象の辺】limitMethod で決める。
//   Scope（範囲・既定）：範囲の辺（保存は頂点 ID の組。項目の入力へ引き直したもの）。
//     全体の範囲・辺の無い範囲は失敗（全辺を勝手にベベルしない）。
//   Angle（角度）：隣の 2 面の法線のなす角が angle 以上の辺（Blender のベベルの制限方法「角度」と同じ）。
//     範囲が全体ならメッシュ全体から、それ以外は両端が範囲に入る辺だけから選ぶ。
//     面の頂点を共有していない辺（UV の継ぎ目などで頂点が分かれた辺）は位置で組を作って角度を測る。
//   どちらも、辺は集まったが隣の面が 2 枚そろわずベベルできないときは失敗。
//   既定を Scope にしているのは、この引数の無い保存済みの項目の結果を変えないため。
//
// 【出力】入力の Clone を EdgeBevelOps がその場で書き換えたもの。範囲の外の面はそのまま残る。
//   Clone はイベントを写さないので、孤立頂点の除去で選択・パーツ選択辞書が付け替わることはない。
//
// 【ID】自動では振らない。新しい頂点には EdgeBevelOps.Result.SourceOf が指す元の頂点の ID を写す
//   （1 つの頂点から複数作るので同じ ID が複数になる）。書き換えた面は元の面の ID のまま、
//   足した面（ベベルの帯・ふた）は 0（未設定）。

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Selection;

namespace Poly_Ling.Modifiers
{
    /// <summary>ベベルする辺の決め方。</summary>
    public enum BevelLimitMethod
    {
        /// <summary>範囲の辺。</summary>
        Scope = 0,
        /// <summary>隣の 2 面の角度が閾値以上の辺。</summary>
        Angle = 1,
    }

    [PLMeshModifier("bevel", DisplayName = "ベベル", Category = "topology",
                    Change = MeshModifierChange.Topology,
                    Description = "辺をベベル（面取り）する。辺は範囲で指定するか、面の角度で決める")]
    public sealed class BevelMeshModifier : IMeshModifier
    {
        [PLParam(Description = "ベベル量（ローカル座標の長さ）", Min = 0.0001, Max = 10.0, Step = 0.001)]
        public float Amount { get; set; } = 0.05f;

        [PLParam(Description = "分割数", Min = 1, Max = 16, Step = 1)]
        public int Segments { get; set; } = 1;

        [PLParam(Description = "角を弧で結ぶ（分割数 2 以上で効く）")]
        public bool Fillet { get; set; } = false;

        [PLParam(Description = "足す面の材質番号", Min = 0, Max = 255, Step = 1)]
        public int MaterialIndex { get; set; } = 0;

        [PLParam(Description = "辺の決め方。Scope＝範囲の辺 / Angle＝隣の 2 面の角度が閾値以上の辺（範囲が全体ならメッシュ全体から）")]
        public BevelLimitMethod LimitMethod { get; set; } = BevelLimitMethod.Scope;

        [PLParam(Description = "角度で決めるときの閾値（度）。隣の 2 面の法線のなす角がこれ以上の辺をベベルする", Min = 0.0, Max = 180.0, Step = 1)]
        public float Angle { get; set; } = 30f;

        public MeshObject Evaluate(MeshModifierInput input, out string error)
        {
            error = null;
            var src = input?.Source;
            if (src == null) { error = "入力のメッシュが無い"; return null; }
            var scope = input.Scope ?? ResolvedMeshModifierScope.CreateAll();

            List<VertexPair> edges;
            if (LimitMethod == BevelLimitMethod.Angle)
            {
                edges = CollectEdgesByAngle(src, scope, Angle);
                if (edges.Count == 0) { error = $"角度 {Angle} 度以上の辺が無い"; return null; }
            }
            else
            {
                if (scope.IsAll) { error = "ベベルする辺を範囲で指定する（全体は受けない。全体を角度で決めるなら limitMethod=Angle）"; return null; }
                if (scope.Edges.Count == 0) { error = "範囲に辺が無い"; return null; }
                edges = scope.Edges.OrderBy(e => e.V1).ThenBy(e => e.V2).ToList();
            }

            var dst = src.Clone();
            var r = EdgeBevelOps.Execute(dst, edges, Amount, Segments, Fillet, MaterialIndex);
            if (r.CollectedEdgeCount == 0 || r.BeveledEdgeCount == 0)
            { error = "ベベルできる辺が無い（隣の面が 2 枚そろわない）"; return null; }

            // 新しい頂点に元の頂点の ID を写す
            for (int i = 0; i < dst.Vertices.Count && i < r.Created.Length; i++)
            {
                if (!r.Created[i]) continue;
                int s = r.SourceOf[i];
                if (s >= 0 && s < src.Vertices.Count) dst.Vertices[i].Id = src.Vertices[s].Id;
            }

            dst.RebuildIdSets();
            dst.InvalidatePositionCache();
            return dst;
        }

        /// <summary>
        /// 隣の 2 面の法線のなす角が angleDeg 以上の辺を集める（頂点番号の昇順に並べる）。
        /// 辺は両端の位置で組にするので、頂点が分かれた辺も 1 本として扱う（代表は最初に見つけた面の頂点番号）。
        /// 面が 2 枚でない辺（境界・3 枚以上）は選ばない。範囲が全体でなければ両端が範囲に入る辺だけ。
        /// </summary>
        public static List<VertexPair> CollectEdgesByAngle(MeshObject mesh, ResolvedMeshModifierScope scope, float angleDeg)
        {
            var groups = new Dictionary<(Vector3Int, Vector3Int), (VertexPair rep, List<int> faces)>();
            var vl = mesh.Vertices;
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var idx = mesh.Faces[fi].VertexIndices;
                int n = idx.Count;
                if (n < 3) continue;
                for (int j = 0; j < n; j++)
                {
                    int a = idx[j], b = idx[(j + 1) % n];
                    if (a == b || a < 0 || b < 0 || a >= vl.Count || b >= vl.Count) continue;
                    var ka = Quantize(vl[a].Position);
                    var kb = Quantize(vl[b].Position);
                    var key = Less(ka, kb) ? (ka, kb) : (kb, ka);
                    if (!groups.TryGetValue(key, out var g)) { g = (new VertexPair(a, b), new List<int>()); groups[key] = g; }
                    if (!g.faces.Contains(fi)) g.faces.Add(fi);
                }
            }

            float cosLimit = Mathf.Cos(Mathf.Clamp(angleDeg, 0f, 180f) * Mathf.Deg2Rad);
            var result = new List<VertexPair>();
            foreach (var g in groups.Values)
            {
                if (g.faces.Count != 2) continue;
                if (scope != null && !scope.IsAll
                    && !(scope.ContainsVertex(g.rep.V1) && scope.ContainsVertex(g.rep.V2))) continue;
                Vector3 n0 = FaceNormal(mesh, g.faces[0]);
                Vector3 n1 = FaceNormal(mesh, g.faces[1]);
                if (n0 == Vector3.zero || n1 == Vector3.zero) continue;
                // 法線のなす角 ≥ angle ⇔ cos ≤ cos(angle)。角度 0 は平らな辺も含む
                if (Vector3.Dot(n0, n1) <= cosLimit + 1e-6f) result.Add(g.rep);
            }
            return result.OrderBy(e => e.V1).ThenBy(e => e.V2).ToList();
        }

        private static Vector3Int Quantize(Vector3 p)
            => new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));

        private static bool Less(Vector3Int a, Vector3Int b)
            => a.x != b.x ? a.x < b.x : a.y != b.y ? a.y < b.y : a.z < b.z;

        private static Vector3 FaceNormal(MeshObject mesh, int fi)
        {
            var idx = mesh.Faces[fi].VertexIndices;
            Vector3 p0 = mesh.Vertices[idx[0]].Position;
            Vector3 sum = Vector3.zero;
            for (int i = 1; i + 1 < idx.Count; i++)
                sum += NormalHelper.CalculateFaceNormal(p0, mesh.Vertices[idx[i]].Position, mesh.Vertices[idx[i + 1]].Position);
            return sum.sqrMagnitude > 1e-20f ? sum.normalized : Vector3.zero;
        }
    }
}
