// BeltAutoDetector.cs
// オブジェクト全体から梯子状ベルトを自動検出する。
// Runtime / Editor 共有。Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/ に配置
//
// 【検出条件】
//  開始側: 三角形が、他の三角形とは「1頂点のみ」で接し（辺の共有なし）、
//          その頂点以外は他の三角形と共有されない。この頂点を開始点とする。
//          その三角形の「開始点を含まない辺」が四角形と共有されていれば梯子の開始とみなす。
//  終了側: 四角形の連結を辿った先が三角形で、その三角形が他に何も接していない場合、
//          四角形に含まれない頂点を終了点とする。
//  開始点と終了点が両方そろったものだけを採用する。
//
// rung は四角形部分のみ。先端の三角形は rung に含めず、開始点／終了点として保持する
// （rung 長 0 の縮退 rung を作らないため）。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>自動検出した梯子1本ぶん。</summary>
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

        public int RungCount => Left.Count;
    }

    public static class BeltAutoDetector
    {
        private const int MaxIter = 4096;

        /// <summary>オブジェクト全体を検索し、条件を満たす梯子をすべて返す。</summary>
        public static List<BeltAutoStrip> Detect(MeshObject mo, out string message)
        {
            var result = new List<BeltAutoStrip>();
            message = "";

            if (mo == null || mo.FaceCount == 0)
            {
                message = "面がありません";
                return result;
            }

            var edgeToFaces = BuildEdgeToFaces(mo);
            var vertToTris  = BuildVertexToTriangles(mo);

            int startCandidates = 0;
            int dropped         = 0;
            var usedSignatures  = new HashSet<string>();

            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                var f = mo.Faces[fi];
                if (f?.VertexIndices == null || f.VertexIndices.Count != 3) continue;

                int apex = FindTriangleApex(mo, edgeToFaces, vertToTris, fi);
                if (apex < 0) continue;

                // 開始点を含まない辺（対辺）が四角形と共有されているか
                var opp = OppositeEdgeOfApex(f, apex);
                if (!opp.IsValid) continue;

                int quad = OtherFace(edgeToFaces, opp, fi);
                if (quad < 0 || !IsQuad(mo, quad)) continue;

                startCandidates++;

                var strip = WalkBelt(mo, edgeToFaces, vertToTris, opp, quad);
                if (strip == null) { dropped++; continue; }

                strip.StartPoint = apex;

                string sig = FaceSignature(strip.Faces);
                if (!usedSignatures.Add(sig)) continue;

                strip.FlipWinding = DetectFlipWinding(mo, strip);
                result.Add(strip);
            }

            message = $"自動検索: {result.Count} 本（開始候補 {startCandidates} / 終了点なしで除外 {dropped}）";
            return result;
        }

        // ================================================================
        // 開始判定
        // ================================================================

        /// <summary>
        /// 三角形 fi が「他の三角形と1頂点のみで接し、辺は共有しない」条件を満たすなら、
        /// その頂点（開始点）を返す。満たさなければ -1。
        /// </summary>
        private static int FindTriangleApex(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            Dictionary<int, List<int>> vertToTris, int fi)
        {
            var verts = mo.Faces[fi].VertexIndices;

            // 三角形どうしの辺共有があれば対象外
            for (int i = 0; i < 3; i++)
            {
                var pair = new VertexPair(verts[i], verts[(i + 1) % 3]);
                if (!pair.IsValid) continue;
                if (!edgeToFaces.TryGetValue(pair, out var fs)) continue;
                foreach (int o in fs)
                    if (o != fi && IsTriangle(mo, o)) return -1;
            }

            // 他の三角形と共有している頂点を数える
            int shared = -1;
            for (int i = 0; i < 3; i++)
            {
                int v = verts[i];
                if (!vertToTris.TryGetValue(v, out var tris)) continue;

                bool hit = false;
                foreach (int o in tris) if (o != fi) { hit = true; break; }
                if (!hit) continue;

                if (shared >= 0) return -1;   // 2頂点以上で接している
                shared = v;
            }
            return shared;
        }

        /// <summary>三角形の、apex を含まない辺。</summary>
        private static VertexPair OppositeEdgeOfApex(Face f, int apex)
        {
            var v = f.VertexIndices;
            var a = new List<int>(2);
            for (int i = 0; i < 3; i++) if (v[i] != apex) a.Add(v[i]);
            return a.Count == 2 ? new VertexPair(a[0], a[1]) : default;
        }

        // ================================================================
        // 走査
        // ================================================================

        /// <summary>開始 rung から四角形を辿り、終了三角形まで到達できたら梯子を返す。</summary>
        private static BeltAutoStrip WalkBelt(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            Dictionary<int, List<int>> vertToTris,
            VertexPair startRung, int startQuad)
        {
            var strip = new BeltAutoStrip();

            VertexPair current = startRung;
            int anchor  = startRung.V1;
            int faceIdx = startQuad;

            strip.Left.Add(anchor);
            strip.Right.Add(startRung.GetOtherVertex(anchor));

            var visitedFaces = new HashSet<int>();

            for (int iter = 0; iter < MaxIter; iter++)
            {
                if (faceIdx < 0 || !IsQuad(mo, faceIdx)) return null;
                if (!visitedFaces.Add(faceIdx)) return null;

                var face = mo.Faces[faceIdx];
                var opp  = FindOppositeEdge(face, current);
                if (!opp.HasValue || !opp.Value.IsValid) return null;

                int oppAnchor = OppositeAnchor(face, current, anchor);
                if (oppAnchor < 0) return null;

                strip.Faces.Add(faceIdx);
                strip.Left.Add(oppAnchor);
                strip.Right.Add(opp.Value.GetOtherVertex(oppAnchor));

                int next = OtherFace(edgeToFaces, opp.Value, faceIdx);
                if (next < 0) return null;

                if (IsTriangle(mo, next))
                {
                    // 終了三角形：他に何も接していないこと
                    int endPoint = EndTrianglePoint(mo, edgeToFaces, vertToTris, next, opp.Value);
                    if (endPoint < 0) return null;

                    strip.EndPoint = endPoint;
                    return strip.RungCount >= 2 ? strip : null;
                }

                current = opp.Value;
                anchor  = oppAnchor;
                faceIdx = next;
            }
            return null;
        }

        /// <summary>
        /// 終了三角形の判定。rung 以外の辺が他面と共有されておらず、
        /// rung に含まれない頂点が他の三角形と共有されていないこと。
        /// </summary>
        private static int EndTrianglePoint(MeshObject mo,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            Dictionary<int, List<int>> vertToTris,
            int triIdx, VertexPair rung)
        {
            var verts = mo.Faces[triIdx].VertexIndices;

            for (int i = 0; i < 3; i++)
            {
                var pair = new VertexPair(verts[i], verts[(i + 1) % 3]);
                if (!pair.IsValid) continue;
                if (pair == rung) continue;
                if (edgeToFaces.TryGetValue(pair, out var fs) && fs.Count > 1) return -1;
            }

            int apex = -1;
            for (int i = 0; i < 3; i++)
            {
                if (verts[i] == rung.V1 || verts[i] == rung.V2) continue;
                apex = verts[i];
            }
            if (apex < 0) return -1;

            if (vertToTris.TryGetValue(apex, out var tris))
                foreach (int o in tris) if (o != triIdx) return -1;

            return apex;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static bool IsQuad(MeshObject mo, int fi)
            => fi >= 0 && fi < mo.FaceCount && mo.Faces[fi]?.VertexIndices != null
               && mo.Faces[fi].VertexIndices.Count == 4;

        private static bool IsTriangle(MeshObject mo, int fi)
            => fi >= 0 && fi < mo.FaceCount && mo.Faces[fi]?.VertexIndices != null
               && mo.Faces[fi].VertexIndices.Count == 3;

        private static Dictionary<VertexPair, List<int>> BuildEdgeToFaces(MeshObject mo)
        {
            var map = new Dictionary<VertexPair, List<int>>();
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

        private static Dictionary<int, List<int>> BuildVertexToTriangles(MeshObject mo)
        {
            var map = new Dictionary<int, List<int>>();
            for (int fi = 0; fi < mo.FaceCount; fi++)
            {
                var v = mo.Faces[fi]?.VertexIndices;
                if (v == null || v.Count != 3) continue;
                for (int i = 0; i < 3; i++)
                {
                    if (!map.TryGetValue(v[i], out var list)) { list = new List<int>(); map[v[i]] = list; }
                    list.Add(fi);
                }
            }
            return map;
        }

        private static VertexPair? FindOppositeEdge(Face face, VertexPair edge)
        {
            var verts = face.VertexIndices;
            if (verts.Count != 4) return null;

            for (int i = 0; i < 4; i++)
            {
                int a = verts[i];
                int b = verts[(i + 1) % 4];
                if ((a == edge.V1 && b == edge.V2) || (a == edge.V2 && b == edge.V1))
                    return new VertexPair(verts[(i + 2) % 4], verts[(i + 3) % 4]);
            }
            return null;
        }

        private static int OppositeAnchor(Face face, VertexPair knownEdge, int knownAnchor)
        {
            var verts = face.VertexIndices;
            if (verts.Count != 4) return -1;

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

        private static int OtherFace(Dictionary<VertexPair, List<int>> edgeToFaces, VertexPair edge, int exceptFace)
        {
            if (!edgeToFaces.TryGetValue(edge, out var list)) return -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != exceptFace) return list[i];
            return -1;
        }

        private static bool DetectFlipWinding(MeshObject mo, BeltAutoStrip strip)
        {
            var verts = mo.Faces[strip.Faces[0]].VertexIndices;
            int l0 = strip.Left[0];
            int r0 = strip.Right[0];

            for (int i = 0; i < verts.Count; i++)
            {
                if (verts[i] != l0) continue;
                return verts[(i + 1) % verts.Count] != r0;
            }
            return false;
        }

        private static string FaceSignature(List<int> faces)
        {
            var sorted = new List<int>(faces);
            sorted.Sort();
            return string.Join(",", sorted);
        }
    }
}
