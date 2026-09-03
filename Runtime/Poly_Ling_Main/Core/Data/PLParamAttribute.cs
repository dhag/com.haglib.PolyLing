// PLParamAttribute.cs
// パラメータ1つぶんのメタデータ。MCP のツールスキーマ生成の入力になる。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 【付ける対象】
//   ・図形生成器のパラメータ構造体（Poly_Ling.PrimitiveMesh 系ほか）の public フィールド
//   ・PanelCommand の public プロパティ
//
// 【付け忘れと「出さない」を区別する】
//   スキーマに出さないものにも Ignore = true を明示して付ける。
//   属性が無い＝付け忘れ、として検出できるようにするため。
//   編集中の選択位置・プレビューの視点角・入出力パスがこれに当たる。
//
// 【範囲の正典】
//   範囲の出どころは 2 つある。どちらを使うかは「ユーザーが範囲を変えられるか」で決まる。
//
//   (a) ParameterLimits の管轄 … LimitKey を指定する。
//       ブラシ半径や強度のように、上下限そのものをユーザーが調整できるものが該当する。
//       実体は persistentDataPath の ParameterLimits.csv にあり、UI からの書き換えが
//       SetF で CSV へ戻る（SculptSettings.cs:39-41）。実行時の値なので属性には書けない。
//       LimitKey には ".Min" / ".Max" を除いた前半だけを入れる
//       （例: "Sculpt.BrushRadius"）。スキーマ生成側が接尾辞を付けて GetF で引く。
//
//   (b) 固定範囲 … Min / Max / Step を使う。
//       PMX のモーフパネル番号 0..3 のように、仕様で決まっていてユーザーが
//       変えてはいけないものが該当する。属性に直接数値を書かず、対象の構造体が持つ
//       const を参照する。同じ const を UI 側の行ヘルパ（SR / IR）も参照するので、
//       範囲の定義は1箇所になる。属性の引数は定数式でなければならないため、
//       const 以外は書けない。
//
//   両方を同時に指定しない。指定した場合は LimitKey を優先する。
//
// 【未指定】
//   Min / Max / Step の既定は double.NaN。スキーマ生成側は NaN を「範囲なし」として扱う。
//   LimitKey の既定は空文字。

using System;

namespace Poly_Ling.Data
{
    /// <summary>
    /// パラメータ1つぶんのメタデータ。
    /// 表示名の実体は文字列表（PrimitiveMeshTexts 等）に置き、ここには引くためのキーだけを持つ。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property,
                    AllowMultiple = false, Inherited = true)]
    public sealed class PLParamAttribute : Attribute
    {
        /// <summary>
        /// 表示名を引くためのキー。PrimitiveMeshTexts のキーと同じものを入れる。
        /// 空のときはフィールド名をそのまま表示名として使う。
        /// </summary>
        public string TextKey { get; set; } = "";

        /// <summary>スキーマの説明文。空なら説明なし。</summary>
        public string Description { get; set; } = "";

        /// <summary>下限。double.NaN は指定なし。</summary>
        public double Min { get; set; } = double.NaN;

        /// <summary>上限。double.NaN は指定なし。</summary>
        public double Max { get; set; } = double.NaN;

        /// <summary>刻み幅。double.NaN は指定なし。</summary>
        public double Step { get; set; } = double.NaN;

        /// <summary>
        /// ParameterLimits のキーの前半。".Min" / ".Max" は付けない
        /// （例: "Sculpt.BrushRadius"）。空のときは指定なし。
        /// 上下限をユーザーが調整できるパラメータはこちらを使い、Min / Max は使わない。
        /// </summary>
        public string LimitKey { get; set; } = "";

        /// <summary>省略できないパラメータか。</summary>
        public bool Required { get; set; }

        /// <summary>
        /// スキーマに出さない。
        /// 編集中の選択位置・プレビューの視点角・入出力パスなど、
        /// 形状そのものを決めないものに付ける。
        /// </summary>
        public bool Ignore { get; set; }

        /// <summary>Min が指定されているか。</summary>
        public bool HasMin => !double.IsNaN(Min);

        /// <summary>Max が指定されているか。</summary>
        public bool HasMax => !double.IsNaN(Max);

        /// <summary>Step が指定されているか。</summary>
        public bool HasStep => !double.IsNaN(Step);

        /// <summary>LimitKey が指定されているか。</summary>
        public bool HasLimitKey => !string.IsNullOrEmpty(LimitKey);
    }
}
