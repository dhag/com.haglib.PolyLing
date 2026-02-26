// Assets/Editor/Poly_Ling_Main/Core/Data/MeshSelectionSet.cs
// メッシュ選択セット（名前ベース）
// ModelContext内のどのメッシュが選択されているかを名前で保存・復元

using System;
using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Model;

namespace Poly_Ling.Data
{
    /// <summary>
    /// メッシュ選択セット
    /// どのメッシュが選択されているかを名前で保存
    /// </summary>
    [Serializable]
    public class MeshSelectionSet
    {
        /// <summary>セット名</summary>
        public string Name { get; set; } = "MeshSet";

        /// <summary>選択カテゴリ（Mesh/Bone/Morph）</summary>
        public ModelContext.SelectionCategory Category { get; set; } = ModelContext.SelectionCategory.Mesh;

        /// <summary>選択メッシュの名前リスト</summary>
        public List<string> MeshNames { get; set; } = new List<string>();

        // ================================================================
        // コンストラクタ
        // ================================================================

        public MeshSelectionSet() { }

        public MeshSelectionSet(string name)
        {
            Name = name;
        }

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>メッシュ数</summary>
        public int Count => MeshNames.Count;

        /// <summary>空かどうか</summary>
        public bool IsEmpty => MeshNames.Count == 0;

        /// <summary>サマリー文字列（UI表示用）</summary>
        public string Summary => $"{Category} ({MeshNames.Count})";

        // ================================================================
        // ファクトリメソッド
        // ================================================================

        /// <summary>
        /// 現在のModelContext選択状態から作成
        /// </summary>
        public static MeshSelectionSet FromCurrentSelection(
            string name,
            ModelContext model,
            ModelContext.SelectionCategory category)
        {
            if (model == null) return new MeshSelectionSet(name);

            var set = new MeshSelectionSet(name) { Category = category };

            List<int> indices = category switch
            {
                ModelContext.SelectionCategory.Mesh => model.SelectedMeshIndices,
                ModelContext.SelectionCategory.Bone => model.SelectedBoneIndices,
                ModelContext.SelectionCategory.Morph => model.SelectedMorphIndices,
                _ => new List<int>()
            };

            foreach (int idx in indices)
            {
                var ctx = model.GetMeshContext(idx);
                if (ctx != null && !string.IsNullOrEmpty(ctx.Name))
                {
                    if (!set.MeshNames.Contains(ctx.Name))
                        set.MeshNames.Add(ctx.Name);
                }
            }

            return set;
        }

        // ================================================================
        // 復元
        // ================================================================

        /// <summary>
        /// ModelContextに選択を復元（名前一致で全て選択）
        /// </summary>
        public void ApplyTo(ModelContext model)
        {
            if (model == null) return;

            var nameSet = new HashSet<string>(MeshNames);

            switch (Category)
            {
                case ModelContext.SelectionCategory.Mesh:
                    model.ClearMeshSelection();
                    break;
                case ModelContext.SelectionCategory.Bone:
                    model.ClearBoneSelection();
                    break;
                case ModelContext.SelectionCategory.Morph:
                    model.ClearMorphSelection();
                    break;
            }

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var ctx = model.GetMeshContext(i);
                if (ctx == null) continue;
                if (!nameSet.Contains(ctx.Name)) continue;

                switch (Category)
                {
                    case ModelContext.SelectionCategory.Mesh:
                        model.AddToMeshSelection(i);
                        break;
                    case ModelContext.SelectionCategory.Bone:
                        model.AddToBoneSelection(i);
                        break;
                    case ModelContext.SelectionCategory.Morph:
                        model.AddToMorphSelection(i);
                        break;
                }
            }
        }

        /// <summary>
        /// 現在の選択に追加（Union）
        /// </summary>
        public void AddTo(ModelContext model)
        {
            if (model == null) return;

            var nameSet = new HashSet<string>(MeshNames);

            for (int i = 0; i < model.MeshContextCount; i++)
            {
                var ctx = model.GetMeshContext(i);
                if (ctx == null || !nameSet.Contains(ctx.Name)) continue;

                switch (Category)
                {
                    case ModelContext.SelectionCategory.Mesh:
                        model.AddToMeshSelection(i);
                        break;
                    case ModelContext.SelectionCategory.Bone:
                        model.AddToBoneSelection(i);
                        break;
                    case ModelContext.SelectionCategory.Morph:
                        model.AddToMorphSelection(i);
                        break;
                }
            }
        }

        // ================================================================
        // クローン
        // ================================================================

        public MeshSelectionSet Clone()
        {
            return new MeshSelectionSet(Name)
            {
                Category = Category,
                MeshNames = new List<string>(MeshNames)
            };
        }
    }

    // ================================================================
    // シリアライズ用DTO
    // ================================================================

    [Serializable]
    public class MeshSelectionSetDTO
    {
        public string name;
        public string category;
        public List<string> meshNames;

        public static MeshSelectionSetDTO FromMeshSelectionSet(MeshSelectionSet set)
        {
            if (set == null) return null;

            return new MeshSelectionSetDTO
            {
                name = set.Name,
                category = set.Category.ToString(),
                meshNames = new List<string>(set.MeshNames)
            };
        }

        public MeshSelectionSet ToMeshSelectionSet()
        {
            var set = new MeshSelectionSet(name ?? "MeshSet");

            if (!string.IsNullOrEmpty(category) &&
                Enum.TryParse<ModelContext.SelectionCategory>(category, out var cat))
            {
                set.Category = cat;
            }

            if (meshNames != null)
                set.MeshNames = new List<string>(meshNames);

            return set;
        }
    }
}
