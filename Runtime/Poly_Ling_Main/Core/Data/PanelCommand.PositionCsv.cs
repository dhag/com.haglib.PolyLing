// PanelCommand.PositionCsv.cs
// 位置付き CSV（名前・親・位置）から、ボーンまたは空の描画オブジェクトを作る操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）

namespace Poly_Ling.Data
{
    /// <summary>
    /// 位置付き CSV を読み、行ごとにボーンまたは空の描画オブジェクトを作って親子につなぐ。
    /// 実処理は PositionCsvNodeBuilder。
    /// </summary>
    [PLCommand(Category = "io.import",
        Effects = PLCommandEffect.CreatesObject | PLCommandEffect.Skeleton | PLCommandEffect.Hierarchy,
        Writes = PLWriteScope.AddOnly,
        Tags = "bone,humanoid,skeleton",
        Description = "名前・親・位置の列を持つ CSV を読み、行ごとにボーンか空の描画オブジェクトを作って親子につなぐ。位置はワールド座標。作業フォルダの下だけを読める。")]
    [PLResult("created",            PLResultKind.Integer, Description = "作った数")]
    [PLResult("roots",              PLResultKind.Integer, Description = "親が付かなかった数")]
    [PLResult("attachedToExisting", PLResultKind.Integer, Description = "モデルに既にあるオブジェクトを親にした数")]
    [PLResult("renamed",            PLResultKind.Integer, Description = "既存の名前と重なったため名前を変えた数")]
    [PLResult("message",            PLResultKind.Text,    Description = "結果の要約")]
    public class CreateObjectsFromPositionCsvCommand : PanelCommand
    {
        [PLParam(Description = "読み込む CSV のパス。作業フォルダからの相対でも絶対でもよい", Required = true)]
        public string FilePath { get; }

        [PLParam(Description = "ボーンとして作る。false にすると頂点の無い描画オブジェクトとして作る")]
        public bool AsBones { get; }

        [PLParam(Description = "名前の列の見出し。空にすると name")]
        public string NameColumn { get; }

        [PLParam(Description = "親の名前の列の見出し。空にすると親を読まない（全部を親なしで作る）")]
        public string ParentColumn { get; }

        [PLParam(Description = "X 座標の列の見出し。空にすると x")]
        public string XColumn { get; }

        [PLParam(Description = "Y 座標の列の見出し。空にすると y")]
        public string YColumn { get; }

        [PLParam(Description = "Z 座標の列の見出し。空にすると z")]
        public string ZColumn { get; }

        [PLParam(Description = "作る行の名前。省くと全行。外した行を親に持つ行は、その行の親をさらに辿ってつなぐ")]
        public string[] IncludeNames { get; }

        public CreateObjectsFromPositionCsvCommand(
            int modelIndex,
            string filePath,
            bool asBones = true,
            string nameColumn = "name",
            string parentColumn = "",
            string xColumn = "x",
            string yColumn = "y",
            string zColumn = "z",
            string[] includeNames = null)
            : base(modelIndex)
        {
            FilePath     = filePath ?? "";
            AsBones      = asBones;
            NameColumn   = nameColumn ?? "";
            ParentColumn = parentColumn ?? "";
            XColumn      = xColumn ?? "";
            YColumn      = yColumn ?? "";
            ZColumn      = zColumn ?? "";
            IncludeNames = includeNames ?? System.Array.Empty<string>();
        }
    }
}
