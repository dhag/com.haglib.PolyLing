// PanelCommand.UiAutomation.cs
// PolyLing の UI を意味 ID で操作する要求（UI 自動操作）。
// ヘルプ・チュートリアルの自動生成、操作デモ、UI 操作の試験に使う。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間。PanelCommand.cs から分割）
//
// 【モデルを見ない】
//   UI の表示・値・強調表示・画面キャプチャだけを扱う。ModelIndex は基底の都合で持つが使わない。
//   ディスパッチャはプロジェクトの null 門より前で捌く（PlayerCommandDispatcher.UiAutomation.cs）。
//
// 【操作できるもの】
//   UiAutomationRegistry に明示登録されたパネル・ボタン・入力欄だけ。
//   画面座標・表示文字列・型による探索はしない。
//
// 【リモート（WebSocket）からは受けない】
//   ホストの画面を他の参加者が動かさないよう、RemoteOwnership.TryAuthorize で拒否する。
//   MCP の経路（PolyLingCommandGateway）は RemoteOwnership を通らない。

namespace Poly_Ling.Data
{
    /// <summary>登録済みのパネル・ボタン・入力欄の一覧を返す。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.None, Description = "操作できる UI のパネルとボタン・入力欄の一覧を返す。ボタンや入力欄の情報は controlIds と同じ並びの配列で返す。")]
    [PLResult("panelIds",            PLResultKind.TextArray,    Description = "パネル ID")]
    [PLResult("panelDescriptions",   PLResultKind.TextArray,    Description = "パネルの説明。panelIds と同じ並び")]
    [PLResult("controlIds",          PLResultKind.TextArray,    Description = "ボタン・入力欄の ID", Optional = true)]
    [PLResult("controlPanels",       PLResultKind.TextArray,    Description = "ボタンや入力欄が属するパネル ID。controlIds と同じ並び", Optional = true)]
    [PLResult("controlTypes",        PLResultKind.TextArray,    Description = "ボタンや入力欄の UI 型名。controlIds と同じ並び", Optional = true)]
    [PLResult("controlDescriptions", PLResultKind.TextArray,    Description = "ボタンや入力欄の説明。controlIds と同じ並び", Optional = true)]
    [PLResult("controlSafety",       PLResultKind.TextArray,    Description = "ボタンや入力欄の安全度（readOnly / safeWrite / destructive / fileOperation / userOnly / unspecified）。controlIds と同じ並び", Optional = true)]
    [PLResult("controlWritable",     PLResultKind.IntegerArray, Description = "値を変更できれば 1、読むだけなら 0。controlIds と同じ並び", Optional = true)]
    [PLResult("controlLabels",       PLResultKind.TextArray,    Description = "画面に出ている見出し（ボタンの文字、入力欄の見出し）。読めないものと未構築のものは空。controlIds と同じ並び", Optional = true)]
    public sealed class UiDescribeCommand : PanelCommand
    {
        [PLParam(Description = "パネル ID。空にすると全パネル")]
        public string PanelId { get; }

        public UiDescribeCommand(int modelIndex, string panelId = "")
            : base(modelIndex)
        {
            PanelId = panelId ?? "";
        }
    }

    /// <summary>指定したパネルを右ペインに表示する。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.None, Description = "指定したパネルを右ペインに表示する。ボタンで開いたときと同じ処理を通る。")]
    public sealed class UiShowPanelCommand : PanelCommand
    {
        [PLParam(Description = "パネル ID。uiDescribe の panelIds のどれか", Required = true)]
        public string PanelId { get; }

        public UiShowPanelCommand(int modelIndex, string panelId)
            : base(modelIndex)
        {
            PanelId = panelId ?? "";
        }
    }

    /// <summary>指定したボタンや入力欄が見える状態にする。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.None, Description = "指定したボタンや入力欄が見える状態にする。所属パネルを開き、折り畳みを開き、ボタンや入力欄が見える位置までスクロールする。")]
    public sealed class UiRevealCommand : PanelCommand
    {
        [PLParam(Description = "ボタン・入力欄の ID。uiDescribe の controlIds のどれか", Required = true)]
        public string ControlId { get; }

        [PLParam(Description = "表示したあとボタンや入力欄を枠で強調するか。省くと強調する")]
        public bool Highlight { get; }

        [PLParam(Description = "強調するとき、今出ている枠を残して足す。省くと前の枠を消してから強調する")]
        public bool Add { get; }

        public UiRevealCommand(int modelIndex, string controlId, bool highlight = true, bool add = false)
            : base(modelIndex)
        {
            ControlId = controlId ?? "";
            Highlight = highlight;
            Add       = add;
        }
    }

    /// <summary>指定したボタンや入力欄の現在値を返す。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.None, Description = "指定したボタンや入力欄の現在値を返す。パネルが表示されていなくても読める。")]
    [PLResult("controlId", PLResultKind.Text,      Description = "ボタン・入力欄の ID")]
    [PLResult("type",      PLResultKind.Text,      Description = "ボタンや入力欄の UI 型名")]
    [PLResult("value",     PLResultKind.Text,      Description = "現在値の文字列。数値は不変文化圏の書式")]
    [PLResult("choices",   PLResultKind.TextArray, Description = "選べる値。選択式の入力欄だけ", Optional = true)]
    [PLResult("min",       PLResultKind.Number,    Description = "下限。スライダーだけ", Optional = true)]
    [PLResult("max",       PLResultKind.Number,    Description = "上限。スライダーだけ", Optional = true)]
    public sealed class UiGetValueCommand : PanelCommand
    {
        [PLParam(Description = "ボタン・入力欄の ID。uiDescribe の controlIds のどれか", Required = true)]
        public string ControlId { get; }

        public UiGetValueCommand(int modelIndex, string controlId)
            : base(modelIndex)
        {
            ControlId = controlId ?? "";
        }
    }

    /// <summary>指定した入力欄の値を変更する。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.ModelWide, Description = "指定した入力欄の値を変更する。利用者が操作したときと同じ処理を通る。範囲を超えた値は入力欄側で丸められ、丸めた後の値を返す。")]
    [PLResult("controlId", PLResultKind.Text, Description = "ボタン・入力欄の ID")]
    [PLResult("value",     PLResultKind.Text, Description = "変更後に読み戻した値")]
    public sealed class UiSetValueCommand : PanelCommand
    {
        [PLParam(Description = "ボタン・入力欄の ID。uiDescribe の controlIds のどれか", Required = true)]
        public string ControlId { get; }

        [PLParam(Description = "設定する値の文字列。数値は小数点に . を使う。真偽値は true / false。選択式は uiGetValue の choices のどれか。一覧は選ぶ行の番号（-1 で選択を外す）", Required = true)]
        public string Value { get; }

        [PLParam(Description = "安全度が destructive / fileOperation のボタンや入力欄を変えるときだけ true にする。省くと false")]
        public bool AllowDestructive { get; }

        public UiSetValueCommand(int modelIndex, string controlId, string value, bool allowDestructive = false)
            : base(modelIndex)
        {
            ControlId        = controlId ?? "";
            Value            = value ?? "";
            AllowDestructive = allowDestructive;
        }
    }

    /// <summary>指定したボタンを押す。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.ModelWide, Description = "指定したボタンを押す。利用者が押したときと同じ処理を通る。表示されていないボタン・無効なボタンは押せない（先に uiReveal を使う）。安全度が destructive / fileOperation のボタンは allowDestructive を true にしたときだけ押す。readOnly / userOnly / unspecified のボタンは押さない。")]
    [PLResult("controlId", PLResultKind.Text, Description = "押したボタン・入力欄の ID")]
    public sealed class UiClickCommand : PanelCommand
    {
        [PLParam(Description = "ボタン・入力欄の ID。uiDescribe の controlIds のどれか", Required = true)]
        public string ControlId { get; }

        [PLParam(Description = "安全度が destructive / fileOperation のボタンを押すときだけ true にする。省くと false")]
        public bool AllowDestructive { get; }

        public UiClickCommand(int modelIndex, string controlId, bool allowDestructive = false)
            : base(modelIndex)
        {
            ControlId        = controlId ?? "";
            AllowDestructive = allowDestructive;
        }
    }

    /// <summary>UI 自動操作の登録状況を検査する。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.None, Description = "UI 自動操作の登録状況を検査する。未登録のセクション・属性の付け忘れ・登録失敗・安全度が未指定のボタン・未対応の型・登録済みパネル内の未登録のボタンや入力欄の数を返す。すべて 0 なら右ペインのすべてのボタンや入力欄が登録されている。")]
    [PLResult("report",               PLResultKind.Text,    Description = "検査結果の全文。複数行")]
    [PLResult("sections",             PLResultKind.Integer, Description = "右ペインのセクションの数")]
    [PLResult("panels",               PLResultKind.Integer, Description = "登録済みパネルの数")]
    [PLResult("controls",             PLResultKind.Integer, Description = "登録済みのボタンや入力欄の数")]
    [PLResult("unregisteredSections", PLResultKind.Integer, Description = "登録されていないセクションの数")]
    [PLResult("missingAttributes",    PLResultKind.Integer, Description = "UiControl 属性の付け忘れの数")]
    [PLResult("registrationErrors",   PLResultKind.Integer, Description = "登録を拒否した数")]
    [PLResult("unspecifiedSafety",    PLResultKind.Integer, Description = "安全度が未指定のボタンの数")]
    [PLResult("unsupportedTypes",     PLResultKind.Integer, Description = "読み書きできない型のボタンや入力欄の数")]
    [PLResult("unregisteredElements", PLResultKind.Integer, Description = "登録済みパネルの中にある、登録されていないボタンや入力欄の数")]
    [PLResult("unknownRouteItems",    PLResultKind.Integer, Description = "コマンドの画面での経路（PLUiRoute）に書かれたボタン・入力欄の ID のうち、登録されていないものの数")]
    [PLResult("unknownOperationItems", PLResultKind.Integer, Description = "画面の操作の宣言（実行のボタン・引数と入力欄の対応・人の操作への置き換え）に書かれた ID のうち、誤っているものの数。パネルを開くまで作られない ID は誤りに数えない")]
    [PLResult("unverifiedOperationItems", PLResultKind.Integer, Description = "画面の操作の宣言の ID のうち、作られ得るかを判定できない置き場にあって、開くまで確かめられないものの数。問題には数えない")]
    public sealed class QueryUiAutomationAuditCommand : PanelCommand
    {
        public QueryUiAutomationAuditCommand(int modelIndex = 0) : base(modelIndex) { }
    }

    /// <summary>コマンドを人が画面で行うときの操作を返す。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.None, NoUi = true, Description = "コマンドを人が画面で行うときの操作を、宣言と今の画面から返す。経路、実行のボタン、引数ごとの入力欄と見出し、値の表記、uiSetValue に渡す値、画面に欄が無い引数、人の操作への置き換え。文字はその時点の表示を読む。まだ作られていない欄は argBuilt が 0 で文字は空（経路のとおりに開いてから読み直す）。モデルは変えない。")]
    [PLResult("command",        PLResultKind.Text,      Description = "コマンド名")]
    [PLResult("routeName",      PLResultKind.Text,      Description = "経路の名前", Optional = true)]
    [PLResult("routeNote",      PLResultKind.Text,      Description = "経路の補足", Optional = true)]
    [PLResult("routeItems",     PLResultKind.TextArray, Description = "経路のボタン・入力欄の ID", Optional = true)]
    [PLResult("routeLabels",    PLResultKind.TextArray, Description = "routeItems の見出し。未構築は空", Optional = true)]
    [PLResult("runControl",     PLResultKind.Text,      Description = "実行のボタンの ID", Optional = true)]
    [PLResult("runLabel",       PLResultKind.Text,      Description = "実行のボタンの文字。未構築は空", Optional = true)]
    [PLResult("manualName",     PLResultKind.Text,      Description = "人の操作への置き換えの名前", Optional = true)]
    [PLResult("manualNote",     PLResultKind.Text,      Description = "置き換えの説明", Optional = true)]
    [PLResult("manualItems",    PLResultKind.TextArray, Description = "置き換えのボタン・入力欄の ID", Optional = true)]
    [PLResult("manualLabels",   PLResultKind.TextArray, Description = "manualItems の見出し。未構築は空", Optional = true)]
    [PLResult("argNames",       PLResultKind.TextArray, Description = "引数のキー")]
    [PLResult("argControls",    PLResultKind.TextArray, Description = "入力欄の ID。argNames と同じ並び")]
    [PLResult("argLabels",      PLResultKind.TextArray, Description = "入力欄の見出し。argNames と同じ並び")]
    [PLResult("argBuilt",       PLResultKind.IntegerArray, Description = "欄が今あれば 1。argNames と同じ並び")]
    [PLResult("argValues",      PLResultKind.TextArray, Description = "渡した値。argNames と同じ並び")]
    [PLResult("argUiValues",    PLResultKind.TextArray, Description = "uiSetValue に渡す値。argNames と同じ並び")]
    [PLResult("argValueLabels", PLResultKind.TextArray, Description = "値の表記（選択肢の文字、オン／オフ）。argNames と同じ並び")]
    [PLResult("argCurrent",     PLResultKind.TextArray, Description = "欄の今の値。argNames と同じ並び")]
    [PLResult("argProblems",    PLResultKind.TextArray, Description = "読み替えられなかった理由。無ければ空。argNames と同じ並び")]
    [PLResult("noFieldArgs",    PLResultKind.TextArray, Description = "画面に入力欄が無い引数")]
    [PLResult("noFieldReasons", PLResultKind.TextArray, Description = "欄が無い理由。noFieldArgs と同じ並び")]
    [PLResult("fixedControls",    PLResultKind.TextArray, Description = "引数に当たらないが、このコマンドと同じ結果にするために決まった値にする入力欄の ID")]
    [PLResult("fixedLabels",      PLResultKind.TextArray, Description = "その見出し。未構築は空。fixedControls と同じ並び")]
    [PLResult("fixedUiValues",    PLResultKind.TextArray, Description = "uiSetValue に渡す値。fixedControls と同じ並び")]
    [PLResult("fixedValueLabels", PLResultKind.TextArray, Description = "値の表記。未構築は空。fixedControls と同じ並び")]
    public sealed class QueryCommandUiOperationCommand : PanelCommand
    {
        [PLParam(Description = "コマンド名（polyling_call に渡す名前）", Required = true)]
        public string CommandName { get; }

        [PLParam(Description = "読み替える引数のキー。省くと宣言した引数の全部を返す（値の表記は空）")]
        public string[] ArgKeys { get; }

        [PLParam(Description = "argKeys と同じ並びの値（シナリオの項目の引数の値と同じ書き方）")]
        public string[] ArgValues { get; }

        public QueryCommandUiOperationCommand(int modelIndex, string commandName, string[] argKeys = null, string[] argValues = null)
            : base(modelIndex)
        {
            CommandName = commandName ?? "";
            ArgKeys     = argKeys ?? System.Array.Empty<string>();
            ArgValues   = argValues ?? System.Array.Empty<string>();
        }
    }

    /// <summary>指定したボタンや入力欄を枠で強調する。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.None, Description = "指定したボタンや入力欄を枠で強調する。add を立てると今出ている枠を残して足す（折り畳みの見出しとその中のボタンを同時に囲むなど）。立てないと前の枠を消してから強調する。右ペインのパネルを切り替えると枠は全部消える。ボタンや入力欄が表示されていないときは失敗する（先に uiReveal を使う）。左ペインのボタンや入力欄は panelId=leftPane のボタンや入力欄。")]
    public sealed class UiHighlightCommand : PanelCommand
    {
        [PLParam(Description = "ボタン・入力欄の ID。uiDescribe の controlIds のどれか", Required = true)]
        public string ControlId { get; }

        [PLParam(Description = "true で強調する。false でこのボタンや入力欄の強調を消す。省くと強調する")]
        public bool Enabled { get; }

        [PLParam(Description = "今出ている枠を残して足す。省くと前の枠を消してから強調する")]
        public bool Add { get; }

        public UiHighlightCommand(int modelIndex, string controlId, bool enabled = true, bool add = false)
            : base(modelIndex)
        {
            ControlId = controlId ?? "";
            Enabled   = enabled;
            Add       = add;
        }
    }

    /// <summary>強調の枠を全部消す。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.None, Description = "uiHighlight / uiReveal で出した強調の枠を全部消す。")]
    public sealed class UiClearHighlightsCommand : PanelCommand
    {
        public UiClearHighlightsCommand(int modelIndex = 0) : base(modelIndex) { }
    }

    /// <summary>画面キャプチャを始める。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.None, Description = "画面キャプチャを始める。撮影はフレーム終端で行うため、完了と保存先は uiCaptureStatus で captureId を指定して取得する。folder を省くとキャプチャパネルで設定した保存先フォルダへ保存する。panelId でパネル 1 つに絞り、maxLongEdge で縮めてから保存できる。cursor で画像に矢印を描ける（実際のカーソルを撮るのではない）。")]
    [PLResult("captureId", PLResultKind.Text, Description = "キャプチャの受付番号")]
    [PLResult("status",    PLResultKind.Text, Description = "pending / completed / failed")]
    public sealed class UiCaptureCommand : PanelCommand
    {
        [PLParam(Description = "撮影範囲。MainView（メイン3D画面）/ TriView（3面図を含むビューポート領域）/ Window（ウインドウ全体）。省くと Window")]
        public string Target { get; }

        [PLParam(Description = "パネル ID。指定するとそのパネルの矩形だけを撮り、target は見ない。uiDescribe の panelIds のどれか。表示されていないパネルは撮れない")]
        public string PanelId { get; }

        [PLParam(Description = "長辺の上限[px]。これを超えるときだけ縮めて保存する。0 なら等倍", Min = 0)]
        public int MaxLongEdge { get; }

        [PLParam(Description = "書き出す形式。png / jpeg。省くと png")]
        public string Format { get; }

        [PLParam(Description = "jpeg の品質。1〜100。png では使わない", Min = 1, Max = 100)]
        public int Quality { get; }

        [PLParam(Description = "ファイル名の土台。連番と拡張子が付く。省くと PolyLingTutorial")]
        public string BaseName { get; }

        [PLParam(Description = "保存先フォルダ。作業フォルダからの相対パスで、作業フォルダの外は指定できない。作業フォルダ直下は . 。無いフォルダは作る。省くとキャプチャパネルで設定した保存先フォルダ")]
        public string Folder { get; }

        [PLParam(Description = "画像に描く矢印の位置の決め方。none（描かない）/ control（ボタンや入力欄の中心）/ position（撮影範囲に対する比率）/ mouse（最後にポインタが動いた位置）。省くと none。範囲の外なら描かず、uiCaptureStatus の cursorInside が false になる")]
        public string Cursor { get; }

        [PLParam(Description = "cursor=control のときのボタン・入力欄の ID。uiDescribe の controlIds のどれか。表示されていないものは撮る前に失敗する（先に uiReveal を使う）")]
        public string CursorControlId { get; }

        [PLParam(Description = "cursor=position のときの横の位置。撮影範囲に対する比率で、0 が左端、1 が右端")]
        public float CursorX { get; }

        [PLParam(Description = "cursor=position のときの縦の位置。撮影範囲に対する比率で、0 が上端、1 が下端")]
        public float CursorY { get; }

        public UiCaptureCommand(
            int modelIndex, string target = "Window", string baseName = "PolyLingTutorial", string folder = "",
            string panelId = "", int maxLongEdge = 0, string format = "png", int quality = 90,
            string cursor = "none", string cursorControlId = "", float cursorX = -1f, float cursorY = -1f)
            : base(modelIndex)
        {
            Cursor          = string.IsNullOrEmpty(cursor) ? "none" : cursor;
            CursorControlId = cursorControlId ?? "";
            CursorX         = cursorX;
            CursorY         = cursorY;
            Target      = string.IsNullOrEmpty(target)   ? "Window"           : target;
            BaseName    = string.IsNullOrEmpty(baseName) ? "PolyLingTutorial" : baseName;
            Folder      = folder ?? "";
            PanelId     = panelId ?? "";
            MaxLongEdge = maxLongEdge < 0 ? 0 : maxLongEdge;
            Format      = string.IsNullOrEmpty(format) ? "png" : format;
            Quality     = quality;
        }
    }

    /// <summary>画面キャプチャの状態を返す。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.None, Description = "uiCapture で始めた画面キャプチャの状態を返す。")]
    [PLResult("captureId", PLResultKind.Text, Description = "キャプチャの受付番号")]
    [PLResult("status",    PLResultKind.Text, Description = "pending / completed / failed")]
    [PLResult("path",      PLResultKind.Text, Description = "保存した画像のパス。completed のときだけ", Optional = true)]
    [PLResult("error",     PLResultKind.Text, Description = "失敗理由。failed のときだけ", Optional = true)]
    [PLResult("cursorX",      PLResultKind.Number, Description = "矢印の先の横の位置。撮影範囲に対する比率で、0 が左端。cursor を指定して completed のときだけ", Optional = true)]
    [PLResult("cursorY",      PLResultKind.Number, Description = "矢印の先の縦の位置。撮影範囲に対する比率で、0 が上端。cursor を指定して completed のときだけ", Optional = true)]
    [PLResult("cursorInside", PLResultKind.Flag,   Description = "矢印の先が撮影範囲の中にあり、描いたか。cursor を指定して completed のときだけ", Optional = true)]
    public sealed class UiCaptureStatusCommand : PanelCommand
    {
        [PLParam(Description = "uiCapture が返した captureId", Required = true)]
        public string CaptureId { get; }

        public UiCaptureStatusCommand(int modelIndex, string captureId)
            : base(modelIndex)
        {
            CaptureId = captureId ?? "";
        }
    }

    /// <summary>頂点が撮影範囲のどこに映るかを返す。</summary>
    [PLCommand(Category = "ui", Writes = PLWriteScope.None, Description = "頂点がメイン 3D 画面のどこに映るかを、撮影範囲に対する比率で返す。結果の x・y を uiCapture の cursor=position の cursorX・cursorY へ渡すと矢印で指せる。位置は GPU の計算をこの呼び出しのときにやり直して読むので、マウスを動かしていなくても今のカメラの値になる。3 面図の位置は返さない。モデルは変えない。")]
    [PLResult("x",       PLResultKind.Number, Description = "横の位置。撮影範囲に対する比率で、0 が左端。画面の位置が無い頂点（隠した頂点、カメラの後ろの頂点）は -1")]
    [PLResult("y",       PLResultKind.Number, Description = "縦の位置。撮影範囲に対する比率で、0 が上端。画面の位置が無い頂点は -1")]
    [PLResult("inside",  PLResultKind.Flag,   Description = "撮影範囲の中か")]
    [PLResult("visible", PLResultKind.Flag,   Description = "裏向きでないか。背面カリングの判定で、他の物の陰に隠れているかは見ない。メイン 3D 画面の背面カリングを切っていると、画面の位置がある頂点はすべて true")]
    public sealed class QueryCapturePositionCommand : PanelCommand
    {
        [PLParam(Description = "オブジェクトの索引", Required = true, IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int MasterIndex { get; }

        [PLParam(Description = "そのオブジェクトの頂点番号", Required = true)]
        public int VertexIndex { get; }

        [PLParam(Description = "撮影範囲。MainView / TriView / Window。uiCapture の target と同じものを指定する。省くと MainView")]
        public string Target { get; }

        public QueryCapturePositionCommand(int modelIndex, int masterIndex, int vertexIndex, string target = "MainView")
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            VertexIndex = vertexIndex;
            Target      = string.IsNullOrEmpty(target) ? "MainView" : target;
        }
    }
}
