// PanelCommand.Shape.cs
// 形（名前付きの頂点位置）の操作要求：追加・削除・属性・切り替え・入れ替え・左右反転・照会・点検。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 形は名前で指す。空文字は基本の形。中身の決まりは ShapeData.cs の冒頭注記。
//
// 【画面】（PolyLing_モーフ整理計画.md 3.11・段 3）
//   左ペイン「ボーン・モーフ」の「形」を押すと右に出るパネル（PlayerShapeSubPanel。パネル ID は shape）が、
//   ここのコマンドを送る。画面に入口があるものは経路（PLUiRoute）を宣言し、照会と点検だけ NoUi。
//   複製のコマンドは置かない。addShape の sourceShape に元の形を渡すのが複製。

namespace Poly_Ling.Data
{
    [PLCommand(Category = "morph", Effects = PLCommandEffect.Morph, Writes = PLWriteScope.ModelWide,
        Description = "形（名前付きの頂点位置）を 1 つ足す。位置は sourceShape の形の写し（空なら基本の形と同じ）。sourceShape に元の形を渡すと複製になる。makeCurrent が true なら足した形へ切り替える。")]
    [PLUiRoute("形（基本の形から追加）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.newName", "shape.addFromBase", Run = "shape.addFromBase")]
    [PLUiRoute("形（今の見た目から追加）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.newName", "shape.addFromCurrent", Run = "shape.addFromCurrent", Order = 1)]
    [PLUiRoute("形（複製）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.duplicate", Run = "shape.duplicate", Order = 2,
        Note = "複製は名前を自動で付ける（元の名前＋「のコピー」）")]
    [PLUiArg("name", "shape.newName")]
    [PLUiNoField("押したボタンと今の形で決まる（基本の形から＝空、今の見た目から・複製＝今の形）", "sourceShape")]
    [PLUiNoField("追加のボタンは足した形へ切り替える。複製は切り替えない", "makeCurrent")]
    [PLUiNoField("足したあと、一覧の下の欄で変える", "nameEnglish", "panel")]
    [PLResult("shapeId",       PLResultKind.Integer, Description = "足した形の番号")]
    [PLResult("movedVertices", PLResultKind.Integer, Description = "基本の形と位置が違う頂点の数")]
    public class AddShapeCommand : PanelCommand
    {
        [PLParam(Description = "足す形の名前。モデルの中で一意", Required = true)]
        public string Name { get; }
        [PLParam(Description = "英語名。空でよい")]
        public string NameEnglish { get; }
        [PLParam(Description = "PMX のモーフパネル。0=眉, 1=目, 2=口, 3=その他",
                 Min = 0, Max = MorphPanels.Count - 1)]
        public int Panel { get; }
        [PLParam(Description = "位置を写す元の形の名前。空なら基本の形")]
        public string SourceShape { get; }
        [PLParam(Description = "true なら足した形を今の形にする")]
        public bool MakeCurrent { get; }

        public AddShapeCommand(int modelIndex, string name, string nameEnglish = "", int panel = 3,
                               string sourceShape = "", bool makeCurrent = false)
            : base(modelIndex)
        {
            Name = name ?? ""; NameEnglish = nameEnglish ?? ""; Panel = panel;
            SourceShape = sourceShape ?? ""; MakeCurrent = makeCurrent;
        }
    }

    [PLCommand(Category = "morph", Effects = PLCommandEffect.Morph, Writes = PLWriteScope.ModelWide,
        Description = "形を 1 つ消す。今の形を消すときは、先に基本の形へ切り替える。")]
    [PLUiRoute("形（削除）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.list", "shape.delete", Run = "shape.delete")]
    [PLUiNoField("一覧で選んだ形（今の形）を消す", "name")]
    public class DeleteShapeCommand : PanelCommand
    {
        [PLParam(Description = "消す形の名前", Required = true)]
        public string Name { get; }
        public DeleteShapeCommand(int modelIndex, string name) : base(modelIndex) { Name = name ?? ""; }
    }

    [PLCommand(Category = "morph", Writes = PLWriteScope.ModelWide,
        Description = "形の名前・英語名・パネルを変える。頂点位置は変えない。")]
    [PLUiRoute("形（名前・英語名・パネル）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.list", "shape.name", "shape.nameEn", "shape.panel",
        Note = "欄を変えると確定する（名前の欄はフォーカスが外れたとき）")]
    [PLUiArg("newName", "shape.name")]
    [PLUiArg("nameEnglish", "shape.nameEn")]
    [PLUiArg("panel", "shape.panel", Values = "0,1,2,3")]
    [PLUiNoField("一覧で選んだ形（今の形）を変える", "name")]
    public class SetShapeAttributesCommand : PanelCommand
    {
        [PLParam(Description = "変える形の今の名前", Required = true)]
        public string Name { get; }
        [PLParam(Description = "新しい名前。モデルの中で一意", Required = true)]
        public string NewName { get; }
        [PLParam(Description = "英語名。空でよい")]
        public string NameEnglish { get; }
        [PLParam(Description = "PMX のモーフパネル。0=眉, 1=目, 2=口, 3=その他",
                 Min = 0, Max = MorphPanels.Count - 1)]
        public int Panel { get; }

        public SetShapeAttributesCommand(int modelIndex, string name, string newName, string nameEnglish = "", int panel = 3)
            : base(modelIndex)
        { Name = name ?? ""; NewName = newName ?? ""; NameEnglish = nameEnglish ?? ""; Panel = panel; }
    }

    [PLCommand(Category = "morph", Effects = PLCommandEffect.VertexPosition, Verification = PLCommandVerification.Visual,
        Writes = PLWriteScope.ModelWide,
        Description = "今の形を切り替える。全描画オブジェクトの頂点位置が、その形の位置になる。以後の頂点編集はその形に入る。")]
    [PLUiRoute("形（切り替え）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.list",
        Note = "一覧の行を選ぶと切り替わる。先頭の行は基本の形")]
    [PLUiNoField("一覧の行で選ぶ", "name")]
    public class SetCurrentShapeCommand : PanelCommand
    {
        [PLParam(Description = "切り替える先の形の名前。空なら基本の形")]
        public string Name { get; }
        public SetCurrentShapeCommand(int modelIndex, string name = "") : base(modelIndex) { Name = name ?? ""; }
    }

    [PLCommand(Category = "morph", Effects = PLCommandEffect.VertexPosition, Verification = PLCommandVerification.Visual,
        Writes = PLWriteScope.ModelWide,
        Description = "今の形と直前の形を入れ替える（見比べ用）。直前の形が無ければ基本の形へ切り替える。")]
    [PLUiRoute("形（直前の形と入れ替え）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.swap", Run = "shape.swap")]
    public class SwapShapeCommand : PanelCommand
    {
        public SwapShapeCommand(int modelIndex) : base(modelIndex) { }
    }

    [PLCommand(Category = "morph", Effects = PLCommandEffect.Morph, Writes = PLWriteScope.ModelWide,
        Description = "形を左右反転した新しい形を作る。同じオブジェクトの中の対称頂点（基本の形の位置で対応を取る）へ、基本の形との差を鏡映して写す。ミラーの関係にあるオブジェクト（対・実体化したミラー・表示ミラー）は元の形の写しになる（左右が鏡映で決まっているので反転しても同じ）。今の形は変えない。")]
    [PLUiRoute("形（左右反転した形を作る）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.list", "shape.newName", "shape.mirror", Run = "shape.mirror")]
    [PLUiArg("newName", "shape.newName")]
    [PLUiNoField("一覧で選んだ形（今の形）を反転する", "name")]
    [PLUiNoField("元の形の値を引き継ぐ。作ったあと、一覧の下の欄で変える", "nameEnglish", "panel")]
    [PLResult("shapeId",           PLResultKind.Integer, Description = "作った形の番号")]
    [PLResult("movedVertices",     PLResultKind.Integer, Description = "作った形で、基本の形と位置が違う頂点の数")]
    [PLResult("unmatchedVertices", PLResultKind.Integer, Description = "元の形で動いていて、対称の相手が見つからなかった頂点の数（その動きは作った形に入らない）")]
    public class MirrorShapeCommand : PanelCommand
    {
        [PLParam(Description = "反転する元の形の名前。基本の形（空）は反転できない", Required = true)]
        public string Name { get; }
        [PLParam(Description = "作る形の名前。モデルの中で一意", Required = true)]
        public string NewName { get; }
        [PLParam(Description = "作る形の英語名。空でよい")]
        public string NameEnglish { get; }
        [PLParam(Description = "作る形の PMX のモーフパネル。0=眉, 1=目, 2=口, 3=その他。-1 なら元の形と同じ",
                 Min = -1, Max = MorphPanels.Count - 1)]
        public int Panel { get; }

        public MirrorShapeCommand(int modelIndex, string name, string newName, string nameEnglish = "", int panel = -1)
            : base(modelIndex)
        { Name = name ?? ""; NewName = newName ?? ""; NameEnglish = nameEnglish ?? ""; Panel = panel; }
    }

    [PLCommand(Category = "morph", Writes = PLWriteScope.None,
        Description = "描画オブジェクトをその形の位置で複製し、新しいモデルとして足す（形・表情は持たない）。今のモデルは変えない。")]
    [PLUiRoute("形（モデルとして取り出す）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.list", "shape.extract", Run = "shape.extract",
        Note = "一覧で選んだ形（今の形）を取り出す。基本の形も取り出せる")]
    [PLUiNoField("一覧で選んだ形（今の形）", "name")]
    [PLResult("newModelIndex", PLResultKind.Integer, Description = "足したモデルの番号")]
    public class ExtractShapeModelCommand : PanelCommand
    {
        [PLParam(Description = "取り出す形の名前。空なら基本の形")]
        public string Name { get; }
        public ExtractShapeModelCommand(int modelIndex, string name = "") : base(modelIndex) { Name = name ?? ""; }
    }

    // 試し表示は頂点位置を変えず、表示用の足し込み（WorkingPositions）に書く（表情の試し表示・モーション再生と同じ 1 系統。
    // PolyLing_モーフ整理計画.md 3.6）。始めるとき、今の形が基本の形でなければ基本の形へ切り替える（記録が 1 件残る）。
    [PLCommand(Category = "morph", Effects = PLCommandEffect.VertexPosition, Verification = PLCommandVerification.Visual,
        Writes = PLWriteScope.ModelWide,
        Description = "形の名前と重みを指定して試し表示する（頂点位置は変えない。表示だけ）。前の試し表示は置き換える。今の形が基本の形でなければ、先に基本の形へ切り替える。")]
    [PLUiRoute("形（試し表示）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.previewShape", "shape.previewWeight",
        Note = "試し表示する形を選び、重みを動かすと始まる")]
    [PLUiNoField("試し表示する形の欄で 1 つ選ぶ", "shapes")]
    [PLUiNoField("重みの欄の値（形 1 つぶん）", "weights")]
    [PLResult("shapeCount", PLResultKind.Integer, Description = "試し表示に入った形の数（名前が当たらない・UV の形は数えない）")]
    public class PreviewShapesCommand : PanelCommand
    {
        [PLParam(Description = "試し表示する形の名前（weights と同じ並び）", Required = true)]
        public string[] Shapes { get; }
        [PLParam(Description = "形ごとの重み（shapes と同じ並び。足りなければ 1）")]
        public float[] Weights { get; }
        public PreviewShapesCommand(int modelIndex, string[] shapes, float[] weights = null)
            : base(modelIndex)
        {
            Shapes = shapes ?? System.Array.Empty<string>();
            Weights = weights ?? System.Array.Empty<float>();
        }
    }

    [PLCommand(Category = "morph", Writes = PLWriteScope.ModelWide,
        Description = "形の試し表示を終え、表示を元へ戻す（頂点位置は変えていない）。")]
    [PLUiRoute("形（試し表示を終える）", "leftPane.fold.BoneMorph", "leftPane.shapeBtn", "shape.previewEnd", Run = "shape.previewEnd")]
    public class EndShapePreviewCommand : PanelCommand
    {
        public EndShapePreviewCommand(int modelIndex) : base(modelIndex) { }
    }

    [PLCommand(Category = "morph", Writes = PLWriteScope.None, NoUi = true,
        Description = "形の一覧と今の形を返す。")]
    [PLResult("current",       PLResultKind.Text,         Description = "今の形の名前。基本の形なら空")]
    [PLResult("previous",      PLResultKind.Text,         Description = "直前の形の名前。基本の形なら空")]
    [PLResult("count",         PLResultKind.Integer,      Description = "形の数（基本の形を除く）")]
    [PLResult("names",         PLResultKind.TextArray,    Description = "形の名前")]
    [PLResult("namesEnglish",  PLResultKind.TextArray,    Description = "英語名。names と同じ並び")]
    [PLResult("ids",           PLResultKind.IntegerArray, Description = "形の番号。names と同じ並び")]
    [PLResult("panels",        PLResultKind.IntegerArray, Description = "パネル。names と同じ並び")]
    [PLResult("movedVertices", PLResultKind.IntegerArray, Description = "基本の形と位置（UV の形なら UV スロット 0）が違う頂点の数。names と同じ並び")]
    [PLResult("kinds",         PLResultKind.TextArray,    Description = "種類（position＝頂点位置 / uv＝UV スロット 0。PMX の UV モーフ）。names と同じ並び")]
    public class QueryShapesCommand : PanelCommand
    {
        public QueryShapesCommand(int modelIndex) : base(modelIndex) { }
    }

    [PLCommand(Category = "morph", Writes = PLWriteScope.None, NoUi = true,
        Description = "表示中の頂点位置（頂点位置 ＋ 試し表示・モーション再生の足し込み WorkingPositions）を返す。足し込みを持つ描画オブジェクトの一覧も返す。モデルは変えない。")]
    [PLResult("meshesWithWorking", PLResultKind.IntegerArray, Description = "足し込みを持つ描画オブジェクトの masterIndex")]
    [PLResult("hasWorking",        PLResultKind.Integer,      Description = "masterIndex の描画オブジェクトが足し込みを持つか（0 / 1）")]
    [PLResult("positions",         PLResultKind.NumberArray,  Description = "vertexIndices の表示位置。x,y,z の順に 3 個ずつ")]
    [PLResult("offsets",           PLResultKind.NumberArray,  Description = "vertexIndices の足し込み。x,y,z の順に 3 個ずつ")]
    public class QueryDisplayedPositionsCommand : PanelCommand
    {
        [PLParam(Description = "読む描画オブジェクトの masterIndex", Required = true,
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int MasterIndex { get; }
        [PLParam(Description = "読む頂点番号")]
        public int[] VertexIndices { get; }
        public QueryDisplayedPositionsCommand(int modelIndex, int masterIndex, int[] vertexIndices = null)
            : base(modelIndex) { MasterIndex = masterIndex; VertexIndices = vertexIndices ?? System.Array.Empty<int>(); }
    }

    [PLCommand(Category = "morph", Writes = PLWriteScope.None, NoUi = true,
        Description = "形の一覧と頂点の控えが決まりどおりかを点検する。issues が 0 なら正常。")]
    [PLResult("issues",           PLResultKind.Integer, Description = "直すべきものの合計（redundantEntries は入れない）")]
    [PLResult("currentMissing",   PLResultKind.Integer, Description = "今の形の番号が一覧に無い（0 か 1）")]
    [PLResult("badIds",           PLResultKind.Integer, Description = "番号の重複・0 以下の番号の件数")]
    [PLResult("badNames",         PLResultKind.Integer, Description = "名前の重複・空の名前の件数")]
    [PLResult("unknownEntries",   PLResultKind.Integer, Description = "一覧に無い番号の控えの件数")]
    [PLResult("forbiddenEntries", PLResultKind.Integer, Description = "持ってはいけない控えの件数")]
    [PLResult("duplicateEntries", PLResultKind.Integer, Description = "同じ頂点に同じ番号の控えが複数ある件数")]
    [PLResult("redundantEntries", PLResultKind.Integer, Description = "基本の形と同じ位置の控えの件数（害は無い）")]
    [PLResult("mirrorMismatch",   PLResultKind.Integer, Description = "ミラー側の形の控えが実体側の鏡映と合わない頂点の数")]
    [PLResult("modifierOutputEntries", PLResultKind.Integer, Description = "モディファイアの出力が持っている形の控えの数（出力は形を持たない決まり）")]
    public class QueryShapeAuditCommand : PanelCommand
    {
        public QueryShapeAuditCommand(int modelIndex) : base(modelIndex) { }
    }
}
