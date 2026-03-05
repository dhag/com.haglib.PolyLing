// PanelCommand.cs
// パネルからメインルーチンへの操作要求
// すべてプリミティブ値で構成される

namespace Poly_Ling.Data
{
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

    /// <summary>BonePose RestPose値変更（FloatField/Slider操作）</summary>
    public class SetBonePoseRestValueCommand : PanelCommand
    {
        public enum Field { PositionX, PositionY, PositionZ, RotationX, RotationY, RotationZ, ScaleX, ScaleY, ScaleZ }
        public int[] MasterIndices { get; }
        public Field TargetField { get; }
        public float Value { get; }
        public SetBonePoseRestValueCommand(int modelIndex, int[] masterIndices, Field field, float value)
            : base(modelIndex) { MasterIndices = masterIndices; TargetField = field; Value = value; }
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
}
