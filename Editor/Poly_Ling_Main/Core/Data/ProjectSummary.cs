// ProjectSummary.cs
// プロジェクト全体の要約情報（IProjectViewを実装）

using System.Collections.Generic;

namespace Poly_Ling.Data
{
    public class ProjectSummary : IProjectView
    {
        public string ProjectName { get; }
        public int CurrentModelIndex { get; }

        /// <summary>IProjectView.CurrentModel（IModelViewとして返す）</summary>
        public IModelView CurrentModel { get; }

        /// <summary>全モデルのリスト</summary>
        public IReadOnlyList<ModelSummary> Models { get; }

        public ProjectSummary(
            string projectName, int currentModelIndex,
            ModelSummary currentModel,
            IReadOnlyList<ModelSummary> models)
        {
            ProjectName = projectName ?? "Untitled";
            CurrentModelIndex = currentModelIndex;
            CurrentModel = currentModel;
            Models = models ?? new List<ModelSummary>();
        }
    }
}
