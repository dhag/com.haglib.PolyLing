// ModifierChainHandler.cs
// 処理列（ModifierStack.IsChain のスタック）を評価器（ModifierEvaluator）に 1 つの節点として載せる処理器。
// Runtime/Poly_Ling_Main/Core/Modifiers/ に配置
//
// 【正本】PolyLing_モディファイア枠組み計画.md 3.4（段 3）
//
// 【データの持ち方】（段 3 で決めた。計画書 3.1）
//   元メッシュは各項目の MeshRefIds[SourceKey]、結果は各項目の OutputObjectIds に持つ。
//   出力の保護・参照切れの片付け・ダイジェストが項目単位の ID で動くので、そのまま乗る。
//   全項目で値が食い違えば評価は失敗にする。
//   項目の参照オブジェクトは、種類の引数のうち PLParam(IsMeshRef) のキーで MeshRefIds に持つ。
//
// 【節点】ModifierEvaluator.AddGroup が処理列のスタックを Entry 1 つにまとめる
//   （入力＝元メッシュと全項目〔無効な項目を含む〕の参照、出力＝結果）。無効な項目の参照も入れるのは、
//   有効の切り替えでグラフを作り直さないため。処理器のキーは Key（action 名にならない文字列）。
//
// 【評価】
//   Refresh  … 元メッシュの署名と項目ごとの指紋（前の指紋・種類名・引数・参照の ID と署名）を比べ、
//              変わった項目から先だけ流して結果へ書く。それより前の項目の結果の控えは使い回す。
//              控えはメモリだけ（保存しない）。失敗した項目で止め、以降は流さない。
//   SyncDrag … 有効な項目がすべて位置だけなら、位置の口で結果の位置だけ書く。
//              そうでなければ評価器が要評価の印を立てる（FollowsDrag が false）。
//   Release  … 控えを捨て、結果のロックを外し、元メッシュの面の塗りを戻す。
//   元メッシュの面の塗りは、結果を初めて見た Refresh で止める（元メッシュは結果のケージ。段 4）。
//
// 【参照の行列】（段 7b）参照ごとに「参照のローカル → 元メッシュのローカル」
//   （元.BindWorldToVertexMatrix × 参照.VertexToBindWorldMatrix）を MeshModifierInput.ReferenceToSource で渡し、
//   項目の指紋に混ぜる。オブジェクトの移動は頂点の格納値を変えないので、評価器が MatrixInputs
//   （AddChainEntry が参照と元メッシュを入れる）の行列の署名で印を立て（MarkMatrixChanged）、ここで指紋が変われば流す。
//
// 【処理範囲】項目の Args の scope.* を MeshModifierScope で読み、元メッシュのパーツ選択辞書で
//   その項目の入力へ引き直す。位置だけの種類は、範囲の外の頂点を枠組みが入力の位置へ戻す。
//   面構成を変える種類は範囲の外を通すのを作者が受け持つ。

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.Ops;

namespace Poly_Ling.Modifiers
{
    public static class ModifierChainHandler
    {
        /// <summary>処理器のキー。action 名・種類名にならない文字列。</summary>
        public const string Key = "@chain";

        /// <summary>元メッシュを指す MeshRefIds のキー。種類の引数名（プロパティ名）は '.' を含まないので重ならない。</summary>
        public const string SourceKey = "chain.source";

        /// <summary>評価器へ登録する処理器（ModifierEvaluator の静的初期化で登録される）。</summary>
        public static readonly IModifierHandler Handler = new ChainHandler();

        /// <summary>項目 1 つの最後の評価の状態。</summary>
        public enum ItemResult { None, Ok, Failed, Blocked, Disabled }

        public sealed class ItemState
        {
            public string ScenarioItemId = "";
            public string Kind = "";
            public ItemResult Result;
            public string Message = "";
        }

        private sealed class ItemCache
        {
            public ulong Fp;
            public MeshObject Output;
            public IMeshModifier Modifier;
            public MeshModifierInfo Info;
            public ResolvedMeshModifierScope Scope;
            public Dictionary<string, MeshObject> References;
            public Dictionary<string, Matrix4x4> ReferenceToSource;
        }

        private sealed class Cache
        {
            public MeshContext Source;
            public ulong SourceFp;
            public MeshObject SourceMesh;
            public readonly List<ItemCache> Items = new List<ItemCache>();
            /// <summary>最後に結果へ書いたときの指紋（0 = 書いていない）。</summary>
            public ulong WrittenFp;
            /// <summary>最後に結果へ書いた中身の署名。</summary>
            public ulong WrittenSig;
            /// <summary>失敗したときの指紋（同じなら流し直さない）。</summary>
            public ulong FailedFp;
            public string FailedMessage = "";
            public List<ItemState> States = new List<ItemState>();
        }

        private static readonly ConditionalWeakTable<MeshContext, Cache> _cache
            = new ConditionalWeakTable<MeshContext, Cache>();

        // ================================================================
        // 照会
        // ================================================================

        /// <summary>項目ごとの最後の評価の状態（結果の描画オブジェクトで引く）。無ければ空。</summary>
        public static IReadOnlyList<ItemState> ItemStates(MeshContext result)
            => result != null && _cache.TryGetValue(result, out var c) ? c.States : new List<ItemState>();

        /// <summary>
        /// 有効な項目がすべて、位置だけ（MeshModifierChange.PositionsOnly）か、
        /// ドラッグ中の追随の口（IMeshModifierDragFollow。段 8）を持つか。
        /// 項目の並び・有効・種類はグラフを作り直さずに変わるので、評価器はその時点の値で聞く。
        /// </summary>
        public static bool FollowsDrag(ModifierEvaluator.Entry e)
        {
            var g = e?.Group;
            if (g?.Steps == null) return false;
            foreach (var s in g.Steps)
            {
                if (s == null || !s.IsExecutable || !s.Enabled) continue;
                var info = MeshModifierRegistry.Find(s.Action);
                if (info == null) return false;
                if (info.Change != MeshModifierChange.PositionsOnly && !info.HasDragFollow) return false;
            }
            return true;
        }

        /// <summary>
        /// 有効な項目に面構成を変える種類があるか（段 8）。ドラッグ中は追随の口で位置だけ写し、
        /// 離したとき・コマンドの終わりに評価器が作り直す（ModifierEvaluator.SyncFromInput が印を立てる）。
        /// </summary>
        public static bool HasTopologyItem(ModifierEvaluator.Entry e)
        {
            var g = e?.Group;
            if (g?.Steps == null) return false;
            foreach (var s in g.Steps)
            {
                if (s == null || !s.IsExecutable || !s.Enabled) continue;
                var info = MeshModifierRegistry.Find(s.Action);
                if (info != null && info.Change != MeshModifierChange.PositionsOnly) return true;
            }
            return false;
        }

        // ================================================================
        // 指紋
        // ================================================================

        private static ulong Mix(ulong h, ulong v) { unchecked { return (h ^ v) * 1099511628211UL + 0x9E3779B97F4A7C15UL; } }

        private static ulong Str(string s)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                if (s != null) foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
                return h;
            }
        }

        private static ulong MeshSig(MeshContext mc)
            => mc?.MeshObject == null ? 0UL : Mix(MeshSignature.Topology(mc.MeshObject), MeshSignature.Positions(mc));

        private static ulong ContentSig(MeshObject mo)
        {
            if (mo == null) return 0UL;
            ulong h = MeshSignature.Topology(mo);
            unchecked
            {
                foreach (var v in mo.Vertices)
                {
                    if (v == null) { h = Mix(h, 1UL); continue; }
                    var p = v.Position;
                    h = Mix(h, (ulong)(long)Mathf.RoundToInt(p.x * 100000f));
                    h = Mix(h, (ulong)(long)Mathf.RoundToInt(p.y * 100000f));
                    h = Mix(h, (ulong)(long)Mathf.RoundToInt(p.z * 100000f));
                }
            }
            return h;
        }

        /// <summary>参照のローカル → 元メッシュのローカル（作り直しと同じバインド空間。段 7b）。</summary>
        private static Matrix4x4 RefToSource(MeshContext source, MeshContext reference)
            => source.BindWorldToVertexMatrix * reference.VertexToBindWorldMatrix;

        /// <summary>
        /// 項目 1 つの指紋の材料（種類名・引数・参照の ID と署名と、参照 → 元メッシュの行列。段 7b）。
        /// 行列は相対なので、相手と元メッシュを一緒に動かしても指紋は変わらない。
        /// </summary>
        private static ulong ItemSig(ModifierStackStep s, Dictionary<ulong, MeshContext> map, MeshContext source)
        {
            ulong h = Str(s.Action);
            foreach (var kv in s.SortedArgs()) { h = Mix(h, Str(kv.Key)); h = Mix(h, Str(kv.Value)); }
            foreach (var kv in s.SortedMeshRefIds())
            {
                if (kv.Key == SourceKey) continue;
                h = Mix(h, Str(kv.Key));
                if (kv.Value == null) continue;
                foreach (ulong id in kv.Value)
                {
                    h = Mix(h, id);
                    if (map.TryGetValue(id, out var mc) && mc != null)
                    {
                        h = Mix(h, MeshSig(mc));
                        if (source != null) h = Mix(h, MeshSignature.Matrix(RefToSource(source, mc)));
                    }
                    else h = Mix(h, 3UL);
                }
            }
            return h;
        }

        // ================================================================
        // 元メッシュ・結果
        // ================================================================

        /// <summary>全項目に共通の元メッシュと結果を引く。食い違い・欠けは理由を返す。</summary>
        private static bool TryEnds(ModifierEvaluator.Entry e, Dictionary<ulong, MeshContext> map,
            out MeshContext source, out MeshContext result, out string error)
        {
            source = null; result = null; error = null;
            ulong src = 0UL, res = 0UL;
            foreach (var s in e.Group.Steps)
            {
                if (s == null || !s.IsExecutable) continue;
                var sids = s.GetMeshRefIds(SourceKey);
                ulong sid = sids.Count == 1 ? sids[0] : 0UL;
                ulong rid = s.OutputObjectIds != null && s.OutputObjectIds.Count == 1 ? s.OutputObjectIds[0] : 0UL;
                if (sid == 0UL) { error = $"項目 {s.ScenarioItemId} に元メッシュ（{SourceKey}）が 1 つだけ入っていない"; return false; }
                if (rid == 0UL) { error = $"項目 {s.ScenarioItemId} に結果（出力）が 1 つだけ入っていない"; return false; }
                if (src == 0UL) { src = sid; res = rid; continue; }
                if (src != sid) { error = $"項目 {s.ScenarioItemId} の元メッシュ {sid} が他の項目（{src}）と違う"; return false; }
                if (res != rid) { error = $"項目 {s.ScenarioItemId} の結果 {rid} が他の項目（{res}）と違う"; return false; }
            }
            if (src == 0UL) { error = "実行する項目が無い"; return false; }
            if (src == res) { error = "元メッシュと結果が同じオブジェクト"; return false; }
            if (!map.TryGetValue(src, out source) || source?.MeshObject == null) { error = $"元メッシュ {src} が見つからない"; return false; }
            if (!map.TryGetValue(res, out result) || result?.MeshObject == null) { error = $"結果 {res} が見つからない"; return false; }
            return true;
        }

        /// <summary>元メッシュの写し（作業中オフセット込み）。</summary>
        private static MeshObject SourceCopy(MeshContext src)
        {
            var mo = src.MeshObject.Clone();
            var wp = src.WorkingPositions;
            if (wp != null)
                for (int i = 0; i < mo.Vertices.Count && i < wp.Length; i++) mo.Vertices[i].Position += wp[i];
            mo.InvalidatePositionCache();
            return mo;
        }

        // ================================================================
        // 項目 1 つ
        // ================================================================

        private static bool RunItem(ModifierStackStep s, MeshObject input, MeshContext source,
            Dictionary<ulong, MeshContext> map, ulong fp, out ItemCache ic, out string error)
        {
            ic = null;
            if (!MeshModifierRegistry.FromArgs(s.Action, s.Args, out var mod, out var scope, out error)) return false;
            var info = MeshModifierRegistry.Find(s.Action);

            var refs = new Dictionary<string, MeshObject>(StringComparer.Ordinal);
            var refMats = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            foreach (var p in info.Params)
            {
                if (!p.Attr.IsMeshRef) continue;
                var ids = s.GetMeshRefIds(p.Key);
                if (ids.Count == 0) continue;
                if (!map.TryGetValue(ids[0], out var rmc) || rmc?.MeshObject == null)
                { error = $"参照 \"{p.Key}\" のオブジェクト {ids[0]} が見つからない"; return false; }
                refs[p.Key] = rmc.MeshObject;
                refMats[p.Key] = RefToSource(source, rmc);
            }

            if (!scope.TryResolve(input, source.PartsSelectionSetList, out var resolved, out error))
            { error = "処理範囲: " + error; return false; }

            var mi = new MeshModifierInput { Source = input, Scope = resolved, References = refs, ReferenceToSource = refMats };
            MeshObject output;
            try { output = mod.Evaluate(mi, out error); }
            catch (Exception ex) { error = ex.Message; return false; }
            if (output == null) { if (string.IsNullOrEmpty(error)) error = "結果が空"; return false; }

            if (info.Change == MeshModifierChange.PositionsOnly)
            {
                if (output.Vertices.Count != input.Vertices.Count)
                { error = $"位置だけの種類が頂点数を変えた（{input.Vertices.Count} → {output.Vertices.Count}）"; return false; }
                if (!resolved.IsAll)
                    for (int i = 0; i < output.Vertices.Count; i++)
                        if (!resolved.ContainsVertex(i)) output.Vertices[i].Position = input.Vertices[i].Position;
                output.InvalidatePositionCache();
            }

            ic = new ItemCache { Fp = fp, Output = output, Modifier = mod, Info = info, Scope = resolved, References = refs,
                                 ReferenceToSource = refMats };
            return true;
        }

        // ================================================================
        // 結果へ書く
        // ================================================================

        private static void WriteResult(ModelContext model, MeshContext result, MeshObject src)
        {
            var dst = result.MeshObject;
            dst.Vertices.Clear();
            dst.Faces.Clear();
            foreach (var v in src.Vertices) dst.Vertices.Add(v.Clone());
            foreach (var f in src.Faces) dst.Faces.Add(f.Clone());
            var lgs = new List<LineGroup>();
            if (src.LineGroups != null) foreach (var lg in src.LineGroups) if (lg != null) lgs.Add(lg.Clone());
            dst.LineGroups = lgs;
            dst.RebuildIdSets();
            dst.InvalidatePositionCache();

            // 描画提出と同じフレームで走り得る入口なので遅延破棄にする（SubdivisionSync と同じ）。
            var mesh = dst.ToUnityMesh(model.MaterialCount);
            mesh.name = result.Name;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            result.ReplaceUnityMeshDeferred(mesh);

            RebuildMirrorSide(model, result);
        }

        /// <summary>
        /// 結果に表示ミラー（MirrorPair のミラー側）があれば、結果に合わせて作り直す（段 8。計画書 5 の決定）。
        /// 口は MirrorBranchOps.RebuildMirrorSideOf（対 1 つだけ。スタックの作り直しと共用）。
        /// モデル全体の生成ミラーを作り直す RebuildDerivedMirrorGeometry は使わない。
        /// 描画提出と同じフレームで走り得る入口なので遅延破棄にする。
        /// </summary>
        public static void RebuildMirrorSide(ModelContext model, MeshContext result)
        {
            if (!MirrorBranchOps.RebuildMirrorSideOf(model, result, out var mc) || mc?.MeshObject == null) return;
            var mm = mc.MeshObject.ToUnityMesh(model.MaterialCount);
            mm.name = mc.Name;
            mm.hideFlags = HideFlags.HideAndDontSave;
            mc.ReplaceUnityMeshDeferred(mm);
            mc.OriginalPositions = (Vector3[])mc.MeshObject.Positions.Clone();
        }

        private static string StatesText(List<ItemState> states)
        {
            if (states.Count == 0) return "項目なし";
            var sb = new StringBuilder();
            foreach (var st in states)
            {
                if (sb.Length > 0) sb.Append(" / ");
                sb.Append(st.ScenarioItemId).Append(' ').Append(st.Kind).Append(": ").Append(st.Result);
                if (!string.IsNullOrEmpty(st.Message)) sb.Append('（').Append(st.Message).Append('）');
            }
            return sb.ToString();
        }

        // ================================================================
        // 処理器
        // ================================================================

        private sealed class ChainHandler : IModifierHandler
        {
            public string Action => Key;

            public void Refresh(ModelContext model, ModifierEvaluator.Entry entry, bool inputsChanged)
            {
                if (entry?.Group?.Steps == null) return;
                var g = entry.Group;
                // 結果は元メッシュと参照に追随しているので、ダイジェストで「古い」とは扱わない（SubdivisionSync と同じ）。
                if (!string.IsNullOrEmpty(g.SourceDigest)) g.SourceDigest = "";

                var map = ModifierEvaluator.BuildIdMap(model);
                if (!TryEnds(entry, map, out var source, out var result, out string endErr))
                {
                    ModifierEvaluator.SetHandlerResult(model, entry, ModifierResult.Failed, endErr);
                    return;
                }

                bool fresh = !_cache.TryGetValue(result, out var c) || !ReferenceEquals(c.Source, source);
                if (fresh)
                {
                    _cache.Remove(result);
                    c = new Cache { Source = source };
                    _cache.Add(result, c);
                    // 元メッシュは結果のケージ。面は塗らない（結果が元の内側・同じ場所にあると隠れる）。
                    // 保存しない表示設定なので、つながりを最初に見たときに立てる（SubdivisionSync と同じ）。
                    source.HideFaceShading = true;
                }

                // 指紋の列（有効な項目だけ）
                ulong srcFp = MeshSig(source);
                var steps = new List<ModifierStackStep>();
                var fps = new List<ulong>();
                ulong fp = srcFp;
                foreach (var s in g.Steps)
                {
                    if (s == null || !s.IsExecutable || !s.Enabled) continue;
                    fp = Mix(fp, ItemSig(s, map, source));
                    steps.Add(s);
                    fps.Add(fp);
                }
                ulong finalFp = Mix(fp, (ulong)steps.Count + 1UL);

                // 書いた結果がそのまま残っていれば何もしない（この入口は GPU バッファを作り直すたびに呼ばれる）。
                if (c.WrittenFp == finalFp && c.WrittenSig == ContentSig(result.MeshObject)) return;
                if (c.FailedFp == finalFp)
                {
                    ModifierEvaluator.SetHandlerResult(model, entry, ModifierResult.Failed, c.FailedMessage);
                    return;
                }

                // 変わった項目より前の控えを使い回す
                if (c.SourceFp != srcFp || c.SourceMesh == null)
                {
                    c.SourceFp = srcFp;
                    c.SourceMesh = SourceCopy(source);
                    c.Items.Clear();
                }
                int keep = 0;
                while (keep < c.Items.Count && keep < fps.Count && c.Items[keep].Fp == fps[keep]) keep++;
                if (keep < c.Items.Count) c.Items.RemoveRange(keep, c.Items.Count - keep);

                // 状態の並びは全項目（無効を含む）。有効な項目の位置を引く。
                var stateOf = new Dictionary<ModifierStackStep, ItemState>();
                var states = new List<ItemState>();
                foreach (var s in g.Steps)
                {
                    if (s == null || !s.IsExecutable) continue;
                    var st = new ItemState { ScenarioItemId = s.ScenarioItemId, Kind = s.Action,
                                             Result = s.Enabled ? ItemResult.None : ItemResult.Disabled };
                    states.Add(st);
                    stateOf[s] = st;
                }
                for (int i = 0; i < keep; i++) stateOf[steps[i]].Result = ItemResult.Ok;

                string fail = null;
                for (int i = keep; i < steps.Count; i++)
                {
                    var s = steps[i];
                    if (fail != null) { stateOf[s].Result = ItemResult.Blocked; stateOf[s].Message = "前の項目が失敗"; continue; }
                    var input = i == 0 ? c.SourceMesh : c.Items[i - 1].Output;
                    if (!RunItem(s, input, source, map, fps[i], out var ic, out string err))
                    {
                        stateOf[s].Result = ItemResult.Failed;
                        stateOf[s].Message = err ?? "";
                        fail = $"項目 {s.ScenarioItemId}（{s.Action}）: {err}";
                        continue;
                    }
                    c.Items.Add(ic);
                    stateOf[s].Result = ItemResult.Ok;
                }
                c.States = states;

                if (fail != null)
                {
                    // 「書いた結果がそのまま残っていれば何もしない」の印を外す。外さないと、失敗の前の引数へ
                    // 戻したとき指紋が最後に書いたときと同じになり、状態が Failed のまま流されない（段 7b で確認）。
                    // 項目の控え（c.Items）は残るので、流し直しは使い回しで済む。
                    c.WrittenFp = 0UL;
                    c.FailedFp = finalFp;
                    c.FailedMessage = fail + " ／ " + StatesText(states);
                    ModifierEvaluator.SetHandlerResult(model, entry, ModifierResult.Failed, c.FailedMessage);
                    return;
                }

                var final = steps.Count == 0 ? c.SourceMesh : c.Items[steps.Count - 1].Output;
                WriteResult(model, result, final);
                c.WrittenFp = finalFp;
                c.WrittenSig = ContentSig(result.MeshObject);
                c.FailedFp = 0UL;
                c.FailedMessage = "";
                ModifierEvaluator.SetHandlerResult(model, entry, ModifierResult.Ok,
                    $"流した {steps.Count - keep} 件・使い回し {keep} 件 ／ " + StatesText(states));
            }

            public void SyncDrag(ModelContext model, ModifierEvaluator.Entry entry, MeshContext movedInput,
                                 Action<MeshContext> upload)
            {
                if (entry?.Group?.Steps == null || !FollowsDrag(entry)) return;
                var map = ModifierEvaluator.BuildIdMap(model);
                if (!TryEnds(entry, map, out var source, out var result, out _)) return;
                if (!_cache.TryGetValue(result, out var c) || c.SourceMesh == null) return;

                var steps = new List<ModifierStackStep>();
                foreach (var s in entry.Group.Steps)
                    if (s != null && s.IsExecutable && s.Enabled) steps.Add(s);
                // 控えが今の列と揃っていなければ書かない（離したときの Refresh が作り直す）。
                if (c.Items.Count != steps.Count) return;

                var smo = source.MeshObject;
                int n0 = smo.Vertices.Count;
                if (n0 != c.SourceMesh.Vertices.Count) return;

                var pos = new Vector3[n0];
                var wp = source.WorkingPositions;
                for (int i = 0; i < n0; i++)
                {
                    pos[i] = smo.Vertices[i].Position;
                    if (wp != null && i < wp.Length) pos[i] += wp[i];
                }

                // 項目ごとに頂点数が変わってよい（面構成を変える種類の追随の口。段 8）。
                // 各項目の入力の頂点数は、控えた入力のメッシュ（前の項目の結果）と同じでなければ書かない。
                for (int k = 0; k < steps.Count; k++)
                {
                    var ic = c.Items[k];
                    var input = k == 0 ? c.SourceMesh : c.Items[k - 1].Output;
                    int n = pos.Length;
                    if (input == null || n != input.Vertices.Count) return;

                    if (ic.Info == null || ic.Info.Change != MeshModifierChange.PositionsOnly)
                    {
                        // 面構成を変える種類：最後の Evaluate の控えで位置だけ写す。
                        if (!(ic.Modifier is IMeshModifierDragFollow df)) return;
                        Vector3[] moved;
                        try { if (!df.FollowPositions(pos, out moved) || moved == null) return; }
                        catch (Exception) { return; }
                        if (ic.Output == null || moved.Length != ic.Output.Vertices.Count) return;
                        pos = moved;
                        continue;
                    }

                    var mi = new MeshModifierInput { Source = input, Scope = ic.Scope, References = ic.References,
                                                     ReferenceToSource = ic.ReferenceToSource };
                    var dst = (Vector3[])pos.Clone();
                    if (ic.Modifier is IMeshModifierPositions pp)
                    {
                        if (!pp.EvaluatePositions(mi, pos, dst, out _)) return;
                    }
                    else
                    {
                        // 位置の口が無い位置だけの種類：入力の写しに位置を入れて Evaluate し、位置だけ取る。
                        var tmp = input.Clone();
                        for (int i = 0; i < n; i++) tmp.Vertices[i].Position = pos[i];
                        mi.Source = tmp;
                        MeshObject o;
                        try { o = ic.Modifier.Evaluate(mi, out _); } catch (Exception) { return; }
                        if (o == null || o.Vertices.Count != n) return;
                        for (int i = 0; i < n; i++) dst[i] = o.Vertices[i].Position;
                    }
                    if (!ic.Scope.IsAll)
                        for (int i = 0; i < n; i++) if (!ic.Scope.ContainsVertex(i)) dst[i] = pos[i];
                    pos = dst;
                }

                var verts = result.MeshObject.Vertices;
                if (pos.Length != verts.Count) return;
                for (int i = 0; i < pos.Length; i++) verts[i].Position = pos[i];
                result.MeshObject.InvalidatePositionCache();
                upload?.Invoke(result);

                // 表示ミラーのミラー側も写す（段 8）。ミラー側は添字恒等対応（BuildMirroredMeshObject）。
                // MirrorPair.MirrorPosition は距離を使わないので、生成ミラーと同じ MirrorPoint（軸・距離）で写す。
                var pair = model.GetMirrorPair(result);
                var mmo = pair != null && ReferenceEquals(pair.Real, result) ? pair.Mirror?.MeshObject : null;
                if (mmo != null && mmo.Vertices.Count == pos.Length)
                {
                    int axis = MirrorBranchOps.ResolveMirrorAxis(result);
                    float dist = result.MirrorDistance;
                    for (int i = 0; i < pos.Length; i++) mmo.Vertices[i].Position = MirrorBranchOps.MirrorPoint(axis, dist, pos[i]);
                    mmo.InvalidatePositionCache();
                    upload?.Invoke(pair.Mirror);
                }
            }

            public void Release(ModelContext model, ModifierEvaluator.Entry entry)
            {
                foreach (var r in entry?.Outputs ?? new List<MeshContext>())
                {
                    if (r == null) continue;
                    _cache.Remove(r);
                    r.IsLocked = false;
                }
                // 元メッシュの面の塗りを戻す（Refresh で止めたもの）。
                var source = entry?.Input(SourceKey);
                if (source != null) source.HideFaceShading = false;
            }
        }
    }
}
