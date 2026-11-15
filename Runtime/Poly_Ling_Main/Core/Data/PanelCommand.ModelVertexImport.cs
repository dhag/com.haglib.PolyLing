// PanelCommand.ModelVertexImport.cs
// 2 つのモデルの間で、オブジェクト 1 組ずつ頂点位置を写す操作要求と、2 モデル表示の開閉。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 書き込みの決まりは Ops/ModelVertexImportOps.cs の冒頭注記。
// 画面では「2 モデル間の頂点取り込み」のパネル（PlayerTwoModelSubPanel）で、
// 一方のリストの行をもう一方のリストの行へドラッグして落とすと 1 組ずつ送る。

using Poly_Ling.Ops;

namespace Poly_Ling.Data
{
    [PLCommand(Category = "morph", Effects = PLCommandEffect.VertexPosition | PLCommandEffect.Morph | PLCommandEffect.CreatesObject,
        Verification = PLCommandVerification.Visual, Writes = PLWriteScope.Targets,
        Description = "別のモデル（modelIndex）のオブジェクトの頂点位置を、写し先のモデル（targetModelIndex）のオブジェクトへ写す。sourceMeshIndices[i] → targetMeshIndices[i] の組ごと。"
                    + "書き込み先は target で選ぶ（CurrentShape＝今の形／BaseShape＝基本の形／NamedShape＝shapeName の形。無ければ作る）。今の形は切り替えない。名前の形へ書くと、書いたオブジェクトの頂点だけがその形で変わる。"
                    + "頂点の対応は matchMode（Index＝頂点番号で頂点数が同じ組だけ／VertexId＝頂点 ID）。頂点数が合わない組は書かずに mismatched に返す。"
                    + "appendIfMismatch が true で書き込み先が今の形なら、合わない組と targetMeshIndices が -1 の組は、写し元のオブジェクトを複製して写し先の一覧の末尾に足す（ボーンウェイトはボーン名、材質は名前で付け直す。形は持ち込まない）。"
                    + "基本の形へ書くときは keepShapeOffsets に従い形の差分を保つ。Undo は 1 件。",
        Tags = "差し替え,モーフ取り込み,形,2モデル,書き戻し")]
    [PLUiManual("2 モデル間の頂点取り込みのパネルで、一方の列の行をもう一方の列の行へドラッグして落とす",
        "leftPane.fold.VertexTopo", "leftPane.twoModelBtn", "twoModel.modelA", "twoModel.modelB",
        "twoModel.targetA", "twoModel.targetB",
        Note = "落とした先の列の書き込み先（今の形・基本の形・形の名前・新しい形）に書く。A の行を B の列の空いた所へ落とすと末尾に足す")]
    [PLResult("pairs",          PLResultKind.Integer,   Description = "書いた組の数（末尾へ足したものを除く）")]
    [PLResult("transferred",    PLResultKind.Integer,   Description = "位置が変わった頂点の数")]
    [PLResult("shape",          PLResultKind.Text,      Description = "書いた形の名前。今の形が基本の形・基本の形へ書いたときは空")]
    [PLResult("shapeCreated",   PLResultKind.Integer,   Description = "形を新しく作ったら 1")]
    [PLResult("appended",       PLResultKind.TextArray, Description = "末尾へ足したオブジェクトの名前", Optional = true)]
    [PLResult("mismatched",     PLResultKind.TextArray, Description = "頂点数が合わず書かなかった組（写し元(数) / 写し先(数)）", Optional = true)]
    [PLResult("weightDropped",  PLResultKind.Integer,   Description = "末尾へ足したオブジェクトで、ボーン名で引けずウェイトを外した頂点の数")]
    [PLResult("materialsAdded", PLResultKind.Integer,   Description = "末尾へ足すために写し先へ足した材質の数")]
    [PLResult("shiftedEntries", PLResultKind.Integer,   Description = "基本の形へ書いて差分を保つために動かした形の控えの数")]
    [PLResult("droppedEntries", PLResultKind.Integer,   Description = "基本の形へ書いて（差分を保たない）、基本の形と同じになって消した控えの数")]
    [PLResult("masterIndices",  PLResultKind.IntegerArray, Description = "書いた・足した写し先のオブジェクトの索引", Optional = true)]
    public class ImportModelVerticesCommand : PanelCommand
    {
        /// <summary>写し元のモデル（PanelCommand.ModelIndex）。</summary>
        public int SourceModelIndex => ModelIndex;

        [PLParam(Description = "写し先のモデルの索引", Required = true)]
        public int TargetModelIndex { get; }

        [PLParam(IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "写し元のオブジェクトの索引。targetMeshIndices と同じ長さ", Required = true)]
        public int[] SourceMeshIndices { get; }

        [PLParam(IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write, MeshRefModelKey = "TargetModelIndex",
                 Description = "写し先のオブジェクトの索引。-1 は末尾へ足す（appendIfMismatch が true のときだけ）", Required = true)]
        public int[] TargetMeshIndices { get; }

        [PLParam(Description = "書き込み先の形（CurrentShape / BaseShape / NamedShape）")]
        public ModelVertexTarget Target { get; }

        [PLParam(Description = "target が NamedShape のときの形の名前。無ければ作る")]
        public string ShapeName { get; }

        [PLParam(Description = "頂点の対応（Index＝頂点番号 / VertexId＝頂点 ID）")]
        public VertexMatchMode MatchMode { get; }

        [PLParam(Description = "基本の形へ書くとき、形の差分を保つ（全部の形を同じだけ動かす）")]
        public bool KeepShapeOffsets { get; }

        [PLParam(Description = "頂点数が合わない組を、写し元の複製として写し先の末尾に足す（書き込み先が今の形のときだけ）")]
        public bool AppendIfMismatch { get; }

        public ImportModelVerticesCommand(int sourceModelIndex, int targetModelIndex,
                                          int[] sourceMeshIndices, int[] targetMeshIndices,
                                          ModelVertexTarget target = ModelVertexTarget.CurrentShape, string shapeName = "",
                                          VertexMatchMode matchMode = VertexMatchMode.Index,
                                          bool keepShapeOffsets = true, bool appendIfMismatch = false)
            : base(sourceModelIndex)
        {
            TargetModelIndex  = targetModelIndex;
            SourceMeshIndices = sourceMeshIndices ?? new int[0];
            TargetMeshIndices = targetMeshIndices ?? new int[0];
            Target            = target;
            ShapeName         = shapeName ?? "";
            MatchMode         = matchMode;
            KeepShapeOffsets  = keepShapeOffsets;
            AppendIfMismatch  = appendIfMismatch;
        }
    }

    [PLCommand(Category = "ui", Writes = PLWriteScope.None,
        Description = "右ペインを 2 モデル表示にする（モデル A＝modelIndex、モデル B＝modelBIndex）。常駐のリストとほかのパネルを隠し、右ペインを広げて、2 つのモデルのオブジェクトの一覧を左（B）右（A）に並べる。閉じるのは closeTwoModelView。")]
    [PLUiRoute("2 モデル表示を開く", "leftPane.fold.VertexTopo", "leftPane.twoModelBtn", "twoModel.modelA", "twoModel.modelB",
        Run = "leftPane.twoModelBtn", Note = "開いてからモデル A・B を選ぶ")]
    [PLUiNoField("開いてからモデル B の欄で選ぶ", "modelBIndex")]
    public class ShowTwoModelViewCommand : PanelCommand
    {
        [PLParam(Description = "モデル B（左の列）の索引。-1 なら今の選びのまま")]
        public int ModelBIndex { get; }

        public ShowTwoModelViewCommand(int modelIndex, int modelBIndex = -1) : base(modelIndex)
        { ModelBIndex = modelBIndex; }
    }

    [PLCommand(Category = "ui", Writes = PLWriteScope.None,
        Description = "2 モデル表示を閉じ、開く前の右ペイン（常駐のリスト・幅）に戻す。")]
    [PLUiRoute("2 モデル表示を閉じる", "twoModel.close", Run = "twoModel.close")]
    public class CloseTwoModelViewCommand : PanelCommand
    {
        public CloseTwoModelViewCommand(int modelIndex) : base(modelIndex) { }
    }
}
