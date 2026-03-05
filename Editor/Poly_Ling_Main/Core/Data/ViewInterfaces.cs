// ViewInterfaces.cs
// パネルが参照する統一インタフェース
// 実装A: ProjectSummary/ModelSummary/MeshSummary（リモート用スナップショット）
// 実装B: LiveProjectView/LiveModelView/LiveMeshView（ローカル用、現物を直接読む）

using System.Collections.Generic;
using UnityEngine;

namespace Poly_Ling.Data
{
    // ================================================================
    // メッシュ単体ビュー
    // ================================================================

    public interface IBonePoseView
    {
        bool HasPose { get; }
        bool IsActive { get; }
        Vector3 RestPosition { get; }
        Vector3 RestRotationEuler { get; }
        Vector3 RestScale { get; }
        int LayerCount { get; }
        Vector3 ResultPosition { get; }
        Vector3 ResultRotationEuler { get; }
        Vector3 BindPosePosition { get; }
        Vector3 BindPoseRotationEuler { get; }
        Vector3 BindPoseScale { get; }
    }

    public interface IMeshView
    {
        // ID
        int MasterIndex { get; }
        string Name { get; }
        MeshType Type { get; }

        // ジオメトリ
        int VertexCount { get; }
        int FaceCount { get; }
        int TriCount { get; }
        int QuadCount { get; }
        int NgonCount { get; }

        // 属性
        bool IsVisible { get; }
        bool IsLocked { get; }
        bool IsFolding { get; }

        // 階層
        int Depth { get; }
        int HierarchyParentIndex { get; }

        // ミラー
        int MirrorType { get; }
        bool IsBakedMirror { get; }
        bool IsMirrorSide { get; }
        bool IsRealSide { get; }
        bool HasBakedMirrorChild { get; }

        // ボーン
        int BoneIndex { get; }
        IBonePoseView BonePose { get; }

        // モーフ
        bool IsMorph { get; }
        int MorphParentIndex { get; }
        string MorphName { get; }
        bool ExcludeFromExport { get; }

        // 表示用（計算プロパティ）
        string InfoString { get; }
        string MirrorTypeDisplay { get; }
        bool HasMirrorIcon { get; }
    }

    // ================================================================
    // モデルビュー
    // ================================================================

    public interface IModelView
    {
        string Name { get; }
        string FilePath { get; }
        bool IsDirty { get; }

        int DrawableCount { get; }
        int BoneCount { get; }
        int MorphCount { get; }
        int TotalMeshCount { get; }

        IReadOnlyList<IMeshView> DrawableList { get; }
        IReadOnlyList<IMeshView> BoneList { get; }
        IReadOnlyList<IMeshView> MorphList { get; }

        int[] SelectedDrawableIndices { get; }
        int[] SelectedBoneIndices { get; }
        int[] SelectedMorphIndices { get; }
    }

    // ================================================================
    // プロジェクトビュー
    // ================================================================

    public interface IProjectView
    {
        string ProjectName { get; }
        int CurrentModelIndex { get; }
        IModelView CurrentModel { get; }
    }
}
