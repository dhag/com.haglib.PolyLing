// PanelCommand.Morph.cs
// モーフ（変換・プレビュー・全選択／全解除・差分からの生成）の操作要求。
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
    // モーフ
    // ================================================================

    [PLCommand(Description = "描画オブジェクトをモーフへ変換し、指定した親の下へ入れる。")]
    public class ConvertMeshToMorphCommand : PanelCommand
    {
        /// <summary>
        /// PMX のモーフパネルの種類数（0=眉 / 1=目 / 2=口 / 3=その他）。
        /// MeshContext.MorphPanel の定義がこれと同じで、パネル側の選択肢
        /// （MeshListSubPanel の panelLabels）も 4 項目で対応する。
        /// Panel を持つ他のコマンドもこの const を参照する。
        /// </summary>
        public const int MorphPanelCount = 4;

        [PLParam(TextKey = "MeshToMorphSourceIndex",
                 Description = "モーフへ変換する描画オブジェクトの masterIndex", Required = true)]
        public int SourceIndex { get; }

        [PLParam(TextKey = "MeshToMorphParentIndex",
                 Description = "モーフを付けるベースメッシュの masterIndex。-1 で未指定", Required = true)]
        public int ParentIndex { get; }

        [PLParam(TextKey = "MeshToMorphName",
                 Description = "生成するモーフの名前", Required = true)]
        public string MorphName { get; }

        [PLParam(TextKey = "MeshToMorphPanel",
                 Description = "モーフパネル。0=眉, 1=目, 2=口, 3=その他",
                 Min = 0, Max = MorphPanelCount - 1, Required = true)]
        public int Panel { get; }
        public ConvertMeshToMorphCommand(int modelIndex, int sourceIndex, int parentIndex, string morphName, int panel)
            : base(modelIndex) { SourceIndex = sourceIndex; ParentIndex = parentIndex; MorphName = morphName; Panel = panel; }
    }

    [PLCommand(Description = "モーフを描画オブジェクトへ戻す。")]
    public class ConvertMorphToMeshCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public ConvertMorphToMeshCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    [PLCommand(Description = "指定したモーフをまとめた表示グループを作る。")]
    public class CreateMorphSetCommand : PanelCommand
    {
        [PLParam(TextKey = "MorphSetName",
                 Description = "作成するモーフセットの名前", Required = true)]
        public string SetName { get; }

        [PLParam(TextKey = "MorphSetType",
                 Description = "PMX のモーフ種別コード。1 = 頂点、3 = グループ", Required = true)]
        public int MorphType { get; }

        [PLParam(TextKey = "MorphSetIndices",
                 Description = "セットに含めるモーフの索引", Required = true)]
        public int[] MorphIndices { get; }
        public CreateMorphSetCommand(int modelIndex, string setName, int morphType, int[] morphIndices)
            : base(modelIndex) { SetName = setName; MorphType = morphType; MorphIndices = morphIndices; }
    }

    // ================================================================
    // モーフプレビュー
    // ================================================================

    [PLCommand(Description = "指定モーフの試し表示を始める。確定するまで頂点は元へ戻せる。")]
    public class StartMorphPreviewCommand : PanelCommand
    {
        [PLParam(TextKey = "PreviewMorphIndices",
                 Description = "プレビューするモーフの索引", Required = true)]
        public int[] MorphIndices { get; }
        public StartMorphPreviewCommand(int modelIndex, int[] morphIndices)
            : base(modelIndex) { MorphIndices = morphIndices; }
    }

    [PLCommand(Description = "試し表示中のモーフの効き具合を変える。")]
    public class ApplyMorphPreviewCommand : PanelCommand
    {
        [PLParam(TextKey = "MorphPreviewWeight",
                 Description = "プレビューに掛けるモーフのウェイト",
                 LimitKey = "MorphPreview.Weight", Required = true)]
        public float Weight { get; }
        public ApplyMorphPreviewCommand(int modelIndex, float weight)
            : base(modelIndex) { Weight = weight; }
    }

    [PLCommand(Description = "モーフの試し表示を終え、その時点の形で確定する。")]
    public class EndMorphPreviewCommand : PanelCommand
    {
        public EndMorphPreviewCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // モーフ全選択/全解除
    // ================================================================

    [PLCommand(Description = "モーフをすべて選択する。")]
    public class SelectAllMorphsCommand : PanelCommand
    {
        [PLParam(TextKey = "AllMorphIndices",
                 Description = "全選択の対象となるモーフの索引", Required = true)]
        public int[] AllMorphIndices { get; }
        public SelectAllMorphsCommand(int modelIndex, int[] allMorphIndices)
            : base(modelIndex) { AllMorphIndices = allMorphIndices; }
    }

    [PLCommand(Description = "モーフの選択をすべて解除する。")]
    public class DeselectAllMorphsCommand : PanelCommand
    {
        public DeselectAllMorphsCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // 差分からのモーフ生成
    // ================================================================

    /// <summary>
    /// 基準モデルとモーフモデルの差分から頂点モーフを生成し、
    /// 基準モデルに MorphExpression として登録する。
    /// Undo 記録付き。
    /// </summary>
    [PLCommand(Description = "基準モデルとモーフモデルの差分から頂点モーフを生成し、 基準モデルに MorphExpression として登録する。")]
    public class CreateMorphFromDiffCommand : PanelCommand
    {
        /// <summary>基準モデルのインデックス（プロジェクト内）</summary>
        [PLParam(TextKey = "MorphDiffBaseModelIndex",
                 Description = "基準モデルの索引", Required = true)]
        public int    BaseModelIndex  { get; }

        /// <summary>モーフモデルのインデックス（プロジェクト内）</summary>
        [PLParam(TextKey = "MorphDiffModelIndex",
                 Description = "差分を取るモーフモデルの索引", Required = true)]
        public int    MorphModelIndex { get; }

        /// <summary>生成するモーフの名前</summary>
        [PLParam(TextKey = "MorphDiffName",
                 Description = "生成するモーフの名前", Required = true)]
        public string MorphName       { get; }

        /// <summary>パネル番号（0=眉 / 1=目 / 2=口 / 3=その他）</summary>
        [PLParam(TextKey = "MorphDiffPanel",
                 Description = "モーフパネル。0=眉, 1=目, 2=口, 3=その他",
                 Min = 0, Max = ConvertMeshToMorphCommand.MorphPanelCount - 1, Required = true)]
        public int    Panel            { get; }

        public CreateMorphFromDiffCommand(
            int baseModelIndex, int morphModelIndex,
            string morphName, int panel)
            : base(baseModelIndex)
        {
            BaseModelIndex  = baseModelIndex;
            MorphModelIndex = morphModelIndex;
            MorphName       = morphName;
            Panel           = panel;
        }
    }
}
