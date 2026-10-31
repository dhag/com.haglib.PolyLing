// PanelCommand.Clipboard.cs
// 選択部分のコピー・カット・貼り付け・その場で複製の操作要求。
// 実体は SelectionClipboardOps（Core/Ops）、受け口は PlayerCommandDispatcher.Clipboard.cs。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間。PanelCommand.cs から分割）

using Poly_Ling.Ops;

namespace Poly_Ling.Data
{
    /// <summary>
    /// 選択部分のコピー（Ctrl+C）。選択中の描画オブジェクトの選択（頂点・辺・線分・面）を
    /// プロジェクトのクリップボードへ写す。モデルは変えない。
    /// </summary>
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.None, Description =
        "選択中の描画オブジェクトで選んでいる頂点・辺・線分・面をクリップボードへ写す。"
        + "面・線分は面が使う頂点ごと、辺は両端の頂点を写す。モデルは変えない。クリップボードはプロジェクトに 1 つで、前の内容は置き換わる。")]
    [PLUiRoute("選択部分のコピー", "leftPane.fold.Topology", "leftPane.copySelectionBtn", Note = "ショートカットは Ctrl+C")]
    [PLResult("objects",  PLResultKind.Integer, Description = "写したオブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "写した頂点の数")]
    [PLResult("faces",    PLResultKind.Integer, Description = "写した面の数（線分を含む）")]
    public class CopySelectionCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[] MasterIndices { get; }

        public CopySelectionCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices ?? System.Array.Empty<int>(); }
    }

    /// <summary>
    /// 選択部分のカット（Ctrl+X）。コピーしてから選択削除（deleteSelection と同じ範囲）を行う。
    /// Undo 1 回で戻る（コピーの内容はクリップボードに残る）。
    /// </summary>
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.Targets,
        Effects = PLCommandEffect.Topology | PLCommandEffect.VertexOrder,
        Hazards = PLCommandHazard.InvalidatesSkinWeights | PLCommandHazard.InvalidatesMorphs | PLCommandHazard.ChangesVertexOrder,
        Description =
        "選択中の描画オブジェクトで選んでいる頂点・辺・線分・面をクリップボードへ写してから、選択の頂点・面・線分を削除する。"
        + "削除の範囲は deleteSelection と同じ（辺だけの選択は消さない）。Undo 1 回で戻る。")]
    [PLUiRoute("選択部分のカット", "leftPane.fold.Topology", "leftPane.cutSelectionBtn", Note = "ショートカットは Ctrl+X")]
    [PLResult("objects",  PLResultKind.Integer, Description = "写したオブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "写した頂点の数")]
    [PLResult("faces",    PLResultKind.Integer, Description = "写した面の数（線分を含む）")]
    public class CutSelectionCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[] MasterIndices { get; }

        public CutSelectionCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices ?? System.Array.Empty<int>(); }
    }

    /// <summary>
    /// クリップボードの貼り付け（Ctrl+V / Ctrl+Shift+V）。
    /// ActiveObject … 編集対象（または targetMasterIndex）の末尾へ足す。ミラーの相手にも鏡像を足す。
    /// NewObject    … 写した部分ごとに新しいオブジェクトを作る。
    /// 貼った部分が選ばれた状態になる。Undo 1 回で戻る。
    /// </summary>
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.ModelWide,
        Effects = PLCommandEffect.Topology | PLCommandEffect.CreatesObject,
        Description =
        "クリップボードの頂点・面を貼る。位置はモデル空間で写したときと同じ場所に重なる。"
        + "destination が ActiveObject なら貼り付け先のオブジェクトの末尾へ足し（ミラーの相手にも鏡像を足す）、NewObject なら写した元のオブジェクトごとに新しいオブジェクトを作る。"
        + "別のモデルへ貼るとボーンウェイトを外し、マテリアル番号を 0 にする。貼った部分が選ばれた状態になる。")]
    [PLUiRoute("選択部分の貼り付け", "leftPane.fold.Topology", "leftPane.pasteDestinationDropdown", "leftPane.pasteSelectionBtn",
        Note = "ショートカットは Ctrl+V（編集対象へ）・Ctrl+Shift+V（新規オブジェクト）")]
    [PLResult("vertices",       PLResultKind.Integer,      Description = "貼った頂点の数")]
    [PLResult("faces",          PLResultKind.Integer,      Description = "貼った面の数（線分を含む）")]
    [PLResult("objects",        PLResultKind.Integer,      Description = "作ったオブジェクトの数（ActiveObject では 0）")]
    [PLResult("weightsRemoved", PLResultKind.Integer,      Description = "ボーンウェイトを外した頂点の数")]
    [PLResult("verticesWithoutWeight", PLResultKind.Integer, Description = "スキンドの貼り付け先へ、ウェイトを持たずに入った頂点の数")]
    [PLResult("mirrorsUpdated", PLResultKind.Integer,      Description = "鏡像を足したミラー側の数")]
    [PLResult("masterIndices",  PLResultKind.IntegerArray, Description = "貼り付け先または作ったオブジェクトの masterIndex", Optional = true)]
    public class PasteSelectionCommand : PanelCommand
    {
        [PLParam(Description = "貼り付け先。ActiveObject = 貼り付け先のオブジェクトへ足す / NewObject = 新しいオブジェクトを作る")]
        public SelectionPasteDestination Destination { get; }

        [PLParam(TextKey = "TargetMasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "ActiveObject のときの貼り付け先の masterIndex。省くと編集対象。NewObject では使わない")]
        public int TargetMasterIndex { get; }

        public PasteSelectionCommand(int modelIndex,
            SelectionPasteDestination destination = SelectionPasteDestination.ActiveObject,
            int targetMasterIndex = -1)
            : base(modelIndex)
        {
            Destination       = destination;
            TargetMasterIndex = targetMasterIndex;
        }
    }

    /// <summary>
    /// その場で複製（Shift+D）。選択中の描画オブジェクトごとに、選択部分を同じオブジェクトの末尾へ複製する。
    /// 複製した部分が選ばれた状態になる。クリップボードは使わない。Undo 1 回で戻る。
    /// </summary>
    [PLCommand(Category = "geometry.topology", Writes = PLWriteScope.Targets,
        Effects = PLCommandEffect.Topology,
        Description =
        "選択中の描画オブジェクトごとに、選んでいる頂点・辺・線分・面を同じオブジェクトの末尾へ同じ位置に複製し、複製した部分を選んだ状態にする。"
        + "ミラーの相手にも鏡像を足す。クリップボードは変えない。")]
    [PLUiRoute("その場で複製", "leftPane.fold.Topology", Note = "ショートカットは Shift+D（複製後は頂点移動ツールになる）")]
    [PLResult("objects",  PLResultKind.Integer, Description = "複製したオブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "複製した頂点の数")]
    [PLResult("faces",    PLResultKind.Integer, Description = "複製した面の数（線分を含む）")]
    [PLResult("mirrorsUpdated", PLResultKind.Integer, Description = "鏡像を足したミラー側の数")]
    public class DuplicateInPlaceCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[] MasterIndices { get; }

        public DuplicateInPlaceCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices ?? System.Array.Empty<int>(); }
    }
}
