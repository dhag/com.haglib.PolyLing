// PanelCommand.Scenario.cs
// シナリオの置き場を読み書きする要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【モデルを見ない】
//   シナリオは ScenarioLibrary が持ち、プロジェクトにもモデルにも属さない。
//   ディスパッチャはプロジェクトの null 門より前で捌く
//   （PlayerCommandDispatcher.Scenario.cs）。何も読み込んでいない状態でも使える。
//   例外は saveScenarioFromGroup だけで、これは現在のモデルの
//   ObjectGroup を読むため、受け口の中でモデルの有無を見る。
//
// 【シナリオは実体に縛らない】
//   ObjectGroup の項目は MeshRefIds / OutputObjectIds で実在の
//   描画オブジェクトを指せるが、シナリオに焼き付けると、そのモデルを
//   閉じた時点で死んだ ObjectId が残る。シナリオを作るときは必ず落とす。
//
// 【引数は 2 本で運ぶ】
//   項目ごとに個数の違う引数は、入れ子の配列として送れない
//   （PolyLing_コマンド追加の手引き.md「可変長のものは 2 本で持つ」）。
//   describeScenario は argCounts（項目ごとの個数）と、
//   連結した argKeys / argValues を返す。
//   addScenarioItem / setScenarioItem は 1 項目ぶんなので argKeys / argValues の 2 本。
//
// 【リモートからは受けない】
//   シナリオの置き場はホストの持ち物。RemoteOwnership が拒否する。
//   MCP の経路（PolyLingCommandGateway）はその判定を通らない。

namespace Poly_Ling.Data
{
    // ================================================================
    // 読む
    // ================================================================

    /// <summary>登録済みのシナリオを一覧する。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "登録済みのシナリオを一覧する。名前・目的・項目数を同じ並びの配列で返す。query を指定すると、名前・目的・札・項目のコマンド名で絞り込む。usageScene を指定すると、その利用シーンを項目に持つシナリオと、利用シーンの relatedScenarios に挙がったシナリオだけを返す。")]
    [PLResult("count",      PLResultKind.Integer,      Description = "シナリオの数")]
    [PLResult("storePath",  PLResultKind.Text,         Description = "シナリオを置いている場所の絶対パス。その下に <フォルダ>/<名前>.csv")]
    [PLResult("names",      PLResultKind.TextArray,    Description = "シナリオの名前", Optional = true)]
    [PLResult("folders",    PLResultKind.TextArray,    Description = "シナリオを置いているフォルダ（「a/b」の形。直下は空）。names と同じ並び", Optional = true)]
    [PLResult("allFolders", PLResultKind.TextArray,    Description = "すべてのフォルダ（空のフォルダも含む）。名前順", Optional = true)]
    [PLResult("goals",      PLResultKind.TextArray,    Description = "シナリオの目的。names と同じ並び", Optional = true)]
    [PLResult("stepCounts", PLResultKind.IntegerArray, Description = "シナリオの項目数。names と同じ並び", Optional = true)]
    public sealed class QueryScenariosCommand : PanelCommand
    {
        [PLParam(Description = "ファイルから読み直してから返す。手で編集したあとに使う")]
        public bool Reload { get; }

        [PLParam(Description = "探す言葉。名前・目的・札・項目のコマンド名と照合する。日本語でよい。省くと全部返す")]
        public string Query { get; }

        [PLParam(Description = "利用シーンの名前。その利用シーンを項目に持つシナリオと、利用シーンの relatedScenarios に挙がったシナリオだけを返す。省くと絞らない")]
        public string UsageScene { get; }

        public QueryScenariosCommand(int modelIndex = 0, bool reload = false, string query = "", string usageScene = "")
            : base(modelIndex)
        {
            Reload     = reload;
            Query      = query ?? "";
            UsageScene = usageScene ?? "";
        }
    }

    /// <summary>シナリオ 1 本の中身を返す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオ 1 本の中身を返す。項目は scenarioItemIds と同じ並びの配列で返し、引数は argCounts で区切った argKeys / argValues に連結して返す。")]
    [PLResult("name",            PLResultKind.Text,         Description = "シナリオの名前")]
    [PLResult("folder",          PLResultKind.Text,         Description = "シナリオを置いているフォルダ。直下は空")]
    [PLResult("goal",            PLResultKind.Text,         Description = "このシナリオで達成したいこと")]
    [PLResult("parentName",      PLResultKind.Text,         Description = "元にしたシナリオの名前。空 = 元がない")]
    [PLResult("changeSummary",   PLResultKind.Text,         Description = "元から何を変えたか")]
    [PLResult("createdBy",       PLResultKind.Text,         Description = "作った者")]
    [PLResult("steps",           PLResultKind.Integer,      Description = "項目の数")]
    [PLResult("preconditions",   PLResultKind.TextArray,    Description = "使う前に満たしているべきこと", Optional = true)]
    [PLResult("successCriteria", PLResultKind.TextArray,    Description = "終わったときに確かめること", Optional = true)]
    [PLResult("tags",            PLResultKind.TextArray,    Description = "探すための札", Optional = true)]
    [PLResult("scenarioItemIds",      PLResultKind.TextArray,    Description = "項目を指す名前", Optional = true)]
    [PLResult("kinds",           PLResultKind.TextArray,    Description = "項目の種別。scenarioItemIds と同じ並び", Optional = true)]
    [PLResult("actions",         PLResultKind.TextArray,    Description = "項目のコマンド名。実行しない項目は空。scenarioItemIds と同じ並び", Optional = true)]
    [PLResult("purposes",        PLResultKind.TextArray,    Description = "項目が要る理由。scenarioItemIds と同じ並び", Optional = true)]
    [PLResult("usageScenes",     PLResultKind.TextArray,    Description = "項目が属する利用シーンの名前。空なら指定なし。scenarioItemIds と同じ並び", Optional = true)]
    [PLResult("argCounts",       PLResultKind.IntegerArray, Description = "項目ごとの引数の数。scenarioItemIds と同じ並び", Optional = true)]
    [PLResult("argKeys",         PLResultKind.TextArray,    Description = "全項目の引数のキーを項目の順に連結したもの。argCounts で区切る", Optional = true)]
    [PLResult("argValues",       PLResultKind.TextArray,    Description = "argKeys と同じ並びの値", Optional = true)]
    [PLResult("refNames",        PLResultKind.TextArray,    Description = "参照先のシナリオの名前。参照項目以外は空。scenarioItemIds と同じ並び", Optional = true)]
    [PLResult("expansionPolicies", PLResultKind.TextArray,  Description = "参照項目の扱い方。参照項目以外は空。scenarioItemIds と同じ並び", Optional = true)]
    [PLResult("scenarioNames",   PLResultKind.TextArray,    Description = "その項目が載っているシナリオの名前。resolve のときだけ。scenarioItemIds と同じ並び", Optional = true)]
    [PLResult("depths",          PLResultKind.IntegerArray, Description = "参照の深さ。0 = 起点。resolve のときだけ。scenarioItemIds と同じ並び", Optional = true)]
    public sealed class DescribeScenarioCommand : PanelCommand
    {
        [PLParam(Description = "シナリオの名前。queryScenarios の names のどれか", Required = true)]
        public string Name { get; }

        [PLParam(Description = "参照項目を辿って平たい項目の列で返す。参照項目そのものは列に出ない")]
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

    /// <summary>項目を持たないシナリオを新しく作る。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "項目を持たないシナリオを新しく作る。項目は addScenarioItem で足す。")]
    [PLResult("name",      PLResultKind.Text,    Description = "作ったシナリオの名前")]
    [PLResult("count",     PLResultKind.Integer, Description = "登録後のシナリオの数")]
    [PLResult("folder",    PLResultKind.Text,    Description = "シナリオを置いたフォルダ。直下は空")]
    [PLResult("storePath", PLResultKind.Text,    Description = "シナリオを置いているフォルダの絶対パス")]
    public sealed class CreateScenarioCommand : PanelCommand
    {
        [PLParam(Description = "シナリオの名前。既にあるときは overwrite を立てること", Required = true)]
        public string Name { get; }

        [PLParam(Description = "このシナリオで達成したいこと")]
        public string Goal { get; }

        [PLParam(Description = "同じ名前のシナリオがあるとき差し替える")]
        public bool Overwrite { get; }

        [PLParam(Description = "置くフォルダ（「顔/輪郭」のように / で区切る）。無ければ作る。省くと、既存のシナリオは今の場所のまま、新しいシナリオは直下")]
        public string Folder { get; }

        public CreateScenarioCommand(int modelIndex, string name, string goal = "", bool overwrite = false, string folder = "")
            : base(modelIndex)
        {
            Name      = name ?? "";
            Goal      = goal ?? "";
            Overwrite = overwrite;
            Folder    = folder ?? "";
        }
    }

    /// <summary>シナリオをフォルダへ移す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオをフォルダへ移す。フォルダは置き場所（整理）だけを表し、中身と名前は変わらない。親は名前で子を呼ぶので、移しても親子関係は切れない。無いフォルダは作る。")]
    [PLResult("folder", PLResultKind.Text,      Description = "移した先のフォルダ（ファイル名に使える形にそろえたもの）。直下は空")]
    [PLResult("names",  PLResultKind.TextArray, Description = "移したシナリオの名前", Optional = true)]
    public sealed class MoveScenariosCommand : PanelCommand
    {
        [PLParam(Description = "移すシナリオの名前。1 本でも無い名前があれば何もしない", Required = true)]
        public string[] Names { get; }

        [PLParam(Description = "移す先のフォルダ（「顔/輪郭」のように / で区切る）。空なら直下")]
        public string Folder { get; }

        public MoveScenariosCommand(int modelIndex, string[] names, string folder = "")
            : base(modelIndex)
        {
            Names  = names ?? System.Array.Empty<string>();
            Folder = folder ?? "";
        }
    }

    /// <summary>シナリオの空のフォルダを作る。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオを置く空のフォルダを作る。「顔/輪郭」のように / で区切ると親も作る。既にあれば何もしない。")]
    [PLResult("folder", PLResultKind.Text, Description = "作ったフォルダ（ファイル名に使える形にそろえたもの）")]
    public sealed class CreateScenarioFolderCommand : PanelCommand
    {
        [PLParam(Description = "作るフォルダ", Required = true)]
        public string Folder { get; }

        public CreateScenarioFolderCommand(int modelIndex, string folder)
            : base(modelIndex)
        {
            Folder = folder ?? "";
        }
    }

    /// <summary>シナリオのフォルダの名前を変える。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオのフォルダの名前を変える（中のシナリオと下のフォルダごと）。「a/b」→「c/b」のように別の親の下へも移せる。行き先が既にあるとき・自分の下へ移すときは失敗する。")]
    [PLResult("folder", PLResultKind.Text, Description = "新しいフォルダ")]
    public sealed class RenameScenarioFolderCommand : PanelCommand
    {
        [PLParam(Description = "今のフォルダ", Required = true)]
        public string Folder { get; }

        [PLParam(Description = "新しいフォルダ", Required = true)]
        public string NewFolder { get; }

        public RenameScenarioFolderCommand(int modelIndex, string folder, string newFolder)
            : base(modelIndex)
        {
            Folder    = folder ?? "";
            NewFolder = newFolder ?? "";
        }
    }

    /// <summary>シナリオの空のフォルダを消す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオの空のフォルダを消す（下の空のフォルダも）。シナリオが入っているときは失敗する（先に移すか消す）。")]
    [PLResult("folder", PLResultKind.Text, Description = "消したフォルダ")]
    public sealed class DeleteScenarioFolderCommand : PanelCommand
    {
        [PLParam(Description = "消すフォルダ", Required = true)]
        public string Folder { get; }

        public DeleteScenarioFolderCommand(int modelIndex, string folder)
            : base(modelIndex)
        {
            Folder = folder ?? "";
        }
    }

    /// <summary>フォルダの中のシナリオを順に呼ぶ親シナリオを作る。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "フォルダの中のシナリオを順に呼ぶ親シナリオを作り、同じフォルダに置く。順番は names で指定し、省くとフォルダ直下のシナリオを名前順に呼ぶ（下のフォルダは含めない）。親の項目はすべて「別のシナリオを呼ぶ項目」で、目的には子の目的を写す。")]
    [PLResult("name",   PLResultKind.Text,      Description = "作った親シナリオの名前")]
    [PLResult("folder", PLResultKind.Text,      Description = "親を置いたフォルダ")]
    [PLResult("steps",  PLResultKind.Integer,   Description = "親の項目の数")]
    [PLResult("calls",  PLResultKind.TextArray, Description = "呼ぶシナリオの名前（呼ぶ順）", Optional = true)]
    [PLResult("count",  PLResultKind.Integer,   Description = "登録後のシナリオの数")]
    public sealed class CreateScenarioFromFolderCommand : PanelCommand
    {
        [PLParam(Description = "子を集めたフォルダ", Required = true)]
        public string Folder { get; }

        [PLParam(Description = "作る親シナリオの名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "呼ぶシナリオの名前を呼ぶ順に。省くとフォルダ直下のシナリオを名前順")]
        public string[] Names { get; }

        [PLParam(Description = "親シナリオで達成したいこと")]
        public string Goal { get; }

        [PLParam(Description = "同じ名前のシナリオがあるとき差し替える")]
        public bool Overwrite { get; }

        public CreateScenarioFromFolderCommand(int modelIndex, string folder, string name, string[] names = null, string goal = "", bool overwrite = false)
            : base(modelIndex)
        {
            Folder    = folder ?? "";
            Name      = name ?? "";
            Names     = names ?? System.Array.Empty<string>();
            Goal      = goal ?? "";
            Overwrite = overwrite;
        }
    }

    /// <summary>シナリオを消す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオを消す。ファイルからも消える。")]
    [PLResult("removed", PLResultKind.Flag,    Description = "消したか")]
    [PLResult("count",   PLResultKind.Integer, Description = "消した後のシナリオの数")]
    public sealed class DeleteScenarioCommand : PanelCommand
    {
        [PLParam(Description = "消すシナリオの名前", Required = true)]
        public string Name { get; }

        public DeleteScenarioCommand(int modelIndex, string name)
            : base(modelIndex)
        {
            Name = name ?? "";
        }
    }

    /// <summary>元を残したままシナリオを複製する。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "元を残したままシナリオを複製する。複製の由来に元の名前が入る。複製は元と同じフォルダに置く。")]
    [PLResult("name",  PLResultKind.Text,    Description = "作ったシナリオの名前")]
    [PLResult("steps", PLResultKind.Integer, Description = "項目の数")]
    [PLResult("count", PLResultKind.Integer, Description = "登録後のシナリオの数")]
    public sealed class ForkScenarioCommand : PanelCommand
    {
        [PLParam(Description = "元にするシナリオの名前", Required = true)]
        public string SourceName { get; }

        [PLParam(Description = "複製の名前。既にあるときは overwrite を立てること", Required = true)]
        public string NewName { get; }

        [PLParam(Description = "元から何を変えるつもりか")]
        public string ChangeSummary { get; }

        [PLParam(Description = "同じ名前のシナリオがあるとき差し替える")]
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

    /// <summary>現在のモデルのオブジェクトグループをシナリオとして登録する。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "現在のモデルのオブジェクトグループをシナリオとして登録する。描画オブジェクトへの参照と出力先は落とす。")]
    [PLResult("name",  PLResultKind.Text,    Description = "作ったシナリオの名前")]
    [PLResult("steps", PLResultKind.Integer, Description = "項目の数")]
    [PLResult("count", PLResultKind.Integer, Description = "登録後のシナリオの数")]
    public sealed class SaveScenarioFromGroupCommand : PanelCommand
    {
        [PLParam(TextKey = "ObjectGroupName", Description = "元にするオブジェクトグループの名前", Required = true)]
        public string GroupName { get; }

        [PLParam(Description = "シナリオの名前。既にあるときは overwrite を立てること", Required = true)]
        public string ScenarioName { get; }

        [PLParam(Description = "このシナリオで達成したいこと")]
        public string Goal { get; }

        [PLParam(Description = "同じ名前のシナリオがあるとき差し替える")]
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

    /// <summary>シナリオの意味情報を書く。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオの目的・前提・成功条件・札・由来を書く。空にした引数は変えない。")]
    [PLResult("name",  PLResultKind.Text,    Description = "シナリオの名前")]
    [PLResult("steps", PLResultKind.Integer, Description = "項目の数")]
    public sealed class SetScenarioMetaCommand : PanelCommand
    {
        [PLParam(Description = "シナリオの名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "このシナリオで達成したいこと。空にすると変えない")]
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

    /// <summary>シナリオへ項目を 1 つ足す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオへ項目を 1 つ足す。afterScenarioItemId を省くと末尾へ足す。Command 以外の項目は action を持たない。")]
    [PLResult("name",      PLResultKind.Text,    Description = "シナリオの名前")]
    [PLResult("scenarioItemId", PLResultKind.Text,    Description = "足した項目 ID")]
    [PLResult("steps",     PLResultKind.Integer, Description = "足した後の項目の数")]
    public sealed class AddScenarioItemCommand : PanelCommand
    {
        [PLParam(Description = "シナリオの名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "項目の種別。Command 以外は実行しない項目")]
        public ObjectGroupStepKind Kind { get; }

        [PLParam(Description = "項目のコマンド名。Kind が Command のときだけ要る")]
        public string Action { get; }

        [PLParam(Description = "この項目が要る理由")]
        public string Purpose { get; }

        [PLParam(Description = "この項目の引数のキー。argValues と同じ長さにすること")]
        public string[] ArgKeys { get; }

        [PLParam(Description = "argKeys と同じ並びの値")]
        public string[] ArgValues { get; }

        [PLParam(Description = "参照先のシナリオの名前。Kind が ScenarioRef のときだけ要る")]
        public string RefName { get; }

        [PLParam(Description = "参照項目の扱い方。Kind が ScenarioRef のときだけ使う")]
        public ScenarioExpansionPolicy ExpansionPolicy { get; }

        [PLParam(Description = "この項目が属する利用シーンの名前（polyling_scenes の名前）。同じ名前が続く項目がその利用シーンの区間になる。空なら指定なし")]
        public string UsageScene { get; }

        [PLParam(Description = "この項目 ID。省くと自動で振る")]
        public string ScenarioItemId { get; }

        [PLParam(Description = "この項目 ID の直後へ入れる。省くと末尾")]
        public string AfterScenarioItemId { get; }

        [PLParam(Description = "この項目 ID の直前へ入れる。afterScenarioItemId と同時に指定すると失敗する")]
        public string BeforeScenarioItemId { get; }

        public AddScenarioItemCommand(
            int modelIndex, string name,
            ObjectGroupStepKind kind = ObjectGroupStepKind.Command,
            string action = "", string purpose = "",
            string[] argKeys = null, string[] argValues = null,
            string refName = "",
            ScenarioExpansionPolicy expansionPolicy = ScenarioExpansionPolicy.Reference,
            string scenarioItemId = "", string afterScenarioItemId = "", string beforeScenarioItemId = "",
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
            ScenarioItemId       = scenarioItemId ?? "";
            AfterScenarioItemId  = afterScenarioItemId ?? "";
            BeforeScenarioItemId = beforeScenarioItemId ?? "";
            UsageScene      = usageScene ?? "";
        }
    }

    /// <summary>シナリオの項目を前後へ動かす。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオの項目を別の位置へ動かす。項目 ID と中身は変わらず、並びだけが変わる。beforeScenarioItemId か afterScenarioItemId のどちらか一方を指定する。")]
    [PLResult("name",       PLResultKind.Text,      Description = "シナリオの名前")]
    [PLResult("scenarioItemId",  PLResultKind.Text,      Description = "動かした項目 ID")]
    [PLResult("fromIndex",  PLResultKind.Integer,   Description = "動かす前の位置（0 始まり）")]
    [PLResult("toIndex",    PLResultKind.Integer,   Description = "動かした後の位置（0 始まり）")]
    [PLResult("scenarioItemIds", PLResultKind.TextArray, Description = "動かした後の項目の並び", Optional = true)]
    public sealed class MoveScenarioItemCommand : PanelCommand
    {
        [PLParam(Description = "シナリオの名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "動かす項目 ID", Required = true)]
        public string ScenarioItemId { get; }

        [PLParam(Description = "この項目の直前へ動かす")]
        public string BeforeScenarioItemId { get; }

        [PLParam(Description = "この項目の直後へ動かす")]
        public string AfterScenarioItemId { get; }

        [PLParam(Description = "先頭へ動かす。before / after を指定したときは見ない")]
        public bool ToTop { get; }

        [PLParam(Description = "末尾へ動かす。before / after を指定したときは見ない")]
        public bool ToBottom { get; }

        public MoveScenarioItemCommand(
            int modelIndex, string name, string scenarioItemId,
            string beforeScenarioItemId = "", string afterScenarioItemId = "",
            bool toTop = false, bool toBottom = false)
            : base(modelIndex)
        {
            Name            = name ?? "";
            ScenarioItemId       = scenarioItemId ?? "";
            BeforeScenarioItemId = beforeScenarioItemId ?? "";
            AfterScenarioItemId  = afterScenarioItemId ?? "";
            ToTop           = toTop;
            ToBottom        = toBottom;
        }
    }

    /// <summary>シナリオの項目を 1 つ丸ごと置き換える。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオの項目を 1 つ丸ごと置き換える。項目 ID と位置は変わらない。省いた引数は既定値で上書きされる。")]
    [PLResult("name",      PLResultKind.Text,    Description = "シナリオの名前")]
    [PLResult("scenarioItemId", PLResultKind.Text,    Description = "置き換えた項目 ID")]
    [PLResult("steps",     PLResultKind.Integer, Description = "項目の数")]
    public sealed class SetScenarioItemCommand : PanelCommand
    {
        [PLParam(Description = "シナリオの名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "置き換える項目 ID。describeScenario の scenarioItemIds のどれか", Required = true)]
        public string ScenarioItemId { get; }

        [PLParam(Description = "項目の種別。Command 以外は実行しない項目")]
        public ObjectGroupStepKind Kind { get; }

        [PLParam(Description = "項目のコマンド名。Kind が Command のときだけ要る")]
        public string Action { get; }

        [PLParam(Description = "この項目が要る理由")]
        public string Purpose { get; }

        [PLParam(Description = "この項目の引数のキー。argValues と同じ長さにすること")]
        public string[] ArgKeys { get; }

        [PLParam(Description = "argKeys と同じ並びの値")]
        public string[] ArgValues { get; }

        [PLParam(Description = "参照先のシナリオの名前。Kind が ScenarioRef のときだけ要る")]
        public string RefName { get; }

        [PLParam(Description = "参照項目の扱い方。Kind が ScenarioRef のときだけ使う")]
        public ScenarioExpansionPolicy ExpansionPolicy { get; }

        [PLParam(Description = "この項目が属する利用シーンの名前（polyling_scenes の名前）。同じ名前が続く項目がその利用シーンの区間になる。空なら指定なし")]
        public string UsageScene { get; }

        public SetScenarioItemCommand(
            int modelIndex, string name, string scenarioItemId,
            ObjectGroupStepKind kind = ObjectGroupStepKind.Command,
            string action = "", string purpose = "",
            string[] argKeys = null, string[] argValues = null,
            string refName = "",
            ScenarioExpansionPolicy expansionPolicy = ScenarioExpansionPolicy.Reference,
            string usageScene = "")
            : base(modelIndex)
        {
            Name            = name ?? "";
            ScenarioItemId       = scenarioItemId ?? "";
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

    /// <summary>シナリオから項目を 1 つ消す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオから項目を 1 つ消す。")]
    [PLResult("name",  PLResultKind.Text,    Description = "シナリオの名前")]
    [PLResult("steps", PLResultKind.Integer, Description = "消した後の項目の数")]
    public sealed class RemoveScenarioItemCommand : PanelCommand
    {
        [PLParam(Description = "シナリオの名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "消す項目 ID。describeScenario の scenarioItemIds のどれか", Required = true)]
        public string ScenarioItemId { get; }

        public RemoveScenarioItemCommand(int modelIndex, string name, string scenarioItemId)
            : base(modelIndex)
        {
            Name      = name ?? "";
            ScenarioItemId = scenarioItemId ?? "";
        }
    }

    /// <summary>シナリオの項目の引数を 1 つだけ書く。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオの項目の引数を 1 つだけ書く。値にカンマを含む引数（masterIndices や点列など）はこちらを使う。addScenarioItem / setScenarioItem の argValues は配列なのでカンマで割れてしまう。")]
    [PLResult("name",      PLResultKind.Text,    Description = "シナリオの名前")]
    [PLResult("scenarioItemId", PLResultKind.Text,    Description = "項目 ID")]
    [PLResult("key",       PLResultKind.Text,    Description = "書いた引数のキー")]
    [PLResult("value",     PLResultKind.Text,    Description = "書いた値。消したときは空")]
    [PLResult("args",      PLResultKind.Integer, Description = "書いた後のこの項目の引数の数")]
    public sealed class SetScenarioItemArgCommand : PanelCommand
    {
        [PLParam(Description = "シナリオの名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "項目 ID。describeScenario の scenarioItemIds のどれか", Required = true)]
        public string ScenarioItemId { get; }

        [PLParam(Description = "書く引数のキー", Required = true)]
        public string Key { get; }

        [PLParam(Description = "値。カンマを含んでよい。remove を立てたときは見ない")]
        public string Value { get; }

        [PLParam(Description = "この引数を消す")]
        public bool Remove { get; }

        public SetScenarioItemArgCommand(
            int modelIndex, string name, string scenarioItemId, string key,
            string value = "", bool remove = false)
            : base(modelIndex)
        {
            Name      = name ?? "";
            ScenarioItemId = scenarioItemId ?? "";
            Key       = key ?? "";
            Value     = value ?? "";
            Remove    = remove;
        }
    }

    /// <summary>参照項目を、参照先の項目の列で置き換える。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "参照項目を、参照先のシナリオの項目の列で置き換える。置き換えた項目には新しい名前を振る。参照先は変わらない。")]
    [PLResult("name",       PLResultKind.Text,      Description = "シナリオの名前")]
    [PLResult("refName",    PLResultKind.Text,      Description = "展開した参照先のシナリオの名前")]
    [PLResult("inserted",   PLResultKind.Integer,   Description = "入れ替わった項目の数")]
    [PLResult("steps",      PLResultKind.Integer,   Description = "展開した後の項目の数")]
    [PLResult("scenarioItemIds", PLResultKind.TextArray, Description = "入れ替わった項目 ID", Optional = true)]
    public sealed class ExpandScenarioRefCommand : PanelCommand
    {
        [PLParam(Description = "シナリオの名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "展開する参照項目 ID。describeScenario の scenarioItemIds のどれか", Required = true)]
        public string ScenarioItemId { get; }

        public ExpandScenarioRefCommand(int modelIndex, string name, string scenarioItemId)
            : base(modelIndex)
        {
            Name      = name ?? "";
            ScenarioItemId = scenarioItemId ?? "";
        }
    }

    // ================================================================
    // 実行する
    // ================================================================

    /// <summary>シナリオを先頭から流す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオを先頭の項目から順に実行する。指示（Instruction）・確認（Observe）の項目と、失敗した項目で止まる。注意（Note）の項目は止まらずに読み飛ばし、別のシナリオを呼ぶ項目（ScenarioRef）では呼ばれたシナリオをその場で流す。止まったら stop と stopMessage を読み、continueScenario で続ける。@prev と @<項目 ID> は、この流れの中で実行した項目の結果だけを指す。途中の項目だけを選んで実行する口は無い。")]
    [PLResult("rootName",         PLResultKind.Text,      Description = "流しているシナリオの名前")]
    [PLResult("stop",             PLResultKind.Text,      Description = "止まった理由。none（項目数の上限で一旦返した）/ judgment（指示・確認の項目）/ failed（項目が失敗）/ finished（最後まで終わった）")]
    [PLResult("stopMessage",      PLResultKind.Text,      Description = "止まった項目の内容、または失敗の理由")]
    [PLResult("scenario",         PLResultKind.Text,      Description = "いま居るシナリオの名前。別のシナリオを呼んでいる間はそちら")]
    [PLResult("scenarioItemId",        PLResultKind.Text,      Description = "いま居る項目 ID（次に処理する項目、または止まった項目）")]
    [PLResult("stepNumber",       PLResultKind.Integer,   Description = "いま居る項目が何番目か。1 始まり")]
    [PLResult("stepCount",        PLResultKind.Integer,   Description = "いま居るシナリオの項目の数")]
    [PLResult("stepKind",         PLResultKind.Text,      Description = "いま居る項目の種別")]
    [PLResult("purpose",          PLResultKind.Text,      Description = "いま居る項目の目的・内容")]
    [PLResult("usageScene",       PLResultKind.Text,      Description = "いま居る項目が属する利用シーン。空なら指定なし。検索の scene をこれに合わせると、この区間のコマンドだけが出る", Optional = true)]
    [PLResult("executedCommands", PLResultKind.Integer,   Description = "この流れで実行したコマンドの数")]
    [PLResult("newLog",           PLResultKind.TextArray, Description = "この呼び出しで処理した項目の記録。1 項目 1 行", Optional = true)]
    [PLResult("routeName",        PLResultKind.Text,      Description = "いま居る項目のコマンドを画面で行う経路の名前。前の項目の経路に近いものを選び、無ければ既定。経路が無ければ空", Optional = true)]
    [PLResult("routeItems",       PLResultKind.TextArray, Description = "その経路のボタン・入力欄の ID。uiReveal / uiHighlight add=true で示せる", Optional = true)]
    [PLResult("routeNote",        PLResultKind.Text,      Description = "その経路のうちボタンで表せない操作の説明", Optional = true)]
    [PLResult("awaitUser",        PLResultKind.Flag,      Description = "人がこの項目のコマンドを実行するのを待っているか（案内バーの入力待ち）", Optional = true)]
    public sealed class RunScenarioCommand : PanelCommand
    {
        [PLParam(Description = "流すシナリオの名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "1 回の呼び出しで処理する項目の上限。0 は止まるまで。パネルが 1 項目ずつ画面を更新するために使う。MCP からは省く", Min = 0)]
        public int MaxSteps { get; }

        [PLParam(Description = "true なら流しを用意するだけで項目を処理しない。案内バーが最初の項目の前に赤枠を出すために使う。MCP からは省く")]
        public bool PrepareOnly { get; }

        public RunScenarioCommand(int modelIndex, string name, int maxSteps = 0, bool prepareOnly = false)
            : base(modelIndex)
        {
            Name        = name ?? "";
            MaxSteps    = maxSteps;
            PrepareOnly = prepareOnly;
        }
    }

    /// <summary>止まった所から続ける。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "runScenario で止まった所から続ける。指示・確認の項目で止まっていたときはその項目を越えて進み、失敗で止まっていたときは同じ項目をやり直す。argKeys / argValues を渡すと、次に実行するコマンドの項目の値だけを差し替える（指示の項目で決めた値を渡すため）。戻り値は runScenario と同じ。")]
    [PLResult("rootName",         PLResultKind.Text,      Description = "流しているシナリオの名前")]
    [PLResult("stop",             PLResultKind.Text,      Description = "止まった理由。none / judgment / failed / finished")]
    [PLResult("stopMessage",      PLResultKind.Text,      Description = "止まった項目の内容、または失敗の理由")]
    [PLResult("scenario",         PLResultKind.Text,      Description = "いま居るシナリオの名前")]
    [PLResult("scenarioItemId",        PLResultKind.Text,      Description = "いま居る項目 ID")]
    [PLResult("stepNumber",       PLResultKind.Integer,   Description = "いま居る項目が何番目か。1 始まり")]
    [PLResult("stepCount",        PLResultKind.Integer,   Description = "いま居るシナリオの項目の数")]
    [PLResult("stepKind",         PLResultKind.Text,      Description = "いま居る項目の種別")]
    [PLResult("purpose",          PLResultKind.Text,      Description = "いま居る項目の目的・内容")]
    [PLResult("usageScene",       PLResultKind.Text,      Description = "いま居る項目が属する利用シーン。空なら指定なし。検索の scene をこれに合わせると、この区間のコマンドだけが出る", Optional = true)]
    [PLResult("executedCommands", PLResultKind.Integer,   Description = "この流れで実行したコマンドの数")]
    [PLResult("newLog",           PLResultKind.TextArray, Description = "この呼び出しで処理した項目の記録。1 項目 1 行", Optional = true)]
    [PLResult("routeName",        PLResultKind.Text,      Description = "いま居る項目のコマンドを画面で行う経路の名前。経路が無ければ空", Optional = true)]
    [PLResult("routeItems",       PLResultKind.TextArray, Description = "その経路のボタン・入力欄の ID", Optional = true)]
    [PLResult("routeNote",        PLResultKind.Text,      Description = "その経路のうちボタンで表せない操作の説明", Optional = true)]
    [PLResult("awaitUser",        PLResultKind.Flag,      Description = "人がこの項目のコマンドを実行するのを待っているか", Optional = true)]
    public sealed class ContinueScenarioCommand : PanelCommand
    {
        [PLParam(Description = "1 回の呼び出しで処理する項目の上限。0 は止まるまで。MCP からは省く", Min = 0)]
        public int MaxSteps { get; }

        [PLParam(Description = "次に実行するコマンドの項目で差し替える引数のキー。argValues と同じ長さにすること")]
        public string[] ArgKeys { get; }

        [PLParam(Description = "argKeys と同じ並びの値。@prev.<キー> / @<項目 ID>.<キー> も書ける")]
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

    /// <summary>流しているシナリオの状態を返す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "流しているシナリオの状態を返す。戻り値は runScenario と同じで、log にはこの流れの記録がすべて入る。流していなければ rootName が空。")]
    [PLResult("rootName",         PLResultKind.Text,      Description = "流しているシナリオの名前。流していなければ空")]
    [PLResult("stop",             PLResultKind.Text,      Description = "止まった理由。none / judgment / failed / finished")]
    [PLResult("stopMessage",      PLResultKind.Text,      Description = "止まった項目の内容、または失敗の理由")]
    [PLResult("scenario",         PLResultKind.Text,      Description = "いま居るシナリオの名前")]
    [PLResult("scenarioItemId",        PLResultKind.Text,      Description = "いま居る項目 ID")]
    [PLResult("stepNumber",       PLResultKind.Integer,   Description = "いま居る項目が何番目か。1 始まり")]
    [PLResult("stepCount",        PLResultKind.Integer,   Description = "いま居るシナリオの項目の数")]
    [PLResult("stepKind",         PLResultKind.Text,      Description = "いま居る項目の種別")]
    [PLResult("purpose",          PLResultKind.Text,      Description = "いま居る項目の目的・内容")]
    [PLResult("usageScene",       PLResultKind.Text,      Description = "いま居る項目が属する利用シーン。空なら指定なし。検索の scene をこれに合わせると、この区間のコマンドだけが出る", Optional = true)]
    [PLResult("executedCommands", PLResultKind.Integer,   Description = "この流れで実行したコマンドの数")]
    [PLResult("log",              PLResultKind.TextArray, Description = "この流れの記録。1 項目 1 行", Optional = true)]
    [PLResult("routeName",        PLResultKind.Text,      Description = "いま居る項目のコマンドを画面で行う経路の名前。経路が無ければ空", Optional = true)]
    [PLResult("routeItems",       PLResultKind.TextArray, Description = "その経路のボタン・入力欄の ID。uiReveal / uiHighlight add=true で示せる", Optional = true)]
    [PLResult("routeNote",        PLResultKind.Text,      Description = "その経路のうちボタンで表せない操作の説明", Optional = true)]
    [PLResult("awaitUser",        PLResultKind.Flag,      Description = "人がこの項目のコマンドを実行するのを待っているか", Optional = true)]
    public sealed class QueryScenarioRunCommand : PanelCommand
    {
        public QueryScenarioRunCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>流すのをやめる。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "流しているシナリオをやめる。それまでに実行した項目の結果はモデルに残る（元に戻すのは Undo）。")]
    [PLResult("stopped", PLResultKind.Flag, Description = "流していたものをやめたか")]
    [PLResult("rootName",         PLResultKind.Text,    Description = "やめた流しの先頭のシナリオ")]
    [PLResult("scenario",         PLResultKind.Text,    Description = "やめたときに居たシナリオ（呼ばれたシナリオの中ならその名前）")]
    [PLResult("scenarioItemId",        PLResultKind.Text,    Description = "やめたときに次に処理するはずだった項目")]
    [PLResult("stepNumber",       PLResultKind.Integer, Description = "その項目の番号（1 始まり）")]
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

    /// <summary>実行したコマンドをシナリオの下書きとして控え始める。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "実行したコマンドをシナリオの下書きとして控え始める。保存していない控えが残っていれば失敗（saveScenarioRecording か discardScenarioRecording を先に）。一時停止中を除き、終了するまでに実行したコマンド（パネル・MCP・UI のどこから撃ったものでも）を、計算済みの引数ごと 1 項目ずつ控える。検証パネルを流すと、項目名・UI での手順・理由も Instruction / Note として入る。シナリオコマンドと UI 自動操作は控えない。")]
    [PLResult("recording", PLResultKind.Flag, Description = "記録中になったか")]
    public sealed class StartScenarioRecordingCommand : PanelCommand
    {
        public StartScenarioRecordingCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>記録を一時停止する。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "記録を一時停止する。一時停止中に実行したコマンドは控えない。控えは残り、resumeScenarioRecording で続きから控える。記録中でなければ失敗。")]
    [PLResult("state", PLResultKind.Text,    Description = "記録の状態（none / recording / paused / ended）")]
    [PLResult("steps", PLResultKind.Integer, Description = "控えた項目の数")]
    public sealed class PauseScenarioRecordingCommand : PanelCommand
    {
        public PauseScenarioRecordingCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>一時停止した記録を再開する。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "一時停止した記録を再開し、続きから控える。一時停止中でなければ失敗（終了した記録は再開できない）。")]
    [PLResult("state", PLResultKind.Text,    Description = "記録の状態（none / recording / paused / ended）")]
    [PLResult("steps", PLResultKind.Integer, Description = "控えた項目の数")]
    public sealed class ResumeScenarioRecordingCommand : PanelCommand
    {
        public ResumeScenarioRecordingCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>記録を終了する。控えは保存か破棄まで残る。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "記録を終了する。控えは残り、saveScenarioRecording でシナリオとして保存するか discardScenarioRecording で破棄するまで、新しい記録は始められない。終了した記録は再開できない。記録中か一時停止中でなければ失敗。")]
    [PLResult("state", PLResultKind.Text,    Description = "記録の状態（none / recording / paused / ended）")]
    [PLResult("steps", PLResultKind.Integer, Description = "控えた項目の数")]
    public sealed class StopScenarioRecordingCommand : PanelCommand
    {
        public StopScenarioRecordingCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>控えをシナリオとして保存する。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "控えた項目をシナリオとして登録し、控えを消す。一時停止中か終了後だけ（記録中は失敗）。失敗したときは控えも状態もそのままなので、名前を変えて保存し直せる。保存したら queryScenarioAudit で、焼いたままの索引が残っていないか確かめること。")]
    [PLResult("name",  PLResultKind.Text,    Description = "登録したシナリオの名前")]
    [PLResult("steps", PLResultKind.Integer, Description = "登録した項目の数")]
    [PLResult("count", PLResultKind.Integer, Description = "登録後のシナリオの数")]
    public sealed class SaveScenarioRecordingCommand : PanelCommand
    {
        [PLParam(Description = "登録するシナリオの名前", Required = true)]
        public string Name { get; }

        [PLParam(Description = "このシナリオで達成したいこと")]
        public string Goal { get; }

        [PLParam(Description = "同じ名前のシナリオがあるとき差し替える")]
        public bool Overwrite { get; }

        [PLParam(Description = "置くフォルダ（「顔/輪郭」のように / で区切る）。無ければ作る。省くと直下（同じ名前を差し替えるときは今の場所のまま）")]
        public string Folder { get; }

        public SaveScenarioRecordingCommand(int modelIndex, string name, string goal = "", bool overwrite = false, string folder = "")
            : base(modelIndex)
        {
            Name      = name ?? "";
            Goal      = goal ?? "";
            Overwrite = overwrite;
            Folder    = folder ?? "";
        }
    }

    /// <summary>控えを捨てて記録をやめる。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "控えた項目を捨てて記録をやめる。記録中・一時停止中・終了後のどれでもよい。控えが無ければ失敗。")]
    [PLResult("steps", PLResultKind.Integer, Description = "捨てた項目の数")]
    public sealed class DiscardScenarioRecordingCommand : PanelCommand
    {
        public DiscardScenarioRecordingCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    /// <summary>記録の状態を返す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "記録の状態と控えた項目の数を返す。")]
    [PLResult("state",    PLResultKind.Text,    Description = "記録の状態（none / recording / paused / ended）")]
    [PLResult("steps",    PLResultKind.Integer, Description = "控えた項目の数")]
    [PLResult("hasDraft", PLResultKind.Flag,    Description = "控えが残っているか（保存か破棄を待っているものを含む）")]
    public sealed class QueryScenarioRecordingCommand : PanelCommand
    {
        public QueryScenarioRecordingCommand(int modelIndex = 0)
            : base(modelIndex)
        {
        }
    }

    // ================================================================
    // 点検する
    // ================================================================

    /// <summary>シナリオの項目を点検する。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオの項目を点検し、撃ち直すと壊れる箇所を返す。literalMeshIndex は IsMeshRef の印が付いた引数に索引が直接入っている項目（名前から引く照会と @ 参照に置き換える）、unknownAction は action を解決できない項目、badRef / missingRef / forwardRef は @<項目 ID>.<キー> の書き方違い・存在しない項目・後ろの項目を指している参照、unknownUsageScene は項目に書いた利用シーンが登録されていないもの。頂点や面の番号は印が無いので点検の対象外。")]
    [PLResult("name",       PLResultKind.Text,      Description = "シナリオの名前")]
    [PLResult("issues",     PLResultKind.Integer,   Description = "指摘の数")]
    [PLResult("scenarioItemIds", PLResultKind.TextArray, Description = "指摘した項目 ID", Optional = true)]
    [PLResult("issueKinds", PLResultKind.TextArray, Description = "指摘の種類。scenarioItemIds と同じ並び", Optional = true)]
    [PLResult("keys",       PLResultKind.TextArray, Description = "指摘した引数のキー。項目そのものの指摘では空。scenarioItemIds と同じ並び", Optional = true)]
    [PLResult("details",    PLResultKind.TextArray, Description = "指摘の中身。scenarioItemIds と同じ並び", Optional = true)]
    public sealed class QueryScenarioAuditCommand : PanelCommand
    {
        [PLParam(Description = "点検するシナリオの名前", Required = true)]
        public string Name { get; }

        public QueryScenarioAuditCommand(int modelIndex, string name)
            : base(modelIndex)
        {
            Name = name ?? "";
        }
    }

    // ================================================================
    // 別ファイルとの出し入れ
    // ================================================================

    /// <summary>シナリオをファイルへ書き出す。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "シナリオを CSV ファイルへ書き出す。形式はシナリオの置き場のファイルと同じ。指定したシナリオが参照しているシナリオも一緒に書くので、そのファイルだけで取り込める。作業フォルダの下だけへ書ける。")]
    [PLResult("path",  PLResultKind.Text,      Description = "書いたファイルの絶対パス")]
    [PLResult("count", PLResultKind.Integer,   Description = "書いたシナリオの数")]
    [PLResult("names", PLResultKind.TextArray, Description = "書いたシナリオの名前。参照先として加わったものも含む", Optional = true)]
    public sealed class ExportScenariosCommand : PanelCommand
    {
        [PLParam(Description = "書き出すシナリオの名前。省くと全部")]
        public string[] Names { get; }

        [PLParam(Description = "書き出し先の CSV ファイルのパス。作業フォルダからの相対でも絶対でもよい", Required = true)]
        public string FilePath { get; }

        public ExportScenariosCommand(int modelIndex, string filePath, string[] names = null)
            : base(modelIndex)
        {
            Names    = names ?? System.Array.Empty<string>();
            FilePath = filePath ?? "";
        }
    }

    /// <summary>ファイルからシナリオを取り込む。</summary>
    [PLCommand(Category = "scenario", Writes = PLWriteScope.None, Description = "CSV ファイルからシナリオを取り込む。ファイルのシナリオをまとめて検査し、1 本でも問題（参照先が無い・循環・名前の重なり）があれば何も登録しない。同じ名前のシナリオは overwrite を立てたときだけ差し替える。作業フォルダの下だけを読める。")]
    [PLResult("added",    PLResultKind.TextArray, Description = "新しく登録したシナリオの名前", Optional = true)]
    [PLResult("replaced", PLResultKind.TextArray, Description = "差し替えたシナリオの名前", Optional = true)]
    [PLResult("count",    PLResultKind.Integer,   Description = "取り込み後のシナリオの数")]
    public sealed class ImportScenariosCommand : PanelCommand
    {
        [PLParam(Description = "取り込む CSV ファイルのパス。作業フォルダからの相対でも絶対でもよい", Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "同じ名前のシナリオがあるとき差し替える。立てないと、重なる名前を挙げて失敗する")]
        public bool Overwrite { get; }

        [PLParam(Description = "新しく入るシナリオを置くフォルダ。省くと直下。差し替えるシナリオは今の場所のまま")]
        public string Folder { get; }

        public ImportScenariosCommand(int modelIndex, string filePath, bool overwrite = false, string folder = "")
            : base(modelIndex)
        {
            FilePath  = filePath ?? "";
            Overwrite = overwrite;
            Folder    = folder ?? "";
        }
    }
}
