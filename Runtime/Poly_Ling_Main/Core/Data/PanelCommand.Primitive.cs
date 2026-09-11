// PanelCommand.Primitive.cs
// 図形生成（配置指定と、基本図形・高度な図形・機構部品）の操作要求。
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
    // 図形生成
    //
    // 【なぜ図形ごとにコマンドを分けるか】
    //   1本のコマンドに図形種別と汎用の入れ物を持たせると、
    //   何を渡せばよいかがコマンドの型から読めなくなる。
    //   図形ごとに型付きのパラメータを1つだけ持たせると、
    //   コマンドの型からそのまま MCP のツールスキーマを起こせる。
    //
    // 【生成そのもの】
    //   Poly_Ling.PrimitiveMesh.PrimitiveMeshFactory.Build がコマンドから MeshObject を作る。
    //   モデルへの反映（追加先の解決・Undo・再構築）はディスパッチャ側が行う。
    // ================================================================

    /// <summary>
    /// 生成した図形をどこへどんな姿勢で置くか。図形の内容とは独立なのでまとめて持つ。
    ///
    /// 【回転・拡大の焼き込み】
    ///   BakeRotation / BakeScale が立っている成分は頂点へ焼き込む。
    ///   立っていない成分は描画オブジェクトの姿勢（BoneTransform）へ入れる。
    ///   「既存の描画オブジェクトに追加」のときは追加先の姿勢を変えられないので、
    ///   指定にかかわらず両方とも焼き込む（BakeRotationEffective / BakeScaleEffective）。
    /// </summary>
    public struct PrimitivePlacement
    {
        /// <summary>配置位置（ワールド）。</summary>
        [PLParam(TextKey = "PlacePosition", Description = "配置位置（ワールド座標）")]
        public Vector3 WorldPosition;

        /// <summary>配置の回転（度）。</summary>
        [PLParam(TextKey = "PlaceRotation", Description = "配置の回転（度）")]
        public Vector3 PlaceRotation;

        /// <summary>配置の拡大率。</summary>
        [PLParam(TextKey = "PlaceScale", Description = "配置の拡大率")]
        public Vector3 PlaceScale;

        /// <summary>回転を頂点へ焼き込むか。</summary>
        [PLParam(TextKey = "BakeRotation", Description = "回転を頂点へ焼き込む")]
        public bool BakeRotation;

        /// <summary>拡大率を頂点へ焼き込むか。</summary>
        [PLParam(TextKey = "BakeScale", Description = "拡大率を頂点へ焼き込む")]
        public bool BakeScale;

        /// <summary>アーマチュア内で姿勢を無視するか。</summary>
        [PLParam(TextKey = "IgnorePoseInArmature", Description = "アーマチュア内で姿勢を無視する")]
        public bool IgnorePoseInArmature;

        /// <summary>
        /// 追加先モード。
        ///
        /// RebuildRole=TargetMode … オブジェクトグループの作り直しでは、
        /// 新しいオブジェクトを作らず既存の出力先へ中身を書き戻す。
        /// そのときここへ ReplaceExisting が書かれる（PLRebuildRole を参照）。
        /// </summary>
        [PLParam(TextKey = "AddMode", Description = "新規オブジェクト / 既存へ追加 / 新規モデル",
                 RebuildRole = PLRebuildRole.TargetMode, RebuildModeValue = "ReplaceExisting")]
        public Poly_Ling.Player.PrimitiveAddMode AddMode;

        /// <summary>
        /// 「既存へ追加」のときの追加先 MeshContextList インデックス。
        /// -1 なら選択オブジェクトリストの先頭。
        /// </summary>
        [PLParam(TextKey = "AddTargetIndex", Description = "追加先の索引。-1 で選択の先頭",
                 IsMeshRef = true, RebuildRole = PLRebuildRole.TargetIndex)]
        public int AddTargetIndex;

        /// <summary>
        /// 生成面へ割り当てるマテリアルスロット番号。
        /// -1 は「指定しない」で、生成器が入れた値をそのまま使う。
        /// </summary>
        [PLParam(TextKey = "MaterialIndex", Description = "マテリアルスロット番号。-1 で指定しない")]
        public int MaterialIndex;

        /// <summary>同一位置の重複頂点を結合するか。</summary>
        [PLParam(TextKey = "MergeDuplicateVertices", Description = "同一位置の重複頂点を結合する")]
        public bool MergeDuplicateVertices;

        /// <summary>
        /// 生成に使った入力とパラメータをオブジェクトグループとして残すか。既定 false。
        ///
        /// false（＝これまでのやり方）のときは、生成が終わった時点で
        /// 入力もパラメータも残らない。あとから作り直すには同じ操作をやり直す。
        /// true のときは ModelContext.ObjectGroups へ 1 件入り、
        /// ソースを直したあとに作り直せるようになる。
        /// </summary>
        [PLParam(TextKey = "KeepAsGroup",
                 Description = "生成に使った入力とパラメータをオブジェクトグループとして残す")]
        public bool KeepAsGroup;

        public static PrimitivePlacement Default => new PrimitivePlacement
        {
            WorldPosition          = Vector3.zero,
            PlaceRotation          = Vector3.zero,
            PlaceScale             = Vector3.one,
            BakeRotation           = true,
            BakeScale              = true,
            IgnorePoseInArmature   = false,
            AddMode                = Poly_Ling.Player.PrimitiveAddMode.NewObject,
            AddTargetIndex         = -1,
            MaterialIndex          = -1,
            MergeDuplicateVertices = true,
            KeepAsGroup            = false,
        };
    }

    /// <summary>図形生成コマンドの共通部分。</summary>
    [PLResult("objects",  PLResultKind.Integer, Description = "生成後に数えた描画オブジェクトの数")]
    [PLResult("vertices", PLResultKind.Integer, Description = "生成物の頂点数の合計")]
    [PLResult("faces",    PLResultKind.Integer, Description = "生成物の面数の合計")]
    [PLResult("holes",    PLResultKind.Integer, Description = "生成物の境界ループ（穴）の数の合計")]
    public abstract class CreatePrimitiveMeshCommand : PanelCommand
    {
        /// <summary>配置と後処理の指定。</summary>
        [PLParam(TextKey = "PrimitivePlacement", Description = "配置と後処理の指定", Required = true)]
        public PrimitivePlacement Placement { get; }

        /// <summary>図形の識別子。PrimitiveMeshTexts のキーと同じ文字列。</summary>
        [PLParam(Ignore = true)]
        public abstract string ShapeName { get; }

        /// <summary>生成する描画オブジェクトの名前。各図形のパラメータが持つ値を返す。</summary>
        [PLParam(Ignore = true)]
        public abstract string MeshName { get; }

        /// <summary>
        /// プロファイル（断面・輪郭）の取り込み元オブジェクトの索引。-1 = ひも付けなし。
        /// プロファイルを持たない図形では使わない。
        /// </summary>
        [PLParam(TextKey = "ProfileSourceIndex",
                 Description = "プロファイルの取り込み元オブジェクトの索引。-1 でひも付けなし",
                 IsMeshRef = true)]
        public int ProfileSourceIndex { get; }

        /// <summary>
        /// プロファイルをどうやって取り込んだか。
        ///
        /// 梯子の BeltAcquireMethod と同じ考え方。点列を焼き込んだままだと
        /// 取り込み元を直しても出力先は古いままになるので、取り方を控えて
        /// 作り直しのたびに掛け直す。詳しくは ProfileAcquire.cs の注記を参照。
        ///
        /// 生成そのものには使わない（点列はもう載っている）。作り直しでだけ読む。
        /// </summary>
        [PLParam(TextKey = "ProfileAcquireMethod",
                 Description = "プロファイルの取り込み方。作り直しのときに同じ手順を掛け直す")]
        public Poly_Ling.PrimitiveMesh.ProfileAcquireMethod ProfileAcquire { get; }

        /// <summary>実際に回転を焼き込むか。「既存へ追加」は無条件に焼き込む。</summary>
        public bool BakeRotationEffective
            => Placement.BakeRotation
               || Placement.AddMode == Poly_Ling.Player.PrimitiveAddMode.AddToExisting;

        /// <summary>実際に拡大率を焼き込むか。</summary>
        public bool BakeScaleEffective
            => Placement.BakeScale
               || Placement.AddMode == Poly_Ling.Player.PrimitiveAddMode.AddToExisting;

        /// <summary>頂点へ焼き込む回転（度）。焼き込まないときはゼロ。</summary>
        public Vector3 BakedRotation => BakeRotationEffective ? Placement.PlaceRotation : Vector3.zero;

        /// <summary>頂点へ焼き込む拡大率。焼き込まないときは 1。</summary>
        public Vector3 BakedScale => BakeScaleEffective ? Placement.PlaceScale : Vector3.one;

        /// <summary>描画オブジェクトの姿勢へ入れる回転（度）。焼き込んだときはゼロ。</summary>
        public Vector3 PoseRotation => BakeRotationEffective ? Vector3.zero : Placement.PlaceRotation;

        /// <summary>描画オブジェクトの姿勢へ入れる拡大率。焼き込んだときは 1。</summary>
        public Vector3 PoseScale => BakeScaleEffective ? Vector3.one : Placement.PlaceScale;

        /// <summary>
        /// profileSourceIndex / profileAcquire は後から足した引数なので末尾に既定値付きで置く。
        /// 従来の呼び出しはそのまま通り、「取り込み方の記録なし」＝作り直しでは
        /// 控えた点列をそのまま使う扱いになる。
        /// </summary>
        protected CreatePrimitiveMeshCommand(
            int modelIndex, PrimitivePlacement placement,
            int profileSourceIndex = -1,
            Poly_Ling.PrimitiveMesh.ProfileAcquireMethod profileAcquire
                = Poly_Ling.PrimitiveMesh.ProfileAcquireMethod.Baked)
            : base(modelIndex)
        {
            Placement          = placement;
            ProfileSourceIndex = profileSourceIndex;
            ProfileAcquire     = profileAcquire;
        }
    }

    // ── 基本図形 ────────────────────────────────────────────────

    [PLCommand(Description = "直方体を作る。角丸と軸ごとの分割を指定できる。")]
    public sealed class CreateCubeCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Cube", Description = "直方体のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.CubeMeshGenerator.CubeParams Params { get; }

        public override string ShapeName => "Cube";
        public override string MeshName  => Params.MeshName;

        public CreateCubeCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.CubeMeshGenerator.CubeParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "球を作る。")]
    public sealed class CreateSphereCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Sphere", Description = "球のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.SphereMeshGenerator.SphereParams Params { get; }

        public override string ShapeName => "Sphere";
        public override string MeshName  => Params.MeshName;

        public CreateSphereCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.SphereMeshGenerator.SphereParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    /// <summary>
    /// MCP用サンドボックスの円筒。既存の円柱（CreateCylinderCommand）とは
    /// パラメータ構造体から別にしてあり、片方をいじってももう片方は動かない。
    /// </summary>
    [PLCommand(Description = "MCP用サンドボックスの円筒を作る。")]
    public sealed class CreateMcpCylinderCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "McpCylinder", Description = "MCP円筒のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.McpCylinderMeshGenerator.McpCylinderParams Params { get; }

        public override string ShapeName => "McpCylinder";
        public override string MeshName  => Params.MeshName;

        public CreateMcpCylinderCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.McpCylinderMeshGenerator.McpCylinderParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "円柱を作る。")]
    public sealed class CreateCylinderCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Cylinder", Description = "円柱のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.CylinderMeshGenerator.CylinderParams Params { get; }

        public override string ShapeName => "Cylinder";
        public override string MeshName  => Params.MeshName;

        public CreateCylinderCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.CylinderMeshGenerator.CylinderParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "カプセルを作る。")]
    public sealed class CreateCapsuleCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Capsule", Description = "カプセルのパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.CapsuleMeshGenerator.CapsuleParams Params { get; }

        public override string ShapeName => "Capsule";
        public override string MeshName  => Params.MeshName;

        public CreateCapsuleCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.CapsuleMeshGenerator.CapsuleParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "平面を作る。")]
    public sealed class CreatePlaneCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Plane", Description = "平面のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.PlaneMeshGenerator.PlaneParams Params { get; }

        public override string ShapeName => "Plane";
        public override string MeshName  => Params.MeshName;

        public CreatePlaneCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.PlaneMeshGenerator.PlaneParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "角錐を作る。")]
    public sealed class CreatePyramidCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Pyramid", Description = "角錐のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.PyramidMeshGenerator.PyramidParams Params { get; }

        public override string ShapeName => "Pyramid";
        public override string MeshName  => Params.MeshName;

        public CreatePyramidCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.PyramidMeshGenerator.PyramidParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "角丸の長円柱（スタジアム形）を作る。")]
    public sealed class CreateStadiumBoxCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "StadiumBox", Description = "小判型のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.StadiumBoxMeshGenerator.StadiumBoxParams Params { get; }

        public override string ShapeName => "StadiumBox";
        public override string MeshName  => Params.MeshName;

        public CreateStadiumBoxCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.StadiumBoxMeshGenerator.StadiumBoxParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    // ── 高度な図形（パラメータだけで閉じるもの） ──────────────────

    /// <summary>
    /// パイプ接続用小判型（手のひらのもと）。
    /// 長さ X と奥行き Z は指定ではなく、円の個数・半径・矩形部の幅から決まる。
    /// </summary>
    [PLCommand(Description = "パイプ接続用小判型（手のひらのもと）。")]
    public sealed class CreatePipeStadiumCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "PipeStadium", Description = "パイプ接続用小判型のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.PipeStadiumMeshGenerator.PipeStadiumParams Params { get; }

        public override string ShapeName => "PipeStadium";
        public override string MeshName  => Params.MeshName;

        public CreatePipeStadiumCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.PipeStadiumMeshGenerator.PipeStadiumParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    /// <summary>
    /// 髪の房。房 M 個 × 筒 N 本 の独立したチューブを 1 つの描画オブジェクトに入れる。
    /// 筒 1 本が部品 1 個になる（フリル・パイプと同じ扱い）。
    /// </summary>
    [PLCommand(Description = "髪の房。房 M 個 × 筒 N 本 の独立したチューブを 1 つの描画オブジェクトに入れる。")]
    public sealed class CreateHairStrandCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "HairStrand", Description = "髪の房のパラメータ", Required = true)]
        public Poly_Ling.HairStrand.HairStrandParams Params { get; }

        public override string ShapeName => "HairStrand";
        public override string MeshName  => Params.MeshName;

        public CreateHairStrandCommand(
            int modelIndex,
            Poly_Ling.HairStrand.HairStrandParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "多角形の歯を持つ歯車を作る。")]
    public sealed class CreateNGonGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "NGonGear", Description = "多角形歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.NGonGearMeshGenerator.NGonGearParams Params { get; }

        public override string ShapeName => "NGonGear";
        public override string MeshName  => Params.MeshName;

        public CreateNGonGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.NGonGearMeshGenerator.NGonGearParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "多角形の星形を作る。")]
    public sealed class CreateNGonStarCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "NGonStar", Description = "星形のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.NGonStarMeshGenerator.NGonStarParams Params { get; }

        public override string ShapeName => "NGonStar";
        public override string MeshName  => Params.MeshName;

        public CreateNGonStarCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.NGonStarMeshGenerator.NGonStarParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "インボリュート平歯車を作る。")]
    public sealed class CreateInvoluteGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "InvoluteGear", Description = "インボリュート歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.InvoluteTrochoidGearMeshGenerator.InvoluteGearParams Params { get; }

        public override string ShapeName => "InvoluteGear";
        public override string MeshName  => Params.MeshName;

        public CreateInvoluteGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.InvoluteTrochoidGearMeshGenerator.InvoluteGearParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    // ── 機構部品 ────────────────────────────────────────────────
    //
    // 歯車まわりの生成器は Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ にある。
    // どれもパラメータ構造体だけで形が決まるので、コマンドは値を運ぶだけでよい。

    [PLCommand(Description = "はすば歯車を作る。")]
    public sealed class CreateHelicalGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "HelicalGear", Description = "はすば歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.HelicalGearMeshGenerator.HelicalGearParams Params { get; }

        public override string ShapeName => "HelicalGear";
        public override string MeshName  => Params.MeshName;

        public CreateHelicalGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.HelicalGearMeshGenerator.HelicalGearParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "内歯車を作る。")]
    public sealed class CreateInternalGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "InternalGear", Description = "内歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.InternalGearMeshGenerator.InternalGearParams Params { get; }

        public override string ShapeName => "InternalGear";
        public override string MeshName  => Params.MeshName;

        public CreateInternalGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.InternalGearMeshGenerator.InternalGearParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "インボリュートのラック（直線歯）を作る。")]
    public sealed class CreateInvoluteRackCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "InvoluteRack", Description = "ラックのパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.InvoluteRackMeshGenerator.InvoluteRackParams Params { get; }

        public override string ShapeName => "InvoluteRack";
        public override string MeshName  => Params.MeshName;

        public CreateInvoluteRackCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.InvoluteRackMeshGenerator.InvoluteRackParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "はすばのラックを作る。")]
    public sealed class CreateHelicalRackCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "HelicalRack", Description = "はすばラックのパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.HelicalRackMeshGenerator.HelicalRackParams Params { get; }

        public override string ShapeName => "HelicalRack";
        public override string MeshName  => Params.MeshName;

        public CreateHelicalRackCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.HelicalRackMeshGenerator.HelicalRackParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "すぐばかさ歯車を作る。")]
    public sealed class CreateStraightBevelGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "StraightBevelGear", Description = "すぐばかさ歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.StraightBevelGearMeshGenerator.StraightBevelGearParams Params { get; }

        public override string ShapeName => "StraightBevelGear";
        public override string MeshName  => Params.MeshName;

        public CreateStraightBevelGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.StraightBevelGearMeshGenerator.StraightBevelGearParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "まがりばかさ歯車を作る。")]
    public sealed class CreateSpiralBevelGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "SpiralBevelGear", Description = "まがりばかさ歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.SpiralBevelGearMeshGenerator.SpiralBevelGearParams Params { get; }

        public override string ShapeName => "SpiralBevelGear";
        public override string MeshName  => Params.MeshName;

        public CreateSpiralBevelGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.SpiralBevelGearMeshGenerator.SpiralBevelGearParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "円筒ウォームを作る。")]
    public sealed class CreateCylindricalWormCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "CylindricalWorm", Description = "円筒ウォームのパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.CylindricalWormMeshGenerator.CylindricalWormParams Params { get; }

        public override string ShapeName => "CylindricalWorm";
        public override string MeshName  => Params.MeshName;

        public CreateCylindricalWormCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.CylindricalWormMeshGenerator.CylindricalWormParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "ウォームホイールを作る。")]
    public sealed class CreateWormWheelCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "WormWheel", Description = "ウォームホイールのパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.WormWheelMeshGenerator.WormWheelParams Params { get; }

        public override string ShapeName => "WormWheel";
        public override string MeshName  => Params.MeshName;

        public CreateWormWheelCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.WormWheelMeshGenerator.WormWheelParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "リボンの蝶結びを作る。輪・端・結び目を別々に指定できる。")]
    public sealed class CreateRibbonBowCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Ribbon", Description = "リボンのパラメータ", Required = true)]
        public Poly_Ling.Ribbon.RibbonBowParams Params { get; }

        public override string ShapeName => "Ribbon";
        public override string MeshName  => Params.MeshName;

        public CreateRibbonBowCommand(
            int modelIndex,
            Poly_Ling.Ribbon.RibbonBowParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    /// <summary>
    /// 回転体。プロファイル（断面の点列）は RevolutionParams.Profile が持つ。
    /// </summary>
    [PLCommand(Description = "回転体。プロファイル（断面の点列）は RevolutionParams.Profile が持つ。")]
    public sealed class CreateRevolutionCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Revolution", Description = "回転体のパラメータ", Required = true)]
        public Poly_Ling.Revolution.RevolutionParams Params { get; }

        public override string ShapeName => "Revolution";
        public override string MeshName  => Params.MeshName;

        public CreateRevolutionCommand(
            int modelIndex,
            Poly_Ling.Revolution.RevolutionParams @params,
            PrimitivePlacement placement,
            int profileSourceIndex = -1,
            Poly_Ling.PrimitiveMesh.ProfileAcquireMethod profileAcquire
                = Poly_Ling.PrimitiveMesh.ProfileAcquireMethod.Baked)
            : base(modelIndex, placement, profileSourceIndex, profileAcquire) { Params = @params; }
    }

    /// <summary>
    /// 2D 押し出し。ループ（輪郭の点列）は Profile2DParams.Loops が持つ。
    /// </summary>
    [PLCommand(Description = "2D 押し出し。ループ（輪郭の点列）は Profile2DParams.Loops が持つ。")]
    public sealed class CreateProfile2DCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Profile2D", Description = "2D押し出しのパラメータ", Required = true)]
        public Poly_Ling.Profile2DExtrude.Profile2DParams Params { get; }

        public override string ShapeName => "Profile2D";
        public override string MeshName  => Params.MeshName;

        public CreateProfile2DCommand(
            int modelIndex,
            Poly_Ling.Profile2DExtrude.Profile2DParams @params,
            PrimitivePlacement placement,
            int profileSourceIndex = -1,
            Poly_Ling.PrimitiveMesh.ProfileAcquireMethod profileAcquire
                = Poly_Ling.PrimitiveMesh.ProfileAcquireMethod.Baked)
            : base(modelIndex, placement, profileSourceIndex, profileAcquire) { Params = @params; }
    }

    [PLCommand(Description = "文字列からメッシュを作る。フォントの輪郭を押し出す。")]
    public sealed class CreateTextMeshCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Text", Description = "文字のパラメータ", Required = true)]
        public Poly_Ling.GlyphText.TextMeshParams Params { get; }

        public override string ShapeName => "Text";
        public override string MeshName  => Params.MeshName;

        public CreateTextMeshCommand(
            int modelIndex,
            Poly_Ling.GlyphText.TextMeshParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }

    [PLCommand(Description = "能面のメッシュを作る。")]
    public sealed class CreateNohMaskCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "NohMask", Description = "面（能面）のパラメータ", Required = true)]
        public Poly_Ling.NohMask.FaceMeshParams Params { get; }

        public override string ShapeName => "NohMask";
        public override string MeshName  => Params.MeshName;

        public CreateNohMaskCommand(
            int modelIndex,
            Poly_Ling.NohMask.FaceMeshParams @params,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = @params; }
    }
}
