// ShapeExportView.cs
// 書き出しに渡すモデルを「基本の形」にした写し。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【正本】PolyLing_モーフ整理計画.md 3.9・段 5
//   PMX・プレファブ・VRM・glTF・FBX の書き出しは、今の形に関係なく基本の形をベースに、形を出す。
//   書き出し側は Vertex.Position を読むので、形の控えを持つ行を、基本の形へ切り替えた写しに差し替える。
//   実モデル・表示・Undo には触れない（ModifierExportView と同じ「写しを渡す」方式）。
//
// 【使い方】各書き出しの入口で、ModifierExportView.For の「後」に通す。
//   ModifierExportView は実モデルのモディファイアの並び（ModifierStacks）を読むが、
//   写し（CloneForExport）はそれを持たないので、順を逆にすると畳みが効かない。
//
// 【行の並び】変えない（書き出し側は索引で読む）。形の控えを持たない行はそのまま（両方の形で同じ位置）。
//   ミラーの対は写した行へ付け替える。
//
// 【モディファイアの結果】畳んで書く結果の行は形を持たない（3.14・計画書 6 の 6）ので、
//   今の形の位置のまま出る。基本の形で計算し直すのは段 7 の見直し（計画書 8 章の残し）。

using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    public static class ShapeExportView
    {
        /// <summary>
        /// 書き出しに渡すモデル。今の形が基本の形なら model そのもの。
        /// そうでなければ、形の控えを持つ行を基本の形にした写しを持つモデル（CurrentShapeId = 0）。
        /// </summary>
        public static ModelContext For(ModelContext model)
        {
            if (model?.MeshContextList == null) return model;
            int current = model.CurrentShapeId;
            if (current == ShapeOps.BaseShapeId) return model;

            var list = model.MeshContextList;
            var rows = new List<MeshContext>(list);
            var replaced = new Dictionary<MeshContext, MeshContext>();
            for (int i = 0; i < list.Count; i++)
            {
                var mc = list[i];
                var mo = mc?.MeshObject;
                if (mo == null || mo.VertexCount == 0 || !ShapeOps.HasAnyShapeData(mo)) continue;
                var row = MeshContextCloneOps.Clone(mc, MeshContextCloneKind.StateSnapshot);
                ShapeOps.SwitchMesh(row.MeshObject, current, ShapeOps.BaseShapeId);
                rows[i] = row;
                replaced[mc] = row;
            }

            var pairsOut = new List<MirrorPair>();
            if (model.MirrorPairs != null)
                foreach (var mp in model.MirrorPairs)
                {
                    if (mp == null) continue;
                    MeshContext real   = mp.Real   != null && replaced.TryGetValue(mp.Real,   out var r) ? r : mp.Real;
                    MeshContext mirror = mp.Mirror != null && replaced.TryGetValue(mp.Mirror, out var m) ? m : mp.Mirror;
                    pairsOut.Add(ReferenceEquals(real, mp.Real) && ReferenceEquals(mirror, mp.Mirror)
                        ? mp
                        : new MirrorPair { Real = real, Mirror = mirror, Axis = mp.Axis });
                }

            var view = model.CloneForExport(rows, pairsOut);
            view.CurrentShapeId  = ShapeOps.BaseShapeId;
            view.PreviousShapeId = ShapeOps.BaseShapeId;
            foreach (var row in replaced.Values) row.ParentModelContext = view;
            return view;
        }
    }
}
