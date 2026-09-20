// PanelCommand.TopologyQuery.cs
// 位相（境界辺・境界頂点・面）を座標で絞って返す照会。モデルは変えない。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間）
//
// 【何のためにあるか】
//   穴つなぎ・面削除・穴の頂点数合わせは、頂点／辺／面の番号で対象を指す。
//   番号は生成順に依存するので、手順の記録（手本）に焼くと
//   作り方を少し変えただけで別の場所を掴む
//   （PlayerRobotBuildTestSubPanel.Stages.cs:38-41 の注記）。
//   ここが「そのときの実データから番号を引き直す」役をする。
//
// 【選ぶのは呼ぶ側】
//   どれを使うかは目的で決まる（前縁か後縁か、上蓋か側面か）。
//   ここは条件で絞った候補を並べて返すだけで、1 つに決めない。
//   決めるのは人か AI。手本には決め方を Instruction の段で書く。
//
// 【量のあるものを返す例外】
//   候補の配列は量がある。通常は結果辞書へ書いて件数だけ返す約束だが、
//   これは次の段の引数に入れるためのものなので、getRawData と同じ扱いで
//   値そのものを返す。新しいコマンドをこの仲間に増やさないこと。

namespace Poly_Ling.Data
{
    /// <summary>座標のどちら端で絞るか。</summary>
    public enum ExtremeAxis
    {
        /// <summary>絞らない。全部返す。</summary>
        None = 0,
        MinX = 1, MaxX = 2,
        MinY = 3, MaxY = 4,
        MinZ = 5, MaxZ = 6,
    }

    /// <summary>境界辺（穴の縁）を座標で絞って返す。モデルは変えない。</summary>
    [PLCommand(Writes = PLWriteScope.None, Description = "境界辺（穴の縁）を並べて返す。atExtreme でどちらか一端の辺だけに絞れる。どれを使うかは呼ぶ側が中点の座標を見て決める。モデルは変えない。")]
    [PLResult("count", PLResultKind.Integer,     Description = "返した境界辺の数")]
    [PLResult("v1",    PLResultKind.IntegerArray, Description = "辺の片側の頂点索引", Optional = true)]
    [PLResult("v2",    PLResultKind.IntegerArray, Description = "辺のもう片側の頂点索引。v1 と同じ並び", Optional = true)]
    [PLResult("midX",  PLResultKind.NumberArray, Description = "辺の中点の x。v1 と同じ並び", Optional = true)]
    [PLResult("midY",  PLResultKind.NumberArray, Description = "辺の中点の y。v1 と同じ並び", Optional = true)]
    [PLResult("midZ",  PLResultKind.NumberArray, Description = "辺の中点の z。v1 と同じ並び", Optional = true)]
    public class QueryBoundaryEdgesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex。省くと現在の編集対象",
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int MasterIndex { get; }

        [PLParam(Description = "どちらか一端の辺だけに絞る。None なら全部")]
        public ExtremeAxis AtExtreme { get; }

        [PLParam(Description = "一端とみなす許容差[m]", Min = 0.0)]
        public float Tolerance { get; }

        public QueryBoundaryEdgesCommand(
            int modelIndex, int masterIndex = -1,
            ExtremeAxis atExtreme = ExtremeAxis.None, float tolerance = 1e-4f)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            AtExtreme   = atExtreme;
            Tolerance   = tolerance;
        }
    }

    /// <summary>面を軸並行の箱で絞って返す。モデルは変えない。</summary>
    [PLCommand(Writes = PLWriteScope.None, Description = "面の中心が指定した箱の中に入っている面を並べて返す。deleteFaces へそのまま渡せる。モデルは変えない。")]
    [PLResult("count",      PLResultKind.Integer,      Description = "箱に入った面の数")]
    [PLResult("faceIndices", PLResultKind.IntegerArray, Description = "面の索引。deleteFaces の faceIndices へ渡す", Optional = true)]
    [PLResult("centerX",    PLResultKind.NumberArray,  Description = "面の中心の x。faceIndices と同じ並び", Optional = true)]
    [PLResult("centerY",    PLResultKind.NumberArray,  Description = "面の中心の y。faceIndices と同じ並び", Optional = true)]
    [PLResult("centerZ",    PLResultKind.NumberArray,  Description = "面の中心の z。faceIndices と同じ並び", Optional = true)]
    public class QueryFacesInBoxCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex。省くと現在の編集対象",
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int MasterIndex { get; }

        [PLParam(Description = "箱の最小側の角（ローカル座標）", Required = true)]
        public UnityEngine.Vector3 Min { get; }

        [PLParam(Description = "箱の最大側の角（ローカル座標）", Required = true)]
        public UnityEngine.Vector3 Max { get; }

        public QueryFacesInBoxCommand(
            int modelIndex, UnityEngine.Vector3 min, UnityEngine.Vector3 max, int masterIndex = -1)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            Min         = min;
            Max         = max;
        }
    }

    /// <summary>相手に最も近い境界頂点を返す。モデルは変えない。</summary>
    [PLCommand(Writes = PLWriteScope.None, Description = "相手の描画オブジェクト（または指定した点）に最も近い境界頂点を返す。穴つなぎ・穴の頂点数合わせが穴を指すのに使う種頂点。モデルは変えない。")]
    [PLResult("found",       PLResultKind.Flag,        Description = "引けたか")]
    [PLResult("vertexIndex", PLResultKind.Integer,     Description = "境界頂点の索引。引けなかったときは -1")]
    [PLResult("distance",    PLResultKind.Number,      Description = "相手までのワールド距離[m]")]
    [PLResult("position",    PLResultKind.NumberArray, Description = "その頂点のワールド座標。x,y,z の 3 つ", Optional = true)]
    public class QueryNearestBoundaryVertexCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "境界頂点を探す側の masterIndex。省くと現在の編集対象",
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int MasterIndex { get; }

        [PLParam(Description = "近さを測る相手の masterIndex。-1 なら targetPoint を使う",
                 IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read)]
        public int TargetMasterIndex { get; }

        [PLParam(Description = "近さを測る相手の点（ワールド座標）。targetMasterIndex が -1 のときだけ使う")]
        public UnityEngine.Vector3 TargetPoint { get; }

        public QueryNearestBoundaryVertexCommand(
            int modelIndex, int masterIndex = -1, int targetMasterIndex = -1,
            UnityEngine.Vector3 targetPoint = default)
            : base(modelIndex)
        {
            MasterIndex       = masterIndex;
            TargetMasterIndex = targetMasterIndex;
            TargetPoint       = targetPoint;
        }
    }
}
