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
//   Min / Max / Step は属性に直接数値を書かず、対象の構造体が持つ const を参照する。
//   同じ const を UI 側の行ヘルパ（SR / IR）も参照するので、範囲の定義は1箇所になる。
//   属性の引数は定数式でなければならないため、const 以外は書けない。
//
// 【未指定】
//   Min / Max / Step の既定は double.NaN。スキーマ生成側は NaN を「範囲なし」として扱う。

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
    }
}
