// PanelCommand.ShapeMqo.cs
// 形と MQO の連携の操作要求：形へ読み込む・再読み込み・MQO へ書き出す（PolyLing_モーフ整理計画.md 3.8・段 4）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 対応付け・読み込み・書き出しの決まりは MQO/ShapeMqoOps.cs の冒頭注記。
// パスは作業フォルダからの相対でも絶対でもよい（読むのは PLSandbox.TryResolveRead、書くのは TryResolveWrite を通す）。
// 画面では「形」のパネル（PlayerShapeSubPanel）の MQO の区画から送る。パス欄に直接書けば
// ファイルを選ぶ窓を通さずに実行できる（窓を開く「…」のボタンはパス欄を埋めるだけ）。

namespace Poly_Ling.Data
{
    [PLCommand(Category = "morph", Effects = PLCommandEffect.VertexPosition, Verification = PLCommandVerification.Visual,
        Writes = PLWriteScope.ModelWide,
        Description = "MQO の頂点位置を今の形として読む。MQO に入っているオブジェクトだけがその位置になり、ほかのオブジェクトは触らない。newShape に名前を渡すと、基本の形から新しい形を作って切り替えてから読む。対応付けは ①覚えた組（同じファイルを読んだ形のとき。pairModels/pairMqos を渡せばそれ）→ ②名前の一致 → ③展開頂点数の一致（相手が 1 つに決まるときだけ）。決まらないものと頂点数が合わないものは読まずに unresolved・mismatched に返す。読み込み元（ファイル・組・倍率・反転）を形に覚える（reloadShapeMqo で読み直す）。今の形が基本の形のときは keepShapeOffsets に従い全部の形を同じだけ動かす。Undo は 1 件。")]
    [PLUiRoute("形（MQO から今の形へ読み込む）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.list", "shape.mqoPath", "shape.mqoImport",
        Run = "shape.mqoImport", Note = "一覧で選んだ形（今の形）へ読む。新しい名前の欄は使わない")]
    [PLUiRoute("形（新しい形を作って MQO から読み込む）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.newName", "shape.mqoPath", "shape.mqoImportNew",
        Run = "shape.mqoImportNew", Order = 1)]
    [PLUiArg("mqoPath", "shape.mqoPath")]
    [PLUiArg("newShape", "shape.newName")]
    [PLUiArg("scale", "shape.mqoScale")]
    [PLUiArg("flipX", "shape.mqoFlipX")]
    [PLUiArg("flipZ", "shape.mqoFlipZ")]
    [PLUiArg("keepShapeOffsets", "shape.mqoKeepOffsets")]
    [PLUiNoField("画面では組を指定しない（覚えた組 → 名前 → 頂点数で決まる）", "pairModels", "pairMqos")]
    [PLResult("shape",          PLResultKind.Text,      Description = "読んだ形の名前。基本の形なら空")]
    [PLResult("pairs",          PLResultKind.Integer,   Description = "読んだ組の数")]
    [PLResult("transferred",    PLResultKind.Integer,   Description = "書き換えた頂点の数（展開頂点で数える）")]
    [PLResult("modelNames",     PLResultKind.TextArray, Description = "読んだ組のモデル側の名前", Optional = true)]
    [PLResult("mqoNames",       PLResultKind.TextArray, Description = "読んだ組の MQO 側の名前。modelNames と同じ並び", Optional = true)]
    [PLResult("matchedBy",      PLResultKind.TextArray, Description = "組の決め方（remembered / name / vertexCount）。modelNames と同じ並び", Optional = true)]
    [PLResult("unresolved",     PLResultKind.TextArray, Description = "相手が決まらず読まなかった MQO のオブジェクト", Optional = true)]
    [PLResult("mismatched",     PLResultKind.TextArray, Description = "展開頂点数が合わず読まなかった組（MQO名(数) / モデル名(数)）", Optional = true)]
    [PLResult("movedVertices",  PLResultKind.Integer,   Description = "読んだ形で基本の形と位置が違う頂点の数（基本の形なら 0）")]
    [PLResult("shiftedEntries", PLResultKind.Integer,   Description = "基本の形の差し替えで、差分を保つために動かした形の控えの数")]
    [PLResult("droppedEntries", PLResultKind.Integer,   Description = "基本の形の差し替え（差分を保たない）で、基本の形と同じになって消した控えの数")]
    public class ImportShapeMqoCommand : PanelCommand
    {
        [PLParam(Description = "読む MQO のパス", Required = true)]
        public string MqoPath { get; }
        [PLParam(Description = "空でなければ、この名前の形を基本の形から作って切り替えてから読む")]
        public string NewShape { get; }
        [PLParam(Description = "MQO 座標に掛ける倍率")]
        public float Scale { get; }
        [PLParam(Description = "X を反転する（MQO は右手系。既定 true）")]
        public bool FlipX { get; }
        [PLParam(Description = "Z を反転する")]
        public bool FlipZ { get; }
        [PLParam(Description = "今の形が基本の形のとき、形の差分を保つ（全部の形を同じだけ動かす）。false なら形は絶対位置のまま")]
        public bool KeepShapeOffsets { get; }
        [PLParam(Description = "組を指定するときのモデル側の描画オブジェクトの名前。pairMqos と同じ並び。①として先に使う")]
        public string[] PairModels { get; }
        [PLParam(Description = "組を指定するときの MQO 側のオブジェクトの名前。pairModels と同じ長さ")]
        public string[] PairMqos { get; }

        public ImportShapeMqoCommand(int modelIndex, string mqoPath, string newShape = "",
                                     float scale = 0.01f, bool flipX = true, bool flipZ = false,
                                     bool keepShapeOffsets = true,
                                     string[] pairModels = null, string[] pairMqos = null)
            : base(modelIndex)
        {
            MqoPath = mqoPath ?? ""; NewShape = newShape ?? "";
            Scale = scale; FlipX = flipX; FlipZ = flipZ; KeepShapeOffsets = keepShapeOffsets;
            PairModels = pairModels ?? new string[0]; PairMqos = pairMqos ?? new string[0];
        }
    }

    [PLCommand(Category = "morph", Effects = PLCommandEffect.VertexPosition, Verification = PLCommandVerification.Visual,
        Writes = PLWriteScope.ModelWide,
        Description = "形が覚えている読み込み元（MQO ファイル・組・倍率・反転）で読み直す（メタセコ側の修正を取り込む）。その形が今の形でなければ切り替えてから読む。対応付けは覚えた組から（importShapeMqo と同じ順）。Undo は 1 件。")]
    [PLUiRoute("形（MQO の再読み込み）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.list", "shape.mqoReload", Run = "shape.mqoReload")]
    [PLUiArg("keepShapeOffsets", "shape.mqoKeepOffsets")]
    [PLUiNoField("一覧で選んだ形（今の形）を読み直す", "name")]
    [PLResult("shape",          PLResultKind.Text,      Description = "読んだ形の名前。基本の形なら空")]
    [PLResult("path",           PLResultKind.Text,      Description = "読んだ MQO のパス（覚えていたもの）")]
    [PLResult("pairs",          PLResultKind.Integer,   Description = "読んだ組の数")]
    [PLResult("transferred",    PLResultKind.Integer,   Description = "書き換えた頂点の数")]
    [PLResult("modelNames",     PLResultKind.TextArray, Description = "読んだ組のモデル側の名前", Optional = true)]
    [PLResult("mqoNames",       PLResultKind.TextArray, Description = "読んだ組の MQO 側の名前", Optional = true)]
    [PLResult("matchedBy",      PLResultKind.TextArray, Description = "組の決め方", Optional = true)]
    [PLResult("unresolved",     PLResultKind.TextArray, Description = "相手が決まらず読まなかった MQO のオブジェクト", Optional = true)]
    [PLResult("mismatched",     PLResultKind.TextArray, Description = "展開頂点数が合わず読まなかった組", Optional = true)]
    [PLResult("movedVertices",  PLResultKind.Integer,   Description = "読んだ形で基本の形と位置が違う頂点の数")]
    [PLResult("shiftedEntries", PLResultKind.Integer,   Description = "差分を保つために動かした形の控えの数")]
    [PLResult("droppedEntries", PLResultKind.Integer,   Description = "差分を保たないときに消した控えの数")]
    public class ReloadShapeMqoCommand : PanelCommand
    {
        [PLParam(Description = "読み直す形の名前。空なら基本の形")]
        public string Name { get; }
        [PLParam(Description = "基本の形を読み直すとき、形の差分を保つ")]
        public bool KeepShapeOffsets { get; }

        public ReloadShapeMqoCommand(int modelIndex, string name = "", bool keepShapeOffsets = true)
            : base(modelIndex)
        { Name = name ?? ""; KeepShapeOffsets = keepShapeOffsets; }
    }

    [PLCommand(Category = "morph", Writes = PLWriteScope.None,
        Description = "今の形の位置を、元にする MQO の対応するオブジェクトへ書き戻して、別のファイル（または同じファイル）へ保存する（既存の MQO 部分書き出し）。useSource が true で今の形に読み込み元があれば、元にする MQO・組・倍率・反転はそれを使う（referencePath を渡せば元にする MQO だけ差し替える）。無ければ referencePath と scale・flipX・flipZ を使い、名前 → 頂点数で組む。")]
    [PLUiRoute("形（MQO へ書き出す）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.list", "shape.mqoExportPath", "shape.mqoExport",
        Run = "shape.mqoExport", Note = "元にする MQO は、今の形に読み込み元があればそれ、無ければ MQO のパス欄")]
    [PLUiArg("filePath", "shape.mqoExportPath")]
    [PLUiArg("scale", "shape.mqoScale")]
    [PLUiArg("flipX", "shape.mqoFlipX")]
    [PLUiArg("flipZ", "shape.mqoFlipZ")]
    [PLUiNoField("読み込み元が無いときだけ、パネルが MQO のパス欄を渡す", "referencePath")]
    [PLUiNoField("パネルは常に true で送る", "useSource")]
    [PLResult("shape",       PLResultKind.Text,      Description = "書き出した形の名前。基本の形なら空")]
    [PLResult("path",        PLResultKind.Text,      Description = "書いたファイル")]
    [PLResult("reference",   PLResultKind.Text,      Description = "元にした MQO")]
    [PLResult("pairs",       PLResultKind.Integer,   Description = "書き戻した組の数")]
    [PLResult("transferred", PLResultKind.Integer,   Description = "書き戻した MQO の頂点の数")]
    [PLResult("modelNames",  PLResultKind.TextArray, Description = "書き戻した組のモデル側の名前", Optional = true)]
    [PLResult("mqoNames",    PLResultKind.TextArray, Description = "書き戻した組の MQO 側の名前", Optional = true)]
    [PLResult("unresolved",  PLResultKind.TextArray, Description = "相手が決まらず書かなかった MQO のオブジェクト", Optional = true)]
    [PLResult("mismatched",  PLResultKind.TextArray, Description = "展開頂点数が合わず書かなかった組", Optional = true)]
    public class ExportShapeMqoCommand : PanelCommand
    {
        [PLParam(Description = "書き出し先の MQO のパス", Required = true)]
        public string FilePath { get; }
        [PLParam(Description = "元にする MQO のパス。空なら今の形の読み込み元")]
        public string ReferencePath { get; }
        [PLParam(Description = "今の形の読み込み元（組・倍率・反転）を使う")]
        public bool UseSource { get; }
        [PLParam(Description = "読み込み元を使わないときの倍率（MQO 座標 × 倍率 = モデルの位置）")]
        public float Scale { get; }
        [PLParam(Description = "読み込み元を使わないときの X の反転")]
        public bool FlipX { get; }
        [PLParam(Description = "読み込み元を使わないときの Z の反転")]
        public bool FlipZ { get; }

        public ExportShapeMqoCommand(int modelIndex, string filePath, string referencePath = "", bool useSource = true,
                                     float scale = 0.01f, bool flipX = true, bool flipZ = false)
            : base(modelIndex)
        {
            FilePath = filePath ?? ""; ReferencePath = referencePath ?? ""; UseSource = useSource;
            Scale = scale; FlipX = flipX; FlipZ = flipZ;
        }
    }
}
