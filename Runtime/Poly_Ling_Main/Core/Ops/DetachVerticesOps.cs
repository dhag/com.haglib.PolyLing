// DetachVerticesOps.cs
// 頂点の分離：切れ目の辺に沿って頂点を分け、面どうしのつながりを切る。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【規則】（PolyLing_UV_Billboard_Design.md 6.4。XYZ と UV の代理の両方で使う）
//   1. 切れ目の辺を決める。
//        面モード：選択面と非選択面の境界の辺（選択面を周りから切り離す）。
//        辺モード：選択辺（選択辺に沿って切り開く）。
//   2. 切れ目の辺の端の頂点ごとに、その頂点を使う面（3 頂点以上）を、
//      その頂点を端に持つ「切れ目でない辺」を共有するものどうしでまとめる（扇形）。
//   3. 扇形が 2 つ以上なら、先頭の面を含む扇形は元の頂点のまま、ほかの扇形には
//      元の頂点の複製を 1 つずつ割り当てる。複製は UV・法線スロット・ウェイトも写す
//      （Vertex.Clone）ので、面の UV / 法線の番号はそのまま通る。
//   4. 分ける前にその頂点で使われていて、分けた後に元の頂点・複製のどちらかで使われなく
//      なったスロットを、それぞれの頂点から詰める（VertexSlotOps.CompactUnusedSlots）。
//      元から使われていないスロットには触れない。
//   既存の「頂点分割」（SplitVerticesTool）は、全ての辺を切れ目にした場合と同じ結果になる。
//
// 【変えないもの】
//   面の実体（Face.Id・並び）、線分（2 頂点の面）、線分群。線分が使う頂点は元の頂点に残る。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    /// <summary>頂点の分離の切れ目の決め方。</summary>
    public enum DetachMode
    {
        /// <summary>選択面と非選択面の境界の辺。</summary>
        Faces = 0,

        /// <summary>選択辺。</summary>
        Edges = 1,
    }

    public static class DetachVerticesOps
    {
        /// <summary>切れ目の辺を集める。</summary>
        public static HashSet<VertexPair> CollectCutEdges(MeshObject mo, SelectionState sel, DetachMode mode)
        {
            var cuts = new HashSet<VertexPair>();
            if (mo == null || sel == null) return cuts;

            if (mode == DetachMode.Edges)
            {
                foreach (var e in sel.Edges) cuts.Add(e);
                return cuts;
            }

            // 面モード：辺ごとに、選択面と非選択面の両方が使っていれば境界。
            var hasSel   = new HashSet<VertexPair>();
            var hasUnsel = new HashSet<VertexPair>();
            for (int f = 0; f < mo.FaceCount; f++)
            {
                var face = mo.Faces[f];
                if (face == null || face.VertexCount < 3) continue;
                bool s = sel.Faces.Contains(f);
                int n = face.VertexCount;
                for (int k = 0; k < n; k++)
                {
                    var e = new VertexPair(face.VertexIndices[k], face.VertexIndices[(k + 1) % n]);
                    (s ? hasSel : hasUnsel).Add(e);
                }
            }
            foreach (var e in hasSel) if (hasUnsel.Contains(e)) cuts.Add(e);
            return cuts;
        }

        /// <summary>分けると増える頂点の数（実行前の見積もり）。</summary>
        public static int CountNewVertices(MeshObject mo, SelectionState sel, DetachMode mode)
        {
            if (mo == null || sel == null) return 0;
            var cuts = CollectCutEdges(mo, sel, mode);
            if (cuts.Count == 0) return 0;
            int n = 0;
            foreach (var fans in BuildFans(mo, cuts).Values) n += fans.Count - 1;
            return n;
        }

        /// <summary>
        /// 頂点を分ける。増えた頂点の数を返す。面・線分の実体は変えない。
        /// 増えた頂点は末尾に足す（MeshObject.AddVertex。頂点 ID はそこで振られる）。
        /// </summary>
        public static int Execute(MeshObject mo, SelectionState sel, DetachMode mode)
        {
            if (mo == null || sel == null) return 0;
            var cuts = CollectCutEdges(mo, sel, mode);
            if (cuts.Count == 0) return 0;

            int origCount = mo.VertexCount;
            int added = 0;
            var fansOf = BuildFans(mo, cuts);

            // 分ける前に各頂点で使われていたスロット。分けた後、元の頂点と複製の
            // それぞれで使われなくなったものを詰める（複製は全スロットを写すため）。
            var usedBefore = VertexSlotOps.UsedSlots(mo, fansOf.Keys);
            var candidates = new Dictionary<int, HashSet<int>>();

            foreach (var kv in fansOf)
            {
                int v = kv.Key;
                var fans = kv.Value;
                candidates[v] = usedBefore[v];
                // 先頭の扇形（最初に見つかった面を含む）は元の頂点のまま。
                for (int g = 1; g < fans.Count; g++)
                {
                    var clone = mo.Vertices[v].Clone();
                    clone.Id = 0;   // AddVertex に ID を振らせる
                    int nv = mo.AddVertex(clone);
                    candidates[nv] = new HashSet<int>(usedBefore[v]);
                    foreach (int f in fans[g])
                    {
                        var vi = mo.Faces[f].VertexIndices;
                        for (int k = 0; k < vi.Count; k++) if (vi[k] == v) vi[k] = nv;
                    }
                    added++;
                }
            }

            if (added > 0)
            {
                VertexSlotOps.CompactUnusedSlots(mo, candidates);
                // 今回増えた頂点を 1 つの部品として扱う（頂点分割と同じ扱い）。
                PartsIdOps.AssignNewVertices(mo, origCount);
                mo.InvalidatePositionCache();
            }
            return added;
        }

        /// <summary>
        /// 切れ目の辺の端の頂点ごとに、面の扇形（面番号の組）を作る。扇形が 2 つ以上の頂点だけを返す。
        /// 扇形の並びは、各扇形の最小の面番号の順。
        /// </summary>
        private static Dictionary<int, List<List<int>>> BuildFans(MeshObject mo, HashSet<VertexPair> cuts)
        {
            var candidates = new HashSet<int>();
            foreach (var e in cuts) { candidates.Add(e.V1); candidates.Add(e.V2); }

            // 頂点 → それを使う面（3 頂点以上）
            var facesOf = new Dictionary<int, List<int>>();
            for (int f = 0; f < mo.FaceCount; f++)
            {
                var face = mo.Faces[f];
                if (face == null || face.VertexCount < 3) continue;
                foreach (int vi in face.VertexIndices)
                {
                    if (!candidates.Contains(vi)) continue;
                    if (!facesOf.TryGetValue(vi, out var list)) facesOf[vi] = list = new List<int>();
                    if (!list.Contains(f)) list.Add(f);
                }
            }

            var result = new Dictionary<int, List<List<int>>>();
            foreach (var kv in facesOf)
            {
                int v = kv.Key;
                var faces = kv.Value;
                if (faces.Count < 2) continue;

                // 面の番号 → 扇形の代表（union-find）
                var parent = new Dictionary<int, int>();
                foreach (int f in faces) parent[f] = f;
                int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
                void Unite(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[System.Math.Max(a, b)] = System.Math.Min(a, b); }

                // v を端に持つ辺 → その辺を使う面
                var facesOnEdge = new Dictionary<VertexPair, List<int>>();
                foreach (int f in faces)
                {
                    var vi = mo.Faces[f].VertexIndices;
                    int n = vi.Count;
                    for (int k = 0; k < n; k++)
                    {
                        if (vi[k] != v) continue;
                        foreach (int w in new[] { vi[(k + n - 1) % n], vi[(k + 1) % n] })
                        {
                            if (w == v) continue;
                            var e = new VertexPair(v, w);
                            if (!facesOnEdge.TryGetValue(e, out var list)) facesOnEdge[e] = list = new List<int>();
                            if (!list.Contains(f)) list.Add(f);
                        }
                    }
                }
                foreach (var ekv in facesOnEdge)
                {
                    if (cuts.Contains(ekv.Key)) continue;
                    var list = ekv.Value;
                    for (int i = 1; i < list.Count; i++) Unite(list[0], list[i]);
                }

                var groups = new SortedDictionary<int, List<int>>();
                foreach (int f in faces)
                {
                    int r = Find(f);
                    if (!groups.TryGetValue(r, out var g)) groups[r] = g = new List<int>();
                    g.Add(f);
                }
                if (groups.Count < 2) continue;
                result[v] = new List<List<int>>(groups.Values);
            }
            return result;
        }
    }
}
