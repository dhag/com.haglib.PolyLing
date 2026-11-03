// PLUiRouteAttribute.cs
// コマンドを人が画面で行うときの経路（どのボタンや入力欄を通るか）の宣言。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PLCommandAttribute と同じ場所）
//
// 【何に使うか】
//   シナリオを流すとき、項目のコマンドに当たるボタンへ赤枠を出す（案内バー）。
//   「今なにをしているか」「次に押すボタン」「人間の入力待ち」のどれでも、この宣言からボタンや入力欄を引く。
//
// 【経路】
//   1 つの経路は、関係するボタン・入力欄の ID の並び（UI 自動操作のボタン・入力欄の ID。uiDescribe で引ける）。
//   区切り（何段目に押すか）は持たない。経路に入ったボタンや入力欄は全部同時に枠で囲む。
//   人間の入力待ちは「そのコマンドが実行されたか」で判定するので、最後に押すボタンを特定しなくてよい。
//   ボタンで表せない操作（ビューポートで描く・ショートカット）は Note に書く。枠はそこへ行き着くまでのボタンや入力欄にだけ付く。
//
// 【入口が複数あるとき】
//   経路を複数書く。Order の小さいものが既定。シナリオを流すときは、前の項目の経路に近いもの
//   （最後のボタンや入力欄が同じパネルにある → 同じボタンや入力欄を含む）を選び、近いものが無ければ既定を使う（UiRouteCatalog.Choose）。
//
// 【書かないとき】
//   UI を持たないコマンド（MCP 専用・照会・内部の後始末）は PLCommand の NoUi を立てる。
//   経路も NoUi も無いコマンドは PanelCommandFactoryAudit.RunAll が数える。
//   経路に書いたボタン・入力欄の ID が UI 自動操作に登録されていないものは queryUiAutomationAudit が数える。
//
// 【図形生成】
//   CreatePrimitiveMeshCommand の派生は図形ごとに書かない。PLUiRouteByShape の付いた型は、
//   画面側（UiRouteCatalog）が図形名から左ペインのボタンと図形ボタンを割り出す。

using System;

namespace Poly_Ling.Data
{
    /// <summary>
    /// コマンドを人が画面で行うときの経路 1 本。具象コマンドに複数付けてよい。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class PLUiRouteAttribute : Attribute
    {
        /// <summary>経路の名前（人が読む。案内バーと queryScenarioRun に出す）。</summary>
        public string Name { get; }

        /// <summary>関係するボタン・入力欄の ID。左ペインなら折り畳みの見出しとボタン、右ペインならパネルのボタンや入力欄。</summary>
        public string[] Items { get; }

        /// <summary>並び順。小さいものが既定の経路。</summary>
        public int Order { get; set; }

        /// <summary>ボタンで表せない操作の説明（ビューポートで描く・ショートカットなど）。空でもよい。</summary>
        public string Note { get; set; } = "";

        /// <summary>最後に押す実行のボタンの ID（画面の操作の宣言）。Items にも入れておく。読み出しは UiOperationCatalog。</summary>
        public string Run { get; set; } = "";

        public PLUiRouteAttribute(string name, params string[] items)
        {
            Name  = name ?? "";
            Items = items ?? new string[0];
        }
    }

    /// <summary>
    /// 経路を図形名から割り出す印。CreatePrimitiveMeshCommand に付け、派生へ継ぐ。
    /// 割り出しは画面側（UiRouteCatalog）が行う（どのボタンがどの図形かを知っているのは画面側だけ）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class PLUiRouteByShapeAttribute : Attribute
    {
    }

    // ================================================================
    // 画面の操作の宣言（PolyLing_チュートリアル計画.md 3.5・段 3）
    // ================================================================
    //
    // 【何に使うか】
    //   シナリオの項目を、人が画面で行う操作へ読み替える。チュートリアルの画面の文字はここから読む。
    //   読み出しは UiOperationCatalog（queryCommandUiOperation）。文字はここにも文書にも持たず、宣言した ID からその時点の表示を読む。
    //
    // 【なぜ PLParam に足さないか】
    //   PLParam は MQOImportSettings のような型の欄にも付き、同じ型を別のコマンドが使う。
    //   画面の ID はコマンドの経路の側の情報なので、経路の隣に置く。
    //   経路が複数あるコマンドで欄が経路ごとに違うものは、今は無い。出てきたら経路の名前を持たせる。
    //
    // 【検査】
    //   引数名と Values の誤り、宣言の漏れ（[参考]）… queryCommandAudit（PanelCommandFactoryAudit）
    //   ID の誤り … queryUiAutomationAudit（UiAutomationAudit。まだ作られていない ID は UiAutomationRegistry.StatusOf が見分ける）

    /// <summary>値を選択肢の文字へ読み替える方法。PLUiArgAttribute.ValueKind が使う。</summary>
    public enum PLUiValueKind
    {
        /// <summary>Values があればその並び、IsMeshRef の引数ならオブジェクト、それ以外は値そのもの。</summary>
        Auto = 0,

        /// <summary>索引の値を「[索引] 名前」「索引: 名前」の選択肢へ読み替える。-1 は先頭の選択肢。</summary>
        Object = 1,

        /// <summary>処理の種類名を MeshModifierRegistry の並びの選択肢へ読み替える。</summary>
        ModifierKind = 2,
    }

    /// <summary>引数 1 つと入力欄の対応。キーは ToArgs のキー（入れ子はドット区切り）。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class PLUiArgAttribute : Attribute
    {
        public string Arg { get; }

        /// <summary>入力欄の ID（パネル ID を含む）。uiSetValue に渡す ID。</summary>
        public string Control { get; }

        /// <summary>
        /// 選択式の欄の選択肢と同じ並びの値（カンマ区切り）。列挙型は名前で書く。
        /// 文字は持たない。何番目の選択肢かだけを決め、文字は画面の選択肢から読む。
        /// </summary>
        public string Values { get; set; } = "";

        public PLUiValueKind ValueKind { get; set; } = PLUiValueKind.Auto;

        /// <summary>
        /// 画面に見出しが無い欄の呼び方（パスの欄など）。見出しがある欄には書かない。
        /// 画面の文字の写しではなく、読者に欄を指す言葉。
        /// </summary>
        public string Unlabeled { get; set; } = "";

        public PLUiArgAttribute(string arg, string control)
        {
            Arg     = arg ?? "";
            Control = control ?? "";
        }
    }

    /// <summary>
    /// 可変の引数（キーの配列と値の配列）と入力欄の対応。ID はキーから作られる（処理列の種類の引数欄）。
    /// 種類ごとには宣言しない。欄は種類の PLParam から ParamFieldBuilder が作るので、規則だけを書く。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PLUiArgListAttribute : Attribute
    {
        public string KeysArg { get; }
        public string ValuesArg { get; }

        /// <summary>入力欄の ID の形。{0} にキーが入る。</summary>
        public string ControlFormat { get; }

        /// <summary>欄を作れないキーを書く入力欄の ID。空なら無い。</summary>
        public string Fallback { get; set; } = "";

        /// <summary>
        /// 実行のあとで欄を出すために、最後の行を選ぶ一覧の ID。空なら選ばない。
        /// 欄は実行の前に無いので、チュートリアルの差し込みは実行のあとで読む。
        /// </summary>
        public string SelectLast { get; set; } = "";

        public PLUiArgListAttribute(string keysArg, string valuesArg, string controlFormat)
        {
            KeysArg       = keysArg ?? "";
            ValuesArg     = valuesArg ?? "";
            ControlFormat = controlFormat ?? "";
        }
    }

    /// <summary>画面に入力欄が無い引数。理由を残す（PLParam の Ignore と同じく、付け忘れと区別するため）。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class PLUiNoFieldAttribute : Attribute
    {
        public string Reason { get; }

        /// <summary>引数のキー。入れ子の全部を指すときは "settings.debugVertexInfo" のように 1 つずつ書く。</summary>
        public string[] Args { get; }

        public PLUiNoFieldAttribute(string reason, params string[] args)
        {
            Reason = reason ?? "";
            Args   = args ?? new string[0];
        }
    }

    /// <summary>
    /// パネルから送られないコマンドを、人が行う別の操作へ置き換える宣言。
    /// 経路（PLUiRoute）と違い、その操作でこのコマンドは実行されない。案内バーは赤枠を出さず、項目をそのまま実行する。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PLUiManualAttribute : Attribute
    {
        /// <summary>操作の名前（人が読む）。</summary>
        public string Name { get; }

        /// <summary>関係するボタン・入力欄の ID。</summary>
        public string[] Items { get; }

        /// <summary>置き換えの説明。画面の文字は書かない。</summary>
        public string Note { get; set; } = "";

        /// <summary>
        /// 人が選ぶものを実行の結果から読むときの、結果の配列のキー。空なら読まない。
        /// 接頭辞で選んだときも、実際に選ばれた名前になる。
        /// </summary>
        public string ResultList { get; set; } = "";

        public PLUiManualAttribute(string name, params string[] items)
        {
            Name  = name ?? "";
            Items = items ?? new string[0];
        }
    }

    /// <summary>
    /// 引数に当たらないが、このコマンドと同じ結果にするために決まった値にする欄。
    /// 画面の実行のボタンがコマンド単体より多くのことをするとき、それを止める欄に使う。
    /// 値は uiSetValue の形で、選択式は選択肢の番号で書く（文字を持たない）。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class PLUiFixedAttribute : Attribute
    {
        public string Control { get; }
        public string Value { get; }

        /// <summary>なぜこの値にするか。人が読む。画面の文字は書かない。</summary>
        public string Reason { get; set; } = "";

        public PLUiFixedAttribute(string control, string value)
        {
            Control = control ?? "";
            Value   = value ?? "";
        }
    }
}
