// ObjectGroup.cs
// 「入力ソース＋生成パラメータ＋出力先」をコマンド列（ステップ）で持つもの。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【何のためにあるか】
//   藤壺・パイプ・フリル・オブジェクトブレンドは、どれも
//   「別のオブジェクトから何かを取り込み、パラメータを掛けて、別のオブジェクトを作る」。
//   これまで入力（基準ベルト・断面プロファイル・ブレンドソース）はパネルの
//   private フィールドにしか無く、生成パラメータも生成後は残らなかったため、
//   ソースを直しても出力先は古いままだった。作り直すには手順ごとやり直すしかない。
//   その関係をモデルに残すのがこれ。
//
// 【ステップ】
//   1 グループは 1 つ以上のステップ（ObjectGroupStep）を並び順に持つ。
//   ステップ 1 つが生成コマンド 1 つで、出力は複数ありうる
//   （はしごから作る揺れボーンの鎖は 1 回の実行で何本もできる）。
//
//   ステップの間に依存グラフは持たない。実行の順はリストの並びそのもの。
//   ステップ間の参照は ObjectId で成立する（前のステップの出力は実在するので
//   ObjectId が振られており、冪等なら作り直されずに同じ ID が残る）。
//   「ステップ n の出力 k」という相対参照は持たない。
//
//   ステップが 1 つのグループは、これまでの ObjectGroup と同じもの。
//   Action / Args / MeshRefIds / OutputObjectId はステップ 0 への窓口として
//   残してあり、既存の呼び出しはそのまま通る。二重には持たない
//   （フィールドとステップの両方に置くと必ず食い違う）。
//
// 【参照は ObjectId で持つ】
//   索引はリストの挿入・削除・並べ替えでずれ、名前はリネームで切れる。
//   ObjectId（MeshContext.ObjectId）は位置非依存で、Undo/Redo の復元でも保たれ
//   （MeshContextCloneOps の StateSnapshot）、複製では新しい値が振られる
//   （ModelContext.Add / Insert）。よって「複製物は自動的に非メンバー」になる。
//   これは MeshContext 側にフィールドを足す案では作れない性質で、
//   参照はグループ → オブジェクトの片方向だけに保つこと。
//
// 【生成パラメータの持ち方】
//   PanelCommandFactory.ToArgs で文字列の対にし、Create で戻す。
//   往復は PanelCommandFactoryAudit が検査済みで、新しい直列化機構は作らない。
//   Args のうち「索引で描画オブジェクトを指す」ものだけは、再構築のときに
//   ステップの MeshRefIds から引き直す（PLParam.IsMeshRef）。
//
// 【一時グループ】
//   ModelContext.ObjectGroups へ入れなければ、保存・転送・Undo・不変条件検査の
//   どれにも触れない。「ちょっと作るだけ」はグループを組んで実行し、そのまま
//   参照を捨てる。捨てたグループは復元できない（生成物からパラメータは逆算できない）。
//
// 【手順の知識として使う】
//   同じ型を 2 通りに使う。
//     実体付き … ModelContext.ObjectGroups に入るもの。ステップの参照が
//                実在の ObjectId を指し、作り直しで出力先へ書き戻す。今までどおり。
//     手本     … ScenarioLibrary に入るもの。モデルに属さず、参照は空でよい。
//                目的・前提・成功条件・由来を持ち、元を残したまま派生を増やす。
//   両者の違いは置き場と参照の埋まり方だけで、型は分けない。分けると
//   ステップ列の器が 2 つになり、CaptureStep の出力先も 2 つになる。



using System;
using System.Collections.Generic;

namespace Poly_Ling.Data
{
    /// <summary>
    /// ステップの種別。
    ///
    /// 【なぜ実行しない段を同じリストに置くか】
    ///   手順の知識には「なぜこの段が要るか」「ここは対象を見て人が決める」
    ///   「この段のあとに何を確かめるか」が混ざる。別のリストに分けると
    ///   並び順の対応を二重に管理することになり、必ずずれる。
    ///   実行側（ObjectGroupOps.BuildCommand /
    ///   PlayerCommandDispatcher.RunObjectGroupStep）が Command 以外を飛ばす。
    /// </summary>
    public enum ObjectGroupStepKind
    {
        /// <summary>生成コマンドを実行する段。既定。</summary>
        Command = 0,

        /// <summary>理由・注意・設計意図。実行しない。</summary>
        Note = 1,

        /// <summary>人または AI への作業指示。実行しない。</summary>
        Instruction = 2,

        /// <summary>実行後に確かめること。実行しない。</summary>
        Observe = 3,
    }

    /// <summary>
    /// グループの由来。どれを元に、何を変えて作ったか。
    ///
    /// 元を書き換えずに派生を増やす使い方をするので、たどれるようにしておく。
    /// 全部空でもよい（手で組んだ最初の 1 件）。
    /// </summary>
    [Serializable]
    public class ObjectGroupProvenance
    {
        /// <summary>元にしたグループの名前。空 = 元がない。</summary>
        public string ParentName = "";

        /// <summary>元から何を変えたか。</summary>
        public string ChangeSummary = "";

        /// <summary>作った者。人の名前でも AI の名でもよい。</summary>
        public string CreatedBy = "";

        /// <summary>3 つとも空か。</summary>
        public bool IsEmpty
            => string.IsNullOrEmpty(ParentName)
            && string.IsNullOrEmpty(ChangeSummary)
            && string.IsNullOrEmpty(CreatedBy);

        public ObjectGroupProvenance Clone() => new ObjectGroupProvenance
        {
            ParentName    = ParentName,
            ChangeSummary = ChangeSummary,
            CreatedBy     = CreatedBy,
        };
    }

    /// <summary>
    /// グループの 1 ステップ。生成コマンド 1 つぶんのパラメータと出力先。
    /// </summary>
    [Serializable]
    public class ObjectGroupStep
    {
        /// <summary>
        /// この段を指す名前。グループ内で一意。
        ///
        /// 【なぜ番号で指さないか】
        ///   「3 番目の段を差し替える」は、前に 1 段挿すだけで別の段を指す。
        ///   段を足す・消す・並べ替える使い方をするので、位置ではなく
        ///   この ID で指す。読み込み時に空なら ObjectGroup.EnsureElementIds が振る。
        /// </summary>
        public string ElementId = "";

        /// <summary>段の種別。既定は実行する段。</summary>
        public ObjectGroupStepKind Kind = ObjectGroupStepKind.Command;

        /// <summary>この段が要る理由。空でもよい。</summary>
        public string Purpose = "";

        /// <summary>
        /// 生成コマンドの action 名（PanelCommandFactory.ActionOf の結果）。
        /// 例: "createFrill" / "createPipe" / "createPlaceObject" / "applyBlend"。
        /// </summary>
        public string Action = "";

        /// <summary>
        /// 生成コマンドのパラメータ（PanelCommandFactory.ToArgs の結果）。
        /// modelIndex は含まない（封筒側の値）。
        /// </summary>
        public Dictionary<string, string> Args = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// 索引で描画オブジェクトを指す Args のキー → その ObjectId 列。
        ///
        /// どのキーがこれに当たるかは PanelCommandFactory.MeshRefKeys が
        /// PLParam(IsMeshRef) から決める。コマンド種別ごとの手書き表は持たない。
        /// 並び順は Args の索引配列の並びと 1 対 1 に対応させること
        /// （藤壺の配置元の順・ブレンドのソース順がそのまま効く）。
        ///
        /// 0 は「そのときも索引が無かった」（例: AddTargetIndex = -1）を表す。
        /// 再構築では 0 の要素を -1 のまま書き戻す。
        /// </summary>
        public Dictionary<string, List<ulong>> MeshRefIds
            = new Dictionary<string, List<ulong>>(StringComparer.Ordinal);

        /// <summary>
        /// このステップの出力先の ObjectId 列。空 = まだ作っていない / 失われた。
        /// 図形生成は 1 件、揺れボーンの鎖は鎖の本数ぶん入る。
        /// 並びは作った順（鎖 0 の根元、鎖 1 の根元 …）。
        /// </summary>
        public List<ulong> OutputObjectIds = new List<ulong>();

        /// <summary>実行する段か（Kind が Command）。</summary>
        public bool IsExecutable => Kind == ObjectGroupStepKind.Command;

        /// <summary>
        /// 再構築に必要なものが揃っているか。
        /// 実行しない段（Note / Instruction / Observe）は action を持たないので常に真。
        /// </summary>
        public bool IsValid
            => !IsExecutable || (!string.IsNullOrEmpty(Action) && Args != null);

        /// <summary>出力先を 1 つでも持っているか。</summary>
        public bool HasOutput
        {
            get
            {
                if (OutputObjectIds == null) return false;
                for (int i = 0; i < OutputObjectIds.Count; i++)
                    if (OutputObjectIds[i] != 0UL) return true;
                return false;
            }
        }

        /// <summary>
        /// 出力先の先頭。無ければ 0。
        /// 1 ステップ 1 出力だったころの OutputObjectId に当たる。
        /// </summary>
        public ulong FirstOutputId
        {
            get
            {
                if (OutputObjectIds == null) return 0UL;
                for (int i = 0; i < OutputObjectIds.Count; i++)
                    if (OutputObjectIds[i] != 0UL) return OutputObjectIds[i];
                return 0UL;
            }
        }

        /// <summary>
        /// 出力先の先頭を差し替える。0 を渡すと出力先を消す。
        /// 複数の出力を持つステップへ 0 以外を書くと、先頭だけが変わる。
        /// </summary>
        public void SetFirstOutput(ulong objectId)
        {
            if (OutputObjectIds == null) OutputObjectIds = new List<ulong>();

            if (objectId == 0UL) { OutputObjectIds.Clear(); return; }

            if (OutputObjectIds.Count == 0) OutputObjectIds.Add(objectId);
            else                            OutputObjectIds[0] = objectId;
        }

        /// <summary>この ObjectId を出力先に含むか。</summary>
        public bool ContainsOutput(ulong objectId)
        {
            if (objectId == 0UL || OutputObjectIds == null) return false;
            for (int i = 0; i < OutputObjectIds.Count; i++)
                if (OutputObjectIds[i] == objectId) return true;
            return false;
        }

        /// <summary>Args の 1 件を読む。無ければ既定値。</summary>
        public string GetArg(string key, string fallback = null)
            => (Args != null && key != null && Args.TryGetValue(key, out var v)) ? v : fallback;

        /// <summary>Args の 1 件を書く。</summary>
        public void SetArg(string key, string value)
        {
            if (Args == null) Args = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(key)) return;
            Args[key] = value ?? "";
        }

        /// <summary>キーに対応する ObjectId 列を読む。無ければ空。</summary>
        public List<ulong> GetMeshRefIds(string key)
            => (MeshRefIds != null && key != null && MeshRefIds.TryGetValue(key, out var v) && v != null)
                ? v : new List<ulong>();

        /// <summary>キーに対応する ObjectId 列を書く。</summary>
        public void SetMeshRefIds(string key, List<ulong> ids)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (MeshRefIds == null)
                MeshRefIds = new Dictionary<string, List<ulong>>(StringComparer.Ordinal);
            MeshRefIds[key] = ids ?? new List<ulong>();
        }

        /// <summary>MeshRefIds をキー順に並べて返す（保存・転送の並びを固定するため）。</summary>
        public List<KeyValuePair<string, List<ulong>>> SortedMeshRefIds()
        {
            var list = new List<KeyValuePair<string, List<ulong>>>();
            if (MeshRefIds == null) return list;
            foreach (var kv in MeshRefIds) list.Add(kv);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }

        /// <summary>
        /// Args をキー順に並べて返す。
        /// 保存・転送・ダイジェストで並びを固定するために使う
        /// （Dictionary の列挙順は保証されないため、そのまま書くと差分が出る）。
        /// </summary>
        public List<KeyValuePair<string, string>> SortedArgs()
        {
            var list = new List<KeyValuePair<string, string>>();
            if (Args == null) return list;
            foreach (var kv in Args) list.Add(kv);
            list.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return list;
        }

        public ObjectGroupStep Clone()
        {
            var c = new ObjectGroupStep
            {
                ElementId       = ElementId,
                Kind            = Kind,
                Purpose         = Purpose,
                Action          = Action,
                Args            = new Dictionary<string, string>(StringComparer.Ordinal),
                MeshRefIds      = new Dictionary<string, List<ulong>>(StringComparer.Ordinal),
                OutputObjectIds = OutputObjectIds != null
                    ? new List<ulong>(OutputObjectIds) : new List<ulong>(),
            };
            if (Args != null)
                foreach (var kv in Args) c.Args[kv.Key] = kv.Value;
            if (MeshRefIds != null)
                foreach (var kv in MeshRefIds)
                    c.MeshRefIds[kv.Key] = kv.Value != null ? new List<ulong>(kv.Value) : new List<ulong>();
            return c;
        }

        public override string ToString()
            => $"ObjectGroupStep[{Kind}:{ElementId}:{Action}] out={(OutputObjectIds?.Count ?? 0)}";
    }

    /// <summary>入力ソース・生成パラメータ・出力先をまとめた 1 件。</summary>
    [Serializable]
    public class ObjectGroup
    {
        // ================================================================
        // 識別
        // ================================================================

        /// <summary>グループ名。モデル内で一意にする。</summary>
        public string Name = "";

        /// <summary>
        /// 実行するコマンド列。並び順がそのまま実行順。
        /// 必ず 1 件以上ある（新しく作った時点で 1 件）。
        /// </summary>
        public List<ObjectGroupStep> Steps = new List<ObjectGroupStep>();

        // ================================================================
        // 意味情報
        //
        // 【何を手で書くか】
        //   action・引数・型・戻り値は属性と実装から取れる。ここに置くのは
        //   機械的に取れないものだけ。何のための手順か、いつ使えるか、
        //   終わったとき何が言えれば成功か、どれを元にどこを変えたか。
        //   全部空でよい。既存の保存データは空のまま読める。
        // ================================================================

        /// <summary>この手順で達成したいこと。空でもよい。</summary>
        public string Goal = "";

        /// <summary>使う前に満たしているべきこと。1 件 1 行。</summary>
        public List<string> Preconditions = new List<string>();

        /// <summary>終わったときに確かめること。1 件 1 行。</summary>
        public List<string> SuccessCriteria = new List<string>();

        /// <summary>探すための札。</summary>
        public List<string> Tags = new List<string>();

        /// <summary>どれを元に、何を変えて作ったか。null にはしない。</summary>
        public ObjectGroupProvenance Provenance = new ObjectGroupProvenance();

        // ================================================================
        // 参照（すべて MeshContext.ObjectId）
        // ================================================================

        /// <summary>
        /// 退避オブジェクトの ObjectId。0 = なし。
        /// 再構築で作り直す前の出力先を残したいときに使う。
        /// 退避は通常の複製なので Vertex.Id / PartsId / SubId は保たれる。
        /// </summary>
        public ulong StashObjectId = 0;

        // ================================================================
        // 更新の判定
        // ================================================================

        /// <summary>
        /// 前回ビルドした時点でのソースのダイジェスト。
        /// 現在の値と食い違えば「ソースが変わった」。
        /// 毎フレーム比べてはならない（ObjectGroupOps.IsStale の注記を参照）。
        /// </summary>
        public string SourceDigest = "";

        /// <summary>
        /// ソースが変わっていたら再構築まで自動で行うか。既定 false。
        /// false のときは印を立てるだけで、作り直しは明示操作で行う。
        ///
        /// 【いつ流れるか】
        ///   ソースの頂点を書き換える操作のあとで、その操作の側から呼ばれる。
        ///   今の呼び出し元はスキンド化の 2 経路
        ///   （SkinKindConverter.ToSkinned / MeshFilterToSkinnedConverter.Execute）。
        ///   スキンド化は頂点をワールドへ焼き直すので、それを取り込むグループの
        ///   ダイジェストが変わり、IsStale が立つ。
        ///
        /// 【編集のたびには流れない】
        ///   毎フレームの監視や、頂点を触るたびのフックは置かない
        ///   （ObjectGroupOps.IsStale はソースの全頂点を走査する）。
        ///   流す契機は呼び出し側が明示的に持つ。
        ///
        /// 【流れるのはソースに含むグループだけ】
        ///   出力に含むグループは流さない。出力をスキンド化したあとに作り直すと、
        ///   生成コマンドは MeshFilter 前提の空間で作り直すので頂点を壊す。
        /// </summary>
        public bool AutoUpdate = false;

        /// <summary>作成日時。</summary>
        public DateTime CreatedAt = DateTime.Now;

        // ================================================================
        // コンストラクタ
        // ================================================================

        public ObjectGroup()
        {
            AddStep(new ObjectGroupStep());
        }

        public ObjectGroup(string name) : this() { Name = name ?? ""; }

        // ================================================================
        // ステップ
        // ================================================================

        /// <summary>ステップの数。</summary>
        public int StepCount => Steps?.Count ?? 0;

        /// <summary>
        /// ステップ 0。無ければ空のステップを作って返す。
        /// 「1 ステップだったころの窓口」がここを通る。
        /// </summary>
        public ObjectGroupStep Step0
        {
            get
            {
                if (Steps == null) Steps = new List<ObjectGroupStep>();
                if (Steps.Count == 0) AddStep(new ObjectGroupStep());
                return Steps[0];
            }
        }

        /// <summary>番号でステップを読む。範囲外は null。</summary>
        public ObjectGroupStep GetStep(int index)
            => (Steps != null && index >= 0 && index < Steps.Count) ? Steps[index] : null;

        /// <summary>末尾へステップを 1 つ足す。ElementId が空なら振る。</summary>
        public ObjectGroupStep AddStep(ObjectGroupStep step)
        {
            if (step == null) return null;
            if (Steps == null) Steps = new List<ObjectGroupStep>();
            Steps.Add(step);
            if (string.IsNullOrEmpty(step.ElementId)) step.ElementId = NextElementId();
            return step;
        }

        /// <summary>ElementId でステップを引く。無ければ null。</summary>
        public ObjectGroupStep FindStep(string elementId)
        {
            int i = IndexOfStep(elementId);
            return i >= 0 ? Steps[i] : null;
        }

        /// <summary>ElementId でステップの位置を引く。無ければ -1。</summary>
        public int IndexOfStep(string elementId)
        {
            if (Steps == null || string.IsNullOrEmpty(elementId)) return -1;
            for (int i = 0; i < Steps.Count; i++)
                if (Steps[i] != null && string.Equals(Steps[i].ElementId, elementId, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        /// <summary>
        /// ElementId が空のステップへ振る。既にある ID とは重ならない。
        ///
        /// ステップ導入より前の保存データには ID が無いので、読み込みの最後に呼ぶ。
        /// 既に入っている ID は書き換えない（書き換えると、その ID を指している
        /// 派生グループの参照が切れる）。
        /// </summary>
        public void EnsureElementIds()
        {
            if (Steps == null) return;

            var used = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Steps.Count; i++)
            {
                var s = Steps[i];
                if (s == null || string.IsNullOrEmpty(s.ElementId)) continue;

                // 重複していたら後ろの方を空に戻して振り直す。
                if (!used.Add(s.ElementId)) s.ElementId = "";
            }

            for (int i = 0; i < Steps.Count; i++)
            {
                var s = Steps[i];
                if (s == null || !string.IsNullOrEmpty(s.ElementId)) continue;
                s.ElementId = NextElementId(used);
                used.Add(s.ElementId);
            }
        }

        /// <summary>まだ使っていない ElementId を 1 つ作る。</summary>
        private string NextElementId(HashSet<string> used = null)
        {
            if (used == null)
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                if (Steps != null)
                    foreach (var s in Steps)
                        if (s != null && !string.IsNullOrEmpty(s.ElementId)) used.Add(s.ElementId);
            }

            for (int n = 1; ; n++)
            {
                string id = "e" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!used.Contains(id)) return id;
            }
        }

        // ================================================================
        // ステップ 0 への窓口（1 ステップだったころの形）
        // ================================================================

        /// <summary>ステップ 0 の action 名。</summary>
        public string Action
        {
            get => Step0.Action;
            set => Step0.Action = value ?? "";
        }

        /// <summary>ステップ 0 のパラメータ。</summary>
        public Dictionary<string, string> Args
        {
            get => Step0.Args;
            set => Step0.Args = value ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>ステップ 0 の描画オブジェクト参照。</summary>
        public Dictionary<string, List<ulong>> MeshRefIds
        {
            get => Step0.MeshRefIds;
            set => Step0.MeshRefIds = value ?? new Dictionary<string, List<ulong>>(StringComparer.Ordinal);
        }

        /// <summary>ステップ 0 の出力先の先頭。0 = まだ作っていない / 失われた。</summary>
        public ulong OutputObjectId
        {
            get => Step0.FirstOutputId;
            set => Step0.SetFirstOutput(value);
        }

        /// <summary>ステップ 0 の Args の 1 件を読む。無ければ既定値。</summary>
        public string GetArg(string key, string fallback = null) => Step0.GetArg(key, fallback);

        /// <summary>ステップ 0 の Args の 1 件を書く。</summary>
        public void SetArg(string key, string value) => Step0.SetArg(key, value);

        /// <summary>ステップ 0 のキーに対応する ObjectId 列を読む。無ければ空。</summary>
        public List<ulong> GetMeshRefIds(string key) => Step0.GetMeshRefIds(key);

        /// <summary>ステップ 0 のキーに対応する ObjectId 列を書く。</summary>
        public void SetMeshRefIds(string key, List<ulong> ids) => Step0.SetMeshRefIds(key, ids);

        /// <summary>ステップ 0 の MeshRefIds をキー順に並べて返す。</summary>
        public List<KeyValuePair<string, List<ulong>>> SortedMeshRefIds() => Step0.SortedMeshRefIds();

        /// <summary>ステップ 0 の Args をキー順に並べて返す。</summary>
        public List<KeyValuePair<string, string>> SortedArgs() => Step0.SortedArgs();

        // ================================================================
        // 参照の集約（全ステップ）
        // ================================================================

        /// <summary>
        /// 全ステップの出力先（0 を除き、重複なし）。並びはステップ順。
        /// </summary>
        public List<ulong> OutputObjectIds
        {
            get
            {
                var seen = new HashSet<ulong>();
                var list = new List<ulong>();
                if (Steps != null)
                {
                    for (int s = 0; s < Steps.Count; s++)
                    {
                        var ids = Steps[s]?.OutputObjectIds;
                        if (ids == null) continue;
                        for (int i = 0; i < ids.Count; i++)
                        {
                            if (ids[i] == 0UL) continue;
                            if (seen.Add(ids[i])) list.Add(ids[i]);
                        }
                    }
                }
                return list;
            }
        }

        /// <summary>この ObjectId をどれかのステップの出力先に含むか。</summary>
        public bool ContainsOutput(ulong objectId)
        {
            if (objectId == 0UL || Steps == null) return false;
            for (int s = 0; s < Steps.Count; s++)
                if (Steps[s] != null && Steps[s].ContainsOutput(objectId)) return true;
            return false;
        }

        /// <summary>
        /// 入力ソースの ObjectId（重複を除いたもの）。
        /// 全ステップの MeshRefIds に載っている全 ID から、出力先と退避を除いたもの。
        /// ダイジェストと UI 表示に使う。
        ///
        /// 前のステップの出力を後のステップが取り込む形では、その ID は
        /// 出力先なのでここには出ない。ダイジェストは「マクロが書き換えないもの」
        /// だけから作られる。
        /// </summary>
        public List<ulong> SourceObjectIds
        {
            get
            {
                var seen = new HashSet<ulong>();
                var list = new List<ulong>();
                if (Steps != null)
                {
                    for (int s = 0; s < Steps.Count; s++)
                    {
                        var refs = Steps[s]?.MeshRefIds;
                        if (refs == null) continue;

                        foreach (var kv in refs)
                        {
                            if (kv.Value == null) continue;
                            for (int i = 0; i < kv.Value.Count; i++)
                            {
                                ulong id = kv.Value[i];
                                if (id == 0UL) continue;
                                if (id == StashObjectId) continue;
                                if (ContainsOutput(id)) continue;
                                if (seen.Add(id)) list.Add(id);
                            }
                        }
                    }
                }
                list.Sort();
                return list;
            }
        }

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>再構築に必要なものが揃っているか。</summary>
        public bool IsValid
        {
            get
            {
                if (Steps == null || Steps.Count == 0) return false;
                for (int s = 0; s < Steps.Count; s++)
                    if (Steps[s] == null || !Steps[s].IsValid) return false;
                return true;
            }
        }

        /// <summary>どれかのステップが出力先を持っているか。</summary>
        public bool HasOutput
        {
            get
            {
                if (Steps == null) return false;
                for (int s = 0; s < Steps.Count; s++)
                    if (Steps[s] != null && Steps[s].HasOutput) return true;
                return false;
            }
        }

        /// <summary>退避を持っているか。</summary>
        public bool HasStash => StashObjectId != 0UL;

        /// <summary>入力ソースの件数。</summary>
        public int SourceCount => SourceObjectIds.Count;

        // ================================================================
        // 参照の操作
        // ================================================================

        /// <summary>この ObjectId をソースに含むか。</summary>
        public bool ContainsSource(ulong objectId)
        {
            if (objectId == 0UL || Steps == null) return false;
            if (objectId == StashObjectId) return false;
            if (ContainsOutput(objectId)) return false;

            for (int s = 0; s < Steps.Count; s++)
            {
                var refs = Steps[s]?.MeshRefIds;
                if (refs == null) continue;
                foreach (var kv in refs)
                {
                    if (kv.Value == null) continue;
                    for (int i = 0; i < kv.Value.Count; i++)
                        if (kv.Value[i] == objectId) return true;
                }
            }
            return false;
        }

        /// <summary>この ObjectId を出力先・退避・ソースのいずれかで指しているか。</summary>
        public bool References(ulong objectId)
        {
            if (objectId == 0UL) return false;
            if (ContainsOutput(objectId)) return true;
            if (StashObjectId == objectId) return true;
            return ContainsSource(objectId);
        }

        // ================================================================
        // クローン
        // ================================================================

        public ObjectGroup Clone()
        {
            var c = new ObjectGroup
            {
                Name          = Name,
                StashObjectId = StashObjectId,
                SourceDigest  = SourceDigest,
                AutoUpdate    = AutoUpdate,
                CreatedAt     = CreatedAt,
                Goal          = Goal,
                Steps         = new List<ObjectGroupStep>(),
            };

            c.Preconditions   = Preconditions   != null ? new List<string>(Preconditions)   : new List<string>();
            c.SuccessCriteria = SuccessCriteria != null ? new List<string>(SuccessCriteria) : new List<string>();
            c.Tags            = Tags            != null ? new List<string>(Tags)            : new List<string>();
            c.Provenance      = Provenance      != null ? Provenance.Clone() : new ObjectGroupProvenance();

            if (Steps != null)
                foreach (var s in Steps)
                    if (s != null) c.Steps.Add(s.Clone());
            if (c.Steps.Count == 0) c.AddStep(new ObjectGroupStep());
            return c;
        }

        public override string ToString()
            => $"ObjectGroup[{Name}] steps={StepCount} action={Action} "
             + $"src={SourceCount} out={OutputObjectIds.Count}";
    }
}
