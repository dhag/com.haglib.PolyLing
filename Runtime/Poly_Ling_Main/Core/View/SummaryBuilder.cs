// SummaryBuilder.cs
// ProjectSummary / ModelSummary / MeshSummary を生成する唯一の入り口
// メインルーチンからのみ呼ばれる。パネルからは呼ばない。
// リモート用。ローカル用はLiveProjectViewを使う。

using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.View
{
    public static class SummaryBuilder
    {
        // ================================================================
        // 唯一の公開エントリポイント
        // ================================================================

        /// <summary>
        /// ProjectSummaryを生成する。
        /// CurrentModelのみフル（MeshSummary付き）、他は軽量スタブ。
        /// </summary>
        public static ProjectSummary Build(ProjectContext project)
        {
            if (project == null)
                return new ProjectSummary("Untitled", -1, null, new List<ModelSummary>());

            var models = new List<ModelSummary>(project.ModelCount);
            for (int i = 0; i < project.ModelCount; i++)
            {
                var model = project.GetModel(i);
                if (model == null)
                {
                    models.Add(BuildLightweight("(null)", null));
                    continue;
                }

                if (i == project.CurrentModelIndex)
                    models.Add(BuildFull(model));
                else
                    models.Add(BuildLightweight(model));
            }

            ModelSummary currentModel = null;
            if (project.CurrentModelIndex >= 0 && project.CurrentModelIndex < models.Count)
                currentModel = models[project.CurrentModelIndex];

            return new ProjectSummary(
                project.Name,
                project.CurrentModelIndex,
                currentModel,
                models);
        }

        // ================================================================
        // ModelSummary生成（内部）
        // ================================================================

        private static ModelSummary BuildFull(ModelContext model)
        {
            var indices = model.TypedIndices;

            var drawableList = BuildMeshList(model.DrawableMeshes, model);
            var boneList = BuildMeshList(model.Bones, model);
            var morphList = BuildMeshList(model.Morphs, model);

            return new ModelSummary(
                model.Name, model.FilePath, model.IsDirty,
                indices.DrawableCount, indices.BoneCount, model.Morphs?.Count ?? 0,
                model.MeshContextCount,
                model.MeshSelectionSetCount, model.ActiveCategory,
                drawableList, boneList, morphList,
                model.SelectedMeshIndices.ToArray(),
                model.SelectedBoneIndices.ToArray(),
                model.SelectedMorphIndices.ToArray());
        }

        private static ModelSummary BuildLightweight(ModelContext model)
        {
            var indices = model.TypedIndices;
            return new ModelSummary(
                model.Name, model.FilePath, model.IsDirty,
                indices.DrawableCount, indices.BoneCount, model.Morphs?.Count ?? 0,
                model.MeshContextCount,
                model.MeshSelectionSetCount, model.ActiveCategory);
        }

        private static ModelSummary BuildLightweight(string name, string filePath)
        {
            return new ModelSummary(
                name, filePath, false,
                0, 0, 0, 0, 0,
                ModelContext.SelectionCategory.Mesh);
        }

        // ================================================================
        // IMeshViewリスト生成（内部）
        // ================================================================

        private static IMeshView[] BuildMeshList(
            IReadOnlyList<TypedMeshEntry> entries, ModelContext model)
        {
            if (entries == null || entries.Count == 0) return System.Array.Empty<IMeshView>();

            var list = new IMeshView[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                list[i] = MeshSummary.FromContext(entry.Context, model, entry.MasterIndex);
            }
            return list;
        }
    }
}
