// PanelCommand.cs
// パネルからメインルーチンへの操作要求
// すべてプリミティブ値で構成される

using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;
using Poly_Ling.Tools;

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

    /// <summary>選択辞書をCSVフォルダへエクスポート（ダイアログはメインエディタ側）</summary>
    public class ExportPartsSetsCsvCommand : PanelCommand
    {
        public ExportPartsSetsCsvCommand(int modelIndex) : base(modelIndex) { }
    }

    /// <summary>CSVファイルから選択辞書をインポート（ダイアログはメインエディタ側）</summary>
    public class ImportPartsSetCsvCommand : PanelCommand
    {
        public ImportPartsSetCsvCommand(int modelIndex) : base(modelIndex) { }
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

        public ConvertMeshFilterToSkinnedCommand(
            int modelIndex,
            bool swapAxisForRotated = false,
            bool setAxisForIdentity = false)
            : base(modelIndex)
        {
            SwapAxisForRotated = swapAxisForRotated;
            SetAxisForIdentity = setAxisForIdentity;
        }
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

    // ================================================================
    // メッシュブレンド
    // ================================================================

    /// <summary>
    /// 選択メッシュ（ターゲット）にソースメッシュをブレンドして適用する。
    /// バックアップ作成 + Undo 記録付き。
    /// </summary>
    public class ApplyBlendCommand : PanelCommand
    {
        /// <summary>ターゲット MeshContext の MasterIndex 配列</summary>
        public int[]  TargetMasterIndices  { get; }
        /// <summary>ソース MeshContext の MasterIndex</summary>
        public int    SourceMasterIndex    { get; }
        /// <summary>ブレンドウェイト [0, 1]</summary>
        public float  BlendWeight          { get; }
        /// <summary>適用後に法線を再計算するか</summary>
        public bool   RecalculateNormals   { get; }
        /// <summary>選択頂点のみに適用するか</summary>
        public bool   SelectedVerticesOnly { get; }
        /// <summary>頂点IDで照合するか</summary>
        public bool   MatchByVertexId      { get; }

        public ApplyBlendCommand(
            int modelIndex,
            int[] targetMasterIndices, int sourceMasterIndex,
            float blendWeight,
            bool recalculateNormals   = true,
            bool selectedVerticesOnly = false,
            bool matchByVertexId      = false)
            : base(modelIndex)
        {
            TargetMasterIndices  = targetMasterIndices;
            SourceMasterIndex    = sourceMasterIndex;
            BlendWeight          = blendWeight;
            RecalculateNormals   = recalculateNormals;
            SelectedVerticesOnly = selectedVerticesOnly;
            MatchByVertexId      = matchByVertexId;
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

    /// <summary>選択メッシュのミラーを実体化した新メッシュをモデルに追加する（Bake Mirror）</summary>
    public class BakeMirrorCommand : PanelCommand
    {
        public int   SourceMasterIndex { get; }
        public int   MirrorAxis        { get; }
        public float Threshold         { get; }
        public bool  FlipU             { get; }
        public BakeMirrorCommand(int modelIndex, int sourceMasterIndex, int mirrorAxis, float threshold, bool flipU)
            : base(modelIndex)
        {
            SourceMasterIndex = sourceMasterIndex;
            MirrorAxis        = mirrorAxis;
            Threshold         = threshold;
            FlipU             = flipU;
        }
    }

    /// <summary>Bake済みメッシュの編集結果を元メッシュに書き戻した新メッシュをモデルに追加する（Write Back）</summary>
    public class WriteBackMirrorCommand : PanelCommand
    {
        public int           EditedMasterIndex   { get; }
        public int           OriginalMasterIndex { get; }
        public Poly_Ling.Tools.WriteBackMode    WriteBackMode { get; }
        public Poly_Ling.Tools.MirrorBakeResult BakeResult    { get; }
        public WriteBackMirrorCommand(int modelIndex, int editedMasterIndex, int originalMasterIndex,
            Poly_Ling.Tools.WriteBackMode writeBackMode, Poly_Ling.Tools.MirrorBakeResult bakeResult)
            : base(modelIndex)
        {
            EditedMasterIndex   = editedMasterIndex;
            OriginalMasterIndex = originalMasterIndex;
            WriteBackMode       = writeBackMode;
            BakeResult          = bakeResult;
        }
    }

    /// <summary>ソースとWriteBack結果をブレンドした新メッシュをモデルに追加する（Blend）</summary>
    public class BlendMirrorCommand : PanelCommand
    {
        public int   SourceMasterIndex    { get; }
        public int   WriteBackMasterIndex { get; }
        public float BlendWeight          { get; }
        public BlendMirrorCommand(int modelIndex, int sourceMasterIndex, int writeBackMasterIndex, float blendWeight)
            : base(modelIndex)
        {
            SourceMasterIndex    = sourceMasterIndex;
            WriteBackMasterIndex = writeBackMasterIndex;
            BlendWeight          = blendWeight;
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
