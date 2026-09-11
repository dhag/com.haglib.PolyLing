// PLDataStore.cs
// コマンドが返した実データの置き場。ModelContext が 1 本持つ名前付きの辞書。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【なぜ回線ではなくモデルへ置くか】
//   既存コマンドは「番号は入口で 1 個、結果は選択状態へ」という流儀で通っている。
//   advancedSelect は種の頂点を 1 個受けて結果を選択へ入れ、
//   moveSelectedVertices は動かす頂点を指定しない。
//   戻り値もこれにそろえる。量のあるものはここへ書き、CommandResult.Data には
//   名前と件数だけを載せる。呼び出し側は次のコマンドへ名前を渡す。
//   名前で後から引き直す流儀は既にある（PanelCommand.cs の AcquireSetName）。
//
// 【種類は 3 つだけ】
//   IndexSet … 頂点／辺／面／線の集合。中身は PartsSelectionSet をそのまま使う。
//               MeshContext.PartsSelectionSetList と同じ型なので、
//               選択へ流し込む経路（MeshContext.LoadSelectionSet）が既にある。
//   LoopSet  … ループの並び。1 本ぶんが頂点列と重心。
//               BridgeAutoPairOps.CollectHoles の戻りをそのまま入れられる形。
//   ValueSet … 名前と値の対。数値または文字列。頂点数・面数・寸法など。
//
//   任意の JSON は入れない。入れると読む側が型を決められず、
//   PanelCommandSchema が出力スキーマを組み立てられなくなる。
//   足りなくなったら種類を増やす。
//
// 【対象の指し方】
//   MasterIndex は MeshContextList の位置。-1 はモデル全体を指す。
//   ObjectId は同じ対象の安定 ID。0 は未設定。並べ替えの後は ObjectId で引き直す。
//
// 【同名のとき】
//   Put は同名の項目を置き換える。並び位置は元のまま保つ。
//   自動採番は GenerateUniqueName を使う。規則は ModelContext の
//   GenerateUniqueMeshSelectionSetName / GenerateUniqueObjectGroupName と同じ。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Selection;

namespace Poly_Ling.Data
{
    /// <summary>PLDataEntry が持つ中身の種類。</summary>
    public enum PLDataKind
    {
        /// <summary>未設定。</summary>
        None = 0,

        /// <summary>頂点／辺／面／線の集合。</summary>
        IndexSet = 1,

        /// <summary>ループの並び。1 本が頂点列と重心。</summary>
        LoopSet = 2,

        /// <summary>名前と値の対。</summary>
        ValueSet = 3,
    }

    /// <summary>ループ 1 本。頂点列と重心を持つ。</summary>
    public sealed class PLDataLoop
    {
        /// <summary>ループを構成する頂点番号。対象オブジェクト内の番号。</summary>
        public List<int> Vertices { get; set; } = new List<int>();

        /// <summary>重心。ワールド座標。</summary>
        public Vector3 Centroid { get; set; }

        public PLDataLoop() { }

        public PLDataLoop(IEnumerable<int> vertices, Vector3 centroid)
        {
            if (vertices != null) Vertices.AddRange(vertices);
            Centroid = centroid;
        }

        /// <summary>頂点数。</summary>
        public int Count => Vertices?.Count ?? 0;
    }

    /// <summary>名前と値 1 組。数値か文字列のどちらかを持つ。</summary>
    public struct PLDataValue
    {
        /// <summary>値の名前。</summary>
        public string Key;

        /// <summary>数値。IsText が true のときは読まない。</summary>
        public double Number;

        /// <summary>文字列。IsText が false のときは読まない。</summary>
        public string Text;

        /// <summary>文字列として持っているか。</summary>
        public bool IsText;

        /// <summary>数値の値を作る。</summary>
        public static PLDataValue Num(string key, double value)
            => new PLDataValue { Key = key ?? "", Number = value, Text = null, IsText = false };

        /// <summary>文字列の値を作る。</summary>
        public static PLDataValue Str(string key, string value)
            => new PLDataValue { Key = key ?? "", Number = 0.0, Text = value ?? "", IsText = true };
    }

    /// <summary>辞書の項目 1 件。</summary>
    public sealed class PLDataEntry
    {
        /// <summary>項目名。戻り値として返すのはこれ。</summary>
        public string Name { get; set; } = "";

        /// <summary>中身の種類。</summary>
        public PLDataKind Kind { get; set; } = PLDataKind.None;

        /// <summary>作成日時。</summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>作った側の名乗り。コマンドの action 名を入れる。空でもよい。</summary>
        public string Source { get; set; } = "";

        /// <summary>対象の位置。MeshContextList の索引。-1 = モデル全体。</summary>
        public int MasterIndex { get; set; } = -1;

        /// <summary>同じ対象の安定 ID。0 = 未設定。</summary>
        public ulong ObjectId { get; set; }

        /// <summary>Kind が IndexSet のときの中身。それ以外は null。</summary>
        public PartsSelectionSet IndexSet { get; set; }

        /// <summary>Kind が LoopSet のときの中身。それ以外は null。</summary>
        public List<PLDataLoop> Loops { get; set; }

        /// <summary>Kind が ValueSet のときの中身。それ以外は null。</summary>
        public List<PLDataValue> Values { get; set; }

        // ================================================================
        // 要約
        // ================================================================

        /// <summary>件数。IndexSet は要素の合計、LoopSet はループ数、ValueSet は値の数。</summary>
        public int Count
        {
            get
            {
                switch (Kind)
                {
                    case PLDataKind.IndexSet: return IndexSet?.TotalCount ?? 0;
                    case PLDataKind.LoopSet:  return Loops?.Count ?? 0;
                    case PLDataKind.ValueSet: return Values?.Count ?? 0;
                    default: return 0;
                }
            }
        }

        /// <summary>表示用の要約。</summary>
        public string Summary
        {
            get
            {
                switch (Kind)
                {
                    case PLDataKind.IndexSet: return IndexSet?.Summary ?? "(empty)";
                    case PLDataKind.LoopSet:  return $"Loop:{Loops?.Count ?? 0}";
                    case PLDataKind.ValueSet: return $"Value:{Values?.Count ?? 0}";
                    default: return "(none)";
                }
            }
        }

        // ================================================================
        // 生成
        // ================================================================

        /// <summary>頂点／辺／面／線の集合として作る。</summary>
        public static PLDataEntry FromIndexSet(
            string name, PartsSelectionSet set,
            int masterIndex = -1, ulong objectId = 0UL, string source = null)
        {
            return new PLDataEntry
            {
                Name        = name ?? "",
                Kind        = PLDataKind.IndexSet,
                Source      = source ?? "",
                MasterIndex = masterIndex,
                ObjectId    = objectId,
                IndexSet    = set ?? new PartsSelectionSet(name ?? ""),
            };
        }

        /// <summary>ループの並びとして作る。</summary>
        public static PLDataEntry FromLoops(
            string name, IEnumerable<PLDataLoop> loops,
            int masterIndex = -1, ulong objectId = 0UL, string source = null)
        {
            var list = new List<PLDataLoop>();
            if (loops != null)
            {
                foreach (var l in loops)
                    if (l != null) list.Add(l);
            }

            return new PLDataEntry
            {
                Name        = name ?? "",
                Kind        = PLDataKind.LoopSet,
                Source      = source ?? "",
                MasterIndex = masterIndex,
                ObjectId    = objectId,
                Loops       = list,
            };
        }

        /// <summary>名前と値の対として作る。</summary>
        public static PLDataEntry FromValues(
            string name, IEnumerable<PLDataValue> values,
            int masterIndex = -1, ulong objectId = 0UL, string source = null)
        {
            var list = new List<PLDataValue>();
            if (values != null)
            {
                foreach (var v in values) list.Add(v);
            }

            return new PLDataEntry
            {
                Name        = name ?? "",
                Kind        = PLDataKind.ValueSet,
                Source      = source ?? "",
                MasterIndex = masterIndex,
                ObjectId    = objectId,
                Values      = list,
            };
        }

        /// <summary>ValueSet から名前で値を引く。無ければ false。</summary>
        public bool TryGetValue(string key, out PLDataValue value)
        {
            value = default;
            if (Values == null || string.IsNullOrEmpty(key)) return false;

            for (int i = 0; i < Values.Count; i++)
            {
                if (Values[i].Key == key) { value = Values[i]; return true; }
            }
            return false;
        }
    }

    /// <summary>
    /// 名前付きの結果辞書。ModelContext が 1 本持つ。
    /// 並びは追加した順。Put は同名を置き換え、位置は保つ。
    /// </summary>
    public sealed class PLDataStore
    {
        private readonly List<PLDataEntry> _entries = new List<PLDataEntry>();

        /// <summary>項目の一覧。追加順。</summary>
        public IReadOnlyList<PLDataEntry> Entries => _entries;

        /// <summary>項目数。</summary>
        public int Count => _entries.Count;

        /// <summary>空か。</summary>
        public bool IsEmpty => _entries.Count == 0;

        /// <summary>名前で引く。無ければ null。</summary>
        public PLDataEntry Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e != null && e.Name == name) return e;
            }
            return null;
        }

        /// <summary>その名前の項目があるか。</summary>
        public bool Contains(string name) => Find(name) != null;

        /// <summary>
        /// 項目を入れる。同名があれば同じ位置で置き換える。
        /// 名前が空の項目は入れない（引けなくなるため）。入れた項目を返す。
        /// </summary>
        public PLDataEntry Put(PLDataEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Name)) return null;

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e != null && e.Name == entry.Name)
                {
                    _entries[i] = entry;
                    return entry;
                }
            }

            _entries.Add(entry);
            return entry;
        }

        /// <summary>名前で消す。消したら true。</summary>
        public bool Remove(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e != null && e.Name == name)
                {
                    _entries.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>全部消す。</summary>
        public void Clear() => _entries.Clear();

        /// <summary>
        /// 一意な名前を作る。規則は ModelContext.GenerateUniqueObjectGroupName と同じ。
        /// </summary>
        public string GenerateUniqueName(string baseName = "Data")
        {
            if (string.IsNullOrEmpty(baseName)) baseName = "Data";

            string name = baseName;
            int counter = 1;
            while (Find(name) != null)
            {
                name = $"{baseName}_{counter}";
                counter++;
            }
            return name;
        }

        /// <summary>種類で絞った名前の一覧。</summary>
        public List<string> NamesOf(PLDataKind kind)
        {
            var names = new List<string>();
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e != null && e.Kind == kind) names.Add(e.Name);
            }
            return names;
        }

        /// <summary>読み込みで丸ごと差し替えるときに使う。null 要素と無名は捨てる。</summary>
        public void ReplaceAll(IEnumerable<PLDataEntry> entries)
        {
            _entries.Clear();
            if (entries == null) return;

            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.Name)) continue;
                Put(e);
            }
        }
    }
}
