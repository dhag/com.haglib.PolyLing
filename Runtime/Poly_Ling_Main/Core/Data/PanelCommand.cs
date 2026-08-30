// PanelCommand.cs
// パネルからメインルーチンへの操作要求
// すべてプリミティブ値で構成される

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;
using Poly_Ling.Ops;

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
        public MeshCategory Category { get; }
        public int[] Indices { get; }
        public SelectMeshCommand(int modelIndex, MeshCategory category, int[] indices)
            : base(modelIndex) { Category = category; Indices = indices; }
    }

    // ================================================================
    // 属性変更
    // ================================================================

    public class ToggleVisibilityCommand : PanelCommand
    {
        public int MasterIndex { get; }
        public ToggleVisibilityCommand(int modelIndex, int masterIndex)
            : base(modelIndex) { MasterIndex = masterIndex; }
    }

    public class SetBatchVisibilityCommand : PanelCommand
    {
        public int[] MasterIndices { get; }
        public bool Visible { get; }
        public SetBatchVisibilityCommand(int modelIndex, int[] masterIndices, bool visible)
            : base(modelIndex) { MasterIndices = masterIndices; Visible = visible; }
    }

    public class ToggleLockCommand : PanelCommand
    {
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
        public int[] MasterIndices { get; }
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
        public int[] MasterIndices { get; }
        public bool  Enabled       { get; }
        public SetMirrorEnabledCommand(int modelIndex, int[] masterIndices, bool enabled)
            : base(modelIndex) { MasterIndices = masterIndices; Enabled = enabled; }
    }

    /// <summary>
    /// 複数オブジェクトのミラータイプを一括設定する。
    /// 値は CycleMirrorTypeCommand と同じ 0..3 の範囲。
    /// </summary>
    public class SetBatchMirrorTypeCommand : PanelCommand
    {
        public int[] MasterIndices { get; }
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
        public int[]   MasterIndices { get; }
        public ulong[] ObjectIds     { get; }
        public string  EditorName    { get; }
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
        public int[] MasterIndices { get; }
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
        public string[]  Names     { get; }
        public Vector3[] Positions { get; }

        /// <summary>行ごとの回転(°)。null = 回転を適用しない。</summary>
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
        public float WedgeLength { get; }

        /// <summary>コンテナの名前。空なら既定名。</summary>
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
        public int ContainerMasterIndex { get; }

        /// <summary>自動検出に使うコンテナ名。空なら既定名。</summary>
        public string ContainerName { get; }

        public ApplyObjectPoseWedgesCommand(int modelIndex, int containerMasterIndex, string containerName)
            : base(modelIndex) { ContainerMasterIndex = containerMasterIndex; ContainerName = containerName; }
    }

    /// <summary>
    /// PreserveNormals フラグ（頂点法線を自動再計算しない）を設定するコマンド。
    /// </summary>
    public class SetPreserveNormalsCommand : PanelCommand
    {
        public int[] MasterIndices { get; }
        public bool  Value         { get; }
        public SetPreserveNormalsCommand(int modelIndex, int[] masterIndices, bool value)
            : base(modelIndex) { MasterIndices = masterIndices; Value = value; }
    }

    /// <summary>ミラー分岐ルートのフラグを設定するコマンド。</summary>
    public class SetMirrorBranchRootCommand : PanelCommand
    {
        public int[] MasterIndices { get; }
        public bool  Value         { get; }
        public SetMirrorBranchRootCommand(int modelIndex, int[] masterIndices, bool value)
            : base(modelIndex) { MasterIndices = masterIndices; Value = value; }
    }

    public class CycleMirrorTypeCommand : PanelCommand
    {
        public int MasterIndex { get; }
        public CycleMirrorTypeCommand(int modelIndex, int masterIndex)
            : base(modelIndex) { MasterIndex = masterIndex; }
    }

    public class RenameMeshCommand : PanelCommand
    {
        public int MasterIndex { get; }
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
        public int[]    MasterIndices { get; }
        public string[] NewNames      { get; }
        public RenameMeshesCommand(int modelIndex, int[] masterIndices, string[] newNames)
            : base(modelIndex) { MasterIndices = masterIndices; NewNames = newNames; }
    }

    /// <summary>
    /// メッシュの TreeView 折りたたみ状態変更
    /// </summary>
    public class SetMeshFoldingCommand : PanelCommand
    {
        public int MasterIndex { get; }
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
        public int[] MasterIndices { get; }
        public DeleteMeshesCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    public class DuplicateMeshesCommand : PanelCommand
    {
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

        public MeshCategory Category { get; }
        public ReorderEntry[] Entries { get; }

        public ReorderMeshesCommand(int modelIndex, MeshCategory category, ReorderEntry[] entries)
            : base(modelIndex) { Category = category; Entries = entries; }
    }

    // ================================================================
    // BonePose
    // ================================================================

    public class InitBonePoseCommand : PanelCommand
    {
        public int[] MasterIndices { get; }
        public InitBonePoseCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    public class SetBonePoseActiveCommand : PanelCommand
    {
        public int[] MasterIndices { get; }
        public bool Active { get; }
        public SetBonePoseActiveCommand(int modelIndex, int[] masterIndices, bool active)
            : base(modelIndex) { MasterIndices = masterIndices; Active = active; }
    }

    public class ResetBonePoseLayersCommand : PanelCommand
    {
        public int[] MasterIndices { get; }
        public ResetBonePoseLayersCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    public class BakePoseToBindPoseCommand : PanelCommand
    {
        public int[] MasterIndices { get; }
        public BakePoseToBindPoseCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    /// <summary>スライダードラッグ開始: Undoスナップショット取得</summary>
    public class BeginBonePoseSliderDragCommand : PanelCommand
    {
        public int[] MasterIndices { get; }
        public BeginBonePoseSliderDragCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    /// <summary>スライダードラッグ終了: Undo記録コミット</summary>
    public class EndBonePoseSliderDragCommand : PanelCommand
    {
        public string Description { get; }
        public EndBonePoseSliderDragCommand(int modelIndex, string description)
            : base(modelIndex) { Description = description; }
    }

    // ================================================================
    // モーフ
    // ================================================================

    public class ConvertMeshToMorphCommand : PanelCommand
    {
        public int SourceIndex { get; }
        public int ParentIndex { get; }
        public string MorphName { get; }
        public int Panel { get; }
        public ConvertMeshToMorphCommand(int modelIndex, int sourceIndex, int parentIndex, string morphName, int panel)
            : base(modelIndex) { SourceIndex = sourceIndex; ParentIndex = parentIndex; MorphName = morphName; Panel = panel; }
    }

    public class ConvertMorphToMeshCommand : PanelCommand
    {
        public int[] MasterIndices { get; }
        public ConvertMorphToMeshCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    public class CreateMorphSetCommand : PanelCommand
    {
        public string SetName { get; }
        public int MorphType { get; }
        public int[] MorphIndices { get; }
        public CreateMorphSetCommand(int modelIndex, string setName, int morphType, int[] morphIndices)
            : base(modelIndex) { SetName = setName; MorphType = morphType; MorphIndices = morphIndices; }
    }

    // ================================================================
    // モーフプレビュー
    // ================================================================

    public class StartMorphPreviewCommand : PanelCommand
    {
        public int[] MorphIndices { get; }
        public StartMorphPreviewCommand(int modelIndex, int[] morphIndices)
            : base(modelIndex) { MorphIndices = morphIndices; }
    }

    public class ApplyMorphPreviewCommand : PanelCommand
    {
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
        public string SetName { get; }
        public SavePartsSetCommand(int modelIndex, string setName)
            : base(modelIndex) { SetName = setName; }
    }

    /// <summary>選択辞書エントリを現在の選択に適用（置き換え）</summary>
    public class LoadPartsSetCommand : PanelCommand
    {
        public int SetIndex { get; }
        public LoadPartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリを現在の選択に追加（Union）</summary>
    public class AddPartsSetCommand : PanelCommand
    {
        public int SetIndex { get; }
        public AddPartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>現在の選択から辞書エントリを除外（Subtract）</summary>
    public class SubtractPartsSetCommand : PanelCommand
    {
        public int SetIndex { get; }
        public SubtractPartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリを削除</summary>
    public class DeletePartsSetCommand : PanelCommand
    {
        public int SetIndex { get; }
        public DeletePartsSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリの名前を変更</summary>
    public class RenamePartsSetCommand : PanelCommand
    {
        public int SetIndex { get; }
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
        public string SetName { get; }
        public SaveNormalExcludeSetCommand(int modelIndex, string setName)
            : base(modelIndex) { SetName = setName; }
    }

    /// <summary>除外セットを現在の選択に適用（置き換え）</summary>
    public class LoadNormalExcludeSetCommand : PanelCommand
    {
        public int SetIndex { get; }
        public LoadNormalExcludeSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>除外セットを削除</summary>
    public class DeleteNormalExcludeSetCommand : PanelCommand
    {
        public int SetIndex { get; }
        public DeleteNormalExcludeSetCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>除外セットの名前を変更</summary>
    public class RenameNormalExcludeSetCommand : PanelCommand
    {
        public int SetIndex { get; }
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

        public Op    Operation  { get; }
        /// <summary>RecalcByAngle のスムージング角（度）</summary>
        public float AngleDeg   { get; }
        /// <summary>Smooth の強度（0-1）</summary>
        public float Strength   { get; }
        /// <summary>AlignToAxis / FlattenOnAxis の軸（0=X, 1=Y, 2=Z）</summary>
        public int   Axis       { get; }
        /// <summary>AlignToAxis の符号（true で負方向）</summary>
        public bool  Negative   { get; }
        /// <summary>Sphereize / PointToTarget の座標</summary>
        public Vector3 Target   { get; }
        /// <summary>Sphereize の中心に選択の重心を使うか</summary>
        public bool  UseSelectionCenter { get; }
        /// <summary>PointToTarget で 1 本のベクトルに揃えるか</summary>
        public bool  AlignVectors { get; }
        /// <summary>平均時の重み付け方式</summary>
        public NormalWeightMode WeightMode { get; }
        /// <summary>
        /// MirrorFlattenSeamX の中央判定しきい値。
        /// |Vertex.Position.x| がこの値以下の頂点を中央（合わせ目）とみなす。
        /// </summary>
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

        public RepairMode Mode { get; }
        public RepairVertexIdsCommand(int modelIndex, RepairMode mode)
            : base(modelIndex) { Mode = mode; }
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
        public int   TargetModelIndex  { get; }
        public int[] SourceMeshIndices { get; }
        public int[] TargetMeshIndices { get; }

        public VertexMatchMode MatchMode { get; }
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
        public string CloneNameBase { get; }
        public CreateBlendCloneCommand(int sourceModelIndex, string cloneNameBase)
            : base(sourceModelIndex) { CloneNameBase = cloneNameBase; }
    }

    /// <summary>ブレンドをクローンモデルに適用する</summary>
    public class ApplyModelBlendCommand : PanelCommand
    {
        /// <summary>クローン先モデルインデックス</summary>
        public int CloneModelIndex { get; }
        public float[] Weights     { get; }
        public bool[]  MeshEnabled { get; }
        public bool    RecalcNormals { get; }
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
        public int CloneModelIndex { get; }
        public float[] Weights     { get; }
        public bool[]  MeshEnabled { get; }
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
        public int TargetModelIndex { get; }
        public SwitchModelCommand(int targetModelIndex)
            : base(targetModelIndex) { TargetModelIndex = targetModelIndex; }
    }

    /// <summary>モデルの名前を変更する</summary>
    public class RenameModelCommand : PanelCommand
    {
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
        public MeshCategory Category { get; }
        public string SetName { get; }
        public string[] MeshNames { get; }
        public SaveSelectionDictionaryCommand(int modelIndex, MeshCategory category, string setName, string[] meshNames)
            : base(modelIndex) { Category = category; SetName = setName; MeshNames = meshNames; }
    }

    /// <summary>選択辞書エントリを選択に適用（置き換えまたは追加）</summary>
    public class ApplySelectionDictionaryCommand : PanelCommand
    {
        public int SetIndex { get; }
        public bool AddToExisting { get; }
        public ApplySelectionDictionaryCommand(int modelIndex, int setIndex, bool addToExisting = false)
            : base(modelIndex) { SetIndex = setIndex; AddToExisting = addToExisting; }
    }

    /// <summary>選択辞書エントリを削除</summary>
    public class DeleteSelectionDictionaryCommand : PanelCommand
    {
        public int SetIndex { get; }
        public DeleteSelectionDictionaryCommand(int modelIndex, int setIndex)
            : base(modelIndex) { SetIndex = setIndex; }
    }

    /// <summary>選択辞書エントリの名前を変更</summary>
    public class RenameSelectionDictionaryCommand : PanelCommand
    {
        public int SetIndex { get; }
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
        public int[] MasterIndices { get; }
        public ProjectionType Projection { get; }
        public float Scale { get; }
        public float OffsetU { get; }
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
        public int MasterIndex { get; }
        public float UvScale { get; }
        public float DepthScale { get; }
        public Vector3 CameraPosition { get; }
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
        public int SourceMasterIndex { get; }
        public int TargetMasterIndex { get; }
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
        public int[] MasterIndices { get; }
        public Field TargetField { get; }
        public float Value { get; }
        public SetBoneTransformValueCommand(int modelIndex, int[] masterIndices, Field field, float value)
            : base(modelIndex) { MasterIndices = masterIndices; TargetField = field; Value = value; }
    }

    /// <summary>BoneTransform スライダードラッグ開始（Undo スナップショット取得）</summary>
    public class BeginBoneTransformSliderDragCommand : PanelCommand
    {
        public int[] MasterIndices { get; }
        /// <summary>ボーン編集の確定モード（A/B）。パネルが送信時に刻む。</summary>
        public BoneMoveMode Mode { get; set; } = BoneMoveMode.BoneOnlyRebind;
        /// <summary>
        /// 「原点だけ移動」中か。true のとき、対象 MeshFilter の見た目を固定したまま
        /// 原点(BoneTransform)だけを動かすよう受信側が自頂点を再ローカル化する。
        /// パネルが送信時に刻む。
        /// </summary>
        public bool OriginOnly { get; set; } = false;
        public BeginBoneTransformSliderDragCommand(int modelIndex, int[] masterIndices)
            : base(modelIndex) { MasterIndices = masterIndices; }
    }

    /// <summary>BoneTransform スライダードラッグ終了（Undo 記録コミット）</summary>
    public class EndBoneTransformSliderDragCommand : PanelCommand
    {
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
        public int[] MasterIndices { get; }
        /// <summary>基準オブジェクトの MasterIndex</summary>
        public int BaseMasterIndex { get; }
        /// <summary>true: 新規メッシュオブジェクトに結果を格納する</summary>
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
        public int AMasterIndex { get; }
        /// <summary>右辺オブジェクトの MasterIndex。差では削る側。</summary>
        public int BMasterIndex { get; }
        /// <summary>演算の種類</summary>
        public Poly_Ling.Ops.BooleanOpKind Op { get; }
        /// <summary>true: 新規メッシュオブジェクトに結果を格納する</summary>
        public bool CreateNewMesh { get; }
        /// <summary>true: 演算後に B を削除する</summary>
        public bool DeleteSourceB { get; }
        /// <summary>true: 演算後に同一位置頂点をマージする</summary>
        public bool MergeVertices { get; }
        /// <summary>同一位置頂点マージのしきい値</summary>
        public float MergeThreshold { get; }
        /// <summary>平面の同一判定の許容量（pb_CSG の epsilon）</summary>
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
    /// 頂点・辺・面をインデックス指定で選択する。
    /// null のフィールドは対応する選択を変更しない。
    /// Additive = false の場合、設定前に既存の選択全体をクリアする。
    /// 辺は [v1a, v2a, v1b, v2b, ...] のフラット配列で指定する。
    /// </summary>
    public class SelectElementsCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex</summary>
        public int   MasterIndex   { get; }
        /// <summary>選択する頂点インデックス配列。null = 変更しない</summary>
        public int[] VertexIndices { get; }
        /// <summary>選択する辺のフラット配列 [v1a, v2a, v1b, v2b, ...]。null = 変更しない</summary>
        public int[] EdgePairs     { get; }
        /// <summary>選択する面インデックス配列。null = 変更しない</summary>
        public int[] FaceIndices   { get; }
        /// <summary>false = 既存選択をクリアしてから設定、true = 既存選択に追加</summary>
        public bool  Additive      { get; }

        public SelectElementsCommand(
            int modelIndex, int masterIndex,
            int[] vertexIndices, int[] edgePairs, int[] faceIndices,
            bool additive = false)
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
            VertexIndices = vertexIndices;
            EdgePairs     = edgePairs;
            FaceIndices   = faceIndices;
            Additive      = additive;
        }
    }

    // ================================================================
    // 頂点移動
    // ================================================================

    /// <summary>
    /// 現在の選択頂点をデルタ値で移動する。Undo記録付き。
    /// CoordinateSpace.World の場合、Delta をモデルローカル空間に変換してから適用する。
    /// </summary>
    public class MoveSelectedVerticesCommand : PanelCommand
    {
        public enum CoordSpace { Local, World }

        /// <summary>対象 MeshContext の MasterIndex</summary>
        public int        MasterIndex      { get; }
        /// <summary>移動量</summary>
        public Vector3    Delta            { get; }
        /// <summary>Delta の座標空間</summary>
        public CoordSpace Space            { get; }
        /// <summary>移動後に法線を再計算するか</summary>
        public bool       RecalcNormals    { get; }

        public MoveSelectedVerticesCommand(
            int modelIndex, int masterIndex,
            Vector3 delta, CoordSpace space,
            bool recalcNormals = false)
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
            Delta         = delta;
            Space         = space;
            RecalcNormals = recalcNormals;
        }
    }

    // ================================================================
    // ピボット移動
    // ================================================================

    /// <summary>
    /// ピボット（原点）をデルタ値で移動する。Undo記録付き。
    /// 全頂点を -Delta 方向に移動し、BoneTransform.Position を +Delta 方向に移動する。
    /// CoordinateSpace.World の場合、Delta をモデルローカル空間に変換してから頂点に適用する。
    /// </summary>
    public class MovePivotCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex</summary>
        public int        MasterIndex { get; }
        /// <summary>ピボットの移動量</summary>
        public Vector3    Delta       { get; }
        /// <summary>Delta の座標空間</summary>
        public MoveSelectedVerticesCommand.CoordSpace Space { get; }

        public MovePivotCommand(
            int modelIndex, int masterIndex,
            Vector3 delta, MoveSelectedVerticesCommand.CoordSpace space)
            : base(modelIndex)
        {
            MasterIndex = masterIndex;
            Delta       = delta;
            Space       = space;
        }
    }

    // ================================================================
    // スカルプトストローク
    // ================================================================

    /// <summary>
    /// スカルプトブラシを一連のローカル空間座標に沿って適用する。Undo記録付き。
    /// BrushCenters は対象メッシュのローカル座標系で指定すること。
    /// </summary>
    public class SculptStrokeCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex</summary>
        public int          MasterIndex   { get; }
        /// <summary>ブラシ中心の列（ローカル空間）</summary>
        public Vector3[]    BrushCenters  { get; }
        /// <summary>スカルプトモード</summary>
        public SculptMode   Mode          { get; }
        /// <summary>ブラシ半径（ローカル空間単位）</summary>
        public float        BrushRadius   { get; }
        /// <summary>強度（0〜1）</summary>
        public float        Strength      { get; }
        /// <summary>反転フラグ</summary>
        public bool         Invert        { get; }
        /// <summary>フォールオフ種別</summary>
        public FalloffType  Falloff       { get; }
        /// <summary>ストローク終了後に法線を再計算するか</summary>
        public bool         RecalcNormals { get; }

        public SculptStrokeCommand(
            int modelIndex, int masterIndex,
            Vector3[] brushCenters,
            SculptMode mode, float brushRadius, float strength,
            bool invert = false,
            FalloffType falloff = FalloffType.Gaussian,
            bool recalcNormals = true)
            : base(modelIndex)
        {
            MasterIndex   = masterIndex;
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
        /// <summary>対象 MeshContext の MasterIndex</summary>
        public int                MasterIndex       { get; }
        /// <summary>選択モード</summary>
        public AdvancedSelectMode Mode              { get; }

        // ── Seed ──────────────────────────────────────────────────
        /// <summary>頂点起点インデックス（不使用時 -1）</summary>
        public int                SeedVertexIndex   { get; }
        /// <summary>辺起点 V1（不使用時 -1）</summary>
        public int                SeedEdgeV1        { get; }
        /// <summary>辺起点 V2（不使用時 -1）</summary>
        public int                SeedEdgeV2        { get; }
        /// <summary>面起点インデックス（不使用時 -1）</summary>
        public int                SeedFaceIndex     { get; }
        /// <summary>ShortestPath 終点インデックス（他モードでは無視）</summary>
        public int                EndVertexIndex    { get; }

        // ── 出力フラグ ──────────────────────────────────────────────
        public bool               SelectVertices    { get; }
        public bool               SelectEdges       { get; }
        public bool               SelectFaces       { get; }

        /// <summary>false = 既存選択をクリアしてから選択</summary>
        public bool               Additive          { get; }

        /// <summary>EdgeLoop モードの方向一致閾値（cos値、デフォルト 0.5）</summary>
        public float              EdgeLoopThreshold { get; }

        public AdvancedSelectCommand(
            int modelIndex, int masterIndex,
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
            float edgeLoopThreshold = 0.5f)
            : base(modelIndex)
        {
            MasterIndex       = masterIndex;
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
        public bool SwapAxisForRotated  { get; }
        /// <summary>回転なしボーンを X軸上向き・Y軸横向きに設定する</summary>
        public bool SetAxisForIdentity  { get; }

        /// <summary>
        /// ミラー分岐ルート配下の「ミラー設定漏れ」を許容し、
        /// ミラー側メッシュを実体側から生成して実体化する。既定は true。
        /// </summary>
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
        public int[] MasterIndices { get; }

        /// <summary>階層の扱い。既定はルート直下へ移す。</summary>
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
        public int[] MasterIndices { get; }

        /// <summary>バインド先ボーンの MeshContextList 索引。</summary>
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
        public int                          TargetBoneMaster { get; }
        public Poly_Ling.UI.SkinWeightPaintMode PaintMode    { get; }
        public float                        WeightValue      { get; }
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
        public int[] BoneMasters { get; }

        /// <summary>長さ 4。各スロットのウェイト値。</summary>
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
        public BlendSourceSpec[] Sources { get; }
        /// <summary>書き込み先 MeshContext の MasterIndex（ModelIndex のモデル内）</summary>
        public int    DestMasterIndex      { get; }
        /// <summary>宛先を複製して、そちらへ書き込むか</summary>
        public bool   CreateNewObject      { get; }
        /// <summary>適用後に法線を再計算するか</summary>
        public bool   RecalculateNormals   { get; }
        /// <summary>選択頂点のみに適用するか（対象は宛先の選択頂点）</summary>
        public bool   SelectedVerticesOnly { get; }
        /// <summary>宛先頂点とソース頂点の対応付け方式</summary>
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
        public int   BeforeMasterIndex     { get; }
        /// <summary>アフター（目標形状）MeshContext の MasterIndex</summary>
        public int   AfterMasterIndex      { get; }
        /// <summary>衝突対象 MeshContext の MasterIndex 配列</summary>
        public int[] ColliderMasterIndices { get; }
        /// <summary>シュリンク量 [0, 1]</summary>
        public float Slider                { get; }
        /// <summary>コライダー面から手前に残す距離（ワールド単位）</summary>
        public float SurfaceOffset         { get; }
        /// <summary>
        /// true : 進行方向に対して表を向いた面のみを衝突とみなす（裏面は素通り）
        /// false: 表裏を問わず衝突とみなす（既定）
        /// </summary>
        public bool  FrontFaceOnly         { get; }
        /// <summary>適用後に法線を再計算するか</summary>
        public bool  RecalculateNormals    { get; }
        /// <summary>
        /// true : 結果を新規オブジェクトとして追加し、ビフォー／アフターを非表示にする（既定）
        /// false: ビフォーを上書きし、元形状を &lt;名前&gt;_backup として追加する
        /// </summary>
        public bool  CreateNewObject       { get; }

        public ApplyShrinkCommand(
            int modelIndex,
            int beforeMasterIndex, int afterMasterIndex,
            int[] colliderMasterIndices,
            float slider,
            float surfaceOffset      = 0f,
            bool  frontFaceOnly      = false,
            bool  recalculateNormals = true,
            bool  createNewObject    = true)
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
        public int   BeforeMasterIndex   { get; }
        /// <summary>アフター（外側の面）MeshContext の MasterIndex</summary>
        public int   AfterMasterIndex    { get; }
        /// <summary>法線を差し替える MeshContext の MasterIndex 配列</summary>
        public int[] TargetMasterIndices { get; }
        /// <summary>適用率 [0, 1]。1 未満なら元の法線と Slerp する。</summary>
        public float Strength            { get; }
        /// <summary>
        /// true : 三角形内を球面補間する
        /// false: 三角形内を線形補間する（既定）
        /// </summary>
        public bool  Spherical           { get; }
        /// <summary>
        /// true : どのプリズムにも入らない頂点を最も近いプリズムへ寄せる
        /// false: どのプリズムにも入らない頂点は変更しない（既定）
        /// </summary>
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
        public int   BeforeMasterIndex         { get; }
        /// <summary>アフター（変形後の対応点）MeshContext の MasterIndex</summary>
        public int   AfterMasterIndex          { get; }
        /// <summary>変形させる MeshContext の MasterIndex</summary>
        public int   TargetMasterIndex         { get; }
        /// <summary>平滑化係数。K 行列の対角に加算される。0 で厳密補間。</summary>
        public float Lambda                    { get; }
        /// <summary>
        /// true : ビフォー／アフターの選択頂点（両者の和集合）だけを制御点にする
        /// false: 全頂点を制御点にする（既定）
        /// </summary>
        public bool  SelectedControlPointsOnly { get; }
        /// <summary>結果の法線を再計算するか</summary>
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
        public int       MasterIndex   { get; }
        /// <summary>変更対象の頂点インデックス配列</summary>
        public int[]     VertexIndices { get; }
        /// <summary>変更対象の UV サブインデックス配列（VertexIndices と同長）</summary>
        public int[]     UVIndices     { get; }
        /// <summary>変更前 UV 座標配列</summary>
        public Vector2[] BeforeUVs     { get; }
        /// <summary>変更後 UV 座標配列</summary>
        public Vector2[] AfterUVs      { get; }
        /// <summary>操作名（Undo スタックの説明文用）</summary>
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
        public int  MasterIndex            { get; }
        /// <summary>バウンダリをシームに含めるか</summary>
        public bool IncludeBoundaryAsSeam  { get; }
        /// <summary>最大反復数</summary>
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
        public int SlotIndex { get; }
        public RemoveMaterialSlotCommand(int modelIndex, int slotIndex)
            : base(modelIndex) { SlotIndex = slotIndex; }
    }

    /// <summary>選択面に指定マテリアルスロットを適用する</summary>
    public class ApplyMaterialToFacesCommand : PanelCommand
    {
        /// <summary>対象 MeshContext の MasterIndex</summary>
        public int   MasterIndex  { get; }
        /// <summary>適用するマテリアルスロット番号</summary>
        public int   MaterialSlot { get; }
        /// <summary>適用対象の面インデックス配列</summary>
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
        public int    BaseModelIndex  { get; }
        /// <summary>モーフモデルのインデックス（プロジェクト内）</summary>
        public int    MorphModelIndex { get; }
        /// <summary>生成するモーフの名前</summary>
        public string MorphName       { get; }
        /// <summary>パネル番号（0=眉 / 1=目 / 2=口 / 3=その他）</summary>
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
        public int   SourceMasterIndex { get; }
        public float TargetRatio       { get; }
        public int   MaxPasses         { get; }
        public float NormalAngleDeg    { get; }
        public float HardAngleDeg      { get; }
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
        public int   SourceMasterIndex { get; }

        /// <summary>ミラー軸（0:X, 1:Y, 2:Z）。メッシュが MirrorType > 0 のときはメッシュ側の設定が優先される。</summary>
        public int   MirrorAxis        { get; }
        public float Threshold         { get; }
        public bool  FlipU             { get; }

        /// <summary>ミラー平面のオフセット（ローカル座標）</summary>
        public float PlaneOffset { get; }

        /// <summary>境界の決め方</summary>
        public MirrorBoundaryMode BoundaryMode { get; }

        /// <summary>境界頂点をミラー平面へ射影するか</summary>
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
        public int SourceMasterIndex { get; }

        /// <summary>どちら側の編集結果を残すか</summary>
        public Poly_Ling.Tools.WriteBackMode Mode { get; }

        /// <summary>
        /// 実体化前のミラー設定（MirrorType / MirrorAxis / MirrorDistance / MirrorMaterialOffset）へ
        /// 戻すか。ツール内の「一時ミラー」はモデルの恒久設定を変えてはいけないので true にする。
        /// false のときは従来どおり MirrorType = 2（結合）を強制する。
        /// </summary>
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
}
