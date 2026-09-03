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
        /// <summary>
        /// 衝突判定の単位。
        /// VertexSegment … 頂点のビフォー→アフター線分とコライダー三角形の交差（既定）
        /// FacePair      … ビフォー面を三角形に割り、面どうしの接触時刻を求める
        /// </summary>
        public Poly_Ling.UI.ShrinkCollisionMode CollisionMode { get; }
        /// <summary>
        /// 面方式の反復上限。頂点方式では使わない。
        /// 停止値は単調減少するので必ず収束するが、上限で打ち切ることもできる。
        /// </summary>
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
        public int       TargetMasterIndex  { get; }
        /// <summary>ターゲットのローカル座標での変形後位置。ターゲットの頂点数と同数であること。</summary>
        public Vector3[] LocalPositions     { get; }
        /// <summary>結果の法線を再計算するか</summary>
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
        public int   SlotIndex { get; }
        /// <summary>設定する基本色（RGBA）</summary>
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
        public Poly_Ling.Tools.SpringBoneTest.SpringBoneTestRigParams Params { get; }

        /// <summary>生成前に同じ接頭辞の既存生成物を消すか。</summary>
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
}
