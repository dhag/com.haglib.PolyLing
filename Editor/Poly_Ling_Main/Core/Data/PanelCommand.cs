// PanelCommand.cs
// パネルからメインルーチンへの操作要求
// すべてプリミティブ値で構成される

using System.Collections.Generic;
using UnityEngine;

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
}
