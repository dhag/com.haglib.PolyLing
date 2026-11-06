// ModifierResultOps.cs
// モディファイアの「元」と「結果」の対を引く共通の口。モデルを書き換えない（CopyGeometry の書き先を除く）。
// Runtime/Poly_Ling_Main/Core/Modifiers/ に配置
//
// 【正本】PolyLing_モディファイア操作統合計画.md 段 2
//   利用者には元と結果を 1 つのオブジェクトとして見せる。実体は 2 行のまま（評価器・保存形式は変えない）。
//   対は次の 2 通り。どちらも選択と編集は元へ回し、一覧と書き出しでは結果を出さない。
//     処理列（ModifierStack.IsChain） … 元は MeshRefIds[ModifierChainHandler.SourceKey]、結果は OutputObjectIds
//     サブディビジョン（subdivideMesh） … 元は MeshRefIds[SubdivisionSync.CageKey]、結果は OutputObjectIds
//   生成の履歴（入力から作るスタック）の出力は対に入れない。出力そのものが利用者の見るオブジェクトだから。
//
// 【畳むのは「他に誰も読まない 1 本道」だけ。これを崩さない】
//   入力 1 つから複数の出力を並列に出せること（はしご → ベルトとボーン）がこのアプリの設計の前提。
//   IsFolded の条件を満たさない形は、一覧・選択・書き出し・適用のどれでも畳まず、別々のオブジェクトのまま扱う。
//     ・元を読むのがその項目だけ（元を他の項目が読むなら並列・分岐）
//     ・結果を読むのが、次の段の元 1 つまでと、処理列の相手の参照だけ（生成が読むなら入力として見えている必要がある）
//     ・項目の出力が 1 つだけ
//   形の決まりは queryMeshModifierAudit の自己試験（ModifierFoldSelfTest）が確かめる。ここを変えるときはそちらも通す。
//
// 【毎フレーム呼ばれる口】IsFoldedResult は原点マーカーのピック判定から呼ばれるので、割り当てをしない。
//   スタックが無いモデルでは最初の判定で抜ける。

using System;
using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Modifiers
{
    public static class ModifierResultOps
    {
        /// <summary>元と結果の対 1 つ。</summary>
        public sealed class Pair
        {
            public ModifierStack     Stack;
            /// <summary>つながりの項目。処理列は先頭の実行項目。</summary>
            public ModifierStackStep Step;
            public MeshContext       Source;
            public MeshContext       Result;
            public bool              IsSubdivision;
        }

        private static bool IsSubdivisionStep(ModifierStack g, ModifierStackStep s)
            => !g.IsChain && string.Equals(s.Action, SubdivisionSync.Action, StringComparison.Ordinal);

        /// <summary>モデルの対を全部引く（スタックの並び順）。元か結果が見つからないものは入れない。</summary>
        public static List<Pair> CollectPairs(ModelContext model)
        {
            var list = new List<Pair>();
            if (model == null || model.ModifierStackCount == 0) return list;

            Dictionary<ulong, MeshContext> map = null;
            foreach (var g in model.ModifierStacks)
            {
                if (g?.Steps == null) continue;
                foreach (var s in g.Steps)
                {
                    if (s == null || !s.IsExecutable) continue;
                    bool subdiv = IsSubdivisionStep(g, s);
                    if (!g.IsChain && !subdiv) continue;

                    var sids = s.GetMeshRefIds(g.IsChain ? ModifierChainHandler.SourceKey : SubdivisionSync.CageKey);
                    ulong rid = s.FirstOutputId;
                    if (sids.Count > 0 && rid != 0UL)
                    {
                        map ??= ModifierEvaluator.BuildIdMap(model);
                        if (map.TryGetValue(sids[0], out var src) && map.TryGetValue(rid, out var res)
                            && src != null && res != null && !ReferenceEquals(src, res))
                            list.Add(new Pair { Stack = g, Step = s, Source = src, Result = res, IsSubdivision = subdiv });
                    }
                    if (g.IsChain) break;   // 処理列は全項目で元と結果がそろっている
                }
            }
            return list;
        }

        /// <summary>このスタックの対。処理列でもサブディビジョンでもなければ null。</summary>
        public static Pair PairOf(ModelContext model, ModifierStack stack)
        {
            if (stack == null) return null;
            foreach (var p in CollectPairs(model))
                if (ReferenceEquals(p.Stack, stack)) return p;
            return null;
        }

        /// <summary>つなぎの項目の元の ObjectId。割り当てをしない。</summary>
        private static ulong SourceIdOf(ModifierStack g, ModifierStackStep s)
        {
            string key = g.IsChain ? ModifierChainHandler.SourceKey : SubdivisionSync.CageKey;
            return s.MeshRefIds != null && s.MeshRefIds.TryGetValue(key, out var ids) && ids != null && ids.Count > 0
                ? ids[0] : 0UL;
        }

        /// <summary>
        /// この項目の元と結果を 1 つのオブジェクトに畳んでよいか（冒頭の決まり）。割り当てをしない。
        /// </summary>
        public static bool IsFolded(ModelContext model, ModifierStack stack, ModifierStackStep step)
        {
            if (model?.ModifierStacks == null || stack == null || step == null) return false;

            ulong sourceId = SourceIdOf(stack, step);
            ulong resultId = step.FirstOutputId;
            if (sourceId == 0UL || resultId == 0UL) return false;
            for (int i = 0; i < step.OutputObjectIds.Count; i++)
                if (step.OutputObjectIds[i] != 0UL && step.OutputObjectIds[i] != resultId) return false;

            ModifierStack nextStack = null;
            ModifierStackStep nextStep = null;
            foreach (var g in model.ModifierStacks)
            {
                if (g?.Steps == null) continue;
                bool sameChain = g.IsChain && ReferenceEquals(g, stack);
                foreach (var s in g.Steps)
                {
                    if (s == null || !s.IsExecutable || s.MeshRefIds == null) continue;
                    if (ReferenceEquals(s, step) || sameChain) continue;
                    bool link = g.IsChain || IsSubdivisionStep(g, s);
                    string linkKey = g.IsChain ? ModifierChainHandler.SourceKey : SubdivisionSync.CageKey;
                    foreach (var kv in s.MeshRefIds)
                    {
                        if (kv.Value == null) continue;
                        if (kv.Value.Contains(sourceId)) return false;
                        if (!kv.Value.Contains(resultId)) continue;

                        if (link && string.Equals(kv.Key, linkKey, StringComparison.Ordinal))
                        {
                            // 次の段の元。段の先が 2 つあれば分岐。
                            bool same = nextStack != null && ReferenceEquals(nextStack, g)
                                        && (g.IsChain || ReferenceEquals(nextStep, s));
                            if (nextStack != null && !same) return false;
                            nextStack = g;
                            nextStep  = s;
                        }
                        else if (!g.IsChain)
                        {
                            return false;   // 生成が結果を入力に読んでいる
                        }
                    }
                }
            }
            return true;
        }

        private static readonly Dictionary<int, int> NoParents = new Dictionary<int, int>();

        /// <summary>結果の索引 → 元の索引（畳むかどうかによらず全部の対）。階層の親の正本。</summary>
        public static IReadOnlyDictionary<int, int> ResultParents(ModelContext model)
        {
            if (model == null || model.ModifierStackCount == 0) return NoParents;
            var map = new Dictionary<int, int>();
            foreach (var p in CollectPairs(model))
            {
                int si = model.MeshContextList.IndexOf(p.Source);
                int ri = model.MeshContextList.IndexOf(p.Result);
                if (si >= 0 && ri >= 0) map[ri] = si;
            }
            return map;
        }

        /// <summary>どれかの実行する項目がこのオブジェクトを入力に読むか。</summary>
        public static bool IsReadBySteps(ModelContext model, ulong objectId)
        {
            if (model?.ModifierStacks == null || objectId == 0UL) return false;
            foreach (var g in model.ModifierStacks)
            {
                if (g?.Steps == null) continue;
                foreach (var s in g.Steps)
                {
                    if (s == null || !s.IsExecutable || s.MeshRefIds == null) continue;
                    foreach (var kv in s.MeshRefIds)
                        if (kv.Value != null && kv.Value.Contains(objectId)) return true;
                }
            }
            return false;
        }

        /// <summary>畳む対だけ（スタックの並び順）。</summary>
        public static List<Pair> FoldedPairs(ModelContext model)
        {
            var list = CollectPairs(model);
            list.RemoveAll(p => !IsFolded(model, p.Stack, p.Step));
            return list;
        }

        /// <summary>畳んだ 1 本道。根の元の名前で末端の形を見せる。</summary>
        public sealed class Fold
        {
            public MeshContext Source;
            public MeshContext Result;
            /// <summary>途中の結果（根と末端を除く）。</summary>
            public List<MeshContext> Middle = new List<MeshContext>();
        }

        /// <summary>畳んだ 1 本道を全部引く。</summary>
        public static List<Fold> CollectFolds(ModelContext model)
        {
            var pairs = FoldedPairs(model);
            var folds = new List<Fold>();
            foreach (var root in pairs)
            {
                if (pairs.Exists(p => ReferenceEquals(p.Result, root.Source))) continue;
                var fold = new Fold { Source = root.Source, Result = root.Result };
                for (int guard = 0; guard < pairs.Count; guard++)
                {
                    var next = pairs.Find(p => ReferenceEquals(p.Source, fold.Result));
                    if (next == null) break;
                    fold.Middle.Add(fold.Result);
                    fold.Result = next.Result;
                }
                folds.Add(fold);
            }
            return folds;
        }

        /// <summary>この描画オブジェクトを元に持つ、畳む対。無ければ null。</summary>
        public static Pair FoldedPairOfSource(ModelContext model, MeshContext source)
        {
            if (source == null) return null;
            foreach (var p in FoldedPairs(model))
                if (ReferenceEquals(p.Source, source)) return p;
            return null;
        }

        /// <summary>畳んだ 1 本道の末端。畳む対の元でなければ mc のまま。</summary>
        public static MeshContext FoldedLeafOf(ModelContext model, MeshContext mc)
        {
            if (model == null || mc == null || model.ModifierStackCount == 0) return mc;
            var pairs = FoldedPairs(model);
            for (int guard = 0; guard < pairs.Count; guard++)
            {
                var cur = mc;
                var next = pairs.Find(p => ReferenceEquals(p.Source, cur));
                if (next == null) break;
                mc = next.Result;
            }
            return mc;
        }

        /// <summary>結果のミラー側なら実体側（結果）を返す。それ以外は mc のまま。</summary>
        private static MeshContext RealOf(ModelContext model, MeshContext mc)
        {
            if (mc.Type == MeshType.MirrorSide || mc.Type == MeshType.BakedMirror)
            {
                if (model.MirrorPairs != null)
                    foreach (var p in model.MirrorPairs)
                        if (p != null && ReferenceEquals(p.Mirror, mc) && p.Real != null) return p.Real;
                int si = mc.BakedMirrorSourceIndex;
                if (si >= 0)
                {
                    var src = model.GetMeshContext(si);
                    if (src != null) return src;
                }
            }
            return mc;
        }

        /// <summary>
        /// この ObjectId を結果に持つ項目と、そのスタック。無ければ false。
        /// </summary>
        private static bool TryFindResultStep(ModelContext model, ulong objectId, out ModifierStack stack, out ModifierStackStep step)
        {
            stack = null; step = null;
            if (objectId == 0UL) return false;
            foreach (var g in model.ModifierStacks)
            {
                if (g?.Steps == null) continue;
                foreach (var s in g.Steps)
                {
                    if (s == null || !s.IsExecutable) continue;
                    if (!g.IsChain && !IsSubdivisionStep(g, s)) continue;
                    if (!s.ContainsOutput(objectId)) continue;
                    stack = g; step = s;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 畳まれた結果（または結果のミラー側）か。利用者には見えない行。割り当てをしない。
        /// </summary>
        public static bool IsFoldedResult(MeshContext mc)
        {
            var model = mc?.ParentModelContext;
            if (model == null || model.ModifierStackCount == 0) return false;
            return TryFindResultStep(model, RealOf(model, mc).ObjectId, out var g, out var s) && IsFolded(model, g, s);
        }

        /// <summary>
        /// 畳まれた結果なら根の元を返す（多段は根までたどる）。畳まれた結果でなければ null。
        /// </summary>
        public static MeshContext FoldedRootOf(ModelContext model, MeshContext mc)
        {
            if (model == null || mc == null || model.ModifierStackCount == 0) return null;
            MeshContext root = null;
            for (int guard = 0; guard <= model.ModifierStackCount; guard++)
            {
                if (!TryFindResultStep(model, RealOf(model, mc).ObjectId, out var g, out var s) || !IsFolded(model, g, s)) break;
                int idx = ObjectIdAllocator.IndexOfId(model.MeshContextList, SourceIdOf(g, s));
                var src = idx >= 0 ? model.GetMeshContext(idx) : null;
                if (src == null) break;
                root = src;
                mc   = src;
            }
            return root;
        }

        /// <summary>
        /// 結果（または結果のミラー側）なら元を返す。結果でなければ null。
        /// </summary>
        public static MeshContext SourceOf(ModelContext model, MeshContext mc)
        {
            if (model == null || mc == null || model.ModifierStackCount == 0) return null;
            if (!TryFindResultStep(model, RealOf(model, mc).ObjectId, out var g, out var s)) return null;
            var sids = s.GetMeshRefIds(g.IsChain ? ModifierChainHandler.SourceKey : SubdivisionSync.CageKey);
            if (sids.Count == 0) return null;
            int idx = ObjectIdAllocator.IndexOfId(model.MeshContextList, sids[0]);
            return idx >= 0 ? model.GetMeshContext(idx) : null;
        }

        /// <summary>畳まれた結果の索引なら根の元の索引へ直す。それ以外はそのまま。選択の入口が使う。</summary>
        public static int ResolveIndex(ModelContext model, int index)
        {
            if (model == null || model.ModifierStackCount == 0) return index;
            var src = FoldedRootOf(model, model.GetMeshContext(index));
            if (src == null) return index;
            int si = model.MeshContextList.IndexOf(src);
            return si >= 0 ? si : index;
        }

        /// <summary>畳まれた結果と、そのミラー側の索引（一覧から外す行）。</summary>
        public static HashSet<int> ResultIndices(ModelContext model)
        {
            var set = new HashSet<int>();
            var peers = new List<int>();
            foreach (var p in FoldedPairs(model))
            {
                int ri = model.MeshContextList.IndexOf(p.Result);
                if (ri < 0) continue;
                set.Add(ri);
                peers.Clear();
                MirrorBranchOps.CollectMirrorPeers(model, ri, peers);
                foreach (int m in peers) set.Add(m);
            }
            AddSweepBands(model, set);
            return set;
        }

        /// <summary>
        /// パイプのためだけに作った帯（同じスタックのパイプ以外に読まれない帯面の出力）も一覧から外す
        /// （操作統合計画 6 章「帯は一覧に出さない」）。揺れ鎖なども読むなら入力として見えている必要があるので外さない。
        /// </summary>
        private static void AddSweepBands(ModelContext model, HashSet<int> set)
        {
            foreach (var g in model.ModifierStacks)
            {
                if (g?.Steps == null || g.IsChain) continue;
                foreach (var s in g.Steps)
                {
                    if (s == null || !s.IsExecutable || s.Action != "edgeRibbonFace") continue;
                    ulong band = s.FirstOutputId;
                    if (band == 0UL) continue;
                    int readers = 0;
                    bool onlyPipe = true;
                    foreach (var o in model.ModifierStacks)
                    {
                        if (o?.Steps == null) continue;
                        foreach (var r in o.Steps)
                        {
                            if (r == null || !r.IsExecutable || r.MeshRefIds == null) continue;
                            foreach (var kv in r.MeshRefIds)
                                if (kv.Value != null && kv.Value.Contains(band))
                                {
                                    readers++;
                                    if (!ReferenceEquals(o, g) || r.Action != "createPipe") onlyPipe = false;
                                    break;
                                }
                        }
                    }
                    if (readers != 1 || !onlyPipe) continue;
                    int bi = ObjectIdAllocator.IndexOfId(model.MeshContextList, band);
                    if (bi >= 0) set.Add(bi);
                }
            }
        }

        /// <summary>
        /// 結果の頂点・面・線分を dst へ写す（ModifierChainHandler.WriteResult と同じ写し方）。
        /// 名前・階層・姿勢・材質の辞書など、頂点以外は dst のまま。
        /// </summary>
        public static void CopyGeometry(MeshObject src, MeshObject dst)
        {
            if (src == null || dst == null) return;
            dst.Vertices.Clear();
            dst.Faces.Clear();
            foreach (var v in src.Vertices) dst.Vertices.Add(v.Clone());
            foreach (var f in src.Faces) dst.Faces.Add(f.Clone());
            var lgs = new List<LineGroup>();
            if (src.LineGroups != null) foreach (var lg in src.LineGroups) if (lg != null) lgs.Add(lg.Clone());
            dst.LineGroups = lgs;
            dst.SkinKind = src.SkinKind;
            dst.RebuildIdSets();
            dst.InvalidatePositionCache();
        }
    }
}
