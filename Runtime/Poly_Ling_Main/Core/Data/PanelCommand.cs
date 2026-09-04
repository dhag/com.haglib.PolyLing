// PanelCommand.cs
// パネルからメインルーチンへの操作要求
// すべてプリミティブ値で構成される

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Ops;
using Poly_Ling.Symmetry;

namespace Poly_Ling.Data
{
    // ================================================================
    // UV投影方式
    // ================================================================

    public enum ProjectionType
    {
        PlanarXY,
        PlanarXZ,
        PlanarYZ,
        Box,
        Cylindrical,
        Spherical
    }
    public abstract class PanelCommand
    {
        public int ModelIndex { get; }
        protected PanelCommand(int modelIndex) { ModelIndex = modelIndex; }
    }

    // ================================================================
    // 選択
    // ================================================================

    public class SelectMeshCommand : PanelCommand
    {
        [PLParam(TextKey = "SelectMeshCategory",
                 Description = "選択するリストの分類", Required = true)]
        public MeshCategory Category { get; }

        [PLParam(TextKey = "SelectMeshIndices",
                 Description = "Category のリスト内での索引", Required = true)]
        public int[] Indices { get; }
        public SelectMeshCommand(int modelIndex, MeshCategory category, int[] indices)
            : base(modelIndex) { Category = category; Indices = indices; }
    }

    // ================================================================
    // 属性変更
    // ================================================================

    public class ToggleVisibilityCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }
        public ToggleVisibilityCommand(int modelIndex, int masterIndex)
            : base(modelIndex) { MasterIndex = masterIndex; }
    }

    public class SetBatchVisibilityCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BatchVisible",
                 Description = "表示する / 隠す", Required = true)]
        public bool Visible { get; }
        public SetBatchVisibilityCommand(int modelIndex, int[] masterIndices, bool visible)
            : base(modelIndex) { MasterIndices = masterIndices; Visible = visible; }
    }

    public class ToggleLockCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }
        public ToggleLockCommand(int modelIndex, int masterIndex)
            : base(modelIndex) { MasterIndex = masterIndex; }
    }

    /// <summary>
    /// 複数オブジェクトのロック状態を一括設定する。
    /// オブジェクトリストの行内ロックボタンを、選択が複数あるときに使う。
    /// </summary>
    public class SetBatchLockCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BatchLocked",
                 Description = "ロックする / 解除する", Required = true)]
        public bool  Locked        { get; }
        public SetBatchLockCommand(int modelIndex, int[] masterIndices, bool locked)
            : base(modelIndex) { MasterIndices = masterIndices; Locked = locked; }
    }

    /// <summary>
    /// ミラーの有無そのものを切り替える。属性を書くだけの
    /// SetBatchMirrorTypeCommand と違い、ミラー側 MeshContext を作る／始末する。
    ///
    /// 【解消（Enabled = false）】
    ///   MirrorGeometryDerived = true （MQO 系）… ミラー側を破棄する。
    ///       実体側から再生成できるため、残しても情報が増えない。
    ///   同 false （PMX 系）… ミラー側を独立メッシュにする（Type = Mesh）。
    ///       ボーンウェイトなど実体側から復元できない情報を持つため破棄しない。
    ///       実体側の DetachedMirrorObjectId に相手を控える。
    ///
    /// 【有効化（Enabled = true）】
    ///   DetachedMirrorObjectId が有効 … その相手を引き当てて再ペアする。
    ///   無い場合                      … 実体側から生成ミラーを作る。
    /// </summary>
    public class SetMirrorEnabledCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "MirrorEnabled",
                 Description = "ミラーを有効にする / 解消する", Required = true)]
        public bool  Enabled       { get; }
        public SetMirrorEnabledCommand(int modelIndex, int[] masterIndices, bool enabled)
            : base(modelIndex) { MasterIndices = masterIndices; Enabled = enabled; }
    }

    /// <summary>
    /// 複数オブジェクトのミラータイプを一括設定する。
    /// 値は CycleMirrorTypeCommand と同じ 0..2 の範囲（0=なし / 1=分離 / 2=結合）。
    /// 上限は MirrorViewUtil.MirrorTypeCount が正典で、3 以上は MQO へ不正値として
    /// 書き出されるため作らない（MirrorViewUtil.cs:43-52）。
    /// </summary>
    public class SetBatchMirrorTypeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BatchMirrorType",
                 Description = "ミラータイプ。0=なし, 1=分離, 2=結合",
                 Min = 0, Max = Poly_Ling.View.MirrorViewUtil.MirrorTypeCount - 1, Required = true)]
        public int   MirrorType    { get; }
        public SetBatchMirrorTypeCommand(int modelIndex, int[] masterIndices, int mirrorType)
            : base(modelIndex) { MasterIndices = masterIndices; MirrorType = mirrorType; }
    }

    /// <summary>
    /// オブジェクトの編集者（担当者）を設定・解放するコマンド。
    ///
    /// EditorName == ""     : 解放（担当者なしに戻す）
    /// EditorName == 自分の名前: 取得（claim）
    /// Force == true        : 他人が担当中でも上書きする（ホスト権限）
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    public class SetObjectEditorCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "EditorName",
                 Description = "担当者の名前。空文字で解放する", Required = true)]
        public string  EditorName    { get; }

        [PLParam(TextKey = "ForceClaim",
                 Description = "他人が担当中でも上書きする。既定は false")]
        public bool    Force         { get; }

        public SetObjectEditorCommand(
            int modelIndex, int[] masterIndices, string editorName,
            ulong[] objectIds = null, bool force = false)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            EditorName    = editorName ?? "";
            Force         = force;
        }
    }

    /// <summary>
    /// IgnorePoseInArmature フラグを設定するコマンド。
    /// true の場合、BoneTransform.Rotation を 0 にリセットする。
    /// </summary>
    public class SetIgnorePoseCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "IgnorePoseValue",
                 Description = "アーマチュア内で姿勢を無視する", Required = true)]
        public bool  Value         { get; }
        public SetIgnorePoseCommand(int modelIndex, int[] masterIndices, bool value)
            : base(modelIndex) { MasterIndices = masterIndices; Value = value; }
    }

    /// <summary>
    /// オブジェクト原点（BoneTransform.Position）を名前指定で一括設定するコマンド。
    /// 「原点だけ移動 = true / 子を一緒に移動 = false」と同じ挙動で適用する。
    ///
    /// Rotations は任意。null なら回転を触らない。要素が null の行も同じく触らない
    /// （CSV に回転列が無い行を「指定なし」として扱うため）。
    /// </summary>
    public class ApplyObjectOriginsCommand : PanelCommand
    {
        [PLParam(TextKey = "ObjectOriginNames",
                 Description = "原点を設定する描画オブジェクトの名前", Required = true)]
        public string[]  Names     { get; }

        [PLParam(TextKey = "ObjectOriginPositions",
                 Description = "Names と同じ並びの原点位置", Required = true)]
        public Vector3[] Positions { get; }

        /// <summary>行ごとの回転(°)。null = 回転を適用しない。</summary>
        [PLParam(TextKey = "ObjectOriginRotations",
                 Description = "Names と同じ並びの回転（度）。null で回転を触らない")]
        public Vector3?[] Rotations { get; }

        public ApplyObjectOriginsCommand(
            int modelIndex, string[] names, Vector3[] positions, Vector3?[] rotations = null)
            : base(modelIndex) { Names = names; Positions = positions; Rotations = rotations; }
    }

    /// <summary>
    /// メッシュオブジェクトの姿勢を、表示用のくさびオブジェクト列としてモデル内に生成する。
    /// くさびは新規の空オブジェクト（コンテナ）の配下に、メッシュの階層を保って並ぶ。
    /// </summary>
    public class GenerateObjectPoseWedgesCommand : PanelCommand
    {
        /// <summary>くさびの全長（オブジェクトの拡大率平均を掛ける前の基準値）。</summary>
        [PLParam(TextKey = "WedgeLength",
                 Description = "くさびの全長。拡大率平均を掛ける前の基準値", Required = true)]
        public float WedgeLength { get; }

        /// <summary>コンテナの名前。空なら既定名。</summary>
        [PLParam(TextKey = "WedgeContainerNewName",
                 Description = "生成するコンテナの名前。空で既定名", Required = true)]
        public string ContainerName { get; }

        public GenerateObjectPoseWedgesCommand(int modelIndex, float wedgeLength, string containerName)
            : base(modelIndex) { WedgeLength = wedgeLength; ContainerName = containerName; }
    }

    /// <summary>
    /// くさびオブジェクト列を読み、名前一致でメッシュオブジェクトの姿勢へ適用する。
    /// 適用は「原点だけ移動」と同じく、自頂点を再局所化して見た目を保つ。
    /// </summary>
    public class ApplyObjectPoseWedgesCommand : PanelCommand
    {
        /// <summary>コンテナの MeshContextList 索引。-1 なら名前で自動検出。</summary>
        [PLParam(TextKey = "WedgeContainerMasterIndex",
                 Description = "くさびコンテナの masterIndex。-1 で名前から自動検出", Required = true)]
        public int ContainerMasterIndex { get; }

        /// <summary>自動検出に使うコンテナ名。空なら既定名。</summary>
        [PLParam(TextKey = "WedgeContainerName",
                 Description = "自動検出に使うコンテナ名。空で既定名", Required = true)]
        public string ContainerName { get; }

        public ApplyObjectPoseWedgesCommand(int modelIndex, int containerMasterIndex, string containerName)
            : base(modelIndex) { ContainerMasterIndex = containerMasterIndex; ContainerName = containerName; }
    }

    /// <summary>
    /// PreserveNormals フラグ（頂点法線を自動再計算しない）を設定するコマンド。
    /// </summary>
    public class SetPreserveNormalsCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "PreserveNormalsValue",
                 Description = "頂点法線を自動再計算しない", Required = true)]
        public bool  Value         { get; }
        public SetPreserveNormalsCommand(int modelIndex, int[] masterIndices, bool value)
            : base(modelIndex) { MasterIndices = masterIndices; Value = value; }
    }

    /// <summary>ミラー分岐ルートのフラグを設定するコマンド。</summary>
    public class SetMirrorBranchRootCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "MirrorBranchRootValue",
                 Description = "ミラー分岐のルートとして扱う", Required = true)]
        public bool  Value         { get; }
        public SetMirrorBranchRootCommand(int modelIndex, int[] masterIndices, bool value)
            : base(modelIndex) { MasterIndices = masterIndices; Value = value; }
    }

    public class CycleMirrorTypeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }
        public CycleMirrorTypeCommand(int modelIndex, int masterIndex)
            : base(modelIndex) { MasterIndex = masterIndex; }
    }

    public class RenameMeshCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "MeshNewName",
                 Description = "描画オブジェクトの新しい名前", Required = true)]
        public string NewName { get; }
        public RenameMeshCommand(int modelIndex, int masterIndex, string newName)
            : base(modelIndex) { MasterIndex = masterIndex; NewName = newName; }
    }

    /// <summary>
    /// 複数オブジェクトの名前を一括変更する（名称一括変更 CSV 用）。
    ///
    /// MasterIndices と NewNames は同じ並び・同じ長さ。
    /// 名前の重複は受け側（PlayerCommandDispatcher）が
    /// MeshRenameCsvHelper.ResolveUniqueNames で自動回避するため、
    /// 送信側は CSV に書かれた希望名をそのまま渡してよい。
    /// </summary>
    public class RenameMeshesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[]    MasterIndices { get; }

        [PLParam(TextKey = "MeshNewNames",
                 Description = "MasterIndices と同じ並び・同じ長さの新しい名前", Required = true)]
        public string[] NewNames      { get; }
        public RenameMeshesCommand(int modelIndex, int[] masterIndices, string[] newNames)
            : base(modelIndex) { MasterIndices = masterIndices; NewNames = newNames; }
    }

    /// <summary>
    /// メッシュの TreeView 折りたたみ状態変更
    /// </summary>
    public class SetMeshFoldingCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndex",
                 Description = "対象の描画オブジェクトの masterIndex", Required = true)]
        public int MasterIndex { get; }

        [PLParam(TextKey = "MeshFolding",
                 Description = "ツリーの子を折りたたむ", Required = true)]
        public bool IsFolding { get; }
        public SetMeshFoldingCommand(int modelIndex, int masterIndex, bool isFolding)
            : base(modelIndex) { MasterIndex = masterIndex; IsFolding = isFolding; }
    }

    // ================================================================
    // リスト操作
    // ================================================================

    public class AddMeshCommand : PanelCommand
    {
        public AddMeshCommand(int modelIndex) : base(modelIndex) { }
    }

    public class DeleteMeshesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public DeleteMeshesCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    public class DuplicateMeshesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public DuplicateMeshesCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    /// <summary>
    /// メッシュリスト順序変更（D&D/上下移動/Indent/Outdent/先頭末尾移動）
    /// </summary>
    public class ReorderMeshesCommand : PanelCommand
    {
        public struct ReorderEntry
        {
            public int MasterIndex;
            public int NewDepth;
            public int NewParentMasterIndex;
        }

        [PLParam(TextKey = "ReorderCategory",
                 Description = "並べ替える対象リストの分類", Required = true)]
        public MeshCategory Category { get; }

        [PLParam(TextKey = "ReorderEntries",
                 Description = "各行の移動先の深さと親を並べたもの", Required = true)]
        public ReorderEntry[] Entries { get; }

        /// <summary>
        /// 親を付け替えたとき、ワールド姿勢を保つようローカル姿勢を組み直すか。
        ///
        /// ComputeWorldMatrices は「親のワールド × 自身のローカル」と積む
        /// （ModelContext.cs:1746-1748）ので、組み直さないと親が付いた瞬間に
        /// 子のワールド位置が親のぶんだけ飛ぶ。
        /// Unity の Transform.SetParent(parent, worldPositionStays: true) と同じ扱いを既定にする。
        ///
        /// false にすると付け替え前のローカル値がそのまま残る（従来の挙動）。
        /// 親からの相対値を直接入れてある場合はこちらを使う。
        /// </summary>
        [PLParam(TextKey = "PreserveWorldTransform",
                 Description = "親を付け替えてもワールド姿勢を保つ")]
        public bool PreserveWorldTransform { get; }

        public ReorderMeshesCommand(
            int modelIndex, MeshCategory category, ReorderEntry[] entries,
            bool preserveWorldTransform = true)
            : base(modelIndex)
        {
            Category               = category;
            Entries                = entries;
            PreserveWorldTransform = preserveWorldTransform;
        }
    }

    // ================================================================
    // BonePose
    // ================================================================

    public class InitBonePoseCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public InitBonePoseCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    public class SetBonePoseActiveCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BonePoseActive",
                 Description = "ボーンポーズを有効にする", Required = true)]
        public bool Active { get; }
        public SetBonePoseActiveCommand(int modelIndex, int[] masterIndices, bool active)
            : base(modelIndex) { MasterIndices = masterIndices; Active = active; }
    }

    public class ResetBonePoseLayersCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public ResetBonePoseLayersCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    public class BakePoseToBindPoseCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public BakePoseToBindPoseCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    // ================================================================
    // モーフ
    // ================================================================

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

    public class ConvertMorphToMeshCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }
        public ConvertMorphToMeshCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

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

    public class StartMorphPreviewCommand : PanelCommand
    {
        [PLParam(TextKey = "PreviewMorphIndices",
                 Description = "プレビューするモーフの索引", Required = true)]
        public int[] MorphIndices { get; }
        public StartMorphPreviewCommand(int modelIndex, int[] morphIndices)
            : base(modelIndex) { MorphIndices = morphIndices; }
    }

    public class ApplyMorphPreviewCommand : PanelCommand
    {
        [PLParam(TextKey = "MorphPreviewWeight",
                 Description = "プレビューに掛けるモーフのウェイト",
                 LimitKey = "MorphPreview.Weight", Required = true)]
        public float Weight { get; }
        public ApplyMorphPreviewCommand(int modelIndex, float weight)
            : base(modelIndex) { Weight = weight; }
    }

    public class EndMorphPreviewCommand : PanelCommand
    {
        public EndMorphPreviewCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // モーフ全選択/全解除
    // ================================================================

    public class SelectAllMorphsCommand : PanelCommand
    {
        [PLParam(TextKey = "AllMorphIndices",
                 Description = "全選択の対象となるモーフの索引", Required = true)]
        public int[] AllMorphIndices { get; }
        public SelectAllMorphsCommand(int modelIndex, int[] allMorphIndices)
            : base(modelIndex) { AllMorphIndices = allMorphIndices; }
    }

    public class DeselectAllMorphsCommand : PanelCommand
    {
        public DeselectAllMorphsCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // パーツ選択辞書
    // ================================================================

    /// <summary>現在のパーツ選択をセットとして保存</summary>
    public class SavePartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetName",
                 Description = "保存するパーツ選択セットの名前", Required = true)]
        public string SetName { get; }
        public SavePartsSetCommand(int modelIndex, string setName)
            : base(modelIndex) { SetName = setName; }
    }

    /// <summary>選択辞書エントリを現在の選択に適用（置き換え）</summary>
    public class LoadPartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "適用するパーツ選択セットの索引", Required = true)]
        public int SetIndex { get; }
        public LoadPartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリを現在の選択に追加（Union）</summary>
    public class AddPartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "現在の選択へ足すパーツ選択セットの索引", Required = true)]
        public int SetIndex { get; }
        public AddPartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>現在の選択から辞書エントリを除外（Subtract）</summary>
    public class SubtractPartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "現在の選択から引くパーツ選択セットの索引", Required = true)]
        public int SetIndex { get; }
        public SubtractPartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリを削除</summary>
    public class DeletePartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "削除するパーツ選択セットの索引", Required = true)]
        public int SetIndex { get; }
        public DeletePartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリの名前を変更</summary>
    public class RenamePartsSetCommand : PanelCommand
    {
        [PLParam(TextKey = "PartsSetIndex",
                 Description = "名前を変えるパーツ選択セットの索引", Required = true)]
        public int SetIndex { get; }

        [PLParam(TextKey = "PartsSetNewName",
                 Description = "パーツ選択セットの新しい名前", Required = true)]
        public string NewName { get; }
        public RenamePartsSetCommand(int modelIndex, int setIndex, string newName)
            : base(modelIndex) { SetIndex = setIndex; NewName = newName; }
    }

    /// <summary>
    /// 選択辞書をCSVフォルダへエクスポート。
    /// FolderPath が空のときは実行側でダイアログを開く（メインエディタ経路）。
    /// </summary>
    public class ExportPartsSetsCsvCommand : PanelCommand
    {
        public string FolderPath { get; }
        public ExportPartsSetsCsvCommand(int modelIndex) : base(modelIndex) { FolderPath = null; }
        public ExportPartsSetsCsvCommand(int modelIndex, string folderPath)
            : base(modelIndex) { FolderPath = folderPath; }
    }

    /// <summary>
    /// CSVフォルダから選択辞書をインポート。
    /// FolderPath が空のときは実行側でダイアログを開く（メインエディタ経路・単一ファイル）。
    /// ByObjectName が true のときはファイル内の "# object" 名と一致するオブジェクトへ読み込む。
    /// </summary>
    public class ImportPartsSetCsvCommand : PanelCommand
    {
        public string FolderPath   { get; }
        public bool   ByObjectName { get; }
        public ImportPartsSetCsvCommand(int modelIndex)
            : base(modelIndex) { FolderPath = null; ByObjectName = false; }
        public ImportPartsSetCsvCommand(int modelIndex, string folderPath, bool byObjectName)
            : base(modelIndex) { FolderPath = folderPath; ByObjectName = byObjectName; }
    }

    // ================================================================
    // 法線再計算 除外辞書（実体は MeshObject.NormalRecalcExcludeList）
    // ================================================================

    /// <summary>現在の選択を法線再計算の除外セットとして保存</summary>
    public class SaveNormalExcludeSetCommand : PanelCommand
    {
        [PLParam(TextKey = "NormalExcludeSetName",
                 Description = "保存する法線再計算 除外セットの名前", Required = true)]
        public string SetName { get; }
        public SaveNormalExcludeSetCommand(int modelIndex, string setName)
            : base(modelIndex) { SetName = setName; }
    }

    /// <summary>除外セットを現在の選択に適用（置き換え）</summary>
    public class LoadNormalExcludeSetCommand : PanelCommand
    {
        [PLParam(TextKey = "NormalExcludeSetIndex",
                 Description = "適用する法線再計算 除外セットの索引", Required = true)]
        public int SetIndex { get; }
        public LoadNormalExcludeSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>除外セットを削除</summary>
    public class DeleteNormalExcludeSetCommand : PanelCommand
    {
        [PLParam(TextKey = "NormalExcludeSetIndex",
                 Description = "削除する法線再計算 除外セットの索引", Required = true)]
        public int SetIndex { get; }
        public DeleteNormalExcludeSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>除外セットの名前を変更</summary>
    public class RenameNormalExcludeSetCommand : PanelCommand
    {
        [PLParam(TextKey = "NormalExcludeSetIndex",
                 Description = "名前を変える法線再計算 除外セットの索引", Required = true)]
        public int SetIndex { get; }

        [PLParam(TextKey = "NormalExcludeSetNewName",
                 Description = "法線再計算 除外セットの新しい名前", Required = true)]
        public string NewName { get; }
        public RenameNormalExcludeSetCommand(int modelIndex, int setIndex, string newName)
            : base(modelIndex) { SetIndex = setIndex; NewName = newName; }
    }

    // ================================================================
    // 面の表示・非表示
    // ================================================================

    /// <summary>
    /// 面の非表示フラグ（Face.IsHidden）を操作する。
    /// 対象は選択中の描画メッシュ（未選択なら編集対象メッシュ単体）。
    ///
    /// 非表示は編集補助であり、面データは残る（エクスポートにも出る）。
    /// メッシュ丸ごとの非表示は ToggleVisibilityCommand / SetBatchVisibilityCommand を使うこと。
    /// </summary>
    public class SetFaceHiddenCommand : PanelCommand
    {
        public enum Mode
        {
            /// <summary>選択面を隠す（面選択が無い場合は何もしない）</summary>
            HideSelected,
            /// <summary>選択面以外を隠す（面選択が無い場合は何もしない）</summary>
            HideUnselected,
            /// <summary>すべての面を表示に戻す</summary>
            ShowAll,
            /// <summary>表示・非表示を反転する</summary>
            InvertHidden,
        }

        [PLParam(TextKey = "FaceHiddenOperation",
                 Description = "隠す / 選択以外を隠す / 全表示 / 反転", Required = true)]
        public Mode Operation { get; }

        public SetFaceHiddenCommand(int modelIndex, Mode operation)
            : base(modelIndex) { Operation = operation; }
    }

    // ================================================================
    // 法線編集
    // ================================================================

    /// <summary>
    /// 選択範囲の法線を編集する。対象は選択中の描画メッシュ（未選択なら編集対象メッシュ単体）。
    ///
    /// 各メッシュ内の対象範囲は次のルールで決まる（NormalEditOps.CollectTargetCorners）。
    ///   面選択がある     → その面のコーナーのみ
    ///   頂点選択のみある → その頂点が参照する全スロット
    ///   選択が無い       → メッシュ全体
    /// ただし RecalcByAngle だけはメッシュ全体が対象（スロットを作り直すため）。
    /// </summary>
    public class NormalEditCommand : PanelCommand
    {
        /// <summary>
        /// AlignToAxis / FlattenOnAxis で指せる軸の数（X / Y / Z）。
        /// パネル側の選択肢（PlayerNormalEditSubPanel の AxisNames）も同数で対応する。
        /// ミラー軸（BakeMirrorCommand.MirrorAxisCount）とは別物なので共有しない。
        /// </summary>
        public const int AxisCount = 3;

        public enum Op
        {
            /// <summary>スムージング角で法線を作り直す（メッシュ全体・スロット再構築）</summary>
            RecalcByAngle,
            /// <summary>面法線にする（フラット化）</summary>
            SetFromFaces,
            /// <summary>
            /// 対象コーナーの面法線だけを頂点ごとに重み付き平均し、その1本を書く。
            /// スロット数は変えない。選択した面だけを使った頂点法線が得られる。
            /// </summary>
            AverageFromFaces,
            /// <summary>統合（頂点上のスロット法線を同一値にする）</summary>
            Unify,
            /// <summary>分離（面ごとに別スロットへ分け、面法線を入れる）</summary>
            Break,
            /// <summary>対象法線を1方向（全体の平均）に揃える</summary>
            AverageAll,
            /// <summary>隣接頂点の法線と補間して平滑化</summary>
            Smooth,
            /// <summary>球状化（中心から外向き）</summary>
            Sphereize,
            /// <summary>ターゲットへ向ける</summary>
            PointToTarget,
            /// <summary>指定軸方向へ向ける</summary>
            AlignToAxis,
            /// <summary>指定軸の成分をゼロにする</summary>
            FlattenOnAxis,
            /// <summary>
            /// ミラー対応（X軸対称）。中央近傍（|Position.x| ≦ MirrorThreshold）の
            /// 頂点だけ法線の X 成分をゼロにする。
            /// </summary>
            MirrorFlattenSeamX,
            /// <summary>反転</summary>
            Flip,
        }

        [PLParam(TextKey = "NormalEditOperation",
                 Description = "法線に対して何をするか", Required = true)]
        public Op    Operation  { get; }

        /// <summary>RecalcByAngle のスムージング角（度）</summary>
        [PLParam(TextKey = "NormalEditAngleDeg",
                 Description = "RecalcByAngle のスムージング角（度）。既定は 59.5",
                 LimitKey = "NormalEdit.AngleDeg")]
        public float AngleDeg   { get; }

        /// <summary>Smooth の強度（0-1）</summary>
        [PLParam(TextKey = "NormalEditStrength",
                 Description = "Smooth の強度。既定は 0.5",
                 LimitKey = "NormalEdit.Strength")]
        public float Strength   { get; }

        /// <summary>AlignToAxis / FlattenOnAxis の軸（0=X, 1=Y, 2=Z）</summary>
        [PLParam(TextKey = "NormalEditAxis",
                 Description = "AlignToAxis / FlattenOnAxis の軸。0=X, 1=Y, 2=Z。既定は 0",
                 Min = 0, Max = AxisCount - 1)]
        public int   Axis       { get; }

        /// <summary>AlignToAxis の符号（true で負方向）</summary>
        [PLParam(TextKey = "NormalEditNegative",
                 Description = "AlignToAxis を負方向にする。既定は false")]
        public bool  Negative   { get; }

        /// <summary>Sphereize / PointToTarget の座標</summary>
        [PLParam(TextKey = "NormalEditTarget",
                 Description = "Sphereize の中心 / PointToTarget の向き先")]
        public Vector3 Target   { get; }

        /// <summary>Sphereize の中心に選択の重心を使うか</summary>
        [PLParam(TextKey = "NormalEditUseSelectionCenter",
                 Description = "Sphereize の中心に選択の重心を使う。既定は true")]
        public bool  UseSelectionCenter { get; }

        /// <summary>PointToTarget で 1 本のベクトルに揃えるか</summary>
        [PLParam(TextKey = "NormalEditAlignVectors",
                 Description = "PointToTarget で 1 本のベクトルに揃える。既定は false")]
        public bool  AlignVectors { get; }

        /// <summary>平均時の重み付け方式</summary>
        [PLParam(TextKey = "NormalEditWeightMode",
                 Description = "面法線を平均するときの重み付け。既定は Uniform")]
        public NormalWeightMode WeightMode { get; }
        /// <summary>
        /// MirrorFlattenSeamX の中央判定しきい値。
        /// |Vertex.Position.x| がこの値以下の頂点を中央（合わせ目）とみなす。
        /// </summary>
        [PLParam(TextKey = "NormalEditMirrorThreshold",
                 Description = "MirrorFlattenSeamX の中央判定しきい値。既定は 0.00001",
                 LimitKey = "NormalEdit.MirrorThreshold")]
        public float MirrorThreshold { get; }

        public NormalEditCommand(
            int modelIndex,
            Op operation,
            float angleDeg = 59.5f,
            float strength = 0.5f,
            int axis = 0,
            bool negative = false,
            Vector3 target = default,
            bool useSelectionCenter = true,
            bool alignVectors = false,
            NormalWeightMode weightMode = NormalWeightMode.Uniform,
            float mirrorThreshold = 0.00001f)
            : base(modelIndex)
        {
            Operation          = operation;
            AngleDeg           = angleDeg;
            Strength           = strength;
            Axis               = axis;
            Negative           = negative;
            Target             = target;
            UseSelectionCenter = useSelectionCenter;
            AlignVectors       = alignVectors;
            WeightMode         = weightMode;
            MirrorThreshold    = mirrorThreshold;
        }
    }

    /// <summary>
    /// 頂点IDの修復。対象は選択中の描画メッシュ（未選択なら編集対象メッシュ単体）。
    ///
    /// 頂点IDはモデル間・オブジェクト間の突き合わせに使う唯一の手掛かりだが、
    /// 未設定・重複・誤付与が混在しやすい。ID を使う操作の前に整えるための操作。
    /// </summary>
    public class RepairVertexIdsCommand : PanelCommand
    {
        public enum RepairMode
        {
            /// <summary>未設定（0 / -1）の頂点にだけ新規IDを割り当てる。既存IDは変更しない。</summary>
            AssignMissing,
            /// <summary>重複IDの 2 個目以降を振り直す。先頭は元のIDを保持する。</summary>
            ResolveDuplicates,
            /// <summary>全頂点に 1 からの連番を振り直す。既存の対応付けは失われる。</summary>
            ReassignSequential,
            /// <summary>全頂点のIDを未設定に戻す。</summary>
            ClearAll,
        }

        [PLParam(TextKey = "RepairVertexIdMode",
                 Description = "未設定のみ / 重複の解消 / 連番振り直し / 全消去", Required = true)]
        public RepairMode Mode { get; }
        public RepairVertexIdsCommand(int modelIndex, RepairMode mode)
            : base(modelIndex) { Mode = mode; }
    }

    /// <summary>
    /// パーツID（Vertex.PartsId）／サブID（Vertex.SubId）の一括採番。
    ///
    /// 【頂点IDとの分離】
    ///   このコマンドは Vertex.Id を読まないし書かない。頂点IDの修復は
    ///   RepairVertexIdsCommand が持つ。両者は独立して掛けられる。
    ///
    /// 【対象】
    ///   TargetMasterIndex で指定した描画オブジェクト 1 つだけ。
    ///   ビューポートの「オブジェクト選択」とは無関係で、選択状態を参照しない。
    ///
    /// 【リファレンス】
    ///   ReferenceVertexCount のときだけ使う。1 つだけ指定する。
    ///   藤壺の配置元が複数オブジェクトだった場合は、あらかじめ 1 つへ結合したものを
    ///   リファレンスに指定すること（このコマンドは結合を行わない）。
    /// </summary>
    public class AssignPartsIdsCommand : PanelCommand
    {
        public enum PartsIdMode
        {
            /// <summary>面・線のつながり（独立性）でパーツを分ける。</summary>
            Connectivity,
            /// <summary>リファレンスの頂点数で頂点列を等分してパーツを分ける。</summary>
            ReferenceVertexCount,
            /// <summary>パーツIDはそのままで、サブIDだけ振り直す。</summary>
            SubIdOnly,
            /// <summary>パーツID・サブIDを 0 に戻す。</summary>
            Clear,
        }

        /// <summary>採番する描画オブジェクトの masterIndex。</summary>
        [PLParam(TextKey = "PartsIdTargetMasterIndex",
                 Description = "採番する描画オブジェクトの masterIndex", Required = true)]
        public int TargetMasterIndex { get; }

        [PLParam(TextKey = "PartsIdMode",
                 Description = "つながり / リファレンス頂点数 / サブIDのみ / 消去", Required = true)]
        public PartsIdMode Mode { get; }

        /// <summary>
        /// 1 パーツの頂点数を取る描画オブジェクトの masterIndex。-1 で未指定。
        /// ReferenceVertexCount 以外のモードでは無視する。
        /// </summary>
        [PLParam(TextKey = "PartsIdReferenceMasterIndex",
                 Description = "1 パーツの頂点数を取るオブジェクトの masterIndex。-1 で未指定")]
        public int ReferenceMasterIndex { get; }

        /// <summary>面にも線にも属さない頂点の扱い。Connectivity のときだけ効く。</summary>
        [PLParam(TextKey = "PartsIdIsolatedPolicy",
                 Description = "孤立頂点をまとめて 1 パーツにするか、1 つずつ独立させるか")]
        public IsolatedVertexPolicy IsolatedPolicy { get; }

        public AssignPartsIdsCommand(
            int modelIndex,
            int targetMasterIndex,
            PartsIdMode mode,
            int referenceMasterIndex = -1,
            IsolatedVertexPolicy isolatedPolicy = IsolatedVertexPolicy.SingleGroup)
            : base(modelIndex)
        {
            TargetMasterIndex    = targetMasterIndex;
            Mode                 = mode;
            ReferenceMasterIndex = referenceMasterIndex;
            IsolatedPolicy       = isolatedPolicy;
        }
    }

    /// <summary>
    /// モデル間・オブジェクト間で頂点データを転送する。
    ///
    /// メッシュのペアは SourceMeshIndices[i] ↔ TargetMeshIndices[i] で明示する
    /// （リスト順に暗黙で対応させない）。両配列は同じ長さであること。
    /// インデックスは各モデルの MeshContextList のインデックス。
    /// </summary>
    public class TransferVertexDataCommand : PanelCommand
    {
        /// <summary>転送元モデル（PanelCommand.ModelIndex）。</summary>
        public int SourceModelIndex => ModelIndex;

        /// <summary>転送先モデル。</summary>
        [PLParam(TextKey = "TransferTargetModelIndex",
                 Description = "転送先モデルの索引", Required = true)]
        public int   TargetModelIndex  { get; }

        [PLParam(TextKey = "TransferSourceMeshIndices",
                 Description = "転送元メッシュの索引。TargetMeshIndices と同じ長さ", Required = true)]
        public int[] SourceMeshIndices { get; }

        [PLParam(TextKey = "TransferTargetMeshIndices",
                 Description = "転送先メッシュの索引。SourceMeshIndices と同じ長さ", Required = true)]
        public int[] TargetMeshIndices { get; }

        [PLParam(TextKey = "TransferMatchMode",
                 Description = "頂点の突き合わせ方", Required = true)]
        public VertexMatchMode MatchMode { get; }

        [PLParam(TextKey = "TransferKinds",
                 Description = "転送する頂点データの種類", Required = true)]
        public VertexDataKind  Kinds     { get; }

        public TransferVertexDataCommand(
            int sourceModelIndex, int targetModelIndex,
            int[] sourceMeshIndices, int[] targetMeshIndices,
            VertexMatchMode matchMode, VertexDataKind kinds)
            : base(sourceModelIndex)
        {
            TargetModelIndex  = targetModelIndex;
            SourceMeshIndices = sourceMeshIndices;
            TargetMeshIndices = targetMeshIndices;
            MatchMode         = matchMode;
            Kinds             = kinds;
        }
    }

    /// <summary>メッシュ選択辞書をCSVファイルへ保存</summary>
    public class SaveMeshSelSetsCsvCommand : PanelCommand
    {
        public string FilePath { get; }
        public SaveMeshSelSetsCsvCommand(int modelIndex, string filePath)
            : base(modelIndex) { FilePath = filePath; }
    }

    /// <summary>メッシュ選択辞書をCSVファイルから読込み、既存リストへ追加</summary>
    public class LoadMeshSelSetsCsvCommand : PanelCommand
    {
        public string FilePath { get; }
        public LoadMeshSelSetsCsvCommand(int modelIndex, string filePath)
            : base(modelIndex) { FilePath = filePath; }
    }

    // ================================================================
    // モデルブレンド
    // ================================================================

    /// <summary>
    /// パネルオープン時にターゲットモデルのクローンを作成してプロジェクトに追加する。
    /// cloneName が空の場合はメインエディタ側でユニーク名を生成する。
    /// 戻り値としてクローンのモデルインデックスが必要だが PanelCommand は戻り値を持たないため、
    /// ハンドラが NotifyPanels を呼び出したあとパネルは OnViewChanged で新モデル数を検出する。
    /// </summary>
    public class CreateBlendCloneCommand : PanelCommand
    {
        [PLParam(TextKey = "CloneNameBase",
                 Description = "クローンの名前の基。空で自動採番", Required = true)]
        public string CloneNameBase { get; }
        public CreateBlendCloneCommand(int sourceModelIndex, string cloneNameBase)
            : base(sourceModelIndex) { CloneNameBase = cloneNameBase; }
    }

    /// <summary>ブレンドをクローンモデルに適用する</summary>
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
    // モデル操作
    // ================================================================

    /// <summary>カレントモデルを切り替える</summary>
    public class SwitchModelCommand : PanelCommand
    {
        [PLParam(TextKey = "TargetModelIndex",
                 Description = "切り替え先のモデルの索引", Required = true)]
        public int TargetModelIndex { get; }
        public SwitchModelCommand(int targetModelIndex)
            : base(targetModelIndex) { TargetModelIndex = targetModelIndex; }
    }

    /// <summary>モデルの名前を変更する</summary>
    public class RenameModelCommand : PanelCommand
    {
        [PLParam(TextKey = "ModelNewName",
                 Description = "モデルの新しい名前", Required = true)]
        public string NewName { get; }
        public RenameModelCommand(int modelIndex, string newName)
            : base(modelIndex) { NewName = newName; }
    }

    /// <summary>モデルを削除する</summary>
    public class DeleteModelCommand : PanelCommand
    {
        public DeleteModelCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // 選択辞書
    // ================================================================

    /// <summary>選択中のメッシュを選択辞書エントリとして保存</summary>
    public class SaveSelectionDictionaryCommand : PanelCommand
    {
        [PLParam(TextKey = "SelectionDictionaryCategory",
                 Description = "保存する選択辞書エントリの分類", Required = true)]
        public MeshCategory Category { get; }

        [PLParam(TextKey = "SelectionDictionarySetName",
                 Description = "保存する選択辞書エントリの名前", Required = true)]
        public string SetName { get; }

        [PLParam(TextKey = "SelectionDictionaryMeshNames",
                 Description = "エントリに含める描画オブジェクトの名前", Required = true)]
        public string[] MeshNames { get; }
        public SaveSelectionDictionaryCommand(int modelIndex, MeshCategory category, string setName, string[] meshNames)
            : base(modelIndex) { Category = category; SetName = setName; MeshNames = meshNames; }
    }

    /// <summary>選択辞書エントリを選択に適用（置き換えまたは追加）</summary>
    public class ApplySelectionDictionaryCommand : PanelCommand
    {
        [PLParam(TextKey = "SelectionDictionarySetIndex",
                 Description = "適用する選択辞書エントリの索引", Required = true)]
        public int SetIndex { get; }

        [PLParam(TextKey = "SelectionDictionaryAddToExisting",
                 Description = "現在の選択へ足す。false で置き換える。既定は false")]
        public bool AddToExisting { get; }
        public ApplySelectionDictionaryCommand(int modelIndex, int setIndex, bool addToExisting = false)
            : base(modelIndex) { SetIndex = setIndex; AddToExisting = addToExisting; }
    }

    /// <summary>選択辞書エントリを削除</summary>
    public class DeleteSelectionDictionaryCommand : PanelCommand
    {
        [PLParam(TextKey = "SelectionDictionarySetIndex",
                 Description = "削除する選択辞書エントリの索引", Required = true)]
        public int SetIndex { get; }
        public DeleteSelectionDictionaryCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリの名前を変更</summary>
    public class RenameSelectionDictionaryCommand : PanelCommand
    {
        [PLParam(TextKey = "SelectionDictionarySetIndex",
                 Description = "名前を変える選択辞書エントリの索引", Required = true)]
        public int SetIndex { get; }

        [PLParam(TextKey = "SelectionDictionaryNewName",
                 Description = "選択辞書エントリの新しい名前", Required = true)]
        public string NewName { get; }
        public RenameSelectionDictionaryCommand(int modelIndex, int setIndex, string newName)
            : base(modelIndex) { SetIndex = setIndex; NewName = newName; }
    }

    /// <summary>
    /// パネル側でモデルを直接変更した後、全パネルにリスト構造変更を通知する。
    /// Paste / LoadCSV 等で使用。
    /// </summary>
    public class NotifyListStructureChangedCommand : PanelCommand
    {
        public NotifyListStructureChangedCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>
    /// パネル側で辞書メタデータを直接変更した後、全パネルに Attributes 変更を通知する。
    /// OnLoadDicFile 等で使用。
    /// </summary>
    public class NotifyDictionaryChangedCommand : PanelCommand
    {
        public NotifyDictionaryChangedCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // UV操作
    // ================================================================

    /// <summary>選択メッシュに投影UV展開を適用する</summary>
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
    // BoneTransform（簡易モード用）
    // ================================================================

    /// <summary>BoneTransform の Position/Rotation/Scale 単一軸値変更</summary>
    public class SetBoneTransformValueCommand : PanelCommand
    {
        public enum Field { PositionX, PositionY, PositionZ, RotationX, RotationY, RotationZ, ScaleX, ScaleY, ScaleZ }

        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        [PLParam(TextKey = "BoneTransformField",
                 Description = "書き換える軸。位置 / 回転 / 拡大率の X・Y・Z", Required = true)]
        public Field TargetField { get; }

        [PLParam(TextKey = "BoneTransformValue",
                 Description = "TargetField へ入れる値。回転は度", Required = true)]
        public float Value { get; }
        public SetBoneTransformValueCommand(int modelIndex, int[] masterIndices, Field field, float value)
            : base(modelIndex) { MasterIndices = masterIndices; TargetField = field; Value = value; }
    }

    /// <summary>BoneTransform スライダードラッグ開始（Undo スナップショット取得）</summary>
    public class BeginBoneTransformSliderDragCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        /// <summary>ボーン編集の確定モード（A/B）。パネルが送信時に刻む。</summary>
        [PLParam(TextKey = "BoneMoveMode",
                 Description = "ボーン編集の確定モード。既定は BoneOnlyRebind")]
        public BoneMoveMode Mode { get; set; } = BoneMoveMode.BoneOnlyRebind;
        /// <summary>
        /// 「原点だけ移動」中か。true のとき、対象 MeshFilter の見た目を固定したまま
        /// 原点(BoneTransform)だけを動かすよう受信側が自頂点を再ローカル化する。
        /// パネルが送信時に刻む。
        /// </summary>
        [PLParam(TextKey = "BoneOriginOnly",
                 Description = "見た目を固定したまま原点だけを動かす。既定は false")]
        public bool OriginOnly { get; set; } = false;
        public BeginBoneTransformSliderDragCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    /// <summary>BoneTransform スライダードラッグ終了（Undo 記録コミット）</summary>
    public class EndBoneTransformSliderDragCommand : PanelCommand
    {
        [PLParam(TextKey = "BoneDragDescription",
                 Description = "Undo 記録に残す操作名", Required = true)]
        public string Description { get; }
        public EndBoneTransformSliderDragCommand(int modelIndex, string description)
            : base(modelIndex) { Description = description; }
    }

    /// <summary>
    /// 現在表示中のポーズ（BonePoseData 合成）を頂点へ焼き込み、ポーズ層をクリアして
    /// 焼き込み後の状態を新しいデフォルト・バインドにリセットする（この姿勢で確定）。
    /// </summary>
    public class FreezeCurrentPoseCommand : PanelCommand
    {
        public FreezeCurrentPoseCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // メッシュマージ
    // ================================================================

    /// <summary>
    /// 選択メッシュオブジェクト群をひとつにマージする。
    /// BaseMasterIndex のオブジェクトを基準トランスフォームとして使用する。
    /// CreateNewMesh が true の場合は新規メッシュオブジェクトを作成して結果を格納する。
    /// false の場合は BaseMasterIndex のメッシュオブジェクトに直接結合する。
    /// </summary>
    public class MergeMeshesCommand : PanelCommand
    {
        /// <summary>マージ対象の MasterIndex 配列（基準オブジェクトを含む）</summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        /// <summary>基準オブジェクトの MasterIndex</summary>
        [PLParam(TextKey = "MergeBaseMasterIndex",
                 Description = "結合の基準になるオブジェクトの masterIndex", Required = true)]
        public int BaseMasterIndex { get; }

        /// <summary>true: 新規メッシュオブジェクトに結果を格納する</summary>
        [PLParam(TextKey = "MergeCreateNewMesh",
                 Description = "結果を新規オブジェクトに入れる。false で基準へ直接結合", Required = true)]
        public bool CreateNewMesh { get; }

        public MergeMeshesCommand(int modelIndex, int[] masterIndices, int baseMasterIndex, bool createNewMesh)
            : base(modelIndex)
        {
            MasterIndices    = masterIndices;
            BaseMasterIndex  = baseMasterIndex;
            CreateNewMesh    = createNewMesh;
        }
    }

    // ================================================================
    // ブーリアン演算
    // ================================================================

    /// <summary>
    /// 2 つのメッシュオブジェクトにブーリアン演算（和 / 差 / 積）を行う。
    /// 演算は A のローカル空間で行い、結果も A の姿勢を引き継ぐ。
    ///
    /// CreateNewMesh が true なら新規メッシュオブジェクトに結果を格納し、
    /// A / B はそのまま残す。false なら A の中身を結果で置き換える。
    /// DeleteSourceB が true なら B を削除する。
    ///
    /// スキンドメッシュは対象にできない（ボーンウェイトが失われるため）。
    /// </summary>
    public class BooleanMeshCommand : PanelCommand
    {
        /// <summary>左辺（基準）オブジェクトの MasterIndex。差では削られる側。</summary>
        [PLParam(TextKey = "BooleanAMasterIndex",
                 Description = "左辺（基準）オブジェクトの masterIndex。差では削られる側", Required = true)]
        public int AMasterIndex { get; }

        /// <summary>右辺オブジェクトの MasterIndex。差では削る側。</summary>
        [PLParam(TextKey = "BooleanBMasterIndex",
                 Description = "右辺オブジェクトの masterIndex。差では削る側", Required = true)]
        public int BMasterIndex { get; }

        /// <summary>演算の種類</summary>
        [PLParam(TextKey = "BooleanOpKind",
                 Description = "和 / 差 / 積のどれを行うか", Required = true)]
        public Poly_Ling.Ops.BooleanOpKind Op { get; }

        /// <summary>true: 新規メッシュオブジェクトに結果を格納する</summary>
        [PLParam(TextKey = "BooleanCreateNewMesh",
                 Description = "結果を新規オブジェクトに入れる", Required = true)]
        public bool CreateNewMesh { get; }

        /// <summary>true: 演算後に B を削除する</summary>
        [PLParam(TextKey = "BooleanDeleteSourceB",
                 Description = "演算後に右辺オブジェクトを削除する", Required = true)]
        public bool DeleteSourceB { get; }

        /// <summary>true: 演算後に同一位置頂点をマージする</summary>
        [PLParam(TextKey = "BooleanMergeVertices",
                 Description = "演算後に同一位置の頂点を結合する", Required = true)]
        public bool MergeVertices { get; }

        /// <summary>同一位置頂点マージのしきい値</summary>
        [PLParam(TextKey = "BooleanMergeThreshold",
                 Description = "同一位置とみなす距離のしきい値",
                 LimitKey = "Boolean.MergeThreshold", Required = true)]
        public float MergeThreshold { get; }

        /// <summary>平面の同一判定の許容量（pb_CSG の epsilon）</summary>
        [PLParam(TextKey = "BooleanEpsilon",
                 Description = "平面の同一判定の許容量。0 以下を渡すと BooleanOps.DefaultEpsilon が使われる", Required = true)]
        public float Epsilon { get; }

        public BooleanMeshCommand(
            int modelIndex,
            int aMasterIndex,
            int bMasterIndex,
            Poly_Ling.Ops.BooleanOpKind op,
            bool createNewMesh,
            bool deleteSourceB,
            bool mergeVertices,
            float mergeThreshold,
            float epsilon)
            : base(modelIndex)
        {
            AMasterIndex   = aMasterIndex;
            BMasterIndex   = bMasterIndex;
            Op             = op;
            CreateNewMesh  = createNewMesh;
            DeleteSourceB  = deleteSourceB;
            MergeVertices  = mergeVertices;
            MergeThreshold = mergeThreshold;
            Epsilon        = epsilon;
        }
    }

    // ================================================================
    // 頂点・辺・面の選択
    // ================================================================

    /// <summary>
    /// 頂点・辺・面・線分をインデックス指定で選択する。
    ///
    /// 実処理は MoveToolHandler / PlayerSelectionOps が持つ。クリック経路と同じ
    /// 「スナップショット → 選択の書き換え → 頂点への展開 → Undo 記録」を通すため、
    /// このコマンドは対象と選ばせる要素だけを運ぶ。
    ///
    /// 【要素とメッシュの対応】
    ///   要素の索引はメッシュ内ローカル番号なので、どのメッシュのものかを
    ///   同じ並び・同じ長さの *MeshIndices で対にして渡す。
    ///     VertexIndices[i] は VertexMeshIndices[i] のメッシュの頂点
    ///     FaceIndices[i]   は FaceMeshIndices[i]   のメッシュの面
    ///     LineIndices[i]   は LineMeshIndices[i]   のメッシュの線分
    ///   辺だけは [v1a, v2a, v1b, v2b, ...] と 2 個 1 組で平坦化してあるので、
    ///   EdgeMeshIndices の長さは EdgePairs の半分になる。
    ///     EdgePairs[2i], EdgePairs[2i+1] は EdgeMeshIndices[i] のメッシュの辺
    ///   入れ子の配列を持てないための形。PanelCommandFactory は平坦な int[] しか
    ///   組み立てられない。
    ///
    /// 【操作の種類】
    ///   Op = Replace のとき、MasterIndices に挙げたメッシュの選択を先に消す。
    ///   1 メッシュだけ消すとほかのメッシュに残った選択が画面に出たままになる
    ///   （GPU のフラグは MeshContext 単位で立つため）。クリック経路の
    ///   ClearAllTargetsSilent と同じ範囲を明示で受ける形。
    ///   Op = Remove は列挙要素を選択から外す。Ctrl クリックが既選択に当たったとき
    ///   （ApplyElementClick の解除分岐）がこれに落ちる。
    ///   Op = Toggle は列挙要素を 1 個ずつ反転する。Ctrl の矩形・投げ縄選択が
    ///   これに落ちる（範囲内の既選択は外れ、未選択は入る）。
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    public class SelectElementsCommand : PanelCommand
    {
        /// <summary>
        /// 選択の書き換え方。
        /// Replace = MasterIndices の選択を消してから列挙要素を選ぶ。
        /// Add     = 列挙要素を足す。
        /// Remove  = 列挙要素を外す。
        /// Toggle  = 列挙要素を 1 個ずつ反転する。
        /// </summary>
        public enum SelectOp { Replace, Add, Remove, Toggle }

        /// <summary>Replace のときに選択を消す対象メッシュの範囲</summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "非加算のときに選択を消す対象の masterIndex 配列", Required = true)]
        public int[]   MasterIndices     { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds         { get; }

        /// <summary>選択する頂点の索引</summary>
        [PLParam(TextKey = "SelectVertexIndices",
                 Description = "選択する頂点の索引。null / 空で頂点を足さない", Required = true)]
        public int[]   VertexIndices     { get; }

        /// <summary>VertexIndices と同じ並び・同じ長さ。各頂点が属する masterIndex</summary>
        [PLParam(TextKey = "SelectVertexMeshIndices",
                 Description = "VertexIndices と同じ並び・同じ長さの masterIndex", Required = true)]
        public int[]   VertexMeshIndices { get; }

        /// <summary>選択する辺のフラット配列 [v1a, v2a, v1b, v2b, ...]</summary>
        [PLParam(TextKey = "SelectEdgePairs",
                 Description = "選択する辺を [v1, v2] の並びで平坦化したもの。null / 空で辺を足さない", Required = true)]
        public int[]   EdgePairs         { get; }

        /// <summary>EdgePairs の組ごとの masterIndex。長さは EdgePairs の半分</summary>
        [PLParam(TextKey = "SelectEdgeMeshIndices",
                 Description = "EdgePairs の組ごとの masterIndex。長さは EdgePairs の半分", Required = true)]
        public int[]   EdgeMeshIndices   { get; }

        /// <summary>選択する面の索引</summary>
        [PLParam(TextKey = "SelectFaceIndices",
                 Description = "選択する面の索引。null / 空で面を足さない", Required = true)]
        public int[]   FaceIndices       { get; }

        /// <summary>FaceIndices と同じ並び・同じ長さ。各面が属する masterIndex</summary>
        [PLParam(TextKey = "SelectFaceMeshIndices",
                 Description = "FaceIndices と同じ並び・同じ長さの masterIndex", Required = true)]
        public int[]   FaceMeshIndices   { get; }

        /// <summary>選択する線分の索引（MeshObject.Faces[] の添字。VertexCount==2）</summary>
        [PLParam(TextKey = "SelectLineIndices",
                 Description = "選択する線分の索引。null / 空で線分を足さない", Required = true)]
        public int[]   LineIndices       { get; }

        /// <summary>LineIndices と同じ並び・同じ長さ。各線分が属する masterIndex</summary>
        [PLParam(TextKey = "SelectLineMeshIndices",
                 Description = "LineIndices と同じ並び・同じ長さの masterIndex", Required = true)]
        public int[]   LineMeshIndices   { get; }

        /// <summary>選択の書き換え方</summary>
        [PLParam(TextKey = "SelectOp",
                 Description = "選択の書き換え方。Replace / Add / Remove / Toggle。既定は Replace")]
        public SelectOp Op                { get; }

        public SelectElementsCommand(
            int modelIndex, int[] masterIndices,
            int[] vertexIndices, int[] vertexMeshIndices,
            int[] edgePairs,     int[] edgeMeshIndices,
            int[] faceIndices,   int[] faceMeshIndices,
            int[] lineIndices,   int[] lineMeshIndices,
            SelectOp op = SelectOp.Replace,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices     = masterIndices ?? System.Array.Empty<int>();
            ObjectIds         = objectIds;
            VertexIndices     = vertexIndices;
            VertexMeshIndices = vertexMeshIndices;
            EdgePairs         = edgePairs;
            EdgeMeshIndices   = edgeMeshIndices;
            FaceIndices       = faceIndices;
            FaceMeshIndices   = faceMeshIndices;
            LineIndices       = lineIndices;
            LineMeshIndices   = lineMeshIndices;
            Op                = op;
        }
    }

    // ================================================================
    // 頂点移動
    // ================================================================

    /// <summary>
    /// 現在の選択頂点をデルタ値で移動する。Undo記録付き。
    ///
    /// 実処理は MoveToolHandler が持つ。マウス経路・数値入力経路と同じ
    /// UpdateAffectedVertices → BeginMove → ApplyDelta → EndMove を通すため、
    /// このコマンドは対象・移動量・マグネット設定だけを運ぶ。
    ///
    /// 対象頂点は「選択メッシュの選択要素」で、辺・面・線分の選択は
    /// SelectionState.Mode に従って頂点へ展開される。
    ///
    /// マグネットの 4 件はハンドラの UI 状態と同名だが、こちらが正典として
    /// 実行時に適用され、実行後に UI の値へ戻される。1 呼び出しが UI 状態に
    /// 依存しないようにするため（MCP の自己完結）。
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    public class MoveSelectedVerticesCommand : PanelCommand
    {
        public enum CoordSpace { Local, World }

        /// <summary>対象 MeshContext の MasterIndex 配列</summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[]        MasterIndices      { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[]      ObjectIds          { get; }

        /// <summary>移動量</summary>
        [PLParam(TextKey = "MoveDelta",
                 Description = "選択頂点の移動量", Required = true)]
        public Vector3      Delta              { get; }

        /// <summary>
        /// Delta の座標空間。
        /// Local は MasterIndices[0] のローカル空間として解釈し、そのメッシュの
        /// WorldMatrix でワールドへ変換する。対象ごとに行列が違うため、基準は
        /// 先頭の 1 本に固定する。
        /// </summary>
        [PLParam(TextKey = "MoveCoordSpace",
                 Description = "Delta の座標空間。Local は MasterIndices[0] のローカル空間", Required = true)]
        public CoordSpace   Space              { get; }

        /// <summary>マグネットを使うか</summary>
        [PLParam(TextKey = "MoveUseMagnet",
                 Description = "選択外の周辺頂点も減衰させて引きずる。既定は false")]
        public bool         UseMagnet          { get; }

        /// <summary>マグネットの影響半径</summary>
        [PLParam(TextKey = "MoveMagnetRadius",
                 Description = "マグネットの影響半径。UseMagnet が false のときは使わない",
                 LimitKey = "Move.MagnetRadius")]
        public float        MagnetRadius       { get; }

        /// <summary>マグネットの減衰の形</summary>
        [PLParam(TextKey = "MoveMagnetFalloff",
                 Description = "マグネットの減衰の形。既定は Smooth")]
        public FalloffType  MagnetFalloff      { get; }

        /// <summary>マグネットの距離計算方式</summary>
        [PLParam(TextKey = "MoveMagnetDistanceMode",
                 Description = "マグネットの距離計算方式。Euclidean / Link。既定は Euclidean")]
        public DistanceMode MagnetDistanceMode { get; }

        /// <summary>
        /// 移動後に法線を再計算するか。
        /// マウス経路は再計算しないので、既定の false で同一結果になる。
        /// </summary>
        [PLParam(TextKey = "MoveRecalcNormals",
                 Description = "移動後に頂点法線を再計算する。既定は false")]
        public bool         RecalcNormals      { get; }

        public MoveSelectedVerticesCommand(
            int modelIndex, int[] masterIndices,
            Vector3 delta, CoordSpace space,
            bool recalcNormals = false,
            bool useMagnet = false,
            float magnetRadius = 0.5f,
            FalloffType magnetFalloff = FalloffType.Smooth,
            DistanceMode magnetDistanceMode = DistanceMode.Euclidean,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices      = masterIndices ?? System.Array.Empty<int>();
            ObjectIds          = objectIds;
            Delta              = delta;
            Space              = space;
            RecalcNormals      = recalcNormals;
            UseMagnet          = useMagnet;
            MagnetRadius       = magnetRadius;
            MagnetFalloff      = magnetFalloff;
            MagnetDistanceMode = magnetDistanceMode;
        }
    }

    // ================================================================
    // ピボット移動
    // ================================================================

    /// <summary>
    /// ピボット（原点）をデルタ値で移動する。Undo記録付き。
    /// 対象の BoneTransform.Position を Delta 方向へ動かし、対象メッシュ（非スキンの
    /// MeshFilter）の頂点を「開始ワールド位置を保つ」よう再局所化する。直接の子は
    /// ワールド位置を保つよう補償される。
    ///
    /// 実処理は ObjectMoveTool（OriginOnly）が持つ。マウス経路と同じ実装を通すため、
    /// このコマンドは対象と移動量だけを運ぶ。
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    public class MovePivotCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex 配列</summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[]      MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[]    ObjectIds     { get; }

        /// <summary>ピボットの移動量</summary>
        [PLParam(TextKey = "PivotDelta",
                 Description = "ピボット（原点）の移動量", Required = true)]
        public Vector3    Delta         { get; }

        /// <summary>
        /// Delta の座標空間。
        /// Local は MasterIndices[0] のローカル空間として解釈し、そのメッシュの
        /// WorldMatrix でワールドへ変換する。対象ごとに行列が違うため、基準は
        /// 先頭の 1 本に固定する。
        /// </summary>
        [PLParam(TextKey = "PivotCoordSpace",
                 Description = "Delta の座標空間。Local は MasterIndices[0] のローカル空間", Required = true)]
        public MoveSelectedVerticesCommand.CoordSpace Space { get; }

        public MovePivotCommand(
            int modelIndex, int[] masterIndices,
            Vector3 delta, MoveSelectedVerticesCommand.CoordSpace space,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            Delta         = delta;
            Space         = space;
        }
    }

    // ================================================================
    // スカルプトストローク
    // ================================================================

    /// <summary>
    /// スカルプトブラシを一連のワールド座標に沿って適用する。Undo記録付き。
    ///
    /// 実処理は SculptTool が持つ。マウス経路と同じ ApplyStrokeToMesh /
    /// CommitStroke を通すため、このコマンドは対象・点列・ブラシ設定だけを運ぶ。
    ///
    /// 【なぜワールド座標か】
    ///   マウス経路（ApplyBrush）は 1 点のブラシ中心を選択メッシュ全部へ掛け、
    ///   メッシュごとに WorldToLocal で変換する。点列をローカル座標 1 組で持つと
    ///   複数メッシュを 1 コマンドで表せない。ローカル化は適用側の仕事とする。
    ///
    /// 【ViewDirections】
    ///   BrushCenters と同じ並び・同じ長さのワールド視線方向。Draw モードの
    ///   反転補正（ApplyStrokeToMesh の viewDirLocal）に使う。空にすると補正を
    ///   行わないため、マウス経路と結果が変わる点に注意する。
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    public class SculptStrokeCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex 配列</summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[]        MasterIndices  { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[]      ObjectIds      { get; }

        /// <summary>ブラシ中心の列（ワールド空間）</summary>
        [PLParam(TextKey = "SculptBrushCenters",
                 Description = "ブラシ中心をストローク順に並べたもの（ワールド座標）", Required = true)]
        public Vector3[]    BrushCenters  { get; }

        /// <summary>視線方向の列（ワールド空間）。BrushCenters と同じ長さ。空で補正なし</summary>
        [PLParam(TextKey = "SculptViewDirections",
                 Description = "BrushCenters と同じ並び・同じ長さのワールド視線方向。空で Draw の反転補正を行わない")]
        public Vector3[]    ViewDirections { get; }

        /// <summary>スカルプトモード</summary>
        [PLParam(TextKey = "SculptMode",
                 Description = "ブラシの効き方", Required = true)]
        public SculptMode   Mode          { get; }

        /// <summary>ブラシ半径（ローカル空間単位）</summary>
        [PLParam(TextKey = "SculptBrushRadius",
                 Description = "ブラシ半径（対象のローカル空間単位）",
                 LimitKey = "Sculpt.BrushRadius", Required = true)]
        public float        BrushRadius   { get; }

        /// <summary>強度（0〜1）</summary>
        [PLParam(TextKey = "SculptStrength",
                 Description = "1 ストロークあたりの効きの強さ",
                 LimitKey = "Sculpt.Strength", Required = true)]
        public float        Strength      { get; }

        /// <summary>反転フラグ</summary>
        [PLParam(TextKey = "SculptInvert",
                 Description = "凹凸を反転する。既定は false")]
        public bool         Invert        { get; }

        /// <summary>フォールオフ種別</summary>
        [PLParam(TextKey = "SculptFalloff",
                 Description = "ブラシ中心からの減衰の形。既定は Gaussian")]
        public FalloffType  Falloff       { get; }

        /// <summary>ストローク終了後に法線を再計算するか</summary>
        [PLParam(TextKey = "SculptRecalcNormals",
                 Description = "ストローク後に頂点法線を再計算する。既定は true")]
        public bool         RecalcNormals { get; }

        public SculptStrokeCommand(
            int modelIndex, int[] masterIndices,
            Vector3[] brushCenters,
            SculptMode mode, float brushRadius, float strength,
            bool invert = false,
            FalloffType falloff = FalloffType.Gaussian,
            bool recalcNormals = true,
            Vector3[] viewDirections = null,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices  = masterIndices ?? System.Array.Empty<int>();
            ObjectIds      = objectIds;
            ViewDirections = viewDirections;
            BrushCenters  = brushCenters;
            Mode          = mode;
            BrushRadius   = brushRadius;
            Strength      = strength;
            Invert        = invert;
            Falloff       = falloff;
            RecalcNormals = recalcNormals;
        }
    }

    // ================================================================
    // 詳細選択（Advanced Select）
    // ================================================================

    /// <summary>
    /// トポロジーベースの詳細選択を実行する。
    /// Mode に応じて使用する Seed フィールドが異なる。
    ///   Connected   : SeedVertexIndex >= 0 → 頂点起点
    ///                 SeedEdgeV1/V2  >= 0 → 辺起点
    ///                 SeedFaceIndex  >= 0 → 面起点
    ///   Belt        : SeedEdgeV1/V2（辺ペア必須）
    ///   EdgeLoop    : SeedEdgeV1/V2（辺ペア必須）
    ///   ShortestPath: SeedVertexIndex（始点）+ EndVertexIndex（終点）
    /// </summary>
    public class AdvancedSelectCommand : PanelCommand
    {
        /// <summary>
        /// 対象 MeshContext の MasterIndex 配列。
        /// 実処理（AdvancedSelectTool）は編集対象メッシュ 1 本にしか効かないため、
        /// 受け口は「1 個で、それが編集対象と一致すること」を要求する。
        /// 配列にしてあるのは他コマンドと形を揃えて ObjectIds と対にするため。
        /// </summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個", Required = true)]
        public int[]              MasterIndices     { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[]            ObjectIds         { get; }

        /// <summary>選択モード</summary>
        [PLParam(TextKey = "AdvancedSelectMode",
                 Description = "選択の広げ方。使う Seed がモードごとに変わる", Required = true)]
        public AdvancedSelectMode Mode              { get; }

        // ── Seed ──────────────────────────────────────────────────
        /// <summary>頂点起点インデックス（不使用時 -1）</summary>
        [PLParam(TextKey = "SeedVertexIndex",
                 Description = "起点にする頂点の索引。-1 で不使用")]
        public int                SeedVertexIndex   { get; }

        /// <summary>辺起点 V1（不使用時 -1）</summary>
        [PLParam(TextKey = "SeedEdgeV1",
                 Description = "起点にする辺の片側の頂点索引。-1 で不使用")]
        public int                SeedEdgeV1        { get; }

        /// <summary>辺起点 V2（不使用時 -1）</summary>
        [PLParam(TextKey = "SeedEdgeV2",
                 Description = "起点にする辺のもう片側の頂点索引。-1 で不使用")]
        public int                SeedEdgeV2        { get; }

        /// <summary>面起点インデックス（不使用時 -1）</summary>
        [PLParam(TextKey = "SeedFaceIndex",
                 Description = "起点にする面の索引。-1 で不使用")]
        public int                SeedFaceIndex     { get; }

        /// <summary>ShortestPath 終点インデックス（他モードでは無視）</summary>
        [PLParam(TextKey = "EndVertexIndex",
                 Description = "ShortestPath の終点頂点索引。他モードでは無視。-1 で不使用")]
        public int                EndVertexIndex    { get; }

        // ── 出力フラグ ──────────────────────────────────────────────
        //
        // モードによっては効かないものがある。実処理を持つ AdvancedSelectTool 側が
        // 意図的に外しているためで、EdgeLoop は頂点（EdgeLoopSelectMode.cs:28）、
        // ShortestPath は辺（ShortestPathSelectMode.cs:42）が対象外。
        [PLParam(TextKey = "AdvancedSelectVertices",
                 Description = "結果を頂点選択へ入れる。既定は true")]
        public bool               SelectVertices    { get; }

        [PLParam(TextKey = "AdvancedSelectEdges",
                 Description = "結果を辺選択へ入れる。既定は false")]
        public bool               SelectEdges       { get; }

        [PLParam(TextKey = "AdvancedSelectFaces",
                 Description = "結果を面選択へ入れる。既定は false")]
        public bool               SelectFaces       { get; }

        /// <summary>false = 既存選択をクリアしてから選択</summary>
        [PLParam(TextKey = "AdvancedSelectAdditive",
                 Description = "既存の選択へ足す。false で置き換える。既定は false")]
        public bool               Additive          { get; }

        /// <summary>
        /// EdgeLoop モードの方向一致閾値（cos値）。
        /// 既定は AdvancedSelectSettings.cs:41 の実既定と同じ 0.7。
        /// </summary>
        [PLParam(TextKey = "EdgeLoopThreshold",
                 Description = "EdgeLoop の方向一致しきい値（cos 値）。既定は 0.5",
                 LimitKey = "AdvancedSelect.EdgeLoopThreshold")]
        public float              EdgeLoopThreshold { get; }

        public AdvancedSelectCommand(
            int modelIndex, int[] masterIndices,
            AdvancedSelectMode mode,
            int seedVertexIndex   = -1,
            int seedEdgeV1        = -1,
            int seedEdgeV2        = -1,
            int seedFaceIndex     = -1,
            int endVertexIndex    = -1,
            bool selectVertices   = true,
            bool selectEdges      = false,
            bool selectFaces      = false,
            bool additive         = false,
            float edgeLoopThreshold = 0.7f,
            ulong[] objectIds       = null)
            : base(modelIndex)
        {
            MasterIndices     = masterIndices ?? System.Array.Empty<int>();
            ObjectIds         = objectIds;
            Mode              = mode;
            SeedVertexIndex   = seedVertexIndex;
            SeedEdgeV1        = seedEdgeV1;
            SeedEdgeV2        = seedEdgeV2;
            SeedFaceIndex     = seedFaceIndex;
            EndVertexIndex    = endVertexIndex;
            SelectVertices    = selectVertices;
            SelectEdges       = selectEdges;
            SelectFaces       = selectFaces;
            Additive          = additive;
            EdgeLoopThreshold = edgeLoopThreshold;
        }
    }

    /// <summary>
    /// 属性で頂点を選ぶ（クリック非依存）。Undo記録付き。
    ///
    /// 実処理は AdvancedSelectTool.ExecuteAttributeSelect が持つ。パネルの「実行」
    /// ボタンと同じ経路を通すため、このコマンドは対象とモードとしきい値だけを運ぶ。
    ///
    /// 【AdvancedSelectCommand との違い】
    ///   あちらは起点（Seed）から選択を広げるモード用で、GPU ホバーが返した要素を
    ///   種にする。こちらは種を持たず、メッシュ全体の属性を走査する。
    ///   AdvancedSelectTool.IsAttributeMode が true を返すモードだけを受け付ける。
    ///
    /// 【LimitToCurrentSelection の効き方】
    ///   OFF … 判定に一致した頂点を AddToSelection に従って追加／削除する。
    ///   ON かつ AddToSelection = true  … 現在の選択のうち一致しなかった頂点を解除する（絞り込み）。
    ///   ON かつ AddToSelection = false … 現在の選択のうち一致した頂点を解除する。
    ///
    /// ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    /// リモート経由の場合、サーバ側で「その位置に本当にそのIDのオブジェクトが
    /// あるか」を照合してから適用する（リスト構造変更によるズレの検出）。
    /// ローカル発行時は null / 空でよい（照合をスキップする）。
    /// </summary>
    public class AdvancedSelectByAttributeCommand : PanelCommand
    {
        /// <summary>
        /// 対象 MeshContext の MasterIndex 配列。
        /// 実処理は編集対象メッシュ 1 本にしか効かないため、受け口は
        /// 「1 個で、それが編集対象と一致すること」を要求する。
        /// </summary>
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個", Required = true)]
        public int[]              MasterIndices           { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[]            ObjectIds               { get; }

        /// <summary>属性モード。UvNormalCount / NearAxis のいずれか</summary>
        [PLParam(TextKey = "AttributeSelectMode",
                 Description = "属性の種類。UvNormalCount / NearAxis", Required = true)]
        public AdvancedSelectMode Mode                    { get; }

        /// <summary>true = 選択に追加、false = 選択から削除</summary>
        [PLParam(TextKey = "AttributeSelectAdd",
                 Description = "一致した頂点を選択へ足す。false で選択から外す。既定は true")]
        public bool               AddToSelection          { get; }

        /// <summary>
        /// UvNormalCount モードのしきい値。
        /// max(Vertex.UVs.Count, Vertex.Normals.Count) がこの値より大きい頂点を選ぶ。
        /// </summary>
        [PLParam(TextKey = "AttributeUvNormalCountThreshold",
                 Description = "UvNormalCount のしきい値。UV／法線の本数がこれを超える頂点を選ぶ")]
        public int                UvNormalCountThreshold  { get; }

        /// <summary>NearAxis モードの対称軸</summary>
        [PLParam(TextKey = "AttributeAxisKind",
                 Description = "NearAxis の基準軸。X / Y / Z")]
        public SymmetryAxis       AxisKind                { get; }

        /// <summary>NearAxis モードの距離しきい値（軸平面からの距離）</summary>
        [PLParam(TextKey = "AttributeAxisDistanceThreshold",
                 Description = "NearAxis の距離しきい値。軸平面からこの距離以内の頂点を選ぶ")]
        public float              AxisDistanceThreshold   { get; }

        /// <summary>現在の選択の中だけを対象にするか</summary>
        [PLParam(TextKey = "AttributeLimitToCurrentSelection",
                 Description = "現在の選択の中だけを対象にする。既定は false")]
        public bool               LimitToCurrentSelection { get; }

        public AdvancedSelectByAttributeCommand(
            int modelIndex, int[] masterIndices,
            AdvancedSelectMode mode,
            bool addToSelection             = true,
            int uvNormalCountThreshold      = 0,
            SymmetryAxis axisKind           = SymmetryAxis.X,
            float axisDistanceThreshold     = 0.00001f,
            bool limitToCurrentSelection    = false,
            ulong[] objectIds               = null)
            : base(modelIndex)
        {
            MasterIndices           = masterIndices ?? System.Array.Empty<int>();
            ObjectIds               = objectIds;
            Mode                    = mode;
            AddToSelection          = addToSelection;
            UvNormalCountThreshold  = uvNormalCountThreshold;
            AxisKind                = axisKind;
            AxisDistanceThreshold   = axisDistanceThreshold;
            LimitToCurrentSelection = limitToCurrentSelection;
        }
    }

    // ================================================================
    // MeshFilter → Skinned 変換
    // ================================================================

    /// <summary>
    /// MeshFilter オブジェクト群をボーン+スキンドメッシュ構造に変換する。
    /// Undo 記録付き。変換後に GPU バッファを再構築する。
    /// </summary>
    public class ConvertMeshFilterToSkinnedCommand : PanelCommand
    {
        /// <summary>回転ありボーンの軸をPMX軸 (Y→X) に入替える</summary>
        [PLParam(TextKey = "SwapAxisForRotated",
                 Description = "回転ありボーンの軸を PMX 軸（Y→X）に入れ替える。既定は false")]
        public bool SwapAxisForRotated  { get; }

        /// <summary>回転なしボーンを X軸上向き・Y軸横向きに設定する</summary>
        [PLParam(TextKey = "SetAxisForIdentity",
                 Description = "回転なしボーンを X 軸上向き・Y 軸横向きにする。既定は false")]
        public bool SetAxisForIdentity  { get; }

        /// <summary>
        /// ミラー分岐ルート配下の「ミラー設定漏れ」を許容し、
        /// ミラー側メッシュを実体側から生成して実体化する。既定は true。
        /// </summary>
        [PLParam(TextKey = "TolerantMirrorBranch",
                 Description = "ミラー分岐ルート配下の設定漏れを許容して実体化する。既定は true")]
        public bool TolerantMirrorBranch { get; }

        public ConvertMeshFilterToSkinnedCommand(
            int modelIndex,
            bool swapAxisForRotated = false,
            bool setAxisForIdentity = false,
            bool tolerantMirrorBranch = true)
            : base(modelIndex)
        {
            SwapAxisForRotated   = swapAxisForRotated;
            SetAxisForIdentity   = setAxisForIdentity;
            TolerantMirrorBranch = tolerantMirrorBranch;
        }
    }

    // ================================================================
    // 描画オブジェクト単位の種別変換（MeshFilter 系 ⇔ SkinnedMesh 系）
    // ================================================================

    /// <summary>
    /// 選んだ描画オブジェクトのウェイトを破棄して MeshFilter 系へ戻す。
    ///
    /// 頂点はスキンド時にワールド（バインド）空間へ焼かれているため、
    /// 変換先の WorldMatrix の逆行列でローカル化し直す（SkinKindConverter）。
    /// ボーンの生成・破棄は行わない。
    /// </summary>
    public class ConvertToMeshFilterCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        /// <summary>階層の扱い。既定はルート直下へ移す。</summary>
        [PLParam(TextKey = "UnskinParentMode",
                 Description = "変換後の階層の扱い。既定は MoveToRoot")]
        public UnskinParentMode ParentMode { get; }

        public ConvertToMeshFilterCommand(
            int modelIndex, int[] masterIndices,
            UnskinParentMode parentMode = UnskinParentMode.MoveToRoot)
            : base(modelIndex)
        {
            MasterIndices = masterIndices;
            ParentMode    = parentMode;
        }
    }

    /// <summary>
    /// 選んだ描画オブジェクトを、指定ボーンへウェイト 1.0 でバインドして
    /// SkinnedMesh 系にする。ボーンの生成は行わない（既存ボーンへ付ける）。
    /// </summary>
    public class ConvertToSkinnedCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] MasterIndices { get; }

        /// <summary>バインド先ボーンの MeshContextList 索引。</summary>
        [PLParam(TextKey = "BindBoneMasterIndex",
                 Description = "ウェイト 1.0 でバインドする先のボーンの masterIndex", Required = true)]
        public int BoneMasterIndex { get; }

        public ConvertToSkinnedCommand(int modelIndex, int[] masterIndices, int boneMasterIndex)
            : base(modelIndex)
        {
            MasterIndices   = masterIndices;
            BoneMasterIndex = boneMasterIndex;
        }
    }

    /// <summary>
    /// ボーンの左右対応（MirrorBoneIndex）を、ボーン名の左右から補完する。
    ///
    /// スキンド変換が確定させた値（-1 以外）は上書きしない。
    /// PMX インポート直後のようにボーンが全て -1 のモデルで、
    /// ミラー生成前に一度だけ実行する用途。
    /// </summary>
    public class ResolveMirrorBoneIndexCommand : PanelCommand
    {
        public ResolveMirrorBoneIndexCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // スキンウェイト一括操作
    // ================================================================

    /// <summary>選択中の描画メッシュ全頂点に指定ウェイトを一括塗りつぶす（Flood）</summary>
    public class FloodSkinWeightCommand : PanelCommand
    {
        [PLParam(TextKey = "SkinWeightTargetBone",
                 Description = "塗り対象のボーンの masterIndex", Required = true)]
        public int                          TargetBoneMaster { get; }

        [PLParam(TextKey = "SkinWeightPaintMode",
                 Description = "塗り方。Replace / Add / Scale / Smooth", Required = true)]
        public Poly_Ling.UI.SkinWeightPaintMode PaintMode    { get; }

        [PLParam(TextKey = "SkinWeightValue",
                 Description = "書き込むウェイト値", Required = true)]
        public float                        WeightValue      { get; }

        [PLParam(TextKey = "SkinWeightStrength",
                 Description = "適用の強さ",
                 LimitKey = "SkinWeight.Strength", Required = true)]
        public float                        Strength         { get; }
        public FloodSkinWeightCommand(int modelIndex, int targetBoneMaster,
            Poly_Ling.UI.SkinWeightPaintMode paintMode, float weightValue, float strength)
            : base(modelIndex)
        {
            TargetBoneMaster = targetBoneMaster;
            PaintMode        = paintMode;
            WeightValue      = weightValue;
            Strength         = strength;
        }
    }

    /// <summary>選択中の描画メッシュ全頂点のボーンウェイトを正規化する（Normalize）</summary>
    public class NormalizeSkinWeightCommand : PanelCommand
    {
        public NormalizeSkinWeightCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>選択中の描画メッシュ全頂点の微小ウェイトを除去する（Prune）</summary>
    public class PruneSkinWeightCommand : PanelCommand
    {
        [PLParam(TextKey = "SkinWeightPruneThreshold",
                 Description = "この値より小さいウェイトを除去する", Required = true)]
        public float Threshold { get; }
        public PruneSkinWeightCommand(int modelIndex, float threshold)
            : base(modelIndex) { Threshold = threshold; }
    }

    /// <summary>
    /// 選択頂点のボーンウェイトを、指定した最大 4 組（ボーン MasterIndex, ウェイト値）で
    /// 直接上書きする。数値入力パネル（PlayerSkinWeightNumericSubPanel）から送られる。
    /// BoneMasters が負値のスロットは未使用として weight 0 で埋める。
    /// 正規化はパネル側のボタンで行うため、ここでは入力値をそのまま書き込む。
    /// </summary>
    public class SetSkinWeightNumericCommand : PanelCommand
    {
        /// <summary>長さ 4。ボーンの MasterIndex。負値は未使用スロット。</summary>
        [PLParam(TextKey = "SkinWeightBoneMasters",
                 Description = "長さ 4。ボーンの masterIndex。負値は未使用スロット", Required = true)]
        public int[] BoneMasters { get; }

        /// <summary>長さ 4。各スロットのウェイト値。</summary>
        [PLParam(TextKey = "SkinWeightWeights",
                 Description = "長さ 4。各スロットのウェイト値", Required = true)]
        public float[] Weights { get; }

        public SetSkinWeightNumericCommand(int modelIndex, int[] boneMasters, float[] weights)
            : base(modelIndex)
        {
            BoneMasters = boneMasters;
            Weights     = weights;
        }
    }

    /// <summary>
    /// 対象メッシュ全件の全頂点についてボーンウェイトを正規化する。
    /// 合計が 1 でない頂点は GPU スキニングで原点方向へ寄り見た目が崩れるため、
    /// 読み込んだモデルや過去の編集で壊れた箇所をまとめて直す。
    /// </summary>
    public class NormalizeAllSkinWeightsCommand : PanelCommand
    {
        public NormalizeAllSkinWeightsCommand(int modelIndex) : base(modelIndex) { }
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
    public class ApplyBlendCommand : PanelCommand
    {
        /// <summary>1 コマンドで受け付けるソースの上限</summary>
        public const int MaxSources = 6;

        /// <summary>ソース一覧（最大 MaxSources 件）</summary>
        [PLParam(TextKey = "MeshBlendSources",
                 Description = "ブレンド元の指定。MaxSources 件まで", Required = true)]
        public BlendSourceSpec[] Sources { get; }

        /// <summary>書き込み先 MeshContext の MasterIndex（ModelIndex のモデル内）</summary>
        [PLParam(TextKey = "MeshBlendDestMasterIndex",
                 Description = "結果を書き込む描画オブジェクトの masterIndex", Required = true)]
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

        public ApplyBlendCommand(
            int modelIndex,
            BlendSourceSpec[] sources,
            int destMasterIndex,
            bool createNewObject      = false,
            bool recalculateNormals   = true,
            bool selectedVerticesOnly = false,
            Poly_Ling.UI.BlendMatchMode matchMode = Poly_Ling.UI.BlendMatchMode.Index)
            : base(modelIndex)
        {
            Sources              = sources ?? System.Array.Empty<BlendSourceSpec>();
            DestMasterIndex      = destMasterIndex;
            CreateNewObject      = createNewObject;
            RecalculateNormals   = recalculateNormals;
            SelectedVerticesOnly = selectedVerticesOnly;
            MatchMode            = matchMode;
        }
    }

    // ================================================================
    // シュリンカー
    // ================================================================

    /// <summary>
    /// ビフォーオブジェクトの頂点をアフターオブジェクトへ向けて移動する。
    /// 衝突対象オブジェクト群と交差した頂点はその位置で停止する。
    /// バックアップ作成 + Undo 記録付き。
    /// </summary>
    public class ApplyShrinkCommand : PanelCommand
    {
        /// <summary>ビフォー（変形対象）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ShrinkBeforeMasterIndex",
                 Description = "変形させる描画オブジェクトの masterIndex", Required = true)]
        public int   BeforeMasterIndex     { get; }

        /// <summary>アフター（目標形状）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ShrinkAfterMasterIndex",
                 Description = "目標形状の描画オブジェクトの masterIndex", Required = true)]
        public int   AfterMasterIndex      { get; }

        /// <summary>衝突対象 MeshContext の MasterIndex 配列</summary>
        [PLParam(TextKey = "ShrinkColliderMasterIndices",
                 Description = "衝突対象の描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] ColliderMasterIndices { get; }

        /// <summary>シュリンク量 [0, 1]</summary>
        [PLParam(TextKey = "ShrinkSlider",
                 Description = "ビフォーからアフターへの進行量",
                 LimitKey = "Shrink.Slider", Required = true)]
        public float Slider                { get; }

        /// <summary>コライダー面から手前に残す距離（ワールド単位）</summary>
        [PLParam(TextKey = "ShrinkSurfaceOffset",
                 Description = "コライダー面から手前に残す距離（ワールド単位）。既定は 0",
                 LimitKey = "Shrink.SurfaceOffset")]
        public float SurfaceOffset         { get; }
        /// <summary>
        /// true : 進行方向に対して表を向いた面のみを衝突とみなす（裏面は素通り）
        /// false: 表裏を問わず衝突とみなす（既定）
        /// </summary>
        [PLParam(TextKey = "ShrinkFrontFaceOnly",
                 Description = "進行方向に表を向いた面だけを衝突とみなす。既定は false")]
        public bool  FrontFaceOnly         { get; }

        /// <summary>適用後に法線を再計算するか</summary>
        [PLParam(TextKey = "ShrinkRecalculateNormals",
                 Description = "適用後に頂点法線を再計算する。既定は true")]
        public bool  RecalculateNormals    { get; }
        /// <summary>
        /// true : 結果を新規オブジェクトとして追加し、ビフォー／アフターを非表示にする（既定）
        /// false: ビフォーを上書きし、元形状を &lt;名前&gt;_backup として追加する
        /// </summary>
        [PLParam(TextKey = "ShrinkCreateNewObject",
                 Description = "結果を新規オブジェクトとして追加する。false でビフォーを上書きする。既定は true")]
        public bool  CreateNewObject       { get; }
        /// <summary>
        /// 衝突判定の単位。
        /// VertexSegment … 頂点のビフォー→アフター線分とコライダー三角形の交差（既定）
        /// FacePair      … ビフォー面を三角形に割り、面どうしの接触時刻を求める
        /// </summary>
        [PLParam(TextKey = "ShrinkCollisionMode",
                 Description = "衝突判定の単位。VertexSegment / FacePair。既定は VertexSegment")]
        public Poly_Ling.UI.ShrinkCollisionMode CollisionMode { get; }
        /// <summary>
        /// 面方式の反復上限。頂点方式では使わない。
        /// 停止値は単調減少するので必ず収束するが、上限で打ち切ることもできる。
        /// </summary>
        [PLParam(TextKey = "ShrinkMaxPasses",
                 Description = "面方式の反復上限。頂点方式では使わない。既定は 8",
                 LimitKey = "Shrink.MaxPasses")]
        public int   MaxPasses             { get; }

        public ApplyShrinkCommand(
            int modelIndex,
            int beforeMasterIndex, int afterMasterIndex,
            int[] colliderMasterIndices,
            float slider,
            float surfaceOffset      = 0f,
            bool  frontFaceOnly      = false,
            bool  recalculateNormals = true,
            bool  createNewObject    = true,
            Poly_Ling.UI.ShrinkCollisionMode collisionMode = Poly_Ling.UI.ShrinkCollisionMode.VertexSegment,
            int   maxPasses          = 8)
            : base(modelIndex)
        {
            BeforeMasterIndex     = beforeMasterIndex;
            AfterMasterIndex      = afterMasterIndex;
            ColliderMasterIndices = colliderMasterIndices;
            Slider                = slider;
            SurfaceOffset         = surfaceOffset;
            FrontFaceOnly         = frontFaceOnly;
            RecalculateNormals    = recalculateNormals;
            CreateNewObject       = createNewObject;
            CollisionMode         = collisionMode;
            MaxPasses             = maxPasses;
        }
    }

    // ================================================================
    // 法線移植
    // ================================================================

    /// <summary>
    /// ビフォー／アフターの2オブジェクトが作るシェル（プリズム群）から、
    /// ターゲットオブジェクトの各頂点へ法線を移植する。Undo 記録付き。
    ///
    /// ビフォーとアフターは同一トポロジ（面数・各面のコーナー数が一致）であること。
    /// 4角形以上の面は i0 / i_k / i_k+1 の扇で三角形化される。
    ///
    /// スキニング無しを前提とする。法線の空間変換はオブジェクト単位の
    /// MeshContext.WorldMatrix だけを使う。
    /// </summary>
    public class ApplyNormalTransplantCommand : PanelCommand
    {
        /// <summary>ビフォー（内側の面）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "TransplantBeforeMasterIndex",
                 Description = "ビフォー（内側の面）の masterIndex", Required = true)]
        public int   BeforeMasterIndex   { get; }

        /// <summary>アフター（外側の面）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "TransplantAfterMasterIndex",
                 Description = "アフター（外側の面）の masterIndex", Required = true)]
        public int   AfterMasterIndex    { get; }

        /// <summary>法線を差し替える MeshContext の MasterIndex 配列</summary>
        [PLParam(TextKey = "TransplantTargetMasterIndices",
                 Description = "法線を差し替える描画オブジェクトの masterIndex 配列", Required = true)]
        public int[] TargetMasterIndices { get; }

        /// <summary>適用率 [0, 1]。1 未満なら元の法線と Slerp する。</summary>
        [PLParam(TextKey = "TransplantStrength",
                 Description = "適用率。1 未満なら元の法線と球面補間する。既定は 1",
                 LimitKey = "NormalTransplant.Strength")]
        public float Strength            { get; }
        /// <summary>
        /// true : 三角形内を球面補間する
        /// false: 三角形内を線形補間する（既定）
        /// </summary>
        [PLParam(TextKey = "TransplantSpherical",
                 Description = "三角形内を球面補間する。false で線形補間。既定は false")]
        public bool  Spherical           { get; }
        /// <summary>
        /// true : どのプリズムにも入らない頂点を最も近いプリズムへ寄せる
        /// false: どのプリズムにも入らない頂点は変更しない（既定）
        /// </summary>
        [PLParam(TextKey = "TransplantAllowNearest",
                 Description = "どのプリズムにも入らない頂点を最も近いプリズムへ寄せる。既定は false")]
        public bool  AllowNearest        { get; }

        public ApplyNormalTransplantCommand(
            int modelIndex,
            int beforeMasterIndex, int afterMasterIndex,
            int[] targetMasterIndices,
            float strength      = 1f,
            bool  spherical     = false,
            bool  allowNearest  = false)
            : base(modelIndex)
        {
            BeforeMasterIndex   = beforeMasterIndex;
            AfterMasterIndex    = afterMasterIndex;
            TargetMasterIndices = targetMasterIndices;
            Strength            = strength;
            Spherical           = spherical;
            AllowNearest        = allowNearest;
        }
    }

    // ================================================================
    // TPSモーフ
    // ================================================================

    /// <summary>
    /// TPSモーフの制御点の選び方。
    ///
    /// Global 以外は「ターゲット頂点ごとに独立に係数を求める」局所モードで、
    /// 近傍の選び方が 4 通りある。局所モードは 1 頂点ごとに LU 分解を行うため、
    /// 制御点数 N に対して「ターゲット頂点数 × (N+4)^3 / 3」の積和が要る。
    /// 半径モード（EuclideanRadius / LinkRadius）は制御点数が入力依存で
    /// 上限が無いため、必ず制御点数の上限で頭打ちにすること。
    ///
    /// リンク距離モード（LinkCount / LinkRadius）は、制御点候補だけの
    /// 誘導部分グラフをたどる。選択が飛び地になっていると到達できず
    /// 制御点が減る。ビフォーに面が無い場合は使えない。
    /// </summary>
    public enum ThinPlateLocalMode
    {
        /// <summary>全域モード。全制御点で 1 度だけ係数を求める（従来動作）。</summary>
        Global          = 0,
        /// <summary>ターゲット頂点位置から直線距離で近い順に N 個。</summary>
        EuclideanCount  = 1,
        /// <summary>最も近い候補点を始点に、リンク距離で近い順に N 個。</summary>
        LinkCount       = 2,
        /// <summary>ターゲット頂点位置から直線距離 L 以下。</summary>
        EuclideanRadius = 3,
        /// <summary>最も近い候補点を始点に、リンク距離 L 以下。</summary>
        LinkRadius      = 4,
    }

    /// <summary>
    /// ビフォー／アフター2オブジェクトの頂点対応から 3D Thin Plate Spline を解き、
    /// ターゲットオブジェクトを変形した結果を新規オブジェクトとして追加する。
    /// ターゲット自身は変更しない。Undo 記録付き。
    ///
    /// ビフォーとアフターは頂点インデックスで対応させるため、頂点数が一致していること。
    ///
    /// スキニング無しを前提とする。空間変換はオブジェクト単位の
    /// MeshContext.WorldMatrix だけを使う。
    /// </summary>
    public class ApplyThinPlateMorphCommand : PanelCommand
    {
        /// <summary>ビフォー（変形前の対応点）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ThinPlateBeforeMasterIndex",
                 Description = "変形前の対応点を持つオブジェクトの masterIndex", Required = true)]
        public int   BeforeMasterIndex         { get; }

        /// <summary>アフター（変形後の対応点）MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ThinPlateAfterMasterIndex",
                 Description = "変形後の対応点を持つオブジェクトの masterIndex", Required = true)]
        public int   AfterMasterIndex          { get; }

        /// <summary>変形させる MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ThinPlateSourceMasterIndex",
                 Description = "変形させる描画オブジェクトの masterIndex", Required = true)]
        public int   TargetMasterIndex         { get; }

        /// <summary>平滑化係数。K 行列の対角に加算される。0 で厳密補間。</summary>
        [PLParam(TextKey = "ThinPlateLambda",
                 Description = "平滑化係数。0 で厳密補間。既定は 0.001",
                 LimitKey = "ThinPlateMorph.Lambda")]
        public float Lambda                    { get; }
        /// <summary>
        /// true : ビフォー／アフターの選択頂点（両者の和集合）だけを制御点にする
        /// false: 全頂点を制御点にする（既定）
        /// </summary>
        [PLParam(TextKey = "ThinPlateSelectedControlPointsOnly",
                 Description = "ビフォー／アフターの選択頂点だけを制御点にする。既定は false")]
        public bool  SelectedControlPointsOnly { get; }

        /// <summary>結果の法線を再計算するか</summary>
        [PLParam(TextKey = "ThinPlateMorphRecalculateNormals",
                 Description = "適用後に頂点法線を再計算する。既定は true")]
        public bool  RecalculateNormals        { get; }

        public ApplyThinPlateMorphCommand(
            int modelIndex,
            int beforeMasterIndex, int afterMasterIndex, int targetMasterIndex,
            float lambda                        = 0.001f,
            bool  selectedControlPointsOnly     = false,
            bool  recalculateNormals            = true)
            : base(modelIndex)
        {
            BeforeMasterIndex         = beforeMasterIndex;
            AfterMasterIndex          = afterMasterIndex;
            TargetMasterIndex         = targetMasterIndex;
            Lambda                    = lambda;
            SelectedControlPointsOnly = selectedControlPointsOnly;
            RecalculateNormals        = recalculateNormals;
        }
    }

    /// <summary>
    /// 算出済みの変形結果を新規オブジェクトとして追加する。
    /// ターゲット自身は変更しない。Undo 記録付き。
    ///
    /// 局所モードの TPS は 1 頂点ごとに LU 分解を行うため実行時間が長く、
    /// 中止できるようバックグラウンドスレッドで走らせる。計算が終わった後に
    /// メインスレッドへ戻すのがこのコマンドで、変形そのものは行わない。
    /// 全域モードは ApplyThinPlateMorphCommand が同期実行するため、
    /// このコマンドを経由しない。
    /// </summary>
    public class ApplyThinPlateMorphResultCommand : PanelCommand
    {
        /// <summary>変形させた MeshContext の MasterIndex</summary>
        [PLParam(TextKey = "ThinPlateTargetMasterIndex",
                 Description = "変形結果を書き込む描画オブジェクトの masterIndex", Required = true)]
        public int       TargetMasterIndex  { get; }

        /// <summary>ターゲットのローカル座標での変形後位置。ターゲットの頂点数と同数であること。</summary>
        [PLParam(TextKey = "ThinPlateLocalPositions",
                 Description = "変形後の頂点位置（ターゲットのローカル座標）。頂点数と同数", Required = true)]
        public Vector3[] LocalPositions     { get; }

        /// <summary>結果の法線を再計算するか</summary>
        [PLParam(TextKey = "ThinPlateRecalculateNormals",
                 Description = "適用後に頂点法線を再計算する。既定は true")]
        public bool      RecalculateNormals { get; }

        public ApplyThinPlateMorphResultCommand(
            int modelIndex, int targetMasterIndex,
            Vector3[] localPositions, bool recalculateNormals = true)
            : base(modelIndex)
        {
            TargetMasterIndex  = targetMasterIndex;
            LocalPositions     = localPositions;
            RecalculateNormals = recalculateNormals;
        }
    }

    // ================================================================
    // UV 編集
    // ================================================================

    /// <summary>
    /// 指定 MeshContext の UV 座標変更をコマンドとして記録する。
    /// ドラッグ移動・一括変換の両方に使用する。
    /// </summary>
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
    public class AddMaterialSlotCommand : PanelCommand
    {
        public AddMaterialSlotCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>指定インデックスのマテリアルスロットを削除する</summary>
    public class RemoveMaterialSlotCommand : PanelCommand
    {
        [PLParam(TextKey = "RemoveMaterialSlotIndex",
                 Description = "削除するマテリアルスロットの番号", Required = true)]
        public int SlotIndex { get; }
        public RemoveMaterialSlotCommand(int modelIndex, int slotIndex)
            : base(modelIndex) { SlotIndex = slotIndex; }
    }

    /// <summary>選択面に指定マテリアルスロットを適用する</summary>
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
    public class SetMaterialColorCommand : PanelCommand
    {
        /// <summary>対象マテリアルスロット番号</summary>
        [PLParam(TextKey = "MaterialColorSlotIndex",
                 Description = "色を変えるマテリアルスロットの番号", Required = true)]
        public int   SlotIndex { get; }

        /// <summary>設定する基本色（RGBA）</summary>
        [PLParam(TextKey = "MaterialBaseColor",
                 Description = "設定する基本色（RGBA）", Required = true)]
        public Color BaseColor { get; }

        public SetMaterialColorCommand(int modelIndex, int slotIndex, Color baseColor)
            : base(modelIndex)
        {
            SlotIndex = slotIndex;
            BaseColor = baseColor;
        }
    }

    // ================================================================
    // 差分からのモーフ生成
    // ================================================================

    /// <summary>
    /// 基準モデルとモーフモデルの差分から頂点モーフを生成し、
    /// 基準モデルに MorphExpression として登録する。
    /// Undo 記録付き。
    /// </summary>
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

    // ================================================================
    // Tポーズ変換
    // ================================================================

    /// <summary>Humanoidマッピングを使用してTポーズに変換する</summary>
    /// <summary>
    /// スプリングボーン検証用のダミー装備を生成する（システムデバッグ）。
    ///
    /// 揺れデータ（SpringBoneChainRoot / SpringBoneJoint / SpringBoneColliders）を
    /// 書き込むオーサリング UI が無いため、VRM 出力の検証ができない。
    /// このコマンドは既存モデルへボーン鎖・スキンドメッシュ・揺れ付帯データ・
    /// コライダーを一度に足す。生成規則は SpringBoneTestRigBuilder が正典。
    /// </summary>
    public class BuildSpringBoneTestRigCommand : PanelCommand
    {
        /// <summary>生成パラメータ。null なら既定値。</summary>
        [PLParam(TextKey = "SpringBoneTestRig",
                 Description = "揺れ物テストリグの生成パラメータ。null で既定値", Required = true)]
        public Poly_Ling.Tools.SpringBoneTest.SpringBoneTestRigParams Params { get; }

        /// <summary>生成前に同じ接頭辞の既存生成物を消すか。</summary>
        [PLParam(TextKey = "SpringBoneClearExisting",
                 Description = "生成前に同じ接頭辞の既存生成物を消す。既定は true")]
        public bool ClearExisting { get; }

        public BuildSpringBoneTestRigCommand(
            int modelIndex,
            Poly_Ling.Tools.SpringBoneTest.SpringBoneTestRigParams prms,
            bool clearExisting = true)
            : base(modelIndex)
        {
            Params        = prms;
            ClearExisting = clearExisting;
        }
    }

    public class ApplyTPoseCommand : PanelCommand
    {
        public ApplyTPoseCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>バックアップから元の姿勢に戻す</summary>
    public class RestoreTPoseCommand : PanelCommand
    {
        public RestoreTPoseCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>現在の姿勢をベースとしてバックアップを破棄する（Undo不可）</summary>
    public class BakeTPoseCommand : PanelCommand
    {
        public BakeTPoseCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // Quad減面
    // ================================================================

    /// <summary>Quad保持減数化を実行して結果メッシュをモデルに追加する</summary>
    public class QuadDecimateCommand : PanelCommand
    {
        [PLParam(TextKey = "QuadDecimateSourceMasterIndex",
                 Description = "減面する描画オブジェクトの masterIndex", Required = true)]
        public int   SourceMasterIndex { get; }

        [PLParam(TextKey = "QuadDecimateTargetRatio",
                 Description = "残す面数の比率",
                 LimitKey = "QuadDecimate.TargetRatio", Required = true)]
        public float TargetRatio       { get; }

        [PLParam(TextKey = "QuadDecimateMaxPasses",
                 Description = "減面を繰り返す回数の上限",
                 LimitKey = "QuadDecimate.MaxPasses", Required = true)]
        public int   MaxPasses         { get; }

        [PLParam(TextKey = "QuadDecimateNormalAngleDeg",
                 Description = "法線を保つ角度のしきい値（度）",
                 LimitKey = "QuadDecimate.AngleDeg", Required = true)]
        public float NormalAngleDeg    { get; }

        [PLParam(TextKey = "QuadDecimateHardAngleDeg",
                 Description = "ハードエッジとみなす角度のしきい値（度）",
                 LimitKey = "QuadDecimate.AngleDeg", Required = true)]
        public float HardAngleDeg      { get; }

        [PLParam(TextKey = "QuadDecimateUvSeamThreshold",
                 Description = "UV シームとみなす差のしきい値",
                 LimitKey = "QuadDecimate.UvSeamThreshold", Required = true)]
        public float UvSeamThreshold   { get; }

        public QuadDecimateCommand(int modelIndex, int sourceMasterIndex,
            float targetRatio, int maxPasses,
            float normalAngleDeg, float hardAngleDeg, float uvSeamThreshold)
            : base(modelIndex)
        {
            SourceMasterIndex = sourceMasterIndex;
            TargetRatio       = targetRatio;
            MaxPasses         = maxPasses;
            NormalAngleDeg    = normalAngleDeg;
            HardAngleDeg      = hardAngleDeg;
            UvSeamThreshold   = uvSeamThreshold;
        }
    }

    // ================================================================
    // Mirror編集
    // ================================================================

    /// <summary>ミラーベイクで境界をどう決めるか</summary>
    public enum MirrorBoundaryMode
    {
        /// <summary>ミラー平面からの距離がしきい値未満の頂点を境界とする（従来）</summary>
        Threshold,
        /// <summary>選択頂点を境界とする</summary>
        SelectedVertices,
    }

    /// <summary>
    /// 選択メッシュ自身にミラーの実体を生やす（ミラー実体化 / in-place）。
    ///
    /// 対称面をまたぐ処理（法線スムージング等）を正しく効かせるための作業用機能であり、
    /// 見た目・エクスポート用の別オブジェクトは作らない。頂点も面も選択メッシュの中に増える。
    /// メッシュが見た目用のミラーモード（MirrorType > 0）だった場合は、実体化と同時に解除する。
    /// </summary>
    public class BakeMirrorCommand : PanelCommand
    {
        /// <summary>
        /// ミラー軸として指せる軸の数（X / Y / Z）。
        /// パネル側の選択肢（PlayerMirrorSubPanel の axisChoices）も同数で対応する。
        /// 法線編集の軸（NormalEditCommand.AxisCount）とは別物なので共有しない。
        /// </summary>
        public const int MirrorAxisCount = 3;

        [PLParam(TextKey = "BakeMirrorSourceMasterIndex",
                 Description = "ミラーを実体化する描画オブジェクトの masterIndex", Required = true)]
        public int   SourceMasterIndex { get; }

        /// <summary>ミラー軸（0:X, 1:Y, 2:Z）。メッシュが MirrorType > 0 のときはメッシュ側の設定が優先される。</summary>
        [PLParam(TextKey = "BakeMirrorAxis",
                 Description = "ミラー軸。0=X, 1=Y, 2=Z。メッシュ側の設定があればそちらが優先される",
                 Min = 0, Max = MirrorAxisCount - 1, Required = true)]
        public int   MirrorAxis        { get; }

        [PLParam(TextKey = "BakeMirrorThreshold",
                 Description = "ミラー平面からの距離が この値未満の頂点を境界とみなす",
                 LimitKey = "Mirror.Threshold", Required = true)]
        public float Threshold         { get; }

        [PLParam(TextKey = "BakeMirrorFlipU",
                 Description = "ミラー側の U 座標を反転する", Required = true)]
        public bool  FlipU             { get; }

        /// <summary>ミラー平面のオフセット（ローカル座標）</summary>
        [PLParam(TextKey = "BakeMirrorPlaneOffset",
                 Description = "ミラー平面のオフセット（ローカル座標）。既定は 0")]
        public float PlaneOffset { get; }

        /// <summary>境界の決め方</summary>
        [PLParam(TextKey = "BakeMirrorBoundaryMode",
                 Description = "境界の決め方。しきい値 / 選択頂点。既定は Threshold")]
        public MirrorBoundaryMode BoundaryMode { get; }

        /// <summary>境界頂点をミラー平面へ射影するか</summary>
        [PLParam(TextKey = "BakeMirrorProjectBoundaryToPlane",
                 Description = "境界頂点をミラー平面へ射影する。既定は true")]
        public bool ProjectBoundaryToPlane { get; }

        public BakeMirrorCommand(int modelIndex, int sourceMasterIndex, int mirrorAxis, float threshold, bool flipU)
            : this(modelIndex, sourceMasterIndex, mirrorAxis, threshold, flipU,
                   0f, MirrorBoundaryMode.Threshold, true)
        {
        }

        public BakeMirrorCommand(
            int modelIndex,
            int sourceMasterIndex,
            int mirrorAxis,
            float threshold,
            bool flipU,
            float planeOffset,
            MirrorBoundaryMode boundaryMode,
            bool projectBoundaryToPlane)
            : base(modelIndex)
        {
            SourceMasterIndex      = sourceMasterIndex;
            MirrorAxis             = mirrorAxis;
            Threshold              = threshold;
            FlipU                  = flipU;
            PlaneOffset            = planeOffset;
            BoundaryMode           = boundaryMode;
            ProjectBoundaryToPlane = projectBoundaryToPlane;
        }
    }

    /// <summary>
    /// ミラー実体化を解除して半身へ戻す（in-place）。
    /// 既定では解除後に見た目・エクスポート用のミラーモード（MirrorType = 2 / 結合）を強制する。
    /// RestoreSavedMirrorSettings = true のときは、実体化前のミラー設定へそのまま戻す。
    /// </summary>
    public class UnbakeMirrorCommand : PanelCommand
    {
        [PLParam(TextKey = "UnbakeSourceMasterIndex",
                 Description = "ミラーを解除する描画オブジェクトの masterIndex", Required = true)]
        public int SourceMasterIndex { get; }

        /// <summary>どちら側の編集結果を残すか</summary>
        [PLParam(TextKey = "UnbakeWriteBackMode",
                 Description = "どちら側の編集結果を残すか", Required = true)]
        public Poly_Ling.Tools.WriteBackMode Mode { get; }

        /// <summary>
        /// 実体化前のミラー設定（MirrorType / MirrorAxis / MirrorDistance / MirrorMaterialOffset）へ
        /// 戻すか。ツール内の「一時ミラー」はモデルの恒久設定を変えてはいけないので true にする。
        /// false のときは従来どおり MirrorType = 2（結合）を強制する。
        /// </summary>
        [PLParam(TextKey = "UnbakeRestoreSavedMirrorSettings",
                 Description = "実体化前のミラー設定へ戻す。false で MirrorType = 2 を強制する")]
        public bool RestoreSavedMirrorSettings { get; }

        public UnbakeMirrorCommand(int modelIndex, int sourceMasterIndex, Poly_Ling.Tools.WriteBackMode mode)
            : this(modelIndex, sourceMasterIndex, mode, false)
        {
        }

        public UnbakeMirrorCommand(
            int modelIndex,
            int sourceMasterIndex,
            Poly_Ling.Tools.WriteBackMode mode,
            bool restoreSavedMirrorSettings)
            : base(modelIndex)
        {
            SourceMasterIndex          = sourceMasterIndex;
            Mode                       = mode;
            RestoreSavedMirrorSettings = restoreSavedMirrorSettings;
        }
    }

    // ================================================================
    // Humanoidボーンマッピング
    // ================================================================

    /// <summary>プレビューマッピングをモデルに適用する</summary>
    public class ApplyHumanoidMappingCommand : PanelCommand
    {
        /// <summary>適用するマッピングのクローン</summary>
        [PLParam(TextKey = "HumanoidMapping",
                 Description = "モデルへ適用する Humanoid ボーンマッピング", Required = true)]
        public Poly_Ling.Data.HumanoidBoneMapping Mapping { get; }
        public ApplyHumanoidMappingCommand(int modelIndex, Poly_Ling.Data.HumanoidBoneMapping mapping)
            : base(modelIndex) { Mapping = mapping; }
    }

    /// <summary>モデルのHumanoidマッピングをクリアする</summary>
    public class ClearHumanoidMappingCommand : PanelCommand
    {
        public ClearHumanoidMappingCommand(int modelIndex) : base(modelIndex) { }
    }

    // ================================================================
    // MediaPipe フェイス変形
    // ================================================================

    /// <summary>MediaPipe ランドマークJSONを使ってカレントメッシュを変形した新メッシュを追加する</summary>
    public class MediaPipeFaceDeformCommand : PanelCommand
    {
        public int    SourceMasterIndex { get; }
        /// <summary>before.json のフルパス</summary>
        public string BeforePath        { get; }
        /// <summary>after.json のフルパス</summary>
        public string AfterPath         { get; }
        /// <summary>triangles.json のフルパス</summary>
        public string TrianglesPath     { get; }

        public MediaPipeFaceDeformCommand(int modelIndex, int sourceMasterIndex,
            string beforePath, string afterPath, string trianglesPath)
            : base(modelIndex)
        {
            SourceMasterIndex = sourceMasterIndex;
            BeforePath        = beforePath;
            AfterPath         = afterPath;
            TrianglesPath     = trianglesPath;
        }
    }

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

        /// <summary>追加先モード。</summary>
        [PLParam(TextKey = "AddMode", Description = "新規オブジェクト / 既存へ追加 / 新規モデル")]
        public Poly_Ling.Player.PrimitiveAddMode AddMode;

        /// <summary>
        /// 「既存へ追加」のときの追加先 MeshContextList インデックス。
        /// -1 なら選択オブジェクトリストの先頭。
        /// </summary>
        [PLParam(TextKey = "AddTargetIndex", Description = "追加先の索引。-1 で選択の先頭")]
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
        };
    }

    /// <summary>図形生成コマンドの共通部分。</summary>
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

        protected CreatePrimitiveMeshCommand(int modelIndex, PrimitivePlacement placement)
            : base(modelIndex) { Placement = placement; }
    }

    // ── 基本図形 ────────────────────────────────────────────────

    public sealed class CreateCubeCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Cube", Description = "直方体のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.CubeMeshGenerator.CubeParams Params { get; }

        public override string ShapeName => "Cube";
        public override string MeshName  => Params.MeshName;

        public CreateCubeCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.CubeMeshGenerator.CubeParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateSphereCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Sphere", Description = "球のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.SphereMeshGenerator.SphereParams Params { get; }

        public override string ShapeName => "Sphere";
        public override string MeshName  => Params.MeshName;

        public CreateSphereCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.SphereMeshGenerator.SphereParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateCylinderCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Cylinder", Description = "円柱のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.CylinderMeshGenerator.CylinderParams Params { get; }

        public override string ShapeName => "Cylinder";
        public override string MeshName  => Params.MeshName;

        public CreateCylinderCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.CylinderMeshGenerator.CylinderParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateCapsuleCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Capsule", Description = "カプセルのパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.CapsuleMeshGenerator.CapsuleParams Params { get; }

        public override string ShapeName => "Capsule";
        public override string MeshName  => Params.MeshName;

        public CreateCapsuleCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.CapsuleMeshGenerator.CapsuleParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreatePlaneCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Plane", Description = "平面のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.PlaneMeshGenerator.PlaneParams Params { get; }

        public override string ShapeName => "Plane";
        public override string MeshName  => Params.MeshName;

        public CreatePlaneCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.PlaneMeshGenerator.PlaneParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreatePyramidCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Pyramid", Description = "角錐のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.PyramidMeshGenerator.PyramidParams Params { get; }

        public override string ShapeName => "Pyramid";
        public override string MeshName  => Params.MeshName;

        public CreatePyramidCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.PyramidMeshGenerator.PyramidParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateStadiumBoxCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "StadiumBox", Description = "小判型のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.StadiumBoxMeshGenerator.StadiumBoxParams Params { get; }

        public override string ShapeName => "StadiumBox";
        public override string MeshName  => Params.MeshName;

        public CreateStadiumBoxCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.StadiumBoxMeshGenerator.StadiumBoxParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    // ── 高度な図形（パラメータだけで閉じるもの） ──────────────────

    /// <summary>
    /// パイプ接続用小判型（手のひらのもと）。
    /// 長さ X と奥行き Z は指定ではなく、円の個数・半径・矩形部の幅から決まる。
    /// </summary>
    public sealed class CreatePipeStadiumCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "PipeStadium", Description = "パイプ接続用小判型のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.PipeStadiumMeshGenerator.PipeStadiumParams Params { get; }

        public override string ShapeName => "PipeStadium";
        public override string MeshName  => Params.MeshName;

        public CreatePipeStadiumCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.PipeStadiumMeshGenerator.PipeStadiumParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    /// <summary>
    /// 髪の房。房 M 個 × 筒 N 本 の独立したチューブを 1 つの描画オブジェクトに入れる。
    /// 筒 1 本が部品 1 個になる（フリル・パイプと同じ扱い）。
    /// </summary>
    public sealed class CreateHairStrandCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "HairStrand", Description = "髪の房のパラメータ", Required = true)]
        public Poly_Ling.HairStrand.HairStrandParams Params { get; }

        public override string ShapeName => "HairStrand";
        public override string MeshName  => Params.MeshName;

        public CreateHairStrandCommand(
            int modelIndex,
            Poly_Ling.HairStrand.HairStrandParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateNGonGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "NGonGear", Description = "多角形歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.NGonGearMeshGenerator.NGonGearParams Params { get; }

        public override string ShapeName => "NGonGear";
        public override string MeshName  => Params.MeshName;

        public CreateNGonGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.NGonGearMeshGenerator.NGonGearParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateNGonStarCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "NGonStar", Description = "星形のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.NGonStarMeshGenerator.NGonStarParams Params { get; }

        public override string ShapeName => "NGonStar";
        public override string MeshName  => Params.MeshName;

        public CreateNGonStarCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.NGonStarMeshGenerator.NGonStarParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateInvoluteGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "InvoluteGear", Description = "インボリュート歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.InvoluteTrochoidGearMeshGenerator.InvoluteGearParams Params { get; }

        public override string ShapeName => "InvoluteGear";
        public override string MeshName  => Params.MeshName;

        public CreateInvoluteGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.InvoluteTrochoidGearMeshGenerator.InvoluteGearParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    // ── 機構部品 ────────────────────────────────────────────────
    //
    // 歯車まわりの生成器は Runtime/Poly_Ling_Main/Tools/PrimitiveMesh/Gears/ にある。
    // どれもパラメータ構造体だけで形が決まるので、コマンドは値を運ぶだけでよい。

    public sealed class CreateHelicalGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "HelicalGear", Description = "はすば歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.HelicalGearMeshGenerator.HelicalGearParams Params { get; }

        public override string ShapeName => "HelicalGear";
        public override string MeshName  => Params.MeshName;

        public CreateHelicalGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.HelicalGearMeshGenerator.HelicalGearParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateInternalGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "InternalGear", Description = "内歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.InternalGearMeshGenerator.InternalGearParams Params { get; }

        public override string ShapeName => "InternalGear";
        public override string MeshName  => Params.MeshName;

        public CreateInternalGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.InternalGearMeshGenerator.InternalGearParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateInvoluteRackCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "InvoluteRack", Description = "ラックのパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.InvoluteRackMeshGenerator.InvoluteRackParams Params { get; }

        public override string ShapeName => "InvoluteRack";
        public override string MeshName  => Params.MeshName;

        public CreateInvoluteRackCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.InvoluteRackMeshGenerator.InvoluteRackParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateHelicalRackCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "HelicalRack", Description = "はすばラックのパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.HelicalRackMeshGenerator.HelicalRackParams Params { get; }

        public override string ShapeName => "HelicalRack";
        public override string MeshName  => Params.MeshName;

        public CreateHelicalRackCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.HelicalRackMeshGenerator.HelicalRackParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateStraightBevelGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "StraightBevelGear", Description = "すぐばかさ歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.StraightBevelGearMeshGenerator.StraightBevelGearParams Params { get; }

        public override string ShapeName => "StraightBevelGear";
        public override string MeshName  => Params.MeshName;

        public CreateStraightBevelGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.StraightBevelGearMeshGenerator.StraightBevelGearParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateSpiralBevelGearCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "SpiralBevelGear", Description = "まがりばかさ歯車のパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.SpiralBevelGearMeshGenerator.SpiralBevelGearParams Params { get; }

        public override string ShapeName => "SpiralBevelGear";
        public override string MeshName  => Params.MeshName;

        public CreateSpiralBevelGearCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.SpiralBevelGearMeshGenerator.SpiralBevelGearParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateCylindricalWormCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "CylindricalWorm", Description = "円筒ウォームのパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.CylindricalWormMeshGenerator.CylindricalWormParams Params { get; }

        public override string ShapeName => "CylindricalWorm";
        public override string MeshName  => Params.MeshName;

        public CreateCylindricalWormCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.CylindricalWormMeshGenerator.CylindricalWormParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateWormWheelCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "WormWheel", Description = "ウォームホイールのパラメータ", Required = true)]
        public Poly_Ling.PrimitiveMesh.WormWheelMeshGenerator.WormWheelParams Params { get; }

        public override string ShapeName => "WormWheel";
        public override string MeshName  => Params.MeshName;

        public CreateWormWheelCommand(
            int modelIndex,
            Poly_Ling.PrimitiveMesh.WormWheelMeshGenerator.WormWheelParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateRibbonBowCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Ribbon", Description = "リボンのパラメータ", Required = true)]
        public Poly_Ling.Ribbon.RibbonBowParams Params { get; }

        public override string ShapeName => "Ribbon";
        public override string MeshName  => Params.MeshName;

        public CreateRibbonBowCommand(
            int modelIndex,
            Poly_Ling.Ribbon.RibbonBowParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    /// <summary>
    /// 回転体。プロファイル（断面の点列）は RevolutionParams.Profile が持つ。
    /// </summary>
    public sealed class CreateRevolutionCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Revolution", Description = "回転体のパラメータ", Required = true)]
        public Poly_Ling.Revolution.RevolutionParams Params { get; }

        public override string ShapeName => "Revolution";
        public override string MeshName  => Params.MeshName;

        public CreateRevolutionCommand(
            int modelIndex,
            Poly_Ling.Revolution.RevolutionParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    /// <summary>
    /// 2D 押し出し。ループ（輪郭の点列）は Profile2DParams.Loops が持つ。
    /// </summary>
    public sealed class CreateProfile2DCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Profile2D", Description = "2D押し出しのパラメータ", Required = true)]
        public Poly_Ling.Profile2DExtrude.Profile2DParams Params { get; }

        public override string ShapeName => "Profile2D";
        public override string MeshName  => Params.MeshName;

        public CreateProfile2DCommand(
            int modelIndex,
            Poly_Ling.Profile2DExtrude.Profile2DParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateTextMeshCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "Text", Description = "文字のパラメータ", Required = true)]
        public Poly_Ling.GlyphText.TextMeshParams Params { get; }

        public override string ShapeName => "Text";
        public override string MeshName  => Params.MeshName;

        public CreateTextMeshCommand(
            int modelIndex,
            Poly_Ling.GlyphText.TextMeshParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    public sealed class CreateNohMaskCommand : CreatePrimitiveMeshCommand
    {
        [PLParam(TextKey = "NohMask", Description = "面（能面）のパラメータ", Required = true)]
        public Poly_Ling.NohMask.FaceMeshParams Params { get; }

        public override string ShapeName => "NohMask";
        public override string MeshName  => Params.MeshName;

        public CreateNohMaskCommand(
            int modelIndex,
            Poly_Ling.NohMask.FaceMeshParams prms,
            PrimitivePlacement placement)
            : base(modelIndex, placement) { Params = prms; }
    }

    // ── 基準ベルトを使う図形 ─────────────────────────────────────

    /// <summary>
    /// 基準ベルト（梯子状データ）を入力に取る図形の共通部分。
    ///
    /// ベルトは取り込み元メッシュのローカル座標を持つ点列で、
    /// パネルでは選択メッシュから拾う。コマンドにはその結果を載せる。
    /// 向き補正とスプライン分割は生成側で掛けるので、拾ったままの値を渡す。
    /// </summary>
    public abstract class CreateBeltPrimitiveCommand : CreatePrimitiveMeshCommand
    {
        /// <summary>基準ベルト。1本が梯子1本にあたる。</summary>
        [PLParam(TextKey = "Belts", Description = "基準ベルト（梯子）の列", Required = true)]
        public Poly_Ling.PrimitiveMesh.BeltCsvEntry[] Belts { get; }

        /// <summary>向き補正。</summary>
        [PLParam(TextKey = "BeltOrient", Description = "梯子の向き補正")]
        public Poly_Ling.PrimitiveMesh.BeltOrientOptions Orient { get; }

        /// <summary>スプライン分割。</summary>
        [PLParam(TextKey = "BeltSpline", Description = "梯子のスプライン分割")]
        public Poly_Ling.PrimitiveMesh.BeltSplineOptions Spline { get; }

        protected CreateBeltPrimitiveCommand(
            int modelIndex, PrimitivePlacement placement,
            Poly_Ling.PrimitiveMesh.BeltCsvEntry[] belts,
            Poly_Ling.PrimitiveMesh.BeltOrientOptions orient,
            Poly_Ling.PrimitiveMesh.BeltSplineOptions spline)
            : base(modelIndex, placement)
        {
            Belts  = belts;
            Orient = orient;
            Spline = spline;
        }
    }

    /// <summary>
    /// フリル。断面プロファイルは A / B の2本まで持てる。
    /// TwoProfiles が false のときは A だけを使う。
    /// </summary>
    public sealed class CreateFrillCommand : CreateBeltPrimitiveCommand
    {
        [PLParam(TextKey = "Frill", Description = "フリルのパラメータ", Required = true)]
        public Poly_Ling.Frill.FrillParams Params { get; }

        /// <summary>断面プロファイル A。</summary>
        [PLParam(TextKey = "FrillProfileA", Description = "断面プロファイル A", Required = true)]
        public Vector2[] ProfileA { get; }

        /// <summary>断面プロファイル B。TwoProfiles が false なら使わない。</summary>
        [PLParam(TextKey = "FrillProfileB", Description = "断面プロファイル B")]
        public Vector2[] ProfileB { get; }

        public override string ShapeName => "Frill";
        public override string MeshName  => Params.MeshName;

        public CreateFrillCommand(
            int modelIndex,
            Poly_Ling.Frill.FrillParams prms,
            Vector2[] profileA, Vector2[] profileB,
            Poly_Ling.PrimitiveMesh.BeltCsvEntry[] belts,
            Poly_Ling.PrimitiveMesh.BeltOrientOptions orient,
            Poly_Ling.PrimitiveMesh.BeltSplineOptions spline,
            PrimitivePlacement placement)
            : base(modelIndex, placement, belts, orient, spline)
        {
            Params   = prms;
            ProfileA = profileA;
            ProfileB = profileB;
        }
    }

    /// <summary>パイプ。断面プロファイルは1本で、閉ループかどうかを別に持つ。</summary>
    public sealed class CreatePipeCommand : CreateBeltPrimitiveCommand
    {
        [PLParam(TextKey = "Pipe", Description = "パイプのパラメータ", Required = true)]
        public Poly_Ling.Pipe.PipeParams Params { get; }

        [PLParam(TextKey = "PipeProfile", Description = "断面プロファイル", Required = true)]
        public Vector2[] Profile { get; }

        [PLParam(TextKey = "PipeProfileClosed", Description = "断面を閉ループとして扱う")]
        public bool ProfileClosed { get; }

        public override string ShapeName => "Pipe";
        public override string MeshName  => Params.MeshName;

        public CreatePipeCommand(
            int modelIndex,
            Poly_Ling.Pipe.PipeParams prms,
            Vector2[] profile, bool profileClosed,
            Poly_Ling.PrimitiveMesh.BeltCsvEntry[] belts,
            Poly_Ling.PrimitiveMesh.BeltOrientOptions orient,
            Poly_Ling.PrimitiveMesh.BeltSplineOptions spline,
            PrimitivePlacement placement)
            : base(modelIndex, placement, belts, orient, spline)
        {
            Params        = prms;
            Profile       = profile;
            ProfileClosed = profileClosed;
        }
    }

    /// <summary>
    /// 藤壺（配置）。配置元はモデル内の描画オブジェクトなので索引で指す。
    /// 索引から MeshObject への解決はディスパッチャ側が行う。
    /// </summary>
    public sealed class CreatePlaceObjectCommand : CreateBeltPrimitiveCommand
    {
        [PLParam(TextKey = "PlaceObject", Description = "配置のパラメータ", Required = true)]
        public Poly_Ling.PlaceObject.PlaceObjectParams Params { get; }

        /// <summary>配置元の MeshContextList インデックス。</summary>
        [PLParam(TextKey = "PlaceSourceIndices", Description = "配置元オブジェクトの索引", Required = true)]
        public int[] SourceMasterIndices { get; }

        public override string ShapeName => "PlaceObject";
        public override string MeshName  => Params.MeshName;

        public CreatePlaceObjectCommand(
            int modelIndex,
            Poly_Ling.PlaceObject.PlaceObjectParams prms,
            int[] sourceMasterIndices,
            Poly_Ling.PrimitiveMesh.BeltCsvEntry[] belts,
            Poly_Ling.PrimitiveMesh.BeltOrientOptions orient,
            Poly_Ling.PrimitiveMesh.BeltSplineOptions spline,
            PrimitivePlacement placement)
            : base(modelIndex, placement, belts, orient, spline)
        {
            Params              = prms;
            SourceMasterIndices = sourceMasterIndices;
        }
    }

    /// <summary>
    /// 出来上がった MeshObject をそのままモデルへ置く。
    ///
    /// 【なぜ図形パラメータではなくメッシュを載せるか】
    ///   プロファイル編集の「メッシュへ反映」や厚み付け（ソリッド化）の結果は、
    ///   図形パラメータから決まるのではなく、編集中の点列や選択面から出来る。
    ///   パラメータ化できないので、そのままメッシュを渡す。
    ///
    /// 【MCP には出さない】
    ///   Mesh はスキーマにできないため Ignore を付けてある。
    ///   MCP からの生成には図形ごとの CreatePrimitiveMeshCommand を使う。
    /// </summary>
    public class AddGeneratedMeshCommand : PanelCommand
    {
        /// <summary>置くメッシュ。呼出し側が作った実体をそのまま渡す。</summary>
        [PLParam(Ignore = true, Description = "出来上がったメッシュ。スキーマには出さない")]
        public MeshObject Mesh { get; }

        /// <summary>描画オブジェクトの名前。</summary>
        [PLParam(TextKey = "MeshName", Description = "生成する描画オブジェクトの名前")]
        public string MeshName { get; }

        /// <summary>配置と後処理の指定。</summary>
        [PLParam(TextKey = "PrimitivePlacement", Description = "配置と後処理の指定", Required = true)]
        public PrimitivePlacement Placement { get; }

        /// <summary>
        /// 回転・拡大は呼出し側で頂点へ焼き込み済みか。
        /// true のとき、ディスパッチャは Placement の回転・拡大を頂点へ入れ直さない。
        /// </summary>
        [PLParam(Ignore = true, Description = "回転・拡大を呼出し側が頂点へ入れ済みか")]
        public bool PoseAlreadyBaked { get; }

        public AddGeneratedMeshCommand(
            int modelIndex, MeshObject mesh, string meshName,
            PrimitivePlacement placement, bool poseAlreadyBaked)
            : base(modelIndex)
        {
            Mesh             = mesh;
            MeshName         = meshName;
            Placement        = placement;
            PoseAlreadyBaked = poseAlreadyBaked;
        }
    }

    // ================================================================
    // 単一メッシュを返さない生成
    // ================================================================

    /// <summary>
    /// 穴つなぎ。2つの穴（境界辺の連結成分）の縁どうしに面を張る。
    /// 穴は種頂点で指す。種から縁を復元するのは生成側。
    /// </summary>
    public class CreateHoleBridgeCommand : PanelCommand
    {
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と図形生成パネルの穴つなぎ UI の双方がここを参照する。

        /// <summary>橋の中間分割数の下限・上限。</summary>
        public const int SubdivisionsMin = 0;
        public const int SubdivisionsMax = 16;

        /// <summary>穴A のあるメッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "BridgeHoleA", Description = "穴A のメッシュ索引", Required = true)]
        public int MeshA { get; }

        /// <summary>穴A の種頂点。</summary>
        [PLParam(TextKey = "BridgeHoleAVertex", Description = "穴A の種頂点番号", Required = true)]
        public int VertexA { get; }

        /// <summary>穴B のあるメッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "BridgeHoleB", Description = "穴B のメッシュ索引", Required = true)]
        public int MeshB { get; }

        /// <summary>穴B の種頂点。</summary>
        [PLParam(TextKey = "BridgeHoleBVertex", Description = "穴B の種頂点番号", Required = true)]
        public int VertexB { get; }

        /// <summary>
        /// 穴A の進行方向ヒント頂点。縁をどちら回りに辿るかを決める。
        /// 辺で取り込んでいないときは -1（縁の並び順そのままで辿る）。
        /// </summary>
        [PLParam(TextKey = "BridgeHoleADirHint", Description = "穴A の進行方向ヒント頂点。-1 で指定なし")]
        public int DirectionHintA { get; }

        /// <summary>穴B の進行方向ヒント頂点。-1 で指定なし。</summary>
        [PLParam(TextKey = "BridgeHoleBDirHint", Description = "穴B の進行方向ヒント頂点。-1 で指定なし")]
        public int DirectionHintB { get; }

        /// <summary>新規オブジェクトにするときの名前。</summary>
        [PLParam(TextKey = "MeshName", Description = "生成物の名前")]
        public string Name { get; }

        /// <summary>行き先。図形生成と同じ「追加先」に従う。</summary>
        [PLParam(TextKey = "AddMode", Description = "新規オブジェクト / 既存へ追加 / 新規モデル")]
        public Poly_Ling.Player.PrimitiveAddMode AddMode { get; }

        [PLParam(TextKey = "AddTargetIndex", Description = "追加先の索引。-1 で選択の先頭")]
        public int AddTargetIndex { get; }

        /// <summary>
        /// 対応フリップと面フリップを、両穴の巻き方向から自動で決めるか。
        ///
        /// true のとき FlipCorrespondence / FlipFaces は使わず、
        /// BridgeLoopOps.TryAutoFlags の判定結果を使う。裏返りとねじれを避けるための既定。
        /// 判定できなかったときはフラグを変えない（TryAutoFlags の仕様）。
        ///
        /// false のときはコマンドの値をそのまま使う。手作業でチェックを
        /// 外した状態を再現したいときに使う。
        /// </summary>
        [PLParam(TextKey = "BridgeAutoFlags", Description = "対応と面の向きを自動で決める")]
        public bool AutoFlags { get; }

        /// <summary>縁どうしの対応をずらす。AutoFlags が true のときは使わない。</summary>
        [PLParam(TextKey = "BridgeFlipPair", Description = "縁どうしの対応を反転する")]
        public bool FlipCorrespondence { get; }

        /// <summary>張った面を裏返す。AutoFlags が true のときは使わない。</summary>
        [PLParam(TextKey = "BridgeFlipFaces", Description = "張った面を裏返す")]
        public bool FlipFaces { get; }

        /// <summary>橋の中間分割数。</summary>
        [PLParam(TextKey = "BridgeSubdiv", Description = "橋の中間分割数",
                 Min = SubdivisionsMin, Max = SubdivisionsMax, Step = 1)]
        public int Subdivisions { get; }

        public CreateHoleBridgeCommand(
            int modelIndex, int meshA, int vertexA, int meshB, int vertexB, string name,
            Poly_Ling.Player.PrimitiveAddMode addMode, int addTargetIndex,
            bool flipCorrespondence, bool flipFaces, int subdivisions,
            int directionHintA = -1, int directionHintB = -1,
            bool autoFlags = true)
            : base(modelIndex)
        {
            AutoFlags          = autoFlags;
            MeshA              = meshA;
            VertexA            = vertexA;
            MeshB              = meshB;
            VertexB            = vertexB;
            DirectionHintA     = directionHintA;
            DirectionHintB     = directionHintB;
            Name               = name;
            AddMode            = addMode;
            AddTargetIndex     = addTargetIndex;
            FlipCorrespondence = flipCorrespondence;
            FlipFaces          = flipFaces;
            Subdivisions       = subdivisions;
        }
    }

    /// <summary>
    /// 辺群ブリッジ。拾った辺そのものを辺群として、その間に面を張る。
    /// 開いた辺の連なりも扱える点が穴つなぎと違う。
    /// 辺は同一メッシュのものに限る（生成側が2群へ分けるため）。
    /// </summary>
    public class CreateEdgeBridgeCommand : PanelCommand
    {
        // ── 値域 ─────────────────────────────────────────────────
        // PLParam 属性と EdgeBridgeToolHandler.Subdivisions の丸めの双方がここを参照する。
        // 穴つなぎ（CreateHoleBridgeCommand）とは上限が違う。辺群ブリッジは
        // 開いた辺の連なりも扱うため、もとから広い範囲を許している。

        /// <summary>橋の中間分割数の下限・上限。</summary>
        public const int SubdivisionsMin = 0;
        public const int SubdivisionsMax = 32;

        /// <summary>辺のあるメッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "EdgeBridgeMesh", Description = "対象メッシュの索引", Required = true)]
        public int MeshIndex { get; }

        /// <summary>拾った辺。両端の頂点番号の組で表す。</summary>
        [PLParam(TextKey = "EdgeBridgeEdges", Description = "拾った辺の列", Required = true)]
        public Poly_Ling.Selection.VertexPair[] Edges { get; }

        /// <summary>面の向きを自動で決めるか。</summary>
        [PLParam(TextKey = "BridgeAutoSelect", Description = "対応と面の向きを自動で決める")]
        public bool AutoCorrespondence { get; }

        [PLParam(TextKey = "BridgeFlipPair", Description = "縁どうしの対応を反転する")]
        public bool FlipCorrespondence { get; }

        [PLParam(TextKey = "BridgeFlipFaces", Description = "張った面を裏返す")]
        public bool FlipFaces { get; }

        [PLParam(TextKey = "BridgeSubdiv", Description = "橋の中間分割数",
                 Min = SubdivisionsMin, Max = SubdivisionsMax, Step = 1)]
        public int Subdivisions { get; }

        public CreateEdgeBridgeCommand(
            int modelIndex, int meshIndex, Poly_Ling.Selection.VertexPair[] edges,
            bool autoCorrespondence, bool flipCorrespondence, bool flipFaces, int subdivisions)
            : base(modelIndex)
        {
            MeshIndex          = meshIndex;
            Edges              = edges;
            AutoCorrespondence = autoCorrespondence;
            FlipCorrespondence = flipCorrespondence;
            FlipFaces          = flipFaces;
            Subdivisions       = subdivisions;
        }
    }

    /// <summary>
    /// プロジェクトを空にして、モデルを 1 つだけ作り直す。
    ///
    /// 【何のためか】
    ///   自動検証は系統ごとに「まっさらな状態」から積み上げる。
    ///   モデルを足すだけだと前の系統のモデルが残り、保存したフォルダに
    ///   関係ないモデルが同梱される（CsvProjectSerializer はプロジェクト内の
    ///   全モデルをフォルダへ書く）。何をどの順番でやった結果なのかが
    ///   読み取れなくなるので、明示的に捨てる口を用意する。
    ///
    /// 【破壊的】
    ///   開いているモデルを全部捨てる。Undo では戻せない。
    ///   UI のボタンには出さず、自動検証とリモートからのみ使う。
    /// </summary>
    public class ResetProjectCommand : PanelCommand
    {
        /// <summary>作り直すモデルの名前。空なら "Model"。</summary>
        [PLParam(TextKey = "ResetProjectModelName", Description = "作り直すモデルの名前")]
        public string ModelName { get; }

        public ResetProjectCommand(string modelName = null)
            : base(0) { ModelName = modelName; }
    }

    // ================================================================
    // Undo / Redo
    // ================================================================

    /// <summary>
    /// 直前の操作を 1 段戻す。モデル非依存なので ModelIndex は 0 固定。
    /// 戻せる履歴が無いときは失敗として返る。
    /// </summary>
    public class PerformUndoCommand : PanelCommand
    {
        public PerformUndoCommand() : base(0) { }
    }

    /// <summary>戻した操作を 1 段やり直す。</summary>
    public class PerformRedoCommand : PanelCommand
    {
        public PerformRedoCommand() : base(0) { }
    }

    /// <summary>
    /// 穴の頂点数を基準穴に合わせる。穴つなぎ（BridgeLoopOps）が要求する
    /// 「2 つの穴の頂点数が同じ」を満たすための前処理。
    ///
    /// 変更されるのは対象穴のメッシュだけで、基準穴は頂点数を読むだけ。
    /// 基準と対象が同じメッシュにあってもよい。
    /// </summary>
    public class MatchHoleRingCountCommand : PanelCommand
    {
        /// <summary>基準穴のあるメッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "HoleRingBaseMesh", Description = "基準穴のメッシュ索引", Required = true)]
        public int BaseMeshIndex { get; }

        /// <summary>基準穴の種頂点。</summary>
        [PLParam(TextKey = "HoleRingBaseVertex", Description = "基準穴の種頂点番号", Required = true)]
        public int BaseVertex { get; }

        /// <summary>基準穴の進行方向ヒント頂点。-1 で指定なし。</summary>
        [PLParam(TextKey = "HoleRingBaseDirHint", Description = "基準穴の進行方向ヒント頂点。-1 で指定なし")]
        public int BaseDirectionHint { get; }

        /// <summary>対象穴のあるメッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "HoleRingTargetMesh", Description = "対象穴のメッシュ索引", Required = true)]
        public int TargetMeshIndex { get; }

        /// <summary>対象穴の種頂点。</summary>
        [PLParam(TextKey = "HoleRingTargetVertex", Description = "対象穴の種頂点番号", Required = true)]
        public int TargetVertex { get; }

        /// <summary>対象穴の進行方向ヒント頂点。-1 で指定なし。</summary>
        [PLParam(TextKey = "HoleRingTargetDirHint", Description = "対象穴の進行方向ヒント頂点。-1 で指定なし")]
        public int TargetDirectionHint { get; }

        /// <summary>三角形を三角形へ分割するか。false なら四角へ分割する。</summary>
        [PLParam(TextKey = "HoleRingSplitTri", Description = "三角形を三角形へ分割する")]
        public bool SplitTriangleIntoTriangles { get; }

        public MatchHoleRingCountCommand(
            int modelIndex,
            int baseMeshIndex, int baseVertex, int baseDirectionHint,
            int targetMeshIndex, int targetVertex, int targetDirectionHint,
            bool splitTriangleIntoTriangles = true)
            : base(modelIndex)
        {
            BaseMeshIndex              = baseMeshIndex;
            BaseVertex                 = baseVertex;
            BaseDirectionHint          = baseDirectionHint;
            TargetMeshIndex            = targetMeshIndex;
            TargetVertex               = targetVertex;
            TargetDirectionHint        = targetDirectionHint;
            SplitTriangleIntoTriangles = splitTriangleIntoTriangles;
        }
    }

    /// <summary>
    /// 面を消す。面削除モードのクリック 1 回ぶんに相当するが、複数枚をまとめて渡せる。
    /// 消すのは指定メッシュの面だけで、他のオブジェクトの選択は巻き込まない。
    /// </summary>
    public class DeleteFacesCommand : PanelCommand
    {
        /// <summary>対象メッシュの MeshContextList インデックス。</summary>
        [PLParam(TextKey = "DeleteFacesMesh", Description = "対象メッシュの索引", Required = true)]
        public int MeshIndex { get; }

        /// <summary>消す面の番号。</summary>
        [PLParam(TextKey = "DeleteFacesIndices", Description = "消す面の番号", Required = true)]
        public int[] FaceIndices { get; }

        public DeleteFacesCommand(int modelIndex, int meshIndex, int[] faceIndices)
            : base(modelIndex)
        {
            MeshIndex   = meshIndex;
            FaceIndices = faceIndices;
        }
    }

    /// <summary>
    /// 歪み複製。複製元を歪ませながら複数組つくり、モデルへ挿入する。
    /// 単一のメッシュを返さないので図形生成コマンドとは別系統にする。
    /// 作業軸はモデル側の状態なのでディスパッチャが解決する。
    /// </summary>
    public class CreateObjectArrayCommand : PanelCommand
    {
        /// <summary>生成パラメータ。</summary>
        [PLParam(TextKey = "ObjectArray", Description = "歪み複製のパラメータ", Required = true)]
        public Poly_Ling.Tools.ObjectArray.ObjectArrayParams Params { get; }

        /// <summary>複製元の MeshContextList インデックス。</summary>
        [PLParam(TextKey = "ObjectArraySources", Description = "複製元オブジェクトの索引", Required = true)]
        public int[] SourceMasterIndices { get; }

        /// <summary>掛ける歪み。DeformerRegistry の実装をそのまま渡す。</summary>
        [PLParam(TextKey = "ObjectArrayDeformer", Description = "掛ける歪み", Required = true)]
        public Poly_Ling.Tools.Deformers.IMeshDeformer Deformer { get; }

        public CreateObjectArrayCommand(
            int modelIndex,
            Poly_Ling.Tools.ObjectArray.ObjectArrayParams prms,
            int[] sourceMasterIndices,
            Poly_Ling.Tools.Deformers.IMeshDeformer deformer)
            : base(modelIndex)
        {
            Params              = prms;
            SourceMasterIndices = sourceMasterIndices;
            Deformer            = deformer;
        }
    }

    // ================================================================
    // 位相編集（パラメータを持たない実行系）
    //
    // 【対象の指定】
    //   MasterIndices は「実行時点の選択オブジェクトと一致すること」を要求する
    //   （照合方式）。受け口は一致しなければ失敗理由を返し、選択を書き換えない。
    //   リモート／MCP から呼ぶときは先に SelectMeshCommand で選択を作る。
    //
    // 【要素の指定】
    //   どの頂点・辺・面に効くかは各メッシュの Selection が持つ。P7 で明示化する。
    //
    // ObjectIds は MasterIndices と同じ並び・同じ長さの安定ID。
    // ローカル発行時は null / 空でよい（照合をスキップする）。
    // ================================================================

    /// <summary>
    /// 選択辺を挟む 2 枚の面を 1 枚へ結合する。
    /// 共有頂点はほかの面が使っていなければ削除して前後の点をつなぐ。
    /// 実処理は FaceMergeTool。対象は選択中の描画オブジェクト全部。
    /// </summary>
    public class FaceMergeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        public FaceMergeCommand(int modelIndex, int[] masterIndices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
        }
    }

    /// <summary>
    /// 選択辺を挟む 2 枚の面を 1 枚へ結合する（共有頂点を新しい面から外す方式）。
    /// 外した頂点はどの面からも使われなくなったときだけ消える。
    /// 実処理は FaceMergeCollapseTool。対象は選択中の描画オブジェクト全部。
    /// </summary>
    public class FaceMergeCollapseCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        public FaceMergeCollapseCommand(int modelIndex, int[] masterIndices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
        }
    }

    /// <summary>
    /// 選択頂点を共有する四角形 4 枚を、四隅を結ぶ四角形 1 枚へ張り替える。
    /// 実処理は Quad4To1Tool。対象は選択中の描画オブジェクト全部。
    /// </summary>
    public class Quad4To1Command : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        public Quad4To1Command(int modelIndex, int[] masterIndices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
        }
    }

    /// <summary>
    /// 選択した三角形とそれを囲む三角形 3 枚を、外側の 3 頂点を結ぶ三角形 1 枚へ張り替える。
    /// 中点細分割の逆操作。実処理は Tri4To1Tool。対象は選択中の描画オブジェクト全部。
    /// </summary>
    public class Tri4To1Command : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        public Tri4To1Command(int modelIndex, int[] masterIndices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
        }
    }

    /// <summary>
    /// 選択頂点を消して、その頂点を囲む面を 1 枚の面へ張り替える。
    /// 周りが閉じていない（境界の）頂点は対象外。
    /// 実処理は VertexDissolveTool。対象は選択中の描画オブジェクト全部。
    /// </summary>
    public class VertexDissolveCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        public VertexDissolveCommand(int modelIndex, int[] masterIndices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
        }
    }

    /// <summary>
    /// 選択頂点を面ごとに独立したコピーへ分離する。2 面以上に共有されている頂点が対象。
    ///
    /// 実処理（SplitVerticesTool）が編集対象メッシュ 1 本にしか効かないため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// 配列なのは他のコマンドと形をそろえて ObjectIds と対にするため。
    /// </summary>
    public class SplitVerticesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        public SplitVerticesCommand(int modelIndex, int[] masterIndices, ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
        }
    }

    // ================================================================
    // 位相・頂点編集（パラメータを持つ実行系）
    //
    // 対象の指定・要素の指定の扱いは上の「パラメータを持たない実行系」と同じ。
    // 設定値はコマンドが正典で、受け口は実行後にパネルの値へ戻す。
    // 1 呼び出しがパネルの状態に依存しないようにするため。
    // ================================================================

    /// <summary>
    /// 選択頂点を消して穴を開ける。頂点につながる各辺の上に新しい頂点を作り、
    /// 元の面を張り替える。実処理は VertexHoleTool。
    /// 対象は選択中の描画オブジェクト全部。
    /// </summary>
    public class VertexHoleCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        /// <summary>
        /// 新しい頂点を置く位置の比率。1 が選択頂点の位置、0 が辺の反対側（根元）。
        /// 小さいほど穴が大きくなる。
        /// </summary>
        [PLParam(TextKey = "VertexHoleRatio",
                 Description = "穴の位置比率。1 = 選択頂点の位置、0 = 辺の根元。既定は 0.5",
                 LimitKey = "VertexHole.Ratio")]
        public float   Ratio { get; }

        public VertexHoleCommand(
            int modelIndex, int[] masterIndices,
            float ratio        = 0.5f,
            ulong[] objectIds  = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            Ratio         = ratio;
        }
    }

    /// <summary>
    /// 面の裏表を反転する。実処理は FlipFaceTool。
    ///
    /// 実処理が編集対象メッシュ 1 本にしか効かない（FlipFaceTool.cs:93）ため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// </summary>
    public class FlipFaceCommand : PanelCommand
    {
        /// <summary>反転する範囲。</summary>
        public enum FlipScope
        {
            /// <summary>選択されている面だけ。</summary>
            Selected,
            /// <summary>メッシュの全面。</summary>
            All
        }

        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "FlipFaceScope",
                 Description = "反転する範囲。Selected / All", Required = true)]
        public FlipScope Scope { get; }

        public FlipFaceCommand(
            int modelIndex, int[] masterIndices,
            FlipScope scope,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            Scope         = scope;
        }
    }

    /// <summary>
    /// 選択頂点を軸ごとに整列する。実処理は AlignVerticesTool。
    ///
    /// 実処理が編集対象メッシュ 1 本にしか効かない（AlignVerticesTool.cs:141）ため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// </summary>
    public class AlignVerticesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "AlignX", Description = "X 座標をそろえる")]
        public bool      AlignX { get; }

        [PLParam(TextKey = "AlignY", Description = "Y 座標をそろえる")]
        public bool      AlignY { get; }

        [PLParam(TextKey = "AlignZ", Description = "Z 座標をそろえる")]
        public bool      AlignZ { get; }

        /// <summary>そろえる先の決め方。</summary>
        [PLParam(TextKey = "AlignMode",
                 Description = "そろえる先。Average / Min / Max", Required = true)]
        public AlignMode Mode   { get; }

        public AlignVerticesCommand(
            int modelIndex, int[] masterIndices,
            bool alignX, bool alignY, bool alignZ,
            AlignMode mode,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            AlignX        = alignX;
            AlignY        = alignY;
            AlignZ        = alignZ;
            Mode          = mode;
        }
    }

    /// <summary>
    /// 選択した辺・線分のつながりを平滑化する。実処理は SmoothEdgesTool。
    ///
    /// 実処理が編集対象メッシュ 1 本にしか効かない（SmoothEdgesTool.cs:116）ため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// </summary>
    public class SmoothEdgesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "SmoothEdgesStrength",
                 Description = "平滑化の強度", LimitKey = "SmoothEdges.Strength")]
        public float Strength     { get; }

        [PLParam(TextKey = "SmoothEdgesIterations",
                 Description = "平滑化の反復回数", LimitKey = "SmoothEdges.Iterations")]
        public int   Iterations   { get; }

        [PLParam(TextKey = "SmoothEdgesFixEndpoints",
                 Description = "チェーンの端点を動かさない")]
        public bool  FixEndpoints { get; }

        [PLParam(TextKey = "SmoothEdgesLockX", Description = "X 方向の移動を禁じる")]
        public bool  LockX { get; }

        [PLParam(TextKey = "SmoothEdgesLockY", Description = "Y 方向の移動を禁じる")]
        public bool  LockY { get; }

        [PLParam(TextKey = "SmoothEdgesLockZ", Description = "Z 方向の移動を禁じる")]
        public bool  LockZ { get; }

        public SmoothEdgesCommand(
            int modelIndex, int[] masterIndices,
            float strength, int iterations,
            bool fixEndpoints = true,
            bool lockX = false, bool lockY = false, bool lockZ = false,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            Strength      = strength;
            Iterations    = iterations;
            FixEndpoints  = fixEndpoints;
            LockX         = lockX;
            LockY         = lockY;
            LockZ         = lockZ;
        }
    }

    /// <summary>
    /// 2 本のボーンが決める平面へ選択頂点を寄せる。実処理は PlanarizeAlongBonesTool。
    ///
    /// 実処理が編集対象メッシュ 1 本にしか効かない（PlanarizeAlongBonesTool.cs:140）ため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    ///
    /// BoneIndexA / BoneIndexB は BoneNames の並び（ツールが組むボーン一覧）の索引で、
    /// MeshContextList の索引ではない。
    /// </summary>
    public class PlanarizeAlongBonesCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "PlanarizeBoneA",
                 Description = "基準ボーン A。ツールのボーン一覧内の索引", Required = true)]
        public int                BoneIndexA { get; }

        [PLParam(TextKey = "PlanarizeBoneB",
                 Description = "基準ボーン B。ツールのボーン一覧内の索引。A と別であること", Required = true)]
        public int                BoneIndexB { get; }

        [PLParam(TextKey = "PlanarizePlaneMode",
                 Description = "平面の置き方。MinMovement / AnchorToA")]
        public PlanePlacementMode PlaneMode  { get; }

        [PLParam(TextKey = "PlanarizeBlend",
                 Description = "寄せる度合い。0 = 動かさない、1 = 完全に平面へ。既定は 1",
                 Min = 0.0, Max = 1.0)]
        public float              Blend      { get; }

        public PlanarizeAlongBonesCommand(
            int modelIndex, int[] masterIndices,
            int boneIndexA, int boneIndexB,
            PlanePlacementMode planeMode = PlanePlacementMode.MinMovement,
            float blend                  = 1f,
            ulong[] objectIds            = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            BoneIndexA    = boneIndexA;
            BoneIndexB    = boneIndexB;
            PlaneMode     = planeMode;
            Blend         = blend;
        }
    }

    /// <summary>
    /// 選択頂点を結合する。実処理は MergeVerticesTool。
    ///
    /// 実処理が編集対象メッシュ 1 本にしか効かない（MergeVerticesTool.cs:119）ため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// </summary>
    public class MergeVerticesCommand : PanelCommand
    {
        /// <summary>結合の仕方。</summary>
        public enum MergeMode
        {
            /// <summary>距離を見ず、選択頂点を 1 点（重心）へ寄せる。</summary>
            Centroid,
            /// <summary>しきい値以下の距離にある頂点どうしだけを結合する。</summary>
            Threshold
        }

        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "MergeVerticesMode",
                 Description = "結合の仕方。Centroid / Threshold", Required = true)]
        public MergeMode Mode      { get; }

        /// <summary>Threshold モードの距離しきい値。Centroid では読まれない。</summary>
        [PLParam(TextKey = "MergeVerticesThreshold",
                 Description = "Threshold モードの距離しきい値。既定は 0.001",
                 Min = 0.0001)]
        public float     Threshold { get; }

        public MergeVerticesCommand(
            int modelIndex, int[] masterIndices,
            MergeMode mode,
            float threshold   = 0.001f,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices = masterIndices ?? System.Array.Empty<int>();
            ObjectIds     = objectIds;
            Mode          = mode;
            Threshold     = threshold;
        }
    }

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
    public class DeleteSelectionCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
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
    public class PipeAlignCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
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
    public class PlaceObjectReshapeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
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
    public class SolidifyCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
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
    /// 選択線分から検出した輪郭ループを押し出してメッシュを作る。
    /// 実処理は LineExtrudeTool + Profile2DExtrudeMeshGenerator。
    ///
    /// 実処理が編集対象メッシュ 1 本の選択線分しか見ないため、
    /// MasterIndices は「1 個で、それが編集対象と一致すること」を要求する。
    /// </summary>
    public class LineExtrudeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
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
    public class SurfaceSnapCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。実行時点の選択オブジェクトと一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
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
    public class EdgeBevelCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
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
    public class EdgeExtrudeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
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
    public class FaceExtrudeCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "対象の描画オブジェクトの masterIndex 配列。要素は 1 個で、編集対象と一致すること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
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
    // スキンウェイト塗り
    // ================================================================

    /// <summary>
    /// ブラシで塗ったスキンウェイトを適用する。実処理は SkinWeightPaintTool。
    ///
    /// 【なぜ点列ではなく頂点列を持つか】
    ///   対象頂点はスクリーン空間のブラシ円で決まる（SkinWeightPaintToolHandler の
    ///   ComputeBrushVertices）。ワールド座標の点列から求め直すと対象が変わり、
    ///   パネル操作とリモート発行で結果が食い違う。よって「掛けた頂点と falloff」を
    ///   そのまま持つ。カメラに依存しないので MCP から見ても自己完結する。
    ///
    /// 【ステップを合成しない理由】
    ///   Add は毎回加算、Replace は毎回 Lerp、Scale は毎回乗算するため、結果は
    ///   適用回数と順序に依存する。ブラシ 1 回分を 1 ステップとして順に適用する。
    ///
    /// 【平坦な配列】
    ///   PanelCommandFactory は文字列パラメータから平坦な配列しか組み立てられないため、
    ///   入れ子を持てない。ステップの区切りは StepStarts が持つ。
    ///   長さの整合（StepStarts.Length == StepMeshIndices.Length、
    ///   VertexIndices.Length == Falloffs.Length、StepStarts が単調増加で範囲内）は
    ///   受け口の実行時検証で守る。型では表現できない。
    /// </summary>
    public class SkinWeightPaintCommand : PanelCommand
    {
        [PLParam(TextKey = "MasterIndices",
                 Description = "ステップが触る描画オブジェクトの masterIndex 配列。実行時点の塗り対象に含まれること",
                 Required = true)]
        public int[]   MasterIndices { get; }

        [PLParam(TextKey = "ObjectIds",
                 Description = "MasterIndices と同じ並び・同じ長さの安定 ID。null でズレ照合を省く")]
        public ulong[] ObjectIds     { get; }

        [PLParam(TextKey = "SkinPaintStepStarts",
                 Description = "各ステップが VertexIndices のどこから始まるか。単調増加。長さ = ステップ数",
                 Required = true)]
        public int[]   StepStarts { get; }

        [PLParam(TextKey = "SkinPaintStepMeshIndices",
                 Description = "各ステップの対象メッシュ masterIndex。StepStarts と同じ長さ",
                 Required = true)]
        public int[]   StepMeshIndices { get; }

        [PLParam(TextKey = "SkinPaintVertexIndices",
                 Description = "全ステップ分の頂点番号を連結したもの", Required = true)]
        public int[]   VertexIndices { get; }

        [PLParam(TextKey = "SkinPaintFalloffs",
                 Description = "VertexIndices と同じ長さの falloff（0〜1）", Required = true)]
        public float[] Falloffs { get; }

        [PLParam(TextKey = "SkinPaintMode",
                 Description = "塗り方。Replace / Add / Scale / Smooth", Required = true)]
        public Poly_Ling.UI.SkinWeightPaintMode PaintMode { get; }

        /// <summary>対象ボーンの masterIndex。Smooth では読まれない。</summary>
        [PLParam(TextKey = "SkinPaintTargetBone",
                 Description = "対象ボーンの masterIndex。Smooth では読まれない")]
        public int   TargetBone { get; }

        [PLParam(TextKey = "SkinPaintStrength", Description = "塗りの強度")]
        public float Strength { get; }

        [PLParam(TextKey = "SkinPaintWeightValue",
                 Description = "書き込む値。Replace は目標値、Add は加算量、Scale は倍率")]
        public float WeightValue { get; }

        public SkinWeightPaintCommand(
            int modelIndex, int[] masterIndices,
            int[] stepStarts, int[] stepMeshIndices,
            int[] vertexIndices, float[] falloffs,
            Poly_Ling.UI.SkinWeightPaintMode paintMode,
            int targetBone    = -1,
            float strength    = 1f,
            float weightValue = 1f,
            ulong[] objectIds = null)
            : base(modelIndex)
        {
            MasterIndices   = masterIndices   ?? System.Array.Empty<int>();
            ObjectIds       = objectIds;
            StepStarts      = stepStarts      ?? System.Array.Empty<int>();
            StepMeshIndices = stepMeshIndices ?? System.Array.Empty<int>();
            VertexIndices   = vertexIndices   ?? System.Array.Empty<int>();
            Falloffs        = falloffs        ?? System.Array.Empty<float>();
            PaintMode       = paintMode;
            TargetBone      = targetBone;
            Strength        = strength;
            WeightValue     = weightValue;
        }
    }

    // ================================================================
    // 作業軸
    //
    // 作業軸（WorkAxisContext）はモデルの頂点・選択を書き換えない。
    // 回転・拡大縮小・歪みのピボット源なので、どの軸を使うかが 1 呼び出しで
    // 確定するよう、差分ではなく状態の全指定にする。
    // 差分指定にすると「送る前の状態を知らないと結果が予測できない」ものになり、
    // SelectElementsCommand の Toggle と同じ問題を抱える。
    // ================================================================

    /// <summary>
    /// 作業軸の状態を指定した値へ差し替える。実処理は WorkAxisContext。
    ///
    /// 「選択重心へ移動」「ワールド軸へ整列」「リセット」も、呼び出し側で結果の
    /// 値を解決してからこのコマンドに載せる。専用コマンドを増やさず、
    /// 実行前の状態に依存しない形にそろえるため。
    ///
    /// Length は WorkAxisContext.Length が下限（MinLength）でクランプする。
    /// </summary>
    public class SetWorkAxisCommand : PanelCommand
    {
        [PLParam(TextKey = "WorkAxisOrigin",
                 Description = "軸の原点（ワールド座標）。\"x,y,z\"", Required = true)]
        public Vector3 Origin { get; }

        /// <summary>
        /// 軸の回転（度）。WorkAxisContext.EulerAngles と同じく Quaternion.Euler で解釈する。
        /// </summary>
        [PLParam(TextKey = "WorkAxisEulerAngles",
                 Description = "軸の回転（度）。\"x,y,z\"", Required = true)]
        public Vector3 EulerAngles { get; }

        [PLParam(TextKey = "WorkAxisLength",
                 Description = "軸長（ワールド単位）。下限は WorkAxisContext.MinLength でクランプされる")]
        public float Length { get; }

        [PLParam(TextKey = "WorkAxisVisible", Description = "ギズモを表示するか")]
        public bool IsVisible { get; }

        public SetWorkAxisCommand(
            int modelIndex,
            Vector3 origin, Vector3 eulerAngles,
            float length   = Poly_Ling.Context.WorkAxisContext.DefaultLength,
            bool isVisible = true)
            : base(modelIndex)
        {
            Origin      = origin;
            EulerAngles = eulerAngles;
            Length      = length;
            IsVisible   = isVisible;
        }
    }

    /// <summary>
    /// 作業軸ライブラリの登録名を呼び出して作業軸へ入れる。
    /// 表示フラグは変えない（WorkAxisEntry.ApplyTo と同じ）。
    /// </summary>
    public class RecallWorkAxisCommand : PanelCommand
    {
        [PLParam(TextKey = "WorkAxisName",
                 Description = "作業軸ライブラリの登録名", Required = true)]
        public string Name { get; }

        public RecallWorkAxisCommand(int modelIndex, string name)
            : base(modelIndex)
        {
            Name = name ?? "";
        }
    }
}
