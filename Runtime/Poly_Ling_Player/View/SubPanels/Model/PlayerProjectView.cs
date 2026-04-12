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
            return model == null ? null : new PlayerModelView(model, index);
        }
    }

    /// <summary>
    /// ModelContext を IModelView として公開するラッパー。
    /// プロパティアクセス時にスナップショットを遅延構築する。
    /// </summary>
    public class PlayerModelView : IModelView
    {
        private readonly ModelContext _model;

        // 遅延構築
        private IReadOnlyList<IMeshView> _drawableList;
        private IReadOnlyList<IMeshView> _boneList;
        private IReadOnlyList<IMeshView> _morphList;
        private int[] _selectedDrawableIndices;
        private int[] _selectedBoneIndices;
        private int[] _selectedMorphIndices;

        public PlayerModelView(ModelContext model, int modelIndex)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }

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

        public int[] SelectedDrawableIndices
            => _selectedDrawableIndices ??= _model.SelectedDrawableMeshIndices.ToArray();
        public int[] SelectedBoneIndices
            => _selectedBoneIndices     ??= _model.SelectedBoneIndices.ToArray();
        public int[] SelectedMorphIndices
            => _selectedMorphIndices    ??= _model.SelectedMorphIndices.ToArray();

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
