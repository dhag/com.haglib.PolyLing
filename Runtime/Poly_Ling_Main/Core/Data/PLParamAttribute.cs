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
    /// <summary>プロファイル本体の値の形。PLParamAttribute.ProfileRole が使う。</summary>
    public enum PLProfileRole
    {
        /// <summary>プロファイルではない。</summary>
        None = 0,

        /// <summary>Vector2[]。点列 1 本。</summary>
        Points = 1,

        /// <summary>float[]（x,y を 2 個ずつ）。ループの区切りは相棒のキーが持つ。</summary>
        FlatLoops = 2,
    }

    /// <summary>
    /// 作り直しで「出来たものを既存の出力先へ書き戻す」ときの役割。
    /// PLParamAttribute.RebuildRole が使う。
    /// </summary>
    public enum PLRebuildRole
    {
        /// <summary>書き戻しには関わらない。</summary>
        None = 0,

        /// <summary>出力先の masterIndex を書くパラメータ（int）。</summary>
        TargetIndex = 1,

        /// <summary>
        /// 書き戻す形へ切り替えるパラメータ。書く値は RebuildModeValue が持つ。
        /// 例: PrimitivePlacement.AddMode へ ReplaceExisting。
        /// </summary>
        TargetMode = 2,
    }

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

        /// <summary>
        /// 値が MeshContextList の索引（int）または索引配列（int[]）であることを示す。
        ///
        /// 【何のために要るか】
        ///   ObjectGroup は生成コマンドを ToArgs の文字列で保存し、あとで Create で
        ///   組み直す。索引はリストの挿入・削除・並べ替えでずれるため、
        ///   保存した値をそのまま使うと別のオブジェクトを指す。
        ///   再構築の直前に、この印が付いたキーだけ ObjectId から索引を引き直す。
        ///
        /// 【付ける対象】
        ///   「どの描画オブジェクトを対象にするか」を指すものだけ。
        ///   マテリアルスロット番号やモーフパネル番号のような、
        ///   MeshContextList とは無関係な整数には付けない。
        ///
        /// 【付けても意味が変わらないもの】
        ///   スキーマ生成・ToArgs・Create の扱いは変わらない。読むのは
        ///   ObjectGroup の再構築だけで、印が無いキーは従来どおり素通りする。
        /// </summary>
        public bool IsMeshRef { get; set; }

        /// <summary>
        /// IsMeshRef の索引が、コマンド自身の ModelIndex 以外のモデルを指しうるとき、
        /// 同じ並びでモデル索引を持つプロパティ名を入れる。空＝自分のモデル内。
        ///
        /// 例: ApplyBlendCommand.SourceMasterIndices は SourceModelIndices と同じ並びで、
        ///     ブレンド元は別モデルでもよい（BlendMatchMode.cs:70-74）。
        ///     この場合 MeshRefModelKey = "SourceModelIndices" を入れる。
        ///     再構築のときは 2 本を対で書き戻す。
        ///
        /// プロパティ名で書くこと。キーへの変換（先頭小文字・別名表）は読む側が行う。
        /// </summary>
        public string MeshRefModelKey { get; set; } = "";

        /// <summary>MeshRefModelKey が指定されているか。</summary>
        public bool HasMeshRefModelKey => !string.IsNullOrEmpty(MeshRefModelKey);

        /// <summary>
        /// このパラメータがプロファイル（断面の点列・輪郭のループ群）の本体であることを示す。
        ///
        /// 【何のために要るか】
        ///   IsMeshRef と同じ考え方。ObjectGroup は生成コマンドを文字列で保存し、
        ///   あとで組み直す。プロファイルを焼き込んだままだと、取り込み元の
        ///   描画オブジェクトを直しても出力先は古いままになる。
        ///   再構築の直前に、この印が付いたキーだけ取り込みを掛け直して差し替える。
        ///
        /// 【値の形】
        ///   Points    … Vector2[]。点列 1 本
        ///   FlatLoops … float[]（x,y を 2 個ずつ）。ループの区切りは
        ///               ProfileLoopStartsKey / ProfileLoopIsHoleKey が指す相棒が持つ
        /// </summary>
        public PLProfileRole ProfileRole { get; set; } = PLProfileRole.None;

        /// <summary>FlatLoops のとき、ループ開始位置を持つプロパティ名。</summary>
        public string ProfileLoopStartsKey { get; set; } = "";

        /// <summary>FlatLoops のとき、穴フラグを持つプロパティ名。</summary>
        public string ProfileLoopIsHoleKey { get; set; } = "";

        /// <summary>
        /// 取り込んだ点列に「長辺を 1 にする等方スケール」を掛けるか。
        ///
        /// 帯系の断面（フリル・パイプ）は rung 長で正規化された系にあるので true。
        /// 回転体・2D 押し出しはモデルのローカル座標をそのまま使うので false。
        /// </summary>
        public bool ProfileNormalize { get; set; }

        /// <summary>プロファイルの印が付いているか。</summary>
        public bool HasProfileRole => ProfileRole != PLProfileRole.None;

        /// <summary>
        /// 作り直しで出力先へ書き戻すときの役割。
        ///
        /// 【何のために要るか】
        ///   ObjectGroup の作り直しは、新しいオブジェクトを作らず既存の出力先へ
        ///   中身を書き戻す。そのために「出力先の索引を書くキー」と
        ///   「書き戻す形へ切り替えるキー」が要る。
        ///   コマンド型で分岐すると、対応するコマンドを足すたびに分岐が伸びるので、
        ///   印は属性側に持たせ、読む側は属性を読むだけにする
        ///   （IsMeshRef / ProfileRole と同じ考え方）。
        ///
        /// 【付ける対象】
        ///   1 つのコマンド型に TargetIndex と TargetMode を 1 つずつまで。
        ///   TargetIndex は int でなければならない。
        ///   入れ子の中（PrimitivePlacement など）に付けてもよい。
        /// </summary>
        public PLRebuildRole RebuildRole { get; set; } = PLRebuildRole.None;

        /// <summary>
        /// TargetMode のときに書く値。
        /// enum はメンバー名（例 "ReplaceExisting"）、bool は "true" / "false"、
        /// int は 10 進の文字列。読む側が Args の文字列へ直す
        /// （変換の規則は PanelCommandFactory.TryFormat と同じ）。
        /// </summary>
        public string RebuildModeValue { get; set; } = "";

        /// <summary>書き戻しの印が付いているか。</summary>
        public bool HasRebuildRole => RebuildRole != PLRebuildRole.None;

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
