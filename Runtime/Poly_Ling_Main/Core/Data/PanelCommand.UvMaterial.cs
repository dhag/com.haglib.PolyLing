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
    [PLCommand(Description = "選択メッシュに投影UV展開を適用する</summary>")]
    public class ApplyUvUnwrapCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
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
    [PLCommand(Description = "UV→XYZ展開メッシュを新規生成してリストに追加する</summary>")]
    public class UvToXyzCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
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
    [PLCommand(Description = "ソースメッシュのXYZ座標をターゲットメッシュのUVに書き戻す</summary>")]
    public class XyzToUvCommand : PanelCommand
    {
        [PLParam(TextKey = "XyzToUvSourceMasterIndex",
                 Description = "XYZ を読む描画オブジェクトの masterIndex", Required = true)]
        public int SourceMasterIndex { get; }

        [PLParam(TextKey = "XyzToUvTargetMasterIndex",
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
    [PLCommand(Description = "指定オブジェクトの UV 座標を書き換える。変更前後の値を渡すので元へ戻せる。")]
    public class ApplyUVChangesCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "MasterIndex",
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
    [PLCommand(Description = "選択メッシュに LSCM UV 展開を実行する。")]
    public class ApplyLscmUnwrapCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "MasterIndex",
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

    /// <summary>マテリアルスロットを末尾に追加する</summary>
    [PLCommand(Description = "マテリアルスロットを末尾に追加する</summary>")]
    public class AddMaterialSlotCommand : PanelCommand
    {
        public AddMaterialSlotCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>指定インデックスのマテリアルスロットを削除する</summary>
    [PLCommand(Description = "指定インデックスのマテリアルスロットを削除する</summary>")]
    public class RemoveMaterialSlotCommand : PanelCommand
    {
        [PLParam(TextKey = "RemoveMaterialSlotIndex",
                 Description = "削除するマテリアルスロットの番号", Required = true)]
        public int SlotIndex { get; }
        public RemoveMaterialSlotCommand(int modelIndex, int slotIndex)
            : base(modelIndex) { SlotIndex = slotIndex; }
    }

    /// <summary>選択面に指定マテリアルスロットを適用する</summary>
    [PLCommand(Description = "選択面に指定マテリアルスロットを適用する</summary>")]
    public class ApplyMaterialToFacesCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int   MasterIndex  { get; }

        /// <summary>適用するマテリアルスロット番号</summary>
        [PLParam(TextKey = "ApplyMaterialSlot",
                 Description = "適用するマテリアルスロットの番号", Required = true)]
        public int   MaterialSlot { get; }

        /// <summary>適用対象の面インデックス配列</summary>
        [PLParam(TextKey = "MaterialFaceIndices",
                 Description = "マテリアルを適用する面の索引", Required = true)]
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
    [PLCommand(Description = "マテリアルスロットの基本色を設定する。")]
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
}
