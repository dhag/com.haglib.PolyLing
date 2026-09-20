// PlayerProjectView.cs
// ProjectContext → IProjectView、ModelContext → IModelView ラッパー
// MeshSummary.FromContext を使いスナップショットを生成する
// Runtime/Poly_Ling_Player/View/ に配置

using System;
using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.Player
{
    /// <summary>
    /// ProjectContext を IProjectView として公開するラッパー。
    /// </summary>
    public class PlayerProjectView : IProjectView
    {
        private readonly ProjectContext _project;

        public PlayerProjectView(ProjectContext project)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
        }

        public string ProjectName       => _project.Name;
        public int    CurrentModelIndex => _project.CurrentModelIndex;
        public IModelView CurrentModel  => GetModelView(_project.CurrentModelIndex);
        public int ModelCount           => _project.ModelCount;

        public IModelView GetModelView(int index)
        {
            var model = _project.GetModel(index);
            return model == null ? null : new PlayerModelView(model, index, _project);
        }

        public VertexTransferPreviewView PreviewVertexTransfer(
            int srcModelIndex, int srcMeshIndex, int dstModelIndex, int dstMeshIndex,
            Poly_Ling.Ops.VertexMatchMode mode)
            => LiveProjectView.ComputeVertexTransferPreview(_project, srcModelIndex, srcMeshIndex, dstModelIndex, dstMeshIndex, mode);

        public IReadOnlyList<string> WorkAxisLibraryNames => LiveProjectView.LibraryNamesOf(_project);
    }

    /// <summary>
    /// ModelContext を IModelView として公開するラッパー。
    /// プロパティアクセス時にスナップショットを遅延構築する。
    /// </summary>
    public class PlayerModelView : IModelView
    {
        private readonly ModelContext _model;
        private readonly ProjectContext _project;

        // 遅延構築
        private IReadOnlyList<IMeshView> _drawableList;
        private IReadOnlyList<IMeshView> _boneList;
        private IReadOnlyList<IMeshView> _morphList;
        private IReadOnlyList<IMeshView> _rigidBodyList;
        private IReadOnlyList<IMeshView> _rigidBodyJointList;
        private int[] _selectedDrawableIndices;
        private int[] _selectedBoneIndices;
        private int[] _selectedMorphIndices;
        private IReadOnlyList<string> _meshSelectionSetNames;

        public PlayerModelView(ModelContext model, int modelIndex, ProjectContext project = null)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _project = project;
        }

        /// <summary>オブジェクトグループの一覧（プロジェクトが無ければ空）。</summary>
        public IReadOnlyList<ObjectGroupView> ObjectGroups
            => _project != null
                ? Poly_Ling.View.LiveProjectView.BuildObjectGroupViews(_project, _model)
                : Array.Empty<ObjectGroupView>();

        public int MaterialCount        => _model.MaterialCount;
        public int CurrentMaterialIndex => _model.CurrentMaterialIndex;
        public MaterialSlotView GetMaterialSlot(int slot)
            => Poly_Ling.View.LiveProjectView.BuildMaterialSlotView(_model, slot);
        public IReadOnlyList<MorphExpressionView> MorphExpressions
            => Poly_Ling.View.LiveProjectView.BuildMorphExpressionViews(_model);

        public int    HumanoidMappingCount => (_model.HumanoidMapping == null || _model.HumanoidMapping.IsEmpty) ? 0 : _model.HumanoidMapping.Count;
        public bool   HasAnySkinWeight     => Poly_Ling.Ops.TPoseConverter.HasAnySkinWeight(_model.MeshContextList);
        public bool   HasTPoseBackup       => _model.TPoseBackup != null;
        public string DiagnoseTPose()      => Poly_Ling.Ops.TPoseConverter.Diagnose(_model.MeshContextList, _model.HumanoidMapping);
        public int HumanoidMissingRequiredCount => Poly_Ling.View.LiveProjectView.HumanoidMissingRequiredOf(_model);
        public AvatarRetargetView AvatarRetarget => Poly_Ling.View.LiveProjectView.BuildAvatarRetargetView(_model);
        public IReadOnlyList<string> SpringBoneColliderGroupNames
            => new List<string>(_model.SpringBoneColliderGroupNames ?? new List<string>());
        public bool HasVrmMeta   => _model.VrmMeta != null;
        public Poly_Ling.Data.VrmMetaData   VrmMetaCopy   => Poly_Ling.Ops.VrmSettingsOps.GetMetaOrNew(_model);
        public bool HasVrmLookAt => _model.VrmLookAt != null;
        public Poly_Ling.Data.VrmLookAtData VrmLookAtCopy => Poly_Ling.Ops.VrmSettingsOps.GetLookAtOrNew(_model);

        public string Name     => _model.Name;
        public string FilePath => _model.FilePath;
        public bool   IsDirty  => _model.IsDirty;

        public int DrawableCount  => _model.DrawableCount;
        public int BoneCount      => _model.BoneCount;
        public int MorphCount     => _model.TypedIndices.GetCount(MeshCategory.Morph);
        public int TotalMeshCount => _model.MeshContextCount;

        public IReadOnlyList<IMeshView> DrawableList
            => _drawableList ??= BuildList(MeshCategory.Drawable);
        public IReadOnlyList<IMeshView> BoneList
            => _boneList     ??= BuildList(MeshCategory.Bone);
        public IReadOnlyList<IMeshView> MorphList
            => _morphList    ??= BuildList(MeshCategory.Morph);
        public IReadOnlyList<IMeshView> RigidBodyList
            => _rigidBodyList ??= BuildList(MeshCategory.RigidBody);
        public IReadOnlyList<IMeshView> RigidBodyJointList
            => _rigidBodyJointList ??= BuildList(MeshCategory.RigidBodyJoint);

        public int[] SelectedDrawableIndices
            => _selectedDrawableIndices ??= _model.SelectedDrawableMeshIndices.ToArray();
        public int[] SelectedBoneIndices
            => _selectedBoneIndices     ??= _model.SelectedBoneIndices.ToArray();
        public int[] SelectedMorphIndices
            => _selectedMorphIndices    ??= _model.SelectedMorphIndices.ToArray();

        /// <summary>選択辞書の名前一覧。並びは MeshSelectionSets と同じ。</summary>
        public IReadOnlyList<string> MeshSelectionSetNames
            => _meshSelectionSetNames ??= BuildSelectionSetNames();

        public int ActiveMeshIndex => _model.ActiveMeshIndex;

        public IMeshView ActiveMesh
        {
            get
            {
                int i  = _model.ActiveMeshIndex;
                var mc = i >= 0 ? _model.GetMeshContext(i) : null;
                return mc != null ? new Poly_Ling.View.LiveMeshView(mc, _model, i) : null;
            }
        }

        public IMeshView GetMesh(int masterIndex)
        {
            var mc = masterIndex >= 0 ? _model.GetMeshContext(masterIndex) : null;
            return mc != null ? new Poly_Ling.View.LiveMeshView(mc, _model, masterIndex) : null;
        }

        private string[] BuildSelectionSetNames()
        {
            var sets = _model.MeshSelectionSets;
            if (sets == null || sets.Count == 0) return Array.Empty<string>();
            var names = new string[sets.Count];
            for (int i = 0; i < sets.Count; i++) names[i] = sets[i]?.Name ?? "";
            return names;
        }

        private IReadOnlyList<IMeshView> BuildList(MeshCategory category)
        {
            var entries = _model.TypedIndices.GetEntries(category);
            var result  = new List<IMeshView>(entries.Count);
            foreach (var e in entries)
                result.Add(MeshSummary.FromContext(e.Context, _model, e.MasterIndex));
            return result;
        }
    }
}
