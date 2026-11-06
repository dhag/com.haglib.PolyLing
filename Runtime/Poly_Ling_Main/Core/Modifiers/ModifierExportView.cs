// ModifierExportView.cs
// 書き出しに渡すモデルを作る。元と結果を 1 つのオブジェクトとして書くための写し。
// Runtime/Poly_Ling_Main/Core/Modifiers/ に配置
//
// 【正本】PolyLing_モディファイア操作統合計画.md 段 2
//   Blender の Apply Modifiers に合わせる。
//     適用する（既定）… 計算後のメッシュを元の名前で書く
//     適用しない       … 元のメッシュだけを元の名前で書く
//   どちらでも畳まれた結果の行は書かない。OBJ・MQO・PMX・FBX・glTF・STL・VRM・プレファブの全部がここを通る。
//   畳むのは ModifierResultOps.CollectFolds の 1 本道だけ。並列・分岐の元と結果は別々のオブジェクトなので、
//   設定によらずそのまま全部書く（消さない）。
//
// 【なぜ写しか】
//   書き出し側は MeshContextList を索引で読む（ボーンウェイト・親・モーフ・Humanoid の割当が索引で指す）。
//   行を抜くと索引がずれ、付け替えは共有している頂点を書き換えることになる。
//   そこで行の並びと数を変えず、元の行を焼いた写しへ、結果の行を「穴」へ差し替える。
//   実モデルには触らない。モディファイアの無いモデルは model をそのまま返すので、今までの書き出しは変わらない。
//
// 【穴】MeshType.Helper・ExcludeFromExport・頂点なし。OBJ・STL・HierarchyBuilder は種類で、
//   PMX は ExcludeFromExport で読み飛ばす。MQO は種類で飛ばさないので IsPlaceholder を見る。
//   Depth を十分大きくする（MeshHierarchyOps.BuildParentIndicesFromDepth の親の探索で、穴を親として拾わないため）。
//
// 【モーフ】計算で頂点数が変わった行のモーフは、頂点の番号が合わないので書かない（警告を出す）。
//   頂点数が同じならそのまま書く。
//
// 【ミラー】結果にミラー側があれば（ミラーのモディファイア）、写しの元の行の相方にする。
//   元にミラー側があって結果に無ければ、焼いた形からミラー側を作り直す。

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Modifiers
{
    public static class ModifierExportView
    {
        private const int PlaceholderDepth = 1 << 20;

        /// <summary>結果の行の代わりに置く穴。</summary>
        private sealed class PlaceholderContext : MeshContext { }

        /// <summary>穴か（書き出さない行か）。</summary>
        public static bool IsPlaceholder(MeshContext mc) => mc is PlaceholderContext;

        /// <summary>
        /// 書き出しに渡すモデル。元と結果の対が無ければ model そのもの。
        /// </summary>
        public static ModelContext For(ModelContext model, bool applyModifiers)
        {
            // 「ここまで表示」は画面だけの設定。書き出しは最後までの形にする。
            ModifierChainHandler.ClearDisplayUpTo(model);
            var pairs = ModifierResultOps.CollectFolds(model);
            if (pairs.Count == 0) return model;

            var list = model.MeshContextList;
            var rows = new List<MeshContext>(list);
            var replaced = new Dictionary<MeshContext, MeshContext>();
            var bakedOfResult = new Dictionary<MeshContext, MeshContext>();

            void Set(int index, MeshContext row)
            {
                replaced[list[index]] = row;
                rows[index] = row;
            }

            var resultPeers = new List<int>();
            var sourcePeers = new List<int>();
            foreach (var p in pairs)
            {
                int si = list.IndexOf(p.Source);
                int ri = list.IndexOf(p.Result);
                if (si < 0 || ri < 0) continue;

                // 途中の段の結果とそのミラー側は書かない。
                foreach (var middle in p.Middle)
                {
                    int mi = list.IndexOf(middle);
                    if (mi < 0) continue;
                    resultPeers.Clear();
                    MirrorBranchOps.CollectMirrorPeers(model, mi, resultPeers);
                    Set(mi, MakePlaceholder(middle));
                    foreach (int m in resultPeers) Set(m, MakePlaceholder(list[m]));
                }

                resultPeers.Clear();
                MirrorBranchOps.CollectMirrorPeers(model, ri, resultPeers);

                Set(ri, MakePlaceholder(p.Result));
                if (!applyModifiers)
                {
                    foreach (int m in resultPeers) Set(m, MakePlaceholder(list[m]));
                    continue;
                }

                var baked = MakeBaked(p.Source, p.Result);
                Set(si, baked);
                bakedOfResult[p.Result] = baked;

                sourcePeers.Clear();
                MirrorBranchOps.CollectMirrorPeers(model, si, sourcePeers);
                if (resultPeers.Count > 0)
                {
                    // 結果のミラー側を元の行の相方として書く。元にもミラー側があれば、書くのは結果の側だけ。
                    string name = MirrorNameOps.MakeMirrorName(baked.Name, MirrorBranchOps.MirrorBranchSuffix, _ => false);
                    foreach (int m in resultPeers) Set(m, MakeMirrorOf(baked, si, list[m], p.Result, name));
                    foreach (int m in sourcePeers) Set(m, MakePlaceholder(list[m]));
                }
                else
                {
                    foreach (int m in sourcePeers) Set(m, MakeMirrorOf(baked, si, list[m], p.Source, list[m].Name));
                }

                // 頂点数が変わったらモーフは番号が合わない。
                if (p.Result.MeshObject.VertexCount != p.Source.MeshObject.VertexCount)
                {
                    int dropped = 0;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var mc = list[i];
                        if (mc == null || mc.Type != MeshType.Morph || mc.MorphParentIndex != si) continue;
                        Set(i, MakePlaceholder(mc));
                        dropped++;
                    }
                    if (dropped > 0)
                        Debug.LogWarning($"[ModifierExportView] {p.Source.Name}: モディファイアで頂点数が変わったので、モーフ {dropped} 件を書き出しません");
                }
            }

            // ミラー対を写しの行へ付け替える。片方でも穴になった対は外す。
            var pairsOut = new List<MirrorPair>();
            if (model.MirrorPairs != null)
                foreach (var mp in model.MirrorPairs)
                {
                    if (mp?.Real == null || mp.Mirror == null) continue;
                    MeshContext real = bakedOfResult.TryGetValue(mp.Real, out var b) ? b
                                     : replaced.TryGetValue(mp.Real, out var r) ? r : mp.Real;
                    MeshContext mirror = replaced.TryGetValue(mp.Mirror, out var m) ? m : mp.Mirror;
                    if (IsPlaceholder(real) || IsPlaceholder(mirror)) continue;
                    pairsOut.Add(ReferenceEquals(real, mp.Real) && ReferenceEquals(mirror, mp.Mirror)
                        ? mp
                        : new MirrorPair { Real = real, Mirror = mirror, Axis = mp.Axis });
                }

            var view = model.CloneForExport(rows, pairsOut);
            foreach (var row in replaced.Values)
                if (row != null) row.ParentModelContext = view;
            return view;
        }

        private static MeshContext MakePlaceholder(MeshContext src)
        {
            var ph = new PlaceholderContext { MeshObject = new MeshObject(src.Name ?? "") };
            ph.Type                 = MeshType.Helper;
            ph.ObjectId             = src.ObjectId;
            ph.Depth                = PlaceholderDepth;
            ph.HierarchyParentIndex = -1;
            ph.IsVisible            = false;
            ph.ExcludeFromExport    = true;
            return ph;
        }

        /// <summary>元の行の写しに、結果の頂点・面・線分を入れる。名前・階層・姿勢は元のまま。</summary>
        private static MeshContext MakeBaked(MeshContext source, MeshContext result)
        {
            var row = MeshContextCloneOps.Clone(source, MeshContextCloneKind.StateSnapshot);
            ModifierResultOps.CopyGeometry(result.MeshObject, row.MeshObject);
            row.HideFaceShading = false;
            row.HideCageLines   = false;
            if (result.MirrorType != 0)
            {
                row.MirrorType           = result.MirrorType;
                row.MirrorAxis           = result.MirrorAxis;
                row.MirrorDistance       = result.MirrorDistance;
                row.MirrorMaterialOffset = result.MirrorMaterialOffset;
            }
            return row;
        }

        /// <summary>
        /// 写しの元の行に対するミラー側の行。形を焼いた行から作り直す。作れなければ穴にする。
        /// mirrorSettings はミラーの軸・距離を持つ行（元のミラーなら元、結果のミラーなら結果）。
        /// </summary>
        private static MeshContext MakeMirrorOf(MeshContext baked, int bakedIndex, MeshContext mirror,
                                                MeshContext mirrorSettings, string name)
        {
            var built = MirrorBranchOps.BuildMirroredMeshObject(
                baked.MeshObject, MirrorBranchOps.ResolveMirrorAxis(mirrorSettings),
                mirrorSettings.MirrorDistance, mirrorSettings.MirrorMaterialOffset, name);
            if (built == null) return MakePlaceholder(mirror);

            var row = MeshContextCloneOps.Clone(mirror, MeshContextCloneKind.StateSnapshot);
            ModifierResultOps.CopyGeometry(built, row.MeshObject);
            row.Name                   = name;
            row.HierarchyParentIndex   = baked.HierarchyParentIndex;
            row.Depth                  = baked.Depth;
            row.BakedMirrorSourceIndex = bakedIndex;
            return row;
        }
    }
}
