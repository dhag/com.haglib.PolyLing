// ModifierEvaluator.cs
// オブジェクトグループの項目（モディファイア）を、入力の変化に合わせて評価する共通の評価器。
// Runtime/Poly_Ling_Main/Core/Ops/ に配置
//
// 【正本は ObjectGroup】（PolyLing_作り直し整備計画.md 2.1）
//   項目 1 つ＝モディファイア 1 つ。入力（MeshRefIds のうち ModifierInventory.InputKeys のキー）・
//   出力（OutputObjectIds）・引数はグループが持つ。ここが持つのは依存のグラフ・入力ごとの署名の控え・
//   要評価の印・最後の評価の結果だけで、保存しない。
//
// 【依存のグラフ】（同 2.2・段 4。ModifierGraph）
//   全グループの項目から導き、依存の順に並べる。グループの増減・変更（構造キーの変化）のときだけ
//   作り直す。以下の処理はすべてこの順で回す。
//
// 【きっかけは 2 か所】（同 2.3）
//   RefreshModel  … GPU バッファを作り直す入口（MeshSceneRenderer.RebuildAdapter）の先頭。
//                    面の追加・削除・Undo・読み込みはここを通る。入力ごとに署名を取り直して、
//                    違う項目を要評価にする。処理器のある項目はここで処理器が流す。
//   SyncFromInput … 頂点ドラッグ中の位置の同期（PlayerViewportManager.SyncMeshPositionsAndTransform）。
//                    動かした描画オブジェクトを入力に持つ項目のうち、ドラッグ中にも追随する
//                    もの（PLModifierAttribute.FollowsDrag）は処理器を呼び、それ以外は要評価の印だけ立てる。
//
// 【流す】（同 2.4・段 4）EvaluatePending
//   処理器の無い項目（Player 層の作り直し RunObjectGroupStep）は、きっかけの中では流さない
//   （書き戻しが GPU バッファの作り直しを呼ぶので再入になる）。呼び出し側（Player 層）が
//   一番外側のコマンドの終わりと、頂点ドラッグを離したときに呼ぶ。作り直しの口は引数で受け取る。
//   要評価の項目を依存の順に流し、出力が変わった下流に印を立てる。途中で失敗した項目の下流は流さない。
//
// 【控えのキー】出力の先頭の描画オブジェクトの ObjectId。モデルごとに持つ。
//   ObjectId をキーにするのは、オブジェクト一覧のスナップショットの Undo（描画オブジェクトを
//   複製で置き換える。ObjectId は保たれる）のあとも控えが効くようにするため。
//   置き換わったあとの入力の署名が控えと違えば、出力も作り直される。
//
// 【作った直後の控え】グループ・項目を足したコマンドが、その場で PrimeGroup・PrimeStep を呼ぶ
//   （CaptureObjectGroup・組立の ExecuteEmptyParentAssembly・デッキの後付け。段 9）。
//   控えを取らないと、画面の更新（RefreshModel）より前の入力・出力の編集が「初めて見た値」として
//   控えられ、変化・未確定として扱われなかった。
//
// 【初めて見た項目】署名を控えるだけで要評価にしない。読み込んだ直後の出力は入力と揃っている。
//
// 【入力を読む空間】（段 10）入力キーごとに PLParam.MeshRefSpace で宣言する。
//   Model（モデル空間で読む入力）と梯子（生成物へ梯子の 格納→バインド を掛けるので行列が結果に効く）は
//   署名に VertexToBindWorldMatrix を混ぜる（Entry.MatrixInputs）。BoneTransform・親の付け替え・
//   オブジェクト移動は頂点の格納値を変えないので、位置の署名では拾えない。行列の署名は
//   書き込みのあるコマンドの終わりに MarkMatrixChanged が取り直す（オブジェクト移動のドラッグも
//   離したときに MoveObjectsCommand・RotateObjectsCommand として流れるので、ここを通る）。
//   ポーズ（BonePoseData）は VertexToBindWorldMatrix に入らないので、歩かせても流れない。

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Poly_Ling.Context;
using Poly_Ling.Data;

namespace Poly_Ling.Ops
{
    /// <summary>action 1 種類ぶんの評価のしかた。</summary>
    public interface IModifierHandler
    {
        /// <summary>受け持つ action 名。</summary>
        string Action { get; }

        /// <summary>
        /// GPU バッファを作り直す入口から呼ばれる。
        /// </summary>
        /// <param name="inputsChanged">入力の署名が前回から変わったか（初めて見たときは false）。</param>
        void Refresh(ModelContext model, ModifierEvaluator.Entry entry, bool inputsChanged);

        /// <summary>
        /// 入力の頂点ドラッグ中に呼ばれる（FollowsDrag の項目だけ）。面構成は変えず、位置だけを書く。
        /// </summary>
        /// <param name="upload">位置を書き直した出力を GPU へ上げる口。</param>
        void SyncDrag(ModelContext model, ModifierEvaluator.Entry entry, MeshContext movedInput,
                      Action<MeshContext> upload);

        /// <summary>
        /// 項目をグループから外すとき（「適用」＝グループ解除）に呼ばれる。
        /// 出力を普通のメッシュとして使えるよう、処理器が立てた表示・ロックなどを戻す。
        /// 呼び出し側はオブジェクト一覧のスナップショットで Undo を積むので、ここでは記録しない。
        /// </summary>
        void Release(ModelContext model, ModifierEvaluator.Entry entry);
    }

    /// <summary>項目の最後の評価の結果。</summary>
    public enum ModifierResult
    {
        /// <summary>まだ流していない。</summary>
        None    = 0,
        /// <summary>評価器が流して成功した。</summary>
        Ok      = 1,
        /// <summary>評価器が流して失敗した。入力が再び変わるまで流さない。</summary>
        Failed  = 2,
        /// <summary>上流の項目が失敗しているので流していない（要評価の印は残る）。</summary>
        Blocked = 3,
        /// <summary>コマンド（引数の書き換え・作り直し・配置元の差し替え）が明示的に作り直した。</summary>
        Rebuilt = 4,
    }

    public static class ModifierEvaluator
    {
        // ================================================================
        // 項目
        // ================================================================

        /// <summary>グループの項目 1 つを評価器から見たもの。</summary>
        public sealed class Entry
        {
            public ObjectGroup     Group;
            public ObjectGroupStep Step;
            /// <summary>モデルのグループ一覧での位置。</summary>
            public int             GroupIndex;
            /// <summary>グループの中での項目番号。</summary>
            public int             StepIndex;
            /// <summary>action の型。引けなければ null。</summary>
            public Type            CommandType;
            public bool            FollowsDrag;
            /// <summary>入力。キーと、引けた描画オブジェクト（引けないものは入れない）。</summary>
            public readonly List<KeyValuePair<string, MeshContext>> Inputs
                = new List<KeyValuePair<string, MeshContext>>();
            /// <summary>入力のうち、Write で持つもの（揺れ鎖の梯子など。項目が書き込む）。</summary>
            public readonly List<MeshContext> WriteInputs = new List<MeshContext>();
            /// <summary>
            /// 入力のうち、モデル空間で読むもの（PLParam.MeshRefSpace = Model。段 10）。
            /// 署名に VertexToBindWorldMatrix を混ぜる。どれかのキーが Model ならその入力は Model。
            /// </summary>
            public readonly HashSet<MeshContext> ModelInputs = new HashSet<MeshContext>();
            /// <summary>入力のうち、ローカルで読むもの（PLParam.MeshRefSpace = Local）。</summary>
            public readonly HashSet<MeshContext> LocalInputs = new HashSet<MeshContext>();
            /// <summary>
            /// 署名に行列を混ぜる入力。モデル空間で読む入力と、梯子（ローカルで読むが、生成物へ梯子の
            /// 格納→バインド を掛けるので行列が結果に効く。ModifierSpace.TryComputeMatrix）。
            /// </summary>
            public readonly HashSet<MeshContext> MatrixInputs = new HashSet<MeshContext>();
            /// <summary>出力。引けた描画オブジェクトだけ、項目の並びで。</summary>
            public readonly List<MeshContext> Outputs = new List<MeshContext>();
            /// <summary>引けなかった入力・出力の ObjectId の数（0 は数えない）。</summary>
            public int MissingRefs;

            // ── グラフ（ModifierGraph.Build が埋める）
            /// <summary>依存の順の番号。</summary>
            public int Order = -1;
            public ModifierEntryStatus Status;
            /// <summary>Excluded・Cycle の理由。</summary>
            public string StatusReason = "";
            /// <summary>評価空間（段 5。ModifierSpace.Classify）。</summary>
            public ModifierSpaceKind Space;
            /// <summary>評価空間の判定の理由（評価できないとき）。</summary>
            public string SpaceReason = "";
            public readonly List<Entry> Upstream   = new List<Entry>();
            public readonly List<Entry> Downstream = new List<Entry>();

            /// <summary>このキーで最初に引けた入力。無ければ null。</summary>
            public MeshContext Input(string key)
            {
                foreach (var kv in Inputs)
                    if (string.Equals(kv.Key, key, StringComparison.Ordinal)) return kv.Value;
                return null;
            }

            public MeshContext FirstOutput => Outputs.Count > 0 ? Outputs[0] : null;

            public bool HasInput(MeshContext mc)
            {
                foreach (var kv in Inputs) if (ReferenceEquals(kv.Value, mc)) return true;
                return false;
            }

            /// <summary>控えのキー（出力の先頭の ObjectId）。出力が無ければ 0（控えを持たない）。</summary>
            public ulong StateId => FirstOutput?.ObjectId ?? 0UL;

            /// <summary>人に見せる名前「グループ名#項目番号」。</summary>
            public string Label => $"{Group?.Name}#{StepIndex}";
        }

        /// <summary>入力 1 つぶんの署名。</summary>
        private struct Sig
        {
            public ulong Topology;
            public ulong Positions;
            /// <summary>モデル空間で読む入力だけ：VertexToBindWorldMatrix の署名（段 10）。それ以外は 0。</summary>
            public ulong Matrix;
        }

        /// <summary>項目ごとの控え。保存しない。</summary>
        private sealed class State
        {
            /// <summary>入力の ObjectId → 署名。</summary>
            public readonly Dictionary<ulong, Sig> Inputs = new Dictionary<ulong, Sig>();
            /// <summary>
            /// 梯子の行列 M の出力の枠（ModifierSpace.OutputFrameBindToVertex）の署名（段 E）。
            /// 梯子の行列で書く項目だけ。それ以外は 0。出力の親の付け替えを入力の変化として拾う。
            /// </summary>
            public ulong? OutputFrame;
            public bool Dirty;
            public ModifierResult LastResult;
            public string LastMessage = "";
            public int LastPass;
            // 未確定の判定の控えはここに持たない（段 F）。出力の描画オブジェクトの
            // ModifierForwardSig（最後に作り直しで書いた中身の署名）が持ち、Undo・保存で中身と一緒に動く。
        }

        /// <summary>型ごとの宣言の控え（属性と入力キーは型から決まる）。</summary>
        private sealed class TypeInfo
        {
            public bool FollowsDrag;
            public List<string> InputKeys;
            /// <summary>
            /// Write で宣言したキー → その宣言。書き込みが有効かは項目の引数で決める
            /// （PanelCommandFactory.IsWriteActive。担当者判定・出力の保護と同じ判定。段 D）。
            /// </summary>
            public Dictionary<string, PanelCommandFactory.MeshRefKey> WriteKeys;
            /// <summary>モデル空間で読むキー（PLParam.MeshRefSpace = Model）。</summary>
            public HashSet<string> ModelKeys;
            /// <summary>ローカルで読むキー（PLParam.MeshRefSpace = Local）。</summary>
            public HashSet<string> LocalKeys;
            /// <summary>生成物の座標系の入力（梯子の取り込み元など）のキー（ModifierSpace.SpaceSourceKey）。無ければ null。</summary>
            public string BeltKey;
        }

        /// <summary>モデルごとのグラフの控え。</summary>
        private sealed class GraphCache
        {
            public ulong Key;
            public List<Entry> Ordered;
        }

        /// <summary>1 回の流しの記録。</summary>
        public sealed class PassInfo
        {
            public int    Sequence;
            public string Trigger = "";
            public int    Ran;
            public int    Failed;
            public int    Blocked;
            public readonly List<string> Lines = new List<string>();
        }

        private static readonly Dictionary<string, IModifierHandler> _handlers
            = new Dictionary<string, IModifierHandler>(StringComparer.Ordinal);

        private static readonly Dictionary<Type, TypeInfo> _typeInfo = new Dictionary<Type, TypeInfo>();

        private static readonly ConditionalWeakTable<ModelContext, GraphCache> _graphs
            = new ConditionalWeakTable<ModelContext, GraphCache>();

        private static readonly ConditionalWeakTable<ModelContext, Dictionary<ulong, State>> _states
            = new ConditionalWeakTable<ModelContext, Dictionary<ulong, State>>();

        private static readonly ConditionalWeakTable<ModelContext, PassInfo> _lastPass
            = new ConditionalWeakTable<ModelContext, PassInfo>();

        private static int  _passSeq;
        private static bool _evaluating;

        static ModifierEvaluator()
        {
            Register(SubdivisionSync.Handler);
        }

        /// <summary>処理器を登録する。同じ action は後から登録したもので置き換える。</summary>
        public static void Register(IModifierHandler handler)
        {
            if (handler == null || string.IsNullOrEmpty(handler.Action)) return;
            _handlers[handler.Action] = handler;
        }

        /// <summary>この action に処理器があるか。</summary>
        public static bool HasHandler(string action)
            => !string.IsNullOrEmpty(action) && _handlers.ContainsKey(action);

        /// <summary>評価器が作り直しを流している最中か（入れ子で流さないため）。</summary>
        public static bool IsEvaluating => _evaluating;

        // ================================================================
        // 項目を集める
        // ================================================================

        /// <summary>モデルの全グループから、実行する項目を全部集める（グループ順・項目順。グラフは埋めない）。</summary>
        public static List<Entry> CollectEntries(ModelContext model) => CollectEntries(model, null);

        /// <summary>
        /// モデルの全グループに加え、extra（モデルにまだ足していないグループ）の項目も集める。
        /// 登録の前の循環の判定（ModifierGraph.CycleReason）が使う。
        /// </summary>
        public static List<Entry> CollectEntries(ModelContext model, ObjectGroup extra)
        {
            var list = new List<Entry>();
            if (model?.ObjectGroups == null || model.MeshContextList == null) return list;

            var map = BuildIdMap(model);

            int gi = 0;
            foreach (var g in model.ObjectGroups)
            {
                AddGroup(list, map, g, gi);
                gi++;
            }
            if (extra != null && !model.ObjectGroups.Contains(extra)) AddGroup(list, map, extra, gi);
            return list;
        }

        private static void AddGroup(List<Entry> list, Dictionary<ulong, MeshContext> map, ObjectGroup g, int gi)
        {
            if (g?.Steps == null) return;
            for (int si = 0; si < g.Steps.Count; si++)
            {
                var s = g.Steps[si];
                if (s == null || !s.IsExecutable || string.IsNullOrEmpty(s.Action)) continue;

                var t  = PanelCommandFactory.ResolveType(s.Action);
                var ti = InfoOf(t);

                var e = new Entry
                {
                    Group = g, Step = s, GroupIndex = gi, StepIndex = si,
                    CommandType = t, FollowsDrag = ti.FollowsDrag,
                };

                foreach (var key in ti.InputKeys)
                {
                    bool write = IsWriteKeyActive(t, ti, key, s.Args);
                    bool model = ti.ModelKeys.Contains(key);
                    bool local = ti.LocalKeys.Contains(key);
                    bool matrix = model || string.Equals(key, ti.BeltKey, StringComparison.Ordinal);
                    foreach (ulong id in s.GetMeshRefIds(key))
                    {
                        if (id == 0UL) continue;
                        if (map.TryGetValue(id, out var mc) && mc?.MeshObject != null)
                        {
                            e.Inputs.Add(new KeyValuePair<string, MeshContext>(key, mc));
                            if (write && !e.WriteInputs.Contains(mc)) e.WriteInputs.Add(mc);
                            if (model) e.ModelInputs.Add(mc);
                            if (local) e.LocalInputs.Add(mc);
                            if (matrix) e.MatrixInputs.Add(mc);
                        }
                        else e.MissingRefs++;
                    }
                }

                if (s.OutputObjectIds != null)
                {
                    foreach (ulong id in s.OutputObjectIds)
                    {
                        if (id == 0UL) continue;
                        if (map.TryGetValue(id, out var mc) && mc != null) e.Outputs.Add(mc);
                        else e.MissingRefs++;
                    }
                }

                list.Add(e);
            }
        }

        /// <summary>
        /// 項目の引数で、key の入力へ書き込みが有効か（段 D）。Write の宣言が無ければ false。
        /// 条件（WriteWhen）は担当者判定・出力の保護と同じ PanelCommandFactory.IsWriteActive で判定する。
        /// </summary>
        private static bool IsWriteKeyActive(Type t, TypeInfo ti, string key, IReadOnlyDictionary<string, string> args)
            => ti.WriteKeys.TryGetValue(key, out var wk) && PanelCommandFactory.IsWriteActive(t, wk, args);

        /// <summary>
        /// 項目の、書き込みが有効な入力キーを畳んだ値（グラフの構造キーに混ぜる。段 D）。
        /// 引数を変えて書き込みの有無が変わったとき、グラフを作り直すため。
        /// </summary>
        public static ulong WriteActiveSignature(ObjectGroupStep s)
        {
            if (s == null || string.IsNullOrEmpty(s.Action)) return 0UL;
            var t = PanelCommandFactory.ResolveType(s.Action);
            if (t == null) return 0UL;
            var ti = InfoOf(t);
            ulong h = 1469598103934665603UL;
            int i = 0;
            foreach (var key in ti.InputKeys)
            {
                if (IsWriteKeyActive(t, ti, key, s.Args)) { unchecked { h = (h ^ (ulong)(i + 1)) * 1099511628211UL; } }
                i++;
            }
            return h;
        }

        // ================================================================
        // 宣言に無い書き込みの見張り（段 D）
        // ================================================================

        /// <summary>項目の読むだけの入力（書き込みが有効でないもの）の、形と位置の署名の控え。</summary>
        public sealed class ReadOnlyInputSnapshot
        {
            internal readonly List<(MeshContext Mc, string Key, ulong Topology, ulong Positions)> Items
                = new List<(MeshContext, string, ulong, ulong)>();
        }

        /// <summary>作り直しの前に呼ぶ。読むだけの入力の署名を控える。</summary>
        public static ReadOnlyInputSnapshot SnapshotReadOnlyInputs(Entry e)
        {
            var snap = new ReadOnlyInputSnapshot();
            if (e == null) return snap;
            var seen = new HashSet<MeshContext>();
            foreach (var kv in e.Inputs)
            {
                var mc = kv.Value;
                if (mc?.MeshObject == null || e.WriteInputs.Contains(mc) || !seen.Add(mc)) continue;
                snap.Items.Add((mc, kv.Key, MeshSignature.Topology(mc.MeshObject), MeshSignature.Positions(mc)));
            }
            return snap;
        }

        /// <summary>
        /// 作り直しのあとに呼ぶ。読むだけの入力の形・位置が変わっていれば説明、無ければ null。
        /// 変わっていれば、書くと宣言していない入力へ項目が書いた（PLParam.MeshRefAccess・WriteWhen の宣言漏れ）。
        /// </summary>
        public static string UndeclaredWrites(Entry e, ReadOnlyInputSnapshot snap)
        {
            if (e == null || snap == null) return null;
            var bad = new List<string>();
            foreach (var it in snap.Items)
            {
                if (it.Mc?.MeshObject == null) continue;
                bool topo = MeshSignature.Topology(it.Mc.MeshObject) != it.Topology;
                bool pos  = MeshSignature.Positions(it.Mc) != it.Positions;
                if (!topo && !pos) continue;
                bad.Add($"「{it.Mc.Name}」（{it.Key}）の{(topo ? "形" : "位置")}");
            }
            return bad.Count == 0 ? null : string.Join("、", bad);
        }

        /// <summary>ObjectId → 描画オブジェクト。</summary>
        public static Dictionary<ulong, MeshContext> BuildIdMap(ModelContext model)
        {
            var map = new Dictionary<ulong, MeshContext>();
            var list = model?.MeshContextList;
            if (list == null) return map;
            for (int i = 0; i < list.Count; i++)
            {
                var mc = list[i];
                if (mc == null || mc.ObjectId == 0UL) continue;
                if (!map.ContainsKey(mc.ObjectId)) map[mc.ObjectId] = mc;
            }
            return map;
        }

        private static TypeInfo InfoOf(Type t)
        {
            if (t == null)
                return new TypeInfo
                {
                    FollowsDrag = false, InputKeys = new List<string>(),
                    WriteKeys = new Dictionary<string, PanelCommandFactory.MeshRefKey>(),
                    ModelKeys = new HashSet<string>(), LocalKeys = new HashSet<string>(),
                };
            if (_typeInfo.TryGetValue(t, out var ti)) return ti;

            var attr = t.GetCustomAttribute<PLModifierAttribute>(inherit: true);
            ti = new TypeInfo
            {
                FollowsDrag = attr?.FollowsDrag ?? false,
                InputKeys   = new List<string>(),
                WriteKeys   = new Dictionary<string, PanelCommandFactory.MeshRefKey>(StringComparer.Ordinal),
                ModelKeys   = new HashSet<string>(StringComparer.Ordinal),
                LocalKeys   = new HashSet<string>(StringComparer.Ordinal),
                BeltKey     = ModifierSpace.SpaceSourceKey(t),
            };
            foreach (var mr in ModifierInventory.InputKeys(t))
            {
                ti.InputKeys.Add(mr.Key);
                if (mr.Access == PLMeshRefAccess.Write) ti.WriteKeys[mr.Key] = mr;
                if (mr.Space == PLMeshRefSpace.Model) ti.ModelKeys.Add(mr.Key);
                else if (mr.Space == PLMeshRefSpace.Local) ti.LocalKeys.Add(mr.Key);
            }
            _typeInfo[t] = ti;
            return ti;
        }

        // ================================================================
        // グラフ
        // ================================================================

        /// <summary>
        /// 依存の順に並べた項目（循環は末尾）。グループの構成が前回と同じなら控えを返す。
        /// </summary>
        public static List<Entry> GetGraph(ModelContext model)
        {
            if (model == null) return new List<Entry>();

            ulong key = ModifierGraph.StructureKey(model);
            if (_graphs.TryGetValue(model, out var gc) && gc.Key == key && gc.Ordered != null)
                return gc.Ordered;

            var ordered = ModifierGraph.Build(CollectEntries(model), HasHandler);
            _graphs.Remove(model);
            _graphs.Add(model, new GraphCache { Key = key, Ordered = ordered });
            return ordered;
        }

        /// <summary>控えてあるグラフ（作り直さない）。無ければ null。</summary>
        private static List<Entry> CachedGraph(ModelContext model)
            => model != null && _graphs.TryGetValue(model, out var gc) ? gc.Ordered : null;

        private static Dictionary<ulong, State> States(ModelContext model)
            => _states.GetValue(model, _ => new Dictionary<ulong, State>());

        // ================================================================
        // 控え
        // ================================================================

        /// <summary>
        /// 項目の入力の署名を今の値で控え、要評価の印を下ろす。
        /// コマンドが出力を作った直後に呼ぶ（直後の RefreshModel で変化と見なさないため）。
        /// </summary>
        public static void Prime(ModelContext model, Entry entry)
        {
            if (model == null || entry == null || entry.StateId == 0UL) return;
            var states = States(model);
            if (!states.TryGetValue(entry.StateId, out var st)) { st = new State(); states[entry.StateId] = st; }
            TakeInputs(st, entry);
            st.Dirty = false;
            RecordForwardOutputs(entry);
        }

        /// <summary>
        /// グループを足した直後に呼ぶ。グループの全項目の入力の署名と、正方向の出力を今の値で控える（段 9）。
        /// 控えないと、画面の更新（RefreshModel）より前の入力・出力の編集が「初めて見た値」として控えられ、
        /// 要評価・未確定として扱われない。グループはモデルに足し終えていること。
        /// </summary>
        public static void PrimeGroup(ModelContext model, ObjectGroup group)
        {
            if (model == null || group == null) return;
            foreach (var e in GetGraph(model))
                if (ReferenceEquals(e.Group, group)) Prime(model, e);
        }

        /// <summary>
        /// 既存のグループへ項目を 1 つ足した直後に呼ぶ（デッキの後付けなど）。その項目だけ控える。
        /// 同じグループのほかの項目は控え直さない（未確定の出力の編集を正方向の結果として上書きしないため）。
        /// </summary>
        public static void PrimeStep(ModelContext model, ObjectGroupStep step)
        {
            if (model == null || step == null) return;
            foreach (var e in GetGraph(model))
                if (ReferenceEquals(e.Step, step)) { Prime(model, e); return; }
        }

        /// <summary>
        /// 控えのキー（出力の先頭の描画オブジェクト）と入力を直接渡して控える。
        /// 項目がまだグループに載っていない・集め直していないときに使う。
        /// モデルは描画オブジェクトの ParentModelContext から引く。
        /// </summary>
        public static void Prime(object stateKey, IEnumerable<MeshContext> inputs)
        {
            if (!(stateKey is MeshContext outMc) || outMc.ObjectId == 0UL) return;
            var model = outMc.ParentModelContext;
            if (model == null) return;

            var st = new State();
            if (inputs != null)
                foreach (var mc in inputs)
                    if (mc?.MeshObject != null && mc.ObjectId != 0UL) st.Inputs[mc.ObjectId] = Take(mc, false);
            States(model)[outMc.ObjectId] = st;
        }

        /// <summary>出力の控えを捨てる。</summary>
        public static void Forget(object stateKey)
        {
            if (!(stateKey is MeshContext outMc) || outMc.ObjectId == 0UL) return;
            var model = outMc.ParentModelContext;
            if (model == null) return;
            States(model).Remove(outMc.ObjectId);
        }

        /// <summary>この項目が要評価か（入力が変わったのにまだ流していない）。</summary>
        public static bool IsDirty(ModelContext model, ObjectGroupStep step)
        {
            if (model == null || step == null) return false;
            var list = CachedGraph(model);
            if (list == null) return false;
            foreach (var e in list)
                if (ReferenceEquals(e.Step, step))
                    return e.StateId != 0UL && States(model).TryGetValue(e.StateId, out var st) && st.Dirty;
            return false;
        }

        /// <summary>照会用：項目の要評価の印と、最後の評価の結果。</summary>
        public static void GetEntryState(ModelContext model, Entry e,
            out bool dirty, out ModifierResult result, out string message)
        {
            dirty = false; result = ModifierResult.None; message = "";
            if (model == null || e == null || e.StateId == 0UL) return;
            if (!States(model).TryGetValue(e.StateId, out var st)) return;
            dirty = st.Dirty; result = st.LastResult; message = st.LastMessage ?? "";
        }

        /// <summary>最後の流しの記録。無ければ null。</summary>
        public static PassInfo LastPass(ModelContext model)
            => model != null && _lastPass.TryGetValue(model, out var p) ? p : null;

        /// <summary>
        /// 全項目の入力の署名を今の値で取り直し、要評価の印を下ろす。
        /// スキンド化（頂点をワールドへ焼き直す）のように、入力の変化として扱わない操作のあとに呼ぶ。
        /// </summary>
        public static void PrimeAll(ModelContext model)
        {
            if (model == null) return;
            var states = States(model);
            foreach (var e in GetGraph(model))
            {
                if (e.StateId == 0UL) continue;
                if (!states.TryGetValue(e.StateId, out var st)) { st = new State(); states[e.StateId] = st; }
                TakeInputs(st, e);
                st.Dirty = false;
                RecordForwardOutputs(e);
            }
        }

        /// <summary>
        /// 指定した描画オブジェクトを入力に持つ評価器の項目（Evaluate）へ、署名を見ずに要評価の印を立てる。
        /// スキンド化のあとに呼ぶ（段 5）。スキンド化は頂点をバインド空間へ焼き直すが、
        /// WorldMatrix が単位のオブジェクトでは位置が動かず署名が変わらない。それでも揺れ鎖の塗りと
        /// 引き継ぎは流し直す必要があるので、署名ではなく「変換した入力」で印を立てる。
        /// 署名は今の値で控え直す。
        /// </summary>
        /// <returns>印を立てた項目の数</returns>
        public static int MarkInputsChanged(ModelContext model, IReadOnlyCollection<ulong> objectIds)
        {
            if (model == null || objectIds == null || objectIds.Count == 0) return 0;
            var ids = new HashSet<ulong>(objectIds);
            var states = States(model);
            int n = 0;
            foreach (var e in GetGraph(model))
            {
                if (e.Status != ModifierEntryStatus.Evaluate || e.StateId == 0UL) continue;
                bool hit = false;
                foreach (var kv in e.Inputs) if (ids.Contains(kv.Value.ObjectId)) { hit = true; break; }
                if (!hit) continue;
                if (!states.TryGetValue(e.StateId, out var st)) { st = new State(); states[e.StateId] = st; }
                TakeInputs(st, e);
                st.Dirty = true;
                n++;
            }
            return n;
        }

        /// <summary>
        /// 入力 1 つの署名。model が true（モデル空間で読む入力。段 10）なら VertexToBindWorldMatrix も混ぜる。
        /// 呼ぶ前に ComputeWorldMatrices を済ませていること（行列は控えの値を読むだけ）。
        /// </summary>
        private static Sig Take(MeshContext mc, bool model)
            => new Sig
            {
                Topology  = MeshSignature.Topology(mc.MeshObject),
                Positions = MeshSignature.Positions(mc),
                Matrix    = model ? MeshSignature.Matrix(mc.VertexToBindWorldMatrix) : 0UL,
            };

        private static Sig Take(Entry e, MeshContext mc) => Take(mc, e != null && e.MatrixInputs.Contains(mc));

        /// <summary>
        /// 評価器が流す項目（Evaluate）の、モデル空間で読む入力の行列の署名と、梯子の行列 M の出力の枠の署名
        /// （段 E）だけを取り直し、控えと違う項目に要評価の印を立てる（段 10）。位置は見ない（位置はきっかけ 2 か所が見る）。
        /// 書き込みのあるコマンドの終わりに呼ぶ。BoneTransform・親の付け替え・オブジェクト移動のように、
        /// 頂点の格納値を変えずにバインド姿勢を変える操作を、入力の変化として拾うため。
        /// 呼ぶ前に ComputeWorldMatrices を済ませていること。初めて見た項目は控えるだけ。
        /// </summary>
        /// <returns>印を立てた項目の数</returns>
        public static int MarkMatrixChanged(ModelContext model)
        {
            if (model == null) return 0;
            var states = States(model);
            int n = 0;
            foreach (var e in GetGraph(model))
            {
                if (e.Status != ModifierEntryStatus.Evaluate || e.StateId == 0UL) continue;
                if (!states.TryGetValue(e.StateId, out var st)) continue;

                bool changed = false;

                // 梯子の行列 M の出力の枠（段 E）。出力の親の付け替え・親の姿勢の変更を拾う。
                // 控えが無い（初めて見た）ときは控えるだけ。
                ulong frameNow = OutputFrameSig(e);
                if (!st.OutputFrame.HasValue) st.OutputFrame = frameNow;
                else if (st.OutputFrame.Value != frameNow) { st.OutputFrame = frameNow; changed = true; }

                foreach (var mc in e.MatrixInputs)
                {
                    ulong id = mc.ObjectId;
                    if (id == 0UL || !st.Inputs.TryGetValue(id, out var prev)) continue;
                    ulong now = MeshSignature.Matrix(mc.VertexToBindWorldMatrix);
                    if (prev.Matrix == now) continue;
                    prev.Matrix = now;
                    st.Inputs[id] = prev;
                    changed = true;
                }
                if (!changed) continue;
                st.Dirty = true;
                n++;
            }
            return n;
        }

        // ================================================================
        // 出力の編集（逆方向を持つ項目。段 6）
        // ================================================================

        private static readonly Dictionary<Type, bool> _hasInverse = new Dictionary<Type, bool>();

        /// <summary>この項目が逆方向を持つか（PLModifierAttribute.Inverse）。</summary>
        public static bool HasInverse(Entry e)
        {
            var t = e?.CommandType;
            if (t == null) return false;
            if (!_hasInverse.TryGetValue(t, out bool b)) { b = ModifierInverse.HasInverse(t); _hasInverse[t] = b; }
            return b;
        }

        private static ulong Mix(ulong h, ulong v) { unchecked { return (h ^ v) * 1099511628211UL + 0x9E3779B97F4A7C15UL; } }

        private static ulong StringHash(string s)
        {
            unchecked
            {
                ulong h = 1469598103934665603UL;
                if (s != null) foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
                return h;
            }
        }

        /// <summary>
        /// 出力の描画オブジェクトの中身の署名（面構成と頂点位置）。0 と 1 は使わない
        /// （0 = 未記録、1 = 未確定のまま読み込んだ印。MeshContext.ModifierForwardSig）。
        /// </summary>
        public static ulong OutputContentSig(MeshContext o)
        {
            if (o?.MeshObject == null) return 2UL;
            var s = Take(o, false);
            ulong h = 7809847782465536322UL;
            h = Mix(h, s.Topology);
            h = Mix(h, s.Positions);
            return h <= MeshContext.ModifierForwardSigEditedMark ? h + 2UL : h;
        }

        /// <summary>
        /// 今の出力の中身を「最後に作り直しで書いた中身」として出力ごとに記録する（段 F）。
        /// 逆方向を持つ項目だけ。
        /// </summary>
        private static void RecordForwardOutputs(Entry e)
        {
            if (e == null || !HasInverse(e)) return;
            foreach (var o in e.Outputs)
                if (o != null) o.ModifierForwardSig = OutputContentSig(o);
        }

        /// <summary>記録の無い（0）出力だけ、今の中身を記録する。逆方向を持つ項目だけ。</summary>
        private static void RecordUnrecordedOutputs(Entry e)
        {
            if (e == null || !HasInverse(e)) return;
            foreach (var o in e.Outputs)
                if (o != null && o.ModifierForwardSig == 0UL) o.ModifierForwardSig = OutputContentSig(o);
        }

        /// <summary>
        /// 出力の描画オブジェクトが、最後に作り直しで書いた中身から手で編集されているか（保存用）。
        /// 記録が無い（0）ときは false。
        /// </summary>
        public static bool IsOutputEdited(MeshContext o)
        {
            if (o == null || o.ModifierForwardSig == 0UL) return false;
            return o.ModifierForwardSig != OutputContentSig(o);
        }

        /// <summary>
        /// 出力が手で編集されていて、確定（逆方向）も破棄もしていないか（段 F）。
        /// 出力ごとの記録（ModifierForwardSig）と今の中身の署名を比べ、1 つでも違えば未確定。
        /// 記録の無い出力は今の中身を記録して未確定ではないとする。逆方向を持たない項目は常に false。
        /// 記録は中身と一緒に Undo・Redo（CopyState）と保存（未確定の真偽）で動くので、
        /// 入力の Undo の直後（出力は前の作り直しの結果のまま）は未確定にならない。
        /// </summary>
        public static bool IsOutputEditPending(ModelContext model, Entry e)
        {
            if (model == null || e == null || e.StateId == 0UL || !HasInverse(e)) return false;
            bool pending = false;
            foreach (var o in e.Outputs)
            {
                if (o == null) continue;
                if (o.ModifierForwardSig == 0UL) { o.ModifierForwardSig = OutputContentSig(o); continue; }
                if (o.ModifierForwardSig != OutputContentSig(o)) pending = true;
            }
            return pending;
        }

        /// <summary>モデルに未確定の出力の編集が 1 つでもあるか。あれば最初の項目の名前。</summary>
        public static string FirstPendingOutputEdit(ModelContext model)
        {
            if (model == null) return null;
            foreach (var e in GetGraph(model))
                if (IsOutputEditPending(model, e)) return $"{e.Label} {e.Step?.Action}";
            return null;
        }


        private static void TakeInputs(State st, Entry e)
        {
            st.Inputs.Clear();
            foreach (var kv in e.Inputs)
            {
                ulong id = kv.Value.ObjectId;
                if (id == 0UL || st.Inputs.ContainsKey(id)) continue;
                st.Inputs[id] = Take(e, kv.Value);
            }
            st.OutputFrame = OutputFrameSig(e);
        }

        /// <summary>
        /// 梯子の行列 M の出力の枠の署名（段 E）。梯子の行列で書く項目（Transform・出力 1 つ。
        /// 作り直しの ComputeStepSpace と同じ条件）だけ。それ以外は 0。
        /// 枠は ModifierSpace.OutputFrameBindToVertex（新規オブジェクトとして作った項目は出力の親）。
        /// 呼ぶ前に ComputeWorldMatrices を済ませていること。
        /// </summary>
        private static ulong OutputFrameSig(Entry e)
        {
            if (e == null || e.Space != ModifierSpaceKind.Transform || e.Outputs.Count != 1) return 0UL;
            bool poseOwned = ModifierSpace.IsPoseOwnedByOutput(e.CommandType, e.Step);
            return MeshSignature.Matrix(ModifierSpace.OutputFrameBindToVertex(e.Outputs[0], poseOwned));
        }

        private static bool InputsChanged(State st, Entry e)
        {
            var seen = new HashSet<ulong>();
            foreach (var kv in e.Inputs)
            {
                ulong id = kv.Value.ObjectId;
                if (id == 0UL || !seen.Add(id)) continue;
                if (!st.Inputs.TryGetValue(id, out var prev)) return true;
                var now = Take(e, kv.Value);
                if (prev.Topology != now.Topology || prev.Positions != now.Positions || prev.Matrix != now.Matrix) return true;
            }
            if (st.OutputFrame.HasValue && st.OutputFrame.Value != OutputFrameSig(e)) return true;
            return seen.Count != st.Inputs.Count;
        }

        /// <summary>入力の署名が控えと違えば印を立てる（控えも取り直す）。初めて見た項目も印を立てる。</summary>
        private static void MarkIfChanged(Dictionary<ulong, State> states, Entry d)
        {
            if (d.StateId == 0UL) return;
            if (!states.TryGetValue(d.StateId, out var st))
            {
                st = new State();
                states[d.StateId] = st;
                TakeInputs(st, d);
                st.Dirty = true;
                return;
            }
            if (!InputsChanged(st, d)) return;
            TakeInputs(st, d);
            st.Dirty = true;
        }

        /// <summary>
        /// 評価器が流す項目（Evaluate）の入力の署名を取り直し、控えと違うものに印を立てる。
        /// Undo・Redo の後に呼ぶ。Undo の復元経路には GPU バッファの作り直し（きっかけ 1）も
        /// 位置の同期（きっかけ 2）も通らないものがある（Undo の対象が選択中でないオブジェクトの
        /// 面構成の復元など）。入力の Undo で出力も戻す（整備計画 2.4）には、ここで署名を見る。
        /// 初めて見た項目は控えるだけ。
        /// </summary>
        public static void MarkChangedInputs(ModelContext model)
        {
            if (model == null) return;
            var states = States(model);
            foreach (var e in GetGraph(model))
            {
                if (e.Status != ModifierEntryStatus.Evaluate || e.StateId == 0UL) continue;
                if (!states.TryGetValue(e.StateId, out var st))
                {
                    st = new State();
                    states[e.StateId] = st;
                    TakeInputs(st, e);
                    continue;
                }
                if (!InputsChanged(st, e)) continue;
                TakeInputs(st, e);
                st.Dirty = true;
            }
        }

        // ================================================================
        // きっかけ 1：GPU バッファを作り直す入口
        // ================================================================

        /// <summary>
        /// 依存の順に、入力ごとに署名を取り直す。違う項目を要評価にし、処理器のある項目を流す。
        /// 処理器の無い項目はここでは流さない（EvaluatePending が流す）。
        /// </summary>
        public static void RefreshModel(ModelContext model)
        {
            if (model == null) return;

            var entries = GetGraph(model);
            if (entries.Count == 0) return;
            var states = States(model);

            foreach (var e in entries)
            {
                _handlers.TryGetValue(e.Step.Action, out var h);
                ulong id = e.StateId;

                if (id == 0UL)
                {
                    // 出力の無い項目は控えを持たない。処理器があれば呼ぶ（変化なし扱い）。
                    h?.Refresh(model, e, false);
                    continue;
                }

                bool first = !states.TryGetValue(id, out var st);
                if (first) { st = new State(); states[id] = st; }

                bool changed = !first && InputsChanged(st, e);
                TakeInputs(st, e);

                // 逆方向を持つ項目：記録の無い（0）出力は、今の中身を作り直しの結果として記録する
                // （読み込み・作成の直後は出力と入力が揃っている。のちの手の編集を未確定と見分けるため）。
                // 未確定のまま読み込んだ印（1）や記録済みの値は上書きしない（段 F）。
                if (!changed) RecordUnrecordedOutputs(e);

                if (h != null)
                {
                    h.Refresh(model, e, changed);
                    st.Dirty = false;
                }
                else if (changed)
                {
                    st.Dirty = true;
                }
            }
        }

        // ================================================================
        // 流す（処理器の無い項目）
        // ================================================================

        /// <summary>
        /// 項目 1 つを作り直す口（Player 層の RunObjectGroupStep）。
        /// error は失敗の理由、note は成功時の説明（梯子・断面をどう扱ったか）。
        /// </summary>
        public delegate bool StepRunner(Entry entry, out string error, out string note);

        /// <summary>
        /// 要評価の項目を依存の順に流す。
        ///   ・流すのは状態が Evaluate のものだけ（Handler はきっかけで処理器が流す。Excluded・Cycle は流さない）
        ///   ・成功したら入力の署名を取り直して印を下ろし、出力が変わった下流に印を立てる
        ///   ・失敗したら入力の署名を控えて印を下ろす（入力が再び変わるまで流さない）
        ///   ・上流（たどった先を含む）に失敗した項目があるものは流さず、印を残す
        /// Undo への記録・画面への配りは呼び出し側が持つ。
        /// </summary>
        public static PassInfo EvaluatePending(ModelContext model, StepRunner run, string trigger)
        {
            var pass = new PassInfo { Trigger = trigger ?? "" };
            if (model == null || run == null || _evaluating) return pass;

            var order  = GetGraph(model);
            var states = States(model);

            // 流すものが無ければ記録も残さない（毎コマンドの終わりに呼ばれるため）。
            bool any = false;
            foreach (var e in order)
            {
                if (e.Status != ModifierEntryStatus.Evaluate || e.StateId == 0UL) continue;
                if (states.TryGetValue(e.StateId, out var st0) && st0.Dirty) { any = true; break; }
            }
            if (!any) return pass;

            pass.Sequence = ++_passSeq;
            _evaluating = true;
            try
            {
                // 出力の編集が未確定の項目（逆方向を持つもの。段 6）。流さず、下流も止める。
                var pending = new HashSet<Entry>();
                foreach (var e in order)
                    if (IsOutputEditPending(model, e)) pending.Add(e);

                // 上流に失敗があるか（依存の順に回すので、見る時点で上流は流し終えている）。
                var blockedBy = new Dictionary<Entry, string>();
                string BlockedBy(Entry e)
                {
                    if (blockedBy.TryGetValue(e, out var r)) return r;
                    r = null;
                    foreach (var u in e.Upstream)
                    {
                        if (pending.Contains(u))
                        { r = $"上流の項目 {u.Label} の出力の編集が未確定"; break; }
                        if (u.StateId != 0UL && states.TryGetValue(u.StateId, out var us)
                            && us.LastResult == ModifierResult.Failed)
                        { r = $"上流の項目 {u.Label} が失敗している"; break; }
                        var rr = BlockedBy(u);
                        if (rr != null) { r = rr; break; }
                    }
                    blockedBy[e] = r;
                    return r;
                }

                foreach (var e in order)
                {
                    if (e.Status != ModifierEntryStatus.Evaluate || e.StateId == 0UL) continue;
                    if (!states.TryGetValue(e.StateId, out var st) || !st.Dirty) continue;

                    // グループが外された・差し替わったものは流さない（次の構造キーで作り直される）。
                    if (!model.ObjectGroups.Contains(e.Group) || !ReferenceEquals(e.Group.GetStep(e.StepIndex), e.Step))
                        continue;

                    if (pending.Contains(e))
                    {
                        st.LastResult  = ModifierResult.Blocked;
                        st.LastMessage = "出力の編集が未確定なので流していない（確定か破棄をする）";
                        st.LastPass    = pass.Sequence;
                        pass.Blocked++;
                        pass.Lines.Add($"{e.Label} {e.Step.Action}: 止めた（出力の編集が未確定）");
                        continue;
                    }

                    string failedUp = BlockedBy(e);
                    if (failedUp != null)
                    {
                        st.LastResult  = ModifierResult.Blocked;
                        st.LastMessage = $"{failedUp}ので流していない";
                        st.LastPass    = pass.Sequence;
                        pass.Blocked++;
                        pass.Lines.Add($"{e.Label} {e.Step.Action}: 止めた（{failedUp}）");
                        continue;
                    }

                    bool ok;
                    string err, note;
                    try { ok = run(e, out err, out note); }
                    catch (Exception ex) { ok = false; err = ex.Message; note = null; }

                    TakeInputs(st, e);
                    st.Dirty    = false;
                    st.LastPass = pass.Sequence;

                    if (ok)
                    {
                        RecordForwardOutputs(e);
                        st.LastResult  = ModifierResult.Ok;
                        st.LastMessage = note ?? "";
                        pass.Ran++;
                        pass.Lines.Add($"{e.Label} {e.Step.Action}: 作り直した");
                        foreach (var d in e.Downstream) MarkIfChanged(states, d);
                    }
                    else
                    {
                        st.LastResult  = ModifierResult.Failed;
                        st.LastMessage = err ?? "作り直しに失敗しました";
                        pass.Failed++;
                        pass.Lines.Add($"{e.Label} {e.Step.Action}: 失敗（{st.LastMessage}）");
                        blockedBy.Clear();
                    }
                }
            }
            finally
            {
                _evaluating = false;
            }

            _lastPass.Remove(model);
            _lastPass.Add(model, pass);
            return pass;
        }

        /// <summary>
        /// コマンドがこの項目を明示的に作り直したあとに呼ぶ（引数の書き換え・作り直し・配置元の差し替え）。
        /// 項目の入力の署名を控えて印を下ろし、出力が変わった下流に印を立てる（流すのは EvaluatePending）。
        /// </summary>
        public static void MarkRebuilt(ModelContext model, ObjectGroupStep step)
        {
            if (model == null || step == null) return;
            var states = States(model);
            foreach (var e in GetGraph(model))
            {
                if (!ReferenceEquals(e.Step, step)) continue;
                if (e.StateId != 0UL)
                {
                    if (!states.TryGetValue(e.StateId, out var st)) { st = new State(); states[e.StateId] = st; }
                    TakeInputs(st, e);
                    st.Dirty       = false;
                    st.LastResult  = ModifierResult.Rebuilt;
                    st.LastMessage = "コマンドが明示的に作り直した";
                    RecordForwardOutputs(e);
                }
                foreach (var d in e.Downstream) MarkIfChanged(states, d);
                return;
            }
        }

        // ================================================================
        // 解除（「適用」）
        // ================================================================

        /// <summary>
        /// グループの全項目を解除する前に呼ぶ。処理器のある項目は Release を呼び、
        /// 全項目の控えを捨てる。処理器を 1 つでも呼んだら true（呼び出し側は一覧の Undo を積む）。
        /// グループ自体はモデルから外さない（呼び出し側が外す）。
        /// </summary>
        public static bool ReleaseGroup(ModelContext model, ObjectGroup group)
        {
            if (model == null || group == null) return false;

            bool released = false;
            var states = States(model);
            foreach (var e in CollectEntries(model))
            {
                if (!ReferenceEquals(e.Group, group)) continue;

                if (_handlers.TryGetValue(e.Step.Action, out var h))
                {
                    h.Release(model, e);
                    released = true;
                }
                if (e.StateId != 0UL) states.Remove(e.StateId);
            }
            return released;
        }

        /// <summary>出力を持つ項目 1 つの在りか。</summary>
        public struct OutputOwner
        {
            public ObjectGroup     Group;
            public int             StepIndex;
            public ObjectGroupStep Step;
        }

        /// <summary>
        /// この ObjectId をどれかのグループの実行項目の出力に持つか。
        /// 先に見つかった 1 つを返す（グループの並び・項目の並び順）。
        /// </summary>
        public static bool TryFindOutputOwner(ModelContext model, ulong objectId, out OutputOwner owner)
        {
            owner = default;
            if (model?.ObjectGroups == null || objectId == 0UL) return false;

            foreach (var g in model.ObjectGroups)
            {
                if (g?.Steps == null) continue;
                for (int si = 0; si < g.Steps.Count; si++)
                {
                    var s = g.Steps[si];
                    if (s == null || !s.IsExecutable) continue;
                    if (!s.ContainsOutput(objectId)) continue;
                    owner = new OutputOwner { Group = g, StepIndex = si, Step = s };
                    return true;
                }
            }
            return false;
        }

        // ================================================================
        // きっかけ 2：頂点ドラッグ中の位置の同期
        // ================================================================

        /// <summary>
        /// mc を入力に持つ項目へ、ドラッグ中の位置の変化を知らせる。
        /// 項目の一覧は控えてあるグラフを使う（毎フレーム集め直さない）。
        /// </summary>
        public static void SyncFromInput(ModelContext model, MeshContext mc, Action<MeshContext> upload)
        {
            if (model == null || mc?.MeshObject == null) return;
            var list = CachedGraph(model);
            if (list == null) return;
            var states = States(model);

            foreach (var e in list)
            {
                if (!e.HasInput(mc)) continue;

                if (e.FollowsDrag && _handlers.TryGetValue(e.Step.Action, out var h))
                {
                    h.SyncDrag(model, e, mc, upload);
                }
                else if (e.StateId != 0UL && states.TryGetValue(e.StateId, out var st))
                {
                    // 追随しない項目は印だけ。署名はドラッグの毎フレームでは取らない。
                    // 流すのは頂点ドラッグを離したとき・コマンドの終わり（EvaluatePending）。
                    st.Dirty = true;
                }
            }
        }
    }
}
