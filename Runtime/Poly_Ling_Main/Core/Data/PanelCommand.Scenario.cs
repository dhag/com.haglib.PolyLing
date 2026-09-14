// PanelCommand.Scenario.cs
// 手本（シナリオ）の置き場を読み書きする要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【モデルを見ない】
//   手本は ScenarioLibrary が持ち、プロジェクトにもモデルにも属さない。
//   ディスパッチャはプロジェクトの null 門より前で捌く
//   （PlayerCommandDispatcher.Scenario.cs）。何も読み込んでいない状態でも使える。
//   例外は saveScenarioFromGroup だけで、これは現在のモデルの
//   ObjectGroup を読むため、受け口の中でモデルの有無を見る。
//
// 【手本は実体に縛らない】
//   ObjectGroup のステップは MeshRefIds / OutputObjectIds で実在の
//   描画オブジェクトを指せるが、手本に焼き付けると、そのモデルを
//   閉じた時点で死んだ ObjectId が残る。手本を作るときは必ず落とす。
//
// 【引数は 2 本で運ぶ】
//   段ごとに個数の違う引数は、入れ子の配列として送れない
//   （PolyLing_コマンド追加の手引き.md「可変長のものは 2 本で持つ」）。
//   describeScenario は argCounts（段ごとの個数）と、
//   連結した argKeys / argValues を返す。
//   addScenarioStep / setScenarioStep は 1 段ぶんなので argKeys / argValues の 2 本。
//
// 【リモートからは受けない】
//   手本の置き場はホストの持ち物。RemoteOwnership が拒否する。
//   MCP の経路（PolyLingCommandGateway）はその判定を通らない。

namespace Poly_Ling.Data
{
    // ================================================================
    // 読む
    // ================================================================

    /// <summary>登録済みの手本を一覧する。</summary>
    [PLCommand(Description = "登録済みの手本（シナリオ）を一覧する。名前・目的・段数を同じ並びの配列で返す。")]
    [PLResult("count",      PLResultKind.Integer,      Description = "手本の数")]
    [PLResult("storePath",  PLResultKind.Text,         Description = "手本を置いているファイルの絶対パス")]
    [PLResult("names",      PLResultKind.TextArray,    Description = "手本の名前", Optional = true)]
    [PLResult("goals",      PLResultKind.TextArray,    Description = "手本の目的。names と同じ並び", Optional = true)]
    [PLResult("stepCounts", PLResultKind.IntegerArray, Description = "手本の段数。names と同じ並び", Optional = true)]
    public sealed class QueryScenariosCommand : PanelCommand
    {
        [PLParam(Description = "ファイルから読み直してから返す。手で編集したあとに使う")]
        public bool Reload { get; }

        public QueryScenariosCommand(int modelIndex = 0, bool reload = false)
            : base(modelIndex)
        {
            Reload = reload;
        }
    }

    /// <summary>手本 1 本の中身を返す。</summary>
    [PLCommand(Description = "手本 1 本の中身を返す。段は elementIds と同じ並びの配列で返し、引数は argCounts で区切った argKeys / argValues に連結して返す。")]
    [PLResult("name",            PLResultKind.Text,         Description = "手本の名前")]
    [PLResult("goal",            PLResultKind.Text,         Description = "この手本で達成したいこと")]
    [PLResult("parentName",      PLResultKind.Text,         Description = "元にした手本の名前。空 = 元がない")]
    [PLResult("changeSummary",   PLResultKind.Text,         Description = "元から何を変えたか")]
    [PLResult("createdBy",       PLResultKind.Text,         Description = "作った者")]
    [PLResult("steps",           PLResultKind.Integer,      Description = "段の数")]
    [PLResult("preconditions",   PLResultKind.TextArray,    Description = "使う前に満たしているべきこと", Optional = true)]
    [PLResult("successCriteria", PLResultKind.TextArray,    Description = "終わったときに確かめること", Optional = true)]
    [PLResult("tags",            PLResultKind.TextArray,    Description = "探すための札", Optional = true)]
    [PLResult("elementIds",      PLResultKind.TextArray,    Description = "段を指す名前", Optional = true)]
    [PLResult("kinds",           PLResultKind.TextArray,    Description = "段の種別。elementIds と同じ並び", Optional = true)]
    [PLResult("actions",         PLResultKind.TextArray,    Description = "段のコマンド名。実行しない段は空。elementIds と同じ並び", Optional = true)]
    [PLResult("purposes",        PLResultKind.TextArray,    Description = "段が要る理由。elementIds と同じ並び", Optional = true)]
    [PLResult("argCounts",       PLResultKind.IntegerArray, Description = "段ごとの引数の数。elementIds と同じ並び", Optional = true)]
    [PLResult("argKeys",         PLResultKind.TextArray,    Description = "全段の引数のキーを段の順に連結したもの。argCounts で区切る", Optional = true)]
    [PLResult("argValues",       PLResultKind.TextArray,    Description = "argKeys と同じ並びの値", Optional = true)]
    [PLResult("refNames",        PLResultKind.TextArray,    Description = "参照先の手本の名前。参照段以外は空。elementIds と同じ並び", Optional = true)]
    [PLResult("expansionPolicies", PLResultKind.TextArray,  Description = "参照段の扱い方。参照段以外は空。elementIds と同じ並び", Optional = true)]
    [PLResult("scenarioNames",   PLResultKind.TextArray,    Description = "その段が載っている手本の名前。resolve のときだけ。elementIds と同じ並び", Optional = true)]
    [PLResult("depths",          PLResultKind.IntegerArray, Description = "参照の深さ。0 = 起点。resolve のときだけ。elementIds と同じ並び", Optional = true)]
    public sealed class DescribeScenarioCommand : PanelCommand
    {
        [PLParam(Description = "手本の名前。queryScenarios の names のどれか", Required = true)]
        public string Name { get; }

        [PLParam(Description = "参照段を辿って平たい段の列で返す。参照段そのものは列に出ない")]
        public bool Resolve { get; }

        public DescribeScenarioCommand(int modelIndex, string name, bool resolve = false)
            : base(modelIndex)
        {
            Name    = name ?? "";
            Resolve = resolve;
        }
    }

    // ================================================================
    // 作る・消す
    // ================================================================

    /// <summary>段を持たない手本を新しく作る。</summary>
    [PLCommand(Description = "段を持たない手本を新しく作る。段は addScenarioStep で足す。")]
    [PLResult("name",      PLResultKind.Text,    Description = "作った手本の名前")]
    [PLResult("count",     PLResultKind.Integer, Description = "登録後の手本の数")]
    [PLResult("storePath", PLResultKind.Text,    Description = "手本を置いているファイルの絶対パス")]
    public sealed class CreateScenarioCommand : PanelCommand
    {
        [PLParam(Description = "手本の名前。既にあるときは overwrite を立てること", Required = true)]
        public string Name { get; }

        [PLParam(Description = "この手本で達成したいこと")]
        public string Goal { get; }

        [PLParam(Description = "同じ名前の手本があるとき差し替える")]
        public bool Overwrite { get; }

        public CreateScenarioCommand(int modelIndex, string name, string goal = "", bool overwrite = false)
            : base(modelIndex)
        {
            Name      = name ?? "";
            Goal      = goal ?? "";
            Overwrite = overwrite;
        }
    }

    /// <summary>手本を消す。</summary>
    [PLCommand(Description = "手本を消す。ファイルからも消える。")]
    [PLResult("removed", PLResultKind.Flag,    Description = "消したか")]
    [PLResult("count",   PLResultKind.Integer, Description = "消した後の手本の数")]
    public sealed class DeleteScenarioCommand : PanelCommand
    {
        [PLParam(Description = "消す手本の名前", Required = true)]
        public string Name { get; }

        public DeleteScenarioCommand(int modelIndex, string name)
            : base(modelIndex)
        {
            Name = name ?? "";
        }
    }

    /// <summary>元を残したまま手本を複製する。</summary>
    [PLCommand(Description = "元を残したまま手本を複製する。複製の由来に元の名前が入る。")]
    [PLResult("name",  PLResultKind.Text,    Description = "作った手本の名前")]
    [PLResult("steps", PLResultKind.Integer, Description = "段の数")]
    [PLResult("count", PLResultKind.Integer, Description = "登録後の手本の数")]
    public sealed class ForkScenarioCommand : PanelCommand
    {
        [PLParam(Description = "元にする手本の名前", Required = true)]
        public string SourceName { get; }

        [PLParam(Description = "複製の名前。既にあるときは overwrite を立てること", Required = true)]
        public string NewName { get; }

        [PLParam(Description = "元から何を変えるつもりか")]
        public string ChangeSummary { get; }

        [PLParam(Description = "同じ名前の手本があるとき差し替える")]
        public bool Overwrite { get; }

        public ForkScenarioCommand(
            int modelIndex, string sourceName, string newName, string changeSummary = "", bool overwrite = false)
            : base(modelIndex)
        {
            SourceName    = sourceName ?? "";
            NewName       = newName ?? "";
            ChangeSummary = changeSummary ?? "";
            Overwrite     = overwrite;
        }
    }

    /// <summary>現在のモデルのオブジェクトグループを手本として登録する。</summary>
    [PLCommand(Description = "現在のモデルのオブジェクトグループを手本として登録する。描画オブジェクトへの参照と出力先は落とす。")]
    [PLResult("name",  PLResultKind.Text,    Description = "作った手本の名前")]
    [PLResult("steps", PLResultKind.Integer, Description = "段の数")]
    [PLResult("count", PLResultKind.Integer, Description = "登録後の手本の数")]
    public sealed class SaveScenarioFromGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "ObjectGroupName", Description = "元にするオブジェクトグループの名前", Required = true)]
        public string GroupName { get; }

        [PLParam(Description = "手本の名前。既にあるときは overwrite を立てること", Required = true)]
        public string ScenarioName { get; }

        [PLParam(Description = "この手本で達成したいこと")]
        public string Goal { get; }

        [PLParam(Description = "同じ名前の手本があるとき差し替える")]
        public bool Overwrite { get; }

        public SaveScenarioFromGroupCommand(
            int modelIndex, string groupName, string scenarioName, string goal = "", bool overwrite = false)
            : base(modelIndex)
        {
            GroupName    = groupName ?? "";
            ScenarioName = scenarioName ?? "";
            Goal         = goal ?? "";
            Overwrite    = overwrite;
        }
    }

    // ================================================================
    // 書き換える
    // ================================================================

    /// <summary>手本の意味情報を書く。</summary>
    [PLCommand(Description = "手本の目的・前提・成功条件・札・由来を書く。空にした引数は変えない。")]
    [PLResult("name",  PLResultKind.Text,    Description = "手本の名前")]
    [PLResult("steps", PLResultKind.Integer, Description = "段の数")]
    public sealed class SetScenarioMetaCommand : PanelCommand
    {
        [PLParam(Description = "手本の名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "この手本で達成したいこと。空にすると変えない")]
        public string Goal { get; }

        [PLParam(Description = "使う前に満たしているべきこと。1 件 1 要素。空にすると変えない")]
        public string[] Preconditions { get; }

        [PLParam(Description = "終わったときに確かめること。1 件 1 要素。空にすると変えない")]
        public string[] SuccessCriteria { get; }

        [PLParam(Description = "探すための札。1 件 1 要素。空にすると変えない")]
        public string[] Tags { get; }

        [PLParam(Description = "元から何を変えたか。空にすると変えない")]
        public string ChangeSummary { get; }

        [PLParam(Description = "作った者。空にすると変えない")]
        public string CreatedBy { get; }

        public SetScenarioMetaCommand(
            int modelIndex, string name,
            string goal = "", string[] preconditions = null, string[] successCriteria = null,
            string[] tags = null, string changeSummary = "", string createdBy = "")
            : base(modelIndex)
        {
            Name            = name ?? "";
            Goal            = goal ?? "";
            Preconditions   = preconditions   ?? new string[0];
            SuccessCriteria = successCriteria ?? new string[0];
            Tags            = tags            ?? new string[0];
            ChangeSummary   = changeSummary ?? "";
            CreatedBy       = createdBy ?? "";
        }
    }

    /// <summary>手本へ段を 1 つ足す。</summary>
    [PLCommand(Description = "手本へ段を 1 つ足す。afterElementId を省くと末尾へ足す。Command 以外の段は action を持たない。")]
    [PLResult("name",      PLResultKind.Text,    Description = "手本の名前")]
    [PLResult("elementId", PLResultKind.Text,    Description = "足した段の名前")]
    [PLResult("steps",     PLResultKind.Integer, Description = "足した後の段の数")]
    public sealed class AddScenarioStepCommand : PanelCommand
    {
        [PLParam(Description = "手本の名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "段の種別。Command 以外は実行しない段")]
        public ObjectGroupStepKind Kind { get; }

        [PLParam(Description = "段のコマンド名。Kind が Command のときだけ要る")]
        public string Action { get; }

        [PLParam(Description = "この段が要る理由")]
        public string Purpose { get; }

        [PLParam(Description = "この段の引数のキー。argValues と同じ長さにすること")]
        public string[] ArgKeys { get; }

        [PLParam(Description = "argKeys と同じ並びの値")]
        public string[] ArgValues { get; }

        [PLParam(Description = "参照先の手本の名前。Kind が ScenarioRef のときだけ要る")]
        public string RefName { get; }

        [PLParam(Description = "参照段の扱い方。Kind が ScenarioRef のときだけ使う")]
        public ScenarioExpansionPolicy ExpansionPolicy { get; }

        [PLParam(Description = "この段の名前。省くと自動で振る")]
        public string ElementId { get; }

        [PLParam(Description = "この段の名前の直後へ入れる。省くと末尾")]
        public string AfterElementId { get; }

        [PLParam(Description = "この段の名前の直前へ入れる。afterElementId と同時に指定すると失敗する")]
        public string BeforeElementId { get; }

        public AddScenarioStepCommand(
            int modelIndex, string name,
            ObjectGroupStepKind kind = ObjectGroupStepKind.Command,
            string action = "", string purpose = "",
            string[] argKeys = null, string[] argValues = null,
            string refName = "",
            ScenarioExpansionPolicy expansionPolicy = ScenarioExpansionPolicy.Reference,
            string elementId = "", string afterElementId = "", string beforeElementId = "")
            : base(modelIndex)
        {
            Name            = name ?? "";
            Kind            = kind;
            Action          = action ?? "";
            Purpose         = purpose ?? "";
            ArgKeys         = argKeys   ?? new string[0];
            ArgValues       = argValues ?? new string[0];
            RefName         = refName ?? "";
            ExpansionPolicy = expansionPolicy;
            ElementId       = elementId ?? "";
            AfterElementId  = afterElementId ?? "";
            BeforeElementId = beforeElementId ?? "";
        }
    }

    /// <summary>手本の段を前後へ動かす。</summary>
    [PLCommand(Description = "手本の段を別の位置へ動かす。段の名前と中身は変わらず、並びだけが変わる。beforeElementId か afterElementId のどちらか一方を指定する。")]
    [PLResult("name",       PLResultKind.Text,      Description = "手本の名前")]
    [PLResult("elementId",  PLResultKind.Text,      Description = "動かした段の名前")]
    [PLResult("fromIndex",  PLResultKind.Integer,   Description = "動かす前の位置（0 始まり）")]
    [PLResult("toIndex",    PLResultKind.Integer,   Description = "動かした後の位置（0 始まり）")]
    [PLResult("elementIds", PLResultKind.TextArray, Description = "動かした後の段の並び", Optional = true)]
    public sealed class MoveScenarioStepCommand : PanelCommand
    {
        [PLParam(Description = "手本の名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "動かす段の名前", Required = true)]
        public string ElementId { get; }

        [PLParam(Description = "この段の直前へ動かす")]
        public string BeforeElementId { get; }

        [PLParam(Description = "この段の直後へ動かす")]
        public string AfterElementId { get; }

        [PLParam(Description = "先頭へ動かす。before / after を指定したときは見ない")]
        public bool ToTop { get; }

        [PLParam(Description = "末尾へ動かす。before / after を指定したときは見ない")]
        public bool ToBottom { get; }

        public MoveScenarioStepCommand(
            int modelIndex, string name, string elementId,
            string beforeElementId = "", string afterElementId = "",
            bool toTop = false, bool toBottom = false)
            : base(modelIndex)
        {
            Name            = name ?? "";
            ElementId       = elementId ?? "";
            BeforeElementId = beforeElementId ?? "";
            AfterElementId  = afterElementId ?? "";
            ToTop           = toTop;
            ToBottom        = toBottom;
        }
    }

    /// <summary>手本の段を 1 つ丸ごと置き換える。</summary>
    [PLCommand(Description = "手本の段を 1 つ丸ごと置き換える。段の名前と位置は変わらない。省いた引数は既定値で上書きされる。")]
    [PLResult("name",      PLResultKind.Text,    Description = "手本の名前")]
    [PLResult("elementId", PLResultKind.Text,    Description = "置き換えた段の名前")]
    [PLResult("steps",     PLResultKind.Integer, Description = "段の数")]
    public sealed class SetScenarioStepCommand : PanelCommand
    {
        [PLParam(Description = "手本の名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "置き換える段の名前。describeScenario の elementIds のどれか", Required = true)]
        public string ElementId { get; }

        [PLParam(Description = "段の種別。Command 以外は実行しない段")]
        public ObjectGroupStepKind Kind { get; }

        [PLParam(Description = "段のコマンド名。Kind が Command のときだけ要る")]
        public string Action { get; }

        [PLParam(Description = "この段が要る理由")]
        public string Purpose { get; }

        [PLParam(Description = "この段の引数のキー。argValues と同じ長さにすること")]
        public string[] ArgKeys { get; }

        [PLParam(Description = "argKeys と同じ並びの値")]
        public string[] ArgValues { get; }

        [PLParam(Description = "参照先の手本の名前。Kind が ScenarioRef のときだけ要る")]
        public string RefName { get; }

        [PLParam(Description = "参照段の扱い方。Kind が ScenarioRef のときだけ使う")]
        public ScenarioExpansionPolicy ExpansionPolicy { get; }

        public SetScenarioStepCommand(
            int modelIndex, string name, string elementId,
            ObjectGroupStepKind kind = ObjectGroupStepKind.Command,
            string action = "", string purpose = "",
            string[] argKeys = null, string[] argValues = null,
            string refName = "",
            ScenarioExpansionPolicy expansionPolicy = ScenarioExpansionPolicy.Reference)
            : base(modelIndex)
        {
            Name            = name ?? "";
            ElementId       = elementId ?? "";
            Kind            = kind;
            Action          = action ?? "";
            Purpose         = purpose ?? "";
            ArgKeys         = argKeys   ?? new string[0];
            ArgValues       = argValues ?? new string[0];
            RefName         = refName ?? "";
            ExpansionPolicy = expansionPolicy;
        }
    }

    /// <summary>手本から段を 1 つ消す。</summary>
    [PLCommand(Description = "手本から段を 1 つ消す。")]
    [PLResult("name",  PLResultKind.Text,    Description = "手本の名前")]
    [PLResult("steps", PLResultKind.Integer, Description = "消した後の段の数")]
    public sealed class RemoveScenarioStepCommand : PanelCommand
    {
        [PLParam(Description = "手本の名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "消す段の名前。describeScenario の elementIds のどれか", Required = true)]
        public string ElementId { get; }

        public RemoveScenarioStepCommand(int modelIndex, string name, string elementId)
            : base(modelIndex)
        {
            Name      = name ?? "";
            ElementId = elementId ?? "";
        }
    }

    /// <summary>手本の段の引数を 1 つだけ書く。</summary>
    [PLCommand(Description = "手本の段の引数を 1 つだけ書く。値にカンマを含む引数（masterIndices や点列など）はこちらを使う。addScenarioStep / setScenarioStep の argValues は配列なのでカンマで割れてしまう。")]
    [PLResult("name",      PLResultKind.Text,    Description = "手本の名前")]
    [PLResult("elementId", PLResultKind.Text,    Description = "段の名前")]
    [PLResult("key",       PLResultKind.Text,    Description = "書いた引数のキー")]
    [PLResult("value",     PLResultKind.Text,    Description = "書いた値。消したときは空")]
    [PLResult("args",      PLResultKind.Integer, Description = "書いた後のこの段の引数の数")]
    public sealed class SetScenarioStepArgCommand : PanelCommand
    {
        [PLParam(Description = "手本の名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "段の名前。describeScenario の elementIds のどれか", Required = true)]
        public string ElementId { get; }

        [PLParam(Description = "書く引数のキー", Required = true)]
        public string Key { get; }

        [PLParam(Description = "値。カンマを含んでよい。remove を立てたときは見ない")]
        public string Value { get; }

        [PLParam(Description = "この引数を消す")]
        public bool Remove { get; }

        public SetScenarioStepArgCommand(
            int modelIndex, string name, string elementId, string key,
            string value = "", bool remove = false)
            : base(modelIndex)
        {
            Name      = name ?? "";
            ElementId = elementId ?? "";
            Key       = key ?? "";
            Value     = value ?? "";
            Remove    = remove;
        }
    }

    /// <summary>参照段を、参照先の段の列で置き換える。</summary>
    [PLCommand(Description = "参照段を、参照先の手本の段の列で置き換える。置き換えた段には新しい名前を振る。参照先は変わらない。")]
    [PLResult("name",       PLResultKind.Text,      Description = "手本の名前")]
    [PLResult("refName",    PLResultKind.Text,      Description = "展開した参照先の手本の名前")]
    [PLResult("inserted",   PLResultKind.Integer,   Description = "入れ替わった段の数")]
    [PLResult("steps",      PLResultKind.Integer,   Description = "展開した後の段の数")]
    [PLResult("elementIds", PLResultKind.TextArray, Description = "入れ替わった段の名前", Optional = true)]
    public sealed class ExpandScenarioRefCommand : PanelCommand
    {
        [PLParam(Description = "手本の名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "展開する参照段の名前。describeScenario の elementIds のどれか", Required = true)]
        public string ElementId { get; }

        public ExpandScenarioRefCommand(int modelIndex, string name, string elementId)
            : base(modelIndex)
        {
            Name      = name ?? "";
            ElementId = elementId ?? "";
        }
    }

    // ================================================================
    // 実行する
    // ================================================================

    /// <summary>手本の段を 1 つ実行する。</summary>
    [PLCommand(Description = "手本の段を 1 つ実行する。引数は手本のものを使い、argKeys と argValues で一部だけ差し替えられる。引数の値に @prev.masterIndices / @prev.objectIds と書くと直前に実行した段の対象に、@prev.<キー> と書くと直前の段の戻り値に置き換わる。実行しない段は何もせず種別と理由を返す。全段を通す口は無く、段は呼ぶ側が 1 つずつ選ぶ。")]
    [PLResult("name",         PLResultKind.Text,      Description = "手本の名前")]
    [PLResult("elementId",    PLResultKind.Text,      Description = "段の名前")]
    [PLResult("kind",         PLResultKind.Text,      Description = "段の種別")]
    [PLResult("purpose",      PLResultKind.Text,      Description = "その段が要る理由")]
    [PLResult("action",       PLResultKind.Text,      Description = "実行したコマンド名。実行しない段では空")]
    [PLResult("executed",     PLResultKind.Flag,      Description = "実際に実行したか。実行しない段と dryRun では false")]
    [PLResult("argKeys",      PLResultKind.TextArray, Description = "組み立てた引数のキー", Optional = true)]
    [PLResult("argValues",    PLResultKind.TextArray, Description = "argKeys と同じ並びの値", Optional = true)]
    [PLResult("prevMasterIndices", PLResultKind.IntegerArray, Description = "実行後に @prev として覚えている masterIndex", Optional = true)]
    [PLResult("prevObjectIds",     PLResultKind.TextArray,    Description = "実行後に @prev として覚えている安定 ID。10 進の文字列", Optional = true)]
    public sealed class RunScenarioStepCommand : PanelCommand
    {
        [PLParam(Description = "手本の名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "実行する段の名前。describeScenario の elementIds のどれか", Required = true)]
        public string ElementId { get; }

        [PLParam(Description = "差し替える引数のキー。argValues と同じ長さにすること。手本に無いキーは足す")]
        public string[] ArgKeys { get; }

        [PLParam(Description = "argKeys と同じ並びの値。@prev.masterIndices / @prev.objectIds と書くと直前に実行した段の対象に、@prev.<キー> と書くと直前の段の戻り値に置き換わる（例: @prev.faceIndices）")]
        public string[] ArgValues { get; }

        [PLParam(Description = "組み立てた引数を返すだけで実行しない")]
        public bool DryRun { get; }

        public RunScenarioStepCommand(
            int modelIndex, string name, string elementId,
            string[] argKeys = null, string[] argValues = null, bool dryRun = false)
            : base(modelIndex)
        {
            Name      = name ?? "";
            ElementId = elementId ?? "";
            ArgKeys   = argKeys   ?? new string[0];
            ArgValues = argValues ?? new string[0];
            DryRun    = dryRun;
        }
    }
}
