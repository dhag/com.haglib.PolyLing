// PanelCommand.TopologyTargeted.cs
// 位相・頂点編集のうち、対象や生成先の指定を伴う実行系の操作要求。
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
    // 位相・頂点編集（対象や生成先の指定を伴う実行系）
    //
    // 対象の指定・要素の指定・設定値の扱いは上の 2 群と同じ。
    // ここは「参照メッシュを別に指定する」「生成物の置き場を指定する」ものを集める。
    // ================================================================

    /// <summary>
    /// 選択されている頂点・面・線分を削除する。実処理は DeleteSelectionTool。
    /// 対象は選択中の描画オブジェクト全部。
    ///
    /// 面だけを消す DeleteFacesCommand と違い、消す要素は各メッシュの Selection が持つ。
    /// </summary>
    [PLCommand(Description = "選択されている頂点・面・線分を削除する。")]
    public class DeleteSelectionCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        public DeleteSelectionCommand(int modelIndex, int[] masterIndices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
        }
    }

    /// <summary>
    /// パイプ状の部品どうしで断面の頂点位置をそろえる。実処理は PipeAlignTool。
    /// 対象は選択中の描画オブジェクト全部。
    ///
    /// PairText / WeightText / TargetText はツール側のパーサ
    /// （PipeAlignOps.ParsePairs / PipeSmoothOps.ParseWeights / ParseTargets）が読む
    /// 書式そのまま。読めなければ受け口が失敗理由を返す。
    /// </summary>
    [PLCommand(Description = "パイプ状の部品どうしで断面の頂点位置をそろえる。")]
    public class PipeAlignCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "PipeAlignMode",
                 Description = "整列の仕方。Auto / Manual / Smooth", Required = true)]
        public PipeAlignMode      Mode      { get; }

        [PLParam(TextKey = "PipeAlignDirection",
                 Description = "書き込む向き。PlusToMinus / MinusToPlus")]
        public PipeAlignDirection Direction { get; }

        [PLParam(TextKey = "PipeAlignEdgeMode",
                 Description = "Smooth のとき端をどう扱うか。Skip / Partial")]
        public PipeSmoothEdgeMode EdgeMode  { get; }

        [PLParam(TextKey = "PipeAlignRingVertexCount",
                 Description = "断面 1 周の頂点数")]
        public int    RingVertexCount { get; }

        [PLParam(TextKey = "PipeAlignCapStart", Description = "始端に蓋をする")]
        public bool   CapStart { get; }

        [PLParam(TextKey = "PipeAlignCapEnd",   Description = "終端に蓋をする")]
        public bool   CapEnd   { get; }

        [PLParam(TextKey = "PipeAlignPairText",
                 Description = "Manual のペア指定。ツールの書式そのまま")]
        public string PairText   { get; }

        [PLParam(TextKey = "PipeAlignWeightText",
                 Description = "Smooth の重み指定。例 \"1,2,4,2,1\"")]
        public string WeightText { get; }

        [PLParam(TextKey = "PipeAlignTargetText",
                 Description = "対象パーツID の指定。例 \"1,3,5\"。空で全部")]
        public string TargetText { get; }

        public PipeAlignCommand(
            int modelIndex, int[] masterIndices,
            PipeAlignMode mode,
            PipeAlignDirection direction = PipeAlignDirection.PlusToMinus,
            PipeSmoothEdgeMode edgeMode  = PipeSmoothEdgeMode.Skip,
            int ringVertexCount          = 0,
            bool capStart                = false,
            bool capEnd                  = false,
            string pairText              = "",
            string weightText            = "",
            string targetText            = "",
            ulong[] objectIds            = null)
            : base(modelIndex)
        {
            MasterIndices   = masterIndices ?? System.Array.Empty<int>();
            ObjectIds       = objectIds;
            Mode            = mode;
            Direction       = direction;
            EdgeMode        = edgeMode;
            RingVertexCount = ringVertexCount;
            CapStart        = capStart;
            CapEnd          = capEnd;
            PairText        = pairText   ?? "";
            WeightText      = weightText ?? "";
            TargetText      = targetText ?? "";
        }
    }

    /// <summary>
    /// 配置済みの部品を原型メッシュの形へ張り直す。実処理は PlaceObjectReshapeTool。
    /// 対象は選択中の描画オブジェクト全部。
    ///
    /// 原型は MeshObject そのものではなく、材料になる描画オブジェクトの
    /// masterIndex 配列で指定する。受け口が MeshObjectAppendOps.Combine で
    /// 並び順どおりに 1 つへ結合する（パネルの「複数チェックで上から結合」と同じ）。
    /// </summary>
    [PLCommand(Description = "配置済みの部品を原型メッシュの形へ張り直す。")]
    public class PlaceObjectReshapeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "PlaceObjectReshapePrototypes",
                 Description = "原型にする描画オブジェクトの masterIndex 配列。並び順に結合する",
                 Required = true)]
        public int[] PrototypeMasterIndices { get; }

        [PLParam(TextKey = "PlaceObjectReshapeMode",
                 Description = "張り直しの方式。Affine / ThinPlateSpline", Required = true)]
        public PlaceObjectReshapeMode Mode { get; }

        [PLParam(TextKey = "PlaceObjectReshapeLambda",
                 Description = "ThinPlateSpline の平滑化の強さ。Affine では読まれない")]
        public float  Lambda     { get; }

        [PLParam(TextKey = "PlaceObjectReshapeTargetText",
                 Description = "対象パーツID の指定。例 \"1,3,5\"。空で全部")]
        public string TargetText { get; }

        public PlaceObjectReshapeCommand(
            int modelIndex, int[] masterIndices,
            int[] prototypeMasterIndices,
            PlaceObjectReshapeMode mode,
            float lambda      = 0f,
            string targetText = "",
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices          = masterIndices ?? System.Array.Empty<int>();
            ObjectIds              = objectIds;
            PrototypeMasterIndices = prototypeMasterIndices ?? System.Array.Empty<int>();
            Mode                   = mode;
            Lambda                 = lambda;
            TargetText             = targetText ?? "";
        }
    }

    /// <summary>
    /// 選択面に厚みを付けて別メッシュとして生成する。実処理は SolidifyTool。
    ///
    /// 実処理が編集対象メッシュ 1 本の選択面しか見ない（SolidifyTool.cs:128, 135）ため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// 生成物の追加は AddGeneratedMeshCommand が担う（ここでは作るところまで）。
    /// </summary>
    [PLCommand(Description = "選択面に厚みを付けて別メッシュとして生成する。")]
    public class SolidifyCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "SolidifyThickness", Description = "付ける厚み")]
        public float  Thickness     { get; }

        [PLParam(TextKey = "SolidifySegmentsFront", Description = "表側の角の分割数")]
        public int    SegmentsFront { get; }

        [PLParam(TextKey = "SolidifySegmentsBack",  Description = "裏側の角の分割数")]
        public int    SegmentsBack  { get; }

        [PLParam(TextKey = "SolidifyEdgeSizeFront", Description = "表側の角の大きさ")]
        public float  EdgeSizeFront { get; }

        [PLParam(TextKey = "SolidifyEdgeSizeBack",  Description = "裏側の角の大きさ")]
        public float  EdgeSizeBack  { get; }

        [PLParam(TextKey = "SolidifyEdgeInward",    Description = "角を内側へ寄せる")]
        public bool   EdgeInward    { get; }

        [PLParam(TextKey = "SolidifyMeshName",      Description = "生成するメッシュの名前")]
        public string MeshName      { get; }

        [PLParam(TextKey = "SolidifyAddToExisting",
                 Description = "既存オブジェクトへ足す。false で新規オブジェクトにする")]
        public bool   AddToExisting { get; }

        /// <summary>
        /// AddToExisting のときの追加先（MeshContextList インデックス）。
        /// -1 は選択オブジェクトリストの先頭。
        /// </summary>
        [PLParam(TextKey = "SolidifyAddTargetIndex",
                 Description = "追加先の masterIndex。-1 で選択オブジェクトの先頭")]
        public int    AddTargetIndex { get; }

        public SolidifyCommand(
            int modelIndex, int[] masterIndices,
            float thickness,
            int segmentsFront    = 0,
            int segmentsBack     = 0,
            float edgeSizeFront  = 0.1f,
            float edgeSizeBack   = 0.1f,
            bool edgeInward      = false,
            string meshName      = "Solidify",
            bool addToExisting   = false,
            int addTargetIndex   = -1,
            ulong[] objectIds    = null)
            : base(modelIndex)
        {
            MasterIndices  = masterIndices ?? System.Array.Empty<int>();
            ObjectIds      = objectIds;
            Thickness      = thickness;
            SegmentsFront  = segmentsFront;
            SegmentsBack   = segmentsBack;
            EdgeSizeFront  = edgeSizeFront;
            EdgeSizeBack   = edgeSizeBack;
            EdgeInward     = edgeInward;
            MeshName       = meshName ?? "Solidify";
            AddToExisting  = addToExisting;
            AddTargetIndex = addTargetIndex;
        }
    }

    /// <summary>
    /// 選択辺を中心線として、ワールド固定幅の帯面を足す。実処理は EdgeRibbonFaceTool。
    ///
    /// 元の辺は変えず、生成した頂点と四角形を末尾へ足すだけ。選択も消さない。
    /// 対象は選択中の描画オブジェクト全部で、各オブジェクトの選択辺を使う。
    /// </summary>
    [PLCommand(Description = "選択辺を中心線として、ワールド固定幅の帯面を足す。")]
    public class EdgeRibbonFaceCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。選択中のものと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(Description = "帯の幅。ワールド単位。0 より大きいこと", Min = 0.000001f)]
        public float WidthWorld { get; }

        [PLParam(Description = "生成物の置き方。追加先モード・材質スロットなど")]
        public PrimitivePlacement Placement { get; }

        public EdgeRibbonFaceCommand(
            int modelIndex, int[] masterIndices,
            float widthWorld  = 0.05f,
            PrimitivePlacement placement = default,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            WidthWorld    = widthWorld;
            Placement     = placement;
        }
    }

    /// <summary>
    /// 選択線分から検出した輪郭ループを押し出してメッシュを作る。
    /// 実処理は LineExtrudeTool + Profile2DExtrudeMeshGenerator。
    ///
    /// 実処理が編集対象メッシュ 1 本の選択線分しか見ないため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// </summary>
    [PLCommand(Description = "選択線分から検出した輪郭ループを押し出してメッシュを作る。")]
    public class LineExtrudeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "LineExtrudeMeshName", Description = "生成するメッシュの名前")]
        public string  MeshName     { get; }

        [PLParam(TextKey = "LineExtrudeAddToCurrent",
                 Description = "編集対象メッシュへ足す。false で新規オブジェクトにする")]
        public bool    AddToCurrent { get; }

        [PLParam(TextKey = "LineExtrudeThickness", Description = "押し出す厚み")]
        public float   Thickness    { get; }

        [PLParam(TextKey = "LineExtrudeScale",     Description = "輪郭の拡大率")]
        public float   Scale        { get; }

        [PLParam(TextKey = "LineExtrudeOffset",    Description = "輪郭の平行移動（XY）")]
        public Vector2 Offset       { get; }

        [PLParam(TextKey = "LineExtrudeFlipY",     Description = "輪郭の Y を反転する")]
        public bool    FlipY        { get; }

        [PLParam(TextKey = "LineExtrudeSegmentsFront", Description = "表側の角の分割数")]
        public int     SegmentsFront { get; }

        [PLParam(TextKey = "LineExtrudeSegmentsBack",  Description = "裏側の角の分割数")]
        public int     SegmentsBack  { get; }

        [PLParam(TextKey = "LineExtrudeEdgeSizeFront", Description = "表側の角の大きさ")]
        public float   EdgeSizeFront { get; }

        [PLParam(TextKey = "LineExtrudeEdgeSizeBack",  Description = "裏側の角の大きさ")]
        public float   EdgeSizeBack  { get; }

        [PLParam(TextKey = "LineExtrudeEdgeInward",    Description = "角を内側へ寄せる")]
        public bool    EdgeInward    { get; }

        public LineExtrudeCommand(
            int modelIndex, int[] masterIndices,
            string meshName      = "LineExtrude",
            bool addToCurrent    = false,
            float thickness      = 0.1f,
            float scale          = 1f,
            Vector2 offset       = default,
            bool flipY           = false,
            int segmentsFront    = 0,
            int segmentsBack     = 0,
            float edgeSizeFront  = 0.1f,
            float edgeSizeBack   = 0.1f,
            bool edgeInward      = false,
            ulong[] objectIds    = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            MeshName      = meshName ?? "LineExtrude";
            AddToCurrent  = addToCurrent;
            Thickness     = thickness;
            Scale         = scale;
            Offset        = offset;
            FlipY         = flipY;
            SegmentsFront = segmentsFront;
            SegmentsBack  = segmentsBack;
            EdgeSizeFront = edgeSizeFront;
            EdgeSizeBack  = edgeSizeBack;
            EdgeInward    = edgeInward;
        }
    }

    /// <summary>
    /// 対象オブジェクトの頂点を、リファレンスオブジェクトの面へ視線方向に張り付ける。
    /// 実処理は SurfaceSnapTool。対象は選択中の描画オブジェクト全部。
    ///
    /// 【1 コマンドに畳んである】
    ///   パネルは「計算 → スライダーで確認 → 決定」の 3 段だが、確定操作は決定の 1 回だけで、
    ///   計算とスライダーは画面上のプレビューでしかない（Undo は ApplyPreview の中の 1 回。
    ///   SurfaceSnapTool.cs:439-453）。よって受け口は計算・スライダー・決定を続けて呼ぶ。
    ///   Slider は最終的な補間量（0 = 動かさない、1 = 完全に張り付く）。
    /// </summary>
    [PLCommand(Description = "対象オブジェクトの頂点を、リファレンスオブジェクトの面へ視線方向に張り付ける。")]
    public class SurfaceSnapCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "SurfaceSnapReferences",
                 Description = "張り付け先にする描画オブジェクトの masterIndex 配列",
                 Required = true)]
        public int[] ReferenceMasterIndices { get; }

        [PLParam(TextKey = "SurfaceSnapCameraKind",
                 Description = "張り付ける向きを決めるカメラ。Current / Perspective / Top / Front など")]
        public SurfaceSnapCameraKind CameraKind { get; }

        [PLParam(TextKey = "SurfaceSnapSelectedVerticesOnly",
                 Description = "選択頂点だけを動かす。false で対象メッシュの全頂点")]
        public bool                SelectedVerticesOnly { get; }

        [PLParam(TextKey = "SurfaceSnapSurfaceOffset",
                 Description = "張り付け先の面からの浮かせ量")]
        public float               SurfaceOffset { get; }

        [PLParam(TextKey = "SurfaceSnapBackface",
                 Description = "裏面を対象にするか。Both / FrontOnly")]
        public SurfaceSnapBackface Backface { get; }

        [PLParam(TextKey = "SurfaceSnapSlider",
                 Description = "補間量。0 = 動かさない、1 = 完全に張り付く。既定は 1",
                 Min = 0.0, Max = 1.0)]
        public float               Slider { get; }

        public SurfaceSnapCommand(
            int modelIndex, int[] masterIndices,
            int[] referenceMasterIndices,
            SurfaceSnapCameraKind cameraKind = SurfaceSnapCameraKind.Current,
            bool selectedVerticesOnly        = false,
            float surfaceOffset              = 0f,
            SurfaceSnapBackface backface     = SurfaceSnapBackface.Both,
            float slider                     = 1f,
            ulong[] objectIds                = null)
            : base(modelIndex)
        {
            MasterIndices          = masterIndices ?? System.Array.Empty<int>();
            ObjectIds              = objectIds;
            ReferenceMasterIndices = referenceMasterIndices ?? System.Array.Empty<int>();
            CameraKind             = cameraKind;
            SelectedVerticesOnly   = selectedVerticesOnly;
            SurfaceOffset          = surfaceOffset;
            Backface               = backface;
            Slider                 = slider;
        }
    }
}
