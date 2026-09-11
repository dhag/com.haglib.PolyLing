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
//   UiAutomationRegistry に明示登録されたパネル・項目だけ。
//   画面座標・表示文字列・型による探索はしない。
//
// 【リモート（WebSocket）からは受けない】
//   ホストの画面を他の参加者が動かさないよう、RemoteOwnership.TryAuthorize で拒否する。
//   MCP の経路（PolyLingCommandGateway）は RemoteOwnership を通らない。

namespace Poly_Ling.Data
{
    /// <summary>登録済みのパネル・項目の一覧を返す。</summary>
    [PLCommand(Description = "操作できる UI のパネルと項目の一覧を返す。項目の情報は controlIds と同じ並びの配列で返す。")]
    [PLResult("panelIds",            PLResultKind.TextArray,    Description = "パネル ID")]
    [PLResult("panelDescriptions",   PLResultKind.TextArray,    Description = "パネルの説明。panelIds と同じ並び")]
    [PLResult("controlIds",          PLResultKind.TextArray,    Description = "項目 ID", Optional = true)]
    [PLResult("controlPanels",       PLResultKind.TextArray,    Description = "項目が属するパネル ID。controlIds と同じ並び", Optional = true)]
    [PLResult("controlTypes",        PLResultKind.TextArray,    Description = "項目の UI 型名。controlIds と同じ並び", Optional = true)]
    [PLResult("controlDescriptions", PLResultKind.TextArray,    Description = "項目の説明。controlIds と同じ並び", Optional = true)]
    [PLResult("controlSafety",       PLResultKind.TextArray,    Description = "項目の安全度（readOnly / safeWrite / destructive / fileOperation / userOnly / unspecified）。controlIds と同じ並び", Optional = true)]
    [PLResult("controlWritable",     PLResultKind.IntegerArray, Description = "値を変更できれば 1、読むだけなら 0。controlIds と同じ並び", Optional = true)]
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
    [PLCommand(Description = "指定したパネルを右ペインに表示する。ボタンで開いたときと同じ処理を通る。")]
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

    /// <summary>指定した項目が見える状態にする。</summary>
    [PLCommand(Description = "指定した項目が見える状態にする。所属パネルを開き、折り畳みを開き、項目が見える位置までスクロールする。")]
    public sealed class UiRevealCommand : PanelCommand
    {
        [PLParam(Description = "項目 ID。uiDescribe の controlIds のどれか", Required = true)]
        public string ControlId { get; }

        [PLParam(Description = "表示したあと項目を枠で強調するか。省くと強調する")]
        public bool Highlight { get; }

        public UiRevealCommand(int modelIndex, string controlId, bool highlight = true)
            : base(modelIndex)
        {
            ControlId = controlId ?? "";
            Highlight = highlight;
        }
    }

    /// <summary>指定した項目の現在値を返す。</summary>
    [PLCommand(Description = "指定した項目の現在値を返す。パネルが表示されていなくても読める。")]
    [PLResult("controlId", PLResultKind.Text,      Description = "項目 ID")]
    [PLResult("type",      PLResultKind.Text,      Description = "項目の UI 型名")]
    [PLResult("value",     PLResultKind.Text,      Description = "現在値の文字列。数値は不変文化圏の書式")]
    [PLResult("choices",   PLResultKind.TextArray, Description = "選べる値。選択式の項目だけ", Optional = true)]
    [PLResult("min",       PLResultKind.Number,    Description = "下限。スライダーだけ", Optional = true)]
    [PLResult("max",       PLResultKind.Number,    Description = "上限。スライダーだけ", Optional = true)]
    public sealed class UiGetValueCommand : PanelCommand
    {
        [PLParam(Description = "項目 ID。uiDescribe の controlIds のどれか", Required = true)]
        public string ControlId { get; }

        public UiGetValueCommand(int modelIndex, string controlId)
            : base(modelIndex)
        {
            ControlId = controlId ?? "";
        }
    }

    /// <summary>指定した項目の値を変更する。</summary>
    [PLCommand(Description = "指定した項目の値を変更する。利用者が操作したときと同じ処理を通る。範囲を超えた値は項目側で丸められ、丸めた後の値を返す。")]
    [PLResult("controlId", PLResultKind.Text, Description = "項目 ID")]
    [PLResult("value",     PLResultKind.Text, Description = "変更後に読み戻した値")]
    public sealed class UiSetValueCommand : PanelCommand
    {
        [PLParam(Description = "項目 ID。uiDescribe の controlIds のどれか", Required = true)]
        public string ControlId { get; }

        [PLParam(Description = "設定する値の文字列。数値は小数点に . を使う。真偽値は true / false。選択式は uiGetValue の choices のどれか。一覧は選ぶ行の番号（-1 で選択を外す）", Required = true)]
        public string Value { get; }

        [PLParam(Description = "安全度が destructive / fileOperation の項目を変えるときだけ true にする。省くと false")]
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
    [PLCommand(Description = "指定したボタンを押す。利用者が押したときと同じ処理を通る。表示されていないボタン・無効なボタンは押せない（先に uiReveal を使う）。安全度が destructive / fileOperation のボタンは allowDestructive を true にしたときだけ押す。readOnly / userOnly / unspecified のボタンは押さない。")]
    [PLResult("controlId", PLResultKind.Text, Description = "押した項目の ID")]
    public sealed class UiClickCommand : PanelCommand
    {
        [PLParam(Description = "項目 ID。uiDescribe の controlIds のどれか", Required = true)]
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
    [PLCommand(Description = "UI 自動操作の登録状況を検査する。未登録のセクション・属性の付け忘れ・登録失敗・安全度が未指定のボタン・未対応の型・登録済みパネル内の未登録の部品の数を返す。すべて 0 なら右ペインの全項目が登録されている。")]
    [PLResult("report",               PLResultKind.Text,    Description = "検査結果の全文。複数行")]
    [PLResult("sections",             PLResultKind.Integer, Description = "右ペインのセクションの数")]
    [PLResult("panels",               PLResultKind.Integer, Description = "登録済みパネルの数")]
    [PLResult("controls",             PLResultKind.Integer, Description = "登録済み項目の数")]
    [PLResult("unregisteredSections", PLResultKind.Integer, Description = "登録されていないセクションの数")]
    [PLResult("missingAttributes",    PLResultKind.Integer, Description = "UiControl 属性の付け忘れの数")]
    [PLResult("registrationErrors",   PLResultKind.Integer, Description = "登録を拒否した数")]
    [PLResult("unspecifiedSafety",    PLResultKind.Integer, Description = "安全度が未指定のボタンの数")]
    [PLResult("unsupportedTypes",     PLResultKind.Integer, Description = "読み書きできない型の項目の数")]
    [PLResult("unregisteredElements", PLResultKind.Integer, Description = "登録済みパネルの中にある、登録されていない操作部品の数")]
    public sealed class QueryUiAutomationAuditCommand : PanelCommand
    {
        public QueryUiAutomationAuditCommand(int modelIndex = 0) : base(modelIndex) { }
    }

    /// <summary>指定した項目を枠で強調する。</summary>
    [PLCommand(Description = "指定した項目を枠で強調する。強調は 1 か所だけで、別のパネルへ切り替えると消える。項目が表示されていないときは失敗する（先に uiReveal を使う）。")]
    public sealed class UiHighlightCommand : PanelCommand
    {
        [PLParam(Description = "項目 ID。uiDescribe の controlIds のどれか", Required = true)]
        public string ControlId { get; }

        [PLParam(Description = "true で強調する。false でこの項目の強調を消す。省くと強調する")]
        public bool Enabled { get; }

        public UiHighlightCommand(int modelIndex, string controlId, bool enabled = true)
            : base(modelIndex)
        {
            ControlId = controlId ?? "";
            Enabled   = enabled;
        }
    }

    /// <summary>画面キャプチャを始める。</summary>
    [PLCommand(Description = "画面キャプチャを始める。撮影はフレーム終端で行うため、完了と保存先は uiCaptureStatus で captureId を指定して取得する。folder を省くとキャプチャパネルで設定した保存先フォルダへ保存する。")]
    [PLResult("captureId", PLResultKind.Text, Description = "キャプチャの受付番号")]
    [PLResult("status",    PLResultKind.Text, Description = "pending / completed / failed")]
    public sealed class UiCaptureCommand : PanelCommand
    {
        [PLParam(Description = "撮影範囲。MainView（メイン3D画面）/ TriView（3面図を含むビューポート領域）/ Window（ウインドウ全体）。省くと Window")]
        public string Target { get; }

        [PLParam(Description = "ファイル名の土台。連番と .png が付く。省くと PolyLingTutorial")]
        public string BaseName { get; }

        [PLParam(Description = "保存先フォルダ。作業フォルダからの相対パスで、作業フォルダの外は指定できない。作業フォルダ直下は . 。無いフォルダは作る。省くとキャプチャパネルで設定した保存先フォルダ")]
        public string Folder { get; }

        public UiCaptureCommand(
            int modelIndex, string target = "Window", string baseName = "PolyLingTutorial", string folder = "")
            : base(modelIndex)
        {
            Target   = string.IsNullOrEmpty(target)   ? "Window"           : target;
            BaseName = string.IsNullOrEmpty(baseName) ? "PolyLingTutorial" : baseName;
            Folder   = folder ?? "";
        }
    }

    /// <summary>画面キャプチャの状態を返す。</summary>
    [PLCommand(Description = "uiCapture で始めた画面キャプチャの状態を返す。")]
    [PLResult("captureId", PLResultKind.Text, Description = "キャプチャの受付番号")]
    [PLResult("status",    PLResultKind.Text, Description = "pending / completed / failed")]
    [PLResult("path",      PLResultKind.Text, Description = "保存した PNG のパス。completed のときだけ", Optional = true)]
    [PLResult("error",     PLResultKind.Text, Description = "失敗理由。failed のときだけ", Optional = true)]
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
}
