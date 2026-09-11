// PLResultAttribute.cs
// コマンドが返す値 1 項目ぶんのメタデータ。MCP の道具一覧の outputSchema になる。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PLParamAttribute と同じ場所）
//
// 【なぜプロパティではなくクラスに付けるか】
//   入力は「コンストラクタ引数 ↔ プロパティ」という実体があるので、
//   PLParam はプロパティに付けられる。
//   戻り値には実体が無い。CommandResult.Data は JSON 文字列 1 本で、
//   中身の形はディスパッチャの case が決める。そのため宣言はコマンドの
//   クラスに並べる形にする。1 コマンドに複数付く（AllowMultiple = true）。
//
// 【対応表を増やさない】
//   入力側の型の対応表は 3 か所ある（TryParse / TryFormat / TryJsonType）。
//   戻り値はそこへ載せない。JSON へ書き出すだけで読み戻さないので、
//   PLResultKind の 7 値で足りる。入力の対応表とは独立に保つ。
//
// 【Entry の形】
//   結果辞書へ書いた項目 1 件の見出し。中身は載せず、名前・種類・件数・要約
//   （と対象があれば masterIndex / objectId）だけを持つ。
//   CommandDataBuilder.Entry が出す形と対にしてある。両方を一緒に直すこと。
//
// 【付け忘れ】
//   PLResult が 1 つも無いコマンドは outputSchema を出さない。
//   戻り値を返さないコマンドが多数のうちは、これが正しい既定。
//
// 【継承する】
//   Inherited = true。図形生成のように、基底クラス 1 つが全 27 種の
//   受け口を兼ねている系統では、宣言も基底に 1 度書けば足りる。
//   個別に返すものが増えたときは、その具象クラスへ足す（重複は Key で分かる）。
//   PLCommand を Inherited = false にしてあるのは説明文が 1 本ずつ違うためで、
//   戻り値の形はそろっているので扱いを変えている。

using System;

namespace Poly_Ling.Data
{
    /// <summary>戻り値 1 項目の形。JSON Schema の型へ 1 対 1 で写す。</summary>
    public enum PLResultKind
    {
        /// <summary>整数。</summary>
        Integer = 0,

        /// <summary>実数。</summary>
        Number = 1,

        /// <summary>文字列。安定 ID のように double で表せない整数もこれで返す。</summary>
        Text = 2,

        /// <summary>真偽値。</summary>
        Flag = 3,

        /// <summary>整数の配列。</summary>
        IntegerArray = 4,

        /// <summary>文字列の配列。</summary>
        TextArray = 5,

        /// <summary>結果辞書へ書いた項目 1 件の見出し（CommandDataBuilder.Entry の形）。</summary>
        Entry = 6,

        /// <summary>
        /// 実数の配列。生データの取得（getRawData）が座標・UV・法線・ウェイトに使う。
        /// 量のあるものを載せるのはこの経路だけの例外。
        /// </summary>
        NumberArray = 7,
    }

    /// <summary>
    /// コマンドが CommandResult.Data に載せる項目 1 件の宣言。
    /// コマンドの具象クラスに、返す項目の数だけ付ける。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class PLResultAttribute : Attribute
    {
        /// <summary>JSON のキー。CommandDataBuilder へ渡すキーと一致させること。</summary>
        public string Key { get; }

        /// <summary>値の形。</summary>
        public PLResultKind Kind { get; }

        /// <summary>スキーマの説明文。空なら説明なし。</summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// 表示名を引くためのキー。PLParam.TextKey と同じ扱いで、
        /// 空のときは使わない。
        /// </summary>
        public string TextKey { get; set; } = "";

        /// <summary>
        /// スキーマに出さない。診断用に一時的に載せている項目に付ける。
        /// 付け忘れ（属性なし）と区別するために明示する。
        /// </summary>
        public bool Ignore { get; set; }

        /// <summary>
        /// 条件によっては載らない項目か。
        /// true のものは outputSchema の required から外れる。
        /// 既定は false（必ず載る）。
        /// </summary>
        public bool Optional { get; set; }

        public PLResultAttribute(string key, PLResultKind kind)
        {
            Key  = key ?? "";
            Kind = kind;
        }
    }
}
