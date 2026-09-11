// PanelCommand.Blend.cs
// モデルブレンド・メッシュブレンドの操作要求。
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
    // モデルブレンド
    // ================================================================

    /// <summary>
    /// パネルオープン時にターゲットモデルのクローンを作成してプロジェクトに追加する。
    /// cloneName が空の場合はメインエディタ側でユニーク名を生成する。
    /// 戻り値としてクローンのモデルインデックスが必要だが PanelCommand は戻り値を持たないため、
    /// ハンドラが NotifyPanels を呼び出したあとパネルは OnViewChanged で新モデル数を検出する。
    /// </summary>
    [PLCommand(Description = "パネルオープン時にターゲットモデルのクローンを作成してプロジェクトに追加する。")]
    public class CreateBlendCloneCommand : PanelCommand
    {
        [PLParam(TextKey = "CloneNameBase",
                 Description = "クローンの名前の基。空で自動採番", Required = true)]
        public string CloneNameBase { get; }
        public CreateBlendCloneCommand(int sourceModelIndex, string cloneNameBase)
            : base(sourceModelIndex) { CloneNameBase = cloneNameBase; }
    }

    /// <summary>ブレンドをクローンモデルに適用する</summary>
    [PLCommand(Description = "ブレンドをクローンモデルに適用する</summary>")]
    public class ApplyModelBlendCommand : PanelCommand
    {
        /// <summary>クローン先モデルインデックス</summary>
        [PLParam(TextKey = "BlendCloneModelIndex",
                 Description = "ブレンド結果を書き込むクローンモデルの索引", Required = true)]
        public int CloneModelIndex { get; }

        [PLParam(TextKey = "BlendWeights",
                 Description = "ブレンド元ごとの重み", Required = true)]
        public float[] Weights     { get; }

        [PLParam(TextKey = "BlendMeshEnabled",
                 Description = "メッシュごとにブレンドへ含めるか", Required = true)]
        public bool[]  MeshEnabled { get; }

        [PLParam(TextKey = "BlendRecalcNormals",
                 Description = "ブレンド後に頂点法線を再計算する", Required = true)]
        public bool    RecalcNormals { get; }

        [PLParam(TextKey = "BlendBones",
                 Description = "ボーンの姿勢もブレンドする", Required = true)]
        public bool    BlendBones  { get; }
        public ApplyModelBlendCommand(
            int sourceModelIndex, int cloneModelIndex,
            float[] weights, bool[] meshEnabled, bool recalcNormals, bool blendBones)
            : base(sourceModelIndex)
        {
            CloneModelIndex = cloneModelIndex;
            Weights      = weights;
            MeshEnabled  = meshEnabled;
            RecalcNormals = recalcNormals;
            BlendBones   = blendBones;
        }
    }

    /// <summary>ブレンドプレビュー（Undo記録なし）</summary>
    [PLCommand(Description = "ブレンドプレビュー（Undo記録なし）</summary>")]
    public class PreviewModelBlendCommand : PanelCommand
    {
        [PLParam(TextKey = "BlendCloneModelIndex",
                 Description = "プレビューを書き込むクローンモデルの索引", Required = true)]
        public int CloneModelIndex { get; }

        [PLParam(TextKey = "BlendWeights",
                 Description = "ブレンド元ごとの重み", Required = true)]
        public float[] Weights     { get; }

        [PLParam(TextKey = "BlendMeshEnabled",
                 Description = "メッシュごとにブレンドへ含めるか", Required = true)]
        public bool[]  MeshEnabled { get; }

        [PLParam(TextKey = "BlendBones",
                 Description = "ボーンの姿勢もブレンドする", Required = true)]
        public bool    BlendBones  { get; }
        public PreviewModelBlendCommand(
            int sourceModelIndex, int cloneModelIndex,
            float[] weights, bool[] meshEnabled, bool blendBones)
            : base(sourceModelIndex)
        {
            CloneModelIndex = cloneModelIndex;
            Weights      = weights;
            MeshEnabled  = meshEnabled;
            BlendBones   = blendBones;
        }
    }

    // ================================================================
    // メッシュブレンド
    // ================================================================

    /// <summary>
    /// メッシュブレンドのソース 1 件。
    ///
    /// ModelIndex は宛先モデルと異なってよい（別モデルのオブジェクトを混ぜられる）。
    /// MasterIndex はその ModelIndex のモデル内での索引であり、
    /// 宛先モデルの索引空間とは別物なので取り違えないこと。
    /// </summary>
    public struct BlendSourceSpec
    {
        /// <summary>ソースが属するモデルの索引</summary>
        public int   ModelIndex;
        /// <summary>そのモデル内の MeshContext 索引</summary>
        public int   MasterIndex;
        /// <summary>ウェイト [0, 1]</summary>
        public float Weight;

        public BlendSourceSpec(int modelIndex, int masterIndex, float weight)
        {
            ModelIndex  = modelIndex;
            MasterIndex = masterIndex;
            Weight      = weight;
        }
    }

    /// <summary>
    /// 宛先メッシュへ、複数のソースメッシュを加重平均でブレンドして適用する。
    ///
    /// 合成規則: result = base × (1 − Σw) + Σ(w_k × src_k)
    /// base はブレンド前形状。Σw > 1 のときは w_k を正規化し base の係数を 0 にする。
    ///
    /// CreateNewObject = false … 宛先に書き込み、ブレンド前の形状を
    ///   バックアップメッシュとして残す。
    /// CreateNewObject = true  … 宛先を複製し、複製側へ書き込む（元は変更しない）。
    ///
    /// 宛先は ModelIndex のモデル内に限る。別モデルを宛先にすると
    /// PanelCommand.ModelIndex と書き込み先が食い違い、Undo と
    /// 所有権判定の基準が二重になるため。
    /// </summary>
    [PLCommand(Description = "宛先メッシュへ、複数のソースメッシュを加重平均でブレンドして適用する。")]
    public class ApplyBlendCommand : PanelCommand
    {
        /// <summary>1 コマンドで受け付けるソースの上限</summary>
        public const int MaxSources = 6;

        /// <summary>
        /// ソース一覧を平行配列で持つ（最大 MaxSources 件）。
        ///
        /// BlendSourceSpec[] のまま持つとスキーマに出せない
        /// （要素が構造体の配列は対応表に無い）ため、3 本の配列に分けてある。
        /// 3 本は同じ長さにすること。受け口が確かめる。
        /// </summary>
        [PLParam(TextKey = "MeshBlendSourceModelIndices",
                 Description = "ブレンド元が属するモデルの索引。3 本の配列は同じ長さにすること",
                 Required = true)]
        public int[]   SourceModelIndices  { get; }

        [PLParam(TextKey = "MeshBlendSourceMasterIndices",
                 Description = "ブレンド元の masterIndex。SourceModelIndices と同じ並び",
                 Required = true,
                 IsMeshRef = true, MeshRefModelKey = "SourceModelIndices")]
        public int[]   SourceMasterIndices { get; }

        [PLParam(TextKey = "MeshBlendSourceWeights",
                 Description = "ブレンド元の重み [0,1]。SourceModelIndices と同じ並び",
                 Required = true)]
        public float[] SourceWeights       { get; }

        /// <summary>
        /// 平行配列から起こしたソース一覧。受け口はこちらを使う。
        /// 3 本の長さが揃っていないときは短い方に合わせる。
        /// </summary>
        public BlendSourceSpec[] Sources
        {
            get
            {
                int n = System.Math.Min(
                    SourceModelIndices?.Length ?? 0,
                    System.Math.Min(SourceMasterIndices?.Length ?? 0, SourceWeights?.Length ?? 0));
                var a = new BlendSourceSpec[n];
                for (int i = 0; i < n; i++)
                    a[i] = new BlendSourceSpec(
                        SourceModelIndices[i], SourceMasterIndices[i], SourceWeights[i]);
                return a;
            }
        }

        /// <summary>
        /// BlendSourceSpec[] を平行配列へ分ける。呼び出し側の書き換えを短くするための補助。
        /// コンストラクタの多重定義にはしない（PickConstructor の選択が不定になるため）。
        /// </summary>
        public static void SplitSources(
            BlendSourceSpec[] sources,
            out int[] modelIndices, out int[] masterIndices, out float[] weights)
        {
            int n = sources?.Length ?? 0;
            modelIndices  = new int[n];
            masterIndices = new int[n];
            weights       = new float[n];
            for (int i = 0; i < n; i++)
            {
                modelIndices[i]  = sources[i].ModelIndex;
                masterIndices[i] = sources[i].MasterIndex;
                weights[i]       = sources[i].Weight;
            }
        }

        /// <summary>書き込み先 MeshContext の MasterIndex（ModelIndex のモデル内）</summary>
        [PLParam(TextKey = "MeshBlendDestMasterIndex",
                 Description = "結果を書き込む描画オブジェクトの masterIndex", Required = true,
                 IsMeshRef = true)]
        public int    DestMasterIndex      { get; }

        /// <summary>宛先を複製して、そちらへ書き込むか</summary>
        [PLParam(TextKey = "MeshBlendCreateNewObject",
                 Description = "宛先を複製してそちらへ書く。既定は false")]
        public bool   CreateNewObject      { get; }

        /// <summary>適用後に法線を再計算するか</summary>
        [PLParam(TextKey = "MeshBlendRecalculateNormals",
                 Description = "適用後に頂点法線を再計算する。既定は true")]
        public bool   RecalculateNormals   { get; }

        /// <summary>選択頂点のみに適用するか（対象は宛先の選択頂点）</summary>
        [PLParam(TextKey = "MeshBlendSelectedVerticesOnly",
                 Description = "宛先の選択頂点だけに適用する。既定は false")]
        public bool   SelectedVerticesOnly { get; }

        /// <summary>宛先頂点とソース頂点の対応付け方式</summary>
        [PLParam(TextKey = "MeshBlendMatchMode",
                 Description = "宛先頂点とソース頂点の突き合わせ方。既定は Index")]
        public Poly_Ling.UI.BlendMatchMode MatchMode { get; }

        /// <summary>
        /// ソースと重みをオブジェクトグループとして残すか。既定 false。
        /// 残すと、ソースを直したあとに同じ設定で混ぜ直せる。
        /// </summary>
        [PLParam(TextKey = "KeepAsGroup",
                 Description = "ソースと重みをオブジェクトグループとして残す")]
        public bool   KeepAsGroup          { get; }

        public ApplyBlendCommand(
            int modelIndex,
            int[] sourceModelIndices, int[] sourceMasterIndices, float[] sourceWeights,
            int destMasterIndex,
            bool createNewObject      = false,
            bool recalculateNormals   = true,
            bool selectedVerticesOnly = false,
            Poly_Ling.UI.BlendMatchMode matchMode = Poly_Ling.UI.BlendMatchMode.Index,
            bool keepAsGroup          = false)
            : base(modelIndex)
        {
            KeepAsGroup          = keepAsGroup;
            SourceModelIndices   = sourceModelIndices  ?? System.Array.Empty<int>();
            SourceMasterIndices  = sourceMasterIndices ?? System.Array.Empty<int>();
            SourceWeights        = sourceWeights       ?? System.Array.Empty<float>();
            DestMasterIndex      = destMasterIndex;
            CreateNewObject      = createNewObject;
            RecalculateNormals   = recalculateNormals;
            SelectedVerticesOnly = selectedVerticesOnly;
            MatchMode            = matchMode;
        }
    }
}
