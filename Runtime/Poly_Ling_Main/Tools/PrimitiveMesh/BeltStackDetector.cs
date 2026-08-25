// BeltStackDetector.cs
// オブジェクト全体から梯子状ベルトを検出する。起点は「開始タグ三角形」で明示する。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【開始タグ三角形】辺を1本も他面と共有せず、他面と共有する頂点がちょうど1個だけの三角形。
//   その共有頂点 P が梯子の起点になる。モデル側に手で付けておく目印で、梯子には含めない。
//   球のてっぺんのように先端が三角形の扇になっている形でも、P が一意に決まる。
//
// 【先端三角形】P に接する三角形（開始タグ自身を除く）。面インデックス昇順で列挙する。
//   先端三角形の「P を含まない辺」が最初の rung、その辺の先の四角形が最初のステップ。
//   辺の共有本数は問わない（先端どうしが辺を共有する形も通す）。
//
// 【縦走査】四角形の対辺を辿る。終端は次のいずれか。
//   - 相手面が無い（自由辺）
//   - 相手面が三角形（先端に到達。rung に含めず終了点として保持する）
//   - 一周して最初の rung へ戻った（閉じた梯子）
//
// 【上下展開】得た梯子を基準段として BeltStackExpander で左右レール側へ横断し、
//   段グループにまとめる。段の並びがそのまま断面プロファイル A→B の補間順になる。
//
// 【未探索チェック】確定した段の構成面と、段の両端に接する三角形を消費済みにする。
//   同じ梯子を指す別の先端三角形は、これで二重に採られない。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>検出した梯子1本ぶん。</summary>
    public sealed class BeltAutoStrip
    {
        public readonly List<int> Left  = new List<int>();
        public readonly List<int> Right = new List<int>();
        public readonly List<int> Faces = new List<int>();

        public bool FlipWinding;
        public int  StartPoint = -1;
        public int  EndPoint   = -1;

        /// <summary>一周してつながっている（閉じた梯子）なら true。</summary>
        public bool Closed;

        /// <summary>上下につながった段グループの識別子。未設定は -1。</summary>
        public int GroupId = -1;

        /// <summary>グループ内での段番号（0 が t=0 側）。</summary>
        public int RowIndex;

        /// <summary>グループの段数。</summary>
        public int RowCount = 1;

        public int RungCount => Left.Count;
    }

    public static class BeltStackDetector
    {
        private const int MaxIter = 4096;

        /// <summary>
        /// オブジェクト全体を検索する。
        /// crossRows = true のとき、見つけた梯子から上下（左右レール側）へ横断して段を足す。
        /// </summary>
        public static List<BeltAutoStrip> Detect(MeshObject mo, bool crossRows, out string message)
        {
            var result = new List<BeltAutoStrip>();
            message = "";

            if (mo == null || mo.FaceCount == 0)
            {
                message = "面がありません";
                return result;
            }

            var edgeToFaces = BeltTopology.BuildEdgeToFaces(mo);
            var vertToFaces = BeltTopology.BuildVertexToFaces(mo);

            // ── 開始タグ三角形の一覧 ──
            var markers   = new List<MarkerRec>();
            var markerSet = new HashSet<int>();

            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                if (!BeltTopology.IsTriangle(mo, fi)) continue;
                if (!TryGetMarker(mo, edgeToFaces, vertToFaces, fi, out int apex)) continue;

                markers.Add(new MarkerRec { Face = fi, Apex = apex });
                markerSet.Add(fi);
            }

            if (markers.Count == 0)
            {
                message = "開始タグ（辺を共有せず頂点1個だけで接する三角形）が見つかりません";
                return result;
            }

            // 開始タグ自身は梯子に含めない。
            var consumed = new HashSet<int>(markerSet);

            int groupCount = 0;
            int tipCount   = 0;
            int dropped    = 0;

            for (int mi = 0; mi < markers.Count; mi++)
            {
                var mk = markers[mi];

                // ── 開始タグに接する三角形（＝先端）の一覧 ──
                var tips = new List<int>();
                if (vertToFaces.TryGetValue(mk.Apex, out var incident))
                {
                    foreach (int f in incident)
                    {
                        if (f == mk.Face) continue;
                        if (markerSet.Contains(f)) continue;
                        if (!BeltTopology.IsTriangle(mo, f)) continue;
                        tips.Add(f);
                    }
                }
                tips.Sort();
                tipCount += tips.Count;

                for (int ti = 0; ti < tips.Count; ti++)
                {
                    int tip = tips[ti];
                    if (consumed.Contains(tip)) continue;   // 別の段として探索済み

                    if (!TryGetStartRung(mo, tip, mk.Apex, out var rung)) { dropped++; continue; }

                    int quad = BeltTopology.OtherFace(edgeToFaces, rung, tip);
                    if (quad < 0 || !BeltTopology.IsQuad(mo, quad) || consumed.Contains(quad))
                    {
                        dropped++;
                        continue;
                    }

                    var baseRow = Walk(mo, edgeToFaces, consumed, rung, quad, mk.Apex);
                    if (baseRow == null) { dropped++; continue; }

                    var rows = crossRows
                        ? BeltStackExpander.Expand(mo, edgeToFaces, baseRow, consumed, out _)
                        : new List<BeltAutoStrip> { baseRow };

                    BeltStackExpander.AssignGroup(rows, groupCount++, consumed);

                    for (int r = 0; r < rows.Count; r++)
                        ConsumeEndTriangles(mo, edgeToFaces, rows[r], consumed);

                    result.AddRange(rows);
                }
            }

            message = $"自動検索: グループ {groupCount} / 段 {result.Count}" +
                      $"（開始タグ {markers.Count} / 先端候補 {tipCount} / 不成立 {dropped}）";
            return result;
        }

        // ================================================================
        // 起点
        // ================================================================

        private struct MarkerRec
        {
            public int Face;
            public int Apex;
        }

        /// <summary>
        /// 三角形 fi が開始タグかを判定する。
        /// 条件は「3辺とも他面と非共有」かつ「他面と共有する頂点がちょうど1個」。
        /// </summary>
        private static bool TryGetMarker(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            Dictionary<int, List<int>> vertToFaces,
            int fi, out int apex)
        {
            apex = -1;

            var verts = mo.Faces[fi].VertexIndices;

            for (int i = 0; i < 3; i++)
            {
                var e = new VertexPair(verts[i], verts[(i + 1) % 3]);
                if (!e.IsValid) return false;
                if (BeltTopology.OtherFace(edgeToFaces, e, fi) >= 0) return false;   // 辺を共有している
            }

            int shared = 0;
            for (int i = 0; i < 3; i++)
            {
                if (!vertToFaces.TryGetValue(verts[i], out var incident)) continue;

                bool hasOther = false;
                for (int k = 0; k < incident.Count; k++)
                    if (incident[k] != fi) { hasOther = true; break; }

                if (!hasOther) continue;

                shared++;
                if (shared > 1) return false;
                apex = verts[i];
            }

            return shared == 1 && apex >= 0;
        }

        /// <summary>先端三角形の「起点 P を含まない辺」＝最初の rung。</summary>
        private static bool TryGetStartRung(MeshObject mo, int tip, int apex, out VertexPair rung)
        {
            rung = default;

            var verts = mo.Faces[tip].VertexIndices;
            int a = -1, b = -1;

            for (int i = 0; i < 3; i++)
            {
                if (verts[i] == apex) continue;
                if (a < 0) a = verts[i];
                else if (b < 0) b = verts[i];
                else return false;
            }

            if (a < 0 || b < 0) return false;

            rung = new VertexPair(a, b);
            return rung.IsValid;
        }

        // ================================================================
        // 縦走査
        // ================================================================

        /// <summary>
        /// 開始 rung から四角形の対辺を辿る。自由辺・三角形・一周のいずれかで終了する。
        /// 消費済みの面に当たった場合は不成立にする。
        /// </summary>
        private static BeltAutoStrip Walk(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            HashSet<int> consumed,
            VertexPair startRung, int startQuad, int apex)
        {
            var strip = new BeltAutoStrip { StartPoint = apex };

            VertexPair current = startRung;
            int anchor  = startRung.V1;
            int faceIdx = startQuad;

            strip.Left .Add(anchor);
            strip.Right.Add(startRung.GetOtherVertex(anchor));

            var visited = new HashSet<int>();

            for (int iter = 0; iter < MaxIter; iter++)
            {
                if (!BeltTopology.IsQuad(mo, faceIdx)) return null;
                if (consumed.Contains(faceIdx)) return null;
                if (!visited.Add(faceIdx)) return null;

                var face = mo.Faces[faceIdx];
                var opp  = BeltTopology.FindOppositeEdge(face, current);
                if (!opp.HasValue || !opp.Value.IsValid) return null;

                int oppAnchor = BeltTopology.OppositeAnchor(face, current, anchor);
                if (oppAnchor < 0) return null;

                strip.Faces.Add(faceIdx);

                // 一周して最初の rung へ戻った。rung を重複させずに閉じる。
                if (opp.Value == startRung)
                {
                    strip.Closed = true;
                    return Finish(mo, strip, 3);
                }

                strip.Left .Add(oppAnchor);
                strip.Right.Add(opp.Value.GetOtherVertex(oppAnchor));

                int next = BeltTopology.OtherFace(edgeToFaces, opp.Value, faceIdx);

                if (next < 0) return Finish(mo, strip, 2);   // 自由辺で終了

                if (BeltTopology.IsTriangle(mo, next))
                {
                    // 先端三角形に到達。rung に含めず、終了点だけ拾う。
                    strip.EndPoint = ApexOf(mo, next, opp.Value);
                    return Finish(mo, strip, 2);
                }

                if (!BeltTopology.IsQuad(mo, next)) return Finish(mo, strip, 2);

                current = opp.Value;
                anchor  = oppAnchor;
                faceIdx = next;
            }

            return null;
        }

        private static BeltAutoStrip Finish(MeshObject mo, BeltAutoStrip strip, int minRung)
        {
            if (strip.RungCount < minRung) return null;
            if (strip.Faces.Count < 1) return null;

            strip.FlipWinding = BeltTopology.DetectFlipWinding(mo, strip.Faces[0], strip.Left[0], strip.Right[0]);
            return strip;
        }

        /// <summary>三角形 fi のうち、辺 edge に含まれない頂点。</summary>
        private static int ApexOf(MeshObject mo, int fi, VertexPair edge)
        {
            var verts = mo.Faces[fi].VertexIndices;
            for (int i = 0; i < verts.Count; i++)
                if (verts[i] != edge.V1 && verts[i] != edge.V2) return verts[i];
            return -1;
        }

        /// <summary>
        /// 段の両端 rung の先に三角形があれば消費済みにする。
        /// 横断で作った段の先端は Faces に入らないため、ここで潰さないと同じ梯子が二重に採られる。
        /// </summary>
        private static void ConsumeEndTriangles(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces, BeltAutoStrip row, HashSet<int> consumed)
        {
            if (row == null || row.Closed) return;
            if (row.Faces.Count < 1 || row.RungCount < 2) return;

            ConsumeTriangleAt(mo, edgeToFaces, row.Left[0], row.Right[0], row.Faces[0], consumed);

            int last = row.RungCount - 1;
            ConsumeTriangleAt(mo, edgeToFaces,
                row.Left[last], row.Right[last], row.Faces[row.Faces.Count - 1], consumed);
        }

        private static void ConsumeTriangleAt(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            int a, int b, int exceptFace, HashSet<int> consumed)
        {
            var e = new VertexPair(a, b);
            if (!e.IsValid) return;

            int f = BeltTopology.OtherFace(edgeToFaces, e, exceptFace);
            if (f < 0) return;
            if (!BeltTopology.IsTriangle(mo, f)) return;

            consumed.Add(f);
        }
    }
}
