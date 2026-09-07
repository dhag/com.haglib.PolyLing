// ObjectGroup.cs
// 「入力ソース＋生成パラメータ＋出力先」を 1 件にまとめたもの。
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
//   SourceObjectIds / OutputObjectId から引き直す（PLParam.IsMeshRef）。
//
// 【一時グループ】
//   ModelContext.ObjectGroups へ入れなければ、保存・転送・Undo・不変条件検査の
//   どれにも触れない。「ちょっと作るだけ」はグループを組んで実行し、そのまま
//   参照を捨てる。捨てたグループは復元できない（生成物からパラメータは逆算できない）。

using System;
using System.Collections.Generic;

namespace Poly_Ling.Data
{
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
        /// 生成コマンドの action 名（PanelCommandFactory.ActionOf の結果）。
        /// 例: "createFrill" / "createPipe" / "createPlaceObject" / "applyBlend"。
        /// </summary>
        public string Action = "";

        /// <summary>
        /// 生成コマンドのパラメータ（PanelCommandFactory.ToArgs の結果）。
        /// modelIndex は含まない（封筒側の値）。
        /// </summary>
        public Dictionary<string, string> Args = new Dictionary<string, string>(StringComparer.Ordinal);

        // ================================================================
        // 参照（すべて MeshContext.ObjectId）
        // ================================================================

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
        /// 入力ソースの ObjectId（重複を除いたもの）。
        /// MeshRefIds に載っている全 ID から、出力先と退避を除いたもの。
        /// ダイジェストと UI 表示に使う。
        /// </summary>
        public List<ulong> SourceObjectIds
        {
            get
            {
                var seen = new HashSet<ulong>();
                var list = new List<ulong>();
                if (MeshRefIds != null)
                {
                    foreach (var kv in MeshRefIds)
                    {
                        if (kv.Value == null) continue;
                        for (int i = 0; i < kv.Value.Count; i++)
                        {
                            ulong id = kv.Value[i];
                            if (id == 0UL) continue;
                            if (id == OutputObjectId || id == StashObjectId) continue;
                            if (seen.Add(id)) list.Add(id);
                        }
                    }
                }
                list.Sort();
                return list;
            }
        }

        /// <summary>出力先の ObjectId。0 = まだ作っていない / 失われた。</summary>
        public ulong OutputObjectId = 0;

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
        /// </summary>
        public bool AutoUpdate = false;

        /// <summary>作成日時。</summary>
        public DateTime CreatedAt = DateTime.Now;

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>再構築に必要なものが揃っているか。</summary>
        public bool IsValid => !string.IsNullOrEmpty(Action) && Args != null;

        /// <summary>出力先を持っているか。</summary>
        public bool HasOutput => OutputObjectId != 0UL;

        /// <summary>退避を持っているか。</summary>
        public bool HasStash => StashObjectId != 0UL;

        /// <summary>入力ソースの件数。</summary>
        public int SourceCount => SourceObjectIds.Count;

        // ================================================================
        // コンストラクタ
        // ================================================================

        public ObjectGroup() { }

        public ObjectGroup(string name) { Name = name ?? ""; }

        // ================================================================
        // 参照の操作
        // ================================================================

        /// <summary>この ObjectId をソースに含むか。</summary>
        public bool ContainsSource(ulong objectId)
        {
            if (objectId == 0UL || MeshRefIds == null) return false;
            if (objectId == OutputObjectId || objectId == StashObjectId) return false;
            foreach (var kv in MeshRefIds)
            {
                if (kv.Value == null) continue;
                for (int i = 0; i < kv.Value.Count; i++)
                    if (kv.Value[i] == objectId) return true;
            }
            return false;
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

        /// <summary>この ObjectId を出力先・退避・ソースのいずれかで指しているか。</summary>
        public bool References(ulong objectId)
        {
            if (objectId == 0UL) return false;
            if (OutputObjectId == objectId) return true;
            if (StashObjectId  == objectId) return true;
            return ContainsSource(objectId);
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

        // ================================================================
        // クローン
        // ================================================================

        public ObjectGroup Clone()
        {
            var c = new ObjectGroup
            {
                Name           = Name,
                Action         = Action,
                OutputObjectId = OutputObjectId,
                StashObjectId  = StashObjectId,
                SourceDigest   = SourceDigest,
                AutoUpdate     = AutoUpdate,
                CreatedAt      = CreatedAt,
                Args           = new Dictionary<string, string>(StringComparer.Ordinal),
                MeshRefIds     = new Dictionary<string, List<ulong>>(StringComparer.Ordinal),
            };
            if (Args != null)
                foreach (var kv in Args) c.Args[kv.Key] = kv.Value;
            if (MeshRefIds != null)
                foreach (var kv in MeshRefIds)
                    c.MeshRefIds[kv.Key] = kv.Value != null ? new List<ulong>(kv.Value) : new List<ulong>();
            return c;
        }

        public override string ToString()
            => $"ObjectGroup[{Name}] action={Action} src={SourceCount} out={OutputObjectId}";
    }
}
