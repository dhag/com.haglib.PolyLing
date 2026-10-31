// SelectionDuplicateOps.cs
// 選択している頂点・辺・線分・面だけを写した、別オブジェクトの MeshContext を作る。
// モデルへの追加と Undo は呼び出し側（PlayerCommandDispatcher の duplicateSelection）が持つ。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【何を写すか】（Extract が正典。選択部分のコピー（SelectionClipboardOps）も同じ範囲を使う）
//   ・選択された面（Selection.Faces）と線分（Selection.Lines）… 面そのものと、面が使う頂点。
//     頂点 2 未満の面は写さない。
//   ・選択された辺（Selection.Edges）
//       edgesAsLines = true  … その辺を含む写す面（頂点 3 以上）が無く、同じ 2 頂点の
//                                写す線分も無いときだけ、新しい線分（2 頂点の面）にする。
//       edgesAsLines = false … 両端の頂点だけを写す。
//   ・選択された頂点（Selection.Vertices）… 写す。どの面にも入らなければ単独の頂点になる。
//
// 【頂点の並び】
//   元の番号の小さい順に並べる。MorphBaseData.RemoveVertices が「残った頂点を元の順に
//   詰める」形なので、この並びにしておけばモーフ基準データをそのまま間引ける。
//
// 【オブジェクトの属性】
//   MeshContextCloneOps.Clone（NewObject）で丸ごと複製してから、頂点と面だけを差し替える。
//   変換（BoneTransform / WorldMatrix / BindPose）・種別・ミラー設定などはそのまま引き継ぐ。
//   頂点番号に結びついたもののうち、間引けないもの（MirrorBakeState /
//   NormalRecalcExcludeList / LineGroups）は空にする。
//
// 【元は書き換えない】

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Selection;

namespace Poly_Ling.Ops
{
    /// <summary>選択部分の複製 1 つぶんの結果。</summary>
    public struct SelectionDuplicateResult
    {
        public bool   Success;
        public string Reason;

        /// <summary>写した頂点の数。</summary>
        public int Vertices;

        /// <summary>写した面の数（線分を含む）。</summary>
        public int Faces;

        /// <summary>辺から新しく作った線分の数。</summary>
        public int LinesFromEdges;
    }

    /// <summary>
    /// 選択部分を写し取った頂点と面（元と同じ格納空間）。元のメッシュは書き換えない。
    /// </summary>
    public sealed class SelectionExtract
    {
        /// <summary>写した頂点（元の複製。元の番号の小さい順）。</summary>
        public List<Vertex> Vertices;

        /// <summary>写した面（元の複製。頂点番号は写し先の番号）。辺から作った線分は末尾。</summary>
        public List<Face> Faces;

        /// <summary>元の頂点番号 → 写し先の頂点番号。</summary>
        public Dictionary<int, int> VertexMap;

        /// <summary>元の面番号 → 写し先の面番号（写した面と線分だけ。辺から作った線分は含まない）。</summary>
        public Dictionary<int, int> FaceMap;

        /// <summary>辺から新しく作った線分の数。</summary>
        public int LinesFromEdges;

        /// <summary>元のメッシュの頂点数。</summary>
        public int SourceVertexCount;
    }

    /// <summary>選択部分だけを別オブジェクトへ複製する。</summary>
    public static class SelectionDuplicateOps
    {
        /// <summary>この MeshContext に写すものがあるか（頂点・辺・線分・面のどれかが選択されている）。</summary>
        public static bool HasSelection(MeshContext src)
            => src?.Selection != null && src.Selection.HasAnySelection;

        /// <summary>
        /// 選択部分の頂点と面を写し取る。写すものが無いときは null を返し、reason に理由を入れる。
        /// 写す範囲は冒頭の【何を写すか】のとおり。
        /// </summary>
        public static SelectionExtract Extract(
            MeshObject mo, SelectionState sel, bool edgesAsLines, out string reason)
        {
            reason = "";
            if (mo == null || sel == null) { reason = "メッシュがありません"; return null; }

            int n = mo.Vertices.Count;

            // ── 写す面（面と線分）
            var faceSet = new SortedSet<int>();
            foreach (int f in sel.Faces) AddFace(mo, f, faceSet);
            foreach (int f in sel.Lines) AddFace(mo, f, faceSet);

            // 写す面が持つ辺（頂点 3 以上の面の周）と、写す線分の 2 頂点。
            var coveredEdges = new HashSet<VertexPair>();
            foreach (int f in faceSet)
            {
                var vi = mo.Faces[f].VertexIndices;
                if (vi.Count == 2) { coveredEdges.Add(new VertexPair(vi[0], vi[1])); continue; }
                for (int k = 0; k < vi.Count; k++)
                    coveredEdges.Add(new VertexPair(vi[k], vi[(k + 1) % vi.Count]));
            }

            // ── 写す頂点
            var keep = new SortedSet<int>();
            foreach (int f in faceSet)
                foreach (int v in mo.Faces[f].VertexIndices) keep.Add(v);
            foreach (int v in sel.Vertices)
                if (v >= 0 && v < n) keep.Add(v);

            // ── 辺
            var newLines = new List<VertexPair>();
            foreach (var e in sel.Edges.OrderBy(p => p.V1).ThenBy(p => p.V2))
            {
                if (!e.IsValid || e.V2 >= n) continue;
                keep.Add(e.V1);
                keep.Add(e.V2);
                if (edgesAsLines && !coveredEdges.Contains(e))
                {
                    newLines.Add(e);
                    coveredEdges.Add(e);
                }
            }

            if (keep.Count == 0) { reason = "写すものが選択されていません"; return null; }

            // ── 元番号 → 新番号
            var map = new Dictionary<int, int>(keep.Count);
            var newVertices = new List<Vertex>(keep.Count);
            foreach (int v in keep)
            {
                map[v] = newVertices.Count;
                newVertices.Add(mo.Vertices[v].Clone());
            }

            var faceMap  = new Dictionary<int, int>(faceSet.Count);
            var newFaces = new List<Face>(faceSet.Count + newLines.Count);
            foreach (int f in faceSet)
            {
                var nf = mo.Faces[f].Clone();
                for (int k = 0; k < nf.VertexIndices.Count; k++)
                    nf.VertexIndices[k] = map[nf.VertexIndices[k]];
                faceMap[f] = newFaces.Count;
                newFaces.Add(nf);
            }
            foreach (var e in newLines)
                newFaces.Add(NewLineFace(map[e.V1], map[e.V2]));

            return new SelectionExtract
            {
                Vertices          = newVertices,
                Faces             = newFaces,
                VertexMap         = map,
                FaceMap           = faceMap,
                LinesFromEdges    = newLines.Count,
                SourceVertexCount = n,
            };
        }

        /// <summary>
        /// 選択部分だけを持つ新しい MeshContext を作る。ObjectId は持たない（model.Add が振る）。
        /// 写すものが無いときは null を返し、result.Reason に理由を入れる。
        /// assignMissingIds が false のときは頂点 ID・面 ID の欠けを埋めない。
        /// </summary>
        public static MeshContext Duplicate(
            MeshContext src, bool edgesAsLines, string newName, out SelectionDuplicateResult result,
            bool assignMissingIds = true)
        {
            result = new SelectionDuplicateResult { Success = false, Reason = "" };

            var mo  = src?.MeshObject;
            var sel = src?.Selection;
            var ex  = Extract(mo, sel, edgesAsLines, out string reason);
            if (ex == null) { result.Reason = reason; return null; }

            int n = ex.SourceVertexCount;
            var map = ex.VertexMap;
            var newVertices = ex.Vertices;
            var newFaces    = ex.Faces;

            // ── 丸ごと複製してから頂点と面を差し替える
            var dup = MeshContextCloneOps.Clone(src, MeshContextCloneKind.NewObject, newName);
            if (dup?.MeshObject == null) { result.Reason = "複製に失敗しました"; return null; }

            var dmo = dup.MeshObject;
            dmo.Vertices = newVertices;
            dmo.Faces    = newFaces;
            dmo.MirrorBakeState         = null;
            dmo.NormalRecalcExcludeList = new List<PartsSelectionSet>();
            dmo.LineGroups              = new List<LineGroup>();
            dmo.RebuildIdSets();
            if (assignMissingIds) dmo.AssignMissingIds();

            // 頂点番号に結びついた配列を、残した頂点に合わせて間引く。
            var removed = new HashSet<int>();
            for (int i = 0; i < n; i++) if (!map.ContainsKey(i)) removed.Add(i);

            if (dup.MorphBaseData != null && removed.Count > 0)
                dup.MorphBaseData.RemoveVertices(removed);

            if (dup.OriginalPositions != null)
            {
                if (dup.OriginalPositions.Length == n)
                {
                    var op = new Vector3[map.Count];
                    foreach (var kv in map) op[kv.Value] = dup.OriginalPositions[kv.Key];
                    dup.OriginalPositions = op;
                }
                else
                {
                    dup.OriginalPositions = newVertices.Select(v => v.Position).ToArray();
                }
            }

            result = new SelectionDuplicateResult
            {
                Success        = true,
                Reason         = "",
                Vertices       = newVertices.Count,
                Faces          = newFaces.Count,
                LinesFromEdges = ex.LinesFromEdges,
            };
            return dup;
        }

        private static void AddFace(MeshObject mo, int f, SortedSet<int> into)
        {
            if (f < 0 || f >= mo.Faces.Count) return;
            var face = mo.Faces[f];
            if (face?.VertexIndices == null || face.VertexIndices.Count < 2) return;
            into.Add(f);
        }

        /// <summary>2 頂点の面（線分）。LineGroupEditOps.NewLineFace と同じ形。</summary>
        private static Face NewLineFace(int a, int b)
        {
            var f = new Face { MaterialIndex = 0 };
            f.VertexIndices.Add(a); f.UVIndices.Add(0); f.NormalIndices.Add(0);
            f.VertexIndices.Add(b); f.UVIndices.Add(0); f.NormalIndices.Add(0);
            return f;
        }
    }
}
