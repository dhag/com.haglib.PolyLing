// PLCommandAttribute.cs
// コマンド 1 本ぶんの説明。MCP の道具一覧に出す。
//
// 【なぜ属性が要るか】
//   PLParam はプロパティ単位の説明で、コマンド自体が何をするかを持つ場所が無かった。
//   <summary> は実行時に読めない（XML ドキュメントはアセンブリに埋まらない）ので、
//   道具一覧の description が空のままになる。
//   MCP のクライアントは説明を見て道具を選ぶため、空だと選べない。
//
// 【書き方】
//   ・1 行で、その道具が何をするかを述べる
//   ・前提（選択が要る・作業フォルダが要る等）は 2 文目に短く添える
//   ・実装の都合（どのクラスが正典か等）は書かない。呼び出し側には関係がない
//
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PLParamAttribute と同じ場所）

using System;

namespace Poly_Ling.Data
{
    /// <summary>
    /// コマンドの説明。PanelCommand の具象クラスに 1 つ付ける。
    /// 付いていないコマンドは道具一覧の description が空になる
    /// （PanelCommandFactoryAudit.RunAll が件数を数える）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PLCommandAttribute : Attribute
    {
        /// <summary>道具一覧に出す説明。</summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// 表示名を引くためのキー。PLParam.TextKey と同じ扱いで、
        /// 空のときは使わない。将来ローカライズへつなぐ余地として置く。
        /// </summary>
        public string TextKey { get; set; } = "";

        public PLCommandAttribute() { }

        public PLCommandAttribute(string description) { Description = description; }
    }
}
