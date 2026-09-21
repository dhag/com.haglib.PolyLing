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

        public ApplyUvUnwrapCommand(int modelIndex, int[] masterIndices,
            ProjectionType projection, float scale, float offsetU, float offsetV)
            : base(modelIndex)
        {
            MasterIndices = masterIndices;
            Projection = projection;
            Scale = scale;
            OffsetU = offsetU;
            OffsetV = offsetV;
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
    [PLCommand(Category = "material", Effects = PLCommandEffect.Material, Writes = PLWriteScope.ModelWide, Description = "マテリアルの枠を末尾へ足す。")]
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
}
