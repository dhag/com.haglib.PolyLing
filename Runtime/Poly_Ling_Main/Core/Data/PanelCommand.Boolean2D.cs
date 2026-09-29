// PanelCommand.Boolean2D.cs
// 同じ平面上にある 2 つのメッシュオブジェクトの 2D ブーリアン演算（和 / 差 / 積 / 排他的論理和）。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 実処理は Boolean2DOps.PerformMeshes（多角形演算は Clipper2）。受け口は
// PlayerCommandDispatcher.Boolean2D.cs。Undo は 3D の booleanMesh と同じくリスト全体を 1 件で記録する。
//
// 【入力の形】source で選ぶ。
//   faces … 3 頂点以上の面。結果は三角形の面（UV は平面の 2D 座標）。
//   lines … 線分（2 頂点の面）の閉じたループ。結果は線分と線分群。
// 2D押し出しの輪郭どうしの演算は図形生成パネル側で行う（Boolean2DOps.PerformProfileLoops）。

using Poly_Ling.Ops;

namespace Poly_Ling.Data
{
    /// <summary>
    /// 同じ平面上の 2 つのメッシュオブジェクトに 2D ブーリアン演算を行う。
    /// 演算は A のローカル空間で行い、結果も A の姿勢を引き継ぐ。
    /// </summary>
    [PLCommand(Category = "geometry.topology", Effects = PLCommandEffect.Topology | PLCommandEffect.CreatesObject,
        Hazards = PLCommandHazard.ChangesVertexOrder | PLCommandHazard.InvalidatesMorphs,
        Verification = PLCommandVerification.Topology | PLCommandVerification.VertexCount | PLCommandVerification.Visual,
        Writes = PLWriteScope.Targets,
        Description = "同じ平面上にある 2 つのメッシュオブジェクトに 2D ブーリアン演算（和 / 差 / 積 / 排他的論理和）を行う。source=faces は 3 頂点以上の面の領域どうしを演算して三角形の面で返し、source=lines は線分の閉じたループが囲む領域どうしを演算して線分で返す。全ての点が同じ平面から planeTolerance 以内に無ければ失敗する。結果を入れたオブジェクトを対象として返す（新規なら追加したもの、置き換えなら A）。")]
    [PLResult("vertices", PLResultKind.Integer, Description = "結果の頂点数")]
    [PLResult("faces",    PLResultKind.Integer, Description = "結果の面数（lines では線分の数）")]
    [PLResult("loops",    PLResultKind.Integer, Description = "結果の輪郭の数（外周と穴）")]
    [PLResult("holes",    PLResultKind.Integer, Description = "そのうち穴の数")]
    public class BooleanMesh2DCommand : PanelCommand
    {
        [PLParam(IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write, WriteWhen = "createNewMesh=false",
                 Description = "左辺（基準）オブジェクトの masterIndex。差では削られる側", Required = true)]
        public int AMasterIndex { get; }

        [PLParam(IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write, WriteWhen = "deleteSourceB=true",
                 Description = "右辺オブジェクトの masterIndex。差では削る側", Required = true)]
        public int BMasterIndex { get; }

        [PLParam(Description = "和 / 差 / 積 / 排他的論理和のどれを行うか", Required = true)]
        public Boolean2DOpKind Op { get; }

        [PLParam(Description = "入力にするもの。faces は 3 頂点以上の面、lines は線分の閉じたループ")]
        public Boolean2DSource Source { get; }

        [PLParam(Description = "結果を新規オブジェクトに入れる。false なら A の中身を結果で置き換える")]
        public bool CreateNewMesh { get; }

        [PLParam(Description = "演算後に右辺オブジェクトを削除する")]
        public bool DeleteSourceB { get; }

        [PLParam(Description = "同じ平面とみなす距離の上限（A のローカルの単位）", Min = 0)]
        public float PlaneTolerance { get; }

        public BooleanMesh2DCommand(int modelIndex, int aMasterIndex, int bMasterIndex,
                                    Boolean2DOpKind op, Boolean2DSource source = Boolean2DSource.Faces,
                                    bool createNewMesh = true, bool deleteSourceB = false,
                                    float planeTolerance = Boolean2DOps.DefaultPlaneTolerance)
            : base(modelIndex)
        {
            AMasterIndex   = aMasterIndex;
            BMasterIndex   = bMasterIndex;
            Op             = op;
            Source         = source;
            CreateNewMesh  = createNewMesh;
            DeleteSourceB  = deleteSourceB;
            PlaneTolerance = planeTolerance;
        }
    }
}
