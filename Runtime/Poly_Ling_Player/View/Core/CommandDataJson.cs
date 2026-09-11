// CommandDataJson.cs
// CommandResult.Data へ載せる JSON を組み立てる。
// Runtime/Poly_Ling_Player/View/Core/ に配置
//
// 【なぜ別ファイルか】
//   組み立てを PlayerCommandDispatcher へ直接書くと、既に 8000 行を超える
//   ファイルがさらに伸びる。ディスパッチャ側は 1 行で呼べる形にする。
//
// 【何を載せるか】
//   量のあるものは載せない。番号列・座標列は ModelContext.DataStore へ書き、
//   ここには名前・種類・件数・要約だけを載せる（PLDataStore.cs の冒頭注記）。
//
// 【JsonBuilder を使う理由】
//   応答の組み立ては PolyLingEditorControlServer が既に JsonBuilder で行っており、
//   Data は KeyRaw でそこへ差し込まれる。同じ器を使えば
//   エスケープ規則が 2 つに割れない。
//
// 【数値の書き方】
//   JsonBuilder は int / float / bool / string しか受けない。
//   double と ulong は RawValue に不変文化圏の文字列を渡す。
//   "G9" は JsonBuilder.Value(float) と同じ規則。

using System.Collections.Generic;
using System.Globalization;
using Poly_Ling.Data;
using Poly_Ling.Remote;

namespace Poly_Ling.Player
{
    /// <summary>CommandResult.Data 用の JSON オブジェクトを 1 個組み立てる。</summary>
    public sealed class CommandDataBuilder
    {
        private readonly JsonBuilder _jb = new JsonBuilder();
        private bool _closed;

        public CommandDataBuilder()
        {
            _jb.BeginObject();
        }

        /// <summary>整数を足す。</summary>
        public CommandDataBuilder Int(string key, int value)
        {
            if (!_closed) _jb.KeyValue(key, value);
            return this;
        }

        /// <summary>実数を足す。</summary>
        public CommandDataBuilder Num(string key, double value)
        {
            if (!_closed)
                _jb.KeyRaw(key, value.ToString("G9", CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>安定 ID を足す。0 は載せない。</summary>
        public CommandDataBuilder Id(string key, ulong value)
        {
            if (!_closed && value != 0UL)
                _jb.KeyRaw(key, value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        /// <summary>文字列を足す。null は載せない。</summary>
        public CommandDataBuilder Text(string key, string value)
        {
            if (!_closed && value != null) _jb.KeyValue(key, value);
            return this;
        }

        /// <summary>真偽値を足す。</summary>
        public CommandDataBuilder Flag(string key, bool value)
        {
            if (!_closed) _jb.KeyValue(key, value);
            return this;
        }

        /// <summary>整数の配列を足す。null・空なら載せない。</summary>
        public CommandDataBuilder Ints(string key, IReadOnlyList<int> values)
        {
            if (_closed || values == null || values.Count == 0) return this;

            _jb.Key(key);
            _jb.BeginArray();
            for (int i = 0; i < values.Count; i++) _jb.Value(values[i]);
            _jb.EndArray();
            return this;
        }

        /// <summary>文字列の配列を足す。null・空なら載せない。</summary>
        public CommandDataBuilder Texts(string key, IReadOnlyList<string> values)
        {
            if (_closed || values == null || values.Count == 0) return this;

            _jb.Key(key);
            _jb.BeginArray();
            for (int i = 0; i < values.Count; i++) _jb.Value(values[i] ?? "");
            _jb.EndArray();
            return this;
        }

        /// <summary>
        /// 実数の配列を足す。null・空なら載せない。
        /// 量のあるものを載せてよいのは生データの取得（getRawData）だけ。
        /// </summary>
        public CommandDataBuilder Nums(string key, IReadOnlyList<float> values)
        {
            if (_closed || values == null || values.Count == 0) return this;

            _jb.Key(key);
            _jb.BeginArray();
            for (int i = 0; i < values.Count; i++) _jb.Value(values[i]);
            _jb.EndArray();
            return this;
        }

        /// <summary>
        /// 辞書の項目 1 件を入れ子で足す。中身そのものは載せず、
        /// 名前・種類・対象・件数・要約だけを載せる。null なら載せない。
        /// </summary>
        public CommandDataBuilder Entry(string key, PLDataEntry entry)
        {
            if (_closed || entry == null) return this;

            _jb.Key(key);
            _jb.BeginObject();
            _jb.KeyValue("name",    entry.Name ?? "");
            _jb.KeyValue("kind",    entry.Kind.ToString());
            _jb.KeyValue("count",   entry.Count);
            _jb.KeyValue("summary", entry.Summary ?? "");
            if (entry.MasterIndex >= 0) _jb.KeyValue("masterIndex", entry.MasterIndex);
            if (entry.ObjectId != 0UL)
                _jb.KeyRaw("objectId", entry.ObjectId.ToString(CultureInfo.InvariantCulture));
            _jb.EndObject();
            return this;
        }

        /// <summary>組み立てた JSON を返す。2 度目以降は同じものを返す。</summary>
        public string Build()
        {
            if (!_closed)
            {
                _jb.EndObject();
                _closed = true;
            }
            return _jb.ToString();
        }
    }

    /// <summary>よく使う形をひとことで作るための入り口。</summary>
    public static class CommandDataJson
    {
        /// <summary>新しい組み立て器を作る。</summary>
        public static CommandDataBuilder New() => new CommandDataBuilder();

        /// <summary>辞書へ入れた項目 1 件だけを返す形。</summary>
        public static string Entry(PLDataEntry entry)
            => new CommandDataBuilder().Entry("entry", entry).Build();

        /// <summary>選択の件数だけを返す形。</summary>
        public static string SelectionCounts(int vertices, int edges, int faces, int lines)
            => new CommandDataBuilder()
                .Int("vertices", vertices)
                .Int("edges",    edges)
                .Int("faces",    faces)
                .Int("lines",    lines)
                .Build();

        /// <summary>描画オブジェクトの規模だけを返す形。</summary>
        public static string MeshCounts(int vertices, int faces, int materials)
            => new CommandDataBuilder()
                .Int("vertices",  vertices)
                .Int("faces",     faces)
                .Int("materials", materials)
                .Build();
    }
}
