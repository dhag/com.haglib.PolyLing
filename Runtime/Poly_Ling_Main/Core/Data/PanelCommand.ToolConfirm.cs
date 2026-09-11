// PanelCommand.ToolConfirm.cs
// ツール操作の確定（ベベル・押し出しのドラッグ、辺トポロジ、面追加、ナイフ）の操作要求。
// Runtime/Poly_Ling_Main/Core/Data/ に配置（PanelCommand.cs と同じ名前空間。PanelCommand.cs から分割）

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Tools.SpringBoneRig;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
{
    // ================================================================
    // ドラッグ確定（ベベル・押し出し）
    //
    // マウス経路は「押した要素 1 つ」と「ドラッグ量」で結果が決まる。
    // コマンドも同じ 2 つを持ち、量は画面座標ではなく対象メッシュの
    // ローカル空間の長さ／ベクトルで指定する。
    //
    // 実処理が編集対象メッシュ 1 本にしか効かないため、MasterIndices は
    // 「1 個で、それが編集対象と一致すること」を要求する。
    // ================================================================

    /// <summary>
    /// 指定した辺をベベルする。実処理は EdgeBevelTool。
    /// </summary>
    [PLCommand(Description = "指定した辺をベベルする。")]
    public class EdgeBevelCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "EdgeBevelV1", Description = "対象の辺の頂点番号 1", Required = true)]
        public int   EdgeV1 { get; }

        [PLParam(TextKey = "EdgeBevelV2", Description = "対象の辺の頂点番号 2", Required = true)]
        public int   EdgeV2 { get; }

        /// <summary>ベベル量。対象メッシュのローカル空間の長さ。</summary>
        [PLParam(TextKey = "EdgeBevelAmount",
                 Description = "ベベル量。対象メッシュのローカル空間の長さ。0 より大きいこと",
                 Required = true)]
        public float Amount   { get; }

        [PLParam(TextKey = "EdgeBevelSegments", Description = "ベベルの分割数")]
        public int   Segments { get; }

        [PLParam(TextKey = "EdgeBevelFillet",
                 Description = "角を弧で結ぶ。false で平坦にする")]
        public bool  Fillet   { get; }

        public EdgeBevelCommand(
            int modelIndex, int[] masterIndices,
            int edgeV1, int edgeV2, float amount,
            int segments      = 1,
            bool fillet       = false,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            EdgeV1        = edgeV1;
            EdgeV2        = edgeV2;
            Amount        = amount;
            Segments      = segments;
            Fillet        = fillet;
        }
    }

    /// <summary>
    /// 指定した辺または線分を押し出す。実処理は EdgeExtrudeTool。
    ///
    /// 辺と線分はどちらか一方だけを指定する。
    /// 辺を指定するときは LineIndex = -1、線分を指定するときは EdgeV1 = EdgeV2 = -1。
    ///
    /// 押し出し量は対象メッシュのローカル空間のベクトル。マウス経路の累積
    /// （EdgeExtrudeTool.cs:296-298）がローカル空間で積まれるのに合わせている。
    /// </summary>
    [PLCommand(Description = "指定した辺または線分を押し出す。")]
    public class EdgeExtrudeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "EdgeExtrudeV1",
                 Description = "対象の辺の頂点番号 1。線分を指定するときは -1")]
        public int     EdgeV1 { get; }

        [PLParam(TextKey = "EdgeExtrudeV2",
                 Description = "対象の辺の頂点番号 2。線分を指定するときは -1")]
        public int     EdgeV2 { get; }

        [PLParam(TextKey = "EdgeExtrudeLineIndex",
                 Description = "対象の線分の索引。辺を指定するときは -1")]
        public int     LineIndex { get; }

        [PLParam(TextKey = "EdgeExtrudeLocalOffset",
                 Description = "押し出し量。対象メッシュのローカル空間のベクトル", Required = true)]
        public Vector3 LocalOffset { get; }

        public EdgeExtrudeCommand(
            int modelIndex, int[] masterIndices,
            int edgeV1, int edgeV2, int lineIndex,
            Vector3 localOffset,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            EdgeV1        = edgeV1;
            EdgeV2        = edgeV2;
            LineIndex     = lineIndex;
            LocalOffset   = localOffset;
        }
    }

    /// <summary>
    /// 指定した面を押し出す。実処理は FaceExtrudeTool。
    /// </summary>
    [PLCommand(Description = "指定した面を押し出す。")]
    public class FaceExtrudeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "FaceExtrudeFaceIndex", Description = "対象の面の索引", Required = true)]
        public int   FaceIndex { get; }

        /// <summary>押し出し距離。対象メッシュのローカル空間の長さ。負値で内側へ。</summary>
        [PLParam(TextKey = "FaceExtrudeDistance",
                 Description = "押し出し距離。対象メッシュのローカル空間の長さ。負値で内側へ",
                 Required = true)]
        public float Distance { get; }

        [PLParam(TextKey = "FaceExtrudeType",
                 Description = "押し出しの種類。Normal / Bevel")]
        public FaceExtrudeSettings.ExtrudeType Type { get; }

        [PLParam(TextKey = "FaceExtrudeBevelScale",
                 Description = "Bevel のときの縮小率。1 で縮小なし")]
        public float BevelScale { get; }

        [PLParam(TextKey = "FaceExtrudeIndividualNormals",
                 Description = "面ごとの法線で押し出す。false で平均法線")]
        public bool  IndividualNormals { get; }

        public FaceExtrudeCommand(
            int modelIndex, int[] masterIndices,
            int faceIndex, float distance,
            FaceExtrudeSettings.ExtrudeType type = FaceExtrudeSettings.ExtrudeType.Normal,
            float bevelScale        = 0.8f,
            bool individualNormals  = false,
            ulong[] objectIds       = null)
            : base(modelIndex)
        {
            MasterIndices     = masterIndices ?? System.Array.Empty<int>();
            ObjectIds         = objectIds;
            FaceIndex         = faceIndex;
            Distance          = distance;
            Type              = type;
            BevelScale        = bevelScale;
            IndividualNormals = individualNormals;
        }
    }

    // ================================================================
    // 辺トポロジ編集（EdgeTopologyTool）
    //
    // 実処理が編集対象メッシュ 1 本にしか効かない（EdgeTopologyTool は
    // ctx.ActiveMeshObject だけを見る）ため、MasterIndices は
    // 「1 個で、それが編集対象と一致すること」を要求する。
    // ================================================================

    /// <summary>
    /// 2 つの三角形が共有する辺を入れ替える（対角線の切り替え）。
    /// 実処理は EdgeTopologyTool の Flip。
    ///
    /// 【型で守れない制約】受け口が実行時に確かめる。
    ///   ・EdgeV1 と EdgeV2 が辺を成すこと
    ///   ・その辺が 2 面に共有され、両側が三角形であること
    /// </summary>
    [PLCommand(Description = "2 つの三角形が共有する辺を入れ替える（対角線の切り替え）。")]
    public class EdgeTopologyFlipCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "EdgeTopologyV1", Description = "辺の頂点 1", Required = true)]
        public int EdgeV1 { get; }

        [PLParam(TextKey = "EdgeTopologyV2", Description = "辺の頂点 2", Required = true)]
        public int EdgeV2 { get; }

        public EdgeTopologyFlipCommand(
            int modelIndex, int[] masterIndices, int edgeV1, int edgeV2,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            EdgeV1        = edgeV1;
            EdgeV2        = edgeV2;
        }
    }

    /// <summary>
    /// 共有辺を消して 2 面を 1 面に結合する。実処理は EdgeTopologyTool の Dissolve。
    ///
    /// 【型で守れない制約】受け口が実行時に確かめる。
    ///   ・EdgeV1 と EdgeV2 が辺を成すこと
    ///   ・その辺が 2 面に共有されていること（境界辺は消せない）
    /// </summary>
    [PLCommand(Description = "共有辺を消して 2 面を 1 面に結合する。")]
    public class EdgeTopologyDissolveCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "EdgeTopologyV1", Description = "辺の頂点 1", Required = true)]
        public int EdgeV1 { get; }

        [PLParam(TextKey = "EdgeTopologyV2", Description = "辺の頂点 2", Required = true)]
        public int EdgeV2 { get; }

        public EdgeTopologyDissolveCommand(
            int modelIndex, int[] masterIndices, int edgeV1, int edgeV2,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            EdgeV1        = edgeV1;
            EdgeV2        = edgeV2;
        }
    }

    /// <summary>
    /// 四角形を対角線で 2 つの三角形に分割する。実処理は EdgeTopologyTool の Split。
    ///
    /// 面番号は載せない。VertexA を含む 4 頂点面を走査して対角が VertexB に
    /// なるものを受け口が解決する（マウス経路と同じ規則）。同じ 2 頂点を対角に
    /// 持つ四角形が複数あるときは最初のものを使う。
    ///
    /// 【型で守れない制約】受け口が実行時に確かめる。
    ///   ・VertexA と VertexB が同一の 4 頂点面の対角であること
    /// </summary>
    [PLCommand(Description = "四角形を対角線で 2 つの三角形に分割する。")]
    public class EdgeTopologySplitCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "EdgeTopologySplitA", Description = "対角の頂点 1", Required = true)]
        public int VertexA { get; }

        [PLParam(TextKey = "EdgeTopologySplitB", Description = "対角の頂点 2", Required = true)]
        public int VertexB { get; }

        public EdgeTopologySplitCommand(
            int modelIndex, int[] masterIndices, int vertexA, int vertexB,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            VertexA       = vertexA;
            VertexB       = vertexB;
        }
    }

    // ================================================================
    // 面追加（AddFaceTool）
    // ================================================================

    /// <summary>
    /// 点列から線分・三角形・四角形を作る。実処理は AddFaceTool.CreateFace。
    ///
    /// 実処理が編集対象メッシュ 1 本にしか効かないため、MasterIndices は
    /// 「1 個で、それが編集対象と一致すること」を要求する。
    ///
    /// 【点の指定】
    ///   PointVertexIndices[i] が 0 以上なら既存頂点を使い、-1 なら
    ///   PointPositions の座標に新しい頂点を作る。座標はメッシュローカル。
    ///
    /// 【ViewPosition】
    ///   AddFaceTool.CreateFace は面法線が視点を向くように巻き順を決める。
    ///   同じ点列でも視点が違えば表裏が変わるので、確定した視点をここに載せる。
    ///   「面の表をこの点へ向ける」と読めばよい。
    ///
    /// 【型で守れない制約】受け口が実行時に確かめる。
    ///   ・PointPositions.Length == PointVertexIndices.Length * 3
    ///   ・点数が 2〜4 で、Mode の要求と整合すること
    ///     （Line は 2、Triangle は 3、Quad は 3 か 4）
    ///   ・既存頂点番号が頂点数の範囲内であること
    /// </summary>
    [PLCommand(Description = "点列から線分・三角形・四角形を作る。")]
    public class AddFaceCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "AddFaceMode",
                 Description = "作るもの。Line(2) / Triangle(3) / Quad(4)", Required = true)]
        public AddFaceMode Mode { get; }

        [PLParam(TextKey = "AddFacePointVertices",
                 Description = "各点の既存頂点番号。新しい頂点を作る点は -1", Required = true)]
        public int[]   PointVertexIndices { get; }

        [PLParam(TextKey = "AddFacePointPositions",
                 Description = "各点のメッシュローカル座標。x,y,z の順に 3 個ずつ並べる。既存頂点の点でも埋めること",
                 Required = true)]
        public float[] PointPositions { get; }

        [PLParam(TextKey = "AddFaceMaterialIndex", Description = "新しい面に付ける材質番号")]
        public int     MaterialIndex { get; }

        [PLParam(TextKey = "AddFaceViewPosition",
                 Description = "面の表を向ける先（ワールド座標）。巻き順の決定に使う",
                 Required = true)]
        public Vector3 ViewPosition { get; }

        public AddFaceCommand(
            int modelIndex, int[] masterIndices,
            AddFaceMode mode,
            int[] pointVertexIndices, float[] pointPositions,
            int materialIndex, Vector3 viewPosition,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices      = masterIndices ?? System.Array.Empty<int>();
            ObjectIds          = objectIds;
            Mode               = mode;
            PointVertexIndices = pointVertexIndices ?? System.Array.Empty<int>();
            PointPositions     = pointPositions ?? System.Array.Empty<float>();
            MaterialIndex      = materialIndex;
            ViewPosition       = viewPosition;
        }
    }

    // ================================================================
    // ナイフ（KnifeTool）
    //
    // 4 モードで確定条件も実処理も違うので、モードごとに別コマンドにする。
    // いずれも編集対象メッシュ 1 本にしか効かない（KnifeTool は
    // ctx.ActiveMeshObject だけを見る）ため、MasterIndices は
    // 「1 個で、それが編集対象と一致すること」を要求する。
    // ================================================================

    /// <summary>
    /// ラダー切断。開始頂点 → セグメント辺 → 終了頂点で切る。
    /// 実処理は LadderCutResolver.Resolve と LadderCutExecutor / NCutExecutor。
    ///
    /// 【型で守れない制約】受け口が実行時に確かめる。
    ///   ・セグメント辺が開始頂点に隣接しないこと
    ///   ・LadderCutResolver.IsSegmentReachable が通ること
    ///   ・Resolve が Ok を返すこと
    /// </summary>
    [PLCommand(Description = "ラダー切断。開始頂点 → セグメント辺 → 終了頂点で切る。")]
    [PLResult("objects",  PLResultKind.Integer, Description = "数えた描画オブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "実行後の頂点数の合計")]
    [PLResult("faces",    PLResultKind.Integer, Description = "実行後の面数の合計")]
    [PLResult("holes",    PLResultKind.Integer, Description = "実行後の境界ループ（穴）の数の合計")]
    public class KnifeLadderCutCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "KnifeStartVertex", Description = "切り始めの既存頂点", Required = true)]
        public int StartVertex { get; }

        [PLParam(TextKey = "KnifeSegmentV1", Description = "代表セグメント辺の頂点 1", Required = true)]
        public int SegmentV1 { get; }

        [PLParam(TextKey = "KnifeSegmentV2", Description = "代表セグメント辺の頂点 2", Required = true)]
        public int SegmentV2 { get; }

        [PLParam(TextKey = "KnifeEndVertex", Description = "切り終わりの既存頂点", Required = true)]
        public int EndVertex { get; }

        [PLParam(TextKey = "KnifeCutRatio",
                 Description = "セグメント辺上の切る位置。SegmentV1 を 0、SegmentV2 を 1 とする比率。既定は 0.5",
                 Min = 0.0, Max = 1.0)]
        public float CutRatio { get; }

        [PLParam(TextKey = "KnifeEqualDivide",
                 Description = "true なら CutRatio を使わず Divisions 等分する。既定は false")]
        public bool  EqualDivide { get; }

        [PLParam(TextKey = "KnifeDivisions",
                 Description = "等分割の分割ピース数。EqualDivide が true のときだけ使う。2 以上",
                 Min = 2)]
        public int   Divisions { get; }

        public KnifeLadderCutCommand(
            int modelIndex, int[] masterIndices,
            int startVertex, int segmentV1, int segmentV2, int endVertex,
            float cutRatio    = 0.5f,
            bool equalDivide  = false,
            int divisions     = 2,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            StartVertex   = startVertex;
            SegmentV1     = segmentV1;
            SegmentV2     = segmentV2;
            EndVertex     = endVertex;
            CutRatio      = cutRatio;
            EqualDivide   = equalDivide;
            Divisions     = divisions;
        }
    }

    /// <summary>
    /// 一意分割。辺を 1 つ指定してベルト／ループ全体を切る。
    /// 実処理は BeltCutResolver.Resolve と LadderCutExecutor / NCutExecutor。
    ///
    /// 【型で守れない制約】受け口が実行時に確かめる。
    ///   ・Resolve が Ok かつ FaceCuts が 1 件以上あること
    /// </summary>
    [PLCommand(Description = "一意分割。辺を 1 つ指定してベルト／ループ全体を切る。")]
    [PLResult("objects",  PLResultKind.Integer, Description = "数えた描画オブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "実行後の頂点数の合計")]
    [PLResult("faces",    PLResultKind.Integer, Description = "実行後の面数の合計")]
    [PLResult("holes",    PLResultKind.Integer, Description = "実行後の境界ループ（穴）の数の合計")]
    public class KnifeBeltLoopCutCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "KnifeBeltEdgeV1", Description = "起点となる辺の頂点 1", Required = true)]
        public int EdgeV1 { get; }

        [PLParam(TextKey = "KnifeBeltEdgeV2", Description = "起点となる辺の頂点 2", Required = true)]
        public int EdgeV2 { get; }

        [PLParam(TextKey = "KnifeCutRatio",
                 Description = "辺上の切る位置。EdgeV1 を 0、EdgeV2 を 1 とする比率。既定は 0.5",
                 Min = 0.0, Max = 1.0)]
        public float CutRatio { get; }

        [PLParam(TextKey = "KnifeEqualDivide",
                 Description = "true なら CutRatio を使わず Divisions 等分する。既定は false")]
        public bool  EqualDivide { get; }

        [PLParam(TextKey = "KnifeDivisions",
                 Description = "等分割の分割ピース数。EqualDivide が true のときだけ使う。2 以上",
                 Min = 2)]
        public int   Divisions { get; }

        public KnifeBeltLoopCutCommand(
            int modelIndex, int[] masterIndices,
            int edgeV1, int edgeV2,
            float cutRatio    = 0.5f,
            bool equalDivide  = false,
            int divisions     = 2,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            EdgeV1        = edgeV1;
            EdgeV2        = edgeV2;
            CutRatio      = cutRatio;
            EqualDivide   = equalDivide;
            Divisions     = divisions;
        }
    }

    /// <summary>
    /// 辺消去。共有辺を消して 2 面を 1 面に統合する。実処理は KnifeTool の MergeFaces。
    ///
    /// EdgeTopologyDissolveCommand と結果は似ているが実装が別で、
    /// こちらは面の巻き順を先頭からたどって合成する。差し替えない。
    ///
    /// 【型で守れない制約】受け口が実行時に確かめる。
    ///   ・2 頂点が辺を成し、ちょうど 2 面に共有されていること
    /// </summary>
    [PLCommand(Description = "辺消去。共有辺を消して 2 面を 1 面に統合する。")]
    [PLResult("objects",  PLResultKind.Integer, Description = "数えた描画オブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "実行後の頂点数の合計")]
    [PLResult("faces",    PLResultKind.Integer, Description = "実行後の面数の合計")]
    [PLResult("holes",    PLResultKind.Integer, Description = "実行後の境界ループ（穴）の数の合計")]
    public class KnifeEraseEdgeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "KnifeEraseEdgeV1", Description = "消す辺の頂点 1", Required = true)]
        public int EdgeV1 { get; }

        [PLParam(TextKey = "KnifeEraseEdgeV2", Description = "消す辺の頂点 2", Required = true)]
        public int EdgeV2 { get; }

        public KnifeEraseEdgeCommand(
            int modelIndex, int[] masterIndices, int edgeV1, int edgeV2,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            EdgeV1        = edgeV1;
            EdgeV2        = edgeV2;
        }
    }

    /// <summary>
    /// シンプル切断。画面上の 2 点を結ぶ直線で切る。実処理は SimpleCutExecutor.Execute。
    ///
    /// 【このコマンドは自己完結しない】
    ///   ScreenP0 / ScreenP1 は「実行時のアクティブビューポート」の座標
    ///   （Y=0 が下・原点が左下）として解釈される。視点やビューポート寸法が
    ///   変われば同じ値でも結果が変わる。
    ///   切る面の判定（カリング）だけは FaceCulledMask で明示できるようにしてある。
    ///
    /// 【型で守れない制約】受け口が実行時に確かめる。
    ///   ・FaceCulledMask は空か、長さが面数と一致すること
    /// </summary>
    [PLCommand(Description = "シンプル切断。画面上の 2 点を結ぶ直線で切る。")]
    [PLResult("objects",  PLResultKind.Integer, Description = "数えた描画オブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "実行後の頂点数の合計")]
    [PLResult("faces",    PLResultKind.Integer, Description = "実行後の面数の合計")]
    [PLResult("holes",    PLResultKind.Integer, Description = "実行後の境界ループ（穴）の数の合計")]
    public class KnifeSimpleCutCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "KnifeSimpleP0",
                 Description = "切断線の 1 点目。実行時のビューポート座標（Y=0 が下）",
                 Required = true)]
        public Vector2 ScreenP0 { get; }

        [PLParam(TextKey = "KnifeSimpleP1",
                 Description = "切断線の 2 点目。実行時のビューポート座標（Y=0 が下）",
                 Required = true)]
        public Vector2 ScreenP1 { get; }

        [PLParam(TextKey = "KnifeSimpleFaceCulledMask",
                 Description = "面ごとに true で「切らない」。長さは面数。空なら全面を対象にする")]
        public bool[] FaceCulledMask { get; }

        [PLParam(TextKey = "KnifeSimpleTriQuad",
                 Description = "5 角以上になった面を三角形と四角形へ分け直す。既定は true")]
        public bool TriQuad { get; }

        public KnifeSimpleCutCommand(
            int modelIndex, int[] masterIndices,
            Vector2 screenP0, Vector2 screenP1,
            bool[] faceCulledMask = null,
            bool triQuad          = true,
            ulong[] objectIds     = null)
            : base(modelIndex)
        {
            MasterIndices  = masterIndices ?? System.Array.Empty<int>();
            ObjectIds      = objectIds;
            ScreenP0       = screenP0;
            ScreenP1       = screenP1;
            FaceCulledMask = faceCulledMask ?? System.Array.Empty<bool>();
            TriQuad        = triQuad;
        }
    }
}
