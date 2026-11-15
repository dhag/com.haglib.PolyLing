// PanelCommand.Morph.cs
// 表情の CSV（BlendShapeSync 形式）の読み書きの操作要求と、PMX のモーフパネルの数。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間。PanelCommand.cs から分割）
//
// 【段 7（PolyLing_モーフ整理計画.md 3.12）】旧モーフ（隠しオブジェクト）のコマンドはここから撤去した。
//   置き換え先：
//     convertMeshToMorph            → addShape（今の見た目から）
//     convertMorphToMesh            → extractShapeModel
//     createMorphFromDiff           → addShape ＋ importShapeMqo
//     createMorphSet                → addExpression
//     setMorphEntryWeights          → setExpressionShapes
//     setMorphExpressionAttributes  → setExpressionAttributes（PanelCommand.Expression.cs）
//     deleteMorphExpression         → deleteExpression（PanelCommand.Expression.cs）
//     start/apply/endMorphPreview   → previewShapes / endShapePreview（PanelCommand.Shape.cs）
//     expandMorphExpressionToModel  → extractShapeModel
//     selectAllMorphs / deselectAllMorphs → 廃止（モーフの選択が無くなった）

namespace Poly_Ling.Data
{
    /// <summary>PMX のモーフパネル。</summary>
    public static class MorphPanels
    {
        /// <summary>
        /// パネルの種類数（0=眉 / 1=目 / 2=口 / 3=その他）。形・表情の Panel はこの範囲。
        /// </summary>
        public const int Count = 4;
    }

    [PLCommand(Category = "morph", Writes = PLWriteScope.ModelWide, Description = "BlendShapeSync CSV を読み込んで表情へ足す（同名は上書き）。行は形の名前で指す。")]
    public class ImportMorphCsvCommand : PanelCommand
    {
        [PLParam(TextKey = "MorphCsvPath", Description = "読み込む CSV のパス", Required = true)]
        public string Path { get; }
        public ImportMorphCsvCommand(int modelIndex, string path) : base(modelIndex) { Path = path ?? ""; }
    }

    [PLCommand(Category = "morph", Writes = PLWriteScope.None, Description = "表情を BlendShapeSync CSV へ書き出す（形の名前で書く）。")]
    public class ExportMorphCsvCommand : PanelCommand
    {
        [PLParam(TextKey = "MorphCsvPath", Description = "保存先 CSV のパス", Required = true)]
        public string Path { get; }
        public ExportMorphCsvCommand(int modelIndex, string path) : base(modelIndex) { Path = path ?? ""; }
    }
}
