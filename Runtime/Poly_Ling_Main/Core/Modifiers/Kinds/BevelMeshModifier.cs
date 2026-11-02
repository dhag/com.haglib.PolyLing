// BevelMeshModifier.cs
// 処理列の種類「ベベル」。範囲の辺をベベルする。実処理は EdgeBevelOps.Execute（ベベルツールと共用）。
// Runtime/Poly_Ling_Main/Core/Modifiers/Kinds/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 段 7a
//
// 【対象の辺】範囲の辺（保存は頂点 ID の組。項目の入力へ引き直したもの）を (V1, V2) の順に並べて渡す。
//   全体の範囲・辺の無い範囲は失敗（全辺を勝手にベベルしない）。
//   辺は集まったが隣の面が 2 枚そろわずベベルできないときも失敗。
//
// 【出力】入力の Clone を EdgeBevelOps がその場で書き換えたもの。範囲の外の面はそのまま残る。
//   Clone はイベントを写さないので、孤立頂点の除去で選択・パーツ選択辞書が付け替わることはない。
//
// 【ID】自動では振らない。新しい頂点には EdgeBevelOps.Result.SourceOf が指す元の頂点の ID を写す
//   （1 つの頂点から複数作るので同じ ID が複数になる）。書き換えた面は元の面の ID のまま、
//   足した面は 0（未設定）。

using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Data;
using Poly_Ling.Ops;
using Poly_Ling.Selection;

namespace Poly_Ling.Modifiers
{
    [PLMeshModifier("bevel", DisplayName = "ベベル", Category = "topology",
                    Change = MeshModifierChange.Topology,
                    Description = "範囲の辺をベベル（面取り）する。範囲に辺が要る")]
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

        public MeshObject Evaluate(MeshModifierInput input, out string error)
        {
            error = null;
            var src = input?.Source;
            if (src == null) { error = "入力のメッシュが無い"; return null; }
            var scope = input.Scope ?? ResolvedMeshModifierScope.CreateAll();
            if (scope.IsAll) { error = "ベベルする辺を範囲で指定する（全体は受けない）"; return null; }
            if (scope.Edges.Count == 0) { error = "範囲に辺が無い"; return null; }

            var edges = scope.Edges.OrderBy(e => e.V1).ThenBy(e => e.V2).ToList();

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
    }
}
