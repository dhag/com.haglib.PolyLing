// PanelCommand.Detach.cs
// 頂点の分離（DetachVerticesOps）の要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 実体は Viewer（PolyLingPlayerViewerCore.Detach.cs）。Undo は対象ごとのスナップショット 1 件。

using Poly_Ling.Ops;

namespace Poly_Ling.Data
{
    /// <summary>切れ目の辺に沿って頂点を分け、面どうしのつながりを切る。</summary>
    [PLCommand(Category = "geometry.topology", Effects = PLCommandEffect.Topology,
        Hazards = PLCommandHazard.MaySplitVertices | PLCommandHazard.InvalidatesMorphs,
        Verification = PLCommandVerification.VertexCount | PLCommandVerification.UVSeams,
        Preconditions = PLCommandPrecondition.RequiresSelection, Writes = PLWriteScope.Targets,
        Description = "頂点の分離。faces モードは選択面を周りの面から切り離し、edges モードは選択辺に沿って切り開く。切れ目の辺の端の頂点を、切れ目でない辺でつながる面の扇形ごとに複製して分ける（UV・法線スロット・ウェイトも写す）。面の並び・Id と線分は変えない。UV の作業空間の代理に使うと、反映で元の UV の継ぎ目になる。")]
    [PLResult("newVertices", PLResultKind.Integer, Description = "増えた頂点の数（全対象の合計）")]
    [PLResult("objects",     PLResultKind.Integer, Description = "頂点が増えた描画オブジェクトの数")]
    public sealed class DetachVerticesCommand : PanelCommand
    {
        [PLParam(IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列。各対象の選択（面モードは選択面、辺モードは選択辺）を使う",
                 Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(Description = "切れ目の決め方。Faces（選択面を切り離す）/ Edges（選択辺で切り開く）", Required = true)]
        public DetachMode Mode { get; }

        [PLParam(Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds { get; }

        public DetachVerticesCommand(int modelIndex, int[] masterIndices, DetachMode mode, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            Mode          = mode;
            ObjectIds     = objectIds;
        }
    }
}
