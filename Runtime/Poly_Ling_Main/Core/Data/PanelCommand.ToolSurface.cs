// PanelCommand.ToolSurface.cs
// ツールハンドラの公開層（操作経路統一計画.md B・C・D）を MCP・リモートから使うコマンド。
// ツールは [PLTool("名前")] を付けたハンドラ。パラメータ・操作・概要は PLToolSurface が名前で扱う。

namespace Poly_Ling.Data
{
    /// <summary>ツールのパラメータ・概要・操作の一覧を返す。モデルは変えない。</summary>
    [PLCommand(Category = "tool", Writes = PLWriteScope.None, Description = "ツールのパラメータ（現在値）・概要・呼べる操作を返す。toolId を省くとツール名の一覧を返す。モデルは変えない。")]
    [PLResult("tools",       PLResultKind.TextArray, Description = "toolId を省いたとき：ツール名の一覧")]
    [PLResult("paramNames",  PLResultKind.TextArray, Description = "パラメータ名")]
    [PLResult("paramTypes",  PLResultKind.TextArray, Description = "パラメータの型名")]
    [PLResult("paramValues", PLResultKind.TextArray, Description = "パラメータの現在値（コマンド引数と同じ文字列表現）")]
    [PLResult("stateNames",  PLResultKind.TextArray, Description = "概要の名前")]
    [PLResult("stateValues", PLResultKind.TextArray, Description = "概要の値")]
    [PLResult("actions",     PLResultKind.TextArray, Description = "呼べる操作の名前")]
    public class QueryToolStateCommand : PanelCommand
    {
        [PLParam(Description = "ツール名。省くとツール名の一覧を返す")]
        public string ToolId { get; }

        public QueryToolStateCommand(int modelIndex, string toolId = "")
            : base(modelIndex) { ToolId = toolId ?? ""; }
    }

    /// <summary>ツールのパラメータを設定し、設定後の実際の値を返す。ハンドラの設定値でありモデルではない。</summary>
    [PLCommand(Category = "tool", Writes = PLWriteScope.None, Description = "ツールのパラメータを名前で設定し、設定後の実際の値を返す。")]
    [PLResult("value", PLResultKind.Text, Description = "設定後の実際の値")]
    public class SetToolParamCommand : PanelCommand
    {
        [PLParam(Description = "ツール名", Required = true)]
        public string ToolId { get; }

        [PLParam(Description = "パラメータ名", Required = true)]
        public string Name { get; }

        [PLParam(Description = "値（コマンド引数と同じ文字列表現）", Required = true)]
        public string Value { get; }

        public SetToolParamCommand(int modelIndex, string toolId, string name, string value)
            : base(modelIndex)
        {
            ToolId = toolId ?? "";
            Name   = name ?? "";
            Value  = value ?? "";
        }
    }

    /// <summary>
    /// ツールの操作を名前で呼ぶ。操作の中で送られるコマンドは入れ子として各段で担当者判定を受ける。
    /// 引数のある操作は argKeys（引数名）と argValues（値。コマンド引数と同じ文字列表現）で渡す。
    /// </summary>
    [PLCommand(Category = "tool", Writes = PLWriteScope.None, Description = "ツールの操作を名前で呼ぶ。引数のある操作は argKeys と argValues で渡す。操作の中で送られるコマンドはそれぞれ担当者判定を受ける。")]
    public class InvokeToolActionCommand : PanelCommand
    {
        [PLParam(Description = "ツール名", Required = true)]
        public string ToolId { get; }

        [PLParam(Description = "操作名", Required = true)]
        public string Action { get; }

        [PLParam(Description = "引数名の並び（queryToolState の actions に出る引数名）")]
        public string[] ArgKeys { get; }

        [PLParam(Description = "引数の値の並び（argKeys と同じ順）")]
        public string[] ArgValues { get; }

        public InvokeToolActionCommand(int modelIndex, string toolId, string action,
            string[] argKeys = null, string[] argValues = null)
            : base(modelIndex)
        {
            ToolId    = toolId ?? "";
            Action    = action ?? "";
            ArgKeys   = argKeys   ?? System.Array.Empty<string>();
            ArgValues = argValues ?? System.Array.Empty<string>();
        }
    }
}
