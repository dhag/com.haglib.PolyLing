// Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/BeltStripExtractor.cs
// 選択された四角形群から「梯子状ベルト」を順序付きで抽出する。
// 走査方式は BeltCutResolver.WalkBelt と同じ（四角形の対辺を辿り、アンカー頂点の伝播で左右を一貫させる）。
// 隣接判定は選択四角形の内部だけで行う（選択外の面へは進まない）。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.PrimitiveMesh
{
    /// <summary>
    /// 梯子状ベルトの抽出結果。
    /// rung（横木）を順序付きで保持し、Left[i] / Right[i] は同じ rung の左右レール頂点。
    /// Faces[i] は rung[i] と rung[i+1] の間の四角形（閉ループ時は最後の面が rung[n-1] と rung[0] の間）。
    /// </summary>
    public sealed class BeltStrip
    {
        public readonly List<int> Left  = new List<int>();
        public readonly List<int> Right = new List<int>();
        public readonly List<int> Faces = new List<int>();

        /// <summary>一周してつながっている（閉じた梯子）なら true。</summary>
        public bool Closed;

        /// <summary>元メッシュの巻き順が (L[i], L[i+1], R[i+1], R[i]) 側なら true。</summary>
        public bool FlipWinding;

        public bool   Ok;
        public string Message = "";

        public int RungCount => Left.Count;
    }

    /// <summary>
    /// 選択四角形群 → 順序付き梯子状ベルト の抽出ユーティリティ。
    /// </summary>
    public static class BeltStripExtractor
    {
        private const int MaxIter = 4096;

        // ================================================================
        // 抽出
        // ================================================================

        public static BeltStrip Extract(MeshObject mo, IReadOnlyCollection<int> selectedFaces)
        {
            var strip = new BeltStrip();

            if (mo == null)
            {
                strip.Message = "メッシュがありません";
                return strip;
            }

            // 選択面のうち四角形だけを対象にする。
            var quadSet = new HashSet<int>();
            if (selectedFaces != null)
            {
                foreach (int fi in selectedFaces)
                {
                    if (fi < 0 || fi >= mo.FaceCount) continue;
                    var f = mo.Faces[fi];
                    if (f == null || f.VertexIndices == null) continue;
                    if (f.VertexIndices.Count != 4) continue;
                    quadSet.Add(fi);
                }
            }

            if (quadSet.Count < 2)
            {
                strip.Message = "選択四角形が2枚以上必要です";
                return strip;
            }

            // 面インデックス昇順で走査し、辺→面マップの並びまで決定的にする。
            var order = new List<int>(quadSet);
            order.Sort();

            var edgeToFaces = BuildEdgeToFacesMap(mo, order);

            VertexPair startRung = default;
            bool found = false;
            for (int k = 0; k < order.Count && !found; k++)
            {
                var verts = mo.Faces[order[k]].VertexIndices;
                for (int i = 0; i < 4; i++)
                {
                    var pair = new VertexPair(verts[i], verts[(i + 1) % 4]);
                    if (!pair.IsValid) continue;
                    if (edgeToFaces.TryGetValue(pair, out var fs) && fs.Count >= 2)
                    {
                        startRung = pair;
                        found = true;
                        break;
                    }
                }
            }

            if (!found)
            {
                strip.Message = "四角形がつながっていません";
                return strip;
            }

            var visitedRungs = new HashSet<VertexPair> { startRung };
            var visitedFaces = new HashSet<int>();

            int startAnchor = startRung.V1;
            var startFaces  = edgeToFaces[startRung];

            // 進行方向1
            var fwdLeft  = new List<int>();
            var fwdRight = new List<int>();
            var fwdFaces = new List<int>();
            bool closed = WalkDirection(mo, quadSet, edgeToFaces, startRung, startAnchor, startFaces[0],
                                        visitedRungs, visitedFaces, fwdLeft, fwdRight, fwdFaces);

            // 進行方向2（閉ループなら不要）
            var bwdLeft  = new List<int>();
            var bwdRight = new List<int>();
            var bwdFaces = new List<int>();
            if (!closed && startFaces.Count >= 2)
            {
                WalkDirection(mo, quadSet, edgeToFaces, startRung, startAnchor, startFaces[1],
                              visitedRungs, visitedFaces, bwdLeft, bwdRight, bwdFaces);
            }

            // 逆方向を反転して連結する。
            for (int i = bwdLeft.Count - 1; i >= 0; i--)
            {
                strip.Left.Add(bwdLeft[i]);
                strip.Right.Add(bwdRight[i]);
            }
            for (int i = bwdFaces.Count - 1; i >= 0; i--)
                strip.Faces.Add(bwdFaces[i]);

            strip.Left.Add(startAnchor);
            strip.Right.Add(startRung.GetOtherVertex(startAnchor));

            for (int i = 0; i < fwdLeft.Count; i++)
            {
                strip.Left.Add(fwdLeft[i]);
                strip.Right.Add(fwdRight[i]);
            }
            for (int i = 0; i < fwdFaces.Count; i++)
                strip.Faces.Add(fwdFaces[i]);

            strip.Closed = closed;

            if (strip.RungCount < 2 || strip.Faces.Count < 1)
            {
                strip.Message = "梯子状ベルトを構成できません";
                return strip;
            }

            strip.FlipWinding = DetectFlipWinding(mo, strip);

            strip.Ok = true;
            int unused = quadSet.Count - visitedFaces.Count;
            strip.Message = unused > 0
                ? $"取り込み: rung {strip.RungCount} / 面 {strip.Faces.Count} / {(closed ? "閉" : "開")} (未使用の選択四角形 {unused})"
                : $"取り込み: rung {strip.RungCount} / 面 {strip.Faces.Count} / {(closed ? "閉" : "開")}";
            return strip;
        }

        // ================================================================
        // 走査
        // ================================================================

        /// <summary>
        /// startRung から startFaceIdx 側へ、四角形の対辺を辿って rung を集める。
        /// 開始 rung へ戻った（閉じた梯子）なら true。
        /// </summary>
        private static bool WalkDirection(
            MeshObject mo, HashSet<int> quadSet,
            Dictionary<VertexPair, List<int>> edgeToFaces,
            VertexPair startRung, int startAnchor, int startFaceIdx,
            HashSet<VertexPair> visitedRungs, HashSet<int> visitedFaces,
            List<int> left, List<int> right, List<int> faces)
        {
            VertexPair current = startRung;
            int currentAnchor  = startAnchor;
            int faceIdx        = startFaceIdx;

            for (int iter = 0; iter < MaxIter; iter++)
            {
                if (faceIdx < 0 || !quadSet.Contains(faceIdx)) return false;
                if (visitedFaces.Contains(faceIdx)) return false;

                var face = mo.Faces[faceIdx];
                var opp  = FindOppositeEdge(face, current);
                if (!opp.HasValue || !opp.Value.IsValid) return false;

                int oppAnchor = OppositeAnchor(face, current, currentAnchor);
                if (oppAnchor < 0) return false;

                visitedFaces.Add(faceIdx);
                faces.Add(faceIdx);

                if (visitedRungs.Contains(opp.Value))
                    return opp.Value == startRung;  // 一周した

                visitedRungs.Add(opp.Value);
                left.Add(oppAnchor);
                right.Add(opp.Value.GetOtherVertex(oppAnchor));

                int next = OtherFace(edgeToFaces, opp.Value, faceIdx);

                current       = opp.Value;
                currentAnchor = oppAnchor;
                faceIdx       = next;
            }

            return false;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        /// <summary>選択四角形の内部だけで 辺→面 マップを作る。</summary>
        private static Dictionary<VertexPair, List<int>> BuildEdgeToFacesMap(MeshObject mo, List<int> orderedQuads)
        {
            var map = new Dictionary<VertexPair, List<int>>();
            foreach (int fi in orderedQuads)
            {
                var verts = mo.Faces[fi].VertexIndices;
                for (int i = 0; i < 4; i++)
                {
                    var key = new VertexPair(verts[i], verts[(i + 1) % 4]);
                    if (!key.IsValid) continue;
                    if (!map.TryGetValue(key, out var list)) { list = new List<int>(); map[key] = list; }
                    list.Add(fi);
                }
            }
            return map;
        }

        /// <summary>四角形 face 内で、辺 edge の対辺を返す。</summary>
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

        /// <summary>
        /// 四角形 face 内で、既知辺 knownEdge 上の頂点 knownAnchor と側辺で接続する
        /// 対辺側の頂点を返す。取得不能なら -1。
        /// </summary>
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

        /// <summary>edge を共有する、exceptFace 以外の面を返す。なければ -1。</summary>
        private static int OtherFace(Dictionary<VertexPair, List<int>> edgeToFaces, VertexPair edge, int exceptFace)
        {
            if (!edgeToFaces.TryGetValue(edge, out var list)) return -1;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != exceptFace) return list[i];
            }
            return -1;
        }

        /// <summary>
        /// 先頭面の巻き順を調べ、(L[i], R[i], R[i+1], L[i+1]) と逆向きなら true。
        /// </summary>
        private static bool DetectFlipWinding(MeshObject mo, BeltStrip strip)
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
    }
}
