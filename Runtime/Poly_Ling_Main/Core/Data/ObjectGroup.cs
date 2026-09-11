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

using System;
using System.Collections.Generic;

namespace Poly_Ling.Data
{
    /// <summary>
    /// グループの 1 ステップ。生成コマンド 1 つぶんのパラメータと出力先。
    /// </summary>
    [Serializable]
    public class ObjectGroupStep
    {
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

        /// <summary>再構築に必要なものが揃っているか。</summary>
        public bool IsValid => !string.IsNullOrEmpty(Action) && Args != null;

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
            => $"ObjectGroupStep[{Action}] out={(OutputObjectIds?.Count ?? 0)}";
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
            Steps.Add(new ObjectGroupStep());
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
                if (Steps.Count == 0) Steps.Add(new ObjectGroupStep());
                return Steps[0];
            }
        }

        /// <summary>番号でステップを読む。範囲外は null。</summary>
        public ObjectGroupStep GetStep(int index)
            => (Steps != null && index >= 0 && index < Steps.Count) ? Steps[index] : null;

        /// <summary>末尾へステップを 1 つ足す。</summary>
        public ObjectGroupStep AddStep(ObjectGroupStep step)
        {
            if (step == null) return null;
            if (Steps == null) Steps = new List<ObjectGroupStep>();
            Steps.Add(step);
            return step;
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
                Steps         = new List<ObjectGroupStep>(),
            };
            if (Steps != null)
                foreach (var s in Steps)
                    if (s != null) c.Steps.Add(s.Clone());
            if (c.Steps.Count == 0) c.Steps.Add(new ObjectGroupStep());
            return c;
        }

        public override string ToString()
            => $"ObjectGroup[{Name}] steps={StepCount} action={Action} "
             + $"src={SourceCount} out={OutputObjectIds.Count}";
    }
}
