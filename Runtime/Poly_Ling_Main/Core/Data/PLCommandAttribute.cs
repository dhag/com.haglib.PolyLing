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

        // ----------------------------------------------------------------
        // 影響・危険性・検証・前提（PolyLing_利用シーン_カテゴライズ設計方針.md 6 節）
        //   分類（Category）は「何をするか」だけを表す。「何が変わり、何が壊れ得るか」は
        //   ここに分けて持つ。利用シーンとモデル状態を突き合わせて、警告・承認・非表示を決める材料になる。
        //   未指定（None）でもコマンドの公開は妨げない。書き込むコマンドで Effects が None のものは
        //   監査（PanelCommandFactoryAudit）が「未設定」として数える。
        // ----------------------------------------------------------------

        /// <summary>実行すると何が変わるか。</summary>
        public PLCommandEffect Effects { get; set; } = PLCommandEffect.None;

        /// <summary>実行すると何が壊れ得るか。</summary>
        public PLCommandHazard Hazards { get; set; } = PLCommandHazard.None;

        /// <summary>実行後に確かめるとよいこと。</summary>
        public PLCommandVerification Verification { get; set; } = PLCommandVerification.None;

        /// <summary>
        /// 呼び出しが成り立つための条件。満たさずに呼ぶと失敗するか、壊れる。
        /// Hazards（実行の結果として壊れ得るもの）とは別に、呼ぶ前に確かめるものを表す。
        /// </summary>
        public PLCommandPrecondition Preconditions { get; set; } = PLCommandPrecondition.None;

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

    /// <summary>
    /// コマンドが変えるもの（PLCommand.Effects）。複数を組み合わせてよい。
    /// 値は旗として 1 ビットずつ割り当てる。並び替えたり詰めたりしないこと
    /// （属性に書いた値の意味が変わる）。新しい値は末尾に足す。
    /// </summary>
    [Flags]
    public enum PLCommandEffect
    {
        None           = 0,
        CreatesObject  = 1 << 0,
        DeletesObject  = 1 << 1,
        VertexPosition = 1 << 2,
        Topology       = 1 << 3,
        VertexOrder    = 1 << 4,
        Normals        = 1 << 5,
        UV             = 1 << 6,
        Material       = 1 << 7,
        Skeleton       = 1 << 8,
        SkinWeights    = 1 << 9,
        Morph          = 1 << 10,
        Animation      = 1 << 11,
        SpringBone     = 1 << 12,
        /// <summary>表示中の姿勢（ポーズ層・T ポーズ）。</summary>
        Pose           = 1 << 13,
        /// <summary>バインドポーズ（ウェイトを付けた基準の姿勢）。</summary>
        BindPose       = 1 << 14,
        /// <summary>オブジェクトの親子・並び。</summary>
        Hierarchy      = 1 << 15,
        /// <summary>ミラーの設定（ミラーの種類・分岐ルートなどのフラグ）。形状は変えない。</summary>
        MirrorSetting  = 1 << 16,
        /// <summary>オブジェクト単位の属性（表示・ロック・名前・原点・グループ設定など）。形状は変えない。</summary>
        ObjectAttribute = 1 << 17,
    }

    /// <summary>
    /// 実行によって壊れ得るもの（PLCommand.Hazards）。複数を組み合わせてよい。
    /// 禁止ではない。利用シーンの注意書きで、承知のうえで使うことがある。
    /// 値の決まりは PLCommandEffect と同じ。
    /// </summary>
    [Flags]
    public enum PLCommandHazard
    {
        None                     = 0,
        InvalidatesSkinWeights   = 1 << 0,
        InvalidatesMorphs        = 1 << 1,
        MaySplitVertices         = 1 << 2,
        ChangesVertexOrder       = 1 << 3,
        BreaksMirrorRelation     = 1 << 4,
        AffectsMultipleObjects   = 1 << 5,
        RequiresUserConfirmation = 1 << 6,
        /// <summary>バインドポーズを書き換え、元の姿勢へ戻せない。</summary>
        ChangesBindPose          = 1 << 7,
    }

    /// <summary>
    /// 実行後に確かめるとよいこと（PLCommand.Verification）。複数を組み合わせてよい。
    /// 値の決まりは PLCommandEffect と同じ。
    /// </summary>
    [Flags]
    public enum PLCommandVerification
    {
        None           = 0,
        Geometry       = 1 << 0,
        Topology       = 1 << 1,
        VertexCount    = 1 << 2,
        Normals        = 1 << 3,
        UVSeams        = 1 << 4,
        SkinWeights    = 1 << 5,
        MorphIntegrity = 1 << 6,
        MirrorIntegrity= 1 << 7,
        Visual         = 1 << 8,
    }

    /// <summary>
    /// 呼び出しが成り立つための条件（PLCommand.Preconditions）。複数を組み合わせてよい。
    /// 値の決まりは PLCommandEffect と同じ。
    /// </summary>
    [Flags]
    public enum PLCommandPrecondition
    {
        None                    = 0,
        /// <summary>頂点・面・オブジェクトのどれかが選ばれていること。</summary>
        RequiresSelection       = 1 << 0,
        /// <summary>対象にスキンウェイトがあること。</summary>
        RequiresSkinWeights     = 1 << 1,
        /// <summary>モデルに Humanoid の割当があること。</summary>
        RequiresHumanoid        = 1 << 2,
        /// <summary>対象にモーフがあること。</summary>
        RequiresMorphs          = 1 << 3,
        /// <summary>取り込み元と取り込み先の頂点数が一致していること。</summary>
        RequiresMatchingVertexCount = 1 << 4,
    }
}
