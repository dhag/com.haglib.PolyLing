// ModifierChainOps.cs
// 処理列（ModifierStack.IsChain）を読む共通の口。受け口（PlayerCommandDispatcher.ModifierChain.cs）と
// 表示用の写し（LiveProjectView.BuildModifierStackViews）の両方が使う。
// Runtime/Poly_Ling_Main/Core/Modifiers/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.1・段 4・段 5
//   元メッシュは各項目の MeshRefIds[ModifierChainHandler.SourceKey]、結果は各項目の OutputObjectIds。
//   参照の引数は MeshRefIds に ObjectId で持ち、読むときは今の索引へ引き直す。
//   ここはモデルを書き換えない。

using System;
using System.Collections.Generic;
using System.Globalization;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Modifiers
{
    public static class ModifierChainOps
    {
        /// <summary>処理列の元メッシュと結果（最初の実行項目から引く）。無ければ null。</summary>
        public static void ChainEnds(ModelContext model, ModifierStack g, out MeshContext source, out MeshContext result)
        {
            source = null; result = null;
            if (model == null || g?.Steps == null) return;
            var map = ModifierEvaluator.BuildIdMap(model);
            foreach (var s in g.Steps)
            {
                if (s == null || !s.IsExecutable) continue;
                var sids = s.GetMeshRefIds(ModifierChainHandler.SourceKey);
                if (sids.Count > 0) map.TryGetValue(sids[0], out source);
                ulong rid = s.FirstOutputId;
                if (rid != 0UL) map.TryGetValue(rid, out result);
                return;
            }
        }

        /// <summary>この描画オブジェクトがどれかの処理列の元メッシュか結果なら、そのスタック。無ければ null。</summary>
        public static ModifierStack ChainOf(ModelContext model, MeshContext mc)
        {
            if (model?.ModifierStacks == null || mc == null || mc.ObjectId == 0UL) return null;
            foreach (var g in model.ModifierStacks)
            {
                if (g == null || !g.IsChain || g.Steps == null) continue;
                foreach (var s in g.Steps)
                {
                    if (s == null || !s.IsExecutable) continue;
                    if (s.ContainsOutput(mc.ObjectId)) return g;
                    if (s.GetMeshRefIds(ModifierChainHandler.SourceKey).Contains(mc.ObjectId)) return g;
                }
            }
            return null;
        }

        /// <summary>項目の今の引数。参照の引数は MeshRefIds から今の索引へ引き直す。</summary>
        public static Dictionary<string, string> CurrentArgs(ModelContext model, ModifierStackStep step)
        {
            var args = new Dictionary<string, string>(StringComparer.Ordinal);
            if (step == null) return args;
            if (step.Args != null) foreach (var kv in step.Args) args[kv.Key] = kv.Value;
            var info = MeshModifierRegistry.Find(step.Action);
            if (info == null || model == null) return args;

            var map = ModifierEvaluator.BuildIdMap(model);
            foreach (var p in info.Params)
            {
                if (!p.Attr.IsMeshRef) continue;
                var ids = step.GetMeshRefIds(p.Key);
                if (ids.Count == 0) continue;
                var parts = new List<string>();
                foreach (ulong id in ids)
                {
                    int idx = map.TryGetValue(id, out var mc) ? model.MeshContextList.IndexOf(mc) : -1;
                    parts.Add(idx.ToString(CultureInfo.InvariantCulture));
                }
                args[p.Key] = string.Join(",", parts);
            }
            return args;
        }

        /// <summary>処理列の節点の最後の評価の結果と理由。節点が無ければ false。</summary>
        public static bool TryGetChainState(ModelContext model, ModifierStack g, out ModifierResult result, out string message)
        {
            result = ModifierResult.None; message = "";
            if (model == null || g == null) return false;
            foreach (var e in ModifierEvaluator.GetGraph(model))
            {
                if (!e.IsChain || !ReferenceEquals(e.Group, g)) continue;
                ModifierEvaluator.GetEntryState(model, e, out _, out result, out message);
                message = message ?? "";
                return true;
            }
            return false;
        }
    }
}
