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
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "登録済みの手本（シナリオ）を一覧する。名前・目的・段数を同じ並びの配列で返す。query を指定すると、名前・目的・札・段のコマンド名で絞り込む。usageScene を指定すると、その利用シーンを段に持つ手本と、利用シーンの relatedScenarios に挙がった手本だけを返す。")]
    [PLResult("count",      PLResultKind.Integer,      Description = "手本の数")]
    [PLResult("storePath",  PLResultKind.Text,         Description = "手本を置いているファイルの絶対パス")]
    [PLResult("names",      PLResultKind.TextArray,    Description = "手本の名前", Optional = true)]
    [PLResult("goals",      PLResultKind.TextArray,    Description = "手本の目的。names と同じ並び", Optional = true)]
    [PLResult("stepCounts", PLResultKind.IntegerArray, Description = "手本の段数。names と同じ並び", Optional = true)]
    public sealed class QueryScenariosCommand : PanelCommand
    {
        [PLParam(Description = "ファイルから読み直してから返す。手で編集したあとに使う")]
        public bool Reload { get; }

        [PLParam(Description = "探す言葉。名前・目的・札・段のコマンド名と照合する。日本語でよい。省くと全部返す")]
        public string Query { get; }

        [PLParam(Description = "利用シーンの名前。その利用シーンを段に持つ手本と、利用シーンの relatedScenarios に挙がった手本だけを返す。省くと絞らない")]
        public string UsageScene { get; }

        public QueryScenariosCommand(int modelIndex = 0, bool reload = false, string query = "", string usageScene = "")
            : base(modelIndex)
        {
            Reload     = reload;
            Query      = query ?? "";
            UsageScene = usageScene ?? "";
        }
    }

    /// <summary>手本 1 本の中身を返す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "手本 1 本の中身を返す。段は elementIds と同じ並びの配列で返し、引数は argCounts で区切った argKeys / argValues に連結して返す。")]
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
    [PLResult("usageScenes",     PLResultKind.TextArray,    Description = "段が属する利用シーンの名前。空なら指定なし。elementIds と同じ並び", Optional = true)]
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
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "段を持たない手本を新しく作る。段は addScenarioStep で足す。")]
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
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "手本を消す。ファイルからも消える。")]
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
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "元を残したまま手本を複製する。複製の由来に元の名前が入る。")]
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
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "現在のモデルのオブジェクトグループを手本として登録する。描画オブジェクトへの参照と出力先は落とす。")]
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
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "手本の目的・前提・成功条件・札・由来を書く。空にした引数は変えない。")]
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
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "手本へ段を 1 つ足す。afterElementId を省くと末尾へ足す。Command 以外の段は action を持たない。")]
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

        [PLParam(Description = "この段が属する利用シーンの名前（polyling_scenes の名前）。同じ名前が続く段がその利用シーンの区間になる。空なら指定なし")]
        public string UsageScene { get; }

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
            string elementId = "", string afterElementId = "", string beforeElementId = "",
            string usageScene = "")
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
            UsageScene      = usageScene ?? "";
        }
    }

    /// <summary>手本の段を前後へ動かす。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "手本の段を別の位置へ動かす。段の名前と中身は変わらず、並びだけが変わる。beforeElementId か afterElementId のどちらか一方を指定する。")]
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
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "手本の段を 1 つ丸ごと置き換える。段の名前と位置は変わらない。省いた引数は既定値で上書きされる。")]
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

        [PLParam(Description = "この段が属する利用シーンの名前（polyling_scenes の名前）。同じ名前が続く段がその利用シーンの区間になる。空なら指定なし")]
        public string UsageScene { get; }

        public SetScenarioStepCommand(
            int modelIndex, string name, string elementId,
            ObjectGroupStepKind kind = ObjectGroupStepKind.Command,
            string action = "", string purpose = "",
            string[] argKeys = null, string[] argValues = null,
            string refName = "",
            ScenarioExpansionPolicy expansionPolicy = ScenarioExpansionPolicy.Reference,
            string usageScene = "")
            : base(modelIndex)
        {
            Name            = name ?? "";
            ElementId       = elementId ?? "";
            UsageScene      = usageScene ?? "";
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
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "手本から段を 1 つ消す。")]
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
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "手本の段の引数を 1 つだけ書く。値にカンマを含む引数（masterIndices や点列など）はこちらを使う。addScenarioStep / setScenarioStep の argValues は配列なのでカンマで割れてしまう。")]
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
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "参照段を、参照先の手本の段の列で置き換える。置き換えた段には新しい名前を振る。参照先は変わらない。")]
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

    /// <summary>手本を先頭から流す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "手本を先頭の段から順に実行する。指示（Instruction）・確認（Observe）の段と、失敗した段で止まる。注意（Note）の段は止まらずに読み飛ばし、別の手本を呼ぶ段（ScenarioRef）では呼ばれた手本をその場で流す。止まったら stop と stopMessage を読み、continueScenario で続ける。@prev と @<段の名前> は、この流れの中で実行した段の結果だけを指す。途中の段だけを選んで実行する口は無い。")]
    [PLResult("rootName",         PLResultKind.Text,      Description = "流している手本の名前")]
    [PLResult("stop",             PLResultKind.Text,      Description = "止まった理由。none（段数の上限で一旦返した）/ judgment（指示・確認の段）/ failed（段が失敗）/ finished（最後まで終わった）")]
    [PLResult("stopMessage",      PLResultKind.Text,      Description = "止まった段の内容、または失敗の理由")]
    [PLResult("scenario",         PLResultKind.Text,      Description = "いま居る手本の名前。別の手本を呼んでいる間はそちら")]
    [PLResult("elementId",        PLResultKind.Text,      Description = "いま居る段の名前（次に処理する段、または止まった段）")]
    [PLResult("stepNumber",       PLResultKind.Integer,   Description = "いま居る段が何段目か。1 始まり")]
    [PLResult("stepCount",        PLResultKind.Integer,   Description = "いま居る手本の段の数")]
    [PLResult("stepKind",         PLResultKind.Text,      Description = "いま居る段の種別")]
    [PLResult("purpose",          PLResultKind.Text,      Description = "いま居る段の目的・内容")]
    [PLResult("usageScene",       PLResultKind.Text,      Description = "いま居る段が属する利用シーン。空なら指定なし。検索の scene をこれに合わせると、この区間のコマンドだけが出る", Optional = true)]
    [PLResult("executedCommands", PLResultKind.Integer,   Description = "この流れで実行したコマンドの数")]
    [PLResult("newLog",           PLResultKind.TextArray, Description = "この呼び出しで処理した段の記録。1 段 1 行", Optional = true)]
    public sealed class RunScenarioCommand : PanelCommand
    {
        [PLParam(Description = "流す手本の名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "1 回の呼び出しで処理する段の上限。0 は止まるまで。パネルが 1 段ずつ画面を更新するために使う。MCP からは省く", Min = 0)]
        public int MaxSteps { get; }

        public RunScenarioCommand(int modelIndex, string name, int maxSteps = 0)
            : base(modelIndex)
        {
            Name     = name ?? "";
            MaxSteps = maxSteps;
        }
    }

    /// <summary>止まった所から続ける。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "runScenario で止まった所から続ける。指示・確認の段で止まっていたときはその段を越えて進み、失敗で止まっていたときは同じ段をやり直す。argKeys / argValues を渡すと、次に実行するコマンドの段の値だけを差し替える（指示の段で決めた値を渡すため）。戻り値は runScenario と同じ。")]
    [PLResult("rootName",         PLResultKind.Text,      Description = "流している手本の名前")]
    [PLResult("stop",             PLResultKind.Text,      Description = "止まった理由。none / judgment / failed / finished")]
    [PLResult("stopMessage",      PLResultKind.Text,      Description = "止まった段の内容、または失敗の理由")]
    [PLResult("scenario",         PLResultKind.Text,      Description = "いま居る手本の名前")]
    [PLResult("elementId",        PLResultKind.Text,      Description = "いま居る段の名前")]
    [PLResult("stepNumber",       PLResultKind.Integer,   Description = "いま居る段が何段目か。1 始まり")]
    [PLResult("stepCount",        PLResultKind.Integer,   Description = "いま居る手本の段の数")]
    [PLResult("stepKind",         PLResultKind.Text,      Description = "いま居る段の種別")]
    [PLResult("purpose",          PLResultKind.Text,      Description = "いま居る段の目的・内容")]
    [PLResult("usageScene",       PLResultKind.Text,      Description = "いま居る段が属する利用シーン。空なら指定なし。検索の scene をこれに合わせると、この区間のコマンドだけが出る", Optional = true)]
    [PLResult("executedCommands", PLResultKind.Integer,   Description = "この流れで実行したコマンドの数")]
    [PLResult("newLog",           PLResultKind.TextArray, Description = "この呼び出しで処理した段の記録。1 段 1 行", Optional = true)]
    public sealed class ContinueScenarioCommand : PanelCommand
    {
        [PLParam(Description = "1 回の呼び出しで処理する段の上限。0 は止まるまで。MCP からは省く", Min = 0)]
        public int MaxSteps { get; }

        [PLParam(Description = "次に実行するコマンドの段で差し替える引数のキー。argValues と同じ長さにすること")]
        public string[] ArgKeys { get; }

        [PLParam(Description = "argKeys と同じ並びの値。@prev.<キー> / @<段の名前>.<キー> も書ける")]
        public string[] ArgValues { get; }

        public ContinueScenarioCommand(
            int modelIndex, int maxSteps = 0, string[] argKeys = null, string[] argValues = null)
            : base(modelIndex)
        {
            MaxSteps  = maxSteps;
            ArgKeys   = argKeys   ?? new string[0];
            ArgValues = argValues ?? new string[0];
        }
    }

    /// <summary>流している手本の状態を返す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "流している手本の状態を返す。戻り値は runScenario と同じで、log にはこの流れの記録がすべて入る。流していなければ rootName が空。")]
    [PLResult("rootName",         PLResultKind.Text,      Description = "流している手本の名前。流していなければ空")]
    [PLResult("stop",             PLResultKind.Text,      Description = "止まった理由。none / judgment / failed / finished")]
    [PLResult("stopMessage",      PLResultKind.Text,      Description = "止まった段の内容、または失敗の理由")]
    [PLResult("scenario",         PLResultKind.Text,      Description = "いま居る手本の名前")]
    [PLResult("elementId",        PLResultKind.Text,      Description = "いま居る段の名前")]
    [PLResult("stepNumber",       PLResultKind.Integer,   Description = "いま居る段が何段目か。1 始まり")]
    [PLResult("stepCount",        PLResultKind.Integer,   Description = "いま居る手本の段の数")]
    [PLResult("stepKind",         PLResultKind.Text,      Description = "いま居る段の種別")]
    [PLResult("purpose",          PLResultKind.Text,      Description = "いま居る段の目的・内容")]
    [PLResult("usageScene",       PLResultKind.Text,      Description = "いま居る段が属する利用シーン。空なら指定なし。検索の scene をこれに合わせると、この区間のコマンドだけが出る", Optional = true)]
    [PLResult("executedCommands", PLResultKind.Integer,   Description = "この流れで実行したコマンドの数")]
    [PLResult("log",              PLResultKind.TextArray, Description = "この流れの記録。1 段 1 行", Optional = true)]
    public sealed class QueryScenarioRunCommand : PanelCommand
    {
        public QueryScenarioRunCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>流すのをやめる。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "流している手本をやめる。それまでに実行した段の結果はモデルに残る（元に戻すのは Undo）。")]
    [PLResult("stopped", PLResultKind.Flag, Description = "流していたものをやめたか")]
    [PLResult("rootName",         PLResultKind.Text,    Description = "やめた流しの先頭の手本")]
    [PLResult("scenario",         PLResultKind.Text,    Description = "やめたときに居た手本（呼ばれた手本の中ならその名前）")]
    [PLResult("elementId",        PLResultKind.Text,    Description = "やめたときに次に処理するはずだった段")]
    [PLResult("stepNumber",       PLResultKind.Integer, Description = "その段の番号（1 始まり）")]
    [PLResult("executedCommands", PLResultKind.Integer, Description = "やめるまでに実行したコマンドの数")]
    public sealed class StopScenarioRunCommand : PanelCommand
    {
        public StopScenarioRunCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    // ================================================================
    // 記録する（ScenarioRecorder）
    // ================================================================

    /// <summary>実行したコマンドを手本の下書きとして控え始める。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "実行したコマンドを手本の下書きとして控え始める。止めるまでに実行したコマンド（パネル・MCP・UI のどこから撃ったものでも）を、計算済みの引数ごと 1 段ずつ控える。検証パネルを流すと、段名・UI での手順・理由も Instruction / Note として入る。手本コマンドと UI 自動操作は控えない。")]
    [PLResult("recording", PLResultKind.Flag, Description = "記録中になったか")]
    public sealed class StartScenarioRecordingCommand : PanelCommand
    {
        public StartScenarioRecordingCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>記録を止め、控えたものを手本として登録する。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "記録を止め、控えた段を手本として登録する。discard を立てると登録せずに捨てる。登録に失敗したときは記録を続けるので、名前を変えて止め直せる。登録したら queryScenarioAudit で、焼いたままの索引が残っていないか確かめること。")]
    [PLResult("name",      PLResultKind.Text,    Description = "登録した手本の名前。捨てたときは空")]
    [PLResult("steps",     PLResultKind.Integer, Description = "登録した段の数")]
    [PLResult("count",     PLResultKind.Integer, Description = "登録後の手本の数")]
    [PLResult("discarded", PLResultKind.Flag,    Description = "登録せずに捨てたか")]
    public sealed class StopScenarioRecordingCommand : PanelCommand
    {
        [PLParam(Description = "登録する手本の名前。discard を立てたときは要らない")]
        public string Name { get; }

        [PLParam(Description = "この手本で達成したいこと")]
        public string Goal { get; }

        [PLParam(Description = "同じ名前の手本があるとき差し替える")]
        public bool Overwrite { get; }

        [PLParam(Description = "登録せずに、控えたものを捨てる")]
        public bool Discard { get; }

        public StopScenarioRecordingCommand(
            int modelIndex, string name = "", string goal = "", bool overwrite = false, bool discard = false)
            : base(modelIndex)
        {
            Name      = name ?? "";
            Goal      = goal ?? "";
            Overwrite = overwrite;
            Discard   = discard;
        }
    }

    // ================================================================
    // 点検する
    // ================================================================

    /// <summary>手本の段を点検する。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "手本の段を点検し、撃ち直すと壊れる箇所を返す。literalMeshIndex は IsMeshRef の印が付いた引数に索引が直接入っている段（名前から引く照会と @ 参照に置き換える）、unknownAction は action を解決できない段、badRef / missingRef / forwardRef は @<段の名前>.<キー> の書き方違い・存在しない段・後ろの段を指している参照、unknownUsageScene は段に書いた利用シーンが登録されていないもの。頂点や面の番号は印が無いので点検の対象外。")]
    [PLResult("name",       PLResultKind.Text,      Description = "手本の名前")]
    [PLResult("issues",     PLResultKind.Integer,   Description = "指摘の数")]
    [PLResult("elementIds", PLResultKind.TextArray, Description = "指摘した段の名前", Optional = true)]
    [PLResult("issueKinds", PLResultKind.TextArray, Description = "指摘の種類。elementIds と同じ並び", Optional = true)]
    [PLResult("keys",       PLResultKind.TextArray, Description = "指摘した引数のキー。段そのものの指摘では空。elementIds と同じ並び", Optional = true)]
    [PLResult("details",    PLResultKind.TextArray, Description = "指摘の中身。elementIds と同じ並び", Optional = true)]
    public sealed class QueryScenarioAuditCommand : PanelCommand
    {
        [PLParam(Description = "点検する手本の名前", Required = true)]
        public string Name { get; }

        public QueryScenarioAuditCommand(int modelIndex, string name)
            : base(modelIndex)
        {
            Name = name ?? "";
        }
    }
}
