// PanelCommand.Scene.cs
// 利用シーン（SceneLibrary）を読む・作る・消すコマンドと、版の照会（queryRevisions）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// シーンはプロジェクトにもモデルにも属さない（SceneLibrary.cs の注記）。
// 振り分けは PlayerCommandDispatcher.Scene.cs が、手本と同じくプロジェクトの null 門より前で行う。
// 一覧の値はコマンドの戻り値に入れ子の配列を持てないため、シーンごとに ';' で連結した文字列で返す。

using System.Collections.Generic;

namespace Poly_Ling.Data
{
    /// <summary>モデル・道具一覧・手本・シーンの版を返す。</summary>
    [PLCommand(Writes = PLWriteScope.None, Category = "mcp",
        Description = "モデル・道具一覧・手本・利用シーンの版を返す。前に見たときと同じ版なら取り直さなくてよい。モデルの版は書き込みのあるコマンドが成功するたびに 1 つ進む。")]
    [PLResult("modelIndex",       PLResultKind.Integer, Description = "読んだモデルの索引")]
    [PLResult("modelRevision",    PLResultKind.Integer, Description = "モデルの版。モデルが無ければ 0")]
    [PLResult("models",           PLResultKind.Integer, Description = "プロジェクトが持つモデルの数")]
    [PLResult("schemaRevision",   PLResultKind.Text,    Description = "道具一覧の版。コマンドの顔ぶれが変わると変わる")]
    [PLResult("scenarioRevision", PLResultKind.Integer, Description = "手本の置き場の版")]
    [PLResult("sceneRevision",    PLResultKind.Integer, Description = "利用シーンの置き場の版")]
    public sealed class QueryRevisionsCommand : PanelCommand
    {
        public QueryRevisionsCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>登録済みの利用シーンを一覧する。</summary>
    [PLCommand(Writes = PLWriteScope.None, Category = "mcp",
        Description = "登録済みの利用シーンを一覧する。シーンは MCP の道具と検索対象のコマンドを作業の場面ごとに絞る定義。一覧の値はシーンごとに ';' で連結して返す。")]
    [PLResult("count",       PLResultKind.Integer,   Description = "シーンの数")]
    [PLResult("storePath",   PLResultKind.Text,      Description = "シーンを置いているファイルの絶対パス")]
    [PLResult("names",       PLResultKind.TextArray, Description = "シーンの名前", Optional = true)]
    [PLResult("descriptions", PLResultKind.TextArray, Description = "説明。names と同じ並び", Optional = true)]
    [PLResult("commands",    PLResultKind.TextArray, Description = "対象コマンド名を ';' で連結。names と同じ並び", Optional = true)]
    [PLResult("categories",  PLResultKind.TextArray, Description = "対象分類を ';' で連結。names と同じ並び", Optional = true)]
    [PLResult("tags",        PLResultKind.TextArray, Description = "対象タグを ';' で連結。names と同じ並び", Optional = true)]
    [PLResult("tools",       PLResultKind.TextArray, Description = "見せる MCP の固定の道具を ';' で連結。空 = 絞らない。names と同じ並び", Optional = true)]
    public sealed class QueryScenesCommand : PanelCommand
    {
        [PLParam(Description = "ファイルから読み直してから返す。手で編集したあとに使う")]
        public bool Reload { get; }

        public QueryScenesCommand(int modelIndex = 0, bool reload = false)
            : base(modelIndex)
        {
            Reload = reload;
        }
    }

    /// <summary>利用シーンを作る、または差し替える。</summary>
    [PLCommand(Writes = PLWriteScope.None, Category = "mcp",
        Description = "利用シーンを作る。同じ名前があるときは overwrite で差し替える。commands・categories・tags のどれかに当たるコマンドが検索対象になり、3 つとも省くと全コマンドが対象。tools は見せる MCP の固定の道具で、省くと絞らない。")]
    [PLResult("name",      PLResultKind.Text,    Description = "登録したシーンの名前")]
    [PLResult("count",     PLResultKind.Integer, Description = "登録後のシーンの数")]
    [PLResult("storePath", PLResultKind.Text,    Description = "シーンを置いているファイルの絶対パス")]
    public sealed class SetSceneCommand : PanelCommand
    {
        [PLParam(Description = "シーンの名前。大小文字は区別しない", Required = true)]
        public string Name { get; }

        [PLParam(Description = "どんな作業の場面か")]
        public string Description { get; }

        [PLParam(Description = "対象にするコマンド名")]
        public string[] Commands { get; }

        [PLParam(Description = "対象にする分類。コマンドの分類（polyling_search の category）と照合する")]
        public string[] Categories { get; }

        [PLParam(Description = "対象にするタグ。コマンドのタグのどれかと一致すれば対象")]
        public string[] Tags { get; }

        [PLParam(Description = "見せる MCP の固定の道具の名前（例 read_lines, unity_capture）。省くと絞らない")]
        public string[] Tools { get; }

        [PLParam(Description = "同じ名前のシーンがあるとき差し替える")]
        public bool Overwrite { get; }

        public SetSceneCommand(int modelIndex, string name, string description = "",
            string[] commands = null, string[] categories = null, string[] tags = null, string[] tools = null,
            bool overwrite = false)
            : base(modelIndex)
        {
            Name        = name ?? "";
            Description = description ?? "";
            Commands    = commands   ?? new string[0];
            Categories  = categories ?? new string[0];
            Tags        = tags       ?? new string[0];
            Tools       = tools      ?? new string[0];
            Overwrite   = overwrite;
        }

        /// <summary>SceneLibrary へ渡す定義にする。</summary>
        public SceneDefinition ToDefinition() => new SceneDefinition
        {
            Name        = Name,
            Description = Description,
            Commands    = new List<string>(Commands),
            Categories  = new List<string>(Categories),
            Tags        = new List<string>(Tags),
            Tools       = new List<string>(Tools),
        };
    }

    /// <summary>利用シーンを消す。</summary>
    [PLCommand(Writes = PLWriteScope.None, Category = "mcp", Description = "利用シーンを消す。ファイルからも消える。")]
    [PLResult("removed", PLResultKind.Flag,    Description = "消したか")]
    [PLResult("count",   PLResultKind.Integer, Description = "消した後のシーンの数")]
    public sealed class DeleteSceneCommand : PanelCommand
    {
        [PLParam(Description = "消すシーンの名前", Required = true)]
        public string Name { get; }

        public DeleteSceneCommand(int modelIndex, string name)
            : base(modelIndex)
        {
            Name = name ?? "";
        }
    }
}
