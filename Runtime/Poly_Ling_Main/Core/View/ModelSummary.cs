// ModelSummary.cs
// モデル全体の要約情報（IModelViewを実装）

using System.Collections.Generic;
using Poly_Ling.Context;
using Poly_Ling.Data;
using Poly_Ling.View;

namespace Poly_Ling.View
{
    public class ModelSummary : IModelView
    {
        // 基本情報
        public string Name { get; }
        public string FilePath { get; }
        public bool IsDirty { get; }

        // カウント
        public int DrawableCount { get; }
        public int BoneCount { get; }
        public int MorphCount { get; }
        public int TotalMeshCount { get; }

        // その他
        public int MeshSelectionSetCount { get; }
        public ModelContext.SelectionCategory ActiveCategory { get; }

        // メッシュリスト（IMeshViewとして統一）
        public IReadOnlyList<IMeshView> DrawableList { get; }
        public IReadOnlyList<IMeshView> BoneList { get; }
        public IReadOnlyList<IMeshView> MorphList { get; }
        public int[] SelectedDrawableIndices { get; }
        public int[] SelectedBoneIndices { get; }
        public int[] SelectedMorphIndices { get; }

        // フルコンストラクタ
        public ModelSummary(
            string name, string filePath, bool isDirty,
            int drawableCount, int boneCount, int morphCount, int totalMeshCount,
            int meshSelectionSetCount, ModelContext.SelectionCategory activeCategory,
            IReadOnlyList<IMeshView> drawableList = null,
            IReadOnlyList<IMeshView> boneList = null,
            IReadOnlyList<IMeshView> morphList = null,
            int[] selectedDrawableIndices = null,
            int[] selectedBoneIndices = null,
            int[] selectedMorphIndices = null)
        {
            Name = name ?? "Untitled";
            FilePath = filePath;
            IsDirty = isDirty;
            DrawableCount = drawableCount;
            BoneCount = boneCount;
            MorphCount = morphCount;
            TotalMeshCount = totalMeshCount;
            MeshSelectionSetCount = meshSelectionSetCount;
            ActiveCategory = activeCategory;

            DrawableList = drawableList ?? _empty;
            BoneList = boneList ?? _empty;
            MorphList = morphList ?? _empty;
            SelectedDrawableIndices = selectedDrawableIndices ?? _emptyIndices;
            SelectedBoneIndices = selectedBoneIndices ?? _emptyIndices;
            SelectedMorphIndices = selectedMorphIndices ?? _emptyIndices;
        }

        private static readonly IMeshView[] _empty = new IMeshView[0];
        private static readonly int[] _emptyIndices = new int[0];
    }
}
