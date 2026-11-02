// ArrayMeshModifier.cs
// 処理列の種類「配列」。範囲の面を一定間隔で並べて写す。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 段 7a（新規。既存の破壊的コマンドは無い）
//
// 【間隔】k 個目の写し（k = 1 … count-1）は k ×（relativeOffset × 対象の外接箱の大きさ（成分ごと）＋ constantOffset）
//   だけずらす。count は元を含む数（1 なら何もしない）。
//
// 【対象】全体 … 全頂点・全面・LineGroup を写す。
//   それ以外 … 範囲に面があればその面、面が無ければ全頂点が範囲に入る面（厚みと同じ規則）と、その頂点。
//   外接箱は対象の頂点から取る。元はそのまま残し、写しを末尾に足す。対象が無ければ失敗。
//
// 【ID】自動では振らない。写しの頂点・面は元と同じ ID（重複になる。作成ガイド 5 節）。

using System.Collections.Generic;
using Poly_Ling.Data;
using UnityEngine;

namespace Poly_Ling.Modifiers
{
    [PLMeshModifier("array", DisplayName = "配列", Category = "topology",
                    Change = MeshModifierChange.Topology,
                    Description = "範囲の面を一定間隔で並べて写す（個数は元を含む）")]
    public sealed class ArrayMeshModifier : IMeshModifier
    {
        [PLParam(Description = "個数（元を含む）", Min = 1, Max = 64, Step = 1)]
        public int Count { get; set; } = 2;

        [PLParam(Description = "相対の間隔（対象の外接箱の大きさに対する倍率）", Min = -10f, Max = 10f, Step = 0.01)]
        public Vector3 RelativeOffset { get; set; } = new Vector3(1f, 0f, 0f);

        [PLParam(Description = "一定の間隔（ローカル座標）", Min = -10f, Max = 10f, Step = 0.01)]
        public Vector3 ConstantOffset { get; set; } = Vector3.zero;

        public MeshObject Evaluate(MeshModifierInput input, out string error)
        {
            error = null;
            var src = input?.Source;
            if (src == null) { error = "入力のメッシュが無い"; return null; }
            var scope = input.Scope ?? ResolvedMeshModifierScope.CreateAll();

            int vc = src.Vertices.Count;

            // ── 対象の面と頂点 ─────────────────────────────────────
            var faces = new List<int>();
            var useV = new bool[vc];
            if (scope.IsAll)
            {
                for (int fi = 0; fi < src.Faces.Count; fi++) if (src.Faces[fi] != null) faces.Add(fi);
                for (int i = 0; i < vc; i++) useV[i] = true;
            }
            else
            {
                bool byFaces = scope.Faces.Count > 0;
                for (int fi = 0; fi < src.Faces.Count; fi++)
                {
                    var f = src.Faces[fi];
                    if (f == null) continue;
                    if (byFaces) { if (scope.Faces.Contains(fi)) faces.Add(fi); continue; }
                    bool inside = f.VertexIndices.Count > 0;
                    foreach (int vi in f.VertexIndices)
                        if (!scope.AffectedVertices.Contains(vi)) { inside = false; break; }
                    if (inside) faces.Add(fi);
                }
                foreach (int fi in faces)
                    foreach (int vi in src.Faces[fi].VertexIndices)
                        if (vi >= 0 && vi < vc) useV[vi] = true;
            }

            var verts = new List<int>();
            for (int i = 0; i < vc; i++) if (useV[i]) verts.Add(i);
            if (verts.Count == 0) { error = "範囲に写す面が無い"; return null; }

            var dst = src.Clone();
            if (Count <= 1) return dst;

            // ── 間隔 ───────────────────────────────────────────────
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (int i in verts)
            {
                var p = src.Vertices[i].Position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            Vector3 size = max - min;
            Vector3 step = Vector3.Scale(RelativeOffset, size) + ConstantOffset;

            // ── 写しを足す ─────────────────────────────────────────
            var remap = new int[vc];
            for (int k = 1; k < Count; k++)
            {
                Vector3 d = step * k;
                for (int i = 0; i < vc; i++) remap[i] = -1;
                foreach (int i in verts)
                {
                    var v = src.Vertices[i].Clone();
                    v.Position += d;
                    remap[i] = dst.AddVertexRaw(v);
                }
                foreach (int fi in faces)
                {
                    var f = src.Faces[fi].Clone();
                    for (int c = 0; c < f.VertexIndices.Count; c++) f.VertexIndices[c] = remap[f.VertexIndices[c]];
                    dst.Faces.Add(f);
                }
                if (scope.IsAll && src.LineGroups != null)
                {
                    foreach (var lg0 in src.LineGroups)
                    {
                        if (lg0 == null) continue;
                        var lg = lg0.Clone();
                        if (lg.Order != null)
                            for (int c = 0; c < lg.Order.Count; c++)
                                if (lg.Order[c] >= 0 && lg.Order[c] < vc) lg.Order[c] = remap[lg.Order[c]];
                        if (lg.ParentVertex >= 0 && lg.ParentVertex < vc) lg.ParentVertex = remap[lg.ParentVertex];
                        dst.LineGroups.Add(lg);
                    }
                }
            }

            dst.RebuildIdSets();
            dst.InvalidatePositionCache();
            return dst;
        }
    }
}
