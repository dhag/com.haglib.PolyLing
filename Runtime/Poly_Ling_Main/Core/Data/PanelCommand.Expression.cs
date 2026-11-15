// PanelCommand.Expression.cs
// 表情（モーフエクスプレッション）を「形の名前 × 重み」で作る・変える・名前を変える・消す操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【正本】PolyLing_モーフ整理計画.md 3.5・3.11・3.12・段 5・段 7
//   表情はグループ（形の名前 × 重み＝GroupChildren）と VRM の付帯データだけを持つ。
//   ここのコマンドは子のうち「形を指すもの」だけを書き換え、形でない子には触らない。
//
// 【画面】左ペイン「ボーン・モーフ」の「モーフエクスプレッション編集」（パネル ID は morph）。

namespace Poly_Ling.Data
{
    [PLCommand(Category = "morph", Effects = PLCommandEffect.Morph, Writes = PLWriteScope.ModelWide,
        Description = "形の名前と重みの並びから、グループの表情を 1 つ作る。shapes は空でもよい（あとで setExpressionShapes で足す）。")]
    [PLUiRoute("表情（グループを作る）", "leftPane.fold.BoneMorph", "leftPane.morphBtn", "morph.newName", "morph.add", Run = "morph.add",
        Note = "作った表情は形を指していない。一覧で選び、形の欄と「足す」で形を足す")]
    [PLUiArg("name", "morph.newName")]
    [PLUiNoField("作ったあと、選んだ表情の欄で変える", "nameEnglish", "panel")]
    [PLUiNoField("作ったあと、選んだ表情の形の欄と「足す」で足す", "shapes", "weights")]
    [PLResult("setIndex", PLResultKind.Integer, Description = "作った表情の番号")]
    public class AddExpressionCommand : PanelCommand
    {
        [PLParam(Description = "表情の名前", Required = true)]
        public string Name { get; }
        [PLParam(Description = "英語名。空でよい")]
        public string NameEnglish { get; }
        [PLParam(Description = "PMX のモーフパネル。0=眉, 1=目, 2=口, 3=その他",
                 Min = 0, Max = MorphPanels.Count - 1)]
        public int Panel { get; }
        [PLParam(Description = "指す形の名前（weights と同じ並び）")]
        public string[] Shapes { get; }
        [PLParam(Description = "形ごとの重み（shapes と同じ並び）")]
        public float[] Weights { get; }

        public AddExpressionCommand(int modelIndex, string name, string nameEnglish = "", int panel = 3,
                                    string[] shapes = null, float[] weights = null)
            : base(modelIndex)
        {
            Name = name ?? ""; NameEnglish = nameEnglish ?? ""; Panel = panel;
            Shapes = shapes ?? System.Array.Empty<string>();
            Weights = weights ?? System.Array.Empty<float>();
        }
    }

    [PLCommand(Category = "morph", Effects = PLCommandEffect.Morph, Writes = PLWriteScope.ModelWide,
        Description = "グループの表情が指す形（子のうち形であるもの）の並びと重みを、渡したものに置き換える（Undo 1 回ぶん）。形でない子は変えない。頂点の表情には使えない。")]
    [PLUiRoute("表情（形を足す）", "leftPane.fold.BoneMorph", "leftPane.morphBtn", "morph.sets", "morph.entry.addShape", "morph.entry.add", Run = "morph.entry.add",
        Note = "足した形の重みは 1。行を選んで重みの欄で変える")]
    [PLUiRoute("表情（形の重み）", "leftPane.fold.BoneMorph", "leftPane.morphBtn", "morph.sets", "morph.set.entries", "morph.entry.weight", Order = 1,
        Note = "形の行を選び、重みの欄を変えると確定する")]
    [PLUiRoute("表情（形を外す）", "leftPane.fold.BoneMorph", "leftPane.morphBtn", "morph.sets", "morph.set.entries", "morph.entry.remove", Run = "morph.entry.remove", Order = 2)]
    [PLUiNoField("一覧で選んだ表情", "setIndex")]
    [PLUiNoField("パネルの形の行の並びから組み立てる（足す・外す・重みの欄）", "shapes", "weights")]
    public class SetExpressionShapesCommand : PanelCommand
    {
        [PLParam(TextKey = "MorphExpressionIndex", Description = "表情の番号", Required = true)]
        public int SetIndex { get; }
        [PLParam(Description = "指す形の名前（weights と同じ並び）。空にすると形を指さなくなる")]
        public string[] Shapes { get; }
        [PLParam(Description = "形ごとの重み（shapes と同じ並び）")]
        public float[] Weights { get; }

        public SetExpressionShapesCommand(int modelIndex, int setIndex, string[] shapes = null, float[] weights = null)
            : base(modelIndex)
        {
            SetIndex = setIndex;
            Shapes = shapes ?? System.Array.Empty<string>();
            Weights = weights ?? System.Array.Empty<float>();
        }
    }

    [PLCommand(Category = "morph", Writes = PLWriteScope.ModelWide,
        Description = "表情の名前（JP/EN）とパネルを変える。形の名前・英語名・パネルは setShapeAttributes で変える。")]
    [PLUiRoute("表情（名前・英語名・パネル）", "leftPane.fold.BoneMorph", "leftPane.morphBtn", "morph.sets", "morph.set.name", "morph.set.nameEn", "morph.set.panel",
        Note = "欄を変えると確定する（名前の欄はフォーカスが外れたとき）")]
    [PLUiArg("name", "morph.set.name")]
    [PLUiArg("nameEnglish", "morph.set.nameEn")]
    [PLUiArg("panel", "morph.set.panel", Values = "0,1,2,3")]
    [PLUiNoField("一覧で選んだ表情", "setIndex")]
    public class SetExpressionAttributesCommand : PanelCommand
    {
        [PLParam(TextKey = "MorphExpressionIndex", Description = "表情の番号", Required = true)]
        public int SetIndex { get; }
        [PLParam(TextKey = "MorphExpressionName", Description = "名前（JP）")]
        public string Name { get; }
        [PLParam(TextKey = "MorphExpressionNameEn", Description = "名前（EN）")]
        public string NameEnglish { get; }
        [PLParam(TextKey = "MorphExpressionPanel", Description = "パネル（0 眉 / 1 目 / 2 口 / 3 その他）",
                 Min = 0, Max = MorphPanels.Count - 1)]
        public int Panel { get; }
        public SetExpressionAttributesCommand(int modelIndex, int setIndex, string name, string nameEnglish, int panel)
            : base(modelIndex) { SetIndex = setIndex; Name = name ?? ""; NameEnglish = nameEnglish ?? ""; Panel = panel; }
    }

    [PLCommand(Category = "morph", Writes = PLWriteScope.ModelWide,
        Description = "表情を 1 つ消す。表情が指す形は消さない。")]
    [PLUiRoute("表情（削除）", "leftPane.fold.BoneMorph", "leftPane.morphBtn", "morph.sets", "morph.set.delete",
        Note = "「この表情を削除」は確認ダイアログを開く（人の操作）")]
    [PLUiNoField("一覧で選んだ表情", "setIndex")]
    public class DeleteExpressionCommand : PanelCommand
    {
        [PLParam(TextKey = "MorphExpressionIndex", Description = "消す表情の番号", Required = true)]
        public int SetIndex { get; }
        public DeleteExpressionCommand(int modelIndex, int setIndex) : base(modelIndex) { SetIndex = setIndex; }
    }
}
