// ModifierGraph.cs
// オブジェクトグループの項目（モディファイア）の依存のグラフ。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【何を導くか】（PolyLing_作り直し整備計画.md 2.2・段 4）
//   全グループの実行項目から「入力オブジェクト → 項目 → 出力オブジェクト」を導き、依存の順に並べる。
//   辺は 2 種類。
//     ・A の出力を B が入力に持つ                → A → B
//     ・A が Write で持つ入力を B が読む          → A → B
//       （揺れ鎖が梯子へウェイトを塗り、パイプ・フリルが生成時にそれを引き継ぐ。計画書 4 章 1）
//       Write は宣言の書き込み条件（WriteWhen）を項目の引数で判定して有効なものだけ（段 D。
//       ModifierEvaluator.IsWriteKeyActive → PanelCommandFactory.IsWriteActive）。
//       A と B がどちらも同じものを Write で持つときは、グループ順・項目順で前のものから張る。
//   同じ順位のものはグループ順・項目順に並べる（Kahn 法）。
//   並べられずに残ったもの（自分の出力を自分で読むものを含む）は「循環」にして評価から外す。
//
// 【いつ作り直すか】グループの増減・変更のときだけ。ModifierEvaluator が構造キー（StructureKey）を
//   比べて決める。キーはグループ・項目・入出力の ObjectId と、引けた描画オブジェクトの同一性・
//   スキンド化の有無だけから作り、頂点は見ない。
//
// 【状態】項目ごとに次のどれか（ModifierEntryStatus）。
//   Handler  … 処理器のある項目（サブディビジョン）。評価器のきっかけで処理器が流す（段 1 のまま）
//   Evaluate … 処理器の無い項目。作り直し（RunObjectGroupStep）で流す
//   Excluded … 流さない（理由は StatusReason）。スキンド化のあとに評価できない項目もここ（ModifierSpace）
//   Cycle    … 循環。流さない

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>項目の評価上の扱い。</summary>
    public enum ModifierEntryStatus
    {
        Evaluate = 0,
        Handler  = 1,
        Excluded = 2,
        Cycle    = 3,
    }

    public static class ModifierGraph
    {
        // ================================================================
        // 組み立て
        // ================================================================

        /// <summary>
        /// 項目に状態・上流・下流・順を埋め、依存の順に並べた一覧を返す。
        /// 循環の項目は末尾に並べる（照会で見せるため）。
        /// </summary>
        /// <param name="hasHandler">action に処理器があるか。</param>
        public static List<ModifierEvaluator.Entry> Build(
            List<ModifierEvaluator.Entry> entries, Func<string, bool> hasHandler)
        {
            var ordered = new List<ModifierEvaluator.Entry>();
            if (entries == null || entries.Count == 0) return ordered;

            foreach (var e in entries)
            {
                e.Upstream.Clear();
                e.Downstream.Clear();
                e.Order = -1;
                ClassifyStatus(e, hasHandler);
            }

            // ── 出力・Write の入力から、そのオブジェクトに関わる項目を引く表
            var producers = new Dictionary<MeshContext, List<ModifierEvaluator.Entry>>();
            var writers   = new Dictionary<MeshContext, List<ModifierEvaluator.Entry>>();
            foreach (var e in entries)
            {
                foreach (var o in e.Outputs) Add(producers, o, e);
                foreach (var w in e.WriteInputs) Add(writers, w, e);
            }

            var selfLoop = new HashSet<ModifierEvaluator.Entry>();

            foreach (var b in entries)
            {
                foreach (var kv in b.Inputs)
                {
                    var x = kv.Value;

                    if (producers.TryGetValue(x, out var ps))
                    {
                        foreach (var a in ps)
                        {
                            if (ReferenceEquals(a, b)) { selfLoop.Add(b); continue; }
                            Link(a, b);
                        }
                    }

                    if (writers.TryGetValue(x, out var ws))
                    {
                        bool bWrites = b.WriteInputs.Contains(x);
                        foreach (var a in ws)
                        {
                            if (ReferenceEquals(a, b)) continue;
                            // 両方が Write なら、前の項目から張る（逆向きは張らない）。
                            if (bWrites && ComparePosition(a, b) > 0) continue;
                            Link(a, b);
                        }
                    }
                }
            }

            // ── Kahn 法。同順位はグループ順・項目順。
            var indeg = new Dictionary<ModifierEvaluator.Entry, int>();
            foreach (var e in entries) indeg[e] = selfLoop.Contains(e) ? 1 : e.Upstream.Count;

            var ready = new List<ModifierEvaluator.Entry>();
            foreach (var e in entries) if (indeg[e] == 0) ready.Add(e);

            while (ready.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < ready.Count; i++)
                    if (ComparePosition(ready[i], ready[best]) < 0) best = i;
                var n = ready[best];
                ready.RemoveAt(best);

                n.Order = ordered.Count;
                ordered.Add(n);

                foreach (var d in n.Downstream)
                {
                    if (selfLoop.Contains(d)) continue;
                    if (--indeg[d] == 0) ready.Add(d);
                }
            }

            // ── 残ったものは循環
            var rest = new List<ModifierEvaluator.Entry>();
            foreach (var e in entries) if (e.Order < 0) rest.Add(e);
            rest.Sort(ComparePosition);
            foreach (var e in rest)
            {
                e.Status       = ModifierEntryStatus.Cycle;
                e.StatusReason = selfLoop.Contains(e)
                    ? "循環：自分の出力を自分で読んでいる"
                    : "循環：依存をたどると自分に戻る";
                e.Order = ordered.Count;
                ordered.Add(e);
            }

            return ordered;
        }

        private static void ClassifyStatus(ModifierEvaluator.Entry e, Func<string, bool> hasHandler)
        {
            e.StatusReason = "";
            e.Space        = ModifierSpaceKind.Same;
            e.SpaceReason  = "";

            if (hasHandler != null && hasHandler(e.Step.Action))
            {
                e.Status = ModifierEntryStatus.Handler;
                return;
            }

            e.Status = ModifierEntryStatus.Excluded;

            if (e.CommandType == null)
            { e.StatusReason = $"未対応の action: {e.Step.Action}"; return; }

            if (e.MissingRefs > 0)
            { e.StatusReason = $"入力か出力のオブジェクトが {e.MissingRefs} 個見つからない"; return; }

            if (e.Outputs.Count == 0)
            { e.StatusReason = "出力が無い（流すたびに新しいオブジェクトが増えるので自動では流さない）"; return; }

            if (!PanelCommandFactory.RebuildKeys(e.CommandType).IsSupported)
            { e.StatusReason = "作り直しで出力へ書き戻せない（RebuildRole が無い）"; return; }

            // スキンド化のあとの評価空間（段 5。ModifierSpace）。
            e.Space = ModifierSpace.Classify(e, out string spaceWhy);
            e.SpaceReason = spaceWhy ?? "";
            if (e.Space == ModifierSpaceKind.Unsupported)
            { e.StatusReason = spaceWhy; return; }

            e.Status = ModifierEntryStatus.Evaluate;
        }

        private static void Add(
            Dictionary<MeshContext, List<ModifierEvaluator.Entry>> map, MeshContext mc, ModifierEvaluator.Entry e)
        {
            if (mc == null) return;
            if (!map.TryGetValue(mc, out var list)) { list = new List<ModifierEvaluator.Entry>(); map[mc] = list; }
            if (!list.Contains(e)) list.Add(e);
        }

        private static void Link(ModifierEvaluator.Entry a, ModifierEvaluator.Entry b)
        {
            if (!a.Downstream.Contains(b)) a.Downstream.Add(b);
            if (!b.Upstream.Contains(a))   b.Upstream.Add(a);
        }

        /// <summary>グループ順・項目順で比べる。</summary>
        public static int ComparePosition(ModifierEvaluator.Entry a, ModifierEvaluator.Entry b)
        {
            int c = a.GroupIndex.CompareTo(b.GroupIndex);
            return c != 0 ? c : a.StepIndex.CompareTo(b.StepIndex);
        }

        /// <summary>e から下流をたどって届く項目（e 自身は含まない）。</summary>
        public static HashSet<ModifierEvaluator.Entry> DownstreamClosure(ModifierEvaluator.Entry e)
        {
            var set = new HashSet<ModifierEvaluator.Entry>();
            var stack = new Stack<ModifierEvaluator.Entry>();
            foreach (var d in e.Downstream) stack.Push(d);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (!set.Add(n)) continue;
                foreach (var d in n.Downstream) stack.Push(d);
            }
            set.Remove(e);
            return set;
        }

        // ================================================================
        // 循環の判定（登録の前）
        // ================================================================

        /// <summary>
        /// focus のグループの項目が循環に入るなら理由を返す。入らなければ null。
        /// focusInModel が false のときは、まだモデルに足していないグループとして末尾に加えて判定する。
        /// 判定だけで、モデルもグループも変えない。
        /// </summary>
        public static string CycleReason(ModelContext model, ObjectGroup focus, bool focusInModel)
        {
            if (model == null || focus == null) return null;

            var entries = ModifierEvaluator.CollectEntries(model, focusInModel ? null : focus);
            Build(entries, null);

            foreach (var e in entries)
            {
                if (!ReferenceEquals(e.Group, focus) || e.Status != ModifierEntryStatus.Cycle) continue;
                return $"グループ「{focus.Name}」の項目 {e.StepIndex}（{e.Step.Action}）が依存の循環になる"
                     + $"（{e.StatusReason}）";
            }
            return null;
        }

        // ================================================================
        // 構造キー
        // ================================================================

        /// <summary>
        /// グラフを作り直すかを決めるキー。グループ・項目の同一性、action、参照の ObjectId と
        /// それが引く描画オブジェクトの同一性・スキンド化の有無、描画オブジェクトの数を混ぜる。
        /// 頂点は見ない。
        /// </summary>
        public static ulong StructureKey(ModelContext model)
        {
            const ulong Offset = 14695981039346656037UL;
            const ulong Prime  = 1099511628211UL;
            ulong h = Offset;
            void Mix(ulong v)
            {
                for (int b = 0; b < 8; b++) { h ^= (byte)(v >> (b * 8)); h *= Prime; }
            }
            void MixStr(string s)
            {
                if (s == null) { Mix(0xFFFFUL); return; }
                foreach (char ch in s) { h ^= ch; h *= Prime; }
                Mix((ulong)s.Length);
            }

            if (model?.ObjectGroups == null || model.MeshContextList == null) return 0UL;

            var map = ModifierEvaluator.BuildIdMap(model);
            void MixId(ulong id)
            {
                Mix(id);
                if (id == 0UL) return;
                if (map.TryGetValue(id, out var mc))
                {
                    Mix((ulong)(uint)RuntimeHelpers.GetHashCode(mc));
                    Mix(mc.IsSkinned ? 1UL : 2UL);
                }
                else Mix(3UL);
            }

            Mix((ulong)model.MeshContextList.Count);
            foreach (var g in model.ObjectGroups)
            {
                if (g == null) { Mix(4UL); continue; }
                Mix((ulong)(uint)RuntimeHelpers.GetHashCode(g));
                if (g.Steps == null) continue;
                foreach (var s in g.Steps)
                {
                    if (s == null) { Mix(5UL); continue; }
                    Mix((ulong)(uint)RuntimeHelpers.GetHashCode(s));
                    Mix(s.IsExecutable ? 6UL : 7UL);
                    MixStr(s.Action);
                    // 書き込みが有効な入力（条件は項目の引数で決まる。段 D）。引数を変えて有無が変われば作り直す。
                    Mix(ModifierEvaluator.WriteActiveSignature(s));
                    if (s.MeshRefIds != null)
                        foreach (var kv in s.MeshRefIds)
                        {
                            MixStr(kv.Key);
                            if (kv.Value == null) continue;
                            foreach (ulong id in kv.Value) MixId(id);
                        }
                    Mix(8UL);
                    if (s.OutputObjectIds != null)
                        foreach (ulong id in s.OutputObjectIds) MixId(id);
                }
            }
            return h;
        }
    }
}
