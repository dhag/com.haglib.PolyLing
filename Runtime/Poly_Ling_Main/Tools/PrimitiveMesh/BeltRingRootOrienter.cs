// BeltRingRootOrienter.cs
// 円環検索で拾った段グループの「根元の端」を、メッシュ側の目印から決める。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【なぜ要るか】
//   円環検索（BeltRingDetector）は面番号の小さい四角形から歩き始め、開始 rung の
//   左を頂点番号の小さい方にする（VertexPair は V1 <= V2）。上下展開
//   （BeltStackExpander）は左側へ伸ばした段を先頭に積む。結果として
//   「先頭の段の左レール」がどちらの端になるかは頂点番号で決まり、形とは無関係。
//   揺れボーンの鎖（Stack）はこの端を根元にし、フリルの 2 プロファイルは
//   この端を A にするので、上下が人の判断（reverseChain / ProfileFlip）頼みになっていた。
//
// 【目印】優先の順に見る。
//   1. 開始タグ三角形（BeltStackDetector.TryGetMarker と同じ判定）の頂点が載っている端
//   2. 四角形以外の面（ふた）が辺で接している端
//   3. 1・2 のどちらも両端に無いとき、レールの長さが短い端
//      （フリル・スカートは付け根から先へ広がるので、付け根の縁が短い）
//   片方の端だけが当たったとき、その端を根元にする。
//   両端に目印がある・長さが同じときは並びを変えず、文言にそう書く。
//
// 【並べ替え】
//   根元が「先頭の段の左レール」になるよう、段の並びを逆にし、各段の左右を入れ替え、
//   RowIndex と FlipWinding を振り直す。rung の番号（列）は変えない。
//   逆にしても rows[r].Right と rows[r+1].Left が同じレールである関係は保たれる。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.PrimitiveMesh
{
    public static class BeltRingRootOrienter
    {
        /// <summary>
        /// 段グループごとに根元の端を決め、必要なら並びを入れ替えた新しいリストを返す。
        /// rows は BeltStackExpander.ExpandAll の結果（GroupId ごとに連続、RowIndex 昇順）。
        /// </summary>
        public static List<BeltAutoStrip> Orient(MeshObject mo, List<BeltAutoStrip> rows, out string message)
        {
            message = "";
            if (mo == null || rows == null || rows.Count == 0) return rows;

            var edgeToFaces = BeltTopology.BuildEdgeToFaces(mo);
            var vertToFaces = BeltTopology.BuildVertexToFaces(mo);

            // ── 開始タグの頂点 ──
            var tagApex = new HashSet<int>();
            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                if (!BeltTopology.IsTriangle(mo, fi)) continue;
                if (BeltStackDetector.TryGetMarker(mo, edgeToFaces, vertToFaces, fi, out int apex))
                    tagApex.Add(apex);
            }

            var result = new List<BeltAutoStrip>(rows.Count);
            var notes  = new List<string>();

            int i = 0;
            while (i < rows.Count)
            {
                // 同じ GroupId の連続をひとかたまりにする。GroupId < 0 は 1 段で 1 グループ。
                int gid = rows[i].GroupId;
                int j = i + 1;
                if (gid >= 0)
                    while (j < rows.Count && rows[j].GroupId == gid) j++;

                var group = rows.GetRange(i, j - i);
                var head  = group[0].Left;
                var tail  = group[group.Count - 1].Right;
                bool closed = group[0].Closed;

                bool tagHead = TouchesTag(head, tagApex);
                bool tagTail = TouchesTag(tail, tagApex);
                bool capHead = TouchesNonQuad(mo, edgeToFaces, head, closed);
                bool capTail = TouchesNonQuad(mo, edgeToFaces, tail, closed);

                int    root;   // 0 = 先頭のまま / 1 = 末尾が根元 / -1 = 決まらない
                string why;

                if (tagHead != tagTail)      { root = tagHead ? 0 : 1; why = "開始タグ"; }
                else if (capHead != capTail) { root = capHead ? 0 : 1; why = "ふた"; }
                else if (!capHead && !tagHead)
                {
                    // 目印が無いとき：レールの長さが短い端を根元にする。
                    // フリル・スカートは付け根から先へ広がるので、付け根の縁が短い。
                    float lenHead = RailLength(mo, head, closed);
                    float lenTail = RailLength(mo, tail, closed);
                    float diff    = lenHead - lenTail;
                    float tol     = 1e-4f * UnityEngine.Mathf.Max(lenHead, lenTail);

                    if (diff < -tol)     { root = 0;  why = "短いレール"; }
                    else if (diff > tol) { root = 1;  why = "短いレール"; }
                    else                 { root = -1; why = "目印なし・レールの長さが同じ"; }
                }
                else
                {
                    root = -1;
                    why  = "両端に目印があり決まらない";
                }

                if (root == 1)
                {
                    result.AddRange(Reverse(mo, group));
                    notes.Add($"グループ{gid}: {why}の端を根元にした（並びを反転）");
                }
                else
                {
                    result.AddRange(group);
                    notes.Add(root == 0
                        ? $"グループ{gid}: {why}の端を根元にした"
                        : $"グループ{gid}: {why}（並びは検出順のまま）");
                }

                i = j;
            }

            message = "根元: " + string.Join(" / ", notes);
            return result;
        }

        /// <summary>段の並びを逆にし、各段の左右を入れ替える。</summary>
        private static List<BeltAutoStrip> Reverse(MeshObject mo, List<BeltAutoStrip> group)
        {
            var outRows = new List<BeltAutoStrip>(group.Count);

            for (int k = group.Count - 1; k >= 0; k--)
            {
                var src = group[k];
                var st  = new BeltAutoStrip();

                st.Left .AddRange(src.Right);
                st.Right.AddRange(src.Left);
                st.Faces.AddRange(src.Faces);

                st.Closed     = src.Closed;
                st.StartPoint = src.StartPoint;
                st.EndPoint   = src.EndPoint;
                st.GroupId    = src.GroupId;
                st.RowIndex   = outRows.Count;
                st.RowCount   = src.RowCount;
                st.FlipWinding = st.Faces.Count > 0 && st.Left.Count > 0
                    ? BeltTopology.DetectFlipWinding(mo, st.Faces[0], st.Left[0], st.Right[0])
                    : src.FlipWinding;

                outRows.Add(st);
            }

            return outRows;
        }

        /// <summary>レールの長さ（閉じていれば最後と最初もつなぐ）。</summary>
        private static float RailLength(MeshObject mo, List<int> rail, bool closed)
        {
            int n = rail.Count;
            int edges = closed ? n : n - 1;
            float len = 0f;
            for (int s = 0; s < edges; s++)
            {
                int a = rail[s], b = rail[(s + 1) % n];
                if (a < 0 || b < 0 || a >= mo.VertexCount || b >= mo.VertexCount) continue;
                len += UnityEngine.Vector3.Distance(mo.Vertices[a].Position, mo.Vertices[b].Position);
            }
            return len;
        }

        private static bool TouchesTag(List<int> rail, HashSet<int> tagApex)
        {
            if (tagApex.Count == 0) return false;
            foreach (int v in rail) if (tagApex.Contains(v)) return true;
            return false;
        }

        /// <summary>レールの辺のどれかに、四角形以外の面が接しているか。</summary>
        private static bool TouchesNonQuad(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces, List<int> rail, bool closed)
        {
            int n = rail.Count;
            int edges = closed ? n : n - 1;

            for (int s = 0; s < edges; s++)
            {
                var e = new VertexPair(rail[s], rail[(s + 1) % n]);
                if (!e.IsValid) continue;
                if (!edgeToFaces.TryGetValue(e, out var faces)) continue;

                foreach (int f in faces)
                {
                    var v = mo.Faces[f]?.VertexIndices;
                    if (v != null && v.Count != 4) return true;
                }
            }
            return false;
        }
    }
}
