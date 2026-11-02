// ThicknessMeshModifier.cs
// 処理列の種類「厚み」。範囲の面に厚みを付けた立体に置き換える。実処理は FaceGroupSolidifier.Build。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 段 6
//
// 【種類名】thickness。solidify は SolidifyCommand の action 名と重なるので使えない
//   （登録表が衝突として外す）。
//
// 【対象の面】
//   全体 … 3 頂点以上の全面
//   それ以外 … 範囲に面があればその面。面が無ければ、全頂点が範囲に入る面。
//   2 頂点の面（線分）は対象にしない。対象が 0 面なら失敗。
//
// 【出力】入力 1 つ → 出力メッシュ 1 つ
//   ・対象の面を外し、残りの面はそのまま通す
//   ・頂点は、対象の面だけが使っていたものを外す。残る面・LineGroup が使う頂点と、
//     入力で孤立していた頂点は残す。残す頂点の索引を詰め、LineGroup も付け替える
//   ・Build の立体を末尾に足す
//
// 【ID】自動では振らない（learnings：頂点 ID・面 ID の自動付与は禁止）。
//   Build は内部の AddVertex で新しい ID を振るので、立体の頂点の ID は元の頂点の ID に書き換え
//   （SourceToVertices）、殻の面の ID は元の面の ID（FaceSources）、側面は 0（未設定）にする。
//   こうしておくと、下流の項目の範囲（頂点 ID）が厚みの後でも同じ頂点の写しに当たる。

using System.Collections.Generic;
using Poly_Ling.Data;
using Poly_Ling.Tools;

namespace Poly_Ling.Modifiers
{
    [PLMeshModifier("thickness", DisplayName = "厚み", Category = "topology",
                    Change = MeshModifierChange.Topology,
                    Description = "範囲の面を、厚みを付けた閉じた立体に置き換える")]
    public sealed class ThicknessMeshModifier : IMeshModifier
    {
        [PLParam(Description = "厚み（総厚。表・裏へ半分ずつ）", LimitKey = "Solidify.Thickness", Step = 0.001)]
        public float Thickness { get; set; } = 0.1f;

        [PLParam(Description = "表側の角の分割数（0 で角処理なし、1 で面取り、2 以上で丸め）", LimitKey = "Solidify.Segments", Step = 1)]
        public int SegmentsFront { get; set; } = 0;

        [PLParam(Description = "裏側の角の分割数（0 で角処理なし、1 で面取り、2 以上で丸め）", LimitKey = "Solidify.Segments", Step = 1)]
        public int SegmentsBack { get; set; } = 0;

        [PLParam(Description = "表側の角の大きさ", LimitKey = "Solidify.EdgeSize", Step = 0.001)]
        public float EdgeSizeFront { get; set; } = 0.02f;

        [PLParam(Description = "裏側の角の大きさ", LimitKey = "Solidify.EdgeSize", Step = 0.001)]
        public float EdgeSizeBack { get; set; } = 0.02f;

        [PLParam(Description = "丸めを内側へ寄せる（位置は変わらない）")]
        public bool EdgeInward { get; set; } = false;

        public MeshObject Evaluate(MeshModifierInput input, out string error)
        {
            error = null;
            var src = input?.Source;
            if (src == null) { error = "入力のメッシュが無い"; return null; }
            var scope = input.Scope ?? ResolvedMeshModifierScope.CreateAll();

            // ── 対象の面 ───────────────────────────────────────────
            var targets = new List<int>();
            bool byFaces = !scope.IsAll && scope.Faces.Count > 0;
            for (int fi = 0; fi < src.Faces.Count; fi++)
            {
                var f = src.Faces[fi];
                if (f == null || f.VertexCount < 3) continue;
                if (scope.IsAll) { targets.Add(fi); continue; }
                if (byFaces) { if (scope.Faces.Contains(fi)) targets.Add(fi); continue; }
                bool inside = true;
                foreach (int vi in f.VertexIndices)
                    if (!scope.AffectedVertices.Contains(vi)) { inside = false; break; }
                if (inside) targets.Add(fi);
            }
            if (targets.Count == 0) { error = "範囲に 3 頂点以上の面が無い"; return null; }

            // ── 立体 ───────────────────────────────────────────────
            var p = new FaceGroupSolidifier.Params
            {
                Thickness     = Thickness,
                SegmentsFront = SegmentsFront,
                SegmentsBack  = SegmentsBack,
                EdgeSizeFront = EdgeSizeFront,
                EdgeSizeBack  = EdgeSizeBack,
                EdgeInward    = EdgeInward,
            };
            var r = FaceGroupSolidifier.Build(src, targets, p, src.Name);
            if (!r.Ok) { error = r.Error ?? "厚みを付けられない"; return null; }

            // ── 残す頂点 ───────────────────────────────────────────
            var isTarget = new bool[src.Faces.Count];
            foreach (int fi in targets) isTarget[fi] = true;

            int vc = src.Vertices.Count;
            var usedAny  = new bool[vc];
            var keep     = new bool[vc];
            for (int fi = 0; fi < src.Faces.Count; fi++)
            {
                var f = src.Faces[fi];
                if (f == null) continue;
                foreach (int vi in f.VertexIndices)
                {
                    if (vi < 0 || vi >= vc) continue;
                    usedAny[vi] = true;
                    if (!isTarget[fi]) keep[vi] = true;
                }
            }
            for (int i = 0; i < vc; i++) if (!usedAny[i]) keep[i] = true;   // 入力で孤立していた頂点
            if (src.LineGroups != null)
                foreach (var lg in src.LineGroups)
                {
                    if (lg == null) continue;
                    if (lg.Order != null) foreach (int vi in lg.Order) if (vi >= 0 && vi < vc) keep[vi] = true;
                    if (lg.ParentVertex >= 0 && lg.ParentVertex < vc) keep[lg.ParentVertex] = true;
                }

            // ── 出力を組む ─────────────────────────────────────────
            var dst = src.Clone();
            dst.Vertices.Clear();
            dst.Faces.Clear();

            var remap = new int[vc];
            for (int i = 0; i < vc; i++)
            {
                if (!keep[i]) { remap[i] = -1; continue; }
                remap[i] = dst.Vertices.Count;
                dst.AddVertexRaw(src.Vertices[i].Clone());
            }
            for (int fi = 0; fi < src.Faces.Count; fi++)
            {
                if (isTarget[fi]) continue;
                var f = src.Faces[fi].Clone();
                for (int k = 0; k < f.VertexIndices.Count; k++) f.VertexIndices[k] = remap[f.VertexIndices[k]];
                dst.Faces.Add(f);
            }
            var lgs = new List<LineGroup>();
            if (src.LineGroups != null)
                foreach (var lg0 in src.LineGroups)
                {
                    if (lg0 == null) continue;
                    var lg = lg0.Clone();
                    if (lg.Order != null)
                        for (int k = 0; k < lg.Order.Count; k++)
                            if (lg.Order[k] >= 0 && lg.Order[k] < vc) lg.Order[k] = remap[lg.Order[k]];
                    if (lg.ParentVertex >= 0 && lg.ParentVertex < vc) lg.ParentVertex = remap[lg.ParentVertex];
                    lgs.Add(lg);
                }
            dst.LineGroups = lgs;

            // 立体の頂点 ID を元の頂点 ID にそろえる（Build が振った ID は使わない）
            var solid = r.Mesh;
            var idOf = new int[solid.Vertices.Count];
            if (r.SourceToVertices != null)
                for (int si = 0; si < r.SourceToVertices.Length; si++)
                {
                    var outs = r.SourceToVertices[si];
                    if (outs == null) continue;
                    foreach (int o in outs) if (o >= 0 && o < idOf.Length) idOf[o] = src.Vertices[si].Id;
                }

            int offset = dst.Vertices.Count;
            for (int i = 0; i < solid.Vertices.Count; i++)
            {
                var v = solid.Vertices[i];
                v.Id = idOf[i];
                dst.AddVertexRaw(v);
            }
            for (int fi = 0; fi < solid.Faces.Count; fi++)
            {
                var f = solid.Faces[fi];
                for (int k = 0; k < f.VertexIndices.Count; k++) f.VertexIndices[k] += offset;
                int from = r.FaceSources != null && fi < r.FaceSources.Length ? r.FaceSources[fi] : -1;
                f.Id = from >= 0 ? src.Faces[from].Id : 0;
                dst.Faces.Add(f);
            }

            dst.RebuildIdSets();
            dst.InvalidatePositionCache();
            return dst;
        }
    }
}
