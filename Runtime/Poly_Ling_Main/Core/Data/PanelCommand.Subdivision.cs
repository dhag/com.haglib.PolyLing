// PanelCommand.Subdivision.cs
// サブディビジョン（細分化曲面）のコマンド。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【つながりはオブジェクトグループに残す】
//   SubdivideMeshCommand を実行すると、親（cageMasterIndex）と回数（level）と
//   子（出力先）を 1 項目のグループとして残す。つながりの正本はこれで、
//   実行時の追随（SubdivisionSync）はここから引く。
//
// 【作り直し】
//   targetMasterIndex に既にある子を渡すと、新しく作らず中身だけ入れ替える
//   （RebuildRole=TargetIndex）。グループの作り直しもこの形で流れる。

namespace Poly_Ling.Data
{
    [PLCommand(Category = "geometry.create.derived", Writes = PLWriteScope.Targets,
        Effects = PLCommandEffect.CreatesObject | PLCommandEffect.Topology | PLCommandEffect.Hierarchy,
        Description = "メッシュを親（ケージ）として Catmull-Clark で細分化した滑らかな子を作る。子は親の子に置かれ、親の頂点編集・面の増減・移動に追随する。")]
    [PLUiRoute("サブディビジョン", "leftPane.fold.Topology", "leftPane.subdivisionBtn", "subdivision.level", "subdivision.create")]
    public class SubdivideMeshCommand : PanelCommand
    {
        [PLParam(Description = "細分化する親（ケージ）の masterIndex", Required = true,
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int CageMasterIndex { get; }

        [PLParam(Description = "細分化の回数", Min = 1, Max = 4, Step = 1)]
        public int Level { get; }

        [PLParam(Description = "既にある子の masterIndex。指定すると新しく作らず中身だけ作り直す。-1 で新しく作る",
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write, RebuildRole = PLRebuildRole.TargetIndex)]
        public int TargetMasterIndex { get; }

        public SubdivideMeshCommand(int modelIndex, int cageMasterIndex, int level = 1, int targetMasterIndex = -1)
            : base(modelIndex)
        {
            CageMasterIndex   = cageMasterIndex;
            Level             = level;
            TargetMasterIndex = targetMasterIndex;
        }
    }

    [PLCommand(Category = "object.group", Writes = PLWriteScope.Targets,
        Effects = PLCommandEffect.ObjectAttribute | PLCommandEffect.DeletesObject,
        Description = "サブディビジョンを解除する。親と子のつながりを外し、子を消すか通常のメッシュとして残す。")]
    [PLUiRoute("サブディビジョンの解除", "leftPane.fold.Topology", "leftPane.subdivisionBtn", "subdivision.deleteChild", "subdivision.release")]
    public class ReleaseSubdivisionCommand : PanelCommand
    {
        [PLParam(Description = "親（ケージ）または子の masterIndex", Required = true,
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write)]
        public int MasterIndex { get; }

        [PLParam(Description = "子を消す。false なら子を通常のメッシュとして残す")]
        public bool DeleteChild { get; }

        public ReleaseSubdivisionCommand(int modelIndex, int masterIndex, bool deleteChild = true)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            DeleteChild = deleteChild;
        }
    }
}
