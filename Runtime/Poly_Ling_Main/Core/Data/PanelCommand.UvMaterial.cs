// PanelCommand.UvMaterial.cs
// UV（操作・編集・展開）とマテリアルリストの操作要求。
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
    // UV操作
    // ================================================================

    /// <summary>選択メッシュに投影UV展開を適用する</summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.Targets, Description = "選択したメッシュへ投影による UV 展開を入れる。")]
    public class ApplyUvUnwrapCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "UvUnwrapProjection",
                 Description = "UV の投影方式", Required = true)]
        public ProjectionType Projection { get; }

        [PLParam(TextKey = "UvUnwrapScale",
                 Description = "投影した UV の拡大率",
                 LimitKey = "UvUnwrap.Scale", Required = true)]
        public float Scale { get; }

        [PLParam(TextKey = "UvUnwrapOffsetU",
                 Description = "U 方向のオフセット",
                 LimitKey = "UvUnwrap.Offset", Required = true)]
        public float OffsetU { get; }

        [PLParam(TextKey = "UvUnwrapOffsetV",
                 Description = "V 方向のオフセット",
                 LimitKey = "UvUnwrap.Offset", Required = true)]
        public float OffsetV { get; }

        [PLParam(Description = "Projection が View のときに投影するビュー。Perspective（メイン画面）/ Top / Front / Side。既定は Perspective")]
        public ViewportKind View { get; }

        [PLParam(Description = "Projection が View のときに UV の 0〜1 にするもの。Underlay（そのビューに表示中の下絵画像の矩形）/ Viewport（ビュー全体）/ Bounds（投影した頂点の範囲）。既定は Underlay。U は画面の右、V は画面の上が正。Scale・Offset はこの後に掛かる")]
        public ViewProjectionFrame ViewFrame { get; }

        public ApplyUvUnwrapCommand(int modelIndex, int[] masterIndices,
            ProjectionType projection, float scale, float offsetU, float offsetV,
            ViewportKind view = ViewportKind.Perspective,
            ViewProjectionFrame viewFrame = ViewProjectionFrame.Underlay)
            : base(modelIndex)
        {
            MasterIndices = masterIndices;
            Projection = projection;
            Scale = scale;
            OffsetU = offsetU;
            OffsetV = offsetV;
            View = view;
            ViewFrame = viewFrame;
        }
    }

    /// <summary>UV→XYZ展開メッシュを新規生成してリストに追加する</summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.AddOnly, Description = "UV を XYZ に展開したメッシュを新しく作り、一覧へ足す。")]
    public class UvToXyzCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "UvZUvScale",
                 Description = "UV を XY へ写すときの拡大率",
                 LimitKey = "UvZ.UvScale", Required = true)]
        public float UvScale { get; }

        [PLParam(TextKey = "UvZDepthScale",
                 Description = "カメラ深度を Z へ写すときの拡大率",
                 LimitKey = "UvZ.DepthScale", Required = true)]
        public float DepthScale { get; }

        [PLParam(TextKey = "UvZCameraPosition",
                 Description = "深度の基準に使うカメラ位置", Required = true)]
        public Vector3 CameraPosition { get; }

        [PLParam(TextKey = "UvZCameraForward",
                 Description = "深度の基準に使うカメラの前方向", Required = true)]
        public Vector3 CameraForward { get; }

        public UvToXyzCommand(int modelIndex, int masterIndex,
            float uvScale, float depthScale, Vector3 cameraPosition, Vector3 cameraForward)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            UvScale = uvScale;
            DepthScale = depthScale;
            CameraPosition = cameraPosition;
            CameraForward = cameraForward;
        }
    }

    /// <summary>ソースメッシュのXYZ座標をターゲットメッシュのUVに書き戻す</summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.Targets, Description = "元メッシュの XYZ 座標を、対象メッシュの UV へ書き戻す。")]
    public class XyzToUvCommand : PanelCommand
    {
        [PLParam(TextKey = "XyzToUvSourceMasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "XYZ を読む描画オブジェクトの masterIndex", Required = true)]
        public int SourceMasterIndex { get; }

        [PLParam(TextKey = "XyzToUvTargetMasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "UV を書き戻す描画オブジェクトの masterIndex", Required = true)]
        public int TargetMasterIndex { get; }

        [PLParam(TextKey = "UvZUvScale",
                 Description = "XY を UV へ戻すときの拡大率",
                 LimitKey = "UvZ.UvScale", Required = true)]
        public float UvScale { get; }

        public XyzToUvCommand(int modelIndex, int sourceMasterIndex, int targetMasterIndex, float uvScale)
            : base(modelIndex)
        {
            SourceMasterIndex = sourceMasterIndex;
            TargetMasterIndex = targetMasterIndex;
            UvScale = uvScale;
        }
    }

    // ================================================================
    // UV 編集
    // ================================================================

    /// <summary>
    /// 指定 MeshContext の UV 座標変更をコマンドとして記録する。
    /// ドラッグ移動・一括変換の両方に使用する。
    /// </summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.Targets, Description = "指定オブジェクトの UV 座標を書き換える。変更前後の値を渡すので元へ戻せる。")]
    public class ApplyUVChangesCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "MasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int       MasterIndex   { get; }

        /// <summary>変更対象の頂点インデックス配列</summary>
        [PLParam(TextKey = "UvChangeVertexIndices",
                 Description = "UV を変える頂点の索引", Required = true)]
        public int[]     VertexIndices { get; }

        /// <summary>変更対象の UV サブインデックス配列（VertexIndices と同長）</summary>
        [PLParam(TextKey = "UvChangeUVIndices",
                 Description = "変更する UV のサブ索引。VertexIndices と同じ長さ", Required = true)]
        public int[]     UVIndices     { get; }

        /// <summary>変更前 UV 座標配列</summary>
        [PLParam(TextKey = "UvChangeBeforeUVs",
                 Description = "変更前の UV 座標", Required = true)]
        public Vector2[] BeforeUVs     { get; }

        /// <summary>変更後 UV 座標配列</summary>
        [PLParam(TextKey = "UvChangeAfterUVs",
                 Description = "変更後の UV 座標", Required = true)]
        public Vector2[] AfterUVs      { get; }

        /// <summary>操作名（Undo スタックの説明文用）</summary>
        [PLParam(TextKey = "UvChangeOperationName",
                 Description = "Undo 記録に残す操作名。既定は UV Edit")]
        public string    OperationName { get; }

        public ApplyUVChangesCommand(
            int modelIndex, int masterIndex,
            int[] vertexIndices, int[] uvIndices,
            Vector2[] beforeUVs, Vector2[] afterUVs,
            string operationName = "UV Edit")
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
            VertexIndices = vertexIndices;
            UVIndices     = uvIndices;
            BeforeUVs     = beforeUVs;
            AfterUVs      = afterUVs;
            OperationName = operationName;
        }
    }

    // ================================================================
    // UV 展開
    // ================================================================

    /// <summary>
    /// 選択メッシュに LSCM UV 展開を実行する。
    /// Seam エッジはコマンド発行時点の mc.SelectedEdges から Dispatcher が読み取る。
    /// </summary>
    [PLCommand(Category = "uv", Writes = PLWriteScope.Targets, Description = "選択メッシュに LSCM UV 展開を実行する。")]
    public class ApplyLscmUnwrapCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "MasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int  MasterIndex            { get; }

        /// <summary>バウンダリをシームに含めるか</summary>
        [PLParam(TextKey = "LscmIncludeBoundaryAsSeam",
                 Description = "外周をシームとして扱う", Required = true)]
        public bool IncludeBoundaryAsSeam  { get; }

        /// <summary>最大反復数</summary>
        [PLParam(TextKey = "LscmMaxIterations",
                 Description = "LSCM の反復回数の上限",
                 LimitKey = "LscmUnwrap.MaxIterations", Required = true)]
        public int  MaxIterations          { get; }

        public ApplyLscmUnwrapCommand(int modelIndex, int masterIndex,
            bool includeBoundaryAsSeam, int maxIterations)
            : base(modelIndex)
        {
            MasterIndex           = masterIndex;
            IncludeBoundaryAsSeam = includeBoundaryAsSeam;
            MaxIterations         = maxIterations;
        }
    }

    // ================================================================
    // マテリアルリスト
    // ================================================================

    /// <summary>
    /// 現在のマテリアルスロット（CurrentMaterialIndex）を切り替える。
    /// モデルの AutoSetDefaultMaterials が立っていれば、既定マテリアルの控えも今の並びで取り直す。
    /// </summary>
    [PLCommand(Category = "material", Writes = PLWriteScope.ModelWide, Description = "現在のマテリアルスロットを切り替える（自動控えが有効なら既定マテリアルも取り直す）。")]
    public class SetCurrentMaterialSlotCommand : PanelCommand
    {
        [PLParam(TextKey = "CurrentMaterialSlotIndex", Description = "現在にするマテリアルスロットの番号", Required = true)]
        public int SlotIndex { get; }
        public SetCurrentMaterialSlotCommand(int modelIndex, int slotIndex) : base(modelIndex) { SlotIndex = slotIndex; }
    }

    /// <summary>マテリアルスロットを末尾に追加する</summary>
    [PLCommand(Category = "material", Effects = PLCommandEffect.Material, Writes = PLWriteScope.ModelWide, Description = "マテリアルの枠を末尾へ足す。足した枠の番号（slotIndex）を返す。")]
    [PLResult("slotIndex",     PLResultKind.Integer, Description = "足した枠の番号（setMaterialTexture などに渡す）")]
    [PLResult("materialCount", PLResultKind.Integer, Description = "足したあとの枠の数")]
    public class AddMaterialSlotCommand : PanelCommand
    {
        public AddMaterialSlotCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>指定インデックスのマテリアルスロットを削除する</summary>
    [PLCommand(Category = "material", Writes = PLWriteScope.ModelWide, Description = "指定した番号のマテリアルの枠を消す。")]
    public class RemoveMaterialSlotCommand : PanelCommand
    {
        [PLParam(TextKey = "RemoveMaterialSlotIndex",
                 Description = "削除するマテリアルスロットの番号", Required = true)]
        public int SlotIndex { get; }
        public RemoveMaterialSlotCommand(int modelIndex, int slotIndex)
            : base(modelIndex) { SlotIndex = slotIndex; }
    }

    /// <summary>選択面に指定マテリアルスロットを適用する</summary>
    [PLCommand(Category = "material", Writes = PLWriteScope.Targets, Description = "選択した面へ、指定したマテリアルの枠を割り当てる。")]
    public class ApplyMaterialToFacesCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "MasterIndex", IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Write,
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int   MasterIndex  { get; }

        /// <summary>適用するマテリアルスロット番号</summary>
        [PLParam(TextKey = "ApplyMaterialSlot",
                 Description = "適用するマテリアルスロットの番号", Required = true)]
        public int   MaterialSlot { get; }

        /// <summary>適用対象の面インデックス配列</summary>
        [PLParam(TextKey = "MaterialFaceIndices",
                 Description = "マテリアルを適用する面の索引。空なら対象メッシュの今の選択面")]
        public int[] FaceIndices  { get; }

        public ApplyMaterialToFacesCommand(int modelIndex, int masterIndex,
            int materialSlot, int[] faceIndices)
            : base(modelIndex)
        {
            MasterIndex  = masterIndex;
            MaterialSlot = materialSlot;
            FaceIndices  = faceIndices;
        }
    }

    /// <summary>
    /// マテリアルスロットの基本色を設定する。
    ///
    /// 【なぜ Data と Material の両方を書くか】
    ///   MaterialReference は永続データを Data（MaterialData）側に持ち、
    ///   Material はそこから起こしたキャッシュ（MaterialReference.cs:28-44）。
    ///   Data だけを書くと既に起きている Material が古い色のままで画面に出ず、
    ///   Material だけを書くと保存に乗らない。マテリアル一覧パネルの色スライダーも
    ///   両方へ書いている（PlayerMaterialListSubPanel.cs:610-625）。
    ///   書く先はディスパッチャ側が持つ。
    /// </summary>
    [PLCommand(Category = "material", Effects = PLCommandEffect.Material, Verification = PLCommandVerification.Visual, Writes = PLWriteScope.ModelWide, Description = "マテリアルスロットの基本色を設定する。")]
    public class SetMaterialColorCommand : PanelCommand
    {
        /// <summary>対象マテリアルスロット番号</summary>
        [PLParam(TextKey = "MaterialColorSlotIndex",
                 Description = "色を変えるマテリアルスロットの番号", Required = true)]
        public int   SlotIndex { get; }

        /// <summary>
        /// 設定する基本色。0〜1 の RGBA を 4 要素で持つ。
        ///
        /// Color をそのまま持つとスキーマに出せない
        /// （PanelCommandFactory の対応表に無い型）ため、平たい実数列にしてある。
        /// </summary>
        [PLParam(TextKey = "MaterialBaseColorRgba",
                 Description = "基本色。0〜1 の RGBA を 4 要素で並べる", Required = true)]
        public float[] BaseColorRgba { get; }

        /// <summary>BaseColorRgba から起こした色。受け口はこちらを使う。</summary>
        public Color BaseColor => new Color(
            Rgba(0, 1f), Rgba(1, 1f), Rgba(2, 1f), Rgba(3, 1f));

        private float Rgba(int i, float fallback)
            => (BaseColorRgba != null && i < BaseColorRgba.Length) ? BaseColorRgba[i] : fallback;

        public SetMaterialColorCommand(int modelIndex, int slotIndex, float[] baseColorRgba)
            : base(modelIndex)
        {
            SlotIndex     = slotIndex;
            BaseColorRgba = baseColorRgba ?? System.Array.Empty<float>();
        }

        /// <summary>
        /// Color から RGBA 配列を作る。呼び出し側の書き換えを短くするための補助。
        ///
        /// コンストラクタの多重定義にはしない。PanelCommandFactory.PickConstructor は
        /// 引数の多い方を選ぶだけで、同数のときの順序が決まっていないため、
        /// 外から使われるコンストラクタが不定になる。
        /// </summary>
        public static float[] ToRgba(Color c) => new[] { c.r, c.g, c.b, c.a };
    }

    /// <summary>
    /// マテリアルスロットの名前を変える。
    /// MQO は材質を名前で見分けるため（同じ名前が並ぶとメタセコイアは先頭の 1 つしか出さない）、
    /// モデル内の他の枠と重なる名前は受け付けない。
    /// 書き出しは Material.name を先に見るので、Data と Material の両方を書く（受け口側）。
    /// </summary>
    [PLCommand(Category = "material", Effects = PLCommandEffect.Material, Writes = PLWriteScope.ModelWide, Description = "マテリアルスロットの名前を変える。モデル内の他の枠と同じ名前は付けられない。")]
    public class SetMaterialNameCommand : PanelCommand
    {
        [PLParam(Description = "対象のマテリアルスロットの番号", Required = true)]
        public int SlotIndex { get; }

        [PLParam(Description = "新しい名前（空は不可。他の枠と重ならないこと）", Required = true)]
        public string Name { get; }

        public SetMaterialNameCommand(int modelIndex, int slotIndex, string name)
            : base(modelIndex)
        {
            SlotIndex = slotIndex;
            Name      = name ?? "";
        }
    }

    /// <summary>
    /// マテリアルスロットのシェーダーを差し替える。色・メインテクスチャ・不透明／半透明は引き継ぐ。
    /// 処理は MaterialEditOps.ApplyShader（操作経路統一計画.md H-3b）。
    /// </summary>
    [PLCommand(Category = "material", Writes = PLWriteScope.ModelWide, Description = "マテリアルスロットのシェーダーを差し替える。色・テクスチャ・不透明／半透明は引き継ぐ。")]
    public class SetMaterialShaderCommand : PanelCommand
    {
        [PLParam(Description = "対象のマテリアルスロットの番号", Required = true)]
        public int SlotIndex { get; }

        [PLParam(Description = "シェーダーの種別。Custom のときは customShaderName で名前を指定する", Required = true)]
        public Poly_Ling.Materials.ShaderType ShaderType { get; }

        [PLParam(Description = "ShaderType が Custom のときのシェーダー名（Shader.Find で探す）")]
        public string CustomShaderName { get; }

        public SetMaterialShaderCommand(int modelIndex, int slotIndex,
            Poly_Ling.Materials.ShaderType shaderType, string customShaderName = "")
            : base(modelIndex)
        {
            SlotIndex        = slotIndex;
            ShaderType       = shaderType;
            CustomShaderName = customShaderName ?? "";
        }
    }

    /// <summary>
    /// マテリアルスロットの実数パラメータ（Metallic・Smoothness）を設定する。
    /// 処理は MaterialEditOps.ApplyScalar（操作経路統一計画.md H-3）。
    /// </summary>
    [PLCommand(Category = "material", Writes = PLWriteScope.ModelWide, Description = "マテリアルスロットの Metallic または Smoothness を設定する。")]
    public class SetMaterialScalarCommand : PanelCommand
    {
        [PLParam(Description = "対象のマテリアルスロットの番号", Required = true)]
        public int SlotIndex { get; }

        [PLParam(Description = "設定するパラメータ。Metallic / Smoothness", Required = true)]
        public Poly_Ling.Materials.MaterialScalarKind Kind { get; }

        [PLParam(Description = "値（0〜1）", Required = true)]
        public float Value { get; }

        public SetMaterialScalarCommand(int modelIndex, int slotIndex,
            Poly_Ling.Materials.MaterialScalarKind kind, float value)
            : base(modelIndex)
        {
            SlotIndex = slotIndex;
            Kind      = kind;
            Value     = value;
        }
    }

    /// <summary>
    /// マテリアルスロットの発光を設定する。発光色 × 強さを HDR の発光色として書く。
    /// 強さを 1 より大きくすると、ブルーム（setViewportBloom）のしきい値を超えて滲む。
    /// 処理は MaterialEditOps.ApplyEmission。発光の欄を持たないシェーダー（Unlit など）では失敗する。
    /// </summary>
    [PLCommand(Category = "material", Effects = PLCommandEffect.Material, Verification = PLCommandVerification.Visual, Writes = PLWriteScope.ModelWide,
        Description = "マテリアルスロットの発光を設定する。発光色 × 強さを発光色として書き、保存に乗る。強さを 1 より大きくするとブルーム（setViewportBloom）で滲む。発光の欄を持たないシェーダー（Unlit など）では失敗する。")]
    public class SetMaterialEmissionCommand : PanelCommand
    {
        [PLParam(Description = "対象のマテリアルスロットの番号", Required = true)]
        public int SlotIndex { get; }

        [PLParam(Description = "true で発光させる、false で発光を切る", Required = true)]
        public bool Enabled { get; }

        [PLParam(Description = "発光色。0〜1 の RGB を 3 要素で並べる")]
        public float[] ColorRgb { get; }

        [PLParam(Description = "強さ（発光色に掛ける倍率）", Min = 0)]
        public float Intensity { get; }

        public SetMaterialEmissionCommand(int modelIndex, int slotIndex, bool enabled,
            float[] colorRgb = null, float intensity = 1f)
            : base(modelIndex)
        {
            SlotIndex = slotIndex;
            Enabled   = enabled;
            ColorRgb  = (colorRgb != null && colorRgb.Length > 0) ? colorRgb : new[] { 1f, 1f, 1f };
            Intensity = intensity;
        }

        /// <summary>発光色 × 強さ（HDR）。</summary>
        public Color HdrColor => new Color(
            (ColorRgb.Length > 0 ? ColorRgb[0] : 1f) * Intensity,
            (ColorRgb.Length > 1 ? ColorRgb[1] : 1f) * Intensity,
            (ColorRgb.Length > 2 ? ColorRgb[2] : 1f) * Intensity,
            1f);
    }

    /// <summary>
    /// マテリアルスロットを不透明／半透明に切り替える。
    /// 処理は MaterialEditOps.ApplySurface（操作経路統一計画.md H-3b）。
    /// </summary>
    [PLCommand(Category = "material", Writes = PLWriteScope.ModelWide, Description = "マテリアルスロットを不透明／半透明に切り替える。")]
    public class SetMaterialSurfaceCommand : PanelCommand
    {
        [PLParam(Description = "対象のマテリアルスロットの番号", Required = true)]
        public int SlotIndex { get; }

        [PLParam(Description = "true で半透明、false で不透明", Required = true)]
        public bool Transparent { get; }

        public SetMaterialSurfaceCommand(int modelIndex, int slotIndex, bool transparent)
            : base(modelIndex)
        {
            SlotIndex   = slotIndex;
            Transparent = transparent;
        }
    }

    /// <summary>
    /// 画像ファイルを読んでマテリアルスロットのテクスチャ欄へ設定する。作業フォルダの下だけを読める
    /// （画面で選んだファイルは PLSandbox.AllowOnceFromDialog で 1 回だけ許可される）。
    /// 処理は MaterialEditOps.ApplyTextureFile（操作経路統一計画.md H-3b）。
    /// </summary>
    [PLCommand(Category = "material", Writes = PLWriteScope.ModelWide, Description = "画像ファイルを読んでマテリアルスロットのテクスチャ欄へ設定する。作業フォルダの下だけを読める。")]
    public class SetMaterialTextureCommand : PanelCommand
    {
        [PLParam(Description = "対象のマテリアルスロットの番号", Required = true)]
        public int SlotIndex { get; }

        [PLParam(Description = "テクスチャ欄のプロパティ名（例 _BaseMap）", Required = true)]
        public string PropertyName { get; }

        [PLParam(Description = "読む画像のパス。作業フォルダからの相対でも絶対でもよい", Required = true)]
        public string FilePath { get; }

        public SetMaterialTextureCommand(int modelIndex, int slotIndex, string propertyName, string filePath)
            : base(modelIndex)
        {
            SlotIndex    = slotIndex;
            PropertyName = propertyName ?? "";
            FilePath     = filePath ?? "";
        }
    }

    // ================================================================
    // UV 調整に合わせたテクスチャの描き直し（計算は UvTextureRemapper）
    // ================================================================

    /// <summary>RemapTextureByUvCommand の写し方。</summary>
    public enum UvTextureRemapMethod
    {
        /// <summary>面ごとのアフィン（四角形は 2 通りの分割の三角形 4 つ、五角形以上は扇形分割の平均）。</summary>
        Affine = 0,

        /// <summary>面ごとに近傍点で解く 2 次元 TPS。解けない面はアフィンへ降格。</summary>
        Tps = 1,
    }

    [PLCommand(Category = "uv", Writes = PLWriteScope.None, NoUi = true,
        Description = "UV 調整前と調整後のオブジェクト（面番号・角番号が一致するもの）から、調整前の UV に合わせて描かれたテクスチャを調整後の UV に合うよう描き直し、PNG に書き出す。写し方は面ごとのアフィン、または近傍点数を指定した 2 次元 TPS。モデルは変えない。")]
    [PLResult("faces",            PLResultKind.Integer, Description = "面の総数")]
    [PLResult("skippedFaces",     PLResultKind.Integer, Description = "角数の不一致・3 角未満・UV 不正で飛ばした面の数")]
    [PLResult("unmappedFaces",    PLResultKind.Integer, Description = "写像が決まらなかった面の数（三角形がすべて潰れているなど）")]
    [PLResult("tpsFaces",         PLResultKind.Integer, Description = "TPS で写した面の数")]
    [PLResult("tpsFallbackFaces", PLResultKind.Integer, Description = "TPS が解けずアフィンへ降格した面の数")]
    [PLResult("controlPoints",    PLResultKind.Integer, Description = "TPS の制御点の数（調整後 UV の位置の重なりを除く）")]
    [PLResult("coveredPixels",    PLResultKind.Integer, Description = "面に覆われた画素の数")]
    [PLResult("paddedPixels",     PLResultKind.Integer, Description = "延ばした画素の数")]
    [PLResult("copiedPixels",     PLResultKind.Integer, Description = "入力をそのまま写した画素の数")]
    [PLResult("invalidPixels",    PLResultKind.Integer, Description = "写像の結果が不正で入力をそのまま写した画素の数")]
    [PLResult("width",            PLResultKind.Integer, Description = "出力の幅")]
    [PLResult("height",           PLResultKind.Integer, Description = "出力の高さ")]
    public class RemapTextureByUvCommand : PanelCommand
    {
        [PLParam(IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "UV 調整前のオブジェクトの masterIndex", Required = true)]
        public int BeforeMasterIndex { get; }

        [PLParam(IsMeshRef = true, MeshRefAccess = PLMeshRefAccess.Read,
                 Description = "UV 調整後のオブジェクトの masterIndex（調整前と面番号・角番号が一致すること）", Required = true)]
        public int AfterMasterIndex { get; }

        [PLParam(Description = "入力画像（調整前の UV に合わせたテクスチャ）のパス。作業フォルダからの相対", Required = true)]
        public string InputPath { get; }

        [PLParam(Description = "出力 PNG のパス。作業フォルダからの相対。大きさは入力と同じ", Required = true)]
        public string OutputPath { get; }

        [PLParam(Description = "写し方。Affine（面ごとのアフィン）または Tps（近傍点数を指定した 2 次元 TPS）")]
        public UvTextureRemapMethod Method { get; }

        [PLParam(Description = "TPS の近傍点数。面の重心（調整後 UV）に近い点をこの数だけ使う。0 で全点を使い 1 回だけ解く", Min = 0, Max = 256, Step = 1)]
        public int NeighborCount { get; }

        [PLParam(Description = "TPS の平滑化係数（画素単位の座標に対する値）。0 で対応点を厳密に通る", Min = 0f, Max = 1000f)]
        public float Lambda { get; }

        [PLParam(Description = "面に覆われない画素を、隣の面の写像で延ばす幅（画素）。それより外は入力をそのまま写す", Min = 0, Max = 64, Step = 1)]
        public int PadPixels { get; }

        public RemapTextureByUvCommand(int modelIndex, int beforeMasterIndex, int afterMasterIndex,
            string inputPath, string outputPath,
            UvTextureRemapMethod method = UvTextureRemapMethod.Affine,
            int neighborCount = 16, float lambda = 0.001f, int padPixels = 4)
            : base(modelIndex)
        {
            BeforeMasterIndex = beforeMasterIndex;
            AfterMasterIndex  = afterMasterIndex;
            InputPath         = inputPath ?? "";
            OutputPath        = outputPath ?? "";
            Method            = method;
            NeighborCount     = neighborCount;
            Lambda            = lambda;
            PadPixels         = padPixels;
        }
    }
}
