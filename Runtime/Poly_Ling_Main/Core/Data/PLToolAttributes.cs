// PLToolAttributes.cs
// ツールハンドラの公開層（操作経路統一計画.md B・C・D）の属性。
//
// 【何のために要るか】
//   パネルはハンドラのプロパティ・メソッドを具体型で直接触っており、MCP やリモートの
//   クライアントからは同じものに名前で届く手段が無かった。属性で宣言したものだけを、
//   名前で読み書き・呼び出しできるようにする（PLToolSurface）。
//   正本はハンドラに置いたまま（プロパティの setter の副作用もそのまま働く）。

using System;

namespace Poly_Ling.Data
{
    /// <summary>ハンドラのクラスに付け、ツール名を宣言する。名前は全体で重複しないこと。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PLToolAttribute : Attribute
    {
        public string Id { get; }
        public string Description { get; set; } = "";
        public PLToolAttribute(string id) { Id = id ?? ""; }
    }

    /// <summary>
    /// 名前で読み書きできるパラメータ（B）。読み書きできる public プロパティ、または public フィールドに付ける。
    /// 型は PanelCommandFactory が文字列と相互変換できるもの（bool・int・float・string・enum・Vector 類など）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class PLToolParamAttribute : Attribute
    {
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// 設定オブジェクトを返すプロパティ、または引数なしメソッドに付ける。
    /// 返したオブジェクト（参照型）の public なフィールドと読み書きできるプロパティを、
    /// "名前.メンバー名" のパラメータとして公開する（名前は Name、省けばメンバー名）。
    /// 設定を専用のオブジェクトに持つハンドラを、ハンドラ側を書き換えずに公開するため。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class PLToolSettingsAttribute : Attribute
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>名前で呼べる操作（C）。引数なしの public メソッドに付ける。</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class PLToolActionAttribute : Attribute
    {
        public string Description { get; set; } = "";
    }

    /// <summary>読み取り専用の概要（D）。public プロパティに付ける（件数・結果など）。</summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class PLToolStateAttribute : Attribute
    {
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// 構造体・オブジェクトで返す概要（下調べの結果など）を、中の public なフィールドと
    /// プロパティごとの概要 "名前.メンバー名" として公開する。プロパティまたは引数なしメソッドに付ける
    /// （名前は Name、省けばメンバー名）。読み取り専用。
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class PLToolStateGroupAttribute : Attribute
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }
}
