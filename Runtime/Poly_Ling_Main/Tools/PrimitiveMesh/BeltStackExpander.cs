// BeltStackExpander.cs
// 梯子1本（基準段）から、レール辺を跨いで上下（左右レール側）の隣段を展開する。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【横断の考え方】基準段のステップ s は四角形 Faces[s] を占め、その左レール辺は
//   (Left[s], Left[s+1])。この辺を共有するもう1枚の四角形が「1つ外側の段」の
//   ステップ s になる。四角形の対辺を取れば、さらに外側のレール頂点が決まる。
//   辺は面どうしで厳密に共有されるため、隣段の rung は基準段の rung と1対1で対応し、
//   段数一致は構造的に保証される。
//
// 【停止条件】
//   - 相手面が無い（自由辺）
//   - 相手面が四角形でない（三角形＝先端に当たった、など）
//   - 相手面が他グループで消費済み
//   - 対辺／アンカーが取れない（ねじれ四角形など）
//   - 同一グループで既出の面に当たった → 縦方向に閉じたとみなして打ち切る
//
// 【途中で切れた場合】開いた基準段では、そこまでの rung で隣段を成立させる（部分採用）。
//   閉じた基準段（円環）では rung の回転が必要になるため、全周成功したときだけ採用する。
//
// 【段の並び】Left 側へ伸ばした段を先頭へ、Right 側へ伸ばした段を末尾へ積む。
//   結果として rows[r] の Right レールと rows[r+1] の Left レールが同じレールになる。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>梯子走査で共用するトポロジ操作。</summary>
    public static class BeltTopology
    {
        /// <summary>辺 → その辺を持つ面インデックス列。三角形も含む（先端判定に要るため）。</summary>
        public static Dictionary<VertexPair, List<int>> BuildEdgeToFaces(MeshObject mo)
        {
            var map = new Dictionary<VertexPair, List<int>>();
            if (mo == null) return map;

            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                var v = mo.Faces[fi]?.VertexIndices;
                if (v == null || v.Count < 3) continue;

                for (int i = 0; i < v.Count; i++)
                {
                    var key = new VertexPair(v[i], v[(i + 1) % v.Count]);
                    if (!key.IsValid) continue;
                    if (!map.TryGetValue(key, out var list)) { list = new List<int>(); map[key] = list; }
                    list.Add(fi);
                }
            }
            return map;
        }

        /// <summary>頂点 → その頂点を持つ面インデックス列。</summary>
        public static Dictionary<int, List<int>> BuildVertexToFaces(MeshObject mo)
        {
            var map = new Dictionary<int, List<int>>();
            if (mo == null) return map;

            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                var v = mo.Faces[fi]?.VertexIndices;
                if (v == null || v.Count < 3) continue;

                for (int i = 0; i < v.Count; i++)
                {
                    int vi = v[i];
                    if (vi < 0) continue;
                    if (!map.TryGetValue(vi, out var list)) { list = new List<int>(); map[vi] = list; }
                    if (!list.Contains(fi)) list.Add(fi);
                }
            }
            return map;
        }

        public static bool IsQuad(MeshObject mo, int fi)
            => mo != null && fi >= 0 && fi < mo.FaceCount && mo.Faces[fi]?.VertexIndices != null
               && mo.Faces[fi].VertexIndices.Count == 4;

        public static bool IsTriangle(MeshObject mo, int fi)
            => mo != null && fi >= 0 && fi < mo.FaceCount && mo.Faces[fi]?.VertexIndices != null
               && mo.Faces[fi].VertexIndices.Count == 3;

        /// <summary>四角形の、指定辺と向かい合う辺。</summary>
        public static VertexPair? FindOppositeEdge(Face face, VertexPair edge)
        {
            var verts = face?.VertexIndices;
            if (verts == null || verts.Count != 4) return null;

            for (int i = 0; i < 4; i++)
            {
                int a = verts[i];
                int b = verts[(i + 1) % 4];
                if ((a == edge.V1 && b == edge.V2) || (a == edge.V2 && b == edge.V1))
                    return new VertexPair(verts[(i + 2) % 4], verts[(i + 3) % 4]);
            }
            return null;
        }

        /// <summary>既知辺のアンカー頂点に対応する、対辺側の頂点。</summary>
        public static int OppositeAnchor(Face face, VertexPair knownEdge, int knownAnchor)
        {
            var verts = face?.VertexIndices;
            if (verts == null || verts.Count != 4) return -1;

            for (int i = 0; i < 4; i++)
            {
                int a = verts[i];
                int b = verts[(i + 1) % 4];
                if ((a == knownEdge.V1 && b == knownEdge.V2) || (a == knownEdge.V2 && b == knownEdge.V1))
                {
                    if (knownAnchor == a) return verts[(i + 3) % 4];
                    if (knownAnchor == b) return verts[(i + 2) % 4];
                    return -1;
                }
            }
            return -1;
        }

        /// <summary>辺を共有する、exceptFace 以外の面。無ければ -1。</summary>
        public static int OtherFace(Dictionary<VertexPair, List<int>> edgeToFaces, VertexPair edge, int exceptFace)
        {
            if (edgeToFaces == null) return -1;
            if (!edgeToFaces.TryGetValue(edge, out var list)) return -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != exceptFace) return list[i];
            return -1;
        }

        /// <summary>先頭面の巻き順を調べ、(L0, R0, ...) と逆向きなら true。</summary>
        public static bool DetectFlipWinding(MeshObject mo, int faceIndex, int l0, int r0)
        {
            var verts = (faceIndex >= 0 && faceIndex < (mo?.FaceCount ?? 0))
                ? mo.Faces[faceIndex]?.VertexIndices : null;
            if (verts == null) return false;

            for (int i = 0; i < verts.Count; i++)
            {
                if (verts[i] != l0) continue;
                return verts[(i + 1) % verts.Count] != r0;
            }
            return false;
        }
    }

    public static class BeltStackExpander
    {
        /// <summary>段数の上限（縦に閉じた形での暴走防止）。</summary>
        private const int MaxRows = 4096;

        // ================================================================
        // 展開
        // ================================================================

        /// <summary>
        /// 基準段から上下へ横断し、段を Left 側 → Right 側 の順に並べて返す。
        /// 戻り値の先頭要素の Left レールが t=0 側、末尾要素の Right レールが t=1 側になる。
        /// consumed には他グループが確定済みの面を渡す（この関数は追加しない）。
        /// </summary>
        public static List<BeltAutoStrip> Expand(
            MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            BeltAutoStrip baseRow,
            HashSet<int> consumed,
            out bool verticalClosed)
        {
            verticalClosed = false;

            var rows = new List<BeltAutoStrip>();
            if (mo == null || edgeToFaces == null || baseRow == null) return rows;
            if (baseRow.RungCount < 2 || baseRow.Faces.Count == 0) return rows;

            rows.Add(baseRow);

            var used = new HashSet<int>(baseRow.Faces);
            var skip = consumed ?? new HashSet<int>();

            // ── Left 側へ ──
            var cur = baseRow;
            for (int i = 0; i < MaxRows; i++)
            {
                var next = CrossRow(mo, edgeToFaces, cur, true, skip, used, out bool hitUsed);
                if (hitUsed) verticalClosed = true;
                if (next == null) break;

                rows.Insert(0, next);
                foreach (int f in next.Faces) used.Add(f);
                cur = next;
            }

            // 縦に閉じているなら Left 側だけで一周しているので、Right 側は辿らない。
            if (!verticalClosed)
            {
                cur = baseRow;
                for (int i = 0; i < MaxRows; i++)
                {
                    var next = CrossRow(mo, edgeToFaces, cur, false, skip, used, out bool hitUsed);
                    if (hitUsed) verticalClosed = true;
                    if (next == null) break;

                    rows.Add(next);
                    foreach (int f in next.Faces) used.Add(f);
                    cur = next;
                }
            }

            return rows;
        }

        /// <summary>
        /// 複数の基準段をまとめて展開し、グループ番号と段番号を振る。
        /// 円環検索・手動取り込みから使う。cross = false なら展開せず1段グループにする。
        /// </summary>
        public static List<BeltAutoStrip> ExpandAll(
            MeshObject mo, IReadOnlyList<BeltAutoStrip> bases, bool cross, out int groupCount)
        {
            var result = new List<BeltAutoStrip>();
            groupCount = 0;

            if (mo == null || bases == null) return result;

            var edgeToFaces = BeltTopology.BuildEdgeToFaces(mo);
            var consumed    = new HashSet<int>();

            for (int i = 0; i < bases.Count; i++)
            {
                var b = bases[i];
                if (b == null || b.RungCount < 2 || b.Faces.Count == 0) continue;

                bool overlap = false;
                foreach (int f in b.Faces) if (consumed.Contains(f)) { overlap = true; break; }
                if (overlap) continue;

                var rows = cross
                    ? Expand(mo, edgeToFaces, b, consumed, out _)
                    : new List<BeltAutoStrip> { b };

                AssignGroup(rows, groupCount++, consumed);
                result.AddRange(rows);
            }

            return result;
        }

        /// <summary>段リストにグループ番号・段番号を振り、構成面を消費済みにする。</summary>
        public static void AssignGroup(List<BeltAutoStrip> rows, int groupId, HashSet<int> consumed)
        {
            if (rows == null) return;

            for (int r = 0; r < rows.Count; r++)
            {
                rows[r].GroupId  = groupId;
                rows[r].RowIndex = r;
                rows[r].RowCount = rows.Count;

                if (consumed == null) continue;
                foreach (int f in rows[r].Faces) consumed.Add(f);
            }
        }

        // ================================================================
        // 横断1段ぶん
        // ================================================================

        /// <summary>
        /// row の指定側レールを跨いで、1つ外側の段を作る。作れなければ null。
        /// hitUsed = true は「同一グループで既出の面に当たった」＝縦方向に閉じた合図。
        /// </summary>
        private static BeltAutoStrip CrossRow(
            MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            BeltAutoStrip row, bool toLeft,
            HashSet<int> consumed, HashSet<int> used,
            out bool hitUsed)
        {
            hitUsed = false;

            var rail = toLeft ? row.Left : row.Right;
            int n = row.RungCount;
            if (n < 2 || row.Faces.Count < 1) return null;

            // 開いた段の面数は rung 数 - 1、閉じた段は rung 数。
            // 自己接触した梯子で面数が多めに入っていても、rung を跨がないように詰める。
            int steps = row.Closed
                ? Mathf.Min(row.Faces.Count, n)
                : Mathf.Min(row.Faces.Count, n - 1);
            if (steps < 1) return null;

            var outer = new List<int>();
            var faces = new List<int>();

            for (int s = 0; s < steps; s++)
            {
                int i0 = s;
                int i1 = (s + 1) % n;
                if (i0 >= rail.Count || i1 >= rail.Count) break;

                var e = new VertexPair(rail[i0], rail[i1]);
                if (!e.IsValid) break;

                int f = BeltTopology.OtherFace(edgeToFaces, e, row.Faces[s]);
                if (f < 0) break;                                   // 自由辺
                if (used.Contains(f)) { hitUsed = true; break; }    // 一周した
                if (consumed.Contains(f)) break;                    // 他グループが使用済み
                if (!BeltTopology.IsQuad(mo, f)) break;             // 三角形など

                var face = mo.Faces[f];
                var opp  = BeltTopology.FindOppositeEdge(face, e);
                if (!opp.HasValue || !opp.Value.IsValid) break;

                int b0 = BeltTopology.OppositeAnchor(face, e, rail[i0]);
                if (b0 < 0) break;

                int b1 = opp.Value.GetOtherVertex(b0);
                if (b1 < 0) break;

                if (outer.Count == 0) outer.Add(b0);
                else if (outer[outer.Count - 1] != b0) break;       // レールがつながらない

                outer.Add(b1);
                faces.Add(f);
            }

            if (faces.Count < 1) return null;

            bool full = (faces.Count == steps);

            if (row.Closed)
            {
                // 閉じた段は全周そろったときだけ採用する（部分採用は rung の回転が要るため）。
                if (!full || steps != n) return null;
                if (outer.Count != n + 1) return null;
                if (outer[n] != outer[0]) return null;   // ねじれて戻った
                outer.RemoveAt(n);
            }

            if (outer.Count < 2) return null;

            var st = new BeltAutoStrip();
            int cnt = outer.Count;

            for (int i = 0; i < cnt; i++)
            {
                if (i >= rail.Count) return null;

                if (toLeft) { st.Left.Add(outer[i]); st.Right.Add(rail[i]); }
                else        { st.Left.Add(rail[i]);  st.Right.Add(outer[i]); }
            }

            st.Faces.AddRange(faces);
            st.Closed      = row.Closed && full;
            st.FlipWinding = BeltTopology.DetectFlipWinding(mo, faces[0], st.Left[0], st.Right[0]);
            return st;
        }
    }
}
