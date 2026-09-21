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

        /// <summary>
        /// コマンドがモデルのどこを書き換えるか。担当者判定（RemoteOwnership）の対象解決に使う。
        /// 既定は Unspecified で、PanelCommandFactoryAudit.RunAll が数える。
        /// </summary>
        public PLWriteScope Writes { get; set; } = PLWriteScope.Unspecified;

        /// <summary>
        /// Writes = Targets のとき、対象のミラー側オブジェクトも書き換えるか。既定は true。
        ///
        /// 頂点位置は描画同期でミラー側へ写り（PlayerViewportManager.Renderer.cs）、
        /// 表示・ロックはミラー側へ広げ（PlayerCommandDispatcher.MeshAttributes.cs の
        /// ExpandToMirrorPeers）、位相変更はミラー側を作り直す。担当者判定は既定で
        /// ミラー側も対象に含める。処理を読んでミラー側へ書かないと確かめたコマンドだけ
        /// false にする。
        /// </summary>
        public bool WritesMirrorSide { get; set; } = true;

        /// <summary>
        /// 分類名（任意）。polyling_search の絞り込みと検索対象に使う。
        /// 空のときは分類なしとして扱い、公開は妨げない。
        /// 例: "mesh.topology" / "bone" / "ui"。
        /// </summary>
        public string Category { get; set; } = "";

        /// <summary>
        /// 検索用の語（任意）。カンマ区切り。polyling_search が名前・説明と一緒に照合する。
        /// 説明文に出てこない言い換え（「板」「プレート」等）を足すための欄。
        /// </summary>
        public string Tags { get; set; } = "";

        public PLCommandAttribute() { }

        public PLCommandAttribute(string description) { Description = description; }
    }

    /// <summary>
    /// コマンドの書き込み範囲。PLCommandAttribute.Writes が使う。
    ///
    /// 【判定での扱い】
    ///   None      … 担当者判定をしない（モデルを書き換えない）
    ///   Targets   … PLParam(IsMeshRef, MeshRefAccess = Write) の付いた引数が指す
    ///                オブジェクトについて担当者判定をする
    ///   AddOnly   … 既存オブジェクトを書き換えず新規に足すだけ。担当者判定をしない
    ///   ModelWide … モデル内に他人の担当が 1 つでもあれば拒否する
    /// </summary>
    public enum PLWriteScope
    {
        /// <summary>未宣言。PanelCommandFactoryAudit.RunAll が数える。</summary>
        Unspecified = 0,

        /// <summary>モデルを書き換えない（照会・書き出し・表示だけの切り替えなど）。</summary>
        None = 1,

        /// <summary>引数で示したオブジェクトだけを書き換える。</summary>
        Targets = 2,

        /// <summary>既存オブジェクトを書き換えず、新規に足すだけ。</summary>
        AddOnly = 3,

        /// <summary>モデル全体に効く、または対象を引数から特定できない。</summary>
        ModelWide = 4,
    }
}
