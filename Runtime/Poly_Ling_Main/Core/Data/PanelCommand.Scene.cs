// PanelCommand.Scene.cs
// 利用シーン（SceneLibrary）を読む・作る・消すコマンドと、版の照会（queryRevisions）・モデル状態の照会（queryModelState）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置
//
// 利用シーンはプロジェクトにもモデルにも属さない（SceneLibrary.cs の注記）。
// 振り分けは PlayerCommandDispatcher.Scene.cs が、手本と同じくプロジェクトの null 門より前で行う。
// 一覧の値はコマンドの戻り値に入れ子の配列を持てないため、利用シーンごとに ';' で連結した文字列で返す。

using System.Collections.Generic;

namespace Poly_Ling.Data
{
    /// <summary>モデル・道具一覧・手本・利用シーンの版を返す。</summary>
    [PLCommand(Writes = PLWriteScope.None, Category = "mcp",
        Description = "モデル・道具一覧・手本・利用シーンの版と、いま続いている一時的な状態（手本の記録中・実行中、モーフやブレンドのプレビュー中）を返す。前に見たときと同じ版なら取り直さなくてよい。モデルの版は書き込みのあるコマンドが成功するたびに 1 つ進む。一時的な状態の間は、その状態を終えるまで避けるべき操作がある。")]
    [PLResult("modelIndex",       PLResultKind.Integer, Description = "読んだモデルの索引")]
    [PLResult("modelRevision",    PLResultKind.Integer, Description = "モデルの版。モデルが無ければ 0")]
    [PLResult("models",           PLResultKind.Integer, Description = "プロジェクトが持つモデルの数")]
    [PLResult("schemaRevision",   PLResultKind.Text,    Description = "道具一覧の版。コマンドの顔ぶれが変わると変わる")]
    [PLResult("scenarioRevision", PLResultKind.Integer, Description = "手本の置き場の版")]
    [PLResult("sceneRevision",    PLResultKind.Integer, Description = "利用シーンの置き場の版")]
    [PLResult("activeModes",      PLResultKind.TextArray, Description = "いま続いている一時的な状態。scenarioRecording / scenarioRun / morphPreview / blendPreview。無ければ空", Optional = true)]
    public sealed class QueryRevisionsCommand : PanelCommand
    {
        public QueryRevisionsCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>モデルがいまどこまで作られているかを返す。</summary>
    [PLCommand(Writes = PLWriteScope.None, Category = "query",
        Description = "カレントモデルの状態を実データから調べて返す。ボーン・スキンウェイト・モーフ・Humanoid 割当・スプリングボーン・ミラー関係の有無と、そこから導く topologyLocked（スキンウェイトかモーフがあり、頂点の増減で壊れる状態）。利用シーンの stateAssumptions と同じ名前で返す。モデルは変えない。")]
    [PLResult("hasModel",          PLResultKind.Flag, Description = "カレントモデルがあるか。無ければ他はすべて false")]
    [PLResult("hasBones",          PLResultKind.Flag, Description = "ボーンがある")]
    [PLResult("hasSkinWeights",    PLResultKind.Flag, Description = "スキンウェイトを持つオブジェクトがある")]
    [PLResult("hasMorphs",         PLResultKind.Flag, Description = "モーフ（表情）がある")]
    [PLResult("hasHumanoid",       PLResultKind.Flag, Description = "Humanoid の割当がある")]
    [PLResult("humanoidComplete",  PLResultKind.Flag, Description = "Humanoid の必須ボーンが揃っている")]
    [PLResult("hasCustomRig",      PLResultKind.Flag, Description = "ボーンはあるが Humanoid の割当が無い")]
    [PLResult("hasSpringBones",    PLResultKind.Flag, Description = "スプリングボーンの関節か鎖の根がある")]
    [PLResult("hasMirrorRelation", PLResultKind.Flag, Description = "ミラー設定かミラーの対がある")]
    [PLResult("topologyLocked",    PLResultKind.Flag, Description = "hasSkinWeights か hasMorphs。頂点や面を増減するとウェイトかモーフが壊れる")]
    public sealed class QueryModelStateCommand : PanelCommand
    {
        public QueryModelStateCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>登録済みの利用シーンを一覧する。</summary>
    [PLCommand(Writes = PLWriteScope.None, Category = "mcp",
        Description = "登録済みの利用シーンを一覧する。利用シーンは、作業の一局面について検索するコマンド・見せる MCP の道具・危険な操作の扱い・実行後の確認を決めたプリセット（Unity のシーンではない）。一覧の値は利用シーンごとに ';' で連結して返す。")]
    [PLResult("count",              PLResultKind.Integer,   Description = "利用シーンの数")]
    [PLResult("storePath",          PLResultKind.Text,      Description = "利用シーンを置いているファイルの絶対パス")]
    [PLResult("names",              PLResultKind.TextArray, Description = "利用シーンの名前", Optional = true)]
    [PLResult("descriptions",       PLResultKind.TextArray, Description = "説明。names と同じ並び", Optional = true)]
    [PLResult("origins",            PLResultKind.TextArray, Description = "builtin（同梱の既製品。変更・削除できない）か user（利用者が作ったもの）。names と同じ並び", Optional = true)]
    [PLResult("explicitCommands",   PLResultKind.TextArray, Description = "分類に関係なく候補に入れるコマンド名を ';' で連結。names と同じ並び", Optional = true)]
    [PLResult("includeCategories",  PLResultKind.TextArray, Description = "候補にする分類を ';' で連結。names と同じ並び", Optional = true)]
    [PLResult("boostTags",          PLResultKind.TextArray, Description = "順位を上げるタグを ';' で連結。names と同じ並び", Optional = true)]
    [PLResult("tools",              PLResultKind.TextArray, Description = "見せる MCP の固定の道具を ';' で連結。空 = 絞らない。names と同じ並び", Optional = true)]
    [PLResult("excludeCommands",    PLResultKind.TextArray, Description = "候補から外すコマンド名を ';' で連結。names と同じ並び", Optional = true)]
    [PLResult("stateAssumptions",   PLResultKind.TextArray, Description = "想定するモデル状態（名前=true/false）を ';' で連結。names と同じ並び", Optional = true)]
    [PLResult("hazardPolicy",       PLResultKind.TextArray, Description = "危険性ごとの扱い（危険性=allow/warn/require-confirmation/hide）を ';' で連結。names と同じ並び", Optional = true)]
    [PLResult("verificationPolicy", PLResultKind.TextArray, Description = "実行後に確かめることを ';' で連結。names と同じ並び", Optional = true)]
    [PLResult("relatedScenarios",   PLResultKind.TextArray, Description = "関係する手本の名前を ';' で連結。names と同じ並び", Optional = true)]
    [PLResult("notes",              PLResultKind.TextArray, Description = "注意書き。names と同じ並び", Optional = true)]
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
        Description = "利用シーンを作る。同じ名前があるときは overwrite で差し替える。既製の利用シーン（origin=builtin）と同じ名前は使えない。explicitCommands に名前があるか includeCategories に属するコマンドが検索の候補になり、両方省くと全コマンドが候補。includeCategories は階層で、geometry は geometry.topology なども含む。boostTags は候補を増やさず順位を上げる。hazardPolicy で危険な操作を警告・承認要求・非表示にでき、stateAssumptions が実際のモデル状態と食い違うと検索で警告する。")]
    [PLResult("name",      PLResultKind.Text,    Description = "登録した利用シーンの名前")]
    [PLResult("count",     PLResultKind.Integer, Description = "登録後の利用シーンの数")]
    [PLResult("storePath", PLResultKind.Text,    Description = "利用シーンを置いているファイルの絶対パス")]
    public sealed class SetSceneCommand : PanelCommand
    {
        [PLParam(Description = "利用シーンの名前。大小文字は区別しない", Required = true)]
        public string Name { get; }

        [PLParam(Description = "どんな作業の局面か")]
        public string Description { get; }

        [PLParam(Description = "分類に関係なく候補に入れるコマンド名")]
        public string[] ExplicitCommands { get; }

        [PLParam(Description = "候補にする分類。階層で指定でき、geometry は geometry.topology なども含む。使える分類は polyling_scenes の応答にある")]
        public string[] IncludeCategories { get; }

        [PLParam(Description = "順位を上げるタグ。候補は増やさない")]
        public string[] BoostTags { get; }

        [PLParam(Description = "見せる MCP の固定の道具の名前（例 read_lines, unity_capture）。省くと絞らない")]
        public string[] Tools { get; }

        [PLParam(Description = "候補から外すコマンド名")]
        public string[] ExcludeCommands { get; }

        [PLParam(Description = "想定するモデル状態。名前=true または 名前=false。名前は queryModelState の戻り値と同じ（hasSkinWeights, topologyLocked など）")]
        public string[] StateAssumptions { get; }

        [PLParam(Description = "危険性ごとの扱い。危険性=扱い。扱いは allow / warn / require-confirmation / hide。危険性の名前は polyling_describe の hazards と同じ（InvalidatesMorphs など）")]
        public string[] HazardPolicy { get; }

        [PLParam(Description = "この利用シーンで実行後に確かめること。名前は polyling_describe の verification と同じ（VertexCount, MorphIntegrity など）")]
        public string[] VerificationPolicy { get; }

        [PLParam(Description = "関係する手本（シナリオ）の名前")]
        public string[] RelatedScenarios { get; }

        [PLParam(Description = "注意書き。禁忌を承知で破る事情など、機械的に表せない例外を書く")]
        public string Notes { get; }

        [PLParam(Description = "同じ名前の利用シーンがあるとき差し替える")]
        public bool Overwrite { get; }

        public SetSceneCommand(int modelIndex, string name, string description = "",
            string[] explicitCommands = null, string[] includeCategories = null, string[] boostTags = null,
            string[] tools = null, string[] excludeCommands = null, string[] stateAssumptions = null,
            string[] hazardPolicy = null, string[] verificationPolicy = null, string[] relatedScenarios = null,
            string notes = "", bool overwrite = false)
            : base(modelIndex)
        {
            Name               = name ?? "";
            Description        = description ?? "";
            ExplicitCommands   = explicitCommands   ?? new string[0];
            IncludeCategories  = includeCategories  ?? new string[0];
            BoostTags          = boostTags          ?? new string[0];
            Tools              = tools              ?? new string[0];
            ExcludeCommands    = excludeCommands    ?? new string[0];
            StateAssumptions   = stateAssumptions   ?? new string[0];
            HazardPolicy       = hazardPolicy       ?? new string[0];
            VerificationPolicy = verificationPolicy ?? new string[0];
            RelatedScenarios   = relatedScenarios   ?? new string[0];
            Notes              = notes ?? "";
            Overwrite          = overwrite;
        }

        /// <summary>
        /// SceneLibrary へ渡す定義にする。キーや名前の誤りは error に入れて null を返す。
        /// 分類の検証（正典にあるか）は呼び出し側が行う。
        /// </summary>
        public SceneDefinition ToDefinition(out string error)
        {
            var d = new SceneDefinition
            {
                Name              = Name,
                Description       = Description,
                ExplicitCommands  = new List<string>(ExplicitCommands),
                IncludeCategories = new List<string>(IncludeCategories),
                BoostTags         = new List<string>(BoostTags),
                Tools             = new List<string>(Tools),
                ExcludeCommands   = new List<string>(ExcludeCommands),
                RelatedScenarios  = new List<string>(RelatedScenarios),
                Notes             = Notes,
            };
            if (!SceneDefinition.TryParseStateAssumptions(StateAssumptions, d.StateAssumptions, out error)) return null;
            if (!SceneDefinition.TryParseHazardPolicy(HazardPolicy, d.HazardPolicy, out error)) return null;
            if (!SceneDefinition.TryParseVerification(VerificationPolicy, out var v, out error)) return null;
            d.VerificationPolicy = v;
            return d;
        }
    }

    /// <summary>利用シーンを消す。</summary>
    [PLCommand(Writes = PLWriteScope.None, Category = "mcp", Description = "利用シーンを消す。ファイルからも消える。")]
    [PLResult("removed", PLResultKind.Flag,    Description = "消したか")]
    [PLResult("count",   PLResultKind.Integer, Description = "消した後の利用シーンの数")]
    public sealed class DeleteSceneCommand : PanelCommand
    {
        [PLParam(Description = "消す利用シーンの名前", Required = true)]
        public string Name { get; }

        public DeleteSceneCommand(int modelIndex, string name)
            : base(modelIndex)
        {
            Name = name ?? "";
        }
    }
}
