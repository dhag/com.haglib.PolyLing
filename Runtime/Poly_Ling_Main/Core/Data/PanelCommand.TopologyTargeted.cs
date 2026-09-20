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
    [PLCommand(Writes = PLWriteScope.Targets, Description = "選択されている頂点・面・線分を削除する。")]
    public class DeleteSelectionCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Writes = PLWriteScope.Targets, Description = "パイプ状の部品どうしで断面の頂点位置をそろえる。")]
    public class PipeAlignCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Writes = PLWriteScope.Targets, Description = "配置済みの部品を原型メッシュの形へ張り直す。")]
    public class PlaceObjectReshapeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "PlaceObjectReshapePrototypes", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
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
    [PLCommand(Writes = PLWriteScope.Targets, Description = "選択面に厚みを付けて別メッシュとして生成する。")]
    public class SolidifyCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    ///
    /// 【辺の読み方】
    ///   EdgeSetName が空なら、対象は選択中の描画オブジェクト全部で、各オブジェクトの選択辺を使う。
    ///   指定されていれば、MasterIndices の各オブジェクトが持つその名前のパーツ選択辞書の辺を使う
    ///   （選択とは照合しない）。オブジェクトグループの作り直しはこちらで同じ帯を組む。
    ///   「オブジェクトグループとして残す」が立っていて辞書名が空のときは、
    ///   受け口が選択辺を辞書へ保存し、その名前を控えたコマンドをグループに残す。
    ///
    /// 【梯子タグ】
    ///   分岐の無い開いた連なりの端に、梯子の自動検索（BeltStackDetector）の目印を付ける。
    ///   開始側は 上(+Y)→下、同じなら 手前(-Z)→奥、同じなら 左(-X)→右 で決める。
    /// </summary>
    [PLCommand(Writes = PLWriteScope.Targets, Description = "辺を中心線として、ワールド固定幅の帯面を足す。梯子の開始・終了タグも付けられる。")]
    public class EdgeRibbonFaceCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列。辞書名を省くときは選択中のものと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(Description = "帯の幅。ワールド単位。0 より大きいこと", Min = 0.000001f)]
        public float WidthWorld { get; }

        [PLParam(Description = "生成物の置き方。追加先モード・材質スロットなど")]
        public PrimitivePlacement Placement { get; }

        [PLParam(Description = "分岐の無い開いた連なりの開始端に、開始三角と開始タグ三角を付ける。開始側は上→下、同じなら手前→奥、同じなら左→右")]
        public bool AddStartTag { get; }

        [PLParam(Description = "分岐の無い開いた連なりの終了端に、終了三角を付ける")]
        public bool AddEndTag { get; }

        [PLParam(Description = "辺を読むパーツ選択辞書の名前。空にすると選択中の辺を使う")]
        public string EdgeSetName { get; }

        public EdgeRibbonFaceCommand(
            int modelIndex, int[] masterIndices,
            float widthWorld  = 0.05f,
            PrimitivePlacement placement = default,
            ulong[] objectIds = null,
            bool addStartTag = false,
            bool addEndTag = false,
            string edgeSetName = "")
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            WidthWorld    = widthWorld;
            Placement     = placement;
            AddStartTag   = addStartTag;
            AddEndTag     = addEndTag;
            EdgeSetName   = edgeSetName ?? "";
        }

        /// <summary>辞書名だけを差し替えた写しを作る。</summary>
        public EdgeRibbonFaceCommand WithEdgeSetName(string edgeSetName)
            => new EdgeRibbonFaceCommand(
                ModelIndex, MasterIndices, WidthWorld, Placement, ObjectIds,
                AddStartTag, AddEndTag, edgeSetName);
    }

    /// <summary>
    /// 辺をパイプにする。辺から帯面（開始タグ付き）を作り、その帯を梯子として
    /// 自動検索で取り込んでパイプを作り、帯を隠す。
    ///
    /// 【オブジェクトグループ】
    ///   帯面の生成（EdgeRibbonFaceCommand）とパイプの生成（CreatePipeCommand）を
    ///   それぞれ 1 ステップとして 1 つのグループに残す。作り直しでは帯を作り直してから
    ///   梯子を取り直すので、元の辺を動かせばパイプが追随する。このコマンド自体は残さない。
    ///
    /// 【辺】
    ///   EdgeSetName が空なら選択辺を新しいパーツ選択辞書へ保存して使う
    ///   （対象は選択中の描画オブジェクトと一致すること）。
    /// </summary>
    [PLCommand(Writes = PLWriteScope.Targets, Description = "辺をパイプにする。帯面と開始タグを作って梯子として取り込み、パイプを作って帯を隠す。帯とパイプは 1 つのオブジェクトグループになる。")]
    [PLResult("groupName", PLResultKind.Text,    Description = "作ったオブジェクトグループの名前")]
    [PLResult("ladders",   PLResultKind.Integer, Description = "帯から取り込んだ梯子の本数")]
    [PLResult("edgeSetName", PLResultKind.Text,  Description = "辺を読んだパーツ選択辞書の名前")]
    public class CreateEdgePipeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "辺を持つ描画オブジェクトの masterIndex 配列。辞書名を省くときは選択中のものと一致すること",
                 Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds { get; }

        [PLParam(Description = "帯（梯子）の幅。ワールド単位。0 より大きいこと", Min = 0.000001f)]
        public float WidthWorld { get; }

        [PLParam(Description = "開始三角と開始タグ三角を付ける。梯子の自動検索の起点なので、パイプにするには必須")]
        public bool AddStartTag { get; }

        [PLParam(Description = "終了三角を付ける")]
        public bool AddEndTag { get; }

        [PLParam(Description = "辺を読むパーツ選択辞書の名前。空にすると選択中の辺を新しい辞書へ保存して使う")]
        public string EdgeSetName { get; }

        [PLParam(Description = "パイプの置き方。追加先モードは新規オブジェクトか既存へ追加。姿勢は使わない")]
        public PrimitivePlacement Placement { get; }

        [PLParam(TextKey = "Pipe", Description = "パイプのパラメータ。ピボットは使わない", Required = true)]
        public Poly_Ling.Pipe.PipeParams Params { get; }

        [PLParam(TextKey = "PipeProfile", Description = "断面プロファイル", Required = true)]
        public Vector2[] Profile { get; }

        [PLParam(TextKey = "PipeProfileClosed", Description = "断面を閉ループとして扱う")]
        public bool ProfileClosed { get; }

        [PLParam(TextKey = "BeltOrient", Description = "梯子の向き補正")]
        public Poly_Ling.PrimitiveMesh.BeltOrientOptions Orient { get; }

        [PLParam(TextKey = "BeltSpline", Description = "梯子のスプライン分割")]
        public Poly_Ling.PrimitiveMesh.BeltSplineOptions Spline { get; }

        public CreateEdgePipeCommand(
            int modelIndex, int[] masterIndices,
            float widthWorld,
            bool addStartTag,
            bool addEndTag,
            string edgeSetName,
            PrimitivePlacement placement,
            Poly_Ling.Pipe.PipeParams @params,
            Vector2[] profile,
            bool profileClosed,
            Poly_Ling.PrimitiveMesh.BeltOrientOptions orient,
            Poly_Ling.PrimitiveMesh.BeltSplineOptions spline,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            WidthWorld    = widthWorld;
            AddStartTag   = addStartTag;
            AddEndTag     = addEndTag;
            EdgeSetName   = edgeSetName ?? "";
            Placement     = placement;
            Params        = @params;
            Profile       = profile ?? System.Array.Empty<Vector2>();
            ProfileClosed = profileClosed;
            Orient        = orient;
            Spline        = spline;
        }
    }

    /// <summary>
    /// 頂点へ藤壺。対象の頂点それぞれの位置へ配置元オブジェクトを複製し、
    /// カメラに向けたビルボードとして置く。実処理は VertexBillboardPlaceToolHandler。
    ///
    /// 【頂点の読み方】
    ///   VertexSetName が空なら、対象は選択中の描画オブジェクトで、各オブジェクトの選択頂点を使う。
    ///   指定されていれば、MasterIndices の各オブジェクトが持つその名前のパーツ選択辞書の頂点を使う。
    ///   「オブジェクトグループとして残す」が立っていて辞書名が空のときは、
    ///   受け口が選択頂点を辞書へ保存し、その名前を控えたコマンドをグループに残す。
    ///   以上は Target = Vertices のとき。
    ///
    /// 【ボーン・原点】Target = Bones なら MasterIndices の各ボーンの位置、
    ///   ObjectOrigins なら各描画オブジェクトの原点へ置く（WorldMatrix の平行移動成分。
    ///   マーカー表示と同じ値）。辞書は使わない。位置は MasterIndices だけで決まるので、
    ///   選択とは照合しない（グループの作り直しでもそのまま再現できる）。
    ///
    /// 【向き】ViewDirection / ViewUp（ワールド）から作る。作り直しでもこの値を使うので、
    ///   向きは作ったときのカメラに固定される。
    /// </summary>
    [PLCommand(Writes = PLWriteScope.Targets, Description = "頂点へ藤壺。対象の頂点それぞれへ配置元オブジェクトを複製し、カメラに向けて置く。")]
    public class CreateVertexBillboardPlaceCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "頂点を持つ描画オブジェクトの masterIndex 配列。辞書名を省くときは選択中のものと一致すること",
                 Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds { get; }

        [PLParam(Description = "頂点を読むパーツ選択辞書の名前。空にすると選択中の頂点を使う")]
        public string VertexSetName { get; }

        [PLParam(TextKey = "PlaceSourceIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "配置元オブジェクトの masterIndex 配列", Required = true)]
        public int[] SourceMasterIndices { get; }

        [PLParam(TextKey = "PlaceIncludeChildren", Description = "配置元の子孫も配置元に加える")]
        public bool IncludeChildren { get; }

        [PLParam(TextKey = "PlaceMode", Description = "配置元が複数のときの割り当て方式")]
        public Poly_Ling.PlaceObject.PlaceSourceMode Mode { get; }

        [PLParam(TextKey = "PlaceSeed", Description = "抽選の乱数シード。割り当て方式が Random のときだけ使う")]
        public int RandomSeed { get; }

        [PLParam(Description = "配置元に掛ける倍率。0 より大きいこと", Min = 0.000001f)]
        public float Scale { get; }

        [PLParam(Description = "配置元の +Z をどちらへ向けるか。TowardCamera = カメラ側 / ScreenUp = 画面の上")]
        public Poly_Ling.PlaceObject.BillboardZDirection ZDirection { get; }

        [PLParam(Description = "カメラの視線方向（ワールド）。0 にしないこと", Required = true)]
        public Vector3 ViewDirection { get; }

        [PLParam(Description = "カメラの上方向（ワールド）。視線と平行にしないこと", Required = true)]
        public Vector3 ViewUp { get; }

        [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
        public string MeshName { get; }

        [PLParam(Description = "生成物の置き方。追加先モード・材質スロットなど。姿勢は使わない")]
        public PrimitivePlacement Placement { get; }

        [PLParam(Description = "置く位置の取り方。Vertices = 選択頂点（または辞書） / Bones = MasterIndices のボーンの位置 / ObjectOrigins = MasterIndices の描画オブジェクトの原点")]
        public Poly_Ling.PlaceObject.BillboardPlaceTarget Target { get; }

        public CreateVertexBillboardPlaceCommand(
            int modelIndex, int[] masterIndices,
            string vertexSetName,
            int[] sourceMasterIndices,
            bool includeChildren,
            Poly_Ling.PlaceObject.PlaceSourceMode mode,
            int randomSeed,
            float scale,
            Poly_Ling.PlaceObject.BillboardZDirection zDirection,
            Vector3 viewDirection,
            Vector3 viewUp,
            string meshName,
            PrimitivePlacement placement,
            ulong[] objectIds = null,
            Poly_Ling.PlaceObject.BillboardPlaceTarget target = Poly_Ling.PlaceObject.BillboardPlaceTarget.Vertices)
            : base(modelIndex)
        {
            MasterIndices       = masterIndices ?? System.Array.Empty<int>();
            ObjectIds           = objectIds;
            Target              = target;
            VertexSetName       = vertexSetName ?? "";
            SourceMasterIndices = sourceMasterIndices ?? System.Array.Empty<int>();
            IncludeChildren     = includeChildren;
            Mode                = mode;
            RandomSeed          = randomSeed;
            Scale               = scale;
            ZDirection          = zDirection;
            ViewDirection       = viewDirection;
            ViewUp              = viewUp;
            MeshName            = meshName ?? "";
            Placement           = placement;
        }

        /// <summary>辞書名だけを差し替えた写しを作る。</summary>
        public CreateVertexBillboardPlaceCommand WithVertexSetName(string vertexSetName)
            => new CreateVertexBillboardPlaceCommand(
                ModelIndex, MasterIndices, vertexSetName, SourceMasterIndices, IncludeChildren,
                Mode, RandomSeed, Scale, ZDirection, ViewDirection, ViewUp, MeshName, Placement,
                ObjectIds, Target);
    }

    /// <summary>
    /// 点指定図形（線分：円筒・角柱 / 三角：板 / 四角：板）を編集対象へ足す。
    /// 実処理は PointDefinedToolHandler（組み立ては PointDefinedMeshBuilder）。
    ///
    /// 【点】
    ///   PointVertexIndices が 0 以上の点は、編集対象のその既存頂点をそのまま使う。
    ///   -1 の点は PointPositions（ワールド座標）に新しい頂点を作る。
    ///   三角・四角の辺の両端が既存頂点で、その間の既存の最短経路のエッジ数が
    ///   辺の分割数と一致するときは、経路上の既存頂点をそのまま使う。
    ///
    /// 【ViewDirection】
    ///   カメラの視線方向（ワールド）。表面をカメラ側へ向け、奥行きをこの向きへ付ける。
    ///
    /// 【型で守れない制約】受け口が実行時に確かめる。
    ///   ・点の数が Mode の要求（Line 2 / Triangle 3 / Quad 4）と一致すること
    ///   ・PointPositions.Length == PointVertexIndices.Length * 3
    ///   ・既存頂点番号が編集対象の頂点数の範囲内であること
    /// </summary>
    [PLCommand(Writes = PLWriteScope.Targets, Description = "指定した点から円筒・角柱、三角形・四角形の板を編集対象へ足す。既存頂点を指す点と、分割数が一致する既存の辺列はそのまま共有する。")]
    public class CreatePointDefinedPrimitiveCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "PointDefinedMode",
                 Description = "作るもの。Line（2 点）/ Triangle（3 点）/ Quad（4 点。周回順）", Required = true)]
        public Poly_Ling.PrimitiveMesh.PointPrimitiveMode Mode { get; }

        [PLParam(TextKey = "PointDefinedPointVertices",
                 Description = "各点が使う編集対象の既存頂点番号。新しい頂点を作る点は -1", Required = true)]
        public int[]   PointVertexIndices { get; }

        [PLParam(TextKey = "PointDefinedPointPositions",
                 Description = "各点のワールド座標。x,y,z の順に 3 個ずつ並べる。既存頂点の点でも埋めること",
                 Required = true)]
        public float[] PointPositions { get; }

        [PLParam(TextKey = "PointDefinedViewDirection",
                 Description = "カメラの視線方向（ワールド）。表面をカメラ側へ向け、奥行きをこの向きへ付ける",
                 Required = true)]
        public Vector3 ViewDirection { get; }

        [PLParam(Description = "形のパラメータ。分割数・奥行き・半径など")]
        public Poly_Ling.PrimitiveMesh.PointDefinedParams Params { get; }

        [PLParam(TextKey = "PointDefinedMaterialIndex", Description = "新しい面に付ける材質番号")]
        public int     MaterialIndex { get; }

        public CreatePointDefinedPrimitiveCommand(
            int modelIndex, int[] masterIndices,
            Poly_Ling.PrimitiveMesh.PointPrimitiveMode mode,
            int[] pointVertexIndices, float[] pointPositions,
            Vector3 viewDirection,
            Poly_Ling.PrimitiveMesh.PointDefinedParams @params,
            int materialIndex = 0,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices      = masterIndices ?? System.Array.Empty<int>();
            ObjectIds          = objectIds;
            Mode               = mode;
            PointVertexIndices = pointVertexIndices ?? System.Array.Empty<int>();
            PointPositions     = pointPositions ?? System.Array.Empty<float>();
            ViewDirection      = viewDirection;
            Params             = @params;
            MaterialIndex      = materialIndex;
        }
    }

    /// <summary>
    /// 選択線分から検出した輪郭ループを押し出してメッシュを作る。
    /// 実処理は LineExtrudeTool + Profile2DExtrudeMeshGenerator。
    ///
    /// 実処理が編集対象メッシュ 1 本の選択線分しか見ないため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// </summary>
    [PLCommand(Writes = PLWriteScope.Targets, Description = "選択線分から検出した輪郭ループを押し出してメッシュを作る。")]
    public class LineExtrudeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
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
    [PLCommand(Writes = PLWriteScope.Targets, Description = "対象オブジェクトの頂点を、リファレンスオブジェクトの面へ視線方向に張り付ける。")]
    public class SurfaceSnapCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。省くとズレ照合をしない")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "SurfaceSnapReferences", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
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
